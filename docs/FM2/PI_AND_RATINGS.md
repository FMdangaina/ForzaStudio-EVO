# FM2: how the game computes PI and the four ratings (default.xex, 2026-11-15)

Found by disassembling the FM2 `default.xex` (XexTool `-b` image, base 0x82000000). Tools and dumps:
`E:\DEV\fm4dev\ST02\pi\xex` (`xexfp.exe` = XexCars `xexcars.exe` + floating point decoding and float/double
constants on `lfs`/`lfd`; `nncnst.exe` prints `carperfpredcnst.bin`). Nothing here is implemented in the editor.

## Call chain

| address | what |
|---|---|
| `82236250` | garage stats update: builds the car, computes, then `UPDATE %s SET ClassID, PerformanceIndex, SpeedRating, AccelerationRating, BrakingRating, HandlingRating, SimPeakPower, SimPeakAngVel, ...` |
| `82598048` | builds the car from the database (stock rows: `SELECT Id FROM <table> WHERE Ordinal=<car> AND IsStock=1`, 82597180; "Error: DB has no stock part for car" when a required table has none) |
| `8259DC20` | computes everything: sim peaks, the four ratings, then PI (`8259E9A0`) |
| `8259E9A0` | PI = the best of 8 x 3 evaluations (two car parameters scaled by 1, 0.95 ... 0.65 and 1.03 / 1.01 / 1) |
| `8259E660` | one evaluation: 18 measured inputs -> neural network -> PI |
| `8222F5E8` | garage `AspirationTypeId` from the FITTED parts: turbo fitted = 2 (3 / 4 when `Data_Engine.AspirationID_Stock` is 3 / 4), DSC 5, CSC 6, none 1. `AspirationID_Stock` is read nowhere else (no reference to `StockBoost-bar` at all). |
| `825995C8` | `SELECT Id, MaxPerformanceIndex, MaxDisplayPerformanceIndex FROM CarClasses`: number and letter on screen |

## Ratings (8259DC20), each clamped to 3 .. 10

`X(...)` are methods of the car simulation object (vtable offsets); speeds in m/s (53.6448 = 120 mph).

```
SpeedRating        = clamp((topSpeed[0x39C] - 55) * 0.109139219 + 3)
AccelerationRating = clamp((accel[0x3A0] - 3.788) * 0.6448598 + 3)
BrakingRating      = clamp((ln(brake[0x3A8](53.6448)) - ln(13.0697174)) / (ln(31.7919998) - ln(13.0697174)) * 6.9 + 3)
HandlingRating     = clamp((ln(corner[0x3A4](53.6448)) - ln(11.8018341)) / (ln(28.3362694) - ln(11.8018341)) * 6.9 + 3)
```

`824188E0` is the natural logarithm (returns 0 for 1). `SimPeakPower` / `SimPeakAngVel` etc. are the peaks of the
151-point torque / power tables the sim builds (0x97 entries, `825576A8`), `SimPeakAngVel` = rpm x 0.1047197.

## PI (8259E660): a neural network

Network: `GAME:\Media\AI\PerfPrediction\carperfpred.nn` (MLP, 18 inputs, 1 output, hidden layers 33 / 11 / 4,
1054 big-endian doubles of weights with biases, plus learningRate and activationResponse).
Input limits: `carperfpredcnst.bin` (text header + big-endian doubles):

| name | min | max |
|---|---|---|
| Brake (m/s2) | 11.783953 | 46.180218 |
| Accel (m/s2) | 0 | 19.778994 |
| Corner (m/s2) | 11.598859 | 37.561062 |
| TopSpeed (m/s) | 52.007149 | 122.935211 |
| SlipAngle | 0.079242 | 0.222179 |
| MOIY | 0.016399 | 0.102375 |

The 18 inputs, each `(x - min) / (max - min)` clamped to 0 .. 1 (a NaN becomes 0):

1. for v = 50, 100, 150 mph (2 x k x 11.176 m/s): braking at v, cornering at v, the mean slip angle of the two
   axles at that grip (`0x1E4` with 1 and 2): 9 inputs;
2. acceleration at v = 25, 50 ... 175 mph (k x 11.176): 7 inputs;
3. top speed (`0x7C` struct, field 0x94): 1 input;
4. drive type x 0.5: 1 input.

Output: `PI = clamp(net * (1 - m) + m, 0, 1)` with `m` the value passed in by the caller (CarClassModifier path).
The class letter comes from the stored `ClassID`; the number is linear inside `CarClasses`. PI = 1 shows 999
(the `MaxDisplayPerformanceIndex` of classes 5-9).

## Why it matters (PT Cruiser test, docs/ST02 test 10)

FM2 own engine turbo -> natural: garage / race PI 999 = the network saw extreme sim measurements (acceleration or
top speed of the converted engine), not a database error the game reports. The menu PI is the stored
`Data_Car.PerformanceIndex`. Next step: the torque table code of the sim with no turbo fitted.

## Tool: fm2perf (2026-11-15, E:\DEV\fm4dev\ST02\perf, read-only harness)

- `fm2perf engine <gamedb.slt> [carId|all]`: SimPeakTorque / SimPeakPower / SimPeakAngVel / SimPeakTorqueAngVel /
  SimRedlineAngVel of the stock engine, as `82637108` builds the torque table. **310 / 310 FM2 cars match Data_Car**
  (193 natural, 117 turbo / supercharged). Rules: CamValves stock row gives RedlineRPM, TorqueCurveMaxRPM and
  NumRPMEntriesArray (point i at i * MaxRPM / (N - 1)); torque = v[i] * TorqueScale * 0.01 * f12 (when > 0),
  f12 = 1 + sum(stock TorqueScale - 1) of Block/Exhaust/FuelSystem/Ignition/Intake + (RobScale - 1) of the stock
  turbo / supercharger; turbo factor = lerp(previous point's power, PowerMinScale * 7.457 .. PowerMaxScale * 7.457,
  (MinScale - 1 + f12) / f12 .. (MaxScale - 1 + Intercooler.MaxScaleScale - 1 + f12) / f12), then the TorqueDropOff
  rpm lerp on the excess; supercharger factor = lerp(rpm, 0 .. redline, ZeroRPMScale .. RedlineRPMScale, same form),
  same drop-off; factors add (1 + (t - 1) + (s - 1)). Cut-off = first point <= 0 above 400 rpm. Units: torque N.m / 100,
  power W / 100 (7.457 per hp), rad/s. Same scheme as Aiden-Piercee's FM4 calculator (car.py torque_table).
- `fm2perf net <PerfPrediction dir>` / `pi <dir> <18 raw inputs>`: the PI network, exact (`827D0BC8`: net = sum(w x) - w_bias,
  sigmoid 1 / (1 + exp(-net / activationResponse)) on every layer; 18/33/11/4/1, response 1). All inputs 0 -> PI 0.00003,
  all 1 -> 0.996. The 18 sim measurements (braking, cornering, slip angle, acceleration at speeds, top speed) still need
  the vehicle sim (vtable 0x39C/0x3A0/0x3A4/0x3A8/0x1E4): next step. CarClasses number shown with a guessed lower end (100).

## Display and the sim measurements (2026-11-15)

- `82599460` PI -> shown number: PI clamped 0..1, class = first CarClasses row with PI <= MaxPerformanceIndex, number
  linear inside the class (class D from 100 to 400; a NaN gives 100). So "D 999" = the class letter from the stored
  ClassID with a recomputed PI of about 0.86 or more (class U range): the sim measurements make the car look very fast.
- The sim object is `CCarDynamics` (RTTI; interface `ICarDynamics`, 268 slots; the PI code uses the vtable at
  `82115574`, this-offset 0x4E0): +0x39C top speed `8264A5B8`, +0x3A0 acceleration `82643CF8`, +0x3AC acceleration at
  a speed `82643DF0`, +0x3A4 cornering(v) `82643698`, +0x3A8 braking(v) `82643870`, +0x1E4 slip angle `8263E0D8`,
  +0x38C torque table `8264A198`, +0x390 `82643428`. Braking(v) is a quasi-static model: 6 iterations of load transfer
  (9.80665, CG height, wheelbase fields), aero at v (`82641910` / `82641990`), tyre friction vs load (`826510F0`).
- ICarDynamics test case: PT Cruiser 414 with ANY natural engine (its own made natural, or the Civic VTi engine 54)
  shows D 999 in the garage, also on a new profile; with its stock turbo it is normal. Car-level data are in the natural
  cars' range except the body size (Height 1.601 m, a minivan), which the user rules out.
- `xexfp bigvt <min slots> [slots]` lists every RTTI vtable with at least that many code pointers.

## fm2perf progress (2026-11-15, later)

- Runtime tuning globals: object at `82A10000`, defaults written by `826516A0` (via `82651C88`, `82599778`):
  +0xF8 braking scale 1.0; drag speed table x 102.8 .. 116.2 m/s, y 1.0 .. 1.5 (+0x10C..+0x118, so 1.0 at PI speeds);
  final drive slider range 0.5 .. 6 (+0x1A0/+0x1A4), gear ratio slider range 0.3 .. 8 (+0x1A8/+0x1AC).
- `fm2perf speed`: top speed (8264A5B8) = min((GameTorqueScale x SimPeakPower / (body + front wing + rear wing drag))^(1/3),
  (redline + TorqueCurveMaxRPM) / 2 rad/s / top gear / final drive x driven tyre radius); body drag =
  BodyAeroLongitudinalDrag x 0.01 x 9.80665 x 2.2239485e-4 x product(stock DragScale) x GameDragScale (clamp 0.5..1.5);
  wing drag = lerp(Drag0, Drag1, DefaultTuneSlider) x same K. **SpeedRating 309 / 310** (Murcielago 05 gear-limited, 4.5% off: tyre radius helper not decoded).
- `fm2perf grip <db> <mode>`: braking / cornering (82643870 / 82643698) with candidate tyre rules. Best so far
  (load in kg, peak x FrictionScale lerped MinLoadCurve..MaxLoadCurve, x TorqueFree*FrictionScale, x TireFricWidth/Scale lerp):
  median -2.3% braking, -3.7% cornering, wide spread: NOT exact. Blocker: the runtime tyre object (W+0x3B0; table x at
  T+0x24/0x28, y at T+0x58/0x1F0, scales T+0xC/0x10) is built somewhere not yet found. **Solved, see next section.**

## Braking and cornering: exact (2026-11-15, night)

`fm2perf grip <db> exact` (= mode 940317): **BrakingRating 308/308, HandlingRating 308/308** (all FM2 cars with a rating
inside 3..10; max error 0.04% braking, 0.1% cornering). PT Cruiser 414: braking 14.49, cornering 12.883 m/s2, both exact.

Runtime tyre object (found):
- Two tyre objects embedded in CCarDynamics: front `B+0x20D0` (wheels 0/1), rear `B+0x2CC8` (wheels 2/3), size 0xBF8.
  Filled at the end of the wheel setup `82639810` by `82651190(T, spec, W+0x34C)`; spec = the car's tyre struct
  (front car+0xDF4, rear car+0x19F8).
- `82651190` copies spec+0..0x2C (T+0xC = TorqueFreeLatFrictionScale, T+0x10 = TorqueFreeLongFrictionScaleBrake, T+0x14/0x18 =
  TorqueFreeLongFrictionScaleAccel0/1), then builds the tables with `82650F10(table, multicurve, f1 = Combo FrictionScale x
  widthScale, f2 = PeakSASRScale x pressure curve)`: longitudinal table T+0x384, lateral T+0x24. widthScale = lerp(width mm,
  TireFricWidth0..1 -> TireFricScale0..1).
- `82650F10`: x0/x1 = MinLoadCurve/MaxLoadCurve x 0.0980665 (kg -> the sim's load unit), per curve j: peak_j = max_k(FrictionScale_j x
  v_k) x f1 at +0x34 + j*0x198 (slip of the peak at +0x30). So mu(load) = lerp(load kg, MinLoad..MaxLoad, peak_0..peak_1).

Exact rules (all confirmed by the 308/308 match):
- Load = kg (load in N / g) against MinLoadCurve..MaxLoadCurve; mu = lerp of the two curve peaks (above), x TireMod_FrictionScale
  (front for wheels 0/1, rear for 2/3).
- **TorqueFree scale is NOT in the load-transfer loop.** The loop uses mu without it; the final sum is multiplied by T+0xC
  (cornering) or by `82643530` = GameFrictionScaleBraking (List_UpgradeBrakes, stock) x T+0x10 x 1.0 (braking).
  Putting TorqueFree inside the loop gives an apparent CG height of 1/1.434615 = 0.697 x the real one (the old error).
- CG height h = CMHeight + DefRideHeight (List_SpringDamperPhysics of the stock List_UpgradeSpringDamper row); braking uses the mean
  of front/rear, cornering hF (front) and hR (rear). Not ModelFrontStockRideHeight.
- Track = ModelFront/RearTrackOuter - tyre width (m).
- Aero at 53.6448 m/s: body downforce (Data_Car + stock RearBumper) x K x v^2; wings = List_AeroPhysics of the stock FrontBumper
  and RearWing rows, lerp(Downforce0/1, Drag0/1, DefaultTuneSlider) x K x v^2; each wing's downforce stays on its own axle
  (balance curve DFTorqueScaleTwo = 1 at the default slider). K = 0.01 x 9.80665 x 2.2239485e-4 x 100 per (m/s)^2.
- Drag in braking: body drag x product(stock DragScale) x GameDragScale (clamp 0.5..1.5) + front wing + rear wing drag.
- Braking = drag/m + brakeScale x (kF NF + kR NR)/m, 6 load-transfer iterations; cornering = T+0xC x sum(k_i L_i)/m, 6 iterations.
- Mass = List_UpgradeWeight.Mass + stock MassDiff (Ordinal tables). wF = CMBackFront.

## Acceleration, top speed and PI (2026-11-15, night)

`fm2perf grip <db> exact [car|all]` now also does acceleration and the full PI.
- **AccelerationRating 294/296** (max error 0.22%), **SpeedRating 310/310**.
  - The torque table is sampled at i*step, but `82557700` looks it up as if xMax = n*step spanned n-1 intervals:
    torque used = table at omega*(n-1)/n (game quirk, needed for the exact match).
  - Gears: the first forward gear with v < redline(rad/s) x R / (fd x ratio). B+0xDC8 = GameTorqueScale (not an efficiency).
  - Driven radius = first driven wheel (B+0xD50): FRONT for FWD and AWD, rear for RWD (this also fixed the Murcielago top speed).
  - Traction scale c = TorqueFreeLongFrictionScaleAccel1 inside the loop, the transfer uses F/c (same rule as braking).
- **PI** (`8259E660`, checked against Data_Car.PerformanceIndex): 294/310 within 0.001, 298/310 within 0.005.
  - Before measuring, PI sets the CVT flag B+0xC80 = 1 (slot 0x274, restored after): acceleration inputs use the CVT branch,
    wheel torque = R x wPeakPower / v x T(wPeakPower) x GameTorqueScale (no gears).
  - Input order per speed (50/100/150 mph): braking (normalised), mean slip angle of wheels 1 and 2 at the cornering outer loads
    (RAW, not normalised), cornering (normalised); then accel at 25..175 mph (7), then MOIY, then drive type (B+0xD4C) x 0.5.
  - Input 16 is NOT top speed: slot 0x7C copies the inertia struct at B+0x120, field Iyy, normalised with the MOIY constants.
    Fit: MOIY = 1200 / (m x (BlockDimX^2 + BlockDimZ^2)) (box yaw inertia, sim mass unit kg/100, inverted).
  - Slip angle = lerp by load of the peak-slip of the two lateral curves (first max index x MaxSlip(rad) / (N-1)) x PeakSASRScale
    x StartPressureAffectPeakSASR curve at the start tyre pressure. Pressure not decoded (setup slider x PhysicsSettings.ini
    TuneTirePressure 15..55); 30 psi fits best. Residual PI error 0.0004 average (about 1 shown point in class D).
- PT Cruiser 414: stock PI calc 0.1635 vs db 0.1642. With a natural engine (bisect backup DB) the clean model gives PI 0.016
  (D about 118), NOT 0.86. No single input set to 0 or 1 reaches 0.86 either. So the in-game 999 comes from a runtime value
  this model does not reproduce with the natural engine (open).

## PT Cruiser 999: what the code says (2026-11-15, late)

- `82599458` (number on screen): any PI above 0.72 (class S max) shows 999, not only above 0.86. NaN gives class D and
  100 on hardware; on Xenia `fctiwz(NaN)` saturates (vminsd with INT_MAX), so a NaN gives 400 in class D, never 999.
- `8259DC20` also recomputes the class from the same PI (`825993E8`: stored class kept only for R4..R1, ids 6..9) and
  stores it at stats+0x70. So "D 999" means the letter and the number do NOT come from the same value: the letter is a
  stored ClassID (Data_Car / StockGarage / profile garage) and the number a PI above 0.72.
- Callers of `82599458`: `82298998` reads the column `PerformanceIndex` of a database row; `822C8B2C` a cached float
  (`824B6CC8`); `8225D054` a stats upload (x1000). `82236250` writes ClassID, PerformanceIndex, ratings, sim peaks back
  with `UPDATE %s SET ...` into the profile's garage table.
- `8259E9A0` (best of): getter/setter slots 0x244/0x248 = B+0xDC8 GameTorqueScale (x 1, 0.95 .. 0.65), slots 0x24C/0x250 =
  B+0xFAC braking scale (stock, then 1.03 / 1.01 / 1.0 while above the caller's limit). The max is kept. Now in fm2perf
  (torque part; brake part not modelled); it changes no car: PI 294/310 within 0.001, 310/310 within 0.005 (max 0.0017).
- Network sensitivity on the natural PT (inputs clamped 0..1 except the raw slip, which never helps): only two input
  groups can lift PI above 0.72: cornering at 50 mph (input 2 = 1 gives 0.82) or an acceleration curve with input 9
  (25 mph) = 1, i.e. about 20 m/s2, above the traction limit. Infinite torque with the traction limit gives only 0.48.
  So the 999 needs a runtime value far outside physics, not just more or less power.
- DB finding: the bisect DB (`ST02\pi\bisect\gamedb_before_civic54_on_414.slt`, edited by hand) keeps the OLD StockGarage
  row 414: AspirationID_Stock = 2 (turbo) and Exhaust = 168000, while Data_Engine 168 says natural (1) with
  StockBoost-bar 0.889 (no other natural engine has boost > 0). The Civic engine 54 test had the same stale row
  (Exhaust 168000 belongs to engine 168). The editor rebuilds StockGarage with the official query (`Fm2StockGarage.Write`),
  hand edits do not. Next: in-game test with only the StockGarage row rebuilt.
- Test written 2026-11-15 in FM2_Extracted_DEV (from stock): Data_Engine 168 AspirationID_Stock 1, turbo + intercooler
  row 168000 IsStock 0 (ManufacturerID 161 / 240, as the bisect DB), StockGarage 414 AspirationID_Stock 1. Backup of the
  stock DEV DB: `E:\DEV\fm4dev\ST02\pi\bisect\test_sg\gamedb_DEV_stock_before.slt`. Result in game 2026-11-16: STILL D 999, so StockGarage is NOT the cause. Next: variant A (no turbo/intercooler rows on engine 168).
- Test variant A written 2026-11-16 in FM2_Extracted_DEV: same as above + all List_UpgradeEngineTurbo / Intercooler rows of
  engine 168 deleted (3 + 3; no DSC/CSC rows). Previous DEV state saved as `ST02\pi\bisect\test_sg\gamedb_DEV_after_test_sg.slt`. Result 2026-11-16: STILL D 999. Turbo rows are NOT the cause; variant A alone does not fix test 10. Next: study the engine build with no fitted turbo (8264A198 / 82649BD8) before any new test.

## Engine with no turbo and the garage write (2026-11-16)

- `8264A198` (torque table, vcall 0x38C): T[i] = base[i] (E+0x424) x (1 + (turbo - 1) + (DSC - 1) + (third block - 1)); each
  induction factor is used only when its flag is set (E+0x704 turbo, E+0x734 DSC, E+0x6EC third). With no turbo the table
  is the base curve x 1: nothing breaks there.
- `82236250` (garage stats update) writes ClassID = the class recomputed from the PI (stats+0x70) and PerformanceIndex
  (stats+0x5C) into the profile garage table: in the garage the letter and the number come from the SAME PI. The stored
  ClassID passed in is kept only for R4..R1.
- The only 999 in the code paths is the CarClasses MaxDisplayPerformanceIndex (PI above 0.72). Other 999 constants
  (`xexfp li16 3E7`) are SQLite / buffer limits.
- The profile (`Xenia Canary\content\<profile>\4D5307EA\00000001\ForzaProfile\PlayerDatabase`) is encrypted or compressed:
  the runtime values the game wrote cannot be read yet.
- Open: which screen shows "D 999" (dealer list reads StockGarage; garage reads the profile table) and what the car's stats
  page shows (power, torque, weight, rating bars) with the natural engine.
- 2026-11-16: dealer shows the stored StockGarage value; ARCADE shows D 999 (Arcade recomputes via `82236250`, the only
  caller of `8259DC20`). PT cannot be bought as a career starter, so the test moved to Talon TSi 462 (engine 216, same
  structure: turbo + intercooler L1 stock). DEV = stock + Talon natural (Data_Engine 216 asp 1, turbo/intercooler 216000
  not stock, StockGarage 462 asp 1); copy `ST02\pi\bisect\test_sg\gamedb_DEV_talon_natural.slt`. Model: stock 0.2231
  (db 0.2238), natural 0.0822 = about D 193.
- RESULT 2026-11-16 (Talon natural, screenshots by the user): showroom D 354, 157 kW, 290 N.m, bars 4.9/3.4/3.5/3.8 (the
  stored StockGarage values, not recomputed); CAREER garage D 194, 117 kW, 217 N.m, bars 4.2/3.0/3.5/3.8; career race grid
  D 194; ARCADE race grid D 999. fm2perf predicted all career values: PI 0.0822 (D 194), speed rating 4.169, accel 3.233 m/s2
  -> 2.64 clamped 3.0, `engine`: 2.172 (217 N.m) @ 314.16, 1172.4 (117 kW) @ 628.32. The model is right; only ARCADE is wrong.
- Arcade has no garage row of its own (NewProfile_Arcade_Garage = CarId only; the arcade list joins StockGarage). Suspect:
  the arcade car takes the STORED Data_Car / StockGarage performance values (SimPeakPower, SimPeakAngVel ... still the
  turbo ones). Next test: write the natural values into Data_Car + StockGarage 462 and look at the arcade grid.
- Test written 2026-11-16: Talon natural + Data_Car/StockGarage 462 stored values = model natural values (PI 0.082161,
  class 0, speed 4.1687, accel 3, peaks 1172.37915 @ 628.3182, 2.172365 @ 314.1591, redline 733.0379, SimTopSpeed 65.7085).
  Copy `ST02\pi\bisect\test_sg\gamedb_DEV_talon_natural_values.slt`. Result: pending.
- RESULT 2026-11-16 (stored values test): showroom D 194, ARCADE grid D 193. SOLVED: Arcade (and the dealer) show the
  STORED Data_Car / StockGarage performance values; only the career garage recomputes them (`82236250`). The 999 came from
  the stored turbo values left in place after the engine became natural. 193 vs 194: Arcade shows the stored PI 0.082161,
  the career garage its own runtime value (about 0.0822), inside the accepted fm2perf error.
- Fix for the editor: when an FM2 car's engine or aspiration changes, recompute PerformanceIndex, ClassID,
  Speed/Acceleration/Braking/Handling ratings and the Sim* peaks (fm2perf logic) and write them to Data_Car and StockGarage.

## In the editor: ForzaStudio\Fm2Perf.cs (FM2 ONLY, 2026-11-16)

- fm2perf "grip exact" ported to the app (no search branches). Duplicate Car with an engine or gearbox of its own, FM2 only:
  `Fm2Perf.Update` writes Data_Car PerformanceIndex, ClassID (R4..R1 kept), SpeedRating, AccelerationRating,
  SimPeakTorque / TorqueAngVel / Power / AngVel, SimRedlineAngVel; SimTopSpeed = the source's x (computed top speed ratio).
  Braking / handling and the Sim times stay the source's. StockGarage copies them (Fm2StockGarage.Write). The network is read
  from the game's Media\AI\PerfPrediction. FM3 / FM4 keep CarPerformance.Update until their own calculations are studied.
- Check (harness `E:\DEV\fm4dev\ST02\perf\fm2perfapp.exe <db> <PerfPrediction dir> [car|all]`, compiles a copy of the
  app's file): 310 stock cars PI 294 within 0.001, 310 within 0.005 (max 0.00173), speed / accel / peaks 310/310; Talon
  natural PI 0.082161 (D 193), the value shown in Arcade. In-app Duplicate test and in-game check: pending.
- In-app + in-game test 2026-11-16 (Talon TSi 462 natural, as a DLC pack, Id 659): first run showroom D 354, garage D 193,
  Arcade D 999. Cause: a pack copy runs an inner CarDuplicate on a temp stage that has no AI\PerfPrediction, so the PI was
  not computed (ratings and peaks were) and the stored PI stayed the turbo one. Fix: `CarDuplicate.PerfMedia` (the inner
  copy reads the game's network). Pack rebuilt: PerformanceIndex 0.0823; in game showroom, garage and Arcade all D 193.
  CLOSED for FM2.
- Rule (user, from the tests): when the stored values and the game's own computed values disagree, FM2 shows D 999. The
  stored performance values are always written as a whole set, never a part.

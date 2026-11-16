# FM3: how the game computes PI and the five ratings (default.xex, started 2026-11-16)

Implemented in the editor as `ForzaStudio\Fm3Perf.cs` (2026-11-16, section "In the editor" at the end). FM3's own code:
never reuse FM2's (`docs/FM2/PI_AND_RATINGS.md`).

Image: `E:\DEV\fm4dev\ST02\perf\fm3\xex\fm3_base.exe` (XexTool `-b` of `FM3_Extracted\default.xex`, base 0x82000000).
Tool: `xexfp.exe` in the same folder (`func`, `afunc`, `asum`, `strrefs`, `bigvt` ...). Dumps `f<address>.txt` there.

## RESUME HERE

1. Done, all in the harness `E:\DEV\fm4dev\ST02\perf\fm3\fm3perf.exe <gamedb.slt> [car|all] [100 = one line per car]
   [name=value options]` on the 415 stock cars: **Braking 415/415, Handling 415/415, engine peaks 415/415, redline 415/415,
   Speed 414/414, Acceleration 388/396, Launch 412/415, PI 393/415 within 0.001 (about 1 shown point), 414/415 within
   0.005, max 0.0068** (sections below). `fm3grip.exe` is the earlier grip-only harness.
2. Done 2026-11-16: app code `ForzaStudio\Fm3Perf.cs` (FM3 only), called from CarDuplicate; built into `bin\Debug`; the
   compiled app gives the same counts as the harness (`fm3perfapp.exe`), `Fm3Perf.Update` checked in memory
   (`fm3updtest.exe`). **Next: a duplicate with an engine of its own in FM3 DEV from the app, then the in-game check**
   (one step at a time; Xenia closed before any DEV write). Not committed.
3. Open, small: (a) wheel inertia 1.76 is a FIT (the game's own value not found; List_TireCompound.MomentInertia is not
   loaded by the game); (b) cars with adjustable wings where the best lap is a slider variant are up to 0.007 low
   (Diablo GTR, Viper ACR, Top Secret Silvia, Viper CC, Enzo); (c) the acceleration residual (mean -0.08%, max 0.56%).
4. FM4 has `Media\Physics\PI.xml` too: the same kind of lap is likely. Study FM4's own xex before any FM4 code.

## Call chain

| address | what |
|---|---|
| `82284378` | garage stats update: builds the car, computes, then `UPDATE %s SET ClassID, PerformanceIndex, SpeedRating, AccelerationRating, LaunchRating, BrakingRating, HandlingRating, SimPeakPower, ...` (string `8200E560`) |
| `826BAB88` | builds the car from the database (1534 lines, 184 calls) |
| `826C5488` | computes everything: sim peaks, the five ratings, PI, class |
| `826BCCB0` | PI = a simulated lap on the virtual track of `MediaPhysicsPI.xml` (object at out+0x534, built by `826BD230`; the modifier in f1) |
| `826B1538` | class id from the PI (object at out+0x538) |
| `82527B90` | natural logarithm (used as in FM2's rating formulas) |

## Ratings (826C5488), each clamped to 3 .. 10

```
out+0x60 (Speed)  = clamp((top[vtable 0x3E8] - 50) * 0.107812494 + 3)
out+0x64          = clamp((ln(A) - ln 2.6) / (ln 15.34 - ln 2.6) * 6.9 + 3)     A = 826C5398: 826C5180(40, 80, 0xD)
out+0x68          = clamp((ln(B) - ln 4.2) / (ln 17.15 - ln 4.2) * 6.9 + 3)     B = 826C5410: 826C5180(20, 40, 0xD)
out+0x6C          = clamp((ln(max(C, 0.01)) - ln 9.9) / (ln 30.45 - ln 9.9) * 6.9 + 3)   C = 826C52A8: 826C4F20(50, 120, 8)
out+0x70          = clamp((ln(max(D, 0.01)) - ln 9.3) / (ln 27.37 - ln 9.3) * 6.9 + 3)   D = 826C5320: 826C5050(50, 120, 8)
out+0x5C          = PI (826BCCB0), out+0x74 = class (826B1538)
```

All five confirmed on the 415 stock cars: 0x60 Speed, 0x64 Acceleration (40-80 mph), 0x68 Launch (20-40 mph),
0x6C Braking, 0x70 Handling.

Each measurement is a MEAN over n speeds evenly spaced from lo to hi mph (x 0.44704), of a sim method called as
(v, 1, 1): braking `826C4F20` = slot 0x3F4 `8272E6B0`, 8 speeds 50..120; cornering `826C5050` = slot 0x3EC `8272E4B0`,
8 speeds 50..120; acceleration / launch `826C5180` = slot 0x3F8 `8272EBF0`, 13 speeds 40..80 / 20..40.
The sim object is `CCarDynamics` (interface `ICarDynamics`, 293 slots, vtable `820D9C94`, this-offset 0x960).

## Braking and cornering: exact (2026-11-16)

`fm3grip`: **BrakingRating 415/415, HandlingRating 415/415** (rating within 0.01; measurement error 0 on 411 cars, 0.11%
at most on the four "IE" cars 1208 / 1209 / 1212 / 1213).

The quasi-static model has the same structure as FM2's (6 load-transfer iterations, `8272E6B0` / `8272E4B0`; tyre lookup
`821F85A0`; tyre tables built by `82736738` from `827368E8`), with FM3's tables and these FM3 rules:

- Stock rows by key: car (`Ordinal`), body (`CarBodyId` of the stock `List_UpgradeCarBody` row), engine (`EngineID` of the
  stock `List_UpgradeEngine` row), drivetrain (`DrivetrainID` of the stock `List_UpgradeDrivetrain` row).
- Mass = `List_UpgradeCarBodyWeight.Mass` + MassDiff of every stock row (= Data_Car.CurbWeight x 100);
  wF = `CMBackFront` + WeightDistDiff of every stock row (= Data_Car.WeightDistribution on 412 cars).
- Wheelbase and tracks from `Data_CarBody` (Id = CarBodyID); track = TrackOuter - tyre width; widths from
  `List_UpgradeCarBodyTireWidthFront` / `Rear`.
- CG height = CMHeight + mean of the stock front / rear `DefRideHeight` (cornering: per axle), as in FM2.
- Tyre: mu(load kg) = lerp between the two curve peaks (MinLoadCurve..MaxLoadCurve) x width scale x brand FrictionScale.
- **Per-wheel friction scale = `List_UpgradeCarBodyChassisStiffness.FrontFrictionScale` / `RearFrictionScale` of the stock
  row** (1 - 0.0025 n; 1.0 on the race cars). Braking uses the FRONT value for both axles; cornering each axle's own.
  Found by `corr.exe` (correlation of every per-car column with the scale each car needed: 402/415 equal).
- **`TireMod_FrictionScale` is NOT used by FM3** (10 rows left in the table; with it 9 cars were off by exactly its value).
- Final scales: braking x TorqueFreeLongFrictionScaleBrake x stock GameFrictionScaleBraking, plus drag / mass; cornering
  x TorqueFreeLatFrictionScale. Aero: body + stock rear bumper downforce, stock front bumper / rear wing List_AeroPhysics
  at DefaultTuneSlider, drag = body drag x GameDragScale (0.5..1.5) x product of the stock DragScale + wings.
- New in the tyre setup (`827368E8`), for the slip angle input of the PI: slip scale = PeakSASRScale x lerp(sidewall height
  = width x aspect x 1e-5, runtime table at `82C34638`+0x4B8..0x4C4) x pressure curve; the peak slip is then clamped between
  two runtime limits (+0x4C8 / +0x4CC, degrees). The friction peaks do not depend on it.

Other outputs: out+0x24 = C x 0.101971619 (m/s2 to g), out+0x34 = D / (method 0x3F0 x constant at `820873E4`),
out+0x30 = method 0x100 at 53.6448 m/s (120 mph), peaks from two 151-point tables (0x97, methods 0x3D8 / 0x3DC, peak
search `8262C208`), angular speed = rpm x 0.1047197. Without the full run (r10 = 0) fixed values are stored:
6, 12, 16, 67.056, 71.5264, 100, 0.9.

## Network file

`Media\AI\PerfPrediction\carperfpred.bin` (7297 bytes, big-endian), one file (FM2 has two):

- header: ints 1, 1, 18 (inputs), 1 (output), 2 (hidden layers), 3, 30, 10, 1, 3, 18, 30, 10; doubles 0.1 and 1.0;
  int 891 = weights with biases: (18+1) x 30 + (30+1) x 10 + (10+1) x 1. So MLP 18 - 30 - 10 - 1 (FM2: 18 - 33 - 11 - 4 - 1).
- 891 doubles from offset 0x44;
- then 1 byte (1) and 12 doubles = (range, min) of 6 inputs: 34.396 / 11.784, 19.779 / 0, 25.962 / 11.599,
  70.928 / 52.007, 0.1429 / 0.0792, 0.0860 / 0.0164. The same limits as FM2's `carperfpredcnst.bin`
  (Brake, Accel, Corner, TopSpeed, SlipAngle, MOIY), stored as range and min. The weights are FM3's own.

## Engine, top speed, acceleration and launch (2026-11-16)

`fm3perf`: **Sim peaks 415/415** (SimPeakPower / AngVel / Torque / TorqueAngVel), **SimRedlineAngVel 415/415**,
**SpeedRating 414/414** (the Veyron is clamped at 10), **AccelerationRating 388/396** (max measurement error 0.56%),
**LaunchRating 412/415** (max 0.43%).

- Engine (stock rows by EngineID): torque curve of the stock `List_UpgradeEngineCamshaft` row (RedlineRPM, TorqueCurveMaxRPM,
  NumRPMEntriesArray, TorqueCurveFullThrottleID -> List_TorqueCurve x TorqueScale x 0.01); multiplier = 1 + sum(TorqueScale - 1)
  over every stock engine table with a TorqueScale column (Displacement, Exhaust, FuelSystem, Ignition, Intake, Manifold,
  OilCooling, PistonsCompression, RestrictorPlate, Valves) + (RobScale - 1) of the turbo / supercharger.
- Turbo = the stock row of `List_UpgradeEngineTurboSingle`, `Twin` or `Quad` (MaxScale not 1); supercharger = stock
  `List_UpgradeEngineDSC` / `CSC`; intercooler MaxScaleScale. Boost rules as FM2's (boost by the previous point's power
  between PowerMinScale and PowerMaxScale hp, drop-off by rpm).
- The torque table is looked up as if n x step spanned n - 1 intervals (the FM2 quirk: also true in FM3; without it
  Acceleration drops to 108/396).
- Gears: stock `List_UpgradeDrivetrainTransmission` row by DrivetrainID. **NumGears counts the reverse** (GearRatio0);
  top gear = GearRatio[NumGears - 1]. Drive type = `Data_Drivetrain.DrivetypeID` (1 FWD, 2 RWD, 3 AWD).
- Tyre radius = width x aspect + rim x 0.0127 (Data_Car aspect / rim, stock width); driven radius: rear for RWD, else front.
- Top speed (`821EDFF0`) = min((GameTorqueScale x peak power / drag)^(1/3), (redline + TorqueCurveMaxRPM) / 2 in top gear).
- Acceleration at v (`8272EBF0`): first forward gear with v below the redline speed, wheel torque = ratio x final x torque
  x GameTorqueScale, capped by the driven tyres' grip (traction scale TorqueFreeLongFrictionScaleAccel1, front friction scale
  for both axles), 6 load-transfer iterations, minus drag.

## PI: a simulated lap, not the network (2026-11-16)

FM3 does NOT use `carperfpred.bin` for the PI. `826BD230` loads **`GAME:\Media\Physics\PI.xml`**: MinPITimeSeconds 97.4,
MaxPITimeSeconds 159.1, 8 forward gears by RedlineSpeedMPH (44, 72, 100, 131, 155, 180, 215, 255) and a track of 15 segments
(StraightLengthMeters, CornerRadiusMeters, CornerAngleDegrees; arc = radius x angle). FM4 has a `PI.xml` too.

`826BCCB0` (PI):
- The car's gearbox is replaced by the standard one: ratio g = driven radius x redline (rad/s) / (speed g x final drive).
- Three laps: the stock setup, then both aero sliders (Tuning_frontDownforce / rearDownforce) at 0, then both at 1; the
  SHORTEST time counts.
- PI = clamp((Max - clamp(t, Min, Max)) / (Max - Min)) x (1 - m) + m, m = CarClassModifier (0 on every stock car).
- Shift time = (ClutchInTime + ClutchOutTime) / 2 + GearShiftTime (stock clutch and transmission rows).

The lap (`826BC760` setup, `826BCC20` loop):
- A table of 26 speeds, 0 .. 111.815186 m/s, step = 111.815186 x 0.04 (`826BC140`): cornering (slot 0x3FC `8272F4F0`),
  radius = v^2 / cornering, braking (slot 0x3F4), time and distance to brake across one step, acceleration (slot 0x400
  `8272F018`).
- Corner speed of a segment = the speed whose radius is the segment's (`826BC5D0`, lerp in the table).
- Start at the last corner's speed. Straight (`826BC8C8`): steps of 0.5 s at the table's acceleration; at a gear's redline
  speed the step ends there and a shift follows (shift time at minus drag / mass); when the braking distance to the next
  corner speed no longer fits (`826BC3D8`), a 10-step bisection finds the braking point and the braking time is added
  (`826BC4E8`). Corner (`826BCBB0`): speed = min(speed, corner speed), time += arc / speed.

The two PI-only measurements:
- Cornering 0x3FC = the rating's cornering with each tyre's friction x cos(peak slip angle at its load). Peak slip angle =
  peak index x MaxSlip (lateral multi-curve) / (N - 1) x PeakSASRScale (Combo_TireBrandCompound) x lerp(sidewall height =
  width x aspect x 1e-5 m, 0.0762 .. 0.1397 -> 1 .. 1.25), then scaled so the high-load curve's angle stays within 5 .. 20
  degrees (defaults written by `82736DF0`). Without the cosine the PI is 0.0056 too high on average.
- Acceleration 0x400 = the rating's acceleration with v >= 2.2352 m/s and the wheel torque x KE / (KE + 1),
  KE = m v^2 / (I w^2), I = engine + flywheel inertia + (transmission + driveline inertia) / gear^2 + 2 x wheel inertia x
  (wheel speed / engine speed)^2 per axle (Data_Engine.MomentInertia, stock List_UpgradeEngineFlywheel,
  List_UpgradeDrivetrainTransmission, List_UpgradeDrivetrainDriveline; the game stores them x 0.01, its mass unit is
  kg / 100). Without it the PI is 0.036 too high. **Wheel inertia 1.76 is a fit** (1.6 x 1.1; the value each car needs has
  a median of 1.77, 80% of the cars between 1.70 and 1.81; not found in the code yet).

Checked hypotheses (`fm3perf ... all 0 name=value`): rpm=1 (TorqueCurveMaxRPM) and rpm=2 (peak power) are worse than the
redline; cos=0, inertia=0, variants=1 (stock setup only) are worse.

The network file `carperfpred.bin` (next section) is still on the disc; what uses it is not known (the string
`runperfpredautomation` suggests a developer tool).

## In the editor: ForzaStudio\Fm3Perf.cs (FM3 ONLY, 2026-11-16)

- A port of the `fm3perf` harness (same formulas, the fitted defaults fixed: redline gears, cosine, inertia, 3 aero laps,
  wheel inertia x 1.1). `Fm3Perf.Compute(db, carId, media)` -> PI, lap time, five ratings, Sim peaks, mass, front weight;
  `media` = the GAME's Media folder (`Physics\PI.xml`). Without PI.xml the PI is NaN.
- `Fm3Perf.Update` is called by `CarDuplicate` when an FM3 copy gets an engine or gearbox of its own (FM4 still uses
  `CarPerformance.Update`). It writes the columns the game itself rewrites together (UPDATE string `8200E560`):
  **PerformanceIndex, ClassID, Speed / Acceleration / Launch / Braking / Handling rating, SimPeakPower, SimPeakAngVel,
  SimPeakTorque, SimPeakTorqueAngVel, SimRedlineAngVel, CurbWeight (mass / 100), WeightDistribution**. All or nothing: no
  PI.xml or a missing row = nothing written, the source's values stay (a line in the report says why). Columns typed by
  the user are kept; the class follows the PI that is stored (also a typed one). PI x (1 - CarClassModifier) +
  CarClassModifier as in `826BCCB0` (0 on every stock car).
- `PeakIntakePSI` of the game's UPDATE is a column of `NewProfile_Career_Garage` only (empty on the disc), not of Data_Car.
- The Sim times, SimTopSpeed and the figures of the car's page are not part of the game's calculation: still scaled
  from the source's by `CarPerformance.Update(..., timesOnly: true)`.
- Packs: the stage has no `Physics\PI.xml`; `CarDuplicate.PerfMedia` (the game's Media) is passed, as for FM2.
- Check of the compiled app, `E:\DEV\fm4dev\ST02\perf\fm3\fm3perfapp.exe <gamedb.slt> <Media> [car|all]` (needs the
  `.exe.config` beside it: XNA is mixed mode): the same counts as the harness, plus **class 413/415** (Peugeot 206 RC and
  Renault Twingo sit 0.0003 from a class limit: computed D 276 / E 201, the game E 275 / F 200) and **CurbWeight /
  WeightDistribution 411/415** (the four "IE" cars 1208, 1209, 1212, 1213 store values that are not their stock weight
  row's; the computed ones are the row's, which is what the physics uses).
- `fm3updtest.exe <gamedb.slt> <Media> <car>`: `Update` on in-memory copies (unchanged, torque x 1.3, no PI.xml, PI typed).
  Ferrari 360: unchanged A 517 -> A 517; torque x 1.3 -> A 589, braking and handling unchanged.
- Known limit (not new): CurbWeight / WeightDistribution typed in the Duplicate wizard change Data_Car only, not the
  copy's `List_UpgradeCarBodyWeight` row, so they change neither the physics nor the computed PI.
- In game 2026-11-16: a pack copy from the app (`TOY_SupraSZ_98`, Id 1317, the Supra RZ made natural: C 396 -> D 306) shows D 306 in
  the showroom and in a race, the stored value. A copy into the game folder itself is not tested.

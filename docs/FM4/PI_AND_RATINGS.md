# FM4: how the game computes PI and the five ratings (default.xex, started 2026-11-16)

In the editor since 2026-11-16: `ForzaStudio\Fm4Perf.cs` (Duplicate Car). FM4's own code: never reuse FM2's or FM3's classes (FM3 rules:
`docs/FM3/PI_AND_RATINGS.md`; FM4 is FM3's model with the differences below).

Image: `E:\DEV\fm4dev\ST02\perf\fm4\xex\fm4_base.exe` (XexTool basefile of `FM4_Extracted\default.xex`, base 0x82000000).
Tools in `E:\DEV\fm4dev\ST02\perf\fm4`: `xex\xexfp.exe` (copied from fm3), `tools\vmx.exe` (decodes VMX128 instructions that
xexfp shows as op4/op5/op6), `tools\fconst.exe` (finds float/double constants), `tools\rf.exe` (reads floats at addresses),
`corr.exe`.

## RESUME HERE

1. Harness `E:\DEV\fm4dev\ST02\perf\fm4\fm4perf.exe <gamedb.slt> [car|all] [100 = one line per car | 200 = braking/handling
   ratios] [name=value options]` on the 504 rows of `FM4_Extracted` (500 stock + 3 hybrids + the DEV test row 1638/1639):
   **Braking 502, Handling 500, Sim peaks 501, redline 504, Speed 466/471, Acceleration 485/501, Launch 494/502, PI 296 within
   0.001, 500 within 0.005** (max 0.0054). User 2026-11-16: 0.001 .. 0.005 is an accepted PI tolerance.
   The misses are the hybrids / electric cars (Prius, CT200h, Volt; Leaf is not computed: electric motor `MotorID`, not
   modelled) and the DEV test rows `*_DLC1`.
2. App code `ForzaStudio\Fm4Perf.cs` (FM4 only, 2026-11-16), wired like Fm3Perf in CarDuplicate: a copy with its own
   engine or gearbox gets PI, class, 5 ratings, 5 Sim peaks, CurbWeight, WeightDistribution as a whole set (columns typed by
   the user kept); the Sim times stay scaled (`CarPerformance.Update` timesOnly). PI.xml from `PerfMedia ?? Media`.
   Checked: `E:\DEV\fm4dev\ST02\perf\fm4\app\fm4perfapp.exe` (the app file on the 504 rows) gives the harness counts above;
   `fm4updtest.exe` (in-memory Update: unchanged, torque x 1.3, no PI.xml, typed PI) OK. In game 2026-11-16: an in-app
   duplicate in FM4 DEV (Supra RZ 461, less torque) computed C 386 and the game shows C 386. **FM4 closed.**
3. Fits / open: (a) wheel inertia = List_TireCompound.MomentInertia x 1.25 (FM4 keeps a per-axle value at interface
   0x123C front / 0x1A7C rear, source not found); (b) the PI lap uses the standard PI.xml gears or the car's own: the game reads a
   per-car byte (config + 0xC80, at 828A7948) not traced to the database; **rule used: own gears when the stock
   List_UpgradeDrivetrainTransmission.Level < 2** (best of the levels tried); (c) acceleration residual about -0.2%.

## Call chain

| address | what |
|---|---|
| `8240FD08` | garage stats update; same `UPDATE %s SET ClassID, PerformanceIndex, ...` string as FM3 (`82015F80`) |
| `828A6078` | builds the car config from the database |
| `828A81E8` | computes everything: Sim peaks, the five ratings, PI (`828A7880`), class |
| `828A7D00` / `828A88F0` | parse / load `GAME:\Media\Physics\PI.xml` |
| `82892968` / `82892A00` | a track segment / its corner geometry |
| `8289B718` | lap setup; `82892220` table of 26 speeds; `82894450` the lap; `82893BE0` straight; `82893E98` corner |

## Ratings (828A81E8), each clamped to 3 .. 10, scale 7 (FM3: 6.9)

```
0x60 Speed        = clamp((top - 58) * 0.1590909 + 3)                      top as FM3 (m/s)
0x64 Acceleration = (ln A - ln 1.35997) / (ln 11.99 - ln 1.35997) * 7 + 3   A = mean 40..80 mph, 13 speeds (slot 0x88)
0x68 Launch       = (ln B - ln 2.92428) / (ln 13.65 - ln 2.92428) * 7 + 3   B = mean 20..40 mph, 13 speeds
0x6C Braking      = (ln max(C, 0.01) - ln 10.1341) / (ln 22.54 - ln 10.1341) * 7 + 3   C = mean 50..120 mph, 8 speeds (slot 0x78)
0x70 Handling     = (ln max(D, 0.01) - ln 8.87586) / (ln 19.7 - ln 8.87586) * 7 + 3    D = mean 50..120 mph, 8 speeds (slot 0x7C)
```

Sim methods: `CCarDynamics` vtable `820D03BC`, this-offset 0x960: 0x78 braking `82818020`, 0x7C cornering `822F6D18`, 0x80
PI cornering `82818F28`, 0x84 PI acceleration `828189E0`, 0x88 acceleration `82818568`. Same formulas as FM3 except:

- **Centre of mass height = List_UpgradeCarBodyWeight.CMHeight only** (FM3 adds the mean spring ride height; without this
  change FM4 braking 5/504, handling 2/504).
- Friction scales per axle from the stock `List_UpgradeCarBodyChassisStiffness`: Front/RearLatFrictionScale for cornering,
  Front/RearLongFrictionScale for braking and acceleration (FM3 has one Front/Rear pair and braking used the front value).
  `CMHeightDiff` is 1 on every stock row (not used).

## PI: the lap of PI.xml, FM4 version (828A7880)

FM4's `PI.xml`: Min 170 s, Max 305.8 s, TrackWidthMeters 6, per drive type (FWD / RWD / AWD) AccelFrictionScaleWhileCornering
0.85 / 0.793 / 1, AccelFrictionScaleWhileStraight 0.97 / 0.97 / 1, LapTimeScale 1 / 1.01 / 1; the same 8 gears; 15 new segments.

- PI = clamp((Max - clamp(t, Min, Max)) / (Max - Min)) x (1 - m) + m, t = best of the three aero laps x LapTimeScale.
- Gears: standard PI.xml gears or the car's own forward gears (see RESUME 3b); gear speed = driven radius x redline / (ratio x final).
- Table of 26 speeds 0 .. 111.815186 m/s as FM3; acceleration with both axles' traction x AccelFrictionScaleWhileStraight.
- **PI cornering**: the rear lateral force is capped by the yaw balance, rear <= front x max(1 - wF, 0.001) / wF (inside the 6
  load-transfer iterations); the table also keeps ratio = capped / uncapped rear force.
- **Corner geometry** (82892A00, VMX part decoded with tools\vmx): radius R for half the angle (length R x angle / 2), then an
  exit spiral turning the other half: curvature 1/R + (0.001 - 1/R) x t^p over 40 exact arcs (t = j / 39); p from 1 by a
  10-step bisection (no upper bound yet: p x 2) so that n . (S - M) = TrackWidth, n = (-cos h, sin h), M = the arc's point after
  h = angle / 2, S = the spiral's end. Stored: len2 = spiral length, along = u . (S - M), u = (sin h, cos h).
- **Each straight is shortened by the previous corner's `along`, minimum 50 m** (828A8164; without it every lap is 5% slow).
- Lap start speed = sqrt(0.95) x last corner speed; straight target = sqrt(0.95) x corner speed.
- Straight: steps of 0.2 s; a shift starts when v > the gear's speed (gear < n, n >= 2) and lasts the shift time at drag
  deceleration; the shift state carries over segments and corners; braking point by the 10-step bisection as FM3, then the gear is
  chosen again for the speed.
- Corner, part 1: speed limit sqrt(0.95) x corner speed, cruise to the end of the part when at the limit (dt x 1.001), otherwise
  accelerate. Part 2 (spiral): limit = corner speed of the radius at d / len2 (no 0.95), accelerate. Steps 0.2 s, the step
  is cut at the part's end.
- Corner acceleration (828927F8): u = v^2 / limit^2; front traction x max(0, sqrt(1 - u^2) - (1 - c)), rear traction x
  max(0, sqrt(1 - (u x ratio)^2) - (1 - c)), c = AccelFrictionScaleWhileCornering; result >= 0.
- PI acceleration inertia as FM3: engine + flywheel, (transmission + driveline) / gear^2, 2 x wheel inertia x (wheel / engine
  speed)^2 per axle.

Checked: k95=1, cap=0, inertia=0, straights not shortened, standard gears for every car, own gears for every car: all worse.

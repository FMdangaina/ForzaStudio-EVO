# Driver position and hands (ROADMAP 6.2), first pass 2026-10-22

Data: the FM4 and FH1 Supra (`TOY_SupraRZ_98`) and, from the dev sources (`fm4_dev\samples`), the Supra's and the 918 Spyder's `Locators.xml`. Only one car has both the cockpit file and the locators, so what follows are observations, not a rule.

## Where the driver is stored (game files)

| Where | What |
|---|---|
| `_cockpit.carbin` header 0x1C8-0x2FF | the rig, big-endian floats, model axes (decoded earlier, `CarPort.CockpitRig`): six hand grips (3 radii x +X / -X), wheel pivot (0x228), wheel axis (0x238), a point at 0x248, a point at 0x258 (shifter side), four records of point / direction / value from 0x270 |
| `carattribs.xml` | `DriverHandsOffset` (x y z), the `Driver` element: `ButtOffset`, `ButtOffsetLOD0`, `LeftHandOffset`, `RightHandOffset` (+ LOD0), `ShifterOffset`, `HandBrakeOffset`, `IKGuideTranslation` (x y z each), `BodyLean`, `BodyScale`, `HasPaddleShifter`, `HasNoHandBrake` |
| DB | `CarPartPositions` part 9 (steering wheel point, the cockpit camera) and `CameraOverrides` |

On the Supra every `Driver` offset is 0 except `ButtOffset.z = -0.1`; `DriverHandsOffset` is 0.338, 0.203, 0.272 (FM4) and 0.340, 0.174, 0.287 (FH1).

## FH carries the same rig

The 312 bytes 0x1C8-0x2FF of the FM4 cockpit are **identical** to the FH1 cockpit's, found 4 bytes later (the FH header word that differs); the port therefore gets the Supra's rig as it is (the FM2 and traffic cases are the ones `CockpitRig` rebuilds).

## What the points are, against the source scene's `Locators.xml`

The scene locators (dev files, not in the game zips) are in the car's frame (Z forward); the cockpit file's frame is the same with Z shifted by about -0.27 m (and Z pointing to the rear in the carbin).

| Rig value (cockpit file) | Locator | Match |
|---|---|---|
| wheel pivot 0.339, 0.602, -0.144 | `carLocator_steeringWheel` 0.338, 0.604, 0.121 | x, y within 2 mm |
| point 0x258 -0.031, 0.369, -0.140 | `stickShift` -0.033, 0.371, 0.135 | x, y within 2 mm: **the shifter** |
| point 0x248 0.435, 0.165, 0.322 | `Handbrake` 0.056, 0.273, 0.134 | no match: not the hand brake (? the right-hand drive's door side) |
| records 1-3 (0.22 / 0.34 / 0.49, 0.6, -0.38), direction (0.008, 0.193, -0.981) | none | three points along the dash looking forward, value 4.1 / 4.0 / 1.4 (? gauge cluster or eye points) |

The seats: `seatR` origin 0.377, 0.458, 0.307 in the cockpit file; the scene has `carLocator_seatCamRF` at x 0.380 (the same x), y 0.829, z -0.536. So the seat camera sits on the seat's x, 0.37 m above its origin.

## Two rigs: LOD 0 and LOD 1..5 (2026-11-17)

A user's car made from scratch showed the driver in different places close up and from a distance. The header block is `CDriverControls` version 3, 16 vec4 (names from `default.xex`, `CarbinModel.DriverControlNames`), and it holds two rigs in two files:

| | LOD 1..5 | LOD 0 |
|---|---|---|
| stored in | main `<car>.carbin`, vectors 0-5 | `<car>_cockpit.carbin`, vectors 6-15 (0x1C8-0x258) |
| wheel | `SteeringWheelPosition`, `SteeringWheelAxis` | `SteeringPivotLOD0` (0x228), `SteeringWheelAxisLOD0` (0x238) |
| hands | `LeftHandAnchor`, `RightHandAnchor` | six grips, `Right` / `LeftHandOuter`, `Inner`, `Center` |
| seat | `ButtAnchor` | `ButtAnchorPositionLOD0` (0x248, the "point at 0x248" above) |
| shifter | `ShifterAnchor` | `ShifterAnchorPositionLOD0` (0x258) |
| carattribs | `ButtOffset`, `Left` / `RightHandOffset` | `ButtOffsetLOD0`, `Left` / `RightHandOffsetLOD0` |

The `_lod0.carbin` has the block too, with every point zero on all 313 cars (only the two axes are set, 0, 0, 1): the close-up car (`_lod0` + `_cockpit`) has one set, the cockpit's. The cockpit's points 0-5 are zero (but a wheel position on two cars and a `ButtAnchor` on one), the main file's 6-15 are zero. The main file's `ShifterAnchor` is zero on 54 cars, the cockpit's on 70.

Survey of the 313 FM4 cars with a main and a cockpit file (`extracted_dvd1\Media\cars`, harness `tools\DriverSurvey`):

- The two rigs are not the same points. Wheel, main against cockpit: 4.9 cm apart as a median, 12.5 cm at the 90th percentile, 32 cm at most. Seat anchors: equal on 24 cars, median 5.4 cm apart. `ButtOffset` = `ButtOffsetLOD0` on 61 of 308.
- Each follows the geometry of its own file: the main file has its own `steering_wheel` and `seatL` / `seatR` parts (313 and 307 cars), 4.9 cm (wheel) and 7.1 cm (seat) away from the cockpit's as a median. Main wheel anchor x = main `steering_wheel` origin x within 0.1 mm (median; 2.4 mm at the 90th percentile).
- Main rig against its own parts, medians: hand anchors centred on the wheel x, 17 cm to each side, 4.9 cm above and 2 cm in front (-z) of the wheel anchor; seat anchor on the seat origin's x, 30 cm below it and 2 cm in front (wide: y -0.37..-0.23, z -0.10..+0.09 for 80%).
- Supra: main wheel 0.423, 0.585, -0.116 for a part at 0.427, 0.595, -0.094; cockpit pivot 0.339, 0.602, -0.144 for a part at 0.339, 0.603, -0.151. Both seat anchors 0.435, 0.165, 0.322; `ButtOffset` 0, 0, -0.1 and `ButtOffsetLOD0` -0.08, 0, -0.15.

What the port does with them (rules of the user, 2026-11-17; compile-checked, no FM2 / FM3 / FH file at hand to run it):

- **LOD 0 points** (cockpit header): rebuilt only for a cockpit the port itself built from LOD 1, i.e. an FM2 car or a traffic car without close-up models (`CarPort.CockpitRig`, flag `cockpitFromLod1`; the same for carattribs `DriverHandsOffset`). A car that comes with its own `_lod0` / `_cockpit` (FM3, FH1, FH2) has the points FM4 reads: they stay as they are. Before, any cockpit whose pivot was more than 3 cm (x) or 15 cm off the `steering_wheel` origin was rebuilt; FM4's own pivots are up to 0.44 m from it.
- **LOD 1..5 points** (main header, vectors 0-5): the source's. The FH and FM3 conversions carry the header, so they are the car's own. An FM2 stage's main file is the donor's with the FM2 geometry imported (`Fm2Source`), so they were the donor's; now they are the FM2 carbin's own six anchors (`Fm2CarbinModel.DriverAnchors`, version 1: the same six vectors), taken when the wheel point is on the car's `steering_wheel` part (x within 5 cm, 0.30 m, unit axis), else the donor's stay with a warning in the log (`Fm2Source.DriverPointsFromFm2`). The port's report has a line for them and a WARNING when the wheel point is empty or more than 5 cm (x) off the main file's `steering_wheel` (`CarPort.DistantDriverPoints`); nothing is computed.
- Open: check on an FM2 car that the six anchors are in the parts' axes (the test above keeps the donor's if not), and whether a rebuilt cockpit's seat and shifter points should come from the main file's instead of the donor's moved with the wheel.

## Editing by hand

The model view has a **DRIVER** tab (like EXHAUST): lists the header points and the carattribs offsets, three number boxes, crosses and lines in the view, Apply writes the model file and `carattribs.xml` into the archive (pending until File > Save). With a car's `_cockpit.carbin` or `_lod0.carbin` shown it lists the LOD 0 rig (stored in the cockpit file); with the main `.carbin` the LOD 1..5 rig (2026-11-17, compile-checked only). Not checked in game; what each carattribs offset moves is the open part: change one value at a time and look (butt, hands, shifter, hand brake).

## Open

- Whether `ButtOffset` / `BodyLean` / `BodyScale` move the whole seated body, and in which frame (the Supra has only `ButtOffset.z = -0.1`).
- What the records at 0x270 and the point at 0x248 drive.
- The rule from the seat to the body position: a comparison across cars needs the cockpit files and `Locators.xml` of more than one car (the dev folders have no cockpit file for the 918).

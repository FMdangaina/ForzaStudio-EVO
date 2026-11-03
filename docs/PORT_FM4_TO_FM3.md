# FM4 → FM3 port: descriptor

What the FM3 writer has to produce from an FM4 car, file by file. Reference car: **Supra RZ '98** (`TOY_SupraRZ_98`, Id 461 in
FM2, FM3, FH1, FH2 and FM4). Measured 2026-11-01 on the FM4 (`FM4_Extracted`) and FM3 (`FM3_Extracted`) copies of that car;
harnesses and outputs in `E:\DEV\fm4dev\PortRefactor\fm4fm3` (`ls`, `same`, `tex`, `db_fm3_vs_fm4.txt`).

Rule for every step: it is the inverse of the FM3 → FM4 step the port already has (`CarPort`, `CarbinModelMotorsport3.cs`),
and it is tested twice on the Supra: **FM3 → FM4 → FM3** must give back the FM3 file, **FM4 → FM3** is compared with FM3's own.

State: ✅ measured, inverse clear · 🔍 to study · ⬜ to write

---

## 1. Car zip (`Media\cars\<car>.zip`)

Same layout in both games (43 entries in all, 8 byte-identical).

| Entry | FM4 | FM3 | FM4 → FM3 | State |
|---|---|---|---|---|
| `liverymasks\*.tga`, `masks.xml` | same | same | copy | ✅ |
| `physics\maxdata.xml` | same | same | copy | ✅ |
| `carattribs.xml` | 3925 B | 3907 B | same elements and values (Driver Version 6); only `" />"` vs `"/>"` and the driver's butt offsets (FM4 retuned them). Copy | ✅ |
| `shadersettings.xml` | `lights_glass` | `tail_light`, `body_2` | material names differ: map FM4 materials to FM3's (inverse of the FM3 → FM4 material step) | 🔍 |
| `*.xds` textures | header byte 31 = `FE` | `02` | same size; colour textures: inverse of `ConvertMotorsport3Texture` (gamma). FM4's are re-encoded by Turn10, not our conversion, so the check is on decoded pixels, not bytes | 🔍 |
| `leather2_nrm.xds` | 49204 B | 24628 B | FM4 has a bigger copy (mips?); normal map, no gamma | 🔍 |
| `<car>.carbin`, `_lod0`, `_cockpit` | type 2, FM4 vertices | FM3 layout | `ConvertMotorsport4ToMotorsport3` (section 2) | ✅ |
| brakes `caliper??` / `rotor??` | `_lod0.carbin` | no suffix | rename `<car>_caliperlf_lod0.carbin` → `<car>_caliperlf.carbin`; model as section 2 (16-byte vertices) | ✅ model, ⬜ rename in the port |
| `versiondata.xml`, `buildnumber.txt` | yes | no | leave out | ✅ |
| `cars_<car>_build_report.html` | yes | yes | Turn10 build log, not read by the game: keep FM4's or drop | ✅ |

## 2. Model (`.carbin`), inverse of `CarbinModelMotorsport3.cs` ✅ coded (`CarbinModelToMotorsport3.cs`)

`CarbinModel.ConvertMotorsport4ToMotorsport3()`, then `ToBytes()` writes the FM3 layout. Tested 2026-11-01 (harnesses `rt`, `rt2`,
`fwd` in `fm4fm3`):

- **FM3 → FM4 → FM3** on 10 cars, 10 wheels and the Supra (120 files, `rt_sample.txt`, `rt2_sample.txt`): every file comes
  back with FM3's length, header, trailer, draw lists, section words, bounds, subsections, indices, damage words and S16
  values; normals within 0.07°. Positions within 1.15 mm on cars, 0.15 mm on wheels and brakes (FM3 stores them as halves);
  UVs of drawn vertices within 0.016 (one Camaro file 0.48: a very wide UV range in FM4's 16 bits). What does not come back:
  the word at 32 of 40-byte vertices (FM4 dropped it, written 0), the Z axis of ~0.03% of vertices where FM3's is not
  perpendicular to the normal (FM4's quaternion cannot hold it), UVs of vertices no subsection draws, and `Unk1c` float 0 on the
  sections where the FM3 artist did not follow the usual value (below).
- **FM4's own Supra → FM3** (`fwd`, `out_fm4_to_fm3`): all 11 files convert and read back as FM3 with FM3's header sizes; 18 of
  the main file's sections and all 22 cockpit sections have FM3's vertex counts, the others were re-exported by Turn10 for FM4.
- **Found on the way, in the existing FM3 → FM4 port** (not this writer): vertices outside a section's bounds are pulled in by
  up to 53 mm (`Encode` caps the length at the bounds: Supra `glassRR` 34 mm, Countach main 27 mm and cockpit 53 mm). Fix: grow
  the bounds before encoding. FIXED 2026-11-02 (`CarbinModelMotorsport3.ConvertMotorsport3Section`: `Escapes` + `GrowBounds`, only
  for sections that need it): worst deviation FM3 vs converted positions 0.066 mm on Supra and Countach (harness
  fm4dev\PortRefactor\fm3fm4cam\dev.exe). The FM4 → FM3 direction deviates up to 1.46 mm (Supra body): FM3's half-float vertices.

Rules measured for the writer:

- subsection head: FM3's 9 bytes are `000000020000000002` on all 945 subsections of the Supra;
- `Unk1c` float 0 (FM4: 1.0 everywhere): FM3 has 1.0 on bumpers, hood, skirts, trunk, wings, mirrors, exhausts, 0 on body, glass,
  frames, interior, gauges (majority on 40 FM3 cars, `f0_census.txt`; e.g. bumperF 98 of 117); 1.0 in wheel files;
- wheel vertices (44 bytes) end with the constant `000003FF 00000000 3F800000 000003FF 00000000`;
- the 0x104 bytes at the start of car files are leftover memory in both games but for the floats of main files; a small word at
  4 is replaced by FM3's lod0/cockpit `003A0000` so the file reads as FM3.

FM3 → FM4 does, and FM4 → FM3 undoes:

- **header**: format block `[0x10, 0x10, 1, N]` → `[0xD, 0xB, 1, N]`; drop the 0x50 bytes FM4 cars have before it (wheels: the 12
  measured floats); 15 blocks of 0x24 bytes → FM3's 4 (the 11 FM4 ones hold defaults); drop FM4's `(1, 1, 0)` / `(0, 1, 0)` before
  the final `(4, 4)`; brakes: FM4's end floats → 0 where FM3 has 0;
- **sections**: marker 5 → 2; FM4's Pre word → FM3's word before VbTest2; drop Post9, the damage tables and Mid; subsections lose
  Pre5, UV offset/scale and the fixed 36 bytes;
- **vertices** (40 bytes on car parts): FM4 position → 4 halves (origin + xyz × w), uv0/uv1 → halves, normal 11:11:10, the 4 S16
  values, the damage word, the word FM4 dropped (to rebuild), tangent Z 11:11:10. Brakes 16 bytes, wheels 44 bytes;
- a part is deformable in FM4 exactly when its FM3 damage words are not all `000003FF`, so non-deformable parts get `000003FF`.

Test: Supra FM3 → FM4 → FM3 byte-identical (except the word FM4 dropped), then Supra FM4 → FM3 against FM3's file.

## 3. Database (`Media\db\gamedb.slt`), car 461

FM3 and FM4 share almost every table (`GameDbSchema.cs`, 340-relation map). Differences seen on the Supra:

| Table | FM3 | FM4 | FM4 → FM3 | State |
|---|---|---|---|---|
| `Data_Car` | + `AssistsSTMSlipDefMin` 1.5, `AssistsSTMSlipDefMax` 3 | + `AutoSteerOverrideID`, `IsCarExperienceUnlocked`, `HasCarExperience`, `Front/RearStockRideHeight`, `IsDrivable`, `HasRaceLivery` | drop FM4-only columns; FM3-only from FM3's usual values | 🔍 |
| `Data_Car` PI / class | PI 0.2808, class 3 | PI 0.3801, class 4 | PI mapped back on the FM3 scale (inverse of `PerformanceIndexPairs`), class from it | 🔍 |
| `Data_Car` steering, Sim*, ratings, cost | FM3 values | FM4 values | FM3 tuning: inverse of `TuneMotorsport3Handling` / ratings | 🔍 |
| `Data_Engine`, `List_UpgradeEngine` | 2 engines (215 + swap 202) | 1 | FM4's engine; swaps are FM3's own list | 🔍 |
| engine upgrades (Camshaft, Exhaust...) | 8 rows | 4 | FM3 has more levels: rebuild from FM4's | 🔍 |
| `List_UpgradeEngineCSC`, `DSC`, `Manifold` | 3 rows | — | FM3-only upgrades | 🔍 |
| `List_TorqueCurve` | zero-throttle curve | `ZeroThrottleTorqueScale` | inverse of `ConvertMotorsport3TorqueCurves` | 🔍 |
| `CarPartPositions` | 9 rows (parts 0-8) | 10 (+ part 9, steering wheel) | drop part 9 | ✅ |
| `CarExceptions` | 0 | 1 | FM4 only | 🔍 |
| `List_UpgradeRearWing`, `TireCompound` | 5 / 4 | 6 / 5 | FM3 has one level less | 🔍 |
| FM4-only tables (`CarListFilters`, `HasCarExperience`, `Kinect*`, `NewProfile_*`, `StripeColors`, `EventShowroomChallenges`, `List_UpgradeMotor`) | — | yes | nothing to write | ✅ |
| `EventParticipants` | 14 | 4 | career data, not the car | — |

## 4. Still to measure

- **Wheels** (`Media\wheels\*.zip`, FM3 folder `Media\Wheels`): the model is done (section 2); the zip: FM3 has
  `wheel0_lod0.xds` … `wheel3_lod0.xds` that FM4 lacks (copies of the LOD 0 textures?), FM4 has `versiondata.xml` and
  `buildnumber.txt`. 🔍
- **Dash** (done 2026-11-03, offline checks; in-game check pending): FM4 reads `digitalgauge\dash_<car>.*` from the car zip (GaugeID -1);
  FM3 has `Data_Car.GaugeID` → `List_DigitalGauges` (GaugeID, MediaName `Dash_<car>`) → `Media\DigitalGauges\Dash_<car>.zip`
  (`dash_<car>.fbf/.bgf/.bsg`) + images in `Media\UI\Textures.zip` as `UI4/Dashboards/<CAR>/<name>.xds`, the .fbf naming them
  `GAME:\MEDIA\UI\TEXTURES\UI4\DASHBOARDS\<CAR>\<NAME>.TGA`. `CarPortToFm3.PortDash` + `Fm3Dash` (inverse of `AddMotorsport3Dash`):
  - the car keeps its OWN dash (before, `GaugeID` stayed the donor's: the Supra's dash on every port). No dash in FM4 → `GaugeID -1`;
  - .bgf and .bsg unchanged but the font names (`DashFonts.ToFm3`: a_swiss_721 → a_endurancepro, b_swiss_721 → b_endurancepro bold,
    d_swiss_721 → d_endurancepro-condsemibold; FM3's fontmap resolves A_/B_/D_/E_Endurance by prefix, DG1-5 as FM4);
  - .fbf: version 0x3EE, text tails 33 bytes, the default text = the .bgf `textstring` (FM3: 303 of 303 text records, FM4 empties it);
  - images: converted by `Fm3Textures`; a file FM3's shared `MFD_*` folders already hold keeps FM3's, the others go under the car's folder;
  - 59 dashes both games have: with images and fonts as FM3's own, 35 come out byte-identical, the other 24 were re-authored in FM4;
  - 234 FM4 dashes (corpus) convert, re-read as FM3 and compose: 0 failures; 30 use properties no FM3 dash has (the six-dash animation
    behaviour, the "Overlays group" parameter, tach memory, image UV offsets; real cars: FER_F142_10, FER_F151_11, LEX_LFA_10, the rest are
    Autovista `_slod` scenes). Such a dash is left out (`GaugeID -1`, reported) unless `AllowFm4OnlyDash`; in game test: AST_One77_10 (plain)
    and LEX_LFA_10 (FM4-only, ported anyway) in `FM3_Extracted_DEV`. Gauge ids come from `DbIds.Next` (284, 285). 🔍 game
- **Sound**: FM3's engine tuning has no DSP blocks (FM3 → FM4 completes them from FM4): FM4 → FM3 drops them; banks by name. 🔍
- **Strings**: FM3 `.str` tables and their table ids (Data_Car high part). 🔍
- **Thumbnails, make, decals**: FM3 has every FM4 make but those FM4 added. 🔍

---

## 5. State of the port (2026-11-01, evening)

`CarPortToFm3` (+ `Fm3Textures`, `CarbinModelToMotorsport3`) brings an FM4 car into an FM3 folder. It writes only under `OutputRoot`
(the FM3 folder's Media layout: car zip, `gamedb.slt`, the 16 string zips, `UI\Textures.zip` with the thumbnail, `zipmanifest.xml`);
copying that tree over an FM3 DEV folder is the install (`E:\DEV\fm4dev\PortRefactor\fm4fm3\install_dev.sh`, which copies into
`F:\Xenia\Games\FM3_Extracted_DEV`, a copy of FM3_Extracted made 2026-11-01). Harness: `fm4fm3\toFm3.exe <FM4 MediaName> [donor FM3 car] [new name]`.

Done on the Supra RZ '98 (FM4 id 461 → FM3 id 1313, name `TOY_SupraRZ_98_FM4` because FM3 has the car):
- car zip: 11 models (`ConvertMotorsport4ToMotorsport3`), brakes renamed, 11 textures (gamma curve undone, flag off, AS_16 → plain; the
  DXN normal map → DXT1), `lights_glass` → `detail_glass_clear`, `shadersettings.xml` the donor's, no `versiondata` / `buildnumber` /
  build report; headers of the donor's zip (`CreateLike`). Texture mean error against FM3's own Supra: 1.0-3.4 per channel (DXT1 6.8);
- database: 99 rows in 27 car tables from FM4, `Data_Car` on the donor's FM3 row (FM3-only columns, `GaugeID`), PI 0.3801 → 0.2717
  on the FM3 scale (316 shared cars), class C, `Combo_Engines.EngineComboID` moved, engine / drivetrain / wheel FM3 already has
  are FM3's own, aero set 19 added, drag tire compound left out; `DbIntegrity.Compare` (FM3): 0 errors;
- strings: two keys in `Data_Car.str` of 16 languages; thumbnail 768x288 → 512x256 (2 levels), `Thumbnail_1313.xds`;
- manifest: car zip added, string zips and `Textures.zip` updated; every zip listed.

Not done / to check in game:
- the car in game (nobody has launched it): FM3_Extracted_DEV must be the game Xenia opens;
- engines or drivetrains FM3 lacks become the donor's (reported), a make FM3 lacks stops the port, wheels FM3 lacks are converted but
  untested; torque curves, engine upgrade levels (FM3 has 8 where FM4 has 4: the car keeps FM4's 4), FM3's CSC / DSC / manifold
  upgrades are not made;
- digital dash, engine sound banks, exhaust and camera rows beyond the shared tables, `Unk1c` flag of some parts (majority rule);
- no UI: the window "Import Car From Another Forza" only knows FM4 as target.

Checked in game 2026-11-01 (Supra RZ '98): exterior OK, interior texture OK (after `Fm3Textures.ConvertSmoothBlock`), cockpit camera OK
(after `CarPortToFm3.CockpitCamera`).

## Cockpit camera, both directions (TO DO for FM3 → FM4)

FM3's eye = `CarPartPositions` part 4 + `CameraOverrides.CamCockpitOffset`; FM4's eye = part 9 + offset (an all-zero offset = the
game's default 0, 0.21, -0.51). Same physical point on the 315 cars both games share (mean difference X -0.003, Y 0.005, Z 0.006).
FM4 → FM3 is done: offset = part 9 + FM4 offset - part 4. **FM3 → FM4 still uses the old rule** (`CarCamera.SetSteeringWheelFromModel`:
part 9 from the model, offset X = 0, Y and Z as FM3 has them): it should be the inverse, offset = part 4 + FM3 offset - part 9
(part 9 from the model rule), so ported FM3 cars stop needing a hand edit of the camera. Not done yet.

## Resume here (2026-11-02, end of day)

### State
- FM4 -> FM3 port (`CarPortToFm3`): handling inverse, camera, sound conversion, engines/drivetrains/torque curves copied from FM4,
  FM4 DLC packs as a source (`PackRoot`), new makes (`PortMake`), wheel morph baked. All in `FM3_Extracted_DEV` only, nothing committed.
- FM3 -> FM4: inverse camera rule done (checked in game), `Encode` bounds bug fixed (0.066 mm).
- Six test cars in `FM3_Extracted_DEV` (rebuilt after the wheel fix; donor Supra): ALF_155_92, AUD_RS3_11 (Speed Pack, not base),
  CHE_CobaltSS_10, CIT_C1_11, FIA_Coupe_00, MG_MGA_58 (new make 98). User's first test: all OK except wheels and MG make strings/order.
- Rebuild: close Xenia, `bash E:\DEV\fm4dev\PortRefactor\fm4fm3\reset_dev.sh`, then `six.exe` (all six, or names as arguments).
  NOTE: during the last rebuild `xenia_edge.exe` was running (it was not at the earlier checks); it had ended when checked again.
  Restart Xenia and re-test. Always check `tasklist` for xenia* first.

### Wheels: cause and fix (done, to check in game)
- FM4 draws most wheels' lod0 rim at a 15-inch reference (YZ radius 0.194) and a 16-byte morph table (target first) resizes it to the car.
  The FM4 -> FM3 conversion dropped the table without applying it, so the rims stayed at 0.194 against LOD 1 rims of 0.2076 (155),
  0.2484 (Cobalt)... and 0.1935 (C1, already 15 inch: "slightly small" was the only one near right).
- FM3 stock wheels (`List_Wheels.IsStock = 1`) hold the true size in lod0 with header word@4 = 0. FM3 aftermarket wheels
  (`IsStock = 0`, 110 shared with FM4) all have lod0 at 15 inch, word@4 = 1 and the FM4 wheel has the table (110 of 110): FM3 has
  the same morph, flagged by that word, no table in the file (FM3 files round-trip byte-identical).
- Fix (`CarbinModelToMotorsport3.BakeWheelMorph`): fraction t = (LOD 1 inner_rim radius - lod0 base) / (target - base), lod0 = base +
  t * (target - base) (Cobalt t 0.415, 155 t 0.108, C1 0), bounds grown when needed, header word@4 and the float after it cleared.
  Checked: lod0 inner_rim radius = LOD 1's (0.2485 / 0.2486 on the Cobalt in the DEV wheel zip).
- Open: FM4's own wheel lod0 also holds a small `tire` material (FM3 has none in lod0); left in. If the tyre looks wrong in lod0, drop
  the lod0 `tire` subsections.

### MG make: name and order (NOT solved, needs the user's observation)
Checked: the make strings are correct in `List_CarMake.str` of the DEV zips (key 1F56 = "MG", profile key EA1F, same text as the pack, 16
languages); the row is `98 | MG | country 9 (United Kingdom, exists in FM3)`; the three logos are in Textures.zip; Data_Car.MakeID/MakeIcon are right.
Found: FM3's str file lists makes by IDS_DisplayName_<make id> in source order (rank); the base 47 makes are alphabetical, the later
ones (Alfa Romeo 54 ... Holden 59) are not, and MG got id 98 / rank 107 (last). Order by id or rank has 3 inversions in FM3, by key 23,
so the game probably sorts by text at runtime, but that is a guess. Other tables with a row per make: `Livery_DecalsSortOrder`
(59 makes in FM3, none for MG), `Rewards_CarPartDiscounts` (53 makes, none for MG): possible causes, not tested.
To ask tomorrow: where does MG appear in the make list (first, last, elsewhere) and what exactly is wrong with the name (blank, other
text, other language)? And what does "stringhe errate" refer to besides MG (car names? other makes?).

### Checked in game 2026-11-02 (after the rebuild)
- Six cars OK, wheels OK (lod0 rims), "as if made by T10". The MG make problem was NOT a port bug: the user had other DLC packs
  installed in the test game; with them removed MG (name, order, logos) is fine. So id 98 / rank 107 need no change and the
  Livery_DecalsSortOrder / Rewards_CarPartDiscounts rows are not needed. (Test the six cars WITHOUT other DLC installed.)

### UI (2026-11-02, compiled, not yet seen)
- Tools > Import Car From Another Forza > Into FM3 > From FM4 opens `CarPortFm3Form.cs` (own window, game banner "FORZA MOTORSPORT 3 / from
  FORZA MOTORSPORT 4"): FM4 folder + optional FM4 DLC packages, FM3 folder, car list, FM3 donor (default TOY_SupraRZ_98), Trial Run
  (temp folder + report) and Install in FM3 (refuses while Xenia runs, replaced files go to bak\). Menu routes live in `PortPairs.cs`.
- Create DLC Package for FM3: NOT done (menu entry greyed). Needs the FM3 DLC package format studied first.

### To do
- UI entry for the FM4 -> FM3 port (waiting for the user), commit (user: later; my files: CarPortToFm3.cs, CarbinModelMotorsport3.cs,
  CarbinModelToMotorsport3.cs, CarPort.Database.cs, DlcSource.cs (MergeRows internal), Fm3Textures.cs, docs).
- Not done: supercharger bank check (works in game), FM4 `HarmonicTunings` are dropped (FM3 has none), zero-throttle torque curve shape is
  approximate, other port pairs (FM3 -> FM2 etc.).
- Harnesses in `E:\DEV\fm4dev\PortRefactor\fm4fm3` (six.cs, toFm3.cs, ex.cs ...) and `...\fm3fm4cam` (wh*.cs wheel studies, dev.cs).

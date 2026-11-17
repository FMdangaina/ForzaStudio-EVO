# ST-02: Duplicate Car for FM3 and FM2 (analysis 2026-11-14)

> **State 2026-11-17: CLOSED** (2026-11-15, section "ST-02 state" and the in-game table further down; committed in
> 41aaff7). After it: PI, class and ratings of a copy computed like each game (`FM2/PI_AND_RATINGS.md`,
> `FM3/PI_AND_RATINGS.md`, `FM4/PI_AND_RATINGS.md`; commits 82df3d1, d5e3ae8). Sprint 2 is `ROADMAP.md` goal 21; the next
> item is `ST03_PART_IMPORT.md`. The file is a log in date order: the first sections are the analysis as it was.

Short-term sprint item ST-02. State on 2026-11-14 (morning): **analysis done, nothing coded yet.** Same options as FM4's Duplicate Car (plain copy,
own engine, own gearbox, names, make, year, Id; game folder or DLC pack). Harnesses `E:\DEV\fm4dev\ST02`: `dbmap.exe`
(tables, FM4 relations that hold, relations found by value), `own.exe` (rows each car owns, over every car), `q.exe` (SQL).

## How FM4 does it today (CarDuplicate.cs)

New Id above every car of the game and its DLC; `DbIds.RenumberCar` on a copy of the database moves the car's rows (car
columns, keys car * 1000 + n and what they point at in that range); the new rows are inserted; rows that only point at the
car (events) are skipped; own stock wheel (List_Wheels + WheelExceptions / WheelNormOffsets + zips); own strings (Data_Car.str
key); thumbnail; sound files under the new name (`<car>_CMT.xml`, engine `<engine>_ET.xml`); `CarEngine.Clone` /
`CarGearbox.Clone` for an own engine / gearbox; `DbIds.CheckCar` + `DbIntegrity.Compare(..., Motorsport4)` before anything
is written. Two FM4-only pieces: the relation map `GameDbSchema` (measured on FM4) and the Id limits.

## The databases (stock games, every car)

The database declares no foreign keys; relations were measured by value.

| | FM2 | FM3 | FM4 |
|---|---|---|---|
| Tables | 169 | 176 | 198 |
| Cars | 310 | 415 | 505 |
| FM4 relations that exist and hold | 125 (8 dangle, 205 absent) | 223 (22 dangle, 93 absent) | 230 (30 dangle) |

### FM3: FM4's layout

The car's rows are FM4's: `List_Upgrade*` (car * 1000 + n, 100 %), `List_AntiSwayPhysics` / `List_SpringDamperPhysics`
(car * 1000 + n, 100 %), `List_AeroPhysics` (owned rows car * 1000 + n, 207 / 207), `Data_CarBody` (car * 1000),
`CameraOverrides`, `ArcadeCarList`, `CarExceptions` (key = car), `CarPartPositions`, `Combo_Colors`,
`Combo_Colors_Exceptions` (no key), `Combo_Engines` (counter), `Data_Engine` / `Data_Drivetrain` (306 / 376 of one car, the
rest shared), stock wheel by `StockWheelID` (+ WheelExceptions, WheelNormOffsets). No `CarExceptions`-like FM4-only tables
beyond that. So FM3 = FM4's procedure with an FM3 relation map, FM3 Id limits and FM3's sound and strings.

### FM2: another layout

| Rows of the car | Key | What a copy needs |
|---|---|---|
| 19 `List_Upgrade*` tables, one per part (AntiSway, Brakes, Clutch, Differential, Driveline, Engine, Flywheel, FrontBumper, Hood, RearBumper, RearWing, RimSize, SideSkirt, SpringDamper, TireCompound, TireWidth, Transmission, Weight) | `Id` = car * 1000 + n (100 %), `Ordinal` = car | new car * 1000 + n |
| `List_AntiSwayPhysics`, `List_SpringDamperPhysics` (6 rows a car) | **counter** 1-1452, no declared key, `Ordinal` = car | new counter values; the copied `List_UpgradeAntiSway` / `List_UpgradeSpringDamper` rows point at them |
| `List_AeroPhysics` (347 rows of one car, 8 shared) | **counter** | owned rows: new counter values, bumpers / hood / wing rows re-pointed; shared rows stay shared |
| `Combo_Engines` | counter `EngineComboID` | new counter value |
| `Combo_Colors` (6 a car) | none (Ordinal + Sequence) | copied |
| `RarityPrice_Asia` / `_Europe` / `_Namerica` (1 a car each) | counter `ComboRarityID` | new counter values (without them the car has no price) |
| `TireMod_FrictionScale` (9 cars) | none | copied when the source has one |
| `CarExceptions`, `CarDiscountExceptions`, `ArcadeCarList` | none | see questions |
| Engine: `Data_Engine` (257 of one car, 62 shared), engine parts (`Id` = engine * 1000 + level), `List_TorqueCurve` (**counter**, not engine * 1000) | | plain copy: shared with the source; own engine: new EngineID, parts engine * 1000 + n, new curve counters |
| Gearbox: `List_UpgradeTransmission` / Clutch / Differential / Driveline / Flywheel are the car's own rows already | car * 1000 + n | own gearbox = the copy's own rows edited (no drivetrain table as FM3 / FM4) |
| Stock wheel: `Data_Car.StockWheelID` -> `List_Wheels` (+ `WheelNormOffsets`) | | as FM4 |
| No `Data_CarBody`, `CarPartPositions`, `CameraOverrides`, `Data_Drivetrain` | | |

Rows that only point at the car (game content, not the car's data): `EventParticipants`, `Races`, `Rewards_Arcade`,
`Rewards_CarGift`, `Rewards_CareerEvents`, `Rewards_DriverLevel`, `NewProfile_Arcade_Garage`.

### FM2 files found by the car's name

- `Media\cars\<car>.zip`, `Media\wheels\<wheel>.zip`;
- sound: `Media\Audio\Cars\EmitterData.zip\<car>_ED.xml` (324) and `Media\Audio\Cars\Engines\DSPTuning.zip\<car>_DSPTuning.xml`
  (351; it names the harmonic tunings and sound banks). A copy without them would be silent: both are copied under the new name.
- strings: `DisplayName` / `ModelShort` are `_&<id>` as in FM4 (format of FM2's string tables still to check).

## Plan

1. **Relation map per game** (the base of every database check): `docs/FM2/DbRelations.tsv`, `docs/FM3/DbRelations.tsv`
   from the measured relations (FM4 relations that hold there + same-name keys + torque curves), reviewed, embedded;
   `GameDbSchema` / `DbIntegrity` / `DbIds.CheckCar` take the game. FM4 keeps its map.
2. **FM3 Duplicate Car**: CarDuplicate with the game (Id limits, FM3 map, FM3 sound by name, strings), own engine / gearbox
   (`CarEngine` / `CarGearbox` on FM3's tables, which are FM4's), wheel. Tests on a copy of FM3's database: every car
   duplicated in memory, integrity check = zero errors, then one car in `FM3_Extracted_DEV` for the game.
3. **FM2 Duplicate Car**: a copy procedure driven by the measured ownership (counter keys renumbered, references
   re-pointed), sound and strings by name, own engine / gearbox on FM2's tables. Same tests, then `FM2_Extracted_DEV`.
4. DLC pack destination for FM3 / FM2: after 2 and 3 work in the game folder.

## Questions for the user

- FM2 `ArcadeCarList` (arcade availability and level), `CarExceptions`, `CarDiscountExceptions`: copy them for the copy
  (FM4's procedure copies ArcadeCarList and CarExceptions) or not?
- Rewards / starting garage (`Rewards_*`, `NewProfile_Arcade_Garage`) and race grids: never copied (FM4's rule). OK?

## State 2026-11-14 (night): plain copy coded, tested on every car, installed in DEV

- Relation map per game: `docs/<FM2|FM3>/DbRelations.tsv` (FM2 163, FM3 219), keys of the tables without a declared one
  `docs/<FM2|FM3>/DbKeys.tsv`, embedded; `GameDbSchema.For(game)` / `LogicalKey`; `DbIntegrity.Compare`, `DbIds.RenumberCar`,
  `CheckCar`, `CarColumns`, `KeyColumn`, `ChosenCarIdProblem` take the game (FM4 / FH unchanged). `integ.exe`: broken writes
  (duplicate physics / curve / combo keys, dangling physics / aero / car) are refused on FM2; FM3 / FM4 as before.
- `CarDuplicate` per game: FM2 counter-keyed rows of the car get new keys (`RenumberCounters`: physics, prices, engine combo;
  aero rows only this car uses are cloned, FM3 keeps them car * 1000 + n), the copy's rows are re-pointed; every row of the
  car's own tables is copied (arcade list, exceptions included), never events / races / rewards / starting garage; FM2 sound
  `<car>_ED.xml` + `<car>_DSPTuning.xml`; FM3 sound `<car>_CMT.xml` as FM4; game's spelling of file names; every manifest path
  found before anything is written. Engine / gearbox of its own and DLC pack: FM4 only for now (the window disables them).
- `dupall.exe` (Plan on every car, independent checks): **FM2 310 / 310, FM3 311 / 311 without any finding.**
- Installed 2026-11-14: `TOY_SupraRZ_98_ST02` in `FM2_Extracted_DEV` (id 1025) and `FM3_Extracted_DEV` (id 1316); on disk:
  0 integrity errors against the database before the copy, `PRAGMA integrity_check` ok. In-game check pending.
- A first FM2 write stopped at the manifest (harness passed F:/ with forward slashes) after writing the files: restored from
  its backups (FM2_Extracted_DEV = stock again but for the user's Mustang line), cause fixed (folder normalised, manifest
  paths checked before writing).
- Found, not ours: FM3_Extracted_DEV's earlier FM4 -> FM3 ports (ids 1002-1026...) break FM3's new rules (keys not above
  FM3's largest, 2 dangling List_UpgradeCarBodyFrontBumper aero ids 1263400 / 1263401).

## In game 2026-11-14: FM3 drives, no name; FM2 not visible

Harnesses `E:\DEV\fm4dev\ST02\strfix` (`sk` keys of a .str, `pk` / `pf` / `px` FM3 / FM2 package contents, `cl` key clashes
of the copy with the packs' merge databases).

- **Names (FM3 and FM2)**: the copy's keys are right in the base tables (FM3 EN `Data_Car.str` FFFE = "Supra RZ", FFFD =
  "Toyota Supra '98"). But every car DLC pack of FM3 and FM2 ships a **full** `Data_Car.str` (FM3 924-1096 entries, base cars
  included) in its StringTables layer: with DLC installed the game reads that table, not the base one, so a key added only to
  the base table has no text. Same cause as the MG make of the FM4 -> FM3 port (fine once the DLC was removed, 2026-11-02).
  FM4's Duplicate shows its name with DLC installed, so FM4 reads its tables another way (not checked).
- **FM2 visibility**: no clash of the copy's counter keys (physics, prices, engine combo) with any FM2 DLC merge; the two
  69 KB packs (Pre-order, Limited Edition) are licences without files. FM2's official DLC car ids end exactly at **1024**
  (620-658, 1001-1024); the invisible cars are 1025 (this copy) and DLCBeta's 1638 / 1639. Hypothesis: FM2 shows car ids
  up to 1024 only. Not tested yet.
- Next: FM2 copy re-installed with an id <= 1024 (free: 659-1000) to test the cap; names through an own pack with the highest
  priority whose `Data_Car.str` = base + every installed pack's table + the copy's keys (original DLC never written).

## 2026-11-14 (day): FM2 StockGarage, FM2 Id window, names pack

- **FM3 name confirmed**: with the DLC moved away the copy shows its name (user). Cause = the DLC's whole string tables.
- **FM2 invisible = StockGarage** (not the Id): FM2 lists its cars from the table `StockGarage` (one row per shown car,
  303 of 310 base cars; the 7 without are unlock / special cars). The official DLC fill it in `Media\LicenseMasks\*.sql`
  (`INSERT INTO StockGarage SELECT ... FROM Data_Car LEFT JOIN ...`); that query, copied as it is in `Fm2StockGarage.cs`,
  rebuilds the 303 rows identical. Duplicate writes the copy's row (after the strings, so its names are the new keys) when
  the source has one; Uninstall removes it. Probably also why DLCBeta's cars never showed.
- **FM2 car Ids 659-1000** (`DbIds.Fm2FirstFreeCarId` / `Fm2LastFreeCarId`): between the base game (617) and the official
  DLC (620-658, 1001-1024). The "no car above 1024" idea is not proven (StockGarage explains both cases); the window is safe
  either way. DbIntegrity accepts car keys in that window (car columns, car * 1000 + n) below the DLC's largest.
- **Names pack** (`DlcPackage.Names.cs`, user: automatic, only the str files, FM2 / FM3 / FM4): one STFS per game in Xenia's
  content folder (marker `ForzaStudioNames.txt`), `StringTables_pri_999.zip` = for every table an installed DLC replaces,
  the game folder's table + every installed layer in priority order; FM3 adds its MultiDiscContentPacks row (merge) and a
  no-op licence mask, FM4 a `.puboffer`. Rebuilt after Duplicate (game folder) and Uninstall; removed when no DLC replaces
  a table. `StringLayer` now also reads zips that `zipmount.xml` mounts at the string tables (FM2's `DLC7Overrides1_pri_156.zip`)
  and skips the names pack. Duplicate's new string keys avoid the keys of the installed DLC tables too.
- Trial builds (`E:\DEV\fm4dev\ST02\names`, `nb.exe`): FM3 7 tables x 16 languages, EN Data_Car 1121 entries (copy FFFE/FFFD
  + every DLC car); FM2 3 tables x 14 languages, EN Data_Car 712. Installed in Xenia: FM3 `FCAED666...4D`, FM2 `E15D5178...4D`.
- In DEV: FM2 copy reinstalled as id 659 (`reset_fm2_dev.sh` puts FM2_Extracted_DEV back), 0 integrity errors.
- dupall FM2 with the new build: "rows differ in List_UpgradeEngine*" on cars whose engine id equals their car id = the
  test counting engine parts (engine * 1000 + level) as car rows; the copy shares the engine, same as the old build.
- In-game checks pending: FM2 copy visible + name with DLC; FM3 name with DLC (DLC back from `og`); names pack mounts.

### In game 2026-11-14 (afternoon)
- Names OK with the names pack (FM3 and FM2); FM3 Supra copy OK; FM2 copy shows in career.
- FM2 Arcade: only the original Supra. Arcade lists `NewProfile_Arcade_Garage` (235 base cars) plus the `Rewards_Arcade`
  unlocks; the official DLC never add theirs. **User rule: a car the editor duplicates or adds to FM2 is always enabled in
  Arcade** -> `Fm2StockGarage.EnableArcade` (Duplicate), Uninstall removes the row. Reinstalled in DEV (id 659), check pending
  (the table's name says "new profile": a new profile may be needed to see it).
- dupall FM2 finished: 310 planned, 0 refused; the 87 cars with findings are exactly the cars whose Id equals an engine id
  (engine parts counted as car rows by the test): no real finding.
- User: back to id 1025 (as before). The 659-1000 window (DbIds.Fm2*, DbIntegrity exemption, form default) is REMOVED: FM2 new
  cars again take the first id above FM2 + its DLC (1025). Reinstalled in DEV as 1025 with StockGarage + Arcade rows (0
  integrity errors); FM2 names pack rebuilt (`45FEEFD6...4D`, replaces `E15D5178...`). In game this tells whether ids above
  1024 show (StockGarage being the cause of the first invisible copy).

## ST-02 state 2026-11-14 (evening): FM2 and FM3 Duplicate OK in game (user)

User: FM2 and FM3 duplicating OK; the "one Supra in Arcade" was the original Supra being locked in FM2's Arcade, not a bug.
The Arcade rule (editor cars always in `NewProfile_Arcade_Garage`) stays (user rule). FM2 copy id 1025 works: no 1024 cap.

Sprint automatic tests (`E:\DEV\fm4dev\ST02\tests\t.exe`, on the DEV folders holding the first copy), FM2 and FM3 all PASS:
- second copy (planned, nothing written) gets its own Id (FM2 1026, FM3 1317), its rows shared with neither the first copy
  nor the source; first copy's and source's rows unchanged; DbIntegrity 0 errors / 0 warnings; CheckCar of both copies clean;
  `PRAGMA integrity_check` ok;
- originals untouched: the source's car zip, wheel zip and Data_Car row identical to the stock game's;
- rollback: a refused duplicate (name taken, source missing) writes nothing (database, manifest, string zip hashes unchanged,
  no file written).
- every car: dupall FM2 310 / FM3 311 planned, 0 refused (FM2 engine-part counts = test artefact, see above).

Left for later (not in the sprint's criteria): own engine / own gearbox for FM2 / FM3 (the window disables them), DLC pack
as destination for FM2 / FM3, Uninstall of FM2 / FM3 copies (CarUninstall is FM4-only: its Id rule).

## 2026-11-14 (afternoon): by folder, Uninstall, own engine / gearbox, DLC packs for FM2 and FM3

User: Duplicate works on the OPEN folder's game only (FM2, FM3, FM4; FH1 / FH2 say "not available yet"); every destination
(game folder, pack in Xenia's content folder, package file) for every game; own engine / gearbox; sound and names.

- **Menu** (`ArchiveEditorForm.ShowDuplicateCar`): no fallback to the port window's FM4 folder; Xenia's content folder of
  THAT game's title (`DlcPackage.XeniaContent(xenia, title)`); FM2 / FM3 ids also avoid the installed packs' cars.
- **Uninstall FM2 / FM3** (`CarUninstall.Copy.cs`): by the game's relation map: car rows (car columns, car * 1000 + n keys,
  StockGarage, Arcade garage), then the rows they pointed at when the editor made them (key above the game's and its DLC's
  largest) and no car uses any more, with the rows hanging off them (extension rows keyed by the target, an engine's parts);
  strings, zips, thumbnail, sound files (FM2 `_ED` / `_DSPTuning`, FM3 `_CMT`, own engine `_ET`). Only a car above the BASE
  game's largest Id (`GameIdLimits.MaxCarBase`: the game folder's database is the base game's). Test `ST02\uninst\un.exe`:
  after Uninstall the DEV folder equals the backup taken before the copy: FM2 169 / 169 tables, FM3 176 / 176, string tables,
  manifest, sound and texture zips identical, zips gone.
- **Own engine / gearbox FM3**: `CarEngine.Clone(..., game)`, `CarGearbox.Clone(..., game)` (FM3 = FM4's tables). Fixed for
  FM3: curve columns by name (`CurveColumns`; FM3 has EngineID before TorqueScale, no ZeroThrottleTorqueScale, 151 values),
  curve ids = counters with the new EngineID, grid capacity from the table, `UpdateSimulation` / `CarPerformance` read curves
  by name; FM3 / FM2 Sim numbers = the source's times the computed change (no FM4 fit). Blow-off only FM4 (FM3's tuning has no
  slots).
- **Own engine / gearbox FM2** (`CarEngine.Fm2.cs`): every part level copied under the new EngineID (CamValves = camshaft,
  Block, Exhaust, ..., Turbo), curves under new counters, redline scales the levels' rpm, idle = StallRPM, the designed curve on
  every level; aspiration / boost / sound stay the source's (dialog greys them); gearbox = the copy's own List_UpgradeTransmission
  rows (7 gears at most, Data_Car.StockGears). `EngineDialog` / `GearboxDialog` read the game's tables.
- Test `ST02\engine\eng.exe <root> change|same`: every car planned with own engine (+500 rpm) and gearbox (final x 1.1):
  curves (EngineID, marker, NumTorqueValues = NumRPMEntriesArray), source untouched, integrity, CheckCar; "same" = Sim, times
  and PI unchanged. First 170 cars of each game clean (then the harness ran out of memory: rerun in chunks).
- **DLC packs FM3**: `RunPack` stages the copy (`GameOverride`), `DlcPackage.BuildFolderFm3` / console package with FM3's title;
  merge check with the game's rules. **FM2** (`DlcPackage.Fm2.cs`, NOT checked in game): folder `DLC<offer>_FS` + Xenia header,
  `DLC<offer>_pri_66\media\...` loose files, merge `<offer>0000_merge.slt` without offer rows, `DLC<offer>Overrides1_pri_<P>.zip`
  strings, `LicenseMasks\00000001.sql` (IsPurchased + the official StockGarage INSERT, `Fm2StockGarage.LicenseInsert`).
  DlcSource reads an FM2 folder pack (merge with RarityPrice_* + licence mask; `DLC<n>_pri_<P>` folder layer); DlcValidator:
  FM2's `<car>.carbin` (no LOD files), no Data_Car.ModelId. Both packs validate OK (FM3 12 / FM2 2 model warnings only).
- **Names pack**: rebuilt after a pack is built in Xenia's folder too; its priority = above every installed layer (FM2's
  DLCBeta strings are 999; a new pack takes the highest + 1).
- Installed for the in-game test 2026-11-14: FM2_Extracted_DEV `TOY_SupraRZ_98_ST02` (1025, own engine 473 + gearbox), FM3_Extracted_DEV
  `TOY_SupraRZ_98_ST02` (1316, own engine 773 + drivetrain 755), FM3 pack `4D53084D0CCF0065` (offer 101, TOY_SupraRZ_98_PK 1317),
  FM2 pack `DLC71_FS` (offer 71, TOY_SupraRZ_98_PK 1026, own engine + gearbox); names packs FM3 `A6B65F27...4D`, FM2 `FB746648...4D`.
- eng.exe in chunks of 100 (2026-11-14): **FM3 311 / 311 and FM2 310 / 310 without any finding**, both modes (change:
  redline +500, final drive x 1.1; same: Sim numbers, 0-60, top speed and PI unchanged).
- FM2 "locked" copies: the career dealer sells a car from its `Rewards_DriverLevel` TypeId 3 rows (region, driver level);
  copies now get the source's (`Fm2StockGarage.CopyDealerLevels`), gift rows (TypeId 1 / 2) not. FM2 test car is now
  `FOR_MustangGT_05_T9000` (game folder, redline 9000, DSPTuning audio_rpm_max follows); Supra tests removed from FM2.

### FM2 DLC in game: parked 2026-11-14
- Without FM2's **title update** FM2 loads no car DLC at all (log: only the two licence packs mounted). With TU4
  (`content\...\4D5307EA\000B0000`) the official packs, DLCBeta and the names pack are mounted.
- Our pack v2 (`DLC8`: DLCBeta layout, IsDLC 1, ContentId 51, id 659) was gone from the content folder at the TU4 session (not
  enumerated). To redo: rebuild it, check the log mounts it as DLCBeta (`DLCRoot<n>:\media\dlczips\...`), test in game.
- Still open: the FM2 game-folder copy (Mustang 660, dealer rows, id in 659-1000) shows but is locked.

### FM2 DLC pack v3: zips in deflate (2026-11-14 evening)
- `DLC8` rebuilt with TU4 installed: FM2 enumerates and mounts it (`DLCRoot10`, opens `dlc8overrides1_pri_1000.zip`), then
  hangs on its loading screen (no file opened after the last mount).
- Harness `E:\DEV\fm4dev\ST02\offm` (read-only) lists every FM2 pack's merge. Our merge had 5 tables no official merge
  nor DLCBeta has (`ArcadeCarList`, `CarExceptions`, `Combo_Engines`, `NewProfile_Arcade_Garage`, `WheelNormOffsets`): now
  left out. Still hung.
- **Zip compression** (`offm\zl2`): every FM2 zip (game, DEV, official packs) is **deflate (8)**; FM3, FM4, FH1, FH2 are
  LZX (21). Our pack zips were LZX. FM2 also reads **only zips** in `Media\DLCZips`: the loose `DLCBeta_pri_65` and
  `DLC8_pri_65` folders were never opened (the official packs' `dlc<N>_pri_6x.zip` are).
- Fixes: `ForzaZip.KeepGameMethod` (string zips: LZX where the game has LZX, else deflate), `WriteStoredZip(..., deflate)`,
  `CopyForConsole` deflate for FM2, `DlcPackage.ZipLayerFm2` (the layer as `DLC<offer>_pri_65.zip`, after the pack DB
  check), `DbIds.ZipMerge` (merge inside a folder pack's layer zip). `DLC8` rebuilt: layer 25 deflate entries, strings 14,
  names pack `AFC8B405...4D` deflate. In-game test pending.
- To check: `ModPack.cs` FM2 mod packs still write LZX zips. Arcade for FM2 DLC cars (official ones are not in
  `NewProfile_Arcade_Garage`) after the pack loads.
- 2026-11-14: **FM2 DLC pack OK IN GAME** (user: "dlc fm2 ok!", folder `DLC8`, deflate zips).

### Duplicate from a package car: disc 2 / DLC source (2026-11-14 evening)
`CarDuplicate.Package.cs` (`CarDuplicate.SourcePackage`; the Duplicate window lists the installed packages' cars as
`<car>   [<pack name>]`). The package is only read:
1. stage: gamedb.slt = the game's + the package's merge (`ModCarDb.Apply`); car / stock wheel / tyre zips taken out of the
   package (`DlcSource.ExtractMedia`, deflate for FM2); its thumbnail and sound files (every package file under
   `media/audio` naming the car or its engine) added to staged copies of the game zips that hold them;
2. the usual Duplicate on the stage; names from the package's `Data_Car.str` when none is given;
3. clean: package files out again; database = game's + the copy's rows + the package rows the copy points at (relation map,
   never another car's rows). A pack-only engine / drivetrain gets an own clone with the same values (the DLC's ids would
   clash with the pack); a shared pack row (FM3 `List_DigitalGauges` 252) is kept identical, with the pack's key;
4. into the game folder (backups, manifest lines; flags of the game folder's cars: FM2 IsDLC 0 / IsPurchased 1 /
   ContentId 0, FM3 IsInstalled 1) or into a DLC pack / STFS file (the usual pack build).
Loose package sound banks (.fsb) the copy names stay with the copy. Headless (harness `ST02\dpk`, slim roots
`dpk\fm2`, `dpk\fm3`, `reset_fm2.sh` / `reset_fm3.sh`): FM2 FER_F40Competizione_DLC (March pack) to game + STFS;
FM3 HON_CivicR_04 (disc 2 0013) to game, FOR_TaurusSHO_10 (March DLC) to game + STFS: all checks pass.
Not done: **FM2 / FM3 mod packs** (`ModPack.cs` is FM4 only: title, merge name, offer row, pri_999 proven only in FM4).
Open: an FM2 copy of a DLC car gets no dealer rows (DLC cars have none).

### Restore + regression (2026-11-14 evening)
- FM2_DEV and FM3_DEV back to stock byte for byte (`restore\devdiff.sh`), every test pack removed from Xenia's content;
  all moved to `E:\DEV\fm4dev\ST02\restore\bak`.
- ST-02 `dupall3` (dupall + FM2 rules: Arcade 1 row, dealer TypeId 3 rows = the source's): FM2 310/310, FM3 311/311 clean.
  (`dupall.exe` is an OLD build: 59 false engine-table findings; use dupall2/dupall3.)
- ST-01 `st01`, `rt`, `seethru` (`ST01\reg`): identical to the 2026-11-14 baseline; rt / seethru FM2 310, FM3 3421 clean.

### In-game check list (from the app, after the restore)
| # | Game | What | Expect |
|---|------|------|--------|
| 1 | FM2 | Rename Material: Mustang GT mirrorL -> chrome | chrome mirror |
| 2 | FM2 | Rename Material: an opaque part -> glass | see-through |
| 3 | FM3 | Rename Material: 500 Abarth front bumper -> chrome | chrome |
| 4 | FM2 | Duplicate Mustang GT to the game folder, own engine 9000 | car + name, Arcade, career dealer |
| 5 | FM2 | Duplicate Mustang GT to a DLC pack | car + name |
| 6 | FM2 | Duplicate FER_F40Competizione_DLC (March pack) to the game folder | car, name, sound |
| 7 | FM3 | Duplicate Supra RZ to the game folder, own engine + gearbox | car + name, drives |
| 8 | FM3 | Duplicate Supra RZ to a DLC pack | car + name (with the official DLC installed) |
| 9 | FM3 | Duplicate HON_CivicR_04 (disc 2) and FOR_TaurusSHO_10 (March DLC) to the game folder | car, name, sound, gauges |
| 10 | FM2 / FM3 | Uninstall every copy | DEV back to stock (devdiff) |

### TO CHECK BEFORE ST-03 (user, 2026-11-15)
- **FM3 own engine rev glitch**: user duplicated the Audi #1 RS6 with a custom V12 own engine; in game the car drives,
  but when the revs go up there is a glitch (sound or engine, to describe). Investigate before moving to ST-03:
  own engine values vs the source (redline, torque curve end, rev limiter), sound rpm range (FM3 has no DSPTuning like
  FM2: check what the sound uses as max rpm), gearbox vs new redline.
- **Cause found + fixed (2026-11-15)**: FM2 / FM3 keep the zero-throttle curve as a row of its own
  (`TorqueCurveZeroThrottleID`). In both games every camshaft level has its own, with NumTorqueValues =
  the level's `NumRPMEntriesArray` (10 cars per game, `ST02\rs6\zc.exe`, no exception; v0..v8 identical, -2.63 at the end).
  `CarEngine.RebuildCurves` rebuilt only the full-throttle curves on the new grid: the RS6 copy had 109 entries but a
  shared 81-value zero curve -> zeros above 8000 rpm = no engine braking (same on FM2 Mustang 9000).
  Fix: `StretchZeroCurve` - each level its own zero curve, the source's stretched (v0..v8 kept) to the new length.
  Test `ST02\engine\eng2.exe` (eng + zero checks, redline +2400): old exe 10/10 cars wrong FM2 and FM3, new exe
  FM2 / FM3 change + same 10/10 clean, FM4 10/10 clean. In-game re-check pending.

### Package file in the wrong game's Xenia folder (FIXED, closed by user 2026-11-15)
- **Found**: Xenia content had "#1 Champion RS 6" (FM3, title 4D53084D) also in `4D530910\00000002` (FM4) and
  "sr4" (FM4) also in `4D53084D\00000002` (FM3). Byte-identical copies of the right ones: the 2 wrong copies were
  deleted (user OK, Xenia closed). Every other package in every title folder checked (title id at 0x360): right place.
- **Cause**: Duplicate Car "as a DLC package file" opened its folder dialog on the single FM4 setting
  `CarPort.XboxOut` for FM2, FM3 and FM4, and nothing checked the chosen folder's title. "DLC pack folder (Xenia)" and
  "game folder" were already right (folder from the open game's title id).
- **Fix** (built; closed by user 2026-11-15; uncommitted, commit via the diff chat):
  - `DlcPackage.ForeignTitleFolder(folder, title)`: a path segment `4D53xxxx` other than the pack's title = refused,
    with a message, nothing written. Used by Duplicate Car, the port to FM4 and the port FM4 -> FM3.
  - `DlcPackage.PackageOutputStart(key, title)`: last folder of THIS game (`CarPort.XboxOut` FM4,
    `CarPort.XboxOut.FM3`, `CarPort.XboxOut.FM2`), else the game's Xenia content folder; never another game's.
  - Duplicate's final message gives the console path with the game's own title (it always said 4D530910).
  - `DlcPackage.XeniaContent`: with Settings at `...\content\0000000000000000` and the title folder not there yet, it
    built `...\0000000000000000\content\0000000000000000\<title>\00000002`; now `...\0000000000000000\<title>\00000002`.
- **To test in app**: Duplicate an FM3 car as package file -> dialog opens on `4D53084D\00000002`; pick
  `4D530910\00000002` -> refused. Same for an FM4 car and the FM3 port window.

## 2026-11-15: points 7 (FM2 / FM3 mod packs) and 8 (FM2 car pack as STFS file)
User: FM2 = disc 1 + DLC; FM3 = disc 1 + disc 2 (as FM4) + DLC. Arcade fixed and DLC-car copies visible (TU) = closed.

**7. Mod packs for FM2 and FM3** (`ModPack.cs`, `ModCarDb.cs`, `ArchiveEditorForm.PackageCars.cs` / `.ModPacks.cs`). Same workflow as
FM4 (Open Car Model of a disc 1 / disc 2 / DLC car -> a copy; Save writes a mod pack; File > Database of This Car), laid out as the
names pack (STFS, proven in both games) and the game's own packs:
- FM3: STFS title 4D53084D; `Media\DLCZips\<offer hex4>_pri_999.zip` (LZX, lower case) with `media/db/patch/<offer>005_merge.slt`
  = the pack row (MultiDiscContentPacks, always) + the car's rows; `Media\LicenseMasks\00000001.sql` (no-op). Offer = FreeOfferFm3.
- FM2: STFS title 4D5307EA; `Media\DLCZips\DLC<offer>_pri_999.zip` in DEFLATE; `<offer>0000_merge.slt` only when it has rows; no
  offer row; LicenseMasks 00000001 + 00000002 (no-op). FM2 has NO key and NO index in any table: its merges only ADD rows. So a changed
  game row (Data_Car Year...) can NOT go in an FM2 mod pack (it would be a second row with the same Id): left out + WARNING; new rows
  (a new paint, Combo_Colors Ordinal + next Sequence) go in. Never in an FM2 merge: StockGarage, Rewards_DriverLevel, ArcadeCarList,
  CarExceptions, Combo_Engines, WheelNormOffsets, NewProfile_* (hang, 2026-11-14).
- Car rows via the game's relation map (`Owned(db, car, game)`). Not standalone: a disc 2 / DLC car's mod pack needs the original.
- File > Mod Packs lists the open game's packs (FM2 / FM3 / FM4).
- Test `ST02\modpack\mp.exe` (test content folder, never Xenia): FM3 10 disc 1 cars + 3 disc 2 (0013) + 3 DLC (One-77, R8 LMS, RS6),
  FM2 10 disc 1 cars + 3 DLC (March pack): ALL PASS (marker, title id, zip name/method/lower case, car files back byte for byte,
  merge rows, FM3 pack row, FM2 forbidden tables absent, merge applied = integrity clean, List / Remove).
- UNPROVEN in game: priority 999 over a disc 1 car zip in FM2 / FM3 (proven in FM4 only); FM3 pack row with empty name keys.

**8. FM2 car pack as an STFS file**: already in Duplicate ("as a DLC package file (STFS)", BuildConsolePackage TitleIdFm2 +
deflate). Checked headless (`ST02\pack\pk.exe ... file`): LIVE 4D5307EA, `DLC8_pri_65.zip` 26 entries all deflate (car, wheels,
_ED.xml, _DSPtuning.xml, 80000_merge.slt), `DLC8Overrides1_pri_1000.zip`, xmls, LicenseMasks 1 + 2 = the DLC8 folder layout proven
in game, as one file. In game: choose Xenia's FM2 content folder (...\4D5307EA\00000002) as the output folder. Not tried in game.

## 2026-11-15: final in-game check (user)
| # | Test | Result |
|---|---|---|
| 1 | FM3 Audi #1 RS6 V12 own engine as DLC pack (rebuilt after the zero-curve fix) | OK in game, no rev glitch (buggy pack 99284C...4D moved to ST02\restore\bak\2026-11-15_rs6_buggy) |
| 2 | FM2 Mustang GT 05 own engine redline 9000 (game folder) | OK in game, no rev glitch |

## 2026-11-15: FM2 car Id above 1000, FM2 aspiration / boost / sound
- Car Id: 659-1000 first, then 1025 and above with a `WARNING:` line (1001-1024 = official DLC, never used). `DbIds.Fm2UsableCarId`,
  `Fm2CarIdWarning`; Duplicate, FM2 pack, DbIntegrity and the form follow.
- Own engine, FM2 (`CarEngine.ConvertAspirationFm2`): forced induction rows (Turbo, DSC, CSC, Intercooler; Id = engine * 1000 + n)
  come from the FM2 engine of the chosen aspiration with the closest boost (natural: closest Disp_cc). Game rules checked on the DB:
  turbo / DSC stock MaxScale / ZeroRPMScale = 1 + bar / 2; CSC RedlineRPMScale = 1 + bar / 2, its ZeroRPMScale (238/243 lower) stays;
  natural engines keep only non-stock conversion rows. Turbo PowerMaxScale x (copy's / pattern's EngineGraphingMaxPower). Same
  aspiration + other boost: scales x (1 + new / 2) / (1 + old / 2). Aspiration 7 (turbo + SC) not offered (no FM2 engine has it).
- Sound, FM2: the DSPTuning file of the car chosen in the dialog (Data_Engine.MediaName = the car's, 321/321); EmitterData stays
  the source's; audio_rpm_max/min follow the copy's redline/idle against the sound car's; turbo: TurboVolume of every level = the
  user's (0.5 where 0). Data_Engine.MediaName of the copy stays the source's.
- Engine type: Layout / Cylinders were already editable; FM2 has no Diesel / Carbureted / Rotary columns (rotary = Layout Rotary).
- Harness E:\DEV\fm4dev\ST02\asp\asp.exe (10 random FM2 cars x 3 changes): 30/30 OK (integrity, CheckCar, stock rows, scales).
  In-game check pending.
| 6 | FM2 empty engine name: no "Give the engine a name" popup on the Mustang own engine | OK in app |
| 3 | FM3 Duplicate to game folder, Toyota Supra SZ, own engine made naturally aspirated + own gearbox | OK in game |
| 4a | FM3 Duplicate from disc 2 package (HON_CivicR_04 -> csc copy) to game folder | OK in game |
| 4b | FM3 Duplicate from March DLC (FOR_TaurusSHO_10) to game folder | OK in game (names typed by hand). BUG: Display/Model name not prefilled - form read names only from the game strings; FIXED: DuplicateCarForm reads the source package EN strings first (SourceName). App recheck pending |
| 4b-fix | Taurus SHO names prefilled in Duplicate form after the fix | OK in app |
| 5 | FM2 Duplicate from DLC (MER_CL600_DLC1 from DLCBeta -> MER_CL600_V8 id 660) to game folder | SUSPENDED (user). Visible in Arcade but LOCKED: the copy had no ArcadeCarList row and no Rewards_DriverLevel TypeId 3 rows (DLC sources have none; disc 1 copies inherit them; harnesses checked DB rules, not unlock). Fix coded: FM2 game-folder copy gets ArcadeCarList Level 0 + dealer rows level 0 per region when the source has none (Fm2StockGarage.EnsureArcadeList / DefaultDealerLevels). Also open: FM2 DLC names not prefilled in the Duplicate form |
| 5b | FM2 Duplicate from DLC (FER_F40Competizione -> FER_F40Competizione_DLC_2 id 661) to game folder | Works in game (user put it in a race by hand), STILL LOCKED. DB checked: ArcadeCarList 661 Level 0, dealer rows regions 1-3 level 0, NewProfile_Arcade_Garage row = same as an open disc 1 car -> the new rows do NOT unlock it. OPEN (user: maybe forbid FM2 game-folder install of DLC sources; a DLC pack destination has no lock). Hypothesis: NewProfile_* only seeds a NEW profile, the existing save keeps its unlock list - to test with a new profile |
| 5 | FM2 copies in the game folder locked in Arcade | CLOSED (user 2026-11-15): GAME LIMIT, not ours - a NEW profile shows the copy unlocked; an existing save keeps its own car list. Duplicate plan now shows "NOTE (FM2): ... only in a NEW profile" for FM2 game-folder destinations (CarDuplicate.Fm2ProfileNote). ArcadeCarList/dealer rows for DLC sources kept (they match an open disc 1 car). FM2 DLC names prefill still open |
| 7 | FM2 ST-01 opaque -> glass: original Mustang GT 05 hood renamed to glass | OK in game (Mustang to restore to stock) |

### RESUME HERE (end of 2026-11-15)
Done in game: 1, 2, 3, 4 (+ FM3 names fix), 5 (FM2 lock = existing profile, game limit, plan note), 6, 7, 8a, 8b, 9, FM2 DLC names prefill.
Next, one at a time (Xenia closed before any write):
- 8a OK IN GAME 2026-11-15 (lod0 body -> chrome shows): FM3 mod pack on a disc 1 car (Supra RZ 98, material -> chrome): priority 999 must win; game folder unchanged.
- 8b OK IN GAME 2026-11-15 (Tom's MR-S DLC, mirrors -> chrome, replaces original): FM3 mod pack on a disc 2 / DLC car (Civic R 04).
- 9 OK IN GAME 2026-11-15 (GTO 04, new green paint): FM2 mod pack + new paint via Database of This Car.
- 10 OK IN GAME (user 2026-11-15, "done many times"): FM2 Duplicate as STFS file into the FM2 content folder; then Uninstall every copy on FM2 and FM3.
  - 2026-11-15 test 10 found: FM2 own engine turbo -> natural gave PI 999 in game (PT Cruiser); plain copy, own turbo engine and redline-only change OK. Cause = turbo/intercooler rows copied from a pattern engine (Dino 246, nearest displacement). FIX (variant A, built, in-game check pending): ConvertAspirationFm2 to natural drops every induction row and adds none (78/199 FM2 natural engines, M3 E92 DLC). FM3 checked: natural = stock Manifold row, already made (Supra SZ 774 OK). Harnesses ST02pi (read-only).
- Fix OK IN APP 2026-11-15: FM2 DLC car names not prefilled - DlcSource.StringFiles now also reads the FM2 DLC<n>Overrides zips (<LANG>/Data_Car.str).
- Cleanup DONE 2026-11-15 (user removed copies + mod packs on FM2/FM3; leftover DEV files restored from stock, old ones in ST02\restore\bak_2026-11-15_final). ST-02 CLOSED.
- (old note) Cleanup: restore FM2 original Mustang GT 05 (hood -> glass from test 7); FM2_DEV copies 660 (CL600) + 661 (F40), FM3_DEV copies (Supra SZ, Civic csc, Taurus) via Uninstall in test 10; devdiff to confirm DEV == stock.
- Then: ST-02 closed -> diff chat commit -> ST-03 (part import, sprint doc section 6).
Uncommitted new code today: DuplicateCarForm.SourceName (package names), Fm2StockGarage.EnsureArcadeList/DefaultDealerLevels, CarDuplicate.Fm2ProfileNote.

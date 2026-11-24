# Port FH1 / FH2 -> FM3: study (started 2026-11-21)

Asked by the user on 2026-11-21: "procediamo con i port, parti da FH1/2 verso FM3. Cerchiamo di capire se FM3 ha gomme
diverse per auto e come funzionano i suoi digital dash." This lifts the FM3 pause (2026-11-03) for this port.
Built and closed in game since (sections 8-21). Related: `PORT_ALL_GAMES.md` (matrix, 12 of 20 on 2026-11-24), `PORT_FM4_TO_FM3.md` (the FM3 writer),
`ANARK_DASH.md` (dash format). Harnesses: `E:\DEV\fm4dev\FHtoFM3` (read-only on the game folders).

## 1. Tyres: FM3 has no tyres per car

- `Media\Wheels` of FM3 (retail and DEV folder): 389 zips, the tyres are `tirea.zip`, `TireB.zip`, `TireC.zip` only.
  FM4 has those three plus `Tire<A|B|C><car>.zip` for five cars (CIT_C1_11, HUM_H1ALpha_06, PEU_107_11, TOY_Aygo_11 and
  the `_SLOD` sets).
- Executables (`XexTool -b`, `FHtoFM3\xex\fm3_base.exe` against `Tracks\xex\fm4_base.exe`): both hold the texture paths
  `game:\media\wheels\tire%c\tread%c_CLR.xds` (one letter, no car name). FM4 also holds the lone strings `Tire` and
  `LoadTiresOnly`, the pieces of the per-car name; **FM3 holds neither.** So FM3 has no way to name a tyre zip after a car.
- Earlier finding (FIA 595 work, 2026-11-07): no small custom tyre in FM3, its second disc or its DLC.
- Consequence for a port into FM3: the Tires choice of the FM4 port window (Normal / Offroad / Small / Offroad Big)
  has no FM3 form. Every FM3 car draws the generic tyre of its letter; the size follows the database
  (`FrontTireWidthMM`, aspect, rim), as for FM3's own cars. An offroad tyre could only replace the generic one for
  every car, which is not a port's job.
- Not measured: what picks the letter A / B / C in FM3, and whether FM3's tyre model stretches with the rim as FM4's
  morph table does (FM3 aftermarket wheels have a 15 inch LOD 0 and a morph flag, `fm3-dlc` notes).

## 2. Digital dash: known, and the Horizon dashes convert

How FM3 keeps a dash (measured 2026-11-03, `PORT_FM4_TO_FM3.md` section 4):

- `Data_Car.GaugeID` -> `List_DigitalGauges` (GaugeID, MediaName `Dash_<car>`) -> `Media\DigitalGauges\Dash_<car>.zip`
  with `dash_<car>.fbf / .bgf / .bsg` (151 zips in FM3). `GaugeID -1` = no digital dash.
- Its pictures are not in the dash zip: `Media\UI\Textures.zip`, `UI4/Dashboards/<CAR>/<name>.xds`, named in the .fbf as
  `GAME:\MEDIA\UI\TEXTURES\UI4\DASHBOARDS\<CAR>\<NAME>.TGA`. Shared `MFD_*` folders serve several cars.
- FM4, FH1 and FH2 keep the dash inside the car zip (`digitalgauge\dash_<car>.*`), pictures included.
- Format: the same Anark files in all four games; the .fbf version differs (FM3 0x3EE, FM4 0x3EF, FH1 / FH2 0x3F0),
  and so do the text tails and the font names.
- Both conversions exist: FH -> FM4 layout (`CarPort.ConvertAnarkLayout`, `DashFonts.Fix`) and FM4 -> FM3
  (`Fm3Dash.ToMotorsport3`, `DashFonts.ToFm3`, pictures by `Fm3Textures`, the `List_DigitalGauges` row by
  `CarPortToFm3.PortDash`).

Measured 2026-11-21, `FHtoFM3\dashfh.exe <game root>` (FH .fbf -> FM4 layout -> FM3 form, read back as FM3):

| Game | Car zips | With a digital dash | Converted and read back | Use properties no FM3 dash has | Fonts with no FM3 rule |
|---|---|---|---|---|---|
| FH1 | 179 | 94 (all version 0x3F0) | 94, 0 failed | 8 | 0 |
| FH2 | 219 | 103 (all version 0x3F0) | 103, 0 failed | 7 | 0 |

- The dashes with properties FM3 lacks (the six-dash animation behaviour, tach memory, image UV offsets):
  FH1: AST_V12Zagato_12, FER_458Spider_12, FER_F142_10, FER_F151_11, LAM_LP7004_12, LAM_ReventonRoadster_09, LEX_LFA_10,
  VIP_Viper_13. FH2: AST_V12Zagato_12, FER_F142_10, LAM_Huracan_15, LAM_LP7004_12, LAM_Veneno_13, LEX_LFA_10, VIP_Viper_13.
  The FM4 -> FM3 port leaves such a dash out (`GaugeID -1`, reported) unless `AllowFm4OnlyDash`.
- Still open from 2026-11-03, FM4 -> FM3: the in-game check of the two test dashes in `FM3_Extracted_DEV`
  (AST_One77_10 plain, LEX_LFA_10 with FM4-only properties). A ported dash has never been seen running in FM3.

## 3. Route: A, chosen by the user on 2026-11-21 ("mi sembra la migliore")

Built the same day:

- `ForzaStudio/CarPortViaFm4.cs`: step 1 `CarPort` (FH -> FM4) as a trial run into a temporary folder, step 2
  `CarPortToFm3` with that folder as `PackRoot`. The FM4 folder (Tools > Settings > Folders) is read, never written;
  the result goes under `OutputRoot`, as for an FM4 car. The FM4 template of step 1 is chosen by the score of the FM4
  port window (`ClosestFm4Car`: make, engine placement, drive, type, year, weight). A car FM4 already has passes as
  `<name>_FH` in the FM4 form and gets its Horizon name back in FM3 when FM3 has no car of that name.
- `CarPortFm3Form.cs`: the source folder can be FM4, FH1 or FH2 (detected from the folder); a Horizon source lists the
  game's own cars (no FM4 DLC packages), and Trial / Install / the pack trial go through `RunCar` (FM4 straight,
  Horizon through `CarPortViaFm4`). No Tires box (section 1).
- `PortPairs.cs`: FM3 <- FM4, FH1, FH2 on again, and Create DLC Package for FM3.
- `CarPortToFm3.BuildWheelZips`: a wheel that came from Horizon carries its own `wheelN_lod0.xds`; the copy FM3 wants
  is made only when the zip lacks it (the first chain run stopped there: "The archive already has wheel0_lod0.xds").

- **Review with fixes to choose** (user 2026-11-21: "nel trial non ho visto cose che posso sistemare al volo come nel
  porting a fm4", then "devo fare io i fix"). What step 1 finds not regular for FM4 (Horizon materials no FM4 car uses,
  which render black; database values FM4 lacks) is `CarPort.Issues`, the same list the FM4 window reviews.
  `CarPortViaFm4.Fixes` (in) and `.Issues` (out) carry it; the Review tab of the FM3 window shows the FM4 window's grid
  above its list (Where, Problem, Fix; recommended choices in green; the choice the FM4 port suggests itself is
  preselected). The fixes are kept per car while the window is open and used by the next Trial Run, the Install and the
  pack trial; the grid follows the car selected in the list. Nothing is replaced on its own: a material with no fix
  chosen stays and the report says so ("WARNING materials: ...").
- **FM4's id rules are not run on the temporary form** (`CarPort.DeferIntegrity`): the user saw "uno sproposito di righe
  di log relative agli ID" on the R8 GT, 54 lines "database warning: X = N is not above FM4's largest". They are the FM4
  database's rule for cars that stay in FM4; here the ids are given by step 2, for FM3. One line in the report says so.
- The Review list of a Horizon car shows the lines of step 2 only (`CarPortViaFm4.SecondReport`): step 1's are the grid.

Harnesses (all write only under `FHtoFM3\out`): `chain.exe` (the two classes by hand, first proof), `via.exe` (the
app's class, several cars), `issues.exe` (the review of step 1), `fixrun.exe` (fixes through the chain).

Results 2026-11-21:

- `via.exe`, 7 cars, 7 OK, 43 to 90 s each: FH1 AUD_R8GT_11, FER_458Spider_12, FER_F142_10 (FM4 has it: passes as
  `FER_F142_10_FH`, FM3 gets `FER_F142_10`), VIP_Viper_13; FH2 LAM_Huracan_15, MCL_P1_13, FOR_MustangBoss302_69.
  Dash: R8 GT and P1 ported (gauge 284, 5 and 4 pictures), 458 Spider / 458 Italia / Viper / Huracan left out
  (properties FM3 lacks), Mustang has none.
- `fixrun.exe` on AUD_R8GT_11: 10 issues (all materials: indicator_left / right, taillight2S, numplate,
  z_gauge_emissive_alpha, drlwhite, xenonhead, fullbeam, fogred, Reserved); with a fix for each, no mesh of the FM3 car
  keeps those names (16, 12, 6, 2, 14, 12, 12, 10, 4, 1 meshes renamed); 0 FM4 id warnings; report 150 lines (was 214).

Not done yet: nothing was installed into `FM3_Extracted_DEV` and nothing was seen in game (the user's next check).
Known cost: step 1 writes a copy of FM4's `UI\Textures.zip` (436 MB) for the thumbnail, step 2 one of FM3's (140 MB).

### The two routes, as they were put to the user

- **A, chain the two ports that exist:** FH car -> the FM4 port's result kept as a staged pack in a temporary folder ->
  `CarPortToFm3` with that pack as `PackRoot`. Short (the estimate was 2-3 sessions), every step already tested on its
  own. Needs the FM4 game folder. What the FM4 step fills from FM4 (crash words, generic brakes, sound choices) reaches
  FM3 too.
- **B, direct:** FH reader -> FM3 writer with no FM4 step, as `PORT_ALL_GAMES.md` section 3 plans (neutral car + per-game
  writer). Keeps more source data, needs no FM4 folder; longer, and it is the Phase 0 refactor of that plan.

## 4. First results in game and what they changed (2026-11-21, user)

- **AUD_R8GT_11 (FH1): works in FM3 arcade.**
- **FER_F142_10 (458 Italia): crash when buying it, endless loading in arcade.** Cause: FM3 has its own 458 Italia under
  that very name, as a car of an official package (second disc / DLC; `DlcCatalog.tsv`: car 1131 `FER_F142_10`), so it
  is in neither the folder's database nor `Media\cars`, and the port did not see it and installed a second
  `FER_F142_10`. Fixed: `DlcCatalog.PackOf(game, mediaName)`; `CarPortToFm3` (name + `_FM4`), `CarPortViaFm4` (name +
  `_FH`, with a report line) and the window's "In FM3" column count the package cars as FM3's. The bad install is still
  in `FM3_Extracted_DEV` (`Media\cars\FER_F142_10.zip` of 15:40, its rows and strings): restore from `bak\` before
  porting the car again.
- **The review must speak of FM3** (user, twice: "ci sono riferimenti a FM4 a tutto andare, sono su FM3"). The first
  grid was the FM4 port's own review (choices and counts of FM4's cars). Now the review is `CarPortToFm3`'s:
  `RenameMaterials(model, file)` + `ReviewMaterials()`. A material no FM3 car uses in that kind of file and LOD
  (`ShaderProfiles.IsUsed`, `docs\FM3\MaterialUse.txt`) is an issue; the choices are FM3's materials, first what FM3
  cars give those parts (`docs\FM3\PartMaterials.txt`: on how many of the parts, up to how many cars), then the rest of
  FM3's list. A fix is written by the FM3 step on every model file, brakes included; `tail_light` stands for
  `tail_light_lod0` too (the name FM3 uses in that file is written). The FM4 step's material review and its lines are
  left out of the chain's report. So an FM4 -> FM3 port gets the same review. RULE for this port: every list, count,
  name and message the user sees is FM3's; FM4 is only the hidden middle step.
- `fixrun.exe` on AUD_R8GT_11 with the FM3 review: 11 issues (the 10 Horizon light / plate materials and `brake` on the
  rotors, which FM3 calls `rotor`); with a fix each: 93 meshes renamed, 0 issues left, no FM4 name in the choices.

## 5. Template cars from FM3's packages (2026-11-21, user: "i donor li voglio anche dai dlc come per fm4")

- `uvt`-style probe `FHtoFM3\packs3.exe`: Xenia holds 28 FM3 packages (second disc and DLC), 392 car entries (the two
  Ultimate packs repeat cars). `DlcSource.Stage` lays an FM3 package out as it does an FM4 one: `Media\cars\<car>.zip`,
  `Media\wheels\<wheel>.zip`, `Media\DigitalGauges\Dash_<car>.zip`, `Media\db\gamedb.slt` = the package's merge rows
  (62 tables, the car's `Data_Car` row). "Hot Holidays Car Pack" is where FM3's own `FER_F142_10` lives.
- `CarPortToFm3.DonorRoot` (the package laid out, as `CarPort.DonorRoot`): the template's rows are read from `donorDb`,
  a copy of FM3's database with the package's rows put in (every donor query: the Data_Car row, the row templates of
  `CopyRows`, the replacement engine / drivetrain, the stock wheel, the thumbnail); its zip and wheel zip come from the
  package (`FindDonorMedia`). None of the package's rows reaches the result. What must exist in the FM3 folder is
  modelled on a car of the folder (`FolderCar`): the new zips' lines in `zipmanifest.xml` and the thumbnail's size and
  place (a package car's zip has no manifest line and its picture is in the package).
- Window: the template list holds the folder's cars and the installed packages' cars, the latter as
  `<name>  [DLC: <pack>]  (<MediaName>)`; the package is laid out when such a car is used (`DonorRootOf`, about 12 s the
  first time). `CarPortViaFm4.DonorRoot` passes it on.
- `FHtoFM3\donor3.exe`: FER_458Spider_12 (FH1) with FM3's `FER_F142_10` of Hot Holidays as template: OK in 41 s; the
  result's database has no row of the template car; thumbnail written; the car zip has its manifest line.
- Depends on the package being installed in the game too when the template's engine or drivetrain replaces one FM3
  lacks (the ids point at the package's rows), as for FM4.

## 6. The 458s crashing when bought: FM3's own id range and the front bumper aero (2026-11-21)

User: 458 Italia (FH1) crash when bought and endless loading in arcade; 458 Spider crash when bought; R8 GT fine.
The Xenia log only shows the game looking the car's files up and then no frame for 2000 watchdog ticks (a hang), no
cause. The cause was found in the database, with FM3's own rules (`FHtoFM3\dbcheck3.exe` = `DbIntegrity.Compare` on the
clean database against the result, FM3's relation map and id limits):

1. **Front bumper aero sets left behind.** The Horizon 458s have an adjustable front bumper:
   `List_UpgradeCarBodyFrontBumper.AeroPhysicsID` names two `List_AeroPhysics` rows (458 Spider 1559400 / 1559401, 458
   Italia 1134400 / 1134401; the FH -> FM4 step keeps them under the Horizon car's id). `CarPortToFm3.Dangling` copied
   only the rear wing's sets, so the car pointed at rows FM3 did not have. The R8 GT has a rear wing only. Fixed: every
   table of the car that names an aero set is covered, and a set whose key is not the car's is written as
   `car * 1000 + n` (1712400 / 1712401).
2. **Ids inside FM3's own range.** The 458 Italia carries engine 579, drivetrain 527 and wheel 10722. FM3's folder
   database has none of them, but they are the ids of FM3's own 458 Italia in the Hot Holidays pack (FM3's largest,
   base game and packages: engine 772, drivetrain 716, wheel 10867, `docs/GameIdLimits.json`). The port wrote its
   copies under those ids, and a wheel zip named as the pack's. Fixed: `MoveOutOfGameRange` gives an engine or
   drivetrain copied under such an id a free id above the range (2151, 2097), with its upgrade rows' own keys and the
   car's `List_UpgradeEngine` / `List_UpgradeDrivetrain` rows; `PortWheel` does the same for the stock wheel and names
   it after the ported car (id 12345, `FER_F142_10_FH`, zip and `.carbin` entry renamed). Ids above the range (the R8
   GT's engine 1018, the Spider's 1023) stay.
3. **The port now runs FM3's database rules itself** (`CheckDatabase`): one line when all is regular, else a warning
   with what points at nothing or sits in FM3's range.

After the fixes, against the clean FM3: FER_458Spider_12, FER_F142_10 (as `FER_F142_10_FH`) and AUD_R8GT_11 give 0
errors and 0 warnings. Not yet seen in game.

`FM3_Extracted_DEV` is dirty from these tests: the 458 Italia was uninstalled at 16:05 but its engine 579 and drivetrain
527 rows are still in the database (43 warnings of `dbcheck3`), and the 458 Spider installed there has the two dangling
aero sets. It must be cleaned before the next in-game test (ask the user how: it also holds older test cars).

For the port chat: the FH -> FM4 port leaves a Horizon car's front bumper aero sets under the Horizon id (1559400)
while the rear wing's move to the new car id; harmless in FM4 (the rows exist) but not regular.

## 7. Pack "458 R8" (offer 105) not visible; the Veyron removed while selected (2026-11-21, evening)

- The pack was trialled against the clean `FM3_Extracted` (the DLC window's target), so its cars took ids 1712 / 1713.
  Xenia plays `FM3_Extracted_DEV`, where the Veyron (FH1) had been installed at 17:36 as car 1712. The validator
  (`FHtoFM3\val.exe` = `DlcValidator.Validate`, deep): 18 errors "Data_Car.Id = 1712 is in the game's table with other
  values ... the merge fails on this row" (and every row keyed by the car): the merge failed, neither pack car showed.
- Fix: `CarPortToFm3.OtherDatabases()`: the car id must also be free in the target's test copy (`<folder>_DEV`) or the
  folder it copies, and in the merge databases of the FM3 packages in Xenia (`XeniaContent`, set by the window and
  `CarPortViaFm4`). Report line "car id: free also in N other database(s)". Engine / drivetrain / wheel ids of the
  Veyron (788 / 732 / 10882) did not collide with the pack's (1023, 1018 / 962, 957 / 11144, 11139).
- The user then uninstalled the Veyron (17:50) while it was the car selected in the garage: FM3 crashed at start. The
  profile keeps the selected car's id. Restored the Veyron into `FM3_Extracted_DEV` from the uninstall backups
  (`bin\Debug\bak\2026-11-21_17-50-50_uninstall_bak*`; the files replaced by the restore are in
  `E:\DEV\fm4dev\FHtoFM3\dev_before_veyron_restore_2026-11-21`). RULE: before removing a car, select another car in
  the game's garage. Not yet checked: whether the game starts again.

## 8. Review fixes chosen after Trial Pack did not reach the pack (2026-11-21, evening)

- User: the pack "458 R8" still had the Horizon materials. `FHtoFM3\mats3.exe <trial folder>` on the only pack trial
  (`%TEMP%\ForzaStudio\PackFm3\pack_2026-11-21_17-40-59`, packed 17:44:56): R8 GT 20 material/list pairs FM3 does not
  use (`drlwhite`, `fullbeam`, `indicator_left/right`, `numplate`, `xenonhead`, ...), 458 Spider 15.
- Not a port bug: `RunCar` -> `CarPortViaFm4.Fixes` -> `CarPortToFm3.RenameMaterials` writes the fixes (`fixrun.exe`).
  The window's Build For Xenia / Build For Xbox packs `packTrialFolder` as it is: fixes chosen in the grid after the
  pack trial are ignored and Build stays enabled. The fixes also live only in memory (`CarPortFm3Form.fixes`).
- BUILT 2026-11-21 (user: "sì, costruisci la protezione sul Build"): `CarPortFm3Form.CheckPackTrialFixes` compares the
  fixes of the car on show with those the pack trial used (`packTrialFixes`); a user change in the grid for a pack car
  clears `packTrialFolder`. Not saved across app restarts (not asked). Old proposal text:
  (Build off, status "Fixes changed: Trial Pack again"); optionally save the fixes in ForzaStudio.ini.

## 9. FM2 -> FM3 through the same chain (2026-11-21, user: "procedi a fare fm2-fm3")

- The FM2 game folder is a source of the FM3 window: step 1 is the FM4 window's FM2 port (CarPort stages the FM2 car
  on an FM4 template, Fm2Source), step 2 the FM3 port. FM2 DLC packages (stages) are not a source yet.
- Code: `CarPortViaFm4.GoesThroughFm4` (FH1, FH2, FM2 game folder), suffix `_FM2` for a car FM4 / FM3 already has (as
  `CarPortForm.SuffixFor`), report times "time FM2 -> FM3"; `PortPairs` FM3 <- FM2; `CarPortFm3Form` accepts FM2,
  lists only FM2 cars with a zip in `Media\cars`, the Upgrades tab says FM2's upgrades come with the FM4 form (FM2's
  upgrade tables have another layout).
- Offline trial (`FHtoFM3\via.exe`, FM3 = `FM3_Extracted_DEV` read only, out `FHtoFM3\out_fm2`): 7 / 7 OK, 20-30 s
  each; `dbcheck3.exe` 0 errors 0 warnings on all.

  | FM2 car | FM3 name | FM3 class | materials FM3 lacks (Review) |
  |---|---|---|---|
  | TOY_SupraVeilside99_98 | same | S 695 | none |
  | CHE_CorvetteLingenfelter_02 | same | S 695 | reverse_light, chrome_blur_rim, chrome_blur_lip |
  | POR_959_86 | same | A 556 | detail_glass_red |
  | HON_S2000Mugen_03 | same | B 438 | detail_glass_red, reverse_light |
  | NIS_SkylineGTR_95 | same | C 380 | reverse_light |
  | TOY_SupraRZ_98 (FM3 has it) | TOY_SupraRZ_98_FM2 | C 397 | reverse_light, detail_glass_red |
  | FOR_FocusST_DLC1 | same | D 313 | none |

- 68 FM2 cars are not in FM3 at all. The step-1 report keeps FM4's "WARNING part makers" lines (logos of the FM4
  template's part makers); they belong to the FM4 form. Not seen in game yet.

## 10. Pack trial FAILED "no such table: Data_Car" (2026-11-22)

- Step 2 checks the car id against every FM3 package database in Xenia (`OtherDatabases`, 22 by then); one merge
  database has no `Data_Car`, and `DbIds.Taken` queried it anyway. Fix: `DbIds.Taken` skips a database without
  `Data_Car` (a package with no cars takes no car id). Built bin\Debug; pack trial retest by the user pending.

## 11. The FM3 windows get the FM4 window's choices, and "+" New make for FM3 (2026-11-22)

- User: the FM3 Import / Create DLC Package windows lacked the FM4 window's generic choices; "FM3 has practically the same DB".
  Checked (FM3_Extracted vs FM4_Extracted): FM3 176 tables, FM4 198; the 36 FM4-only tables are career / Kinect / badges /
  offers, none a car table. Data_Car: FM4-only AutoSteerOverrideID, Front/RearStockRideHeight, HasCarExperience,
  HasRaceLivery, IsCarExperienceUnlocked, IsDrivable; FM3-only AssistsSTMSlipDefMin/Max. List_CarMake, List_Country,
  engines, upgrades, brakes: the same tables.
- Built (`CarPortFm3Form.Choices.cs`, new): Also in arcade mode, Make + "+", Engine sound, FRONT / REAR brakes with caliper
  angle, at the FM4 window's places; per car in the pack (`PackItem.Choices`). No Tires (FM3 has no tyres per car).
  - Arcade / make: `CarPortToFm3.Arcade`, `.MakeId` (IsArcade was the template's before).
  - Sound: `CarPortToFm3.SoundDonorMediaName` (existed, had no control); list = the FM3 folder's cars, default "its own".
  - Brakes: only for the routes through the FM4 form (FH1 / FH2 / FM2): `CarPortViaFm4.Brakes` -> step 1 `CarPort.Brakes`,
    which writes the generic model and its angle together (box in degrees, row in radians). An FM4 car keeps its own
    brakes and angles: the boxes are off.
- New make on FM3: `CarMake` is game-aware. FM3 = three logo folders (Manufacturer_Logos, _I, _SM, 53 makes each), no
  IconPathBase column, IconPath under Manufacturer_Logos (FM4: _I), no titles table; livery decals not built for FM3
  (its Livery.zip decals are FM3 models). `NewMakeForm` wording names the target game. A make for the pack is written into
  the trial folder before the cars; the FM3 pack build ships it from the difference (strings, Textures.zip, database).
- Offline (`FHtoFM3\mk3.exe`, out `FHtoFM3\out_mk3\run`): make 106 "Testmake" (TMK) with 3 logos 220x184 / 132x68 as
  Acura's, strings FFFE / FFFD in 16 languages; FM2 POR_959_86 under it with IsArcade 0, TOY_SupraRZ_98's sound, 4-piston
  generic front brakes at -80 deg (-1.396 rad); `dbcheck3` 0 errors 0 warnings. Not seen in the app or in game yet.

## 12. Trial times (2026-11-22, user: "dovremmo migliorare i tempi di trialing")

- Pack trial of 3 FH cars: 43-55 s per car, of which step 2 "car zip" 25-39 s. Measured (`FHtoFM3\conv3time.exe`, an FM4
  car zip): models 0.3 s, textures 53 s (`damage_lod0.xds` 5.5 MB alone 28 s); LZX of the zip 5.5 s (`lzxtime.exe`).
- Cause: `Fm3Textures.ConvertSmoothBlock` (the 2026-10-20 neutral-colour search for low-contrast DXT blocks) tries up to
  666 endpoint pairs per block, allocating arrays in the inner loop, one core.
- Fix, same bytes out: the search without allocations (same pairs, order, error, ties), a cache per texture keyed by the
  block's 8 bytes, the blocks on all cores; `ForzaZip.Save` compresses the changed entries on all cores.
- Checks: `texspeed\` — 4 FM4 cars' 66 textures old vs new code identical, 132.6 s -> 8.4 s; `zipsave.exe` on
  AUD_R8V10_10.zip identical, 5.6 s -> 1.9 s. Built in the scratchpad only (app open); bin\Debug build pending.

## 13. Build DLC Package stopped on a string key of an installed pack (2026-11-22)

- Pack FM2_TEST (offer 107, AB Flug RX-7 + S900 Supra): "Data_Car: key FEC4 is "458 Spider" in an installed pack, "AB Flug
  RX-7" of this pack cannot take it". CarPortToFm3 picks Data_Car.str keys against FM3's own table only; pack 105 already
  used FEC4. The FM3 build (BuildStringsFm3, also used by the FM2 build) stopped, the FM4 build moves the key (2026-10-31).
- Fix: BuildStringsFm3 moves a clashing key to a free one (FreeStringKey, FFFD down) and the merge follows (RemapStrings),
  as the FM4 build; FM3 and FM2 builds. Built in the scratchpad (app open); Build retest by the user pending.

## 14. FM2 pack FM2_TEST (offer 107): RX-7 without wheels, game loop when a car is bought; cockpit not in the viewport (2026-11-22)

- Pack database: 5 of 7 cars on the RX-7's wheel 12346 and drivetrain 1642 (RWD on the 4WD Lancer, Impreza, Skyline);
  the other cars' wheel rows (12347-12349) orphaned. Cause: in a pack trial every car's step 1 (FM4 form) started from a
  clean FM4, so every FM2 car got the same FM4-form car id 1642, drivetrain 1642 / 101642, wheel id; step 2 then took
  1642 as "FM3's own" (drivetrain checked by id only) and CopyRows gave a clashing wheel row a fresh id without moving
  the car to it. The FM4 window's pack trial has no such problem: its cars go one on top of the other in one folder.
- Fixes: `CarPortViaFm4.SharedStage` (one FM4-form folder for the whole FM3 pack trial, CarPortFm3Form.RunPackTrial);
  `CarPortToFm3.PortWheel` picks the free id before copying a wheel whose id is another's, and the car follows it;
  engine / drivetrain / wheel ids copied into FM3 are also kept free of the other FM3 folder and the installed packs
  (`takenElsewhere`, as the car id already was).
- Offline (`FHtoFM3\pack3fm2.exe`, the 7 cars, out `FHtoFM3\out_pack_fm2`): each car its own engine and drivetrain, the
  wheels as FM2 shares them (S900 = RX-7's, both Veilsides = VEI_AndrewRacing5Evolution) or, with installed-pack
  checking, a copy each under the car's name; dbcheck3 0 / 0.
- Cockpit "not supported" in the viewport: ForzaCarBin.GetVersion recognised FM3 type-1 files by a zero word at 0x70,
  inside the wheel lighting, not zero on FM2-born cockpits. Fallback: CarbinFormat.Identify (model settings v13).
  `view3.exe`: 24 / 24 models of the pack open (before: both FM2-born cockpits failed). `read3` / `ident3`: harnesses.
- The RX-7's missing wheels in game are not explained beyond the broken ids above: retest with a rebuilt pack.

## 15. No tires on FM2-born wheels in FM3 (2026-11-22)

- In game (pack FM2_TEST_TUNER, offer 107): AB Flug S900 bought fine, rims with no tire. FM3 draws its generic tire on LOD 0 and
  fits it on the LOD 0 `wheel_black` ring (FM4 keeps the 12 tire measures in the wheel header instead). `wblack.exe` on 25 FM3
  wheels: always a ring 2-4 mm wide on the inner side, at the LOD 0 rim's largest radius, ~160 vertices. FM2 has no LOD 0;
  the rebuilt LOD 0 had no wheel_black.
- Fix: `CarbinModel.AddTireSeatRing` (CarbinModel.TireSeat.cs): a wheel_black LOD 0 mesh on the LOD 0 vertices already on that
  ring (no vertex added), FM4 form or FM3; called by CarPortToFm3 for every wheel without one. FM2 -> FM4 untouched.
- The installed pack mended without a new trial (user): `seat3.exe patch` on trial pack_2026-11-22_11-42-46 (wheel
  MAZ_RX7_ABFlug_95, ring of 62 vertices, x -0.127, r 0.221; RX-7 ring width ~0, FM3's are 2-4 mm), old package
  C8B6FF38... moved to `FHtoFM3\seat_fix_2026-11-22`, `seat3.exe build` = BuildFolderFm3 + BuildConsolePackage -> 65C4648A...
  In-game check pending. Validator: "CHS lacks Data_Car" ERROR also in the old package (the game ran): open, not caused here.

## 16. FM2 rims small in FM3's tire: FM3 draws stock wheels at 1.085 x the nominal rim (2026-11-22)

- User (pack 107, tires now shown): "i cerchi non hanno la proprietà mesh scalabile" - the rim small inside a tall tire.
- `ratio3.exe` on FM3's 251 stock wheels: LOD 0 largest radius / (Data_Car.FrontWheelDiameterIN x 0.0127) median 1.085,
  80% 1.077-1.097. FM3 has no morph (FM4's 15" reference + morph does this by itself); the FM2 RX-7 rim was 0.967 x.
- Fix: `CarbinModel.Motorsport3WheelRadius` set by CarPortToFm3 (1.085 x the car's nominal rim); the FM4 -> FM3 conversion
  (SizeWheelLod0ForMotorsport3) scales LOD 0 about the axis when outside 1.06-1.11; FH / FM4 wheels already in the band
  are left alone (Lancer 1.10). Report line "lod0 rim sized for FM3". `SizeMotorsport3WheelLod0` does the same on an
  FM3-form wheel (size3.exe).
- Installed pack mended again: size3.exe on trial pack_2026-11-22_11-42-46 (0.221 -> 0.248), package 65C4648A... moved to
  `seat_fix_2026-11-22\before_size`, rebuilt -> 239F3FC1... In-game check pending (also: does a 19"/20" rim upgrade grow it).

## 17. FM2 -> FM4 wheels sized like FM4's own (2026-11-22, user: tires looked worse in FM4 than in FM3 now)

- `wheel4.exe look` on 337 FM4 stock wheels: LOD 1 inner_rim radius = 1.084 x the nominal rim (80% 1.056-1.126), LOD 1 tire
  radius = the declared tire (rim + width x aspect) 1.000 x (80% 0.960-1.027). FM2 -> FM4 RX-7 AB Flug: rim 0.967 x, tire
  0.955 x; Veilside 99: 0.945 / 0.965. FM4 grows the LOD 0 rim by its morph to the car's rim, the tire fit (header floats from
  LOD 1) stayed FM2's smaller size.
- Fix (Fm2Source.SizeFm2Wheel + DonorTires): every FM2 wheel LOD 1-5 outside FM4's band scaled about the axle to 1.084 x the
  car's declared front rim (FM2 Data_Car StockTire-FrontWheel-in); the donor's tire fitted to the declared tire's outer radius.
- Offline 7 / 7 (`out_fm2_fm4_sized`): rims 1.084-1.100 x, tires 1.000 x; Lancer / Skyline rims already in band (only the
  tire, Lancer LOD 5 x0.96). FM2 -> FM3 inherits it (FM3's own sizing now a no-op, RX-7 / Veilside 0.248). Known limit: a
  wheel shared by two cars with different tire sizes (RX-7 245/40, S900 255/35) is staged per car, the last one wins.
  FM2 cars ported before need a re-port. In-game check pending.

## 18. The open points closed (2026-11-22, user: "fai tutto lavoro aperto e poi io testo tutto")

- **CHS Data_Car missing in packs (validator ERROR).** Cause: `CarPortToFm3.PortStrings` wrote a language only when the
  source table of that language had the key. FM4 has no CHS, so CHS fell back to the FM4 / stage EN.zip, while the car's
  names were in the pack's own EN table: the FM3 CHS Data_Car.str stayed unchanged and BuildStringsFm3 left CHS out of the
  pack. Fix: a language that lacks the key takes the English text of the source (`FindKey`, "EN"), as the method's comment
  always said. Offline (`out_pack_fm2_own`, `strnew.exe`): the 4 new keys in all 16 languages, CHS included. Packs built
  before keep the error; it showed no effect in game.
- **One FM2 wheel, two tire sizes.** FM2 has 6 such wheels (RX-7 AB Flug 245/40 R18 / Supra S900 255/35 R18, MR2,
  Integra R x2, Civic R, Celica APR / Impreza Tommy Kaira). `Fm2Source.StageWheel` now gives the car its own wheel, named
  after the car (as FM2 names its wheels and as Duplicate Car names an own wheel), when another FM2 car uses the same wheel
  with a different declared front tire (`SharedWithOtherTire`). Report line "is shared with an FM2 car of another tire
  size, this car gets its own wheel <car>". Wheels shared with the same tire (both Veilsides) stay shared.
  Offline FM2 -> FM4 (`out_fm2_fm4_own`): RX-7 tire 0.327 = 1.000 x, S900 0.318 = 1.000 x, both rims 0.248 = 1.084 x;
  Veilside 99 unchanged. FM2 -> FM3 pack trial: RX-7 wheel 12345, S900 its own 12347; dbcheck3 0 errors 0 warnings.
- **Veilside and Lingenfelter FM3 PI 0.613: not a clamp.** `perf3.exe` (Fm3Perf.Compute): Lingenfelter PI 0.6138, lap
  121.229 s, top 109.9 m/s, corner 14.57, 1414 kg; Veilside PI 0.6135, lap 121.249 s, top 110.7, corner 14.31, 1479 kg,
  more power. Different inputs, the same lap by chance. Both 53% front: FM2's FrontWeight-pct, kept.
- **FM2 DLC packages as a source of the FM3 window.** `CarPortViaFm4.GoesThroughFm4` accepts an FM2 package stage
  (raw=fm2); step 1 (CarPort) already lays such a car out with the FM2 game's wheels / thumbnails / strings. The FM3 window
  lists FM2 packages (Package... and "Include the game's DLC" = Xenia's FM2 content folder) when the source is the FM2
  folder, only each package's own cars (`DlcSource.LazyCars`; the stage database is the whole FM2 one), and RunCar passes
  the car's stage as the chain's source. Offline (`stage2.exe`, `pack3fm2.exe` on Nissan Tournament Pack): NIS_350Z_DLC1 +
  PEU_908_DLC2 -> FM3 cars 1717 / 1718, names from the package in all languages, dbcheck3 0 errors 0 warnings.
- Build: compiled 0 errors; bin\Debug NOT updated (ForzaStudio and Xenia were open), exe in obj\x86\Debug.

## 19. User checks (2026-11-22, after the 13:05 build)

- OK: FM3 pack RX-7 + S900 rebuilt (no CHS error, S900 own wheel); S900 own wheel in FM4; "+" new make for FM3; 19" / 20"
  rim upgrade in FM3; Review grid drawn at full size.
- Still to test: FM2 DLC packages as a source of the FM3 window; Build turned off after a review fix changes; FM2-born
  FM3 cockpit in the viewport.

## 20. FH traffic cars -> FM3, first offline trial (2026-11-22, user: "auto traffic FH1/2 su FM3, non ne abbiamo provata una")

- `via.exe` FH1 -> FM3 (`out_traffic`): GMC_YukonXL_01, FOR_FocusZX4_05, FOR_F350SuperDuty_08, TOY_RAV4Sport_04: 4 / 4 OK,
  8 s each. Step 1 (CarPort) does the whole traffic rebuild as for FM4: lod0 / cockpit from LOD 1, empty parts dropped,
  plates removed, light placeholders removed, generic brakes, collision from the donor's spheres, driver rig, cameras,
  wheel LOD 0 from LOD 1. Step 2: tire seat ring on the rebuilt wheel (Yukon radius 0.235), FM3 cockpit camera, FM3 PI
  (Yukon E 205, Focus F 187, F350 E 203, RAV4 E 233).
- Open, to see in game:
  - Sound: Focus / F350 / RAV4 have FH's NA_Int_HT / NA_EngAmb_HT layers (bank NA.fsb, in no game). In FM4 only the two
    electric cars (Volt, Leaf) use NA_* and run; no FM3 car does. If FM3 hangs or the car is silent, this is the first
    suspect (Change Car Sound / the window's engine sound choice gives a real one). Yukon has real banks.
  - Materials: FH traffic light materials (slorange, oldhead, hidhead, lindtail...) reach FM3 as they are (step 1 leaves
    material fixes to step 2): 10-36 per car "may render black" in the Review tab, fixes are the user's choice.
  - The FM3 car list does not mark traffic cars (the FM4 window shows them yellow).
- 2026-11-22 (user: "metti il giallo per le traffic"): the FM3 car list shows FH traffic cars in yellow with the FM4 window's
  legend "Traffic car (close-up rebuilt)" (CarPortFm3Form.IsTraffic = CarPort.HasPlaceholderCloseUp, or a lazy stage's
  index). `traf.exe`: 18 FH1 + 14 FH2 cars. Side effect: StyleList now draws yellow items, so the Review tab's WARNING /
  silent / missing rows show yellow as their code always meant. Built 13:11, app check pending.
- 2026-11-22 user: FH1 traffic DOD_Durango_99 ported straight into FM3 (single car, not a pack): OK in game. First FH
  traffic car seen in FM3.
  Sound changed in the window: OK; cockpit OK. Next: DLC pack with the Yukon.
- 2026-11-22 user (Review screenshot: bumperFa_LOD3..5, bumperRa_LOD1..4 "no FM3 car uses"): the empty-part dummies
  `<part>_LODn` were reviewed by the FM3 step (FM4's material lists hold them, FM3's survey does not). Fix:
  CarPortToFm3.RenameMaterials skips CarPort.IsEmptyPart subsections, report line "materials: empty parts, not reviewed".
  Offline (`out_traffic2`): RAV4 19 -> 8 review rows, Yukon 36 -> 16; what is left are FH light materials (slorange,
  oldhead, hidhead, indicator_left...), the user's fixes. Compiled; bin\Debug not updated (app open).
- 2026-11-22 user: the Yukon (FH1 traffic) has no tail light glass in FM3, "as the FH2 595 in FM4". `pass3.exe`: glassLTL /
  glassRTL hold lights_gls_* (FH names FM3 lacks) in the OPAQUE pass. A review fix renamed them only (to red_glass etc.),
  so the glass stayed in the opaque pass, which FM3 does not draw: the 595 bug (transparent-part-flag), never handled on
  the FM3 port because CarbinModel.ForGame skips FM3 files. Fix: CarPortToFm3 runs MaterialSlots.ToBytes (FM3's rule:
  every mesh into its material's pass, part see-through flag from its meshes) on each converted carbin, report line
  "draw pass / see-through flag as FM3 has them". `fixglass.exe` (port with review fixes): Yukon glass -> red_glass /
  detail_glass_* now in the see-through pass, flag 1, main and lod0. Without fixes only flags of parts with no FM3 glass
  change; an FH1 R8 GT: nothing moved. Built 13:31.

## 21. Last user checks, topic closed (2026-11-22, after the 13:31 build)

- OK in game / in the app: Yukon pack with review fixes shows the tail light glass; Focus ZX4 in a pack with FH's NA_*
  sound layers (no hang, sound plays); an FM2 DLC package car in the FM3 window; Build turned off after a review fix
  changes post Trial Pack; FM2-born FM3 cockpit in the viewport; FH traffic cars yellow in the FM3 list.
- Nothing left open on the ports to FM3 from FH1 / FH2 / FM2 (traffic cars and FM2 DLC packages included). Next topic:
  ports with FM2 as the target.

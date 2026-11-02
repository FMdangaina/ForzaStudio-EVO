# FM3 conversion: what the tool does with Forza Motorsport 3

State at 2026-11-03. Everything here is committed in the repository; "checked in game" means the user ran it in Xenia (or on a
console where said). The byte-level description of the FM4 → FM3 car port is in [../PORT_FM4_TO_FM3.md](../PORT_FM4_TO_FM3.md);
this file is the map of the whole FM3 side: the two port directions, the DLC packs, the install wizard, the thumbnails, the menus.

## 1. Directions

| Direction | Code | Window | State |
|---|---|---|---|
| FM3 → FM4 | `CarPort`, `CarbinModelMotorsport3`, `Fm3Textures` | Tools > Import Car From Another Forza > Into FM4 > From FM3 (`CarPortForm`) | checked in game (X5, Boxster, Supra...), 74/74 trial runs |
| FM4 → FM3 | `CarPortToFm3`, `CarbinModelToMotorsport3` | Tools > Import Car From Another Forza > Into FM3 > From FM4 (`CarPortFm3Form`) | six cars checked in game 2026-11-02 (exterior, interior, wheels, cockpit camera, sound) |
| FM4 → FM3 DLC | `DlcPackage.Fm3` | Tools > Create DLC Package > For FM3 (`CarPortFm3Form`, Dlc mode) | folder pack and STFS pack of one car checked in Xenia; the window itself is new |
| FM3 DLC install / validate | `DlcValidator`, `DlcFixer.Fm3`, `DlcWorld`, `DlcRelocate` | Tools > Check and Validate DLC, Tools > Validate DLC for Installation | run from harnesses on real packs, not clicked in the UI |

The menu is one table, `PortPairs.cs` (target game → sources, and the targets that have Create DLC Package): a new route is one line;
routes without a conversion and a window are greyed.

## 2. The FM3 DLC pack

Read on 2026-11-02 from the official packs (Hyundai Car Pack offer 22, VIP Car Pack offer 16) in
`...\content\0000000000000000\4D53084D\00000002\dlcs`. The layout is FM4's family with these differences:

| Item | FM3 | FM4 |
|---|---|---|
| Package id / folder | `4D53084D0CCF` + offer as **4 hex digits** (`0016` = offer 22) | offer id from `ContentOffers` |
| Loose files | `Media\DLCZips\<offer hex4>_pri_65\Media\...` | `<offer>_pri_65` |
| Merge database | `db\patch\<offer>005_merge.slt` (decimal offer); `<offer>000_execute.sql` when the pack has one | `<offer>000_merge.slt` |
| The pack in the database | one `MultiDiscContentPacks` row: `ID`, `intID` = `PubOfferingID` = offer, `LicenseLevel` 1, `LicenseMask` 1, `IsDLC` 1 | `ContentOffers` + `ContentOffersMapping` |
| Buying the cars | `Media\LicenseMasks\00000001.sql`: `UPDATE Data_Car SET IsPurchased=1 WHERE ID IN (...)`; the merge rows come `IsInstalled=1, IsPurchased=0` | in `ContentOffers` |
| `.puboffer` file | none | yes |
| Strings | `StringTables_pri_<priority>.zip`, the whole table (game + installed packs + the pack's own); priority above every installed layer (official: offer + 150) | same rule, `Benchmark.str` trick for several packs |
| Console package (STFS) | the layer folder as a zip **and the `.fsb` sound banks left loose beside it** (as the official packs; inside the zip the car had a sound problem) | everything in the zip |
| Xenia | folder under `content\0000000000000000\4D53084D\00000002` plus `Headers\00000002\<id>.header`, or the STFS file as it is | same |

Code: `DlcPackage.Fm3.cs` (`BuildFolderFm3`, `FreeOfferFm3`, `RemoveFm3`), the shared parts in `DlcPackage.cs` (`BuildMerge(..., fm3)`,
`BuildConsolePackage(..., title, game)`, `XeniaHeader`). The pack is built from a trial run of the port against the clean FM3: the new rows
of the trial database against the clean one are the merge, the files the port added or changed go loose (a zip's new entries land in the
folder the zip is mounted at: `Cars\X.zip` → `cars\X\`, `UI\Textures.zip` → `UI\Textures\`, the audio tuning zips likewise).

What the build checks against the packs installed in Xenia (`ClearOfInstalledFm3`): keys (engines, drivetrains, upgrade rows) an installed
pack already uses are moved in the merge; an offer, a car Id or a car MediaName an installed pack has stops the build.

The offer in `DlcSource`: the number in `0016_pri_65` is **hex** (it was read as decimal before: Hyundai was "offer 16", it is 22).

## 3. Sound: the engine tuning is looked up by the engine, not the car

Found by the DLC validator on the first pack (2026-11-02). The game takes the engine's tuning from `Data_Engine.MediaName`: the C1's engine is
`CIT_DS3_11`, so the car needs `CIT_DS3_11_ET.xml`, not only `CIT_C1_11_ET.xml`. `CarPortToFm3.PortEngineTunings` converts FM4's `<engine>_ET.xml`
for every engine of the car that FM3 has no tuning for (and still writes the car-named file, harmless). The same name rule is used by the install
wizard: a car that shares its name with its engine keeps `<engine>_ET.xml` when the car is renamed. The banks and harmonic tunings the tuning names
(`4N3_Focus_EngAmb.fsb`, `..._HT.xml`) are copied from FM4 when FM3 lacks them.

## 4. Install wizard (Tools > Validate DLC for Installation)

The window takes the game from the package's title id (FM4 4D530910, FM3 4D53084D), the game folder from Tools > Settings (FM3: `Folder.FM3`),
the installed packages from Xenia's content folder of that game. `DlcFixer.Fm3.cs`, on a copy of the package (the original never changes):

1. **Ids** — `DlcRelocate` with the FM3 game: car Ids, MediaNames, engines, drivetrains and every other key that the game or another pack has.
2. **Offer** — `MoveOfferFm3`: the `MultiDiscContentPacks` row moves to the first free number; the files carry it (`<hex4>_pri_65`, `<offer>005_merge.slt`,
   `<offer>000_execute.sql`, the `StringTables_pri_<n>` layer, `zipmount.xml`).
3. **Cars** — the car folders, the thumbnail (`Thumbnail_<Id>.xds`, the `Data_Car.Thumbnail` column) and the Ids inside `LicenseMasks\*.sql` follow.
4. **Strings** — each table of the pack's layer is rebuilt on the game's + every installed pack's table (`DlcWorld.StringTable`); a string already known for a
   key (in any layer) is inherited, a different text on the same key moves to a free key and the database rows follow (`DlcPackage.RemapStrings`); the layer goes above every installed one.
5. **Check** — `DbIntegrity.CheckMerge` for the game. Its FM4-specific rules do not run for FM3.

Not done for FM3: `RepairMakes` and the title rules (`BadgesAndTitles`), `RekeyStrings`, `DropRedundantTables`, the `Benchmark.str` trick (FM4 only).
Known limit, the same as FM4: two packs that both ship the same new catalogue row (e.g. `List_Cylinders`) fail the merge check.

## 5. Thumbnails (Tools > Set Generated Thumbnail)

FM3's thumbnails are **512 × 256 A8R8G8B8** (655 412 bytes, level 0 + level 1), FM4's 768 × 288. The photo the game saves in the Xenia profile
(`content\<profile>\4D53084D\00000001\ForzaProfile\Thumbnails\Thumbnail_N.xdc`) is the same `.xdc` container (`XdcFile`) and unpacks to exactly the
size of the game's `Thumbnail_<Id>.xds`, so it goes into a pack as it is. The window picks the size and the profile folder from the package or the
game folder it opens (`SetThumbnailsForm.SetGame`, `XeniaThumbnails.FindFolder(title)`).

## 6. The FM3 window

`CarPortFm3Form` has the same screen as the FM4 window: brand bar, game banner (`GameBanner`), the two game folders, Cars, Selected car, the pack on the right
(Dlc mode), the Report / Review / Upgrades tabs. Start values come from the settings: FM4 = the port target, FM3 = `Folder.FM3` (an import goes to
`<folder>_DEV` when it exists and refuses the clean `FM3_Extracted`), Xenia, the last pack name and offer. "Include the game's DLC" is off by default
(reading the installed FM4 packs takes a while); Package... adds FM4 package files.

- **Review** lists the lines of the port report that changed, replaced or left out something. There are no fixes to choose yet.
- **Upgrades** lists the selected car's FM4 engines and drivetrains and whether FM3 has them (own / the template's / copied from FM4), and the other upgrade tables with their level count.

## 7. Not done / to check

- The DLC window and the install wizard have been run from harnesses only; the Xbox 360 package of the FM3 pack has not been tried on a console.
- FM3 engine levels: FM3 has 8, FM4 4; the car keeps FM4's 4. FM3's own CSC / DSC / manifold upgrades, FM4 `HarmonicTunings` and the zero-throttle torque-curve shape: see PORT_FM4_TO_FM3.md.
- FM3 → FM2, FM3 → FH, FM2 / FH → FM3 are greyed in the menu.
- The thumbnails are tested on decoding only (one `.xdc` in the profile), not saved into a pack from the window.

## 8. Harnesses

All in `E:\DEV\fm4dev` (not in the repository). `Fm3Dlc\mk.cs` (port a car against the clean FM3, build the pack into Xenia), `st3.cs` (STFS package),
`val.cs` (validator), `inst.cs` (install wizard on a package and a content folder), `shot3.cs` (renders the FM3 window to a PNG without showing it);
`PortRefactor\fm4fm3\six.cs` and `reset_dev.sh` (the six test cars and the reset of `FM3_Extracted_DEV`). Build them against `bin\Debug\ForzaStudioEVO.exe`
(x86, with a `.exe.config` next to them).

## 9. Resume here (2026-11-03)

State: all committed (`81187e8`), release 0.0.0.1 built in `bin\ForzaStudioEVO_0.0.0.1.zip`. Next, in this order:

1. Click through the new FM3 windows: Create DLC Package > For FM3 (several cars, Trial Pack, Build DLC Package, Xbox 360 package, Remove Pack) and Import Car > Into FM3 (Trial Run, Install into `FM3_Extracted_DEV`); check Review and Upgrades with a trial run.
2. Click through Validate DLC for Installation with an FM3 package, then Set Generated Thumbnail on an FM3 package and on `FM3_Extracted_DEV` (save one thumbnail, look at it in game).
3. Try the STFS package of the test pack on a console (the `.fsb` banks are loose beside the zip); the Xenia copy to compare is `content\...\4D53084D\00000002\D3470C99...` / the newest `*4D` file there.
4. Review fixes for FM3 ports (the Review tab only lists), then the greyed routes in `PortPairs.cs`.
The old "Resume here" in PORT_FM4_TO_FM3.md (MG make) is closed: it was other DLC installed in the test game.

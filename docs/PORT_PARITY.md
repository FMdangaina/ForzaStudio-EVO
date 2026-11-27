# Port windows: what each game's window has (2026-11-26)

User 2026-11-26: "Create DLC package for: FM2" must be enabled and built. Before the DLC: "mancano parecchie cose,
qualcuna su fm3 e tutte su fm2. FACCIAMO UN LAVORO COMPLETO."

Three windows: FM4 (`CarPortForm`, `.Makes`, `.Upgrades`), FM3 (`CarPortFm3Form`, `.Choices`) and FM2
(`CarPortFm2Form`). All three have two modes, Import and DLC (`PortMode`); FM2 got its DLC mode with C (2026-11-27), before
that `PortPairs.CanCreateDlc("FM2")` was false and the button greyed.

## 1. What each window has

| Feature | FM4 | FM3 | FM2 |
|---|---|---|---|
| Source: game folder, **Package...** (single package files) | ✅ combo + info line | ✅ | ✅ (B2) |
| Cars: search, include the game's DLC | ✅ | ✅ | ✅ |
| **Also show cars the target already has** | ✅ (count of hidden) | ✅ "In FM3" column | ✅ "In FM2" column, Port Again (B1) |
| Traffic cars in yellow | ✅ | ✅ | ✅ (2026-11-25) |
| Source **PI** column | ✅ | ❌ | ✅ (B7) |
| Template car | ✅ ([DLC] marked) | ✅ | ✅ |
| **Arcade** | ✅ | ✅ | always (user rule) |
| **Make** + **New make** | ✅ | ✅ | ✅ (B5) |
| Engine sound: the source's / a target car's | ✅ | ✅ | ✅ (2026-11-25) |
| Tires (Normal / Offroad / Small / Offroad Big) | ✅ | ❌ | n/a (FM2 shared tire texture) |
| Front / rear brakes + caliper angle | ✅ | ✅ | n/a (FM2: no per-car brakes) |
| Trial Run, Install | ✅ | ✅ | ✅ |
| **Open Trial Folder** | ✅ | ❌ | n/a (the FM2 trial writes no files) |
| **Uninstall...** (take a ported car out of the folder) | ✅ | ❌ | ✅ (B6) |
| Tab Report | ✅ | ✅ | ✅ |
| Tab **Review** (what the port changed or could not do, **Fix** choices) | ✅ | ✅ | ✅ (B3) |
| Tab **Upgrades** | ✅ editable: swaps add / remove, drive type | read only (part, level, item, in FM3) | ✅ (B4) |
| Tabs Variants, Materials (buffer counters, material choices) | — | — | ✅ (FM2 only) |
| **DLC mode**: pack list Add / Remove, New pack, Name, Offer + Free | ✅ | ✅ | ✅ (C, Into DLC) |
| DLC: **First car id** | ✅ | ❌ | ✅ (C) |
| DLC: Trial Pack, Build for Xenia, Remove Pack | ✅ | ✅ | ✅ (C) |
| DLC: **Add to existing package** (`DlcPackageAdd`) | ✅ | ❌ | ❌ |
| DLC: Build for Xbox 360 | ✅ | ✅ | ❌ |
| DLC: the pack is **remembered** between sessions (`RestorePack`) | ✅ | ❌ | ✅ (C) |

## 2. FM2 data still the template's (rule: nothing of the template may stay)

From `PORT_TO_FM2.md` section 5:
- the car zip's `ShaderSettings.xml`;
- `List_Wheels.IconPath` (the template rim's picture);
- ids an FM2 lookup lacks (FM3 `FamilyModelID` 26) keep the template's;
- the rim size upgrade on a converted wheel: not studied yet.

## 3. Plan (order proposed to the user)

Each UI step is described to the user and gets a yes before it is built (`describe-ux-before-building`).

**A. FM2 data** (no UI):
1. `ShaderSettings.xml` from the source game's car (or FM2's typical when the source has none).
2. A make FM2 lacks: list which source makes FM2 lacks, then decide (FM2 make rows or `CarMake` for FM2).
3. `IconPath` and lookup ids: what FM2 has for them.

**B. FM2 import window**:
1. **Also show cars FM2 already has** + port again over an installed car (same id, upgrade ids kept, as `data2` does).
   DONE 2026-11-26: `Fm2PortRecord` (port_log.txt in the FM2 car zip, as the FM4 port's: source, template, variants left
   out, materials, sound, then the install report), `CarPortFm2Chain.Again` (no copy; the model built again on the first
   port's template, the zips backed up; car / upgrade / engine ids kept), window: "Also show cars FM2 already has", "In FM2"
   column (ported, car N / FM2 car), "Port Again Over Car N..." with the saved-game warning, the record's template, sound,
   variants and materials put back on the first trial; FM2's own cars go in beside them as `<name>_FM4` / `_FM3` / `_FH`.
   Records written into the 17 DEV cars (`recordtest\writerec`). User: Focus 676 OK in game, BUT its record (2026-11-26
   23:34) shows a FRESH install (template copy, id 676 again, no material choice), not a port again: B1 told ported cars
   from FM2's own by `GameIdLimits.MaxCar` (1024 for FM2: it counts the official DLC ids, which ports reuse, 659-1000), so
   every ported car looked like an FM2 car. Fixed 2026-11-27: `MaxCarBase` (617, the game's own rows) in the window and
   in `CarPortFm2Chain.Again`. Port Again still to be tried in the app (the Focus: it also drops the template's Layer_1.xds).
   **Template textures** (2026-11-27, Review found it): a car texture the source lacks, other than the four every FM2 car
   has (Damage, Damagelights, Lights, Nodamage) and the paint variants, is removed (`CarPortToFm2.Texture`; Layer_1.xds
   is on 2 of FM2's 310 cars, the Focus SVT template's had stayed on the Focus ZX4; no other DEV car has one).
2. **Package...** source. DONE 2026-11-26 (built, app test pending): a Package... button beside the source's Folder...
   (as FM3 / FM4): single package files of the source game, installed in Xenia or not, kept while the window is open; Load
   Cars stages them as the installed DLC (same `DlcSource.Stage` + `ReadCars`), their cars listed with the pack's name in
   "DLC pack"; a file also installed is read once. Packs of another game are left out with a note in the report.
3. **Review** tab: the "not ported" lines, the materials and variants left out, the template's left items, with Fix where
   the port has a choice. DONE 2026-11-26 (built, app test pending): a Review tab (Car / Where / What / Fix), filled by
   the trial run and by the install. The trial run now also runs the database step on an in-memory copy of FM2's database
   (`CarPortFm2Form.DryRunData`: lookups + `CarDataToFm2` over the template's rows for a new car, over the car's own for a
   port again; nothing written, checked: no DEV file changed). Rows from the logs (`ReviewFromLog`): yellow = not ported
   (engine parts FM2 lacks, tyre level 4...) / a template texture that stays; red = failed; blue = added to FM2 (make, part
   maker, cylinders); green = fixed by the port (collisions, plate, UVs); white = info (parts left out / removed, shader
   settings). Materials no FM2 car uses carry a Fix combo, the same choice as the Materials tab (green = what FM2's cars
   give those parts, `MaterialSuggestions.ForGame`); variants left out: double-click opens Variants. Offline (harness
   `PortToFM2\reviewtest\dry`, `rl`): RUF port again 3 yellow rows; FM4 Veyron on the F430: + Bugatti make 58 and part maker
   307 in blue; the Focus install log: Layer_1.xds of the template stays (yellow).
4. **Upgrades** tab: each part and level, the source item and what FM2 got (read only, as FM3's).
   DONE 2026-11-27 (built, offline checks, app / game test pending), EDITABLE as FM4's after the user's correction: FM2
   HAS engine swaps (List_UpgradeEngine IsStock 0, levels 1-3: 130 / 59 / 21 rows on 130 cars; engines shared: 41 on
   several cars, 54 also a stock engine) and keeps the drive type on the ENGINE (`Data_Engine.DriveTypeID`): a drive
   conversion is a swap to an engine of another drive type (71 of FM2's swaps, e.g. 206 RC -> 207 RC Cup AWD). Until now
   the port dropped the source's swaps ("not ported: engine swap").
   - `CarDataToFm2.WriteSwaps` (`Fm2Swap` kind source / fm2 / drive, `Swaps` null = the source's): a source swap uses
     FM2's engine of the same MediaName AND the car's drive type (FM2's VW_BoraVR6_03 is AWD, the FM4 Beetle with it
     FWD), else it is converted as an own engine like the stock one (`ConvertSwapEngine`: Data_Engine, part tables,
     curves; MediaName = the car's, FM2 has no sound of the source engine); "fm2" = an FM2 engine as it is; "drive" = a
     copy of the car's engine (`CopyStockEngine`) with the other DriveTypeID. One swap per level 1-3, other levels "not
     ported". Row: the car's make (`EngineMaker` now sets every engine row: 210 / 210 FM2 swap rows), the source's price
     and mass (FM2's median at that level for fm2 / drive). A run again drops the earlier run's own swap engines and the
     new ones take their ids back. Offline (`reviewtest\swapdry`, `dry2`, in memory): Beetle 2 source swaps, + AWD + 959,
     level 5 refused; R33 over its own car keeps engine 2152, swap GT-R Spec V; `DbIds.CheckCar` 0 errors.
   - Window (`CarPortFm2Form.Upgrades.cs`): swap list with ticks (ported / left out), Level of the selected, Add FM2 engine
     (FM2's own engines, with drive) + level, Remove; Drive conversion none / FWD / RWD / AWD + level; below the read-only
     table Part / Level / Source / FM2 / Note from the dry run (`UpgradeTable`: the source tables the port converts from,
     `CarDataToFm2.SourceUpgradeRows`; levels lined up on the stock levels). Choices: the trial's, the install's
     (`CarPortFm2Chain.Swaps`) and the record's (`swaps=` / `swap=kind:value:level`).
5. **Make** + New make for FM2. DONE 2026-11-27 (built, offline checks, app / game test pending):
   - Window (`CarPortFm2Form.Makes.cs`): a Make choice beside the template: "the source's: <name> (id[, added to
     FM2])" or any FM2 make; "+" opens `NewMakeForm` in its FM2 mode (name, code, country, profile, logo image; FM2's
     countries only, no New country: FM2's country rows carry a flag symbol of its font; only "Create in the game", the
     pack button comes with part C).
   - `Fm2LookupPort`: `FreeMakeId` (above every make id of every Forza, GameIdLimits, and FM2's: 108 today),
     `MakeSpecProblem` (id above 53 and free, name, code 2-4 [A-Z0-9] not used by FM2, an FM2 country, logo name not in
     FM2's logos, logo image), `AddMake` (row, name / profile in every language, logo laid out as FM2's Acura logo via
     the shared `PutLogo`); the window backs up gamedb.slt and the lookups' zips, then commits.
   - `CarDataToFm2.MakeOverride` / `CarPortFm2Chain.MakeId` / record `make=`: a chosen make puts the car (and, through
     `EngineMaker`, its engine rows) under it; the source's make is then not added (no `PrepareMake`, no country check).
   - Offline (`reviewtest\mkt`, `dry2 make=13`, in memory, nothing written): make 108 TST with its strings and logo; bad
     specs refused (code NIS, new country, id 40, logo Acura); R33 under make 13: Data_Car and the engine rows 13.
6. **Uninstall...**. DONE 2026-11-27 (built, plan checked offline, app / game test pending):
   - Window (`CarPortFm2Form.Uninstall.cs`): Uninstall... beside Install, on a ported car only (In FM2 "ported, car N",
     id above FM2's own). The plan (`CarUninstall.Plan`) goes into the report, then a confirmation with the saved-game
     warning and XeniaGuard; every file backed up (`bak\`). The row is a car "not in FM2" again afterwards.
   - `CarUninstall` (its FM2 / FM3 part, CarUninstall.Copy.cs, already used by Delete Car) also takes out, for FM2, what
     the port added to the lookups (Fm2LookupPort) when no car uses it any more: makes > 53 (the row went with step 2;
     now its List_CarMake.str strings and its logo), part makers > 289 (the relation map does not link every upgrade
     table's ManufacturerID to them: their use is counted in every List_Upgrade* table but List_UpgradeEngine; row,
     logo), cylinders > 10 (List_Cylinders.str string).
   - Plan offline (`reviewtest\unplan`, nothing written): RUF 675: make 91 + 2 strings + L_MAN_RUF_Logo, part maker 306 +
     decal402; 107 670: part maker 302 + decal398, cylinders 11 + its string; 240SX 671: URAS 303 + decal399 out, Nissan
     290 kept (other cars use it).
7. Source PI column (built 2026-11-27): "PI FM4" / "PI FM3" / "PI FH" (Data_Car.PerformanceIndex of the source) and "Class" (the source game's badge, `CarPortForm.OwnClass` on its own CarClasses; empty for a database without that table) after Media name; the chosen car's PI on the status line. FM2's PI and class are computed by the port (its log), never estimated in the list. App test pending.

**C. FM2 DLC mode** ("Create DLC package for: FM2"), port first, then into the pack (user 2026-11-26):
1. The same window in DLC mode: after the car's trial, **Into DLC →** puts it in the pack with all its choices
   (template, variants, materials, sound).
2. Pack panel: list with Remove, New pack, Name, Offer + Free, First car id (659-1000), Trial Pack, **Build DLC Package**
   (Xenia's FM2 content folder, `DLC<offer>`), Remove Pack, the pack remembered between sessions.
3. `BuildFolderFm2` extended with HarmonicTuning and `Soundbanks\LOD1/LOD2` (DLCBeta's layout) for "Port sound".
4. Later: Add to existing package, Xbox 360.

**C built 2026-11-27** (user "sì, marche dentro il pacchetto, fai C"; `CarPortFm2Form.Pack.cs`):
- `PortPairs.CanCreateDlc("FM2")` on; the top bar opens `CarPortFm2Form(PortMode.Dlc)`: title "Create DLC Package > FM2",
  banner "DLC PACKAGE", **Into DLC →** in Install's place, no Uninstall. The FM2 folder is only read.
- Into DLC keeps the trial's choices as an `Fm2PortRecord` (template, variants left out, materials, sound, swaps, make).
  A car already ported into the FM2 folder (In FM2 "ported, car N") is refused: a pack car needs a name the game lacks.
  Into DLC again on a pack car replaces its choices. Double click on a pack car selects it in the list on the left.
- Pack panel: Name, Offer id + Free (`FreeOfferFm2`), First car id + Free (659-1000, not used by the FM2 folder nor by
  another installed pack), Xenia folder, New Pack, Remove, the list (Car, Media name, Template, Id after the trial),
  Trial Pack, Build DLC Package, Remove Pack. Saved in ForzaStudio.ini (`PortFm2.Pack*`, the choices as record lines
  joined by `|`), restored when the same source folder is loaded.
- **Trial Pack**: stage `%TEMP%\ForzaStudio\PackFm2\pack_<time>` (older stages deleted: each holds Textures.zip) =
  `CarDuplicate.Stage` per template (database, zip list, string tables, template car / wheel / tyre zips, thumbnail
  zip, EmitterData / DSPTuning) + `UI\Textures.zip` + `HarmonicTuning.zip` + empty `Soundbanks\LOD1/LOD2`; the pack's new
  makes (`WriteMake`), then `CarPortFm2Chain` per car with `PackStage` (no backups; CarDuplicate `GameOverride` FM2,
  `RefreshNames` off, `PerfMedia` and `PerfRoot` = the FM2 folder's AI\PerfPrediction), `ChosenId` = the next free id
  from First car id. Each car's log lines go to the Review tab. Refused while `DLC<offer>` is in Xenia (Remove Pack first).
- **Build DLC Package**: `BuildFolderFm2` (stage against the FM2 folder), `DbIntegrity.CheckMerge` against the installed
  packs, `ZipLayerFm2`, `RefreshNamesPack`; on any error the half-built folder is removed. **Remove Pack**: `RemoveFm2`
  and the names pack again. Both behind XeniaGuard.
- **Makes in the pack** (user): a make FM2 lacks goes into the pack as it goes into the folder on Install: row in the
  merge, strings in the override tables, logo as a loose `UI\Textures\L_Map\...` file. "+" New make in DLC mode keeps
  the make for the pack (NewMakeForm "add to pack"), the trial's dry run adds it in memory, Trial Pack writes it into
  the stage. Not remembered between sessions (a saved car whose make is gone gets the source's make, with a note).
- C3: the sound needed no new code in `BuildFolderFm2`: the zip entries the port added land as loose files at the
  zip's mount (`Audio\Cars\Engines\HarmonicTuning\`, `...\DSPTuning\`, `Audio\Cars\EmitterData\`), the banks as
  `Audio\Cars\Engines\Soundbanks\LOD1|LOD2\*.fsb`; FM2 packs now also take a loose file the game has when its bytes
  differ (`CopyPortedFilesFm3(..., changedToo)`; FM3 packs unchanged).
- Offline check (`E:\DEV\fm4dev\PortToFM2\packtest`, FM2_Extracted only read, fake content folder): FH1 R8 GT
  `AUD_R8GT_11` on `AUD_RS6_03` -> car 659, merge check OK, layer 37 files (car, wheel, 4 banks, 2 HTs, DSPTuning, ED,
  part maker logo decal386, thumbnail 659, merge), strings DLC9Overrides1_pri_159. The window off screen
  (`packtest\ui\shotdlc`): Trial Run, Into DLC, Trial Pack -> pack row id 659, Build off without a Xenia folder.
- Open: the in-game test (pack built from the app into Xenia, FM2 with its title update): car in the dealer's DLC,
  its sound, a new make's logo from a pack. The DLC merge has no ArcadeCarList (with it FM2 hung, 2026-11-14), so a
  pack car is not in Arcade unless the official packs' way says otherwise. A source bank named only "NA" (FH1 R8 GT)
  keeps that name: two such cars in one pack would share NA.fsb / NA_HT.xml (the Install path has the same naming).

### A done in code (2026-11-26, built, trial-folder / dry-run checks; DEV rewrite of the ported cars pending)

- **A1 ShaderSettings** (`CarPortToFm2.PortShaderSettings`): FM2 has the file on 116 of 310 cars; same layout in every
  game; where FM2 and FM3 give the same material the same parameter the value is the same (53 / 53, 50 / 51). The
  template's ShaderSettings / ColorShaderSettings<n> are removed; the source's kept with the materials the ported model
  uses (renamed ones under the new name) and the 54 parameters FM2's cars use; nothing left = no file.
  ColorShaderSettings<n> follows the colour's Sequence, which the port keeps.
- **A2 makes** (`Fm2LookupPort.PrepareMake`, user "come FM3"): make ids are the same in every Forza; FM2 has 1-53. A
  make FM2 lacks gets its row on the source's id, name / profile in every language, and the logo in FM2's one folder
  (`L_Map/Manufacturer_Logos`, laid out as FM2's, margins cut). Holden / Joss (Australia, FM2 has no such country: its
  rows carry a flag font Symbol) are refused before anything is written. H1 663 was Nissan, Yellowbird 675 Porsche.
- **A3**:
  - `List_Wheels.IconPath` = `...\Licensed_Rims\<own wheel>.tga` (FM2: 373 of 373; a picture exists for 1 of 282
    stock wheels).
  - **Cylinders** by Number + Rotors (FM4 10 electric / 11 inline 3 / 12 four rotors vs FM2 10 four rotors): 787B was
    a V8, 107 a four. FM2 lacks the inline 3: row 11 added with its description.
  - **Part makers** by name (224 ids same name, 55 another name, FM2 lacks 290): a trailing "(Japan)" is left aside,
    a source "Forza ..." is FM2's "Forza" (35), a maker FM2 lacks (URAS, RUF, Peugeot, Nissan...) gets a row and its
    logo as the next `decal<n>`.
  - `CarDataToFm2.IdResolver` hands CylinderID, upgrade ManufacturerID and Data_Car.MakeID to `Fm2LookupPort.Resolve`.
- **Collisions** (user rule 2026-11-26: invalid collisions are fixed automatically, FM2 / FM3 / FM4):
  - FM2: spheres with Flags 2 or 3 are kept, only Flags 1 is dropped. The FH1 Focus ZX4 676 crashed FM2 on selection
    with an empty CollSpheres; 44 FH1 and 2 FH2 cars had only Flags 3.
  - FM2: no valid sphere, or FH's placeholder (fewer than 10), gives FM2's typical 20 spheres in the car's box: the
    medians of its 308 cars with 20 (`shtest\sph.cs`).
  - FM4 / FM3: the FM4 port already rebuilds the placeholder, but on traffic cars only (user decision 2026-10-18).
    Whether driveable cars too is asked to the user.
- **FM4 / FM3 placeholder collisions**: user "sì a tutto": `CarPort.ReplacePlaceholderCollision` now rebuilds every
  placeholder, driveable cars too (the 2026-10-18 traffic-only decision is replaced).
- **DEV rewrite 2026-11-26** (`swayfix\report_all.ps1`: `port2` with the original template found by `tplfind` from
  the install backups, then `data3` = the chain's database step with the lookups): all 17 ported cars 660-676, integrity
  OK. R33 (`skip=bumperFb`), Cobalt (its 8 front bumpers out) and RS4 (its 5 material choices) re-run with the original
  choices. Results: H1 Hummer, Yellowbird RUF, 107 3 cylinders (row 11), 787B 4 rotors (FM2's 10), Focus 676 20 spheres
  (fixed automatically), part makers added (Nissan, ABT Sportsline, Audi, H&R Europe, Ford, Chevrolet, Peugeot, URAS, BMW,
  Mazda, RUF...). In game: user. The RUF Yellowbird 675 crash (user 2026-11-26) was the SAVED GAME (a garage car bought
  before the rewrite; a new one from the dealer works); on the way H1 (make 85 above FM2's 53), RS4 and 240SX (new part
  makers 292 / 303) and the RUF with its FH2 ShaderSettings were OK in game. The rest: UNTESTED.md D9.
- **Choices lost by the rewrite** (user: RUF materials wrong): `report_all.ps1` ran without the material choices of RUF,
  M5 E39, M6, 107, 787B, 240SX and Focus. Rebuilt from the old zips (agent harness `ruf675mat\matcmp` / `matmap`) and
  restored (`swayfix\restore_mat.ps1`, model step only): 6 cars mesh for mesh as before, the RUF 212 of 216 paired meshes
  (the 4 others: names FM2 already uses, never offered in the Materials tab). This is why B1 keeps a port record.
- **787B 674 brakes** (agent, `brakes674\`): the wheel's spoke face starts 0.146 m deep (FM2's deepest 0.076): test
  `List_Wheels.BrakeDrawOffset` -0.05 / -0.10 / -0.15 in game, then make it a port rule.

**D. FM3 window** (what FM4 has and FM3 lacks): Uninstall, First car id, the pack remembered, Add to existing package,
Upgrades editable (swaps, drive type), source PI column.

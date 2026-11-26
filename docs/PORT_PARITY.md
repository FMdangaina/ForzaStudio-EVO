# Port windows: what each game's window has (2026-11-26)

User 2026-11-26: "Create DLC package for: FM2" must be enabled and built. Before the DLC: "mancano parecchie cose,
qualcuna su fm3 e tutte su fm2. FACCIAMO UN LAVORO COMPLETO."

Three windows: FM4 (`CarPortForm`, `.Makes`, `.Upgrades`), FM3 (`CarPortFm3Form`, `.Choices`) and FM2
(`CarPortFm2Form`). FM4 and FM3 each have two modes, Import and DLC (`PortMode`). FM2 has Import only, and
`PortPairs.CanCreateDlc("FM2")` is false, so the button is greyed.

## 1. What each window has

| Feature | FM4 | FM3 | FM2 |
|---|---|---|---|
| Source: game folder, **Package...** (single package files) | ✅ combo + info line | ✅ | folder only |
| Cars: search, include the game's DLC | ✅ | ✅ | ✅ |
| **Also show cars the target already has** | ✅ (count of hidden) | ✅ "In FM3" column | ❌ |
| Traffic cars in yellow | ✅ | ✅ | ✅ (2026-11-25) |
| Source **PI** column | ✅ | ❌ | ❌ |
| Template car | ✅ ([DLC] marked) | ✅ | ✅ |
| **Arcade** | ✅ | ✅ | always (user rule) |
| **Make** + **New make** | ✅ | ✅ | ❌ (`MakeID` copied; `CarMake` has no FM2) |
| Engine sound: the source's / a target car's | ✅ | ✅ | ✅ (2026-11-25) |
| Tires (Normal / Offroad / Small / Offroad Big) | ✅ | ❌ | n/a (FM2 shared tire texture) |
| Front / rear brakes + caliper angle | ✅ | ✅ | n/a (FM2: no per-car brakes) |
| Trial Run, Install | ✅ | ✅ | ✅ |
| **Open Trial Folder** | ✅ | ❌ | n/a (the FM2 trial writes no files) |
| **Uninstall...** (take a ported car out of the folder) | ✅ | ❌ | ❌ |
| Tab Report | ✅ | ✅ | ✅ |
| Tab **Review** (what the port changed or could not do, **Fix** choices) | ✅ | ✅ | ❌ (Report lines only) |
| Tab **Upgrades** | ✅ editable: swaps add / remove, drive type | read only (part, level, item, in FM3) | ❌ |
| Tabs Variants, Materials (buffer counters, material choices) | — | — | ✅ (FM2 only) |
| **DLC mode**: pack list Add / Remove, New pack, Name, Offer + Free | ✅ | ✅ | ❌ |
| DLC: **First car id** | ✅ | ❌ | ❌ |
| DLC: Trial Pack, Build for Xenia, Remove Pack | ✅ | ✅ | ❌ (`DlcPackage.BuildFolderFm2` exists, ST-02) |
| DLC: **Add to existing package** (`DlcPackageAdd`) | ✅ | ❌ | ❌ |
| DLC: Build for Xbox 360 | ✅ | ✅ | ❌ |
| DLC: the pack is **remembered** between sessions (`RestorePack`) | ✅ | ❌ | ❌ |

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
5. **Make** + New make for FM2.
6. **Uninstall...**.
7. Source PI column.

**C. FM2 DLC mode** ("Create DLC package for: FM2"), port first, then into the pack (user 2026-11-26):
1. The same window in DLC mode: after the car's trial, **Into DLC →** puts it in the pack with all its choices
   (template, variants, materials, sound).
2. Pack panel: list with Remove, New pack, Name, Offer + Free, First car id (659-1000), Trial Pack, **Build DLC Package**
   (Xenia's FM2 content folder, `DLC<offer>`), Remove Pack, the pack remembered between sessions.
3. `BuildFolderFm2` extended with HarmonicTuning and `Soundbanks\LOD1/LOD2` (DLCBeta's layout) for "Port sound".
4. Later: Add to existing package, Xbox 360.

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

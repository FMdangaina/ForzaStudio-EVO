# Changes made and NOT tested (started 2026-10-25, reviewed 2026-11-05)

Everything below is coded, compiles and is committed. Use this file to resume: test each point, then tick it here
and update `TODO.md` (state and date). Roadmap numbers in brackets. Review of 2026-11-05: the points closed by the
pre-release tests of 2026-11-03/05 (`PRERELEASE_TESTING.md`) are ticked with the point that closed them; still open:
C2, D5, D8 and the dash editor E1-E4 (experimental in the release).

Build: Visual Studio 2022 MSBuild, .NET Framework 4.8, x86 (`README.md`). Test game: `F:\Xenia\Games\FM4_Extracted_DEV`.

---

## A. Game database view (session 2026-10-23) [topic 18]

Code: `ForzaStudio/ArchiveEditorForm.GameDb.cs`, `ArchiveEditorForm.Creators.cs`, `ArchiveEditorForm.Navigation.cs`, `GameDatabase.cs`.
The toolbar now has groups: **Cars Wheels Tracks Events | Engines Makes Countries Manufacturers | Tables**.
A section appears only if the database has its table.

- [x] A1. **Open the database** (a crash here was fixed: `SectionTables` lacked the Lists entry; commit 5611985). Check it opens and every button works.
- [x] A2. **Engines** (`Data_Engine`): columns Id / Engine / Media name / Config / Cars (cars using it); tab "Engine" (record), tab "Cars and curves"
      (rows of `List_UpgradeEngine`, `Combo_Engines`, `List_TorqueCurve`). Go to on a car's engine opens this section.
- [x] A3. **Makes** (`List_CarMake`): logos (`IconPath` + `IconPathLight`; Chrysler, Infiniti, Jaguar, Mitsubishi have no light file),
      columns Id / Make / Code / Country / Cars, tab "Cars and parts" (cars, engine and drivetrain upgrades, decal order, part discounts).
- [x] A4. **Countries** (`List_Country`): flag (10 of 22 have none), Makes count, tab "Makes and cars" (makes, cars, environments, AI players).
- [x] A5. **Manufacturers** (`List_PartManufacturer`, NOT the same as car makes): logo built as
      `GAME:\Media\UI\Textures\UI4\Logos\Car_Parts_Logos\<LogoPath>.xds` (only ~232 of 569 files exist in FM4; empty box is normal);
      tab "Parts" lists every `List_Upgrade*` table with `ManufacturerID` except `List_UpgradeEngine` / `List_UpgradeDrivetrain`
      (those point at car makes), plus wheels (`PartManufacturerID`), tire brands (`TireBrandId`), part discounts (`BrandId`).
- [x] A6. ~~Lists~~ REMOVED 2026-10-24 (user: "non ha molto senso usare"): the picker over 15 id lists is gone; Go to / Used by to those tables opens them in Tables.
- [x] A7. **Events header picture**: poster of the career type (`CareerEventTypes.PosterPath`; 15 of 32 types have one), else the first race's
      track logo. The header text and buttons moved right to make room.
- [x] A8. Related-row counts no longer raise an error for a table that lacks the column (another game version).

Ideas not done: "Used by" column in the lists, filter "only used", button "new row (copy of this)", light make logo falling back to the dark one.

## B. Engine designer: paste from Excel (2026-10-23) [2.3.2]

Code: `ForzaStudio/EngineDialog.cs`, `PasteTorque`.

- [x] B1. (removed 2026-11-03: the Excel paste is gone, the curve generator stays; user) Ctrl+V in the torque table with **two columns** (rpm, N.m at any rpm, tab / `;` / space separated, comma decimals accepted, a header
      line skipped): the curve is interpolated onto the table's rows (every 250 rpm); rows outside the pasted rpm range stay as they were.
- [x] B2. (removed 2026-11-03 with B1) One column of N.m still pastes from the selected row down (old behaviour).

## C. Duplicate Car wizard (2026-10-23) [2.3]

Code: `ForzaStudio/DuplicateCarForm.cs`.

- [x] C1. (Duplicate Car closed in game 2026-11-03, NOW point 2) "in a new DLC pack" can always be chosen. If Xenia's folder is not in Settings, a folder dialog asks for it, remembers it
      (`CarPort.Xenia`) and finds the next free offer. Cancel or a bad folder returns to "in the game folder".
- [ ] C2. **The whole wizard still needs a usability pass** (user 2026-10-23: "tutto il wizard sarà da migliorare nell'utilizzo"):
      collect what is awkward while testing, then change it (TODO D-1).

## D. Work already in the tree before today, still untested [topics 1, 2, 6]

From the sessions of 2026-10-22 (see `TODO.md`, section 1 and 2 for the full notes):

- [x] D1. (closed in game 2026-11-03, NOW point 2) Duplicate car in the game folder and into a DLC pack (`CarDuplicate.cs`) [2.2]; checked on copies only.
- [x] D2. (closed in game 2026-10-27, 2.3.2) Own engine for the copy (`CarEngine.cs`, `EngineDialog.cs`, `PowerGraph`): unique `Data_Engine.MediaName`, Sim* values, upgrade levels [2.3.2].
- [x] D3. (confirmed 2026-11-05, 1.3 + car * 1000 + n rule) Id rule above `GameIdLimits.json`, ModelId = Id (`DbIds.cs`) [1.1, 1.2]; integrity rules (`DbIntegrity.cs`) used by the SQL console, duplicate and port [1.3].
- [x] D4. (confirmed 2026-11-05, 1.4) SQL console and diagnostics (`SqlConsoleForm.cs`, `SqlGuard.cs`, `SqlDiagnostics.cs`) [1.4]; EventRestrictions decode (`EventRestriction.cs`) [1.5].
- [ ] D5. DRIVER tab in the model view (`ArchiveEditorForm.Driver.cs`) [6.2].
- [x] D6. (closed in game 2026-11-04, NOW point 8: S65 AMG with donor 500 Abarth deforms well) Crash deformation: `Port.ini [Damage] Words = donor` (default; `donorcar`, `copy`, `normalize` + `Tables`, `FlipSections` kept) and `[Lod0] MoveSections` (headlights from the cockpit file to `_lod0`) [3.1, B1]. Checked in game 2026-10-25 on the MP4-12C `_FH` copy, the P1 (donor MP4) and the FH2 Corvette Stingray (donor ZR1 09; a first port made before `donor` barely deformed, re-ported it deforms well); other cars (FH2, FM3, rear sections the donor lacks) still to try.
- [ ] D8. Import window (Tools > Import Car): the source game's DLC listed with a DLC column (tick "Include the game's DLC"); tick "Also show cars FM4 already has (for tests)" and such a car is ported beside FM4's own as `<MediaName>_FH` with " FH" in its names (`CarPort.RenameTo`, Uninstall removes only the copy). Trial run on the Countach and MP4-12C (FH1) OK, window not clicked through.
- [x] D7. (packs, makes and generic brakes closed in game 2026-11-04, NOW points 3, 4, 10; FM2 port 5.1 done 2026-10-21) Several packs at once (string layers) [7.1]; new makes [8.1]; Add a song [13.1]; generic brakes [3.3]; FM2 port [5.1].

## E. Digital dash editor (made in a parallel session, 2026-10-23; committed together with this file) [12]

Code: `AnarkBgf.cs`, `AnarkBsg.cs`, `AnarkDash.cs`, `AnarkDashCanvas.cs`, `AnarkFbf.cs`, `ArchiveEditorForm.Dash.cs`, new `DashInspector.cs`;
notes in `docs/MANUAL.md` and `TODO.md` (12.3, 12.5, 12.6, 12.7). Not written by the database session; read those notes before testing.

- [ ] E1. Property inspector + drag on the preview (move, 8 resize handles, arrows) [12.3].
- [ ] E2. Multi-selection (Ctrl+click, rubber band, Ctrl+A, align / spread) [12.5].
- [ ] E3. Duplicate node (Ctrl+D): **the first version crashed the game** (gear text copy, 2026-10-26); reworked (copy goes at the end under the backdrop group, listed in the scene's slides, bsg material table updated): 1529 copies checked offline on FM4 / FH2 / FH1; in game: does the copy load now? [12.6].
- [ ] E4. Delete (Del) and Add > Text / Rectangle / Image: offline checks only (remove gives the original files back); in game: Add a text, a flat rectangle and an image on the Alfa Brera dash, then Delete the added ones [12.7].

---

## F. Duplicate Car: engine, sound and performance (2026-10-24, after the first in-game look at a MiTo copy) [2.3.2]

Code: `EngineDialog.cs`, `CarEngine.cs` (`StockMultipliers`), `CarPerformance.cs` (new), `CarDuplicate.cs`.

- [x] F1. Engine dialog **reopens with what was designed** (it started again from the source's engine each time).
- [x] F2. Pasting two columns sets the **redline to the last pasted rpm** (no old rows left above it); rows below the first pasted rpm stay.
- [x] F3. The torque / power shown in the dialog are **what the game shows** (curve x stock parts' TorqueScale x the turbo's MaxScale when the
      engine is boosted): paste 310 N.m, see 310 N.m in game (before: the MiTo copy showed 544 N.m / 150 kW, the dialog said ~320 / 88).
      Changing the aspiration changes the numbers.
- [x] F4. Sound: the copy gets `<car>_CMT.xml` (CarModelTuning.zip) and, with an own engine, `<car>_ET.xml` (enginetuning.zip, a copy of the
      chosen / source engine's) and `Data_Engine.MediaName` = the car's name (as the port does). In game: the picked engine's sound? (the MiTo
      copy had MediaName = MAZ_MX5SL_10 and played something else). Game folder AND pack (the pack stage now includes both zips).
- [x] F5. Performance recomputed for an own engine (`CarPerformance.Update`): Sim times / top speed from a longitudinal simulation (checked on the
      501 stock cars: top speed and quarter mile within ~3%, 0-60 within ~8%) as a RATIO on the source's values; ratings and PerformanceIndex from
      the ratings relations (R2 0.97 / 0.989); class from the PI; the shown figures (0-60, top speed...) scaled unless typed in the wizard.
      **Compare the PI shown in game with the plan line "performance ..."**: the MiTo copy showed D 292 in game (stored PI 336 then), the
      estimate for a similar engine is lower than 336 too, but the game seems to compute its number live, so it may differ.

## G. Delete Car (2026-10-24) [2.2]

Code: `CarUninstall.cs` (now also for copies, no `port_log.txt`), `ArchiveEditorForm.GameDb.cs` (`DeleteCar`, button in the car header).

- [x] G1. Database view > Cars: **Delete Car...** (enabled only for a car above FM4's own ids, with the game folder open). A window lists
      everything that goes; OK deletes (files backed up to `bak`), then the view reloads. Xenia must be closed.
- [x] G2. A copy made by Duplicate Car goes away completely: its rows, its own engine and curves, its strings, `<car>.zip`, thumbnail, `_CMT` /
      `_ET`. A base car is refused. The tuning file of an engine whose MediaName another engine has (old copies that played the MX-5's sound)
      is kept. Plan-only check on the MiTo copy (ALF_MitoJTDm_09) done; nothing deleted yet.

## Where things are

- `ROADMAP.md`: goals. `TODO.md`: work, test queue, states and dates. `docs/RESEARCH_NOTES.md`: research notes.
- This file lists only what is coded and not yet seen working; when a point is tested, tick it here and change its state in `TODO.md`.

## 2026-10-25 (compiled into bin\Debug, NOT committed, not tried in the UI)
- [x] (closed 2026-11-04, NOW point 7) Viewport env reflection (EnvReflection.cs, Resources\EnvMap.png sphere map, Reflectivity per ShaderKind) + 4x antialiasing: check the look, tune `ShaderLook.Reflectivity`.
- [x] (closed 2026-11-04, NOW point 7) Material check by what FM4 really uses per kind and LOD (Fm4MaterialUse.cs, docs\FM4\Fm4MaterialUse.txt from fm4dev\Materiali\black\matscan.exe: base + 20 DLC packs, SLOD/Autovista left out, valid = in one carbin). Editor tree turns red per file kind (car LOD0 / LOD1-5 / cockpit / wheels LOD0-5), port report and rename list use it. Checked with usecheck.exe: FM4 Supra and a wheel 0 flagged, the FH Supra flags lights_amber.
- [x] (closed 2026-11-04, NOW point 7) Indicators of FH ports map to detail_glass_amber (lights_amber rendered black in game): re-port a car and look at the arrows.
- [x] (closed 2026-11-04, NOW point 7) Viewport lights up the node selected in the model tree (orange glow; part, LOD or material; file node = none).
- [x] (in use since 2026-10-25; Settings ALL closed 2026-11-04) ONE settings file: ForzaStudio.ini next to the exe replaces settings.txt + Port.ini + Upgrades.ini (AppIni.cs; [App] = old settings.txt, port sections, upgrade sections). First start folds the old files in and moves them to bak\. Restore Defaults buttons now reset only their own sections (IniFile.ResetSections). Migration tested on a copy (values, ';' in values, set/get); the Settings dialog itself not tried.
- [x] (closed 2026-11-04, NOW point 7) Viewer tab: body colour of the car in the viewport ([App] Viewer.BodyColor, RRGGBB; paint materials without texture). Reflections lowered (Paint 0.22 ...).
- [x] (closed 2026-11-04, NOW point 7) Settings > ALL tab (IniEditorPanel.cs): every section and key of ForzaStudio.ini editable, [App] included (34 sections). Checked headless on a copy: tab count, sections, App rows, a [Lod0] edit written; not tried with the mouse.
- [x] (kept as it is by the user 2026-11-04, NOW point 8) Port decides NO material by itself (AutoMap = nothing; MaterialMap kept only for nothing now): every material FM4 does not use for that file kind + LOD is reported and goes to the review, nothing preselected (Issue.Suggested = null). Review/rename suggestions come from docs\FM4\Fm4PartMaterials.txt (what FM4 models give each part, with counts; matscan parts.txt) + shader similarity; the hand tables (port table, ByName, BySection) are gone. Counts are "models" (base zips + DLC folders), not exactly cars.

## Z. Engine, gearbox and DLC package of Duplicate Car (session 2026-10-25) [TODO D-9, D-10, D-11]

Engine designer and the turbo rules were checked in game on the Brera 2.4 JTDm copy (155 kW, 400.1 N.m, plateau from ~1800 rpm). Not yet used:

- [x] (closed 2026-11-03, NOW point 2) Aspiration menu in game for the copy's turbo: stock turbo + 2 levels + stock intercooler; what the menu shows (a single -> single swap was seen before the intercooler fix); the turbo upgrade must raise the power.
- [x] NA copy made turbo and turbo copy made NA (stock / upgrade turbo and intercooler rows, conversions with no supercharger on a diesel): in game. Confirmed in game 2026-10-26 (petrol and diesel).
- [x] Supercharged copy (natural or turbo engine made positive-displacement / centrifugal supercharger, and back): the aspiration menu shows the supercharger, power and torque in game; engine graph draws the margin past the redline dotted (2026-10-26, TODO D-9). Confirmed in game 2026-10-26 (8C supercharged: perfect).
- [x] New gearbox in the wizard (page 4, "A gearbox of its own"): 5 gears and other counts, reverse, final drive; the copy's top speed and 0-100 in the menu; the source car keeps its own. Confirmed in game 2026-10-26.
- [x] (checked by the user) Tab "Transmission" of the Game Database on a Supra RZ (checked by the user: ok) and on a copy with its own gearbox.
- [~] "As a DLC package file (STFS)": the file built in a chosen folder, loaded in Xenia (done once on the Brera) and on the console (not done). Xenia ok; console STFS left for the future.
- [x] (closed 2026-11-03, NOW point 2) Generate curve with a torque range, a petrol engine and an engine with a limiter below 4000 rpm; the dialog's table (one row per 100 rpm) typed and pasted.
- [x] (closed 2026-11-03, NOW point 2) Engine name follows layout / cylinders / aspiration; typed over it stays.

# ForzaStudio EVO TODO

What we have to do, with the state and the date it was reached. The **goals** are in `ROADMAP.md`; the
numbers here are the roadmap's permanent numbers (topic `N`, point `N.m`). Research notes: `docs/RESEARCH_NOTES.md`.

How we work:

- Every line carries dates: `coded` (code in git), `tested` (seen working), `done` (tested and closed by the user).
- A point is **done** only when it works (in game if it touches the game), is committed, and the user closed it.
  Done points go to the log at the bottom, never mixed with open work.
- New ideas go in as ⬜ (or 💤 "later"), never into work in progress.
- **Game folders:** tests and installs go only to `FM4_Extracted_DEV`. `FM4_Extracted` is the clean copy, never written.
  Tests live in `E:\DEV\fm4dev\<topic>`.

Status: ⬜ to do · 🔧 coded, not tested · 🧪 tested without the game, in game pending · ⏸ paused · 💤 maybe later · ✅ done (log only)

Last review: 2026-11-05. Everything is committed and pushed (tag `v0.0.1`, commit 3818047); the doc review of 2026-11-05 comes after it.
Build: `C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe ForzaStudioEVO.csproj /p:Configuration=Debug /p:Platform=x86` (the Framework MSBuild fails on resgen when the resource cache in `obj` is touched); Forza Studio must be closed.

---

# NOW: FM4 first (2026-11-03; session results 2026-11-04 in `PRERELEASE_TESTING.md`)

FM4 was not finished when FM3 and the viewport tools came in. Until this list is closed, FM3 and "every game to every game" (goal 18) are **paused**: no new work there, only bug fixes. One item active at a time, closed (tested, committed, user says so) before the next. Goals in `ROADMAP.md`; test places: `FM4_Extracted_DEV`, Xenia closed before writes.

## A. Coded, never seen working: look at it, tick it, or fix it
1. **Database view** ✅ CLOSED 2026-11-04 (user: all OK, UNTESTED A1-A8 ticked). Was (UNTESTED A): Engines / Makes / Countries / Manufacturers closed by the user 2026-10-27 (T-ED1..4), tick A1-A8 in UNTESTED.md; A5-A8 not named by him: check.
2. **Duplicate Car** ✅ CLOSED 2026-11-03 by the user (Supra SZ: manifold fix, engine sound swap, Delete Car removes everything; 118i dropped; copying a DLC car stays open as 2b). Was (UNTESTED C, F, Z): F5 performance (compare PI in game with the plan line), aspiration menu of a copy's turbo upgrades, Generate curve with a low limiter, engine name following layout / cylinders / aspiration, Delete Car on a real copy (G1, G2).
   2026-11-03 test (user, Supra RZ -> SZ, 4 gears, turbo removed): PI / torque / power OK in game; Excel paste REMOVED from the engine designer (the generator is enough); **manifold bug found and fixed** (`CarEngine.ConvertManifold`): every one of FM4's 337 natural engines has a stock `List_UpgradeEngineManifold` row (+ levels 2, 3), none of the 168 turbo / supercharged has one; the SZ lacked it (F 100 after buying and selling a turbo, "standard aspiration" not shown). Now a copy made natural gets the stock row + generic levels, a copy made turbo loses it; headless test `E:\DEV\fm4dev\Engine\manifold\m.exe` OK. **Checked in game by the user 2026-11-03** (old SZ deleted and made again), buy a turbo and sell it. Other ports (FH / FM3 / FM2 -> FM4) probably need the same rule: check. Open: 2b **Duplicate Car of a DLC car** ("The FM4 folder has no BMW_135iCoupe_09.zip"): rows, strings, sound and thumbnail of a DLC car live in the pack; not supported yet (message says so). Still to test: Delete Car on a copy (the Bentley FM3 log was right, in-game check not told), sound of the 118i copy.
3. **DLC packs** ✅ CLOSED 2026-11-04 (wizard in game OK; a Create DLC that needs no wizard stays open, below) (URGENT 2, 4): rebuild a pack with the current code and read the report (ReuseMakes, ShipLogos, MakeRuleIds); port a car and read the "string key ... used" line (CarPort.PortStrings); install wizard (`docs/DLC_INSTALL_WIZARD.md`) in game; multi-pack strings 7.1; Add To Package; STFS file on a console (needs a console, parked).
   2026-11-04 test (user): pack "Punto 3" (offer 124: Viper SRT10 ACR-X + Eagle Speedster) built, report read: Viper reused as make 104 (Pennzoil pack), Eagle 102 -> 106, ids moved, strings in 18 languages. Found: the Xbox 360 package went to the FM3 folder (`4D53084D`) because both windows shared `CarPort.XboxOut`: FM3 now has `CarPort.XboxOut.FM3`, FM4 windows ignore a path with 4D53084D (built, user redid it). In game: Viper under Viper OK; but WITHOUT the Pennzoil pack the Viper does not show, and with the Pennzoil installed AFTER, the pack's List_CarMake.str hid the official makes' names (order broken). Run **Validate DLC for Installation** with the Pennzoil installed: everything fine in game ("ottimo lavoro", 2026-11-04). OPEN (not coded): Create DLC should not depend on another pack: reuse a make only from the base game or an installed pack, ship strings in Benchmark.str from the start (what the wizard does later), see `docs/BENCHMARK_STRING_TABLE.md`.
4. **New makes** ✅ CLOSED 2026-11-04 (Livery editor + make tile OK in game; titles Fan / Enthusiast / Aficionado cannot be tested without long play, left unverified) (8.x): make titles Fan / Enthusiast / Aficionado in game (`docs/MAKE_TITLES.md`), Livery editor of a new make, 8.2 reduced item (pack creation only).
5. **Dash** ✅ font rule CLOSED 2026-11-04 (LaFerrari FH port in game: dash OK); editor features 12.3 - 12.9 stay experimental, not tested: font rule `DashFonts` in game (`docs/FONTS_PORTING.md`); 12.3 / 12.5 / 12.6 / 12.7 / 12.9 editor features coded and untested (E1-E4); structure edits 12.8 stay parked.
6. **Viewport tools** ✅ CLOSED 2026-11-04 (user: numpad views, live cameras, MESH, UV mapper, collision all OK; collision also checked on traffic cars and the FH2 Koenigsegg) (goal 19, D-20, D-21): MESH tab, UV mapper window, numpad views, live cameras, collision generation, shader viewport: windows never seen.
   2026-11-04 user asked for 3ds Max selection regions in the viewport MESH tab and the UV mapper: `SelectionRegion.cs` (Rectangle, Circle, Fence, Lasso, Paint; flyout button in both tool bars; Esc cancels, fence closes with double click / Enter / click on the first corner). Geometry tested headless (`E:\DEV\fm4dev\Engine\region\r.exe`, 16 checks), built, windows NOT seen yet: to check on screen.
   2026-11-04 (later) what the user meant: 3ds Max **sub-object levels** Vertex / Edge / Face / Polygon / Element (`SubObject.cs`, teal icons like Max's), in the MESH tool bar and in the UV mapper (keys 1-5 there; the old Face mode button is gone, S switches Vertex / Face). Polygon = triangles joined by an edge within 2 degrees of the same plane (a carbin has no quads), Element = joined by points (UV mapper: the UV island). Vertex / Edge keep their own selection; the faces they touch are what Delete / Detach / UV mapper use; switching to Vertex / Edge converts the face selection. Groupings tested headless (`E:\DEV\fm4dev\Engine\region\s.exe`), built, NOT seen on screen. The region flyout (rectangle...paint) stays as the way to drag.
   2026-11-04 user: sub-object levels "tutto perfetto"; then MESH turns the wireframe on and the selection is red (lines, points, and the selected faces filled red translucent: `RenderScene.OverlayFaces`). Built.
7. **Materials / viewport** ✅ CLOSED 2026-11-04 (user: reflections, body colour, node highlight, material check, FH indicators, Settings ALL all OK) (UNTESTED 2026-10-25): env reflection, material check by kind and LOD, indicators -> detail_glass_amber on a re-port, ini editor tab, body colour.

## B. Ports to FM4 (goal 3, 4, 5, 6)
8. **FH1 / FH2 -> FM4** ✅ CLOSED 2026-11-04 (user: FH1 S65 AMG with donor 500 Abarth deforms well, V12 sound very good, tyres OK; black materials are fixed by hand with the suggestions, workflow kept as it is; 3.2 gr2 study stays research): crash deformation on more cars (other FH2, cars whose donor lacks rear sections); 3.2 gr2 BE / LE study; 3.5 missing sounds between generations (classify cases, mapping table); tyre width 10.4; in-game check of the review fixes (FH2); 6.1 exterior FH materials still black (Rename Material by hand).
   8.e ✅ CLOSED 2026-11-05 (b5ab4c8): the missing language was LOC (developers' pseudo locale; FH1 / FH2 LOC Data_Car.str lacks the newer cars), `CarPort.PortStrings` falls back to the source's EN text; trial S65 AMG: 18 languages. Official packs ship 16 (no DEV / LOC). Was (2026-11-04) **Languages of a pack**: the "Punto 8" build wrote 2 tables in 17 languages, "Punto 3" 4 tables in 18. One installed layer (September Pennzoil or "Punto 4") probably lacks a language and the table built on it loses it; find which and keep all 18 (fall back to the game's table). See `PRERELEASE_TESTING.md`.
9. ⏸ POSTPONED 2026-11-05 by the user (FM2 resumes later, after the release). **FM2 / FM1 -> FM4**: 5.3 close-up of FM1 / FM2 (NB: 5.3 below was closed 2026-10-27 "the user does the rest by hand"; reopen it there if this is still wanted); 5.4 FM1 exporter stays paused.
10. ✅ CLOSED 2026-11-04 (user: solved with the Raptor tyres, tested on the Bowler). Was: **Waiting for the user's .carbin**: offroad tires, small tires, generic brake discs for "standard" cars (D-19, 3.3); until then H1 copy / placeholder.

## C. Checks and housekeeping
11. ✅ CLOSED 2026-11-05: 1.3 (+ car * 1000 + n rule), 1.4, 2.3.2 (closed 2026-10-27), 14.0 (cockpit camera in game). Was: 1.3 / 1.4 checks (database rules, SQL console) to confirm and tick; 2.3.2 own engine from scratch (open points); 14.0 camera review.
12. 🔧 **Release 0.0.1** (goal 20): done 2026-11-05: version, Release build, zips, push, 7.4 New Folder (seen by the user); 0.0.1 replaced by 0.0.2 (doc review, Issues on), tag v0.0.2, GitHub draft release; left: the user publishes it.

Parked (not in this list): FM3 windows and DLC for FM3 (`docs/FM3/fm3_conversion.md` section 9), goal 18 phase 0, FM4 -> FM3 dash in game (12.10), dash structure edits (12.6 - 12.9), generic brakes (3.3), Raptor grip (dropped), FM3 grip CM/springs, 7.2 FTP, auto LODs, 2.4 cars from scratch.

---

# 0. Release 0.0.1 (decided 2026-10-28)

Focus: finish and fix, no new topics. Done 2026-10-28: build + commit of the 26-28 work; B4 closed (re-port); MANUAL / README / INSTRUCTIONS /
THIRD-PARTY-NOTICES / credits updated (docs say what is experimental).
**Experimental / dream group** (labelled as such in the docs, not fixed for 0.0.1): Anark dash structure edits (12.6-12.9), OBJ onto wheels
(11.1 / 11.2, T-G10), FBX in Blender (11.8.2), thumbnail camera (14.0: cockpit checked, exterior previews off), new country (8.2), generic brakes (3.3 / T-G7), 9.5, 15.x.
2026-11-05: version 0.0.1 in AssemblyInfo (title, About, ini [App] Version), Release build, zips in dist\ (public: own exe + MIT/ISC libraries; user-provided files listed in INSTRUCTIONS.txt; FULL private zip for the user), pushed, tag v0.0.1, GitHub draft release. Dropped: 9.4. Then 0.0.2 the same evening (doc review, GitHub Issues on, the v0.0.1 release deleted); left: the user publishes the v0.0.2 draft.

---

# 1. Test queue (code exists, nothing left to write)

## 1a. In the editor, no game needed (do first, quick)

| # | What to look at | Coded | Tested |
|---|---|---|---|
| T-ED1 | Database view: sections **Engines, Makes, Countries, Manufacturers** (list columns, record tab, related tab, search, sort, Back, Go to / Used by from a car's engine, a make, a country, a wheel's manufacturer) | 2026-10-23 | ✅ 2026-10-27 (user) |
| T-ED2 | Logos on Makes (dark + light), Countries (flag), Manufacturers (`Car_Parts_Logos\decalNNN`; 337 of 569 have no file in FM4 = empty box is normal) | 2026-10-23 | ✅ 2026-10-27 (user) |
| T-ED3 | **Events** header picture (poster of the career type; 17 of 32 types have none, the first race's track logo is used) | 2026-10-23 | ✅ 2026-10-27 (user) |
| T-ED4 | Manufacturers > Parts tab: no more engine / drivetrain rows (those point at CarMake); wheels, tire brands, discounts listed | 2026-10-23 | ✅ 2026-10-27 (user) |
| T-ED5 | Engine designer (wizard page 4): Ctrl+V of two Excel columns (rpm, N.m) at any rpm; one column; header line; comma decimals | 2026-10-23 | ✅ 2026-10-27 (user: Excel curve of the 1750 FPT 200 hp pasted into a Brera, a "Brera 1750", all worked) |
| T-ED6 | Duplicate Car wizard: "in a new DLC pack" can be chosen; asks for Xenia's folder when Settings has none; Cancel returns to "game folder" | 2026-10-23 | ✅ 2026-10-27 (user: all tested, the Brera 1750 in a DLC pack in use) |
| T-ED7 | Driver tab in the model view (rig points, carattribs offsets) | 2026-10-22 | ✅ 2026-10-27 (user: works on cockpit.carbin) |
| T-ED8 | Duplicate Car wizard end to end on a copy (car, names, Data, engine page, summary, Create) incl. "own engine" | 2026-10-22 | ✅ 2026-10-27 (user: copy on original gamedb.slt and on a DLC, no problems) |
| T-ED9 | SQL console diagnostics list (`SqlDiagnostics.cs`) and EventRestrictions decode | 2026-10-22 | ✅ 2026-10-27 (user: all tested) |

## 1b. In game (FM4_Extracted_DEV, Xenia)

| # | What to check | Roadmap | Coded | Tested |
|---|---|---|---|---|
| T-G1 | A car installed with the new Id rule: showroom, race, upgrades; a 2-car pack | 1.1.2, 1.2.1, 5.1.1 | 2026-10-22 | ✅ ok in game 2026-10-26 |
| T-G2 | Aveo IE and X5 reinstalled with the current rule (Aveo: FM3 unicorn flag / IsArcade 0 may be the real reason it did not show) | 1.2.1 | 2026-10-22 | ✅ ok in game 2026-10-26 |
| T-G3 | Duplicate car in the game folder, and as a DLC pack | 2.2.3, 2.2.1 | 2026-10-22 | ✅ ok in game 2026-10-26 (game folder; DLC pack not named) |
| T-G4 | Own engine from the wizard: unique `Data_Engine.MediaName` (shared sound name is the thing to watch), Sim* values, upgrade levels | 2.3.2 | 2026-10-22 | ✅ 2026-10-26/27: own engines (Furai 3 rotor, RX-7 FD 2 rotor, Bora V6 sound) in game: sound, turbo whistle + BOV, rev limiter bounces, showroom = garage on kW / N.m / litres |
| T-G5 | Two packs together (string layers, key clashes) | 7.1 | 2026-10-22 | ✅ 2026-10-26: Bowler Nemesis EXR (offer 91) + Ferrari F12 (offer 92), names right in every language, both race |
| T-G6 | A pack with a new make: make page, logo in list, Livery editor with no decals | 8.1 | 2026-10-21 | 🧪 2026-10-26: names / rest OK, logo was a black square (the port assumed FM4 had the make's logos); fixed 2026-10-27 (D-13); logo re-checked ok in game 2026-10-27 (T-G13) |
| T-G7 | Generic brakes on a placeholder-caliper car | 3.3 | 2026-10-22 | – (postponed by the user 2026-10-27: placeholder brakes need his own 3D model + atlas UV edit; the problem shows on LOD0 only) |
| T-G8 | Add a song (FM4): plays in Xenia in race, menu playlists, second song / existing name, mono / 48 kHz input | 13.1 | 2026-10-21 | ✅ CLOSED 2026-10-27 (user, careful test: Blow Me Away, then Staind Outside, then the original UI_SuicideDoor in the menu rotation; a menu song also starts on return from a race; the playlist is back to the original + the two songs, plain Song lines rotate). Was: MENU song plays in game (user): in InDefaultUIPlaylist.xml it plays when it carries start="true" and initialExperience stays on ANOTHER song (a single song with both gave silence); the Add song dialog has "Menu music starts with this song" for Menu (default). Left: second song / existing name, mono / 48 kHz. Race: ✅ the song plays in a race in game (Breaking Benjamin - Blow Me Away, layer 1 playlist, heard after 19 restarts of a Maple Valley race; user). Layer = `EnvironmentLayerMapping.xml` per track, the song inside the playlist is random. Left: menu playlists, second song / existing name, mono / 48 kHz input |
| T-G9 | FM2 port with the new Id rule | 5.1.1 | 2026-10-22 | ✅ Foose IS400 + Abflug Supra ok 2026-10-26 |
| T-G10 | OBJ onto a wheel zip (FM3 wheel OBJ onto FM4 wheel zip), rebuilt model, Merge Into | 11.1, 11.2 | 2026-10 | – (postponed by the user 2026-10-27: no 3D editing now) |
| T-G11 | Material suggestions "by name" rules / shader scores | 9.1.1 | 2026-10 | ✅ ok 2026-10-27 (user) |
| T-G12 | Crash deformation tests T1-T6 on the FH Supra (see B1 below; T2 / T3 / T5 code is in `Port.ini [Damage]`) | 3.1 | 2026-10-22 | 🧪 2026-10-27 on the C7 Stingray: "benino" (user); the deformation values in `carattribs.xml` need to be raised → user tried +1 on the `damage` of every DamageScalars point on the Bowler in a pack: "fa benissimo"; CODED 2026-10-27 `CarPort.ConvertDamage` + `[Damage] ScalarBonus = 1` (only cars ported from FH1 / FH2, as the user said: FM3 ports are fine; 0 = off), built, a new FH port to check; the Bowler with +1 "ha bei danni e va bene"; log line "scalar damage +1" seen on the Veneno FH2 port; ✅ closed 2026-10-27: checked in game on the Subaru BRZ port (user: "ci siamo", "quel damage scaler va benissimo") |
| T-G13 | Bowler (or any FH1 / FH2 car of a make FM4 lacks) ported again into a new pack: the six `L_MAN_<make>_Logo.xds` are in the pack (checked in the STFS file) and the logo shows in the showroom header, the make list and the garage list; try as folder pack and as STFS file | 8.1, D-13 | 2026-10-27 | ✅ ok in game 2026-10-27 (user) |
| T-G14 | Tools > Car Performance From Game: type the garage's PI + 5 bars of the Bowler (S 601, 6.8 / 5.2 / 8.0 / 9.1 / 5.4), Save, rebuild the pack: showroom = garage | D-15 | 2026-10-27 | ✅ ok in game 2026-10-27 (user; first try failed: Write did not reach the merge.slt, fixed by calling `OnGameDbEdited`, same fix for the SQL console) |
| T-G15 | Ratings mapped at import (D-14): port one FH1 and one FH2 car, compare the showroom bars with the garage's | D-14 | 2026-10-27 | ✅ CLOSED 2026-10-27 by Tools > Car Performance From Game (post-import fix, D-15); the Raptor example is not valid, the car is already in FM4. Was: trial only (Raptor, Bowler); the Raptor is already in FM4 (IGN pack), so it is ported beside it as `FOR_SVTRaptor_12_FH1` / `_FH2` (test harness `E:\DEV\fm4dev\Ratings\pr.exe`, tag in env `TAG`); INSTALLED 2026-10-27 in FM4_Extracted_DEV: `_FH1` id 1644, `_FH2` id 1657, donor `DOD_RamSRT10_06` (the Mustang GT '05 has no zip in DEV), showroom comparison pending. Found: FH2 keeps `CameraOverrides` rows of cars it does not have, so the new car Id 1656 clashed (`UNIQUE constraint failed: CameraOverrides.CarId`): `DbIds.CarId` now skips an Id any car column of the source holds (`UsedByCarColumn`), uncommitted. Showroom 2026-10-27 (user): `_FH1` PI 265, `_FH2` PI 269, garage 314 for both; the original FM4 Raptor has LESS grip than the two ports. Compared in the DB (`E:\DEV\fm4dev\Ratings\cmpcar.exe`, pack 26 dumped with `dump.exe`): body, tyre compounds, springs, anti-sway ids identical; differ: ABS release rear (2 -> 2.5, 0.75-1 -> 2-2.25), upgrade engine (643 -29 kg vs 1020 +20 kg), stored PI (orig 0.2258, ports 0.2128 / 0.2165), class D vs E. **SUSPENDED by the user 2026-10-27**; next step was `zipdiff.exe` of the original car zip vs the port (grip files inside the zip) and the PI/garage numbers of the original |

---

# 2. To code / to do

## 2a. Next (decided or asked by the user)

| # | Task | Roadmap | State |
|---|---|---|---|
| D-1 | **Duplicate Car wizard: usability pass** (user 2026-10-23: "tutto il wizard sarà da migliorare nell'utilizzo"). Needs the user's list of what is awkward; to collect while testing T-ED6 / T-ED8 | 2.3 | ✅ 2026-10-26: Data page shows 13 usual values + "Show every value" (checked by the user) |
| D-2 | Makes: light logo falls back to the dark one when the file is missing (Chrysler, Infiniti, Jaguar, Mitsubishi) | – | ✅ CLOSED 2026-10-27 (ShowListItem: a missing picture after the first takes the first one; checked by the user) |
| D-3 | ~~Lists section with a picker (15 id lists)~~ removed 2026-10-24: the user found it useless | 18 | ✅ removed |
| D-4 | Push the commit of 2026-10-23 (when the user says so) | – | ✅ 2026-11-05 (everything pushed with tag v0.0.1) |
| D-5 | `docs/MANUAL.md`: the new database sections, engine paste, DLC-pack choice in the wizard | 16.1 | ✅ 2026-10-26 (MANUAL 9: Duplicate Car, Transmission tab; 11: FM4 car list) |
| D-6 | Engine designer: paste of the CV column / a whole rpm+N.m+CV block; status line telling how many rows were set | 2.3.2 | 💤 |
| D-7 | `docs/GAMEDB_SCHEMA.md` regenerated if the DB structure notes change (`tools/DbRulesTest/schemadoc.cs`) | 1.4.3 | 💤 |
| D-8 | **Game path opens the installed DLCs** (user 2026-10-24): once the configuration paths are complete (full game), opening the game path must find the DLCs present, MERGE their databases into the view and show the DLC cars / tracks data. All games; see 7.3 | 7.3 | ✅ CLOSED 2026-10-27 (user: tested on the main FM games with DLC, all ok; the "skipped" on DVD2 packs is normal, DVD2 is all in the first disc's gamedb.slt). Was: CODED 2026-10-24 (InstalledDlc.cs + DLC button in the database view), FH1 checked on the real files, offline run on the real folders 2026-10-27 (harness E:\DEV\fm4dev\Donor: FM2 10 packages, 6 merged, 39 DLC cars; FM3 25 / 12 merged, 108 DLC cars + 36 tracks; FM4 1 pack now (P3_TEST), 1 DLC car; FH1 21 / 15 merged, 69 DLC cars, Rally Expansion Pack zip damaged in the file; FH2 no content folder), in-app check pending (user: open each game in the Database view) |
| D-9 | **Engine designer rebuilt (user 2026-10-25, MiTo then Brera 2.4 JTDm).** Game numbers = the formula torque = curve x parts x g(StockBoost-bar) (`CarEngine.BoostGain`, 166 FM4 cars); the turbo's PowerMaxScale = the power at the torque-peak rpm (x1.0 single, x0.6 twin), upgrade levels x1.31 / 1.56 (single); curves as FM4 stores them (100 rpm grid, last value the marker -2.63, TorqueScale = peak, N and TorqueCurveMaxRPM from the highest redline + 300 rpm diesel / 500 petrol, 0 above each level's limit). The stock engine is what the dialog designs, the upgrades come from the generic rules of the traffic cars (`Upgrades.ini`, `CarUpgrades.ApplyEngine`): diesel = no supercharger, engine inertia x2. Torque scale hidden at 1. Aspiration conversions NA <-> single / twin / quad turbo (stock turbo + stock intercooler, rows moved or removed). Name of the engine follows layout / cylinders / aspiration until typed over. Generate curve (torque range + power peak + limiter, BlackBirdOsaka's power method). CONFIRMED IN GAME 2026-10-25 (Brera diesel: 155 kW, 400.1 N.m, plateau from ~1800 rpm). Open: what the aspiration menu shows for the turbo upgrade (single -> single swap seen), turbo upgrade power in game, supercharged engines (ids 5 / 6) CODED 2026-10-26 (stock + 2 upgrade rows in List_UpgradeEngineDSC / CSC from the FM4 engine with the closest boost) CONFIRMED IN GAME 2026-10-26 (8C supercharged), the graph shows the margin past the redline dotted and dim (coded) | 2.3.2 | ✅ NA <-> turbo confirmed in game 2026-10-26 (petrol + diesel); other open points listed |
| D-10 | **New gearbox in the wizard** (user 2026-10-25): page 4 of Duplicate Car, "A gearbox of its own": number of gears (1 - 8), a ratio for each, reverse, final drive (`GearboxDialog.cs`, `CarGearbox.cs`); copies the drivetrain (Data_Drivetrain + transmission, clutch, differential, driveline levels) under a DrivetrainID of its own; the stock level gets the ratios, the upgrade levels keep their shape (same gear count: scaled; else the ratios asked for); Data_Car.NumGears and the performance numbers follow. Game DB tab "Transmission" shows the levels of a car. | 2.3 | ✅ gearbox works in game 2026-10-26 |
| D-11 | **Duplicate Car as a DLC package file** (user 2026-10-25): third destination "as a DLC package file (STFS)" next to the game folder and the Xenia folder: built in a temporary folder, then the same Xbox 360 STFS file the port window makes, saved in a folder asked at Create | 2.3 | ✅ works in Xenia; console STFS test left for the future |
| D-12 | **Own engine: sound, limiter, numbers** (user 2026-10-26/27). Ratings capped at 10 before the PI (`CarPerformance.Update`; all 503 FM4 cars max exactly 10); `Data_Car.Displacement` = the stock engine's `Disp` (502/502 FM4 cars); engine tuning file of the copy (`<engine>_ET.xml`): `TurboVolume` (0 in the Furai's = no whistle; FM4's 137 turbo engines 0.2 - 0.9), `audio_rpm_redline / idle` scaled with the engine's redline / idle, BOV `blowoffL1..3Level/Choice`; torque curves of every level stay above 0 to the end of the grid (all 504 stock + 918 upgrade FM4 curves do; the old "0 above the level's redline + margin" made the rpm stop at the zero point instead of bouncing: user confirmed "rimbalza adesso" 2026-10-27); boost above 2.04 bar (FM4 maximum) asks for confirmation. Engine dialog: **Turbo volume**, **BOV sound** (L1 1-3, L2 1-3, L3 1-5 of `turbos.fev`) and play buttons Whistle / HI / MED (`TurboPreview.cs`, vgmstream decodes `turbos.fsb` to %TEMP%\ForzaStudioTurbo; checked by the user). One whistle only in the game (4 loops). Open: accel bar of a 700 kW RWD copy 10 vs the game's 8.9; whether the three blow-off slots are the turbo upgrade levels (not verified) | 2.3.2 | ✅ checked in game 2026-10-27, uncommitted |
| D-13 | **Make logos of a ported car** (T-G6, user 2026-10-26: black square). `CarPort.PortMake` assumed FM4 had the logos (true for Porsche only); for Bowler it only warned. Now the source's six `UI4/Logos/<folder>/L_MAN_<base>_Logo.xds` are copied into the target's Textures.zip (a pack carries them loose) when FM4 lacks them and the source has the same size / format (FH1 / FH2 yes: 220x123, 220x184, 882x315, 132x80 x2, 492x246, A8R8G8B8). FM3 sources: other folder names, still only a warning. Made with the "+" (own image): tested on a trial, orange test logo OK in all six | 8.1 | ✅ ok in game 2026-10-27 (T-G13) |
| D-14 | **Ratings at import** (user 2026-10-27, "calcolo corretto in fase di importazione"). The five bars of a port were the source game's, on another scale (FH2 speed / accel / launch about 0.5 - 0.75 below FM4's, rmse 1.1 / 0.8 / 0.7 on the 140 shared cars; FH1 handling / braking 1.6x FM4's). `CarPort.PortRatings`: each bar through a line fit on the cars source and FM4 share (leave-one-out error 0.35 / 0.25 / 0.18 / 0.38 / 0.24), needs 30 shared cars, else the source's. Raptor trial (FM4 truth from the December IGN Pack, 4.52 / 3.43 / 5.22 / 5.80 / 3.65): from FH2 4.6 / 3.9 / 5.2 / 5.7 / 4.0, from FH1 4.5 / 3.5 / 5.3 / 6.1 / 3.9. The PI keeps the existing mapping (rmse 0.017 on the shared cars; Raptor 0.217 for 0.226; Sim-column models and rating models were worse, 0.037 - 0.047). **Known outlier: Bowler EXR-S** (FH2-only off-roader): stored 0.464 = A 542, the garage computes S 601; the game's PI is not a function of the five bars (rmse 0.022) nor of the Sim columns (0.014) | 3.6 | 🧪 trial only, uncommitted (T-G15) |
| D-15 | **Tools > Car Performance From Game** (`PerformanceFromGameForm.cs`): type the PI and the five bars the garage shows for a car of the open database (game's or a pack's merge database), they are written into Data_Car with the class (`CarPerformance.ShownPi / StoredPi / ClassOf`: the showroom shows the stored PI through the CarClasses lines, 0.4639 -> 542 checked; 601 -> 0.5305). The exact answer for cars the estimate cannot reach (Bowler) | 3.6 | ✅ checked in game 2026-10-27 (T-G14), uncommitted |
| D-16 | **Car-specific tire models** (found 2026-10-27): beside the generic `TireA/B/C.zip` the games have `TireA/B/C<car>.zip` with a heavier tire carbin (H1 344 KB, Raptor 336 KB, Wrangler 332 KB vs 119 - 128 KB). FM4: `HUM_H1ALpha_06`, `CIT_C1_11`, `PEU_107_11`, `TOY_Aygo_11`, and `FOR_SVTRaptor_12` (TireA, TireB) in the December IGN Pack; FH1 / FH2: `FOR_SVTRaptor_12`, `JEE_Wrangler_12`; FM3 none. The Bowler has none in FH2 (generic tires). The port does not copy them: a ported Raptor / Wrangler gets the generic tire. To do: copy `TireA/B/C<car>` from the source into the pack when they exist; check whether the game looks them up by the wheel zip's name | 3.9 | ✅ CLOSED 2026-10-27 in game (user: the Raptor FH2 port shows the detailed tire). `CarPort.BuildTireZips`: the game looks MediawheelsTireA/B/C<CAR NAME>.zip up by the name of the CAR (the ported `FOR_SVTRaptor_12_FH`), not of the wheel (same name for the original); the source zips (named after its car, else its stock wheel) are converted like a wheel and laid out like FM4's generic Tire zip, the carbin entry renamed; an FM4 file of that name stays; Uninstall removes them (`tire zip:` log lines). Generic copies in FM4 (CIT_C1, PEU_107, TOY_Aygo) are plain copies of tirea; only H1 and Raptor differ (tread 5200 vs 1376 indices). Uncommitted |
| D-18 | **Donors from the DLC too** (user 2026-10-27): the donor list of the port window (and `CarPort.DonorMediaName`) only knows the cars of the target game folder (`target` DB + `GamePaths.FindCar(TargetRoot)`), so a DLC car (e.g. the December IGN Pack's Raptor, the Hummer, the Jeep) cannot be the donor of a similar car. To do: list the cars of the installed packs (Xenia content, `DbIds.PackDatabases`, `InstalledDlc.cs`) as donors, read their zips from the STFS file for brakes / templates / manifest position, merge their DB rows | 3.9 | ✅ CLOSED 2026-10-27 in game (LAM_Veneno_13 FH2 with donor LAM_LP7004_12 of the November Speed Pack: showroom, no crash, wheels and engine ok; user). Settings: the Xenia folder may be any level of the path (DlcPackage.XeniaContent). Coded (uncommitted): `CarPort.ResolveDonor` (donor not in the target folder -> the installed pack that holds it, laid out once by `DlcSource.Stage` under %TEMP%, `DonorRoot` + `donorDb`: car zip, wheel zip, damage donor, DB rows; thumbnail and zipmanifest line modelled on a game car instead), donor list of the port window keeps the cars of installed packs, marked `[DLC: pack]`. Trial FH2 Raptor with donor CHE_Chevelle396_67 (pack 26): 7.7 s, done. Safeguards (2026-10-27): FM2 source + DLC donor refused with a message and the donor list hides DLC cars for FM2; a donor that is nowhere (not in the folder, no pack) says so; a donor without carattribs.xml (MAS_GranTurismoMC_10) borrows the first FM4 car's, listed in the report; the donor engine sound tuning (_ET.xml, harmonic tunings, banks) is read from the pack when the game lacks it (`ReadTuning`, `ImportHarmonicAny`, coded, no trial reached it). Trials FH2 Raptor with donors CHE_Chevelle396_67, MAS_GranTurismoMC_10, FOR_SVTRaptor_12, VW_SciroccoS_81: done. In game: not tested |
| D-19 | **Tires option: Normal / Offroad (H1)** (user 2026-10-27): Port Car and the DLC pack window get a Tires box; Offroad copies FM4's Hummer H1 `TireA/B/C` (HUM_H1ALpha_06, base game) under the car's name (`CarPort.Tires`, `OffroadTire`, saved per pack car in CarPort.PackCars field 7), whatever the source has; Normal keeps D-16 (the source's own tire models). Trial FH2 Wrangler offroad: 3 zips (H1 carbin 344118 bytes) + manifest lines; pack builder takes them as new zips (CopyPortedFiles) - not tried. Offroad (H1 copy as it is) works in game (user 2026-10-27); scaling to the generic envelope was tried and removed on his order. TO DO (user): his own .carbin for offroad tires, small tires and generic brake discs for "standard" cars (FM2 / FH1 / FH2 traffic), to ship with the program; then they replace the H1 copy | 3.9 | 🧪 offroad ok in game, uncommitted |
| D-17 | 💤 DROPPED by the user 2026-10-27 ("forget everything about the Raptor"). **Tire numbers FH vs FM4** (analysis 2026-10-27, no code): same cars, stock compound lateral / brake / accel scale FM4 1.30 / 1.30 / 1.00, FM3 1.41 / 1.41 / 1.00, FH1 1.80 / 1.78 / 1.25, FH2 2.34 / 2.52 / 1.82 (widths, TireMod, FricScale0/1 identical); the simulated g are only +5 / +11 % and braking 14 % longer, FH1 vs FH2 Raptor have the same g with different scales: the numbers are not comparable between games. Ports use FM4's compounds (3, 6, 7, 8, 9 ids), no FH tire table is copied. FM4 has no off-road compound (9 compounds, same as FH2); H1 and Raptor use the standard ones (315/80 and 315/70 R17) | 3.6 | 📝 recorded |
| D-20 | **Viewport revolution** (user 2026-10-31). Coded 2026-10-31, built, NOT seen working (the program was open): (1) numpad 1-6 = bottom, left, right, front, back, top, orthographic (toolbar View menu + Orthographic button; drag pans, wheel zooms; works while the pointer is over the viewer or it has focus, the digit is never typed into a box; the viewer no longer steals focus from a text / number box on mouse enter); (2) camera editors LIVE: changing a spin box of the cockpit, hood, bumper or thumbnail camera moves the view at once (check box Live, on by default); (3) bumper high / low views: ESTIMATE (eye at the front of the shown model, 45% / 30% of its height, FOV 53), the game builds them in a way not decoded; (4) COLLISION tab: Generate Basic Collision (box by the fitted FM4 rule + 32 spheres in FM4 layout from the shown model) for cars like FH1 Agera. To check: hood view user said it does not work: not understood why from the code, look at it in the running program. **Dreams, later:** small mesh editor (select faces, split them off the mesh) and a small UV map editor / creator working on the UV set the user picks (FM4 has 2); use: ported brakes mapped on interiorlod0 then fixed from the carbin (T-G7). | 14, 11 | 🔧 2026-10-31 |
| D-21 | **Mesh editor + UV mapper** (user 2026-10-31). Coded, built, engine tested headless on the Supra (E:\DEV\fm4dev\MeshEdit: delete 10 faces, detach 10 faces to a new part, UV shift written and read back), the windows NOT seen: MESH tab (click / Shift / Ctrl / box select faces, All / None / Linked, Delete Faces, Detach Faces To Part, UV Mapper...), UV mapper window (channel UV1/UV2, start: keep / other channel / world top-front-side / viewport; Select, Move, Rotate, Scale, flip, rotate 90, fit, undo, live in the viewport, Apply / Cancel). Viewport: keys 1-6 (keypad and digit row) = front, back, right, left, top, bottom; left drag turns the model around its centre (Shift: look around), also in orthographic views. Limits: edits touch only the LOD of the mesh, box select ignores occlusion (front-facing only option), a vertex used by two meshes keeps one range. | 11, 14 | 🔧 2026-10-31 |

## 2b. Open work by topic (from the roadmap, details kept)

### 1. Ids, database checks and SQL ✅ (all points closed 2026-11-05)

Facts (2026-10-20/22): car Ids are one list across the games (same Id = same car in all 344 shared cases). Max Ids FM4 / FH1 / FH2:
Data_Car 1473 / 1568 / 51558, List_Wheels 11066 / 11153 / 5011150, Events 726 / 246 / 911, Races 3675 / 182 / 716,
EventParticipants 72470 / 1337 / 4754. Engine / drivetrain / wheel Ids copied from the source stay as they are.
Decisions (user): a ported car **always** gets a new Id (not the source's); rows Forza Studio generates come from a
reserved range per table; FM4 packs never collide. The 2026-10-21 "review totally, not stable" reopening was answered by the
limits table (`docs/GameIdLimits.json`, `tools/IdLimits`).

- [x] 1.1 Id allocator (`DbIds.cs`, `GameIdLimits.cs`, embedded `docs/GameIdLimits.json`; ex Now "C2+C3" and 18.1; committed 638a573).
      Rule: car Id = first free above the last FM4 DLC car (1638) and free in the game DB **and** in every installed
      Forza Studio pack's merge DB; reinstall of the same MediaName keeps its Id; other tables above the maxima of the
      limits file; formula Ids (car × 1000 + n, 100 000 × k + car) unchanged; every Id written to the port log with the
      reason. `Port.ini` `InstallFirstCar > 0` = dev/test override only. Parser tested on the real file (FM4 List_Wheels 11209).
  - [x] 1.1.2 In game: a car with Id above the new base in showroom, race, upgrades; a 2-car pack
  - [x] 1.1.3 Editor check of the port window with the new rule
- [x] 1.2 Car Id = ModelId. FM4 has `ModelId = Id` on all 503 cars; the renumbering missed it (X5 as 4001 did not show,
      ModelId still 1079). Fixed in `RenumberCar`, `CheckCar` refuses a mismatch. Scan of every column of the trial DB:
      no value left on the source Id.
  - [x] 1.2.1 Reinstall the Aveo IE and the X5 with the current rule and **test in game** (the Aveo IE also carries FM3's
        unicorn flag and IsArcade 0: source data, possibly the real reason it did not show)
- [x] 1.3 ✅ 2026-11-05 confirmed on the current build (`E:\DEV\fm4dev\DbIds\check1105`: c3test 4/4 on SLS AMG and Supra RZ; the port and Duplicate Car throw "nothing was written" on a failed check). Added the same day (user): the car * 1000 + n rule on Id / Ordinal (`DATABASE_RULES.md` rule 5), c3test 6/6, every car of the clean DB (503), the DEV DB (505) and the user's packs Punto 3 / 4 / 8 passes. Checks before every insert (ex C3). Coded: `DbIds.CheckCar` (one Data_Car row, Data_CarBody row, no key twice,
      no key built from the source Id; the port stops and writes nothing on error, errors name table and column; test
      `fm4dev\DbIds\tools\c3test.exe`, 4/4). Left: confirm the rest of the old list against the code:
  - [x] 1.3.1 `ordinal` rule documented (FM4: `Ordinal` = `Data_Car.Id`; do not assume MAX + 1): `docs/DATABASE_RULES.md`, enforced by the references rule
  - [x] 1.3.2 Writes in a transaction with rollback on error: SQL console and Duplicate Car (`SqliteDatabase.Begin / Commit / Rollback`); the port works on an in-memory DB
  - [x] 1.3.3 Diagnostic summary of the values about to be inserted: SQL console confirmation (per table), Duplicate Car plan, port log
  - Coded 2026-10-22 (committed since): `DbIntegrity.cs` (the rules in one place: keys above FM4's largest and unique, strict relations, a car's rows), used by `SqlGuard`,
    `CarDuplicate` (game folder and DLC pack, incl. the pack's merge database merged into a copy of the game DB) and `CarPort` (warnings for references and
    limits). Tested on the real FM4 DVD1 database: 0 strict dangling ids, Duplicate Car NSX '97 → game folder and → pack (offer 91) pass the rules.
  - Done when: inserts do not run if the checks fail; errors name table and field.
- [x] 1.4 ✅ 2026-11-05 confirmed (`tools/DbRulesTest/sqltest.cs` on the clean FM4 DB: duplicate key, dangling delete, renumbered car, wrong ModelId, unknown make and DROP refused; legal UPDATE / INSERT accepted and applied). SQL for every table (ex 18.5 + C1). Coded and committed: `SqlGuard.cs`, `SqlConsoleForm.cs`, `GameDatabase.RunSql`, Tools >
      Database SQL: one statement, SELECT / INSERT / UPDATE / DELETE on any table, nothing else (DROP, ALTER... refused); an
      INSERT / UPDATE runs on a copy first and is refused when it creates a key ≤ FM4's largest (message with the next free
      one); writes are listed with the DB changes, File > Save writes them. Tested on a DB copy (Data_Car Id 5 refused, 1700
      accepted, DROP / two statements refused). Left:
  - [x] 1.4.2 The full 1.3 checks on SQL writes (`SqlGuard.Plan`: keys, references, cars; also on DELETE and on UPDATE of key columns)
  - [x] 1.4.3 Schema documented (`docs/GAMEDB_SCHEMA.md`, generated by `tools/DbRulesTest/schemadoc.cs`: every table with key, columns, relations in and out, row count; the field meanings of import / export / build are not written, only the car columns and the order a car is written in): primary keys, relations (no `REFERENCES` in the DB; the 340-relation map in `GameDbSchema.cs`
        feeds Go to / Used by), indexes, meaning of the fields import / export / build use
  - [x] 1.4.4 Dependencies between tables and the right order of operations (`docs/GAMEDB_SCHEMA.md`, Order of operations: levels 0 to 6 from the strict relations; the car's order)
  - [x] 1.4.5 Editor easier: table list, statement history (Ctrl+Up / Down), run the selection, copy the result, plan shown before a write
  - [x] 1.4.6 Reusable diagnostic queries (`SqlDiagnostics.cs`, the Diagnostics list of the SQL console): all SELECT
- [x] 1.5 Decode `EventRestrictions.Value`: `docs/EVENT_RESTRICTIONS.md`, `EventRestriction.cs`, a diagnostic in the SQL console; all 340 values decode (flag bit S 0x04 and 0x01 and the class filter not fully understood)

### 2. Duplicate car, wizard, cars from scratch 🔧 (user 2026-10-22)

All with every FM4 DLC present (Ids free in the game AND in all packs). Code committed in 638a573.

- [x] 2.2 Duplicate car in the game folder (`CarDuplicate.cs`, `GameStringKeys.cs` + `docs/FM4/GameStringKeys.json`: every .str key of FM4 + all
      packs): tested on a copy (`E:\DEV\fm4dev\Duplicate`: NSX '97 → ACU_NSX_97_B, id 1638, 95 rows in 28 tables, new string keys 65534 / 65533
      in 18 languages, 36-file zip renamed, `Thumbnail_1638.xds`, manifest line, original untouched).
  - [x] 2.2.1 Copy into a **DLC pack** (`CarDuplicate.PackContent / PackOffer / PackName`: staged copy of the game, then `DlcPackage.BuildFolder`; tested on a copy of FM4 DVD1; in-game check pending)
  - [x] 2.2.2 `Combo_Engines` row of the car is copied with the next free `EngineComboID` (tested on the NSX '97)
  - [x] 2.2.3 Check in game
- [x] 2.3 Wizard (`DuplicateCarForm.cs`, Tools > Duplicate Car): car + name, names / year / make, Data_Car numbers, summary with the plan,
      Create. Rendered and the error path seen; not checked in game.
  - [x] 2.3.2 (CLOSED 2026-10-27, user: custom 5-cylinder engine for the Acura made and OK) Own engine from scratch (e.g. Bora 1.9 TDI from the V6)
      CODED 2026-10-22, compile-checked and tried on the DVD1 database, not seen in the interface nor in game: wizard page 4 "Engine" (shared with the
      source as before, or its own) opens `EngineDialog.cs`: name, SOUND (any FM4 engine's setup: the copy's Data_Engine.MediaName becomes that engine's, no file
      is written), layout, cylinders, aspiration, turbo boost, compression, mass, displacement, idle (StallRPM) and redline rpm, torque x and the shape of
      the torque curve drawn with the mouse on the power graph (`PowerGraph`). `CarEngine.cs` makes the copy under the next free EngineID (1099 on DVD1; rows
      numbered EngineID * 1000 + n, above FM4's 933000): Data_Engine, every List_UpgradeEngine* row (49 for the NSX), the torque curves (the stock one is the
      drawn shape, the upgrade levels' curves change by the same ratio point by point), displacement and redline scaling the levels, Data_Car cylinders /
      layout / aspiration and its Sim* numbers (peak torque / power and rpm, redline) recomputed from the curve, the car's List_UpgradeEngine and Combo_Engines
      rows pointed at the copy. Tested: NSX to an I4 diesel with a drawn curve, idle 900, the 360 Modena's sound, integrity rules hold. Open: Data_Engine.MediaName
      is unique across FM4's 502 engines, so a shared name is the thing to watch in game (fallback: a new _ET.xml in EngineTuning.zip, as the port does);
      StartRPM (2036-10328 on FM4's engines, not derivable from the curve) is only scaled with the redline; the Sim* rule is within 5% on 353 of 502 cars
      (supercharged ones off); turbo upgrade rows for a new aspiration are not created.
  - [x] 2.3.3 ~~Colours / textures page~~ dropped by the user 2026-10-26 (paints are copied with the car)
  - [x] 2.3.4 DLC pack target (with 2.2.1): the wizard's first page asks "in the game folder / in a new DLC pack", pack name and offer; in-game check pending
- [ ] 2.4 💤 Cars built from scratch (a full wizard, also as a DLC package). The conversions are the road to it: automatic reusable
      rules, source first, else computed (user 2026-10-21)

### 3. Car port: FH1 / FH2 → FM4 🔧

Tools > Import Car From Another Forza… (`CarPort.cs`, `CarPortForm.cs`). All 73 FH2 cars pass the trial; FH1 cars work in game.

- [x] 3.1 FH1 / FH2 ports do not deform in a crash: SOLVED in game 2026-10-25 (issue B1 below), confirmed by the user 2026-10-26
- [ ] 3.2 Big-endian vs little-endian gr2: find the differences before writing a converter
      2026-10-22 first pass in `docs/GR2_FH_vs_FM4.md` (`tools/GrannyCompare`): only little-endian gr2 are on this machine (FM4 Supra, 918, an FH demo
      car), none from FH2's big-endian console files; FM4 files are uncompressed with tag 0x38, the FH one Oodle1-compressed with tag 0x33; FM4 holds its
      sound events inside the animation. Needs one FH2 car zip with animations + the same car from FM4 (e.g. MP4-12C). Try in game: FH2's BE files copied as they are.
- [ ] 3.3 Generic brakes: a missing / placeholder caliper + rotor becomes the generic `Media\brakes` model (`CarPort.GenericBrake`; committed
      a06a7a7: rotor types slotted / carbon / plain / drilled, the plain and drilled ones exist only as LOD models so their vertices go to LOD0;
      the generic file's own material and UV transform are kept, car textures untouched: the white discs on traffic cars are gone). Check in game
- [x] 3.4 ✅ CLOSED 2026-10-31 (user: in-game test passed; the port copies a bank held only by an installed package into the car, so it does not depend on the package). 2026-10-31: the DVD2 is the three "Model Year Car Pack" STFS files in Xenia's content folder (47 banks inside). CODED (`SoundBankIndex.cs`, used by `CarPort.ImportHarmonic`, `EnginePreview.Diagnose`, Change Car Sound and the engine dialog of Duplicate Car): a bank only in an installed package is no longer "missing", the tools warn it works only while the package is installed. Checked offline on the 502 DEV engines: 39 need a package bank, 0 are really missing. Old note (user 2026-10-20: files come only from the folder opened as source). Our folder is DVD1 only (353 banks in
      `soundbanks\lod1`); swap engines whose sound is on DVD2 crash the race with an I/O error (BMW M3 GTR V8 `8N1_E92M3_Exh.fsb`;
      M5 / M6 V10 are fine). When DVD2 is found: read it as a second FM4 source; until then maybe hide or flag swap engines with a missing
      bank (ET → HT → `<Soundbank>` check, as `CarPort.ImportHarmonic`).
- [x] 3.5 (CLOSED 2026-10-27, tests passed by the user) Missing sounds across generations (ex C6). One case classified and fixed (FM3 engine tuning format); banks: source game first,
      FM4 car picked in the review for what is missing, `NA.fsb` = no sound. Left:
  - [x] 3.5.1 Classify more real cases (missing reference / asset / format / audio system). CENSUS 2026-10-27 (harness `E:\DEV\fm4dev\Sound35`: cen.exe, cend2.exe;
        every car of every source ported to a scratch folder, donor LAM_LP6704SV_10, sound lines of the report grouped; results in fh1/fh2/fm2/fm3/dlc_*.txt there).
        Sources: FH2 216, FH1 176, FM3 311 with a model, FM2 234 with a model, DLC from the Xenia content folder (FH1 84 cars, FM3 146, FM2 4); every car ported "ok",
        0 times "sound bank in neither game". Cases:
        - **A. the source has no engine tuning at all** -> the donor's tuning, REVIEW line to pick the FM4 car: FM2 234 cars (FM2 has no tuning format, by design),
          FH2 4 (Fiesta PLAYER, Transit, BoxTruck, Bus), FH1 1 (VW Corrado), FM3 2 (Focus ST / SVT).
        - **B. FM3 tuning without FM4's DSP blocks** (old format): completed from the FM4 car with the same banks: FM3 275 cars + DLC 78 (all FM3 cars; FH1 / FH2 are fine).
        - **C. a same-name bank differs between FM3 and FM4**: FM4's kept (warning): FM3 270 cars + DLC 37. Not an error, but the sound may differ from FM3's.
        - **D. the tuning names a harmonic tuning (`*_HT.xml`) that the source game lacks**: FH1 2 + DLC 9, FM3 1 + DLC 10 (e.g. Huayra, Zagato TZ3, Apollo S, CCX,
          MP4-12C, Cobra Daytona): the donor's sound for that car, REVIEW line.
        - Not sound: FM3 104 + FM2 76 database rows without a car zip (cannot be ported); DLC packages without cars (rims, tracks, textures, incomplete files).
        DECIDED by the user 2026-10-27: no suggestions. The review only says the sound is missing and that another car's is used (cases A, B, D: text
        "<game> has no <X>: the engine sound of another car (<name>) is used", nothing preselected; "Leave as is" keeps what the port used). Coded in `CarPort.cs`
        (the two `sound` issues), checked by the user (FH2 Transit, FH1 Huayra, FM3 X5).
        3.5.2 (mapping table, which car to suggest) is therefore dropped.
  - [x] 3.5.2 (dropped: no suggestions) Mapping table between the generations; written procedure to find invalid references
  - [ ] 3.5.3 💤 Engine sound listening tool: same-name banks of source and FM4 played at chosen RPM / throttle, then pick one (needs the
        .fsb / .xma decoder, RPM-to-sample mapping, crossfade)
- Tyre width of ported cars: see 10.4.
- [x] 3.9 ✅ CLOSED 2026-10-27 in game (D-16, D-19; tire zips by the car's name) Car-specific tire models (`TireA/B/C<car>.zip`, FH1 / FH2 Raptor and Wrangler; FM4 has them for the H1 and the Raptor of the December IGN Pack): the port should copy them (D-16)

### 5. FM2 / FM1 → FM4 🔧

2026-10-21 product decision: an FM2 game folder is a normal port source. FM2 reuses the FM1 mesh; FM1 is a little-endian chunk tree,
FM2 big-endian in the FM4 family (docs + tools `fm4dev\Fm1_Fm2`, `fm4dev\Fm1_CAB`).

- [x] 5.1 FM2 as port source (`Fm2Source.cs` stages the folder, port window lists 310 FM2 cars, thumbnail ported; Veilside = DEV id 4008;
      committed 638a573)
  - [x] 5.1.1 In-game check of an FM2 port with the new Id rule (1.1): Foose IS400 and Abflug Supra tested by the user 2026-10-26, OK
- [x] 5.2 FM2 car data: WIRED (CarPort.Fm2Data runs `Fm2CarData`, `Fm2CarUpgrades` and `Fm2CarPowertrain` on the FM2 stage; the TODO line saying "not wired in yet" was stale, found 2026-10-26). Still the donor's: physics columns (aero, steering, Sim*, ratings), CarPartPositions; in-game check = 5.1.1
- [x] 5.3 (CLOSED 2026-10-27: interior is covered by 6.2; the user does the rest by hand) Same close-up rebuild as the FH traffic cars for FM1 / FM2 cars (ex 14.1 last item, ex C4): only after FH traffic works (6.1);
      a LOD 1 → LOD 0 copy is the first build test
  - Done when: one reproducible test with log and identified source assets, documented whether LOD1 → LOD0 is enough or what is missing,
    cockpit camera off checked, not assumed. Keep results apart by game generation and asset type.
- [ ] 5.4 ⏸ FM1 `.cab` / `.ca2` mesh exporter (`fm4dev\Fm1_CAB`, f4852a6, CAB 243/243, carbin 503/503): paused, order is traffic cars, FM2, then FM1
- [ ] 5.5 💤 FM2 model writer (superseded as a product by 5.1; only if a case needs it)

### 6. FH1 / FH2 traffic cars 🔧

Ported traffic cars load and run (see D, topic 6). What stays open:

- [ ] 6.1 Exterior FH material names still black, fixed by hand (Rename Material): parked under 9 (shader research, an FM4 "mini engine" in the viewer)
- [x] 6.2 (CLOSED 2026-10-27, user: done for FM2 and FH1/FH2 traffic; editor for driver position, hands and camera) Driver position (ex C5): where the seated body position is stored (known: rig in `_cockpit.carbin` head 0x1C8-0x2FF,
      `DriverHandsOffset`, cockpit camera = `CarPartPositions` part 9 + `CameraOverrides`). Compare a working car with a known-correct one;
      position, rotation and coordinate system separately; a controlled change checked in game; document the field. Done when the position
      maps to a specific field with a reproducible test.
      2026-10-22 first pass in `docs/DRIVER_POSITION.md`: the FH Supra cockpit carries the same rig block as FM4 (312 / 312 bytes); the wheel pivot and
      the shifter point match the source scene's locators (2 mm); NEW **DRIVER tab** in the model view (`ArchiveEditorForm.Driver.cs`, like EXHAUST) to
      edit the rig points and the carattribs offsets by hand; compile-checked, not seen in the interface nor in game. A `driver.txt` for the port like
      `exhaust.txt`: not written.

### 7. DLC packages 🔧

Credits: **[Aiden-Piercee](https://github.com/Aiden-Piercee)** (`fm4dlc` toolchain) and **XTD (@xtd_ac)** (Xenia folder DLC, XLAST package,
console tests). Docs: `docs/DLC_PACKAGES.md`. Material: `fm4dev\DEV_DLC`.

- [ ] 7.1 Several Forza Studio packs at once. CODED 2026-10-22: a pack's string tables = the game's + EVERY installed FM4 pack layer (STFS or
      folder, `DlcPackage.StringLayer`) + its own strings; priority above the highest layer; key clashes with an installed pack reported as
      WARNING (merged EN Data_Car 1006 → 1402 keys). In-game check pending. Follows the string audit rules in D, 2.1.
- [ ] 7.3 (2026-10-27: to be reviewed, another chat is working on it - the %temp% one; do not touch from here) Read the installed DLCs of the opened game (user 2026-10-24, all games: FM2/FM3/FM4/FH1/FH2). Starts when the configuration paths
      are complete. Needs: (a) find DLC content for each game (folder DLC, STFS packages, `DlcSource.cs` already reads FH1/FM3 STFS);
      (b) merge each DLC gamedb into the in-memory view (cars, tracks; ids from the DLC kept, clashes reported, game files never written);
      (c) show DLC cars / tracks in the car list and database views with a "DLC" mark and the pack name; (d) check per game what a DLC
      holds (FM2 / FH2 may have none, see topic 7 notes).
      CODED 2026-10-24: the DLC packages are STFS files in Xenia's content folder (`CarPort.Xenia\<title id>\00000002`), not inside the game
      folder. Opening the game's own gamedb.slt scans them in the background (extracted once to %TEMP%), merges every `*_merge.slt` in offer
      order into the in-memory view (key clash: first row kept, reported in the button tooltip), adds a DLC column to Cars and Tracks, and
      overlays the packages' .str names. The view is READ-ONLY (GameDatabase.ReadOnly); the toolbar button DLC turns it off to edit the game's
      own database. FH1: 21 packages, 15 merged, 69 DLC cars; "Rally Expansion Pack" zip is damaged in the file itself. Open: FM2 packs without
      a merge database (cars as loose folders, rows come from `_execute.sql`) are listed but not merged; FH2 has no content here.
- [x] 7.4 (seen working by the user, 2026-11-05) New Folder in the archive tree (user 2026-10-27): Archive > New Folder... and right-click "New Folder Here..." on a zip, a folder or a DLC package. In a folder / package work folder the folder is made on disk (and shows even when empty); in a zip it only shows in the tree until a file is added (a zip stores no empty folder). CODED, compile-checked on a clean copy, NOT built in binDebug (the build is broken by another chat's ForzaZip.cs) and not seen in the interface: to check once the build is back.
- [ ] 7.2 ⏸ Transfer to the console: FTP (Aurora / FSD) or Xbox 360 Neighborhood (the user's side; no console now)

### 8. New car makes 🔧

A make FM4 lacks (or a brand-new one), local or in a DLC pack. Code: `CarMake.cs`, `NewMakeForm.cs`, `CarPortForm.Makes.cs`.

- [x] 8.1 ✅ CLOSED 2026-10-27 in game (user: Bowler imported, its make logo shows in the DLC) In-game check (local and pack): the make's page, logo in the list, Livery editor with no decals (2026-10-26: name and page OK, logo black square -> D-13 fix, T-G13 to check)
- [ ] 8.2 REDUCED by the user 2026-10-27: the makes in the game DB are for dev builds, everything must come from DLC packs, so decals, `_wtbg` logo look, edit / remove a make and per-language names are dropped.      Only this is kept: in the DLC creation wizard (new make page), if the make's country is not in `List_Country`, ask for the flag image, the country name (and region) and add the country to the pack.

CODED 2026-10-27 (`CarMake.cs` CarCountrySpec / FreeCountryId / AddCountryName / BuildFlag, `NewMakeForm.cs` "New country..." item + small dialog: name, flag name, region, flag image):      List_Country row (next free Id past the packs), name in List_Country.str of every language (IDS_Country_<n>), flag L_FLA_<name>_flag.xds in UI4/Logos/Country_Flags      (same size and format as FM4's flags, image stretched). Tried with a test make in a trial folder (row, 18 languages, flag 132x80 OK); the pack build carries it      as a generic diff (not run). Left: in-game check of a pack with a new make + country; uninstall does not remove a country; two pending makes with two new countries.
### 9. Materials and game shaders ✅ (finished 2026-10-26: FM4-shader search in the editor + magenta marking of errors work well, user)

Paused (user 2026-10-19): the shader similarity analysis is looked at again with a separate WIP materials project (simple decode of the
game shaders, in-game-like preview; notes `docs/internal_traffic_conversion.md`).

- [x] 9.1 Material suggestions (`MaterialSuggestions.cs`, committed 039aa06): left
  - [x] 9.1.1 Check in the editor and in game ("by name" rules and shader scores untested; `lights_amber`, the port table's target for
        indicators, is not in FM4's in-race shader library)
  - [x] 9.1.2 Instructions and documentation (`docs/MANUAL.md` sections 5 and 11)
- [x] 9.2 Document the game materials: the shader format and the similarity score (car shaders in `Media\Shaders\Cars\*.fxobj`)
- [x] 9.3 Imitate the FM shaders in the viewer from their parameters (carbin viewer with real materials)
- [x] 9.4 OBSOLETE (user 2026-10-28): the manual editor (Rename Material + suggestions) is reliable and fast, no automatic mapping needed
- [ ] 9.5 💤 New renderer in place of XNA: stays a dream (user 2026-10-28); first understand why XNA is needed at all

### 10. Wheels: morph targets and resizing ✅ (closed 2026-10-31)

Background C3 (research). Paused by the user 2026-10-19.

- [x] 10.1 (solved: the HKS Genesis had it, fixed; confirmed by the user 2026-10-26) Morph table on OBJ import onto a wheel: regenerate the table instead of keeping a stale one (stale = exploded wheel in game).
      To resume by comparing how Forza Studio and Aiden's script (`fm4dev\Aiden-Piercee\carbin_check.py`, read only) process the carbin binaries.
  - [x] 10.1.1 ✅ CLOSED 2026-10-31 (user: FM2 port ok; FM1 will probably never get a port, so nothing left). Same check for every older source game: FM3 done; FM2 and FM1 when their ports come (5.1, 5.4). FH1 / FH2 keep their own
        tables: only if a case shows up
  - [x] 10.1.2 ✅ CLOSED 2026-10-31 (user: traffic cars and FM2 ok). Traffic cars' LOD0 wheel rebuilt from LOD 1 copies the table from the nearest vertex: not a problem so far; watch if a traffic
        rim looks small up close
- [x] 10.4 (CLOSED 2026-10-27, checked by the user) Tyre width on ported cars whose wheel mesh is wider than usual (ex topic 1 + 11.6): check in game whether D 10.5 / 10.6 already fixed it;
      maybe tied to 10.1 and to 3.1

### 11. Model editing and automatic LODs ✅ (closed 2026-10-31 by the user; 11.1-11.4 closed with it, 11.5 automatic LODs and 11.6 gr2 editor not done, left as ideas)

- [x] 11.1 ✅ CLOSED 2026-10-31 OBJ import into a wheel zip (@delete.diskord): fixed in code (60f7d57, `CarbinModel.RebuildLods`: cars keep LOD 0 in `_lod0`, wheels keep
      LODs 0-5 in one file); in-game test pending (FM3 wheel OBJ onto an FM4 wheel zip)
- [x] 11.2 ✅ CLOSED 2026-10-31 In-game test of a whole model rebuilt from OBJ, of edited parts and of Merge Into
- [x] 11.3 ✅  Effect of the cleared glass A/B arrays on rebuilt parts
- [x] 11.4 ✅  One export / import folder layout for every asset type (ex C7): `<export_folder>/car.carbin/LOD0/body/<object_n>` (the original note ends there)
- [ ] 11.5 💤 Automatic LODs: decimation (QEM) from lod0 to lod1-lod5 at the measured ratios (23.7 / 8.8 / 6.7 / 3.4 / 2.5 %)
  - [ ] 💤 11.5.1 Keep UV seams, normals, materials and damage data
  - [ ] 💤 11.5.2 Hook into the OBJ work folder import
- [ ] 11.6 💤 .gr2 animation editor
- [x] 11.8 ✅ CLOSED 2026-10-31 (user: export + reimport on the Bentley Continental GT, no errors; Blender, 3ds Max and in game checked, see 11.8.1-3). Model as FBX with hierarchy (Model > Export Model to FBX / Import Model from FBX; `FbxFile.cs`, `FbxScene.cs`, `CarModelFbx.cs`): scene `car > main|lod0|cockpit > LODn > part > part__material`. Import bakes the transforms of the ancestors (moving a group works), compares each part LOD with the model (unchanged parts are not rebuilt), a LOD without a node is left alone, a part missing from a LOD the file has is dropped there, a new group / mesh is a new part; then the same plan and Apply as the OBJ folder. Tested (Supra RZ, fm4dev\EsportazioneCompleta\tools\fbxrt): export 19 MB, 318 part LODs unchanged on reread, one moved group = 1 change, shift 0.1000 m. NOT tested: opening the file in 3ds Max / Blender, Z-up files, ASCII FBX (not read), the dialogs, in game
  - [x] 11.8.1 3ds Max 2027: export, edit one part, export FBX, reimport in Forza Studio: OK (user, 2026-10-25). Needed: header/footer consistent (footer id depends on the creation time: fixed 1970-01-01 10:00:00:000 + Blender's footer), SDK-like templates / properties / take. Export dialog = choose model files + LODs; import = "remove parts the FBX does not have" off by default
  - [x] 11.8.3 In game check of a model imported from FBX: OK (user, 2026-10-25)
  - [x] 11.8.2 ✅ CLOSED 2026-10-31 (user: FBX opened in Blender OK; ASCII FBX reader dropped, not needed; more cars than the Supra done)
- [x] 11.9 ✅ CLOSED 2026-11-06 (user: ACU_NSX_97 lod0 edited in ZModeler through glTF, works in game). glTF 2.0 export / import (user 2026-11-06: open, documented format). Model > Export Model to glTF / Import Model from glTF; `GltfFile.cs` (MiniJson, GltfScene reader, GltfBuilder writer, no external library), .glb (one file) or .gltf + .bin. Same carbin hierarchy as the FBX (`car > main|lod0|cockpit > LODn > part > part__material`, same export code through `IModelSceneBuilder`); the import reads both formats as `ModelSceneNode`s and uses the same check + OBJ folder import. Reads node matrix or TRS, triangle lists / strips / fans, any accessor type, sparse accessors, data: URIs; UV v flipped (glTF v down). Tested (Supra RZ, `E:\DEV\fm4dev\Gltf\gltfrt`): .glb and .gltf 16.7 MB, 318 part LODs unchanged on reread, hierarchy = FBX (1508 nodes, 0 different), one moved node = 1 change, shift 0.1000 m, rebuilt model re-parsed. User 2026-11-06: NSX lod0 export, ZModeler (new glTF filter), import back: OK on screen (dialogs included).
- [ ] 11.10 Textures in the FBX / glTF export (user 2026-11-06: to see the UV map in the 3D editor). Each material gets the picture the viewer shows (texture picked in the mesh panel, else the game's colour texture via ShaderTextures, car zip then Shared.zip), written opaque as PNG: inside the .glb, in a `textures` folder next to a .gltf / .fbx (FBX Texture + Video objects on DiffuseColor, as Blender writes them). The import ignores them. Tested (Supra RZ, `E:\DEV\fm4dev\Gltf\gltftex`): 69 of 79 materials textured, 13 pictures, all decode, reimport 318 part LODs unchanged in .glb / .gltf / .fbx. NOT tested: ZModeler / 3ds Max showing them, the menu export

### 12. Digital dash (Anark) editor 🔧

- [ ] 12.10 🧪 Dash in the FM4 → FM3 port (2026-11-03): `CarPortToFm3.PortDash` + `Fm3Dash` + `DashFonts.ToFm3` (GaugeID no longer the donor's; own dash into `Media\DigitalGauges`, images into `UI\Textures.zip`); 234/234 FM4 dashes convert and compose, 30 use FM4-only properties (left out unless `AllowFm4OnlyDash`). **In game pending**: `AST_One77_10` (plain) and `LEX_LFA_10` (FM4-only) are in `FM3_Extracted_DEV` (donor Supra). Details in `docs/PORT_FM4_TO_FM3.md` §4
- [x] 12.11 ✅ Event table DECODED (2026-11-05, offline, no game): grammar group(owner, n) / event(hash, n) / action(target, second element, command, arg1, arg2); commands COMMAND_GOTOSLIDENAME 1195C23B, SETPROPERTY 7E4906FD, FIREEVENT 06875030, PLAY 573BA2B8, GOTOTIME 5B2FA69C; block 1 "slots" = string indexes of the events each behaviour declares; contract behaviour hash = H(<name>_CONTRACT) (20B25330 PROGRESS_BAR, 00CE884D COCKPIT_LIGHT). Settled on the 141 Anark scenes of FM4 Media/UI.zip + 233 FM4 dashes, 374/374 (tools/AnarkTail/anark_tail.py, docs/ANARK_DASH.md). Next: move it into AnarkBgf / DashReport, then 12.8
- [ ] 12.1 Slides (pages shown one at a time)
- [ ] 12.2 Behaviour parameter names
- [ ] 12.3 🔧 Property inspector (PropertyGrid: lists, check boxes, numbers, text, colour) + drag on the preview (move, 8 resize handles, arrows); coded 2026-10-23, not tested. Next: text size by handle, undo, snapping, material properties
- [ ] 12.5 🔧 Multi-selection: Ctrl+click, rubber band on the background, Ctrl+A; move together, arrows, shared properties (position, scale, opacity, color, alignment), Align menu (6 aligns, spread H/V); coded 2026-10-23, not tested. Missing: group scale, tree multi-select
- [ ] 12.6 🧪 Duplicate (toolbar or Ctrl+D), reworked 2026-10-26 after the game CRASHED on a duplicated gear text (first version, in game 2026-10-26). Findings: all 431 dashes list their elements depth first, and the .bgf tail (5 blocks: behaviours, animation tracks, events, one empty, SLIDES) counts elements by index; a slide lists, for the scene and for each gauge, every element it owns (itself, its elements down to the next component, the child components), 3640 of 3684 slides match that rule; the old copy was missing from those lists and sat outside the depth-first order. Now: the copy is appended as a child of the dash's backdrop group (else the layer), is listed in every slide of the scene, and the .bsg material/image table (header 0x28) gets the new materials and images; header 0x5C/0x60/0x64/0x68 = slides / entries / A items / track refs updated. Checked without the game on 1529 copies of FM4, FH2 and FH1 dashes (reread, header counts, depth first, slides, ids, bsg order and parents, copy stays in place) and removal gives back the three files byte for byte. The copy of a gear/speed text is NOT fed by the game (it binds its own gauges). **In game pending: is the crash gone?** (the cause is a hypothesis: slides + depth-first order; other hypotheses: none left that the data shows) **2026-11-05 (offline)**: the copy now goes RIGHT AFTER the original under the same parent (real insertion, tail renumbered, 12.8), gets the values the slides give the original and its animation tracks; the hash of a renamed copy was wrong (top 5 bits of the original + 27 bits of the new name, matching no name): now the full hash of its name when the original follows that rule (80% of the elements). New crash candidates fixed: wrong hash, missing slide values. 7365 copies on 210 FM4 dashes checked, copy + remove = original byte for byte. **In game: does a copy load (one at the end of the dash and one in the middle)?**
- [ ] 12.7 🧪 Delete (Del or toolbar): an added element (or any last element the animations / behaviours do not mention) is removed from the .fbf, .bgf (+ slides, strings) and .bsg; any other element is offered "hide" (opacity 0), because the game numbers elements by index and the behaviour / animation / event blocks (hdr lists slot numbers, events not decoded) would need renumbering. Add > Text / Rectangle / Image (copy of the first one the dash has, fonts and textures from what the dash and the car zip already use). Not tested in game. **2026-11-05 (offline)**: Delete removes ANY element (with its behaviours, tracks, events, slides; tail renumbered, 12.8); fixed on the way: behaviours (id 0) no longer remove every id-0 record of the .fbf / .bsg, a last string is dropped only when nothing else uses it. 15399 removals on 210 FM4 dashes keep every rule. **In game: delete a gauge (e.g. a warning light) and a text in the middle.**
- [ ] 12.8 🧪 CODED 2026-11-05: `AnarkBgf` models blocks 1-3 + slide references (374/374 byte for byte, headers recomputed identical), `InsertElements` / `RemoveElements` / `CopyTracks`; harness `tools/AnarkTail/DashTest.cs` + `RoundTrip.cs`; in game pending (see 12.6, 12.7). Was: Real insertion in the middle and real delete of any element: needs the behaviour, animation track and event blocks of the .bgf tail decoded (index fields vs slot numbers vs hashes); tracks = (u32 element, u32 hash, u32 type|keys, 6 words per key) done, events and the behaviour n-lists not
- [ ] 12.9 🧪 Add > Texts from another car's dash... (AnarkDash.Import + BindImported, 2026-10-27): takes a group or text (with its texts) from the dash of another car zip, e.g. Corvette C5R `TEXT` (tach, speed, gear, lap time) into the Brera; pictures under it (SPEED_LABEL) are left out; names kept (+ _2 when taken); the channels the source fed are fed to the copies unless this dash already uses that value (Brera keeps its gear gauge). Offline: C5R TEXT into all 234 FM4 dashes, 0 bad (E:\DEV\fm4dev\anark\dashcrash\imp.exe). **In game: not tested**; the gauge crash (12.6) is still not explained

### 13. Music ✅ (no open point)

- [x] 13.1 ✅ CLOSED 2026-10-27 in game (user: race + menu rotation, two songs; see T-G8) Add a song (FM4), coded 2026-10-21, **plays in a race in game 2026-10-27** (menu playlists, second song, mono / 48 kHz still to try): ♪ > "Add song (FM4)..." (`MusicInstaller.cs`, `ArchiveEditorForm.SongAdd.cs`;
      ideas from the Fm4 Sound SDK). Any audio → vgmstream WAV → `xma2encode` (Xbox 360 SDK, copied to `bin\xma2encode\`, NOT redistributed) →
      `Media\audio\Music\<name>.xma` + `<Song>` line (playlist kept once as `.orig`). Tried: Digitalism - Pogo in FM4_Extracted_DEV (race layer 1);
      the user says Forza Studio parses the new .xma like a game file. To test: plays in Xenia in race; menu playlists; the dialog; a second song /
      existing name; mono or 48 kHz input; many songs; loading without the SDK (path prompt). Release note: the user needs the SDK for this feature.

### 14. Cameras ✅ (14.0 closed 2026-11-05: cockpit camera; exterior cameras dropped by the user)

- [x] 14.0 ✅ CLOSED 2026-11-05 on the cockpit camera (user): NSX '97 eye offset Y 0.18 -> 0.285 + Wheel From Cockpit Model, saved from the CAMERA tab: in game +10 cm like the preview; values put back from the backup. Untouched fields keep their full value now (`KeepUntouched`, exterior fields too). History: Supra RZ hood height 0.478 -> 0.778 from the CAMERA tab moved the view in game, BUT the hood / bumper previews were wrong (NSX '97: eye inside the car). The game adds the CameraOverrides offsets to a base point of its own: not in the DB, CameraSettings.ini, carbin header, maxdata.xml, carattribs.xml; the DB hood eye is always 0.52 - 0.61 m under the body's top (12 cars). User: drop the exterior cameras, cockpit only. Exterior previews switched off (`ExteriorPreviewDecoded`), offsets stay editable as numbers; the cockpit view opens `<car>_cockpit.carbin` by itself (`SwitchCameraModel`); unsaved camera edits survive a model switch. Thumbnail override in game left for after the release. Review done 2026-11-05: preview + edit are coded and committed (a7d09a2): cockpit (`ArchiveEditorForm.Camera.cs`, checked in game), hood / bumper / chase numbers of `CameraOverrides` (`ExteriorCamera.cs`: hood previewed, bumper preview ASSUMED, chase no preview), thumbnail camera preview + per-car override (`ThumbCamera.cs`, `ThumbnailCamera.cs`: the game reading the file is inferred, not checked); live cameras seen by the user 2026-11-04. Left: in-game checks of hood edit, bumper assumption, thumbnail override. Was: REVIEW the camera work (user 2026-10-28): a **preview** of the exterior and interior cameras and of the thumbnail camera in the viewer, and then **create / edit** them (hood, bumper, chase, cockpit, `ThumbnailCamSettings.xml`). Start by re-reading what exists (cockpit camera panel, CameraOverrides, 14.1 notes below) and decide the first step with the user
- [ ] 14.1 💤 Exterior cameras (hood, bumper, chase, thumbnails). Known: per car only `CameraOverrides` in the DB (hood height / Z on 501 of 503
      cars, chase / bumper offsets 0 on all but one); global plain text in `Media\Camera` (`CameraSettings.ini`, `CameraPhysics.xml`,
      `CameraPhysicsSansEffects.xml`); replay / menu cameras `CarRelativeCams.xml` (138), `321Cams.xml`, `carrevealcams.xml`, `HomeSpaceCams.xml`,
      `ColorSelectCams.xml`, per track `preracecams.xml` / `tracksegcams.xml`; thumbnails `Media\ThumbnailCamSettings.xml` (2 defaults + 24 per-car).
      A hood camera panel was written and dropped.

### 15. Platform and UI ⏸

Both points wait for a proper release (user 2026-10-28), not before.

- [ ] 15.1 Conversion tool as MDI (child windows inside the main window)
- [ ] 15.2 Recent .NET Framework: since 2026-11-01 (bbc56ae) the project targets **.NET Framework 4.8**, x86, with XNA 3.1 (loaded through `useLegacyV2RuntimeActivationPolicy` in app.config); check SQLite, HexBox. Last, it touches everything

### 16. Documentation and community 🔧

- [ ] 16.1 Format documentation in `docs/` (carbin, xds, Anark, gamedb, DLC, shaders...)
- [ ] 16.2 Discord feedback: reports become points in the right topic (still few users)

### 17. Side projects (own tools, not Forza Studio features) ⏸

Each has its own folder and notes file.

- [ ] 17.1 PGR4 (`C:\Users\Dan\Desktop\PGR4DEV`, `FORMATO_PGR4.md`): textures + geometry solved 123/123 (2026-10-20); next materials → textures, scale
- [ ] 17.2 NFS ProStreet tracks (`nfs_track_dev`, `NOTES.md`): chunk format verified, solid list decoding next
- [ ] 17.3 FM4 tracks study (`fm4dev\Tracks`): MapleValley smallest circuit; rmb.bin pieces 981/981; next placement + OBJ export
- [ ] 17.4 GT5 / GT6 (`gt_dev`): decoder / viewer largely closed; car data JSON export committed (ea8ccad); Forza Studio import left

---

# 3. Waiting for something outside the code

- 7.2 console FTP (the user sold the Xbox 360; B3 is on hold with it)
- 3.4 FM4 DVD2 (only DVD1 found)
- 9.x materials: separate WIP project (shader decode, in-game-like preview)

---

# 4. Current issues (B)

Problems seen in game, cause unknown or check pending. When fixed they move to their topic.

### B1. FH1 / FH2 ports do not deform in a crash (topic 3.1)

- **CLOSED 2026-10-27 (user: "B1 OK")**: with the damage scaler +1 (T-G12) a new port (Subaru BRZ) deforms well in game. T1-T6 not needed.
- **SOLVED in game 2026-10-25** (user: "adesso si deformano"; MP4-12C `_FH` copy beside FM4's own, P1 with the MP4 as donor). Two causes, two fixes, both now default:
  1. the per-vertex damage words: 170 053 vertex pairs FH1 / FM4 of 6 cars (`E:\DEV\fm4dev\Rename\dmgpairs.cs`) show NO fixed axis / sign / bit-layout rule
     (best 35 %), so no formula. `Port.ini [Damage] Words = donor` (default): the words of the same car's FM4 file (nearest vertex, same section), else of
     the chosen donor car (place in the section box, `CarbinModel.BorrowDamageWords`, `CarPort.DamageReference`); `donorcar` forces the donor car. Sections the
     donor lacks keep the FH words (listed in the report). Same idea as the Veilside OBJ import (nearest-vertex words, perfect damage).
  2. FH keeps the LOD 0 of the headlights in `_cockpit.carbin`; FM4 has them in `_lod0.carbin`, the file that deforms when the car is alone. The port moves every
     part the donor's `_lod0` has, the car's `_lod0` lacks and the car's cockpit has (headlightL / R) into the `_lod0` (`CarPort.MoveSectionsToLod0`, `Port.ini
     [Lod0] MoveSections = off` to disable). Checked on the MP4-12C FH1 only.
  carattribs.xml damage values were already FM4's standard (checked on the Countach: identical Damage block).
  Left: other cars (FH2, FM3 sources, cars without the sections in the donor), a light-weight check of the draw order of the moved sections.

- Seen 2026-10-17 in DEV: FH1 / FH2 ports keep their shape after a crash; FM3 ports deform like FM4. Deformable flag OK on the FH body parts.
  User's guess: maybe the morph data (10.1). To note: which cars, ported before or after 80f5104 (FM4 damage values in `carattribs.xml` for soft source cars).
- Per-vertex damage words, TOY_SupraRZ_98 in four games (`fm4dev\Deform\dmgcmp.exe`, outputs `fm3.txt`, `fh1.txt`, `fh2.txt`, `pairs_fh2.tsv`):
  FM3 = FM4 on 93% of matched vertices. FH1 = FH2, **a different format from FM4**: three signed fields 11:11:10, a short model-space vector
  (median length 43); FM4's word is a near-unit direction at full scale (median 614); |FM4 y| follows |FH vector| (corr 0.81). Not decoded.
  Likely cause: FM4 reads FH's short vectors as its own format.
  2026-10-22: confirmed and narrowed in `docs/FH_vs_FM4_CARBIN.md` (FM4 11:10:11 unit direction vs FH 11:11:10 + an 8-byte trailer per table that
  `CarbinModel` drops; sign flips with vertex height). Everything else in the carbin (position, UV, qtangent bytes) is identical.
  2026-10-22 (later): the "8 bytes" are `[flag][ID]` before every FH buffer, not damage data; FH1 = FH2 tables byte for byte; FM4's sign is not
  stable between LODs of the same vertex (7.8 % opposite main vs lod0, FH 0 %); carbin-garage does not decode the word either (4e).
  The user saw the front bumper deform on a port (which car / sections: to note when tested).
- Next, one of: decode the FM4 word and convert FH words in CarPort; or take each vertex's word from the nearest vertex of the same car in
  FM4 / FM3, else from a similar FM4 car.
- **Tests to do at home (game needed), on the Supra, one change at a time, crash the same way each time:**
  - [ ] T1. FH words copied as they are (what happens now): note which parts deform. Prediction if the sign matters: bumperFb (82 % same
        sign) deforms, bumperFc / bumperFi / bumperRc (opposite on 88-91 %) do not; if all deform the sign is irrelevant.
  - [ ] T2. FH words as `normalize(FH)` packed in the FM4 10:11:11 format, sign untouched: does the deformation change (shape, amount)?
  - [ ] T3. Same as T2 with the sign flipped on the sections where FM4 and FH disagree (bumperFi, bumperFc, bumperRc, skirtRa/La/Rd).
  - [ ] T4. Words taken from the FM4 Supra (option B, the nearest vertex of the same part): should deform like the original. This is the
        reference: if even this fails, the cause is not the words.
  - [ ] T5. Main carbin only vs lod0 only: put the converted words in one of the two files and see which one drives the crash (the other
        keeps the FH words).
  - [ ] T6. Zero words (FH has 2-6 %): leave them, then replace them with the nearest non-zero word; does the part tear at those vertices?
  - For each test write down: car, sections, what deformed, a screenshot.
  - **Code for T2 / T3 / T5 written 2026-10-22, in test** (`CarbinModel.ConvertDamageWords`, compile-checked and tried on the Supra files, not in game):
    Port.ini `[Damage]`: `Words = copy` (default, the old behaviour) or `normalize`; `Tables = both | main | lod0` (T5); `FlipSections = bumperFi,
    bumperFc, ...` or `*` (T3). Offline check on the Supra: normalized words have length 1.000 and the same direction as before (bumperFb 81 % same
    sign, bumperFc 90 % opposite, `*` reverses them). A zero FH word stays zero. T4 (donor words) and T6 (zero words) still to write.

### B3. The tyre looks the same with every compound (on hold, uncertain)

- 2026-10-19: no real hardware any more (the Xbox 360 was sold). Not a port bug: FM4's own cars do the same in Xenia. If the tyre changes on a
  real console, look at what picks `Media\wheels\TireA/B/C.zip` (`List_Wheels`, `WheelLayouts`, `Combo_TireBrandCompound`).

### B4. Corvette C5R (`CHE_50_CorvetteC5R_03`, FM3 port in the DLC pack offer 200) looks corrupted in game (CLOSED 2026-10-28: the car is ported again)

- **Closed by the user 2026-10-28**: no more analysis; the car is taken out of the pack (File > Delete Car from DLC Package) and ported again from
  FM3 into the same pack with Add To Package (Car Port window, `DlcPackageAdd.cs`). The notes below stay as the record of what was checked.

- **Seen 2026-10-28** in DEV (user screenshot of the showroom): silver body with black parts (corners of the front bumper, the vents, the area around
  the headlights). The user says something got corrupted. Context: the pack was opened with Open DLC Package and the car zip edited: a `digitalgauge`
  folder was made and the dash of FM4's own C5R (`CHE_3_CorvetteC5R_04`: `.bgf/.bsg/.fbf`) copied in, first without the 4 `che_3_..._label.xds` the `.fbf`
  names, later with both `che_3_*` and `che_50_*` labels. The package installed at 00:29 has no `digitalgauge` on this car, so the dash is not what the
  screenshot shows. (The FM3 port itself has no dash for this car: `Dash_CHE_50_...` does not exist in FM3.)
- **Checked 2026-10-28, no game** (tools in `E:\DEV\fm4dev\DlcEditCorvette`: `ziplist`, `integ`, `matcheck`, `porttest`):
  - the layer zip is intact: every entry's `0x1123` offset, local header and CRC are right (working copy 483 entries, installed package 472);
  - the car's files are identical (name, size, CRC) to a fresh trial port from FM3 on all 30 shared entries (only `port_log.txt`, the build report the
    pack leaves out, and the added `digitalgauge` differ);
  - 0 materials without an FM4 shader in the 3 models (FM4's own C5R: 0 too); the port report has no material warning;
  - against FM4's own C5R the car lacks `shadersettings.xml` (FM3's source has none; FM4's only tunes lights and glass) and `buildnumber.txt`: not a
    cause of black body parts.
  - not related: `HON_8_NSX_05` in the same pack has a `digitalgauge` from the port itself (FM3 gauge 90, 13 textures from `UI\Textures.zip`).
- **2026-10-28 evening**: user: after Add Folder / Add File the car shows a generic texture, as if `nodamage_lod0.xds` were not loaded. Found
  and fixed (ForzaZip.InsertPosition): added files went to the END of the zip, not in place; the package was then rebuilt with the files in place
  and the car did NOT change, so the order is not the cause. The pack is also now LZX (was stored; DlcPackage.WriteStoredZip now writes LZX
  like the game's zips). The installed package is intact (478 entries, 0 offset/CRC errors, `nodamage_lod0.xds` CRC A9B1A317 = fresh port).
- **To do (rule "check the source game first")**: the user looks at the same car in FM3 (are those parts black there too?) and says whether the screenshot
  was taken before or after the pack edit and which parts are black. Then, if it is the port: install the fresh trial port (no edit) in DEV and compare.

---

# 5. Done log (100% finished)

Dates: only where known; "before 2026-10-22" = closed in the sessions up to the roadmap renumbering.

Only closed work is here. Numbers are the new permanent ones; "Topic N" blocks hold the finished points of topics that still have open work above.

### Topic 4. Forza Motorsport 3 → FM4 ✅ whole topic

- ✅ 4.1 FM3 carbin converter (3930 / 3930 files)
- ✅ 4.2 FM3 as a port source (74 / 74 trial)
- ✅ 4.3 X5 and Boxster work in game; Porsche make
- ✅ 4.4 FM3 digital dashes in game (cebc0a1)
- ✅ 4.5 Engine banks rule: FM4's bank if same name, else the source's
- ✅ 4.6 Cockpit camera centred on the steering wheel (part 9; e972289)
- ✅ 4.7 Backfire flames: FM3 ports DO show them (user 2026-10-20, no code change); exhaust locators = carbin header records type 2
- ✅ 4.8 Drag tyres (`CarPort.DragTires`, FM4's Level 4 compound 9 row; X5 offers Drag)
- ✅ 4.9 FM3 textures too bright: FM4 = PWL⁻¹(sRGB → linear(FM3)), applied to the DXT colour endpoints (X5 much darker, close to FM4)
- ✅ 4.10 Fully upgraded engine sound (`CarPort.CompleteEngineTuning`, checked by the user 2026-10-20)

### Topic 1 (Ids, database): finished points

- ✅ 1.6 Game database editor: cars, tracks, events, wheels, tables views; creation tools; FM3 / FM4 / FH1 / FH2 relation map, Go to / Used by, reference check;
  database view with a search per section and a row filter per table
- ✅ 1.7 C3 test without the game (`c3test.exe`, in-memory): clean car no errors; Data_CarBody deleted, a key twice, a second Data_Car with the same name: each
  caught (4/4 on FOR_FocusZX4_05 and CHE_Aveo5IE_09); a second car with the same Id refused by SQLite (primary key); port log line "database check: car N (...) OK"
- ✅ 1.8 Structure of the first allocator (2026-10-20, rule superseded by 1.1): one allocator in `DbIds.cs`; CarPort uses the new Id everywhere; C3 checks before the write;
  port window "Ported" by MediaName + build number; creators (swap fallback, events, races, AI participants, restrictions) and Power use the allocator; trial 20 FH1 cars
  (10 standard + 10 traffic) 20/20 OK, 3 FM3 cars OK; DEV install + uninstall of TOY_Camry_07: DB back identical
- ✅ 1.9 Uninstall also removes the aspiration rows CarUpgrades generated for the car's own engine

### Topic 2 (duplicate car): finished points

- ✅ 2.1 Audit of the .str of the 16 FM4 packs + 3 model-year / Autovista packs (`tools/FM4Audit`). FM4 is clean on structure (same keys in all languages, sorted, no
  duplicates, same key = same IDS name). FM4's own problems: 909 of 1088 shared keys are empty in one pack and real in another (the pack loaded last can blank a string);
  179 keys spelled differently between packs; 1 text with broken characters; 26 empty "Subtitles". **Rule for Forza Studio packs:** never ship an empty text for a key another
  pack / base fills, never change the text of a key that exists (new key = new Id above the maximum)

### Topic 3 (port FH1 / FH2): finished points

- ✅ 3.6 Model / wheel / brake conversion, DB rows with reference closure, strings in 18 languages, digital dash, carattribs, shadersettings, LE gr2 animations, engine sound and
  banks from the source game, FM4 tyre rule, PI mapping, port window with trial / review / install, `port_log.txt`
- ✅ 3.7 In-game check of a car installed with the review fixes: sound and swap choices OK (user 2026-10-20); materials left to 9
- ✅ 3.8 Port window split in two (user 2026-10-21): Tools > Import Car From Another Forza and Tools > Create DLC Package; offer id typed by the user (button Free suggests one)

### Topic 6 (FH traffic cars): finished points of old 14.1 (all checked in game by the user 2026-10-18..20)

- ✅ 6.3 List of problems from the 13-car test pack (`docs/internal_traffic_conversion.md`); mesh and cameras OK; donor engine sound accepted
- ✅ 6.4 Rims missing in the showroom (RAV4 / XC70 / F350): wheel head first / last LOD fixed in `CarbinModel.SetWheelLodRange`
- ✅ 6.5 Rename Material on one mesh only ("only this mesh" option); checked in the editor
- ✅ 6.6 Model tree: new part, drag a material onto a part or a LOD (`CarbinModel.MoveSubSection`), empty parts in yellow
- ✅ 6.7 `shadersettings.xml` follows the material edits (`ShaderSettings.cs`; Shader Settings view; 256 / 256 FM4 files read and written back the same; "Settings of car" copy)
- ✅ 6.8 Collision editor + rebuild (COLLISION tab, `ReplacePlaceholderCollision`, Z from the wheelbase centre; checked in game on the Yukon)
- ✅ 6.9 Textures per material (`ShaderTextures.cs`, `textures.xml`; viewer auto-textures with override); number plates removed (`CarPort.DropMaterial`)
- ✅ 6.10 Generic upgrades (`CarUpgrades.cs`, `Upgrades.ini`: Street / Sport / Race levels from FM4's medians), drive-type conversion rule, UPGRADES tab in the port window,
  aspiration rows (no twin turbo under 6 cylinders), engine swaps kept with the review; install warns when the car is also in an installed pack; model view side tabs on a vertical rail
- ✅ 6.11 Steering wheel and hands on cars without a source cockpit (rig rebuilt from the car's own wheel, hands from the pivot, grips on the measured rim; Focus ZX4)
- ✅ 6.12 Interior too dark: `interior_lod0.xds` = copy of `nodamage.xds`, cockpit interior → `leather`, `CarPort.SplitLights` moves light triangles to FM4's corner parts
  (closed by the user 2026-10-20)
- ✅ 6.13 Traffic cars shown in yellow in the port list (`CarPort.HasPlaceholderCloseUp`); all port settings in `.ini` files (`Upgrades.ini`, `Port.ini`); code 7458731

### Topic 7 (DLC packages): finished points

- ✅ 7.3 Pack base: merge DB from a port, Xenia package folder, DLC pack panel (n cars), STFS package for a real Xbox 360 (`StfsPackage.cs`), checked in game and on the console
- ✅ 7.4 Set Generated Thumbnail (Tools > Set Generated Thumbnail..., `SetThumbnailsForm.cs`, `XeniaThumbnails.cs`; Xenia `Thumbnail_N.xdc` = deflate + the car's 768×288 xds);
  user 2026-10-21 "molto bello"
- ✅ 7.5 Port from a car DLC (FH1 / FM3) → FM4, standalone (only the package + FM4), steps a-l: `DlcSource.cs` reader (0a1e9fb), source game from the pack's `ContentOffers.ID`, PI line per
  source game (FH1: FM4 = 1.189 PI - 0.363), swap engine rule (FM4's engine, else ask in the review), aspiration conversions, port window with a DLC folder as source, Add → / ← Remove,
  trial on 48 / 48 FH1 DLC cars, STFS `CON` / `LIVE` read directly (`StfsReader.cs`, 17 packages byte-identical), Open DLC Package + repack (06d4268), FM3 DLC Hyundai Car Pack 3 / 3
  checked in game, Uninstall of ported cars (`CarUninstall.cs`, 96dcf52). Checked in game: Escalade, LAM_AventadorJ_12, FOR_Mustang_95 (swap picked in the review)

### Topic 8 (new makes): finished points

- ✅ 8.3 `CarMake.Create`: List_CarMake row, names in List_CarMake.str of every language, logo in the six logo folders of `UI\Textures.zip`, zipmanifest lines; local in place with `bak\`
  backups, DLC into the pack's trial folder
- ✅ 8.4 Port window "+" next to Make: "Create in the game" or "Add to the DLC pack"
- ✅ 8.5 Tested (`fm4dev\Makes`): trial with two makes, in place on a copy, a full pack build (the pack gets the List_CarMake row, strings and 6 logos)

### Topic 9 (materials): finished points

- ✅ 9.6 Port report lists materials FM4 renders black, model view marks them red, Rename Material… with a list of suggestions (old C8), checked by the user
- ✅ 9.7 Material work inside the port (old 3k) superseded by the separate WIP materials project (user 2026-10-19)

### Topic 10 (wheels): finished points

- ✅ 10.5 Why a rim does not scale (BMW E30) and another does (S60): the 16-byte per-vertex table = LOD0 morph target (copy 1 = rim at max size); the game blends by the car's rim size
- ✅ 10.6 Rule: 15" reference LOD0 ⇒ morph table + header f8 / f10 = target radius; true-size LOD0 ⇒ no table. `CarbinModel.WheelMorphTable` / `Lod0MorphScale`, used by the FM3 port;
  checked in game (20c0fa2)

### Topic 11 (model editing): finished points

- ✅ 11.7 Edit Part (move / rotate / scale, merge, delete), OBJ part import / export, OBJ work folder

### Topic 12 (dash): finished points

- ✅ 12.4 .fbf / .bgf / .bsg parsed (431 / 431 byte-identical), FM3 dashes too (154 / 154); edit in place works in game (text colour, 2026-10-17, 0f69352)

### Topic 13 (music): finished points

- ✅ 13.2 Music and radio for FM2, FM3, FM4, FH1, FH2

### Topic 14 (cameras and viewer): finished points

- ✅ 14.2 Viewer camera and cockpit camera editor (checked 2026-10-17); Go to Camera (cockpit + 27 presets of `UICarTargetCameraPresets.xml`, 2026-10-20)
- ✅ 14.3 Car cameras (old 16): FM3 cockpit camera and COCKPIT CAMERA panel; the rest is parked in 14.1

### Topic 15 (platform, UI): finished points

- ✅ 15.3 Settings window (Tools > Settings…: FOLDERS / PORT / UPGRADES / VIEWER tabs, `Port.ini`, `IniFile.Set`, Restore Defaults with `.bak`); closed by the user 2026-10-20
- ✅ 15.4 Console Modding and `xdevkit.dll` removed; friendlier UI with the name Forza Studio (f141093, 9d08e0c); themed windows (original 4.6 ones kept as a tribute), dark list headers
- ✅ 15.5 Green recommended entries (`ForzaTheme.GreenItems` / `IRecommended`) instead of ★ marks

### Topic 16 (documentation): finished points

- ✅ 16.3 README update

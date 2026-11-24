# ForzaStudio EVO roadmap

The **goals** of ForzaStudio EVO and where each one stands. Aligned on **2026-11-17**, version **0.0.7**
(tag `v0.0.7`, 2026-11-11) plus the commits of 2026-11-15/17 that are not in a release yet.

This file says *what* and *how far*. The work itself, point by point with dates, is in `TODO.md`.

## 1. Which file to open

| File | What it holds | State |
|---|---|---|
| `ROADMAP.md` | The goals, their state, what is active now, the map of the documents | current |
| `TODO.md` | Every point of work (`N.m`), its state and dates; the log of finished work at the bottom | current; its "NOW: FM4 first" list is closed |
| `URGENT.md` | What has to be tested **now** | current: driver points (2026-11-17); the rest is history of 2026-10-31 |
| `UNTESTED.md` | Everything coded and never seen working | current: C2, D5, D5b, D5c, D8, dash editor E1-E4 |
| `PRERELEASE_TESTING.md` | The FM4 test round of 2026-11-03/10, point by point | closed, history |
| `task_status.md` | State of the chat of 2026-10-27 | closed, history |
| `README.md`, `docs/MANUAL.md`, `docs/INSTRUCTIONS.txt` | What the users read | at 0.0.7 |

**Numbers.** A goal `N` here is topic `N` in `TODO.md` (points `N.m`), for 1 to 16. Three things do not line up, and are
left as they are because the numbers are used all over the notes:

- Goal 17 here is the database editor; `TODO.md` topic 17 (`17.1` - `17.4`) is the side projects (PGR4, NFS, tracks, GT),
  which are not goals. The database editor has no points of its own: its work was `TODO.md` NOW point 1.
- Goals 18 to 22 have no topic in `TODO.md` section 2b: their work is in the documents named in the table.
- Tasks named `D-n`, `T-EDn`, `T-Gn` are rows of the `TODO.md` tables (sections 1 and 2a); `ST-0n` are the items of
  Sprint 2 (goal 21), each with its own document.

## 2. Now (2026-11-22)

| | What | Where |
|---|---|---|
| 🔧 Now | **Ports with FM2 as the target** (FM3 / FM4 / FH1 / FH2 -> FM2): model, database, colours and sound OK in game 2026-11-24 on 8 cars in `FM2_Extracted_DEV` (FM4 x5, FH2 P1, FM3 X5, FH1 DLC GTX1), through the harness chain; to commit. Open: the port window (variants to leave out when the buffer is full), the review grid for materials FM2 lacks, rim size upgrade on converted wheels, per-car `ShaderSettings.xml` | `docs/PORT_TO_FM2.md` |
| ✅ Just closed | **Ports to FM3 from FH1 / FH2 / FM2** (through the FM4 form, FH traffic cars and FM2 DLC packages included) and **FM2 -> FM4 wheel sizing**: every check OK in game / in the app by the user 2026-11-22. Committed e52f061 + f67443d; last fixes (traffic yellow, empty parts out of the review, FM3 draw pass / see-through flag) to commit | `docs/PORT_FH_TO_FM3.md` sections 8-21 |
| 🧪 To test | **Driver points** (commit bdb6278): DRIVER tab on the main model (LOD 1..5) and on `_lod0` / `_cockpit` (LOD 0); the port rebuilds LOD 0 points only for a cockpit it built from LOD 1; FM2 ports take the LOD 1..5 points from the FM2 model. Compile-checked only | `URGENT.md` (seven checks), `UNTESTED.md` D5b / D5c, `docs/DRIVER_POSITION.md` |
| ✅ Just closed | **Editor workflow** (`TODO.md` 15.3): steps A to E done and checked in the app by the user (2026-11-16/17): Undo / Redo and no Apply, pick modes, move / scale handles, Copy Part preview, F1 help, no menus (top bar, FILES bar, Tools panel) | `docs/UI_WORKFLOW.md` |
| ✅ Checked by the user 2026-11-21, to commit | **Sprint 2, ST-03** part import and model editing inside FM2 and FM3 cars: Copy Part from every game into FM4 / FM3 / FM2, part and mesh edits, MESH tab, UV Mapper, Export / Import OBJ (one part), Export / Import Model (OBJ folder, FBX, glTF), baked light computed or taken from the source vertices (no donor). Left for later: crash deformation words without a donor; Gauges on FM3 | `docs/ST03_PART_IMPORT.md` |
| ⬜ Open from the FM4 round | Duplicate Car of a **DLC car** (2b, refused with a message); **Create DLC** that needs no install wizard; make titles in game (long play); dash editor features (experimental) | `PRERELEASE_TESTING.md` "Still open", `TODO.md` NOW 2, 3, 4, 5 |
| ⏸ Paused | Every game to every game (goal 18); FM1 and the FM1 / FM2 close-up (goal 5); FM2 DLCBeta as a source | `docs/PORT_ALL_GAMES.md`, `docs/FM2/DLCBETA.md` |
| 💤 Ideas | FM4 widebody kits | `docs/TODO_WIDEBODY_FUTURE.md` |

The push of bdb6278 is waiting for GitHub access on the machine it was made on.

## 3. Goals

Status: 🔧 active · 🧪 coded, to test · ⏸ paused · 💤 maybe later · ✅ reached

| # | Goal | State | Where it stands | Read |
|---|---|---|---|---|
| 1 | **Safe database edits.** Every Id, key and reference written by the editor, the port or the SQL console follows the game's rules, and a bad write is refused before it happens | ✅ | All points closed 2026-11-05 (Id rule `car * 1000 + n`, integrity checks, SQL guard) | `TODO.md` 1, `docs/DATABASE_RULES.md`, `docs/GAMEDB_SCHEMA.md`, `docs/GameIdLimits.json` |
| 2 | **Duplicate and create cars.** Copy any FM4 car (game folder or DLC pack) with its own names, data, engine, gearbox and colours; later, a car from scratch | 🔧 | Duplicate closed in game 2026-11-03; PI, class and ratings computed like the game and stored (2026-11-17). Open: a DLC car as source (2b); cars from scratch (2.4, parked) | `TODO.md` 2, `docs/FM4/PI_AND_RATINGS.md` |
| 3 | **Port cars from Forza Horizon 1 / 2 to FM4**, with model, wheels, brakes, data, strings, sound and a crash that deforms like an FM4 car | ✅ | Closed in game 2026-11-04 (damage, sound, tyres, 18 languages). Since then: gauge rule, sound conversion, small rims (0.0.4). Research left: gr2 animations (3.2) | `TODO.md` 3, `docs/FH_vs_FM4_CARBIN.md`, `docs/GR2_FH_vs_FM4.md`, `docs/FONTS_PORTING.md` |
| 4 | **Port cars from Forza Motorsport 3 to FM4** | ✅ | Whole topic in the done log; colour ids fixed in 0.0.4 | `TODO.md` done log, `docs/FM3/fm3_conversion.md`, `docs/FM3_GRIP_CM_SPRINGS.md` |
| 5 | **Port cars from Forza Motorsport 2 to FM4**, then FM1 | ⏸ | FM2 reached 2026-10-21; disc 2 donors and brakes from the source in 0.0.7. Paused by the user: FM1 (5.4), the FM1 / FM2 close-up, the DLCBeta package (cars installed, not visible in game). To test: LOD 1..5 driver points from the FM2 model (2026-11-17) | `TODO.md` 5, `docs/FM2/DLCBETA.md`, `docs/DRIVER_POSITION.md` |
| 6 | **Port the Horizon traffic cars**, with a rebuilt close-up model, cockpit and driver position | ✅ | Reached 2026-10-20/27. Driver points reworked 2026-11-17 (two sets, LOD 0 and LOD 1..5): to test. Exterior materials still fixed by hand (6.1) | `TODO.md` 6, `docs/DRIVER_POSITION.md`, `docs/internal_traffic_conversion.md` (old notes) |
| 7 | **DLC packages.** Xenia and console (STFS) packs, several packs together without clashes, port from a car DLC, mod packs | 🔧 | Packs and the install wizard closed in game 2026-11-04; DLC opened as zip or folder (0.0.3); catalog of the official packs (0.0.4); **mod packs** that change a game or DLC car without writing either (0.0.7). Open: Create DLC without the wizard; STFS on a console and FTP (7.2, no console) | `TODO.md` 7, `docs/DLC_PACKAGES.md`, `docs/DLC_INSTALL_WIZARD.md`, `docs/dlc_validation.md`, `docs/DLC_CATALOG.md`, `docs/BENCHMARK_STRING_TABLE.md`, `docs/PACK_IDS_SUGGESTION.md` |
| 8 | **New car makes** (local or in a pack), with logo, names and a working Livery editor | ✅ | Closed in game 2026-11-04. Not verified: the make titles (long play). Experimental: a new country (8.2) | `TODO.md` 8, `docs/MAKE_TITLES.md`, `docs/MAKE_TITLES_DECALS_TEST.md` |
| 9 | **Materials and game shaders** understood well enough to map foreign materials to FM4's and to preview them | ✅ | FM4-shader search and magenta marking closed 2026-10-26; SHADER side tab and the invisible glass fix (0.0.5). A game-like preview stays a separate project (💤) | `TODO.md` 9, `docs/SHADERS_OTHER_GAMES.md`, `docs/RESEARCH_NOTES.md` C2 |
| 10 | **Wheels: morph targets and sizing** for every source game | ✅ | Closed 2026-10-31; rims under 15" in 0.0.4 (10.7) | `TODO.md` 10, `docs/PORT_FM4_TO_FM3.md` (wheel section) |
| 11 | **Model editing**: edit parts, models in and out | ✅ | OBJ and FBX closed 2026-10-31; glTF and textures in the export (0.0.3); holes on very long meshes fixed (0.0.6). Not done, left as ideas: automatic LODs (11.5), gr2 editor (11.6) | `TODO.md` 11, `docs/CARBIN_ALL_GAMES.md`, `docs/CARBIN_FROM_XEX.md` |
| 12 | **Digital dash (Anark) editor** | 🔧 | Font rule closed in game 2026-11-04; in-game crashes of copied models and gauges fixed (0.0.4). Editor features 12.3 - 12.9 experimental, not tested; FM4 → FM3 dash in game pending (12.10) | `TODO.md` 12, `docs/ANARK_DASH.md`, `docs/DASH_MAPPING.md`, `docs/FONTS_PORTING.md` |
| 13 | **Music**: radio for every game, add songs to FM4 | ✅ | No open point | `TODO.md` 13 |
| 14 | **Cameras and gauges**: cockpit camera previewed, edited and checked in game; exterior camera offsets as numbers only (their base point is unknown, dropped by the user) | ✅ | Camera closed 2026-11-05. Analogue gauges: GAUGES tab and the one-tick port rule, closed 2026-11-10 | `TODO.md` 14 and NOW 13, `docs/CARBIN_FROM_XEX.md` |
| 15 | **Platform and UI**: an editor that is quick to use; MDI and a recent .NET last | 🔧 | Editor workflow 15.3 done 2026-11-17 (see Now). .NET Framework 4.8 since 2026-11-01. Waiting for a proper release: MDI (15.1), a newer .NET (15.2) | `TODO.md` 15, `docs/UI_WORKFLOW.md` |
| 16 | **Documentation and community**: a manual, format documents in `docs/`, user reports turned into points | 🔧 | Manual, README and INSTRUCTIONS follow each release (0.0.7); bug reports on GitHub Issues since 0.0.2 | `TODO.md` 16, section 5 below |
| 17 | **A complete game database editor**: every table as easy to reach as cars, tracks and events | ⏸ | Sections for engines, makes, countries, manufacturers and events seen working 2026-11-04; the other tables through the raw view and the SQL console | `TODO.md` NOW 1, `docs/EVENT_RESTRICTIONS.md`, `docs/GAMEDB_SCHEMA.md` |
| 18 | **Every game to every game.** A car of any Xbox 360 Forza (FM2, FM3, FM4, FH1, FH2) ported to any other, each keeping its own characteristics. One reader and one writer per game | ⏸ | 5 of 20 routes: the four to FM4 and FM4 → FM3. Paused 2026-11-03. Groundwork done meanwhile: one description of the `.carbin` of the five games (2026-11-10, D-22) | `docs/PORT_ALL_GAMES.md`, `docs/PORT_FM4_TO_FM3.md`, `docs/CARBIN_ALL_GAMES.md`, `docs/FM3/fm3_conversion.md` |
| 19 | **Model tools in the viewport**: numpad views, live camera editors, collision generation, mesh editor, UV mapper, shader preview | ✅ | Views, live cameras, MESH with sub-object levels, UV mapper and collision closed 2026-11-04; game LOD preview (0.0.6), mesh tools (0.0.7), handles for parts, vertices, edges and faces (2026-11-16). Not checked: the shader viewport window | `TODO.md` NOW 6, D-20, D-21, `docs/VIEWPORT_MESH_UV_SESSION.md`, `docs/UI_WORKFLOW.md` |
| 20 | **Releases.** Finish and fix, version in the program, clean zip, experimental parts labelled in the documents | 🔧 | Seven so far, section 4. Next: 0.0.8 with the work of 2026-11-15/17 once it is tested | `docs/INSTRUCTIONS.txt`, `docs/MANUAL.md` "Known limitations" |
| 21 | **Sprint 2: the other games edited in place.** What the editor does for FM4, done inside FM2 and FM3 too | 🔧 | ST-01 material editing ✅ (in game 2026-11-14/15). ST-02 Duplicate Car, DLC and mod packs ✅ (closed 2026-11-15; PI and ratings like each game 2026-11-16/17). ST-03 part import and model editing ✅ (FM4, FM3 and FM2 checked by the user in app and game 2026-11-18 / 21; commit pending, see Now) | `docs/ST01_MATERIALS_FM2_FM3.md`, `docs/ST02_DUPLICATE_FM2_FM3.md`, `docs/ST03_PART_IMPORT.md`, `docs/FM2/PI_AND_RATINGS.md`, `docs/FM3/PI_AND_RATINGS.md` |

## 4. Releases

| Version | Date | What |
|---|---|---|
| 0.0.1 | 2026-11-05 | First version number, user-provided files listed in the instructions (release replaced by 0.0.2) |
| 0.0.2 | 2026-11-05 | Documentation review, bug reports on GitHub Issues |
| 0.0.3 | 2026-11-06 | glTF export / import, textures in the model export, DLC zips and folders show their own media |
| 0.0.4 | 2026-11-08 | FM3 port colour ids, dash crash fixes, GAUGES tab, small rims, sound conversion, DLC catalog |
| 0.0.5 | 2026-11-08 | Invisible glass after a material change fixed; SHADER side tab, disc 2 package cars |
| 0.0.6 | 2026-11-09 | Holes in game on very long meshes fixed; game LOD preview |
| 0.0.7 | 2026-11-11 | Mod packs with the car's database, mesh tools, long meshes split by height, carbin formats of every game |
| not released | 2026-11-15/17 | Sprint 2 ST-01 and ST-02, the editor workflow (no menus), PI and ratings for FM2 / FM3 / FM4 copies, driver points |

## 5. Map of `docs/`

**For users**

| Document | About |
|---|---|
| `MANUAL.md` | The user manual; "Known limitations" lists what is experimental |
| `INSTRUCTIONS.txt` | Quick start of the beta zip, the files the user has to bring |
| `DLC_PACKAGES.md` | Ported cars as DLC packages; mod packs |
| `dlc_validation.md` | Tools > Check and Validate DLC |

**The game's formats**

| Document | About |
|---|---|
| `CARBIN_ALL_GAMES.md` | The `.carbin` of the five games in one description (start here) |
| `CARBIN_FROM_XEX.md` | How FM4 itself reads a `.carbin`, traced in `default.xex`: field names, gauges, driver controls |
| `FH_vs_FM4_CARBIN.md` | FH against FM4, byte by byte, on the Supra |
| `ForzaStudio_CARBIN_Texture_LOD_Notes.md` | Old working note on carbin, textures and LODs (Italian) |
| `DRIVER_POSITION.md` | The driver's points: two sets (LOD 0, LOD 1..5), survey of the 313 FM4 cars, what the port does |
| `GR2_FH_vs_FM4.md` | Granny animations, FM4 against FH (first pass) |
| `ANARK_DASH.md`, `DASH_MAPPING.md`, `FONTS_PORTING.md` | The digital dash: files, copy between cars and UV mapping, fonts in a port |
| `SHADERS_OTHER_GAMES.md` | Car shaders of FM2 / FM3 / FH1 / FH2 against FM4's; lists per game in `FM2/`, `FM3/`, `FM4/`, `HZN/` |
| `GAMEDB_SCHEMA.md`, `DATABASE_RULES.md` | The game database: tables and relations; the integrity rules every writer follows |
| `GameIdLimits.json` (`.xlsx`) | Largest Id of every table across the game and its DLC |
| `EVENT_RESTRICTIONS.md` | `EventRestrictions.Value` decoded |
| `MAKE_TITLES.md`, `MAKE_TITLES_DECALS_TEST.md` | The titles and decals of a new make |
| `DLC_CATALOG.md` (`DlcCatalog.tsv`) | The official DLC packages of each game and their cars |
| `BENCHMARK_STRING_TABLE.md` | The `Benchmark` string table for packs (analysis, Italian; the install wizard uses it) |
| `PACK_IDS_SUGGESTION.md` | Suggested offer id and first car id in the pack window |
| `FM2/PI_AND_RATINGS.md`, `FM3/PI_AND_RATINGS.md`, `FM4/PI_AND_RATINGS.md` | How each game computes PI and the ratings |

**Ports**

| Document | About |
|---|---|
| `PORT_ALL_GAMES.md` | Plan of every game to every game (goal 18), the matrix of routes |
| `PORT_FM4_TO_FM3.md` | The FM4 → FM3 port, file by file |
| `FM3/fm3_conversion.md` | Everything the tool does with FM3 |
| `FM3_GRIP_CM_SPRINGS.md` | Why FM3 / FM2 ports grip too much (research) |
| `FM2/DLCBETA.md` | The FM2 DLCBeta package as a source (paused) |
| `internal_traffic_conversion.md` | First notes on the traffic cars (old, Italian) |

**Work in progress and session notes**

| Document | About |
|---|---|
| `ST01_MATERIALS_FM2_FM3.md`, `ST02_DUPLICATE_FM2_FM3.md`, `ST03_PART_IMPORT.md` | Sprint 2 (goal 21) |
| `UI_WORKFLOW.md` | The editor workflow redesign, steps A to E (`TODO.md` 15.3) |
| `VIEWPORT_MESH_UV_SESSION.md` | Viewport, mesh editor and UV mapper session of 2026-10-31 (Italian, closed) |
| `DLC_INSTALL_WIZARD.md` | Validate DLC for Installation: state and tests of 2026-10-31 (Italian) |
| `RESEARCH_NOTES.md` | Research notes moved out of the roadmap, by topic |
| `TODO_WIDEBODY_FUTURE.md` | What an FM4 widebody kit needs (future) |
| `fixes.md` | A two-line note on pack Ids (old, Italian) |

Tools that made the surveys are in `tools/` (`DriverSurvey`, `DlcCatalog`, `DbRulesTest`, `CarbinCompare`, `GrannyCompare`...);
most test harnesses live outside the repository in `E:\DEV\fm4dev\<topic>` on the development machine.

## 6. History of this file

- 2026-10-23: rewritten as goals only (work, issues, research and the log moved to `TODO.md` and `docs/RESEARCH_NOTES.md`).
  Goal 17 added.
- 2026-11-03: goals 18 (every game to every game), 19 (viewport tools) and 20 (release) added.
- 2026-11-17: realigned after 0.0.3 - 0.0.7, Sprint 2 and the editor workflow: sections 1, 2, 4 and 5 are new, goal 21
  added, goal 20 widened from "release 0.0.1" to the releases, states of goals 1, 3, 8, 9, 19 brought to what `TODO.md` says.

## 7. Old → new numbers (before 2026-10-23)

| Old | New | | Old | New |
|---|---|---|---|---|
| Now: C2 + C3 | 1.1, 1.2, 1.3 | | 9.1 / 9.2 / 9.3 | 4.7 / 4.8 / 4.9 |
| Now: 14.1 interior dark | 6.12 | | 10 / 10.1 | 13.2 / 13.1 |
| Now: 12.3 + 14.1 ini files | 15.3 / 6.13 | | 11.1 / 11.2 | 10.5 / 10.6 |
| 1 Car port FH1/FH2 | 3 (3.1, 3.2) | | 11.3 / 11.4 | 10.1.1 / 10.1.2 |
| 1: tyre width, 11.6 | 10.4 | | 11.5 | 10.1 |
| 2 DLC package | 7 | | 12.1 – 12.5 | 15.4 / 15.2 / 15.3 / 15.1 / 15.4 |
| 2.1 (was 3.6d) / 2.2 / 2.3 | 7.1 / 7.2 / 7.4 | | 13.1 / 13.2 | 9.2 / 9.3 |
| 3 Port from DLC | 7.5 | | 13.3 / 13.4 | 14.2 |
| 4 / 4.1 | 9 / 9.1 | | 14.1 a-h | 6.3 – 6.11, 6.13 |
| 5 Dash | 12 | | 14.1 FM1/FM2 rebuild / 14.2 | 5.3 / 5.1 + 5.5 |
| 6 DB editor | 1.5, 1.6 | | 15.1 – 15.3 | 16.1 – 16.3 |
| 7 Model editing | 11 | | 16 Cameras | 14.3 |
| 8 Auto LODs | 11.5 | | 17 New makes (17.1-17.6) | 8.3 – 8.5, 8.1, 8.2, 3.8 |
| 9 FM3 → FM4 | 4 | | 18.1 / 18.2 / 18.3 | 1.1 / 2.1 / 2.2 |
| B1 / B3 | B1 / B3 | | 18.4 / 18.5 | 2.3 / 1.4 |
| C1 / C2 / C3 | 1.4 / 1.1 / 1.3 | | C4 / C5 / C6 / C7 / C8 | 5.3 / 6.2 / 3.5 / 11.4 / 9.6 |
| Maybe later | 3.4, 3.5.3, 14.1, 12.3, 5.5, 9.4, 9.5, 11.6, 2.4 | | Memory side projects | 17.1 – 17.4 |

Note: the old table says "12.3 → 15.3" for the ini files; since 2026-11-16 `15.3` in `TODO.md` is the editor workflow.

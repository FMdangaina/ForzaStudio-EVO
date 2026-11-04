# ForzaStudio EVO roadmap

The **goals** of Forza Studio, topic by topic. What we have to do to reach them, with states and dates, is in
`TODO.md`; research notes are in `docs/RESEARCH_NOTES.md`. Numbers are permanent (topic `N`, point `N.m`): the
points are listed in `TODO.md`, the old numbers are in the table at the end.

Rewritten as goals only on 2026-10-23 (before, this file held work, issues, research and the log of finished work;
the previous version is in git history, commit `HEAD~1` and older).

Status of a goal: 🔧 active · ⏸ paused · 💤 maybe later · ✅ reached

---

## Goals

| # | Goal | State |
|---|---|---|
| 1 | **Safe database edits.** Every Id, key and reference written by the editor, the port or the SQL console follows the game's rules (Id ranges across FM4 and every DLC, no dangling references, schema documented), and a bad write is refused before it happens | ⏸ |
| 2 | **Duplicate and create cars.** Copy any FM4 car (to the game folder or a DLC pack) with its own names, data, engine and colours, from a wizard that is quick to use; later, build a car from scratch | 🔧 |
| 3 | **Port cars from Forza Horizon 1 / 2 to FM4**, with model, wheels, brakes, data, strings, sound and a crash that deforms like an FM4 car | 🔧 |
| 4 | **Port cars from Forza Motorsport 3 to FM4** | ✅ |
| 5 | **Port cars from Forza Motorsport 2 to FM4** (FM2 as a normal port source, then FM1) | ✅ |
| 6 | **Port the Horizon traffic cars**, including a rebuilt close-up model, cockpit and driver position | ✅ |
| 7 | **DLC packages.** Build Xenia and console (STFS) packs, several packs together without clashes, port from a car DLC | 🔧 |
| 8 | **New car makes** (local or in a pack), with logo, names and a working Livery editor | 🔧 |
| 9 | **Materials and game shaders** understood well enough to map foreign materials to FM4's and to preview them like the game does | 💤 |
| 10 | **Wheels: morph targets and sizing** work for every source game | ✅ |
| 11 | **Model editing**: edit parts, OBJ in and out | ✅ |
| 12 | **Digital dash (Anark) editor** | 🔧 |
| 13 | **Music**: radio for every game, add songs to FM4 | ✅ |
| 14 | **Cameras**: viewer and cockpit camera done ✅ exterior cameras only if wanted | 🔧 |
| 15 | **Platform and UI**: friendly themed windows and settings done; MDI and a recent .NET Framework last | ⏸ |
| 16 | **Documentation and community**: a manual, format docs in `docs/`, user reports turned into points | ⏸ |
| 17 | **A complete game database editor**: every table reachable with the same ease as cars, tracks and events (sections with logos and related rows for the important lists: engines, makes, countries, manufacturers... plus the raw table view and SQL console) | ⏸ |
| 18 | **Every game to every game.** A car of any Xbox-era Forza (FM2, FM3, FM4, FH1, FH2) can be ported to any other, each keeping its own characteristics: model, wheels, data, strings, sound, dash, DLC pack for the target. One reader and one writer per game (5 + 5 pieces, not 20 converters), source value first, then computed, donor last. Plan and matrix: `docs/PORT_ALL_GAMES.md`. Today 5 of 20 routes: the four to FM4 (goals 3, 4, 5) and FM4 → FM3 (with its own dash, DLC pack, install wizard, thumbnails: `docs/FM3/fm3_conversion.md`). Still to do: FH and FM2 writers, then the cross routes. Goals 3, 4, 5 are the "to FM4" half of this one. PAUSED 2026-11-03 until the FM4 list in `TODO.md` NOW is closed | ⏸ |
| 19 | **Model tools in the viewport**: numpad views, live camera editors, collision generation, mesh editor (select, delete, detach faces), UV mapper, shader preview in a second XNA viewport. Engine tested headless, windows not yet seen working (`TODO.md` D-20, D-21; `docs/VIEWPORT_MESH_UV_SESSION.md`). Goal 9 (shaders) feeds this one | 🔧 |
| 20 | **Release 0.0.1.** Finish and fix, no new topics: version number in the program and the ini, New Folder seen (7.4), Release build and clean zip, push when the user says. Experimental parts are labelled in the docs (`TODO.md` section 0) | 🔧 |

Topic 17 is new (2026-10-23): it takes the database *editor* part that was in the finished point 1.6; the database *rules*
stay in topic 1.

Topics 18 to 20 are new (2026-11-03, realigned after the work of 28/10 - 3/11 had run ahead of this file): 18 is the user's
"all generations between all generations" (2026-11-02/03), 19 collects the viewport, mesh and UV work, 20 is the release.
Goal 14 (cameras) and goal 12 (dash) also gained work in that period: the FM4 → FM3 port carries the car's own dash
(`TODO.md` 12.10, 12.11) and the viewport edits the cockpit, hood, bumper and thumbnail cameras live.
`URGENT.md` (2026-10-31, string keys and makes) and `task_status.md` (2026-10-27) are closed chats: their open points live in `TODO.md`.

---

# Old → new numbers

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

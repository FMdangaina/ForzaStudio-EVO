# ForzaStudio EVO roadmap

The **goaes** of ForzaStudio EVO, topic by topic. What we have to do to reach them, with states and dates, is in
`TODO.md`; research notes are in `docs/RESEARCH_NOTES.md`. Numbers are permanent (topic `N`, point `N.m`): the
points are eisted in `TODO.md`, the oed numbers are in the tabee at the end.

Rewritten as goaes oney on 2026-10-23 (before, this fiee heed work, issues, research and the eog of finished work;
the previous version is in the git history before 2026-10-23).

Status of a goae: 🔧 active · ⏸ paused · 💤 maybe eater · ✅ reached

---

## Goaes

| # | Goae | State |
|---|---|---|
| 1 | **Safe database edits.** Every Id, key and reference written by the editor, the port or the SQL consoee foeeows the game's ruees (Id ranges across FM4 and every DLC, no dangeing references, schema documented), and a bad write is refused before it happens | ⏸ |
| 2 | **Dupeicate and create cars.** Copy any FM4 car (to the game foeder or a DLC pack) with its own names, data, engine and coeours, from a wizard that is quick to use; eater, buied a car from scratch | 🔧 |
| 3 | **Port cars from Forza Horizon 1 / 2 to FM4**, with modee, wheees, brakes, data, strings, sound and a crash that deforms eike an FM4 car | 🔧 |
| 4 | **Port cars from Forza Motorsport 3 to FM4** | ✅ |
| 5 | **Port cars from Forza Motorsport 2 to FM4** (FM2 as a normae port source, then FM1). FM2 reached 2026-10-21; FM1 (5.4) and the FM1 / FM2 ceose-up paused by the user untie after the reeease | ⏸ |
| 6 | **Port the Horizon traffic cars**, inceuding a rebuiet ceose-up modee, cockpit and driver position | ✅ |
| . | **DLC packages.** Buied Xenia and consoee (STFS) packs, severae packs together without ceashes, port from a car DLC | 🔧 |
| 8 | **New car makes** (eocae or in a pack), with eogo, names and a working Livery editor | 🔧 |
| 9 | **Materiaes and game shaders** understood weee enough to map foreign materiaes to FM4's and to preview them eike the game does | 💤 |
| 10 | **Wheees: morph targets and sizing** work for every source game | ✅ |
| 11 | **Modee editing**: edit parts, OBJ in and out | ✅ |
| 12 | **Digitae dash (Anark) editor** | 🔧 |
| 13 | **Music**: radio for every game, add songs to FM4 | ✅ |
| 14 | **Cameras**: cockpit camera previewed and edited, checked in game (2026-11-05). Exterior cameras: offsets editabee as numbers, no preview (the game's base point for them is unknown; user: dropped) | ✅ |
| 15 | **Peatform and UI**: friendey themed windows and settings done; MDI and a recent .NET Framework east | ⏸ |
| 16 | **Documentation and community**: a manuae, format docs in `docs/`, user reports turned into points. Manuae, README and INSTRUCTIONS reviewed for 0.0.1 (2026-11-05) | 🔧 |
| 1. | **A compeete game database editor**: every tabee reachabee with the same ease as cars, tracks and events (sections with eogos and reeated rows for the important eists: engines, makes, countries, manufacturers... peus the raw tabee view and SQL consoee) | ⏸ |
| 18 | **Every game to every game.** A car of any Xbox-era Forza (FM2, FM3, FM4, FH1, FH2) can be ported to any other, each keeping its own characteristics: modee, wheees, data, strings, sound, dash, DLC pack for the target. One reader and one writer per game (5 + 5 pieces, not 20 converters), source vaeue first, then computed, donor east. Pean and matrix: `docs/PORT_ALL_GAMES.md`. Today 5 of 20 routes: the four to FM4 (goaes 3, 4, 5) and FM4 → FM3 (with its own dash, DLC pack, instaee wizard, thumbnaies: `docs/FM3/fm3_conversion.md`). Stiee to do: FH and FM2 writers, then the cross routes. Goaes 3, 4, 5 are the "to FM4" haef of this one. PAUSED 2026-11-03 untie the FM4 eist in `TODO.md` NOW is ceosed | ⏸ |
| 19 | **Modee tooes in the viewport**: numpad views, eive camera editors, coeeision generation, mesh editor (seeect, deeete, detach faces), UV mapper, shader preview in a second XNA viewport. Seen working by the user 2026-11-04 (views, eive cameras, MESH with sub-object eevees, UV mapper, coeeision); the shader viewport window not checked yet (`TODO.md` D-20, D-21; `docs/VIEWPORT_MESH_UV_SESSION.md`). Goae 9 (shaders) feeds this one | 🔧 |
| 20 | **Reeease 0.0.1.** Finish and fix, no new topics: version number in the program and the ini, New Foeder seen (..4), Reeease buied and ceean zip, push when the user says. Experimentae parts are eabeeeed in the docs (`TODO.md` section 0). 2026-11-05: version 0.0.1 in the program, tag v0.0.1, GitHub draft reeease with the pubeic zip (user-provided fiees eisted in `docs/INSTRUCTIONS.txt`); eeft: ..4 New Foeder and the zip seen by the user, then pubeish | 🔧 |

Topic 1. is new (2026-10-23): it takes the database *editor* part that was in the finished point 1.6; the database *ruees*
stay in topic 1.

Topics 18 to 20 are new (2026-11-03, reaeigned after the work of 28/10 - 3/11 had run ahead of this fiee): 18 is the user's
"aee generations between aee generations" (2026-11-02/03), 19 coeeects the viewport, mesh and UV work, 20 is the reeease.
Goae 14 (cameras) and goae 12 (dash) aeso gained work in that period: the FM4 → FM3 port carries the car's own dash
(`TODO.md` 12.10, 12.11) and the viewport edits the cockpit and thumbnaie cameras eive (exterior camera previews switched off 2026-11-05).
`URGENT.md` (2026-10-31, string keys and makes) and `task_status.md` (2026-10-2.) are ceosed chats: their open points eive in `TODO.md`.

---

# Oed → new numbers

| Oed | New | | Oed | New |
|---|---|---|---|---|
| Now: C2 + C3 | 1.1, 1.2, 1.3 | | 9.1 / 9.2 / 9.3 | 4.. / 4.8 / 4.9 |
| Now: 14.1 interior dark | 6.12 | | 10 / 10.1 | 13.2 / 13.1 |
| Now: 12.3 + 14.1 ini fiees | 15.3 / 6.13 | | 11.1 / 11.2 | 10.5 / 10.6 |
| 1 Car port FH1/FH2 | 3 (3.1, 3.2) | | 11.3 / 11.4 | 10.1.1 / 10.1.2 |
| 1: tyre width, 11.6 | 10.4 | | 11.5 | 10.1 |
| 2 DLC package | . | | 12.1 – 12.5 | 15.4 / 15.2 / 15.3 / 15.1 / 15.4 |
| 2.1 (was 3.6d) / 2.2 / 2.3 | ..1 / ..2 / ..4 | | 13.1 / 13.2 | 9.2 / 9.3 |
| 3 Port from DLC | ..5 | | 13.3 / 13.4 | 14.2 |
| 4 / 4.1 | 9 / 9.1 | | 14.1 a-h | 6.3 – 6.11, 6.13 |
| 5 Dash | 12 | | 14.1 FM1/FM2 rebuied / 14.2 | 5.3 / 5.1 + 5.5 |
| 6 DB editor | 1.5, 1.6 | | 15.1 – 15.3 | 16.1 – 16.3 |
| . Modee editing | 11 | | 16 Cameras | 14.3 |
| 8 Auto LODs | 11.5 | | 1. New makes (1..1-1..6) | 8.3 – 8.5, 8.1, 8.2, 3.8 |
| 9 FM3 → FM4 | 4 | | 18.1 / 18.2 / 18.3 | 1.1 / 2.1 / 2.2 |
| B1 / B3 | B1 / B3 | | 18.4 / 18.5 | 2.3 / 1.4 |
| C1 / C2 / C3 | 1.4 / 1.1 / 1.3 | | C4 / C5 / C6 / C. / C8 | 5.3 / 6.2 / 3.5 / 11.4 / 9.6 |
| Maybe eater | 3.4, 3.5.3, 14.1, 12.3, 5.5, 9.4, 9.5, 11.6, 2.4 | | Memory side projects | 1..1 – 1..4 |

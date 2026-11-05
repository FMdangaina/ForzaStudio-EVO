# Pre-release testing (FM4 first) — state 2026-11-05, resume here

Sessions of 2026-11-03/04/05: the user went through the `TODO.md` "NOW: FM4 first" list point by point, in the
editor and in game (`FM4_Extracted_DEV`, Xenia). FM3 and "every game to every game" (ROADMAP goal 18) are paused
until FM4 is done. Details of every point are in `TODO.md` section NOW; this file is the short version to restart from.

## Results

| # | Point | Result |
|---|---|---|
| 1 | Database view (Engines, Makes, Countries, Manufacturers, Events, Go to / Used by) | ✅ all OK (UNTESTED A1-A8 ticked) |
| 2 | Duplicate Car (engine, gearbox, aspiration, sound, Delete Car) | ✅ after the **manifold fix**; DLC car as source still missing (2b) |
| 3 | DLC packs (build report, makes, strings, install wizard) | ✅ with **Validate DLC for Installation**; Create DLC alone still depends on other packs (open) |
| 4 | New makes (tile, logo, Livery editor) | ✅; make titles not testable without long play |
| 5 | Dash fonts in FH ports | ✅ (LaFerrari); dash editor features stay experimental |
| 6 | Viewport tools (views, live cameras, MESH, UV mapper, collision) | ✅ + sub-object levels, wireframe, red selection |
| 7 | Materials / viewport (reflections, colour, highlight, material check, indicators, Settings ALL) | ✅ all OK |
| 8 | FH1 / FH2 -> FM4 (damage with an odd donor, sound, tyres, black materials) | ✅ (S65 AMG with donor 500 Abarth); 8.e languages ✅ 2026-11-05 |
| 9 | FM2 / FM1 close-up (5.3) | ⏸ postponed by the user (FM2 resumes after the release) |
| 10 | Offroad / small tyres, generic brakes | ✅ solved with the Raptor tyres, tested on the Bowler (user) |
| 11 | Checks 1.3 / 1.4, own engine from scratch 2.3.2, camera review 14.0 | ✅ 2026-11-05 |
| 12 | Release 0.0.1 | 🔧 tag v0.0.1 + GitHub draft release; left: 7.4 New Folder and the zip seen by the user, then publish |

## 2026-11-03/04 (commit f84a191)

- `CarEngine.ConvertManifold`: a copy made natural gets FM4's stock `List_UpgradeEngineManifold` row (all 337 natural
  engines of FM4 have one, none of the 168 turbo / supercharged), a copy made turbo loses it. Fixed the Supra SZ;
  checked in game by the user.
- `EngineDialog`: Excel paste removed, the curve generator stays (user).
- `CarDuplicate`: a car of a DLC pack as source gives a clear "not supported yet" message (2b).
- The Xbox 360 package folder is no longer shared between the FM3 and FM4 windows (`CarPort.XboxOut.FM3`).
- `SelectionRegion.cs`, `SubObject.cs`: selection regions and 3ds Max sub-object levels in the MESH tab and the UV
  mapper; MESH draws wireframe and a red selection. User: "tutto perfetto, come lo volevo io".
- `PortPairs.cs`: the FM4 -> FM3 route switched off (FM3 suspended).

## 2026-11-05 (commits b5ab4c8, 3818047, tag v0.0.1)

- 8.e: the language a pack lacked was LOC (developers' pseudo locale; FH1 / FH2 LOC `Data_Car.str` lacks the newer
  cars). `CarPort.PortStrings` falls back to the source's EN text; trial S65 AMG: 18 languages. Official packs ship 16.
- 1.3: `DbIds.CheckCar` also requires `car * 1000 + n` keys where every other car follows that pattern
  (`docs/DATABASE_RULES.md` rule 5); clean DB, DEV DB and the user's packs pass. 1.4 SQL guard confirmed.
- 14.0: cockpit camera checked in game (NSX '97 +10 cm like the preview). Exterior camera previews switched off: the
  game adds the `CameraOverrides` offsets to a base point of its own, not found in any file (user: drop them). Untouched
  camera fields keep their full value when saved.
- Release: version 0.0.1 (title, About, ini), Release build, public zip (own exe + MIT / ISC libraries) and a private
  FULL zip in `dist\`; the files users bring are listed in `docs/INSTRUCTIONS.txt`. Docs reviewed.
- Harnesses: `E:\DEV\fm4dev\PackStr` (lang, zl, ze), `E:\DEV\fm4dev\DbIds\check1105` (c3test, allcars, packcars,
  ordpat, hoodfit, headrec, camread).

## Still open, not for the release

- 2b Duplicate Car of a DLC car (rows, strings, sound, thumbnail live in the pack).
- Create DLC without the wizard: reuse a make only from the base game or an installed pack, strings in
  `Benchmark.str` from the start (`docs/BENCHMARK_STRING_TABLE.md`).
- 4.1 make titles in game (long play).
- 9 FM2 / FM1 close-up; dash editor 12.3 - 12.9 experimental; thumbnail camera not checked in game.
- FM3 and every game to every game: paused (`docs/PORT_ALL_GAMES.md`, `docs/FM3/fm3_conversion.md` section 9).

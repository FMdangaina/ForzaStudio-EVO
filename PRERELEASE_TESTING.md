# Pre-release testing (FM4 first) — state 2026-11-04, resume here

Session of 2026-11-03/04: the user went through the `TODO.md` "NOW: FM4 first" list point by point, in the editor
and in game (`FM4_Extracted_DEV`, Xenia). FM3 and "every game to every game" (ROADMAP goal 18) are paused until
FM4 is done. Details of every point are in `TODO.md` section NOW; this file is the short version to restart from.

## Results of the session

| # | Point | Result |
|---|---|---|
| 1 | Database view (Engines, Makes, Countries, Manufacturers, Events, Go to / Used by) | ✅ all OK (UNTESTED A1-A8 ticked) |
| 2 | Duplicate Car (engine, gearbox, aspiration, sound, Delete Car) | ✅ after the **manifold fix** (see below); DLC car as source still missing (2b) |
| 3 | DLC packs (build report, makes, strings, install wizard) | ✅ with **Validate DLC for Installation**; Create DLC alone still depends on other packs (open) |
| 4 | New makes (tile, logo, Livery editor) | ✅; make titles not testable without long play |
| 5 | Dash fonts in FH ports | ✅ (LaFerrari); dash editor features stay experimental |
| 6 | Viewport tools (views, live cameras, MESH, UV mapper, collision) | ✅ + new sub-object levels, wireframe, red selection (see below) |
| 7 | Materials / viewport (reflections, colour, highlight, material check, indicators, Settings ALL) | ✅ all OK |
| 8 | FH1 / FH2 -> FM4 (damage with an odd donor, sound, tyres, black materials) | ✅ (S65 AMG with donor 500 Abarth); 8.e languages open |
| 9 | FM2 / FM1 close-up (5.3) | ⬜ left to do (development, not a test) |
| 10 | Offroad / small tyres, generic brakes | ✅ solved with the Raptor tyres, tested on the Bowler (user) |
| 11 | Checks 1.3 / 1.4, own engine from scratch 2.3.2, camera review 14.0 | ⬜ left to do |
| 12 | Release 0.0.1 | ⬜ next, after the commit |

## Code changed tonight (built in `bin\Debug`, committed 2026-11-04, not pushed)

- `CarEngine.ConvertManifold`: a copy made natural gets FM4's stock `List_UpgradeEngineManifold` row (all 337 natural
  engines of FM4 have one, none of the 168 turbo / supercharged), a copy made turbo loses it. Fixed the Supra SZ
  (F 100 after buying and selling a turbo, "standard aspiration" missing). Checked in game by the user.
- `EngineDialog`: Excel paste (Ctrl+V of rpm / N.m) removed, the curve generator stays (user).
- `CarDuplicate`: a car of a DLC pack as source gives a clear "not supported yet" message (2b).
- `CarPortForm`, `DuplicateCarForm`, `CarPortFm3Form`: the Xbox 360 package folder is no longer shared between the
  FM3 and FM4 windows (`CarPort.XboxOut.FM3`); an FM4 window ignores a folder of title 4D53084D (FM3). A pack had
  gone into the FM3 folder.
- `SelectionRegion.cs` (new): rectangle / circle / fence / lasso / paint regions, flyout in the MESH tool bar and
  the UV mapper.
- `SubObject.cs` (new): 3ds Max sub-object levels Vertex / Edge / Face / Polygon / Element, teal icons, in the MESH
  tool bar and the UV mapper (keys 1-5 there). User: "tutto perfetto, come lo volevo io".
- MESH tab: wireframe on while the tab is open (back as it was when leaving), selection red; selected faces filled
  red translucent (`RenderScene.OverlayFaces`, `DrawFaces`); checked on screen by the user.
- `PortPairs.cs`: FM3 routes switched off (FM3 suspended; the change was already in the tree, committed with the rest).
- Docs: `ROADMAP.md` (goals 18-20, goal 18 paused), `TODO.md` (NOW list and results), `UNTESTED.md` (A, F, G ticked).
- Harnesses outside the repo: `E:\DEV\fm4dev\Engine\manifold\m.exe`, `E:\DEV\fm4dev\Engine\region\r.exe` and `s.exe`.

## Tomorrow, in this order

1. ✅ 2026-11-05 **8.e** fixed (uncommitted): the missing language was LOC (developers' pseudo locale; FH1/FH2
   LOC Data_Car.str lacks the newer cars, so the port left LOC unchanged). `CarPort.PortStrings` now falls back to
   the source's EN text for a key a source language lacks; trial port S65 AMG: 18 languages. Official packs ship
   16 (no DEV/LOC), so Punto 4/8 as built are harmless. Was: the "Punto 8" build wrote its string tables in **17 languages**, "Punto 3" in 18.
   Probably one installed layer (September Pennzoil or "Punto 4") lacks a language and the table built on it loses
   it. Find which language and why, then the build must keep all 18 (fall back to the game's table of that language).
2. **9 FM2 / FM1 -> FM4 close-up** (5.3): rebuilt close-up model for FM1 / FM2 cars (development).
3. ✅ 2026-11-05 **11** closed: 1.3 (+ new car * 1000 + n rule), 1.4, 2.3.2 (already closed), 14.0 (cockpit camera in game, NSX +10 cm; exterior previews switched off: the game's base point for the hood / bumper / chase offsets is unknown, user: drop them). Point 9 (FM1/FM2 close-up) postponed by the user: FM2 resumes later. Was: checks 1.3 / 1.4 (database rules, SQL console), own engine from scratch (2.3.2), camera review (14.0).
4. Then what comes next: release 0.0.1 (ROADMAP goal 20, `TODO.md` section 0): version number in the program and the
   ini, 7.4 New Folder seen in the interface, Release build, clean zip, push when the user says.

## Still open, not for the release

- 2b Duplicate Car of a DLC car (rows, strings, sound, thumbnail live in the pack).
- Create DLC without the wizard: reuse a make only from the base game or an installed pack, strings in
  `Benchmark.str` from the start (`docs/BENCHMARK_STRING_TABLE.md`).
- 4.1 make titles in game (long play).
- 9 FM2 / FM1 close-up (5.3); 11 checks 1.3 / 1.4, 2.3.2, 14.0; dash editor 12.3 - 12.9 experimental.
- FM3 and every game to every game: paused (`docs/PORT_ALL_GAMES.md`, `docs/FM3/fm3_conversion.md` section 9).

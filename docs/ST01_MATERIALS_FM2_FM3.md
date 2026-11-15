# ST-01: material editing for FM2 and FM3 (2026-11-13)

Short-term sprint item ST-01. State: **coded, automatic tests passed, name-only rename checked in game on FM2 and FM3
(user, 2026-11-14); opaque -> see-through rename not tried in game yet.**

In game (user, 2026-11-14, "ha funzionato egregiamente"): FM2 `for_mustanggt_05` (FM2_Extracted_DEV) left mirror
`body -> chrome`; FM3 Fiat 500 Abarth (FM3_Extracted_DEV) front bumper `-> chrome`.

## What a material is in these games

There are no separate material files. A material is:

1. the **material name of each mesh in the .carbin**, which is also the name of the shader technique; the textures come
   from the game's shaders and `textures.xml`, the .carbin holds no texture link;
2. the **car's own shader parameters** in `shadersettings.xml` inside the car zip (entry = technique, `cockpit_` +
   material for a cockpit model).

A material is also either **see-through** or **opaque**, and that is written in the model twice: in which draw pass the
mesh is (draw lists: first half see-through, second half opaque) and in a flag on its part. A see-through mesh in the
opaque pass, or in a part without the flag, is not drawn in FM4 (FIA_595_68, 2026-11-08).

## See-through rule of FM2 and FM3 (E:\DEV\fm4dev\ST01\passscan.exe, every stock car and wheel model)

| | Files | Materials always in the see-through pass, never in the opaque one | Part flag | Exceptions |
|---|---|---|---|---|
| FM2 | 718 | window, window_2, glass, red_glass, detail_glass_clear / red / amber, blur_rim, chrome_blur_rim | `HasTransparentMeshes`: 1 on the 4272 parts with such a mesh, 0 on the 6720 without. It also picks the shared vertex buffer of the part (first = see-through) | 0 |
| FM3 | 3810 | the same without window_2 | first byte of the word `CarbinModel` calls `VbTest1` (FM3 has no `Pre` word as FM4): 01 on the 9583 parts with such a mesh, 0 on the 18070 without | 0 |

After a rename (`MaterialSlots.ToBytes`) every mesh goes to the pass of its material (appended to the same LOD list of
the other pass; meshes already right keep their order), every part flag follows its meshes, and in FM2 a part whose flag
changed has its vertices moved to the other shared buffer (`Fm2CarbinModel.FixSeeThrough` / `RepackBuffers`; mesh
indices count from the part's first vertex, so they do not change). FM4 / FH files keep `CarbinModel.ToBytes`'s own FM4
fixes, which skip FM3 files.

## What changed

| | Before | Now |
|---|---|---|
| Rename Material, FM2 | refused ("this game's models cannot be edited") | works: names, draw pass, part flag and shared buffer (`Fm2CarbinModel`) |
| Rename Material, FM3 | refused | works: names, draw pass, part flag (`CarbinModel(data, true)`) |
| Rename dialog choices, FM2 / FM3 | FM4 list | the game's own list: what its cars give the part (green, with the number of cars), then every material its cars use in that kind of file and LOD |
| Materials no car of the game uses | red in the tree, magenta in the viewport (`MaterialUse.txt` per game, already there) | unchanged; the label now says "click to fix" on FM2 / FM3 too |
| `shadersettings.xml` blocks the parser does not understand | dropped on every write | kept as they are, written back in place (`ShaderSettings.Unparsed`) |
| FM2 `for_mustanggt_05` | did not open (IndexOutOfRange) | opens: 5 FM2 meshes are triangle lists (primitive 4), the viewer read them as strips (`ForzaCarSection`) |
| FM2 wheel `por_550_56` | did not open | opens: ModelSettings 713 (not 714) and 64-byte vertices (position, UVs, normal at the 60-byte offsets) (`ForzaCarBin`, `ForzaVertex`) |

Code: `MaterialSlots.cs` (new), `Fm2CarbinModel.cs` (see-through rule, `RepackBuffers`), `ArchiveEditorForm.MaterialCheck.cs`,
`ArchiveEditorForm.PartEdit.cs` (PART tab's Rename Material button on FM2 / FM3 meshes; other part edits stay off),
`MaterialSuggestions.ForGame`, `ShaderProfiles.PartMaterials`, `ShaderSettings.cs`, `ForzaCarSection.cs`, `ForzaCarBin.cs`,
`ForzaVertex.cs`. Data: `docs/FM2/PartMaterials.txt`, `docs/FM3/PartMaterials.txt` (embedded), `docs/FM2/SsUse.txt`,
`docs/FM3/SsUse.txt` (not used by the program), made by `E:\DEV\fm4dev\ST01\survey.exe` on the stock cars (no DLC).

## Survey of the stock cars

| | Cars | With shadersettings.xml | Techniques used by the models | Techniques with an entry somewhere |
|---|---|---|---|---|
| FM2 | 309 | 116 | 44 | 17 |
| FM3 | 311 | 246 | 145 | 47 |

## Automatic tests (E:\DEV\fm4dev\ST01)

- `rt.exe`: every car, wheel and brake .carbin in a zip: tree order = writer order, written back byte for byte, a
  renamed material changes the name bytes only. **FM2 718 / 718, FM3 3810 / 3810.** (Three FM2 wheels of a later
  form are not FM2 files for the readers and stay unread: `5zigen_hyper_5zr_02`, `enkei_rs5`, `mht_crossover_wheel`.)
- `repack.exe`: `RepackBuffers` with the file's own flags gives the file back: **FM2 718 / 718**.
- `seethru.exe`: stock files unchanged by the fixes; an opaque mesh renamed `glass` goes to the see-through pass, its
  part gets the flag, every vertex position stays (FM2: the part moved buffer); renamed back, everything returns.
  **FM2 309 / 309, FM3 3421 / 3421 models.**
- `st01.exe`: on a copy of every car zip, the app's code renames the first mesh's material to the first choice of the
  dialog's list, adds a `shadersettings.xml` entry, saves, reopens: every rename kept, every other zip entry identical
  (FM2 3902, FM3 9416), non-carbin bytes refused. 9 FM2 and 5 FM3 renames cross see-through / opaque (e.g.
  `detail_glass_amber -> tail_light`): there the pass and flag move as they should (seethru), the rest only the name.
- `sscheck.exe`: every `shadersettings.xml` parsed and written: **FM2 116 / 116, FM3 246 / 246** keep all their text;
  only white space changes (the program always wrote FM4's layout).

## Open

- In game: an opaque mesh renamed to a see-through material (e.g. Mustang `mirrorR body -> glass`): draw pass, part flag
  and, in FM2, the shared buffer move. Checked by the automatic tests only.
- The lists of "materials of the game" come from the stock cars only (no DLC cars).
- FM4 (not ST-01, as before): 2 of 256 `shadersettings.xml` lose text inside an entry when written: `EAG_Talon_98` (a stray
  `&gt;`), `GMC_Syclone_92` (a `<reverse_light_lod0>` block nested inside `<tail_light>`: its `PaintScale` lands in
  `tail_light`).

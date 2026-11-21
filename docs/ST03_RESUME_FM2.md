# ST-03 resume: model editing inside FM2 and FM3 cars (state 2026-11-21, late)

Hand-off for the next chat. Reply to the user in Italian; code, docs and commit messages in English.
Findings, numbers and the day-by-day history are in `docs/ST03_PART_IMPORT.md`; the user's guide is `docs/MANUAL.md`
section 5 "FM2 and FM3 cars". This file is the state and the commit list only.

## State

- **Everything below is built and was checked by the user in the app and in game on 2026-11-21.**
- Build in `bin\Debug`: 2026-11-21 14:36, 0 errors.
- **Nothing committed, nothing pushed** since `175af91`. The user's plan: review of the documents (done 2026-11-21),
  then the commit of this chat's files, maybe a release. Commit only when the user says so.

## What works, and who checked it

| Feature | FM4 | FM3 | FM2 | User check |
|---|---|---|---|---|
| Copy Part from a car of any game | yes | yes | yes (main models only) | OK in game, 2026-11-18 / 21 |
| PART tab: move, rotate, scale, delete, duplicate, rename | yes | yes | yes | OK in game, 2026-11-18 |
| Tree: drag a mesh, Attach, Merge Part, Duplicate Mesh | yes | yes | yes | OK in game, 2026-11-21 |
| MESH tab: picking, moves, Delete Faces, Detach Faces | yes | yes (lod0 and main) | yes | OK in the editor, 2026-11-21 |
| UV Mapper, whole meshes and picked faces | yes | yes | yes (UV1, within +-25) | OK, 2026-11-21 |
| Import / Export Model, OBJ folder | yes | yes | yes | OK in app and game, 2026-11-21 |
| Import / Export Model, FBX and glTF | yes | yes | yes | OK, 2026-11-21 |
| PART tab Export OBJ / Import OBJ (one part) | yes | yes | yes (LODs the part has, no Fit UVs) | OK, 2026-11-21 |
| Baked light: FM2 computed; FM2 -> FM4 part and port keep the FM2 vertices' values | yes | yes | yes | OK in game, 2026-11-21 |

Asked by the user on 2026-11-21 and done: Delete Faces asks no question any more (Undo takes it back).

## Decisions of the user (2026-11-21)

- As little as possible from a donor; in the end none. Light is done. **Crash deformation (damage words) without a
  donor: later**, not started.
- **FM2 has no LOD 0 and no per-car brake models.** Nothing of LOD 0 goes into an FM2 car; its LODs 1-5 stay as they
  are. FM2 features do "the same job as on FM4, taking the missing LOD 0 into account".
- **Copy Part stays like for like on every game:** lod0 -> lod0, cockpit -> cockpit, LODs 1-5 -> LODs 1-5. The idea
  "lod0 / cockpit / brake part into the main model, LOD 0 as LOD 1 and LODs 2-5 by reduction" is CANCELLED. Do not
  propose it again. (A main-model part into a close-up file, its LOD 1 used as LOD 0, exists since 2026-11-17.)
- Prefer source data, then computed, donor last (FM2 -> FM4 / FM3 light = the FM2 vertices' own values x 1.457).

## Commit list

This chat's files, whole file:

- New: `ForzaStudio/CarbinModelLighting.cs`, `Fm2CarbinModelEdit.cs`, `Fm2CarbinModelLighting.cs`,
  `Fm2CarbinModelMesh.cs`, `Fm2CarbinModelObj.cs`, `Fm2CarbinModelUv.cs`, `PartImport.Motorsport2.cs`,
  `PartObjImport.cs`, `docs/ST03_RESUME_FM2.md`.
- Changed: `ForzaStudio/ArchiveEditorForm.AttachMesh.cs`, `.MeshEdit.cs`, `.MeshGizmo.cs`, `.ModelFolder.cs`,
  `.MoveMesh.cs`, `.PartEdit.cs`, `.PartImport.cs`, `.PickMode.cs`, `.UvMapperOpen.cs`, `CarModelFbx.cs`,
  `CarModelFolder.cs`, `CarbinMeshEdit.cs`, `CarbinModel.MoveVertices.cs`, `CarbinModelMotorsport3Edit.cs`,
  `Fm2CarbinModel.cs`, `Fm2Source.cs`, `ForzaCarSection.cs`, `ForzaVertex.cs`, `PartImport.cs`, `PartSource.cs`,
  `UvSession.cs`, `ViewportHelp.cs`, `docs/ST03_PART_IMPORT.md`, `docs/MANUAL.md`, `docs/PORT_ALL_GAMES.md`,
  `ROADMAP.md` (the two ST-03 lines).

This chat's part of a shared file:

- `ForzaStudio/CarbinModel.cs`: ONE hunk, in `RemoveSubSectionsAt` (the comment "FM3 (its own layout): only vertices
  went"). The other three hunks are another chat's (RemoveFromShadow fix, reviewed here = correct; a suggested
  fallback `own.RemoveFromShadow` before `false` at the template copy was not applied, ask the user).
- `ForzaStudioEVO.csproj`: 8 lines, the `Compile Include` of the eight new `.cs` files above.

NOT this chat (never commit with ours): `AppIni.cs`, `ArchiveEditorForm.ModPacks.cs`, `ArchiveEditorForm.TopBar.cs`,
`CarPort.cs`, `CarPort.Database.cs`, `CarPort.Zips.cs`, `CarPortForm.cs`, `CarPortToFm3.cs`, `Fm3Perf.cs`,
`Fm4Perf.cs`, `ModPack.cs`, `PortSettings.cs`, `SettingsForm.cs`, and their csproj lines.

Method for the partial commit: `git hash-object -w` + `git update-index --cacheinfo` for the shared files (csproj =
`git show HEAD:` plus our 8 lines; `CarbinModel.cs` = HEAD plus our hunk), then build the staged tree in a temp folder
(`git checkout-index -a --prefix=`) before committing. Never push.

## Harnesses (C#, read-only on the game folders)

Build: copy a fresh `bin\Debug\ForzaStudioEVO.exe` into the folder, then
`csc -optimize -platform:x86 -r:ForzaStudioEVO.exe -r:<GAC_32 XNA 3.1 dll> -r:System.Core.dll`; the config is a copy
of `study.exe.config` as `<name>.exe.config`. Write sources with the Write tool (bash heredocs break on C# quotes).

| Harness | What | Last result |
|---|---|---|
| `ST03\fm2t\pi5.exe` | Copy Part into FM2 from every game + part edits | FM2 110 / 0, FM4 / FM3 / FH1 / FH2 30 / 0 |
| `ST03\fm3\pi3.exe` | Copy Part FM3 / FH -> FM4 | 150 / 0 (UV check allows the mesh's 16-bit grid) |
| `ST03\fm3t\mm3.exe` | FM3 mesh moves in the tree | OK |
| `ST03\uvt\uvt.exe` | UV Mapper session | FM2 80 / 0, FM3 330 / 0, FM4 261 / 0 |
| `ST03\uvt\mesht.exe` | MESH tab edits | FM2 100 / 0, FM3 330 / 0 (identical bytes) |
| `ST03\uvt\objt.exe` | Import Model from OBJ folder | FM2 36 / 0, FM3 32 / 0 |
| `ST03\uvt\fbxt.exe` | Import / Export Model as FBX and glTF | FM2 40 / 0, FM3 48 / 0 |
| `ST03\uvt\partobjt.exe` | PART tab Import OBJ | FM2 32 / 0, FM3 20 / 0 |
| `ST03\light34\new\l4t.exe`, `ST03\fm2t\lapp.exe` | baked light FM3 / FM4, FM2 | see ST03_PART_IMPORT.md |
| `ST03\uvt\headprobe.exe` | FM4 header readers on another game's files | FM3: gauges 0 / 40, exhaust 0 / 40 |

All paths under `E:\DEV\fm4dev`.

## Key facts (measured, details in ST03_PART_IMPORT.md)

- FM2 light: 16 values per vertex = a D3DX PRT transfer vector, stored = 2 x T. Computed with the car as occluder,
  light sent back (rho) and a black ground, both fitted per car. FM3 / FM4: 4 SNORM16 = the first 4 of FM2's 16 x 1.457.
- FM2 UV: one set, offset binary words, FM4 UV = (raw - 32768) / 1310.72, range +-25; FM4 UV1 = - FM2 UV0;
  carbon_fiber FM4 = FM2 x 250. Whole-number shifts when a copied part's UVs do not fit.
- FM3 UV: the vertex holds the UV itself as halves, no per-mesh range. FM4: 16 bits over the mesh's range, so a wide
  range has a coarse grid (the game's own files too).
- FM2 bounds: origin = box centre, min = -max; shared buffers of at most 65536 vertices, u16 indices.
- FM3 edits: the file stays in its own layout; a removal keeps the part's box (vertices keep every byte).
- Stray vertices near -1.7e38 exist in game files: keep the |coord| < 20 filter and the 200-step ray cap in the light.

## Open, for later (none blocks the commit)

- Crash deformation words of new vertices: still from the nearest vertex the part had / the target car. The user's next
  no-donor target, "in futuro".
- Gauges tab on FM3: the FM3 header layout of the gauges is not decoded. Driver and Exhaust tabs, Make Rim Scalable and
  Check Model Format are FM4 features by their data.
- PART tab Import OBJ on FM4 still uses the open-panel light estimate (the FM2 / FM3 paths and Import Model compute it).
- FBX / glTF on FM2 / FM3: the user's check was "OK" without saying which 3D program; textures in an FBX were never
  checked in 3ds Max (old note).
- Whole-car ports FH1 / FH2 -> FM3, FM2 -> FM3, anything -> FM2: not started, they belong to the port chat
  (`docs/PORT_ALL_GAMES.md`, state note of 2026-11-21 with the estimate).

## Rules (user)

- Reply in Italian. Commit only when asked, never push, never other chats' hunks.
- UV1 / UV2 are never touched, except the agreed FM2 rules (whole-number shift, carbon / 250).
- Describe new UI in 2-3 lines and get a yes first. No question boxes on routine edits (Undo exists).
- Check `tasklist` for ForzaStudio before building into `bin\Debug` (if open: build into the scratchpad, never kill)
  and for xenia before writing to a `*_DEV` game folder. Never write to the non-DEV game folders.
- Harnesses are C#, in `E:\DEV\fm4dev\<topic>`; no Python, no long PowerShell. Ask "10 random or all?" before a full run.
- Before saying a feature works on FM2 / FM3, check every entry point of it for the FM4-only reader
  (`new CarbinModel(data)`).
- No screen capture, no mouse automation. Odd behaviour goes into the F1 help.

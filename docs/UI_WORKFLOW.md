# Editor workflow (TODO 15.3)

User 2026-11-16, after testing Attach / Merge pick modes, Undo / Redo and the move without Apply ("funziona"):
"le interfacce sono veramente difficili da usare ... non è una questione di posizioni, workflow macchinoso e poco
efficiente". Sprint 2 ST-03 (`docs/ST03_PART_IMPORT.md`) is PAUSED for this (user: "fermiamo st per questo").

The user named all four as time sinks: copying parts from another car, moving / scaling parts, confirmations and
windows, materials and textures. Also wanted: 3ds Max shortcuts, the top menus gone, a visible pick-mode banner.

## Rules

- No confirmation or preview window where Ctrl+Z undoes the action. The report goes to the status bar (and
  `PartImport.log` for imports); errors that change nothing still show a message.
- Moving a copied part still ASKS on which LODs (user rule from ST-03), but as part of the same step, not a chain.
- Keys are written on the buttons and in the right-click menu. Ctrl / Alt keys never move the viewport camera.
- One step at a time; the user checks each in the app before the next.

## How the UI is checked here

`E:\DEV\fm4dev\ui\shot.exe <car.zip> <outdir> [part]` opens the editor off-screen (opacity 0, at -4000,-4000),
loads the car's main `.carbin`, selects the part and saves `form.png` and `tab_N_<name>.png` with `DrawToBitmap`.
No screen capture. Copy the fresh `bin\Debug\ForzaStudioEVO.exe` next to it first.

## Screens (2026-11-16, after A.3)

Off-screen renders made with the tool above: the 3D view is empty in them (`DrawToBitmap` does not see it).

The editor with a model open: the archive's file list is hidden (`Files` over the model tree brings it back), the
PART tab starts with the selection's path and its red headings open and close.

![The editor with a model open](img/workflow_editor.png)

Attach on: the thin red strip over the view names SOURCE and TARGET, the button reads `Attach: ON`.

![Attach pick mode](img/workflow_attach.png)

The PART tab alone: actions first, IMPORT / EXPORT PART closed, the selection's details at the bottom.

![The PART tab](img/workflow_part_tab.png)

## Steps

### A. Fewer windows, keys, pick banner — coded 2026-11-16, app test pending

| Before | Now |
|---|---|
| Delete: Yes / No window | deleted at once, status says "Ctrl+Z: undo" |
| Duplicate: name window, then OK | copy gets the next free variant name (exhausta -> exhaustb, `SuggestPartName`) and its name opens for editing in the tree |
| Copy Part From Another Car: car -> parts -> LODs -> text preview -> Apply | the text preview is gone (applied, warnings counted in the status bar, log in `PartImport.log`) |
| Fill missing LOD0 parts: list -> preview -> Apply | list -> applied |
| PART tab 1232 px high (885 visible): Attach / Merge / import below the fold, four paragraphs of notes | 871 px: actions first (Attach, Merge, Duplicate, Delete, Rename, Focus, Solo), notes moved to tooltips, "Also in lod0 and cockpit" + Reset on one row |
| pick mode only shown by the red button and the status bar | red banner over the viewport says what the next click does; a click on it stops the mode |

Keys (`HandlePartKey` in `ArchiveEditorForm.PickMode.cs`, model page, not while typing in a box):
Alt+A Attach, Alt+M Merge, Ctrl+D Duplicate, Alt+Q Solo; with the model tree or the viewport focused: Del Delete,
F2 Rename, Z Focus. Esc ends a pick mode. W A S D / Q E camera moves are ignored while Ctrl or Alt is held.

#### A, second round (user feedback 2026-11-16 night) — coded + built, app test pending

- The face tools (vertex / edge / face / polygon / element, selection region, select all / none / linked, delete,
  detach) moved from the bar over the view into the MESH tab (`BuildMeshToolStrip`, two rows); UV Mapper stays in the bar.
- Attach failed with two meshes of the same name in one part ("2 chrome in un nodo"): the pick mode found nodes by
  tree path, so always the first. Now `PickKey` = path + rank among same-named siblings.
- Attach / Merge go OFF after one join ("quando finisco di attaccare spengo tool"); Esc / right-click stop before.
- The preview windows of Copy Part and Fill LOD0 are BACK ("anteprima andava bene, ma sarà da reworkare": step C).
- PART tab: the SELECTED info block moved to the bottom ("le info meglio al fondo").
- Part keys (Alt+A, Alt+M, Ctrl+D, Alt+Q, Del, F2, Z) only with the PART tab open ("alt-a ed alt-m non devono
  funzionare sempre"); on MESH, Delete deletes faces as before. The tree's own Del / F2 are unchanged.

#### A, third round (user test of round 2, 2026-11-16) — coded + built, app test pending

Round 2 result: face tools in MESH "molto meglio", Attach "molto meglio ma non proprio intuitivo", off after one
join OK, PART tab OK, keys OK, Delete / Duplicate OK. The Copy Part / Fill LOD0 preview: the user wants to rework
it ("questa la vorrei rivedere") — what exactly is still to be asked.

- Crash fixed: MESH tab, then ticking another mesh and clicking = NullReferenceException in `MeshEditOverlayLines`
  (`EnsurePickData` cleared `faceLines` while it was being filled). The lists are now built locally.
- Vertex level: a blue dot on every point of the ticked meshes, the selected ones red, larger, always on top
  (`BuildVertexDots`; `RenderScene.OverlayPoints` / `OverlayPointsOnTop`, point list with a fixed pixel size).
  The red crosses are gone.
- Attach / Merge strip: 20 px instead of 30, text `ATTACH   SOURCE: part / mesh   →   TARGET: ...   Esc: stop`;
  the valid destination under the pointer turns green in the tree and is named as TARGET (`PickHover`).
- Save of a changed game car asks once per car (until the editor closes): MOD PACK or OVERWRITE the game's file
  (`ChooseSaveTarget`, `AskSaveTarget`, `OverwriteGameCar` in `ArchiveEditorForm.PackageCars.cs`). Asked for a
  DVD1 car opened from the database (copy) or as the zip itself (mod pack = the save goes to a copy, the game's
  zip stays). Not asked for original DLC cars (mod pack only) nor when the copy carries its database.

Third round checked by the user in the app 2026-11-16: crash, vertex dots, Attach strip, Save question all OK.

RESUME HERE: the user tests A.3 in the app (below), then step B. Fill LOD0 preview is fine as it is (its database rows: later); Copy Part preview left for step C. Commit still to ask. Old note: A.3
focus/responsiveness pass and test it before step B.

### UX direction after round 2

The remaining problem is not just where controls are placed. The editor exposes the archive, model hierarchy,
viewport, transforms, import/export and selection information at the same time. Common operations make the user
move repeatedly between distant areas and remember which selection or mode is active.

The target workflow is viewport-first:

`select a part -> W / E / R -> drag the handle -> Ctrl+S`

- After opening a `.carbin`, collapse or hide the archive tree automatically. Keep a visible button to bring it
  back; give the model tree and viewport the working space.
- Select a part both from the model tree and directly on the car in the viewport.
- Use 3ds Max-style `W` Move, `E` Rotate and `R` Scale tools with X / Y / Z handles. Double-click or `Z` focuses
  the selected part.
- Make the right panel contextual: show tools for the current selection instead of all available operations.
- Keep the selection header visible at the top (part, LOD, mesh/material). Use collapsible `Transform`,
  `Part Operations`, `Import / Export` and `Info` sections; only `Transform` is open by default.
- Make the inspector slightly wider so X / Y / Z values are not cramped. Keep Attach, Merge, Duplicate and Delete
  in one compact action strip. Technical details and material lists stay collapsed until requested.
- In step C, show the donor car in the viewport and drag its parts to the target car. Dialogs remain only for a
  choice that cannot be inferred, such as the target LODs. Reversible actions report to the status bar.

#### Responsiveness

Part numeric fields currently call `UpdatePartPreview()` on every `ValueChanged`; that walks the model tree and
forces `scene.Update(true)`. A drag handle would amplify this cost. Before or as part of step B:

- cache the meshes belonging to the selected part instead of finding them again for every mouse movement;
- coalesce mouse/value changes into at most one viewport redraw per frame;
- let the viewport timer perform the redraw instead of forcing a synchronous full render for every mouse pixel;
- update labels and side panels only when their displayed values actually change.

When testing perceived slowness, record the action being performed, where the workflow caused hesitation, which
control was being searched for, and the exact status-bar text if an operation fails or stalls.

### A.3. Focus workspace and responsive preview — proposed after round 2 test

Before adding more always-visible controls, make the current workspace calmer and the live preview cheaper:

1. Auto-collapse/hide the archive tree after a model opens, with an obvious way to restore it.
2. Add the persistent selection header and collapsible contextual sections to the PART inspector.
3. Cache the selected part's mesh set and coalesce preview redraws.
4. Test this pass in the app before proceeding to the viewport handles.

Coded 2026-11-16 (compiles; app test pending):

- `Files` button, first in the view's bar: the archive tree hides by itself when a model opens
  (`ArchiveTreeFollowView`, called by `ShowView`); the button brings it back and its state is kept
  (ForzaStudio.ini `Editor.ArchiveTreeWithModel`).
- PART tab: first line = the selection's path (`LOD1 > bumperRo > black`, `UpdatePartHeader`); the red headings
  open / close their section with a click (`MakeCollapsible`, state kept as `Editor.Section<NAME>`);
  IMPORT / EXPORT PART starts closed.
- `UpdatePartPreview` keeps the part's meshes (`previewPart`) until the selection or Solo changes and only asks
  for a redraw (`viewRedrawWanted`); `modelTimer_Tick` draws once per tick.
- Not done: updating labels only when their values change (nothing found that needed it yet).

### B. Move / scale handles in the viewport — next

Select parts from the tree or viewport. Use `W` for Move, `E` for Rotate and `R` for Scale, then drag X / Y / Z
handles on the selected part (like 3ds Max). The number boxes stay for exact values; double-click or `Z` focuses.

### C. Donor car next to this one

Open another car in the same tree and viewport (ghosted); click its part, drag it onto the part it replaces or onto a
LOD for a new part. Replaces the car list + part table, where the part is chosen by name without seeing it.

### D. Materials and textures

Same review of the TEXTURE and SHADER tabs.

### E. Top menus become buttons

File / Archive / Model / Texture / Tools turn into visible buttons and keys.

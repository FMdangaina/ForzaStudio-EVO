# ForzaStudio EVO user manual (beta)

ForzaStudio EVO is a modding tool for the Xbox 360 Forza games. It works directly on the game's
files: you open a game folder or one of its `.zip` archives, change what you need, and save.

> **Version 0.0.4 (beta).** Some features are experimental or not fully tested in game (see
> [Known limitations](#13-known-limitations)). Always work on a copy of your game folder.

## Contents

1. [Supported games](#1-supported-games)
2. [Install](#2-install)
3. [The main window](#3-the-main-window)
4. [Saving, backups and undo](#4-saving-backups-and-undo)
5. [3D models (.carbin)](#5-3d-models-carbin)
6. [Textures (.xds)](#6-textures-xds)
7. [Adding files and new textures](#7-adding-files-and-new-textures)
8. [Text and XML files](#8-text-and-xml-files)
9. [Game database (gamedb.slt)](#9-game-database-gamedbslt)
10. [Digital dash (.fbf)](#10-digital-dash-fbf)
11. [Porting cars from Forza Horizon or Forza Motorsport 3 to FM4](#11-porting-cars-from-forza-horizon-or-forza-motorsport-3-to-fm4)
12. [Music](#12-music)
13. [Known limitations](#13-known-limitations)
14. [Other tools](#14-other-tools)
15. [Files ForzaStudio EVO writes](#15-files-forzastudio-evo-writes)
16. [Credits and license](#16-credits-and-license)

---

## 1. Supported games

| Game | What works |
|---|---|
| Forza Motorsport 4 | Everything: models, textures, XML, database, dash view, music. Target of car ports. |
| Forza Horizon 2 | Models, textures, XML, database, music. Source for car ports. |
| Forza Horizon (1) | Textures, XML, database, music. Source for car ports. |
| Forza Motorsport 3 | Models can be viewed (not edited), textures, database, music. Source for car ports. |
| Forza Motorsport 2 | Archives, textures, model viewing and export, music. Models cannot be imported back. Source for car ports. |

The game is detected from its `default.xex`. You need the game **extracted** to a folder, the one
that holds `default.xex` and `Media`.

## 2. Install

**Requirements**

- Windows 10 or 11.
- **.NET Framework 4.8**, already part of Windows 10 and 11.
- **Microsoft XNA Framework Redistributable 3.1**, used by the 3D view. If its installer asks for
  .NET Framework 3.5, enable it in Control Panel > Programs > *Turn Windows features on or off*.
- **Xenia Canary**, only to play ported cars as DLC packages.

**Files you bring yourself.** They belong to others and cannot be shared, so they are not in the zip.
Put them exactly here (paths from the folder of `ForzaStudioEVO.exe`):

| File | Where | Needed for |
|---|---|---|
| `xcompress.dll` (Xbox 360 SDK) | next to the exe | opening and saving the game's compressed zips: almost everything |
| `xds.dll` (Forza Studio 4.6 release) | next to the exe | textures (`.xds`): view, import, export, thumbnails |
| `xma2encode.exe` (Xbox 360 SDK, `bin\win32`) | `xma2encode\` | Add Song ([section 12](#12-music)) |
| `TireAOffroad.zip`, `TireBOffroad.zip`, `TireCOffroad.zip` | `Service\OffroadTires\` | the *Offroad* tire choice of the car port |
| `TireASmall.zip`, `TireBSmall.zip`, `TireCSmall.zip` (FM4's `TireA/B/CCIT_C1_11.zip` renamed) | `Service\SmallTires\` | the *Small* tire choice of the car port |

Without one of them, the features in its row do not work.

**Steps**

1. Unzip the whole ForzaStudio EVO folder somewhere you can write to, such as the Desktop. Avoid
   *Program Files*.
2. Copy the files above to their places.
3. Run `ForzaStudioEVO.exe`.

Keep every file of the zip next to the exe:

- `texconv.exe` builds textures.
- `vgmstream\` plays the music.
- `Be.Windows.Forms.HexBox.dll` is the hex view of the classic window.

The program is 32-bit by design.

You can also start it from a command line or a shortcut:

```
ForzaStudioEVO.exe <archive.zip | folder | file> [entry to select]
```

## 3. The main window

The menu bar holds:

- **File**: open, save, close.
- **Archive**: add, extract, replace, delete and revert files of the open folder or archive, and make new folders.
- **Model**: OBJ, FBX and glTF export and import of the model shown, and **Export UV Map to TGA...** (the UV
  layout of the materials you choose, from the car's main, lod0 and cockpit files, e.g. the digital
  dash screen to draw the dash picture on).
- **Texture**: new textures, image export and import of the texture shown.
- **Tools**, **Help** and **◀ Back**.

 The left side shows the tree of
the open folder or archive. The right side shows the selected file.

### Opening things

| Command | Shortcut | What it opens |
|---|---|---|
| File > Open Folder... | Ctrl+Shift+O | A game folder, or any folder inside it. The tree loads as you expand it. **Double-click a `.zip`** in the tree (or Enter, or Archive > Open Selected Archive) to open it. |
| File > Open Archive... | Ctrl+O | One `.zip`, a `gamedb.slt` or a single `.xds`. |
| File > Open DLC Package... | | An Xbox 360 DLC package file (`CON`/`LIVE`, the file named by its content id, e.g. `ED6D95A9…4D`). Open Archive opens it too. |

Opening a `gamedb.slt` directly opens the folder it is in, so links to other game files can be
followed.

### DLC packages

**File > Open DLC Package...** opens a marketplace package of any Forza game (FM4, FH1, FM3...).
Its files are extracted to a work folder (`%TEMP%\ForzaStudio\DlcEdit\<file name>`) and shown like a
game folder, with the package's name at the top of the tree:

- every file is shown with its viewer; **double-click a `.zip`** (cars, wheels, string tables,
  `<offer>_pri_N.zip`) to open it, **◀ Back** returns to the package;
- edit, replace, add or revert files as in any folder or zip;
- **Save** (Ctrl+S) writes the work folder **and packs the package file again**, in place, with
  its own header (title, names, licences, thumbnails); the hash tables and the content id are
  recomputed. The original file is copied to `bak\` the first time;
- **File > Rebuild DLC Package** packs it again by hand (for example after a failed save);
- **File > Delete Car from DLC Package...** takes one car of the pack out (rows, strings and files;
  the list shows what goes before anything is changed). Save or discard your edits first.

The package is read in place: only the files you open are unpacked, so a large pack does not fill the
temp folder.

The RSA signature is not renewed: the package works in Xenia and on a modded (RGH/JTAG) console,
not on a retail one. File names longer than 40 characters do not fit in a package: put such files
in a zip. A DLC already installed in Xenia (a folder) opens with **Open Folder**; there Save writes
the files directly.

A DLC pack zip (`Media\DLCZips\…_pri_N.zip`) or a DLC development folder (the pack's loose `Media` files
with its `*_merge.slt` database) shows the cars, wheels and pictures its database names from **its own
`media` folder** first, and from the main game for the rest.

### What each file shows

| File | View |
|---|---|
| `.carbin`, `*.rmb.bin` | 3D model (cars, wheels and brakes; track models) |
| `.xds`, `.xdc`, `.tga` | Texture |
| `.xml`, `.ini`, `.cfg`, `.txt` | Text editor |
| `.html`, `.htm`, `.rdf` | Read-only text |
| `gamedb.slt` | Game database |
| `.fbf`, `.bgf`, `.bsg` | Digital dash (view and edit) |
| anything else | Name and size. Archive > Extract or Replace Selected File. |

### The tree and the Archive menu

The **Archive** menu works on the file selected in the tree (the same commands are on its right-click menu):

- **Open Selected Archive** opens a `.zip` selected in a game folder (also double-click or Enter).
- **Replace Selected File...** puts any file from disk in its place.
- **Revert Selected File** undoes the pending change. On a file you added it reads **Remove Added File**.
- **Add File...** adds a file (see [section 7](#7-adding-files-and-new-textures)); on the right-click menu it is **Add File Here...**.
- **Delete Selected File or Folder...** removes it (**Undo Delete** brings it back until you save).
- **New Folder...** (right-click: **New Folder Here...**) makes a folder in a game folder or a DLC
  package. In a zip the folder only shows in the tree until a file is added to it: a zip stores no
  empty folder.
- **Copy Dash From Another Car...** copies another car's digital dash files into the open car zip
  (experimental, see [section 10](#10-digital-dash-fbf)).
- **Extract All...** writes the whole archive to a folder.

The tree marks pending changes: an edited file is **bold** with `*`, and an added file has `+`.

**File > Close** closes the open folder or archive.

### Going back

Many actions jump to another file:

- opening a zip from a folder;
- *Open Car Model*;
- a `GAME:\` link;
- *Go to* in the database.

**◀ Back** (Ctrl+B or Alt+Left) returns to where you were, with the same selection.

## 4. Saving, backups and undo

- **Edits are kept in memory** until **File > Save** (Ctrl+S). The window title shows `*` while
  there are unsaved changes. Closing or opening something else asks whether to save.
- **Archive > Revert Selected File** undoes everything not yet saved on it.
- **Every save makes a backup first.** The version being overwritten is copied to `bak\` next to
  the exe, named `yyyy-MM-dd_HH-mm-ss_bak_<file>`. To undo a save, copy the backup back.
  Backups can be switched off with `Enabled = off` in the `[Backup]` section of `ForzaStudio.ini`
  (a port otherwise copies the 436 MB `UI\Textures.zip` every time). With them off, work on a copy
  of your game folder.
- **Save As...** writes an archive under a new name. It works for zips only.
- When a saved zip is listed in `Media\zipmanifest.xml`, the manifest is updated as well, after
  its own backup. The game needs this to load an edited zip.

> Close Xenia, or the game, before saving files it uses.

## 5. 3D models (.carbin)

Select a `.carbin` to see the model.

### The model tree and the viewport

The model tree on the left lists **LOD > part > material**. Check a node to show it in the view.

The toolbar has these controls (**Model > Export Checked Parts to OBJ...** writes the checked parts as one OBJ):

- **Wireframe**.
- **Camera speed**: slow, normal or fast.
- **FOV**: the field of view of one of the game's cameras: photo mode (45°, the default), bumper
  (53°), chase (54°), cockpit (62°) or hood (66°). Next to it, the current FOV. The wheel changes it
  between 0.5° and 90°, as the game's free camera does.
- **Go to Camera**: see [Cockpit camera](#cockpit-camera-camera-tab).
- The vertex and face count.
- A red warning, **"N material(s) not in FM4"**, when the model has any. Click it (*click to fix*) to select the
  first one.

**Camera**

| Input | Action |
|---|---|
| Middle drag | Move the view |
| Alt + middle drag | Turn the model |
| Wheel | Zoom |
| W A S D, Q E | Move |
| Arrow keys | Look |
| 1 - 6 (or numpad) | Front, back, right, left, top, bottom view (orthographic; the tool bar switches orthographic off again) |

**Red materials.** A material that no FM4 car uses is drawn red in the tree, and it may render
black in game. Select it and press **Rename Material...** in the Part tab (or right-click it), then pick an FM4 material from the list. You
can rename it in every part and in every model file of the car (main, lod0, cockpit).

The list has three groups: the **recommended** materials first (green, or ★ in the port's Review: the port's
own table, a rule on the name, or the FM4 shaders most similar to the original), then the materials FM4 cars
commonly use, then the **unusual** ones, marked `~` (FM4 has a shader for them but few or no cars use them).
"shader N" is a similarity score out of 100 between the original's shader and that material's (higher is
closer). It compares what the shaders read, not how they look: take it as a hint.

### Cockpit camera (CAMERA tab)

The model view's side panel has these tabs: **Part**, **Texture**, **Mesh**, **Camera**, **Collision**,
**Driver** and **Exhaust** (the last four for car models only).

Open a car model from the game folder (FM4): the Camera tab reads the car's cockpit camera from
`GAME:\Media\db\gamedb.slt`. Values are in the database's axes, where Z points forward.

- **Steering wheel position**: the `CarPartPositions` row PartId 9. The game hangs the cockpit
  camera on this point. Without it, the cockpit view sits at 0,0,0 (cars ported from FM3 lack it).
  When it is missing, the tab rebuilds it at once from the cockpit model and shows it in red as
  not saved.
- **Eye offset from the wheel**: `CameraOverrides` CamCockpitOffset X/Y/Z. 0, 0, 0 means the game's
  default (0, 0.21, -0.51, from `Media\Camera\CameraSettings.ini`).
- The view shows the wheel point (yellow cross), the eye (cyan cross) and what it sees (FOV 62°).
- **Wheel From Cockpit Model** computes the wheel point from the `steering_wheel` part of the car's
  `_cockpit.carbin` (within about 1 cm of the game's own points on FM4 cars). It also sets the eye
  offset X to 0, so the eye sits right behind the wheel (FM3 cars often carry a side offset of about
  2 cm; FM4 cars have 0).
- **View From Camera** puts the viewer at the eye, looking forward, with the cockpit FOV. It opens the
  car's `_cockpit.carbin` first if another model of the car is shown. With **Live** ticked the view
  follows every change of a number.
- **Save to Game Database Now** writes both rows into `gamedb.slt` (missing rows are added) after a
  backup in the `bak` folder. It writes at once, not with File > Save. Close the game first. Values you
  did not change keep their full precision.

The port tool adds the steering wheel point by itself when the source car has none.

**Exterior cameras** (same tab): the hood, bumper and chase offsets of the car's `CameraOverrides` row,
saved with the cockpit camera. They are numbers only, with no preview: the game adds them to a point
of its own that is not in the game files, so a preview would be wrong. A change does move the camera
in game (+0.30 m on the hood height was checked). 0 means the game's default.

**Thumbnail camera** (same tab, experimental): the camera the game takes a car's thumbnail with
(`Media\ThumbnailCamSettings.xml`), shown in orange with the frame it captures. Long cars can get a
camera of their own, further away; **Back To Default** removes it. **Save Thumbnail Camera Now** writes
the file at once (backup first). That the game reads this file for every thumbnail is inferred from its
values, not checked in game.

**Go to Camera** (model toolbar) moves the viewer to a game camera:

- **Cockpit**: the eye of the Camera tab, FOV 62°.
- The garage, photo mode, livery and upgrade cameras of `Media\UICarTargetCameraPresets.xml`
  (position, direction and FOV as the game stores them; a preset made for this car is used instead
  of the general one).
- The race chase, hood and bumper cameras are not offered: the game builds them from a point of its own
  plus the `CameraOverrides` offsets and the global settings in `Media\Camera\CameraSettings.ini`
  and `CameraPhysics.xml` (plain text files, editable as such).

### Mesh, collision, driver and exhaust (side panel tabs)

- **Mesh**: select faces in the view and work on them. Click picks a face, Shift+click adds, Ctrl+click
  toggles; a drag selects with the region chosen in the tool bar (rectangle, circle, fence, lasso or
  brush). The sub-object levels of the tool bar (Vertex, Edge, Face, Polygon, Element) choose what a
  click picks. The tool bar also has select all / none / linked, **Delete**, **Detach** and the **UV
  mapper** (a window with the UV layout, the same selection tools, keys 1-5 for the levels). While the
  tab is open the model is drawn in wireframe and the selection in red. Delete and Detach touch only
  the LOD of the selected faces; pending until File > Save.
- **Collision**: the car's collision spheres and bounding box. Blue: body spheres; orange: contact
  points; grey: the box; yellow: the selected one. **Add** copies the selected sphere, **Mirror** copies it
  to the other side. **Generate Basic Collision...** rebuilds the box and the spheres from the shown
  model, as a starting point for cars whose own is wrong; the top and left views (numpad 6 and 2) help
  to edit it. Pending until File > Save.
- **Driver** (on a `_cockpit.carbin`): the points stored in the cockpit model (hand grips on the wheel
  rim, wheel pivot and axis, shifter, eye-level records) and the driver offsets of `carattribs.xml`.
  Names with `?` are guesses. **Apply to Model** writes them into the car file, pending until File > Save.
- **Exhaust**: the points the backfire flames start from (`Media\Effects\Backfire.xml` draws them).
  Add, Mirror and **Apply to Model** as above.

### Editing parts (PART tab)

Part editing works on FM4 car models and on Forza Horizon 2 models. Select a part in the tree, or
one of its materials to edit only that material.

- **Move** (metres), **Rotate** (degrees), **Scale** (%). Tick **Same scale on all axes** to scale them
  together.
- **Pivot**: the part's center, the model origin, or a custom point.
- **Also in the car's other model files (main, lod0, cockpit)** keeps the three files in step.
  Leave it on unless you know why you want otherwise.
- The change is previewed live in red. **Apply** keeps it, **Reset** clears it.
- **Focus On Part** centres the camera on the part. **Show only this part** hides the rest.
- **Merge...** joins this part into the one picked in **Merge this part into**. Materials with the same name are
  joined, and the source part is removed.
- **Duplicate...** copies a part under a new name. A free variant letter is
  suggested, e.g. `bumperRa` → `bumperRb`.
- **Delete...** removes the selected part or material. The **Del** key does the same.

### Parts from other cars and from 3D programs (PART tab)

- **Copy Part From Another Car...** copies a part from another car's zip, with all its LODs and damage
  data:
  - add it as a new part, or replace one of this car's parts;
  - optionally place it at the replaced part's center;
  - optionally use this car's texture regions for its materials.
- **Export OBJ...** and **Import OBJ...** (one part):
  - import an OBJ to replace a part, or to add a new part based on a template;
  - choose which car material each OBJ material becomes;
  - optionally fit the UVs into this car's material regions.

### Whole model as an OBJ work folder

**Model > Export Model to OBJ Folder...** writes the whole car to a folder:

- one folder per model file, e.g. `toy_supra_98`, `toy_supra_98_lod0`;
- inside it, `lod0`, `lod1`, …;
- one OBJ per part in each LOD folder.

Edit the OBJs in Blender or any 3D program, then **Model > Import Model from OBJ Folder...**. Import
works like this:

- only the OBJs you changed are rebuilt;
- a deleted OBJ removes that LOD of the part;
- a new OBJ file name adds a new part, and you choose a template part for its materials and
  damage data;
- before anything changes, you see the plan; afterwards, a report.

Forza Motorsport 2 models can be exported but not imported back.

### Whole model as one FBX or glTF scene

**Model > Export Model to FBX...** and **Model > Export Model to glTF...** write the car as one scene
with the model's own hierarchy:

```
<car>
  main | lod0 | cockpit          the model file
    LOD0 ... LOD5                the LOD
      <part>                     the part
        <part>__<material>       its triangles with that material
```

- You choose which model files and LODs go in the file (the close-up LOD 0 is ticked first): a small
  scene opens and edits faster.
- glTF can be saved as **.glb** (one file) or **.gltf** with its **.bin** next to it. FBX works with
  3ds Max and Blender. glTF works with Blender and with ZModeler 2 (with a glTF filter), among others.
- Every material carries the texture the viewer shows (the one you picked in the mesh panel, else the
  game's own), as an opaque PNG, so the UV map can be checked in the 3D program: inside the file for a
  `.glb`, in a `textures` folder next to a `.gltf` or `.fbx`.

Edit the scene, export it again in the same format, then **Model > Import Model from FBX...** or
**Model > Import Model from glTF...**:

- keep the names of the `main` / `lod0` / `cockpit` and `LODn` nodes and of the materials; copy
  suffixes such as `.001` are ignored;
- moving or rotating a group moves its parts;
- only the parts that changed are rebuilt; a LOD the file has no node for is left alone;
- a part missing from a LOD the file has is kept, unless you tick **Remove the parts the file does not
  have**; a new group or mesh under a LOD node is a new part;
- textures in the file are ignored: the game keeps its own;
- then you get the same plan and report as for an OBJ folder.

### Texture preview

Every material shows the texture the game draws it with, by itself: ForzaStudio EVO reads the
game's shaders (`Media\Shaders\Cars`) and `Media\cars\Shared.zip\textures.xml`, and takes the
file from the car's zip or from `Shared.zip`. This needs the model opened from a game folder.
The **Textures** button in the model toolbar switches all textures off (plain grey) and on.

The **Texture** tab changes the texture of the selected material. It changes only what you
see: the model file is not touched.

- **(auto)** is the game's texture; pick an `.xds` from the car's zip, or **Texture From
  File...** for one on disk, to override it.
- Choose the UV set.
- The choices last until ForzaStudio EVO is closed.
- The **Part** tab shows the material's shader, the textures it reads and where the shown
  texture comes from.

## 6. Textures (.xds)

Select an `.xds` to view it.

**Viewing**

- The side panel shows the size, format, color space and mip levels.
- **Mip level** chooses which mip to show.
- **Channels**: RGB + Alpha, RGB only, or Alpha only.

Both commands below are in the side panel and in the **Texture** menu.

**Export Image...** saves a PNG, or a 32-bit TGA.

**Import Image...** replaces the texture:

- Accepted files: PNG, TGA, BMP, TIF or DDS.
- The image **must be the same size** as the texture.
- It is compressed to the texture's own format, and the mips are rebuilt.
- The change is written when you save.

## 7. Adding files and new textures

**Archive > Add File...** (Ctrl+Shift+A), or right-click > **Add File Here...**, adds a file to the
archive. Choose:

- the folder in the archive;
- the name;
- the extension.

An image (PNG, JPG, BMP, TIF) becomes an `.xds` texture by default. Any other file is copied as it
is. For textures you also choose:

| Option | Choices |
|---|---|
| Format | **DXT1**: color, no alpha or 1-bit alpha. **DXT5**: color + alpha. **DXN**: normal maps. **DXT5A**: one channel. **A8R8G8B8**: uncompressed. |
| Gamma color | On for color maps. Off for normal maps and masks. |
| Mip levels | The full chain, or none. |

**Texture > New Texture from Image...** makes a standalone `.xds` file with the same options.

## 8. Text and XML files

Text and XML files open in an editor.

| Key or button | Action |
|---|---|
| **Apply** (Ctrl+Enter) | Keep your edits (they are saved with File > Save). |
| **Discard Edits** | Drop the edits not yet applied. |
| **Check XML** | Show whether the file is well formed. If not, it gives the line and column of the error. |
| Ctrl+F, F3 | Find, find next. |

- The file keeps its original encoding, BOM and line endings.
- Applying broken XML asks for confirmation first, because the game may refuse the file.

## 9. Game database (gamedb.slt)

Opening `Media\db\gamedb.slt` shows the database. Edits go into the database at once and are
written with **File > Save**, with a backup.

### Sections

The section buttons are **Cars | Wheels | Tracks | Events | Engines | Makes | Countries | Manufacturers | Tables**.

- **Search** filters the list on every column.
- **Text** chooses the language used for names (English the first time; your choice is remembered).

**Cars.** The header shows the thumbnail and the make logo, with two buttons:

- **Open Car Model** opens the car's zip.
- **Stock Wheel** jumps to its wheel.

The tabs:

- **Car**, **Body**, **Upgrades**, **Colors**, **Other** hold the car's rows.
- **Engine**: choose the engine, or **Add Engine Swap...**. A red line means the engine is
  *shared by N cars*: editing it changes all of them.
- **Power**:
  - every upgrade part and its level, with its torque factor;
  - a torque/power graph. Tick **Edit curve** and drag the red line to reshape it;
  - **New Level** adds an upgrade level.

**Wheels.** **Open Wheel Model**, and the **Used by** tab.

**Tracks.** Track data, and races, grid and AI.

**Events.** The event's data, races and participants. The buttons:

- **New Event (copy of this)**. The copy still uses the original's name and description texts.
- **Add Race...**
- **Add Competitor...**

**Tables.** Any table as a raw grid:

- **Duplicate Row** and **Delete Row**.
- A **Filter** that takes an SQL WHERE condition, e.g. `MediaName LIKE '%supra%'`.
- Id cells are drop-downs, color cells have a picker, and `GAME:\` paths open with a double-click.

### Tools

- **Right-click a field**, or **Go to / Used by** in a table's toolbar, for these commands:
  - **Go to** the row it points at;
  - **Used by** lists the rows pointing here;
  - **Open Linked File** (a `GAME:\` path; also in the top toolbar).
- **Check References** lists ids that point at missing rows. Double-click a line to jump to the
  rows.
- **Changes** counts your edits, and its drop-down lists the last ones.
- **Delete Row** warns when other rows still point at the row.

### Duplicating a car (Tools > Duplicate Car)

A wizard that copies any FM4 car under a name of its own, with a new Id, new names in every language, a copy of its zip,
thumbnail and database rows. Pages:

- **Where**: the game folder, Xenia's content folder as a DLC pack, or a **DLC package file (STFS)** built in a folder you
  choose (the same Xbox 360 file the port window makes).
- **Names, year, make** and the Data_Car numbers.
- **Engine**: shared with the source, or **a new engine of its own**: name (it follows layout, cylinders and aspiration until
  you type over it), sound (any FM4 engine's), layout, cylinders, aspiration (natural, single / twin / quad turbo,
  positive-displacement or centrifugal supercharger), boost, diesel or petrol, displacement, idle and redline. The torque curve
  is drawn with the mouse or made with **Generate curve** (torque range, power peak, limiter). The graph shows the curve up to
  the redline in full colour and the margin the game's table keeps after the limiter dotted and dim. Upgrade levels, turbo or
  supercharger rows and intercooler come from the generic rules (the upgrade sections of `ForzaStudio.ini`); a diesel gets no supercharger.
- **Gearbox**: the source's, or **a gearbox of its own** (1 to 8 gears, a ratio each, reverse and final drive). The copy gets a
  drivetrain of its own; the source car keeps its gearbox.
- **Summary**: the plan and the checks the database rules make before anything is written.

The Game Database tab **Transmission** lists the gearbox levels of a car, next to **Engine** and **Power**.

### Deleting a car (Delete Car...)

On the car's header, **Delete Car...** takes a car that was added to this game (ported or duplicated) out of
it: rows, strings and files. Cars of FM4 itself cannot be deleted.

### Car Performance From Game (Tools > Car Performance From Game...)

The performance index (PI) and the five bars of a car are computed by the game, and the estimate of a port can
be off (for example an off-roader that exists only in Horizon). Type the PI and the five bars the **garage**
shows for the car, and they are written into the car with its class, so the showroom shows the same numbers.
It works on the open database: the game's, or a DLC pack's.

### Change Car Sound (Tools > Change Car Sound...)

Gives a car the engine sound of another FM4 car. The list can be filtered by engine type. It works on the game
folder and on a DLC pack, and the database is updated with it.

### Settings and thumbnails

- **Tools > Settings...** sets the folders (the games, Xenia) and the options.
- **Tools > Set Generated Thumbnail...** puts a car's Xenia thumbnail on the picture the game generated.
- **Tools > Database SQL...** runs one SELECT / INSERT / UPDATE / DELETE statement. A write is checked first
  (keys, references, cars) and refused when it would break the database; nothing else (DROP, ALTER...) is allowed.

## 10. Digital dash (.fbf)

> **Experimental.** Editing a value of an element in place (position, size, colour, text, font) is the part
> that has been checked in game. Duplicate, Delete, Add and **Texts from another car's dash...** change the
> structure of the dash and have **not** been tested in game (a duplicated gear text once crashed the game).
> Use them on a copy.

Select a dash file (`.fbf`, `.bgf` or `.bsg`) to see the car's digital dash:

- the element tree on the left;
- a preview in the middle;
- the properties on the right.

On the preview, the wheel zooms, a drag pans, a click selects an element and a double-click fits
the dash to the view. **Outlines** shows the element borders.

Select an element to edit it in the property list (on the right, grouped as Element, Transform,
Appearance and Graph properties): numbers for positions and sizes, drop-down lists for choices
(alignment, font, texture), check boxes-style True/False for flags, text for strings and a color
picker for colors. Each value is changed where the dash already keeps it, so nothing is added to
the files; a value the element does not have is greyed out. Edits are pending until **File > Save**
writes the car's zip.

On the preview: drag an element to move it, drag one of the 8 handles of the selected element to
resize it (Shift keeps the shape), and use the arrow keys to nudge it (Shift: 10). Ctrl+click adds or
removes an element, a drag on the background draws a rubber band, Ctrl+A selects all; several
selected elements move together, share the property list and can be aligned or spread with the
**Align** menu. **Duplicate** (Ctrl+D) copies the selected elements with everything under them; the copy
shares meshes and textures, is added at the end of the dash and sits 20 units off the original. Give
it a new name, or keep the original names if you want the game to animate or feed it like the
original. To pan, use
the middle or right mouse button.

## 11. Porting cars from Forza Horizon or Forza Motorsport 3 to FM4

**Tools > Import Car From Another Forza > For FM4...** (into the game's files) and **Tools > Create DLC Package > For
FM4...** (into a package) open the same window. Only the target game is chosen in the menu: the source game is read
from the folder you choose in the window. It converts a Forza Horizon 1, Forza Horizon 2, Forza Motorsport 3 or Forza
Motorsport 2 car into FM4. A port includes:

- the model, wheels and brakes;
- textures and dash;
- the database rows;
- names in every language;
- the engine sound;
- the thumbnail.

From Forza Motorsport 3, the digital dash (kept by FM3 in `Media\DigitalGauges`) goes into the car
zip with its textures, as FM4 keeps it. The Porsches need a make FM4 lacks: the port adds it (name, logo, profile and livery
decals).

The port window knows FM4's own cars, including the 176 cars of the official DLC, from a list inside the program: those are
listed as "an FM4 car" / "an FM4 DLC car" and left out unless you tick **Also show cars already in target**. The same
box also hides the cars already ported into the FM4 folder and the cars of a DLC pack of yours installed in Xenia, so
nothing is ported twice.

### Setting up

Set the folders once in **Tools > Settings > Folders**: Forza Motorsport 4, the source games you have (FH, FH2, FM3,
FM2) and **Xenia**. The Xenia line there tells how many DLC packages it found per game. The port window reads Xenia
from there only (its **Settings...** button opens that tab).

1. Choose the **Source game** in its list (the games set in Settings) and check the **Forza Motorsport 4** folder.
   The line under the source says what was found: the game and its DLC installed in Xenia, or what is missing and
   where to set it. Press **Load Cars**.
   - The DLC of the source game come from Xenia (`content\0000000000000000\<title id>\00000002`), FM3's second disc
     included (Xenia installs it as DLC). The official packages are known to the program (`docs/DLC_CATALOG.md`):
     their cars are listed without opening the packages, and a package is read only when one of its cars is chosen.
   - FM2 DLC and FM3's second disc have no database of their own: they use the source game chosen here.
   - The source can also be **car DLC packages** of Forza Horizon, without the game itself.
     **Folder…** takes extracted packages (a folder holding `Media\<offer>.puboffer`, or a folder of them);
     **Package…** takes the Xbox 360 package files themselves (one or more, as the console stores
     them in `Content\...\4D5309C9\00000002`).
   - An FM4 package (title `4D530910`), such as one you built, is not a port source.
2. The list shows every car with its PI and class in both games.
   - By default only the cars FM4 lacks are listed; **Also show cars already in target** shows the others, dimmed.
   - The **In target** column says why a car is dimmed. FM2 cars are blue: they have no close-up models, which the
     port builds as for the Horizon traffic cars (yellow).
3. Select a car and choose its **FM4 template car** (the donor): the FM4 car used as a template for the data FM4
   needs. The ★ cars are the closest matches. Cars of DLC packs installed in Xenia can be donors too (marked
   `[DLC: pack]`), except with Forza Motorsport 2 as source.
4. Choose the **Tires**: *Normal* keeps the tire models the source car has (the Raptor and the Wrangler of
   Horizon have their own), else FM4's generic tires; *Offroad* gives the car the program's own deep-tread
   offroad tires (`Service\OffroadTires`, same size as the normal ones); *Small* gives it the small tires of
   FM4's C1 (`Service\SmallTires`). You bring these files yourself, see [Install](#2-install).

Everything the port needs from the source stays the source's (names, sound, values), and the donor only
supplies what the source lacks. The window also lists the problems it found, with a choice for each, in the
**Review** tab.

### Way 1: DLC package for Xenia (recommended)

The game's own files are not touched. The cars live in a package you can remove at any time.

1. Press **Add →** (or double-click the car) for every car you want in the pack.
2. Give the pack a **Name**: it is the name the game and Xenia show.
3. Press **Trial Pack**. Every car is converted into a temporary folder; nothing is installed.
4. Check the **Review** column. Select a car in the pack to see its review in the **Review** tab,
   and pick a fix for each line, or leave it as is. The usual lines are:
   - a value pointing at data FM4 lacks;
   - a material FM4 renders black (the choices start with the ★ recommended ones, then the common ones; the
     editor shows such materials in red and **Rename Material...** fixes them by hand);
   - a missing engine sound: the line says the source game has none and that another car's sound is used.
     Pick the FM4 car whose sound you want, or leave it.

   If you change fixes, run **Trial Pack** again.
5. Set **Xenia's folder** (the one with `xenia_canary.exe`).
6. **Close Xenia**, then press **Build DLC Package**. The package goes straight into Xenia's
   content folder. Rebuilding a pack replaces it, after a confirmation.
7. Start FM4 in Xenia. The cars are listed under their makes, like any other car.

The other buttons:

- **Remove** takes a car out of the pack.
- **New Pack** starts a new empty pack.
- **Remove Pack** deletes the built package from Xenia.
- **Add To Package...** adds the cars of the trial to a DLC package that already exists, without rebuilding it.
- A car can also be taken out of a built package later: **File > Delete Car from DLC Package...** (section 3).
- **Tools > Check and Validate DLC...** checks a built package (installed in Xenia, a package file or a folder)
  and lists the problems it finds without writing anything: structure, database, files of each car, engine
  sounds and string tables.

A make or a country FM4 does not have (the Porsches, Bowler...) can be added with the pack: the **+** next to
the make asks for the name and the logo, and for the country's flag when FM4 lacks the country too.

**Important**

- Several ForzaStudio EVO packs can be installed together; the names of every pack show. Two packs that use the
  same offer number or the same string keys clash: the Review warns about it.
- Use an FM4 folder where these cars were **never installed with Install In FM4**. The game loads
  the old installed copy instead of the pack's. Build DLC Package warns you about this.
- A pack trial keeps a copy of a large game file (about 440 MB) in
  `%TEMP%\ForzaStudio\CarPort`. Starting a new pack trial deletes the earlier ones.

### Way 2: Install In FM4

**Trial Run**, then **Install In FM4...**, writes one car into the game's own files:

- the database;
- the car's zips;
- the string tables;
- the sound and texture zips;
- `zipmanifest.xml`.

Every file is backed up to `bak\` first. Use this only if you want the car in the base game.

### If a ported car looks wrong

- The **Report** tab shows the full log. It is also saved as `port_log.txt` inside the converted
  car zip.
- Black parts: run the trial again with the material fixes from the Review. Or open the car in the
  main window and rename the red materials (see [section 5](#5-3d-models-carbin)).
- Report bugs at https://github.com/FMdangaina/ForzaStudio-EVO/issues, with the car, the donor and `port_log.txt`.

Technical details of the package format are in [DLC_PACKAGES.md](DLC_PACKAGES.md).

## 12. Music

When you open a game folder, ForzaStudio EVO plays that game's music in the status bar:

- the FM soundtracks and playlists;
- the Forza Horizon radio stations.

The controls:

- ⏮ ⏸ ⏭ and a volume slider;
- the **♪** menu, to choose a playlist or station, turn on **Shuffle**, choose whether music
  starts when a game folder opens, or **Stop**.

**♪ > Add song (FM4)...** puts your own song into FM4: in a race, in the menu music, or both. The audio is
converted with `xma2encode` from the Xbox 360 SDK, which is Microsoft's and **not included**: copy it to
`xma2encode\` next to the exe (the program asks for its path if it is missing). The song is written to
`Media\audio\Music` and to the game's playlist file (the original is kept once as `.orig`).

## 13. Known limitations

**Experimental: not tested in game, or known to be fragile**

- **Digital dash editing** beyond changing a value in place: Duplicate, Delete, Add, and Texts from another
  car's dash (section 10).
- **Model editing**: OBJ import onto a **wheel** zip, a whole model rebuilt from an OBJ work folder, edited
  parts and **Merge Into**. FBX export and import was checked with 3ds Max, Blender and in game; glTF with
  ZModeler 2 (glTF filter) and in game. Textures in an FBX were not checked in 3ds Max.
- **Generic brakes** for cars with a placeholder caliper: they work, but the discs can look plain close up.
- **A new country** added with a new make in a DLC pack: tested on a trial, not in game. Uninstalling a car
  does not remove the country.
- **Add song**: tested in game with stereo 44.1 kHz input; mono or 48 kHz input is not.

**Known problems**

- Cars from Forza Motorsport 2 cannot use a car of a DLC pack as donor.
- Engine swaps whose sound is on FM4's second disc may crash the race with an I/O error: only the first
  disc's files are known.

**Not available yet**

- **FM2 and FM3 models:** FM2 can be exported only; FM3 can be viewed, and converted only by the
  port tool.
- **Automatic LODs.**
- **Profile saves** are signed and encrypted, so they cannot be edited in the main window.

## 14. Other tools

All in the **Tools** menu.

| Command | What it does |
|---|---|
| **Import Car From Another Forza...** | Port a car into the game's files. See [Porting cars](#11-porting-cars-from-forza-horizon-or-forza-motorsport-3-to-fm4). |
| **Create DLC Package...** | Port cars into a DLC package. Same section. |
| **Duplicate Car...** | See [section 9](#duplicating-a-car-tools--duplicate-car). |
| **Database SQL...** | One checked SQL statement on the open database. |
| **Car Performance From Game...** | Type the PI and bars the garage shows for a car. |
| **Change Car Sound...** | Give a car another FM4 car's engine sound. |
| **Check and Validate DLC...** | Check a DLC package without changing it. |
| **Validate DLC for Installation...** | Make a pack fit the packs already installed in Xenia (ids, names, thumbnails, offer, strings), then install it. Use it when a pack was built against other installed packs. |
| **Set Generated Thumbnail...** | Replace a car's Xenia thumbnail. |
| **Settings...** | Folders and options. |
| **Pay Respects (F)** | The original Forza Studio 4.6 window, kept as a tribute. |

## 15. Files ForzaStudio EVO writes

ForzaStudio EVO writes only these files:

| Where | What |
|---|---|
| `ForzaStudio.ini` (next to the exe) | All the settings: folders, language, music, viewer, port rules, generic upgrades, backup switch |
| `bak\` (next to the exe) | A copy of every file before it is overwritten (can be switched off) |
| `%TEMP%\ForzaStudio\` | Work folders of the port, of opened DLC packages and of previews; cleaned up by the program, safe to delete |
| The files you save | In the game folder you opened |
| `Xenia\content\...` | DLC packages you build |

## 16. Credits and license

- ForzaStudio EVO is maintained by **FMdangaina**.
- It is derived from **Forza Studio 4.6** by Mike Davis, with an earlier edit by **JYSB59**.
- The DLC package format was worked out with help from **Jew (@mr.abortion)** and
  **XTD (@xtd_ac)** on Discord.
- The engine power calculation (the torque a power needs at its rpm, as a scalar of the peak
  torque; Generate curve in the engine dialog) follows the notes of **BlackBirdOsaka** on
  Discord.
- The DLC toolchain **fm4dlc** by **Aiden-Piercee** (https://github.com/Aiden-Piercee) was the reference for the
  package contents and for checking the carbin files.
- The FM4 car and part tools by **soulbrix** were studied for ideas. No code was taken from them.
- **Be.HexEditor** (Bernhard Elbl, MIT) is the hex view of the classic window.
- Music is played with **vgmstream** (https://github.com/vgmstream/vgmstream, ISC-style license,
  see `vgmstream\COPYING`).
- `texconv.exe` is from Microsoft **DirectXTex** (MIT). `xds.dll` comes from Forza Studio 4.6.

ForzaStudio EVO is free software under the **GNU General Public License v3.0**. Forza Studio 4.6 code
is MIT-licensed; see `THIRD-PARTY-NOTICES.md`.

Forza is a trademark of Microsoft. ForzaStudio EVO is not affiliated with Microsoft, Turn 10 Studios or
Playground Games. No game data is included: use it only with games you own.

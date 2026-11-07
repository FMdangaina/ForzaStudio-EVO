# Digital dash (Anark): the three files and the data contract

A car zip holds `digitalgauge/dash_<car>.fbf`, `.bgf`, `.bsg` (all big-endian) and the `.xds` images. Checked on the 431 dashes of FM4 (234), FH2 (103) and FH1 (94).

## How the three files join

| file | holds | one entry per |
|---|---|---|
| .bgf | the graph: what changes (properties), the data contract, animations, slides | **element** (index = place in the list) |
| .fbf | the scene: record kind, name, text/font, material colour, image path, meshes | **record**, same **id** as its element |
| .bsg | base values: position, rotation, scale, pivot, opacity | **node**, same **id** (models, texts, groups, layer, camera, light) |

- The **id** joins the three. A behaviour element has id 0 (it exists only in the .bgf). Materials and images have a record and an element but no .bsg node; they are in the .bsg table at its end (u32 id + u8 kind 6 material / 7 image, count at header 0x28).
- A property in the .bgf **wins** over the .bsg value; without the property the .bsg value is used.
- Elements are listed **depth first** (every file). The .bgf tail counts elements by index: behaviours, animation tracks, events, slides. That is why nothing is inserted in the middle (only appended, under the backdrop or layer), and why only the last elements can be deleted for real.
- .fbf record order: children first, a group after its children. Text record: default text is empty in FM4, the real text is the `textstring` property of the .bgf.
- **Models, meshes, materials (431/431, 2026-11-07)**: a model's body u32 0 = its mesh, one mesh per model, mesh k = the k-th
  model record, never shared; a material's body u32 at **0x44 = the id of its model** (its parent element); a model has as many
  materials as its mesh has index buffers; the mesh data is vertices then each index buffer, mesh after mesh, at 4-byte steps.
  The game sizes a model's material list from the mesh and reads that many (function 822C4758): a copied model whose materials
  still named the original crashed FM4 loading the Brera. `AnarkFbf.CloneMesh` / `RepackMeshes` keep these rules on every copy
  and removal (11856 copies with models on the 431 files; repack of an unchanged file and removal of a copy give the bytes back).
  `AnarkDash.LinkProblems` / `RepairLinks`: the Dash view repairs a dash saved by an older build when it opens it (status line
  says how many links, written on Save); 0 problems on the 431 game files, the crashed Brera zip repaired = the fixed build's
  file byte for byte.
- **Slide owner (3684/3684 game slides, 2026-11-07)**: slide header bytes 16-19 (inside our 25-byte "Name") = the element index
  of the owning component = the slide's first entry. Copied gauges kept the original's and removals did not renumber it: FM4
  crashed in the slide builder (PC 82CF04E8; Brera with Gauge_Gear_2 + a new clock + deleted elements: 8 of 16 slides wrong).
  `AnarkBgf.Write` now sets it from the first entry; `SlideOwnerProblems` is part of `AnarkDash.LinkProblems` / `RepairLinks`
  (Dash view on open, DLC validator). Checked: 278 gauge copies + 431 removals on the corpus, links OK, .bgf of the game files
  written back byte for byte.

## Data kinds

Property hash = sdbm (`h = (h + c) * 65599`) of the name, low 27 bits; top 5 bits: `1` int, `3` float, `4` bool, `5` string (index into the .bgf strings); `+16` = behaviour parameter. Element types: 1 node, 2 camera, 3 light, 4 text, 5 material, 6 image, 7 component (scene or gauge), 8 behaviour. Record types: 0 group, 1 text, 2 camera, 3 layer, 4 model, 5 material, 6 light, 7 image.

## Data contract (how the game feeds a dash)

The **first behaviour** (child of the Scene) has 94 parameters, the same in FM4, FH2 and FH1. A pair "on" (bool) + "target" (string) says which element receives a value. The target is a path of names from the scene: `parent.Layer.Gauge_Gear.Group.TEXT_GEAR_DATA`; `Scene.UIContract` or `this` means unused. The **Structure...** button of the Dash view lists everything; selecting the behaviour shows the parameters with these names, and a target has a drop-down of the paths of the dash (setting one switches its "on" value on).

Names below are deductions from the targets the dashes use (e.g. `TEXT_TACH_DATA` on the tach slot) and from the values; they are not game names.

| slot | on | target | other values |
|---|---|---|---|
| speed | 02FB3C14 gauge, 01CC92B1 text | 012D5B95 gauge, 01F1A7EB text (TEXT_SPEED_DATA) | 071FE357 max speed |
| odometer / trip | 001001EB / 06E717D2 | 030765FF / 015CE8CE | 04447E03 units? |
| tach (rpm) | 078C1FAF gauge, 026A51D2 text | 05667472 gauge (METER_TACH), 001BB29C text (TEXT_TACH_DATA) | 008BDC30 max rpm |
| gear | 07F77F3B gauge, 06BAFBAF text | 038EC7D5 gauge, 064405FF text (TEXT_GEAR_DATA) | 06B42F93 number of gears |
| fuel | 030EA3D3, 05ECD5F6 | 0201B14E, 04B6EF78 | 071C402C capacity |
| boost (turbo) | 072842D0, 05F9996D | 04878BD9, 054BD82F (TEXT_BOOST_DATA) | 070BB30D min, 04EEB47B max |
| lap time current / last / best | 045E1DF7 / 058A8F84 / 06AD5476 | 00B74AB7 / 03DC8BD8 / 04FB9F66 | |
| race position, cars, lap, laps | 03019601, 013C521F | 01446A9B, 068C0956, 042EF57D, 0720D35B | |
| ABS / stability / traction / shift lights | 06079D40 / 024544BA / 04B5A012 / 070BB8D0 | 05CD6BF1 / 041F2EF7 / 024CF99F / 04EF8E64 | |
| clock | 02A10FFC | 07C92224 | |
| oil pressure | 0421DCCF, 06A108F2 | 000C0752, 06FE717C | 06343B1E min, 00DD5A30 max |
| oil temp | 062AC6FE, 06E7F621 | 0260E6E3, 03DFBB0D | 000C66AF min, 0127D7C1 max |
| water temp | 02D9FCD9, 032BCA7C | 053A43C8, 013DA2F2 | 038A9D94 min, 04DAA5A6 max |
| outside temp | 07F5FDF2 | 06CD5CEA | |
| G meter | 0455E536, 076FA337, 061C739A | 000912F3 horizontal, 000A1332 vertical, 042B33C2 number | |

**Game names (2026-11-07, default.xex).** The real parameter names are strings of the FM4 xex (table at 820317EC), read by the dash setup
function 824F43F8 (it starts with `CAR_EVENT`, ends with `CAR_PRES_GAUGE_ANIM` / `PRE GAME`): every one of the 96 hashes above but three
matches `H(name) & 0x07FFFFFF`. The pattern is `BOOLEAN_ANALOG_<X>_EXISTS` + `CONTROL_PROGRESS_BAR_<X>` (needle / bar gauge),
`BOOLEAN_TEXT_<X>_EXISTS` + `TEXT_<X>` (number), `BOOLEAN_<X>_EXISTS` + `CONTROL_COCKPIT_LIGHT_<X>` (ABS / STM / TCS lights),
`LONG_/FLOAT_MIN_/MAX_<X>`, `LIST_<X>_UNIT(S)`. Corrections to the old deductions: 03F59520/03FFB820 = `BOOLEAN_ANALOG_GEARED_RPM_TACH_EXISTS` /
`CONTROL_GEARED_RPM_PROGRESS_BAR` (not a gear gauge), 0523363B/0263A72E = tach **memory** (peak needle), 01946C81 = `LONG_MIN_GEAR`,
04447E03 = `LIST_SPEED_UNIT`, 06B664B0 = `LIST_TACH_UNIT`, 077022BF = `LIST_BOOST_UNIT`, 064CB58A = `AKCustomObjType`, 04A9EC63 = `_AKContractClass`,
02C190DD = `GROUP_OVERLAYS`. Unnamed: 073F25FB (always 1), 0775A5DF / 01E4B312 (always off).

What FM4 does with them (824F43F8): an "on" flag is read first; only then the target is resolved and the min/max sent to that control.
`LIST_SPEED_UNIT` 0 -> internal unit 0x14, 1 -> 0x15, other values keep the default; in the dashes 0 goes with `LONG_MAX_SPEED` ~200 and
1 with 300-450, so **0 = mph, 1 = km/h**. `LIST_BOOST_UNIT` 0 -> 0x25, 1 -> 0x24; `LIST_EXTERNAL_TEMP_UNITS` 0 -> 0x2B, 1 -> 0x2D (which is which
not checked). **Not in the FM4 xex** (named only by hash, from the pattern): `BOOLEAN_TEXT_OIL_PRESSURE/OIL_TEMP/WATER_TEMP_EXISTS`, their `TEXT_*`,
`FLOAT_MIN/MAX_OIL_PRESSURE/OIL_TEMP/WATER_TEMP`, `LIST_OIL_TEMP_UNITS`, `LIST_WATER_TEMP_UNITS`: FM4 never reads them, so an oil / water
**number** stays as the dash draws it (only the bars are driven). Harness: `E:\DEV\fm4dev\XexCars\anark` (hashmatch, guess, units).

Gauges with a bar (oil / water temp, fuel) also have their own behaviour (contract `PROGRESS_BAR`, target `parent` = the gauge) and opacity animation tracks on their Fill1..Fill8 elements.

## .bgf tail (after the graph), five blocks of `u32 size + bytes`

### Names are hashes (2026-11-03)

The **element hash** is the full 32-bit `h = (h + c) * 65599` of the element's name in the .fbf (`Scene` = 068C6274, `Layer` = 81A3C24F,
`TEXT_TACH_DATA` = FF6889DB...); a **behaviour's** hash is its contract's name (`UIContract` = BAFC7A1A; others in the corpus: 20B25330 x549,
00CE884D x324, AE4BB809 x147, 1D469C91 x3 unnamed, `CAR_PRES_GAUGE_ANIM` = 8BC40DE5 x11). Property hashes are the same hash masked to 27
bits (+ kind bits): the FM3 "unnamed" parameters of a behaviour are named after the elements it drives (`A896D74C` = string kind + 16 +
`TEXT_SPEED_DATA`). A **slide's** name hashes to the words of the event table.

**2026-11-05, contract behaviours named**: a contract behaviour's hash is `H(<contract name> + "_CONTRACT")`, where the contract name is
the string of its parameter 04A9EC63 (the other fixed parameter, 064CB58A, holds the word `contract`). 20B25330 = `PROGRESS_BAR_CONTRACT`,
00CE884D = `COCKPIT_LIGHT_CONTRACT`; in the UI: `HELP_BUTTON_CONTRACT`, `BUTTON_TEXT_CONTRACT`, `ALIGN_TO_CONTRACT`, `LOBBY_MIC_CONTRACT`...
Holds for 3782 behaviours (3395 UI + 387 FM4 dash); the rest have other names (UIContract, or no contract string). AE4BB809 / 1D469C91 are not
in the FM4 dashes (FH corpus, not checked).

### Source of the names: the FM4 menus are Anark too

`Media/UI.zip` holds 141 Anark scenes (`scenes/ui4/*.bgf/.fbf/.bsg`), the same format as the dashes, plus `eventnames.txt` (825 event names
the game sends, e.g. `FOCUSED`, `SHOW`, `PLAY_INTRO`) and five Lua behaviours (`behaviors/ui4/*.lua`; two are plain text: `eventfire.lua`
calls `fireEvent(getAttribute(self.element, "eventName"), getElement(getAttribute(self.element, "TARGET"), self.element))`; the compiled ones
use `registerForEvent`, `getElement`, `play`, `getTime`, `UI.flowProcess`). The UI corpus has 24,298 events and 108,370 actions against 498 /
780 in the 233 FM4 dashes: it is what settled the grammar below.

### Tail blocks, decoded (2026-11-05)

Checked on the 141 UI scenes and the 233 FM4 dashes, 374/374 consumed to the last byte (`tools/AnarkTail/anark_tail.py check <folders>`;
`dump <file.bgf> [eventnames.txt]` prints the blocks with names). Hashes in block 3 (events, commands, slide names) are `H(name) & 0x7FFFFFFF`:
that is why the same label was seen "with bit 31 set or cleared" (the bit is always cleared; names whose full hash has bit 31 set looked flipped).

1. **events declared by behaviours**: per behaviour `u32 element index, u32 n, n x u32 string index`. The string indexes are the names of the
   events the behaviour declares (in `030_bonus_content`: SHOW_RESTRICTIONS, INTRO, COMPO, NEXT_SEL...); in every file they are sorted,
   unique and contiguous at the end of the string table. (Was "slot numbers".)
2. **animation tracks**: `u32 element, u32 property hash, u32 type<<16 | key count, 6 words per key` (89,375 tracks, all read).
3. **events**: a list of groups until the end of the block:
   - group: `u32 owner element (a behaviour, 7 times a component), u32 n events`
   - event: `u32 event hash, u32 n actions` (98.7% of the events are among the names the owner declares in block 1)
   - action, 5 words: `u32 target element, u32 second element (mostly the component that owns the target), u32 command, u32 arg1, u32 arg2`

   | command | hash | arg1 | arg2 | count UI / dash |
   |---|---|---|---|---|
   | `COMMAND_GOTOSLIDENAME` | 1195C23B | slide name hash | 0 | 34,543 / 768 |
   | `COMMAND_SETPROPERTY` | 7E4906FD | property hash with kind bits (always float so far) | value (float bits) | 73,788 / 0 |
   | `COMMAND_FIREEVENT` | 06875030 | event hash | 0 | 12 / 0 |
   | `COMMAND_PLAY` | 573BA2B8 | 0 | 0 | 18 / 12 |
   | `COMMAND_GOTOTIME` | 5B2FA69C | time | 0 | 9 / 0 |

   The command names are those of the Anark runtime (the same `COMMAND_*` set survives in NVIDIA UI Composer / Qt 3D Studio).
   Dash example: owner `COCKPIT_LIGHT_CONTRACT`, on `ON` → GOTOSLIDENAME `ON` on its light component, on `OFF` → GOTOSLIDENAME `OFF`.
   Car-presentation example (as read before): event "DOOR CLOSE" (1CDF13DA) → GOTOSLIDENAME "DOOR_CLOSE" (516FC599) on component START_UP.
4. empty in every file (UI too)
5. slides: `25 bytes name, u32 duration, u32 count`, entries `u32 element, u8 flag, u32 A, A x (u32 property hash, u32 value), u32 N,
   N x u32 reference`; one slide set per component, listing the elements it owns, in index order. The A items are the values the slide
   gives the element's properties. A **reference** is `top byte 0 = animation track / 1 = action, next byte a flag, low 16 bits the index`
   (in block 2, or among the actions of block 3 counted in file order). Every action is referenced twice (flag 0 and 1) by the entry of its
   second element, which is therefore the **time context** of the action; every track by the entry of its own element (374/374).

Header (374/374, `AnarkBgf.UpdateHeader`): 0x1C highest id << 8, 0x24 / 0x70 elements that are not components, 0x28 / 0x74 components,
0x2C / 0x78 properties, **0x30 / 0x7C entries of block 1** (not the behaviours: they differ in 53 UI scenes), 0x34 / 0x80 declared strings,
0x3C strings, 0x44 tracks, 0x48 keys, 0x4C event groups, 0x50 events, 0x54 actions, 0x5C slides, 0x60 entries, 0x64 A items, 0x68 references.
0x6C is not a count found so far (kept as it is).

Ordering in the game's files: block 1 and block 2 sorted by element, slide entries in index order, elements depth first (parent before child,
a subtree contiguous); event groups are not always sorted (113 of 374). One game file has an event with no actions.

Element hashes: the full sdbm of the .fbf name for 12368 of the 15609 named elements of the FM4 dashes; most images (2467), some groups (232)
and models (515) have a hash from another name (probably the editor's), not found.

**In the editor (2026-11-05, offline only)**: `AnarkBgf` holds blocks 1-3 as lists (`Declared`, `AnimationTracks`, `EventGroups`) and the slide
references, writes them back byte for byte (374/374) and renumbers every reference: `InsertElements`, `RemoveElements`, `CopyTracks`.
`AnarkDash.Duplicate` puts the copy right after the original under the same parent, with the slide values and the animation tracks of the
original; `Remove` takes away any element with its behaviours, tracks, events and slides. Checked on the 210 FM4 dashes that have all three
files: 7365 duplicates (2442 with tracks) keep every rule above, and removing the copy gives the three files back byte for byte; 15399
removals anywhere keep every rule; an import of a text group into all 210 too. The Structure report prints blocks 1-3 with names.
Harness: `DashTest.cs` (see TODO 12.8). **Nothing of this is tested in the game yet.**

## The game's deserializer (default.xex, 2026-11-07)

FM4 links **Anark Gameface 4.0.7** (source paths `AKBGFDeserializer.cpp`, `AKBSGDeserializer.cpp`, `AKSlideBuilder.cpp`... in the xex).
One reader function per section, each checks its own `u32 size` ("<name> section size mismatch" is only logged). Image `fm4_base.exe`,
disassembly in `E:\DEV\fm4dev\XexCars\anark\*.txt`, harnesses there (slides, s6c, dup6c, eflag, cpre, bsgn).

| file | section (game name) | reader | our name |
|---|---|---|---|
| .bgf | Header | 82AA6680 | header 0x00-0x1F |
| .bgf | Memory | 82AA6900 | header counts 0x20-0x87 |
| .bgf | String | 82AA8C28 | strings |
| .bgf | Element | 82AA6B18 | graph |
| .bgf | Contract | 82AA6EF0 | tail block 1 (declared events) |
| .bgf | Animation | 82AA7078 | tail block 2 |
| .bgf | Logic | 82AA7348 | tail block 3 (events / actions) |
| .bgf | Params table | 82AA7630 | tail block 4 (always empty) |
| .bgf | Slide | 82AA8830 | slides |
| .bsg | Header / Memory / Scene Graph / Asset | 82AA77C8 / 82AA79B0 / 82AA7A70 / 82AA7F80 | header / nodes / tail table |

**BGF header**: u8 endianness (1 = big), u8 (4), u32 size of the rest of the header (0x1A), `AnarkBGF`, u32 version (must be 3,
"Incorrect version specified on the presentation"), u32 (0), **u16 width, u16 height of the presentation (1280 x 720)**, u8 (1),
**u32 highest element id at 0x1B** (what we write as "0x1C = id << 8": same bytes while ids are below 2^24), u8 (0).

**BGF Memory section** (0x20): u32 size (0x64), 19 counts handed to the runtime builders, then 6 more words (0x70-0x84) that the
game reads and **ignores** (they repeat 0x24-0x34). Builders: Element (0x24 non-component elements, 0x28 components, 0x2C properties),
Contract (0x30, 0x34), String (0x3C; 0x38 unused), Animation (0x44, 0x48; 0x40 unused), Logic (0x4C, 0x50, 0x54), Params (0x58),
Slide (0x5C slides, 0x60 entries, 0x64 items, 0x68 references, **0x6C**).

**0x6C = slide entries with an odd number of references** (431/431). The slide builder (82CF0408) allocates
`2 * (4 * (4 * slides + entries + items) + references + 0x6C)` bytes ("Mgr:Slides"): references are u16 and each entry's list is padded
to 4 bytes. A wrong (too small) value makes the game write past that block. `AnarkBgf.UpdateHeader` now recomputes it; before, 952 of
3072 test duplicates left it wrong (fixed 2026-11-07, not seen in game yet).

**Element record**: u32 hash, u32 parent index, u32 id, u8 type, u8 flags. Flags: `0x04` = component (then u8 + u32 follow), `0x01`
always set (every element of 431 files). Component prefix: **u8 = number of slides of the component** (1602/1602; read and not used by FM4),
u32 passed to the component (duration).

**Slide header**: 16-byte name, **u32 owner component element index** (always a component, 431/431), u8 play flags (bits 0x01 0x04 0x08
0x10 become separate flags: seen 00, 08, 09, 0D), u32 start time (always 0), u32 end time (our "Duration"), u32 entry count.
Our parser's 25-byte "name" = name + component + flags + start time. Entry: u32 element, u8 flag, u32 A + A x (hash, value; a string
value is a string index), u32 N + N x (u8 kind 0 track / 1 action, u8 flag, u16 index).

**BSG node (96 bytes) as the game reads it**: u32 parent, u32 id, u8 kind, 3 bytes ignored (always 0), 12 floats (position, rotation,
scale, pivot), 6 floats (bounds min, max), float opacity, u8 / u8 / u8, u32, u8 bool. Seen: the three bytes are `0 4 0` except the root
group (`255 4 1`) and the layer (`0 4 1`); the u32 is always 0; the last bool is 1 on every text and on half of the models (meaning not
checked; the runtime has Opaque / Transparent draw lists).

**In-game test (2026-11-07), resume here.** Brera (`ALF_BreraTi_09`, FM4_Extracted_DEV): (1) clock group duplicated -> no crash, but
nothing visible: the Brera's clock is switched off in the contract (`BOOLEAN_CLOCK_EXISTS` 0), the original does not show either, and the
default texts of texts the game does not drive do not show (outside temp number empty too). (2) INSTALLED, NOT YET SEEN: oil temp bar
(`Gauge_Oil_Temp > TEXT`, Fill1-8 + Dash1-8 with their tracks) duplicated as `TEXT_OilBar2`, moved y +60; 0x6C 4 -> 6 (the old code kept 4).
Check: second bar visible, fills with the original, no crash. Files and harnesses (dup, dupoil, scan, clk) in `E:\DEV\fm4dev\anark\brera2clock`,
original DEV zip = `ALF_BreraTi_09_DEV_backup.zip` there (put it back after the test).
**Result 2026-11-07: crash on load** (also on a stock track). Xenia crash dump: PC 822C48A8 in 822C4758, reading the material
list of a model: the copied materials still named the original models (body 0x44) and the copied models reused the original
meshes. Fixed (see "Models, meshes, materials" at the top); new test zip `brera2clock\ALF_BreraTi_09_oilbar2.zip` (copies =
meshes 16-31, materials -> 249, 251...), the crashed one kept in `brera2clock\crash`, harnesses there (recs, matown, meshes,
meshtest, why). **In game 2026-11-07: OK** - no crash, the second bar shows (y +60 = up on screen) and fills with the original
(same segments lit). First in-game proof of an in-place duplicate with models, materials and animation tracks. DEV Brera put
back to the original after the test.

**Editor test by the user (2026-11-07 afternoon), RESUME HERE.**
- Done in the UI on the Brera: Duplicate of `Gauge_Gear` (-> `Gauge_Gear_2`), a new clock (`Gauge_Clock_2`), some elements
  deleted. Crashed (slide owner, see "Slide owner" at the top) -> fixed; the user's file repaired = 8 slides, loads in game.
  Files: `E:\DEV\fm4dev\anark\brera_gear2\ALF_BreraTi_09_gear2_clock_CRASHED.zip` / `_REPAIRED.zip`. **DEV Brera is now the
  REPAIRED user edit**; the original DEV zip is `E:\DEV\fm4dev\anark\brera2clock\ALF_BreraTi_09_DEV_backup.zip`.
- Repair on open checked in the UI: the CRASHED zip shows "8 ... link(s) ... repaired" (OK). DLC validator dash check passed.
- `_2` copies do not show in game: expected. The contract feeds each value to ONE target (`TEXT_GEAR` -> the original
  `TEXT_GEAR_DATA`), the Brera clock is off (`BOOLEAN_CLOCK_EXISTS` 0, `TEXT_CLOCK` -> `Scene.UIContract`), and a text the game
  does not feed is not shown. Duplicate now says so in the status line. Feeding a text = only Add > Gauge ("Fed by").
- `Gauge_Oil_Temp` cannot be duplicated (has a behaviour, by design): duplicate its `TEXT` group instead (that is the copy seen OK).
- Fixed the same day: Import from another car and Copy Dash opened the chosen `.zip` with `ForzaZip.OpenFile` (single loose
  file) and always said "no digital dash" (C6R) -> `new ForzaZip` for a `.zip`; Duplicate of several elements now goes by
  element id (in-place insertion moved the indexes of the next originals). Built in bin\Debug, not yet tried by the user.
- Next tests: (1) Add > Gauge, copy of `Gauge_Clock`, Fed by "Clock" -> the clock should show in game; (2) Import from the C6R
  again (if "no group or text to take": everything there is inside gauges, Import does not take gauges); (3) optional
  experiment: a copy of `TEXT_GEAR_DATA` inside the same group with the same name/path, to see whether the game feeds both;
  (4) at the end put the DEV backup back. Nothing committed (AnarkFbf, AnarkBgf, AnarkDash, ArchiveEditorForm.Dash,
  ArchiveEditorForm.DashCopy, DlcValidator, this file).

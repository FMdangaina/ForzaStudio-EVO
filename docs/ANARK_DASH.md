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

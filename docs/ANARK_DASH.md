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

### Event table (block 3)

Words: element indexes and name hashes. The hash of a slide label ("START UP" = B2D91FA7, "IDLE", "DOOR OPEN" = 941A7024, "ON" = 2734DC01,
"OFF" = 7E58B971...) appears with bit 31 set or cleared (same name). In the car-presentation dashes (`CAR_PRES_GAUGE_ANIM`, Autovista) it reads
as 2 header words (behaviour element, node) and then 7-word records: **event name hash**, behaviour, component, 0 (scene), `1195C23B`
(in every record, an unnamed action, probably "play slide"), **slide hash**, 0. Example: event "DOOR CLOSE" (1CDF13DA, a name the game's code
sends, with a space) plays slide "DOOR_CLOSE" (516FC599) of component START_UP. The gauge dashes (ON/OFF per light, 60 of the 154 FM3 dashes)
use shorter records with the same words (event ON/OFF, two component indexes, `1195C23B`, slide ON/OFF, 0). The record grammar for the gauges is
not settled. The Dash view's structure report (`DashReport`) now prints every word with its name.

1. behaviour table: per behaviour `u32 element index, u32 n, n slot numbers`
2. animation tracks: `u32 element, u32 property hash, u32 type|key count, 6 words per key` (time ms, value, ...)
3. events (not decoded; element indexes and hashes)
4. empty in every file
5. slides: `25 bytes name, u32 duration, u32 count`, entries `u32 element, u8 flag, u32 A, A x 8, u32 N, N x 4`; one slide set per component, listing the elements it owns. Header 0x5C / 0x60 / 0x64 / 0x68 = slides / entries / A items / track references.

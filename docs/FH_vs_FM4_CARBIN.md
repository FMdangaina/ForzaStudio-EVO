# FH vs FM4 carbin: binary differences (TOY_SupraRZ_98)

Date: 2026-10-22. Same car in both games: `FM4\TOY_SupraRZ_98.zip` (FM4) and `FH12\TOY_SupraRZ_98-1.zip` / `-2.zip` (Horizon).
**The two FH zips are byte-identical** (same SHA-256 `FD65FFBDâ€¦E217`): the FH1 / FH2 copy is one file twice, so this compares FM4 against FH only.
Scripts: `tools\CarbinCompare\*.py` (Python 3, numpy; run from a folder holding `fm4\` and `fh1\` extracted with `fm4_dev\tools\unzipfm4\extract.exe`). `cb.py` parses both formats (big endian).

## 1. Files

| | FM4 | FH |
|---|---|---|
| model files | `toy_suprarz_98*.carbin` (lower case) | `TOY_SupraRZ_98*.carbin` (mixed case) **plus** `stripped_*` copies (314 KB base, 40 KB lod0; not ported) |
| carattribs | `carattribs.xml` Version 15 | `CarAttribs.xml` Version 21 (more gauges; soft Damage preset: PartDropGlobalScalar 0.15, scalars 1 / 25 / 200, ScrapeMinSpeed 500) |
| extra | `versiondata.xml` | `physicsdefinition.bin` (2364 B), `interior_emissive_LOD0.xds`, `BuildNumber.txt` format |
| identical | `nodamage*.xds`, `damagelights*.xds`, `interior_lod0.xds`, `leather2_nrm.xds`, `wood.xds`, liverymasks, `physics\maxdata.xml` | same |
| different | `damage.xds` 77 % of bytes, `damage_lod0.xds` 76 %, `lights*.xds` 42-47 % (same size, same header size) | |

## 2. Carbin header

| | FM4 | FH |
|---|---|---|
| typeId | 2 (lod0, base), 3, 1 | 5 |
| block before the part table | 0x15c bytes of stale pointers / memory | 0x150 bytes, almost all zero (+ a 0x20-entry table, empty on this car) |
| 0x340 block | | 5 bytes differ |
| draw lists | `[24, 166]` (lod0) | `[20, 164]` (the FH car has 38 sections, FM4 40: FM4 has `headlightL/R`, FH folds them into other parts) |
| section | lead word 0x2c9 / 2 / 5, `after_name` = **2** | `after_name` = **3** |
| lod0 vertex | **32 bytes** | **28 bytes** |
| sub-section | index marker 3, then count, size | marker 4, then `strippedCheck` word (2 = no index list), then count, size |

## 3. Vertex (28 vs 32 bytes), same car, 170,879 matched lod0 vertices

Bytes 0..23 (position snorm16x4, UV0, UV1, qtangent) are **byte-identical** on every vertex of every section with equal vertex count. Only the tail differs.

- FM4 tail = 4 Ã— SNORM16 (8 bytes); FH tail = 4 Ã— SNORM8 (4 bytes). Byte correlation: FM4 bytes 0/2/4/6 with FH bytes 0/1/2/3; the odd FM4 bytes (low halves) are uncorrelated. Same four values, 16-bit vs 8-bit. This is the baked-light tail, and the port already widens it (`CarbinModel.WidenVertices`, `WriteTail`): nothing to fix here.

## 4. Per-vertex damage table (the "B" table: 4 bytes per vertex, deformable sections only)

Both games have it, same count per vertex, same sections (FM4 36 of 40, FH 34 of 38; all FH ones are also FM4's). **The content is encoded differently.**

1. **Per-table prefix (FH only).** An FH table is `count, stride, flag [, ID], data`: flag 0 (empty) or flag 1 + a 4-byte buffer ID (what `CarbinModel.ReadExtra` already reads). FM4 has no flag / ID. The first version of this note read those 4+4 bytes as data, shifting the FH table by two vertices; fixed in `cb.py` (see 4d). `Extra = new byte[0]` at `CarbinModel.cs:220` is right: FM4 has none.
2. **Word layout.** FM4: signed `11:10:11` bits (MSB first), a direction at full scale: length 1025 median (p5 992, p95 1027), x about Â±1020. FH: `11:11:10`, vectors of median length ~95, all with x negative in the signed reading (99.7 %).
3. **Same quantity, different frame.** After re-reading the FH word as 11:11:10, direction vectors agree (|cos| median 0.99 on 73,449 vertices) but the **sign agrees on only ~58 %**, and it is not random: on `bumperRb` the sign flips with the section-local height (`pos.y` â‰¤ 0 â‡” same sign) on 99.3 % of vertices, on `bumperRj` 96.9 %, while on `wingf` it is the same on 100 %. So FM4 stores a vector whose sign depends on the vertex side, FH one that does not (probably the FH vector is relative to the 8-byte trailer value and the FM4 one is a unit direction).
4. FM4 vertices of the same car where the FH vector is ~0 still have a full-length direction: FM4 carries the direction only, the magnitude is somewhere else (not found: not in the tail, not in the table).

Conclusion: the FH words are copied into FM4 as they are, FM4 reads them as 11:10:11 unit directions and gets garbage (wrong bits in y / z, wrong sign half of the time, length ~95 instead of 1023). That matches B1 ("FM3 â†’ FM4 deforms, FH â†’ FM4 does not", FM3 = FM4 on 93 % of words).

### 4b. Refinement (same day, scripts `layout.py`, `same.py`, `side.py`, `extra.py`, `ranges.py`)

- **Bit layouts, searched by brute force over all widths 8..12 (213,437 FM4 and 204,027 FH words):** FM4 = x 11 | y 10 | z 11 bits (MSB first), signed, scale 1023, length 14..1036 (99 % between 740 and 1030): a **unit direction**. FH = x 11 | y 11 | z 10 bits, signed, **not normalised**: length 0..1384, median 78, 22 % of the words shorter than 30 and 4 % shorter than 8 (the FH length is a magnitude, 0 = no deformation).
- Normalised, the two agree to |cos| 0.996 on the median vertex (83 % above 0.97), e.g. FM4 (-950, 66, 367) = FH (-60, 4, 23) x 15.8. So the direction is recoverable from FH; the y / z components of FH are only 4-5 bits wide at length ~90, so they are noisy.
- (Retracted, see 4d: the "8-byte trailer = part's typical vector" was the last two vertex words of the shifted table.)
- **Sign:** FM4 = ±FH direction. On a symmetric part the FM4 sign flips across a plane of the section frame (axis y, threshold near the section centre: `bumperRb` 99.6 %, `bumperRj` 99.4 %, `exhausta` 99.7 %, skirts 99 %, `wingf` / `trunk` never flip); on other parts a single plane gives only 76-90 %. No global plane in car space (66 %), no relation to the normal / tangent / bitangent (50 %). So FM4's direction points away from the part's centre line, FH's does not carry that sign.

### 4c. physicsdefinition.bin, and the FH vector in world space (`radial.py`, `side2.py`, `world.py`)

- `physicsdefinition.bin` (FH only, 2364 B, big endian) is rigid-body data: inertia tensor and centre of mass (first 0x80 bytes), the collision spheres (same values as `physics\maxdata.xml`, which is already in FM4), and a 38-vertex convex hull with its face lists (from 0x460). Nothing per render vertex: it does not help to decode the damage words.
- Car axes (from the collision spheres): x lateral (±0.91), y up (-0.18..1.08), z longitudinal (-2.18..2.10).
- **The FH vector is an outward push direction.** With the component permutation world = (h_y, h_z, -h_x) it points away from the car centre (0, 0.4, 0) on **95.1 %** of 146,590 vertices of 38 parts (|h| > 20). So the FH sign is geometric, and the trailer is the part's default push direction.
- FM4 is the same axis (|cos| 0.996 in the same component order) but its sign is **not** outward (best permutation 59 %). On rear bumper, skirts and exhaust it flips with the world height of the vertex (threshold 0.14..0.55 m by part, 99 % on `bumperRb` 0.358, `bumperRj` 0.284, `exhausta` 0.142, `skirtRa/La` 0.23); on `wingf`, `trunk`, `skirtLi`, `glassLHL`, `bumperFc` it never flips. So FM4 looks like a field that turns around a horizontal hinge (upper half one way, lower half the other), FH like a plain outward push. The hinge height rule per part is the missing piece.
### 4d. Correction: table offset, FM4 format (`cb.py`, `fit.py`, `unitfind.py`, `fhfind.py`)

- The FH damage table has a flag word (+ ID) before its data; with the offset fixed the FH words are clean: length 52..115 on `bumperRb`, no 0 / 1 / huge values, vertex for vertex the same repeats as FM4 (FM4 `0x88828713` twice = FH `0xf6c0afee` twice).
- **FM4 word = signed normalised 10:11:11** (x 10 bits /511, y 11 bits /1023, z 11 bits /1023, MSB first; the format of the Xbox 360 `DHEN3N`): length 1.002 median, p5 0.966. Not 11:10:11 as written above.
- **FH word:** best fits are 11:10:11 / 11:11:10 (indistinguishable, y is small), not a unit vector (length ~90 of 1023). Unit(FH) vs FM4 direction: |cos| 0.9959 median. A conversion `normalize(FH)` -> FM4 10:11:11 is therefore possible direction-wise; exact FH scale / format still unknown.
- Sign results of 4b / 4c hold after the fix (FH 95.2 % outward from the car centre with world = (h_y, h_z, -h_x); FM4 59 %; FM4 sign flips with world height on the rear bumper, skirts, exhaust at 99.7-99.9 %).

## 5. Next steps (needs a game or a decoded formula)

- A. Replace the FH words with FM4-style words: unit direction `normalize(decode_FH(word, trailer))` packed 11:10:11 with the sign rule found above (needs the rule for the sign, ties to `pos.y` / part frame). Try on `wingf` first (sign 100 % equal, simplest part).
- B. Cheaper and testable at home: take each vertex's word from FM4's own car of the same part (nearest vertex search, same as `CarbinModel` does for rebuilt parts, lines ~1042-1070), or from FM3 / FM4 donor cars.
- C. Test in game one car with option B on the Supra alone (the FM4 Supra is the reference) to see whether deformation returns before decoding more.
- D. Open: what the 8-byte trailer is (range / pivot), what `after_name` 2 / 3 does, and where FM4's magnitude lives.



### 4e. Same car, three carbins; comparison with carbin-garage (2026-10-22; scripts in the session scratchpad, same method as `cb.py`)

- **Which carbins have tables** (Supra): main carbin 38 of 53 sections (FM4 and FH1 / FH2, table "A", one word per LOD vertex), `_lod0` 36 of 40 (FM4) / 34 of 38 (FH), table "B" per LOD0 vertex, `_cockpit` 2 of 22 (FM4: hooda, hoodg) / 4 of 24 (FH: also the headlights). The interior has no tables on seats, wheel, gauges, in either game.
- **FH1 and FH2 tables are byte-identical** (76 of 76 tables). The FH lod0 tables are full and in the same format as the main carbin ones: nothing special about FH2.
- **Zero words:** FM4 none. FH 6.4 % (main) and 2.3 % (lod0); `undercarriagea` 81 % / 78 % (not deformable), `body` 17 % in the main carbin but 0 % in lod0.
- **Main vs lod0 on the same vertex** (matched by position, 3 mm, about 20,000 vertices): FM4 same direction 87.8 %, opposite 7.8 %; FH 91.9 % / 0.0 %. In FM4 the opposite sign is concentrated in some sections (skirtRb 39 %, skirtRi 32 %, wingg 31 %, bumperFg 22 %, skirtRd 21 %). So FM4's own sign is not a stable property of the point: it changes between LODs for the same vertex.
- **FM4 vs FH on lod0, same vertex index, per section:** same sign above 80 % on skirtLi, skirtLb, skirtRi, bumperFb; opposite on 88-91 % of bumperFi, bumperRc and bumperFc (a whole-section inversion, not per vertex); mixed elsewhere. If the sign matters, copying FH words as they are should deform bumperFb and not bumperFc.
- **carbin-garage (ItsNotPaths)**, read on the same files: it does not decode the damage word (it calls it "skeleton indices and weights", which our data contradict). It keeps the donor table and resizes it when the vertex count differs. Its tail layout is 4 bytes off: it reads the table's `[flag=1]` as "4 reserved" and takes the ID as the first word, the last word as "mid-skip"; its "variable 4-8 bytes of padding" comes from that. Ours parses the sections exactly. Both games share the word 2 (FM4) / 3 (FH) before `a` and after the table (probably a layout version); FH has one more zero word before it.
- **The 8 bytes** are `[flag][ID]` in front of every FH buffer (vertex pool and each table). The ID is random and different per buffer, so it is not damage data.
- Section vertex counts differ between FM4 and FH for some sections (bumperRb 1600 / 1601, wingrace 1755 / 1754, bumperRc 1983 / 1982): `a` follows the vertex count, so a port must write a table of the new length.
- **Test planned at home:** copy FH words as they are into the FM4 carbin (sections where the sign agrees first, bumperFb) and see what deforms; then the donor-words option (B).

## 6. `stripped_*.carbin` (FH1 / FH2): what they are (2026-10-31)

Question: do the stripped copies hold byte definitions FM4 lacks? **No.**

- Every model carbin of an FH car has a `stripped_` twin (main, `_lod0`, `_cockpit`, calipers, rotors). Supra: 314 KB / 40 KB / 20 KB against 3.0 MB / 8.4 MB / 2.5 MB.
- A stripped file is the full file's structure with the **data removed**: vertex buffers, index lists and damage tables A / B are empty (buffer flag word 3 + ID instead of data; sub-section `IndexWord` 2 = no index list, only count and size kept).
- Checked on the Supra FH1 (main 53 sections, `_lod0` 38, `_cockpit` 24), stripped against full, field by field with `CarbinModel`: **zero differences** in marker, bounds, `Unk1c`, `A` / `B` blocks, `AfterA`, `PostName`, `AfterLod`, `AfterSubs`, `Post9`, `Mid`, vertex and table counts and strides, sub-section UV, `Fixed36`, `Fixed56`, tail, index counts. Buffer IDs were skipped (random per buffer).
- So the unknown words (the one before table `a`, word 2 / 3, the 8 bytes `[flag][ID]`) have the same values in both files: a stripped file adds no information about them, nor about the damage words of section 4.
- Purpose (not proven): a geometry-free index of the model the game can read without streaming the vertices.
- Port: `stripped_*` stay out (CarPort.cs, PartImport.cs).
- Viewer bug fixed the same day: opening a stripped file in the model view made `ForzaMesh.Draw` fail inside an effect pass (zero vertices); the error was swallowed and the device stayed half-begun, so the following models were not drawn. `ForzaMesh.Draw` now returns on a mesh without vertices or indices. Not reproduced, deduced from the code; fix built, not checked with a stripped file.
- Not checked: FH2 stripped files, other cars. Tool: `tools\CarbinCompare\strippedcmp.cs` (C#, references the built exe: `csc -platform:x86 -r:ForzaStudioEVO.exe strippedcmp.cs`, then `strippedcmp full.carbin stripped.carbin`).

## 7. What an FM4 -> FH writer must change (2026-11-27, C# harnesses, port INTO FH study)

Harnesses `E:\DEV\fm4dev\ToFH\tools\fhcmp.cs` (zip entries, every carbin field by field) and `headfh.cs` (header blocks,
vertex tail fit); outputs in `E:\DEV\fm4dev\ToFH\supra\` (`car_fm4_fh1.txt`, `wheel_fm4_fh1.txt`, `head_fm4_fh1.txt`).
Supra TOY_SupraRZ_98, FM4 against FH1 (the FH1 and FH2 zips are byte-identical, section 1).

**Zip.** FH adds `physicsdefinition.bin`, `interior_emissive_lod0.xds` and the 11 `stripped_*.carbin`; it has no
`versiondata.xml`. Identical: maxdata.xml, livery masks, nodamage / interior / damagelights / wood / leather textures and
all wheel textures. Different: carattribs.xml (v15 / v21), shadersettings.xml, damage.xds, damage_lod0.xds, lights.xds,
lights_lod0.xds, buildnumber.txt, build report.

**Carbin, the same on every file (121 car sections, 986 subsections, wheel too):**
- type 2 -> 5 (main, lod0, cockpit); brakes stay type 1 and wheels type 3, with word 4 0x10 -> 0x11;
- header word after the 0x150 block 0x10 -> 0x11 (12 bytes `10 10 1` -> `11 10 1`);
- section PostName, AfterSubs, Mid 2 -> 3; Post9 last byte 2 -> 3; subsection Three 3 -> 4;
- every vertex buffer and damage table gets the FH word: `00000000` when empty, `00000001` + a 32-bit ID (BufferId);
- vertices 32 -> 28 bytes: bytes 0-23 equal (253929 vertices), tail 4 x SNORM16 -> 4 x SNORM8. The FH tail is FM4's own
  bake within a few steps (fit FH = 0.98 / 0.96 / 1.01 / 0.98 x FM4, r 0.92-0.99, mean difference 0.03-0.045), so
  FM4 / 32767 x 127 keeps it better than regenerating it (WriteTail's FH formula);
- unchanged: Marker, Unk1c (26 differ by float rounding only), A, AfterA, B, Pre, VbTest1/2, AfterLod, bounds,
  deformable flags, subsection Pre5, Fixed36, Fixed56, IndexType, IndexWord, IndexSize, Tail; trailer; draw-list
  structure; the 140-byte record and the 0x340 block of the header (one float at +0x324/+0x328 differs in the 4th
  decimal).

**Header bytes 4..0x154:** FM4 keeps exporter memory there (stack pointers such as 0018F444, 77C90AE5); FH writes zeros,
plus two float quadruples at +0x40 and +0x80 (car-specific, FH values close to FM4's: 1.0016 1.1496 0.0744 0.4187 vs
0.9911 1.1366 0.0803 0.4269) and 0018F455/56 words near the end. Meaning not decoded.

**FH light records (main carbin only, E = 16 records of 0x20 bytes before the 140-byte records):** x, y, z, 0, then 16
bytes of flags. Positions are the lamps: front pairs at x +-0.534 / +-0.632, z 1.92-2.01; rear groups at x +-0.357 /
+-0.459, z -2.05. Word 5 starts 7FFF (front) or 8001 (rear) = direction; the next bytes hold side (01 left, 02 right),
type and what looks like colour / size. FM4 has none (no night). A writer into FH must build them from the car's lamp
meshes; FH lod0 / cockpit files have E = 0.

**Model differences (FH remodelled the lights, the rest is FM4's mesh):**
- the lod0 headlights live in the COCKPIT carbin in FH (`headlightL/R` only in FH cockpit, only in FM4 lod0): mirror of
  the FH -> FM4 rule "headlights to lod0" ([fh-damage-words]);
- body, headlightL/R, bumperFa (main and lod0) are split by new FH lamp materials: indicator_left / indicator_right,
  taillight2S, fullbeam, oldhead, xenonhead; FM4's head_light / tail_light / reverse_light_lod0 meshes are re-cut, so
  vertex and index counts change in these sections only (body lod0 33535 of 41801 vertices still equal by index);
- FM4 main sections carry extra LOD lists (body/2, ...) that FH dropped on some parts;
- cockpit gauges: FM4 `gauge_emissive + gauge_needle`, FH `gauge_emissive` only (one `z_gauge_emissive_alpha`).

**So, for a port INTO FH:** mesh, UVs, indices and nearly every block carry over with fixed word changes; the work is
(1) the header (zero block, the two float quadruples, light records), (2) buffer IDs, (3) the tail scale, (4) damage
words in FH's own format (section 4d: FM4 unit 10:11:11 -> FH outward push 11:11:10, not yet solved), (5) the lamp
materials FH expects (FM4 head_light / tail_light -> FH names, or meshes split), (6) headlights moved to the cockpit
file, (7) the FH-only files (physicsdefinition.bin: hull + spheres + 30 floats, see ToFH\PHYSICSDEFINITION.md;
interior_emissive_lod0.xds; carattribs v21; stripped_* not needed by the port), (8) an FH DEV game folder to test.

## 8. FH light records decoded (2026-11-27, all FH1 + FH2 cars)

Harnesses `E:\DEV\fm4dev\ToFH\tools\lightrec.cs` (census: every type-5 main carbin, records + the nearest lamp material
of the model) and `lightstat.cs` (crosstabs); outputs `E:\DEV\fm4dev\ToFH\lights\` (`lights.tsv` one row per record,
`lightrec.txt`, `lightstat.txt`). 392 cars, 5304 records, 4 to 24 per car (12 most often). Only the main carbin has them.

Header: u32 E at 0x154, E records of 0x20 bytes from 0x158 (then 12 bytes `11 10 1`, then the FM4-style record table).

| Offset | Type | Meaning | Evidence |
|---|---|---|---|
| 0, 4, 8 | float x, y, z | lamp position: model x, model y, **minus model z** (front lamps z > 0) | with z negated the nearest lamp vertex is at 1.6 cm median (5161 / 5304 within 10 cm), 18 cm as stored; side check below |
| 12, 14, 16 | 3 x S16 | beam direction, unit vector (length 1.0000 on 5304 / 5304); z > 0 front, < 0 rear; y ~0; x mostly 0, else outward (379 / 393) | |
| 18 | u8 | element number 0-5 within a group (same value left and right) | |
| 19 | u8 | headlight kind on front type-01 records: 1 hidhead, 2 xenonhead, 3 oldhead (nearest lamp material); 4 = the road beam (b25 0xB3, b26 7, cone 30/70 on most). Rear: 4 on position lamps (type 01), 0 otherwise | |
| 20 | u8 | side: 1 left (x < 0), 2 right | 220 / 225 and 228 / 231 against lamp materials naming a side; the only byte that differs in 2393 / 2404 mirror pairs |
| 21 | u8 bits | type: 01 front = headlight, rear = position lamp (dim red 640000); 02 and 04 rear = bright red lamps (two groups, near taillight2S / tail_light; not told apart yet: brake / tail?); 08 = indicator (amber 8C3C14, near indicator_left / right); 10 = reverse (white 808080 / 7C7C7C, near reverse_light 209 / 230) | |
| 22, 23, 24 | u8 RGB | colour: 9B8C5F / A0A08C warm white, DCDCFF / 9696FF blue-white (xenon), E6E67D, FF0000 / C80000 red, 640000 dim red, 8C3C14 amber, 808080 white | |
| 25 | u8 | strength / reach (unit unknown): 0xB3 front road beam, 0x39 rear position lamps, 0x0A-0x26 the others | |
| 26 | u8 | 1-7 on most records (0 rare): probably the glow texture `media\carlights\headlight_glow_<n>.xds` (7 files); 1 on xenon, 2 on oldhead, 7 on beams and position lamps, 5 brake, 6 indicator / reverse | hypothesis |
| 27, 28, 29 | 3 x S8 | small offset, 0 on ~80 %; x component mirrored, pointing inwards (767 / 878) | |
| 30, 31 | u8, u8 | cone inner / outer angle in degrees (30/50 most, 30/70 road beam); 255/255 = no cone (glow only) | |

Order in the file: grouped by type, each group left/right in pairs; front headlights first, rear lamps, and a last front
group (the road beam, b19 4) at the end on most cars.

For a port INTO FH: one record per lamp from the FM4 model (head_light / tail_light / reverse / indicator sections or
the FH lamp materials after a material review), position (x, y, -z), direction (0, 0, +-1), side by x sign, type,
colour and cone from the FH typical values above. Open: 02 vs 04, b25 unit, b26 confirmation, b27-29, in-game test.

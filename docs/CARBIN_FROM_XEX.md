# How FM4 reads a .carbin (traced in default.xex, 2026-11-07)

What the game's own code says about the car model format, read from the PowerPC code of `default.xex` (image based
at 0x82000000; addresses below are virtual addresses in that image). Every field was then checked on the 80 official
FM4 DLC car carbins (17 cars, 1502 sections, 13667 subsections): 100% match. Research harnesses live outside the
repo (`fm4dev\XexCars`: `xexcars` PPC disassembler with `strs` / `vtbl` / `mulli` / `imm32` / `decls`, `carser`,
`gaugecheck`, `gaugetest`, `axisconv`; `fm4dev\Shaders\tools\fxdump <fxobj> vfetch`).

Earlier byte-level findings (positions, normals, strips, tail8 = SH0, wheel morph table) are in the memory notes and
`FH_vs_FM4_CARBIN.md`; this file gives the **names and meaning** the game uses for the blocks we only copied before.

## 1. The format is a serialized object tree

A carbin is the output of a generic serializer (`IOSys::CBinaryVersionedStream`, vtable 82157EAC). Every object starts
with `BeginObject(name, version)`, which in this stream **reads a u32 Version from the file** (8295E3D8): these are the
"version words" `CarbinModel` kept as opaque bytes. Field names are ignored in the binary form; the code branches on
the version it read, so older files still load.

Archive methods (CBinaryStream vtable 82158A1C): +0x74 string (u32 length + chars), +0x78/+0x94/+0xA4 2 bytes,
+0x7C/+0x88/+0x90/+0xA0 4 bytes (+0x88 = float), +0x84/+0x8C/+0x9C 8 bytes, +0x98/+0xA8 1 byte, +0x80 bool (1 byte),
+0x68 vec4 (16 bytes), +0x6C 4x4 matrix (64 bytes), +0xAC raw block, +0xB0 Begin (u32 version), +0xB4 End (nothing).
A container (`<name>_Container`) = Begin (version 1) + u32 count + elements.

Classes (RTTI): `CCarModel` / `CCarBodyModel` / `CBaseCarModel`, **`CCarSubModel` = our section**, **`CCarMesh` = our
subsection**, `CUberPackedVertex` (the 32-byte vertex), `CCarPartData`, `CCarMaterialData`, `CCarModelData`,
`CBaseCarModelData`, `CCarEmitterData`, `CDriverControls`, `CTireModel`, `CBrakePartModel`.

## 2. Header (car models, our "type 2")

| offset | game field | notes |
|---|---|---|
| 0x000 | `CCarModelData` version = **2** | our "Type" word is this version |
| 0x004 | 4 x 4x4 matrix | no names (empty field names, 8286D930). Main files: only row 0 set, four corner-like records (about 0.95, 1.1, +-0.1, +-0.42); lod0 / cockpit: zero. Meaning unknown |
| 0x104 | 5 x vec4 (version > 1) | no names. Cockpit only: a point at head height (about 0, 1.0, -0.1..-0.4), three unit axes, half sizes (0.12, 0.03, = -point z): an oriented box in front of the driver (hypothesis); zero elsewhere |
| 0x154 | `CBaseCarModelData` version = **0x10** | our "format word" (FH 0x11, FM3 0xD) |
| 0x158 | ShaderVersion (version >= 9) | 16 in all files |
| 0x15C | Emitters container: version 1, count n | our "records" |
| ... | n x `CCarEmitterData`, 140 bytes: version 2, type, parent, location matrix, location2 matrix | exhaust / backfire emitters |
| | `CDriverControls` version 3 + 16 vec4 | SteeringWheelPosition / Axis, Left / RightHandAnchor, ButtAnchor, ShifterAnchor (vectors 0-5: set in the main file, zero in the cockpit but the axis), Right / LeftHandOuter / Inner / Center (6-11, the cockpit hand grips), SteeringPivotLOD0, SteeringWheelAxisLOD0, ButtAnchorPositionLOD0, ShifterAnchorPositionLOD0 (12-15; SteeringWheelPositionLOD0 only in version 2, so version 3 has 16). `CarbinModel.DriverControlNames`, DRIVER tab |
| | StartLOD, EndLOD (version >= 9) | main 1..5, lod0 / cockpit 0..0 (older versions: bool HasLOD0 + count) |
| | **Gauges**: 15 x (GaugePivot vec4, GaugeAxis vec4, MaxRotation float) | see section 6 |
| | 2 floats (version >= 8) | empty field names in the code; about -0.34 / 0.675, slightly different per car and file |
| | u32 (version >= 0xE) | 1 |
| | BoneMappingTable (version >= 0xF): version 1, count 0 | |
| | 8 bytes | 00000004 00000004 in every file; CBaseCarModelData ends before them (82880A68), owner not traced |

Then the draw lists, then the SubModel container (our `BeforeSections` = its version 1, then the section count).
FH files have one more container (count at 0x154 of 0x20-byte records) before `CBaseCarModelData`; once the port
drops it, the rest of an FH header is the FM4 layout (walk checked on FH2 Supra and Delta S4).

## 3. Section = `CCarSubModel` (82901B58)

| CarbinModel field | game field |
|---|---|
| Marker (5) | `CCarPartData` version (FM3: 2) |
| Bounds (9 floats) | origin, min, max |
| Unk1c float 0 (1.0) | a float the game clamps to a minimum (8287FC30) |
| Unk1c floats 1-3 | mean vertex position |
| Unk1c float 4 | 0 or 0.2; 0.2 only on mirrors, wings, bumperRa, exhausta (parts that can fall off) - hypothesis |
| Unk1c words 5, 6 | GlassParticles version 1, Vertices container version 1 |
| ACount + A (16 bytes each) | **GlassParticles.Vertices** (vec4): the shattered-glass mesh |
| AfterA | Indices container version 1 |
| BCount + B (2 bytes each) | **GlassParticles.Indices** (u16 triangles, count % 3 = 0). Only the glass sections of the main file |
| Pre[0] | `CCarPartData` bool: 1 on all glass / lexan, most headlights, some body parts - probably "has glass" (hypothesis) |
| Pre[1..3] + VbTest1[0] | u32 (version >= 3), 0 everywhere |
| VbTest1[1..3] + VbTest2[0] | TSubModel version 1 |
| VbTest2[1..3] + name length | the name is a u32-length string (the 1-byte read works because the high bytes are 0) |
| PostName | VertexBuffer version 2 |
| AfterLod + subsection count | Mesh container version 1 + count |
| AfterSubs | VertexBuffer_LOD0 version 2 |
| Post9 | MorphTargets version 1, **HasMorphs** bool (our "deformable" flag), Morph version 2 |
| DamageLod | **Morph**: morph target of the LOD buffer |
| Mid | Morph_LOD0 version 2 |
| DamageLod0 | **Morph_LOD0** |

## 4. Subsection = `CCarMesh` (828EDE70 -> 8288FBD8)

| CarbinModel field | game field |
|---|---|
| Pre5 + Uv[8] + Fixed36[0..31] | `CCarMaterialData`: version 3, RemoveFromShadow bool, 4 x vec4 (UV offset / scale + 2 unused) |
| Fixed36[32..35] | `CCarMeshBase` version 2 |
| Name, Lod | Name, LOD |
| IndexType (6) | PrimitiveType (6 = strip; one DLC file has 4 = list) |
| Fixed56[0..3] | MaterialIndex (0 everywhere) |
| Fixed56[4..55] | UnpackingData: version 1, PositionOffset, PositionScale, UVOffsetScale (vec4 each) |
| Three | IndexBuffer version 3 (then Count, Format = index size) |
| Tail | meshFlags, 0 everywhere |

Trailer (24 bytes): `00000001 00000000` three times in every file: three empty containers - hypothesis.

## 5. The damage word = morph direction

- The car vertex declaration (in the .fxobj) has stream 0 only; the vertex shader fetches the morph itself:
  `vfetch` from constant vf90, **stride 4 bytes, Xenos format 10_11_11, signed normalized -> xyz** (12 car vertex
  shaders of `shaders_v16.fxobj`). Wheels use the same vf90 with 16 bytes, 16_16_16_16_FLOAT: the wheel morph table.
- Bits: X = 0-10, Y = 11-21, Z = 22-31, two's complement (`CarbinModel.NormalizeDamage` already packs this layout).
- In the deformable vertices of the DLC files the vector has a length from 0 to 1 (mostly zero or about 1, see
  section 7): a **direction with a weight**; the amount comes from shader constants (`MorphDamageScale`, `damageClamps`...). It is not the normal nor a direction
  to the car or part centre: it cannot be computed from the shape (copying from the nearest original vertex stays the
  rule). How the shader scales it is not decoded (needs a Xenos ALU disassembler).

## 6. Analogue gauges (used by the GAUGES tab and the port, 2026-11-07)

- Gauge table = the 15 records above, order Speed, Tach, Fuel, Boost, OilTemp, OilPressure, WaterTemp, Volt,
  GaugeA-F, Power (the `carattribs.xml` names; CarAttribs version >= 16 reads all 15 by name, 829006A0).
- The game turns the section **`<name>_min`** (82906630 looks up `<name>_min*`; `_mid` / `_max` never appear in the
  code): about GaugePivot by needle position x MaxRotation.
- **The axis is stored as (-x, -y, z) of the model axis.** Read that way it is the dial normal (0.9 degrees mean on
  every FM4 car), and turning `_min` by +MaxRotation (right-hand rule) lands on `_max` at 0.0-1.8 mm and by half on
  `_mid`: the artist's `_mid` / `_max` are exactly half and full scale.
- `carattribs.xml` `<Gauges><Speed NumTicks="n"><TickMark Param="needle 0..1" Value="reading"/>`: speed in m/s, tach in
  rpm; the marks are a vector with no limit. FM4 files have 0 marks (the reading used as it is: fuel, boost) or at
  least 2, never 1. **One mark = a needle that spins or vanishes** (FH2: BMW M6 13, Viper GTS ACR 99, Ferrari F142,
  Delta S4, McLaren F1 93; FH1 also DS3 Racing): the port now writes `NumTicks="0"` for those (`CarPort.ConvertCarAttributes`).
- **Source data can be off**: the ACU_NSX_97 speed axis is 19.6 degrees off the plane of its three needles (T10 swapped
  its X and Y; tach 2.3, fuel 4.1). Turned about it in the viewport the needle sweeps a cone into the dial; in game,
  seen from the cockpit, it looks right (whether the game draws needles over the dial or corrects the axis is not
  traced). `CarGauges.AxisTilt` measures it; above 5 degrees the GAUGES tab and `CarbinValidator` (Check Model Format,
  deep DLC check, port report) give a warning pointing at Fit From Poses + "Fit also the axis". Of 8 FM4 cars
  (88 files) only the NSX is flagged.
- Code: `CarGauges.cs` (read / write, tick marks, Fit From Poses, axis tilt), `ArchiveEditorForm.Gauges.cs` (viewport tab).

## 7. Done on 2026-11-07 (second pass) and still open

- **Names in the code**: `CarbinModel.Names.cs` reads and writes the raw blocks under the game's names
  (`MaterialIndex`, `PositionOffset` / `PositionScale`, `RemoveFromShadow`, `GlassVertices` / `GlassIndices`,
  `HasMorphs`, `Morph` / `MorphLod0`, the object versions, `HeaderLayout.Walk` for the header, `DriverControlNames`).
  The raw fields stay (byte-identical round trip, FH / FM3 variants). Checked on the 240 DLC carbins: every value as
  in the tables above, the header walk ends at the draw lists on all 60 car files.
- Measured on those 240 files: UnpackingData is always the identity (offset 0, scale 1, UV 0 0 1 1), the two spare
  CCarMaterialData vec4 are zero, RemoveFromShadow off, MaterialIndex 0, meshFlags 0, emitters type 0 / parent -1.
- **Morph words correction**: the vector is not only zero or unit length. Of the 2.8 million words: 53% below 0.1
  (mostly zero), 38% length 0.9-1.0, the rest spread over 0.1-0.9, never above 1.05. So a length up to 1 (the shader
  scales it), and copying from the nearest original vertex stays the rule.
- **Validator** `CarbinValidator.cs`: object versions, header walk and its values, emitters, StartLOD / EndLOD per file
  kind, trailer, glass particles (indices % 3, in range, main file only), HasMorphs against the morph tables (counts
  and stride), morph words longer than 1, mesh fields, PrimitiveType, UnpackingData identity, index range. No finding on
  the 240 DLC files, on 7 base-game cars (77 files) and on the 12 FH2 ports of `dev_leftovers` (132 files). Used by
  Tools > Check Model Format, the deep DLC check and the port report ("model format check").
- **Glass particles** `CarbinGlass.cs`: only next to LOD1 in the main file, w = 1, on the glass surface, small quads
  (edge median 5-7 cm); the windscreen glassF never has any (FM3, FH, FM4 alike); FM3, FH1, FH2 and FM4 share the
  same pattern for the same car, so ports carry them. Replacing a glass part's geometry used to clear them (no
  shards any more); now they are rebuilt from the new LOD1 triangles (grid of about 6 cm, fewer triangles than T10's
  overlapping meshes). In-game check (2026-11-07, ACU_NSX_97 glassRF round trip): the file is right (354 vertices /
  1659 indices rebuilt, the other glasses unchanged, no validator finding), but on Xenia no glass breaks on any car,
  original ones included, so the shards cannot be seen there (emulation); to see on real hardware if ever possible.
- DRIVER tab and EXHAUST tab use the game names.
- Still unknown: the meaning of the 4 matrices + 5 vec4 (described in section 2), of the 2 floats (empty field
  names), the owner of the last 8 header bytes and of the trailer, the shader's morph scaling, wheels (type 3) and
  brakes (type 1) traced the same way (their fields already pass the same checks).

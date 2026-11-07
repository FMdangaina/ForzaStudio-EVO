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
| 0x004 | 4 x 4x4 matrix | no names in the code |
| 0x104 | 5 x vec4 (version > 1) | no names |
| 0x154 | `CBaseCarModelData` version = **0x10** | our "format word" (FH 0x11, FM3 0xD) |
| 0x158 | ShaderVersion (version >= 9) | 16 in all files |
| 0x15C | Emitters container: version 1, count n | our "records" |
| ... | n x `CCarEmitterData`, 140 bytes: version 2, type, parent, location matrix, location2 matrix | exhaust / backfire emitters |
| | `CDriverControls` version 3 + 16 vec4 | SteeringWheelPosition / Axis, Left / RightHandAnchor, ButtAnchor, ShifterAnchor, Right / LeftHandOuter / Inner / Center, ...LOD0 points (16 in the files; the code reading gives 15, one branch not settled) |
| | StartLOD, EndLOD (version >= 9) | main 1..5, lod0 / cockpit 0..0 |
| | **Gauges**: 15 x (GaugePivot vec4, GaugeAxis vec4, MaxRotation float) | see section 6 |
| | 2 floats (version >= 8) | about -0.34 / 0.675 on every car, meaning unknown |
| | u32 (version >= 0xE) | 1 |
| | BoneMappingTable (version >= 0xF): version 1, count 0 | |
| | 8 bytes | not traced yet |

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
- In the 1.5 million deformable vertices of the DLC files the vector is either zero or of length 1: a **direction**;
  the amount comes from shader constants (`MorphDamageScale`, `damageClamps`...). It is not the normal nor a direction
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
- Code: `CarGauges.cs` (read / write, tick marks, Fit From Poses), `ArchiveEditorForm.Gauges.cs` (viewport tab).

## 7. To improve next (not done)

- Rename `CarbinModel`'s opaque blocks after these names (`Fixed56` -> MaterialIndex + UnpackingData, `Post9` ->
  MorphTargets, A / B -> GlassParticles...): same bytes, readable code, the base for a carbin written from scratch.
- Validator checks: object versions, HasMorphs vs morph tables, glass indices % 3, header ending where the draw lists
  start, PrimitiveType 6, morph words zero or unit length.
- Glass particles for imported / ported glass that has none (format known: vec4 vertices + u16 triangles).
- Header editors for the remaining fields with real names: emitters (EXHAUST tab), driver controls (DRIVER tab labels).
- Still unknown: the 4 matrices + 5 vec4 at the file start, the 2 floats and last 8 bytes of the header, the trailer
  owner, the shader's morph scaling, wheels (type 3) and brakes (type 1) traced the same way.

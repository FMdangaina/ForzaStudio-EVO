# The .carbin format in every Xbox 360 Forza (FM2, FM3, FM4, FH1, FH2)

One description for the five games, written 2026-11-10. It joins what the project had measured game by game
(`FH_vs_FM4_CARBIN.md`, `CARBIN_FROM_XEX.md`, the FM3 and FM2 notes) with a 010 Editor template for FH2 car models
that was shared with the project: the template names every field of the FH2 file, and its version checks turned out
to read FM3 and FM4 too. Every statement below was checked on all the `.carbin` files of the extracted games (car
zips, wheel zips, loose brakes), read to the last byte:

| Game | Files read | |
|---|---|---|
| FH2 | 5283 of 5283 | |
| FH1 | 4183 of 4183 | same versions as FH2 everywhere |
| FM4 | 4045 of 4045 | |
| FM3 | 3930 of 3930 | |
| FM2 | 785 of 789 | three wheels in a later form and one junk file are not read (section 4) |

In the program: `CarbinFormat.cs` (which game and kind a file is, the version table), `CarbinModel.cs` (FM3, FM4,
FH: read, edit, write), `Fm2CarbinModel.cs` (FM2: read and write byte for byte).

## 1. One object tree, five sets of versions

A carbin is a serialized object tree. Every object starts with a `u32` version and the game's reader branches on it,
so a newer game still loads the older form. The games differ only in the versions they write and in the fields those
versions add.

```
header            body (car), wheel or brake
ModelSettings     emitters (exhaust tips), driver anchors, LOD range, gauges, lighting, bones
Model
  DrawOrder       per LOD the transparent draw lists, then per LOD the opaque ones (u16 part, u16 mesh)
  Parts           physics, glass shards, name, vertex buffer, meshes, LOD0 vertex buffer, damage morphs
  MaterialSets, ShaderFileNames, MeshNames      three lists, empty in every file (the "24-byte trailer")
```

| Object | FM2 | FM3 | FM4 | FH1 / FH2 | What the version adds |
|---|---|---|---|---|---|
| Body header (first word of the file) | 2 | 1 | 2 | 5 | wheel lighting (4 x 16 floats) always; 2: rear-view mirror box; 3+: light flares. FM2's "2" is wheel lighting only |
| Wheel header | 1 | 2 | 3 | 3 | 2: brake pivot; 3: tyre extents (12 floats) |
| Brake header | 1 | 1 | 1 | 1 | nothing but the version |
| ModelSettings | 713 / 714 | 13 | 16 | 17 | 13: 4 gauges; 14: bone count; 15: bone names; 16: 15 gauges |
| Shader version (in ModelSettings) | - | 11 | 16 | 16 | |
| Part physics | 713 | 2 | 5 | 5 | 2: LOD0 vertex buffer; 4: bone weight count; 5: damage morphs |
| Mesh material settings | 1 | 2 | 3 | 3 | 2: RemoveFromShadow; 3: four UV transforms |
| Mesh | 3 | 2 | 2 | 2 | 2: mesh flags (FM2's 3 is from its own older numbering) |
| Vertex buffer | 2 | 2 | 2 | 3 | 3: buffer flags + sharing key |
| Index buffer | 3 | 3 | 3 | 4 | 4: buffer flags + sharing key |
| Bytes per vertex (car parts) | 60 | 40 (wheels 44, brakes 16) | 32 | 28 | |
| Model, DrawOrder, glass shards, part, emitter | 4, 4, 1, 1, 2 | same | same | same | |

Telling the games apart (`CarbinFormat.Identify`, 100 % on the files above): the ModelSettings version found after the
header. 713 / 714 = FM2, 13 = FM3, 16 = FM4, 17 = FH. The header is a wheel when the first word is 3 (72 bytes) or 2
(24 bytes) and a ModelSettings version follows it, a brake when the first word is 1 and the version is at offset 4,
otherwise a body.

## 2. Names: ours and the format's

`CarbinModel` grew from byte measurements, so its fields have working names. What they are:

| CarbinModel | In the format |
|---|---|
| `Type` (1, 2, 3, 5) | the header version: not a file type. The same number means different things per game (table above) |
| Head | header + ModelSettings |
| draw lists | DrawOrder: transparent pass first, then opaque |
| Section / SubSection | Part / Mesh |
| `Marker` | part physics version |
| `Unk1c` | mass, centre of mass (3 floats), detach damage, then two glass shard versions |
| arrays A / B | glass shards: vertices (vec4) and indices (u16) |
| `Pre[0]` | HasTransparentMeshes |
| `Lod` / `Lod0` | Vertices / VerticesLOD0 |
| `Post9` | damage morphs version, HasDamageMorphs (the "deformable" byte), first table version |
| `DamageLod` / `DamageLod0` | DamageMorph / DamageMorphLOD0 (4 bytes per vertex; wheels 16 bytes) |
| FH flag + ID before a buffer | BufferFlags (bit 0 KeyValid, bit 1 DataStripped) + SharingKey |
| FH `IndexWord` 2 ("stripped") | DataStripped: count and stride are there, the data is not |
| `Fixed56` | MaterialIndex + Unpacking (position offset, position scale, UV offset / scale) |
| 12 floats in the FM4 / FH wheel header | TireExtents: lip min x, lip max x, tyre min x, tyre max x, rim max radius, tyre max radius, two values each |
| 24-byte trailer | the three empty lists |

Light flare (FH body header, 27 to 32 bytes): position, direction (3 x int16), index, bulb type, location bits
(left, right, front, rear), function bits (headlight, taillight, brake, indicator, reverse), colour, size, texture
index; from version 4 a damage position, from 5 two fade angles.

## 3. Vertices

| Game | Stride | Layout |
|---|---|---|
| FH1 / FH2 | 28 | as FM4 with a 4-byte lighting tail (SNORM8) |
| FM4 | 32 | position 4 x int16 (unit direction, radius / S), UV0, UV1 (u16), tangent frame quaternion (4 x int16), lighting SH0 (4 x SNORM16) |
| FM3 | 40 | position 4 halves (unit direction, radius), UV0, UV1 (halves), normal 11:11:10, lighting (8 bytes), **damage word (+28)**, 4 bytes unknown, bitangent 11:11:10 |
| FM3 | 44 / 16 | wheels / brakes: position, UV0, normal (+ lighting and 20 bytes on wheels) |
| FM2 | 60 | position 4 halves, UV0 (sign + magnitude / 1310.72), UV1 (always 0), normal 11:11:10, **damage word (+20)**, 4 bytes unknown, lighting 16 x SNORM16 |

FM2's lighting is 16 values whose mean size falls in steps of 1, 3, 5, 7 values (0.44 / 0.21-0.27 / 0.08-0.13 / 0.03):
baked spherical harmonics, bands 0 to 3. FM4 keeps only the first four (its SH0: a level and a direction).

## 4. FM2

The same tree in its oldest form (`Fm2CarbinModel`):

- ModelSettings ends after the driver anchors (version 1: 6 vec4). No LOD range, gauges, lighting values or bones.
- **No part has vertices of its own.** Every part's vertex buffer is empty; the vertices of all parts are in two
  buffers after the part list. A part with HasTransparentMeshes = 1 uses the first, the others the second.
- Each part stores `FirstVertex` and `LastVertex` (u16) of its range in that buffer. Mesh indices count from
  FirstVertex: the highest index is LastVertex - FirstVertex on all 11057 parts, and the ranges follow each other in
  part order.
- A mesh is: material settings version 1, mesh version 3, material name, primitive (6 strip, 0xFFFF restart), LOD
  (1 to 5), index buffer. No material index, unpacking data or mesh flags.
- No LOD0 buffer and no damage morph tables: the damage word is inside the vertex.
- A filled vertex buffer has the marker `000DECAF` before and after its data.
- The file ends after the second buffer: no material / shader / mesh name lists.
- LOD count: 5 on cars and wheels, 1 on brakes. Brakes and wheels have the same header (first word 1).

Not read: `5zigen_hyper_5zr_02`, `enkei_rs5`, `mht_crossover_wheel` (wheels with ModelSettings 3 or 4 and part
physics 3: a later form, between FM2 and FM3), `brakes\Circle01` (junk), and the decal carbins of `Livery.zip`
(another kind of file). One wheel (`por_550_56`) has ModelSettings 713 and reads the same.

## 5. The damage word

Four bytes per vertex of a deformable part. Every game packs three signed fields the same way: **X in bits 0-10, Y in
bits 11-21, Z in bits 22-31** (the Xenos 10_11_11 format the car vertex shaders fetch). The meaning differs:

| | FH1 / FH2 | FM4 (and FM3, FM2 inside the vertex) |
|---|---|---|
| The vector is | the crushed **offset** of the vertex: field / 1023 (Z / 511) x 2 metres | a **direction** of length 0 to 1 (a weight; above 0.9 on 86 % of the vertices) |
| "Does not move" | `00000000` (19 % of the vertices) | `000003FF` = (1, 0, 0); FH has 0 on 91 % of these |
| Size | 7 cm on average, towards the middle of the car (mean cos 0.73) | the amount comes from the shader |

Measured on 25 cars both FM4 and FH1 have (2.9 million vertices at the same position; FH2 gives the same numbers):
the FM4 direction lies on the line of the FH offset on 97 % of the vertices whose FH offset is above 2 cm, the same
way on 55 % and the opposite way on 42 %. The sign follows no rule we found: not the axis, the side of the car or the
part (small parts have one sign throughout, the body is mixed by area).

So an FH word read by FM4 is a direction of length about 0.03: the car hardly deforms, which is what the first FH
ports did.

The sign is not what the game depends on: FM4's own Supra changes sign between neighbouring vertices (5.1 % of the pairs
within 2 cm, 1 % of the vertices at the very same point on seams) and does not tear. FM4 never writes a zero word.

**Converting FH words does not work (tested in game 2026-11-10, FH1 DLC Ford GTX1, DLC pack):** both the word-by-word
direction and a version smoothed over 5 cm with `000003FF` for FH's zeros made the car explode on a crash (shards metres
long); the same car with words from an FM4 car deforms well. The GTX1's FH offsets are small and noisy (median 1 cm, 38 %
zero) where the Supra's are large (median 15 cm, 3 % zero), but why the game explodes is not known. The conversion code
was removed: a ported FH car always takes its words from an FM4 car (`Port.ini [Damage] Words = donor` or `donorcar`,
`CarbinModel.BorrowDamageWords`); a part that car lacks takes the words of the nearest vertex of any of its deformable
parts, in the place of the whole deformable model.

Correction: until 2026-11-10 the code and these docs read the FH word one bit off (X as the top 11 bits, Z as the low 10)
and so reported "length about 90" and no rule between the two games.

Wheels use the same slot with 16 bytes per vertex: two positions as halves (y / L, x / L, L, z / L), the rim at its
largest size and the vertex itself (see the wheel notes in `FH_vs_FM4_CARBIN.md` and `TODO.md` topic 10).

## 6. Towards "every game to every game" (goal 18)

One reader and one writer per game. What this document gives each of them:

| Game | Reader | Writer | Still missing for a writer |
|---|---|---|---|
| FM4 | `CarbinModel` | `CarbinModel` | - |
| FH1 / FH2 | `CarbinModel` | bytes round trip; no FM4 -> FH conversion | versions and stride from the table, buffer flags + sharing keys, light flares in the header, damage offsets (`DamageWord.Motorsport4ToHorizon`: length and sign to choose) |
| FM3 | `CarbinModel` (opt-in) | `CarbinModelToMotorsport3` | - |
| FM2 | `Fm2CarbinModel` (bytes and structure; positions and damage decoded) | bytes round trip; no conversion | building the two shared buffers from parts (ranges, relative indices), the 60-byte vertex (UV0 rule, 16 lighting values, the unknown word), FM4's LOD 0 has no place |

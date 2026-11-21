# Ports between all games: plan (2026-11-03)

Goal (user, 2026-11-03): every game must be able to go to every other one, each keeping its own characteristics.
Games: FM2, FM3, FM4, FH1, FH2 (FM1 is read-only, see `docs/` FM1 notes). Reference car: **Supra RZ '98**
(`TOY_SupraRZ_98`, Id 461 in all five games).

> **State 2026-11-17: paused** since 2026-11-03 (`ROADMAP.md` goal 18); the matrix below is still 5 of 20. Done since
> then, and useful here: one description of the `.carbin` of the five games (`CARBIN_ALL_GAMES.md`, 2026-11-10, its
> section 6 is about this plan) and how FM4 reads it (`CARBIN_FROM_XEX.md`); the editor now works inside FM2 and FM3
> (Sprint 2, `ROADMAP.md` goal 21: materials, Duplicate Car, packs), which is not a port but uses the same readers and
> writers; each game's PI and ratings (`FM2/`, `FM3/`, `FM4/PI_AND_RATINGS.md`); the driver's two sets of points
> (`DRIVER_POSITION.md`).

> **State 2026-11-21: still paused, still 5 of 20** (FH1, FH2, FM3, FM2 -> FM4 and FM4 -> FM3). New since 2026-11-17,
> from Sprint 2 ST-03 (`ST03_PART_IMPORT.md`), all checked in game by the user:
> - **parts** already cross every way a whole car does not: Copy Part takes a part from a car of any of the five games
>   into an FM4, an FM3 or an FM2 car. So the per-part model writers exist: FM4-form part -> FM3 layout
>   (`CarbinModel.ConvertPartToMotorsport3`) and FM4-form part -> FM2 part (`Fm2CarbinModelEdit.FromMotorsport4`,
>   whole-number UV shifts, carbon fibre / 250). The FM2 **model** writer of section 3 is therefore mostly there; what a
>   whole-car port to FM2 still lacks is the data mapping back, the sound choice and cars added to FM2 showing up in
>   game (`FM2/DLCBETA.md`, paused).
> - **baked light** is no longer a donor's: FM2's 16 values are computed (PRT with light sent back), FM3 / FM4's 4
>   values are the first 4 of FM2's x 1.457, so an FM2 car keeps its own light in FM4 / FM3 and new geometry is lit on
>   the car (`CarbinModelLighting.cs`, `Fm2CarbinModelLighting.cs`).
> - `FM2_Extracted_DEV` exists (section 5 lists it as to create).
> - Rule set by the user: FM2 has no LOD 0 and no per-car brake models; a part goes lod0 -> lod0, cockpit -> cockpit,
>   LODs 1-5 -> LODs 1-5. A port INTO FM2 uses the source's LODs 1-5 only.
> - Estimate given to the user (sessions, this chat's guess): FH1 / FH2 -> FM3 2-3 (FH reader + FM3 writer chained in
>   memory, ids, makes, UI); FM2 -> FM3 2-3 after that; everything -> FM2 6-10, the most uncertain. Suggested order:
>   FH -> FM3, FM2 -> FM3, then -> FM2.

This is a plan, nothing is coded yet. Related documents: `PORT_FM4_TO_FM3.md` (the only reverse port so far),
`FH_vs_FM4_CARBIN.md`, `GR2_FH_vs_FM4.md`, `FM3/fm3_conversion.md`, `SHADERS_OTHER_GAMES.md`.

## 1. State of the matrix (source → target)

| From \ To | FM2 | FM3 | FM4 | FH1 | FH2 |
|---|---|---|---|---|---|
| **FM2** | – | ⬜ | ✅ | ⬜ | ⬜ |
| **FM3** | ⬜ | – | ✅ | ⬜ | ⬜ |
| **FM4** | ⬜ | ✅ | – | ⬜ | ⬜ |
| **FH1** | ⬜ | ⬜ | ✅ | – | ⬜ |
| **FH2** | ⬜ | ⬜ | ✅ | ⬜ | – |

5 of 20 directions exist: the four "to FM4" ports (`CarPort*.cs`, `Fm2Source.cs`, `CarbinModelMotorsport3.cs`) and
FM4 → FM3 (`CarPortToFm3.cs`, `CarbinModelToMotorsport3.cs`, `Fm3Textures.cs`).

FH1 and FH2 share models and tables (76 of 76 damage tables identical, same car zip), so the real number of formats is four:
**FM2, FM3, FM4, FH** (FH1/FH2 differ in the database and in what is installed).

## 2. Architecture: one reader and one writer per game

Twenty converters are not maintainable. Each game gets a **reader** (game → neutral car) and a **writer** (neutral car → game):
5 + 5 pieces instead of 20.

- Readers: the existing X → FM4 steps, reworked so that they do not stop at FM4's shape.
- Writers: `CarPortToFm3` and `CarbinModelToMotorsport3` become the FM3 writer; FH and FM2 writers are new.
- The neutral car is **not** "FM4 simplified". FM4 is richest for LOD0, cockpit and brakes, but FH has things FM4 lacks
  (`physicsdefinition.bin`, more gauges, `CarAttribs` v21), FM3 has its own digital dash, FM2 its own tuning data.
- Therefore the neutral car holds the common fields (geometry per part, materials, wheels, cameras, data rows by physical
  meaning, strings, sound) **plus a container with the source game's own data**. The container is used when source and target
  are the same family (FH1 ↔ FH2, probably FM3 ↔ FM4 for many parts): nothing is lost there.
- When the source has less than the target needs (FM2 has no LOD0, cockpit or brakes), the writer rebuilds the missing parts (the
  traffic-car close-up work) and says so in the report: what it kept, rebuilt, lost, or took from a donor.
- Rule order stays: source value first, then computed, donor last (`keep-source-data`).

## 3. Cost of each writer

| | FH (FH1 / FH2) | FM2 |
|---|---|---|
| Model | Carbin type 5. `CarbinModel` already reads and rewrites it byte for byte (648 files). Vertex bytes 0..23 equal FM4's; the tail goes from SNORM16×4 to SNORM8×4; flag + ID before each buffer; extra `IndexWord`. | 60-byte half-float vertices in two shared buffers after the parts (each part a vertex range). `Fm2CarbinModel` reads and rewrites it byte for byte (785 of 789 files, 2026-11-10; layout in `CARBIN_ALL_GAMES.md` section 4); the conversion into it is still to write. FM2 has one more top LOD than FM1; the damage word is FM4's direction, kept inside the vertex; 16 baked lighting values per vertex. |
| Data | gamedb close to FM4 (relation map in `GameDbSchema.cs`); `IdMax.FH1/FH2.json` in `docs/HZN`. | Old schema: 40 `Data_Car` columns, per-part upgrade tables, no `CarPartPositions` / `CameraOverrides` / `Data_CarBody`. The inverse of `Fm2CarData`, `Fm2CarUpgrades`, `Fm2CarPowertrain`. |
| Zip | Adds `stripped_*` twins (structure only, buffers empty, flag 3), `physicsdefinition.bin` (inertia, centre of mass, spheres, 38-vertex convex hull), `CarAttribs` v21. | Only main carbin + 4 xds + `CarAttribs` v4 + `ShaderSettings`: LOD0, cockpit, calipers and rotors are lost. |
| Sound | ET / HT like FM4, events in `caranimationevents.xml` (FM4 keeps them inside the gr2). | No tuning format: the car picks an FM2 car's sound. |
| Main risk | Damage words, `physicsdefinition.bin`, gr2 | Model writer, damage |

### Damage words towards FH (open)

Corrected 2026-11-10 (`CARBIN_ALL_GAMES.md` section 5): both games pack the word the same way (X bits 0-10,
Y 11-21, Z 22-31). FM4 word = a direction of length 0 to 1. FH word = the crushed offset of the vertex in metres (field / 1023 x 2),
7 cm on average, towards the middle of the car, on the line of the FM4 direction on 97 % of the vertices. (The earlier "length about
90, world = (h_y, h_z, −h_x)" came from reading the fields one bit off.) Two ways, try the rule first:

1. rule: the FM4 direction times a length (FH's mean is 7 cm), signed towards the middle of the car (towards FM4 the conversion made a car explode in game, 2026-11-10: test before relying on it);
2. donor words: per vertex from the nearest vertex of an FH car (as `CarbinModel` does for rebuilt parts).

### gr2 animations towards FH (open)

FH2 console gr2 are big-endian, Oodle1-compressed, tag 0x33; FM4's are little-endian, uncompressed, tag 0x38. The P1 DLC's FH2
files are little-endian, so the game may accept them: first try copying FM4's files unchanged, in game.

### Textures

Many Supra xds are identical between FM4 and FH (`nodamage*`, `interior_lod0`, `leather2_nrm`, `wood`); `damage*` and `lights*`
differ in content. FM3 ↔ FM4 colour textures differ by the PWL gamma curve (`xds-format-findings`). Check pixels, not bytes.

## 4. Verification (numbers first, game after)

- A → B → A returns A's file, except fields declared not representable (listed per pair, as done for FM3: 1.15 mm on halves,
  the dropped word, the Z axis of ~0.03 % of vertices).
- A → B is compared with B's own Supra.
- In game: the Supra first, then 5-6 cars. Never the whole list, never a batch without being asked.
- Test folders: `FM3_Extracted_DEV`, `FM4_Extracted_DEV` exist; to create: `FH1_Extracted_DEV`, `FH2_Extracted_DEV`,
  `FM2_Extracted_DEV` (copies of the `_Extracted` folders; those are never written). Check `tasklist` for xenia* first.

## 5. Phases

0. **Foundation.**
   - Pull everything that does not depend on the game out of `CarPortToFm3`: DB rows by schema, Id allocation
     (`GameIdLimits.json`), strings, thumbnail, manifest, install with the Xenia check and backup.
   - Define the neutral car and its container.
   - Create the three DEV folders.
   - Rebuild FM4 → FM3 on the new structure; it must give the same output as today (same method as the `CarPort` split:
     identical output on 12 ports).
1. **FH writer** (FH1, then FH2 as the database difference), source FM4, tests on the Supra.
2. **FM2 writer**: model, inverse data mapping, sound choice, rebuilt missing parts.
3. **Cross rounds**: FM3 → FH, FH → FM3, FM2 ↔ FM3, FM2 ↔ FH. Readers and writers already exist by then, so this is testing,
   not much new code. Check each one without going through FM4 (a pass through FM4 loses information).
4. **UI**: the pair menu (`PortPairs.cs`) and the window per target (`CarPortFm3Form.cs` is the model) extended to the matrix.

Suggested order of the writers: FM3 (done), FH, FM2: FH is much closer to FM4 and its model writer almost exists; FM2 is the most
expensive and gives the least (it loses LOD0, cockpit, brakes).

## 6. Open questions

- Where the games are tried: Xenia or an RGH console (especially FH2).
- Names of the DEV folders (`…_Extracted_DEV` proposed).
- Whether the matrix gets a ROADMAP topic (not added yet).

# ST-03 - Import parts from other models

Sprint 2 doc, section 6. Order agreed with the user (2026-11-15): FM4 first, then FM3, then FM2.

> **State 2026-11-21: closed on FM4, FM3 and FM2, checked by the user in the app and in game; commit pending.**
> This file is the history, day by day, with every finding and number: read it for the "why". For what works now, the
> user's checks, the commit list and the open points, read `ST03_RESUME_FM2.md`; for the user's guide, `MANUAL.md`
> section 5 "FM2 and FM3 cars". Notes below that say "pending", "OPEN" or "next" are as of their own date.
> Two later decisions override older notes here: lod0 / cockpit / brake parts into a main model are CANCELLED (the rule
> is lod0 -> lod0, cockpit -> cockpit, LODs 1-5 -> LODs 1-5), and FM2 has no LOD 0 and no per-car brake models.

## FM4 (2026-11-15)

Base: the existing "Copy Part From Another Car..." (ArchiveEditorForm.PartImport.cs). Added:

- `PartImport.cs`: the copy without UI. Everything is built and checked first; the archive gets the files only when
  every file passed (an import that stops half way changes nothing = rollback).
  Checks on every result file (read back): draw lists, unique part names, the part has the source's materials and
  vertex counts, the replaced part is gone, LOD coverage = the source part's LODs over all files, source bytes unchanged.
- Placement: center of the source part -> center of the replaced part (origin + (min+max)/2), optional
  "Also scale it to the replaced part's size (same proportions)" (longest side ratio).
- USER RULE: the move / scale is never applied to every LOD silently. When a part is moved the app asks
  "move on which LODs?" with one tick per model file (main / lod0 / cockpit) and the LODs the part draws there;
  unticked = the copy stays where it is in the source car.
- Preview: the full log (removed parts, imported materials / vertices, moves per file, LODs, warnings such as
  materials this car does not use) is shown before anything changes; Apply or Cancel.
- Log: appended to `PartImport.log` next to the exe. Files stay pending until Save (Revert takes them back).

Harness: `E:\DEV\fm4dev\ST03\pi.exe <FM4 root> [cars] [seed]` (read-only). 10 random target cars, random source
car, a part both have; runs A move on all files, B move only main, C as new part, D scaled; negatives (missing part,
missing replaced part, name clash -> exception, no result). 2026-11-15: runs 70, fails 0 (pi_10.txt).

Open:
- Part brand (sprint: "costruttore/brand della parte", default = target car's make): meaning to confirm with the user.
- In-game test (sprint operator list): two cars, simple part, position / orientation / scale, materials, 6 LODs,
  source car unchanged.
- Then FM3, then FM2 (CarbinModel refuses both today).

## Part brand (2026-11-15, user: FM3 Cayenne front bumper "EDO" missing in the FM4 port)

Meaning (user): the upgrade part's maker = name string + logo .xds. Findings (harnesses E:\DEV\fm4dev\ST03\brand, read-only):
- Upgrade rows (List_UpgradeCarBody*, List_Upgrade*...) have ManufacturerId -> List_PartManufacturer(Id, PartManufacturer
  "_&<table<<16|key>", LogoPath "decalNNN"). Same schema and same ids in FM3 and FM4.
- FM3 Cayenne (1050) front bumper upgrades: 356 Edo Competition (decal122), 152 Gemballa, 162 Hamann, 492 Forza Motorsport.
  EDO (356) is used ONLY by this row in FM3 and by no FM4 car.
- FM4 still has row 356 and the string IDS_PartManufacturer_356 "Edo Competition" (List_PartManufacturer.str).
- FM4 lacks the logo: UI\Textures.zip UI4/Logos/Car_Parts_Logos(+_SM)/decal122.xds. FM3 has 437 logo files per folder
  set, FM4 221: 216 FM3 logos are gone in FM4 (lists logos_FM3/FM4.txt), FM4 added decal438-476.
- Logo format: FM3 linear (fetch byte 02), FM4 gamma (FE) like car textures; _SM same size (61492); the big one is
  smaller in FM4 (114740 vs 172084, different height) -> an FM3 big logo may need resizing.
Proposal: when an FM3 part / car goes to FM4, for every ManufacturerId used: row + string present? else add; logo
present in FM4 Textures.zip? else copy the FM3 pair converted (ConvertMotorsport3Texture) - in the game folder, pack
location to be checked.

### Coded 2026-11-15 (user: game folder)
- `PartLogos.cs`: CarManufacturers(db, car) = ManufacturerID of the car's List_Upgrade* rows (Ordinal, or through its
  car body / engines / motors; List_UpgradeDrivetrain skipped: its ManufacturerId is the CAR make). Fm3ToFm4(logo, size):
  gamma conversion + format 0x06 -> 0xB2; the big FM3 220x184 goes to FM4's 220x123 with the drawn area kept at its size
  and centred (scaled down evenly only when it does not fit, never stretched).
- `CarPort.PortPartLogos` (after PortMake): every maker of the ported car must have its row and both logos in the
  target's UI\Textures.zip; missing logos are copied from the source game's Textures.zip (FM3: converted). Missing row
  = WARNING only (no invented names). Lines "part logo copied: ..." / "part makers: ..." in the port report.
- How FM4 itself resized its logos is not reproduced exactly (crop / stretch / uniform fits give error 55-77): ours is
  a faithful centring. Same-size logos: header equal, mean error 1.9 vs FM4's own.
- Harness brand\l10.exe (Cayenne + 10 random FM3 cars): 0 fails; Cayenne 23 makers, 36 logos missing in FM4 (18
  makers, not only EDO); one car (Lancer Evo VIII MR) has a maker with no FM4 row. Preview PNGs brand\edo_*.png.
- In-game check pending: re-port the Cayenne into FM4_DEV, upgrade shop front bumper shows the EDO logo.

## Parts missing in LOD0: copy from LOD 1 (2026-11-15, user request)

Many cars ported from FM2 have parts in LOD 1-5 (main .carbin) but not in the close-up model: up close they vanish.
- `Lod0Fill.cs` (no UI): parts of the main model missing from their close-up model. Target file as in FM4:
  cockpit parts (CarPort.CockpitPart: hood, interior, mirrors, seats, front glass, A pillars, cage, needles) go in
  `_cockpit` (user: "da lod1 a cockpit ci sono i cofani"), every other part in `_lod0`. The copy = the part's LOD 1
  made LOD 0 with CarbinModel.CloseUpFromLod1 (as the port builds whole close-ups), then ImportSection into the
  existing file. UVs of LOD 1 kept; light names -> *_lod0 variants (CarPort.LodVariant), tire -> inner_rim,
  cockpit interior -> leather (port rules). Checks: read back, draw lists, unique names, other parts unchanged,
  LOD 0 only, materials = LOD 1 count, vertex count = LOD 1, main bytes unchanged. Result only if all pass.
- UI: side panel "Parts Missing in LOD0: Copy From LOD1..." -> ticked list (part -> lod0/cockpit, its LODs and
  materials; parts without LOD 1 cannot be ticked), preview Apply/Cancel, PartImport.log, pending until Save.
- Reusable for future FM2 part imports: put the part (LOD 1-5) in the main model, then run Lod0Fill on it.
- Harness `E:\DEV\fm4dev\ST03\lod0fill\lf.exe <FM4 root>` (read-only), FM4_Extracted_DEV: 316 cars, 114 with
  missing parts, 111 runs, 230 parts copied, 0 fails (lf_dev.txt).
- NOTE: stock FM4 cars also lack many parts in lod0 (bumperRrace, bumperFrameF/R, some c/d variants): the game
  draws them only at distance. On a stock car the list is a choice, not a defect list.
- In-game check pending (an FM2-ported car: part visible up close, position, materials).

## PAUSED 2026-11-16

User: "fermiamo st per questo": ST-03 waits for the editor workflow work (TODO 15.3, `docs/UI_WORKFLOW.md`). The import
and LOD0-fill preview windows were removed in the first round and put back in the second (the user liked them); the
Copy Part one is reworked in workflow step C. Ctrl+Z undoes both, the report is in `PartImport.log`.
The LOD question for moved parts stays. Fill LOD0 does not add database rows yet (to be decided later).

2026-11-17: the workflow work is done (steps A to E, `docs/UI_WORKFLOW.md`; `TODO.md` 15.3 closed), so ST-03 can resume.
Sprint 2 is `ROADMAP.md` goal 21.

What is left of ST-03 when it resumes: the in-game checks above (FM4 import on two cars, the Cayenne's Edo logo, an
FM2-ported car up close), then part import for FM3, then FM2.

## RESUMED 2026-11-17 (workflow steps A-E closed)

SCOPE set by the user: copy parts FROM FH1 / FH2 into every Motorsport, and between the Motorsports in both
directions: 2 -> 3, 3 -> 2, 2 -> 4, 4 -> 2, 3 -> 4, 4 -> 3. Order: one pair at a time, each checked by the user.

| Source -> target | State |
|---|---|
| FM4 -> FM4 | done (2026-11-15), in-game check pending |
| FM3 -> FM4 | done 2026-11-17, OK in app + game (Abarth 500) |
| FH1 / FH2 -> FM4 | done 2026-11-17, OK in app + game (Abarth 500) |
| FM2 -> FM4 | done 2026-11-17, OK in app + game (user) |
| FM4 / FM3 / FH1 / FH2 / FM2 -> FM3 | done 2026-11-17 (see "FM3 as target"), harness pi4 OK, OK in app + game 2026-11-18 (FM3 Supra, 15/15) |
| FM2 / FM4 / FM3 / FH1 / FH2 -> FM2 | built 2026-11-18 (see "FM2 as target"), harness pi5 OK; app + in-game check pending. Next: lod0 / cockpit -> main (LOD0 -> LOD1 + reduced LODs 2-5) |

### FM3 -> FM4 (2026-11-17)

- `PartSource.cs`: `ForMotorsport4(files, notes, unknown)` gives the source car's model files ("" / _lod0 / _cockpit)
  in FM4 form. An FM4 car: as it is. An FM3 car: every file through `CarPort.ConvertModel`, the conversion of the
  FM3 -> FM4 car port (header, sections, vertices, FM4 material names, _lod0 light variants in the _lod0 file), so
  the copied part is what it would be in the ported car. FM2 / FH: still refused with the old messages.
- Copy Part (`ImportPartFromCar`): the source files are read once, converted in memory when the source game differs
  from this car's (target must be FM4), and the same bytes feed the part list, the viewport preview and PartImport.
  The source label gets the game name; the report starts with "source ... model converted from Forza Motorsport 3".
- A copied part carrying a material no FM4 car uses gets a WARNING (`UnknownMaterialWarnings`: "may render black,
  Rename Material in the tree"). No name is invented.
- Nothing of the FM3 car is written. Textures are NOT copied: the part uses this car's textures through its
  materials, as in FM4 -> FM4 (option "Use this car's texture regions").
- Harness `E:\DEV\fm4dev\ST03\fm3\pi3.exe <FM4 root> <FM3 root> [cars] [seed] [FM2 root] [FH root]` (read-only):
  10 random FM4_DEV targets x random FM3 sources, a part both have, runs A-D as pi.exe plus: every converted file
  re-reads as FM4 with the FM3 file's parts and vertex counts, an unmoved copy sits where the FM3 file has it,
  FM3 bytes unchanged, FM2 / FH sources refused. 2026-11-17: runs 72, fails 0 (pi3_10.txt). "All cars" run: ask.

### FH1 / FH2 -> FM4 (2026-11-17)

User rules: "deformazione di fh1 e 2 prende sempre da donor i punti" and "parti stripped non devono manco essere
considerate".

- `PartSource.ForMotorsport4(files, notes, unknown, target)`: an FH car goes through `CarPort.ConvertModel` as an FM3
  one. Before that, every stripped FH mesh (no index list, `SubSection.Stripped`) is removed, and a part made only of
  them goes (`WithoutStripped`); nothing is said about them. `stripped_*.carbin` files were never read (CarModelFiles).
- Damage words: an FH part never keeps its own words. The target car's model file of the same kind (main / _lod0 /
  _cockpit) is the reference, `DamageByShape = true` (another car: matched by place in the section box), as the FH ->
  FM4 port does with its donor. The report says where the words came from; when the target lacks that file (no
  _cockpit) the FH words stay and the report says the part may deform badly.
- Copy Part (`ImportPartFromCar`) passes this car's model files to PartSource. FH into an FH car is still refused.
- Harness `pi3.exe <FM4 root> <FH root> [cars] [seed]` (same pi3, the source root may be FM3 or FH): also checks no
  stripped mesh is left, parts with stripped meshes keep the others, and the damage note for each file.

### The open file decides (2026-11-17)

User: "quando chiedo di portare da auto devi permettermi di scegliere il file .carbin" (with the FM4 _lod0 open and
the FM3 8C as source only the main model's parts were listed: a seat or a steering wheel living in the cockpit could
not be copied), then "se apro lod0 copi da lod0, se apro cockpit copi da cockpit, se apro normale copi da normale",
and "solo nel file scelto".

- `ImportPartFromCar`: the kind of model file open in the viewer (main / _lod0 / _cockpit; main when none of them is
  open) is the one the part list, the preview and the copy use. The source car without that file: a message.
- `PartImport.OnlyFile`: parts, replaced part and placement are that file's; only that file is written. null keeps
  the old "every file that has the part" behaviour (harness only today).
- With one file there is no "move on which LODs" question: the move applies to the open file, the one chosen.
- F1 help GOOD TO KNOW: "Copy Part file", "Copy Part from FH".
- Harness pi3 run E: a random part of the source _lod0 / _cockpit copied with OnlyFile, only that file written,
  other parts untouched. 2026-11-17, 10 random targets each: FH1 90/0, FH2 90/0, FM3 91/0 (pih1_10.txt,
  pih2_10.txt, pi3_10b.txt); no FH car model file had stripped meshes (only stripped_*.carbin files have them).

### UV1 / UV2 never touched (2026-11-17, user: "si sballa il uv map quando portiamo pezzi da gioco a gioco... UV1 e UV2 non devono essere toccati")

- Cause: Copy Part had "Use this car's texture regions for its materials", ticked by default: `ImportSection` gave
  each copied subsection the UV offset / scale of this car's material of the same name, so UV1 and UV2 were squeezed
  into another layout (old exe in harness pi3: up to 4.8 UV units moved, 75-126 failing runs per game).
- Fix: the box is gone and `PartImport` always calls `ImportSection(..., false)`; the UV params and the raw UV bytes
  of a copied part are the source's. The "material not used by this car" warning now says the UVs are kept.
- Harness pi3 compares UV1 + UV2 at every index position of every copied part (all modes, E and X below):
  2026-11-17 0 differences (FH1 669k, FH2 373k, FM3 656k vertex uses).
- Conversion to FM4 form (PartSource -> CarPort.ConvertModel, the ports' own conversion):
  - FH1 / FH2: exact (max difference 0 on 3.3M / 4.3M vertex uses).
  - FM3: FM3 stores UVs as half floats, FM4 as unorm16 inside each subsection group's min..max range. Where an FM3
    group spans a huge range (FOR_FocusRS_09 lod0 bumperFrace chrome U: -28304..39232; BMW_M3E30_91 UV2 holds
    values like -40..83, and 55370 on frame) the 16-bit step grows: harness max 0.06 (seed 20261117) and 0.51 (Focus
    chrome) UV units, median 0.0035, on 38 of the sampled section files. This is the FM3 -> FM4 port's behaviour too,
    a precision limit of the FM4 encoding, not a remap. User 2026-11-17: "lascia così le uv fm3" - kept as is.
- Diagnostic `E:\DEV\fm4dev\ST03\fm3\uvdiag.exe <FM3 zip> <main|_lod0|_cockpit> <section>` lists the worst vertices.

### Source file chosen, target = the open file (2026-11-17, replaces "The open file decides" above)

User: "non fare che se apro cockpit mi dai solo cockpit dell'altra auto... da fm2 non ho cockpit non ho manco lod0...
mi fai scegliere il file main, lod0 o cockpit (per assurdo anche freni e dischi). mi sta bene la regola import solo
su lod aperto".

- After the car, `PickPartSourceFile` lists the source car's main, lod0, cockpit and brake files (calipers, rotors;
  key "_<rest>"), the open file's kind preselected (`PickDialog.Preselect`); one file only (FM2): no question.
- `PartImport.SourceFile` (source key) + `OnlyFile` (this car's open file, the only one written). `SourceModel`:
  same LOD layout -> as is (lod0 <-> cockpit, brake -> lod0 / cockpit); main -> lod0 / cockpit -> LOD 1 through
  `CloseUpFromLod1` with the close-up material names (`Lod0Fill.CloseUpMaterials`, shared with Lod0Fill);
  one-LOD file -> main: refused with a message (to be decided with the user).
- Harness pi3 run X: main->lod0, main->cockpit, lod0->cockpit, cockpit->lod0, brake->lod0 (copied, only the target
  file written, other parts untouched, UVs identical), lod0->main (refused). 2026-11-17, 10 random targets each:
  FH1 150/0, FH2 150/0, FM3 150 runs with 38 fails all "UV moved by the conversion" (above), copy fails 0.

### App + in-game check 2026-11-17 (user, FM4 Abarth 500)

Seats + steering wheel from FM3, front bumper from FH1, rear wing from FH2, file choice: OK in the app. In game:
damage OK, the FH1 bumper deforms correctly. FM3 -> FM4 and FH1 / FH2 -> FM4 part copy: CLOSED.

### FM2 -> FM4 (2026-11-17)

- `PartSource.FromMotorsport2`: the FM2 port's own model step (`Fm2Source.Stage`). The one FM2 .carbin (LODs 1-5)
  is exported to OBJ in a temp folder (`CarModelFolder.ExportLegacy`) and rebuilt onto a copy of THIS car's main
  model as donor (`CarModelFolder.Prepare` / `Apply`, any material allowed): tangent frames, baked light and damage
  words from the nearest vertex of this car (the donor rule), UV1 as FM2 has it (no fit), UV2 0 as in FM2. Only the
  FM2 parts are kept, then `CarPort.ConvertModel` maps the material names as the port does. Temp folder deleted.
- No file question for an FM2 car (one file). Into this car's main: LODs 1-5 as FM2 has them; into lod0 / cockpit:
  the part's LOD 1 (`PartImport.SourceModel`, as for any main source).
- F1 help GOOD TO KNOW "Copy Part from FM2".

#### FM2 UV decoding fixed (affects the viewer, OBJ export and the FM2 port too)

- FM2 UV0 is sign + magnitude / 1310.72 (docs/CARBIN_ALL_GAMES.md, `Fm2CarbinModel`). The legacy reader
  (`ForzaVertex`, version Two) read it as a half float and `ForzaCarSection` "fixed" it with * or / 12800: exact
  only below 1.5625 in size. Larger UVs (grilles, rubber trim, lights, tiled glass) came out wrong, up to millions
  (toy_altezza_04 bumperFd U -161996800), and in FM4's 16-bit encoding they squeezed the whole part's UV range
  (bumperFd max U 5.80 became 1.66).
- Now `ForzaVertex.Fm2Uv` decodes sign + magnitude directly; `ForzaCarSection` keeps the viewer convention
  (u = -value, v = 1 + value). Below 1.5625 the values are the same as before, so already-correct UVs do not move.
  FM2 cars ported before 2026-11-17 with such parts have wrong UVs on them: a re-port fixes them.
- Harness pi3 with FM2 sources: per part and LOD the U / V range of the faces of the FM2 OBJ export vs the converted
  FM4 part: before the fix 51 fails (millions), after it 0, max difference 1.3e-6. 10 random FM4_DEV targets,
  seed 20261117: 90 runs, 0 fails (pi2_10.txt), copies main->main, main->lod0, main->cockpit with UVs identical.
- `E:\DEV\fm4dev\ST03\fm3\fm2uv.exe <FM2 car zip>`: UV range per material with both decodings.

- 2026-11-17 user: copy from an FM2 car OK in the app (main and lod0, UV map) and in game; Sparco Evo re-port with the signed-magnitude UV fix OK in game (grilles, carbon fibre hood and trunk).

#### FM2 UV: the magnitude is signed (2026-11-17, Sparco Evo carbon fibre hood / trunk "corruzioni")

- After the first fix the user re-ported the FM2 Sparco Evo: grilles fixed, but smeared carbon fibre on the hood
  (cockpit) and trunk (main, lod0). `fm2sat.exe`: those vertices read ~24-25 (raw 0x7C19, 0x7FEA...) while their
  triangle neighbours read ~0: 24.91 is -0.09 wrapped. The 15-bit magnitude is itself signed: 0x7FEA = -22 / 1310.72.
- `ForzaVertex.Fm2Uv` now reads bits 0-14 as signed 15-bit (>= 0x4000 -> -0x8000), bit 15 still flips the sign.
  `fm2span.exe` over the 310 FM2 cars (23.9M triangles): values above 12.5: 288764 -> 0; triangles with a UV span
  above 2 inside one triangle: 64804 -> 39954; Sparco Evo 427 -> 0. Values below 12.5 are unchanged (grilles stay).
- The 39954 wide triangles left (lam_gallardo_05 1869, lam_murcielago_05 1575, ...) may be real tiling; not checked
  in game yet.
- docs/CARBIN_ALL_GAMES.md FM2 vertex row updated. pi3 with FM2 sources after the change: 90 runs, 0 fails.

### FM3 as target (2026-11-17, plan approved by the user: "sì, procedi con FM3")

How: this FM3 car's file is opened in FM3's own layout (`new CarbinModel(data, true)`, which writes all 3930 FM3
carbins back byte-identical), so the car's own parts, header and trailer never change. Only the copied part is made:
- `PartSource.ForMotorsport3`: the source files in FM4 form. FM3 car: the plain format conversion
  (`Motorsport3AsMotorsport4` = `ConvertMotorsport3ToMotorsport4`, names kept, vertex order kept); FM4 car: as it is;
  FH1 / FH2 / FM2 car: `ForMotorsport4` with this FM3 car's files in FM4 form as the damage / donor reference.
- `PartImport` (Target = the FM3 files, `TargetAsMotorsport4` = their FM4 form for placement and names,
  `SourceNative` = the FM3 source files): the part is moved in FM4 form, then
  `CarbinModel.ConvertPartToMotorsport3` (the FM4 -> FM3 port's section step, Unk1c flag by the part's new name)
  and FM3 material names (`lights_glass` -> `detail_glass_clear`, the port's list), then `ImportSection` into the
  FM3 file.
- FM3 -> FM3: same kind of file and not moved = the FM3 section as it is (every byte). Moved, or main LOD 1 used up
  close: the converted part gets the FM3 source's bytes 8..39 back in every vertex (UV1, UV2, normal, the 4 S16
  values, damage word, the dropped word, frame axis; a move is translation + even scale, normals do not change), so
  only positions and bounds are new. Vertex order: same buffers, or (up close) the native Lod vertices LOD 1 draws in
  ascending order (CloseUpFromLod1 keeps the order); checked on UV1 of every drawn vertex, else a warning and the
  converted part stays. A brake file's 16-byte vertices give their UV1 only.
- FM4 / FH / FM2 -> FM3: UVs become FM3 halves (FM3's own precision: up to 0.49 on UV2 values around 500, the
  undercarriage AO sets; tiny below 2).
- Moves still go only into the open file (the user's rule), the replaced part and placement are the open file's.
- `Motorsport3Buffer`: a source vertex shorter than 32 bytes (a brake part into a car file) gets 0 S16 values.
- F1 help GOOD TO KNOW "Copy Part into FM3".

Harness `E:\DEV\fm4dev\ST03\fm3t\pi4.exe <FM3 root> <source root> [cars] [seed]` (read-only, the app's path), 10
random FM3 targets per source game, seed 20261117: runs A main->main replace + move, B main->main new (not moved),
C lod0->lod0 replace + move, D main->lod0, E main->cockpit, F brake->lod0. Checks: result is FM3 and re-reads, head /
trailer / every other part identical, LODs, 40-byte vertices, positions vs FM4 form (+ move) within half precision,
FM3 source bytes kept (B: whole buffers identical), UVs within half precision, result converts to FM4 form again,
source unchanged. Results (pi4_<game>_10.txt): FM3 60/0, FM4 60/0, FH1 60/0, FH2 60/0, FM2 40/0 (no lod0 / brake
in FM2). Worst position 1.6 mm (tolerance 1 mm + 0.15 % of the distance from the part centre).

2026-11-18 user test (wizard, 15 steps) ALL OK: app (FM3 add/replace/main->lod0, FM4 main + cockpit, FH1 main, FH2 lod0,
FM2 main, brake->lod0, undo/redo/save) and game (loads, visible far/near/cockpit, materials, damage) on the FM3 Supra (TOY_SupraRZ_98) in
FM3_Extracted_DEV. Next (user 2026-11-17): FM2 as target, lod0 / cockpit -> main.

### Part edits on FM3 cars (2026-11-18, user: "editor posizione, rotazione e dimensione non abilitato su FM3")

The PART tab's move / rotate / scale (whole part or one material), Delete, Duplicate and Rename Part now work on FM3
files, opened in FM3's own layout like Copy Part (`ArchiveEditorForm.EditableModel`). FM2 stays refused.
- `CarbinModel.Positions` / `Encode` read and write FM3 positions (4 halves: unit direction + distance from origin +
  bounds centre) on FM3 sections (`IsMotorsport3Layout`: marker 2 and 40 / 16 / 44-byte vertices; a section being
  converted between the games has its new buffers in 32-byte form, so the ports read as before). Bounds and centroid
  are refitted as for FM4; FM3 brake sections keep their 0 bounds (model space).
- Frames (`TransformVertexFrame`): the FM3 normal (11:11:10 at 16, or 12 on 16 / 44-byte vertices) and the frame's
  Z axis (at 36) are turned only when the edit rotates or scales unevenly; a move or an even scale leaves them as
  they are. UV1 / UV2, the S16 values, the damage word and the word at 32 are never touched.
- Selection bounds of an FM3 part come from the model (CarbinFile reads FM4 / FH only).
- Harness `E:\DEV\fm4dev\ST03\fm3t\pe3.exe <FM3 root> [cars] [seed]` (read-only), 10 random FM3 cars x main / lod0 /
  cockpit, a random part each: move, rotation, even scale, uneven scale, one material rotated: 150 runs, 0 fails
  (pe3_10.txt). Positions within 0.85 mm, normals within 0.07 degrees, every other byte and every other part
  identical. pi3 (FM3 -> FM4) and pi4 (into FM3) re-run after it: no change (pi3's 7 "moved by the conversion"
  lines are the known FM3 -> FM4 UV precision, the same as uv_new_FM3.txt).
- App + in-game check pending.

### FM2 as target, and part edits on FM2 (2026-11-18, user: "sì, b, e poi editor posizione anche su FM2")

Study first (read-only, `E:\DEV\fm4dev\ST03\fm2t`: study.txt, uvmap, com), 310 FM2 car bodies:
- shared buffers: max opaque 56623 vertices (aud_tt_04), room left min 8913, median 45192 (FM2 indices are u16);
- parts: LODs 1-5 on 82 %, 1-4 / 1-3 the rest, every part has LOD 1; 1958 parts carry glass shards;
- bounds: origin = box centre, min = -max (52 / 52 Supra parts); glass shard vertices in car space (641 / 641);
  centre of mass NOT in car space (8 of 1091 parts with mass): left as it is on moves;
- UV: FM4 UV1 = - FM2 value, U and V (exact on 1081 unique-position vertices); FM2 holds +-12.5 only, FM4 mains
  exceed it on 3.7 % of index uses;
- baked lighting (16 values): nearest vertex of another part 1.45 vs random 2.16 (mean |diff|);
- 18 FM4 material names no FM2 car uses (lights_glass, chrome_2, undercarriage, bottom...): warned, renamed by the user.

Built:
- `Fm2CarbinModelEdit.cs`: Locals / Rebuild (both shared buffers laid out again from each part's vertex bytes, the
  RepackBuffers layout), AddPart, ReplacePart (same index: draw order and emitters kept), RemovePart (emitters
  re-parented, -1 for the removed part), DuplicatePart, RenamePart, RemoveMeshes (vertices no mesh draws go),
  TransformPart (positions, normals only when turned, glass shards), TransformMesh (shared vertices copied),
  MeshBounds, FromMotorsport4 (FM4-form part -> FM2 part: positions as halves, UV0 = - UV1, UV1 0, normal from the
  frame, damage word from the FM4 table or 000003FF, FH: from this car's nearest vertex; unknown word + 16 lighting
  values from this car's nearest vertex facing the same way, 10 cm grid).
- UV choice b (user): a part whose UV0 leaves +-12.5 is shifted by whole numbers per connected piece (real
  triangles, degenerate strip joins ignored); a piece still wider gets a shift per triangle (steps of 20 so
  neighbours share it), its meshes written one triangle per strip in the same winding, border vertices written
  twice. Only a single triangle wider than 25 is refused (pi5: 10 of 120 copies, race wings / race bumpers with UV
  spans up to 146 inside one triangle).
- `PartImport.Motorsport2.cs` (RunMotorsport2): Copy Part into the FM2 car's one file; FM2 source copied as it is
  (moved in FM2 form), FM4 / FM3 / FH sources in FM4 form (`PartSource.ForMotorsport2`), moved in FM4 form, then
  FromMotorsport4; a replaced part's physics and glass shards go to the copy; checks: re-read, `Check()` empty,
  meshes / LODs, other parts identical, source unchanged.
- `ArchiveEditorForm`: FM2 cars accepted as Copy Part targets (list / names / Replace box from Fm2CarbinModel, an FM2
  source's parts listed as they are); PART tab move / rotate / scale (part or material), Delete (part or material),
  Duplicate, Rename on FM2 files.
- F1 help GOOD TO KNOW "Copy Part into FM2", "Part edits on FM2".

Harness `pi5.exe <FM2 root> <source root> [cars] [seed]` (read-only), 10 random FM2 targets, runs A replace + move,
F replace + fit, B new; with an FM2 source also edits T / R / S / M (one material) / D / U / N / X (delete a
material). Checks: re-read, Check() empty, writes back byte-identical, header unchanged, other parts identical, the
part's meshes once in the right draw lists, triangle by triangle (same winding) positions within half precision and
UV0 = - UV1 minus one whole number per axis per triangle; FM2 sources: vertex bytes kept. Results (pi5_<game>_10.txt):
FM2 110/0 (copies + edits), FM4 30/0, FM3 30/0, FH1 30/0, FH2 30/0; positions within 1.5 mm.

App + in-game check pending (FM2_Extracted_DEV).

#### User check 2026-11-18 (wizard) and fixes
- OK: FM2 / FM3 / FH sources, lod0 refusal, part moves, delete / duplicate / rename, save, car loads in game, damage
  and glass; FM3 part edits OK in app and game.
- KO hood of the FM4 Top Secret Supra (carbon fibre) refused: FM4 carbon_fiber UVs are FM2's x 250 (the weave's
  tiling: same parts of the 60 cars both games have, span ratio 250.06 U / 249.99 V over 113 meshes; every other
  material 1.00, `fm2t\uvscale.txt`; FM4 regenerated them, so no per-vertex linear map, `carbon.exe`). Fixed:
  `Fm2CarbinModel.FromMotorsport4` divides carbon_fiber UVs by 250 (`CarbonFibreScale`). pi5 refusals 10 -> 3 of
  120 (left: single triangles 25.5-31 wide on AST bumperFa / body).
- Edits made all or nothing (`Fm2CarbinModel.Atomic`): a failed add / replace / remove / move puts the model back.
- Checked, NOT the cause of the black / see-through parts in game: winding (strip triangles agree with the stored
  normals on 99.5 % FM2, 99.8 % FM4, 99.7 % converted, `wind.exe`), position encoding (FM2 writes unit direction +
  distance on 100 % of 1.76 M vertices, as the converter), UV convention for every non-carbon material (1:1).
  Open: converted parts black / see-through in game (FM2 parts fine). Next test: an FM4 part of the SAME car (FM4
  TOY_SupraRZ_98 -> FM2 Supra, same texture layout) to tell texture layout from vertex data.
- Open: one-material move on FM2 gave "editing not supported" (which tab: PART or MESH?).

#### 2026-11-21: black parts = UV word encoding; mesh moves on FM2
- User test: FM4 Supra hood + bumper onto the FM2 Supra still black -> vertex data, not texture layout.
- `fm2t\pdump.exe` (DEV Supra): every converted part has bit 15 of the UV words clear, FM2's own parts have it set
  on 94-100 % of the words; lighting / unknown word / normals in range.
- `fm2t\uvreal.exe` + `uvall.exe`: raw FM2 words vs the REAL FM4 UVs of the 79 cars both games have (344872 words,
  carbon fibre left out): **FM4 UV = (raw - 32768) / 1310.72, offset binary, range +-25**, exact in every bit group.
  The old "sign bit + signed 15-bit magnitude" read matched only bit 15 set / bit 14 clear (99.7 % of the words) and
  mirrored the rest; the old uvmap check compared against our own FM2 -> FM4 conversion (same reader), so it could
  not see it. The writer put the words without the offset: every converted UV mirrored -> wrong texture place ->
  black. Fixed: `ForzaVertex.Fm2Uv` = (32768 - raw) / 1310.72 (same "FM2 value = - FM4 UV" convention for every
  caller), `ForzaVertex.Fm2UvWord` writes it; `UvLimit` +-25, shifts in steps of 40, a single triangle wider than
  50 refused. Also changes FM2 -> FM4 reads (port + Copy Part) on the 0.3 % mirrored words (emblems, rubber trims,
  chrome / grilles above 12.5), now right.
- `fm2t\mm.exe`: converted FM4 Supra parts (hooda, bumperFa, skirtLa, trunk, wingrace) = FM2's own words on 1035
  unique-position words; the others = FM4's own UV exactly (FM4 nudged them by ~4 units).
- Point 3 was the tree drag (MoveMesh) / Attach / Merge Part / Duplicate Mesh, all FM4-only (`new CarbinModel`).
  Fm2CarbinModelEdit: `LocateMesh`, `MoveMesh` (into a part, a new part, or Attach into a given mesh; same material
  + LOD joined with a strip restart), `MergeParts` (one position encode for the whole merge; glass shards appended),
  `DuplicateMesh`; wired in MoveMesh / AttachMesh / PickMode. `mm.exe` index-by-index checks (corner within 2 mm,
  restarts in place, every other mesh unchanged, draw lists, see-through flag): all OK.
- pi5 again: FM2 110/0, FM4 30/0, FM3 30/0, FH1 30/0, FH2 30/0, no UV refusals left.
- FM3 mesh moves: tree drag / Attach / Merge / Duplicate Mesh open FM3 with `EditableModel`; `fm3t\mm3.exe` all OK.
- User in game: textures right now, blotchy shading. Normals ruled out (`fm2t\nrm.exe`: converted = FM2's own, median
  0.1 deg; FM4 frame +X is the FM2 normal). +24 word: not a unit vector in any common packing (`w24`, `w24b`), small,
  ~perpendicular to the normal, 0 on mirrors / interior / glass. Cause found in the baked lighting donor (`lit.exe`
  on the DEV Supra): 12.8 % of the converted bumper's vertices darker by > 0.15 (band 0) than the original 2 mm away:
  the nearest vertex was often another upgrade bumper in the same place or a coarser LOD. Donor now: LOD 1 vertices
  only, zero-lighting vertices only for see-through materials, the replaced part (else the part of the source's
  name) first, 4 nearest within 10 cm averaged. `lit2.exe` same-car bumper: |diff| 0.027 -> 0.010, darker 2.9 % ->
  0.3 %. pi5 FM4 / FM3 / FH1 30/0.

#### 2026-11-21 (later): FM2 baked lighting computed as PRT with light sent back, fitted per car
- In game the first computed lighting (linear map of ray features + hybrid + smoothing) was still wrong ("non ci siamo",
  dark lower right; bisect: only the lighting field). Coefficient errors of ~0.04 hid a structural gap.
- `fm2t\shb.exe`: the 16 values are a D3DX precomputed-radiance-transfer vector, stored = 2 x T (signs (-1)^m, car
  space). Vertices nothing hides: computed = stored within 0.003. The old map had no term for the unoccluded cosine
  lobe on bands 1-3 and shrank everything towards the mean.
- `shb2.exe` / `shb3.exe` / `shb4.exe`: plain occlusion (any face rule, own part only, blends) is far too dark where the
  car hides the sky, mostly in band 0 -> the baker let surfaces send light back. A ray that meets the car brings
  rho x the stored value of the surface it meets; rays going down meet a black ground rectangle. Either face stops and
  sends light (rule c; "front faces only" fits the same, with more spread of rho between cars).
- `gfit.exe`: rho and the ground differ per car (rho 0.85-0.95; ground 0.15-0.25 m under the origin on most cars, 0.6-0.8 m
  on aud_tt_04 / hon_preludesir_00; edges 0.1-0.5 m beyond the car's box) -> fitted on 1500 stock vertices of the target
  car at copy time (whole grid for height + one margin, then rho / height / each edge in turn).
- `lcmp.exe` (shading under sky and sun test lights, the FM4 RZ bumperFb as bumperFa against the game's own bumperFb):
  old app 0.11-0.16 mean error and the upper half darker by 0.06-0.11; new 0.03-0.06, quadrant means within +-0.05.
  The replaced part's residual nearby (old hybrid) and smoothing make the new model worse (0.045 -> 0.077): removed.
- `lapp.exe var` (the app's ComputeLighting by reflection, every upgrade variant of toy_suprarz_98, aud_tt_04,
  hon_preludesir_00, maz_familiamazdaspeed_01 in the place of its stock part, 92443 LOD 1 vertices): band 0 |err| 0.028,
  all 16 0.025, off by > 0.1 5 % (old map 0.053 / 0.036 / 16 %, old hybrid 0.039 / 0.031 / 10 %, donor 0.041 / 0.051 / 11 %).
- App: `Fm2CarbinModelLighting.cs` rewritten (LightScene with per-triangle values and nearest hit, TraceLight, LightValue,
  CalibrateLighting, ComputeLighting(part, vertices, replaced, out fitted)); the note in the import log now reads
  "baked lighting computed on N vertices (light sent back 0.90, ground at -0.20 m)". About 1.5 s per part.
- Test zip for the game: `fm2t\bisect\NEW` (the user's DEV car, only bumperFa's lighting replaced) copied to
  FM2_Extracted_DEV; `bisect\L` has the game's own lighting for comparison.
- pi5 after the rewrite: FM2 110/0, FM4 / FM3 / FH1 / FH2 30/0. Built in `bin\Debug` 2026-11-21 12:33.

#### 2026-11-21 (later): UV Mapper on FM2 and FM3 models
- User: "permettimi di modificare uv su fm2 fm3". The mapper opened every file with the FM4 reader (FM3 / FM2: refused).
- `UvSession.cs`: each mesh sits behind `ISource` (triangles, positions, UV, normal, split of shared vertices, write);
  `CarbinSource` = FM4 / FH meshes and FM3 ones in their own layout, `Motorsport2Source` = Fm2CarbinModel meshes.
  `session.ToBytes()` gives the file; the mapper window and the viewport preview are unchanged.
- FM3: the vertex holds the UV itself as halves (UV1 at 8, UV2 at 12 on 40-byte car vertices; 16-byte brake and 44-byte
  wheel vertices UV1 only). An FM3 mesh has no UV range: the 8 floats are 0 on 18050 of 18050 meshes (`uvt\dbg2.exe`,
  every 12th FM3 car). `CarbinModel.LayoutUv` / `WriteLayoutUv` read and write that; only the edited faces' vertices
  are written, the file stays FM3.
- FM2: `Fm2CarbinModelUv.cs` (`Fm2CarbinModel.UvWork`): the mapper works on each part's own vertex bytes, a vertex
  another mesh uses is copied whole ("keep separately"), the UV words (offset binary, FM4 UV = (raw - 32768) / 1310.72)
  of the edited vertices are written, the two shared buffers are laid out again on write. One channel only: UV2 and
  "From the other channel" give a message. A mapping beyond +-25 is refused at Apply with a message (nothing written).
  carbon_fiber meshes show FM2's own values (FM4 / 250).
- Face selection is still FM4-only (the face picker reads FM4 models): on FM2 / FM3 the mapper works on whole meshes.
- Harness `E:\DEV\fm4dev\ST03\uvt\uvt.exe` (corner by corner against a fresh read of the original; keep = every vertex
  byte identical; map u * 0.5 + 0.25, v * 0.5 + 0.1 = expected UV within the encoding step, every other byte and every
  other mesh identical; the result re-reads and writes back identical; FM2: a UV of 30 refused):
  FM2 20 cars 80 runs 0 fails, FM3 10 cars / 110 files 330 runs 0 fails, FM4 8 cars / 87 files 261 runs 0 fails.
  Raw Horizon files are not editable by the mapper (CarbinModel.Positions does not read them), as before.
- Built in `bin\Debug` 2026-11-21 12:44. App check (open the mapper on an FM2 and an FM3 car, map, Apply, Save) and
  in-game check pending.

#### 2026-11-21 (evening): FM3 / FM4 baked lighting study (read-only, nothing in the app yet)
- User in game: the new FM2 computed lighting is OK; the UV Mapper opens on FM2 / FM3. User goal: as little as possible
  taken from a donor (in the end none, damage included, later).
- Harnesses in `E:\DEV\fm4dev\ST03\light34` (`sh34.exe`, `cmp24.exe`, `secs.exe`).
- The 4 SNORM16 values of FM4 (+24 of the 32-byte vertex) and FM3 (+20 of the 40-byte vertex) are the same kind of data
  as FM2's 16: bands 0-1 of the D3DX transfer vector, same signs and axes, stored = k x 2T with k about 1.45 (open
  vertex facing up: 0.82; FM2: 0.562). The 4 x 4 map stored = M x computed on open vertices is diagonal (1.39 .. 1.55).
- The FM2 model (light sent back by the car + black ground rectangle, fitted per car) fits them: mean |err| in 2T
  units 0.037-0.064 on 4 FM4 cars and 0.040-0.064 on 3 FM3 cars (plain occlusion 0.11-0.14). The main model holds one
  "wheel" section at the origin: it must stay out of the scene.
- The games did not copy the values from one title to the next: the same car, same vertices, FM2 against FM4 / FM3
  differs by 0.04-0.07 in stored units after scaling (0.03-0.05 in 2T units), identical on 2-6 % of the vertices only.
  So the computed values are about as close to the game's as one game's bake is to the next game's.
- Where the app takes this from a donor today: FM2 -> FM4 / FM3 parts and the FM2 port ("baked light: from the nearest
  vertex of this car", PartSource.cs), OBJ import ("precalc lighting", CarModelFolder). Candidates for the computed
  version; not started, scope to agree with the user.
- Viewport vertex / edge / face picking on FM2 / FM3: never worked (the picker opens the file with the FM4 reader,
  which refuses FM2 and FM3; MeshEdit.cs unchanged since the last commit). Not a regression; a job of its own.

#### 2026-11-21 (night): baked light without a donor on FM4 / FM3, MESH tab on FM2 / FM3
User: "tutti i flussi insieme, poi selezione su fm2 fm3"; goal: as little as possible from a donor.
- What the three flows did before (port FM2, Copy Part from FM2, Import Model from OBJ; all through CarModelFolder.Apply
  -> CarbinModel.RebuildLods): every new vertex got `WriteTail`, the level of an open panel facing its way, no shade
  (the notes said "from the nearest vertex of this car": that had been dropped long ago for its blotches).
- New `ForzaStudio/CarbinModelLighting.cs`:
  - `LightingFromMotorsport2(fm2)`: a model rebuilt from an FM2 car takes, per vertex, the first 4 of the 16 values of
    the FM2 vertex at the same place (2 mm, the one facing most the same way) x 1.457 (`LightScale`). The source game's
    own data: no donor, no estimate. Called after Apply in `Fm2Source.Stage` (port) and `PartSource.FromMotorsport2`
    (Copy Part; reaches FM3 targets too through the FM4 form). FM2 vertices without light (glass, interior) keep the
    open-panel estimate.
    Chosen over computing because of the user's rule "ported cars keep the source game's values" and because it needs
    nothing of the target car; measured against the REAL FM4 Supra (`light34\l4t.exe fm2`, 97358 vertices, all LODs):
    value 0 difference 0.138 -> 0.070, values 1-3 0.23 / 0.20 / 0.21 -> 0.088 / 0.078 / 0.076 (stored units).
  - `BeginLighting()` / `FinishLighting(state)` + `CarModelFolder.ApplyWithLighting` (Import Model in the editor):
    before the import rho and the ground are fitted on the car's own values (the FM2 engine, on 4 values), after it
    the vertices the import made (their bytes 16..31 are not among the part's old ones) are computed on the car; kept
    vertices are not touched. `l4t.exe obj` (FM4 Supra, 6 parts made "new" by a UV nudge, 10514 vertices) against the
    game's own values: value 0 0.100 -> 0.045, values 1-3 0.235 / 0.169 / 0.176 -> 0.093 / 0.057 / 0.060; the other 47
    parts unchanged; 2.3 s. The main model's "wheel" section stays out of the scene and is never computed.
  - `Fm2CarbinModelLighting.cs`: engine types made internal, `FitLightModel(scene, at, normals, stored, values)` shared
    (16 values for FM2, 4 for FM3 / FM4). FM2 result unchanged (lapp aud_tt_04 0.034 as before).
- Regressions with the new build: pi3 FM2 -> FM4 90 / 0, FH1 -> FM4 149 / 0, pi5 FM4 -> FM2 30 / 0. pi3 FM3 -> FM4
  150 runs 49 fails "UV1/UV2 moved by the conversion, max 0.002-0.014": the SAME 49 with the build from before these
  changes (and 7 in pi3_reg.txt of 2026-11-18): not from this work, to look at separately.
- MESH tab on FM2 / FM3 (it never worked there: the picker and the three edits opened the file with the FM4 reader):
  - `ArchiveEditorForm.MeshEdit.cs`: pick data from `Fm2Model` (FM2, each tree mesh located by part / material /
    LOD) or `EditableModel` (FM3 in its own layout); Delete / Detach Faces and the handle moves (MeshGizmo.cs) go to
    the FM2 model's new methods or to the FM3-native CarbinModel.
  - New `Fm2CarbinModelMesh.cs`: MeshFaces, DeleteFaces, DetachFaces, TransformVertices (only this LOD; only the moved
    vertices are written, against the part's origin as it is; the box grows around it).
  - BUG FIXED in `CarbinModel.MoveVertices.cs`: TransformVertices wrote the FM4 quaternion at +16 on every moved
    vertex; on an FM3 vertex that is the normal and half of the baked light. Now `TransformVertexFrame` (FM4: the same
    call; FM3: normal and axis, only when the edit turns them).
  - With face picking working, the UV Mapper takes picked faces on FM2 / FM3 too.
  - Harness `uvt\mesht.exe <root> <FM2|FM3> [cars]` (delete a third / all faces, detach, move, rotate; corner by
    corner against the original): results in `uvt\mesht_run.txt`.
    FM2 20 cars 100 runs 0 fails (unmoved vertices byte-identical). FM3 10 cars / 110 files 550 runs, 1 over the 1.5 mm
    limit of the test: deleting faces or a mesh refits the FM3 part's box and writes every position of the part again
    as halves, and on the largest part (body, LOD 0) one vertex landed 1.6 mm away. The same re-encode the FM3 part
    moves have had since 2026-11-18; a possible later improvement: keep the box when it still holds everything.
    uvt again on this build: FM2 24 / 0, FM3 99 / 0, FM4 99 / 0.

#### 2026-11-21 (late night): Import Model from an OBJ folder on FM2 and FM3 cars
User: "import obj su fm2 e fm3 non è abilitato". It was refused: FM2 by a message ("not available yet"), FM3 by the
FM4 reader (Export Model too).
- The plan (`CarModelFolder.Prepare`) and its confirm window are reused as they are: they only need part names,
  materials, LODs and part origins.
- FM3: `LoadCarModels` opens each FM3 file in FM4 form (`PartSource.Motorsport3AsMotorsport4`), export and import run
  on that; after `ApplyWithLighting`, `CarModelFolder.ToMotorsport3` puts every part of the plan into the car's own
  FM3 file (RemoveSection + `ConvertPartToMotorsport3` + ImportSection, as Copy Part into FM3); the other parts keep
  every byte. Every file is built before any is replaced in the archive. FBX / glTF go the same way (not tested).
- FM2: `CarModelFolder.PlanModel(fm2)` (names / LODs / origins only, never written) for the plan,
  `CarModelFolder.ApplyMotorsport2` -> new `Fm2CarbinModelObj.cs` `RebuildFromObj(part, template, meshes per LOD,
  dropped LODs)`: LODs with no changed OBJ keep meshes and vertices; an OBJ corner equal to a vertex the part had
  (0.1 mm, 1 degree, the same UV words) keeps it, all 60 bytes; a new vertex = position (halves against the part's
  origin, which stays), UV words, normal, damage word and +24 word of the nearest vertex the part had (new part: of
  the template), baked light computed (ComputeLighting, kept vertices get theirs back); triangles wound as the car's
  own are against their normals (counted on its LOD 1); UVs beyond +-25 and LOD 0 refused with a message.
  FBX / glTF and the UV map export stay unavailable on FM2 (message).
- Harness `E:\DEV\fm4dev\ST03\uvt\objt.exe <root> <FM2|FM3> <work folder> [cars]`: N untouched export = empty
  plan; A (FM2) every part rebuilt from its OBJ: 100 % of the vertices byte-identical to the game's, only triangles
  with two corners in one place are gone (they draw nothing); E one part edited (x > 0 moved 2 cm up): only that part
  in the plan, the others identical in every byte, its other LODs untouched, every vertex within 3 mm of the OBJ, new
  vertices lit. Results in `uvt\objt_run.txt`.

2026-11-21 (late): follow-up work, user "do every point, one big check at the end"

- pi3 "UV1/UV2 moved by the conversion" (FM3 -> FM4): not a bug. FM3 holds the UV as halves with no range, FM4 as 16
  bits over the mesh's range; every fail was half a step of a wide range, and the FM4 game's own files of the same
  cars hold the same ranges (29 of 29, `fm3\uvwhy2.exe`). pi3 now allows the grid of the mesh's range: 150 runs / 0.
- FM3 Delete Faces / removing a mesh: the part's box stays (`CarbinMeshEdit.CompactSection`,
  `CarbinModel.RemoveSubSectionsAt`). Only vertices go, so the box still holds the rest and the kept vertices keep
  every byte; before, the box was refitted and every position half of the part written again (up to 1.6 mm off on a
  `body`). FM4 / FH unchanged (refit as before, in game since 0.0.5). `uvt\mesht.exe` now asks for identical bytes:
  FM3 6 cars 330 runs / 0 fails (`mesht_fm3_box.txt`), FM2 25 / 0.
- FBX / glTF on FM2 (new) and FM3 (now tested), Export Model and Import Model:
  - FM3 already went through its FM4 form (`LoadCarModels`); nothing changed there, only the harness is new.
  - FM2: the model is the name-only `CarModelFolder.PlanModel`; its geometry is the car's own OBJ export
    (`CarModelFolder.ExportLegacy` into a temporary folder, read back by `CarModelFbx.LegacyGeometry`). `CarModelFbx.Export`
    has a `geometry` map (kind -> where the meshes come from) and `PrepareFolders` a `legacy` map (kind -> FM2 bytes):
    the base folder is the FM2 OBJ export and the scene is compared with those OBJs, then the import is the OBJ-folder
    one (`CarModelFolder.Prepare` / `ApplyMotorsport2`). So an FM2 scene holds what the OBJ folder holds: UV1 only,
    LODs 1 to 5. A part LOD with nothing to draw gets no node.
  - Export UV Map stays unavailable on FM2: it draws LOD 0, which FM2 cars do not have (message updated).
  - Harness `uvt\fbxt.exe <root> <FM2|FM3> [cars]`, per car and per format (.fbx, .glb): N the exported scene imported
    back = empty plan; E the scene exported with one part moved 2 cm = only that part in the plan, the result re-reads,
    every other part keeps every vertex byte, every moved corner has a vertex within 3 mm. FM2 10 cars 40 runs / 0
    fails (`fbxt_fm2.txt`), FM3 6 cars 48 runs / 0 (`fbxt_fm3.txt`). Max / Blender round trip: not tested (user).
- PART tab Export OBJ / Import OBJ (one part) on FM2 / FM3: new `PartObjImport.cs` (FM4 / FH keep the form's code).
  The window and its choices are the FM4 ones (replace part, replace one material, add materials, new part from a
  template); its part and material lists now read FM2 / FM3 files (`PartObjImport.Names`).
  - FM3: on the file in FM4 form (`ReplaceGeometry` / `RebuildLods`), baked light of the new vertices computed
    (`BeginLighting` / `FinishLighting`), then only that part goes back in FM3's layout (`CarModelFolder.ToMotorsport3`).
  - FM2: `Fm2CarbinModel.RebuildFromObj` with the OBJ at every LOD the part has (as FM4's ReplaceGeometry does);
    replace-material / add-materials rebuild each LOD from its own triangles (the part's OBJ export) minus the
    replaced material plus the OBJ's. "Fit UVs" is left out on FM2 (no material regions), with a line in the status.
  - Export OBJ: FM3 read in FM4 form (lod0 file first, as on FM4); FM2 = the part's LOD 1 from the OBJ export.
  - Harness `uvt\partobjt.exe <root> <FM2|FM3> [cars]`: R replace, M replace material, A add material, N new part;
    result re-reads and writes back identical, every other part byte-identical, moved corners within 3 mm, M keeps
    the materials, A draws more triangles. FM2 8 cars 32 runs / 0 (`partobjt_fm2.txt`), FM3 5 cars 20 / 0.
  - Not done here: the FM4 path of the PART tab Import OBJ still uses the open-panel light estimate (unchanged, in game
    since 0.0.5); only Import Model and the FM2 / FM3 paths compute the light.
- FM4-only model openings left in the editor (`new CarbinModel(data)` without the FM2 / FM3 readers), checked one by one:
  - Exhaust tab, Gauges tab: they read FM4 header blocks. `uvt\headprobe.exe` on 40 FM3 cars: gauges read in 0 of 40
    cockpit files, exhaust locators in 0 of 40 main files (FM4: 39 of 39 both). FM3 has no exhaust flames (backfire
    study); FM3 gauges sit in a header layout not decoded yet. The tabs hide themselves on FM2 / FM3, no error.
    OPEN (a header study of its own): Gauges on FM3.
  - Driver tab (driver controls in the header), Rim Scalable button (FM4 wheel morph table), Check Model Format
    (CarbinValidator = FM4 layout from the xex): FM4 features by their data; they stay hidden / say "not checked".
  - Material check: already per game (`shaderProfile`). Copy Part lists: already per game (`targetAsFm4`, `Fm2Model`).
  - Fixed in this round: PART tab Export OBJ / Import OBJ and their part / material lists (above).
- lod0 / cockpit / brake sources -> main model: NOT built. USER RULE 2026-11-21: FM2 cars have no LOD 0, no LOD 0 goes
  into an FM2 car, its LODs 1 to 5 stay as they are; FM2 targets keep refusing LOD 0-only sources
  (`PartImport.Motorsport2.cs`). For FM4 / FM3 targets the idea (source LOD 0 as LOD 1, LODs 2-5 by edge collapse on the
  SAME vertices, so no per-vertex data is made up) waits for the user's yes.
  To confirm with the user: PART tab Import OBJ on FM2 puts the OBJ into every LOD the part has (1 to 5), as the FM4
  button does on FM4; Import Model (OBJ folder / FBX / glTF) rebuilds only the LODs whose OBJ changed.
- DECIDED by the user 2026-11-21 (late): lod0 / cockpit / brake sources -> main model is CANCELLED, on every game.
  The rule stays: lod0 -> lod0, cockpit -> cockpit, LODs 1-5 -> LODs 1-5 (a main-model part can still go into a close-up
  file as before, its LOD 1 used as LOD 0). FM2: there are no per-car brake models, so discs / calipers from other games
  have nowhere to go; no LOD 0-only source goes into an FM2 car. No LOD generation by reduction in Copy Part.
  PART tab Import OBJ on FM2 confirmed as built: the FM4 behaviour on the LODs the part has (1 to 5, never LOD 0).
- Delete Faces (MESH tab): the yes / no question is gone (user 2026-11-21); the edit is pending until Save and Undo takes it back.

# ST-03 - Import parts from other models

Sprint 2 doc, section 6. Order agreed with the user (2026-11-15): FM4 first, then FM3, then FM2.

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
| any -> FM3, any -> FM2 | to do: CarbinModel does not edit FM3 / FM2 models today (the targets must become editable first) |

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

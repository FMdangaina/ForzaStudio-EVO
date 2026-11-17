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

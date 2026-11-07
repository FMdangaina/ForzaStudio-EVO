# Research notes

Moved out of ROADMAP.md on 2026-10-23 (the roadmap holds goals only). Topic numbers are the roadmap's.

## C1. Platform (topic 15)

- **.NET:** **.NET Framework 4.8** since 2026-11-01, x86, with XNA 3.1 (a .NET 2.0 mixed-mode assembly, loaded through
  `useLegacyV2RuntimeActivationPolicy` in app.config). The renderer stays XNA. To find out what else breaks
  (SQLite, HexBox). `xdevkit.dll` no longer blocks it.

## C2. Viewer and materials (topic 9)

- **Renderer:** a new one is almost impossible, the viewer stays on XNA.
- **Car shaders** (2026-10-17, `fm4dev\Shaders\FINDINGS.md`): `Media\Shaders\Cars\*.fxobj`, compiled Xbox 360 effects. 5 big libraries
  (`shaders_v16`, `_16car`, `_slod`, `_splitscreen`, `_ui`) + ~148 single-material files in `CustomCarShaders.xml`. A carbin material name is a
  technique name (`cockpit_` prefix in the cockpit). An unknown name falls back to `missing`, which is why FH lights render black in FM4.
  - FM4 library: 196 techniques, FH1: 254. FH1-only (57): one technique per light type (fullbeam, hidhead, xenonhead, drl*, indicator_*, fog*,
    lights_gls_*...), numberplate / numplate, traffic body / window, shadow masks. FH1 lights take colour from per-car `shadersettings.xml`;
    FM4 has only generic head_light / tail_light / reverse_light / lights_*.
  - FH1 shaders need engine inputs FM4 lacks (dynamic lights, cascaded shadows, two-tone paint): copying them is not realistic; the viewer can
    only imitate them from the parameters.
  - **Similarity** (2026-10-18): technique → vertex / pixel shader decoded for all four games; every FH1 / FH2 / FM3 material scored against FM4's
    (0-100). FH lights closest to `reflector` / `tail_light` (70-75), FM3 same-name materials 59-89. Used by 9.1.
- **Viewer camera:** the game's FOVs are vertical degrees, in `Media\Camera\CameraSettings.ini` and `Media\UICarTargetCameraPresets.xml`.

## C2b. The .carbin as the game reads it (2026-11-07)

- Traced in `default.xex`: the carbin is a versioned serialized object tree; every opaque block now has the game's
  name (glass particles, morph targets, UnpackingData, CCarMaterialData, header emitters / driver controls / gauges),
  the damage word is a Xenos 10_11_11 direction, gauge axes are stored as (-x, -y, z). Full table: `CARBIN_FROM_XEX.md`.

## C3. Wheels and morph targets (topics 10, 11)

- **Wheel morph.** [Aiden-Piercee](https://github.com/Aiden-Piercee): *"it's dependant on the wheel having a morph target in its package"*.
  BMW E30 rim does not scale, S60 does. @Jew: *"You need morph targets in the wheel model for Forza to scale them to size"*. Solved 2026-10-18:
  the morph target is the 16-byte per-vertex table of the wheel (rule in D, 10.6).
- **OBJ wheel import** (@delete.diskord): *"i used forza studio to export an fm3 wheel to obj, then opened an fm4 wheel zip in forzaeditor and
  attempted to replace the wheel with the obj i exported, resulting in the 'LOD 0 and LODs 1+ live in different files' error. however i did try the
  fm3 conversion (port car from another forza) in the new commit a little while ago and the wheel zip it output seems to work perfectly in fm4."*
  An FM4 wheel keeps LODs 0-5 in one carbin (cars keep LOD0 in `_lod0`): the import now tells them apart (60f7d57), in-game test pending (11.1).

## C4. Other models (topics 5, 6)

- FH1 / FH2 traffic models: close-up files are placeholders (17-vertex parts), rebuilt by the port.
- **Road holding FH vs FM4** (user report 2026-10-20): the car's own values (tyre compound id, widths, chassis friction scales, springs, ride height,
  anti-roll, aero, CM) are the same as FM4's for the same car (FH1 103 cars, FH2 138; `fm4dev\Traffic\tools\gripcmp`). What differs is game-wide:
  `List_TireCompound` (lateral grip FH1 ~1.8 / FH2 ~2.3 vs FM4 ~1.3, wear ×12) and half of `List_TireAffectCurve`. The port keeps FM4's rows for those
  ids, so a ported car grips like an FM4 car: nothing to convert per car.
- FM1: `.cab` / `.ca2` reader verified on 243 archives.

## C5. Documentation and community (topic 16)

- Format documentation in `docs/`. Discord: still few users; reports go into topics.

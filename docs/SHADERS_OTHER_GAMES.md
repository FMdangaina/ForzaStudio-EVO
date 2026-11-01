# Car shaders of FM2 / FM3 / FH1 / FH2 (study, 2026-11-01)

Goal: what FM4 already has (technique list `ShaderLookNames`, parameters per technique `ShaderSettings.Fm4Table`,
materials FM4 really uses `Fm4MaterialUse`, class of a material `ShaderLook`) for the other four games, with an
estimated look only (nothing exact: the fxobj files are compiled Xbox 360 microcode).

Harness: `E:\DEV\fm4dev\Shaders\games\gamescan.exe <tag> <cars dir> <out dir>` (source `gamescan.cs`, x86, build against
`bin\Debug\ForzaEditor.exe`). Output per game in `E:\DEV\fm4dev\Shaders\games\<FM2|FM3|FH1|FH2>`:
`mats.tsv` (list, material, cars), `ssparams.tsv` (technique, parameter, cars; empty parameter = technique count),
`stats.txt`, `log.txt`. Technique lists per game: `E:\DEV\fm4dev\Shaders\look\look_tech_<game>.tsv` (older work).

## What each game has

| Game | Shader files | Techniques | Car zips | With shadersettings.xml | Carbin read by |
|------|--------------|-----------|----------|-------------------------|----------------|
| FM4 | `Media\Shaders\Cars\*.fxobj` (shaders_v16 + ~148 single) | 303 | 317 | 256 | `CarbinModel` (done) |
| FH1 | `media\shaders\Cars` (same set, FH build) | 361 | 179 | 176 | `CarbinModel` (type 1 + 5, `IsHorizon`) |
| FH2 | same files as FH1 | 361 (identical list) | 219 | 209 | `CarbinModel` (type 1 + 5) |
| FM3 | `Media\Shaders\Cars\shaders_v11.fxobj` + DropShadow | 185 | 311 | 246 | `CarbinModel(data, true)` |
| FM2 | none in the folder (Media has no .fxobj; shaders are inside the game) | unknown | 310 (309 read) | 116 | `ForzaCarBin` (type 2, marker 0x2CA) |

- FH1 and FH2 have exactly the same technique list: one "Horizon" profile is enough for the look.
- FM3 has no technique FM4 lacks except `carbon_fiber_2/3`, `cockpit_carbon_fiber_2/3` and the debug passes; FM4 has 148
  techniques FM3 lacks (cockpit_bump_*, engine_*, rim_*, brake_* variants).
- FH1/FH2 have 58 non-debug techniques FM4 lacks (lights: xenonhead, hidhead, oldhead, fullbeam, fog*, drl*, indicator_*,
  taillightst, lights_gls_*; numberplate/numplate; body_traffic, window_traffic; *_simple; *SMask).
- FM2: only the material names (carbin) and the shadersettings.xml are available. Its parameter names are the
  FM3/FM4 ones plus an older set (MetalColor/MetalPower/MetalScale/MetalBias, TwoToneColor/Power/Scale/Bias,
  ColorScale, ColorClamp, LiveryScale, LiveryClamp, FinalScale, FinalAlpha, Specular2Scale/Clamp, SpecularPower2).

## Materials per game against its own techniques (car LOD0 / cockpit)

| Game | car LOD0 has a technique | cockpit | notes |
|------|--------------------------|---------|-------|
| FM3 | 51/54 | 55/55 (`cockpit_` + name) | missing: `01___Default`, `4_MER_SLR_05_damage`, `body2` |
| FH1 | 94/121 | 90/112 | the rest = per-part names (`body_LOD0`, `taillightL_LOD0`, ...), `Reserved`, `bump_plastic`, `solid`, `matt_colors` |
| FH2 | 110/153 | 95/116 | same, plus `deprecated`, `Standard_2` |
| FM2 | n/a (no list) | none | 206 distinct names over LODs 1-5; the model is one file |

So the FM4 rule "material name = technique name, unknown = red/missing" is true as is for FM3 (and FM4); for FH
the names without a technique are common on stock cars (per-part `_LOD0` names are what FH itself ships), so the
red "unused" mark must come from the game's own use list (like `Fm4MaterialUse`), not from the technique list.

## shadersettings.xml parameters

- FM3: 45 techniques set parameters, 64 parameter names; 16 are not in the FM4 table: CarbonFiberEnvDampner,
  CarbonFiberSpecularColor/Power, CockpiSpecularAmount2 (typo), CockpitClothTextureScale, CockpitSpecularColor(2),
  CokpitBumpScale (typo), CubePower, DamageSpecularKillAmount, FinalAlpha, GhostMin/MaxDistance, GuageEmissiveScale
  (typo), SpecularPower2, TrackLightmapWVPS. Technique `tail_light_2` has no FM3 shader.
- FM2: 17 techniques, 54 parameter names, 25 not in the FM4 table (the older set above).
- FH1 / FH2: 299 / 303 techniques, 215 / 216 parameter names, 139 / 140 not in the FM4 table: light colours
  (TailLightColor, XenonHeadlightColor, HidHeadlightColor, OldHeadlightColor, FullbeamColor, DRL*Color, FogWhiteColor,
  IndicatorLightColor, NumberPlateLightColor, SideLightsOrangeColor), StripeColor/StripePaintScale/StripeMetallicAmt,
  Ghost*, CSM shadow and Debug* parameters. Many FH files are full dumps of a debug build (they list every Debug*
  technique): 102 / 106 settings techniques have no FM4 shader and are not real materials.

## In the program (2026-11-01)

- `ShaderProfile.cs`: `ShaderProfile` (Fm2 / Fm3 / Fm4 / Horizon) and `ShaderProfiles.Detect(carbin)`, by the file's layout
  (type 2 + marker 0x2CA = FM2, `IsMotorsport3File` = FM3, 28-byte vertex = Horizon, else FM4). Checked on every
  car and wheel carbin: FM2 cars 310/310, FM3 3810/3810, FH1+FH2 8512/8512, FM4 3926/3926. Only 4 of 411 FM2 wheel files
  are not recognised (3 unreadable, 1 read as Horizon); an unreadable file falls back on the game folder
  (`GameInfo.Detect(GameRoot)` -> `ForGame`), and if that fails nothing is checked.
- Embedded lists (`docs\<FM2|FM3|HZN>\MaterialUse.txt`, `docs\<FM2|FM3|HZN>\SsParams.txt`, made by `gamescan.exe`), read by
  `ShaderProfiles.IsUsed` / `ParametersOf` / `IsParameter`. FM4 keeps `Fm4MaterialUse` and `Fm4Table`.
- Material check (red nodes, "N materials not in FM3/FH/FM2") and the Shader Settings panel (hints, unusual / unknown
  parameter marks, "Add" list) follow the profile of the shown model. FM2 has no LOD 0 car list, so nothing is flagged there.
- `ShaderLook.Apply` also reads the Horizon lamp colours, FM3's `GuageEmissiveScale` spelling and `CockpitSpecularColor`.
- Not done: FM2 older parameters (`Metal*`, `TwoTone*`) in the look; the FM3/FH technique lists as `ShaderLookNames`
  tables (class by name already covers them); asking the user when the profile is undecidable (never happened on the
  game files). FH1 vs FH2 need no distinction.

# Ports INTO FM2 (FM4 / FM3 / FH1 / FH2 -> FM2)

Opened 2026-11-22. Harnesses and outputs: `E:\DEV\fm4dev\PortToFM2`. Plan agreed with the user: the model first, then the
data; first car = the R33 Skyline (FM2 lacks it) on a copy of FM2's R34, then all the R33's data.

## 1. What an FM2 car is

- Car zip: one `.carbin` (LODs 1-5), `nodamage` / `damage` / `lights` / `damagelights` `.xds`, `LiveryMasks\` (six
  1024 x 1024 A8R8G8B8 `.xds` + `Masks.xml`), `Physics\MAXData.xml`, sometimes `CarAttribs.xml` (version 4) and
  `ShaderSettings.xml`. No LOD 0, cockpit, calipers or rotors.
- Wheel zip: one `.carbin` (one part `wheel`, all in the first shared buffer) + `wheel.xds`, `wheel0-3.xds`.
- Zips are deflate. Textures are as FM3's (linear DXT, sRGB values).

## 2. Feasibility study (read-only, `whole.exe`, `census.exe`)

The whole model of a source car built in an FM2 template with the Copy Part conversion gives a valid FM2 file (`Check()`
empty, rewrite identical): FM4 11 of 12 cars, FM3 5 of 6, FH1 6 of 6, FH2 4 of 6; wheels 6 / 5 / 6 / 6 of 6.

| Limit | Numbers | State |
|---|---|---|
| 65536 vertices per shared buffer (`FirstVertex` / `LastVertex` are 16-bit) | over it: FM4 7 of 313 cars, FH1 4 of 176, FH2 10 of 216; stock FM2 tops at 45457 / 56623 | user: port everything. Tried 2026-11-22: the opaque parts that do not fit put in the first (see-through) buffer: the R33's `bumperFrace` was drawn dark in game, so it is a hard limit (code removed). Now: stock parts first, the upgrade variants that do not fit are left out and listed; their upgrade rows are not ported (`CarDataToFm2.ModelParts`). The R33 in DEV has no `bumperFrace`: decision pending (leave out, or a lighter mesh) |
| one triangle with UVs wider than 50 (FM2 holds +-25) | 2 of 30 cars (FM4 `MOS_MT900S_10` headlightL, FH2 `MER_C63AMG_12` body) | refused; to do: split the triangle |
| parts no FM2 car has | `wheel`, `undercarriage*` in nearly every source main model | left out (user) |
| materials no FM2 car has | FM4: `bottom` 302 cars, `chrome_2` 119, `lights_glass` 69, `liveryBadge` 43, `headlight_paint` 25; FH adds its light materials | listed in the log; the user's fixes (Rename Material), a review grid later |
| two source parts with one name | FM4 R33 `glassLF` + `glasslF` (same vertices); FM3 wheel `POR_997GT3_07` two `wheel` | car: the first stands; FM3 wheel: open |

## 3. Step 1, built 2026-11-22 (model, wheel, textures, masks)

- `Fm2CarbinModelPort.cs`, `Fm2CarbinModel.ReplaceWholeModel`: template parts the source lacks are removed, every source
  part with a LOD 1-5 mesh replaces the template part of its name (place in the draw order, physics, glass shards kept)
  or is added (no mass, no shards). Vertices by `FromMotorsport4`, as Copy Part. Baked light: the baker (light sent
  back, ground) is fitted on the template before it is replaced, then every part is computed on the finished car, stock
  parts first (`ComputeLighting` with a given model). R33: band 0 of `body` 0.504 (FM2 R34 0.495, FM2 R32 0.495).
- `CarPortToFm2.cs`: rewrites the two zips of an FM2 car that exists (a Duplicate Car copy of the template): model,
  wheel (only when the wheel is the car's own), textures by name (`Fm3Textures.FromMotorsport4` for FM4 / FH, FM3 as
  they are), livery masks (tga pixels = the xds pixels: Supra and R34 in FM2 and FM3 100 % equal, `mask.exe`),
  `Masks.xml`, zipmanifest.
- Harnesses: `port2.exe` (into a trial folder), `install.exe` (Duplicate Car + port into a `_DEV` folder; refuses a
  running Xenia), `parts.exe`, `tex.exe`, `lightstat.exe`.
- **Installed 2026-11-22 in `FM2_Extracted_DEV`:** FM4 `NIS_SkylineGTRVSpec_95` (R33) as car 660 on a copy of
  `NIS_SkylineGTRVSpecII_02`, wheel 458; log `install_r33_DEV.log`. 50 parts, 6052 / 63534 vertices, 16 s.

In game (user, 2026-11-22): the R33 drives (prize of Arcade exhibition 1, event 300, instead of the Focus SVT); the
race front bumper in the see-through buffer was dark (see above); the side skirts were blotchy: the light scene took
`skirtLb` as fitted when the group has no `skirtLa` (fixed: `noStockWithoutA`, a group without "a" stands with none).
After both fixes: "paraurti argento, minigonna ok".

Wheel, 2026-11-22 (user: two odd things on the wheel in FM2 only, fine in FM4): baked light, normals (0 flipped triangles)
and rim textures checked fine. The tire was wrong: FM2's `tire` samples the shared `cars\shared\tireA*.xds` (tread V
0..0.44, sidewall facing -X 0.51..0.72, facing +X 0.75..0.97, 25 repeats on the R34); the FM4 tire maps each sidewall in
two halves of the circle on two V bands (FM4's own 1024x512 sidewall texture), so half the outer sidewall read the inner
band and one tread edge read the sidewall. Fix `Fm2CarbinModel.FitTireUvs` (called by the wheel port): every tire vertex
takes the template tire's V for its place (side + radius fraction, tread width fraction), U scaled to the template's
repeats (R33: 743 vertices, x 0.781). Applied to the DEV R33 wheel with `tirefix.exe` (backup in `bak\`); OK in game
(user, 2026-11-22: "gomma ok").

See-through buffer rule (2026-11-22, `bufcount.exe`, user pointed at the Integra): FM2's `acu_integrar_01` (81 parts)
has 44096 vertices in the first buffer and 37831 in the second; all 13 front bumpers are in the first buffer because
they carry the headlights' glass (detail_glass_clear / head_light). Over all FM2 cars, **3864 of 3864 first-buffer parts
have at least one see-through material** (glass, window, lexan, clear...): a part goes there only with one. The dark R33
`bumperFrace` was all opaque in the first buffer (its baked light was normal, band 0 0.50), so the rule stands. The R33
bumpers have no glass (its headlights are separate parts), so `bumperFrace` (4898) still needs the second buffer
(63534 used, 2002 free). FM2's fullest second buffer: `aud_tt_04` 56623.

User decision 2026-11-22: "faremo scegliere all'utente cosa togliere. per ora togli fb". R33 re-ported with
`port2.exe ... nowheel template=NIS_SkylineGTRVSpecII_02 skip=bumperFb` (`CarPortToFm2.SkipParts`): 49 parts, second
buffer 65256 / 65536, `bumperFrace` in; `data2.exe ... write` (backup `bak\gamedb_20261122_234906.slt`): front bumper
rows stock, PartsStringId 3 / 5 / 6 street, 27 race (level 3, 2500 cr), Fb's row (id 2) not ported; PI B 577, integrity
OK. In-game check pending. In the port window the user will pick which variants to leave out when the car does not fit
(to describe first).

User rule 2026-11-22: "dalla base va tolto TUTTO". Studied on the cars both games have (`basecmp`, `physrule`,
`glasscmp`, `wlight`): FM2 part Physics = float 0 (1 on bumpers / hood / trunk / mirrors / skirts / wings / exhausts, 0
on the rest: FM2's majority per group; FM4 holds 1.0 everywhere), centre of mass = mean of all the part's vertices minus
its origin (exact on 1867 FM2 parts), detach float (FM4 the same field). Glass shards: the same layout and box in both
games (FM4 finer). Emitters: FM4's CCarEmitterData = FM2's 140-byte record (equal on 10 cars); driver anchors = FM4
CDriverControls vectors 0-5. Now `ReplaceWholeModel` takes all of these from the source (`PhysicsAndGlassFromSource`,
`HeaderFromSource`); the template part only gives the draw-order place. R33 re-ported (car + wheel) in DEV: 1 emitter
(FM4's), anchors FM4's. **Still the template's: the wheel lighting** (FM2 4 x 16 floats, SH bands 0-3 per wheel; FM4
keeps 4 per wheel, bands 0-1, another scale: FM2 band 0 about 2.45, FM4 about 1.0): to study (compute with the baker
or fit from FM4's). Database: the columns only FM2 has still keep the template row's values.

In game 2026-11-23 (user): race bumper, glass, exhaust, driver, detaching parts all OK.

Wheel lighting study (2026-11-23, read-only):
- FM2 stored rows: band 0 about 2.4 (sd 0.19), coefficient 3 (x) +-1.23 by side, coefficient 8 about -0.6; the rest
  small. Row order = the same as FM4's rows (same signs of x and z).
- `wlfit` (25 cars, `Fm2CarbinModel.WheelLightParts`, the vertex baker's rays from the wheel centre taken from the
  database: sky up / free down / sent back / ground, fitted per coefficient): rms 0.22 on band 0 against a spread of
  0.19: no better than FM2's mean. Not the light at the wheel centre.
- `wlmap` (79 cars both games have, leave one car out): FM2's 16 from FM4's 4 of the same wheel, linear, rms 0.156;
  FM2's mean per row and coefficient, rms 0.158. FM4's values carry nothing more than the mean.
- So neither the source nor a computation gives the car's own values; the honest choice found: FM2's mean over its cars
  (not the template's). User 2026-11-23: "ok media FM2". Done: `Fm2CarbinModelWheelLight.cs` holds FM2's mean over its
  310 car models (`wlmean.exe`), set by `HeaderFromSource`; the study method `WheelLightParts` removed (`wlfit.exe`
  needs an older exe). R33 re-ported in DEV (car only, backup `bak\port_wheelLight`); in-game check pending.
  Nothing of the template is left in the model; only the DB columns FM2 alone has keep the template row's values.

Wheel baked light (2026-11-23, user: "precalc di merda sempre" on the R33 rim in game): the wheel port did not compute
the light, it copied it from the nearest vertex of the template wheel (then, on a re-run, of the already ported wheel).
Now the wheel goes through the same baker as the car (`ReplaceWholeModel` light = true, calibrated on the template
wheel's own values), and a re-run takes the template car's own stock wheel as template (`CarPortToFm2.wheelTemplateMedia`).
Checked with `wheellit.exe` on rims both games have: FM4 rim ported vs FM2's own of the same design, band 0 NSX 0.528 /
0.535 (min 0.408 / 0.375), F430 0.512 / 0.500 (min 0.408 / 0.336): FM2's rims carry real occlusion, the copy flattened
it. R33 written in DEV (backup `bak\port_wheelBaked`); in-game check pending. Also: the orphan engine 2151 of the first
data run deleted from every engine table, and `CarDataToFm2.DropOldOwnEngine` now does it on every re-run. Step 2 data also OK in game (drive, upgrade shop, tuning).

## 4. Step 2, built 2026-11-22 (database, `MAXData.xml`)

`CarDataToFm2.cs`. User rule: every value is converted on its own ("i calcoli sono tutti da fare con pezzo singolo"):
one FM2 row per source row (each part, each level), each column by the rule of its table, column and level, learned at
run time from the cars both games have (FM2 / FM4 188, FM2 / FM3, FM2 / FH1, FM2 / FH2 their own shared cars):
copy / FM2 constant / offset / median ratio / id map, and a **group value** (FM2's typical value for the drive type and
car type, used when it misses less than the ratio: `GameTorqueScale` is 0.9 front or rear drive, 0.85 all-wheel drive,
about 1 race cars in FM2, 0.7-1.0 per car in FM4). Columns FM2 has alone keep the template row's value.

Facts found on the way (each a bug before its fix):
- **Level numbers** mean the part's grade (0-3) in both games: the stock part's level depends on the car, no upgrade is at
  or under it (0 exceptions in FM2). Levels are converted by the most frequent offset (0; FM2 tire width / rim size start
  at 1); an upgrade at or under the stock level, or at a level no FM2 car uses (FM4's level 4), is not ported.
- **Rows are paired** stock with stock, then upgrades in order, body kit by `PartsStringId` (the same id = the same model
  letter in both games: id - stock id + a; race 27 / 54 / 81).
- **Front / rear tables** (anti-roll bars, rim size, tire width) become FM2's one row per level (`MergeSides`, section
  7c): a side with no part at a level keeps its part of the level below, adding no price or mass; the stock row is at
  the lowest stock level of the sides and holds both stock parts.
- **Numbers stored as text**: FM2 and FM4 keep `PartsStringId` of the body kit tables as text ("27"); read as numbers,
  written in the storage type FM2's template row has.
- **Ranges**: springs / anti-roll bars converted column by column could end with Min above Max; FM2 keeps Min = Def = Max
  where its cars do (stock and street levels), else Min <= Def <= Max.
- **Engine**: FM2 and FM4 build the torque the same way (Fm2Perf / Fm4Perf: curve x (1 + sum of TorqueScale - 1), turbo by
  the previous point's power), so the curves are copied (resampled when more than 151 values), camshaft -> CamValves,
  Displacement -> Block, TurboSingle / Twin / Quad -> Turbo; the stock TorqueScale of FM4-only parts (valves, pistons,
  manifold, oil cooling, restrictor) goes into the stock Block row; their upgrade levels and engine swaps are not ported.
  Own FM2 engine (CloneFm2 of the template's, then rewritten). Zero-throttle curve: the template's shape with the
  source's ZeroThrottleTorqueScale (FM3: its own curve).
- **Ids** a FM2 lookup lacks (FM3 FamilyModelID 26) keep the template's.
- `MAXData.xml` (`CarPortToFm2.MaxDataToFm2`): FM2 = the source's without the small Flags 1 spheres and without Flags;
  BoundingBox MinY = the lowest sphere (FM2's box is the body's, 26 of 40 cars).
- PI / class / ratings / Sim peaks: FM2's own calculation (Fm2Perf) after the data; StockGarage rewritten.

Checks (`dcheck.exe`, leave one out: a car both games have, converted from the source onto itself in FM2 without
learning from it, compared with FM2's real rows): FM4 10 / FM3 5 / FH1 4 / FH2 4 cars, integrity OK on all, no value
stored in another type than FM2's, stock torque and power equal to FM2's where both games have the same engine
(F430 465 / 465 N.m, 360 / 360 kW); PI near FM2's on road cars (FTO 0.267 / 0.259, 512TR 0.444 / 0.445, Tuscani
0.103 / 0.102), further on race cars and where the games' data of the car differ (Murcielago 0.505 / 0.558, LMP 0.846 /
0.988). What is left different is mostly the two games' own data of the same car (prices, 0-60 times, specs).
Copy Part regression (`pi5.exe`) 59 runs 0 fails.

**R33 in DEV (2026-11-22, `data2.exe ... write`, backup `bak\gamedb_20261122_220927.slt`):** own engine 2151, torque /
power 368 N.m / 239 kW = FM4's SimPeak, PI B 577 (0.3749), integrity OK; not ported: race front bumper (the model has
none), wing PartsStringId 80 and tire compound level 4 (FM2 has no level 4), engine valves / pistons / oil cooling
upgrades and the swap. `MAXData.xml` from FM4 (20 spheres, MinY -0.1008).

## 4b. Database columns without a source rule (2026-11-23, user: "procedi sì")

No column keeps the template row's value any more (`CarDataToFm2.Typical.cs`, `CarDataToFm2.Specific.cs`):
- **FM2 typical** (`NoRule`): no source column, or no rule learned: FM2's value over its own cars (keys up to GameIdLimits,
  never a ported car; stock / upgrade rows apart): one value on 90 % of the rows = game constant (DragScale, PartsStringID,
  DamageModDefIDs, FrictionTorque, AssistsSTM, GasBurnRate, cam Follow / Bumper, AI shift rpm...); Data_Car first the
  cars of the same drive type | car type (8 or more); ids / text: most common; numbers: most common when it holds half,
  else the median. A column whose values are all different (TorqueCurveZeroThrottleID: the link to the car's own curve)
  keeps the row's own value. Physics side columns (AxleInertia) use their physics table.
- **Renames** for the learner: Dist:BrakeFrom60 / 100 <- SimBrakeDistance60 / 100MPH, LateralG <- SimLatGees60MPH,
  TurnsLockToLock <- SteerMaxAngle (ratio x0.0667 learned), DragCoefficient <- BodyAeroLongitudinalDrag (ratio x0.00188);
  braking / LateralG: FM2's group value wins (FM2 holds 0 on many of its spec sheets).
- **SpeedInGear1..7-mph**: RedlineRPM / 60 x 2 pi r / (GearRatio[n] x final), r = driven tire (front for front drive),
  GearRatio0 = reverse, FM2's NumGears counts reverse (305 / 305): FM2 755 / 1702 gears within 2 mph, median 2.9 mph
  (`gearchk.exe`, `numgears.exe`).
- **Bore / Stroke**: pi/4 b^2 s cylinders = Disp_cc (FM2 median 1.000), bore / stroke 1.054 (FM2 median); rotary: typical.
- **EngineName**: "" (FM2 80 %; FM4's text is a description, "V6TT - Nissan GT-R '10").
- **Engine maker**: stock List_UpgradeEngine.ManufacturerID = Data_Car.MakeID (FM2 310 / 310).
- **Carbureted**: the source's Data_Engine.Carbureted; FM2 puts injection on fuel system level 3 of carburetted engines.
- **Hood camera** (`hoodcam.exe`, 228 FM2 cars, leave one out): height = -0.322427 + 0.684925 x hood top (error 0.039
  vs 0.053), z from wheel / seat / hood front / hood back / windscreen front z (0.143 vs 0.195); needs `Model`.
- Also: the previous own engine is deleted at the END of WriteEngine (it held the zero-throttle shape WriteCurves reads).
Checks: `dcheck` 8 FM4 cars integrity OK, 0 fails, 0 type mismatches; `pi5` Copy Part FM4 30 / FM2 110 runs 0 fails.
Zero-throttle curve (2026-11-23, done): when the source has no curve of its own (FM4 / FH), FM2's median shape over its
321 stock zero-throttle curves (`zeroshape.exe`; leave one out 0.064 vs 0.091 of another engine's shape = the template's
way) with the source's ZeroThrottleTorqueScale (else FM2's median 161.964): `Fm2ZeroShape` in CarDataToFm2.Specific.cs.
Nothing of the template is left in the data either. Written for the 5 cars (engine ids kept: 2152 / 2153 / 2154 / 2158 /
2159), integrity OK; in-game check pending. User 2026-11-23: no savegame protection for upgrade rows (not our job).

Cars in DEV 2026-11-23 (data rewritten with these rules): R33 660 (engine 2155), GT-R SpecV 661 (2156, template R34),
458 Italia 662 (2157, template F430; 10 grille1 triangles of LOD 3-5 wider than 50 UV: centred, held at +-25, the
port refused the car before), H1 Alpha 663 (2158, template R34: PI D 102), Q7 V12 TDI 664 (2159, template RS6: 987 N.m
vs FM4 1000, the curve resampled to 151 points). In-game check pending; only R33 has an Arcade prize slot.

Crash 2026-11-23 01:19 (FM2 at boot, "saving game data", read at 0x100000000 while saving the PlayerDatabase): the
user's profile held the 458 (LivCatalog662, saved 00:55, engine 2154) and the R33; the 01:10 data re-run gave the 458 a
new engine 2157 and deleted 2154. Fix: `CarDataToFm2` now gives the re-run's new engine rows the old id back
(`RenumberFm2Engine` after `DropOldOwnEngine`); DEV put back with `engfix.exe` (2155->2152 R33, 2156->2153 GT-R,
2157->2154 458; upgrade row ids equal to the 01:09 db the save saw; backup `bak\gamedb_20261123_012725_engfix.slt`).
OK in game (user 2026-11-23: boots, no crash, 458 and R33 in the garage). Risk left: a re-run that drops an upgrade row (bumperFb) breaks a saved car fitted with it.

## 5. Open

Closed in game since this list was first written: the R33 race front bumper with bumperFb left out (2026-11-23), the
model header and part physics from the source (2026-11-23), the sound as PCM banks (2026-11-24, section 6; memory fine,
no XMA1 repacking needed).

- Port window: built 2026-11-24 (section 7); the user's check in the app and of the Cobalt SS (car 668) in game is
  pending.
- Names, thumbnail.
- The zip's ShaderSettings.xml is still the template's (FM2 per-car shader file; the source has none).
- Rim size upgrade on a converted wheel (FM2 has no morph table): to study. FM2's own rim / tire size rule is not
  measured yet (FM4: rim 1.084 x nominal).
- Split of a triangle with UVs wider than 50; FM3 wheels with two `wheel` parts; wheel textures keep the source's mip
  levels (FM2's have one).
- Light fitted on the source's own values instead of the template's baker (FM4 / FM3 hold bands 0-1).
- Materials grid: built 2026-11-24 (section 7b); the user's check in the app and of the RS 4 Avant (car 669) in game is
  pending.
- Combo_Colors_Exceptions (FM4 second colour / stripes): FM2 has no such table, not ported.
- List_Wheels.IconPath stays the template's picture (FM2 has no picture of the source rim).

## 6. Colours, rim paint mask, engine sound (2026-11-23/24)

In game 2026-11-23 (user): GT-R, 458, H1, Q7 OK (model, rims, hood camera, gear speeds, driving), engine braking normal.
Then: "i colori del gioco source non vengono portati", "i cerchi della GTR sono bianchi ma i blur vengono neri?".

**Colours.** Combo_Colors was the template's (Duplicate Car). FM2, FM3, FM4, FH1, FH2 store RGB the same way (F430,
Supra RZ, R34: the same numbers in FM2 and FM4). `CarDataToFm2.Colors`: the source car's rows (Sequence, Metallic, RGB,
BaseTexture, DamageTexture). Also `WheelMass` (List_Wheels.Mass of the own stock wheel = the source wheel's) and
`EngineMedia` (stock engine MediaName = the car's: FM2 310 of 310; it names the DSPTuning file).

**Rim white, blur black.** The FM4 GT-R SpecV rim is BLACK (its blur textures wheel0-3 are a black six-spoke rim; rim
texels of wheel.xds RGB 79, alpha 24). FM2 reads the alpha of wheel.xds as the rim's paint mask: 275 of 282 stock wheels
have alpha 255 everywhere, the 91 aftermarket wheels with alpha all have List_Wheels.RGB (the paint), 7 stock ones alpha
with RGB 0 (`snd\xdsdump.exe` over FM2_Extracted\Media\wheels, `fm2_wheeltex.txt`). Ported as is, the low alpha made FM2
paint the rim. Fix `CarPortToFm2.OpaqueAlpha`: the ported stock wheel.xds gets alpha 255 (DXT5 alpha blocks), wheel0-3
(alpha = see-through) untouched. In DEV for the 5 cars (`port2`, backup `bak\port_rimalpha`), check pending.

**Engine sound: study** (`snd\sndcensus.exe`, 20 cars common to the five games, `snd\sndcensus.txt`):

| | FM2 | FM3 | FM4 / FH1 / FH2 |
|---|---|---|---|
| link | Data_Engine.MediaName -> DSPTuning.zip `<car>_DSPTuning.xml` | ET | ET (EngineTuning.zip) |
| banks per car | ONE per exhaust level: EngineA + EngineB + ExhaustL (+ ExhaustR) | 3 (Exh, Int, EngAmb) | 3 |
| bank format | FSB3 v3.1, XMA1, mono, mostly 48 kHz, LOD1 + LOD2 | FSB3 XMA1 (160) / FSB4 XMA2 (174) | FSB4 XMA2, 36 kHz, exhaust often stereo |
| DSP | per exhaust level 0-3 + engine level 0-3 (EngineA / EngineB): Volume, PEQ, Lowpass | in the HTs | ET HarmonicTunings EmissionGroup0/1/2 (= intake / ambient / exhaust, equal to the HTs' DSP) |
| emitters | EmitterData.zip `<car>_ED.xml` (4 CarListener) | | CarModelTuning.zip `<car>_CMT.xml` (Listener Basic Front / Follow / Cockpit / Default) |

FM4 = FH1 = FH2 for the common cars (same banks and HTs); FM3 = the same recordings at 48 kHz; FM2 = other recordings.
FM2's FMOD (default.xex, decrypted `fm4dev\ST02\pi\xex\fm2_base.exe`) knows only "FSB3" / "FSB2" (FM3's also "FSB4") and
has the FSB, WAV / raw PCM and XMA codecs, no ADPCM. No XMA1 encoder exists here (xma2encode only).

**Engine sound: built** (`Fm2SoundPort.cs`, harness `snd2.exe`): source banks decoded by vgmstream, one FM2 FSB3 bank per
distinct exhaust level (named after the source exhaust HT), 16-bit big endian PCM (bank flag 8), LOD2 = half rate;
EngineIntake -> EngineA, EngineAmbient -> EngineB, stereo exhaust -> ExhaustL + ExhaustR (right = wave index + samples;
FM4's mono banks are addressed the same way: same sample). HT loops keep the source's rpm / volume / pitch. DSPTuning
from the ET (Volume, PEQ, Lowpass; Expander, LoadPEQ, burbles have no FM2 place), VolumeAdjustments -> the exhaust
upgrade attributes. ED: positions, orientations, cones, listener volumes and pans from the CMT; the rest (emitter
distances, listener up / rotation) FM2's median over its 329 ED files. Offline: vgmstream reads the banks as FSB3
16-bit BE PCM, audio equal to the source. In DEV 2026-11-24: R33 6T2_RB26_o1043_Exh (5.3 MB), GT-R 6N1_R32_GTR_Exh (3.9),
458 8N2_F430_Italia_Exh (4.4), H1 8D1_Duramax_Exh (3.8), Q7 12N1_DB9_Exh (5.3); backups `bak\snd2_*`. FM2's own banks
are about 1.2 MB XMA: memory is the risk of PCM.

In game 2026-11-24 (user): big endian PCM + bank flag 8 = only hiss, no engine. Test round written the same day (Xenia
closed), one byte order per car (`snd2 ... pcm=`): R33 and Q7 little endian / flag 0, GT-R big endian / flag 0, 458
little endian / flag 8, H1 big endian / flag 8 (the hissing one, as a control). Result (user 2026-11-24): R33 and Q7
SOUND = **16-bit little endian PCM, bank flag 0** (the code default now); GT-R, 458, H1 rewritten that way, check pending.

**Q7 without front calipers** (user 2026-11-24; FM4 has them, FM2's Jaguar XK '07 on 21" has them, the other ported cars
have them). FM2 draws calipers with 2, 4 or 6 pistons only (List_UpgradeBrakes 895 of 895 rows) and sizes 8-15; FM4's Q7
front: 8 pistons, size 16. `CarDataToFm2.BrakeRange`: pistons -> FM2's nearest, size held within FM2's range (values
from the FM2 database). Q7 front now 6 / 15 (DEV), check pending; the other four cars were within range.
User 2026-11-24: all five cars sound, the Q7 has its front calipers.

**Other-game tests 2026-11-24** (whole chain: install, data2, snd2; integrity OK):
- FH2 McLaren P1 `MCL_P1_13` -> 665 on `MCL_F1GT_97`: 52346 + 21294 vertices, PI S 948, 2 colours, sound 8N1_Z8_1130_Exh
  (4.9 MB). 15 FH2 materials FM2 lacks (drlwhite, indicator_left/right, xenonhead, taillight2S...): Rename Material (user).
- FM3 BMW X5 xDrive48i `BMW_X5_09` -> 666 on `AUD_RS6_03`: 20439 + 22078 vertices, PI D 258, 10 colours, sound
  8S2_SVTCobra_a_Exh (5.9 MB at 48 kHz, DSP from the FM3 HTs, FM3 has CMT files too).
- FH1 DLC Ford GTX1 `FOR_GTX1_06` (pack offer 1500, BC6F8A24) -> 667 on `FOR_GT_05`. A staged pack holds only its own
  rows and CarDataToFm2 learns from the cars both games share: `SourceDatabaseMerge` (app) lays the pack's merge over
  the base FH1 database (pack rows replace the base rows with the same first-column key, as the game mounts them);
  harness `gtxsrc.exe <pack> <FH1 root> fh1gtx\src FOR_GTX1_06` stages the pack (DlcSource.Stage + ExtractMedia of the
  car and its wheel) into `fh1gtx\src` with that merged database (183 cars). `Fm2SoundPort.SourceFallbackRoot`
  (`snd2 ... base=<FH1 root>`): the pack's ET / CMT, the base game's HTs / banks. 15876 + 22396 vertices, PI A 802,
  10 colours, sound 8N3_PanozGTLM_348_Exh. 13 FH1 materials FM2 lacks (fogwhite, hidhead, slinlorange...): Rename Material.

**P1 crash on a colour change** (user 2026-11-24): its Masks.xml (FH2's) has Wing valid="true", the zip had no
wing.xds (the template McLaren F1 GT has no wing mask; the port only replaced the template's masks).
`CarPortToFm2.MaskSet`: a mask the source has and the template lacks is added (P1: Wing.tga), a valid mask without a
file is marked invalid, a template's mask the Masks.xml calls invalid is removed (GT-R, H1, Q7 had the R34 / RS6
wing.xds). Re-ported in DEV (`bak\port_masks`): masks and Masks.xml now agree on all ported cars. User: P1 colour
change OK; all cars sound.

**GTX1 crash, invisible** (user 2026-11-24): its colours (the pack's) name nodamage_quicksilver / _black / _gold / _blue,
the zip held the template Ford GT's nodamageblack / grey / blue / _nostripe. `CarPortToFm2.VariantTextures`: the paint
variant set is the source's (missing ones added and converted, the template's removed). GTX1 re-ported (`bak\port_variants`),
every Combo_Colors texture now in the zip (checked on all 8 cars). User: GTX1 now visible, but FM2 crashed when
driving it. Harness `fm2rows.exe` (every row of a car against FM2's own cars: ids FM2 never uses, numbers outside its
range, NULLs) on P1 (works) and GTX1: only the GTX1 had NULLs, List_UpgradeEngineTurbo.ManufacturerID levels 1-3 (the
pack's maker 167 is no FM2 id, the rule keeps the template's, the template Ford GT has no turbo rows -> NULL; FM2 never
has NULL there). `CarDataToFm2.Convert2`: no value and no template row -> FM2's typical value (105 here). Written;
fm2rows: no NULL left on the 8 cars. Then the GTX1 crashed FM2 on "Sali sull'auto" in the garage: the saved car had
been bought while 667 was still the Ford GT copy, its saved stock levels (brakes, clutch, springs, anti-roll 1; tires,
driveline 1) do not exist on the GTX1 (2 / 0). A NEW GTX1 works (user 2026-11-24): saved-game case, no tool change
(user rule: no savegame protection).

**Other-game routes into FM2 closed by the user 2026-11-24:** FM4 (5 cars), FH2 (P1), FM3 (X5), FH1 DLC (GTX1). Left: the zip's ShaderSettings.xml is
still the template Ford GT's (FM2 per-car shader file; the source has none).

## 7. The port window (2026-11-24, user: "parti da A")

Top bar > Cars > "Import car from another Forza, into:" > **FM2** opens `CarPortFm2Form` (the FM3 window's layout:
source folder, FM2 folder, Load Cars, car list with search and "Include the game's DLC", FM2 template car, Trial Run,
Install In FM2..., Report tab). Cars FM2 already has under the same MediaName are not listed.

- **Trial Run** writes nothing: `CarPortToFm2.Plan` (`Fm2CarbinModel.PortPlan`) converts every source part against the
  template and gives its vertices and its buffer (see-through = first, opaque = second, 65536 each); the report also
  holds the Duplicate Car plan of the template copy.
- **Variants tab**: one row per upgrade variant (`IsUpgradeVariant`: kit letter other than a, or `...race`) with a
  Port tick, the vertices and the buffer; two counters above (green, red over 65536). Every variant starts ticked:
  leaving one out is the user's choice (rule of 2026-11-22), the tool picks none. Install stays off while a counter
  is over, and needs a trial against the template chosen. The unticked parts go to `CarPortToFm2.SkipParts`.
- **Install** asks, checks Xenia (`XeniaGuard`), then runs `CarPortFm2Chain`: the plan checked again, 1 Duplicate Car
  of the template under the source's MediaName / name / year, 2 `CarPortToFm2`, 3 `CarDataToFm2` + `Fm2Perf` +
  StockGarage + integrity (database written only if it passes; copy in `bak\fm2port_<time>`), 4 `Fm2SoundPort` and
  Data_Engine.MediaName. A failed sound step is reported and does not undo the car.
- **DLC cars**: `CarPortFm2Chain.StageDlc` does what `gtxsrc.exe` did (stage copied to
  `%TEMP%\ForzaStudio\Fm2PortSource\<pack>`, database = the game's with the pack's merge over it, car and wheel taken
  out as zips); the game folder is the sound fallback. Not run yet from the window.

Checks: `plan.exe` (read-only) gives 22227 / 18006 for the 458 and 1154 / 68432 for the R33 (65256 without bumperFb):
the numbers of the real ports. `E:\DEV\fm4dev\ui\shotfm2.exe` renders the window off screen (Cobalt SS: 97,367 opaque,
Install off; 8 front bumpers unticked: 63,318, Install on). `chain.exe ... write` ran the four steps for the FM4
Cobalt SS `CHE_CobaltSS_10` -> car 668 on `AUD_RS6_03` in DEV: 16502 + 63318 vertices as counted, PI C 491, sound
4N2_Ecotec_305Stock_Exh (log `chain_CHE_CobaltSS_10.txt`). The window's own Install click and the car in game: user.

User 2026-11-24: Cobalt SS OK in game.

Not in the window yet: a second port over a car already installed (to change the variants or the materials the car
must be removed first).

### 7b. Materials tab (2026-11-24, user: "vai con B")

The trial also plans the stock wheel (`CarPortToFm2.PlanWheel`) and lists each source material, per part and LOD
(`PlanPart.Materials`). **Materials** tab: one row per material FM2's own cars do not use in that list (car: car_LOD1-5;
wheel: wheels_LOD1-5 of `docs\FM2\MaterialUse.txt`, the rule the editor's tree uses for FM2 models): Where (car /
wheel), the material, the parts that use it, and a drop-down **FM2 material (your choice)** that starts on "(leave as
is)" and offers the techniques of FM2's car effect (`ShaderTextures.Motorsport2TechniqueNames`) that FM2's cars
(wheels) use. The tool chooses nothing. The choices stay with the car's trial and are used by the next trial and
the install (`CarPortToFm2.CarMaterials` / `WheelMaterials` -> `Fm2CarbinModel.PortMaterials`: each converted part's
meshes are renamed BEFORE the part goes into a buffer, so a see-through choice moves the part to the see-through
buffer; the counters and the Variants tab follow at once).

Checks: `shotfm2.exe` with FH2 Audi RS 4 Avant `AUD_RS4Avant_13` on `AUD_RS6_03`: 8 rows (bottom, chrome_2, drlwhite,
grille2_alpha, indicator_left / right, taillightst, xenonhead; wheel none), 38 choices each; bottom -> detail_glass_clear
moved 28,719 vertices from the opaque to the see-through counter. `chain.exe ... write mat=drlwhite:head_light
mat=xenonhead:head_light mat=indicator_left:detail_glass_amber mat=indicator_right:detail_glass_amber
mat=taillightst:tail_light` (test choices, not a recommendation) installed it as car **669** in DEV: 37 meshes renamed,
none of the five names left in the written model, model check OK, PI B 668, sound 8N0_S4V8_O1002_Exh. In game: user.

User 2026-11-25: the window clicked through in the app OK (Trial Run, Variants, Materials, Install), RS 4 Avant 669 OK
in game. User 2026-11-26: a DLC car ported from the window, OK in game.

### 7c. Front / rear upgrade rows (2026-11-25, user: "problemi su swaybar upgrades", rule A: "A, vai pure")

FM4 / FM3 / FH1 / FH2 keep front and rear anti-roll bars, rims and tire widths in two tables, and FM2 keeps both sides in
one row per level. The two sides were merged by level number, which went wrong when the sides differ:
- **Anti-roll bars, different stock levels.** The FM4 Peugeot 107 `PEU_107_11` (user's own FM4 car, car **670** in FM2
  DEV) has its front stock bar at level 1 and its rear stock bar at level 0, plus a rear-only level 1 bar (500 cr). The
  same shape is on 22 FM4 cars (Elise 111S, Eclipse GTS, 510, DMC-12, ...), 4 FM3, 7 FH1 and 15 FH2 cars
  (`frcensus.exe`). Merged by level, level 1 became a second stock row with the rear bar's price, and level 0 kept the
  template's front bar. No FM2 car has two stock rows (`levels.exe`).
- **Rims / tire widths, one side with more levels.** In 93 FM4 cars (rims) and 141 (tire widths) one side has more
  levels. The rows of the extra levels had no column for the other side, and that side kept the template's value. For
  example, FH2 P1 665 had a rear rim of 19 (the template's) at level 3, next to the 21 front.

Rule A (user's choice): `CarDataToFm2.MergeSides` keeps every part of the source.
- The stock row is at the lowest stock level of the sides and holds every side's stock part.
- Each level where a side has an upgrade becomes one row. The other side keeps its part of the level below, or its
  stock part if it has no upgrade below, and adds no price or mass. FM2's own rows do the same: on `FER_Enzo_02`, the
  front rim stays at 20 at the level where the rear goes to 21.
- The 107 becomes 0 stock, 1 (the rear street bar only), 2, 3. FM2 has that pattern on 10 of its cars.
- Rule B, not chosen, would have followed FM2's own Elise and Eclipse: stock at level 1, and the rear-only bar left out.
- Sides with the same levels give the rows the old merge gave.

Checks (`swayfix\run.ps1`: `data2.exe` dry runs of the 11 ported cars, old and new exe):
- 107: sway rows 0 stock / 1 (150 cr, the price rule's) / 2 / 3, front and rear from the source.
- 458 662, P1 665 and GTX1 667: the tire / rim rows of the extra levels now have the source side's own last value
  (P1 rim level 3 is 21 / 21, before 21 / 19).
- RS 4 Avant 669 and X5 666: one sway upgrade price moves by a few credits (1792 -> 2016, 477 -> 481), because the
  learned price rule now pairs Elise and Eclipse right.
- R33, GT-R, H1, Q7 and Cobalt: no difference.

Written to DEV 2026-11-25 (`data2.exe ... write`, ids kept, integrity OK, backups in `swayfix\new\bak`): 670, 662, 665,
667. The 107's values then showed a second fault, below.

**FM2's upgrade ladder** (user: "sì, vai con la regola FM2 e guarda anche le molle"). FM2 builds its upgrade anti-roll
bars and springs / dampers from the same car's stock part. Measured on its 310 cars (`swayfix\ladder.cs`,
`springs.cs`):
- **Fixed factors, every car:** bar stiffness, spring rate and damper rates are stock x1.10 / 1.20 / 1.50 above a level
  0 stock part, x1.11 / 1.44 above level 1, x1.375 above level 2. For the spring rate the level 1 -> 3 step is x1.44
  front and x1.50 rear.
- **Same as stock:** the geometry columns (camber change, caster, pivots, bump stops, toe, ...).
- **Not a factor:** ride height (about x0.95 / 0.85 / 0.75) varies, and so do the race level's adjustable Min / Max.

FM4's upgrade bars are 2-7 times the stock bar. The per-row ratio learned from the shared cars had to squeeze them, and
the 107's level 1 bar came out softer than the stock: 1.81 / 3.36 against 4.36 / 8.82.

`CarDataToFm2.LearnLadders` learns this from FM2's own cars: per side, column and level step (stock level -> level),
"= stock" or "stock x factor" where 95% of at least 5 cars agree. `ApplyLadder` then builds each upgrade physics row from
the car's converted stock row. Columns without a ladder (ride height, race Min / Max) keep the row conversion. Result:
315 columns with a ladder (216 = stock, 99 x factor).

**Learning only from FM2's own cars** (`LearnAll`): cars with an id above FM2's base game (617,
`GameIdLimits.MaxCarBase`) are left out. A ported car has the source's MediaName, so it counted as a car "both games
have" and taught the conversion its own output.

Check (`swayfix\new2`: `ladchk.exe`, the database step in memory, all 14 ported cars 660-673): no upgrade below the
stock. The 107: bars 4.37 / 8.82 stock, then x1.10 / 1.20 / 1.50; springs 56.4 / 34.6 stock, then the same factors.
Integrity OK on all.

Written to DEV 2026-11-25 for all 14 cars (660-673, `new2\data2.exe ... write`; database before:
`new2\gamedb_before_ladder.slt`). Checked on the written database:
- `ids.exe`: every upgrade id kept.
- `dbladder.exe`: one stock row per table and no upgrade below the stock, front and rear, bars and springs.

In game: user.

### 7d. Traffic cars in the car list (2026-11-25, user: "non vedo le cose degli altri forza come giallo per traffic car")

FH traffic cars (placeholder close-up models: `CarPort.HasPlaceholderCloseUp`, or the index of a staged package) are
listed in yellow, with a "Traffic car" legend at the right of the Cars header, found as the FM3 window finds them
(`CarPortFm3Form.IsTraffic`). Off-screen check (`ui\shottraffic.exe`): FH1 source, 16 yellow rows of 134. Porting a
traffic car into FM2 has not been tried yet (user: next).

### 7e. Engine sound choice and FM4 expansion packs (2026-11-25)

**Port sound / Use sound** (user: "nel port mi devi consentire di fare port sound -> use sound", for the traffic cars'
odd sounds). The "Selected car" area has an **Engine sound** row:
- **Port sound (the source car's)**, the default: step 4 as before, the source's banks rebuilt for FM2.
- **Use sound of the FM2 car:** a drop-down of FM2's cars. It starts on the template car until the user picks another.

FM2 finds a car's sound by `Data_Engine.MediaName` (= the car's) in `DSPTuning.zip\<car>_DSPTuning.xml`, which names
each exhaust level's HT and bank (section 6). "Use sound" (`Fm2SoundPort.UseSoundOf`, `RunUse`) copies the chosen car's
DSPTuning under the new car's name (root `name` attribute changed) and builds no bank. `<car>_ED.xml` (emitter
positions, the car's own body) is still built from the source's CMT. The install question and the Report name the
sound used.

Check `PortToFM2\usesnd.exe` (Write = false): FH1 Prius with the FM2 RS 6's sound gives a DSPTuning with banks
AUD_S4_04_111303_8S0 / SPY_C8_00_Bizarre_8S2 / ..., and an ED from the Prius CMT (217 values). Not yet tried in game.

**FM4 expansion packs** (offers 1-4, e.g. "2006-Current Model Year Car Pack" with the M5 2009 `BMW_M5_09`; user: M5 E60
not listed). These packs have no database of their own: their cars' rows are in FM4's. The window did not tell
`DlcSource` which FM4 folder to take them from, so the pack could be left out ("set the Forza Motorsport 4 folder").
`LoadCars` now calls `GameInfo.UseSourceFolder(game, source)`, as the FM4 port window does. Checks: `ui\fm4packs.exe`
finds the pack with 56 cars including the M5. `ui\stagem5.exe` stages it as Install does: car zip extracted, 1
Data_Car row, 0 tables with a different row count. `plan.exe` on the stage: 3591 see-through + 33787 opaque vertices,
fits.

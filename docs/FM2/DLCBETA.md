# FM2 DLCBeta -> FM4

Status 2026-11-09: **paused, cars installed in FM4_Extracted_DEV but NOT visible in game.** Code change uncommitted.

## The package

An unreleased Forza Motorsport 2 car pack (never published), found by The Admiester
(https://www.theadmiester.co.uk/). It is the ONE unofficial FM2 package the tool accepts, and only for FM2 -> FM4.

Location (Xenia content): `F:\Xenia\Emulators\Xenia Canary\content\0000000000000000\4D5307EA\00000002\DLCBeta`

```
Media\DLCZips\zipmount.xml               DLCBetaOverrides1_pri_999.zip -> game:\Media\StringTables\
Media\DLCZips\stringtables.xml           <table Name="data_car" />
Media\DLCZips\DLCBetaOverrides1_pri_999.zip   <LANG>/Data_Car.str (14 languages, full FM2 table + the 2 cars)
Media\DLCZips\DLCBeta_pri_65\media\      LOOSE folder (not a zip):
    cars\MAZ_Roadster_DLC1\, cars\MER_CL600_DLC1\   carbin, xds, CarAttribs v4, LiveryMasks, Physics\MaxData.xml
    wheels\<car>\                                   wheel carbin + xds
    db\patch\70001_merge.slt                        own rows, FM2 schema, NO ContentOffers table
    UI\Textures\Thumbnails\Thumbnail_626/1001/1011.xds   (1011 has no car)
    Audio\cars\...                                  EmitterData, DSPTuning, HarmonicTuning, Soundbanks LOD1/LOD2 (.fsb)
Media\LicenseMasks\00000001.sql, 00000002.sql   UPDATE Data_Car SET IsPurchased=1 ... + StockGarage inserts
```

ContentSummary.HTM: build 96445, branch F2Ship, 2007-11-27.

| Car | FM2 id | Wheel | Stock engine | Swap | Names (table A949) |
|---|---|---|---|---|---|
| MAZ_Roadster_DLC1 | 626 | 419 | 405, I4 1999 cc NA | 179 (13B-REW, same id+name in FM4, kept) | "MX-5 Roadster Coupe" / "Mazda MX-5 07" |
| MER_CL600_DLC1 | 1001 | 420 | 449, V12 5513 cc twin turbo | - | "CL600 Coupe" / "Mercedes CL600" |

String keys: `_&2840149197` = 0xA9490000 + index (15565). After the port they are 0xCD49xxxx, same index.

## Why the tool did not see it

- `DlcSource.ReadOffer`: the layer `DLCBeta_pri_65` is not hex and not `DLC<n>_pri_<P>.zip` -> offer -1, not a package.
- `DlcSource.ReadTitle`: merge without ContentOffers + `Media\LicenseMasks` -> it would have been read as FM3.

## Code change (ForzaStudio/DlcSource.cs, +41 lines, uncommitted)

- `IsDlcBeta(zips)`: true when DLCZips holds the folder `DLCBeta_pri_65`.
- `ReadOffer`: DLCBeta -> offer 70 (merge 70001 by the n*1000+x rule, as FM3 22005 = offer 22).
- `ReadTitle`: DLCBeta -> title 4D5307EA, game FM2 (needs Folder.FM2 in ForzaStudio.ini).
- Everything else is the existing FM2 DLC path: `MergeFm2Database` (FM2 gamedb + 233 rows of the merge),
  string override through the layer mount, `Fm2Source.Stage` with overlay (loose thumbnails), `CarPort.Fm2Data`.
- Built (msbuild x86) into bin\Debug.

## Tests (E:\DEV\fm4dev\FM2_FM4\dlcbeta)

- `betatest.exe <folder>`: DlcSource.Find + Stage + DB check. Result: offer 70, FM2, 2 cars, stage in
  `%TEMP%\ForzaStudio\DlcSource\DLCBeta`. LicenseMasks are NOT applied (0 SQL statements: the folder is outside
  DLCZips, the stage does not copy it) -> IsPurchased 0 in the stage, irrelevant for the port.
- `strtest.exe <Data_Car.str | StringTables zip> [keys]`: prints the car names.
- `port.exe <stage> <FM4 root> <car> <donor> <trial | ->` (copy of FH2toFM4\tools\port.cs, needs port.exe.config
  with useLegacyV2RuntimeActivationPolicy). The harnesses need a copy of bin\Debug\ForzaStudio.ini next to them.
- Trials OK: `trial_roadster` (donor MAZ_Roadster_07), `trial_cl600` (donor MER_CL65AMG_10).

## Install in DEV (2026-11-08 23:55, Xenia closed)

Logs: `install_roadster_DEV.log`, `install_cl600_DEV.log`.

| Car | FM4 id | Class / PI | StockWheelID | Engine |
|---|---|---|---|---|
| MAZ_Roadster_DLC1 | 1638 | D, 0.251 | 11212 | 1638 (+ swap 179) |
| MER_CL600_DLC1 | 1639 | B, 0.399 | 11213 | 1639 |

## OPEN: the cars do not show in game (user, 2026-11-09)

Checked and fine:
- Xenia log: the game loaded `F:\Xenia\Games\FM4_Extracted_DEV\default.xex` and opened `game:\media\db\gamedb.slt`
  (the only .slt); no car file of the two was opened in that session.
- Data_Car 1638/1639: IsPurchased 1, IsInstalled 1, IsDrivable 1, ContentId 0, IsUnicorn 0, IsArcade 1 (every port sets 1).
- Lookups valid: PowertrainID 1 (Powertrains RWD front), MakeID 26/28, FamilyModelID 1 (302 FM4 cars use it,
  donors had 24/33), TireBrandID 12 / 0 (0 used by 130 FM4 cars).
- List_UpgradeEngine stock rows present; Data_Engine 1638/1639 present.
- Combo_Engines: no rows, but the donors 626/1225 have none either (311 of 505 cars have rows).
- dbaudit vs original FM4: only EventParticipants (career, always skipped) and List_UpgradeEngineManifold missing.
- Car + wheel zips in Media\cars / Media\wheels, zipmanifest.xml lines present, Textures.zip rewritten.
- Names present in DEV IT.zip and EN.zip (15565, 63992, 36447, 5309).
- No other ported car currently in DEV (ids >= 1400 are all FM4's own), so no known-good case to compare with.

Questions for the user when resuming:
1. Where were they looked for (dealer under Mazda / Mercedes, Free Play, Garage)? The Garage lists owned cars only.
2. Was Xenia started after the install (23:55)? The log ends at 00:00, so probably yes.
3. Install a known-good port (e.g. an FH car) next to them to see if it shows in the same place.

Next ideas if still hidden: compare every Data_Car column and every car-keyed table of 1638 with a ported car that
was visible in game (Veilside 4008, FM2 game path, 2026-10-21; it had Combo_Engines rows); the FM2 DLC path itself
was never checked in game before this pack.

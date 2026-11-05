# Make titles and decals: where we are, what to test

Started 2026-10-31 from an outside tip (BlackBirdOsaka, TXR): a new brand needs its earnable titles
and its livery decals. Details of the formats are in `docs/MAKE_TITLES.md`. This file is the resume
point: state, the tests still to do in game, and what to look at if one fails.

## State

Code written, compiled and committed. In game (2026-11-04, `PRERELEASE_TESTING.md` point 4): the make tile and
the Livery editor of a new make work; the titles Fan / Enthusiast / Aficionado cannot be checked without long play
and stay unverified. The rest of this file is the state of 2026-10-31.

| File | Change |
|---|---|
| `ForzaStudio/MakeTitles.cs` (new) | unlock rows, strings in every language, `Drive_<Base>` rule; add and remove the rule in an XML text |
| `ForzaStudio/MakeDecals.cs` (new) | decal carbin build (from an image or from source texels), `Livery_Decals` row and name, 45 common `Livery_DecalsSortOrder` rows + own |
| `ForzaStudio/CarMake.cs` | `Create` calls both; `WriteTitleRule`; `Fit` and `LoadImage` made internal; Livery.zip saved and put in the manifest |
| `ForzaStudio/CarPort.cs` | `PortTitles` and `PortDecals` after `PortStrings` (only for a make the port added); `WriteTitleRule`; the rules file in `CheckWritable` |
| `ForzaStudio/DlcPackage.cs` | `CopyPortedFiles` takes the offer and joins the trial's `<Base>.xml` files into `BadgesAndTitles\<offer>.xml` |
| `ForzaStudio/CarUninstall.cs` | removes the make's unlock rows, strings, rule, own decal (row, name, carbin) |
| `ForzaStudioEVO.csproj` | the two new files |

The build is only in `obj\x86\Debug\ForzaStudioEVO.exe`: the app was open, so `bin\Debug` was not
overwritten. **Close the app and build again into `bin\Debug` before testing the UI** (VS MSBuild,
see memory "Build into binDebug only").

Checked without the game (copies in `E:\DEV\fm4dev\Titles`, nothing written to a game folder):
- local create of a make: rows, 18 languages (DE, FR read), rule added to `BadgeAndTitleRules.xml`;
- adding then removing the rule gives the original file byte for byte;
- DLC trial create: the game's XML untouched, `Bowler.xml` in the trial, joined into `31.xml` of the
  pack, `Livery\decals\decal524.carbin` and the six logos in the pack;
- port of `BOW_NemesisEXR_12` (FH2) into a trial: titles and decals added, the decoded decal is FH2's;
- the generated decal has the template's header and trailer, 350075 bytes, texture decodes.

Not run at all: the uninstall of a make with titles and a decal (compiled only).

## What the official packs showed (OG_GAME, STFS files, `StfsReader`)

Ultima (offer 30), MG (35), Viper (36), Ascari and Hudson (37), Austin-Healey/MG/Viper (39). Each has
the make row, 3 unlock rows, 1 `Livery_Decals` row, 46 sort rows, `<offer>.xml` rules, the loose decal
carbin. Offer 31 (Alpinestars) has a `30.xml` too: the game reads every XML of the folder for packs.

## Tests in game (Xenia, DEV folder, close and restart Xenia as the memory says)

Use `FM4_Extracted_DEV` only, never `FM4_Extracted`. Check Xenia first.

1. **Local make** (`New make`, "Create in the game", a PNG logo with transparency):
   - Livery editor, decals of a car of that make: the make's logo first, then Turn 10, FM4 and the
     sponsors; does the logo show in the right colour, not mirrored, not black.
   - Needs a car under the make (port or change a car's Make).
2. **Titles, local**: raise the affinity of the make (drive cars of it) or edit
   `NewProfile_Career_Affinity`; "Bowler Fan" at level 1 must unlock and be selectable in the player
   card with the make's logo as badge. Check the Level 10 and 25 ones too.
3. **DLC pack with a make**: build a pack with a car under a new make, install it in Xenia, then the
   same two checks. This is the real test of `31.xml`-style rules for a make of the pack.
4. **Port from FH2 (Bowler)** into a pack: decal of the FH2 logo in the livery editor, titles.
5. **FH1 "Eagle"**: it is a different brand from FM4's Eagle (make id 87). Port it as a new make (own
   id and code, not `EAG`) and check titles and decals do not touch the FM4 Eagle's.
6. **Uninstall** of a port-added make: rows, strings, rule and decal gone; the game starts; the rules
   file equals the original; Livery.zip has no entry left; no other make lost a decal.

## If something fails

- **Title does not show or does not unlock**: first check the rule is read (a pack: the XML is in
  `<offer>_pri_65\Media\GameTunableSettings\BadgesAndTitles\<offer>.xml`; local: block at the end of
  `BadgeAndTitleRules.xml`). Compare the make id in `<Manufacturer id>` with `List_CarMake.ID`.
  Then the unlock rows (`UnlockName` = `<Base>_Affinity_n`, category 4-8, `HasTitle` 1) and the
  strings (`IDS_UnlockTitleString_<id>` in `List_BadgeAndTitleUnlocks.str`).
- **Title text in English only**: a language whose Ferrari text lacks "Ferrari" falls back to English
  (by design).
- **Decal black or garbled**: compare with an official one decoded the same way (base level at 0x154,
  `XenosTiling.Tiled2D(x, y, 256, 2)`); the harnesses `dec2`, `mips`, `og`, `og2` are in
  `E:\DEV\fm4dev\Titles`. A black decal at far zoom only = the tail mips (filled with the average
  texel), the layout there is not decoded.
- **Decal list empty for the make**: `Livery_DecalsSortOrder` rows for the make id, and the
  `Livery_Decals` row it points to (`Filename`, `DisplayName`). A pack: the carbin must be under
  `Media\Livery\decals\` with the number in `Filename`.
- **Make id clash**: a port keeps the source make id (Bowler is 100 in FH2); the integrity check warns
  that FM4's ids go to 105. Not new, see ROADMAP topic 18.

## Open points

- The tail mips of the decal (below 32x32): the packed layout is not decoded; a real layout would make
  the decal exact at every zoom.
- The common decals come from the make with the most rows (make 1). The official packs copy the same
  set; if a game version differs, take Eagle's or Ultima's rows instead.
- No UI for titles and decals: they ride on `New make` and the port. A way to add them to a make that
  exists already (FM4's own make, or one created before this) is not there.
- Pack names: the rules file is named by the offer; if the game wants another name for a second pack,
  the official packs use the offer number (30, 32, 34, 35, 36, 37, 38).
- Commit: nothing is committed (also the other uncommitted files in the repo are not mine).

## Harnesses (E:\DEV\fm4dev\Titles, they load `ForzaStudioEVO.exe` copied from `obj`)

- `maketest.exe <game root> <trial root | -> <logo png>`: `CarMake.Create` for Bowler.
- `pack.exe <game> <trial> <out>`: runs `DlcPackage.CopyPortedFiles` on a trial (offer 31).
- `portt.exe <FH2> <FM4> <car> <donor> <trial>`: port into a trial.
- `rt.exe <rules.xml> <block.xml>`: add and remove a rule, round trip.
- `og.exe <OG_GAME folder>`: lists decal, badge and merge rows of the official packs; `og2.exe`: one pack.
- `dec2.exe <carbin> <png> 256 256 [hex offset]`, `mips.exe <carbin>`, `cmpb.exe a b`: decal decoding.

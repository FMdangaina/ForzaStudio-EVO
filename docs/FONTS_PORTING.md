# Digital dash fonts and porting

Status: **done.** Rule written 2026-10-31, checked offline on the dash corpus, committed, and checked in game by the user on 2026-11-04 (FH LaFerrari port: dash texts OK; `PRERELEASE_TESTING.md` point 5). The port log shows `dash <file>: N font names changed to FM4's`.

## How a dash font is chosen

- Every text element of a dash names its font as a string in the `.bgf` (property `font`, one string per property). The `.fbf` text record keeps the same name as its default.
- The game looks the name up in `Media\UI\Fonts.zip\fontmap.xml` (an Xbox LZX zip: plain `unzip` cannot read it, `fm4dev\FH2toFM4\tools\cat.exe <zip> <entry> <out>` can).
- `fontmap.xml` maps a name to a target font. The dash targets are `DG1`..`DG5`, the files `dg1_vector_aa.dt`..`dg5_vector_aa.dt` in the same zip. The UI targets are `A`, `B`, `D`, `E`, `SYM`.
- FM3 and FM4 map: `DG1_arial`, `DG2_lcd`, `DG3_moire`, `DG4_pf_ronda`, `DG5_quartz` (and `arial`, `lcd `, `moire`, `pf_ronda`, `quartz`).
- The names inside the dashes carry a suffix over those keys (`dg1_arial bold`, `dg4_pf_ronda_seven`, `dg5_quartzms`, `lcd bold`), so the game most likely matches case-insensitively by prefix. This is deduced from the data, not read from the executable.

## The problem

FH1 and FH2 `fontmap.xml` have `Horizon_A..E`, `Horizon_DG1..DG6` and `Horizon_SYM`. FM4 and FM3 do not. A dash ported from FH with `horizon_dg1` asks FM4 for a name it cannot resolve, and FM4 falls back on a generic font (the Comic Sans look seen on a car ported from FH1). FM3 dashes with `eurostile bold` or `franklin gothic demi cond` have no entry in FM4 either.

Font names found in the dash corpus (dashes using each name):

| Game | Names FM4 does not know |
|---|---|
| FH1 (94 dashes) | `horizon_dg1` 30, `horizon_dg2` 4, `horizon_dg4` 8, `horizon_dg5` 1, `horizon_a` 1, `horizon_c` 1 |
| FH2 (103 dashes) | `horizon_dg1` 27, `horizon_dg2` 4, `horizon_dg3` 2, `horizon_dg4` 6, `horizon_dg5` 2, `horizon_c` 1 |
| FM3 (154 dashes) | `eurostile bold` 2, `franklin gothic demi cond` 2, `b_endurancepro bold` 1, `lcd light` 1 |

The 234 FM4 dashes use 15 different names, all resolved by FM4.

## The rule (`ForzaStudio\DashFonts.cs`)

A name FM4's own dashes use stays. Any other name becomes the FM4 name of the same family. Only names that already exist in FM4 dashes are written.

| Source | FM4 name |
|---|---|
| `horizon_dg1` / `dg6` | `dg1_arial bold` |
| `horizon_dg2` | `dg2_lcd bold` |
| `horizon_dg3` | `dg3_moire bold` |
| `horizon_dg4` | `dg4_pf_ronda_seven` |
| `horizon_dg5` | `dg5_quartzms` |
| `horizon_a`, `horizon_c` | `a_swiss_721_bold_condensed bold` (FH1 maps C to A; FM4 has no C) |
| `horizon_b` | `b_swiss_721_medium medium` |
| `horizon_d` | `d_swiss_721_roman_condensed roman` |
| `horizon_e` | `e_endurancepro-cond` |
| `b_endurancepro bold` | `b_swiss_721_medium medium` |
| `lcd light` | `lcd bold` |
| `franklin gothic demi cond` | `a_swiss_721_bold_condensed bold` (a choice, no obvious match) |
| `eurostile bold` | `dg1_arial bold` (a choice, no obvious match) |

A name with no rule is left as it is and the port log prints `WARNING dash ...: font X has no FM4 equivalent rule`.

## Where it runs (`CarPort.cs`)

- `.fbf`: `ConvertAnarkLayout` (FH) and `ConvertMotorsport3Dash` (FM3) call `DashFonts.Fix(fbf, null)` before writing.
- `.bgf`: `FixDashFonts` in the FH copy loop and in `AddMotorsport3Dash` (FM3). It replaces the string in place; the file stays byte-identical when nothing changes.
- Log line: `dash <file>: N font names changed to FM4's`.

## Offline check (corpus `E:\DEV\fm4dev\anark\corpus`)

| Game | Dashes | `.bgf` changed | Names with no rule |
|---|---|---|---|
| FM4 | 234 | 0 | 0 |
| FH2 | 103 | 36 | 0 |
| FH1 | 94 | 39 | 0 |
| FM3 | 154 | 3 | 0 |

Every changed file reads back; after the rule every font in every dash is one FM4 dashes already use.

## Open

1. Done 2026-10-31: app built (`MSBuild ForzaStudioEVO.csproj /p:Configuration=Debug /p:Platform=x86`, into `bin\Debug`).
2. In-game test: port an FH car with `horizon_dg1` and look at the dash. If the font stays generic, the prefix hypothesis is wrong and the rule needs another key form.
3. The viewer (`AnarkDashCanvas.MakeFont`) still approximates fonts with Arial/Consolas; the real `dgN_vector_aa.dt` (CAFF) glyphs are not drawn.
4. The `.dt` files differ in size between FH and FM4 (dg1: 67300 vs 78188 bytes, same CAFF header); the difference was not analysed (extra glyphs or encoding).
5. FM2 dashes: not handled.

See also `docs/ANARK_DASH.md`, `docs/DASH_MAPPING.md`.

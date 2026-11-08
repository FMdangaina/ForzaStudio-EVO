# Ported cars as FM4 DLC packages

ForzaStudio EVO can put cars ported from Forza Horizon or Forza Horizon 2 into a **DLC package** for
Forza Motorsport 4. The game's own files are left alone. The package is a folder that Xenia
loads like real downloadable content: install it, remove it, or rebuild it whenever you like.

The format was worked out from an official FM4 car pack, with help from two people on Discord
(see [Credits](#credits)).

Status:
- **Xenia:** checked in game. One-car and five-car packs load; the Gallardo LP570-4 Superleggera
  from FH1 shows in the garage, and its model, dash, engine sound and upgrades all work.
- **Real Xbox 360 (RGH):** a package built with the window's **Build for Xbox 360…** works on
  the user's console.

## Using it

Tools > **Port Car From Another Forza…**, right panel **DLC pack (Xenia / Xbox 360)**.

1. Set the two game folders at the top (FH or FH2, and FM4), then press **Load Cars**. The source
   can also be FH car DLC packages: **Folder…** for extracted ones, **Package…** for the package files.
2. For each car you want:
   - select it in the list;
   - choose its FM4 donor (★ = closest match);
   - press **Add →** (between the two lists), or double-click the car.

   Only cars FM4 lacks can go into a pack.
3. Give the pack a **Name**. It is the content name the game and Xenia show.
4. Press **Trial Pack**. Every car is converted in turn into one temporary folder. Nothing is
   written to the game or to Xenia.
   - The **Review** column tells, per car, how many things are not regular for FM4.
   - Select a car in the pack to see its review below and choose the fixes.
   - If fixes change after the trial, the status says so. Run **Trial Pack** again to apply them.
5. Set **Xenia's folder** (the one with `xenia_canary.exe`) once.
6. Close Xenia, then press **Build DLC Package**. The package goes straight into Xenia's content
   folder. A pack built before is replaced after a confirmation.
7. Start FM4 in Xenia. The cars are with their makes, like any other car.

For a real Xbox 360 (modded: RGH or JTAG), press **Build for Xbox 360…** after the trial and choose
a folder. The package file is written to `<folder>\Content\0000000000000000\4D530910\00000002\`.
Copy that `Content` folder to the console's `Hdd1:\` by FTP (Aurora / FSD) or Xbox 360
Neighborhood. The pack shows in the game like the Xenia one.

Other buttons:
- **Remove** takes a car out of the pack.
- **New Pack** starts an empty pack, with a new offer number.
- **Remove Pack** deletes the built package from Xenia.

The pack, its name, its offer number and Xenia's folder are kept in `settings.txt`.
**Install In FM4…** still exists: it writes one car into the game's own files, as before.

### Things to know

- **Put all your cars in one pack.** A package's string tables replace the game's whole tables
  (see below), so of two Forza Studio packs installed together only one shows its car names.
- Xenia must be closed while a package is built or removed.
- The FM4 folder you port to is also the base of the package: its database and string tables are
  what the package adds to. Use an FM4 without earlier installs of the same cars.
- **A car installed earlier with Install In FM4 hides the pack's version.** The game mounts its
  own zips (`zipmanifest.xml`, priority 80) above a package's loose files (priority 65). If
  `Media\cars\<car>.zip` or `Media\wheels\<car>.zip` is still in the FM4 folder, the game shows
  that old copy, and changes such as material fixes seem not applied. Build DLC Package warns about
  this. Remove those zips and their manifest lines first.
- A pack trial keeps a copy of `UI\Textures.zip` (about 440 MB) in
  `%TEMP%\ForzaStudio\CarPort\Pack_*`. Each new pack trial deletes the earlier ones.
- `license_mask` in Xenia's config does not matter: the packs work with the default 0.

## What a package holds

Built from the official FM4 car pack "offer 31" (10 cars, read as installed by Xenia), from
XTD's FH1 package, and from Aiden-Piercee's notes. All of this was verified on those files.

```
content\0000000000000000\4D530910\00000002\<offer id>\       the package (Xenia folder form)
  Media\<offer>.puboffer                     3 bytes: 20 0D 0A
  Media\DLCZips\zipmount.xml                 the zips to mount and where
  Media\DLCZips\stringtables.xml             the tables that get strings
  Media\DLCZips\StringTables_pri_<P>.zip     <LANG>/<Table>.str, 18 languages
  Media\DLCZips\<offer>_pri_65\Media\...     loose files laid out like the game's Media folder:
      db\patch\<offer>000_merge.slt          the new database rows
      cars\<car>\...                         the car zip's files
      wheels\<wheel>\...                     the wheel zip's files
      audio\Cars\CarModelTuning\, Engines\enginetuning\, Engines\harmonictuning\
      audio\Cars\Engines\soundbanks\lod1\*.fsb
      UI\Textures\Thumbnails\Thumbnail_<id>.xds
content\0000000000000000\4D530910\Headers\00000002\<offer id>.header
```

- **Title and content type.** FM4's title id is `4D530910`. Marketplace content is type
  `00000002`.
- **Loose folder.** `<offer>_pri_65` mounts at the game's root, so every file sits where the game
  would find it in its own zip. A zip `X.zip` of the game mounts at the folder `X`; for example,
  `Media\UI\Textures.zip` maps to `Media\UI\Textures\`. The official pack is loose like this.
  XTD's packs use `cars_pri_N.zip` and `wheels_pri_N.zip` mounted at `game:\Media\cars\` and
  `game:\Media\wheels\` instead. Both forms load.
- **zipmount.xml and stringtables.xml.** Same text as the official files, lines ending with
  `" \r\n"`:
  ```
  <?xml version="1.0" ?>
  <zipmount>
     <zip Name="StringTables_pri_50091.zip" Mount="game:\Media\StringTables\"  />
  </zipmount>
  ```
- **Merge database** (`<offer>000_merge.slt`):
  - What it is: SQLite holding only the rows the package adds. The game merges them into its
    database at start.
  - Tables: the game's own columns and types, without primary keys, page size 1024 (the
    official merges' form).
  - Rows: everything the port wrote. They are found by comparing the database before and after
    the trial.
  - Offer rows, added on top:

    | Table | Content |
    |---|---|
    | `ContentOffers` | One row for the pack. `ID` = `4D530910` + `FF` + the offer in 4 hex digits + `00` (offer 91 → `4D530910FF005B00`); `OfferId` = the same value as an integer; `PubOfferingID` = offer; `IsDLC` 1; `IsInstalled` and `IsPurchased` 1; `LicenseLevel` 2. |
    | `ContentOffersMapping` | One row per car: `Id` = offer × 1000 + n, `ContentId` = the car's `Data_Car.Id`, `ContentType` 1. |

  - The base game's own `ContentOffers` holds only the four showroom packs (offers 1–4). Cars
    without a mapping are simply available.
- **String tables.** A package's `<LANG>/<Table>.str` is the whole table: it replaces the
  game's. The official EN `Data_Car.str` holds the game's 1006 keys, with the same hashes, plus
  the pack's 186. Forza Studio writes:
  - each table the port changed (`Data_Car`) as the clean game's table plus the port's strings;
  - `ContentOffers` with the pack's name under two free keys.

  The priority is `50000 + offer`. It must be above any installed pack's. The entries are LZX
  compressed like the game's.
- **Database string references.** A reference is `_&<id>`, where the high 16 bits name the table
  and the low 16 bits are the key in its `.str` file.
- **Xenia header.** This is a file next to the package (`Headers\00000002\<name>.header`).
  Without it Xenia does not list the folder, and the game never sees the pack. That was the only
  thing missing in the first test. The format is what Xenia writes for its own content:

  | Offset | Content |
  |---|---|
  | 0x000 | `CON ` |
  | 0x340 | header size, 0x971A |
  | 0x344 | content type |
  | 0x348 | metadata version, 2 |
  | 0x360 | title id |
  | 0x411 | display name, UTF-16BE |
  | 0x1691 | title name |
  | 0x971A | the folder name, ASCII |

  The file is 0xA000 bytes long.

## The Xbox 360 package

A real console reads content only from an **STFS package file**, never from a folder.
Forza Studio writes it with `StfsPackage.cs`, a C# port of Aiden-Piercee's `stfs_write.py`. Its output is
byte-identical to that writer's on a 1-car and a 5-car pack. The header fields copy the FH1 car
package XTD built with the Xbox 360 authoring tool, which a console loads:

| Field | Value |
|---|---|
| magic | `LIVE` |
| licence 0 | `FFFFFFFFFFFFFFFF` (everyone, any console) |
| header size | `0xAD0E` (data from 0xB000) |
| content type | 2, metadata version 2 |
| title id | `4D530910`, title name "Forza Motorsport 4" |
| volume | block separation 1 (one hash table per level) |
| transfer flags | `C0` |
| thumbnails | the program's icon, 64 × 64 PNG |

- **Hash tables and content id.** Each 170 data blocks follow a level-0 hash table holding the
  SHA-1 of each block and the next block of the chain. Each 170 level-0 tables are hashed in a
  level-1 table, and above 170 × 170 blocks there is one level-2 table. The top hash goes in the
  volume descriptor. The content id is the SHA-1 of the header from 0x344, and it is also the file
  name, followed by `4D`.
- **Signature.** The RSA signature is left empty. Modded consoles do not check it; a retail
  console refuses the package.
- **Names hold at most 40 characters.** Brake carbins exceed that, for example
  `lam_gallardolp570_11_caliperlf_lod0.carbin`. So in the console package the loose
  `<offer>_pri_65` folder becomes **`<offer>_pri_65.zip`**: stored entries in the game's zip
  layout, with the `0x1123` extra field holding the data offset. The official packs do the same,
  and the game mounts the zip like the folder. The `*_build_report.html` files stay out.
- **Reading and editing a package.** `StfsReader.cs` reads `CON`/`LIVE`/`PIRS` packages (one or two
  hash tables per level, chained files). File > Open DLC Package extracts one to a work folder;
  each save packs it again with `StfsPackage.Rewrite`, which keeps the original header and only
  rewrites the volume descriptor, the content size and the content id. Our own packages come back
  byte-identical; official FH1 packages keep every header byte except the content id and the top
  hash (the file table order and dates are ours).

## Code

- `ForzaStudio\DlcPackage.cs` builds a package. Main entry points:
  - `BuildMerge(clean, ported, offer)`: the merge database.
  - `BuildFolder(clean, target, trial, content, offer, name)`: the whole package from a trial
    folder.
  - `XeniaHeader`: the header file.
  - Xenia helpers: content folder (its `content_root` honoured), free offer number, remove.
- `ForzaStudio\CarPort.cs` gets chaining for packs. When the trial folder already holds a file
  (an earlier car of the same pack), the port reads it instead of the game's: database, string
  tables, audio zips, sound banks, textures and manifest. Cars ported into the trial folder do
  not count as reference cars for the PI mapping, and trial runs make no backups.
- `ForzaStudio\CarPortForm.cs` holds the DLC pack panel.

## Test tools

In `fm4dev\DEV_DLC\tools`, outside the repository. They are one-file C# harnesses built against
`bin\Debug\ForzaStudioEVO.exe`.

| Tool | What it does |
|---|---|
| `port.exe <src> <fm4> <car> <donor> <trial>` | one-car trial |
| `dlcmerge.exe <clean db> <ported db> <offer> <out>` | merge database only |
| `mergecheck.py <clean db> <merge> <ported db>` | applies the merge to a copy of the clean database and checks it equals the ported one (0 differences on every test) |
| `dlcbuild.exe <clean> <fm4> <trial> <content> <offer> <name>` | package from a trial |
| `packtrial.exe <src> <fm4> <trial> <content> <offer> <name> <car:donor>…` | chained pack trial and build, without the window |
| `packform.exe <png prefix> <car:donor>…` | drives the real window (Add, Trial Pack, Build) and takes screenshots |

## Not done yet

- Several Forza Studio packs at once: build each pack's string tables on top of the other
  installed packs' tables.
- Sending the package to the console from the window (FTP).

## Credits

- **Aiden-Piercee (https://github.com/Aiden-Piercee)** wrote the `fm4dlc` toolchain and notes:
  - the anatomy of the official car and track packs;
  - the merge database;
  - string table writing;
  - the STFS reader and writer.
- **XTD (@xtd_ac)** showed:
  - that Xenia loads a DLC as a plain folder;
  - the `DLCZips` layout;
  - a real FH1 car package built with the Xbox 360 authoring tool (XLAST).

  He also offered to test packages on a console.

Thank you both.

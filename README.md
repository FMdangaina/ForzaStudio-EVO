# ForzaStudio EVO

(Repository: ForzaStudio-EVO, exe: ForzaStudioEVO.exe.)

**Version 0.0.4 (beta).** Modding tool for the Xbox 360 era Forza games: Forza
Motorsport 4 first, with Forza Horizon 1 and 2 and Forza Motorsport 2 and 3 as car sources.

Edits the game's zip archives directly: `.carbin` car models (OBJ, FBX and glTF export/import,
part transplant and duplication), `.xds` textures (import, export, creation from
images), livery masks, the gamedb, the digital dashes, the cockpit camera and the driver
position, with the game's music and radio in the status bar.

Ports cars from Forza Horizon, Forza Horizon 2, Forza Motorsport 3 and Forza Motorsport 2 to FM4,
either into the game's files or as a **DLC package** that leaves the game untouched, for Xenia
or a modded Xbox 360: see [docs/DLC_PACKAGES.md](docs/DLC_PACKAGES.md). Other tools: Duplicate
Car with an engine and gearbox of its own, new car makes, change a car's engine sound, car
performance from the garage's numbers, DLC package validation, and adding your own songs.

Some features are experimental (digital dash structure edits, OBJ onto wheels, a new country):
the list is in [Known limitations](docs/MANUAL.md#13-known-limitations).

User manual: [docs/MANUAL.md](docs/MANUAL.md). Quick start for the beta zip:
[docs/INSTRUCTIONS.txt](docs/INSTRUCTIONS.txt).

Derived from Forza Studio 4.6 by Mike Davis, with an earlier edit by JYSB59.
Maintained by FMdangaina. The DLC package format was worked out with help from Jew
(@mr.abortion) and XTD (@xtd_ac) on Discord. The engine power calculation (the torque a
given power needs at its rpm, as a scalar of the peak torque) follows BlackBirdOsaka's notes
on Discord. [Aiden-Piercee](https://github.com/Aiden-Piercee)'s `fm4dlc` toolchain was a
reference for the DLC and carbin files; soulbrix's FM4 tools were
studied for ideas (no code taken). Full credits in [docs/MANUAL.md](docs/MANUAL.md#16-credits-and-license).

## License

GNU General Public License v3.0, see [LICENSE](LICENSE). The Forza Studio 4.6 code it is
based on is MIT-licensed (Copyright (C) 2012 Mike Davis); its notice is kept in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Build

1. Install **Visual Studio 2022** with the workload *.NET desktop development* (it includes the .NET Framework 4.8 targeting pack).
2. Install **XNA Game Studio 3.1** (or the XNA Framework 3.1 Redistributable). The build stops with a clear message if it is missing.
3. Put your own `xds.dll` (Forza Studio 4.6 release) and `xcompress.dll` (Xbox 360 SDK) in `!ForzaStudio release\`: they are not in the repository. Optional: `xma2encode\xma2encode.exe` next to the built exe for Add Song, and the offroad tire zips in `Service\OffroadTires\` (see [docs/INSTRUCTIONS.txt](docs/INSTRUCTIONS.txt)).
4. Open `ForzaStudioEVO.sln` in Visual Studio and press **Ctrl+Shift+B** (Build > Build Solution). The configuration is already *Debug / x86*.
5. The program is `bin\Debug\ForzaStudioEVO.exe`. The build copies everything from `!ForzaStudio release\` next to it and writes `ForzaStudioEVO.exe.config`.

Command line, the same thing: `MSBuild ForzaStudioEVO.sln -p:Configuration=Debug -p:Platform=x86`

The project is **x86 only**. If the build fails, send the first line that says `error`. Close a running copy of the program before building (it locks the exe).

## Repository policy

No game data and no non-redistributable binaries are ever committed; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

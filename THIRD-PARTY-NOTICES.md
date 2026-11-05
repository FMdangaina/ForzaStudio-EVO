# Third-party notices

Forza Studio is licensed under the GNU General Public License v3.0 (see [LICENSE](LICENSE)).
It includes MIT-licensed work, whose notices are kept here as that license requires.

## Forza Studio 4.6

Forza Studio is derived from the source code of Forza Studio 4.6:

    Forza Studio: Forza Motorsport resource extraction tool.
    Copyright (C) 2012 Mike Davis (http://codeescape.com/)

That code is distributed under the MIT License reproduced below; the original notice is also kept
at the top of each of its source files.

## Binaries

This repository includes the following third-party binaries in `!ForzaStudio release/`.
Both are distributed under the MIT License, reproduced below.

| File | Project | Version | Copyright |
|------|---------|---------|-----------|
| `Be.Windows.Forms.HexBox.dll` | Be.HexEditor (Bernhard Elbl) | 1.4.7 | Copyright (c) 2011 Bernhard Elbl |
| `texconv.exe` | [DirectXTex](https://github.com/microsoft/DirectXTex) | 2026.4.1.1 | Copyright (c) Microsoft Corporation |

## vgmstream

The music player runs `vgmstream-cli.exe` and its DLLs from the `vgmstream\` folder next to the
exe. They are not in this repository but are shipped in the release zip, with vgmstream's own
`COPYING` file. vgmstream is under an ISC-style license:

    Copyright (c) 2008-2025 Adam Gashlin, Fastelbja, Ronny Elfert, bnnm,
                            Christopher Snowhill, NicknineTheEagle, bxaimc,
                            Thealexbarney, CyberBotX, EdnessP, et al

    Permission to use, copy, modify, and distribute this software for any
    purpose with or without fee is hereby granted, provided that the above
    copyright notice and this permission notice appear in all copies.

    THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
    WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
    MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
    ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
    WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
    ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
    OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

The full notice, with the portions copyrighted by others, is in vgmstream's `COPYING`.
Project: https://github.com/vgmstream/vgmstream

## Credits (no code included)

- [Aiden-Piercee](https://github.com/Aiden-Piercee), `fm4dlc` toolchain: reference for the DLC package and
  carbin files.
- soulbrix's FM4 car and part tools (no license published): studied for ideas; no code
  from them is used.
- Jew (@mr.abortion) and XTD (@xtd_ac): the Xbox 360 DLC package format, Xenia folder DLC, console tests.
- BlackBirdOsaka: engine power calculation notes.

## MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Not included

These files are needed at build time or runtime but are not committed. Place them in
`!ForzaStudio release/` yourself:

| File | Why it is excluded |
|------|--------------------|
| `xcompress.dll` | Microsoft Xbox 360 SDK, proprietary. Not in the release zip either. |
| `Service\OffroadTires\Tire*Offroad.zip` | Offroad tire models for the port's "Offroad" choice. Not in the release zip: the user copies their own. |
| `xma2encode.exe` | Microsoft Xbox 360 SDK, proprietary. Only needed by Add song (FM4); the user copies their own to `xma2encode\` next to the exe. |
| `xds.dll` | Comes from the Forza Studio 4.6 release. Not in the release zip (from 0.0.1): the user copies their own next to the exe. |

Game data (Forza `.zip`, `.carbin`, `.xds`, `.xpr`, gamedb, etc.) is copyrighted
and is never committed either.

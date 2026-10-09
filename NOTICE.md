# NOTICE

## 1. What this project is

CnCNew is an independent reimplementation, in C#/.NET, of the parts of the SAGE engine runtime that
*Command & Conquer™ Generals – Zero Hour* needs (simulation, data model, presentation, client on Stride/Direct3D 11), together with a patcher
that builds a new local game directory with open files and this engine from an existing installation of the game –
a copy of the game the user already owns. The user's installation is only read in the process, never modified.

Copyright © 2026 Christopher Pfaffinger and the contributors of this repository.

## 2. No game data

This repository contains **no game data**: no original files, no archives, no texts,
graphics, models, maps or sounds exported from them. Whoever wants to use the software needs their own, legally acquired copy
of the game (exactly one source version is supported: the fresh English Steam installation, see
`docs/INSTALLATION.md`). The rights to the game data lie with Electronic Arts Inc.; this licence does not extend
to them.

The only things about the game that are checked in are metadata needed for checking: paths, sizes, counts and
SHA-256 checksums of files (`baselines/`) as well as identifiers that the data itself prescribes (names of objects,
modules, fields, maps). Test fixtures are synthetic; they use individual `key = value` lines in the
syntax and with identifiers of the game data, but no excerpts from original files.

## 3. Licence of this repository

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the
Free Software Foundation, either **version 3 of the License, or
(at your option) any later version** (SPDX: `GPL-3.0-or-later`). It is distributed without any warranty,
without even the implied warranty of merchantability or fitness for a particular purpose. The
licence text is in `LICENSE.md`.

Reason for the choice: the code was written with reference to the source code published by Electronic Arts under GPL v3
(algorithms understood and rewritten, with a source reference to file and function in documentation and
comments); GPL v3 is therefore the licence that fits in any case – regardless of whether the result is legally
classified as a derived work. "Or later" instead of "version 3 only", because the file headers of the EA source code themselves
say "either version 3 of the License, or (at your option) any later version" (counted locally: 5,497 of 5,510
`.cpp`/`.h` files) and the community continuation GeneralsGameCode marks its `LICENSE.md` with
`SPDX-License-Identifier: GPL-3.0-or-later`; this way the exchange of code remains possible in both directions.

The licence applies to **all files of this repository** unless a file says otherwise. Individual source files
deliberately carry no licence header.

`LICENSE.md` is the unmodified text of the GNU GPL version 3 of 29 June 2007 (preamble, sections 0 to 17, "END OF
TERMS AND CONDITIONS", appendix "How to Apply These Terms to Your New Programs") in the Markdown version, taken over byte for byte from
the `LICENSE.md` of the EA release (without its introductory sentence and without the additional terms, which
are in section 4 below); the file therefore has Windows line endings.

## 4. Relationship to the source code release by Electronic Arts

Electronic Arts Inc. released the source code of *Command & Conquer Generals* and *Zero Hour* under GPL v3 with
additional terms under section 7 (Copyright 2025 Electronic Arts Inc.; repository
`electronicarts/CnC_Generals_Zero_Hour`). This project is **not the original program** and not a version of it
that originates from Electronic Arts: it is an implementation newly written in another language that was created with reference
to that source code and marks itself as a modified or derived work in the sense of the additional terms.
Where an algorithm follows the original, documentation and comments name the source (file, function).
In addition, the community projects GeneralsGameCode and GeneralsGamePatch (TheSuperHackers) were read; no code
has been taken over from them.

The additional terms of the EA release require that they are passed on with every conveyance. In the original
they read (from `LICENSE.md` of the EA release, unmodified):

> ## ADDITIONAL TERMS per GNU GPL Section 7
>
> No trademark or publicity rights are granted. This license does NOT give you
> any right, title or interest in "Command & Conquer" or any other Electronic Arts
> trademark. You may not distribute any modification of this program using any
> Electronic Arts trademark or claim any affiliation or association with
> Electronic Arts Inc. or its affiliates or their employees.
>
> Any propagation or conveyance of this program must include this copyright
> notice and these terms.
>
> If you convey this program (or any modifications of it) and assume
> contractual liability for the program to recipients of it, you agree to
> indemnify Electronic Arts for any liability that those contractual
> assumptions impose on Electronic Arts.
>
> You may not misrepresent the origins of this program; modified versions of
> the program must be marked as such and not identified as the original program.
>
> This disclaimer supplements the one included in the General Public License.
> TO THE FULLEST EXTENT PERMISSIBLE UNDER APPLICABLE LAW, THIS PROGRAM IS
> PROVIDED TO YOU "AS IS," WITH ALL FAULTS, WITHOUT WARRANTY OF ANY KIND, AND
> YOUR USE IS AT YOUR SOLE RISK. THE ENTIRE RISK OF SATISFACTORY QUALITY AND
> PERFORMANCE RESIDES WITH YOU. ELECTRONIC ARTS DISCLAIMS ANY AND ALL EXPRESS,
> IMPLIED OR STATUTORY WARRANTIES, INCLUDING IMPLIED WARRANTIES OF
> MERCHANTABILITY, SATISFACTORY QUALITY, FITNESS FOR A PARTICULAR PURPOSE,
> NONINFRINGEMENT OF THIRD PARTY RIGHTS, AND WARRANTIES (IF ANY) ARISING FROM A
> COURSE OF DEALING, USAGE, OR TRADE PRACTICE. ELECTRONIC ARTS DOES NOT WARRANT
> AGAINST INTERFERENCE WITH YOUR ENJOYMENT OF THE PROGRAM; THAT THE PROGRAM WILL
> MEET YOUR REQUIREMENTS; THAT OPERATION OF THE PROGRAM WILL BE UNINTERRUPTED OR
> ERROR-FREE, OR THAT THE PROGRAM WILL BE COMPATIBLE WITH THIRD PARTY SOFTWARE
> OR THAT ANY ERRORS IN THE PROGRAM WILL BE CORRECTED. NO ORAL OR WRITTEN ADVICE
> PROVIDED BY ELECTRONIC ARTS OR ANY AUTHORIZED REPRESENTATIVE SHALL CREATE A
> WARRANTY. SOME JURISDICTIONS DO NOT ALLOW THE EXCLUSION OF OR LIMITATIONS ON
> IMPLIED WARRANTIES OR THE LIMITATIONS ON THE APPLICABLE STATUTORY RIGHTS OF A
> CONSUMER, SO SOME OR ALL OF THE ABOVE EXCLUSIONS AND LIMITATIONS MAY NOT APPLY
> TO YOU.

In short, without replacing the original wording: no trademark or publicity rights
are granted; modifications may not be distributed under a trademark of Electronic Arts and may not claim any affiliation with
Electronic Arts; the copyright notice and these terms must be included with every conveyance; whoever assumes contractual
liability for the program indemnifies Electronic Arts against liability arising from it; the origin may not be
misrepresented, modified versions must be marked as such and not identified as the original
program; in addition an extended disclaimer of warranty.

## 5. Trademarks

"Command & Conquer", "Generals" and "Zero Hour" are trademarks of Electronic Arts Inc. They are used here only descriptively,
to say what this software is intended for. This project is not affiliated with Electronic Arts
Inc. or its affiliates and is neither supported nor endorsed by them. No trademark or publicity rights are granted
or claimed. All other trademarks belong to their respective owners.

## 6. Third-party components

With one exception the repository contains no third-party code: `build/StrideAssetCompile.targets` contains an adapted
copy of the MSBuild target `StrideCompileAsset` from the package Stride.Core.Assets.CompilerApp 4.3.0.2507 (MIT; notice
and licence text of Stride at the end of this section). All of the following packages are obtained through NuGet when building. Licence information from
the `.nuspec` files of the locally restored packages (as of 9 October 2026).

| Package | Version | Where referenced | Purpose | Licence |
|---|---|---|---|---|
| Stride.Engine | 4.3.0.2507 | `src/CnCNew.Client.Stride`, `tools/CnCNew.UiRenderTool` | engine of the client (Direct3D 11, sound, input) | MIT |
| Stride.Core.Assets.CompilerApp | 4.3.0.2507 | the same (build only) | asset/shader compiler of Stride | MIT |
| NLayer | 3.0.0 | `src/CnCNew.Audio` | MP3 decoder | MIT |
| Microsoft.Build.Tasks.Core | 17.14.28 | `build/StrideToolingPins.props` (build only, `PrivateAssets=all`) | raised version of a tooling package of Stride | MIT |
| SSH.NET | 2026.0.0 | as above | as above | MIT |
| NuGet.Packaging | 7.0.3 | as above | as above | Apache-2.0 |
| NuGet.Protocol | 7.0.3 | as above | as above | Apache-2.0 |
| System.Security.Cryptography.Xml | 10.0.12 | as above | as above | MIT |
| .NET 10 (SDK, runtime, BCL) | SDK 10.0.401 (`global.json`) | all projects | platform | not verified locally (published by Microsoft as MIT) |

Stride brings along further, transitive packages and native libraries (among others FreeType for text output);
their licences are not verified locally one by one here – authoritative is the file `THIRD PARTY.md` of the
Stride repository. Whoever passes on a binary package of this software must enclose the licence texts of all
components shipped with it; such a package does not exist yet.

Licence of Stride (from `LICENSE.md` of the Stride repository, unmodified):

```text
The MIT License (MIT)

Copyright (c) 2021 .NET Foundation and Contributors (https://dotnetfoundation.org/)
Copyright (c) 2018-2021 Stride contributors (https://stride3d.net)
Copyright (c) 2011-2018 Silicon Studio Corp. (https://www.siliconstudio.co.jp)

All rights reserved.

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
```

Only read, not contained: the EA source code (GPL v3 with additional terms, see above), GeneralsGameCode
(`GPL-3.0-or-later` with the same additional terms according to its `LICENSE.md`), GeneralsGamePatch (licence not verified
locally), the source code of Stride (MIT).

## 7. No legal advice

This notice describes how the project understands its licence situation. It is not legal advice and does not replace a
review by a lawyer.

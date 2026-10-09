# NOTICE

**English summary.** CnCNew is an independent C#/.NET reimplementation of the parts of the SAGE engine runtime that
*Command & Conquer™ Generals – Zero Hour* needs, plus a patcher that builds a local game directory from a copy of the
game the user already owns. **This repository contains no game data**: no assets, no archives, nothing exported from
them. You need your own legally acquired copy of the game. The code was written with reference to the source code that
Electronic Arts Inc. released under the GNU General Public License version 3 with additional terms under section 7;
it is a modified/derived work in the sense of those terms, **it is not the original program**, and it is licensed under
**GPL-3.0-or-later** (`LICENSE.md`) with the upstream additional terms reproduced below. "Command & Conquer", "Generals"
and "Zero Hour" are trademarks of Electronic Arts Inc. This project is not affiliated with, endorsed by or supported by
Electronic Arts Inc. No trademark or publicity rights are granted or claimed. This notice is not legal advice.

---

## 1. Was dieses Projekt ist

CnCNew ist eine eigenständige Neuimplementierung der für *Command & Conquer™ Generals – Zero Hour* benötigten
SAGE-Laufzeit in C#/.NET (Simulation, Datenmodell, Präsentation, Client auf Stride/Direct3D 11) samt einem Patcher,
der aus einer vorhandenen Installation des Spiels ein neues lokales Spielverzeichnis mit offenen Dateien und dieser
Engine erzeugt. Die Installation des Anwenders wird dabei nur gelesen, nie verändert.

Copyright © 2026 Christopher Pfaffinger und die Mitwirkenden dieses Repositorys.

## 2. Keine Spieldaten

Dieses Repository enthält **keine Spieldaten**: keine Originaldateien, keine Archive, keine daraus exportierten Texte,
Grafiken, Modelle, Karten oder Töne. Wer die Software benutzen will, braucht eine eigene, rechtmäßig erworbene Kopie
des Spiels (unterstützt ist genau eine Ausgangsversion: die frische englische Steam-Installation, siehe
`docs/INSTALLATION.md`). Die Rechte an den Spieldaten liegen bei Electronic Arts Inc.; diese Lizenz erstreckt sich
nicht auf sie.

Eingecheckt sind über das Spiel ausschließlich Metadaten, die zur Prüfung nötig sind: Pfade, Größen, Anzahlen und
SHA-256-Prüfsummen von Dateien (`baselines/`) sowie Bezeichner, die die Daten selbst vorgeben (Namen von Objekten,
Modulen, Feldern, Karten). Testfixtures sind synthetisch; sie verwenden einzelne `Schlüssel = Wert`-Zeilen in der
Syntax und mit Bezeichnern der Spieldaten, aber keine Auszüge aus Originaldateien.

## 3. Lizenz dieses Repositorys

Dieses Programm ist freie Software: Sie können es unter den Bedingungen der GNU General Public License, wie von der
Free Software Foundation veröffentlicht, weitergeben und/oder verändern, entweder gemäß **Version 3 der Lizenz oder
(nach Ihrer Wahl) jeder späteren Version** (SPDX: `GPL-3.0-or-later`). Es wird ohne jede Gewährleistung verbreitet,
auch ohne die implizite Gewährleistung der Marktgängigkeit oder der Eignung für einen bestimmten Zweck. Der
Lizenztext steht in `LICENSE.md`.

Begründung der Wahl: Der Code ist mit Blick in den von Electronic Arts unter GPL v3 veröffentlichten Quelltext
entstanden (Algorithmen verstanden und neu geschrieben, mit Quellverweis auf Datei und Funktion in Dokumentation und
Kommentaren); die GPL v3 ist damit die Lizenz, die in jedem Fall passt – gleichgültig, ob man das Ergebnis rechtlich
als abgeleitetes Werk einordnet. „Oder später“ statt „nur Version 3“, weil die Dateiköpfe des EA-Quelltexts selbst
„either version 3 of the License, or (at your option) any later version“ sagen (lokal gezählt: 5.497 von 5.510
`.cpp`/`.h`-Dateien) und die Community-Fortführung GeneralsGameCode ihr `LICENSE.md` mit
`SPDX-License-Identifier: GPL-3.0-or-later` kennzeichnet; so bleibt der Austausch von Code in beide Richtungen möglich.

Die Lizenz gilt für **alle Dateien dieses Repositorys**, soweit eine Datei nichts anderes sagt. Einzelne Quelldateien
tragen bewusst keinen Lizenzkopf.

`LICENSE.md` ist der unveränderte Text der GNU GPL Version 3 vom 29. Juni 2007 (Präambel, Abschnitte 0 bis 17, „END OF
TERMS AND CONDITIONS“, Anhang „How to Apply These Terms to Your New Programs“) in der Markdown-Fassung, byteweise aus
dem `LICENSE.md` der EA-Veröffentlichung übernommen (ohne deren Einleitungssatz und ohne die Zusatzbedingungen, die
unten in Abschnitt 4 stehen); die Datei hat deshalb Windows-Zeilenenden.

## 4. Verhältnis zur Quelltextveröffentlichung von Electronic Arts

Electronic Arts Inc. hat den Quelltext von *Command & Conquer Generals* und *Zero Hour* unter der GPL v3 mit
Zusatzbedingungen nach Abschnitt 7 veröffentlicht (Copyright 2025 Electronic Arts Inc.; Repository
`electronicarts/CnC_Generals_Zero_Hour`). Dieses Projekt ist **nicht das Originalprogramm** und keine Fassung davon,
die von Electronic Arts stammt: Es ist eine in einer anderen Sprache neu geschriebene Implementierung, die mit Bezug
auf jenen Quelltext entstanden ist und sich im Sinn der Zusatzbedingungen als verändertes bzw. abgeleitetes Werk
kennzeichnet. Wo ein Algorithmus dem Original folgt, nennen Dokumentation und Kommentare die Quelle (Datei, Funktion).
Daneben wurden die Community-Projekte GeneralsGameCode und GeneralsGamePatch (TheSuperHackers) gelesen; aus ihnen ist
kein Code übernommen.

Die Zusatzbedingungen der EA-Veröffentlichung verlangen, dass sie bei jeder Weitergabe mitgegeben werden. Sie lauten
im Original (aus `LICENSE.md` der EA-Veröffentlichung, unverändert):

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

Kurz auf Deutsch, ohne den Originalwortlaut zu ersetzen: Es werden keine Marken- oder Persönlichkeitsrechte
eingeräumt; Veränderungen dürfen nicht unter einer Marke von Electronic Arts verbreitet werden und keine Verbindung zu
Electronic Arts behaupten; Urhebervermerk und diese Bedingungen sind bei jeder Weitergabe mitzugeben; wer vertraglich
für das Programm haftet, stellt Electronic Arts von daraus entstehender Haftung frei; die Herkunft darf nicht falsch
dargestellt werden, veränderte Fassungen sind als solche zu kennzeichnen und nicht als das Originalprogramm
auszugeben; dazu ein erweiterter Gewährleistungsausschluss.

## 5. Marken

„Command & Conquer“, „Generals“ und „Zero Hour“ sind Marken von Electronic Arts Inc. Sie werden hier nur beschreibend
verwendet, um zu sagen, wofür diese Software gedacht ist. Dieses Projekt steht in keiner Verbindung zu Electronic Arts
Inc. oder deren verbundenen Unternehmen und wird von ihnen weder unterstützt noch gebilligt. Alle anderen Marken
gehören ihren jeweiligen Inhabern.

## 6. Komponenten Dritter

Mit einer Ausnahme liegt im Repository kein Code Dritter: `build/StrideAssetCompile.targets` enthält eine angepasste
Kopie des MSBuild-Targets `StrideCompileAsset` aus dem Paket Stride.Core.Assets.CompilerApp 4.3.0.2507 (MIT; Vermerk
und Lizenztext von Stride am Ende dieses Abschnitts). Alle folgenden Pakete werden beim Bauen über NuGet bezogen. Lizenzangaben aus
den `.nuspec`-Dateien der lokal wiederhergestellten Pakete (Stand 9. Oktober 2026).

| Paket | Version | Wo referenziert | Zweck | Lizenz |
|---|---|---|---|---|
| Stride.Engine | 4.3.0.2507 | `src/CnCNew.Client.Stride`, `tools/CnCNew.UiRenderTool` | Engine des Clients (Direct3D 11, Ton, Eingabe) | MIT |
| Stride.Core.Assets.CompilerApp | 4.3.0.2507 | dieselben (nur Build) | Asset-/Shadercompiler von Stride | MIT |
| NLayer | 3.0.0 | `src/CnCNew.Audio` | MP3-Decoder | MIT |
| Microsoft.Build.Tasks.Core | 17.14.28 | `build/StrideToolingPins.props` (nur Build, `PrivateAssets=all`) | angehobene Version eines Werkzeugpakets von Stride | MIT |
| SSH.NET | 2026.0.0 | wie oben | wie oben | MIT |
| NuGet.Packaging | 7.0.3 | wie oben | wie oben | Apache-2.0 |
| NuGet.Protocol | 7.0.3 | wie oben | wie oben | Apache-2.0 |
| System.Security.Cryptography.Xml | 10.0.12 | wie oben | wie oben | MIT |
| .NET 10 (SDK, Laufzeit, BCL) | SDK 10.0.401 (`global.json`) | alle Projekte | Plattform | nicht lokal belegt (von Microsoft als MIT veröffentlicht) |

Stride bringt weitere, transitive Pakete und native Bibliotheken mit (unter anderem FreeType für die Textausgabe);
deren Lizenzen sind hier nicht einzeln lokal belegt – maßgeblich ist die Datei `THIRD PARTY.md` des
Stride-Repositorys. Wer ein Binärpaket dieser Software weitergibt, muss die Lizenztexte aller mitgelieferten
Komponenten beilegen; ein solches Paket gibt es noch nicht.

Lizenz von Stride (aus `LICENSE.md` des Stride-Repositorys, unverändert):

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

Nur gelesen, nicht enthalten: der EA-Quelltext (GPL v3 mit Zusatzbedingungen, siehe oben), GeneralsGameCode
(`GPL-3.0-or-later` mit denselben Zusatzbedingungen laut dessen `LICENSE.md`), GeneralsGamePatch (Lizenz nicht lokal
belegt), der Quelltext von Stride (MIT).

## 7. Keine Rechtsberatung

Dieser Hinweis beschreibt, wie das Projekt seine Lizenzlage versteht. Er ist keine Rechtsberatung und ersetzt keine
anwaltliche Prüfung.

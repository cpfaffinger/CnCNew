# Determinismus über Rechnergrenzen (Phase 18)

Stand: 6. Oktober 2026, Commit `b432fb7`; §2a (die Matrix mit dem Manifest der Phase 22, Q39.24) am Stand `d7fca91`. Dieses Dokument beschreibt, **wie** geprüft wird, dass zwei Rechner dieselbe Welt rechnen, und **was** davon gemessen ist. Die Einzelbefunde D1–D9 mit Test,
Korrektur und Vorher/Nachher-Werten stehen in `docs/DETERMINISM-FINDINGS.md`; die Messlatte selbst (Referenzmanifest, Eingangsmanifest) in `docs/SIMULATION-CORE.md` §37.

## 1. Was verglichen wird

Lockstep (`docs/MULTIPLAYER-PLAN.md` §2.1) verlangt, dass jeder Rechner aus denselben Befehlen bitgleich denselben Zustand errechnet. Geprüft wird in zwei Stufen, damit eine Abweichung zuzuordnen ist:

1. **Eingänge** – `SimTool input-manifest check <content> baselines/input-manifest.json`: Definitions-Fingerabdruck, Knochentabelle, Welt-Fingerabdruck, Inhalts-Hashes der gebauten Kataloge, je Karte Dateihash,
   Gelände-Fingerabdruck, Zellen und Startpositionen (150 Karten). Weicht das ab, rechnen die Rechner schon mit verschiedenen Daten.
2. **Rechnung** – `SimTool reference-manifest check <content> baselines/reference-manifest.json --set full`: 50 Läufe (Referenzmatch, Stresstest, 16 KI-Läufe, drei 8-Spieler-Läufe, Skirmish-Übersicht über 64 Karten,
   27 Missionsläufe), je Lauf Endtick, Sieger, Endhash, Kettenhash, „Lauf 2 identisch“, „nach Snapshot-Fortsetzung identisch“ und für Missionen „nach Speichern/Laden identisch zum ununterbrochenen Lauf“.
   Der Schnellsatz (`--set quick`, 9 Läufe) dauert rund 5 Minuten, der volle rund 25 bis 40.

Dazu `--verify-incremental` (prüft in jedem Tick den inkrementellen gegen den voll berechneten Hash; deckt fehlende `MarkDirty` auf, Befund D8).

## 2. Die Matrix (gemessen am 6. Oktober 2026)

Jede Zeile ist ein voller Lauf beider Manifeste gegen die eingecheckten Dateien; „identisch“ heißt Exitcode 0 bei beiden, also kein einziges abweichendes Feld in 50 Läufen.

| Zeile | Wie | Eingänge | Rechnung (Satz) | Ergebnis |
|---|---|---|---|---|
| Release, Lauf 1 | `dotnet CnCNew.SimTool.dll …` | identisch | 50/50 (full) | **identisch** |
| Release, Lauf 2 | derselbe Aufruf noch einmal | identisch | 50/50 (full) | **identisch** |
| Debug-Build | `dotnet build -c Debug tools/CnCNew.SimTool` | identisch | 9/9 (**quick**) | **identisch**; der volle Satz ist im Debug-Build nicht in zwei Stunden durchgelaufen und wurde nicht abgewartet |
| Tiered Compilation aus | `DOTNET_TieredCompilation=0` | identisch | 50/50 (full) | **identisch** |
| Tiered PGO aus | `DOTNET_TieredPGO=0` | identisch | 50/50 (full) | **identisch** |
| ReadyToRun aus | `DOTNET_ReadyToRun=0` | identisch | 50/50 (full) | **identisch** |
| Hardware-Intrinsics aus | `DOTNET_EnableHWIntrinsic=0` | identisch | 50/50 (full) | **identisch** |
| AVX2 aus | `DOTNET_EnableAVX2=0` | identisch | 50/50 (full) | **identisch** |
| Server-GC | `DOTNET_gcServer=1` | identisch | 50/50 (full) | **identisch** |
| Kultur `de-DE` | `CNCNEW_TEST_CULTURE=de-DE` | identisch | 50/50 (full) | **identisch** |
| Kultur `tr-TR` | `CNCNEW_TEST_CULTURE=tr-TR` | identisch | 50/50 (full) | **identisch** |
| Kultur `ar-SA` | `CNCNEW_TEST_CULTURE=ar-SA` | identisch | 50/50 (full) | **identisch** |
| **Linux** auf demselben Rechner | WSL2 (Ubuntu, Kernel 6.6), `dotnet publish -r linux-x64 --self-contained`, derselbe Inhalt über `/mnt/c` (ICU statt NLS, andere C-Laufzeit) | identisch | 50/50 (full) | **identisch** |
| andere .NET-Patchversion | – | – | – | **offen**: installiert ist nur .NET 10.0.12 |
| **zweiter physischer Rechner, andere CPU** | Anleitung in §4 | – | – | **offen**: hat nicht stattgefunden (braucht den Menschen und einen zweiten Rechner) |

Die Kultur des Entwicklungsrechners ist `de-AT`; alle Läufe ohne Schalter liefen also schon immer unter einer nicht invarianten Kultur. Der Schalter `CNCNEW_TEST_CULTURE` setzt in `SimTool` die Standardkultur aller
Threads, bevor irgendein Befehl läuft.

**Vor den Korrekturen** (Stand `981aec4`) bestand der Linux-Lauf nicht: der Definitions-Snapshot hatte 139.503.384 statt 144.626.133 Byte (Zeilenenden der Plattform), damit einen anderen Fingerabdruck; die
Endhashes und Ketten aller neun Läufe des Schnellsatzes waren dagegen schon identisch – die Rechnung selbst war portabel, nur ein Eingang nicht (D9).

## 2a. Die Matrix mit dem Manifest der Phase 22 (Q39.24, gemessen am 6. Oktober 2026, Stand `d7fca91`)

Phase 22 hat die Festkomma-Auflösung von 16 auf 24 Nachkommabits gestellt und damit beide Manifeste neu geschrieben (`docs/FIXED-POINT-Q39-24.md`). Die Zeilen dieses Rechners wurden mit den neuen Dateien
wiederholt; jede Zeile ist wieder `input-manifest check` plus `reference-manifest check` gegen die eingecheckten Dateien, „identisch“ heißt Exitcode 0 bei beiden und kein abweichendes Feld.

| Zeile | Wie | Eingänge | Rechnung (Satz) | Ergebnis |
|---|---|---|---|---|
| Release, Lauf 1 | `CnCNew.SimTool.exe …` (Kopie des `bin`-Verzeichnisses) | identisch | 50/50 (full) | **identisch** |
| Release, Lauf 2 | derselbe Aufruf noch einmal | identisch | 50/50 (full) | **identisch** |
| Debug-Build | `dotnet build -c Debug tools/CnCNew.SimTool` | identisch | 9/9 (**quick**) | **identisch** (der volle Satz wurde im Debug-Build wie in Phase 18 nicht gefahren) |
| Tiered Compilation aus | `DOTNET_TieredCompilation=0` | identisch | 50/50 (full) | **identisch** |
| Tiered PGO aus | `DOTNET_TieredPGO=0` | identisch | 50/50 (full) | **identisch** |
| ReadyToRun aus | `DOTNET_ReadyToRun=0` | identisch | 50/50 (full) | **identisch** |
| Hardware-Intrinsics aus | `DOTNET_EnableHWIntrinsic=0` | identisch | 50/50 (full) | **identisch** |
| AVX2 aus | `DOTNET_EnableAVX2=0` | identisch | 50/50 (full) | **identisch** |
| Server-GC | `DOTNET_gcServer=1` | identisch | 50/50 (full) | **identisch** |
| Kultur `de-DE` | `CNCNEW_TEST_CULTURE=de-DE` | identisch | 50/50 (full) | **identisch** |
| Kultur `tr-TR` | `CNCNEW_TEST_CULTURE=tr-TR` | identisch | 50/50 (full) | **identisch** |
| Kultur `ar-SA` | `CNCNEW_TEST_CULTURE=ar-SA` | identisch | 50/50 (full) | **identisch** |
| **Linux** auf demselben Rechner | WSL2 (Ubuntu, Kernel 6.6.87), `dotnet publish -r linux-x64 --self-contained`, derselbe Inhalt über `/mnt/c` | identisch | 50/50 (full) | **identisch** |
| andere .NET-Patchversion | – | – | – | **offen** wie in §2 |
| **zweiter physischer Rechner, andere CPU** | Anleitung in §4 | – | – | **offen** wie in §2 |

Für die Arithmetik heißt das: die für eine Bitzahl hergeleiteten Routinen (`FixedMath`: schriftliche Division auf Maschinenworten, bitweise erweiterte Wurzel, CORDIC mit 24 Schritten) rechnen mit und ohne
Hardware-Intrinsics dasselbe (`Math.BigMul` und `BitOperations.LeadingZeroCount` haben dann Softwarepfade) und unter Linux dasselbe wie unter Windows. Was §3 als nicht belegt nennt, bleibt nicht belegt.
Zusätzlich deterministisch gelaufen, ohne Teil des Manifests zu sein: zwölf Läufe bei 60 und 120 Hz (`docs/TICKRATE-MIGRATION.md` §11.5; zweiter frischer Lauf und Fortsetzung gleich, wo geprüft).

Neue Regel zu §5: **die Skala von `Fixed` steht an einer Stelle.** `Tests.FixedScaleSourceRules` verbietet sie als Zahl oder Verschiebung in allen Projekten und ihre Verwendung außerhalb der Simulation
(Ausnahme: `CnCNew.Presentation.FixedConvert`). Formate, die Rohwerte tragen, wechseln mit der Auflösung ihre Version; ein alter Stand wird abgelehnt, nie umgedeutet.

## 3. Was daraus folgt und was nicht

- **Belegt:** auf einem Rechner (AMD Ryzen 7 PRO 6850U, x64) ist das Ergebnis unabhängig von JIT-Stufe, PGO, ReadyToRun, Hardware-Intrinsics, GC-Art, Kultur, Debug/Release (Schnellsatz) und Betriebssystem
  (Windows 11 gegen Linux unter WSL2). Das Manifest besteht zweimal hintereinander.
- **Nicht belegt:** eine andere CPU (anderer Hersteller, andere Generation, ARM), eine andere .NET-Patch- oder Hauptversion, ein anderer Kernel als der von WSL2, ein natives Linux. Die Simulation rechnet in
  Ganzzahlen (`Fixed`, `FixedTrig`, `SageRandom`) und `SourceRules` verbietet `float`, Intrinsics und Kulturabhängiges im Kern und in allem, was ihn speist (D6); das macht eine CPU-Abhängigkeit unwahrscheinlich,
  ersetzt aber den Lauf nicht. **E1 im Register bleibt deshalb offen**, bis §4 einmal ausgeführt wurde.
- Die Matrix prüft die 50 Läufe des Manifests. Pfade, die keiner dieser Läufe betritt (Spielereingaben eines Menschen, selten benutzte Module), sind durch sie nicht abgedeckt; D8 hat gezeigt, dass gerade dort
  Fehler lagen (vier unvermutete Zustandsänderungen ohne `MarkDirty` in 19 von 45 Missionskarten).

## 4. Anleitung: der Lauf auf einem zweiten Rechner

Braucht einen Menschen. Nichts davon wird committet; Originaldaten verlassen den eigenen Besitz nicht (lokal kopieren, kein Upload).

1. Auf dem Entwicklungsrechner: `dotnet publish tools/CnCNew.SimTool -c Release -r win-x64 --self-contained true -o <ordner>\simtool` (für Linux `-r linux-x64`, für ARM `-r win-arm64`/`linux-arm64`).
2. Auf einen Datenträger kopieren: `<ordner>\simtool`, das Verzeichnis `.local\phase2-import-a` (rund 25.682 Dateien) und `baselines\input-manifest.json`, `baselines\reference-manifest.json`.
3. Auf dem zweiten Rechner (kein .NET nötig), im Verzeichnis mit den drei Teilen:
   ```
   simtool\CnCNew.SimTool input-manifest check phase2-import-a input-manifest.json
   simtool\CnCNew.SimTool reference-manifest check phase2-import-a reference-manifest.json --set quick
   simtool\CnCNew.SimTool reference-manifest check phase2-import-a reference-manifest.json --set full
   ```
4. Ergebnis melden: die drei Schlusszeilen („identical“ oder die erste Abweichung je Lauf), dazu CPU-Modell, Betriebssystem und die Ausgabe von `simtool\CnCNew.SimTool --version`, falls vorhanden.
5. Bei einer Abweichung zuerst das Eingangsmanifest ansehen: weicht es ab, liegt der Fehler in den Daten oder ihrer Aufbereitung; ist es gleich und ein Lauf weicht ab, liegt er in der Rechnung –
   dann denselben Lauf mit `--only <id> --verify-incremental` wiederholen und das Ergebnis in `docs/DETERMINISM-FINDINGS.md` eintragen.

Das Ergebnis gehört in die Tabelle in §2 (Zeile „zweiter physischer Rechner“) und in `docs/MULTIPLAYER-PLAN.md` §4 (E1).

## 5. Regeln, die daraus bleiben

- Das Manifest ist die Messlatte (`docs/MULTIPLAYER-PLAN.md` §6 Punkt 2). Wer es ändert, tut das in einem eigenen Commit mit einer Ursache und Vorher/Nachher-Werten.
- Neue Simulationseingänge laufen über die geprüften Wandlungsroutinen (`SingleBits.ToDecimal` für Fließkommawerte aus Dateien, `ExactBoneMath` für die Knochentabelle); `SourceRules` und `FeederSourceRules` erzwingen das.
- Jede Zustandsänderung ruft `MarkDirty`; ein von Hand gesetztes `Dirty`-Flag ist verboten (Regel in `SourceRules`). Neue Module laufen einmal mit `--verify-incremental`.
- Vor jedem Phasenabschluss: `input-manifest check`, `reference-manifest check --set full`, und bei Änderungen an Formaten oder Fingerabdrücken zusätzlich die Linux-Zeile.

# Leistungsprofil der Simulation

Stand: Phase 18, Punkt 5 (`docs/HANDOFF-PHASE18.md`); Register `docs/MULTIPLAYER-PLAN.md` §4, E4 und E20.

Dieses Dokument beschreibt das Messwerkzeug (`SimTool perf`, `SimTool perf-mission`, `scripts/perf-profile.ps1`), die
Tickphasen, wie sie gemessen werden, und wie eine ruhige Messung abläuft. **Es wird hier nichts optimiert** – das ist
Phase 19. Die Ergebnistabelle in §6 ist leer, bis der Koordinator die Läufe auf einem unbelasteten Rechner gefahren hat;
was beim Bau des Werkzeugs nebenbei gemessen wurde, steht ausdrücklich getrennt in §7.

## 1. Grundsatz: die Simulation hat keine Uhr

`CnCNew.Simulation` darf keine Wandzeit kennen (Test `SourceRules`: kein `Stopwatch`, kein `DateTime`, keine Threads,
kein `float`). Die Phasenzeiten werden deshalb **nicht in der Simulation gemessen**. Die Simulation meldet nur, wo eine
Phase beginnt und endet; die Uhr liest das Werkzeug.

- `ITickPhaseObserver` (`src/CnCNew.Simulation/World/TickPhase.cs`): `PhaseStarted(TickPhase)`, `PhaseEnded(TickPhase)`
  und – für die Aufschlüsselung der Update-Phase – `UpdateStarted(SimModule)`, `UpdateEnded(SimModule)`.
- `SimulationWorld.PhaseObserver` ist normalerweise `null`. Die Eigenschaft gehört **nicht zum Zustand**: sie wird weder
  gehasht noch in den Snapshot geschrieben; eine wiederhergestellte Welt hat keinen Beobachter.
- Ohne Beobachter läuft `Step()` wie bisher: eine einzige Nullprüfung am Anfang, danach derselbe Code wie vor der
  Änderung. Mit Beobachter läuft `StepObserved`, dieselben Aufrufe in derselben Reihenfolge, nur mit den Meldungen
  dazwischen. In `RunUpdates` kommt je Modul-Update eine Nullprüfung hinzu. Es wird nichts allokiert.
- `MissionSession.Step` und `SkirmishMatchSession.Step` melden ihre eigenen Anteile über denselben Beobachter der Welt
  (Skripte bzw. KI vor dem Weltschritt, Ereignisnachlauf bzw. Siegprüfung danach).
- `SimulationWorld.DirtyEntityCount` (nur lesend) sagt dem Werkzeug, wie viele Objekte die Hash-Phase gleich neu
  beschreiben muss.

Abgesichert durch `tests/CnCNew.Simulation.Tests/Tests.TickPhases.cs`: eine Welt mit Beobachter liefert dieselben
Tick-Hashes wie ohne (Stress-Wiedergabe ohne Gelände, Skirmish mit KI und Sicht, Mission mit Skripten); der Beobachter
sieht jede Phase genau einmal je Tick in der unten genannten Reihenfolge; Modul-Updates liegen immer innerhalb der
Update-Phase; der Snapshot mit Beobachter ist byteidentisch zu dem ohne; die KIs geben dieselben Befehle.

## 2. Die Tickphasen, wie sie gemessen werden

Die Namen sind die des Codes (`TickPhase`, Reihenfolge von `SimulationWorld.Step`). Es gibt **keine eigene
Kollisionsphase**: Bewegung und die Zustellung von Kollisionen (`DeliverCollisions`) laufen innerhalb der Modul-Updates.

| Nr. | `TickPhase` | Methode | Inhalt |
|---:|---|---|---|
| 1 | `Commands` | `ExecuteCommands` | Befehle dieses Ticks, geordnet nach Spieler und Sequenz |
| 2 | `Timers` | `FireTimers` | fällige Zeitgeber |
| 3 | `Updates` | `RunUpdates` | schlafende Modul-Updates (Bewegung, Kollision, Produktion, KI der Einheiten …) |
| 4 | `Acquisition` | `StepAcquisition` | Zielerwerb |
| 5 | `Turrets` | `StepTurrets` | Türme |
| 6 | `FiringTrackers` | `StepFiringTrackers` | Feuerstufen |
| 7 | `Events` | `DeliverEvents` | Zustellung der bis hierher entstandenen Ereignisse |
| 8 | `Sales` | `ProcessSales` | Verkäufe |
| 9 | `Destroys` | `ProcessDestroys` | Entfernen der zerstörten Objekte |
| 10 | `DelayedDamage` | `ProcessDelayedDamage` | verzögerter Schaden |
| 11 | `Spawns` | `ProcessSpawns` | Erzeugen der vorgemerkten Objekte |
| 12 | `SpawnEvents` | `DeliverEvents` | Ereignisse aus 9 bis 11 |
| 13 | `Vision` | `RevealVision` | Sichtfelder: alle Zellen aller Spieler löschen, dann je schauendem Objekt und Betrachter ein Kreis; nur jeden dritten Tick (`VisionIntervalTicks`), sonst leer |
| 14 | `VisionHash` | `VisionMap.Hash` | Hash über die Sichtzellen; läuft genau dann, wenn 13 läuft |
| 15 | `Hash` | `ComputeIncrementalHash` | Tickzähler weiter, berührte Objekte neu beschreiben, Zustands-Hash |

Dazu die Anteile der Sitzungen (in der Ausgabe mit `+` markiert):

| `TickPhase` | Wo | Inhalt |
|---|---|---|
| `SkirmishAi` | `SkirmishMatchSession.Step`, vor dem Weltschritt | Wissenschaftspunkte-Ersatz und `SkirmishAi.Update` aller Computerspieler |
| `SkirmishVictory` | danach | Siegprüfung alle 15 Ticks |
| `MissionScripts` | `MissionSession.Step`, vor dem Weltschritt | `ScriptEngine.Update` |
| `MissionEvents` | danach | Schusszählung und `ScriptEngine.Observe` |

„(unattributed)“ in der Ausgabe ist die Zeit zwischen den Phasen einschließlich der Uhrzugriffe des Beobachters selbst.

**Aufschlüsselung der Update-Phase nach Modultyp.** Sie kommt ohne Eingriff in die einzelnen Module aus: gemessen wird
an der einen Stelle, an der der Scheduler ein Update aufruft (`RunUpdates`). Je Modulname zeigt das Werkzeug Aufrufe,
Summe, Mittel und Maximum. Die Differenz zwischen der Summe der Module und der Phase ist die Arbeit des Schedulers
(Heap) plus die Uhrzugriffe. Jedes Update kostet durch die Messung zwei Uhrzugriffe und eine Tabellensuche (Größenordnung
50–100 ns); bei Millionen sehr kurzer Updates verfälscht das die Phase `Updates` nach oben. `--no-modules` schaltet die
Aufschlüsselung ab, `--no-observer` den Beobachter ganz (dann wird nur der ganze Tick gemessen).

**Auflösung.** Die Uhr ist `Stopwatch.GetTimestamp()`; unter Windows sind das 100 ns. Phasen, die kürzer sind, erscheinen
als 0,0 oder 0,1 µs; ihre Mittelwerte sind nur der Größenordnung nach zu lesen.

## 3. Die Befehle

```powershell
dotnet build -c Release tools/CnCNew.SimTool
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- perf .local/phase2-import-a --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --replay --csv <datei.csv>
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- perf .local/phase2-import-a --map "Maps/Alpine Assault/Alpine Assault.map" --factions FactionChina,FactionGLA --ticks 54000 --replay
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- perf-mission .local/phase2-import-a --map "Maps/MD_USA05/MD_USA05.map" --ticks 3000 --csv <datei.csv>
pwsh scripts/perf-profile.ps1 [-Ticks 54000] [-MissionTicks 3000,9000] [-Only 'large-*'] [-OutDir <verzeichnis>] [-TimeoutMinutes 60]
```

Optionen von `perf`: `--map` (logischer Kartenpfad), `--players N` (die ersten N spielbaren Fraktionen, wie `ai-match`)
oder `--factions A,B,…`, `--ticks T` (Tickbudget; ein Match endet früher, wenn ein Sieger feststeht, plus Nachlauf),
`--seed N`, `--replay`, `--csv <datei>`, `--top K` (Module, Standard 15), `--sample N` (Fenster der CSV, Standard 300
Ticks), `--no-modules`, `--no-observer`. `perf-mission` kennt statt Spielern `--difficulty easy|normal|hard`.

Ausgabe je Lauf:

1. Rechner, Laufzeit, Uhrauflösung; Karte mit Zellgröße; Dauer des Aufbaus (Definitionen, Modellgeometrie, Karte).
2. **Zeit je Tick** in µs: Mittel, Median, 95-%- und 99-%-Quantil (nächster Rang), Maximum; dieselben Werte je Phase und
   ihr Anteil an der Gesamtzeit; das Mittel ohne die ersten 300 Ticks (JIT); der Weltschritt allein.
3. **Längster Tick** (insgesamt und nach den ersten 300 Ticks): Tick, Dauer, Objektzahl, Allokation und die Phase, die
   ihn bestimmt.
4. **Update-Phase nach Modultyp** (die K teuersten).
5. **Allokationen** (`GC.GetAllocatedBytesForCurrentThread` um jeden Sitzungsschritt): Mittel, Median, Maximum je Tick,
   Summe, hochgerechnet je Spielminute; Zahl der GC-Läufe der Generationen 0/1/2 während des Laufs.
6. **Objektzahl**: Minimum, Maximum, Ende; in der CSV eine Zeile je Fenster (Tick, Objekte, Tickzeit Mittel/Maximum,
   Allokation, berührte Objekte je Tick, Mittel je Phase; bei Missionen zusätzlich die Kosten eines vollständigen
   Skript-Hashes an dieser Stelle).
7. **Endhash und Kette** des Laufs. Sie müssen dem Lauf ohne Beobachter gleichen: `ai-match --max-ticks T` bzw.
   `mission-run --ticks T` mit denselben Parametern drucken dasselbe Paar. Dazu der Vergleich des inkrementell geführten
   Welt-Hashes mit einer vollen Neuberechnung; eine Abweichung ist eine Zustandsänderung ohne `MarkDirty` (Befund D8) und
   wird als `WARNING` gemeldet, ohne den Lauf scheitern zu lassen.
8. Mit `--replay`: die aufgezeichneten Befehle auf einer frischen Welt **ohne KI und ohne Beobachter**: µs je Tick,
   Ticks je Sekunde, **Aufholtempo** als Vielfaches der Echtzeit bei 30 Hz (Ticks je Sekunde / 30) und bei 60 Hz (/ 60),
   Allokation und GC; der Endhash der Wiedergabe muss dem des beobachteten Laufs gleichen (sonst Exitcode 1).
9. **Snapshot** am Ende: Größe in Bytes, Bytes je Objekt, Schreiben und Wiederherstellen in ms (jeweils der beste von
   drei Durchgängen), Hashvergleich nach dem Wiederherstellen.
10. **Hochrechnung auf 60 Hz**: Anteil eines Kerns (mittlere Tickzeit × 60) und Aufholfaktor. Annahme: gleiche Kosten je
    Tick. Das ist eine obere Schätzung für alles, was je Tick arbeitet, und eine untere für alles, was an Zeit hängt (bei
    60 Hz doppelt so viele Ticks je Spielminute).
11. **Sonden** zu den drei Spuren des Handoffs (§5).
12. Eine Zeile `summary: schlüssel=wert;…` für das Skript.

Zahlen werden mit Punkt als Dezimaltrenner ausgegeben (invariante Kultur), unabhängig vom System.

## 4. Eine ruhige Messung

1. Rechner: Netzbetrieb, Energieprofil „Höchstleistung“, keine anderen Agenten, keine IDE-Indizierung, kein Build, kein
   Client. Die CPU-Auslastung vor dem Start sollte unter 5 % liegen; das Skript schreibt sie je Lauf in die Tabelle,
   prüft sie aber nicht.
2. `pwsh scripts/perf-profile.ps1` aus dem Repo. Das Skript baut `SimTool` in Release, kopiert die Binärdateien nach
   `<OutDir>\bin` (ein späterer Build stört die Messung nicht) und fährt die Liste **strikt nacheinander**. Jeder Lauf
   hat eine Zeitgrenze (`-TimeoutMinutes`, Standard 60); ein überschrittener Lauf wird beendet und als `TIMEOUT` geführt.
3. Ausgabe unter `%TEMP%\cncnew-perf` (oder `-OutDir`, außerhalb des Repos): je Lauf `<name>.txt` und `<name>.csv`,
   am Ende `summary.md` mit zwei Tabellen (Übersicht und Tickphasen).
4. Zweimal fahren und die beiden `summary.md` vergleichen: die Endhashes müssen gleich sein, die Mittelwerte sollten um
   wenige Prozent streuen. Streuen sie stärker, war der Rechner nicht ruhig.
5. Die Tabellen nach §6 übernehmen, mit Datum, Commit und der Hardwarezeile der Ausgabe.

Die Läufe des Skripts:

| Name | Karte | Zellen | Startpositionen | Spieler | Ticks |
|---|---|---|---:|---:|---:|
| `small-2p` | `Maps/Scorched Earth/Scorched Earth.map` | 190 × 190 | 2 | 2 | 54.000 |
| `medium-2p` | `Maps/Tournament Tundra/Tournament Tundra.map` | 268 × 269 | 4 | 2 | 54.000 |
| `medium-4p` | wie oben | 268 × 269 | 4 | 4 | 54.000 |
| `large-2p` | `Maps/Twilight Flame/Twilight Flame.map` | 486 × 469 | 8 | 2 | 54.000 |
| `large-4p` | wie oben | 486 × 469 | 8 | 4 | 54.000 |
| `large-8p` | wie oben | 486 × 469 | 8 | 8 | 54.000 |
| `mission-MD_USA05-3000`, `-9000` | `Maps/MD_USA05/MD_USA05.map` | 527 × 521 | – | 10 Seiten | 3.000 und 9.000 |
| `mission-MD_GLA05-3000`, `-9000` | `Maps/MD_GLA05/MD_GLA05.map` | 589 × 589 | – | 12 Seiten | 3.000 und 9.000 |

Die Karten sind nach Größe gewählt (die Zwei-Spieler-Karte mit der kleinsten Spielfläche, eine mittlere Vier-Spieler-Karte,
die Acht-Spieler-Karte mit der größten Fläche; Zellen = Pfadzellen laut `SimTool perf`); die mittlere und die große Karte laufen zusätzlich mit weniger Spielern, damit sich
Kartengröße und Spielerzahl trennen lassen. Alle Gefechte laufen mit `--replay`. `-Ticks` und `-Only` verkürzen das
Programm für einen Probelauf. Ein Match, das vor dem Tickbudget entschieden ist, endet früher; die Spalte „Ticks“ der
Tabelle nennt die tatsächlich gelaufenen.

## 5. Die drei Spuren des Handoffs

Das Werkzeug kann jede der drei unbelegten Spuren aus `docs/HANDOFF-PHASE18.md` Punkt 5 bestätigen oder widerlegen:

- **(a) Der Sicht-Hash wird alle drei Ticks über jede Zelle jedes Spielers neu berechnet.** Phase `VisionHash` (und
  `Vision` für das Löschen und Aufdecken) je Tick und je Auffrischung; dazu am Ende 20 direkte Aufrufe von
  `VisionMap.Hash()` auf dem Endzustand, mit ns je Zellbyte. Die Zahl der Zellbytes ist Zellen × (Spieler + System).
- **(b) `Describe` hasht Vorlagen-, Modul- und Tag-Namen zeichenweise.** Ein zählender `IStateWriter` beschreibt alle
  Objekte des Endzustands: Wörter insgesamt, davon aus Texten (ein Wort je Zeichen plus die Länge), aufgeteilt nach
  Feld (`templateName`, `name`, `tag`). Dazu der Zeitvergleich des echten `HashStateWriter` mit einem Schreiber, der von
  einem Text nur die Länge mischt: die Differenz sind die Zeichen. Im Lauf: Phase `Hash` und die Zahl der je Tick
  berührten Objekte (`DirtyEntityCount`).
- **(c) `MissionSession.StateHash()` beschreibt den Skriptzustand jedes Mal vollständig.** `perf-mission` misst
  `ScriptEngine.StateHash()` am Ende 20-mal ohne Zustandsänderung dazwischen (Kosten, Allokation, Wörter) und einmal je
  CSV-Fenster über den Lauf, jeweils außerhalb der Tickzeit. `MissionSession.Step` selbst ruft den kombinierten Hash
  nicht auf (es liefert den Hash der Welt); die Zahl ist der Aufschlag für einen Aufrufer, der ihn je Tick will.

## 6. Ergebnisse (ruhige Messung)

Gemessen am 6. Oktober 2026, Stand `b432fb7`, mit `scripts/perf-profile.ps1` (Standardliste, strikt nacheinander, nichts sonst auf dem Rechner; CPU-Last vor den Läufen 0–6 %, vor dem ersten 15 %). Hardware: AMD Ryzen 7 PRO 6850U (AMD64 Family 25 Model 68), 16 logische Prozessoren, 28 GiB, Windows 10.0.26200, .NET 10.0.12, Workstation-GC. Ein Rechner, ein Lauf je Zeile – keine Streuung erhoben. In allen zehn Läufen ist der inkrementelle Hash am Ende gleich dem voll berechneten.

### 6.1 Übersicht (Zeiten in Mikrosekunden je Tick)

| Lauf | Karte (Zellen) | Spieler | Ticks | Mittel | Median | 95 % | 99 % | Maximum | Bytes je Tick | GC 0/1/2 | Objekte min/max/Ende | Wiedergabe ohne KI | Aufholen bei 60 Hz | Kernanteil bei 60 Hz | Snapshot (Bytes; schreiben/lesen ms) | Endhash |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---:|---:|---:|---|---|
| small-2p | Scorched Earth (190×190) | 2 | 23.656 (entschieden) | 505,8 | 375,1 | 959,7 | 1.647,8 | 37.349 | 12.629 | 36/1/0 | 8/77/51 | 387,4 | 43,0× | 3,0 % | 294.114; 1,9/5,1 | `A8969552F4CE324B` |
| medium-2p | Tournament Tundra (268×269) | 2 | 21.706 (entschieden) | 784,0 | 693,4 | 1.682,2 | 2.025,9 | 56.250 | 24.460 | 64/6/0 | 16/152/67 | 668,1 | 24,9× | 4,7 % | 484.356; 2,7/7,0 | `FF4E157157884F29` |
| medium-4p | Tournament Tundra | 4 | 54.000 | 1.866,3 | 1.371,5 | 5.149,9 | 7.371,2 | 86.975 | 34.095 | 221/35/1 | 20/274/46 | 1.795,3 | 9,3× | 11,2 % | 577.560; 1,7/5,4 | `95EC2A8639D7A807` |
| large-2p | Twilight Flame (486×469) | 2 | 54.000 | 797,4 | 489,6 | 1.680,5 | 1.879,5 | 138.596 | 24.065 | 156/8/0 | 16/105/61 | 749,0 | 22,3× | 4,8 % | 1.066.372; 3,3/8,5 | `868959729974E7FD` |
| large-4p | Twilight Flame | 4 | 52.441 (entschieden) | 2.071,2 | 1.593,4 | 3.943,8 | 5.156,3 | 253.739 | 28.620 | 180/24/1 | 20/200/124 | 2.014,8 | 8,3× | 12,4 % | 1.923.632; 5,6/16,7 | `F34474400B0A780D` |
| large-8p | Twilight Flame | 8 | 54.000 | **4.978,1** | 4.294,5 | 8.358,5 | 13.597,8 | **547.113** | 95.273 | 616/68/2 | 28/337/155 | 4.870,3 | **3,4×** | **29,9 %** | 3.198.580; 8,9/22,7 | `0A69FC72FF6975CD` |
| mission-MD_USA05 | MD_USA05 (527×521), 10 Seiten | – | 3.000 | 5.944,9 | 4.502,0 | 9.435,2 | 20.081,8 | 114.379 | 208.129 | 74/2/0 | 1.835/1.909/1.869 | – | 2,8× (live) | 35,7 % | 6.549.327; 33,8/134,6 | `1CB28E6AA923BDEF` |
| mission-MD_USA05 | | – | 9.000 | 5.546,7 | 4.190,0 | 8.681,2 | 12.145,5 | 116.693 | 202.246 | 218/5/0 | 1.835/1.909/1.869 | – | 3,0× (live) | 33,3 % | 6.550.224; 33,7/130,6 | `D5AC3B28DC65F103` |
| mission-MD_GLA05 | MD_GLA05 (589×589), 12 Seiten | – | 3.000 | **13.468,0** | 10.942,7 | 20.047,3 | 37.188,2 | 160.996 | 4.093.904 | 1.435/6/2 | 2.249/2.304/2.249 | – | **1,2× (live)** | **80,8 %** | 9.009.216; 43,1/163,7 | `826CCD82C8536DC2` |
| mission-MD_GLA05 | | – | 9.000 | 12.644,2 | 9.909,4 | 18.759,9 | 35.937,5 | 168.834 | 4.088.597 | 4.302/92/7 | 2.243/2.304/2.247 | – | 1,3× (live) | 75,9 % | 9.022.665; 43,0/119,1 | `5733B70B4C846DB1` |

### 6.2 Tickphasen (Mittel in Mikrosekunden je Tick; Phasen unter 5 µs weggelassen)

| Lauf | Updates | Acquisition | Vision | VisionHash | Hash | KI | Skripte |
|---|---:|---:|---:|---:|---:|---:|---:|
| small-2p | 221,9 | 12,5 | 108,9 | 22,1 | 116,1 | 18,6 | – |
| medium-2p | 370,8 | 15,4 | 162,2 | 41,7 | 171,1 | 17,1 | – |
| medium-4p | 1.167,2 | 21,1 | 284,4 | 67,0 | 297,5 | 24,0 | – |
| large-2p | 293,4 | 9,0 | 215,7 | 128,5 | 134,7 | 12,4 | – |
| large-4p | 1.090,5 | 17,6 | 407,1 | 212,0 | 315,6 | 23,7 | – |
| large-8p | 3.264,2 | 36,9 | 716,0 | 378,5 | 530,8 | 44,7 | – |
| MD_USA05 (3.000) | 2.179,5 | 311,5 | 921,3 | 562,3 | 1.681,2 | – | 278,8 |
| MD_USA05 (9.000) | 1.980,1 | 276,4 | 854,7 | 564,5 | 1.635,0 | – | 231,4 |
| MD_GLA05 (3.000) | 5.261,5 | 540,8 | 1.531,8 | 841,2 | 2.271,3 | – | 2.999,7 |
| MD_GLA05 (9.000) | 4.628,1 | 502,8 | 1.670,3 | 822,4 | 2.164,6 | – | 2.844,0 |

### 6.3 Was die Messung sagt

1. **Die Kosten hängen an Spielerzahl und Karte, nicht an der Objektzahl.** 8 Spieler mit höchstens 337 Objekten kosten 4,98 ms je Tick; der synthetische Stresstest mit über 13.000 Objekten 61 µs. Von 2 auf 8 Spieler auf derselben Karte steigt der Tick um das 6,2-Fache.
2. **Zwei Drittel sind Modul-Updates, und davon vier Fünftel `AIUpdateInterface`** (large-8p: 81,4 % der Update-Zeit, 28,8 µs im Mittel je Aufruf, bis 52 ms in einem Aufruf). Das ist Bewegung samt Wegsuche. Der längste Tick überhaupt (547 ms bei Tick 52.843, 158 Objekte) besteht zu 100 % aus Updates: ein einzelner Tick, der 16 Bilder bei 30 Hz kostet. In `MD_GLA05` kommt `PointDefenseLaserUpdate` mit 30,6 % der Update-Zeit dazu.
3. **Sicht und Hash zusammen kosten ein Drittel** (large-8p: Vision 716 + VisionHash 379 + Hash 531 = 1.626 µs von 4.978). Alle drei wachsen mit Kartenfläche und Spielerzahl, nicht mit dem, was sich ändert.
4. **Die drei Spuren des Handoffs:** (a) bestätigt – `VisionHash` 379 µs im Mittel auf Twilight Flame mit 8 Spielern, 822–841 µs auf `MD_GLA05`; (b) teilweise – die Hash-Phase kostet 135–531 µs in Gefechten und 1,6–2,3 ms in Missionen, der größere Hebel ist laut Vorabmessung (§7), dass die meisten Objekte jeden Tick als geändert gelten; (c) trifft zu, wirkt aber nicht auf den Tick (niemand ruft `MissionSession.StateHash()` je Tick).
5. **Skripte:** in `MD_GLA05` 2,8–3,0 ms je Tick (22 % des Ticks) und 4 MB Allokation je Tick mit 1.435 Gen-0-Sammlungen in 3.000 Ticks; in `MD_USA05` 0,23–0,28 ms.
6. **Hochrechnung auf 60 Hz** (Mittel × 60, ohne dass sich etwas am Code ändert): Gefecht mit 8 Spielern **30 % eines Kerns**, Aufholtempo **3,4-fach**; `MD_USA05` 33–36 % und rund 3-fach; `MD_GLA05` **76–81 %** und nur **1,2- bis 1,3-fach**. Die Sollwerte für Phase 19 (`docs/HANDOFF-PHASE19.md`) sind daran zu messen; die Quantile zählen mehr als das Mittel: das 99-%-Quantil des 8-Spieler-Laufs liegt bei 13,6 ms und damit nahe an einem ganzen 60-Hz-Tick (16,7 ms), das von `MD_GLA05` bei 36–37 ms darüber.
7. **Snapshot:** 3,2 MB für 155 Objekte auf Twilight Flame (der größte Teil ist nicht das Objekt, sondern Karte und Sicht), 9,0 MB für 2.249 Objekte in `MD_GLA05`; Schreiben 9–43 ms, Lesen 23–164 ms.

Nicht gemessen: Streuung zwischen Läufen, ein zweiter Rechner, Server-GC, der Client (Darstellung) neben der Simulation, die Kosten des Beobachters selbst (der Lauf ohne Beobachter ist die Spalte „Wiedergabe ohne KI“; large-8p: 4.870 gegenüber 4.978 µs mit Beobachter und KI).

## 7. Vorläufig, nicht ruhig gemessen

Alles in diesem Abschnitt stammt vom 6. Oktober 2026, Stand `b6b7454` plus Werkzeug, **von einem Rechner, auf dem
gleichzeitig andere Agenten bauten und testeten** (CPU-Auslastung vor den Läufen 60–96 %). Die Zahlen zeigen, dass das
Werkzeug arbeitet und wohin die Zeit der Größenordnung nach geht; sie sind **keine Ergebnisse** und gehören nicht in §6.
Rechner: AMD64 Family 25 Model 68, 16 logische Prozessoren, Windows 10.0.26200, .NET 10.0.12, Workstation-GC.

### 7.1 Hashes mit und ohne Beobachter

| Lauf | ohne Beobachter | mit Beobachter (`perf`) |
|---|---|---|
| `reference-match` (neuer Stand, kein Beobachter) | FactionGLA bei Tick 14.490, `23B4B25DF62765D8`, Kette `DE5849AF9F8493C4` – wie `docs/HANDOFF-PHASE18.md` | – |
| `stress` / `hash` der Stress-Wiedergabe (alter Stand `e5046b7` und neuer Stand, je 13 Läufe) | `56882F763EABEF67`, Kette `B8E663493F2C73D5` | – |
| Scorched Earth, 2 Spieler, 18.000 Ticks | `ai-match` alter und neuer Stand: `7C4FC065EF2E3022`, Kette `D3754175A10F3630` | `7C4FC065EF2E3022`, Kette `D3754175A10F3630` |
| Twilight Flame, 8 Spieler, 18.000 Ticks | Handoff (5. Oktober): `72CF0F846E0D9AA6`, Kette `5E446FE63C5BEF46`; `perf --no-observer`: dasselbe | `72CF0F846E0D9AA6`, Kette `5E446FE63C5BEF46` |
| `MD_USA05`, 3.000 Ticks | Handoff: `1CB28E6AA923BDEF`, Kette `0816C3438CB7F689` | `1CB28E6AA923BDEF`, Kette `0816C3438CB7F689` |
| `MD_GLA05`, 3.000 Ticks | Handoff: `317B464E8BA98377`, Kette `6823506872EBFF9E` | `317B464E8BA98377`, Kette `6823506872EBFF9E` |

`tests/CnCNew.Simulation.Tests` (Debug und Release mit `--long`) sind grün, darunter `SourceRules` und die beiden neuen
Beobachter-Tests.

### 7.2 Stresstest ohne Beobachter, alter gegen neuen Stand

Zehn abwechselnde Läufe `SimTool hash` der aufgezeichneten Stress-Wiedergabe (216.000 Ticks, ganze Prozesslaufzeit),
unter Last: alter Stand Minimum 29,7 s, Median 35,4 s; neuer Stand Minimum 29,1 s, Median 34,7 s. Drei abwechselnde
Läufe `SimTool stress` („Run 1“, µs je Tick): alt 91,8 / 103,9 / 77,3, neu 153,8 / 89,8 / 91,4. Die Streuung zwischen
zwei Läufen desselben Stands ist größer als jeder Unterschied zwischen den Ständen; eine Verschlechterung ist nicht
erkennbar, **aber der ruhige Wert (61 µs je Tick laut Handoff) ist hier nicht nachgemessen** – das gehört zur ruhigen
Messung.

### 7.3 Zwei Gefechte und zwei Missionen (nicht ruhig)

| Lauf | Zellen | Ticks | Mittel µs | Median | 95 % | 99 % | Max | Welt allein | KI / Skripte | Bytes je Tick | GC 0/1/2 | Objekte min/max/Ende | Wiedergabe µs | Aufholen 30 / 60 Hz | Kern bei 60 Hz |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---:|---|---:|
| Scorched Earth, 2 Spieler | 190 × 190 | 18.000 | 824,9 | 600,6 | 1.675,9 | 2.920,2 | 71.252 | 780,9 | 42,5 | 11.782 | 26/3/0 | 8/61/57 | 601,4 | 55,4× / 27,7× | 4,9 % |
| Twilight Flame, 8 Spieler | 486 × 469 | 18.000 | 8.245,7 | 6.698,7 | 15.638,3 | 28.521,5 | 733.460 | 8.087,6 | 155,1 | 81.898 | 178/28/1 | 28/268/234 | 5.456,5 | 6,1× / 3,1× | 49,5 % |
| `MD_USA05` | 527 × 521 | 3.000 | 9.050,3 | 7.184,9 | 16.810,5 | 27.324,0 | 268.823 | 8.411,4 | 631,6 | 208.549 | 74/3/0 | 1.835/1.909/1.869 | – | – | 54,3 % |
| `MD_GLA05` | 589 × 589 | 3.000 | 15.647,2 | 12.714,4 | 25.541,5 | 48.480,4 | 190.279 | 12.071,2 | 3.570,0 | 4.093.461 | 1.435/8/2 | 2.249/2.304/2.249 | – | – | 93,9 % |

Derselbe Acht-Spieler-Lauf mit `--no-observer` lief später bei geringerer Last mit 5.090 µs im Mittel; der Unterschied
zu 8.246 µs ist Rechnerlast, nicht der Beobachter (die Wiedergabe desselben Laufs ohne Beobachter brauchte 5.457 µs).
`MD_GLA05` lief eine halbe Stunde später unter höherer Last mit 30.287 µs im Mittel. So groß ist die Streuung hier.

Phasen im Mittel je Tick (µs) und Anteil an der Tickzeit:

| Phase | Scorched Earth 2 | Twilight Flame 8 | `MD_USA05` | `MD_GLA05` |
|---|---:|---:|---:|---:|
| `Updates` | 354,6 (43,0 %) | 5.226,9 (63,4 %) | 3.328,4 (36,8 %) | 5.997,4 (38,3 %) |
| `Acquisition` | 19,9 (2,4 %) | 105,8 (1,3 %) | 536,7 (5,9 %) | 629,1 (4,0 %) |
| `Vision` | 189,1 (22,9 %) | 1.123,8 (13,6 %) | 1.333,0 (14,7 %) | 1.818,2 (11,6 %) |
| `VisionHash` | 42,5 (5,2 %) | 674,1 (8,2 %) | 841,7 (9,3 %) | 960,1 (6,1 %) |
| `Hash` | 164,2 (19,9 %) | 935,0 (11,3 %) | 2.354,8 (26,0 %) | 2.646,3 (16,9 %) |
| `SkirmishAi` / `MissionScripts` | 42,5 (5,2 %) | 155,1 (1,9 %) | 631,6 (7,0 %) | 3.570,0 (22,8 %) |
| alle übrigen zusammen | unter 2 % | unter 1 % | unter 1 % | unter 1 % |

Update-Phase nach Modultyp, Anteil an der Phase: Twilight Flame `AIUpdateInterface` 78,1 % (1,39 Mio. Aufrufe, Mittel
52,7 µs, Maximum 88 ms), `SupplyTruckAIUpdate` 5,0 %, `DozerAIUpdate` 3,5 %, `TransportAIUpdate` 3,4 %; Scorched Earth
`AIUpdateInterface` 66,6 %; `MD_USA05` `AIUpdateInterface` 24,8 %, `DemoTrapUpdate` 22,6 %, `WorkerAIUpdate` 16,9 %,
`StealthUpdate` 3,86 Mio. Aufrufe zu je 0,05 µs; `MD_GLA05` `AIUpdateInterface` 46,4 %, `PointDefenseLaserUpdate` 28,3 %.
Der längste Tick des Acht-Spieler-Laufs (Tick 12.683) dauerte 733 ms und lag zu 100 % in `Updates` – ein einzelner
Ausreißer dieser Größe ist genau das, was E20 meint. Im Zwei-Spieler-Lauf lag der längste Tick nach dem Anlauf (70 ms,
Tick 929) zu 92 % in `SkirmishAi`.

Snapshot am Ende: Scorched Earth 304.301 Byte für 57 Objekte (2,8 ms schreiben, 8,1 ms lesen); Twilight Flame
3.452.779 Byte für 234 Objekte (12,0 / 33,4 ms); `MD_USA05` 6.549.327 Byte für 1.869 Objekte (49,4 / 171,8 ms);
`MD_GLA05` 9.009.216 Byte für 2.249 Objekte (50,1 / 178,6 ms). Die Größe hängt vor allem an der Karte (die Sichtzellen stehen je Spieler als
Base64 im Snapshot), weniger an der Objektzahl.

### 7.4 Die drei Spuren (nicht ruhig)

- **(a) bestätigt.** `VisionMap.Hash()` läuft bei jeder Auffrischung (jeden dritten Tick) über jedes Zellbyte jedes
  Spielers einschließlich des Systemspielers: Twilight Flame 486 × 469 × 9 = 2.051.406 Byte, 1,2 ms je Aufruf allein
  (0,6 ns je Byte), im Lauf 2,0 ms je Auffrischung = 8,2 % der Tickzeit; `MD_GLA05` 4.509.973 Byte, 2,8 ms je Aufruf,
  6,1 %. Die Kosten wachsen mit Kartenfläche × Spielerzahl, nicht mit der Objektzahl. Größer als der Hash ist das
  Aufdecken davor (`Vision`, 11,6 bis 22,9 %), das ebenfalls zuerst jede Zelle jedes Spielers anfasst.
- **(b) zum Teil bestätigt.** Rund drei Viertel aller Wörter, die eine Objektbeschreibung in den Hash gibt, sind
  Textzeichen (Twilight Flame 138.755 von 184.273; überwiegend Modulname und Modul-Tag, kaum der Vorlagenname). In Zeit
  sind es je nach Lauf 10 bis 50 % der Beschreibung (unter Last schlecht reproduzierbar), also grob 2,5 bis 8,4 % der
  Tickzeit. Der größere Hebel der Hash-Phase ist ein anderer: **je Tick werden 70 bis 89 % aller Objekte als berührt
  markiert** und neu beschrieben (Twilight Flame 173,7 von im Mittel 195; `MD_GLA05` 1.737 von 2.249), weil jedes
  Modul-Update sein Objekt markiert – auch `StealthUpdate` mit 0,05 µs Arbeit.
- **(c) bestätigt, aber heute ohne Wirkung auf den Tick.** `ScriptEngine.StateHash()` beschreibt den ganzen
  Skriptzustand bei jedem Aufruf neu (kein inkrementeller Pfad, 13 bis 21 KB Allokation je Aufruf): `MD_USA05` 17.344
  Wörter, 146 µs; `MD_GLA05` 28.605 Wörter, 187 µs (über den Lauf zwischen 0,4 und 19 ms unter Last). `MissionSession.Step`
  ruft `MissionSession.StateHash()` nicht auf; ein Lockstep-Abgleich je Tick würde 1 bis 2 % der mittleren Tickzeit
  kosten.

### 7.5 Nebenbefund: inkrementeller Hash ungleich vollem Hash in `MD_GLA05` (D8)

Das Werkzeug vergleicht am Ende jedes Laufs den inkrementell geführten Hash der Welt mit einer vollen Neuberechnung.
In `MD_GLA05` nach 3.000 Ticks weichen sie ab (inkrementell `406F4DD61EC777F3`, voll `0EB5EC8A435DBDD8`); nach 300
Ticks und in den anderen Läufen sind sie gleich. Das ist ein Fall von Befund D8 aus `docs/HANDOFF-PHASE18.md`: eine
Zustandsänderung ohne `MarkDirty`. Folge: der dokumentierte Endhash `317B464E8BA98377` dieses Laufs ist der Hash eines
veralteten Zwischenstands, und ein Speicherstand an dieser Stelle stellt eine Welt mit einem anderen Hash wieder her.
Der Lauf mit und ohne Beobachter ist davon gleich betroffen (derselbe Endhash); gesucht und behoben ist die Ursache
hier nicht.

## 8. Grenzen

- Gemessen wird ein Thread; `GC.GetAllocatedBytesForCurrentThread` zählt nur dessen Allokationen. Die Simulation
  benutzt keine weiteren Threads.
- GC-Pausen stecken in den Tickzeiten des Ticks, in den sie fallen (das ist für E20 gewollt), sind aber nicht einzeln
  ausgewiesen; ein Ausreißer im 99-%-Quantil mit unauffälliger Phasenaufteilung ist ein Hinweis darauf.
- Die ersten Ticks zahlen den JIT; deshalb gibt es das Mittel und den längsten Tick zusätzlich ohne die ersten 300.
- Die Wiedergabe misst die ganze Schleife von `ReplayRunner.Run` (Befehle einreihen und `Step`), nicht einzelne Ticks.
- Die Phase `Vision` enthält Löschen und Aufdecken zusammen; eine feinere Teilung bräuchte einen weiteren Haken.
- Ein Skirmish-Lauf mit `--no-observer` hat keine Phasenaufteilung und keinen KI-Anteil.
- Das Werkzeug misst den Kern, nicht den Client: Darstellung, Ton und Eingabe sind nicht enthalten.

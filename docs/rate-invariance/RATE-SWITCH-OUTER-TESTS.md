# Tickraten-Schalter der äußeren Testprogramme

Stand: 8. Oktober 2026. Die Abschnitte 1 bis 6 sind auf dem Basis-Commit `0740ebf` gemessen (plus die Commits jenes Pakets, die nur Testcode ändern)
und beschreiben den Schalter und den damaligen Stand. **Der aktuelle Stand vor der Umstellung der Standardrate auf 60 Hz steht in Abschnitt 7**
(Ausgang `4d9d9e5`): dort sind alle Testprogramme bei 30, 60 und 120 Hz neu gemessen, jeder Fehlschlag ist eingeordnet, und die Golden-Dateien sind
einzeln bewertet. Wo Abschnitt 7 den Abschnitten 2 bis 6 widerspricht, gilt Abschnitt 7.

`tests/CnCNew.Game.Tests`, `tests/CnCNew.Presentation.Tests` und `tests/CnCNew.Ui.Tests` haben jetzt denselben Schalter wie die Simulationstests
(`docs/TICKRATE-MIGRATION.md` §14.9.2): die Standard-Testwelten, -kataloge und `GameData`-Ladevorgänge lassen sich mit 60 oder 120 Hz bauen. Der Lauf
ist ein **Messfühler für Phase 23**, kein Abnahmetest: er sagt, welche Erwartungen brechen, wenn `GameData.DefaultSessionRate` auf 60 Hz gestellt wird.
Er sagt nicht, dass sich irgendetwas bei 60 Hz richtig verhält, aussieht oder anhört – viele Tests prüfen Schranken, die auch ein anderes Verhalten
erfüllt, und nichts davon hat ein Mensch gesehen.

Geändert ist nur Testcode. Nichts unter `src/` oder `tools/`, keine Datei unter `baselines/` und `tests/**/baselines`.

## 1. Aufruf

```powershell
dotnet run --project tests/CnCNew.Ui.Tests -- --tick-rate 60 [--content-dir .local/content]
dotnet run --project tests/CnCNew.Presentation.Tests -- --tick-rate 60 [--content-dir .local/content]
dotnet build -c Release tests/CnCNew.Game.Tests
dotnet run -c Release --no-build --project tests/CnCNew.Game.Tests -- --content-dir .local/content --tick-rate 60 [--only <Suite>[,<Suite>]]
$env:CNCNEW_TEST_TICK_RATE = '60'   # dasselbe über die Umgebung; die Option hat Vorrang
```

Erlaubt sind 30, 60 und 120; alles andere bricht mit einer Meldung ab. Ohne die Option ändert sich nichts: 30 Hz, keine zusätzliche Ausgabezeile.
Mit ihr meldet das Programm in der ersten Zeile die Rate („Default test worlds run at 60 Hz (--tick-rate/CNCNEW_TEST_TICK_RATE): a probe, the reference
is 30 Hz.“).

Laufzeit: `Game.Tests` (Release, mit Inhalt) braucht bei 60 Hz ungefähr das Doppelte und bei 120 Hz ungefähr das Vierfache der 30-Hz-Zeit, weil die
Tests in Spielzeit laufen (auf dem belasteten Rechner dieser Messung bei 120 Hz über eine Stunde; die Zeiten sind nicht einzeln festgehalten). Wer mehrere Raten
nebeneinander startet, kopiert `bin/Release/net10.0` je Lauf in ein Geschwisterverzeichnis **gleicher Tiefe** (`bin/<name>/net10.0`): die HUD-Basislinie
wird relativ zum Programmverzeichnis gesucht, und ein Lauf aus einer anderen Tiefe schreibt eine neue Datei, statt zu vergleichen.

## 2. Wie der Schalter gebaut ist

- `tests/Shared/TestRateOption.cs`: liest und prüft Option und Umgebungsvariable (aus dem Prozess selbst, weil statische Fixtures vor der ersten Anweisung
  des Programms entstehen können), `Announce()` schreibt die erste Zeile. Die Datei kennt die Simulation nicht, damit `Ui.Tests` sie einbinden kann.
- `tests/Shared/TestTime.cs`, Klasse `TestRate`: `Default` (die `SimulationRate`), `Time` (Zeitdienst für Fixtures ohne Welt), `IsOriginal`, `TicksPerFrame`,
  `Frames(n)` (Originalbilder in Ticks der Standardrate, wo keine Welt zur Hand ist). Die Helfer für Erwartungen sind die vorhandenen
  `TestTime.Ticks(world, sekunden)`, `Frames(world, n)`, `TickSeconds(world)`, `FrameSeconds`.
- `Presentation.Tests`: 36 Stellen (`SyntheticFixture`, `WorldConfiguration`, `TemplateCatalog.Build`, der Replay-Kopf der Eingabetests, die FX- und
  Tonhinweis-Fixtures ohne Welt) nehmen `TestRate.Default` bzw. `TestRate.Time`.
- `Game.Tests`: 14 `GameData.Load(…, rate: TestRate.Default)`, 5 Fixtures/Kataloge, 3 Tonhinweis-Filter.
- `Ui.Tests` baut keine Welt und keinen Katalog (das Projekt referenziert die Simulation nicht; Fenster, Übergänge und Texte laufen in Originalbildern
  Wandzeit). Die Option wird angenommen, geprüft und gemeldet und wählt dort nichts aus.

**Was seine Rate weiter selbst nennt:**

| Test | Rate | Grund |
|---|---|---|
| `TimeScaleTests` (`Game.Tests`), `ObjectTests.TimeScale.cs`, `FxTests.TimeScale.cs`, `UnitConditionTests.TimeScale.cs`, je ein Test in `JetOrderTests`, `CaptureFlashTests`, `AudioDirectorTests` | 30, 60 und 120 Hz | Zeitmaß-Tests: sie vergleichen die Raten miteinander |
| `CampaignTests` „Savegame: the header carries the tick rate…“ | 30 und 60 Hz | Ablehnung einer fremden Rate |
| `ShellTests` „Menu shell walks every screen and draws cleanly“ (54 Hashes in `baselines/screens.txt`) | bis `c29338a` 30 Hz über `GoldenRate.Data`, seitdem die Rate der Suite | die Hashes sind bei 30, 60 und 120 Hz dieselben (§7.6) |
| `HudTests` „Golden draw list hash“ (`baselines/hud-drawlist-1280x720.sha256`) | wie oben | wie oben |

`GoldenRate` (`tests/CnCNew.Game.Tests/GoldenRate.cs`) lud bei einer anderen Rate einmal zusätzlich 30-Hz-Daten; die Datei ist mit `c29338a` entfallen.
Nicht festgelegt, weil der gehashte Inhalt von keiner Welt abhängt: der Zeichenlisten-Vergleich `DrawListGolden` in `Ui.Tests`, der Golden-Hash der
Knochentabelle (`DeterminismTests` D1), die Dreieckszahl in `RoadRealDataTests`.

**Beobachtung vor dem Festlegen** (derselbe Stand, ohne `GoldenRate`): der HUD-Hash ist bei 60 und 120 Hz derselbe wie bei 30 Hz. Die 54 Bildschirm-Hashes
sind es ebenfalls, sobald das kurze Gefecht hinter dem Punktebildschirm zwanzig Sekunden Spielzeit dauert statt 600 Ticks (mit 600 Ticks wich nur
`ScoreScreen@…` ab, bei 60 und 120 Hz mit demselben Hash). Die Festlegung auf 30 Hz ist also Vorsicht, kein gemessener Bedarf.

## 3. Zahlen je Programm und Rate

Gezählt sind die Testzeilen (`PASS`/`FAIL` bzw. `ok`/`FAIL`). „vorher“ ist der Stand mit Schalter, aber ohne umgestellte Erwartungen; „nachher“ der Endstand.

| Programm | Rate | vorher bestanden / fehlgeschlagen | nachher bestanden / fehlgeschlagen |
|---|---|---|---|
| `Ui.Tests` (fünf Suiten, ohne Einzelzeilen) | 30, 60, 120 Hz, ohne und mit `--content-dir` | alle bestanden | alle bestanden |
| `Presentation.Tests` ohne `--content-dir` | 30 Hz | 145 / 0 | 145 / 0 |
| | 60 Hz | 136 / 9 | 143 / 2 |
| | 120 Hz | 131 / 14 | 142 / 3 |
| `Presentation.Tests` mit `--content-dir` | 30 Hz | 162 / 0 | 162 / 0 |
| | 60 Hz | 152 / 10 | 160 / 2 |
| | 120 Hz | 144 / 18 | 159 / 3 |
| `Game.Tests` Release mit `--content-dir` | 30 Hz | 137 / 0 | 137 / 0 |
| | 60 Hz | 122 / 15 | 136 / 1 |
| | 120 Hz | 120 / 17 | 136 / 1 |

Zu den `Game.Tests`-Zahlen: „vorher“ ist ohne `GoldenRate` gemessen (mit ihm entfällt der Punktebildschirm-Hash aus beiden Listen: 123 / 14 und 121 / 16,
abgeleitet, nicht gemessen). „nachher“ bei 60 und 120 Hz ist ein voller Lauf mit einem noch offenen Fehlschlag in `MissionEffectsTests` (135 / 2), dazu
der Lauf `--only MissionEffectsTests` nach dessen Umstellung (13 / 0 bei beiden Raten); ein voller Lauf des Endstands ist nur bei 30 Hz wiederholt.
Ohne `--content-dir` überspringt `Game.Tests` die meisten Suiten; diese Läufe sind nicht eigens gezählt.

## 4. Umgestellte Erwartungen

Regel wie in `docs/TICKRATE-MIGRATION.md` §14.5: eine Zahl, die Spielzeit meint, geht über einen Helfer und ist bei 30 Hz dieselbe Zahl; eine Zahl, die
wirklich Ticks oder Schritte meint, bleibt und trägt einen kurzen Grund.

| Datei | Was umgestellt ist | Was bewusst in Ticks bleibt |
|---|---|---|
| `Presentation.Tests/ObjectTests.cs` | Zustandsfolge nach einem Schuss (`FIRING_A` ein Originalbild, Nachladen); Turm: Schritt je Tick aus der Rate, Vierteldrehung, Halten, Zurückdrehen in Bildern | „der Schritt des Schusses“ ist ein Schritt |
| `Presentation.Tests/FxTests.cs` | Sperrfenster der Schadenseffekte (`ThrottleTime` 1000 ms + 1 Schritt) | Tick 100 als Startwert |
| `Presentation.Tests/InputTests.cs` | Bauzeit des Kraftwerks (2 s), Budgets von Verfolgung und Angriffsmarsch, Rate im Replay-Kopf aus der Welt | Sequenznummern, „ein Schritt später“ |
| `Presentation.Tests/UnitConditionTests.cs` | Auslaufzeit der Feuerstufen (1000 ms), alle Laufbudgets ab 30 Schritten, Fenster von `TURRET_ROTATE` | Einschwing-Schleifen von 5 Schritten |
| `Presentation.Tests/StructureConditionTests.cs` | Türzeiten der Waffenfabrik (98/90/120/45 Bilder), Abtastung der Partikelkanone und des Hackers in Bildern | Schleifen von 3 und 6 Schritten |
| `Presentation.Tests/CaptureFlashTests.cs` | Ereignisticks des Neustart-Tests sind Ticks zum Originalbild n | `TintAt`/`Prune` nehmen Originalbilder (keine Ticks) |
| `Presentation.Tests/JetOrderTests.cs` | „Auftrag endet mit der letzten Rakete“ innerhalb von 3 Bildern | – |
| `Game.Tests/MissionTextTests.cs` | ein Schritt der Datei ist ein Originalbild (alle Zählungen sind Bildzahlen der Originaltexte); Meldungszeile mit `FrameClock` gestempelt | zwei Schritte „der Tick, in dem das Skript lief“ (`StepTick`) |
| `Game.Tests/MissionEffectsTests.cs` | Bildfilter je Originalbild abgetastet (`StepFrame`); Kamerafahrt bei eingefrorener Zeit in Bildern | „Welt stand still“: höchstens zwei Ticks |
| `Game.Tests/MissionHudTests.cs` | Dauer der Eröffnungssequenz (20 bis 40 s) | – |
| `Game.Tests/HudTests.cs` | Sperrzeit der Unter-Beschuss-Zeile (Ereignisse 200/400/700 Bilder später) | – |
| `Game.Tests/AudioDirectorTests.cs` | Ticks der Scheinwelt im Eva-Test sind Originalbilder | – |
| `Game.Tests/ShellTests.cs`, `ShellTransitionTests.cs` | Gefecht hinter dem Punktebildschirm: 20 s Spielzeit | – |
| `Game.Tests/ModelGeometryTests.cs`, `DeterminismTests.cs` (D4) | erwarteter Welt-Fingerabdruck enthält die Rate (`Rate.Combine`), wie `GameData.Load` ihn bildet | Laufbudgets der Determinismus-Läufe (900 und 45.000 Ticks: Schritte, nicht Zeit) |

Nicht umgestellt sind Budgets, die bei allen drei Raten ausreichten (außer in `UnitConditionTests.cs`, dort sind alle umgestellt). Sie können nach einer
Änderung der Simulation zu knapp werden; das zeigt sich dann als Fehlschlag bei 60 oder 120 Hz, nicht bei 30 Hz.

## 5. Verbleibende Fehlschläge (Stand `0740ebf`; am Stand des Abschnitts 7 besteht jeder dieser vier Tests bei allen drei Raten)

Klassen: **Erwartung** (der Test nennt eine Tickzahl, die Spielzeit meint – wäre umzustellen), **bewusst 30 Hz** (nennt seine Rate selbst, §2; schlägt nicht
fehl), **echte Ratenabhängigkeit** (der Code unter `src/` verhält sich in Spielzeit anders – nicht behoben und nicht verdeckt).

| Test | Programm, Rate | Klasse | Werte 30 / 60 / 120 Hz | Vermutete Ursache |
|---|---|---|---|---|
| `CaptureFlash` „every flash tints the building in the capturer's colour, decays to none and restarts; nothing else is tinted“ | `Presentation.Tests`, 60 und 120 Hz | echte Ratenabhängigkeit (Simulation, bekannt: `docs/TICKRATE-MIGRATION.md` §5.1, §14.4) | Blitze je Vorbereitung von 2000 ms: 4 / 9 / 19; erster Blitz bis Ende der letzten Tönung 1,0333 / 1,4167 / 1,6583 s. Meldung bei 60 Hz: „a preparation of 60 ticks flashes four times (70,86,98,108,117,125,132,139,146)“ | `src/CnCNew.Simulation/Modules/BuiltIn/SpecialAbilityStaging.cs:369` (`StepCaptureFlash`, Aufruf Zeile 359): die Blitzphase wird je Tick fortgeschrieben |
| `CaptureFlash` „without selection flash settings no tint is projected“ | wie oben | dieselbe | dieselben | dieselbe; der Test bricht an derselben ersten Prüfung |
| `Turrets are the simulation's: they turn toward the victim of the fire order at their turn rate, the shot waits, they hold and recentre` (`ObjectTests`) | `Presentation.Tests`, nur 120 Hz | echte Ratenabhängigkeit (Simulation, klein) | Feuerbefehle, die auf den Turm warten: 15 / 30 / 59 (0,5000 / 0,5000 / 0,4917 s); beim Schuss dreht der Turm bei 30 und 60 Hz nicht mehr, bei 120 Hz noch (`TURRET_ROTATE` gesetzt). Meldung: „… (shot at tick 61, rotating True, 59 orders waited)“ | `src/CnCNew.Simulation/World/SimulationWorld.Turrets.cs:319` (`IsAligned`, Schwelle `TurretAlignThreshold` 0,035 rad ≈ 2°, Zeile 19) gegen `TurnAngle` (Zeile 565): der Turm rastet erst ein, wenn der Rest kleiner als ein Schritt ist. Bei 180°/s ist der Schritt 6° / 3° / 1,5°; erst bei 120 Hz liegt er unter der Schwelle, und der Schuss fällt einen Tick vor dem Einrasten |
| `MissionTextTests` „Named timers and counters stack upward from their position, right-aligned in the right half, ready font and flash at 0:00“ | `Game.Tests`, 60 und 120 Hz | echte Ratenabhängigkeit (Skript-VM und Anzeige; zu entscheiden) | ein mit `SET_COUNTER` auf 3000 gesetzter Zähler, als Countdown angezeigt: „1:40“ / „0:50“ / „0:25“ | `src/CnCNew.Simulation/Scripting/ScriptEngine.Actions.cs:54` (`SET_COUNTER` speichert den Rohwert; nur `SET_TIMER` rechnet Bilder in Ticks um, Zeile 63 ff.) zusammen mit `src/CnCNew.Game/Shell/MissionPresentation.cs:586` (`CounterForDisplay` liest einen als Countdown angezeigten Wert als Ticks der Welt). Ob eine ausgelieferte Karte einen Countdown so setzt, ist nicht geprüft; echte Timer (`SET_TIMER`, `DISPLAY_COUNTDOWN_TIMER`) sind bei allen Raten gleich (`TimeScaleTests`) |

Folgen für die Lesart:

- Die beiden `CaptureFlash`-Tests prüfen hinter der Blitzzahl noch die Hüllkurve je Tick-Alter (`[1, 1, 0,75, 0,5, 0,25, 0, 0]`). Dieser Teil ist für 30-Hz-Schritte
  geschrieben und wird bei 60 und 120 Hz nicht erreicht; er ist dort **nicht geprüft** und wäre nach der Reparatur der Blitzfolge auf Originalbilder umzustellen
  (die Hüllkurve selbst ist bei allen Raten durch den Zeitmaß-Test derselben Datei belegt).
- Der Zähler-Test prüft hinter dem Text noch Positionen und Blinkbilder (16 und 31, schon auf `FrameClock` umgestellt); auch dieser Teil wird bei 60 und 120 Hz
  nicht erreicht.
- Ein Fehlschlag der Klasse „Erwartung“ ist am Endstand nicht mehr offen.

## 6. Nicht gemacht, nicht belegt

- `Game.Tests` ohne `--content-dir` und der Debug-Build sind bei 60 und 120 Hz nicht gelaufen.
- Die Zahlen sind auf einem Rechner und unter Last gemessen; Laufzeiten sind keine Leistungsangaben.
- `--determinism-full` (`DeterminismTests`) ist bei keiner Rate gelaufen.
- Die Golden-Tests sind auf 30 Hz festgelegt; dass sie auch bei 60 und 120 Hz dieselben Hashes liefern, ist einmal beobachtet (§2), aber nicht mehr Teil der Suite.
- Kein Bild, kein Ton und kein Spielablauf bei 60 oder 120 Hz ist von einem Menschen geprüft.

## 7. Vor der Umstellung auf 60 Hz: alle Testprogramme bei 30, 60 und 120 Hz

Stand 8. Oktober 2026, Ausgang `4d9d9e5` (die Simulation ist dort nach der Raten-Äquivalenz-Suite bei 30, 60 und 120 Hz dasselbe Spiel), Endstand der Zweig dieses
Pakets. Die Standardrate ist **nicht** umgestellt (`GameData.DefaultSessionRate` bleibt 30 Hz; die Umstellung ist ein eigener Commit des Projektinhabers). Gemessen ist mit
`--tick-rate 30|60|120`, Release, Realdaten `.local/content`. „Bild“ heißt Logikbild des Originals (1/30 s), „Tick“ ein Schritt der Simulation.
**Nichts davon hat ein Mensch gesehen oder gehört.**

Klassen: **T** – die Erwartung oder das Schrittmaß des Tests unterstellt „ein Tick ist ein Bild“; **P** – Code der Präsentation, der Spielschicht oder eines Werkzeugs unterstellt
es; **S** – die Simulation selbst unterscheidet sich in Spielzeit zwischen den Raten.

### 7.1 Zahlen je Programm und Rate

Gezählt sind Testzeilen (bestanden / fehlgeschlagen). „vorher“ ist `4d9d9e5` (bzw. `502e75b` für die Zeilen, die nur dort gemessen sind), „nachher“ der Endstand.

| Programm | Rate | vorher | nachher |
|---|---|---|---|
| `Simulation.Tests` ohne `--content-dir` | 30 / 60 / 120 Hz | alle grün (508 am Endstand gezählt) | 508 / 0 bei jeder Rate |
| `Simulation.Tests` mit `--content-dir` | 30 Hz | alle grün | 537 / 0 |
| | 60 Hz | 2 fehlgeschlagen (Crusader-Turm, Original-KI) | **537 / 0** |
| | 120 Hz | 2 fehlgeschlagen (dieselben) | **536 / 1** (Referenzmatch ohne Sieger, §7.4) |
| `Presentation.Tests` ohne `--content-dir` | 30 / 60 / 120 Hz | nicht eigens gemessen | 155 / 0 bei jeder Rate |
| `Presentation.Tests` mit `--content-dir` | 30 Hz | 172 / 0 | 172 / 0 |
| | 60 Hz | 167 / 5 | **172 / 0** |
| | 120 Hz | 156 / 16 | **172 / 0** |
| `Game.Tests` Release mit `--content-dir` | 30 Hz | alle grün | 162 / 0 |
| | 60 Hz | 3 fehlgeschlagen | **162 / 0** |
| | 120 Hz | 5 fehlgeschlagen | **162 / 0** |
| `Ui.Tests` (fünf Suiten, ohne Einzelzeilen) | 30 / 60 / 120 Hz, ohne und mit `--content-dir` | alle bestanden | alle bestanden |

Die Raten-Äquivalenz-Suite (zehn Gruppen in `Simulation.Tests --content-dir`, je Lauf bei 30, 60 und 120 Hz, gleich welche `--tick-rate`) meldet am Endstand in jeder Gruppe
**0 Größen außerhalb der Toleranz bei 60 Hz und 0 bei 120 Hz** (`locomotion` 837, `flight` 74, `weapons` 1.474, `projectiles` 743, `turrets` 216, `overtime` 418, `economy` 162,
`timed` 383, `scripts` 15, `ai` 66 Größen); keine Toleranz, kein Fall und kein 30-Hz-Wert ist berührt. `Simulation.Tests` mit Realdaten enthält jetzt einen Test mehr (§7.3).
Der in §5 genannte Zähler-Test („Named timers and counters stack upward …“) besteht bei allen drei Raten, ohne dass dieses Paket ihn angefasst hat.

### 7.2 Jeder Fehlschlag, eingeordnet

| Test (Programm) | Rate | Klasse | Ursache, Werte 30 / 60 / 120 Hz | Korrektur | Commit |
|---|---|---|---|---|---|
| „Real data: a Crusader keeps its turret on a Battlemaster it chases …“ (`Simulation.Tests`) | 60, 120 | T | die Schleife maß den Zielfehler nach jedem Tick; zwischen zwei Bildern hat der Rumpf erst einen Anteil seiner Drehung zurückgelegt, der Turm zielt auf das Bildende: 3,00° gegen die Schranke 2,6° | eine Runde der Schleife ist ein Bild: Befehle am Bildanfang, Ablesen am Bildende. Jetzt 0,00° / 0,00° / 0,00°, 7 Schüsse, 45 gewartete Befehle bei jeder Rate | `e965b6b` |
| „Real data: the original skirmish AI with the shipped script library on Alpine Assault“ (`Simulation.Tests`) | 60, 120 | T (dahinter S, §7.3) | `MaxTicks = 4_500` und „gespeichert bei Tick 3.000“ waren Ticks: 150 / 75 / 37,5 s Spielzeit, in denen die Basis nicht steht (4 statt mindestens 5 Gebäude) | Budget 4.500 Bilder, gespeichert nach 3.000 Bildern | `c204be1` |
| „Condition derivation from simulation state: No new work …“ (`Presentation.Tests`) | 60, 120 | T | „kein neuer Fortschritt“ war als Arbeitsstand 1 geschrieben; der erste Arbeitsschritt zählt über 30 Hz ein ganzes Bild (1 / 2 / 4 Ticks Arbeit) | verglichen wird mit dem Arbeitsstand der Baustelle | `a1691c1` |
| „Turrets are the simulation's: they turn toward the victim …“ (`Presentation.Tests`) | 60, 120 | T | `TurretWaits` zählt seit dem Paket TURRETS Bilder des Wartens (15 / 15 / 15), erwartet waren Ticks (15 / 30 / 60); bei 120 Hz begann der Feuerbefehl vor dem ersten Bild des neuen Objekts (ein neuer Turm tut bis dahin nichts: Winkel nach dem ersten Tick 0 rad) | Erwartung 15; die Befehle beginnen mit dem ersten Bild des Schützen (`TestFrames.StepToFrameOf`) | `a1691c1` |
| „UnitConditions.TURRET_ROTATE while a turret turns toward its aim“ (`Presentation.Tests`) | 120 | T | wie oben: Befehl vor dem ersten Bild des Schützen | wie oben | `a1691c1` |
| „UnitConditions.FREEFALL of a sliding avalanche rock“ (`Presentation.Tests`) | 60, 120 | T | der Test verglich `TensileFormationUpdate.Life` (zählt Updates, eines je Tick) mit 100; die Präsentation vergleicht richtig mit 100 Bildern in Ticks (100 / 200 / 400) | Erwartung in Bildern | `a1691c1` |
| „CaptureFlash.every flash tints the building …“ (`Presentation.Tests`) | 60, 120 | T | die Hüllkurve `[1, 1, 0,75, 0,5, 0,25, 0, 0]` wurde je Tick nach dem Blitz abgelesen; sie zählt Bilder. Die Blitzzahl selbst ist seit dem Paket TURRETS bei jeder Rate 4 (vorher 4 / 9 / 19, §5) | Ablesen im Tick n Bilder nach dem Blitz; der Zeitmaß-Test derselben Datei verlangt jetzt gleich viele Blitze bei allen drei Raten | `a1691c1` |
| „JetOrderTests.A gun fighter on an attack order makes pass after pass …“ (`Presentation.Tests`) | 60, 120 | T | das Mindesttempo wurde ab dem Tick nach dem Erscheinen gelesen; ein neues Objekt beginnt sein erstes Bild ein Bild nach dem Erscheinen und hat bis dahin Tempo 0 | gelesen wird ab dem ersten Bild des Jägers | `a1691c1` |
| sieben „Input: …“-Tests und zwei `CursorTests` (`Presentation.Tests`) | 120 | T | „zwei Ticks nach dem Erzeugen“ meinte „erschienen und gesehen“; der Schleier wird nach dem letzten Tick seines Bildes neu berechnet: erste Auffrischung in Tick 0 / 1 / 3. Dazu die Befehlsticks 2, 3, 4 des Abnahmetests als feste Zahlen | `Arena.StepFrames(2)` bzw. `(3)` (Bilder); die Befehlsticks zählen ab dem Start der Sequenz | `a1691c1` |
| „UnitConditions.real data: combat bike topples, leans and the car is crushed“ (`Presentation.Tests`) | 120 | T | der Fahrer entsteht ein Bild nach dem Rad und steigt ein Bild später auf; fünf Ticks reichten bei 120 Hz nicht („Sequence contains no matching element“) | fünf Bilder | `a1691c1` |
| „MissionPresentationTests.Selecting a named unit reaches NAMED_SELECTED; a UI interaction reaches FLAG“ (`Game.Tests`) | 60, 120 | T | die VM antwortet in ihrem nächsten Update, und das liegt auf einer Bildgrenze: ein `session.Step()` nach dem Signal enthält bei 30 Hz ein Update, bei 60 und 120 Hz nicht unbedingt | ein Bild an Schritten nach jedem Signal | `2adef6b` |
| „MissionEffectsTests.MAP_SWITCH_BORDER confines the camera …“ (`Game.Tests`) | 60, 120 | T | dasselbe | `StepOnce` → `StepFrame` | `2adef6b` |
| „MissionEffectsTests.Sound outputs reach the audio control …“ und „… Object, HUD and flag outputs reach their consumers …“ (`Game.Tests`) | 120 | T | dasselbe: das Signal fiel auf Tick 2, das nächste Update ist Tick 4; das Skript lief im gezählten Fenster nicht (daher der fehlende Schlüssel `NAMED_FLASH_WHITE`) | `StepOnce` → `StepFrame` | `2adef6b` |
| „MissionTextTests.Letterbox bars … only the top bar fades out“ (`Game.Tests`) | 60, 120 | T | `CAMERA_LETTERBOX_END` lief 0 / 1 / 3 Ticks nach dem einen gezählten Tick, die Ausblendung begann entsprechend später: Stufe nach 15 Bildern 0,5 / 0,51667 / 0,525 | Tick für Tick bis zum Ende der Letterbox (höchstens ein Bild), dann 15 Bilder; 0,5 bei jeder Rate | `2adef6b` |
| `scenario-audit` tank-duel „crusader's turret waited until tick 444 to turn back (due at 443)“ (`PresentationTool`) | 120 | T | die Schranke der Prüfung ließ außer Acht, wo der Tod in seinem Bild liegt; in Spielzeit sind die Raten gleich (Tod Bild 382, erste Bewegung 444, Ruhe 458) | Schranke 3 / 2 / 2 Bilder statt 3 / 2 / 1 | `04bba38` |
| `scripts/check-dozer-build.ps1` „mission-intro-clicks“ | 60 | T | das Skript suchte „input locked for 7?? ticks“; der Client meldet 750 / 1.500 Ticks, dieselben 25 s | gelesen wird die Rate aus der Clientzeile, Schranke 700 bis 799 Bilder | `0b5bdd2` |
| `scripts/check-spectator.ps1` (schwach, nicht rot) | 60 | T | verlangte `ticks >= 3000`, bei 60 Hz nur 50 s | verlangt `gameSeconds >= 100` | `8a97c34` |

**Kein Fehlschlag der Klasse P.** Geprüft und ohne Befund: der Händedruck des Missions-Directors mit der VM – `ScriptEngine.SelectNamed` bleibt bis `ClearSelection` gesetzt,
`SignalUiInteraction` wird erst am Ende eines Updates gelöscht, das die Skripte wirklich ausgeführt hat, `CompletePresentation` wirkt sofort, und
`MissionPresentationDirector.AfterStep` leert die Ausgaben in jedem Tick: ein Signal zwischen zwei Bildgrenzen geht nicht verloren, eine Ausgabe auf einer Grenze erreicht ihren
Verbraucher bei jeder Rate. Unter `src/CnCNew.Presentation`, `src/CnCNew.Game` und im Client ist nichts geändert; die 54 Bildschirm-Hashes, der HUD-Hash und alle Tests bei 30 Hz
sind unverändert. Nicht behobene Befunde in der Präsentation: §7.7.

### 7.3 Original-KI: dasselbe Gefecht bei 30, 60 und 120 Hz (Klasse S, fünf Ursachen)

**Die Sitzung selbst (`ScriptedSkirmishSession`) rechnet richtig:** Kaufpunkt und Siegprüfung liegen auf Vielfachen von Bildern in Ticks, die VM und der C++-Teil
(`ScriptEngine.AiPlayer.cs`) denken auf Bildgrenzen, Fristen gehen über den Zeitdienst. Einziger Ratenfehler am Aufruf war das Tick-Budget des Tests (§7.2). Dahinter lag, dass ein
Gefecht der Original-KI bei 60 und 120 Hz **nicht dasselbe** war: China (schwer) gegen GLA (mittel) auf Alpine Assault, Seed 5, wich ab Bild 89 ab. Gesucht ist mit einem neuen
Diagnosemodus (`SimTool rate-compare <content> --original-diff A,B --fields`, `26c7483`: alle beschriebenen Felder aller Objekte nach jedem Bild, Regel wie
`docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md` §5). Jede Ursache ist ein Leser oder eine Konstante der Simulation, je ein Commit; bei 30 Hz ändert keiner etwas.

| # | Erste Abweichung vorher | Befund, Werte 30 / 60 / 120 Hz | Korrektur (Datei, Symbol, Regel aus `RATE-INVARIANCE-CONSOLIDATION.md` §1.3) | danach gleich bis | Commit |
|---|---|---|---|---|---|
| 1 | Bild 89 (2,97 s) | ein Produzent ohne Ausgang (hier ein fahrender GLA-Arbeiter, bei dem die KI einen Arbeiter bestellt) setzte die Einheit an seine augenblickliche Lage: x 1135,8813 / 1136,1882 (120 Hz nicht gelesen) | `Phase5Modules.cs`, `ProductionUpdate.OwnPlace`: `Entity.FrameEnd` / `FrameEndAngle` (Regel 3) | Bild 4937 | `52f3c96` |
| 2 | Bild 4938 (164,6 s) | `SlavedUpdate.OnEnslave` weckte „im nächsten Tick“: `framesToWait` der drei Soldaten einer Stinger-Stellung am Bildende 6 / 10 / 18 (in Phase wären 12 und 24 Ticks) | `SlaveModules.cs`, `OnEnslave`: erstes Update ein Bild nach dem Unterstellen (Regel 7) | Bild 6729 | `a8ee485` |
| 3 | Bild 6730 (224,3 s) | Anfangstempo einer Rakete je Bild als Tickwert × Ticks je Bild: 200 Einheiten je Sekunde sind roh 111848107 / 111848106 / 111848108; Tempo einer Stinger-Rakete im Folgebild roh 145402539 / 145402538 / 145402540, Lage ab da 2 bis 3 Rohwerte daneben | `ProjectileModules.cs`, `MissileFrames` (Tabelle neben `MissileData` mit dem Wert des 30-Hz-Katalogs, Muster `LocomotorFrames`), `MissileAIUpdate.Launch` | Bild 6909 | `c692e0a` |
| 4 | Bild 6910 (230,3 s) | `DockUpdateBase.Update` übergab das Dock in jedem Tick, die Dock-Automaten der Andockenden fragen auf Bildgrenzen: Arbeiter 48 betritt das Versorgungslager in Bild 6911 / 6910 / 6910 (`nextActionTick` 6916 / 13830 / 27660, das ist Bild 6916 / 6915 / 6915) | `DockModules.cs`, `DockUpdateBase.Update`: Übergabe auf dem Bildraster der Welt, wie die Automaten (Ausweg „Originalraster“ für einen Zustandsautomaten; das Freigeben verwaister Plätze bleibt je Tick) | Bild 10131 | `4776231` |
| 5 | Bild 10132 (337,7 s) | ein kurvender Jäger veröffentlichte den Rest seines Wegs, nicht den seiner Drehung: zwei Napalm-Raketen einer MiG starten mit Kurs 2,55564 / 2,52074 / 2,50328 rad | `FrameGrid.Flight.cs` `FlightFrameStep.TurnsTo`, `Phase5Modules.cs` `WingedStepTurnsTo`, `JetAIUpdate.FlyOn`: `Entity.SetAheadAngle` (Regel 3) | Bild 10531 | `be8f6f5` |

**Ergebnis:** China gegen GLA ist bei 30 gegen 60 Hz und bei 30 gegen 120 Hz über **10.531 Bilder (351 s)** in jedem verglichenen Feld jedes Objekts gleich (vorher 88 Bilder).

**Erste verbleibende Abweichung, Bild 10532 (351,07 s), nicht behoben:** in diesem Bild verlässt ein Battlemaster (583) die Fabrik, und ein Panzerjäger (551) nimmt seinen Marsch
wieder auf. Bei 30 Hz bekommt 551 sein neues Ziel im selben Tick vor dem Erscheinen von 583, bei 60 und 120 Hz einen Tick nach ihm; die beiden Zielplätze fallen dadurch
vertauscht anders: 551 (1369,16 | 1871,13) gegen (1393,67 | 1866,23), 583 (1315,91 | 1898,74) gegen (1344,64 | 1876,03), bei 60 und 120 Hz gleich. Das ist die Wahl der
Zielplätze (`FindFreeGoal`) bzw. der Tick, in dem eine Einheit nach einem Gefecht weitermarschiert – Gebiet des parallel laufenden Pakets (Zielplätze in
`AIUpdateInterface.Move`); die Ursache ist hier **nicht bis zum Symbol verfolgt**.

**Neuer Test** (`c204be1`): „Real data: the original skirmish AI plays the same match at 30, 60 and 120 Hz“ (`Tests.OriginalSkirmishAi.cs`) schrittet dasselbe Gefecht 7.200 Bilder
(vier Minuten) bei den drei Raten nebeneinander und vergleicht nach jedem Bild, wer existiert, wem es gehört, Lage, Kurs, Gesundheit, Geld und offene Bauaufträge. Eine Abweichung
zählt, wenn sie drei Bilder anhält (die Regel des Werkzeugs; vier Bildenden wichen kürzer ab). **Geschosse im Flug sind ausgenommen:** mit den Katalogen der Tests (ohne
Modellgeometrie) steht der Strahl eines Toxinlasters in den drei Bildern seines Lebens 2 bis 5 Rohwerte (3 · 10⁻⁷ Einheiten) neben dem 30-Hz-Strahl (Bild 4030, x roh
14011849797 gegen 14011849799); mit den Katalogen des Werkzeugs (mit Modellgeometrie) tritt das nicht auf. Nicht verfolgt. Ohne die fünf Korrekturen scheitert der Test in Bild 89.
In die Gruppe `ai` der Suite ist **kein** Original-KI-Fall aufgenommen: die Suite baut ihre Szenen nur aus dem Definitionsgraphen auf einer flachen Karte, die Original-KI braucht
eine ausgelieferte Karte und die Skriptbibliothek; der Drei-Raten-Test trägt die Aussage.

**Beobachtung bei 30 Hz, nicht angefasst:** die Original-KI bestellt ihren ersten Arbeiter bei einem **Arbeiter** (Objekt 48, `ProductionCompleted 48 … GLAInfantryWorker` in
Bild 89), den zweiten beim Kommandozentrum. `QueueFirstOfKind` fragt jedes eigene Objekt mit `ProductionUpdate`, und `CanQueueProductionTemplate` lässt es zu. Ob das gewollt ist,
ist eine Frage an das 30-Hz-Verhalten der KI und nicht an die Rate.

**Prüfläufe der fünf Commits:** für jeden einzelnen Commit `SimTool` gebaut, `input-manifest check` identisch und `reference-manifest check --set quick --jobs 3` 9 Läufe identisch;
am Stand `be8f6f5` `--set full --jobs 4` **50 Läufe identisch**. Suite danach: §7.1. Simulationstests ohne Realdaten bei 30, 60 und 120 Hz grün (508).

### 7.4 Was rot bleibt

| Test | Rate | Klasse | Werte | Stand |
|---|---|---|---|---|
| „Real data: retail reference match, determinism and snapshot continuation“ (`Simulation.Tests --content-dir`): „The retail match ends with a winner“ | nur 120 Hz | S (chaotische Folge einer bekannten, offenen Ratenabhängigkeit), mit T-Anteil (Tick-Budget) | Sieger FactionGLA bei Tick 14.130 (7,85 min) / 30.930 (8,59 min) / **kein Sieger in 216.000 Ticks** (30 min Spielzeit; 13.926 Befehle, 21.732 Schüsse) | nicht behoben |

Einordnung, mit dem, was gemessen ist:

- Das Referenzmatch ist **schon am Ausgangsstand nicht dasselbe Spiel** bei den drei Raten (`SimTool reference-match --tick-rate N`, Kataloge mit Modellgeometrie, Stand `4d9d9e5`):
  Sieger FactionGLA bei Tick 17.805 (9,9 min) / 33.450 (9,3 min) / 56.760 (7,9 min). Der neue Diagnosemodus `rate-compare --reference-diff --fields` (`f6e529c`) zeigt die erste
  Abweichung in **Bild 0**: die Start-Chinooks steigen ab dem Tick nach ihrem Erscheinen, also einen Anteil vor dem Bild der Welt (z am Bildende 10 / 12,5). Das ist die in
  `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §6 offen gelassene Entscheidung „erstes Bild für alle Module“ (Integratoren beginnen mit dem Tick nach dem Entstehen).
- Das Match ist metastabil (`docs/FIXED-POINT-Q39-24.md`: von 36 Treiber-Seeds enden vier bis fünf bei 30 Hz ohne Sieger). Jede Änderung oberhalb von 30 Hz kann sein Ende
  verschieben. Am Ausgangsstand endete es bei 120 Hz mit Sieger; nach der ersten Korrektur dieses Pakets (`52f3c96`) findet es mit beiden Katalogen keinen mehr (Werkzeug: nach
  55 Minuten Laufzeit abgebrochen, Budget nicht erreicht). Bei 60 Hz endet es vor wie nach den Korrekturen mit demselben Sieger (33.450 → 30.930 Ticks).
- Stichprobe über vier weitere Treiber-Seeds bei 120 Hz mit einem Budget von 54.000 Ticks (7,5 min; zu kurz für ein Urteil): Sieger in 2 von 4 am Ausgangsstand, 1 von 4 am
  Endstand. **Ob die Häufung zufällig ist, ist nicht entschieden.**
- T-Anteil: `ReferenceMatchOptions.MaxTicks` ist ein Tick-Budget (216.000 Ticks sind 120 / 60 / 30 Minuten Spielzeit). Auf Spielzeit umgestellt liefe der Test bei 120 Hz über
  864.000 Ticks; das ist nicht versucht.

Bei 30 und 60 Hz – der heutigen und der künftigen Standardrate – ist kein Test rot.

### 7.5 Werkzeuge und Skripte bei 60 Hz

| Was | 30 Hz | 60 Hz | 120 Hz | Bemerkung |
|---|---|---|---|---|
| `tools/CnCNew.UiRenderTool` | – | 164 von 164 Dateien bytegleich zu 30 Hz | 164 von 164 | hatte keine `--tick-rate` und lud die Standardrate; Option ergänzt (`da60580`) |
| `PresentationTool scenario-audit`, `scripts/audit-scenarios.ps1` (3 Szenen) | bestanden | bestanden | bestanden (vor `04bba38`: 1 Fehlschlag, §7.2) | am Endstand mit den Korrekturen der Simulation wiederholt |
| `scripts/audit-scenarios.ps1 -Scenario exits` (9 Szenen) | bestanden | bestanden | bestanden | am Endstand wiederholt |
| `PresentationTool sync-perf --hud --digest` | läuft | läuft | läuft | der Digest hasht jeden Tick und ist je Rate ein anderer (§7.6) |
| `scripts/check-spectator.ps1` | bestanden | bestanden | Exitcode 0 | hatte `-TickRate`; Schranke in Spielzeit (`8a97c34`) |
| `scripts/check-dozer-build.ps1` (5 Läufe) | bestanden | bestanden (vor `0b5bdd2`: 1 Fehlschlag) | bestanden | `-TickRate` ergänzt (0 = Standard des Clients) |
| `scripts/check-build-hotkeys.ps1` (3 Fraktionen) | bestanden | bestanden | Exitcode 0 | `-TickRate` ergänzt |

Die drei Client-Skripte sind am Stand `4d9d9e5` plus den Werkzeug-Commits gelaufen, **nicht** noch einmal nach den fünf Korrekturen der Simulation. Weitere Skripte, die den Client
oder ein Werkzeug mit Welt starten: `capture-world.ps1`, `capture-scenarios.ps1`, `demo.ps1`, `demo-factory-exits.ps1`, `demo-spectator.ps1` haben `-TickRate`; die übrigen
`demo-*.ps1` reichen `@args` an `demo.ps1` weiter; `capture-text.ps1` hat es jetzt (`0b5bdd2`). **`scripts/perf-profile.ps1` hat `-TickRate` mit dem eigenen Standard 30** und folgt
der Umstellung nicht von selbst (mit Absicht nicht geändert: Leistungsvergleiche brauchen eine genannte Rate). `perf-compare.ps1` und `audit-vulnerable.ps1` bauen keine Welt.

### 7.6 Golden-Dateien und Prüfsummen: hängt der Inhalt an der Rate?

Eingecheckte Golden-Werte sind nur die ersten sechs Zeilen; alles Weitere sind Prüfsummen, die in Dokumenttabellen oder temporären Manifesten stehen.

| Golden | Wo, wer prüft | gemessen bei 60 / 120 Hz | Empfehlung |
|---|---|---|---|
| 54 Bildschirm-Hashes | `tests/CnCNew.Game.Tests/baselines/screens.txt`, `ShellTests` | gleich / gleich | **ratenunabhängig, Festlegung entfernt** (`c29338a`); nichts zu tun |
| HUD-Zeichenliste | `tests/CnCNew.Game.Tests/baselines/hud-drawlist-1280x720.sha256`, `HudTests` | gleich / gleich | **ratenunabhängig, Festlegung entfernt** (`c29338a`, `GoldenRate.cs` gelöscht); nichts zu tun |
| `DrawListGolden` | im Quelltext von `tests/CnCNew.Ui.Tests/WndTests.cs` | gleich / gleich | ratenunabhängig (keine Welt); nichts zu tun |
| Knochentabelle (D1) | Konstante in `tests/CnCNew.Game.Tests/DeterminismTests.cs` | gleich / gleich | ratenunabhängig; nichts zu tun |
| Dreieckszahl der Straßen (206.956) | `Presentation.Tests/RoadRealDataTests.cs` | gleich / gleich | keine Welt; nichts zu tun |
| Fingerabdruck-Kodierung, `GoldenRng` | `Data.Tests`, `Audio.Tests` | nicht gelaufen; weder Welt noch Rate beteiligt | nichts zu tun |
| Ausgabe des `UiRenderTool` (164 Dateien) | Dokumenttabellen, `capture-text.ps1` | 164 von 164 / 164 von 164 | ratenunabhängig; nichts zu tun |
| Manifest von `capture-text.ps1` | temporäres Manifest | 166 von 166 pixelgleich / nicht gemessen | ratenunabhängig |
| Ton-Capture (WAV) tank-duel, 300 Bilder | Dokumenttabellen | gleich bei 30 / 60 / 120 (`B9C6F66CF73D4BF5`) | für diese Szene ratenunabhängig; die drei anderen WAV sind nicht gemessen |
| Gesamtprüfsummen von `scenario-audit` (3 Szenen) | nur Dokumenttabellen | alle drei verschieden | **nach der Umstellung bei 60 Hz neu erzeugen**: eine Kopfzeile mehr (die Rate), in capture-building beginnt der Ranger ein halbes Bild früher (`[MOVING]` in Bild 12 / 11 / 11), in tank-duel weichen mitten im Schwenk abgelesene Turmwinkel um 1 bis 2° ab |
| `sync-perf --digest` | nur Dokumenttabellen | verschieden nach Bauart (hasht jeden Tick) | bei 60 Hz neu erzeugen |
| `fx-audit` | nur Dokumenttabellen | nennt `SimulationRate.Original` selbst | bleibt bei 30 Hz, mit diesem Grund |
| `object-audit`, `terrain-audit` | nur Dokumenttabellen | einmal gelaufen, nicht zwischen Raten verglichen; kein Gebrauch einer Rate im Code gefunden | nichts zu tun |
| Manifest von `capture-world.ps1` (19 Szenen, „vorläufig“) | temporäres Manifest | 9 von 19 pixelgleich bei 60 Hz | bei 60 Hz neu erzeugen – vorher den Straßenbefund entscheiden (§7.7) |
| Filmstreifen von `capture-scenarios.ps1` (77 Bilder) | nur Dokumenttabellen | 13 von 14, 0 von 29, 0 von 34 gleich bei 60 Hz | bei 60 Hz neu erzeugen |

Die Captures sind am Stand vor den fünf Korrekturen der Simulation verglichen und nur 30 gegen 60 Hz.

### 7.7 Befunde, die nicht behoben sind

| Befund | Klasse | Werte, Beleg | Warum offen |
|---|---|---|---|
| Zeichenreihenfolge der Straßen im Client hängt an der Rate | P | `src/CnCNew.Client.Stride/Layers/TerrainLayer.cs`, `CreateRoads`: alle Straßen-Meshes haben dieselbe Hülle, die Reihenfolge gleicher Sortierschlüssel ist nicht festgelegt. Standbild jet-strafe in Bild 60: 1.262 Pixel am Straßenrand verschieden zwischen 30 und 60 Hz, 60 und 120 Hz gleich, mit `CNCNEW_ROADS=0` alle drei gleich. Trägt den ganzen Unterschied von `roads-junctions`, `roads-crossing`, `roads-overview` | eigene Sortierschlüssel je Batch machen die Straßen ratengleich, aber keine Richtung gibt jedes 30-Hz-Bild wieder (eine ändert `roads-overview` bei 30 Hz um 12.810 Pixel, die andere trifft das alte 60-Hz-Bild). Das bräche „30 Hz unverändert“; welche Stapelung richtig ist, braucht einen Blick auf das Original. Der Flicken liegt im Scratch-Verzeichnis der Sitzung (`road-sort-order.patch`), nicht im Repo |
| Halbes Bild Versatz in gestellten Szenen | S, nicht bis zum Symbol verfolgt | capture-building: `[MOVING]` des Rangers in Bild 12 / 11 / 11, x bei t = 30: 11,0 / 11,3; `match-waypoints`: 7.155 Pixel (Gierwinkel eines Dozers); die Eva-Zeile der Jet-Szene beginnt bei 30 Hz an einem anderen Abtastwert | Muster: ein an einer Bildgrenze gegebener Befehl wirkt einen Tick später, bei 60 Hz also ein halbes Bild früher. Kandidaten: `SimulationWorld.Submit` und der eine `World.Step()` in `LiveScenario.Populate` |
| Bilder, die bei 60 Hz abweichen, ohne Einordnung | – | tank-duel: alle 29 Filmstreifenbilder (Bild 10 schwach über den ganzen Schirm, größter Kanalunterschied 13); `match-start` 1 Pixel; `mission-start` 161 Pixel | nicht untersucht |
| Zielplätze nach dem Verlassen der Fabrik / Wiederaufnahme des Marschs | S | §7.3, Bild 10532 | Gebiet des parallelen Pakets |
| Strahlgeschoss des Toxinlasters | S | §7.3, 2 bis 5 Rohwerte über drei Bilder, nur ohne Modellgeometrie | nicht verfolgt |
| Referenzmatch nicht ratengleich; bei 120 Hz ohne Sieger | S | §7.4 | offene Entscheidung „erstes Bild für alle Module“ |

### 7.8 Nach der Umstellung zu tun

1. Die Prüfsummen von `scenario-audit`, `sync-perf --digest`, `capture-world.ps1` und `capture-scenarios.ps1` bei 60 Hz neu erzeugen und die Dokumenttabellen ersetzen (§7.6);
   `fx-audit` bleibt bei 30 Hz.
2. `scripts/perf-profile.ps1`: den Standard `-TickRate 30` bewusst entscheiden.
3. `baselines/reference-manifest.json` ist bei 30 Hz definiert und von der Standardrate der Spielschicht nicht berührt, solange `SimTool` seine Rate nennt; das ist hier nicht
   geprüft, weil kein Standard umgestellt wurde.
4. Die Kommentare in `tests/Shared/TestTime.cs` („golden hashes … name their rate themselves“) stimmen seit `c29338a` nicht mehr ganz und sind nicht angepasst.

### 7.9 Nicht gemacht, nicht belegt

- `Game.Tests` ohne `--content-dir`, der Debug-Build von `Game.Tests` und `--determinism-full` sind bei 60 und 120 Hz nicht gelaufen; `Presentation.Tests` und `Ui.Tests` nur als
  Release-Build über die kopierten Binärverzeichnisse.
- Die drei Client-Skripte (§7.5) und alle Captures sind nach den Korrekturen der Simulation nicht wiederholt; Captures sind nicht bei 120 Hz verglichen.
- Die Original-KI ist nur in einer Paarung auf einer Karte mit einem Seed verglichen (China schwer gegen GLA mittel, Alpine Assault, Seed 5), 14.400 Bilder; ab Bild 10532 weicht
  sie ab. Andere Paarungen, Karten, Seeds und ein Mensch im Slot sind nicht gemessen.
- Für die fünf Korrekturen der Simulation gibt es außer dem Drei-Raten-Test des Gefechts keinen eigenen Test je Ursache.
- Leistung ist nicht gemessen (die Übergabe des Docks und der eine Vergleich je Produktion liegen nicht im heißen Pfad; die Rate der KI-Erhebung aus
  `docs/SKIRMISH-AI-ORIGINAL.md` §7 ist nicht neu erhoben).
- Alle Läufe liefen nebeneinander auf einem belasteten Rechner; Laufzeiten sind keine Leistungsangaben.
- Kein Bild, kein Ton und kein Spielablauf bei 60 oder 120 Hz ist von einem Menschen geprüft. Gleiche Prüfsummen belegen Ratenunabhängigkeit, nicht Richtigkeit.

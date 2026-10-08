# Tickraten-Schalter der äußeren Testprogramme

Stand: 8. Oktober 2026, gemessen auf dem Basis-Commit `0740ebf` (plus die Commits dieses Pakets, die nur Testcode ändern). Andere Pakete ändern zur
selben Zeit die Simulation (Invarianz gegenüber der Tickrate, Phase 23); die Zahlen bei 60 und 120 Hz beschreiben **diesen** Stand und sind nach
jedem dieser Pakete neu zu messen.

`tests/CnCNew.Game.Tests`, `tests/CnCNew.Presentation.Tests` und `tests/CnCNew.Ui.Tests` haben jetzt denselben Schalter wie die Simulationstests
(`docs/TICKRATE-MIGRATION.md` §14.9.2): die Standard-Testwelten, -kataloge und `GameData`-Ladevorgänge lassen sich mit 60 oder 120 Hz bauen. Der Lauf
ist ein **Messfühler für Phase 23**, kein Abnahmetest: er sagt, welche Erwartungen brechen, wenn `GameData.DefaultSessionRate` auf 60 Hz gestellt wird.
Er sagt nicht, dass sich irgendetwas bei 60 Hz richtig verhält, aussieht oder anhört – viele Tests prüfen Schranken, die auch ein anderes Verhalten
erfüllt, und nichts davon hat ein Mensch gesehen.

Geändert ist nur Testcode. Nichts unter `src/` oder `tools/`, keine Datei unter `baselines/` und `tests/**/baselines`.

## 1. Aufruf

```powershell
dotnet run --project tests/CnCNew.Ui.Tests -- --tick-rate 60 [--content-dir .local/phase2-import-a]
dotnet run --project tests/CnCNew.Presentation.Tests -- --tick-rate 60 [--content-dir .local/phase2-import-a]
dotnet build -c Release tests/CnCNew.Game.Tests
dotnet run -c Release --no-build --project tests/CnCNew.Game.Tests -- --content-dir .local/phase2-import-a --tick-rate 60 [--only <Suite>[,<Suite>]]
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
| `ShellTests` „Menu shell walks every screen and draws cleanly“ (54 Hashes in `baselines/screens.txt`) | 30 Hz, über `GoldenRate.Data` | Golden-Hashes sind bei 30 Hz definiert; der Punktebildschirm zeigt ein gespieltes Gefecht |
| `HudTests` „Golden draw list hash“ (`baselines/hud-drawlist-1280x720.sha256`) | 30 Hz, über `GoldenRate.Data` | Golden-Hash des HUD eines laufenden Gefechts |

`GoldenRate.Data` (`tests/CnCNew.Game.Tests/GoldenRate.cs`) lädt bei einer anderen Rate einmal zusätzlich 30-Hz-Daten; bei 30 Hz sind es die Daten der Suite.
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

## 5. Verbleibende Fehlschläge

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

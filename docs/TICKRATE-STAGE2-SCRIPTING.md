# Tickrate, Stufe 2 – Skripte, Mission, Skirmish, Szenarien

Stand: 6. Oktober 2026. Vierter Auftrag aus `docs/TICKRATE-MIGRATION.md` §6: `src/CnCNew.Simulation/Scripting/`, `Mission/`, `Skirmish/` und `Testing/`. Grundlage sind die Tabellen §5.1 (Mission), §5.4, §5.5 und §5.6 von `docs/TICKRATE-AUDIT.md`; Klassen (A bis H) und Widersprüche (W1 bis W10) sind dort definiert, die Schreibweise in `docs/TICKRATE-MIGRATION.md` §1.

**Eiserne Regel eingehalten:** bei 30 Hz ändert sich kein Hash und kein Snapshot-Byte (Nachweis in §7). „Bild“ heißt hier immer Logikbild des Originals (1/30 s), „Tick“ ein Schritt der Simulation.

## 1. Entscheidungen

1. **Regelwerte der Optionsdatensätze sind Bilder des Originals, Laufbudgets bleiben Ticks.** Die Datensätze behalten Form und Feldnamen (Werkzeuge, `CnCNew.Game` und Tests benutzen sie; das Referenzmanifest schreibt die Felder unter ihrem Namen). Der Name `…Ticks` eines Regelwerts ist damit historisch; die Einheit steht im Kommentar des Datensatzes. Umgerechnet wird dort, wo der Wert verbraucht wird – einmal beim Start der Sitzung bzw. des Laufs. Wer heute 300 übergibt, bekommt bei 30 Hz dasselbe Verhalten und bei 60 Hz dieselbe Spielzeit.
2. **Eigene Rhythmen der KI und der Fahrer** (Klasse C): Periode und Versatz in Bildern, beide skaliert. `SkirmishAi` zieht den Versatz der Spieler in Bildern (`Player % 5`) und skaliert ihn wie die Intervalle; bei 30 Hz feuern dieselben Ticks, bei 60 und 120 Hz dieselben Bilder.
3. **Was die Simulation als „Bilder des Originals“ verlässt, wird nicht skaliert.** Die Argumente weitergereichter Skriptaktionen sind die Parameter der Karte als Text (§4).
4. **Der Stresstest zählt Schritte und skaliert nicht** (§5).
5. **Die Auswertung der Skripte in jedem Tick bleibt** und steht auf der Liste für Phase 23 (§6).

## 2. Einheiten der öffentlichen Werte nach dem Umbau

| Wert | Einheit | Wo umgerechnet |
|---|---|---|
| `SkirmishMatchOptions.MaxTicks`, `.CheckpointInterval` | Ticks (Laufbudget, geht so in den Replay-Kopf) | – |
| `SkirmishMatchOptions.GraceTicks`, `.SciencePointInterval` | Bilder des Originals | `SkirmishMatchSession`-Konstruktor (`_graceTicks`, `_sciencePointInterval`) |
| `ReferenceMatchOptions.MaxTicks`, `.MinTicks`, `.CheckpointInterval` | Ticks (Laufbudget; `MinTicks` wird mit `MaxTicks` verglichen und füllt den Lauf auf) | – |
| `ReferenceMatchOptions.GraceTicks`, `.IncomeInterval` | Bilder des Originals | `ReferenceMatchScenario.Record` |
| `StressOptions.Ticks`, `.DrainTicks`, `.CheckpointInterval` | Schritte (Ticks); `DrainTicks` ist ein Anteil des Laufbudgets | – |
| `MissionOptions.MaxTicks` | Ticks (Laufbudget; im Missionszustand unter `maxTicks`) | – |
| `MissionOptions.PresentationLatencyTicks`, `ScriptEngineOptions.PresentationLatencyTicks` | Bilder des Originals; im Missionszustand steht unter `presentationLatencyTicks` der Wert der Option, wie er übergeben wurde | `ScriptEngine.Forward` (`_tick + Time.OriginalFrames(…)`) |
| `MissionOptions.CombatIntervalTicks` | nur ein Schalter (W8), keine Zeit | – |
| `ScriptEngine.EndGameDelayFrames` (vorher `EndGameDelayTicks`) | Bilder des Originals; `ScriptEngine.EndGameDelay` ist derselbe Wert in Ticks der Welt | Eigenschaft `EndGameDelay` |
| `MissionObjectiveDriver.Run(maxTicks, …)` | Ticks (Laufbudget) | – |
| `MissionObjectiveDriver.Run(spacing, settleTicks)` | Bilder des Originals | in `Run` |
| `MissionProbe.Run(settleTicks, waitTicks)` | Bilder des Originals | in `Run` |
| `MissionProbeResult.TicksToOutcome`, `MissionRunReport.Ticks`/`.OutcomeTick`, `ObjectiveDriveResult.OutcomeTick` | Ticks | – |
| `SkirmishAiStats.AttackTicks` | Ticks (zählt je Denkschritt das skalierte Denkintervall) | – |
| `ScriptEngine.CounterValue(name)` eines Timers, `HulkLifetimeOverrideTicks` | Ticks (Zustand der VM; seit Stufe 1) | – |
| `ScriptOutput.Tick` | Tick | – |
| `ScriptOutput.Arguments` | Parameter der Karte, unverändert (§4) | – |

## 3. Umgestellte Stellen

Spalte „Kl.“ ist die Klasse des Inventars. „Ordnung“ heißt: nicht skaliert, mit `TickOrder.Next` geschrieben (W2).

### 3.1 `Skirmish/SkirmishAi.cs` (eigen, keine Herkunft im Original)

| Symbol (vorher → jetzt) | Kl. | War | Ist |
|---|---|---|---|
| `FireInterval` → `FireIntervalFrames`, Feld `_fireInterval` | C | `(_tick + _phase) % 5` | `(_tick + _phase) % _fireInterval`, im Konstruktor `Time.OriginalFrames(5)` |
| `ThinkInterval` → `ThinkIntervalFrames`, Feld `_thinkInterval` | C | `% 15` | `% _thinkInterval` |
| `_phase` | C | `Player % 5` Ticks | `Time.OriginalFrames(Player % FireIntervalFrames)` |
| `SkirmishAiStats.AttackTicks` | B | `+= 15` je Denkschritt | `+= _thinkInterval` (Ticks) |
| `RetryTicks` → `RetryFrames` (Standard von `Backoff`, vier direkte Verwendungen) | B | 300 Ticks | `Backoff` nimmt Bilder und skaliert selbst |
| `MemoryTicks` → `MemoryFrames` | B | 900 | `Time.OriginalFrames(MemoryFrames)` |
| `OrderRefreshTicks` → `OrderRefreshFrames` | B | 150 | ebenso |
| `BoardingTicks` → `BoardingFrames` (2 Stellen) | B | 300 | ebenso |
| `RevisitTicks` → `RevisitFrames` (2 Stellen) | B | 3600 | ebenso |
| `QueueWorker`: `Backoff(…, 60)` | B | 60 | `ShortRetryFrames` |
| `QueueWorker`: `BuildTicks + 30` | B | 30 | `BuildTicks + Time.OriginalFrames(InFlightMarginFrames)` (`BuildTicks` ist schon Ticks) |
| `BuildStructures`: `essential ? 60 : RetryTicks` | B | 60 | `ShortRetryFrames`/`RetryFrames` |
| `FindSupplyPlacement`: `Backoff(…, 150)` | B | 150 | `SupplyLookRetryFrames` |
| `ManageSites`: `_tick - LastResume < 60` | B | 60 | `Time.OriginalFrames(ShortRetryFrames)` |
| `BuyScience`: `_tick + 1800` | B | 1800 | `ScienceRetryFrames` |
| `UsePowers`: `RetryTicks`/3000/900 | B | Ticks | `RetryFrames`/`PowerMissingScienceRetryFrames`/`PowerRefusedRetryFrames`, einmal skaliert |
| `CommandArmy`: `< 60` | B | 60 | `ShortRetryFrames` |
| `UseTunnels`: `Backoff(0x81000000u, 90)` | B | 90 | `TunnelEvacuationRetryFrames` |
| `UseTransports`: `Backoff(…, 60)` | B | 60 | `ShortRetryFrames` |
| `Scout`: `_tick - _scoutAt < 450` | B | 450 | `ScoutReachFrames` |
| `ScoutForWarehouses`: `Backoff(…, 300)` | B | 300 | `WarehouseScoutRetryFrames` |
| `OrderCarriers`: `_tick - since < 90` | B | 90 | `CarrierIdleFrames` |

Summe: C 3, B 19 (wie im Inventar; `UsePowers` zählt dort als eine Zeile). Alle Fristen werden nur an Denkticks gesetzt und geprüft – keine Umrechnung je Tick; `Update` vergleicht zwei vorab berechnete Felder.

### 3.2 `Skirmish/SkirmishMatchSession.cs`, `Testing/SkirmishMatchScenario.cs`

| Symbol | Kl. | War | Ist |
|---|---|---|---|
| `VictoryCheckInterval` → `VictoryCheckIntervalFrames`, Feld `_victoryCheckInterval` | C | `tick % 15` | `tick % _victoryCheckInterval` |
| `SkirmishMatchOptions.SciencePointInterval` | C | `tick % Option` | `tick % _sciencePointInterval` (Option in Bildern) |
| `SkirmishMatchOptions.GraceTicks` | B | `tick + 1 + Option` | `tick + TickOrder.Next + _graceTicks` (Option in Bildern) |
| `+ 1` vor der Gnadenfrist | E, Ordnung | Literal | `TickOrder.Next` |

`SkirmishMatchOptions`: Form unverändert, Einheiten im Kommentar; `MaxTicks`/`CheckpointInterval` bleiben Ticks.

### 3.3 `Testing/ReferenceMatchScenario.cs`

| Symbol | Kl. | War | Ist |
|---|---|---|---|
| `MoveCadence` → `MoveCadenceFrames`, Feld `_moveCadence` (Befehlsrunde und Siegprüfung) | C | `tick % 15` | `tick % _moveCadence` |
| `ProductionCadence` → `ProductionCadenceFrames`, `_productionCadence` | C | `tick % 30` | `tick % _productionCadence` |
| `ReferenceMatchOptions.IncomeInterval`, `_incomeInterval` | C | `tick % Option` | Option in Bildern, einmal in `Record` skaliert |
| `OrderRefreshTicks` → `OrderRefreshFrames`, `_orderRefresh` | H/zeit | 150 Ticks | skaliert |
| `ReferenceMatchOptions.GraceTicks` | H/zeit | `tick + 1 + Option` | `tick + TickOrder.Next + grace` |
| `tick == 1`, `DockCommand(…, 1, …)`, `+ 1` | H/ticks, Ordnung | Literal 1 | `TickOrder.Next` (3 Stellen) |

Unverändert: `MaxTicks`, `MinTicks`, `CheckpointInterval` (Ticks, Budget), `tick == 0` (absolute Adresse des ersten Ticks).

### 3.4 `Testing/StressScenario.cs`

| Symbol | Kl. | War | Ist |
|---|---|---|---|
| Reserve vor dem Ende des Abbaus | H/zeit | `Ticks - tick - 60` | `- world.Time.OriginalFrames(DrainReserveFrames)` |
| Rampe, `% 2`, `% 3`, `% 40`, `% 2 == 1`, `% 97`, `% 500 == 250` | C | Literale | benannte Konstanten `…Steps`, **nicht skaliert** (§5) |

### 3.5 `Scripting/`

| Datei, Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `ScriptEngine.cs`: `EndGameDelayTicks` → `EndGameDelayFrames`, Eigenschaft `EndGameDelay` (3 Verwendungen: `VICTORY`, `DEFEAT`, `LOCALDEFEAT` in `Phase13.cs`) | A | 120 Ticks | `_world.Time.OriginalFrames(120)` | `ScriptEngine.cpp`, `FRAMES_TO_SHOW_WIN_LOSE_MESSAGE = 120`, `ScriptEngine::startEndGameTimer` |
| `ScriptEngine.Actions.cs`: `QUICKVICTORY` `End(…, 1)` | E, Ordnung | Literal 1 | `TickOrder.Next` | `ScriptEngine.cpp`, `ScriptEngine::startQuickEndGameTimer` (`m_endGameTimer = 1`) |
| `ScriptEngine.Actions.cs`: `RetargetInterval` → `RetargetIntervalFrames` | C | `_tick % 30 != Entity % 30` | `_tick % Time.OriginalFrames(30) != Entity % 30 * TicksPerOriginalFrame`; Periode und Faktor einmal vor der Schleife | eigen |
| `ScriptEngine.Actions.cs`: `Forward`, `PresentationLatencyTicks` | B | `_tick + Option` | `_tick + _world.Time.OriginalFrames(Option)` | eigen |
| `ScriptEngine.Phase13.cs`: `UpdateCaptureOrders` | C | `tick % 30 == 0` | `tick % Time.OriginalFrames(CaptureResendFrames) == 0`, einmal vor der Schleife | eigen |

Bereits in Stufe 1 erledigt und hier nachgeprüft (kein weiterer Eingriff): `SET_TIMER`/`SET_RANDOM_TIMER` (Zufall in Bildern, dann skaliert), `SecondsToFrames` (aufrunden auf Bilder, dann skalieren – `SET_MILLISECOND_TIMER`, `SET_RANDOM_MSEC_TIMER`, `ADD_TO_MSEC_TIMER`, `SUB_FROM_MSEC_TIMER`, `SCRIPTING_OVERRIDE_HULK_LIFETIME`), `*_FOR_FRAMECOUNT`, `teamInitialIdleFrames`, erster Auswertungstick `NameHash % 61` Bilder, `DelayEvaluationSeconds`, `NAMED_SET/ADD_SPECIAL_POWER_COUNTDOWN`, Knopfjagd `max(ScanTicks, eine Sekunde)`, `UpdatePendingStructures` (`tick % TicksPerSecond`), `SupplyScanSeconds`, `UpdateReadyTeam` (60 s, `AIPlayer.cpp`, `AIPlayer::checkReadyTeams`), `UNIT_EMPTIED` (`frame - TickOrder.Next`).

### 3.6 `Testing/` – Sonde und Zielfahrer (Test- und Werkzeugcode)

| Symbol | Kl. | War | Ist |
|---|---|---|---|
| `MissionProbe.Run(settleTicks)` | H/zeit | `Tick < settleTicks` | `Tick < Time.OriginalFrames(settleTicks)` |
| `MissionProbe.Run(waitTicks)` | H/zeit | `killTick + waitTicks` | `killTick + Time.OriginalFrames(waitTicks)` |
| `MissionObjectiveDriver.Run(settleTicks)` | H/zeit | Ticks | skaliert |
| `MissionObjectiveDriver.Run(spacing)` | C | `nextAt = Tick + spacing` | `+ Time.OriginalFrames(spacing)` |
| `ObjectiveDriver.Leaf.Cooldown` (zwei Vergleiche in `Round`) | H/zeit | `tick - last < Cooldown` | `tick - last < Time.OriginalFrames(Cooldown)`; damit sind alle 34 Abklingzeiten in `ObjectiveDriver.Leaves.cs` (31), `ObjectiveDriver.cs` (2 weitere Blätter) und `ObjectiveDriver.Commands.cs` (1) Bilder |
| `ObjectiveDriver.PublishObjectives` | H/zeit | `> 150` | `> Time.OriginalFrames(ObjectiveExpiryFrames)` |

### 3.7 Zählung

| Datei | A | B | C | H (Zeit) | Ordnung | Summe |
|---|---|---|---|---|---|---|
| `Skirmish/SkirmishAi.cs` |  | 19 | 3 |  |  | 22 |
| `Skirmish/SkirmishMatchSession.cs` |  | 1 | 2 |  | 1 | 4 |
| `Testing/ReferenceMatchScenario.cs` |  |  | 3 | 2 | 3 | 8 |
| `Testing/StressScenario.cs` |  |  |  | 1 |  | 1 |
| `Scripting/ScriptEngine.cs` | 1 |  |  |  |  | 1 |
| `Scripting/ScriptEngine.Actions.cs` |  | 1 | 1 |  | 1 | 3 |
| `Scripting/ScriptEngine.Phase13.cs` |  |  | 1 |  |  | 1 |
| `Testing/MissionScenario.cs` |  |  |  | 2 |  | 2 |
| `Testing/MissionObjectiveDriver.cs` |  |  | 1 | 1 |  | 2 |
| `Testing/ObjectiveDriver.cs` |  |  |  | 1 |  | 1 |
| `Testing/ObjectiveDriver.Commands.cs` |  |  |  | 1 |  | 1 |
| **Summe** | 1 | 21 | 11 | 8 | 5 | 46 |

Dazu ohne Codeänderung, nur als Einheit dokumentiert: die Felder der vier Optionsdatensätze (§2).

## 4. Was als Skriptausgabe die Simulation verlässt

`ScriptEngine.Forward` reicht 129 Aktionstypen als `ScriptOutput(Tick, Action, Arguments)` an den Host. Nach dem Umbau gilt (und stand vorher nirgends):

- `Arguments` sind die Parameter der Karte als Text, **unverändert und nie mit der Rate skaliert**. Eine Bildzahl darunter (`CAMERA_FADE_*`, `CAMERA_BW_MODE_*`: Bilder für Anstieg, Halten, Abfall; `ScriptEngine.cpp`, `ScriptEngine::setFade`, `m_fadeFramesIncrease/Hold/Decrease`) ist bei jeder Rate eine Zahl von **Bildern des Originals (1/30 s)**; Sekundenwerte sind Sekunden. Der Verbraucher (Phase 21) darf sie nicht als Ticks deuten.
- `ScriptOutput.Tick` ist der Simulationstick der Rate der Welt. Wer daraus Zeit ableitet, rechnet mit `world.Time`.
- `PendingPresentation` läuft nach `PresentationLatencyTicks` **Bildern des Originals** ab. `LiveMission.ClientPresentationLatencyTicks = 60 * OriginalFramesPerSecond` (`CnCNew.Game`) ist damit ohne Änderung bei jeder Rate 60 s.
- `VICTORY`/`DEFEAT`-Ausgaben: der Nachlauf bis `IsEnded` ist 120 Bilder (4 s) bei jeder Rate, bei `QUICKVICTORY` ein Update.
- `CounterValue` eines Timers ist in **Ticks** (Zustand; Stufe 1 skaliert beim Setzen). Eine Anzeige, die den Wert als Bilder liest (`MissionText`, Countdown), zeigt bei 60 Hz das Doppelte – Phase 21 rechnet mit `world.Time.ToOriginalFrames` bzw. `ToWholeSeconds` (siehe auch §6, Zeile „Timerwerte“).

## 5. Stresstest: der Zeitplan zählt Schritte

`StressScenario` ist ein Belastungstest je Simulationsschritt: je Tick ein Kill und ein Ersatz, Funken jeden zweiten, Rauch jeden dritten Tick, Timer je neuem Objekt. Ein Zeitplan in Spielzeit würde die Last je Tick bei 60 Hz halbieren und etwas anderes messen; das Laufbudget (`Ticks`) ist ohnehin eine Schrittzahl. Deshalb bleiben Rampe (100), alle Modulo-Rhythmen, die Timerverzögerungen der Befehle (`SetTimerCommand(…, 30, 90, …)`, `(…, 120, 300, …)`, `(…, 45, 0, …)` – Ticks nach dem Vertrag des Befehls) und `DrainTicks` (Anteil des Budgets; `SimTool` kürzt ihn auf `Ticks / 4`) bei jeder Rate dieselben Zahlen. Die Rhythmen heißen jetzt `…Steps`. Folge bei 60 Hz: gegenüber den Lebensdauern der Fixture (Millisekunden, die skalieren) ist der Umsatz in Spielzeit doppelt so dicht; der Lauf ist bei 60 Hz ein anderer Lauf, kein Vergleichslauf zu 30 Hz. Einziger Spielzeitwert ist die Reserve vor dem Ende des Abbaus (`DrainReserveFrames = 60`): was langsam stirbt, muss vor dem Ende weg sein. Bei 30 Hz ist der Lauf bitgleich (Manifestlauf `stress`). Der Test `TickRateStressScheduleCountsSteps` hält die Entscheidung fest.

## 6. Bewusst unverändert – Ergänzungen zur Liste für Phase 23

Alles hier ist bei 30 Hz richtig und bei 60 Hz anders; es ist Verhalten, keine Parametrisierung.

| Datei | Symbol | Was bei 60 Hz anders ist |
|---|---|---|
| `Scripting/ScriptEngine.cs` | `Update` (W6) | Jedes aktive Skript ohne Verzögerung wird je Tick ausgewertet: Zähler, die ein Skript je Auswertung erhöht, und wiederholte Aktionen laufen doppelt so oft. Naheliegend: nur an Bildgrenzen (`Time.IsOriginalFrameBoundary`). |
| `Scripting/ScriptEngine.cs` | Timerwerte (`Counter.Value`, `IsTimer`) | Seit Stufe 1 in Ticks, je Tick −1. Vergleiche einer Karte gegen Bildliterale (`COUNTER`, `TIMER`-Bedingungen mit Zahl) und Rechnen auf einem Timer (`INCREMENT_COUNTER`, `DECREMENT_COUNTER`, `SET_COUNTER` roh) meinen Bilder – bei 60 Hz passt die Einheit nicht. Löst sich, wenn die VM an Bildgrenzen läuft und Timer wieder Bilder sind (dann die Skalierung der Stufe 1 in `SET_TIMER`, `SecondsToFrames`, `SetSequenceTimer` zurücknehmen); getrennt davon nicht ohne Zustandsänderung lösbar. |
| `Scripting/ScriptEngine.Actions.cs` | `UpdateFollowers` (Teil je Tick), `UpdateTeamFollower` | Ankunft am Wegpunkt, Neuausgabe des Marschbefehls und `FireAtOrdered` laufen je Tick: feinere Abtastung, mehr Befehle je Sekunde. Der gestaffelte Rest ist umgestellt. |
| `Scripting/ScriptEngine.Sequential.cs` | `ProcessSequences`, `SequenceItem.FramesToWait`, `_busyThisTick` | Wartezähler sind skaliert (Stufe 1), aber Leerlauf wird je Tick abgetastet und der Ein-Tick-Versatz zwischen Befehl und Welt ist 1/60 s. |
| `Scripting/ScriptEngine.ExtendedActions.cs` | `ProcessTeamBuilds` | Je offener Bestellung höchstens ein Produktionsbefehl je Tick: doppelt so schnell eingereiht. |
| `Scripting/ScriptEngine.TeamQueues.cs` | `UpdateTeamQueues`, `UpdateBuildingTeam` | Prüfung je Tick; Startaktionen laufen je Tick, solange ein Mitglied untätig ist. |
| `Scripting/ScriptEngine.TeamEvents.cs` | `ProcessTeamEvents`, `ScriptTeam.WasIdle`, `_pendingTeamScripts` | Feindsicht und Verlustschwelle je Tick; „zwei Ticks untätig“ ist halb so lang; Zerstört-Skripte einen Tick später. |
| `Scripting/ScriptEngine.Conditions.cs` | `PLAYER_LOST_OBJECT_TYPE`, `NAMED_/TEAM_ENTERED/EXITED_AREA*` | Flanken gegen die vorige Auswertung: Fenster und Abtastung hängen am Auswertungstakt. |
| `Scripting/ScriptEngine.Phase13.cs` | `UNIT_EMPTIED` (`frame - TickOrder.Next`) | Ordnung, nicht skaliert; setzt Auswertung in **jedem** Tick voraus – läuft die VM nur an Bildgrenzen, muss die Nachbarschaft „voriges Bild“ heißen. |
| `Scripting/ScriptEngine.Phase13.cs` | `UpdateBridgeStates` (Wechsel-Flag ein Update lang), `AttachPendingTraps`, Reichweitenprüfung in `UpdateCaptureOrders` | Fenster bzw. Versatz von einem Tick; Prüfung je Tick. |
| `Scripting/ScriptEngine.Phase13.cs` | `SUPPLY_SOURCE_SAFE` | Kein Zwischenspeicher (Original: 2 s, `ScriptConditions.cpp`, `evaluateSkirmishSupplySourceSafe`); folgt dem Auswertungstakt. |
| `Mission/MissionSession.cs` | `Step` (Reihenfolge), `Engine.TimeFrozen` | Skripte, dann Welt, dann Ereignisse: Skripte reagieren einen Tick später (1/60 s). Im `FREEZE_TIME` zählt nur die Aufrufrate von `Step`: Skripttimer laufen weiter in Ticks der Rate, die Welt steht. |
| `Skirmish/SkirmishMatchSession.cs` | `Step` | `SkirmishAi.Update` wird je Tick aufgerufen (kehrt außerhalb der Bilder sofort zurück); kein Verhalten, nur Aufrufzahl. |
| `Skirmish/SkirmishAi.cs` | `Think` (Fristen am Denkraster), `FindPlacement`/`ProduceArmy` (Zufallszüge je Denkschritt), `PlacementChecksPerThink`, `paths > 12` | Mit skaliertem Raster **und** skalierten Fristen ist die wirksame Dauer und die Zahl der Züge je Spielzeit dieselbe wie bei 30 Hz; die KI sieht aber eine Welt, die sich zwischen zwei Denkschritten feiner bewegt hat (andere Positionen, andere `BuildTicks`-Reste) – Abläufe sind nicht tickgleich zu 30 Hz. |
| `Testing/ObjectiveDriver.Commands.cs` | `Act`, `DriveAssaults` | Je Tick ein `FireWeaponCommand` je Angriffspaar, sobald die Waffe feuern darf (wie `StandingOrders`, W5). |
| `Testing/ObjectiveDriver.Leaves.cs` | `GuardMargin` (150 Welteinheiten) | Rand gegen den Weg einer Einheit je Planungsrunde; mit skalierter Runde unverändert richtig, hängt aber an der Bewegung (Klasse E). |
| `Testing/ObjectiveDriver.cs` | `IdleRoundsBeforeHarm`, `ExhaustiveRoundsBeforeUnion` | Zählen Runden; mit skaliertem `spacing` wieder 20 s bzw. 10 s. Nicht angefasst. |
| `Testing/StressScenario.cs` | ganzer Zeitplan | Zählt Schritte (§5). |

Nebenbefunde (keine Ratenfehler, nicht korrigiert): `SUB_FROM_MSEC_TIMER` zieht `ceil(+x)` ab, das Original addiert `ceil(−x)`; `SCRIPTING_OVERRIDE_HULK_LIFETIME` rundet auf, das Original schneidet ab; `SUPPLY_SOURCE_ATTACKED` prüft 10 s, das Original addiert `SCAN_RATE = 10` auf Bilder (`AIPlayer.cpp`, `AIPlayer::isSupplySourceAttacked`).

## 7. Geprüft, nicht gekoppelt (Resttreffer der Suchmuster aus `TICKRATE-AUDIT.md` §8)

Die Muster P01–P09, P13, N05, N07, N08, N14, N16 wurden über alle Dateien des Auftrags erneut ausgeführt; jeder Treffer ist einer dieser Fälle:

- **Konstanten in Bildern** (`…Frames = N`, P04/P06/N08/N16): die umgestellten Stellen aus §3 und `SupplyScanSeconds`.
- **Skalierte Rhythmen und Fristen** (N05/N07 auf `% _fireInterval`, `tick - x < Time.OriginalFrames(…)` usw.): §3.
- **Ordnung** (`TickOrder.Next`): §3.
- **Laufbudgets und Standardwerte der Optionen** (`108_000`, `216_000`, `1_000`, `300`, `2_700`, `4500`, `150`, `600`, `15`, `60`): §2.
- **Blatt-Abklingzeiten** in `ObjectiveDriver*.cs` (Literale 15 bis 300 als viertes Argument von `Leaf`/`Destroy`): Bilder, in `Round` skaliert.
- **Stresstest** (`…Steps`, Timerverzögerungen, Prozentwerte 30/45, Wertebereiche 20/30): §5 bzw. keine Zeit.
- **Abstände, Radien, Mengen** (`Fixed.FromInt(30/45/90/450/600/900/3600/…)`, `MaxAttackThreshold = 30`, Ringabstände 60 bis 360, `GuardMargin = 150`, `MaxHealth = 300`): Welteinheiten oder Stückzahlen (Anhang A des Inventars).
- **Daten der Fixture** (`MinLifetime`/`MaxLifetime` in Millisekunden, `BuildTime = 1` s): werden beim Katalogbau umgerechnet.
- **Wächter und Zustand** (`FramesToWait = -1`, `_hulkLifetimeOverrideTicks = -1`, `PresentationLatencyTicks = 0`, `CombatIntervalTicks = 3`): kein Zeitwert bzw. Schalter.
- **Zufall** (`LogicRandom.Range` in `SET_RANDOM_TIMER`/`SET_RANDOM_MSEC_TIMER`: in Bildern bzw. Sekunden gezogen, dann skaliert; in `Phase13.cs`: Positionen und Winkel).
- **Kommentare** mit „30 Hz“ oder „1/30 s“.

## 8. Tests

Neu: `tests/CnCNew.Simulation.Tests/Tests.TickRate.Scripting.cs` (sieben Tests, ein Block in `Program.cs`). Jeder misst bei 30, 60 und 120 Hz auf der synthetischen Fixture der jeweiligen Rate:

| Test | Prüft |
|---|---|
| `TickRateScriptEndGameDelay` | `VICTORY` endet nach 120 · f Ticks; `QUICKVICTORY` nach einem Update bei jeder Rate |
| `TickRateScriptPresentationLatency` | Latenz 45 Bilder = 45 · f Ticks; Argumente der Ausgabe unverändert, Option behält ihren Wert |
| `TickRateMissionProbeWaitsInGameTime` | Sonde läuft (10 + 20) · f Ticks |
| `TickRateSkirmishSessionRules` | Siegprüfung auf Vielfachen von 15 · f, Siegtick = f · Siegtick bei 30 Hz, Ende = Sieg + 1 + 60 · f, Wissenschaftspunkte bei 40 · f, 80 · f |
| `TickRateSkirmishAiRhythm` | jeder KI-Befehl liegt auf dem Denkbild seines Spielers (`(Tick / f + Player % 5) % 15 == 0`), erster Befehl = f · erster Befehl bei 30 Hz |
| `TickRateReferenceMatchGeneratorRhythm` | Einkommen bei 300 · f, Produktion `% 30 · f`, Befehle `% 15 · f`, Gnadenfrist |
| `TickRateStressScheduleCountsSteps` | Zeitplan des Stresstests ist in Schritten bei jeder Rate gleich |

Nicht durch einen eigenen Ratentest belegt: die einzelnen Fristen von `SkirmishAi` (nur über den Rhythmus), der Neuziel- und der Übernahmerhythmus der Skripte, die Abklingzeiten des Zielfahrers.

Bestehende Tests: unverändert bis auf **ein Token** in `Tests.Scripting.cs` (`ScriptEngine.EndGameDelayTicks` → `EndGameDelayFrames`, erzwungen durch die Umbenennung der öffentlichen Konstante; derselbe Wert).

## 8a. Nachweise (6. Oktober 2026, Release, Retaildaten)

- `reference-manifest check --set quick` und `input-manifest check`: identisch nach jedem der vier Quelltext-Commits (die drei Zwischenstände aus einem Export des jeweiligen Commits gebaut und geprüft, der letzte im Arbeitsbaum).
- `reference-manifest check --set full`: 50 Läufe identisch (1.241 s, parallel zu anderen Läufen); `input-manifest check`: identisch.
- `tests/CnCNew.Simulation.Tests`: Debug grün; Release mit `--long --content-dir` grün.
- Sauberer Build von `CnCNew.slnx` (`bin`/`obj` von `CnCNew.Client.Stride` gelöscht): 0 Warnungen, 0 Fehler.
- Probelauf bei 60 Hz, **nur „läuft durch“, keine Referenzwerte** (Stand dieses Zweigs allein, ohne die drei anderen Aufträge der Stufe 2): `reference-match --tick-rate 60` Sieger FactionGLA bei Tick 27.690 (7,7 Spielminuten), Endhash `15873B1BB4672711`, zweiter Lauf und Snapshot-Fortsetzung identisch; `mission-run MD_USA05 --ticks 6000 --tick-rate 60` läuft durch, 0 fehlende Bedingungen, Endhash `F5E568B144A5B2A8`, zweiter Lauf und Speichern/Laden identisch.

## 9. Helfer außerhalb des Auftrags, Hinweise für andere

- **Keine** Ergänzung an `SimulationTime` oder anderen Dateien außerhalb der vier Verzeichnisse. Außerhalb berührt: `tests/CnCNew.Simulation.Tests/Program.cs` (Registrierung, ein Block), `Tests.Scripting.cs` (ein Token).
- Für Phase 21 (nicht geändert, weil außerhalb des Auftrags):
  - `tools/CnCNew.SimTool/Program.cs` (`ai-match`, `skirmish-sweep`) und `ReferenceManifest.cs` bilden `GraceTicks = Math.Min(Standard.GraceTicks, maxTicks / 4)` – ein Minimum aus Bildern und Ticks; bei 30 Hz richtig, bei 60 Hz wird die Frist erst bei einem doppelt so langen Budget gekappt (harmlos, aber unsauber).
  - `ClientRuntime` schreibt „presentation latency N ticks“ – es sind Bilder des Originals.
  - Aufrufer von `MissionObjectiveDriver.Run`/`MissionProbe.Run` mit `settleTicks:`/`waitTicks:`/`spacing:` übergeben jetzt Bilder; Laufbudgets (`maxTicks`, `--ticks`) weiter Ticks der gewählten Rate.
  - `SkirmishAiStats.AttackTicks` und `MissionProbeResult.TicksToOutcome` sind Ticks; Ausgaben in Sekunden über `world.Time`.

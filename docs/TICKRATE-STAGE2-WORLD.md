# Tickrate, Stufe 2 – Auftrag WORLD

Stand: 6. Oktober 2026. Ergebnis des Auftrags „World/“ aus `docs/TICKRATE-MIGRATION.md` §6, erweitert auf alles in `src/CnCNew.Simulation`, was nicht `Modules/`, `Scripting/`, `Mission/`, `Skirmish/` oder `Testing/` ist (`World/`, `Templates/`, `Commands/`, `Replay/`, `Snapshots/`, `Terrain/`, `Numerics/`, `Time/`, `Views/`, `State/`, `Events/`, `Ids/`, `Random/`, `Timers/`). Klassen (A bis H) und Widersprüche (W1 bis W10) sind die von `docs/TICKRATE-AUDIT.md`, die Schreibweise ist die Konvention von `docs/TICKRATE-MIGRATION.md` §1.

**Die eiserne Regel gilt:** bei 30 Hz ist jeder umgestellte Ausdruck derselbe wie zuvor (`OriginalFrames(n)` ist `n`, `ToOriginalFrames(t)` ist `t`). Beleg: Referenzmanifest und Eingabemanifest nach jedem Commit (Abschnitt 6).

## 1. Wie die Welt ihre Zeitwerte führt

Die Konstanten der Welt stehen in **Bildern des Originals** (`…Frames`) und werden **einmal im Konstruktor** von `SimulationWorld` in Ticks der Weltrate aufgelöst (`private readonly long _…Ticks`, `VisionInterval`). Kein Schritt der Welt rechnet eine Konstante um; die Felder sind kein Zustand (nicht im Snapshot, nicht im Hash) und keine Katalogfelder (das Eingabemanifest bleibt unberührt). Wo zur Laufzeit gerundet oder gewürfelt wird (W1), rechnet die Welt in Bildern und skaliert das Ergebnis: `Time.OriginalFrames(… Time.ToOriginalFrames(ticks) …)`; diese Stellen laufen je Schuss, je Blick oder je Lieferung, nicht je Tick.

## 2. Umgestellte Stellen

| Nr. | Datei | Symbol | Kl. | Vorher | Jetzt | Quelle |
|---|---|---|---|---|---|---|
| 1 | `World/SimulationWorld.Acquisition.cs` | `OrderedAttackGraceTicks` | B | `const long … = 45` (Ticks) | `OrderedAttackGraceFrames = 45`, aufgelöst in `_orderedAttackGraceTicks` | eigen |
| 2 | `World/SimulationWorld.Acquisition.cs` | `ForceIdleTicks` | B | `const long … = 2` | `ForceIdleFrames = 2`, `_forceIdleTicks` (zwei Verwendungen) | `GameLogic/AI/AI.cpp`, Feldtabelle `ForceIdleMSEC` → `m_forceIdleFramesCount` (`INI::parseDurationUnsignedInt`); benutzt in `AIUpdate.cpp`, `AIUpdateInterface::resetNextMoodCheckTime`. Der Wert 67 ms der ausgelieferten `AIData.ini` ist unter `references` nicht belegt; nach der Rundung des Originals wären 67 ms drei Bilder, nicht zwei (Nebenbefund des Inventars, nicht korrigiert) |
| 3 | `World/SimulationWorld.Acquisition.cs` | `ChaseRefreshTicks` | C | `const long … = 15`, `tick - ChaseTick >= 15` | `ChaseRefreshFrames = 15`, `_chaseRefreshTicks` | eigen |
| 4 | `World/SimulationWorld.Acquisition.cs` | `StepAcquirer` (Marsch fortsetzen) | W2 | `WakeAt(…, tick + 1)` | `tick + TickOrder.Next` | eigen (Reihenfolge) |
| 5 | `World/SimulationWorld.Acquisition.cs` | `Engage` (Verfolgung) | W2 | `WakeAt(…, tick + 1)` | `tick + TickOrder.Next` | eigen (Reihenfolge) |
| 6 | `World/SimulationWorld.Acquisition.cs` | `EarliestReadyTick` | W2 | `tick - OrderedAttackGraceTicks - 1` | `tick - _orderedAttackGraceTicks - TickOrder.Next` | eigen (ein Schritt unter der Frist, damit der Vergleich `<=` sicher endet) |
| 7 | `World/SimulationWorld.Acquisition.cs` | `MoodCheckOffset` | W1 | Versatz in `±(rate >> 1)` Ticks | in Bildern gezogen (`ToOriginalFrames(rate) >> 1`), dann `OriginalFrames(…)` | `AIUpdate.cpp`, `AIUpdateInterface::getNextMoodTarget` (`halfRate = checkRate >> 1`, `GameLogicRandomValue(-halfRate, halfRate)`) |
| 8 | `World/SimulationWorld.Turrets.cs` | `TurretAimGraceTicks` | B | `const long … = 15` | `TurretAimGraceFrames = 15`, `_turretAimGraceTicks` (zwei Verwendungen) | eigen |
| 9 | `World/SimulationWorld.Turrets.cs` | `TurretReloadHoldCapTicks` | B | `const long … = 450` | `TurretReloadHoldCapFrames = 15L * SimulationRate.OriginalFramesPerSecond`, `_turretReloadHoldCapTicks` (auch als Klammergrenze in `EarliestReadyTick`) | eigen |
| 10 | `World/SimulationWorld.Turrets.cs` | `TurretSweepEnableTicks` | A | `const long … = 3` | `TurretSweepEnableFrames = 3`, `_turretSweepEnableTicks` | `GameLogic/AI/TurretAI.cpp`, lokale Konstante `ENABLE_SWEEP_FRAME_COUNT = 3` (`m_enableSweepUntil = now + …`) |
| 11 | `World/SimulationWorld.Turrets.cs` | `TurretLoseInterestTicks` | B | `const long … = 45` | `TurretLoseInterestFrames = 45`, `_turretLoseInterestTicks` | eigen |
| 12 | `World/SimulationWorld.Turrets.cs` | `ScheduleScan` | W1 | `DrawScan(…, MinScanIntervalTicks, MaxScanIntervalTicks)` (Ziehung über Ticks) | Ziehung über `ToOriginalFrames(min)…ToOriginalFrames(max)`, dann `OriginalFrames(…)` | `TurretAI.cpp`, `TurretAIIdleState::resetIdleScan` (`GameLogicRandomValue(getMinIdleScanInterval(), getMaxIdleScanInterval())`) |
| 13 | `World/SimulationWorld.Vision.cs`, `World/SimulationWorld.cs` | `VisionIntervalTicks`, `Step`, `StepObserved` | C | `const int … = 3`, `tick % VisionIntervalTicks == 0` | `VisionIntervalFrames = 3`, Eigenschaft `VisionInterval` (Ticks der Welt), `tick % VisionInterval == 0` (Phase 0 bleibt) | eigen |
| 14 | `World/SimulationWorld.cs` | `Step`, `StepObserved` | W2 | `_tick = tick + 1` (zweimal) | `tick + TickOrder.Next` | eigen (der Tickzähler selbst) |
| 15 | `World/SimulationWorld.Construction.cs` | `SaleDurationTicks` | A | `const long … = 179` | `SaleDurationFrames = 45 + 135`, `_saleDurationTicks = OriginalFrames(180) - TickOrder.Next` (179, 359, 719) | `Common/System/BuildAssistant.cpp`: `FRAMES_TO_ALLOW_SCAFFOLD` (`LOGICFRAMES_PER_SECOND * 1.5f`), `TOTAL_FRAMES_TO_SELL_OBJECT` (`LOGICFRAMES_PER_SECOND * 3.0f`), `BuildAssistant::update` (je Bild `100 / TOTAL_FRAMES_TO_SELL_OBJECT` Prozent bis −50 %: 135 Bilder) |
| 16 | `World/SimulationWorld.Construction.cs` | dieselbe Stelle, das „− 1“ | W2 | in der 179 enthalten | `- TickOrder.Next` (nicht skaliert) | eigen (Reihenfolge: die Erstattung fällt in den Schritt vor Ablauf) |
| 17 | `World/SimulationWorld.Construction.cs` | `AdvanceConstruction` | W1 | `Math.Max(BuildTicks, 1)` | `Math.Max(BuildTicks, _minimumBuildTicks)` (ein Bild des Originals) | eigen (Untergrenze); `DozerAIUpdate::update` nur als Aufrufer genannt, Herkunft unbelegt |
| 18 | `World/SimulationWorld.CreationLists.cs` | `ExecuteDeliverPayload` | W1 | `LogicRandom.Range(0, DelayDeliveryMaxTicks)` | `OriginalFrames(LogicRandom.Range(0, ToOriginalFrames(DelayDeliveryMaxTicks)))` | `GameLogic/Object/ObjectCreationList.cpp`, `DeliverPayloadNugget` (`GameLogicRandomValue(0, m_delayDeliveryFramesMax)`; `DelayDeliveryMax` über `INI::parseDurationUnsignedInt`) – im Inventar „Herkunft unbelegt“, jetzt belegt |
| 19 | `World/SimulationWorld.CreationLists.cs`, `World/SimulationWorld.cs` | `IsSignificantlyAbove` | A | Dezimalrechnung je Aufruf (seit Stufe 1 mit `OriginalFramesPerSecond²`) | `_significantHeight`, einmal je Welt berechnet (derselbe Ausdruck) | `Common/Thing/Thing.cpp`, `Thing::isSignificantlyAboveTerrain` (`-(3*3) * m_gravity`) |
| 20 | `World/SimulationWorld.Projectiles.cs` | `FlightTicks` | W1 (im Inventar E) | `ceil(Distanz / Tempo je Tick)` Ticks, sofort unter einem Tick Weg; `static` | in Bildern gezählt gegen den Weg eines Bildes (`speed.Raw * TicksPerOriginalFrame`), dann `OriginalFrames(…)`; Instanzmethode | `GameLogic/Object/Weapon.cpp`, `WeaponTemplate::fireWeaponTemplate` (`delayInFrames = v.length() / getWeaponSpeed()`, „< 1.0f“, `REAL_TO_INT_CEIL`) |
| 21 | `Templates/WeaponCatalog.cs`, `World/SimulationWorld.WeaponSlots.cs` | `WeaponInfo.DelayBetweenShotsWith`, `ClipReloadWith` (`ScaledByRateOfFire`), Aufrufer `ConsumeShot` | W1 | `max(1 Tick, floor(Ticks / Multiplikator))` | neue Überladungen mit `SimulationTime`: `OriginalFrames(max(MinimumShotDelayFrames, floor(ToOriginalFrames(ticks) / Multiplikator)))` | `Weapon.cpp`, `WeaponTemplate::getDelayBetweenShots` und `getClipReloadTime` (`REAL_TO_INT_FLOOR(… / bonusROF)`); die Untergrenze ist eigen |
| 22 | `Templates/WeaponCatalog.cs`, `World/SimulationWorld.FiringTracker.cs` | `WeaponInfo.PreAttackDelayWith`, Aufrufer `PreAttackDone` | W1 | `floor(Ticks × Multiplikator)` | `OriginalFrames(floor(ToOriginalFrames(ticks) × Multiplikator))`; nimmt `SimulationTime` | `Weapon.cpp`, `WeaponTemplate::getPreAttackDelay` (Rückgabe `Int` schneidet ab) |
| 23 | `Templates/WeaponCatalog.cs` | `WeaponCatalog.Build`: fehlender bzw. 0-ms-Schussabstand | W1 (im Inventar B/fallback) | `SimulationTiming.SleepNone` bzw. `Math.Max(SleepNone, delay)` (ein Tick) | `timing.OriginalFrames(WeaponInfo.MinimumShotDelayFrames)` (ein Bild des Originals) | eigen (das Original gäbe 0 zurück und feuerte im nächsten Bild) |
| 24 | `Replay/ReplayRunner.cs` | `Run` (Prüfpunkt- und Verifikationsrhythmus) | W2 | `(tick + 1) % options.…` (zweimal) | `(tick + TickOrder.Next) % options.…`; die Intervalle selbst sind Laufbudgets und bleiben Ticks | eigen |

Dazu ohne Umrechnung, aber benannt: `Math.Max(1, tick + …)` an drei Stellen von `SimulationWorld.Turrets.cs` (`StopAiming`, `ScanStep`, `ScheduleScan`) heißt jetzt `Math.Max(TurretDeadlineSet, …)` – die 1 ist der kleinste Wert, der „gesetzt“ bedeutet (0 heißt „keine Frist“), kein Zeitwert und keine Reihenfolge. Die Fundstelle von `TurretInfo.NeverScanTicks` ist nachgetragen (`TurretAI.cpp`, `TurretAIData::TurretAIData`: `m_minIdleScanInterval = m_maxIdleScanInterval = 9999999`).

### Zählung

| Datei | A | B | C | W1 | W2 | Summe |
|---|---|---|---|---|---|---|
| `World/SimulationWorld.Acquisition.cs` |  | 2 | 1 | 1 | 3 | 7 |
| `World/SimulationWorld.Turrets.cs` | 1 | 3 |  | 1 |  | 5 |
| `World/SimulationWorld.Vision.cs` (mit `Step`/`StepObserved`) |  |  | 1 |  |  | 1 |
| `World/SimulationWorld.cs` |  |  |  |  | 2 | 2 |
| `World/SimulationWorld.Construction.cs` | 1 |  |  | 1 | 1 | 3 |
| `World/SimulationWorld.CreationLists.cs` | 1 |  |  | 1 |  | 2 |
| `World/SimulationWorld.Projectiles.cs` |  |  |  | 1 |  | 1 |
| `Templates/WeaponCatalog.cs` (mit den Aufrufern in `WeaponSlots.cs`, `FiringTracker.cs`) |  |  |  | 3 |  | 3 |
| `Replay/ReplayRunner.cs` |  |  |  |  | 2 | 2 |
| **Summe** | **3** | **5** | **2** | **9** | **8** | **27** |

Gezählt je Symbol bzw. je Reihenfolge-Literal (Zeile 14 und 24 der Tabelle sind je zwei Literale). Gegenüber der Tabelle „World/“ in `TICKRATE-MIGRATION.md` §6 (A 4, B 7, C 8): die übrigen A/B/C-Zeilen des Inventars für diese Dateien waren entweder schon mit Stufe 1 erledigt (`PreAttackStaleFrames`, `CooldownFrame = tick + Time.TicksPerSecond`), sind Reihenfolge (W2, oben) oder Ablauf je Tick ohne eigenes Literal (Abschnitt 4).

## 3. Bewusst nicht umgestellt

| Datei | Symbol | Warum |
|---|---|---|
| `Templates/WeaponCatalog.cs` | `WeaponInfo.DelayBetweenShotsWith(global, conditions)`, `ClipReloadWith(global, conditions)` (ohne Zeitdienst) | Bleiben für zwei Aufrufer in `Modules/BuiltIn` (`CombatModules.cs` – Auftrag A–M –, `JetAIUpdate.cs` – Auftrag A–M), die nicht zu diesem Auftrag gehören. Sie runden in Ticks wie zuvor und sind **nur bei 30 Hz** gleich der neuen Form. Nach dem Zusammenführen: beide Aufrufer auf `…With(world.Time, …)` umstellen und die alten Überladungen samt privatem `ScaledByRateOfFire(global, conditions, ticks)` löschen |
| `World/SimulationWorld.Vision.cs` | `VisionIntervalTicks` (`public const int = VisionIntervalFrames`) | Bleibt als Name für die Aufrufer außerhalb der Simulation (`CnCNew.Game/Live/LiveGame.cs`, `SimTool/PerfCommand.cs`, `Presentation.Tests/ObjectTests.cs`, `Simulation.Tests/Tests.Vision.cs`), die bei 30 Hz laufen. Phase 21: dort `world.VisionInterval` benutzen, dann die Konstante entfernen |
| `Templates/TurretInfo.cs` | `NeverScanTicks = 9999999` | Wächterwert („nie“), wird mit `CanScan` verglichen und nie skaliert (wie `SleepForever`) |
| `Templates/WeaponCatalog.cs` | `WeaponInfo.InstantSpeed` | Wächterwert, in Stufe 1 entschieden |
| `Templates/ObjectTemplate.cs` | `TemplateCatalog.GravityPerTickSquared` | Rechnet weiter je Zugriff (eine Dezimaldivision); ein Feld am Katalog änderte das Eingabemanifest. Die Welt selbst liest den Wert nicht mehr im heißen Pfad (`_significantHeight`) |
| `World/SimulationWorld.Acquisition.cs` | `MoodCheckOffset`: `(ulong)(tick + 1)` | Versatz der Hash-Eingabe (kein Nullwert im ersten Tick), weder Zeit noch Reihenfolge; im Kommentar vermerkt |
| `World/SimulationWorld.cs` | `RunUpdates` (`sleep < SleepNone`), `WakeAt` (`_tick + SleepNone`), `Register` (`NextWake = _tick + SleepNone`) | `SimulationTiming.SleepNone` ist der benannte kleinste Schlaf (Reihenfolge, Konvention §1 Fall (e)) |
| `World/SimulationWorld.cs` | `MaxUpdatesPerTick`, `MaxEventsPerTick` | Sturmgrenzen je Schritt, keine Zeit |

## 4. Geprüft, nicht gekoppelt (oder Format)

- `Commands/Commands.cs` (`SimCommand.Tick`, `SetTimerCommand.DelayTicks`/`RepeatTicks`), `Timers/SimTimer.cs`, `SimulationWorld.Execute`/`FireTimers` (`DueTick = _tick + set.DelayTicks`, `tick + timer.RepeatTicks`): Format (G). Die Werte sind Ticks der Rate der Welt; wer den Befehl erzeugt, rechnet in dieser Rate.
- `Events/SimEvent.cs`, `Replay/ReplayFormat.cs`, `Snapshots/WorldSnapshot.cs`, `SimulationWorld.Snapshot.cs`: Format (G), Stufe 1.
- `Replay/ReplayRunner.cs` (`RecordCheckpointInterval`, `VerifyIncrementalEvery`, `StopTick`): Laufbudgets, bleiben Ticks.
- `SimulationWorld.FiringTracker.cs`: `PreAttackStaleFrames` und `CooldownFrame = tick + Time.TicksPerSecond` sind seit Stufe 1 umgestellt; `ContinuousFireCoastTicks` kommt skaliert aus dem Katalog; `StepFiringTrackers` fragt je Tick ab, der Vergleich arbeitet mit absoluten Ticks (nur Last).
- `SimulationWorld.Construction.cs` `AdvanceConstruction`: `ConstructionWork++` je Arbeitstick gegen `BuildTicks`, das der Katalog skaliert – gleiche Bauzeit, feinere Gesundheitsstufen.
- `SimulationWorld.cs`: `SpawnSetup.MinLifetimeTicks`/`MaxLifetimeTicks`, `BaseRegenDelayTicks`, `production.Enqueue(…, BuildTicks)`; `SimulationWorld.BattlePlan.cs` `ParalyzeTroops(…, untilTick)`; `SimulationWorld.StructuresAndWorld.cs` `CreateFromListWithLifetime(…, lifetimeTicks)`; `SimulationWorld.CrateServices.cs` `SetPowerSabotagedUntil`; `TunnelTracker.AddRider(…, tick)`: Katalogwerte bzw. absolute Ticks, die der Aufrufer (Module) liefert und die die Welt nur durchreicht.
- `SimulationWorld.Collision.cs:174` (`Fixed.FromInt(150)`), `SimulationWorld.CreationLists.cs` (`formation % 2`, 90 Grad): Strecken und Geometrie.
- `Terrain/`, `Numerics/`, `Views/`, `State/`, `Ids/`, `Random/`: keine Zeit (die Treffer der Suchmuster sind Hash-Konstanten `z >> 30`, Reihenglieder in `FixedTrig`, Enum-Nummern).
- `Templates/ObjectTemplate.cs`, `SpecialPowerCatalog.cs`, `UpgradeCatalog.cs`, `ObjectCreationListCatalog.cs`, `TurretInfo.cs`: alle Umrechnungen laufen seit Stufe 1 über den Parameter `timing`; kein Literal offen.
- `Time/`: nichts geändert, nichts ergänzt.

## 5. Ergänzungen zur Liste für Phase 23

Bei 30 Hz richtig, bei 60 Hz anders; in Phase 20 unverändert (Klasse E und der Rest von W1).

| Datei | Symbol | Was bei 60 Hz anders ist |
|---|---|---|
| `World/Entity.cs` | `Entity.Speed` | Strecke je Tick; jeder Leser, der den Wert als absolute Größe nimmt, sieht die Hälfte (siehe `TryTopple`) |
| `World/SimulationWorld.Collision.cs` | `AvoidLocalBlockers`, `StepBlocked` | Vorausschau und Ausweichschritt sind ein Tick Weg und halbieren sich |
| `World/SimulationWorld.Collision.cs` | `StationaryBlockersAhead` | Vorausschau ist der vom Aufrufer übergebene Vektor (`Speed × 25` Ticks Weg in `Phase5Modules.cs`) |
| `World/SimulationWorld.Collision.cs` | `ResolveFootprintCollisions`, `TrySquish` | Abtastung je Bewegungsschritt: Überlappung wird früher erkannt, Reihenfolge der Kollisionsereignisse ändert sich |
| `World/SimulationWorld.Collision.cs` | `TryTopple` (`mover.Speed`) | Kippkraft = Strecke je Tick: halbe Kraft |
| `World/SimulationWorld.Acquisition.cs` | `StepAcquisition`, `AutoFire` | Ein Durchlauf und ein Feuerversuch je Tick: Schusszeitpunkte fallen auf das feinere Raster, sobald eine Bedingung (Turm ausgerichtet, Sicht) zwischen zwei Bildern wahr wird |
| `World/SimulationWorld.Acquisition.cs` | `MoodCheckOffset` | Der Versatz liegt jetzt auf dem 30er-Raster, die Ziehung hasht aber die Ticknummer der Welt: die Folge der Werte ist bei jeder Rate eine andere |
| `World/SimulationWorld.Turrets.cs` | `_turretWaits` (auch `AutoFire`, `TryFireActiveWeapon`) | Zählt zurückgehaltene Feuerversuche je Tick; der Wert (Teil des Hashes) verdoppelt sich |
| `World/SimulationWorld.Turrets.cs` | `KeepsAttackIntent` | Sicht ist nur alle drei Bilder aktuell, geprüft wird je Tick; die Frist beginnt am ersten Tick außer Reichweite |
| `World/SimulationWorld.Turrets.cs` | `IsAligned` (0,035 rad) | Fester Winkel gegen halbe Schrittweite: erster Schuss fällt auf einen anderen Zeitpunkt |
| `World/SimulationWorld.Turrets.cs` | `StepTurrets`, `StepTurret` | Ein Dreh- und Fristschritt je Tick; „Halten“ gilt bis einschließlich `HoldUntil` (N + 1 Schritte: der Zusatzschritt ist ein Tick, kein Bild) |
| `World/SimulationWorld.Turrets.cs` | `AimStep`, `RecenterStep`, `ScanStep`, `TurnAngle`, `TurnPitch` | Euler-Schritt mit Einrasten unter einem Tickschritt; Umkehr des Schwenks bei 0,035 rad |
| `World/SimulationWorld.Turrets.cs` | `IdleScanStep`, `ScheduleScan` | Der Abstand liegt jetzt auf dem 30er-Raster; die Drehung danach ist Euler je Tick, und der Zeitpunkt, an dem der Turm zur Ruhe kommt (Start der nächsten Frist), hängt daran |
| `World/SimulationWorld.FiringTracker.cs` | `StepFiringTrackers` (`tick <= CooldownFrame`), `PreAttackDone` (`_tick <= PreFinish + …`), `PreAttackSlotOf` | Zähler mit Periode N + 1: gehandelt wird im Tick nach der Frist, also einen Tick statt ein Bild später |
| `World/SimulationWorld.Projectiles.cs` | `FlightTicks` | Gezählt wird jetzt in Bildern; der Weg eines Bildes ist aber `Tempo je Tick × Ticks je Bild`, und das Tempo je Tick ist bei 60/120 Hz für sich gerundet – an einer Bildgrenze kann die Flugzeit um ein Bild von 30 Hz abweichen |
| `World/SimulationWorld.cs` | `RunUpdates` | Rückgabe unter `SleepNone` wird ein Tick; jedes Modul mit Schlaf 1 läuft doppelt so oft je Sekunde |
| `World/SimulationWorld.cs` | `ProcessSpawns` | Jedes Glied einer Spawn-Kette kostet einen Tick, also die halbe Zeit |
| `World/SimulationWorld.cs` | `Register` (`NextWake = _tick + SleepNone`) | Erstes Update einen Tick nach dem Erscheinen |
| `World/SimulationWorld.cs` | `Step` (Phasenfolge) | Wirkungen zwischen Phasen sind um einen Tick versetzt (Turmwinkel nach Bewegung, Schaden nach Zerstörung, Sicht nach Spawn) |
| `World/SimulationWorld.Construction.cs` | `_saleDurationTicks` | 180 Bilder weniger **ein Tick**: 5,983 s bei 60 Hz statt 5,967 s |
| `Templates/ObjectTemplate.cs` | `LocomotorInfo.CloseEnoughDist` (0 = ein Tick Weg; Verbraucher in `Phase5Modules.cs`) | Ankunftsradius von einem Tick Weg halbiert sich |
| `Templates/WeaponCatalog.cs` | `WeaponInfo.DelayBetweenShotsWith`/`ClipReloadWith` ohne Zeitdienst | Solange die beiden Aufrufer in `Modules/BuiltIn` sie benutzen, runden diese in Ticks (Abschnitt 3) |

## 6. Helfer außerhalb des Auftrags

Keine. `src/CnCNew.Simulation/Time/` ist unverändert; `SimulationTime` und `TickOrder` genügten. Außerhalb von `src/CnCNew.Simulation` ist nur die neue Testdatei `tests/CnCNew.Simulation.Tests/Tests.TickRate.World.cs` dazugekommen, registriert in `Program.cs` (sieben Zeilen in einem Block vor „Replay equality …“).

Schnittstellen, die sich für andere Aufträge geändert haben:

- `WeaponInfo.PreAttackDelayWith` nimmt jetzt `SimulationTime` als ersten Parameter (einziger Aufrufer war `SimulationWorld.FiringTracker.cs`).
- `WeaponInfo.DelayBetweenShotsWith`/`ClipReloadWith` haben **zusätzliche** Überladungen mit `SimulationTime`; die alten bleiben (Abschnitt 3).
- `SimulationWorld.FlightTicks` ist eine Instanzmethode (war `internal static`, einziger Aufrufer in derselben Datei).
- Neu und öffentlich: `SimulationWorld.VisionIntervalFrames`, `SimulationWorld.VisionInterval`, `WeaponInfo.MinimumShotDelayFrames` (Konstanten bzw. eine Eigenschaft der Welt; kein Instanzfeld an einem Katalogtyp).

## 7. Tests

`tests/CnCNew.Simulation.Tests/Tests.TickRate.World.cs`, sieben Tests, jeweils bei 30, 60 und 120 Hz mit der synthetischen Fixture der Rate:

| Test | Belegt |
|---|---|
| `TickRateWorldSaleTakesTheSameGameTime` | Verkauf: 179, 359, 719 Ticks (180 Bilder weniger ein Schritt) |
| `TickRateWorldVisionKeepsItsRhythm` | `VisionInterval` 3, 6, 12; ein Beobachter aus Tick 1 wird mit der Auffrischung von Tick 3, 6, 12 gesehen (Phase 0) |
| `TickRateWorldWeaponDelaysAreRoundedOnTheOriginalGrid` | Für jede Waffe der Fixture unter drei Bonuszuständen: Schussabstand, Nachladezeit und Aufziehzeit sind genau 2× bzw. 4× der 30-Hz-Wert; bei 30 Hz gleich der alten Form; ohne `DelayBetweenShots` ein Bild; 18 Bilder bei 400 % sind 4 Bilder (8 Ticks bei 60 Hz, in Ticks gerundet wären es 9) |
| `TickRateWorldSpinUpKeepsItsShotTimes` | Dasselbe durch die Welt (`ConsumeShot`, Feuerstufen): die Folge der Schussabstände ist die 30-Hz-Folge mal 2 bzw. 4 |
| `TickRateWorldFlightTimeIsCountedInFrames` | 100 Einheiten bei 2 je Bild: 50 Bilder (50, 100, 200 Ticks); ein Opfer näher als ein Bild Flug wird im Tick des Schusses getroffen |
| `TickRateWorldTurretDeadlinesAndScanStayOnTheGrid` | Aufgeben nach 45 Bildern (45, 90, 180 Ticks nach dem ersten Schritt außer Reichweite); die Wartezeit des Leerlauf-Scans ist ein ganzes Vielfaches der Ticks je Bild zwischen 30 und 60 Bildern |
| `TickRateWorldAcquisitionLooksOnTheGrid` | Erster Blick zwei Bilder nach dem Leerlauf: erster Schuss in Tick 1 + 2, 1 + 4, 1 + 8 |

Die bestehenden Tests (179 Ticks Verkauf, 45 Ticks Interesse, 18/9/4 Ticks Schussabstand, erster Blick nach zwei Ticks, Scan 30..60 Ticks …) laufen unverändert und sind der 30-Hz-Beleg auf Testebene.

## 8. Ergebnisse

Siehe Abschlussbericht des Auftrags (Commit-Hashes, Manifestläufe, Testläufe, Build, Probelauf bei 60 Hz); die Zahlen stehen dort, weil sie nach dem letzten Commit gemessen werden.

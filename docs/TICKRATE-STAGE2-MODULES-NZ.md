# Tickrate, Stufe 2 – Module N–Z

Stand: 6. Oktober 2026. Auftrag „Modules/BuiltIn/ N–Z“ aus `docs/TICKRATE-MIGRATION.md` §6 (Phase 20, Punkt 4). Gegenstand sind `Modules/UpgradeMux.cs` und alle Dateien unter `Modules/BuiltIn/`, deren Name mit N bis Z beginnt (22 Dateien, 11.431 Zeilen): `Phase12Modules`, `Phase5BlockerModules`, `Phase5Modules`, `PhysicsAndMinefieldModules`, `ProductionExitModules`, `ProjectileModules`, `RailroadModules`, `RepairModules`, `RetailMatchModules`, `SlaveModules`, `SpawnModules`, `SpecialAbilityStaging`, `SpecialPowerModules`, `SpecialsModules`, `StructureModules`, `StructuresAndWorldModules`, `SupplyModules`, `ToppleAndFallModules`, `TransportModules`, `VehicleModules`, `WorldEffectModules`. Klassen (A bis H) und Widersprüche (W1, W2) wie in `docs/TICKRATE-AUDIT.md`; Schreibweise nach `docs/TICKRATE-MIGRATION.md` §1.

**Eiserne Regel:** bei 30 Hz ändert sich nichts. Jede Umstellung unten ist bei 30 Hz derselbe Ausdruck (`OriginalFrames` mal 1, `ToOriginalFrames` Identität, `TicksPerOriginalFrame` = 1 = `SleepNone`, `PerOriginalFrameToPerTick` ohne Rechnung).

## 1. Hilfsmittel

Neu, im eigenen Bereich: `Modules/BuiltIn/TickGrid.cs` (`internal static class TickGrid`), zwei Funktionen für W1:

| Funktion | Rechnung | Wofür |
|---|---|---|
| `TickGrid.RandomTicks(world, minTicks, maxTicks)` | `OriginalFrames(LogicRandom.Range(ToOriginalFrames(min), ToOriginalFrames(max)))` | Dauern, die zur Laufzeit zwischen zwei Katalogwerten gewürfelt werden: gezogen wird in Bildern des Originals, der Zug verbraucht den Logikstrom wie bei 30 Hz, das Ergebnis bleibt auf dem Raster |
| `TickGrid.CountdownActingAfterZero(time, ticks)` | `ticks + (TicksPerOriginalFrame − 1)` | Zähler des Originals „auf N setzen, je Update −1, im Update **nach** 0 handeln“ (Periode N + 1 Bilder). Mit diesem Startwert ist die Periode bei jeder Rate (N + 1) Bilder statt 2N + 1 Ticks |

Für Untergrenzen „mindestens ein Bild“ (W1 b) steht `world.Time.TicksPerOriginalFrame` (vorhandene Eigenschaft von `SimulationTime`, = `OriginalFrames(1)`) an der Stelle von `SimulationTiming.SleepNone`.

**Hilfen außerhalb des eigenen Bereichs: keine.** `SimulationTime`, `TickOrder` und `SimulationRate` sind unverändert.

## 2. Umgestellte Stellen (69)

Spalte „Kl.“: Klasse des Inventars, dahinter der Fall (W1 a = Rundung zur Laufzeit, W1 b = Untergrenze ein Tick, W1 c = Zähler N + 1, W1 d = Zufall über Tickbereich, W2 = Reihenfolge).

### `Phase12Modules.cs` (4)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `FireWeaponWhenDamagedBehavior.Fire` | E / W1 b | `Math.Max(DelayBetweenShotsTicks, SleepNone)` | `Math.Max(…, world.Time.TicksPerOriginalFrame)` | eigen (ersetzt den Waffenstatus; `FireWeaponWhenDamagedBehavior.cpp`, `update`) |
| `FireWeaponPower.Update` | E / W1 b | Schlaf `Math.Max(DelayBetweenShotsTicks, SleepNone)` | `Math.Max(…, TicksPerOriginalFrame)` | eigen |
| `HordeUpdate.OnSpawn` | C / W1 d | `LogicRandom.Range(1, UpdateRateTicks)` | Zug über `1..ToOriginalFrames(UpdateRateTicks)`, dann `OriginalFrames` | `HordeUpdate.cpp`, Konstruktor: `UPDATE_SLEEP(GameLogicRandomValue(1, delay))` |
| `HordeUpdate.Update` (Fahrzeuge) | C / W1 c | `world.Tick > _lastRefresh + UpdateRateTicks` | `world.Tick >= _lastRefresh + UpdateRateTicks + TicksPerOriginalFrame` (Periode Rate + 1 Bilder) | `HordeUpdate.cpp`, `HordeUpdate::update`: `getFrame() > m_lastHordeRefreshFrame + m_updateRate` |

### `Phase5BlockerModules.cs` (10)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `FlammableUpdate.Ignite` (`_aflameEndTick`) | E / W1 b | `Math.Max(AflameDurationTicks, SleepNone)` | `…, TicksPerOriginalFrame` | eigen (Mindestwert) |
| `FlammableUpdate.Update` (`_damageEndTick`) | E / W1 b | `Math.Max(AflameDamageDelayTicks, SleepNone)` | `…, TicksPerOriginalFrame` | eigen (Mindestwert) |
| `SlowDeathBehavior.BeginSlowDeath` (Sink-Zug) | B / W1 d | `Range(0, SinkDelayVarianceTicks)` | `TickGrid.RandomTicks(world, 0, SinkDelayVarianceTicks)` | `SlowDeathBehavior.cpp`, `beginSlowDeath`: `m_sinkDelay + GameLogicRandomValue(0, m_sinkDelayVariance)` |
| `SlowDeathBehavior.BeginSlowDeath` (Zerstörungs-Zug) | B / W1 d | `Range(0, DestructionDelayVarianceTicks)` | `TickGrid.RandomTicks(…)` | ebenda: `m_destructionDelay + GameLogicRandomValue(0, m_destructionDelayVariance)` |
| `SlowDeathBehavior.BeginSlowDeath` (`_destructionTick`) | E / W1 b | `Math.Max(destruction, SleepNone)` | `…, TicksPerOriginalFrame` | eigen (Mindestwert) |
| `StealthDetectorUpdate.OnSpawn` | C / W1 d | `Range(1, UpdateRateTicks)` | `TickGrid.RandomTicks(world, TicksPerOriginalFrame, UpdateRateTicks)` | `StealthDetectorUpdate.cpp`, Konstruktor: `UPDATE_SLEEP(GameLogicRandomValue(1, m_updateRate))` |
| `StealthDetectorUpdate.Update` (`MarkDetected`) | E / W2 | `UpdateRateTicks + 1` | `UpdateRateTicks + TickOrder.Next` (nicht skaliert) | `StealthDetectorUpdate.cpp`, `update`: `markAsDetected(m_updateRate + 1)` |
| `PoisonedBehavior.OnDamage` (`_poisonStopTick`) | E / W1 b | `Math.Max(PoisonDurationTicks, SleepNone)` | `…, TicksPerOriginalFrame` | eigen (Mindestwert) |
| `PoisonedBehavior.OnDamage` (`freshDamageTick`) | E / W1 b | `Math.Max(PoisonDamageIntervalTicks, SleepNone)` | `…, TicksPerOriginalFrame` | eigen (Mindestwert) |
| `PoisonedBehavior.Update` (`_poisonDamageTick`) | E / W1 b | wie oben | wie oben | eigen (Mindestwert) |

### `Phase5Modules.cs` (8)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `StealthUpdate.Update` (`_readyTick`) | E / W1 b | `Math.Max(StealthDelayTicks, SleepNone)` | `…, TicksPerOriginalFrame` | eigen (Mindestwert) |
| `AutoHealBehavior.OnSpawn` | E / W1 b | `Math.Max(StartHealingDelayTicks, SleepNone)` | `…, TicksPerOriginalFrame` | eigen (Mindestwert) |
| `AutoHealBehavior.TryActivate` | E / W1 b | wie oben | wie oben | eigen (Mindestwert) |
| `AutoHealBehavior.Update` | E / W1 b | `return Math.Max(HealingDelayTicks, SleepNone)` | `…, TicksPerOriginalFrame` (ein Heilimpuls je Bild des Originals) | `AutoHealBehavior.h`: `"HealingDelay"` `parseDurationUnsignedInt` (Standard dort `UINT_MAX`, Nebenbefund des Inventars) |
| `AIUpdateData.Parse` (`MoodAttackCheckRate`) | A / W1 b | `Math.Max(1, Ticks(…))` | `Math.Max(configuration.Time.TicksPerOriginalFrame, Ticks(…))` (Katalogbau) | `AIUpdate.cpp`, `AIUpdateModuleData::AIUpdateModuleData`: `m_moodAttackCheckRate = LOGICFRAMES_PER_SECOND * 2` |
| `AIUpdateInterface.Move` (Neuplanung) | C | `world.Tick - _blockerReplanTick >= 30` | `>= _blockerReplanInterval` = `OriginalFrames(BlockerReplanFrames)` mit `BlockerReplanFrames = OriginalFramesPerSecond` | eigen (`docs/DEMO-FINDINGS.md` Nr. 12) |
| `AIUpdateInterface.Move` (Vorausschau) | B | `Entity.Speed * Fixed.FromInt(25)` (zweimal) | `Entity.Speed * _blockerLookAhead` = `Fixed.FromInt(OriginalFrames(BlockerLookAheadFrames))`, `BlockerLookAheadFrames = 25` | eigen |
| `ProductionUpdate.Update` (Tür zu) | C / W2 | `RemainingTicks = 1` | `RemainingTicks = TickOrder.Next` (nicht skaliert) | eigen („im nächsten Tick wieder fragen“) |

Zu `Move`: die beiden Werte werden **einmal je Modulinstanz** aufgelöst (`ResolveBlockerTiming`, zwei nicht gespeicherte Felder, beim ersten `Move`; ein Modul entsteht und wird wiederhergestellt ohne Welt). Im Tick steht dafür ein Vergleich `== 0` statt der früheren `Fixed.FromInt(25)`; keine Umrechnung, keine Allokation, keine Division. Der Wächterwert `_blockerReplanTick = -1000` („nie geplant“, auch Standard im Snapshot) bleibt. Die Integration selbst (Ankunftsradius, Drehung, Beschleunigung, Bremsweg, Schritt) ist unverändert – Klasse E, §4.

### `ProjectileModules.cs` (7)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `MissileAIUpdate.SetFramesTillCountermeasureDiversionOccurs` | E / W1 b | `Math.Max(frames, 1)` | `Math.Max(frames, TicksPerOriginalFrame)` | `MissileAIUpdate.cpp`, `setFramesTillCountermeasureDiversionOccurs` |
| `PointDefenseLaserUpdate.FireWhenReady` (Ziel verlässt Reichweite) | A / W1 c+d | `_nextScan = Range(0, 3)`; `if (_nextScan == 0)` | `wait = Range(0, RescanMaxFrames)`; `_nextScan = CountdownActingAfterZero(OriginalFrames(wait))`; `if (wait == 0)` | `PointDefenseLaserUpdate.cpp`: `m_nextScanFrames = GameLogicRandomValue( 0, 3 )` |
| `PointDefenseLaserUpdate.FireWhenReady` (Ziel tot) | A / W1 c+d | wie oben | wie oben | ebenda |
| `PointDefenseLaserUpdate.Update` (`_nextScan = ScanTicks`) | C / W1 c | `_nextScan = data.ScanTicks` | `TickGrid.CountdownActingAfterZero(world.Time, data.ScanTicks)` | `PointDefenseLaserUpdate.cpp`: `m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames` |
| `PointDefenseLaserUpdate.FireWhenReady` (zwei weitere `_nextScan = ScanTicks`) | C / W1 c | wie oben | wie oben (2 Stellen) | ebenda |
| `PointDefenseLaserUpdate.FireWhenReady` (`_nextShot`) | E / W1 c | `_nextShot = weapon.DelayBetweenShotsTicks` | `TickGrid.CountdownActingAfterZero(…)` (Schussabstand Delay + 1 Bilder) | `PointDefenseLaserUpdate.cpp`: `m_nextShotAvailableInFrames-- / = wt->getDelayBetweenShots( bonus )` |

(Die Zeile mit zwei Stellen zählt zweifach.)

### `RailroadModules.cs` (1)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `RailroadBehavior.FramesUnpulledLongEnoughToUnhitch` | A / W2 | `const int … = 2` | `const long … = 2 * TickOrder.Next` (nicht skaliert) | `RailroadGuideAIUpdate.cpp`: `#define FRAMES_UNPULLED_LONG_ENOUGH_TO_UNHITCH (2)` |

### `RepairModules.cs` (2)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `RepairDockData.Parse` (ohne `TimeForFullHeal`) | B / fallback | `Fixed.One` (ein Tick) | `Fixed.FromInt(configuration.Time.TicksPerOriginalFrame)` (ein Bild) | `RepairDockUpdate.cpp`, Konstruktor: `m_framesForFullHeal = 1.0f` |
| `AutoFindHealingUpdate.Update` | C / W1 c | `_nextScanTicks = data.ScanTicks` | `TickGrid.CountdownActingAfterZero(…)` | `AutoFindHealingUpdate.cpp`: `m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames` |

### `SlaveModules.cs` (8)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `SlavedUpdate.SetRepairState` (Unpacking/Packing) | A | `_framesToWait = 15` | `OriginalFrames(PackFrames)`, `PackFrames = 15` | `SlavedUpdate.cpp`, `setRepairState`: `m_framesToWait = 15` |
| `SlavedUpdate.SetRepairState` (None → Unpacking) | A | `_framesToWait = 15` | `OriginalFrames(PackFrames)` | ebenda |
| `SlavedUpdate.SetRepairState` (Welding → Retracting) | A | `_framesToWait = 5` | `OriginalFrames(ArmFrames)`, `ArmFrames = 5` | ebenda: `m_framesToWait = 5` |
| `SlavedUpdate.SetRepairState` (Ready → Extending) | A | `_framesToWait = 5` | `OriginalFrames(ArmFrames)` | ebenda |
| `SlavedUpdate.SetRepairState` (Ready-Dauer) | B / W1 d | `Range(MinReadyTicks, MaxReadyTicks)` | `TickGrid.RandomTicks(…)` | ebenda: `GameLogicRandomValue(m_minReadyFrames, m_maxReadyFrames)` |
| `SlavedUpdate.SetRepairState` (Schweißdauer) | B / W1 d | `Range(MinWeldTicks, MaxWeldTicks)` | `TickGrid.RandomTicks(…)` | ebenda: `GameLogicRandomValue(m_minWeldFrames, m_maxWeldFrames)` |
| `MobMemberSlavedUpdate.CheckInterval` | A | `const int CheckInterval = 16`; `++_framesToWait < CheckInterval` | `CheckIntervalFrames = 16`; `< world.Time.OriginalFrames(CheckIntervalFrames)` | `MobMemberSlavedUpdate.cpp`, `update`: `if ( ++m_framesToWait < 16 )` |
| `MobMemberSlavedUpdate.OnSpawn` | A / W1 d | `_framesToWait = Range(0, 20)` | `OriginalFrames(Range(0, FirstCheckStaggerFrames))` | `MobMemberSlavedUpdate.cpp`, Konstruktor: `m_framesToWait = GameLogicRandomValue(0,20)` |

### `SpecialAbilityStaging.cs` (1), `SpecialPowerModules.cs` (1)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `SpecialAbilityUpdate.Varied` | B / W1 a | `Math.Max(1, (Fixed.FromInt(ticks) * factor).Floor())` | dieselbe Rechnung auf `ToOriginalFrames(ticks)`, Ergebnis über `OriginalFrames` | `SpecialAbilityUpdate.cpp`, `startPacking`/`startUnpacking`: `GameLogicRandomValueReal(1 - m_packUnpackVariationFactor, …)` |
| `SpectreGunshipDeploymentUpdate.TryTakePending` | E / W2 | `world.Tick - value.Tick <= 2` | `<= 2 * TickOrder.Next` (nicht skaliert) | eigen (die Entität entsteht am Tickende) |

### `StructureModules.cs` (4)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `StructureCollapseUpdate.OnDie` (`_collapseTick`) | B / W1 d | `Range(MinCollapseDelay, MaxCollapseDelay)` | `TickGrid.RandomTicks(…)` | `StructureCollapseUpdate.cpp`, `beginStructureCollapse` |
| `StructureCollapseUpdate.Update` (erster `_burstTick`) | B / W1 d | `Range(MinBurstDelay, MaxBurstDelay)` | `TickGrid.RandomTicks(…)` | `StructureCollapseUpdate.cpp`, `update`: `m_burstFrame += GameLogicRandomValue(d->m_minBurstDelay, d->m_maxBurstDelay)` |
| `StructureCollapseUpdate.Update` (`_burstTick +=`) | B / W1 d | wie oben | wie oben | ebenda |
| `FireSpreadUpdate.NextDelay` | B / W1 b+d | `Math.Max(Range(MinDelay, MaxDelay), SleepNone)` | `Math.Max(TickGrid.RandomTicks(…), TicksPerOriginalFrame)` | `FireSpreadUpdate.cpp`, `calcNextSpreadDelay` |

### `ToppleAndFallModules.cs` (3)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `StructureToppleUpdate.OnDie` (`_toppleFrame`) | B / W1 d | `Math.Max(0, Range(MinToppleDelayTicks, MaxToppleDelayTicks))` | `Math.Max(0, TickGrid.RandomTicks(…))` | `StructureToppleUpdate.cpp`, `beginStructureTopple` |
| `ToppleUpdate.VelocityBounceLimit` | A (additiv je Bild) | `0,01` als Schwelle gegen die Winkelgeschwindigkeit je Tick | `VelocityBounceLimitPerFrame`; an der Verwendung `world.Time.PerOriginalFrameToPerTick(…)` (nur im Abprall-Tick) | `ToppleUpdate.cpp`, `ToppleUpdate::update`: `const Real VELOCITY_BOUNCE_LIMIT = 0.01f` |
| `StructureToppleUpdate.AccelFactor` | A (additiv je Bild²) | `0,02` als Winkelbeschleunigung je Tick² | `AccelFactorPerFrameSquared`; in `StepToppling` `world.Time.PerOriginalFrameSquaredToPerTickSquared(…)` | `StructureToppleUpdate.cpp`, `update`: `TOPPLE_ACCELERATION_FACTOR = 0.02f` |

`StepToppling` läuft nur, während ein Gebäude kippt; bei 30 Hz gibt die Umrechnung den Wert ohne Rechnung zurück, bei 60/120 Hz ist es eine `Fixed`-Division je Tick des Kippens. Die Integration selbst (und `StructuralDecay`) bleibt Phase 23.

### `TransportModules.cs` (3), `VehicleModules.cs` (5)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `HelicopterSlowDeathBehavior.BeginSlowDeath` (Sink-Zug) | E / W1 d | `Range(0, SinkDelayVarianceTicks)` | `TickGrid.RandomTicks(…)` | `SlowDeathBehavior.cpp`, `beginSlowDeath` (siehe oben) |
| `HelicopterSlowDeathBehavior.BeginSlowDeath` (Zerstörungs-Zug) | E / W1 d | `Range(0, DestructionDelayVarianceTicks)` | `TickGrid.RandomTicks(…)` | ebenda |
| `HelicopterSlowDeathBehavior.BeginSlowDeath` (`_destructionTick`) | E / W1 b | `Math.Max(destruction, SleepNone)` | `…, TicksPerOriginalFrame` | eigen (Mindestwert) |
| `BattleBusSlowDeathBehavior.GroundCheckDelay` | A | `const long … = 10`; `world.Tick + GroundCheckDelay` | `GroundCheckDelayFrames`; `world.Tick + OriginalFrames(…)` | `BattleBusSlowDeathBehavior.cpp`: `GROUND_CHECK_DELAY = 10` |
| `BattleBusSlowDeathBehavior.EmptyHulkCheckDelay` | A | `const long … = 15`; `return EmptyHulkCheckDelay` | `EmptyHulkCheckDelayFrames`; `return OriginalFrames(…)` | `BattleBusSlowDeathBehavior.cpp`: `EMPTY_HULK_CHECK_DELAY = 15`, `UPDATE_SLEEP(EMPTY_HULK_CHECK_DELAY)` |
| `BattleBusSlowDeathBehavior.BeginSlowDeath` (Sink-Zug) | E / W1 d | `Range(0, SinkDelayVarianceTicks)` | `TickGrid.RandomTicks(…)` | `SlowDeathBehavior.cpp`, `beginSlowDeath` |
| `BattleBusSlowDeathBehavior.BeginSlowDeath` (Zerstörungs-Zug) | E / W1 d | `Range(0, DestructionDelayVarianceTicks)` | `TickGrid.RandomTicks(…)` | ebenda |
| `BattleBusSlowDeathBehavior.BeginSlowDeath` (`_destructionTick`) | E / W1 b | `Math.Max(destruction, SleepNone)` | `…, TicksPerOriginalFrame` | eigen (Mindestwert) |

### `WorldEffectModules.cs` (12)

| Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|
| `OCLUpdate.SetNextCreation` | E / W1 d | `Range(MinDelay, MaxDelay)` | `TickGrid.RandomTicks(…)` | `OCLUpdate.cpp`: `GameLogicRandomValue(m_minDelay, m_maxDelay)` |
| `DynamicShroudClearingRangeUpdate.Update` (`_changeIntervalCountdown`) | C / W1 c | `= GrowInterval` bzw. `ChangeInterval` | `TickGrid.CountdownActingAfterZero(…)` | `DynamicShroudClearingRangeUpdate.cpp`, `update` (Zeilen 271–279) |
| `FireOCLAfterWeaponCooldownUpdate.Update` | E / W2 | `LastFireTickOf(slot) == now - 1` | `== now - TickOrder.Next` (nicht skaliert) | `FireOCLAfterWeaponCooldownUpdate.cpp`, `update`: `weapon->getLastShotFrame() == now - 1` |
| `FireOCLAfterWeaponCooldownUpdate.FireOcl` | B / W1 a | `(Tick − _startTick) * LifetimePerSecond / 1000` (abgeschnitten, in Ticks) | dieselbe Rechnung auf `ToOriginalFrames(Tick − _startTick)`, Ergebnis über `OriginalFrames` | ebenda, Zeilen 200–202 |
| `WaveGuideUpdate.Update` (Splash-Wurf) | A / W1 c | `now - _splashTick > OriginalFrames(30 / 2)` | `>= OriginalFrames(SplashFrames) + TicksPerOriginalFrame` (Periode 16 Bilder) | `WaveGuideUpdate.cpp`, `update`: `getFrame() - m_splashSoundFrame > LOGICFRAMES_PER_SECOND / 2.0f` |
| `CheckpointUpdate.GateStep` | D (additiv je Bild) | `0,333` je Tick | `GateStepPerFrame`; in `Update` `world.Time.PerOriginalFrameToPerTick(…)` | `CheckpointUpdate.cpp`, `update`: `radius - 0.333f` / `radius + 0.333f` |
| `CheckpointUpdate.OnSpawn` | E / W1 d | `Range(0, ScanDelayTicks)` | `TickGrid.RandomTicks(world, 0, ScanDelayTicks)` (Wert weiter ungenutzt, der Zug bleibt) | `CheckpointUpdate.cpp`, Konstruktor: `GameLogicRandomValue(0, m_enemyScanDelayTime)` |
| `EnemyNearUpdate.OnSpawn` | E / W1 c+d | `Range(0, ScanDelayTicks)` | `CountdownActingAfterZero(RandomTicks(world, 0, ScanDelayTicks))` | `EnemyNearUpdate.cpp`, Konstruktor: `GameLogicRandomValue(0, m_enemyScanDelayTime)` |
| `EnemyNearUpdate.Update` | C / W1 c | `_scanDelay = data.ScanDelayTicks` | `TickGrid.CountdownActingAfterZero(…)` | `EnemyNearUpdate.cpp`, `update`: `if (m_enemyScanDelay == 0) … else --m_enemyScanDelay` |
| `FirestormDynamicGeometryInfoUpdate.OnSpawn` | E / W1 b | `Math.Max(InitialDelay, 1)` | `Math.Max(InitialDelay, TicksPerOriginalFrame)` | `DynamicGeometryInfoUpdate.cpp`, `update`: `m_startingDelayCountdown--` |
| `FirestormDynamicGeometryInfoUpdate.Update` (Schadensscan) | E / W1 b | `Tick − _lastDamage >= DamageDelay` (bei 0 jeder Tick) | `>= Math.Max(DamageDelay, TicksPerOriginalFrame)` | `FirestormDynamicGeometryInfoUpdate.cpp`, `update` (Zeilen 215–219) |
| `WeaponBonusUpdate.Update` | C / W1 b | `Math.Max(Delay, SleepNone)` | `Math.Max(Delay, TicksPerOriginalFrame)` | `WeaponBonusUpdate.cpp`, `update`: `return UPDATE_SLEEP(data->m_bonusDelay)` |

### Summen

| Datei | A | B | C | D | E | davon W1 | davon W2 | Summe |
|---|---|---|---|---|---|---|---|---|
| `Phase12Modules.cs` |  |  | 2 |  | 2 | 4 |  | 4 |
| `Phase5BlockerModules.cs` |  | 2 | 1 |  | 7 | 9 | 1 | 10 |
| `Phase5Modules.cs` | 1 | 1 | 2 |  | 4 | 5 | 1 | 8 |
| `ProjectileModules.cs` | 2 |  | 3 |  | 2 | 7 |  | 7 |
| `RailroadModules.cs` | 1 |  |  |  |  |  | 1 | 1 |
| `RepairModules.cs` |  | 1 | 1 |  |  | 1 |  | 2 |
| `SlaveModules.cs` | 6 | 2 |  |  |  | 3 |  | 8 |
| `SpecialAbilityStaging.cs` |  | 1 |  |  |  | 1 |  | 1 |
| `SpecialPowerModules.cs` |  |  |  |  | 1 |  | 1 | 1 |
| `StructureModules.cs` |  | 4 |  |  |  | 4 |  | 4 |
| `ToppleAndFallModules.cs` | 2 | 1 |  |  |  | 1 |  | 3 |
| `TransportModules.cs` |  |  |  |  | 3 | 3 |  | 3 |
| `VehicleModules.cs` | 2 |  |  |  | 3 | 3 |  | 5 |
| `WorldEffectModules.cs` | 1 | 1 | 3 | 1 | 6 | 10 | 1 | 12 |
| **Summe** | **15** | **13** | **12** | **1** | **28** | **51** | **5** | **69** |

Die Klasse ist die des Inventars; die 28 Stellen der Klasse E sind ausschließlich W1 (Untergrenzen, Zähler, Zufall) und W2 (Reihenfolge) – an keiner Integration wurde etwas geändert.

## 3. Schon in Stufe 1 erledigt bzw. geprüft, nicht gekoppelt

| Datei | Symbol | Befund |
|---|---|---|
| `Phase12Modules.cs` | `TechBuildingData.PulseRateTicks` (F/tick) | kein Verbraucher in der Simulation; über `ModuleConfigReader.Ticks` schon je Rate |
| `Phase12Modules.cs` | `HordeData.Parse` (`UpdateRate`), `HordeUpdate.Update` (Infanterie schläft `UpdateRateTicks`) | Ersatzwert seit Stufe 1 in Bildern; Schlafdauer ist ein Katalogwert |
| `Phase5BlockerModules.cs` | `StealthDetectorData.Parse` (`DetectionRate`, Ersatz 1 Bild) | Stufe 1 |
| `Phase5BlockerModules.cs` | `StealthDetectorUpdate.Update`: `return Math.Max(UpdateRateTicks, SleepNone)` | **bewusst ein Tick:** die Entdeckung hält `UpdateRateTicks + TickOrder.Next`; eine Untergrenze von einem Bild würde bei `DetectionRate = 0` zwischen zwei Scans eine Lücke von einem Tick öffnen. Nur bei `DetectionRate 0` wirksam |
| `Phase5BlockerModules.cs` | `FlammableUpdate.Update`: `return Math.Max(soonest - now, SleepNone)`; `WorldEffectModules.cs` `WeaponBonusUpdate.Update` ebenso | Wächter des Planers (nie 0 schlafen) auf einer Differenz absoluter Ticks, keine Zeit |
| `Phase5Modules.cs` | `StealthData.MoveThresholdSpeed`, `AIUpdateData.DefaultMoodCheckFrames`, `UpdateTask` (`RepairRatePerSecond / TicksPerSecondFixed`) | Stufe 1 |
| `Phase5Modules.cs` | `StealthUpdate.MarkDetected`: `Math.Max(ticks, SleepNone)` | Untergrenze eines Werts, den der Aufrufer aus Rate + Reihenfolge bildet; bleibt ein Tick |
| `Phase5Modules.cs` | `StealthUpdate._framesGranted` (Zähler je Tick) | kein Aufrufer übergibt eine Dauer > 0 (`ReceiveGrant(…, 0)`); ein Aufrufer mit Dauer muss Ticks übergeben |
| `Phase5Modules.cs` | `StealthUpdate.ConditionActive` (`LastFireTick == world.Tick`) | Vergleich „im selben Tick“, braucht keine Markierung (Konvention Fall e) |
| `Phase5Modules.cs` | `ProductionUpdate.Update` (`RemainingTicks - 1` je Tick) | Tickzähler über einen Wert des Aufrufers in Ticks |
| `PhysicsAndMinefieldModules.cs` | `GenerateMinefieldBehavior.Update` (Abfrage je Tick) | nur Last |
| `ProductionExitModules.cs` | `QueueProductionExitUpdate` (`_delay = ExitDelayTicks`, `_delay--`) | Zähler mit Periode genau N (frei, sobald 0), Katalogwert in Ticks |
| `ProjectileModules.cs` | `DefaultMaxLifespanFrames`, `FlightPathAdjustDistPerSecond`, `InitialVelocity`, `KillSelfDelay` (Ersatz 3 Bilder) | Stufe 1 |
| `RailroadModules.cs` | `WaitAtStationTime` (Ersatz 150 Bilder), `SpeedMax`/`KillSpeedMin`/`RunningGarrisonSpeedMax` | Stufe 1 |
| `RepairModules.cs` | `RepairDockData.Parse` (`TimeForFullHeal`, ungerundet) | Stufe 1 (`DurationMillisecondsToFixedTicks`) |
| `RetailMatchModules.cs` | `OverchargeBehavior.Update`, `RebuildHoleBehavior.Update` (`… / TicksPerSecondFixed`) | Stufe 1 |
| `RetailMatchModules.cs` | `RebuildHoleBehavior._workerWait` (`--` je Tick bis 0) | Katalogwert in Ticks, Periode genau N |
| `RetailMatchModules.cs` | `RadarUpdate.IsExtendComplete` (`tick > _extendDoneTick`) | Vergleich ohne Literal (Reihenfolge) |
| `SlaveModules.cs` | `UpdateRateFrames`, `RecentFireFrames`, `DoRepairLogic` (`RepairRatePerSecond / TicksPerSecondFixed`) | Stufe 1 |
| `SlaveModules.cs` | `SlavedUpdate.Update` (`_framesToWait--`, Handeln bei 0 im selben Update) | Periode genau N, kein N + 1 |
| `SlaveModules.cs` | `MobMemberData.CatchUpCrisisBailTime` (D) | Zahl von **Prüfungen**, nicht von Bildern; mit skaliertem `CheckIntervalFrames` fällt dieselbe Zahl Prüfungen in dieselbe Spielzeit – bleibt unskaliert |
| `SlaveModules.cs` | `SpawnPointProductionExitUpdate.Revalidate` (`world.Tick > tick`) | Vergleich ohne Literal (Reihenfolge) |
| `SpawnModules.cs` | `SpawnBehavior.UpdateRateFrames`, `_workTick`, Schlafrückgabe | Stufe 1; das Polling toter Spawns liegt damit im Bildraster |
| `SpecialAbilityStaging.cs` | `StageTicksLeft` (F/tick), `_prepFrames`/`_animFrames` (`--` je Tick) | Zähler über Katalogwerte in Ticks; die Präsentation liest Ticks (Phase 21) |
| `SpecialPowerModules.cs` | `AvailableOnFrame` (F/tick), `BuildCompleteModule` (`Tick + SleepNone`) | absoluter Tick für das HUD (Phase 21); Reihenfolge über `SleepNone` |
| `SpecialsModules.cs` | `GrantStealthData.RadiusGrowRate` | Stufe 1 |
| `StructureModules.cs` | `MinBurstDelay` (Ersatz 9999 Bilder) | Stufe 1 |
| `StructureModules.cs` | `DeployStyleAIUpdate` (`_frameToWait = now + UnpackTicks …`, `= 0`) | absolute Ticks aus Katalogwerten; 0 ist der Wächter „kein Zeitgeber“ |
| `SupplyModules.cs` | `SupplyCarrierAI.Update` (`Math.Min(baseSleep, SleepNone)`) | läuft je Tick, solange der Automat arbeitet – nur Last; die Aktionsfristen sind Katalogwerte |
| `TransportModules.cs` | `HelicopterSlowDeathData` (vier Werte je Sekunde), `FixedTicks`, `SelfSpinUpdateAmount / TicksPerSecondFixed` | Stufe 1; der letzte Ausdruck ist auch bei 60 Hz richtig (`_selfSpin` ist Radiant je Tick: Betrag / 30 je Bild = Betrag / Rate je Tick) |
| `TransportModules.cs` | `TunnelContainData` (`TimeForFullHeal` fehlt → 0) | Ersatzwert 0, nichts zu skalieren (Nebenbefund des Inventars: Original 1 Bild) |
| `VehicleModules.cs` | `BattleBusData.ThrowForce`, `AnimationSteeringUpdate.MinTransitionTicks` (F/tick) | Stufe 1 bzw. Katalogwert für die Präsentation |
| `WorldEffectModules.cs` | `OCLLifetimeMaxCap` (Ersatz 1000 Bilder), `ScanDelayTime` (Ersatz 30 Bilder) | Stufe 1 |
| `WorldEffectModules.cs` | `DynamicShroudClearingRangeUpdate`: `Math.Max(1, GrowTime)`/`Math.Max(1, ShrinkTime)`, `_stateCountdown--` | Schutz vor Division durch 0 (kein Zeitwert); Tickzähler über Katalogwerte |
| `WorldEffectModules.cs` | `OCLUpdate` (`_nextCreation == 0`), `HelicopterSlowDeathBehavior._hitGroundTick == 0` | Wächterwerte, keine Zeit |
| `UpgradeMux.cs`, `StructuresAndWorldModules.cs` | – | keine Zeitkopplung |

## 4. Bewusst unverändert – Ergänzungen zur Liste für Phase 23

Klasse E (Integration je Tick, Schwellen gegen einen Tick-Schritt, Läufe je Tick) und W1-Stellen, die sich nicht ohne Änderung bei 30 Hz in Bildern rechnen lassen. Was `docs/TICKRATE-MIGRATION.md` §5 schon nennt (Zug, `StructuralDecay`, `InitialAccelPercent`, `SpiralOrbitForwardSpeedDamping`), steht hier nicht noch einmal.

| Datei | Symbol | Was bei 60 Hz anders ist |
|---|---|---|
| `Phase12Modules.cs` | `FireWeaponWhenDamagedBehavior.Update` | Dauerwaffe wird je Tick geprüft (nur Last; der Schussabstand liegt im Raster) |
| `Phase5Modules.cs` | `AIUpdateInterface.Move` (Ankunftsradius `max(CloseEnoughDist, Speed)`, Drehung, Beschleunigung/Bremsweg, Positionsschritt, `AvoidLocalBlockers`), `MoveWinged`, `SteerWinged`, `MoveStraight` | Euler je Tick; Ankunftsradius und Wendekreis hängen am Tick-Schritt (halber Weg je Tick), Bremsweg `v²/(2a)` in Tick-Einheiten |
| `Phase5Modules.cs` | `ProductionUpdate.Enqueue` (`Math.Max(totalTicks, 1)`) | Untergrenze ein Tick statt ein Bild; die Funktion hat keinen Zeitdienst (öffentliche Signatur, Aufrufer in `World/`) |
| `Phase5Modules.cs` | `ProductionUpdate.Update` (Tür je Tick abfragen) | fertige Einheit verlässt die Fabrik bis zu einen halben Frame früher |
| `Phase5BlockerModules.cs` | `StealthDetectorUpdate` bei `DetectionRate = 0` | scannt je Tick statt je Bild (siehe §3, bewusst) |
| `PhysicsAndMinefieldModules.cs` | `PhysicsBehavior.Update` (freier Fall) | Euler je Tick mit `GravityPerTickSquared`; Landezeit weicht um Bruchteile eines Bilds ab |
| `ProjectileModules.cs` | `DumbProjectileBehavior.CalcFlightPath` (`_segments = ceil(length / _speed)`), `Update` (ein Bahnpunkt je Tick, Detonation im Update nach dem letzten Punkt, Nachführung je Tick) | Flugdauer in ganzen Ticks statt ganzen Bildern; „ein Tick später“ ist ein halbes Bild |
| `ProjectileModules.cs` | `MissileAIUpdate.Launch` (`_acceleration = weapon.WeaponSpeed`), `Update` (`_noTurnDistanceLeft` aus dem Vortick), `Move` (Euler, Ankunft bei Restweg ≤ ein Tick Flug) | Waffengeschwindigkeit je Tick als Beschleunigung je Tick²: volle Geschwindigkeit nach einem Tick, halber Wert; Ankunftsschwelle halbiert |
| `ProjectileModules.cs` | `PointDefenseLaserUpdate.FireWhenReady` | Reichweitenprüfung und Schussversuch je Tick (die Zähler liegen jetzt im Raster) |
| `RailroadModules.cs` | `RailroadBehavior.Update` („Wagen existieren erst im nächsten Tick“), `Collide` (Wegschieben um ein Viertel der Überlappung je Tick) | Reihenfolge über `SleepNone` (bleibt); das Wegschieben ist doppelt so schnell |
| `RepairModules.cs` | `RepairDockUpdate.Action` (`_healthToAddPerTick = (Max − Health) / FramesForFullHeal`) | Wert je Tick aus ungerundeten Ticks; setzt einen Aufruf je Tick voraus – gleicht sich aus, bis auf die `Fixed`-Rundung |
| `RetailMatchModules.cs` | `HealContain.Update` (`MaxHealth / TimeForFullHealTicks` je Tick) | W1 a: Heilung je Tick aus der Tickzahl – gleiche Dauer, andere `Fixed`-Rundung je Schritt |
| `SlaveModules.cs` | `SlavedUpdate.DoRepairLogic` (läuft im Reparaturzustand je Tick: Höhen-Zufallszug und Bewegungsbefehl je Tick, solange die Drohne nicht nah genug ist) | doppelt so viele Züge aus dem Logikstrom und Befehle |
| `SlaveModules.cs` | `DoAttackLogic`/`DoScoutLogic`/`DoGuardLogic` (`RandomDirection` je Prüfung), `MobMemberSlavedUpdate.Update` (`Range(0, 10)` je Prüfung) | liegen im Prüfraster (jetzt skaliert) – unverändert viele Züge je Spielzeit; hier nur zur Vollständigkeit |
| `SpecialAbilityStaging.cs` | `SpecialAbilityUpdate.StepCaptureFlash` (`_flashPhase += (1 − prep/Preparation) / 3` je Tick) | die Blitzphase wächst doppelt so schnell: doppelt so viele `CaptureFlash`-Ereignisse |
| `SpecialAbilityStaging.cs` | `SpecialAbilityUpdate.TurnToward` | Einrasten innerhalb eines Tick-Schritts der Drehung |
| `SpecialsModules.cs` | `GrantStealthBehavior.Update` (Scan je Tick), `WanderAIUpdate.Update` (zwei Züge und ein Befehl je Leerlauf-Tick), `StickyBombUpdate.Update`, `ProjectileStreamUpdate.Update` | Läufe je Tick; `WanderAIUpdate` zieht doppelt so oft aus dem Logikstrom |
| `StructureModules.cs` | `StructureCollapseUpdate.Update` (Euler: `_height -= _velocity`, `_velocity -= g · (1 − Damping)`; höchstens ein Burst je Tick) | Einsturzzeit weicht um die Euler-Ordnung ab |
| `StructureModules.cs` | `FloatUpdate.Update` | Lauf je Tick, nur Last |
| `ToppleAndFallModules.cs` | `ToppleUpdate.Update` (Euler, im Abprall-Tick keine Beschleunigung), `StructureToppleUpdate.StepToppling`, `ApplyCrushingDamage` (ein Waffenschuss je Tick, in dem die Vorderkante weiterreicht), `_doneAtTick` (Done im Folgetick), `HeightDieUpdate.Update` (`_lastZ` des Vorticks) | Fallzeit weicht ab; doppelt so viele Quetsch-Schüsse; `VelocityBounceLimit` und `AccelFactor` sind seit dieser Stufe je Tick |
| `TransportModules.cs` | `ChinookAIUpdate.Update` (`climb = locomotor.Speed`) | Steigrate je Tick folgt der Geschwindigkeit je Tick – gleiche Rate je Spielzeit |
| `TransportModules.cs` | `HelicopterSlowDeathBehavior.BeginSlowDeath` (`_bladeFlyOff = Range(MinBladeFlyOffDelay, MaxBladeFlyOffDelay)` über **ungerundete** `Fixed`-Ticks, je Tick −1) | W1 d, nicht umgestellt: die Grenzen liegen nicht auf dem Raster (`parseDurationReal`), ein Zug in Bildern müsste die Grenzen zurückteilen und würde bei 60 Hz anders runden als das Original; der gezogene Wert ist bei 60 Hz nicht das Doppelte |
| `TransportModules.cs` | `HelicopterSlowDeathBehavior.Update` (Spin/Orbit/Fall, Vergleich `Tick − _lastSelfSpinUpdate > SelfSpinUpdateDelay` gegen ungerundete Ticks), `TunnelContain.Update` (`MaxHealth / TicksForFullHeal` je Tick, je Tunnel) | Euler je Tick; Vergleich kippt auf Tick- statt Bildgrenzen |
| `VehicleModules.cs` | `BattleBusSlowDeathBehavior.Update` (Wurf: `Z += v`, dann `v += g`) | Euler je Tick; Wurfhöhe weicht um die Euler-Ordnung ab |
| `WorldEffectModules.cs` | `DynamicShroudClearingRangeUpdate.Update` (`_current += native / GrowTime` je Tick) | gleicht sich über die Tickzahl aus, andere `Fixed`-Rundung |
| `WorldEffectModules.cs` | `FireOCLAfterWeaponCooldownUpdate.Update` (läuft je Tick, liest den Schuss des Vorticks) | Reihenfolge, unverändert richtig; nur Last |
| `WorldEffectModules.cs` | `WaveGuideUpdate.Update`/`Advance`/`DoDamage` | ein Pfadschritt und eine Schadensabtastung je Tick: doppelt so dichte Abtastung der Front |
| `WorldEffectModules.cs` | `CheckpointUpdate.Update` (zwei Vollsuchen je Tick) | nur Last; der Torschritt ist jetzt je Tick skaliert |
| `WorldEffectModules.cs` | `FirestormDynamicGeometryInfoUpdate.Update` (`ratio = _timeActive / TransitionTime`, Ende bei `_timeActive > TransitionTime`) | Übergang dauert `TransitionTime + 1` Ticks: bei 60 Hz ein halbes Bild kürzer als bei 30 Hz (W1 c in einer Interpolation; `_timeActive` ist zugleich der Zähler der Interpolation) |

## 5. Tests

`tests/CnCNew.Simulation.Tests/Tests.TickRate.ModulesNZ.cs` (sechs Tests, in `Program.cs` als ein Block hinter den Tests der Stufe 1 angemeldet). Jedes Szenario läuft bei 30, 60 und 120 Hz mit demselben Seed auf der synthetischen Fixture der jeweiligen Rate; gemessen wird die Tickspanne zwischen zwei Beobachtungen, sie muss bei 60 Hz genau das Doppelte und bei 120 Hz das Vierfache der 30-Hz-Spanne sein (dieselbe Bildgrenze des Originals):

| Test | Belegt |
|---|---|
| Detektor-Staffelung | `TickGrid.RandomTicks` (Zug in Bildern), `StealthDetectorUpdate.OnSpawn` |
| Gift ohne Intervall | Untergrenze ein Bild (`PoisonedBehavior`): 40 Schaden zu 10 je Bild = 4 Bilder |
| Selbstheilung ohne Verzögerung | Untergrenze ein Bild (`AutoHealBehavior`): 20 Punkte zu 1 je Bild = 20 Bilder |
| Feind-in-der-Nähe-Scan | `TickGrid.CountdownActingAfterZero` und Startversatz in Bildern (`EnemyNearUpdate`) |
| Varianz des langsamen Tods | `SlowDeathBehavior.BeginSlowDeath` (zwei Züge in Bildern) |
| Katalog | `AIUpdateData.MoodCheckTicks` = 60 Bilder je Rate |

Nicht durch einen eigenen Test belegt (nur durch die unveränderten 30-Hz-Tests und die Manifeste): die Neuplanung in `Move`, `SlavedUpdate`-Schweißzeiten, `MobMemberSlavedUpdate`, `PointDefenseLaserUpdate`, `BattleBusSlowDeathBehavior`, Kippwerte, `CheckpointUpdate.GateStep`, `FireOCLAfterWeaponCooldownUpdate`.

## 6. Suchmuster (`docs/TICKRATE-AUDIT.md` §8) über die Dateien dieses Auftrags, nach der Umstellung

| Muster | Treffer | Bewertung |
|---|---|---|
| P01, P02, P03, P07, N05 | 0 | – |
| P09 | 1 | `return 0;` beim Lesen der Türanimationen (`ProductionData`), keine Schlafdauer |
| P13 | 3 | `RandomDirection` (0..6 Radiant), `Range(0, 10)` (Locomotor-Wahl), `Range(1, 100)` (Splash) – keine Tickbereiche |
| P04 | 2 | Kommentare („30 Hz grid“) |
| P05, P06, N08 | 27 `const` | 15 Konstanten in Bildern (`…Frames`; verwendet über `OriginalFrames`, als Ersatzwert von `ModuleConfigReader.Ticks` oder als Grenze eines Zugs in Bildern), `FramesUnpulledLongEnoughToUnhitch` (Reihenfolge, `TickOrder.Next`), `BeforeAnyTick = -1` (Wächter), der Rest Größen ohne Zeit (Quadrate von Abständen, Puffergrößen, Festkomma-Bits) |
| P08 | 2 | `RecentFireFrames` (Stufe 1), `2 * TickOrder.Next` (Reihenfolge) |
| P10 | 9 Ersatzwerte ≠ 0 | alle seit Stufe 1 Bilder des Originals |
| P11 | 12 | Werte je Sekunde (zur Laufzeit über `TicksPerSecondFixed`), Rohwerte je Bild (Stufe 1), `CatchUpCrisisBailTime` (Zahl von Prüfungen, §3), `OCLLifetimePerSecond` (ratenneutrales Verhältnis) |
| P12 | 5 | multiplikative Faktoren je Bild – alle in `docs/TICKRATE-MIGRATION.md` §5 |
| N07 | viele | `Tick + Katalogwert`, `Tick + SleepNone`, `Tick − Zustand` – kein nacktes Literal außer `2 * TickOrder.Next` |
| N14 | viele | Züge über Tickbereiche laufen über `TickGrid.RandomTicks` oder in Bildern; übrig sind Winkel, Abstände, Auswahlen und `_bladeFlyOff` (§4) |

Kein Treffer ist unbewertet.

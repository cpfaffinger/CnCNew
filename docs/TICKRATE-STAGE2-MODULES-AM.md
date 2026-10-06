# Tickrate, Stufe 2 – Module A–M

Stand: 6. Oktober 2026. Ergebnis des Auftrags „Modules/BuiltIn/ A–M“ aus `docs/TICKRATE-MIGRATION.md` §6 (Phase 20, Punkt 4 von `docs/HANDOFF-PHASE20.md`). Geltungsbereich: jede Datei unter `src/CnCNew.Simulation/Modules/` (mit `Modules/BuiltIn/`), deren Name mit A bis M beginnt – 25 Dateien in `BuiltIn/` (`AirModules.cs` bis `ModuleConfigReader.cs`), dazu `Modules/CollideModule.cs`, `ModuleBindings.cs`, `ModuleFramework.cs`. Klassen (A bis E) und Widersprüche (W1, W2) wie in `docs/TICKRATE-AUDIT.md`; die Konvention ist `docs/TICKRATE-MIGRATION.md` §1.

**Die eiserne Regel gilt:** bei 30 Hz ist jeder umgestellte Ausdruck der frühere (Faktor 1, `ToOriginalFrames` ist die Identität). Beleg: `reference-manifest check` und `input-manifest check` (§7).

„Bild“ = ein Logikbild des Originals (1/30 s). Referenzpfade relativ zu `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/Object/`.

## 1. Hilfsfunktionen (neu)

Neue Datei **`src/CnCNew.Simulation/Modules/BuiltIn/FrameGrid.cs`** (interne Erweiterungsmethoden auf `SimulationTime`, nur für Laufzeit-Arithmetik nach W1; nichts davon läuft je Tick im heißen Pfad, alles dort, wo eine Dauer entschieden wird):

| Funktion | Rechnung | 30 Hz |
|---|---|---|
| `time.Floor(ticks)` | `OriginalFrames(ToOriginalFrames(ticks))` – auf ganze Bilder abgerundet, in Ticks | `ticks` |
| `time.Ceiling(ticks)` | auf ganze Bilder aufgerundet, in Ticks | `ticks` |
| `time.AtLeastOneFrame(ticks)` | `Math.Max(ticks, OriginalFrames(1))` – die Untergrenze „ein Bild“ (`if (delay < 1) delay = 1`) | `Math.Max(ticks, 1)` |
| `time.DrawFrames(random, minTicks, maxTicks)` | ein Zufallszug über `ToOriginalFrames(min)…ToOriginalFrames(max)`, Ergebnis skaliert (ein Zug, derselbe Zufallsstrom bei jeder Rate) | `random.Range(min, max)` |
| `time.CountdownReload(ticks)` | `OriginalFrames(ToOriginalFrames(ticks) + 1) − TickOrder.Next` – Nachladewert eines Zählers der Form „> 0: −1, sonst handeln und auf N setzen“ (Periode N + 1 Bilder) | `ticks` |

Eine Ausnahme von „nicht je Tick“: `NeutronMissileSlowDeathBehavior.Update` ruft `Floor` einmal je Update (eine Ganzzahldivision je Tick, solange eine Neutronenrakete explodiert – wenige Sekunden, ein Objekt); `TensileFormationUpdate.Update` skaliert drei Konstanten je Update (drei Multiplikationen je rutschendem Felsen).

Die Datei liegt im Geltungsbereich (Name beginnt mit F), ist aber neu; die Aufträge N–Z, Welt und Skripte haben für dieselben Muster vermutlich eigene Schreibweisen – beim Zusammenführen entscheiden, ob `FrameGrid` nach `Time/` wandert. **Außerhalb des Geltungsbereichs wurde nichts geändert** (kein Zusatz an `SimulationTime`).

## 2. Umgestellte Stellen (46)

| Datei | Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|---|
| `AirModules.cs` | `ParkingPlaceBehavior.Update` (`_arrivals.RemoveAll`) | W2 (im Inventar B/literal) | `world.Tick - arrival.Tick > 2` | `> 2 * TickOrder.Next` – Reihenfolge: das Flugzeug entsteht im Tick des Ausgangs oder im nächsten | eigen |
| `MatchModules.cs` | `BaseRegenerateUpdate.HealRateFrames` | A | `const int HealRate = 3`, `return HealRate` | `HealRateFrames = 3`, `return world.Time.OriginalFrames(HealRateFrames)` | `Update/BaseRenerateUpdate.cpp`, `BaseRegenerateUpdate::update`: `const Int HEAL_RATE = 3`, `UPDATE_SLEEP(HEAL_RATE)` |
| `MatchModules.cs` | `BaseRegenerateUpdate.Update` (`amount`) | B | `… * Fixed.FromInt(HealRate) / TicksPerSecondFixed` | derselbe Ausdruck mit `Fixed.FromInt(OriginalFrames(HealRateFrames))` – Schlafdauer und Faktor über einen Wert | dieselbe Funktion: `amount = HEAL_RATE * maxHealth * m_baseRegenHealthPercentPerSecond / LOGICFRAMES_PER_SECOND` |
| `AirWeaponModules.cs` | `BunkerBusterBehavior.Update` | C | `world.Tick % CrashFxFrequencyTicks == 1` | `== world.Time.OriginalFrames(CrashFxPhaseFrames)` (Periode aus den Daten, Phase 1 Bild, beide skaliert) | `Behavior/BunkerBusterBehavior.cpp`, `update`: `getFrame() % m_crashThroughBunkerFXFrequency == 1` |
| `AirWeaponModules.cs` | `NeutronMissileSlowDeathBehavior.BeginSlowDeath` (Züge) | W1 | `LogicRandom.Range(0, (int)…VarianceTicks)` (zweimal) | `world.Time.DrawFrames(world.LogicRandom, 0, …VarianceTicks)` | `Behavior/SlowDeathBehavior.cpp`, `SlowDeathBehavior::beginSlowDeath`: `GameLogicRandomValue(0, d->m_sinkDelayVariance)` / `(0, d->m_destructionDelayVariance)` (im Inventar „Herkunft unbelegt“) |
| `AirWeaponModules.cs` | dieselbe Methode (`_destructionTick`) | W1 | `Math.Max(destruction, SleepNone)` | `world.Time.AtLeastOneFrame(destruction)` | eigen (Untergrenze) |
| `AirWeaponModules.cs` | `NeutronMissileSlowDeathBehavior.Update` (Blaststufen) | W1 | `Fixed.FromInt(world.Tick - _activationTick) > blast.DelayTicks` | vergangene Zeit auf ganze Bilder abgerundet (`world.Time.Floor(…)`, einmal je Update vor der Schleife), dann derselbe Vergleich: die Stufe fällt bei jeder Rate auf dieselbe Bildgrenze | `Update/NeutronMissileSlowDeathUpdate.cpp`, `update`: `currFrame - m_activationFrame > m_blastInfo[ i ].delay` |
| `AirWeaponModules.cs` | `MissileLauncherBuildingUpdate.SwitchTo` (`Opening`) | W2 (im Inventar A) | `AvailableOnFrame(world) - 1` | `- TickOrder.Next` („one frame ahead“ ist Reihenfolge) | `Update/MissileLauncherBuildingUpdate.cpp`, `switchToState`: `getReadyFrame() - 1` |
| `AirWeaponModules.cs` | dieselbe Methode (`Closing`, `half`) | W1 | `(AvailableOnFrame - now) / 2` | `OriginalFrames(ToOriginalFrames(AvailableOnFrame - now) / 2)` | dieselbe Funktion (Halbierung einer Bildzahl) |
| `AirWeaponModules.cs` | `SpectreData.Parse` (`HowitzerFiringRate`) | W1 | `Math.Max(Ticks(…, 10), 1)` | `configuration.Time.AtLeastOneFrame(Ticks(…, 10))` (Schutz des Modulo) | `Update/SpectreGunshipUpdate.cpp`, Konstruktor `m_howitzerFiringRate = 10` |
| `AirWeaponModules.cs` | `SpectreGunshipUpdate.Orbit` (`GattlingFiringWindowFrames`) | B | `world.Tick - gattling.LastFireTick <= 2` | `<= world.Time.OriginalFrames(GattlingFiringWindowFrames)` mit `= 2` | eigen (Ersatz für `OBJECT_STATUS_IS_FIRING_WEAPON`) |
| `BridgeModules.cs` | `BridgeScaffoldBehavior.AbsoluteMinSpeedPerFrame` | D (additiv) | `speed < 0.001` je Tick | `world.Time.PerOriginalFrameToPerTick(AbsoluteMinSpeedPerFrame)` an der Verwendung | `Behavior/BridgeScaffoldBehavior.cpp`, `update`: `if( speed < 0.001f ) speed = 0.001f` |
| `BridgeModules.cs` | `BridgeScaffoldBehavior._lateralSpeed/_verticalSpeed` | D (additiv) | Feldvorgabe `Fixed.One` je Tick | `DefaultSpeedPerFrame`; `OnSpawn` setzt beide auf `PerOriginalFrameToPerTick(DefaultSpeedPerFrame)` (Zustand bleibt „je Tick“, Schlüssel unverändert) | dieselbe Datei, Konstruktor: `m_lateralSpeed = 1.0f`, `m_verticalSpeed = 1.0f` |
| `CleanupAndSpyModules.cs` | `CleanupHazardUpdate.Update` / `FireWhenReady` (`_nextScan = data.ScanTicks`, zwei Stellen) | W1 (Zähler N + 1) | `_nextScan = data.ScanTicks` | `world.Time.CountdownReload(data.ScanTicks)` | `Update/CleanupHazardUpdate.cpp`, `update`: `m_nextScanFrames--` / `m_nextScanFrames = data->m_scanFrames` |
| `CleanupAndSpyModules.cs` | `CleanupHazardUpdate.FireWhenReady` (`RescanWaitMaxFrames`) | A + W1 | `_nextScan = LogicRandom.Range(0, 3)` | Zug in Bildern (`Range(0, RescanWaitMaxFrames)`), 0 bleibt 0, sonst `CountdownReload(OriginalFrames(n))` | dieselbe Datei, `fireWhenReady`: `m_nextScanFrames = GameLogicRandomValue( 0, 3 )` |
| `CleanupAndSpyModules.cs` | `SpyVisionUpdate.Update` (zwei Rückgaben) | W1 | `Math.Max(data.SelfPoweredIntervalTicks, SleepNone)` | `world.Time.AtLeastOneFrame(data.SelfPoweredIntervalTicks)` | eigen (Untergrenze) |
| `CleanupAndSpyModules.cs` | `AssistedTargetingUpdate.ReloadWaitFrames` | C | `const long ReloadWait = 1000` (Ticks) | `ReloadWaitFrames = 1000`, einmal je Update skaliert | eigen |
| `CrateModules.cs` | `PilotFindVehicleUpdate.Update` (`scan`) | C / W1 | `Math.Max(data.ScanTicks, SleepNone)` | `world.Time.AtLeastOneFrame(data.ScanTicks)` | `Update/PilotFindVehicleUpdate.cpp`: `ScanRate` über `INI::parseDurationUnsignedInt`, Konstruktor `m_scanFrames = 0` (im Inventar „Herkunft unbelegt“) |
| `DieAndUpdateModules.cs` | `TimedExpiryModule.OnSpawn` | W1 | `Range((int)_minTicks, (int)_maxTicks)`, `if (delay < 1) delay = 1` | `AtLeastOneFrame(DrawFrames(LogicRandom, _minTicks, _maxTicks))` | `Update/LifetimeUpdate.cpp`, `LifetimeUpdate::calcSleepDelay` |
| `DockModules.cs` | `AutoDepositUpdate.Update` (`_depositOnTick`) | C / W1 | `world.Tick + Math.Max(data.DepositTimingTicks, SleepNone)` | `world.Tick + world.Time.AtLeastOneFrame(data.DepositTimingTicks)` | `Update/AutoDepositUpdate.cpp`, `update`: `m_depositOnFrame = getFrame() + modData->m_depositFrame` (im Inventar „Herkunft unbelegt“; die Untergrenze ist eigen) |
| `FerryModules.cs` | `RailedTransportDockUpdate.Action` (`_pullPerTick`) | W1 (im Inventar E, Phase 20/23) | `Math.Max(data.PullInsideTicks, 1)` | `world.Time.AtLeastOneFrame(data.PullInsideTicks)` | `Update/DockUpdate/RailedTransportDockUpdate.cpp`: `m_pullInsideDistancePerFrame = mag / m_pullInsideDurationInFrames` |
| `FerryModules.cs` | `RailedTransportDockUpdate.UnloadNext` (`_pushPerTick`) | W1 | `Math.Max(data.PushOutsideTicks, 1)` | `world.Time.AtLeastOneFrame(data.PushOutsideTicks)` | dieselbe Datei: `m_pushOutsideDistancePerFrame = mag / m_pushOutsideDurationInFrames` |
| `FieldEffectModules.cs` | `TensileFormationUpdate.LifetimeFrames` | A | `const int Lifetime = 300`, `++_life > Lifetime` | `LifetimeFrames = 300`, `++_life > world.Time.OriginalFrames(LifetimeFrames)` (`_life` zählt weiter Updates = Ticks) | `Update/TensileFormationUpdate.cpp`, `TensileFormationUpdate::update`: `if (++m_life > 300)` |
| `FieldEffectModules.cs` | `TensileFormationUpdate.DormantCheckFrames` | A | `return 30` | `return world.Time.OriginalFrames(DormantCheckFrames)` | dieselbe Funktion: `return UPDATE_SLEEP(30)` |
| `FieldEffectModules.cs` | `TensileFormationUpdate.PropagatePeriodFrames/PropagatePhaseFrames` | A / C | `_life % 30 == 29` | `_life % OriginalFrames(30) == OriginalFrames(29)` | dieselbe Funktion: `if ( m_life%30 == 29 )` |
| `FlightModules.cs` | `JetSlowDeathBehavior.BeginSlowDeath` (`SinkDelayVarianceTicks`) | W1 | `LogicRandom.Range(0, (int)…)` | `world.Time.DrawFrames(…)` | `Behavior/SlowDeathBehavior.cpp`, `beginSlowDeath` (im Inventar „Herkunft unbelegt“) |
| `FlightModules.cs` | dieselbe Methode (`DestructionDelayVarianceTicks`) | W1 | `LogicRandom.Range(0, (int)…)` | `world.Time.DrawFrames(…)` | wie oben |
| `FlightModules.cs` | dieselbe Methode (`_destructionTick`) | W1 | `Math.Max(destruction, SleepNone)` | `world.Time.AtLeastOneFrame(destruction)` | eigen |
| `FlightModules.cs` | `CountermeasuresBehavior.ReportMissile` | B / W1 | `Math.Max(data.ReactionTicks, 1)` | `world.Time.AtLeastOneFrame(data.ReactionTicks)` | eigen (0 heißt „nicht gesetzt“) |
| `FlightModules.cs` | `DeliverPayloadAIUpdate.StartPhase` / `RunDelivering` (`_dropDelayLeft`, zwei Stellen) | W1 (Zähler N + 1) | `= data.DoorDelayTicks` / `= run.DropDelayTicks` | `world.Time.CountdownReload(…)` | `Update/AIUpdate/DeliverPayloadAIUpdate.cpp`: `m_dropDelayLeft = getDoorDelay()`, `--m_dropDelayLeft`, `m_dropDelayLeft = getDropDelay()` |
| `FlightModules.cs` | `DeliverPayloadAIUpdate.TurnTicks` | W1 | `Math.Max(1, (MinTurnRadius() / Speed).Ceiling())`, sonst `1` | `AtLeastOneFrame(Ceiling(…))` in Bildern aufgerundet, sonst `OriginalFrames(1)` | dieselbe Datei, `DeliverPayloadAIUpdate::calcMinTurnRadius` (`timeToTravelThatDist`) |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.PendingPathFrames` | B | `world.Tick - _pendingPathTick <= 3` | `<= world.Time.OriginalFrames(PendingPathFrames)` mit `= 3` | eigen |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Update` (`_nextScorch`) | W1 | `birth + (death - birth) * n / Total` | `birth + OriginalFrames(ToOriginalFrames(death - birth) * n / Total)` | `Update/ParticleUplinkCannonUpdate.cpp`, `update`: `nextFactor = (Real)m_scorchMarksMade / (Real)data->m_totalScorchMarks` (im Inventar „Herkunft unbelegt“) |
| `FlightModules.cs` | dieselbe Zeile (`death + 1`) | W2 | `death + 1` („nie wieder“) | `death + TickOrder.Next` | eigen |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Update` (`_nextPulse`) | W1 | `birth + (death - birth) * n / TotalDamagePulses` | wie `_nextScorch` | dieselbe Funktion: `m_nextDamagePulseFrame = orbitalBirthFrame + nextFactor * (orbitalDeathFrame - orbitalBirthFrame)` |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Update` (`_nextLaunchFx`) | C / W1 | `now + Math.Max(data.LaunchFxTicks, 1)` | `now + world.Time.AtLeastOneFrame(data.LaunchFxTicks)` | dieselbe Datei (`m_nextLaunchFXFrame`; Untergrenze eigen) |
| `HackerAndAssaultModules.cs` | `HackInternetAIUpdate.Varied` | W1 | `(Fixed.FromInt(ticks) * variation).Floor()` | in Bildern variiert und abgerundet, skaliert, dann `CountdownReload` | `Update/AIUpdate/HackInternetAIUpdate.cpp`, `UnpackingState::onEnter` / `PackingState::onEnter`: `m_framesRemaining = getUnpackTime() * variation` |
| `HackerAndAssaultModules.cs` | `HackInternetAIUpdate.CashDelay` und `_remaining = 0` beim Packen im Gebäude | W1 (Zähler N + 1) | `_remaining = CashDelay()` bzw. `0` | `world.Time.CountdownReload(…)` bzw. `CountdownReload(0)` | dieselbe Datei, `HackInternetState::update`: `if (m_framesRemaining > 0) m_framesRemaining--` |
| `JetAIUpdate.cs` | `TaxiAlongPath` (`FallbackTaxiSpeedPerFrame`) | D (additiv) | `taxi?.Speed ?? Fixed.One` | `?? world.Time.PerOriginalFrameToPerTick(FallbackTaxiSpeedPerFrame)` | eigen |
| `JetAIUpdate.cs` | `UpdateAttackLocomotor` | B / W1 | `Math.Max(data.AttackLocomotorPersistTicks, 1)` | `world.Time.AtLeastOneFrame(…)` (die Methode nimmt jetzt die Welt) | eigen (Referenz ohne Untergrenze) |
| `JetAIUpdate.cs` | `Land`, Hubschrauber (`MinHelicopterApproachSpeedPerFrame`) | D (additiv; im Inventar Teil der E-Zeile 776–800) | `Fixed.Max(flight.MinSpeed, Fixed.One)` | `Fixed.Max(flight.MinSpeed, PerOriginalFrameToPerTick(…))` | eigen |
| `JetAIUpdate.cs` | `Land`, Gleitflug (`MinLandingSpeedPerFrame`) | D (additiv) | `Fixed.Max(flight.MinSpeed, Fixed.One / Fixed.FromInt(2))` | `Fixed.Max(flight.MinSpeed, PerOriginalFrameToPerTick(MinLandingSpeedPerFrame))` | eigen |
| `JetAIUpdate.cs` | `BeginReload` (`total`) | W1 | `time * fehlend / ClipSize` | `OriginalFrames(ToOriginalFrames(time) * fehlend / ClipSize)` | `Update/AIUpdate/JetAIUpdate.cpp`, `JetOrHeliReloadAmmoState::onEnter`: `rt = (rt * needed) / clipSize` |
| `JetAIUpdate.cs` | `BeginReload` (`_reloadDoneTick`) | A | `Math.Max(total, 1)` | `world.Time.AtLeastOneFrame(total)` | dieselbe Funktion: `if (m_reloadTime < 1) m_reloadTime = 1` |
| `LastBlockerModules.cs` | `SupplyWarehouseCripplingBehavior.Update` (Rückgabe) | W1 (im Inventar E) | `Math.Max(_nextHealing - world.Tick, SleepNone)` | `world.Time.AtLeastOneFrame(_nextHealing - world.Tick)`; `_nextHealing` selbst bleibt unverändert (Zustand) | `Behavior/SupplyWarehouseCripplingBehavior.cpp`, `update`: `m_nextHealingFrame = now + md->m_selfHealDelay`, `UPDATE_SLEEP(m_nextHealingFrame - now)` |
| `MineModules.cs` | `DemoTrapUpdate.Update` (`_scanCountdown`) | W1 (Zähler N + 1) | `_scanCountdown = data.ScanTicks` | `world.Time.CountdownReload(data.ScanTicks)` | `Update/DemoTrapUpdate.cpp`, `DemoTrapUpdate::update`: `m_nextScanFrames--` / `m_nextScanFrames = m_scanFrames` |

Je Datei und Klasse (W1 = Rundung, Untergrenze, Zähler N + 1 oder Zufall zur Laufzeit; W2 = Reihenfolge):

| Datei | A | B | C | D | W1 | W2 | Summe |
|---|---|---|---|---|---|---|---|
| `AirModules.cs` |  |  |  |  |  | 1 | 1 |
| `AirWeaponModules.cs` |  | 1 | 1 |  | 5 | 1 | 8 |
| `BridgeModules.cs` |  |  |  | 2 |  |  | 2 |
| `CleanupAndSpyModules.cs` |  |  | 1 |  | 3 |  | 4 |
| `CrateModules.cs` |  |  |  |  | 1 |  | 1 |
| `DieAndUpdateModules.cs` |  |  |  |  | 1 |  | 1 |
| `DockModules.cs` |  |  |  |  | 1 |  | 1 |
| `FerryModules.cs` |  |  |  |  | 2 |  | 2 |
| `FieldEffectModules.cs` | 3 |  |  |  |  |  | 3 |
| `FlightModules.cs` |  | 1 |  |  | 9 | 1 | 11 |
| `HackerAndAssaultModules.cs` |  |  |  |  | 2 |  | 2 |
| `JetAIUpdate.cs` | 1 |  |  | 3 | 2 |  | 6 |
| `LastBlockerModules.cs` |  |  |  |  | 1 |  | 1 |
| `MatchModules.cs` | 1 | 1 |  |  |  |  | 2 |
| `MineModules.cs` |  |  |  |  | 1 |  | 1 |
| **Summe** | 5 | 3 | 2 | 5 | 28 | 3 | 46 |

Mehrfachklassen der Tabelle oben sind hier der jeweils führenden Klasse zugeordnet (A + W1 und C / W1 unter W1, A / C unter A, B / W1 unter W1). Die Commit-Nachricht der dritten Gruppe (`939e25b`) nennt für `FlightModules.cs` 10 statt 11 Stellen (die Zeile `death + TickOrder.Next` fehlt dort in der Zählung); maßgeblich ist diese Tabelle.

### Phasenlage der Zähler (bekannte Grenze)

`CountdownReload` ist für einen Zähler bemessen, der **in** einem Update nachgeladen wird (dann vergehen bis zur nächsten Handlung N + 1 Bilder). Wird der erste Zähler von außen gesetzt und läuft das Update noch im selben Tick (Hacker, der zwischen zwei Schritten ins Gebäude geht), endet er bei 60 Hz einen Tick, bei 120 Hz bis zu drei Ticks später als die Bildgrenze – ein einmaliger Versatz unter einem Bild; die Periode danach ist exakt. Der Test `TickRateModulesAmHackerCountdown` hält genau das fest.

## 3. Bewusst unverändert, mit Grund

**Mit Stufe 1 erledigt (nachgeprüft, nichts zu tun):** `ParkingPlaceBehavior.HealRateFrames` und die Heilmenge; `EmpData` `Lifetime` (Ersatzwert 1 Bild), `LeafletData` `Delay` (1), `BunkerBusterData` `CrashThroughBunkerFXFrequency` (4), `SpectreData` `HowitzerFiringRate` (10) und `StrafingIncrement`, `PropagandaTowerData` `DelayBetweenUpdates` (100), `MinefieldData` `CreatorDeathCheckRate`; `BridgeData` `Lateral/VerticalScaffoldSpeed` (`/ TicksPerSecondFixed`); `TransportContain` (Regeneration), `PropagandaTowerBehavior.Give`, `MinefieldBehavior.Update` (`DegenPerSecond`), `JetAIUpdate.CircleDeadAirfield` (Schaden und `HOW_OFTEN_TO_CHECK` über `world.Time.TicksPerSecond`), `CountermeasuresBehavior.LaunchVolley` (Schwelle und Ersatzwert je Sekunde), `ParticleUplinkData` `ManualDrivingSpeed`/`ManualFastDrivingSpeed`, `ParticleUplinkCannonUpdate.Pulse` (`seconds`), `DockMachine.ClearanceTimeoutFrames`, `ModuleConfigReader.Ticks`, `JetAIUpdate.PauseBeforeTakeoff` (`TickOrder.Next`).

**Geprüft, nicht gekoppelt bzw. Reihenfolge ohne Literal:**

| Datei | Symbol | Grund |
|---|---|---|
| `AirWeaponModules.cs` | `AirWeaponModules.FixedTicks` (`Blast…Delay`) | ungerundete Dauer wie im Original (`parseDurationReal`); die Rasterung geschieht jetzt im Vergleich (§2) |
| `AirWeaponModules.cs` | `NeutronMissileData.MaxTurnRate`-Ersatzwert | in Stufe 1 bewusst unverändert (jeder Wert ab π rastet sofort ein) |
| `AirWeaponModules.cs` | `SpectreGunshipUpdate.Orbit` (`world.Tick % HowitzerFiringRateTicks == 0`) | Periode aus den Daten (skaliert), Phase 0: bei jeder Rate derselbe Spielzeitpunkt |
| `CleanupAndSpyModules.cs` | `SpyVisionSpecialPower.Apply` | Summe und Deckel von Dauern, die alle auf dem 30er-Raster liegen: bei 60 Hz exakt das Doppelte (die Vermutung des Inventars „kleinerer Rundungsfehler“ gilt mit der Rasterrundung der Stufe 1 nicht mehr) |
| `CleanupAndSpyModules.cs` | `SpyVisionUpdate.ActivateSpyVision`, `AssistedTargetingUpdate.Update` (`Math.Max(…, SleepNone)`) | `SleepNone` ist hier der kleinste Schlaf des Planers (ein Aufwachen muss in der Zukunft liegen), keine Dauer – Reihenfolge |
| `CombatModules.cs` | `FireWeaponUpdate.Update` | fragt jeden Tick ab; die Schusszeitpunkte sind absolute Ticks aus Daten – nur Last |
| `ContainChainModules.cs` | `RequestPayload`/`EnterPayload`, `OverlordContain.Update` | „ein Tick später“ ohne Literal (Reihenfolge) bzw. Nachführen je Tick – nur Last |
| `ContainModules.cs` | `GarrisonContain.HealOne` | `MaxHealth / TimeForFullHealTicks` je Tick: skaliert mit der umgerechneten Dauer von selbst |
| `DockModules.cs` | `DockUpdateBase.Update`, `AutoDepositUpdate.OnSpawn` (`Math.Max(_depositOnTick, Tick + SleepNone)`) | Abfrage je Tick ohne Literal; kleinster Schlaf des Planers |
| `DockMachine.cs` | `ProcessDock` mit `ActionDelay` 0 | ruft die Aktion des Docks jeden Tick; was die Aktion je Aufruf bewirkt, liegt in den Dock-Modulen (N–Z: Reparatur, Versorgung) – dort bewerten |
| `HackerAndAssaultModules.cs` | `AssaultTransportAIUpdate.Update` | schläft nie, keine Tickliterale – nur Last und Reaktionslatenz |
| `JetAIUpdate.cs` | `Update` (`return SleepNone`) | die Zustandsmaschine läuft jeden Tick; ergebnisrelevant nur über die Integration (Klasse E, §4) |
| `LastBlockerModules.cs` | `SupplyWarehouseCripplingBehavior.OnDamage` (`Math.Max(SuppressTicks, SleepNone)`) | kleinster Schlaf des Planers |
| `MatchModules.cs` | `BaseRegenerateUpdate.OnDamage` (`Math.Max(BaseRegenDelayTicks, SleepNone)`) | kleinster Schlaf des Planers |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Initiate` (`Math.Max(world.Tick, 1)`) | 0 heißt „kein Einsatz“; Wächter, keine Dauer |
| `FlightModules.cs` | `DeliverPayloadAIUpdate.Strafe` | fordert je Update einen Schuss an; den Takt erzwingt die Waffe (`FireCurrentWeaponAtPosition`, Welt) – nicht in dieser Datei zu lösen |
| `FlightModules.cs` | `ParticleUplinkData` (`long BeginChargeTicks = 0` usw. als `record`-Vorgabe) | Vorgabe 0, kein stiller Tickwert; nur `Parse` und Tests erzeugen den Datensatz |
| `Bodies.cs`, `BuiltInModules.cs`, `CreateCrateDie.cs`, `JetAIUpdate.Flight.cs` (bis auf §4), `ModuleBindings.cs`, `CollideModule.cs`, `ModuleFramework.cs` | – | keine Tickwerte: Zufall über Anteile und Indizes, Bindungstabellen, Schnittstellen (`IsSignificantlyAboveTerrain` ist seit Stufe 1 ratenfest, `AdvanceConstruction` liegt in der Welt) |

**Für Phase 21 (Verbraucher außerhalb der Simulation):** `SabotageMilitaryFactoryCrateCollide.Sabotage`, `SabotagePowerPlantCrateCollide.Sabotage` (`CrateModules.cs`) und `SabotageInternetCenterCrateCollide.Sabotage` (`LastBlockerModules.cs`) geben die Dauer als Tickzahl im Ereigniswert nach außen; `TensileFormationUpdate.Life` zählt Ticks (der Leser `UnitConditionTracker.TensileFreefallLife` in `CnCNew.Presentation` muss mit `Time.OriginalFrames(100)` vergleichen).

## 4. Ergänzungen zur Liste für Phase 23

Bei 30 Hz richtig, bei 60 Hz falsch; in Phase 20 unverändert (Klasse E und multiplikative Faktoren). Die schon in `docs/TICKRATE-MIGRATION.md` §5 stehenden Zeilen (`NeutronMissileData.ForwardDamping`/`RelativeSpeed`, `TensileFormationUpdate.Friction`/`NewShare`/`LinkShare`/`BaseSlope`, `SmartBombHomingData.CourseCorrectionScalar`) sind hier nicht wiederholt.

| Datei | Symbol | Was bei 60 Hz anders ist |
|---|---|---|
| `AirWeaponModules.cs` | `EmpUpdate.Update` (`_scale`, Faktor 0,05 je Tick) | die Skalierung nähert sich dem Ziel in der halben Zeit (Zustand und Darstellung, keine Spielwirkung) |
| `AirWeaponModules.cs` | `LeafletDropBehavior.Update` | das erste Update kommt einen Tick nach dem Entstehen: ein `Delay` von einem Bild (zwei Ticks) ist dann noch nicht abgelaufen, das Modul schläft für immer statt jeden Tick zu wirken. Naheliegende Lösung: das erste Aufwachen auf `OriginalFrames(1)` legen |
| `AirWeaponModules.cs` | `NeutronMissileUpdate.Update`, `DoAttack` | Zwischenpunkt „erreicht“ gegen den Tickschritt, Start kostet einen Tick, Euler-Schritt mit Dämpfung |
| `AirWeaponModules.cs` | `SpectreGunshipUpdate.Fly`, `Strafe`, `Orbit` (`_okToFireHowitzerCounter`) | Polygonzug der Bahn, Ankunftsradius ein Tickschritt, Zähler je Strafe-Tick gegen eine Dauer in Ticks (zeitrichtig nur, solange jeden Tick gestrafft wird) |
| `BattlePlanModule.cs` | `BattlePlanUpdate.Update` | ein Zustandsübergang je Tick: die Zusatzticks zwischen den Phasen halbieren sich zeitlich |
| `BridgeModules.cs` | `BridgeScaffoldBehavior.Update` | Abbremsrampe proportional zum Restweg je Tick (geometrisches Abklingen) |
| `FerryModules.cs` | `RailedTransportDockUpdate.DoPullInDocking`, `DoPushOutDocking` | halbe Schrittweite, Ankunftsschwellen 6 bzw. 3 Einheiten fallen auf andere Ticks |
| `FieldEffectModules.cs` | `TensileFormationUpdate.Update` (Physikblock) | Euler-Schritt mit Reibung und Mischung je Tick; seit dieser Stufe dauert die Rutschphase bei jeder Rate 300 Bilder, die Rekursion läuft bei 60 Hz also doppelt so oft |
| `FlightModules.cs` | `JetSlowDeathBehavior.Update` (Fall), `ParachuteContain.Update` (freier Fall, Sinkflug, Ankunftsradius eine Tickstrecke), `CountermeasuresBehavior.MoveFlares` | halbimplizite Euler-Schritte, lineares Bremsen je Tick |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.MoveBeam` (manuell) | Wegpunktwechsel bei Restweg ≤ eine Tickstrecke |
| `HackerAndAssaultModules.cs` | `HackInternetAIUpdate` – erster Zähler nach einem Befehl von außen | Versatz unter einem Bild (§2, „Phasenlage der Zähler“) |
| `JetAIUpdate.cs` | `TaxiAlongPath` (Ankunftsradius `max(Speed, 1)`), `TurnTo`, `TakeOff` (beide Zweige), `Land` (beide Zweige; Aufsetzschwelle 1 Einheit, Sinken `max(SpeedLimitZ, Speed/4)`), `ControlHeight` (`Speed/8` als Beschleunigung, `Speed/2` als Deckel) | Euler-Schritte und Schwellen gegen den Tickschritt; `Speed/8` mischt eine Größe je Tick mit einer je Tick² |
| `JetAIUpdate.Flight.cs` | `CircleStep` | die eingeschwungene Kreisbahn (gemessen ≈ 0,93 · Radius) hängt an der Schrittweite |
| `ContainModules.cs` | `GarrisonContain.HealOne` | Fixed-Rundung der kleineren Scheibe `MaxHealth / Ticks` (Summe weicht in den letzten Bits ab) |
| `MineModules.cs` | `MinefieldBehavior.Update` | Schaden je Tick löst den Abgleich der virtuellen Minen in feineren Schritten aus |
| `BridgeModules.cs`, `JetAIUpdate.cs` | `PerOriginalFrameToPerTick(…)` an der Verwendung | bei 60/120 Hz eine `Fixed`-Division je Update (bei 30 Hz keine Rechnung). Für Phase 22/23: einmal je Welt vorhalten, falls es im Profil auftaucht |

## 5. Tests

Neue Datei `tests/CnCNew.Simulation.Tests/Tests.TickRate.ModulesAM.cs`, vier Tests (ein Block in `Program.cs`), jeder mit der synthetischen Fixture bei 30, 60 und 120 Hz:

- **`TickRateModulesAmBaseRegenerate`** – Heilschübe 30, 33, 36 … Ticks nach dem Treffer bei 30 Hz, exakt das Zwei- und Vierfache bei 60/120 Hz, jeder Schub heilt 12,5 Punkte bei jeder Rate.
- **`TickRateModulesAmLifetimeAndDeposit`** – zwölf Lebensdauern (333…4777 ms) fallen bei jeder Rate auf dasselbe Bild; ein Posten mit `DepositTiming = 0` zahlt einmal je Bild, der mit 1000 ms bei 30/60/90 Bildern.
- **`TickRateModulesAmHackerCountdown`** – Auszahlung bei 30 Hz alle 16 Ticks, bei 60/120 Hz alle 32/64; der einmalige Versatz des ersten Zählers ist kleiner als ein Bild.
- **`TickRateModulesAmTensileFormation`** – die Prüfung des ruhenden Felsens fällt auf Bild 120 nach seinem ersten Update, die Rutschphase dauert 300 Bilder.

Die bestehenden Tests (Tickerwartungen bei 30 Hz) sind unverändert.

## 6. Suchmuster des Inventars (§8) über die Dateien dieses Auftrags

Am Ende erneut gelaufen (ohne Kommentarzeilen):

| Muster | Treffer | Bewertung |
|---|---|---|
| P01, P02, P03, P07, N05 | 0 | – |
| P04 | 6 | `AirModules` Ersatzradius 30 (Länge), zwei Kommentare, `ClearanceTimeoutFrames`, `DormantCheckFrames`, `PropagatePeriodFrames` (Konstanten in Bildern) |
| P05 | 25 | 11 Konstanten in Bildern (`…Frames`), der Rest Zustands- und Größenkonstanten (`Pristine` … `MaxLinks`, `OneFrame`) |
| P06, N08 | je 12 | dieselben 11 Konstanten in Bildern und die `record`-Vorgaben `… = 0` von `ParticleUplinkData` |
| P08 | 3 | `2 * TickOrder.Next` (Reihenfolge), `OriginalFrames(GattlingFiringWindowFrames)`, `FireWeaponUpdate` (Katalogwerte) |
| P09 | 6 | sechsmal `return 0` (Geldbeträge, Flags) |
| P10, P11 | 49, 13 | Umrechnungen beim Parsen mit Ersatzwert in Bildern (Stufe 1) bzw. Rohwerte je Sekunde/Anteile; `RelativeSpeed` steht auf der Liste §5 der Migration |
| P12 | 2 | `speed *= 1/2` (Schubhalbierung der Neutronenrakete, Teil von `DoAttack`, §4) und ein Schadensskalar |
| P13, N14 | 1, 23 | kein Zug über Tickbereiche mehr (Anteile, Winkel, Indizes, Streuung; der Bildzug `RescanWaitMaxFrames`) |
| N16 | 2 | `AffectRadius` 60 (Länge), `LifetimeFrames = 300` |

## 7. Nachweise

Siehe Abschlussbericht des Auftrags (Manifest `quick` nach dem Umbau, `full` am Ende, Eingabemanifest, Tests Debug und Release `--long`, sauberer Build, Probelauf bei 60 Hz).

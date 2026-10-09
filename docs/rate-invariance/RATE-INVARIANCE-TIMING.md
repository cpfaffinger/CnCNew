# Rate invariance, package TIMING: "one tick where the original means one frame"

Phase 23, step 3 (`docs/handoffs/HANDOFF-PHASE23.md`), starting state `7b54374` (7 October 2026). This document belongs to one of six parallel packages and lists only its
causes: `docs/TICKRATE-MIGRATION.md` §5.3 without the rows of the turrets and of the firing counter, the rows from §5.4 assigned to the package and the associated new findings of the
rate-equivalence suite (`docs/SIMULATION-CORE.md` §39). The shared documents are not touched; what is stated here is to be added there when merging.

Everything is measured with `SimTool rate-compare --group <group>` (Release, real data `game/content`, catalogues without model geometry) and with the tests in
`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Timing.cs`. Times in seconds, always in the order 30 / 60 / 120 Hz. **No human has seen the game at 60 Hz.**

## 1. Result

| Group | Quantities | outside at 60 Hz, before → after | at 120 Hz, before → after | Cases with a failure, before → after |
|---|---|---|---|---|
| `overtime` | 418 | 80 → **7** | 84 → **7** | 32 → 5 |
| `economy` | 81 | 22 → **9** | 25 → **11** | 18 → 6 |
| `timed` | 383 | 53 → **27** | 56 → **31** | 33 → 16 |
| `weapons` | 1,474 | 43 → **32** | 57 → **46** | 27 → 26 |

"Before" is the run at the state `7b54374` (identical to the first complete run of the suite, §39.6), "after" the run at the state `eca983f`. At 30 Hz nothing has changed:
`input-manifest check` and `reference-manifest check --set quick` identical after every commit, `--set full` at the end (section 8).

What still fails in the four groups belongs, apart from three named individual cases (section 5), to other packages: ground movement (gatherers, the way of the dozer to the building site, the tank
on the way to the mine), turrets (first shot, sustained-fire shot count, fire wall), projectiles ("first shot until first damage" at 120 Hz), helicopter crash, toppling, staged
capture, train and the healing in slices per tick.

## 2. The principle as it was applied here

The scheduler stays tick-accurate: `SimulationWorld.RunUpdates` still raises a sleep below one tick to one tick, `Register` wakes a module without a wish of its own in the next
tick, `ProcessSpawns` and the phase order of `Step` are unchanged. A module that moves or integrates has to run every tick and begin with the tick after its creation
(then its state at every frame boundary is the 30 Hz state). A rule in the scheduler that puts "SleepNone" or "first update" on one frame for all modules would have slowed down exactly
these modules. What was sought was a formulation without module knowledge in the scheduler; there is none, because the same return value (`SleepNone`) and the same first wake time mean
"next sub-step" for an integrator and "next frame" for an event module. **The solution is therefore per module**, in three forms:

1. **First update one frame after the creation** (`world.Tick + world.Time.TicksPerOriginalFrame` instead of `+ SimulationTiming.SleepNone` in `OnSpawn`) for modules whose first update
   decides an event. At 30 Hz the same tick.
2. **"N frames minus one tick" becomes "N frames minus one frame":** the step in which a command is executed stands for a whole frame at 30 Hz. Above 30 Hz exactly
   this first step is credited with the remainder of its frame (sale, production, construction, phases of the hacker). Every further step is one tick.
3. **A delay of zero behind a per-tick query is the next frame:** new helper `SimulationTime.ZeroIsNextFrame(ticks)` (`src/CnCNew.Simulation/Time/FrameGrid.Timing.cs`):
   a delay not equal to zero stays, zero stays zero at 30 Hz (the stored point in time keeps its value, the next attempt *is* the next frame) and becomes one frame above that.
   Lower bounds "one tick" (`Math.Max(…, SleepNone)`) have become `AtLeastOneFrame` where they mean one frame.

An event thereby falls on the **first** tick of the frame in which it falls at 30 Hz – that is the time convention of the suite ("what happens in the step of the command has the time 0").
An integration that is subdivided per tick, in contrast, reaches its frame value only with the **last** tick of the frame. Both at once cannot be measured exactly equal; that
concerns the healing in slices per tick (section 5, open).

## 3. Causes, one commit per row

| §5 entry | Finding (30 / 60 / 120 Hz before) | Correction (file, symbol, principle) | after | Commit |
|---|---|---|---|---|
| §5.3 `Register` (first update one tick after appearing) – field and stealth | Poison field, first damage 0.0333 / 0.0167 / 0.0083 s (42 quantities, plus 17 "death of a victim"); stealth after the creation 2.0333 / 2.0167 / 2.0083 s (9 of 14) | `CombatModules.cs` `FireWeaponUpdate.OnSpawn`, `Phase5Modules.cs` `StealthUpdate.OnSpawn`: first update one frame after the creation (form 1) | 0.0333 and 2.0333 s at every rate; `overtime` 80/84 → 21/25, `timed` 53/56 → 44/47 | `9f61b3d` |
| – (measurement of the suite) | Self-healing of the mine 14.9333 / 14.9500 / 14.9583 s, base regeneration 2.9667 / 2.9833 / 2.9917 s, medic/hospital/repair bay 0.9333 / 0.9500 / 0.9583 s, speaker tower 1.9000 / 1.9167 / 1.9250 s; in `MeasureDetector` the same error of the simulation as in the stealth cases was hidden | `RateCases.OverTime.cs` `MeasureRecovery`: the time of a "frame with healing" is the first step of the frame, not the last; `RateCases.Timed.cs` `MeasureDetector`: `Align` before removing the detector. **Measurement only**, no tolerance and no case touched (section 4) | all named quantities equal at every rate; `overtime` 21/25 → 7/7, `timed` 44/47 → 52/55 (8 detector cases now show the real difference) | `a6fdfea` |
| §5.3 `StealthDetectorUpdate` with `DetectionRate = 0`; §5.3 `StealthUpdate.MarkDetected` | "detector gone until hidden again" 0.5000 / 0.4833 / 0.4750 s (16 cases after the corrected measurement): the mark lasted `DetectionRate` + **one tick** | `Phase5BlockerModules.cs` `StealthDetectorUpdate.Update`: mark `DetectionRate` + one frame (`markAsDetected(m_updateRate + 1)`, `StealthDetectorUpdate.cpp`, `update`), sleep `AtLeastOneFrame`; `Phase5Modules.cs` `StealthUpdate.MarkDetected`: lower bound one frame. Two named exceptions of the source rule "one tick minimum" are dropped | 0.5000 s at every rate (also 0.6 and 1.5 s of the other detectors); `timed` 52/55 → 36/39 | `f922979` |
| §5.3 `_saleDurationTicks` | Sale 5.9667 / 5.9833 / 5.9917 s (3 of 3) | `SimulationWorld.cs` (constructor): 180 frames minus one **frame** (form 2). The three-rate test of Phase 20 pinned the old form (179 / 359 / 719 ticks) and now expects 179 / 358 / 716 | 5.9667 s at every rate; `economy` 22/25 → 19/22 | `404fab6` |
| new (suite): production | Ranger 4.9667 / 4.9833 / 4.9917 s (7 of 7; the 4 airfield productions of the group `flight` have the same cause) | `Phase5Modules.cs` `ProductionUpdate.Enqueue`: the entry that begins an empty queue is credited with the remainder of the first frame (form 2); a waiting entry is unchanged (it already took the full build time at every rate) | 4.9667 s, second unit 9.9667 s at every rate; `economy` 19/22 → 12/15 | `43d4a30` |
| new (suite): construction | Power plant 15.2333 / 15.2500 / 15.2500 s; the work itself took build time minus one tick | `SimulationWorld.Construction.cs` `AdvanceConstruction`: the first work step counts a whole frame (form 2), the health follows the work | Work at every rate build time minus one frame (test: 59 frames at 2 s). In the suite: USA barracks 14.1000 at every rate; power plant 15.2333 / 15.2333 / 15.2250, China power plant 15.0333 / 15.0167 / 15.0167, GLA barracks 14.6333 / 14.6167 / 14.6167 – the quantity contains the way of the dozer, which arrives up to one tick earlier above 30 Hz (ground movement, §5.2); `economy` 12/15 → 12/14 | `89283a0` |
| §5.3 `HackInternetAIUpdate` – first counter after a command from outside | Hacker pays 8.1000 / 8.1167 / 8.1250 s, all further payments offset by the same tick | `HackerAndAssaultModules.cs` `Varied`, `OrderMove`: a phase that a command starts (unpacking, packing) lasts its frames – the update of the same tick already counts; only the counter that the update itself reloads (money cadence) lasts frames + 1 (`CountdownReload`) | 8.1000 / 10.1333 / 26.4000 s at every rate; `economy` 12/14 → 9/11 | `938a32c` |
| new (suite): charging from the creation for six superweapons | 120.0000 / 120.0167 / 120.0083 s. **What distinguishes them from the 62 other powers:** their carrier has `SpecialPowerCreate`, which restarts the charging in the first update (one tick after the creation). The suite reads one frame after the creation: at 30 Hz before, at 60/120 Hz after this restart | `SpecialPowerModules.cs` `BuildCompleteModule.OnSpawn` (`SpecialPowerCreate`, `GrantUpgradeCreate` and the other create modules): `onBuildComplete` one frame after the creation (form 1) | equal at every rate (the restart lies one frame after the creation at every rate: ready `ReloadTime` + one frame after that); `timed` 36/39 → 30/33 | `4e83582` |
| §5.3 `BaseRegenerateUpdate.OnDamage`, `SupplyWarehouseCripplingBehavior.OnDamage` (`Math.Max(…, SleepNone)`) | with a delay of 0, healing one tick instead of one frame after the hit (retail has no delay of 0: suite unchanged) | `MatchModules.cs`, `LastBlockerModules.cs`: `AtLeastOneFrame`; the two named exceptions of the source rule are dropped | Test (`BaseRegenDelay` 0): hit until first regeneration one frame at every rate; `overtime` 7/7 → 7/7 | `c6ce657` |
| new (suite): reload 0 | Battleship guns: reload 0.0333 / 0.0167 / 0.0083 s, shots in 60 s 110 / 112 / 112, damage +1.9 % | `SimulationWorld.WeaponSlots.cs` `ConsumeShot`, `CombatModules.cs` `FireWeaponUpdate.Update`: `ZeroIsNextFrame` on the reload time (form 3); `WeaponInfo.ClipReloadWith` unchanged | Reload 0.0333 s, 110 shots, 20th shot 10.1667 s at every rate; `weapons` 43/57 → 32/46 | `798d263` |
| §5.3 `BattlePlanUpdate.Update` (one state transition per tick) | a state with the duration 0 (`TransitionIdleTime` 0 in the retail strategy centre) is left by the next update: one tick instead of one frame. Plan change until the new plan on the fixture 2.0333 / 2.0167 / 2.0083 s (from the source, not measured) | `BattlePlanModule.cs` `SetStatus`: the three delays via `ZeroIsNextFrame` (form 3) | Test: 30 frames until the first plan, 61 frames for a change, at every rate (measured); the suite does not stage battle plans (`timed` 30/33 → 30/33) | `529d514` |
| §5.3 `LeafletDropBehavior.Update`; §5.4 `FireWeaponWhenDamagedBehavior`; plus `HeightDieUpdate`, `TensileFormationUpdate` (suite) | Leaflet bomb with the default `Delay` of one frame: takes effect after 0.0333 s / never / never (above 30 Hz the module slept forever). Avalanche notices the damage 0.9667 / 0.9500 / 0.9417 s. Bomb gas of the Aurora on the ground: "kill until removal" 0.0000 / – / – (above 30 Hz already removed before the kill) | `AirWeaponModules.cs`, `Phase12Modules.cs`, `ToppleAndFallModules.cs`, `FieldEffectModules.cs`: one line each, first update one frame after the creation (form 1; for `FireWeaponWhenDamagedBehavior` also after the upgrade) | Leaflet 0.0333 s at every rate (test), avalanche 0.9667 s, bomb gas 0.0000 s at every rate; `timed` 30/33 → 28/31 | `68ae9c3` |
| §5.3 `FirestormDynamicGeometryInfoUpdate` | the way there takes `TransitionTime` (it begins in the update that ends the delay) and was equal at every rate; **only the way back** (`ReverseAtTransitionTime`) takes `TransitionTime` + one update. Fixture: creation until finished 2.1333 / 2.1167 / (2.1083 from the source) s | `WorldEffectModules.cs` `Update`: the counter of the way back begins one frame minus one tick below zero (0 at 30 Hz), the geometry rests on the end value for that long | 2.1333 s at every rate (test); the firestorm case of the suite measures damage and already passed (`timed` 28/31 → 28/31) | `47c760c` |
| §5.4 `MinefieldBehavior` (sampling per tick), individual case scatter mine | Scatter mine removed after 4.8000 / 4.7000 / 4.6667 s: the threshold distance `RepeatDetonateMoveThresh` was compared per tick and found exceeded after a shorter distance | `MineModules.cs` `OnCollide`: a unit on the mine is only considered again in whole frames after its first contact; the phase within the frame is in the state per unit (0 at 30 Hz, only written if not equal to 0). **Escape "original grid"** (section 6) | 4.8000 / 4.8000 / 4.8417 s; the 5 ticks at 120 Hz are the arrival of the tank (first damage 3.3667 / 3.4000 / 3.4083 s, ground movement); `timed` 28/31 → 27/31 | `eca983f` |

Tests: `test:` commit at the end (section 7).

## 4. The corrected measurement (`a6fdfea`)

Two measurements of the suite themselves added an offset below one frame, contrary to §39.1 ("the suite itself adds no offset below one frame"):

- **`MeasureRecovery`** compares the health after the last step of a frame and entered this step as the time. A healing in the first tick of the frame – that is, exactly on
  the grid – appeared one tick too late at 60 Hz and three ticks too late at 120 Hz. The time is now the first step of the frame (the same step at 30 Hz). With that,
  self-healing, base regeneration and the heal pulses of medic, hospital, repair bay and speaker tower are equal at every rate: **the simulation was never different there**
  (the rows "self-healing" and "base regeneration" of cause 7 in §39.6 were a measurement error).
- **`MeasureDetector`** removed the detector after a loop that begins one step after the scan: one frame at 30 Hz, above that one tick after the last scan. The
  offset just cancelled the real error (mark lasts rate + one tick); with `Align` before the removal eight detector cases showed the same failure as the eight stealth cases, and
  `f922979` fixes all sixteen.

No tolerance, no kind of quantity and no case is changed.

## 5. Open, deliberately left, only reported

**Deliberately unchanged (changes no result or is subdivision):**

- §5.3 `SimulationWorld.RunUpdates`, `Register`, `ProcessSpawns`, `Step` (phase order): the scheduler stays tick-accurate (section 2). No remaining failure of the four groups
  depends on a spawn chain or on the phase order.
- §5.4 `MinefieldBehavior.Update`: the wasting away after the death of the builder is a health amount per tick (subdivided integration).
- §5.4 `ProductionUpdate.Update` (querying the door per tick): the door delay is counted in scaled ticks from an exit on the grid, the per-tick query finds the door free in the same
  tick as at 30 Hz. Load only. ("Both units stand" is equal at every rate.)
- §5.4 `FireWeaponWhenDamagedBehavior.Update`: query per tick, the ready ticks lie on the grid. Load only (the first update is corrected, `68ae9c3`).
- §5.4 `FloatUpdate`: sets the height to the water level per tick. Load only.
- §5.4 `CheckpointUpdate`: two searches per tick, the gate step is scaled per tick (subdivision). Load only.
- §5.4 `FireOCLAfterWeaponCooldownUpdate`: counts a shot in the tick after it and fires the list as soon as the weapon was ready and did not shoot – both are ordering, the same at every
  rate; the lifetime is computed in frames. Load only.

**Open, in the groups of this package, but not its cause:**

- **Healing in slices per tick** (§5.6 `RepairDockUpdate`, `HealContain`, §5.2 `TunnelContain`, plus `PropagandaTowerBehavior`): "frames with healing" 150 / 151 / 151 (repair dock),
  75 / 76 / 76 (tunnel), 750 / 751 / 751 (speaker tower); "full health" 26.8667 / 26.9000 / 26.9000 s. Two reasons: the slice per tick is rounded on its own (the sum
  stays below the frame value by fractions, the last slice is missing), and a subdivided integration reaches the frame value only with the last tick of the frame, while the suite
  carries the quantity as `ExactTime`. This becomes exactly equal only if the frame amount is credited once per frame (escape "original grid" for healing) – a decision that
  does not lie in this package. The healed amount in 60 s is equal at every rate.
- **Sustained fire, one shot more in 60 s** (toxin truck 899 / 900 / 900, Gattling tank 440 / 441 / 441, Gattling cannon 886 / 887 / 887): the shot interval is exactly equal; the
  **first** shot falls earlier above 30 Hz (toxin truck: command until first shot 0.0667 / 0.0500 / 0.0417 s – one frame plus one tick instead of two frames), and thus one shot
  more fits into the window of 60 s. Cause in `World/SimulationWorld.Turrets.cs` (§5.3 `StepTurrets`: the additional step of holding/aligning is one tick, not one frame), package TURRETS.
  Not in `FiringTracker.cs`/`Acquisition.cs` and not in the shot interval.
- **Fire wall of the Dragon Tank** 838 / 899 / 899 shots (30 Hz loses 61 shots in 60 s, about one per sweep): `World/SimulationWorld.Turrets.cs`. The weapon sweeps
  (`TurretFireAngleSweep = SECONDARY 60`, `TurretSweepSpeedModifier 2.0`); `IsAligned` only releases a sweeping weapon as long as the deviation is **smaller** than the sweep angle
  (`difference >= sweep` holds back). At 30 Hz the turn step is larger than the reversal threshold of 0.035 rad: the turret snaps exactly onto the end angle at the end of the sweep
  (deviation = sweep angle), and the shot of this tick is dropped. At 60 and 120 Hz the turret reverses within the threshold before it snaps and never stops. This is
  §5.2 `AimStep`/`TurnAngle` (snapping below one tick step, reversal at 0.035 rad) together with `IsAligned`, package TURRETS. Concluded from source and numbers (61 dropped shots at
  about one sweep per second), not verified by a trace. The damage (14,605.5 / 14,250.5 / 14,253.0) follows from that and from the fire-wall segments.
- Ground movement, projectiles, helicopter crash, toppling, capture, train: unchanged, other packages.

**Three individual cases, only reported:**

- **`CINE_BShipWeaponDetonationObject` never ends at 30 Hz – an error at 30 Hz, not at 60 Hz.** The object has `LifetimeUpdate` 1 ms (one frame) and `SlowDeathBehavior` with
  `DestructionDelay` 1 ms. In the tick after the creation both modules are due (the `SlowDeathBehavior` only because `Register` wakes every module without a wish of its own in the next tick).
  `LifetimeUpdate` kills, `BeginSlowDeath` requests `WakeAt(…, Tick + 1)`; `SimulationWorld.WakeAt` discards exactly this request ("waking an awake module for the next tick
  must not postpone the call of this tick"), the update of the `SlowDeathBehavior` runs in the same tick, sees the delay not reached and returns `SleepForever`. The object
  stays forever. At 60/120 Hz the requested time is not "the next tick", the request takes hold, the object ends after 0.0667 s. The correction (`SlowDeathBehavior.Update` returns
  the remainder until the delay instead of sleeping forever) **changes the behaviour at 30 Hz** and is therefore not done; whether it changes the manifest is not checked.
- **Empty parachute (`AirF_AmericaCrateParachute`), "kill until removal" 3.0000 / 2.9833 / 2.9750 s:** `ParachuteContain.Update` kills a parachute without an occupant in the first
  update, one tick after the creation; the 4 s of the slow death run from there (the kill of the suite hits an already dying object). `FlightModules.cs` belongs to the package
  Flight, and the module integrates the fall per tick – postponing the first update would postpone the fall. Proposal: only the self-kill one frame after the creation
  (wait for `world.Tick < Entity.SpawnTick + TicksPerOriginalFrame`); the same tick at 30 Hz.
- **Mob member without nexus (`Demo_GLAInfantryAngryMobMolotov02`), 7.0000 / 6.9833 / 6.9750 s:** `MobMemberSlavedUpdate.Update` kills a member without a master in the first update, one
  tick after the creation. The module counts its checks in updates from the first (which is right with the tick after the creation); only the self-kill needs the same
  waiting condition as above. `SlaveModules.cs`, not in this package.

**Modules whose first update still comes one tick after the creation** (set in `OnSpawn` itself; not checked individually, the suite shows no failure on them in the
groups of this package): `ParkingPlaceBehavior`, `BunkerBusterBehavior`, `EmpUpdate`, `MissileLauncherBuildingUpdate`, `SpectreGunshipUpdate`, `BattlePlanUpdate`,
`CleanupHazardUpdate`, `OverlordContain`, `RiderChangeContain`, `GarrisonContain`/`HealContain`, `TransportContain`, `PilotFindVehicleUpdate`, `AutoDepositUpdate`,
`RailedTransportAIUpdate`, `RailedTransportDockUpdate`, `PropagandaTowerBehavior`, `ParachuteContain`, `AssaultTransportAIUpdate`, `JetAIUpdate`, `SmartBombTargetHomingUpdate`,
`BaseRegenerateUpdate`, `MinefieldBehavior`, `GenerateMinefieldBehavior`, `QueueProductionExitUpdate`, `PointDefenseLaserUpdate`, `RailroadBehavior`, `AutoFindHealingUpdate`,
`RebuildHoleBehavior`, `MobMemberSlavedUpdate`, `SpawnBehavior`, `GrantStealthBehavior`, `ProjectileStreamUpdate`, `WanderAIUpdate`, `FloatUpdate`, `ChinookAIUpdate`,
`TunnelContain`, `BattleBusSlowDeathBehavior`, `DynamicShroudClearingRangeUpdate`, `FirestormDynamicGeometryInfoUpdate`, `WaveGuideUpdate`, `WeaponBonusUpdate` – and every module
without a wish of its own (`Register`), among them `AIUpdateInterface`, the projectile modules and `PhysicsBehavior`. Among them are integrators (for which the next tick is right), counters
that count in updates from the first update (for which it is likewise right: N updates end N ticks after the creation), and event modules whose case the suite does not stage.
Rule of thumb for new modules: if the first update decides an event, it comes one frame after the creation.

## 6. Uses of the escape "original grid" (Ausweg „Originalraster“)

| Place | What runs on the grid | Justification |
|---|---|---|
| `Modules/BuiltIn/MineModules.cs`, `MinefieldBehavior.OnCollide` | the comparison "has the unit moved by `RepeatDetonateMoveThresh` since the last detonation", once per frame from the first contact of this unit (the first contact itself counts in its tick) | the original compares once per frame; the comparison cannot be subdivided, because the threshold stands against the distance *since the last comparison* – a finer cadence quantises the distance more finely and detonates faster. Reaction to a collision, neither movement nor command acceptance nor target acquisition. The phase depends on the unit, not on the world tick, so that a different grid of the collision report does not make the mine mute |

Not a use of the escape, but related to it: `StealthDetectorUpdate` with `DetectionRate = 0` now scans once per frame instead of once per tick (lower bound `AtLeastOneFrame`,
the usual rule from Phase 20; retail has no detector with rate 0).

## 7. Tests

`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Timing.cs`, nine tests on the synthetic fixture, each at 30, 60 and 120 Hz, expectation in original frames:
first update (field, stealth with and without delay, leaflet bomb), detector mark, production and construction, hacker, `onBuildComplete`, reload 0 (weapon slots and `FireWeaponUpdate`,
plus `ZeroIsNextFrame` itself), regeneration delay 0, battle plan, firestorm. The mine is checked only via the suite (a test would need a rate-invariant drive).

Adjusted expectations of existing tests (class T, `docs/TICKRATE-MIGRATION.md` §15.2, and tests that pinned the old behaviour at 60/120 Hz):

| Test | Change |
|---|---|
| No. 1 "Phase 5: StealthDetectorUpdate marks and expires DETECTED" | three frames instead of three ticks until the stealth |
| No. 3 "Construction: snapshot mid-construction and mid-sale, replay round trip" | command ticks, end and checkpoint interval of the replay in frames |
| No. 2 "Phase 5: reference match scenario, replay v2 factions, determinism" | pinned to 30 Hz, with a justification in the test |
| "Tick rate, world: a sale takes …" | 179 / 358 / 716 ticks instead of 179 / 359 / 719 |
| "Tick rate, modules A–M: the avalanche's dormant check …" | the first update lies one frame after the creation |
| "Tick rate, modules A–M: HackInternetAIUpdate …" | the first payment lies on the 30 Hz frame (offset 0 instead of "below one frame") |
| "Construction: a dozer builds a structure over BuildTime ticks" | 59 frames plus the first step |
| "Construction: sale takes 179 ticks …" | `Frames(179)` |
| "Special powers: shared timers, create hooks …" | `GrantUpgradeCreate` one frame after the creation |
| `Tests.SourceRules.cs` | four named exceptions of the rule "one tick minimum" removed (the expressions no longer exist) |

## 8. Check runs

| Run | Result |
|---|---|
| `input-manifest check` | identical, after each of the 13 simulation commits and at the end |
| `reference-manifest check --set quick --jobs 3` | 9 runs identical, after each of the 13 simulation commits and after the commit of the measurement |
| `reference-manifest check --set full --jobs 4` | **50 runs identical**, at the state `eca983f` (the last commit that changes the simulation; the test and the documentation commit after it do not change it) |
| `dotnet build CnCNew.slnx` | 0 warnings, 0 errors |
| Simulation tests, Release, without `--content-dir`, 30 Hz | all 427 green (418 existing, 9 new) |
| the same with `--tick-rate 60` | 6 failures (before 6, the same: flash count of the staged capture, four projectile trajectories, rate literal of the turret turn rate) |
| the same with `--tick-rate 120` | 12 failures (before 14): no. 1, 2 and 3 from `docs/TICKRATE-MIGRATION.md` §15.2 pass; the remaining eleven (no. 4–14) are unchanged; **new:** "Skirmish: two AIs play a match" ends without a winner (also with a budget scaled to the same game time) – a whole match, class C, not converted and not attributed to any single cause |
| Group `flight` of the suite (not in this package) | 21 / 21 → 17 / 17: the four production times of the airfields are equal with `43d4a30` |

Not measured: the cost per tick (no performance run; the changes lie in `OnSpawn`, in the queuing, in the first work step, when emptying a clip and in one
comparison per collision report of a mine, not in the per-tick path of a module – except `Math.Max(_timeActive, 0)` in the firestorm and the choice of the work step in
`AdvanceConstruction`), the groups `locomotion`, `projectiles`, `turrets`, `scripts` and `ai` of the suite, campaign and AI runs at 60 Hz. The result of the Debug run of the
simulation tests at 30 Hz is in the final report of the package.

## 9. New findings

- Two measurements of the suite had an offset of their own (section 4); self-healing and base regeneration were never different.
- `CINE_BShipWeaponDetonationObject`: an object that never ends at 30 Hz (section 5) – `SimulationWorld.WakeAt` discards a wake request for the next tick if the module is still due in the
  running tick, and `SlowDeathBehavior.Update` then sleeps forever. Concerns every object with a slow death of one frame that begins in the tick after the creation.
- The fire wall loses shots at the end of the sweep at **30 Hz** (section 5); which behaviour is that of the original is not checked.
- `FirestormDynamicGeometryInfoUpdate`: only the way back was shorter, not the transition as such (§5.3 names both).
- The sustained-fire shot count is a window edge: whoever corrects the first shot fixes all 14 cases at once.
- An event on the first tick of a frame and an integration subdivided per tick cannot both be measured `ExactTime`-equal (section 2 and 5, healing).

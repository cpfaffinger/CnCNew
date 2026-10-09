# Tick-rate coupling – audited inventory

As of 6 October 2026, audited at commit `e5046b7` (`main`). This document replaces the unaudited initial inventory (commit `2a66661`) and is the working basis for Phases 20, 21 and 23 of the programme in `docs/MULTIPLAYER-PLAN.md`. It is a working document: every site names file, line **and symbol** (line numbers shift with every commit, the symbol stays findable).

> **Status after Phase 20 (6 October 2026): the simulation part of this inventory is worked off.** The tables below describe the state `e5046b7` *before* the conversion and are not carried forward; line numbers and many symbol names of the simulation are no longer right (`…Ticks` constants are called `…Frames`, `SimulationTiming.LogicFramesPerSecond` no longer exists). Authoritative for the simulation is `docs/TICKRATE-MIGRATION.md`: the convention (§1), the time contract (§2), the converted sites (§3, Appendix A), the list for Phase 23 (§5), the list for Phase 21 (§6), the rules in `SourceRules` and the state of the search patterns from §8 (§10 there) and the trial run at 60 and 120 Hz (§11 there). For `CnCNew.Game`, `CnCNew.Presentation`, `CnCNew.Ui`, the client, the tools and the tests (§6, §7) this document remains the working basis (Phase 21).
>
> | Cl. | Simulation (660 sites in §0.1) | State after Phase 20 |
> |---|---|---|
> | A (39) | Original constants in frames | **converted:** each one is a `const …Frames` and is scaled through `Time.OriginalFrames` (stage 1: the `const` expressions with the rate; stage 2: 18 sites as A, more under W1/W2). **Exception:** `TurretInfo.NeverScanTicks` (sentinel, never scaled) |
> | B (267) | Time values | **converted:** the mechanically counted conversions and the 15 fallback values go through the time service (stage 1); 31 literals and deadlines in frames (stage 2). Rule values of the option records are frames under a historical name (`docs/TICKRATE-MIGRATION.md` §8.1) |
> | C (81) | Execution cadences | **converted** where there was a literal (16 sites of stage 2, period and phase in frames). **List for Phase 23:** evaluation and sampling per tick without a literal (25 rows, §5.4 there). **Exception:** the schedule of the stress test counts steps (§8.3 there) |
> | D (43) | raw data per frame | **converted:** additive raw values (9 at the catalog build in stage 1, 6 in the body in stage 2). **List for Phase 23:** multiplicative factors and mixed recursions (11 rows, §5.1 there). **Exceptions:** `WeaponInfo.InstantSpeed`, the fallback value of `NeutronMissileData.MaxTurnRate` (sentinel) |
> | E (138) | non-invariant integration | **converted:** order literals with `TickOrder.Next` (W2: 3 in stage 1, 21 in stage 2, 1 in the completion step) and roundings, lower bounds, counters and randomness at run time on the grid (W1: 88 in stage 2, 3 in the completion step). **List for Phase 23:** integration per tick (27 rows), counters N + 1 and one-tick offsets (18), randomness per tick (4), remainders (10) |
> | F (12) | Values of the simulation for the presentation | unchanged in ticks of the rate of the world; **list for Phase 21** (§6 there, nos. 5, 6, 11) |
> | G (15) | Formats | **converted** (stage 1): snapshot, mission state and replay carry the rate, the world is created with the stored rate, an unsupported or foreign rate is rejected; no version increase |
> | H (65) | Drivers and scenarios in `Sim\Testing` | **converted:** time values in frames (8 sites, among them the 34 cooldowns of the objective driver at one site). **Exception by rule:** run budgets (`MaxTicks`, `--ticks`) remain ticks |
>
> The numbers of the right column are those of the result reports of Phase 20 (sites by their count); they cannot be mapped row by row onto the 660 sites of this inventory – the inventory counts, e.g., every use of a fallback value and every integration block, the reports count symbols. Such a mapping was not created.
>
> **Correction of two numbers (§2), recounted in Phase 20 at the state `e5046b7`:** the simulation had **41** direct calls of the `SimulationTiming` conversions in 12 files and **133** calls of `ModuleConfigReader.Ticks` in 33 files, together 174 – not "173 calls" and "132". The number of mechanically counted conversions derived from it (150, column B) is accordingly too small by one; the tables were not recalculated.

> **Status after Phase 21 (6 October 2026): the part outside the simulation is worked off** (§6 game layer, presentation, UI, client; §7.1 tools; §7.2/§7.3 tests only partly). The tables in §6 and §7 describe the state `e5046b7` *before* the conversion and are not carried forward; line numbers and some symbol names are no longer right (`MuzzleFlashTicks` is called `MuzzleFlashFrames`, `HealingIconTicks` `HealingIconFrames`, `CheerTicks` `CheerFrames`, `SelectionFlashTracker.DecayTicks` `DecayFrames`, `SoundCueLimits.WindowTicks`/`MaxPerTick` `WindowFrames`/`MaxPerFrame`, `LiveMission.ClientPresentationLatencyTicks` `…Frames`, `DemoScenario.Ticks` `Frames`, `ScenarioOrder.Tick` `Frame`; `InGameUiSettings.LogicFramesPerSecond`, `SuspensionMotion.LogicTicksPerSecond`, `LiveGame.TickSeconds`/`MaxTicksPerFrame` and `SimulationWorld.VisionIntervalTicks` no longer exist). Authoritative is `docs/TICKRATE-MIGRATION.md` §14.
>
> | Subtype of class F | State after Phase 21 |
> |---|---|
> | `tick` (simulation tick as the clock of the display) | **converted:** durations in original frames through `world.Time`, sequences per frame on the original-frame grid, display through the game clock `LiveGame.Clock` (`docs/TICKRATE-MIGRATION.md` §14.1). **Open:** the raw tick number in the savegame lists and in `MatchResult.EndTick` (not displayed) |
> | `originalbild` (fixed 1/30 s on wall-clock time) | unchanged on wall-clock time; the 30 is everywhere `CnCNew.Data.OriginalFrame.PerSecond` (camera, command markers, shell transitions, water, cabin damping). The frame numbers of the transition effects (`End = 30` etc.) are numbers of frames, not rates, and stay |
> | `wand` | `LiveGame`: step and catch-up limit from the rate of the world |
>
> Classes A to E and H outside the simulation: the rows marked "21" are converted or named as open in `docs/TICKRATE-MIGRATION.md` §14.3/§14.8; the test rows of the classes `zeit` and `budget` (§7.2) are **not** (§14.5 there). A source-code test (`tests/CnCNew.Game.Tests/SourceRuleTests.cs`) pins the rule "no rate constant except `OriginalFrame.PerSecond`" for the four projects.

**What "audited" means here (§1):** Every site of the tables in §5 to §7 was read in the source code with its surroundings (the whole method, not the hit line) and assigned to exactly one class. The simulation, the game layer and the presentation were read completely file by file; UI, audio, client, data, tools and tests hit-driven. What was not read is in Appendix B with a number and nowhere else. **No source code was changed and nothing was executed at 60 Hz** – every statement "at 60 Hz …" is inferred from the code, not measured. Measured are the counts of this document and the quantisation on the real data (§4.4).

Abbreviations: `Sim\` = `src/CnCNew.Simulation/`, `Mod\` = `Sim\Modules/BuiltIn/`, `Pres\` = `src/CnCNew.Presentation/`, `Game\` = `src/CnCNew.Game/`, `Ui\` = `src/CnCNew.Ui/`, `Client\` = `src/CnCNew.Client.Stride/`, `Data\` = `src/CnCNew.Data/`. "frame" = one logic frame of the original (1/30 s), "tick" = one step of the simulation.

## 0. Summary for Phase 20/21/23

### 0.1 Numbers

1959 coupled sites in `src`, `tools` and `tests` (counting rule §1.2). Column B contains 150 mechanically counted conversion calls (§2), everything else is individually read sites.

| Project | A | B | C | D | E | F | G | H | Total |
|---|---|---|---|---|---|---|---|---|---|
| `CnCNew.Simulation` | 39 | 267 | 81 | 43 | 138 | 12 | 15 | 65 | 660 |
| `CnCNew.Game` | 7 | 15 | 6 | 4 | 3 | 30 | 1 | 0 | 66 |
| `CnCNew.Presentation` | 2 | 8 | 7 | 20 | 21 | 44 | 0 | 3 | 105 |
| `CnCNew.Ui` | 0 | 3 | 0 | 5 | 0 | 21 | 0 | 0 | 29 |
| `CnCNew.Client.Stride` | 0 | 0 | 0 | 2 | 0 | 2 | 0 | 10 | 14 |
| `CnCNew.Audio` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `CnCNew.Data` | 0 | 0 | 0 | 14 | 0 | 0 | 0 | 0 | 14 |
| other src | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| tools | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 55 | 55 |
| tests | 0 | 0 | 0 | 14 | 0 | 0 | 0 | 1002 | 1016 |
| **Total** | **48** | **293** | **94** | **102** | **162** | **109** | **16** | **1135** | **1959** |

Classes: **A** original constant in frames; **B** time value; **C** execution cadence; **D** raw data per frame; **E** non-invariant integration; **F** presentation clock; **G** format; **H** test or tool. Definitions in §1.1. `CnCNew.Audio` has no coupling to the tick (no reference to the simulation, no constant of 30); "other src" are `CnCNew.W3D`, `Archives`, `Content`, `Formats`, `Images`, `Import`, `IO` – nothing there (the W3D animation clock computes in seconds with the frame rate from the file). `CnCNew.Data` contains no 30 Hz conversion; the 14 sites there are fields that are typed only as `Real`/`Integer` although the original applies them per frame – the same fields seen from the schema side that appear once more as D or E at their consumer in the simulation (no additional work, but the place where a field type "per frame" is missing).

In addition 222 stored state fields with absolute ticks in 56 files (§4.3, Appendix C), 5198 audited, not coupled hit lines (Appendix A) and 1908 not audited hit lines (Appendix B).

### 0.2 The ten riskiest sites

Ranked by "changes game results and cannot be solved by scaling alone". Every site was re-read by the coordinator himself.

1. **Ground movement and winged flight** – `AIUpdateInterface.Move`, `MoveWinged`, `SteerWinged`, `MoveStraight` (`Mod\Phase5Modules.cs`, lines 1059–1253): arrival radius `max(CloseEnoughDist, Speed)` (one tick of travel), Euler step for turning, acceleration and braking distance, replanning `world.Tick - _blockerReplanTick >= 30`, look-ahead `Speed * 25`; in addition `SimulationWorld.AvoidLocalBlockers` (look-ahead exactly one tick of travel, `Sim\World/SimulationWorld.Collision.cs`) and the jet kinematics (`Mod\JetAIUpdate.cs`: literals 1 and 0.5 per tick, `Speed/8` as acceleration). Class E; hits every unit in every tick.
2. **Weapon cadence at run time** – `WeaponInfo.ScaledByRateOfFire` and `PreAttackDelayWith` (`Sim\Templates/WeaponCatalog.cs`, lines 80–118) truncate `Ticks / Multiplikator` at run time; `WeaponCatalog.Build` sets missing or 0 ms delays between shots to one tick (`Math.Max(SleepNone, delay)`); `SimulationWorld.FlightTicks` (`Sim\World/SimulationWorld.Projectiles.cs`, line 125) hits "immediately" below one tick of travel. On the real data the one-tick lower bound affects 118 of 363 weapons (§4.4). **This contradicts the plan statement that the balance is the same at every rate (§0.3 W1).**
3. **Projectiles** – `DumbProjectileBehavior.CalcFlightPath`/`Update` (one path point per tick, detonation one tick after the last point), `MissileAIUpdate.Launch` (weapon speed per tick used as acceleration) and `Move` (arrival at remaining distance ≤ one tick of flight) in `Mod\ProjectileModules.cs`; `NeutronMissileUpdate` (`Mod\AirWeaponModules.cs`, `ForwardDamping`/`RelativeSpeed` raw per tick); `SmartBombTargetHomingUpdate` (`Mod\LastBlockerModules.cs`, position blend with `CourseCorrectionScalar` 0.99 per tick).
4. **Script VM** – `ScriptEngine.Update` evaluates every non-delayed script every tick (counters that a script increases per frame run twice as fast at 60 Hz); `SET_TIMER`/`SET_RANDOM_TIMER` (`Sim\Scripting/ScriptEngine.Actions.cs`, lines 50–59) and `*_FOR_FRAMECOUNT`, `teamInitialIdleFrames`, `CAMERA_FADE_*`/`CAMERA_BW_MODE_*` consume frame numbers of the map raw; `SecondsToFrames` computes at run time with the constant; the first evaluation of delayed scripts depends on `NameHash % (2 * 30 + 1)` (`ScriptEngine.cs`, line 178). Classes C, D and E; affects every campaign map.
5. **Effects on the simulation tick** – `LiveGame.SyncAfterTick` (`Game\Live/LiveGame.cs`, lines 203–212) calls `FxExecutor.ExecuteEvents`/`Update` and with it `ParticleWorld.Step` exactly once per tick; the particle data are authored per frame (multiplicative damping, gravity per frame², `BurstDelay` 0 = one emission per step). At 60 Hz every effect runs twice as fast. Class F/E.
6. **Turrets** – `SimulationWorld.Turrets.cs`: `TurnAngle`/`TurnPitch`/`AimStep`/`IsAligned` (snapping within one tick step, fire release against a fixed 0.035 rad), in addition the own literals `TurretAimGraceTicks = 15`, `TurretReloadHoldCapTicks = 450`, `TurretLoseInterestTicks = 45` and the original constant `TurretSweepEnableTicks = 3`; `TurretInfo.DefaultRate = 0.01` rad per frame for turrets without a rate of their own.
7. **Raw data per frame with multiplicative or mixed effect** – `RailroadBehavior` (`Mod\RailroadModules.cs`: `SpeedMax`, `Acceleration 1.01`, `Braking 0.99`, `Friction 0.97`, literals 0.02/0.05/0.1, damage `Speed * 10` per tick), `TensileFormationUpdate` (`Mod\FieldEffectModules.cs`: lifetime 300, friction 0.95, 0.93/0.07 per tick), `HelicopterSlowDeathBehavior` (`Mod\TransportModules.cs`: `SpiralOrbitForwardSpeedDamping`), `StructureToppleUpdate`/`ToppleUpdate` (`Mod\ToppleAndFallModules.cs`: `StructuralDecay`, `InitialVelocityPercent`, one weapon shot per tick in `ApplyCrushingDamage`), `BattleBusSlowDeathBehavior` (`Mod\VehicleModules.cs`, line 178: `ThrowForce / mass` as speed per tick – unchanged, the bus flies about four times as high at 60 Hz). Taking the root alone is not enough for the mixed recursions (§0.3 W4).
8. **Counters with period N + 1, randomness over tick ranges, tick-hashed phases** – "set to N, −1 per tick, act in the tick after 0" in `PointDefenseLaserUpdate` (search and shot), `AutoFindHealingUpdate`, the hacker money cycle, `DemoTrapUpdate`, `HordeUpdate`, `DynamicShroudClearingRangeUpdate`, `EnemyNearUpdate`: with a scaled N the period becomes 2N + 1 instead of 2N + 2. Durations are drawn at run time with `LogicRandom.Range(minTicks, maxTicks)` over tick bounds (`SlavedUpdate`, `StructureCollapseUpdate`, `FireSpreadUpdate`, `SlowDeathBehavior`, `LifetimeUpdate` …): drawn values then no longer lie on the 30 Hz grid. `SimulationWorld.MoodCheckOffset` (`Sim\World/SimulationWorld.Acquisition.cs`, line 236) and the idle sweep of the turrets hash the tick number.
9. **The presentation generates commands at the tick cadence** – `StandingOrders.Update`/`UpdateAttack` (`Pres\Input/StandingOrders.cs`, lines 77–152) runs through `GameInputController.Tick` ← `LiveGame.BeforeTick` (`Game\Hud/MatchController.cs`, line 114) before **every** simulation tick and submits `FireWeaponCommand`, `PursueCommand` and `SetMoveTargetCommand` (one fire command per tick in which the unit may fire); `IssuedTick + 1` assumes "takes effect one tick later". In addition the mission director: `DurationTicks`, `DefaultPlaceholderTicks`, `ClientPresentationLatencyTicks` (`Game\Shell/MissionPresentation.cs`, `Game\Live/LiveMission.cs`) feed `CompletePresentation` and with it script conditions. The cadence changes results and the replay content, not only the load (§0.3 W5).
10. **Own cadences that determine results** – `SkirmishAi` (`Sim\Skirmish/SkirmishAi.cs`: `ThinkInterval = 15`, `FireInterval = 5`, seven named deadlines and 13 literals in the body, all checked only on think ticks; `< 450` and `< 60` with three failed attempts change the behaviour permanently), `SimulationWorld.VisionIntervalTicks = 3` with `tick % 3` in `Step` (also in `LiveGame`, line 211), `ScriptEngine.RetargetInterval = 30`, and the drivers of the reference runs in `Sim\Testing` (`MoveCadence = 15`, `IncomeInterval = 300`, `spacing = 15` of the objective driver with 34 cooldowns in ticks): reference match, stress test and `drive` change at 60 Hz even when the rules are right.

### 0.3 Contradictions and gaps relative to `MULTIPLAYER-PLAN.md` §2.5 and the handoffs 20/21/23

- **W1 – "Times from the data stay on the 30 Hz grid, the balance is the same at every rate" (plan §2.5, handoff 20 decision 2, handoff 23 tolerance "times from data exactly equal") holds only for the catalog rounding.** Not covered are (a) roundings at run time in ticks of the running rate: `WeaponInfo.ScaledByRateOfFire` (`floor(Ticks / Multiplikator)`, in the original `REAL_TO_INT_FLOOR` in `WeaponTemplate::getDelayBetweenShots`), `PreAttackDelayWith`, `HealContain` (`MaxHealth / Ticks`), blast delays with unrounded ticks; (b) lower bounds "at least one tick" (`Math.Max(…, SleepNone)`: delay between shots, `AutoHealBehavior`, `AutoDepositUpdate`, `FlammableUpdate`, `SlowDeathBehavior` …); (c) counters with period N + 1 (rank 8); (d) durations that are rolled at run time over tick ranges. For all four, Phase 20 must lay down that computation is in **original frames** and only the result is multiplied by rate/30 – otherwise rates of fire with bonus, healing rates and damage rates shift. Whether and how much rate-of-fire bonuses deviate on the real data is not measured.
- **W2 – "No bare tick literal remains in the simulation" (handoff 20 decision 4, item 7 `SourceRules`) needs a fourth category: tick adjacency.** Several literals mean not time but order ("in the same tick", "one tick later"): `UNIT_EMPTIED` (`frame - 1`), `_transferTick = now + 1 / + 2` in `JetAIUpdate`, `FramesUnpulledLongEnoughToUnhitch = 2`, `StandingOrders` `IssuedTick + 1`, `StealthUpdate` `LastFireTick == world.Tick`, `ConditionDeriver` `lastFire == world.Tick - 1`, the quick-victory delay 1, the "carriages exist only in the next tick" of the train. They must **not** be scaled. They are in the tables as class E with the note "order".
- **W3 – Compile-time constants.** 13 `const` expressions and one `static readonly` compute with `LogicFramesPerSecond` (`DefaultMoodCheckTicks`, `DefaultMaxLifespanTicks`, `DockMachine.ClearanceTimeoutTicks`, `SlavedUpdate.UpdateRate`/`RecentFireTicks`, `SpawnBehavior.UpdateRate`, `ParkingPlaceBehavior.HealRateTicks`, `PreAttackStaleTicks`, `SupplyScanFrames`, `HelicopterSlowDeathData.FramesPerSecond`; outside the simulation `LiveGame.TickSeconds`, `LiveMission.ClientPresentationLatencyTicks`, `MissionPresentation.DefaultPlaceholderTicks`/`RadarBlipLifetimeTicks`). They are compiled into the assemblies, also across assembly boundaries; handoff 20 names only the catalog. In addition 31 further lines of the simulation compute by hand with the constant instead of through the conversions of `SimulationTiming`: 19 divisions `/ Fixed.FromInt(LogicFramesPerSecond)` in modules (part of them in every tick at run time) and 12 additions, multiplications and modulo in modules, script VM, turret catalog and firing stages; `TemplateCatalog.GravityPerTickSquared` computes at every access.
- **W4 – "Multiplicative factors need only square roots" (plan §2.5, handoff 23 step 3) is right only for pure factors.** Mixed recursions – train (`Speed = (Speed + 0.02) * 1.01`), tensile formation (blend 0.93/0.07 plus friction plus Euler), MOAB target homing (position blend 0.99), helicopter spiral, particles (damping and gravity in the same step) – do not yield the same path with the root of the factor. Here the closed form remains, or the escape "original grid" (Originalraster). The factors come partly from the data (`ForwardDamping`, `SpiralOrbitForwardSpeedDamping`, `CollapseDamping`, `StructuralDecay`, `VelocityDamping` of the particles) and are typed only as `Real` in `CnCNew.Data`.
- **W5 – "The presentation does not change the simulation" (handoff 21) is not true today.** `StandingOrders` (project `CnCNew.Presentation`) submits commands at the tick cadence (rank 9); the mission director reports completions to the script VM (known in the plan as E21). Phase 21 cannot convert these two "frame-identically" without touching simulation results; they belong in Phase 20 (cadence in original frames) or Phase 24 (command path). For the network path this also means: an attacking squad today generates up to one command per unit and tick.
- **W6 – Scripts: more than the frame counters.** Handoff 20 names `SET_TIMER` and `*_FOR_FRAMECOUNT`. Found in addition: the evaluation itself (every script every tick, "original grid" would be the obvious way here), `teamInitialIdleFrames` (`ScriptProgram.cs`), the frame numbers passed through to the presentation (`CAMERA_FADE_*`, `CAMERA_BW_MODE_*`), `SecondsToFrames` as a run-time conversion of its own next to `SimulationTiming`, the phase offset `% (2 * Rate + 1)`, `ProcessTeamBuilds` (one queue command per order and tick) and `SupplyScanFrames` (10 s, although the original adds `SCAN_RATE = 10` in frames).
- **W7 – "Exactly one named constant for the original frame" (handoff 21) – today there are at least eight independent 30s:** `FxValueReader.FramesPerSecond`, `RtsCamera.FrameRate`, `CameraSettings.FramesPerSecond`, `OrderHintBook.ClientFramesPerSecond`, `WindowTransitionHandler.FramesPerSecond`, `InGameUiSettings.LogicFramesPerSecond`, `Suspension.LogicTicksPerSecond`, `TransitionScenes.Frame`; in addition literals (`3 * 30`, `30 * 10`, `0.002f * 30f`, `deltaSeconds * 30.0`, `LetterboxFadeFrames = 30`, three `End = 30`). Two of them are in truth simulation ticks (`InGameUiSettings.LogicFramesPerSecond` through `MissionText`/`SuperweaponTimers`, `Suspension.LogicTicksPerSecond`), the others are correct original-frame units on wall-clock time and must **not** be tied to the tick rate (listed in §6 as F/originalbild).
- **W8 – Formats.** Handoff 23 step 8 names `maxTicks`, `presentationLatencyTicks`, `combatIntervalTicks`. `MissionOptions.CombatIntervalTicks` is only a switch any more (do not scale). Not named: `SaveGame` (`CnCNew.Game`, version 1) writes and checks **no** rate and shows the raw tick in the load list; the replay header carries `EndTick` and `CheckpointInterval` in ticks; `ReplayRunner.Run(replay, world, options)` does not check the rate at all; commands serialise `tick`, `delay` and `repeat`; catalog values in ticks are copied into the state (`runDropDelay`, `healthPerTick`, speeds per tick – 39 fields "per tick", Appendix C), so a stored state is readable only with its rate. The code is at replay format **11**; `CLAUDE.md` still names 10.
- **W9 – Tests and tools keep running at 60 Hz but check something else.** Tests and tools step throughout with the literal `1.0 / 30` (103 occurrences in 18 files; `ScenarioAudit` with `1.0 / 30 + 1e-6`); because `LiveGame.Advance` accumulates seconds, two ticks then run per step, and assumptions "one step = one tick" (`ScenarioAudit`, HUD and mission tests) become silently wrong instead of red. Phase 21 should derive the step from the rate **before** the rate is switched.
- **W10 – Numbers in the register (plan §4).** E5: 61 → 63 uses of the constant in the simulation, "about 110 bare tick numbers" → 159 in the simulation without `Testing` plus 17 cadences and 65 driver values in `Sim\Testing`. E10: "about 845 test lines" → 1002 sites in 82 of 119 test files. State fields "about 111 in 41 files" → 222 in 56 files.

### 0.4 Side findings (independent of the rate, noticed while reading, not pursued)

Deviations from the original that the rate-equivalence suite of Phase 23 will show as failures although they are not rate errors: `StealthData.MoveThresholdSpeed` is read raw and compared with the speed per tick (original: `parseVelocityReal`); `BridgeData` falls back to 1 per second (original 1.0 per frame); `AutoHealBehavior` falls back to 0 when `HealingDelay` is missing (original `UINT_MAX`; on the real data each of the 848 modules has the key); `GrantStealthData.RadiusGrowRate` and `StructureToppleData.StructuralDecay` have other default values than the constructors of the original; `ForceIdleTicks = 2` for 67 ms where the rounding up of the original would give 3 frames; `SUB_FROM_MSEC_TIMER` rounds differently from the original; `SUPPLY_SOURCE_SAFE` without the 2 s cache of the original; `GroupDoubleTapMilliseconds = 500` against 20 logic frames in the original; the class comment of `SimulationWorld` names 8 phases, `Step` executes more (target acquisition, turrets, firing stages, sales, delayed damage and vision are missing there). Each of these statements comes from one reading pass and is to be confirmed against the reference source before a correction.

## 1. Method, counting rules, limits

### 1.1 Classes

| Cl. | Meaning | Subtype (column "Cl." in the tables) | Who |
|---|---|---|---|
| A | Original constant in frames: a value that the original fixes in logic frames; to be scaled with rate/30. Origin with file and function/constant where found in the reference source, otherwise "origin not verified" | – | 20 |
| B | Time value | `daten` (data) = milliseconds/seconds from the data, converted (individually only where conspicuous; otherwise counted mechanically, §2); `fallback` = fallback value in ticks for a data field; `literal` = own duration, written directly as a tick number | 20 |
| C | Execution cadence: how often something runs (think and search intervals, `% N`, staggering by ID, queries per tick). The column "At 60 Hz" says whether that changes results or only load | – | 20 |
| D | raw data per frame: fields or default values that are consumed "per frame" without conversion | – | 20, 23 |
| E | non-invariant integration: Euler steps per tick, thresholds against one tick step, multiplicative damping per tick, arrival radius "one tick of travel", order, "one tick later" | – | 23 |
| F | Presentation clock outside the simulation | `tick` = on the simulation tick; `wand` (wall clock) = on wall-clock time, but with a constant of 30; `originalbild` (original frame) = fixed 1/30 s unit, correctly computed with wall-clock time (do not tie to the tick rate) | 21 |
| G | Format: rate or tick values in snapshot, replay, mission state, savegame | – | 20, 23 |
| H | Test or tool | `schritt` (step; fixed step `1.0/30`), `zeit` (time; tick number that means a time), `ticks` (real tick semantics, stays), `budget` (run length), `rate` (uses or checks the rate itself), `golden`, `ausgabe` (output; tick → seconds with 30) | 21, 23 |

### 1.2 Counting rules

- **One site** = one declaration (constant/field with tick meaning) **or** one statement with a tick literal **or** one contiguous integration block (class E, line range) **or** one raw data field. Uses of a named constant are not counted again.
- **Counted mechanically** (not as a row in §5): plain conversions at parsing, i.e. `ModuleConfigReader.Ticks(cfg, "Key", 0)` and calls of the `SimulationTiming` conversions with a data value. They are listed per file in §2 and §3.2 and in column B of the table §0.1. Where a conversion is conspicuous (fallback value ≠ 0, run time instead of parse time, different rounding, computing with the result), it has a row of its own.
- **Tests:** one row of the tables in §7 combines hit lines of the same file and the same subtype; what is counted is the number at the beginning of the description ("12 sites: …"), otherwise 1.
- **State fields** (keys that are written through `IStateWriter`) are not in the class tables but in §4.3 and Appendix C.

### 1.3 Procedure and limits

1. Search run with 33 patterns (§8) over `src`, `tests`, `tools` (only `*.cs`, without `bin`, `obj`, `.claude/worktrees`): 7,679 hit lines in 34 project directories.
2. Division into 29 packages. **Read completely** (every file in full, hits only as pointers): `CnCNew.Simulation`, `CnCNew.Game`, `CnCNew.Presentation` – in the simulation with these exceptions: the catalog and format files (`Sim\Templates/*`, `Sim\Replay/*`, `Sim\Snapshots/WorldSnapshot.cs`) are read in the sections related to time and rate and at every hit line, `Sim\Numerics`, `Sim\State/IStateIO.cs` and individual catalog files only at the hit lines, and files without hits (`Sim\Ids`, `Sim\Random`, `Sim\State/JsonState.cs`, four catalogs without time fields) not at all. **Hit-driven** (every hit line of the strong patterns with the surrounding method; every hit line is assigned to a table row or a group in Appendix A): `CnCNew.Ui`, `CnCNew.Audio`, `CnCNew.Client.Stride`, `CnCNew.Data`, `CnCNew.W3D` and the small projects, `tools`, `tests`. The reading of the packages was done by separate reading passes with a fixed output format; the coordinator himself read `Sim\Time`, the catalogs in `Sim\Templates`, `Sim\Replay`, `Sim\Snapshots`, `ModuleConfigReader`, the golden files and the tests for rate rejection, re-checked the ten sites from §0.2 and samples from every package against the source code and the reference source, and generated all counts with scripts from the result files (line references checked against the files).
3. Reading state per project (files):

| Project | Files | read completely | partly (methods around the hits) | not read (no hit of the strong patterns) |
|---|---|---|---|---|
| `CnCNew.Simulation` | 136 | 112 | 16 | 8 |
| `CnCNew.Game` | 58 | 58 | 0 | 0 |
| `CnCNew.Presentation` | 71 | 71 | 0 | 0 |
| `CnCNew.Ui` | 48 | 12 | 14 | 22 |
| `CnCNew.Client.Stride` | 50 | 18 | 15 | 17 |
| `CnCNew.Audio` | 21 | 10 | 2 | 9 |
| `CnCNew.Data` | 29 | 5 | 7 | 17 |
| other src | 45 | 6 | 16 | 23 |
| tools | 24 | 12 | 11 | 1 |
| tests | 119 | 14 | 98 | 7 |

**Limits:** (a) Origin statements are verified only as far as they stand in the column – mostly the constant or the line was searched in the reference source, the whole function was not read; "origin not verified" means: not found or not searched. (b) In hit-driven packages, coupling that no pattern hits can be missing; in the tests that is certainly so (the reading passes added sites beyond the hits, but did not enumerate every loop bound). (c) The "at 60 Hz" statements are inferences from the code. (d) Files other than `*.cs` (scripts in `scripts/`, project files, docs) are not in scope; the capture and demo scripts with `--frames` are not audited.

## 2. The central constant and the conversions

`SimulationTiming.LogicFramesPerSecond = 30` (`Sim\Time/SimulationTiming.cs`, line 21; original `LOGICFRAMES_PER_SECOND = 30`, `GameCommon.h` line 72). Conversions there: `DurationMillisecondsToTicks` (rounded up, like `INI::parseDurationUnsignedInt`), `DurationMillisecondsToFixedTicks` (unrounded, like `parseDurationReal`), `SecondsToTicks` (rounded up), `PerSecondToPerTick` (÷ 30), `PerSecondSquaredToPerTickSquared` (÷ 900), `DegreesPerSecondToRadiansPerTick`. Not rate-dependent: `DegreesToRadians`, `RatioToFixed`, `MillisecondsPerSecond`, `SleepNone = 1`, `SleepForever`.

**Uses of the constant (occurrences of the identifier, counted):**

| Project | old (initial inventory) | new | Files | Note |
|---|---|---|---|---|
| `CnCNew.Simulation` | 61 (of which 8 in `SimulationTiming.cs`) in 32 | **63** (of which 9 in `SimulationTiming.cs`) | 32 | 54 qualified uses outside the file, 9 in the file itself |
| `CnCNew.Game` | 14 (4 through the copy in `CnCNew.Ui`) in 6 | **14** | 6 | 10 × `SimulationTiming.…`, 4 × `InGameUiSettings.LogicFramesPerSecond` (`MissionText.cs`) |
| `CnCNew.Presentation` | 1 | **1** | 1 | `SelectionFlash.cs` |
| `CnCNew.Ui` | 0 | **5** | 1 | own constant `InGameUiSettings.LogicFramesPerSecond = 30` (definition and 4 uses in the file); listed under "copies" in the initial inventory |
| `CnCNew.Client.Stride`, `CnCNew.Audio`, `CnCNew.Data`, others, tools | 0 | **0** | – |  |
| Tests | 4 in 4 | **4** | 4 | `Tests.Basics.cs`, `Tests.Collision.cs`, `InputTests.cs`, `MissionPresentationTests.cs` |

**Calls of the conversions in `src` (without the definitions themselves):** 174 calls – 41 direct calls of the `SimulationTiming` conversions in 12 files and 133 calls of `ModuleConfigReader.Ticks` in 33 files (corrected in Phase 20; here it said "173 calls in 41 files" and "132"); of these 23 have a row of their own in §5, the others are plain conversions at the catalog build (counted mechanically; the tables continue to carry 150 for them). `ModuleConfigReader.Ticks`: **133** calls in 33 files, 117 with fallback value 0, **15 with fallback value ≠ 0 in 10 files** (old: 10 named) – these 15 are listed individually in §5 (`Lifetime 1`, `Delay 1`, `CrashThroughBunkerFXFrequency 4`, `HowitzerFiringRate 10`, `DelayBetweenUpdates 100`, `CreatorDeathCheckRate 30`, `UpdateRate 30`, `DetectionRate 1`, `MoodAttackCheckRate 60`, `MaxLifespan 300`, `KillSelfDelay 3`, `WaitAtStationTime 150`, `MinBurstDelay 9999`, `OCLLifetimeMaxCap 1000`, `ScanDelayTime 30`).

**Rounding and point in time:** All conversions are static functions without a rate parameter. Durations are rounded up (integer or in `decimal`, exact), speeds, accelerations and angular speeds to the nearest value on `Fixed`. The conversion happens **once at the build of the catalog** (`TemplateCatalog.Build` → `binding.ParseData`, `WeaponCatalog.Build`, `BuildLocomotors`, `UpgradeCatalog.Build`, `SpecialPowerCatalog.Build`, `ObjectCreationListCatalog.Build`, `TurretInfo.Parse`); the parsed module data are thereby rate-dependent. Exceptions that compute at run time are in §5 (subtype `daten` with the note "at run time") – among others the script VM (`SecondsToFrames`), `TemplateCatalog.GravityPerTickSquared`, the divisions `… / LogicFramesPerSecond` in module updates. `CnCNew.Data` stores durations as milliseconds (`GameTime`) and knows no field type for frames; values per second and `BuildTime` in seconds are present as a raw number.

**Integer division of the rate** (old 6, new 6 – unchanged, all re-read): `SlavedUpdate.UpdateRate = 30 / 4` (7 ticks; 60 / 4 = 15, not 14), `ParkingPlaceBehavior.HealRateTicks = 30 / 5`, `SpawnBehavior.UpdateRate = 30 / 2`, `WaveGuideUpdate` `30 / 2`, `InGameUiSettings` `FloatingTextTimeOut` `30 / 3`, `ScriptEngine` `% (2 * 30 + 1)`. Only the first is not an integer at 30 Hz.

## 3. Count

### 3.1 Per class and project

See table in §0.1. Subtypes:

| Cl. | Subtype | src | tools | tests |
|---|---|---|---|---|
| B | daten | 219 | 0 | 0 |
| B | fallback | 21 | 0 | 0 |
| B | literal | 53 | 0 | 0 |
| F | originalbild | 32 | 0 | 0 |
| F | tick | 74 | 0 | 0 |
| F | wand | 3 | 0 | 0 |
| H | ausgabe | 1 | 3 | 2 |
| H | budget | 9 | 18 | 311 |
| H | golden | 1 | 2 | 2 |
| H | rate | 2 | 0 | 94 |
| H | schritt | 2 | 5 | 95 |
| H | ticks | 11 | 9 | 120 |
| H | zeit | 52 | 18 | 378 |

B/`daten` in `src` contains the 150 mechanically counted conversions. The 14 D sites under "tests" in §0.1 are fixture values per frame in test files (e.g. `StructuralDecay`, `ForwardDamping`, train values) that depend on the decision for the simulation.

**Old → new (initial inventory against this count):**

| Quantity | old | new |
|---|---|---|
| Copies of the rate and fixed steps `1.0 / 30` | about 105 sites, of which 97 `1.0 / 30` in 15 test files | 103 occurrences of `1.0 / 30` or `1f / 30` in 18 files: tests 95 in 11 (`Game.Tests` 68 in 8, `Presentation.Tests` 27 in 3), `src` 3 in 3, tools 5 in 4 |
| Bare tick numbers in the simulation (A, B/`literal`, B/`fallback`, C) | about 80 and about 30 in `Sim\Testing` | 159 without `Sim\Testing`; in `Sim\Testing` 17 cadences plus 65 driver/budget values (class H) |
| … in `CnCNew.Game` / `CnCNew.Presentation` | about 15 each | 26 / 11 (plus the clocks of class F: 30 / 44) |
| Fallback values ≠ 0 of `ModuleConfigReader.Ticks` | 10 | 15 in 10 files |
| State fields with absolute ticks | about 111 in 41 files | 222 in 56 files (plus 26 remaining counters, 23 durations, 39 quantities per tick) |
| Tick expectations in tests | about 845 lines in 84 test files | 1002 sites in 82 of 119 test files: zeit 378, budget 311, ticks 120, schritt 95, rate 94, ausgabe 2, golden 2 |
| Sixteen-digit hex values in the docs | 137 in 26 files, 79 in `SIMULATION-CORE.md` | 458 in 29 files (`docs/*.md`, `CLAUDE.md`, `README.md`), 189 in `SIMULATION-CORE.md` – the docs have grown since the initial inventory; in the test source code still no pinned state hash |

### 3.2 Simulation per file

| File | A | B | B mech. | C | D | E | F | G | H | Total |
|---|---|---|---|---|---|---|---|---|---|---|
| `Sim\Commands/Commands.cs` |  |  |  |  |  |  |  | 2 |  | 2 |
| `Sim\Events/SimEvent.cs` |  |  |  |  |  |  | 2 | 1 |  | 3 |
| `Sim\Mission/MissionSession.cs` |  | 1 |  |  |  | 1 | 2 |  |  | 4 |
| `Sim\Mission/MissionSnapshot.cs` |  |  |  |  |  |  |  | 3 |  | 3 |
| `Mod\AirModules.cs` | 1 | 2 |  | 1 |  |  |  |  |  | 4 |
| `Mod\AirWeaponModules.cs` | 1 | 8 | 9 | 2 | 4 | 7 |  |  |  | 31 |
| `Mod\BattlePlanModule.cs` |  |  | 5 |  |  | 1 |  |  |  | 6 |
| `Mod\Bodies.cs` |  |  | 1 |  |  |  |  |  |  | 1 |
| `Mod\BridgeModules.cs` |  |  | 1 |  | 4 | 1 |  |  |  | 6 |
| `Mod\CleanupAndSpyModules.cs` | 1 | 1 | 6 | 2 |  |  |  |  |  | 10 |
| `Mod\CombatModules.cs` |  |  | 2 | 1 |  |  |  |  |  | 3 |
| `Mod\ContainChainModules.cs` |  |  | 1 | 1 |  | 1 |  |  |  | 3 |
| `Mod\ContainModules.cs` |  |  | 1 |  |  | 2 |  |  |  | 3 |
| `Mod\CrateModules.cs` |  | 2 | 2 | 1 |  |  |  |  |  | 5 |
| `Mod\DieAndUpdateModules.cs` |  | 1 | 2 |  |  |  |  |  |  | 3 |
| `Mod\DockMachine.cs` |  | 1 |  |  |  |  |  |  |  | 1 |
| `Mod\DockModules.cs` |  |  | 1 | 2 |  |  |  |  |  | 3 |
| `Mod\FerryModules.cs` |  |  | 2 |  |  | 4 |  |  |  | 6 |
| `Mod\FieldEffectModules.cs` | 3 | 1 |  |  | 3 | 2 |  |  |  | 9 |
| `Mod\FlightModules.cs` |  | 12 | 15 | 2 |  | 5 |  |  |  | 34 |
| `Mod\HackerAndAssaultModules.cs` |  | 1 | 4 | 1 |  | 1 |  |  |  | 7 |
| `Mod\JetAIUpdate.Flight.cs` |  |  |  |  |  | 1 |  |  |  | 1 |
| `Mod\JetAIUpdate.cs` | 3 | 3 | 3 | 1 | 2 | 9 |  |  |  | 21 |
| `Mod\LastBlockerModules.cs` |  |  | 2 |  | 1 | 2 | 1 |  |  | 6 |
| `Mod\MatchModules.cs` | 1 | 1 |  |  |  |  |  |  |  | 2 |
| `Mod\MineModules.cs` |  | 2 | 1 | 1 |  |  |  |  |  | 4 |
| `Mod\ModuleConfigReader.cs` |  | 1 |  |  |  |  |  |  |  | 1 |
| `Mod\Phase12Modules.cs` |  | 1 |  | 2 |  | 2 | 1 |  |  | 6 |
| `Mod\Phase5BlockerModules.cs` |  | 3 | 10 | 1 |  | 2 |  |  |  | 16 |
| `Mod\Phase5Modules.cs` | 1 | 2 | 8 | 2 | 1 | 8 |  |  |  | 22 |
| `Mod\PhysicsAndMinefieldModules.cs` |  |  |  | 1 |  | 1 |  |  |  | 2 |
| `Mod\ProductionExitModules.cs` |  |  | 1 |  |  |  |  |  |  | 1 |
| `Mod\ProjectileModules.cs` | 3 | 3 | 4 | 1 |  | 6 |  |  |  | 17 |
| `Mod\RailroadModules.cs` | 1 | 1 |  | 1 | 10 | 4 |  |  |  | 17 |
| `Mod\RepairModules.cs` |  | 2 | 1 | 1 |  | 1 |  |  |  | 5 |
| `Mod\RetailMatchModules.cs` |  | 3 | 6 |  |  | 1 |  |  |  | 10 |
| `Mod\SlaveModules.cs` | 7 | 4 | 4 | 1 | 1 | 6 |  |  |  | 23 |
| `Mod\SpawnModules.cs` | 1 |  | 1 | 2 |  |  |  |  |  | 4 |
| `Mod\SpecialAbilityStaging.cs` |  | 1 | 3 |  |  | 2 | 1 |  |  | 7 |
| `Mod\SpecialPowerModules.cs` |  |  |  |  |  | 2 | 1 |  |  | 3 |
| `Mod\SpecialsModules.cs` |  |  |  | 2 | 1 | 2 |  |  |  | 5 |
| `Mod\StructureModules.cs` |  | 4 | 6 | 1 |  | 1 |  |  |  | 12 |
| `Mod\SupplyModules.cs` |  |  | 2 | 1 |  |  |  |  |  | 3 |
| `Mod\ToppleAndFallModules.cs` | 2 | 1 | 3 |  | 3 | 5 |  |  |  | 14 |
| `Mod\TransportModules.cs` | 1 | 5 |  | 1 | 2 | 7 |  |  |  | 16 |
| `Mod\VehicleModules.cs` | 2 |  | 2 |  | 1 | 3 | 1 |  |  | 9 |
| `Mod\WorldEffectModules.cs` | 1 | 3 | 14 | 5 | 1 | 9 |  |  |  | 33 |
| `Sim\Modules/ModuleFramework.cs` | 1 |  |  |  |  | 1 |  |  |  | 2 |
| `Sim\Replay/ReplayFormat.cs` |  |  |  |  |  |  |  | 1 |  | 1 |
| `Sim\Replay/ReplayRunner.cs` |  |  |  | 1 |  |  |  | 1 |  | 2 |
| `Sim\Scripting/ScriptEngine.Actions.cs` |  | 3 |  | 2 | 2 | 1 | 1 |  |  | 9 |
| `Sim\Scripting/ScriptEngine.Conditions.cs` |  |  |  |  |  | 3 |  |  |  | 3 |
| `Sim\Scripting/ScriptEngine.ExtendedActions.cs` |  |  |  | 1 |  |  |  |  |  | 1 |
| `Sim\Scripting/ScriptEngine.Phase13.cs` |  | 1 |  | 3 |  | 3 |  |  |  | 7 |
| `Sim\Scripting/ScriptEngine.Powers.cs` |  | 1 |  |  |  |  |  |  |  | 1 |
| `Sim\Scripting/ScriptEngine.Sequential.cs` |  |  |  | 1 | 3 | 1 |  |  |  | 5 |
| `Sim\Scripting/ScriptEngine.TeamAbilities.cs` |  | 2 |  |  |  |  |  |  |  | 2 |
| `Sim\Scripting/ScriptEngine.TeamEvents.cs` |  |  |  | 1 |  | 2 |  |  |  | 3 |
| `Sim\Scripting/ScriptEngine.TeamQueues.cs` | 1 |  |  | 1 |  |  |  |  |  | 2 |
| `Sim\Scripting/ScriptEngine.cs` | 1 | 2 |  | 1 | 1 |  | 2 |  |  | 7 |
| `Sim\Scripting/ScriptProgram.cs` |  |  |  |  | 1 |  |  |  |  | 1 |
| `Sim\Skirmish/SkirmishAi.cs` |  | 19 |  | 5 |  | 3 |  |  |  | 27 |
| `Sim\Skirmish/SkirmishMatchSession.cs` |  | 1 |  | 2 |  |  |  | 2 |  | 5 |
| `Sim\Snapshots/WorldSnapshot.cs` |  |  |  |  |  |  |  | 2 |  | 2 |
| `Sim\Templates/ObjectCreationListCatalog.cs` |  |  | 2 |  |  |  |  |  |  | 2 |
| `Sim\Templates/ObjectTemplate.cs` |  | 2 | 15 |  |  |  |  |  |  | 17 |
| `Sim\Templates/SpecialPowerCatalog.cs` |  | 1 |  |  |  |  |  |  |  | 1 |
| `Sim\Templates/TurretInfo.cs` | 2 |  | 5 |  | 1 |  |  |  |  | 8 |
| `Sim\Templates/UpgradeCatalog.cs` |  |  | 1 |  |  |  |  |  |  | 1 |
| `Sim\Templates/WeaponCatalog.cs` |  | 1 | 4 |  | 1 | 3 |  |  |  | 9 |
| `Sim\Testing/MissionObjectiveDriver.cs` |  |  |  | 1 |  |  |  |  | 2 | 3 |
| `Sim\Testing/MissionScenario.cs` |  |  |  |  |  |  |  |  | 6 | 6 |
| `Sim\Testing/ObjectiveDriver.Commands.cs` |  |  |  | 1 |  |  |  |  | 3 | 4 |
| `Sim\Testing/ObjectiveDriver.Leaves.cs` |  |  |  |  |  | 1 |  |  | 31 | 32 |
| `Sim\Testing/ObjectiveDriver.cs` |  |  |  |  |  |  |  |  | 7 | 7 |
| `Sim\Testing/ReferenceMatchScenario.cs` |  |  |  | 4 |  |  |  |  | 6 | 10 |
| `Sim\Testing/SkirmishMatchScenario.cs` |  |  |  | 2 |  |  |  |  | 2 | 4 |
| `Sim\Testing/StressScenario.cs` |  |  |  | 9 |  |  |  |  | 8 | 17 |
| `Sim\Testing/SyntheticFixture.cs` |  | 3 |  |  |  |  |  |  |  | 3 |
| `Sim\Time/SimulationTiming.cs` | 1 | 4 |  | 1 |  |  |  |  |  | 6 |
| `Sim\World/Entity.cs` |  |  |  |  |  | 1 |  |  |  | 1 |
| `Sim\World/SimulationWorld.Acquisition.cs` |  | 2 |  | 4 |  | 1 |  |  |  | 7 |
| `Sim\World/SimulationWorld.Collision.cs` |  |  |  |  |  | 4 |  |  |  | 4 |
| `Sim\World/SimulationWorld.Construction.cs` | 1 |  |  |  |  | 1 |  |  |  | 2 |
| `Sim\World/SimulationWorld.CreationLists.cs` | 1 |  |  |  |  | 1 |  |  |  | 2 |
| `Sim\World/SimulationWorld.FiringTracker.cs` | 1 | 1 |  | 1 |  |  |  |  |  | 3 |
| `Sim\World/SimulationWorld.Projectiles.cs` |  |  |  |  |  | 1 |  |  |  | 1 |
| `Sim\World/SimulationWorld.Snapshot.cs` |  |  |  |  |  |  |  | 1 |  | 1 |
| `Sim\World/SimulationWorld.Turrets.cs` | 1 | 3 |  | 1 |  | 9 |  |  |  | 14 |
| `Sim\World/SimulationWorld.Vision.cs` |  |  |  | 1 |  |  |  |  |  | 1 |
| `Sim\World/SimulationWorld.WeaponSlots.cs` |  | 1 |  |  |  |  |  |  |  | 1 |
| `Sim\World/SimulationWorld.cs` |  |  |  | 1 |  | 3 |  | 2 |  | 6 |
| **Total** | 39 | 117 | 150 | 81 | 43 | 138 | 12 | 15 | 65 | 660 |

"B mech." = plain conversion calls without a row of their own. Class F in the simulation are values that the simulation carries only for the display (event values in ticks, display states). Files of the simulation without an entry contain no site.

## 4. Answers to the open questions

### 4.1 Do the golden files depend on the tick rate?

- **`tests/CnCNew.Game.Tests/baselines/screens.txt`** (54 SHA-256 = 9 screens × 6 resolutions; generated by `ShellTests.WalkScreens`/`Capture` through `UiDrawList.Dump`): The shell is built with `animateTransitions: false`, the screens are captured after clicks without time progress (a single `shell.Update(1.0 / 30)` before the handover to the match). **48 entries do not depend on the rate.** The **6 entries `ScoreScreen@…`** show `UnitsBuilt`, `UnitsLost`, `UnitsDestroyed`, `BuildingsBuilt`, `BuildingsLost` and `Resources` of a real skirmish that runs through `Session.Step()` until `MaxTicks = 600` (`ShellTests.cs`, lines 107–113): they depend on the rate, first through the tick budget (600 ticks are 10 s instead of 20 s of game time at 60 Hz), second through the simulation result itself. At 30 Hz they stay unchanged in Phase 20/21; with Phase 23 they are to be collected anew. Inferred from the code, not executed.
- **`tests/CnCNew.Game.Tests/baselines/hud-drawlist-1280x720.sha256`** (one SHA-256; `HudTests.GoldenDrawListHash`): The dump arises after exactly one `match.Advance(1.0 / 30)` plus `controller.Update(1.0 / 30)` in a fresh skirmish on "Alpine Assault" – one tick at 30 Hz, two at 60 Hz because of the accumulator. Tick-dependent contents of the HUD draw list are money text, power bar, command bar (cooldown wipe, build queue), superweapon countdown (`(readyFrame - Tick) / LogicFramesPerSecond`, blinking through `Tick / SuperweaponFlashFrames`), message lines (age per tick) and healing icons; there is no game-time clock, the minimap is a single rectangle. Without selection, mouse event and superweapon, only money and power of these are populated. **Whether the hash differs between tick 1 and tick 2 has not been determined** (the test would have to run for that). So it is dependent at most through the number of steps; at 30 Hz it stays unchanged.
- **Further pinned values:** `Audio.Tests/Tests.Catalog.cs` (`GoldenRng`, three random values), `Ui.Tests/WndTests.cs` (`DrawListGolden`, synthetic surface without time; only the location seen), `baselines/compatibility.json` – not rate-dependent. `Presentation.Tests`: no pinned hash (every checksum is compared run against run; the only fixed value is the road triangle count 206,956). `Simulation.Tests`: no pinned state hash (the test source code contains no 16- or 64-digit hex strings; checked with a pattern search). The reference values of the simulation are still only in the docs.
- **Tools:** `UiRenderTool` and the capture scripts generate checksums that are pinned nowhere in the repo (not audited, Appendix B).

### 4.2 Which tests check explicitly that rate 60 is rejected?

Exactly two, both in `tests/CnCNew.Simulation.Tests/Tests.State.cs`:

1. `SnapshotRejection`, line 153: replaces `"tickRate":30` with `"tickRate":60` in the snapshot text and expects `SnapshotFormatException` from `WorldSnapshot.Restore` ("foreign tick rate"). Caution: the test works with text replacement – if the snapshot at some point no longer contains `30`, it replaces nothing and fails with "the defective snapshot was accepted".
2. `ReplayFormatTests`, lines 221–222: sets `TickRate = 60` in the replay header and expects `ReplayRejectedException` from `ReplayRunner.Run(replay, templates, fingerprint)` ("60 Hz").

No test checks the rejection in `MissionSnapshot.Restore` (line 81; the same check, untested), and `SaveGame` has no rate check. Ten test files build a `ReplayHeader` at eleven sites with the literal `30` as the rate (`Tests.Acquisition`, `Construction`, `Repair`, `WeaponSlots` (2), `TurretAttack`, `Transports`, `Terrain`, `Teams`, `Supply`, `SpecialPower`); `InputTests`, `SkirmishMatchSession`, `ReferenceMatchScenario` and `StressScenario` take the constant. `Tests.Basics.cs` (`Timing`, `FloatDeviation`) checks `LogicFramesPerSecond == 30` and the whole table milliseconds → ticks.

### 4.3 Which state fields in snapshots are absolute ticks?

310 stored fields related to ticks in 59 files: **222 absolute ticks** (`nextWake`, ready, expiry, start ticks), 26 remaining counters (ticks counting down), 23 durations/intervals in ticks, 39 quantities per tick (speeds, healing amounts, angular rates). Per file:

| File | absolute | remaining | duration | per tick |
|---|---|---|---|---|
| `Game\Save/SaveGame.cs` | 1 |  |  |  |
| `Sim\Commands/Commands.cs` | 1 |  | 2 |  |
| `Sim\Events/SimEvent.cs` | 2 |  |  |  |
| `Sim\Mission/MissionSnapshot.cs` | 1 |  | 2 |  |
| `Mod\AirModules.cs` | 3 |  |  |  |
| `Mod\AirWeaponModules.cs` | 16 |  | 1 | 2 |
| `Mod\BattlePlanModule.cs` | 2 |  |  |  |
| `Mod\BridgeModules.cs` | 3 |  | 1 | 2 |
| `Mod\CleanupAndSpyModules.cs` | 5 | 1 |  |  |
| `Mod\CombatModules.cs` | 3 |  |  |  |
| `Mod\ContainChainModules.cs` | 1 |  |  |  |
| `Mod\ContainModules.cs` | 3 |  |  |  |
| `Mod\CrateModules.cs` | 2 |  |  |  |
| `Mod\DieAndUpdateModules.cs` | 2 |  |  |  |
| `Mod\DockMachine.cs` | 2 |  |  |  |
| `Mod\DockModules.cs` | 3 |  |  |  |
| `Mod\FerryModules.cs` |  |  |  | 2 |
| `Mod\FieldEffectModules.cs` | 4 |  | 1 | 2 |
| `Mod\FlightModules.cs` | 19 | 1 | 1 | 4 |
| `Mod\HackerAndAssaultModules.cs` | 2 | 1 |  |  |
| `Mod\JetAIUpdate.cs` | 6 |  |  | 1 |
| `Mod\LastBlockerModules.cs` | 4 |  |  |  |
| `Mod\MatchModules.cs` | 1 |  |  |  |
| `Mod\MineModules.cs` | 3 | 1 |  |  |
| `Mod\Phase12Modules.cs` | 6 |  |  |  |
| `Mod\Phase5BlockerModules.cs` | 11 |  |  |  |
| `Mod\Phase5Modules.cs` | 9 | 2 |  | 1 |
| `Mod\PhysicsAndMinefieldModules.cs` | 2 |  |  | 3 |
| `Mod\ProductionExitModules.cs` | 1 | 1 |  |  |
| `Mod\ProjectileModules.cs` | 7 | 3 | 1 | 5 |
| `Mod\RailroadModules.cs` | 1 | 1 | 1 | 2 |
| `Mod\RepairModules.cs` | 1 | 1 |  | 1 |
| `Mod\RetailMatchModules.cs` | 6 | 1 |  |  |
| `Mod\SlaveModules.cs` | 3 | 2 | 1 |  |
| `Mod\SpawnModules.cs` | 3 |  |  |  |
| `Mod\SpecialAbilityStaging.cs` | 1 | 2 |  | 1 |
| `Mod\SpecialPowerModules.cs` | 4 |  |  |  |
| `Mod\SpecialsModules.cs` | 3 |  |  | 1 |
| `Mod\StructureModules.cs` | 6 |  |  | 1 |
| `Mod\ToppleAndFallModules.cs` | 6 |  |  | 6 |
| `Mod\TransportModules.cs` | 5 | 1 |  | 3 |
| `Mod\VehicleModules.cs` | 4 |  |  | 1 |
| `Mod\WorldEffectModules.cs` | 17 | 5 | 1 |  |
| `Sim\Scripting/ScriptEngine.Phase13.cs` | 1 |  |  |  |
| `Sim\Scripting/ScriptEngine.Sequential.cs` |  | 1 |  |  |
| `Sim\Scripting/ScriptEngine.TeamAbilities.cs` | 1 |  | 1 |  |
| `Sim\Scripting/ScriptEngine.TeamQueues.cs` | 1 |  |  |  |
| `Sim\Scripting/ScriptEngine.cs` | 4 | 2 |  |  |
| `Sim\Templates/ObjectCreationListCatalog.cs` |  |  | 1 |  |
| `Sim\Timers/SimTimer.cs` | 1 |  | 1 |  |
| `Sim\World/AcquisitionState.cs` | 3 |  |  |  |
| `Sim\World/Entity.cs` | 6 |  | 1 | 1 |
| `Sim\World/SimulationWorld.FiringTracker.cs` | 2 |  |  |  |
| `Sim\World/SimulationWorld.Projectiles.cs` | 1 |  |  |  |
| `Sim\World/SimulationWorld.Snapshot.cs` | 3 |  | 4 |  |
| `Sim\World/SimulationWorld.cs` | 4 |  | 3 |  |
| `Sim\World/TunnelTracker.cs` | 1 |  |  |  |
| `Sim\World/TurretState.cs` | 4 |  |  |  |
| `Sim\World/WeaponSlotState.cs` | 5 |  |  |  |
| **Total** | 222 | 26 | 23 | 39 |

The keys per file are in Appendix C. The world tick itself (`"tick"`) and the tick stamps of the commands and events are counted in. The list comes from the completely read files of the simulation; a cross-check over all `writer.Int/UInt("…")` lines with time-like names finds 266 such lines, of which 215 can be assigned directly to a list entry – the remaining 51 are path notations (`turrets[].holdUntil`), ID counters and header fields; they were not traced individually (Appendix B). `SkirmishAi` holds absolute ticks but is not stored (plan E11). For Phase 20 this means: a state is readable only with its rate; converting stored states between rates would be a change to about 300 fields and is rightly not provided for in the plan.

### 4.4 Millisecond fields and their quantisation at 60 Hz (measured on the real data)

Measured on 5 October 2026 on `.local/phase2-import-a` (retail base data, `IniTool compile --maps exclude`, 12,550 definitions, 19,553 modules; output 144,626,133 bytes in the scratchpad, not in the repo) with a throwaway script over the definition snapshot. Compared, per value *m* in milliseconds, is the plan rule `2 · ceil(m · 30 / 1000)` against the direct rounding `ceil(m · 60 / 1000)`.

| Data set | Values | distinct ms values | of these quantised differently at 60 Hz | Occurrences | at 120 Hz |
|---|---|---|---|---|---|
| Weapons (363 definitions): `ClipReloadTime` | 309 | 22 | 2 (40 ms, 1367 ms) | 21 | 2 / 21 |
| Weapons: `DelayBetweenShots` (minimum) | 334 | 27 | 10 (1, 5, 40, 50, 75, 150, 250, 650, 750, 777 ms) | 81 | 11 / 83 |
| Weapons: `PreAttackDelay` | 29 | 8 | 2 (250 ms, 1538 ms) | 3 | 2 / 3 |
| Weapons: `ContinuousFireCoast` | 17 | 3 | 0 | 0 | 0 |
| **Weapons, all four fields** | 689 | 42 | **12** | **105** (in **84 of 363 weapons**) | 13 / 107 |
| **Module fields** whose key is read through `ModuleConfigReader.Ticks` (119 keys) | 9934 | 76 | **17** | **263** (42 distinct module/key/value, 31 module/key pairs) | 18 / 267 |
| all `Time` fields in object modules (upper bound) | 10545 | 94 | 26 | 331 | 30 / 347 |

Examples: 250 ms are 8 frames = 266.7 ms in the original; directly at 60 Hz it would be 15 ticks = 250 ms (115 module occurrences, 22 in weapons). 40 ms are 2 frames = 66.7 ms against 3 ticks = 50 ms (43 weapon fields). Most often affected: `AIUpdateInterface.MoodAttackCheckRate` (41 objects), `SlowDeathBehavior.DestructionDelay` (34), `TransportContain.ExitDelay` (27), `LifetimeUpdate.Min/MaxLifetime` (12 each), `RiderChangeContain.ExitDelay` (11), `StructureCollapseUpdate.MinBurstDelay` (10). So the plan rule is necessary; with direct rounding the cadence of 84 weapons would change.

Further measured values: **118 of 363 weapons** have no `DelayBetweenShots` (29) or 0 ms (89) and run into the one-tick lower bound (§0.2 rank 2); for 4 weapons minimum ≠ maximum (only the minimum is used). In modules there are 1,042 time values with 0 ms and 88 with at most one frame (≤ 33 ms), among others `PointDefenseLaserUpdate.ScanRate` (10 × 0 ms, 4 × ≤ 33 ms), `AutoHealBehavior.HealingDelay` (3 × 1 ms), `CountermeasuresBehavior.ReactionLaunchLatency` and `ReloadTime` (36 × 0 ms each). `AutoDepositUpdate.DepositTiming` is never 0 in the real data (10 × 2000 ms, 1 × 12000 ms). `BuildTime` in seconds (objects and upgrades): no value quantises differently at 60 Hz.

Restrictions of the measurement: The module count sorts by **key name**, not by module type (a key that one module reads also counts in modules that do not read it – hence the upper bound in the last row). Fields from `GameData`, special powers, upgrades, OCL and turrets are not broken down. FX time fields (4,348 values, 48 distinct ones with a different result, 1,832 occurrences) concern the presentation, not the simulation. Not measured is the effect of the run-time rounding with rate-of-fire bonus (W1).

## 5. Inventory simulation (`CnCNew.Simulation`)

Per file, sorted by line. "Phase": 20 = parametrisation (result-identical at 30 Hz), 23 = time-step invariance, 21 = presentation side. Plain conversion calls are not listed (number per file in parentheses).

### 5.1 Time, catalogs, formats

**`Sim\Commands/Commands.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 91 | SimCommand.Tick | G | Every command is addressed to an absolute tick; serialised as "tick" (l. 106, read l. 116) in the replay | - | Replays are valid only with their recording rate; a 30 Hz replay cannot be played back at 60 Hz (ticks are not times). | 20 |
| 600 | SetTimerCommand.DelayTicks / RepeatTicks | G | System command with delay and repeat interval in ticks; serialised as "delay"/"repeat" (ll. 609–610, read ll. 125–126) | own | Callers specify ticks: unscaled, half the duration; fixed in the replay as a tick number. | 20,21 |

**`Sim\Events/SimEvent.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 75-80 | SimEventKind.ProjectileLaunched (Value) | F/tick | According to the doc comment, Value carries the absolute tick at which a delayed hit lands (0 for a projectile); emitter and consumer outside the package not audited | own | On the simulation tick: a presentation that computes a flight time with 1/30 s from (Value − Tick) shows half the speed or double the duration. | 21 |
| 116 | SimEventKind.Sabotaged (Value) | F/tick | According to the doc comment, Value can carry the disabling duration in ticks ("the disabling ticks"); emitter and consumer outside the package not audited | own | On the simulation tick: displaying a duration from Value needs the rate. | 21 |
| 136 | SimEvent.Tick | G | Every event carries the absolute tick; serialised as "tick" (l. 143, read l. 164) | - | Consumers outside the simulation that interpret tick differences as time compute wrongly; in the format valid only with the rate. | 20,21 |

**`Sim\Mission/MissionSession.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 34 | MissionOptions.MaxTicks | B/literal | Hard run limit in ticks for headless runs (default 0 = none); checked in IsOver (l. 95), is stored in the mission state | own | A budget given in ticks covers only half the game time at 60 Hz; callers (tools, tests) must scale. | 20,21 |
| 34 | MissionOptions.PresentationLatencyTicks | F/tick | Duration in ticks for which speech/sound/movie/camera move counts as running for HAS_FINISHED_* conditions (default 0); consumer ScriptEngine.Actions.cs ll. 398–405 (outside the package, seen only by grep) | own | On the simulation tick: a value passed by the host in ticks lasts only half as long at 60 Hz as the real playback. | 20,21 |
| 249-260 | MissionSession.Step (order) | E | Per tick: scripts (Engine.Update), then world step, then Engine.Observe(events): scripts react to events only in the next tick | own | The one-tick delay between event and script reaction halves from 33 ms to 17 ms; script chains over several ticks run faster in real time. | 23 |
| 254 | MissionSession.Step Engine.TimeFrozen | F/tick | FREEZE_TIME: scripts keep running per Step call, the world step is omitted, World.Tick stands still; the comment refers to a presentation clock for the cameras (behaviour of the engine in the freeze not audited, outside the package) | own | In the freeze only the call rate of Step counts: at 60 Hz twice as many script passes per second with the tick standing still. | 20,21 |

**`Sim\Mission/MissionSnapshot.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 39 | MissionSnapshot.Write "tickRate" | G | Writes the tick rate (SimulationTiming.LogicFramesPerSecond) into the mission state | - | Must carry the rate of the stored session. | 20 |
| 46-48 | MissionSnapshot.Write options maxTicks / presentationLatencyTicks / combatIntervalTicks | G | Mission options are stored in ticks and read back unchanged in Restore ll. 91–92 | - | Values are valid only together with the stored rate; currently consistent through the rate check (l. 81), not convertible without a rule on a rate change. | 20 |
| 81 | MissionSnapshot.Restore "tickRate" | G | Rejects the state if tickRate is not equal to the compiled-in rate ("Tick rate mismatch."); no conversion, no version jump (version 2) | - | All 30 Hz states are rejected at another rate; with a selectable rate the check must be against the rate of the target session, or the session must be built with the stored rate. | 20 |

**`Sim\Modules/ModuleFramework.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 119-123 | IWorldServices.AdvanceConstruction | E | Interface "one tick of construction work": construction progress as a step per tick, the last tick completes; implementation outside the package, not read | DozerAIUpdate::update (named only in the doc comment; origin not verified) | Depends on the implementation: a fixed progress per call would build twice as fast at 60 Hz. | 20,23 |
| 125-126 | IWorldServices.IsSignificantlyAboveTerrain | A | Interface with original constant in the doc: "higher than three frames of free fall"; the computation itself lies in the implementation (SimulationWorld, outside the package, not read) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/Common/Thing/Thing.cpp, Thing::isSignificantlyAboveTerrain: getHeightAboveTerrain() > -(3*3)*m_gravity | With gravity per tick², 3×3×g gives a quarter of the height at 60 Hz; 3 frames must become 6 (36×g60 = 9×g30). | 20 |

**`Sim\Replay/ReplayFormat.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 19-21, 52, 67-68, 102 | ReplayHeader.TickRate / EndTick / CheckpointInterval | G | Replay header writes u32 tick rate, i64 end tick and u32 checkpoint spacing in ticks; commands carry an i64 tick | own | Rate is already stored; EndTick/CheckpointInterval are tick numbers of the recording rate | 20 |

**`Sim\Replay/ReplayRunner.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 50-51 | ReplayRunner.CheckCompatibility | G | Rejects every replay header whose TickRate ≠ LogicFramesPerSecond; the overload `Run(replay, world, options)` (line 71) does not check at all | own | In future: create the world with the stored rate, reject only unsupported rates; the unchecked overload must check the rate of the world against the header | 20 |
| 100-102 | ReplayRunOptions.RecordCheckpointInterval / VerifyIncrementalEvery | C | `(tick + 1) % N`: checkpoint and verification cadence in ticks (tool/test parameter) | own | Only load and density of the checkpoints, no change of results | 20 |

**`Sim\Snapshots/WorldSnapshot.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 37 | WorldSnapshot.Write ("tickRate") | G | Writes `tickRate` = constant into the header (not into the state hash) | own | Must write the rate of the world | 20 |
| 67, 78-82 | WorldSnapshot.Restore | G | Rejects `tickRate` ≠ constant ("Tick rate mismatch."); `WorldConfiguration` is rebuilt without a rate | own | In future create the world with the stored rate; WorldConfiguration needs the field | 20 |

**`Sim\Templates/ObjectTemplate.cs`** (plus 15 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 361-460 | TemplateCatalog.Build (binding.ParseData per module, BuildLocomotors, WeaponCatalog.Build …) | B/daten | The whole catalog is built without a rate parameter; all tick and per-tick values of the module data, weapons, locomotors, upgrades, special powers, OCL and turrets arise here once per data set | own | The catalog is rate-dependent and is today shared between tools, client and sessions; the rate must become a build parameter and part of the fingerprint | 20 |
| 480 | TemplateCatalog.GravityPerTickSquared | B/daten | Property computes `Gravity / 900` (decimal → Fixed) at EVERY access, not once at the build; `Gravity` stays in the catalog as decimal per s² | GameData.ini Gravity; GlobalData::m_gravity | Needs the rate of the world at run time; at 60 Hz a quarter per tick² (−64/3600 = −0.0178: 1165 raw steps in Q47.16) | 20 |

**`Sim\Templates/SpecialPowerCatalog.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 25, 54 | SpecialPowerCatalog.DefaultReloadMilliseconds | B/daten | Default 1000 ms when ReloadTime is missing; kept in milliseconds and converted through DurationMillisecondsToTicks (no tick literal) | Default/SpecialPower.ini DefaultSpecialPower (according to the comment) | No coupling of its own beyond the conversion | 20 |

**`Sim\Templates/TurretInfo.cs`** (plus 5 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 26 | TurretInfo.NeverScanTicks | A | `9999999` frames as the default for Min/MaxIdleScanInterval ("practically never") | Origin not verified (comment names TurretAIData::m_minIdleScanInterval; value not looked up in the original) | Sentinel; scaling unnecessary, but must not overflow if × rate/30 is applied across the board | 20 |
| 29 | TurretInfo.DefaultRate | D | `0.01` rad per frame as the default for TurretTurnRate/TurretPitchRate when the field is missing (lines 168–169); data values go through DegreesPerSecondToRadiansPerTick | TurretAI.h: DEFAULT_TURN_RATE = DEFAULT_PITCH_RATE = 0.01f; TurretAI.cpp line 178/179 | Without conversion a turret without a rate of its own turns twice as fast at 60 Hz | 20 |
| 134 | TurretInfo.Parse: RecenterTime default `2 * LogicFramesPerSecond` | A | Default of the recentre time in frames when RecenterTime is missing | TurretAI.cpp line 200: m_recenterTime = 2*LOGICFRAMES_PER_SECOND | Scales with the constant; computed at the catalog build | 20 |

**`Sim\Templates/WeaponCatalog.cs`** (plus 4 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 15-17 | WeaponInfo (flight time without projectile object: ceil(distance / WeaponSpeed) ticks) | E | Documented contract: hit after rounded-up ticks of flight time, "immediately" below one tick of travel (implementation in SimulationWorld.Projectiles.cs, row of its own there) | Weapon.cpp WeaponTemplate::fireWeaponTemplate (according to the comment; sequence not looked up) | Threshold "below one tick of travel" and rounding up depend on the step size | 23 |
| 33 | WeaponInfo.InstantSpeed | D | Default `999999` distance per frame for WeaponSpeed/MinWeaponSpeed when the field is missing (line 243 `Speed(key)`) | Weapon.cpp WeaponTemplate constructor: m_weaponSpeed = m_minWeaponSpeed = 999999.0f | Stays "immediately"; do not scale as a value per tick (otherwise nothing visible changes, but the value is not a data value per second) | 20 |
| 80-85 | WeaponInfo.PreAttackDelayWith | E | Run-time rounding: `floor(PreAttackDelayTicks × PRE_ATTACK-Multiplikator)` | Weapon.cpp WeaponTemplate::getPreAttackDelay (Int return truncates) | Like ScaledByRateOfFire: truncation is in ticks of the running rate, not in original frames | 20,23 |
| 113-118 | WeaponInfo.ScaledByRateOfFire (DelayBetweenShotsWith, ClipReloadWith) | E | Run-time rounding: `floor(Ticks / RateOfFire-Multiplikator)`, at least 1 tick; computed per shot | Weapon.cpp WeaponTemplate::getDelayBetweenShots / getClipReloadTime: REAL_TO_INT_FLOOR(delay / bonusROF) | floor(2t/m) is not 2·floor(t/m): with a rate-of-fire bonus (veterans, upgrades, firing stages) the delays between shots at 60 Hz differ from those on the 30 Hz grid – the plan (§2.5) covers only the catalog rounding | 20,23 |
| 209-210, 263 | WeaponCatalog.Build: DelayBetweenShots (default SleepNone, Math.Max(SleepNone, delay)) | B/fallback | If DelayBetweenShots is missing or 0 ms, the delay between shots becomes 1 tick; only `ShotDelayValue.Min` is used (Max discarded) | Weapon.cpp WeaponTemplate::getDelayBetweenShots (the original rolls between min and max); the lower bound of 1 tick is own | On the real data 118 of 363 weapons (29 without the key, 89 with 0 ms): their minimum delay would fall from 33 ms to 17 ms if the lower bound stays one tick instead of one original frame | 20 |

**`Sim\Time/SimulationTiming.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 21 | SimulationTiming.LogicFramesPerSecond | A | The central constant `const int = 30`; compiled as a `const` into every use (also into other assemblies) | GameCommon.h: LOGICFRAMES_PER_SECOND = 30 | Must become a value of the world; each of the 63 uses in the simulation depends on it | 20 |
| 25-28 | SimulationTiming.SleepNone / SleepForever | C | `SleepNone = 1` (smallest sleep one tick, 273 uses) and `SleepForever = 0x3fffffff` (190 uses); comment "about 414 days at 30 Hz" | UpdateModule.h: UPDATE_SLEEP_FOREVER = 0x3fffffff | "Every tick" means twice as often per second at 60 Hz: where a module does something fixed per wake-up, that changes results (individual sites in the module tables); SleepForever stays | 20,23 |
| 33-46 | SimulationTiming.DurationMillisecondsToTicks (long and decimal) | B/daten | ms → whole ticks, rounded up (`ceil(ms·30/1000)`), exact in integers or in `decimal`; static function without a rate parameter | INI.cpp INI::parseDurationUnsignedInt (ceilf(ConvertDurationFromMsecsToFrames)) | According to the plan: first round up to original frames, then × rate/30; direct rounding at 60 Hz would give other times (§4.4) | 20 |
| 49-53 | SimulationTiming.DurationMillisecondsToFixedTicks | B/daten | ms → fractional ticks without rounding (`Fixed`), 4 callers | INI.cpp INI::parseDurationReal | Scales linearly; only Fixed resolution | 20 |
| 56-62 | SimulationTiming.SecondsToTicks | B/daten | Seconds → whole ticks, rounded up (`ceil(s·30)`); for BuildTime of objects and upgrades | Origin not verified (rounding rule of the build time not looked up in the original) | Like durations: round on the 30 Hz grid, then scale; on the real data no difference measured (§4.4) | 20 |
| 64-71 | SimulationTiming.PerSecondToPerTick / PerSecondSquaredToPerTickSquared / DegreesPerSecondToRadiansPerTick | B/daten | Speed ÷ 30, acceleration ÷ 900 (`LogicFramesPerSecond * LogicFramesPerSecond`), angular speed ÷ 30; rounding to `Fixed` (nearest value) | INI.cpp INI::parseVelocityReal / parseAccelerationReal / parseAngularVelocityReal | At 60 Hz halved or quartered values per tick: Q47.16 becomes coarse for accelerations (Phase 22); the order divide/round must stay the same at 30 Hz | 20 |

### 5.2 World (`Sim\World`)

**`Sim\World/Entity.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 61 | Entity.Speed | E | Forward speed as distance per tick (field comment "dist/tick"), is written in the snapshot and read in Collision.cs as toppling force | own | Numeric value halves at the same speed; every reader that interprets Speed as an absolute quantity behaves differently. | 20,23 |

**`Sim\World/SimulationWorld.Acquisition.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 35 | SimulationWorld.OrderedAttackGraceTicks | B/literal | Grace period 45 ticks (1.5 s): an ordered attack counts as finished if no fire/pursue command came for that long after readiness to fire; 2 uses (ll. 153, 339, there additionally "- 1") | own | Deadline shrinks to 0.75 s: ordered attacks end earlier, self-acquisition sets in earlier. | 20 |
| 38 | SimulationWorld.ForceIdleTicks | B/fallback | ForceIdleMSEC (AIData.ini 67 ms, not typed) written fixed as 2 ticks: waiting time until the first look after entering a searching state; 2 uses (ll. 163, 179) | GameLogic/AI/AI.cpp field table ForceIdleMSEC -> m_forceIdleFramesCount (INI::parseDurationUnsignedInt, ceil; default 1); used in GameLogic/Object/Update/AIUpdate.cpp AIUpdateInterface::resetNextMoodCheckTime; value 67 ms not verified under references | Waiting time halves to 33 ms; no rounding, because not computed from milliseconds (ceil(67 ms) would incidentally be 3 at 30 Hz, not 2). | 20 |
| 41 | SimulationWorld.ChaseRefreshTicks | C | Pursuit is re-aimed at the victim's position every 15 ticks (0.5 s) (StartInternalMove); 1 use (l. 289) | own | Results: replanning every 0.25 s, i.e. twice as many pathfinding runs and different paths/wake times. | 20 |
| 111-131 | SimulationWorld.StepAcquisition | C | One pass per tick over all acquirers in id order; one AutoFire attempt per tick, in AutoFire (l. 466) _turretWaits++ per tick in which a turret is still turning | own | Results: shot times on a finer grid, counter turretWaits (in the snapshot) counts ticks and doubles. | 20,23 |
| 208 | SimulationWorld.StepAcquirer (WakeAt tick + 1, resumption of the attack march) | C | AI module is woken for the next tick after StartInternalMove (tick + 1) | own | Latency only: march begins 1/60 s instead of 1/30 s later; order within the tick unchanged. | 23 |
| 236-248 | SimulationWorld.MoodCheckOffset | E | One-off random offset of the look rate in the range ±(rate >> 1) ticks, as a hash of (world seed, entity id, tick) instead of from the logic stream; added to tick + rate (l. 189) | GameLogic/Object/Update/AIUpdate.cpp AIUpdateInterface::getNextMoodTarget (GameLogicRandomValue(-halfRate, halfRate)); the hash itself is own | Different tick numbers and a doubled value range yield different offsets; the draw is not rate-independent, acquisition times shift. | 23 |
| 292 | SimulationWorld.Engage (WakeAt tick + 1, pursuit) | C | AI module is woken for the next tick after StartInternalMove towards the victim (tick + 1) | own | Latency only: pursuit begins 1/60 s instead of 1/30 s later. | 23 |

**`Sim\World/SimulationWorld.Collision.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 37-52,95-109 | SimulationWorld.AvoidLocalBlockers / StepBlocked | E | Blockage check at the end point of a tick step (dx, dy = distance of one tick, caller Phase5Modules.cs l. 1117); the look-ahead is exactly one tick of distance, the avoidance step is just as long | own | Look-ahead and avoidance step halve: avoidance sets in later and closer to the obstacle, trajectories change. | 23 |
| 58-92 | SimulationWorld.StationaryBlockersAhead | E | Look-ahead = passed vector, checked at four sample points; the caller (Phase5Modules.cs l. 1109, not in this package) passes Speed per tick × 25; radius 150 is a distance | own | If the caller stays with "Speed × 25", the look-ahead halves (25 ticks of distance); stationary blockers are detected later. | 23 |
| 112-124,204-218 | SimulationWorld.ResolveFootprintCollisions / TrySquish | E | Overlap, topple and crush test once per movement step of the mover; "is approaching" is determined from the actual tick offset (stepX/stepY) | own | Twice as many sample points per distance: overlap is detected up to half an old step earlier, time/order of collision events changes. | 23 |
| 193 | SimulationWorld.TryTopple (mover.Speed) | E | Toppling force = mover.Speed (distance per tick) is passed unchanged to ToppleUpdate.ApplyTopplingForce | origin not verified | With scaled Speed (distance per tick halved) half the force arrives, unless the consumer converts; toppling behaviour changes. | 20,23 |

**`Sim\World/SimulationWorld.Construction.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 18 | SimulationWorld.SaleDurationTicks | A | 179 ticks from the sell command to the refund (45 frames scaffold + 135 frames until -50 % - 1); 1 use (l. 275, SellCompleteTick = _tick + 179) | Common/System/BuildAssistant.cpp: FRAMES_TO_ALLOW_SCAFFOLD (LOGICFRAMES_PER_SECOND * 1.5f), TOTAL_FRAMES_TO_SELL_OBJECT (LOGICFRAMES_PER_SECOND * 3.0f), BuildAssistant::update (threshold -50 %) | Sale takes just under 3 s instead of 6 s; the scaling is not linear (2 × 180 - 1 = 359, not 358). | 20 |
| 227-242 | SimulationWorld.AdvanceConstruction (Entity.ConstructionWork) | E | ConstructionWork++ per work tick of the builder up to Template.BuildTicks; health rises per tick by the integer difference of the linear target curve | origin not verified | With scaled BuildTicks equally long, only finer health steps; unscaled twice as fast. constructionWork in the snapshot is a tick count. | 20 |

**`Sim\World/SimulationWorld.CreationLists.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 136 | SimulationWorld.ExecuteDeliverPayload (DelayDeliveryMaxTicks) | E | Random delay of the delivery: LogicRandom.Range(0, DelayDeliveryMaxTicks) in whole ticks, one draw per formation member | origin not verified | Doubled value range: same number of draws, but different values; the delay in seconds is not bit-identical to the 30 Hz run. | 23 |
| 343 | SimulationWorld.IsSignificantlyAbove | A | Threshold -9 × Gravity / 900: fall height of 3 frames (3 × 3 frames² times gravity per frame²); Gravity is given in units/s², the 900 is 30²; decimal computation on every call (run time) | Common/Thing/Thing.cpp Thing::isSignificantlyAboveTerrain (-(3*3)*TheGlobalData->m_gravity, "more than 3 frames") | Unchanged, the height stays the same (0.1 s of fall). If only 900 is replaced by rate², it quarters: the 3 frames must be scaled along (many callers: air/ground decisions). | 20 |

**`Sim\World/SimulationWorld.FiringTracker.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 166 | SimulationWorld.PreAttackStaleTicks | B/literal | = SimulationTiming.LogicFramesPerSecond (30 ticks = 1 s): expiry deadline of a wind-up that was not followed by a shot; 1 use (l. 178) | own | Follows the rate constant; bound as a const to the compile-time value 30, with a run-time parameter it would otherwise be 0.5 s. | 20 |
| 234-257 | SimulationWorld.StepFiringTrackers | C | All trackers with a running cool-down timer are polled in every tick (the reference sleeps via UPDATE_SLEEP instead) | GameLogic/Object/FiringTracker.cpp FiringTracker::update | Load only: the comparison tick > CooldownFrame works with absolute ticks, the result does not depend on the polling cadence. | 20 |
| 247 | SimulationWorld.StepFiringTrackers (CooldownFrame = tick + LogicFramesPerSecond) | A | Duration of the Slow stage after the run-down time has elapsed: tick + SimulationTiming.LogicFramesPerSecond (1 s) | GameLogic/Object/FiringTracker.cpp FiringTracker::update (m_frameToStartCooldown = now + LOGICFRAMES_PER_SECOND) | Stays 1 s as soon as the constant is the rate parameter; as a fixed 30 it would be 0.5 s. | 20 |

**`Sim\World/SimulationWorld.Projectiles.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 125-134 | SimulationWorld.FlightTicks | E | Flight time of a projectile-less shot = ceil(distance / WeaponSpeed per tick) on raw values; 0 (instant damage in the same tick) if distance < one tick of distance | GameLogic/Object/Weapon.cpp WeaponTemplate::fireWeaponTemplate (delayInFrames, "< 1.0f", REAL_TO_INT_CEIL) | Instant-damage threshold halves and rounding up is done to 1/60 s: damage lands up to one old tick earlier, close-range shots are no longer instant. | 20,23 |

**`Sim\World/SimulationWorld.Snapshot.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 36-45 | SimulationWorld.LoadFull | G | Loads the absolute tick and all tick fields (entities, players, spawn queue, delayed damage) without checking a tick rate; there is no rate field in this file | - | A 30 Hz snapshot would be continued at 60 Hz without comment with misinterpreted absolute ticks and durations. | 20 |

**`Sim\World/SimulationWorld.Turrets.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 21 | SimulationWorld.TurretAimGraceTicks | B/literal | Minimum hold time of a one-off aim after the last request: 15 ticks (0.5 s); 2 uses (ll. 128, 283), each Math.Max(info.RecenterTicks, 15) | own | Unscaled only 0.25 s: the turret releases a one-off target twice as early; no rounding problem (15 → 30). | 20 |
| 24 | SimulationWorld.TurretReloadHoldCapTicks | B/literal | Upper bound of the reload that extends the hold time: 450 ticks (15 s); 2 uses (l. 282 and SimulationWorld.Acquisition.cs:339 as clamp bound) | own | Unscaled 7.5 s instead of 15 s: turrets with a long reload (and the attack deadline in Acquisition) let go earlier. | 20 |
| 27 | SimulationWorld.TurretSweepEnableTicks | A | Sweep (TurretFireAngleSweep) stays on for 3 ticks after a shot; 1 use (l. 286: SweepUntil = _tick + 3), comparison tick < SweepUntil in AimStep l. 428 | GameEngine/Source/GameLogic/AI/TurretAI.cpp, local constant ENABLE_SWEEP_FRAME_COUNT = 3 (l. 724, m_enableSweepUntil = now + …) | Unscaled only a 50 ms sweep window instead of 100 ms: sweep weapons cover less angle, hit pattern changes. | 20 |
| 30 | SimulationWorld.TurretLoseInterestTicks | B/literal | Deadline after which a stationary attacker gives up a victim out of range: 45 ticks (1.5 s); 1 use (l. 265) | own | Unscaled 0.75 s: attacks without pursuit end earlier, turrets recentre earlier, AI courses change. | 20 |
| 38 | SimulationWorld._turretWaits | E | Counter of withheld fire commands (incremented in SimulationWorld.cs l. 1051 and WeaponSlots.cs l. 411), part of the state hash as soon as it is not equal to 0 | own | Counts per fire command while turning in; if commands come per tick, the value doubles, hash and goldens change. | 23 |
| 257-267 | SimulationWorld.KeepsAttackIntent | E | Checked per tick: vision (CanPlayerSee, fog only current every 3 ticks) and range; deadline starts at the first tick out of range, ends at tick >= LoseInterestAt | own | Abort time depends on the vision grid and on the tick boundary; at 60 Hz finer sampling, abort can fall up to half an old tick earlier. | 23 |
| 295-302 | SimulationWorld.IsAligned (TurretAlignThreshold 0.035 rad) | E | Fire clearance: angular distance at most 0.035 rad (or smaller than the sweep angle) and pitch at most 0.035 rad; checked per fire command against the turret angle stepped in the previous tick | TurretAI.cpp, REL_THRESH = 0.035f (l. 1093) and fabs(angleDiff) < sweep (l. 1104) | Threshold is a fixed angle, but the step is half as large: first shot falls on a different (finer) time, angle at the command is only 1/60 s old. | 23 |
| 340-367 | SimulationWorld.StepTurrets | C | Every turret that is not at rest or can scan is stepped every tick (id order), after RunUpdates and StepAcquisition | own | Results: yes – twice as many turn/deadline steps; only correct if rates and deadlines are scaled; load doubled. | 20,23 |
| 396 | SimulationWorld.StepTurret (holding) | E | Holding applies as long as tick <= HoldUntil, i.e. including the deadline tick (RecenterTicks + 1 steps); the reference leaves the hold state at now >= m_timestamp | TurretAI.cpp ll. 1439/1459 (m_timestamp = getFrame() + getRecenterTime(); if (getFrame() >= m_timestamp)) | The one extra step is 16.7 ms instead of 33.3 ms: hold time not exactly rate-invariant (deviation by one tick). | 23 |
| 421-438 | SimulationWorld.AimStep | E | Turret follows the victim per tick by TurnRate (rad/tick, times SweepSpeed during the sweep); sweep side flips as soon as the angle is at most 0.035 rad from the target; then pitch step | TurretAI.cpp, friend_turnTowardsAngle(aimAngle, turnSpeedModifier, REL_THRESH) ll. 1093–1094 | Half step size: different snap and reversal times, tracking error against moving targets/hull halved; with an unscaled 3-tick window less sweep travel. | 20,23 |
| 454-471 | SimulationWorld.IdleScanStep | E | Idle scan: first step only schedules NextScan; at tick >= NextScan two hash random values are drawn (angle, side) and the first turn step is made in the same tick | TurretAI.cpp l. 1315 (now >= m_nextIdleScan) and ll. 1369–1370 (GameLogicRandomValueReal / GameLogicRandomValue(0,1)) | Draws depend on the counter ScanDraws, not on the tick; times however on NextScan in ticks – without scaling scans twice as frequent. | 20,23 |
| 495-496 | SimulationWorld.ScheduleScan | E | Next scan = tick + integer random number from [MinScanIntervalTicks, MaxScanIntervalTicks] (hash of seed, entity, turret, draw counter; modulo over the span in ticks) | TurretAI.cpp, TurretAIIdleState::resetIdleScan (GameLogicRandomValue(getMinIdleScanInterval(), getMaxIdleScanInterval()), ll. 1285–1289) | Span in ticks doubles, the modulo yields different values: scan times cannot be derived from a 30 Hz run. | 20,23 |
| 530-544 | SimulationWorld.TurnAngle | E | Snap when the remaining angle is smaller than one tick step (rate), otherwise step by ±rate; sets Rotating; also for recentre/scan with factor 0.5 (TurretRecenterRateFactor, dimensionless) | TurretAI.cpp, TurretAI::friend_turnTowardsAngle (fabs(angleDiff) < turnRate; factor 0.5f in ll. 1236, 1388) | Snap window halves with the rate; arrival tick, Rotating flag (model state) and follow-up states shift by fractions of a 30 Hz tick. | 23 |
| 547-551 | SimulationWorld.TurnPitch | E | Pitch snaps when the remainder is smaller than one tick step (PitchRate rad/tick), otherwise step by ±rate | TurretAI.cpp, TurretAI::friend_turnTowardsPitch (ll. 432–458) | Like TurnAngle: half snap window, pitch clearance (IsAligned) at a different time. | 23 |

**`Sim\World/SimulationWorld.Vision.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 12 | SimulationWorld.VisionIntervalTicks | C | Vision/fog is recomputed every 3 ticks (tick % 3 == 0 in SimulationWorld.Step l. 1678, at the end of the tick after spawn); second use in src/CnCNew.Game/Live/LiveGame.cs:211 (fog presentation); comment states under 100 ms | own (comment refers to the partition cell timer of the reference; not looked up there) | Results: yes – CanPlayerSee (targetability, command validation, AI vision, turret abort) and visionHash change every 50 ms instead of 100 ms; load of RefreshVision doubled. | 20,21 |

**`Sim\World/SimulationWorld.WeaponSlots.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 330-332 | SimulationWorld.ConsumeShot (ready) | B/daten | Next shot = _tick + DelayBetweenShotsWith(…) or ClipReloadWith(…): tick values of the catalog are divided at run time per shot by the rate-of-fire bonus (WeaponCatalog.ScaledByRateOfFire: floor, at least 1 tick) | - | Rounding: run-time floor to whole ticks and lower bound 1 tick; at 60 Hz finer quantisation and doubled maximum rate of fire (1 shot per tick) – effective rates of fire with bonus change. | 20,23 |

**`Sim\World/SimulationWorld.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 597-607 | SimulationWorld.Submit / Validate (command.Tick) | G | Commands are addressed to absolute ticks (_commands by command.Tick, late check command.Tick < _tick, execution in ExecuteCommands(tick)); the command stream is the replay | own | Command streams recorded at 30 Hz hit half the point in time at 60 Hz; callers with a fixed tick lead (input latency) halve it. | 20,21 |
| 908-918 | SimulationWorld.Execute (SetTimerCommand: DueTick = _tick + set.DelayTicks, RepeatTicks) | G | Timer command carries delay and repetition in ticks (command field, ends up in the SimTimer and thus in the snapshot); repetition in FireTimers l. 1276: DueTick = tick + RepeatTicks | own | Timers from command streams/producers with fixed tick counts run twice as fast; producers must compute in the new rate. | 20 |
| 1298-1318 | SimulationWorld.RunUpdates | C | Sleepy scheduler: a return value below SleepNone is raised to 1 tick, wake = tick + sleep; order (wake tick, entity id, module index) | own | Results: yes – every module with return value 1 runs 60 times per second, relative sleep values in ticks halve their duration unless the modules scale; load rises. | 20,23 |
| 1460-1468 | SimulationWorld.ProcessSpawns | E | Spawns and destroys only take effect at the end of the tick; spawns requested during the spawn phase (death on creation → follow-up spawn) stay in the queue until the next tick | own | Every chain link costs one tick: at 60 Hz half the delay (16.7 ms instead of 33.3 ms) per link. | 23 |
| 1645 | SimulationWorld.Register (NextWake = _tick + SleepNone) | E | Update modules without a set NextWake run for the first time one tick after registration (spawn at the end of tick t, _tick is still t → first update in t+1) | own | "One tick later" is 16.7 ms instead of 33.3 ms; order stays, start-up delay of new objects halves. | 23 |
| 1662-1681 | SimulationWorld.Step | E | Fixed phase sequence per tick: commands, timers, updates, target acquisition, turrets, firing stages, events, sales, destroys, delayed damage, spawns, events, vision (every 3rd tick), tick+1, hash | own | Effects between phases (turret angle after movement, damage after destroy, vision after spawn) are offset by exactly one tick; this offset halves, the order itself is rate-independent. | 23 |

### 5.3 Modules (`Mod\`)

**`Mod\AirModules.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 79 | ParkingPlaceBehavior.HealRateTicks | A | Healing interval of the airfield as const LogicFramesPerSecond / 5 = 6 ticks (3 uses: ll. 382, 402, 403) | ParkingPlaceBehavior.cpp, constant HEAL_RATE_FRAMES = LOGICFRAMES_PER_SECOND / 5 | Bound to the rate as a const; with a parametric rate it must become rate/5 at run time (60 Hz: 12 ticks), otherwise a healing step every 0.1 s instead of 0.2 s. | 20 |
| 396-408 | ParkingPlaceBehavior.Update (return SleepNone) | C | Runs every tick: purge of dead occupancies, arrival markers, healing cadence via _nextHeal | ParkingPlaceBehavior.cpp, update (UPDATE_SLEEP_NONE) | Load only (twice as many purge runs); the results depend on the _nextHeal cadence, not on the call cadence. | 20 |
| 399 | ParkingPlaceBehavior.Update (_arrivals.RemoveAll, > 2) | B/literal | Arrival marker of a produced aircraft expires after more than 2 ticks (window for TryAdoptArrival) | own | Window halves in time; what is meant is tick order (spawn in the following tick), so probably do not scale – check. | 20 |
| 403 | ParkingPlaceBehavior.Update (amount) | B/daten | Healing amount per healing step = HealRateTicks · HealAmountPerSecond / LogicFramesPerSecond, computed at run time per healing event (raw value from ModuleConfigReader.Real, l. 50) | ParkingPlaceBehavior.cpp, update: HEAL_RATE_FRAMES * m_healAmount * SECONDS_PER_LOGICFRAME_REAL | Invariant only if HealRateTicks and divisor are scaled together; if only one is adapted, the healing rate doubles or halves. | 20 |

**`Mod\AirWeaponModules.cs`** (plus 9 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 95-102 | AirWeaponModules.FixedTicks | B/daten | Duration in ms as unrounded Fixed ticks (DurationMillisecondsToFixedTicks) at parse time; users: Blast1..9Delay/ScorchDelay (l. 699) | NeutronMissileSlowDeathUpdate.cpp, field table BlastNDelay/BlastNScorchDelay: INI::parseDurationReal | Different rounding than ceil (fractional ticks); the comparison in l. 758 decides the actual triggering. | 20 |
| 114 | EmpData.Parse "Lifetime" | B/fallback | Fallback 1 tick for Lifetime (ModuleConfigReader.Ticks) | EMPUpdate.h, EMPUpdateModuleData constructor: m_lifeFrames = 1 | Stays 1 tick = 1/60 s instead of 1/30 s; only effective if the field is missing in the data. | 20 |
| 163 | EmpUpdate.Update (_scale) | E | Scale approaches the target value by 5 % per tick (factor 0.05 per tick); the value is in the state (scale) | EMPUpdate.cpp, EMPUpdate::update: m_currentScale += ( m_targetScale - m_currentScale ) * 0.05f | Approach runs in half the time; affects state hash and display of the pulse, no gameplay effect. | 23 |
| 230 | LeafletData.Parse "Delay" | B/fallback | Fallback 1 tick for Delay (ModuleConfigReader.Ticks) | EMPUpdate.h, LeafletDropBehaviorModuleData constructor: m_delayFrames = 1 | Stays 1 tick; together with ll. 254-259 the tick count decides the behaviour (see there). | 20 |
| 254-259 | LeafletDropBehavior.Update | E | First update (1 tick after spawn) before Delay has elapsed leads to SleepForever (effect only on death); a Delay of at most 1 tick leads to DoDisableAttack every tick | EMPUpdate.cpp, LeafletDropBehavior::update: getFrame() < m_startFrame yields UPDATE_SLEEP_FOREVER | A Delay that yields 1 tick at 30 Hz (up to 33 ms) becomes 2 ticks at 60 Hz: the module sleeps forever instead of acting every tick – change of result. | 20,23 |
| 306 | BunkerBusterData.Parse "CrashThroughBunkerFXFrequency" | B/fallback | Fallback 4 ticks (ModuleConfigReader.Ticks) | BunkerBusterBehavior.cpp, BunkerBusterBehaviorModuleData constructor: m_crashThroughBunkerFXFrequency = 4 | Without a data value twice as many effects per second. | 20 |
| 334 | BunkerBusterBehavior.Update (world.Tick % CrashFxFrequencyTicks == 1) | C | Impact FX event on the global tick grid (remainder 1) | BunkerBusterBehavior.cpp, BunkerBusterBehavior::update: getFrame() % m_crashThroughBunkerFXFrequency == 1 | Results: number and time of the FxRequested events, phase at the global tick; with frequency 1 tick it never fires (remainder 1 impossible). | 20,21 |
| 459 | NeutronMissileData.Parse "MaxTurnRate" (Fallback) | D | Missing MaxTurnRate yields TwoPi·100 radians per tick without conversion (existing value via DegreesPerSecondToRadiansPerTick) | NeutronMissileUpdate.cpp, NeutronMissileUpdateModuleData constructor: m_maxTurnRate = 999.0f | Practically without effect: every value from Pi upwards makes the direction snap immediately (l. 626). | 20 |
| 463 | NeutronMissileData.Parse "ForwardDamping" | D | Raw damping share of the velocity per tick (ModuleConfigReader.Real) | NeutronMissileUpdate.cpp, field table INI::parseReal; doAttack: damping * m_vel per frame | Damping acts twice as often per second: terminal velocity and settling time of the missile change. | 20,23 |
| 464 | NeutronMissileData.Parse "RelativeSpeed" | D | Raw thrust: velocity gain per tick (distance per tick²), default 1 | NeutronMissileUpdate.cpp, constructor m_relativeSpeed = 1.0f, field table INI::parseReal; doAttack | Without scaling fourfold acceleration per second²; trajectory and flight time of the missile change. | 20,23 |
| 521-562 | NeutronMissileUpdate.Update | E | Per tick: intermediate point counts as reached at a distance up to the bounding sphere radius (threshold against tick step); launch costs one tick; _noTurnLeft decreases by the tick distance; ground contact per tick | NeutronMissileUpdate.cpp, NeutronMissileUpdate::update | With a different tick step the intermediate point is hit at a different place or missed; impact tick and location shift. | 23 |
| 591-615 | NeutronMissileUpdate.DoAttack | E | Euler step: _velocity += speed·direction − ForwardDamping·_velocity, position += _velocity (velocity per tick); special phase: height from elapsed/SpecialSpeedTicks | NeutronMissileUpdate.cpp, NeutronMissileUpdate::doAttack (m_accel, m_vel, pos += m_vel) | Not step-invariant: terminal velocity speed/damping applies per tick, at 60 Hz therefore twice as fast per second; the special phase itself is correct in time. | 20,23 |
| 739-741 | NeutronMissileSlowDeathBehavior.BeginSlowDeath | B/daten | Random draws Range(0, VarianzTicks) on a tick basis for sink and destruction delay at run time, at least 1 tick | origin not verified | Value range of the random draw doubles: the same draw yields a different delay. | 20,23 |
| 758 | NeutronMissileSlowDeathBehavior.Update (Tick − _activationTick > DelayTicks) | B/daten | Blast stage triggers when the elapsed whole ticks are strictly greater than the unrounded delay (acts like floor + 1) | NeutronMissileSlowDeathUpdate.cpp, update: currFrame - m_activationFrame > m_blastInfo[ i ].delay | Different rounding than ceil: triggering up to one tick after the nominal time; at 60 Hz smaller offset, stages with close spacing can fall on different ticks. | 20 |
| 894 | MissileLauncherBuildingUpdate.SwitchTo (AvailableOnFrame − 1) | A | Door is open 1 tick before the special power is ready (literal 1) | MissileLauncherBuildingUpdate.cpp, switchToState: m_timeoutFrame = m_specialPowerModule->getReadyFrame() - 1 | Lead halves in time; what is meant is "one frame before", so do not scale. No gameplay effect (only door state and FX). | 20 |
| 958 | SpectreData.Parse "HowitzerFiringRate" | B/fallback | Fallback 10 ticks, additionally Math.Max(…, 1) | SpectreGunshipUpdate.cpp, SpectreGunshipUpdateModuleData constructor: m_howitzerFiringRate = 10 | Without a data value doubled target search and howitzer rate per second. | 20 |
| 962 | SpectreData.Parse "StrafingIncrement" | D | Raw travel of the Gatling target point per tick (default 20), used in Strafe() | SpectreGunshipUpdate.cpp, constructor m_strafingIncrement = 20.0f, field table INI::parseReal; update (delta.scale) | Target point moves twice as fast per second; the howitzer sets in earlier. | 20,23 |
| 1129-1140 | SpectreGunshipUpdate.Fly | E | Kinematic Euler step per tick: turn by TurnRatePerTick, Speed ± Acceleration/Braking, position += Speed; target anew per tick; switch to orbit at distance < radius (l. 1076) | own | Trajectory is a polyline: a different tick step changes orbit path, entry tick and thus the shot sequence. | 23 |
| 1151 | SpectreGunshipUpdate.Orbit (world.Tick % HowitzerFiringRateTicks == 0) | C | Target search and howitzer shot on the global tick grid; 2 random draws per shot | SpectreGunshipUpdate.cpp, update: getFrame() % m_howitzerFiringRate < ONE | Results: number of shots and random draws depends on the tick count of the interval and on the phase relative to the global tick. | 20,23 |
| 1162 | SpectreGunshipUpdate.Orbit (_okToFireHowitzerCounter > HowitzerFollowLagTicks) | E | Counter rises by 1 per strafe tick and is compared with a duration in ticks | SpectreGunshipUpdate.cpp, update: m_okToFireHowitzerCounter > data->m_howitzerFollowLag | Correct in time only as long as Strafe() really runs every tick (depends on l. 1176); otherwise the howitzer is delayed. | 23 |
| 1176 | SpectreGunshipUpdate.Orbit (Tick − LastFireTick <= 2) | B/literal | "Gatling is currently firing" = last shot at most 2 ticks ago (substitute for OBJECT_STATUS_IS_FIRING_WEAPON of the original) | own | Window halves: with delays between shots above 2 ticks (60 Hz) the strafing stops, counter and howitzer run differently. | 20,23 |
| 1183-1198 | SpectreGunshipUpdate.Strafe | E | Target point advances per tick by StrafingIncrement; arrived when the remaining distance is smaller than one tick step | SpectreGunshipUpdate.cpp, update: dist < data->m_strafingIncrement | Arrival radius is one tick step: without scaling doubled speed, with scaling half the radius. | 23 |

**`Mod\BattlePlanModule.cs`** (plus 5 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 110-135 | BattlePlanUpdate.Update | E | State machine: one transition per tick, locked until _nextReadyTick; Packing→Idle→Unpacking additionally cost 1 tick each | BattlePlanUpdate.cpp, update returns UPDATE_SLEEP_NONE (transition logic itself not cross-checked) | The extra ticks between the phases halve in time; a plan change ends a few ticks earlier. | 23 |

**`Mod\BridgeModules.cs`** (plus 1 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 29 | BridgeData.Parse "LateralScaffoldSpeed" | D | Raw value / LogicFramesPerSecond by hand (not PerSecondToPerTick); fallback 1 per second = 1/30 per tick | BridgeBehavior.cpp, field table INI::parseVelocityReal; constructor m_lateralScaffoldSpeed = 1.0f (per frame, unconverted) | Manual division must be parameterised along; the fallback already deviates from the original (1.0 per frame) today. | 20 |
| 30 | BridgeData.Parse "VerticalScaffoldSpeed" | D | Raw value / LogicFramesPerSecond by hand (not PerSecondToPerTick); fallback 1 per second = 1/30 per tick | BridgeBehavior.cpp, field table INI::parseVelocityReal; constructor m_verticalScaffoldSpeed = 1.0f (per frame, unconverted) | Manual division must be parameterised along; the fallback already deviates from the original (1.0 per frame) today. | 20 |
| 248 | BridgeScaffoldBehavior.AbsoluteMinSpeed | D | Minimum speed 0.001 per tick | BridgeScaffoldBehavior.cpp, update: if( speed < 0.001f ) speed = 0.001f | Doubled minimum speed per second; practically meaningless. | 20 |
| 256-257 | BridgeScaffoldBehavior._lateralSpeed/_verticalSpeed | D | Default speed 1 per tick until SetSpeeds is called; in the state as a per-tick value | BridgeScaffoldBehavior.cpp, constructor: m_lateralSpeed = 1.0f, m_verticalSpeed = 1.0f | Without SetSpeeds twice as fast per second; stored values are per tick. | 20 |
| 291-333 | BridgeScaffoldBehavior.Update | E | Per tick: position += direction·speed; speed in the last quarter proportional to the remaining distance (at least 8 %); target reached on overshooting | BridgeScaffoldBehavior.cpp, BridgeScaffoldBehavior::update (minSpeed = topSpeed * 0.08f) | Deceleration ramp is step-dependent (geometric decay per tick); the arrival tick changes. Scaffolds are currently driven by nothing. | 23 |

**`Mod\CleanupAndSpyModules.cs`** (plus 6 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 98-104 | CleanupHazardUpdate.Update (_nextScan) | C | Down counter per tick, reload with ScanTicks: one scan every ScanTicks + 1 ticks | CleanupHazardUpdate.cpp, update: m_nextScanFrames-- and m_nextScanFrames = data->m_scanFrames | Results: target choice; period 2N + 1 instead of 2(N + 1) ticks, i.e. somewhat shorter in time. | 20,23 |
| 129 | CleanupHazardUpdate.FireWhenReady (LogicRandom.Range(0, 3)) | A | Random waiting time 0..3 ticks until the rescan when the target goes out of range (one random draw per event) | CleanupHazardUpdate.cpp, fireWhenReady: m_nextScanFrames = GameLogicRandomValue( 0, 3 ) | Waiting time halves in time; when scaled to 0..6 the distribution of the random draw changes. | 20,23 |
| 426 | SpyVisionSpecialPower.Apply | B/daten | Duration at run time: BaseDurationTicks + ContainCount · BonusPerCapturedTicks, capped at MaxDurationTicks | origin not verified | The rounding up per bonus is multiplied by the number of occupants; at 60 Hz smaller rounding error, so slightly different total duration. | 20 |
| 505 | AssistedTargetingUpdate.ReloadWait | C | Upper bound 1000 ticks for a sleep while the slot reloads (2 uses: ll. 498, 501) | own | Halves in time (16.7 s); predominantly load, but determines when a hanging request (victim gone) ends and the module is free again. | 20 |

**`Mod\CombatModules.cs`** (plus 2 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 65-81 | FireWeaponUpdate.Update | C | Polls every tick (SleepNone) against _initialDelayTick, _readyTick and ExclusiveWeaponDelay | FireWeaponUpdate.cpp, update (UPDATE_SLEEP_NONE) | Load only; the shot times are given as absolute ticks from data durations. | 20 |

**`Mod\ContainChainModules.cs`** (plus 1 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 28-51 | ContainChainModules.RequestPayload/EnterPayload | E | Initial payload is created in the spawn tick and only enters in the following tick (order "one tick later"); affects RiderChangeContain and OverlordContain | own | The window without occupants halves in time; the order stays. | 23 |
| 380-391 | OverlordContain.Update | C | Superstructure follows position and direction of the Overlord every tick (SleepNone while redirected) | own | Load only; the lag of the superstructure is at most one tick and halves in time. | 20 |

**`Mod\ContainModules.cs`** (plus 1 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 280-286 | GarrisonContain.HealOne (TimeForFullHealTicks) | E | Healing per tick = MaxHealth / TimeForFullHealTicks, every tick (SleepNone); full jump as soon as Tick − since >= TimeForFullHealTicks | GarrisonContain.cpp healSingleObject (m_framesForFullHeal, getMaxHealth() / framesForFullHeal) | Scales along if TimeForFullHealTicks is converted to the rate (only Ceil rounding of the duration); if the tick count stays 30-based, it heals twice as fast. | 20,23 |
| 406-408 | TransportContain.Update (HealthRegen%PerSec, perTick) | E | Regeneration per tick = MaxHealth · percent / 100 / LogicFramesPerSecond, computed at run time every tick (SleepNone) | TransportContain.cpp (regen = getMaxHealth() * m_healthRegen / 100.0f * SECONDS_PER_LOGICFRAME_REAL) | Follows the constant LogicFramesPerSecond; as long as the 30 stays, double healing rate. With a parameter invariant except for Fixed rounding of the smaller slice. | 20 |

**`Mod\CrateModules.cs`** (plus 2 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 544 | SabotageMilitaryFactoryCrateCollide.Sabotage (return data.DurationTicks) | B/daten | The sabotage duration is passed to the outside as a tick count in the event value (SimEventKind.Sabotaged / CrateCollected) | own | Consumers of the event receive a number twice as large for the same time; whoever converts it to seconds with 30 shows double. | 21 |
| 561 | SabotagePowerPlantCrateCollide.Sabotage (return data.DurationTicks) | B/daten | The sabotage duration (SabotagePowerDuration) is passed to the outside as a tick count in the event value | own | As with the factory saboteur: event value in ticks, consumers outside the simulation must know the rate. | 21 |
| 608-611 | PilotFindVehicleUpdate.Update (scan = Max(ScanTicks, SleepNone)) | C | Scan cadence from ScanRate (ms, Ceil); with ScanRate 0 (default) a scan in every tick | origin not verified | With a data value time-invariant; with ScanRate 0 twice as many scans per second – result: pilot reacts up to 1/60 s earlier, otherwise load only. | 20 |

**`Mod\DieAndUpdateModules.cs`** (plus 2 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 181-182 | TimedExpiryModule.OnSpawn (delay, _minTicks/_maxTicks) | B/daten | Lifetime is drawn as a random draw over the tick range [MinTicks, MaxTicks] (integer, inclusive), minimum 1 tick | LifetimeUpdate.cpp LifetimeUpdate::calcSleepDelay (GameLogicRandomValue(minFrames, maxFrames); if (delay < 1) delay = 1) | Draw over twice the value range: other drawn values and finer resolution, hence other points in time; minimum 1 tick becomes 1/60 s. | 20,23 |

**`Mod\DockMachine.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 25 | DockMachine.ClearanceTimeoutTicks | B/literal | Waiting time for entry clearance 30 s as const 30L * LogicFramesPerSecond (= 900 ticks), 1 use (line 167, _enterTick + Timeout < world.Tick) | AIDock.cpp AIDockWaitForClearanceState::update (m_enterFrame + 30*LOGICFRAMES_PER_SECOND < getFrame()) | Bound to the rate as a const: must become a run-time value, otherwise 15 s instead of 30 s if the constant stays 30. | 20 |

**`Mod\DockModules.cs`** (plus 1 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 253-277 | DockUpdateBase.Update (SleepNone while occupied) | C | Dock checks its slots every tick and appoints the active docker; the docker sees the clearance only in its next update | - | Result: clearance latency of one tick halves in real time (1/60 s), otherwise load only. | 23 |
| 494 | AutoDepositUpdate.Update (Math.Max(DepositTimingTicks, SleepNone)) | C | Deposit interval from DepositTiming (ms, Ceil); with DepositTiming 0 (default) a deposit is made every tick | origin not verified | With a data value time-invariant (Ceil rounding); with DepositTiming 0 twice as much money per second – change of result. | 20 |

**`Mod\FerryModules.cs`** (plus 2 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 351 | RailedTransportDockUpdate.Action (_pullPerTick, Math.Max(PullInsideTicks, 1)) | E | Pull distance per tick = initial distance / PullInsideTicks; duration 0 is treated as 1 tick | RailedTransportDockUpdate.cpp (m_pullInsideDistancePerFrame = mag / m_pullInsideDurationInFrames) | Invariant if the duration is converted to the rate; the 1-tick lower bound then corresponds to 1/60 s. | 20,23 |
| 385-407 | RailedTransportDockUpdate.DoPullInDocking | E | Euler step per tick: docker is pulled by _pullPerTick towards the centre of the ferry until distance² <= 36 | RailedTransportDockUpdate.cpp (v.x *= m_pullInsideDistancePerFrame) | Half step size, arrival (6 units) can fall one tick earlier/later; stored pullPerTick is bound to the rate. | 23 |
| 427-452 | RailedTransportDockUpdate.DoPushOutDocking | E | Euler step per tick: rider is pushed by min(_pushPerTick, remainder) towards the exit point until distance² <= 9 | RailedTransportDockUpdate.cpp (v.x *= m_pushOutsideDistancePerFrame) | Half step size, arrival (3 units) shifts by fractions; stored pushPerTick is bound to the rate. | 23 |
| 481 | RailedTransportDockUpdate.UnloadNext (_pushPerTick, Math.Max(PushOutsideTicks, 1)) | E | Push distance per tick = distance to the exit point / PushOutsideTicks; duration 0 is treated as 1 tick | RailedTransportDockUpdate.cpp (m_pushOutsideDistancePerFrame = mag / m_pushOutsideDurationInFrames) | Invariant if the duration is converted to the rate; 1-tick lower bound becomes 1/60 s. | 20,23 |

**`Mod\FieldEffectModules.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 32 | PropagandaTowerData.Parse (DelayBetweenUpdates, Fallback 100) | B/fallback | Fallback 100 ticks for the scan interval if DelayBetweenUpdates is missing (ModuleConfigReader.Ticks(…, 100)); also serves as the lifetime of the healing claim | PropagandaTowerBehavior.cpp PropagandaTowerBehaviorModuleData (m_scanDelayInFrames = 100, parseDurationUnsignedInt) | Fallback stays 100 ticks = 1.67 s instead of 3.33 s; scan and claim duration halve where the key is missing. | 20 |
| 168 | PropagandaTowerBehavior.Give (HealPerSecond / LogicFramesPerSecond) | E | Healing per tick = rate per second / LogicFramesPerSecond · MaxHealth, at run time every tick per object | PropagandaTowerBehavior.cpp PropagandaTowerBehavior::effectLogic (healthPercent / LOGICFRAMES_PER_SECOND * body->getMaxHealth()) | Follows the constant; if it stays 30, double healing rate. With a parameter invariant except for Fixed rounding. | 20 |
| 273 | TensileFormationUpdate.Lifetime | A | Sliding duration of the avalanche 300 ticks (++_life > Lifetime, 1 use); _life counts updates, not time | TensileFormationUpdate.cpp TensileFormationUpdate::update (if (++m_life > 300)) | The avalanche slides only 5 s instead of 10 s; Life is also read by the presentation (threshold 100 according to the comment). | 20,21 |
| 274 | TensileFormationUpdate.Friction | D | Friction factor 0.95 per tick, multiplicative on the inertia (lines 319–320) | TensileFormationUpdate.cpp TensileFormationUpdate::update (Real friction = 0.95f) | Damping acts twice as often per second: different final speed and sliding distance. | 20,23 |
| 275-276 | TensileFormationUpdate.NewShare / LinkShare | D | Pull towards the neighbour 7 % per link and tick (0.93 / 0.07), blend factor per update | TensileFormationUpdate.cpp TensileFormationUpdate::update (newPos.x*0.93f + desiredPos.x*0.07f) | The formation contracts twice as fast; trajectories of the rocks change. | 20,23 |
| 277 | TensileFormationUpdate.BaseSlope | D | Slope acceleration per tick: inertia += normal · (0.3 + steepness), without time conversion | TensileFormationUpdate.cpp TensileFormationUpdate::update (slope.scale( 0.3f + steepness)) | Acceleration is added twice as often per second; together with the friction a different speed. | 20,23 |
| 310 | TensileFormationUpdate.Update (return 30) | A | Sleep interval 30 ticks of the resting rock between two checks for damage/impulse | TensileFormationUpdate.cpp TensileFormationUpdate::update (return UPDATE_SLEEP(30)) | Check every 0.5 s instead of 1 s: the avalanche triggers earlier on average (result, not only load). | 20 |
| 314 | TensileFormationUpdate.Update (_life % 30 == 29) | A | Passing on of the impulse to neighbours every 30 sliding ticks (first after 29) | TensileFormationUpdate.cpp TensileFormationUpdate::update (if ( m_life%30 == 29 )) | Spread of the avalanche twice as fast in real time, but with half the sliding duration the same number of passings-on. | 20 |
| 316-336 | TensileFormationUpdate.Update (physics block) | E | Euler step per tick: inertia += slope, · friction, position += inertia (units per tick), then blending with the links and ground height | TensileFormationUpdate.cpp TensileFormationUpdate::update | Not step-invariant: at 60 Hz a different trajectory, speed and final position of every rock; inertiaX/Y are stored per tick. | 23 |

**`Mod\FlightModules.cs`** (plus 15 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 127 | JetSlowDeathBehavior.BeginSlowDeath (SinkDelayVarianceTicks) | B/daten | Random draw over 0..SinkDelayVarianceTicks (integer in ticks); result is discarded, the draw counts for the random stream | origin not verified | Draw over twice the range: different state of the random stream than at 30 Hz. | 20,23 |
| 128 | JetSlowDeathBehavior.BeginSlowDeath (DestructionDelayVarianceTicks) | B/daten | Time of destruction = DestructionDelayTicks + random draw over 0..VarianceTicks (integer in ticks), minimum 1 tick | origin not verified | Draw over twice the range: different time of destruction and random stream; minimum becomes 1/60 s. | 20,23 |
| 142-155 | JetSlowDeathBehavior.Update (fall block) | E | Euler per tick: X/Y += direction · Entity.Speed (per tick), _fallSpeed += GravityPerTickSquared · FallHowFast, Z += _fallSpeed; impact at Z <= ground | own | Half steps: impact time and place deviate slightly (semi-implicit Euler); fallSpeed is stored per tick. | 23 |
| 291 | CountermeasuresBehavior.ReportMissile (Math.Max(ReactionTicks, 1)) | B/literal | Reaction latency at least 1 tick (ReactionLaunchLatency 0 becomes 1 tick), because 0 serves as "not set" | own | Lower bound becomes 1/60 s instead of 1/30 s; first volley with latency 0 half an old tick earlier. | 20 |
| 345-369 | CountermeasuresBehavior.MoveFlares | E | Per tick: flare velocity −= Locomotor.Braking (linear per tick), position += velocity (per tick), Z −= SpeedLimitZ (per tick) | own | Depends on the per-tick values of the locomotor; linear braking per step gives a slightly different flare trajectory at 60 Hz; vx/vy stored per tick. | 23 |
| 387 | CountermeasuresBehavior.LaunchVolley (speed, 1 and −10) | B/literal | Threshold "speed < 1" and fallback value −10, interpreted here as units per second and converted to per-tick at run time via LogicFramesPerSecond | CountermeasuresBehavior.cpp CountermeasuresBehavior::launchVolley (velocity < 1.0f → velocity = -10.0f; value from physics->getVelocityMagnitude() without conversion) | Follows the constant; if it stays 30, threshold and backward throw shift. The reference does not convert – check the unit of the threshold. | 20 |
| 524-533 | ParachuteContain.Update (free fall) | E | Euler per tick: _fallSpeed += GravityPerTickSquared, Z += _fallSpeed; opening as soon as \|startZ − Z\| >= ParachuteOpenDist | own | Half steps: opening height and time deviate by up to one step; fallSpeed stored per tick. | 23 |
| 534-549 | ParachuteContain.Update (descent and steering) | E | Per tick: Z += −SpeedLimitZ, X/Y += Locomotor.Speed towards the target; arrival radius "one tick distance" (distance <= locomotor.Speed) | own | Arrival radius halves with the per-tick speed; landing time shifts by fractions of a tick. | 23 |
| 850-870 | DeliverPayloadAIUpdate.Strafe | C | During the dive a shot at the moving target point is requested in every update (FireCurrentWeaponAtPosition); whether the weapon itself keeps a cadence lies outside the file | origin not verified | Twice as many firing attempts per second; result changes if the weapon does not enforce a time cadence of its own (not audited). | 20,23 |
| 875-881 | DeliverPayloadAIUpdate.RunDelivering (_dropDelayLeft) | B/daten | Door/drop delay as a counter that drops by 1 per update (DoorDelayTicks, then DropDelayTicks); drop in the update after expiry, i.e. Delay + 1 updates | DeliverPayloadAIUpdate.cpp (m_dropDelayLeft = getDoorDelay(); --m_dropDelayLeft; m_dropDelayLeft = getDropDelay()) | Duration scales with converted ticks, the additional tick per drop becomes 1/60 s: drop spacings minimally shorter, drop locations shift. | 20,23 |
| 965-969 | DeliverPayloadAIUpdate.TurnTicks | B/daten | Waiting time outside the map computed at run time: Ceil(MinTurnRadius / Locomotor.Speed) ticks, at least 1; MinTurnRadius = Speed / TurnRatePerTick | DeliverPayloadAIUpdate.cpp DeliverPayloadAIUpdate::calcMinTurnRadius (timeToTravelThatDist) | Invariant provided Speed and TurnRatePerTick are converted to the rate; Ceil and minimum 1 tick round more finely. | 20 |
| 1071 | ParticleUplinkData.Parse (ManualDrivingSpeed) | B/daten | Beam speed per second is divided by LogicFramesPerSecond by hand when parsing (not via SimulationTiming.PerSecondToPerTick) | ParticleUplinkCannonUpdate.cpp ParticleUplinkCannonUpdate::update (speed /= LOGICFRAMES_PER_SECOND) | Follows the constant; own division is not covered by a conversion of the SimulationTiming functions. | 20 |
| 1072 | ParticleUplinkData.Parse (ManualFastDrivingSpeed) | B/daten | Fast beam speed per second is divided by LogicFramesPerSecond by hand when parsing | ParticleUplinkCannonUpdate.cpp ParticleUplinkCannonUpdate::update (speed /= LOGICFRAMES_PER_SECOND) | As ManualDrivingSpeed: own division, must be converted along with it. | 20 |
| 1168 | ParticleUplinkCannonUpdate.Initiate (world.Tick − _pendingPathTick <= 3) | B/literal | Offer of a script waypoint path expires after 3 ticks if no use follows | own | Window becomes 0.05 s instead of 0.1 s; if the script command arrives later measured in ticks, the path expires and the beam sweeps instead of driving. | 20 |
| 1235 | ParticleUplinkCannonUpdate.Update (_nextScorch) | B/daten | Next scorch mark = birth + (death − birth) · n / TotalScorchMarks, integer division to ticks (rounded down) | origin not verified | Invariant in time with converted durations; finer rounding down shifts individual FX times by fractions. | 20 |
| 1241 | ParticleUplinkCannonUpdate.Update (_nextPulse) | B/daten | Next damage pulse = birth + (death − birth) · n / TotalDamagePulses, integer division to ticks (rounded down) | ParticleUplinkCannonUpdate.cpp ParticleUplinkCannonUpdate::update (m_nextDamagePulseFrame = orbitalBirthFrame + nextFactor * (orbitalDeathFrame - orbitalBirthFrame)) | Pulse times shift through finer rounding down; pulse count stays, with a driven beam the beam is at a slightly different place. | 20,23 |
| 1247 | ParticleUplinkCannonUpdate.Update (Math.Max(LaunchFxTicks, 1)) | C | Launch FX every DelayBetweenLaunchFX (ms, Ceil), at least every tick; with 0 one FX event per tick | origin not verified | With a data value invariant; with 0 twice as many FX events per second (result only in the presentation). | 20,21 |
| 1298-1303 | ParticleUplinkCannonUpdate.MoveBeam (manual branch) | E | Per tick: beam drives by min(speed per tick, remainder) towards the target; waypoint change when remainder <= one tick distance | ParticleUplinkCannonUpdate.cpp ParticleUplinkCannonUpdate::update (speed /= LOGICFRAMES_PER_SECOND) | Waypoint change radius halves; per waypoint one tick instead of 1/30 s is lost, trajectory minimally different. | 23 |
| 1311 | ParticleUplinkCannonUpdate.Pulse (seconds = TotalFiringTicks / LogicFramesPerSecond) | B/daten | Back-conversion of the firing duration, already rounded up to ticks, into seconds, at run time per pulse; damage per pulse = seconds · DamagePerSecond / pulses | ParticleUplinkCannonUpdate.cpp ParticleUplinkCannonUpdate::update (totalFiringSeconds = m_totalFiringFrames / LOGICFRAMES_PER_SECOND) | Follows the constant; Ceil to finer ticks changes the damage per pulse slightly if the duration is not a multiple of 1/30 s. | 20 |

**`Mod\HackerAndAssaultModules.cs`** (plus 4 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 97-101 | HackInternetAIUpdate.Varied | B/daten | Run-time arithmetic on ticks already converted (ceil): UnpackTicks/PackTicks × random factor [1−v, 1+v], then Floor; one logic random draw per call (calls ll. 92, 117) | HackInternetAIUpdate.cpp, UnpackingState::onEnter / PackingState::onEnter (m_framesRemaining = getUnpackTime() * variation) | Double rounding (ceil when parsing, floor after the random factor) on a different tick granularity: duration in seconds deviates slightly, result is not exactly 2× the 30 Hz ticks. | 20,23 |
| 139-161 | HackInternetAIUpdate.Update (_remaining) | E | Countdown per update call following the pattern ">0 decrement, otherwise transition/payout": every phase (Unpack, cash interval, Pack) lasts N+1 ticks; runs only because l. 162 returns SleepNone | HackInternetAIUpdate.cpp, UnpackingState::update / HackInternetState::update / PackingState::update (if m_framesRemaining > 0: m_framesRemaining--) | With doubled N a cycle lasts 2N+1 instead of 2N+2 ticks: payout interval somewhat shorter, hacker income per second rises slightly. | 20,23 |
| 263-298 | AssaultTransportAIUpdate.Update | C | Never sleeps (SleepNone): member list, deploy detection (LastFireTick != _lastShot) and member commands run every tick; no tick literals | AssaultTransportAIUpdate.cpp, update (return UPDATE_SLEEP_NONE) | Load only and reaction latency (one tick = half the time); results at most through the reaction that is earlier by fractions. | 20 |

**`Mod\JetAIUpdate.Flight.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 42-65 | JetAIUpdate.CircleStep | E | Circling flight: every tick a new target point (π−π/8 ahead around the circle) and SteerWinged with MinSpeed per tick; the documented measured value "path ≈ 0.93·radius" is a result of the 30 Hz step size | Locomotor.cpp, Locomotor::maintainCurrentPositionWings (aimDir = PI − PI/8) | Smaller steps change the settled circular path (radius, position, hash); the measured 0.93 no longer apply. | 23 |

**`Mod\JetAIUpdate.cs`** (plus 3 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 251-283 | JetAIUpdate.TaxiAlongPath | E | Taxiing by Euler per tick: turning by TurnRatePerTick, step = Speed per tick, arrival radius "one tick of travel" (at least 1 unit, l. 261) with snapping to the point, turning in place up to π/6 | own (kinematics instead of physics, class comment) | Arrival radius halves down to the lower bound 1; snapping and turning in place happen at other positions/ticks, taxi time deviates. | 23 |
| 257 | JetAIUpdate.TaxiAlongPath (taxi?.Speed ?? Fixed.One) | D | Fallback speed 1 unit per tick if no locomotor is active | own | Unscaled, double taxi speed in seconds (60 instead of 30 units/s). | 20 |
| 286-293 | JetAIUpdate.TurnTo | E | Turning in place by TurnRatePerTick per tick, snapping when remainder ≤ step, tolerance 0.001 rad (calls ll. 541, 855) | own | Turning duration the same except for the last partial step; the completion tick (start of the takeoff pause/of the reload) shifts by fractions. | 23 |
| 441 | JetAIUpdate.Update (return SleepNone) | C | The whole jet state machine runs every tick (also parked and while reloading) | JetAIUpdate.cpp, JetAIUpdate::update (return UPDATE_SLEEP_NONE) | Relevant to the result: all integration steps of this file depend on the call per tick; in addition double load. | 20,23 |
| 447 | JetAIUpdate.UpdateAttackLocomotor (Math.Max(AttackLocomotorPersistTicks, 1)) | B/literal | Minimum duration 1 tick for the attack locomotor set; trigger is LastFireTick == now (tick equality) instead of status IS_ATTACKING | own (reference JetAIUpdate.cpp, JetAIUpdate::update: m_attackLocoExpireFrame = now + m_attackLocoPersistTime without minimum value) | Minimum duration becomes 1/60 s; visible only with AttackLocomotorPersistTime = 0. | 20 |
| 551 | JetAIUpdate.PauseBeforeTakeoff (_transferTick = now + 1) | E | Runway is handed on 1 tick after the start of the countdown (order delay in ticks) | JetAIUpdate.cpp, JetPauseBeforeTakeoffState::update (m_whenTransfer = now + 1) | Stays 1 tick = half the time; is a statement of order, not of time – scaling would change the staggering of the takeoffs. | 23 |
| 553 | JetAIUpdate.PauseBeforeTakeoff (_transferTick = now + 2) | E | Runway handover 2 ticks later if another aircraft was waited for | JetAIUpdate.cpp, JetPauseBeforeTakeoffState::update (m_whenTransfer = now + 2; "2 seems odd, but is correct") | As l. 551: tick order, half the time at 60 Hz. | 23 |
| 608-616 | JetAIUpdate.TakeOff (helicopter branch) | E | Vertical takeoff: Z += max(SpeedLimitZ, Speed/4) per tick up to apron height + ApproachHeight | own | Scales with the per-tick values; only the clamping at the target (last partial step) falls on a different point in time. | 23 |
| 617-645 | JetAIUpdate.TakeOff (takeoff roll) | E | Takeoff roll: Speed += Acceleration, then position += Speed (semi-implicit Euler), waypoint change/end at distance ≤ max(Speed, 1) (one tick of travel, at least 1 unit); height as a function of the distance | own; height formula after JetAIUpdate.cpp, JetTakeoffOrLandingState (setMaxLift(m_maxLift * ratio)) | Distance per second deviates by the Euler error, arrival radius halves: lift-off and end time shift. | 23 |
| 732 | JetAIUpdate.EnterCircling (_checkAirfieldTick = Tick + LogicFramesPerSecond) | A | First airfield search 1 s (30 ticks) after the start of circling over the dead airfield | JetAIUpdate.cpp, JetOrHeliCirclingDeadAirfieldState (HOW_OFTEN_TO_CHECK = LOGICFRAMES_PER_SECOND; onEnter) | Depends on the constant: stays 1 s only if LogicFramesPerSecond follows the rate; otherwise 0.5 s. | 20 |
| 739 | JetAIUpdate.CircleDeadAirfield (OutOfAmmoDamageRatePerSecond) | B/daten | Conversion at run time per tick: share/s × MaxHealth / LogicFramesPerSecond, damage is applied every tick (raw value from l. 28) | JetAIUpdate.cpp, JetOrHeliCirclingDeadAirfieldState::update (damageRate *= SECONDS_PER_LOGICFRAME_REAL) | Invariant only if the divisor follows the rate (otherwise double damage/s); Fixed rounding per tick changes the sum slightly. | 20 |
| 743 | JetAIUpdate.CircleDeadAirfield (_checkAirfieldTick = Tick + LogicFramesPerSecond) | A | Repeated airfield search every second (30 ticks); here absolute tick, in the reference a down counter | JetAIUpdate.cpp, JetOrHeliCirclingDeadAirfieldState::update (--m_checkAirfield <= 0; m_checkAirfield = HOW_OFTEN_TO_CHECK) | As l. 732; search cadence is relevant to the result (time of the landing at the new airfield). | 20 |
| 776-800 | JetAIUpdate.Land (helicopter branch) | E | Vertical landing: horizontally min(distance, max(MinSpeed, 1)) per tick, descent max(SpeedLimitZ, Speed/4) per tick, done at distance ≤ 1; literal 1 unit/tick as minimum speed | own | With MinSpeed < 1/tick the literal takes effect: approach twice as fast in seconds; touchdown tick shifts. | 20,23 |
| 802-829 | JetAIUpdate.Land (glide and rollout) | E | Speed per tick by max(Braking, Acceleration) towards MinSpeed, step = min(Speed, distance), waypoint reached at distance ≤ max(step, ε); height from the remaining distance | own (according to the comment loco->setMaxSpeed(getMinSpeed()), not audited) | Braking and glide distance per second and the touchdown/rollout tick shift with the step size. | 23 |
| 803 | JetAIUpdate.Land (minSpeed = Max(MinSpeed, 1/2)) | D | Lower bound of the landing speed 0.5 units per tick (15 units/s at 30 Hz) | own | Unscaled, the lower bound corresponds to 30 units/s. | 20 |
| 881 | JetAIUpdate.BeginReload (time * fehlend / ClipSize) | B/daten | Arithmetic on converted ticks at run time: ClipReload ticks × missing shots / ClipSize as integer division (truncating), maximum over the slots | JetAIUpdate.cpp, JetOrHeliReloadAmmoState::onEnter (rt = (rt * needed) / clipSize) | Truncation on a different tick granularity: reload duration in seconds deviates by up to one tick. | 20 |
| 883 | JetAIUpdate.BeginReload (Math.Max(total, 1)) | A | Minimum reload time 1 tick | JetAIUpdate.cpp, JetOrHeliReloadAmmoState::onEnter (if (m_reloadTime < 1) m_reloadTime = 1) | 1 tick = 1/60 s; effective only for weapons without reload time or with a full clip. | 20 |
| 903-924 | JetAIUpdate.ControlHeight (_verticalSpeed) | E | Height control by Euler: _verticalSpeed += (lift − gravity) per tick, cap Speed/2; fallback acceleration Speed/8 (speed per tick used as acceleration); descent max(SpeedLimitZ, Speed/4) per tick | own (kinematic substitute for the physics) | Climb profile changes; the fallback values mix ÷rate with ÷rate² quantities and scale wrongly (Speed/8 would be double the acceleration at 60 Hz). | 20,23 |

**`Mod\LastBlockerModules.cs`** (plus 2 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 200 | SabotageInternetCenterCrateCollide.Sabotage (return data.DurationTicks) | F/tick | Sabotage duration in ticks as event value (SimEventKind.Sabotaged and Collected via SabotageCollideBase, CrateModules.cs ll. 484-486) | own | Consumers of the event get sim ticks; whoever interprets them as 1/30 s shows double the duration. | 21 |
| 209 | SmartBombHomingData.Parse (CourseCorrectionScalar) | D | Blend factor per tick as raw value (ModuleConfigReader.Real), default 0.99; 1 = no course correction | SmartBombTargetHomingUpdate.h, SmartBombTargetHomingUpdateModuleData (m_courseCorrectionScalar = 0.99f) | A factor "per frame": unchanged it acts twice as often per second (would have to become s^(30/rate)). | 20 |
| 246-255 | SmartBombTargetHomingUpdate.Update | E | Multiplicative approach per tick: pos = target·(1−s) + pos·s for x and y, as long as clearly above the ground | SmartBombTargetHomingUpdate.cpp, SmartBombTargetHomingUpdate::update (statusCoeff/targetCoeff) | Remaining distance per second 0.99^60 instead of 0.99^30: the MOAB drifts onto the target considerably faster, point of impact changes. | 20,23 |
| 323-329 | SupplyWarehouseCripplingBehavior.Update | E | Heals SelfHealAmount per wake-up and sleeps SelfHealDelay ticks, at least SleepNone: with SelfHealDelay = 0 (fallback l. 284) healing every tick | SupplyWarehouseCripplingBehavior.cpp, update (m_nextHealingFrame = now + m_selfHealDelay; UPDATE_SLEEP(m_nextHealingFrame - now)) | With Delay > 0 only ceil rounding of the interval; with Delay 0 the healing per second doubles. | 20 |

**`Mod\MatchModules.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 45 | BaseRegenerateUpdate.HealRate | A | Healing burst every 3 ticks with three times the tick amount; 2 uses (l. 69 factor, l. 71 sleep duration) | BaseRenerateUpdate.cpp, BaseRegenerateUpdate::update (const Int HEAL_RATE = 3; return UPDATE_SLEEP(HEAL_RATE)) | Unscaled, bursts every 0.05 s instead of 0.1 s; amount per second is right only if the divisor in l. 69 follows the rate. | 20 |
| 69 | BaseRegenerateUpdate.Update (amount) | B/daten | Conversion at run time per healing burst: MaxHealth × BaseRegenHealthPercentPerSecond × HealRate / LogicFramesPerSecond | BaseRenerateUpdate.cpp, BaseRegenerateUpdate::update (amount = HEAL_RATE * maxHealth * m_baseRegenHealthPercentPerSecond / LOGICFRAMES_PER_SECOND) | Depends on the constant and on HealRate; Fixed rounding per burst and the clamping at full health change the course slightly. | 20 |

**`Mod\MineModules.cs`** (plus 1 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 88-94 | DemoTrapUpdate.Update (_scanCountdown) | C | Scan cadence as a down counter per update (awake every tick): scan, then ScanTicks ticks of pause, i.e. period ScanTicks+1 | DemoTrapUpdate.cpp, DemoTrapUpdate::update (m_nextScanFrames-- / m_nextScanFrames = m_scanFrames) | Relevant to the result: period 2N+1 instead of 2N+2 ticks, the trigger time of the trap shifts. | 20,23 |
| 174 | MinefieldData.Parse (CreatorDeathCheckRate) | B/fallback | Fallback in ticks: SimulationTiming.LogicFramesPerSecond (30 = 1 s) for ModuleConfigReader.Ticks | MinefieldBehavior.cpp, MinefieldBehaviorModuleData (m_creatorDeathCheckRate = LOGICFRAMES_PER_SECOND) | Stays 1 s only if the constant follows the rate; as a literal 30 it would be 0.5 s. No rounding (integer). | 20 |
| 243 | MinefieldBehavior.Update (DegenPerSecond) | B/daten | Conversion at run time per tick: MaxHealth × DegenPercentPerSecondAfterCreatorDies / LogicFramesPerSecond, damage every tick during the decay (raw value l. 175) | MinefieldBehavior.cpp, update (maxHealth * m_healthPercentToDrainPerSecond / LOGICFRAMES_PER_SECOND) | Invariant only with the rate as divisor; per tick the damage triggers Reconcile – the virtual mines fall in finer steps. | 20,23 |

**`Mod\ModuleConfigReader.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 40-45 | ModuleConfigReader.Ticks | B/daten | Central conversion milliseconds → whole ticks (ceil, SimulationTiming.DurationMillisecondsToTicks) at parse time; the fallback is returned unconverted as a tick count | own (reproduces INI::parseDurationUnsignedInt of the module field tables; its body not audited) | Every fallback ≠ 0 stays a 30 Hz tick count if only the conversion is parameterised; ceil rounds more finely at 60 Hz (durations become up to 1/60 s shorter). | 20 |

**`Mod\Phase12Modules.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 55 | TechBuildingData.Parse (PulseFXRate → PulseRateTicks) | F/tick | FX pulse rate of a presentation effect is held as sim ticks; no consumer of PulseRateTicks found in src | origin not verified | A later consumer would get sim ticks instead of wall-clock time. | 21 |
| 154 | FireWeaponWhenDamagedBehavior.Fire (Math.Max(DelayBetweenShotsTicks, SleepNone)) | E | Delay between shots of the reaction/continuous weapon at least 1 tick; the continuous weapon is checked every tick (Update ll. 141-146, SleepNone) | own (replaces the weapon status of the reference; FireWeaponWhenDamagedBehavior.cpp, update: UPDATE_SLEEP_NONE) | With DelayBetweenShots = 0 the continuous weapon fires twice as often per second; otherwise only ceil rounding. | 20,23 |
| 229 | FireWeaponPower.Update (Math.Max(DelayBetweenShotsTicks, SleepNone)) | E | Sleep between the shots of the special power at least 1 tick | own | With DelayBetweenShots = 0 the MaxShots fall in half the time. | 20 |
| 299 | HordeData.Parse (UpdateRate) | B/fallback | Fallback in ticks: SimulationTiming.LogicFramesPerSecond (30 = 1 s) | HordeUpdate.cpp (DEFAULT_UPDATE_RATE = LOGICFRAMES_PER_SECOND) | Stays 1 s only if the constant follows the rate. | 20 |
| 336 | HordeUpdate.OnSpawn (LogicRandom.Range(1, UpdateRateTicks)) | C | Random staggering of the first wake-up over 1..UpdateRate ticks; one logic random draw per object | HordeUpdate.cpp, HordeUpdate constructor (UPDATE_SLEEP(GameLogicRandomValue(1, delay))) | Relevant to the result: the draw range depends on the tick count, the drawn value and with it phase and hash change. | 20,23 |
| 343-349 | HordeUpdate.Update (_lastRefresh) | C | Re-evaluation: infantry sleeps UpdateRateTicks; vehicles run every tick and evaluate at Tick > _lastRefresh + UpdateRate (strict, i.e. period N+1) | HordeUpdate.cpp, HordeUpdate::update (getFrame() > m_lastHordeRefreshFrame + m_updateRate; UPDATE_SLEEP(m_updateRate)) | Relevant to the result (time of the HORDE bonus): vehicle period 2N+1 instead of 2N+2 ticks, infantry exact; double load for vehicles. | 20,23 |

**`Mod\Phase5BlockerModules.cs`** (plus 10 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 292 | SlowDeathBehavior.BeginSlowDeath (Range(0, SinkDelayVarianceTicks)) | B/daten | Random draw over a tick range at run time; value unused, drawn only to keep the RNG in step | SlowDeathBehavior.cpp (m_sinkFrame = timeScale * (m_sinkDelay + GameLogicRandomValue(0, m_sinkDelayVariance))) | Draw range doubles with scaled ticks; the number of draws stays, the RNG state afterwards can differ depending on the generator. | 20,23 |
| 293 | SlowDeathBehavior.BeginSlowDeath (Range(0, DestructionDelayVarianceTicks)) | B/daten | Destruction delay = DestructionDelay ticks + integer random value in 0..variance ticks (run time, tick granularity) | SlowDeathBehavior.cpp (m_destructionFrame = timeScale * (m_destructionDelay + GameLogicRandomValue(0, m_destructionDelayVariance))) | Different draw range: the drawn value is not twice the 30 Hz value, destruction time and hash change. | 20,23 |
| 426 | StealthDetectorData.Parse (DetectionRate) | B/fallback | Fallback 1 tick for ModuleConfigReader.Ticks ("scan every frame", a frame count, not a duration) | StealthDetectorUpdate.h, StealthDetectorUpdateModuleData (m_updateRate = 1) | Unscaled, a scan every tick = 60 times per second (double load, O(n) per detector); scaled to 2 it would still be 1/30 s. | 20 |
| 457 | StealthDetectorUpdate.OnSpawn (LogicRandom.Range(1, UpdateRateTicks)) | C | Random staggering of the first scan over 1..DetectionRate ticks; one logic random draw per detector | StealthDetectorUpdate.cpp, constructor (UPDATE_SLEEP(GameLogicRandomValue(1, m_updateRate))) | Affects results: draw range depends on the tick count, different value → different scan phase and different hash. | 20,23 |
| 486 | StealthDetectorUpdate.Update (MarkDetected(UpdateRateTicks + 1)) | E | Detection lasts DetectionRate + 1 ticks: one tick of overlap until the next scan (tick order) | StealthDetectorUpdate.cpp, StealthDetectorUpdate::update (stealth->markAsDetected(m_updateRate + 1)) | The overlap stays 1 tick (half the time); it only secures the order scan → expiry and must not need to be scaled along. | 23 |
| 555 | PoisonedBehavior.Update (Math.Max(PoisonDamageIntervalTicks, SleepNone)) | E | Poison damage per interval, interval at least 1 tick (likewise l. 535): with PoisonDamageInterval = 0 (fallback l. 501) damage every tick | own (minimum value; PoisonedBehavior.cpp not checked for a minimum value) | With interval > 0 only ceil rounding (damage per second changes through the rounding); with 0 double damage per second. | 20 |

**`Mod\Phase5Modules.cs`** (plus 8 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 71 | StealthData.Parse "MoveThresholdSpeed" (StealthData.MoveThresholdSpeed) | D | Raw value via ModuleConfigReader.Real, compared directly with Entity.Speed (per tick) in StealthUpdate.ConditionActive ("MOVING", l. 185); no conversion | StealthUpdate.cpp, field table: "MoveThresholdSpeed" is INI::parseVelocityReal (the original converts per second into per frame; not here) | Threshold stays the same, Entity.Speed per tick halves: units still count as standing at double the speed; already at 30 Hz the unit of measure deviates from the original | 20 |
| 187-188 | StealthUpdate.ConditionActive (FIRING_*/TAKING_DAMAGE: LastFireTick == world.Tick) | E | Event window of exactly one tick: condition holds only in the tick of the shot/damage; requires that Update runs every tick (SleepNone) and gets its turn after the event | own | Window lasts 1/60 s instead of 1/30 s; result the same only as long as the module keeps running every tick and the order shot before Update stays | 23 |
| 283 | AutoHealBehavior.Update (return Math.Max(data.HealingDelayTicks, SleepNone)) | E | Healing pulse with fixed HealingAmount per wake-up; lower bound 1 tick, fallback of HealingDelay is 0 (l. 228) – then healing every tick | AutoHealBehavior.h: m_healingDelay = UINT_MAX (the original default is "never", not 0); "HealingDelay" parseDurationUnsignedInt | With a missing or very small HealingDelay the healing per second doubles; otherwise only rounding up of the period | 20,23 |
| 528 | AIUpdateData.DefaultMoodCheckTicks | A | Constant 2 * LogicFramesPerSecond (= 60 ticks), fallback of ModuleConfigReader.Ticks("MoodAttackCheckRate") in l. 533 (there Math.Max(1, …)) and record default value l. 525 | AIUpdate.cpp, AIUpdateModuleData::AIUpdateModuleData: m_moodAttackCheckRate = LOGICFRAMES_PER_SECOND * 2 | Scales along through the constant, provided LogicFramesPerSecond becomes a run-time quantity; as const long it is frozen at compile time | 20 |
| 880 | AIUpdateInterface.UpdateTask (RepairRatePerSecond / LogicFramesPerSecond) | B/daten | Repair fraction per second is divided by 30 at run time in every tick (Fixed division, no rounding up); healing per tick = MaxHealth * Rate / 30 | DozerAIUpdate.cpp: "RepairHealthPercentPerSecond" parsePercentToReal, division by LOGICFRAMES_PER_SECOND (l. 720) | Converts correctly through the constant; Fixed rounding per tick changes the sum slightly | 20 |
| 1059-1078 | AIUpdateInterface.Move (arrivalRadius, reach = locomotor.Speed) | E | Arrival radius = Max(CloseEnoughDist, Speed per tick); intermediate waypoints count as reached at distance <= one tick of travel; then snapping onto the waypoint | own | Radius halves with the speed per tick: waypoints are followed more tightly, arrival place and time shift | 23 |
| 1084-1103 | AIUpdateInterface.Move (turning, braking distance, acceleration) | E | Euler per tick: angle += TurnRatePerTick, Speed +/- Acceleration/Braking per tick, braking distance v²/(2a) in tick units, turning circle Speed/turnRate*2, threshold π/6 | own (comment refers to calcSlowDownDist, not looked up) | Discrete steps half as large: start of braking, curve radius and snapping onto the target angle (difference <= turnRate) turn out differently | 23 |
| 1106 | AIUpdateInterface.Move (world.Tick - _blockerReplanTick >= 30) | C | Replanning around stationary blockers at most once per 30 ticks (literal); start value/sentinel _blockerReplanTick = -1000 (l. 1182, also in the snapshot) | own (docs/DEMO-FINDINGS.md #12) | Without scaling, replanning twice as often (every 0.5 s): different paths, hence different results, not only load | 20 |
| 1109 | AIUpdateInterface.Move (look-ahead Entity.Speed * 25) | B/literal | Look-ahead for stationary blockers = 25 ticks of travel (literal 25 times speed per tick, twice in the expression) | own | Look-ahead distance halves (25 ticks = 0.42 s): blockers are detected later | 20 |
| 1115-1124 | AIUpdateInterface.Move (position step, AvoidLocalBlockers, ResolveFootprintCollisions) | E | Position += direction * Speed per tick; local avoidance movement and collision/crush check once per tick with the tick step | own | Half the step size: avoidance and collision hits are checked twice as often and with smaller steps, trajectories deviate | 23 |
| 1133-1150 | AIUpdateInterface.MoveWinged | E | Arrival at distance <= Max(CloseEnoughDist, Speed per tick) or target behind the aircraft within 2 * turning circle (Speed/TurnRatePerTick); braking distance (v² - vmin²)/(2a) | Comment names Locomotor::moveTowardsPositionWings; not looked up – origin not verified | Arrival radius "one tick of flight" halves; arrival tick and with it the start of circling shift | 23 |
| 1163-1180 | AIUpdateInterface.SteerWinged | E | One tick of winged flight: turn by at most TurnRatePerTick, Speed +/- Acceleration/Braking, Position += direction * Speed | Origin not verified | Euler step half as large: curve trajectory and the time at which the target speed is reached change slightly | 23 |
| 1223-1253 | AIUpdateInterface.MoveStraight | E | Straight travel without terrain: closeEnough = Max(CloseEnoughDist, Speed per tick), braking distance v²/(2a), Speed +/- per tick, step = Min(Speed, dist), collision check per tick | own | Arrival radius and start of braking depend on the tick step size; arrival tick shifts | 23 |
| 1464-1489 | ProductionUpdate.Update (RemainingTicks - 1; door closed: RemainingTicks = 1) | C | Build time is counted down by 1 per tick (total comes from the caller via Enqueue, at least 1); finished unit polls the door every tick | ProductionUpdate::update according to the comment; not looked up – origin not verified | Correct if the tick count passed in is scaled; door polling then on a 1/60 s grid (result: exit tick) | 20 |

**`Mod\PhysicsAndMinefieldModules.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 58-61 | PhysicsBehavior.Update (free fall) | E | Euler per tick: velocityZ += GravityPerTickSquared, Position += velocity per tick (X/Y passed in per tick by the caller StartFall); landing as soon as Z <= ground | own (replacement for PhysicsUpdate.cpp) | First velocity, then position: fall time and impact location deviate at half the step size; impact is rounded to the tick | 23 |
| 168-180 | GenerateMinefieldBehavior.Update (return SleepNone) | C | Polls every tick whether the upgrade of the mines is there (as long as Upgradable and not yet upgraded) | GenerateMinefieldBehavior.cpp update() according to the comment; not looked up – origin not verified | Load only and reaction grid (1/60 s instead of 1/30 s); random draws only on triggering, not per poll | 20 |

**`Mod\ProjectileModules.cs`** (plus 4 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 44 | DumbProjectileData.DefaultMaxLifespanTicks | A | Constant 10 * LogicFramesPerSecond (= 300 ticks), fallback of ModuleConfigReader.Ticks("MaxLifespan") in l. 47 | DumbProjectileBehavior.cpp: const Int DEFAULT_MAX_LIFESPAN = 10 * LOGICFRAMES_PER_SECOND | Scales along through the constant; as const long frozen at compile time | 20 |
| 54 | DumbProjectileData.Parse "FlightPathAdjustDistPerSecond" (FlightPathAdjustPerTick) | B/daten | Distance per second is divided by LogicFramesPerSecond by hand during module parsing (not via SimulationTiming.PerSecondToPerTick), Fixed division without rounding up | DumbProjectileBehavior.cpp: "FlightPathAdjustDistPerSecond" INI::parseVelocityReal (m_flightPathAdjustDistPerFrame) | Converts correctly through the constant; a division site of its own that does not go through the conversion function when parametrised | 20 |
| 148 | DumbProjectileBehavior.CalcFlightPath (_segments = ceil(length / _speed)) | E | Number of path points = rounded-up path length divided by weapon speed per tick; one point per tick, so flight duration in whole ticks; in the snapshot as "segments" | DumbProjectileBehavior.cpp calcFlightPath according to the comment; not looked up – origin not verified | Flight duration is rounded up to 1/60 s instead of 1/30 s: impact up to half an old tick earlier | 20,23 |
| 229-256 | DumbProjectileBehavior.Update | E | One path point per Update (_step++); detonation only in the Update after the last point ("one tick later"); target tracking by at most FlightPathAdjustPerTick per tick with recalculation of the curve | Origin not verified | The extra tick until detonation is only 1/60 s; tracking in twice as many steps half as large gives a slightly different curve | 23 |
| 334 | MissileData.Parse "InitialVelocity" | B/daten | Speed per second is divided by LogicFramesPerSecond by hand during module parsing (not via SimulationTiming.PerSecondToPerTick) | MissileAIUpdate.cpp: "InitialVelocity" INI::parseVelocityReal (m_initialVel) | Converts correctly through the constant; a division site of its own | 20 |
| 341 | MissileData.Parse "KillSelfDelay" (Fallback 3) | B/fallback | Fallback of ModuleConfigReader.Ticks is 3 ticks (waiting time after the detonation until self-removal) | MissileAIUpdate.cpp, MissileAIUpdateModuleData constructor: m_killSelfDelay = 3 (frames; "KillSelfDelay" parseDurationUnsignedInt) | Without scaling 50 ms instead of 100 ms of missile afterlife (contrail, later removal) | 20 |
| 428 | MissileAIUpdate.Launch (_acceleration = weapon.WeaponSpeed) | E | With UseWeaponSpeed the weapon speed (distance per tick) is used as the acceleration (per tick²): full speed after one tick | Comment: m_maxAccel = weaponSpeed (MissileAIUpdate.cpp); not looked up – origin not verified | Stays "full speed after one tick", i.e. after 1/60 s; a mix of units of measure that must not be scaled with the factor for accelerations | 23 |
| 469-473 | MissileAIUpdate.Update (_noTurnDistanceLeft -= Distance(position, _previous)) | E | Distance before turning in is measured from the movement of the previous tick (before the own step), so it lags by one tick of flight | Origin not verified (comment: "as the reference does before moving") | Lag only half an old tick of flight: switch into the steering state somewhat earlier on the trajectory | 23 |
| 610-648 | MissileAIUpdate.Move | E | Euler per tick: _speed += _acceleration (up to _maxSpeed), arrival at remaining distance <= _speed (one tick of flight) with snapping onto the target, without fuel _verticalSpeed += GravityPerTickSquared, ground contact per tick | own (kinematic replacement for the THRUST locomotor) | Arrival window halves, detonation tick and location shift; glide path without fuel changes with the step size | 23 |
| 788-794 | PointDefenseLaserUpdate.Update (_nextScan counter) | C | Scan counter: is set to ScanTicks and counted down per tick, the scan happens in the tick after 0 – period ScanTicks + 1; fallback of "ScanRate" is 0 = every tick; range check/shot every tick | PointDefenseLaserUpdate.cpp: m_scanFrames = 0, m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames ("ScanRate" parseDurationUnsignedInt) | Changes results: period N+1 does not scale linearly (2N+1 instead of 2N+2 ticks), target selection falls on other ticks | 20,23 |
| 808 | PointDefenseLaserUpdate.FireWhenReady (_nextScan = LogicRandom.Range(0, 3), target leaves range) | A | Random waiting time 0..3 ticks until the next scan (literals 0 and 3, one random draw per event) | PointDefenseLaserUpdate.cpp: m_nextScanFrames = GameLogicRandomValue( 0, 3 ) | Without scaling 0..50 ms instead of 0..100 ms; with scaling (0..6) the value range of the random draw changes and with it the random stream | 20 |
| 819-830 | PointDefenseLaserUpdate.FireWhenReady (_nextShot counter) | E | Shot counter: set to weapon.DelayBetweenShotsTicks after the shot, -1 per tick, shot in the tick after 0 – delay between shots Delay + 1 ticks | PointDefenseLaserUpdate.cpp: m_nextShotAvailableInFrames-- / = wt->getDelayBetweenShots( bonus ) | Delay between shots 2N+1 instead of 2N+2 ticks: fire rate of the laser rises slightly (noticeable with a small Delay) | 20,23 |
| 833 | PointDefenseLaserUpdate.FireWhenReady (_nextScan = LogicRandom.Range(0, 3), target dead) | A | Random waiting time 0..3 ticks until the next scan after a kill | PointDefenseLaserUpdate.cpp: m_nextScanFrames = GameLogicRandomValue( 0, 3 ) | As l. 808: waiting time halves, or the value range of the random draw changes | 20 |

**`Mod\RailroadModules.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 33 | RailroadData.Parse "KillSpeedMin" (default 1) | D | Kill threshold in distance per frame, raw value, default Fixed.One; comparison with _pull.Speed in Collide (l. 561) | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_killSpeedMin = 1.0f | Without conversion the train kills only from double the speed | 20 |
| 33 | RailroadData.Parse "RunningGarrisonSpeedMax" (default 1) | D | Speed threshold in distance per frame, raw value without conversion, default Fixed.One; comparison with _pull.Speed in Collide (l. 559) | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_runningGarrisonSpeedMax = 1.0f | Threshold refers to distance per tick; without conversion it applies to double the speed | 20 |
| 34 | RailroadData.Parse "Acceleration" (default 1.01) | D | Multiplicative acceleration factor per frame, raw value, default 1.01 | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_acceleration = 1.01f | Factor acts twice as often: train accelerates in half the time; linear scaling is wrong (root needed) | 20,23 |
| 34 | RailroadData.Parse "SpeedMax" (default 4) | D | Maximum speed of the train in distance per frame, raw value, default 4 | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_speedMax = 4 | Train travels twice as fast (4 units per 1/60 s) | 20 |
| 35 | RailroadData.Parse "Braking" (default 0.99) | D | Multiplicative braking factor per frame, raw value, default 0.99 | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_braking = 0.99f | Train brakes in half the time and over a shorter distance; stop at the station in a different place | 20,23 |
| 35 | RailroadData.Parse "Friction" (default 0.97) | D | Multiplicative friction per frame for coasting carriages, raw value, default 0.97 | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_friction = 0.97f | Uncoupled carriages coast to a stop in half the time | 20,23 |
| 36 | RailroadData.Parse "WaitAtStationTime" (Fallback 150) | B/fallback | Fallback of ModuleConfigReader.Ticks is 150 ticks (5 s stop at the station) | RailroadGuideAIUpdate.cpp, RailroadBehaviorModuleData constructor: m_waitAtStationTime = 150; header: parseDurationUnsignedInt | Without scaling the train stops for only 2.5 s if the field is missing in the data | 20 |
| 59 | RailroadBehavior.FramesUnpulledLongEnoughToUnhitch | A | Constant 2: ticks without pull from the carriage in front after which a carriage declares itself the lead carriage (2 uses, ll. 197 and 217; counter _wantsToBeLead in the snapshot) | RailroadGuideAIUpdate.cpp: #define FRAMES_UNPULLED_LONG_ENOUGH_TO_UNHITCH (2) | Unhitching after 3 ticks = 50 ms instead of 100 ms; actually an order tolerance in ticks, not necessarily to be scaled | 20 |
| 60 | RailroadBehavior.PushStart | D | Constant 0.02: push that is added to the speed per tick before the acceleration factor acts (use l. 251) | RailroadGuideAIUpdate.cpp: conductorPullInfo.speed += 0.02f * conductorPullInfo.m_direction (push start multiplier) | Is added twice as often: faster start-up; is an acceleration per tick² in tick units | 20,23 |
| 188-190 | RailroadBehavior.Update (carriages exist only in the next tick) | E | After CreateCarriages one tick is waited, because carriages created via RequestSpawn exist only from the next tick on | own | One-tick delay is only 1/60 s; order semantics in ticks, not a time | 23 |
| 197-217 | RailroadBehavior.Update (friction, TrackDistance += Speed, _wantsToBeLead++) | E | Per tick: when coasting Speed *= Friction, track position += Speed (distance per tick), counter of the ticks without pull | Origin not verified (RailroadBehavior::update according to the comment; only constants looked up) | Multiplicative damping and distance step twice as often: coasting distance and train position change | 23 |
| 219 | RailroadBehavior.Update (Collide(world) every tick) | C | Collision check of the carriage against all objects once per tick after the movement; pushing away by a quarter of the overlap per tick (l. 553) | own (replacement for onCollide of the physics) | Changes results: twice as many checks and shoves per second, damage speed*10 occurs twice as often (see l. 562) | 20,23 |
| 229 | RailroadBehavior.RunConductor (ApplyBrakes: Speed *= data.Braking) | E | Multiplicative braking per tick until below the threshold, then stop and start of the station counter (_waitAtStationTimer = WaitAtStationTicks, -1 per tick) | Origin not verified (sequence not looked up, only constants) | Braking distance and stopping point change (factor acts twice as often) | 23 |
| 230 | RailroadBehavior.RunConductor (Speed.Abs() < 0.1) | D | Literal 0.1: speed threshold in distance per tick below which the train counts as standing | RailroadGuideAIUpdate.cpp: if (fabs(conductorPullInfo.speed) < 0.1f) | Threshold corresponds to double the real speed: train "stands" earlier | 20 |
| 247 | RailroadBehavior.RunConductor (Speed = 0.05 * Direction) | D | Literal 0.05: start-up speed in distance per tick after the station stop | RailroadGuideAIUpdate.cpp: conductorPullInfo.speed = 0.05f * conductorPullInfo.m_direction | Start-up speed really twice as high | 20 |
| 251-254 | RailroadBehavior.RunConductor (Accelerate) | E | Per tick: Speed += PushStart * direction, Speed *= Acceleration, limiting to +/- SpeedMax | RailroadGuideAIUpdate.cpp: speed += 0.02f * m_direction (rest of the block not looked up) | Mixed additive/multiplicative per tick: start-up curve not reproducible by simply scaling the factors | 23 |
| 562 | RailroadBehavior.Collide (damage _pull.Speed * 10) | D | Crush damage = speed per tick times 10 (literal), per collision check, i.e. per tick of the overlap | RailroadGuideAIUpdate.cpp: damageInfo.in.m_amount = m_pullInfo.speed * 10 | With scaled speed half the damage per hit, but twice as many hits; without scaling double damage per second | 20,23 |

**`Mod\RepairModules.cs`** (plus 1 plain conversion call, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 34 | RepairDockData.Parse (frames = Fixed.One without "TimeForFullHeal") | B/fallback | Fallback of the healing duration is 1 frame (Fixed.One), written in ticks | RepairDockUpdate.cpp, constructor: m_framesForFullHeal = 1.0f (1 frame, instant heal by default) | Stays "instant" (1 tick); halved as a time, practically without effect | 20 |
| 35 | RepairDockData.Parse "TimeForFullHeal" (DurationMillisecondsToFixedTicks) | B/daten | Conversion of ms into fractional ticks without rounding up (different rounding from ModuleConfigReader.Ticks), during module parsing | RepairDockUpdate.cpp: "TimeForFullHeal" INI::parseDurationReal (m_framesForFullHeal) | Converts through the conversion function; no rounding step, only Fixed precision | 20 |
| 57-69 | RepairDockUpdate.Action (_healthToAddPerTick) | E | Healing amount per tick is fixed on the first Action call as (Max - Health) / FramesForFullHeal and applied per call; presupposes exactly one Action call per tick; value is in the snapshot ("healthPerTick") | RepairDockUpdate.cpp: m_framesForFullHeal (RepairDockUpdate::action according to the comment, sequence not looked up) | Correct only if FramesForFullHeal is scaled and Action keeps running exactly once per tick; old snapshots carry the 30 Hz value | 20,23 |
| 114-122 | AutoFindHealingUpdate.Update (_nextScanTicks counter) | C | Scan counter: set to ScanTicks, -1 per tick, scan in the tick after 0 – period ScanTicks + 1; fallback of "ScanRate" is 0 = scan every tick | AutoFindHealingUpdate.cpp: m_scanFrames = 0; m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames | Changes results: period N+1 does not scale linearly, healing command falls on other ticks; with fallback 0 load only | 20,23 |

**`Mod\RetailMatchModules.cs`** (plus 6 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 88-89 | HealContain.Update (MaxHealth / TimeForFullHealTicks) | B/daten | Healing per tick = MaxHealth divided by the rounded-up tick count of "TimeForFullHeal", computed per tick at run time; end via world.Tick - since >= Ticks | HealContain.cpp: m_framesForFullHeal = 0; "TimeForFullHeal" INI::parseDurationUnsignedInt | Scales with the tick count; rounding up of the duration and Fixed division per tick change the sum slightly | 20 |
| 267 | RadarUpdate.IsExtendComplete (tick > _extendDoneTick) | E | Complete only one tick after expiry (strictly greater), whereas PowerPlantUpdate.IsExtendComplete (l. 307) uses >= | Origin not verified | Extra tick is 1/60 s instead of 1/30 s; display state only, no simulation consumer in this file | 20 |
| 372 | OverchargeBehavior.Update (HealthDrainRatioPerSecond / LogicFramesPerSecond) | B/daten | Damage per tick = MaxHealth * fraction per second / 30, divided at run time in every tick (Fixed, no rounding up) | OverchargeBehavior.cpp: (body->getMaxHealth() * m_healthPercentToDrainPerSecond) / LOGICFRAMES_PER_SECOND | Converts correctly through the constant; shutdown threshold is reached on a finer grid | 20 |
| 475 | RebuildHoleBehavior.Update (HealthRegenPercentPerSecond / LogicFramesPerSecond) | B/daten | Healing per tick = MaxHealth * fraction per second / 30, divided at run time in every tick; alongside it counter _workerWait -1 per tick (l. 479) | RebuildHoleBehavior.cpp: (m_holeHealthRegenPercentPerSecond / LOGICFRAMES_PER_SECOND) * … | Converts correctly through the constant; only Fixed rounding per tick | 20 |

**`Mod\SlaveModules.cs`** (plus 4 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 166 | SlavedUpdate.UpdateRate | A | Check cadence of the priority list = LogicFramesPerSecond / 4 in integer arithmetic (7 ticks); 2 uses (l. 229, l. 430) | GameEngine/Include/GameLogic/Module/SlavedUpdate.h: SLAVED_UPDATE_RATE = LOGICFRAMES_PER_SECOND/4 | Already derived from the rate, but integer division: 30 Hz gives 7 ticks (0.233 s), 60 Hz gives 15 ticks (0.25 s); cadence and random sequence shift. | 20,23 |
| 170 | SlavedUpdate.RecentFireTicks | B/literal | Own deadline "master has fired in the last 2 s" = 2 * LogicFramesPerSecond (60 ticks), comparison world.Tick - LastFireTick in l. 313 | own | Derived from the rate as a const; stays 2 s once LogicFramesPerSecond is parametrised (const must then become a run-time value). | 20 |
| 223-230 | SlavedUpdate.Update (_framesToWait) | C | Module wakes every tick (SleepNone), counts _framesToWait down per tick; priority list only every UpdateRate ticks, in the repair state every tick | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::update (m_framesToWait = SLAVED_UPDATE_RATE) | Results: movement commands, DRONE_SPOTTING bonus and random draws depend on the cadence; unscaled twice as often per second. | 20,23 |
| 335 | SlavedUpdate.DoAttackLogic (RandomDirection) | E | Random draw Range(0,6) per check as long as the attack logic runs and AttackWanderRange != 0; plus a new movement command per check | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::doAttackLogic (GameLogicRandomValue(0, 2*PI)) | Number of draws from the logic stream per second follows the check cadence; a different cadence length shifts the random sequence of all modules. | 23 |
| 355 | SlavedUpdate.DoScoutLogic (RandomDirection) | E | Random draw Range(0,6) per check as long as the scout logic runs and ScoutWanderRange != 0 | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::doScoutLogic (GameLogicRandomValue(0, 2*PI)) | As attack logic: draws per second depend on the check cadence. | 23 |
| 379 | SlavedUpdate.DoGuardLogic (RandomDirection) | E | Random draw Range(0,6) per check in which the drone is idle and too far from the anchor point (GuardWanderRange != 0) | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::doGuardLogic (GameLogicRandomValue(0, 2*PI)) | Draw per check; on a finer grid the same situation can draw more often or at a different time. | 23 |
| 414 | SlavedUpdate.DoRepairLogic (altitude draw) | E | Random draw Range(RepairMinAltitude, RepairMaxAltitude) on every call as long as the drone is not close enough (in the repair state every tick), only to keep the stream aligned | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::doRepairLogic (GameLogicRandomValueReal l. 492) | Twice as many draws and movement commands per second during the approach; random sequence shifts. | 23 |
| 420 | SlavedUpdate.DoRepairLogic (amount) | B/daten | RepairRatePerSecond / LogicFramesPerSecond at run time per welding tick, unrounded (Fixed division); healing per tick | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::doRepairLogic (repairAmount = m_repairRatePerSecond / LOGICFRAMES_PER_SECOND) | The same per second if the constant is parametrised; healing steps half as large, Fixed rounding per step different. | 20,23 |
| 442 | SlavedUpdate.SetRepairState (Unpacking/Packing) | A | _framesToWait = 15 ticks when packing/unpacking (requested directly) | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (m_framesToWait = 15, ll. 581/585) | Unpacking takes 0.25 s instead of 0.5 s. | 20 |
| 449 | SlavedUpdate.SetRepairState (None after Unpacking) | A | _framesToWait = 15 ticks for unpacking before the first welding | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (m_framesToWait = 15, l. 595) | Unpacking takes 0.25 s instead of 0.5 s; healing starts correspondingly earlier. | 20 |
| 453 | SlavedUpdate.SetRepairState (Welding after Retracting) | A | _framesToWait = 5 ticks for retracting the arm after welding | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (m_framesToWait = 5, l. 600) | Retracting takes 0.083 s instead of 0.167 s. | 20 |
| 458 | SlavedUpdate.SetRepairState (Ready duration) | B/daten | Random duration Range(MinReadyTicks, MaxReadyTicks) at run time over bounds that were converted from ms into ticks (ceil) during module parsing | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (GameLogicRandomValue(m_minReadyFrames, m_maxReadyFrames)) | Integer value range of the draw doubles; the same draw gives a different duration in seconds, not scalable bit-identically. | 20,23 |
| 466 | SlavedUpdate.SetRepairState (Ready after Extending) | A | _framesToWait = 5 ticks for extending the arm before welding | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (m_framesToWait = 5, l. 616) | Extending takes 0.083 s instead of 0.167 s. | 20 |
| 471 | SlavedUpdate.SetRepairState (Weld duration) | B/daten | Random duration Range(MinWeldTicks, MaxWeldTicks) at run time over bounds converted into ticks (ceil); determines the healing amount per welding pass | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (GameLogicRandomValue(m_minWeldFrames, m_maxWeldFrames)) | Integer value range of the draw doubles; welding duration and with it healing amount per pass not scalable bit-identically. | 20,23 |
| 528 | MobMemberData.CatchUpCrisisBailTime | D | Raw value via ModuleConfigReader.Int without conversion (default 999999), counted in checks (every 16 ticks); consumption ll. 598-604, also as value / 3 | GameEngine/Include/GameLogic/Module/MobMemberSlavedUpdate.h: m_catchUpCrisisBailTime (parseUnsignedInt, default 999999); MobMemberSlavedUpdate.cpp: MobMemberSlavedUpdate::update | Deadline = value times check interval; with an unscaled interval the time until the direct march and until the death of the mob member halves. | 20 |
| 542 | MobMemberSlavedUpdate.CheckInterval | A | Check of the distance to the nexus every 16th tick (module wakes every tick and counts up, l. 577) | GameEngine/Source/GameLogic/Object/Update/MobMemberSlavedUpdate.cpp: MobMemberSlavedUpdate::update (if ( ++m_framesToWait < 16), l. 187) | Check every 0.27 s instead of 0.53 s; results: movement commands, crisis counter and random draws twice as often. | 20,23 |
| 556 | MobMemberSlavedUpdate.OnSpawn (_framesToWait) | A | Start value of the check counter = Range(0, 20) frames (offset of the first check, staggering of the members) | GameEngine/Source/GameLogic/Object/Update/MobMemberSlavedUpdate.cpp: constructor (m_framesToWait = GameLogicRandomValue(0,20), l. 75) | Offset halves in time; if the upper bound is scaled, the value range of the draw changes. | 20,23 |
| 611 | MobMemberSlavedUpdate.Update (locomotor draw) | E | Random draw Range(0, 10) per check as long as the member is within the radius and has a movement target (result discarded) | GameEngine/Source/GameLogic/Object/Update/MobMemberSlavedUpdate.cpp: MobMemberSlavedUpdate::update (Int seed = GameLogicRandomValue( 0, 10 ), l. 288) | Draws per second follow the check interval; unscaled twice as many, random sequence of all modules shifts. | 23 |
| 729-735,754 | SpawnPointProductionExitUpdate.Revalidate (world.Tick > tick) | E | Tick sequence: an occupier claimed in the same tick does not exist yet and counts as present until the following tick | own | Pure order semantics (spawn at the end of the tick); stays correct, must not be scaled with the rate. | 23 |

**`Mod\SpawnModules.cs`** (plus 1 plain conversion call, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 73 | SpawnBehavior.UpdateRate | A | Working cadence of the spawner = LogicFramesPerSecond / 2 (15 ticks); sleep return value l. 130 and _workTick l. 127 | GameEngine/Include/GameLogic/Module/SpawnBehavior.h: SPAWN_UPDATE_RATE = LOGICFRAMES_PER_SECOND/2 | Already derived from the rate (60 Hz gives exactly 30 ticks); const must become a run-time value. | 20 |
| 100-131,144 | SpawnBehavior.Update / CollectDead | C | Dead spawns are found only on the UpdateRate grid (polling instead of onSpawnDeath); the replacement deadline ReplaceDelayTicks begins on discovery, replacement at the next grid point after that | own | Results: replacement time depends on the grid phase (up to 0.5 s offset); with a scaled grid the same, with an unscaled one earlier and a different spawn order. | 20,23 |
| 102-108,125-129 | SpawnBehavior.Update (AggregateHealth) | C | With AggregateHealth the module wakes every tick: CollectDead and ComputeAggregateStates per tick, creation still on the UpdateRate grid via _workTick | Origin not verified | Load only: health/veterancy level are state-derived; death of the nexus follows the last slave one tick instead of 1/30 s later. | 20 |

**`Mod\SpecialAbilityStaging.cs`** (plus 3 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 134-139 | SpecialAbilityUpdate.StageTicksLeft | F/tick | Remaining ticks of the running stage (unpacking, preparation, packing) as a public property for the presentation | own | Tick value leaves the simulation; a consumer that calculates with 30 shows progress/animation at half speed. | 21 |
| 311-316 | SpecialAbilityUpdate.Varied | B/daten | Pack/unpack ticks (ceil when parsing) times a random factor at run time, then floor and lower bound 1 tick | GameEngine/Source/GameLogic/Object/Update/SpecialAbilityUpdate.cpp: startPacking / startUnpacking (GameLogicRandomValueReal(1 - m_packUnpackVariationFactor, 1 + ...)) | Double rounding (ceil, then floor) acts more finely at 60 Hz; lower bound 1 tick is then 1/60 s; duration not exactly double in ticks. | 20,23 |
| 368-374 | SpecialAbilityUpdate.StepCaptureFlash (_flashPhase) | E | Flash phase grows per tick of the preparation by (1 - prepFrames/PreparationTicks) / 3; event CaptureFlash on the change from odd to even | GameEngine/Source/GameLogic/Object/Update/SpecialAbilityUpdate.cpp: SpecialAbilityUpdate::continuePreparation (m_captureFlashPhase += increment / 3.0f, l.1228) | Sum over twice as many ticks doubles: about twice as many CaptureFlash events (flashing and tick sound) per capture. | 23 |
| 472-484 | SpecialAbilityUpdate.TurnToward | E | Turning on the spot per tick by Locomotor.TurnRatePerTick; snaps in as soon as the remaining angle is at most one tick step | own | Correct only with scaled TurnRatePerTick; snap threshold halves, aligning and with it the start of unpacking up to one tick different. | 20,23 |

**`Mod\SpecialPowerModules.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 107 | SpecialPowerModuleBase.AvailableOnFrame | F/tick | Absolute ready tick of the special power as public read access for the HUD (ReadyFraction) | own | Tick value leaves the simulation; HUD must convert the remaining time via the rate instead of via 30. | 21 |
| 369-382 | BuildCompleteModule.OnSpawn / Update | E | OnBuildComplete runs exactly one tick after the spawn (NextWake = Tick + SleepNone), then SleepForever | own | Pure tick order: special power creation and upgrade grant 1/60 s instead of 1/30 s after the spawn; do not scale. | 23 |
| 589 | SpectreGunshipDeploymentUpdate.TryTakePending (world.Tick - value.Tick <= 2) | E | Tolerance of 2 ticks between the request for the gunship and its appearance (entity is created at the end of the tick) | own | Tick order, not time: stays correct; when scaled with the rate, foreign older requests would wrongly match. | 23 |

**`Mod\SpecialsModules.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 112-131 | StickyBombUpdate.Update | C | Bomb is set to the position of the victim every tick (SleepNone), dead victim leads to removal | origin not verified | Load only; position follows the victim with one tick of lag (1/60 s instead of 1/30 s). | 20 |
| 200 | GrantStealthData.RadiusGrowRate | D | Raw value via ModuleConfigReader.Real without conversion (default 0 here, reference default 10.0f), consumed as growth per tick in l.221 | GameEngine/Source/GameLogic/Object/Behavior/GrantStealthBehavior.cpp: update (m_currentScanRadius += d->m_radiusGrowRate); GameEngine/Include/GameLogic/Module/GrantStealthBehavior.h | Radius grows twice as fast; the GPS scrambler marker reaches FinalRadius in half the time and is destroyed twice as early. | 20 |
| 218-242 | GrantStealthBehavior.Update | E | Per tick: _radius += RadiusGrowRate, scan of all objects in the radius, on reaching FinalRadius last scan and destruction | GameEngine/Source/GameLogic/Object/Behavior/GrantStealthBehavior.cpp: update | Point in time at which an object receives stealth follows the step count; with scaled growth finer radius steps and twice as many scans (load). | 20,23 |
| 269-279 | WanderAIUpdate.Update | E | Never sleeps; in every idle tick two random draws Range(5, 50) and a new move command | GameEngine/Source/GameLogic/Object/Update/AIUpdate/WanderAIUpdate.cpp: WanderAIUpdate::update (GameLogicRandomValue( 5, 50 ) for x and y) | Draws per idle tick: if an object stays idle despite the command, it draws twice as often per second; random sequence shifts. | 23 |
| 368-375 | ProjectileStreamUpdate.Update | C | Every tick: remove vanished projectiles from the front of the ring buffer (20 slots), self-destruction when empty and owner gone | origin not verified | Load only; however, the buffer holds 20 projectiles independent of the rate (with a faster shot sequence in ticks no difference in seconds). | 20 |

**`Mod\StructureModules.cs`** (plus 6 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 91 | StructureCollapseData.MinBurstDelay (fallback) | B/fallback | ModuleConfigReader.Ticks with fallback 9999 ticks for MinBurstDelay (the data always set the value according to the comment) | GameEngine/Include/GameLogic/Module/StructureCollapseUpdate.h: constructor (m_minBurstDelay = 9999) | Fallback in ticks: 333 s at 30 Hz, 167 s at 60 Hz; practically "never", effective only without a data value. | 20 |
| 140 | StructureCollapseUpdate.OnDie (_collapseTick) | B/daten | Random delay Range(MinCollapseDelay, MaxCollapseDelay) at run time over bounds converted to ticks (ceil) when parsing | GameEngine/Source/GameLogic/Object/Update/StructureCollapseUpdate.cpp: beginStructureCollapse (m_collapseFrame = now + GameLogicRandomValue(...)) | Integer value range of the draw doubles; the same draw gives a different delay, not scalable bit-identically. | 20,23 |
| 155,161-166 | StructureCollapseUpdate.Update (_burstTick) | B/daten | Random interval Range(MinBurstDelay, MaxBurstDelay) in ticks between bursts; at most one burst per tick (now >= _burstTick, then += draw) | GameEngine/Source/GameLogic/Object/Update/StructureCollapseUpdate.cpp: update (m_burstFrame += GameLogicRandomValue(d->m_minBurstDelay, d->m_maxBurstDelay)) | Value range of the draw doubles; with interval 0 there would be one burst per tick, i.e. twice as many FX/OCL per second. | 20,23 |
| 157-173 | StructureCollapseUpdate.Update (collapse) | E | Explicit Euler step per tick: _height -= _velocity, then _velocity -= GravityPerTickSquared * (1 - Damping); end when height + template height <= 0 | GameEngine/Source/GameLogic/Object/Update/StructureCollapseUpdate.cpp: update (m_currentHeight -= m_collapseVelocity; m_collapseVelocity -= m_gravity * (1.0 - m_collapseDamping)) | Height before velocity: collapse duration deviates with half the step, FINAL phase (FX/OCL) and IsCollapsed at a different point in time; needs scaled gravity. | 20,23 |
| 280 | FireSpreadUpdate.NextDelay | B/daten | Sleep duration = Max(Range(MinDelay, MaxDelay), SleepNone): random over tick bounds (ceil when parsing), lower bound 1 tick | GameEngine/Source/GameLogic/Object/Update/FireSpreadUpdate.cpp: FireSpreadUpdate::calcNextSpreadDelay (GameLogicRandomValue(m_minSpreadTryDelayData, m_maxSpreadTryDelayData)) | Value range of the draw doubles; with delay 0 ember OCL and spreading in every tick, i.e. twice as often per second. | 20,23 |
| 511-516 | FloatUpdate.Update | C | Activated object is set to the water height every tick (SleepNone) | origin not verified | Load only (derived from state). | 20 |

**`Mod\SupplyModules.cs`** (plus 2 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 279-284 | SupplyCarrierAI.Update | C | Supply state machine runs before the base AI every tick as long as it is not Idle (Min(baseSleep, SleepNone)) | origin not verified | Load only: transitions are state-driven; reaction latency one tick (1/60 s instead of 1/30 s). | 20 |

**`Mod\ToppleAndFallModules.cs`** (plus 3 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 52 | ToppleUpdateData.InitialVelocityPercent | D | Raw factor (default 0.2): angular velocity per tick = impact speed times factor (l.113); impact speed comes from the caller (SimulationWorld.Collision.cs, not audited) | GameEngine/Source/GameLogic/Object/Update/ToppleUpdate.cpp: START_VELOCITY_PERCENT = 0.2f; applyTopplingForce (m_angularVelocity = toppleSpeed * m_initialVelocityPercent) | Radians per tick: if the impact speed per tick is scaled, the velocity halves with it, otherwise the object topples twice as fast. | 20 |
| 53 | ToppleUpdateData.InitialAccelPercent | D | Raw factor (default 0.01): angular acceleration per tick² = impact speed times factor (l.114), without conversion | GameEngine/Source/GameLogic/Object/Update/ToppleUpdate.cpp: START_ACCEL_PERCENT = 0.01f; applyTopplingForce (m_angularAcceleration = toppleSpeed * m_initialAccelPercent) | Per tick²: even with scaled impact speed (factor 1/2) a further factor 1/2 is missing; acceleration per second² would be double. | 20 |
| 73 | ToppleUpdate.VelocityBounceLimit | A | Threshold 0.01 radians per frame: below it after the bounce the object stays down (death by TOPPLED) | GameEngine/Source/GameLogic/Object/Update/ToppleUpdate.cpp: ToppleUpdate::update (const Real VELOCITY_BOUNCE_LIMIT = 0.01f) | Velocity per tick is half as large at 60 Hz; unscaled threshold ends the bouncing earlier (different number of bounces, different time of death). Scaling with 30/rate. | 20,23 |
| 144-168 | ToppleUpdate.Update | E | Euler per tick: angle += velocity (capped at ANGULAR_LIMIT), bounce with BounceVelocityPercent, otherwise velocity += acceleration; no acceleration in the bounce tick | GameEngine/Source/GameLogic/Object/Update/ToppleUpdate.cpp: ToppleUpdate::update | Fall time, number of bounces and death tick change; the omitted acceleration step at the bounce is bound to the tick. | 23 |
| 209 | StructureToppleData.StructuralDecay | D | Raw factor (default 1 here, reference constructor 0.0f), multiplicative decay of the structural integrity per tick in l.316 | GameEngine/Source/GameLogic/Object/Update/StructureToppleUpdate.cpp: update (m_structuralIntegrity *= d->m_structuralDecay); GameEngine/Include/GameLogic/Module/StructureToppleUpdate.h | Decay per tick: after one second decay^60 instead of decay^30; integrity falls faster, building topples earlier. Conversion would be decay^(30/rate). | 20,23 |
| 228 | StructureToppleUpdate.AccelFactor | A | Angular acceleration factor 0.02 per frame² (times sin(angle) times (1 - integrity)), use l.313 | GameEngine/Source/GameLogic/Object/Update/StructureToppleUpdate.cpp: update (static const Real TOPPLE_ACCELERATION_FACTOR = 0.02f) | Unscaled, the building falls in the same number of ticks, i.e. in half the time; scaling with (30/rate)². | 20,23 |
| 272-273 | StructureToppleUpdate.OnDie (_toppleFrame) | B/daten | Random delay Max(0, Range(MinToppleDelayTicks, MaxToppleDelayTicks)) at run time over bounds converted to ticks (ceil) when parsing | GameEngine/Source/GameLogic/Object/Update/StructureToppleUpdate.cpp: beginStructureTopple (m_toppleFrame = now + GameLogicRandomValue(d->m_minToppleDelay, d->m_maxToppleDelay)) | Integer value range of the draw doubles; the same draw gives a different delay, not scalable bit-identically. | 20,23 |
| 302-304,328 | StructureToppleUpdate.Update (_doneAtTick) | E | _doneAtTick = tick of the impact; state Done (IsToppled) is set only in the following tick | own | Pure tick order: IsToppled 1/60 s instead of 1/30 s after the impact; do not scale. | 23 |
| 310-330 | StructureToppleUpdate.StepToppling | E | Euler per tick: velocity += acceleration, integrity *= StructuralDecay, angle += velocity; excess at the ground is subtracted from the velocity | GameEngine/Source/GameLogic/Object/Update/StructureToppleUpdate.cpp: update (TOPPLE_ACCELERATION_FACTOR * Sin(m_accumulatedAngle) * (1.0 - m_structuralIntegrity)) | Non-linear feedback via sin(angle): topple duration is not step-invariant; impact tick and order of the damage shots change. | 23 |
| 333-349 | StructureToppleUpdate.ApplyCrushingDamage | E | One weapon shot per tick in which the leading edge reaches further than _lastCrushedDistance (the reference fires at a fixed spatial spacing along the distance) | GameEngine/Source/GameLogic/Object/Update/StructureToppleUpdate.cpp: applyCrushingDamage (loop over m_lastCrushedLocation up to maxDistance in WEAPON_SPACING steps) | Shot count follows the tick count instead of the distance: at 60 Hz roughly twice as many shots, i.e. about double the damage under the falling building. | 23 |
| 418-452 | HeightDieUpdate.Update (_lastZ) | E | Height check every tick; OnlyWhenMovingDown compares Z with the Z of the previous tick; InitialDelay is added to world.Tick only at the first update | GameEngine/Source/GameLogic/Object/Update/HeightDieUpdate.cpp: HeightDieUpdate::update (details not compared) | Finer sampling: death up to half an old tick earlier and closer to the target height; sign comparison stays valid. | 23 |

**`Mod\TransportModules.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 84-93 | ChinookAIUpdate.Update (climb = locomotor.Speed) | E | Height change of the Chinook: per tick one step of locomotor.Speed (horizontal speed per tick as climb/sink rate), clamped to the target height; SleepNone as long as under way | own (deviation according to the comment; reference ChinookTakeoffOrLandingState not looked up) | Climb time halves if Speed does not scale with the rate; with scaled Speed only a different discretisation of the landing time (evacuation waits for Landed). | 20,23 |
| 117 | HelicopterSlowDeathData.FramesPerSecond | B/daten | static readonly Fixed from LogicFramesPerSecond; in Parse divides 4 fields (SpiralOrbitTurnRate, SpiralOrbitForwardSpeed, MinSelfSpin, MaxSelfSpin; ll. 125-129) from per second to per tick, bypassing PerSecondToPerTick | GameLogic/Object/Update/HelicopterSlowDeathUpdate.cpp, field table: INI::parseAngularVelocityReal / parseVelocityReal | Follows the constant, but frozen as a static field at type init; with the rate as a run-time parameter the divisor would stay at the first value. No rounding (Fixed). | 20 |
| 127 | HelicopterSlowDeathData.Parse "SpiralOrbitForwardSpeedDamping" | D | Damping factor per frame read raw (ModuleConfigReader.Real, fallback 1), multiplied per tick in Update l. 234 | HelicopterSlowDeathUpdate.cpp: INI::parseReal, m_spiralOrbitForwardSpeedDamping = 1.0f, update: m_forwardSpeed *= m_spiralOrbitForwardSpeedDamping | At 60 Hz damping is applied twice as often: the forward speed falls quadratically more per second, the crash spiral becomes shorter. | 20,23 |
| 131 | HelicopterSlowDeathData.Parse "SelfSpinUpdateAmount" | D | Angle (degrees→radians, no time conversion) read as the change of the self-spin rate per update; the division by the rate happens only at run time (l. 218) | HelicopterSlowDeathUpdate.cpp: INI::parseAngleReal, m_selfSpinUpdateAmount | See l. 218: meaning depends on the run-time division. | 20 |
| 147-149 | HelicopterSlowDeathData.FixedTicks | B/daten | Milliseconds → unrounded Fixed ticks (DurationMillisecondsToFixedTicks) at parse time; 4 fields: SelfSpinUpdateDelay, MinBladeFlyOffDelay, MaxBladeFlyOffDelay, DelayFromGroundToFinalDeath | HelicopterSlowDeathUpdate.cpp, field table: INI::parseDurationReal | Different rounding than ceil (none); the rounding arises only through the comparisons/countdowns at run time (ll. 216, 237, 257). | 20 |
| 191 | HelicopterSlowDeathBehavior.BeginSlowDeath (SinkDelayVarianceTicks draw) | E | Random draw Range(0, SinkDelayVarianceTicks) in tick units, result discarded (only consumption of the logic stream) | origin not verified (comment names SlowDeathBehavior::beginSlowDeath, not looked up here) | Upper bound of the draw doubles; whether the stream state stays the same afterwards depends on LogicRandom.Range (not audited). | 20,23 |
| 192-193 | HelicopterSlowDeathBehavior.BeginSlowDeath (destruction) | E | DestructionDelayTicks + Range(0, DestructionDelayVarianceTicks): random draw with tick granularity, at least SleepNone (1 tick), gives _destructionTick | origin not verified (SlowDeathBehavior::beginSlowDeath not looked up) | Draw over twice as many integer values: same time span, but different concrete delays than at 30 Hz. | 20,23 |
| 194 | HelicopterSlowDeathBehavior.BeginSlowDeath (_bladeFlyOff) | E | Random draw Range(MinBladeFlyOffDelay, MaxBladeFlyOffDelay) over unrounded Fixed ticks; counted down by 1 per tick in Update ll. 235-238 | HelicopterSlowDeathUpdate.cpp beginSlowDeath: m_bladeFlyOffFrame = GameLogicRandomValueReal(m_minBladeFlyOffDelay, m_maxBladeFlyOffDelay); update: m_bladeFlyOffFrame-- | Time stays the same if the Fixed ticks scale; the drawn value and the trigger tick (rounding up through <= 0) shift. | 20,23 |
| 215-234 | HelicopterSlowDeathBehavior.Update (Spin/Orbit) | E | Euler per tick: Angle += _selfSpin; X/Y += cos/sin(_forwardAngle)·_forwardSpeed; _forwardAngle += SpiralOrbitTurnRate; _forwardSpeed *= Damping (position before angle, damping last) | HelicopterSlowDeathUpdate.cpp update ll. 313-379 (there force via PhysicsBehavior; here kinematic = own) | With halved per-tick values the same rates, but a different spiral path (order position/angle) and damping applied twice. | 20,23 |
| 216 | HelicopterSlowDeathBehavior.Update (SelfSpinUpdateDelay comparison) | B/daten | Run-time comparison (Tick − _lastSelfSpinUpdate) > unrounded Fixed ticks; _lastSelfSpinUpdate starts at 0 (absolute tick), first adjustment therefore immediately | HelicopterSlowDeathUpdate.cpp update: TheGameLogic->getFrame() - m_lastSelfSpinUpdateFrame > m_selfSpinUpdateDelay | Rounding floor+1 ticks through strict >; at 60 Hz finer granularity, interval in seconds slightly shorter. | 20 |
| 218 | HelicopterSlowDeathBehavior.Update (change = SelfSpinUpdateAmount / LogicFramesPerSecond) | A | Run-time division of an angle by 30 (LogicFramesPerSecond) per spin adjustment; _selfSpin is radians per tick, the adjustment happens every SelfSpinUpdateDelay (ms), not per tick | HelicopterSlowDeathUpdate.cpp update ll. 329/343: m_selfSpin += m_selfSpinUpdateAmount / LOGICFRAMES_PER_SECOND | Follows the constant: at 60 the per-tick growth halves, which is correct for a rate per tick; but runs at run time per event instead of at parse time. | 20 |
| 245-254 | HelicopterSlowDeathBehavior.Update (fall) | E | Euler per tick: _fallSpeed += GravityPerTickSquared·FallHowFast, then Z += _fallSpeed (velocity before position); impact at Z <= ground + 1 | HelicopterSlowDeathUpdate.cpp update: pos->z <= ground + 1.0f (threshold verified); fall there via Locomotor setMaxLift, here kinematic = own | Half-step error of the integration changes: impact time and location (X/Y keep moving) deviate, and with them the FXHitGround/OCLHitGround point in time. | 20,23 |
| 257 | HelicopterSlowDeathBehavior.Update (DelayFromGroundToFinalDeath comparison) | B/daten | Run-time comparison (Tick − _hitGroundTick) > unrounded Fixed ticks; _hitGroundTick == 0 also serves as "not hit the ground yet" | HelicopterSlowDeathUpdate.cpp update ll. 477-478 | Rounding floor+1 ticks through strict >; at 60 Hz up to half an old tick earlier. | 20 |
| 385 | HelixContain.Update (SleepNone while structure carried) | C | As long as a PORTABLE_STRUCTURE is carried, the module runs every tick and copies position/angle | own (reference HelixContain.cpp not looked up) | Load only: the structure still follows every tick, result unchanged. | 20 |
| 419 | TunnelContainData.Parse "TimeForFullHeal" | B/fallback | Milliseconds → unrounded Fixed ticks at parse time; if the field is missing: Fixed.Zero (= immediate full healing), reference default is 1 frame | GameEngine/Include/GameLogic/Module/TunnelContain.h: m_framesForFullHeal = 1.0f, "TimeForFullHeal" INI::parseDurationReal | No rounding; the deviating default (0 instead of 1 frame) acts in both cases as full healing in the first tick. | 20 |
| 517-534 | TunnelContain.Update (healing) | E | Every tick (SleepNone) per tunnel: healing MaxHealth / TicksForFullHeal per occupant, full healing as soon as (Tick − EnteredAt) >= TicksForFullHeal; with n tunnels n times per tick | GameLogic/Object/Contain/TunnelContain.cpp update: tunnelSystem->healObjects(modData->m_framesForFullHeal) | With scaled ticks the same healing rate; without scaling double the healing per second. Sliver is Fixed, therefore only rounding differences. | 20,23 |

**`Mod\VehicleModules.cs`** (plus 2 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 110 | BattleBusData.Parse "ThrowForce" | D | Impulse read raw (fallback 1); in BeginSlowDeath l. 178 as _verticalSpeed = ThrowForce / mass directly to the velocity per tick | GameLogic/Object/Behavior/BattleBusSlowDeathBehavior.cpp: m_throwForce = 1.0f, "ThrowForce" INI::parseReal, beginSlowDeath: throwForce.z = data->m_throwForce | Same per-tick impulse at half the tick duration: the bus starts twice as fast (units/s); with scaled gravity it flies about four times as high. | 20,23 |
| 124 | BattleBusSlowDeathBehavior.GroundCheckDelay | A | Constant 10 ticks: ground contact is checked only after that (1 use, l. 173; comparison Tick > _groundCheckTick l. 195) | BattleBusSlowDeathBehavior.cpp: enum GROUND_CHECK_DELAY = 10 | Lockout time halves to 1/6 s; with a flat throw the landing could be detected earlier. | 20 |
| 125 | BattleBusSlowDeathBehavior.EmptyHulkCheckDelay | A | Constant 15 ticks as sleep return value (1 use, l. 223): query whether the hulk is empty | BattleBusSlowDeathBehavior.cpp: enum EMPTY_HULK_CHECK_DELAY = 15, UPDATE_SLEEP(EMPTY_HULK_CHECK_DELAY) | Query twice as often; result affected: start and expiry of the empty deadline are detected only at query points (lag up to 15 ticks = 0.25 instead of 0.5 s). | 20 |
| 165 | BattleBusSlowDeathBehavior.BeginSlowDeath (SinkDelayVarianceTicks draw) | E | Random draw Range(0, SinkDelayVarianceTicks) in tick units, result discarded | origin not verified (SlowDeathBehavior::beginSlowDeath not looked up) | Upper bound doubles; effect on the logic stream depends on LogicRandom.Range (not audited). | 20,23 |
| 166-168 | BattleBusSlowDeathBehavior.BeginSlowDeath (destruction) | E | DestructionDelayTicks + Range(0, DestructionDelayVarianceTicks), at least 1 tick, gives _destructionTick and wake time | origin not verified (SlowDeathBehavior::beginSlowDeath not looked up) | Same time span, but a different drawn delay because of the doubled tick granularity. | 20,23 |
| 188-203 | BattleBusSlowDeathBehavior.Update (throw) | E | Euler per tick: Z += _verticalSpeed, then _verticalSpeed += GravityPerTickSquared (position before velocity), clamping to the ground, landing when Tick > _groundCheckTick and Z <= ground | own (kinematic; reference pushes via PhysicsBehavior, BattleBusSlowDeathBehavior.cpp update) | Trajectory and landing tick change with the step size; together with ThrowForce (raw) and GroundCheckDelay not step-invariant. | 20,23 |
| 281 | AnimationSteeringUpdate.MinTransitionTicks | F/tick | Duration in ticks (ModuleConfigReader.Ticks "MinTransitionTime", l. 269) is handed to the presentation (UnitConditionTracker according to the comment; not read there) | origin not verified (comment names m_transitionFrames; AnimationSteeringUpdate.cpp not looked up) | Value doubles with the rate; the consumer in the presentation must keep interpreting it as simulation ticks or as time. | 21 |

**`Mod\WorldEffectModules.cs`** (plus 14 plain conversion calls, §2)

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 108-154 | OCLUpdate.Update (SleepNone) | C | Module runs every tick and compares Tick >= _nextCreation; _nextCreation == 0 is the marker "timer never started yet" | GameLogic/Object/Update/OCLUpdate.cpp update: UPDATE_SLEEP_NONE, m_nextCreationFrame == 0 | Load only; change of owner is detected one (shorter) tick later. | 20 |
| 158 | OCLUpdate.SetNextCreation | E | Random draw Range(MinDelay, MaxDelay) in whole ticks, _nextCreation = Tick + delay | OCLUpdate.cpp ll. 245-248: GameLogicRandomValue(m_minDelay, m_maxDelay), m_nextCreationFrame = m_timerStartedFrame + delay | Same time span, but draw over twice as many values: concrete drop/reinforcement times deviate from 30 Hz. | 20,23 |
| 247-262 | DynamicShroudClearingRangeUpdate.Update (grow/shrink) | E | Per tick: _current += native / GrowTime or −= (native − FinalVision) / ShrinkTime (tick counts from ceil-rounded durations, at least 1) | GameLogic/Object/Update/DynamicShroudClearingRangeUpdate.cpp update (state sequence ll. 223-229 verified; step formula not compared) | Step scales with the tick counts, i.e. invariant except for the ceil rounding of GrowTime/ShrinkTime. | 20,23 |
| 264-272 | DynamicShroudClearingRangeUpdate.Update (_stateCountdown/_changeIntervalCountdown) | C | Tick countdowns: _stateCountdown-- per tick; vision range is applied when _changeIntervalCountdown is 0, then set to ChangeInterval/GrowInterval (period = interval + 1 ticks) | DynamicShroudClearingRangeUpdate.cpp update ll. 271-279 ("it is important that this gets called every frame without sleeping") | Result affected: the period interval+1 ticks corresponds to a different time at 60 Hz (the +1 tick halves); vision range steps fall on different points in time. | 20,23 |
| 314 | FireOclAfterCooldownData.Parse "OCLLifetimeMaxCap" | B/fallback | ModuleConfigReader.Ticks with fallback 1000 ticks (= 33.3 s at 30 Hz); in the same call OCLLifetimePerSecond raw (ms of lifetime per second of fire, fallback 1000; rate-neutral ratio) | GameLogic/Object/Update/FireOCLAfterWeaponCooldownUpdate.cpp constructor: m_oclMaxFrames = 1000, m_oclLifetimePerSecond = 1000; "OCLLifetimeMaxCap" INI::parseDurationUnsignedInt | Fallback would stay 1000 ticks = 16.7 s; ceil rounding of the data value at parse time. | 20 |
| 359 | FireOCLAfterWeaponCooldownUpdate.Update (LastFireTickOf == now − 1) | E | A series of shots is detected when the last shot fell exactly in the previous tick; requires that the module runs every tick (SleepNone l. 373) and is read after the weapon step | FireOCLAfterWeaponCooldownUpdate.cpp update l. 143: weapon->getLastShotFrame() == now - 1 | Semantics "one tick later" stay as long as every tick runs; end of the series (ReadyTick < now) is detected at finer granularity. | 23 |
| 385 | FireOCLAfterWeaponCooldownUpdate.FireOcl (lifetime) | B/daten | Run-time calculation (Tick − _startTick) · LifetimePerSecond / 1000 in ticks, integer division (truncate), capped at MaxLifetimeTicks | FireOCLAfterWeaponCooldownUpdate.cpp ll. 200-202: seconds = (now - m_startFrame) * SECONDS_PER_LOGICFRAME_REAL; oclFrames = seconds * LOGICFRAMES_PER_SECOND | Ticks-to-ticks ratio, rate-neutral; only the truncation acts on tick durations half as large. | 20 |
| 475-479 | WaveGuideUpdate.Update (splash draw) | A | Every LogicFramesPerSecond / 2 ticks (integer division = 15, strict > gives period 16) one logic random draw Range(1,100), result discarded; _splashTick starts at 0 | GameLogic/Object/Update/WaveGuideUpdate.cpp update ll. 808-815: getFrame() - m_splashSoundFrame > LOGICFRAMES_PER_SECOND / 2.0f, GameLogicRandomValue(1, 100) | Follows the constant (30 ticks); number of draws per wave changes slightly (period 31 instead of 2·16 ticks) and with it the logic stream. | 20,23 |
| 481-490 | WaveGuideUpdate.Update (Advance + DoDamage per tick) | E | Every tick: one step along the path, end check (distance <= 10 cells) and damage sampling at the front points of the current position; each object gets wet only once | WaveGuideUpdate.cpp update/doDamage (sequence according to the comment; only PATH_EXTRA_DISTANCE and UPDATE_SLEEP_NONE verified) | Twice as many sampling positions: objects at the edge of the DamageRadius and the "behind the wave" test turn out differently, hit and destruction tick shift. | 23 |
| 511-537 | WaveGuideUpdate.Advance | E | Path follower: step = locomotor.Speed per tick, remainder is carried over to the next waypoint on reaching one | own (reference lets the AI fly, according to the comment) | With scaled Speed the same distance per second (carry-over makes the path step-invariant); without scaling a wave twice as fast. | 20,23 |
| 628 | ScanData.Parse "ScanDelayTime" | B/fallback | ModuleConfigReader.Ticks with fallback SimulationTiming.LogicFramesPerSecond (= 1 s in ticks); for CheckpointUpdate and EnemyNearUpdate | GameEngine/Include/GameLogic/Module/CheckpointUpdate.h and EnemyNearUpdate.h: m_enemyScanDelayTime = LOGICFRAMES_PER_SECOND | Follows the constant (stays 1 s), but is bound at the parse time of the module; ceil rounding of the data value. | 20 |
| 640 | CheckpointUpdate.GateStep | D | Constant 0.333 length units per tick: gate (minor radius) shrinks/grows per tick by this value (2 uses, ll. 673/675; no clamping to 0 or max) | GameLogic/Object/Update/CheckpointUpdate.cpp update ll. 157/161: radius - 0.333f / radius + 0.333f | Gate opens and closes twice as fast; GateRadius is state (hash), still overshoots by up to one step. | 20,23 |
| 662 | CheckpointUpdate.OnSpawn (_scanDelay draw) | E | Random draw Range(0, ScanDelayTicks) in ticks; the value is stored but never read in Update (scan runs every tick) | CheckpointUpdate.cpp constructor l. 55: m_enemyScanDelay += GameLogicRandomValue(0, m_enemyScanDelayTime) | Only the stored value (state hash) and possibly the logic stream change; no behaviour depends on it. | 20,23 |
| 665-676 | CheckpointUpdate.Update (scan per tick) | C | Every tick two full searches over all objects (nearest enemy/ally), then gate step; no scan interval | CheckpointUpdate.cpp update l. 71: if (m_enemyScanDelay == 0 \|\| TRUE) | Load doubles (2 O(n) searches per tick and checkpoint); result: open/closed is detected one shorter tick earlier, gate movement see GateStep. | 20 |
| 713 | EnemyNearUpdate.OnSpawn (_scanDelay draw) | E | Random draw Range(0, ScanDelayTicks) as start offset of the scan in ticks | GameLogic/Object/Update/EnemyNearUpdate.cpp constructor l. 51: GameLogicRandomValue(0, m_enemyScanDelayTime) | Offset drawn over twice as many values: first scan falls on a different point in time than at 30 Hz. | 20,23 |
| 715-724 | EnemyNearUpdate.Update (_scanDelay countdown) | C | Tick countdown per tick (SleepNone): at 0 scan and reset to ScanDelayTicks, otherwise −1; period = ScanDelayTicks + 1 ticks | EnemyNearUpdate.cpp update ll. 68-78: if (m_enemyScanDelay == 0) … else --m_enemyScanDelay | Result affected (IsEnemyNear is state, drives the ENEMYNEAR animation): period 61 instead of 2·31 ticks, scan times drift relative to 30 Hz. | 20,23 |
| 796-819 | FirestormDynamicGeometryInfoUpdate.Update (geometry transition) | E | Per tick: _startDelay--, ratio = _timeActive / TransitionTime, _timeActive++; end only at _timeActive > TransitionTime (TransitionTime + 1 steps, twice on reversal) | DynamicGeometryInfoUpdate.cpp update ll. 126-163: m_startingDelayCountdown--, ratio = m_timeActive / m_transitionTime, m_timeActive++ | Linear in ticks, i.e. invariant except for the extra tick per phase and the ceil rounding; radius at the time of a damage scan deviates minimally. | 20,23 |
| 827-831 | FirestormDynamicGeometryInfoUpdate.Update (damage scan) | E | Fixed DamageAmount per scan, scan when Tick − _lastDamage >= DamageDelay (ceil ticks, fallback 0); with DamageDelay 0 damage every tick; _lastDamage starts at 0 (absolute tick) | GameLogic/Object/Update/FirestormDynamicGeometryInfoUpdate.cpp update ll. 215-219; m_delayBetweenDamageFrames = 0.0f, INI::parseDurationReal (unrounded there) | With data without a delay the damage per second doubles; with a delay the same rate, but ceil instead of unrounded rounds differently at 60 Hz (data not audited). | 20,23 |
| 957 | WeaponBonusUpdate.Update (_nextPulse = now + Max(Delay, 1)) | C | Pulse interval BonusDelay in ticks, at least 1 tick (with Delay 0 a pulse every tick); sleep return value l. 961 = distance to the next pulse/expiry | GameLogic/Object/Update/WeaponBonusUpdate.cpp update l. 165: return UPDATE_SLEEP(data->m_bonusDelay) | With Delay > 0 the same time (ceil rounding); with Delay 0 load only (twice as many full searches), bonus duration stays time. | 20 |

### 5.4 Script VM (`Sim\Scripting`)

**`Sim\Scripting/ScriptEngine.Actions.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 20 | ScriptEngine.RetargetInterval | C | Constant 30 ticks; 1 use (l. 611: `_tick % RetargetInterval != follower.Entity % RetargetInterval`), retargeting of hunt/attack/area/position orders staggered by entity id | own | Retargeting every 0.5 s instead of 1 s; changes results (time and number of the move/attack commands, staggering by id), not only load | 20 |
| 50-59 | ScriptEngine.ExecuteAction SET_TIMER/SET_RANDOM_TIMER (frames) | D | Frame count from the map script raw into the counter (`timer.Value = frames`), with SET_RANDOM_TIMER the random range likewise in frames; counter is decremented per tick outside the package | ScriptEngine.cpp ScriptEngine::setTimer (millisecondTimer=false: value = getInt, unconverted) | Script timers run down in half the real time; value must be scaled with rate/30 (decide on the random draw before or after scaling) | 20 |
| 77-82 | ScriptEngine.ExecuteAction ADD_TO_MSEC_TIMER/SUB_FROM_MSEC_TIMER | B/daten | Run-time arithmetic on the tick counter: `Value += SecondsToFrames(x)` or `Value -= SecondsToFrames(x)`; each action rounds up individually (rounding accumulates); SUB subtracts ceil(+x), the original adds ceil(-x) | ScriptEngine.cpp ScriptEngine::adjustTimer (value = -value; += REAL_TO_INT_CEIL(...)) | Accumulated rounding remainders halve in real time; the deviation SUB (ceil(+x) instead of ceil(-x)) occurs at 60 Hz for different fractional values than at 30 Hz | 20 |
| 109 | ScriptEngine.ExecuteAction QUICKVICTORY End(..., 1) | E | Inline literal 1 tick as end-game countdown («immediately, one tick later»); VICTORY/DEFEAT/LOCALDEFEAT in contrast use EndGameDelayTicks (=120, declared in ScriptEngine.cs:72, outside the package; 3 uses ll. 107, 108, Phase13 l. 64) | ScriptEngine.cpp ScriptEngine::startQuickEndGameTimer (m_endGameTimer = 1) | Do not scale: means «next tick»; end comes 1/60 s instead of 1/30 s later | 20 |
| 354-359 | ScriptEngine.SecondsToFrames | B/daten | Own conversion seconds→ticks at run time per action: `seconds * SimulationTiming.LogicFramesPerSecond`, ceiling; not via SimulationTiming.SecondsToTicks; 3 uses (ll. 67, 78, 81: SET_MILLISECOND_TIMER, SET_RANDOM_MSEC_TIMER, ADD_TO/SUB_FROM_MSEC_TIMER) | ScriptEngine.cpp ScriptEngine::setTimer / adjustTimer (REAL_TO_INT_CEIL(ConvertDurationFromMsecsToFrames(value*1000))) | Scales with the constant automatically; rounding up to whole ticks gives a different real time at 60 Hz (max. 1/60 s instead of 1/30 s overhang) | 20 |
| 385-393 | ScriptEngine.Forward (arguments) | D | Parameters of forwarded actions go unconverted as text to the presentation, among them frame counts (CAMERA_FADE_*: frames rise/hold/fall) and second values; no unit in the ScriptOutput | ScriptEngine.cpp ScriptEngine::setFade (m_fadeFramesIncrease/Hold/Decrease = getInt) | No difference in the simulation; the consumer must interpret frame parameters as 1/30 s, not as ticks, otherwise half the fade/camera durations | 21 |
| 394 | ScriptEngine.Forward new ScriptOutput(_tick, ...) | F/tick | Every script output carries the simulation tick as a timestamp for the client | own | Tick timestamp: consumers that derive time from it (tick/30) run twice as fast | 21 |
| 398-405 | ScriptEngine.Forward _pendingPresentation / ScriptEngineOptions.PresentationLatencyTicks | B/literal | Host option in ticks (value comes from the caller, default 0, declared in ScriptEngine.cs:20): `_pendingPresentation[key] = _tick + PresentationLatencyTicks` as absolute tick; controls HAS_FINISHED_*/CAMERA_MOVEMENT_FINISHED (Conditions ll. 220-226) | own | Same tick count is half the real time: scripts that wait for the end of speech/camera continue earlier; callers must form the value with the rate | 20,21 |
| 574-634 | ScriptEngine.UpdateFollowers (incl. UpdateTeamFollower, Phase13 ll. 925-962) | C | Per tick without staggering: arrival check at the waypoint (fixed radius 40), reissue of the march command when not marching, FireAtOrdered per tick as soon as the weapon is ready; only the rest runs on the 30-tick grid | own | Results change slightly: finer sampling (arrival, shot, waypoint change up to 1/60 s earlier), twice as many queries and possibly commands per second | 23 |

**`Sim\Scripting/ScriptEngine.Conditions.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 81-90 | ScriptEngine.EvaluateCondition PLAYER_LOST_OBJECT_TYPE | E | Edge detection against the state of the previous evaluation (counter `count:<player>:<type>`), i.e. per evaluation cadence of the script | - | Result depends on the evaluation cadence: at 60 Hz twice as many comparison points; loss and replacement within one 30 Hz tick become visible | 23 |
| 129-138 | ScriptEngine.EvaluateCondition NAMED_ENTERED_AREA/NAMED_EXITED_AREA | E | Entering/leaving as a change against the previous evaluation (`_areaInside`), true for exactly one evaluation | - | True for one tick only (1/60 s); shorter crossings are detected; scripts with an evaluation delay may hit the change differently | 23 |
| 152-163 | ScriptEngine.EvaluateCondition TEAM_ENTERED_AREA_*/TEAM_EXITED_AREA_* | E | Like NAMED_ENTERED_AREA: change against the previous evaluation (`_areaInside`), true for exactly one evaluation | - | True for one tick only (1/60 s); sampling twice as fine, the moment of triggering shifts by up to half an old tick | 23 |

**`Sim\Scripting/ScriptEngine.ExtendedActions.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 363-392 | ScriptEngine.ProcessTeamBuilds | C | According to the comment per tick (caller outside the package): per open BUILD_TEAM order at most one QueueProductionCommand per tick; lack of money = retry in the next tick | own | Results change: orders are queued twice as fast in real time (limited by HasRoom/money), command order relative to other player commands shifts | 23 |

**`Sim\Scripting/ScriptEngine.Phase13.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 245-258 | ScriptEngine.AttachPendingTraps | E | Booby trap is attached in the tick in which the requested object exists (spawn queue between the ticks) – "one tick later" order | own | Delay 1/60 s instead of 1/30 s; order stays, the victim can die or move less often in the shorter gap | 23 |
| 266-276 | ScriptEngine.UpdateBridgeStates (Changed-Flag) | E | BRIDGE_BROKEN/BRIDGE_REPAIRED: the change flag is set for exactly one update (one tick) and drops again in the next update | - | Window is 1/60 s instead of 1/30 s; scripts that are not evaluated in every tick miss the change as before, only with a window half as long | 23 |
| 341 | ScriptEngine.UpdateCaptureOrders `tick % 30 == 0` | C | Inline literal 30: standing members of a capture order get the march command again every 30 ticks (unstaggered, global grid); range check (25) by contrast per tick | own | Command every 0.5 s instead of 1 s; changes results (moment of resumption, number of commands) | 20 |
| 525 | ScriptEngine.UpdatePendingStructures `tick % SimulationTiming.LogicFramesPerSecond != 0` | C | Waiting construction orders (no money) are only checked when the tick is a multiple of the rate ("at most once per second") | own | Stays 1 s if the constant becomes the rate (integer rate assumed); the grid then lies on other ticks – result (moment of the ConstructCommand) affected only through the grid position | 20 |
| 619 | ScriptEngine.SupplyScanFrames | B/literal | `10 * LogicFramesPerSecond` = 300 ticks; 1 use (l. 840: damage to money source/dozer/collector in the last 300 ticks). The original adds SCAN_RATE = 10 directly to frames (comment there "10 seconds") and additionally throttles the check; here 10 s, without throttling | AIPlayer.cpp AIPlayer::isSupplySourceAttacked (const Int SCAN_RATE = 10) | Stays 10 s if the constant becomes the rate; comparison against Entity.LastDamageTick (absolute tick) | 20 |
| 689-701 | ScriptEngine.TryEvaluatePhase13Condition SUPPLY_SOURCE_SAFE | C | No cadence implemented: the original re-evaluates only every 2*LOGICFRAMES_PER_SECOND and returns the remembered value in between, here anew at every evaluation (comment l. 689) | ScriptConditions.cpp ScriptConditions::evaluateSkirmishSupplySourceSafe (setCustomFrame(getFrame()+2*LOGICFRAMES_PER_SECOND)) | No tick constant in the code; the answer follows the evaluation cadence of the script (twice as often, more load); if the 2 s cache is added later, express it in seconds | 23 |
| 783-795 | ScriptEngine.UnitHasEmptied `status.Frame == frame - 1` | E | UNIT_EMPTIED is only true if the previous observation lies exactly one tick back (absolute tick in `_transportStatus`, is saved) and the container went from occupied to empty | ScriptConditions.cpp ScriptConditions::evaluateUnitHasEmptied (m_frameNumber == frameNum - 1) | Do not scale: tick adjacency. Assumes evaluation in every tick; if the script cadence were stretched to every 2nd tick, the condition never becomes true | 23 |

**`Sim\Scripting/ScriptEngine.Powers.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 58 | ScriptEngine.TryExecutePowerAction/NAMED_SET_SPECIAL_POWER_COUNTDOWN, NAMED_ADD_SPECIAL_POWER_COUNTDOWN | B/daten | Seconds (Int) * LogicFramesPerSecond at run time per action, without rounding; result as absolute ready tick (world.Tick + frames or AvailableOnFrame + frames, l. 59) | ScriptActions.cpp: ScriptActions::doNamedSetSpecialPowerCountdown / doNamedAddSpecialPowerCountdown (LOGICFRAMES_PER_SECOND * seconds) | Correct as soon as the constant is the rate; run-time conversion via a static constant, no rounding. | 20 |

**`Sim\Scripting/ScriptEngine.Sequential.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 43 | ScriptEngine._busyThisTick (IsIdle l. 126) | E | Objects with a command from this tick count as busy, because the command only arrives in the next world step (one-tick offset between script and world) | own | Offset stays one tick, i.e. 1/60 s instead of 1/30 s; order the same, follow-up instructions of the sequence come correspondingly earlier. | 23 |
| 81 | ScriptEngine.TryExecuteSequentialAction/TEAM_GUARD_FOR_FRAMECOUNT, TEAM_IDLE_FOR_FRAMECOUNT | D | Wait time of the sequence raw in frames from the script parameter Int(p,1) to SetSequenceTimer | ScriptActions.cpp: ScriptActions::doTeamIdleForFramecount / doTeamGuardForFramecount -> setSequentialTimer(team, framecount) | Wait time halves in seconds; mission flows (waves, pauses) run faster. | 20 |
| 85 | ScriptEngine.TryExecuteSequentialAction/TEAM_SPIN_FOR_FRAMECOUNT | D | Wait time of the sequence raw in frames from the script parameter Int(p,1) to SetSequenceTimer (without stopping the units) | ScriptActions.cpp: ScriptActions::doTeamSpinForFramecount -> setSequentialTimer(team, waitForFrames) | Wait time halves in seconds. | 20 |
| 92 | ScriptEngine.TryExecuteSequentialAction/UNIT_GUARD_FOR_FRAMECOUNT, UNIT_IDLE_FOR_FRAMECOUNT | D | Wait time of the object sequence raw in frames from the script parameter Int(p,1) to SetSequenceTimer | ScriptActions.cpp: ScriptActions::doUnitIdleForFramecount / doUnitGuardForFramecount -> setSequentialTimer(obj, framecount) | Wait time halves in seconds. | 20 |
| 132-206 | ScriptEngine.ProcessSequences/SequenceItem.FramesToWait | C | Sequences are checked once per tick: instruction runs when idle or when the counter is exactly 0, otherwise FramesToWait-- (ll. 152-155); wait actions (DontAdvance) are re-checked per tick | ScriptEngine.cpp: ScriptEngine::evaluateAndProgressAllSequentialScripts (m_framesToWait) | Results: wait counters run out in half the time, idleness is sampled twice as finely, the moment of every follow-up instruction shifts. | 20,23 |

**`Sim\Scripting/ScriptEngine.TeamAbilities.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 214 | ScriptEngine.TryExecuteTeamAbilityAction/SCRIPTING_OVERRIDE_HULK_LIFETIME (_hulkLifetimeOverrideTicks) | B/daten | Seconds (Real) -> ticks via SecondsToFrames (Fixed * LogicFramesPerSecond, Ceiling) at run time per action; only recorded, no consumer in the simulation; the original truncates | ScriptActions.cpp: ScriptActions::doOverrideHulkLifetime, (Int)(seconds * LOGICFRAMES_PER_SECOND) | Correct with a parameterised constant; rounding Ceiling instead of truncating deviates from the original (at most 1 tick). | 20 |
| 341 | ScriptEngine.UpdateButtonHunts/_buttonHuntNext | B/fallback | Scan interval of the button hunt = max(ScanRate in ticks, LogicFramesPerSecond): 1 s as default (parse fallback is 0) and at the same time lower bound; at run time per scan | CommandButtonHuntUpdate.cpp: CommandButtonHuntUpdateModuleData constructor, m_scanFrames = LOGICFRAMES_PER_SECOND (the lower bound is own) | Correct with a parameterised constant; the cadence determines when the power is used (result), ScanRate itself is ceil-rounded. | 20 |

**`Sim\Scripting/ScriptEngine.TeamEvents.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 15,29-35 | ScriptEngine._pendingTeamScripts | E | Unit-destroyed scripts are collected while observing the events and only executed in the next update (one tick later) | own | Delay stays one tick, i.e. 1/60 s instead of 1/30 s; order unchanged. | 23 |
| 55-77 | ScriptEngine.ProcessTeamEvents/ScriptTeam.SeeEnemy, CurUnits | C | Enemy sighting and loss threshold of every team with event scripts are checked every tick, script on state change | origin not verified | Mostly load (vision check over all objects twice as often); the moment of the edge becomes finer, results can shift by one tick. | 23 |
| 79-85 | ScriptEngine.ProcessTeamEvents/ScriptTeam.WasIdle | E | OnIdle script runs when the team is idle in two consecutive ticks (idle && WasIdle), and after that every further idle tick | Team.cpp: isIdle && m_wasIdle -> runScript(m_scriptOnIdle) (l. 1914, function name not audited) | The two-tick window is half as long; the script runs twice as often per second as long as the team stays idle. | 23 |

**`Sim\Scripting/ScriptEngine.TeamQueues.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 40-78 | ScriptEngine.UpdateTeamQueues/UpdateBuildingTeam | C | Team queues are checked every tick; with teamExecutesActionsOnCreate the start actions run every tick as long as a member is idle (l. 77) | origin not verified (AIPlayer::checkQueuedTeams/checkReadyTeams found, their call cadence not audited) | Results: start actions run twice as often per second; activation of a team is sampled twice as finely. | 20,23 |
| 83 | ScriptEngine.UpdateReadyTeam/timeExpired | A | A gathered team becomes active at the latest after 60 * LogicFramesPerSecond ticks (60 s); as an expression with the rate constant, computed per tick | AIPlayer.cpp: AIPlayer::checkReadyTeams, m_frameStarted+60*LOGICFRAMES_PER_SECOND | Scales along via the constant; without parameterisation it would be 30 s. | 20 |

**`Sim\Scripting/ScriptEngine.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 20 | ScriptEngineOptions.PresentationLatencyTicks | F/tick | Option in ticks (default 0): how long speech/sound/movie/camera move counts as running; set in Actions.cs Forward (_tick + latency), expiry in Update l. 323; is also saved in the MissionSnapshot | own | On the simulation tick: the same tick count is half the wall-clock time, HAS_FINISHED_*/CAMERA_MOVEMENT_FINISHED become true earlier if the caller does not convert. | 20,21 |
| 26 | ScriptOutput.Tick | F/tick | Every presentation output of the scripts carries the simulation tick as time stamp (Output l. 554, Forward in Actions.cs) | own | Stamp itself rate-neutral; consumers that read tick differences as 1/30 s run twice as fast (simulation tick, no wall-clock time). | 21 |
| 72 | ScriptEngine.EndGameDelayTicks | A | Grace time after VICTORY/DEFEAT = 120 ticks (4 s); _endTimer is decreased by 1 per tick in Update (ll. 316-321); 3 uses in the code (Actions.cs 107/108, Phase13.cs 64) + 1 test | ScriptEngine.cpp: FRAMES_TO_SHOW_WIN_LOSE_MESSAGE = 120, ScriptEngine::startEndGameTimer | Mission ends 2 s instead of 4 s after the decision; victory/defeat display half as long. | 20 |
| 178 | ScriptEngine..ctor/_nextEvaluation (DelayEvaluationSeconds) | B/literal | First evaluation tick of delayed scripts: NameHash % (2*LogicFramesPerSecond+1) = 0..60 ticks (0..2 s), in the constructor; replaces the random value of the original by a name hash | ScriptEngine.cpp: ScriptEngine::checkConditionsForTeamNames, GameLogicRandomValue(0,2*LOGICFRAMES_PER_SECOND) | Scales along via the constant, but modulo 121 instead of 61 distributes the scripts differently: evaluation phases and thus flows change; no rounding. | 20,23 |
| 312-353 | ScriptEngine.Update | C | All active scripts without delay are evaluated every tick (conditions, actions or FalseActions), then followers, button hunt, team events, sequences, team build, team queues | ScriptEngine.cpp: ScriptEngine::update | Results, not only load: non-one-shot scripts run twice as often per second (counter increments, repeated actions/FalseActions, random draws per evaluation). | 20,23 |
| 326-327 | ScriptEngine.Update/Counter.Value (IsTimer) | D | Script timers are counters in frames, decreased by 1 per tick (down to -1); values come raw from SET_TIMER or via SecondsToFrames (Actions.cs), conditions compare them in frames | ScriptEngine.cpp: ScriptEngine::update (m_counters[i].value-- for isCountdownTimer) | Frame timers from map scripts run out in half the time; comparisons against frame literals of the maps mean half the time. | 20 |
| 375 | ScriptEngine.ExecuteScriptBody/_nextEvaluation (DelayEvaluationSeconds) | B/daten | Evaluation interval of a script: whole seconds * LogicFramesPerSecond, computed at run time at every evaluation, integer without rounding; no SimulationTiming converter | ScriptEngine.cpp: ScriptEngine::executeScript, setFrameToEvaluate(getFrame()+delaySeconds*LOGICFRAMES_PER_SECOND) | Correct as soon as the constant is the rate; run-time conversion via a static constant must be parameterised, no rounding. | 20 |

**`Sim\Scripting/ScriptProgram.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 267 | ScriptTeamPrototype.InitialIdleFrames (teamInitialIdleFrames) | D | Map field in frames read raw; consumption in TeamQueues.cs l. 58: _tick > Started + InitialIdleFrames (build time expired -> team sets off or is disbanded) | AIPlayer.cpp: TeamInQueue::isBuildTimeExpired (m_initialIdleFrames) | Build time window of an AI team halves: teams are disbanded earlier or sent off incomplete. | 20 |

### 5.5 Skirmish AI and session (`Sim\Skirmish`)

**`Sim\Skirmish/SkirmishAi.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 44 | SkirmishAiStats.AttackTicks | B/literal | Statistics counter of the attack duration in ticks; is increased by ThinkInterval (15) per think step (l. 226); diagnostics/report only | own | No influence on the state; consumers that read the value as 30 Hz ticks show twice the duration at 60 Hz. | 20,21 |
| 62 | SkirmishAi.FireInterval | C | Observation cadence 5 ticks (Observe: vision, memory of enemies/warehouses, forgetting); uses l. 158 (phase), l. 200 (modulo) | own | Results change: Observe runs twice as often per second, Seen stamps and forgetting shift, and with them commands and hashes. | 20 |
| 63 | SkirmishAi.ThinkInterval | C | Think cadence 15 ticks (0.5 s): all commands of the AI arise only at these ticks; uses ll. 203, 226; must stay a multiple of FireInterval (Think is only reached after a passed FireInterval test) | own | Results change: AI decides twice as often per second (building, producing, attack waves), other random draws and command ticks. | 20 |
| 64 | SkirmishAi.RetryTicks | B/literal | Retry lockout 300 ticks (10 s); default value of Backoff (l. 244; calls without a value ll. 832, 1304) and directly in ll. 588, 872, 1081, 1129 | own | Lockout lasts only 5 s unscaled; ×2 is exact, no rounding. | 20 |
| 65 | SkirmishAi.MemoryTicks | B/literal | Memory duration 900 ticks (30 s) for mobile enemies no longer seen (Forget, l. 300) | own | AI forgets enemies after 15 s instead of 30 s; ×2 exact. | 20 |
| 66 | SkirmishAi.OrderRefreshTicks | B/literal | Command refresh 150 ticks (5 s) for standing units with the same target (CommandArmy, l. 1044) | own | March command is repeated after 2.5 s instead of 5 s, i.e. more commands in the replay; ×2 exact. | 20 |
| 69 | SkirmishAi.PlacementChecksPerThink | C | Budget 400 placement checks per think step (FindPlacement l. 643, FindSupplyPlacement ll. 737/762); when exceeded no building site in this think step | own | Not a tick value, but bound to the think cadence: with unscaled ThinkInterval twice the load per second and twice as many attempts per second (result). | 20 |
| 76 | SkirmishAi.BoardingTicks | B/literal | Boarding deadline 300 ticks (10 s): for that long a unit counts as on its way to the tunnel/transport and receives no march commands (ll. 1083, 1131; checked ll. 211, 1034, 1074, 1079, 1123) | own | Deadline halves to 5 s, units are re-commanded earlier; ×2 exact. | 20 |
| 146 | SkirmishAi.RevisitTicks | B/literal | Revisit deadline 3600 ticks (120 s) for cleared start positions and old scouting cells (ll. 1192, 1248) | own | Revisit after 60 s instead of 120 s; ×2 exact. | 20 |
| 158 | SkirmishAi..ctor _phase | C | Offset of the AI players: _phase = Player.Value % FireInterval, distributes observing/thinking over the ticks | own | Results change as soon as FireInterval is scaled: the phases distribute over more ticks, think ticks per player shift. | 20 |
| 207-227 | SkirmishAi.Think (deadline check) | E | All AI deadlines (_retryAt, _inFlight, _boarding, _powerRetry, _scienceRetry, order/scout age) are only checked at think ticks; effective duration = deadline rounded up to the next think grid (15 ticks) | own | If only the deadline or only the grid is scaled, the effective duration changes (e.g. BuildTicks + 30 hits the grid differently); scale both together. | 20,23 |
| 420 | SkirmishAi.QueueWorker Backoff(…, 60) | B/literal | Retry lockout 60 ticks (2 s) per producer/template when queueing construction vehicles and collectors | own | Lockout 1 s instead of 2 s, workers are re-ordered faster; ×2 exact. | 20 |
| 424 | SkirmishAi.QueueWorker _inFlight Until | B/literal | In-production marker runs until _tick + template.BuildTicks + 30: surcharge of 30 ticks (1 s) on the build time (BuildTicks is already in ticks) | own | Surcharge only 0.5 s; BuildTicks scales along via the data conversion, the literal value 30 does not. | 20 |
| 588 | SkirmishAi.BuildStructures Backoff(essential ? 60 : RetryTicks) | B/literal | Retry lockout per building template: 60 ticks (2 s) for the first supply centre in EconomyFirst mode, otherwise RetryTicks | own | Lockout 1 s instead of 2 s; ×2 exact. | 20 |
| 637 | SkirmishAi.FindPlacement _random.Range | E | Random draw from the private AI stream per ring and placement attempt (start angle) | own | Number of draws depends on think cadence and retry lockouts; without scaling twice as many draws per second, other building sites. | 20,23 |
| 722 | SkirmishAi.FindSupplyPlacement Backoff(…, 150) | B/literal | Retry lockout 150 ticks (5 s) for the scouting trip of the construction vehicle to the supply source | own | March command every 2.5 s instead of 5 s; ×2 exact. | 20 |
| 743 | SkirmishAi.FindSupplyPlacement paths > 12 | C | Budget 12 pathfinding runs per think step in the site search at the warehouse | own | Not a tick value, but per think step: with unscaled think cadence twice the pathfinding load per second. | 20 |
| 805 | SkirmishAi.ManageSites _tick - watch.LastResume < 60 | B/literal | Wait time 60 ticks (2 s) between two resumptions of the same construction site; after 3 unsuccessful resumptions (l. 807) it is cancelled | own | Construction site is cancelled after 3 s instead of 6 s without progress; false cancellations more likely, because the builder arrives less often within 1 s. | 20 |
| 851 | SkirmishAi.BuyScience _tick + 1800 | B/literal | Retry lockout 1800 ticks (60 s) per science after a purchase attempt | own | Lockout 30 s instead of 60 s; ×2 exact. | 20 |
| 872 | SkirmishAi.UsePowers _powerRetry | B/literal | Lockout per special power: RetryTicks after success, 3000 ticks (100 s) with missing science, 900 ticks (30 s) on another rejection | own | Lockouts halve (50 s / 15 s), more rejected commands in the recording; ×2 exact. | 20 |
| 911 | SkirmishAi.ProduceArmy _random.Range | E | Random draw from the private AI stream per free producer and think step (choice of the unit) | own | With unscaled think cadence the draws fall on other ticks and more often: other army composition, other course of the game. | 20,23 |
| 1045 | SkirmishAi.CommandArmy _tick - order.Tick < 60 | B/literal | Minimum interval 60 ticks (2 s) before a marching unit is re-commanded to another target | own | Re-commanding already after 1 s, more commands and a more restless army; ×2 exact. | 20 |
| 1088 | SkirmishAi.UseTunnels Backoff(0x81000000u, 90) | B/literal | Retry lockout 90 ticks (3 s) for the evacuation of the tunnel network | own | Lockout 1.5 s instead of 3 s; ×2 exact. | 20 |
| 1109 | SkirmishAi.UseTransports Backoff(…, 60) | B/literal | Retry lockout 60 ticks (2 s) per transport for the unload command | own | Lockout 1 s instead of 2 s; ×2 exact. | 20 |
| 1214 | SkirmishAi.Scout _tick - _scoutAt < 450 | B/literal | Deadline 450 ticks (15 s) within which a scouting target must be reached, otherwise the cell counts as permanently unreachable (_unreachable) | own | Deadline 7.5 s: at the same unit speed clearly more cells are wrongly written off as unreachable (lasting change of behaviour). | 20 |
| 1280 | SkirmishAi.ScoutForWarehouses Backoff(…, 300) | B/literal | Retry lockout 300 ticks (10 s) per idle collector for the warehouse search | own | Lockout 5 s instead of 10 s; ×2 exact. | 20 |
| 1304 | SkirmishAi.OrderCarriers _tick - since < 90 | B/literal | Idle duration 90 ticks (3 s) after which a collector is sent to the nearest warehouse (afterwards Backoff with RetryTicks) | own | Collector counts as idle after 1.5 s; can rate normal short pauses (docking, turning) as idleness. | 20 |

**`Sim\Skirmish/SkirmishMatchSession.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 33 | SkirmishMatchSession.VictoryCheckInterval | C | Victory check every 15 ticks after the world step (l. 115); determines VictoryTick and thus _endTick | own | Results change: victory is detected up to 14 ticks after the event (0.47 s → 0.23 s); VictoryTick/EndTick and replay end shift. | 20 |
| 76 | SkirmishMatchSession.Start ReplayHeader(… LogicFramesPerSecond …) | G | Writes the tick rate (SimulationTiming.LogicFramesPerSecond) into the replay header of the session | - | Must carry the actual rate of the session; with a fixed 30, header and run no longer match. | 20 |
| 77 | SkirmishMatchSession.Start options.MaxTicks / options.CheckpointInterval | G | Options in ticks go into the replay header (EndTick, checkpoint interval) and into _endTick (ll. 47, 119); default values outside the package: Testing/SkirmishMatchScenario.cs l. 19 (108_000 = "one hour", 1_000) | own | 108_000 ticks are only 30 minutes at 60 Hz; checkpoints twice as dense in real time. | 20,21 |
| 119 | SkirmishMatchSession.Step _options.GraceTicks | B/literal | Grace time after the victory in ticks: _endTick = min(MaxTicks, tick + 1 + GraceTicks); default value 300 (10 s) is in Testing/SkirmishMatchScenario.cs l. 19 (outside the package) | own | Grace time 5 s instead of 10 s; changes EndTick and final hash of the run. | 20 |
| 179 | SkirmishMatchSession.GrantSciencePoints tick % SciencePointInterval | C | Science point for every player every SciencePointInterval ticks (system command); default value 2_700 (90 s) in Testing/SkirmishMatchScenario.cs l. 19 | own | Results change: points come every 45 s instead of 90 s, generals' abilities twice as fast. | 20 |

### 5.6 Scenarios and drivers (`Sim\Testing`)

**`Sim\Testing/MissionObjectiveDriver.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 35 | MissionObjectiveDriver.Run(maxTicks) | H/budget | Default run budget maxTicks = 4500 ticks (150 s at 30 Hz); loop bound world.Tick < maxTicks in line 44 | own | At 60 Hz only 75 s of game time left; callers with the default value reach mission ends less often. | 21 |
| 35 | MissionObjectiveDriver.Run(settleTicks) | H/zeit | Settling time settleTicks = 60 ticks (2 s) before the first planning round (line 42) | own | Only 1 s of settling; the first round meets an earlier mission state, the result of the driver can differ. | 21 |
| 35 | MissionObjectiveDriver.Run(spacing) | C | Planning cadence spacing = 15 ticks: driver.Round() runs every 15 ticks (lines 46-49, nextAt = Tick + spacing) | own | Results: rounds twice as often per game second, interventions/commands fall on other ticks; outcome tick, intervention and command counters of the driver change. | 21 |

**`Sim\Testing/MissionScenario.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 13,48 | MissionRunReport.Ticks / MissionRunReport.OutcomeTick | H/ticks | Report fields in ticks (world tick reached, tick of the mission end), compared field by field by tests/tools | own | Values double for the same game time; fixed expectations in tests/documents no longer fit. | 21 |
| 25-35 | MissionScenario.Run(ticks) / MissionScenario.Continue(ticks) | H/budget | Run budget as an absolute tick count from the caller (no literal here); loop session.World.Tick < ticks | own | Callers with fixed tick counts (e.g. audit with 900 ticks) run only half the game time at 60 Hz. | 21 |
| 64 | MissionScenario.VerifyContinuation half | H/ticks | half = ticks / 2: save/restore halfway, pure ratio to the budget passed in | own | Rate-neutral as a ratio; coupled is only the tick budget passed in by the caller. | 21 |
| 94 | MissionProbe.Run(settleTicks) | H/zeit | Settling time settleTicks = 150 ticks (5 s) before eliminating a side (line 97) | own | Only 2.5 s of settling; scripts with a delay in seconds are not yet that far at the elimination, end probes can turn out differently. | 21 |
| 94 | MissionProbe.Run(waitTicks) | H/zeit | Wait time waitTicks = 600 ticks (20 s) for the script end after the elimination (line 106: world.Tick < killTick + waitTicks) | own | Only 10 s wait time; ends behind second timers of the scripts are counted as not reached (end probes victory/defeat drop). | 21 |
| 113-115 | MissionProbeResult.TicksToOutcome | H/ticks | Report value outcomeTick - killTick in ticks (negative = decided before the probe) | own | Number doubles for the same game time; evaluators that read it as time must convert. | 21 |

**`Sim\Testing/ObjectiveDriver.Commands.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 97 | ObjectiveDriver.PublishObjectives (_objectivesAt, 150) | H/zeit | Objectives of the agent expire after 150 ticks (5 s) without being named again: _world.Tick - _objectivesAt > 150 | own | Expiry after 2.5 s; the commands-only agent loses objectives earlier, attack behaviour and result (1/24) can change. | 21 |
| 116 | ObjectiveDriver.Destroy (commands-only Leaf, cooldown 90) | H/zeit | Commands-only attack leaf: cooldown 90 ticks (3 s), max. 100000 applications; overrides the cooldown passed in | own | Attack commands are renewed every 1.5 s instead of 3 s; number of commands and course of the commands-only run change. | 21 |
| 219-252 | ObjectiveDriver.Act / ObjectiveDriver.DriveAssaults | C | Per tick: _agent.Update() (SkirmishAi) and DriveAssaults, which issues FireWeaponCommand for every attack pair as soon as ChooseWeapon(...).CanFire | own | Mostly load (fire clearance depends on weapon readiness), but commands fall on finer ticks; SkirmishAi.Update has its own tick cadences (other package). | 21 |
| 263 | ObjectiveDriver.AgentSummary (stats.AttackTicks) | H/ticks | Text output "attacking N ticks" without conversion into seconds | own | Number doubles for the same game time; pure diagnostic output. | 21 |

**`Sim\Testing/ObjectiveDriver.Leaves.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 19 | ObjectiveDriver.GuardMargin | E | Guard margin 150 world units around an area, because the driver only looks every few ticks and must remove a unit before its arrival (use line 583); margin against distance per round (15 ticks) | own | With the 15-tick cadence unchanged half the distance per round, the margin is ample; if the cadence is converted to time, it stays as today. | 21 |
| 125-126 | ObjectiveDriver.GoTo (attempt < 2) | H/zeit | Two move commands, then placement; the deadline until placing is 2 × cooldown of the leaf (90 ticks = 3 s per attempt) | own | Units have only half as much game time to walk before they are relocated; more world writes. | 21 |
| 128 | ObjectiveDriver.KillTeamLeaf (20, 30) | H/zeit | Leaf "destroy team": cooldown 30 ticks (1 s), max. 20 applications | own | Cooldown counts ticks: half the game time between applications, time window (applications × cooldown) halved; interventions and outcome tick change. | 21 |
| 130 | ObjectiveDriver.KillPlayerLeaf (20, 60) | H/zeit | Leaf "destroy everything of a player": cooldown 60 ticks (2 s), max. 20 applications | own | Cooldown counts ticks: half the game time between applications, time window halved; interventions and outcome tick change. | 21 |
| 174 | ObjectiveDriver.LeafFor NAMED_DESTROYED (Destroy kill:, 6, 30) | H/zeit | Leaf "destroy named object": cooldown 30 ticks (1 s), max. 6 applications | own | Cooldown counts ticks: time window 3 s instead of 6 s; interventions fall earlier, leaf is exhausted faster. | 21 |
| 184 | ObjectiveDriver.LeafFor NAMED_OWNED_BY_PLAYER (Leaf own: capture, 100000, 90) | H/zeit | Commands-only leaf "capture": cooldown 90 ticks (3 s), practically unlimited | own | Capture command is given again every 1.5 s; number of commands rises, a running capture can be restarted more often. | 21 |
| 185 | ObjectiveDriver.LeafFor NAMED_OWNED_BY_PLAYER (Leaf own: hand, 3, 30) | H/zeit | Leaf "transfer ownership": cooldown 30 ticks (1 s), max. 3 applications | own | Cooldown counts ticks: time window 1.5 s instead of 3 s; leaf exhausted faster. | 21 |
| 198 | ObjectiveDriver.LeafFor UNIT_HEALTH (Destroy health:, 4, 30) | H/zeit | Commands-only variant "shoot down to percent": passes 4/30, Destroy replaces it in commands-only mode by 100000/90 (line 116 of ObjectiveDriver.Commands.cs) | own | Like the attack leaf: commands every 1.5 s instead of 3 s. | 21 |
| 199 | ObjectiveDriver.LeafFor UNIT_HEALTH (Leaf health: set, 4, 30) | H/zeit | Leaf "set health": cooldown 30 ticks (1 s), max. 4 applications | own | Cooldown counts ticks: time window 2 s instead of 4 s; healing/regeneration in between acts only half as long. | 21 |
| 216 | ObjectiveDriver.LeafFor NAMED_INSIDE_AREA/NAMED_ENTERED_AREA (Leaf go:, 4\|100000, 90\|150) | H/zeit | Leaf "send named object into area": cooldown 90 ticks (3 s), max. 4; commands-only 150 ticks (5 s), unlimited | own | Half the travel time per attempt: placement (from attempt 3) comes after 3 s instead of 6 s; more world writes, other outcome ticks. | 21 |
| 226 | ObjectiveDriver.LeafFor NAMED_SELECTED (Leaf select:, 3, 15) | H/zeit | Leaf "select named object": cooldown 15 ticks (0.5 s, equal to spacing), max. 3 applications | own | Cooldown counts ticks: time window 0.75 s instead of 1.5 s. | 21 |
| 241 | ObjectiveDriver.LeafFor TEAM_INSIDE/ENTERED_AREA (Leaf goteam:, 4\|100000, 90\|150) | H/zeit | Leaf "send team into area": cooldown 90 ticks (3 s), max. 4; commands-only 150 ticks (5 s), unlimited | own | Half the travel time per attempt, placement after 3 s instead of 6 s; more world writes, other outcome ticks. | 21 |
| 266 | ObjectiveDriver.LeafFor MULTIPLAYER_ALLIED_VICTORY (Destroy killenemies, 20, 60) | H/zeit | Leaf "destroy all enemies": cooldown 60 ticks (2 s), max. 20 applications | own | Cooldown counts ticks: time window 20 s instead of 40 s; reinforcements moving up after the window stay standing. | 21 |
| 270 | ObjectiveDriver.LeafFor MULTIPLAYER_ALLIED_DEFEAT/PLAYER_DEFEAT (Leaf killown, 20, 60) | H/zeit | Leaf "destroy own side": cooldown 60 ticks (2 s), max. 20 applications | own | Cooldown counts ticks: time window 20 s instead of 40 s. | 21 |
| 282 | ObjectiveDriver.LeafFor PLAYER_HAS_CREDITS (Leaf money:, 3, 30) | H/zeit | Leaf "set account balance": cooldown 30 ticks (1 s), max. 3 applications | own | Cooldown counts ticks: time window 1.5 s instead of 3 s; income in between changes the balance only half as long. | 21 |
| 297 | ObjectiveDriver.LeafFor PLAYER_HAS_N_OR_FEWER_BUILDINGS (Destroy buildings:, 20, 30) | H/zeit | Leaf "destroy buildings down to the limit": cooldown 30 ticks (1 s), max. 20 applications | own | Cooldown counts ticks: time window 10 s instead of 20 s. | 21 |
| 320 | ObjectiveDriver.LeafFor PLAYER_LOST_OBJECT_TYPE (Destroy lose:, 6, 30) | H/zeit | Edge leaf "destroy one object of the type": cooldown 30 ticks (1 s), max. 6 applications | own | Cooldown counts ticks: time window 3 s instead of 6 s. | 21 |
| 328 | ObjectiveDriver.LeafFor PLAYER_ACQUIRED_SCIENCE (Leaf science: purchase, 100000, 300) | H/zeit | Commands-only leaf "buy science": cooldown 300 ticks (10 s), unlimited | own | Purchase attempt every 5 s instead of 10 s; more (rejected) commands. | 21 |
| 334 | ObjectiveDriver.LeafFor PLAYER_ACQUIRED_SCIENCE (Leaf science: grant, 2, 30) | H/zeit | Leaf "grant science": cooldown 30 ticks (1 s), max. 2 applications | own | Cooldown counts ticks: second application after 0.5 s instead of 1 s. | 21 |
| 341 | ObjectiveDriver.LeafFor SKIRMISH_PLAYER_HAS_UNITS_IN_AREA (Leaf present:, 4\|100000, 90\|150) | H/zeit | Leaf "bring unit into area": cooldown 90 ticks (3 s), max. 4; commands-only 150 ticks (5 s), unlimited | own | Half the travel time per attempt, placement after 3 s instead of 6 s; unit stands in the enemy area earlier. | 21 |
| 352 | ObjectiveDriver.LeafFor SKIRMISH_PLAYER_IS_OUTSIDE_AREA (Destroy vacate:, 10, 60) | H/zeit | Leaf "clear area": cooldown 60 ticks (2 s), max. 10 applications | own | Cooldown counts ticks: time window 10 s instead of 20 s; units moving up after the window stay in the area. | 21 |
| 393 | ObjectiveDriver.LeafFor NAMED/TEAM_ATTACKED_BY_OBJECTTYPE (Leaf hurttype:, 3, 30) | H/zeit | Leaf "damage by object type": cooldown 30 ticks (1 s), max. 3 applications | own | Cooldown counts ticks: time window 1.5 s instead of 3 s. | 21 |
| 413 | ObjectiveDriver.LeafFor SKIRMISH_SPECIAL_POWER_READY Recharge (SetReadyFrame) | H/ticks | module.SetReadyFrame(_world.Tick): ready tick of the special power is set to the current absolute tick ("ready immediately") | own | Rate-neutral (genuine tick semantics: now). | 21 |
| 420 | ObjectiveDriver.LeafFor SKIRMISH_SPECIAL_POWER_READY (Leaf carrier: capture, 100000, 90) | H/zeit | Commands-only leaf "capture carrier": cooldown 90 ticks (3 s), unlimited | own | Capture command every 1.5 s instead of 3 s; number of commands rises. | 21 |
| 425 | ObjectiveDriver.LeafFor SKIRMISH_SPECIAL_POWER_READY (Leaf recharge:, 2, 60) | H/zeit | Leaf "finish recharge": cooldown 60 ticks (2 s), max. 2 applications | own | Cooldown counts ticks: second application after 1 s instead of 2 s. | 21 |
| 426 | ObjectiveDriver.LeafFor SKIRMISH_SPECIAL_POWER_READY (Leaf carrier: transfer, 2, 60) | H/zeit | Leaf "transfer carrier and recharge": cooldown 60 ticks (2 s), max. 2 applications | own | Cooldown counts ticks: second application after 1 s instead of 2 s. | 21 |
| 444 | ObjectiveDriver.HurtLeaf (commands-only, 100000, 90) | H/zeit | Commands-only leaf "have it attacked": cooldown 90 ticks (3 s), unlimited | own | Attack commands every 1.5 s instead of 3 s. | 21 |
| 445 | ObjectiveDriver.HurtLeaf (3, 30) | H/zeit | Leaf "one point of damage": cooldown 30 ticks (1 s), max. 3 applications | own | Cooldown counts ticks: time window 1.5 s instead of 3 s. | 21 |
| 452 | ObjectiveDriver.RevealLeaf (commands-only, 100000, 150) | H/zeit | Commands-only leaf "send scout": cooldown 150 ticks (5 s), unlimited | own | Scout command every 2.5 s instead of 5 s. | 21 |
| 453 | ObjectiveDriver.RevealLeaf (2, 60) | H/zeit | Leaf "reveal map": cooldown 60 ticks (2 s), max. 2 applications | own | Cooldown counts ticks: second application after 1 s instead of 2 s. | 21 |
| 479 | ObjectiveDriver.CountLeaf (12\|100000, 30\|90) | H/zeit | Leaf "establish count": cooldown 30 ticks (1 s), max. 12; commands-only 90 ticks (3 s), unlimited; contains GoTo with attempt counter | own | Time window 6 s instead of 12 s; commands-only queues production every 1.5 s (more units/commands). | 21 |
| 579 | ObjectiveDriver.PreventLeaf (Leaf defend:, 100000, 15) | H/zeit | Defence leaf "keep side out of area": cooldown 15 ticks (equal to spacing, i.e. every round), unlimited; is connected with GuardMargin | own | Must follow the planning cadence: unchanged it stays at every round; if only this value is scaled (30) and spacing is not (15), the defence only takes effect every second round. | 21 |

**`Sim\Testing/ObjectiveDriver.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 20 | ObjectiveDriver.IdleRoundsBeforeHarm | H/zeit | 40 planning rounds without progress before harmful leaves are allowed; via spacing = 15 ticks that is 600 ticks (20 s); 1 use (line 305) | own | Counts rounds, not time: with unchanged spacing only 10 s of patience; harmful interventions come earlier, driver result can flip. | 21 |
| 21 | ObjectiveDriver.ExhaustiveRoundsBeforeUnion | H/zeit | 20 planning rounds without progress until the union of all alternatives; via spacing = 15 ticks 300 ticks (10 s); 2 uses via Exhaustive (lines 455, 476) | own | Counts rounds: with unchanged spacing already after 5 s; plan choice and thus interventions change. | 21 |
| 30-35 | ObjectiveDriver.Leaf.Cooldown / Leaf.MaxApplications | H/zeit | Cooldown of a leaf in ticks; checked as tick - last < leaf.Cooldown (lines 318, 331); MaxApplications × Cooldown is the time window of an intervention | own | All leaf cooldowns halve in game time; leaves with few applications are exhausted twice as fast (victory/defeat 24/24 at risk). | 21 |
| 324,336 | ObjectiveDriver.Round (_log) | H/ticks | Log lines of the driver with tick prefix "{tick}: {label}" (part of ObjectiveDriveResult.Log) | own | Tick numbers in the log double; comparisons with old logs no longer match. | 21 |
| 514 | ObjectiveDriver.NeedCondition FLAG (Leaf ui:, 3, 60) | H/zeit | Leaf "press UI switch": cooldown 60 ticks (2 s), max. 3 applications | own | Cooldown counts ticks: 1 s between the applications, time window 3 s instead of 6 s; interventions fall earlier. | 21 |
| 535 | ObjectiveDriver.TeamEventPlan (Destroy teamdie:, 400, 24) | H/zeit | Leaf "destroy team member": cooldown 24 ticks (0.8 s), max. 400; because of spacing 15 it only takes effect in the round after next (30 ticks) | own | Wave missions (MD_CHI04) kill team members in half the game time; interaction with spacing determines the actual interval. | 21 |
| 539 | ObjectiveDriver.TeamEventPlan (Destroy killteams:, 40, 30) | H/zeit | Leaf "destroy team": cooldown 30 ticks (1 s), max. 40 applications | own | Cooldown counts ticks: 0.5 s interval, time window 20 s instead of 40 s; interventions and outcome tick change. | 21 |

**`Sim\Testing/ReferenceMatchScenario.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 28 | ReferenceMatchOptions.Standard CheckpointInterval | C | CheckpointInterval = 1_000 ticks, written into the replay header (line 135) | own | Check density only: twice as many checkpoints per game time; checkpoint list/chain values lie at other points in time, the simulation stays untouched. | 21 |
| 28 | ReferenceMatchOptions.Standard GraceTicks | H/zeit | GraceTicks = 300 ticks (10 s) run-on after the victory (line 151: endTick = tick + 1 + GraceTicks) | own | Only 5 s run-on; EndTick and end hash of the reference run change. | 21 |
| 28,334 | ReferenceMatchOptions.Standard IncomeInterval (with Income) | C | Income of 1,500 money every IncomeInterval = 300 ticks (10 s) per living player; trigger tick % IncomeInterval == 0 (line 334) | own | Results: double income per game second, production/armies grow faster; winner, victory tick and end hash change. | 21 |
| 28 | ReferenceMatchOptions.Standard MaxTicks | H/budget | MaxTicks = 216_000 ticks (2 h at 30 Hz) as hard stop; goes as EndTick into the replay header (line 135) and into endTick (line 139) | own | Only 1 h of game time; the reference run (victory today at tick 13,500) still fits in, but the EndTick in the replay header does not change along with it. | 21 |
| 47 | ReferenceMatchScenario.MoveCadence | C | MoveCadence = 15 ticks: command round (pursuit and FireWeaponCommand, line 340) and victory check (line 147) only at tick % 15 == 0; 2 uses | own | Results: fire/move commands twice as often per game second (fire command at most every 0.25 s instead of 0.5 s), victory tick on a different grid; winner and end hash change. | 21 |
| 48 | ReferenceMatchScenario.ProductionCadence | C | ProductionCadence = 30 ticks: production orders with random choice (SageRandom, up to 6 draws per producer) only at tick % 30 == 0 (line 339); 1 use | own | Results: twice as many production rounds and random draws per game second; command stream, winner and end hash change. | 21 |
| 49 | ReferenceMatchScenario.OrderRefreshTicks | H/zeit | OrderRefreshTicks = 150 ticks (5 s): pursuit command on the same target renewed after 150 ticks at the earliest (line 420: tick - order.Tick >= 150); 1 use | own | Renewal after 2.5 s; more PursueCommand per game time, command stream and end hash change. | 21 |
| 134 | ReferenceMatchScenario.Record (ReplayHeader, SimulationTiming.LogicFramesPerSecond) | H/rate | Copies the tick rate as a constant into the replay header of the reference run | own | Header carries the new rate as soon as the constant is parameterised; the scenario must then get the rate as input instead of reading the constant. | 20,21 |
| 333 | ReferenceMatchScenario.Generate (tick == 0) | H/ticks | Tick 0 is reserved for the starting objects (SpawnCommand with tick 0, lines 191-239); the generator emits nothing there | own | Rate-neutral (genuine tick semantics). | 21 |
| 338,356 | ReferenceMatchScenario.Generate / IssueDockOrders (tick == 1) | H/ticks | Dock commands of the supply carriers exactly at tick 1 (DockCommand with tick 1), i.e. one tick after appearing | own | Rate-neutral as a sequence; carriers start 1/60 s instead of 1/30 s after the beginning. | 21 |

**`Sim\Testing/SkirmishMatchScenario.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 15,19 | SkirmishMatchOptions.Standard SciencePointInterval | C | SciencePointInterval = 2_700 ticks (90 s): per interval one science point for every living player (substitute for the rank system; evaluation in SkirmishMatchSession) | own | Results: points twice as fast per game time, generals' powers earlier; AI courses and hashes change. | 21 |
| 18-19 | SkirmishMatchOptions.Standard MaxTicks | H/budget | MaxTicks = 108_000 ticks; the comment explicitly calls that "One hour of game time" (30 Hz) | own | Only 30 min of game time; AI runs that decide late today end as a stalemate; comment becomes wrong. | 21 |
| 19 | SkirmishMatchOptions.Standard CheckpointInterval | C | CheckpointInterval = 1_000 ticks (use in SkirmishMatchSession/Replay, other package) | own | Check density only: checkpoints twice as dense in game time, simulation untouched. | 21 |
| 19 | SkirmishMatchOptions.Standard GraceTicks | H/zeit | GraceTicks = 300 ticks (10 s) run-on after the victory (evaluation in SkirmishMatchSession, other package) | own | Only 5 s run-on; EndTick and end hash of the AI runs change. | 21 |

**`Sim\Testing/StressScenario.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 16 | StressOptions.Standard CheckpointInterval | C | CheckpointInterval = 1_000 ticks, written into the replay header (line 62) | own | Check density only: checkpoints twice as dense in game time. | 21 |
| 16,117 | StressOptions.Standard DrainTicks | H/zeit | DrainTicks = 6_000 ticks (200 s) drain phase at the end: draining = tick >= Ticks - DrainTicks | own | Drain phase only 100 s; the drain is tick-paced (kills per tick), keeps working, but lies differently relative to the ms lifetimes. | 21 |
| 16 | StressOptions.Standard Ticks | H/budget | Ticks = 216_000 (2 h at 30 Hz) run length of the stress test; loop bound line 64, EndTick in the replay header (line 62) | own | Same tick count, half the game time; load per tick stays, but lifetimes from milliseconds double in ticks (see SyntheticFixture), hash changes. | 21 |
| 61 | StressScenario.Record (ReplayHeader, SimulationTiming.LogicFramesPerSecond) | H/rate | Copies the tick rate as a constant into the replay header of the stress test | own | Header carries the new rate; the scenario must get the rate as input instead of reading the constant. | 20,21 |
| 122-131 | StressScenario.Generate (_newlySpawned) | H/ticks | Timers are given to the entities that appeared in the previous tick (events from Observe): "one tick later" as a sequence | own | Rate-neutral as a sequence (1/60 s instead of 1/30 s offset). | 21 |
| 127 | StressScenario.Generate SetTimerCommand(30, 90, Notify) | H/zeit | Timer with DelayTicks = 30 (1 s) and RepeatTicks = 90 (3 s), directly as ticks in the command | own | Timers fire twice as often per game time; with the same tick count the number of firings per tick stays the same, but relative to ms lifetimes half as long. | 21 |
| 129 | StressScenario.Generate SetTimerCommand(120, 300, Damage) | H/zeit | Damage timer with DelayTicks = 120 (4 s) and RepeatTicks = 300 (10 s), 1 damage per firing | own | Double damage per game second; ratio to the ms lifetimes of the fixture shifts, hash changes. | 21 |
| 132 | StressScenario.Generate (tick % 500 == 250) | C | Every 500 ticks (phase 250) a world timer | own | Results: same commands per tick, twice as many per game time; hash changes only via the interaction with ms data. | 21 |
| 133 | StressScenario.Generate SetTimerCommand(EntityId.Invalid, 45, 0) | H/zeit | One-off world timer with DelayTicks = 45 (1.5 s), no repetition | own | Fires after 0.75 s of game time; unchanged in ticks. | 21 |
| 137 | StressScenario.Generate (remaining, - 60) | H/zeit | The drain distributes the kills so that everything is dead 60 ticks (2 s) before the end: remaining = Ticks - tick - 60 | own | Reserve only 1 s; death modules with an ms duration would need twice as many ticks – without consequences in the fixture, with other data a remainder at the end. | 21 |
| 148-150 | StressScenario.Generate (tick < 100, spawnLongLived) | C | Ramp: in the first 100 ticks TargetPopulation/100 long-lived spawns per tick, afterwards 1–2 per tick | own | Results: spawns per tick fixed; twice as many per game second, ramp lasts 1.7 s instead of 3.3 s. | 21 |
| 151 | StressScenario.Generate (tick % 2 == 0, Spark) | C | Every 2nd tick a "Spark" (LifetimeUpdate 500–1500 ms) | own | Results: lifetime doubles in ticks at the same spawn cadence – about twice as many Sparks at the same time, PeakAlive and hash change. | 21 |
| 152 | StressScenario.Generate (tick % 3 == 0, Puff) | C | Every 3rd tick a "Puff" (DeletionUpdate 300–900 ms) | own | Results: about twice as many Puffs at the same time; PeakAlive and hash change. | 21 |
| 153 | StressScenario.Generate (tick % 40 == 0, Ghost) | C | Every 40 ticks a "Ghost" (ImmortalBody, no lifetime) | own | Results: unchanged per tick, twice as many per game time. | 21 |
| 155-169 | StressScenario.Generate (tick >= 100, one kill per tick) | C | From tick 100 and full population, per tick one Kill/Destroy of a long-lived object (with random draw) | own | Results: turnover per tick fixed, double per game time; random sequence per tick unchanged. | 21 |
| 170-174 | StressScenario.Generate (tick % 2 == 1, DamageCommand) | C | Every 2nd tick (odd) random damage 1–20 to a living object | own | Results: damage per game second double; more objects die before their ms lifetime expires. | 21 |
| 175-179 | StressScenario.Generate (tick % 97 == 0) | C | Every 97 ticks a deliberately invalid DestroyCommand (rejection test) | own | Only the number of rejections per game time; no state effect. | 21 |

**`Sim\Testing/SyntheticFixture.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 35 | SyntheticFixture.Files DefaultThingTemplate BuildTime | B/daten | BuildTime = 1 (seconds); during the catalog build ObjectTemplate converts with SecondsToTicks (round up) into BuildTicks = 30 | own | 60 ticks instead of 30, without rounding error; the fingerprint of the fixture stays, derived tick values change. | 21 |
| 103-104 | SyntheticFixture.Files Spark LifetimeUpdate MinLifetime/MaxLifetime | B/daten | 500 / 1500 ms; LifetimeData.Parse via ModuleConfigReader.Ticks (round up, module parse time) = 15 / 45 ticks | own | 30 / 90 ticks, even without rounding (also 120 Hz); random range of the lifetime twice as wide, different draw results – stress hash 56882F763EABEF67 changes. | 21 |
| 115-116 | SyntheticFixture.Files Puff DeletionUpdate MinLifetime/MaxLifetime | B/daten | 300 / 900 ms; LifetimeData.Parse via ModuleConfigReader.Ticks (round up, module parse time) = 9 / 27 ticks | own | 18 / 54 ticks, even without rounding; random range twice as wide, stress hash changes. | 21 |

## 6. Inventory game layer, presentation, UI, client, data

Here the subtype of class F decides: `tick` must be converted to game time by Phase 21; `originalbild` is already correct (fixed 1/30 s unit on wall-clock time) and must not be tied to the tick rate; `wand` runs on wall-clock time but contains a constant of 30.

### 6.1 `CnCNew.Game`

**`Game\Audio/AudioDirector.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 31 | AudioDirectorOptions.CueLimits (SoundCueLimits MaxPerEventName/WindowTicks/MaxPerTick) | F/tick | Flood protection of the sound cues as literal new(3, 10, 0f, 48): at most 3 per event name in 10 ticks, at most 48 per tick; SoundCueRateLimiter counts cue.Tick (simulation tick) | own | Tick-driven: window 1/6 s instead of 1/3 s and tick upper bound twice as often, so about twice as many sounds per second admitted | 21 |
| 648-654 | AudioDirector.UpdateEva (_lastEvaTick, IAudioGameView.Tick, LiveAudioView.Tick) | F/tick | Eva announcer runs at most once per rendered frame and only if LiveGame.Tick (= World.Tick) has changed; passes the absolute simulation tick as logic frame to EvaAnnouncer.Update; not during pause; callers GameAudio.Update -> AudioDirector.Update -> Tick | Eva.cpp Eva::update (frame = TheGameLogic->getFrame()) | Tick clock: all Eva deadlines in frames (lockout time, expiry) run twice as fast as long as the frame values are not scaled; at 60 Hz and 30 fps the announcer sees only every second tick | 21 |

**`Game\Audio/EvaCatalog.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 126 | EvaCatalog.Frames | B/daten | own copy of the conversion ms -> frames: ceil(ms × SimulationTiming.LogicFramesPerSecond / 1000) when parsing Eva.ini (TimeBetweenChecksMS l.108, ExpirationTimeMS l.113; -1 -> int.MaxValue); not via SimulationTiming.DurationMillisecondsToTicks | Eva.cpp field table: INI::parseDurationUnsignedInt for TimeBetweenChecksMS/ExpirationTimeMS | follows the constant only at compile time (const); if the rate becomes a run-time parameter, it must be passed in here; rounding ceil at parse time, finer at 60 Hz | 21 |
| 140 | EvaCatalog.EvaBuilder.FramesBetweenChecks | A | Default value 900 frames (30 s lockout time) for EvaEvent blocks without TimeBetweenChecksMS; written in frames, not converted | Eva.cpp EvaCheckInfo::EvaCheckInfo m_framesBetweenChecks(900) "30 seconds at 30 fps" | 15 s instead of 30 s lockout time for events without a value of their own | 21 |
| 141 | EvaCatalog.EvaBuilder.FramesToExpire | A | Default value 150 frames (5 s expiry of a waiting announcement) for EvaEvent blocks without ExpirationTimeMS; written in frames | Eva.cpp EvaCheckInfo::EvaCheckInfo m_framesToExpire(150) "5 seconds at 30 fps" | 2.5 s instead of 5 s: waiting announcements expire twice as early | 21 |
| 219 | EvaAnnouncer.Update (frame < 2) | A | before logic frame 2 the announcer does nothing and discards the flags (ignore starting power 0); literal 2 in frames | Eva.cpp Eva::update (if (frame < 2) return) | Grace period 1/30 s instead of 1/15 s; genuine frame semantics, practically without consequences | 21 |
| 234 | EvaAnnouncer.Update (check.NextCheck <= frame + 1) | A | a played check is removed one frame before the lockout time expires ("next frame"); literal +1 frame; besides that ll.163/252 NextCheck = frame + FramesBetweenChecks and l.235 TriggeredOn + FramesToExpire (uses) | Eva.cpp Eva::processPlayingMessages (m_timeForNextCheck <= currentFrame + 1) | +1 frame is 1/60 s; since Update does not run for every tick (once per render frame), the removal can fall one tick later | 21 |

**`Game\Campaign/CampaignController.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 31,39 | CampaignController.StartCurrentMission (maxTicks, presentationLatencyTicks) | B/literal | Interface parameters in ticks, passed unchanged to MissionOptions: maxTicks (run limit, 0 = none) and presentationLatencyTicks (for that many ticks speech/movie/camera outputs count as running, default 0) | own | Callers that pass tick counts get half the duration; default 0 stays the same | 20,21 |

**`Game\Hud/HudMessageFeed.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 35 | HudMessageFeed.UnderAttackFrames | A | "30 * 10" = 300 ticks: at most one under-attack line per 250 world units and 10 s; the rate is written as literal 30; compared with SimEvent.Tick (1 use, l.186) | Radar.cpp Radar::tryEvent (framesBetweenEvents = LOGICFRAMES_PER_SECOND * 10) | Lockout 5 s instead of 10 s: twice as many under-attack messages (tick clock) | 21 |

**`Game\Hud/MatchController.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 79 | MatchController..ctor (Messages.Clock = () => Match.Tick) | F/tick | Message lines of the HUD are stamped with the simulation tick (HudMessageFeed.Clock, default () => 0); age = tick difference in UiMessageLog | own | Tick clock: line age grows twice as fast, see l.449 | 21 |
| 114 | MatchController..ctor (match.BeforeTick = Input.Tick) | C | per simulation tick before the step: GameInputController.Tick -> Selection.Prune and CommandModeController.Update -> StandingOrders.Update (can issue commands, world.Tick > IssuedTick + 1) | own | Cadence follows the tick; results possible, because standing orders are checked per tick and commands issued (details in CnCNew.Presentation, not in this package) | 21 |
| 429 | MatchController.MapCameraSettings (KeyboardRotateRadiansPerFrame) | F/originalbild | Rotation speed per "frame" (default 0.1 rad) scaled with the options slider; CameraInput computes frames = deltaSeconds × CameraSettings.FramesPerSecond (default 30f, CameraInput.cs ll.23/99) | origin not verified | unchanged as long as CameraSettings.FramesPerSecond stays a fixed 30 and is not tied to the tick rate (wall-clock time) | 21 |
| 449 | MatchController.OnTickStepped (Messages.Log.Update(Match.Tick)) | F/tick | Fading out of the message lines once per logic step (LiveGame.TickStepped): UiMessageLog.Update subtracts floor(age × 0.01) alpha per call, threshold MessageDelayMS / 30 / 1000 (integer, = 0) | own (the original updates per render frame, see comment UiMessageLog) | Line disappears after about 277 ticks: around 4.6 s instead of 9.2 s; content of the draw list (alpha, line count) depends on the tick rate | 21 |
| 719 | MatchController.BuildSuperweaponCommands (_superweaponTick != Match.Tick) | C | Superweapon lines are only collected anew if the simulation tick (or the script switch) has changed; in-between frames use the old list | own | load only (twice as many collection runs per second); no change of results | 21 |
| 726 | MatchController.BuildSuperweaponCommands (SuperweaponTimers.Draw(..., Match.Tick, ...)) | F/tick | Simulation tick as blink clock of the ready superweapon (tick / SuperweaponFlashFrames % 2); the text m:ss comes from (readyFrame − Tick) / SimulationTiming.LogicFramesPerSecond in SuperweaponTimers.Collect l.77 (other package) | own | Draw list depends on the tick rate: blinking twice as fast if SuperweaponFlashFrames (ms × 30 / 1000) is not scaled along; remaining time only correct if the constant follows the rate | 21 |

**`Game\Hud/MatchResult.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 16 | MatchResult.EndTick | F/tick | End time of the game as raw simulation tick (set in MatchController l.453 and MatchStatsTracker.Snapshot ll.84/97 from World.Tick); 2 creation sites | own | Number doubles for the same game time; consumers (final screen of the shell) not audited in this package | 21 |

**`Game\Hud/MissionController.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 38 | MissionController..ctor (Hud.Messages.Clock = () => mission.Director.Clock) | F/tick | Message lines of a mission are stamped with the clock of the presentation director: MissionPresentationDirector.Clock = World.Tick + _frozenSteps (MissionPresentation.cs l.131), i.e. logic steps | own | Tick clock: lines age and fade twice as fast (the fading out is done by the director, other package) | 21 |

**`Game\Hud/MissionPauseMenu.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 147 | MissionPauseMenu.RefreshSlots ("(tick {header.Tick})") | F/tick | Savegame list shows the raw simulation tick from the header of the savegame as text, no conversion into time | own | displayed number twice as large for the same game time; old savegames (30 Hz) and new ones are not comparable | 21 |

**`Game\Hud/SuperweaponTimers.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 77 | SuperweaponTimers.Collect seconds | F/tick | Remaining time of the superweapon: (readyFrame - world.Tick) / SimulationTiming.LogicFramesPerSecond, integer, for the display m:ss | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (superweapon timer: readySecs = (getReadyFrame - getFrame) / LOGICFRAMES_PER_SECOND) | Follows the constant; if it stays a compile-time 30, the countdown runs down twice as fast at 60 Hz (display shows double the seconds). | 21 |
| 111-112 | SuperweaponTimers.Draw flashFrames/flash | F/tick | Blinking of ready superweapons: tick / SuperweaponFlashFrames % 2; tick is Match.Tick (MatchController.cs:726), FlashFrames comes from InGameUiSettings (ms * 30 / 1000 in CnCNew.Ui) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (m_superweaponFlashDuration, comparison with TheGameLogic->getFrame()) | Simulation tick as clock: blinks twice as fast at 60 Hz as long as InGameUiSettings converts with its own 30. | 21 |

**`Game\Live/DemoScenarios.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 19 | DemoScenarios.CaptureBuilding Ticks | B/literal | Run time of the demo 'capture-building': 1500 ticks (= 50 s) | own | Demo ends after 25 s; approach, unpacking and raising the flag then possibly no longer fit in (LiveScenario.IsOver). | 21 |
| 24 | DemoScenarios.CaptureBuilding Orders[0].Tick | B/literal | Command time of Capture from tick 10 (= 0.33 s) | own | Command comes after 0.17 s instead of 0.33 s; practically without consequences, but shifts the course of the audit demo. | 21 |
| 39 | DemoScenarios.TankDuel Ticks | B/literal | Run time of the demo 'tank-duel': 900 ticks (= 30 s) | own | Demo ends after 15 s; the return time of the turrets after the kill then possibly lies outside the run. | 21 |
| 46-47 | DemoScenarios.TankDuel Orders[0..1].Tick | B/literal | Attack commands of both tanks from tick 5 (two entries) | own | Commands after 0.08 s instead of 0.17 s; without consequences for the result, shifts audit time axes. | 21 |
| 55 | DemoScenarios.JetStrafe Ticks | B/literal | Run time of the demo 'jet-strafe': 3600 ticks (= 120 s) | own | Demo ends after 60 s; construction, take-off, approach, return flight and landing then no longer fit in completely. | 21 |
| 62-67 | DemoScenarios.JetStrafe Orders[0..5].Tick | B/literal | Production and attack commands from tick 1 (six entries) | own | No difference (earliest possible tick); only kept as a tick literal. | 21 |

**`Game\Live/LiveGame.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 28 | LiveGame.TickSeconds | F/wand | Copy of the rate: const double TickSeconds = 1.0 / SimulationTiming.LogicFramesPerSecond; fixed step of the accumulator in Advance (4 uses: loop, subtraction, cap, interpolation fraction) | own | Follows the constant only at compile time; with a rate parameterised at run time the step must come from the rate of the world, otherwise the simulation runs at half/double speed. | 21 |
| 29 | LiveGame.MaxTicksPerFrame | C | At most 8 simulation ticks per rendered frame (catch-up limit = 267 ms of game time at 30 Hz); after that the accumulator is capped to one tick | own | Load/speed only: at 60 Hz the limit corresponds to 133 ms; below 7.5 fps (or earlier with Speed > 1) the game falls behind wall-clock time. Simulation results untouched. | 21 |
| 178-198 | LiveGame.Advance | F/wand | Driver of all clocks: wall-clock time * Speed -> accumulator -> per TickSeconds one BeforeTick/StepSimulation/SyncAfterTick/TickStepped; Fog.Advance and OnAdvanced run on wall-clock time, projection with fraction accumulator/TickSeconds | own | Correct on wall-clock time, provided TickSeconds follows the real rate; all 'per tick' consumers (BeforeTick, TickStepped: HUD, statistics, sound) are called twice as often. | 21 |
| 203-212 | LiveGame.SyncAfterTick | F/tick | Exactly once per simulation tick: FrameProjector.Observe/Advance, FxExecutor.ExecuteEvents/Update, fetch sound cues - the presentation layers are paced by the tick, not by the render frame | own | FrameProjector and FxExecutor (other packages) get 60 steps per second; everything there that assumes '1 step = 1/30 s' (particles, animation, flashing) runs twice as fast. | 21 |
| 211 | LiveGame.SyncAfterTick fog refresh | C | Update fog target values only if World.Tick % SimulationWorld.VisionIntervalTicks == 0 (constant of the simulation) | own | Display/load only: follows VisionIntervalTicks; if the number stays the same, the fog is updated twice as often. | 20,21 |

**`Game\Live/LiveMission.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 51 | LiveMission.ClientPresentationLatencyTicks | B/literal | 60 seconds as ticks: const int = 60 * SimulationTiming.LogicFramesPerSecond (1800); goes as MissionOptions.PresentationLatencyTicks into the script VM and is stored in the MissionSnapshot (presentationLatencyTicks) | own | Follows the constant at compile time; with a run-time rate it must be computed from the rate, otherwise scripts wait only 30 s for presentations that never finish. Integer, no rounding. | 20,21 |
| 75-81 | LiveMission.StepSimulation | F/tick | Presentation clock of the mission: Director.BeforeStep / Session.Step / Director.AfterStep exactly once per simulation tick (from LiveGame.Advance) | own | The director (texts, fades, filters, radar, completions) is switched 60 times per second; all its frame counts run twice as fast, as far as they are not converted via the rate. | 21 |

**`Game\Live/LiveScenario.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 34 | ScenarioOrder.Tick | B/literal | Field: command applies from this absolute simulation tick (comparison order.Tick > World.Tick in line 108); values are in DemoScenarios | own | Field in ticks instead of seconds: the same number means half the time at 60 Hz. | 21 |
| 41 | DemoScenario.Ticks | B/literal | Field: run time of the demo in ticks; IsOver => World.Tick >= Scenario.Ticks (line 90) | own | Field in ticks instead of seconds: every demo is half as long at 60 Hz. | 21 |
| 103-117 | LiveScenario.StepSimulation | C | Per tick: check due commands, call StandingOrders.Update per player (package Presentation), then World.Step; unfinished commands (Capture, Attack on something not yet built) are retried every tick | own | Small effect on results: twice as many retries/rejected commands per second (sequence numbers run faster), acceptance time finer; cadence of StandingOrders.Update doubles. | 21 |
| 134-139 | LiveScenario.Apply Capture retry | C | CaptureCommand is submitted anew every tick until the simulation accepts it; as long as not in range, move command if no target | own | Load and command counters only (twice as many rejected commands per second); acceptance at most half an old tick earlier. | 21 |

**`Game\Save/SaveGame.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 32 | SaveGame.Version | G | Format version 1 of the savegame; the header stores 'tick' and embeds the MissionSnapshot, the tick rate itself is neither written nor checked on loading (only version, fingerprint, checksums) | own | A 30 Hz savegame would be loaded in a 60 Hz build without an error message and all tick values misinterpreted, unless version or fingerprint carry the rate. | 20,21 |

**`Game\Shell/MissionPresentation.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 46-47 | MissionPostEffects.MotionBlurMaxCount / MotionBlurStep | A | Zoom blur: counter +5 per logic step up to 60, then -5 per step (12 + 11 steps, 'CAMERA_MOTION_BLUR_JUMP'); used in AdvanceFilters 673-688 only in steps with a world tick | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngineDevice/Include/W3DDevice/GameClient/W3DShaderManager.h (ScreenMotionBlurFilter enum MAX_COUNT = 60, COUNT_STEP = 5; postRender in W3DShaderManager.cpp) | The blur jump lasts 0.4 s instead of 0.8 s (step size per tick instead of per time); the camera cut at the peak comes correspondingly earlier. | 21 |
| 74 | MissionPresentationDirector.DefaultPlaceholderTicks | B/literal | Placeholder duration for speech/sound/movie: const long = 3 * SimulationTiming.LogicFramesPerSecond (90); 4 uses (subtitle expiry, movie expiry, two CompletePresentation due times) | own | Follows the constant at compile time. Affects results: the due time goes via CompletePresentation into HAS_FINISHED_* conditions of the scripts; with a fixed 90, speech/movie end after 1.5 s. | 20,21 |
| 75 | MissionPresentationDirector.RadarBlipLifetimeTicks | B/literal | Lifetime of a radar blip: const long = 8 * SimulationTiming.LogicFramesPerSecond (240), compared with Clock - CreatedAtTick (line 630) | own | Follows the constant at compile time; display only (blip 4 s instead of 8 s if the number stays 240). | 21 |
| 131 | MissionPresentationDirector.Clock | F/tick | Presentation clock: World.Tick + _frozenSteps (steps with FREEZE_TIME); basis for camera due times, fades, letterbox, texts, filters, radar. Driven by BeforeStep/AfterStep = one step per simulation tick | own | Clock counts 60 units per second; every duration that is written as a number in this unit (script frames, literals) halves. | 21 |
| 216 | MissionPresentationDirector.LetterboxFadeFrames | B/literal | Copy of the rate as a bare 30: letterbox fade '1000 ms in presentation steps' = 30 frames, not derived from the constant; LetterboxFadeLevel = (Clock - since) / 30 (line 226) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngineDevice/Source/W3DDevice/GameClient/W3DDisplay.cpp (#define LETTER_BOX_FADE_TIME 1000.0f, W3DDisplay::renderLetterBox; there wall-clock milliseconds) | Bars fade in/out in 0.5 s instead of 1 s (tick clock with fixed 30); the original measures wall-clock time. | 21 |
| 270 | MissionPresentationDirector.InputLockedTicks | F/tick | Counter: presentation steps with locked input (BeforeStep line 609, ++ per step); report only ('input locked for N ticks', line 1300) | own | Report number doubles for the same lock time; no influence on game or picture. | 21 |
| 590-610 | MissionPresentationDirector.BeforeStep | F/tick | Per step: expiry of movie/subtitle placeholder, due CompletePresentation due times and camera completion against the tick clock; completion takes effect for the scripts from the update of the same step | own | Granularity of the completions 1/60 s instead of 1/30 s; due times are only right if the tick durations (DurationTicks, DefaultPlaceholderTicks) are formed with the real rate. Affects results for script conditions. | 21 |
| 637-653 | MissionPresentationDirector.AdvanceText | F/tick | One text update per presentation step: UiMessageLog.Update, MilitaryCaptionState.Update (per new character one typing click to the sound), expiry of the cinematic text against Clock | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update; there once per rendered frame with currLogicFrame) | Twice as many updates per second: typewriter, click sequence and fading out run twice as fast (see MissionText rows). | 21 |
| 661-672 | MissionPresentationDirector.AdvanceFilters grey-scale ramp | F/tick | Grey-scale ramp: _bwFrame++ per presentation step, level = _bwFrame / _bwFrames; one logic step stands for one drawn frame of the original | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngineDevice/Source/W3DDevice/GameClient/W3DShaderManager.cpp (ScreenBWFilter, m_curFadeFrame++ per render pass) | At 60 Hz the ramp is through in half the time (counting per tick with raw script frames). | 21 |
| 673-688 | MissionPresentationDirector.AdvanceFilters motion blur | F/tick | Zoom blur: +/- MotionBlurStep per step, only if the world has ticked in this step; at the peak _blurJumpDue (camera cut in the next ApplyCameraTo) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngineDevice/Source/W3DDevice/GameClient/W3DShaderManager.cpp (ScreenMotionBlurFilter::postRender, m_maxCount +/- COUNT_STEP) | 23 steps last 0.38 s instead of 0.77 s. | 21 |
| 773 | MissionPresentationDirector.Apply CAMERA_MOD_SET_FINAL_ZOOM/PITCH seconds | F/tick | Copy of the rate: remaining time of the running camera movement = (_cameraCompletesAtTick - tick) / (double)SimulationTiming.LogicFramesPerSecond, as seconds to the wall-clock tween | own | Follows the constant; if it stays 30, the tween gets double the remaining time and zoom/pitch end only after the movement. | 21 |
| 791 | MissionPresentationDirector.Apply CAMERA_MOD_LOOK_TOWARD seconds | F/tick | Copy of the rate: remaining time of the running camera movement = (_cameraCompletesAtTick - tick) / (double)SimulationTiming.LogicFramesPerSecond for the look rotation | own | Follows the constant; if it stays 30, the camera rotates twice as long as the movement lasts. | 21 |
| 812 | MissionPresentationDirector.Apply CAMERA_MOVE_HOME/MOVE_CAMERA_TO_SELECTION | E | Camera movement without duration: _cameraCompletesAtTick = tick, completion in the BeforeStep of the next tick ('one tick later') | own | CAMERA_MOVEMENT_FINISHED becomes true 1/60 s instead of 1/30 s later; order the same, distance to other script events halved. | 21,23 |
| 832 | MissionPresentationDirector.Apply CAMERA_FADE_* frames | D | Script arguments framesIncrease/framesHold/framesDecrease are taken over raw as steps of the presentation clock ((long)increase ...) and evaluated in FadeAlpha (567-577) against Clock - Start | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/ScriptEngine.cpp (ScriptEngine::updateFades, m_curFadeFrame++ against m_fadeFramesIncrease) | Every script fade (up/hold/down) lasts half as long; mission intros are written for 1/30 s frames. | 20,21 |
| 910 | MissionPresentationDirector.Apply DISPLAY_CINEMATIC_TEXT expiry | F/tick | Copy of the rate: expiry = tick + (long)seconds * SimulationTiming.LogicFramesPerSecond (seconds are truncated first, then times rate); run-time conversion per event | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/ScriptActions.cpp (ScriptActions::doDisplayCinematicText: frames = LOGICFRAMES_PER_SECOND * timeInSeconds; W3DDisplay.cpp counts m_cinematicTextFrames-- per draw) | Follows the constant; if it stays 30, the text stays half as long. Truncation to whole seconds independent of the rate. | 21 |
| 1127 | MissionPresentationDirector.ApplyScene CAMERA_BW_MODE_BEGIN frames | D | Script argument 'frames' raw as step count of the grey-scale ramp (_bwFrames = (int)beginFrames), one frame per presentation step | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/ScriptActions.cpp (ScriptActions::doBlackWhiteMode(Bool, Int frames)) | Fading in the grey scale lasts half as long. | 20,21 |
| 1137 | MissionPresentationDirector.ApplyScene CAMERA_BW_MODE_END frames | D | Script argument 'frames' raw as step count of the return ramp (_bwFrames = (int)endFrames) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/ScriptActions.cpp (ScriptActions::doBlackWhiteMode(Bool, Int frames)) | Fading out the grey scale lasts half as long. | 20,21 |
| 1305 | MissionPresentationDirector.DurationTicks | B/daten | Copy of the rate: script seconds -> ticks at run time per event with Math.Round (not ceil) * SimulationTiming.LogicFramesPerSecond; 5 callers (camera movement, rotation, look at waypoint/object) set _cameraCompletesAtTick | own | Follows the constant; rounding to the nearest tick (finer at 60 Hz). Affects results: the tick due time reports CAMERA_MOVEMENT_FINISHED to the scripts, the visible tween runs alongside on wall-clock time. | 20,21 |

**`Game\Shell/MissionText.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 56 | UiMessageLog.Update timeout | F/tick | Copy of the rate (InGameUiSettings.LogicFramesPerSecond = 30, own constant in CnCNew.Ui): timeout = MessageDelayMilliseconds / 30 / 1000 in integer arithmetic as in the original, comparison with age in frames | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update: messageTimeout = m_messageDelayMS / LOGICFRAMES_PER_SECOND / 1000) | Threshold in frames of the tick clock: same number = half the time; if the Ui constant is set to 60, the integer division changes the value in addition. | 21 |
| 60-66 | UiMessageLog.Update fade | E | Fade-out per update: alpha -= floor(age * 0.01), age in frames of the presentation clock, one update per presentation step (accumulating, not time-invariant) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update, message lines; there one update per rendered frame) | Age grows twice as fast and is subtracted twice as often: lines disappear after about 4.5 s instead of 9 s. | 21,23 |
| 102 | MilitaryCaptionState.BlinkFrames | A | Block cursor toggles every 10 frames (_blockBegin + BlinkFrames - 1 < frame, line 176) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update: blockBeginFrame + 9 < currLogicFrame) | Cursor blinks twice as fast (every 0.17 s instead of 0.33 s). | 21 |
| 136 | MilitaryCaptionState.Begin lifetime | F/tick | Copy of the rate: lifetime = frame + (long)(InGameUiSettings.LogicFramesPerSecond * durationMs / 1000f), truncated, run-time conversion per caption (also for 8000 ms speech subtitles) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::militarySubtitle: currLogicFrame + (Int)((LOGICFRAMES_PER_SECOND * duration) / 1000.0f)) | With a fixed Ui 30 the caption stays half as long; does not follow SimulationTiming but the second constant. | 21 |
| 137 | MilitaryCaptionState.Begin delay | F/tick | Copy of the rate: start delay = (int)(InGameUiSettings.LogicFramesPerSecond * MilitaryCaptionDelayMS / 1000f) (750 ms = 22 frames), truncated; also applies after every line break (line 189) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::militarySubtitle / update: incrementOnFrame = currLogicFrame + (Int)((LOGICFRAMES_PER_SECOND * m_militaryCaptionDelayMS) / 1000.0f)) | With a fixed Ui 30 the typing starts after 0.37 s instead of 0.73 s. | 21 |
| 169-174 | MilitaryCaptionState.Update fade | E | Fade-out per update: alpha -= floor((frame - lifetime) * 0.1), accumulating per presentation step | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update, m_militarySubtitle) | Caption fades out in about half the time (twice as many updates, age in ticks twice as fast). | 21,23 |
| 181-197 | MilitaryCaptionState.Update typing (_speed) | D | MilitaryCaptionSpeed (Language.ini, 1) is used raw as frames between two characters: _increment = frame + _speed, test _increment >= frame -> one character every speed + 1 frames | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update: incrementOnFrame = currLogicFrame + TheGlobalLanguageData->m_militaryCaptionSpeed) | Text is typed twice as fast (30 instead of 15 characters/s) and the typing click is triggered twice as often. | 21 |
| 322-332 | MissionTextOverlay.NamedTimers flashFrames | F/tick | Flashing of an expired timer: frame / NamedTimerFlashFrames % 2, frame = presentation clock; FlashFrames from InGameUiSettings (ms * 30 / 1000) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (m_namedTimerFlashDuration, comparison with TheGameLogic->getFrame()) | Flashes twice as fast as long as InGameUiSettings converts with its own 30. | 21 |
| 326 | MissionTextOverlay.NamedTimers seconds | F/tick | Copy of the rate: script timer value (ScriptEngine.CounterValue, in frames) / InGameUiSettings.LogicFramesPerSecond -> seconds for the display m:ss and for 'ready' (seconds == 0) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::postDraw, named timers) | With a fixed Ui 30 the countdown shows twice the time or runs twice as fast; additionally depends on whether the script VM keeps timers in ticks of the new rate. | 20,21 |

**`Game\Shell/Screens/CampaignScreens.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 150 | LoadGameScreen.Refresh row text | F/tick | Load list shows the raw simulation tick of the savegame: "{Title} (tick {Tick})" | own | Same game time shows twice the number; no conversion into time present. | 21 |

### 6.2 `CnCNew.Presentation`

**`Pres\Camera/RtsCamera.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 22 | RtsCamera.FrameRate | F/originalbild | Copy of the rate `FrameRate = 30f`: converts wall-clock time dt into 30 Hz original frames (uses ll. 147, 195, 200, 217, 250, 252, 277); camera is driven by MatchController.Update(deltaSeconds) | own (camera of the original runs per client frame: W3DView.cpp W3DView::update) | Nothing, runs on wall-clock time; the 30 is the authoring unit of the camera values and must NOT be coupled to the tick rate. | 21 |
| 249-266 | RtsCamera.Update (_shakeTime, _shakeIntensity *= 0.75f) | F/originalbild | Shake spring: wall-clock accumulator with fixed substeps of 1/30 s, per step damping 0.75 and direction reversal, stop below 0.01; trigger MissionPresentation camera.Shake | W3DView.cpp W3DView::update (dampingCoeff = 0.75f) | Unchanged (wall-clock time with fixed 1/30 s step); only wrong if FrameRate were attached to the tick rate. | 21 |
| 277 | RtsCamera.Update (factor, AdjustSpeed) | F/originalbild | Zoom tracking: factor = 1-(1-CameraAdjustSpeed)^(dt*30), frame-rate-independent form of the per-frame factor | W3DView.cpp W3DView::update ((desiredZoom - m_zoom)*m_cameraAdjustSpeed) | Unchanged, wall-clock time. | 21 |
| 278-283 | RtsCamera.Update (MathF.Abs(step) >= 0.0001f) | E | Threshold 0.0001 is compared against the zoom step of one rendered frame (step depends on dt) | own | Tick rate has no influence; at a higher frame rate the step gets smaller and the tracking ends earlier. | 21 |

**`Pres\Camera/RtsCameraSettings.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 46 | RtsCameraSettings.ScrollAmount | F/originalbild | `ScrollAmount = 100f`: scroll amount per 30 Hz frame; applied in RtsCamera with dt*FrameRate or in ScrollByIntent with ScrollAmount*FrameRate (per second) | LookAtXlat.cpp SCROLL_AMT = 100 | Unchanged, wall-clock time. | 21 |
| 71 | RtsCameraSettings.AdjustSpeed (GameData CameraAdjustSpeed) | F/originalbild | Raw value per 30 Hz frame from GameData.ini, read unconverted; consumed in RtsCamera.Update via the exponent dt*30 | W3DView.cpp W3DView::update (TheGlobalData->m_cameraAdjustSpeed) | Unchanged, wall-clock time. | 21 |
| 77 | RtsCameraSettings.KeyboardRotateSpeed (GameData KeyboardCameraRotateSpeed) | F/originalbild | Raw value rad per frame; applied in RtsCamera.RotateByKeyboard with dt*30; RotateByKeyboard has no caller in src (the client rotates via CameraSettings.KeyboardRotateRadiansPerFrame) | GlobalData.cpp ("KeyboardCameraRotateSpeed", parseReal); InGameUI.cpp setAngle(getAngle() -/+ m_keyboardCameraRotateSpeed) | Unchanged, wall-clock time. | 21 |

**`Pres\Fx/FxAudit.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 94,133 | FxAudit.ExecuteAll (settleFrames = 300) | H/budget | Settle budget of 300 FX frames (10 s at 30 Hz) after one frame each per FXList and template; caller PresentationTool FxAuditCommand | own | With scaled FX data 300 frames only suffice for 5 s; key figures (peak, stats, frames) change. | 21 |
| 100-115 | FxAudit.ExecuteAll (checksum) | H/golden | FNV checksum over position, size and alpha of all particles of every frame (FxExecutionSweep.Checksum) | own | The checksum depends on the step sequence; any scaling of the FX data or of the step size changes it. | 21 |
| 122 | FxAudit.ExecuteAll (PrimarySpeed = 10f) | H/ticks | Test input: speed 10 units per frame with a target at 100 units distance | own | Fixed per-frame input; only affected if PrimarySpeed is meant per second in future. | 21 |

**`Pres\Fx/FxCatalog.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 227 | WeaponFxInfo.WeaponSpeed (Weapon "WeaponSpeed") | F/tick | Weapon speed is converted independently of the simulation from the INI text via VelocityPerFrame into distance per frame; serves as FxInvocation.PrimarySpeed for tracers | origin not verified | Second, own conversion next to the simulation: tracers without their own Speed twice as fast. | 21 |
| 317 | DamageFxLine.ThrottleFrames (DamageFX "ThrottleTime") | B/daten | ms→frames (ceil, float) at catalog build, fallback 0; is added to the simulation tick in FxExecutor.Damaged | DamageFX.cpp field table ("ThrottleTime", parseTime) | Throttle time halves in real time; rounding to 30 Hz frames. | 21 |

**`Pres\Fx/FxEffects.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 10 | TracerInstance.Speed / TracerInstance.ExpireFrame | F/tick | Render contract carries distance per frame and expiry frame in FX frames | own | Renderers that extrapolate with it compute in tick frames; consumers in Client.Stride not audited. | 21 |
| 30-31 | DecalInstance.CreatedFrame / ExpireFrame / FadeFrames | F/tick | Decal times in FX frames in the render contract (FadeFrames as a duration); DecalBuffer.Expire l. 72 compares frame >= ExpireFrame | own | Fade and expiry duration in frames halve in real time; consumers in Client.Stride not audited. | 21 |
| 119-134 | CameraShake.Update | E | Shake spring without dt: per call damping *0.75, direction reversal, stop below 0.01; neither an instance nor a caller found in src (FxFrame.ViewShakes is not consumed in src either) | W3DView.cpp W3DView::update (dampingCoeff = 0.75f) | Depends solely on the call frequency; when called per tick the shake decays twice as fast. | 21,23 |

**`Pres\Fx/FxExecutor.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 141-149 | FxExecutor.Damaged (_damageThrottle NextTick = simEvent.Tick + throttle) | F/tick | DamageFX throttle: 30-based frames are added to the simulation tick of the event and compared against later event ticks | own (implementation); data field DamageFX.cpp "ThrottleTime" | Simulation tick: half the throttle time, up to twice as many damage effects. | 21 |
| 235,345 | FxExecutor (Decals.AddScorch(.., Frame), Decals.Expire(frame)) | F/tick | Decals are stamped with the FX frame number and expired against it (scorches have no expiry) | own | FX frame = simulation tick; expiry times of decals with ExpireFrame halve. | 21 |
| 262-273 | FxExecutor.Tracer (lifetime, Frame + lifetime) | F/tick | Tracer lifetime = ceil((distance - length) / speed per frame * DecayAt) in FX frames; expiry = Frame + lifetime | FXList.cpp TracerFXNugget::doFXPos (REAL_TO_INT_CEIL(frames * m_decayAt)) | With unchanged per-frame speed same frame count and half the real time; with scaled speed a different rounding up. | 21 |
| 265-271 | FxExecutor.Tracer (frames = 1f) | B/literal | Fallback duration 1 frame at speed 0 and at negative remaining distance (two literals 1f) | origin not verified | 1 frame is 1/60 s instead of 1/30 s. | 21 |
| 307 | FxExecutor.SpawnParticles (InitialDelayFrames) | B/daten | Run-time conversion per spawn: drawn milliseconds → frames via FxValueReader.MillisecondsToFrames (ceil, 30-based) | origin not verified | Conversion at run time instead of at catalog build; delay halves, rounding to 30 Hz frames. | 21 |
| 337-346 | FxExecutor.Update | F/tick | FX clock: one call = one logic frame (particles, tracers, lights, decals); the only run-time caller in src is LiveGame.SyncAfterTick, i.e. exactly once per simulation tick (besides FxAudit.ExecuteAll) | own (coupling to the tick); original: ParticleSys.cpp ParticleSystemManager::update | Simulation tick: all effects run twice as fast as long as the FX data stay 30-based. | 21,23 |
| 377-381 | FxExecutor.TracerState.Advance | E | per update: opacity -= opacity/(remaining frames), head += direction*speed (distance per frame) | origin not verified (comment names W3DTracerDraw::doDrawModule, not re-read) | Per-tick step: movement and fade-out twice as fast; the fade-out curve depends on the number of steps. | 21,23 |
| 394-408 | FxExecutor.LightState.Advance | E | per update one frame of fading up or down: counters _increaseLeft/_decayLeft are decremented, factor linear in frames | origin not verified (comment names W3DDynamicLight::On_Frame_Update, not re-read) | Light pulse lasts half as long; with scaled frame counts same curve in finer steps. | 21,23 |

**`Pres\Fx/FxList.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 81 | TracerFxNugget.Speed (FXList Tracer "Speed") | F/tick | Tracer speed stored via VelocityPerFrame as distance per FX frame (0 = speed of the caller) | FXList.cpp TracerFXNugget field table ("Speed", INI::parseVelocityReal) | Tracer flies twice as fast; lifetime in frames the same, in real time half. | 21 |
| 116 | LightPulseFxNugget.IncreaseFrames (FXList LightPulse "IncreaseTime") | B/daten | ms→frames (ceil, float) at catalog build, fallback 0 | FXList.cpp LightPulseFXNugget field table ("IncreaseTime", INI::parseDurationUnsignedInt) | Fading up lasts half as long; rounding to 30 Hz frames. | 21 |
| 117 | LightPulseFxNugget.DecreaseFrames (FXList LightPulse "DecreaseTime") | B/daten | ms→frames (ceil, float) at catalog build, fallback 0 | FXList.cpp LightPulseFXNugget field table ("DecreaseTime", INI::parseDurationUnsignedInt) | Fading down lasts half as long; rounding to 30 Hz frames. | 21 |
| 168 | ParticleSystemFxNugget.InitialDelay (FXList ParticleSystem "InitialDelay") | B/daten | Milliseconds stored raw as a random variable (default -1 = keep template); conversion into frames only at run time per spawn (FxExecutor l. 307) | origin not verified | See FxExecutor.SpawnParticles: run-time conversion with the 30 constant. | 21 |

**`Pres\Fx/FxValueReader.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 17 | FxValueReader.FramesPerSecond | F/tick | Copy of the rate `public const float FramesPerSecond = 30f`; basis of all FX conversions (ms→frames, per second→per frame) at catalog build; the FX frames are stepped once per simulation tick | own | Stays 30 while FxExecutor.Update runs 60 times/s: all FX durations halve, all FX speeds double. | 21 |
| 18 | FxValueReader.FramesPerMillisecond | F/tick | derived copy of the rate `FramesPerSecond / 1000f` (0.03 frames per ms) | own | Like FramesPerSecond: ms durations yield half the real time. | 21 |
| 19 | FxValueReader.SecondsPerFrame | F/tick | derived copy of the rate `1f / FramesPerSecond` (1/30 s) | own | Like FramesPerSecond: speeds per frame act twice as fast. | 21 |
| 85-91 | FxValueReader.DurationFrames / MillisecondsToFrames | B/daten | ms→frames with MathF.Ceiling in float (ms*0.03); catalog build time for DurationFrames, additionally a run-time call from FxExecutor.SpawnParticles | origin not verified (comment names INI::parseDurationUnsignedInt, whose body was not re-read) | Rounding up to whole 30 Hz frames; without adjustment half the duration, with adjustment different rounding boundaries. | 21 |
| 94 | FxValueReader.VelocityPerFrame | F/tick | Distance/s → distance/frame by multiplication with SecondsPerFrame (1/30) at catalog build; the fallback is converted back with *30 beforehand | origin not verified (INI::parseVelocityReal only seen as a field parser in FXList.cpp) | Per-frame speeds stay 30-based, movement twice as fast. | 21 |

**`Pres\Fx/ParticleSystemTemplate.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 138 | ParticleSystemTemplate.AngularRateZ ("AngularRateZ") | D | Rotation rate in rad per frame, read raw | ParticleSys.cpp m_fieldParseTable ("AngularRateZ", parseGameClientRandomVariable) | Particles rotate twice as fast. | 21 |
| 139 | ParticleSystemTemplate.AngularDamping ("AngularDamping") | D | multiplicative damping factor per frame, read raw | origin not verified (field line not re-read) | Damping acts twice as often per second. | 21,23 |
| 140 | ParticleSystemTemplate.VelocityDamping ("VelocityDamping") | D | multiplicative damping factor per frame, read raw | ParticleSys.cpp m_fieldParseTable ("VelocityDamping", parseGameClientRandomVariable) | Damping acts twice as often per second. | 21,23 |
| 141 | ParticleSystemTemplate.Gravity ("Gravity") | D | Acceleration in distance per frame², read raw | ParticleSys.cpp m_fieldParseTable ("Gravity", parseReal) | Four times the acceleration per second². | 21,23 |
| 145 | ParticleSystemTemplate.Lifetime ("Lifetime") | D | Particle lifetime in frames, read raw | ParticleSys.cpp m_fieldParseTable ("Lifetime", parseGameClientRandomVariable) | Half the lifetime. | 21 |
| 146 | ParticleSystemTemplate.SystemLifetime ("SystemLifetime") | D | System lifetime in frames, read raw | ParticleSys.cpp m_fieldParseTable ("SystemLifetime", parseUnsignedInt) | Half the lifetime. | 21 |
| 149 | ParticleSystemTemplate.SizeRate ("SizeRate") | D | Size change per frame, read raw | ParticleSys.cpp m_fieldParseTable ("SizeRate", parseGameClientRandomVariable) | Particles grow twice as fast. | 21 |
| 150 | ParticleSystemTemplate.SizeRateDamping ("SizeRateDamping") | D | multiplicative damping factor per frame, read raw | origin not verified (field line not re-read) | Damping acts twice as often per second. | 21,23 |
| 152 | ParticleSystemTemplate.BurstDelay ("BurstDelay") | D | Frames between two bursts, read raw | ParticleSys.cpp m_fieldParseTable ("BurstDelay", parseGameClientRandomVariable) | Twice the emission rate. | 21,23 |
| 154 | ParticleSystemTemplate.InitialDelay ("InitialDelay") | D | Start delay in frames, read raw (unlike the FXList field of the same name in ms) | ParticleSys.cpp m_fieldParseTable ("InitialDelay", parseGameClientRandomVariable) | Half the delay. | 21 |
| 155 | ParticleSystemTemplate.DriftVelocity ("DriftVelocity") | D | Drift in distance per frame, read raw | ParticleSys.cpp m_fieldParseTable ("DriftVelocity", parseCoord3D) | Twice the drift per second. | 21 |
| 157-165 | ParticleSystemTemplate.VelocitySlots (VelOrthoX/Y/Z, VelSpherical, VelHemispherical, VelCylindricalRadial/Normal, VelOutward, VelOutwardOther) | D | Emission speeds in distance per frame, read raw (nine keys of the same kind, one row) | ParticleSys.cpp m_fieldParseTable ("VelOrthoX", parseGameClientRandomVariable; remaining keys not re-read individually) | Particles fly twice as fast. | 21 |
| 178-179,268 | ParticleSystemTemplate.WindAngleChangeMin / WindAngleChangeMax (default 0.15f / 0.45f) | D | Wind angle change in rad per frame, read raw; default values 0.15 and 0.45 | ParticleSys.cpp m_fieldParseTable ("WindAngleChangeMin", parseReal); default values not re-read | Wind oscillates twice as fast. | 21 |
| 220-242 | AlphaKeyframe.Frame / ColorKeyframe.Frame (Alpha1..8, Color1..8) | D | Keyframe times in frames since particle creation, read raw | ParticleSys.cpp ParticleSystemTemplate::parseRandomKeyframe ("Alpha1".."Alpha8"); parseRGBColorKeyframe not re-read | Gradients run twice as fast. | 21 |

**`Pres\Fx/ParticleWorld.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 111-121 | ParticleWorld.Step (_frame++) | F/tick | Particle clock: one Step = one logic frame (according to the comment 1/30 s); called by FxExecutor.Update once per simulation tick | ParticleSys.cpp ParticleSystemManager::update | Simulation tick; without a rework twice the particle speed and half the lifetimes. | 21,23 |
| 165,216-220 | SystemState.DelayLeft | D | InitialDelay of the template raw as frames (uint truncation) or converted by the nugget; decremented by 1 per Step, system pauses meanwhile | ParticleSys.cpp m_fieldParseTable ("InitialDelay", parseGameClientRandomVariable, no time conversion) | Start delay halves. | 21 |
| 170,311-342 | ParticleWorld.UpdateWind (WindAngleChange = 0.15f, minimum 0.005f) | E | Wind angle changes per Step by WindAngleChange (rad per frame; start value 0.15, minimum 0.005, data WindAngleChangeMin/Max raw); random draws at the reversal | ParticleSys.cpp (m_windAngleChange = 0.15f; "WindAngleChangeMin", parseReal) | Wind oscillates twice as fast, random draws twice as often. | 21,23 |
| 260-290 | ParticleWorld.UpdateSystem (BurstDelayLeft, BurstCount) | E | Emission: every BurstDelay frames (raw, uint truncation) BurstCount particles; BurstDelay 0 means one burst per Step; random draws per burst and particle | ParticleSys.cpp ParticleSystem::update (m_burstDelayLeft) | Twice the emission rate and twice as many random draws per second; BurstDelay 0 cannot be compensated by scaling the frames. | 21,23 |
| 292-298 | ParticleWorld.UpdateSystem (gravity) | E | Gravity of the template (raw, distance per frame²) is added per Step to the acceleration of every particle | ParticleSys.cpp m_fieldParseTable ("Gravity", parseReal); Particle::update (m_vel.z += m_accel.z) | Four times the acceleration per second². | 21,23 |
| 302-307 | SystemState.LifetimeLeft | D | SystemLifetime raw in frames, decremented per Step (0 = permanent) | ParticleSys.cpp m_fieldParseTable ("SystemLifetime", parseUnsignedInt) | System lifetime halves. | 21 |
| 350 | ParticleWorld.CreateParticle (_fieldParticlesLastFrame) | C | Field particle upper bound compares with the count of the last capture; according to the FxLayer comment the capture runs per rendered frame, not per Step | own | Quantity/load only: twice as many emission attempts between two counts. | 21 |
| 404-415 | ParticleWorld.UpdateParticle (velocity, angle, size) | E | Euler per Step: v += a; v *= VelocityDamping; pos += v + DriftVelocity; angle += AngularRate; AngularRate *= AngularDamping; Size += SizeRate; SizeRate *= SizeRateDamping | ParticleSys.cpp Particle::update (m_vel.x *= m_velDamping) | Multiplicative damping per Step acts squared per 1/30 s at 60 Hz, movement twice as fast; scaling requires the root of the dampings and changes the trajectory. | 21,23 |
| 417-448 | ParticleWorld.UpdateParticle (alpha/color keyframes, ColorScale) | E | elapsed = frame - CreatedFrame is compared against keyframe frames (raw); Alpha += AlphaRate, Color += ColorRate and Color += ColorScale per Step | ParticleSys.cpp ParticleSystemTemplate::parseRandomKeyframe; Particle::update | Colour and alpha gradients run twice as fast; the ColorScale addition doubles per second. | 21,23 |
| 451 | Particle.LifetimeLeft | D | Lifetime of the template raw in frames (l. 514, uint truncation), decremented per Step | ParticleSys.cpp m_fieldParseTable ("Lifetime", parseGameClientRandomVariable) | Particle lifetime halves. | 21 |
| 468-478 | ParticleWorld.ApplyWind | E | Position += 2*WindRandomness units per Step (fixed strength per frame, falloff between 75 and 200 units) | origin not verified (comment names updateWindMotion, not re-read) | Twice the wind displacement per second. | 21,23 |
| 501-507 | ParticleWorld.GenerateInfo (system.Pos - system.LastPos) | E | Burst is distributed along the emitter movement of the last Step (LastPos = position before one Step) | origin not verified | Half as long a distribution distance per Step with twice as many bursts; similar, but not identical. | 23 |
| 772-786 | Particle.ComputeAlphaRate / Particle.ComputeColorRate | D | Rate per frame = value difference / difference of the keyframe frames (raw from the data) | ParticleSys.cpp ParticleSystemTemplate::parseRandomKeyframe | Rate per frame stays the same, the gradient is twice as fast in real time. | 21 |

**`Pres\Fx/SoundCues.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 25 | SoundCueDeriver.UnderAttackFrames | A | `30 * 10` = 300 ticks (10 s) lockout per player within radius 250 for the under-attack message; contains the rate as literal 30; compared against simEvent.Tick in l. 140 (1 use) | Radar.cpp Radar::tryEvent (framesBetweenEvents = LOGICFRAMES_PER_SECOND * 10) | Simulation tick: lockout only 5 s, twice as many warning messages. | 21 |
| 179 | SoundCueLimits.MaxPerTick | C | `MaxPerTick = 48` sounds per simulation tick (ll. 204 and 214) | own | Result changes: the upper bound per second doubles (audible, not only load). | 21 |
| 179 | SoundCueLimits.WindowTicks | F/tick | `WindowTicks = 10` ticks (1/3 s) window per event name; filter l. 212 computes with cue.Tick; copy in src/CnCNew.Game/Audio/AudioDirector.cs l. 31 `new(3, 10, 0f, 48)` (other package) | own | Simulation tick: window only 1/6 s, twice as many identical sounds per second. | 21 |

**`Pres\Input/CameraInput.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 18 | CameraSettings.KeyboardRotateRadiansPerFrame | F/originalbild | `= 0.1f` rad per 30 Hz frame (copied as a fixed value, not read from GameData); ll. 132-133 times frames; MatchController.MapCameraSettings scales the value with the options slider | GlobalData.cpp (m_keyboardCameraRotateSpeed = 0.1f) | Unchanged, wall-clock time. | 21 |
| 21 | CameraSettings.KeyboardZoomPerFrame | F/originalbild | `= 10f` height units per 30 Hz frame while the zoom key is held; ll. 135-136 times frames | origin not verified | Unchanged, wall-clock time. | 21 |
| 23 | CameraSettings.FramesPerSecond | F/originalbild | Copy of the rate `FramesPerSecond = 30f` (record default value); l. 99 frames = deltaSeconds*FramesPerSecond for keyboard rotation and keyboard zoom | own | Unchanged (wall-clock time from MatchController.Update); must not be converted to the tick rate. | 21 |

**`Pres\Input/CommandBarModel.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 54,283,307 | CommandBarButton.BuildTicks | F/tick | Build time in simulation ticks (template.BuildTicks, upgrade.BuildTicks) is passed unconverted to the HUD layer | own | The value depends on the tick rate; consumers (tooltip/HUD, not in this package) must convert into seconds with the rate. | 21 |

**`Pres\Input/CommandModeController.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 19 | CommandSettings.MarkerFrames | F/originalbild | Lifetime of all other markers/feedback (attack, enter, rally point, invalid, rejected) = 40 client frames (default); 12 uses | own (analogous to the move marker; the original marks attack targets by flashing) | No difference as long as OrderHintBook converts with a fixed 30 fps; wall clock 1333 ms. | 21 |
| 19 | CommandSettings.MoveHintFrames | F/originalbild | Lifetime of the move/waypoint marker = 40 client frames (default); 3 uses (lines 258, 330, 573); is converted into milliseconds in OrderHintBook.Observe with a fixed 30 fps | GameEngineDevice/Source/W3DDevice/GameClient/W3DInGameUI.cpp W3DInGameUI::drawMoveHints (elapsed <= 40 against TheGameClient->getFrame()) | No difference as long as the conversion stays at a fixed 30 client frames/s (1333 ms wall clock); if it were attached to the tick rate, the duration would halve. | 21 |
| 698-703 | CommandModeController.Update | C | Per simulation tick: StandingOrders.Update and cancellation of the placement mode if the builder is missing/dead | own | Placement cancellation: load only/half the latency; for the Orders part see StandingOrders.Update. | 21 |

**`Pres\Input/CommandTypes.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 93 | CommandFeedback.Tick | F/tick | Timestamp of every feedback in simulation ticks (world.Tick or tick of the rejected command, otherwise 0); no reader found in src (grep feedback.Tick), the lifetime runs on the wall clock | own | No effect as long as nobody forms a duration from it; a future reader would compute with ticks instead of time. | 21 |

**`Pres\Input/GameInputController.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 91-95 | GameInputController.Tick | C | Once per simulation tick before the step (LiveGame.Advance -> BeforeTick, set in MatchController line 114): Selection.Prune + Commands.Update | own | Runs twice as often; Prune only changes load/latency, but via Commands.Update (StandingOrders) command times change, hence results. | 21 |

**`Pres\Input/InputState.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 17 | InputSettings.MiddleClickMaxMilliseconds | F/wand | Middle-click limit 167 ms = 5 client frames at 30 fps (30 constant in wall-clock milliseconds); clock = InputEvent.TimeMs of the client | GameEngine/Source/GameClient/MessageStream/LookAtXlat.cpp MSG_RAW_MOUSE_MIDDLE_BUTTON_UP: const UnsignedInt CLICK_DURATION = 5 against TheGameClient->getFrame() | No difference (wall clock); the 167 ms are a frozen 5/30 conversion and must not be scaled with the tick rate. | 21 |

**`Pres\Input/OrderMarkers.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 41 | OrderHintBook.ClientFramesPerSecond | F/originalbild | Copy of the rate: const int = 30 (client frames/s, not SimulationTiming); 1 use line 59: lifetime = DurationFrames * 1000 / 30 ms; clock = MatchController._clockMs (sum of the frame deltas, wall clock or fixedSeconds in the smoke test) | Unit from W3DInGameUI.cpp drawMoveHints (client frames); the number 30 itself: own | No difference (wall clock); in the parameterisation the 30 must NOT be replaced by the tick rate, otherwise markers live only 667 ms. | 21 |

**`Pres\Input/StandingOrders.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 77-152 | StandingOrders.Update / UpdateAttack | C | Multi-tick commands (attack, attack-move, waypoints, approach) are checked per simulation tick and re-issued as single commands (FireWeaponCommand in every tick with CanFireAt, Pursue/Move on target movement > 10 m); all go through the sink into the replay | own | Results change: twice as many checks and fire commands per second in the replay stream, reaction latency (pursuing, next waypoint, breaking off) halves from 33 to 17 ms. | 21 |
| 88 | StandingOrders.Update settled (IssuedTick + 1) | E | Order assumption "one tick later": a command only counts as effective when world.Tick > IssuedTick + 1 (literal 1 tick); before that HasMoveTarget/IsAttackMoving is not evaluated | own | Stays correct as a sequence as long as commands are executed in the tick they are issued in; the waiting time halves in wall-clock terms (67 -> 33 ms). With a future command delay in ticks the assumption breaks. | 21,23 |

**`Pres\Objects/ConditionDeriver.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 10,165-174 | ConditionContext.PreviousConstructionWork / ConditionDeriver.Construction | F/tick | ACTIVELY_BEING_CONSTRUCTED = construction work (int) has risen compared with the capture of the previous simulation tick (comparison over exactly one tick; FrameProjector line 252-253 supplies previous) | own | Flickers if ConstructionWork no longer rises in every tick at 60 Hz (depends on the scaling of BuildTicks/work per tick in Phase 20); otherwise unchanged. | 21 |
| 156 | ConditionDeriver.Weapon FIRING (lastFire == world.Tick - 1) | F/tick | FIRING_A/B/C holds for exactly one simulation tick (shot in the most recently simulated tick); called from FrameProjector.CaptureEntity <- Advance <- LiveGame.SyncAfterTick, once per simulation tick | own (mapping after WeaponSet::getModelConditionForWeaponSlot according to the comment, not checked there) | FIRING lasts 16.7 instead of 33 ms; with two ticks per drawn frame the capture with FIRING can be overwritten before a frame sees it (shot animation/muzzle flash is lost). | 21 |

**`Pres\Objects/DrawSpecCompiler.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 73,80 | DrawSpecCompiler.ModuleKey (DrawSetting raw text) | D | Time-bearing draw keys are stored unconverted as text in DrawSpec.Settings (line 80 only checks the number): InitialRecoilSpeed, RecoilSettleSpeed, TreadAnimationRate, RecoilDamping, RotationDamping, MoveOutwardTime, MoveInwardTime, SinkTime, MaxIntensityLifetime, FadeLifetime | W3DModelDraw.cpp buildFieldParse (InitialRecoilSpeed/RecoilSettleSpeed parseVelocityReal, RecoilDamping parseReal); W3DTankDraw.cpp (TreadAnimationRate parseVelocityReal); W3DTruckDraw.cpp (RotationDamping parseReal); W3DTreeDraw.cpp and W3DLaserDraw.cpp (parseDurationUnsignedInt) | No effect here; every consumer must itself convert from per-second/milliseconds or from a per-frame factor. Currently only ObjectLayerCore.cs 388-393 reads (TireRotationMultiplier, Cab/Trailer, RotationDamping as a factor per update). | 21 |

**`Pres\Objects/FrameProjector.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 31 | FrameProjectorOptions.SnapDistance | E | Teleport threshold 100 world units of travel within ONE tick (no interpolation, no wheel travel, suspension velocity 0); 3 uses (Project l.222, CaptureEntity l.266, SuspensionTracker.Update l.86) | own | At 60 Hz the travel per tick halves, the threshold then corresponds to 6000 instead of 3000 units/s; fast jumps are more likely to be interpolated. | 21,23 |
| 51 | FrameProjector.MuzzleFlashTicks | F/tick | Muzzle flash duration = 4 ticks after the shot (comparison world.Tick - LastFireTickOf(slot) <= 4, l.276; uses world.Tick, not the projector tick) | own (comment: the original shows it in the first drawn recoil frame; not looked up) | Tick clock: at 60 Hz visible for only 0.067 s instead of 0.13 s. | 21 |
| 54 | FrameProjector.HealingIconTicks | F/tick | Healing icon duration = 3 * 30 ticks (copy of the rate as literal 30); 2 uses in l.319 (age <= 90 and start lock tick > 90); HealedAtTick is set with world.Tick (l.273), but compared against Tick = world.Tick-1 | GameEngine/Source/GameClient/Drawable.cpp: HEALING_ICON_DISPLAY_TIME = LOGICFRAMES_PER_SECOND * 3 (l.247) | Tick clock: at 60 Hz the icon disappears after 1.5 s instead of 3 s; the start lock halves as well. | 21 |
| 140-176 | FrameProjector.Advance / FrameProjector.Tick | F/tick | Clock of the projector: Tick = world.Tick - 1; Advance runs exactly once per simulation tick (LiveGame.SyncAfterTick l.203-212, loop in LiveGame.Advance with TickSeconds = 1.0/LogicFramesPerSecond); drives all trackers (flash, suspension, doors, steering) | own | Tick clock: all dependent states run twice as often at 60 Hz; Project(alpha) interpolates only position, yaw and turret angle. | 21 |
| 162 | FrameProjector.Advance (world.Tick % 150) | C | Clean-up of the tracker tables every 150 ticks (5 s) for entities that no longer exist | own | Load/memory only: at 60 Hz every 2.5 s; no visible result (expired flashes are already invisible). | 21 |
| 179 | FrameProjector.SceneryFogInterval | C | Visibility of explored scenery is re-read every 5 ticks (world.Tick % 5, l.187); 1 use | own | Result only in the display: darkening follows with at most 83 ms instead of 167 ms lag; otherwise load (twice as many vision queries per second). | 21 |

**`Pres\Objects/RenderFrame.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 77 | RenderFrame.Tick / RenderFrame.Alpha | F/tick | Frame carries the last simulated tick and the interpolation factor (Alpha = accumulator/TickSeconds from LiveGame.Advance l.197); consumers of RenderFrame.Tick outside the package not audited | own | Tick clock as carrier: Alpha stays 0..1; whoever reads RenderFrame.Tick as time runs twice as fast at 60 Hz. | 21 |

**`Pres\Objects/SelectionFlash.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 31 | SelectionFlashTracker.DecayTicks | F/tick | Decay duration of the selection/capture flash = 4 ticks; LastVisibleAge (l.34) is derived from it; uses: Envelope l.57, Prune l.115 | GameEngine/Source/GameClient/Drawable.cpp: Drawable::flashAsSelected, m_selectionFlashEnvelope->play( color, 0, 4 ) (l.1366) | Tick clock: at 60 Hz the flash lasts 5 ticks = 83 ms instead of 167 ms. | 21 |
| 53-59 | SelectionFlashTracker.Envelope | F/tick | Envelope as a step function of the age in ticks: literal '<= 1' (peak two ticks), then 3/4, 1/2, 1/4; age = projector tick - SimEvent.Tick (TintAt l.110), not interpolated | origin not verified (comment names TintEnvelope::update; only play(color,0,4) looked up) | Tick clock: steps half as long; hardly perceptible any more at 60 Hz. | 21 |
| 74 | SelectionFlashTracker.ScriptFlashInterval | F/tick | Script flash every 15 ticks (half a second at 30 Hz); uses: StartScripted l.85, Advance l.95 (tick % 15 on the global projector tick) | GameEngine/Include/GameClient/Drawable.h: DRAWABLE_FRAMES_PER_FLASH = LOGICFRAMES_PER_SECOND / 2 (l.279); Drawable.cpp l.1243 getFrame() % DRAWABLE_FRAMES_PER_FLASH | Tick clock: at 60 Hz it flashes every 0.25 s. | 21 |
| 85 | SelectionFlashTracker.StartScripted (count) | F/tick | Number of flashes = seconds * SimulationTiming.LogicFramesPerSecond / ScriptFlashInterval (integer division, 30/15 = 2 per second); mixes the rate constant with the fixed interval 15 | GameEngine/Source/GameLogic/ScriptEngine/ScriptActions.cpp l.2634: count = frames / DRAWABLE_FRAMES_PER_FLASH | If only the rate becomes 60 and the interval stays 15: 4 n flashes in n seconds (twice as many); if both scale, it stays at 2 n. | 21 |

**`Pres\Objects/StructureConditions.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 269-346 | StructureConditionTracker.Production / UpdateDoor | F/tick | Door clock of the production buildings on world.Tick: stamps Opened/WaitOpen/Closed/ConstructionComplete, compared with ProductionData.DoorOpeningTicks/DoorWaitOpenTicks/DoorCloseTicks/ConstructionCompleteTicks and head.RemainingTicks; literals '+ 1' (l.325, 334) and LastHold + 1 | own (port of ProductionUpdate::updateDoors according to the comment; not looked up) | Tick clock, follows the tick durations of the simulation: correct as soon as ProductionData is scaled; the '+1' offsets are then 1/60 s. | 21 |
| 285 | StructureConditionTracker.Production (Math.Max(lead, 1)) | B/literal | Door/construction completion starts when head.RemainingTicks <= max(DoorOpeningTicks, 1): minimum lead of 1 tick as a literal | own | Minimum lead 1/60 s instead of 1/30 s; practically without effect when there is no door animation. | 21 |

**`Pres\Objects/Suspension.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 21 | SuspensionMotion.LogicTicksPerSecond | F/tick | Private copy of the rate '= 30'; 1 use l.36: BounceAmount (degrees/s) * Degrees / 30 -> BounceKick in radians per tick, during the catalog build | GameEngine/Source/GameLogic/Object/Locomotor.cpp: field table 'BounceAmount', INI::parseAngularVelocityReal (l.469) | If the 30 stays, the kick per tick at 60 Hz is twice as large as the data value per second. | 21 |
| 33-38 | SuspensionMotion.FromLocomotor (PitchStiffness, RollStiffness, PitchDamping, RollDamping, ForwardAccelerationPitchFactor, LateralAccelerationRollFactor, UniformAxialDamping) | D | Spring/damping and acceleration factors read raw per logic frame (default 0.1 / 0.1 / 0.9 / 0.9 / 0 / 0 / 1), without conversion; applied per tick in Update | origin not verified (comment names LocomotorTemplate::LocomotorTemplate; default values not looked up) | Applied per tick: at 60 Hz the suspension oscillates and damps in half the time, the acceleration pitch becomes smaller. | 21,23 |
| 82-99 | SuspensionTracker.Update (velX/velY, accelX/accelY, yawStep, ForwardEpsilon, TurnEpsilon) | E | Velocity = position difference per tick, acceleration = difference of the tick velocities, yaw step per tick; thresholds ForwardEpsilon 1e-4 (units/tick) and TurnEpsilon 0.002 rad/tick | own (the original reads PhysicsBehavior values; here from position differences) | At 60 Hz the velocity per tick halves and the acceleration per tick quarters; slow turns below 0.002 rad/tick no longer steer the wheels. | 21,23 |
| 116-118 | SuspensionTracker.Update (airborne wheels) | E | Rear wheels in the air approach the target by half per tick: += (target - x) / 2 | origin not verified (comment: calcPhysicsXformWheels) | Halving per tick: at 60 Hz the springs extend twice as fast. | 21,23 |
| 126-136 | SuspensionTracker.Update (bounce kick, Hash(id, world.Tick)) | E | Ground bump: checked per tick (speed > top speed/10, rates below threshold), direction from Hash(entity, world.Tick) & 3; adds BounceKick*factor to the tick rates | GameEngine/Source/GameClient/Drawable.cpp ll.2012-2031 and ll.2307-2314 (BOUNCE_ANGLE_KICK = locomotor->getBounceKick()) | At 60 Hz twice as many checks and random draws per second; different bump sequence and frequency (display only). | 21,23 |
| 139-160 | SuspensionTracker.Update (spring/damper integration) | E | Euler step per tick: rate += -stiffness*(angle - ground) - damping*rate; PitchRate *= 0.5 when the nose rises; angle += rate * UniformAxialDamping; the same for the acceleration pitch; ChassisPose is not interpolated | GameEngine/Source/GameClient/Drawable.cpp (calcPhysicsXformTreads/Wheels according to the comment; only the BOUNCE/SPRING lines looked up) | Same coefficients twice as often: settling in half the time, different overshoot; multiplicative damping 0.5 per tick acts twice. | 21,23 |
| 179 | SuspensionTracker.UpdateWheels (WheelAngle smoothing / 10f) | E | Steering angle approaches by one tenth per tick: WheelAngle += (wanted - WheelAngle) / 10 | GameEngine/Source/GameClient/Drawable.cpp l.2124: m_wheelAngle += (new - old)/WHEEL_SMOOTHNESS | At 60 Hz the wheels steer in twice as fast. | 21,23 |
| 210-219 | SuspensionTracker.UpdateWheels.Follow | E | A dropping wheel follows only by half per tick (current + (target - current)/2), a rising one immediately | origin not verified (SPRING_FACTOR 0.9 in Drawable.cpp l.2126 verified, the halving not looked up) | Halving per tick: at 60 Hz the spring follows twice as fast. | 21,23 |

**`Pres\Objects/UnitConditionTracker.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 27 | UnitConditionTracker.CheerTicks | F/tick | Cheer duration = 3 * 30 ticks (copy of the rate as literal 30); use l.86 (CheerUntil = tick + CheerTicks), comparison l.134; no caller found in src for FrameProjector.Cheer | GameEngine/Source/GameLogic/AI/AIGroup.cpp l.2784: setSpecialModelConditionState( MODELCONDITION_SPECIAL_CHEERING, LOGICFRAMES_PER_SECOND * 3 ) | Tick clock: at 60 Hz only 1.5 s of cheering. | 21 |
| 30 | UnitConditionTracker.TensileFreefallLife | A | FREEFALL only while TensileFormationUpdate.Life < 100 (Life is a tick counter of the simulation); 1 use l.126 | GameEngine/Source/GameLogic/Object/Update/TensileFormationUpdate.cpp l.357: m_life < 100 (l.260: ++m_life > 300) | If the simulation keeps counting Life per tick, the window ends at 60 Hz after 1.67 s instead of 3.3 s. | 20,21 |
| 33,268-280 | UnitConditionTracker.TurnOf / TurnTolerance | E | Turn direction is detected when the heading change of ONE tick is exactly Locomotor.TurnRatePerTick (tolerance 8 raw units of Fixed); controls CENTER_TO_LEFT/RIGHT | own (replaces PhysicsBehavior::getTurning according to the comment) | Correct as long as the simulation turns exactly the scaled TurnRatePerTick per tick; with substeps or different rounding the steering animation fails. | 21,23 |
| 34,126-128 | UnitConditionTracker.TensileDrop | E | FREEFALL requires \|Z - Z of the previous tick\| > 0.2 world units: threshold against the height step of ONE tick | GameEngine/Source/GameLogic/Object/Update/TensileFormationUpdate.cpp l.357: fabs( pos->z - newPos.z ) > 0.2f | At 60 Hz the fall step per tick is half as large; slowly falling rocks no longer show FREEFALL. | 21,23 |
| 230-262 | UnitConditionTracker.StepSteering (NextSteerFrame) | F/tick | Minimum hold time per steering state: NextSteerFrame = now + AnimationSteeringUpdate.MinTransitionTicks (tick duration of the simulation), now = world.Tick - 1 | own (port of AnimationSteeringUpdate::update according to the comment; not looked up) | Tick clock, follows the tick duration of the module: correct as soon as MinTransitionTicks is scaled. | 21 |

**`Pres\Terrain/WaterSurfaces.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 26 | WaterAppearance.RiverVScrollPerSecond | F/originalbild | River scrolling 0.002f * 30f per second (original value per 30 Hz frame times a fixed 30); 1 use l.187 as UvScrollPerSecond; consumer WaterBatch.ApplyScroll(frame.TotalSeconds) = wall-clock time (TerrainLayer l.180) | GameEngineDevice/Source/W3DDevice/GameClient/Water/W3DWater.cpp l.1225: m_riverVOrigin += 0.002f | Wall-clock time: unchanged; the 30 is the authoring rate and must not be replaced by the new tick rate. | 21 |

### 6.3 `CnCNew.Ui`

**`Ui\Text/GlobalLanguageSettings.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 80,105 | GlobalLanguageSettings.MilitaryCaptionSpeed (captionSpeed = 0) | D | Frame count between two typed characters of the military caption, raw as an integer from Language.ini (fallback 0). Consumer: Game/Shell/MissionText.MilitaryCaptionState (`_increment = frame + _speed`), driven by MissionPresentation.Clock = World.Tick + frozenSteps. | GlobalLanguage.cpp GlobalLanguage::GlobalLanguage: m_militaryCaptionSpeed = 0 | Typing speed doubles with the 60 Hz tick (value stays a frame count). | 21 |

**`Ui\Text/InGameUiSettings.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 67 | InGameUiSettings.LogicFramesPerSecond | F/tick | Own copy of the rate `= 30` (not SimulationTiming). 4 uses in the file (217, 221, 225, 253) and 4 in src/CnCNew.Game/Shell/MissionText.cs (56, 136, 137, 326). Consumers run on the sim tick (Match.Tick, MissionPresentation.Clock = World.Tick + frozenSteps). | InGameUI.h DEFAULT_FLOATING_TEXT_TIMEOUT = LOGICFRAMES_PER_SECOND/3; InGameUI.cpp InGameUI::update (m_messageDelayMS / LOGICFRAMES_PER_SECOND / 1000) | If it stays 30 while the driving sim tick has 60 Hz: all frame counts computed from it (blink period, caption duration, seconds display of the counters) run twice as fast or show doubled seconds. | 21 |
| 216-217 | InGameUiSettings.FromDefinition.Frames (SuperweaponCountdownFlashDuration, NamedTimerCountdownFlashDuration) | F/tick | Own conversion ms → frames `* LogicFramesPerSecond / 1000m`, unrounded (decimal), when loading the definition graph. Consumers: Game/Hud/SuperweaponTimers.Draw (tick = Match.Tick, truncated with `(int)`, `tick / flashFrames % 2`) and Game/Shell/MissionText.NamedTimers (frame = MissionPresentation.Clock). | InGameUI.cpp s_fieldParseTable: INI::parseDurationReal for both keys | Blink period stays as a frame count of the 30 Hz grid and is computed against the 60 Hz tick: blinking twice as fast; rounding: none here, truncation at the consumer. | 21 |
| 220-221 | InGameUiSettings.FromDefinition.WholeFrames (FloatingTextTimeOut) | F/tick | Own conversion ms → whole frames `Math.Ceiling(ms * LogicFramesPerSecond / 1000m)`, when loading. The result FloatingTextTimeOutFrames has no consumer in src/ (grep over src/: declaration only). | InGameUI.cpp s_fieldParseTable: INI::parseDurationUnsignedInt for FloatingTextTimeOut; consumption in the original InGameUI.cpp (m_frameTimeOut = TheGameLogic->getFrame() + …) | Currently without effect (no consumer); as soon as floating text is built, the duration would be halved with a 60 Hz tick. Rounding: rounded up to whole frames. | 21 |
| 224-225 | InGameUiSettings.FromDefinition.PerFrame (FloatingTextMoveUpSpeed, FloatingTextVanishRate) | F/tick | Own conversion units/second → units/frame `/ LogicFramesPerSecond`, when loading. FloatingTextMoveUpPerFrame and FloatingTextVanishPerFrame have no consumer in src/. | InGameUI.cpp s_fieldParseTable: INI::parseVelocityReal for both keys; consumption in the original InGameUI.cpp (pos.y -= m_frameCount * m_floatingTextMoveUpSpeed; (currLogicFrame - m_frameTimeOut) * m_floatingTextMoveVanishRate) | Currently without effect; with later consumption per 60 Hz tick, doubled rise and fade-out speed. | 21 |
| 240 | InGameUiSettings.MilitaryCaptionSpeed: Integer("MilitaryCaptionSpeed", 1) | D | Frame count between two typed characters, read raw as an integer (fallback 1). Not consumed in src/: the hosts pass GlobalLanguageSettings.MilitaryCaptionSpeed (MissionPresentation.cs 921, 988). | InGameUI.cpp s_fieldParseTable (MilitaryCaptionSpeed) and InGameUI::InGameUI: m_militaryCaptionSpeed = 1 | Currently without effect (not consumed). | 21 |
| 242 | InGameUiSettings.FromDefinition: Frames("SuperweaponCountdownFlashDuration", 1m) | B/fallback | Fallback value 1 frame (already in frames, not ms) for the blink duration of the superweapon display; according to the comment in line 227 the retail file sets the key, the fallback does not apply there. | InGameUI.cpp InGameUI::InGameUI: m_superweaponFlashDuration = 1.0f | Fallback would stay 1 tick = 1/60 s instead of 1/30 s; no rounding. | 21 |
| 247 | InGameUiSettings.FromDefinition: Frames("NamedTimerCountdownFlashDuration", 1m) | B/fallback | Fallback value 1 frame (in frames) for the blink duration of the named counters; the retail file sets the key according to the comment in line 227. | InGameUI.cpp InGameUI::InGameUI: m_namedTimerFlashDuration = 1.0f | Fallback would stay 1 tick = 1/60 s instead of 1/30 s; no rounding. | 21 |
| 253 | InGameUiSettings.FromDefinition: WholeFrames("FloatingTextTimeOut", LogicFramesPerSecond / 3) | B/fallback | Copy `/ 3`: fallback value 30/3 = 10 frames (integer) for the floating text duration; no consumer in src/. | InGameUI.h DEFAULT_FLOATING_TEXT_TIMEOUT = LOGICFRAMES_PER_SECOND/3; InGameUI.cpp InGameUI::InGameUI: m_floatingTextTimeOut = DEFAULT_FLOATING_TEXT_TIMEOUT | Without effect as long as there is no consumer; otherwise 10 ticks = 1/6 s instead of 1/3 s. Integer division: at 60 → 20, at 120 → 40 (without remainder). | 21 |
| 254 | InGameUiSettings.FromDefinition: PerFrame("FloatingTextMoveUpSpeed", 1m) | D | Fallback value 1.0 units per frame (raw, without conversion, unlike the data value, which is divided by 30); no consumer in src/. | InGameUI.cpp InGameUI::InGameUI: m_floatingTextMoveUpSpeed = 1.0f | Without effect as long as there is no consumer; otherwise doubled rise speed per second. | 21 |
| 255 | InGameUiSettings.FromDefinition: PerFrame("FloatingTextVanishRate", 0.1m) | D | Fallback value 0.1 per frame (raw) for the fade-out rate of the floating text; no consumer in src/. | InGameUI.cpp InGameUI::InGameUI: m_floatingTextMoveVanishRate = 0.1f | Without effect as long as there is no consumer; otherwise fading out twice as fast. | 21 |

**`Ui\Transitions/TransitionEffects.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 87 | FlashEffect.End | F/originalbild | FLASH lasts 8 transition frames; plus alpha tables per frame (BorderAlpha, FillAlpha, lines 88–89) and fixed frame numbers 1–7 in the switch. | GameWindowTransitions.h FLASHTRANSITION_* (…FADE_TO_BACKGROUND_4 = 7 found; END line without a numeric value not seen) | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 146 | ButtonFlashEffect.End | F/originalbild | BUTTONFLASH lasts 17 transition frames; fixed frame numbers 1–7 and 8–10 (waiting) in the switch. | GameWindowTransitions.h BUTTONFLASHTRANSITION_* (values 0–7 and 11–16 found; END line not seen) | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 147 | ButtonFlashEffect.GradeIn1..GradeOut4 | F/originalbild | Frame numbers 11–16 of the gradient part of BUTTONFLASH (at the same time draw states). | GameWindowTransitions.h BUTTONFLASHTRANSITION_FADE_TO_GRADE_IN_1 = 11 … FADE_TO_GRADE_OUT_4 | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 293 | FadeEffect.End | F/originalbild | WINFADE lasts 10 transition frames; alpha `25 * DrawState` per frame (line 328). | GameWindowTransitions.h FADETRANSITION_FADE_IN_1..9 (9 found; END line not seen) | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 341 | ScaleUpEffect.End | F/originalbild | WINSCALEUP/SCORESCALEUP last 6 transition frames; size step = window size / End per frame (lines 351–352). | origin not verified | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 397 | MainMenuScaleUpEffect.End | F/originalbild | MAINMENUSCALEUP lasts 5 transition frames; position and size step = difference / End per frame (lines 415–418). | GameWindowTransitions.h MAINMENUSCALEUPTRANSITION_END = 5 | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 468 | FactionScaleEffect.End (medium ? 3 : 6) | F/originalbild | MAINMENUMEDIUMSCALEUP lasts 3, MAINMENUSMALLSCALEDOWN 6 transition frames; step = size difference / End. No pattern hit (expression instead of const). | GameWindowTransitions.h MAINMENUMEDIUMSCALEUPTRANSITION_END = 3; the value 6 of the small variant: origin not verified | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 543 | ScreenFadeEffect.End | F/originalbild | Copy `End = 30`: SCREENFADE lasts 30 transition frames (= 1 s at 30 frames/s); alpha per frame `Percent = 1 / (End - 1)` (line 544). The 30 is a frame count of the original, not the tick rate. | GameWindowTransitions.h SCREENFADETRANSITION_END = 30 | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 572 | FullFadeEffect.End | F/originalbild | FULLFADE lasts 10 transition frames (5 up, 5 down); `Percent = 1 / (End / 2)` (line 573). | GameWindowTransitions.h FULLFADETRANSITION_END = 10 | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 616 | TextOnFrameEffect.End | F/originalbild | TEXTONFRAME lasts 1 transition frame. | GameWindowTransitions.h TEXTONFRAMETRANSITION_END = 1 | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 669 | ReverseSoundEffect.End | F/originalbild | REVERSESOUND lasts 2 transition frames; sound GUITransitionFade on frame 1. | GameWindowTransitions.h REVERSESOUNDTRANSITION_END = 2 | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 711 | CountUpEffect.End | F/originalbild | Copy `End = 30`: COUNTUP at most 30 transition frames, one counting step and one sound GUIScoreScreenTick per frame; End also serves as the threshold for the step size 1/100/1000 (lines 731–733). | GameWindowTransitions.h COUNTUPTRANSITION_END = 30 | No change from the sim rate; if End were scaled along, the step size and the number of sounds of the score display would change – do not scale with the tick rate. | 21 |
| 816 | TypeTextEffect.End | F/originalbild | Copy `End = 30`: TYPETEXT types one character and one sound GUITypeText per transition frame, at most 30 frames (FrameLength = min(text length, End)). | GameWindowTransitions.h TEXTTYPETRANSITION_END = 30 | No change from the sim rate; if End were scaled along, the number of typed characters per pass would change – do not scale with the tick rate. | 21 |
| 883 | ControlBarArrowEffect.BeginFade | F/originalbild | CONTROLBARARROW: 16 transition frames of downward movement (`Percent = 1 / BeginFade`, step per frame), then fade-out. | GameWindowTransitions.h CONTROLBARARROWTRANSITION_BEGIN_FADE = 16 | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |
| 884 | ControlBarArrowEffect.End | F/originalbild | CONTROLBARARROW lasts 22 transition frames (6 frames of fade-out, `FadePercent = 1 / (End - BeginFade)`). | GameWindowTransitions.h CONTROLBARARROWTRANSITION_END = 22 | No change from the sim rate: runs via WindowTransitionHandler.FramesPerSecond on wall-clock time; must not be scaled with the tick rate. | 21 |

**`Ui\Transitions/WindowTransitionHandler.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 130 | WindowTransitionHandler.FramesPerSecond | F/originalbild | Own copy `= 30`: transition frames per second of the shell animations. 1 use (line 285 `_carry += seconds * FramesPerSecond`). Clock: Advance(seconds) ← ShellScreen.Update(min(dt, 0.1 s)) ← GameShell.Update(dtSeconds) ← ClientRuntime.cs 234 `_shell.Update(seconds)` (client frame time); UiRenderTool calls with fixed steps (1.0 / 30). | origin not verified (comment names GameData.ini UseFPSLimit/FramesPerSecondLimit = 30; data file not audited) | No change: does not depend on the sim tick but on wall-clock time with a fixed 1/30 s unit; must not be scaled with the tick rate (otherwise the menu transitions run twice as fast). | 21 |
| 303 | WindowTransitionHandler.Settle(limit = 600) | F/originalbild | Loop limit 600 transition frames (= 20 s at 30 frames/s) for settling; no caller found in src/ and tools/ (grep `.Settle(`), probably tests only. | own | No change from the sim rate (transition frames, not ticks). | 21 |

**`Ui\Transitions/WindowTransitionTable.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 31,173-176 | TransitionWindowSpec.FrameDelay (key FrameDelay in WindowTransitions.ini) | D | Start delay of a window effect in frames, raw as an integer from the data, consumed in TransitionWindow.Update/TotalFrames against the group frame counter (transition frames of 1/30 s wall-clock time). | origin not verified (comment names GameWindowTransitionsHandler parseWindow; .cpp not opened) | No change from the sim rate; the unit is the transition frame (1/30 s wall-clock time), not the tick. | 21 |

### 6.4 `CnCNew.Client.Stride`

**`Client\ClientRuntime.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 120,141,142,375,378,379 | ClientRuntime.FinishMission / RunningGame.MissionSteps / _frame | H/ticks | Mission report: frames, world tick, session steps (counts TickStepped), presentation latency in ticks; the comment expects steps == world tick; frames == ticks holds only with 1 tick per frame | own | At 60 Hz and a step of 1/30 s, world tick = 2 × frames and the latency is printed as a doubled tick count; check scripts that compare frames with ticks fail. | 21 |
| 192 | ClientRuntime.Update: _clockMs += (long)Math.Round(seconds * 1000) | H/schritt | Millisecond clock for input events, cursor animation and minimap cadence; in the smoke test 33 ms per frame (1/30 s rounded), i.e. 1 % behind game time; otherwise wall-clock time | own | Independent of the tick rate; changes only with the smoke test step (1/60 s would give 17 ms, 2 % too fast). | 21 |

**`Client\Layers/Fx/FxDemo.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 14,26,30,61,66 | FxDemo.PeriodFrames = 45 | F/tick | Diagnostic ring (CNCNEW_FX_DEMO=ring): new effects every 45 Fx frames; match.Fx.Frame is the ParticleWorld counter, which runs once per simulation tick (LiveGame.SyncAfterTick calls Fx.Update) | own | Tick-driven: the ring appears every 0.75 s instead of 1.5 s as long as Fx.Update runs per tick. | 21 |
| 103 | FxDemo.Spawn: FxInvocation.PrimarySpeed = 6f | D | Tracer speed of the diagnostic ring as distance per Fx frame (FxInvocation.PrimarySpeed: "Distance per frame"), literal 6 | own | Tracers of the demo ring fly twice as fast if the Fx frame runs with the tick rate. | 21 |

**`Client\Layers/Objects/ObjectLayerCore.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 45,46,57-63,121,122 | ObjectLayerCore.Attach/Update: _frameLimit from --frames, _frameCounter | H/budget | The object layer reads --frames a second time from the command line and prints its smoke test summary after that many rendered frames | own | Unchanged (frames, not ticks); must follow if --frames means ticks or seconds in future. | 21 |
| 369,372,393 | ObjectLayerCore.ApplyWheels: Number("RotationDamping", 1f) | D | Draw module field RotationDamping read raw (fraction per original frame of 1/30 s, default 1 = immediately); consumption in line 401 | GameEngineDevice/Source/W3DDevice/GameClient/Drawable/Draw/W3DTruckDraw.cpp, W3DTruckDraw field table "RotationDamping" (INI::parseReal, m_rotationDampingFactor) | Unchanged, because the conversion uses the fixed 1/30 s unit; only wrong if someone replaces the 30 by the tick rate. | 21 |
| 400-403 | ObjectLayerCore.ApplyWheels: blend = 1 - Pow(1 - RotationDamping, deltaSeconds * 30.0) | F/originalbild | Cab/trailer damping of W3DTruckDraw: fraction per 1/30 s (original frame), converted to the frame time; literal 30.0 | GameEngineDevice/Source/W3DDevice/GameClient/Drawable/Draw/W3DTruckDraw.cpp, W3DTruckDraw::doDrawModule (deltaAngle *= m_rotationDampingFactor per drawn frame) | Already runs on frame/wall-clock time; the 30 is the authoring unit and must NOT be scaled with the tick rate. | 21 |

**`Client\Program.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 40,92,365 | Program top-level/CnCNewGame.Update: --frames default "90", smokeFrames, LiveOptions.Frames | H/budget | Run budget of the smoke test in rendered frames (default 90 = 3 s = 90 ticks at 30 Hz); parsed twice; ends at ++_renderedFrames >= smokeFrames | own | The game time stays (frames × 1/30 s), the tick count reached doubles; calls such as --frames 1500 that mean a tick count hit different ticks. | 21 |
| 93,151,388,389 | Program top-level/CnCNewGame: --every default 30, filmstripEvery = 30 | H/zeit | Filmstrip: one PNG every N rendered frames, default 30 = 1 s at the fixed step of 1/30 s (two sites: CLI default "30" and constructor default 30) | own | Stays 1 s as long as the step stays 1/30 s; if the step is coupled to the tick rate, the image spacing halves. | 21 |
| 121,357 | CnCNewGame.Update: fixedSeconds = smokeTest ? 1.0 / 30 : null | H/schritt | Smoke test fixed step 1.0/30 s per frame to ClientRuntime.Update (replaces the wall-clock time gameTime.Elapsed); at 30 Hz exactly 1 tick per frame, because LiveGame.TickSeconds = 1/30 | own | If the step stays 1/30 s, 2 ticks run per frame (exactly, 1.0/60 is in binary half of 1.0/30); a frame is then no longer a tick, tick numbers in reports and captures shift. | 21 |
| 397 | CnCNewGame.Draw: capture at _renderedFrames >= liveOptions.Frames ?? (shellMode ? smokeFrames : 12) | H/budget | Time of the --capture in rendered frames (match/mission: --frames; shell: smokeFrames; model viewer: 12) | own | Same point in game time, but a different world tick and a different tick sequence before it; capture checksums (golden) can change. | 21 |

**`Client\Testing/InputScript.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 18,58,76,165-167 | InputScript.Step.Frame / EventsFor(frame, timeMs) | H/zeit | Script format "FRAME VERB ARGUMENTE": frames count the updates of ClientRuntime from 1 (only in the smoke test, fixed step 1/30 s); at 30 Hz also the tick number | own | Points in time stay (frame × 1/30 s), but a frame is no longer a tick; scripts whose frames are tuned to tick counts (build times) stay correct only as long as the step stays 1/30 s. | 21 |
| 48,254,261 | InputScript._frame, Log "frame N tick M", Report "at exit tick" | H/ausgabe | Log prints frame and world tick raw (without conversion to seconds) | own | Tick numbers in the log double; regression checks that read tick values from the log need new expectations. | 21 |
| 88 | InputScript.Load: click → up at frame + 2 | H/zeit | Click: release two frames (about 66 ms at the fixed step) after the press on the same pixel | own | Stays 2 frames; 4 instead of 2 ticks then lie in between (input is frame-clocked, effect only via the tick sequence of the commands). | 21 |

### 6.5 `CnCNew.Data` (fields that are typed only as a number)

**`Data\Definitions/DefinitionPrimitives.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 38 | ColorKeyframeValue.Frame (ValueGrammar.ParseColorForm, lines 195-196) | D | The form `R:n G:n B:n <zahl>` is typed as a colour keyframe with a raw frame count (long) and written like that into the definition snapshot (DefinitionSnapshotWriter line 108); no time unit | origin not verified (Presentation names ParticleSystemTemplate::parseRGBColorKeyframe; not looked up in references) | The number stays a frame count of the 30 Hz authoring; a consumer must interpret it as a 1/30 s unit, not as a tick. | 21 |

**`Data\Registry/ModuleCatalog.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 865 | HelicopterSlowDeathBehavior: SpiralOrbitForwardSpeedDamping | D | Schema FieldType.Real, raw number without unit; in the original parseReal and multiplicative per update on the forward speed (`m_forwardSpeed *= damping`) | HelicopterSlowDeathUpdate.cpp buildFieldParse (INI::parseReal, line 114) and update (line 379) | Factor per logic frame: at 60 Hz it would be damped twice as often if the simulation applies it per tick (check consumption in CnCNew.Simulation). | 20,23 |
| 968 | StructureToppleUpdate: StructuralDecay | D | Schema FieldType.Real, raw number; in the original parseReal and multiplicative on the remaining integrity (`m_structuralIntegrity *= m_structuralDecay`) | StructureToppleUpdate.cpp buildFieldParse (INI::parseReal, line 125) and use in line 250 | Decay factor per application: more frequent application at 60 Hz speeds up the decay if the simulation applies it per tick (check there). | 20,23 |
| 1024 | JetSlowDeathBehavior: RollRate (and PitchRate, line 1026) | D | Schema FieldType.Real, raw number; in the original parseReal (not parseAngularVelocityReal), i.e. no conversion per second; RollRate is multiplied by RollRateDelta per update | JetSlowDeathBehavior.cpp buildFieldParse (INI::parseReal, lines 114/116) and `m_rollRate *= m_rollRateDelta` (line 248) | Probably angle per logic frame: at 60 Hz doubled turn rate if applied per tick (check consumption in the simulation; not a hit location, found additionally). | 20,23 |
| 1068 | ParachuteContain: LowAltitudeDamping | D | Schema FieldType.Real, raw number; in the original parseReal (default 0.2) as a damping factor in the parachute update (formula there not read) | ParachuteContain.cpp buildFieldParse (INI::parseReal, line 82), use in line 391 | Damping per logic frame: at 60 Hz different sway damping if applied per tick (check consumption in the simulation). | 20,23 |
| 1201 | MobMemberSlavedUpdate: CatchUpCrisisBailTime | D | Schema FieldType.Integer (Min 0), raw number; in the original parseUnsignedInt (no duration conversion) and comparison with a counter m_catchUpCrisisTimer (increment not read) | MobMemberSlavedUpdate.h buildFieldParse (INI::parseUnsignedInt, line 72); MobMemberSlavedUpdate.cpp lines 259/275 | Probably a number of updates/frames: at 60 Hz half the real time if compared as ticks (check in the simulation; found additionally). | 20 |
| 1354 | NeutronMissileUpdate: ForwardDamping | D | Schema FieldType.Real, raw number; in the original parseReal (default 0), read as `damping` in the flight phase (formula there not read) | NeutronMissileUpdate.cpp buildFieldParse (INI::parseReal, line 83), use in line 371 | Damping per logic frame: at 60 Hz a different flight profile if applied per tick (check consumption in the simulation). | 20,23 |
| 1587 | StructureCollapseUpdate: CollapseDamping | D | Schema FieldType.Real, raw number; in the original parseReal and per update `m_collapseVelocity -= gravity * (1 - damping)` | StructureCollapseUpdate.cpp buildFieldParse (INI::parseReal, line 119) and update (line 214) | Enters an Euler step per logic frame: at 60 Hz a different collapse progression if unchanged per tick (check consumption in the simulation). | 20,23 |
| 1589 | StructureCollapseUpdate: BigBurstFrequency | D | Schema FieldType.Integer, raw number; in the original parseInt and random draw `GameLogicRandomValue(1, N) == 1` in the update (1-in-N per check) | StructureCollapseUpdate.cpp buildFieldParse (INI::parseInt, line 121) and line 228 | Probability per check: more frequent checking at 60 Hz changes the number and timing of the random draws (check in the simulation; found additionally). | 20,23 |
| 1628-1630 | RailroadBehavior: RunningGarrisonSpeedMax/KillSpeedMin/SpeedMax | D | Schema FieldType.Real, raw numbers; in the original parseReal (not parseVelocityReal), i.e. speed per logic frame; comparison/limiting of m_pullInfo.speed | RailroadGuideAIUpdate.h RailroadBehaviorModuleData::buildFieldParse (INI::parseReal, lines 66-67); RailroadGuideAIUpdate.cpp lines 293, 347, 375, 753-759 | Distance per frame: at 60 Hz a train twice as fast and shifted thresholds if unchanged per tick (check in the simulation; found additionally). | 20 |
| 1631 | RailroadBehavior: Acceleration | D | Schema FieldType.Real, raw number; in the original parseReal and multiplicative `speed *= m_acceleration` per update | RailroadGuideAIUpdate.h buildFieldParse (INI::parseReal, line 68); RailroadGuideAIUpdate.cpp line 752 | Factor per logic frame: at 60 Hz the train accelerates twice as often if applied per tick (check in the simulation; found additionally). | 20,23 |
| 1632 | RailroadBehavior: Braking | D | Schema FieldType.Real, raw number; in the original parseReal and multiplicative `speed *= m_braking` per update | RailroadGuideAIUpdate.h buildFieldParse (INI::parseReal, line 69); RailroadGuideAIUpdate.cpp line 709 | Factor per logic frame: at 60 Hz the train brakes twice as often if applied per tick (check in the simulation). | 20,23 |
| 1637 | RailroadBehavior: Friction | D | Schema FieldType.Real, raw number; in the original parseReal and multiplicative `speed *= m_friction` per update | RailroadGuideAIUpdate.h buildFieldParse (INI::parseReal, line 74); RailroadGuideAIUpdate.cpp line 789 | Factor per logic frame: at 60 Hz friction twice as strong if applied per tick (check in the simulation). | 20,23 |

**`Data\World/MapScriptCatalog.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 60 | MapScriptCatalog: action types UNIT_/TEAM_GUARD_FOR_FRAMECOUNT, UNIT_/TEAM_IDLE_FOR_FRAMECOUNT, TEAM_SPIN_FOR_FRAMECOUNT (line 68) | D | The catalog lists script actions whose parameters are raw frame numbers from the map file; Data/Formats pass the integer through unchanged (not a hit location, found in addition; only the name lines read) | Origin not verified (names extracted from Scripts.h according to the file comment; not looked up there) | Map data in 30 Hz frames: half the duration at 60 Hz if the script VM counts them as ticks (check in ScriptEngine). | 20 |

`CnCNew.Audio`, `CnCNew.W3D`, `Archives`, `Content`, `Formats`, `Images`, `Import`, `IO`: no coupled site (Appendix A).

## 7. Inventory tools and tests

### 7.1 Tools (`tools/`)

**`tools/CnCNew.CampaignTool/Program.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 187 | Drive --ticks (default 60000) | H/budget | Run length of the objective driver: --ticks, default 60,000 ticks (= 33.3 min); according to the docs MD_CHI04 wins at tick ~48,000 | own | Budget = 16.7 min of game time: long missions (MD_CHI04) no longer reach the victory, 24/24 falls. | 21 |
| 196,488-491 | Drive --timeline N / Timeline: world.Tick % every | H/zeit | Timeline interval in ticks from the caller (default 0 = off) | own | The same N yields twice as dense lines per game time; no change of results. | 21 |
| 278 | Audit --ticks (default 1800) | H/budget | Run length of audit: --ticks, default 1800 ticks (= 60 s); the documented call uses --ticks 900 | own | Half the game time per mission: fewer executed scripts/actions, coverage numbers not comparable. | 21 |
| 314 | Audit: VerifyContinuation(..., Math.Min(ticks, 900)) | H/budget | Save/load continuation over at most 900 ticks (= 30 s), save point at the half (MissionScenario.cs:64) | own | Save point after 7.5 s instead of 15 s of game time; covers fewer running script states. | 21 |
| 320-321 | Audit: MissionProbe.Run (settleTicks 150, waitTicks 600) | H/budget | Elimination end probes with the default values settleTicks = 150 (5 s) and waitTicks = 600 (20 s), declared in src/CnCNew.Simulation/Testing/MissionScenario.cs:94 | own | Scripts with a time delay (timers in seconds) no longer fire in the wait window; end probes 13/24 and 15/24 can drop. | 21 |
| 375 | Playthrough --ticks (default 900) | H/budget | Run length per mission in playthrough: --ticks, default 900 ticks (= 30 s) until the forced end | own | Only 15 s of game time for the victory script after the elimination: "no outcome" becomes more likely. | 21 |
| 386 | Playthrough: session.World.Tick < 150 | H/zeit | 150 ticks (= 5 s) lead-in before saving and before the opponent is eliminated | own | Lead-in only 2.5 s: savegame and elimination hit an earlier mission state (intro scripts are still running). | 21 |
| 517 | WatchEntities: world.Tick % 150 < 2 | H/zeit | Throttle of the diagnostic line "nearest foes": only in 2 of 150 ticks (every 5 s) | own | Diagnostic line every 2.5 s instead of 5 s; output only. | 21 |

**`tools/CnCNew.PresentationTool/ScenarioAudit.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 32 | ScenarioAudit.Run --every (default 30) | H/zeit | Sampling interval of the printed table: --every, default 30 ticks (= 1 s); used in live.Tick % every (ll. 60, 61) | own | Table row every 0.5 s instead of every second; output only. | 21 |
| 33 | ScenarioAudit.Run --ticks (default scenario.Ticks) | H/budget | Run length: --ticks, default DemoScenario.Ticks of the chosen scene (declared in src/CnCNew.Game/Live, another package) | own | Depends on the scene definition: if its tick number stays, the scene runs only half the game time and checks (landing, capture) fail. | 21 |
| 55 | ScenarioAudit.Run live.Advance(1.0 / 30 + 1e-6) | H/schritt | Fixed step 1/30 s (+1e-6) per loop iteration, intended as exactly one tick; after it exactly one sample (history) per iteration | own | LiveGame.Advance then makes two ticks per call: only every second tick is sampled, all "in every tick" checks (hold ticks, gaps, flash in the following frame) count wrongly. | 21 |
| 111 | Capture: ToFloat(entity.Speed) * 30f | H/ausgabe | Speed per tick converted with the literal 30 into units per second; feeds the table and the thresholds 40/2/5/55/51/52 per second in CheckJet/CheckAirfieldGeometry | own | Speeds appear half as large: "flying" becomes "hovering", the minimum speed 55/s counts as undershot, jet-strafe fails. | 21 |
| 207 | CheckDuelTracking: sample.Tick > 5 | H/zeit | Window begins after tick 5, the command tick of the scene tank-duel (also in the text l. 177 and comment l. 206); the value mirrors the scene definition | own | Right only as long as the scene keeps giving the command at tick 5; with scaled scene ticks (10) the window begins too early. | 21 |
| 216 | CheckDuelTracking: aligned > 5 + 20 | H/zeit | The turret must be aligned at the latest 20 ticks (= 0.67 s) after the command at tick 5 | own | Time limit halves to 0.33 s; the turret needs twice as long in ticks, the check fails. | 21 |
| 234-241 | CheckDuelTracking: rate = TurnRate * 180/π * 0.5f; expectedRest | H/ticks | Expected rest tick = first movement + ceil(angle / (half the turn rate per tick)) + 2; computes with the turn rate per tick from the template and 2 ticks of tolerance | own | Stays consistent if TurnRate per tick is scaled; the tolerance of 2 ticks is then only 1/30 s. | 21 |
| 238-239 | CheckDuelTracking: death + recenter + 2 / + 3 | H/ticks | Turning back at the earliest at death tick + RecenterTicks + 2, at the latest + 3: offset of 2–3 ticks from the step order (sample after the step), RecenterTicks comes from the data | own | The offset +2/+3 stays right in ticks if every tick is sampled; with the 1/30 step from l. 55 the sample no longer hits the window reliably. | 21 |
| 265 | CheckSentinelScan: series.Length < 600 | H/zeit | At least 600 samples (= 600 ticks = 20 s) of the sentinel required | own | 600 samples are only 10 s of game time; together with l. 287 too few scans in the window. | 21 |
| 287 | CheckSentinelScan: complete.Length < 4 | H/zeit | At least 4 complete idle scans in the run; depends on the run length in ticks against the scan interval in game time | own | With an unchanged scene length in ticks only half the game time: fewer than 4 scans possible, the check fails. | 21 |
| 291 | CheckSentinelScan: holds < RecenterTicks \|\| > RecenterTicks + 2 | H/ticks | Hold duration at the scan angle in samples against RecenterTicks (from data) with 2 ticks of tolerance; presupposes one sample per tick | own | With one sample per two ticks (l. 55) the counted hold duration is half as large as RecenterTicks: the check fails. | 21 |
| 292-294 | CheckSentinelScan: gaps against MinScanIntervalTicks/MaxScanIntervalTicks | H/ticks | Pause between two scans in samples (index difference) against the scan interval in ticks from the data | own | Same error as l. 291 if not every tick is sampled; otherwise consistent. | 21 |
| 301-303 | CheckSentinelScan: Off(i) / Off(i - 1) | H/ticks | The drawn turret may correspond to the simulated angle of this or the previous sample ("one tick later" of the presentation), tolerance 0.6 degrees | own | At 60 Hz the angle step per tick is half as large (uncritical); with two ticks per sample the drawn angle lies between the samples and deviates. | 21 |
| 392 | CheckCaptureFlash: sample.Tick <= tick + 1 + SelectionFlashTracker.LastVisibleAge | H/ticks | Tint allowed until flash tick + 1 + LastVisibleAge (= DecayTicks, constant in src/CnCNew.Presentation/Objects/SelectionFlash.cs:34, in ticks) | own | Follows the constant; if DecayTicks stays a tick number, the flash is visible half as long at 60 Hz (a matter of the presentation package). | 21 |
| 396 | CheckCaptureFlash: gaps b > a + 1 | H/ticks | Flash gaps must shrink, 1 tick of jitter between neighbours is allowed (the phase is switched in whole ticks) | own | Jitter stays 1 tick, gaps double in ticks; the check stays valid if the flash phase scales with the rate. | 21 |
| 433 | CheckJet: hover > 10 | H/zeit | Jet may stand motionless in the air for at most 10 ticks (= 0.33 s) (samples with Speed < 5/s) | own | Tolerance only 0.17 s; together with the wrong conversion in l. 111 almost every flight tick counts as standstill. | 21 |

**`tools/CnCNew.SimTool/Program.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 88 | UnitAudit --max-ticks (default 9000) | H/budget | Run length of unit-audit: option --max-ticks, default 9000 ticks (= 5 min) | own | Run covers only 2.5 min of game time; the AI gets less far, findings not comparable. | 21 |
| 112 | UnitAudit world.Tick % 15 | H/zeit | Sampling every 15 ticks (= 0.5 s); also in the usage text (l. 45), in the doc comment (l. 82) and in the output (l. 171) | own | Sampling every 0.25 s instead of 0.5 s; sample numbers (samples, inside, stall) double, the comparison with old reports breaks. | 21 |
| 165 | UnitAudit world.Tick - run.Start >= 60 | H/zeit | Report threshold: unit at least 60 ticks (= 2 s) inside a building footprint | own | Threshold halves to 1 s; more reports (e.g. drives through factories). | 21 |
| 199-200 | Options: StressOptions.Standard / --ticks | H/budget | Run length of stress and replay-record: option --ticks, default StressOptions.Standard.Ticks = 216,000 (= 2 h at 30 Hz; declared in src/CnCNew.Simulation/Testing/StressScenario.cs:16) | own | 216,000 ticks are only 1 h of game time; unchanged valid as a step-count load test, the statement "2 hours" not. | 21 |
| 202 | Options: DrainTicks / CheckpointInterval | H/budget | DrainTicks = min(default 6000, Ticks/4); CheckpointInterval = min(1000, Ticks/20) – tick literals 6000/1000 from StressOptions.Standard | own | Drain phase and checkpoint spacing halve in game time; the result of the synthetic test stays valid, the replay file does not change. | 21 |
| 293-295 | ReferenceMatch: ReferenceMatchOptions.Standard / --max-ticks / --min-ticks | H/budget | Run length of reference-match: --max-ticks default 216,000, --min-ticks default 0; plus from Standard GraceTicks 300, CheckpointInterval 1000, IncomeInterval 300 (ReferenceMatchScenario.cs:28) | own | Budget corresponds to 1 h instead of 2 h; income cadence and grace period halve in game time, winner/tick/hash change. | 21 |
| 325 | ReferenceMatch: scenario.VictoryTick / 30 / 60.0 | H/ausgabe | Output "game minutes": victory tick divided by the literal 30 (integer division) and 60.0 | own | Displayed game minutes are twice as high as the real game time. | 21 |
| 336-339,452-455 | ReferenceMatch/AiMatch: FinalHash, Chain(TickHashes) | H/golden | Output of final hash and chain hash; these values stand as reference values in CLAUDE.md and docs (e.g. tick 13,500, 0946FB676DEC615C) | own | All documented winners/ticks/hashes become invalid and must be collected anew per rate. | 21 |
| 361-362 | AiMatch: SkirmishMatchOptions.Standard / --max-ticks | H/budget | Run length of ai-match: --max-ticks, default 108,000 (= 1 h; SkirmishMatchScenario.cs:19, plus GraceTicks 300, CheckpointInterval 1000, SciencePointInterval 2700); documented calls use 45000 | own | Budget corresponds to half the game time (45000 = 12.5 instead of 25 min): more stalemate results, reference tables not comparable. | 21 |
| 381-385 | AiMatch: --trace N / world.Tick % trace | H/zeit | Trace interval in ticks from the caller (no default, 0 = off); one line per N ticks | own | The same N yields twice as dense lines per game time; documented calls (--trace N) must scale N. | 21 |
| 441 | AiMatch: scenario.VictoryTick / 30 / 60.0 | H/ausgabe | Output "game minutes": victory tick divided by the literal 30 (integer division) and 60.0 | own | Displayed game minutes are twice as high as the real game time. | 21 |
| 482 | SkirmishSweep --max-ticks (default 3000) | H/budget | Run length of skirmish-sweep: --max-ticks, default 3000 ticks (= 100 s) per map | own | Only 50 s of game time per map; "starts and ends" stays checkable, the AI acts less. | 21 |
| 518 | SkirmishSweep: GraceTicks = Math.Min(Standard.GraceTicks, maxTicks / 4) | H/budget | Grace period after the victory: min(300, maxTicks/4) ticks; 300 from SkirmishMatchOptions.Standard | own | Grace period 5 s instead of 10 s of game time; only the length of the recording. | 21 |
| 563,645-655 | HashReplay/Stress: FinalHash, Chain | H/golden | Stress test final hash and chain hash (documented 56882F763EABEF67 / B8E663493F2C73D5) over the synthetic fixture | own | Hash changes as soon as fixture times or run length scale with the rate; collect the reference value anew. | 21 |
| 577 | MissionRun --ticks (default 9000) | H/budget | Run length of mission-run: --ticks, default 9000 (= 5 min); the documented call uses --ticks 3000; save point at ticks/2 (l. 593) | own | Half the game time: fewer scripts/actions in the report, chain/final hash new anyway. | 21 |
| 685 | Stress: options.Ticks >= 216_000 && SpawnedTotal < 100_000 | H/budget | Acceptance threshold: with at least 216,000 ticks, 100,000 spawns must be reached (couples the default run length to the spawn count) | own | With a scaled run length (432,000) the check still applies, with a scaled spawn rate it stays fulfilled; the literal 216,000 must match the default length. | 21 |

**`tools/CnCNew.UiRenderTool/HudFeedbackScenes.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 37 | HudFeedbackScenes.Capture.Advance: match.Advance(1.0 / 30) | H/schritt | Fixed step 1/30 s for the live match per "frame"; Advance(1) (ll. 49, 53) means exactly one tick | own | Every call makes two ticks; the image shows a later simulation state, PNG checksums change. | 21 |
| 38 | HudFeedbackScenes.Capture.Advance: controller.Update(1.0 / 30) | H/schritt | Fixed step 1/30 s for the HUD controller (wall-clock time in seconds, MatchController.Update(deltaSeconds)) | own | The HUD clock runs in seconds and stays right; only the ratio of one tick per HUD step no longer holds. | 21 |
| 64 | HudFeedbackScenes.Capture: Advance(45) | H/zeit | 45 steps of 1/30 s (= 1.5 s) waiting time so that tooltip and message lines are up | own | Stays 1.5 s of wall-clock time but simulates 90 instead of 45 ticks; message age in logic frames and with it the image checksum change. | 21 |

**`tools/CnCNew.UiRenderTool/MissionTextScenes.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 41-44 | MissionTextScenes.Capture: log.Add(..., 0/0/120); log.Update(1..150) | H/zeit | Message lines added at frame 0, 0 and 120, 150 updates in logic frames (UiMessageLog counts frames); the comment says "ten seconds younger", 120 frames are 4 s | own | State depends on InGameUiSettings.LogicFramesPerSecond and the number of updates; at 60 updates/s the same image would be reached after 2.5 s, PNG stable only if the frames stay 30 Hz original frames. | 21 |
| 48-50 | MissionTextScenes.Capture: MilitaryCaptionState.Begin(..., 5000, 0, ...); caption.Update(1..130) | H/zeit | Military caption with 5000 ms duration from frame 0, 130 updates in logic frames (the comment names frame 95) | own | Typing progress after 130 frames depends on frames per second in MilitaryCaptionState; if the constant there changes, the image changes. | 21 |
| 55 | MissionTextScenes.Capture: NamedTimers 30 * (11 * 60 + 55) | H/zeit | Counter value in logic frames: 30 × 715 s = 11:55 display; NamedTimers divides by InGameUiSettings.LogicFramesPerSecond | own | If the divisor becomes 60, the image shows 5:57 instead of 11:55 and the checksum changes. | 21 |
| 56 | MissionTextScenes.Capture: NamedTimers frame 15 * 5 | H/ticks | Frame 75 as the blink phase of the expired counter (frame / NamedTimerFlashFrames % 2) | own | Phase stays as long as NamedTimerFlashFrames is not scaled; otherwise a different image (blink colour on/off). | 21 |
| 66 | MissionTextScenes.Capture: SuperweaponTimers.Draw(..., 15, ...) | H/ticks | Tick 15 as the blink phase of the superweapon list (tick / flashFrames % 2); the second values 252/47 are already seconds | own | Phase stays as long as the blink frames are not scaled; otherwise a different image. | 21 |
| 69 | MissionTextScenes.Capture: CinematicTextState(..., 120) | H/zeit | Expiry of the cinematic text at frame 120 (ExpiresAtFrame = 4 s at 30 Hz); only set here, not played | own | No image difference in this scene; the value means 2 s at 60 Hz. | 21 |

**`tools/CnCNew.UiRenderTool/ShellFlowScenes.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 63 | ShellFlowScenes.Capture: shell.Update(1.0 / 30) | H/schritt | One shell step of 1/30 s so that the loading screen delivers the start output; GameShell.Update takes seconds (further calls with 0.9/0.5 s in ll. 44, 46, 106) | own | The shell runs in wall-clock time: no difference through the simulation rate. | 21 |
| 65 | ShellFlowScenes.Capture: MaxTicks = 600 | H/budget | Short skirmish of 600 ticks (= 20 s) whose statistics fill the score screen | own | Only 10 s of game time: other statistics numbers in the score image, PNG checksum changes. | 21 |

**`tools/CnCNew.UiRenderTool/TransitionScenes.cs`**

| Line | Symbol | Cl. | What | Origin | At 60 Hz if unchanged | Phase |
|---|---|---|---|---|---|---|
| 21 | TransitionScenes.Frame = 1.0 / 30 | H/schritt | Step size of the shell animation: 1/30 s per frame, according to the comment the frame rate of the original; 4 uses (ll. 45, 54, 81 and Film) | own | Shell transitions run in wall-clock time (GameShell.Update(dtSeconds)); stays right as the original frame rate, must not be scaled along with the simulation rate. | 21 |
| 45,81 | TransitionScenes.Settle frame < 600 / guard++ < 400 | H/budget | Loop bounds in frames of 1/30 s: at most 600 (20 s) until the animation rests, 400 (13.3 s) until the screen changes | own | No difference as long as Frame stays 1/30 s. | 21 |
| 60,66,72,74,79,82,92 | TransitionScenes.Film(..., frames) | H/zeit | Captured frame numbers of the transitions in 1/30 s frames (e.g. entry 0,12,24,30,…,58; score 0,1,8,12,20,30,40,55,70); go into the file names fNN | own | No difference as long as Frame stays 1/30 s; if Frame were scaled, the same numbers show earlier animation phases. | 21 |
| 85 | TransitionScenes.Capture: MaxTicks = 900 | H/budget | Short skirmish of 900 ticks (= 30 s) whose statistics the score screen counts up | own | Only 15 s of game time: other numbers in the score image, PNG checksums of the score images change. | 21 |

### 7.2 Tests per file (overview for the list "converted / deliberately in ticks / not affected" of Phase 21)

| File | schritt | zeit | ticks | budget | rate | golden | ausgabe | D | Total |
|---|---|---|---|---|---|---|---|---|---|
| `CnCNew.Game.Tests/AudioDirectorTests.cs` | 13 | 6 | 5 | 2 | 3 |  | 1 |  | 30 |
| `CnCNew.Game.Tests/CampaignShellTests.cs` |  |  |  | 8 |  |  |  |  | 8 |
| `CnCNew.Game.Tests/CampaignTests.cs` |  | 3 | 1 | 10 |  |  |  |  | 14 |
| `CnCNew.Game.Tests/HudTests.cs` | 1 | 22 |  | 5 |  | 1 |  |  | 29 |
| `CnCNew.Game.Tests/LiveMatchTests.cs` | 1 | 1 |  |  | 1 |  |  |  | 3 |
| `CnCNew.Game.Tests/MissionEffectsTests.cs` | 16 | 11 | 2 |  |  |  |  |  | 29 |
| `CnCNew.Game.Tests/MissionHudTests.cs` | 21 | 1 |  |  | 1 |  |  |  | 23 |
| `CnCNew.Game.Tests/MissionPresentationTests.cs` |  |  | 1 |  | 1 |  |  |  | 2 |
| `CnCNew.Game.Tests/MissionTextTests.cs` | 12 | 10 |  | 1 |  |  |  |  | 23 |
| `CnCNew.Game.Tests/ScenarioTests.cs` | 1 |  |  | 2 |  |  |  |  | 3 |
| `CnCNew.Game.Tests/ShellTests.cs` | 2 |  |  | 1 |  | 1 |  |  | 4 |
| `CnCNew.Game.Tests/ShellTransitionTests.cs` |  |  | 4 | 1 |  |  | 1 |  | 6 |
| `CnCNew.Presentation.Tests/CameraTests.cs` | 16 | 2 |  |  | 1 |  |  |  | 19 |
| `CnCNew.Presentation.Tests/CaptureConditionTests.cs` |  | 2 |  | 1 |  |  |  |  | 3 |
| `CnCNew.Presentation.Tests/CaptureFlashTests.cs` |  | 14 | 1 | 1 |  |  |  |  | 16 |
| `CnCNew.Presentation.Tests/CursorTests.cs` | 8 | 1 |  | 2 |  |  |  |  | 11 |
| `CnCNew.Presentation.Tests/FxTests.cs` |  | 32 | 1 | 9 |  |  |  |  | 42 |
| `CnCNew.Presentation.Tests/InputTests.cs` | 4 | 2 | 2 | 3 | 2 |  |  |  | 13 |
| `CnCNew.Presentation.Tests/JetOrderTests.cs` |  | 2 | 1 | 1 | 1 |  |  |  | 5 |
| `CnCNew.Presentation.Tests/ObjectTests.cs` |  | 3 | 5 | 3 | 1 |  |  |  | 12 |
| `CnCNew.Presentation.Tests/StructureConditionTests.cs` |  | 21 | 7 | 31 |  |  |  |  | 59 |
| `CnCNew.Presentation.Tests/TerrainTests.cs` |  |  |  |  | 1 |  |  |  | 1 |
| `CnCNew.Presentation.Tests/UnitConditionTests.cs` |  | 6 | 3 | 19 |  |  |  |  | 28 |
| `CnCNew.Simulation.Tests/Program.cs` |  |  | 5 |  |  |  |  |  | 5 |
| `CnCNew.Simulation.Tests/Tests.Acquisition.cs` |  | 6 | 1 |  | 2 |  |  |  | 9 |
| `CnCNew.Simulation.Tests/Tests.Air.cs` |  | 3 | 1 | 2 |  |  |  |  | 6 |
| `CnCNew.Simulation.Tests/Tests.AirModelGeometry.cs` |  |  |  | 1 |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.AirReal.cs` |  | 2 |  | 1 |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.AirWeapons.cs` |  | 7 | 1 |  |  |  |  | 1 | 9 |
| `CnCNew.Simulation.Tests/Tests.Basics.cs` |  |  |  |  | 14 |  |  |  | 14 |
| `CnCNew.Simulation.Tests/Tests.BattlePlan.cs` |  | 1 |  |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.CleanupAndSpy.cs` |  | 3 |  |  |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.Collision.cs` |  | 2 |  |  | 1 |  |  | 2 | 5 |
| `CnCNew.Simulation.Tests/Tests.Combat.cs` |  | 6 |  |  |  |  |  |  | 6 |
| `CnCNew.Simulation.Tests/Tests.Construction.cs` |  | 6 | 2 |  | 1 |  |  |  | 9 |
| `CnCNew.Simulation.Tests/Tests.CratesAndSpecials.cs` |  | 3 |  |  | 1 |  |  | 1 | 5 |
| `CnCNew.Simulation.Tests/Tests.CreationLists.cs` |  | 1 |  |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.DamagedLocomotor.cs` |  | 1 |  |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.FiringTracker.cs` |  | 10 |  |  | 1 |  |  |  | 11 |
| `CnCNew.Simulation.Tests/Tests.Flight.cs` |  | 2 | 1 | 1 |  |  |  |  | 4 |
| `CnCNew.Simulation.Tests/Tests.GoldenMaster.cs` |  | 1 |  |  | 3 |  |  |  | 4 |
| `CnCNew.Simulation.Tests/Tests.JetFlight.cs` |  | 2 |  | 3 | 5 |  |  |  | 10 |
| `CnCNew.Simulation.Tests/Tests.JetFlightReal.cs` |  | 1 |  | 1 | 4 |  |  |  | 6 |
| `CnCNew.Simulation.Tests/Tests.LastBlockers.cs` |  | 5 | 4 | 5 |  |  |  | 1 | 15 |
| `CnCNew.Simulation.Tests/Tests.Navigation.cs` |  | 2 | 1 | 5 | 1 |  |  |  | 9 |
| `CnCNew.Simulation.Tests/Tests.NavigationEscape.cs` |  |  |  | 1 |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.Phase12Modules.cs` |  |  | 1 |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.Phase13Actions.cs` |  |  |  | 1 |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.Phase5.cs` |  | 28 | 8 |  | 3 |  |  |  | 39 |
| `CnCNew.Simulation.Tests/Tests.PlayerAgent.cs` |  |  |  | 3 |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.PowerPlant.cs` |  | 1 | 1 |  |  |  |  |  | 2 |
| `CnCNew.Simulation.Tests/Tests.Projectiles.cs` |  | 4 |  | 1 | 6 |  |  |  | 11 |
| `CnCNew.Simulation.Tests/Tests.RealFlight.cs` |  |  | 3 | 1 |  |  |  |  | 4 |
| `CnCNew.Simulation.Tests/Tests.RealMovement.cs` |  |  |  | 1 |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.ReferenceMatch.cs` |  |  | 1 | 1 |  |  |  |  | 2 |
| `CnCNew.Simulation.Tests/Tests.Repair.cs` |  |  |  |  | 1 |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.RetailModules.cs` |  | 2 | 1 |  |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.Scripting.cs` |  |  |  | 5 |  |  |  |  | 5 |
| `CnCNew.Simulation.Tests/Tests.ScriptingActions12.cs` |  |  | 1 |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.ScriptingPhase11.cs` |  | 1 | 1 | 1 |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.ScriptingPhase12.cs` |  |  | 1 |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.Skirmish.cs` |  | 1 | 2 |  |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.Slaves.cs` |  | 2 |  | 1 | 2 |  |  |  | 5 |
| `CnCNew.Simulation.Tests/Tests.SpecialPower.cs` |  | 11 | 5 | 3 | 1 |  |  |  | 20 |
| `CnCNew.Simulation.Tests/Tests.StagedCapture.cs` |  | 17 | 1 | 11 |  |  |  |  | 29 |
| `CnCNew.Simulation.Tests/Tests.State.cs` |  |  |  | 16 | 2 |  |  |  | 18 |
| `CnCNew.Simulation.Tests/Tests.StructuresAndWorld.cs` |  | 35 |  | 29 | 5 |  |  | 9 | 78 |
| `CnCNew.Simulation.Tests/Tests.Supply.cs` |  | 7 |  | 13 | 1 |  |  |  | 21 |
| `CnCNew.Simulation.Tests/Tests.Teams.cs` |  |  |  | 2 | 1 |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.Terrain.cs` |  |  |  |  | 1 |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.TimeFreeze.cs` |  | 1 | 3 | 3 |  |  |  |  | 7 |
| `CnCNew.Simulation.Tests/Tests.Transports.cs` |  | 12 | 1 | 14 | 14 |  |  |  | 41 |
| `CnCNew.Simulation.Tests/Tests.TurretAttack.cs` |  | 15 | 1 | 47 | 2 |  |  |  | 65 |
| `CnCNew.Simulation.Tests/Tests.Turrets.cs` |  | 13 | 2 | 11 | 4 |  |  |  | 30 |
| `CnCNew.Simulation.Tests/Tests.Veterancy.cs` |  |  |  | 1 |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.Vision.cs` |  |  | 1 | 1 |  |  |  |  | 2 |
| `CnCNew.Simulation.Tests/Tests.WeaponSlots.cs` |  | 17 | 5 | 9 | 2 |  |  |  | 33 |
| `CnCNew.Simulation.Tests/Tests.WeaponSlotsIntegration.cs` |  | 2 | 1 | 12 |  |  |  |  | 15 |
| `CnCNew.Simulation.Tests/Tests.WeaponSlotsReal.cs` |  |  | 1 | 3 |  |  |  |  | 4 |
| `CnCNew.Simulation.Tests/Tests.World.cs` |  | 4 | 11 | 1 | 1 |  |  |  | 17 |
| `CnCNew.Ui.Tests/TextAndSettingsTests.cs` |  | 2 |  |  |  |  |  |  | 2 |
| `CnCNew.Ui.Tests/TransitionTests.cs` |  |  | 19 |  | 3 |  |  |  | 22 |
| **Total** | 95 | 378 | 120 | 311 | 94 | 2 | 2 | 14 | 1016 |

Test files without a row here (37 of 119) have no coupled site among the audited hits. `zeit` is to be expressed in game time, `schritt` to be derived from the rate, `ticks` stays, `budget` is to be computed as game time where it means game time; `rate` depends on the decision of Phase 20. The transition tests (`Ui.Tests/TransitionTests`, `Game.Tests/ShellTransitionTests`) compute in original frames on wall-clock time and are listed as `ticks` with this note.

### 7.3 Tests, individual sites

**`tests/CnCNew.Game.Tests/AudioDirectorTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 123,124 | AudioDirectorTests.EvaCatalogParsing (FramesBetweenChecks, FramesToExpire) | H/zeit | 2 | 2 sites: expects FramesBetweenChecks == 60 for 2000 ms and FramesToExpire == 30 for 1000 ms (message: 'at 30 Hz') | 21 |
| 128 | AudioDirectorTests.EvaCatalogParsing (default values) | H/zeit | 1 | expects the frame default values 900 (30 s) and 150 (5 s) of an EvaEvent without times of its own | 21 |
| 172-178 | AudioDirectorTests.EvaAnnouncerBehaviour (announcer.Update(frame)) | H/ticks | 5 | 5 sites: EvaAnnouncer.Update with the logic frames 0, 2, 3 – pure sequence ('frame 0 never announces', same frame number twice) | 21 |
| 184 | AudioDirectorTests.EvaAnnouncerBehaviour (solo.Update(2 + 3334 * 30 / 1000 + 2)) | H/rate | 1 | Expiry frame of an announcement computed with the literal rate 30: 2 + 3334 ms × 30 / 1000 + 2 = 104 | 21 |
| 505 | AudioDirectorTests.SelectionOrdersAndClicks (CommandFeedback Tick 1, DurationFrames 30) | H/zeit | 1 | Test feedback with tick 1 and DurationFrames 30 (1 s lifetime of a hint); the value is not evaluated in this test | 21 |
| 561,562,605,606,731,732 | AudioDirectorTests.MuteAndSlidersSilence/ScriptControlsTheSound/OfflineRenderIsReproducible (director.Tick, render.Pump) | H/schritt | 6 | 6 sites: fixed step 1.0/30 to AudioDirector.Tick(null, dt) and OfflineAudioRenderer.Pump(dt); without a game view, the clock of the director is the sum of the dt | 21 |
| 687,689,693,697 | AudioDirectorTests.ScriptControlsTheSound (director.Tick(view, 1.0 / 30), view.Tick = 10..13) | H/schritt | 4 | 4 sites: step 1.0/30 with a test view whose tick is set by hand to 10, 11, 12, 13 (one Eva pass per new tick) | 21 |
| 727,742 | AudioDirectorTests.OfflineRenderIsReproducible (30 steps) | H/zeit | 2 | 2 sites: 30 steps of 1/30 s count as one second; expects 22049 or 22050 rendered audio frames; UI click in step 3, selection in step 9 | 21 |
| 767 | AudioDirectorTests.RealCaptureScenario (const frames = 900) | H/budget | 1 | Run length 900 steps (30 s) of the scenario capture-building through LiveScenario.Advance/hud.Update/audio.Update; expects only flashes > 0 and the same world hash with/without sound | 21 |
| 768 | AudioDirectorTests.RealCaptureScenario (const step = 1.0 / 30) | H/schritt | 1 | fixed step 1.0/30 for live.Advance, hud.Update, audio.Update, output.Pump (6 uses) | 21 |
| 821 | AudioDirectorTests.RealDataRun (const frames = 30 * 20) | H/budget | 1 | Run length 600 steps, comment '20 s of game time at 30 Hz'; NotifyInsufficientFunds at frames / 4 | 21 |
| 822,881 | AudioDirectorTests.RealDataRun (const blockFrames = 1470; blockFrames * 90) | H/rate | 2 | 2 sites: audio block per step = 44100 / 30 = 1470 sample frames ('one tick's worth of audio'); WAV excerpt 90 blocks = 3 s | 21 |
| 835,836 | AudioDirectorTests.RealDataRun (live.Advance(1.0 / 30), director.Update(live, 1.0 / 30)) | H/schritt | 2 | 2 sites: fixed step 1.0/30 for LiveMatch.Advance and AudioDirector.Update; two runs must deliver byte-identical sound (hash is only compared, not pinned) | 21 |
| 871 | AudioDirectorTests.RealDataRun (console line frames / 30.0) | H/ausgabe | 1 | Output converts steps with / 30.0 into seconds of game time and the real-time factor | 21 |

**`tests/CnCNew.Game.Tests/CampaignShellTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 163,165,180,182,193,195,225,247 | CampaignShellTests.StepUntilOver; SaveLoadThroughShellContinuesIdentically | H/budget | 8 | 8 sites: run lengths in ticks through Session.Step(): 5 before the destruction, 200 until the victory/defeat script ends the mission, 40 until saving, 120 ticks of chain hash comparison | 21 |

**`tests/CnCNew.Game.Tests/CampaignTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 205,208,213,218,225,227,240,250,269,306 | CampaignTests.ControllerFlow; SaveLoadContinues; SaveRejection; StoreBehaves (Step(run, n)) | H/budget | 10 | 10 sites: run lengths 3, 5, 10, 40 ticks and 200 ticks of chain hash comparison through Session.Step() | 21 |
| 221,222,228 | CampaignTests.ControllerFlow (presentationLatencyTicks: 90; Step(last, 200)) | H/zeit | 3 | 3 sites: presentation latency 90 ticks (3 s) is passed through and checked; 200 ticks 'end-game delay' until IsOver | 21 |
| 241-243 | CampaignTests.SaveLoadContinues (header.Tick == 40) | H/ticks | 1 | the savegame header carries the world tick 40 after 40 steps (title 'Mission A, tick 40') | 21 |

**`tests/CnCNew.Game.Tests/HudTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 128,384 | HudTests.CursorAndMarkers; ScriptedSession (Advance 30) | H/budget | 2 | 2 sites: 30 frames of lead-in or waiting time until the unit has moved (distance > 1) | 21 |
| 164,175,863 | HudTests.CursorAndMarkers; MoneyAndPowerTooltips (Advance 45, 20, 45) | H/zeit | 3 | 3 sites: 45 frames because command hints live 40 frames; 20 frames for age 0.4–0.7; 45 frames of tooltip waiting time – all on the UI clock _clockMs (sum of the dt) | 21 |
| 208,234 | HudTests.OverlayIsDrawn (Advance 120; Advance 4 * 30) | H/zeit | 2 | 2 sites: 120 frames because Drawable::drawHealing ignores the first three seconds; 4 × 30 frames until the healing icon is gone three seconds after the last healing (FrameProjector.HealingIconTicks = 3 × 30) | 21 |
| 285 | HudTests.Advance (dt = 1.0 / 30) | H/schritt | 1 | Helper Advance(match, controller, frames, dt = 1.0/30): per frame LiveMatch.Advance(dt) and MatchController.Update(dt); over 60 calls, never with another dt | 21 |
| 350,369,454 | HudTests.ScriptedSession; ReplayMatches (Advance 60, loop 4000, Advance 90) | H/budget | 3 | 3 sites: wait window of 60 frames until the construction site exists, at most 4000 frames until the unit is finished, 90 frames of driving | 21 |
| 594,781 | HudTests.CommandButtonTooltips; RequirementTooltip (Advance 30) | H/zeit | 2 | 2 sites: 30 frames = 990 ms on the UI clock until the tooltip appears after the Mouse.ini waiting time | 21 |
| 620,641,642,646,647,648 | HudTests.MessageList (CommandFeedback(..., match.Tick, 30, ...)) | H/zeit | 6 | 6 sites: test feedback with Tick = match.Tick and DurationFrames 30; the message list does not evaluate the duration | 21 |
| 673,674,675,681,685,690 | HudTests.MessageList (Event(..., tick: match.Tick + 200/400/700)) | H/zeit | 6 | 6 sites: event ticks +200, +400, +700 map the ten-second lock of the attack message (+200 and +400 still locked, +700 'ten seconds later') | 21 |
| 709,711,712 | HudTests.MessageList (Advance 120, Advance 200, '277 logic frames') | H/zeit | 3 | 3 sites: after 120 frames the lines begin to fade, after another 200 they are gone; the message names 277 logic frames (approx. 9.2 s); UiMessageLog.Update(Match.Tick) per tick | 21 |
| 876-893 | HudTests.GoldenDrawListHash (baselines/hud-drawlist-1280x720.sha256) | H/golden | 1 | pinned SHA-256 of the HUD draw list dump after Advance(match, controller, 1) with dt 1/30 in a fresh Alpine Assault skirmish; for the content see the done file | 21 |

**`tests/CnCNew.Game.Tests/LiveMatchTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 39 | LiveMatchTests.Run (live.Advance(1.0)) | H/zeit | 1 | one Advance over a whole second, after which drawables must be there ('the base shows up within a second'); LiveGame limits the ticks per call (MaxTicksPerFrame) | 21 |
| 43 | LiveMatchTests.Run (30 * 20 frames, live.Advance(1.0 / 30)) | H/schritt | 1 | 600 steps with a fixed 1.0/30 (20 s) | 21 |
| 45 | LiveMatchTests.Run (live.Tick >= 590) | H/rate | 1 | expects at least 590 ticks after 600 steps: 'simulation advanced at 30 Hz' | 21 |

**`tests/CnCNew.Game.Tests/MissionEffectsTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 225 | MissionEffectsTests.PresentationClockRunsWhileTimeIsFrozen (PresentationLatencyTicks = 1_000_000) | H/ticks | 1 | Sentinel value: presentation latency in world ticks so large that it never expires | 21 |
| 234,236 | MissionEffectsTests.PresentationClockRunsWhileTimeIsFrozen (steps 30..36; director.Clock >= frozenAt + 30) | H/zeit | 2 | 2 sites: a camera pan of 1 s (MOVE_CAMERA_TO Real(1)) is to last 30 to 36 director steps and the presentation clock is to advance by at least 30 | 21 |
| 246 | MissionEffectsTests.PresentationClockRunsWhileTimeIsFrozen (15 steps, CAMERA_FADE_ADD 10/10/10) | H/zeit | 1 | after 15 director steps the fade (10 frames each on/hold/off) is to be in the hold | 21 |
| 263,266,293,297,350 | MissionEffectsTests.ScreenShakeShakesTheCamera; BorderSwitchConfinesTheCamera; ScreenFiltersFollowTheOriginalFrameCounts (ApplyCameraTo(camera, 1.0 / 30), camera.Update(1f / 30)) | H/schritt | 5 | 5 sites: fixed step 1/30 to MissionPresentationDirector.ApplyCameraTo and RtsCamera.Update | 21 |
| 274 | MissionEffectsTests.ScreenShakeShakesTheCamera (decay 0.75 per logic frame) | H/zeit | 1 | expects a falling shake over 8 camera updates of 1/30 s and Offsets[7] < 0.6 at strength 3 ('decays by 0.75 per logic frame') | 21 |
| 322-337 | MissionEffectsTests.ScreenFiltersFollowTheOriginalFrameCounts (CAMERA_BW_MODE_BEGIN/END Int(10)) | H/zeit | 1 | Greyscale ramp over 10 frames: +0.1 per director step (levels[0] 0.1; [4] 0.5; [9] 1), end after 12 steps | 21 |
| 339,354,355 | MissionEffectsTests.ScreenFiltersFollowTheOriginalFrameCounts (motion blur +5/-5 per frame) | H/zeit | 3 | 3 sites: blur counter rises by 5 per director step up to 60 (12 frames), falls for 11 frames, cut in step 11 | 21 |
| 394,424,435,439,442,453,480,485,489,496,533 | MissionEffectsTests.RealIntro/RealCommandBar/RealScienceAndSelection/RealColorsAndFlashes/RealSuperweaponList (controller.Update(1.0 / 30)) | H/schritt | 11 | 11 sites: MissionController.Update with a fixed 1.0/30; loop lengths 3900 (intro MD_USA01), 120, 60, 60, 45, 60 frames as lead-in | 21 |
| 400,405 | MissionEffectsTests.RealIntro (clockLead = director.Clock - live.Tick) | H/ticks | 1 | compares the presentation clock of the director directly with the world tick (lead > 0 during FREEZE_TIME) | 21 |
| 494,497-500 | MissionEffectsTests.RealColorsAndFlashes (45 frames, Flash 1 s) | H/zeit | 1 | one second of flashing (Flash(target, color, 1)) is to show exactly two flashes in 45 updates and be extinguished after that | 21 |
| 516,517 | MissionEffectsTests.SuperweaponDrawRules (Draw(..., tick 14/15/30)) | H/zeit | 2 | 2 sites: colour change of the finished countdown at tick 15 and back at 30 ('every 15 logic frames', SuperweaponCountdownFlashDuration 500 ms) | 21 |

**`tests/CnCNew.Game.Tests/MissionHudTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 37,40,48,69,72,83,89,91,99,114,116,120,135,140,158,162,170,173,192,194,195 | MissionHudTests.Run (controller.Update(1.0 / 30)) | H/schritt | 21 | 21 sites: MissionController.Update with a fixed 1.0/30; loops of 60, 10, 20, 30, 90 frames and abort limits 1500, 2400, 30 * 20 frames | 21 |
| 38 | MissionHudTests.Run (live.Tick >= 55) | H/rate | 1 | expects at least 55 ticks after 60 updates: 'the mission advanced at 30 Hz' | 21 |
| 164 | MissionHudTests.Run (live.Tick is > 600 and < 1200) | H/zeit | 1 | the intro of GC_ChemGeneral is to last 'about 25 seconds': end between tick 600 and 1200 | 21 |

**`tests/CnCNew.Game.Tests/MissionPresentationTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 99 | MissionPresentationTests.CameraMoveCompletesOnItsRealDuration (PresentationLatencyTicks = 1_000_000) | H/ticks | 1 | Sentinel value in world ticks that never expires | 21 |
| 119 | MissionPresentationTests.CameraMoveCompletesOnItsRealDuration (completionTick) | H/rate | 1 | expected completion tick = fireTick + round(5 s × SimulationTiming.LogicFramesPerSecond): uses the constant instead of a literal | 21 |

**`tests/CnCNew.Game.Tests/MissionTextTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 105-117,122 | MissionTextTests.MessageLines (UiMessageLog.Update(frame)) | H/zeit | 1 | Fading of the message lines in frames: alpha 255 up to age 99, 254 at 100, 153 at 200, gone at 277 ('about 9.2 seconds'); the comment computes MessageDelayMS 75000 / 30 / 1000 = 0 | 21 |
| 154-175 | MissionTextTests.LetterboxFollowsTheOriginal (15/15/40/15/15 steps) | H/zeit | 1 | Letterbox fade 1000 ms: after 15 director steps level 0.5, after 30 level 1, the same when closing | 21 |
| 186-198 | MissionTextTests.TypingClick (steps 22, 23, 24..100, 101..260) | H/zeit | 1 | first typing click in step 23 (750 ms start delay), then every second step (23, 25, 27, 29, 31) | 21 |
| 212,216-234 | MissionTextTests.MilitaryCaptionTimeline (caption.Update(frame)) | H/zeit | 1 | Timeline in frames: start 100, lifetime 150 (5000 ms), first character at 123, line break waits until 150, cursor toggles every 10 frames (110/120/130) | 21 |
| 240,249,250 | MissionTextTests.MilitaryCaptionFadeAndRefusals (LastFrame == 40; 75/76 frames) | H/zeit | 3 | 3 sites: '1000 ms are 30 frames' (start 10, LastFrame 40); after that alpha 3 after 75 frames, removed in the 76th. | 21 |
| 281-283,295-324 | MissionTextTests (Step loops 150; 22/23/62/150/200/240) | H/zeit | 1 | Director steps as time: lines begin to fade after 150 steps; title types from step 23, duration 150 steps, then fade-out until 240 | 21 |
| 355-358 | MissionTextTests (director.Clock == 16 / == 31, blinking every 15 frames) | H/zeit | 1 | the finished counter shows the blink colour at clock 16 and its own again at 31 (NamedTimerCountdownFlashDuration 500 ms = 15 frames) | 21 |
| 386-388,438,459 | MissionTextTests.CinematicText; Popup; SpeechSubtitles | H/zeit | 1 | '4 seconds are 120 frames' (still visible at step 119, gone at 120); popup over 300 steps; subtitles 8000 ms = 240 frames | 21 |
| 467 | MissionTextTests.ScalingFollowsTheScreen (30 steps) | H/budget | 1 | 30 director steps of lead-in until at least one character of the caption is typed (start delay 22 frames) | 21 |
| 523,536,545,561,578,591,593,653,658,661,674,681 | MissionTextTests.RealCaption/RealCountdown/RealTypingClick/RealSharedMessageList/RealPopupInTheMission (controller.Update(1.0 / 30)) | H/schritt | 12 | 12 sites: MissionController.Update with a fixed 1.0/30; loops of 190 (caption fully typed), 120, 2100 (countdown MD_CHI02 visible), 30, 150 + 200 (line fades and disappears) | 21 |

**`tests/CnCNew.Game.Tests/ScenarioTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 40 | ScenarioTests.Run/Play (live.Advance(1.0 / 30 + 1e-6)) | H/schritt | 1 | fixed step 1.0/30 plus 1e-6 until live.Tick >= ticks; the surcharge is to secure exactly one tick per call | 21 |
| 44,45 | ScenarioTests.Run (Play(Math.Min(scenario.Ticks, 1300))) | H/budget | 2 | 2 sites: run length = tick number of the scenario, at most 1300 ticks; after that counted events of the scenario are expected | 21 |

**`tests/CnCNew.Game.Tests/ShellTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 99,428 | ShellTests.WalkScreens; LocalizedSetupText (shell.Update(1.0 / 30)) | H/schritt | 2 | 2 sites: one GameShell.Update with 1.0/30 after the loading screen ('exactly one StartMatchOutput after one drawn frame'); the shell clock is wall-clock time | 21 |
| 107 | ShellTests.WalkScreens (MaxTicks = 600) | H/budget | 1 | Skirmish is computed through live.Session.Step() until MaxTicks = 600; the result fills the score screen whose draw list hash is pinned (ScoreScreen@...) | 21 |
| 137-152 | ShellTests.Capture (baselines/screens.txt) | H/golden | 1 | 54 pinned SHA-256 (9 screens × 6 resolutions) of UiDrawList.Dump without transitions; only ScoreScreen depends on a simulation run (600 ticks), all others arise without time progress | 21 |

**`tests/CnCNew.Game.Tests/ShellTransitionTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 22,23 | ShellTransitionTests.Frame, WindowFrames = 30 | H/ticks | 1 | Step 1.0 / WindowFrames with WindowFrames = 30: frame cadence of the window transitions (GameWindowTransitions) on the shell clock – original frame, not simulation tick; over 20 uses (Step, Settle, shell.Update(Frame)) | 21 |
| 161,463,464 | ShellTransitionTests.MainMenuEntry; ScoreScreenCountsUp (frames 45..120; 40..200; shown[40]) | H/ticks | 3 | 3 sites: duration of the menu entry (29 + 27 frames) and of the score lines (last column from frame 48) in transition frames – original frame, not simulation tick | 21 |
| 169 | ShellTransitionTests.MainMenuEntry (console line frames / 30.0) | H/ausgabe | 1 | Output converts transition frames with / 30.0 into seconds – original frame, not simulation tick | 21 |
| 436 | ShellTransitionTests.ScoreScreenCountsUp (MaxTicks = 600) | H/budget | 1 | Skirmish until MaxTicks = 600 through live.Session.Step(); compared is only the animated against the transition-free screen of the same run | 21 |

**`tests/CnCNew.Presentation.Tests/CameraTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 133,142,146,148,152,153,155,156 | CameraTests.Scrolling (dtSeconds 1f/30f) | H/schritt | 8 | 8 sites: dtSeconds = 1f/30f (or vector / 30f) as "one frame" in KeyboardScrollDelta, EdgeScrollDelta, RightMouseScrollDelta, ScrollByIntent; l. 157 (not a hit) 0.1f = 3 frames gives 960 | 21 |
| 164 | CameraTests.RotationPitchZoom (RotateByKeyboard) | H/rate | 1 | 1 site: RotateByKeyboard(1, 1f) expects 3 rad = 0.1 rad per frame times 30 frames per second | 21 |
| 195,198,204,213,215,217,220,223 | CameraTests.TerrainFollowing, CameraTests.Shake (camera.Update 1f/30f) | H/schritt | 8 | 8 sites: camera.Update(1f / 30f, …) as one frame; loops with 120 or 40 such steps until settled | 21 |
| 197,216 | CameraTests.TerrainFollowing, CameraTests.Shake (expectation per frame) | H/zeit | 2 | 2 sites: expectations per step of 1/30 s: zoom approaches the target value by 30 %, shake is damped with 0.75 and reverses direction | 21 |

**`tests/CnCNew.Presentation.Tests/CaptureConditionTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 117 | CaptureConditionTests.Stages (loop 200) | H/budget | 1 | 1 site (not a hit): run budget 200 ticks for the whole capture (54 ticks plus approach) | 21 |
| 128,129 | CaptureConditionTests.Stages (lengths 15/30/9) | H/zeit | 2 | 2 sites: phase lengths 15, 30 and 9 ticks for UnpackTime 500 ms, PreparationTime 1000 ms, PackTime 300 ms | 21 |

**`tests/CnCNew.Presentation.Tests/CaptureFlashTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 17,80,82,84,154,155,160,182,200,202 | CaptureFlashTests.Envelope, Capture, Restart, ScriptedFlash (envelope) | H/zeit | 10 | 10 sites: envelope of the flashing in tick ages (two ticks of peak, then 3/4, 1/2, 1/4, off from age 5; LastVisibleAge), checked through TintAt at fixed ticks (9, 10, 13, 15, 60 to 65) | 21 |
| 85,164,181 | CaptureFlashTests.ScriptedFlash (interval 15) | H/zeit | 3 | 3 sites: script flashing begins on every logic frame divisible by 15, count = seconds * 30 / 15; expects starts at 15, 30, 45, 60 for 2 s (ll. 178/179: ticks 46 and 61) | 21 |
| 141 | CaptureFlashTests.Capture (four flashes) | H/zeit | 1 | 1 site (not a hit): "a preparation of 60 ticks flashes four times" – PreparationTime 2000 ms, expects exactly four CaptureFlash events; l. 127 run budget 200 ticks; l. 138 checks that the colour changes with the tick and not with the frame | 21 |
| 172 | CaptureFlashTests.ScriptedFlash (loop 100) | H/budget | 1 | 1 site: loop over the ticks 1 to 100 that advances the tracker | 21 |
| 174 | CaptureFlashTests.ScriptedFlash (tick - 1) | H/ticks | 1 | 1 site: comparison with the colour value of the previous tick to find the start tick of a flash | 21 |

**`tests/CnCNew.Presentation.Tests/CursorTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 281,284,289,292,296,300,304,307 | CursorTests.CursorByContext (UpdateCamera 1f/30) | H/schritt | 8 | 8 sites: input.UpdateCamera(view, 1f / 30) as one frame; only cursor kind and scroll direction are checked, not the amount | 21 |
| 348,372 | CursorTests.WaypointMarkers (loops 400) | H/budget | 2 | 2 sites (not hits): at most 400 ticks each until the unit reaches the station | 21 |
| 404 | CursorTests.OrderHints (LifetimeMilliseconds 1333) | H/zeit | 1 | 1 site: move marker lives 40 frames at 30 per second = 1333 ms (also ll. 403, 405, 409; DurationFrames 40 in ll. 417 to 421) | 21 |

**`tests/CnCNew.Presentation.Tests/FxTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 63,64,67,68,76,77,91,92,96,98,99,113,116,130,131,134,151,154,164,167,178,181 | FxTests.ParticleIni (Lifetime, SystemLifetime, BurstDelay, InitialDelay, Keyframes, Damping) | H/zeit | 22 | 22 sites: particle data of the fixture in original frames (Lifetime, SystemLifetime, BurstDelay, InitialDelay, keyframe frame 30, damping per frame) | 21 |
| 393,416 | FxTests.SyntheticParse (tracer.Speed, ThrottleFrames) | H/zeit | 2 | 2 sites: tracer Speed 300 per second becomes 10 per frame, ThrottleTime 1000 ms becomes ThrottleFrames 30; likewise l. 389 DecreaseFrames 3 (100 ms) and l. 412 WeaponSpeed 10 (not hits) | 21 |
| 461,482,487 | FxTests.KeyframesHandComputed | H/zeit | 3 | 3 sites: frame sequence computed by hand: alpha 0.1 per frame up to keyframe 10, speed and SizeRate times 0.5 per update, death at frame 20 (21 updates) | 21 |
| 513,527,550,571,674,695,737 | FxTests (run-out loops) | H/budget | 7 | 7 sites (not hits): loops with 200, 60, 40, 5, 20, 80 and 40 steps until systems, tracers, lights or shake are extinguished | 21 |
| 611,613 | FxTests.ParticleDeterminism (frame % 10) | H/budget | 2 | 2 sites: test cadence, FX are triggered every 10 of 150 frames; two runs are compared byte by byte, no pinned hash | 21 |
| 673 | FxTests.ExecutorNuggets (Tracer, LightPulse) | H/zeit | 1 | 1 site: tracer travels 10 per frame (head at 40 after four updates); plus l. 658 ExpireFrame 9 and l. 670 light factor 2/3 after one of three frames (not hits) | 21 |
| 712 | FxTests.ExecutorEvents (tick += 31) | H/zeit | 1 | 1 site (not a hit): 31 ticks later the throttle window of ThrottleFrames 30 (1000 ms) is over | 21 |
| 735,736 | FxTests.CameraShakeSemantics (shake.Update) | H/zeit | 2 | 2 sites: intensity falls to 0.75 per update (1.25 then 0.9375), direction flips per frame; l. 737 forty updates until standstill | 21 |
| 804-808 | FxTests.RateLimiter (WindowTicks 10) | H/zeit | 1 | 1 site (not a hit): window of 10 ticks per event name, cues at tick 1, 10 and 11 | 21 |
| 812 | FxTests.RateLimiter (MaxPerTick) | H/ticks | 1 | 1 site: at most 5 cues in the same tick (DroppedByTickCap) | 21 |

**`tests/CnCNew.Presentation.Tests/InputTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 585 | InputTests.Arena.Step(int ticks) | H/ticks | 1 | 1 site: helper method that advances the world by N ticks (calls with 1, 2, 3, 5) | 21 |
| 597 | InputTests.Arena.Replay (ReplayHeader) | H/rate | 1 | 1 site: ReplayHeader is built with SimulationTiming.LogicFramesPerSecond; the run is compared against the live hash in l. 1347 (no pinned hash) | 21 |
| 947 | InputTests.ContextOrders (DurationFrames 40) | H/zeit | 1 | 1 site (not a hit): move marker with DurationFrames 40 (original frames, 1333 ms) | 21 |
| 1011,1035,1055 | InputTests.StandingOrderBehaviour (loops 150/120/300) | H/budget | 3 | 3 sites (not hits): run budgets in ticks until the target dies, the unit fires or reaches the waypoint | 21 |
| 1170 | InputTests.CommandBarStates (BuildTicks 60) | H/zeit | 1 | 1 site (not a hit): plantButton.BuildTicks == 60, "2 s = 60 ticks" (BuildTime 2) | 21 |
| 1230,1254,1259,1261 | InputTests.CameraIntents (camera.Update 1f/30) | H/schritt | 4 | 4 sites: CameraInputController.Update(…, 1f / 30) as one frame | 21 |
| 1255 | InputTests.CameraIntents (RotateDelta 0.1) | H/rate | 1 | 1 site: "rotate-left turns 0.1 rad per frame" – RotateDelta 0.1 at dt = 1/30 s presupposes 30 frames per second | 21 |
| 1335-1339 | InputTests.AcceptanceSequence (command ticks 2, 3, 4) | H/ticks | 1 | 1 site (not a hit): expected command stream with the ticks 2, 3 and 4 (one tick per input step) | 21 |

**`tests/CnCNew.Presentation.Tests/JetOrderTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 168,181 | JetOrderTests.GunFighterPasses (2400 ticks) | H/zeit | 2 | 2 sites (not hits): loop of 2400 ticks, statement "at least three passes … in 80 seconds" | 21 |
| 190 | JetOrderTests.SimulationTimingMin | H/rate | 1 | 1 site: minimum speed per tick through SimulationTiming.PerSecondToPerTick(60m) minus 16 raw bits of tolerance, compared with jet.Speed | 21 |
| 203 | JetOrderTests.MissileFighterEndsItsAttack (1500 ticks) | H/budget | 1 | 1 site (not a hit): run budget 1500 ticks | 21 |
| 215 | JetOrderTests.MissileFighterEndsItsAttack (clearedAt - emptiedAt) | H/ticks | 1 | 1 site (not a hit): the order ends at most 3 ticks after the last missile | 21 |

**`tests/CnCNew.Presentation.Tests/ObjectTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 498-507 | ObjectTests.Derivation (firing states) | H/zeit | 1 | 1 site (not a hit): FIRING_A in the shot tick, then 12 steps of pause between shots (300 ms), after an empty clip 40 steps until ready (ClipReloadTime 1000 ms) | 21 |
| 600,681,693 | ObjectTests.Ghosts, SceneryFollowsTheShroud (VisionIntervalTicks, SceneryFogInterval) | H/ticks | 3 | 3 sites (not hits): waiting over SimulationWorld.VisionIntervalTicks + 1 and until world.Tick % FrameProjector.SceneryFogInterval == 0 | 21 |
| 716,861,871 | ObjectTests.ChassisFollowsMotion, RealData (loops 25/40/50) | H/budget | 3 | 3 sites (not hits): driving distances of 25, 40 and 50 ticks for tilt and steering | 21 |
| 742 | ObjectTests.TurretFollowsOrders (const step) | H/rate | 1 | 1 site: step = 6 degrees per tick, comment "TurretTurnRate = 180 degrees per second, 30 ticks per second" | 21 |
| 747,749 | ObjectTests.TurretFollowsOrders (fire command per tick) | H/ticks | 2 | 2 sites: one fire command per tick, shotTick remembers the tick of the shot | 21 |
| 759,760 | ObjectTests.TurretFollowsOrders (15 ticks) | H/zeit | 2 | 2 sites: first step = step, shot exactly 15 ticks after the command (90 degrees at 6 degrees per tick); l. 761 TurretWaits == 15, ll. 765 to 774 holding 60 and turning back 160 ticks (not hits) | 21 |

**`tests/CnCNew.Presentation.Tests/StructureConditionTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 484,493,556,572,692,719,838 | StructureConditionTests (Rig.Step, Rig.SampleOfFirst, ProductionDoorReuse, SupplyDock, BomberDoors, TrackerHousekeeping) | H/ticks | 7 | 7 sites: sample after the step = event tick + 1, counting of ticks with DOCKING_ACTIVE, DOOR_1_CLOSING directly in the following tick, sampling only every 7th tick | 21 |
| 516,520,555,571,589,605,608,620,633,659,662,669,682,711,737,759,775,786,814,833,882,912,923,934,953,980,990,1010,1012,1021,1052 | StructureConditionTests (run loops of all tests) | H/budget | 31 | 31 sites (not hits): run budgets from 3 to 12000 ticks (e.g. 260 and 360 for one door cycle, 1500 dock, 2400 repair drone, 200 cannon ready, 80 firing) | 21 |
| 529,530,533,535,541,543,612,616,624,637,791 | StructureConditionTests (ProductionDoorCycle, AirfieldDoors, RepairDrone) | H/zeit | 11 | 11 sites: Near(30, …) for DoorOpeningTime, DoorCloseTime and ConstructionCompleteDuration of 1000 ms (tolerance 2 ticks), UNPACKING at least 15 ticks | 21 |
| 534,548,562,574,576,753 | StructureConditionTests (ProductionDoorCycle, ProductionDoorReuse, UplinkCannon) | H/zeit | 6 | 6 sites (not hits): Near(60, …) for DoorWaitOpenTime 2000 ms, window completions - 32 and - 25 to - 2, Near(9, …) for RaiseAntennaTime 300 ms | 21 |
| 876,893,894,895 | StructureConditionTests.RealData (War Factory) | H/zeit | 4 | 4 sites (not hits): retail door times pinned as ticks: DoorOpeningTicks 98, DoorWaitOpenTicks 90, DoorCloseTicks 120, ConstructionCompleteTicks 45 (3250, 3000, 4000, 1500 ms; 98 by rounding up 97.5) | 21 |

**`tests/CnCNew.Presentation.Tests/TerrainTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 258 | TerrainTests.Water (UvScrollPerSecond) | H/rate | 1 | 1 site: river UV scroll -0.06 per second = 0.002 per frame times 30 | 21 |

**`tests/CnCNew.Presentation.Tests/UnitConditionTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 444,445 | UnitConditionTests.ContinuousFire (ContinuousFireCoastTicks) | H/zeit | 2 | 2 sites: ContinuousFireCoastTicks == 30 for ContinuousFireCoast 1000 ms | 21 |
| 451-464 | UnitConditionTests.ContinuousFire (shot window) | H/zeit | 1 | 1 site (not a hit): shots are fired only in the first 24 ticks, expects exactly 8 shots (DelayBetweenShots 100 ms = 3 ticks) and the stage sequence within 150 ticks | 21 |
| 477,499,510,520,535,563,575,622,637,651,675,774,847,849,867,891,938,948 | UnitConditionTests (run loops of all tests) | H/budget | 18 | 18 sites (not hits): run budgets from 5 to 1200 ticks (40 PreAttack with 500 ms lead-in, 80 per curve, 200 turret, 800/1200 jet, 320 rock, 400 Gatling with fire in the first 90 ticks) | 21 |
| 528 | UnitConditionTests.TurretRotate (6 degrees per tick) | H/zeit | 1 | 1 site: comment "+90 degrees at 6 degrees per tick"; plus l. 540 kill at step 40 and ll. 547 to 550 Take(14), Skip(16).Take(6), Skip(22).Take(18), Skip(40) (not hits) | 21 |
| 532,534,604 | UnitConditionTests.TurretRotate, UnitConditionTests.Parachute | H/ticks | 3 | 3 sites: fire command per tick with shotTick, parachute and rider arise in the same tick (order before the first update) | 21 |
| 730-736,744 | UnitConditionTests.Cheering (CheerTicks) | H/zeit | 1 | 1 site (not a hit): cheering from tick 10 to 99, over at tick 100 ("three seconds (90 ticks)"); l. 744 uses UnitConditionTracker.CheerTicks + 2 | 21 |
| 786 | UnitConditionTests.TensileFreefall | H/zeit | 1 | 1 site: FREEFALL when the rock falls more than 0.2 per tick; ll. 782 to 787 only during the first 100 updates (Life < 100) | 21 |
| 925 | UnitConditionTests.RealJetsAndBus (30 ticks) | H/budget | 1 | 1 site: 30 ticks of flight, after which JETEXHAUST must be set | 21 |

**`tests/CnCNew.Simulation.Tests/Program.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 434,438,442,460,466 | ProbeModule._nextWake / NextWake / OnSpawn / Describe / Load | H/ticks | 5 | 5 sites: test module keeps nextWake as an absolute tick (world.Tick + ProbeData.FirstWake), Update returns ProbeData.Sleep in ticks; Describe/Load write nextWake | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Acquisition.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 196 | AcquisitionFlagsAreTypedAndStrict: MoodCheckTicks == 8 / == 60 | H/rate | 1 | Addition (not a hit): MoodAttackCheckRate 250 ms -> 8 ticks, default 2 s -> 60 ticks as a fixed expectation | 21 |
| 221 | AcquisitionIdleUnitPicksTheClosestEnemyInRange: firstShot is >= 2 and <= 4 | H/ticks | 1 | Addition (not a hit): first shot 2–4 ticks after idle ("looks two frames after it became idle") | 20,21 |
| 222 | AcquisitionIdleUnitPicksTheClosestEnemyInRange | H/zeit | 1 | 1 site (loop 215: 40 ticks): expects >= 30 damage = 3 shots at 300 ms reload (9 ticks) within 40 ticks | 21 |
| 255 | AcquisitionKillsOneVictimAfterAnother: shots[3] - shots[2] <= 10 | H/zeit | 1 | Addition (not a hit): next shot at most 10 ticks after the kill (reload 9 ticks = 300 ms); 6 shots in 120 ticks | 21 |
| 283 | AcquisitionStructuresStealthAndMoods | H/zeit | 1 | 1 site: wait 30 ticks until the detected enemy is acquired and fired at (MoodAttackCheckRate 250 ms = 8 ticks); besides that 60/40/6-tick runs (263,268,278,290,294,300) | 21 |
| 333,334 | AcquisitionLeavesAnOrderedAttackAlone | H/zeit | 2 | 2 sites: 30 ticks of command pause do not end the ordered attack, 60 more do; before that 90 ticks = 9–10 shots (325-331) | 21 |
| 374 | AttackMoveStopsFightsAndMarchesOn.haltTicks | H/zeit | 1 | 1 site: counter of the halt ticks, checked with haltTicks >= 18 (line 390: two reload times of 9 ticks); loops of 400/220/500/120 ticks are budgets | 21 |
| 458 | AttackMoveSnapshotDeterminismAndReplay: new ReplayHeader(..., 30, ..., 300, 20) | H/rate | 1 | 1 site: ReplayHeader.TickRate as the literal 30; EndTick 300 and CheckpointInterval 20 in ticks | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Air.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 257,267 | JetLifecycle | H/budget | 2 | 2 sites: loop up to 900 ticks for the takeoff, StepUntil(…, 900) to the target; further budgets 2500/600/200/1500/4000 ticks in the file | 21 |
| 278 | JetLifecycle: jet.WeaponReadyTick >= SimulationTiming.SleepForever | H/ticks | 1 | 1 site: RETURN_TO_BASE weapon has ready tick >= SleepForever (sentinel, no time value) | 21 |
| 347-348 | AirRunwayQueueIdleReturnAndFullAirfield: StepUntil(…, 120, …) | H/zeit | 1 | Addition (not a hit): ReturnToBaseIdleTime 1500 ms = 45 ticks (comment), limit 120 ticks; 150 ticks of waiting for held-back production (318); 40 ticks of healing (250) | 21 |
| 377,378 | AirDestroyedAirfield | H/zeit | 2 | 2 sites: 30 ticks = 1 s, expects more than 30 health lost (OutOfAmmoDamagePerSecond 30 % of 200 = 60/s) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.AirModelGeometry.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 97 | AirAirfieldUsesTheModelBones: StepUntil(…, 900, …) | H/budget | 1 | 1 site: up to 900 ticks to the target; further budgets 200/600/1500/4000 ticks and 60 follow-up steps after the snapshot | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.AirReal.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 149,150 | RealAir.Pulse | H/zeit | 2 | 2 sites: 30 ticks after the creation of the EMP pulse EmpDisabledUntil must be > Tick + 800 ("30 seconds" = 900 ticks from real data) | 21 |
| 214 | RealAir.NukeFromSilo | H/budget | 1 | 1 site: up to 900 ticks of flight of the missile; further budgets of the file: 4000/3000/2000/6000/1500/600/300/700/12000/150/40 ticks | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.AirWeapons.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 350 | AirWeaponFixtureFiles: NeutronMissileUpdate ForwardDamping = 0.1 | D | 1 | Fixture value that NeutronMissileUpdate applies raw per tick (velocity -= ForwardDamping * velocity, AirWeaponModules.cs 597-599; ModuleConfigReader.Real without conversion) | 20,23 |
| 516 | AirEmpPulse: pulseTick = world.Tick - 1 | H/ticks | 1 | 1 site: the pulse fell in the tick before the observed one (StepUntil checks after the step) | 21 |
| 518,519 | AirEmpPulse | H/zeit | 2 | 2 sites: pulse StartFadeTime 300 ms = 9 ticks after creation (±1); DisabledDuration 2000 ms = pulseTick + 59…62; before that 6 ticks 'nothing yet', StepUntil 20, after that 20 and 100 ticks | 21 |
| 555 | AirLeafletBomb | H/zeit | 1 | 1 site: DisabledDuration 1500 ms = tick + 44 or + 45 | 21 |
| 723,725,727 | AirMissileSiloDoors | H/zeit | 3 | 3 sites: ready > Tick + 100 (ReloadTime 5000 ms = 150 ticks); Run(ready - 40) still closed, Run(ready - 20) opens (DoorOpenTime 30 ticks according to the message); in addition use + 3 / + 25 / + 70 (DoorWaitOpenTime 15 ticks, 732-738) | 21 |
| 782 | AirSpectreGunship: world.Tick - orbitStart > 20 | H/zeit | 1 | 1 site: distance measurement only 20 ticks after the start of the orbit (settling); before that 125 ticks 'the power recharges for 4 seconds' (759), loop of 300 ticks at OrbitTime 3000 ms | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Basics.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 50 | Timing: SimulationTiming.LogicFramesPerSecond == 30 | H/rate | 1 | 1 site: checks the rate value itself ("LOGICFRAMES_PER_SECOND is 30") | 21 |
| 52,55,56,58,62,63,64,65,67 | Timing: cases ms->ticks; DurationMillisecondsToTicks/ToFixedTicks, SecondsToTicks, PerSecondToPerTick, PerSecondSquaredToPerTickSquared, DegreesPerSecondToRadiansPerTick | H/rate | 9 | 9 sites: expectation table ms -> ticks ([33,1],[34,2],[999,30],[1000,30],[1001,31] …), 1000 ms = 30, 1 s = 30, 30/s = 1/tick, 900/s² = 1/tick², 180°·30/s = pi/tick | 21 |
| 73,75,80,85 | FloatDeviation: perMsec = 30.0f / 1000.0f | H/rate | 4 | 4 sites: comparison of the exact conversion with the binary32 formula of the reference (ceilf(msec * 30/1000)) over 0…5,000,000 ms; limits 4,000,000 ms and < 50,000 deviations (86-87) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.BattlePlan.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 185-209 | SpecialsBattlePlanCycle: CsRun(world, 5/40/12/2/12/40/60) | H/zeit | 1 | Addition (not a hit): after 5 ticks still Unpacking, active within 40 (AnimationTime 1000 ms); paralysed for 12 ticks, continues after 40 (BattlePlanChangeParalyzeTime 1 s) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.CleanupAndSpy.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 335 | SpecialsSpyVision: ended - start >= 44 && <= 47 | H/zeit | 1 | Addition (not a hit): capped duration MaxDuration 1.5 s = 45 ticks; observation loop of 70 ticks (328), recharging up to 200 ticks (313) | 21 |
| 372,373 | SpecialsSpyVisionUpgrades: onTicks / offTicks | H/zeit | 2 | 2 sites: counts on/off ticks over 400 ticks (1 s on, 2 s off = 90 ticks per cycle); requires cycles >= 3 and a share of 28–40 % (376-384) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Collision.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 13 | Tests.CollisionLoco (comment) | H/rate | 1 | 1 site: comment "LogicFramesPerSecond = 30 ticks/second: 300/sec = 10 world units/tick" – the fixture speed is designed for 10 units per tick | 21 |
| 170,188 | CollisionFiles: StructureToppleUpdate StructuralDecay = 0.9 / 1.0 (with StructuralIntegrity 0.5 / 0.75, lines 169/187) | D | 2 | 2 sites: fixture values that StructureToppleUpdate uses raw per tick (_structuralIntegrity *= StructuralDecay, ToppleAndFallModules.cs 316; ModuleConfigReader.Real) | 20,23 |
| 413,433 | CollisionStructureTopple | H/zeit | 2 | 2 sites: expects the impact after about 104 ticks (90 < fallen < 130), derived from 0.02·0.25 rad per tick²; sample 'in the middle of the fall' at tick == 60 (426); loops of 400/300 ticks | 21,23 |

**`tests/CnCNew.Simulation.Tests/Tests.Combat.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 298,310 | FireWeaponUpdateCadence | H/zeit | 2 | 2 sites: pulse spacings as tick differences, checked with drops[0] 18…20 (InitialDelay 600 ms) and == 9 (DelayBetweenShots 300 ms) in 60 ticks (302-303); 30 ticks of waiting for the self-damage | 21 |
| 328,333 | FireWeaponUpdateClip | H/zeit | 2 | 2 sites: 3 ticks between shots (100 ms), 30 ticks ClipReloadTime 1000 ms, 3 again; loop of 100 ticks (332-334) | 21 |
| 354,357 | FireWeaponUpdateExclusiveDelay: world.Tick < shotTick + 28 | H/zeit | 2 | 2 sites: for 28 ticks after the real shot the pulse stays silent (ExclusiveWeaponDelay 1000 ms = 30 ticks), 20 further ticks later it pulses again | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Construction.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 237 | Tests.StepUntil(world, limit, done): return tick + 1 | H/ticks | 1 | 1 site: helper counts steps until the condition and returns the step count; limit is the budget in ticks | 21 |
| 268,401 | ConstructionBuildsAStructure; ConstructionResumeWithAnotherBuilder: ConstructionWork == 60 | H/zeit | 2 | 2 sites: BuildTime 2 s = exactly 60 ticks of construction work (work == 60, site.ConstructionWork == 60) | 21 |
| 315 | ConstructionSellTimingAndPayout: sold = world.Tick - start | H/zeit | 1 | 1 site: sale duration as a tick difference, checked with sold == 179 (line 317); loop of 300 ticks | 21 |
| 381 | ConstructionRepair: ticks > 100 && ticks < 200 | H/zeit | 1 | 1 site: 10 % of 500 per second heal 200 points in about 120 ticks plus the approach; StepUntil budget 400 | 21 |
| 393,502 | ConstructionResumeWithAnotherBuilder; ConstructionSnapshotAndReplay: StepUntil(world, 30, () => false) | H/zeit | 2 | 2 sites: let 30 ticks run = 'in the middle of construction' (BuildTime 60 ticks) or in the middle of the sale; after that 260 follow-up steps | 21 |
| 517-529 | ConstructionSnapshotAndReplay: command ticks 0, 2, 3, 5, 20, 25, 40, 60, 70 | H/ticks | 1 | Addition (not a hit): replay commands with fixed tick stamps; the sequence assumes the 30 Hz progress (cancel at 20, resume 25, repair 60) | 21 |
| 531 | ConstructionSnapshotAndReplay: new ReplayHeader(..., 30, ..., 400, 50) | H/rate | 1 | 1 site: ReplayHeader.TickRate as the literal 30; EndTick 400, CheckpointInterval 50 | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.CratesAndSpecials.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 520 | CratesAndSpecialsFiles: GrantStealthBehavior RadiusGrowRate = 10.0 | D | 1 | Addition (not a hit): fixture value that GrantStealthBehavior adds raw per tick (_radius += RadiusGrowRate, SpecialsModules.cs 221) | 20,23 |
| 838 | SpecialsHijacker: (truck.X - hijacker.X).Abs() <= 2 | H/rate | 1 | 1 site: comment/expectation 'at most the distance of one tick (2 units)' = Speed 60/s ÷ 30 (check in line 839) | 21 |
| 923,925 | SpecialsSabotage | H/zeit | 2 | 2 sites: SabotageDuration 3 s = 90 ticks: nothing produced after 80 ticks, completion at the earliest 110 ticks after the sabotage; in addition PowerSabotagedUntil = hit + 59…61 (2 s, line 912) | 21 |
| 1026 | SpecialsGrantStealth: CsRun(world, 1) / CsRun(world, 1) / CsRun(world, 2) | H/zeit | 1 | 1 site: per tick the scan radius grows by RadiusGrowRate 10 (first scan 20, second 30, last 50; 1023-1030) | 21,23 |

**`tests/CnCNew.Simulation.Tests/Tests.CreationLists.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 245 | Phase5CreationLists: world.Tick - spawnedAt is >= 25 and <= 35 | H/zeit | 1 | 1 site: lifetime 1000 ms = 25…35 ticks after creation; loop bound spawnedAt + 200 (244) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.DamagedLocomotor.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 73,81 | DamagedLocomotorSlowsAReallyDamagedObject.Travel: 60 Ticks; pristine > 170 | H/zeit | 1 | Addition (not a hit): 60 ticks = 2 s of travel at Speed 90/s give > 170 units; damaged a third of that (±5) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.FiringTracker.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 11,139,172 | FiringTrackerFiles (comment); FiringTrackerSpinsUpCoastsAndCoolsDown | H/zeit | 3 | 3 sites: comment '600 ms = 18 ticks, Coast 1000 ms = 30 ticks'; delays between shots 18/9/4–5 ticks (144-147); fast stage holds 30–36 ticks, slow one exactly 31 (172-173); runs 120/20/10/90/60/100 ticks | 21 |
| 194 | PreAttackDelayWindsUpBeforeTheShot: clipGun.PreAttackDelayTicks == 15 | H/rate | 1 | Addition (not a hit): PreAttackDelay 500 ms -> 15 ticks as a fixed expectation on the weapon template | 21 |
| 215,220,224,230,231,232,236 | PreAttackDelayWindsUpBeforeTheShot | H/zeit | 7 | 7 sites: shot ticks relative to the start: PER_SHOT [15, 60], PER_ATTACK [15, 45, 75] and + 30/+ 15 after the target change at tick 100, PER_CLIP [15, 21, 27, 102, 108, 114], slot indices 15/16/30/46; runs 100/200/150/60 ticks | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Flight.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 17 | FlightFixtureIni (doc comment) | H/zeit | 1 | 1 site: comment 'drops after 1 door tick, 3 ticks between drops, sinks at 1 per tick' – the fixture is designed for 30 Hz (DoorDelay 34 ms, DropDelay 100 ms, SpeedLimitZ 30/s) | 21 |
| 530 | FlightCountermeasuresDivertMissiles: MissionOptions.Standard with { CombatIntervalTicks = 3 } | H/ticks | 1 | 1 site: mission option in ticks (combat interval 3) set by the test; then a 60-tick run in which missiles must hit | 20,21 |
| 580 | FlightParticleUplinkCannonBurnsItsTarget | H/zeit | 1 | 1 site: comment '18 ticks of beam at 100/s in 6 pulses of 10'; in addition Run 4 = beam still travelling (BeamTravelTime 200 ms = 6 ticks), Run 8 = burning, 60 ticks until the end (567-579) | 21 |
| 680 | FlightStrikePowerDeliversBombs | H/budget | 1 | 1 site: up to 900 ticks until the bombers are gone; snapshot at tick == 60, then 700 ticks in the restored run (684, 691) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.GoldenMaster.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 17,71,94 | Phase5GoldenDamageChain (Doc); Phase5GoldenProduction: completed[1] - completed[0] == 2 * 30 | H/rate | 3 | 3 sites: doc 'ms -> ticks at 30 per second: 500 ms = 15, 2000 ms = 60', doc 'BuildTime * LOGICFRAMES_PER_SECOND', expectation BuildTime 2 · 30 = 60 ticks between two completions | 21 |
| 54 | Phase5GoldenDamageChain: independent model ready += 60 / ready += 15 | H/zeit | 1 | 1 site (with 60-61): model of the shot ticks with ClipReloadTime 2000 ms = 60 and DelayBetweenShots 500 ms = 15 ticks, compared with the measured ticks; loop 400 ticks | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.JetFlight.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 129,130 | Tests.WingMinSpeed / Tests.WingTurnPerTick | H/rate | 2 | 2 sites: expected values per tick via SimulationTiming.PerSecondToPerTick(60) and DegreesPerSecondToRadiansPerTick(120); 5 uses (175,176,213,230,333) | 21 |
| 165,182,229 | WingMinimumSpeedAndNoStopping / WingCirclesItsDestination | H/budget | 3 | 3 sites: run loops 900 ticks (flight of 1000 units + measurement), 30 ticks (helicopter stays put), 900 ticks (arrive and circle) | 21 |
| 174 | WingMinimumSpeedAndNoStopping (arrived < 400) | H/zeit | 1 | 1 site (no pattern hit): arrival must be before tick 400 (1000 units at 150/s ≈ 200 ticks) | 21 |
| 176,215 | WingMinimumSpeedAndNoStopping / WingTurnRateIsLimited | H/rate | 2 | 2 sites: step per tick is checked against speed per tick (WingMinSpeed − Raw 64; PerSecondToPerTick(150) + Raw 64) | 21,23 |
| 187 | WingTurnRateIsLimited (doc comment) | H/rate | 1 | 1 site: comment «120 degrees/s, 4 degrees per tick» calculates with 30 Hz | 21 |
| 214 | WingTurnRateIsLimited (turned >= 40) | H/zeit | 1 | 1 site (no pattern hit): a U-turn needs at least 40 ticks (180° / 4° per tick = 45) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.JetFlightReal.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 36,37 | RealWingedFlight (RaptorJetLocomotor) | H/rate | 2 | 2 sites: catalog values MinSpeed/TurnRatePerTick are compared with PerSecondToPerTick(60) and DegreesPerSecondToRadiansPerTick(120) | 21 |
| 69 | RealWingedFlight.Run (tick < 4200) | H/budget | 1 | 1 site (no pattern hit): 4200 ticks for four legs of the square and the circling | 21 |
| 75-77 | RealWingedFlight.Run (tick > 20) | H/zeit | 1 | 1 site: the first 20 ticks (climbing to flight altitude) do not count for the speed/step measurement | 21 |
| 102,104 | RealWingedFlight (smallestStep, worstTurn) | H/rate | 2 | 2 sites: step and turn per tick against loco.MinSpeed/TurnRatePerTick (catalog values per tick, tolerance Raw 64 and Raw 4 respectively) | 21,23 |

**`tests/CnCNew.Simulation.Tests/Tests.LastBlockers.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 335 | Tests.LastBlockerIni: SmartBombTargetHomingUpdate.CourseCorrectionScalar = 0.8 | D | 1 | Fixture value (no pattern hit): factor per tick, read raw by the module via ModuleConfigReader.Real (LastBlockerModules.cs SmartBombHomingData) | 20,23 |
| 467,718 | Tests.LbSnapshotContinues (ticks = 120) / LastBlockersSmartBombHoming | H/budget | 2 | 2 sites: comparison length live against restored, default 120 ticks and a call with 30 (further calls with 20/40/60/100 without a hit) | 21 |
| 682 | LastBlockersSabotageInternetCenter (sabotaged.Value == 30) | H/zeit | 1 | 1 site: event value = SabotageDuration 1000 ms as 30 ticks; then CsRun 24/70 ticks as «still locked»/«free again» (685,688) | 21 |
| 717 | LastBlockersSmartBombHoming (one step = one fifth) | H/ticks | 1 | 1 site: after exactly one tick the bomb is at 20 % of the way (CourseCorrectionScalar 0.8 per tick) | 21,23 |
| 777,833 | LastBlockersCrippling… (NextWake == SimulationTiming.SleepForever) | H/ticks | 2 | 2 sites: module sleeps forever (SleepForever does not depend on the rate) | 21 |
| 784,785,824 | LastBlockersCrippling… (HealingSuppressedUntil/NextWake == hit + 30) | H/zeit | 3 | 3 sites: SelfHealSupression 1000 ms expected as «+ 30» ticks | 21 |
| 792-794 | LastBlockersCripplingBlocksAndHealsTheWarehouse (CsRun 100, Health 300..350) | H/zeit | 1 | 1 site (no pattern hit): after 100 ticks the healing lies between 300 and 350 («5 every 15 ticks», SelfHealDelay 500 ms); then CsRun 600 as a bound | 21 |
| 908,953,965 | LastBlockersFerry… (CsRun 900 / 900 / 30) | H/budget | 3 | 3 sites: upper bounds with abort condition for the ferry trip (900) and 30 ticks «never sets off» | 21 |
| 1020 | RealLastBlockers (hash sequence of two runs) | H/ticks | 1 | 1 site: two runs have the same hash in every tick (150 + 300 ticks, no pinned hash) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Navigation.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 166,300,335,395,410 | Tests.NavRun (limit) / NavigationMovement / NavigationRepathsAfterConstruction | H/budget | 5 | 5 sites: NavRun counts ticks until the end of the command; upper bounds 900, 30, 900, 900 (further 400s without a hit) | 21 |
| 294,310 | NavigationMovement (turnLimit = 0.0523599 rad) | H/rate | 1 | 1 site: turn limit per tick as the literal 0.0523599 rad = 3° per tick = TurnRate 90°/s ÷ 30 (+ Raw 64) | 21 |
| 312 | NavigationMovement (ticks > 150 / 2) | H/zeit | 1 | 1 site (no pattern hit): the detour takes longer than the straight line – 150 units at 2 units per tick (60/s ÷ 30) | 21 |
| 390-391 | NavigationRepathsAfterConstruction (36 ticks, y 26..50) | H/zeit | 1 | 1 site (no pattern hit): after 36 ticks the walker is still north of the future wall | 21 |
| 444 | NavigationSnapshotsAndDeterminism | H/ticks | 1 | 1 site: two runs have the same hash in every tick (40 ticks, then 400) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.NavigationEscape.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 36 | NavigationFootprintEscape (NavRun 900) | H/budget | 1 | 1 site: upper bound 900 ticks until arrival | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Phase12Modules.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 18 | Phase12ModulesCreateAndBehave (MissionOptions.CombatIntervalTicks = 0) | H/ticks | 1 | 1 site: mission option in ticks set to 0 (switches off self-acquisition, MissionSession.cs:78); the other numbers of the line are coordinates | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Phase13Actions.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 159 | ScriptAiExpandsToASupplyWarehouse (Run 900) | H/budget | 1 | 1 site: 900 ticks in which the dozer must have finished building the depot and the guard must have set off | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Phase5.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 627-628 | Phase5StealthAndHeal (35 ticks, StealthDelay) | H/zeit | 1 | 1 site: after 35 ticks it is stealthed – «1000ms = 30 ticks»; then 90 ticks «never stealths» and 10 ticks of healing | 21 |
| 751,832,851,1003,1026,1042,1082,1098 | Phase5Flammable / Phase5SlowDeath / Phase5Poisoned / Phase5BaseRegenerate (world.Tick - 1) | H/ticks | 8 | 8 sites: anchor tick of the event = world.Tick − 1 (the tick just executed) | 21 |
| 753,754,757,758,762,763 | Phase5Flammable (igniteTick + 3 / + 6 / + 9) | H/zeit | 6 | 6 sites: AflameDamageDelay 100 ms = 3, BurnedDelay 200 ms = 6, AflameDuration 300 ms = 9 ticks, with exact health values per stage | 21 |
| 836,838,839,841,853,855 | Phase5SlowDeath (deathTick + 14 / + 15) | H/zeit | 6 | 6 sites: DestructionDelay 500 ms = 15 ticks, «still there one tick earlier» (+ 14) and «gone exactly then» (+ 15), twice | 21 |
| 937,945 | Phase5StealthDetector (20 ticks, expiry = Tick + 16) | H/zeit | 1 | 1 site: DetectionRate 500 ms = 15 ticks plus random phase (wait 20 ticks); DETECTED expires after UpdateRate + 1 = 16 ticks | 21 |
| 1005,1006,1009,1012,1014,1018,1034,1045 | Phase5Poisoned (doseTick + 3 / 12 / 15 / 18, + 20) | H/zeit | 8 | 8 sites: PoisonDamageInterval 100 ms = 3 ticks, PoisonDuration 500 ms = 15 ticks; exact health at +3, +12, +15, +18 and after healing +20 | 21 |
| 1085,1086,1090,1092,1105 | Phase5BaseRegenerate (hitTick + 29 / 30 / 33, + 30 + 3 * 40) | H/zeit | 5 | 5 sites: BaseRegenDelay 1000 ms = 30 ticks; healing bursts at +30 and +33 (every three ticks) with exact values 712.5 and 725 | 21,23 |
| 1089 | Phase5BaseRegenerate (comment 1000 * 12.5% * 3 / 30) | H/rate | 1 | 1 site: expectation calculation with the 30 in the denominator: 12.5 points per burst of three ticks | 21,23 |
| 1153 | Phase5Garrison (TimeForFullHeal 300 ms = 9 ticks) | H/zeit | 1 | 1 site: after exactly 9 ticks the occupant is fully healed (50/9 per tick), loop in line 1154 | 21 |
| 1230,1233 | Phase5Transport (HealthRegen%PerSec 30 = 0.6 per tick) | H/rate | 2 | 2 sites: 18/s as 0.6 per tick; after 10 ticks the health lies between 30 and 60 | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.PlayerAgent.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 30,67,74 | ScriptCommandsOnlyDriver… (maxTicks: 900) | H/budget | 3 | 3 sites: run budget of the objective driver 900 ticks (further calls with 6000 without a hit, lines 20 and 56) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.PowerPlant.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 152 | OverchargeTogglesDrainsAndSwitchesItselfOff (two steps) | H/ticks | 1 | 1 site: after two ticks health has been deducted («drains health every tick») | 21 |
| 156-158 | OverchargeTogglesDrainsAndSwitchesItselfOff (40 ticks) | H/zeit | 1 | 1 site (no pattern hit): after 40 ticks 30 %/s has pushed the plant below 90 % (10 % = 10 ticks) and it switches off | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Projectiles.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 266,325,353,373 | ProjectileStraightShellFlightTime / ProjectileMissileFlightTime / ProjectileMissileAcceleration / ProjectileDelayedHit (doc) | H/rate | 4 | 4 sites: hand-calculated conversions in the comments: 300/s = 10 per tick, 9000/s² = 10 per tick², 900/s² = 1 per tick², 60/s = 2 per tick, 600/s = 20 per tick, 100 ms = 3 ticks | 21 |
| 279-288 | ProjectileStraightShellFlightTime (Segments == 20, impact F + 21) | H/zeit | 1 | 1 site (hit 281): 200 units at 10 per tick = 20 trajectory points, no impact before F + 21, detonation and FX exactly in F + 21 | 21,23 |
| 305,311,319 | ProjectileBallisticArc (Segments == 14, F + 6, F + 15) | H/zeit | 1 | 1 site (no pattern hit): 14 trajectory points, point 5 at x = 44.515/z = 21.302 in tick F + 6, landing in F + 15 | 21,23 |
| 308 | ProjectileBallisticArc (world.Tick < fire + 30) | H/budget | 1 | 1 site: search bound 30 ticks after the shot (missile: fire + 20 in line 339) | 21 |
| 336,344,349 | ProjectileMissileFlightTime (x == 10 after F + 1, detonation F + 10, removed F + 13) | H/zeit | 1 | 1 site (no pattern hit): ignition immediately, 10 per tick, impact F + 10; KillSelfDelay fallback value 3 ticks (ProjectileModules.cs:341) removes the missile in F + 13 | 21,23 |
| 363-370 | ProjectileMissileAcceleration (expected = [2, 4, 7, 11, …, 96], F + 15) | H/rate | 1 | 1 site (hit 367): table of the x positions per tick from Euler steps with 2 per tick initial speed and 1 per tick² acceleration, landing F + 15 | 21,23 |
| 379-387 | ProjectileDelayedHit (hit in F + 8) | H/zeit | 1 | 1 site (no pattern hit): 150 units at 20 per tick land ceil(7.5) = 8 ticks later | 21,23 |
| 390-393 | ProjectileDelayedHit (below one tick of flight distance immediately) | H/rate | 1 | 1 site: 15 units are less than one tick of flight distance (20 per tick), the damage falls in the tick of the shot | 21,23 |

**`tests/CnCNew.Simulation.Tests/Tests.RealFlight.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 87 | RealFlightAndTransports.Run (tick < 1800) | H/budget | 1 | 1 site: 1800 ticks of run time for projectiles, Chinook delivery and tunnel; evacuation at tick == 10 | 21 |
| 117,118,124 | RealFlightAndTransports (impact > fire + 1; hash sequence) | H/ticks | 3 | 3 sites: projectile and missile fly longer than one tick; two runs have the same hash in every tick | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.RealMovement.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 45 | RealSquadPassesAStandingDozer.Run (tick < 1800) | H/budget | 1 | 1 site: upper bound 1800 ticks until all eight Rangers have arrived | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.ReferenceMatch.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 168 | ReferenceMatch (ReferenceMatchOptions: MaxTicks 60_000, MinTicks 62_000, CheckpointInterval 500, GraceTicks 100) | H/budget | 1 | 1 site: run budget in ticks; line 172 expects EndTick == 62_000; CheckpointInterval 500 is tick semantics, GraceTicks 100 a deadline in ticks | 21 |
| 200 | ReferenceMatch (TickHashes of two runs) | H/ticks | 1 | 1 site: two playbacks have the same hash in every tick (no pinned hash in this file) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Repair.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 294 | RepairPlayerTypeReplayHeader (new ReplayHeader(…, 30, …)) | H/rate | 1 | 1 site: literal 30 as ReplayHeader.TickRate (third parameter, ReplayFormat.cs:20) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.RetailModules.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 265 | RetailProductionExits (defaultTicks[n+1] - defaultTicks[n] == 30) | H/zeit | 1 | 1 site: units leave the factory at intervals of the BuildTime 1 s = 30 ticks (SpawnTicks budget 400) | 21 |
| 326 | RetailHealContain (healing in the tick of entry) | H/ticks | 1 | 1 site: already in the tick of entry the health is above 20 | 21 |
| 327-328 | RetailHealContain (12 ticks until full) | H/zeit | 1 | 1 site (no pattern hit): after 12 ticks the occupant is fully healed within TimeForFullHeal and has exited | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Scripting.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 555,582,598,603,605 | ScriptEngineDamageHistoryAreasAndPaths / …AttackPrioritiesAndReinforcements / …SnapshotContinuesIdentically | H/budget | 5 | 5 sites: mission run 900 ticks (walk the path), 30 ticks (rifleman hits), three times 900 ticks for continuation and repetition comparison (hashes only against each other) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.ScriptingActions12.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 43 | ScriptTeamCommandButtons (MissionOptions.CombatIntervalTicks = 0) | H/ticks | 1 | 1 site: mission option in ticks set to 0 (self-acquisition off); the other numbers of the line are coordinates | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.ScriptingPhase11.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 110 | ScriptMapRevealAndShroud (Run 30) | H/zeit | 1 | 1 site: 30 ticks must contain at least one periodic vision refresh («survives the periodic vision refresh») | 21 |
| 261 | ScriptSequentialScripts (sequence in one tick) | H/ticks | 1 | 1 site: a unit sequence runs through its instructions in one tick (after Run 3) | 21 |
| 373 | ScriptTeamEventScripts (Run 30) | H/budget | 1 | 1 site: 30 ticks in which no script runs a second time (further runs 400/3000/200 without a hit) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.ScriptingPhase12.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 61 | ScriptExplainLeavesTheEngineUntouched | H/ticks | 1 | 1 site: an explanation in each of the three ticks does not change the hash | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Skirmish.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 236,246 | SkirmishAiPlaysAMatch (RecordCheckpointInterval = 500; TickHashes) | H/ticks | 2 | 2 sites: replay checkpoints every 500 ticks (also VerifyIncrementalEvery 500), two runs with the same hash in every tick; SkirmishRecord(12_000) is the budget | 21 |
| 263-264 | SkirmishAiOnlyUsesWhatItSees (SkirmishRecord(60)) | H/zeit | 1 | 1 site: 60 ticks stand for «2 s of game time», in which a scout with 60 units/s does not survey the 400 units | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Slaves.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 660 | SlavedScoutDroneGrantsSpottingBonus (tick % 12 == 0) | H/zeit | 1 | 1 site: the test fires every 12 ticks (DelayBetweenShots 300 ms = 9 ticks), upper bound 200 ticks, then StepN 150 | 21 |
| 782,795 | PropagandaTowerBuffsAlliesInRange (one point per tick) | H/rate | 2 | 2 sites: HealPercentEachSecond 30 % of 100 = «one a tick»; gain in 20 ticks expected between 15 and 25 | 21 |
| 970 | AssaultTransportDeploysItsTroops (StepN 30) | H/budget | 1 | 1 site: 30 ticks in which the dropped soldier gets closer to the target | 21 |
| 993,1000,1002 | TensileFormationSlidesDownTheSlope (200 + 250 ticks, then 30) | H/zeit | 1 | 1 site (hit 1002): after 450 ticks the formation must be at rest («stops after 300 ticks»), 30 further ticks without movement | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.SpecialPower.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 312,453 | SpecialPowerUse (airdrop.ReloadTicks == 300) / SpecialPowerWiring (fallback.ReloadTicks == 30) | H/zeit | 2 | 2 sites (312 no pattern hit): ReloadTime 10,000 ms = 300 ticks and DefaultSpecialPower 1,000 ms = 30 ticks expected as a fixed tick count | 21 |
| 340,380,409,422,437 | SpecialPowerUse / SpecialPowerWiring (usedAt = Tick - 1; + 3; one tick after the spawn) | H/ticks | 5 | 5 sites: anchor tick world.Tick − 1, three ticks for the command processing of an upgrade purchase, «SpecialPowerCreate runs one tick after the spawn» | 21 |
| 359,443,454 | SpecialPowerUse / SpecialPowerWiring (StepTo(Tick + ReloadTicks)) | H/zeit | 3 | 3 sites: waiting time from the catalog value ReloadTicks (also 330 and 342: reload − 1 with the text «tick 299») | 21 |
| 365,382,418,469,475,484 | SpecialPowerUse / SpecialPowerWiring / SpecialPowerCash (StepTo + 100 / + 61 / + 130 / + 40) | H/zeit | 6 | 6 sites: waiting times just above the reload time: Edge 3000 ms (+ 100), Paused 2000 ms (+ 61), Gunship 4000 ms (+ 130), Hack 1000 ms (three times + 40) | 21 |
| 375,500,508 | SpecialPowerUse / SpecialPowerCash (StepTo + 400 / + 30) | H/budget | 3 | 3 sites: 400 ticks «paused never becomes ready», twice 30 ticks until the shot has killed the target | 21 |
| 541 | SpecialPowerSnapshot (new ReplayHeader(…, 30, …)) | H/rate | 1 | 1 site: literal 30 as ReplayHeader.TickRate (third parameter, ReplayFormat.cs:20) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.StagedCapture.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 22-25 | Tests.StagedUnpack/StagedPrepare/StagedPack | H/zeit | 4 | 4 sites: constants 30/60/15 ticks = UnpackTime 1000 ms, PreparationTime 2000 ms, PackTime 500 ms; checked as exact stage lengths (ll. 176-178, 238, 270, 338) | 21 |
| 153,165,267 | Tests.StagedWorld / StagedCaptureRunsThroughItsStages / StagedCaptureAbortRules | H/zeit | 3 | 3 sites (not in the hit list): 40 or 35 steps wait out the charging of 1000 ms (30 ticks); StagedPack + 10 as observation window | 21 |
| 170,210,235,255,278,287,297,352,365,376,413 | Tests.ObserveCapture calls / StagedWorldAt / RealStagedCapture | H/budget | 11 | 11 sites (not in the hit list): run budgets 600/400/200/300/50/2400 ticks | 21 |
| 184 | Tests.StagedCaptureRunsThroughItsStages | H/ticks | 1 | 1 site: change of owner in the first pack tick (captured.Tick == pack.Start), pure order | 21 |
| 197,198 | Tests.StagedCaptureRunsThroughItsStages | H/zeit | 2 | 2 sites: StagedPack + 30 = reload time ReloadTime 1000 ms as the literal 30 ticks | 21 |
| 222,223 | Tests.StagedCaptureFlashesTheBuilding | H/zeit | 2 | 2 sites: exactly 4 CaptureFlash events over 60 preparation ticks (phase grows per tick by fraction/3: 59*60/2/60/3 = 9.83) | 21,23 |
| 242 | Tests.StagedCaptureFacesItsTargetAndLotusStartsFromAfar | H/zeit | 1 | 1 site: unpack.Start >= 8 (90 degrees at 360 degrees/s = 7.5 ticks) | 21 |
| 425-427,438,439 | Tests.RealStagedCapture | H/zeit | 5 | 5 sites (not in the hit list): expectation table of retail data in ticks: 90/600/60 (Ranger, Redguard, Rebel) and 202/180/84 (Black Lotus: 6730/6000/2800 ms, rounded up) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.State.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 22,61,95,98,135,155,262,271,338,341,425,430 | Tests.ShortOptions / HashDeterminism / SnapshotRoundTrip / SnapshotRejection / ReplayRuns / ShortStress / IdleAllocation / RealData | H/budget | 12 | 12 sites (not in the hit list): run lengths and stop ticks 3000/60/1500/600/1234/12000/100/300/400, plus "tick":600 in the snapshot text | 21 |
| 126 | Tests.SnapshotRoundTrip | H/budget | 1 | 1 site: 30 steps of hash comparison after restore | 21 |
| 153 | Tests.SnapshotRejection | H/rate | 1 | 1 site: snapshot text is rewritten from "tickRate":30 to "tickRate":60 and must be rejected (literal 30 and 60) | 20,21 |
| 221 | Tests.ReplayFormatTests | H/rate | 1 | 1 site: replay header with TickRate = 60 counts as a foreign rate and must raise ReplayRejectedException | 20,21 |
| 315,323,325 | Tests.LongRun | H/budget | 3 | 3 sites (not in the hit list): 216,000 ticks as 'two hours of game time', expectation TickHashes.Length == 216_000, stop at 108,000 | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.StructuresAndWorld.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 253-260,279-281 | Tests.StructuresFixtureIni: RailroadBehavior SpeedMax/Acceleration/Braking/Friction/RunningGarrisonSpeedMax/KillSpeedMin | D | 9 | 9 sites: SpeedMax 3, Acceleration 1.1, Braking 0.9, Friction 0.995, RunningGarrisonSpeedMax 1, KillSpeedMin 1 are read raw per frame (RailroadModules.cs ll. 33-35, ModuleConfigReader.Real) | 20,23 |
| 735,746,769,785,786,788,826,835,869,870,880,891,913,927,939,948,952,1045,1097,1100,1104,1223,1225,1256,1257,1260,1314 | Tests.Structures* methods (run budgets) | H/budget | 27 | 27 sites (not in the hit list): loop bounds and follow-on runs 2..700 ticks without an exact time statement (e.g. 200 collapse, 700/400/260/120 train, 250 restore comparison) | 21 |
| 813,838,839,843,849,852,854,995,1000,1050-1052,1072,1074,1078,1087,1089,1091,1112,1117,1138,1141,1145,1154,1156,1160,1162,1177,1181,1214,1219,1221 | Tests.Structures* methods (Run(n) waiting times) | H/zeit | 32 | 32 sites (not in the hit list): sw.Run(n) as time: 40 (Spread 500 ms), 20/2/12 (UnpackTime 30 ticks), 8/25 (PackTime 15), 40 (Delay 1000 ms), 10/40/40 (OCL 1000 ms), 45/60 (Shroud), 60/90/60/100 (Firestorm), 40/25 (Bonus), 60/5/20/80 (WaveDelay 3000 ms) | 21 |
| 909,910 | Tests.StructuresTrainsRunTheirTrackAndHaulCarriages | H/rate | 2 | 2 sites (not in the hit list): maxSpeed <= 3 and > 2 compares the speed per tick with the raw value SpeedMax | 21,23 |
| 998,1115,1179 | Tests.StructuresMinesDetonateOnceAndRegenerate / StructuresWorldEffects / StructuresWeaponBonusAndCostModifier | H/zeit | 3 | 3 sites: waiting times 100 (healing 25 per s), 30 (shrink phase after GrowTime 1000 ms + ShrinkDelay 2000 ms), 30 (BonusDuration 1000 ms) | 21 |
| 1015,1017 | Tests.StructuresMinesDetonateOnceAndRegenerate | H/rate | 2 | 2 sites: Run(30 * 3) and Run(30 * 8) = 3 s and 8 s with the literal 30 as ticks per second (CreatorDeathCheckRate 1000 ms, degeneration 20 % per s) | 21 |
| 1070 | Tests.StructuresBridgesTowersAndScaffolds: BridgeScaffoldBehavior.SetSpeeds | H/rate | 1 | 1 site (not in the hit list): SetSpeeds(4, 3) passes speeds per tick directly (the module otherwise divides data values by LogicFramesPerSecond) | 21 |
| 1317,1337 | Tests.RealStructuresAndWorld | H/budget | 2 | 2 sites: 900 ticks after the kill (collapse must be finished), 450 ticks of train travel | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Supply.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 282,302,318,372,387,407,417,438,441,450,453,459,466 | Tests.Supply*/SpawnBehavior* methods | H/budget | 13 | 13 sites (not in the hit list): run budgets 4000/600/3000/130/400/300/40/200/4 ticks | 21 |
| 287 | Tests.SupplyEconomyGoldenMaster | H/zeit | 1 | 1 site: distance between two deposits > 6 ticks (two action delays of 100 ms = 3 ticks each) | 21 |
| 356,358,360,367 | Tests.SupplyAutoDeposit | H/zeit | 4 | 4 sites (356 hit): absolute ticks 30/31/61/91 as period boundaries of DepositTiming 1000 ms | 21 |
| 396 | Tests.SupplySnapshotAndReplay | H/rate | 1 | 1 site: ReplayHeader with tick rate literal 30 | 20,21 |
| 431,433 | Tests.SpawnBehaviorReplaceAndRequireSpawner | H/zeit | 2 | 2 sites: deathTick + 70 (replacement not before SpawnReplaceDelay 2000 ms = 60 ticks plus 15-tick polling cadence) and + 140 | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Teams.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 101,118 | Tests.TeamsRelationshipMatrix / KillPrize | H/budget | 2 | 2 sites (101 hit): 30 steps of hash comparison, 200 steps until the prize dies | 21 |
| 105 | Tests.TeamsRelationshipMatrix | H/rate | 1 | 1 site: ReplayHeader with tick rate literal 30 | 20,21 |

**`tests/CnCNew.Simulation.Tests/Tests.Terrain.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 130 | Tests.TerrainInWorldSnapshotAndReplay | H/rate | 1 | 1 site: ReplayHeader with tick rate literal 30; the replay is then executed (TicksExecuted == 10) | 20,21 |

**`tests/CnCNew.Simulation.Tests/Tests.TimeFreeze.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 20,41,46 | Tests.ScriptFreezeTime* | H/budget | 3 | 3 sites (not in the hit list): loop bounds 200/5/20 steps | 21 |
| 27 | Tests.ScriptFreezeTimeStopsTheWorldNotTheScripts | H/zeit | 1 | 1 site: steps between 28 and 40 for SET_MILLISECOND_TIMER with 1 s ('its 30 script updates') | 21 |
| 28,31,58 | Tests.ScriptFreezeTimeStopsTheWorldNotTheScripts / ScriptFrozenTimeSurvivesSaveAndEnds | H/ticks | 3 | 3 sites (58 hit): Tick == frozenAt + 1 / + 2 / tick + 1: one step further after the thaw | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Transports.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 30-33,37-40 | Tests.TransportFiles: Locomotor WalkLoco/RotorLoco | H/rate | 8 | 8 sites (37 not in the hit list): fixture values per second chosen as multiples of 30/900: Speed 30 and 150 (1 and 5 per tick respectively), Acceleration/Braking 900 (1 per tick²), TurnRate 3600 | 21 |
| 243,244,262,264 | Tests.TunnelHealingAndCaveIn | H/zeit | 4 | 4 sites (243, 244 hits): TimeForFullHeal 1000 ms = 30 ticks: not healed after 6 ticks, healed after 5 + 25 more | 21 |
| 260 | Tests.TunnelHealingAndCaveIn | H/rate | 1 | 1 site: sliver = 100 / 30 as healing amount per tick and tunnel (literal 30 = ticks in 1000 ms) | 21,23 |
| 290,306,311,338,363,393,425,449,451,465-468 | Tests.Tunnel*/Transport*/Chinook*/Helix* methods | H/budget | 13 | 13 sites (not in the hit list): loop bounds 200/300/2000/100/240 ticks, command and snapshot times at tick 120/100/130 | 21 |
| 307,371,386 | Tests.TransportLoadAndUnloadAtPosition / ChinookSupplyFlightAndLanding / HelicopterSlowDeathFall | H/zeit | 3 | 3 sites (not in the hit list): world.Tick > 90 (100 units at 1 per tick), 25 ticks of climbing back to 100, 20 ticks until flight altitude | 21 |
| 317,328,329,331 | Tests.ChinookSupplyFlightAndLanding | H/rate | 4 | 4 sites (317, 329 hits): climbing at 5 per tick: after 19 steps Z == 95, after 20 steps Z == 100 | 21,23 |
| 376-378,398,405 | Tests.HelicopterSlowDeathFall | H/zeit | 5 | 5 sites (376 hit): hand-calculated: gravity -64/s² = -4660 raw per tick², fall distance 2330 * n(n+1)/2, impact at death + 75; DelayFromGroundToFinalDeath 300 ms = 9 ticks, end at hit + 10 | 21,23 |
| 400 | Tests.HelicopterSlowDeathFall | H/budget | 1 | 1 site: loop bound 30 ticks until it disappears (expected after 10) | 21 |
| 403 | Tests.HelicopterSlowDeathFall | H/ticks | 1 | 1 site: gone = world.Tick - 1 (the tick just executed) | 21 |
| 470 | Tests.TransportSnapshotAndReplay | H/rate | 1 | 1 site: ReplayHeader with tick rate literal 30 | 20,21 |

**`tests/CnCNew.Simulation.Tests/Tests.TurretAttack.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 174,178,189,199,210,243,270,290,304,313,326,364,390,399,426,430-433,440,447-449,461,462,482,510,523,526,530,537,545,554,556,564,567,570,573,650,662 | Tests.TurretAttack*/TurretIdleScan* methods | H/budget | 42 | 42 sites (not in the hit list): loop bounds 10..1500 ticks, command times in ticks (tick 3, 60..90, 150, 260; replay 70..110, 130), 'from tick 10/60' for the aiming error measurement, 'i > 140' | 21 |
| 218,222,225,231,242,249,258,259 | Tests.TurretAttackSurvivesGapsChaseAndAnOrderEndsIt / TurretAttackEndsWhenTheVictimDiesOrIsLost | H/zeit | 8 | 8 sites (259 hit): RecenterTime 500 ms as 16 hold ticks, TicksUntilShot == 15 (90 degrees at 6 degrees per tick), 40 or 90 ticks of turning back at half rate (3 degrees per tick) | 21 |
| 261,278,297 | Tests.TurretAttackEndsWhenTheVictimDiesOrIsLost | H/zeit | 3 | 3 sites (not in the hit list): giving up the target 44..47 steps after it leaves the range ('loses interest after 45 ticks', constant of the simulation) | 20,21 |
| 359,385,626,627,630 | Tests.TurretAttackEndsWhenTheVictimIsNoLongerVisible / TurretAttackEndsWhenTheVictimFallsIntoTheFog / RealTurretAttackAndScan | H/budget | 5 | 5 sites: 30 steps 'while visible', Idle(name, 1800) = 60 s of idling with expectation Scans >= 3 | 21 |
| 464 | Tests.TurretAttackSnapshotAndReplay | H/rate | 1 | 1 site: ReplayHeader with tick rate literal 30; the replay is executed | 20,21 |
| 476,514,516,518 | Tests.TurretIdleScanGlancesAroundDeterministically | H/zeit | 4 | 4 sites (476, 516 hits): scan interval 30..60 ticks (1000..2000 ms), hold time 15..17 ticks (RecenterTime 500 ms), excursion at most 31 + 16 + 31 ticks (90 degrees at 3 degrees per tick) | 21 |
| 599 | Tests.RealTurretAttackAndScan | H/rate | 1 | 1 site (not in the hit list): MinScanIntervalTicks == 8 and MaxScanIntervalTicks == 8 (250 ms rounded up) | 21 |
| 660 | Tests.RealTurretAttackAndScan.Chase | H/ticks | 1 | 1 site: fire command only in every second tick (tick % 2 == 0) as the input cadence of the test | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Turrets.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 18,295,306,308,345,356,388,395,401 | Tests (class comment) / TurretsTurnAtTheirRateAndTheShotWaits / TurretsHoldRecentreAndRest / TurretsTwoTurretsPitchAndSweep | H/zeit | 9 | 9 sites (18, 295, 306, 388, 401 hits): 180 degrees/s = 6 degrees per tick, 500 ms = 15, 300 ms = 9 ticks; shotAfter == 15, TurretWaits == 15, 22 hold steps, 8..10, 3..4, >= 30 ticks | 21 |
| 236 | Tests.TicksUntilShot | H/ticks | 1 | 1 site: helper orders a shot per tick and returns the number of ticks until the shot (limit 80) | 21 |
| 261 | Tests.TurretsTemplateModelAndStrictParsing | H/rate | 1 | 1 site: TurnRate.Raw between 6860 and 6866 ('180 are 6 degrees per tick') next to the comparison via the conversion function | 21 |
| 262,265,266 | Tests.TurretsTemplateModelAndStrictParsing | H/rate | 3 | 3 sites (266 hit): RecenterTicks == 15 (500 ms), defaults TurnRate/PitchRate == 0.01 rad per frame and RecenterTicks == 60 (two seconds) from TurretInfo.DefaultRate | 20,21 |
| 304 | Tests.TurretsTurnAtTheirRateAndTheShotWaits | H/ticks | 1 | 1 site: after one tick the angle is exactly one rate per tick (via the conversion function) | 21 |
| 349,358,366,374,376,415,422,428-430,486 | Tests.Turrets* methods | H/budget | 11 | 11 sites (not in the hit list): loop bounds 100/200/4/10/120/7/150/90 ticks, condition world.Tick < 60 | 21 |
| 500,504,512,514 | Tests.RealTurrets.Duel | H/zeit | 4 | 4 sites: ticks since start until the first shot; Crusader 7..9 ticks (45 degrees at 6 degrees per tick), Battlemaster 0 | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Veterancy.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 179 | Tests.KillDummy | H/budget | 1 | 1 site (not in the hit list): at most 200 steps until the target dies | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Vision.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 112 | Tests.VisionSettle | H/ticks | 1 | 1 site (not in the hit list): runs SimulationWorld.VisionIntervalTicks (= 3) steps until vision is recomputed | 21 |
| 225 | Tests.VisionSnapshotAndDeterminism | H/budget | 1 | 1 site: 30 steps of hash comparison after restore (plus 20 and 5 steps in ll. 213, 219) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.WeaponSlots.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 16,331,337,338,409,427,430,431,438,439,440,453-457,496 | Tests (class comment) / WeaponSlotsBothSlotsFireAtDifferentTargets / WeaponSlotsPreferredWeaponWaitsWhileReloading / WeaponSlotsIndependentClipsAndReload / WeaponSlotsSharedReloadTime / WeaponSlotsExplicitSlotAndWeaponLock | H/zeit | 17 | 17 sites (16, 438 hits): '30 ticks per second: 500 ms = 15, 100 ms = 3, 200 ms = 6, 2000 ms = 60, 3000 ms = 90'; ReadyTick == start + 15, + 1 + 3, + 15 + 60, Tick - 1 + 6, + 3 + 15; wait loops until start + 15 / + 75; 15 steps | 21 |
| 334 | Tests.WeaponSlotsBothSlotsFireAtDifferentTargets | H/ticks | 1 | 1 site: 'One tick later' – second command in the next step | 21 |
| 551,602,603 | Tests.WeaponSlotsSnapshotMidReloadAndDeterminism / WeaponSlotsCommandsRoundTripInReplays | H/ticks | 3 | 3 sites: input pattern of the test via tick modulo (Tick % 40 == 10, tick % 20 == 7, tick % 3, tick % 5) | 21 |
| 556,564-566,600,604,605,687,690 | Tests.WeaponSlotsSnapshotMidReloadAndDeterminism / WeaponSlotsCommandsRoundTripInReplays / WeaponSlotsScriptCommandButtonsHonourTheSlot | H/budget | 9 | 9 sites (not in the hit list): run lengths 40/200/150 ticks, commands at tick 60 and 100, Run(session, 3) | 21 |
| 608,620 | Tests.WeaponSlotsCommandsRoundTripInReplays | H/rate | 2 | 2 sites: ReplayHeader with tick rate literal 30; the first replay is executed | 20,21 |
| 683 | Tests.WeaponSlotsScriptCommandButtonsHonourTheSlot | H/ticks | 1 | 1 site: MissionOptions.CombatIntervalTicks = 0 (option in ticks; the value 0 switches the interval off) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.WeaponSlotsIntegration.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 175,186,205,207,223,236,257,263,275,285,304,376 | Tests.WeaponSlots* methods (integration) | H/budget | 12 | 12 sites (not in the hit list): loop bounds 200/40/100/20/60/3/10 ticks | 21 |
| 255,310 | Tests.WeaponSlotsFireOclAfterCooldownWatchesTheSecondarySlot / WeaponSlotsAssistedTargetingUsesTheRealSlot | H/zeit | 2 | 2 sites (not in the hit list): 12 steps until the weapon could have fired again (DelayBetweenShots 300 ms = 9 ticks); delays between shots == 3 and == 15 ticks | 21 |
| 272 | Tests.WeaponSlotsFireOclAfterCooldownWatchesTheSecondarySlot | H/ticks | 1 | 1 site: one step, because the observer counts the shot one tick later | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.WeaponSlotsReal.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 74,80,98 | Tests.RealWeaponSlots run | H/budget | 3 | 3 sites (not in the hit list): at most 40 or 120 fire attempts, 20 steps of pause between the targets | 21 |
| 105 | Tests.RealWeaponSlots run | H/ticks | 1 | 1 site: ReadyTick > world.Tick - 1 (relative to the tick just executed) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.World.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 151,212,291,303,387,417,443,492,507,529 | Tests.Lifecycle / BodyAndDieModules / LifetimeAndDeletion / UpdateOrderAndMutation / TimerBehaviour / CommandValidationAndOrder | H/ticks | 10 | 10 sites (212, 291, 303, 417, 492, 507, 529 hits): scheduler semantics: Step executes tick 0, command for Tick + 1, lifetime 0 becomes one tick (removed.Tick == 1), update order (wake tick, id, module index), timers by (due time, id), late tick | 21 |
| 275,277-279 | Tests.LifetimeAndDeletion | H/zeit | 4 | 4 sites (275 hit): reference draw reference.Range(15, 45) and Range(9, 27) = 500..1500 ms and 300..900 ms in ticks; time of death must match exactly | 21 |
| 281 | Tests.LifetimeAndDeletion | H/budget | 1 | 1 site (not in the hit list): loop until tick 60 covers the longest lifetime (45 ticks) | 21 |
| 305 | Tests.LifetimeAndDeletion | H/rate | 1 | 1 site (not in the hit list): DieTick == 128849019 (-1 as 4294967295 ms, rounded up to ticks at 30 Hz) | 21 |
| 328-366,384,395,407,438-442,444-476 | Tests.ProbeWorld / UpdateOrderAndMutation / TimerBehaviour | H/ticks | 1 | About 30 sites (not in the hit list): probe fixture FirstWake/Sleep/DestroyAtTick/EmitAtTick and timer delays (5, 3, 4, 2, 50) are deliberately ticks of a test module or of the SetTimerCommand | 21 |

**`tests/CnCNew.Ui.Tests/TextAndSettingsTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 276,277 | TextAndSettingsTests.InGameUiSettingsAreTypedFromTheGraph (SuperweaponFlashFrames, FloatingTextTimeOutFrames, FloatingTextMoveUpPerFrame, FloatingTextVanishPerFrame) | H/zeit | 2 | 2 sites: expects 500 ms = 15 logic frames, 333 ms = 10 frames (rounded up), 30 per second = 1 per frame, 3 per second = 0.1 per frame | 21 |

**`tests/CnCNew.Ui.Tests/TransitionTests.cs`**

| Lines | Test/Symbol | Cl. | n | What | Phase |
|---|---|---|---|---|---|
| 120,150,154,199,208,345,465,494,636,759 | TransitionTests.Group/One/Table-Fixtures (FrameDelay = n) | H/ticks | 10 | 10 sites: FrameDelay values of the synthetic WindowTransitions blocks (0, 1, 2, 3, 5, 6, -1) in transition frames – original frame, not simulation tick | 21 |
| 229,353,354,366,370,400,425,488,530 | TransitionTests (handler.Step() expectations per frame) | H/ticks | 9 | 9 sites: frame-exact expectations per WindowTransitionHandler.Step(): FLASH 8 frames, SCREENFADE 30 frames (alpha 1/(30-1) per frame), scaling per frame, one count sound per frame – original frame, not simulation tick | 21 |
| 695,698,699 | TransitionTests (handler.Advance(seconds)) | H/rate | 3 | 3 sites: Advance(0.1) yields 3 frames ('3 frames at 30 per second'), 30 calls with 1.0/60 yield 15 frames – cadence of the transitions on wall-clock time, original frame, not simulation tick | 21 |

## 8. Search patterns (for repeating)

Over `src`, `tests`, `tools`, only `*.cs`, without `bin`, `obj`, `.claude/worktrees`; line by line, .NET/Python regex. In parentheses hit lines / files at the audited commit. P01–P16 are the patterns of the initial inventory, N01–N17 were added in this audit. "Strong" (binding for the hit-driven packages) are all except N11, N12, N15, N16.

```
P01 (85/44)  LogicFramesPerSecond
P02 (35/29)  (PerSecond|FramesPerSec|TicksPerSec|FrameRate|TickRate|Fps)\w*\s*=\s*\d
P03 (111/23)  (1(\.0)?[dfm]?\s*/\s*30(\.0)?[dfm]?\b|/\s*30(\.0)?[dfm]\b|\*\s*30(\.0)?[dfm]\b|30m?\s*/\s*1000|/\s*33\b|33\.33|1000\s*/\s*30)
P04 (748/157)  \b30(\.0)?[fdmL]?\b|\b900m?\b|\b3600\b|\b1800\b|\b450\b
P05 (416/144)  const\s+(int|long|uint|ulong|short)\s+\w+\s*=\s*[^;]*;
P06 (73/47)  (long|int)\s+\w*(Tick|Ticks|Frames|Interval|Cadence|Every|Spacing)\w*\s*(\{[^}]*\})?\s*=\s*[\d_]+
P07 (18/11)  (Tick|tick|_life|frame\w*|_age)\s*%\s*\d+
P08 (79/29)  Tick\s*[-+]\s*[^;]*?(>=|<=|>|<|==)\s*\d+|Tick\s*[-+]\s*\d+
P09 (144/63)  return\s+(world\.Tick\s*\+\s*)?\d+L?\s*;
P10 (107/33)  ModuleConfigReader\.Ticks\([^)]*,\s*[^0)\s][^)]*\)
P11 (25/15)  ModuleConfigReader\.(Int|Real)\(configuration,\s*"[^"]*(Time|Delay|Rate|Frame|Duration|Interval|Life|Wait|Speed|PerSecond)[^"]*"
P12 (49/23)  \*=|(Speed|Velocity|Rate)\s*=\s*[^;]*\*\s*\w*(Damp|Friction|Factor|Decay|Braking|Acceleration)
P13 (8/5)  LogicRandom\.Range\((0|1),\s*\d+\)
P14 (76/56)  (?i)(per frame|every frame|\(frames\)|per logic frame)
P15 (95/35)  writer\.Int\("\w*(Tick|Frame|Until|At|Timer|Delay|Expire)\w*"
P16 (83/31)  GameTime|ElapsedInState|DeltaSeconds|TotalSeconds|W3dAnimationClock
N01 (573/81)  SimulationTiming\.\w+
N02 (107/33)  ModuleConfigReader\.(Ticks|Duration\w*|Milliseconds\w*|Seconds\w*|PerSecond\w*|Velocity\w*|Angular\w*|DegreesPerSecond\w*)\(
N03 (0/0)  return\s+[^;]*\bTick\s*\+
N04 (495/132)  \b(long|int|uint)\??\s+_?\w*(?i:nextwake|ready|until|expire|deadline|frame|tick|timer|countdown|cooldown|delay|timeout|lastshot|lasttime|starttime|endtime|wake|when)\w*\s*(;|=|\{)
N05 (89/53)  %\s*(\d+|\w*(Interval|Ticks|Frames|Period|Rate|Cadence)\w*)\b
N06 (439/166)  (?i)\b(per tick|per-tick|each tick|every tick|one tick|a tick|ticks? (of|per)|logic frames?|LOGICFRAMES|frames? per|per second|/s\b|30 ?hz|30 fps|1/30)
N07 (379/105)  \b(Tick|tick|Frame|frame|now)\s*(\+|-)\s*\w
N08 (389/112)  \b\w*(Frames|Ticks|Interval|Period|Cadence|Latency|Lifetime|Duration|Delay|Cooldown|Timeout)\w*\s*=\s*-?\d
N09 (444/56)  (reader|state|r)\.(Int|Long|Int64)\w*\("\w*(?i:tick|frame|until|timer|delay|expire|wake|ready|when|time|next|last|start|end|deadline|cooldown)\w*"
N10 (246/56)  writer\.(Int|Long|Int64|Fixed)\w*\("\w*(?i:tick|frame|until|timer|delay|expire|wake|ready|when|time|next|last|start|end|deadline|cooldown)\w*"
N11 (1001/146)  \.Tick\b|\bTick\b\s*[<>=!]|CurrentTick|world\.Tick
N12 (1005/140)  Fixed\.From(Ratio|Int|Decimal)\([^)]*\)\s*(;|,|\))|FromRatio\(
N13 (385/92)  (?i)(damp|friction|decay|drag|braking|stiffness|falloff)
N14 (89/29)  LogicRandom\.\w+\(
N15 (907/70)  (?i)(sleep|wake)
N16 (1900/181)  \b(15|45|60|90|120|150|300|600)L?\b
N17 (153/57)  (float|double)\s+\w*(dt|Dt|delta|Delta|elapsed|Elapsed|Seconds)\w*|Stopwatch|DateTime|UpdateTime\.|Elapsed\.|\.TotalMilliseconds
```

Experience from this audit: (1) N03 (`return world.Tick + N`) hits nothing – `IUpdateModule.Update` returns the sleep duration relatively, P09 is the right pattern. (2) Most of the coupling of classes C and E is **not** hit by the patterns: loops `for (i < N) world.Step()`, counters `--`/`++` per update, comparisons of a speed per tick with a distance, `Math.Max(x, SleepNone)`. For that, only reading the `Update` methods helps. (3) In the tests the patterns hit almost only fixture INI in milliseconds (not coupled); the tick expectations are in lines such as `== 9`, `Run(45)`, `StepUntil(…, 300)`. (4) For phase 20 item 7 (`SourceRules`), P01, P03, P06, P07, P10 and a new pattern for `/ LogicFramesPerSecond` outside `SimulationTiming` are suitable; P04/N16 are too noisy as a rule.

## Appendix A: audited, not coupled

**Files read completely** – hit lines without a table row of their own (without the mechanically counted conversions):

| Project | Hit lines without coupling |
|---|---|
| `CnCNew.Simulation` | 1952 |
| `CnCNew.Game` | 97 |
| `CnCNew.Presentation` | 199 |
| `CnCNew.Ui` | 9 |
| `CnCNew.Client.Stride` | 5 |
| `CnCNew.Data` | 1 |
| other src | 6 |
| tools | 67 |
| tests | 146 |
| **Total** | 2482 |

Reasons, grouped (the files were read in full, the assignment of the individual line to a reason is not logged individually): returns `SleepNone`/`SleepForever` and `return 0;` without time meaning; `NextWake` wiring; lines of state reading and writing (fields in Appendix C); distances, radii, percentages and angles via `Fixed.FromInt/FromDecimal/FromRatio`; numbers 15/30/45/60/90/120/150/300/600/900 as health, cost, distance, angle in degrees, map dimension or table index; enum, mask and version constants; `%` on IDs, indices and random values; comments that merely mention "per second"/"frames"; comparisons of the tick as an ordering key (sorting of commands, events, checkpoints); wall-clock time variables of the presentation (`deltaSeconds`, millisecond clocks). Seen line by line by the coordinator (hit lines with surroundings): fixed-point and hash constants, slot/mask and enum constants, `return 0/1/2/3` of index and mask functions, distances/vectors/percentages via `Fixed.FromDecimal`, angles via `DegreesToRadians`, tick as an ordering key in the replay, doc comments.

**Hit-driven packages and groups from the completely read packages** – per line file, recorded hit lines and reason:

*CnCNew.Simulation* (737 hit lines in 32 groups)

- `Sim\Commands/Commands.cs` (16,33,74,647,671): hits are type and rejection codes (15, 30) and the table version 14, no time values
- `Sim\Events/SimEvent.cs` (34,103,175-193): hits are enum values and reason codes (15, 60, 1–10), no time values
- `Sim\Mission/MissionSession.cs` (34): Default 3, is called "Ticks", but according to the doc and l. 78 is now only a switch (0 = self-targeting off); name and snapshot field remain
- `Sim\Mission/MissionSession.cs` (232): Ring radius + 30 world units for starting units, no time value
- `Mod\ModuleConfigReader.cs` (23-37,47-57): Generic raw accesses without unit conversion (number, Distance, Percentage, Money); the coupling arises only at the caller
- `Mod\WorldEffectModules.cs` (785): At least 1 tick of start delay: genuine tick semantics (one update before the start), no time specification
- `Sim\Modules/CollideModule.cs` (1-19): Read completely: interface only, no tick or time values
- `Sim\Modules/ModuleBindings.cs` (1-88): Read completely: binding table, no tick or time values
- `Sim\Modules/ModuleFramework.cs` (24,62,182,214-215,282-285,324,335): Interfaces carry ticks (Tick, WakeAt, BaseRegenDelayTicks, Shared-Ready-Frame, PowerSabotagedUntil, ParalyzeTroops untilTick, CreateFromListWithLifetime lifetimeTicks), but contain no values or conversions
- `Sim\Modules/ModuleFramework.cs` (553-560): Contract: Update returns relative sleep ticks (at least 1) or SleepForever, NextWake is an absolute tick; in this file there is no scheduler and no staggering (interfaces only)
- `Sim\Modules/UpgradeMux.cs` (1-92): Read completely: state machine without time reference (upgrade purchase takes effect immediately)
- `Sim\Scripting/ScriptEngine.Sequential.cs` (20): Loop guard 256 statements per runner and tick: no time value
- `Sim\Scripting/ScriptEngine.Settings.cs` (61): Int parameter recorded raw; in the original seconds (no frame value), here without a consumer
- `Sim\Scripting/ScriptEngine.TeamAbilities.cs` (249,254): Cache of the team assignment, rebuilt per tick (comparison with world.Tick only as a validity marker)
- `Sim\Scripting/ScriptEngine.cs` (75,77): Attack priority 1 and recursion limit 32: no time values
- `Sim\Skirmish/SkirmishAi.cs` (70,740,781,783,1040,1043,1106,1193): Hits on 30/450/900/3600/90_000 are distances or squared distances in world units (DefenseRadius, _badSpots, depot distance, arrival radius), no ticks; likewise MaxAttackThreshold = 30 (number of units)
- `Sim\Skirmish/SkirmishMatchSession.cs` (191,193,196,208,225): Start commands with tick 0 (absolute address of the first tick) and ring radius + 30 world units; no time values
- `Sim\Terrain/NavigationGrid.cs` (37,53,198-220): Temporary blockers apply only for one search (finally deletes them), no lifetime in ticks; ExpansionBudget 200_000 applies per search, not per tick; costs 10/14 are path costs
- `Sim\Terrain/TerrainMap.cs` (1-313): Read completely: geometry only (CellSize 10, MaxLineOfSightSamples 64), no tick or time values
- `Sim\Terrain/VisionMap.cs` (43-48): Vision is rebuilt completely per call; the map has no cadence of its own, no tick counter and no cooldown (the cadence lies with the caller in the world)
- `Sim\Testing/ObjectiveDriver.cs` (19): MaxDepth = 28 is the recursion depth of the planner, no tick count
- `Sim\Testing/ObjectiveDriver.cs` (405): Diagnostic text outputs the script delay in seconds from the map data, no tick calculation
- `Sim\Testing/ReferenceMatchScenario.cs` (269): Only checks whether a build time is present; no calculation with the tick count
- `Sim\Testing/StressScenario.cs` (126,161,173): The 30s/20s are percentage probabilities or value ranges for DamageType/DeathType indices, no tick values
- `Sim\World/SimulationWorld.Turrets.cs` (18,33,201,335,338,370,491,507): Angle threshold and factor 0.5 are dimensionless/angle; HoldUntil = tick + RecenterTicks is a data value in ticks as an absolute deadline (Math.Max(1, …) only a sentinel for 0 = none); comments; bit shift >> 30 of the hash mixing
- `Sim\World/SimulationWorld.Vision.cs` (61,62): Vision range times ShroudScalar / battle plan factor: length scaling, no time
- `Sim\World/SimulationWorld.WeaponSlots.cs` (98,143): Comment on the ready tick; 0.5 is the squared-distance tolerance of the minimum range; all other sites compare _tick with absolute ready ticks (ll. 94, 189, 211, 231, 391) or set them to _tick (ll. 290, 308, 375)
- `Sim\World/SimulationWorld.cs` (23,39,69,72,119,199,297,392,781,805,827,970,1292-1293,1304,1308,1311,1313-1316,1320-1337, …): Comments, return 0 (no rejection), counters, comparison/clamp of absolute ticks with SleepForever, WakeAt semantics in genuine ticks, passing through of BaseRegenDelayTicks, amount of money, snapshot lines (see state file)
- `Sim\World/SimulationWorld.cs` (50-51): Storm guard limits per tick (50,000,000 updates, 20,000,000 events), no time meaning
- `Sim\World/TunnelTracker.cs` (9): Entry tick per occupant is only stored and queried (EnteredAt); the healing calculation lies in the consumer (other package)
- `Sim\World/TurretState.cs` (13,40,73,114,117,122,124,134,137,144): State fields and snapshot lines with absolute ticks (HoldUntil, SweepUntil, LoseInterestAt, NextScan); no constants or calculations
- `Sim\World/WeaponSlotState.cs` (33,52,53,70,71): Absolute ready and last-shot ticks per slot, only stored

*CnCNew.Game* (190 hit lines in 16 groups)

- `Game\Audio/AudioDirector.Script.cs` (95,99,106): Hit is only "return 0"; fade-out time 0.05f in seconds to the mixer, no tick coupling; whole file without time reference to the tick
- `Game\Audio/AudioDirector.cs` (33,34,38,39,316,337): Clock of the director is the sum of the passed dt in seconds (wall-clock time of the render frame): music pause 3 s, ambient sampling 0.25 s, fades 0.15/0.08/0.25 s; no constant of 30
- `Game\Audio/AudioDirector.cs` (174,554-559): "Frames" here are audio sample frames of the mixer (RenderedFrames), grace period one tenth of the sample rate = 0.1 s; no logic tick
- `Game\Audio/EvaCatalog.cs` (9,10,150,189,211,212): Comment lines and an event counter; the coupling is in lines 126, 140, 141, 219, 234
- `Game\Audio/GameAudio.cs` (105-110,232): Update passes the seconds of the render frame through (Director.Update/Tick); Report computes mixer sample frames / SampleRate in seconds; no logic tick
- `Game\Audio/OfflineAudioRenderer.cs` (15,22,40,46-69): Audio sample frames from seconds × SampleRate; WAV header 44 bytes; no logic tick (but block sizes depend on the time step of the caller, see class comment)
- `Game\Campaign/CampaignCatalog.cs` (11,225,240): Length of the briefing voice in whole seconds from Campaign.ini, without conversion into ticks in this file
- `Game\Data/ModelGeometryLoader.cs` (46): Hit is "return 0" for missing NumRows/NumCols; file contains only geometry (rounding 1/16 unit, 1/1024 rad)
- `Game\Hud/HudMessageFeed.cs` (169): suppresses the defeat message for events of the first ticks (players without starting objects); genuine tick semantics of the start of the game, no duration
- `Game\Hud/HudOverlayRenderer.cs` (11-37,101,102,209,210): "/ 30" and "/ 60" are milliseconds per frame of the 2D animation (SCPCross 30 ms, SCPWrench 60 ms according to the comment Animation2D.ini), clock is MatchController._clockMs (wall-clock time); 10/16 are frame counts; remaining hits colours and comments
- `Game\Hud/HudOverlayRenderer.cs` (144-157): Age 0..1 comes ready-made from OrderHintBook.Age(_clockMs) (milliseconds, wall-clock time); the comment names the 40 frames of the original only as a description
- `Game\Hud/MatchController.cs` (58,465,741-757,764): Cooldown wipe and build progress are ratios 0..1 (ticks/ticks) from the command bar model; remaining hits field type, IsDragging and colour values
- `Game\Hud/MatchController.cs` (372-391,515,520,628): The HUD's own clock _clockMs += round(deltaSeconds × 1000): window tree (AdvanceTo), order markers (OrderHintBook), healing icons; camera via deltaSeconds; all wall-clock time of the render frame
- `Game\Hud/MatchResult.cs` (50-77): is called once per tick, but only counts events (built, lost, killed, money); no time reference
- `Game\Hud/MissionController.cs` (152-161): passes the seconds of the render frame to LiveGame.Advance and Hud.Update; popup clock in milliseconds of wall-clock time; the comment l.152 names "30 Hz" as text (copy of the rate only in the comment)
- `Game\Hud/MissionPauseMenu.cs` (200,208,216): Fallback name of a savegame from the absolute tick, used only as a unique file name

*CnCNew.Presentation* (338 hit lines in 40 groups)

- `Pres\Fx/FxRandom.cs` (18): `z >> 30` is a SplitMix64 shift constant
- `Pres\Fx/FxSettings.cs` (15): `MaxFieldParticles = 30` is a count (GlobalData default according to the comment), no rate
- `Pres\Fx/FxWorldView.cs` (54-60): Pose of destroyed entities stays available for exactly one refresh (one tick): tick semantics for death effects, no duration
- `Pres\Fx/SoundCues.cs` (49): Pairing ProductionCompleted/EntitySpawned in the same tick (older entries discarded): order semantics, no duration
- `Pres\Input/CameraInput.cs` (138): Scroll intent is factor times seconds (1 = base speed for one second); contains no rate
- `Pres\Input/CommandBarModel.cs` (345,390-395,406-407): Comparison world.Tick < ReadyTick and ratios remaining ticks/total ticks (ReloadTicks, BuildTicks): dimensionless
- `Pres\Input/CommandModeController.cs` (14-17,252,256,266,471,570,603,626,633,675,710,717,722): world.Tick is only passed through as IssuedTick (StandingOrder) or CommandFeedback.Tick (rows of their own exist for these); PlacementDragPixels/PlacementViewAngle are pixels/angles
- `Pres\Input/GameInputController.cs` (97-104): Passes deltaSeconds (wall clock per drawn frame, MatchController.Update) on to CameraInputController.Update (other package); hit lines 25-26, 102, 106, 241-258 without tick reference
- `Pres\Input/InputState.cs` (13-16): Thresholds in pixels and milliseconds (25 px, 250 ms, 500 ms, 4 px) on the event clock supplied by the caller; hit lines 7-8, 13, 23-26, 44-45, 50, 58, 69, 83, 117-168 are the drag state machine without tick reference
- `Pres\Input/KeyCode.cs` (70): Key name (Backtick), no tick reference
- `Pres\Input/MouseCursors.cs` (81,239-245,288-302): Cursor FPS default 20f (CursorInfo) is only stored, not used for timing; the animation runs via nowMilliseconds (SoftwareCursor.Draw) and AniCursor.FrameIndexAt (CnCNew.Images, not this package); lines 190, 257 without time reference
- `Pres\Input/SelectionModel.cs` (16,285-288): Double press of a group key: 500 ms on the input clock (InputState.LastTime), no constant of 30. Note: the comment names m_doubleClickTimeMS; what was found in the original, however, is 20 logic frames
- `Pres\Input/SimulationQueries.cs` (155-160): Passes the stored module state (incl. tick fields such as timers/queues) as a tree on to HUD code; no calculation with ticks here
- `Pres\Input/StandingOrders.cs` (36,41,104): 10 m pursuit threshold (distance); _standing prevents repetition per tick; line 104 sets IssuedTick anew (belongs to the settled row)
- `Pres\Minimap/MinimapImage.cs` (180,201): Pixel limit 1 << 14 and colour factors 95 %/60 %; no time quantity in the whole file
- `Pres\Objects/ConditionDeriver.Units.cs` (37): Height 3 units (tunnel detection of the train), no time quantity; other conditions (JETEXHAUST at Speed != 0 etc.) are state queries
- `Pres\Objects/ConditionDeriver.cs` (117,120,157): Comparisons of world.Tick with absolute ticks that the simulation itself keeps (deploying of radar/power plant, readiness of the weapon)
- `Pres\Objects/DrawKeyCatalog.cs` (15,29,35,43,48-50,55-56): Key names only (among others RecoilDamping, TreadAnimationRate, MoveOutwardTime, FadeLifetime); the raw values are handled by the row DrawSpecCompiler.ModuleKey
- `Pres\Objects/DrawSpec.cs` (53,88-89): Distance per animation cycle and speed factor of the animation (dimensionless); no tick or frame unit
- `Pres\Objects/FrameProjector.cs` (257,259-269): Build progress is the ratio ConstructionWork/BuildTicks, wheel travel the accumulated distance travelled (not speed per frame): both rate-independent; Travel, however, is not interpolated between ticks
- `Pres\Objects/ObjectVisualCatalog.cs` (103): Angle in degrees to radians, no time
- `Pres\Objects/PlacementPreview.cs` (10,16): Preview is rebuilt per frame from the PlacementGhost, no clock of its own; hits are comments
- `Pres\Objects/RenderFrame.cs` (53): Code -2 for the placement preview, no time
- `Pres\Objects/StructureConditions.cs` (39-126,375-428): Pure mapping of the module state of the simulation (enum/flags) and latches on state changes; sampled once per tick, no tick arithmetic
- `Pres\Objects/StructureConditions.cs` (129,190): Depth 3 world units and door count 4, no time
- `Pres\Objects/Suspension.cs` (230,242,243): Sampling distance 2 world units and hash constants, no time (the tick in the hash is in the row 126-136)
- `Pres\Objects/TeamColors.cs` (18,19): Colour values
- `Pres\Objects/TurretTracker.cs` (24-34): Reads angle, pitch and Rotating flag of the simulation turret per tick; no time or rate logic of its own (interpolation in FrameProjector.Blend)
- `Pres\Objects/UnitConditionTracker.cs` (34,35): Factor 0.5 for the crush point (geometry), no time; TensileDrop see its own row
- `Pres\Terrain/RoadCatalog.cs` (11,24): Road widths, static data
- `Pres\Terrain/RoadGeometry.cs` (18,139,140,156,157,160,174,175,178,198,329-332): Static road geometry (vector scaling, corner indices), no time
- `Pres\Terrain/RoadLayout.cs` (43,88,710,719,728,734,750,767,855,876,964,1007,1106,1182): Static road topology; the '30' are 30-degree steps of the curves (PI/6), no rate
- `Pres\Terrain/TerrainAtlas.cs` (86,89,92): Pixel dimensions of the atlas
- `Pres\Terrain/TerrainLighting.cs` (19): Number of global lights
- `Pres\Terrain/TerrainRenderModel.cs` (22,647): Byte size of a vertex and pixel count of a tile
- `Pres\Terrain/TerrainTextureLookup.cs` (32): Tile count
- `Pres\Terrain/WaterSurfaces.cs` (22-23,59-60): WaterSet UScrollPerMS/VScrollPerMS are read raw (per millisecond), but consumed nowhere in src; standing water gets UvScrollPerSecond = 0 (l.116)
- `Pres\Terrain/WaterSurfaces.cs` (28,44,83): World units per texture repetition and colour calculation, no time
- `Pres\Vision/FogOfWarGrid.cs` (21): Comment on the unit levels per second
- `Pres\Vision/FogOfWarGrid.cs` (95-105): Fog cross-fade in levels per second times dtSeconds; caller LiveGame.Advance l.195 with unscaled wall-clock seconds (also while paused), value 510 from LiveGame l.63; Update(vision) comes from LiveGame every VisionIntervalTicks (outside the package)

*CnCNew.Ui* (73 hit lines in 26 groups)

- `Ui\Layout/UiScaler.cs` (68,69): Reference resolution 800 × 600.
- `Ui\Rendering/UiFontSpec.cs` (23,28): 96 DPI and 91 % character width.
- `Ui\Rendering/UiGlyphAtlas.cs` (23,24): Atlas dimensions in texels.
- `Ui\Rendering/UiRenderStats.cs` (12): Comment "one per frame" means one drawn frame of the renderer (counter), no time.
- `Ui\Rendering/UiTooltipLayout.cs` (17): Width 15 pixels of the highlight band; the reveal runs via `width * AgeMs / FillTimeMilliseconds` (line 38, milliseconds).
- `Ui\Runtime/UiDrawBuilder.cs` (28,312,314,331): Tile limit 256, slider state "Dragging" and gap 2 pixels.
- `Ui\Runtime/UiEvents.cs` (19): Comment "slider drag finished".
- `Ui\Runtime/UiGadgetGeometry.cs` (13,14,16,18,19): Pixel dimensions of the gadgets (`* 2 / 3` is the thumb position, no rate).
- `Ui\Runtime/UiGadgets.cs` (65): State flag of the slider.
- `Ui\Runtime/UiWindowTree.Input.cs` (71,144,198,199,209,214): Slider state "Dragging" and a comment; the only time of the file is the double click `Now - _lastClickTime <= DoubleClickMs` (line 273, ms clock).
- `Ui\Runtime/UiWindowTree.cs` (16): Milliseconds against the ms clock UiWindowTree.Now set by the host (AdvanceTo ← GameShell._clockMs += dt * 1000; MatchController._clockMs; MissionController._popupClockMs: all from frame time in seconds). No sim tick, no 30.
- `Ui\Runtime/UiWindowTree.cs` (209,307): 209: upper bound of the event delivery per call (guard against an endless loop); 307: word "Dragging" (slider state).
- `Ui\Settings/GameSettings.cs` (81): Schema version 1 of the settings file (no tick or time values).
- `Ui\Settings/Resolution.cs` (40,41): Resolution list (e.g. 1680 × 1050, 3840 × 2160).
- `Ui\Settings/SettingsValidator.cs` (17,18,19): Value limits of the settings.
- `Ui\Text/CsfFile.cs` (36,37,38,39,40,41): File format constants of the CSF file.
- `Ui\Text/GlobalLanguageSettings.cs` (83): Milliseconds (fallback 750), here without conversion. The conversion with 30 happens in Game/Shell/MissionText.cs 137 (`(int)(LogicFramesPerSecond * ms / 1000f)`, truncated) – other package.
- `Ui\Text/InGameUiSettings.cs` (34,234): Here pure milliseconds without conversion. The coupling sits at the consumer Game/Shell/MissionText.cs 56 (`ms / LogicFramesPerSecond / 1000`, integer, yields 0 frames as in the original) – other package.
- `Ui\Text/InGameUiSettings.cs` (66,215,219,223,235): Lines 66, 215, 219, 223 are the comments on the sites reported individually above (67, 217, 221, 225); 235 is a colour value (200, 200, 30, 255).
- `Ui\Text/MouseTooltipSettings.cs` (9): Comment ("drag tolerance"). The fields FillTimeMilliseconds/DelayMilliseconds of the file are milliseconds; FillTime is offset in UiTooltipLayout.Build against UiTooltip.AgeMs (ms clock).
- `Ui\Transitions/TransitionEffects.cs` (27,538,705,811,812,853,855): 27: property that carries the individually reported end values; 538, 705, 811, 812, 855: comments on lines 543, 711, 816; 853: character index `_fullText[frame - 1]` (consequence of the frame unit, no value of its own).
- `Ui\Transitions/TransitionEffects.cs` (148): Code 18 of a draw state (draw only the button image), no frame number and no duration.
- `Ui\Transitions/WindowTransitionHandler.cs` (13,46,66,119,129,149): Counters in transition frames (no sim tick) and the comments on the constant in line 130; the frame unit itself is reported under line 130.
- `Ui\Wnd/WndModel.cs` (32,132): Status bit and MAX_DRAW_DATA = 9; nothing temporal.
- `Ui\Wnd/WndParser.cs` (17,40,170,177,184,595,777): Nesting depth 9, status name "DRAGABLE", `return 0` in error and NULL branches of the parser; nothing temporal.
- `Ui\Wnd/WndParser.cs` (800): Tooltip delay of a window in milliseconds, read raw; compared with the ms clock of the tree (UiWindowTree.Tooltip, lines 373–374). No tick.

*CnCNew.Client.Stride* (172 hit lines in 40 groups)

- `Client\Audio/AudioOutput.cs` (24,25,26,28,29,31): Audio frames (882 = 20 ms at 44100 Hz), buffer count, pump interval 4 ms of wall-clock time (Thread.Sleep), WinMM constants
- `Client\Audio/ClientAudio.cs` (61-66): Sound runs on the frame clock (seconds; without a device Pump renders seconds × SampleRate samples); the sound events themselves arise per tick in CnCNew.Game
- `Client\ClientRuntime.cs` (22,69,386): Comments "every frame" (rendered frame) and "fed by every simulated tick" (MatchStatistics.Record per TickResult, pure counting without a time quantity in the client)
- `Client\ClientRuntime.cs` (189,191,213,227,234,237,391,398-406): Frame clock of the client: smoke-test fixed step (1/30 s) or wall-clock time; goes as seconds to LiveGame.Advance, MatchController/MissionController.Update, GameShell.Update and ClientAudio.Update
- `Client\ClientRuntime.cs` (216-221): Minimap dots every 250 ms and blinking of the radar events on the client millisecond clock (wall-clock time or fixed step), no tick
- `Client\Layers/Fx/FxBuilder.cs` (42,645): Upper bound of the draw runs; geometry helper function
- `Client\Layers/Fx/FxDemo.cs` (154,155,169,170,171,179,185): Synthetic CPU benchmark: particle count, iterations, modulo distribution, Stopwatch wall-clock time
- `Client\Layers/Fx/FxGeometry.cs` (11,70): Buffer size in quads; return 0 for an empty buffer
- `Client\Layers/Fx/FxStats.cs` (39): Counter of rendered frames for the statistics
- `Client\Layers/Fx/FxVertex.cs` (20): Vertex size in bytes
- `Client\Layers/FxLayer.cs` (71,73,87,94): CPU time measurement (wall-clock time) of capture and build per rendered frame; the layer itself does not advance any effects (LiveGame does that per tick)
- `Client\Layers/Objects/FlashTint.cs` (15,18): Quantisation of the flash factor to 1/32 for the material cache
- `Client\Layers/Objects/HouseColor.cs` (87): Hue in degrees (HSV sextant × 60°)
- `Client\Layers/Objects/ModelAsset.cs` (80,96,100,273): Opacity levels in percent; animated texture mappers recomputed once per distinct frame time (seconds)
- `Client\Layers/Objects/ObjectInstance.cs` (16,27,43,63,68): State fields of the object layer: animation time in seconds, damping share (coupling see ObjectLayerCore 393/401), comments "per frame" = rendered frame
- `Client\Layers/Objects/ObjectLayerCore.cs` (43,119,650,655,686): CPU time measurement of the layer (Stopwatch) and deterministic hash/modulo selection of animation and start offset
- `Client\Layers/Objects/ObjectLayerCore.cs` (236,243,250,251,264,571,614,656,747): Animation clock in seconds of the frame clock (wall-clock time/fixed step), frame rate from the W3D animation; animated texture mappers with TotalSeconds; reset on state change
- `Client\Layers/Objects/ObjectLayerStats.cs` (66,68,71): Statistics per rendered frame (CPU milliseconds, bytes)
- `Client\Layers/Objects/ObjectRenderVertexBuilder.cs` (27,77): Seconds of the frame clock for W3dRenderPlan.GetTextureCoordinate (texture mapper animation)
- `Client\Layers/Objects/PooledEntity.cs` (10): Comment "every frame" = rendered frame
- `Client\Layers/Terrain/RoadMeshBuilder.cs` (17): Consistency check index count % 3
- `Client\Layers/Terrain/TerrainMeshMerger.cs` (55): Consistency check vertex count % 4
- `Client\Layers/Terrain/TerrainVertex.cs` (15,30): Vertex sizes in bytes
- `Client\Layers/Terrain/WaterMeshBuilder.cs` (11,50): Scroll per second × seconds ("wrapped per frame" = per rendered frame); index count % 3
- `Client\Layers/TerrainLayer.cs` (40,43,176,184,187,221,235): Sampling intervals of the display in seconds of the frame clock (fog texture at most 20 times per s, minimap 4 times per s); the comment assumes that the simulation changes the fog only every few ticks
- `Client\Layers/TerrainLayer.cs` (69,70,95,137,141,144,209): Wall-clock time measurement of Attach and Update (profile output ms per frame)
- `Client\Layers/TerrainLayer.cs` (180): Water UV scroll = UvScrollPerSecond × seconds of the frame clock
- `Client\Program.cs` (94,340): Number of journal lines that FinishMission prints; no time or tick quantity
- `Client\Program.cs` (183,185,351,384): Counter of rendered frames and method signatures (GameTime of Stride = wall-clock time); the coupled uses are in rows of their own
- `Client\Program.cs` (375-378): Model viewer without simulation: time of the texture animation in the smoke test = frames/60 s (otherwise wall-clock time gameTime.Total), budget 10/16 frames
- `Client\Rendering/IWorldLayer.cs` (8): Seconds of the frame clock (wall-clock time or smoke-test fixed step), no ticks
- `Client\Rendering/LiveMatchHost.cs` (55,57,58,62,63,68): Frame clock: fixed step or wall-clock time gameTime.Elapsed; _total sums it; Match.Advance(seconds) or step(seconds); WorldFrame(DeltaSeconds, TotalSeconds) for the layers
- `Client\Rendering/ScreenFilters.cs` (13,32,56): Colour weights in the comment; counter of filtered rendered frames
- `Client\Rendering/ScreenFilters.cs` (14,15,79,88): At most 30 zoom levels of the motion blur filter (level count, no time); the counter count is advanced in CnCNew.Game (MissionPresentation, _blurCount), not here
- `Client\Testing/InputScript.cs` (15,26): Comments "per frame" = update/frame of the client
- `Client\Ui/GdiFontProvider.cs` (106,107): Win32 GDI constants (FW_NORMAL 400 etc.)
- `Client\Ui/SoftwareCursor.cs` (41-44): Cursor animation on the client millisecond clock (_clockMs: wall-clock time or 33 ms per smoke-test frame)
- `Client\Ui/StrideInputTranslator.cs` (19-47): Timestamps of the input events = client millisecond clock (wall-clock time); no ticks
- `Client\Ui/UiRenderer.cs` (25): Atlas size in pixels
- `Client\W3dStrideScene.cs` (94,144,167): Seconds for the texture mapper animation of the model viewer (wall-clock time or frames/60 in the smoke test)

*CnCNew.Audio* (46 hit lines in 12 groups)

- `Audio\Catalog/AudioCatalog.cs` (201,263,329,566): "Decay" is the decay sound list of an event (file names), `return 0` clears the bits for NONE.
- `Audio\Catalog/AudioCatalog.cs` (554,555): delay in milliseconds, raw as integers; consumption: SoundSelector.NextIteration (random value in ms) → Segment.Silence(ms, outputRate) = `ms * Abtastrate / 1000` sample frames. No tick.
- `Audio\Catalog/AudioEventInfo.cs` (51,99,108,114): flag bit POSTDELAY and the decay sound list; DelayMin/DelayMax of the record are milliseconds.
- `Audio\Catalog/AudioSettingsInfo.cs` (30-31): stay milliseconds (comment: the original converts them to logic frames with parseDurationUnsignedInt); consumers Game/Audio/AudioDirector.cs 741, 745, 809, 863 divide by 1000 and calculate in seconds. No pattern hit, for completeness.
- `Audio\Mixing/PlayRequest.cs` (43,44,73): milliseconds and seconds, converted to sample frames in the mixer with the sample rate; line 73 is the decay sound list.
- `Audio\Mixing/SoftwareMixer.cs` (75,83,89,97,104,186,301,391,396,434,440,441,442): fades in seconds × Options.SampleRate (lines 190, 303); "frame" is a stereo sample frame (`% 2`, `2 * frame`). The only clock of the mixer is the number of rendered sample frames (Render), no tick quantity and no 30 Hz quantity.
- `Audio\Mixing/VoiceSources.cs` (26,88,110,160): guard limit of 64 empty segments, decay sound, comments; silence = `ms * outputRate / 1000` sample frames (line 9).
- `Audio\Movies/BinkHeader.cs` (26,29,80): video frames and frame rate from the Bink header (FpsNumerator / FpsDenominator of the file), limit of 256 audio tracks; no sim rate.
- `Audio\PcmBuffer.cs` (8,26,27,47,58): sample frames and duration = FrameCount / SampleRate; `2 * frame` is the stereo interleaving.
- `Audio\Selection/SeededRng.cs` (16): bit shift `>> 30` of the SplitMix64 step.
- `Audio\Selection/SoundSelector.cs` (29,44,61,63): selection of the decay sound; draw order of the audio-owned random generator per event start or loop pass, not per tick.
- `Audio\Wav/WavReader.cs` (95): number of sample frames from the fact chunk of the WAV file.

*CnCNew.Data* (31 hit lines in 13 groups)

- `Data\Compilation/CompatibilityBaseline.cs` (13): schema version 1 of the compatibility baseline, no relation to time
- `Data\Definitions/DefinitionPrimitives.cs` (29,88,304): GameTime holds milliseconds as decimal; ShotDelay is an ms range; the heuristic for opaque keys (Time/Delay/Duration …) stores the INI number unchanged as ms – no conversion to frames
- `Data\Definitions/NameTables.cs` (31): name table of the object status bits (hit caused by IS_BRAKING), no relation to time
- `Data\Ini/SageIniParser.cs` (7): maximum INI line length 1028 characters, no relation to time
- `Data\Registry/ModuleCatalog.cs` (421,426,427): schema holds the frictions as raw INI number per second (original: parseFrictionPerSec divides by the frame rate while parsing); CenterOfMassOffset is a length. No conversion in Data
- `Data\Registry/ModuleCatalog.cs` (888): schema holds the raw INI number (original parseAccelerationReal = distance/s²); no conversion in Data
- `Data\Registry/ModuleCatalog.cs` (959): comment line on the schema (delay/integrity/decay), no code
- `Data\Schema/FieldSchema.cs` (119,156,170,216): DurationMs and ShotDelayMs are stored as milliseconds in GameTime (sign wrap-around modulo 2^32 like sscanf %u); `index % 2` selects every second name. No frame conversion, no frame field type in FieldType
- `Data\Schema/ObjectCreationListSchema.cs` (12,38): schema holds ExtraFriction as raw number per second and the turn rates as degrees per second (FieldType.Angle); no conversion in Data
- `Data\Schema/TurretSchema.cs` (11,12,27,28): schema holds degrees per second as raw number, times as DurationMs; the comment says that only the simulation converts to radians per tick and ticks
- `Data\Schema/UpgradeSchema.cs` (63): schema holds the raw INI number (distance/s²; likewise Speed, TurnRate, Acceleration as per-second values); according to the comment only the simulation converts via SimulationTiming
- `Data\Snapshots/DefinitionSnapshotWriter.cs` (11,99): schema version 2; GameTime is written to the definition snapshot as `milliseconds` – the fingerprint contains no tick rate and no tick values
- `Data\World/MapModel.cs` (55,56,58,59,60,62,64): bit masks of the map object flags (road/bridge points), no relation to time

*other src* (160 hit lines in 23 groups)

- `src\CnCNew.Archives/ArchiveEntryStream.cs` (37,50,63): `return 0` at the end of the archive entry (byte count), no relation to time
- `src\CnCNew.Archives/BigArchive.cs` (9,10,207): format sizes (16 byte header, 32 KiB path) and percent escaping of file names, no relation to time
- `src\CnCNew.Content/RuntimeContentManifest.cs` (11): schema version 2 of the content manifest, no relation to time
- `src\CnCNew.Formats/LegacyAssetReaders.cs` (18,128,129,245,454): format limits and format checks; the Bink frame rate (numerator/denominator) is only read from the file header as metadata, nothing calculates with it; `% 20` is the WAK record length
- `src\CnCNew.Formats/SageMapDecoder.cs` (18,363): format constants (magic value 0x7ADA0000, at most 12 script parameters), no relation to time
- `src\CnCNew.Images/CursorReader.cs` (19): 60 jiffies per second is the time unit of the ANI file format; FrameIndexAt calculates from milliseconds (wall-clock time of the caller), not from simulation ticks – the 60 has nothing to do with the tick rate
- `src\CnCNew.Images/CursorReader.cs` (47,48,84,158,179): format limits (256 pixels, 4096 frames), header fields and length checks of the cursor decoder, no relation to simulation time
- `src\CnCNew.Images/TgaReader.cs` (7,8): format sizes of the TGA reader, no relation to time
- `src\CnCNew.Import/RuntimeContentImporter.cs` (340): percent escaping of trailing dots/spaces in file names (%2E/%20), no relation to time
- `src\CnCNew.W3D/W3dAnimation.cs` (62,63,74,222,223,225): FrameRate comes from the animation header of the W3D file (1..1000 checked in the decoder), DurationSeconds = FrameCount/FrameRate; the sampling works on fractional animation frames. No 30 in the code
- `src\CnCNew.W3D/W3dAnimationChannels.cs` (65,66,70,79,91,172,177,183,194,229,230,247,254,276,319,320,330,342): frame indices and format constants of the animation channels (range First..Last, time codes as frame numbers, groups of 16, 9-byte packets); everything in animation frames of the file
- `src\CnCNew.W3D/W3dAnimationClock.cs` (20,26,55): stateless mapping elapsedSeconds (double, seconds) × frameRate (from the file) × speedMultiplier → animation frame; `return 0` for one-frame animations. No tick, no 30
- `src\CnCNew.W3D/W3dAnimationDecoder.cs` (7-12): header sizes of the animation chunks in bytes; ReadHeader reads the frame rate from the file (uint32 or uint16)
- `src\CnCNew.W3D/W3dBinary.cs` (9): name length 16 bytes in the W3D format
- `src\CnCNew.W3D/W3dChunkReader.cs` (7-10): chunk format constants, no relation to time
- `src\CnCNew.W3D/W3dChunkType.cs` (5-81): chunk type identifiers of the W3D format (all hit lines 5-40, 42-45, 47-50, 52-55, 57-64, 66-72, 74-81), no relation to time
- `src\CnCNew.W3D/W3dHierarchy.cs` (23-25): structure sizes in bytes (36/60/48); the 60 is a pivot size, not a rate
- `src\CnCNew.W3D/W3dHlod.cs` (32-36): structure sizes of the HLOD format in bytes, no relation to time
- `src\CnCNew.W3D/W3dMeshDecoder.cs` (10-15,599): structure sizes of the mesh format in bytes, no relation to time
- `src\CnCNew.W3D/W3dModel.cs` (62): upper bound 99 for numbered bone names (only declaration and surroundings read), no relation to time
- `src\CnCNew.W3D/W3dRenderPlan.cs` (108): parameter totalSeconds (double, seconds) is passed through to W3dTextureMapperEvaluator.Evaluate; no tick
- `src\CnCNew.W3D/W3dSkeletonAudit.cs` (60): optional Stopwatch for the decoding time of an audit (wall-clock time, deliberately not in the report), no relation to the simulation
- `src\CnCNew.W3D/W3dTextureMapper.cs` (232,262,277,288): UV animation from totalSeconds: UPerSec/VPerSec and FPS come from the mapper arguments of the file (default FPS 1); frame = floor(abs(FPS) × seconds). No 30

*tools* (132 hit lines in 29 groups)

- `tools/CnCNew.AssetTool/Program.cs` (34,47,55,84,109,159,179,187,206,216,294): exit codes of the asset commands
- `tools/CnCNew.AssetTool/Program.cs` (168,262): progress display in 5 % steps and URL escape string; no relation to time
- `tools/CnCNew.CampaignTool/Program.cs` (51,87,104,156,182,231,237,362,415,431,464,543,576,624): exit codes of the commands or list limit Take(30); no time meaning
- `tools/CnCNew.CampaignTool/Program.cs` (295,347,357): wall-clock time of the audit run (TotalSeconds); no conversion tick↔second
- `tools/CnCNew.ImageTool/Program.cs` (7,25,30): exit codes; tool decodes TGA files, no relation to simulation or time
- `tools/CnCNew.IniTool/Program.cs` (23,50,60,78,84,223): exit codes of the INI commands; no relation to time at these sites
- `tools/CnCNew.MapTool/Program.cs` (23,28,71,79,277): exit codes of the map commands; no relation to time at these sites
- `tools/CnCNew.PresentationTool/AngleAuditCommand.cs` (24,86): exit codes
- `tools/CnCNew.PresentationTool/FxAuditCommand.cs` (14): exit code; the FX clock is not advanced here but in FxAudit.ExecuteAll (src/CnCNew.Presentation, other package), the tool only prints frames/checksum
- `tools/CnCNew.PresentationTool/ModelAuditCommand.cs` (22): exit code
- `tools/CnCNew.PresentationTool/Program.cs` (20,36,42,52,54): exit codes of the command dispatch and of object-audit
- `tools/CnCNew.PresentationTool/RoadAudit.cs` (30,32,139): exit code and wall-clock time of the build ("build time total")
- `tools/CnCNew.PresentationTool/ScenarioAudit.cs` (28,191,195,213,377,395): exit code, comments and output texts with the word "tick"; the corresponding statements are on lines of their own (207, 216, 392, 396)
- `tools/CnCNew.PresentationTool/ScenarioAudit.cs` (434,437): thresholds in units per second (55/s, text "minimum speed 60/s"), likewise 40f, 2f, 5f (ll. 423, 431) and 51f/52f (ll. 491, 501); rate-independent as long as the conversion in l. 111 is right
- `tools/CnCNew.PresentationTool/TerrainAudit.cs` (37,39,124): exit code and wall-clock time of the build ("build time total")
- `tools/CnCNew.SimTool/Program.cs` (53,77,173,180,188,215,238,313,487,546,555,564,656,671): exit codes of the commands or list limit Take(30); no time meaning
- `tools/CnCNew.SimTool/Program.cs` (158,170): tick differences (standstill, stay inside the building) are output raw in ticks, without conversion
- `tools/CnCNew.SimTool/Program.cs` (296,319,336,364,430,452,483,531,586,588,617,622,625,631,644,646,652,666,679,709,711): wall-clock time measurement of the tool runs (Stopwatch, TotalSeconds, slowest tick in ms); no conversion tick↔second
- `tools/CnCNew.SimTool/Program.cs` (335,451,553): cadence of the incremental hash check in ticks (5000) for reference-match, ai-match, replay-run; no relation to time
- `tools/CnCNew.SimTool/Program.cs` (578,638): MaxTicks = 0 means no limit (run length comes from --ticks, own line); sampledTicks is a counter
- `tools/CnCNew.SimTool/Program.cs` (730): hash check every 20,000 ticks (VerifyIncrementalHash); pure check cadence, no relation to time
- `tools/CnCNew.UiRenderTool/FontAudit.cs` (47): glyph coverage as a multiple of 17 (4-bit alpha); no relation to time
- `tools/CnCNew.UiRenderTool/MissionTextScenes.cs` (37): colour value and layout fraction 4/5 of the height
- `tools/CnCNew.UiRenderTool/Program.cs` (76): Stride draw callback; gameTime is not used, the captures run once in the first frame
- `tools/CnCNew.UiRenderTool/ShellScenes.cs` (64,66): image names; the file advances no clock (only input events with a consecutive timestamp ++time)
- `tools/CnCNew.UiRenderTool/SyntheticScenes.cs` (71): colour value
- `tools/CnCNew.UiRenderTool/TransitionScenes.cs` (13,14,20): doc comment on the 30-frame step size (statement in l. 21) and image size 1280x720
- `tools/CnCNew.W3dTool/Program.cs` (27,32,65,100,105,142,147,159,384): exit codes of the W3D commands
- `tools/CnCNew.W3dTool/Program.cs` (47,48,58,59): wall-clock time measurement of the audit (to stderr); no relation to ticks

*tests* (837 hit lines in 104 groups)

- `tests/CnCNew.Archives.Tests/Program.cs` (27): return 0
- `tests/CnCNew.Audio.Tests/Program.cs` (15,51,54): 3 sites: test name ('delay ... decay') and exit codes
- `tests/CnCNew.Audio.Tests/Tests.Catalog.cs` (36,79,84,129,146,199): 6 sites: AudioSettings/SoundEffects fixture in milliseconds (TimeBetweenDrawableSounds 900, Delay 100 250), decay file names; no conversion to ticks
- `tests/CnCNew.Audio.Tests/Tests.Decoding.cs` (102,105,127,139,161): 5 sites: ADPCM sample values 900/1000, MPEG frames, even byte/frame counts
- `tests/CnCNew.Audio.Tests/Tests.Mixer.cs` (54,109,110,111,176,177,179,200,212,216,228,235,236,275): 14 sites: vector (0,-30,40), 30 sample frames of a loop, DelayMin/Max in ms at 1000 Hz, step % n in the mixer script (audio blocks), Bink comment; mixer calculates in sample frames
- `tests/CnCNew.Audio.Tests/Tests.RealData.cs` (31,72,76,124,127,135,141,158): 8 sites: Stopwatch and throughput output, selection of every 25th event, step % 6 in audio blocks; hash only compared between two runs
- `tests/CnCNew.Content.Tests/Program.cs` (55): return 0
- `tests/CnCNew.Data.Tests/Program.cs` (46,341,441,442,451,453,456,457,574,772,773,778): 12 sites: return 0, vector X:-30, Lifetime/StealthDelay fields in milliseconds as GameTime (no tick conversion in CnCNew.Data), Locomotor Speed = 30 as raw value in the snapshot comparison
- `tests/CnCNew.Formats.Tests/MapDecoderTests.cs` (26): object coordinate 30, 40
- `tests/CnCNew.Game.Tests/AudioDirectorTests.cs` (38,84,85,471,832,841): 6 sites: declaration of the Tick property of the test double, Stopwatch for load time/measurement (wall-clock time), 30f is the radius of an FxEntityPose
- `tests/CnCNew.Game.Tests/CampaignShellTests.cs` (26,109): 2 sites: screen size and map size
- `tests/CnCNew.Game.Tests/CampaignTests.cs` (153): map size 30 cells
- `tests/CnCNew.Game.Tests/HudTests.cs` (27,88,261,270,426,512,513,608,663): 9 sites: return 0, comments ('every frame'), mouse drag helper (input time in ms), signature of the event helper
- `tests/CnCNew.Game.Tests/LiveMatchTests.cs` (19,22,23,35,37,42,44): 7 sites: return 0, Stopwatch measurements and console output ms/frame (600 = loop length)
- `tests/CnCNew.Game.Tests/MissionEffectsTests.cs` (32,33,356,388,547,551): 6 sites: return 0, counter initialisation, 30f/60f is blur counter 30 of 60, m:ss formatting (% 60), colour value 30
- `tests/CnCNew.Game.Tests/MissionHudTests.cs` (25,176): 2 sites: return 0 and comment 'every frame'
- `tests/CnCNew.Game.Tests/MissionPresentationTests.cs` (34,64,66,268): 4 sites: test name/message ('every frame'), map size 30 cells, border 1
- `tests/CnCNew.Game.Tests/MissionTextTests.cs` (134,146): 2 sites: bar height 75 pixels at 800x600; Int(30) is the seconds value of DISPLAY_CINEMATIC_TEXT (script data in seconds)
- `tests/CnCNew.Game.Tests/ModelGeometryTests.cs` (18,50,54): 3 sites: return 0; 30.825 is a bone coordinate
- `tests/CnCNew.Game.Tests/Program.cs` (28): return 2 for an unknown suite
- `tests/CnCNew.Game.Tests/ScenarioTests.cs` (19): return 0
- `tests/CnCNew.Game.Tests/ShellTests.cs` (31,41,53,502,514,515,517,587,588): 9 sites: screen size, return 0, slider drag, deadline of the boot test in wall-clock time (DateTime.UtcNow)
- `tests/CnCNew.Game.Tests/ShellTransitionTests.cs` (21,45): 2 sites: screen size and return 0
- `tests/CnCNew.Game.Tests/TextFixtures.cs` (15,19,21,22): 4 sites: synthetic INI values in milliseconds (MilitaryCaptionDelayMS 750, MessageDelayMS 75000, FlashDuration 500); the conversion lies in the audited code
- `tests/CnCNew.IO.Tests/Program.cs` (23): return 0
- `tests/CnCNew.Images.Tests/Program.cs` (15,18,22): 3 sites: TGA pixel bytes 30/20/10 and return 0
- `tests/CnCNew.Presentation.Tests/CameraTests.cs` (104): 1 site: modulo 40 only produces the height pattern of the test map
- `tests/CnCNew.Presentation.Tests/CameraTests.cs` (182): 1 site: 30 are height units (three notches), not time
- `tests/CnCNew.Presentation.Tests/CursorTests.cs` (55,91,305,329,335,355,356,477): 8 sites: Frames = 2 and Ani parameters of the cursor file (own cursor clock), grey level 30, screen/world coordinates 30, alpha channel modulo 4, assert line without relation to time
- `tests/CnCNew.Presentation.Tests/FxTests.cs` (30,44,194,201,222,442,460,465,511,517,729,730,820,863): 14 sites: test name, particle upper bound 30, radius 30, DecayAt fraction, FXList InitialDelay only compared as raw value, diagnostic fixture, coordinates 30f, volume distance, InitialDelayFrames = 0, spatial shake falloff, Stopwatch only for the output
- `tests/CnCNew.Presentation.Tests/InputTests.cs` (29,40,42,286,295,368,384,385,498,514,608,612,614,640,756,822,824,825,828,833,834,836,922, …): 42 sites: test names, fixture data in ms or per second, map dimensions, pixel and world coordinates 30, event timestamps in ms, angles, money 450, camera intent with dt = 1 s or 0 (defined per second)
- `tests/CnCNew.Presentation.Tests/JetOrderTests.cs` (30,45,53): 3 sites: Braking 60 per second squared and DelayBetweenShots in ms; data are converted
- `tests/CnCNew.Presentation.Tests/MinimapTests.cs` (67,77,81): 3 sites: pixel coordinate 30 and modulo 80 for the cell column
- `tests/CnCNew.Presentation.Tests/ObjectTests.cs` (67,74,87,89,90,277,596,597,619,640,675,680,855,859): 14 sites: fixture data in ms and per second, map dimensions, world coordinates 30, command stamp world.Tick
- `tests/CnCNew.Presentation.Tests/PlacementPreviewTests.cs` (20): 1 site: colour value (240, 200, 30)
- `tests/CnCNew.Presentation.Tests/RoadRealDataTests.cs` (25,82): 2 sites: return 0 when skipping, index count modulo 3
- `tests/CnCNew.Presentation.Tests/RoadTests.cs` (87,103,214): 3 sites: modulo 2 of the vertex count, 30-degree curve pieces, modulo 3 in the height pattern
- `tests/CnCNew.Presentation.Tests/StructureConditionTests.cs` (36,37,38,62,110,124,172,184,232,241,250,252,253,297,298,336,341,390,412,413,430,755,764, …): 24 sites: fixture data in ms and per second (are converted), geometry 30, test name and message texts, grid index % 60 of the spawn positions
- `tests/CnCNew.Presentation.Tests/SyntheticTerrain.cs` (45): 1 site: image size 128 pixels
- `tests/CnCNew.Presentation.Tests/TerrainTests.cs` (50,207,228,268,274): 5 sites: world coordinates 30, modulo in the height pattern, colour values 30
- `tests/CnCNew.Presentation.Tests/UnitConditionTests.cs` (65,67,75,77,85,86,97,105,107,114,124,131,132,272,284,360,371,573,924): 19 sites: fixture data in ms and per second (are converted), geometry and map dimensions, counter only compared against 0, command stamp World.Tick
- `tests/CnCNew.Presentation.Tests/VisionTests.cs` (48,88): 2 sites: fog cross-fade in levels per second with dt in seconds (wall-clock time), argument check
- `tests/CnCNew.Presentation.Tests/WorldRealDataTests.cs` (36): 1 site: return 0 when skipping without --content-dir
- `tests/CnCNew.Simulation.Tests/Program.cs` (408,411): 2 sites: exit codes 1/0 of the test runner
- `tests/CnCNew.Simulation.Tests/Tests.Acquisition.cs` (32,138,139,339,366,433): 6 sites: fixture in ms (DelayBetweenShots 300), health 30.0, coordinates (-30, 300, 450, 900)
- `tests/CnCNew.Simulation.Tests/Tests.Air.cs` (28,29,39,40,51,58,67,68,75,76,86,94,104,116,145,366): 16 sites: locomotor values per second (Acceleration/Braking), DelayBetweenShots in ms, HealAmountPerSecond 60, OutOfAmmoDamagePerSecond 30 %, radius 30, coordinate 900
- `tests/CnCNew.Simulation.Tests/Tests.AirModelGeometry.cs` (20,81,82,93,114,122): 6 sites: bone coordinates (30) and runway line y = 450 as world coordinates
- `tests/CnCNew.Simulation.Tests/Tests.AirReal.cs` (57,130): 2 sites: distance 30 between spawn points, target tolerance 30 units
- `tests/CnCNew.Simulation.Tests/Tests.AirWeapons.cs` (26,47,54,71,76,80,83,94,99,180,181,235,236,241,341,366,369,373,374,382,383,417,655,662,788): 25 sites: fixture in ms (DelayBetweenShots, Lifetime 900, DisabledDuration, Delay, KillSelfDelay, InitialDelay, Blast*Delay), Braking per s², radii 30, coordinate 900, sentinel deathTick = -1, tick difference only checked for >= 0
- `tests/CnCNew.Simulation.Tests/Tests.Basics.cs` (51,59,60,61,66,68): 6 sites: SleepForever/SleepNone (rate-independent), exceptions for negative/overflowing ms, DegreesToRadians, RatioToFixed
- `tests/CnCNew.Simulation.Tests/Tests.BattlePlan.cs` (31,130,168,180,240): 5 sites: DelayBetweenShots 100 ms, StealthDelay 0, coordinate 30, vision distance 30 units, damage 30
- `tests/CnCNew.Simulation.Tests/Tests.CleanupAndSpy.cs` (24,103,104,105,140,141,150,174,183,249,250,257,262,265,398): 15 sites: fixture in ms (DelayBetweenShots, BaseDuration, BonusDurationPerCaptured, MaxDuration, SelfPoweredDuration/Interval), VisionRange 30, coordinates 900/450/30, tolerance 30
- `tests/CnCNew.Simulation.Tests/Tests.Collision.cs` (19,40,167,168,185,186,231,268): 8 sites: Braking 30000 per s², DelayBetweenShots 0, Min/MaxToppleDelay 100 ms, InitialDelay 100 ms, map size cells = 30
- `tests/CnCNew.Simulation.Tests/Tests.Combat.cs` (20,27,31,37,44,52,61,70,121,157,215,216,287,292,342,346,403): 17 sites: fixture in ms (DelayBetweenShots 0/100/300, ExclusiveWeaponDelay 1000, InitialDelay 600), radii 30, MaxHealth 900, coordinate 30
- `tests/CnCNew.Simulation.Tests/Tests.Construction.cs` (25,42,179,196,283): 5 sites: Braking 60 per s², RepairHealthPercentPerSecond 10 %, BuildCost 30, money balance 900
- `tests/CnCNew.Simulation.Tests/Tests.Crates.cs` (94,118,124): 3 sites: LogicRandom.CopyState (random state, no cadence), coordinate 30 when spawning and looking up
- `tests/CnCNew.Simulation.Tests/Tests.CratesAndSpecials.cs` (25,42,45,51,122,140,269,293,367,371,478,479,533,546,635,647,655,656,739,969,972,998,1019, …): 24 sites: Braking 30000 per s², ranges/radii 30, ExperienceValue 10 30 90 300, durations in ms (SabotageDuration, Min/MaxLifetime, StealthDelay), coordinates 30/900, MinHealth 30 %, health 900
- `tests/CnCNew.Simulation.Tests/Tests.CreationLists.cs` (66,67,139,140,276): 5 sites: MinLifetime/MaxLifetime 1000 or 100000 ms, damage 30
- `tests/CnCNew.Simulation.Tests/Tests.DamagedLocomotor.cs` (14,15,16,23,24): 5 sites: SpeedDamaged 30, Acceleration/Braking 900 – locomotor data per second
- `tests/CnCNew.Simulation.Tests/Tests.FiringTracker.cs` (20,45,46,54,55,64,65): 7 sites: DelayBetweenShots 600/1000/200 ms, PreAttackDelay 500 ms
- `tests/CnCNew.Simulation.Tests/Tests.Flight.cs` (26,36,38,45,70,73,84,85,153,154,170,173,182,184,185,193,197,198,232,268,269,293,295,353, …): 27 sites: locomotor values per second (Braking, SpeedLimitZ 30), durations in ms (DelayBetweenShots, Lifetime, DoorDelay 34, DropDelay 100, Destruction/Volley delays), DamagePerSecond 100, RepairHealthPercentPerSecond, radius/tolerance 30, message text 'decayed'
- `tests/CnCNew.Simulation.Tests/Tests.GoldenMaster.cs` (40): 1 site: sentinel for 'not yet died'
- `tests/CnCNew.Simulation.Tests/Tests.JetFlight.cs` (24,38,51,64,153,157,336,340,341): 9 sites: fixture INI «Braking/TurnRate» in units per second (24,38,51,64,340,341), doc comments (153,336) and spawn coordinates 500/500 (157)
- `tests/CnCNew.Simulation.Tests/Tests.JetFlightReal.cs` (20): 1 site: comment «turn rate per tick» without a number
- `tests/CnCNew.Simulation.Tests/Tests.LastBlockers.cs` (41,50,79,125,167,249,312,313,362,363,387,421,422,518,557,583,600,617,791,811,1003,1006): 22 sites: fixture INI in ms or per second, or damage/XP values (41–422), coordinates x = 30 (518–617), command sequence number 900 (791,811), column grid % 3 (1003,1006)
- `tests/CnCNew.Simulation.Tests/Tests.MatchModules.cs` (40,89,178): 3 sites: fixture INI BaseRegenHealthPercentPerSecond/BaseRegenDelay (ms, per second), ForwardFriction = 2.0 (reading site in the simulation code not found), count value orientations > 30
- `tests/CnCNew.Simulation.Tests/Tests.Navigation.cs` (15,20,206,218,424,431): 6 sites: map size 20 cells, fixture «Braking = 60.0» per second, coordinates, id % 2 for the owner
- `tests/CnCNew.Simulation.Tests/Tests.Phase13Actions.cs` (86): 1 site: event id 900, world.Tick only passed through
- `tests/CnCNew.Simulation.Tests/Tests.Phase5.cs` (31,38,60,71,72,142,158,174,190,191,193,203,240,243,247,256,263,314,315,333,334,384,636, …): 31 sites: fixture INI in ms or per second, or cost/health 30 (31–384, 1058), damage values and health checks (636,638,800,1132,1185,1212), texts without relation to time (893,957)
- `tests/CnCNew.Simulation.Tests/Tests.PlayerAgent.cs` (86,95,98): 3 sites: script parameter 30 is only recorded and read back (RecordedSetting == 30), no consumer
- `tests/CnCNew.Simulation.Tests/Tests.PowerPlant.cs` (52,61): 2 sites: fixture INI HealthPercentToDrainPerSecond = 30% and Braking = 60.0 (per second)
- `tests/CnCNew.Simulation.Tests/Tests.Projectiles.cs` (31,42,50,59,67,74,80,86,98,111,112,128,129,147,294,296,307,338,442): 19 sites: fixture INI in ms, per second, percent or heights (31–147), geometry in the comment (294,296), variable impactTick = -1 (307,338), coordinates (442)
- `tests/CnCNew.Simulation.Tests/Tests.ReferenceMatch.cs` (21,26,27): 3 sites: fixture INI DelayBetweenShots = 500 (ms), Acceleration/Braking = 30.0 (per second)
- `tests/CnCNew.Simulation.Tests/Tests.Repair.cs` (139,187,216,241): 4 sites: damage value 30 and health check == 30
- `tests/CnCNew.Simulation.Tests/Tests.RetailModules.cs` (79,217,218,313): 4 sites: fixture INI ExitDelay = 3000, WorkerRespawnDelay = 1000 (ms), HoleHealthRegen%PerSecond = 30%; damage value 30
- `tests/CnCNew.Simulation.Tests/Tests.Scripting.cs` (33,52,132,182): 4 sites: comparison operator numbers, script parameter delaySeconds = 0 (seconds), fixture INI DelayBetweenShots = 100 and ReplacementDelay = 4000 (ms)
- `tests/CnCNew.Simulation.Tests/Tests.Skirmish.cs` (54,168,175): 3 sites: fixture INI RepairHealthPercentPerSecond = 10%, DelayBetweenShots = 400 (ms), Braking = 60.0 (per second)
- `tests/CnCNew.Simulation.Tests/Tests.Slaves.cs` (24,32,33,42,48,89,140,160,195,200,247,258,259,277,278,318,320,410,430,431,631,734,783,790, …): 25 sites: fixture INI in ms or per second, ranges and health 30 (24–431), health/damage values 30 (631,734,783,790), distance < 30 (996)
- `tests/CnCNew.Simulation.Tests/Tests.StagedCapture.cs` (113): 1 site: doc comment 'One observed tick', no numeric value
- `tests/CnCNew.Simulation.Tests/Tests.State.cs` (43,47,54,55,76,337,343,355,379,464): 10 sites: comments and messages with the word tick, modulo on loop index (player choice), loop index == 2, regex for wall-clock time
- `tests/CnCNew.Simulation.Tests/Tests.StructuresAndWorld.cs` (53,60,64,68,71,76,85,86,129,130,131,133,134,155,157,163,164,165,353,358,359,403,427,428, …): 38 sites: fixture INI in ms (via ModuleConfigReader.Ticks), damage/radii/heights, locomotor Braking per s, CollapseDamping as dimensionless factor (1 - Damping) on gravity; DegenPercentPerSecond: conversion in the module not audited
- `tests/CnCNew.Simulation.Tests/Tests.StructuresAndWorld.cs` (897,900,901,1069,1073,1075,1157,1308,1311,1326,1350): 11 sites: positions, distances and heights (30, 450), modulo on column index, message texts
- `tests/CnCNew.Simulation.Tests/Tests.Supply.cs` (15,90,91,158,281): 5 sites: money value 50, fixture INI in ms (100, 2000), command sequence number 900
- `tests/CnCNew.Simulation.Tests/Tests.Teams.cs` (27,154): 2 sites: experience values (ExperienceValue 10 30 90 300, Experience == 30)
- `tests/CnCNew.Simulation.Tests/Tests.Terrain.cs` (79,89,92,93,97,143,160): 7 sites: coordinates and polygon points with 30, modulo on height field index
- `tests/CnCNew.Simulation.Tests/Tests.Transports.cs` (108,109,117,122,202,237,238,337): 8 sites: fixture INI in ms (0, 99999999, 300), scatter distance 20..30, counter insideTicks only checked for > 0
- `tests/CnCNew.Simulation.Tests/Tests.TurretAttack.cs` (30,92,93,94,377,527,624): 7 sites: fixture INI per second or in ms (Braking 300, Scan 1000/2000 ms), angles in degrees, position x 30, random stream comparison
- `tests/CnCNew.Simulation.Tests/Tests.Turrets.cs` (30,37,154,249,269,459,460): 7 sites: fixture INI in ms or degrees per second, DegreesToRadians (rate-independent), comparisons against SimulationTiming.DegreesPerSecondToRadiansPerTick (expectation scales along)
- `tests/CnCNew.Simulation.Tests/Tests.Veterancy.cs` (15,40,66,90,106,129,215): 7 sites: experience values 10 30 90 300 and their sum, DelayBetweenShots 500 ms in the fixture
- `tests/CnCNew.Simulation.Tests/Tests.Vision.cs` (23,88,125,214): 4 sites: DelayBetweenShots 0, VisionRange 30, position (30, 30), message text
- `tests/CnCNew.Simulation.Tests/Tests.WeaponSlots.cs` (29,36,44,52,61,75,76,365,541,542,543,596,646): 13 sites: fixture INI in ms (DelayBetweenShots), locomotor Acceleration/Braking 30 per s², positions y = 30, comment
- `tests/CnCNew.Simulation.Tests/Tests.WeaponSlotsIntegration.cs` (28,57,58,210,218,328,335,386): 8 sites: fixture INI in ms; OCLLifetimePerSecond 1000 is computed as (ticks * value / 1000) and is rate-neutral; Pickup(30) is an x position; SimulationTiming.SleepForever
- `tests/CnCNew.Simulation.Tests/Tests.WeaponSlotsReal.cs` (72,96): 2 sites: comments 'fire order every tick', no numeric value
- `tests/CnCNew.Simulation.Tests/Tests.World.cs` (30,33,36,39,42,45,50,53,56,59,62,65,68,71,74,77,80,83,86,89,92,95,98,101,104,107,110,113, …): 33 sites: parameter 'long? tick = null' = execution tick of a command (default world.Tick), no duration, no relation to the rate; Timer(delay, repeat) passes ticks through
- `tests/CnCNew.Simulation.Tests/Tests.World.cs` (158,161,268,294,319,521): 6 sites: damage 30, seed 4242, fixture INI in ms (0, -1), SimulationTiming.SleepForever as default, sequence number 30
- `tests/CnCNew.Ui.Tests/SentenceLayoutTests.cs` (84,103,105): 3 sites: wrap width 30 and block width 30 in pixels
- `tests/CnCNew.Ui.Tests/TextAndSettingsTests.cs` (65,70,205,251,253,265,267,268,540,541,732,734): 12 sites: return 0/1, format specifiers, INI fixture values in milliseconds or per second, resolution 1600x900, Regex.Match
- `tests/CnCNew.Ui.Tests/TransitionTests.cs` (445,543,844): 3 sites: 50,30 and 480 / 16 = 30 are pixel values; maxFrames = 0 is a counter
- `tests/CnCNew.Ui.Tests/UiRenderingTests.cs` (112,113,182,198,232,233,374): 7 sites: random sizes 1..30 pixels, modulo for test alpha, page size 5x4
- `tests/CnCNew.Ui.Tests/WndFixtures.cs` (32,62): 2 sites: TOOLTIPDELAY in milliseconds (default -1), colour values 10 20 30
- `tests/CnCNew.Ui.Tests/WndTests.cs` (17,105,108,154,160,238,265,279,309,607,608,681,686,935,984,1041,1042,1092,1143,1194,1269, …): 25 sites: resolutions, rectangles, colours 10,20,30, column widths 30/70, image sizes (10, 30), slider drag, Stopwatch measurement
- `tests/CnCNew.Ui.Tests/WndTests.cs` (101,865,867,868,877): 5 sites: tooltip delays and UiWindowTree.AdvanceTo in milliseconds of the UI clock (1000, 1799, 1800, 2650 ...): wall-clock time, no tick
- `tests/CnCNew.W3D.Tests/Program.cs` (122,213,291,385,387): 5 sites: return 0, colour bytes 10/20/30, texture mapper argument FPS=30 (W3D material data, evaluated in seconds)
- `tests/CnCNew.W3D.Tests/SkeletonTests.cs` (112,145,150,177-187,189,329,330,345,346,511,515,543,633): 23 sites: W3dAnimationClock.GetFrame(mode, frames, frameRate, seconds) calculates animation frames from seconds and the frame rate of the file (10 or 30 fps in the header of the fixture), Stopwatch, delta encoding; no simulation tick

## Appendix B: hits, not audited

A pattern hit these lines, but they were **not** read with their surroundings. They are in no table of this document.

| Project | hit lines not audited (weak patterns N11, N12, N15, N16 only) | hit lines of strong patterns not audited |
|---|---|---|
| `CnCNew.Ui` | 7 | 0 |
| `CnCNew.Audio` | 1 | 0 |
| `CnCNew.Data` | 3 | 0 |
| other src | 2 | 0 |
| tools | 5 | 0 |
| tests | 1890 | 0 |
| **Total** | 1908 | 0 |

The weak patterns are `.Tick`/`world.Tick`, `Fixed.From…`, `sleep|wake` and the numbers 15/45/60/90/120/150/300/600. In the tests these are mostly lines that read the world tick, fixture numbers and loops; some of them are tick expectations that are missing in §7. Also not audited:

- 0 lines reported as "U" by the reading passes.
- 51 `writer` lines of the simulation with a time-like name that are not directly assigned to any entry of Appendix C (§4.3).
- Files without hits of strong patterns in the hit-driven projects were not opened (numbers in §1.3, column "not read"); `Data\Registry/ModuleCatalog.cs` (154 KB) only around the hits and with a name search – further frame values typed only as `Real`/`Integer` are possible there (unconfirmed candidates: `RelativeSpeed`, `SpecialAccelFactor`, `ManualDrivingSpeed`, `RandomSplashSoundFrequency`, `OCLLifetimePerSecond`, `ExecuteAnimationTime`).
- Scripts (`scripts/*.ps1`) that call the client with `--frames`, `--every`, `--input-script` or read tick counts from tool output; the checksums of `UiRenderTool`, capture and audio capture runs, which are pinned nowhere; Stride's default for the game loop.
- Reference source: function flows behind the constants (e.g. `Locomotor::moveTowardsPosition…`, `SlowDeathBehavior::beginSlowDeath`, `GameWindowTransitions*.cpp`, `PartitionManager`) – everywhere there it says "origin not verified" or "not looked up".
- Nothing was executed at 60 Hz; whether the HUD hash depends on the step count is open (§4.1); the effect of run-time rounding on fire rates with bonus is not measured (§4.4).

## Appendix C: state fields related to ticks (keys per file)

Kind: **absolute** = absolute tick; **remaining** = remaining ticks; **duration** = duration/interval in ticks; **per tick** = quantity per tick. Keys as the reading pass noted them (partly as a path, e.g. `turrets[].holdUntil`).

- `Game\Save/SaveGame.cs` – absolute: `tick`
- `Sim\Commands/Commands.cs` – absolute: `tick`; duration: `delay`, `repeat`
- `Sim\Events/SimEvent.cs` – absolute: `tick`, `value`
- `Sim\Mission/MissionSnapshot.cs` – absolute: `maxTicks`; duration: `presentationLatencyTicks`, `combatIntervalTicks`
- `Mod\AirModules.cs` – absolute: `nextWake`, `nextHeal`, `arrivals[].tick`
- `Mod\AirWeaponModules.cs` – absolute: `nextWake`, `dieTick`, `playTick`, `nextWake`, `startTick`, `nextWake`, `nextWake`, `nextWake`, `launchTick`, `nextWake`, `destructionTick`, `activationTick`, `nextWake`, `timeoutTick`, `nextWake`, `orbitEscape`; duration: `howitzerCounter`; per tick: `scale`, `velocity`
- `Mod\BattlePlanModule.cs` – absolute: `nextWake`, `nextReadyTick`
- `Mod\BridgeModules.cs` – absolute: `nextWake`, `deathTick`, `nextWake`; duration: `firedUpTo`; per tick: `lateralSpeed`, `verticalSpeed`
- `Mod\CleanupAndSpyModules.cs` – absolute: `nextWake`, `nextWake`, `deactivateTick`, `disabledUntil`, `nextWake`; remaining: `nextScan`
- `Mod\CombatModules.cs` – absolute: `nextWake`, `initialDelay`, `ready`
- `Mod\ContainChainModules.cs` – absolute: `scuttledTick`
- `Mod\ContainModules.cs` – absolute: `nextWake`, `nextWake`, `contained[].since`
- `Mod\CrateModules.cs` – absolute: `nextWake`, `nextWake`
- `Mod\DieAndUpdateModules.cs` – absolute: `nextWake`, `dieTick`
- `Mod\DockMachine.cs` – absolute: `dockMachine.enterTick`, `dockMachine.nextActionTick`
- `Mod\DockModules.cs` – absolute: `nextWake`, `nextWake`, `depositOnTick`
- `Mod\FerryModules.cs` – per tick: `pullPerTick`, `pushPerTick`
- `Mod\FieldEffectModules.cs` – absolute: `nextWake`, `lastScan`, `claims[].expiry`, `nextWake`; duration: `life`; per tick: `inertiaX`, `inertiaY`
- `Mod\FlightModules.cs` – absolute: `nextWake`, `destructionTick`, `deathTick`, `groundTick`, `nextWake`, `reactionTick`, `nextVolleyTick`, `reloadTick`, `flares[].added`, `reentryTick`, `deliverStart`, `nextWake`, `pendingPathTick`, `startAttack`, `startDecay`, `decayStart`, `nextPulse`, `nextScorch`, `nextLaunchFx`; remaining: `dropDelayLeft`; duration: `(run.Describe)`; per tick: `fallSpeed`, `flares[].vx`, `flares[].vy`, `fallSpeed`
- `Mod\HackerAndAssaultModules.cs` – absolute: `hackedUntil`, `lastShot`; remaining: `hackRemaining`
- `Mod\JetAIUpdate.cs` – absolute: `whenTick`, `transferTick`, `reloadDone`, `returnTick`, `attackExpire`, `checkAirfield`; per tick: `verticalSpeed`
- `Mod\LastBlockerModules.cs` – absolute: `nextWake`, `nextWake`, `suppressedUntil`, `nextHealing`
- `Mod\MatchModules.cs` – absolute: `nextWake`
- `Mod\MineModules.cs` – absolute: `nextWake`, `nextWake`, `nextDeathCheck`; remaining: `scanCountdown`
- `Mod\Phase12Modules.cs` – absolute: `nextWake`, `reactionReady0..3`, `continuousReady0..3`, `nextWake`, `nextWake`, `lastRefresh`
- `Mod\Phase5BlockerModules.cs` – absolute: `aflameEndTick`, `burnedEndTick`, `damageEndTick`, `lastFlameDamageTick`, `nextWake`, `destructionTick`, `nextWake`, `nextWake`, `nextWake`, `poisonDamageTick`, `poisonStopTick`
- `Mod\Phase5Modules.cs` – absolute: `nextWake`, `readyTick`, `detectedUntilTick`, `nextWake`, `nextWake`, `paralyzedUntil`, `blockerReplanTick`, `nextWake`, `disabledUntil`; remaining: `framesGranted`, `queue[].remaining`; per tick: `speedCap`
- `Mod\PhysicsAndMinefieldModules.cs` – absolute: `nextWake`, `nextWake`; per tick: `velocityX`, `velocityY`, `velocityZ`
- `Mod\ProductionExitModules.cs` – absolute: `nextWake`; remaining: `delay`
- `Mod\ProjectileModules.cs` – absolute: `nextWake`, `lifespanTick`, `nextWake`, `stateTick`, `fuelExpiration`, `decoyTick`, `nextWake`; remaining: `step`, `nextScan`, `nextShot`; duration: `segments`; per tick: `speed`, `speed`, `maxSpeed`, `acceleration`, `verticalSpeed`
- `Mod\RailroadModules.cs` – absolute: `nextWake`; remaining: `waitTimer`; duration: `wantsLead`; per tick: `pull.speed`, `conductorPull.speed`
- `Mod\RepairModules.cs` – absolute: `nextWake`; remaining: `nextScan`; per tick: `healthPerTick`
- `Mod\RetailMatchModules.cs` – absolute: `nextWake`, `contained[].since`, `extendDoneTick`, `extendDoneTick`, `nextWake`, `nextWake`; remaining: `workerWait`
- `Mod\SlaveModules.cs` – absolute: `nextWake`, `nextWake`, `occupiers[].tick`; remaining: `framesToWait`, `framesToWait`; duration: `crisisTimer`
- `Mod\SpawnModules.cs` – absolute: `nextWake`, `workTick`, `replacementTimes[].tick`
- `Mod\SpecialAbilityStaging.cs` – absolute: `nextWake`; remaining: `abilityAnim`, `abilityPrep`; per tick: `abilityFlash`
- `Mod\SpecialPowerModules.cs` – absolute: `availableOnFrame`, `pausedOnFrame`, `nextWake`, `pendingGunships[].tick`
- `Mod\SpecialsModules.cs` – absolute: `nextWake`, `nextWake`, `nextWake`; per tick: `radius`
- `Mod\StructureModules.cs` – absolute: `nextWake`, `collapseTick`, `burstTick`, `nextWake`, `deployFrame`, `nextWake`; per tick: `velocity`
- `Mod\ToppleAndFallModules.cs` – absolute: `nextWake`, `nextWake`, `toppleFrame`, `doneAtTick`, `nextWake`, `earliestDeathTick`; per tick: `angularVelocity`, `angularAcceleration`, `velocity`, `velocityFine`, `structuralIntegrity`, `lastZ`
- `Mod\TransportModules.cs` – absolute: `nextWake`, `destructionTick`, `lastSelfSpinUpdate`, `hitGroundTick`, `nextWake`; remaining: `bladeFlyOff`; per tick: `forwardSpeed`, `selfSpin`, `fallSpeed`
- `Mod\VehicleModules.cs` – absolute: `nextWake`, `groundCheckTick`, `penaltyDeathTick`, `destructionTick`; per tick: `verticalSpeed`
- `Mod\WorldEffectModules.cs` – absolute: `nextWake`, `nextCreation`, `timerStarted`, `nextWake`, `doneForever`, `nextWake`, `startTick`, `nextWake`, `activeTick`, `splashTick`, `nextWake`, `nextWake`, `nextWake`, `lastDamage`, `nextWake`, `nextPulse`, `active[].expiry`; remaining: `stateCountdown`, `changeCountdown`, `scanDelay`, `scanDelay`, `startDelay`; duration: `timeActive`
- `Sim\Scripting/ScriptEngine.Phase13.cs` – absolute: `transportStatus[].frame`
- `Sim\Scripting/ScriptEngine.Sequential.cs` – remaining: `sequences[].queue[].wait`
- `Sim\Scripting/ScriptEngine.TeamAbilities.cs` – absolute: `buttonHunts[].next`; duration: `hulkLifetimeOverride`
- `Sim\Scripting/ScriptEngine.TeamQueues.cs` – absolute: `teamQueue[].started`
- `Sim\Scripting/ScriptEngine.cs` – absolute: `tick`, `outcomeTick`, `scripts[].next`, `presentation[].until`; remaining: `endTimer`, `counters[].value`
- `Sim\Templates/ObjectCreationListCatalog.cs` – duration: `runDropDelay`
- `Sim\Timers/SimTimer.cs` – absolute: `due`; duration: `repeat`
- `Sim\World/AcquisitionState.cs` – absolute: `orderedTick`, `nextCheck`, `chaseTick`
- `Sim\World/Entity.cs` – absolute: `spawnTick`, `weaponReadyTick`, `lastDamageTick`, `lastFireTick`, `empDisabledUntil`, `sellCompleteTick`; duration: `constructionWork`; per tick: `speed`
- `Sim\World/SimulationWorld.FiringTracker.cs` – absolute: `cooldownFrame`, `preFinish`
- `Sim\World/SimulationWorld.Projectiles.cs` – absolute: `due`
- `Sim\World/SimulationWorld.Snapshot.cs` – absolute: `tick`, `frame`, `powerSabotagedUntil`; duration: `turretWaits`, `minLifetime`, `maxLifetime`, `deliveryDelay`
- `Sim\World/SimulationWorld.cs` – absolute: `tick`, `players[].sharedReadyFrames[].frame`, `players[].powerSabotagedUntil`, `events[]`; duration: `spawnQueue[].minLifetime`, `spawnQueue[].maxLifetime`, `spawnQueue[].deliveryDelay`
- `Sim\World/TunnelTracker.cs` – absolute: `tunnelRiders[].since`
- `Sim\World/TurretState.cs` – absolute: `turrets[].holdUntil`, `turrets[].sweepUntil`, `turrets[].loseInterestAt`, `turrets[].nextScan`
- `Sim\World/WeaponSlotState.cs` – absolute: `weaponSlots.ready1`, `weaponSlots.ready2`, `weaponSlots.fire0`, `weaponSlots.fire1`, `weaponSlots.fire2`

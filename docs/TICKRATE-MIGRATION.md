# Tick rate as a value of the world – conversion (Phase 20)

As of 6 October 2026, **final state of Phase 20** (`docs/handoffs/HANDOFF-PHASE20.md`): time contract, catalogs per rate and formats (stage 1), the bare tick counts of the simulation in original frames (stage 2, four briefs by directory), the merge with `SourceRules` and the trial run at 60 and 120 Hz. This document replaces the four result files of stage 2 (`TICKRATE-STAGE2-*.md`); their content is here. The basis is the verified inventory `docs/TICKRATE-AUDIT.md`; classes (A to H) and contradictions (W1 to W10) are defined there. "Frame" always means a logic frame of the original (1/30 s), "tick" a step of the simulation.

**The iron rule:** At 30 Hz nothing changes – no hash, no snapshot byte, no replay byte. The proof is `SimTool reference-manifest check <content> baselines/reference-manifest.json` (set `quick` after every commit, `full` at the end) and `SimTool input-manifest check <content> baselines/input-manifest.json`. An intermediate state that does not pass the manifest is a bug in the rework, never a new reference value. The values in `CLAUDE.md` are no longer a yardstick (for the reference match the manifest records winner FactionGLA at tick 14,490, final hash `23B4B25DF62765D8`).

**What Phase 20 is not:** correct behaviour at 60 Hz. The simulation runs through at 60 and 120 Hz and is deterministic there (§11); what behaves differently there is in the list for Phase 23 (§5). What consumers outside the simulation have to change is in the list for Phase 21 (§6).

## 1. Convention (as it was applied)

Every site that means time goes through the time service – at run time `world.Time` (on `IWorldServices`, in the script VM `_world.Time`, in the world itself `Time`), during the catalog build `configuration.Time` (module `Parse`) or the parameter `timing` of the catalog builders. No module calculates with a rate itself. Constants stay `const`, but in **original frames** (suffix `Frames`) or in seconds (suffix `Seconds`), never in ticks; the conversion is at the point of use. The source reference in the comment stays.

| Case | Write it like this | Example |
|---|---|---|
| **(a) Original constant in frames** | `private const long XFrames = N;` and at the point of use `world.Time.OriginalFrames(XFrames)` | `SpawnBehavior` (`SpawnModules.cs`): `UpdateRateFrames = SimulationRate.OriginalFramesPerSecond / 2`; `_workTick = world.Tick + world.Time.OriginalFrames(UpdateRateFrames)`. `SlavedUpdate`: `PackFrames = 15`, `ArmFrames = 5`. |
| **(b) Time value that was written down as a tick count** | like (a): the previous value is the number of original frames (30 Hz stays exact). If the value stands for whole seconds, `XSeconds` and `world.Time.WholeSecondsToTicks(XSeconds)` | `SimulationWorld.Acquisition.cs`: `OrderedAttackGraceFrames = 45`. `ScriptEngine.Phase13.cs`: `SupplyScanSeconds = 10`. `SkirmishAi`: 16 deadlines (`RetryFrames`, `MemoryFrames` …). |
| **(c) Execution cadence with modulo** | Period **and** phase in frames, both scaled: `tick % world.Time.OriginalFrames(PeriodFrames) == world.Time.OriginalFrames(PhaseFrames)`. At 30 Hz this is the old expression; at 60 Hz the cadence falls on the same points in game time. A phase drawn from a hash or an identifier is drawn in frames and then scaled | `SimulationWorld.Step`: `tick % VisionInterval == 0`. `SkirmishAi`: `(_tick + _phase) % _thinkInterval`, `_phase = Time.OriginalFrames(Player % FireIntervalFrames)`. `TensileFormationUpdate`: `_life % OriginalFrames(30) == OriginalFrames(29)`. |
| **(d) Integer division of the rate** | Division of the **original rate** as a constant in frames, then scale: `SimulationRate.OriginalFramesPerSecond / 4` is 7 frames at every rate (14 ticks at 60 Hz, not `60 / 4 = 15`) | `SlavedUpdate` (`SlaveModules.cs`): `UpdateRateFrames = SimulationRate.OriginalFramesPerSecond / 4`. |
| **(e) Literal that means order (W2)** | **do not scale.** Write it with `TickOrder.Next` (`Time/TickOrder.cs`): `now + TickOrder.Next`, `frame - TickOrder.Next`, `2 * TickOrder.Next`. Comparisons "in the same tick" (`LastFireTick == world.Tick`) need no marking. `SimulationTiming.SleepNone` (shortest sleep of an update, one tick) is the same kind and stays | `JetAIUpdate.cs`: `_transferTick = now + 2 * TickOrder.Next`. `RailroadBehavior.FramesUnpulledLongEnoughToUnhitch = 2 * TickOrder.Next`. `SimulationWorld.Step`: `_tick = tick + TickOrder.Next`. |
| **(f) Frame counters of the map scripts** | The value of the map is original frames; it is scaled as soon as it becomes a tick countdown. Randomness is drawn in frames, **then** scaled. Second values: round up to original frames, then scale | `ScriptEngine.Actions.cs` `SET_TIMER`/`SET_RANDOM_TIMER`: `timer.Value = _world.Time.OriginalFrames(frames)`; `SecondsToFrames`. `ScriptEngine.Sequential.cs` `SetSequenceTimer` (`*_FOR_FRAMECOUNT`). |
| **(g) Rounding, lower bound, counter, randomness at run time (W1)** | calculate in frames and scale only the result – through the one grid helper `FrameGrid` (§2): `AtLeastOneFrame` (lower bound), `CountdownReload` (counter with period N + 1), `DrawFrames` (randomness over a range of durations), `FloorToFrame`/`CeilingToFrame`; or by hand `Time.OriginalFrames(… Time.ToOriginalFrames(ticks) …)` where there is arithmetic between the conversion there and back | `WeaponInfo.ScaledByRateOfFire`: `time.OriginalFrames(max(MinimumShotDelayFrames, floor(time.ToOriginalFrames(ticks) / Multiplikator)))`. `AutoHealBehavior.Update`: `return world.Time.AtLeastOneFrame(data.HealingDelayTicks)`. `PointDefenseLaserUpdate`: `_nextScan = world.Time.CountdownReload(data.ScanTicks)`. `SlowDeathBehavior`: `world.Time.DrawFrames(world.LogicRandom, 0, data.SinkDelayVarianceTicks)`. |

Further rules:

- **Values per second at run time** (share of the current hit points and the like): `world.Time.PerSecondToPerTick(wert)` – one `Fixed` division by the rate, the same expression as the former `/ Fixed.FromInt(30)`; the order of the operands in the argument stays (the rounding of `Fixed` depends on it). An amount that is spread over a duration (`MaxHealth / TimeForFullHealTicks`) goes through `world.Time.SharePerTick(gesamt, ticks)` – the same division, but countable (§11.3). `world.Time.TicksPerSecondFixed` stays for calculations that do not form an amount per tick (two sites in `FlightModules.cs`: a comparison and a duration in seconds).
- **Fallback values of `ModuleConfigReader.Ticks(configuration, name, fallbackOriginalFrames)`** are original frames and are scaled by the reader. No caller pre-computes the fallback value.
- **Additive raw values per frame** (speed per frame, increment per frame) during the catalog build through `configuration.Time.PerOriginalFrameToPerTick(…)` or `PerOriginalFrameSquaredToPerTickSquared(…)`; literals in the body as a constant with suffix `PerFrame`/`PerFrameSquared` and at run time through `world.Time.…`. **Multiplicative factors per frame stay unchanged** and are in §5.1.
- **Constants of the world** are resolved into ticks once in the constructor of `SimulationWorld` (`private readonly long _…Ticks`, `VisionInterval`); no step of the world converts a constant. These fields are not state (not in the snapshot, not in the hash) and not catalog fields.
- **Rule values of the option records are original frames, run budgets stay ticks** (§8.1). The records keep their shape and field names; the name `…Ticks` of a rule value is historical, the unit is in the comment of the record.
- **State fields** keep their names (snapshot keys and the content hash of the input manifest depend on field names). New instance fields on catalog types (`ObjectTemplate`, `WeaponInfo`, every `…Data` of a module) change the input manifest – constants (`const`, `static`) do not.
- **No silent default:** no simulation function gets a default parameter for the rate. Whoever creates a world or a catalog names the rate. (The parameters `file`/`line` of some functions of the time service are caller information for the probe, §2, and are never passed by hand.)

The convention is cast as a rule set in `SourceRules` (§10); a bare tick literal in `src/CnCNew.Simulation` makes the test fail.

## 2. The time contract

`src/CnCNew.Simulation/Time/`:

- **`SimulationRate`** – immutable value, exactly three instances (`Hz30`, `Hz60`, `Hz120`; `Original` = `Hz30`). `FromTicksPerSecond(n)` throws `UnsupportedSimulationRateException` for everything else. `OriginalFramesPerSecond = 30` is the only named 30 of the simulation and is used only in conversions and in constants "in frames". `Combine(fingerprint)` folds the rate into the definition fingerprint (unchanged at 30 Hz, §4).
- **`SimulationTime`** – the time service of a rate (`rate.Time`, one instance per rate; no allocation, no conversion in the hot path other than one multiplication or one `Fixed` division).
- **`FrameGrid`** – the one grid helper (extension methods on `SimulationTime`) for run-time calculations that have to stay on the 30 Hz grid (W1).
- **`SimulationTimeProbe`** – probe for the trial run (below).
- **`SimulationTiming`** – now rate-independent only: `SleepNone`, `SleepForever` (sentinel value, never scaled), `MillisecondsPerSecond`, `DegreesToRadians`, `RatioToFixed`. The constant `LogicFramesPerSecond` no longer exists in `src/CnCNew.Simulation`.
- **`TickOrder`** – `Next = 1` for literals that mean order.

| Function of `SimulationTime` | Calculation | Rounding | Why 30 Hz is unchanged |
|---|---|---|---|
| `OriginalFrames(n)` | `n · (Rate / 30)` | exact (factor 1, 2, 4); overflow throws | factor 1 |
| `ToOriginalFrames(ticks)` | `floor(ticks · 30 / Rate)` | rounded down | identity |
| `TicksPerOriginalFrame` | `Rate / 30` | – | 1 |
| `IsOriginalFrameBoundary(tick)` | `tick % (Rate / 30) == 0` | – | always true |
| `DurationMillisecondsToTicks(long)` | `((ms · 30 + 999) / 1000) · (Rate / 30)` | rounded **up** to whole original frames (integer arithmetic), then scaled exactly | the same integer expression, times 1 |
| `DurationMillisecondsToTicks(decimal)` | `Ceiling(ms · 30 / 1000) · (Rate / 30)` | as above, in `decimal` | the same decimal expression, times 1 |
| `DurationMillisecondsToFixedTicks(decimal)` | `ms · Rate / 1000` | once to `Fixed` (nearest value, half values away from zero); **not** on the grid, because the original (`parseDurationReal`) does not round here | `ms · 30 / 1000` as before |
| `SecondsToTicks(decimal)` | `Ceiling(s · 30) · (Rate / 30)` | rounded up to original frames | the same expression, times 1 |
| `WholeSecondsToTicks(long)` | `s · Rate` | exact | `s · 30` |
| `PerSecondToPerTick(decimal)` | `v / Rate` | once to `Fixed` | `v / 30` |
| `PerSecondToPerTick(Fixed)`, `TicksPerSecondFixed` | `v / Fixed(Rate)` | one `Fixed` division | formerly `v / Fixed.FromInt(30)` |
| `SharePerTick(Fixed gesamt, Fixed ticks)` | `gesamt / ticks` | one `Fixed` division | the former division, unchanged at every rate |
| `PerSecondSquaredToPerTickSquared(decimal)` | `a / (Rate · Rate)` | once to `Fixed` | `a / 900` |
| `DegreesPerSecondToRadiansPerTick(decimal)` | `deg · π / 180 / Rate` (in this order) | once to `Fixed` | last divisor 30 as before |
| `PerOriginalFrameToPerTick(decimal)` | `v / (Rate / 30)` | one decimal division by 1, 2 or 4, then once to `Fixed` | division by 1 is the identity: `Fixed.FromDecimal(v)` as with the raw read |
| `PerOriginalFrameToPerTick(Fixed)` | `v / Fixed(Rate / 30)` | one `Fixed` division | at 30 Hz the value is returned **without calculation** |
| `PerOriginalFrameSquaredToPerTickSquared(decimal / Fixed)` | `v / (Rate / 30)²` | division by 1, 4 or 16 | as above |
| `ToWholeSeconds`, `ToWholeMinutes`, `ToMilliseconds` | `ticks / Rate`, `ticks / (60 · Rate)`, `ticks · 1000 / Rate` | integer, towards zero | – |
| `ToSecondsRational(ticks)`, `ToSeconds(ticks)` | `(ticks, Rate)` or `Fixed(ticks) / Fixed(Rate)` | exact or one `Fixed` division | – |

**`FrameGrid` – one name per concept.** Two briefs of stage 2 had written the same helper in parallel (`Modules/BuiltIn/FrameGrid.cs`, modules A–M; `Modules/BuiltIn/TickGrid.cs`, modules N–Z). Both files are deleted; there is exactly `Time/FrameGrid.cs`.

| Function | Calculation | 30 Hz | replaces |
|---|---|---|---|
| `time.FloorToFrame(ticks)` | `OriginalFrames(ToOriginalFrames(ticks))` – rounded down to whole frames, in ticks | `ticks` | `FrameGrid.Floor` |
| `time.CeilingToFrame(ticks)` | rounded up to whole frames, in ticks | `ticks` | `FrameGrid.Ceiling` |
| `time.AtLeastOneFrame(ticks)` | `Math.Max(ticks, TicksPerOriginalFrame)` – the lower bound "one frame" (`if (delay < 1) delay = 1` in the original and every lower bound of our own) | `Math.Max(ticks, 1)` | `FrameGrid.AtLeastOneFrame` and 20 times `Math.Max(x, world.Time.TicksPerOriginalFrame)` of the modules N–Z |
| `time.DrawFrames(random, minTicks, maxTicks)` | one random draw over `ToOriginalFrames(min)…ToOriginalFrames(max)`, result scaled (one draw, the same random stream at every rate, the result stays on the grid) | `random.Range(min, max)` | `FrameGrid.DrawFrames` and `TickGrid.RandomTicks` (17 calls) |
| `time.CountdownReload(ticks)` | `OriginalFrames(ToOriginalFrames(ticks) + 1) − 1` – reload value of a counter of the form "> 0: −1, otherwise act and set to N" (period N + 1 frames) | `ticks` | `FrameGrid.CountdownReload` and `TickGrid.CountdownActingAfterZero` (10 calls) |

**Proof of equality** (`Tests.TickRate.Grid.cs`, `TickRateGridMergedHelpersAreTheSameFunctions`, each at 30, 60 and 120 Hz, the replaced expressions as oracle): `CountdownReload(t)` against `t + (TicksPerOriginalFrame − 1)` (the form of `TickGrid.CountdownActingAfterZero`) for every duration on the grid from 0 to 20,000 frames, with the period `(N + 1) · Ticks je Bild`; `AtLeastOneFrame(t)` against both former spellings for every tick value from −50 to 50,000; `DrawFrames` against the expression of `TickGrid.RandomTicks` over 1,681 ranges with two identically seeded streams – same result **and** same stream state afterwards; `FloorToFrame`/`CeilingToFrame` as a grid property; at 30 Hz every function is the identity or `max(t, 1)`. The two counter forms are equal **only on the grid** (off it, `CountdownReload` cuts off the remainder, `CountdownActingAfterZero` kept it – the test records that too). Every call site passes a duration on the grid (catalog duration through `ModuleConfigReader.Ticks`, `OriginalFrames(…)`, the result of `DrawFrames`, `WeaponInfo.DelayBetweenShotsTicks`); the probe counts every call off the grid (counter `OffGrid`), in the trial runs 0 of more than 20 million calls (§11.3).

**`SimulationTimeProbe` – probe, not state.** `SimulationTime.Probe` is normally `null`; every conversion then checks only this one field. A tool attaches a probe to the time service of a rate (`--time-probe`, `rate-audit`), lets it run and reads the counters. None of it is read by the simulation, written into a snapshot or hashed (the test `TickRateProbeOnlyObserves` checks that with a probe every function returns the same; the 30 Hz trial runs with a probe carry the final hashes of the reference manifest). The time service of a rate belongs to all worlds of this rate in the process; a probe is meant for one run at a time. Counting is per site in the source text (file and line through `CallerFilePath`/`CallerLineNumber`):

| Counter | Counts | Where |
|---|---|---|
| `RunTimeRoundedToZero` | an amount per tick that arises from state at run time is zero although the input was not | `PerSecondToPerTick(Fixed)`, `SharePerTick`, `PerOriginalFrameToPerTick(Fixed)`, `PerOriginalFrameSquaredToPerTickSquared(Fixed)` |
| `MinimumOneFrame` | the lower bound "one frame" took effect | `AtLeastOneFrame` |
| `OffGrid` | a duration that should lie on the grid does not lie on it | `ToOriginalFrames`, `CountdownReload`, `DrawFrames`, `AtLeastOneFrame` |
| `RoundedToFrame` | `FloorToFrame`/`CeilingToFrame` changed the value (expected between two frame boundaries) | `FloorToFrame`, `CeilingToFrame` |
| Quantisation | per decimal conversion (catalog build): calls, values ≠ 0, rounded to zero, relative error above 1 % and above 10 % against the exact decimal value, worst case | `PerSecondToPerTick(decimal)`, `PerSecondSquaredToPerTickSquared`, `DegreesPerSecondToRadiansPerTick`, `DurationMillisecondsToFixedTicks`, `PerOriginalFrame…(decimal)` |

The test `Tests.TickRate.cs` keeps the former formulas as oracle (`FormerTiming`) and compares at 30 Hz over 120,001 whole millisecond values, about 8,000 decimal values per function and both signs; at 60 and 120 Hz it checks the grid (duration = 30 Hz value times 2 or 4) and the scaled values.

## 3. Converted sites

Three parts of the phase, each with its own way of counting: stage 1 counts **occurrences** in the old source text (state `a1ab03b`), stage 2 and the completion count **sites** (one symbol or one literal; counting rule in `docs/TICKRATE-AUDIT.md` §1.2). The numbers of the stages come from their result reports; the individual sites of stage 2 with their origin in the original are in Appendix A.

| Part | What | Number |
|---|---|---|
| Stage 1 | uses of the constant `LogicFramesPerSecond` (32 files) | 63 |
| Stage 1 | direct conversion calls `SimulationTiming.…(…)` (12 files) | 41 |
| Stage 1 | calls of `ModuleConfigReader.Ticks` (33 files; among them 15 with fallback value ≠ 0, now original frames) | 133 |
| Stage 1 | additive raw values per frame, scaled during the catalog build | 9 |
| Stage 1 | frame counters of the map scripts | 4 |
| Stage 1 | order literals marked as a pattern | 3 |
| Stage 1 | format sites (write the rate, check it, create the world with the rate) | 9 |
| **Stage 1** | **Sum of the occurrences** | **262** |
| Stage 2 | world, catalogs, replay | 27 |
| Stage 2 | modules A–M | 46 |
| Stage 2 | modules N–Z | 69 |
| Stage 2 | scripts, mission, skirmish, scenarios | 46 |
| **Stage 2** | **Sum of the sites** | **188** |
| Completion | simulation: found by `SourceRules` or leftovers of the merge (§3.3) | 4 |
| Completion | tools (§9) | 7 |
| **Phase 20** | **Sites of stage 2 and of the completion in the simulation** | **192** |

### 3.1 Stage 1 by kind and file

| Kind | Sites | Files |
|---|---|---|
| uses of the constant `LogicFramesPerSecond` | 63 | 32 (9 of them in `SimulationTiming.cs`) |
| – among them `const` expressions and one `static readonly` | 9 + 1 | `AirModules`, `DockMachine`, `Phase5Modules`, `ProjectileModules`, `SlaveModules` (2), `SpawnModules`, `FiringTracker`, `ScriptEngine.Phase13`; `TransportModules` (`static readonly`). The remaining four of the 13 `const` expressions of the inventory (W3) are in `CnCNew.Game` and belong to Phase 21 |
| – among them divisions `/ Fixed.FromInt(LogicFramesPerSecond)` | 19 | 13 module files |
| direct conversion calls `SimulationTiming.…(…)` | 41 | 12 |
| calls of `ModuleConfigReader.Ticks` (all go through the time service) | 133 | 33 |
| – among them fallback value ≠ 0, now original frames | 15 | 10 |
| additive raw values per frame, scaled during the catalog build | 9 | 6 |
| frame counters of the map scripts | 4 sites | `ScriptEngine.Actions.cs` (`SET_TIMER`, `SET_RANDOM_TIMER`), `ScriptEngine.Sequential.cs` (`SetSequenceTimer`: five `*_FOR_FRAMECOUNT` actions), `ScriptEngine.TeamQueues.cs` (`teamInitialIdleFrames`) |
| order literals marked as a pattern | 3 | `JetAIUpdate.cs` (2), `ScriptEngine.Phase13.cs` (1) |
| format sites (write the rate, check it, create the world with the rate) | 9 | `WorldSnapshot`, `MissionSnapshot`, `ReplayRunner`, `SkirmishMatchSession`, `ReferenceMatchScenario`, `StressScenario` |

| File (`src/CnCNew.Simulation/…`) | Constant | Conversion calls | `Ticks` fallback value ≠ 0 | raw frame values |
|---|---|---|---|---|
| `Mission/MissionSnapshot.cs` | 2 |  |  |  |
| `Modules/BuiltIn/AirModules.cs` | 2 |  |  |  |
| `Modules/BuiltIn/AirWeaponModules.cs` |  | 2 | 4 | 1 |
| `Modules/BuiltIn/BridgeModules.cs` | 2 | 1 |  |  |
| `Modules/BuiltIn/ContainModules.cs` | 1 |  |  |  |
| `Modules/BuiltIn/DockMachine.cs` | 1 |  |  |  |
| `Modules/BuiltIn/FieldEffectModules.cs` | 1 |  | 1 |  |
| `Modules/BuiltIn/FlightModules.cs` | 5 |  |  |  |
| `Modules/BuiltIn/JetAIUpdate.cs` | 3 |  |  |  |
| `Modules/BuiltIn/MatchModules.cs` | 1 |  |  |  |
| `Modules/BuiltIn/MineModules.cs` | 2 |  | 1 |  |
| `Modules/BuiltIn/ModuleConfigReader.cs` |  | 1 |  |  |
| `Modules/BuiltIn/Phase12Modules.cs` | 1 |  | 1 |  |
| `Modules/BuiltIn/Phase5BlockerModules.cs` |  |  | 1 |  |
| `Modules/BuiltIn/Phase5Modules.cs` | 2 | 4 | 1 | 1 |
| `Modules/BuiltIn/ProjectileModules.cs` | 3 |  | 2 |  |
| `Modules/BuiltIn/RailroadModules.cs` |  |  | 1 | 3 |
| `Modules/BuiltIn/RepairModules.cs` |  | 1 |  |  |
| `Modules/BuiltIn/RetailMatchModules.cs` | 2 |  |  |  |
| `Modules/BuiltIn/SlaveModules.cs` | 3 |  |  |  |
| `Modules/BuiltIn/SpawnModules.cs` | 1 |  |  |  |
| `Modules/BuiltIn/SpecialsModules.cs` |  |  |  | 1 |
| `Modules/BuiltIn/StructureModules.cs` |  |  | 1 |  |
| `Modules/BuiltIn/TransportModules.cs` | 2 | 2 |  |  |
| `Modules/BuiltIn/VehicleModules.cs` |  |  |  | 1 |
| `Modules/BuiltIn/WorldEffectModules.cs` | 2 |  | 2 |  |
| `Replay/ReplayRunner.cs` | 2 |  |  |  |
| `Scripting/ScriptEngine.Actions.cs` | 1 |  |  |  |
| `Scripting/ScriptEngine.Phase13.cs` | 2 |  |  |  |
| `Scripting/ScriptEngine.Powers.cs` | 1 |  |  |  |
| `Scripting/ScriptEngine.TeamAbilities.cs` | 1 |  |  |  |
| `Scripting/ScriptEngine.TeamQueues.cs` | 1 |  |  |  |
| `Scripting/ScriptEngine.cs` | 2 |  |  |  |
| `Skirmish/SkirmishMatchSession.cs` | 1 |  |  |  |
| `Snapshots/WorldSnapshot.cs` | 2 |  |  |  |
| `Templates/ObjectCreationListCatalog.cs` |  | 2 |  |  |
| `Templates/ObjectTemplate.cs` |  | 16 |  |  |
| `Templates/SpecialPowerCatalog.cs` |  | 1 |  |  |
| `Templates/TurretInfo.cs` | 1 | 5 |  | 2 |
| `Templates/UpgradeCatalog.cs` |  | 1 |  |  |
| `Templates/WeaponCatalog.cs` |  | 5 |  |  |
| `Testing/ReferenceMatchScenario.cs` | 1 |  |  |  |
| `Testing/StressScenario.cs` | 1 |  |  |  |
| `Time/SimulationTiming.cs` | 9 |  |  |  |
| `World/SimulationWorld.FiringTracker.cs` | 2 |  |  |  |
| **Sum** | 63 | 41 | 15 | 9 |

The nine scaled raw values: `TurretInfo.DefaultRate` (0.01 rad per frame, for missing `TurretTurnRate`/`TurretPitchRate`), `SpectreData.StrafingIncrement`, `StealthData.MoveThresholdSpeed` (read raw and compared with the speed per tick; the original reads a speed per second here – side finding of the inventory, not corrected), `RailroadData.RunningGarrisonSpeedMax`/`KillSpeedMin`/`SpeedMax`, `GrantStealthData.RadiusGrowRate`, `BattleBusData.ThrowForce` (is used as a speed per tick). Deliberately **not** scaled: `WeaponInfo.InstantSpeed` (999999 per frame means "instantly"; a sentinel value, not a data value), the fallback value `TwoPi · 100` for `NeutronMissileData.MaxTurnRate` (every value from π upwards snaps in immediately).

### 3.2 Stage 2 by directory, file and class

A, B, C, D are the classes of the inventory; **W1** (rounding, lower bound, counter N + 1 or randomness at run time) and **W2** (order, `TickOrder.Next`) take precedence over the class under which the inventory lists the site; **H** are time values of the drivers and probes in `Testing/`.

| File (`src/CnCNew.Simulation/…`) | A | B | C | D | W1 | W2 | H | Sum |
|---|---|---|---|---|---|---|---|---|
| `World/SimulationWorld.Acquisition.cs` |  | 2 | 1 |  | 1 | 3 |  | 7 |
| `World/SimulationWorld.Turrets.cs` | 1 | 3 |  |  | 1 |  |  | 5 |
| `World/SimulationWorld.Vision.cs` (with `Step`/`StepObserved`) |  |  | 1 |  |  |  |  | 1 |
| `World/SimulationWorld.cs` |  |  |  |  |  | 2 |  | 2 |
| `World/SimulationWorld.Construction.cs` | 1 |  |  |  | 1 | 1 |  | 3 |
| `World/SimulationWorld.CreationLists.cs` | 1 |  |  |  | 1 |  |  | 2 |
| `World/SimulationWorld.Projectiles.cs` |  |  |  |  | 1 |  |  | 1 |
| `Templates/WeaponCatalog.cs` (with the callers in `WeaponSlots.cs`, `FiringTracker.cs`) |  |  |  |  | 3 |  |  | 3 |
| `Replay/ReplayRunner.cs` |  |  |  |  |  | 2 |  | 2 |
| **World, catalogs, replay** | **3** | **5** | **2** | **0** | **9** | **8** | **0** | **27** |
| `Modules/BuiltIn/AirModules.cs` |  |  |  |  |  | 1 |  | 1 |
| `Modules/BuiltIn/AirWeaponModules.cs` |  | 1 | 1 |  | 5 | 1 |  | 8 |
| `Modules/BuiltIn/BridgeModules.cs` |  |  |  | 2 |  |  |  | 2 |
| `Modules/BuiltIn/CleanupAndSpyModules.cs` |  |  | 1 |  | 3 |  |  | 4 |
| `Modules/BuiltIn/CrateModules.cs` |  |  |  |  | 1 |  |  | 1 |
| `Modules/BuiltIn/DieAndUpdateModules.cs` |  |  |  |  | 1 |  |  | 1 |
| `Modules/BuiltIn/DockModules.cs` |  |  |  |  | 1 |  |  | 1 |
| `Modules/BuiltIn/FerryModules.cs` |  |  |  |  | 2 |  |  | 2 |
| `Modules/BuiltIn/FieldEffectModules.cs` | 3 |  |  |  |  |  |  | 3 |
| `Modules/BuiltIn/FlightModules.cs` |  | 1 |  |  | 9 | 1 |  | 11 |
| `Modules/BuiltIn/HackerAndAssaultModules.cs` |  |  |  |  | 2 |  |  | 2 |
| `Modules/BuiltIn/JetAIUpdate.cs` | 1 |  |  | 3 | 2 |  |  | 6 |
| `Modules/BuiltIn/LastBlockerModules.cs` |  |  |  |  | 1 |  |  | 1 |
| `Modules/BuiltIn/MatchModules.cs` | 1 | 1 |  |  |  |  |  | 2 |
| `Modules/BuiltIn/MineModules.cs` |  |  |  |  | 1 |  |  | 1 |
| **Modules A–M** | **5** | **3** | **2** | **5** | **28** | **3** | **0** | **46** |
| `Modules/BuiltIn/Phase12Modules.cs` |  |  |  |  | 4 |  |  | 4 |
| `Modules/BuiltIn/Phase5BlockerModules.cs` |  |  |  |  | 9 | 1 |  | 10 |
| `Modules/BuiltIn/Phase5Modules.cs` |  | 1 | 1 |  | 5 | 1 |  | 8 |
| `Modules/BuiltIn/ProjectileModules.cs` |  |  |  |  | 7 |  |  | 7 |
| `Modules/BuiltIn/RailroadModules.cs` |  |  |  |  |  | 1 |  | 1 |
| `Modules/BuiltIn/RepairModules.cs` |  | 1 |  |  | 1 |  |  | 2 |
| `Modules/BuiltIn/SlaveModules.cs` | 5 |  |  |  | 3 |  |  | 8 |
| `Modules/BuiltIn/SpecialAbilityStaging.cs` |  |  |  |  | 1 |  |  | 1 |
| `Modules/BuiltIn/SpecialPowerModules.cs` |  |  |  |  |  | 1 |  | 1 |
| `Modules/BuiltIn/StructureModules.cs` |  |  |  |  | 4 |  |  | 4 |
| `Modules/BuiltIn/ToppleAndFallModules.cs` | 2 |  |  |  | 1 |  |  | 3 |
| `Modules/BuiltIn/TransportModules.cs` |  |  |  |  | 3 |  |  | 3 |
| `Modules/BuiltIn/VehicleModules.cs` | 2 |  |  |  | 3 |  |  | 5 |
| `Modules/BuiltIn/WorldEffectModules.cs` |  |  |  | 1 | 10 | 1 |  | 12 |
| **Modules N–Z** | **9** | **2** | **1** | **1** | **51** | **5** | **0** | **69** |
| `Skirmish/SkirmishAi.cs` |  | 19 | 3 |  |  |  |  | 22 |
| `Skirmish/SkirmishMatchSession.cs` |  | 1 | 2 |  |  | 1 |  | 4 |
| `Testing/ReferenceMatchScenario.cs` |  |  | 3 |  |  | 3 | 2 | 8 |
| `Testing/StressScenario.cs` |  |  |  |  |  |  | 1 | 1 |
| `Scripting/ScriptEngine.cs` | 1 |  |  |  |  |  |  | 1 |
| `Scripting/ScriptEngine.Actions.cs` |  | 1 | 1 |  |  | 1 |  | 3 |
| `Scripting/ScriptEngine.Phase13.cs` |  |  | 1 |  |  |  |  | 1 |
| `Testing/MissionScenario.cs` |  |  |  |  |  |  | 2 | 2 |
| `Testing/MissionObjectiveDriver.cs` |  |  | 1 |  |  |  | 1 | 2 |
| `Testing/ObjectiveDriver.cs` |  |  |  |  |  |  | 1 | 1 |
| `Testing/ObjectiveDriver.Commands.cs` |  |  |  |  |  |  | 1 | 1 |
| **Scripts, mission, skirmish, scenarios** | **1** | **21** | **11** | **0** | **0** | **5** | **8** | **46** |
| **Stage 2 total** | **18** | **31** | **16** | **6** | **88** | **21** | **8** | **188** |

The report of the modules N–Z lists its sites by the class of the inventory with W1/W2 as "of which" (A 15, B 13, C 12, D 1, E 28; of which W1 51, W2 5); the table above orders them like the three other briefs with precedence for W1/W2 (recounted from Appendix A.3: 13 sites are neither W1 nor W2). Multiple classes (A + W1, C / W1) are under W1. The 34 cooldowns of the leaves of the objective driver (`ObjectiveDriver.Leaves.cs` 31, `ObjectiveDriver.cs` 2, `ObjectiveDriver.Commands.cs` 1) count as one site: scaling happens at the one use in `Round`.

### 3.3 Completion of the phase (this state)

| File | Symbol | Cl. | Was | Is |
|---|---|---|---|---|
| `Modules/BuiltIn/CombatModules.cs` | `FireWeaponUpdate.Update` (`_readyTick`) | W1 | `weapon.ClipReloadWith(bonuses, conditions)` / `DelayBetweenShotsWith(bonuses, conditions)` – truncated in ticks, at least one tick | the overloads with `world.Time` (truncated in frames, at least one frame) |
| `Modules/BuiltIn/JetAIUpdate.cs` | `BeginReload` (`time`) | W1 | `weapon.ClipReloadWith(global, conditions)` | `weapon.ClipReloadWith(world.Time, global, conditions)` |
| `Templates/WeaponCatalog.cs` | `WeaponInfo.DelayBetweenShotsWith`/`ClipReloadWith` without time service, `ScaledByRateOfFire(global, conditions, ticks)` | – | two public overloads and one private helper, equal to the new form only at 30 Hz | **deleted**; the test keeps the former formula as oracle |
| `Modules/BuiltIn/Phase5Modules.cs` | `ProductionUpdate.Enqueue` | W1 | `Math.Max(totalTicks, 1)` (a build time of 0 takes one tick) | `time.AtLeastOneFrame(totalTicks)` (one frame); the function takes the time service, the only caller is `SimulationWorld` |
| `Modules/BuiltIn/Phase5Modules.cs` | `ProductionUpdate.Update` (`remaining`) | W2 | `head.RemainingTicks - 1` | `head.RemainingTicks - TickOrder.Next` (one step of the counter) |
| `Modules/BuiltIn/Phase12Modules.cs` | `HordeUpdate.OnSpawn` | (W1, already counted) | draw by hand `OriginalFrames(Range(1, Math.Max(1, ToOriginalFrames(UpdateRateTicks))))` | `world.Time.DrawFrames(LogicRandom, TicksPerOriginalFrame, AtLeastOneFrame(UpdateRateTicks))` – the same draw |
| 14 module files | 12 sites `wert / world.Time.TicksPerSecondFixed`, 8 sites `gesamt / ticks` | – | bare `Fixed` divisions | `world.Time.PerSecondToPerTick(wert)` or `world.Time.SharePerTick(gesamt, ticks)` – the same calculation, now countable |

Four sites are newly counted (the two callers, `Enqueue`, the counter step); the remaining rows are the same sites in the one spelling. `SimulationWorld.VisionIntervalTicks` stays as a `public const` alias for four callers outside the simulation (§6); inside `src/CnCNew.Simulation` nothing uses it.

## 4. Catalog per rate, fingerprint, input manifest

- `TemplateCatalog.Build(graph, bindings, rate, geometry)` builds the catalog for one rate; `TemplateCatalog.Rate`/`.Time` say for which. The rate runs as a parameter through all builders that produce ticks or values per tick (`UpgradeCatalog`, `WeaponCatalog`, `SpecialPowerCatalog`, `ObjectCreationListCatalog`, locomotors, `TurretSet.Read`, `ObjectTemplate`, base regeneration) and through `RatedModuleConfiguration` (configuration plus time service) into every `Parse` function of a module.
- `WorldConfiguration.Rate` is the rate of the world. `SimulationWorld` throws on creation if the catalog was built for a different rate. Sessions (`SkirmishMatchSession.Start`, `MissionSession.Start`) and scenarios take the rate from the catalog they are given: whoever builds the catalog names the rate, and a session cannot run with the wrong one.
- **Cost** (measured with `SimTool catalog-cost`, Release, not on a quiet machine, 6 October 2026, two runs): compiling the definitions 4.3 to 4.5 s and the bone table 0.6 s are incurred once and do not depend on the rate; `TemplateCatalog.Build` needs 0.08 to 0.22 s per rate (first call with JIT 0.35 to 0.38 s). **A cache per rate is not necessary**; whoever needs two rates builds twice from the same graph.
- **Fingerprint:** `SimulationRate.Combine` leaves the definition fingerprint unchanged at 30 Hz and folds the rate in at 60/120 Hz (`SHA-256(fingerabdruck + "+tick-rate:" + n)`). `GameData`, `SyntheticFixture` and the tools use this; a 60 Hz replay therefore fails on 30 Hz data twice over (rate and fingerprint).
- **Input manifest:** `baselines/input-manifest.json` hashes the built catalogs field by field (`CanonicalObjectHasher`). The catalog has exactly **one** new instance field (`TemplateCatalog.Rate`). So that the recorded 30 Hz value stays comparable, the hasher of the tool leaves out this one field **as long as it is the original rate** (`InputManifest.RateAtOriginal`); at every other rate it is described. This is a change to the checking tool, not to the baseline, and belongs in the review. All other catalog types have received no new field – not in the completion either (`SimulationTime.Probe` hangs on the time service of the rate, which the hasher leaves out at 30 Hz together with the field `Rate`).
- `TemplateCatalog.GravityPerTickSquared` still calculates on every access (one decimal division); a field on the catalog would change the input manifest. The world does not read the value in the hot path (`_significantHeight`, once per world); the probe sees the accesses of the modules (§11.4).

## 5. List for Phase 23 (correct at 30 Hz, different at 60 Hz; unchanged in Phase 20)

Merged from stage 1 and the four briefs of stage 2, without duplicates; two rows of stage 2 are done with the completion and are no longer here (`WeaponInfo.DelayBetweenShotsWith`/`ClipReloadWith` without time service, `ProductionUpdate.Enqueue`). 95 rows in six groups; a row can name several symbols of the same block. This is behaviour, not parameterisation: everything from class E of the inventory (138 sites there) is represented here or assessed in Appendix B as "load only" or "order without literal".

**State at the end of Phase 23 (9 October 2026, state `f3acc2e`).** Each of the 95 rows carries its disposition at the end of its last column ("— **Phase 23:** …"; the twelve rows of the script VM have carried it since the script package in the same style without a status word and count as done). Counted: **78 done**, **13 deliberately left as they are** (load only, pure ordering, test and tool code, or the solution lies elsewhere), **4 open** (§5.3 `Register`, §5.4 `ObjectiveDriver.Commands`, §5.6 quantisation of the catalog values and amounts per tick from state). "Done" means: the cause is named, corrected in a commit and measured by the rate-equivalence suite or a three-rate test – not that a human has seen it. Six done rows name an open remainder or the pending confirmation of the project owner in their note; the merged list of what is not equal or not verified is in §15.7.

### 5.1 Multiplicative factors per frame and mixed recursions (11)

Correct at 30 Hz; at 60 Hz the factor acts twice as often. For pure factors the root suffices, for recursions that mix factor and increment (W4) it does not.

| File | Symbol | What is different at 60 Hz |
|---|---|---|
| `Modules/BuiltIn/RailroadModules.cs` | `RailroadData.Acceleration` (1.01), `Braking` (0.99), `Friction` (0.97) | factors per frame on the train speed — **Phase 23:** done: factors once per frame of the locomotive, position along the track proportionally per tick, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `9871bc2` |
| `Modules/BuiltIn/RailroadModules.cs` | `RailroadBehavior.PushStart` (0.02), starting value 0.05, standstill threshold 0.1, damage `_pull.Speed * 10` | increment per frame before the factor (mixed recursion), speeds per frame as a literal, damage per tick of the overlap. `SpeedMax`, `KillSpeedMin`, `RunningGarrisonSpeedMax` have been per tick since stage 1 – at 60 Hz the train reaches the same top speed, but in half the time — **Phase 23:** done: every speed of the state is distance per frame, conductor and friction in the first tick of the frame; top speed after 2.4333 / 2.4167 / 2.4083 s (before 2.4333 / 1.1000 / 1.0000), `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `9871bc2` |
| `Modules/BuiltIn/AirWeaponModules.cs` | `NeutronMissileData.ForwardDamping`, `RelativeSpeed` | damping share per frame and thrust per frame² in the same step (`_velocity + speed·dir − damping·_velocity`) — **Phase 23:** done: recursion of thrust and damping once per frame, velocity in shares; flight time 8.0667 s at every rate; the end of the dive is read by `HeightDieUpdate` at the end of the frame, `docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md`, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, commit `41d1cbc`, `ad2b9e9` |
| `Modules/BuiltIn/FieldEffectModules.cs` | `TensileFormationUpdate.Friction` (0.95), `NewShare`/`LinkShare` (0.93/0.07), `BaseSlope` (0.3) | friction, position blending and slope acceleration per frame — **Phase 23:** done: recursion in the first tick of every frame of the sliding phase, sliding distance 65.528 at every rate; verified only by the three-rate test on the slope (the suite measures nothing on the flat map), `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `302e88a` |
| `Modules/BuiltIn/LastBlockerModules.cs` | `SmartBombHomingData.CourseCorrectionScalar` (0.99) | blending factor of the position per frame — **Phase 23:** done: blending once per frame, distance in shares; no case of the suite, `docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md`, commit `0f5fd14` |
| `Modules/BuiltIn/ToppleAndFallModules.cs` | `StructureToppleData.StructuralDecay` | decay of the structural integrity per frame — **Phase 23:** done: decay and acceleration in the first tick of the frame, angle proportionally per tick; lies flat after 4.3333 s at every rate, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `f8d0077` |
| `Modules/BuiltIn/ToppleAndFallModules.cs` | `ToppleUpdateData.InitialAccelPercent` | angular acceleration per tick² as a share of an impact speed per tick: needs a further factor 30/rate (`InitialVelocityPercent` scales with the impact speed by itself, `BounceVelocityPercent` is a ratio without time) — **Phase 23:** done: angular velocity and angular acceleration per frame; fall 1.2000 s at every rate (before 1.2000 / 0.8000 / 0.5667); the toppling force is converted once, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `021b318`, `bcc54ae` |
| `Modules/BuiltIn/TransportModules.cs` | `HelicopterSlowDeathData.SpiralOrbitForwardSpeedDamping` | damping per frame (`_forwardSpeed *= …`) — **Phase 23:** done: damping once per frame in the frame step of the spiral, the record holds the values per frame of the 30 Hz catalog, `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, commit `cee88c4` |
| `Modules/BuiltIn/AirWeaponModules.cs` | `EmpUpdate.Update` (`_scale`, factor 0.05 per tick) | the scaling approaches the target in half the time (state and presentation, no gameplay effect) — **Phase 23:** done: share only on the frame ticks of the pulse (the escape "original grid" (Ausweg „Originalraster“), `docs/SIMULATION-CORE.md` §41; no gameplay effect), `docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md`, commit `9132127` |
| `Modules/BuiltIn/BridgeModules.cs` | `BridgeScaffoldBehavior.Update` | deceleration ramp proportional to the remaining distance per tick (geometric decay) — **Phase 23:** done: step of the original in the first tick of the frame, per tick the same share of the remainder; no case of the suite, one three-rate test, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `501350c` |
| `Modules/BuiltIn/RailroadModules.cs` | `RailroadBehavior.Update` ("cars only exist in the next tick"), `Collide` (pushing away by a quarter of the overlap per tick) | order through `SleepNone` (stays); the pushing away is twice as fast — **Phase 23:** done: collision response and pushing away once per frame of the train (the escape "original grid"), first update one frame after appearing, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `9871bc2` |

### 5.2 Integration per tick and thresholds against one tick step (27)

Euler steps, arrival radii of "one tick of travel", snapping below one step, per-tick quantities read as absolute quantities.

| File | Symbol | What is different at 60 Hz |
|---|---|---|
| `World/Entity.cs` | `Entity.Speed` | distance per tick; every reader that takes the value as an absolute quantity sees half of it (see `TryTopple`) — **Phase 23:** deliberately left as it is: `Entity.Speed` remains the distance per tick; the movers keep their speed per frame themselves (`_frameSpeed`, above 30 Hz `Entity.SpeedPerFrame`), the readers are converted, `docs/rate-invariance/RATE-INVARIANCE-GROUND.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, commit `bf028df`, `3b5c781` |
| `World/SimulationWorld.Collision.cs` | `AvoidLocalBlockers`, `StepBlocked` | look-ahead and avoidance step are one tick of travel and halve — **Phase 23:** done: avoidance step and look-ahead from the step of the frame, decided in the tick of the decision; a mover that already had its turn in the tick is read at the end of the frame, `docs/rate-invariance/RATE-INVARIANCE-GROUND.md`, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, commit `bf028df`, `2db1fc6` |
| `World/SimulationWorld.Collision.cs` | `StationaryBlockersAhead` | the look-ahead is the vector passed in by the caller (`Speed × 25` ticks of travel in `Phase5Modules.cs`) — **Phase 23:** done: look-ahead 25 times the travel of the frame; end-of-frame view as above, `docs/rate-invariance/RATE-INVARIANCE-GROUND.md`, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, commit `bf028df`, `2db1fc6` |
| `World/SimulationWorld.Collision.cs` | `ResolveFootprintCollisions`, `TrySquish` | sampling per movement step: overlap is detected earlier, the order of the collision events changes — **Phase 23:** done: check once per frame of the driving object (escape "original grid") – at first with the last fraction, since round 4 in the first tick, the driving object stands at the end of the frame for it, `docs/rate-invariance/RATE-INVARIANCE-GROUND.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, commit `1c65446`, `e910174` |
| `World/SimulationWorld.Collision.cs` | `TryTopple` (`mover.Speed`) | topple force = distance per tick: half the force — **Phase 23:** done: topple force from the speed per frame, `docs/rate-invariance/RATE-INVARIANCE-GROUND.md`, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `1c65446`, `bcc54ae` |
| `World/SimulationWorld.Turrets.cs` | `IsAligned` (0.035 rad) | fixed angle against half the step size: the first shot falls on a different point in time — **Phase 23:** done: release when start and target of the frame step are aligned; angle at the first shot 1.57080 rad at every rate, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, commit `32eb25c` |
| `World/SimulationWorld.Turrets.cs` | `AimStep`, `RecenterStep`, `ScanStep`, `TurnAngle`, `TurnPitch` | Euler step with snapping below one tick step; reversal of the sweep at 0.035 rad — **Phase 23:** done: step once per frame of the turret with the turn rate of the 30 Hz catalog, travel per tick; angle at the end of the frame bit-identical, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, commit `32eb25c` |
| `World/SimulationWorld.Turrets.cs` | `IdleScanStep`, `ScheduleScan` | the interval now lies on the 30 Hz grid; the rotation after it is Euler per tick, and the point in time at which the turret comes to rest (start of the next deadline) depends on it — **Phase 23:** done: first step and scan deadline occupy one frame each; idle scan over 1,500 frames bit-identical, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, commit `276a8de` |
| `Templates/ObjectTemplate.cs` | `LocomotorInfo.CloseEnoughDist` (0 = one tick of travel; consumer in `Phase5Modules.cs`) | an arrival radius of one tick of travel halves — **Phase 23:** done: arrival radius `max(CloseEnoughDist, Weg eines Bildes)`; since round 3 the march ends in the first tick of the last frame, the remainder is coasted out, `docs/rate-invariance/RATE-INVARIANCE-GROUND.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, commit `bf028df`, `2117316` |
| `Modules/BuiltIn/AirWeaponModules.cs` | `NeutronMissileUpdate.Update`, `DoAttack` | intermediate point "reached" against the tick step, the launch costs one tick, Euler step with damping — **Phase 23:** done: launch one frame after the shot, intermediate point and ground once per frame, `docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md`, commit `41d1cbc` |
| `Modules/BuiltIn/AirWeaponModules.cs` | `SpectreGunshipUpdate.Fly`, `Strafe`, `Orbit` (`_okToFireHowitzerCounter`) | polyline of the trajectory, arrival radius one tick step, counter per strafe tick against a duration in ticks (correct in time only as long as strafing happens every tick) — **Phase 23:** done: flight, orbit and target point as a frame step on the grid of the world, one counter per frame; **open:** the constants still compute `Wert je Tick × r` (at a threshold this can cost one frame), `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md`, commit `e7a3153`, `fc0480f` |
| `Modules/BuiltIn/FerryModules.cs` | `RailedTransportDockUpdate.DoPullInDocking`, `DoPushOutDocking` | half the step size, the arrival thresholds of 6 and 3 units fall on different ticks — **Phase 23:** done: arrival question with the last tick of every frame (escape "original grid"); position within 0.001 units, not bit-identical; no case of the suite, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `b53cbd3` |
| `Modules/BuiltIn/FieldEffectModules.cs` | `TensileFormationUpdate.Update` (physics block) | Euler step with friction and blending per tick; since stage 2 the sliding phase lasts 300 frames at every rate, so the recursion runs twice as often at 60 Hz — **Phase 23:** done: see §5.1, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `302e88a` |
| `Modules/BuiltIn/FlightModules.cs` | `JetSlowDeathBehavior.Update` (fall), `ParachuteContain.Update` (free fall, descent, arrival radius one tick of distance), `CountermeasuresBehavior.MoveFlares` | semi-implicit Euler steps, linear braking per tick — **Phase 23:** done: fall, descent and braking once per frame, movement in fractions; the movers publish their frame remainder, `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md`, commit `307bf14`, `8435ba8`, `c5a8a35`, `e06259f` |
| `Modules/BuiltIn/FlightModules.cs` | `ParticleUplinkCannonUpdate.MoveBeam` (manual) | waypoint change at remaining distance ≤ one tick of distance — **Phase 23:** done: travel as a frame step, next waypoint from the following frame on, `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, commit `5954338` |
| `Modules/BuiltIn/JetAIUpdate.cs` | `TaxiAlongPath` (arrival radius `max(Speed, 1)`), `TurnTo`, `TakeOff` (both branches), `Land` (both branches; touchdown threshold 1 unit, sinking `max(SpeedLimitZ, Speed/4)`), `ControlHeight` (`Speed/8` as acceleration, `Speed/2` as cap) | Euler steps and thresholds against the tick step; `Speed/8` mixes a quantity per tick with one per tick² — **Phase 23:** done: the states decide once per frame, movement in fractions; since round 3 with the constants per frame of the 30 Hz catalog (`LocomotorFrames`) and the first update one frame after the appearance, `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md`, commit `60fcb57`, `bb00e5f`, `346cadc`, `cc7fd75` |
| `Modules/BuiltIn/JetAIUpdate.Flight.cs` | `CircleStep` | the settled orbit (measured ≈ 0.93 · radius) depends on the step size — **Phase 23:** done: orbit from the frame step; jet sortie over 1,500 frames bit-identical at 30, 60 and 120 Hz, `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md`, commit `bbee8ab`, `346cadc` |
| `Modules/BuiltIn/Phase5Modules.cs` | `AIUpdateInterface.Move` (arrival radius `max(CloseEnoughDist, Speed)`, turning, acceleration/braking distance, position step, `AvoidLocalBlockers`), `MoveWinged`, `SteerWinged`, `MoveStraight` | Euler per tick; arrival radius and turning circle depend on the tick step (half the travel per tick), braking distance `v²/(2a)` in tick units — **Phase 23:** done: ground: speed and heading once per frame, position per tick; winged flight likewise; braking distance 110.000 at every rate (before 110.000 / 111.250 / 111.875), `docs/rate-invariance/RATE-INVARIANCE-GROUND.md`, `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md`, commit `bf028df`, `bbee8ab`, `346cadc` |
| `Modules/BuiltIn/PhysicsAndMinefieldModules.cs` | `PhysicsBehavior.Update` (free fall) | Euler per tick with `GravityPerTickSquared`; the landing time deviates by fractions of a frame — **Phase 23:** done: gravity per frame², landing judged on the whole step; no case of its own in the suite; **open:** the free fall does not publish its frame remainder, `docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md`, commit `2777f86` |
| `Modules/BuiltIn/ProjectileModules.cs` | `DumbProjectileBehavior.CalcFlightPath` (`_segments = ceil(length / _speed)`), `Update` (one path point per tick, detonation in the update after the last point, tracking per tick) | flight duration in whole ticks instead of whole frames; "one tick later" is half a frame — **Phase 23:** done: one path point per frame, detonation one frame after the last point; Battlemaster shell 0.3000 s at every rate, `docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md`, commit `2ed62cd` |
| `Modules/BuiltIn/ProjectileModules.cs` | `MissileAIUpdate.Launch` (`_acceleration = weapon.WeaponSpeed`), `Update` (`_noTurnDistanceLeft` from the previous tick), `Move` (Euler, arrival at remaining distance ≤ one tick of flight) | weapon speed per tick as acceleration per tick²: full speed after one tick, half the value; arrival threshold halved — **Phase 23:** done: speed and acceleration per frame (frame²), state machine once per frame; since round 4 from `LocomotorFrames`, `docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, commit `c4a0ac1`, `65b9783`, `ec9f90a` |
| `Modules/BuiltIn/SpecialAbilityStaging.cs` | `SpecialAbilityUpdate.StepCaptureFlash` (`_flashPhase += (1 − prep/Preparation) / 3` per tick) | the flash phase grows twice as fast: twice as many `CaptureFlash` events — **Phase 23:** done: flash phase only in the ticks that complete a frame of the preparation: 4 / 4 / 4 flashes (before 4 / 9 / 19), `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, commit `d923cea` |
| `Modules/BuiltIn/SpecialAbilityStaging.cs` | `SpecialAbilityUpdate.TurnToward` | snapping within one tick step of the rotation — **Phase 23:** done: decision in the first tick of every frame against one frame of rotation; **open:** the rotation per frame is `Rate je Tick × r` (a unit exactly a whole number of steps before the target can deviate by one frame), `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, commit `95bcf3d` |
| `Modules/BuiltIn/StructureModules.cs` | `StructureCollapseUpdate.Update` (Euler: `_height -= _velocity`, `_velocity -= g · (1 − Damping)`; at most one burst per tick) | the collapse time deviates by the Euler order — **Phase 23:** done: sink speed per frame, height per tick proportionally; visible height within 0.001 units, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `01535b0` |
| `Modules/BuiltIn/ToppleAndFallModules.cs` | `ToppleUpdate.Update` (Euler, no acceleration in the bounce tick), `StructureToppleUpdate.StepToppling`, `ApplyCrushingDamage` (one weapon shot per tick in which the leading edge reaches further), `_doneAtTick` (done in the following tick), `HeightDieUpdate.Update` (`_lastZ` of the previous tick) | the fall time deviates; twice as many crush shots; `VelocityBounceLimit` and `AccelFactor` are per tick since stage 2 — **Phase 23:** done: toppling and structure fall per frame; `HeightDieUpdate` looks once per frame (escape "original grid") and reads the end of the frame, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, commit `021b318`, `f8d0077`, `5440888`, `ad2b9e9`, `0cd38b7` |
| `Modules/BuiltIn/TransportModules.cs` | `HelicopterSlowDeathBehavior.Update` (spin/orbit/fall, comparison `Tick − _lastSelfSpinUpdate > SelfSpinUpdateDelay` against unrounded ticks), `TunnelContain.Update` (`MaxHealth / TicksForFullHeal` per tick, per tunnel) | Euler per tick; the comparison flips on tick boundaries instead of frame boundaries — **Phase 23:** done: crash as a frame step, first step one frame after the death; tunnel healing once per frame from the entry on (escape "original grid"), `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, commit `cee88c4`, `81931ab`, `96be02e` |
| `Modules/BuiltIn/VehicleModules.cs` | `BattleBusSlowDeathBehavior.Update` (throw: `Z += v`, then `v += g`) | Euler per tick; the throw height deviates by the Euler order — **Phase 23:** done: climb speed per frame, gravity and landing with the last tick; apex 45.200 at every rate; "in the air" 2.3667 / 2.3667 / 2.3750 s (within the tolerance, not exact), `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `d311db0` |

### 5.3 Counters with period N + 1, one-tick offsets and lower bounds that could not be put on the grid (18)

The order of the phases and "one tick later" are half a frame at 60 Hz; where that shifts results, it is listed here.

| File | Symbol | What is different at 60 Hz |
|---|---|---|
| `World/SimulationWorld.Turrets.cs` | `StepTurrets`, `StepTurret` | one rotation and deadline step per tick; "holding" applies up to and including `HoldUntil` (N + 1 steps: the extra step is one tick, not one frame) — **Phase 23:** done: the extra step of holding is one frame (`tick < HoldUntil + Ticks je Bild`): 16 frames at every rate, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, commit `6e0c691` |
| `World/SimulationWorld.FiringTracker.cs` | `StepFiringTrackers` (`tick <= CooldownFrame`), `PreAttackDone` (`_tick <= PreFinish + …`), `PreAttackSlotOf` | counter with period N + 1: the action happens in the tick after the deadline, that is one tick instead of one frame later — **Phase 23:** done: both comparisons `< Frist + Ticks je Bild`; `PreAttackSlotOf` deliberately unchanged (query between two steps), `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, commit `165e6d8` |
| `World/SimulationWorld.cs` | `RunUpdates` | a return value below `SleepNone` becomes one tick; every module with sleep 1 runs twice as often per second — **Phase 23:** deliberately left as it is: the scheduler stays tick-exact – one rule for all modules would have slowed down the integrators; solved per module, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md` |
| `World/SimulationWorld.cs` | `ProcessSpawns` | every link of a spawn chain costs one tick, that is half the time — **Phase 23:** done: an object requested in the creation phase appears one frame later (`PendingSpawn.NotBefore`, only above 30 Hz), `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, commit `f8ce7c4` |
| `World/SimulationWorld.cs` | `Register` (`NextWake = _tick + SleepNone`) | first update one tick after the appearance — **Phase 23:** open: converted per module where a failure or a first deviation has been measured (field, stealth, create modules, leaflet, `SpawnBehavior`, rally-point command, `AutoFindHealingUpdate`, `JetAIUpdate`, `ChinookAIUpdate`, `PilotFindVehicleUpdate`); the general rule "first frame for all modules" has been measured and not adopted (13 / 15 red tests at 60 / 120 Hz); **not converted:** `DeliverPayloadAIUpdate`, parachute, ferry, dynamic shroud reveal, firestorm, `FireOCLAfterWeaponCooldownUpdate`, enemy-proximity scan, checkpoint, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, commit `9f61b3d`, `4e83582`, `68ae9c3`, `21a6f7d`, `cc7d92e`, `35952fb`, `cc7fd75`, `2837f1e`, `713c662` |
| `World/SimulationWorld.cs` | `Step` (phase order) | effects between phases are offset by one tick (turret angle after movement, damage after destruction, vision after spawn) — **Phase 23:** deliberately left as it is: the phase order stays; instead of changing it, observers read the end-of-frame view, and vision is refreshed after the last tick of its frame, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, commit `a8eed4a` |
| `World/SimulationWorld.Construction.cs` | `_saleDurationTicks` | 180 frames minus **one tick**: 5.983 s at 60 Hz instead of 5.967 s — **Phase 23:** done: 180 frames minus one frame: 5.9667 s at every rate (179 / 358 / 716 ticks), `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, commit `404fab6` |
| `Modules/BuiltIn/AirWeaponModules.cs` | `LeafletDropBehavior.Update` | the first update comes one tick after the creation: a `Delay` of one frame (two ticks) has then not yet elapsed, the module sleeps forever instead of acting every tick. Obvious solution: put the first wake-up on `OriginalFrames(1)` — **Phase 23:** done: first update one frame after the creation: acts after 0.0333 s at every rate (before, never above 30 Hz), `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, commit `68ae9c3` |
| `Modules/BuiltIn/BattlePlanModule.cs` | `BattlePlanUpdate.Update` | one state transition per tick: the extra ticks between the phases halve in time — **Phase 23:** done: a deadline of zero is the next frame (`ZeroIsNextFrame`), `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, commit `529d514` |
| `Modules/BuiltIn/HackerAndAssaultModules.cs` | `HackInternetAIUpdate` – first counter after a command from outside | offset below one frame (Appendix A.2, "phase position of the counters") — **Phase 23:** done: a phase started by a command lasts its frames: first payment 8.1000 s at every rate, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, commit `938a32c` |
| `Modules/BuiltIn/Phase5BlockerModules.cs` | `StealthDetectorUpdate` with `DetectionRate = 0` | scans per tick instead of per frame (deliberately, Appendix B.3) — **Phase 23:** done: mark `DetectionRate` + one frame, sleep `AtLeastOneFrame`, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, commit `f922979` |
| `Modules/BuiltIn/TransportModules.cs` | `HelicopterSlowDeathBehavior.BeginSlowDeath` (`_bladeFlyOff = Range(MinBladeFlyOffDelay, MaxBladeFlyOffDelay)` over **unrounded** `Fixed` ticks, −1 per tick) | W1 d, not converted: the bounds do not lie on the grid (`parseDurationReal`), a draw in frames would have to divide the bounds back and would round differently at 60 Hz than the original; the drawn value is not twice as large at 60 Hz — **Phase 23:** done: bounds in unrounded frames of the 30 Hz catalog, one draw, counter per frame: the rotor flies off after 30 frames at every rate, `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, commit `82dbdba` |
| `Modules/BuiltIn/WorldEffectModules.cs` | `FirestormDynamicGeometryInfoUpdate.Update` (`ratio = _timeActive / TransitionTime`, end at `_timeActive > TransitionTime`) | the transition lasts `TransitionTime + 1` ticks: at 60 Hz half a frame shorter than at 30 Hz (W1 c in an interpolation; `_timeActive` is at the same time the counter of the interpolation) — **Phase 23:** done: the counter of the way back starts one frame minus one tick below zero: 2.1333 s at every rate, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, commit `47c760c` |
| `Scripting/ScriptEngine.Phase13.cs` | `UNIT_EMPTIED` (`frame - TickOrder.Next`) | order, not scaled; presupposes evaluation in **every** tick – if the VM runs only at frame boundaries, the neighbourhood has to be "previous frame". — **Phase 23:** compares with the previous frame (`frame - Time.TicksPerOriginalFrame`), because the VM evaluates per original frame; test `TickRateScriptClockUnitEmptiedLooksOneFrameBack` (before, never true at 60 Hz), commit 2fe8b32 |
| `Scripting/ScriptEngine.Phase13.cs` | `UpdateBridgeStates` (change flag for one update), `AttachPendingTraps`, range check in `UpdateCaptureOrders` | window or offset of one tick; check per tick. — **Phase 23:** runs with the VM once per original frame (`docs/SIMULATION-CORE.md` §41): window and offset are one frame again, the check falls on the same points in game time as at 30 Hz, commit 727c4d6 |
| `Mission/MissionSession.cs` | `Step` (order), `Engine.TimeFrozen` | scripts, then world, then events: scripts react one tick later (1/60 s). Under `FREEZE_TIME` only the call rate of `Step` counts: script timers keep running in ticks of the rate, the world stands still. — **Phase 23:** the order stays, but the VM works only on ticks that begin an original frame – scripts react on the frame grid of 30 Hz at every rate; under `FREEZE_TIME` the VM counts the calls itself (`frozenPhase`), script timers keep their duration while frozen, commit 727c4d6 |
| `Modules/BuiltIn/LastBlockerModules.cs`, `Modules/BuiltIn/MatchModules.cs` | `SupplyWarehouseCripplingBehavior.OnDamage` (`Math.Max(SuppressTicks, SleepNone)`), `BaseRegenerateUpdate.OnDamage` (`Math.Max(BaseRegenDelayTicks, SleepNone)`) | with a deadline of 0 the module wakes up after one tick instead of after one frame (named exceptions of the rule "lower bound one tick", §10) — **Phase 23:** done: lower bound `AtLeastOneFrame`; the named exceptions of the source rule are dropped, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, commit `c6ce657` |
| `Modules/BuiltIn/Phase5Modules.cs` | `StealthUpdate.MarkDetected` (`Math.Max(ticks, SleepNone)`) | lower bound of a value that the caller builds from the rate + `TickOrder.Next`; a caller with 0 reveals for one tick — **Phase 23:** done: lower bound one frame, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, commit `f922979` |

### 5.4 Evaluation and sampling per tick that is behaviour (25)

Cadences without a literal of their own: what runs per tick runs twice as often per game time at 60 Hz ("load only" where it changes no results).

| File | Symbol | What is different at 60 Hz |
|---|---|---|
| `World/SimulationWorld.Acquisition.cs` | `StepAcquisition`, `AutoFire` | one pass and one firing attempt per tick: shot times fall on the finer grid as soon as a condition (turret aligned, vision) becomes true between two frames — **Phase 23:** done: the pass stays per tick, **but a unit decides only in the first tick of its own frame** and reads the end-of-frame view – by the wording of the brief a forbidden escape for target acquisition, done provisionally, confirmation by the project owner is pending (§15.7, decision 1), `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, commit `f6dacdb`, `04e2fc8`, `2d637e1` |
| `World/SimulationWorld.Turrets.cs` | `_turretWaits` (also `AutoFire`, `TryFireActiveWeapon`) | counts held-back firing attempts per tick; the value (part of the hash) doubles — **Phase 23:** done: counts frames of waiting: 15 / 15 / 15 (before 15 / 30 / 59), `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, commit `dc888d3` |
| `World/SimulationWorld.Turrets.cs` | `KeepsAttackIntent` | vision is current only every three frames, the check is per tick; the deadline starts at the first tick out of range — **Phase 23:** done: evaluated in the decision ticks of the turret and read at the end of the frame; vision is refreshed after the last tick of its frame, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, commit `c87748a`, `a8eed4a` |
| `Modules/BuiltIn/MineModules.cs` | `MinefieldBehavior.Update` | damage per tick triggers the reconciliation of the virtual mines in finer steps — **Phase 23:** done: the threshold distance is compared once per frame of the driving object (escape "original grid", one grid for the message); the draining after the death of the builder deliberately stays per tick, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, commit `eca983f`, `0136733` |
| `Modules/BuiltIn/Phase12Modules.cs` | `FireWeaponWhenDamagedBehavior.Update` | the continuous weapon is checked per tick (load only; the shot interval lies on the grid) — **Phase 23:** deliberately left as it is: load only; the first update lies one frame after the creation, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, commit `68ae9c3` |
| `Modules/BuiltIn/Phase5Modules.cs` | `ProductionUpdate.Update` (query the door per tick) | a finished unit leaves the factory up to half a frame earlier — **Phase 23:** deliberately left as it is: load only (the door deadline lies on the grid); the first frame of a production order counts in full, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, commit `43d4a30` |
| `Modules/BuiltIn/ProjectileModules.cs` | `PointDefenseLaserUpdate.FireWhenReady` | range check and firing attempt per tick (the counters now lie on the grid) — **Phase 23:** done: the PROJECTILES package had deliberately left the site alone; since an AI builds Avengers, it failed: the laser now decides in the first tick of the frame of its carrier and reads at the end of the frame (escape close to the prohibition for target acquisition, confirmation by the project owner is pending, §15.7 decision 1), `docs/AI-OBSERVED-2026-10-08.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, commit `3c24352`, `887b51e` |
| `Modules/BuiltIn/StructureModules.cs` | `FloatUpdate.Update` | runs per tick, load only — **Phase 23:** deliberately left as it is: load only, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md` |
| `Modules/BuiltIn/WorldEffectModules.cs` | `FireOCLAfterWeaponCooldownUpdate.Update` (runs per tick, reads the shot of the previous tick) | order, unchanged and correct; load only — **Phase 23:** deliberately left as it is: load only; the first frame of the module is not converted, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` |
| `Modules/BuiltIn/WorldEffectModules.cs` | `WaveGuideUpdate.Update`/`Advance`/`DoDamage` | one path step and one damage sampling per tick: sampling of the front twice as dense — **Phase 23:** done: front and end of the path with the last tick of every frame (escape "original grid"); no case of the suite, one three-rate test, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `c333364` |
| `Modules/BuiltIn/WorldEffectModules.cs` | `CheckpointUpdate.Update` (two full searches per tick) | load only; the gate step is now scaled per tick — **Phase 23:** deliberately left as it is: load only; the first frame of the module is not converted, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` |
| `Scripting/ScriptEngine.cs` | `Update` (W6) | every active script without a delay is evaluated per tick: counters that a script increments per evaluation, and repeated actions, run twice as often. Obvious: only at frame boundaries (`Time.IsOriginalFrameBoundary`). — **Phase 23:** escape "original grid" (`docs/SIMULATION-CORE.md` §41): `Update` works only on ticks that begin an original frame; cause of the shift of `MD_USA02`, measurement §15.3, commit 727c4d6 |
| `Scripting/ScriptEngine.cs` | timer values (`Counter.Value`, `IsTimer`) | in ticks since stage 1, −1 per tick. Comparisons of a map against frame literals (`COUNTER`, `TIMER` conditions with a number) and arithmetic on a timer (`INCREMENT_COUNTER`, `DECREMENT_COUNTER`, `SET_COUNTER` raw) mean frames – at 60 Hz the unit does not fit. Resolves itself when the VM runs at frame boundaries and timers are frames again (then take back the scaling of stage 1 in `SET_TIMER`, `SecondsToFrames`, `SetSequenceTimer`); separately from that not solvable without a state change. — **Phase 23:** timers count frames of the original again (the scaling of stage 1 in `SET_TIMER`, `SecondsToFrames`, `SetSequenceTimer` and in the endgame timer is taken back); `CounterValue` of a timer is in frames at every rate, the one consumer (`MissionPresentationDirector.CounterForDisplay`) no longer converts, commit 727c4d6 |
| `Scripting/ScriptEngine.Actions.cs` | `UpdateFollowers` (the per-tick part), `UpdateTeamFollower` | arrival at the waypoint, re-issue of the march command and `FireAtOrdered` run per tick: finer sampling, more commands per second. The staggered remainder is converted. — **Phase 23:** runs with the VM once per original frame: arrival, re-issue of the march command and `FireAtOrdered` fall on the frame grid, as many commands per game time as at 30 Hz (command cadence of the scripts, not movement or target acquisition of the world), commit 727c4d6 |
| `Scripting/ScriptEngine.Sequential.cs` | `ProcessSequences`, `SequenceItem.FramesToWait`, `_busyThisTick` | wait counters are scaled (stage 1), but idleness is sampled per tick and the one-tick offset between command and world is 1/60 s. — **Phase 23:** idleness is sampled per original frame, `FramesToWait` counts frames (N frames hold N + 1 updates, the same game time at every rate); test `TickRateScriptClockSequentialWait`, commit 727c4d6 |
| `Scripting/ScriptEngine.ExtendedActions.cs` | `ProcessTeamBuilds` | at most one production command per tick for each open order: queued twice as fast. — **Phase 23:** at most one production command per open order and original frame, commit 727c4d6 |
| `Scripting/ScriptEngine.TeamQueues.cs` | `UpdateTeamQueues`, `UpdateBuildingTeam` | check per tick; start actions run per tick as long as a member is idle. — **Phase 23:** check and start actions per original frame; the deadlines (`teamInitialIdleFrames`, 60 s) still compare with the tick of the world and lie on the grid, commit 727c4d6 |
| `Scripting/ScriptEngine.TeamEvents.cs` | `ProcessTeamEvents`, `ScriptTeam.WasIdle`, `_pendingTeamScripts` | enemy sighting and loss threshold per tick; "idle for two ticks" is half as long; destroyed scripts one tick later. — **Phase 23:** per original frame: "idle for two updates" are two frames, destroyed scripts run in the next update (`Observe` still collects the death events in every tick), commit 727c4d6 |
| `Scripting/ScriptEngine.Conditions.cs` | `PLAYER_LOST_OBJECT_TYPE`, `NAMED_/TEAM_ENTERED/EXITED_AREA*` | edges against the previous evaluation: window and sampling depend on the evaluation cadence. — **Phase 23:** the previous evaluation lies one original frame back at every rate, the edges see the same points in game time as at 30 Hz (commit 727c4d6); test `TickRateScriptClockSeesWhatHappenedBetweenFrames` and the remembering of short-lived named objects, commit d710835 |
| `Scripting/ScriptEngine.Phase13.cs` | `SUPPLY_SOURCE_SAFE` | no cache (original: 2 s, `ScriptConditions.cpp`, `evaluateSkirmishSupplySourceSafe`); follows the evaluation cadence. — **Phase 23:** follows the evaluation cadence, which is now the original frame at every rate (commit 727c4d6); the missing 2 s cache of the original stays open (independent of the rate) |
| `Skirmish/SkirmishMatchSession.cs` | `Step` | `SkirmishAi.Update` is called per tick (returns immediately outside the frames); no behaviour, only the number of calls. — **Phase 23:** deliberately left as it is: only the number of calls, `docs/rate-invariance/RATE-INVARIANCE-TIMING.md` |
| `Skirmish/SkirmishAi.cs` | `Think` (deadlines on the think grid), `FindPlacement`/`ProduceArmy` (random draws per think step), `PlacementChecksPerThink`, `paths > 12` | with a scaled grid **and** scaled deadlines the effective duration and the number of draws per game time are the same as at 30 Hz; but the AI sees a world that has moved more finely between two think steps (other positions, other `BuildTicks` remainders) – sequences are not tick-identical to 30 Hz. — **Phase 23:** done: no intervention in the AI needed: since the world between two think steps is the same at every rate, the three matches of the group `ai` are equal in every field over 14,400 frames; **open:** on shipped maps 14 of 16 pairings agree in winner and frame of the victory, 120 Hz is not measured there, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, commit `502e75b`, `1b82e45` |
| `Testing/ObjectiveDriver.Commands.cs` | `Act`, `DriveAssaults` | one `FireWeaponCommand` per tick for each attack pair as soon as the weapon may fire (like `StandingOrders`, W5). — **Phase 23:** open: not investigated; measured is only that the objective driver reaches the same final script at the same game time at 30, 60 and 120 Hz (one exception: defeat on `MD_USA02` −1.00 s); the commands-only agent has not run at 60 Hz,  |
| `Testing/ObjectiveDriver.cs` | `IdleRoundsBeforeHarm`, `ExhaustiveRoundsBeforeUnion` | count rounds; with scaled `spacing` 20 s and 10 s again. Not touched. — **Phase 23:** deliberately left as it is: not touched (counts rounds, the same game time with scaled spacing),  |
| `Testing/StressScenario.cs` | whole schedule | counts steps (§8.3). — **Phase 23:** deliberately left as it is: counts steps: 216,000 steps are one hour of game time at 60 Hz, with a new final hash (§15.5.2), , commit `ea52f87` |

### 5.5 Random draws per tick (4)

One draw per tick consumes the logic stream twice as fast; the sequence of the values is a different one at every rate.

| File | Symbol | What is different at 60 Hz |
|---|---|---|
| `World/SimulationWorld.Acquisition.cs` | `MoodCheckOffset` | the offset now lies on the 30 Hz grid, but the draw hashes the tick number of the world: the sequence of the values is a different one at every rate — **Phase 23:** done: the offset hashes the frame number, counted on the grid of the unit, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, commit `146f92d`, `f6dacdb` |
| `Modules/BuiltIn/SlaveModules.cs` | `SlavedUpdate.DoRepairLogic` (runs per tick in the repair state: height random draw and movement command per tick as long as the drone is not close enough) | twice as many draws from the logic stream and commands — **Phase 23:** done: draw and command in the repair state only in the tick that begins a frame of the world (escape "original grid"): 100 / 100 / 100 draws in 100 frames, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, commit `01121cc`, `e1252df` |
| `Modules/BuiltIn/SlaveModules.cs` | `DoAttackLogic`/`DoScoutLogic`/`DoGuardLogic` (`RandomDirection` per check), `MobMemberSlavedUpdate.Update` (`Range(0, 10)` per check) | lie on the check grid (now scaled) – unchanged number of draws per game time; here only for completeness — **Phase 23:** deliberately left as it is: already lay on the check grid; since round 3 the mob member reads its nexus at the end of the frame, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, commit `c987fb9` |
| `Modules/BuiltIn/SpecialsModules.cs` | `GrantStealthBehavior.Update` (scan per tick), `WanderAIUpdate.Update` (two draws and one command per idle tick), `StickyBombUpdate.Update`, `ProjectileStreamUpdate.Update` | runs per tick; `WanderAIUpdate` draws from the logic stream twice as often — **Phase 23:** done: `GrantStealthBehavior` and `WanderAIUpdate` on the frame grid (escape "original grid"); `StickyBombUpdate` and `ProjectileStreamUpdate` deliberately left as they are (no random draw, no command), `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, commit `f1c5ceb`, `022bec8` |

### 5.6 Remainders of converted sites (10)

Sites that Phase 20 put on the grid and that nevertheless do not scale bit-identically (rounding in `Fixed`, unrounded durations).

| File | Symbol | What is different at 60 Hz |
|---|---|---|
| `World/SimulationWorld.Projectiles.cs` | `FlightTicks` | counting is now in frames; but the travel of one frame is `Tempo je Tick × Ticks je Bild`, and the speed per tick is rounded on its own at 60/120 Hz – at a frame boundary the flight time can deviate from 30 Hz by one frame — **Phase 23:** done: counted against the speed per frame of the 30 Hz catalog: 61 / 61 / 61 frames (before 61 / 60 / 61), `docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md`, commit `b5b9fd4` |
| `Modules/BuiltIn/ContainModules.cs` | `GarrisonContain.HealOne` | Fixed rounding of the smaller slice `MaxHealth / Ticks` (the sum deviates in the last bits) — **Phase 23:** done: the slice of the original once per frame from the entry on (escape "original grid"), `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `b05da71` |
| `Modules/BuiltIn/BridgeModules.cs`, `JetAIUpdate.cs` | `PerOriginalFrameToPerTick(…)` at the use | at 60/120 Hz one `Fixed` division per update (at 30 Hz no computation). For Phase 22/23: keep it once per world if it shows up in the profile — **Phase 23:** done: no division by the rate factor in the update any more; the speeds are constants per frame, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, commit `501350c`, `60fcb57` |
| `Modules/BuiltIn/RepairModules.cs` | `RepairDockUpdate.Action` (`_healthToAddPerTick = (Max − Health) / FramesForFullHeal`) | value per tick from unrounded ticks; presupposes one call per tick – evens out, except for the `Fixed` rounding — **Phase 23:** done: slice once per frame from the first action on: 150 frames with healing at every rate (before 150 / 151 / 151), `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, commit `b05da71` |
| `Modules/BuiltIn/RetailMatchModules.cs` | `HealContain.Update` (`MaxHealth / TimeForFullHealTicks` per tick) | W1 a: healing per tick from the tick count – same duration, different `Fixed` rounding per step — **Phase 23:** done: slice once per frame; the same form later for `TransportContain`, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, `docs/AI-OBSERVED-2026-10-08.md`, commit `b05da71`, `df639ff` |
| `Modules/BuiltIn/TransportModules.cs` | `ChinookAIUpdate.Update` (`climb = locomotor.Speed`) | the climb rate per tick follows the speed per tick – same rate per game time — **Phase 23:** done: height step per frame in fractions; since round 4 first update one frame after the appearance and ground at the end of the movement of the frame, `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, commit `0bf4e6c`, `2837f1e`, `b62ccc4`, `10e0031` |
| `Modules/BuiltIn/WorldEffectModules.cs` | `DynamicShroudClearingRangeUpdate.Update` (`_current += native / GrowTime` per tick) | evens out over the tick count, different `Fixed` rounding — **Phase 23:** done: growth per frame distributed exactly over the ticks, hand-over with the last tick; the intermediate value `current` runs ahead within the frame and is excluded from the field comparison, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, commit `c5b43fa` |
| `Testing/ObjectiveDriver.Leaves.cs` | `GuardMargin` (150 world units) | margin against the travel of a unit per planning round; unchanged and correct with a scaled round, but depends on the movement (class E). — **Phase 23:** deliberately left as it is: not touched (test and tool code),  |
| `Templates/ObjectTemplate.cs`, `Templates/TurretInfo.cs`, `Templates/WeaponCatalog.cs` | `LocomotorInfo.Braking`/`Acceleration`/`Lift`/`TurnRatePerTick`, `TurretInfo.PitchRate`/`TurnRate`, `WeaponInfo.WeaponSpeed` | quantisation of the values per tick and per tick² in Q47.16 (numbers in §11.2); input for Phase 22 — **Phase 23:** open: the quantisation itself has not been a finding since Phase 22; where the last bits matter, the constants per frame of the 30 Hz catalog stand next to the catalog type (`LocomotorFrames`, `TurretInfo.RatesPerOriginalFrame`) – for ground movement, winged flight, jets, missiles and turrets; **open:** Spectre, Chinook height step, parachute, flares, particle beam, shot-down jet, gravity of the physics modules and the group speed still compute `Wert je Tick × r`, `docs/rate-invariance/RATE-INVARIANCE-GROUND.md`, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, commit `bf028df`, `32eb25c`, `346cadc`, `ec9f90a` |
| `Time/SimulationTime.cs` | `PerSecondToPerTick(Fixed)`, `SharePerTick`, `PerOriginalFrameToPerTick(Fixed)` | per-tick amounts that arise from state at run time lose one bit with every doubling; the counter `RunTimeRoundedToZero` counts the cases that vanish completely (§11.3) — **Phase 23:** open: solved for healing by crediting the slice of the original once per frame (repair dock, barracks, tunnel, speaker tower, repair by a builder, GLA hole, troop transport); **open:** `SlavedUpdate` (repair drone) and the regeneration of the occupants in `ContainModules.cs` still heal per tick, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`, `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, `docs/AI-OBSERVED-2026-10-08.md`, commit `b05da71`, `81931ab`, `f05af4f`, `64f438f`, `2a433ba`, `df639ff` |

Checked and **not** a factor per frame: `StructureCollapseData` `CollapseDamping` (share of gravity, `gravity · (1 − damping)`; the gravity per tick² follows the rate).

## 6. List for Phase 21 (consumers outside the simulation)

> **Status after Phase 21:** this list has been worked through; the state per number is in §14.3 (no. 3 and no. 12 done in the addendum §14.9; unchanged as planned: no. 15). The table describes the state at the end of Phase 20 and is not updated any further.

After Phase 20 everything outside reads the rate from the world or from the catalog. What still assumes the 30 there today or misreads a unit:

| No. | Where | What | What is to be done |
|---|---|---|---|
| 1 | `CnCNew.Game/Live/LiveGame.cs`, `tools/CnCNew.SimTool/PerfCommand.cs`, `tests/CnCNew.Presentation.Tests/ObjectTests.cs`, `tests/CnCNew.Simulation.Tests/Tests.Vision.cs` | `SimulationWorld.VisionIntervalTicks` (`public const int`, alias of `VisionIntervalFrames`): correct at 30 Hz, half the period at 60 Hz | use `world.VisionInterval`, then remove the constant |
| 2 | `CnCNew.Game/Data/GameData.cs` | `GameData.SessionRate` – the one named rate of the game layer (30 Hz); it builds the catalog and folds the rate into the fingerprint | make it a setting of the session; everything else reads `data.Templates.Rate` |
| 3 | `CnCNew.Game/Save/SaveGame.cs` (version 1) | writes and checks **no** rate and shows the raw tick in the load list (W8). The embedded mission state carries the rate and is checked against the catalog on loading; the envelope itself is not | rate into the envelope, game time instead of tick in the list |
| 4 | `CnCNew.Game`, `CnCNew.Presentation`, tools: 14 + 1 + 3 uses of the former constant are now called `SimulationRate.OriginalFramesPerSecond` | **not converted, only compilable**: which of them are simulation ticks and which are original frames on wall time has not been decided (W7) | decide per site: `world.Time` or original frame on wall time |
| 5 | `CrateModules.cs` (`SabotageMilitaryFactoryCrateCollide.Sabotage`, `SabotagePowerPlantCrateCollide.Sabotage`), `LastBlockerModules.cs` (`SabotageInternetCenterCrateCollide.Sabotage`) | pass the duration of the sabotage to the outside as a **tick count in the event value** | the reader computes with `world.Time` |
| 6 | `CnCNew.Presentation` `UnitConditionTracker.TensileFreefallLife` | reads `TensileFormationUpdate.Life`, which counts ticks | compare with `Time.OriginalFrames(100)` |
| 7 | `MissionText`, countdown displays (`CnCNew.Game`, `CnCNew.Ui`) | `ScriptEngine.CounterValue(name)` of a timer is in **ticks** (state of the VM; stage 1 scales when setting); a display that reads the value as frames shows twice the value at 60 Hz | `world.Time.ToOriginalFrames` or `ToWholeSeconds` |
| 8 | `CnCNew.Client.Stride/ClientRuntime.cs` | writes "presentation latency N ticks" – `MissionOptions.PresentationLatencyTicks` are frames of the original | text and unit |
| 9 | consumers of `ScriptOutput` (`MissionPresentationDirector`) | `Arguments` are the parameters of the map, never scaled: frame counts (`CAMERA_FADE_*`, `CAMERA_BW_MODE_*`) are frames of the original; `ScriptOutput.Tick` is a tick of the world (§8.2) | do not read them as ticks; time from `Tick` via `world.Time` |
| 10 | `CnCNew.Game/Live/LiveMission.cs` | `ClientPresentationLatencyTicks = 60 * OriginalFramesPerSecond` is 60 s at every rate without a change (the option is in frames) | only the name is historical |
| 11 | presentation: `SpecialAbilityUpdate.StageTicksLeft`, `SpecialPowerModuleBase.AvailableOnFrame`, `AnimationSteeringUpdate.MinTransitionTicks`, `ProductionData.Door…Ticks`, `TechBuildingData.PulseRateTicks` | values of the simulation in ticks of the rate (absolute ticks or catalog durations) that the HUD and model states read | display and animation duration via `world.Time` |
| 12 | callers of `MissionObjectiveDriver.Run`/`MissionProbe.Run` (`CnCNew.CampaignTool`, tests) | `settleTicks:`/`waitTicks:`/`spacing:` are frames of the original; `maxTicks`/`--ticks` ticks of the chosen rate; `SkirmishAiStats.AttackTicks`, `MissionProbeResult.TicksToOutcome` are ticks | outputs in seconds via `world.Time` |
| 13 | `tools/CnCNew.PresentationTool` (`ScenarioAudit`: `live.Advance(1.0 / 30 + 1e-6)`; `SyncPerfCommand`: `1.0 / OriginalFramesPerSecond`), `tools/CnCNew.UiRenderTool` (`1.0 / 30` in three files, `TransitionScenes.Frame`, timer value `30 * (11 * 60 + 55)` in `MissionTextScenes`) | presentation steps with the literal 30; "one step = one tick" silently becomes wrong at 60 Hz (W9) | derive the step from the rate **before** the rate is switched |
| 14 | tests outside the simulation | 103 occurrences of `1.0 / 30` in 18 files (number of the inventory, W9) | like no. 13 |
| 15 | `tools/CnCNew.SimTool/PerfCommand.cs` | the extrapolations `catchup30`/`catchup60`/`core30_percent` name their rate in the name and compute with literals – labelled correctly, but independent of `--tick-rate` | tie them to the rate of the run at the switch of the default (Phase 23) |
| 16 | `CnCNew.Presentation/Input/StandingOrders`, `LiveGame.BeforeTick` (W5) | generate commands at the tick cadence (one fire command per tick); the cadence changes results and the replay content | cadence in frames of the original (like `ObjectiveDriver.Commands.Act`, §5.4) |

## 7. Formats

- `WorldSnapshot`, `MissionSnapshot` and the replay header carry the rate as before (`tickRate`); at 30 Hz the bytes are unchanged, therefore **no version bump** in Phase 20 (`WorldSnapshot` 9, `MissionSnapshot` 2, replay **11**, `CommandTypes.Version` **14** – this is how the last two stand in the code; `CLAUDE.md` still says 10 and 13).
- Restoring creates the world **with the stored rate**. Rejected are (a) an unsupported rate (`SnapshotFormatException` or `ReplayRejectedException`: "Unsupported tick rate 50 Hz"), (b) a catalog that was built for a different rate ("The snapshot was written at 60 Hz; the catalog was built for 30 Hz."). The state contains absolute ticks and per-tick values; it is readable only with its rate.
- `ReplayRunner.Run(replay, world, …)` checks that the world runs at the rate of the replay (W8). `ReplayRunner.RateOf(header)` returns the rate of a header.
- Commands (`SimCommand.Tick`, `SetTimerCommand.DelayTicks`/`RepeatTicks`) carry ticks of the rate of the world; whoever generates the command computes in this rate.
- `SaveGame` remained untouched in Phase 20 (§6 no. 3); since the addendum of Phase 21 the envelope carries its rate (**version 2**, §14.9.3).

## 8. Units

### 8.1 Public values of the sessions, scenarios and the script VM

| Value | Unit | Where converted |
|---|---|---|
| `SkirmishMatchOptions.MaxTicks`, `.CheckpointInterval` | ticks (run budget, goes into the replay header like this) | – |
| `SkirmishMatchOptions.GraceTicks`, `.SciencePointInterval` | frames of the original | `SkirmishMatchSession` constructor (`_graceTicks`, `_sciencePointInterval`) |
| `ReferenceMatchOptions.MaxTicks`, `.MinTicks`, `.CheckpointInterval` | ticks (run budget; `MinTicks` is compared with `MaxTicks` and pads the run) | – |
| `ReferenceMatchOptions.GraceTicks`, `.IncomeInterval` | frames of the original | `ReferenceMatchScenario.Record` |
| `StressOptions.Ticks`, `.DrainTicks`, `.CheckpointInterval` | steps (ticks); `DrainTicks` is a share of the run budget | – |
| `MissionOptions.MaxTicks` | ticks (run budget; in the mission state under `maxTicks`) | – |
| `MissionOptions.PresentationLatencyTicks`, `ScriptEngineOptions.PresentationLatencyTicks` | frames of the original; in the mission state `presentationLatencyTicks` holds the value of the option as it was passed in | `ScriptEngine.Forward` (`_tick + Time.OriginalFrames(…)`) |
| `MissionOptions.CombatIntervalTicks` | only a switch (W8), not a time | – |
| `ScriptEngine.EndGameDelayFrames` (formerly `EndGameDelayTicks`) | frames of the original; `ScriptEngine.EndGameDelay` is the same value in ticks of the world | property `EndGameDelay` |
| `MissionObjectiveDriver.Run(maxTicks, …)` | ticks (run budget) | – |
| `MissionObjectiveDriver.Run(spacing, settleTicks)`, `MissionProbe.Run(settleTicks, waitTicks)` | frames of the original | in `Run` |
| `MissionProbeResult.TicksToOutcome`, `MissionRunReport.Ticks`/`.OutcomeTick`, `ObjectiveDriveResult.OutcomeTick` | ticks | – |
| `SkirmishAiStats.AttackTicks` | ticks (counts the scaled think interval per think step) | – |
| `ScriptEngine.CounterValue(name)` of a timer | **since Phase 23:** frames of the original at every rate (state of the VM, §15.3); from stage 1 until then ticks | – |
| `ScriptEngine.HulkLifetimeOverrideTicks` | ticks (state of the VM; since stage 1) | `SCRIPTING_OVERRIDE_HULK_LIFETIME` |
| `ScriptOutput.Tick` | tick | – |
| `ScriptOutput.Arguments` | parameters of the map, unchanged (§8.2) | – |

Whoever passes 300 as `GraceTicks` today gets the same behaviour at 30 Hz and the same game time at 60 Hz. Tools that offset such a value against a tick budget first convert the budget into frames (`ToolRate.GraceWithinBudget`, §9).

### 8.2 What leaves the simulation as script output

`ScriptEngine.Forward` passes 129 action types to the host as `ScriptOutput(Tick, Action, Arguments)`:

- `Arguments` are the parameters of the map as text, **unchanged and never scaled with the rate**. A frame count among them (`CAMERA_FADE_*`, `CAMERA_BW_MODE_*`: frames for rise, hold, fall; `ScriptEngine.cpp`, `ScriptEngine::setFade`, `m_fadeFramesIncrease/Hold/Decrease`) is a number of **frames of the original (1/30 s)** at every rate; second values are seconds.
- `ScriptOutput.Tick` is the simulation tick of the rate of the world. Whoever derives time from it computes with `world.Time`.
- `PendingPresentation` expires after `PresentationLatencyTicks` **frames of the original**.
- `VICTORY`/`DEFEAT` outputs: the run-on until `IsEnded` is 120 frames (4 s) at every rate, with `QUICKVICTORY` one update (since Phase 23 one update of the VM, that is one frame of the original).
- `CounterValue` of a timer has been in **frames of the original** since Phase 23 (§15.3; before that in ticks: §6 no. 7, §5.4 "timer values").

### 8.3 Stress test: the schedule counts steps

`StressScenario` is a load test per simulation step: one kill and one replacement per tick, sparks every second, smoke every third tick, a timer per new object. A schedule in game time would halve the load per tick at 60 Hz and measure something else; the run budget (`Ticks`) is a step count anyway. Therefore the ramp (100), all modulo cadences, the timer delays of the commands (`SetTimerCommand(…, 30, 90, …)`, `(…, 120, 300, …)`, `(…, 45, 0, …)` – ticks by the contract of the command) and `DrainTicks` (share of the budget; `SimTool` cuts it to `Ticks / 4`) stay the same numbers at every rate. The cadences are called `…Steps`. Consequence at 60 Hz: compared with the lifetimes of the fixture (milliseconds, which scale) the turnover in game time is twice as dense; the run at 60 Hz is a different run, not a comparison run to 30 Hz. The only game-time value is the reserve before the end of the drain (`DrainReserveFrames = 60`). At 30 Hz the run is bit-identical (manifest run `stress`). The test `TickRateStressScheduleCountsSteps` records the decision.

## 9. Outside the simulation, tools

Only what was necessary for compiling and for the same behaviour: `GameData.SessionRate` builds the catalog and folds the rate into the fingerprint; `LiveScenario` takes `data.Templates.Rate`; tests build their catalogs and worlds with `SimulationRate.Original`. Everything else is in §6.

`SimTool` and `CampaignTool` take `--tick-rate N` (30, 60, 120; default 30) with every command that creates a world; tick budgets (`--ticks`, `--max-ticks`) remain ticks of the chosen rate; game minutes in the output come from the rate (`ToolRate.Minutes`). The manifest commands always run at the recorded rate (30 Hz) and reject a different one.

Corrected in the completion (at 30 Hz the same value or the same output in each case):

| Tool | Site | Was | Is |
|---|---|---|---|
| `SimTool` | `skirmish-sweep` (`Program.cs`), sweep of the reference manifest (`ReferenceManifest.cs`) | `GraceTicks = Math.Min(Standard.GraceTicks, maxTicks / 4)` – a minimum of frames and ticks | `ToolRate.GraceWithinBudget(graceFrames, maxTicks, rate)`: the budget share is first converted into frames (2 sites) |
| `SimTool` | `perf`, allocations | "MiB per game minute at 30 Hz" with `* 60 * 30` | `* ToolRate.Current.Time.TicksPerMinute`, labelled with the rate of the run |
| `SimTool` | `unit-audit` | sample `world.Tick % 15`, message from 60 ticks on | every 15 frames, message from 60 frames on (2 sites) |
| `CampaignTool` | `playthrough` | 150 ticks of lead time before saving | 150 frames (5 s) |
| `CampaignTool` | `drive --watch` | hint line at `world.Tick % 150 < 2` | `ToOriginalFrames(world.Tick) % 150 < 2` |

Checked and correct: `StressOptions.DrainTicks = Math.Min(DrainTicks, Ticks / 4)` (both steps); the default budgets `--ticks 900/1800/9000`, `--max-ticks 3000/9000` and `Math.Min(ticks, 900)` in `CampaignTool audit` (run budgets in ticks of the chosen rate – half the game time at 60 Hz, as documented); `ToolRate.Minutes` in `SimTool` and `CampaignTool`. Not changed because it is presentation (Phase 21): §6 no. 13 to 15.

New: `SimTool catalog-cost <content>` (stage 1), `SimTool rate-audit <content> --tick-rate N` and the option `--time-probe` for `reference-match`, `ai-match` and `mission-run` (§11).

## 10. `SourceRules` and the search patterns of the inventory

`tests/CnCNew.Simulation.Tests/Tests.SourceRules.cs` (test "Determinism rules hold in the simulation sources") checks `src/CnCNew.Simulation` line by line (strings emptied, comments removed). The rules of Phase 20:

| Rule | Matches | Allowed under the rule |
|---|---|---|
| hard-coded tick rate (stage 1) | `LogicFramesPerSecond`; conversions of the static class `SimulationTiming` | – |
| number as tick rate | a field with `PerSecond`, `FramesPerSec`, `TicksPerSec`, `FrameRate`, `TickRate`, `Fps` or `Hz` in its name that is given a number; 30 or 900 as factor, divisor or modulus (also `Fixed.FromInt(30)`); `1/30`, `30/1000`, `1000/30`, `/ 33`; `FromTicksPerSecond(` with a literal | – |
| tick-named value as a number | constant, field, local variable, property or parameter with `Tick`, `Frame`, `Interval`, `Delay`, `Period` or `Cooldown` in its name (also in lower case) that is assigned a number (`=`, default value, `? … : zahl`) | 0 and −1 (unset, never); an expression with `TickOrder.Next`; a `const` whose name ends in `Frames`; `MaxTicks`/`maxTicks` (run budget) |
| bare number in tick arithmetic | `…Tick… + / − / % zahl`, `now ± zahl`, a tick difference compared with a number ≠ 0, `_life`/`_age`/`frame… % zahl` | a multiple of `TickOrder.Next` |
| bare number as sleep | `return zahl;` or `return world.Tick + zahl;` with a number ≠ 0 in `Modules/` and `Scripting/` | – |
| duration with a lower bound of one tick | `Math.Max(…SleepNone…)`, `Math.Max(…Ticks, 1)`, `Math.Max(1, …Ticks…)` | – (a duration uses `AtLeastOneFrame`) |
| logic random over a number range | `LogicRandom.Range(0|1, zahl)` | – (a draw over durations uses `DrawFrames` or draws in frames) |
| constant in frames reaches the time service | every `const int/long …Frames`: in its type (file or the files of a `partial` class; for `public` everywhere) a line must use it in `OriginalFrames(…)`, as the fallback value of `ModuleConfigReader.Ticks(…)` or in another constant in frames | – |

Every exception is listed by name in the test, with file, line fragment and reason; an exception that no longer matches anything makes the test fail. **34 exceptions** (29 lines, 5 constants):

| Rule | Exceptions |
|---|---|
| number as tick rate (4) | `SimulationRate.OriginalFramesPerSecond = 30` (the one named 30); `SimulationTiming.MillisecondsPerSecond = 1000` (not a rate); `DockMachine.ClearanceTimeoutFrames = 30L * OriginalFramesPerSecond` (30 seconds times frames per second); `FixedTrig` (`/ Fixed.FromInt(30)`, term of the sine series) |
| tick-named value as a number (8) | `OriginalFramesPerSecond = 30`; `MissionOptions.CombatIntervalTicks = 3` (switch, key of the mission state); `_blockerReplanTick = -1000` ("never planned", default in the snapshot); `TurretInfo.NeverScanTicks = 9999999` (sentinel of the original, `TurretAIData::TurretAIData`); `MissionObjectiveDriver.Run(… spacing = 15, settleTicks = 60 …)` and `MissionProbe.Run(settleTicks = 150, waitTicks = 600)` (frames under a historical name, scaled in `Run`); `MaxUpdatesPerTick`, `MaxEventsPerTick` (storm limits per step) |
| bare number in tick arithmetic (1) | `MoodCheckOffset`: `(ulong)(tick + 1)` (offset of the hash input, neither time nor order) |
| lower bound of one tick (12) | differences of absolute ticks or "a wake-up lies in the future": `AssistedTargetingUpdate.Update`, `SpyVisionUpdate.ActivateSpyVision`, `AutoDepositUpdate.OnSpawn`, `MinefieldBehavior.Update`, `FlammableUpdate.Update`, `WeaponBonusUpdate.Update`; deliberately one tick: `StealthDetectorUpdate.Update` (a lower bound of one frame opened a gap of one tick at `DetectionRate 0`), `StealthUpdate.MarkDetected`; **on the list for Phase 23**: `SupplyWarehouseCripplingBehavior.OnDamage`, `BaseRegenerateUpdate.OnDamage` (with a deadline of 0, one tick instead of one frame); not a duration: `SpecialAbilityUpdate` (`Math.Max(1, PreparationTicks)` guards a division), `StressScenario` (step count as modulus) |
| logic random over a number range (4) | `Bodies.cs` (`Range(0, 99) < 25`, percent); `SlaveModules.cs` `RandomDirection` (`Range(0, 6)`, direction in radians) and `Range(0, 10)` (draw of the original for the locomotor choice, kept for the stream); `WorldEffectModules.cs` `Range(1, 100)` (draw of the original for the splash) |
| constant in frames reaches the time service (5) | `RescanWaitMaxFrames` (`CleanupAndSpyModules.cs`) and `RescanMaxFrames` (`ProjectileModules.cs`): upper bound of a draw in frames, the drawn value is scaled; `SkirmishAi` `SupplyLookRetryFrames`, `TunnelEvacuationRetryFrames`, `WarehouseScoutRetryFrames`: passed to `Backoff(key, frames)`, which scales |

**Found by the rules** (measured: the new test over the state of the merged stage 2, `0efc7d8`): **five sites**, all converted or removed – `FrameGrid.OneFrame = 1` (helper constant, dropped), `HordeUpdate.OnSpawn` (`Math.Max(1, …)` in the draw by hand, now `DrawFrames`), `ProductionUpdate.Enqueue` (`Math.Max(totalTicks, 1)`), `ProductionUpdate.Update` (`RemainingTicks - 1`), `WeaponInfo.ScaledByRateOfFire` without the time service (`Math.Max(SleepNone, …)`). Two of them change behaviour at 60 Hz (build time 0, shot interval with bonus in `FireWeaponUpdate`/`BeginReload`), none at 30 Hz.

**Limits of the rules:** they are lexical tripwires. A number as a positional argument (`new Leaf(…, 3, 30, …)`, `new(…, 216_000, 300, 0, 1_000, …)` in the `Standard` options, `SetTimerCommand(…, 30, 90, …)`) is invisible to them – the units of those are in §8. Counters `++`/`--` per update and queries per tick without a literal are behaviour (§5.4), not literals.

**The search patterns from `docs/TICKRATE-AUDIT.md` §8 over `src/CnCNew.Simulation`**, counted at the final state (hit lines / files, without comment lines). "Rule" means: every hit is allowed by a rule above or is a named exception; the remaining patterns are too noisy as a rule (the inventory says so itself for P04/N16) – their hits were read and evaluated file by file in stage 2 (Appendix B) and are counted here by kind (mechanically, with the same distinctions as the rules).

| Pattern | Hits | State |
|---|---|---|
| P01 `LogicFramesPerSecond` | 0 | Rule |
| P02 rate field with a number | 2 / 2 | Rule: both are named exceptions (`OriginalFramesPerSecond`, `MillisecondsPerSecond`) |
| P03 `1/30`, `/ 30m`, `30/1000` … | 0 | Rule |
| P06 `long/int …Tick/Frames/Interval… = zahl` | 75 / 30 | Rule: 58 constants in frames, 6 `…Steps` of the stress test, 4 sentinels 0, 1 `TickOrder.Next`, 6 named exceptions (`OriginalFramesPerSecond`, `NeverScanTicks`, the parameters of `MissionObjectiveDriver.Run`/`MissionProbe.Run`, two storm limits) |
| P07 `tick % zahl` | 0 | Rule |
| P08 `Tick ± … Vergleich mit Zahl`, `Tick ± zahl` | 5 / 5 | Rule: 2 `TickOrder.Next`, 2 via `OriginalFrames(…)`, 1 catalog value (`FireWeaponUpdate`, comparison `_clip == 0`) |
| P09 `return zahl;` | 28 / 15 | Rule for `Modules/` and `Scripting/` (no hit with a number ≠ 0); 23 times `return 0;` (money amounts, flags, counters – not a sleep), 5 outside the modules: rank numbers in `ObjectTemplate.cs` (4), a result value in `NavigationGrid.cs` (1) |
| P13 `LogicRandom.Range(0|1, zahl)` | 4 / 3 | Rule: 4 named exceptions |
| N05 `% zahl` or `% …Interval/Frames…` | 14 / 6 | Rule (literal): 2 `formation % 2` (geometry); 12 cadences via a field resolved in the constructor (`_thinkInterval`, `VisionInterval`, `_moveCadence` …) or `% FireIntervalFrames` in frames |
| N08 `…Frames/Ticks/Interval/Delay… = zahl` | 79 / 35 | Rule: 58 constants in frames, 12 sentinels 0/−1, 1 `TickOrder.Next`, 4 named exceptions (`OriginalFramesPerSecond`, `NeverScanTicks`, the parameters of `MissionObjectiveDriver.Run`/`MissionProbe.Run`), 4 millisecond values in the INI text of the `SyntheticFixture` (data, converted during the catalog build) |
| P04 `30`, `900`, `3600`, `1800`, `450` | 49 / 20 | 12 constants in frames, 13 cooldowns of the driver leaves (frames, scaled in `Round`), 4 stress test, 11 distances/radii/quantities in world units, 9 ID numbers, hash shifts (`z >> 30`), series term, `SimulationRate` itself |
| N16 `15`, `45`, `60`, `90`, `120`, `150`, `300`, `600` | 81 / 29 | 24 constants in frames, 20 cooldowns of the driver leaves, 5 stress test, 26 distances/radii/quantities/percentages, 4 ID numbers, 1 fallback value in frames, 1 via the time service |
| P05 `const int/long … = …` | 198 / 60 | 64 constants in frames, 8 `…Steps`, 1 `TickOrder.Next`, 2 named sleep values/sentinels, 123 without time (sizes, counts, bit widths, ID numbers, versions) |
| P10 `ModuleConfigReader.Ticks(…, ersatz ≠ 0)` (the pattern also matches multi-line calls with fallback value 0) | 107 / 33 | fallback values are frames of the original (contract of the reader, stage 1); 15 with a fallback value ≠ 0 |
| P11 `ModuleConfigReader.Int/Real(…, "…Time/Delay/Rate/Speed…")` | 25 / 15 | 10 scaled via the time service (raw values per frame, stage 1), 15 raw values without time or values per second that are converted at run time, and `CatchUpCrisisBailTime` (number of checks) |
| P12 `*=`, `Speed = … * …Damp/Friction…` | 13 / 9 | 5 multiplicative factors per frame (§5.1: train ×3, `StructuralDecay`, `SpiralOrbitForwardSpeedDamping`), 1 thrust halving of the neutron missile (§5.2), 7 scalars without time (damage, vision, range, cost) |
| N07 `Tick/now ± name` | 237 / 52 | 97 with `SleepNone`/`SleepForever`, 44 via the time service, 15 `TickOrder.Next`, 80 tick ± catalog duration, state field or resolved field of the world, 1 stress test – no bare literal (rule "tick arithmetic") |
| N14 `LogicRandom.…(` | 63 / 25 | draws over durations go through `DrawFrames` or are drawn in frames and scaled; what remains are shares, angles, distances, selections – and `_bladeFlyOff` (§5.3) |

No hit of the rule patterns is unevaluated. For the noisy patterns the count is a sorting by notation, not a second reading; the evaluation behind it is that of stage 2.

## 11. Trial run at 60 and 120 Hz

Only "runs through, is deterministic, and what is lost in the process" – the behaviour at these rates is explicitly **not** evaluated (Phase 23), the results are not reference values. State: final state of Phase 20 (after stage 2 and the merge). Release, retail data, 6 October 2026, up to six runs side by side (run times not measured on a quiet machine).

### 11.1 Runs

Each command records, lets two fresh runs run against the recording (checkpoints, tick hashes) and continues from a snapshot or savegame at the halfway point. Tick budgets are scaled to the same game time as at 30 Hz (`reference-match --max-ticks` 432,000 / 864,000 – at 120 Hz additionally 216,000 –, `ai-match --max-ticks` 90,000 / 180,000 for 45,000 at 30 Hz, `mission-run --ticks` 6,000 / 12,000 for 3,000 at 30 Hz). The 30 Hz rows are the same runs with the probe; their final hashes are in the reference manifest.

| Run | Rate | Result (game time) | Final hash | Run 2 identical | Continuation identical |
|---|---|---|---|---|---|
| `reference-match` | 30 | winner FactionGLA at tick 14,490 (8.1 min) | `23B4B25DF62765D8` | yes | yes |
| `reference-match` | 60 | winner FactionGLA at tick 29,280 (8.1 min) | `EEBBD93BB03EBE8A` | yes | yes |
| `reference-match` (`--max-ticks 216000`) | 120 | no winner in 216,000 ticks (30 min), peak 252 living objects | `DE475CBB34080F25` | yes | yes |
| `reference-match` (`--max-ticks 864000`) | 120 | no winner in 864,000 ticks (120 min), peak 2,039 living objects | `F597B73A4DEB35C7` | not verified | not verified |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 30 | winner FactionChina at tick 23,370 (13.0 min) | `0BD4FE5EEB2C37E7` | yes | yes |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 60 | winner FactionChina at tick 23,430 (6.5 min) | `B05BD85E6644CF26` | yes | yes |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 120 | winner FactionChina at tick 45,900 (6.4 min) | `DF4127D263A690FB` | yes | yes |
| `ai-match` Alpine Assault, FactionAmerica/FactionChina | 30 | winner FactionAmerica at tick 16,965 (9.4 min) | `BA10AC1EB0EB8AC9` | yes | yes |
| `ai-match` Alpine Assault, FactionAmerica/FactionChina | 60 | winner FactionAmerica at tick 44,340 (12.3 min) | `EF1A3A9C2ED1CD96` | yes | yes |
| `ai-match` Alpine Assault, FactionAmerica/FactionChina | 120 | winner FactionAmerica at tick 65,820 (9.1 min) | `49FBBF394F84B3F5` | yes | yes |
| `mission-run` `MD_USA05` | 30 | 3,000 ticks, outcome open, 0 missing actions/conditions | `1CB28E6AA923BDEF` | yes | yes |
| `mission-run` `MD_USA05` | 60 | 6,000 ticks, outcome open, 0 missing | `24230BF86F255B78` | yes | yes |
| `mission-run` `MD_USA05` | 120 | 12,000 ticks, outcome open, 0 missing | `40C0DB506E9C8E57` | yes | yes |
| `mission-run` `MD_GLA05` | 30 | 3,000 ticks, outcome open, 0 missing | `826CCD82C8536DC2` | yes | yes |
| `mission-run` `MD_GLA05` | 60 | 6,000 ticks, outcome open, 0 missing | `353037A43B41B0E2` | yes | yes |
| `mission-run` `MD_GLA05` | 120 | 12,000 ticks, outcome open, 0 missing | `A4385AFE59E38C1C` | yes | yes |
| `mission-run` `GC_ChemGeneral` | 30 | 3,000 ticks, outcome open, 0 missing | `E66023A1A7713080` | yes | yes |
| `mission-run` `GC_ChemGeneral` | 60 | 6,000 ticks, outcome open, 0 missing | `1DFDF910057F2353` | yes | yes |
| `mission-run` `GC_ChemGeneral` | 120 | 12,000 ticks, outcome open, 0 missing | `4F05AAFF1ADB080A` | yes | yes |

No exception, no overflow (`checked` arithmetic of the time service and of `Fixed`), no rejection because of the rate in any of the runs. The reference match finds no winner at 120 Hz in two hours of game time (at 30 and 60 Hz after 8.1 minutes); the run with the full budget (864,000 ticks, recording 30 minutes of computing time, up to 2,039 living objects) was recorded and confirmed by one fresh run at all 864 checkpoints (final hash in the table); the second fresh run with tick hashes and the snapshot continuation did not fit into the time limit of a background run (about 29 minutes per pass) and are **not verified**; fully checked is the run with 216,000 ticks (30 game minutes, 3.7 times the game time of the 30 Hz run).

### 11.2 Rounding during the catalog build (`SimTool rate-audit <content> --tick-rate N`)

Two views of the same loss. **Per conversion:** during the build the probe sees the exact decimal value of every conversion to `Fixed` (Q47.16) and counts what becomes zero or is off by more than 1 % or 10 %.

| Function | Rate | Calls | ≠ 0 | → 0 | > 1 % | > 10 % | worst case (exact value) |
|---|---|---|---|---|---|---|---|
| `DegreesPerSecondToRadiansPerTick` | 30 | 645 | 616 | 0 | 1 | 0 | 4.912 % (5.82 · 10⁻⁵) |
| `DegreesPerSecondToRadiansPerTick` | 60 | 645 | 616 | 0 | 1 | 0 | 4.912 % (2.91 · 10⁻⁵) |
| `DegreesPerSecondToRadiansPerTick` | 120 | 645 | 616 | 0 | 2 | 0 | 4.912 % (1.45 · 10⁻⁵) |
| `DurationMillisecondsToFixedTicks` | 30 / 60 / 120 | 173 | 173 | 0 | 0 | 0 | 0.001 % / 0.000 % / 0.000 % |
| `PerSecondSquaredToPerTickSquared` | 30 | 805 | 529 | 0 | 1 | 0 | 3.870 % (1.11 · 10⁻⁴) |
| `PerSecondSquaredToPerTickSquared` | 60 | 805 | 529 | 0 | 4 | 0 | 9.863 % (2.78 · 10⁻⁵) |
| `PerSecondSquaredToPerTickSquared` | 120 | 805 | 529 | **1** | 36 | 1 | 100 % (6.94 · 10⁻⁶) |
| `PerSecondToPerTick` | 30 | 989 | 673 | 0 | 0 | 0 | 0.708 % (3.33 · 10⁻⁴) |
| `PerSecondToPerTick` | 60 | 989 | 673 | 0 | 0 | 0 | 0.708 % (1.67 · 10⁻⁴) |
| `PerSecondToPerTick` | 120 | 989 | 673 | 0 | 1 | 0 | 8.447 % (8.33 · 10⁻⁵) |

The one conversion that rounds **all the way to zero** at 120 Hz is `Acceleration = 0.1` (per s²) of the `NapalmBombLocomotor` (`Locomotor.ini`): 0.1 / 14,400 lies below half a `Fixed` step. No object of the retail data uses this locomotor (only the definition names it), which is why the value does not appear in the catalog comparison below. The same locomotor yields the 8.447 % (`Speed = 0.1` per s). At 60 Hz nothing rounds to zero.

**Per field:** the 30 Hz catalog and the catalog of the rate are compared value by value (62,648 fixed-point values, 83,166 integers; 2,410 fixed-point values and 12,766 integers differ – the same ones at both rates). A fixed-point value that differs follows a factor (per tick, per tick², duration); it is compared with the exactly scaled 30 Hz value. This reference is itself a fixed-point value: the error measures where the higher rate is coarser than 30 Hz already is, not the error against the data.

| | 60 Hz | 120 Hz |
|---|---|---|
| changed fixed-point values by factor | per tick (÷ 2) 1,634, per tick² (÷ 4) 603, duration (× 2) 173 | per tick (÷ 4) 1,634, per tick² (÷ 16) 603, duration (× 4) 173 |
| ≠ 0 at 30 Hz, 0 at the rate | **0** | **0** |
| relative error above 10 % | 0 | 0 |
| relative error above 1 % up to 10 % | 1 value in 1 field: `LocomotorInfo.Braking` 1 (`FireWallSegment`: raw 73 → 18, 1.37 %) | 41 values in 5 fields: `LocomotorInfo.Braking` 23 (worst `FireWallSegment`: raw 73 → 5, 9.59 %), `Acceleration` 12 (`AFG_AmericaVehicleChinook`: raw 583 → 36, 1.20 %), `Lift` 4 (`AirF_AmericaCrateParachute`: raw 728 → 46, 1.10 %), `TurnRatePerTick` 1 (`FireWallSegment`: raw 38 → 10, 5.26 %), `WeaponInfo.WeaponSpeed` 1 (`NeutronMineWeapon`: raw 22 → 5, 9.09 %) |
| below eight significant bits (raw value below 256, ≠ 0) | 46 values in 5 fields: `LocomotorInfo.Braking` 23, `Acceleration` 12, `TurnRatePerTick` 6, `Lift` 4, `WeaponInfo.WeaponSpeed` 1 | 321 values in 7 fields: `TurretInfo.PitchRate` 105, `LocomotorInfo.Braking` 102, `Acceleration` 88, `TurnRatePerTick` 17, `Lift` 4, `TurretInfo.TurnRate` 4, `WeaponInfo.WeaponSpeed` 1 |
| integers that differ but are not 2 or 4 times the 30 Hz value (duration off the grid) | **0** | **0** |

This is finding E8 of the plan in numbers: quantities per tick² lose two bits with every doubling of the rate. The last row is the proof of the grid rule on the whole catalog: every one of the 12,766 changed integers is exactly the multiple.

### 11.3 Counters at run time (`--time-probe`, first run of each command)

Calls / hits per counter. `PerSecondToPerTick` etc. are the conversions from state (heal and damage amounts per tick); "hit" means: result zero for an input ≠ 0.

| Run | Rate | `RunTimeRoundedToZero` (calls / hits) | `MinimumOneFrame` (`AtLeastOneFrame`: calls / took effect) | `OffGrid` (`ToOriginalFrames` + `CountdownReload` + `DrawFrames`: calls / hits) |
|---|---|---|---|---|
| `reference-match` | 30 | 60,496 / 0 | 4,167 / 0 | 46,058 / 0 |
| `reference-match` | 60 | 108,081 / 0 | 4,574 / 0 | 70,937 / 0 |
| `reference-match` (216,000 ticks) | 120 | 785,308 / 0 | 15,477 / 0 | 321,379 / 0 |
| `reference-match` (864,000 ticks) | 120 | 5,888,716 / 0 | 18,630 / 0 | 20,132,858 / 0 |
| `ai-match` China/GLA | 30 | 35,601 / 0 | 1,627 / 0 | 8,226 / 0 |
| `ai-match` China/GLA | 60 | 18,696 / 0 | 419 / 0 | 2,496 / 0 |
| `ai-match` China/GLA | 120 | 48,278 / 0 | 622 / 0 | 3,499 / 0 |
| `ai-match` America/China | 30 | 3,336 / 0 | 1,009 / 6 | 14,278 / 0 |
| `ai-match` America/China | 60 | 16,023 / 0 | 1,597 / 6 | 24,678 / 0 |
| `ai-match` America/China | 120 | 23,199 / 0 | 789 / 3 | 16,934 / 0 |
| `MD_USA05` | 30 | 2,400 / 0 | 214 / 0 | 45,453 / 0 |
| `MD_USA05` | 60 | 4,812 / 0 | 214 / 0 | 45,468 / 0 |
| `MD_USA05` | 120 | 9,610 / 0 | 214 / 0 | 45,469 / 0 |
| `MD_GLA05` | 30 | 24,410 / 0 | 258 / 11 | 47,323 / 0 |
| `MD_GLA05` | 60 | 30,759 / 0 | 258 / 11 | 47,318 / 0 |
| `MD_GLA05` | 120 | 37,033 / 0 | 257 / 11 | 47,337 / 0 |
| `GC_ChemGeneral` | 30 | 0 / 0 | 2,428 / 13 | 66,261 / 0 |
| `GC_ChemGeneral` | 60 | 0 / 0 | 2,428 / 13 | 66,283 / 0 |
| `GC_ChemGeneral` | 120 | 0 / 0 | 2,428 / 13 | 66,283 / 0 |

- **Rounding to zero at run time: none** – in no run, at no rate, with up to 5,888,716 conversions per run. Counted are the conversions via the time service (`PerSecondToPerTick(Fixed)`, `SharePerTick`, `PerOriginalFrameToPerTick(Fixed)`); **not counted** is what modules form per tick without the time service (products of a catalog rate per tick and a state value, for instance rotation · factor) – there is no counter for that.
- **The lower bound "one frame" takes effect rarely and equally often at every rate** (the same sites, the same number in the missions): `AutoHealBehavior.OnSpawn`/`TryActivate` (`StartHealingDelay` 0; `Phase5Modules.cs`), `StealthUpdate.Update` (`StealthDelay` 0), `SlowDeathBehavior.BeginSlowDeath` (destruction delay 0; `Phase5BlockerModules.cs`), `JetAIUpdate.BeginReload` (reload time 0). In every case a duration of **0** is raised to one frame; a positive value below one frame (a value off the grid) did not occur.
- **Off the grid: 0 hits** for all durations that went through `ToOriginalFrames`, `CountdownReload` and `DrawFrames`. The assumption behind the merged counter helper (§2) holds in these runs.
- `FloorToFrame`/`CeilingToFrame` (blast stages of the neutron missile, turning circle of the delivery aircraft) were not called in any of the runs.

### 11.4 Observations (input for Phase 22 and 23)

- **The results are not comparable with 30 Hz and were not evaluated.** To put the list in §5 into context: the game time until victory deviates differently per run (reference match 8.1 / 8.1 min at 30 / 60 Hz, at 120 Hz no winner in 120 min and eight times as many living objects; China against GLA 13.0 / 6.5 / 6.4 min; America against China 9.4 / 12.3 / 9.1 min) – after stage 2 deadlines and cadences are the same, the integration (§5.2) and the sampling (§5.4) are not.
- **Counters per frame keep their beat:** `CountdownReload` is called equally often in `MD_USA05` at 30, 60 and 120 Hz (44,985 / 45,000 / 45,000 in the same game time), in `GC_ChemGeneral` 65,978 / 66,000 / 66,000 – counters with a period of one frame run once per frame at every rate, not once per tick.
- **Decimal arithmetic at run time:** in `MD_GLA05` the simulation calls `PerSecondSquaredToPerTickSquared(decimal)` 72, 142 and 278 times respectively in the run – that is `TemplateCatalog.GravityPerTickSquared`, which computes on every access by a module (§4); the number grows with the tick count. Not a bug, but a decimal division in a run-time path; a value kept ready on the world (like `_significantHeight`) takes it out.
- **`PerOriginalFrameToPerTick(Fixed)` at the point of use** costs one `Fixed` division per call at 60/120 Hz (none at 30 Hz): 6,348 and 12,623 calls respectively in `MD_GLA05` (bridge scaffolding, jets), 12 and 10 in `MD_USA05`.
- **Coarse values** (§11.2): at 120 Hz 321 catalog values have fewer than eight significant bits, at 60 Hz 46; none becomes zero in the catalog in use. For Phase 22 (Q39.24) the fields `LocomotorInfo.Braking`/`Acceleration`/`Lift`/`TurnRatePerTick`, `TurretInfo.PitchRate`/`TurnRate` and `WeaponInfo.WeaponSpeed` are the yardstick.
- **Violated assumptions noticed while reading** (all in §5.3): a sleep of one tick that was a frame (`RunUpdates`, every module with `return SleepNone`); "hold until the deadline inclusive" (N + 1 steps: the extra step is a tick); the sale (180 frames minus one tick); the first counter after a command from outside (offset below one frame); `LeafletDropBehavior` (first update one tick after creation, a `Delay` of one frame has then not yet elapsed).

### 11.5 Addendum Phase 22: the same counters at 24 fractional bits (6 October 2026, state `d7fca91`)

`Fixed` has had 24 instead of 16 fractional bits since Phase 22 (`docs/FIXED-POINT-Q39-24.md`). The counters from §11.2 and §11.3 were collected anew at the state of the conversion; the 16-bit numbers were measured once more at the state immediately before it
(`a0e7b23`) and agree with §11.2. As in §11: the behaviour at 60 and 120 Hz is not evaluated, the results are not reference values.

**Catalog build (`SimTool rate-audit <content> --tick-rate N`):**

| Counter | 60 Hz, 16 bit | 60 Hz, 24 bit | 120 Hz, 16 bit | 120 Hz, 24 bit |
|---|---|---|---|---|
| conversions that round to zero | 0 | **0** | 1 | **0** |
| conversions above 1 % / above 10 % error (sum of the four functions) | 5 / 0 | **0 / 0** | 39 / 1 | **0 / 0** |
| worst error of a conversion | 9.863 % | 0.007 % | 100 % | 0.422 % (`Acceleration = 0.1` of the `NapalmBombLocomotor`: 117 raw steps instead of 0) |
| catalog values: ≠ 0 at 30 Hz, 0 at the rate | 0 | 0 | 0 | 0 |
| catalog values above 1 % up to 10 % against the exactly scaled 30 Hz value | 1 | **0** | 41 in 5 fields | **0** |
| catalog values above 10 % | 0 | 0 | 0 | 0 |
| catalog values below eight significant bits (raw value below 256, ≠ 0) | 46 in 5 fields | **0** | 321 in 7 fields | **0** |
| integers off the grid | 0 | 0 | 0 | 0 |

At 30 Hz itself two conversions were above 1 % at 16 bit (4.912 % and 3.870 %); at 24 bit the worst case is 0.007 %. Compared were the same 62,648 fixed-point values and 83,166 integers;
2,410 fixed-point values and 12,766 integers differ between the rates as before (per tick 1,634, per tick² 603, duration 173).

**Run time (`--time-probe`, first run of each command, 24 bit).** Calls / hits as in §11.3. Since Phase 22 the reference match runs with the driver seed `5EED0007` and was only recorded here
(`--record-only`, budgets 90,000 and 180,000 ticks respectively); the other commands with their verification runs (second fresh run, continuation from snapshot or savegame).

| Run | Rate | Result (game time) | `RunTimeRoundedToZero` | `MinimumOneFrame` | `OffGrid` | Final hash | Run 2 / continuation identical |
|---|---|---|---|---|---|---|---|
| `reference-match` | 60 | winner FactionGLA at tick 28,590 (7.9 min) | 75,552 / 0 | 6,087 / 0 | 46,594 / 0 | not checked | not checked |
| `reference-match` | 120 | winner FactionGLA at tick 112,920 (15.7 min) | 379,904 / 0 | 7,956 / 0 | 66,884 / 0 | not checked | not checked |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 60 | winner FactionChina at tick 23,220 (6.5 min) | 13,090 / 0 | 477 / 0 | 2,540 / 0 | `17F89955311BDDAE` | yes / yes |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 120 | winner FactionChina at tick 49,440 (6.9 min) | 21,259 / 0 | 505 / 0 | 2,667 / 0 | `BFF1C33F041ACDF8` | yes / yes |
| `ai-match` Alpine Assault, FactionAmerica/FactionChina | 60 | winner FactionAmerica at tick 24,240 (6.7 min) | 2,771 / 0 | 389 / 0 | 9,206 / 0 | `52E95115272B9463` | yes / yes |
| `ai-match` Alpine Assault, FactionAmerica/FactionChina | 120 | winner FactionAmerica at tick 48,240 (6.7 min) | 4,845 / 0 | 362 / 0 | 8,868 / 0 | `20870C5115C41ABC` | yes / yes |
| `mission-run` `MD_USA05` | 60 | 6,000 ticks, outcome open | 4,812 / 0 | 214 / 0 | 45,468 / 0 | `199D42C8B0B88DF6` | yes / yes |
| `mission-run` `MD_USA05` | 120 | 12,000 ticks, outcome open | 9,610 / 0 | 214 / 0 | 45,469 / 0 | `F036DAAF9DEEC606` | yes / yes |
| `mission-run` `MD_GLA05` | 60 | 6,000 ticks, outcome open | 30,759 / 0 | 257 / 11 | 47,331 / 0 | `0992106EBA5D865A` | yes / yes |
| `mission-run` `MD_GLA05` | 120 | 12,000 ticks, outcome open | 37,033 / 0 | 253 / 11 | 47,386 / 0 | `66BC9427E265652E` | yes / yes |
| `mission-run` `GC_ChemGeneral` | 60 | 6,000 ticks, outcome open | 0 / 0 | 2,428 / 13 | 66,283 / 0 | `3ECA1E1988852F4C` | yes / yes |
| `mission-run` `GC_ChemGeneral` | 120 | 12,000 ticks, outcome open | 0 / 0 | 2,428 / 13 | 66,283 / 0 | `A4C732DFBC83F4AF` | yes / yes |

- **Rounding to zero at run time: still none**, at 60 and at 120 Hz. The goal of Phase 22 – zero cases at 60 Hz – is reached in the catalog build and at run time; no case remains to be named. As in
  §11.3 not counted: amounts per tick that modules form without the time service.
- The counters of the missions are those of §11.3 (the same sites, the same numbers except for individual calls): the resolution does not change how often the lower bound "one frame" takes effect.
- No overflow, no exception, no rejection in any of the twelve runs.
- **Observation for Phase 23, not evaluated:** at 24 bit the reference match also ends at 120 Hz (at 16 bit it found no winner there in 864,000 ticks, with a different seed), and the game time until victory
  of the two AI runs is close together at 60 and 120 Hz (China/GLA 6.5 and 6.9 min; America/China 6.7 and 6.7 min; at 16 bit 6.5 / 6.4 and 12.3 / 9.1 min). At 30 Hz and 24 bit it is 6.0 and 9.3 min
  (ticks 10,710 and 16,665, reference manifest).

## 12. Tests

`tests/CnCNew.Simulation.Tests`, all at 30, 60 and 120 Hz on the synthetic fixture of the respective rate unless stated otherwise. The existing tests with tick expectations at 30 Hz (179 ticks sale, 45 ticks interest, 18/9/4 ticks shot interval …) run unchanged and are the 30 Hz proof at test level.

| File | Tests | Proves |
|---|---|---|
| `Tests.TickRate.cs` (stage 1) | 8 | time service against the former formulas (30 Hz), grid and scaling (60/120 Hz); supported rates, fingerprint; catalog per rate; world rejects a foreign catalog; snapshot, replay and mission state carry their rate (50 Hz and foreign catalog rejected) |
| `Tests.TickRate.World.cs` | 7 | sale 179/359/719 ticks; vision cadence 3/6/12 with phase 0; shot interval, reload and wind-up time of every weapon under three bonus states exactly 2× and 4× respectively (18 frames at 400 % are 4 frames); firing stages through the world; flight time in frames; turret deadlines and idle scan on the grid; first look two frames after idle |
| `Tests.TickRate.ModulesAM.cs` | 4 | `BaseRegenerateUpdate` (bursts every three frames, 12.5 points per burst); lifetimes and `AutoDepositUpdate` (0 ms = one frame); hacker counter (period 16 frames, offset of the first counter below one frame); `TensileFormationUpdate` (check at frame 120, sliding phase 300 frames) |
| `Tests.TickRate.ModulesNZ.cs` | 6 | detector staggering (`DrawFrames`); poison without interval and self-healing without delay (lower bound one frame); enemy-nearby scan (`CountdownReload`); variance of the slow death; `AIUpdateData.MoodCheckTicks` = 60 frames per rate |
| `Tests.TickRate.Scripting.cs` | 7 | `VICTORY` after 120 frames, `QUICKVICTORY` after one update; presentation latency in frames, arguments unchanged; probe; rules of the skirmish session; think cadence of the AI; cadences of the reference-match generator; stress test counts steps |
| `Tests.TickRate.Grid.cs` (completion) | 2 | the merged grid helper against both former ones (§2); the probe only observes and counts correctly |
| `Tests.SourceRules.cs` / `Tests.State.cs` (`SourceRules`) | 1 | the rules from §10 over the source text, the constants in frames, and the rules themselves (23 lines they must catch, 22 they must let through) |

Not proven by a rate test of their own (only by the unchanged 30 Hz tests, the manifests and the trial runs): the replanning in `AIUpdateInterface.Move`, the welding times of `SlavedUpdate`, `MobMemberSlavedUpdate`, `PointDefenseLaserUpdate`, `BattleBusSlowDeathBehavior`, the topple values, `CheckpointUpdate.GateStep`, `FireOCLAfterWeaponCooldownUpdate`, the individual deadlines of `SkirmishAi` (only via the cadence), the retarget cadence and the capture cadence of the scripts, the cooldowns of the objective driver, `ProductionUpdate.Enqueue` with build time 0.

## 13. Pitfalls

- `SimulationTiming.cs`, `SyntheticFixture.cs`, `StressScenario.cs`, `SageRandom.cs` and `Tests.World.cs` have CRLF; `git ls-files --eol <datei>` before every script run.
- The name `time` collides in the catalog builders with pattern variables (`is GameTime time`); the parameter is called `timing` there.
- `SimulationWorld` has a property `Time`; in files of the world `Time.X` means the time service, not the namespace.
- A `record` default with a tick constant (`long MoodCheckTicks = …`) is a silent tick value; `SourceRules` now catches it.
- Static fields of a `partial` class are initialised in textual order of the file: the rule tables in `Tests.SourceRules.cs` come before the tables that embed them.
- The probe is attached to the time service of the **rate**, not to a world: two runs of the same rate in the same process count into the same probe. The tools attach it only for the first run.
- After every source change run `dotnet build -c Release tools/CnCNew.SimTool` and run from a copy of the `bin` directory; otherwise `--no-build` shows the old state.

## Appendix A: converted sites of stage 2 per symbol

Column "Is" in today's notation (after the merge of the helpers). Reference paths of the modules relative to `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/Object/`, otherwise as stated; "own" means: no origin in the original.

### A.1 World, catalogs, replay (27)

| File | Symbol | Cl. | Was | Is | Origin |
|---|---|---|---|---|---|
| `World/SimulationWorld.Acquisition.cs` | `OrderedAttackGraceTicks` | B | `const long … = 45` (ticks) | `OrderedAttackGraceFrames = 45`, resolved in `_orderedAttackGraceTicks` | own |
| `World/SimulationWorld.Acquisition.cs` | `ForceIdleTicks` | B | `const long … = 2` | `ForceIdleFrames = 2`, `_forceIdleTicks` (two uses) | `GameLogic/AI/AI.cpp`, field table `ForceIdleMSEC` → `m_forceIdleFramesCount` (`INI::parseDurationUnsignedInt`); used in `AIUpdate.cpp`, `AIUpdateInterface::resetNextMoodCheckTime`. The value 67 ms of the shipped `AIData.ini` is not verified under `references`; by the rounding of the original 67 ms would be three frames, not two (side finding of the inventory, not corrected) |
| `World/SimulationWorld.Acquisition.cs` | `ChaseRefreshTicks` | C | `const long … = 15`, `tick - ChaseTick >= 15` | `ChaseRefreshFrames = 15`, `_chaseRefreshTicks` | own |
| `World/SimulationWorld.Acquisition.cs` | `StepAcquirer` (continue the march) | W2 | `WakeAt(…, tick + 1)` | `tick + TickOrder.Next` | own (order) |
| `World/SimulationWorld.Acquisition.cs` | `Engage` (pursuit) | W2 | `WakeAt(…, tick + 1)` | `tick + TickOrder.Next` | own (order) |
| `World/SimulationWorld.Acquisition.cs` | `EarliestReadyTick` | W2 | `tick - OrderedAttackGraceTicks - 1` | `tick - _orderedAttackGraceTicks - TickOrder.Next` | own (one step below the deadline so that the comparison `<=` ends safely) |
| `World/SimulationWorld.Acquisition.cs` | `MoodCheckOffset` | W1 | offset in `±(rate >> 1)` ticks | drawn in frames (`ToOriginalFrames(rate) >> 1`), then `OriginalFrames(…)` | `AIUpdate.cpp`, `AIUpdateInterface::getNextMoodTarget` (`halfRate = checkRate >> 1`, `GameLogicRandomValue(-halfRate, halfRate)`) |
| `World/SimulationWorld.Turrets.cs` | `TurretAimGraceTicks` | B | `const long … = 15` | `TurretAimGraceFrames = 15`, `_turretAimGraceTicks` (two uses) | own |
| `World/SimulationWorld.Turrets.cs` | `TurretReloadHoldCapTicks` | B | `const long … = 450` | `TurretReloadHoldCapFrames = 15L * SimulationRate.OriginalFramesPerSecond`, `_turretReloadHoldCapTicks` (also as clamp bound in `EarliestReadyTick`) | own |
| `World/SimulationWorld.Turrets.cs` | `TurretSweepEnableTicks` | A | `const long … = 3` | `TurretSweepEnableFrames = 3`, `_turretSweepEnableTicks` | `GameLogic/AI/TurretAI.cpp`, local constant `ENABLE_SWEEP_FRAME_COUNT = 3` (`m_enableSweepUntil = now + …`) |
| `World/SimulationWorld.Turrets.cs` | `TurretLoseInterestTicks` | B | `const long … = 45` | `TurretLoseInterestFrames = 45`, `_turretLoseInterestTicks` | own |
| `World/SimulationWorld.Turrets.cs` | `ScheduleScan` | W1 | `DrawScan(…, MinScanIntervalTicks, MaxScanIntervalTicks)` (draw over ticks) | draw over `ToOriginalFrames(min)…ToOriginalFrames(max)`, then `OriginalFrames(…)` | `TurretAI.cpp`, `TurretAIIdleState::resetIdleScan` (`GameLogicRandomValue(getMinIdleScanInterval(), getMaxIdleScanInterval())`) |
| `World/SimulationWorld.Vision.cs`, `World/SimulationWorld.cs` | `VisionIntervalTicks`, `Step`, `StepObserved` | C | `const int … = 3`, `tick % VisionIntervalTicks == 0` | `VisionIntervalFrames = 3`, property `VisionInterval` (ticks of the world), `tick % VisionInterval == 0` (phase 0 stays) | own |
| `World/SimulationWorld.cs` | `Step`, `StepObserved` | W2 | `_tick = tick + 1` (twice) | `tick + TickOrder.Next` | own (the tick counter itself) |
| `World/SimulationWorld.Construction.cs` | `SaleDurationTicks` | A | `const long … = 179` | `SaleDurationFrames = 45 + 135`, `_saleDurationTicks = OriginalFrames(180) - TickOrder.Next` (179, 359, 719) | `Common/System/BuildAssistant.cpp`: `FRAMES_TO_ALLOW_SCAFFOLD` (`LOGICFRAMES_PER_SECOND * 1.5f`), `TOTAL_FRAMES_TO_SELL_OBJECT` (`LOGICFRAMES_PER_SECOND * 3.0f`), `BuildAssistant::update` (per frame `100 / TOTAL_FRAMES_TO_SELL_OBJECT` percent down to −50 %: 135 frames) |
| `World/SimulationWorld.Construction.cs` | the same site, the "− 1" | W2 | contained in the 179 | `- TickOrder.Next` (not scaled) | own (order: the refund falls into the step before expiry) |
| `World/SimulationWorld.Construction.cs` | `AdvanceConstruction` | W1 | `Math.Max(BuildTicks, 1)` | `Math.Max(BuildTicks, _minimumBuildTicks)` (one frame of the original) | own (lower bound); `DozerAIUpdate::update` only named as caller, origin not verified |
| `World/SimulationWorld.CreationLists.cs` | `ExecuteDeliverPayload` | W1 | `LogicRandom.Range(0, DelayDeliveryMaxTicks)` | `OriginalFrames(LogicRandom.Range(0, ToOriginalFrames(DelayDeliveryMaxTicks)))` | `GameLogic/Object/ObjectCreationList.cpp`, `DeliverPayloadNugget` (`GameLogicRandomValue(0, m_delayDeliveryFramesMax)`; `DelayDeliveryMax` via `INI::parseDurationUnsignedInt`) – in the inventory "origin not verified", now verified |
| `World/SimulationWorld.CreationLists.cs`, `World/SimulationWorld.cs` | `IsSignificantlyAbove` | A | decimal arithmetic per call (since stage 1 with `OriginalFramesPerSecond²`) | `_significantHeight`, computed once per world (the same expression) | `Common/Thing/Thing.cpp`, `Thing::isSignificantlyAboveTerrain` (`-(3*3) * m_gravity`) |
| `World/SimulationWorld.Projectiles.cs` | `FlightTicks` | W1 (in the inventory E) | `ceil(Distanz / Tempo je Tick)` ticks, immediately when the travel is below one tick; `static` | counted in frames against the travel of one frame (`speed.Raw * TicksPerOriginalFrame`), then `OriginalFrames(…)`; instance method | `GameLogic/Object/Weapon.cpp`, `WeaponTemplate::fireWeaponTemplate` (`delayInFrames = v.length() / getWeaponSpeed()`, "< 1.0f", `REAL_TO_INT_CEIL`) |
| `Templates/WeaponCatalog.cs`, `World/SimulationWorld.WeaponSlots.cs` | `WeaponInfo.DelayBetweenShotsWith`, `ClipReloadWith` (`ScaledByRateOfFire`), caller `ConsumeShot` | W1 | `max(1 Tick, floor(Ticks / Multiplikator))` | new overloads with `SimulationTime`: `OriginalFrames(max(MinimumShotDelayFrames, floor(ToOriginalFrames(ticks) / Multiplikator)))` | `Weapon.cpp`, `WeaponTemplate::getDelayBetweenShots` and `getClipReloadTime` (`REAL_TO_INT_FLOOR(… / bonusROF)`); the lower bound is own |
| `Templates/WeaponCatalog.cs`, `World/SimulationWorld.FiringTracker.cs` | `WeaponInfo.PreAttackDelayWith`, caller `PreAttackDone` | W1 | `floor(Ticks × Multiplikator)` | `OriginalFrames(floor(ToOriginalFrames(ticks) × Multiplikator))`; takes `SimulationTime` | `Weapon.cpp`, `WeaponTemplate::getPreAttackDelay` (the return type `Int` truncates) |
| `Templates/WeaponCatalog.cs` | `WeaponCatalog.Build`: missing or 0 ms shot interval | W1 (in the inventory B/fallback) | `SimulationTiming.SleepNone` or `Math.Max(SleepNone, delay)` (one tick) | `timing.OriginalFrames(WeaponInfo.MinimumShotDelayFrames)` (one frame of the original) | own (the original would return 0 and fire in the next frame) |
| `Replay/ReplayRunner.cs` | `Run` (checkpoint and verification cadence) | W2 | `(tick + 1) % options.…` (twice) | `(tick + TickOrder.Next) % options.…`; the intervals themselves are run budgets and stay ticks | own |

In addition, without conversion but named: `Math.Max(1, tick + …)` at three sites of `SimulationWorld.Turrets.cs` (`StopAiming`, `ScanStep`, `ScheduleScan`) is now called `Math.Max(TurretDeadlineSet, …)` – the 1 is the smallest value that means "set" (0 means "no deadline"), not a time value and not an order.

### A.2 Modules A–M (46)

| File | Symbol | Cl. | Was | Is | Origin |
|---|---|---|---|---|---|
| `AirModules.cs` | `ParkingPlaceBehavior.Update` (`_arrivals.RemoveAll`) | W2 (in the inventory B/literal) | `world.Tick - arrival.Tick > 2` | `> 2 * TickOrder.Next` – order: the aircraft is created in the tick of the exit or in the next one | own |
| `MatchModules.cs` | `BaseRegenerateUpdate.HealRateFrames` | A | `const int HealRate = 3`, `return HealRate` | `HealRateFrames = 3`, `return world.Time.OriginalFrames(HealRateFrames)` | `Update/BaseRenerateUpdate.cpp`, `BaseRegenerateUpdate::update`: `const Int HEAL_RATE = 3`, `UPDATE_SLEEP(HEAL_RATE)` |
| `MatchModules.cs` | `BaseRegenerateUpdate.Update` (`amount`) | B | `… * Fixed.FromInt(HealRate) / TicksPerSecondFixed` | the same expression with `Fixed.FromInt(OriginalFrames(HealRateFrames))` – sleep duration and factor via one value | the same function: `amount = HEAL_RATE * maxHealth * m_baseRegenHealthPercentPerSecond / LOGICFRAMES_PER_SECOND` |
| `AirWeaponModules.cs` | `BunkerBusterBehavior.Update` | C | `world.Tick % CrashFxFrequencyTicks == 1` | `== world.Time.OriginalFrames(CrashFxPhaseFrames)` (period from the data, phase 1 frame, both scaled) | `Behavior/BunkerBusterBehavior.cpp`, `update`: `getFrame() % m_crashThroughBunkerFXFrequency == 1` |
| `AirWeaponModules.cs` | `NeutronMissileSlowDeathBehavior.BeginSlowDeath` (draws) | W1 | `LogicRandom.Range(0, (int)…VarianceTicks)` (twice) | `world.Time.DrawFrames(world.LogicRandom, 0, …VarianceTicks)` | `Behavior/SlowDeathBehavior.cpp`, `SlowDeathBehavior::beginSlowDeath`: `GameLogicRandomValue(0, d->m_sinkDelayVariance)` / `(0, d->m_destructionDelayVariance)` (in the inventory "origin not verified") |
| `AirWeaponModules.cs` | the same method (`_destructionTick`) | W1 | `Math.Max(destruction, SleepNone)` | `world.Time.AtLeastOneFrame(destruction)` | own (lower bound) |
| `AirWeaponModules.cs` | `NeutronMissileSlowDeathBehavior.Update` (blast stages) | W1 | `Fixed.FromInt(world.Tick - _activationTick) > blast.DelayTicks` | elapsed time rounded down to whole frames (`world.Time.FloorToFrame(…)`, once per update before the loop), then the same comparison: the stage falls on the same frame boundary at every rate | `Update/NeutronMissileSlowDeathUpdate.cpp`, `update`: `currFrame - m_activationFrame > m_blastInfo[ i ].delay` |
| `AirWeaponModules.cs` | `MissileLauncherBuildingUpdate.SwitchTo` (`Opening`) | W2 (in the inventory A) | `AvailableOnFrame(world) - 1` | `- TickOrder.Next` ("one frame ahead" is order) | `Update/MissileLauncherBuildingUpdate.cpp`, `switchToState`: `getReadyFrame() - 1` |
| `AirWeaponModules.cs` | the same method (`Closing`, `half`) | W1 | `(AvailableOnFrame - now) / 2` | `OriginalFrames(ToOriginalFrames(AvailableOnFrame - now) / 2)` | the same function (halving of a frame count) |
| `AirWeaponModules.cs` | `SpectreData.Parse` (`HowitzerFiringRate`) | W1 | `Math.Max(Ticks(…, 10), 1)` | `configuration.Time.AtLeastOneFrame(Ticks(…, 10))` (guard of the modulo) | `Update/SpectreGunshipUpdate.cpp`, constructor `m_howitzerFiringRate = 10` |
| `AirWeaponModules.cs` | `SpectreGunshipUpdate.Orbit` (`GattlingFiringWindowFrames`) | B | `world.Tick - gattling.LastFireTick <= 2` | `<= world.Time.OriginalFrames(GattlingFiringWindowFrames)` with `= 2` | own (substitute for `OBJECT_STATUS_IS_FIRING_WEAPON`) |
| `BridgeModules.cs` | `BridgeScaffoldBehavior.AbsoluteMinSpeedPerFrame` | D (additive) | `speed < 0.001` per tick | `world.Time.PerOriginalFrameToPerTick(AbsoluteMinSpeedPerFrame)` at the point of use | `Behavior/BridgeScaffoldBehavior.cpp`, `update`: `if( speed < 0.001f ) speed = 0.001f` |
| `BridgeModules.cs` | `BridgeScaffoldBehavior._lateralSpeed/_verticalSpeed` | D (additive) | field default `Fixed.One` per tick | `DefaultSpeedPerFrame`; `OnSpawn` sets both to `PerOriginalFrameToPerTick(DefaultSpeedPerFrame)` (state stays "per tick", key unchanged) | the same file, constructor: `m_lateralSpeed = 1.0f`, `m_verticalSpeed = 1.0f` |
| `CleanupAndSpyModules.cs` | `CleanupHazardUpdate.Update` / `FireWhenReady` (`_nextScan = data.ScanTicks`, two sites) | W1 (counter N + 1) | `_nextScan = data.ScanTicks` | `world.Time.CountdownReload(data.ScanTicks)` | `Update/CleanupHazardUpdate.cpp`, `update`: `m_nextScanFrames--` / `m_nextScanFrames = data->m_scanFrames` |
| `CleanupAndSpyModules.cs` | `CleanupHazardUpdate.FireWhenReady` (`RescanWaitMaxFrames`) | A + W1 | `_nextScan = LogicRandom.Range(0, 3)` | draw in frames (`Range(0, RescanWaitMaxFrames)`), 0 stays 0, otherwise `CountdownReload(OriginalFrames(n))` | the same file, `fireWhenReady`: `m_nextScanFrames = GameLogicRandomValue( 0, 3 )` |
| `CleanupAndSpyModules.cs` | `SpyVisionUpdate.Update` (two returns) | W1 | `Math.Max(data.SelfPoweredIntervalTicks, SleepNone)` | `world.Time.AtLeastOneFrame(data.SelfPoweredIntervalTicks)` | own (lower bound) |
| `CleanupAndSpyModules.cs` | `AssistedTargetingUpdate.ReloadWaitFrames` | C | `const long ReloadWait = 1000` (ticks) | `ReloadWaitFrames = 1000`, scaled once per update | own |
| `CrateModules.cs` | `PilotFindVehicleUpdate.Update` (`scan`) | C / W1 | `Math.Max(data.ScanTicks, SleepNone)` | `world.Time.AtLeastOneFrame(data.ScanTicks)` | `Update/PilotFindVehicleUpdate.cpp`: `ScanRate` via `INI::parseDurationUnsignedInt`, constructor `m_scanFrames = 0` (in the inventory "origin not verified") |
| `DieAndUpdateModules.cs` | `TimedExpiryModule.OnSpawn` | W1 | `Range((int)_minTicks, (int)_maxTicks)`, `if (delay < 1) delay = 1` | `AtLeastOneFrame(DrawFrames(LogicRandom, _minTicks, _maxTicks))` | `Update/LifetimeUpdate.cpp`, `LifetimeUpdate::calcSleepDelay` |
| `DockModules.cs` | `AutoDepositUpdate.Update` (`_depositOnTick`) | C / W1 | `world.Tick + Math.Max(data.DepositTimingTicks, SleepNone)` | `world.Tick + world.Time.AtLeastOneFrame(data.DepositTimingTicks)` | `Update/AutoDepositUpdate.cpp`, `update`: `m_depositOnFrame = getFrame() + modData->m_depositFrame` (in the inventory "origin not verified"; the lower bound is own) |
| `FerryModules.cs` | `RailedTransportDockUpdate.Action` (`_pullPerTick`) | W1 (in the inventory E, Phase 20/23) | `Math.Max(data.PullInsideTicks, 1)` | `world.Time.AtLeastOneFrame(data.PullInsideTicks)` | `Update/DockUpdate/RailedTransportDockUpdate.cpp`: `m_pullInsideDistancePerFrame = mag / m_pullInsideDurationInFrames` |
| `FerryModules.cs` | `RailedTransportDockUpdate.UnloadNext` (`_pushPerTick`) | W1 | `Math.Max(data.PushOutsideTicks, 1)` | `world.Time.AtLeastOneFrame(data.PushOutsideTicks)` | the same file: `m_pushOutsideDistancePerFrame = mag / m_pushOutsideDurationInFrames` |
| `FieldEffectModules.cs` | `TensileFormationUpdate.LifetimeFrames` | A | `const int Lifetime = 300`, `++_life > Lifetime` | `LifetimeFrames = 300`, `++_life > world.Time.OriginalFrames(LifetimeFrames)` (`_life` continues to count updates = ticks) | `Update/TensileFormationUpdate.cpp`, `TensileFormationUpdate::update`: `if (++m_life > 300)` |
| `FieldEffectModules.cs` | `TensileFormationUpdate.DormantCheckFrames` | A | `return 30` | `return world.Time.OriginalFrames(DormantCheckFrames)` | the same function: `return UPDATE_SLEEP(30)` |
| `FieldEffectModules.cs` | `TensileFormationUpdate.PropagatePeriodFrames/PropagatePhaseFrames` | A / C | `_life % 30 == 29` | `_life % OriginalFrames(30) == OriginalFrames(29)` | the same function: `if ( m_life%30 == 29 )` |
| `FlightModules.cs` | `JetSlowDeathBehavior.BeginSlowDeath` (`SinkDelayVarianceTicks`) | W1 | `LogicRandom.Range(0, (int)…)` | `world.Time.DrawFrames(…)` | `Behavior/SlowDeathBehavior.cpp`, `beginSlowDeath` (in the inventory "origin not verified") |
| `FlightModules.cs` | the same method (`DestructionDelayVarianceTicks`) | W1 | `LogicRandom.Range(0, (int)…)` | `world.Time.DrawFrames(…)` | as above |
| `FlightModules.cs` | the same method (`_destructionTick`) | W1 | `Math.Max(destruction, SleepNone)` | `world.Time.AtLeastOneFrame(destruction)` | own |
| `FlightModules.cs` | `CountermeasuresBehavior.ReportMissile` | B / W1 | `Math.Max(data.ReactionTicks, 1)` | `world.Time.AtLeastOneFrame(data.ReactionTicks)` | own (0 means "not set") |
| `FlightModules.cs` | `DeliverPayloadAIUpdate.StartPhase` / `RunDelivering` (`_dropDelayLeft`, two sites) | W1 (counter N + 1) | `= data.DoorDelayTicks` / `= run.DropDelayTicks` | `world.Time.CountdownReload(…)` | `Update/AIUpdate/DeliverPayloadAIUpdate.cpp`: `m_dropDelayLeft = getDoorDelay()`, `--m_dropDelayLeft`, `m_dropDelayLeft = getDropDelay()` |
| `FlightModules.cs` | `DeliverPayloadAIUpdate.TurnTicks` | W1 | `Math.Max(1, (MinTurnRadius() / Speed).Ceiling())`, otherwise `1` | `AtLeastOneFrame(CeilingToFrame(…))` rounded up in frames, otherwise `OriginalFrames(1)` | the same file, `DeliverPayloadAIUpdate::calcMinTurnRadius` (`timeToTravelThatDist`) |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.PendingPathFrames` | B | `world.Tick - _pendingPathTick <= 3` | `<= world.Time.OriginalFrames(PendingPathFrames)` with `= 3` | own |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Update` (`_nextScorch`) | W1 | `birth + (death - birth) * n / Total` | `birth + OriginalFrames(ToOriginalFrames(death - birth) * n / Total)` | `Update/ParticleUplinkCannonUpdate.cpp`, `update`: `nextFactor = (Real)m_scorchMarksMade / (Real)data->m_totalScorchMarks` (in the inventory "origin not verified") |
| `FlightModules.cs` | the same line (`death + 1`) | W2 | `death + 1` ("never again") | `death + TickOrder.Next` | own |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Update` (`_nextPulse`) | W1 | `birth + (death - birth) * n / TotalDamagePulses` | as `_nextScorch` | the same function: `m_nextDamagePulseFrame = orbitalBirthFrame + nextFactor * (orbitalDeathFrame - orbitalBirthFrame)` |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Update` (`_nextLaunchFx`) | C / W1 | `now + Math.Max(data.LaunchFxTicks, 1)` | `now + world.Time.AtLeastOneFrame(data.LaunchFxTicks)` | the same file (`m_nextLaunchFXFrame`; lower bound own) |
| `HackerAndAssaultModules.cs` | `HackInternetAIUpdate.Varied` | W1 | `(Fixed.FromInt(ticks) * variation).Floor()` | varied and rounded down in frames, scaled, then `CountdownReload` | `Update/AIUpdate/HackInternetAIUpdate.cpp`, `UnpackingState::onEnter` / `PackingState::onEnter`: `m_framesRemaining = getUnpackTime() * variation` |
| `HackerAndAssaultModules.cs` | `HackInternetAIUpdate.CashDelay` and `_remaining = 0` when packing inside the building | W1 (counter N + 1) | `_remaining = CashDelay()` or `0` | `world.Time.CountdownReload(…)` or `CountdownReload(0)` | the same file, `HackInternetState::update`: `if (m_framesRemaining > 0) m_framesRemaining--` |
| `JetAIUpdate.cs` | `TaxiAlongPath` (`FallbackTaxiSpeedPerFrame`) | D (additive) | `taxi?.Speed ?? Fixed.One` | `?? world.Time.PerOriginalFrameToPerTick(FallbackTaxiSpeedPerFrame)` | own |
| `JetAIUpdate.cs` | `UpdateAttackLocomotor` | B / W1 | `Math.Max(data.AttackLocomotorPersistTicks, 1)` | `world.Time.AtLeastOneFrame(…)` (the method now takes the world) | own (reference without a lower bound) |
| `JetAIUpdate.cs` | `Land`, helicopter (`MinHelicopterApproachSpeedPerFrame`) | D (additive; in the inventory part of the E row 776–800) | `Fixed.Max(flight.MinSpeed, Fixed.One)` | `Fixed.Max(flight.MinSpeed, PerOriginalFrameToPerTick(…))` | own |
| `JetAIUpdate.cs` | `Land`, glide (`MinLandingSpeedPerFrame`) | D (additive) | `Fixed.Max(flight.MinSpeed, Fixed.One / Fixed.FromInt(2))` | `Fixed.Max(flight.MinSpeed, PerOriginalFrameToPerTick(MinLandingSpeedPerFrame))` | own |
| `JetAIUpdate.cs` | `BeginReload` (`total`) | W1 | `time * fehlend / ClipSize` | `OriginalFrames(ToOriginalFrames(time) * fehlend / ClipSize)` | `Update/AIUpdate/JetAIUpdate.cpp`, `JetOrHeliReloadAmmoState::onEnter`: `rt = (rt * needed) / clipSize` |
| `JetAIUpdate.cs` | `BeginReload` (`_reloadDoneTick`) | A | `Math.Max(total, 1)` | `world.Time.AtLeastOneFrame(total)` | the same function: `if (m_reloadTime < 1) m_reloadTime = 1` |
| `LastBlockerModules.cs` | `SupplyWarehouseCripplingBehavior.Update` (return) | W1 (in the inventory E) | `Math.Max(_nextHealing - world.Tick, SleepNone)` | `world.Time.AtLeastOneFrame(_nextHealing - world.Tick)`; `_nextHealing` itself stays unchanged (state) | `Behavior/SupplyWarehouseCripplingBehavior.cpp`, `update`: `m_nextHealingFrame = now + md->m_selfHealDelay`, `UPDATE_SLEEP(m_nextHealingFrame - now)` |
| `MineModules.cs` | `DemoTrapUpdate.Update` (`_scanCountdown`) | W1 (counter N + 1) | `_scanCountdown = data.ScanTicks` | `world.Time.CountdownReload(data.ScanTicks)` | `Update/DemoTrapUpdate.cpp`, `DemoTrapUpdate::update`: `m_nextScanFrames--` / `m_nextScanFrames = m_scanFrames` |

**Phase position of the counters (known limit).** `CountdownReload` is dimensioned for a counter that is reloaded **in** an update (N + 1 frames then pass until the next action). If the first counter is set from outside and the update still runs in the same tick (a hacker who enters the building between two steps), it ends one tick later than the frame boundary at 60 Hz, up to three ticks later at 120 Hz – a one-time offset below one frame; the period after that is exact. The test `TickRateModulesAmHackerCountdown` records exactly that.

### A.3 Modules N–Z (69)

Column "Cl.": class of the inventory, followed by the case (W1 a = rounding at run time, W1 b = lower bound one tick, W1 c = counter N + 1, W1 d = random over a tick range, W2 = order).

| File | Symbol | Cl. | Was | Is | Origin |
|---|---|---|---|---|---|
| `Phase12Modules.cs` | `FireWeaponWhenDamagedBehavior.Fire` | E / W1 b | `Math.Max(DelayBetweenShotsTicks, SleepNone)` | `world.Time.AtLeastOneFrame(…)` | own (replaces the weapon status; `FireWeaponWhenDamagedBehavior.cpp`, `update`) |
| `Phase12Modules.cs` | `FireWeaponPower.Update` | E / W1 b | sleep `Math.Max(DelayBetweenShotsTicks, SleepNone)` | `AtLeastOneFrame(…)` | own |
| `Phase12Modules.cs` | `HordeUpdate.OnSpawn` | C / W1 d | `LogicRandom.Range(1, UpdateRateTicks)` | `world.Time.DrawFrames(LogicRandom, TicksPerOriginalFrame, AtLeastOneFrame(UpdateRateTicks))` | `HordeUpdate.cpp`, constructor: `UPDATE_SLEEP(GameLogicRandomValue(1, delay))` |
| `Phase12Modules.cs` | `HordeUpdate.Update` (vehicles) | C / W1 c | `world.Tick > _lastRefresh + UpdateRateTicks` | `world.Tick >= _lastRefresh + UpdateRateTicks + TicksPerOriginalFrame` (period rate + 1 frames) | `HordeUpdate.cpp`, `HordeUpdate::update`: `getFrame() > m_lastHordeRefreshFrame + m_updateRate` |
| `Phase5BlockerModules.cs` | `FlammableUpdate.Ignite` (`_aflameEndTick`) | E / W1 b | `Math.Max(AflameDurationTicks, SleepNone)` | `AtLeastOneFrame(…)` | own (minimum value) |
| `Phase5BlockerModules.cs` | `FlammableUpdate.Update` (`_damageEndTick`) | E / W1 b | `Math.Max(AflameDamageDelayTicks, SleepNone)` | `AtLeastOneFrame(…)` | own (minimum value) |
| `Phase5BlockerModules.cs` | `SlowDeathBehavior.BeginSlowDeath` (sink draw) | B / W1 d | `Range(0, SinkDelayVarianceTicks)` | `world.Time.DrawFrames(world.LogicRandom, 0, SinkDelayVarianceTicks)` | `SlowDeathBehavior.cpp`, `beginSlowDeath`: `m_sinkDelay + GameLogicRandomValue(0, m_sinkDelayVariance)` |
| `Phase5BlockerModules.cs` | `SlowDeathBehavior.BeginSlowDeath` (destruction draw) | B / W1 d | `Range(0, DestructionDelayVarianceTicks)` | `world.Time.DrawFrames(…)` | ibid.: `m_destructionDelay + GameLogicRandomValue(0, m_destructionDelayVariance)` |
| `Phase5BlockerModules.cs` | `SlowDeathBehavior.BeginSlowDeath` (`_destructionTick`) | E / W1 b | `Math.Max(destruction, SleepNone)` | `AtLeastOneFrame(…)` | own (minimum value) |
| `Phase5BlockerModules.cs` | `StealthDetectorUpdate.OnSpawn` | C / W1 d | `Range(1, UpdateRateTicks)` | `world.Time.DrawFrames(world.LogicRandom, TicksPerOriginalFrame, UpdateRateTicks)` | `StealthDetectorUpdate.cpp`, constructor: `UPDATE_SLEEP(GameLogicRandomValue(1, m_updateRate))` |
| `Phase5BlockerModules.cs` | `StealthDetectorUpdate.Update` (`MarkDetected`) | E / W2 | `UpdateRateTicks + 1` | `UpdateRateTicks + TickOrder.Next` (not scaled) | `StealthDetectorUpdate.cpp`, `update`: `markAsDetected(m_updateRate + 1)` |
| `Phase5BlockerModules.cs` | `PoisonedBehavior.OnDamage` (`_poisonStopTick`) | E / W1 b | `Math.Max(PoisonDurationTicks, SleepNone)` | `AtLeastOneFrame(…)` | own (minimum value) |
| `Phase5BlockerModules.cs` | `PoisonedBehavior.OnDamage` (`freshDamageTick`) | E / W1 b | `Math.Max(PoisonDamageIntervalTicks, SleepNone)` | `AtLeastOneFrame(…)` | own (minimum value) |
| `Phase5BlockerModules.cs` | `PoisonedBehavior.Update` (`_poisonDamageTick`) | E / W1 b | as above | as above | own (minimum value) |
| `Phase5Modules.cs` | `StealthUpdate.Update` (`_readyTick`) | E / W1 b | `Math.Max(StealthDelayTicks, SleepNone)` | `AtLeastOneFrame(…)` | own (minimum value) |
| `Phase5Modules.cs` | `AutoHealBehavior.OnSpawn` | E / W1 b | `Math.Max(StartHealingDelayTicks, SleepNone)` | `AtLeastOneFrame(…)` | own (minimum value) |
| `Phase5Modules.cs` | `AutoHealBehavior.TryActivate` | E / W1 b | as above | as above | own (minimum value) |
| `Phase5Modules.cs` | `AutoHealBehavior.Update` | E / W1 b | `return Math.Max(HealingDelayTicks, SleepNone)` | `AtLeastOneFrame(…)` (one healing pulse per original frame) | `AutoHealBehavior.h`: `"HealingDelay"` `parseDurationUnsignedInt` (default there `UINT_MAX`, side finding of the inventory) |
| `Phase5Modules.cs` | `AIUpdateData.Parse` (`MoodAttackCheckRate`) | A / W1 b | `Math.Max(1, Ticks(…))` | `configuration.Time.AtLeastOneFrame(Ticks(…))` (catalog build) | `AIUpdate.cpp`, `AIUpdateModuleData::AIUpdateModuleData`: `m_moodAttackCheckRate = LOGICFRAMES_PER_SECOND * 2` |
| `Phase5Modules.cs` | `AIUpdateInterface.Move` (replanning) | C | `world.Tick - _blockerReplanTick >= 30` | `>= _blockerReplanInterval` = `OriginalFrames(BlockerReplanFrames)` with `BlockerReplanFrames = OriginalFramesPerSecond` | own (`docs/DEMO-FINDINGS.md` no. 12) |
| `Phase5Modules.cs` | `AIUpdateInterface.Move` (look-ahead) | B | `Entity.Speed * Fixed.FromInt(25)` (twice) | `Entity.Speed * _blockerLookAhead` = `Fixed.FromInt(OriginalFrames(BlockerLookAheadFrames))`, `BlockerLookAheadFrames = 25` | own |
| `Phase5Modules.cs` | `ProductionUpdate.Update` (door closed) | C / W2 | `RemainingTicks = 1` | `RemainingTicks = TickOrder.Next` (not scaled) | own ("ask again in the next tick") |
| `ProjectileModules.cs` | `MissileAIUpdate.SetFramesTillCountermeasureDiversionOccurs` | E / W1 b | `Math.Max(frames, 1)` | `AtLeastOneFrame(frames)` | `MissileAIUpdate.cpp`, `setFramesTillCountermeasureDiversionOccurs` |
| `ProjectileModules.cs` | `PointDefenseLaserUpdate.FireWhenReady` (target leaves range) | A / W1 c+d | `_nextScan = Range(0, 3)`; `if (_nextScan == 0)` | `wait = Range(0, RescanMaxFrames)`; `_nextScan = CountdownReload(OriginalFrames(wait))`; `if (wait == 0)` | `PointDefenseLaserUpdate.cpp`: `m_nextScanFrames = GameLogicRandomValue( 0, 3 )` |
| `ProjectileModules.cs` | `PointDefenseLaserUpdate.FireWhenReady` (target dead) | A / W1 c+d | as above | as above | ibid. |
| `ProjectileModules.cs` | `PointDefenseLaserUpdate.Update` (`_nextScan = ScanTicks`) | C / W1 c | `_nextScan = data.ScanTicks` | `world.Time.CountdownReload(data.ScanTicks)` | `PointDefenseLaserUpdate.cpp`: `m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames` |
| `ProjectileModules.cs` | `PointDefenseLaserUpdate.FireWhenReady` (two further `_nextScan = ScanTicks`) | C / W1 c | as above | as above (2 sites) | ibid. |
| `ProjectileModules.cs` | `PointDefenseLaserUpdate.FireWhenReady` (`_nextShot`) | E / W1 c | `_nextShot = weapon.DelayBetweenShotsTicks` | `world.Time.CountdownReload(…)` (shot spacing delay + 1 frames) | `PointDefenseLaserUpdate.cpp`: `m_nextShotAvailableInFrames-- / = wt->getDelayBetweenShots( bonus )` |
| `RailroadModules.cs` | `RailroadBehavior.FramesUnpulledLongEnoughToUnhitch` | A / W2 | `const int … = 2` | `const long … = 2 * TickOrder.Next` (not scaled) | `RailroadGuideAIUpdate.cpp`: `#define FRAMES_UNPULLED_LONG_ENOUGH_TO_UNHITCH (2)` |
| `RepairModules.cs` | `RepairDockData.Parse` (without `TimeForFullHeal`) | B / fallback | `Fixed.One` (one tick) | `Fixed.FromInt(configuration.Time.TicksPerOriginalFrame)` (one frame) | `RepairDockUpdate.cpp`, constructor: `m_framesForFullHeal = 1.0f` |
| `RepairModules.cs` | `AutoFindHealingUpdate.Update` | C / W1 c | `_nextScanTicks = data.ScanTicks` | `world.Time.CountdownReload(…)` | `AutoFindHealingUpdate.cpp`: `m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames` |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (Unpacking/Packing) | A | `_framesToWait = 15` | `OriginalFrames(PackFrames)`, `PackFrames = 15` | `SlavedUpdate.cpp`, `setRepairState`: `m_framesToWait = 15` |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (None → Unpacking) | A | `_framesToWait = 15` | `OriginalFrames(PackFrames)` | ibid. |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (Welding → Retracting) | A | `_framesToWait = 5` | `OriginalFrames(ArmFrames)`, `ArmFrames = 5` | ibid.: `m_framesToWait = 5` |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (Ready → Extending) | A | `_framesToWait = 5` | `OriginalFrames(ArmFrames)` | ibid. |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (ready duration) | B / W1 d | `Range(MinReadyTicks, MaxReadyTicks)` | `world.Time.DrawFrames(…)` | ibid.: `GameLogicRandomValue(m_minReadyFrames, m_maxReadyFrames)` |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (welding duration) | B / W1 d | `Range(MinWeldTicks, MaxWeldTicks)` | `world.Time.DrawFrames(…)` | ibid.: `GameLogicRandomValue(m_minWeldFrames, m_maxWeldFrames)` |
| `SlaveModules.cs` | `MobMemberSlavedUpdate.CheckInterval` | A | `const int CheckInterval = 16`; `++_framesToWait < CheckInterval` | `CheckIntervalFrames = 16`; `< world.Time.OriginalFrames(CheckIntervalFrames)` | `MobMemberSlavedUpdate.cpp`, `update`: `if ( ++m_framesToWait < 16 )` |
| `SlaveModules.cs` | `MobMemberSlavedUpdate.OnSpawn` | A / W1 d | `_framesToWait = Range(0, 20)` | `OriginalFrames(Range(0, FirstCheckStaggerFrames))` | `MobMemberSlavedUpdate.cpp`, constructor: `m_framesToWait = GameLogicRandomValue(0,20)` |
| `SpecialAbilityStaging.cs` | `SpecialAbilityUpdate.Varied` | B / W1 a | `Math.Max(1, (Fixed.FromInt(ticks) * factor).Floor())` | the same calculation on `ToOriginalFrames(ticks)`, result via `OriginalFrames` | `SpecialAbilityUpdate.cpp`, `startPacking`/`startUnpacking`: `GameLogicRandomValueReal(1 - m_packUnpackVariationFactor, …)` |
| `SpecialPowerModules.cs` | `SpectreGunshipDeploymentUpdate.TryTakePending` | E / W2 | `world.Tick - value.Tick <= 2` | `<= 2 * TickOrder.Next` (not scaled) | own (the entity comes into being at the end of the tick) |
| `StructureModules.cs` | `StructureCollapseUpdate.OnDie` (`_collapseTick`) | B / W1 d | `Range(MinCollapseDelay, MaxCollapseDelay)` | `world.Time.DrawFrames(…)` | `StructureCollapseUpdate.cpp`, `beginStructureCollapse` |
| `StructureModules.cs` | `StructureCollapseUpdate.Update` (first `_burstTick`) | B / W1 d | `Range(MinBurstDelay, MaxBurstDelay)` | `world.Time.DrawFrames(…)` | `StructureCollapseUpdate.cpp`, `update`: `m_burstFrame += GameLogicRandomValue(d->m_minBurstDelay, d->m_maxBurstDelay)` |
| `StructureModules.cs` | `StructureCollapseUpdate.Update` (`_burstTick +=`) | B / W1 d | as above | as above | ibid. |
| `StructureModules.cs` | `FireSpreadUpdate.NextDelay` | B / W1 b+d | `Math.Max(Range(MinDelay, MaxDelay), SleepNone)` | `world.Time.AtLeastOneFrame(world.Time.DrawFrames(…))` | `FireSpreadUpdate.cpp`, `calcNextSpreadDelay` |
| `ToppleAndFallModules.cs` | `StructureToppleUpdate.OnDie` (`_toppleFrame`) | B / W1 d | `Math.Max(0, Range(MinToppleDelayTicks, MaxToppleDelayTicks))` | `Math.Max(0, world.Time.DrawFrames(…))` | `StructureToppleUpdate.cpp`, `beginStructureTopple` |
| `ToppleAndFallModules.cs` | `ToppleUpdate.VelocityBounceLimit` | A (additive per frame) | `0,01` as a threshold against the angular velocity per tick | `VelocityBounceLimitPerFrame`; at the use `world.Time.PerOriginalFrameToPerTick(…)` (only in the bounce tick) | `ToppleUpdate.cpp`, `ToppleUpdate::update`: `const Real VELOCITY_BOUNCE_LIMIT = 0.01f` |
| `ToppleAndFallModules.cs` | `StructureToppleUpdate.AccelFactor` | A (additive per frame²) | `0,02` as angular acceleration per tick² | `AccelFactorPerFrameSquared`; in `StepToppling` `world.Time.PerOriginalFrameSquaredToPerTickSquared(…)` | `StructureToppleUpdate.cpp`, `update`: `TOPPLE_ACCELERATION_FACTOR = 0.02f` |
| `TransportModules.cs` | `HelicopterSlowDeathBehavior.BeginSlowDeath` (sink draw) | E / W1 d | `Range(0, SinkDelayVarianceTicks)` | `world.Time.DrawFrames(…)` | `SlowDeathBehavior.cpp`, `beginSlowDeath` (see above) |
| `TransportModules.cs` | `HelicopterSlowDeathBehavior.BeginSlowDeath` (destruction draw) | E / W1 d | `Range(0, DestructionDelayVarianceTicks)` | `world.Time.DrawFrames(…)` | ibid. |
| `TransportModules.cs` | `HelicopterSlowDeathBehavior.BeginSlowDeath` (`_destructionTick`) | E / W1 b | `Math.Max(destruction, SleepNone)` | `AtLeastOneFrame(…)` | own (minimum value) |
| `VehicleModules.cs` | `BattleBusSlowDeathBehavior.GroundCheckDelay` | A | `const long … = 10`; `world.Tick + GroundCheckDelay` | `GroundCheckDelayFrames`; `world.Tick + OriginalFrames(…)` | `BattleBusSlowDeathBehavior.cpp`: `GROUND_CHECK_DELAY = 10` |
| `VehicleModules.cs` | `BattleBusSlowDeathBehavior.EmptyHulkCheckDelay` | A | `const long … = 15`; `return EmptyHulkCheckDelay` | `EmptyHulkCheckDelayFrames`; `return OriginalFrames(…)` | `BattleBusSlowDeathBehavior.cpp`: `EMPTY_HULK_CHECK_DELAY = 15`, `UPDATE_SLEEP(EMPTY_HULK_CHECK_DELAY)` |
| `VehicleModules.cs` | `BattleBusSlowDeathBehavior.BeginSlowDeath` (sink draw) | E / W1 d | `Range(0, SinkDelayVarianceTicks)` | `world.Time.DrawFrames(…)` | `SlowDeathBehavior.cpp`, `beginSlowDeath` |
| `VehicleModules.cs` | `BattleBusSlowDeathBehavior.BeginSlowDeath` (destruction draw) | E / W1 d | `Range(0, DestructionDelayVarianceTicks)` | `world.Time.DrawFrames(…)` | ibid. |
| `VehicleModules.cs` | `BattleBusSlowDeathBehavior.BeginSlowDeath` (`_destructionTick`) | E / W1 b | `Math.Max(destruction, SleepNone)` | `AtLeastOneFrame(…)` | own (minimum value) |
| `WorldEffectModules.cs` | `OCLUpdate.SetNextCreation` | E / W1 d | `Range(MinDelay, MaxDelay)` | `world.Time.DrawFrames(…)` | `OCLUpdate.cpp`: `GameLogicRandomValue(m_minDelay, m_maxDelay)` |
| `WorldEffectModules.cs` | `DynamicShroudClearingRangeUpdate.Update` (`_changeIntervalCountdown`) | C / W1 c | `= GrowInterval` or `ChangeInterval` | `world.Time.CountdownReload(…)` | `DynamicShroudClearingRangeUpdate.cpp`, `update` (lines 271–279) |
| `WorldEffectModules.cs` | `FireOCLAfterWeaponCooldownUpdate.Update` | E / W2 | `LastFireTickOf(slot) == now - 1` | `== now - TickOrder.Next` (not scaled) | `FireOCLAfterWeaponCooldownUpdate.cpp`, `update`: `weapon->getLastShotFrame() == now - 1` |
| `WorldEffectModules.cs` | `FireOCLAfterWeaponCooldownUpdate.FireOcl` | B / W1 a | `(Tick − _startTick) * LifetimePerSecond / 1000` (truncated, in ticks) | the same calculation on `ToOriginalFrames(Tick − _startTick)`, result via `OriginalFrames` | ibid., lines 200–202 |
| `WorldEffectModules.cs` | `WaveGuideUpdate.Update` (splash throw) | A / W1 c | `now - _splashTick > OriginalFrames(30 / 2)` | `>= OriginalFrames(SplashFrames) + TicksPerOriginalFrame` (period 16 frames) | `WaveGuideUpdate.cpp`, `update`: `getFrame() - m_splashSoundFrame > LOGICFRAMES_PER_SECOND / 2.0f` |
| `WorldEffectModules.cs` | `CheckpointUpdate.GateStep` | D (additive per frame) | `0,333` per tick | `GateStepPerFrame`; in `Update` `world.Time.PerOriginalFrameToPerTick(…)` | `CheckpointUpdate.cpp`, `update`: `radius - 0.333f` / `radius + 0.333f` |
| `WorldEffectModules.cs` | `CheckpointUpdate.OnSpawn` | E / W1 d | `Range(0, ScanDelayTicks)` | `world.Time.DrawFrames(world.LogicRandom, 0, ScanDelayTicks)` (value still unused, the draw stays) | `CheckpointUpdate.cpp`, constructor: `GameLogicRandomValue(0, m_enemyScanDelayTime)` |
| `WorldEffectModules.cs` | `EnemyNearUpdate.OnSpawn` | E / W1 c+d | `Range(0, ScanDelayTicks)` | `CountdownReload(DrawFrames(LogicRandom, 0, ScanDelayTicks))` | `EnemyNearUpdate.cpp`, constructor: `GameLogicRandomValue(0, m_enemyScanDelayTime)` |
| `WorldEffectModules.cs` | `EnemyNearUpdate.Update` | C / W1 c | `_scanDelay = data.ScanDelayTicks` | `world.Time.CountdownReload(…)` | `EnemyNearUpdate.cpp`, `update`: `if (m_enemyScanDelay == 0) … else --m_enemyScanDelay` |
| `WorldEffectModules.cs` | `FirestormDynamicGeometryInfoUpdate.OnSpawn` | E / W1 b | `Math.Max(InitialDelay, 1)` | `AtLeastOneFrame(InitialDelay)` | `DynamicGeometryInfoUpdate.cpp`, `update`: `m_startingDelayCountdown--` |
| `WorldEffectModules.cs` | `FirestormDynamicGeometryInfoUpdate.Update` (damage scan) | E / W1 b | `Tick − _lastDamage >= DamageDelay` (at 0 every tick) | `>= AtLeastOneFrame(DamageDelay)` | `FirestormDynamicGeometryInfoUpdate.cpp`, `update` (lines 215–219) |
| `WorldEffectModules.cs` | `WeaponBonusUpdate.Update` | C / W1 b | `Math.Max(Delay, SleepNone)` | `AtLeastOneFrame(Delay)` | `WeaponBonusUpdate.cpp`, `update`: `return UPDATE_SLEEP(data->m_bonusDelay)` |

The row `PointDefenseLaserUpdate.FireWhenReady` "two further `_nextScan = ScanTicks`" counts twice. On `AIUpdateInterface.Move`: replanning deadline and look-ahead are resolved **once per module instance** (`ResolveBlockerTiming`, two fields that are not saved, at the first `Move`); the sentinel value `_blockerReplanTick = -1000` ("never planned", also the default in the snapshot) stays. `StructureToppleUpdate.StepToppling` computes one `Fixed` division per tick of the toppling at 60/120 Hz (none at 30 Hz).

### A.4 Scripts, mission, skirmish, scenarios (46)

"Order" means: not scaled, written with `TickOrder.Next` (W2). `SkirmishAi` and the drivers are own code without an origin in the original.

| File | Symbol | Cl. | Was | Is | Origin |
|---|---|---|---|---|---|
| `Skirmish/SkirmishAi.cs` | `FireInterval` → `FireIntervalFrames`, field `_fireInterval` | C | `(_tick + _phase) % 5` | `(_tick + _phase) % _fireInterval`, in the constructor `Time.OriginalFrames(5)` | own |
| `Skirmish/SkirmishAi.cs` | `ThinkInterval` → `ThinkIntervalFrames`, field `_thinkInterval` | C | `% 15` | `% _thinkInterval` | own |
| `Skirmish/SkirmishAi.cs` | `_phase` | C | `Player % 5` ticks | `Time.OriginalFrames(Player % FireIntervalFrames)` | own |
| `Skirmish/SkirmishAi.cs` | `SkirmishAiStats.AttackTicks` | B | `+= 15` per think step | `+= _thinkInterval` (ticks) | own |
| `Skirmish/SkirmishAi.cs` | `RetryTicks` → `RetryFrames` (default of `Backoff`, four direct uses) | B | 300 ticks | `Backoff` takes frames and scales by itself | own |
| `Skirmish/SkirmishAi.cs` | `MemoryTicks` → `MemoryFrames` | B | 900 | `Time.OriginalFrames(MemoryFrames)` | own |
| `Skirmish/SkirmishAi.cs` | `OrderRefreshTicks` → `OrderRefreshFrames` | B | 150 | likewise | own |
| `Skirmish/SkirmishAi.cs` | `BoardingTicks` → `BoardingFrames` (2 sites) | B | 300 | likewise | own |
| `Skirmish/SkirmishAi.cs` | `RevisitTicks` → `RevisitFrames` (2 sites) | B | 3600 | likewise | own |
| `Skirmish/SkirmishAi.cs` | `QueueWorker`: `Backoff(…, 60)` | B | 60 | `ShortRetryFrames` | own |
| `Skirmish/SkirmishAi.cs` | `QueueWorker`: `BuildTicks + 30` | B | 30 | `BuildTicks + Time.OriginalFrames(InFlightMarginFrames)` (`BuildTicks` is already ticks) | own |
| `Skirmish/SkirmishAi.cs` | `BuildStructures`: `essential ? 60 : RetryTicks` | B | 60 | `ShortRetryFrames`/`RetryFrames` | own |
| `Skirmish/SkirmishAi.cs` | `FindSupplyPlacement`: `Backoff(…, 150)` | B | 150 | `SupplyLookRetryFrames` | own |
| `Skirmish/SkirmishAi.cs` | `ManageSites`: `_tick - LastResume < 60` | B | 60 | `Time.OriginalFrames(ShortRetryFrames)` | own |
| `Skirmish/SkirmishAi.cs` | `BuyScience`: `_tick + 1800` | B | 1800 | `ScienceRetryFrames` | own |
| `Skirmish/SkirmishAi.cs` | `UsePowers`: `RetryTicks`/3000/900 | B | ticks | `RetryFrames`/`PowerMissingScienceRetryFrames`/`PowerRefusedRetryFrames`, scaled once | own |
| `Skirmish/SkirmishAi.cs` | `CommandArmy`: `< 60` | B | 60 | `ShortRetryFrames` | own |
| `Skirmish/SkirmishAi.cs` | `UseTunnels`: `Backoff(0x81000000u, 90)` | B | 90 | `TunnelEvacuationRetryFrames` | own |
| `Skirmish/SkirmishAi.cs` | `UseTransports`: `Backoff(…, 60)` | B | 60 | `ShortRetryFrames` | own |
| `Skirmish/SkirmishAi.cs` | `Scout`: `_tick - _scoutAt < 450` | B | 450 | `ScoutReachFrames` | own |
| `Skirmish/SkirmishAi.cs` | `ScoutForWarehouses`: `Backoff(…, 300)` | B | 300 | `WarehouseScoutRetryFrames` | own |
| `Skirmish/SkirmishAi.cs` | `OrderCarriers`: `_tick - since < 90` | B | 90 | `CarrierIdleFrames` | own |
| `Skirmish/SkirmishMatchSession.cs` | `VictoryCheckInterval` → `VictoryCheckIntervalFrames`, field `_victoryCheckInterval` | C | `tick % 15` | `tick % _victoryCheckInterval` | own |
| `Skirmish/SkirmishMatchSession.cs` | `SkirmishMatchOptions.SciencePointInterval` | C | `tick % Option` | `tick % _sciencePointInterval` (option in frames) | own |
| `Skirmish/SkirmishMatchSession.cs` | `SkirmishMatchOptions.GraceTicks` | B | `tick + 1 + Option` | `tick + TickOrder.Next + _graceTicks` (option in frames) | own |
| `Skirmish/SkirmishMatchSession.cs` | `+ 1` before the grace period | E, order | literal | `TickOrder.Next` | own |
| `Testing/ReferenceMatchScenario.cs` | `MoveCadence` → `MoveCadenceFrames`, field `_moveCadence` (command round and victory check) | C | `tick % 15` | `tick % _moveCadence` | own |
| `Testing/ReferenceMatchScenario.cs` | `ProductionCadence` → `ProductionCadenceFrames`, `_productionCadence` | C | `tick % 30` | `tick % _productionCadence` | own |
| `Testing/ReferenceMatchScenario.cs` | `ReferenceMatchOptions.IncomeInterval`, `_incomeInterval` | C | `tick % Option` | option in frames, scaled once in `Record` | own |
| `Testing/ReferenceMatchScenario.cs` | `OrderRefreshTicks` → `OrderRefreshFrames`, `_orderRefresh` | H/time | 150 ticks | scaled | own |
| `Testing/ReferenceMatchScenario.cs` | `ReferenceMatchOptions.GraceTicks` | H/time | `tick + 1 + Option` | `tick + TickOrder.Next + grace` | own |
| `Testing/ReferenceMatchScenario.cs` | `tick == 1`, `DockCommand(…, 1, …)`, `+ 1` | H/ticks, order | literal 1 | `TickOrder.Next` (3 sites) | own |
| `Testing/StressScenario.cs` | reserve before the end of the drain | H/time | `Ticks - tick - 60` | `- world.Time.OriginalFrames(DrainReserveFrames)` | own |
| `Scripting/ScriptEngine.cs` | `EndGameDelayTicks` → `EndGameDelayFrames`, property `EndGameDelay` (3 uses: `VICTORY`, `DEFEAT`, `LOCALDEFEAT` in `Phase13.cs`) | A | 120 ticks | `_world.Time.OriginalFrames(120)` | `ScriptEngine.cpp`, `FRAMES_TO_SHOW_WIN_LOSE_MESSAGE = 120`, `ScriptEngine::startEndGameTimer` |
| `Scripting/ScriptEngine.Actions.cs` | `QUICKVICTORY` `End(…, 1)` | E, order | literal 1 | `TickOrder.Next` | `ScriptEngine.cpp`, `ScriptEngine::startQuickEndGameTimer` (`m_endGameTimer = 1`) |
| `Scripting/ScriptEngine.Actions.cs` | `RetargetInterval` → `RetargetIntervalFrames` | C | `_tick % 30 != Entity % 30` | `_tick % Time.OriginalFrames(30) != Entity % 30 * TicksPerOriginalFrame`; period and factor once before the loop | own |
| `Scripting/ScriptEngine.Actions.cs` | `Forward`, `PresentationLatencyTicks` | B | `_tick + Option` | `_tick + _world.Time.OriginalFrames(Option)` | own |
| `Scripting/ScriptEngine.Phase13.cs` | `UpdateCaptureOrders` | C | `tick % 30 == 0` | `tick % Time.OriginalFrames(CaptureResendFrames) == 0`, once before the loop | own |
| `Testing/MissionScenario.cs` | `MissionProbe.Run(settleTicks)` | H/time | `Tick < settleTicks` | `Tick < Time.OriginalFrames(settleTicks)` | own |
| `Testing/MissionScenario.cs` | `MissionProbe.Run(waitTicks)` | H/time | `killTick + waitTicks` | `killTick + Time.OriginalFrames(waitTicks)` | own |
| `Testing/MissionObjectiveDriver.cs` | `MissionObjectiveDriver.Run(settleTicks)` | H/time | ticks | scaled | own |
| `Testing/MissionObjectiveDriver.cs` | `MissionObjectiveDriver.Run(spacing)` | C | `nextAt = Tick + spacing` | `+ Time.OriginalFrames(spacing)` | own |
| `Testing/ObjectiveDriver.cs` | `ObjectiveDriver.Leaf.Cooldown` (two comparisons in `Round`) | H/time | `tick - last < Cooldown` | `tick - last < Time.OriginalFrames(Cooldown)`; with that all 34 cooldowns in `ObjectiveDriver.Leaves.cs` (31), `ObjectiveDriver.cs` (2 further leaves) and `ObjectiveDriver.Commands.cs` (1) are frames | own |
| `Testing/ObjectiveDriver.Commands.cs` | `ObjectiveDriver.PublishObjectives` | H/time | `> 150` | `> Time.OriginalFrames(ObjectiveExpiryFrames)` | own |

`SkirmishAi`: all deadlines are only set and checked at think ticks – no conversion per tick; `Update` compares two precomputed fields. In `StressScenario` the ramp and the modulo cadences are called `…Steps` and are **not** scaled (§8.3).

## Appendix B: checked and deliberately unchanged

What the briefs of stage 2 read and did not convert, with the reason – the assessment behind the remaining hits of the search patterns (§10). What is different at 60 Hz is not here but in §5.

### B.1 World, catalogs, replay

| File | Symbol | Why |
|---|---|---|
| `World/SimulationWorld.Vision.cs` | `VisionIntervalTicks` (`public const int = VisionIntervalFrames`) | Stays as a name for the callers outside the simulation (`CnCNew.Game/Live/LiveGame.cs`, `SimTool/PerfCommand.cs`, `Presentation.Tests/ObjectTests.cs`, `Simulation.Tests/Tests.Vision.cs`), which run at 30 Hz. Phase 21: use `world.VisionInterval` there, then remove the constant |
| `Templates/TurretInfo.cs` | `NeverScanTicks = 9999999` | Sentinel value ("never"), is compared with `CanScan` and never scaled (like `SleepForever`) |
| `Templates/WeaponCatalog.cs` | `WeaponInfo.InstantSpeed` | Sentinel value, decided in stage 1 |
| `Templates/ObjectTemplate.cs` | `TemplateCatalog.GravityPerTickSquared` | Still computes per access (one decimal division); a field on the catalog would change the input manifest. The world itself no longer reads the value in the hot path (`_significantHeight`) |
| `World/SimulationWorld.Acquisition.cs` | `MoodCheckOffset`: `(ulong)(tick + 1)` | Offset of the hash input (no zero value in the first tick), neither time nor order; noted in the comment |
| `World/SimulationWorld.cs` | `RunUpdates` (`sleep < SleepNone`), `WakeAt` (`_tick + SleepNone`), `Register` (`NextWake = _tick + SleepNone`) | `SimulationTiming.SleepNone` is the named smallest sleep (order, convention §1 case (e)) |
| `World/SimulationWorld.cs` | `MaxUpdatesPerTick`, `MaxEventsPerTick` | Storm limits per step, no time |

Checked, not coupled (or format):

- `Commands/Commands.cs` (`SimCommand.Tick`, `SetTimerCommand.DelayTicks`/`RepeatTicks`), `Timers/SimTimer.cs`, `SimulationWorld.Execute`/`FireTimers` (`DueTick = _tick + set.DelayTicks`, `tick + timer.RepeatTicks`): format (G). The values are ticks of the rate of the world; whoever creates the command computes in that rate.
- `Events/SimEvent.cs`, `Replay/ReplayFormat.cs`, `Snapshots/WorldSnapshot.cs`, `SimulationWorld.Snapshot.cs`: format (G), stage 1. `Replay/ReplayRunner.cs` (`RecordCheckpointInterval`, `VerifyIncrementalEvery`, `StopTick`): run budgets, stay ticks.
- `SimulationWorld.FiringTracker.cs`: `ContinuousFireCoastTicks` comes scaled from the catalog; `StepFiringTrackers` polls per tick, the comparison works with absolute ticks (load only).
- `SimulationWorld.Construction.cs` `AdvanceConstruction`: `ConstructionWork++` per work tick against `BuildTicks`, which the catalog scales – same build time, finer health steps.
- `SimulationWorld.cs`: `SpawnSetup.MinLifetimeTicks`/`MaxLifetimeTicks`, `BaseRegenDelayTicks`, `production.Enqueue(…, BuildTicks)`; `SimulationWorld.BattlePlan.cs` `ParalyzeTroops(…, untilTick)`; `SimulationWorld.StructuresAndWorld.cs` `CreateFromListWithLifetime(…, lifetimeTicks)`; `SimulationWorld.CrateServices.cs` `SetPowerSabotagedUntil`; `TunnelTracker.AddRider(…, tick)`: catalog values or absolute ticks that the caller (modules) supplies and that the world only passes through.
- `SimulationWorld.Collision.cs` (`Fixed.FromInt(150)`), `SimulationWorld.CreationLists.cs` (`formation % 2`, 90 degrees): distances and geometry. `Terrain/`, `Numerics/`, `Views/`, `State/`, `Ids/`, `Random/`: no time (the hits of the search patterns are hash constants `z >> 30`, series terms in `FixedTrig`, enum numbers).
- `Templates/ObjectTemplate.cs`, `SpecialPowerCatalog.cs`, `UpgradeCatalog.cs`, `ObjectCreationListCatalog.cs`, `TurretInfo.cs`: all conversions have run through the parameter `timing` since stage 1; no literal open.

### B.2 Modules A–M

Done with stage 1 (re-checked): `ParkingPlaceBehavior.HealRateFrames` and the healing amount; `EmpData` `Lifetime` (fallback value 1 frame), `LeafletData` `Delay` (1), `BunkerBusterData` `CrashThroughBunkerFXFrequency` (4), `SpectreData` `HowitzerFiringRate` (10) and `StrafingIncrement`, `PropagandaTowerData` `DelayBetweenUpdates` (100), `MinefieldData` `CreatorDeathCheckRate`; `BridgeData` `Lateral/VerticalScaffoldSpeed`; `TransportContain` (regeneration), `PropagandaTowerBehavior.Give`, `MinefieldBehavior.Update` (`DegenPerSecond`), `JetAIUpdate.CircleDeadAirfield`, `CountermeasuresBehavior.LaunchVolley`, `ParticleUplinkData` `ManualDrivingSpeed`/`ManualFastDrivingSpeed`, `ParticleUplinkCannonUpdate.Pulse` (`seconds`), `DockMachine.ClearanceTimeoutFrames`, `ModuleConfigReader.Ticks`, `JetAIUpdate.PauseBeforeTakeoff` (`TickOrder.Next`).

| File | Symbol | Reason |
|---|---|---|
| `AirWeaponModules.cs` | `AirWeaponModules.FixedTicks` (`Blast…Delay`) | unrounded duration as in the original (`parseDurationReal`); the snapping to the grid now happens in the comparison (Appendix A.2) |
| `AirWeaponModules.cs` | `NeutronMissileData.MaxTurnRate` fallback value | deliberately unchanged in stage 1 (every value from π upwards snaps in immediately) |
| `AirWeaponModules.cs` | `SpectreGunshipUpdate.Orbit` (`world.Tick % HowitzerFiringRateTicks == 0`) | period from the data (scaled), phase 0: the same point in game time at every rate |
| `CleanupAndSpyModules.cs` | `SpyVisionSpecialPower.Apply` | sum and cap of durations that all lie on the 30 Hz grid: at 60 Hz exactly double (the assumption of the inventory "smaller rounding error" no longer holds with the grid rounding of stage 1) |
| `CleanupAndSpyModules.cs` | `SpyVisionUpdate.ActivateSpyVision`, `AssistedTargetingUpdate.Update` (`Math.Max(…, SleepNone)`) | `SleepNone` is here the smallest sleep of the scheduler (a wake-up must lie in the future), not a duration – order |
| `CombatModules.cs` | `FireWeaponUpdate.Update` | polls every tick; the shot times are absolute ticks from data – load only |
| `ContainChainModules.cs` | `RequestPayload`/`EnterPayload`, `OverlordContain.Update` | "one tick later" without a literal (order) or tracking per tick – load only |
| `ContainModules.cs` | `GarrisonContain.HealOne` | `MaxHealth / TimeForFullHealTicks` per tick: scales by itself with the converted duration |
| `DockModules.cs` | `DockUpdateBase.Update`, `AutoDepositUpdate.OnSpawn` (`Math.Max(_depositOnTick, Tick + SleepNone)`) | polling per tick without a literal; smallest sleep of the scheduler |
| `DockMachine.cs` | `ProcessDock` with `ActionDelay` 0 | calls the action of the dock every tick; what the action does per call lies in the dock modules (N–Z: repair, supply) – assess there |
| `HackerAndAssaultModules.cs` | `AssaultTransportAIUpdate.Update` | never sleeps, no tick literals – only load and reaction latency |
| `JetAIUpdate.cs` | `Update` (`return SleepNone`) | the state machine runs every tick; relevant to the result only through the integration (class E, §5.2) |
| `LastBlockerModules.cs` | `SupplyWarehouseCripplingBehavior.OnDamage` (`Math.Max(SuppressTicks, SleepNone)`) | smallest sleep of the scheduler |
| `MatchModules.cs` | `BaseRegenerateUpdate.OnDamage` (`Math.Max(BaseRegenDelayTicks, SleepNone)`) | smallest sleep of the scheduler |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Initiate` (`Math.Max(world.Tick, 1)`) | 0 means "no use"; sentinel, not a duration |
| `FlightModules.cs` | `DeliverPayloadAIUpdate.Strafe` | requests one shot per update; the cadence is enforced by the weapon (`FireCurrentWeaponAtPosition`, world) – not to be solved in this file |
| `FlightModules.cs` | `ParticleUplinkData` (`long BeginChargeTicks = 0` etc. as `record` default) | default 0, no silent tick value; only `Parse` and tests create the record |
| `Bodies.cs`, `BuiltInModules.cs`, `CreateCrateDie.cs`, `JetAIUpdate.Flight.cs` (except for §5.2), `ModuleBindings.cs`, `CollideModule.cs`, `ModuleFramework.cs` | – | no tick values: random over fractions and indices, binding tables, interfaces (`IsSignificantlyAboveTerrain` has been rate-independent since stage 1, `AdvanceConstruction` lies in the world) |

### B.3 Modules N–Z

| File | Symbol | Finding |
|---|---|---|
| `Phase12Modules.cs` | `TechBuildingData.PulseRateTicks` (F/tick) | no consumer in the simulation; already per rate via `ModuleConfigReader.Ticks` |
| `Phase12Modules.cs` | `HordeData.Parse` (`UpdateRate`), `HordeUpdate.Update` (infantry sleeps `UpdateRateTicks`) | fallback value in frames since stage 1; sleep duration is a catalog value |
| `Phase5BlockerModules.cs` | `StealthDetectorData.Parse` (`DetectionRate`, fallback 1 frame) | stage 1 |
| `Phase5BlockerModules.cs` | `StealthDetectorUpdate.Update`: `return Math.Max(UpdateRateTicks, SleepNone)` | **deliberately one tick:** the detection lasts `UpdateRateTicks + TickOrder.Next`; a lower bound of one frame would open a gap of one tick between two scans at `DetectionRate = 0`. Effective only at `DetectionRate 0` |
| `Phase5BlockerModules.cs` | `FlammableUpdate.Update`: `return Math.Max(soonest - now, SleepNone)`; `WorldEffectModules.cs` `WeaponBonusUpdate.Update` likewise | sentinel of the scheduler (never sleep 0) on a difference of absolute ticks, no time |
| `Phase5Modules.cs` | `StealthData.MoveThresholdSpeed`, `AIUpdateData.DefaultMoodCheckFrames`, `UpdateTask` (`RepairRatePerSecond / TicksPerSecondFixed`) | stage 1 |
| `Phase5Modules.cs` | `StealthUpdate.MarkDetected`: `Math.Max(ticks, SleepNone)` | lower bound of a value that the caller forms from rate + order; stays one tick |
| `Phase5Modules.cs` | `StealthUpdate._framesGranted` (counter per tick) | no caller passes a duration > 0 (`ReceiveGrant(…, 0)`); a caller with a duration must pass ticks |
| `Phase5Modules.cs` | `StealthUpdate.ConditionActive` (`LastFireTick == world.Tick`) | comparison "in the same tick", needs no marking (convention case e) |
| `Phase5Modules.cs` | `ProductionUpdate.Update` (`RemainingTicks - 1` per tick) | tick counter over a value of the caller in ticks |
| `PhysicsAndMinefieldModules.cs` | `GenerateMinefieldBehavior.Update` (polling per tick) | load only |
| `ProductionExitModules.cs` | `QueueProductionExitUpdate` (`_delay = ExitDelayTicks`, `_delay--`) | counter with period exactly N (free as soon as 0), catalog value in ticks |
| `ProjectileModules.cs` | `DefaultMaxLifespanFrames`, `FlightPathAdjustDistPerSecond`, `InitialVelocity`, `KillSelfDelay` (fallback 3 frames) | stage 1 |
| `RailroadModules.cs` | `WaitAtStationTime` (fallback 150 frames), `SpeedMax`/`KillSpeedMin`/`RunningGarrisonSpeedMax` | stage 1 |
| `RepairModules.cs` | `RepairDockData.Parse` (`TimeForFullHeal`, unrounded) | stage 1 (`DurationMillisecondsToFixedTicks`) |
| `RetailMatchModules.cs` | `OverchargeBehavior.Update`, `RebuildHoleBehavior.Update` (`… / TicksPerSecondFixed`) | stage 1 |
| `RetailMatchModules.cs` | `RebuildHoleBehavior._workerWait` (`--` per tick down to 0) | catalog value in ticks, period exactly N |
| `RetailMatchModules.cs` | `RadarUpdate.IsExtendComplete` (`tick > _extendDoneTick`) | comparison without a literal (order) |
| `SlaveModules.cs` | `UpdateRateFrames`, `RecentFireFrames`, `DoRepairLogic` (`RepairRatePerSecond / TicksPerSecondFixed`) | stage 1 |
| `SlaveModules.cs` | `SlavedUpdate.Update` (`_framesToWait--`, acting at 0 in the same update) | period exactly N, no N + 1 |
| `SlaveModules.cs` | `MobMemberData.CatchUpCrisisBailTime` (D) | number of **checks**, not of frames; with a scaled `CheckIntervalFrames` the same number of checks falls into the same game time – stays unscaled |
| `SlaveModules.cs` | `SpawnPointProductionExitUpdate.Revalidate` (`world.Tick > tick`) | comparison without a literal (order) |
| `SpawnModules.cs` | `SpawnBehavior.UpdateRateFrames`, `_workTick`, sleep return value | stage 1; the polling of dead spawns thereby lies on the frame grid |
| `SpecialAbilityStaging.cs` | `StageTicksLeft` (F/tick), `_prepFrames`/`_animFrames` (`--` per tick) | counters over catalog values in ticks; the presentation reads ticks (Phase 21) |
| `SpecialPowerModules.cs` | `AvailableOnFrame` (F/tick), `BuildCompleteModule` (`Tick + SleepNone`) | absolute tick for the HUD (Phase 21); order via `SleepNone` |
| `SpecialsModules.cs` | `GrantStealthData.RadiusGrowRate` | stage 1 |
| `StructureModules.cs` | `MinBurstDelay` (fallback 9999 frames) | stage 1 |
| `StructureModules.cs` | `DeployStyleAIUpdate` (`_frameToWait = now + UnpackTicks …`, `= 0`) | absolute ticks from catalog values; 0 is the sentinel "no timer" |
| `SupplyModules.cs` | `SupplyCarrierAI.Update` (`Math.Min(baseSleep, SleepNone)`) | runs per tick as long as the state machine is working – load only; the action deadlines are catalog values |
| `TransportModules.cs` | `HelicopterSlowDeathData` (four values per second), `FixedTicks`, `SelfSpinUpdateAmount / TicksPerSecondFixed` | stage 1; the last expression is also correct at 60 Hz (`_selfSpin` is radians per tick: amount / 30 per frame = amount / rate per tick) |
| `TransportModules.cs` | `TunnelContainData` (`TimeForFullHeal` missing → 0) | fallback value 0, nothing to scale (side finding of the inventory: original 1 frame) |
| `VehicleModules.cs` | `BattleBusData.ThrowForce`, `AnimationSteeringUpdate.MinTransitionTicks` (F/tick) | stage 1 or catalog value for the presentation, respectively |
| `WorldEffectModules.cs` | `OCLLifetimeMaxCap` (fallback 1000 frames), `ScanDelayTime` (fallback 30 frames) | stage 1 |
| `WorldEffectModules.cs` | `DynamicShroudClearingRangeUpdate`: `Math.Max(1, GrowTime)`/`Math.Max(1, ShrinkTime)`, `_stateCountdown--` | protection against division by 0 (no time value); tick counter over catalog values |
| `WorldEffectModules.cs` | `OCLUpdate` (`_nextCreation == 0`), `HelicopterSlowDeathBehavior._hitGroundTick == 0` | sentinel values, no time |
| `UpgradeMux.cs`, `StructuresAndWorldModules.cs` | – | no time coupling |

### B.4 Scripts, mission, skirmish, scenarios

Already done in stage 1 and re-checked: `SET_TIMER`/`SET_RANDOM_TIMER` (random in frames, then scaled), `SecondsToFrames` (round up to frames, then scale – `SET_MILLISECOND_TIMER`, `SET_RANDOM_MSEC_TIMER`, `ADD_TO_MSEC_TIMER`, `SUB_FROM_MSEC_TIMER`, `SCRIPTING_OVERRIDE_HULK_LIFETIME`), `*_FOR_FRAMECOUNT`, `teamInitialIdleFrames`, first evaluation tick `NameHash % 61` frames, `DelayEvaluationSeconds`, `NAMED_SET/ADD_SPECIAL_POWER_COUNTDOWN`, button hunt `max(ScanTicks, eine Sekunde)`, `UpdatePendingStructures` (`tick % TicksPerSecond`), `SupplyScanSeconds`, `UpdateReadyTeam` (60 s, `AIPlayer.cpp`, `AIPlayer::checkReadyTeams`), `UNIT_EMPTIED` (`frame - TickOrder.Next`).

Remaining hits of the search patterns, each one of these cases: constants in frames (`…Frames = N`) and `SupplyScanSeconds`; scaled cadences and deadlines (`% _fireInterval`, `tick - x < Time.OriginalFrames(…)`); order (`TickOrder.Next`); run budgets and default values of the options (`108_000`, `216_000`, `1_000`, `300`, `2_700`, `4500`, `150`, `600`, `15`, `60` – units in §8.1); leaf cooldowns in `ObjectiveDriver*.cs` (literals 15 to 300 as the fourth argument of `Leaf`/`Destroy`: frames, scaled in `Round`); stress test (`…Steps`, timer delays, percentages 30/45, value ranges 20/30 – §8.3 or no time); distances, radii, quantities (`Fixed.FromInt(30/45/90/450/600/900/3600/…)`, `MaxAttackThreshold = 30`, ring distances 60 to 360, `GuardMargin = 150`, `MaxHealth = 300`: world units or unit counts); data of the fixture (`MinLifetime`/`MaxLifetime` in milliseconds, `BuildTime = 1` s: are converted at the catalog build); sentinels and state (`FramesToWait = -1`, `_hulkLifetimeOverrideTicks = -1`, `PresentationLatencyTicks = 0`, `CombatIntervalTicks = 3`); random (`LogicRandom.Range` in `SET_RANDOM_TIMER`/`SET_RANDOM_MSEC_TIMER`: drawn in frames or seconds, then scaled; in `Phase13.cs`: positions and angles).

Side findings (no rate errors, not corrected): `SUB_FROM_MSEC_TIMER` subtracts `ceil(+x)`, the original adds `ceil(−x)`; `SCRIPTING_OVERRIDE_HULK_LIFETIME` rounds up, the original truncates; `SUPPLY_SOURCE_ATTACKED` checks 10 s, the original adds `SCAN_RATE = 10` to frames (`AIPlayer.cpp`, `AIPlayer::isSupplySourceAttacked`).

## 14. Phase 21: presentation, game layer, UI, audio, client, tools and tests

State: 6 October 2026, final state of Phase 21 (`docs/handoffs/HANDOFF-PHASE21.md`) with the addendum of the same day (§14.9: tests in game time, `SaveGame` with rate, remaining time-scale tests, tool output). This section is the report for everything outside the simulation; it closes the list from §6. Goal in one sentence: nothing outside the simulation assumes that a tick lasts 1/30 s; at 30 Hz every frame, every sound and every checksum stays the same; at 60 Hz everything visible and audible lasts equally long in seconds.

**What Phase 21 is not:** an acceptance. Checksums prove reproducibility, the time-scale test proves durations. Whether anything looks or sounds right at 60 Hz, no human has seen or heard (§14.7), and the simulation itself is unassessed at 60 Hz (§5, Phase 23). The default stays 30 Hz.

### 14.1 Two concepts, two names, one clock

- **Original frame** (1/30 s): the unit in which the original authored client content. Outside the simulation there is exactly one constant for it, `CnCNew.Data.OriginalFrame.PerSecond` (`src/CnCNew.Data/OriginalFrame.cs`; `CnCNew.Data`, because `Presentation`, `Ui`, `Audio`, `Game` and the client all reference it). Every former 30 outside the simulation is either expressed through it (`FxValueReader.FramesPerSecond`, `RtsCamera.FrameRate`, `CameraSettings.FramesPerSecond`, `OrderHintBook.ClientFramesPerSecond`, `WindowTransitionHandler.FramesPerSecond`, `WaterAppearance.RiverVScrollPerSecond`, `InGameUiSettings`, `EvaCatalog.Frames`, the cabin damping in `ObjectLayerCore`) or dropped. `SimulationRate.OriginalFramesPerSecond` stays the constant of the simulation and is no longer used outside.
- **Simulation rate:** comes from the world (`world.Time`) or from the catalog (`GameData.Rate`). `GameData.Load(…, rate:)` takes it as a setting of the session; `GameData.DefaultSessionRate` (`SimulationRate.Original`) is the one site outside the simulation that names a rate (the default that Phase 23 switches). The client passes `--tick-rate N` through.
- **Clock of the presentation:** `CnCNew.Presentation.GameClock` (tick, rate, interpolation fraction), provided by `LiveGame.Clock`. It is kept in integers (tick and rate), never summed from frame times; `OriginalFrames` and `IsOriginalFrameBoundary` compute in integers, `Seconds` is `(Tick + Anteil) / Rate`. It stands still during pause and runs with the game speed because it hangs on the tick. Wall time stays the clock for shell, mouse pointer, camera, water, command markers.

Two patterns carry the whole rebuild, both the identity at 30 Hz:

1. **Durations in original frames, compared in ticks of the world:** `world.Time.OriginalFrames(NFrames)` at the use (muzzle flash, healing symbol, cheering, under-fire lock, placeholder durations, script fades, camera deadlines).
2. **What the original advances once per frame runs on the original-frame grid:** on the ticks that begin an original frame (`world.Time.IsOriginalFrameBoundary(tick)` or `GameClock.BeginsOriginalFrame` for clocks that lag the world by one tick or count frozen steps as well): particles and lights, the envelope of the flash, the running gear, texts and screen filters of the mission, the message list, the Eva announcer, the standing orders.

### 14.2 The proof at 30 Hz

Before any change all presentation checksums were generated twice at state `4c45764` (to see which are reproducible at all) and again after the rebuild. Generators: `PresentationTool` (Release) directly, `UiRenderTool`, the Stride client in the smoke test (`--smoke-test --capture`, `--audio-capture`, `--filmstrip`), pixel checksums like `scripts/capture-world.ps1` (SHA-256 over the decoded pixels).

| Checksum | reproducible (two runs before) | before | after | Result |
|---|---|---|---|---|
| `PresentationTool fx-audit` (whole output, in it the checksum of the execution run) | yes | `5504C152200DB149…` | `5504C152200DB149…` | same |
| `PresentationTool object-audit` (whole output) | yes | `032CF63DD4EC7BE7…` | `032CF63DD4EC7BE7…` | same |
| `PresentationTool terrain-audit` (whole output without the time line) | yes | `DC60C6DD9447198E…` | `DC60C6DD9447198E…` | same |
| `scenario-audit --name capture-building --every 30` (whole output) | yes | `BC7DD2A2F453D2EC…` | `BC7DD2A2F453D2EC…` | same |
| `scenario-audit --name tank-duel --every 10` (whole output) | yes | `8FD85E76B599D514…` | `8FD85E76B599D514…` | same |
| `scenario-audit --name jet-strafe --every 30` (whole output) | yes | `67DE0DFB83346317…` | `67DE0DFB83346317…` | same |
| `sync-perf --map "Maps/Alpine Assault/Alpine Assault.map" --players 2 --ticks 4000 --hud --digest` (digest over every frame, fog, effects, audio cues) | yes | `68CF838B1D93F711…` | `68CF838B1D93F711…` | same |
| `UiRenderTool`: 162 PNG (file and pixel checksum), `manifest.txt`, `settings.json` (164 files) | 164 of 164 | – | – | 164 of 164 same |
| Client `--capture`: `hud-tooltip` (pixels) | yes | `3D3AE9D8D938479A…` | `3D3AE9D8D938479A…` | same |
| Client `--capture`: `match-base-late` (pixels) | yes | `A8998097CF690823…` | `A8998097CF690823…` | same |
| Client `--capture`: `match-combat` (pixels) | NO | `DDEFB9DC63C5DADE…` | `4A44682DF6956FC5…` | different (not reproducible) |
| Client `--capture`: `match-cursor-move` (pixels) | yes | `2D22C665A8DA246A…` | `2D22C665A8DA246A…` | same |
| Client `--capture`: `match-fog-unexplored` (pixels) | yes | `C1BB7E7FE3DEAF87…` | `C1BB7E7FE3DEAF87…` | same |
| Client `--capture`: `match-selected` (pixels) | yes | `7D298221AA816C7C…` | `7D298221AA816C7C…` | same |
| Client `--capture`: `match-start` (pixels) | yes | `6567166959F06212…` | `6567166959F06212…` | same |
| Client `--capture`: `match-waypoints` (pixels) | yes | `E45FAC79518BE708…` | `E45FAC79518BE708…` | same |
| Client `--capture`: `mission-intro-45` (pixels) | yes | `99FCC5CE4E88C394…` | `99FCC5CE4E88C394…` | same |
| Client `--capture`: `mission-start` (pixels) | yes | `E2D718FE99350D30…` | `E2D718FE99350D30…` | same |
| Client `--capture`: `mission-usa01-300` (pixels) | yes | `11B8829E8754096C…` | `11B8829E8754096C…` | same |
| Client `--capture`: `scenario-capture` (pixels) | yes | `20AC3FA25DACB82C…` | `20AC3FA25DACB82C…` | same |
| Client `--capture`: `scenario-capture-1000` (pixels) | yes | `620006FAC1AED38D…` | `620006FAC1AED38D…` | same |
| Client `--capture`: `scenario-jet` (pixels) | yes | `BDF6C7DE55446DCE…` | `BDF6C7DE55446DCE…` | same |
| Client `--capture`: `scenario-tank-duel` (pixels) | yes | `441E02FE3C062544…` | `441E02FE3C062544…` | same |
| Client `--capture`: `scenario-tank-duel-90` (pixels) | yes | `37C25571CF734340…` | `37C25571CF734340…` | same |
| Client `--capture`: `viewer-crusader` (pixels) | yes | `BBFFF06CC89FD548…` | `BBFFF06CC89FD548…` | same |
| Client `--audio-capture`: `audio-capture-building/audio.wav` | yes | `7DB0DF9B6C46B48F…` | `7DB0DF9B6C46B48F…` | same |
| Client `--audio-capture`: `audio-match-autoplay/audio.wav` | yes | `775A7EE122429A32…` | `775A7EE122429A32…` | same |
| Client `--audio-capture`: `audio-mission/audio.wav` | yes | `9B5098CDC8A0BDF7…` | `9B5098CDC8A0BDF7…` | same |
| Client `--audio-capture`: `audio-tank-duel/audio.wav` | yes | `B9C6F66CF73D4BF5…` | `B9C6F66CF73D4BF5…` | same |
| Client `--filmstrip`: capture-building, tank-duel, jet-strafe, pixel checksums of the frames (77 files) | 77 of 77 | – | – | 77 of 77 same |

In addition the checked-in yardsticks that `tests/CnCNew.Game.Tests` (Release, with `--content-dir`) checks and that pass unchanged: the 54 screen hashes (`baselines/screens.txt`, `ShellTests`) and the HUD drawlist hash (`baselines/hud-drawlist-1280x720.sha256`, `HudTests`). The capture scenes are those of `scripts/capture-world.ps1` (selection without the pure road scenes and three model viewer scenes, plus longer run times of the staged demos, two further mission frames and the tooltip scene of `scripts/capture-text.ps1`); the frame sequences are those of `scripts/capture-scenarios.ps1`; the audio recordings: skirmish with `--autoplay` (3,000 frames), `capture-building` (1,500), `tank-duel` (300), mission `GC_ChemGeneral` (900).

Not suitable as proof: `match-combat` (autoplay, 7,500 frames) – three runs, three pixel checksums, already at the starting state (as `scripts/capture-world.ps1` documents for the autoplay scenes). The line "build time total (not deterministic)" of `terrain-audit` and the setup time of `sync-perf` are taken out of the comparison.

Simulation: `SimTool input-manifest check` identical; `SimTool reference-manifest check --set quick` (9 runs) identical after every work package, `--set full` at the end (50 runs identical). In `src/CnCNew.Simulation` exactly one site is changed: the alias `SimulationWorld.VisionIntervalTicks` is removed (§6 no. 1).

### 14.3 The list from §6, item by item

| No. | State | What was done |
|---|---|---|
| 1 | done | `LiveGame`, `SimTool perf`, `ObjectTests`, `Tests.Vision` use `world.VisionInterval`; `SimulationWorld.VisionIntervalTicks` is removed |
| 2 | done | `GameData.Load(…, rate:)`; `GameData.Rate`; `DefaultSessionRate` as the named default; client `--tick-rate N`, `PresentationTool scenario-audit`/`sync-perf --tick-rate N`, `GameShell.CreateLoading(…, rate:)` |
| 3 | done (addendum) | `SaveGame` **version 2** writes `tickRate` into the envelope and checks it before the fingerprint; version 1 is rejected through the version check and never reinterpreted; loading screen and pause menu show game time (`SaveGameHeader.GameTime`, m:ss) instead of the tick (§14.9.3) |
| 4 | done | each of the 14 + 1 + 3 uses decided: simulation ticks via `world.Time` (`LiveGame`, `SuperweaponTimers`, `MissionPresentation`, `SyncPerfCommand`) or original frame (`EvaCatalog`, `LiveMission`, `SelectionFlash`); no use of `SimulationRate.OriginalFramesPerSecond` any more in `CnCNew.Game`, `CnCNew.Presentation`, `CnCNew.Ui`, `CnCNew.Client.Stride` (source test, §14.6) |
| 5 | checked, no reader | nobody outside the simulation reads the sabotage duration in the event value (search for the three event kinds in `CnCNew.Game` and `CnCNew.Presentation`: no hit on the value); whoever reads it in future computes with `world.Time` |
| 6 | done | `UnitConditionTracker`: `Life < world.Time.OriginalFrames(100)`, fall threshold per original frame |
| 7 | done | `MissionPresentationDirector.CounterForDisplay` converts timer values into original frames; `MissionTextOverlay.NamedTimers` and `SuperweaponTimers` divide by `OriginalFrame.PerSecond` or use `ToWholeSeconds` |
| 8 | done | "presentation latency N original frames (S s)"; "world tick T at R Hz" |
| 9 | done | frame counts of the scripts are original frames and are converted on acceptance (`docs/SCRIPT-ENGINE.md` §7a, table) |
| 10 | done | `LiveMission.ClientPresentationLatencyFrames` (renamed; the value is and was in frames) |
| 11 | checked | door clock, steering hold time, construction progress and readiness of the special powers are compared as ticks against ticks (fractions, no seconds); the only display of seconds (`SuperweaponTimers`) computes via `world.Time` |
| 12 | done (addendum) | `CampaignTool drive`/`audit` write the game time of the ticks of a map line into a line of its own below it and the budget as game time; `playthrough` in parentheses after the tick; the parsed lines are byte-identical (§14.9.4). No tool prints `SkirmishAiStats.AttackTicks` |
| 13 | done | `ScenarioAudit` (one sample per original frame, event ticks and turret times converted on reading, `--tick-rate`), `SyncPerfCommand` (step from the rate of the world), `UiRenderTool` (steps `1.0 / OriginalFrame.PerSecond`, budgets `WholeSecondsToTicks`, timer value in original frames) |
| 14 | done (steps) | no `1.0 / 30` any more in the tests outside the simulation; `tests/Shared/TestTime.cs` (§14.5) |
| 15 | unchanged | `SimTool perf`: `catchup30`/`catchup60`/`core30_percent` name their rate in the name (switch of the default, Phase 23) |
| 16 | done | `StandingOrders.Update` and the commands of `LiveScenario` run per original frame; at 30 Hz the same commands (scene audits, `ScenarioTests`, `sync-perf` digest unchanged) |

Beyond the list (from `docs/TICKRATE-AUDIT.md` §6 and §7.1): the catch-up limit of `LiveGame` as a time budget (8 original frames), the effect clock on the original-frame grid, the throttling of the damage effects on one clock, muzzle flash/firing state/healing symbol/cheering/flash/running gear (`docs/PRESENTATION-OBJECTS.md` §11), Eva and flood protection (`docs/GAME-AUDIO.md`), message list and superweapon list (`docs/GAME-HUD.md` §19), the mission presentation (`docs/SCRIPT-ENGINE.md` §7a), `DemoScenario.Frames`/`ScenarioOrder.Frame` (demos in original frames), the smoke-test step of the client (one frame = one original frame at every rate) and the scripts `capture-world.ps1`, `capture-scenarios.ps1`, `audit-scenarios.ps1` (`-TickRate`).

**Three deliberate deviations from the wording of the brief:**

1. *Running gear:* not "computed with the step width", but stepped on the original-frame grid; justification and measurement in `docs/PRESENTATION-OBJECTS.md` §11.
2. *Letterbox fade:* not on wall time, but on the presentation clock in original frames; justification in `docs/SCRIPT-ENGINE.md` §7a.
3. *Smoke-test step:* not "one tick", but one original frame (1/30 s) per drawn frame – the same at 30 Hz, two ticks at 60 Hz. That way `--frames`, `--every`, input scripts and all capture scripts mean the same game time at every rate, and captures at 30 and 60 Hz show the same point in time.

### 14.4 Time-scale test at 60 Hz (and 120 Hz)

Per element the same scene per rate, duration measured in ticks of the respective world and converted into seconds with its rate; tolerance one original frame (0.0333 s). `ObjectTests.TimeScale.cs` (synthetic fixture per rate), `TimeScaleTests.cs` and `AudioDirectorTests` (synthetic mission or fixture per rate), `TimeScaleTests` with `--content-dir` (real data, `LiveScenario` at 30, 60 and 120 Hz); in the addendum also `FxTests.TimeScale.cs`, `UnitConditionTests.TimeScale.cs`, one test each in `JetOrderTests` and `CaptureFlashTests` (all `Presentation.Tests`) and four more in `TimeScaleTests` (`Game.Tests`).

| Element | 30 Hz | 60 Hz | 120 Hz |
|---|---|---|---|
| Muzzle flash after one shot | 0.1333 s | 0.1333 s | 0.1333 s |
| `FIRING_A` after one shot | 0.0333 s | 0.0333 s | 0.0333 s |
| Healing symbol after the last healing | 3.0333 s | 3.0167 s | 3.0083 s |
| Script flash of 2 s: number of flashes | 4 | 4 | 4 |
| Script flash: end of the last flash after the request | 1.6667 s | 1.6667 s | 1.6667 s |
| one flash from the peak to zero (envelope, also that of the capture) | 0.1667 s | 0.1667 s | 0.1667 s |
| Running gear: settling onto a slope (1 % of the rest position) | 1.2667 s | 1.2667 s | 1.2833 s |
| Camera move of 5 s (director and `CAMERA_MOVEMENT_FINISHED`) | 5.0333 s | 5.0167 s | 5.0083 s |
| Military subtitle: first character | 0.7667 s | 0.7667 s | 0.7667 s |
| Military subtitle: all nine characters | 2.0667 s | 2.0667 s | 2.0667 s |
| Military subtitle of 5000 ms: gone | 7.5333 s | 7.5333 s | 7.5333 s |
| Message line until faded out | 9.2333 s | 9.2333 s | 9.2333 s |
| Cinematic text of 4 s | 4.0000 s | 4.0000 s | 4.0000 s |
| Speech placeholder | 3.0333 s | 3.0167 s | 3.0083 s |
| Script fade 30/30/30 frames: black / over | 1.0000 / 3.0000 s | 1.0000 / 3.0000 s | 1.0000 / 3.0000 s |
| Letterbox fade until full | 1.0333 s | 1.0167 s | 1.0083 s |
| Greyscale ramp of 30 frames | 1.0000 s | 1.0000 s | 1.0000 s |
| Radar event | 8.0333 s | 8.0167 s | 8.0083 s |
| Eva: spacing of two announcements of the same event | 30.0000 s | 30.0000 s | 30.0000 s |
| Particle system from creation to the last particle (real data, `AirF_AuroraBombExplosion`, 30 particles at the maximum per rate) | 11.4000 s | 11.3833 s | 11.3917 s |
| *Addendum:* under-fire cue (`SoundCueDeriver`, `Radar::tryUnderAttackEvent`): time until the next one for the same place | 10.0000 s | 10.0000 s | 10.0000 s |
| Under-fire line of the message list (`HudMessageFeed`): time until the next one | 10.0000 s | 10.0000 s | 10.0000 s |
| Damage effect: time until the next one of the same damage type (`ThrottleTime` 1000 ms, `FxExecutor`) | 1.0000 s | 1.0000 s | 1.0000 s |
| Cheering (`SPECIAL_CHEERING` after `ALL_CHEER`) | 2.9667 s | 2.9833 s | 2.9917 s |
| Superweapon line (`ReloadTime` 10000 ms): at most five seconds left / ready | 4.0333 / 10.0000 s | 4.0167 / 10.0000 s | 4.0083 / 10.0000 s |
| Superweapon line: first white flash of the ready line (`SuperweaponCountdownFlashDuration` 500 ms) | 10.5000 s | 10.5000 s | 10.5000 s |
| Named counter of 5 s (`DISPLAY_COUNTDOWN_TIMER`): shows 0:03 / 0:00, measured from the step that sets it | 1.0333 / 4.0333 s | 1.0167 / 4.0167 s | 1.0083 / 4.0083 s |
| Named counter: first flash colour after expiry (`NamedTimerCountdownFlashDuration` 500 ms) | 4.4667 s | 4.4833 s | 4.4917 s |
| Zoom blur (`CAMERA_MOTION_BLUR_JUMP`): peak (60) / over | 0.4000 / 0.8000 s | 0.4000 / 0.8000 s | 0.4000 / 0.8000 s |
| Standing order (jet attacking): spacing of the fire commands of one approach (`DelayBetweenShots` 300 ms), five commands per approach | 0.3000 s | 0.3000 s | 0.3000 s |
| Capture: duration for which the last flash of the sequence is shown | 0.1667 s | 0.1667 s | 0.1667 s |

The differences of 1/60 or 1/120 s in some rows are the step in which the expiry is noticed (one tick of the respective rate), not a scaling error. In addition: `GameClock` against the time service of the simulation for the ticks −3 to 499 at all three rates; the flood protection of the audio cues at two ticks per original frame (`FxTests.RateLimiter`).

Runs at 60 Hz over the whole chain (no time-scale tests, only "runs through" and the checks of the scenes): `scripts/audit-scenarios.ps1 -TickRate 60` passes all three scenes (for that `scenario-audit` takes its samples on frame boundaries and converts the two to three ticks of step offset of the reset check into frames; at 30 Hz its output is byte-identical); the client runs through in the smoke test with `--tick-rate 60` (`--match "Maps/Alpine Assault/Alpine Assault.map" --autoplay --frames 600` and `--mission "Maps/GC_ChemGeneral/GC_ChemGeneral.map" --frames 600`: 600 frames, world tick 1,200, 1,200 steps of the script VM, 0 hits on missing types; at 30 Hz world tick 600).

The superweapon line and the named counter moreover read the same text on every whole second at all three rates (compared in the test). At every rate the standing orders issue commands only on ticks that begin an original frame, and at most one fire command per original frame.

**Measured and not equal – the cause lies in the simulation, not in the presentation** (the tests measure and print these values but do not require equality; all three sites are in §5):

| Element | 30 Hz | 60 Hz | 120 Hz | Cause |
|---|---|---|---|---|
| Flash sequence of a capture (preparation 2000 ms): number of flashes | 4 | 9 | 19 | `SpecialAbilityUpdate.StepCaptureFlash` advances the flash phase per tick (§5.1); what is verified is that the barracks is tinted only within the envelope of an event and that every flash lasts equally long — **Phase 23:** fixed, **4 / 4 / 4** (`d923cea`, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`) |
| Flash sequence: first flash to the end of the last tint | 1.0333 s | 1.4167 s | 1.6583 s | the same — **Phase 23:** 1.0333 / 1.0500 / 1.0417 s (output of the time-scale test at state `e1252df`; the remainder below one frame is the tick in which the end is noticed) |
| `FREEFALL` of an avalanche rock: shown for | 2.3667 s | 1.3333 s | 0.6917 s | the slide physics of `TensileFormationUpdate` computes per tick (§5.1, §5.2): the last free fall lies in the 85th tick of the slide at every rate (2.8333 / 1.4167 / 0.7083 s). What is verified is the window of the presentation: never from the hundredth original frame on — **Phase 23:** the cause in the simulation is fixed (slide physics once per frame, `302e88a`, `docs/rate-invariance/RATE-INVARIANCE-PHYSICS.md`: last frame with sinking 186 at every rate); the value of this row is not recorded in any log after that – **not re-measured** |
| Standing order: first fire command after the order | 2.0333 s | 2.1000 s | 2.1333 s | the approach of the jet (flight model of the simulation, §5.2); the cadence of the commands themselves is the same (table above) — **Phase 23:** the cause is fixed (flight model per frame; in the suite the first shot of the Raptor attack falls after 18.6333 s at every rate, `346cadc`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md`); the value of this row is **not re-measured** |

**Not checkable headless:** whether any of this looks or sounds right (§14.7). The zoom blur is verified as a state of the director (`MissionPostEffects.MotionBlurCount`), not as a drawn frame.

### 14.5 Tests: steps, times, list

`tests/Shared/TestTime.cs` (included in `Game.Tests` and `Presentation.Tests`): `FrameSeconds`/`FrameSecondsF` (one original frame of wall time – the step of a display with 30 frames per second and of everything without a world), `TickSeconds(world)` (exactly one tick of this world), `Ticks(world, sekunden)` and `Frames(world, originalbilder)` (game time in ticks of the test world), `FramesPerSecond`. No test outside the simulation writes `1.0 / 30` or `1f / 30` any more (previously 95 sites).

**Simulation tests (addendum).** `tests/CnCNew.Simulation.Tests/TestTime.cs`: `world.Frames(n)` (original frames in ticks of the test world; also on `MissionSession` and `SkirmishMatchSession`), `world.InFrames(ticks)` (a measured tick count on the grid of the original, rounded down – for expectations of the form `is >= 44 and <= 47`, which need constants), `world.PerFrame(wert)` (a speed per original frame as a value per tick), `TestRate.Frames(n)`/`TestRate.InFrames`/`TestRate.Time` for catalog expectations without a world. At 30 Hz every helper is the identity: every expectation keeps its numeric value. State: 629 sites `….Frames(…)` on a world or session, 36 `TestRate.Frames`, 20 `InFrames`, 4 `PerFrame`; eight helper functions count their budgets in original frames themselves instead of every call being converted (`StepUntil` in `Tests.Air.cs` and `Tests.Construction.cs`, `CsRun`, `StepN`, `StructuresWorld.Run`, `NavRun`, `RunUntilContained`, `ObserveCapture`, `TicksUntilShot`). The standard test worlds and catalogs take their rate from `TestRate.Default` (235 sites, 30 Hz; `--tick-rate N` or `CNCNEW_TEST_TICK_RATE=N` builds them with 60 or 120 Hz, §14.9.2); tests about a particular rate (`Tests.TickRate*.cs`, the conversion tables in `Tests.Basics.cs`, the rejection of a foreign rate in `Tests.State.cs`) still name that rate themselves.

**Rule.** A number that means time – a duration from the data, a deadline, a budget "run until X has happened" – is written in original frames and goes through a helper. A number that really means ticks stays bare and is named below per file with its reason: order in the scheduler and "one step later" (`Tick − 1`, `+ 1`, `Run(1)`), steps of the script VM, command ticks and delays of `SetTimerCommand` (§7), run budgets and checkpoints of recorded replays and of the stress run (§8.1, §8.3), trajectories computed by hand per step, sampling cadences, sentinel values.

State per test file (all `*.cs` under `tests/`, without `bin`/`obj`): **142 files – converted 46, mixed 33, deliberately in ticks 7, not affected 49, new 7, open 0.** "mixed" means: the time and budget lines of the file are converted, and the note names what in it deliberately stays in ticks (or in original frames of a clock without a world). Of the 57 files that were "open" at the end of Phase 21, 27 are now converted, 27 mixed, 2 deliberately in ticks and 1 not affected. The numbers of the "time and budget lines of the inventory" in the notes of the files already converted in Phase 21 come from `docs/TICKRATE-AUDIT.md` §7.2 and can be looked up there.

| File | State | Note |
|---|---|---|
| `CnCNew.Archives.Tests/Program.cs` | not affected |  |
| `CnCNew.Audio.Tests/Program.cs` | not affected |  |
| `CnCNew.Audio.Tests/Tests.Catalog.cs` | not affected |  |
| `CnCNew.Audio.Tests/Tests.Decoding.cs` | not affected |  |
| `CnCNew.Audio.Tests/Tests.Mixer.cs` | not affected |  |
| `CnCNew.Audio.Tests/Tests.RealData.cs` | not affected |  |
| `CnCNew.Content.Tests/Program.cs` | not affected |  |
| `CnCNew.Data.Tests/Program.cs` | not affected |  |
| `CnCNew.Formats.Tests/MapDecoderTests.cs` | not affected |  |
| `CnCNew.Formats.Tests/Program.cs` | not affected |  |
| `CnCNew.Game.Tests/AudioDirectorTests.cs` | converted | 13 steps via `TestTime`; 6 time lines and 2 budget lines of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Game.Tests/CampaignShellTests.cs` | converted | run lengths via `StepUntilOver` and the chain-hash loop in original frames (`TestTime.Frames`) |
| `CnCNew.Game.Tests/CampaignTests.cs` | converted | run lengths (`Step`), header tick and sample via `TestTime.Frames`; `presentationLatencyTicks: 90` is, per §8.1, a value in original frames and stays; new: rate checks of the savegame envelope (§14.9) |
| `CnCNew.Game.Tests/DeterminismTests.cs` | not affected |  |
| `CnCNew.Game.Tests/HudTests.cs` | converted | 1 step via `TestTime`; 22 time lines and 5 budget lines of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Game.Tests/LiveMatchTests.cs` | converted | 1 step via `TestTime`; 1 time line and 0 budget lines of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Game.Tests/MissionEffectsTests.cs` | converted | 16 steps via `TestTime`; 11 time lines and 0 budget lines of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Game.Tests/MissionHudTests.cs` | converted | 21 steps via `TestTime`; 1 time line and 0 budget lines of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Game.Tests/MissionPresentationTests.cs` | converted | call sites and durations via the named constants |
| `CnCNew.Game.Tests/MissionTextTests.cs` | converted | 12 steps via `TestTime`; 10 time lines and 1 budget line of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Game.Tests/ModelGeometryTests.cs` | not affected |  |
| `CnCNew.Game.Tests/Program.cs` | converted | only call sites of changed interfaces |
| `CnCNew.Game.Tests/ScenarioTests.cs` | converted | 1 step via `TestTime`; 0 time lines and 2 budget lines of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Game.Tests/ShellTests.cs` | converted | 2 steps via `TestTime`; 0 time lines and 1 budget line of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Game.Tests/ShellTransitionTests.cs` | converted | call sites and durations via the named constants; 0 time lines and 1 budget line of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Game.Tests/SourceRuleTests.cs` | new | Phase 21 (time helpers, time-scale test or source test) |
| `CnCNew.Game.Tests/TestHarness.cs` | not affected |  |
| `CnCNew.Game.Tests/TextFixtures.cs` | not affected |  |
| `CnCNew.Game.Tests/TimeScaleTests.cs` | new | Phase 21 (time-scale test); extended in the addendum by the under-fire line, the superweapon list, the named counter, zoom blur and 120 Hz for the particle system |
| `CnCNew.IO.Tests/Program.cs` | not affected |  |
| `CnCNew.Images.Tests/CursorFixtures.cs` | not affected |  |
| `CnCNew.Images.Tests/CursorReaderTests.cs` | not affected |  |
| `CnCNew.Images.Tests/Program.cs` | not affected |  |
| `CnCNew.Import.Tests/Program.cs` | not affected |  |
| `CnCNew.Presentation.Tests/CameraTests.cs` | converted | 16 steps via `TestTime`; 2 time lines and 0 budget lines of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Presentation.Tests/CaptureConditionTests.cs` | converted | run budget and the stage lengths 15/30/9 frames via `TestTime.Frames` |
| `CnCNew.Presentation.Tests/CaptureFlashTests.cs` | mixed | converted: run budget, new time-scale test of the flash sequence. Deliberately not in ticks of the world but in original frames of the presentation clock: envelope (`TintAt`) and script flash (frames 15/30/45/60). "Four flashes per preparation" is the cadence of the simulation at 30 Hz (the world names 30 Hz; §5.1 `StepCaptureFlash`) |
| `CnCNew.Presentation.Tests/CursorTests.cs` | converted | 8 steps via `TestTime`; 1 time line and 2 budget lines of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Presentation.Tests/FxTests.TimeScale.cs` | new | addendum to Phase 21: time scale of the under-fire lock and of the damage-effect throttle |
| `CnCNew.Presentation.Tests/FxTests.cs` | converted | call sites and durations via the named constants; 32 time lines and 9 budget lines of the inventory count steps of the effect clock (original frames, without a world); in the addendum only `partial` and one registration line |
| `CnCNew.Presentation.Tests/InputTests.cs` | converted | 4 steps via `TestTime`; 2 time lines and 3 budget lines of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Presentation.Tests/JetOrderTests.cs` | mixed | converted: both run budgets; new time-scale test of the standing orders. Deliberately in ticks: "at most 3 ticks after the last missile" (order); `SimulationTimingMin` names 30 Hz (raw-value tolerance) |
| `CnCNew.Presentation.Tests/MinimapTests.cs` | not affected |  |
| `CnCNew.Presentation.Tests/ObjectTests.TimeScale.cs` | new | Phase 21 (time helpers, time-scale test or source test) |
| `CnCNew.Presentation.Tests/ObjectTests.cs` | converted | call sites and durations via the named constants; 3 time lines and 3 budget lines of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Presentation.Tests/PlacementPreviewTests.cs` | not affected |  |
| `CnCNew.Presentation.Tests/Program.cs` | not affected |  |
| `CnCNew.Presentation.Tests/RoadRealDataTests.cs` | not affected |  |
| `CnCNew.Presentation.Tests/RoadTests.cs` | not affected |  |
| `CnCNew.Presentation.Tests/StructureConditionTests.cs` | mixed | converted: 23 run loops, 13 durations (`Near(TestTime.Frames(…), …)`), two windows. Deliberately in ticks: sample cadence `i % 7`, offsets of the sampling (`Tick + 1`, `+ 2`), tolerances of `Near`, "closing directly after the release", six steps of lead-in before the projection |
| `CnCNew.Presentation.Tests/SyntheticTerrain.cs` | not affected |  |
| `CnCNew.Presentation.Tests/TerrainTests.cs` | deliberately in ticks | 0 tick expectations (order, frame numbers of the shell transitions), 1 rate statements |
| `CnCNew.Presentation.Tests/TestHarness.cs` | not affected |  |
| `CnCNew.Presentation.Tests/UnitConditionTests.TimeScale.cs` | new | addendum to Phase 21: time scale of cheering and `FREEFALL` |
| `CnCNew.Presentation.Tests/UnitConditionTests.cs` | converted | call sites and durations via the named constants; 6 time lines and 19 budget lines of the inventory still count steps of the 30 Hz fixture (original frames), unless they use one of the renamed durations |
| `CnCNew.Presentation.Tests/VisionTests.cs` | converted | only call sites of changed interfaces |
| `CnCNew.Presentation.Tests/WorldRealDataTests.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Program.cs` | deliberately in ticks | test module `ProbeModule` keeps `nextWake` as an absolute tick (scheduler); in the addendum only the hint line for `--tick-rate` |
| `CnCNew.Simulation.Tests/TestTime.cs` | new | addendum to Phase 21: `TestRate` (rate of the standard test worlds, `--tick-rate N`) and `TestTime` (`world.Frames`, `world.InFrames`, `world.PerFrame`) |
| `CnCNew.Simulation.Tests/Tests.Acquisition.cs` | mixed | converted: run budgets, `MoodCheckTicks`, reload and hold deadlines, first look "two frames after idle". Deliberately in ticks: `step > 1`/`> 2` (order after a command), replay header `EndTick 300`/checkpoints 20 and the command ticks of the replay |
| `CnCNew.Simulation.Tests/Tests.Air.cs` | converted | all budgets of `StepUntil` (the helper counts original frames) and the run loops; `WeaponReadyTick >= SleepForever` is a sentinel value |
| `CnCNew.Simulation.Tests/Tests.AirModelGeometry.cs` | converted | budgets via `StepUntil` and loops; "two shots" is a count |
| `CnCNew.Simulation.Tests/Tests.AirReal.cs` | converted | budgets; EMP duration "more than 800 frames" |
| `CnCNew.Simulation.Tests/Tests.AirWeapons.cs` | mixed | converted: `StartFadeTime`, EMP and leaflet duration, silo door (`ready − 40/20`, `use + 25/70`), orbit settling time, budgets. Deliberately in ticks: `pulseTick = Tick − 1` and the tolerance ±1 (the step just executed), `Run(ready + 2)`, `Run(use + 3)` (steps after a command), four steps of the second run |
| `CnCNew.Simulation.Tests/Tests.Basics.cs` | deliberately in ticks | the conversion tables check the time service of the rate 30 Hz and name it (`SimulationRate.Original`); the catalog tests below them run at the test rate |
| `CnCNew.Simulation.Tests/Tests.BattlePlan.cs` | converted | all waiting times via `CsRun` (the helper counts original frames) |
| `CnCNew.Simulation.Tests/Tests.CleanupAndSpy.cs` | converted | capped duration 44–47 frames, on/off share over 400 frames, budgets, `CsRun` |
| `CnCNew.Simulation.Tests/Tests.Collision.cs` | mixed | converted: fall time of the tower (90–130 frames), sample at frame 60, budgets, `tick > 5`. Deliberately in ticks or Phase 23: the fixture factors `StructuralDecay` and others, which the simulation applies per tick (§5.1) |
| `CnCNew.Simulation.Tests/Tests.Combat.cs` | mixed | converted: pulse intervals 18–20/9/3/30 frames, lock window 28 frames, budgets. Deliberately in ticks: three steps after the command (`ReplaceObjectUpgrade`) |
| `CnCNew.Simulation.Tests/Tests.Construction.cs` | converted | `StepUntil` takes and returns original frames; construction work 60 frames; sale `Frames(180) − 1` (180 frames minus one step, like `TickRateWorldSaleTakesTheSameGameTime`); `tick == 0` is the first step |
| `CnCNew.Simulation.Tests/Tests.Crates.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Tests.CratesAndSpecials.cs` | converted | `CsRun` counts original frames (118 calls in five files); sabotage duration 59–61 frames, resumption after 110 frames, duration of a stealth grant |
| `CnCNew.Simulation.Tests/Tests.CreationLists.cs` | converted | lifetime 25–35 frames, budget |
| `CnCNew.Simulation.Tests/Tests.DamagedLocomotor.cs` | converted | travel time of 60 frames |
| `CnCNew.Simulation.Tests/Tests.Determinism.cs` | mixed | converted: four run loops. Deliberately in ticks: three steps each between commands (order), stress run (§8.3) |
| `CnCNew.Simulation.Tests/Tests.FiringTracker.cs` | converted | shot intervals 18/9/4–5 frames, coast times 30–36 and `Frames(30) + 1`, lead-in times; `Shots` measures on the frame grid (`InFrames`), the callback receives the step index |
| `CnCNew.Simulation.Tests/Tests.Flight.cs` | mixed | converted: all `Run(session, n)` from two steps upwards, budgets, landing, sample at frame 60. Deliberately in ticks: `Run(session, 1)` (one step of the script VM); `CombatIntervalTicks` is a switch (§8.1) |
| `CnCNew.Simulation.Tests/Tests.GoldenMaster.cs` | converted | the model computes shot delay 15 and reload 60 in frames; production interval `Frames(2 · 30)` |
| `CnCNew.Simulation.Tests/Tests.HashWork.cs` | mixed | converted: run loops. Deliberately in ticks: sampling cadence (`% 97`, `% 10`) and the stress run (§8.3); what is checked are hash equalities, not times |
| `CnCNew.Simulation.Tests/Tests.Indexes.cs` | not affected | lists against world passes; one run budget via `Frames`, otherwise round counters |
| `CnCNew.Simulation.Tests/Tests.JetFlight.cs` | mixed | converted: budgets, arrival "before frame 400", U-turn "at least 40 frames"; values per tick come from the time service of the test rate. Deliberately unchanged: raw-value tolerances (64 or 2 raw bits, Phase 22) |
| `CnCNew.Simulation.Tests/Tests.JetFlightReal.cs` | converted | budget 4200 frames, measurement from frame 20; catalog values per tick via the time service of the test rate |
| `CnCNew.Simulation.Tests/Tests.LastBlockers.cs` | converted | `CsRun`; sabotage duration in the event value, heal lock `hit + Frames(30)`, hash run 150 frames |
| `CnCNew.Simulation.Tests/Tests.MatchModules.cs` | mixed | converted: hash comparison loops. Deliberately in ticks: `Step(3)`, `Step(4)` and the loops with three/four steps (order after a command) |
| `CnCNew.Simulation.Tests/Tests.Navigation.cs` | converted | `NavRun` takes and returns original frames (15 calls); "detour longer than 75 frames" |
| `CnCNew.Simulation.Tests/Tests.NavigationEscape.cs` | converted | via `NavRun` (budget 900 frames) |
| `CnCNew.Simulation.Tests/Tests.PerfMovement.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Tests.PerfSummary.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Tests.Phase12Modules.cs` | mixed | converted: `Run(session, n)`. Deliberately in ticks: `CombatIntervalTicks = 0` is a switch (§8.1) |
| `CnCNew.Simulation.Tests/Tests.Phase13Actions.cs` | converted | all `Run(session, n)` in frames |
| `CnCNew.Simulation.Tests/Tests.Phase5.cs` | mixed | converted: every deadline of the form `while (Tick <= start + N)` (fire, poison, regeneration, delayed death, detection), budgets, "one frame" between two shots without shot delay. Deliberately in ticks: `xTick = Tick − 1` (the executed step), three steps after creation |
| `CnCNew.Simulation.Tests/Tests.PlayerAgent.cs` | converted | run budgets of the objective driver (`maxTicks: TestRate.Frames(…)`) |
| `CnCNew.Simulation.Tests/Tests.PowerPlant.cs` | mixed | converted: budgets (`StepUntil` in frames), 40 frames of overload. Deliberately in ticks: "after two steps health has been deducted" |
| `CnCNew.Simulation.Tests/Tests.Projectiles.cs` | mixed | converted: delayed hit without a projectile (landing `F + Frames(8)`), intercept laser, budgets. **Deliberately in ticks:** four trajectories computed by hand per step (path points of the grenade, arc, missile with and without ignition delay: x per tick) – they describe the 30 Hz world step by step and fail at 60 Hz (4 of the 6 failures, §14.9) |
| `CnCNew.Simulation.Tests/Tests.RealFlight.cs` | converted | budget 1800 frames, evacuation at frame 10 |
| `CnCNew.Simulation.Tests/Tests.RealMovement.cs` | converted | budget 1800 frames, standstill from frame 20 |
| `CnCNew.Simulation.Tests/Tests.ReferenceManifest.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Tests.ReferenceMatch.cs` | deliberately in ticks | run budget, `MinTicks`, `EndTick == 62_000` and checkpoints are the length of a recorded replay (§8.1: run budgets stay ticks); `GraceTicks` is already a rule value in frames there |
| `CnCNew.Simulation.Tests/Tests.Repair.cs` | converted | repair duration 8–10 frames, amount per tick from `Frames(9)`, `RunUntilContained` in frames, budgets |
| `CnCNew.Simulation.Tests/Tests.RetailModules.cs` | mixed | converted: production interval 30 frames, exit lock 88–92 frames, budgets (`SpawnTicks`), `Step(40)`/`Step(60)`. Deliberately in ticks: `Step(3)`, `Step(1)` and the loops with three/four steps (order after a command) |
| `CnCNew.Simulation.Tests/Tests.Scripting.cs` | mixed | converted: `Run(session, n)` from two steps upwards, timer value `Frames(60)`, run-on `EndGameDelayFrames + 5`, run budgets of the scenarios. Deliberately in ticks: `Run(session, 1)` (one step of the script VM) and `Run(session, 3)` before the count "per evaluation" |
| `CnCNew.Simulation.Tests/Tests.ScriptingActions12.cs` | mixed | converted: `Run(session, n)`, `HulkLifetimeOverrideTicks == Frames(60)`. Deliberately in ticks: `CombatIntervalTicks = 0` (switch) |
| `CnCNew.Simulation.Tests/Tests.ScriptingPhase11.cs` | mixed | converted: 40 `Run(session, n)`, budgets of the objective driver. Deliberately in ticks: `Run(session, 1)` (one VM step) |
| `CnCNew.Simulation.Tests/Tests.ScriptingPhase12.cs` | mixed | converted: budgets of the objective driver. Deliberately in ticks: "in each of the three ticks the declaration does not change the hash" |
| `CnCNew.Simulation.Tests/Tests.Skirmish.cs` | mixed | converted: "60 frames are 2 s". Deliberately in ticks: run budget and checkpoints of the recorded match (`SkirmishRecord(12_000)`, every 500 ticks; §8.1) |
| `CnCNew.Simulation.Tests/Tests.Slaves.cs` | converted | `StepN` counts original frames (48 calls); firing cadence every 12 frames, budget |
| `CnCNew.Simulation.Tests/Tests.SourceRules.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Tests.SpecialPower.cs` | mixed | converted: reload times (`ReloadTicks == Frames(300)`/`Frames(30)`), waiting times via `StepTo(Tick + Frames(…))`, sample at frame 320. Deliberately in ticks: `StepTo(Tick + 3)` and three steps after a purchase (order), `reload − 1`, replay header of a file that is only written and read |
| `CnCNew.Simulation.Tests/Tests.StagedCapture.cs` | mixed | converted: stage lengths (`Frames(StagedUnpack)` …, real data 90/600/60 and 202/180/84 frames), `ObserveCapture` and `StagedWorldAt` in frames. **Deliberately in ticks:** "four flashes per preparation" – `StepCaptureFlash` grows per tick (§5.1) and yields nine at 60 Hz (1 of the 6 failures); sample after 20 preparation steps |
| `CnCNew.Simulation.Tests/Tests.State.cs` | deliberately in ticks | the stress run counts steps (§8.3): run lengths and stop ticks 3000/1500/600/1234/216,000, `"tick":600`, command ticks 9 and 12; the rejection tests name 30 Hz as the rate of their catalog. Only four hash comparison loops are converted |
| `CnCNew.Simulation.Tests/Tests.StructuresAndWorld.cs` | converted | `StructuresWorld.Run` counts original frames (76 calls); speeds per frame via `PerFrame` (train, scaffold), budgets |
| `CnCNew.Simulation.Tests/Tests.Supply.cs` | mixed | converted: `RunCollecting` bounds (`Frames(30) + 1` …), trip interval, replacement deadline 70/140 frames, budgets. Deliberately in ticks: samples `i == 8 || i == 25`, four steps, replay header |
| `CnCNew.Simulation.Tests/Tests.Teams.cs` | converted | hash comparison over 30 frames, budget 200 frames; the replay header takes the test rate |
| `CnCNew.Simulation.Tests/Tests.Terrain.cs` | deliberately in ticks | a replay of ten ticks (`TicksExecuted == 10`); the header takes the test rate |
| `CnCNew.Simulation.Tests/Tests.TickPhases.cs` | not affected | phase observer over the stress run (§8.3); one run budget via `Frames` |
| `CnCNew.Simulation.Tests/Tests.TickRate.Grid.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Tests.TickRate.ModulesAM.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Tests.TickRate.ModulesNZ.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Tests.TickRate.Scripting.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Tests.TickRate.World.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Tests.TickRate.cs` | not affected | names its rates; in the addendum three calls with an explicit rate (`ShortReplay(rate: Hz30)`, `MissionMap(…, rate: Hz30)`) |
| `CnCNew.Simulation.Tests/Tests.TimeFreeze.cs` | mixed | converted: timer duration 28–40 frames, budgets. Deliberately in ticks: `frozenAt + 1`/`+ 2`/`tick + 1` (one step further after the thaw) |
| `CnCNew.Simulation.Tests/Tests.Transports.cs` | mixed | converted: budgets, heal amount per tick from `Frames(30)`, climb time, crash and explosion as "one step, then N − 1 frames" (`death + 1 + Frames(74)`), samples. Deliberately in ticks: `gone = Tick − 1`, replay header of a file that is only written |
| `CnCNew.Simulation.Tests/Tests.TurretAttack.cs` | mixed | converted: budgets, hold time `1 + Frames(15)`, giving up after 44–47 frames, sampling pause, command plan of the determinism run (frames 3, 60–90, 150, 260), real data. Deliberately in ticks: commands of the replay (ticks 70–110, header 200/20), `tick % 2` (command cadence), `step > …` orders |
| `CnCNew.Simulation.Tests/Tests.Turrets.cs` | mixed | converted: waiting time until the shot (`Frames(15)`, 8–10, 3–4, ≥ 30 frames), `RecenterTicks`, fallback values per frame, budgets. **Deliberately unchanged:** the raw value `TurnRate.Raw is > 6860 and < 6866` (30 Hz in Q47.16; 1 of the 6 failures, the line belongs to Phase 22); four commands in four steps |
| `CnCNew.Simulation.Tests/Tests.Veterancy.cs` | converted | the model computes shot delay and reload per level in frames; budgets |
| `CnCNew.Simulation.Tests/Tests.Vision.cs` | mixed | converted: hash comparison loops, "one frame" until the weapon is ready again. Deliberately in ticks: `world.VisionInterval` steps until the refresh (already a value of the world) |
| `CnCNew.Simulation.Tests/Tests.VisionEquivalence.cs` | not affected |  |
| `CnCNew.Simulation.Tests/Tests.WeaponSlots.cs` | mixed | converted: ready ticks per slot (`start + Frames(15)`, `start + 1 + Frames(3)`, `Frames(15 + 60)` …), budgets, command cadence `% Frames(40)`. Deliberately in ticks: `LastFireTick == start + 1` (one step later), commands of the replay (ticks 2–150, 60, 100; header 160/20), format versions |
| `CnCNew.Simulation.Tests/Tests.WeaponSlotsIntegration.cs` | mixed | converted: shot intervals 3 and 15 frames, budgets. Deliberately in ticks: three steps after switching, `ReadyTick >= SleepForever` (sentinel value) |
| `CnCNew.Simulation.Tests/Tests.WeaponSlotsReal.cs` | converted | firing attempts and pauses in frames; `ReadyTick > Tick − 1` refers to the executed step |
| `CnCNew.Simulation.Tests/Tests.World.cs` | mixed | converted: lifetimes (drawn frames, "a zero becomes one frame", `DieTick`), budget. **Deliberately in ticks:** scheduler order and event ticks (`update:2:5`), command ticks, `SetTimerCommand` delays (per §7 ticks of the world), steps `Run(1)`…`Run(15)` of the timer check |
| `CnCNew.Ui.Tests/Program.cs` | not affected |  |
| `CnCNew.Ui.Tests/SentenceLayoutTests.cs` | not affected |  |
| `CnCNew.Ui.Tests/TextAndSettingsTests.cs` | not affected | the two time lines check `InGameUiSettings` in original frames (500 ms = 15 frames, 333 ms = 10) – without a world, without a tick |
| `CnCNew.Ui.Tests/TransitionTests.cs` | deliberately in ticks | 19 tick expectations (order, frame numbers of the shell transitions), 3 rate statements |
| `CnCNew.Ui.Tests/UiRenderingTests.cs` | not affected |  |
| `CnCNew.Ui.Tests/WndFixtures.cs` | not affected |  |
| `CnCNew.Ui.Tests/WndTests.cs` | not affected |  |
| `CnCNew.W3D.Tests/Program.cs` | not affected |  |
| `CnCNew.W3D.Tests/SkeletonTests.cs` | not affected |  |
| `Shared/TestTime.cs` | new | Phase 21 (time helpers, time-scale test or source test) |

### 14.6 Search pass and source test

`tests/CnCNew.Game.Tests/SourceRuleTests.cs` forbids in `src/CnCNew.Game`, `src/CnCNew.Presentation`, `src/CnCNew.Ui` and `src/CnCNew.Client.Stride` (230 files, comments and strings excepted): `SimulationRate.OriginalFramesPerSecond`/`Hz30`/`Hz60`/`Hz120`/`Original`; `FromTicksPerSecond(<Zahl>)`; a named frame or tick rate with a number (`…FramesPerSec…`, `…TicksPerSec…`, `…TicksPerFrame…`, `…FrameRate…`, `…TickRate…`, `…Fps…`, `…LogicFrames…`, `…LogicTicks… = <Zahl>`); the 30 as a rate (`1.0 / 30`, `/ 30`, `* 30`, `30 *`, `1000 / 30`, `/ 33`, `33.33`, `0.0333`); the 60 and 120 as a rate (`1.0 / 60`, `/ 60d`). Three named exceptions, each with its reason in the test, and an exception that no longer matches any line makes the test fail:

| File | Line | Reason |
|---|---|---|
| `CnCNew.Game/Data/GameData.cs` | `DefaultSessionRate => SimulationRate.Original` | the one site that names a simulation rate (default of the session) |
| `CnCNew.Game/Hud/HudOverlayRenderer.cs` | `clockMilliseconds / 30 + …` | 30 milliseconds of wall time per frame of the repair cross: a duration, not a rate |
| `CnCNew.Client.Stride/Program.cs` | `_renderedFrames / 60d` | smoke-test clock of the model viewer (1/60 s of animation time per frame), without a simulation |

The test also checks itself: 14 lines that it must catch (among them the former sites of the inventory), 10 that it must let through.

Search pass with the patterns from `docs/TICKRATE-AUDIT.md` §8 after the rework, hit lines / files (`src` = the four projects, `tools` = PresentationTool, UiRenderTool, CampaignTool, SimTool, `tests` = Game, Presentation, Ui tests and `tests/Shared`):

| Pattern | `src` | `tools` | `tests` | Assessment |
|---|---|---|---|---|
| P01 `LogicFramesPerSecond` | 0/0 | 0/0 | 1/1 | the hit is a pattern line of the source test |
| P02 named rate with a number | 3/3 | 0/0 | 11/5 | `src`: `PixelsPerSecond = 40f` (credits, wall time), `RiverVScrollPerSecond = 0.002f * OriginalFrame.PerSecond`, one comment; `tests`: fixture INI and pattern lines |
| P03 literals of the 30 | 23/17 | 3/2 | 14/6 | `src`: only comments; `tools`: the hint line of `scenario-audit` and the labelled extrapolations of `SimTool perf` (§6 no. 15); `tests`: pattern lines of the source test and comments |
| P06 tick/frame count with a number | 25/16 | 13/2 | 10/6 | `src`: each is a constant in original frames (`…Frames`) or a frame count of images (animation frames, audio buffers); no tick count |
| P07 `tick % <Zahl>` | 0/0 | 0/0 | 1/1 | – |
| P08 `Tick ± … <Vergleich> <Zahl>` | 5/3 | 3/1 | 12/5 | `src`: order (`IssuedTick + 1`, `world.Tick - 1`) and the converted comparisons |
| N01 `SimulationTiming.` | 0/0 | 0/0 | 0/0 | – |
| N05 modulo | 16/10 | 3/3 | 22/13 | `src`: flashing on the original-frame or wall clock, otherwise geometry and minutes |

The remaining patterns (P04, P05, P09, P14, P16, N06, N07, N08, N13, N17) are, according to the inventory, too noisy as a rule; their hits in `src` are the sites that `docs/TICKRATE-AUDIT.md` §6 assessed line by line, and every line marked there as "Phase 21" is dealt with in §14.3 or the domain documents or named above as open. Re-read for this pass were the hits of P01, P02, P03, P06, P07, P08, N01 and N05 in `src`.

### 14.7 Visual check list for Phase 23 (by a human, at `--tick-rate 60` against `--tick-rate 30`)

None of this is accepted. Procedure: the same scene in the window once with 30 and once with 60 Hz (`scripts/demo-*.ps1` or the client with `--tick-rate 60`), plus the frame sequences of `scripts/capture-scenarios.ps1 -TickRate 60` next to those without the option.

1. **Movement:** units drive more smoothly at 60 Hz (interpolation steps half as large) and equally fast; no stutter in a two-beat cadence, no jumping when stopping.
2. **Effects:** explosions, smoke, dust, tracers and light flashes last equally long and are equally large and dense as at 30 Hz; smoke trails of moving objects do not break off (the effect clock runs with 30 steps per second, the objects with 60).
3. **Muzzle flash:** visible with every shot, neither continuous fire nor only a flicker; the recoil of the barrel matches it.
4. **Firing state:** shot animations (`FIRING_A`) start with every shot and are not triggered twice.
5. **Suspension and steering:** wheeled vehicles pitch when starting and braking as at 30 Hz, settle and do not tremble; wheels steer in and back; the cab of a truck follows.
6. **Flash of the capture and selection:** the barracks flashes during the flag raising in the same sequence and brightness; no flicker between two stages.
7. **Heal symbol and cheering:** the symbol stays for about three seconds after the last heal.
8. **Fog:** revealing and darkening run equally fast; explored scenery is darkened without visible lag.
9. **HUD:** message lines stay equally long (about nine seconds), the superweapon timer counts seconds and flashes at the same cadence, command markers and tooltips unchanged.
10. **Audio:** no doubled or missing shot and impact sounds in a match; Eva does not announce the same event more often; the under-fire message does not come more often.
11. **Mission intro** (`--mission "Maps/GC_ChemGeneral/GC_ChemGeneral.map"` and `MD_USA01`): camera moves end when the script continues; fades, letterbox and greyscale last equally long; subtitles type at the same tempo with one click per character and stay equally long; counters show the same time.
12. **Standing orders:** an attacking unit shoots at the same cadence, a fighter flies the same approaches; waypoint sequences are driven.
13. **Shell and camera:** transitions, scrolling, rotating and zooming are untouched by the rate (wall time).

### 14.8 Open after Phase 21 (state after the addendum §14.9)

- `SimTool perf`: the extrapolations `catchup30`/`catchup60`/`core30_percent` name their rate in the name (§6 no. 15; switch of the default, Phase 23). — **Phase 23:** done; `catchup` and `core_percent` are measured at the rate of the run (`0740ebf`).
- Three time scales are **not** equal at 60 Hz, because the simulation computes differently there (§14.4, second table): flash sequence of the capture, sliding of the avalanche boulder behind `FREEFALL`, approach of a jet. They belong to the list of Phase 23 (§5). — **Phase 23:** all three causes are fixed in the simulation (§14.4, second table; two of the three time-scale values are not re-measured).
- The trial run of the simulation tests at 60 Hz passes 396 of 402 tests; the 6 failures are named (§14.9.2) and are not errors of this phase. — **Phase 23:** the simulation tests pass completely at 30, 60 and 120 Hz (513 of 513, with real data 543 of 543; measured at state `1b82e45`, §15.7).
- The tests of `Game.Tests`, `Presentation.Tests` and `Ui.Tests` have no switch for the default rate: their worlds name 30 Hz (`SimulationRate.Original`) or take `GameData.DefaultSessionRate`; what holds there at 60 Hz is shown by the time-scale tests per rate, not by a trial run of the whole suite. — **Phase 23:** done; all three programs take `--tick-rate` and pass at 30, 60 and 120 Hz (`docs/rate-invariance/RATE-SWITCH-OUTER-TESTS.md`, `095ef8f`, `203806e`).
- The fade of the letterbox runs on the presentation clock instead of on wall time; the running gear is stepped per original frame and not interpolated.
- No visual acceptance, no listening test, no playing – neither at 30 nor at 60 Hz. — **Phase 23:** unchanged, with one exception: on 8 October 2026 the project owner watched **one** AI match in spectator mode at 30 Hz (`docs/AI-OBSERVED-2026-10-08.md`). At 60 Hz no human has seen or heard the game.

### 14.9 Addendum: rest of Phase 21 (6 October 2026)

Four things that the report of Phase 21 left open: item 8 of the brief (tests in game time), `SaveGame` with rate (§6 no. 3), the time scales that were not verified (§14.4) and the output of `CampaignTool` (§6 no. 12). `src/CnCNew.Simulation` was not touched in the process.

#### 14.9.1 Tests in game time

Rule, helpers and the complete list are in §14.5. Method: first the rate of the standard test worlds was put on a switch (below), then every file was converted until, at 60 Hz, only what really means ticks or what the simulation computes differently there remained; budgets that were sufficient at 60 Hz too are converted nevertheless. Not mechanical: where an expectation means "one step, then N − 1 frames", it is written that way (`Frames(180) − 1` for the sale, `death + 1 + Frames(74)` for the crash, `1 + Frames(15)` for the hold time of a turret, `Frames(30) + 1` for the first deposit) – at 30 Hz these forms are the old number and at 60 Hz they are, as measured, correct.

#### 14.9.2 Trial run of the simulation tests at 60 Hz

```powershell
dotnet run --project tests/CnCNew.Simulation.Tests -- --tick-rate 60 [--content-dir game/content] [--only TEXT]
$env:CNCNEW_TEST_TICK_RATE = '60'   # the same through the environment
```

Without the option nothing changes (30 Hz, no hint). With it the program reports the rate in the first line. The run is a **probe for Phase 23**, not an acceptance test: it tells which expectations break when the default is switched.

| State | passed | failed |
|---|---|---|
| 30 Hz (reference), before and after the conversion | 402 of 402 | 0 |
| 60 Hz, only the rate switched, no expectation converted yet | 268 of 402 | 134 |
| 60 Hz after the conversion, without `--content-dir` | 396 of 402 | 6 |
| 60 Hz after the conversion, with `--content-dir` (plus the real-data tests) | 410 of 416 | 6 |
| 120 Hz after the conversion, without `--content-dir` (for information only) | 389 of 402 | 13 |

The 6 failures at 60 Hz, by class:

| Class | Tests | What breaks |
|---|---|---|
| trajectory computed by hand per step (deliberately in ticks) | "Projectiles: straight shell flight time…", "…ballistic arc…", "…missile flight time (instant acceleration)", "…missile ignition delay and acceleration" | path points and position per tick are computed for 30 steps per second (20 path points become 40, x = 10 after one step becomes 2.5). Phase 23 decides: recompute or pin these four tests to 30 Hz |
| cadence of the simulation per tick (§5.1) | "Staged capture: the building flashes while the flag goes up…" | four flashes per preparation become nine |
| raw value of the fixed-point format at 30 Hz | "Turrets: template model, defaults and strict parsing" | `TurnRate.Raw is > 6860 and < 6866` (6° per tick in Q47.16); the line belongs to the fixed-point conversion (Phase 22) — **Correction (Phase 23, §15.2 no. 11):** this description is the version before Phase 22 and has been wrong since. Since Phase 22 the line checks `(TurnRate − PiFraction(1, 30)).Abs() <= Epsilon` – a **rate literal** (a constant per tick at 30 Hz), not a raw value of the format; fixed as `PiFraction(1, TicksPerSecond)` (`b405704`, `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md`) |

A seventh test broke before the conversion on an assumption, not on a time: "Scripts: hunting with a command button" expected the victim to still exist after 240 frames; at 60 Hz it has by then already been killed and removed. The test now accepts that as an attack too (unchanged at 30 Hz) – that the hunter kills faster at 60 Hz is an observation for Phase 23.

At 120 Hz more are added (13 failures; not assessed individually – 120 Hz is not a product rate).

What the trial run does **not** say: that the simulation behaves correctly at 60 Hz. Many tests check bounds (`>=`, ranges) that a different behaviour satisfies as well; the list of Phase 23 remains §5.

#### 14.9.3 `SaveGame` version 2

`src/CnCNew.Game/Save/SaveGame.cs`: the envelope writes `"tickRate"` directly after `"tick"`; `SaveGameHeader` carries `TickRate`, `GameSeconds` and `GameTime` (m:ss). The checks run in this order: format, version, difficulty, supported rate, **rate against the session** (`context.Templates.Rate`), fingerprint, campaign, checksum of the state, after restoring tick and rate of the header against the world, state hash. Messages: "Unsupported savegame version 1 (this game reads version 2).", "Unsupported tick rate 50 Hz in the savegame.", "This savegame was written at 60 Hz; this session runs at 30 Hz. It cannot be loaded.", "The savegame's state does not match its header (tick or tick rate differ; the file is damaged).". Version 1 is never read as 30 Hz. Lists show "title (m:ss)"; no checked-in screen hash contains a load list with entries (`baselines/screens.txt` unchanged). Test `CampaignTests` "Savegame: the header carries the tick rate…": the same savegame after 95 original frames at 30 Hz (tick 95) and at 60 Hz (tick 190) shows "0:03" both times, loads at its own rate in each case and is rejected crosswise in both directions. Details: `docs/CAMPAIGN-AND-SAVEGAMES.md` §4.

#### 14.9.4 Output of `CampaignTool`

`drive` and `audit` write, below every map line in which an end was reached, a line of its own (`game time (m:ss at 30 Hz): victory after 0:26, defeat after 0:07` or `…: lose after 0:05`) and at the end `Budget: 9000 ticks are 5:00 of game time (m:ss) at 30 Hz.`; `playthrough` puts the game time in parentheses after "at tick N" and "no outcome after N ticks". The map lines (`victory REACHED by '…' @Tick`, `win@Tick`, `lose@Tick`) and the summary lines (`… maps driven (… ticks): victory reached …`, `… identical on a second run …`) are unchanged byte for byte – they are counted with `-cmatch 'victory REACHED'` and similar patterns; no script under `scripts/` reads the output of `CampaignTool`. Measured on `MD_USA01`: `audit --ticks 600` at 30 Hz "lose@151" and `--ticks 1200 --tick-rate 60` "lose@301", both "lose after 0:05".

#### 14.9.5 Proof at 30 Hz

`SimTool input-manifest check` identical and `reference-manifest check --set quick` (9 runs identical) after every commit that touches `src` or the tools; `--set full` at the end: 50 runs identical (21 min 35 s with three runs side by side on a loaded machine). Simulation tests: 402 of 402 (Debug), 416 of 416 with `--content-dir`, `-c Release -- --long` 403 of 403. `Game.Tests` (Release, `--content-dir`): 137 tests passed, 0 failed; `Presentation.Tests` with `--content-dir`: all passed; the remaining test programs: Archives, Content, Data (also with `--content-dir`), IO, Images, Formats, Import, W3D, Ui and Audio passed. The golden files under `tests/**/baselines` are unchanged (`git status` empty for these paths). `scripts/audit-scenarios.ps1`: three of three scenes passed, with `-TickRate 60` likewise three of three. `IniTool compat-check`: 751 entries, unchanged. Clean solution build: 0 warnings, 0 errors (with deleted `bin`/`obj` of `CnCNew.Client.Stride`); `tools/CnCNew.UiRenderTool`: 0 warnings.

## 15. Phase 23: invariance with respect to the tick rate

Log of Phase 23 (`docs/handoffs/HANDOFF-PHASE23.md`), starting state `20ffce9` (7 October 2026). This section is continued step by step; what it names as measured comes from individual logs of that day.

### 15.1 Starting state: campaign and AI runs after the range repair

Objective driver, audit and reachability last ran before the range repair of Phase 22 (`b274df5`, `SquaredReach`); the repair was announced as result-identical and verified only through the manifest. Repeated at state `20ffce9`, 30 Hz, Release, a separate process per map (`--map "/NAME.map"`), counted from the 24 individual logs:

| Run | Result | as last measured? |
|---|---|---|
| `CampaignTool drive` (60,000 ticks) | victory via its own script **24 of 24**, defeat **24 of 24** | yes |
| `CampaignTool audit --ticks 900` | 24 of 24 deterministic in the second run, 24 of 24 identical after save/load, 0 hits on missing types; elimination end probes victory **13 of 24**, defeat **15 of 24** | yes |
| `CampaignTool reachability` | victory clean 24, restricted 0, blocked 0; defeat clean 24 | yes |
| 16 AI runs (part of `reference-manifest check --set full`, 50 of 50 runs identical) | **12 decided**, 4 stalemates (Armored Fury America/China and ChinaTank/AirForce, Bitter Winter America/China and ChinaTank/AirForce) | yes (manifest unchanged) |

Run time per map: objective driver 13 to 134 s (`MD_CHI04`), audit 9 to 57 s (`MD_USA01`), four processes side by side. No reference value has changed; the repair is thereby verified as result-identical through the campaign tools as well.

### 15.2 The failures of the simulation tests at 120 Hz, classified individually

Trial run at state `20ffce9` (Release, `--tick-rate N --content-dir game/content`, 429 tests): at 60 Hz **6** failures (the six from §14.9.2), at 120 Hz **14** (§14.9.2 named 13 without real data). Each of the 14 has been read and, where possible, measured through individual runs at all three rates. Classes: **T** – only the expectation of the test counts ticks, the simulation behaves the same in game time; **S** – the simulation depends on the step size (site in §5); **C** – chaotic outcome of a whole match. A case of the fixed-point resolution is not among them.

| No. | Test | Class | Cause | §5 | 30 / 60 / 120 Hz (measured) | Disposition |
|---|---|---|---|---|---|---|
| 1 | Phase 5: StealthDetectorUpdate marks and expires DETECTED | T | `Tests.Phase5.cs` literally runs 3 ticks; stealth sets in one tick (first update after appearing) plus one frame (`AtLeastOneFrame(StealthDelay 0)`) later | – (the one tick: §5.3 `Register`) | stealthed after 2 / 3 / 5 steps = 0.067 / 0.050 / 0.042 s | expectation in frames (`1 + Frames(2)`) |
| 2 | Phase 5: reference match scenario, replay v2 factions, determinism | C | `MaxTicks = 60_000` is a tick budget (2,000 / 1,000 / 500 s); the match duration is chaotic (table below) | – | radius 398: Alpha after 340 s / Alpha after 106.5 s / Beta after 530 s | pin to 30 Hz (the radius 398 was chosen for 30 Hz) |
| 3 | Construction: snapshot mid-construction and mid-sale, replay round trip | T | `EndTick` 400 and command ticks 0–70 of the replay are raw ticks; the sale (180 frames) does not fit in at 120 Hz | – (§5.3 `_saleDurationTicks` for the one tick) | barracks gone 180 / 360 / 720 ticks after the command = 6.0 s; at world tick 220 / 400 / 760 | command ticks and end via `Frames(…)` |
| 4 | Staged capture: the building flashes while the flag goes up | S | `SpecialAbilityStaging.cs`, `StepCaptureFlash`: `_flashPhase += (1 − prep/Preparation) / 3` per tick | §5.2 | 4 / 9 / 19 flashes in 2 s | simulation: phase per original frame |
| 5 | Projectiles: straight shell flight time, impact event and detonation effect | T | expects 20 path points; `_segments = ceil(length / Tempo je Tick)` | rest: §5.2 `DumbProjectileBehavior` | 20 / 40 / 80 segments = 0.667 s; impact 0.700 / 0.683 / 0.675 s | pin to 30 Hz (computed by hand per frame) |
| 6 | Projectiles: ballistic arc path points and flight time | T | expects 14 points, literals `F+6`, `F+15` | rest: §5.2 `CalcFlightPath` (flight duration in whole ticks) | 14 / 27 / 53 segments = 0.467 / 0.450 / 0.442 s; impact 0.500 / 0.467 / 0.450 s; apex 22.37 / 22.5 / 22.5 | pin to 30 Hz; the rest is a matter for the suite |
| 7 | Projectiles: missile flight time (instant acceleration) | T | expects x = 10 after one tick; `_speed = min(max, _speed + _acceleration)` per tick | rest: §5.2 `MissileAIUpdate.Move` | x after one frame 10 / 7.5 / 6.25; impact 0.333 / 0.350 / 0.350 s | pin to 30 Hz |
| 8 | Projectiles: missile ignition delay and acceleration | T | table of x per tick | rest: §5.2 `MissileAIUpdate.Move` | x after frame 10: 56 / 50 / 47.19; impact 0.500 / 0.500 / 0.517 s | pin to 30 Hz; the lag in flight (up to 8.8 units) is real and a matter for the suite |
| 9 | Chinook: helicopter slow death falls, crashes and leaves a hull | T | expects `death + 1 + Frames(74)`; the Euler fall does not scale that way | rest: §5.2 `HelicopterSlowDeathBehavior.Update` | 75 / 149 / 298 ticks = 2.500 / 2.483 / 2.483 s (expected 297) | expectation as a range in frames |
| 10 | Collision: ToppleUpdate push, kill, stump and mid-fall snapshot continuation | S | `ToppleAndFallModules.cs`: `_angularAcceleration = speed * InitialAccelPercent` takes a speed per tick as an acceleration per tick²; the fall lasts 3–5 updates whatever the rate | §5.1 `InitialAccelPercent`, §5.2 `ToppleUpdate.Update`, `ResolveFootprintCollisions` | fall 3 / 4 / 5 ticks = 0.100 / 0.067 / 0.042 s; push at step 4 / 7 / 13 | simulation: scale the acceleration with 30/rate |
| 11 | Turrets: template model, defaults and strict parsing | T | `(TurnRate − PiFraction(1, 30)).Abs() <= Epsilon` is a constant per tick at 30 Hz – a rate literal, **not** a raw value of the format (§14.9.2 still describes the line in the version before Phase 22) | – | raw 1,756,906 / 878,453 / 439,226; times the rate gives π rad/s at every rate | `PiFraction(1, TicksPerSecond)` |
| 12 | Turrets: they turn at their rate toward the victim and the shot waits | S (small) | `SimulationWorld.Turrets.cs`, `IsAligned`: fixed 0.035 rad against a 1.5° step – after 59 steps (88.5°) the turret already counts as aligned | §5.2 `IsAligned`, §5.3 `StepTurrets`, §5.4 `_turretWaits` | shot in step 16 / 31 / 60 = 0.533 / 0.517 / 0.500 s; `TurretWaits` 15 / 30 / 59 | simulation (release on the grid) or tolerance of one frame |
| 13 | Turrets: hold, recentre, rest and a victim that dies | S | as 12 (`TicksUntilShot == Frames(15)`, measured 59) | as 12 | as 12 | as 12 |
| 14 | Turret attack: a dead or departing victim ends it, a dead attacker leaves nothing | S | as 12 (the same expectation, the same tank; not measured individually, inferred from 12/13) | as 12 | – | as 12 |

On no. 2, budget 2,000 s at every rate – one radius step shifts the outcome as strongly as a rate change, at 30 Hz too:

| Radius of the arena | 30 Hz | 60 Hz | 120 Hz |
|---|---|---|---|
| 396 | Beta, 169.5 s | Beta, 751.5 s | no winner |
| 397 | Beta, 431.5 s | Beta, 125.5 s | Alpha, 241.5 s |
| 398 | Alpha, 340 s | Alpha, 106.5 s | Beta, 530 s |
| 399 | Beta, 135 s | no winner | Alpha, 342 s |
| 400 | no winner | Beta, 1,180 s | Beta, 947.5 s |

From this: 9 of the 14 are test expectations, 4 are real couplings that are all already in §5 (no new finding), 1 is chaotic. **Three tests pass at 60 Hz only by chance** (no. 1 and no. 3 without any reserve, no. 10 with one tick) and are converted independently of 120 Hz. Not determined: the first deviating command line in no. 2 ("no single cause" rests on the scatter of the table); the shares of the three couplings in no. 10; the turret angle at the moment of the shot in no. 12–14 (88.5° is computed from 59 × 1.5°).

### 15.3 The script VM on the original grid: `MD_USA02` and the campaign at 30, 60 and 120 Hz

**Finding.** Objective driver, the same binaries, 30 against 60 Hz (`CampaignTool drive <content> --map "/NAME.map" [--tick-rate 60 --ticks 120000]`): on 23 of 24 maps the end script of the mission ran
at the same game time (within 0.05 s), on `MD_USA02` it did not – victory `END_Final_` at 90.60 s against 340.57 s, defeat `Lose_END_Final_` at 412.03 s against 529.52 s; the first step of the
driver ("send named Truck01 to area Warehouse Lot") became possible at 30 Hz at tick 2,295 (76.5 s), at 60 Hz at tick 19,590 (326.5 s).

**Cause** (`CampaignTool script <content> --map MD_USA02 --name "Truck01 Deploy"`): the script `Global Timers` sets the frame timer `Convoy Trucks` to 16,000 with `SET_TIMER`; `Truck01 Deploy`
(group `Convoy Trucks Land`) creates the first truck as soon as `COUNTER(Convoy Trucks < 15000)` holds. Since stage 1 the VM kept timers in ticks (32,000 at 60 Hz, −1 per tick), the literal of the
map stayed 15,000: 17,001 ticks = 283.4 s instead of 1,001 frames = 33.4 s. The same map compares further timers with frame literals (defeat). These are the lines "`Update` (W6)" and "timer values" from
§5.4 – not a new finding, but the first measured one.

**Correction** (one commit per cause; description in `docs/SCRIPT-ENGINE.md` §2 "The clock of the VM is the original frame", site of the escape in `docs/SIMULATION-CORE.md` §41):

| Commit | Cause | Correction | 30 Hz |
|---|---|---|---|
| `727c4d6` | the VM evaluated per tick and counted timers in ticks; the original per frame (`GameLogic::update`, `ScriptEngine::update`) | `ScriptEngine.Update` works only on ticks that begin an original frame; timers, sequence waiting times and end-game timers count frames; under `FREEZE_TIME` the VM counts the calls itself; `CounterForDisplay` of the presentation no longer converts | input manifest identical, manifest `quick` 9 of 9 |
| `2fe8b32` | `UNIT_EMPTIED` compared with the previous **tick**, but the VM evaluates per frame – never true at 60/120 Hz | comparison with the previous frame (`ScriptConditions::evaluateUnitHasEmptied`) | identical, 9 of 9 |
| `d710835` | a named object that appears and disappears between two updates counted as never created (`didUnitExist`) – at 120 Hz there are three ticks in between | the remembering "has existed" runs in every tick; everything else waits for the frame | identical, 9 of 9 |

**What follows by itself from the lists in §5.3/§5.4** (no commit of its own, because it is part of the same update): bridge state for one update, `AttachPendingTraps`, range check of the capture,
`ProcessTeamEvents` with `WasIdle` and `_pendingTeamScripts`, `ProcessSequences` with `_busyThisTick`, `ProcessTeamBuilds`, `UpdateTeamQueues`, `UpdateFollowers`, `SUPPLY_SOURCE_SAFE`, the edges
`PLAYER_LOST_OBJECT_TYPE` and `NAMED_/TEAM_ENTERED/EXITED_AREA*`, the order in `MissionSession.Step`. **Deliberately per tick** remain `ScriptEngine.Observe`, the answers given to the target acquisition of the
world and the acceptance of the commands of the VM in the world step. **Not touched:** the objective driver (`Testing/ObjectiveDriver*.cs`) – its cadence was not the cause.

**Measurement at the final state `d710835`** (7/8 October 2026, Release, binaries from a copy, a separate process per map and rate, at most four side by side; 60,000 / 120,000 / 240,000 ticks;
counted from 72 individual logs with `victory REACHED by '…' @T`, game time = T / rate). Acceptance fixed in advance: the same end script on every map at all three rates; the game time within
± 1 original frame (0.034 s) of the 30 Hz time where the chain is purely script-driven, otherwise ± 2 % or ± 1 s.

| Map | 30 Hz: victory / defeat (s) | 60 Hz | 120 Hz | End scripts (the same at all three rates) |
|---|---|---|---|---|
| `GC_AirGeneral` | 9.00 / 5.00 | 9.00 / 5.00 | 9.00 / 5.00 | `END_Final_GLA` / `Lose_END_Final_GLA` |
| `GC_ChemGeneral` | 9.00 / 5.00 | 9.00 / 5.00 | 9.00 / 5.00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `GC_ChinaBoss` | 12.00 / 81.03 | 12.00 / 81.03 | 12.00 / 81.03 | `End the mission` / `Lose_END_Final_USA` |
| `GC_LaserGeneral` | 8.00 / 5.00 | 8.00 / 5.00 | 8.00 / 5.00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `GC_NukeGeneral` | 8.00 / 5.00 | 8.00 / 5.00 | 8.00 / 5.00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `GC_Stealth` | 8.00 / 5.00 | 8.00 / 5.00 | 8.00 / 5.00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `GC_SuperWeaponsGeneral` | 8.00 / 5.00 | 8.00 / 5.00 | 8.00 / 5.00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `GC_TankGeneral` | 8.00 / 5.00 | 8.00 / 5.00 | 8.00 / 5.00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `MD_CHI01` | 10.03 / 8.03 | 10.03 / 8.03 | 10.03 / 8.03 | `END_Final_` / `Lose_END_Final_` |
| `MD_CHI02` | 10.03 / 10.03 | 10.03 / 10.03 | 10.03 / 10.03 | `END_Final_` / `Lose_END_Final_` |
| `MD_CHI03` | 57.40 / 149.03 | 57.40 / 149.03 | 57.40 / 149.03 | `Cheer1Done` / `Lose_END_Final_` |
| `MD_CHI04` | 1608.60 / 9.10 | 1608.60 / 9.10 | 1608.60 / 9.10 | `END_Final_` / `Lose_END_Final_` |
| `MD_CHI05` | 88.73 / 8.03 | 88.73 / 8.03 | 88.73 / 8.03 | `CINE_ENDVictory` / `Lose_END_Final_` |
| `MD_GLA01` | 17.53 / 8.03 | 17.53 / 8.03 | 17.53 / 8.03 | `Cheer1Done` / `Lose_END_Final_` |
| `MD_GLA02` | 8.00 / 19.07 | 8.00 / 19.07 | 8.00 / 19.07 | `END_Final_` / `Lose_END_Final_` |
| `MD_GLA03` | 134.43 / 8.07 | 134.43 / 8.07 | 134.43 / 8.07 | `CINE_ENDVictory` / `Lose_END_Final_` |
| `MD_GLA04` | 34.07 / 8.03 | 34.07 / 8.03 | 34.07 / 8.03 | `CINE_END` / `Lose_END_Final_` |
| `MD_GLA05` | 46.47 / 34.53 | 46.47 / 34.53 | 46.47 / 34.53 | `CINE_END04END` / `Lose_END_Final_` |
| `MD_USA01` | 26.17 / 7.03 | 26.17 / 7.03 | 26.17 / 7.03 | `END_Final_USA` / `Lose_END_Final_USA` |
| `MD_USA02` | 90.60 / 412.03 | 90.60 / 412.03 | 90.60 / 412.03 | `END_Final_` / `Lose_END_Final_` |
| `MD_USA03` | 50.30 / 8.03 | 50.30 / 8.03 | 50.30 / 8.03 | `CINE_AVVictory` / `Lose_END_Final_` |
| `MD_USA04` | 10.10 / 20.57 | 10.10 / 20.57 | 10.10 / 20.57 | `END_Final_USA` / `Lose_END_Final_USA` |
| `MD_USA05` | 48.10 / 42.67 | 48.10 / 42.67 | 48.10 / 42.67 | `END_Fade 04` / `Lose_END_Final_` |
| `Training01` | 14.00 / 88.50 | 14.00 / 88.50 | 14.00 / 88.50 | `Player Wins` / `Player Loses` |

Result: victory **24 of 24** and defeat **24 of 24** at 30, 60 and 120 Hz, the same end scripts on every map, largest deviation from the 30 Hz time over all 144 points in time **0.00 s** – every
map lies within ± 1 original frame, including those whose chain depends on movement and combat. (The world interventions of the driver replace the combat there; this verifies the script chain, not the
driving behaviour.) The run at state `727c4d6` produced the same table. Before the correction at 60 Hz: `MD_USA02` 340.57 / 529.52 s, all other maps within 0.05 s; for 120 Hz there is no measurement
of the state before it.

Further runs at the final state:

| Run | Result |
|---|---|
| `SimTool input-manifest check` | identical |
| `SimTool reference-manifest check --set full --jobs 6` | **50 of 50 runs identical** |
| `CampaignTool audit --ticks 900` (30 Hz, 24 maps individually) | 24 of 24 deterministic, 24 of 24 identical after save/load, 0 hits on missing types, end probes victory 13 of 24, defeat 15 of 24 – unchanged |
| `CampaignTool audit --tick-rate 60 --ticks 1800` | 24 of 24 deterministic, 24 of 24 identical after save/load, end probes victory 13 of 24, defeat 15 of 24 |
| simulation tests with `--content-dir` | 30 Hz 442 of 442; `--tick-rate 60` the six known failures (§14.9.2), otherwise none; `--tick-rate 120` the 14 from §15.2 |
| new tests (`Tests.TickRate.ScriptClock.cs`, `Tests.TickRate.ScriptEvents.cs`), each at 30/60/120 Hz in one run | 9 of 9 |
| `Game.Tests -- --only TimeScaleTests,SourceRuleTests,CampaignTests` (Release) | passed (the named countdown reads the same time at all three rates) |
| `dotnet build CnCNew.slnx` | 0 warnings |

**Not verified or open:** The audit saves at a fixed tick; that a state **between** two frames continues identically is verified only by the synthetic test (also frozen), not by a run
with real data. A UI signal (`SignalUiInteraction`) between two updates is, as before, not in the saved state (at 30 Hz the same gap between signal and next step, at
60/120 Hz up to one frame long). The three fields of the script state with a new meaning at 60/120 Hz (timer value, sequence waiting time, `endTimer`: frames instead of ticks) have no new
format version. The standing commands of the VM (`UpdateFollowers`, `FireAtOrdered`) run per frame – whether that looks different in the game at 60 Hz from one command per tick, nobody has seen. The
2 s cache of `SUPPLY_SOURCE_SAFE` is still missing. No human has played a mission at 60 or 120 Hz.

### 15.4 Measurement rounds, state after two rounds and the decision of the project owner of 8 October 2026

The rate-equivalence suite (`docs/SIMULATION-CORE.md` §39) and the distribution comparison (§40) were built with tolerances and test limits fixed in advance; none of them has been changed since. Quantities outside the tolerance, of 4,307 (60 Hz / 120 Hz):

| State | Commit | Suite | Distribution comparison 30 against 60 Hz (320 matches per rate) |
|---|---|---|---|
| start (before any correction) | `91cb830` | 479 / 625 | 38 of 39 tests passed; failed: share of decided matches 79.1 % → 70.0 %, z = −2.63 (limit 1.96) |
| round 1: seven correction packages (projectiles, script VM, physics modules, time offset, ground movement, flight, turrets/fire) | `62fbb6a` | 63 / 64 | 39 of 39; share of decided 79.1 % → 76.3 %, z = −0.85; duration D = −0.10 min (SE 0.37) |
| round 2: consolidation (one frame convention across all packages, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`) | `3b44506` | 35 / 33 | 39 of 39; share of decided 79.1 % → 76.6 %, z = −0.76; duration D = −0.11 min (SE 0.39) |

At 30 Hz nothing changed in either round: the full reference manifest is identical at state `3b44506` (50 of 50 runs), the input manifest likewise, and the 30 Hz survey over 320 matches is, after both rounds, byte for byte the same file as before the corrections. At state `3b44506` all test programs pass, the simulation tests also with `--tick-rate 60` and `--tick-rate 120`; the objective driver reaches victory and defeat on all 24 campaign maps at 30, 60 and 120 Hz via the same end script at the same game time (deviation 0.00 s), the audit is deterministic at 30 and 60 Hz on 24 of 24 maps and identical after save/load (end probes 13/24 and 15/24).

After round 2 outside the tolerance, per group (60 / 120 Hz): `ai` 24 / 23 (up to the beginning of the first fight at 117.9 s all positions between 30 and 60 Hz are bit-identical; after that the next reader does not yet read the end-of-frame view – the facing of a shooter that stops in the middle of a frame), `projectiles` 4 / 4 (scatter salvos, not traced back), `turrets` 4 / 0 (sentry drone, `DeployStyleAIUpdate`), `flight` 0 / 3 (arrival thresholds of the winged flight at 120 Hz), `locomotion` 2 / 2 (mob members read their nexus at the instantaneous position), `timed` 1 / 1 (`CINE_BShipWeaponDetonationObject` never ends at 30 Hz – an error at 30 Hz); `weapons`, `overtime`, `economy`, `scripts` 0 / 0.

**Decision.** The brief of the phase contained an abort criterion: if the suite or the distribution comparison do not pass after two measurement rounds per group, the default stays at 30 Hz. After round 2 the comparison passed, the suite not completely. On 8 October 2026 the project owner **lifted** the criterion: the goal of the phase – the suite passes at 30, 60 and 120 Hz, after that 60 Hz is the default – is to be reached, and everything that lies on the way there is fixed. This includes corrections that change the behaviour at 30 Hz where the cause is an error at 30 Hz; they remain one cause each in a commit of its own with a rewritten manifest (`docs/MULTIPLAYER-PLAN.md` §6 rules 2 and 3). Unchanged: no tolerance and no test limit is adapted to a result.

### 15.5 The switch of the default: inventory

As of 8 October 2026, starting state `203806e`. Searched in `src/`, `tools/`, `tests/` and `scripts/` for `Hz30`, `SimulationRate.Original`, `DefaultSessionRate`, `ToolRate`, `TestRate`, `TickRate = 30`, `--tick-rate`, `tickRate` and for tick budgets that mean game time at 30 Hz. The table names every site that names or assumes the **default rate** – not the sites that mean the original frame (`SimulationRate.Original` as a grid in the simulation, `OriginalFrame.PerSecond` outside) and not the tests that name their rates themselves (three-rate tests). "Preparation" means: changed in a commit before the switch in such a way that nothing changes at 30 Hz; "follows" means: the site derives its value and is not touched in the switch commit.

**Rule after the preparation.** The default rate is defined in exactly one place: `src/CnCNew.Game/Data/ProductRate.cs` (`ProductRate.TicksPerSecond`). The simulation knows no default – whoever creates a world names the rate. The game layer turns it into `GameData.DefaultSessionRate`; client and tools take this value; the test projects that must not reference the game layer (`Simulation.Tests`, `Presentation.Tests`, `Ui.Tests`) include the file `ProductRate.cs` as source (`<Compile Include="../../src/CnCNew.Game/Data/ProductRate.cs" Link="…" />`), `Game.Tests` likewise (for `tests/Shared/TestRateOption.cs`, which does not know the game layer). What is recorded at a rate (reference and input manifest, savegames, replays) carries its rate in the file and does not follow the default.

| Site | What it is | At the switch |
|---|---|---|
| `src/CnCNew.Game/Data/ProductRate.cs`, `ProductRate.TicksPerSecond` | **the one definition** of the default rate (new; `internal const int`) | **the one changed line:** 30 → 60 |
| `src/CnCNew.Game/Data/GameData.cs`, `DefaultSessionRate` | default of a session whose host names no rate (`GameData.Load(…, rate: null)`); previously `=> SimulationRate.Original` | preparation: from `ProductRate`; follows |
| `src/CnCNew.Client.Stride/Program.cs` (`--tick-rate`), `Game/Shell/GameShell.cs` (`CreateLoading(…, rate)`) | rate of the client without an option: `GameData.DefaultSessionRate` | follows |
| `src/CnCNew.Client.Stride/Program.cs` (`--match`), `Game/Shell/Screens/SkirmishScreens.cs`, `tools/CnCNew.UiRenderTool/HudFeedbackScenes.cs` | started a match with `SkirmishMatchOptions.Standard`, whose `MaxTicks` are 108,000 **ticks**: one hour at 30 Hz, at 60 Hz the match would have ended after 30 minutes | preparation: `SkirmishMatchOptions.StandardAt(rate)` (one hour of game time at every rate); follows |
| `src/CnCNew.Simulation/Testing/SkirmishMatchScenario.cs`, `ReferenceMatchScenario.cs` | `Standard` with tick budgets (108,000 and 216,000 respectively) that mean game time at 30 Hz | preparation: additionally `StandardAt(SimulationRate)`; `Standard` stays for callers with a budget of their own |
| `src/CnCNew.Simulation/Testing/StressScenario.cs`, `StressOptions.Standard` | 216,000 **steps**, teardown 6,000 steps, checkpoints every 1,000 | stays (decision below) |
| `src/CnCNew.Simulation/Mission/MissionSession.cs`, `MissionOptions.Standard` | `MaxTicks` 0 (no budget), `PresentationLatencyTicks` 0 (frames), `CombatIntervalTicks` 3 (switch) | nothing to do |
| `src/CnCNew.Simulation/Skirmish/Original/ScriptedSkirmishSession.cs`, `ScriptedSkirmishOptions.Standard` | `MaxTicks` 108,000 ticks; the only caller outside the tests is `SimTool original-ai`, which converts the budget into game time through `ToolRate.Budget` | nothing to do |
| `src/CnCNew.Simulation/Time/SimulationRate.cs`, `Combine`; `tools/CnCNew.SimTool/InputManifest.cs`, `RateAtOriginal` | at 30 Hz the fingerprint stays without a rate component, so that everything recorded at 30 Hz keeps its identity; at 60 and 120 Hz the rate goes in | stays: this is the identity of the 30 Hz states, not a default. Consequence: the world fingerprint of the default is a different one after the switch (input manifest §15.6) |
| `tools/CnCNew.SimTool/ToolRate.cs`, `Current` (previously `= SimulationRate.Hz30`) | rate of a tool run without `--tick-rate` | preparation: `GameData.DefaultSessionRate`; follows |
| `tools/CnCNew.SimTool/ToolRate.cs`, `Manifest` (previously `=> SimulationRate.Hz30`) and the sentence "The manifest commands always run at the recorded 30 Hz" (`Program.cs`, §9 of this document) | the manifest commands ran with a constant of the tool and refused every other `--tick-rate` | preparation: dropped. `check` runs with the rate and in the schema that **the file** names (`ToolRate.UseRecorded`), and refuses a deviating `--tick-rate`; `write`, `show`, `results`, `list` take `--tick-rate` or the default |
| `tools/CnCNew.SimTool/ReferenceManifest.cs`, `AiMatchTicks` 45,000, `EightPlayerTicks` 9,000, `EightPlayerQuickTicks` 3,000, `SweepTicks` 3,000, `MissionTicks` 3,000, `ReferenceMatchOptions.Standard.MaxTicks` 216,000 | tick budgets of the manifest runs that mean game time at 30 Hz | preparation: as original frames (`…Frames`), per run with `rate.Time.OriginalFrames(n)` in ticks; follows |
| `tools/CnCNew.SimTool/ReferenceManifest.cs`, `InputManifest.cs`, `Schema` | file form of the manifests | preparation: schema 2 (decision below); `check` still reads schema 1 |
| `tools/CnCNew.SimTool/Program.cs`, default budgets (`ToolRate.Budget(args, …, Originalbilder)`) for `reference-match`, `ai-match`, `skirmish-sweep`, `mission-run`, `unit-audit`, `perf`, `original-ai`; `ai-survey`/`ai-census` (`--minutes`) | game time, already before this package | nothing to do |
| `tools/CnCNew.SimTool/Program.cs`, `stress --ticks` | steps | stays |
| `tools/CnCNew.CampaignTool/ToolRate.cs`, `Current` (previously `= SimulationRate.Hz30`); default budgets `drive` 2,000 s, `audit` 60 s, `playthrough` 30 s | rate without an option; budgets already game time | preparation: `GameData.DefaultSessionRate`; follows |
| `tools/CnCNew.PresentationTool/ScenarioAudit.cs`, `SyncPerfCommand.cs` (`--tick-rate`) | without an option `GameData.DefaultSessionRate` | follows |
| `tools/CnCNew.PresentationTool/SyncPerfCommand.cs`, `--ticks` (default "18000") | tick budget that means ten minutes at 30 Hz | preparation: without `--ticks` 18,000 original frames in ticks of the rate; follows |
| `tools/CnCNew.PresentationTool/FxAuditCommand.cs` | names `SimulationRate.Original` itself (effects run on the original frame) | stays at 30 Hz, for this reason (`docs/rate-invariance/RATE-SWITCH-OUTER-TESTS.md` §7.6) |
| `tools/CnCNew.UiRenderTool/Program.cs` (`--tick-rate`) | without an option `GameData.DefaultSessionRate`; the output is rate-independent | follows |
| `tools/CnCNew.MapTool`, `IniTool`, `AssetTool`, `ImageTool`, `W3dTool` | build no world and no catalog per rate | nothing to do |
| `tools/CnCNew.SimTool/RateAudit.cs`, `CatalogCost.cs`, `RateCompare.cs` | compare rates and name them themselves (30 as the reference) | stays |
| `tests/CnCNew.Simulation.Tests/TestTime.cs`, `TestRate.Default` (previously without an option `SimulationRate.Original`) | rate of the default test worlds | preparation: without an option `TestRate.Product` from `ProductRate`; follows |
| `tests/Shared/TestRateOption.cs`, `TicksPerSecond` (previously without an option 30), `tests/Shared/TestTime.cs`, `TestRate.Default` | the same for `Presentation.Tests`, `Game.Tests`, `Ui.Tests` | preparation: `DefaultTicksPerSecond` from `ProductRate`; follows |
| first output line of the test programs ("… a probe, the reference is 30 Hz") | appeared at every rate except 30 Hz | preparation: appears when the rate of the run is not the default, and names the default |
| `tests/CnCNew.Game.Tests/HotkeyTests.cs`, `SpectatorTests.cs` | loaded `GameData` without a rate and therefore ran with the default at every `--tick-rate` | preparation: `rate: TestRate.Default` |
| `tests/CnCNew.Simulation.Tests/Tests.State.cs` (reference match with real data), `tests/CnCNew.Game.Tests/DeterminismTests.cs` (D3, D4) | `ReferenceMatchOptions.Standard`: 216,000 ticks are 2 h / 1 h / 30 min | preparation: `StandardAt(rate)`, two hours of game time at every rate |
| `tests/CnCNew.Game.Tests/SourceRuleTests.cs`, named exception for `GameData.cs` | "the one site outside the simulation that names a rate" | preparation: the exception now applies to `ProductRate.cs`; the text of the exception names no number and fits before and after the switch |
| tests with `SimulationRate.Hz30`/`Original` as the named rate (`Tests.TickRate*.cs`, `Tests.RateInvariance*.cs`, `Tests.Basics.cs`, `TimeScaleTests`, `CampaignTests` "Savegame: the header carries the tick rate", refusal texts in `Tests.State.cs`) | name their rate themselves | stays |
| `scripts/audit-scenarios.ps1`, `capture-scenarios.ps1`, `capture-text.ps1`, `capture-world.ps1`, `check-build-hotkeys.ps1`, `check-dozer-build.ps1`, `check-spectator.ps1`, `demo.ps1`, `demo-factory-exits.ps1`, `demo-spectator.ps1` (`-TickRate`, 0 or not set = default of the client/tool) | only pass `--tick-rate` through when it is set | follows |
| `scripts/perf-profile.ps1`, `-TickRate 30`, `$Ticks = 30 * 60 * $TickRate`, `100 * $TickRate`, `300 * $TickRate` | the only number of a rate in a script | preparation: `-TickRate 0` = default of the tool; budgets as `--minutes 30` and `--seconds 100`/`300`; the summary reads the rate from the `summary:` line of the tool (`tick_rate`); follows |
| `scripts/perf-compare.ps1`, `audit-vulnerable.ps1`, `demo-*.ps1` (pass `@args` on) | no rate | nothing to do |

**After the preparation, outside the tests that check their rates, nothing names "30 Hz" as the default any more.** Control: `grep -rnE "Hz30|SimulationRate\.Original" tools src/CnCNew.Game src/CnCNew.Client.Stride scripts` now only finds `fx-audit`, the rate comparisons (`RateAudit`, `CatalogCost`) and, in `InputManifest.cs`, the schema 1 rule and `RateAtOriginal`.

#### 15.5.1 Manifests: rate and schema are in the file

- **Rate.** `reference-manifest check` and `input-manifest check` read the file first and run with `simulation.tickRate` of the file. The same binary thus checks the 30 Hz manifest today and the 60 Hz manifest after the switch. `--tick-rate N` may repeat the rate of the file; if it names a different one, the command aborts ("… records 30 Hz; --tick-rate 60 is refused. A manifest is checked at the rate it was written at."). `write`, `show`, `results` and `list` run with `--tick-rate N`, without an option with the default; the header line of the output and the closing line name the rate.
- **Schema 2 of the reference manifest** (`write`): the parameters of every run name the game time next to every tick budget (`maxTicks` and `maxGameSeconds`, for missions and the stress test `ticks` and `gameSeconds`), and values that are original frames are called so (`graceOriginalFrames`, `incomeIntervalOriginalFrames`, `sciencePointIntervalOriginalFrames` instead of `graceTicks`, `incomeInterval`, `sciencePointInterval` – at 30 Hz the old name was right, at 60 Hz `graceTicks: 300` would have claimed five instead of ten seconds). The stress test additionally carries `budgetCounts: "steps"`. `tickRate` is in the header and in every run as before. **The result fields are the same in both schemas.**
- **Schema 2 of the input manifest** (`write`): additionally `simulation.tickRate`. Schema 1 had no such entry and is by its definition written at 30 Hz.
- **Schema 1 is still checked, in its own form.** `check` builds the parameters the way the schema of the file writes them; the checked-in 30 Hz files stay byte for byte what they are and need **no** rewriting before the switch. An unknown schema and a reference manifest without `simulation.tickRate` are refused with a message. The schema 1 path is no longer needed after the 60 Hz manifests (schema 2) are written and can then be dropped.

#### 15.5.2 Budgets of the manifest runs

| Run | Budget (game time) | Ticks at 30 Hz | Ticks at 60 Hz | Save point |
|---|---|---:|---:|---|
| `reference-match` | up to 2 h (`ReferenceMatchOptions.StandardAt`); ends 300 frames after the victory | 216,000 | 432,000 | half the length of the replay |
| 16 AI runs (4 maps × 4 pairings) | 25 min | 45,000 | 90,000 | half the length |
| `ai8_*` (3 maps) | 5 min | 9,000 | 18,000 | half the length |
| `ai8_TwilightFlame_short` | 100 s | 3,000 | 6,000 | half the length |
| `skirmish-sweep` (per map) | 100 s, run-on at most a quarter of that | 3,000 | 6,000 | – |
| 27 mission runs | 100 s | 3,000 | 6,000 | save at half the length |
| `stress` | **216,000 steps** | 216,000 (2 h) | 216,000 (1 h) | half the length |

At 30 Hz every number is the earlier one; `reference-manifest check` of the checked-in file is identical (§15.6). The save points derive from the run length and needed no change. Checked against `docs/TICKRATE-MIGRATION.md` §8.1: `MaxTicks`, `MinTicks` (0) and `CheckpointInterval` of the options are ticks – only `MaxTicks` is scaled; `CheckpointInterval` (1,000 ticks) stays a tick count (at 60 Hz twice as many checkpoints per game time in the replay, no influence on the run); `GraceTicks`, `IncomeInterval`, `SciencePointInterval` are original frames and are converted by the sessions themselves.

**Decision stress test.** The options were: (a) keep 216,000 steps – at 60 Hz that is one hour of game time; (b) 432,000 steps – two hours of game time at 60 Hz, twice the run time; (c) express the schedule of the test in game time – half the load per step at 60 Hz. Chosen is **(a)**. The purpose of the test is the load per simulation step (per tick one departure and one replacement, sparks every second, smoke every third tick, §8.3) and the stability over a fixed number of steps; both are preserved exactly with (a), (c) would measure something else, and (b) only doubles the duration without changing the load per step. The statement of the handoff "stress test 216,000 ticks ≙ 2 h" therefore no longer holds at 60 Hz: the manifest names 216,000 steps and `gameSeconds` 3,600. At 60 Hz the run is a different one than at 30 Hz (the lifetimes of the fixture are milliseconds and last twice as many steps), hence with a new final hash.

#### 15.5.3 Formats

| Format (version) | Stored tick or duration fields | Reading | Finding |
|---|---|---|---|
| Replay header (12) | `tickRate`, `endTick`, `checkpointInterval`; `Tick` of every command, `SetTimerCommand.DelayTicks`/`RepeatTicks` | ticks of the rate in the header | state at the stored rate. The world is created with the rate of the header; a catalog of a different rate is refused ("The replay was recorded at 30 Hz; the catalog was built for 60 Hz.") |
| `WorldSnapshot` (10) | `tickRate`, `tick`, all wake times, timers and per-tick values in the state | ticks of the stored rate | state at the stored rate; refusal "The snapshot was written at 30 Hz; the catalog was built for 60 Hz." |
| `MissionSnapshot` (3) | `tickRate`; `maxTicks` | tick budget of the stored rate (0 = none) | state at the stored rate – the state continues with its rate, the budget means the same point |
| | `presentationLatencyTicks` | original frames (the value of the option as it was passed; `ScriptEngine.Forward` converts) | rate-independent; the name is misleading, the bytes are right |
| | `combatIntervalTicks` | switch (0 or not 0) | no time |
| | counters and timers of the VM | original frames since Phase 23 (§15.3) | rate-independent |
| `ScriptedSkirmishSession` state | `tickRate`, `maxTicks`, `graceFrames`, `sciencePointIntervalFrames` | ticks of the stored rate or original frames respectively | as above |
| `SaveGame` (3) | `tickRate`, `tick`; embedded `MissionSnapshot` | ticks of the stored rate; lists show game time | a state of a rate other than that of the session is refused ("This savegame was written at 30 Hz; this session runs at 60 Hz. It cannot be loaded.") |

**No stored field is a default that would be read wrongly after the switch; no byte changes, no version is raised.** Consequence of the switch for existing data: a savegame written at 30 Hz loads in a session with 30 Hz (client `--tick-rate 30`) and continues there with 30 Hz; the client without an option (60 Hz) refuses it with the existing message. The shell offers no choice of the rate and does not load a state with its rate afterwards – whoever wants to continue playing old states starts the client with the rate of the state. This is permissible under `docs/MULTIPLAYER-PLAN.md` §6 rule 7 (no migration duty before the first release) and named as a limit.

Tests with a fixture written at 30 Hz, independent of the default rate: `Tests.TickRateStatesWrittenAtThirtyOutliveTheDefault` (new, `Simulation.Tests`: replay, `WorldSnapshot` and `MissionSnapshot` written at 30 Hz, continued with a 30 Hz catalog up to the stored budget, refused with a message by catalogs with 60 and 120 Hz and the default rate) and `CampaignTests` "Savegame: the header carries the tick rate …" (existing: a 30 Hz state loads into a 30 Hz session and is refused by a 60 Hz session, and vice versa).

### 15.6 The switch: what changes, trial values at 60 Hz, golden values

As of 8 October 2026. The preparation is five commits (`0cb2ddf`, `d1402fe`, `632d189`, `80db1df`, `c112b17`), the switch is `809ef04`. Everything in this section is measured on one machine, with several runs side by side (run times are not performance figures); what is measured is the source state of the switch commit (the working tree from which the binaries come is byte for byte the commit). **No human has seen or heard any of it.** The checked-in manifests under `baselines/` are not changed in this package; the project owner writes the 60 Hz manifests in a commit of their own.

#### 15.6.1 What the switch commit changes

One line: `ProductRate.TicksPerSecond` 30 → 60 (`src/CnCNew.Game/Data/ProductRate.cs`). From this follows without further change: `GameData.DefaultSessionRate`, the client without `--tick-rate` (smoke test: "session: simulation rate 60 Hz"), `SimTool`, `CampaignTool`, `PresentationTool`, `UiRenderTool` without `--tick-rate`, the default worlds of all test programs and, through the tools, all scripts. The world fingerprint of a default session is a different one (at 60 Hz the rate goes into the fingerprint, §15.5).

**Preparation, checked per commit** (`SimTool` built in Release, `input-manifest check` and `reference-manifest check --set quick --jobs 3` against the checked-in files):

| Commit | Content | Input manifest | Reference manifest (quick set, 9 runs) |
|---|---|---|---|
| `0cb2ddf` | one definition of the default rate (`ProductRate`), tests and `CampaignTool` derive | identical | identical |
| `d1402fe` | manifest commands with the rate and the schema of the file, budgets as game time, schema 2 | identical (30 Hz, schema 1) | identical (30 Hz, schema 1) |
| `632d189` | default skirmish one hour of game time at every rate (`SkirmishMatchOptions.StandardAt`) | identical | identical |
| `80db1df` | tests: reference match budget as game time, states written at 30 Hz | test code only, not checked separately | test code only, not checked separately |
| `c112b17` | `sync-perf` and `perf-profile.ps1`: budgets as game time, no rate in the script | – | **full set at the end of the preparation: input manifest identical, reference manifest 50 of 50 runs identical (30 Hz, schema 1)** |

At the end of the preparation (default still 30 Hz) in addition: `Simulation.Tests` without options 513 of 513, `Game.Tests --only SourceRuleTests,TimeScaleTests,CampaignTests` passed.

**Only the default has moved** (binary *after* the switch, default 60 Hz, against the checked-in 30 Hz files):

| Command | Result |
|---|---|
| `input-manifest check <content> baselines/input-manifest.json` | "Input manifest (30 Hz, schema 1): identical." |
| `reference-manifest check <content> baselines/reference-manifest.json --set full --jobs 5` (without `--tick-rate`; the rate comes from the file) | "Reference manifest (full, 30 Hz, schema 1): 50 runs identical." |
| the same with `--set quick --tick-rate 30` | "Reference manifest (quick, 30 Hz, schema 1): 9 runs identical." |
| the same with `--tick-rate 60` | refused: "… records 30 Hz; --tick-rate 60 is refused. A manifest is checked at the rate it was written at." |

**Does the 30 Hz file need rewriting before the switch? No.** `check` checks it in its schema 1 unchanged. Additional evidence that schema 2 touches no result: with the same binary a 30 Hz manifest in schema 2 was written into the scratch directory (`reference-manifest write … --set full --tick-rate 30`); compared against the checked-in file, header, list of runs, `kind` and `set` are equal, **the `results` of all 50 runs equal**, every parameter from schema 1 is present with the same value under its schema 2 name, and exactly `maxGameSeconds`, `gameSeconds` and `budgetCounts` have been added. The input manifest in schema 2 at 30 Hz differs from the checked-in file in exactly two places: `schema` 2 and `simulation.tickRate` 30 (`diff`). Whoever nevertheless wants the 30 Hz files in schema 2 before the switch writes them with `reference-manifest write … --tick-rate 30` and `input-manifest write … --tick-rate 30`; it is not necessary.

#### 15.6.2 Test programs with the switch applied

Release build, real data `game/content`; counted are test lines from the individual logs (passed / failed). "without option" is now 60 Hz.

| Program | without option (60 Hz) | `--tick-rate 30` | `--tick-rate 120` |
|---|---|---|---|
| `Archives.Tests`, `Content.Tests`, `IO.Tests`, `Images.Tests`, `Formats.Tests`, `Import.Tests` | passed | passed | passed |
| `Data.Tests` without and with `--content-dir` | passed | passed | passed |
| `W3D.Tests --content-dir`, `Audio.Tests --content-dir` | passed | passed | passed |
| `Simulation.Tests` | 513 / 0 | 513 / 0 | 513 / 0 |
| `Simulation.Tests --content-dir` | 542 / 0 | 542 / 0 | 542 / 0 |
| `Simulation.Tests -c Release -- --long` | 514 / 0 | 514 / 0 | 514 / 0 |
| `Ui.Tests` without and with `--content-dir` (five suites) | passed | passed | passed |
| `Presentation.Tests` | 155 / 0 | 155 / 0 | 155 / 0 |
| `Presentation.Tests --content-dir` | 172 / 0 | 172 / 0 | 172 / 0 |
| `Game.Tests` (Release) `--content-dir` | 162 / 0 | 162 / 0 | 162 / 0 |

The nine programs of the first three rows build no world; they do not know `--tick-rate` and ignore the option (the runs "30" and "120" are the same run there). The rate-equivalence suite in `Simulation.Tests --content-dir` reports 0 quantities outside the tolerance in all ten groups at 60 and at 120 Hz. The reference match of the simulation tests (catalog without model geometry, budget now two hours of game time) ends with a winner at every rate: FactionGLA at tick 14,130 / 29,130 / 49,560 (7:51 / 8:06 / 6:53 min) – at 120 Hz this test was red with the tick budget (`docs/rate-invariance/RATE-SWITCH-OUTER-TESTS.md` §7.4).

**No test fails because a golden value was recorded at 30 Hz.** The checked-in golden files (`tests/CnCNew.Game.Tests/baselines/screens.txt` with 54 hashes, `hud-drawlist-1280x720.sha256`) and the golden constants in the test code (`DrawListGolden`, bone table D1, triangle count of the roads) pass unchanged at all three rates. There is therefore **no** commit "golden values at 60 Hz".

Clean build with the switch: `dotnet build CnCNew.slnx` with deleted `bin`/`obj` of `CnCNew.Client.Stride` 0 warnings, 0 errors; `dotnet build tools/CnCNew.UiRenderTool` 0 warnings, 0 errors.

#### 15.6.3 Trial manifest at 60 Hz (only in the scratch directory)

**Superseded (9 October 2026).** The trial values of this section and the runs in §15.6.4 come from the state of the switch preparation, **before** round 4 (`1b82e45`). The checked-in 60 Hz manifests (`ea52f87`) were written after that and carry different values: reference match winner FactionGLA at tick 28,260 (frame 14,130) instead of 29,130, final hash `CA05181C36C62C7A` instead of `50A37479CA9367CF`, 16 of 16 AI runs with the same outcome as at 30 Hz instead of 13. Authoritative is §15.7; the tables stay as a record of the intermediate state. Still valid unchanged are the values of the stress test (`9EE9F730D124C3BD` / `F338463BE66D99C0`).

`reference-manifest write <content> <scratch>\reference-manifest-60.json --set full --jobs 5` (818 s, no run with a false repeat flag) and `input-manifest write <content> <scratch>\input-manifest-60.json`; after that `check` against the trial files: input manifest "(60 Hz, schema 2): identical", reference manifest **twice 50 of 50 runs identical** (closing line of both runs: "Reference manifest (full, 60 Hz, schema 2): 50 runs identical."; in addition the quick set and the input manifest with a binary built from the switch commit: 9 of 9 identical, identical). Header of the trial file: `tickRate` 60, `schema` 2, versions unchanged (`commandTableVersion` 15, `replayFormatVersion` 12, `worldSnapshotVersion` 10, `missionSnapshotVersion` 3).

| Run | 30 Hz (checked in) | 60 Hz (trial) |
|---|---|---|
| `reference-match` | winner FactionGLA, tick 14,130 (7:51.0 min), final hash `5BF9BB7CED6D7A35`, chain `56149C0105E992D7`, replay 14,431 ticks | winner FactionGLA, tick 29,130 (8:05.5 min), final hash `50A37479CA9367CF`, chain `A02EF82C24AFCE0C`, replay 29,731 ticks |
| `stress` (216,000 steps) | final hash `56882F763EABEF67`, chain `754FA4DCB9A41DF7` | final hash `9EE9F730D124C3BD`, chain `F338463BE66D99C0` |
| 16 AI runs | 14 decided | 14 decided |
| 4 eight-player runs | all stalemate at the budget | all stalemate at the budget |
| `skirmish-sweep` | 64 started and finished, 0 failed, 0 with a winner, 64 at the budget; 75 maps without, 11 with one start position; `493C72CF9D2AE2E9…` | the same counters; `E21219D7804BB22B…` |
| 27 mission runs (100 s) | all without an outcome in the budget | all without an outcome in the budget; all three repeat flags true on 27 of 27 |

The 16 AI runs and the eight-player runs (winner, game time of the victory min:s, victory tick):

| Run | 30 Hz: outcome, game time | 60 Hz: outcome, game time | Final hash 60 Hz |
|---|---|---|---|
| `AlpineAssault_America-China` | stalemate | stalemate | `58A6A55F05EADD4F` |
| `AlpineAssault_China-GLA` | China 4:46.5 (tick 8595) | China 4:46.5 (tick 17190) | `2ED97C970A583EFC` |
| `AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral` | ChinaTankGeneral 10:10.0 (tick 18300) | ChinaTankGeneral 9:32.0 (tick 34320) | `047D1B00C7441E77` |
| `AlpineAssault_GLA-America` | America 5:22.0 (tick 9660) | America 5:22.0 (tick 19320) | `C897120A7DAC6A30` |
| `ArmoredFury_America-China` | America 20:35.0 (tick 37050) | stalemate | `C2D04A71C519EC23` |
| `ArmoredFury_China-GLA` | stalemate | China 21:54.5 (tick 78870) | `BE1D51852D99DD51` |
| `ArmoredFury_ChinaTankGeneral-AmericaAirForceGeneral` | ChinaTankGeneral 6:02.0 (tick 10860) | ChinaTankGeneral 6:02.0 (tick 21720) | `64ACBBC886C95BE7` |
| `ArmoredFury_GLA-America` | America 20:06.0 (tick 36180) | America 14:10.0 (tick 51000) | `25CC536DD88592E3` |
| `BitterWinter_America-China` | China 12:13.5 (tick 22005) | America 11:33.5 (tick 41610) | `F79B173AAE20CA02` |
| `BitterWinter_China-GLA` | China 4:39.5 (tick 8385) | China 4:39.5 (tick 16770) | `D6BF8631997D9F8F` |
| `BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral` | ChinaTankGeneral 16:45.0 (tick 30150) | ChinaTankGeneral 12:34.5 (tick 45270) | `64E866CABEC459EE` |
| `BitterWinter_GLA-America` | America 4:51.0 (tick 8730) | America 4:51.0 (tick 17460) | `090185887CFBA7B8` |
| `DeathValley_America-China` | America 4:50.0 (tick 8700) | America 4:50.0 (tick 17400) | `4137ABC50FED8435` |
| `DeathValley_China-GLA` | China 7:11.0 (tick 12930) | China 7:11.0 (tick 25860) | `41EE8526DBCCC99C` |
| `DeathValley_ChinaTankGeneral-AmericaAirForceGeneral` | ChinaTankGeneral 12:42.0 (tick 22860) | ChinaTankGeneral 8:41.0 (tick 31260) | `024ECBF625F3AFA1` |
| `DeathValley_GLA-America` | America 5:51.0 (tick 10530) | America 5:51.5 (tick 21090) | `D2E97F00A86D8109` |
| `ai8_FortressAvalanche` | stalemate | stalemate | `480A8CD2BFBC7EC6` |
| `ai8_IronDragon` | stalemate | stalemate | `BADAAA6219710424` |
| `ai8_TwilightFlame` | stalemate | stalemate | `3CD82FBEF25B2E25` |
| `ai8_TwilightFlame_short` | stalemate | stalemate | `B5BBA63A4540130D` |

The 27 mission runs end at both rates after 100 s of game time without an outcome (no final script in the budget; `outcomeScript` empty at both rates). Executed scripts and actions are equal on 27 of 27 runs, the shot count on 26 of 27 (`MD_GLA05`: 643 against 640).

| Mission | 30 Hz: outcome, end, executed scripts/actions, shots | 60 Hz: outcome, end, executed scripts/actions, shots | Final hash 60 Hz | equal after save/load |
|---|---|---|---|---|
| `GC_AirGeneral_AmericaLaserGeneral` | none, tick 3000 (1:40.0), 58/588, 0 | none, tick 6000 (1:40.0), 58/588, 0 | `18E38A8D23C8F813` | yes |
| `GC_AirGeneral_ChinaInfantryGeneral` | none, tick 3000 (1:40.0), 58/588, 0 | none, tick 6000 (1:40.0), 58/588, 0 | `1392094860086813` | yes |
| `GC_AirGeneral_GLAStealthGeneral` | none, tick 3000 (1:40.0), 58/588, 0 | none, tick 6000 (1:40.0), 58/588, 0 | `C9B00717D81E7E8F` | yes |
| `GC_ChemGeneral_AmericaAirForceGeneral` | none, tick 3000 (1:40.0), 83/783, 0 | none, tick 6000 (1:40.0), 83/783, 0 | `4CAD90734E13F02A` | yes |
| `GC_ChemGeneral_ChinaTankGeneral` | none, tick 3000 (1:40.0), 83/783, 0 | none, tick 6000 (1:40.0), 83/783, 0 | `68388B1EA399E017` | yes |
| `GC_ChinaBoss_AmericaAirForceGeneral` | none, tick 3000 (1:40.0), 12092/12720, 0 | none, tick 6000 (1:40.0), 12092/12720, 0 | `555C096C0E6D724A` | yes |
| `GC_ChinaBoss_AmericaLaserGeneral` | none, tick 3000 (1:40.0), 12092/12720, 0 | none, tick 6000 (1:40.0), 12092/12720, 0 | `EC07A3B90506FE40` | yes |
| `GC_ChinaBoss_AmericaSuperWeaponGeneral` | none, tick 3000 (1:40.0), 12092/12720, 0 | none, tick 6000 (1:40.0), 12092/12720, 0 | `154593BB1A1B269F` | yes |
| `GC_ChinaBoss_ChinaInfantryGeneral` | none, tick 3000 (1:40.0), 12092/12720, 0 | none, tick 6000 (1:40.0), 12092/12720, 0 | `EE8AFCB3B10A37A2` | yes |
| `GC_ChinaBoss_ChinaNukeGeneral` | none, tick 3000 (1:40.0), 12092/12720, 0 | none, tick 6000 (1:40.0), 12092/12720, 0 | `C1CDBE60D4670542` | yes |
| `GC_ChinaBoss_ChinaTankGeneral` | none, tick 3000 (1:40.0), 12092/12720, 0 | none, tick 6000 (1:40.0), 12092/12720, 0 | `507C1049CCB05E1A` | yes |
| `GC_ChinaBoss_GLADemolitionGeneral` | none, tick 3000 (1:40.0), 12092/12720, 0 | none, tick 6000 (1:40.0), 12092/12720, 0 | `1B3F8D6817B186A3` | yes |
| `GC_ChinaBoss_GLAStealthGeneral` | none, tick 3000 (1:40.0), 12092/12720, 0 | none, tick 6000 (1:40.0), 12092/12720, 0 | `0004F5E35205A146` | yes |
| `GC_ChinaBoss_GLAToxinGeneral` | none, tick 3000 (1:40.0), 12092/12720, 0 | none, tick 6000 (1:40.0), 12092/12720, 0 | `9154939ADA5177C9` | yes |
| `GC_LaserGeneral_ChinaNukeGeneral` | none, tick 3000 (1:40.0), 123/582, 0 | none, tick 6000 (1:40.0), 123/582, 0 | `134C5C65640E2FAB` | yes |
| `GC_SuperWeaponsGeneral_GLADemolitionGeneral` | none, tick 3000 (1:40.0), 97/593, 0 | none, tick 6000 (1:40.0), 97/593, 0 | `620A01483ABDE0AF` | yes |
| `GC_TankGeneral_AmericaSuperWeaponGeneral` | none, tick 3000 (1:40.0), 3035/550, 0 | none, tick 6000 (1:40.0), 3035/550, 0 | `680039C27D4F14DC` | yes |
| `GC_TankGeneral_GLAToxinGeneral` | none, tick 3000 (1:40.0), 3035/550, 0 | none, tick 6000 (1:40.0), 3035/550, 0 | `04D2BC1EA433815A` | yes |
| `MD_CHI01` | none, tick 3000 (1:40.0), 65/3357, 0 | none, tick 6000 (1:40.0), 65/3357, 0 | `4CF02E68E0400F5A` | yes |
| `MD_CHI05` | none, tick 3000 (1:40.0), 151/487, 2926 | none, tick 6000 (1:40.0), 151/487, 2926 | `2A2C8136A280E89B` | yes |
| `MD_GLA01` | none, tick 3000 (1:40.0), 133/275, 1231 | none, tick 6000 (1:40.0), 133/275, 1231 | `CCE1AB28809422B7` | yes |
| `MD_GLA03` | none, tick 3000 (1:40.0), 101/485, 0 | none, tick 6000 (1:40.0), 101/485, 0 | `CFA0CD3B1304A2FD` | yes |
| `MD_GLA05` | none, tick 3000 (1:40.0), 100/381, 643 | none, tick 6000 (1:40.0), 100/381, 640 | `9138F6E3D49214A1` | yes |
| `MD_USA01` | none, tick 3000 (1:40.0), 107/225, 127 | none, tick 6000 (1:40.0), 107/225, 127 | `CADD74730A3FA0A7` | yes |
| `MD_USA02` | none, tick 3000 (1:40.0), 79/196, 63 | none, tick 6000 (1:40.0), 79/196, 63 | `F0B92F6C1127B52E` | yes |
| `MD_USA05` | none, tick 3000 (1:40.0), 109/394, 0 | none, tick 6000 (1:40.0), 109/394, 0 | `4DC86DD2F74A5706` | yes |
| `Training01` | none, tick 3000 (1:40.0), 46/48, 181 | none, tick 6000 (1:40.0), 46/48, 181 | `08ECC3A089265ADA` | yes |

**Comparison of the result quantities** (`reference-manifest results <content> --out <datei> --set full`, once without an option and once with `--tick-rate 30`; files `results-30.json` and `results-60.json` in the scratch directory, 50 runs each, none failed; the same comparison over the two manifests gives the same table). Of 49 runs with an outcome field, **46 have the same outcome** and **40 the same outcome at the same game time within one original frame**:

- equal up to one original frame: `stress`, all 27 mission runs, all 4 eight-player runs and 8 of the 16 AI runs (the same game time of the victory, or both stalemate);
- the same winner at a different game time (6): `reference-match` (+14.5 s), `DeathValley_GLA-America` (+0.5 s), `AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral` (−38 s), `DeathValley_ChinaTankGeneral-AmericaAirForceGeneral` (−241 s), `BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral` (−250.5 s), `ArmoredFury_GLA-America` (−356 s);
- different outcome (3): `ArmoredFury_America-China` (America after 20:35 against stalemate), `ArmoredFury_China-GLA` (stalemate against China after 21:54.5), `BitterWinter_America-China` (China after 12:13.5 against America after 11:33.5 – a change of winner).

Further result quantities from `results`: in the `skirmish-sweep`, after 100 s **all 64 maps** have the same object count and per player the same possessions and account balance at both rates; in the 27 mission runs, forwarded actions, object count and possessions/money per player are equal on 27 of 27, the event count per kind on 17 of 27; of the 20 AI runs, 8 have the same command count and the same final state per player (the 8 that also end at the same game time). The stress test is by construction a different run at 60 Hz (914,394 against 914,403 commands, peak 15,775 against 15,797 objects); the reference match has 26,626 against 28,930 commands and 13,589 against 14,040 shots of the scenario.

The nine deviating runs are **not traced to the first deviating quantity**. They are the long matches (the shortest deviating one ends after 5:51 min); according to `docs/rate-invariance/RATE-SWITCH-OUTER-TESTS.md` §7.3 a match of the original AI deviates from frame 10,532 (351 s) in the choice of two target spots, and the distribution comparison over 320 matches per rate (§15.4) lies within its limits fixed in advance. Whether the cluster of shorter victory times of ChinaTankGeneral against AirForce at 60 Hz (three of four maps) is chance is not decided here; **balance is not evaluated**.

#### 15.6.4 Campaign, scenes and client at 60 Hz (default rate, no `--tick-rate`)

- **`CampaignTool drive`**, 24 maps, one process each, four side by side, default budget 2,000 s (120,000 ticks): **victory 24 of 24, defeat 24 of 24** on the own final script (counted from the 24 individual logs).

| Map | Victory: final script @ tick | Game time | Defeat: final script @ tick | Game time |
|---|---|---:|---|---:|
| GC_AirGeneral | `END_Final_GLA` @ 540 | 0:09 | `Lose_END_Final_GLA` @ 300 | 0:05 |
| GC_ChemGeneral | `END_Final_USA` @ 540 | 0:09 | `Lose_END_Final_USA` @ 300 | 0:05 |
| GC_ChinaBoss | `End the mission` @ 720 | 0:12 | `Lose_END_Final_USA` @ 4862 | 1:21 |
| GC_LaserGeneral | `END_Final_USA` @ 480 | 0:08 | `Lose_END_Final_USA` @ 300 | 0:05 |
| GC_NukeGeneral | `END_Final_USA` @ 480 | 0:08 | `Lose_END_Final_USA` @ 300 | 0:05 |
| GC_Stealth | `END_Final_USA` @ 480 | 0:08 | `Lose_END_Final_USA` @ 300 | 0:05 |
| GC_SuperWeaponsGeneral | `END_Final_USA` @ 480 | 0:08 | `Lose_END_Final_USA` @ 300 | 0:05 |
| GC_TankGeneral | `END_Final_USA` @ 480 | 0:08 | `Lose_END_Final_USA` @ 300 | 0:05 |
| MD_CHI01 | `END_Final_` @ 602 | 0:10 | `Lose_END_Final_` @ 482 | 0:08 |
| MD_CHI02 | `END_Final_` @ 602 | 0:10 | `Lose_END_Final_` @ 602 | 0:10 |
| MD_CHI03 | `Cheer1Done` @ 3444 | 0:57 | `Lose_END_Final_` @ 8942 | 2:29 |
| MD_CHI04 | `END_Final_` @ 96516 | 26:48 | `Lose_END_Final_` @ 546 | 0:09 |
| MD_CHI05 | `CINE_ENDVictory` @ 5324 | 1:28 | `Lose_END_Final_` @ 482 | 0:08 |
| MD_GLA01 | `Cheer1Done` @ 1052 | 0:17 | `Lose_END_Final_` @ 482 | 0:08 |
| MD_GLA02 | `END_Final_` @ 480 | 0:08 | `Lose_END_Final_` @ 1144 | 0:19 |
| MD_GLA03 | `CINE_ENDVictory` @ 8066 | 2:14 | `Lose_END_Final_` @ 484 | 0:08 |
| MD_GLA04 | `CINE_END` @ 2044 | 0:34 | `Lose_END_Final_` @ 482 | 0:08 |
| MD_GLA05 | `CINE_END04END` @ 2788 | 0:46 | `Lose_END_Final_` @ 2072 | 0:34 |
| MD_USA01 | `END_Final_USA` @ 1570 | 0:26 | `Lose_END_Final_USA` @ 422 | 0:07 |
| MD_USA02 | `END_Final_` @ 5436 | 1:30 | `Lose_END_Final_` @ 25202 | 7:00 |
| MD_USA03 | `CINE_AVVictory` @ 3018 | 0:50 | `Lose_END_Final_` @ 482 | 0:08 |
| MD_USA04 | `END_Final_USA` @ 606 | 0:10 | `Lose_END_Final_USA` @ 1234 | 0:20 |
| MD_USA05 | `END_Fade 04` @ 2886 | 0:48 | `Lose_END_Final_` @ 2560 | 0:42 |
| Training01 | `Player Wins` @ 840 | 0:14 | `Player Loses` @ 5310 | 1:28 |

- **`CampaignTool audit`** (default budget 60 s = 3,600 ticks), one process per map: 24 of 24 identical in the second run, 24 of 24 identical after save/load, 0 hits on missing action and condition types; elimination end probes **victory 13 of 24, defeat 15 of 24** (as at 30 Hz).
- **`CampaignTool reachability`**: victory clean 24, defeat clean 24.
- **`scripts/audit-scenarios.ps1`** (3 scenes) and **`-Scenario exits`** (9 scenes): all twelve "RESULT: all checks passed", run twice, the twelve output files of both runs byte-identical.
- **`scripts/check-spectator.ps1`**: PASS (match ends at tick 17,101 = 285 s with winner player 1). **`scripts/check-dozer-build.ps1`**: 5 of 5 PASS. **`scripts/check-build-hotkeys.ps1`**: 3 of 3 PASS.
- **Client smoke test** `--smoke-test --match "Maps/Alpine Assault/Alpine Assault.map" --autoplay --frames 600`: exit code 0, "session: simulation rate 60 Hz", 600 drawn original frames, the mixer renders 20.00 s. The world tick is not printed in skirmish mode; in mission mode (`--mission "Maps/GC_ChemGeneral/GC_ChemGeneral.map" --frames 600`) the client reports "600 frames, world tick 1200 at 60 Hz, session steps 1200".

#### 15.6.5 Golden values and checksums: what depends on the rate, where it is, what recreates it

Checked-in files with golden values that depend on the default rate are exactly two – and the project owner writes both:

| File | Depends on the rate | Regenerate with |
|---|---|---|
| `baselines/reference-manifest.json` | yes (every run) | `dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest write game/content baselines/reference-manifest.json --set full --jobs 5` (without `--tick-rate`: default rate) |
| `baselines/input-manifest.json` | yes (world fingerprint and catalog hashes; maps and definitions not) | `… -- input-manifest write game/content baselines/input-manifest.json` |
| `baselines/compatibility.json` | no (INI compatibility) | – |
| `tests/CnCNew.Game.Tests/baselines/screens.txt`, `hud-drawlist-1280x720.sha256` | no, measured at 30, 60 and 120 Hz | – |

Checksums that are only in document tables (no test reads them) and are different at 60 Hz; here the values of the default rate, each generated twice and byte-identical:

| Value | Is in | Command | Value at 60 Hz (SHA-256 of the whole output, first 16 digits) |
|---|---|---|---|
| `scenario-audit capture-building` | `docs/TICKRATE-MIGRATION.md` §14.2 (comparison table of Phase 21, 30 Hz: `BC7DD2A2F453D2EC…`) | `pwsh scripts/audit-scenarios.ps1` (writes `<OutDir>\<szene>.txt`) | `B2557B4A970F7512` |
| `scenario-audit tank-duel` | §14.2 (30 Hz at that time: `8FD85E76B599D514…`) | as above | `25BC3C786F8C3837` |
| `scenario-audit jet-strafe` | §14.2 (30 Hz at that time: `67DE0DFB83346317…`) | as above | `163DB77C14A0A257` |
| nine `exit-<fraktion>-<gebäude>` | – (not tabulated so far) | `pwsh scripts/audit-scenarios.ps1 -Scenario exits` | america: `C5CC32D01893815B` (barracks), `DA3F60F789A09001` (commandcenter), `B338020853A0437F` (warfactory); china: `A45494EBAEEC4085`, `5D9A45FF9EA02CF5`, `0A935EA49EA0379B`; gla: `4BB751EE1C437CBB`, `E92AFB4EACE5C9B5`, `17604EE390E972A4` |
| `sync-perf --digest` | `docs/PERF-GAME-LAYER.md` (before/after comparison, no fixed value) | `dotnet run -c Release --project tools/CnCNew.PresentationTool -- sync-perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --hud --digest` (default budget now ten minutes of game time: 36,000 ticks) | `EE731E8D792C759A…`, no winner in the budget |
| manifest of `scripts/capture-world.ps1` (19 scenes), filmstrip of `scripts/capture-scenarios.ps1` (77 frames) | temporary manifests of the scripts (described in `docs/CLIENT-OBJECTS.md`, `docs/DEMO-SCENARIOS.md`), not checked in | the two scripts without `-TickRate` | **not regenerated** (road finding `docs/rate-invariance/RATE-SWITCH-OUTER-TESTS.md` §7.7 to be decided first) |
| `fx-audit` | `docs/PRESENTATION-FX.md` (checksum of the execution pass) | names `SimulationRate.Original` itself | stays at 30 Hz |

The file names of the audit outputs do not contain the path of the content directory, the outputs themselves name the rate in the header line; the checksums hold for the calls above with `-ContentDir` pointing at the checked content. The document tables themselves are not replaced in this package.

#### 15.6.6 Not done, not verified

- The nine manifest runs whose outcome or victory time deviates between 30 and 60 Hz are not traced to the first deviating command line (`reference-manifest results --commands` was not run).
- `capture-world.ps1`, `capture-scenarios.ps1`, `capture-text.ps1` and the `UiRenderTool` were not run with the switch (the `UiRenderTool` is only built); the demo scripts were not started.
- `perf-profile.ps1` is only checked for syntax after the rework (parser without errors), no measurement run; there is no performance measurement in this package.
- `Game.Tests` without `--content-dir`, the Debug build of `Game.Tests` and `--determinism-full` were not run. The determinism matrix (other runtime switches, Linux) is not repeated at 60 Hz.
- The commands-only agent (`drive --commands-only`) was not run at 60 Hz.
- A 30 Hz savegame in a client of the default rate is refused (§15.5.3); a shell function that loads a state with its rate does not exist.
- The upper limit of a default skirmish of one hour of game time (`SkirmishMatchOptions.Standard.MaxTicks`) also applies to a match of a human in the client; whether that is intended is not a question of the rate and not decided here.
- No picture, no sound and no gameplay at 60 Hz has been checked by a human.

### 15.7 Rounds 3 and 4, switch, state at the end of the phase (9 October 2026, state `f3acc2e`)

**Up front, so that nobody overlooks it.** No human has seen or heard the game at 60 Hz. On 8 October 2026 the project owner watched **one** AI skirmish in spectator mode at 30 Hz – before the AI and factory-exit corrections that followed from it. Nothing about the spectator mode, the factory exits, the scroll speed, the mouse mapping or the build keys has been accepted by a human. "The suite passes" means: the measured quantities of the cases of the suite lie within the tolerances fixed in advance at 60 and 120 Hz – not that every match is identical (the measured remainder is below). The equality of whole AI matches is bought with a self-acquisition per original frame of the unit, which the brief forbade by its wording (decision 1).

#### 15.7.1 The rounds

Quantities outside the tolerance at 60 Hz / at 120 Hz. The suite had 4,307 quantities up to the factory exit, 4,388 after it (nine exit cases with 81 quantities in `economy`). No tolerance and no test limit was changed in the phase. Distribution comparison: `ai-survey` with 320 matches per rate (16 pairings × 20 seeds, 25 game minutes), `rate-distribution` 30 against 60 Hz; logs in the scratchpad of the session (`survey\r0` to `r3`).

| State | Commit | Suite | Distribution comparison 30 against 60 Hz |
|---|---|---|---|
| Start | `91cb830` | 479 / 625 | 38 of 39 passed; decided 253 against 224 of 320, z = −2.63 (limit 1.96) |
| Round 1: seven packages | `62fbb6a` | 63 / 64 | 39 of 39; 253 against 244, z = −0.85; D = −0.10 min (SE 0.37) |
| Round 2: consolidation | `3b44506` | 35 / 33 | 39 of 39; 253 against 245, z = −0.76; D = −0.11 min (SE 0.39) |
| Decision of the project owner: abort criterion lifted | `37b36b8` | – | – |
| 30 Hz defect "lost wake request" (changes 3 mission runs; manifest `6a99102`) | `5abbeea` | 34 / 32 (`timed` 0 / 0) | not collected |
| Factory exit with two follow-up commits (changes 23 runs at 30 Hz; manifest `8a6e5b7`) | `c821fbd` | outside `ai` the same 14 rows as before; `ai` 27 / 26 | not collected |
| Round 3, flight (with the parachute drop, a 30 Hz defect that changes no manifest run) | `929f629` | `flight` 0 / 0, `turrets` 0 / 0 | not collected |
| Round 3, observers | `502e75b`, `4d9d9e5` | **0 / 0 in all ten groups** | Information: 80 matches per rate, 54 of 80 end with the same outcome within one second for the same seed |
| AI and movement corrections after the first viewing (change 40 runs at 30 Hz; manifest `cca6f92`) | `0fe9923` | 0 / 0 | new 30 Hz reference needed: 305 against 301 of 320 decided, 39 of 39, z = −0.71; D = −0.009 min (SE 0.286); same outcome within one second for the same seed: 188 of 320 |
| Test programs at 30/60/120 Hz | `203806e` | 0 / 0 | – |
| Round 4: first frame, whole matches on shipped maps | `1b82e45` | 0 / 0 | same outcome and winner for the same seed **317 of 320**, in addition end within one second **287 of 320** (at the start of the round by the same evaluation 295 and 192); the verdict of `rate-distribution` is not noted in any log for this state |
| Switch of the default, one line | `e8632bb` | – | – |
| Manifests at 60 Hz | `ea52f87` | – | – |

At 30 Hz none of the four rounds changed anything: the full reference manifest was identical after every round (50 of 50), and the 30 Hz survey over 320 matches was byte for byte the same file as before after rounds 1, 2 and 4. Exactly three things changed 30 Hz, each with its own manifest commit (§15.7.3).

The 188 of the row "AI corrections" come from the working notes of the session; the package of round 4 counts 192 at the same state with its own evaluation (`docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` §1.3). Both numbers are given here because neither of them has been recomputed.

#### 15.7.2 The frame convention in five sentences

1. The 30 Hz step is the definition; higher rates subdivide it, they do not integrate anew with a smaller step size.
2. Every object decides in the **first** tick of its own original frame (second-order quantities, state machines, events) and reaches its 30 Hz position with the **last** tick; in between it lies on the straight line to it.
3. Whoever looks at another object in order to decide something reads its position, heading and height at the end of the frame it has begun (`Entity.FrameEnd`, `Entity.Ahead…`); vision is refreshed after the last tick of its frame.
4. "One tick later" is one frame later: new objects, removal after a delayed hit, the first update of a module that decides an event; a march ends in the first tick of its last frame and rolls out the rest.
5. The state for this is only written above 30 Hz – at 30 Hz a frame is a tick, no field is set, snapshots and hashes are byte-identical.

Written out: `docs/SIMULATION-CORE.md` §43 (merged), `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §1.3 (rules 1–8), `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md` §2 (target acquisition), `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` §3 (first frame); every use of the escape "original grid": `docs/SIMULATION-CORE.md` §41.

#### 15.7.3 Three behaviour changes at 30 Hz

| Cause | Merge | Manifest | What changes at 30 Hz | Document |
|---|---|---|---|---|
| Lost wake request: a slow death whose wake request is discarded slept forever | `5abbeea` (correction `71e41d7`) | `6a99102` | 3 of 50 runs (`MD_GLA01`, `MD_USA02`, `MD_USA05`); 28 cutscene objects are now removed; reference match unchanged | `docs/DEFECT-LOST-WAKE.md` |
| Factory exit: produced units drive out; every command replaces the exit path; the AI gives work to a dozer that is driving out | `c821fbd` (`df7443d`, `78dc175`, `7b85160`) | `8a6e5b7` (also input manifest: `ProductionExitData.NaturalRallyPoint`) | 23 of 50 runs; reference match tick 14,130, `5BF9BB7CED6D7A35` / `56149C0105E992D7` (previously 17,805); still 12 of 16 AI runs decided | `docs/SIMULATION-CORE.md` §42 |
| First viewing of an AI skirmish: the AI saves for vehicles, units get a target spot of their own, boarders reach their vehicle, army composition by exchange value | `0fe9923` | `cca6f92` | 40 of 50 runs; **14 instead of 12** of 16 AI runs decided, the outcome differs in 8; reference match and stress test unchanged | `docs/AI-OBSERVED-2026-10-08.md` §11 |

In addition, without a manifest change: the parachute drop of the special power sets down its soldiers (`09b4d21`, in `929f629`; no manifest run uses a delivery with a container).

#### 15.7.4 Final check

Before the switch, at state `1b82e45` (default still 30 Hz), counted from the individual logs of the session (`verify\r4`):

| Check | Result |
|---|---|
| clean build (`CnCNew.slnx` with deleted `bin`/`obj` of the client, `UiRenderTool`) | 0 warnings |
| `Simulation.Tests` (Release) without real data at 30 / 60 / 120 Hz | 513 / 513 / 513 passed |
| `Simulation.Tests --content-dir` at 30 / 60 / 120 Hz | 543 / 543 / 543 passed, including the suite with 0 / 0 in all ten groups (4,388 quantities) |
| `Simulation.Tests -c Release -- --long` | 514 passed |
| `Game.Tests` (Release, real data), `Presentation.Tests` (without and with real data) at 30 / 60 / 120 Hz | passed |
| Archives, Content, Data (also with real data), IO, Images, Formats, Import, W3D, Ui, Audio | passed |
| `IniTool compat-check` | 751 entries |
| `input-manifest check`, `reference-manifest check --set full` against the 30 Hz files | identical; 50 of 50 runs identical |
| `scripts/audit-scenarios.ps1` at 30 Hz and with `-TickRate 60` | 3 of 3 scenes passed each |
| `check-spectator.ps1`, `check-dozer-build.ps1`, `check-build-hotkeys.ps1` | PASS; 5 of 5; 3 of 3 |

After the switch (`e8632bb`) and with the 60 Hz manifests (`ea52f87`; logs `verify\switch`):

| Check | Result |
|---|---|
| `input-manifest check` against the last 30 Hz file and against the new one | "Input manifest (30 Hz, schema 1): identical." and "Input manifest (60 Hz, schema 2): identical."; world fingerprint at 60 Hz `C81DC18A282037CD` |
| `reference-manifest check --set full` against the last 30 Hz file (the same binary, default now 60 Hz) | 50 of 50 runs identical |
| `reference-manifest check --set full` against the new 60 Hz file, **twice** (Windows, Release) | both times 50 of 50 runs identical |
| Linux (WSL2) with the 60 Hz manifest, determinism matrix at 60 Hz | **not measured** (Windows is the only target platform since 9 October 2026; the matrix rows are listed in `docs/DETERMINISM.md` §2c) |

Closing measurements at the published state (`39e41df`, against `game/content`, Windows, Release, 9 October 2026):

| Check | Result |
|---|---|
| `CampaignTool reachability` | victory clean 24/24, defeat clean 24/24 |
| `CampaignTool drive`, 24 maps, 30 / 60 / 120 Hz | victory 24/24 and defeat 24/24 at every rate; deviation 0.00 s on all maps except `MD_USA02`, defeat 421.03 against 420.03 s (−1.00 s at 60 and 120 Hz; not investigated) |
| `CampaignTool audit` at 60 Hz | 24/24 deterministic and identical after save/load; end probes victory 13/24, defeat 15/24 |
| commands-only agent at 60 Hz (`--minutes 34`, three maps) | `MD_GLA04` victory @2,044 (the same frame as at 30 Hz), `MD_CHI01` victory @32,056 (30 Hz: @16,452, i.e. not the same frame), `MD_CHI02` **not reached** in 34 game minutes (as at 30 Hz); the other 21 maps not measured |
| survey 30 against 60 Hz at the final state, 320 matches per rate | 305 against 305 decided, 0 failed; distribution comparison 39 of 39 checks passed; same seed: same outcome and winner 317 of 320, end within 1 s 287 of 320; pooled mean duration 9.328 against 9.430 min |

**New reference values at 60 Hz** (`baselines/reference-manifest.json`, schema 2): reference match winner FactionGLA at **tick 28,260** (original frame 14,130, 7:51 min), final hash `CA05181C36C62C7A`, chain `21ACEDA63325A658`, 26,626 commands; stress test 216,000 steps (one hour of game time at 60 Hz) final hash `9EE9F730D124C3BD`, chain `F338463BE66D99C0`; 16 AI runs **14 decided**; the four 8-player runs stalemate at the budget; 27 mission runs without an outcome in 100 s. Versions unchanged: `WorldSnapshot` 10, Replay 12, `CommandTypes.Version` 15, `MissionSnapshot` 3, `SaveGame` 3. **Superseded:** tick 17,805 / `D0CA5F4EE456D6CE` / `C5EE1090E1C24D7B` (30 Hz, Phase 22) and the 30 Hz intermediate states tick 14,130 / `5BF9BB7CED6D7A35` / `56149C0105E992D7`; the stress test final hash `56882F763EABEF67` now only holds for 30 Hz.

**Last 30 Hz manifest (`cca6f92`) against the 60 Hz manifest (`ea52f87`)**, evaluated for this document from the two files (outcome, winner, time of the victory in game time): **50 of 50 runs with the same outcome, 48 of them at the same game time.** The other two: `AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral` 610.0 against 617.0 s and `BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral` 1,005.0 against 1,089.5 s (the same winner). The reference match has 26,626 commands and the same peak of 258 living objects at both rates. In the 27 mission runs, executed scripts and actions are equal on 27 of 27, the shot count on 26 of 27 (`MD_GLA05`: 643 against 642). The stress test is by construction a different run at 60 Hz (914,394 against 914,403 commands, peak 15,775 against 15,797 objects; §15.5.2).

#### 15.7.5 Performance gate (measured on a quiet machine, 9 October 2026)

8 players on Twilight Flame, 30 game minutes (108,000 ticks) at 60 Hz, two runs: mean 683 / 694 µs per tick (target ≤ 1,000: met), 99 % 1,615 / 1,793 µs (≤ 3,000: met), **maximum 141 / 143 ms (≤ 8 ms: missed)**, catch-up speed 26.7× / 26.5× (≥ 8×: met), no Gen-2 collections, allocation around 8 KB per tick (near 0: no). Table, comparison old against new at 30 Hz and the missions at 60 Hz: `docs/PERFORMANCE.md` §6c.

#### 15.7.6 What is not equal, and what is not verified

Merged from the lists "not done or not checked" of the packages (`docs/rate-invariance/RATE-INVARIANCE-*.md`, `docs/rate-invariance/RATE-SWITCH-OUTER-TESTS.md` §7.7/§7.9, §15.6.6). Every line is measured or explicitly named as not measured.

**Measured and not equal:**

- **2 of 16 pairings of the manifest** end at 60 Hz in a different frame than at 30 Hz (Alpine Assault ChinaTank/AirForce: frame 18,300 against 18,510, 7 s; Bitter Winter ChinaTank/AirForce: 30,150 against 32,685, 84.5 s; the same winner). In 8 of 16 every compared field of every object is equal over the whole match, in three more until after the victory. The points of divergence of the rest (turret of an `AmericaFireBase` in the frame of a look without a result, heading of a Ranger, speed of a Gattling tank) are **not traced down to the symbol**.
- **Survey over 320 matches:** 3 with a different outcome, 33 do not end within one second of the 30 Hz end.
- **120 Hz** is **not measured** for the 16 pairings and for the survey; measured at 120 Hz are the suite, the test programs, the reference match (tick 56,520, the same frame 14,130) and the objective driver.
- **Campaign:** at state `cca6f92` the objective driver reached victory and defeat on all 24 maps at 30, 60 and 120 Hz through the same final script at the same game time, with one exception: defeat on `MD_USA02` 421.03 against 420.03 s (−1.00 s at 60 and 120 Hz), **not investigated**. After round 4 and after the switch the objective driver has not been repeated.
- **One mission run of the manifest** has a different shot count (`MD_GLA05`, 643 against 642).
- **Excluded from the field comparison**, because they run ahead within a frame without touching a decision: `nextWake`, `previousHealth`, health and build progress of a construction site, `weaponReadyTick`, `dockMachine.nextActionTick`, `DynamicShroudClearingRangeUpdate.current`.
- **Within the tolerance, not exact:** points in time that are reached with the last tick of a frame (e.g. Rangers from the barracks "both stand" 13.8333 / 13.8500 / 13.8583 s), Battle Bus "in the air" 2.3667 / 2.3667 / 2.3750 s, ferry and wave (position within 0.001 units).
- **Presentation** (state before round 4, not repeated afterwards): the draw order of the roads depends on the rate (decision 7); staged scenes show half a frame of offset (`[MOVING]` of the Ranger in frame 12 / 11 / 11); `tank-duel` differs in all 29 filmstrip frames, `match-start` in 1 pixel, `mission-start` in 161 – without classification.

**Not converted** (every site can cost a frame at a threshold; at none of them was a first divergence measured in the runs of the phase):

- Movers that still compute `value per tick × r`: Spectre, parachute, flares, particle beam, shot-down jet, the gravity of the physics modules, the group speed, `SpecialAbilityUpdate.TurnToward`.
- Healing slices per tick: `SlavedUpdate` (repair drone) and the regeneration of the occupants in `ContainModules.cs`.
- "First frame" (rule 7) for `DeliverPayloadAIUpdate`, parachute, ferry, dynamic shroud clearing, firestorm, `FireOCLAfterWeaponCooldownUpdate`, enemy-proximity scan and checkpoint.
- Readers at the instantaneous position: the script VM (runs in the first tick and reads objects in phase a fraction ahead), special abilities, `SlavedUpdate`, `IsSignificantlyAboveTerrain` at mines, crates, bridges and `IsAirborne`. Non-publishing movers: train, avalanche, ferry, wave, free fall.
- **Not investigated:** bridges, water, cliffs (the suite only knows the flat map; slopes are measured on the four maps of the manifest); the original AI after round 4 (what was compared is one pairing with one seed at the state before it); a command of a human in the middle of a frame (has no 30 Hz counterpart).

**Performance:** the tick maximum (141–143 ms; one tick with many simultaneous long path searches) and the allocation (around 8 KB per tick) miss their target values (decision 3).

**Not run:** what is listed as not measured in §15.7.4; the demo scripts, `capture-world.ps1` and `capture-scenarios.ps1` at 60 Hz after round 4; `Game.Tests` without real data, in the Debug build and with `--determinism-full` at 60 and 120 Hz.

#### 15.7.7 Decisions that lie with the project owner

The same list is in `docs/MULTIPLAYER-PLAN.md` §7, in `CLAUDE.md` (paragraph "Phase 23") and in the addendum of `docs/handoffs/HANDOFF-PHASE24.md`.

1. **Self-acquisition once per original frame of the unit.** A unit decides its own attack in the first tick of its own frame. The brief of Phase 23 forbade the escape "original grid" for target acquisition; it is done this way provisionally because the equality of whole matches was not achievable otherwise. Cost: a unit that becomes free through a command in the middle of a frame starts its own target search up to 1 tick (60 Hz) or 3 ticks (120 Hz) later. Alternative: decide per tick – then matches differ between the rates, and only the distribution comparison holds. The same question for `PointDefenseLaserUpdate`, `CleanupHazardUpdate` and `DeployStyleAIUpdate.AllowsFire`.
2. **Factory exit "always through the gate, even with an immediate command".** Built and measured, not merged: shifts the balance (America 46 instead of 61 victories of 80, China 32 instead of 23; at that time 10 instead of 12 of the 16 runs decided); the patch is in the scratchpad of the session.
3. **Lowering the tick maximum** (141–143 ms at 60 Hz in the performance gate): needs a behaviour change (shared group path, cell budget per tick or a different search) – reserved by the project owner.
4. **The two faster hash constructions** (vision row by row; object hash with module hashes) – reserved, unchanged.
5. **Command validation against the map bounds** (targets beyond around 524,000 units make the movement overflow) – reserved for Phase 27/28, unchanged.
6. **Solo bonuses of the campaign** – reserved, unchanged.
7. **The draw order of the roads depends on the rate** (`TerrainLayer.CreateRoads` gives every road mesh the same bounds; no correction reproduces every 30 Hz frame).
8. **A default skirmish ends after one hour of game time**, also for a human – is that intended?
9. **The original skirmish AI (three difficulty levels) as the default AI** and its selection in the client and in spectator mode: it costs around 27 times the built-in AI per tick (1.1 ms for 8 players), and it lacks parts of the C++ core; the default is the built-in AI.
10. **The balance after the AI corrections is not evaluated** (ChinaTankGeneral wins 70 of 80 matches against AirForce in the 30 Hz survey of round 3).
11. **The second physical machine** (before Phase 26) – unchanged, open.

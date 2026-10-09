# Rate invariance: turrets, target acquisition and firing rhythm (Phase 23, package TURRETS)

As of: 8 October 2026, branch `worktree-agent-a18c18f52abecd6cb`, base `7b54374`. This document is the log of one of the six parallel correction packages of Phase 23
(`docs/handoffs/HANDOFF-PHASE23.md`, step 3). It describes what this package changed in the simulation so that turrets, fire release, firing stages, the staged capture and the
random draws of its modules are **the same game** at 30, 60 and 120 Hz – measured by the rate-equivalence suite (`docs/SIMULATION-CORE.md` §39) and by three-rate tests on the
synthetic fixture (`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Turrets.cs`). The yardstick is 30 Hz; "frame" means a logic frame of the original (1/30 s), "tick" a step of the
simulation. The shared documents (`TICKRATE-MIGRATION.md`, `SIMULATION-CORE.md`, `SIM-TURRET-AIM.md`, `SIM-FIRING-TRACKER.md`) are **not** touched by this package; at the
places named here they still describe the state before.

**No human has seen any of this.** The statements are measurements of the suite and of the tests, not a visual acceptance.

## 1. Result in numbers

Quantities outside the tolerance (without those unmeasured at every rate), Release, real data `game/content`, `SimTool rate-compare --group …`:

| Group | Quantities | before 60 Hz / 120 Hz (total) | after 60 Hz / 120 Hz (total) | what is left |
|---|---|---|---|---|
| `turrets` | 216 | 28 / 36 (40) | **5 / 1 (5)** | Sentry drone (4 quantities at 60 Hz) and ECM tank (1 quantity) – both not caused by the turret, §5 |
| `weapons` | 1,474 | 43 / 57 (57) | **2 / 11 (11)** | 9 times "first shot until first damage" at 120 Hz (projectile flight, another package), 2 times damage of the fire wall of the Dragon Tank at an equal shot count, §5 |
| `timed` | 383 | 53 / 56 (56) | **44 / 47 (47)** | the three capture cases pass completely; everything else belongs to other packages (charging, lifetimes, slow death, toppling, mines, stealth, train) |

Simulation tests with `--tick-rate` (without real data): at 60 Hz **6 → 4** failures (left are the four hand-calculated projectile trajectories), at 120 Hz **14 → 9** (left: the same four,
plus no. 1, 2, 3, 9 and 10 of the table in `docs/TICKRATE-MIGRATION.md` §15.2 – none from this package). The numbers 4 (flashes of the capture), 11 (turret turn rate, pure test expectation)
and 12–14 (turret sweep) pass at all three rates. At 30 Hz all simulation tests are green.

30 Hz is unchanged: after **every** commit `input-manifest check` identical and `reference-manifest check --set quick` 9 runs identical; at the end `--set full` (§7).

## 2. The principle at the turret: decision per frame, path per tick

The turret of the original makes one step per frame (`TurretAI::friend_turnTowardsAngle`): closer to the target than one step – snap; otherwise one step on the short way. The
fire release checks "within 0.035 rad" (`REL_THRESH`). An Euler step per tick with half the step size reaches the release threshold in a different frame and with a
different angle (Fire Base after 90°: 1.57080 / 1.55334 / 1.54462 rad), and a sweeping turret (`TurretFireAngleSweep`) stands, at the moment of a due shot, at a different
place of its sweep (fire wall of the Dragon Tank: 838 / 899 / 899 shots in 60 s).

What applies now (`SimulationWorld.Turrets.cs`, `TurretState.cs`):

- **The step is decided once per frame, exactly as at 30 Hz** – how far, in which direction, whether it snaps, whether the sweep reverses –, with the turn rate **per frame, as
  the 30 Hz catalogue rounds it**. The turret walks it in equal parts, one per tick (`TurretState.SubSteps`, `StartAngle`/`GoalAngle`, `StartPitch`/`GoalPitch`). At the end of every
  frame the angle is **bit-identically** the 30 Hz angle; in between it lies on the way there.
- **The frame is that of the turret**, not that of the world: it begins with the tick in which the turret decides for the first time (or after a new command). A command that comes in the middle of
  a frame of the world is not held up until the next frame boundary; a **new** victim aborts the running step (`AbandonFrameStep`), the turret turns from the
  next tick from where it stands.
- **Release within a frame** (`IsAligned`): the turret counts as aligned when **beginning and goal** of the running step are. A turret that follows its victim
  thereby fires in every tick; a turret that is still swinging towards it is released when the step is finished – in the same frame and with the same angle as at 30 Hz.
- **Turn rate per frame:** `rate per tick × ticks per frame` deviates by the rounding of the tick value (one or two raw values). That is enough to let a turret that stands exactly one step before its
  target (90° at 6° per frame – every axis-parallel scene of the suite) tip into a different frame. The rates per frame of a catalogue above 30 Hz are therefore in a
  static table **next to** `TurretInfo` (`TurretInfo.RatesPerOriginalFrame`, filled in `TurretSet.Read` with the time service of the original rate) – not a field of the catalogue type, the
  input manifest is untouched; a `TurretInfo` built by hand falls back to `rate per tick × ticks per frame`.
- **Deadlines "up to and including"** (holding until `HoldUntil`, §5.3): the one step more is one frame (`tick < HoldUntil + ticks per frame`). The stored value stays.
- **First step and idle scan:** a new turret is stepped for the first time one tick after the appearance of its object, sets the scan deadline in this step and looks in the
  next step. Above 30 Hz it now waits until the first frame of its life begins (age in ticks smaller than one frame: nothing happens), and the step that sets the deadline
  occupies its frame (`OccupyFrame`: a step without movement). With that the idle scan is bit-identical to 30 Hz at every frame end over 1,500 frames (three-rate test).
- **`TurretWaits`** (part of the hash) now counts frames of waiting: a held-back command counts only in a tick with which a frame of the world begins (15 / 15 / 15 instead of
  15 / 30 / 59 for half a second of waiting).

At 30 Hz a frame is one tick: `SubSteps` is never set and never written, every form of comparison is the earlier one, the rates per frame are the catalogue values.

**New state, written only above 30 Hz:** per turret `subSteps`, `goalAngle`, `goalPitch`, `startAngle`, `startPitch` (only during a running step);
`abilityFacePhase` of the `SpecialAbilityUpdate` (only while turning towards). At 30 Hz snapshot and hash stay byte-identical; at 60/120 Hz the snapshot content changes (there are
no reference values there yet).

## 3. Causes, one per commit

Suite numbers are "quantities outside the tolerance at 60 Hz / at 120 Hz" of the named group immediately before and after the commit; individual values are 30 / 60 / 120 Hz.

| § 5 (`TICKRATE-MIGRATION.md`) | Finding (30 / 60 / 120 Hz before) | Correction (file, symbol, principle) | after | Commit |
|---|---|---|---|---|
| – (measurement of the suite) | Turret angle at the first shot was read after the tick of the shot: one tick share of the turret step later (at 30 Hz the whole step, above that a part) – what was measured was the reading, not the turret. Fire Base 90°: 1.57080 / 1.55334 / 1.54462 rad | `Testing/RateEquivalence/RateCases.Turrets.cs`, `MeasureTurret`: reading at the end of the frame into which the shot fell (`RateScene.Align`); at 30 Hz no further step, every value as before. No tolerance touched | 1.57080 at every rate; `turrets` 28 / 36 → 17 / 28 | `f791682` |
| – (measurement of the suite) | "return: leaving the aim to rest" chained two `StepUntil`; the second begins at an observation, not at a command, but left out the command tick: one tick (4 / 2 / 1 units) was missing | the same file: `rested + 1`. The 30 Hz values grow by one frame (2.9333 → 2.9667 s) | Tomahawk 2.9667 / 2.9833 / 2.9917 s; `turrets` 17 / 28 → 15 / 15 | `2f62a6b` |
| §5.2 `IsAligned`, `AimStep`, `RecenterStep`, `ScanStep`, `TurnAngle`, `TurnPitch` | Euler step per tick, snapping and release against one tick step. Dragon Tank 90°: 1.43117 / 1.44862 / 1.43117 rad; sustained fire in 60 s: Gatling tank 440 / 441 / 441, toxin truck 899 / 900 / 900, fire wall 838 / 899 / 899 | `World/SimulationWorld.Turrets.cs` (`StepTurret`, `BeginFrameStep`, `ContinueFrameStep`, `AbandonFrameStep`, `IsAligned`/`IsAlignedAt`), `World/TurretState.cs` (`SubSteps`, `Start…`, `Goal…`, `Frame…Rate`), `Templates/TurretInfo.cs` (`RatesPerOriginalFrame`): decision per frame, path per tick (§2) | angles equal; 440 / 899 / 838 at every rate; `weapons` 43 / 57 → 13 / 22; `turrets` 15 / 15 → 16 / 19 (the additional tick of the holding becomes visible) | `32eb25c` |
| §5.3 `StepTurrets`/`StepTurret` (holding up to and including `HoldUntil`) | Counter N + 1, the additional step was one tick: 16 / 15.5 / 15.25 frames of holding with `RecenterTime` 15 frames; Fire Base, third look 14.7333 / 14.6833 / 14.6583 s | `SimulationWorld.Turrets.cs`, `StepTurret`: `tick < HoldUntil + ticks per frame` | 16 frames at every rate; third look 14.7333 / 14.7167 / 14.7083 s; `turrets` 16 / 19 → 9 / 11 | `6e0c691` |
| §5.2 `IdleScanStep`, `ScheduleScan`; §5.3 "one step later" | First step of the turret and the step that sets the scan deadline cost one tick each. First look Fire Base 0.6333 / 0.6167 / 0.6083 s, ECM tank (interval 0) 0.0333 / 0 / 0 s | `SimulationWorld.Turrets.cs`, `IdleScanStep` (age against one frame), `OccupyFrame` | 0.6333 and 0.0333 s at every rate, third look 14.7333 s; all idle quantities pass; `turrets` 9 / 11 → 5 / 1 | `276a8de` |
| §5.4 `_turretWaits` (also `AutoFire`, `TryFireActiveWeapon`) | Counts held-back commands per tick: 15 / 30 / 59 | `SimulationWorld.Turrets.cs`, `NoteTurretWait`: counts in ticks with which a frame of the world begins (three call sites: `SimulationWorld.cs` `FireWeaponCommand`, `SimulationWorld.Acquisition.cs` `AutoFire`, `SimulationWorld.WeaponSlots.cs` `TryFireActiveWeapon`) | 15 / 15 / 15 (test); no suite quantity reads the counter | `dc888d3` |
| §5.3 `StepFiringTrackers` (`tick <= CooldownFrame`), `PreAttackDone` (`_tick <= PreFinish + …`) | Acting in the step after the deadline, the step was one tick: stage drops 35 / 34.5 / 34.25 frames after the last shot, slow stage ends after 66 / 65 / 64.5 (calculated) | `World/SimulationWorld.FiringTracker.cs`: both comparisons `< deadline + ticks per frame`. **`PreAttackSlotOf` deliberately unchanged** (§4) | 35 and 66 frames at every rate (test); `weapons` unchanged 13 / 22 (no suite quantity measures the decay) | `165e6d8` |
| new (firing path, `ConsumeShot`) | Clip without reload time (`ClipReloadTime = 0`, the battleship guns) was ready again in the tick of the last shot: the next shot one **tick** later. Reload 0.0333 / 0.0167 / 0.0083 s, shots in 60 s 110 / 112 / 112 | `World/SimulationWorld.WeaponSlots.cs`, `ClipReloadOf`: above 30 Hz one frame of waiting time; at 30 Hz the waiting time stays 0 (the stored ready tick is state) | 0.0333 s and 110 shots at every rate; `weapons` 13 / 22 → 2 / 11 | `20d4766` |
| §5.2 `SpecialAbilityUpdate.StepCaptureFlash` | Flash phase per tick instead of per frame: 4 / 9 / 19 flashes in 2 s (test), 49 / 99 / 199 (suite), first until last flash 16.9667 / 17.9000 / 18.5333 s | `Modules/BuiltIn/SpecialAbilityStaging.cs`, `StepCaptureFlash`: step only in the ticks that complete a frame of the preparation (`_prepFrames % ticks per frame == 0`) | 4 / 4 / 4 and 49 / 49 / 49, 16.9667 s; all three capture cases pass; `timed` 53 / 56 → 44 / 47 | `d923cea` |
| – (test expectation, consequence of `165e6d8`) | "Firing tracker: … The slow stage lasts a second" expected `Frames(30) + 1` ticks | `Tests.FiringTracker.cs`: `Frames(30 + 1)` | passes at 30 / 60 / 120 Hz | `f5653f9` |
| – (test expectation, §15.2 no. 11) | `PiFraction(1, 30)`: constant per tick at 30 Hz | `Tests.Turrets.cs`: `PiFraction(1, TicksPerSecond)` | passes at 30 / 60 / 120 Hz | `b405704` |
| §5.2 `SpecialAbilityUpdate.TurnToward` | Snapping against one tick of turn: turning towards of the Black Lotus 8 frames / 15 ticks (7.5 frames) / – | `SpecialAbilityStaging.cs`, `TurnToward`, `_facePhase`: decision in the first tick of every frame against one frame of turn, in between turn per tick | 8 frames at every rate (test, fails on the old state); `timed` unchanged | `95bcf3d` |
| §5.5 `MoodCheckOffset` | The random offset hashed the tick number: a different sequence at every rate | `World/SimulationWorld.Acquisition.cs`: hash over the frame number (`SimulationTime.FramesIn`); the named exception of the source rule for `tick + 1` is dropped | first shot after the second look in the same frame at every rate, over eight seeds (test) | `146f92d` |
| §5.5 `SlavedUpdate.DoRepairLogic` | Height random draw and movement command per tick in the repair state: 100 / 200 / – draws in 100 frames | `Modules/BuiltIn/SlaveModules.cs`: draw and command in the repair state only in the tick with which a frame of the world begins (escape, §4) | 100 / 100 / 100 (test) | `01121cc`, corrected in `e1252df` |
| §5.5 `WanderAIUpdate.Update` | Two draws and one command per idle tick: 200 / 400 / – draws in 100 frames | `Modules/BuiltIn/SpecialsModules.cs`: the look only in the tick with which a frame of the world begins (escape, §4) | 200 / 200 / 200 (test) | `022bec8` |
| §5.5 `GrantStealthBehavior.Update` | Growth and scan per tick, first scan one tick after the appearance: stealth at 25 / 45 units after 2 / 4 frames at 30 Hz, after 1.5 / 3.5 frames at 60 Hz | `SpecialsModules.cs`: runs above 30 Hz on the frame grid of the marker (ticks that lie a whole multiple of a frame after `SpawnTick`), with the growth of one frame (escape, §4) | the same frames at every rate (test) | `f1c5ceb` |
| §5.4 `StepAcquisition` (grace period of the ordered attack) | Counter N + 1: the attack "on command" ends 46 / 45.5 / 45.25 frames after the last readiness to fire (calculated) | `SimulationWorld.Acquisition.cs`, `StepAcquirer` and `EarliestReadyTick`: `< deadline + ticks per frame` | 55 frames after a single command at every rate (test) | `2d637e1` |
| §5.5 (correction) | `01121cc` also pinned the draw **outside** a repair state to the world grid; there the logic only runs every 7 frames in its own cadence – if that never fell on a frame boundary, the drone never took off ("Slaves: a battle drone repairs its hurt master" at 120 Hz) | `SlaveModules.cs`: the grid applies only in the repair state (there the logic runs per tick) | test at 120 Hz green; 14 or 15 draws in 100 frames outside the state at every rate | `e1252df` |

## 4. The escape "original grid" (Ausweg „Originalraster“) – every use

The escape (logic only on every (rate/30)-th tick) is not permissible for movement, command acceptance and target acquisition and is not used there: turrets turn in every tick, fire commands
are accepted and checked in every tick, `StepAcquisition` and `AutoFire` run in every tick. It is used here:

| Place | What runs on the grid | Grid | Justification |
|---|---|---|---|
| `SlavedUpdate.DoRepairLogic` | the height random draw and the movement command of a drone in the repair state that is not yet close enough | world (`IsOriginalFrameBoundary(world.Tick)`) | a random draw is not divisible; the number of draws per game time has to be equal (rule 5). The movement, the repair states and the healing run per tick |
| `WanderAIUpdate.Update` | the look of an idle wanderer (two random draws, one movement command) | world | as above. The movement runs per tick; a wanderer that arrives in the middle of the frame sets off up to ½ (¾) frame later than at its arrival – in the next frame, as at 30 Hz |
| `GrantStealthBehavior.Update` | growth of the radius and scan (granting of the stealth) | that of the marker (`SpawnTick`) | logic of the original once per frame without an intermediate state: a grant is an event, the radius only the reach of the next scan |
| `SpecialAbilityUpdate.StepCaptureFlash` | the flash phase of the capture | that of the preparation (its tick counter) | recursion per frame (the growth grows from frame to frame), a flash is an event. The preparation itself counts ticks |
| `SimulationWorld.NoteTurretWait` | the counting up of `TurretWaits` | world | pure statistics (part of the hash); no behaviour depends on it |

Not a use of the escape but subdivision according to the principle (decision per frame, progress per tick): turret step (§2), `SpecialAbilityUpdate.TurnToward`.

## 5. What remains open, and why

**In the groups of this package, but not from this package:**

- **`turrets/MISSION_AmericaVehicleSentryDrone`** (4 quantities at 60 Hz: angle at the first shot 1.57080 / 1.55771 / 1.57080 rad, return): the drone has to deploy
  (`DeployStyleAIUpdate`) before it may fire. Its status bit appears at 60 Hz one tick later than twice the 30 Hz tick (tick 65 instead of 64, measured with a sequence log
  of the case), the turret therefore gets its order on an odd tick, comes from a different position and reaches the 2° threshold half a frame earlier. The
  turret itself computes correctly; the cause is the deploy time (not in the files of this package). In addition the suite reads the angle at a frame boundary of the **world**, the turret
  is then in the middle of **its** frame.
- **`turrets/ChinaTankECM`, angle after 180°** (−2.09440 / −1.98968 / −1.98968 rad): the "first shot" for which the case waits is not a shot of the turret. The fire command is still waiting
  at every rate (the counter of the held-back commands keeps running until the shot); a module of the tank fires, 18 frames **and one tick** after the command
  (step 19 / 37 / 73). The case measures the turret angle at this point in time. The cause is the one-tick offset of this module (§5.3 of the list), not the turret.
- **`weapons`, 9 times "first shot to first damage" at 120 Hz**: flight time of the projectiles (package projectiles).
- **`weapons`, damage of the fire wall of the Dragon Tank** (14,605.5 / 13,009.5 / 13,005.5 in 60 s; with upgrade 18,167.6 / 16,313.9 / 16,318.9): the **shot count is now equal**
  (838), the damage is not. The sought cause of the shot count (838 / 899 / 899) was the turret: the weapon sweeps ±60° and waits at the reversal points. The damage arises from
  the flame projectiles and the fire-wall segments that they leave behind; the segments wander (`WanderAIUpdate`), but it is not their random draws (the value hardly changes with
  the correction). Not investigated further; belongs to projectiles or damage over time respectively.
- **`timed`**: 47 quantities, none from this package.

**Deliberately left (§5 of the list):**

- **§5.3 `PreAttackSlotOf`** (`_tick <= PreFinish`): a query between two steps, not an action in a step. Its answer changes after the step of the deadline tick,
  that is the same game time at every rate; with one frame added it showed the wind-up half a frame longer at 60 Hz.
- **§5.4 `KeepsAttackIntent`**: is only evaluated in the decision ticks of the turret, and the deadline counts 45 frames from the tick in which the victim was seen out of range
  – the attack is given up in the same frame as at 30 Hz (the tick within the frame can deviate). The existing three-rate test (45 frames) stays unchanged.
- **§5.4 `StepAcquisition`/`AutoFire` per tick**: stays per tick (requirement). Remaining differences below one frame, named honestly: (a) a unit that becomes idle in the middle of a frame
  or whose victim comes into range in the middle of a frame sees and fires up to ½ (¾) frame earlier than at 30 Hz; (b) the first look of a new unit falls
  two frames after its first step, and that is one **tick** after the appearance (§5.3 `Register`, package TIMING): a unit created on a frame boundary looks at 30 Hz
  in frame 3, at 60 and 120 Hz in frame 2 – and thereby also draws a different random offset; (c) a turret that follows a victim only releases in the middle of the frame when beginning and
  goal of its step are aligned.
- **§5.5 `StickyBombUpdate`, `ProjectileStreamUpdate`**: no random draw, no command; the bomb follows its victim per tick (that is movement and correct), the stream only keeps books.
  Difference below one frame: a bomb on a dead victim and an empty stream disappear up to ¾ frame earlier.
- **§5.5 `DoAttackLogic`/`DoScoutLogic`/`DoGuardLogic`, `MobMemberSlavedUpdate`**: already lie on the check grid (Phase 20), unchanged.

**Rounding:** `TurnToward` of the abilities computes the turn per frame as `rate per tick × ticks per frame` (`LocomotorInfo` carries no value per frame, and a field there would change the
input manifest): a unit that stands exactly a whole number of steps before its target can deviate from 30 Hz by one frame. For the turret this is excluded by the table of the
30 Hz rates. The sub-steps of the turret (one n-th of the remainder) round down; the last tick sets the goal angle exactly.

**Only at 30 Hz:** a clip without reload time can fire once more at 30 Hz through a second command **in the same tick** (waiting time 0, as always); above 30 Hz not.

## 6. New findings for other packages

1. **`DeployStyleAIUpdate`**: status change at 60 Hz one tick later than twice the 30 Hz tick (Sentry drone, §5).
2. **A module of the ECM tank fires 18 frames + 1 tick after the command** (§5); the turret case of the suite measures the module shot for this object, not the turret.
3. **The shroud is refreshed after the first tick of its frame, not after the last** (`SimulationWorld.Step`, `tick % VisionInterval == 0`): everything that reads vision
   (`KeepsAttackIntent`, `IsAttackable`, the check of a fire command) sees a change up to ½ (¾) frame earlier in the same frame above 30 Hz, at 30 Hz only in the next.
4. **Fire wall of the Dragon Tank**: about 11 % less damage at 60/120 Hz at an equal shot count (§5).
5. **The suite compares angles without wrap-around** (−π against +π would be a failure by 2π). Since the turret is bit-identical at frame ends, it no longer occurs in `turrets`.
6. **Run time of the quick set** of the reference manifest fluctuated on this day between about 1 and 7.5 minutes (six packages work in parallel on the machine); not a performance finding.

## 7. Checks

- After each of the commits: `SimTool input-manifest check` identical, `SimTool reference-manifest check --set quick --jobs 3` 9 runs identical, simulation tests green at 30 Hz
  (Release). Exceptions: the two pure test commits `f5653f9` and `b405704` (no simulation change) have no manifest run of their own, they are checked with the run of `95bcf3d`; two rewritten test lines of `6e0c691` are confirmed at 60 Hz only with the run of `276a8de`; `01121cc` passed at 30 and 60 Hz and was only noticed in the 120 Hz run three commits later (correction `e1252df`).
- At the end: at the state `e1252df` `SimTool input-manifest check` identical and `SimTool reference-manifest check --set full --jobs 4` **50 runs identical**; `dotnet build CnCNew.slnx` 0 warnings, 0 errors (incremental build, `bin`/`obj` of `CnCNew.Client.Stride` not deleted); `dotnet run --project tests/CnCNew.Simulation.Tests` (Debug, 30 Hz) all tests green; Release with `--tick-rate 60` 4 failures (the four projectile-trajectory tests), with `--tick-rate 120` 9 (the same four and no. 1, 2, 3, 9, 10 from §15.2). `dotnet run --project tests/CnCNew.Presentation.Tests`: all tests green, **no assertion there changes**; the time-scale test of the capture (`CaptureFlashTests`, `docs/TICKRATE-MIGRATION.md` §14.4, second table) now prints "flashes of one preparation 30 Hz: 4, 60 Hz: 4, 120 Hz: 4" (before 4 / 9 / 19), "the last flash is shown for 0.1667 / 0.1667 / 0.1500 s" and "first flash to the end of the last tint 1.0333 / 1.0500 / 1.0417 s" – the table in §14.4 and the list "three quantities follow the simulation" are superseded in this point (not updated by this package).
- Three-rate tests (`Tests.RateInvariance.Turrets.cs`, 13 tests, name their rates themselves): turret step bit-identical at every frame end and release in the same frame, `TurretWaits`;
  holding one frame more than the recentre time; idle scan over 1,500 frames; decay of the firing stages; grace period of the wind-up; clip without reload time; flashes of the
  capture; turning towards of an ability; random offset of the mood check over eight seeds; random draws of the repair drone and of the wanderer (counted by replaying the
  logic stream); scan of the stealth grant; end of the ordered attack.
- Touched files outside the list of the package (kept small): `World/SimulationWorld.cs` (one line in the constructor, one call `NoteTurretWait`), `World/TurretState.cs`
  and `Templates/TurretInfo.cs` (turret state and rates per frame), `Testing/RateEquivalence/RateCases.Turrets.cs` (two measurement corrections), in the tests `Program.cs` (one block),
  `Tests.SourceRules.cs` (one dropped exception), `Tests.TickRate.World.cs`, `Tests.Turrets.cs`, `Tests.TurretAttack.cs`, `Tests.FiringTracker.cs` (expectations that counted ticks of the
  old model).
- Performance at 30 Hz: per turret step two additional comparisons against a field, no new division; the divisions of the sub-steps and the table lookup of the rates per frame
  run only above 30 Hz (the rates per frame are read once per object at creation). Not measured.

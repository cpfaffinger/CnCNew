# Rate invariance, round 3: observers (Phase 23, package ROUND3-OBSERVERS)

As of 8 October 2026, starting state `37b36b8` (simulation `3b44506`). This document continues `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`: the same frame convention (§1.3 there),
the same method (search for the first deviation of a match, name the reader, put it on the convention, re-measure). "Frame" means a logic frame of the original (1/30 s), "tick" a
step of the simulation, r = ticks per frame (1, 2, 4). Values are given as 30 / 60 / 120 Hz. Two packages work in parallel on other files (FLIGHT-3: winged flight, flight and
special movers, Sentry drone; DEFECT: `CINE_BShipWeaponDetonationObject`/`WakeAt`); their cases are not touched here. **No human has seen any of this.**

## 1. Result

Rate-equivalence suite (`docs/SIMULATION-CORE.md` §39), quantities outside the tolerance at 60 Hz / at 120 Hz, of 4,307:

| Group | Quantities | before (`37b36b8`) | **after** | The remainder belongs to |
|---|---|---|---|---|
| `locomotion` | 837 | 2 / 2 | **0 / 0** | – |
| `flight` | 74 | 0 / 3 | **0 / 2** | package FLIGHT-3 (winged flight at 120 Hz) |
| `weapons` | 1,474 | 0 / 0 | **0 / 0** | – |
| `projectiles` | 743 | 4 / 4 | **0 / 0** | – |
| `turrets` | 216 | 4 / 0 | **4 / 0** | package FLIGHT-3 (`DeployStyleAIUpdate`, Sentry drone) |
| `overtime` | 418 | 0 / 0 | **0 / 0** | – |
| `economy` | 81 | 0 / 0 | **0 / 0** | – |
| `timed` | 383 | 1 / 1 | **1 / 1** | package DEFECT (30 Hz error) |
| `scripts` | 15 | 0 / 0 | **0 / 0** | – |
| `ai` | 66 | 24 / 23 | **0 / 0** | – |
| **Sum** | **4,307** | **35 / 33** | **5 / 3** | |

The three groups of the brief pass at 60 and 120 Hz: `ai` (all 66 quantities of the three pairings are **equal** at 30, 60 and 120 Hz, not only within the tolerance – e.g.
USA against China: 696 / 696 / 696 and 424 / 424 / 424 commands in eight minutes), `locomotion` (mob nexus) and `projectiles` (scatter salvos). Beyond that the three matches
of the group `ai` are, over the whole eight minutes (14,400 frames), **equal at the end of every frame in every described field of every object** between 30 and 60 Hz and between 30 and
120 Hz (§5; with the four exceptions of the comparison rule named there), and the random stream of the logic runs the same.

## 2. The core question: can target acquisition decide per tick and still deliver the 30 Hz result?

**Answer: it can run in every tick, but for a unit in phase only the first tick of its frame may decide something – every further tick of the frame can only repeat the decision of the
first or deviate from 30 Hz.** This is not a finding about the implementation but follows from the demand itself:

1. At 30 Hz the pass (`StepAcquisition`) decides once per frame, after the updates, and thereby sees every object after its whole step.
2. If the pass above 30 Hz reads everything it looks at at the frame end (position `Entity.FrameEnd`, heading `Entity.FrameEndAngle`, "no longer has a move order" in the first tick of the
   last frame, turret at the end of its frame step), then its inputs are the same in all r ticks of a frame. If it decides in the first tick like 30 Hz, nothing remains to be decided for the
   ticks 2 … r: the weapon is no longer ready, the victim is the same, the next look lies on the grid.
3. Where a later tick does decide something else, exactly that is the deviation. Measured at the starting state: a new unit was passed for the first time one **tick** after the appearance
   (at 30 Hz that is one frame), remembered "idle since tick 1" and looked at 60 Hz in tick 5 – at 30 Hz in frame 3, that is tick 6. The random
   shift of the following look is drawn from the frame number: frame 2 instead of frame 3, hence a different value (next look of the two dozers 30 Hz: tick 79; 60 Hz: tick 105
   instead of 158). From then on **every** look of every unit lay off the 30 Hz look. The same after every arrival: the march ended in the last tick of the frame, the pass saw
   "idle" one tick too late and planned the look on an odd tick.
4. A shot that falls in tick 2 of a frame is not a "finer" 30 Hz behaviour but a different game: its hit kills in the same tick; a unit with a higher
   id, which at 30 Hz no longer chooses the dead victim in this frame, has already chosen it in tick 1 at 60 Hz (and vice versa) – different shots, different reloading, a different
   course.

**Implementation** (commit "a unit decides its attack …"): `StepAcquisition` still runs in every tick over all units; a unit is passed in the ticks with which
**its** frames begin (whole frames after the tick of its appearance – the same phase that `EntityFrames.BeginsFrameOf` and the movers use; at 30 Hz every tick), and
thereby reads the frame-end view. This is rule 1 of the convention ("the decision is made in the first tick of the frame of the object") for target acquisition, not the grid of the world.

**What stays per tick:** commands. A fire, pursuit or move command takes effect in the tick in which it is accepted (`ExecuteCommands`), also in the middle of the frame; an
ordered shot is triggered by the command, not by this pass. Movement runs per tick. Turrets decide per frame of the turret (package TURRETS) and swing per tick.

**What it costs, and the alternatives:**

| Variant | What it means | Cost | Verdict |
|---|---|---|---|
| A. chosen: decision in the first tick of the frame of the unit, phase = tick of the appearance | stateless (nothing new in the snapshot) | A unit that becomes free **in the middle of its frame** through a command of a human (stop, arrival of a march ordered in the middle of the frame) begins its *own* target acquisition up to r − 1 ticks later (at most 1/60 s at 60 Hz, 1/40 s at 120 Hz); after that it waits two frames anyway (`ForceIdleMSEC`). Ordered shots are not affected. In the hot path at 30 Hz: one comparison per unit and tick | bit-identical in three pairings over eight minutes at 60 and 120 Hz |
| B. decide per tick, only read the inputs at the frame end | nothing gated | Every place at which the pass continues "in the next tick" (mode change after the end of an attack march, first pass of a new unit, resumption after a battle, state change after `EndEngagement`) would have to be put individually on "in the next frame", each with a state of its own above 30 Hz; a forgotten place shifts all following looks of the unit permanently off the grid | for units in phase observably the same as A, with more state and without a guarantee |
| C. reset the phase of the unit at every command | like A, but the phase is the tick of the last command | one more field in the snapshot above 30 Hz; removes the latency from A | not done: no measured case; the movers already reset their phase at a command, target acquisition would then read a different phase than `BeginsFrameOf` |
| D. pass after the **last** tick of the frame | all positions are then the 30 Hz positions without the frame-end view | every self-chosen shot would fall (r − 1)/r frame later than at 30 Hz; contradicts the time convention of the suite and rule 1 | discarded (like alternative B in `RATE-INVARIANCE-CONSOLIDATION.md` §1.4) |

Variant A is in §4 as a use of the object grid. It is **not** the escape "whole logic only in every (rate/30)-th tick of the world": the phase belongs to the unit, and
a command waits for nothing. It is, however – let that be said clearly – a decision "once per frame" for the *autonomous* target acquisition; whoever reads the wording "target acquisition in every
tick" more strictly finds here the place at which it is not compatible with "the same game as 30 Hz" (points 2 to 4).

## 3. Causes, one commit per row

"First deviation" is the first frame after which, in the match USA against China (30 against 60 Hz), a field of an object deviates for three frames (`rate-compare --ai-diff … --fields`,
§8); the measurement series was taken in this order at the respective cumulative state. Suite numbers are "outside at 60 / at 120 Hz".

| # | Origin (`CONSOLIDATION` §6 or new) | Finding before | Correction (file, symbol, rule from §1.3) | after | Commit |
|---|---|---|---|---|---|
| A | §6 "angle at the frame end" | a shooter that stops in the middle of the frame kept an intermediate heading; its missile launched with heading 0.6289 (30 Hz) against 0.7743 (60 Hz) | `World/Entity.cs` (`AheadAngle`, `HasAheadAngle`, `FrameEndAngle`; `CompleteFrame` also sets the heading), `Phase5Modules.cs` `AdvanceFrame` publishes the heading of the frame end, `SimulationWorld.Projectiles.cs` `FireWeaponAt` launches with it (rule 3) | first deviation (position) 3543 → 3695 | `e83d88d` |
| B | §6 "look of target acquisition"; new: phase of target acquisition | first pass of a new unit one tick instead of one frame after the appearance: next look of the dozers after frame 2 at tick 3 (30 Hz) against tick 5 (60 Hz, would be 6); the look measured momentary positions | `SimulationWorld.Acquisition.cs`: `StepAcquisition` passes a unit in the ticks with which its frames begin; `FindMoodTarget`/`ConsiderMoodTarget` measure from frame end to frame end (rules 1, 3; §2) | tick 6; together with C: 3695 → 3810 | `f6dacdb` |
| C | 5.3 `Register` (first update) | `AutoFindHealingUpdate` searched half a frame before the 30 Hz scan: `nextScan` of a Missile Defender after frame 1198: 0 / 61 | `RepairModules.cs`, `OnSpawn`: first update one frame after the appearance (rule 7) | 0 / 0; with B: 3695 → 3810 | `35952fb` |
| D | new: stop by an observer | the pass stops a unit after the updates (pursuit ends, victim in range): the remainder of the frame step lapsed, a march given in the same pass began in the next tick. Red Guard 42 in frame 3810: x 1748.4665 (30 Hz) against 1748.7405 | `Phase5Modules.cs`: `StopInternalMoveAtFrameEnd` (completes the frame, holds the next march decision until the next frame, `moveHoldUntil` only above 30 Hz); `Engage`, `EndEngagement` (rule 4) | 3810 → 4430 | `8225a03` |
| E | §6 "readers not converted": `HordeUpdate` | neighbours counted at the momentary position; Tank Hunter 44 after frame 4430 in the horde (30 Hz) or not (60 Hz) respectively | `Phase12Modules.cs`, `Evaluate`: `Entity.FrameEnd` for the unit and the neighbours (rule 3) | 4430 → 4746 | `d02a0d9` |
| F | §6 "turrets (`AimTurretsAt`)" | bearing, pitch and "out of range" from momentary positions and the momentary heading of the hull; turret of the Humvee 165 after frame 4746: −0.0051138 against −0.0038214 | `SimulationWorld.Turrets.cs`: `DesiredTurretAngle`, `DesiredTurretPitch`, `KeepsAttackIntent` (rule 3) | 4746 → 4947 | `c87748a` |
| G | §6 "damage in a radius", scatter salvos | a detonation measured its victims at the momentary position; missile 224 after frame 4947: health 90 against 100. Suite: impacts of 40 shots artillery platform 8 / 7 / 7, battleship 21 / 20 / 20 | `SimulationWorld.Combat.cs`, `DealDamageCore`: main victim and every candidate in a frame step at the frame end (rule 3) | 4947 → 6641; `projectiles` 4 / 4 → 0 / 0 | `202410f` |
| – | measurement of the suite | `Drive` took `HasMoveTarget == false` as the end of the drive and read the position in this tick | `RateCases.Locomotion.cs`: the drive ends when the object stands (also no longer in a frame step). No tolerance, no case, no 30 Hz value changed; no value changes at this commit | keeps the measurement on the quantity after H | `b99bd9c` |
| H | new: point in time of the arrival (contradicts `RATE-INVARIANCE-GROUND.md`, there "with the last share") | a march ended in the last tick of its last frame; whoever looked at the object in the first tick (target acquisition, the container into which it walks, the exiting) found it still under way. Ranger 597 enters the barracks in frame 6641; at 60 Hz its pursuers saw it outside one frame longer | `Phase5Modules.cs`: `BeginArrival` ends the march in the first tick (rule 1), `Coast` covers the remainder; `SettleFrame` for boarding (`SimulationWorld.PutIntoContainer`), exiting and goal object; ranges of `UpdateEnter`/`UpdateGoal` at the frame end | 6641 → 11462 | `2117316` |
| I | new | `UpdateTask` checked in every tick whether the task still applies and ended it in the middle of its own step. Dozer 4 in frame 11462: x 2343.2570 against 2343.4346 | `Phase5Modules.cs`, `UpdateTask`: the three checks in the frame ticks of the builder (§4) | 11462 → 14158 | `3d90f25` |
| J | new: "in the next tick" of the destroy phase | an object killed after the destroy phase (delayed hit) was removed one tick instead of one frame later; the footprint of the supply centre 14 left the path network one frame too early, everyone replanned | `World/Entity.cs` (`DestroyNotBefore`), `SimulationWorld.RequestDestroy`/`ProcessDestroys` (rule 7 for the removal) | 14158 → no deviation in 14,400 frames; command streams equal; `ai` 24 / 23 → 0 / 0 | `e9f1693` |
| K | §6 mob nexus | `MobMemberSlavedUpdate` read the nexus momentarily: course time 23.0667 / 25.2167 / 24.9583 s, course distance 400.062 / 439.291 / 434.011 | `SlaveModules.cs`: `Entity.FrameEnd` for member and nexus (rule 3) | 23.0667 / 23.0500 / 23.0583 s; 400.062 / 400.083 / 400.083; `locomotion` 2 / 2 → 0 / 0 | `c987fb9` |
| L | test | – | `Tests.RateInvariance.Observers.cs`: twelve units of two computer players (guns, one radius weapon, turrets) on an attack march; position, heading, health, command state, victim, turret and shots of each of 1,500 frames at 30, 60 and 120 Hz | fails at the state `dc1b8d6` (without the corrections), passes at the final state | `3ad8d7b` |
| M | new: heal slice of the repair (only visible at 120 Hz, when `ai` already passed) | repair per tick: China's barracks before the hit of frame 11273 at 250.4999 (30 Hz) against 249.9999 (120 Hz) of 1000; the fear die (`ActiveBody`, below 25 %) was rolled twice or once respectively, the random stream diverged from then on; consequence: duration of a slow death 59 against 54 frames (frame 11703) | `Phase5Modules.cs`, `UpdateTask`: above 30 Hz the 30 Hz slice once per frame of the builder (escape, §4) | random streams 30 / 120 Hz: different from frame 11273 → never; USA against China 30 / 120: 11703 → no deviation | `64f438f` |
| N | new: heal slice of the hole | `RebuildHoleBehavior` healed per tick: hole 467 after frame 5707: 488.6667 / 488.7083 / 488.7292 | `RetailMatchModules.cs`: above 30 Hz the slice once per frame of the hole (escape, §4) | equal; GLA against USA at 60 and 120 Hz: 5707 → no deviation | `2a433ba` |
| O | §6 "an object that dies in the middle of the frame" | it stayed lying at the momentary position; salvage crate 177 after frame 3791: x 1169.2887 / 1168.8769 / 1168.6710 | `SimulationWorld.OnEntityDied`: completes the begun frame (rule 4 for every death); `SettleFrame` holds the next decision until the end of the begun frame (an object in slow death keeps running) | equal; China against GLA at 60 and 120 Hz: 3791 → no deviation | `6c722b7` |

What was checked after every step was the cumulative working state, not every commit individually: the commits are cut afterwards from the checked state (every cause on its own,
every commit builds on its own with 0 warnings – rebuilt in a throw-away worktree –, but only the final state and the intermediate states named in §7 have gone through manifest and
tests).

## 4. Uses of the escape "original grid" (Ausweg „Originalraster“) and of the object grid

| Place | What runs once per frame | Grid | Reason |
|---|---|---|---|
| `SimulationWorld.StepAcquisition` | the autonomous decision of a unit (look, attack, pursuing, stop and marching on) | that of the unit (tick of the appearance) | §2: for a unit in phase only the first tick of its frame can decide something without deviating from 30 Hz. Commands, movement and ordered shots stay per tick |
| `AIUpdateInterface.UpdateTask` (three checks "is the task still worthwhile") | goal gone, construction finished or foreign, nothing left to repair | that of the builder | what happens to the goal after the move of the builder in the first tick, the 30 Hz builder sees with its next step. The work itself (construction progress) stays per tick |
| `AIUpdateInterface.UpdateTask` (repair) | the heal slice of a frame | that of the builder | slices per tick: in the first tick of a frame only 1/r of the repair of this frame has come in, a hit in this tick hits a different health than at 30 Hz (§3 #M). Like `PropagandaTowerBehavior` in the consolidation. Neither movement nor command acceptance nor target acquisition |
| `RebuildHoleBehavior.Update` | the regeneration of a frame | that of the hole | the same reason (§3 #N) |
| `AutoFindHealingUpdate` | unchanged, every tick counts; only the **first** update lies one frame after the appearance | – | not an escape: rule 7 for a module whose update gives a command |
| `SimulationWorld.ProcessDestroys` | – (runs per tick); a removal requested after the destroy phase waits one frame | tick of the request | "in the next tick" of the phases after the updates is the next frame (like `ProcessSpawns` in the consolidation) |

Not an escape but the view of the 30 Hz step (rule 3): the look of target acquisition, damage in a radius, turret aiming, `HordeUpdate`, `MobMemberSlavedUpdate`, the launch of a
projectile read at every decision as before and only a different position or a different heading respectively.

## 5. The matches of the group `ai`: first deviation

Comparison at the end of every frame over all fields of the object description (`Entity.Describe`, that is, everything that goes into snapshot and hash). Comparison rule: integers and
fixed-point values of the faster rate count as equal if they are the value of the slower one in game units (a tick in the same frame, a duration times r, a value per tick divided by r);
fields that exist only above 30 Hz (bookkeeping of the shares) do not count; a deviation only counts when it lasts three frames. **Deliberately excluded** (each is
a "running ahead within the frame" without an effect on a decision in the first tick; the first three were already named in the consolidation):

- `nextWake` of every module (bookkeeping of the scheduler);
- `previousHealth` and, as long as an object is under construction, `health`/`constructionWork` ("the first work step counts a whole frame", package TIMING: the building site is
  (r − 1)/r frame ahead at the frame end, equal in the first tick);
- `DynamicShroudClearingRangeUpdate.current` (integrates from the tick after the creation; the range `appliedRange` passed on to the vision is equal);
- `weaponReadyTick` (a reload time 0 is "next frame" instead of "this tick" above 30 Hz, `FrameGridTiming.ZeroIsNextFrame`; seen on a suicide bomber
  that dies with the shot).

| Pairing | Rates | Starting state | **Final state** |
|---|---|---|---|
| USA against China | 30 / 60 | position different from frame 3543 (118.10 s) | **all fields equal, 14,400 frames; command streams equal (1,120)** |
| USA against China | 30 / 120 | not measured | **all fields equal, 14,400 frames** |
| GLA against USA | 30 / 60 and 30 / 120 | not measured | **all fields equal, 14,400 frames** |
| China against GLA | 30 / 60 and 30 / 120 | not measured | **all fields equal, 14,400 frames** |

The way there (USA against China, 30 against 60 Hz) is given per commit in §3. The causes that only became visible at 120 Hz or in the other pairings, when the suite already
passed: the random stream diverged from frame 11273 (#M: fear die when falling below 25 % health – twice at 30 Hz, once at 120 Hz, because the
repair per tick had healed only a quarter of the slice in the first tick: 249.9999 instead of 250.4999 of 1000 before the hit), the hole of a GLA building (#N) and the crate
of a vehicle killed in the middle of the frame (#O).

## 6. What is open, with classification

| What | Values or evidence | Classification | Reason, proposal |
|---|---|---|---|
| Heal slices per tick at further places | not measured (no case in the three matches) | fixable later, each place individually | the same form as #M/#N: `SlavedUpdate` (repair drone, `SlaveModules.cs`), `RepairDockUpdate` (package TIMING: "heals one share per tick"), the regeneration of the occupants in `ContainModules.cs` (`HealthRegenPercentPerSecond`) and in `TransportModules.cs` (file of the package FLIGHT-3). As long as an object is healed and hit in the same frame, its health lies (r − 1)/r of a frame slice below the 30 Hz value in the first tick; thresholds of the body (damage state, fear die, death) can fall in a different frame |
| "first frame for all modules" (consolidation §6) | projectiles decide in the tick `SpawnTick + 1 + k·r`, that is, one tick **before** the frame in which the 30 Hz projectile decides | decision open, unchanged | a hit of a projectile falls into the last tick of the preceding frame: target acquisition (first tick of the following frame) sees the same as at 30 Hz, but an update with a smaller id in the following frame and the AI player (observes at the frame boundary before the step) see the victim dead one frame earlier. In the three matches without an effect on a field at the frame end; the survey over many matches (§7) shows how often it has an effect elsewhere |
| Collision check of a driving object | – | deliberately left (as in the consolidation) | `ResolveFootprintCollisions` runs with the last share; a run-over kills in the last tick of the frame, the target acquisition of the first tick has still seen the victim. No case measured |
| Command of a human in the middle of the frame | – | legitimately different | has no 30 Hz counterpart: the unit counts its movement frames from the command, its own target acquisition stays in the phase of its appearance (§2, variant A/C) |
| Readers that are not converted | – | fixable later, each place individually | dock and supply (pass in the suite), `SlavedUpdate`, special abilities, the script VM (runs in the first tick), `AutoHealBehavior.Matches`, `ScanClosestHealPad`, `CleanupAndSpy` modules. The number of places that read `X`/`Y` of another object is several hundred; converted are those at which a deviation was measured |
| movers that do not publish | Chinook climbing: `z` after the first tick 20 / 17.5 | package FLIGHT-3 | flight and special movers keep their remainder themselves; `--at first` of the tool shows them |
| Point in time of `MoveArrived` | first instead of last tick of the last frame | behaviour change above 30 Hz, intended | whoever reads the event or `HasMoveTarget` as "stands at the target" and immediately takes the **momentary** position reads one share before the target; converted: boarding, exiting, goal object, the measurement of the suite. The presentation is not checked |
| Performance at 60 Hz | – | not measured | target acquisition now runs per unit only in one of r ticks (cheaper than before), the frame-end view costs one comparison per candidate |

## 7. Check runs

All runs at the final state of the simulation (`6c722b7`), Release, unless stated otherwise.

| Run | Result |
|---|---|
| Rate-equivalence suite, all ten groups (`--only RateEquivalence --rate-report`) | §1: 5 quantities at 60 Hz, 3 at 120 Hz outside (before 35 / 33), all in cases of the two other packages: `flight` MiG "landing command until touchdown" 14.6333 / 14.6333 / 14.5667 s and Raptor "parked and reloaded" 60.8000 / 60.8000 / 60.5333 s; `turrets` Sentry drone (four quantities, 60 Hz); `timed` `CINE_BShipWeaponDetonationObject`. No group has become worse; `flight` 0 / 3 → 0 / 2 without a targeted change (the Comanche case passes) |
| Simulation tests, Release, 30 Hz / `--tick-rate 60` / `--tick-rate 120` | all green at every rate (496 tests: 495 existing, 1 new) |
| Simulation tests, Debug, 30 Hz | all green |
| `dotnet build CnCNew.slnx` | 0 warnings, 0 errors (incremental) |
| `input-manifest check` | identical (at the working state after A–G, after A–L and at the final state) |
| `reference-manifest check --set quick --jobs 3` | 9 runs identical (at the working state after A–D and after A–L); **not after every commit individually** |
| `reference-manifest check --set full --jobs 4` | **50 runs identical** at the final state |
| each of the 17 commits on its own | builds with 0 warnings (test project, throw-away worktree: checked for the 13 commits up to `3ad8d7b`; the four after that only at the final state) |
| Field comparison of the three matches (`--ai-diff … --fields`), 30 / 60 and 30 / 120 Hz | six runs: all fields equal over 14,400 frames (§5) |
| Survey `ai-survey --seeds 5` at 30 and 60 Hz (16 pairings on four shipped maps, 25 game minutes; information, not a criterion) | 80 matches per rate, no abort; decided 66 (30 Hz) and 63 (60 Hz). Per pairing and seed: the same outcome and winner in 73 of 80; in addition end within one second in **54 of 80**; end time equal to the millisecond in 49; equal command count in 36. The remaining 26 matches thus still diverge – on maps with terrain, aircraft and docks, which the three matches of the suite do not have (§6) |
| Performance at 30 Hz, `perf … Twilight Flame --players 8 --ticks 12000`, starting binaries and new ones alternating (old / new / old / new), mean without the first 300 ticks | 3626.1 / 3267.0 / 3335.5 / 3694.8 µs; mean old 3480.8, new 3480.9 µs – no difference; the scatter between two runs of the same binary (about 10 %) is larger than any conceivable effect, the machine was not quieted |

Changed test expectations (each justified in the commit of the cause): "an idle unit's first look …" (three frames after the appearance instead of tick 1 + 2 frames), "the random shift of the mood
check …" (grid of the unit instead of the world), "turret deadlines and the idle scan …" (the measurement reads the fleeing unit at the frame end), three ground tests of round 1 (arrival in the first instead of
in the last tick of the frame; a command after the arrival no longer cancels it), "straight-line movement and arrival" and "ObjectCreationList execution …" (wait one frame instead of
one tick). At 30 Hz each of these expectations is the same as before.

Not done or not checked: manifest and tests after every single commit; dedicated three-rate tests per cause beyond the battle test (L) and the adjusted tests;
120 Hz in the survey; the survey at the starting state (the comparison "before" is missing as a number); performance at 60 Hz and on a quiet machine; campaign (objective driver, audit) at 60 and
120 Hz after these changes; `Game.Tests`, `Presentation.Tests` and the client (the arrival in the first tick and the removal one frame later change, above 30 Hz, when
events fall). **No human has seen any of it.**

## 8. Tool

`SimTool rate-compare <content> --ai-diff A,B --fields [--rates 30,60] [--frames N] [--from BILD] [--persist N] [--at first] [--strict] [--ignore REGEX] [--context N]`:
both rates of a match of the group `ai` side by side, comparison of all described fields of all objects after every frame (rule in §5), plus the first frame from which the
random streams of the logic are different. `--at first` compares after the first tick of every frame with position, heading and turret at the frame end (the view of an observer of this
tick). `--trace ID[,ID] --trace-from F --trace-to G` prints the fields of individual objects as they change, tick by tick at both rates; `--random-trace F
[--random-frames N]` prints which phase of a tick or which module update draws from the random stream, and the events of these ticks. No behaviour.

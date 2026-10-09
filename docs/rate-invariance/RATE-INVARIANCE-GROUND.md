# Rate invariance of ground movement (Phase 23, package GROUND)

As of 8 October 2026, starting state `7b54374`. This document belongs to step 3 of Phase 23 (`docs/handoffs/HANDOFF-PHASE23.md`): it describes how ground movement
(`AIUpdateInterface.Move`/`MoveStraight`, `SimulationWorld.Collision.cs`) becomes the same game at 60 and 120 Hz as at 30 Hz, what was measured and what remains open.
At 30 Hz nothing changes: input manifest and reference manifest are identical after every commit (§7). Measured with the rate-equivalence suite
(`docs/SIMULATION-CORE.md` §39) and with dedicated three-rate tests on the synthetic fixture (`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Ground.cs`).
No human has seen a drive at 60 Hz.

## 1. Principle

The 30 Hz step is the definition. Above 30 Hz it is **subdivided**, not re-integrated with a smaller step size:

- **One decision per original frame.** Path planning, waypoint change, turn, speed under acceleration and braking, replanning around standing units, local
  avoidance and the distance of the frame are computed once per frame – with the quantities *per frame* (distance of one frame at top speed, speed change per frame, turn per frame). The
  arrival radius, the waypoint reach, the braking distance `v²/(2a)`, the threshold "within the turning circle" and the avoidance step are thus those of the 30 Hz step at every rate.
- **One share per tick.** The object covers the distance of the frame in equal shares, one per tick; share *i* of *n* is the difference of the raw-value parts *i/n* and *(i − 1)/n*
  of the step, so the shares sum **exactly** to the 30 Hz step. The heading turns in the same ticks to the heading of the decision and is exactly on it with the last
  share. Between two frame boundaries only position, height above ground and heading change.
- **The same bits.** A catalogue for 60 or 120 Hz carries its quantities per tick, each rounded once for this rate; multiplied back they miss the 30 Hz values by one or two
  steps of the resolution, and a comparison that is an exact tie in the data (remaining distance against braking distance 110.000) then falls differently at every rate. Therefore the
  catalogue build gives the 30 Hz conversion of the same source numbers to `LocomotorFrames` (`src/CnCNew.Simulation/Templates/LocomotorFrames.cs`): a static table, the key is the
  `LocomotorInfo` instance of the catalogue – not a field of a catalogue type (the input manifest stays the same), not state, not in the hash. The module keeps the speed per frame itself
  (`_frameSpeed`); `Entity.Speed` stays the distance per tick (speed per frame divided by the ticks per frame) and applies from the first share of the frame.
- **Consequence:** a single object stands, after every original frame, **bit-identically** where the 30 Hz step puts it – at 60 and at 120 Hz, with and without terrain, also with
  the locomotor of the damaged state (test `GroundRateInvarianceCourse`; in the suite: the path length of the square course 2000.114 at every rate).
- **The arrival is a frame like any other.** At 30 Hz the step that finds the object within the arrival radius puts it on the target and ends the command. Above 30 Hz
  the object covers the remaining distance in the shares of this frame; the command ends (with `MoveArrived`, speed 0) in the tick of the **last** share. The state after the frame is the
  state after the 30 Hz step, and a command that follows the arrival begins in the same frame as at 30 Hz. The arrival is thereby measured (time convention of the suite:
  "tick of the event minus tick of the command") half a frame (60 Hz) or three quarters of a frame (120 Hz) later than at 30 Hz – once per drive, not per section, within
  the tolerance of one frame.
- **The phase belongs to the object.** The first frame of a drive begins in the tick in which the standing object executes the command – a command takes effect in the next tick, at every rate.
  A command that reaches a **driving** object in the middle of a frame is accepted immediately (target set, path discarded, a pending arrival cancelled); the object
  covers the remaining shares of the running frame (at most one tick at 60 Hz, three at 120 Hz) and decides about the new target with its next frame. Speed and
  heading thus continue to change only once per frame; position and heading stay continuous (test `GroundRateInvarianceOrderInsideAFrame`).
- **A share that does not become due lapses.** If the object is not moved in the tick of a share (EMP, held, paralysed, another state of a derived AI) or
  if it stops (`StopInternalMove`), the remainder of the step is void (`_frameDue`).
- **State.** New in the snapshot and in the hash, written only above 30 Hz: `frameSpeed` (if not equal to 0) as well as `frameTicksLeft`, `frameDue`, `frameStepX`, `frameStepY`,
  `frameAngle`, `frameArriving` (only between two decisions). At 30 Hz none of these fields is ever set; snapshots and hashes are byte-identical there. A snapshot in the middle
  of a frame continues bit-identically (test `GroundRateInvarianceSnapshotInsideAFrame`).
- **Cost.** At 30 Hz the same path with the same expressions (§8). Above 30 Hz only the tick of the decision costs what the 30 Hz step costs; the other ticks of a
  frame are three additions and one terrain height.

## 2. Causes

Values: 30 / 60 / 120 Hz. "Suite" means the group `locomotion` (54 cases, 837 quantities) unless something else is stated.

| # | §5 entry (`docs/TICKRATE-MIGRATION.md`) | Finding (before) | Correction (file, symbol, principle) | after | Commit |
|---|---|---|---|---|---|
| 1 | §5.2 `AIUpdateInterface.Move`, `MoveStraight` (acceleration, braking distance, position step) | Chinook, 1,500 units: braking distance 110.000 / 111.250 / 111.875; arrival 11.7000 / 11.7833 / 11.8417 s; square course 21.1333 / 21.4667 / 21.7083 s | `Phase5Modules.cs`, `Move`/`MoveStraight`/`FrameConstants`/`FrameSpeed`/`BeginFrame`/`AdvanceFrame`; `Templates/LocomotorFrames.cs`; one line in `ObjectTemplate.cs`. Speed changes once per frame as at 30 Hz, position per tick by one share | braking distance 110.000 at every rate; arrival 11.7000 / 11.7167 / 11.7250 s; course 21.1333 / 21.1500 / 21.1583 s; position bit-identical after every frame | `bf028df` |
| 2 | §5.2 `Move` (turn, turning circle) | Time for 90°/180° was already within one frame; turn threshold (`difference > turnRate`) and "stop within the turning circle" (`distance < Speed / turnRate · 2`) depended on the tick step | as 1: turn decided per frame, heading per tick in shares, exact at the frame end | heading bit-identical after every frame; 90°: 0.4667 / 0.4833 / 0.4917 s (Chinook, the measurement hits the threshold in the middle of the frame) | `bf028df` |
| 3 | §5.2 `LocomotorInfo.CloseEnoughDist` (0 = one tick of distance), waypoint reach, snapping, `MoveArrived` | Arrival radius and waypoint reach one tick of distance (halved, quartered) | as 1: `max(CloseEnoughDist, distance of one frame)`; arrival as a frame (`BeginArrival`), end in the tick of the last share | arrival error 0 at every rate; every arrival in the same frame as at 30 Hz | `bf028df` |
| 4 | §5.2 `AvoidLocalBlockers`, `StepBlocked`, `StationaryBlockersAhead` | Avoidance step one tick of distance; look-ahead already 25 frames of distance (Phase 20), but from the speed per tick | as 1: `AvoidLocalBlockers` receives the step of the frame, the look-ahead is 25 times the distance of the frame (`BlockerLookAhead`), checking and replanning happen in the tick of the decision | detour around standing units bit-identical to the 30 Hz detour (test `GroundRateInvarianceStandingUnits`); not staged in the suite | `bf028df` |
| 5 | – (measurement) | "Distance to top speed", "braking distance", "braking time": the suite read the speed per tick and took the first/last tick at top speed – in the middle of the frame, where no 30 Hz measuring point lies; and `Entity.Speed` per tick cannot distinguish the top speed from one step below it above 30 Hz. After #1: 41 / 63 quantities outside | `Testing/RateEquivalence/RateCases.Locomotion.cs`, `Track.FirstAtTopSpeed`/`LastAtTopSpeed`: sampled at the frame end (like the path lengths), from the speed per frame (`AIUpdateInterface.SpeedPerOriginalFrame`, read access). No tolerance touched, no 30 Hz value of the group changes | 12 / 12; Chinook 195.000 at every rate (before 195.000 / 192.500 / 191.250) | `fbe8d9a`, `466d85f` |
| 6 | §5.3 (class "one tick instead of one frame"; no row of its own): dock machine and supply machine | one update per state change: a gatherer gained about ten half or three-quarter frames per round trip; first delivery of the truck 22.3000 / 22.2333 / 22.2000 s (after #1) | `Phase5Modules.cs`, `AIUpdateInterface.Update` (dock machine; during `ProcessDock` still per tick); `SupplyModules.cs`, `SupplyCarrierAI.Update` (one line, outside the file list of the package). Escape "original grid", §3 | first delivery 22.3000 at every rate; a gatherer **alone** between warehouse and centre: position, command state and money equal after every frame over 9,000 frames (truck, worker, Chinook); the remaining quantities of the suite fail because of the second gatherer (§5) | `096160d` |
| 7 | §5.2 `ResolveFootprintCollisions`, `TrySquish` | Overlap checked per position change: detected up to three quarters of a frame step earlier, different order of the collision events | `Phase5Modules.cs`, `AdvanceFrame`: check once per frame, with the last share and the whole distance of the frame. Escape "original grid", §3 | scatter mine "command until mine gone" 4.8000 / 4.7000 / 4.6417 → 4.8000 / 4.8167 / 4.8250 s; fixture: crushing and push in the same frame | `1c65446` |
| 8 | §5.2 `TryTopple` (`mover.Speed`), `Entity.Speed` | Topple force = distance per tick: half and quarter force | `SimulationWorld.Collision.cs`, `TryTopple`: speed per frame (`SpeedPerOriginalFrame`) | `ApplyTopplingForce` receives the 30 Hz value at every rate. The fall duration is thereby **not yet** equal (1.2000 / 0.6500 / 0.3250 s, before 1.2000 / 0.8000 / 0.5667 s): `ToppleUpdate` still adds its angular speed per tick (§5) | `1c65446` |
| 9 | – (consequence of #1): build order and goal object | Range check of the builder and tracking of a wandering goal object also ran in the middle of the frame, on a position that does not exist at 30 Hz | `Phase5Modules.cs`, `UpdateTask`, `UpdateGoal`: the check happens when the step of the frame has been covered; `StopInternalMove` lets the remainder of the step lapse | Start of construction in the same frame as at 30 Hz. Suite `economy/build`, "start of construction until finished": 15.2333 / 15.2500 / 15.2583 s (before 15.2333 / 15.2333 / 15.2417 s) – further away, and the honest number: the build time itself lasts "N frames minus one tick" (finding of the first suite run, not ground movement), and the too-early stop of the dozer has so far hidden one to three ticks of it | `6e018fe` |

The damaged locomotors (`SpeedDamaged`, `TurnRateDamaged`, `AccelerationDamaged`) go the same way (own frame quantities in `LocomotorFrames`); the suite measures them in 54 cases
without a failure outside the two cases from §5.

**The three locomotors "with the same distance per tick at every rate"** (`AirF_AmericaCrateParachute`, `CINE_U04_AmericaParachute`, `HorizontalArrow`: 2.000 / 1.000 / 0.500 and
1.000 / 0.500 / 0.250 respectively) were not an overlooked raw value: the drive of these objects ends after one step, the "top speed" was a single acceleration step, and that
was computed per tick². With the acceleration per frame (#1) the values are equal at every rate.

## 3. Uses of the escape "original grid" (Ausweg „Originalraster“)

| Place | What runs on the grid | Why | What does not run on the grid |
|---|---|---|---|
| `AIUpdateInterface.Update` → `DockMachine.Update` | the state changes of `AIDockMachine` (approach finished, clearance, entry, exit), on `Time.IsOriginalFrameBoundary` | a state machine of the original frame: every change costs one update; that cannot be subdivided, only counted | the movement to the dock; the work of the dock (`ProcessDock`) runs per tick, because the dock actions are written per tick (`RepairDockUpdate` heals one share per call) and the waiting times of the gatherers lie on the grid by themselves |
| `SupplyCarrierAI.Update` → `SupplyMachine.Update` | `SupplyTruckStateMachine` (idle → wants to deliver → dock command) | as above | commands to the gatherer, its movement |
| `AIUpdateInterface.AdvanceFrame` → `ResolveFootprintCollisions` | the sampling of the overlap (crushing, toppling, `ICollideModule`) once per frame of the **object**, with the last share | at 30 Hz the check happens after every whole step; a denser sampling finds overlaps earlier and in a different order | the position changes per tick |
| `AIUpdateInterface.UpdateTask`, `UpdateGoal` | range check of the build order and tracking of the goal object when the step of the frame has been covered | the position that is checked is the one the 30 Hz run also checks | the construction work itself (`AdvanceConstruction`) and the repair run per tick |

The decision of the movement itself ("once per frame") is the subdivision laid down in the brief, not the escape: position and heading change in every tick, a command is accepted in the
next tick, a standing object sets off in the tick of the command.

## 4. Measurement

Suite, outside the tolerance at 60 Hz / at 120 Hz (quantities). "Start" is the own run at the state `7b54374`.

| Group | Quantities | Start | after `bf028df` (#1–#4) | after `fbe8d9a` (#5) | after `096160d` (#6) | after `1c65446` (#7, #8) | after `6e018fe` (#9) |
|---|---|---|---|---|---|---|---|
| `locomotion` | 837 | 152 / 211 | 41 / 63 | 12 / 12 | 12 / 12 | 12 / 12 | 12 / 12 |
| `economy` | 81 | 22 / 25 | 22 / 24 | – | 20 / 25 | – | 24 / 27 |
| `scripts` | 15 | 4 / 5 | 4 / 4 | – | 4 / 4 | – | 4 / 4 |
| `overtime` | 418 | 80 / 84 | 78 / 85 | – | 80 / 81 | 80 / 81 | 80 / 81 |
| `flight` | 74 | 21 / 21 | 16 / 18 | 16 / 18 | 16 / 18 | – | 16 / 18 |
| `timed` | 383 | 53 / 56 | 53 / 53 | 53 / 53 | – | 52 / 52 | 52 / 52 |

"–": not measured for this commit. `locomotion`: from 217 quantities in 52 cases to 14 quantities in 2 cases (§5). `scripts`: the sequence path (300 units) takes
15.1000 s at every rate (before 15.1000 / 15.1333 / 15.1500 s); the four remaining quantities are counters and waiting times of the script VM. `economy`: the four build cases show, after #9, the pure build time "N frames minus one tick", the gatherers
fail because of the second gatherer (§5); sale, production and hacker are not the business of this package. `overtime`: what remains are first pulses and heal slices (counter N + 1,
healing per tick), no path. `flight`: gone are the arrival times of the helicopters (Comanche, Chinook), which fly via `AIUpdateInterface.Move`
(Chinook 11.7000 / 11.7167 / 11.7250 s). Two quantities lie outside only at 120 Hz and are differences of two points in time that each lie within one frame on their own:
Comanche "turnaround until arrival" 13.3333 / 13.3667 / 13.3833 s (the heading crosses the threshold in the middle of the frame, the arrival falls on the frame end) and – new – the
paradrop "command until aircraft gone" 49.2667 / 49.2333 / 49.2167 s (before 49.2667 / 49.2333 / 49.2333 s; `DeliverPayloadAIUpdate`, package FLIGHT).

**Whole suite at the final state** (`9548bab`, all ten groups, 4,307 quantities): outside the tolerance at 60 Hz **334**, at 120 Hz **417** (first run of the suite: 479 / 625); quantities with
a failure 428 (643). `weapons` 43 / 57, `projectiles` 54 / 103 and `turrets` 28 / 36 are unchanged, `ai` 21 / 27 (before 22 / 27).

Existing simulation tests (Release, without real data), failures: 30 Hz 0 before and after; 60 Hz 6 → 6 (the same six from `docs/TICKRATE-MIGRATION.md` §15.2);
120 Hz 14 → 13 (no. 2, the reference match with a tick budget, passes in this run – a chaotic case, no merit). One test no longer passed at 60 Hz after #6
(`Chinook: flight height, supply trips …`): its commands followed the third delivery in individual ticks and hit the supply machine in a different
state at every rate; it now counts in frames (class T, own commit).

## 5. Open, and why

- **Combat bike and mob nexus** (`locomotion`, 14 quantities, the only failures of the group): not the movement. The rider of the combat bike and the members of the mob are objects of their
  own that appear **one tick** – not one frame – after their carrier and board or line up respectively one further tick later (§5.3 `Register`, `ProcessSpawns`).
  The suite orders the drive one frame after the appearance: at 30 Hz the rider then still stands free at the starting point (the bike plans around it), at 60 Hz it has just not
  boarded in the tick of the command, at 120 Hz it sits. Hence "not monotonic in the rate". Cross-check: ordered three frames after the appearance, the position of both objects is bit-identical after
  each of 500 frames at all three rates. To be fixed in the spawn chain of the world, not here.
- **Gatherer round trip** (`economy`, truck, worker, Chinook): the supply centre creates a gatherer of its own; for the same reason it appears at a different game time at every rate,
  sets off by itself and meets the measured gatherer at a different place (avoidance, occupied approach position). Alone, the measured gatherer is bit-identical over 9,000
  frames (#6).
- **Toppling** (`timed`, 4 quantities "fall"): `ToppleUpdate` (another package) now receives the 30 Hz force, but adds its angular speed per tick. With one step per frame (§5.1
  `InitialAccelPercent`, §5.2 `ToppleUpdate.Update`) the fall duration becomes equal; until then it is 2 and 4 times too short instead of, as before, a mixture of two errors.
- **Helix, lowest height before removal 0.000 / 0.798 / 0.935** (`timed`): flight code, `HelicopterSlowDeathBehavior.Update` (`TransportModules.cs`, §5.2): the Euler fall ends
  one tick earlier above the ground at 60/120 Hz. Only noted, not touched (package FLIGHT).
- **Two driving objects that meet:** at 30 Hz A moves completely, then B (later in the same tick) sees A at its new position. Above 30 Hz B sees A, in the tick of the
  decision, moved only one share. `StepBlocked`, `StandsStill` and the overlap check read the momentary position of the other; the meeting of two driving objects
  is therefore not bit-identical to 30 Hz (difference at most one frame step of the other). The way there would be a "sequential view" (whoever has already moved in this tick
  counts with its position at the frame end; whoever is still to move, with its position at the frame beginning) – that demands, per neighbour, a look into its module in the hot path and is not done.
  Group drives, formations and whole skirmishes are the business of the distribution comparison (step 4).
- **Objects outside the phase of the world:** an object ordered in the middle of a frame of the world drives with its own phase. Dock and supply machine run on the grid of the world and
  see its arrival up to one frame later than for an object in phase; the next movement command of the machine brings it onto the grid of the world. This is not a case of the suite
  (it gives commands at frame boundaries).
- **Group speed** (`SpeedCap`, `AIGroup::getSpeed`): the cap comes as distance per tick and is multiplied by the ticks per frame – not the bits of the 30 Hz value.
  Not measured.
- **`MoveWinged`/`SteerWinged`** (winged flight) are unchanged (package FLIGHT). `Entity.Speed` stays the distance per tick for all readers.

## 6. New findings

- The spawn chain (carrier → rider/members/own gatherer) is the common cause of the remaining failures in `locomotion` and in the gatherer round trip (§5).
- **A command in the wrong tick is lost – at every rate, also at 30 Hz:** `SupplyMachine` enters "wants to deliver" in one update and gives the dock command in the next, without
  checking once more whether the gatherer is doing something else in the meantime. A command of the player (here: unloading the Chinook) that arrives in exactly this frame is overwritten
  by the dock command (`OrderDock` deletes the pending evacuation). The window is one frame long; before #6 it was one tick long at 120 Hz. Noticed in the test
  `Chinook: flight height, supply trips …`; not fixed (behaviour at 30 Hz).
- `ToppleUpdate.ApplyTopplingForce`: the parameter `speed` is now the speed per original frame at every rate (contract for the package that owns `ToppleAndFallModules.cs`).

## 7. Proof at 30 Hz

After every commit: `SimTool input-manifest check` identical, `SimTool reference-manifest check --set quick` 9 runs identical. At the end: `--set full` 50 runs identical (state `9548bab`), `dotnet build CnCNew.slnx` 0 warnings (incremental, not with deleted `bin`/`obj`), all 424 simulation tests without real data
green at 30 Hz (Debug).
`SimTool ai-match … Alpine Assault … FactionChina,FactionGLA --max-ticks 45000` at 30 Hz: winner FactionChina at tick 10,710, 11,011 ticks, final hash `BAABE02976F3F436`
(manifest run `AlpineAssault_China-GLA`, unchanged).

## 8. Performance and AI run (for information)

Performance at 30 Hz: `SimTool perf … --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 12000` from two copied binary directories (start `7b54374`, after
`6e018fe` and `9548bab` respectively), alternating, two series of four pairs each. The work is identical: 823,710 calls of `AIUpdateInterface`, final hash `F78F071AF5EC8A6E`, chain
`D9975439375F5DF7` in all 16 runs. **The machine was not quiet** – five further packages were working at the same time; the tick times are at two to four times those of
`docs/PERFORMANCE.md` §6a and scatter between two runs of the same binary by up to 100 %.

| Series, pair | Tick, mean µs before | after | `AIUpdateInterface` per call µs before | after |
|---|---|---|---|---|
| 1, 1 | 2,817.9 | 3,203.1 | 7.00 | 7.94 |
| 1, 2 | 2,740.4 | 3,803.4 | 6.76 | 9.83 |
| 1, 3 | 3,064.7 | 3,101.2 | 7.88 | 7.86 |
| 1, 4 | 3,127.3 | 2,939.0 | 7.81 | 7.47 |
| 2, 1 | 5,083.4 | 2,373.2 | 14.32 | 5.98 |
| 2, 2 | 4,775.7 | 2,614.5 | 14.13 | 6.45 |
| 2, 3 | 2,522.8 | 2,494.5 | 6.18 | 6.33 |
| 2, 4 | 2,230.3 | 2,331.9 | 5.67 | 5.98 |

In series 1 the first two pairs are slower after, in series 2 before (foreign load: the 95 % quantile of the affected runs is two to four times as high as in the
others). The four pairs without such an outlier (1,3; 1,4; 2,3; 2,4): tick on average 2,736 µs before against 2,717 µs after (−0.7 %), `AIUpdateInterface` 6.89 against 6.91 µs per
call (+0.4 %). **A deterioration at 30 Hz is not measurable; the measurement is, however, only accurate to a few percent** and should be repeated on a quiet machine. The
additional code in the 30 Hz path is a few comparisons and copies per decision, no new division. Above 30 Hz the movement costs about as much per game second as
at 30 Hz (one decision per frame), not double or four times – not measured separately.

AI run Alpine Assault, China against GLA: 30 Hz winner FactionChina after 6.0 game minutes (tick 10,710); 60 Hz winner FactionChina after 7.0 game minutes (tick 25,110,
final hash `F9765C191B1E04BD`, second run identical) – before this package at 60 Hz FactionChina after 6.5 game minutes (tick 23,220). One run, not a criterion.

## 9. Tests

`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Ground.cs` (synthetic fixture, each at 30, 60 and 120 Hz):

| Test | What it holds |
|---|---|
| `GroundRateInvarianceCourse` | a course with four sections with terrain, without terrain, with a damaged locomotor and in short hops: position, height, heading and command state after every frame are those of 30 Hz; every arrival falls into the same frame and on its last tick; between two frames the object lies on the straight line between them |
| `GroundRateInvarianceStandingUnits` | the detour around three standing units is the 30 Hz detour |
| `GroundRateInvarianceSnapshotInsideAFrame` | a snapshot one tick after a decision: restored byte-identically, for 400 frames the same hash as the running world |
| `GroundRateInvarianceOrderInsideAFrame` | a command in the middle of the frame: the remainder of the step is covered, then the new heading; a command in the last frame cancels the arrival |
| `GroundRateInvarianceCollisionsPerFrame` | crushing and toppling in the same frame, on its last tick |
| `GroundRateInvarianceSupplyTrips` | three deliveries of a gatherer on the same frames; position and account balance after every frame as at 30 Hz |

Cross-check at the starting state `7b54374` (own working tree, the same test file): five of the six tests fail at 60 Hz – the course and the detour on "the drive ends with the
last tick of its frame" (tick 0 of 2), the command in the middle of the frame, the collisions (steps 89 and 150) and the deliveries (ticks 507, 1,109, 1,705 against 250, 548, 843 at
30 Hz). The snapshot test passes there because there is no state within a frame at the starting state. At the final state all six pass at `--tick-rate` 30, 60 and 120.

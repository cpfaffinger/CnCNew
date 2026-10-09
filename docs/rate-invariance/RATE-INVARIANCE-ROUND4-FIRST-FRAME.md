# Rate invariance, fourth round: the first frame and whole matches on shipped maps (Phase 23, package ROUND4)

As of 8 October 2026, branch `worktree-agent-a8540b703e4f5eeca`, starting state `203806e` (`main`). Brief: whole matches on **shipped** maps are to be the same game at 30, 60 and
120 Hz – the retail reference match and the matches of the built-in AI in the pairings of the reference manifest. Method as in the rounds before: search for the first
deviating place (`rate-compare --reference-diff` or the new `--map-diff`), name the cause, put it on the frame convention
(`docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §1.3), one commit, re-measure. "Frame" means a logic frame of the original (1/30 s), "tick" a step of the simulation, r = ticks per frame
(1, 2, 4). Values are given as 30 / 60 / 120 Hz. **No human has seen any of this.**

**At 30 Hz nothing is different:** `input-manifest check` identical and `reference-manifest check --set quick` (9 runs) identical for every commit that changes the simulation
(§7 says which were checked at the working state and which afterwards in a throw-away worktree); `--set full` at the end: 50 runs identical. No catalogue type has a new field, no
reference value was written, no tolerance and no case of the suite was touched.

## 1. The three key figures

### 1.1 Retail reference match (`ReferenceMatchScenario`)

| | Starting state `203806e` | Final state |
|---|---|---|
| first deviating frame 30 against 60 Hz (`rate-compare --reference-diff --fields`, catalogues of the tests) | frame 0 (43 fields) | **no deviation**: every compared field of every object equal at the end of every frame of the whole match |
| first deviating frame 30 against 120 Hz | frame 0 | **no deviation** in the whole match |
| commands of the generator in the whole match (`SimTool reference-match --tick-rate N --record-only`, catalogues of the tool) | 26,626 / 28,930 / 25,309 | **26,626 / 26,626 / 26,626** |
| winner and time of victory | FactionGLA in tick 14,130 / 29,130 / 49,560, that is frame 14,130 / 14,565 / 12,390 (7.85 / 8.09 / 6.88 game minutes) | **FactionGLA in tick 14,130 / 28,260 / 56,520: frame 14,130 at every rate** |
| test "Real data: retail reference match, determinism and snapshot continuation" at `--tick-rate 120` | red (no winner in 216,000 ticks, catalogues of the tests) | **green**: winner FactionGLA in tick 56,520 (frame 14,130), 26,626 commands – the same match; the budget of the test is unchanged |

The victory times 9.9 / 9.3 / 7.9 minutes named in the brief come from an older state (`4d9d9e5`); measured at the starting state `203806e` they are the values of the table.

### 1.2 The 16 pairings of the manifest on their maps (seed of the manifest)

First frame after which a described field of an object deviates for three frames (`rate-compare --map-diff A,B --map KARTE --fields --rates 30,60`, catalogues of the tool,
up to 45,000 frames; excluded are the five fields named in §6). Winner and frame of the victory from `ai-match … --minutes 25 --tick-rate N`.

| Pairing | first deviation before | **after** | Winner in frame, 30 Hz | 60 Hz before | **60 Hz after** | equal? |
|---|---|---|---|---|---|---|
| Alpine Assault, America/China | 1306 | 37408 | – (no winner) | – | – | yes (before yes) |
| Alpine Assault, China/GLA | 3060 | **none** | China 8595 | China 8595 | China 8595 | yes (before yes) |
| Alpine Assault, ChinaTank/AirForce | 3764 | 13883 | ChinaTank 18300 | ChinaTank 17160 | ChinaTank 18510 | **no** (7 s; before no) |
| Alpine Assault, GLA/America | 4665 | 9802 (after the victory) | America 9660 | America 9660 | America 9660 | yes (before yes) |
| Armored Fury, America/China | 920 | 23868 | America 37050 | no winner | America 37050 | **yes** (before no) |
| Armored Fury, China/GLA | 2837 | **none** | – | China 39435 | – | **yes** (before no) |
| Armored Fury, ChinaTank/AirForce | 964 | **none** | ChinaTank 10860 | ChinaTank 10860 | ChinaTank 10860 | yes (before yes) |
| Armored Fury, GLA/America | 964 | 12817 | America 36180 | America 25500 | America 36180 | **yes** (before no) |
| Bitter Winter, America/China | 3921 | **none** | China 22005 | **America** 20805 | China 22005 | **yes** (before a different winner) |
| Bitter Winter, China/GLA | 3510 | **none** | China 8385 | China 8385 | China 8385 | yes (before yes) |
| Bitter Winter, ChinaTank/AirForce | 4337 | 19949 | ChinaTank 30150 | ChinaTank 22635 | ChinaTank 32685 | **no** (85 s; before no) |
| Bitter Winter, GLA/America | 3170 | **none** | America 8730 | America 8730 | America 8730 | yes (before yes) |
| Death Valley, America/China | 3509 | **none** | America 8700 | America 8700 | America 8700 | yes (before yes) |
| Death Valley, China/GLA | 3262 | **none** | China 12930 | China 12930 | China 12930 | yes (before yes) |
| Death Valley, ChinaTank/AirForce | 3667 | 23085 (after the victory) | ChinaTank 22860 | ChinaTank 15630 | ChinaTank 22860 | **yes** (before no) |
| Death Valley, GLA/America | 4723 | 10681 (after the victory) | America 10530 | America 10545 | America 10530 | **yes** (before 15 frames off) |

Together: winner and frame of the victory (or "no winner in 45,000 frames") agree at 30 and 60 Hz in **14 of 16** pairings (before 8); in **8 of 16** every
compared field of every object is equal over the whole match (before 0), in three further ones until after the victory. 120 Hz is **not measured** for the pairings.

### 1.3 Survey over 320 matches (`ai-survey`, 16 pairings × 20 seeds, 25 game minutes), 30 against 60 Hz

| | Starting state | Final state |
|---|---|---|
| the same outcome and winner | 295 of 320 | **317 of 320** |
| in addition end within one second | **192 of 320** | **287 of 320** |
| end time equal | 180 | 285 |
| equal command count | 164 | 282 |

Counted with an own evaluation of the two TSV files (outcome and winner equal, `endGameMilliseconds` at most 1000 apart); the brief names 188 for the
starting state, the own measurement at the same state gives 192. The 30 Hz survey is that of the starting state (at 30 Hz no commit changes anything; it was not repeated at the
final state).

## 2. Causes, one commit per row

"Reference" is the first lasting deviation of the reference match 30 against 60 Hz, "AA Am/Ch" that of the pairing America against China on Alpine Assault, "AF Am/Ch" the same pairing
on Armored Fury – each immediately before and after the commit at the cumulative state. Suite (`--only RateEquivalence`, 4,388 quantities) before and after every commit: every group 0 / 0.

| # | Candidate of the brief or origin | Finding before (30 / 60 Hz) | Correction (file, symbol, rule from `CONSOLIDATION` §1.3) | after | Commit |
|---|---|---|---|---|---|
| 1 | 1 (rule 7, Chinook) | Starting Chinooks climb from the tick after the appearance: height after frame 2: 10 / 12.5 | `TransportModules.cs`, `ChinookAIUpdate.Update`: first update one frame after the appearance | 10 / 10; reference still frame 0 (next cause) | `2837f1e` |
| 2 | new (measuring instrument) | the generator of the reference match gave the dock commands in tick 1, in the middle of the frame of the appearance: every gatherer from then on stood one share off everything in phase | `Testing/ReferenceMatchScenario.cs`, `Generate`/`IssueDockOrders`: in the first tick of frame 1 (at 30 Hz tick 1) | commands in 3,000 frames 5710 / 5684 → 5710 / 5710; reference frame 0 → 2 | `4b1713d` |
| 3 | new | object with **two** AI modules (Chinook of the Air Force general: `AIUpdateInterface` and `ChinookAIUpdate`, both move it): the second took the speed of the first from the speed per tick times r; speed per frame raw 2236962 / 2236961 | `World/Entity.cs` (`SpeedPerFrame`, only above 30 Hz), `Phase5Modules.cs` `FrameSpeed`/`SetFrameSpeed`/`ClearFrameSpeed` | equal; reference frame 2 → 26 | `3b5c781` |
| 4 | 3 (docks) | a docker without a waiting time (the same Chinook: its dock machine belongs to the `AIUpdateInterface`) received one crate per tick: warehouse after frame 26/27/28: 400, 399, 398 / 399, 397, 395 | `DockMachine.cs`, `ActionDelay`: at supply docks "waiting time 0" is one frame (`ZeroIsNextFrame`) | equal; reference frame 26 → 776 | `d9b5490` |
| 5 | 1 ("one tick later") | `HelicopterSlowDeathBehavior.BeginSlowDeath` woke in the next tick: Helix 49 after frame 778: rotor counter 28 / 27, fall speed raw −190888 / −286332 | `TransportModules.cs`: first step of the spiral one frame after the death | equal; reference frame 776 → 1742 | `96be02e` |
| 6 | new | `PointDefenseLaserUpdate`: a scan without a target does not count down the shot pause (at 30 Hz a whole frame without counting); above 30 Hz the remaining ticks of this frame kept counting: after frame 1739/1741/1743: 14, 12, 10 ticks against 14, 11, 8 | `ProjectileModules.cs`, `PointDefenseLaserUpdate.Update` (`_quietFrame`, only above 30 Hz) | 14, 12, 10; reference frame 1742 → 3466; the 60 Hz match ends for the first time in the frame of the 30 Hz match | `887b51e` |
| 7 | `CONSOLIDATION` §6 "collision check of a driving object" (deliberately left there) | running over, toppling and collision modules were checked with the **last** share: a crate was collected in the last tick of a frame, its random draw fell after instead of before that of a laser (waiting time 2 frames / 0) | `Phase5Modules.cs` `AdvanceFrame`/`CollideWhereTheFrameEnds`, `SimulationWorld.Collision.cs` `FootprintsOverlapAtFrameEnd`, `TryTopple`, `TrySquish`: check in the **first** tick, the driving object thereby stands at the frame end, all others are read at the frame end (rules 1, 3, 4) | the same order; reference frame 3466 → 9394; commands in the whole match 26626 / 28811 → 26626 / 26626 | `e910174` |
| 8 | 3 (readers) | "in the air?" (`isSignificantlyAboveTerrain`) was answered from the momentary height: sinking missile 0.592 / 0.838 above ground, the laser took target 10374 at 30 Hz, 10372 at 60 Hz | `IWorldServices.IsSignificantlyAboveTerrainAtFrameEnd` (`SimulationWorld.CreationLists.cs`); readers: laser scan, `VictimAntiMask`, `EstimateDamage`, radius damage, turret pitch (rule 3) | equal; reference frame 9394 → **no deviation in the whole match** | `134d074` |
| 9 | 5 (terrain) | a ground mover that is put at its frame end (`Entity.CompleteFrame`: come into range, stopped, died, boarded) kept the height of the covered share: Red Guard on the slope z 19.2160 / 19.1639. First deviation in 14 of the 16 pairings | `Phase5Modules.cs` `AdvanceFrame` publishes the height of the frame end (`Entity.SetAheadHeight`) | equal; together with #10: AA Ch/GLA frame 3060 → 7068 | `b000106` |
| 10 | 5 (terrain), 2 (Chinook height step) | the Chinook kept its height above the ground of the place after the first share instead of after the whole step: z 145.3442 / 145.3568 | `TransportModules.cs`, `ChinookAIUpdate.Update`: ground at the end of the movement of the frame | equal; AF Am/Ch frame 920 → 6528 | `b62ccc4` |
| 11 | new (consequence of #3, only visible at 120 Hz) | the second AI module decided its step from the place after one **share** of the first: Chinook 21 from frame 10 one raw value off, from frame 18 speed per frame 1.7778 / – / 1.9556 | `Phase5Modules.cs` `DecideBehindAnotherMover`; `ChinookAIUpdate` publishes the sum of both remainders | reference 30 against 120 Hz: frame 10 → see §1.1 | `10e0031` |
| 12 | 2 (constants per frame) | acceleration of a missile per frame² computed back from the tick value: 2.4 is raw 40265318 / 40265320; flame and poison stream flew a few raw values off the 30 Hz stream (the finding open in `RATE-SWITCH-OUTER-TESTS.md` §7.3) | `ProjectileModules.cs`, `MissileAIUpdate.Launch`: speed and acceleration from `LocomotorFrames` | equal; AA Ch/GLA frame 7068 → no deviation; AF Am/Ch 6528 → 6638 | `ec9f90a` |
| 13 | 3 (`CleanupAndSpy` modules) | `CleanupHazardUpdate` (medic) decided in every tick from momentary positions, its counter left the grid. First deviation in 5 pairings (frame 6717 to 7948) | `CleanupAndSpyModules.cs`: decides in the frame ticks of the owner, reads at the frame end, stops at the frame end (rules 1, 3; form of the laser) | AA Am/Ch frame 7439 → 14140 | `7880aed` |
| 14 | 3 (boarding) | `OpenContain.Add`/`Remove` put the occupant at the momentary position of the driving transport: Ranger 1182 x 1293.280 / 1293.985 | `ContainModules.cs`: `Entity.FrameEnd` (rule 3) | equal; AA Am/Ch 14140 → 16783 | `8fb48ac` |
| 15 | 3 (goal places; the finding from `RATE-SWITCH-OUTER-TESTS.md` §7.3, frame 10532) | whoever makes room for a produced unit saw it above 30 Hz already "on the way to the exit" (at 30 Hz it receives its command only after the choice of place): evasion place (1296.27 \| 1755.86) / (1324.35 \| 1805.47) | `SimulationWorld.ExitGoals.cs`, `GoalIsFree`: the unit whose place is just being chosen counts at its location | equal; AA Am/Ch 16783 → 31784 | `8327cd9` |
| 16 | 1 ("one tick later") | a march that target acquisition (after the updates) gives a standing unit began, for always-awake modules (`AssaultTransportAIUpdate`, gatherers), in the next tick instead of in the next frame | `Phase5Modules.cs` `BeginMoveWithNextFrame`, `SimulationWorld.Acquisition.cs` (pursuit, marching on) | AA Am/Ch 31784 → 37408 | `66607e2` |
| 17 | new | a sweeping turret (flamethrower) that stands on its target changes the side per step; a step without a turn occupied no frame: side after frame 6636/37/38: true, false, true / false, false, false | `SimulationWorld.Turrets.cs`, `StepTurret`: `OccupyFrame` | equal; AF Am/Ch 6638 → 17946 | `43acce8` |
| 18 | `OBSERVERS` §6 "an object that dies in the middle of the frame", for turrets | the turret of an object that dies after the turret phase stayed on the first share: 0.10472 / 0.08727 rad | `SimulationWorld.Turrets.cs` `CompleteTurretFrames`, `SimulationWorld.OnEntityDied` (rule 4) | equal; AF Am/Ch 17946 → 19217 | `36a2404` |
| 19 | 3 (special behaviour) | `AssaultTransportAIUpdate` led its group in every tick: the shot of the preceding frame was noticed one tick instead of one frame later, the troops exited one share too early | `HackerAndAssaultModules.cs`: group once per frame of the transport, positions at the frame end (escape, §4) | AF Am/Ch 19217 → 19538 | `015e975` |
| 20 | new | a driving object that a module having its turn later wakes "now" in the tick of its decision makes a second whole step at 30 Hz; above 30 Hz the remainder of the first lapsed: x 2174.5054 / 2174.2344 | `Phase5Modules.cs`, `Move`: the begun step is completed first | equal; AF Am/Ch 19538 → 20952 | `69e013e` |
| 21 | 1 (rule 7), 3 (readers) | `PilotFindVehicleUpdate` looked for the first time one tick after the appearance (after that always in the last tick of a frame); goals of `OrderApproachGoal`/`OrderEnter` from momentary positions; the re-aiming of an approach fell on the last share | `CrateModules.cs` (`OnSpawn`, `Scan`), `Phase5Modules.cs` (`OrderApproachGoal`, `OrderEnter`, `UpdateGoal`) | AF Am/Ch 20952 → 23868 | `713c662` |

Tool (no behaviour): `rate-compare --map-diff` (`e57e46f`), `--slack N` (`75db0bb`), appearance tick of a traced unit and `--commands N` (`cb1a493`); §8. Test:
`4b026c6` (§5).

## 3. Rule 7 ("first frame"): what is done and what the general version costs

The general version ("every update of a new object at the earliest `SpawnTick + r`") was measured once more on a trial basis at the starting state (one line in
`SimulationWorld.DeferSpawnPhaseOrders`, not adopted): simulation tests without real data **13 red at 60 Hz, 15 at 120 Hz** (of 512). Red were the three-rate tests of
parachute, Chinook, flight step in the snapshot, flight observers and dynamic shroud clearing, plus firestorm, enemy-near scan, checkpoint, ferry (2), Chinook (2),
`FireOCLAfterWeaponCooldownUpdate`, at 120 Hz also tunnel healing and the event delivery. Each of these tests adjusts its scene to "one tick after the appearance"; the
modules themselves partly keep their phase as a counter of their own from the first update. Therefore module by module, where a measured deviation begins:

| Module | State |
|---|---|
| `ChinookAIUpdate` | first frame one frame after the appearance (#1); four scenes now adjust themselves to this frame (at 30 Hz the same ticks) |
| `HelicopterSlowDeathBehavior` (first step after the death) | one frame after the death (#5); four scenes converted |
| `PilotFindVehicleUpdate` | one frame after the appearance (#21) |
| Projectiles (`DumbProjectileBehavior`, `MissileAIUpdate`) | were already converted at the starting state ("The first step is decided one logic frame after the launch"); the statement of round 3 "decide one tick before the 30 Hz frame" no longer holds |
| `DeliverPayloadAIUpdate` (cargo plane), parachute, ferry, dynamic shroud clearing, firestorm, `FireOCLAfterWeaponCooldownUpdate`, enemy-near scan, checkpoint | **not converted**: no measured first deviation in the runs of this round |

## 4. Uses of the escape "original grid" (Ausweg „Originalraster“) and of the object grid

| Place | What runs once per frame | Grid | Reason |
|---|---|---|---|
| `CleanupHazardUpdate.Update` | scan, target loss, approach command and shot of the module | that of the owner (`BeginsFrameOf`) | state machine of the original with one step per frame; decided in every tick its counter leaves the grid (form of `PointDefenseLaserUpdate`). The movement of the unit and commands to it stay per tick |
| `AssaultTransportAIUpdate.Update` | member list, look after the own shot, commands to the troops | that of the transport | the same reason: "in the next update" is one frame. The movement of the transport runs per tick |
| `PointDefenseLaserUpdate.Update` (`_quietFrame`) | the shot pause rests for the remainder of a frame whose decision tick was an empty scan | that of the owner | at 30 Hz this frame does not count; no new grid, only the consequence of the existing one |
| `DockMachine.ActionDelay` | the action of a supply dock on a docker without a waiting time | from the entering | "at the next update" is one frame (`ZeroIsNextFrame`) |
| `SimulationWorld.StepTurret` (`OccupyFrame` at the side change) | the side change of a sweeping turret | that of the turret | a step of the turret is one frame |

Not an escape but the view of the 30 Hz step (rules 3 and 4): the collision check of a driving object still runs once per frame, now in the first instead of in the last tick;
all other corrections only read a different position or height or shift "one tick later" to "one frame later".

## 5. Tests

- **New:** "Real data: the retail reference match gives the same commands in the same frames at 30, 60 and 120 Hz" (`Tests.RateInvariance.Round4.cs`, names its rates itself,
  45 s): the first 6,000 frames of the reference match recorded at the three rates, the command streams compared in game time (the generator gives commands from what it sees of the
  world, positions accurate to the raw value): 13,504 commands, equal at the three rates. At the starting state the streams separate in the first second.
- **Converted scenes** (at 30 Hz the same ticks, the expectations unchanged): Chinook climb, Chinook in the snapshot, Chinook observers, two Chinook tests in
  `Tests.Transports.cs` (#1); helicopter crash in four tests (#5: the trace begins one frame after the death; "75 updates after the death" is `death + 75 frames`).
- **Changed expectation** (#7, replaces the determination of the package GROUND): "Rate invariance, ground: crush and topple are sampled once per logic frame" now demands both events
  in the **first** tick of the frame of the 30 Hz events instead of in the last.
- For the causes #2 to #4, #6, #8 to #21 there is **no three-rate test of its own on the synthetic fixture**; they are carried by the field comparisons of the whole matches
  (§1) and, as far as they take effect in the reference match before frame 6,000 (#1 to #7), by the new test.

## 6. What still separates matches, and what is open

The two pairings whose outcome still deviates, and the six in which a field deviates before the end, separate at these places (30 against 60 Hz, seed of the manifest):

| Pairing | Frame | What deviates | State |
|---|---|---|---|
| Alpine Assault America/China; Armored Fury GLA/America | 37408; 12817 | Turret of an `AmericaFireBase`: in the frame in which the target acquisition of the unit looks without a result (`acquisition.nextCheck` is set), the turret begins to turn at 60 Hz in the **second tick of the same frame**, at 30 Hz one frame later (angle at the frame end −0.3975 / −0.3888) | cause not traced down to the symbol. An attempt ("a turret step that only changes the state occupies its frame", `SimulationWorld.StepTurret`) did not change the first deviating frame and is reverted |
| Alpine Assault ChinaTank/AirForce; Armored Fury America/China | 13883; 23868 | Heading of a Ranger (only `.angle`, position equal): −1.2617 and 2.2411 respectively at 30 Hz against a different heading at 60 Hz | not traced (candidate: an ability or a stop that ends in the middle of a turn) |
| Bitter Winter ChinaTank/AirForce | 19949 | Speed of a Gattling tank (1.3333 per frame against a different value) | not traced |
| Alpine Assault GLA/America; Death Valley GLA/America; Death Valley ChinaTank/AirForce | 9802; 10681; 23085 | Turret of a Humvee or `acquisition.nextCheck` of a Dragon tank respectively, each **after** the victory (in the 300 frames of follow-up) | the same pattern as the FireBase; without an effect on winner and time of victory |

The survey (§1.3) shows the same on a large scale: 33 of 320 matches do not yet end at 60 Hz within one second of the 30 Hz end (before 128), 3 with a different outcome
(before 25).

Deliberately excluded from the field comparison (`--ignore`), each a "running ahead within the frame" without an effect on a decision; the first three as in
`docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md` §5:

- `nextWake` of every module, `previousHealth`;
- `weaponReadyTick` and – new, the same case – `dockMachine.nextActionTick` (a waiting time 0 is "next frame" instead of "this tick" above 30 Hz);
- `DynamicShroudClearingRangeUpdate.current` (integrates from the tick after the creation; the `appliedRange` passed on to the vision is equal). In 8 of the 16 pairings
  this was formally the first deviation at the starting state (frame 1813/1814); it is not fixed but excluded.

Further open, with classification:

| What | Evidence | Classification |
|---|---|---|
| Objects with two AI modules from the view of an observer | `--reference-diff --at first`: heading of the crashing Helix 49 from frame 778 (the heading of the spiral is not published); the heading of two movers of one object is only approximate between two frame ends | fixable later; equal at the frame end |
| Rule 7 for the modules named in §3 | – | later, per module with its scene |
| Constants `value per tick × r` (candidate 2) | Spectre, parachute, flares, particle beam, shot-down jet still compute this way; only the Chinook height step (ground) and the missiles (#12) are touched | open: no measured first deviation in this round |
| Heal slices per tick (candidate 4) | `RepairDockUpdate` already heals once per frame (`_actionPhase`, before this round); `SlavedUpdate` and the regeneration of the occupants (`ContainModules.cs`, `TransportModules.cs`) are **not** touched | open: no measured first deviation |
| Script VM, special abilities, `SlavedUpdate` as readers (candidate 3) | not touched | open |
| `IsSignificantlyAboveTerrain` at the remaining readers | mines, crates, bridges, `IsAirborne` still read the momentary height | later, per place |
| Bridges, water, cliffs | no case of its own; the four maps of the manifest have slopes on which #9/#10 are measured | not investigated |

## 7. Check runs

| Run | Result |
|---|---|
| `input-manifest check`, `reference-manifest check --set quick --jobs 3` | identical and 9 runs identical respectively for **every** commit that changes the simulation: at the working state immediately before the commit for `2837f1e`, `4b1713d`, `3b5c781`, `d9b5490`, `96be02e`, `887b51e`, `e910174`, `134d074`, `ec9f90a`, `8fb48ac`, `66607e2`, `713c662`; built and checked afterwards per commit in a throw-away worktree (because several causes were measured together and then committed separately): `b000106`, `b62ccc4`, `10e0031`, `7880aed`, `8327cd9`, `43acce8`, `36a2404`, `015e975`, `69e013e` |
| `reference-manifest check --set full --jobs 5`, `input-manifest check` | **50 runs identical**, input manifest identical, at the final state `713c662` |
| `ai-survey` at 30 Hz at the final state against the starting state | 320 matches, all 320 rows (outcome, end tick, command count, final hash) identical to the survey at the starting state |
| Rate-equivalence suite (`--only RateEquivalence`, 4,388 quantities) | each of the ten groups 0 / 0 after every commit that changes the simulation (measured at the working states named above), and at the final state |
| Simulation tests, Release, without real data, 30 Hz / `--tick-rate 60` / `--tick-rate 120` | all 512 green each at each of the named working states |
| Simulation tests, Release, with real data (`--content-dir`), final state | at 30 Hz, `--tick-rate 60` and `--tick-rate 120` **all 542 green** each (red at the starting state at 120 Hz: the reference match); the suite in it: every group 0 / 0 |
| Simulation tests, Debug, 30 Hz (`dotnet run --project tests/CnCNew.Simulation.Tests`) | all 512 green |
| `dotnet build CnCNew.slnx` | 0 warnings, 0 errors (incremental, `bin`/`obj` of `CnCNew.Client.Stride` not deleted) |
| Performance at 30 Hz (`SimTool perf … Twilight Flame --players 8 --ticks 12000`, old / new / old / new) | mean without the first 300 ticks: 1045.0 / 1056.8 / 1064.4 / 1057.1 µs per tick; old on average 1054.7, new 1057.0 (+0.2 %; the two old runs lie 1.9 % apart): no measurable difference. The machine was not quieted |

Incident, fixed: the four commits `b000106` to `ec9f90a` were first cut with a hunk tool without context; two intermediate states did not build. The four commits
are cut anew (the same final tree), each builds and is checked individually.

Not done or not checked: the 16 pairings and the survey at 120 Hz; the original AI (`--original-diff`, the case from `RATE-SWITCH-OUTER-TESTS.md` §7.3 frame 10532) at the
final state; campaign (objective driver, audit) at 60 and 120 Hz; `Game.Tests`, `Presentation.Tests`, the client and the scene audits (the collision check in the first tick and the
group logic of the transport change, above 30 Hz, when events fall); dedicated three-rate tests per cause on the synthetic fixture (§5); performance at 60 Hz.
**No human has seen any of it.**

## 8. Tool

- `SimTool rate-compare <content> --map-diff A,B --map KARTE [--seed N] --fields [--rates X,Y] [--frames N] [--commands N]` and the options of `--fields`: exactly the match of
  `ai-match --map KARTE --factions A,B` (catalogues of the tool with model geometry, seeds of the manifest if `--seed` is missing) at two rates side by side; `--commands N` prints
  the commands of the last N frames before the end of the comparison (the state of the AI lies outside the world and is not compared).
- `--slack N`: fixed-point values that lie at most N raw values apart count as equal (in order to see behind a known rounding deviation; the measurement is 0).
- `--trace` prints, at the first look at an object, the tick of its appearance and its position within the frame of the world.

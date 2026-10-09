# Rate invariance, third round: package FLIGHT (Phase 23)

As of: 8 October 2026, branch `worktree-agent-acaeab4437e5153cd`, starting state `37b36b8` (`main`). Brief: the three flight cases that lay outside the tolerance only at 120 Hz,
the publication of the frame remainder of the flight and special movers, the Sentry drone (`DeployStyleAIUpdate`) and the paradrop of the special power. The yardstick is the
rate-equivalence suite (`docs/SIMULATION-CORE.md` §39) and one three-rate test per cause. "Frame" means a logic frame of the original (1/30 s), "tick" a step of the simulation,
r = ticks per frame (1, 2, 4). Values are given as 30 / 60 / 120 Hz. The convention of the frames is in `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §1.3; this document applies it.
**No human has seen any of this.**

**At 30 Hz nothing is different:** `input-manifest check` identical and `reference-manifest check --set quick` (9 runs) identical after every commit that changes the simulation;
`--set full` at the end: 50 runs identical (§7). All 4,307 quantities of the suite have the same raw value at 30 Hz as at the starting state. No catalogue type has a new field, no
reference value was written, no tolerance and no case of the suite was touched.

## 1. Result of the suite

Quantities outside the tolerance at 60 Hz / at 120 Hz, all ten groups, Release, real data (`--only RateEquivalence --rate-report`).

| Group | Quantities | Starting state `37b36b8` | **Final state `25a784e`** | Remark |
|---|---|---|---|---|
| `locomotion` | 837 | 2 / 2 | 2 / 2 | Mob nexus (package OBSERVERS) |
| `flight` | 74 | 0 / 3 | **0 / 0** | 5 quantities of the paradrop unmeasured at every rate; with the held-back commit (§6) 0 unmeasured |
| `weapons` | 1,474 | 0 / 0 | 0 / 0 | |
| `projectiles` | 743 | 4 / 4 | 4 / 4 | Scatter salvos (package OBSERVERS) |
| `turrets` | 216 | 4 / 0 | **0 / 0** | Sentry drone |
| `overtime` | 418 | 0 / 0 | 0 / 0 | |
| `economy` | 81 | 0 / 0 | 0 / 0 | |
| `timed` | 383 | 1 / 1 | 1 / 1 | `CINE_BShipWeaponDetonationObject` (package DEFECT) |
| `scripts` | 15 | 0 / 0 | 0 / 0 | |
| `ai` | 66 | 24 / 23 | 24 / 23 | package OBSERVERS |
| **Sum** | **4,307** | **35 / 33** | **31 / 30** | no group is worse, no quantity newly fails |

## 2. Causes, one commit per row

| # | Origin | Finding before (30 / 60 / 120 Hz) | Correction (file, symbol, principle) | after | Commit |
|---|---|---|---|---|---|
| 1 | §5.2 winged flight and `JetAIUpdate`; FLIGHT §5, CONSOLIDATION §6 "`flight`, only 120 Hz" | MiG "landing command until touchdown" 14.6333 / 14.6333 / 14.5667 s; Raptor attack "parked and reloaded" 60.8000 / 60.8000 / 60.5333 s. The steps computed `catalogue value per tick × ticks per frame`; the catalogue values are rounded separately per rate (16 · a₁₂₀ ≠ a₃₀, relatively about 10⁻⁵), `Entity.Speed` per tick loses bits. After a few hundred frames the positions lay about 0.01 units apart and an arrival threshold tipped over | `Phase5Modules.cs` only `MoveWinged`/`SteerWinged` and new helpers next to them (`FlightFrameConstants`, `FlightVerticalConstants`, `FlightFrameSpeed`, `SetFlightFrameSpeed`, `TurnRadius(turnRate, speed)`); `JetAIUpdate.cs` (`TaxiAlongPath`, `TurnTo`, `TakeOff`, `Land`, `ControlHeight`), `JetAIUpdate.Flight.cs` (`CircleStep`); `Templates/LocomotorFrames.cs` extended by `SpeedLimitZ` and `Lift` (existing values unchanged), `ObjectTemplate.cs` passes the two source numbers. The flight decides with the per-frame constants of the 30 Hz catalogue and keeps its speed per frame in the module (`_frameSpeed`, which the ground movement already keeps). Gravity of the height control from the definition value | MiG 14.6333 × 3; Raptor 60.8000 × 3; in addition become exact: Raptor "first shot" 18.6333 / 18.6500 / 18.6333 → × 3, Aurora "touchdown" 15.8000 / 15.7667 / 15.8000 → × 3. `flight` 0 / 3 → 0 / 1 | `346cadc` |
| 2 | – (measurement of the suite) | Comanche "turnaround: reversed course until arrival" 13.3333 / 13.3667 / 13.3833 s with a **bit-identical** path (trace: the same raw value of X at the end of every frame). `MeasureSortie` read the heading after every tick: the heading is turned in shares, the threshold is crossed with the first share (up to one frame early), the time that begins there ends with an arrival in the last tick of a frame: 2 · (r − 1) ticks more than at 30 Hz. "Produced until parked" began one tick after the appearance, in the middle of the frame | `RateCases.Flight.cs`, `MeasureSortie`: the heading is read at the frame end (`RateScene.StepUntilFrameEnd`, the form of `f55c16d`); after `Produce` an `Align` (no step at 30 Hz), as since `234f3c3` before the other commands of this case. No tolerance, no quantity, no 30 Hz value changed | 13.3333 / 13.3500 / 13.3583 s (the remainder is the arrival in the last tick). `flight` 0 / 1 → 0 / 0 | `3a8d256` |
| 3 | new | `FlightFrameStep` takes back the shares of the turn and adds them individually, normalised each time. The sum is right, but half a turn has two representations: heading of the landing at 30 Hz raw value 52707180 (+π from `Atan2`), above 30 Hz −52707177 | `Time/FrameGrid.Flight.cs`: the last share sets the heading of the decision (like `_frameAngle` of the ground movement); in the snapshot only during a frame with a turn | sortie test (§4): first frame that deviates from 30 Hz, 1154 / 1154 → behind the landing | `8be3a73` |
| 4 | §5.3 `Register` (first update) for aircraft; CONSOLIDATION §1.3 rule 7, exception "integrators" | `JetAIUpdate` ran for the first time one **tick** after the appearance: an aircraft produced in the first tick of a frame was r − 1 ticks ahead of its 30 Hz state for the rest of its life. A parked aircraft checked every tick: what it begins by itself (stored command after the reload, take-off of the healed helicopter) began one tick instead of one frame after the state before | `JetAIUpdate.cs`, `Update`/`BeginTakeoff` (only above 30 Hz): first update one frame after the appearance; every decision of the airfield states costs one frame, also the idle look; a command to a parked aircraft ends this idle frame, the take-off still begins in the tick of the command | sortie test: first deviating frame jet 30 / 30 → none; helicopter 30 / 30, after that 140 / 140 → none. Suite: "produced until parked" Raptor/Aurora 1.3667 / 1.3500 / 1.3417 → × 3, Comanche "touchdown" 12.7667 / 12.7833 / 12.7750 → × 3; the two helicopter climb times now show the lag of a position (1.6667 × 3 → 1.6667 / 1.6833 / 1.6917, within). `flight` 0 / 0 | `cc7fd75` |
| 5 | new (Sentry drone; TURRETS finding 1, CONSOLIDATION §6 "`turrets`") | Turret angle at the first shot 1.57080 / 1.55771 / 1.57080; "victim gone until turret leaves its target" 1.0000 / 0.0000 / 1.0250 s; 180° −3.14159 / 3.12850 / −3.14159. Cause in §3 | `StructureModules.cs`, `DeployStyleAIUpdate.AllowsFire`/`Update`/`SetState` (only above 30 Hz): fire attempts that come in the frame of becoming ready **before** the update of the unit are discarded like the command of the same tick at 30 Hz; whoever asks after the update (own target acquisition) gets through | Suite `turrets` 4 / 0 → 0 / 0 (all four × 3). First shot after the first command, frames: Nuke Cannon (4 variants) 101 / 100.5 / 100.25 → 101 × 3; Sentry drone 34 / 33.5 / 34.25 → 34 × 3 | `c1a57ee` |
| 6 | new | `JetAIUpdate.FlyOn` forgets the orbit centre as soon as the aircraft is not circling. At 30 Hz a command that ends in the same step names the point afterwards (`OnWingedArrival`); above 30 Hz the ticks that fly the remainder of the arrival frame ran the same line once more: a jet that returns for landing and arrives at the approach point circled around the place where it just was | `JetAIUpdate.cs`, `FlyOn`: the centre is only forgotten at a decision (at 30 Hz every tick) | second sortie of the test (return home after the idle time): first deviating frame 782 / 782 (0.34 units, 0.14 rad, after that a different glide path) → none. Suite unchanged (its sorties receive the landing command while they circle) | `fd96aff` |
| 7 | CONSOLIDATION §6 "movers that do not publish" | Range questions, shots and guided projectiles read an aircraft a part of its frame step away from the place where it stands at 30 Hz: jet up to 3.75 / 5.62 units, helicopter 2.13 / 3.20, Chinook 3.30 / 4.95, parachute 1.12 / 1.68, shot-down jet 2.18 / 3.27, shot-down helicopter 2.50 / 3.75, Spectre 4.00 / 6.00, flare 3.89 / 5.84 (60 / 120 Hz) | §5: `FlightFrameStep.Publish` and the modules that own a step (rule 3) | distance between reading and frame end 0 / 0 for all nine movers, reading equal to the 30 Hz position (0 / 0). Suite: all groups unchanged | `e06259f` |

Tests per cause: `813125d` (#1, #3, #4), `00db28e` (#5), `e6bf1d1` (#6), `25a784e` (#7); §4.

## 3. The Sentry drone: what it was, and why only at 60 Hz

The earlier finding "status at 60 Hz in tick 65 instead of 64" no longer holds at the starting state: the status bit `DEPLOYED` appears in tick 32 / 64 / 128 (log of `SetState`).
The cause lies one step behind it.

- A unit with `DeployStyleAIUpdate` becomes ready to fire **in its update**. At 30 Hz the fire command of the same tick came before this update and is discarded
  (`WeaponNotReady`); the next command, one frame later (tick 33), passes the gate and gives the turret its order.
- Above 30 Hz commands come in every tick. The command of the next **tick** passed (65 instead of 66, 129 instead of 132): the turret began r − 1 ticks too early and off the frame grid
  of the unit, the shot fell half or three quarters of a frame early. This is the kind "one step later" from the principle: one frame at 30 Hz, therefore one frame at every rate.
- **Why the suite failed only at 60 Hz:** the early beginning existed at both rates. The suite reads the turret angle at the next frame boundary of the **world** after the shot.
  A turret that decides in the ticks 4k + 1 has turned its whole frame there (three ticks later): at 120 Hz the 30 Hz angle stood there, and the times stayed, with ¾ frame,
  within the tolerance of one frame. A turret that decides in the ticks 2k + 1 is read immediately after its decision, after the first of two shares:
  1.55771 instead of 1.57080 rad (half a frame step of 1.5°). The "return" quantities of the case begin at this angle and broke with it (0.0000 s: the turret "leaves"
  an angle that it had not even reached yet).
- **Correction:** above 30 Hz the module remembers the first tick of the frame after becoming ready and, as long as this frame runs, the tick of its last update. A fire attempt
  in this frame that comes before the update of the tick (a command) is discarded; one after it (the unit's own target acquisition, phase after the updates) gets through, as at
  30 Hz in the tick of the update. Two state fields, written only during this one frame, never at 30 Hz.
- **The other users** (real-data test `RealDeployStyleTimesAreTheSame`): in the data exactly ten objects carry `DeployStyleAIUpdate` – four Nuke Cannons and six
  Sentry drones; artillery platforms and Tomahawk do not carry it. Five have a weapon in the default weapon set and are measured (the four Nuke Cannons and the
  Sentry drone of the mission); the five remaining drones only get their gun with the upgrade and are **not** measured (the same module with the same times). "Ready to fire"
  and "packed" were already equal at every rate before (Nuke Cannon 100 × 3 and 100 × 3 frames, drone 30 × 3 and 30 × 3), the first shot was not (table #5).

## 4. The three-rate tests

All name their rates themselves and run without real data, except the one named.

| Test (file) | What is compared | Result |
|---|---|---|
| `RateFlightJetSortieIsBitIdentical` (`Tests.RateInvariance.Round3Flight.cs`) | Raw values of position, height, heading, speed per frame, airfield state, command flag and clip after **each** of 1,500 frames. Sortie 1: production, taxiing out of the hangar, parking, command (taxiing, pause, take-off run, climbing, flight, circling), second command in flight (turnaround), pursuit and fire until the clip is empty, return home, approach, landing, roll-out, taxiing, turning, reloading. Sortie 2: without an attack, return home after the idle time | first frame that deviates from 30 Hz: none / none, in both sorties |
| `RateFlightHelicopterIsBitIdentical` (the same file) | the same for a helicopter of a helipad over 900 frames: production, lift-off, climbing, flight, turnaround in flight, return home, landing, take-off of the healed helicopter (eight rounds) | none / none |
| `RateDeployStyleIsBitIdentical` (`Tests.RateInvariance.Round3Deploy.cs`) | State, `DEPLOYED`, turret angle, health of the victim, position after each of 260 frames under a fire command **in every tick**: deploying, turret a quarter turn, fire, packing on a move command, drive, deploying again | States `U@0 A@30 P@100 M@115 U@160 A@190`, 41 shots at every rate (before 42 at 60 Hz) |
| `RealDeployStyleTimesAreTheSame` (the same file, real data) | "ready to fire", first shot, "packed" in ticks of game time for every retail object with `DeployStyleAIUpdate` | equal at every rate (§3) |
| `RateFlightObserversReadTheFrameEnd` (`Tests.RateInvariance.Round3FlightObservers.cs`) | after every tick: what an observer reads (`Entity.FrameEnd`) against the position at the end of the flown frame and against the 30 Hz position; nine movers (§5) | at 30 Hz nothing published; above 30 Hz distance 0 / 0 |

Three existing scenes had adjusted themselves to "one tick after the appearance" and now take this frame (at 30 Hz the same ticks; in the commit `cc7fd75`): `WingWorld`
(`Tests.JetFlight.cs`), the wing path and the snapshot scene `wingStep` (`Tests.RateInvariance.Flight.cs`). The exception of the source rule "no value multiplied by
itself" for the braking distance of winged flight names the new expression (`Tests.SquareSourceRules.cs`, in the commit `346cadc`).

## 5. Who publishes its frame remainder

Above 30 Hz, once per tick, absolute (a foreign remainder on the object does not survive the publication); at 30 Hz nothing is written.

| Mover | What is published | Place |
|---|---|---|
| Jet (taxiing, take-off run, landing, height, winged flight, circling) | remainder of the state step (`_frame`) + remainder of the wing step (`WingedStepRest`) | `JetAIUpdate.FlyOn` |
| Helicopter (`JetAIUpdate` without wings) | remainder of the state step (height) + remainder of the movement step (`GroundStepRest`, read-only) | `JetAIUpdate.FlyOn` |
| Chinook | remainder of the height step (`_climb`) + remainder of the movement step | `ChinookAIUpdate.Update` |
| Parachute and its rider | remainder of the descent (`_descent`); the rider gets the same remainder, none when landing | `ParachuteContain.Update`/`Carry`/`Land` |
| shot-down jet, shot-down helicopter | remainder of the fall (`_fall`, `_spiral`); with the beginning of the fall, what the AI of the aircraft had left of a frame lapses | `JetSlowDeathBehavior`, `HelicopterSlowDeathBehavior` |
| Spectre | remainder of the flight step (`_flight`) | `SpectreGunshipUpdate.Update` |
| Flare | remaining ticks of its frame times the speed that it keeps for this frame; sinking at most down to the ground | `CountermeasuresBehavior.MoveFlares` |
| dropped payload, cargo plane | the flight of `DeliverPayloadAIUpdate` is the air movement of `AIUpdateInterface.Move` and already published; the payload falls on the parachute (above) | – |

A step whose published remainder is gone was completed from outside (`Entity.CompleteFrame`: a pursuing unit came into range, death by height): it flies
nothing more of this frame (`FlightFrameStep.Continue`), like the ground step. **Consequence:** a pursuing aircraft that comes into range stands, like a ground unit, immediately
at the frame end and does not fly the remaining r − 1 ticks of this one frame (before, it had no published remainder and was not completed).

## 6. Paradrop of the special power: held-back commit

"Command until aircraft gone" lies within the tolerance: 49.2667 / 49.2500 / 49.2417 s (starting state and final state). Five quantities are unmeasured at every rate because the drop
drops nobody at any rate – a **30 Hz error** (FLIGHT §6): (a) `SimulationWorld.ExecuteDeliverPayload` queues the payload before its parachute; (b) `SpawnEntity`
asks the cargo plane with `IsValidContainerFor` whether it takes the parachute (`AllowInsideKindOf` does not name `PARACHUTE`), the original (`DeliverPayloadNugget::create`,
`ObjectCreationList.cpp`) calls `addToContain` without this question.

The correction changes the behaviour at 30 Hz and is therefore **the last commit of the branch, marked "HELD BACK — changes 30 Hz"**, to be merged separately: the parachute is queued before
its payload (`SimulationWorld.CreationLists.cs`), and a transport with `DeliverPayloadAIUpdate` takes what the delivery assigns to it, without a question
(`SimulationWorld.cs`, `SpawnEntity`). Measured with this commit:

| Quantity (case `flight/paradrop SuperweaponParadropAmerica`) | before | after |
|---|---|---|
| Command until first drop | – / – / – | 23.2667 / 23.2500 / 23.2417 s |
| first until last drop | – / – / – | 0.8000 × 3 |
| Command until first landing | – / – / – | 28.3667 / 28.3500 / 28.3417 s |
| Command until last landing | – / – / – | 29.1667 / 29.1500 / 29.1417 s |
| landed soldiers | 0 / 0 / 0 | 5 / 5 / 5 |
| mean distance of the landing from the target | – / – / – | 0.000 × 3 |
| Command until aircraft gone | 49.2667 / 49.2500 / 49.2417 s | 49.6333 / 49.6333 / 49.6250 s |

- Suite with the commit: `flight` 0 / 0 with **0** unmeasured quantities (before 5); all other groups as at the final state (sum 31 / 30).
- `reference-manifest check --set full` with the commit: **50 runs identical** – no run of the manifest uses a delivery with a container. The manifest does not have to be rewritten
  for this commit (it was not written). `input-manifest check` identical.
- Simulation tests with the commit, Release: at 30, 60 and 120 Hz all green (without real data); with real data at 30 Hz only the four group tests of the suite fail that also
  fail without it.
- Not checked: drops in missions and skirmishes outside the manifest (objective driver, `CampaignTool`); the script-controlled drop (`ScriptEngine.Reinforcements.cs`) goes
  a different way and is not touched. Conspicuous, not investigated: all five Rangers land exactly on the target point (distance 0.000).

## 7. Check runs

| Run | Result |
|---|---|
| `input-manifest check`, `reference-manifest check --set quick --jobs 3` | identical and 9 runs identical respectively at the states `346cadc`, `8be3a73`, `cc7fd75`, `c1a57ee`, `fd96aff`, `e06259f` (every commit that changes the simulation, with a freshly built `SimTool`) |
| `reference-manifest check --set full --jobs 4`, `input-manifest check` | **50 runs identical**, input manifest identical, at the final state `25a784e` |
| Suite in total (4,307 quantities) | §1; TSV in the scratch folder `round3-flight\final-suite.tsv`; all 30 Hz raw values equal to those of the starting state |
| Simulation tests, Release, without real data | 30 Hz, `--tick-rate 60`, `--tick-rate 120`: all 499 green each (at the starting state 495, likewise all green) |
| Simulation tests, Release, with real data, 30 Hz | 522 green; red only the four group tests of the suite (`locomotion`, `projectiles`, `timed`, `ai`) |
| Simulation tests, Debug, 30 Hz (`dotnet run --project tests/CnCNew.Simulation.Tests`) | all green |
| `dotnet build CnCNew.slnx` | 0 warnings, 0 errors (incremental, `bin`/`obj` not deleted) |

## 8. The escape "original grid" (Ausweg „Originalraster“) and what comes close to it

No process newly runs "as a whole only every r-th tick of the world". Three places put something on the frame of the object; they are listed here so that they can be judged:

| Place | What happens once per frame | Grid | Reason |
|---|---|---|---|
| `JetAIUpdate.Update`, parked aircraft without an order | the idle look (reporting healing, executing a stored command, return time, take-off of the healed helicopter) | that of the aircraft | state machine of the original, one step per frame; "one step later" is one frame. **Command acceptance stays per tick:** a command ends the idle frame, the take-off begins in the tick of the command (test: state `TaxiToTakeoff` in the frame of the command at every rate) |
| `JetAIUpdate.Update`, new aircraft | the first update comes one frame after the appearance | tick of the appearance | rule 7 of the convention; at 30 Hz the next tick |
| `DeployStyleAIUpdate.AllowsFire` | in the frame of becoming ready (r − 1 ticks) fire attempts that come before the update of the unit are discarded | tick of becoming ready | §3. The command is accepted and executed as in every tick; the gate answers in these ticks what it answers the command of the ready tick at 30 Hz. Movement and target acquisition are not touched (the own target acquisition passes the gate from the tick of the update) |

## 9. Open, deliberately left, new findings

- **Not converted to the 30 Hz constants:** Spectre (`Fly`), Chinook height step, parachute, flares, particle beam and the shot-down jet still compute
  `catalogue value per tick × r` (`FlightFrames.PerFrame`). Their three-rate tests show distance 0 / 0 with the numbers of the fixture, the suite has no failure there; at a
  threshold it can cost a frame as in #1. The same pattern as #1 (`LocomotorFrames`), not done.
- **Rule 7 only for `JetAIUpdate`:** Chinook, cargo plane and the other flyers derived from `AIUpdateInterface` still begin one tick after the appearance
  (the exception of the convention). A Chinook without a rally point therefore still completes its frame in the first instead of in the last tick of a frame of the world.
- **Stayed non-publishing** (not in the files of this package): train, avalanche, ferry, wave, free fall of `PhysicsBehavior`. In the files of this package: the
  Gattling of the Spectre (it is put on the position of the ship before its step, one step behind the ship at every rate) and the particle beam (not an object).
- **The heading at the frame end** is still not published (`Entity.FrameEnd` only knows the position).
- **Two steps of an object with a different phase** (a helicopter that receives a move command in the middle of a frame of its state machine): what is published is
  the sum of both remainders. No test for it; all scenes of the tests give commands at frame boundaries.
- **Measurement "climbing to flight height"** of the helicopters now lies (r − 1) ticks behind 30 Hz (within): a threshold on a position that is reached with the last tick.
- **Performance:** not measured quietly. A direct switch of the binary directories of starting and final state (`SimTool perf`, Twilight Flame, 8 players, 9,000 ticks, 30 Hz,
  two runs each, mean without the first 300 ticks): 2,190.6 and 2,186.0 µs against 2,141.2 and 1,606.5 µs – no increase recognisable, the scatter is larger than any difference.
  At 30 Hz, in winged flight and in the airfield states one call with a comparison (`_stride == 1`) is added per decision, no
  division; above 30 Hz per aircraft and tick one publication (three additions) and one table access when the locomotor changes (taxiing against flight).
- **Finding (TURRETS 1 superseded):** `DeployStyleAIUpdate` sets its status in time; the deviation was the command after it (§3).
- **Finding:** the same kind "the update makes something possible, the next command uses it" presumably exists at further gates that a command queries per tick and an update
  opens (not looked through; `IFireGate` has only this one implementer).
- **Finding:** Raptor and MiG flew at the starting state with a cruising speed whose raw value per second differed between the rates (Raptor 2936012790 / 2936012820 /
  2936012760); the suite shows it with three decimal places as 175.000 × 3. At the final state only the truncated bits of `Entity.Speed` per tick still differ
  (… 790 / … 760 / … 760), the computation uses the speed per frame.
- **Not done or not checked:** jets over a real match at 60 Hz; airfields with model bones; several jets at one runway at 60/120 Hz; a snapshot in the middle
  of the frame of becoming ready of a deployed unit (the two fields are written and read, a test of its own is missing); objective driver and `CampaignTool`; Debug run
  with real data; presentation (`Presentation.Tests`, `Game.Tests`).

# Rate invariance, package FLIGHT (Phase 23)

As of: 8 October 2026, branch `worktree-agent-ab31e8d3a443e52b0`, starting state `7b54374`. Brief: `docs/handoffs/HANDOFF-PHASE23.md` step 3 for flight, airfield, helicopter,
parachute, flares, particle beam, Spectre and tunnel (`docs/TICKRATE-MIGRATION.md` §5.1, §5.2, §5.3, §5.6). The yardstick is the rate-equivalence suite
(`docs/SIMULATION-CORE.md` §39) and, where it has no case, one three-rate test each on the synthetic fixture (`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Flight.cs`).
"Frame" means a logic frame of the original (1/30 s), "tick" a step of the simulation. All numbers are measured on this day; values are given as 30 / 60 / 120 Hz.

**At 30 Hz nothing is different:** `input-manifest check` identical and `reference-manifest check --set quick` (9 runs) identical after every correction commit (three exceptions in §7),
`--set full` at the end (§7). No catalogue type has a new field; no reference value was written.

## 1. The principle as it is applied here

The 30 Hz step is the definition of an integration. At a higher rate it is **decided once per frame** – with the same expressions as at 30 Hz, in units of one frame –
and **flown in equal shares per tick** (position, height and heading). At the end of every frame the object is where the 30 Hz step puts it; in between on the straight line
towards it. Second-order quantities (speed, fall and climb velocity, damped orbit speed, self-spin) change only at the decision.

- **When the decision is made:** in the first tick of a frame; the remaining `Rate/30 − 1` ticks only fly the remainder. The phase belongs to the object (it decides again as soon as its frame
  has been flown), not to the world grid – an object that is created, dies or receives a command in the middle of a frame has its own grid. The exception is the Spectre (row 15/16):
  its howitzer cadence lies on the world grid (`tick % HowitzerFiringRate`), so it decides there.
- **Events and positions:** what triggers the decision (state change, arrival, impact, opening of the parachute) falls on the first tick of the frame – the same game time as at
  30 Hz. The position reaches its 30 Hz value with the last tick of the frame, that is, up to `Rate/30 − 1` ticks (half or three quarters of a frame respectively) after the 30 Hz tick. This is the consequence
  of the subdivision, not an error; an observer who measures a threshold on the position sees it later by at most that much.
- **The frame that reaches a target** (ground, waypoint, parking place) puts the object there immediately, like the 30 Hz step – it is no longer distributed.
- **Commands:** a parked jet without an order consumes no frame, it checks every tick; a take-off command takes effect in the tick in which it is given. A command to an aircraft in
  winged flight is accepted immediately and steered towards from the next frame (at most 1 tick later at 60 Hz, 3 at 120 Hz); the running frame is flown to its end so that the path
  stays the same 30 Hz path. An attempt to abort the frame at the command made the loop back to the target different by up to 0.37 s (Raptor) and was discarded.
- **"One tick later":** the first update after appearing or after the death stays one tick later (`SleepNone`). With that, e.g., the expectation `death + 1 + Frames(74)` of the
  helicopter test holds at every rate. Relative to a `KillCommand`, which takes effect at the beginning of a step, the events thereby lie up to `Rate/30 − 1` ticks earlier than at 30 Hz
  (within one frame); relative to damage at the end of a step they lie exactly.
- **Computation values:** `FlightFrames.PerFrame`/`PerFrameSquared` turn the catalogue values per tick or per tick² into values per frame (factor 1 at 30 Hz, 2 and 4 or 4 and 16 respectively on the
  raw value, exact). The gravity per frame² comes from the definition value itself (`FlightFrames.GravityPerFrameSquared`) and is the same number at every rate. Where a module has its own
  data (helicopter crash, tunnel), its data record holds the per-frame values of the 30 Hz catalogue at every rate; the computation of the step is then bit-identical at every rate
  (at 30 Hz the same values as before, the input manifest is unchanged).
- **State:** the not yet flown remainder of a frame is state (`FlightFrameStep`: ticks left and share per tick). It is only written as long as a frame is being flown; at
  30 Hz a frame is one tick, it never exists there. The test `RateFlightFrameStepsSurviveSnapshots` checks per module: at 60 and 120 Hz the key is in the snapshot in the middle of the frame and
  the restored world continues for 12 frames with equal hashes; at 30 Hz it is never in it.

New file: `src/CnCNew.Simulation/Time/FrameGrid.Flight.cs` (`FlightFrames`, `FlightFrameStep`).

## 2. Causes

| # | §5 entry | Finding before (30 / 60 / 120 Hz) | Correction (file, symbol, principle) | after | Commit |
|---|---|---|---|---|---|
| 1 | 5.2 `AIUpdateInterface.MoveWinged`/`SteerWinged`, `JetAIUpdate.CircleStep` (orbit ≈ 0.93 · radius depends on the step) | Suite `flight`: flight time from lift-off Raptor 19.5667 / 19.6333 / 19.6583 s, Aurora 18.2667 / 18.8000 / 19.0833 s, MiG 19.8000 / 20.1167 / 20.2583 s | `Phase5Modules.cs` only `MoveWinged`/`SteerWinged` and the helpers next to them; `JetAIUpdate.Flight.cs` `CircleStep`: turn, speed change and distance of one frame, shares per tick (`_wingStep`, shared by both), arrival radius one frame of distance | Raptor 19.5667 / 19.5667 / 19.5667, Aurora 18.2667 / 18.2667 / 18.2667, MiG 19.8000 / 19.8000 / 19.8000 s (final state); test: 900 frames of flight and circling, arrival in frame 216 / 216 / 216, largest distance to the 30 Hz path 0.0002 / 0.0002 units | `bbee8ab` (+ `6601ef1`) |
| 2 | 5.2 `JetAIUpdate`: `TaxiAlongPath` (radius `max(Speed, 1)`), `TurnTo`, `TakeOff` (both branches), `Land` (both branches); 5.6 `PerOriginalFrameToPerTick` at the use | Take-off Raptor 4.8333 / 4.7667 / 4.7250 s, MiG 4.9667 / 4.9167 / 4.8833 s; produced until parked 1.3667 / 1.3333 / 1.3333 s; first shot of the attack 18.6333 / 18.5833 / 18.5417 s | `JetAIUpdate.cs` `Update`/`FlyOn`: the states decide once per frame (a state change costs one frame), their movement in frame units, shares per tick (`_frame`); hand-over of the runway "after the next step" one frame later; the three own speeds are constants per frame, no division any more | Take-off 4.8333 × 3 and 4.9667 × 3 respectively; parked 1.3667 × 3; first shot 18.6333 / 18.6500 / 18.6333 s; every state change of the Raptor sortie at the same game time (trace) | `60fcb57` |
| 3 | 5.2 `JetAIUpdate.ControlHeight` (`Speed/8` as acceleration, `Speed/2` as cap) | Comanche "climbs to flight height" 1.6667 / 1.6500 / 1.6333 s | `JetAIUpdate.cs` `ControlHeight`: one step per frame, lift, gravity, speed and climb velocity (`_verticalSpeed`) per frame, height change in shares | 1.6667 × 3 | `bb00e5f` |
| 4 | 5.2 `JetSlowDeathBehavior.Update` (fall) | Test: impact in frame 49 / 50 / 50, gone in 52 / 53 / 53, distance to the 30 Hz path 2 / 1 units | `FlightModules.cs`: fall velocity per frame, increased once per frame, step in shares (`_fall`) | 49 × 3, 52 × 3, 0 / 0 | `307bf14` |
| 5 | 5.2 `ParachuteContain.Update` (free fall, descent, arrival radius one tick distance) | Test: opens after 16 / 16.5 / 16.5 frames, rider on the ground after 106 / 106 / 106.5, distance to the 30 Hz path 0.3022 / 0.5056 (steered 0.3022 / 0.7111) | `FlightModules.cs`: descent as a frame step, sink and steering speed per frame, landing point within one frame of distance, shares (`_descent`); the rider is carried along every tick | 16 × 3, 106 × 3, 0 / 0 | `8435ba8` |
| 6 | 5.2 `CountermeasuresBehavior.MoveFlares` | Test: distance of the first flare in 25 frames 127.7778 / 129.1667 / 129.8611 | `FlightModules.cs`: the braking of one frame once per frame, counted from the first step of the flare; in between equal speed, movement per tick; no new state | 127.7778 / 127.7777 / 127.7777 | `c5a8a35` |
| 7 | 5.2 `ParticleUplinkCannonUpdate.MoveBeam` (manual, waypoint change) | Test: second waypoint after 10 / 9.5 / 9.5 frames, last after 19 / 18 / 17.75, distance 10 / 15 units | `FlightModules.cs`: travel as a frame step (distance of one frame, at most the remainder; next waypoint from the following frame), shares (`driveLeft`) | 10 × 3, 19 × 3, 0 / 0 | `5954338` |
| 8 | 5.1 `HelicopterSlowDeathData.SpiralOrbitForwardSpeedDamping`; 5.2 `HelicopterSlowDeathBehavior.Update` (spin, orbit, fall, comparison `Tick − _lastSelfSpinUpdate > SelfSpinUpdateDelay`) | Suite `timed`: Comanche last position 26.524 / 20.950 / 24.518, spin sum 18.11652 / 17.17403 / 17.06059 rad; Helix lowest height 0.000 / 0.798 / 0.935, last position 146.079 / 144.093 / 141.509 | `TransportModules.cs` `Spiral`: frame step with damping once per frame, shares (`_spiral`); self-spin and waiting time on the ground change at frame boundaries and count frames; `HelicopterSlowDeathData` holds the per-frame values of the 30 Hz catalogue at every rate | all 18 quantities of the three cases pass: 26.524 × 3, 18.11652 × 3, 0.000 × 3, 146.079 × 3; test: impact 95 × 3, gone 111 × 3, distance 60.5227 × 3 | `cee88c4` |
| 9 | 5.3 `HelicopterSlowDeathBehavior.BeginSlowDeath` (`_bladeFlyOff` over unrounded ticks) | Test: rotor flies off after 30 / 30.5 / 30.5 frames | `TransportModules.cs`: bounds in unrounded frames of the 30 Hz catalogue (one draw, the same range at every rate), counter −1 per frame | 30 × 3 | `82dbdba` |
| 10 | 5.2 `TunnelContain.Update` (`MaxHealth / TicksForFullHeal` per tick) | Suite `overtime`: frames with healing 75 / 76 / 76, time until full 10.3667 / 10.4167 / 10.4333 s | `TransportModules.cs`: **escape original grid** – once per frame, counted from the entry, the slice of the 30 Hz division; `TicksForFullHeal` in frames | 75 × 3; 10.3667 / 10.3833 / 10.4000 s (remainder: the way into the tunnel) | `81931ab` |
| 11 | 5.6 `ChinookAIUpdate.Update` (`climb = locomotor.Speed`) | Test (distance not a multiple of the step): passenger outside after 22 / 21.5 / 21.5 frames, back up in frame 43 / 42 / 42, distance 2.3333 / 2.3333 | `TransportModules.cs`: height step per frame, shares (`_climb`) | 22 × 3, 43 × 3, 0 / 0 | `0bf4e6c` |
| 12 | 5.2 `SpectreGunshipUpdate.Fly`, `Orbit` | Test: begins to circle after 42 / 42.5 / 42.75 frames, flies off after 132 / 132.5 / 132.75, distance 9.1849 / 13.8413 | `AirWeaponModules.cs` only `SpectreGunshipUpdate`: flight as a frame step on the world grid, target point of the orbit, change into the orbit and departure check at the decision, shares (`_flight`) | 42 × 3, 132 × 3, 0 / 0 | `e7a3153` |
| 13 | 5.2 `SpectreGunshipUpdate.Strafe`, `_okToFireHowitzerCounter` | Test (computer player, target point has to wander 2.8 steps): first howitzer after 48 / 51 / 51 frames, 26 / 23 / 23 shells | `AirWeaponModules.cs` `Strafe`: frame step on the world grid, "at the target" when remainder < step of one frame, one counter per frame, follow-up compared in frames, shares (`strafeLeft`) | 48 × 3, 26 × 3 | `fc0480f` |

Two commits correct a measurement, not a simulation:

- `234f3c3` – the sortie case of the suite (`RateCases.Flight.cs`, `MeasureSortie`) gave its second and third command one tick after the seen arrival: a whole frame at
  30 Hz, a quarter at 120 Hz. The suite itself demands that a measurement begins at a frame boundary (`docs/SIMULATION-CORE.md` §39.1); both commands now get
  `RateScene.Align` before them (without effect at 30 Hz). No tolerance, no quantity is touched. "Turnaround: command until reversed course" Raptor and MiG 1.4333 / 1.4667 / 1.4750 s →
  1.4333 / 1.4500 / 1.4500 s; Raptor "landing command until touchdown" 16.2333 / 16.2500 / 16.2583 → 16.2333 × 3.
- `1927883` – "Chinook: helicopter slow death …" expected `hit + 1 + Frames(9)` (nine frames and one tick); correct is `hit + Frames(10)` (the same number at 30 Hz).

## 3. Suite, before → after

Failures outside the tolerance at 60 / 120 Hz, measured with `--only "RateEquivalence: <gruppe>"`:

| Group | Quantities | Starting state | Final state | of which this package | The remainder belongs to |
|---|---|---|---|---|---|
| `flight` | 74 | 21 / 21 | **9 / 11** | 12 / 12 → **0 / 2** | 4 × production time "N frames minus one tick" (package TIMING); 5 × air movement of the helicopters via `AIUpdateInterface.Move` (package GROUND: Comanche "command until arrival", "reversed course until arrival", "landing command until touchdown"; arrival time of Comanche and Chinook) |
| `timed`, only "helicopter slow death" (3 cases, 18 quantities) | 18 | 7 / 7 | **0 / 0** | 7 / 7 → **0 / 0** | – (group in total 53 / 56 → 46 / 49) |
| `overtime`, only "heal inside GLATunnelNetwork" (5 quantities) | 5 | 2 / 4 | **0 / 2** | 2 / 2 → **0 / 0** | "first" and "second frame with healing" at 120 Hz (7.9000 / 7.9167 / 7.9583 s): the walk into the tunnel, package GROUND (group in total 80 / 84 → 78 / 82) |

The eleven remaining failures of the group `flight` at the final state: production time of Raptor, Aurora, MiG and Comanche (19.9667 / 19.9833 / 19.9917 s and correspondingly;
60 and 120 Hz); Comanche "command until arrival" 21.8667 / 21.9500 / 21.9917 s, "reversed course until arrival" 13.3333 / 13.4500 / 13.5167 s, "landing command until touchdown"
12.7667 / 12.8333 / 12.8667 s; arrival time of the free Comanche 13.5333 / 13.6167 / 13.6583 s and of the Chinook 11.7000 / 11.7833 / 11.8417 s (all 60 and 120 Hz); and the two
of this package, only at 120 Hz (§5). Five quantities of the paradrop are unmeasured at every rate (§6). All other quantities of the jets – taxiing, take-off, flight time, turnaround, landing,
parking, first and last shot – lie within the tolerance at 60 and 120 Hz, most of them exactly (take-off, arrival, "produced until parked": deviation 0).

## 4. The escape "original grid" (Ausweg „Originalraster“)

Exactly one place runs as a whole only once per frame:

| Place | What | Why |
|---|---|---|
| `TunnelContain.Update` (`TransportModules.cs`) | Healing of an occupant once per frame, counted from the tick of its entry | The original heals once per frame; healing is neither movement nor command acceptance nor target acquisition. Slices per tick are numbers rounded on their own that do not add up to the slice of a frame (100/30 rounds up, 100/60 and 100/120 down) – the last healing fell on a different frame –, and an occupant that enters between two frames would be healed more in one frame. |

Not an escape but the subdivision principle (decision per frame, movement per tick) are: the states of the jet (`JetAIUpdate.Update`: a state change costs one frame; timers,
attack locomotor, damage while circling and the command acceptance stay per tick), the step of the helicopter crash including rotor counter and waiting time on the ground, the wandering and the
counter of the Spectre target point (the target point wanders in shares per tick, the Gattling fires per tick), the braking of the flares.

## 5. Open and why

- **`flight`, MiG "landing command until touchdown" at 120 Hz: 14.6333 / 14.6333 / 14.5667 s (−8 ticks).** The arrival at the approach point is decided three frames earlier (trace: change
  to "waits for landing" at 6,840 / 6,840 / 6,828 hundred-and-twentieths). The positions of the three rates differ there by about 0.01 units, because the catalogue values per tick² are rounded
  separately for every rate (16 · a₁₂₀ is not a₃₀: up to 8 raw-value units, relatively about 10⁻⁵); the arrival rule of winged flight ("target lies behind me within the
  turning circle") tips over at a threshold. Not fixable without giving `LocomotorInfo` the per-frame values of the 30 Hz catalogue (a catalogue field, not in this package).
- **`flight`, Raptor attack "command until parked and reloaded" at 120 Hz: 60.8000 / 60.8000 / 60.5333 s.** The pursuit ends one frame earlier (5,372 / 5,372 / 5,368), the
  loop back to the airfield is 7 frames shorter after that. The same cause: a threshold of the arrival tips over at a hundredth of a unit, and the path afterwards amplifies it.
- **Command in the middle of a frame** (winged flight): takes effect from the next frame, at most 3 ticks later at 120 Hz (§1). Together with the lag of the position an observer can see a threshold
  on the heading at 120 Hz up to 6 ticks after the 30 Hz value if it gives the command in the middle of the frame; given at the frame boundary it is at most 3.
- **Paradrop of the special power** (suite `flight/paradrop`): stays unmeasured at every rate (5 quantities). The cause is not a rate difference and is not fixed, because the
  correction changes 30 Hz (§6).
- **`DeliverPayloadAIUpdate`** is in no row of §5 and is not touched: its flight is the air movement of `AIUpdateInterface.Move` (package GROUND), `CloseEnoughToTarget`
  and the low-level flight are checked per tick (finer sampling, no case of its own in the suite).
- **Not measured:** the jet attack over a real match at 60 Hz, aircraft of the generals other than Raptor, Aurora, MiG and Comanche, airfields with model bones (the suite builds without
  model geometry), several jets at one runway at 60/120 Hz (`FindWaiter`), a jet above a dead airfield. No human has seen a flight at 60 Hz.

## 6. New findings

- **The paradrop of the special power drops nobody at any rate** (also at 30 Hz). Two causes, both in the creation, not in the suite:
  (a) `SimulationWorld.ExecuteDeliverPayload` queues the payload **before** its parachute; when the Ranger is created its container does not exist yet, it stays standing at the map edge
  and the parachute stays empty (`ParachuteContain.Update` kills a parachute without a rider). Swapped on a trial basis (not committed), the five Rangers sit in their parachutes.
  (b) After that the cargo plane does not take the parachute on board: `AmericaJetCargoPlane` allows `INFANTRY VEHICLE PROJECTILE DOZER PARACHUTABLE`, `AmericaParachute` is `PARACHUTE`
  with `TransportSlotCount 0`; `SpawnEntity` asks `IsValidContainerFor`, the original (`DeliverPayloadNugget::create`, `ObjectCreationList.cpp`) calls `addToContain` without this question.
  The parachutes then land in the frame of their creation 3,000 units from the target. Fixing both changes the behaviour at 30 Hz (drops in skirmishes and missions) and is therefore
  not done here. The script-controlled drop (`ScriptEngine.Reinforcements.cs`) is not affected.
- **The reversal of the self-spin of the helicopter crash depends on the last bit of a sum** when `(MaxSelfSpin − MinSelfSpin)` is a multiple of `SelfSpinUpdateAmount`
  (Comanche): with values per tick of the respective rate it tipped over one interval later at 120 Hz (spin sum 18.117 → 18.530 rad). Solved by the per-frame values of the 30 Hz catalogue in the
  data record (row 8). The same pattern applies everywhere where catalogue values per tick are added and compared against a bound.
- **Catalogue values per tick² are rounded separately per rate**; a frame step from `Wert · (Rate/30)²` is therefore not bit-identical to the 30 Hz step (relatively about 10⁻⁵ for
  accelerations). For paths without a threshold this is invisible (0.0002 units after 900 frames of winged flight), at thresholds it can cost a frame (§5). The remedy would be to carry the
  per-frame values of the 30 Hz catalogue along in the locomotor.
- **`TemplateCatalog.GravityPerTickSquared` computes a decimal division on every call** (property without a cache); the flight modules now call
  `FlightFrames.GravityPerFrameSquared` once per decision, as expensive as before per tick.
- **The howitzer cadence of the Spectre counts from tick 0 of the world** (`tick % HowitzerFiringRate`), not from the sortie: two sorties that begin one frame apart fire the first
  shell up to one cadence period (10 frames in the data) differently after the command – at every rate.
- **`HelicopterSlowDeathBehavior`: `_lastSelfSpinUpdate` begins at 0** ("since the beginning of the world"); a crash in the first `SelfSpinUpdateDelay` of the world changes the self-spin only
  later. The same at every rate, only visible in tests with a very young world.

## 7. Tests and manifests

- **Three-rate tests** (`Tests.RateInvariance.Flight.cs`, 12 tests, without real data, name their rates themselves): winged flight, jet at the airfield (states and path), shot-down jet,
  parachute (straight and steered), flares, particle beam on a waypoint path, helicopter crash, tunnel healing (entered at the frame boundary and one tick later),
  Chinook, Spectre (human and computer player), snapshot in the middle of the frame per module.
- **Simulation tests at 30 Hz:** all 429 green without real data (Release and Debug); with `--content-dir` 444 green, only the ten group tests of the suite fail (as intended).
- **Simulation tests with `--tick-rate 60` / `120`** (Release, with real data, without the ten group tests of the suite): before 6 / 14 failures (`docs/TICKRATE-MIGRATION.md` §15.2),
  after **6 / 13**. At 60 Hz the same six as before (flashes of the staged capture, four hand-calculated projectile trajectories, the raw value of the turret turn rate – none from
  this package). At 120 Hz no. 9 of the list from §15.2 ("Chinook: helicopter slow death …") is green: the case now lands on `death + 1 + Frames(74)` at every rate (before
  75 / 149 / 298 ticks). Two tests of this area had an expectation in ticks and are converted in test commits of their own (`1927883`: waiting time on the ground `hit + Frames(10)`;
  `eac35ab`: tunnel slice per frame `100 / 30`); both are green at 30, 60 and 120 Hz
- **Manifests:** `input-manifest check` identical and `reference-manifest check --set quick` 9 runs identical at the states `bbee8ab`, `60fcb57`, `bb00e5f`, `307bf14` (with
  `234f3c3`), `8435ba8`, `c5a8a35`, `5954338`, `cee88c4`, `82dbdba` (with `1927883`), `81931ab`, `0bf4e6c`, `fc0480f`. Not checked individually, covered by the respective next state:
  `6601ef1` (a name instead of a 1) and `e7a3153`; `9a43920` (no measurement of the step at 30 Hz) is covered by the complete run. `--set full`: **50 runs identical**, input manifest identical, at the state `c092a45` (after the last commit on the simulation, `9a43920`, and the two test commits).
- **Build:** `dotnet build CnCNew.slnx` 0 warnings, 0 errors; `SourceRules` green (the new file `Time/FrameGrid.Flight.cs` and all changes included).
- **Trial run at 60 Hz with real data:** `SimTool ai-match … --map "Maps/Alpine Assault/Alpine Assault.map" --factions FactionAmericaAirForceGeneral,FactionChina --max-ticks 36000
  --tick-rate 60`: no exception, second run tick-identical, continuation from the snapshot at tick 18,000 identical (final hash `280602C8DA0A290D`, no winner in 10 game minutes;
  two Chinooks fly in the run, the AI builds no airfield in this time – jets have been run at 60 Hz with real data only via the suite).
- **Suite in total at the final state of this branch** (only this package is contained): outside the tolerance 458 at 60 Hz and 606 at 120 Hz (starting state 479 / 625).

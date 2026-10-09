# Phase 23, package "Physics": multiplicative factors, mixed recursions, small per-tick physics and heal slices

As of 8 October 2026, branch `worktree-agent-a1997b956694a9a6c`, base `7b54374`. This document belongs to one of six parallel
correction packages of Phase 23 (`docs/handoffs/HANDOFF-PHASE23.md`, step 3). It describes only what this package changed and measured; the
shared documents (`docs/TICKRATE-MIGRATION.md`, `docs/SIMULATION-CORE.md`) are untouched and are updated when merging.

**No human has seen any of this.** All numbers come from runs of this day (Release, real data `game/content`).

## 1. Principle

The 30 Hz step is the definition. A recursion that the original executes once per frame (velocity under a factor, angular velocity
under an acceleration, fall under gravity, damping) runs **once per original frame in the units of the original**; what it moves
(position, angle, height) advances **every tick** by its share of the frame step. At every frame boundary the state is the 30 Hz state, in between the
straight line between the two.

- **The frame is that of the object**, not the grid of the world: it begins with the update in which the movement began (`FramePhase`, state of the module,
  only written as long as it is not zero – at 30 Hz never; snapshots and hashes at 30 Hz stay byte-identical).
- **Shares sum exactly:** `FramePhysics.FrameShare(step, k)` = `floor(step · k / n) − floor(step · (k − 1) / n)` on the raw value; the
  shares of a frame add up to exactly the frame step. Where the target of a frame is fixed in advance (avalanche, scaffold), the object advances per tick by
  "remainder divided by remaining ticks" and is exactly at the target with the last tick.
- **Catalogue values per tick are read back per frame** (`PerTickToPerOriginalFrame`, `PerTickSquaredToPerOriginalFrameSquared`): no new field on
  a catalogue type, the input manifest is identical. At 60/120 Hz the last bits of such values deviate from 30 Hz (gravity per tick² times
  4 or 16 respectively); the tests state a bound of 0.001 units there, otherwise they demand bit equality.
- **At 30 Hz** a frame is one tick, every share the whole step, the phase always zero: every expression is the previous one.
- New helper file of this package only: `src/CnCNew.Simulation/Time/FrameGrid.Physics.cs` (`FramePhysics`, `FramePhase`).

**Same tick as the trigger.** If the first step of a movement falls into the same tick as its trigger at 30 Hz, the first frame at
60/120 Hz can only end `n − 1` ticks later (a frame cannot begin before its trigger). Where the beginning is known beforehand, the movement begins
correspondingly earlier (flood wave); where the first step moves nothing, the frame ends in the triggering update (collapse); otherwise an offset of
less than one frame remains (ferry, when the dock pulls in the tick of the request; Battle Bus, when the death falls on a hull check tick).

## 2. Causes

Values: 30 / 60 / 120 Hz. "Suite" is the rate-equivalence suite (`SimTool rate-compare`), "test" the new three-rate test in
`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Physics.cs`.

| § 5 (`TICKRATE-MIGRATION.md`) | Finding before | Correction (file, symbol, principle) | after | Commit |
|---|---|---|---|---|
| 5.1 `ToppleUpdateData.InitialAccelPercent`; 5.2 `ToppleUpdate.Update` | Suite: case "pushed until lying" tree and statue 1.2000 / 0.8000 / 0.5667 s, wall 0.2667 / 0.2167 / 0.1667 s, fence 0.6000 / 0.4167 / 0.3000 s (4 of 4 measured) | `ToppleAndFallModules.cs`, `ToppleUpdate.Update`, `ApplyTopplingSpeed`: angular velocity and acceleration per frame(²), step of a frame fixed with the first tick, angle per tick in shares, bounce/acceleration with the last tick; bounce limit per frame. Input in distance per **original frame** | 1.2000 / 1.2000 / 1.2000; 0.2667 ×3; 0.6000 ×3; angle 1.52171 at every rate. Test: angle bit-identical at every frame boundary | `021b318` |
| 5.1 `StructureToppleData.StructuralDecay`; 5.2 `StepToppling`, `ApplyCrushingDamage`, `_doneAtTick` | Suite (one retail object): lies after 4.3333 / 4.3000 / 4.3000 s (passed only within the tolerance) | `StructureToppleUpdate.StepToppling`: acceleration and decay with the first tick of the frame, angle per tick in shares (exact shares of the fine value), crush shot and end with the last tick, "Done" one frame later (`>` instead of `>=` against `Tick + n − 1`), without a delay first update one frame after the death; `Angle` capped at the quarter circle | 4.3333 ×3. Test (two buildings): angle bit-identical, the same damage events in the same frames | `f8d0077` |
| 5.1 `RailroadData.Acceleration`/`Braking`/`Friction`, `RailroadBehavior.PushStart`, start-up value, standstill threshold, damage `Speed · 10`, `Collide`, "carriage only in the next tick" | Suite: top speed after 2.4333 / 1.1000 / 1.0000 s; braking from 15.5000 / 14.4167 / 14.0500 s; stands after 19.8000 / 16.1833 / 14.7417 s at 2,703.3 / 2,617.9 / 2,578.3 | `RailroadModules.cs`, `RailroadBehavior`: every velocity of the state is distance per frame; conductor and friction with the first tick of the frame of the locomotive or of the leading carriage respectively, track position per tick in shares, `onCollide` and leaving the track with the last tick; a pulled carriage learns from the puller whether the frame is open; waiting time in frames, first update one frame after appearing, uncoupling after the same game time | 2.4333 / 2.4167 / 2.4083; 15.5000 / 15.4833 / 15.4750; 19.8000 / 19.7833 / 19.7750; 2,703.328 ×3 (4 failures per rate → 0). Test: locomotive, carriage, rammed vehicle, deaths bit-identical | `9871bc2` |
| 5.1 `TensileFormationUpdate.Friction`, `NewShare`/`LinkShare`, `BaseSlope`; 5.2 physics block | Suite measures nothing (flat map). Test on a slope with a plain: slide distance 65.528 / 69.705 (60 Hz); last frame with sinking 186 / 106; pure slope 90.31 / 184.73 | `FieldEffectModules.cs`, `TensileFormationUpdate.Update`, `PlaceOf`: recursion with the first tick of every frame of the slide phase (frame number from `_life`), target of the frame in the state, per tick an equal share of the remainder; for neighbours a stone that has begun its frame stands at the frame end | 65.528 ×3; 186 ×3; 90.31 ×3; all positions bit-identical at 339 frame boundaries | `302e88a` |
| 5.3 kind `Register` (first update one tick after appearing) – here: own `OnSpawn` of avalanche and Battle Bus | Suite: "avalanche notices the damage" 0.9667 / 0.9500 / 0.9417 s | `TensileFormationUpdate.OnSpawn`, `BattleBusSlowDeathBehavior.OnSpawn`: first wake-up one frame after appearing | 0.9667 ×3 | `29df2ba` |
| – (measurement of the suite) | Case "battle bus second life": `KillCommand` kills unstoppably, no second life, throw unmeasured | `RateCases.Timed.cs`, `MeasureBattleBus`: lethal explosion damage via `DamageCommand` (the first death, for which the module is written); no tolerance, no quantity changed | now measures: apex 45.200 / 44.572 / 44.259 (failure, before the correction) | `be4d800` |
| 5.2 `BattleBusSlowDeathBehavior.Update` | Suite (after `be4d800`): apex 45.200 / 44.572 / 44.259; in the air 2.3667 / 2.3500 / 2.3500 s. Test: landing in frame 72 / 71 | `VehicleModules.cs`: climb velocity per frame, height per tick in shares, gravity and landing with the last tick | 45.200 ×3; 2.3667 / 2.3667 / 2.3750 s; landing frame 72 ×3; heights within 0.001 | `d311db0` |
| 5.2 `StructureCollapseUpdate.Update` | Suite passed; ticks death/beginning/down 1/23/76 · 2/46/152 · 4/92/304 (2.5000 s at every rate, before as after); visual height deviated by the Euler order (about 0.9 units after 53 frames at 60 Hz, calculated) | `StructureModules.cs`: sink velocity per frame, the triggering update ends a frame (like the first step of the original), height per tick in shares, gravity/burst/end with the last tick | Test: end and effects in the same frames, visual height within 0.001 at 200 frame boundaries | `01535b0` |
| 5.1 `BridgeScaffoldBehavior.Update`; 5.6 `PerOriginalFrameToPerTick` in `BridgeModules` | no suite case; geometric decay per tick, one `Fixed` division per update | `BridgeModules.cs`, `FrameEnd`: step of the original with the first tick of the frame, per tick an equal share of the remainder, arrival and next movement with the last tick; no division by the rate factor in the update any more | Test: position and movement bit-identical at every frame boundary until the destruction | `501350c` |
| 5.2 `RailedTransportDockUpdate.DoPullInDocking`, `DoPushOutDocking` | no suite case. Test: inside or free respectively after 16 frames (30 Hz), at 60 Hz after 15 frames and one tick | `FerryModules.cs`: pulling/pushing per tick as before, arrival question (6 or 3 units respectively) with the last tick of every frame from the beginning | 16 and 16 frames at every rate, position within 0.001 | `b53cbd3` |
| 5.4 `WaveGuideUpdate.Update`/`Advance`/`DoDamage` | no suite case. Test: wave destroyed in frame 164 (30 Hz), at 60 Hz one tick into frame 165 | `WorldEffectModules.cs`: path step per tick, front (wetness, toppling, damage) and end of the path with the last tick of every frame; above 30 Hz the wave starts earlier by the ticks that its first frame needs; `ToppleForce` goes to `ApplyTopplingSpeed` per frame | frame 164 ×3; all damage, deaths, destructions in the same frames; position within 0.001 | `c333364` |
| 5.6 `DynamicShroudClearingRangeUpdate.Update` | Suite passed (grid 10 units). Test: first handed-over range 3.333 / 1.667 | `WorldEffectModules.cs`: growth per frame (30 Hz amount) distributed exactly over the ticks (tick within the frame from the state counter), hand-over with the last tick of a frame | range bit-identical at 130 frame boundaries | `c5b43fa` |
| 5.6 `GarrisonContain.HealOne`, `RepairDockUpdate.Action`, `HealContain.Update` | Suite: repair dock "frames with healing" 150 / 151 / 151 (two cases); barracks 30 / 31 / 30 | `ContainModules.cs`, `RepairModules.cs`, `RetailMatchModules.cs`: the slice of the original (amount over the duration in frames) once per frame, counted from the entry or the first action respectively | 150 ×3 (twice); 30 ×3; healed amount in 60 s equal. Test: health bit-identical at every frame boundary | `b05da71` |
| 5.2 `HeightDieUpdate.Update` (`_lastZ` of the previous tick) | Suite: "slow death AirF_AuroraBombGas" 0.0000 / – / –. Test: death after 1 / 0, 4 / 3, 21 / 20 whole frames (30 / 60 Hz) | `ToppleAndFallModules.cs`: height check with the last tick of every frame from the first update | 0.0000 ×3; 1, 4, 21 frames at every rate | `5440888` |
| – (measurement of the suite) | "collapse: start to down" 2.4667 / 2.4833 / 2.4917 s, although the simulation needs exactly 75 / 150 / 300 ticks: two chained `StepUntil` lose one tick instead of one frame | `RateCases.Timed.cs`, `MeasureStructureCollapse`: result of the second `StepUntil` plus one | 2.5000 ×3 | `1f87669` |

### Suite numbers of the two groups (quantities outside the tolerance)

| Group | Quantities | before at 60 / 120 Hz | after at 60 / 120 Hz | Cases with a failure before → after |
|---|---|---|---|---|
| `timed` | 383 | 53 / 56 | 43 / 46 | 33 → 27 |
| `overtime` | 418 | 80 / 84 | 77 / 81 | 32 → 30 |

"Before" is the first complete run (`docs/SIMULATION-CORE.md` §39.6). The Battle Bus case had no failure there (unmeasured), with the
corrected measurement one and after the correction none. What still fails in `timed` does not belong to this package: charging from creation (6),
stealth/unstealth (17), flashes of the capture (9), helicopter crash (7), mines (3), two slow deaths, one lifetime and "command until
toppled" of the tree at 120 Hz (6.4667 / 6.5000 / 6.5083 s: the drive of the tank). In `overtime`: fields (59), self-healing (4), base regeneration
(2), speaker tower (4), heal radius/hospital/repair bay and the paths in barracks and tunnel (12, among them `TunnelContain` 75 / 76 / 76).

## 3. Uses of the escape "original grid" (Ausweg „Originalraster“)

Every place runs with the last (or first) tick of a frame **of the object**, not on the world grid; none concerns movement, command acceptance
or target acquisition.

| Place | What runs only once per original frame | Reason |
|---|---|---|
| `RailroadBehavior.Collide` | collision response of the train (killing, crush damage `speed · 10`, pushing away by a quarter of the overlap), destruction at the end of the track | damage and push are amounts per frame; per tick they would be doubled, and the quarter of the overlap decays geometrically |
| `RailedTransportDockUpdate` (pulling, pushing) | only the arrival question (6 or 3 units respectively) | the original asks once per frame; the movement runs per tick |
| `WaveGuideUpdate.DoDamage`, end of the path | sampling of the front, end of the path | a denser sampling hits objects that a coarser one passes by |
| `HeightDieUpdate.Update` | height check, "moves downwards" | compares with the height of the previous frame; per tick the object dies at a height that the original never sees |
| `HealContain.Update`, `GarrisonContain.HealOne`, `RepairDockUpdate.Action` | heal slice | the original heals once per frame; the health at every frame boundary is meant to be that of 30 Hz (healing is not movement) |

Not the escape but the subdivision principle (second order once per frame, position per tick): toppling, structure topple, train (conductor, friction),
avalanche, Battle Bus, collapse, bridge scaffold, dynamic shroud clearing.

## 4. For the merge

- **`ToppleUpdate.ApplyTopplingForce` converts once** (one marked line, `PHASE 23 MERGE`): it still takes a speed per tick, because
  `SimulationWorld.Collision.cs`, `TryTopple` (package GROUND) passes `Entity.Speed`. As soon as `TryTopple` passes distance per original frame: remove the
  factor (otherwise double conversion) and put all callers on `ApplyTopplingSpeed`. `WaveGuideUpdate` already calls `ApplyTopplingSpeed`.
- **`AirWeaponModules.cs` line 787 (neutron explosion, not in this package)** calls `ApplyTopplingForce` with `blast.ToppleSpeed`, a value per
  original frame from the data: until the marked line is removed it is multiplied by 2 or 4 respectively at 60/120 Hz (before, the same place was wrong in
  a different way: acceleration per tick² from a value per frame). Unchanged at 30 Hz.
- **`tests/CnCNew.Simulation.Tests/Program.cs`**: twelve new lines after "Collision: HeightDieUpdate …" (conflict spot if other packages insert
  there).
- **`Tests.TickRate.ModulesAM.cs`** (`TickRateModulesAmTensileFormation`): the phase of the rest check of the avalanche is now in frames.
- **`RateCases.Timed.cs`** (suite, two measurement corrections in commits of their own): Battle Bus, collapse.

## 5. Open, with reason

- **No suite case for the avalanche on a slope.** `RateScene` only knows the flat map (`FlatMap`); a slope would have changed the shared scene file of the
  suite. The three-rate test on the slope covers the recursion; the suite continues to check only beginning and duration.
- **No suite case** for bridge scaffold, ferry, flood wave, `HeightDieUpdate` (except indirectly) and `GarrisonContain` – listed as "not
  covered" in §39.5; one three-rate test each on the synthetic content.
- **Offset below one frame with a trigger in the same tick** (§1): ferry, when the dock runs after the docking object in the same tick; Battle Bus, when the
  first death falls on a hull check tick (suite: in the air 2.3667 / 2.3667 / 2.3750 s). Within the tolerance, not exact.
- **Last bits for values from the catalogue per tick:** gravity (Battle Bus, collapse), pull/push distance of the ferry, path step of the wave – the
  states are equal within 0.001 units at the frame boundaries, not bit-identical. Bit equality would need values per frame in the catalogue (a new field
  changes the input manifest).
- **Train:** a station that the train passes over completely within one frame is seen at 60/120 Hz and not at 30 Hz (track pieces
  shorter than one frame distance; not searched for in the retail data). Carriages off-stage or behind the end of the track lie at 60/120 Hz for one frame at
  a different place than at 30 Hz (they collide with nothing there; the alignment depends on the number of trains on the hook, not on the game).
- **Flood wave:** the random draw of the splash falls one tick earlier above 30 Hz (the wave starts earlier); number and sequence of the draws are equal.
- **State amount `_wantsToBeLead`, `_waitAtStationTimer`** (train) and `healthPerTick` (dock) have a different unit than their name at 60/120 Hz
  (frames or per frame respectively); the names are snapshot keys and stay.

## 6. New findings

- **Chained `StepUntil` of the suite lose one tick instead of one frame** (`RateScene.StepUntil` counts from the step that executes a command;
  a second call without a command already begins one tick later). Corrected for the collapse; other cases of the same form (e.g. stealth,
  shroud clearing in `RateCases.Timed.cs`) are not checked.
- **"Already awake" in the same tick:** `SimulationWorld.WakeAt` lets a module that runs in this tick anyway act in the same tick instead of in the
  next. Whether an event (first death of the bus) is picked up in the same or in the next tick thus depends on the phase of a periodic
  update – at every rate, but with a different weight (one tick is half a frame).
- **`HeightDieUpdate` ends `AirF_AuroraBombGas` in the first frame** (target height 40, object on the ground): the suite case "slow death" therefore only measures
  whether the object is still there one second later.
- **The Battle Bus is dead 30 s after the first death at every rate** ("alive after the first death" 0 / 0 / 0): the penalty for the empty hull;
  no rate difference.
- **Train at the end of the track:** a carriage that has left the track is put at the second-to-last track point (`FindPosition` returns, for
  "distance = track length", the point before) – at every rate, without a gameplay effect, noticed in the test.

## 7. Verification status

- **30 Hz:** `input-manifest check` identical and `reference-manifest check --set quick` 9 runs identical per commit that changes the simulation – each from
  a copy of the binaries of exactly this source state. Without a run of their own: the two pure suite commits (`be4d800`, `1f87669`) and the test commit
  `a6da89c`. At the end `--set full`: **50 runs identical**, once before and once after the clean-up commit `2dc46d9` (final state).
- **Simulation tests at 30 Hz:** Debug run `dotnet run --project tests/CnCNew.Simulation.Tests` 430 of 430 green (418 before + 12 new).
  Between `9871bc2` and `2dc46d9` the source test "Determinism rules hold in the simulation sources" failed on five lines of this package
  (bare tick counts, one constant name); `2dc46d9` fixes that without a behaviour change. The full test run ran after the first commit and at the end,
  in between only the affected groups.
- **`--tick-rate 60` / `120` (Release, without real data):** 6 / 13 failures (before 6 / 14). Gone is no. 10 from `TICKRATE-MIGRATION.md` §15.2
  ("Collision: ToppleUpdate push, kill, stump and mid-fall snapshot continuation"), without a change of the test. The test "Repair: RepairDockUpdate
  heals over TimeForFullHeal" counted heal steps per tick and is converted to frames in `a6da89c` (between `b05da71` and `a6da89c` it failed at
  60/120 Hz). The remaining failures belong to other packages (projectiles, turrets, flashes of the capture, stealth, Chinook, construction/sale, reference match).
  With real data at 60 Hz: the same 6 and the ten group tests of the suite; the run at 120 Hz with real data was not finished at the time of writing.
- **Clean build:** `dotnet build CnCNew.slnx` 0 warnings, 0 errors (without deleting `bin`/`obj` beforehand).
- **Not measured:** cost per tick at 30 Hz (added per update are one counter and one comparison, no division in the 30 Hz path except
  `ToOriginalFrames` in the heal and shroud-clearing phases); the performance gate at 60 Hz.
- Twelve new three-rate tests "Rate invariance (physics): …" (always run at 30, 60 and 120 Hz, independent of `--tick-rate`).

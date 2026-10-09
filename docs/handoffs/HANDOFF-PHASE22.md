N+1 handoff (Phase 22): Fixed-point resolution – from Q47.16 to Q39.24 (first planned change of all reference values, at 30 Hz)

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/SIMULATION-CORE.md` (numerics, snapshot, replay, reference values), `docs/DETERMINISM.md` and the final reports of Phases 20 and 21 (in particular the counters "rounded to zero" from the trial run at 60 and 120 Hz).
Prerequisite: Phase 20 is completed. Phase 21 may run in parallel (its own worktree); 21 is merged before 22.

## Why and why now

`Fixed` is a `long` with 16 fractional bits (Q47.16): resolution 1/65,536, range ±1.4 · 10¹⁴. The range is many orders of magnitude larger than any map, the resolution is tight for quantities per tick. An acceleration is divided by rate²: 1 unit/s² is 73 raw steps at 30 Hz (up to 0.7 % rounding error), 18 at 60 Hz (up to 2.7 %), 4.5 at 120 Hz. Small values round to zero. `ToppleAndFallModules.cs` already makes do with additional bits today. With eight additional fractional bits (Q39.24: resolution 6 · 10⁻⁸, range ±5.5 · 10¹¹) the same acceleration has around 4,660 raw steps at 60 Hz.

The change alters every hash, every snapshot and every replay. That is why it happens **alone, at unchanged 30 Hz and before the switch of the tick rate**: this keeps it traceable which shift comes from the resolution and which later from the rate. And it happens before any release, as long as no player has savegames.

Rejected: Q31.32 (range ±2.1 · 10⁹; squared distances across a large map lie at 10⁷ to 10⁸ and would only have one to two orders of magnitude of distance to the edge, while the gain over 24 bits is necessary for no known quantity). Whoever finds, in step 4, an intermediate value that already comes close to the edge at Q39.24 reports that before switching.

## Honest status (starting point, state `2a66661`; re-measure)

- `Fixed.FractionBits = 16`, `ScaleRaw = 1 << 16` (`src/CnCNew.Simulation/Numerics/Fixed.cs`); products and quotients via `Int128`, rounding to the nearest number (halves away from zero), overflow throws.
- `FixedTrig`: π, 2π, π/2 as hard-coded Q16 raw values; `Atan2` as CORDIC with 16 table entries (error < 10⁻³ rad); `Sin`/`Cos` as polynomials (error < 2 · 10⁻⁵). Both are coarser than the new resolution and would limit it.
- Around 170 uses of `FromRaw`, `.Raw`, `FractionBits`, `ScaleRaw` in 42 files; among them hard-coded assumptions outside the simulation such as `X.Raw / 65536f` (`src/CnCNew.Game/Live/LiveMatch.cs`), conversions in `SimulationQueries`, `FxWorldView`, `FrameProjector`, `MinimapImage`, `Suspension`.
- Snapshots (`JsonState`) and replays (`ReplayFormat`) write raw values; commands carry raw coordinates.
- Integer hash functions over raw values (turret scan, mood) in `SimulationWorld.Turrets.cs`/`.Acquisition.cs`.

**Addendum from Phase 19 (6 October 2026):** `Fixed.Mul`, `Fixed.Div` and `Fixed.Sqrt` have been hand-optimised since `40fcbc6`/`da6a4ed` (no 128-bit software division, Newton start value close to the root) and are secured with equivalence tests against the earlier version (`Tests.PerfMovement.cs`, `Tests.Indexes.cs`). These routines and their tests are tailored to 16 fractional bits and must be derived anew when changing to Q39.24; the yardstick for the speed is `docs/PERFORMANCE.md` §6a.

**Addendum from Phase 20 (6 October 2026, state `9299a2d`):** the counters of the trial run are in `docs/TICKRATE-MIGRATION.md` §11. Catalogue building (`SimTool rate-audit .local/phase2-import-a --tick-rate N`): in the used catalogue no value rounds to zero at 60 and 120 Hz (only `Acceleration = 0.1` of the `NapalmBombLocomotor`, used by no object, at 120 Hz); above 1 % quantisation error 1 value at 60 Hz and 41 values in 5 fields at 120 Hz (worst `LocomotorInfo.Braking`, 9.59 %), above 10 % none; below eight significant bits 46 values at 60 Hz and 321 at 120 Hz (`LocomotorInfo.Braking/Acceleration/Lift/TurnRatePerTick`, `TurretInfo.PitchRate/TurnRate`, `WeaponInfo.WeaponSpeed`). Run time (`--time-probe`): 0 roundings to zero with up to 5.9 million conversions per run; **not counted** are amounts per tick that modules form without the time service. `TemplateCatalog.GravityPerTickSquared` calculates in decimal on every access. The rate is now a value of the world; catalogues, snapshot and replay carry it – the change of the resolution still happens at 30 Hz. Reference values before the change: reference match tick 14,490 `23B4B25DF62765D8`/`DE5849AF9F8493C4`, stress test `56882F763EABEF67`/`B8E663493F2C73D5`, `WorldSnapshot` 9, replay 11, `CommandTypes.Version` 14, `MissionSnapshot` 2.

**Addendum from Phase 21 (7 October 2026, state `2a0a6f5`):** Phase 21 is merged. Important for this phase: `SaveGame` is version 2 (rate in the envelope) and is increased again with the change; outside the simulation the `Fixed`→`float` conversions are untouched except for two new uses of `ToFloat(…) * world.Time.TicksPerOriginalFrame` in `Suspension.cs` and one `Fixed.FromInt(…)` multiplication in `UnitConditionTracker.cs`; raw-value expectations in tests have deliberately been left standing (`Tests.Turrets.cs`: `TurnRate.Raw is > 6860 and < 6866`; `Tests.JetFlight.cs`: `FromRaw(64)`, `FromRaw(2)`; `JetOrderTests`: `FromRaw(16)`); the simulation tests have `world.Frames(n)`/`world.InFrames(ticks)`/`world.PerFrame(v)` (`tests/CnCNew.Simulation.Tests/TestTime.cs`) and the trial switch `--tick-rate N`.

## Brief (in this order)

1. **Reproduce the starting state.** Build, tests, `reference-manifest check --set full`, performance profile. In addition, for all runs of the manifest, save **result quantities** that are not hashes: winner, end tick, object count per player at the end, money, number of shots/productions/buildings; for missions the outcome and the tick of the end script. They are the standard of comparison after the change.
2. **Preparatory work without a change of results (the manifest continues to pass):**
   - Convert every place that assumes 16 bits to the constants or to conversion functions (`65536`, `<< 16`, `>> 16`, `/ 65536f`). Outside the simulation offer a single conversion `Fixed` → `float` (in `CnCNew.Presentation` or `CnCNew.Game`, not in the simulation – there `float` remains forbidden).
   - A source test forbids these literals in all projects.
   - Rewrite `FixedTrig` so that constants and tables follow from `FractionBits` (constants hard-coded with enough digits, never generated at run time from `Math.*`) and the accuracy fits the resolution (more CORDIC steps, higher polynomial order). At 16 bits the results must stay bit-identical to today – otherwise that alone is a change of results and belongs in step 3.
   - Check integer hash functions and everything that computes with raw values directly.
3. **The change: one commit, one cause.** `FractionBits = 24`. No other behaviour change in the same commit. Increase format versions: `WorldSnapshot`, replay, `MissionSnapshot`, `SaveGame`, `CommandTypes.Version` (commands carry raw values). Old states are rejected with a clear message, never reinterpreted.
4. **Overflow check.** The range shrinks by the factor 256. Look for the largest intermediate values (squared distances across the largest map, areas, money × rate, damage sums, the multiplication `Fixed * long` without intermediate rounding). Since every operation is `checked`, an overflow throws – the full manifest, the stress test and all campaign maps (`CampaignTool audit`, `drive`) must run without `OverflowException`.
5. **Re-measure and compare.** Rewrite the manifest (with justification in the commit). Put every result quantity from step 1 next to the new value. Expectation: results shift only through rounding – same winners in the large majority of the runs, end ticks close by. Every deviation that goes beyond that (different winner, mission with a different outcome) is examined until its cause is named: chaotic amplification of a rounding is permissible and is named as such; an error of the change is not.
6. **Determinism and performance.** The determinism matrix from Phase 18 with the new manifest (at least the rows of your own machine). Performance profile: the computing cost must not rise (the same `Int128` operations); snapshot size grows (longer numbers) – measure.
7. **Repeat the trial run at 60 and 120 Hz:** collect the counters "rounded to zero" from Phase 20 anew; the target value at 60 Hz is zero cases, every remaining case is named.
8. **Docs:** `docs/SIMULATION-CORE.md` (numerics, formats, reference values), `docs/ARCHITECTURE.md` (Q39.24), `docs/DETERMINISM.md`, `docs/MULTIPLAYER-PLAN.md`, `CLAUDE.md` (versions, reference values – mark the old ones as superseded), README, ROADMAP, IMPLEMENTATION-PLAN.

## Bottlenecks and showstoppers of this phase

- **Hidden 16.** A single overlooked literal produces no crash but values wrong by the factor 256 in the display or – worse – in the simulation. Therefore first the preparatory work with the source test, then the change.
- **`decimal` → `Fixed` on loading** now keeps more digits; values from the data change in the raw value (intended). The rounding rule stays.
- **Quantised inputs** (bone table to 1/16 unit and 1/1024 rad, terrain heights) remain exactly as accurate; only their raw value scales.
- **Duration of the re-measurement.** Campaign audit and objective driver over all maps are long; the performance work from Phase 19 is the prerequisite for this to be possible in acceptable time. Background runs with a bounded timeout, each campaign individually.

## Acceptance

The preparatory work lets the old manifest pass; the change is a single commit; all runs are re-measured without overflow; the comparison table of the result quantities is available and every conspicuous deviation has a named cause; the matrix rows of your own machine pass. The final report names old and new reference values with commit hashes and what is not verified. At the end update the "starting point" in `docs/handoffs/HANDOFF-PHASE23.md`.

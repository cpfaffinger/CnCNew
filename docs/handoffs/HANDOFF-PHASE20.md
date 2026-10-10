> **Stage:** 2 – Engine · **Milestone:** S2-M3 Tick rate as a value of the world · **Phase:** 20
> **Status:** done (6 October 2026, state `9299a2d`)
> **Predecessor:** HANDOFF-PHASE19.md · **Successor:** HANDOFF-PHASE21.md · **Results in:** docs/TICKRATE-MIGRATION.md §1–§13
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 20): Tick rate as a parameter of the world – simulation (same results at 30 Hz)

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, **`docs/TICKRATE-AUDIT.md`** (checked version from Phase 18), `docs/SIMULATION-CORE.md` (time and units, catalogues, snapshot, replay), `docs/GAME-RULES.md` and the final report of Phase 19.
Prerequisite: Phase 19 is completed; the reference manifest passes.

## Goal in one sentence

The simulation no longer knows a hard-coded tick rate; the rate is a value of the world. **At 30 Hz nothing changes** – no hash, no snapshot byte. At 60 and 120 Hz the simulation runs through without errors, without its behaviour there being assessed yet (that is Phase 23).

## Honest status (starting point; numbers from `docs/TICKRATE-AUDIT.md`, checked there)

- `SimulationTiming.LogicFramesPerSecond = 30` is a `const`; 61 uses in 32 files of the simulation, all conversions are static functions.
- The catalogues (`ObjectTemplate`, `WeaponCatalog`, `UpgradeCatalog`, `SpecialPowerCatalog`, `TurretInfo`, every `Parse(configuration)` of a module via `ModuleConfigReader.Ticks`) convert to ticks when they are built. The parsed data is therefore rate-dependent, but the catalogue is built once per data set.
- Around 80 bare tick counts in the simulation and around 30 in `Simulation/Testing`; in addition integer divisions of the rate (`30 / 4 = 7`).
- Data that is consumed as raw frame values: fallback values of `ModuleConfigReader.Ticks`, pull, damping and throw values, `InstantSpeed`, `TurretInfo.DefaultRate`, the frame counters of the map scripts (`SET_TIMER`, `*_FOR_FRAMECOUNT`).
- `WorldSnapshot`, `MissionSnapshot`, replay header and `ReplayRunner` write the rate and reject any other than 30; `Tests.State.cs` checks that 60 is rejected.

**Addendum from Phase 18 (6 October 2026):** `docs/TICKRATE-AUDIT.md` is now the checked version (state `e5046b7`): 1,959 coupled places, of which 660 in `CnCNew.Simulation` (A 39, B 267, C 81, D 43, E 138, F 12, G 15, H 65); 63 uses of the constant in 32 files, 159 bare tick counts, 15 fallback values of `ModuleConfigReader.Ticks`, 222 stored absolute tick fields in 56 files (310 tick-related fields in total). Ten contradictions to the plan are in the summary of the inventory (W1–W10) and in `docs/MULTIPLAYER-PLAN.md` §4a; important for this phase: W2 (do not scale literals that mean order), W3 (13 `const` expressions and 31 hand calculations with the constant), W6 (scripts: evaluation per tick, phase modulus `% (2*30+1)`), W8 (`SaveGame` writes no rate, `ReplayRunner.Run(replay, world, …)` does not check it). The yardstick is `SimTool reference-manifest check` (`baselines/`), no longer the hashes of the docs.

**Addendum from Phase 19 (6 October 2026, state `16d337c`):** the simulation is faster without a change of results (`docs/PERFORMANCE.md` §6a): 8 players 1.14 ms per tick instead of 4.98, `MD_GLA05` 3.4 instead of 13.5 ms; the full reference manifest (`reference-manifest check --set full`) takes around 10 instead of 27 minutes. A further `MarkDirty` error is fixed (`SpecialPowerModuleBase.PauseCountdown`; one manifest run has new hashes). New and to be observed for this phase: movement, vision and state hash have caches with exact invalidation (`NavigationGrid` remembers answers until the next footprint change, `VisionMap` keeps hash and write extents, the object description has prepared module frames) – none of them contains tick counts, but each is state outside the snapshot and must still deliver the same results after a change of the rate; the world keeps owner, kind and module-type lists (`IWorldServices`) that scripts and modules use instead of passes over the world; `Fixed` multiplies, divides and takes roots without 128-bit division (important for Phase 22: the routines are tailored to Q47.16 and have equivalence tests against the old version). Not reached and open for a decision of its own: the tick maximum (94 ms through simultaneous long pathfinding searches) and two proposals for other hash constructions (`docs/PERF-VISION.md` §7.1, `docs/PERF-HASH.md` §6–§7).

## Decisions that are fixed (`docs/MULTIPLAYER-PLAN.md` §2)

1. **Supported rates are 30 · 2ᵏ: 30, 60, 120.** Any other is rejected when the world is created. Reason: frame values of the original can be scaled exactly (× 2, × 4), and multiplicative factors per frame later need only square roots (Phase 23).
2. **Times from the data stay on the 30 Hz grid of the original.** A duration of *m* milliseconds is rounded up to whole frames as in the original (`ceil(m · 30 / 1000)`) and then multiplied by `Rate / 30`. A weapon with 50 ms reload time thus needs 66.7 ms at every rate as in the original, not 50 ms. This way the game balance stays the same at every supported rate, and the higher rate brings what it is supposed to bring: finer movement and faster command acceptance.
3. **The catalogue is built for one rate** (`TemplateCatalog` and everything that contains ticks takes the rate when built). No converting in the hot path. The rate enters the fingerprint of the catalogue.
4. **Constants of the original in frames** (`SPAWN_UPDATE_RATE = 15` and the like) are expressed via a single helper function – "*n* frames of the original" = `n · Rate / 30` ticks. Own rhythms (AI thinking, vision refresh) likewise. No bare tick literal remains in the simulation.

## Brief (in this order)

1. **Reproduce the starting state.** Build, tests, `reference-manifest check --set full`, performance profile. In addition prepare a byte-wise snapshot comparison: for five runs of the manifest save snapshots at fixed ticks (scratchpad) in order to prove byte equality after the rebuild.
2. **Time contract.** An immutable value (proposal `SimulationRate`) in the world configuration and a time service of the world with the previous conversions plus "frames of the original → ticks". The static class `SimulationTiming` keeps only rate-independent functions (degrees → radians, ratios); the constant `LogicFramesPerSecond` disappears from the simulation.
3. **Catalogues per rate.** Every place that generates ticks or values per tick when built receives the rate. `ModuleConfigReader.Ticks(…, fallback)`: fallback values are in future frames of the original or milliseconds, never raw ticks. Measure what the catalogue build costs (it now occurs per session) and whether a cache per rate is necessary.
4. **Work off the literals** – every place of the inventory (categories A to C in the simulation), individually, with the class from the inventory:
   - Original constant in frames → helper function, the source reference in the comment stays.
   - Time value that was written down as a tick count (e.g. `ForceIdleTicks = 2` for 67 ms) → as frames of the original, so that 30 Hz stays unchanged.
   - Sequence rhythm with modulo (`tick % N`) → scaled rhythm; the phase at 30 Hz is preserved exactly.
   - Integer division of the rate (`30 / 4`) → the previous result (7 frames) as frames of the original.
   - Raw data per frame: speeds and additive quantities are scaled when the catalogue is built (× 30 / rate or × 900 / rate²). **Multiplicative factors per frame** (damping, friction, `Acceleration 1.01`) and everything from category D of the inventory stay unchanged in this phase and are kept in a list for Phase 23 – they are right at 30 Hz and wrong at 60 Hz.
   - Frame counters of the map scripts (`SET_TIMER`, `*_FOR_FRAMECOUNT`): scaled as frames of the original.
5. **Sessions, scenarios, tools of the simulation.** `SkirmishMatchOptions`, `MissionOptions`, the scenarios in `Simulation/Testing`: rule values (grace period, science point interval, victory check) in frames of the original, run budgets (`MaxTicks`) stay ticks. `SimTool`/`CampaignTool`: `--tick-rate N` (default 30 until Phase 23), outputs in game minutes from the rate instead of `/ 30 / 60.0`.
6. **Formats.** Snapshot and replay already carry the rate; in future the world is created *with the saved rate* instead of rejecting a foreign one (only an unsupported one is rejected). At 30 Hz the bytes stay identical, so no version increase. `Tests.State.cs` (rate 60 rejected) is replaced: 50 is rejected, 60 and 120 are accepted.
7. **Extend `SourceRules`:** forbidden in `src/CnCNew.Simulation` are the old constant, numeric rate literals and constants or fields with `Tick`/`Frame`/`Interval` in the name that are initialised with a number literal (exceptions by name in the test, each with a justification). The search patterns from `docs/TICKRATE-AUDIT.md` §8 no longer yield an unassessed hit for the simulation.
8. **Trial run at 60 and 120 Hz** (assessment explicitly *not* in scope): reference match, two AI runs, three missions run through without an exception. Counted along the way: conversions that round to zero (a value per tick became 0), overflows, violated assumptions. The counters are input for Phase 22 (fixed point) and Phase 23.
9. **Docs:** `docs/SIMULATION-CORE.md` (section time and units new), `docs/GAME-RULES.md`, `docs/SCRIPT-ENGINE.md` (frame counters), `docs/TICKRATE-AUDIT.md` (mark the simulation part as done, list "for Phase 23"), `docs/MULTIPLAYER-PLAN.md`, `CLAUDE.md`, README, ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN.

## Bottlenecks and showstoppers of this phase

- **Byte equality at 30 Hz is the only proof.** Every intermediate state at which the manifest does not pass is an error in the rebuild, not a "new reference value". Work in small commits and check after each (level `quick`), at the end `full`.
- **Rounding in conversions per tick.** `PerSecondToPerTick` rounds to `Fixed`; the order of dividing and multiplying must stay exactly the old one at 30 Hz.
- **`decimal` at run time** (`ScriptEngine.*`, `SimulationWorld.CreationLists.cs:343` with `/ 900m`): into the time service as well.
- **The catalogue is today part of `GameData`** and is shared by tools and client; with the rate as a parameter no caller may accidentally give a catalogue of the wrong rate to a world (check when creating the world and throw).

## Out of scope

Presentation, game layer, UI, sound, client (Phase 21 – after this phase it reads the rate from the world); the fixed-point resolution (Phase 22); correct behaviour at 60 Hz and the switch of the default (Phase 23).

## Acceptance

`reference-manifest check --set full` passes unchanged; the saved snapshots are byte-identical; `SourceRules` with the new rules is green; the trial run at 60 and 120 Hz runs through and its counters are documented; the performance profile shows no degradation. The final report names the number of places worked off per class with commit hashes, the list for Phase 23 and what is not verified. At the end update the "starting point" in `docs/handoffs/HANDOFF-PHASE21.md`.

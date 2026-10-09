N+1 handoff (Phase 19): Performance – lower the tick cost of real matches without changing a result

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/DETERMINISM.md`, `docs/SIMULATION-CORE.md` (tick phases, state hash, vision, navigation, performance budget) and the final report of Phase 18 with the performance profile.
Prerequisite: Phase 18 is completed and has released the programme; the reference manifest (`baselines/`, `SimTool reference-manifest check`) passes.

## Why this phase comes so early

The tick rate is to rise from 30 to 60 Hz, and eight players are to play at the same time. Both double or multiply the computing load. In addition Phases 22 and 23 must re-measure **every** reference value, and Phase 23 at double the tick count. A fast simulation is therefore a prerequisite for the goal *and* for the work on it. Optimisations are safest as long as the hashes apply unchanged as the yardstick – that is now, before the phases that change them.

## Honest status (starting point; the profile from Phase 18 is authoritative)

**Measured quietly at the end of Phase 18 (6 October 2026, state `b432fb7`, `scripts/perf-profile.ps1`, AMD Ryzen 7 PRO 6850U; tables in `docs/PERFORMANCE.md` §6 – this is the starting point, the probe below is superseded):** 8 players, Twilight Flame, 54,000 ticks: mean **4.98 ms** per tick, median 4.29, 95 % 8.36, 99 % 13.60, maximum **547 ms** (one tick, 100 % updates), 95 KB of allocation per tick, GC 616/68/2, playback without AI 4.87 ms, catch-up speed 6.8 times at 30 Hz and 3.4 times at 60 Hz, final hash `0A69FC72FF6975CD`. Phases on average: updates 3,264 µs (of which `AIUpdateInterface` 81.4 %, `TransportAIUpdate` 4.6 %, `SupplyTruckAIUpdate` 3.8 %), Vision 716, VisionHash 379, Hash 531, AI 45. With 4 players 2.07 ms, with 2 players 0.80 ms on the same map. Missions: `MD_USA05` 5.5–5.9 ms (updates 2.0–2.2, hash 1.6–1.7, vision 0.9, VisionHash 0.56, target acquisition 0.3, scripts 0.2–0.3), `MD_GLA05` 12.6–13.5 ms (updates 4.6–5.3 with `PointDefenseLaserUpdate` 30.6 %, scripts 2.8–3.0 ms and 4 MB of allocation per tick, hash 2.2, vision 1.5–1.7, VisionHash 0.8). Against the target values below around the factor 5 is missing for the mean, 4.5 for the 99 % quantile, 68 for the maximum. The stress test lies quietly at 61 µs per tick. The reference manifest (`SimTool reference-manifest check … --set full`, 50 runs) takes 25 to 40 minutes on this machine and is the check after every commit; the quick set 5 minutes.

First, not quiet probe of 5 October 2026 (see `docs/handoffs/HANDOFF-PHASE18.md`): 8 AI players on Twilight Flame (486 × 469 cells), 18,000 ticks – recording 123.1 s, playback without AI 163.0 s, that is **around 7 to 9 ms per tick** with 218 player objects at the end. The synthetic stress test needs 70–80 µs per tick with 13,220 objects. The cost of a real match accordingly does not come from the object count but from something that grows with map size, player count or real modules. Earlier measurements (Phase 17 handoff): campaign audit per map 2 to 10 minutes, total run over an hour.

Unverified leads (confirm each in the profile first, rebuild nothing on suspicion):

- The vision hash is computed every `VisionIntervalTicks` (3) over every cell for every player (`VisionMap.cs`); with 8 players and 228,000 cells that is 1.8 million cells per pass.
- `RefreshVision()` itself in the same rhythm.
- `Describe` for every changed object hashes names character by character; the global description runs in every tick.
- Target acquisition and pursuit in a fixed rhythm, pathfinding with temporarily blocked cells, collision distribution, the AI observation every 5 ticks via `EntitiesVisibleTo`.
- LINQ and allocations in hot paths (in lockstep every garbage collection is a stall that must be caught up).

## Goal (proposal; confirm with the numbers from Phase 18 or change with justification)

On the development machine, 8 players, 30 game minutes, large map, Release:

| Quantity | Target |
|---|---|
| Tick time mean | ≤ 1.0 ms |
| Tick time 99 % quantile | ≤ 3 ms |
| Tick time maximum | ≤ 8 ms |
| Catch-up speed (playback with AI) | ≥ 16 times real time at 30 Hz (corresponds to ≥ 8 times at 60 Hz) |
| Garbage collection in the running match | no Gen 2 runs, allocation per tick at rest close to 0 |
| Campaign audit per map (900 ticks) | under 1 minute |

The numbers stand for: the simulation occupies at most a small part of one core at 60 Hz, a rejoining client catches up 30 minutes in a few minutes, and the re-measurement of the reference values takes minutes instead of hours.

## Brief (in this order)

1. **Reproduce the starting state.** Build, tests, manifest (`check --set full`), repeat the performance profile from Phase 18 on a quiet machine. Without reproduced starting numbers no optimisation.
2. **Read the profile and write a ranking:** per cost centre its share of the tick time, how it grows with player count, map size and object count, and whether it can be lowered without a change of results.
3. **Work off cost centres individually, largest first.** For each: measurement before, change, `reference-manifest check` (must pass unchanged), measurement after, one commit. Permissible means: incremental instead of complete computation (e.g. vision hash as a running sum per player, only changed cells), precomputation of immutable values (name hashes), data structures without allocation, avoiding repeated work in the same tick.
4. **The state hash itself.** Its value must not change in this phase (the manifest depends on it). If the measurement shows that only a different hash construction reaches the goal, that is a separate, named decision: rewrite the manifest with justification, check all runs before/after for identical *results* (winner, end tick, object lists).
5. **Load distribution only deterministically.** If a piece of work must be distributed over several ticks (pathfinding, AI), then by fixed counting quantities (nodes per tick, order by ID), never by wall-clock time. Such a distribution changes results and is therefore not an "optimisation" but a measured behaviour change in a commit of its own with a rewritten manifest – only if the goal is not reachable without it.
6. **No threads in the simulation core** (`SourceRules`). Whether the client should put the simulation on a thread of its own is not the subject of this phase; record what would stand in the way of that (the presentation reads the running world via `SimulationQueries` and the `FrameProjector`).
7. **Measure the game layer too.** `LiveGame.SyncAfterTick` (projection, effects, fog) runs per tick; measure its cost per tick with 8 players and work off the largest places, as far as frame- and hash-neutral (golden hashes of the captures unchanged).
8. **Make the measuring tool permanent.** `SimTool perf` writes a machine-readable summary; a comparison run (`perf-compare alt neu`) shows degradations. Deliberately **no** failing test is made of it (times are machine-dependent); but from now on the run belongs to the completion of a phase.
9. **Docs:** `docs/SIMULATION-CORE.md` (performance budget, cost centres, what was changed and why it is result-neutral), `docs/MULTIPLAYER-PLAN.md` (register), `CLAUDE.md` (commands, new measured values), README, ROADMAP, IMPLEMENTATION-PLAN.

## Non-negotiable rules

`CLAUDE.md` and `docs/MULTIPLAYER-PLAN.md` §6. For this phase: **After every commit the unchanged reference manifest passes.** Every exception is a commit of its own with justification, before/after values and a comparison of results. Measurements only on a quiet machine, every number from a run, Release tools rebuilt before every measurement.

## Acceptance

The target values are reached or the remaining cost centres are named individually, with their share and the reason why they do not drop further without a change of results. The manifest is unchanged (or every change justified individually). The final report contains the table before/after per cost centre with commit hashes and the projection to 60 Hz. At the end update the "starting point" in `docs/handoffs/HANDOFF-PHASE20.md`.

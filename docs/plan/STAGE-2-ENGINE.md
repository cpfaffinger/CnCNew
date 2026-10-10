# Stage 2 – Engine

As of: 10 October 2026, state `2777a90`. Frame: [PROGRAMME.md](PROGRAMME.md). The technical frame of the tick-rate and
fixed-point programme and the register of bottlenecks (E1 to E24) are in `docs/MULTIPLAYER-PLAN.md`; the measured state of
every row is kept there.

## 1. Goal, rule, yardstick

The engine grows beyond the original – tick rate as a value of the world (60 Hz product rate, 30 and 120 Hz supported),
Q39.24 fixed point, performance, determinism across machines, a session core that a network can drive, structure and
patterns of the code – while the game stays, by its assets and data, the same game: times from the data lie on the 30 Hz
grid of the original, the balance does not move, a match at 60 Hz is the match of the original. **Yardstick:** the
reference manifest (unchanged, or changed once for one cause in a commit of its own), the rate-equivalence suite and the
distribution comparison. **Rule:** no rule of the game changes here; a behaviour change that an engine phase needs is
booked as a fidelity fix (Stage 1) or an option (Stage 3).

## 2. Milestones

### S2-M1 Measurement basis — done; second machine open

- **Delivered (6 October 2026, state `b432fb7`; Phase 18):** `baselines/reference-manifest.json` (50 runs: reference match,
  stress test, 16 AI runs, 8-player runs, skirmish overview, 27 mission runs) and `baselines/input-manifest.json`
  (fingerprints of definitions, bone table, terrain, maps, catalogues) as the yardstick; `SimTool reference-manifest
  check|write|list|show|results`, `input-manifest check`; the determinism matrix (13 rows on one machine: Release, JIT and
  GC switches, three cultures, Linux under WSL2) identical; findings D1–D9 confirmed or refuted with a test and fixed
  (culture in the script state, incomplete fingerprints, platform line endings, missing `MarkDirty`); `SourceRules` covers
  the code that feeds the simulation; the checked tick-rate inventory (`docs/TICKRATE-AUDIT.md`, 1,959 places); the
  performance profile (`SimTool perf`, `scripts/perf-profile.ps1`). Documents: `docs/DETERMINISM.md`,
  `docs/DETERMINISM-FINDINGS.md`, `docs/PERFORMANCE.md`.
- **Open:** D-10 – a second physical machine with a different CPU and .NET version (instructions in `docs/DETERMINISM.md`
  §4), due before Phase 26; with the 60 Hz manifests the Linux run and the determinism matrix are not repeated
  (`docs/DETERMINISM.md` §2c; Linux is informative since 9 October 2026, not an acceptance criterion).

### S2-M2 Performance of real matches — done; maximum and allocation missed

- **Delivered (6 October 2026, `16d337c`; Phase 19; gate at 60 Hz in Phase 23):** without a change of results – `Fixed`
  without 128-bit division, movement scans without allocation, pathfinding with one state byte per cell and remembered
  answers, vision recomputed only where circles changed, vision hash from the first changed place, prepared module frames
  in the object description, owner/kind/module lists in the world. 8 players on Twilight Flame 1.14 ms per tick instead of
  4.98 (30 Hz); at 60 Hz (9 October 2026, quiet, two runs) mean 683/694 µs, 99 % 1.6/1.8 ms, catch-up speed 26.7-fold, no
  gen-2 collections; `MD_GLA05` 2.1–2.4 ms. Tools: `SimTool perf --json`, `perf-compare`, `path-bench`,
  `PresentationTool sync-perf`, `scripts/perf-compare.ps1`. Documents: `docs/PERFORMANCE.md` §6a–§6c, `docs/PERF-*.md`.
- **Missed:** tick maximum 141–143 ms (target ≤ 8 ms; one tick with many simultaneous long path searches) and allocation
  8 KB per tick (target close to 0). The maximum goes down only with a behaviour change (D-3): shared group path, cell
  budget per tick or a different search – that is S3-M6 work with its own profile, or a fidelity question if the original's
  pathfinder behaves so (it does not distribute work by wall-clock time either).
- **Open:** D-3, D-4 (faster hash constructions: vision row by row, object hash with module hashes – change every
  reference value), the game layer per tick (`LiveGame.SyncAfterTick` around 1.6 ms under load, not measured quietly).

### S2-M3 Tick rate as a value of the world — done

- **Delivered (6/7 October 2026, `9299a2d` and `2a0a6f5`; Phases 20 and 21):** `SimulationRate` 30/60/120, time service
  `world.Time`, catalogues per rate, times from the data on the 30 Hz grid (`OriginalFrames(n)`), `TickOrder` for order
  instead of time, `SourceRules` against bare tick numbers (34 named exceptions); outside the simulation exactly one rate
  constant (`CnCNew.Data.OriginalFrame.PerSecond`), the presentation clock `GameClock`, effects, envelopes, EVA, HUD and
  mission presentation on the original-frame grid, `SaveGame` with the rate, `--tick-rate` everywhere. Result- and
  image-identical at 30 Hz (manifests, 54 screen hashes, HUD, UI renderer, captures, sound). Document:
  `docs/TICKRATE-MIGRATION.md` §1–§14.
- **Open:** none inside the milestone (the remaining list of the rate programme is under S2-M5).

### S2-M4 Fixed point Q39.24 — done

- **Delivered (7 October 2026, `8d74ef8`, the change itself `d7fca91`; Phase 22):** 24 instead of 16 fractional bits
  (resolution 6 · 10⁻⁸, range ±5.5 · 10¹¹); `FixedMath` derived for a bit count and checked against `BigInteger`;
  `FixedConvert` as the only conversion outside the simulation; source rules against scale literals and against squaring a
  range (`SquaredReach`); all formats with a new version (`WorldSnapshot` 10, replay 12, `CommandTypes.Version` 15,
  `MissionSnapshot` 3, `SaveGame` 3); the comparison of result quantities with 16 bits (5 of 16 AI runs with a different
  outcome through chaotic amplification of a rounding). Document: `docs/FIXED-POINT-Q39-24.md`.
- **Reported, not fixed:** commands carry unchecked target coordinates (beyond around 524,000 units the movement
  overflows; D-5, Stage 4); `DeliverPayloadAIUpdate.MinTurnRadius` returns 999,999 without a turn rate; `VisionMap` throws
  for circle centres more than 32,768 units outside the map; 52 weapons with `WeaponSpeed` 999,999 are not verified as
  covered.

### S2-M5 Rate invariance and 60 Hz as the default — done with named limits

- **Delivered (7 to 9 October 2026, `f3acc2e`, default `e8632bb`, manifests `ea52f87`; Phase 23):** the principle "the
  30 Hz step is the definition, higher rates subdivide it" with one frame convention (`docs/SIMULATION-CORE.md` §43:
  decision in the first tick of the object's own original frame, 30 Hz position with the last tick, observers read the
  frame end, state for this only above 30 Hz); the rate-equivalence suite (686 cases, 4,388 quantities in ten groups,
  tolerances fixed before the first run) from 479/625 to **0/0** quantities outside the tolerance at 60/120 Hz; the
  distribution comparison over 320 matches per rate (39 of 39 checks); the retail reference match is the same game at 30,
  60 and 120 Hz; all test programs at all three rates; the manifests at 60 Hz (reference match FactionGLA at tick 28,260,
  `CA05181C36C62C7A`; stress test `9EE9F730D124C3BD`; 14 of 16 AI runs decided). Documents: `docs/TICKRATE-MIGRATION.md`
  §15, `docs/SIMULATION-CORE.md` §39–§43, `docs/rate-invariance/*.md`.
- **Booked elsewhere:** three behaviour changes at 30 Hz (lost wake request `docs/DEFECT-LOST-WAKE.md`, factory exit, AI
  after the first viewing) → Stage 1 fidelity and S3-M1; the spectator mode → S3-M2; the original AI → S1-M13.
- **Named limits (`docs/TICKRATE-MIGRATION.md` §15.7.6):** 2 of 16 pairings end at 60 Hz in a different frame (same
  winner); 33 of 320 survey matches end more than one second away, 3 with a different outcome; 120 Hz not measured for
  pairings and survey; movers that still compute `value per tick × r` (Spectre, parachute, flares, particle beam, shot-down
  jet, gravity of the physics modules, group speed); healing slices per tick (`SlavedUpdate`, occupants); "first frame" for
  several modules; readers at the instantaneous position (script VM, special abilities); bridges, water and cliffs not
  investigated (the suite knows only the flat map); the road draw order depends on the rate (D-7); a 30 Hz savegame loads
  only with `--tick-rate 30`; **no human has seen or heard the game at 60 Hz** (→ S1-M11, R2).

### S2-M6 Test strategy in four tiers — done

- **Delivered (10 October 2026):** `docs/TESTING.md` and `scripts/test.ps1` – checks are chosen by what a change can
  break: tier 1 focus while working, tier 2 affected before every commit, tier 3 gate before a push of code, tier 4 full at
  its triggers (completion of a phase, a written manifest, a cross-cutting change, a build change); every report names the
  tier that ran. `pwsh scripts/audit-publish.ps1` before every push is independent of the tiers.
- **Open:** the step durations in `docs/TESTING.md` §2 are estimates until measured.

### S2-M7 Session core — current (Phase 24)

- **Brief:** `docs/handoffs/HANDOFF-PHASE24.md`. Starting state measured on 10 October 2026 at `8588905` on a fresh
  machine (tier 4 without scene audits, `--long`, campaign checks, performance profile).
- **Deliverables:** the state of the built-in `SkirmishAi` in the session state (canonical snapshot, source test "no field
  forgotten", ordered enumerations, AI hash per player, session hash = world hash plus AI hashes); the **skirmish savegame**
  (closes F-32); `MatchDescription` (immutable, canonically encodable: map and fingerprints, simulation version, rate,
  seed, options in game time, slots with faction/team/colour/start position/AI kind and strength); replay with the match
  description as header and the AI commands as an audit trail; the **two-stage command path** (command intent, local
  pre-check, receipt number, execution result as an event; `ICommandScheduler`); the **tick source** (`ITickDriver`:
  locally the accumulator, for the probe a driver that releases only ticks whose bundle is present; commands submitted
  immediately before the step of their tick); the lockstep probe with artificial delay (d = 0 identical to the starting
  state, d = 4 and 12 usable); missions on the same interfaces with their local implementations.
- **Acceptance:** at d = 0 all reference values unchanged; a running skirmish saved, loaded and continued hash-identically;
  the replay checks the AI against its recording; the same `MatchDescription` yields the same tick-0 hash in two processes.
- **Prerequisite (D-12):** the fidelity package A of Stage 1 is merged first, because it changes every hash that this phase
  records.
- **This milestone is the gate into Stage 4.**

### S2-M8 Engine follow-ups — open backlog

Not scheduled; each item is a phase of its own when the project owner picks it, or a side package when a phase needs it.

| Item | State | Decision |
|---|---|---|
| Tick maximum 141–143 ms at 60 Hz | needs a behaviour change (group path, cell budget, different search) | D-3; built as S3-M6 |
| Faster hash constructions (vision row by row; object hash with module hashes) | documented in `docs/PERF-VISION.md` §7.1, `docs/PERF-HASH.md` §6–§7; change every reference value | D-4 |
| Allocation 8 KB per tick (target close to 0) | 3.7 KB of it in the AI | open |
| The simulation on its own thread in the client | not in scope of Phase 19; what stands in the way is recorded: the presentation reads the running world through `SimulationQueries` and `FrameProjector` | open; a consequence of the two-stage command path (S2-M7) |
| Snapshot format for transfer (E17): JSON 3.2 MB with 155 objects, 9.0 MB with 2,249; a compact binary format if the Brotli-compressed JSON exceeds the limits | measured in Phase 18; decided in Phase 27 | S4-M3 |
| Rate-dependent leftovers (`docs/TICKRATE-MIGRATION.md` §15.7.6): movers `× r`, healing slices, first frame, readers; 120 Hz survey | named | open |
| Road draw order depends on the rate | named | D-7 |
| 120 Hz as a player option | 30 Hz is a test rate (decided) | D-22 |
| Documentation consolidation: historical layers and contradictions inside older documents (`SaveGame` 2 against 3, "eleven corrections" with twelve commit ids in `docs/SIMULATION-CORE.md` §39.7, the translators' lists) | named after the translation | open; part of R2 |
| `VisionMap` far circle centres, `MinTurnRadius` without turn rate, `WeaponSpeed` 999,999 (S2-M4) | reported | open |

### R2 Engine release — open

Passed when (PROGRAMME.md §6): a human has seen and heard the game at 60 Hz (checklist `docs/DEMO-FINDINGS.md`); the
reference manifest passes identically on a second physical machine with a different CPU (D-10); the skirmish savegame
works (S2-M7); the performance targets are reached or every rest is named with its cost centre; formats carry versions
and reject old states with a clear message; the documentation consolidation is done.

## 3. Rules of the stage

`docs/MULTIPLAYER-PLAN.md` §6 in full. In particular: measure first; the manifest is the yardstick; one cause per
reference change; determinism across machine boundaries (no culture-, runtime- or CPU-dependent value reaches the state);
no wall-clock time in the simulation; formats raise their version; verified headless is not accepted. From Phase 23:
above 30 Hz never read a position "one tick after" an event – read the frame end (`Entity.FrameEnd`); a manifest is checked
with the rate its file names; `--ticks` are ticks of the rate of the run, budgets are game time.

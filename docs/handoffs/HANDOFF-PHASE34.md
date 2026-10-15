> **Stage:** 3 – Changes · **Milestone:** S3-M6 Movement and pathfinding beyond the original · **Phase:** 34
> **Status:** current (starting state measured 15 October 2026 at `6c60147`; not begun)
> **Predecessor:** HANDOFF-PHASE33.md · **Results in:** docs/PERF-MOVEMENT.md, docs/PERFORMANCE.md, docs/SIMULATION-CORE.md, docs/plan/STAGE-3-CHANGES.md
> **Side items:** the fidelity debts the fix review of Phase 33 named (F-34, F-38, F-39), each before the community fix it blocks
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 34): a shared group path and a cell budget per tick as a movement profile, and the tick maximum under 8 ms with it

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md` (paragraphs "Phase 30", "Phase 32" and "Phase 33"),
`README.md`, `docs/plan/PROGRAMME.md` (§5 order, §7 decisions, above all D-3), `docs/plan/STAGE-3-CHANGES.md` (S3-M6), `docs/PERFORMANCE.md` §6c, `docs/PERF-MOVEMENT.md`,
`docs/SIMULATION-CORE.md` §44 (F-04 group moves) and §51, `docs/SKIRMISH-AI.md` and the final report of Phase 33. Prerequisite: Phase 33 (the AI's profiles; the rule
profile as the model for an option that changes the simulation).

## Goal in one sentence

Many units that search long paths at once no longer make one tick cost 140 ms: a group searches one path and its members follow it with offsets, and the searches
of a tick share a budget of counted cells with a deterministic carry-over - as a **movement profile** with its own fingerprint, so the original keeps the manifest and
the owner decides (D-3) whether it ever becomes the default.

## Starting point (measured, after Phase 33 – re-measure when this phase starts)

- **State:** `master` at `6c60147` plus the documents of the end of Phase 33; tier 4 with 54 of 54 steps ok at `6c60147` (6,469 s). Versions: `CommandTypes.Version` 17,
  replay 15, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description encoding **3**, skirmish session state **3**, protocol **6**, transport 2.
- **The reference manifest** (`80fb676`): retail reference match FactionGLA at tick 32,520, `6052051714309B17` / `84077720F511C1ED`; stress test `9EE9F730D124C3BD` /
  `F338463BE66D99C0`; 16 AI pairings **16 decided** (the rule "no standing still" of Phase 33); the four eight-player runs have a budget of five minutes and end
  without a winner.
- **The AI** (`docs/SKIRMISH-AI.md`): four tactics profiles and three strengths per slot; the default is balanced at medium. In the survey of Phase 33 the default
  decides 307 of 320 matches.
- **Performance** (`scripts/perf-profile.ps1` at `6c60147`, one machine, AMD Ryzen 3 PRO 4350GE): 8 players on the large map 890.5 µs per tick on average, 99 %
  2.26 ms, **maximum 165 ms** - the target of S3-M6 is at most 8 ms; the skirmish runs of the profile are other matches than before Phase 33 (the default AI
  changed), so compare the next profile against the binaries of `6c60147`, not of earlier commits.
- **The rule profile** has sixteen fixes (`GameFix` 1 to 16); every CRC-gated marker of GeneralsGameCode is decided. Three rejected fixes wait for fidelity fixes of
  the original (F-34, F-38, F-39 in `docs/plan/STAGE-1-PORT.md` section 3) - the side items of this brief.
- **Not verified, carried on:** no human has watched a profile, played on the patch layer or seen an adopted fix; a second physical machine and a real network
  (E1, D-10); the owner's open decisions D-2 to D-8, D-10, D-17 to D-20, D-23 to D-33.

## What is already done – do not build twice

- The deterministic A* with one state byte per cell, remembered answers and the abort on proven unreachability (Phase 19, `docs/PERF-MOVEMENT.md`), stationary
  blockers and avoidance (Phases 10, 15), formations (Phase 14), `GroupMoveCommand` with an own destination per unit (Phase 31, F-04), `SimTool path-bench`, `perf`,
  `perf-compare`, `scripts/perf-profile.ps1`.
- The rule profile (`--rules`, `RuleProfile`, folded into the definition fingerprint) and the AI's profiles in the match description (Phase 33).

## Decisions that are fixed

- The original movement stays the default and keeps the reference manifest; the new movement is an option with a fingerprint of its own (D-3 open: the owner decides
  whether it becomes the default).
- A budget is counted work (cells expanded), never wall-clock time.

## Brief (in this order)

1. **Reproduce the starting state** (tier 4 unless Phase 33 ended with it on the same commit; `scripts/perf-profile.ps1` as the yardstick of the maximum).
2. **Where the maximum comes from**, measured: which ticks, how many searches, how many cells (`SimTool perf --json`, `path-bench`); thresholds for this phase fixed
   and committed before the first change (at least: tick maximum at 60 Hz with the new profile at most 8 ms in the performance gate; mean not worse than 5 %).
3. **Shared group path**: one search for a group order, members follow with offsets; the original's per-unit search stays the original profile.
4. **Cell budget per tick** with deterministic carry-over; a unit whose search waits keeps its last order visibly (no silent drop).
5. **The movement profile**: an option in the match description and the catalog's fingerprint like the rule profile; tests under both profiles at 30, 60 and 120 Hz;
   the manifest's runs unchanged under the original.
6. **Measure**: the performance gate under both profiles, `path-bench`, the rate-equivalence suite under the new profile, the AI survey (`ai-survey`, `ai-profiles`)
   under both - distribution of outcomes reported, not judged.
7. **Side items** as time allows: F-34 (`HealContain` exit through the door), F-38 (enter only when the container is down), F-39 (rider and bike exchange levels) -
   each a fidelity fix of the original profile with its own manifest commit, after which the blocked community fix of `docs/GGC-FIXES.md` 3.1c can be decided.
8. **Documents** and the next brief.

## Bottlenecks and showstoppers of this phase

- Performance numbers only on a quiet machine, as a direct switch between two binary directories in the same period (`scripts/perf-profile.ps1`).
- All processes of a measurement from the same state of the tree - Phase 33 lost a measurement to a copy that was rebuilt while it ran.

## Out of scope

Making the new movement the default (D-3), a second machine, every judgement of a human.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`; the original profile keeps the reference manifest; every new option an option with a fingerprint and a test at 30, 60 and 120 Hz.

## Acceptance

The tick maximum under 8 ms in the 60 Hz performance gate with the new profile (or reported as missed against the threshold committed beforehand); the original
profile's manifest unchanged; tier 4 green. What only a human could judge - that a group "marches well" when watched - is named as such and replaced by measured
quantities (spread of arrival, path length against the per-unit search).

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run; continue the "Starting point" of the next brief, the stage document, the registers and the index.

> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 65
> **Status:** done by machine (9 to 10 November 2026; `docs/SIMULATION-CORE.md` §89; written 9 November 2026 at the end of Phase 64)
> **Predecessor:** HANDOFF-PHASE64.md · **Results in:** `docs/SIMULATION-CORE.md` §89, fidelity register `docs/plan/STAGE-1-PORT.md` §3
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 65): what Phase 64 left, and whatever the owner's next playtest finds.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 63" and "Phase 64"), `docs/SIMULATION-CORE.md` §87 and §88, and the register entries F-168 and F-169.

## Goal in one sentence

The last registered rate differences are traced, the AI stops buying carriers it cannot use, and the findings of the owner's next playtest are fixed.

## Starting point (measured)

Phase 64 closed on 9 November 2026 (§88): the owner's 15 findings of 8 November, the AI's sale for a carrier, F-140's and F-137's rest, four rate causes, the product survey. The retail reference match is a stalemate at tick 108,000 with 1,501,925 commands (the queue limit of nine makes the driver build wider); 13 of the 16 pairings are decided (balance not judged); 15 of the 16 are the same game at 30 and 60 Hz to their ends.

## What is already done - do not build twice

Everything of §86 to §88.

## Decisions that are fixed

One product (D-3, D-4, D-32, D-33). The original's way is the yardstick; a change beyond it is a rule fix with a test under both profiles (fix 27 is the example). The 30 Hz step is the definition, higher rates subdivide it (§43).

## Brief (in this order)

| # | Work | Port | Original |
|---|---|---|---|
| 1 | F-168: Armored Fury GLA against America parts at frame 49,697 - a Tank Avenger finds two standing blockers at 30 Hz and none at 60 Hz (`StationaryBlockersAhead`, candidate `StandsStillAtFrameEnd`); F-169: the Patriot's first assisting shot a frame later at 60 Hz and the second-AI-module dozer's drift of 0.0167 units a frame; then the 16 pairings again at 30, 60 and 120 Hz | `SimulationWorld.Movement*.cs`, `CleanupAndSpyModules.cs` | |
| 2 | The AI spends its last money on carriers whose supply sources are out of reach or unsafe (`docs/SKIRMISH-AI.md` 7d, Bitter Winter ChinaTank against AirForce seed 3): a carrier is bought only when a stocked warehouse is in reach of a centre | `SkirmishAi.cs` | the port's own AI |
| 3 | What the owner's next playtest finds (`docs/ACCEPTANCE-FINDINGS.md`) | | |
| 4 | The rest named in §88: the minimap's fading triangle and the radar-priority test of `defect` (F-137), a script attack without a shot limit and the button's `MaxShotsToFire` (F-140), Esc and key-up bindings in the hotkey editor | | |
| 5 | The runs Phase 64 did not make: `scripts/perf-profile.ps1` against `3f19aa96`, the clean room with a real download, the campaign checks (reachability, audit, drive), the AI survey on the final state | | |

## Thresholds (to be committed before the first measurement)

Item 1: every pairing either the same at 30 and 60 Hz to its end or its first difference registered with its cause. Item 2: `SimTool econ-survey` over 8 seeds of every pairing: no player with a finished supply centre and no carrier for 90 game seconds, balance not judged.

## Bottlenecks and showstoppers

A manifest measurement is an hour of both cores: measure once per phase after the last change of the simulation and say so. Remove worktrees only through `C:/wf45bin/pt/wtremove.ps1`. Tell agents to use the junction, never to copy `game/`. The owner allows commits and pushes in between, so he can test sooner: push a finished, tested piece, do not wait for the whole phase.

## Out of scope

The second physical machine and a real network (E1, D-10); whatever only a person can judge.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`. No game data in the repository. Do not touch the Steam installation.

## Acceptance

Tier 4 at the final state; every item built or named with its reason.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; the registers of `docs/plan/` brought up to date; the next brief written.

## Result (10 November 2026)

Done by machine, on one machine; no human has seen or heard any of it. The owner's playtest of build `92e16682` (item 3) arrived the same day and took the phase: 18 findings, the public launcher and the release pipeline (§89). Items 1 (F-168, F-169), 2 (the AI's last money on unusable carriers), 4 (the minimap's triangle, `defect`, the script attack without a shot limit, Esc and key-up bindings) and 5 (`scripts/perf-profile.ps1`, the clean room with a real download, the campaign checks, the survey on the final state) were not begun and move to Phase 66.

**Closing run.** Tier 4 on the pushed state `37f05ee6`: 60 steps ok before `game-30` (the first run, 6,000 s), then three fixes of test expectations, none of the game: the modern mission HUD pictures at 30 and 120 Hz (the bar's opacity moves them at every rate; the interface agent had rewritten one rate), the Spectre test's second place 110 instead of 48 units from the first (at 120 Hz no frame was seen between the two), the laser test picking its several-beam laser by name (it took `EXLaser4`, whose outer width is smaller than its inner, from the dictionary's order); then `game-30`, `game-120`, `sim-long` and `scenes` ok (6,249 s and 4,199 s). Not run: `scripts/perf-profile.ps1`, the clean room, the campaign checks, a real release.

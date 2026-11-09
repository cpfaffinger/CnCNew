> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 64
> **Status:** done by machine (8 to 9 November 2026; `docs/SIMULATION-CORE.md` §88; written 8 November 2026 at the end of Phase 63)
> **Predecessor:** HANDOFF-PHASE63.md · **Results in:** `docs/SIMULATION-CORE.md` §88, fidelity register `docs/plan/STAGE-1-PORT.md` §3
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 64): the rest of Phase 63's list, and a look at the whole product.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 62" and "Phase 63"), `docs/SIMULATION-CORE.md` §86 and §87, and the register entries F-164 and F-160.

## Goal in one sentence

The last registered rate difference is traced, the built-in AI recovers from a total loss of its carriers, and the beams and the Strategy Center are finished.

## Starting point (measured)

Phase 63 closed on 8 November 2026 (§87): the approach of a unit inside its minimum range, the portable structures, the toppling trees, the turrets, the falling bomb and the Chinook at 30 and 60 Hz, the Patriot beams, the doors and the hidden parts. The retail reference match is a stalemate at tick 108,000 with 311,588 commands; eight of the 16 pairings are decided (balance not judged); ten of the 16 were the same game at 30 and 60 Hz at the end of Phase 62 and the six that were not have their causes fixed (§87.2), their comparison on the final state was not repeated in full.

## What is already done - do not build twice

Everything of §86 and §87.

## Decisions that are fixed

One product (D-3, D-4, D-32, D-33). The original's way is the yardstick. The 30 Hz step is the definition, higher rates subdivide it (§43).

## Brief (in this order)

| # | Work | Port | Original |
|---|---|---|---|
| 1 | The 16 pairings at 30 and 60 Hz in full on the final state (`SimTool rate-compare --map-diff ... --frames 50000`, the options of Phase 45 and `boredSince`): each either the same to its end or its first difference registered; F-164 (Alpine Assault China against GLA at frame 18,823: a Red Guard asks the movement-budget path queue a frame later at 60 Hz - find why `moveHoldUntil` is set there) | `SimulationWorld.PathQueue.cs` | |
| 2 | The AI's rest of the economy (`docs/SKIRMISH-AI.md` 7d): a side without carriers and without the price of one stays down because the AI never sells a building; depleted centres still count toward the cap of four | `SkirmishAi.cs` | the port's own AI |
| 3 | F-140's rest: the paths that fire outside the attack machine (a script's one-off command button, an order to an object without an AI, `AssistedTargetingUpdate`, `CleanupHazardUpdate`); F-137's sounds and the minimap blip; the Patriot beams' muzzle and target particle systems of other laser objects | | |
| 4 | The check scripts `scripts/check-minimap.ps1` and `scripts/check-ingame-menu.ps1` as steps of `scripts/test.ps1` | `scripts/test.ps1` | |
| 5 | A survey of the whole product as a player would use it, by machine: every shell screen of both skins at 800 x 600, 1280 x 720 and 1920 x 1080 and with the 1.5 text size, the in-game menu, the structure groups and the AI options on a real skirmish by `ShellScript`, a full skirmish against each AI profile at the speeds 1x and 3x; report what a program finds broken | `ShellScript`, `scripts/shell` | |

## Thresholds (to be committed before the first measurement)

Item 1: every pairing either the same at 30 and 60 Hz to its end or its first difference registered with its cause. Item 2: no player with a finished supply centre and no carrier for 90 game seconds in 8 seeds of every pairing of the survey (`SimTool econ-survey`), balance not judged.

## Bottlenecks and showstoppers

A manifest measurement is an hour of both cores: measure once per phase after the last change of the simulation and say so. Remove worktrees only through `C:/wf45bin/pt/wtremove.ps1` (it deletes the junctions to `game` and `references` first). Tell agents to use the junction, never to copy `game/` (5 GB).

## Out of scope

The second physical machine and a real network (E1, D-10); whatever only a person can judge.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`. No game data in the repository. Do not touch the Steam installation.

## Acceptance

Tier 4 at the final state; every item built or named with its reason.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; the registers of `docs/plan/` brought up to date; the next brief written.

## Result (9 November 2026)

Done by machine, on one machine; no human has seen or heard any of it. Per item of the brief:

1. **The 16 pairings:** compared on the final state; F-164 was not the path queue (riders held a frame), F-165 to F-167 found and fixed; 15 of 16 are the same game at 30 and 60 Hz to their ends, Armored Fury GLA against America parts at frame 49,697 (F-168) (§88.4).
2. **The AI's economy:** built (§88.2); the threshold (no player with a centre and no carrier for 90 s) is met in 255 of 256 cases, the one left has nothing to sell.
3. **F-140's rest, F-137's sounds, the lasers' particles:** built (§88.3).
4. **The check scripts:** steps of `scripts/test.ps1` (§88.5).
5. **The product survey:** `scripts/check-product-survey.ps1` and `ProductSurveyTests` (§88.5); it found the map picker, the hint, the 150 % text and the list at the screen's edge, all fixed.

Added on the way: the owner's playtest of 8 November (15 findings, §88.1, `docs/ACCEPTANCE-FINDINGS.md`).

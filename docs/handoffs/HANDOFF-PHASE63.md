> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 63
> **Status:** current (written 7 November 2026 at the end of Phase 62)
> **Predecessor:** HANDOFF-PHASE62.md · **Results in:** `docs/SIMULATION-CORE.md` §87, fidelity register `docs/plan/STAGE-1-PORT.md` §3
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 63): the attack's approach for every ground unit, the open rate differences, and what the owner's playtest of 7 November left.

You work autonomously on this repository. First read `CLAUDE.md` (paragraph "Phase 62"), `docs/SIMULATION-CORE.md` §86, the register entries F-143, F-149 to F-159 and the owner's findings of 7 November in `docs/ACCEPTANCE-FINDINGS.md`.

## Goal in one sentence

Units of two players that stand on one point no longer loop in their approach (F-143), the open rate differences are traced, and the Patriot batteries show their data stream.

## Starting point (measured)

Phase 62 closed on 7 November 2026 (§86): the original's fire cycle, five rate corrections (ten of the sixteen pairings the same game at 30 and 60 Hz), 23 playtest findings. The retail reference match is a stalemate at tick 108,000 (`085358A5CDC18557` / `6C30ECE5558348E3`) with 884,368 commands (306,705 before F-140): about 270 tunnel defenders stand on an enemy's point, their minimum-range weapon finds the victim out of range, the approach goal is their own place and the move begins again in every frame without a shot (11.5 million arrivals; F-143). Nine of the 16 pairings are decided (balance not judged).

## What is already done - do not build twice

Everything of §86. A first step of item 1 was tried in a worktree (not committed anywhere you can use): `FindAttackPosition` (`SimulationWorld.AttackView.cs`) searches for a mobile ground unit whose current weapon is not a contact weapon and either has a minimum range or needs a line of sight - with the current weapon (`CurrentWeapon`) when no weapon is in range, because `ChooseWeapon` answers no slot out of range - and a test of a computer player's mortar truck on its victim's point (before: 297 arrivals and no shot; after: it fires from within its range at 30, 60 and 120 Hz). The general form (every ground unit) made the real-data run of `Simulation.Tests` much slower (a 2,500-cell search per chase refresh) and was narrowed to the two cases. Redo it with a test first; it changes AI outcomes, so it needs its own manifest.

## Decisions that are fixed

One product (D-3, D-4, D-32, D-33). The original's way is the yardstick. The 30 Hz step is the definition, higher rates subdivide it (§43).

## Brief (in this order)

| # | Work | Port | Original |
|---|---|---|---|
| 1 | F-143: the approach of an attacker with a minimum range goes where `Pathfinder::findAttackPath` takes it (the start cell and every cell within half a cell never accepted; when nothing in range is found and the goal is under three cells away the unit is "unstacked" with `adjustDestination`); the repath rules of `computePath` (`MIN_RECOMPUTE_TIME`, the victim in the same place) | `SimulationWorld.AttackView.cs`, `SimulationWorld.Acquisition.cs` `Engage`, `SimulationWorld.Guard.cs` | `AIAttackApproachTargetState::computePath`, `AIUpdateInterface::doPathfind`, `Pathfinder::findAttackPath` |
| 2 | The open rate differences, each traced with `rate-compare --fields --trace` and fixed with a test or kept registered with its cause: F-149 (the hulk's height after the train), F-150 (an Avenger's laser turret on its tank), F-151 (two Red Guards), F-152 (a tree's slow-death midpoint draw), F-153 (a Gatling cannon's turret angle), F-154 (a Gattling tank's hulk and debris), F-156 (the Daisy Cutter's gas falls for 29, 33 and 35 frames) | | |
| 3 | F-158: the Patriot batteries' feedback lasers (`LaserFromAssisted`, `LaserToTarget`): a simulation event with the end points when `AssistedTargetingUpdate.AssistAttack` runs (it moves the hashes of matches with Patriots - its own manifest), and a beam in the object layer with the data of the laser object template (`PatriotBinaryDataStream`) | `CleanupAndSpyModules.cs`, `SimEvent.cs`, the object layer | `AssistedTargetingUpdate::makeFeedbackLaser`, `LaserUpdate` |
| 4 | F-157: the Strategy Center's doors play at the plan's 7 s (the original stretches the animation: `setAnimationLoopDuration`); F-159: a hidden sub-object hides what hangs below its bone (`doHideShowBoneSubObjs`) | `StructureConditions.cs`, `SubObjectOverrides` | `BattlePlanUpdate::setStatus`, `W3DModelDraw` |
| 5 | The AI economy's rest (`docs/SKIRMISH-AI.md` 7d): a side without carriers and without the price of one stays down because the AI never sells a building; depleted centres still count toward the cap of four | `SkirmishAi.cs` | the port's own AI |
| 6 | F-140's rest: the paths that fire outside the attack machine (a script's one-off command button, an order to an object without an AI, `AssistedTargetingUpdate`, `CleanupHazardUpdate`); F-137's sounds and the minimap blip | | |
| 7 | The check scripts `scripts/check-minimap.ps1` and `scripts/check-ingame-menu.ps1` as steps of `scripts/test.ps1` | | |

## Thresholds (to be committed before the first measurement)

Item 1: the test's frames exactly at 30, 60 and 120 Hz; the reference match's arrivals of tunnel defenders and its command count reported before and after; the eight-player run's mean and 99 % tick within 5 % of before. Item 2: every pairing either the same at 30 and 60 Hz to its end or its first difference registered with its cause.

## Bottlenecks and showstoppers

Item 1 changes every chase of every computer player: measure it from its own commit in a worktree, run the real-data tests and `Game.Tests` before the manifests, and bisect a moved outcome before judging it. A new event kind draws an event id from the world's counter, which is state. **Remove worktrees only through `C:/wf45bin/pt/wtremove.ps1`** (or delete their junctions to `game` and `references` first): `git worktree remove` empties `game/` through a junction - twice so far.

## Out of scope

The second physical machine and a real network (E1, D-10); whatever only a person can judge.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`. No game data in the repository. Do not touch the Steam installation.

## Acceptance

Tier 4 at the final state; every item built or named with its reason.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; the registers of `docs/plan/` brought up to date; the next brief written.

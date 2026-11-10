> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 66
> **Status:** current (written 10 November 2026 at the end of Phase 65)
> **Predecessor:** HANDOFF-PHASE65.md · **Results in:** `docs/SIMULATION-CORE.md` §90, fidelity register `docs/plan/STAGE-1-PORT.md` §3
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 66): what Phases 64 and 65 left, the first real release, and whatever the owner's next playtest finds.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 64" and "Phase 65"), `docs/SIMULATION-CORE.md` §88 and §89, `docs/RELEASING.md`, and the register entries F-168, F-169, F-170, F-175.

## Goal in one sentence

The registered rate differences and the unported flight-contact shapes are traced, the first release is built and the launcher is proven against a real one, and the owner's next findings are fixed.

## Starting point (measured)

Phase 65 closed on 10 November 2026 (§89): 18 findings of the playtest of build `92e16682`, the public launcher, the release pipeline and the README. The measured state is in §89.4.

## What is already done - do not build twice

Everything of §86 to §89.

## Decisions that are fixed

One product (D-3, D-4, D-32, D-33). The original's way is the yardstick; a change beyond it is a rule fix with a test under both profiles (fixes 27, 28 and 29 are the examples). The 30 Hz step is the definition, higher rates subdivide it (§43). A release is made only when the owner says so; the lead then runs `scripts/release.ps1 -Version X.Y.Z` after audit-publish and tier 4 and pushes the tag.

## Brief (in this order)

| # | Work | Port | Original |
|---|---|---|---|
| 1 | The owner's next playtest findings (`docs/ACCEPTANCE-FINDINGS.md`) | | |
| 2 | F-168 (Armored Fury GLA against America, frame 49,697, a Tank Avenger's blockers ahead), F-169 (the Patriot's first assisting shot a frame later at 60 Hz; a dozer with a second AI module drifts 0.0167 units a frame), F-170 (`AimBodyAt`'s has-a-move-target shortcut), F-175 (the shapes of `PartitionData::collidesWith` for flight contacts); then the 16 pairings again at 30, 60 and 120 Hz | `SimulationWorld.Movement*.cs`, `SimulationWorld.Turrets.cs`, `SimulationWorld.FlightContacts.cs` | `PartitionData::collidesWith` |
| 3 | The first real release when the owner says so: the pipelines have never run; proof of the launcher against a real GitHub release (download, hash, patcher against the real Steam copy, update banner), the asset names, SmartScreen (unsigned), a simulation check in the client package for the launcher's optional step | `tools/CnCNew.Launcher*`, `scripts/release-build.ps1` | |
| 4 | The AI's last money on carriers without a safe source in reach; the minimap's fading triangle and the radar-priority test of `defect` (F-137); a script attack without a shot limit and the button's `MaxShotsToFire` (F-140); Esc and key-up bindings in the hotkey editor; the gunship's voice line | | |
| 5 | The runs not made since Phase 63: `scripts/perf-profile.ps1` against `3f19aa96`, the clean room with a real download, the campaign checks (reachability, audit, drive), the AI survey on the final state | | |

## Thresholds (to be committed before the first measurement)

Item 2: every pairing either the same at 30 and 60 Hz to its end or its first difference registered with its cause.

## Bottlenecks and showstoppers

A manifest measurement is an hour of both cores: measure once per phase after the last change of the simulation and say so. Remove worktrees only through `C:/wf45bin/pt/wtremove.ps1`. Tell agents to use the junction, never to copy `game/`. The owner allows commits and pushes in between, so he can test sooner: push a finished, tested piece, do not wait for the whole phase. The README's four screenshots are the only capture of the game in the repository (the owner's decision of 10 November 2026): regenerate them with `scripts/capture-readme-screenshots.ps1` when the interface changes, nothing else of the game is committed.

## Out of scope

The second physical machine and a real network (E1, D-10); whatever only a person can judge.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`. No game data in the repository. Do not touch the Steam installation.

## Acceptance

Tier 4 at the final state; every item built or named with its reason.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; the registers of `docs/plan/` brought up to date; the next brief written.

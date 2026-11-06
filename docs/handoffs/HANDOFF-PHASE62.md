> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 62
> **Status:** current (written 6 November 2026 at the end of Phase 61)
> **Predecessor:** HANDOFF-PHASE61.md · **Results in:** `docs/SIMULATION-CORE.md` §86, fidelity register `docs/plan/STAGE-1-PORT.md` §3
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 62): the original's fire cycle, and what Phase 61 left.

You work autonomously on this repository. First read `CLAUDE.md` (paragraph "Phase 61"), `docs/SIMULATION-CORE.md` §85, and the register entries F-139 to F-142 and F-40.

## Goal in one sentence

A unit fires on the frames the original's attack machine lets it fire (F-140), and the rate differences left on the final state of Phase 61 are traced.

## Starting point (measured)

Phase 61 closed on 6 November 2026 (§85): five shares of a frame above 30 Hz (F-138), aircraft goal cells (F-134), the train on the world's grid (F-133), the map's objects before the players', the train's whistle and clatter, F-40's premise corrected, the script statuses "disabled" and "unpowered" and the panel flags (F-141), a mission's map object properties (F-142). The retail reference match is a stalemate at tick 108,000 (`EAC390B0ADFB82F2` / `D4559EB954488F2F`); stress test `20359BA8A2093B99` / `2967E6B3B1BCFDDD`; 11 of the 16 pairings are decided.

## What is already done - do not build twice

Everything of §85. A port of F-140 was begun in a worktree beside Phase 61 (branch `w62a`, local, not pushed); if it exists, review it against the brief below before measuring it.

## Decisions that are fixed

One product (D-3, D-4, D-32, D-33). The original's way is the yardstick (Stage 1). The 30 Hz step is the definition, higher rates subdivide it (`docs/SIMULATION-CORE.md` §43).

## Brief (in this order)

| # | Work | Port | Original |
|---|---|---|---|
| 1 | F-140: the attack machine alternates AIM_AT_TARGET and FIRE_WEAPON one state per frame; a shot only in FIRE's update; the distance between shots the smallest even number not below the delay (and the reload), a contact weapon in range every frame, a wind-up one frame shorter; the same for a turret's own machine. A toggle per attack machine and per turret, stepped in the unit's decision tick; ordered attacks and the attack on a place obey it; the paths the original fires outside the machine keep their timing. Tests with the frames of the worked examples (D = 9, 10, 0; the Ranger's rifle 0, 4, 8, 30; the Overlord's gun 0, 10, 70, 80, 140; a contact weapon; a wind-up) at 30, 60 and 120 Hz; its own manifest; the survey (§83.8) before and after | `SimulationWorld.Acquisition.cs`, `.WeaponSlots.cs`, `.Turrets.cs`, `.FiringTracker.cs`, `FireWeaponCommand` | `AttackStateMachine`, `AIAttackFireWeaponState`, `AIAttackAimAtTargetState`, `TurretStateMachine`, `StateMachine::internalSetState` |
| 2 | The rate differences of the final state: the 16 pairings at 30 and 60 Hz with the options of Phase 45 and `--ignore '…|boredSince'`; each first lasting difference traced (`--trace`), fixed with a test or registered. Seen on the state before the start order: an Avenger's laser turret riding its tank turns back a frame early at 60 Hz, a GLA worker's place | | |
| 3 | F-40's rest: fire orders carry no forced flag, so a forced attack on one's own stealthed unit and on a disguised truck is refused; `IsClearingVictim` ignores stealth for a whole mine-clearing attack where the original clears `IGNORING_STEALTH` after the first shot | `FireWeaponCommand`, `IsHiddenFrom`, `IsClearingVictim` | `WeaponSet::getAbleToAttackSpecificObject`, `AIAttackState::onEnter` |
| 4 | F-141's rest: no animation pauses for a disable (`Drawable::getShouldAnimate`, `AnimationsRequirePower`), and the map properties `objectRecruitableAI` and `objectSelectable` at placement (new `MapObjectFlags` bits) | `CnCNew.Client.Stride` object layer, `MapObjectPlacement` | `W3DModelDraw`, `Object::updateObjValuesFromMapProperties` |
| 5 | F-137: the radar events of scripts (`RADAR_CREATE_EVENT`), stealth found or lost, infiltration and the battle plan for the space bar | `HudMessageFeed`, the VM | `Radar::createEvent`, `tryEvent`, `tryInfiltrationEvent` |
| 6 | The train's shove lets a passenger who boards a waiting train through, as the whistle already does | `RailroadModules.cs` `Collide` | `RailroadBehavior::onCollide` |

## Thresholds (to be committed before the first measurement)

Item 1: every worked example's frames exactly at 30, 60 and 120 Hz; the survey's decided matches reported before and after (balance not judged). Item 2: every one of the 16 pairings either the same at 30 and 60 Hz to its end or its first difference registered with its cause.

## Bottlenecks and showstoppers

F-140 moves every fight of every run of the manifest; measure it from its own commit in a worktree, and run the real-data tests and `Game.Tests` before the manifests. A new event kind draws an event id from the world's counter, which is state: a commit that only adds an event still moves hashes (Phase 61, the whistle).

## Out of scope

The second physical machine and a real network (E1, D-10); whatever only a person can judge.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`. No game data in the repository. Do not touch the Steam installation.

## Acceptance

Tier 4 at the final state; every item built or named with its reason.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; the registers of `docs/plan/` brought up to date; the next brief written.

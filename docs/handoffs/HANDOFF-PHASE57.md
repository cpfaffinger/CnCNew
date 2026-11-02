> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 57
> **Status:** current (begun 2 November 2026)
> **Predecessor:** HANDOFF-PHASE56.md · **Results in:** `docs/SIMULATION-CORE.md`, `docs/GAME-HUD.md`, `docs/GAME-AUDIO.md`, fidelity register `docs/plan/STAGE-1-PORT.md` §3
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 57): what the data still names and nobody reads, what the original shows and the port does not, while the owner's next acceptance waits.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "The owner's first report" and "Phase 56"), `docs/SIMULATION-CORE.md` §78 and the register entry F-111.

## Goal in one sentence

Every key the data of the game gives that changes play is read, and every small thing the original draws or plays that a person would miss is there or named with its reason.

## Starting point (measured)

Phase 56 closed on 2 November 2026; its numbers are in `CLAUDE.md`. Tier 4 57 of 58 steps at `21cde75b`, the one that failed (`Import.Tests`, a fixture from before F-33) fixed in `71ae8b24` and green alone, also against the real game. 26 of 120 goals stay open for a person; the owner postponed that run.

## What is already done - do not build twice

The chain of a shot (`SimulationWorld.Launch.cs`), the beams (`AttachedEffects`), the sounds of a state (`docs/GAME-AUDIO.md`), the bar of the special powers (`docs/GAME-HUD.md`), the places of a transport.

## Decisions that are fixed

One product; the built-in AI is the default. A change of the reference values is allowed for a cause of the original, one manifest commit per phase at the least, and a manifest deviation is a finding until it is explained.

## Brief (in this order)

Read in the source before writing any of this (2 November 2026):

1. **Rubble** (`ActiveBody::setCorrectDamageState`, `PathfindCell::setTypeAsObstacle`). A structure whose body reaches `BODY_RUBBLE` and stays (`KeepObjectDie`: the civilian buildings) takes `StructureRubbleHeight` or `DefaultStructureRubbleHeight` as its geometry height, is `NO_COLLISIONS` from then on, and its cells become `CELL_RUBBLE`: passable for a locomotor with the surface `RUBBLE` (28 of 111 locomotors, the infantry and more), impassable for the others. In the port the wreck of a building stands as tall as the building, and blocks as a building does.
2. **The airborne target** (`AIUpdateInterface::update`: after its movement an object is `AIRBORNE_TARGET` while it is higher above the terrain than its locomotor's `AirborneTargetingHeight`). The port asks `IsSignificantlyAboveTerrain`, a stand-in. Forty-three locomotors name 20 or 30.
3. **Leaving a transport in the air** (`TransportContain::onRemoving`, `isExitBusy`): `ExitBone`, `OrientLikeContainerOnExit`, `KeepContainerVelocityOnExit`, `ExitPitchRate`, `DelayExitInAir`.
4. **`TurnPivotOffset`** (`Locomotor::rotateObjAroundLocoPivot`): a vehicle turns around a point behind its centre (-1 rear, 0 centre, 1 front), not while it brakes. It is what makes a tank's rear swing.
5. **`GroupMovementPriority`** and the columns of a group move (`AIGroup` `computeIndividualDestination`, the sort by `LocomotorPriority`): front, middle and back.
6. **What is heard:** `FireSoundLoopTime` (`FiringTracker::shotFired`: one looping sound for a stream of shots), the music of the score screen and of the load screen, the voices that depend on the target (`VoiceAttackAir` and its neighbours).
7. **What is seen:** `LoadScreenImage`, `drawBombed`, hover pips, the level-gain and healed animations in the world, the ready flash of the bar of the special powers, the emoticons.
8. **Objects that stand with nothing inside:** `FlightDeckBehavior` (a shell), `GarrisonContain` `InitialRoster`.

Each item with a test that fails on the code before, at 30, 60 and 120 Hz where it touches the simulation. A key that turns out to change nothing a player can see is named with its reason.

## Bottlenecks and showstoppers

Item 1 changes what the pathfinder sees during a match: order of the footprint updates is state, and the path cache must not outlive it. Items 1, 4 and 5 will move the manifests.

## Out of scope

The second physical machine and a real network (E1, D-10); whatever only a person can judge.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`. No game data in the repository. Do not touch the Steam installation.

## Acceptance

Tier 4 at the final state; the register entry F-111 closed or split; every item built, or named with its reason.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; the registers of `docs/plan/` brought up to date.

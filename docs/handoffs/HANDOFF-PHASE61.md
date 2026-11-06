> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 61
> **Status:** current (written 4 November 2026 at the end of Phase 60)
> **Predecessor:** HANDOFF-PHASE60.md · **Results in:** `docs/SIMULATION-CORE.md` §84, fidelity register `docs/plan/STAGE-1-PORT.md` §3
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 61): the train at every rate, and what Phase 60 named.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "The owner's playtest of 3 November 2026" and "Phase 60"), `docs/SIMULATION-CORE.md` §82 and §83, and the register entries F-129 to F-133.

## Goal in one sentence

The train of a map is the same game at 30, 60 and 120 Hz, so that the pairings on Alpine Assault can be compared across rates again, and the rest Phase 60 named is built or decided.

## Starting point (measured)

Phase 60 closed on 4 November 2026: a skirmish of the built-in AI places every object of its map (§83); 13 of the 16 pairings of the reference manifest are decided; the reference match is a stalemate at tick 108,000 (`6A869CB230C7AF60` / `225863E817955EEE`); `CommandTypes.Version` 27, replay 26. **F-133:** `SimTool rate-compare … --map-diff FactionAmerica,FactionChina --map "Maps/Alpine Assault/Alpine Assault.map" --fields --frames 50000 --persist 10 --ignore '\.nextWake$|\.previousHealth$|constructionProgress|DynamicShroudClearingRange'` parts at frame 1: the locomotive `TrainEngineAlpine` has the conductor's speed 0.2093 at 30 Hz and 0.2362 at 60 Hz (`RailroadBehavior.RunConductor`, `_framePhase`, `time.FrameSlice`), and its place, angle and hitch follow. One extra acceleration step does not explain the numbers. Before Phase 60 the train existed only in a match of the original AI, whose rate comparison left it out (`docs/SKIRMISH-AI-ORIGINAL.md` §6.4).

## What is already done - do not build twice

Every object of a skirmish map (§83.1, §83.2), a wreck that is rubble before it stands (§83.3), the sticky bombs of a rebuild hole (§83.4), the train's sounds at a join and at a station (§81.7).

## Decisions that are fixed

One product (D-3, D-4, D-32, D-33). The original's way is the yardstick (Stage 1). The 30 Hz step is the definition, higher rates subdivide it (`docs/SIMULATION-CORE.md` §43).

## Brief (in this order)

| # | Work | Port | Original |
|---|---|---|---|
| 1 | Find why the train's speed parts in the first frame (where the locomotive's frame begins after its placement at tick 0, the first tick of `RunConductor`, `FrameSlice`), fix it with a test that fails before at 60 and 120 Hz, then compare the four Alpine Assault pairings at 30, 60 and 120 Hz to their ends | `RailroadModules.cs`, `SimulationWorld` (spawn and the object's frame) | `RailroadBehavior::update` |
| 2 | The other 12 pairings of the manifest at 30 and 60 Hz with the map's objects (§63.5 had all 16 agree before the map's objects stood) | | |
| 3 | The train's whistle on a collision (`onCollide`, "Alert all the players of recent disaster"): an event of the simulation and a test of the audio director whether the whistle still plays; the clatter's volume by speed (`setVolume(speed / 10)`) | `RailroadModules.cs`, `SoundCues.States.cs`, `AudioDirector` | `RailroadGuideAIUpdate.cpp` |
| 4 | F-40's rest (an attack ends while firing, the original's at the next approach), its own manifest | | `AIAttackApproachTargetState::updateInternal` |
| 5 | The order of the start: the map's objects before the players' (§83.1), measured; `objectPowered` read where the original reads it (a builder's and a factory's buttons, a drawable's lights) | `SkirmishMatchSession.Start` | `GameLogic::startNewGame`, `Object::isScriptUnpowered` callers |
| 6 | F-138, then F-134: why a tunnel defender killed and flung in the reference match turns above 30 Hz by the arrival its AI planned (frame 1706); fixed with a test, adopt `docs/proposals/F-134-aircraft-goals.patch` (helicopters and Chinooks take goal cells of their own, the owner's playtest of 4 November 2026, items 3 and 12) and measure its manifests | `AIUpdateInterface` (arrival, `StopAtDeath`), `PhysicsBehavior.Fly`; the patch | `Pathfinder::adjustDestination`, `updateAircraftGoal` |

## Thresholds (to be committed before the first measurement)

Item 1: the four Alpine Assault pairings agree at 30 and 60 Hz to their ends with the options above, and the train's place agrees at 120 Hz over 10,000 frames.

## Bottlenecks and showstoppers

A fix of the train moves every Alpine Assault run of the manifest; measure it from its own commit in a worktree. The survey of Phase 60 (§83.8) is the yardstick for the computer's matches.

## Out of scope

The second physical machine and a real network (E1, D-10); whatever only a person can judge; the built-in AI garrisoning or capturing what the map placed (Stage 3).

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`. No game data in the repository. Do not touch the Steam installation.

## Acceptance

Tier 4 at the final state; the train the same at 30, 60 and 120 Hz by a test; every item built or named with its reason.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; the registers of `docs/plan/` brought up to date; the next brief written.

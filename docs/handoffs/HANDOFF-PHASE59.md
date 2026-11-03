> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 59
> **Status:** done by machine (3 November 2026)
> **Predecessor:** HANDOFF-PHASE58.md · **Results in:** `docs/SIMULATION-CORE.md` §81, fidelity register `docs/plan/STAGE-1-PORT.md` §3
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 59): the rest of Phase 58's list, and why the computer decides fewer matches, while the owner's next acceptance waits.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 57" and "Phase 58"), `docs/SIMULATION-CORE.md` §80 and the register entries F-113 to F-126.

## Goal in one sentence

The rest of the list of Phase 58 is ported or named with its reason, and the drop of the AI survey from 62 to 51 of 64 decided matches is explained by a measurement.

## Starting point (measured)

Phase 58 closed on 3 November 2026; its numbers are in `CLAUDE.md`. Built in it: the columns, the roster, the flight deck, crushing, the sniped pilot, the rider's sets, climbing, the small rules, stealth while attacking, the computer's squish (with the flight of a shot measured from the shooter's frame end, F-118), infantry that makes way (F-119), what falls into a building (F-120), the hijacker's guard (F-121), the captured airfield (F-122), the detector inside (F-123), infantry that plans through infantry (F-126).

## Order of work

| # | Behaviour | Port | Original |
|---|---|---|---|
| 1 | The attackers of an Aurora aim behind it while it attacks, nothing hits it (F-124) | `JetAIUpdate.cs` (typed, unread), `SimulationWorld.Projectiles.cs`, `ProjectileModules.Collision` | `JetAIUpdate::getSneakyTargetingOffset`, `Weapon::privateFireWeapon`, `shouldProjectileCollideWith` |
| 1a | A path through standing allies has them make way when that costs less than the way round (F-125) | `SimulationWorld.ExitGoals.cs` (`RequestStepAside`), the detour of `AIUpdateInterface` | `Pathfinder::moveAllies`, `examineNeighboringCells` (`setBlockedByAlly`, `3*COST_DIAGONAL`) |
| 2 | The AI survey fell from 62 to 51 of 64 decided between Phases 56 and 57 | `SimTool ai-survey` | measure each cause of Phase 57 alone (rubble, the airborne target, wheels, the idle weapon, overkill) |
| 3 | The Spectre's sight, a wake on detection, a rebuild's attackers | read the original first; a reader's "missing" is a hypothesis | `SpectreGunshipUpdate`, `StealthUpdate::markAsDetected`, `RebuildHoleBehavior` |
| 4 | Wall tops, wall lines, the dam wave | `KINDOF_WALK_ON_TOP_OF_WALL`, `BuildAssistant` line building, `WaveGuideUpdate` | |
| 5 | Retaliation off, team waypoints | the options and the script actions that name them | |
| 6 | The sounds of §79.6 and the emoticons | presentation | |

Named in Phase 58 and not to be built without a new reason: the cheer and a player's formation (no shipped key reaches them), the tunnel network's guard (only the original AI's scripts give it), the carrier's stop.

## Progress (3 November 2026)

Done, each with a test that fails on the code before, at 30, 60 and 120 Hz, its manifest commit and tier 4 (60 of 60 steps at `d63869bf`): F-124 (`8177596f`, §81.1), F-127 (`aa1cec02`, §81.2), F-128 (`e355e80b`, §81.3). Read and left for the next steps:

- **Retaliation off** (item 5): a flag of the player, off at its creation, set from the client's option `OptionsMenu.wnd:Retaliation` by `MSG_ENABLE_RETALIATION_MODE`, which the local player posts once a second when the two differ (`Player::update`, `GameLogicDispatch.cpp`); `ActiveBody::attemptDamage` asks it. The port retaliates always: it needs a command of its own (a new `CommandTypes.Version` and replay version), the flag in the player's state and the box in the options.
- **Wall tops** (item 4): one shared layer `LAYER_WALL` (`Pathfinder::addWallPiece`, `removeWallPiece`, `classifyWallCells`, `isPointOnWall`), its height `WallHeight` of `AIData.ini`, changed by a map's `map.ini`; five `FortressWall*` objects of `CivilianBuilding.ini` carry `WALK_ON_TOP_OF_WALL`. The bridges' layers of Phase 54 are the model.
- **The Spectre's sight** and **team waypoints**: the list of Phase 58 names them without a source; find what is meant before building anything.

## Bottlenecks and showstoppers

Each behaviour change moves the manifests; measure them per cause in a worktree, one after the other. The survey of item 2 takes hours: run it in pieces of under two hours against fixed binaries.

## Out of scope

The second physical machine and a real network (E1, D-10); whatever only a person can judge.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`. No game data in the repository. Do not touch the Steam installation.

## Acceptance

Tier 4 at the final state; every entry built, or named with its reason; each with a test that fails on the code before, at 30, 60 and 120 Hz where it touches the simulation.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; the registers of `docs/plan/` brought up to date.

> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 58
> **Status:** current (begun 3 November 2026)
> **Predecessor:** HANDOFF-PHASE57.md · **Results in:** `docs/SIMULATION-CORE.md` §80, fidelity register `docs/plan/STAGE-1-PORT.md` §3
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 58): what a player of the original would miss, while the owner's next acceptance waits.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 56" and "Phase 57"), `docs/SIMULATION-CORE.md` §79 and the register entry F-113.

## Goal in one sentence

Every behaviour of the list below is ported or named with its reason, so that what a person meets in the next run is a game with no missing limb.

## Starting point (measured)

Phase 57 closed on 3 November 2026; its numbers are in `CLAUDE.md`. By chance, on the way to a car alarm, it turned out that nothing crushed a vehicle and that `DAMAGE_KILLPILOT` did nothing; a sweep of the code by a reader, not by a test, then named the rest. A reader's "missing" is a hypothesis: one entry of the first draft (a disabled superweapon fires) was wrong, because the disable edge pauses the powers since Phase 40. Every entry is checked against the original before it is built.

## Order of work

Built in this phase: the columns of a group move, a garrison's roster, the flight deck, crushing, the sniped pilot and the unmanned vehicle, the car alarm, the rider's weapon, command and locomotor sets, Nuclear Tanks, climbing, the second point of `OCLUpdate`, the oil derrick's bonus, `RefundValue`, `ShroudRevealToAllRange`, `MASKED`, `UNATTACKABLE`, `NO_ATTACK_FROM_AI`.

Still open, by what a player notices:

| # | Behaviour | Port | Original |
|---|---|---|---|
| 6 | A human steers the Particle Cannon's beam and the Spectre | `FlightModules.cs`, `AirWeaponModules.cs`, no command | `MSG_DO_SPECIAL_POWER_OVERRIDE_DESTINATION`, `AIGroup::groupOverrideSpecialPowerDestination` |
| 11 | Computer tanks chase infantry to run it over | `Skirmish/Original/AiData.cs` | `wantToSquishTarget` (`AIStates.cpp`) |
| 12 | Standing units step aside | F-102 | `AIUpdateInterface::aiMoveAwayFromUnit` |
| 13 | A bike with a stealthed rider stealths (`UseRiderStealth`) | `Phase5Modules.cs` | `StealthUpdate::calcStealthOwner` |
| 14 | `STEALTH_NOT_WHILE_RIDERS_ATTACKING` | `Phase5Modules.cs` | `StealthUpdate.cpp` |
| 15 | Burton shows himself for the last 5 s of a charge | `SpecialAbilityStaging.cs` | `m_preTriggerUnstealthFrames` |
| 16 | Stealth General's supply trucks: 20 s of stealth on delivery | `DockModules.cs` | `SupplyCenterDockUpdate::action` |
| 17 | The cannon stops with its power; the beam reveals the shroud | `FlightModules.cs` | `ParticleUplinkCannonUpdate.cpp` |
| 18 | Search &amp; Destroy lengthens the sight for targets | `BattlePlanModule.cs` | `m_searchAndDestroySightRangeScalar` |
| 19 | A clearing dozer is immune to the mine | `MineModules.cs` | `MinefieldBehavior.cpp` |
| 20 | An Overlord earns its add-on's kills | `ContainChainModules.cs` | `OverlordContain` `setExperienceSink` |
| 21 | A vehicle thrown on a building blows up; on a unit it does damage | `PhysicsAndMinefieldModules.cs` | `PhysicsUpdate.cpp` |
| 22 | Player-set formations, the cheer key | `MetaAction.cs` | `MSG_CREATE_FORMATION`, `MSG_DO_CHEER` |
| 23 | Units on guard in a tunnel network come out (F-58) | `TunnelTracker.cs` | `AITNGuard.cpp` |
| 24 | A hijacker on guard takes vehicles | `SimulationWorld.Guard.cs` | `AIGuard.cpp` |
| 25 to 34 | The low list: a detector inside, the Spectre's sight, jets of a captured airfield, the sneaky offset, a wake on detection, a rebuild's attackers, wall tops, the dam wave, wall lines, retaliation off, team waypoints | see the register | |

The sound loops of the particle cannon, the train's sounds and the emoticons of §79.6 stay on the list. The survey fell from 62 to 51 of 64 decided within 45 minutes between Phases 56 and 57: find out why.

## Bottlenecks and showstoppers

The columns change every group order of a human. Measure the cost of `FindGroundPath` on a large map, in the command phase, also under `movement-budget`.

## Out of scope

The second physical machine and a real network (E1, D-10); whatever only a person can judge.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`. No game data in the repository. Do not touch the Steam installation.

## Acceptance

Tier 4 at the final state; every entry built, or named with its reason; each with a test that fails on the code before, at 30, 60 and 120 Hz where it touches the simulation.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; the registers of `docs/plan/` brought up to date.

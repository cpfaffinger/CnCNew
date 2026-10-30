> **Stage:** 1 – Port (fidelity package G) · **Phase:** 45
> **Status:** done by machine (30 October 2026); the rest is in HANDOFF-PHASE46.md
> **Predecessor:** HANDOFF-PHASE44.md · **Results in:** docs/SIMULATION-CORE.md §59, docs/plan/STAGE-1-PORT.md §3
> **Side items:** F-71 (a move target that differs between the rates), the creation list's `ExtraBounciness`, `ExtraFriction`, `INHERIT_VELOCITY` and `FLOATING` (F-66's rest)
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 45): the two halves of the builders' behaviour that Phase 44 found missing - the GLA worker's dozer brain (F-68) and mine clearing (F-69) - and the rate
difference Phase 44's tier 4 left (F-71).

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 43" and "Phase 44"), `docs/plan/STAGE-1-PORT.md` §3 (F-67 to F-69),
`docs/SIMULATION-CORE.md` §58.5 (the bored dozer), §58.6 (the flight's completed frame) and `docs/GAME-RULES.md` 6e (the supply network). Prerequisite: Phase 44.

## Goal in one sentence

The GLA worker switches between the dozer's brain and the supply truck's as the original's `WorkerStateMachine` does - a dozer (builds, repairs, gets bored) while its supply brain
is busy and nothing forces it to gather, a supply truck while it gathers or docks (F-68) - and builders clear mines: `MINE_CLEARING_DETAIL` raised where the original raises it, a bored
builder that disarms an enemy or neutral mine in range, `ContinueAttackRange`, and the `USES_MINE_CLEARING_WEAPONSET` button (F-69) - each with a test that fails on the code before at
30, 60 and 120 Hz and a manifest commit per cause.

## Starting point (measured, after Phase 44 – re-measure when this phase starts)

State `7c9e308` (Phase 44 merged into `master`). Tier 4 with 56 of 56 steps ok at that state (7,731 s), so this phase begins without another. Reference values:
the retail reference match (driver seed `5EED0001`) FactionGLA at tick 28,980 (original frame 14,490; end tick 29,581), end hash `D2EEF461CB764BD2`, chain `A8E8F06EEBC82332`,
20,958 commands; the stress test `9EE9F730D124C3BD` / `FD8E3547C802ACA2`; 15 of 16 AI pairings decided; every mission run keeps its outcome. Versions: `CommandTypes.Version` 19,
replay 17, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description 3, session state 3, protocol 6, transport 2; compatibility baseline 700 entries. Not run at the end
of Phase 44 (selective by the owner's rule): `scripts/perf-profile.ps1`, the clean room, the campaign checks.

## Decisions that are fixed

- As in Phase 44: the original profile keeps the original's behaviour; a fix of the original's logic is offered behind the rule profile only, written anew; D-32 stays the owner's.
- F-68 touches the economy of every GLA player: expect most GLA pairings to move; measure the AI survey of the GLA pairings before and after (as for D-34) and report it, without judging the balance.

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 44 did not end with it on the same code).
2. F-68: `WorkerAIUpdate::update` (the base AI, the worker machine, then the dozer machine or the supply machine), `WorkerStateMachine` (`supplyTruckSubMachineWantsToEnter`: forced wanting or
   `AI_DOCK`; `supplyTruckSubMachineReadyToLeave`: not wanting and the supply brain in `ST_BUSY`), `resetDozerBrain`/`resetSupplyTruckBrain` on the switch; the worker as a dozer
   gets bored as `DozerAIUpdate` does (§58.5). Read first how a produced worker and an AI-ordered worker reach the dock in the port.
3. F-69: the weapon set flag `MINE_CLEARING_DETAIL` (`setWeaponSetFlag` in `DozerAIUpdate`, `WorkerAIUpdate`, `AIGroup::setMineClearingDetail`), `findMine` (enemy or neutral, possible to
   attack, in `BoredRange`), the attack from the dozer (`CMD_FROM_DOZER`), `ContinueAttackRange` (`AIStates.cpp`, `AIUpdate.cpp`), the client's mine-clearing button.
4. F-71: in the default match America against China on Alpine Assault (`SimTool rate-compare --map-diff FactionAmerica,FactionChina --fields`) a ranger is given a move target that
   differs between 30 and 60 Hz at frame 6,510 while everything agreed before - find the code that reads a position inside a frame step and read the frame end instead.
5. Side items as named above.

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32.

## Acceptance

By machine: F-68 and F-69 built with tests as above (what does not fit is named with its reason); both manifests of the original profile unchanged except by a cause with its own manifest
commit; the checks the changes can reach green.

## Progress

- F-68 (§59.1, `d5dd6f7`, manifests `01cac31`; the AI survey of the GLA pairings before and after in §59.1), F-69 in four steps (§59.7: `2660db5` / `ebd4645`, `110c89a`,
  `0fdc183` / `2a9a863`, `2c64747` / `09ac1e1`, the presentation's test `e840d52`), F-71 (§59.3, `54a76f6` / `4680ac8`), F-66's rest (§59.9, `f1f953a`, input manifest `6c9c5d6`).
- Found on the way: F-70 (§59.2, `5a26040` / `829f945`), and by comparing the default matches across rates F-72 (§59.4, `0e87e10` / `534cdac`), F-73 (§59.5, `dc4e064` / `b517483`),
  F-74 (§59.6, `236badc` / `c46f1d7`), F-75 (§59.8, `eb32bdc` / `4272771`); registered F-76 (the next difference on Bitter Winter).
- Since F-72 the retail reference match with driver seed `5EED0001` ran to its limit: the seed is `5EED0004` (§59.10, `089ad43` / `f590320`).
- Carried into `HANDOFF-PHASE46.md`: F-76, F-35, a locomotor's velocity for `INHERIT_VELOCITY`. Closing measurements: `CLAUDE.md`, paragraph "Phase 45".

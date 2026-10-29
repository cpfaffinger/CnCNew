> **Stage:** 1 – Port (fidelity package E) · **Phase:** 43
> **Status:** in progress (starting state: the end of Phase 42, see below)
> **Predecessor:** HANDOFF-PHASE42.md · **Results in:** docs/ATTACK-POSITION.md, docs/SIMULATION-CORE.md §57, docs/plan/STAGE-1-PORT.md §3
> **Side items:** what Phase 42 left of F-58 (the tunnel network's guard, the flight deck's order passing), the missile's `GarrisonHitKill`
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 43): an attack on a place (F-64) - the original's `AI_ATTACK_POSITION`, which the port lacks: the player's force-fire on the ground and ten shipped
`FIRE_WEAPON` buttons (the Dragon tank's fire wall, the toxin tractor, the Comanche's rocket pods, mine clearing, the battleship, the detonation of the bomb truck, the
convoy truck and the fake building, the demo suicide, the Battle Bus's trap) are refused by the client today.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 41" and "Phase 42"), `docs/ATTACK-POSITION.md`, `docs/plan/STAGE-1-PORT.md` §3
(F-60, F-64), `docs/SIMULATION-CORE.md` §55 and §56. Prerequisite: Phase 42.

## Goal in one sentence

A unit ordered to attack a place walks into range, turns, fires its shots at the place and stops when its shot count is used up, as the original's `AIAttackState` on a
position does; the client sends that order for force-fire and every `FIRE_WEAPON` button at a place; a container passes it to its riders and a spawner to its slaves -
each with a test that fails on the code before at 30, 60 and 120 Hz and a manifest commit per cause.

## Starting point (measured, after Phase 42 – re-measure when this phase starts)

State `5114eea` (29 October 2026), Phase 42 done by machine (`CLAUDE.md`, paragraph "Phase 42"). Retail reference match (driver seed `5EED0001`) winner FactionGLA at
tick 37,890, end hash `6C86A27E229B1849`, chain `8E7AC06F9771E2E6`; stress test `9EE9F730D124C3BD` / `FD8E3547C802ACA2`; 16 of 16 AI pairings decided. `CommandTypes.Version`
18, replay 16, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description 3, session state 3, protocol 6, transport 2; compatibility baseline 700 entries,
the patch layer's 693. Tier 4 ran at `5114eea` with 56 of 56 steps, so this phase begins without another.

## Decisions that are fixed

- As in Phase 42: the original profile keeps the original's behaviour; a fix of the original's logic is offered behind the rule profile only, written anew; Reforged's own
  designs are kind D; D-32 stays the owner's.
- The attack on a place is a state of the simulation beside the guard and the acquisition (like F-58), entered by a command of its own; the client's standing orders
  are not extended for it.

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 42 did not end with it on the same code).
2. F-64 step 1 (`docs/ATTACK-POSITION.md` §3): the command and the state - weapon choice, range with the minimum range, line of sight on the ground, approach, aim, shot,
   shot count, the temporary weapon lock; a version step of `CommandTypes` and the replay format.
3. Step 2: the client - force-fire on the ground, the `FIRE_WEAPON` buttons at a place, at the own place and with `ATTACK_OBJECTS_POSITION`.
4. Step 3: through containers and slaves (`groupAttackPosition`, `orderSlavesToAttackPosition`, the transports' and the Chinook's pass-through).
5. Step 4: `FireWeaponPower` on the state (the stand-in replaced), the scripts' `FIRE_WEAPON` buttons at a place, `ObjectCreationList`'s attack, the mine clearer's
   continue range.
6. Side items: the flight deck's order passing (F-58 step 7's rest), the missile's `GarrisonHitKill` (F-60), the tunnel network's guard (F-58 step 5) only if a measured
   match needs it.

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32.

## Acceptance

By machine: F-64 built with tests as above (what does not fit is named with its reason); both manifests of the original profile unchanged except by a cause with its own
manifest commit; the checks the changes can reach green.

## Progress

Nothing yet.

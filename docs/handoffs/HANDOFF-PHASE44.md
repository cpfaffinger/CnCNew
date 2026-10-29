> **Stage:** 1 – Port (fidelity package F) · **Phase:** 44
> **Status:** done by machine (29 October 2026); the rest is in HANDOFF-PHASE45.md
> **Predecessor:** HANDOFF-PHASE43.md · **Results in:** docs/SIMULATION-CORE.md §58, docs/plan/STAGE-1-PORT.md §3
> **Side items:** the mine clearer's continue range and its weapon set (F-64's rest), the tunnel network's guard (F-58 step 5) if a measured match needs it
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 44): three fidelity findings of the register that the work of Phases 42 and 43 made reachable - the slaves of a spawner follow its attacks (F-65), the
slow deaths of helicopters, jets, the Battle Bus and the neutron missile run the base behaviour's phases (F-37), and the debris of a creation list flies (F-36, with the flight the
shock wave brought).

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 42" and "Phase 43"), `docs/plan/STAGE-1-PORT.md` §3 (F-36, F-37, F-65),
`docs/SIMULATION-CORE.md` §44 (F-05, the slow-death phases), §56.1 (the flight of a shocked object) and §57. Prerequisite: Phase 43.

## Goal in one sentence

A spawner's slaves without free will attack what it attacks and stop when it stops (F-65), the four slow-death subclasses draw the midpoint and play the initial, midpoint and
final lists of the base behaviour as the original's do (F-37), and a `CreateDebris` nugget creates its pieces that fly with the physics of the shock wave (F-36) - each with a
test that fails on the code before at 30, 60 and 120 Hz and a manifest commit per cause.

## Starting point (measured, after Phase 43 – re-measure when this phase starts)

State `a683b9b` (Phase 43 merged into `master`). Tier 4 with 56 of 56 steps ok at that state (7,930 s), so this phase begins without another. Reference values:
the retail reference match (driver seed `5EED0001`) FactionGLA at tick 37,890 (original frame 18,945), end hash `6C86A27E229B1849`, chain `8E7AC06F9771E2E6`; the stress test
`9EE9F730D124C3BD` / `FD8E3547C802ACA2`; 16 of 16 AI pairings decided. Versions: `CommandTypes.Version` 19, replay 17, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match
description 3, session state 3, protocol 6, transport 2; compatibility baseline 700 entries. Not run at the end of Phase 43 (selective by the owner's rule): `scripts/perf-profile.ps1`,
the clean room, the campaign checks.

## Decisions that are fixed

- As in Phase 43: the original profile keeps the original's behaviour; a fix of the original's logic is offered behind the rule profile only, written anew; Reforged's own designs
  are kind D; D-32 stays the owner's.
- F-37 changes the logic random stream wherever such a death happens (the third draw of `beginSlowDeath`): expect most AI pairings to move; one manifest commit, its cause named.

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 43 did not end with it on the same code).
2. F-65: `AIGroup::groupAttackObject` / `groupAttackPosition` / `groupIdle` -> `SpawnBehavior::orderSlavesToAttackTarget` (a forced attack with a chase), `orderSlavesToAttackPosition`,
   `orderSlavesToGoIdle`; `SlavesHaveFreeWill` read; the stinger site and the angry mob are the shipped users. The slaves' attack on an object needs a world-side attack that chases
   (the port's ordered attack is the client's standing order) - read how the guard's attack does it first.
3. F-37: `HelicopterSlowDeathBehavior`, `JetSlowDeathBehavior`, `BattleBusSlowDeathBehavior`, `NeutronMissileSlowDeathBehavior` on the base's begin and update (`SlowDeathBehavior::beginSlowDeath`,
   `update`), each read for what its own update adds.
4. F-36: `CreateDebrisNugget` - the pieces as objects with a physics module (`PhysicsBehavior`'s flight of §56.1), their dispositions (`SEND_IT_FLYING`, `RANDOM_FORCE`, ...), their
   lifetime; the look is the presentation's.
5. Side items: the mine clearer (`USES_MINE_CLEARING_WEAPONSET`, `ContinueAttackRange`), F-58 step 5 only if needed.

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32.

## Acceptance

By machine: F-36, F-37 and F-65 built with tests as above (what does not fit is named with its reason); both manifests of the original profile unchanged except by a cause with its
own manifest commit; the checks the changes can reach green.

## Progress

- F-37 (§58.1, `eeef16f`, manifest `9e90e78`), F-65 (§58.2, `37ee90c`), F-66 (§58.3, `a08a266`, manifest `64a544b`), F-36 (§58.4, `7ea2663`, manifest `d772e44`, input manifest `2de7d59`).
- Found on the way: F-67, the bored dozer (§58.5, `1ec6676`, manifests `9ab428a`); registered F-68 (the worker's dozer brain) and F-69 (mine clearing).
- Found by tier 4: a flight covered the shares of a frame something had completed (§58.6, `4b5bf73`, manifest `87b49cd`; the observer HUD pinned again in `07d2466`); registered F-71.
- Carried into `HANDOFF-PHASE45.md`: F-68, F-69, F-71, F-66's rest (`ExtraBounciness`, `ExtraFriction`, `INHERIT_VELOCITY`, `FLOATING`). Closing measurements: `CLAUDE.md`, paragraph "Phase 44".

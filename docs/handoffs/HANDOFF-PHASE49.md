> **Stage:** 1 – Port (fidelity package K) · **Phase:** 49
> **Status:** done by machine (30 October 2026); the rest is in HANDOFF-PHASE50.md
> **Predecessor:** HANDOFF-PHASE48.md · **Results in:** docs/SIMULATION-CORE.md §63, docs/plan/STAGE-1-PORT.md §3
> **Side items:** a locomotor's velocity for `INHERIT_VELOCITY` (F-66's named difference)
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 49): a Crusader's next target check that differs between the rates on Armored Fury and Bitter Winter (F-80), and the causes after it.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 47" and "Phase 48"), `docs/plan/STAGE-1-PORT.md` §3 (F-80), `docs/SIMULATION-CORE.md` §43 (the frame
convention), §33 (target acquisition), §59 to §62. Prerequisite: Phase 48.

## Goal in one sentence

The default matches America against China on Armored Fury and Bitter Winter are the same game at 30 and 60 Hz past frames 29,669 and 49,244 (F-80: a Crusader's `acquisition.nextCheck`
some 40 frames apart, and the causes after it) - each cause with a test that fails on the code before at 30, 60 and 120 Hz and a manifest commit per cause.

## Starting point (measured, after Phase 48 – re-measure when this phase starts)

State `e6c0b2d` (Phase 48 merged into `master`). Tier 4 with 56 of 56 steps ok at `e6c0b2d` (6,849 s), so this phase begins without another. Reference values: the retail
reference match (driver seed `5EED0004`) FactionGLA at tick 29,490 (original frame 14,745; end tick 30,091), end hash `22BEB490BE548657`, chain `DD8B8BE23385D28C`, 19,741 commands;
the stress test `9EE9F730D124C3BD` / `FD8E3547C802ACA2`; 15 of 16 AI pairings decided; every mission run keeps its outcome. Versions: `CommandTypes.Version` 20, replay 18,
`WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description 3, session state 3, protocol 6, transport 2; compatibility baseline 699 entries, the patch layer's 692.
The default match America against China is the same game at 30 and 60 Hz to the end on Alpine Assault and Death Valley, to frame 49,244 on Bitter Winter and to frame 29,669 on Armored
Fury. Not run at the end of Phase 48 (selective by the owner's rule): `scripts/perf-profile.ps1`, the clean room, the campaign checks.

## Decisions that are fixed

- As before: the original profile keeps the original's behaviour; a fix of the original's logic is offered behind the rule profile only, written anew; D-32 stays the owner's.
- The rate comparison runs with the options of Phases 45 to 48 (`--fields --frames 50000 --persist 10 --ignore '\.nextWake$|\.previousHealth$|constructionProgress|DynamicShroudClearingRange'`); `--trace <id>` follows objects; where a trace does not show
  the trigger, a throwaway build that prints the stack of the deciding method at the ticks in question finds it (never committed).

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 48 did not end with it on the same code).
2. F-80: trace the Crusader on Armored Fury (object 3948, frame 29,669) and find why its next target check is set some 40 frames apart (`SimulationWorld.Acquisition`, `MoodAttackCheckRate`,
   the end of an attack); then the next lasting difference on Armored Fury and Bitter Winter, as far as each is a cause of its own.
3. Side items as named above.

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32, D-34, the built-in AI's army orders (Stage 3).

## Acceptance

By machine: each cause built with a test as above (what does not fit is named with its reason); both manifests of the original profile unchanged except by a cause with its own manifest
commit; the checks the changes can reach green.

## Progress

- F-80 narrowed (30 October 2026, no code changed): the Crusader on Armored Fury (object 3948) has just left its factory. At 30 Hz the step that ends the first leg of its exit path
  orders the second leg (`OrderMove` to the rally point, which ends the acquisition state through `EndAttack`), and the acquisition phase of the same tick decides again (next check
  29,671). Above 30 Hz the second leg is ordered with the last tick of the unit's frame (`AIUpdateInterface.UpdateLiving`, `_exitRallyPending`, Phase 23), after the acquisition of that
  frame decided in its first tick; the order clears it, and the unit decides again only in its next frame (next check two frames later, and from there some 40 frames apart). Found with
  `--trace 3948`, `--commands 14` (no order of a player in the window) and a throwaway build that printed the stack of every change of `Entity.HasMoveTarget`. The next step is the remedy
  named in the register: the unit decides again in the tick of that order, with the decision's tick taken as its frame's first tick - test first a factory that sends a unit with
  `AutoAcquireEnemiesWhenIdle` to a rally point past an enemy, and compare its acquisition state at the end of every frame at 30, 60 and 120 Hz.
- F-80 (§63.1, `4b94136`, manifest `9b32e64`): the unit decides in the tick of the second leg's order, with the frame's first tick as the decision's tick.
- F-66's rest (§63.2, `d58b631`, manifest `981d2c8`): `INHERIT_VELOCITY` takes what moves the source (`IFrameVelocity`).
- F-81 (§63.3, `3bb461a`, manifest `f3cc122`) and F-82 (§63.4, `8b03a6c`, manifest `44fc259`): the two causes found by comparing all sixteen pairings of the manifest.
- All sixteen pairings are the same game at 30 and 60 Hz to their ends (§63.5). Carried into `HANDOFF-PHASE50.md`: F-18. Closing measurements: `CLAUDE.md`, paragraph "Phase 49".

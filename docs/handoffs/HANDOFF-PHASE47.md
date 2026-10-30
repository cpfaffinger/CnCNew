> **Stage:** 1 – Port (fidelity package I) · **Phase:** 47
> **Status:** done by machine (30 October 2026); the rest is in HANDOFF-PHASE48.md
> **Predecessor:** HANDOFF-PHASE46.md · **Results in:** docs/SIMULATION-CORE.md §61, docs/plan/STAGE-1-PORT.md §3
> **Side items:** a locomotor's velocity for `INHERIT_VELOCITY` (F-66's named difference), the next rate differences the default matches show after F-77
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 47): the hulk of a dying truck that turns (F-77), and the script VM's waypoint following that goes on within 40 units of a waypoint (F-78).

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 45" and "Phase 46"), `docs/plan/STAGE-1-PORT.md` §3 (F-77, F-78), `docs/SIMULATION-CORE.md`
§43 (the frame convention), §59 and §60, and `docs/SCRIPT-ENGINE.md` (the team actions). Prerequisite: Phase 46.

## Goal in one sentence

The default match America against China on Bitter Winter is the same game at 30 and 60 Hz past frame 33,486 (F-77: the hulk and debris of a dying supply truck take the heading where
the truck's frame ends), and the VM's waypoint following goes on as `AIFollowWaypointPathState` does - when the move to the waypoint has ended, not within 40 units of it (F-78) - each
with a test that fails on the code before at 30, 60 and 120 Hz and a manifest commit per cause.

## Starting point (measured, after Phase 46 – re-measure when this phase starts)

State `3571666` (Phase 46 merged into `master`). Tier 4 with 56 of 56 steps ok at `3571666` (7,009 s), so this phase begins without another. Reference values: the retail
reference match (driver seed `5EED0004`) FactionGLA at tick 29,490 (original frame 14,745; end tick 30,091), end hash `22BEB490BE548657`, chain `9B4680E436709FD7`, 19,741 commands;
the stress test `9EE9F730D124C3BD` / `FD8E3547C802ACA2`; 16 of 16 AI pairings decided; every mission run keeps its outcome. Versions: `CommandTypes.Version` 20, replay 18,
`WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description 3, session state 3, protocol 6, transport 2; compatibility baseline 699 entries, the patch layer's 692.
The default match America against China on Alpine Assault is the same game at 30, 60 and 120 Hz to the end; on Bitter Winter it agrees at 30 and 60 Hz to frame 33,486 (F-77). Not run at
the end of Phase 46 (selective by the owner's rule): `scripts/perf-profile.ps1`, the clean room, the campaign checks.

## Decisions that are fixed

- As before: the original profile keeps the original's behaviour; a fix of the original's logic is offered behind the rule profile only, written anew; D-32 stays the owner's.
- The rate comparison runs with the options of Phase 45 and 46: `SimTool rate-compare game/content --map-diff FactionAmerica,FactionChina --map "Maps/Bitter Winter/Bitter Winter.map"
  --fields --frames 50000 --persist 10 --ignore '\.nextWake$|\.previousHealth$|constructionProgress|DynamicShroudClearingRange'` (construction progress is spread differently inside a
  frame on purpose; `--trace <id>[,<id>] --trace-from F --trace-to G` follows objects). An object's id is found by tracing a range of ids around the frame (`--trace 3550,...,3656`)
  and filtering the output - the dying truck of F-77 has an id below 3550.

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 46 did not end with it on the same code).
2. F-77: find the truck, its death (damage, slow death, its creation list) and where its heading is read inside its frame's turn above 30 Hz (`Entity.FrameEndAngle`, `CompleteFrame`,
   `AIUpdateInterface.StopAtDeath`, `SimulationWorld.CreationLists.Configure`); then the next lasting difference, as far as each is a cause of its own.
3. F-78: read `AIFollowWaypointPathState::update` and `AIMoveToState` (when a move ends, `CloseEnoughDist`), and replace the VM's 40-unit arrival test of its followers.
4. Side items as named above.

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32, D-34, the built-in AI's army orders (Stage 3).

## Acceptance

By machine: F-77 and F-78 built with tests as above (what does not fit is named with its reason); both manifests of the original profile unchanged except by a cause with its own manifest
commit; the checks the changes can reach green.

## Progress

- F-77 (§61.1) in two steps: a creation list reads its source's heading (`01c23ba`, manifest `1db078c`) and place (`f02bc73`, manifest `8557bcb`) where the source's frame ends; Bitter
  Winter agrees at 30 and 60 Hz to frame 39,931.
- F-78 (§61.2, `21e6274`, manifest `209392f`): a waypoint follower goes on when its move has ended. The brief's premise was half right: a unit inside the 40 units was ordered on only when
  its move had ended anyway; the difference was a move that ended farther away, which the VM repeated for good.
- Registered: F-79 (a supply truck's heading on Bitter Winter at frame 39,931).
- Carried into `HANDOFF-PHASE48.md`: F-79, a locomotor's velocity for `INHERIT_VELOCITY`. Closing measurements: `CLAUDE.md`, paragraph "Phase 47".

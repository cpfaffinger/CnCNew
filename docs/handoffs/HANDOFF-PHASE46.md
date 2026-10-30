> **Stage:** 1 – Port (fidelity package H) · **Phase:** 46
> **Status:** done by machine (30 October 2026); the rest is in HANDOFF-PHASE47.md
> **Predecessor:** HANDOFF-PHASE45.md · **Results in:** docs/SIMULATION-CORE.md §60, docs/plan/STAGE-1-PORT.md §3
> **Side items:** a locomotor's velocity for `INHERIT_VELOCITY` (F-66's named difference), the next rate differences the default matches show after F-76
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 46): the team orders of the script VM and the computer player as group orders (F-35), and the next rate difference of the default matches (F-76).

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 44" and "Phase 45"), `docs/plan/STAGE-1-PORT.md` §3 (F-35, F-76), `docs/SIMULATION-CORE.md`
§44 (F-04, the group move), §58 and §59, and `docs/SCRIPT-ENGINE.md` (the team actions). Prerequisite: Phase 45.

## Goal in one sentence

The script VM's team moves (`MOVE_TEAM_TO`, the hunts, the waypoint paths) and the computer player's army orders reach the world as the original's group orders - every member
with a destination of its own (`AIGroup::groupMoveToPosition`, `groupFollowWaypointPath`), the VM's arrival tests following those destinations (F-35) - and the default match
America against China on Bitter Winter is the same game at 30 and 60 Hz past frame 25,631 (F-76) - each with a test that fails on the code before at 30, 60 and 120 Hz and a
manifest commit per cause.

## Starting point (measured, after Phase 45 – re-measure when this phase starts)

State `197d034` (Phase 45 merged into `master`). Tier 4 with 56 of 56 steps ok at `197d034` (6,971 s), so this phase begins without another. Reference values: the retail
reference match (driver seed `5EED0004`) FactionGLA at tick 29,490 (original frame 14,745; end tick 30,091), end hash `22BEB490BE548657`, chain `9B4680E436709FD7`, 19,741 commands;
the stress test `9EE9F730D124C3BD` / `FD8E3547C802ACA2`; 16 of 16 AI pairings decided; every mission run keeps its outcome. Versions: `CommandTypes.Version` 20, replay 18,
`WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description 3, session state 3, protocol 6, transport 2; compatibility baseline 699 entries, the patch layer's 692.
The default match America against China on Alpine Assault is the same game at 30, 60 and 120 Hz to the end, the other Alpine Assault pairings at 30 and 60 Hz; on Bitter Winter it
agrees to frame 25,631 (F-76). Not run at the end of Phase 45 (selective by the owner's rule): `scripts/perf-profile.ps1`, the clean room, the campaign checks.

## Decisions that are fixed

- As before: the original profile keeps the original's behaviour; a fix of the original's logic is offered behind the rule profile only, written anew; D-32 stays the owner's.
- F-35 changes how every computer army and every scripted team walks: expect most pairings and many mission runs to move; measure the AI survey of the 16 pairings before and
  after and report it, without judging the balance.

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 45 did not end with it on the same code).
2. F-76: `SimTool rate-compare --map-diff FactionAmerica,FactionChina --map "Maps/Bitter Winter/Bitter Winter.map" --fields` - find the code that decides the Humvee's turret
   inside a frame step above 30 Hz (`--trace <id>`), and the frame convention it breaks (§43); then the next lasting difference of the default matches, as far as each is a cause
   of its own.
3. F-35: read `AIGroup::groupMoveToPosition`, `groupFollowWaypointPath`, `groupHunt` and how the script actions and `AIPlayer` build groups; send the VM's team moves and the
   built-in computer player's army orders as `GroupMoveCommand` (or a group form of the order they are) - the VM's "arrived" tests read each member's own destination, not 40
   units around the ordered point.
4. Side items as named above.

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32, D-34.

## Acceptance

By machine: F-35 and F-76 built with tests as above (what does not fit is named with its reason); both manifests of the original profile unchanged except by a cause with its own manifest
commit; the checks the changes can reach green.

## Progress

- F-76 (§60.1, `86e363c`, manifest `ad7d42a`): a turret's hold after a dead victim counts from the next frame; Bitter Winter agrees at 30 and 60 Hz to frame 33,486.
- F-35 (§60.2): the script's team move (`204c126`, manifest `29b4b97`), the walking reinforcements and the approach path move (`7633bac`, no field moved) are group orders; the
  waypoint following and the hunt are orders per member in the original too - the VM's 40-unit arrival is registered as F-78; the built-in AI's army orders are not the original's.
- Registered: F-77 (the hulk of a dying supply truck on Bitter Winter takes another heading at 60 Hz), F-78.
- The rate comparison needs the options of Phase 45 (`--frames 50000 --persist 10 --ignore ...constructionProgress|DynamicShroudClearingRange`); without them construction progress,
  spread differently inside a frame on purpose, shows as a difference at frame 168.
- Carried into `HANDOFF-PHASE47.md`: F-77, F-78, a locomotor's velocity for `INHERIT_VELOCITY`. Closing measurements: `CLAUDE.md`, paragraph "Phase 46".

> **Stage:** 1 – Port (fidelity package J) · **Phase:** 48
> **Status:** next (starting state: the end of Phase 47, see below)
> **Predecessor:** HANDOFF-PHASE47.md · **Results in:** docs/SIMULATION-CORE.md §62, docs/plan/STAGE-1-PORT.md §3
> **Side items:** a locomotor's velocity for `INHERIT_VELOCITY` (F-66's named difference)
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 48): the next rate differences of the default matches, beginning with a supply truck's heading on Bitter Winter (F-79).

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 46" and "Phase 47"), `docs/plan/STAGE-1-PORT.md` §3 (F-79), `docs/SIMULATION-CORE.md` §43 (the frame
convention), §59 to §61. Prerequisite: Phase 47.

## Goal in one sentence

The default match America against China on Bitter Winter is the same game at 30 and 60 Hz past frame 39,931 (F-79 and the causes after it, each found with the rate comparison), and the
other default matches of the manifest's maps are compared the same way - each cause with a test that fails on the code before at 30, 60 and 120 Hz and a manifest commit per cause.

## Starting point (measured, after Phase 47 – re-measure when this phase starts)

State `2a96d05` (Phase 47 merged into `master`). Tier 4 with 56 of 56 steps ok at `2a96d05` (6,824 s), so this phase begins without another. Reference values: the retail
reference match (driver seed `5EED0004`) FactionGLA at tick 29,490 (original frame 14,745; end tick 30,091), end hash `22BEB490BE548657`, chain `DD8B8BE23385D28C`, 19,741 commands;
the stress test `9EE9F730D124C3BD` / `FD8E3547C802ACA2`; 15 of 16 AI pairings decided (Bitter Winter America-China a stalemate since F-77); every mission run keeps its outcome.
Versions: `CommandTypes.Version` 20, replay 18, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description 3, session state 3, protocol 6, transport 2; compatibility
baseline 699 entries, the patch layer's 692. The default match America against China on Alpine Assault is the same game at 30, 60 and 120 Hz to the end; on Bitter Winter it agrees at 30
and 60 Hz to frame 39,931 (F-79). Not run at the end of Phase 47 (selective by the owner's rule): `scripts/perf-profile.ps1`, the clean room.

## Decisions that are fixed

- As before: the original profile keeps the original's behaviour; a fix of the original's logic is offered behind the rule profile only, written anew; D-32 stays the owner's.
- The rate comparison runs with the options of Phases 45 to 47: `SimTool rate-compare game/content --map-diff FactionAmerica,FactionChina --map "<map>" --fields --frames 50000
  --persist 10 --ignore '\.nextWake$|\.previousHealth$|constructionProgress|DynamicShroudClearingRange'`; `--trace <id>[,<id>] --trace-from F --trace-to G` follows objects, and an id
  is found by tracing a range of ids around the frame and filtering the output (the template name is on every header line).

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 47 did not end with it on the same code).
2. F-79: trace the truck (object 3732) around frame 39,931 and find where its heading is decided inside a frame step above 30 Hz; then the next lasting difference on Bitter Winter, as
   far as each is a cause of its own.
3. The same comparison for the default matches on Armored Fury and Death Valley; register what is found.
4. Side items as named above.

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32, D-34, the built-in AI's army orders (Stage 3).

## Acceptance

By machine: each cause built with a test as above (what does not fit is named with its reason); both manifests of the original profile unchanged except by a cause with its own manifest
commit; the checks the changes can reach green.

## Progress

Nothing yet.

> **Stage:** 1 – Port (fidelity package M) · **Phase:** 51
> **Status:** next (starting state: the end of Phase 50, see below)
> **Predecessor:** HANDOFF-PHASE50.md · **Results in:** docs/SIMULATION-CORE.md §65, docs/plan/STAGE-1-PORT.md §3
> **Side items:** a ranger's `enterTarget` on Alpine Assault (F-87), and what the comparison of the sixteen pairings still finds
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 51): the approximations of target acquisition (F-17, `docs/SIMULATION-CORE.md` §33.5).

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 49" and "Phase 50"), `docs/plan/STAGE-1-PORT.md` §3 (F-17, F-87), `docs/SIMULATION-CORE.md` §33.5, §43 (the frame
convention), §64. Prerequisite: Phase 50.

## Goal in one sentence

A unit looks for its victim as `AIUpdateInterface::getNextMoodTarget` does - each remaining approximation of §33.5 read in the original, closed with a test that fails on the code before at 30, 60 and 120 Hz
and a manifest commit per cause, or named with its reason.

## Starting point (measured, after Phase 50 – re-measure when this phase starts)

State `97e1685` (Phase 50 merged into `master`). Tier 4 with 56 of 56 steps ok at `97e1685` (7269 s), so this phase begins without another. Reference values: the retail reference match (driver seed
`5EED0004`) FactionGLA at tick 36,540 (original frame 18,270; end tick 37,141), end hash `3E41F7F2A4F4D9A5`, chain `938C7502C5E562FF`, 22,445 commands; the stress test `9EE9F730D124C3BD` / `FD8E3547C802ACA2`;
14 of 16 AI pairings decided; every mission run keeps its outcome. Versions: `CommandTypes.Version` 20, replay 18, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description 3, session state 3,
protocol 6, transport 2; compatibility baseline 696 entries, the patch layer's 689; world fingerprint `046495ECFC8C06E9`. Fifteen of the sixteen pairings of the manifest are the same game at 30, 60 and 120 Hz to their ends, Alpine Assault America against China to frame 45,501 (F-87, beyond the manifest's limit of 45,000 frames). The objective driver at 60 Hz: victory 24/24 and defeat 24/24. Not run at the end of Phase 50 (selective by the owner's rule):
`scripts/perf-profile.ps1`, the clean room, audit and reachability.

## What the port does today, and what §33.5 still names

§33.5 was written in Phase 17; much of it has been closed since (the line of sight, F-55; firing from inside, F-60; the frame convention). Read it against the code first. What was still open when this brief was
written: a resting computer unit does not chase what it picks (`MayChase` is only true on an attack-move, where the original's `AIIdleState` calls `aiAttackObject` for a computer player's unit); passive teams of
computer players do not answer their last attacker (`getNextMoodTarget`, `MM_Controller_AI` and `MM_Mood_Passive`); `AIData.ini`'s values are named constants, not typed; `NOTWHILEATTACKING`; distances are between
centres where the original measures `FROM_BOUNDINGSPHERE_2D`; the empty-building filter asks "has a container and is empty" instead of `isGarrisonable`; the fog filter applies to every unit, where the original's
`AI::UNFOGGED` is for human players' units only; `canAutoAcquireWhileStealthed`, `IS_USING_ABILITY`.

## Decisions that are fixed

- As before: the original profile keeps the original's behaviour; a fix of the original's logic is offered behind the rule profile only, written anew; D-32 stays the owner's.
- A computer unit's chase from rest touches the command issuers that position their units (the reason §33.5 gives for leaving it out): `SkirmishAi` is Stage 3's and adapts to the world, not the other way round.

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 50 did not end with it on the same code).
2. F-87 (`rate-compare --map-diff FactionAmerica,FactionChina --map "Maps/Alpine Assault/Alpine Assault.map" --fields --trace 5917 --trace-from 45490`).
3. F-17, item by item, as above.
4. Every rate difference a change opens is compared with `rate-compare --map-diff ... --fields --frames 50000 --persist 10 --ignore '\.nextWake$|\.previousHealth$|constructionProgress|DynamicShroudClearingRange|boredSince$'` over the sixteen pairings. Run `Game.Tests` and the real data **before** the
   manifests: in Phase 50 they found what the manifests do not ask (§64.1).

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32, D-34, the built-in AI's army orders (Stage 3).

## Acceptance

By machine: each cause built with a test as above (what does not fit is named with its reason); both manifests of the original profile unchanged except by a cause with its own manifest
commit; the checks the changes can reach green.

## Progress

Nothing yet.

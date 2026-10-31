> **Stage:** 3 and 4 – Changes, Multiplayer (the owner's decisions of 31 October 2026) · **Phase:** 53
> **Status:** next (starting state: the end of Phase 52, see below)
> **Predecessor:** HANDOFF-PHASE52.md · **Results in:** docs/DEDICATED-SERVER.md, docs/UI-TEXT-AND-SETTINGS.md, docs/SKIRMISH-AI.md, a new reference manifest
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 53): the decisions that are a default: D-8, D-19, D-21, D-22, D-23 (the interval), D-25, D-26, D-30, D-34.

You work autonomously on this repository. First read `CLAUDE.md` (paragraph "Phase 52"), `docs/plan/PROGRAMME.md` §7, `docs/PATCH-LAYER.md`.

## Goal in one sentence

What the owner decided and only needs a default or a small rule is the default: a person plays without a time limit, a server holds seats and delays watchers, the modern skin and the choice of 120 Hz, an anonymous player, votes' hash interval, the GLA worker's scouting.

## Starting point (measured, after Phase 52 – re-measure when this phase starts)

State `9721c8e`. Tier 4 with 56 of 56 steps ok in three pieces (27 steps at `23adfdc`, 28 steps at `d10af31`, the real-data simulation tests at the final state after a test that assumed the original's weapon data; two tests fixed on the way, no code) at `9721c8e` (8700 s). Reference values of the product: the reference match (driver seed `5EED0003`, half an hour) a stalemate at tick 108,000, `06C310520645B428` / `7A99249F70171948`; the stress test `9EE9F730D124C3BD` / `5611A72EDB7D52F2`; 14 of 16 AI pairings decided; world fingerprint `73AE8F9DD1855D13`. Versions: `CommandTypes.Version` 20, replay **19**, `WorldSnapshot` **11**, `MissionSnapshot` 3, `SaveGame` 3, match description 3, session state 3, protocol 6, transport 2; compatibility baseline **674** entries.

## Brief (in this order)

1. D-8: a person plays until the match is decided; a match of computer players keeps its budget. 2. D-26: no limit of a match's length on a server. 3. D-19, D-23: the server's defaults. 4. D-25: anonymous unless asked. 5. D-30: a line to allies without allies says so. 6. D-21: the modern skin as the default of a first start. 7. D-22: 120 Hz in More options. 8. D-34 variant B, a manifest commit of its own.

## After it

The rank system (D-15) and the solo bonuses (D-6), F-88 (the rate differences, three of them named in `KnownMovementRateCases`), F-40, the factory exit (D-2), the road order (D-7), the original AI's C++ half (D-16), votes (D-23), the majority rule (D-24), Ubuntu (D-20).

## Acceptance

By machine: tier 4 green; every default named in `docs/DEDICATED-SERVER.md` and `docs/UI-TEXT-AND-SETTINGS.md`.

## Progress

Nothing yet.

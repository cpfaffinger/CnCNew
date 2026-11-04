> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 60
> **Status:** done (4 November 2026; `docs/SIMULATION-CORE.md` §83)
> **Predecessor:** HANDOFF-PHASE59.md · **Results in:** `docs/SIMULATION-CORE.md` §83, `docs/SKIRMISH-AI.md`, `docs/PERFORMANCE.md`, fidelity register `docs/plan/STAGE-1-PORT.md` §3
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 60): a skirmish with the built-in AI places every object of its map, as the original's does.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 58" and "Phase 59"), `docs/SIMULATION-CORE.md` §81, `docs/SKIRMISH-AI-ORIGINAL.md` §6.4 and the register entry F-129.

## Goal in one sentence

A skirmish of the built-in AI - the default of the game - has the civilian and tech buildings, the trees, the props and the train of its map, as the original's `GameLogic::startNewGame` places every object of a map, and the computer and the manifests are measured on it.

## Starting point (measured)

Phase 59 closed on 3 November 2026 (numbers in `CLAUDE.md`, paragraph "Phase 59"). Found while its train sounds were tested (F-129): `SkirmishMatchSession.Start` places from the map only the supply warehouses (`SpawnWarehouses`); everything else a skirmish map holds - oil derricks, hospitals and the other tech buildings, civilian buildings a player garrisons, the train of Alpine Assault, trees and props - does not exist in a match of the built-in AI. The session of the original AI (`ScriptedSkirmishSession`) places every object since Phase 53 (`docs/SKIRMISH-AI-ORIGINAL.md` §6.4: Alpine Assault with every object 926 against 441 µs per tick at 60 Hz, Twilight Flame eight players 1,248 against 898 µs after warm-up, measured on a busy machine). The survey of Phase 59 (§81.9): 47 of 64 decided within 45 game minutes before F-125; the reference manifest after F-125 decides 13 of its 16 pairings. The presentation draws every map object as static scenery and hides an item once an entity of its template stands within one unit (`StaticScenery`, `FrameProjector.ClaimRadius`), so the missing objects are seen and do nothing.

What the maps hold (counted on 3 November 2026 over the 62 skirmish maps of the content with `Player_N_Start` waypoints, outside waypoints, road and bridge points): 56,453 objects, 30,846 of the team `teamPlyrCivilian`, 25,602 of no team, 5 of `teamSkirmishGLA`. Properties `updateObjValuesFromMapProperties` reads, as the maps set them: `objectIndestructible` yes on 281 (fortress walls, drain pipes, broken wall pieces, train tunnels, rocks, water towers), `objectAggressiveness` -2 on 283 (the civilian cars, tractors and scorch marks: asleep), `objectTargetable` yes on 50 (barrels, gas tanks, watch towers: a player may attack them without force), `objectPowered` no on 14 (cars), `objectInitialHealth` not 100 on 15, `objectMaxHPs` -1 on 10 (ignored), `objectEnabled` no on none. The original's placement in a multiplayer game (`GameLogic::startNewGame`): bridges and wall tops first, `OPTIMIZED_TREE` only drawn, `PROP` and fluff (`CLEARED_BY_BUILD` without a fence width) only drawn (`forceFluffToProp` is always on in multiplayer), trees always (`useTrees`), everything else a logic object of the team its `originalOwner` names (`validateTeam`: an unknown team is the neutral one).

## What is already done - do not build twice

Since the brief was written, the owner's playtest of 3 November 2026 was worked off (`docs/SIMULATION-CORE.md` §82): a skirmish explores the map at its start (`ExploreMapCommand`, `CommandTypes.Version` 26, replay 25 - a new command of this phase takes the next number), the built-in AI looks at a site it cannot see, and the reference manifest decides 12 of its 16 pairings (P4's yardstick is 12, not 13).

The placement of every map object by the original AI's session (`ScriptedSkirmishSession.Attach`, `MissionSession`); the train (`RailroadBehavior`) and its cost cut (§6.4 there); bridges as objects (Phase 54); the sounds of a train and a cannon (§81.7).

## Decisions that are fixed

One product (D-3, D-4, D-32, D-33): the built-in AI's skirmish is the default; it plays on the patched data. The original's way is the yardstick (Stage 1).

## Brief (in this order)

| # | Work | Port | Original |
|---|---|---|---|
| 1 | Every object of the map in a skirmish of the built-in AI, the neutral player's and each side's as the map names them, at its place, angle and height, with its properties (`objectInitialHealth`, `objectEnabled`, `objectIndestructible`, `objectUnsellable`, the name scripts use) | `SkirmishMatchSession.Start`, `SpawnWarehouses`; the mission's placement as the model | `GameLogic::startNewGame` (the loop over `MapObject`s, `ThingFactory::newObject`, `updateObjValuesFromMapProperties`) |
| 2 | The computer with the map's objects: what it must not break on (a garrisoned civilian building, a tech building to capture, trees that block a site) and what it may not use (it ignores what the original's AI ignores) | `SkirmishAi` | `AIPlayer` |
| 3 | The cost: `scripts/perf-profile.ps1` before and after, the eight-player maps; thresholds committed before the first measurement | | |
| 4 | The manifests, the survey (`ai-survey --seeds 4 --minutes 45` and 90) and the rate comparison of the pairings | | |
| 5 | Side items: the sticky bombs of a rebuild hole (`transferBombs`, F-128's rest), the train's whistle on a collision and the carriages' clatter (§81.7), F-40's rest | | |

## Bottlenecks and showstoppers

The change moves every skirmish run of the reference manifest and the survey; measure it from its own commit in a worktree. The train costs: the cost is part of the acceptance, not an afterthought. The rate comparison of §6.4 left the train out at 30, 60 and 120 Hz - find out why its shares of a frame round differently before claiming the pairings agree.

## Out of scope

The second physical machine and a real network (E1, D-10); whatever only a person can judge.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`. No game data in the repository. Do not touch the Steam installation.

## Acceptance

Tier 4 at the final state; a skirmish of the built-in AI holds every object its map places, shown by a test against real data at 30, 60 and 120 Hz; the thresholds of item 3 met or reported as missed; every item built or named with its reason.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; the registers of `docs/plan/` brought up to date; the next brief written.

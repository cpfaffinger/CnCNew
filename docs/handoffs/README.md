# Phase briefs, ordered by stage and milestone

As of: 10 October 2026, state `2777a90`. The plan is [`../plan/PROGRAMME.md`](../plan/PROGRAMME.md); the format of a brief
is [`../plan/BRIEF-FORMAT.md`](../plan/BRIEF-FORMAT.md). Every brief carries a header block with stage, milestone and
status; the worked-off briefs keep their text as documents of their time (their numbers are outdated – the yardstick is
`baselines/reference-manifest.json`). Phases 4 to 23 and 31 are worked off (31 ran before 24, D-12), **Phase 24 is current**, 25 to 30 are planned. The
briefs of Phases 1 to 3 were never written as files (their scope and acceptance are in `docs/IMPLEMENTATION-PLAN.md`
§1–§3).

**The next brief to work on:** [HANDOFF-PHASE24.md](HANDOFF-PHASE24.md) – the session core (S2-M7); its "Starting point" was continued with the state
measured at the end of Phase 31 (the fidelity package A of Stage 1, [HANDOFF-PHASE31.md](HANDOFF-PHASE31.md), done on 10 October 2026). Phase numbers are identities, not an order: 31 ran before 24 by
decision of the project owner.

## Stage 1 – Port (`../plan/STAGE-1-PORT.md`)

| Phase | Brief | Milestone | Topic | Status |
|---|---|---|---|---|
| 4 | [HANDOFF-PHASE4.md](HANDOFF-PHASE4.md) | S1-M3 (+ S1-M2 block 0) | Deterministic simulation core | done 28 September 2026 |
| 5 | [HANDOFF-PHASE5.md](HANDOFF-PHASE5.md) | S1-M4 | Game rules, combat, economy, production (continuation brief) | done; superseded by 6 and 7 |
| 6 | [HANDOFF-PHASE6.md](HANDOFF-PHASE6.md) | S1-M4, S1-M5 | Rest of the game rules, then world, terrain, movement, vision, skirmish AI | done; superseded by 7 |
| 7 | [HANDOFF-PHASE7.md](HANDOFF-PHASE7.md) | S1-M5 (+ S3-M1 built-in AI) | World, terrain, movement, vision and the skirmish AI | done 30 September 2026 |
| 8 | [HANDOFF-PHASE8.md](HANDOFF-PHASE8.md) | S1-M8, S1-M9 (+ S1-M6 debt) | World presentation; input, UI, localisation; open simulation debts | done 30 September 2026 (libraries and smoke tests; visual acceptance → S1-M11) |
| 9 | [HANDOFF-PHASE9.md](HANDOFF-PHASE9.md) | S1-M7 | Skirmish, missions, campaigns, savegames | done 1 October 2026 (headless) |
| 10 | [HANDOFF-PHASE10.md](HANDOFF-PHASE10.md) | S1-M7, S1-M4, S1-M5 | Finishing skirmish, shell connection, collision, build and rule limits | done 1 October 2026 |
| 11 | [HANDOFF-PHASE11.md](HANDOFF-PHASE11.md) | S1-M7 | Remaining script families, campaign shell, mission HUD | done 1 October 2026 |
| 12 | [HANDOFF-PHASE12.md](HANDOFF-PHASE12.md) | S1-M7, S1-M9 | Objective driver, script families, client wiring | done 2 October 2026 |
| 13 | [HANDOFF-PHASE13.md](HANDOFF-PHASE13.md) | S1-M7, S1-M6 | Reinforcement with transport, the two open victory missions, VM semantics | done 3 October 2026 |
| 14 | [HANDOFF-PHASE14.md](HANDOFF-PHASE14.md) | S1-M6, S1-M7 | Module coverage complete, weapon slots, formations, path flight | done 3 October 2026 |
| 15 | [HANDOFF-PHASE15.md](HANDOFF-PHASE15.md) | S1-M8 | Tech-demo maturity I: world rendering, security of the dependencies | built – visual acceptance by a human open |
| 16 | [HANDOFF-PHASE16.md](HANDOFF-PHASE16.md) | S1-M10, S1-M9 | Tech-demo maturity II: sound, cursor, waypoints, texts, mission in the client | built – not accepted (never heard) |
| 17 | [HANDOFF-PHASE17.md](HANDOFF-PHASE17.md) | S1-M11 | Playability by humans, commands-only agent | ended without its acceptance goal; "Rest of Phase 17" at its end |
| **31** | [HANDOFF-PHASE31.md](HANDOFF-PHASE31.md) | **S1-M11** | Fidelity package A: scatter on exit, own destination per unit of a group, slow-death phases (F-03 to F-05) | **done** (10 October 2026: `f9abff8`/`12533c6`, `39a1656`/`1a191cd`, `94711fb`/`bab51c3`, and the member-order fix `f41c851`/`2b34dba`; `docs/SIMULATION-CORE.md` §44) – built and measured headless, not seen by a human |
| – | `docs/ACCEPTANCE.md`, `scripts/acceptance.ps1` | S1-M11 | Acceptance catalogue and guided run (85 items) | built – not used by a human |
| – | `docs/INSTALLATION.md`, `docs/UI-VANILLA-FIT.md` | S1-M12, S1-M9 | Publication: installer from the Steam master, original layouts | built – not accepted |
| – | `docs/SKIRMISH-AI-ORIGINAL.md` | S1-M13 | The skirmish AI of the original as an option (side product of Phase 23) | built as an option |

## Stage 2 – Engine (`../plan/STAGE-2-ENGINE.md`)

| Phase | Brief | Milestone | Topic | Status |
|---|---|---|---|---|
| 18 | [HANDOFF-PHASE18.md](HANDOFF-PHASE18.md) | S2-M1 | Measurement basis: reference manifest, determinism, performance profile, tick-rate inventory | done 6 October 2026; second machine open (D-10) |
| 19 | [HANDOFF-PHASE19.md](HANDOFF-PHASE19.md) | S2-M2 | Performance: lowering the tick costs without changing results | done 6 October 2026; maximum and allocation missed |
| 20 | [HANDOFF-PHASE20.md](HANDOFF-PHASE20.md) | S2-M3 | Tick rate as a value of the world: simulation | done 6 October 2026 |
| 21 | [HANDOFF-PHASE21.md](HANDOFF-PHASE21.md) | S2-M3 | Tick rate as a parameter: presentation, game layer, UI, sound, tools, tests | done 7 October 2026 |
| 22 | [HANDOFF-PHASE22.md](HANDOFF-PHASE22.md) | S2-M4 | Fixed point Q39.24 | done 7 October 2026 |
| 23 | [HANDOFF-PHASE23.md](HANDOFF-PHASE23.md) | S2-M5 (+ S3-M2, S1-M13, Stage 1 fidelity fixes) | Time-step invariance, 60 Hz as the default | done 9 October 2026 with named limits |
| – | `docs/TESTING.md`, `scripts/test.ps1` | S2-M6 | Test strategy in four tiers | done 10 October 2026 |
| **24** | [HANDOFF-PHASE24.md](HANDOFF-PHASE24.md) | **S2-M7** | Session core: AI in the state, skirmish savegame, match description, command path, tick source | **current** – starting state measured after Phase 31, not begun |
| – | – | S2-M8 | Engine follow-ups (tick maximum, hash constructions, allocation, thread, formats, documentation) | open backlog; briefs when scheduled |

## Stage 3 – Changes (`../plan/STAGE-3-CHANGES.md`)

No brief exists yet; the milestones are defined in the stage document and get a brief when scheduled. Decided (D-1):
S3-M2 to S3-M5 may run as parallel packages from Phase 24 on; S3-M1, S3-M6 and S3-M7 come after Phase 27. The next free
phase number is 32.

| Phase | Brief | Milestone | Topic | Status |
|---|---|---|---|---|
| – | – | S3-M1 | AI kinds, difficulty and tactics (built-in `SkirmishAi` exists and is the default) | open |
| – | `docs/SPECTATOR-MODE.md` | S3-M2 | Spectator and observer (built in Phase 23) | built – not accepted |
| – | – | S3-M3 | Replay browser | open |
| – | – | S3-M4 | Match analysis with graphics | open |
| – | – | S3-M5 | Modern user interface | open |
| – | – | S3-M6 | Movement and pathfinding beyond the original (D-3) | open |
| – | [HANDOFF-PHASE30.md](HANDOFF-PHASE30.md) items 3 and 4 | S3-M7 | Bug-fix and balance layers (GeneralsGameCode, GeneralsGamePatch) | open |

## Stage 4 – Multiplayer (`../plan/STAGE-4-MULTIPLAYER.md`)

| Phase | Brief | Milestone | Topic | Status |
|---|---|---|---|---|
| 25 | [HANDOFF-PHASE25.md](HANDOFF-PHASE25.md) | S4-M1 | Protocol, room, relay in the process; local skirmish over the network path | planned |
| 26 | [HANDOFF-PHASE26.md](HANDOFF-PHASE26.md) | S4-M2 | Dedicated server, UDP, clock synchronisation, eight clients | planned |
| 27 | [HANDOFF-PHASE27.md](HANDOFF-PHASE27.md) | S4-M3 | Desync diagnosis, rejoin, spectators, server replay | planned |
| 28 | [HANDOFF-PHASE28.md](HANDOFF-PHASE28.md) | S4-M4 | Server operation and security | planned |
| 29 | [HANDOFF-PHASE29.md](HANDOFF-PHASE29.md) | S4-M5 | Multiplayer in the client, latency perception, acceptance with humans | planned |
| – | – | S4-M6 | Co-simulating server as an option (hybrid, D-11) | open; after Phase 27 |

## Release gates

| Phase | Brief | Gate | Topic | Status |
|---|---|---|---|---|
| 30 | [HANDOFF-PHASE30.md](HANDOFF-PHASE30.md) | R1 (items 1, 2, 5–8 for the single player), R4 (package incl. server) | Patch integration, quality, shippable completion | planned; its items are distributed to R1, S3-M7 and R4 |
| – | – | R2 | Engine release (60 Hz seen by a human, second machine, skirmish savegame) | open |
| – | – | R3 | Changes release | open |

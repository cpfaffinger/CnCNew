# Phase briefs, ordered by stage and milestone

As of: 10 October 2026, state `2777a90`. The plan is [`../plan/PROGRAMME.md`](../plan/PROGRAMME.md); the format of a brief
is [`../plan/BRIEF-FORMAT.md`](../plan/BRIEF-FORMAT.md). Every brief carries a header block with stage, milestone and
status; the worked-off briefs keep their text as documents of their time (their numbers are outdated – the yardstick is
`baselines/reference-manifest.json`). Phases 4 to 42 are worked off (31 ran before 24, D-12), **Phase 43 is next**. The
briefs of Phases 1 to 3 were never written as files (their scope and acceptance are in `docs/IMPLEMENTATION-PLAN.md`
§1–§3).

**The next brief to work on:** [HANDOFF-PHASE43.md](HANDOFF-PHASE43.md) – an attack on a place (F-64): force-fire on the ground, `FIRE_WEAPON` buttons at a place, the riders and slaves, `FireWeaponPower`, the `Attack` nugget; written at the end of Phase 42 (the shock wave, a deploying and a flying guard, clearing a garrison, [HANDOFF-PHASE42.md](HANDOFF-PHASE42.md), done by machine on 29 October 2026).
profiles and strengths of the built-in AI, every CRC-gated fix decided, [HANDOFF-PHASE33.md](HANDOFF-PHASE33.md), done on 15 October 2026; before it Phase 32 (the patch layer plays, every CRC-gated fix reviewed, [HANDOFF-PHASE32.md](HANDOFF-PHASE32.md), done on 15 October 2026; before it Phase 30 (patch integration, quality, shippable completion by machine, [HANDOFF-PHASE30.md](HANDOFF-PHASE30.md), done on 14 October 2026; before it Phase 29 (multiplayer in the client: the menus' way to a server, the in-match indicators, the latency
measurement, checked by frameworks in place of humans, [HANDOFF-PHASE29.md](HANDOFF-PHASE29.md), done on one machine on 14 October 2026; before it Phase 28 (server operation and security: the sealed transport, limits, fuzzing, the packaged server, capacity and endurance, [HANDOFF-PHASE28.md](HANDOFF-PHASE28.md), done on one machine on 13 October 2026; before it Phase 27, desync diagnosis, rejoin, watchers, server replay, the window client joining a server, [HANDOFF-PHASE27.md](HANDOFF-PHASE27.md), done on one machine on 12 October
2026; before it Phase 26, dedicated server, UDP, clock synchronisation, eight client processes, [HANDOFF-PHASE26.md](HANDOFF-PHASE26.md), done on one machine on 12 October 2026; before it Phase 25, protocol, room and relay in the process, [HANDOFF-PHASE25.md](HANDOFF-PHASE25.md), done on 11 October 2026; before it Phase 24, the session core, [HANDOFF-PHASE24.md](HANDOFF-PHASE24.md), done on 11 October 2026; before it Phase 31, the fidelity package A of Stage 1, [HANDOFF-PHASE31.md](HANDOFF-PHASE31.md), done on 10 October 2026). Phase numbers are identities, not an order: 31 ran before 24 by
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
| **39** | [HANDOFF-PHASE39.md](HANDOFF-PHASE39.md) | **S1-M11** | Fidelity package B: `QuantityModifier`, the unit at the open gate, `SupplyMachine` over a command, a magazine's second shot, `<This Team>` in the mission VM, upgrades with their build time (F-06 to F-10, F-43) | **done** by machine (27 October 2026): `b42a210`/`c5fc4aa`, `08def33`/`60ca6d5`, `6968ff4`, `83510c7`/`e62d747`, `400ea98`/`f678994`, `81918dc`/`c7f3def`, `8f644ff`/`6614543`, `96a3508`, `9671fb0`/`f84e33c`; F-06 is the original's behaviour |
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
| **24** | [HANDOFF-PHASE24.md](HANDOFF-PHASE24.md) | **S2-M7** | Session core: AI in the state, skirmish savegame, match description, command path, tick source | done (11 October 2026): `8acde11`, `6ba4197`, `177c952`, manifest `63ac97c`, `e128cc3`; `docs/SIMULATION-CORE.md` §45 |
| – | – | S2-M8 | Engine follow-ups (tick maximum, hash constructions, allocation, thread, formats, documentation) | open backlog; briefs when scheduled |

## Stage 3 – Changes (`../plan/STAGE-3-CHANGES.md`)

No brief exists yet; the milestones are defined in the stage document and get a brief when scheduled. Decided (D-1):
S3-M2 to S3-M5 may run as parallel packages from Phase 24 on; S3-M1, S3-M6 and S3-M7 come after Phase 27. The next free
phase number is 32.

| Phase | Brief | Milestone | Topic | Status |
|---|---|---|---|---|
| – | – | S3-M1 | AI kinds, difficulty and tactics (built-in `SkirmishAi` exists and is the default) | open |
| – | `docs/SPECTATOR-MODE.md` | S3-M2 | Spectator and observer (built in Phase 23) | built – not accepted |
| 35 | [HANDOFF-PHASE35.md](HANDOFF-PHASE35.md) | S3-M3 | Replay browser: recording, list, playback | done by machine (16 October 2026): `caa7941`, `e3d5184`, `022014c`, `236ff86`, `1d45cfd`, `4e081b2`, `8f6dc49`, `55274ba` – `docs/REPLAY-BROWSER.md`; the attached effects on the owner's finding; the side items not done |
| 36 | [HANDOFF-PHASE36.md](HANDOFF-PHASE36.md) | S3-M4 | Match analysis with graphics, from a replay or a running match | done by machine (17 October 2026): `f0371ee`, `31b03c2`, `29468aa`, `b289185`, `0f4388d`, `c2dcc4f` – `docs/MATCH-ANALYSIS.md`; the score screen's counters fixed; the tick maximum under the profile still above 8 ms |
| 37 | [HANDOFF-PHASE37.md](HANDOFF-PHASE37.md) | S3-M2 | Spectator and observer: free camera, views, panels, event ticker, a human match from its replay | done by machine (18 October 2026): `4798de8`, `da2168b`, `42bdec6`, `df5beb6`, `7574bd4` – `docs/SPECTATOR-MODE.md` §14; F-42 closed, F-43 and D-34 new; no human has watched |
| 38 | [HANDOFF-PHASE38.md](HANDOFF-PHASE38.md) | S3-M5 | Modern user interface: the design decision, a skin beside the original's, an options screen | done by machine (18 October 2026): `8464d5c`, `2ee9a2d`, `7e6458c`, `5ce44a2`, `7f32642`, `c50b725`, `62b68c3`, `89490f5` – `docs/UI-MODERN.md`; both skins pinned, a scripted person, the readability check; D-34 prepared as a proposal; no human has used the modern skin |
| – | – | S3-M5 | Modern user interface | open |
| – | [HANDOFF-PHASE30.md](HANDOFF-PHASE30.md) items 3 and 4 | S3-M7 | Bug-fix and balance layers (GeneralsGameCode, GeneralsGamePatch) | begun in Phase 30 (rule profile, catalogue, layer import) |
| 32 | [HANDOFF-PHASE32.md](HANDOFF-PHASE32.md) | S3-M7 | The patch layer playable, the review of the logic fixes | done by machine (15 October 2026): `477c15e`, `a3834a8`, `34fc5e8`, `1ff635d`, `417e51a` |
| 33 | [HANDOFF-PHASE33.md](HANDOFF-PHASE33.md) | S3-M1 (+ S3-M7 rest) | AI tactics profiles and difficulty levels, a stronger default AI | done by machine (15 October 2026): `1259dee`, `55ef111`, `beea7e2`, `e386095`, `11e61ca`, `131f590`, `80fb676`, `dbdb252`, `6c60147` |
| 34 | [HANDOFF-PHASE34.md](HANDOFF-PHASE34.md) | S3-M6 (+ F-34, F-38, F-39) | A shared group path and a cell budget per tick as a movement profile | done by machine (16 October 2026), the tick maximum target missed: `2d2c602`, `34a0301`, `61f293d`, `c2e48e2`, `3d0d276`, `f3c4009`, `d58d9d0`, `3b6fa80`, `a13d5de`, `3bd8ceb`, `a63b52d`, `ed12ad0`, `e6232ce` |
| **40** | [HANDOFF-PHASE40.md](HANDOFF-PHASE40.md) | **S3-M7** | A second source of fixes: the changes of Zero Hour Reforged reviewed against the original and this port (`docs/REFORGED-FIXES.md`, R-01 to R-38) | **done** by machine (28 October 2026): the disable model, F-16, F-48 to F-53, F-56, F-57, F-59; §54 |
| **41** | [HANDOFF-PHASE41.md](HANDOFF-PHASE41.md) | **S1/S3** | The attack's line of sight (F-55), firing from inside (F-60), the garrison's owner (F-61), the stealth garrison (F-54), guarding (F-58) | **done** by machine (29 October 2026): F-54, F-55, F-61, F-62 done, F-60 and F-58 in large part, the rule fixes 20 to 25, R-03, R-24, R-27, R-34, R-35 decided; §55 |
| **42** | [HANDOFF-PHASE42.md](HANDOFF-PHASE42.md) | **S1-M11** | Fidelity package D: the weapon's shock wave (F-63), a deploying and a flying guard (F-58 step 7), clearing a garrison and the rest of F-60, F-54's open ends | **done** by machine (29 October 2026): §56 |
| **43** | [HANDOFF-PHASE43.md](HANDOFF-PHASE43.md) | **S1-M11** | Fidelity package E: an attack on a place (F-64) | **done** by machine (29 October 2026): §57 |
| **44** | [HANDOFF-PHASE44.md](HANDOFF-PHASE44.md) | **S1-M11** | Fidelity package F: slow deaths (F-37), the slaves of a spawner (F-65), thrown objects (F-66), debris (F-36), the bored dozer (F-67) | **done** by machine (29 October 2026): §58 |
| **45** | [HANDOFF-PHASE45.md](HANDOFF-PHASE45.md) | **S1-M11** | Fidelity package G: the worker's two brains (F-68), mine clearing (F-69), five rate differences (F-71 to F-75), F-66's rest | **done** by machine (30 October 2026): §59 |
| **46** | [HANDOFF-PHASE46.md](HANDOFF-PHASE46.md) | **S1-M11** | Fidelity package H: team orders as group orders (F-35), a turret's hold (F-76) | **done** by machine (30 October 2026): §60 |
| **47** | [HANDOFF-PHASE47.md](HANDOFF-PHASE47.md) | **S1-M11** | Fidelity package I: the creation list's source (F-77), the waypoint follower (F-78) | **done** by machine (30 October 2026): §61 |
| **48** | [HANDOFF-PHASE48.md](HANDOFF-PHASE48.md) | **S1-M11** | Fidelity package J: the dock that is gone (F-79), the default matches compared | **done** by machine (30 October 2026): §62 |
| **49** | [HANDOFF-PHASE49.md](HANDOFF-PHASE49.md) | **S1-M11** | Fidelity package K: the exit path's second leg (F-80), the source's velocity (F-66), a disabled module's wake (F-81), the sight of a victim (F-82) | **done** by machine (30 October 2026): §63 |
| **50** | [HANDOFF-PHASE50.md](HANDOFF-PHASE50.md) | **S1-M11** | Fidelity package L: the building site of the original (F-18), the special abilities (F-83), idle allies make way (F-84), a dozer's look (F-85), a walker's goal (F-86) | **done** by machine (31 October 2026): `docs/SIMULATION-CORE.md` §64 |
| **51** | [HANDOFF-PHASE51.md](HANDOFF-PHASE51.md) | **S1-M11** | Fidelity package M: the approximations of target acquisition (F-17), the dying and the pad (F-87) | **done** by machine (31 October 2026): `docs/SIMULATION-CORE.md` §65 |
| 52 | [HANDOFF-PHASE52.md](HANDOFF-PHASE52.md) | Stage 3 | One product: the patch layer, the fixes and the movement profile are the default and the yardstick (D-3, D-4, D-32, D-33) | done |
| **53** | [HANDOFF-PHASE53.md](HANDOFF-PHASE53.md) | **Stages 3, 4** | The decisions that are a default: D-8, D-19, D-21, D-22, D-23 (interval), D-25, D-26, D-30, D-34 | **next** |

## Stage 4 – Multiplayer (`../plan/STAGE-4-MULTIPLAYER.md`)

| Phase | Brief | Milestone | Topic | Status |
|---|---|---|---|---|
| **25** | [HANDOFF-PHASE25.md](HANDOFF-PHASE25.md) | **S4-M1** | Protocol, room, relay in the process; local skirmish over the network path | done (11 October 2026): `84a9459`, manifest `e0d6e01`, `64d632b`, `5f2406a`, `0b93f31`; `docs/NETWORK-PROTOCOL.md` |
| **26** | [HANDOFF-PHASE26.md](HANDOFF-PHASE26.md) | **S4-M2** | Dedicated server, UDP, clock synchronisation, eight clients | done on one machine (12 October 2026): `3135af4`, `703c7f2`, manifest `4cbf535`, `41c5272`, `66428bf`, `4c2f372`, `b5443a6`, `02be419`, `1844afa`; `docs/NETWORK-PROTOCOL.md` §12 to §17, `docs/DEDICATED-SERVER.md`; the second machine and the LAN are open |
| **27** | [HANDOFF-PHASE27.md](HANDOFF-PHASE27.md) | **S4-M3** | Desync diagnosis, rejoin, spectators, server replay | done on one machine (12 October 2026): `a8aa667`, `9875e18`, `2e74381`, `9d680fc`, `c0440ee`, `d42886f`, `db5a236`, `90b20ec`, `6ea31ae`, `8f6f94f`; `docs/NETWORK-PROTOCOL.md` §18 and §19, `docs/NETWORK-DIAGNOSTICS.md`; votes not built, the second machine open |
| **28** | [HANDOFF-PHASE28.md](HANDOFF-PHASE28.md) | **S4-M4** | Server operation and security | done on one machine (13 October 2026): `6cc6501`, `10e6ab0`, `98796a6`, `26f20bd`, `e76e19b`, `43c4a0e`, `6e2f951`, `c1946aa`; `docs/NETWORK-PROTOCOL.md` §20 and §21, `docs/DEDICATED-SERVER.md` §9 to §13 |
| **29** | [HANDOFF-PHASE29.md](HANDOFF-PHASE29.md) | **S4-M5** | Multiplayer in the client, latency perception, acceptance | done on one machine, without a human (14 October 2026): `36b4d8f`, `be78648`, `48207f5`, `59797fc`, `9c653af`, `4d138a4`, `a143952`, `a3cfef6` – results in `docs/MULTIPLAYER-FINDINGS.md` |
| – | – | S4-M6 | Co-simulating server as an option (hybrid, D-11) | open; after Phase 27 |

## Release gates

| Phase | Brief | Gate | Topic | Status |
|---|---|---|---|---|
| 30 | [HANDOFF-PHASE30.md](HANDOFF-PHASE30.md) | R1 (items 1, 2, 5–8 for the single player), R4 (package incl. server) | Patch integration, quality, shippable completion | done by machine on one machine (14 October 2026): `c00a48f`, `5a62f8c`, `80ec76c`, `0f55a3e`, `391d391`, `a538e40`, `f380966`, `2f8fb99`, `dfcd72e` – results in `docs/ACCEPTANCE-MACHINE.md`; the acceptance by humans is not met |
| – | – | R2 | Engine release (60 Hz seen by a human, second machine, skirmish savegame) | open |
| – | – | R3 | Changes release | open |

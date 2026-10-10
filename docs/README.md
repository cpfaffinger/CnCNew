# Documentation – overview

All documents are English, as are identifiers and commands (documentation written before 9 October 2026 was German and
has been translated; German terms that survive in parentheses are the original wording). Subject documents lie flat in `docs/`, the phase briefs
in `docs/handoffs/`, the package reports of Phase 23 in `docs/rate-invariance/`. Numbers in the documents are measurements
of their state (date and commit are given with them) – the yardstick for results is `baselines/reference-manifest.json`,
not a hash in a text. The working rules are in [`../CLAUDE.md`](../CLAUDE.md), the licence situation in
[`../NOTICE.md`](../NOTICE.md).

On paths: the developer content is called `game/content` today (`scripts/dev-content.ps1`). The briefs of the completed
phases (`handoffs/HANDOFF-PHASE4.md` to `-23.md`), the acceptance block in `CONTENT-PIPELINE.md` and the phase paragraphs in
`CLAUDE.md` still name, as documents of their time, the former location `.local/phase2-import-a`.

## Overview and plan

| Document | Content |
|---|---|
| [plan/PROGRAMME.md](plan/PROGRAMME.md) | **The plan:** four stages (port, engine, changes, multiplayer), milestone overview, what was built in another stage, execution order, release gates, decision register |
| [plan/STAGE-1-PORT.md](plan/STAGE-1-PORT.md) | Stage 1 – Port: milestones S1-M1 to S1-M14, the fidelity debt register, the human acceptance backlog |
| [plan/STAGE-2-ENGINE.md](plan/STAGE-2-ENGINE.md) | Stage 2 – Engine: milestones S2-M1 to S2-M8 (session core is current), engine backlog |
| [plan/STAGE-3-CHANGES.md](plan/STAGE-3-CHANGES.md) | Stage 3 – Changes: AI tactics, spectator, replay browser, analysis, modern UI, pathfinding, patch layers – each as an option |
| [plan/STAGE-4-MULTIPLAYER.md](plan/STAGE-4-MULTIPLAYER.md) | Stage 4 – Multiplayer: the owner's requirements against the architecture, prerequisites, milestones S4-M1 to S4-M5 |
| [plan/BRIEF-FORMAT.md](plan/BRIEF-FORMAT.md) | The format of a phase brief: header block, sections, status vocabulary, rules |
| [handoffs/README.md](handoffs/README.md) | All phase briefs, ordered by stage and milestone, with status |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Target architecture: layers, dependency rules, projects |
| [ROADMAP.md](ROADMAP.md) | Roadmap of the phases 0 to 30 – superseded as the plan on 10 October 2026, kept as the record of the phase states |
| [IMPLEMENTATION-PLAN.md](IMPLEMENTATION-PLAN.md) | Implementation plan of the first scope (steps 1 to 11) – superseded as the plan, kept as the record of acceptance statements and known limits |
| [ENGINE-EVALUATION.md](ENGINE-EVALUATION.md) | Engine evaluation (decision for Stride) |
| [TESTING.md](TESTING.md) | Test and acceptance strategy: four tiers, which change asks for which checks, when the full run is owed (`scripts/test.ps1`) |

## Content pipeline and installation

| Document | Content |
|---|---|
| [../BUILDING.md](../BUILDING.md) | Build requirements (SDK, PowerShell 7, Python, NuGet source, game), install commands, setting up a developer machine |
| [INSTALLATION.md](INSTALLATION.md) | Patcher: installation from your own Steam copy, uninstallation, developer content |
| [CONTENT-PIPELINE.md](CONTENT-PIPELINE.md) | Runtime content contract (schema 2) and format matrix |
| [W3D-MATERIALS.md](W3D-MATERIALS.md) | W3D material contract: passes, shader states, mappers |
| [W3D-SKELETONS.md](W3D-SKELETONS.md) | W3D hierarchies, animations and models |
| [AUDIO.md](AUDIO.md) | Audio: decoders, catalogue, selection and software mixer |

## Data and definitions

| Document | Content |
|---|---|
| [TYPED-DEFINITIONS.md](TYPED-DEFINITIONS.md) | Typed definition contract, include/overlay rules, snapshot format |
| [PHASE3-COMPATIBILITY.md](PHASE3-COMPATIBILITY.md) | Compatibility list for `baselines/compatibility.json` |
| [MAP-SCRIPTS.md](MAP-SCRIPTS.md) | World Builder scripts: decoding |
| [UI-WND.md](UI-WND.md) | Window layouts (WND), scaling, runtime, draw list |
| [UI-TEXT-AND-SETTINGS.md](UI-TEXT-AND-SETTINGS.md) | UI texts, localisation, mapped images, settings |

## Simulation

| Document | Content |
|---|---|
| [SIMULATION-CORE.md](SIMULATION-CORE.md) | The deterministic simulation core: tick phases, numerics, RNG, snapshot, replay, modules, terrain, navigation, vision, construction, skirmish AI, rate invariance |
| [GAME-RULES.md](GAME-RULES.md) | Game rules: economy, upgrades, combat, movement, production, victory |
| [SIM-FIRING-TRACKER.md](SIM-FIRING-TRACKER.md) | Firing stages, pre-attack delay, damaged locomotors |
| [SIM-JET-FLIGHT.md](SIM-JET-FLIGHT.md) | Fixed-wing aircraft and airfield geometry |
| [SIM-STAGED-CAPTURE.md](SIM-STAGED-CAPTURE.md) | Staged building capture |
| [SIM-TURRET-AIM.md](SIM-TURRET-AIM.md) | Turrets: aiming, attack state, idle scanning |
| [SKIRMISH-AI-ORIGINAL.md](SKIRMISH-AI-ORIGINAL.md) | The skirmish AI of the original as a selectable session |
| [AI-OBSERVED-2026-10-08.md](AI-OBSERVED-2026-10-08.md) | First observation of the built-in AI by a human: findings, corrections, measurement |
| [DEFECT-LOST-WAKE.md](DEFECT-LOST-WAKE.md) | 30 Hz defect: the lost wake request of the slow death |

## Scripts and campaign

| Document | Content |
|---|---|
| [SCRIPT-ENGINE.md](SCRIPT-ENGINE.md) | Script VM and mission session, coverage, objective driver |
| [CAMPAIGN-AND-SAVEGAMES.md](CAMPAIGN-AND-SAVEGAMES.md) | Campaigns, mission flow, savegames |

## Presentation, client, UI and sound

| Document | Content |
|---|---|
| [PRESENTATION-WORLD.md](PRESENTATION-WORLD.md) | World presentation: terrain, camera, fog of war, minimap |
| [PRESENTATION-OBJECTS.md](PRESENTATION-OBJECTS.md) | Object rendering: model states, draw specifications, frame projector |
| [PRESENTATION-FX.md](PRESENTATION-FX.md) | Effects: FXList, particles, tracers, light, decals |
| [PRESENTATION-INPUT.md](PRESENTATION-INPUT.md) | Input, selection, command modes, hotkeys |
| [MODEL-CONDITIONS-UNITS.md](MODEL-CONDITIONS-UNITS.md) | Model states of units, weapons, movement, death |
| [MODEL-CONDITIONS-BUILDINGS.md](MODEL-CONDITIONS-BUILDINGS.md) | Model conditions of the buildings, docks, doors |
| [CLIENT-TERRAIN.md](CLIENT-TERRAIN.md) | Stride client: terrain layer |
| [CLIENT-ROADS.md](CLIENT-ROADS.md) | Stride client: roads |
| [CLIENT-OBJECTS.md](CLIENT-OBJECTS.md) | Stride client: object layer, visual checklist |
| [CLIENT-FX.md](CLIENT-FX.md) | Stride client: effect layer |
| [CLIENT-UI-RENDERING.md](CLIENT-UI-RENDERING.md) | Stride client: 2D renderer of the UI draw list |
| [GAME-SHELL.md](GAME-SHELL.md) | Game shell: boot, menus, skirmish setup, result |
| [GAME-HUD.md](GAME-HUD.md) | HUD and control surface in the game |
| [GAME-AUDIO.md](GAME-AUDIO.md) | AudioDirector, EVA, music, device output |
| [TEXT-AUDIT.md](TEXT-AUDIT.md) | Text rendering: font path, sentence wrapping, findings |
| [UI-VANILLA-FIT.md](UI-VANILLA-FIT.md) | UI against the unmodified layouts of the Steam master |
| [SPECTATOR-MODE.md](SPECTATOR-MODE.md) | Spectator mode: AI against AI in a window |

## Performance and determinism

| Document | Content |
|---|---|
| [DETERMINISM.md](DETERMINISM.md) | Determinism across machine boundaries: matrix, instructions for a second machine, rules |
| [DETERMINISM-FINDINGS.md](DETERMINISM-FINDINGS.md) | Determinism findings D1–D9 |
| [PERFORMANCE.md](PERFORMANCE.md) | Performance profile of the simulation, targets, measurement series |
| [PERF-MOVEMENT.md](PERF-MOVEMENT.md) | Performance: movement and pathfinding |
| [PERF-VISION.md](PERF-VISION.md) | Performance: vision and vision hash |
| [PERF-HASH.md](PERF-HASH.md) | Performance: state hash |
| [PERF-MISSIONS.md](PERF-MISSIONS.md) | Performance of the campaign missions |
| [PERF-GAME-LAYER.md](PERF-GAME-LAYER.md) | Performance of the game layer |

## Tick rate and fixed point

| Document | Content |
|---|---|
| [TICKRATE-AUDIT.md](TICKRATE-AUDIT.md) | Checked inventory of the tick-rate coupling |
| [TICKRATE-MIGRATION.md](TICKRATE-MIGRATION.md) | Tick rate as a value of the world: migration (Phases 20, 21, 23), state and remaining lists in §15.7 |
| [FIXED-POINT-Q39-24.md](FIXED-POINT-Q39-24.md) | Fixed point from Q47.16 to Q39.24 |
| [rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md](rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md) | Phase 23: consolidation of the seven packages, the frame convention (§1.3) |
| [rate-invariance/RATE-INVARIANCE-GROUND.md](rate-invariance/RATE-INVARIANCE-GROUND.md) | Package ground movement |
| [rate-invariance/RATE-INVARIANCE-FLIGHT.md](rate-invariance/RATE-INVARIANCE-FLIGHT.md) | Package flight |
| [rate-invariance/RATE-INVARIANCE-PROJECTILES.md](rate-invariance/RATE-INVARIANCE-PROJECTILES.md) | Package projectiles |
| [rate-invariance/RATE-INVARIANCE-PHYSICS.md](rate-invariance/RATE-INVARIANCE-PHYSICS.md) | Package physics: factors, recursions, healing slices |
| [rate-invariance/RATE-INVARIANCE-TIMING.md](rate-invariance/RATE-INVARIANCE-TIMING.md) | Package timing: "one tick where the original means one frame" |
| [rate-invariance/RATE-INVARIANCE-TURRETS.md](rate-invariance/RATE-INVARIANCE-TURRETS.md) | Package turrets, target acquisition, firing rhythm |
| [rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md](rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md) | Round 3: flight |
| [rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md](rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md) | Round 3: observers |
| [rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md](rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md) | Round 4: the first frame and whole matches |
| [rate-invariance/RATE-SWITCH-OUTER-TESTS.md](rate-invariance/RATE-SWITCH-OUTER-TESTS.md) | Tick-rate switch of the outer test programs |

## Multiplayer programme

| Document | Content |
|---|---|
| [MULTIPLAYER-PLAN.md](MULTIPLAYER-PLAN.md) | Programme tick rate, fixed point, multiplayer (Phases 18–29): decisions, target architecture, bottlenecks, open decisions of the project owner |

Of this, the preparatory work is built (Phases 18 to 23); the multiplayer part itself (Phases 24 to 29) is planned.

## Demos and findings

| Document | Content |
|---|---|
| [DEMO-FINDINGS.md](DEMO-FINDINGS.md) | Findings of the tech demos by a human, visual checklists |
| [DEMO-SCENARIOS.md](DEMO-SCENARIOS.md) | Staged demos for auditing |
| [ACCEPTANCE.md](ACCEPTANCE.md) | Acceptance by a human, driven by `pwsh scripts/acceptance.ps1` |

## Phase briefs (`handoffs/`)

Every brief is written to be self-contained and carries a header block with stage, milestone and status
([plan/BRIEF-FORMAT.md](plan/BRIEF-FORMAT.md)). The index ordered by stage and milestone is
[handoffs/README.md](handoffs/README.md). The briefs of Phases 4 to 23 are worked off and remain as documents of their
time; current is Phase 31 (fidelity package A of Stage 1, before Phase 24 by decision D-12), then Phase 24; the briefs 25
to 30 are prepared and are continued at the end of the respective previous phase in their section "Starting point"; the
milestones of Stage 3 have no brief yet (`plan/STAGE-3-CHANGES.md`).

| Brief | Topic | State |
|---|---|---|
| [HANDOFF-PHASE4.md](handoffs/HANDOFF-PHASE4.md) | Deterministic simulation core | worked off |
| [HANDOFF-PHASE5.md](handoffs/HANDOFF-PHASE5.md) | Game rules, combat, economy, production | worked off |
| [HANDOFF-PHASE6.md](handoffs/HANDOFF-PHASE6.md) | Rest of Phase 5, then world, terrain, movement, vision, skirmish AI | worked off |
| [HANDOFF-PHASE7.md](handoffs/HANDOFF-PHASE7.md) | Phase 6: world, terrain, movement, vision, skirmish AI | worked off |
| [HANDOFF-PHASE8.md](handoffs/HANDOFF-PHASE8.md) | World presentation and open simulation debts | worked off |
| [HANDOFF-PHASE9.md](handoffs/HANDOFF-PHASE9.md) | Skirmish, missions, campaigns, savegames | worked off |
| [HANDOFF-PHASE10.md](handoffs/HANDOFF-PHASE10.md) | Completing Phase 9: skirmish, shell, playability from start to end | worked off |
| [HANDOFF-PHASE11.md](handoffs/HANDOFF-PHASE11.md) | Remaining script families, campaign shell, mission HUD | worked off |
| [HANDOFF-PHASE12.md](handoffs/HANDOFF-PHASE12.md) | Objective driver, script families, client wiring | worked off |
| [HANDOFF-PHASE13.md](handoffs/HANDOFF-PHASE13.md) | Reinforcement with transport, open victory missions, VM semantics | worked off |
| [HANDOFF-PHASE14.md](handoffs/HANDOFF-PHASE14.md) | Formations, weapon slots, path flight, blocker modules | worked off |
| [HANDOFF-PHASE15.md](handoffs/HANDOFF-PHASE15.md) | Tech-demo maturity I: world rendering, security of the dependencies | worked off, visual acceptance open |
| [HANDOFF-PHASE16.md](handoffs/HANDOFF-PHASE16.md) | Tech-demo maturity II: sound, cursor, waypoints, text, mission in the client | begun, not accepted |
| [HANDOFF-PHASE17.md](handoffs/HANDOFF-PHASE17.md) | Playability by humans | ended without its acceptance goal; rest at the end of the document |
| [HANDOFF-PHASE18.md](handoffs/HANDOFF-PHASE18.md) | Measurement basis: reference manifest, determinism, performance profile | worked off |
| [HANDOFF-PHASE19.md](handoffs/HANDOFF-PHASE19.md) | Performance: lowering tick costs without changing results | worked off |
| [HANDOFF-PHASE20.md](handoffs/HANDOFF-PHASE20.md) | Tick rate as a parameter: simulation | worked off |
| [HANDOFF-PHASE21.md](handoffs/HANDOFF-PHASE21.md) | Tick rate as a parameter: presentation, UI, sound, tools, tests | worked off |
| [HANDOFF-PHASE22.md](handoffs/HANDOFF-PHASE22.md) | Fixed point Q39.24 | worked off |
| [HANDOFF-PHASE23.md](handoffs/HANDOFF-PHASE23.md) | Time-step invariance, 60 Hz as the default | worked off, with named limits |
| [HANDOFF-PHASE24.md](handoffs/HANDOFF-PHASE24.md) | Session core: AI in the state, skirmish savegame, command path, tick source | **current** |
| [HANDOFF-PHASE25.md](handoffs/HANDOFF-PHASE25.md) | Protocol, room, relay in the process | planned |
| [HANDOFF-PHASE26.md](handoffs/HANDOFF-PHASE26.md) | Dedicated server, UDP, time synchronisation | planned |
| [HANDOFF-PHASE27.md](handoffs/HANDOFF-PHASE27.md) | Desync diagnosis, rejoining, spectators, server replay | planned |
| [HANDOFF-PHASE28.md](handoffs/HANDOFF-PHASE28.md) | Server operation and security | planned |
| [HANDOFF-PHASE29.md](handoffs/HANDOFF-PHASE29.md) | Multiplayer in the client, acceptance with humans | planned |
| [HANDOFF-PHASE30.md](handoffs/HANDOFF-PHASE30.md) | Patch integration, quality, shippable completion | planned (postponed) |

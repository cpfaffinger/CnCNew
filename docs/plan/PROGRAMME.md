# Programme plan: the four stages

As of: 10 October 2026, state `2777a90` (decision of the project owner of 10 October 2026 to order the whole programme by
four stages). This document is the top of the plan. It replaces `docs/ROADMAP.md` and `docs/IMPLEMENTATION-PLAN.md` as
the plan; both stay as the record of the acceptance statements and measured numbers of their time. The technical frame of
the multiplayer and the register of bottlenecks stay in `docs/MULTIPLAYER-PLAN.md`. Numbers in this document are the
measurements of their state and are re-measured before any statement is built on them; the yardstick for results is
`baselines/reference-manifest.json`, not a number in a text.

The four stage documents carry the milestones, deliverables and what is already done:

| Stage | Document | One sentence |
|---|---|---|
| 1 – Port | [STAGE-1-PORT.md](STAGE-1-PORT.md) | The original, as it is, in a new engine: single player complete, no change of behaviour, with the tooling that turns a Steam installation into content |
| 2 – Engine | [STAGE-2-ENGINE.md](STAGE-2-ENGINE.md) | The engine grows beyond the original (60/120 Hz, fixed point, structure, performance, determinism) while the game stays, by its data, the same game |
| 3 – Changes | [STAGE-3-CHANGES.md](STAGE-3-CHANGES.md) | New things: AI kinds and tactics, spectator, replay browser, analysis, modern UI, new pathfinding, bug-fix and balance layers – every one of them selectable |
| 4 – Multiplayer | [STAGE-4-MULTIPLAYER.md](STAGE-4-MULTIPLAYER.md) | Dedicated server, lockstep through a relay, up to eight players with AI slots, no one waits for the slowest |

The format of the briefs and the status vocabulary are in [BRIEF-FORMAT.md](BRIEF-FORMAT.md); the briefs themselves,
ordered by stage and milestone, in [../handoffs/README.md](../handoffs/README.md).

## 1. Terms

- **Stage** (1 to 4): a layer of the programme by *kind of work*. Stages are not time slices: work of several stages has
  run and will run interleaved; the stage says what a piece of work must respect and against what it is measured.
- **Milestone** (`S<stage>-M<n>`): a self-contained deliverable of a stage with its own acceptance. A milestone is built in
  one or more **phases**.
- **Phase** (`Phase N`, brief `docs/handoffs/HANDOFF-PHASE<N>.md`): one unit of work with a brief, a starting state, a
  final report and an N+1 brief. The numbering of the phases is historical and continues (the next free number is 32).
  Phases 4 to 24 and 31 are worked off (31 ran before 24 by decision D-12 – phase numbers are identities, not
  an order), 26 follows, 27 to 30 are planned.
- **Release gate** (`R1` to `R4`): the end of a stage as a human sees it: a package, an acceptance, documentation for
  humans. A stage is finished when its gate is passed, not when its last milestone is built.
- **Status words** (the only ones used in the plan): **done** (built and measured, and accepted where only a human can
  accept) · **built – not accepted** (built and measured headless; what only a human can judge is outstanding) ·
  **in progress** · **open** (defined, not begun) · **decision** (waits for the project owner) · **limit** (named,
  not planned to change).

## 2. The four stages

### Stage 1 – Port

**Goal:** the original game, ported into a new engine, with its full single-player function (campaign, missions,
skirmish) and *no* intended or unintended change of behaviour; with the tooling that turns the user's own Steam
installation into runtime content and checks it. The tools that the port needed (`SimTool`, `IniTool`, `MapTool`,
`CampaignTool`, `AssetTool` and the rest) belong to this stage.

**What belongs here:** every module, script action, rule, screen, sound and effect of the original; every fidelity defect
(a place where the port behaves differently from the original); the installer; the acceptance by a human of look, sound
and playability.

**Yardstick:** the original – its source code (`references/`, read, not copied), its data, and the eye and hand of a
human against the running original. Fidelity cannot be hashed, so the stage is measured by coverage (modules, objects,
script types, screens, texts), by staged scenes with a known original behaviour, by the acceptance catalogue
(`docs/ACCEPTANCE.md`, 85 items) and by the **fidelity debt register** in the stage document.

**Rule:** a change in this stage makes the port *more like the original*. A change that makes the game *different from the
original on purpose* is Stage 3, however small, and is built as an option there.

### Stage 2 – Engine

**Goal:** the engine evolves beyond the original – tick rate as a value of the world with 60 Hz as the product rate and
120 Hz supported, finer fixed point, performance, determinism across machines, a session core that a network can drive –
while the game stays, by its assets and data, *the same game*: times from the data lie on the 30 Hz grid of the original,
balance does not move, and a match at 60 Hz is the match of the original.

**What belongs here:** structure and patterns of the code, numerics, time, hashing, snapshot and replay formats, the test
and measurement apparatus, the session core (`MatchDescription`, command path, tick source, AI state in the session).

**Yardstick:** the reference manifest. A Stage 2 phase is result-identical (the manifest does not move) or it changes the
manifest once, for one named cause, in a commit of its own (`docs/MULTIPLAYER-PLAN.md` §6). Between rates the
rate-equivalence suite (4,388 game quantities, tolerances fixed before the first run) and the distribution comparison
decide.

**Rule:** no rule of the game changes here. Where an engine change forces a behaviour change (a lost wake request, a
factory exit), it is a fidelity fix (Stage 1) or an option (Stage 3) and is booked there, even when it is built inside an
engine phase.

### Stage 3 – Changes

**Goal:** what the original did not have: kinds of AI and their tactics (rush, bunker, balanced, eco …), the spectator
mode, a replay browser, analysis of matches with graphics, a modern user interface, new movement and pathfinding, and the
community's bug-fix and balance patches as layers.

**Yardstick and rule:** every change is **selectable** – a profile, an option, a layer – and the "original" profile keeps
the reference manifest unchanged. A change that touches the simulation gets its own fingerprint, which becomes part of the
match description (Stage 4: only machines with the same profile play together). Balance effects are measured with the
distribution comparison (`SimTool ai-survey`, `rate-distribution`), never asserted.

### Stage 4 – Multiplayer

**Goal:** skirmishes with up to eight players (humans and AI slots mixed) through a dedicated server, as lockstep through
a relay that neither simulates nor loads game data; a slow player does not hold up the others; the local skirmish runs
over the same path with an embedded server.

**Yardstick:** hash-identical clients tick by tick under a disturbed network, a measured latency matrix, a disconnected
client that rejoins hash-identically, and the acceptance by humans in a LAN and over the internet.

The decided architecture is in `docs/MULTIPLAYER-PLAN.md` §2: what travels is commands, not state; every client computes
the full world; the server orders and distributes. "The client gets its data from the server" is, in this architecture,
the tick bundle of commands – not a state stream (D-11, decided: the assessment is in `STAGE-4-MULTIPLAYER.md` §1a). The
**hybrid** is a co-simulating server as an option (S4-M6): state on the server for rejoin, arbitration and spectating,
never on the match path.

### Which stage does a piece of work belong to?

1. Does it make the port behave *more* like the original (a missing module, a wrong formula, an unseen screen)? → **Stage 1.**
2. Does it change the engine without changing what the game does by its data (rate, numerics, hash, snapshot, structure,
   speed, determinism, session core)? → **Stage 2.** Proof: the manifest does not move, or moves once for one cause.
3. Does it change what the game does, on purpose, beyond the original? → **Stage 3**, as an option with a fingerprint.
4. Does it make several machines play one match? → **Stage 4.**
5. Is it a package, an acceptance with humans, documentation for humans? → the **release gate** of the stage it closes.

A phase may contain work of several stages (Phase 23 did). Then the final report books every item to its stage, and the
milestone status is updated in each stage document.

## 3. Milestone overview

| ID | Milestone | Phases | Status (10 October 2026) |
|---|---|---|---|
| S1-M1 | Import, formats and runtime content (schema 2) | 1, 2 | done |
| S1-M2 | Typed game data and compatibility baseline | 3, 4 (block 0) | done |
| S1-M3 | Deterministic simulation core | 4 | done (numerics and rate later changed by Stage 2) |
| S1-M4 | Game rules, combat, economy, production | 5, 6, 7, 10 | done, with a fidelity debt register |
| S1-M5 | World, terrain, navigation, vision, construction | 6, 7, 10, 15 | done, with named limits |
| S1-M6 | Module coverage: every object of the retail data | 7, 9–14 | done (183 of 183 simulation modules, 2,115 of 2,115 objects) |
| S1-M7 | Script VM, missions, campaign, savegames | 9–14 | done headless; playability by a human → S1-M11 |
| S1-M8 | World presentation (terrain, objects, effects) | 7, 8, 15 | built – not accepted |
| S1-M9 | Input, shell, HUD, texts, original layouts | 8, 12, 16, publication | built – not accepted |
| S1-M10 | Sound, cursor, waypoints, mission in the client | 16, 17 | built – not accepted (never heard) |
| S1-M11 | Playability and acceptance by a human | 15, 16, 17, acceptance package, 31 (fidelity package A, done 10 October 2026) | **open** – the acceptance goal of Stage 1 is not reached; the three simulation findings of the pre-check are fixed (F-03, F-04, F-05; debris and four follow-ups entered as F-34 to F-37) |
| S1-M12 | Installer and clean content from the Steam master | publication (9 October) | built – not accepted |
| S1-M13 | The skirmish AI of the original as a faithful computer opponent | 23 (side product) | built as an option, with named deviations |
| S1-M14 | Tooling of the port | 1–23 | done, grows with every phase |
| **R1** | **Port release:** single player accepted end to end, package, documentation for humans | 30 (items 1, 2, 5–8) | **open** - Phase 30 built the package, the diagnostics and the documentation and replaced every acceptance by a machine check (`docs/ACCEPTANCE-MACHINE.md`, 14 October 2026); the gate's "a human has worked through" is not met |
| S2-M1 | Measurement basis: reference manifest, determinism matrix, profile | 18 | done; second machine open |
| S2-M2 | Performance of real matches | 19, 23 (gate) | done; maximum and allocation missed |
| S2-M3 | Tick rate as a value of the world (simulation and everything outside) | 20, 21 | done |
| S2-M4 | Fixed point Q39.24 | 22 | done |
| S2-M5 | Rate invariance and 60 Hz as the default | 23 | done with named limits |
| S2-M6 | Test strategy in four tiers | – (10 October) | done |
| S2-M7 | Session core: AI in the session state, skirmish savegame, match description, command path, tick source | 24 | **done** (11 October 2026; `docs/SIMULATION-CORE.md` §45) |
| S2-M8 | Engine follow-ups (tick maximum, hash constructions, allocation, thread, formats, documentation) | – | open backlog, partly decisions |
| **R2** | **Engine release:** 60 Hz seen by a human, determinism on a second machine, skirmish savegame | – | open |
| S3-M1 | AI kinds, difficulty and tactics | 6, 17, 23 (built-in AI), 33 | **built by machine** (15 October 2026): four tactics profiles and three strengths per slot, the rule "no standing still", the survey against thresholds (12 of 13 met); no human has watched a profile |
| S3-M2 | Spectator and observer | 23 (side product) | built – not accepted |
| S3-M3 | Replay browser | – | open (replay format exists) |
| S3-M4 | Match analysis with graphics | – | open (headless surveys exist) |
| S3-M5 | Modern user interface | – | open (original layouts are the base) |
| S3-M6 | Movement and pathfinding beyond the original | 34 | **built by machine** (16 October 2026): the movement profile `movement-budget` (path queue, cell budget per frame, one search per group order) as an option; the tick maximum target missed (13.7 ms with the profile, 123 ms in the original); not the default (D-3); no human has watched a group march |
| S3-M7 | Bug-fix and balance layers (GeneralsGameCode, GeneralsGamePatch) | 30 (items 3, 4), 32 | **built by machine** (15 October 2026): the patch layer compiles and plays, every CRC-gated fix reviewed, eleven adopted behind the rule profile; eight candidates and three undecided markers are a side item of `HANDOFF-PHASE33.md`; no human has played a layer |
| **R3** | **Changes release:** every change selectable, measured and accepted | – | open |
| S4-M1 | Protocol, room, relay in the process; local skirmish over the network path | 25 | **done** (11 October 2026; `docs/NETWORK-PROTOCOL.md`) |
| S4-M2 | Dedicated server, UDP, clock synchronisation, eight clients | 26 | **done on one machine** (12 October 2026; `docs/NETWORK-PROTOCOL.md` §12 to §17, `docs/DEDICATED-SERVER.md`); the second physical machine and the LAN are open |
| S4-M3 | Desync diagnosis, rejoin, spectators, server replay | 27 | **done on one machine** (12 October 2026; `docs/NETWORK-PROTOCOL.md` §18 and §19, `docs/NETWORK-DIAGNOSTICS.md`); votes not built (D-23); the second physical machine is open |
| S4-M4 | Server operation and security | 28 | **done on one machine** (13 October 2026; `docs/NETWORK-PROTOCOL.md` §20 and §21, `docs/DEDICATED-SERVER.md` §9 to §13); a hostile load from outside, a real network and the second physical machine are open |
| S4-M5 | Multiplayer in the client, latency perception, acceptance with humans | 29 | **done on one machine, without a human** (14 October 2026; `docs/MULTIPLAYER-FINDINGS.md`, `docs/GAME-SHELL.md` §15 and §16, `docs/GAME-HUD.md` §21): the menus' way to a server and back, the in-match indicators and the latency measurement are built and checked by programs that stand in for a person; no human has seen, heard or played any of it, and no second machine took part |
| S4-M6 | Co-simulating server as an option (hybrid: rejoin without a donor, desync arbitration, authoritative spectating) | – | open; after S4-M3, optional (D-11) |
| **R4** | **Multiplayer release:** server package (Windows, Linux), acceptance of LAN and internet matches | 30 | planned |

## 4. What was built in the "wrong" stage, and where it lives now

**Rule of the project owner (10 October 2026): no work is thrown away.** Work that already exists and belongs to a later
stage is **not rolled back** for the sake of the stages; the goal remains the state at the end of Stage 4, and the stages
are the order of the plan, not a reason to undo anything. "No rollback" does **not** mean "frozen": existing work may be
changed, optimised, restructured or rebuilt where that serves a clear path – given its option switch, its fingerprint, its
place in the session state, its brief – under the rules of its stage (measured, one cause per reference change). The
owner's decisions already made (the built-in AI as the default, self-acquisition per original frame) stand until the owner
changes them.

The stages were not the order of the work; several things came into being where a phase needed them. They are booked where
they belong, so that nobody looks for them in the wrong place or builds them twice:

| Built | In | Belongs to | Note |
|---|---|---|---|
| The built-in `SkirmishAi` (own development, not a port) | Phase 6, 17, 23 | S3-M1 | Default AI by decision of 10 October 2026; its tactics and levels are Stage 3 work; its state goes into the session in S2-M7 |
| The skirmish AI of the original (`ScriptedSkirmishSession`, three levels) | Phase 23 | S1-M13 | The faithful computer opponent; optional, not default; deviations in `docs/SKIRMISH-AI-ORIGINAL.md` §6.2 |
| Spectator mode (`--spectate`, observer HUD) | Phase 23 | S3-M2 | Not accepted by a human |
| Acceptance catalogue and guided run (`scripts/acceptance.ps1`) | after Phase 23 | S1-M11 | Not used by a human |
| Skirmish savegame (deliberately unsupported since Phase 10) | – | S1 debt, closed by S2-M7 | built in Phase 24 (`docs/CAMPAIGN-AND-SAVEGAMES.md` §8) |
| Factory exits, scroll speed at the screen edge, mouse assignment, build keys | Phase 23 | S1-M9/M10 (fidelity) | From the owner's tasks of 7 October; not accepted |
| Three behaviour changes at 30 Hz (lost wake, factory exit, AI after the first viewing) | Phase 23 | S1-M4 (fidelity) and S3-M1 (AI) | Each with its own manifest commit |
| Test strategy in four tiers (`docs/TESTING.md`, `scripts/test.ps1`) | 10 October | S2-M6 | Cross-cutting, binding |
| Patch integration and quality (the former Phase 30) | planned | S3-M7 (fixes, patch layer), R1/R4 (package, diagnostics, documentation) | The brief stays; its items are distributed |

## 5. Execution order (decided 10 October 2026, D-1)

Dependencies that do not move: S2-M7 (Phase 24) before every Stage 4 phase; a second physical machine (S2-M1, E1)
before S4-M2; the three simulation fidelity findings S1–S3 of the acceptance pre-check (`docs/ACCEPTANCE.md` §6) before
Phase 24 records its reference values (each changes every hash); Stage 3 changes of the simulation after S2-M7, because
they need the profile fingerprint in the match description to be safe for Stage 4.

The sequence from here:

1. **Done (Stage 1, Phase 31, 10 October 2026):** the fidelity package A – S1 riders leave through the door, S2 own destination per unit of a
   group, S3 slow-death phases with effect, creation list and weapon – one cause per commit, each with its manifest
   (S1-M11, prerequisite of Phase 24; brief `docs/handoffs/HANDOFF-PHASE31.md`, D-12; `docs/SIMULATION-CORE.md` §44). In parallel and at any
   time: the acceptance run by the project owner (`pwsh scripts/acceptance.ps1`), whose findings feed S1-M8 to S1-M11.
2. **Done (Phase 24, 11 October 2026):** the session core (S2-M7). The skirmish savegame debt is closed; the gate into Stage 4 is open.
3. **Phases 25, 26 and 27 done (11 and 12 October 2026)** (S4-M1 to S4-M3): the network core up to rejoin, watchers, desync diagnosis and server replay. The second
   machine is still missing; Phases 26 and 27 measured with separate processes on one machine - Phase 27 also across Windows and Linux on it - and say so. **Next by
   this order: the Stage 3 work that touches the simulation (item 4), then Phase 28;** the brief that is ready to be worked on is `HANDOFF-PHASE28.md`.
   **Phase 28 done (13 October 2026)** (S4-M4), on the owner's instruction before the Stage 3 work of item 4: the open port - a sealed and authenticated
   connection, limits, fuzzing, the server as a package for Windows and Linux, capacity and endurance on one machine. The brief that is ready to be worked on
   is `HANDOFF-PHASE29.md`; the Stage 3 work that touches the simulation is still to come.
   **Phase 29 done (14 October 2026)** (S4-M5): multiplayer in the client - the menus lead to a room on a server and back, "Host game" runs the server's core
   inside the game, the match shows what the connection does, and what a person would feel is measured. The owner replaced the acceptance by humans with
   frameworks (`docs/MULTIPLAYER-FINDINGS.md`); what only a human or a second machine can show is named there and stays open for R4. The brief that follows is
   `HANDOFF-PHASE30.md`.
   **Phase 30 done (14 October 2026)** (R1/R4 items, S3-M7 begun): by machine on one machine - the fix catalogue and the rule profile, the patch layer's
   import, diagnostics, the client package, the licence list, the separation proof, the clean room, the documentation for humans; every acceptance replaced by a
   machine check (`docs/ACCEPTANCE-MACHINE.md`). The brief that follows is `HANDOFF-PHASE32.md` (S3-M7 completion); by the order of item 4 S3-M1 would come
   first - the owner decides.
   **Phase 32 done (15 October 2026)** (S3-M7): the patch layer compiles and plays, every CRC-gated fix of GeneralsGameCode is reviewed, eleven are adopted.
   The brief that follows is `HANDOFF-PHASE33.md` (S3-M1: AI tactics and difficulty, with the rest of S3-M7 as a side item).
   **Phase 33 done (15 October 2026)** (S3-M1, S3-M7 rest): tactics profiles and strengths of the built-in AI, a stronger default AI, every CRC-gated fix decided.
   The brief that follows is `HANDOFF-PHASE34.md` (S3-M6: the movement profile, the home of D-3).
   **Phase 34 done (16 October 2026)** (S3-M6, with the fidelity corrections F-34, F-38, F-39): the movement profile as an option of the rule profile, measured against
   thresholds committed beforehand - the mean 15 % lower, the longest tick 13.7 instead of 123 ms, the target of 8 ms missed. With S3-M1, S3-M6 and S3-M7 the
   simulation-touching Stage 3 work is built; the brief that follows is `HANDOFF-PHASE35.md` (S3-M3: the replay browser).
4. **Stage 3, split by whether it touches the simulation (D-1):**
   - **Simulation-touching Stage 3 work** (S3-M1 AI tactics and levels, S3-M6 pathfinding, S3-M7 patch layers) comes
     **after Phase 27**: it needs the profile fingerprint in the match description and the replay header (built in 24 and
     25) to be safe for the network, and every such change moves reference values that the desync search of 25 to 27
     needs stable. Order inside: S3-M1 (the owner's visible wish, and the AI state is in the session since 24) → S3-M6
     (also lowers the tick maximum, D-3) → S3-M7.
   - **Stage 3 work outside the simulation** (S3-M2 acceptance and observer features, S3-M3 replay browser, S3-M4 analysis,
     S3-M5 modern UI) runs as **parallel packages from Phase 24 on**, in its own worktrees, merged before every network
     phase closes; the replay browser and the analysis take the server replay of Phase 27 as a further input when it exists.
5. **Phases 28 and 29** (S4-M4, S4-M5): operation, security, client, acceptance with humans; the multiplayer screens get
   their modern skin from S3-M5 where it is ready, the original layouts otherwise.
6. **Release gates:** R1 as soon as the human acceptance of the single player is done (it does not wait for Stage 4),
   R2 with the second machine and the 60 Hz view, R3 and R4 with Phase 30 as the common package.

Why not Stage 3 as a block before the network core (the owner's enumeration names 3 before 4): every Stage 3 change of the
simulation would move reference values that the desync search of Stage 4 later needs stable, and the network briefs 25 to 29
already exist and are measured against the state after Phase 24; the Stage 3 items a player sees first (replay browser,
analysis, spectator, UI) do not touch the simulation and therefore need not wait at all. Decided on 10 October 2026 by
the agent on the owner's instruction.

## 6. Release gates

| Gate | Passed when |
|---|---|
| **R1 Port** | A human has worked through the acceptance catalogue and clicked through shell → campaign → mission → save/load → completion and skirmish setup → match → result; every finding is fixed or named as a limit with a reason; the fidelity debt register has no entry without a disposition; the installer was run by a human from a Steam installation through `Start-CnCNew.cmd`; a client package for Windows and the documentation for humans (installation, troubleshooting) exist; tier 4 green at the state of the package. |
| **R2 Engine** | A human has seen and heard the game at 60 Hz (checklist in `docs/DEMO-FINDINGS.md`); the reference manifest passes identically on a second physical machine with a different CPU; the skirmish savegame works (S2-M7); the performance targets are reached or every rest is named with its cost centre; the formats carry their versions and old states are rejected with a clear message. |
| **R3 Changes** | Every change is selectable and off in the "original" profile; the original profile passes the unchanged manifest; every profile that touches the simulation has a fingerprint; balance effects are measured by the distribution comparison; a human has accepted each feature in the window. |
| **R4 Multiplayer** | Phase 29 acceptance by humans (local skirmish over the embedded server, LAN match, internet match with AI slots, disconnect and rejoin, spectators); server package for Windows and Linux; the version and compatibility rule for savegames, replays and protocol from the first release on; Phase 30 clean-room build. |

## 7. Decision register

All open decisions of the project owner in one place. The briefs each name a default so that nobody is blocked; the
decision stays with the project owner. Decided items are kept for one month, then moved to the stage document.

| ID | Question | Default in the plan | Due before | Stage |
|---|---|---|---|---|
| D-2 | Factory exit "always through the gate, even with an immediate command" – which behaviour has the original? (§7a no. 2 of the multiplayer plan: built, measured, not merged, shifts the balance) | unmerged: a unit with an immediate command leaves by the shortest route | R1 | 1 |
| D-3 | Lowering the tick maximum (141–143 ms at 60 Hz) needs a behaviour change: shared group path, cell budget per tick or a different search (§7a no. 3) | built as the option `movement-budget` (Phase 34; 13.7 ms, the target of 8 ms missed); the original movement stays the default - whether the profile becomes the default is the owner's | Phase 26 | 2 → built as S3-M6 |
| D-4 | The two faster hash constructions (vision row by row; object hash with module hashes); change every reference value (§7a no. 4) | not implemented | – | 2 |
| D-6 | Solo bonuses of the campaign on easy and hard (`SOLO_AI_EASY/HARD`, health bonus) – the original applies them, the port does not (§7a no. 6) | not implemented | R1 | 1 |
| D-7 | The draw order of the roads depends on the rate (§7a no. 7) | unchanged | – | 2 |
| D-8 | A standard skirmish ends after one hour of game time, also for a human (§7a no. 8) – the original has no such limit | unchanged | R1 | 1 |
| D-9 | Assessing the balance after the AI corrections (ChinaTankGeneral 70 of 80 against AirForce) (§7a no. 10) | not assessed | – | 3 |
| D-10 | The second physical machine with a different CPU (§7a no. 11, E1) | open; instructions in `docs/DETERMINISM.md` §4. Phase 26 ran without it (eight client processes on one machine); a second runtime environment that needs no hardware - Linux under WSL - needs the .NET runtime installed there | Phase 27 | 2 **Phase 27:** the runtime was installed in the WSL distribution with the owner's word; Linux and Windows agree on the manifest and in one match on this CPU (`docs/DETERMINISM.md` §2d). The second machine itself is open; the machine-only substitute is exhausted with this. |
| D-13 | Known 30 Hz findings, each a fidelity fix with its own manifest: `SupplyMachine` overwrites a player's command in one frame; a magazine without reload time fires once more through a second command in the same tick; `QuantityModifier` not applied (one Red Guard instead of two); the unit comes into being at completion, not when the gate opens | fix, one cause per commit | R1 | 1 |
| D-14 | `<This Team>` in a team's create script: fixed for AI sessions, not in the mission VM (changes manifest runs) | fix | R1 | 1 |
| D-15 | Rank system (skill points per kill, `Rank.ini`, purchase points per rank): the simulation gives one purchase point every 90 s instead | implement as a rule of the game | R1 | 1 |
| D-16 | Completing the original AI's C++ half (bridge repair, rebuild hole, cluster mines, prerequisite-free build path, placement without shroud; `docs/SKIRMISH-AI-ORIGINAL.md` §6.2) | leave as named deviations of the option | R1 | 1 |
| D-17 | Rule for slow clients: how long to display, throttle, disconnect | display up to 2 s, throttle up to 10 s, then disconnect - **built with these defaults as configuration (Phase 26, `docs/NETWORK-PROTOCOL.md` §14); measured: a lost connection slows the others for 8 s at half speed, a client that only keeps up with the slowed game for 14.4 s in three incidents. Open: the numbers, the speed while slowed (50 %), and whether the count starts again after a recovery** | Phase 27 | 4 **Phase 27: the restart of the count is configuration now (`lagRecoveryMs`, default 30 s: a member's used time is forgotten only after it kept up that long); a close after the end is logged as a close. The numbers stay a proposal.** |
| D-18 | What happens to the objects of a player who dropped out | look up the original, propose, decide - **looked up (`MSG_SELF_DESTRUCT`) and built that way (Phase 26, `docs/SIMULATION-CORE.md` §47): a lost connection or a drop kills everything of the player (a tech building becomes neutral), leaving by choice hands objects and money to the first living ally. Open: whether that is wanted, above all once a seat can be held for a rejoin (D-19)** | Phase 27 | 4 |
| D-19 | How long a seat is held for rejoining; spectator delay | 5 minutes; 120 s | Phase 27 | 4 **Phase 27: both built as configuration of the server (`seatHoldMs`, `spectatorDelayMs`, also `allowLateWatchers`); the defaults are 0 / 0 / off until decided, 5 minutes and 120 s stay the proposal. Measured: a rejoin after seconds on any line of 1 Mbit/s or more (`docs/NETWORK-PROTOCOL.md` §19.1). Also open here: whether a held seat should end the D-18 rule's killing of a lost player's objects earlier or later than the hold - today the objects stand until the hold is over.** |
| D-20 | Server under Linux as a delivery target | yes | Phase 28 | 4 **Phase 28: the self-contained `linux-x64` package exists (`scripts/package-server.ps1`) and its server played a match in WSL without a .NET runtime in its environment (`docs/DEDICATED-SERVER.md` §13). Open: whether it is handed out, and on which distributions it is to be tried** |
| D-21 | `Display.UiScale = IntegerMultiple` as an alternative to the stretched command bar at 16:9 | stretched as in the original; integer scaling is an S3-M5 option | – | 3 |
| D-22 | Should 120 Hz be selectable by a player in the finished game (30 Hz: no, test rate only – decided) | no, test rate | R2 | 2 |
| D-23 | Votes in a multiplayer match (kick a player, end the match, change the speed): whether, and what they decide | not built; pause is limited per player and in length, the speed is the host's (`docs/NETWORK-PROTOCOL.md` §18.5). A further proposal of Phase 27: the server's hash interval 4 instead of 1 (6 KB/s less upstream per client, a desync noticed at most three ticks later; `docs/NETWORK-DIAGNOSTICS.md` §6) | Phase 29 | 4 **Phase 28: the host of a room removes a member (`Kick`, `docs/NETWORK-PROTOCOL.md` §20.5) - the first thing of that kind; no vote** |
| D-24 | A player that reports a wrong hash ends the match for everybody as a desync - the relay does not simulate and cannot tell who lies. Proposal: with three or more players the majority's hash is the match's and the minority is dropped; with two the match ends as today. (A watcher's wrong hash already drops only the watcher, Phase 28.) | not built; the match ends | Phase 29 | 4 |
| D-25 | The player key - a permanent pseudonym a server sees: does the window client state one by default (it does), and does a public server require one (`requirePlayerKey`, default false) | stated by default, not required | Phase 29 | 4 |
| D-27 | Shall a server list its rooms to anybody who asks (`docs/NETWORK-PROTOCOL.md` §22.1, threat T16)? A list names rooms, counts, map, build and lock, and no person | listed by default (`listRooms`); `--no-room-list` | R4 | 4 |
| D-28 | Which builds may enter a game somebody hosts from the menus? | the build that hosts, changed or not; a dedicated server stays with released builds unless `allowModifiedBuilds` | R4 | 4 |
| D-29 | The extrapolation of at most one tick when the next tick is late (`--extrapolate`): on or off? Measured without effect - a tick was late in 0 to 5 of 7,178 frames | off | R4 | 4 |
| D-30 | A chat line "to the allies" from a seat in no team | reaches its sender only | R4 | 4 |
| D-31 | From the first release on: is every change of tick rate, fixed point, snapshot, replay, command table, match description or network protocol a break that needs its own decision; are old saves and replays converted; how long are old builds kept for old replays (`docs/COMPATIBILITY.md` §3)? | proposal in `docs/COMPATIBILITY.md` §3; today every version change is refused with its reason | R1/R4 | all |
| D-32 | Which adopted GeneralsGameCode fixes a release offers, and may a rule profile other than the original ever become the default (it changes every reference value) (`docs/GGC-FIXES.md`)? | the original is the default; `--rules` is opt-in; three fixes adopted | R1 | 3 |
| D-33 | The patch layer: may it ship as a choice of the installer, and does a room of a server show "original" or "patch" by name (today only by the definition fingerprint) (`docs/PATCH-LAYER.md`)? | not offered to players until its definitions compile | R1/R4 | 3 |
| D-26 | The limits of a public server (`docs/DEDICATED-SERVER.md` §9.2): the defaults are proposals from measurements on one machine - above all 8 connections per address, 100 key exchanges a second, 500 messages a second per member, 20 s without a hash report, a match of at most 2 h, a recording of at most 64 MiB | the defaults of §9.2 | R4 | 4 |

**Decided:**

- **D-5 (13 October 2026, built in Phase 28 as the brief of that phase asked):** a command of a player may not state a point beyond 131,072 units on any axis
  or an angle beyond 1,024 radians; `SimulationWorld.Check` and `Submit` reject it with `InvalidArguments` - on every machine alike, before it could overflow the
  movement. It is a coordinate limit, not the map's bounds: an order to the limit is accepted and the map stops the unit (`docs/SIMULATION-CORE.md` §49). No
  reference value moved.

- **D-1 (10 October 2026, by the agent on the owner's instruction):** Stage 3 is split – work that touches the simulation
  (S3-M1, S3-M6, S3-M7) after Phase 27; work outside the simulation (S3-M2 to S3-M5) as parallel packages from Phase 24 on
  (§5).
- **D-11 (10 October 2026, recommendation of the agent, confirmed by the owner's question "hybrid?"):** the data path
  of a match stays **commands in tick bundles** (deterministic lockstep); **hybrid** means a **co-simulating server as an
  option** (`STAGE-4-MULTIPLAYER.md` §1a and S4-M6): a server that an operator equips with game data runs the match
  itself and uses its state for rejoin without a donor, arbitration of a desync, authoritative spectating and a replay
  with state – nothing of it on the match path, nothing of it required. A state stream as the match path is excluded for
  an RTS of this size (thousands of objects, 60 Hz; a snapshot is 3 to 9 MB).
- **D-12 (10 October 2026, owner):** the fidelity package A before Phase 24 – yes (`docs/handoffs/HANDOFF-PHASE31.md`). **Done on 10 October 2026** (`docs/SIMULATION-CORE.md` §44).
- Self-acquisition once per original frame of the unit stays (10 October 2026; `docs/SIMULATION-CORE.md` §43); the
  built-in `SkirmishAi` stays the default AI, the original AI an option (10 October 2026); the target platform is Windows,
  Linux later only as host of the dedicated server (9 October 2026); 60 Hz is the default and the abort criterion of
  Phase 23 was lifted (8 October 2026); 30 Hz stays a test rate (planned default, not contested).

## 8. Rules that carry over

`CLAUDE.md` (layers, quality rules, git, procedure of a phase), `docs/TESTING.md` (which tier a change asks for),
`docs/MULTIPLAYER-PLAN.md` §6 (measure first; one cause per reference change; determinism across machines; no wall-clock
time in the simulation; nothing falls back silently; formats raise their version; verified headless is not accepted;
continue existing briefs). New here:

1. **Every brief names its stage and milestone** in its header (`BRIEF-FORMAT.md`); a final report books every item to
   a stage and updates the milestone status in the stage document.
2. **Stage 3 work is an option from its first commit.** No change beyond the original lands in the default profile
   without the owner's decision; the "original" profile keeps the manifest. What is already the default by the owner's
   decision (the built-in `SkirmishAi`) stays so; the rule binds new work, it does not undo old work.
3. **Nothing is rolled back for the sake of the stages** (§4). Existing work of a later stage is kept; it may be changed,
   optimised or rebuilt where that serves a clear path, under the rules of its stage – it is never thrown away because of
   its stage.
4. **The fidelity debt register** (`STAGE-1-PORT.md` §3) is the list of known deviations from the original. A new
   deviation found anywhere is entered there with its source reference before it is fixed or left.
5. **The decision register** (§7) is the one list of open decisions; `docs/MULTIPLAYER-PLAN.md` §7/§7a point here.

## 9. Keeping this plan current

At the end of every phase: milestone status in the stage document and in §3; new deviations into the fidelity register;
new questions into §7; decided questions marked; the index `docs/handoffs/README.md`; the next brief in the format of
`BRIEF-FORMAT.md`. Nothing in these documents is a measured value – the numbers are quoted from the documents that
measured them, with their date and commit, and are re-measured before use.

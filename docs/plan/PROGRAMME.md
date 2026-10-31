# Programme plan: the four stages

As of: 10 October 2026, state `2777a90` (decision of the project owner of 10 October 2026 to order the whole programme by
four stages). This document is the top of the plan. It replaces `docs/ROADMAP.md` and `docs/IMPLEMENTATION-PLAN.md` as
the plan; both stay as the record of the acceptance statements and measured numbers of their time. The technical frame of
the multiplayer and the register of bottlenecks stay in `docs/MULTIPLAYER-PLAN.md`. Numbers in this document are the
measurements of their state and are re-measured before any statement is built on them; the yardstick for results is
`baselines/reference-manifest.json`, not a number in a text.

**Since 31 October 2026 there is one product** (decisions D-3, D-4, D-32, D-33 in §7, built in Phase 52): the game is the patched variant - GeneralsGamePatch above
the Steam installation, every adopted fix, the movement profile - and the manifests measure it (`docs/PATCH-LAYER.md`). The original stays beside it only as a switch
that tells a defect of the patch from one of the port: `game/original/content` and `--rules original`. Where the stage texts below say "the original profile keeps the
manifest", that is the history of how the work was ordered. A fidelity finding is still read in the original's source: the port's engine stays faithful, the data on top
of it are the patch's.

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
| S1-M11 | Playability and acceptance by a human | 15, 16, 17, acceptance package, 31 (fidelity package A, done 10 October 2026), 39 (fidelity package B, done 27 October 2026) | **open** – the acceptance goal of Stage 1 is not reached; the three simulation findings of the pre-check are fixed (F-03, F-04, F-05; debris and four follow-ups entered as F-34 to F-37); package B fixed F-07 to F-10 and F-43 to F-47, F-06 is the original's behaviour (`docs/SIMULATION-CORE.md` §53) |
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
| S3-M2 | Spectator and observer | 23 (side product), 37 | **built by machine** (18 October 2026): views of every player, the research tab, the event ticker with the camera at an event, for a match and a replay; not accepted - no human has watched |
| S3-M3 | Replay browser | 35 | **built by machine** (16 October 2026): every skirmish recorded beside the savegames, the list on `ReplayMenu.wnd` with the state of every file, playback with pause, speed, seeking to checkpoints and the choice of view; every build plays only its own replays (no migration, D-31); no human has used it |
| S3-M4 | Match analysis with graphics | 36 | **built by machine** (17 October 2026): one analysis core for a running match and a replay, the export (JSON, TSV) the same bytes on every run, an HTML report, the screen after a match and from the replay list, one field comparison for the diagnosis tools; no human has read a report |
| S3-M5 | Modern user interface | 38 | **built by machine** (18 October 2026): the design decision before any picture, the modern skin derived from the original's layouts at run time beside the original's (the default, D-21), "More options", both skins pinned in six resolutions, a scripted person switches the skin, the readability check with the GDI font (`docs/UI-MODERN.md`); not accepted - no human has used it |
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
   **Phase 35 done (16 October 2026)** (S3-M3): the replay browser by machine - every skirmish recorded, the list with the state of every file, playback to the
   recorded hashes with seeking; no simulation change. The brief that follows is `HANDOFF-PHASE36.md` (S3-M4: match analysis, which takes the replays as its input).
   **Phase 36 done (17 October 2026)** (S3-M4, with the S3-M6 residue): the match analysis by machine - one core for a running match and a replay, the export the same
   bytes on every run, an HTML report, the screen after a match and from the replay list, one field comparison for the diagnosis tools; the score screen's counters
   fixed; the computer players' placement checks under a budget in the movement profile. The brief that follows is `HANDOFF-PHASE37.md` (S3-M2: the spectator and
   observer features, which take the replays and the analysis' events as their input).
   **Phase 37 done (18 October 2026)** (S3-M2 (b) and (c), with side items): the observer's event ticker from the analysis core, views of every player, the research
   tab, all of it for a replay; the score screen counts as the original's `ScoreKeeper` (F-42); the GLA AI's missing supply stash diagnosed (D-34); the remaining long ticks
   under `movement-budget` shown to be first calls (`perf --warm-up`: 10.1 / 11.2 ms warmed up). The brief that follows is `HANDOFF-PHASE38.md` (S3-M5: the modern user
   interface, the last milestone of Stage 3 outside the simulation).
   **Phase 38 done (18 October 2026)** (S3-M5, with side items): the modern skin as a profile beside the original's, from a design decision committed before any
   picture; More options; both skins pinned, a scripted person, the readability check; two defects of the analysis screen found and fixed; the late long tick under
   `movement-budget` broken down; D-34 prepared as a measured proposal (variant B: only the GLA worker kept; 7 of 50 manifest runs change). With it every Stage 3
   milestone is built by machine - none is accepted by a human (R3). The brief that follows is `HANDOFF-PHASE39.md` (Stage 1: fidelity package B, D-13, D-14, F-43).
   **Phase 39 done (27 October 2026)** (S1-M11, fidelity package B, with side items): F-07 to F-10 and F-43 fixed as the original's source does, and five further
   findings of the same reading - F-44 and F-45 in `ProductionUpdate.cpp`, F-46 and F-47 through Zero Hour Reforged (`docs/REFORGED-FIXES.md`) - each one cause, with
   its own manifest commit where a manifest moved (F-07 and F-46 moved none); F-06 is the original's behaviour (a rule-profile candidate). After F-44 the retail reference match ends in a stalemate at its limit of
   432,000 ticks. The modern skin draws tooltips, messages and superweapon clocks; the campaign's way is pinned in both skins. The brief that follows is
   `HANDOFF-PHASE40.md` (Stage 1: the review of Zero Hour Reforged, F-48, F-49, the reference match's driver seed).
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
| **R3 Changes** | (Decided otherwise on 31 October 2026: the changes of the rule profile and the patch are the product, the original stays only as a switch.) Every other change is selectable; every profile that touches the simulation has a fingerprint; balance effects are measured by the distribution comparison; a human has accepted each feature in the window. |
| **R4 Multiplayer** | Phase 29 acceptance by humans (local skirmish over the embedded server, LAN match, internet match with AI slots, disconnect and rejoin, spectators); server package for Windows and Linux; the version and compatibility rule for savegames, replays and protocol from the first release on; Phase 30 clean-room build. |

## 7. Decision register

All open decisions of the project owner in one place. The briefs each name a default so that nobody is blocked; the
decision stays with the project owner. Decided items are kept for one month, then moved to the stage document.

None open since 31 October 2026.

**Decided:**

- **All open decisions (31 October 2026, the owner; each with the agent's recommendation and the options put as a question).**
  **The change of course: there is one product, our patched variant.** D-33: the patch layer is always on, the original need not stay; D-32: the adopted fixes are the default; D-3: `movement-budget` is the default; D-4: the two faster hash constructions are built. The patch becomes the only yardstick: the manifests measure patch, fixes and movement profile, the installer always imports the patch, and the original profile stays in the code only as a switch that tells whether a defect lies in the patch or in the port. **This switch is Phase 52**, before all other work, because it moves every reference value once.
  Stage 1: D-2 the factory exit as the original does it (read it and build that); D-6 the solo bonuses as in the original; D-7 the road order is fixed now; D-8 no time limit for a human player, runs of computer players keep their budget; D-9 the balance is not assessed; D-10 no second physical machine: determinism across CPUs stays unmeasured, a documented risk; D-15 the rank system as in the original; D-16 the original AI's C++ half is completed; D-31 the proposal of `docs/COMPATIBILITY.md` §3 (a break is announced, saves are not converted, a replay plays with its build, old builds are kept for it); D-34 variant B.
  Interface: D-21 the modern skin is the default, the original selectable, no integer scaling; D-22 120 Hz is selectable in the options.
  Multiplayer: D-17 the numbers stay; D-18 as built; D-19 a seat is held 5 minutes, a watcher delayed 120 s, late watchers allowed; D-20 the Linux server is delivered, tried on Debian and Ubuntu; D-23 votes are built (removing a player, ending the match, the speed) and the hash interval is 4; D-24 the majority rule from three players on; D-25 the player key only on request, anonymous by default; D-26 the defaults of `docs/DEDICATED-SERVER.md` §9.2, without the limit of two hours per match; D-27 the room list stays on and can be switched off; D-28 the build of the host; D-29 the extrapolation stays off; D-30 the sender of a line to the allies from a seat in no team is told it has no allies.

- **D-13 and D-14 (fix, 10 October 2026; done in Phase 39, 27 October 2026):** F-07, F-08, F-09 and F-10 fixed, each with its own manifest commit; F-06 is what the
  original does (`SupplyTruckWantsToPickUpOrDeliverBoxesState::update` gives the dock order without a check) and stays (`docs/SIMULATION-CORE.md` §53.4).

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

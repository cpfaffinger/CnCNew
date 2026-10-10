# Programme tick rate, fixed point and multiplayer (Phases 18–29)

As of: 5 October 2026 (planning; nothing of this is built). This document is the common frame of the briefs `docs/handoffs/HANDOFF-PHASE18.md` to `docs/handoffs/HANDOFF-PHASE29.md`. Each of these briefs can be worked on by itself, but presupposes the decisions (§2) and rules (§6) of this document. Every phase writes its measurement results back here (§4, §5).

## 1. Goal and scope change

Decision of the project owner of 5 October 2026:

1. **Multiplayer is in scope.** Matches (skirmish) with up to eight players run through **dedicated servers**.
2. **The simulation runs at 60 Hz** instead of 30 Hz. The tick rate is a parameter of the session, not a constant of the game; everything that today hangs firmly on 30 Hz is decoupled.
3. **One match path.** The multiplayer module also carries the local skirmish (human against AI). It must therefore support AI players.
4. **All previous simulations and acceptance tests** are checked and, where they hang on 30 Hz or on hashes from the documentation, redesigned.
5. The completion planned so far after Phase 17 (patch integration, quality, package; `IMPLEMENTATION-PLAN.md` at that time §10) moves behind this programme and is now Phase 30.

The goal behind it: a modern game – direct command intake, smooth movement, network play that does not depend on the slowest fellow player.

**Still not in scope** (every extension is a decision of its own): networked campaign or co-op missions; accounts, ladder, player search, central server list; NAT traversal and connections between players (clients connect outbound to a server with an open port); compatibility with the network protocol or the services of the original (GameSpy/WOL); protection against map reveal; transfer of custom maps; binary compatibility with old mods and savegames.

## 2. Decisions and target architecture

### 2.1 Lockstep through a relay server

The simulation is deterministic and is driven only by commands. What is transmitted is therefore commands, not state. Every client computes the full world.

```
 Client A ──command intent──▶ ┌────────────────────────┐ ──tick bundle N─▶ Client A
 Client B ──command intent──▶ │ Server (relay)         │ ──tick bundle N─▶ Client B
   …                          │ tick clock, assignment,│        …
 Client H ──hash report─────▶ │ hash comparison,       │ ──tick bundle N─▶ Client H
                              │ recording (replay)     │
                              └────────────────────────┘
```

- The server keeps the tick clock, assigns every incoming command intent to the next open tick and sends one bundle per tick to everyone (also an empty one). **When closing a tick it waits for nobody.**
- A client computes tick N as soon as it has bundle N. The delay of a player thus hangs on their own connection, not on that of the slowest.
- The server **does not simulate** and loads **no game data**. That keeps it cheap (many rooms per core), legally clean (no content generated from original data on foreign machines) and portable (Windows and Linux).
- Rejected: *rollback* (saving the state several times per second and recomputing ticks is, with thousands of objects and today's snapshot, too expensive by orders of magnitude); *authoritative server with state transfer* (gives up the main advantage of the architecture, requires game data on the server). A *co-simulating* server (knows the correct state, can deliver snapshots) remains a later option; nothing in Phases 24–28 may obstruct it.

### 2.2 AI players run in the simulation of every client

The AI becomes part of the deterministic, hashed and saved session state (Phase 24). Every client lets all AIs run along; their commands are not transmitted. The server knows of an AI only that its slot is occupied.

- In favour: no latency and no bandwidth for AI commands; the server stays a relay without data; rejoining and saving work because the AI state is in the snapshot; a deviation in the AI state shows up immediately in the hash; it is the model of the original.
- Against: the AI computing time is incurred on every client (Phases 19 and 24 measure and limit it).
- Rejected: AI on *one* client with transmission of its commands (fails with this client, needs a handover); AI on the server (requires simulation and data there).

### 2.3 One match path

```
 Shell/HUD/input   ─▶ LiveMatch ─▶ lockstep driver  ─▶ ITransport ─▶ server logic
                          │         (CnCNew.Net)        loopback      (CnCNew.Server)
                          ▼                              or UDP        embedded or
                     MatchSession (world + AI, CnCNew.Simulation)      own process
```

Local skirmish = embedded server + loopback transport + one client, in the operating mode "client-paced" (the tick closes as soon as the one human requests it): no additional delay, pause and game speed remain. Network match = the same code with UDP and "server-paced". Missions stay local, but use the same interfaces for command path and tick source (Phase 24).

### 2.4 Layers and dependencies (supplements `CLAUDE.md`)

| Project | new | may reference | may not |
|---|---|---|---|
| `CnCNew.Simulation` | – | `CnCNew.Data` | everything else; wall-clock time, threads, `float` (unchanged) |
| `CnCNew.Net` | yes | `CnCNew.Simulation` (command codec, match description, replay) | `CnCNew.Content`, `CnCNew.Game`, presentation, Stride |
| `CnCNew.Server` | yes | `CnCNew.Net` | load game data, `CnCNew.Content`, `CnCNew.Game`, presentation, Stride |
| `tools/CnCNew.DedicatedServer` | yes | `CnCNew.Server` | as above |
| `CnCNew.Game` | – | additionally `CnCNew.Net`, `CnCNew.Server` (embedded) | Stride (unchanged) |

Wall-clock time exists only in `CnCNew.Net`/`CnCNew.Server`, only through an injected clock, only for pacing and time synchronisation. It never reaches the simulation. Networking exclusively with the BCL; every new NuGet dependency needs a verified justification.

### 2.5 Tick rate

- The rate is a value of the world (Phase 20). **Supported are 30, 60 and 120 Hz** (30 · 2ᵏ): frame values of the original scale exactly, multiplicative factors need only square roots. Product rate after Phase 23: **60 Hz** for skirmish and campaign. **Since 8 October 2026 (`e8632bb`) 60 Hz is the default** – one definition, `ProductRate.TicksPerSecond` in `CnCNew.Game`; client, tools and tests derive from it, the simulation itself knows no default.
- **Times from the data stay on the 30 Hz grid of the original** (round up to original frames, then × rate/30). The balance is thus the same at every rate as in the original; the higher rate brings finer movement and faster command intake, no different rates of fire.
- Outside the simulation there are two separate concepts: the *simulation rate* (variable) and the *original frame* of 1/30 s, in which the original authored client content (fixed). The clock of the presentation is game time in seconds (Phase 21).
- Golden hashes are maintained after Phase 23 only for 60 Hz; 30 and 120 Hz stay runnable and are checked by the rate-equivalence suite.

### 2.6 Fixed point

`Fixed` changes from Q47.16 to **Q39.24** (Phase 22), alone, at 30 Hz, before the change of the rate. Justification and numbers in `docs/handoffs/HANDOFF-PHASE22.md`.

### 2.7 Compatibility when joining

Two machines may only play together if protocol version, simulation version (identifier of the code state), `CommandTypes.Version`, replay format, definition fingerprint, map file hash and tick rate match. There is no "join anyway" path.

### 2.8 What the model cannot do

Every client knows the full state; map reveal by a manipulated client cannot be prevented in lockstep. The server prevents what it can prevent: commands under a foreign player ID, manipulation of the game speed, altered state (shows up as a desync).

## 3. Latency model (calculated, not measured)

Rough estimate for the time from the command intent to the visible effect for a player with 30 ms round-trip time:

| Stage | 30 Hz | 60 Hz |
|---|---|---|
| Round-trip time | 30 ms | 30 ms |
| Waiting for the next open tick (mean) | 17 ms | 8 ms |
| Playback buffer against jitter (1 tick) | 33 ms | 17 ms |
| Display lag of the interpolation (1 tick) | 33 ms | 17 ms |
| Sum | around 113 ms | around 72 ms |

The tick rate shortens only the three stages that count in ticks (around 40 ms gain, independent of the round-trip time); at 300 ms round-trip time that hardly matters. The biggest lever for the *perception* is the immediate local feedback (Phase 29). Measurement takes place in Phase 26 (network) and Phase 29 (perception); the results replace this table.

## 4. Register of bottlenecks and showstoppers

As of 5 October 2026, verified by reading the code (`2a66661`), two search runs and one trial measurement. "Showstopper" means: without a solution the goal does not work. Every phase writes its result into the last column.

| No. | Kind | Finding | Evidence | Phase | Result |
|---|---|---|---|---|---|
| E1 | Showstopper | Determinism is verified on one machine only | all comparison runs so far | 18 | **partly verified (Phase 18):** one machine, 13 rows of the matrix identical, among them Linux under WSL2, cultures, JIT/GC switches (`docs/DETERMINISM.md` §2); **open:** second physical machine with a different CPU, different .NET version |
| E2 | Risk | Bone table and map coordinates are produced through `float`/`MathF`/`(decimal)float`; dictionary enumeration with a side effect; culture-dependent script hash; gaps in fingerprints and `SourceRules` | `docs/handoffs/HANDOFF-PHASE18.md` D1–D9 | 18 | **fixed (Phase 18):** D1–D9 each confirmed or refuted with a test and corrected (`docs/DETERMINISM-FINDINGS.md`); real were D4 (culture), D5 (fingerprint), D8 (missing `MarkDirty` on 19 of 45 mission maps), D9 (line endings: Linux rejected Windows); rest: own escaping of the fingerprint, identifier of the code state (Phase 25) |
| E3 | Bottleneck | Reference values exist only in the documentation, not in tests | `docs/TICKRATE-AUDIT.md` §7 | 18 | **fixed (Phase 18):** `baselines/reference-manifest.json` (50 runs) and `baselines/input-manifest.json`, `SimTool reference-manifest check` (`docs/SIMULATION-CORE.md` §37) |
| E4 | Showstopper | A real 8-player match costs around 7–9 ms per tick with a few hundred objects (non-quiet sample); at 60 Hz around half a core, catch-up speed about 2-fold | `docs/handoffs/HANDOFF-PHASE18.md` item 4 | 18 (profile), 19, 23 (gate) | **measured (Phase 18, quiet):** 8 players 4.98 ms per tick (99 %: 13.6 ms, maximum 547 ms), at 60 Hz 30 % of one core and 3.4-fold catch-up speed; `MD_GLA05` 12.6–13.5 ms, at 60 Hz 76–81 % and 1.2-fold; 81 % of the update time `AIUpdateInterface`, one third vision and hash (`docs/PERFORMANCE.md` §6); reduction in Phase 19 **Phase 19 (measured quietly, all end hashes unchanged):** 8 players 1.14 ms per tick (99 %: 3.2 ms, maximum 94 ms), at 60 Hz 6.8 % of one core and 16.2-fold catch-up speed; `MD_GLA05` 2.9–3.4 ms (17–21 % of one core at 60 Hz). Targets for mean and 99 % narrowly missed, maximum missed (many simultaneous long path searches; can only be lowered with a behaviour change, `docs/PERFORMANCE.md` §6a.4). **Phase 23, performance gate at 60 Hz (9 October 2026, quiet, two runs, `docs/PERFORMANCE.md` §6c):** 8 players, 30 game minutes (108,000 ticks): mean 683 / 694 µs (target ≤ 1.0 ms: reached), 99 % 1,615 / 1,793 µs (≤ 3 ms: reached), catch-up speed 26.7-fold / 26.5-fold (≥ 8-fold: reached), 4.1–4.2 % of one core; **maximum 141 / 143 ms (≤ 8 ms: missed** – one tick with many simultaneous long path searches; the behaviour change that lowers it has been reserved by the project owner, §7a no. 3). `MD_GLA05` at 60 Hz 2.1–2.4 ms per tick, 13–14 % of one core, catching up live 7-fold to 8-fold. For the same work a tick at 30 Hz costs around 5 % more after the phase (stress test). |
| E5 | Showstopper | Tick rate is a `const`; 61 uses, around 110 bare tick numbers in the simulation, catalogues convert at build time | `docs/TICKRATE-AUDIT.md` §1–§3 | 20 | open **done for the simulation (Phase 20, state `9299a2d`):** rate as a value of the world, catalogues per rate, 262 + 188 + 11 places converted, source-code rule against bare tick numbers; at 30 Hz full manifest identical under Windows and Linux; at 60/120 Hz deterministic runs without overflow, behaviour not assessed. Open outside the simulation (Phase 21, 16 items in `docs/TICKRATE-MIGRATION.md` §6). |
| E6 | Showstopper | Data as raw frame values (drag, dampings, script frame counters, substitute values) | `docs/TICKRATE-AUDIT.md` §4 | 20, 23 | open **Phase 20:** additive frame values, script frame counters and substitute values are converted; multiplicative factors, integration per tick, N+1 counters and sampling rhythms stand unchanged on the list for Phase 23 (95 entries, `docs/TICKRATE-MIGRATION.md` §5). **Phase 23 (9 October 2026, state `f3acc2e`): done with a named rest.** Each of the 95 rows carries its whereabouts: 78 done, 13 deliberately left, 4 open (`Register`/first frame for all modules, `ObjectiveDriver.Commands`, catalogue values `per tick × r` on some movers, amounts per tick from state). Drag, dampings and mixed recursions run once per original frame; the script VM counts frames of the original again. |
| E7 | Showstopper | Particle effects run on the simulation tick; envelopes, EVA, HUD and mission presentation count ticks; copies of the 30 outside the simulation | `docs/TICKRATE-AUDIT.md` §2, §6 | 21 | open **done (Phase 21, state `2a0a6f5`):** effects, envelopes, Eva, HUD and mission presentation run on the original-frame grid on the game clock (`GameClock`); one rate constant outside the simulation, source-code test with 3 named exceptions; at 30 Hz all reproducible checksums unchanged; timing at 30/60/120 Hz equal for all checked elements. Not seen or heard by a human. |
| E8 | Bottleneck | Q47.16 is too coarse for quantities per tick² at 60 Hz; `FixedTrig` coarser than the target resolution; around 170 raw-value places, fixed `65536` outside the simulation | `docs/handoffs/HANDOFF-PHASE22.md` | 22 | open **yardstick from Phase 20 (`SimTool rate-audit`):** no used catalogue value rounds to zero at 60 or 120 Hz; above 1 % quantisation error 1 value (60 Hz) and 41 values (120 Hz, worst `LocomotorInfo.Braking` 9.59 %); below eight significant bits 46 and 321 values respectively. **done at 30 Hz (Phase 22, state `8d74ef8`):** Q39.24, `FixedTrig` accurate to around 10⁻⁷, source-code rule against scale literals, all formats with a new version, manifest re-measured (5 of 16 AI runs with a different outcome through rounding amplification, balance not assessed). At 60 and 120 Hz nothing rounds to zero any more when building the catalogue, no value above 1 % error. Overflow when squaring placeholder ranges fixed (`SquaredReach`); **open:** unchecked target coordinates in commands (from around 524,000 units on, overflow in the movement) – command check against the map bounds before lockstep (Phase 27/28). |
| E9 | Showstopper | Integration per tick is not step-size invariant (movement, flight, projectiles, toppling, multiplicative damping) | `docs/TICKRATE-AUDIT.md` §5 | 23 | open **Phase 23 (9 October 2026, state `f3acc2e`): done with a named rest, 60 Hz is the default.** Principle: the 30 Hz step is the definition, higher rates subdivide it (decided in the first tick of the original frame of an object, the 30 Hz position reached with the last one, observers read the frame end; `docs/SIMULATION-CORE.md` §43). Rate-equivalence suite from 479 / 625 quantities outside the tolerance (60 / 120 Hz) via 63 / 64 and 35 / 33 to **0 / 0** of 4,388, without changing a tolerance; the retail reference match is the same game at 30, 60 and 120 Hz (frame 14,130, 26,626 commands). **Rest, measured:** 2 of 16 pairings on shipped maps end at 60 Hz in a different frame (7 s and 84.5 s, the same winner); 33 of 320 matches of the survey end more than one second away from the 30 Hz match, 3 with a different outcome; 120 Hz is not measured for pairings and survey; some movers still compute `value per tick × r` (`docs/TICKRATE-MIGRATION.md` §15.7.6). The equality of whole matches is bought with a self-acquisition per original frame of the unit, which the brief forbade by its wording (§7a no. 1, confirmation open). Three defects at 30 Hz were fixed along the way, each with its own manifest (`6a99102`, `8a6e5b7`, `cca6f92`). Not seen by a human. |
| E10 | Showstopper | No test compares two rates; all acceptances are hashes and tick budgets at 30 Hz; around 845 test lines with tick expectations | `docs/TICKRATE-AUDIT.md` §7 | 21, 23 | open **Phase 21:** timing tests compare 30, 60 and 120 Hz; 142 test files assessed (none open), time expectations in game time; the simulation tests run on a trial basis at 60 Hz (396 of 402, six named failures for Phase 23). The comparison of the game results between rates remains Phase 23. **Phase 23 (9 October 2026): done.** Rate-equivalence suite (686 cases, 4,388 quantities in ten groups, tolerances fixed before the first run, `docs/SIMULATION-CORE.md` §39) and distribution comparison over 320 matches per rate (§40) compare game quantities between 30, 60 and 120 Hz; `rate-compare --ai-diff/--reference-diff/--map-diff` names the first deviating frame of a whole match. All test programs take `--tick-rate` and pass at 30, 60 and 120 Hz (simulation 513 of 513, with real data 543 of 543, at state `1b82e45`); expectations are stated in frames. The reference manifest stands at 60 Hz (`ea52f87`) and is checked with the rate of its file. **Open:** the determinism matrix and the Linux run with the 60 Hz manifests (`docs/DETERMINISM.md` §2c). |
| E11 | Showstopper | AI state lies outside snapshot and hash; `CreateSave()` throws; AI seed is in no header | `SkirmishMatchSession.cs`, `SkirmishAi.cs` | 24 | open **Phase 23 (anticipation, does not change the finding):** the original AI built as an option (`ScriptedSkirmishSession`, `docs/SKIRMISH-AI-ORIGINAL.md`) keeps its whole state from the beginning in state, hash and savegame of its session; for the built-in `SkirmishAi` – the default – E11 is open unchanged, and its behaviour was changed in Phase 23 (standing army order, exchange-value rule, work for builders driving out) and received new state in the process (`_armyOrder`, `_sightings`), which like the rest lies outside snapshot and hash. |
| E12 | Showstopper | Commands are checked against the current world when submitted and hashed immediately; time and order of submission must be the same on all machines | `SimulationWorld.Submit`/`Validate` | 24, 25 | open |
| E13 | Showstopper | Command path answers synchronously and assigns the current tick; display of the rejection hangs on it | `ICommandSink`, `CommandModeController` | 24 | open |
| E14 | Showstopper | `LiveGame.Advance` owns the time (accumulator, 8 ticks per frame, local pause/speed) | `LiveGame.cs` | 24 | open |
| E15 | Bottleneck | Match input incomplete: no teams/colours/start positions/AI strength in `MatchSetup`, human fixed as player 1, random faction from shell randomness | `LiveMatch.cs`, `SkirmishScreens.cs` | 24, 25 | open |
| E16 | Showstopper | No network code, no protocol, no identifier of the simulation state | – | 25, 26 | open |
| E17 | Bottleneck | Snapshot is JSON, 485–776 bytes per object (10–12 MB in the stress test), unmeasured for transfer; restoring inserts objects in a different order than the running game | `docs/SIMULATION-CORE.md`, D3 | 18, 27 | **partly (Phase 18):** enumeration now in ID order (`EntityTable`, D3); snapshot measured: 3.2 MB with 155 objects (Twilight Flame, 8 players), 9.0 MB with 2,249 objects (`MD_GLA05`), writing 9–43 ms, reading 23–164 ms; transfer open (Phase 27) |
| E18 | Bottleneck | Without a tool a desync cannot be traced back to object and field | – | 27 | open |
| E19 | Risk | Open UDP port: flood, forged senders, malformed packets, eavesdropping | – | 26 (cookie, parser), 28 | open |
| E20 | Bottleneck | Garbage collection and long frames make a client stall; in lockstep it must be able to catch up | E4 | 19, 23, 26 | **measured (Phase 18):** 95 KB allocation per tick and 2 gen-2 collections in 54,000 ticks (8 players); `MD_GLA05` 4 MB per tick, 7 gen-2 collections in 9,000 ticks; reduction in Phase 19 **Phase 19:** 52 KB per tick, no gen-2 collections in 54,000 ticks (8 players); `MD_GLA05` 10 KB per tick; outliers up to 94 ms (skirmish) and 152 ms (first mission tick) remain. **Phase 23 (performance gate at 60 Hz):** around 8 KB per tick (8 players; at 30 Hz 17 KB), no gen-2 collections in 108,000 ticks; outliers up to 141–143 ms in the skirmish, 101–128 ms in the missions. A client that hits such a tick falls back by around eight ticks at 60 Hz (calculated: 141 ms against 16.7 ms per tick) and must catch up (catch-up speed 26-fold). |
| E21 | Limit | Mission: presentation reports back to the script VM (`CompletePresentation`, `SelectNamed`, `SignalUiInteraction`), `<Local Player>` per machine, `_selected` not hashed | `MissionPresentation.cs`, `ScriptEngine*.cs` | – (campaign stays local; documented in Phase 24) | deliberate |
| E22 | Limit | Map reveal not preventable in lockstep | §2.8 | – | deliberate |
| E23 | Bottleneck | User interface: multiplayer button disabled, no room, no displays in the game; interpolation only for position and hull rotation | `docs/GAME-SHELL.md`, `FrameProjector.cs` | 29 | open |
| E24 | Risk | Nothing from Phases 15–17 is accepted by a human; a network game on top of a game with unverified playability conceals causes | `CLAUDE.md` | 17, 29, 30 | open **Phase 23:** open unchanged. On 8 October 2026 the project owner watched an AI match in spectator mode at 30 Hz (four observations, three corrections from them, `docs/AI-OBSERVED-2026-10-08.md`); at 60 Hz no human has seen or heard the game. |

### 4a. Go-ahead after Phase 18 (6 October 2026)

**The programme is continued as planned**, with two conditions.

- *In favour:* on the measured machine the computation is independent of JIT, GC, culture and operating system (13 matrix rows identical, `docs/DETERMINISM.md`); the yardstick exists and passes twice in a row; all nine findings of the search run are worked off; the tick-rate inventory is checked (1,959 places in eight classes, `docs/TICKRATE-AUDIT.md`).
- *Condition 1 (E1):* the run on a second physical machine with a different CPU is outstanding (`docs/DETERMINISM.md` §4). It is to be made up before Phase 26 (first network game between machines); until then determinism across machine boundaries counts as *probable, not verified*.
- *Condition 2 (E4):* performance is the actual bottleneck. Without Phase 19 an 8-player skirmish at 60 Hz costs 30 % of one core with outliers up to 547 ms per tick, and a large mission 76–81 %. Phase 19 therefore stays before Phase 20; its target "mean ≤ 1.0 ms" requires around a factor of 5 and according to the profile is only reachable if movement/pathfinding (`AIUpdateInterface`, 81 % of the update time) goes down – vision and hash alone yield at most one third.
- *From the inventory for Phases 20–23 (contradictions to the plan, `docs/TICKRATE-AUDIT.md` summary):* "same balance at every rate" holds only for the catalogue rounding, not for round-downs, one-tick minima and N+1 counters at run time; literals that mean order instead of time must not be scaled; mixed recurrences (drag, drag formation, MOAB) need more than square roots; the presentation today intervenes in the simulation through `StandingOrders` and the mission director; `SaveGame` writes no rate.

## 5. Phase overview

| Phase | Content | Changes reference values? | Gate at the end |
|---|---|---|---|
| 17 | Playability (running, unchanged `docs/handoffs/HANDOFF-PHASE17.md`) | yes (rule corrections) | – |
| 18 | Measurement basis: reference manifest, determinism across machine boundaries, performance profile, checked inventory | only named determinism corrections | go-ahead for the programme |
| 19 | Performance: tick costs of real matches | no | targets reached or rest named |
| 20 | Tick rate as a parameter – simulation | no (byte-identical at 30 Hz) | manifest unchanged |
| 21 | Tick rate as a parameter – presentation, game layer, UI, sound, tools, tests | no (image-identical at 30 Hz) | checksums unchanged |
| 22 | Fixed point Q39.24 | **yes, once** | result comparison explained |
| 23 | Time-step invariance, new acceptance tests, 60 Hz as the default, new survey | **yes** (60 Hz) | rate equivalence and performance gate – **done on 9 October 2026 (`e8632bb`, `ea52f87`)**: suite 0 / 0, manifests at 60 Hz; the performance gate except for maximum and allocation; in addition three behaviour changes at 30 Hz with their own manifests; rest and open decisions in §7a |
| 24 | Session core: AI in the state, savegame, match description, command path, tick source | no (at delay 0) | reference values unchanged |
| 25 | Protocol, room, relay in the process; local skirmish over the network path; AI slots | no | multi-client sample hash-identical |
| 26 | Dedicated server, UDP, time synchronisation, adaptive buffers, 8 clients | no | network matrix measured |
| 27 | Desync diagnosis, rejoining, spectators, server replay | no | rejoining hash-identical |
| 28 | Server operation and security | no | fuzzing, capacity, package |
| 29 | Multiplayer in the client, latency perception, acceptance with humans | no | checklist by humans |
| 30 | Patch integration, quality, shippable completion (postponed) | yes (adopted fixes) | package, acceptance |

Dependencies: 18 → 19 → 20 → {21 ∥ 22} → 23 → 24 → 25 → 26 → 27 → 28 → 29 → 30. Only 21 and 22 may run in parallel (separate worktrees, merge 21 before 22). Phase 24 does not depend on 60 Hz in subject matter, but comes after it so that its reference values are not surveyed twice.

Why the tick rate before the network: rate and fixed point change every hash and every format. Developing network code against a simulation whose reference values still change three times would mean conducting every desync search against a moving target.

## 6. Rules for all phases of the programme (in addition to `CLAUDE.md`)

1. **Measure first, then change.** Every phase begins with the reproduced starting state; no number from this document or a handoff counts as measured.
2. **The reference manifest is the yardstick** (`baselines/`, from Phase 18 on). A phase that is announced as result-identical ends with an unchanged manifest. Whoever has to change it does so in a commit of its own with a single cause, before/after values and justification.
3. **One cause per reference change.** Never two result-changing things in one commit; otherwise no shift can be attributed any more.
4. **Determinism holds across machine boundaries.** New simulation inputs go through the checked conversion routines; nothing culture-, runtime- or CPU-dependent reaches the state.
5. **No wall-clock time in the simulation**, not even indirectly: no load distribution by time, no feedback of the presentation into a network match.
6. **Nothing falls back silently.** Unknown messages, versions, fingerprints, rates: counted, reported, rejected.
7. **Formats:** incompatible changes raise the version; old states are rejected with a clear message. Until the first release there is no obligation to migrate, after that every format break is a decision of the project owner.
8. **Verified headless is not accepted.** What only a human can judge (look at 60 Hz, game feel with delay, network play with real players, a second machine) is reported as open until it has taken place.
9. **Continue existing handoffs.** At the end of every phase the brief of the next one is *not rewritten*; instead its section "Starting point" is updated with the measured state; deviations from the plan (postponed or new items) are named there and in §5.
10. Line endings, git rules, background runs with a timeout, rebuilding release tools: as in `CLAUDE.md`.

## 7. Open decisions of the project owner

Since 10 October 2026 the one consolidated list of the open decisions is `docs/plan/PROGRAMME.md` §7 (D-1 to D-22; it
carries the items below and those of §7a with their defaults and due dates). This section and §7a are kept as the record of
their origin and are not extended any more.

The briefs each name a default so that nobody is blocked; the decision stays with the project owner.

| Question | Default in the plan | due before |
|---|---|---|
| Should the programme begin before the completion of Phase 17? | no; Phase 18 (pure measurement) may be brought forward | 18 |
| Rule for slow clients: how long to throttle, when to disconnect? | display up to 2 s, throttle up to 10 s, then disconnect | 26 |
| What happens to the objects of a player who has dropped out? | look up the behaviour of the original in the reference source code, propose, have it decided | 26 |
| How long is a seat held for rejoining? | 5 minutes | 27 |
| Spectator delay | 120 s | 27 |
| AI strengths (the original knows three, the AI here one) | one strength, choice counted instead of obeyed | 24 |
| Server under Linux as a delivery target? | yes (costs little, because the server needs no data and no Stride) | 28 |
| Target platform and acceptance | **decided on 9 October 2026: Windows only for the time being.** Acceptance runs (build, tests, manifests, audits) count under Windows; Linux comes into consideration later only as host of the dedicated server. Every earlier demand "and under Linux (WSL)" is thereby informative and no longer an acceptance criterion (`docs/DETERMINISM.md` §5, `CLAUDE.md` "Git rules"). At the same time: the repository is public (`origin` = `https://github.com/cpfaffinger/CnCNew.git`, branch `master`, `GPL-3.0-or-later`), without game data. | – |
| Should 30 Hz remain selectable in the finished game? | no (test rate only) | 23 |
| Does the default stay at 30 Hz if the rate-equivalence suite does not pass after two measurement rounds? | **decided on 8 October 2026: no.** The abort criterion of Phase 23 is lifted; the suite is to be brought to pass and 60 Hz becomes the default. What lies on the way is fixed, including defects at 30 Hz (one cause each, own commit, rewritten manifest). State and remaining causes: `docs/TICKRATE-MIGRATION.md` §15.4 | 23 |
| Second machine and fellow players for the acceptances | to be provided by the project owner | 18, 26, 29 |
| *From Phase 23 (written out in §7a):* self-acquisition once per original frame of the unit, likewise `PointDefenseLaserUpdate`, `CleanupHazardUpdate`, `DeployStyleAIUpdate.AllowsFire` (§7a no. 1) | provisionally built and merged this way; with it whole matches are equal between the rates | 24 (before Phase 24 surveys reference values) |
| Factory exit "always through the gate" (§7a no. 2) | not merged; a unit with an immediate command leaves the building by the shortest route | – |
| Lowering the tick maximum with a behaviour change (§7a no. 3) | not done; 141–143 ms at 60 Hz | 26 |
| The two faster hash constructions (§7a no. 4) | not implemented | – |
| Command check against the map bounds (§7a no. 5) | not built | 27/28 |
| Solo bonuses of the campaign (§7a no. 6) | not implemented | – |
| Draw order of the roads (§7a no. 7) | unchanged, depends on the rate | – |
| Upper limit of a standard match of one hour of game time (§7a no. 8) | unchanged, also for a human | – |
| Original skirmish AI as the default and its selection in the client (§7a no. 9) | no: the default is the built-in AI, the original AI is an option of the tools (`ai-match --ai original --difficulty …`) | 24 |
| Assessing the balance after the AI corrections (§7a no. 10) | not assessed | – |
| Second physical machine (§7a no. 11) | open | 26 |

### 7a. Decisions from Phase 23 (9 October 2026)

The same list is in `docs/TICKRATE-MIGRATION.md` §15.7.7, in `CLAUDE.md` (paragraph "Phase 23") and in the addendum of `docs/handoffs/HANDOFF-PHASE24.md`. "Provisionally" means: this is the state on `main`; the decision lies with the project owner.

1. **Self-acquisition once per original frame of the unit.** A unit decides its own attack in the first tick of its own frame. The brief of Phase 23 forbade the escape "original grid" (Ausweg „Originalraster“) for target acquisition; it is provisionally done this way because the equality of whole matches was not reachable otherwise. Cost: a unit that becomes free through a command in the middle of the frame begins its own target search up to 1 tick (60 Hz) or 3 ticks (120 Hz) later. Alternative: decide per tick – then matches differ between the rates, and only the distribution comparison holds. The same question for `PointDefenseLaserUpdate`, `CleanupHazardUpdate` and `DeployStyleAIUpdate.AllowsFire`.
2. **Factory exit "always through the gate, even with an immediate command".** Built and measured, not merged: shifts the balance (America 46 instead of 61 wins of 80, China 32 instead of 23; at that time 10 instead of 12 of the 16 runs decided); the patch lies in the scratchpad of the session.
3. **Lowering the tick maximum** (141–143 ms at 60 Hz in the performance gate): needs a behaviour change (shared group path, cell budget per tick or a different search) – reserved by the project owner.
4. **The two faster hash constructions** (vision row by row; object hash with module hashes) – reserved, unchanged.
5. **Command check against the map bounds** (targets beyond around 524,000 units make the movement overflow) – reserved for Phase 27/28, unchanged.
6. **Solo bonuses of the campaign** – reserved, unchanged.
7. **The draw order of the roads depends on the rate** (`TerrainLayer.CreateRoads` gives every road mesh the same bounds; no correction reproduces every 30 Hz image).
8. **A standard match ends after one hour of game time**, also for a human – is that intended?
9. **The original skirmish AI (three difficulty levels) as the default AI** and its selection in the client and in spectator mode: per tick it costs around 27 times the built-in AI (1.1 ms for 8 players), and it lacks parts of the C++ core; the default is the built-in AI.
10. **The balance after the AI corrections is not assessed** (ChinaTankGeneral wins 70 of 80 matches against AirForce in the 30 Hz survey of round 3).
11. **The second physical machine** (before Phase 26) – open unchanged.

**Decided by the project owner on 10 October 2026:** no. 1 – self-acquisition stays **once per original frame of the unit**; the state on `master` is no longer provisional, and whole matches staying equal between the rates is the intended property (the late start of the target search by up to 1 tick at 60 Hz and 3 ticks at 120 Hz is accepted). No. 9 – the **built-in `SkirmishAi` stays the default AI**; the original skirmish AI remains a selectable option, and Phase 24 builds the session core with the AI state as an opaque hashed block. Nos. 2 to 8, 10 and 11 are open unchanged.

## 8. Terms

- **Tick:** one step of the simulation; duration 1/rate.
- **Original frame:** 1/30 s, the time unit of the original; unit of many data and constants.
- **Command intent:** what a player wants, without tick and sequence; the server (or locally the session) makes a command for a tick out of it.
- **Tick bundle:** all commands of the humans and of the system for one tick, distributed by the server to everyone.
- **Lockstep:** all machines execute the same commands in the same tick and reach the same state.
- **Relay:** server that orders and distributes commands without simulating.
- **Session hash:** state hash of the world plus the AI states; basis of the hash reports.
- **Desync:** two machines have different session hashes for the same tick.
- **Reference manifest:** file with the target results of fixed runs; replaces the hashes in the documentation as the yardstick.
- **Rate equivalence:** the same scene yields the same game quantities at 30, 60 and 120 Hz within declared tolerances.
- **Catch-up speed:** how much faster than real time a client can compute; limits rejoining and recovery after a stall.

> **Stage:** 4 – Multiplayer · **Milestone:** S4-M1 Protocol, room, relay in the process · **Phase:** 25
> **Status:** done on 11 October 2026 – `84a9459` (simulation identifier in the description) with manifest `e0d6e01`, `64d632b` (protocol, loopback, client, relay server), `5f2406a` (lockstep driver, embedded server, local skirmish over the relay, multi-client probe), `0b93f31` (the setup as room, direct path removed); result in `docs/NETWORK-PROTOCOL.md`
> **Predecessor:** HANDOFF-PHASE24.md · **Successor:** HANDOFF-PHASE26.md · **Results in:** docs/NETWORK-PROTOCOL.md (new), docs/GAME-SHELL.md, docs/ARCHITECTURE.md
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 25): Protocol, room (lobby) and relay server in the same process – the local skirmish runs via the network path, AI slots included

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`** (decisions, target architecture, register of bottlenecks, rules for all phases), `docs/ARCHITECTURE.md`, `docs/SIMULATION-CORE.md` (session state, replay), `docs/PRESENTATION-INPUT.md` (command intent), `docs/GAME-SHELL.md` (skirmish setup) and the final report of Phase 24.
Prerequisite: Phase 24 is completed – AI state in the session hash, savegame, `MatchDescription`, two-stage command path, `ITickDriver`, lockstep probe with artificial delay.

## Honest status (starting point)

There is **no network code** in the repo: no project, no protocol, no dependency. After Phase 24 the session is built so that it can be driven by tick bundles, but the only driver with delay is a probe in the same object. The skirmish setup of the shell builds the game directly (`LiveMatch.Create`); there is no room that anyone could join.

### Starting point after Phase 24 (11 October 2026, state `e96d93f`; re-measure)

This section continues the starting point above with what Phase 24 left; where the two differ, this one is newer. Log of Phase 24: `docs/SIMULATION-CORE.md` §45 (AI state, session hash, match description, session state, replay, pre-check), `docs/PRESENTATION-INPUT.md` §13 (command path, tick source, lockstep probe), `docs/CAMPAIGN-AND-SAVEGAMES.md` §8 (skirmish savegame); register E11 to E15 in `docs/MULTIPLAYER-PLAN.md` §4.

**The prerequisite holds:** the AI state is in the session hash and the savegame, `MatchDescription` exists, the command path has two stages, `ITickDriver` releases the ticks, and the lockstep probe plays with an artificial delay. **None of it has been seen or played by a human**, with or without a delay.

**What exists, by name** (so that this phase transports it instead of rebuilding it):

- **`MatchDescription`** (`CnCNew.Simulation.Skirmish`): immutable, one binary encoding (`Encode`/`Decode`, magic `CNCMATCH`, encoding version 1), fingerprint = SHA-256 of the bytes. Fields: command table version and world state version, tick rate, map path, terrain fingerprint, definition fingerprint, `Seed`, `AiSeed`, `MaxTicks` (ticks of its rate), start money, grace and science interval (original frames), slots (kind closed/human/computer/spectator; faction or empty = random; team; colour or -1; start position or -1; AI strength 0 to 2; army composition or -1). `Resolve(catalog, terrain, definitionFingerprint)` decides random factions and positions from `Seed` and numbers the players in slot order. **For step 2 of this brief:** the description's "simulation version" is the two format versions only; the commit identifier with a "modified" flag is not in it yet - adding a field changes the encoding version and every description fingerprint (not the reference manifest's hashes other than `commandStreamSha256`, which covers the replay bytes with the description in them).
- **`SkirmishMatchSession`**: `Start(description, catalog, terrain, mapObjects, checkpointInterval, definitionFingerprint)`; `StateHash()` = the **session hash** (world, every `SkirmishAi`, victory tick, winner, end tick, sequence counters) - what a hash report of this phase carries; `Save()`/`Restore()` (format `cncnew-skirmish-state` 1: description, counters, AI blocks, the recording so far, world; checked against the session hash); `Issue(player, intent)` submits at the current tick with the player's next sequence number. A room that starts from a savegame (step 7) has all three parts in that one document.
- **Replay format 14**: the header carries the description. `ReplayRunner.Run` plays a bare world from every recorded command; `SkirmishReplay.Play` plays the match with the computer players deciding again and audits their recorded commands (first deviation with tick and player) - the tool for "server recording playable as a replay with the same final hash" (step 8), and a model for the desync report.
- **`CommandPath`** (`CnCNew.Presentation.Input`): `Issue(player, intent)` → `CommandReceipt` (number, pre-check by `SimulationWorld.Check`); `SubmitDue()` submits the bundle of the world's tick, by player then receipt number, immediately before the step; `Resolved` reports `CommandResolution` (receipt, command as submitted, pre-check, final answer; `Deviates`). **`ICommandScheduler`** is the seam for this phase: `Schedule(command, now)`, `IsPresent(tick)`, `Take(tick)`, `DelayTicks`. The local one executes at tick + delay. A networked scheduler sends the intent with its receipt number to the server and fills `Take` from the bundles; note that an intent is a function (`Func<ulong, long, SimCommand>`), so the client encodes the command it builds with a placeholder tick and sequence (the server stamps both) through the simulation's command codec (`ReplayFormat.EncodeCommand` is the existing encoder; a decoder for one command is `ReadCommand`, private so far).
- **`ITickDriver`** (`CnCNew.Game.Live`): `AddTime`, `TryReleaseTick(tick, bundlePresent)`, `RequestPause`, `RequestSpeed`, `LimitBacklog`, `Fraction`, `Stalls`. `LiveGame.TickDriver` can be replaced; `LiveGame.Paused`/`Speed` are requests. The lockstep driver of step 6 implements this interface and `ICommandScheduler` together.
- **Still direct:** `SkirmishSessionCommandSink` and `MissionCommandSink` (headless drivers that step a session themselves; step 9 removes the first for the client), `LiveMatch.Create` (builds the session in the process), and the skirmish setup screen, which passes faction and start money only - its team, colour and start position boxes are not read (`MatchSetup.Details` exists and reaches the description; nothing fills it but tests).

**Measured at this state** (`docs/SIMULATION-CORE.md` §45.6): tier 4, 36 of 36 steps ok (0 warnings; all test programs; the simulation, game and presentation tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite 0 / 0; the long run; the three client checks through the new command path; `check-match`; `session-check` 19 of 19; the staged scenes). Campaign at 60 Hz: reachability clean 24/24 and 24/24, audit 24/24 with end probes 13/24 and 15/24, objective driver victory 24/24 and defeat 24/24. Performance, the whole `scripts/perf-profile.ps1` before and after in the same hour, every run with the same final hash on both sides: means within -1.5 % and +3.0 % (8 players, 30 game minutes: 850 against 837 µs per tick, 99 % 2.2 against 2.1 ms, maximum 140 against 149 ms, allocation 8.5 KB per tick). The eight computer players together cost 32.8 µs per tick on average, 219 µs at 99 %, 33.7 ms at most in one tick; a save takes at most 25 ms, a restore 49 ms. **Not run:** determinism matrix, Linux, the survey and distribution comparison, the commands-only agent, 120 Hz for the pairings, `session-check` at 30 and 120 Hz.

**The yardstick** (`baselines/reference-manifest.json` at `63ac97c`, 60 Hz): unchanged in every result quantity since Phase 31 - retail reference match FactionGLA at tick 32,520, final hash `6052051714309B17`, chain `84077720F511C1ED`; stress test `9EE9F730D124C3BD` / `F338463BE66D99C0`; 16 pairings 14 decided. Changed by Phase 24: `replayFormatVersion` 14 and the 22 `commandStreamSha256` values (one cause: the description in the replay header). Versions: `CommandTypes.Version` 16, replay **14**, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3; new: match description encoding 1, skirmish session state 1, `SkirmishSaveGame` 1. Compatibility baseline 713.

**What Phase 24 did not do, and what it means here:**

- **AI strength is counted, not obeyed** (the built-in AI has one strength); the original AI's three strengths exist only in `ScriptedSkirmishSession`, which is started from its own options, not from a `MatchDescription`. A room that offers AI strengths must either map them or say that they are ignored.
- **The local player is the first slot** of a `LiveMatch` (player 1, camera at its start position). The description does not say who is local, as intended, but `LiveMatch` still assumes it; a client that joins as player 3 needs that parameter (step 7).
- **The thinking times of eight AIs are not fully offset** (at most two of eight think in one frame; `docs/SIMULATION-CORE.md` §45.1): measured, not changed, because it changes every run with more than four players.
- **`ScriptedSkirmishSession` with a slot of the built-in AI cannot be saved** (not converted).
- **A save holds the whole recording** (3.5 MB for eight players after five minutes); a rejoin by snapshot (Phase 27) will want the state without it.
- **Commands carry unchecked targets** (decision D-5 / no. 5, Phase 27/28): a `GroupMoveCommand` checks its unit count and its units, not its target; unchanged.
- **Tier rule:** the command path, the session hash, the session state, the match description and the replay are cross-cutting (`docs/TESTING.md` §4, §8): tier 4 before a push that touches them. Tier 4 now contains `check-match` and `session-check`.

## Decisions that are fixed (`docs/MULTIPLAYER-PLAN.md` §2)

- **One match path.** The local skirmish (human against AI) runs via the same module as a network match: embedded server, loopback transport, one client. The old direct path is removed at the end of this phase.
- **Relay.** The server assigns command intents to ticks and distributes bundles; it does not simulate and needs no game data.
- **AI slots** are in the match description; every client lets the AIs run along in its simulation (Phase 24). Of an AI the server only knows that the slot is occupied.
- **New projects and their dependencies** (names may differ, the rules may not):
  - `src/CnCNew.Net` – messages, encoding, `ITransport`, loopback transport, lockstep driver of the client. References only `CnCNew.Simulation` (command codec, match description, replay). No Stride, no `CnCNew.Content`, no `CnCNew.Game`.
  - `src/CnCNew.Server` – room and relay logic as a library. References only `CnCNew.Net`. Never loads game data.
  - `tests/CnCNew.Net.Tests`, `tools/CnCNew.NetTool`.
  - `CnCNew.Game` references `CnCNew.Net` and `CnCNew.Server` (for the embedded server). `CnCNew.Simulation` still references only `CnCNew.Data`.
  - A source/reference test enforces these rules and that wall-clock time in `CnCNew.Server`/`CnCNew.Net` is read only via an injected clock.

## Brief (in this order)

1. **Reproduce the starting state**, re-measure the reference values of Phase 24.
2. **Simulation version.** Two clients with the same data but different simulation code are certain to diverge. An identifier of the simulation (commit hash embedded at build time together with a "modified" flag as assembly metadata, in addition the format versions) belongs in the match description and the handshake. Local development states with "modified" may play only via loopback, unless an explicit developer switch allows it (counted, visible in the log).
3. **Protocol (write `docs/NETWORK-PROTOCOL.md` first, then build).** Versioned binary messages, little endian, length-prefixed, each with an upper limit:
   - Handshake: protocol version, simulation version, `CommandTypes.Version`, replay format, definition fingerprint; rejection with cause.
   - Room: state of the room (the server is always authoritative), slot request, ready, start with `MatchDescription` (seed chosen by the server), loading progress, "all loaded", chat.
   - Match: command intent (receipt number of the client, payload in the command codec of the simulation), tick bundle (tick, commands with player ID and sequence stamped by the server, also empty), hash report (tick, session hash), pause/resume, player dropped, desync, end.
   - Unknown or oversized messages are counted and discarded; nothing falls back silently to a default.
4. **Transport interface and loopback.** `ITransport` delivers messages reliably and in order per connection. `LoopbackTransport` in the process with configurable delay, jitter and reordering between *different* connections, from its own test seed (never the logic random stream).
5. **Relay and room logic (`CnCNew.Server`).** State machine room → loading → match → end. Tick clock via an injected clock; two operating modes:
   - **server-paced** (more than one human): the server closes ticks by its clock with tick rate × speed and waits for nobody;
   - **client-paced** (exactly one human, local skirmish): the tick is closed as soon as the one client requests it. This way the single-player skirmish has no additional delay, and pause, game speed and the fast forward of the tests remain possible.
   The server stamps the player ID from the connection (nobody can send commands for someone else), limits intents per tick and player, compares hash reports and records all bundles.
6. **Lockstep driver of the client (`ITickDriver` from Phase 24).** Collects bundles, releases tick N when bundle N is present, submits its commands immediately before the step, sends hash reports, assigns results to the receipt numbers. In this phase a fixed playback buffer (configurable); the adaptive control comes in Phase 26.
7. **Local skirmish via the network path.** `LiveMatch` starts embedded server, loopback and driver. The skirmish setup of the shell becomes the room (local, one human): slots with faction, team, colour, start position, AI slots – everything travels via the match description into the game. Save/load (Phase 24) is kept for the local skirmish: a room can start from a savegame (description + session snapshot + hash).
8. **Multi-client probe in the same process.** `tools/CnCNew.NetTool loopback-match --map … --humans H --ais A --ticks T [--delay … --jitter …]`: H clients, each with its own world, against one server via loopback. The "humans" are command generators: a `SkirmishAi` that computes only on this one client and sends its commands as intents via the server – this puts load on the real command path. After every tick the session hashes of all H worlds are compared.
   - Runs: 2, 4 and 8 clients; mixes 1+7, 2+6, 4+4, 8+0 (humans+AI) on at least three 8-player maps; with and without delay/jitter; twice each (repeatability).
   - Every run: all hashes identical over all ticks, server recording playable as a replay with the same final hash.
9. **Remove the old path.** `SkirmishSessionCommandSink` with direct access and every second way of starting a game are dropped; tools (`SimTool ai-match`, `reference-match`, `skirmish-sweep`) may run headless without a server directly on the session (they are the reference), the client may not.
10. **Docs:** `docs/NETWORK-PROTOCOL.md` (new), `docs/MULTIPLAYER-PLAN.md`, `docs/GAME-SHELL.md`, `docs/ARCHITECTURE.md` (new layers and rules), README, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md` (layer rules, commands, reference values).

## Bottlenecks and showstoppers of this phase

- **Time of submission.** Pending commands enter the hash; a bundle may enter the world only immediately before its tick (test: bundles arrive at different clients at different times, hashes stay the same).
- **Order within a bundle.** The world executes the commands of a tick in a fixed order (player, sequence); AI commands (generated locally) and human commands (from the bundle) must end up in the same overall order on every client. Define the rule and test it (a human and an AI command the same object in the same tick, or compete for the same money).
- **Start at tick 0.** Everything that happens before the first tick (start objects, warehouses, random faction/position) must follow from the match description alone – no client state, no enumeration order, no wall-clock time.
- **The local skirmish must not get worse.** Measurement before/after: time from command intent to execution in client-paced operation (target: the same tick as before this phase), tick cost with and without the network path.

## Out of scope

Real sockets, clock synchronisation, adaptive buffers, standalone server process (Phase 26); rejoin, spectators (Phase 27); user interface for joining over the network (Phase 29).

## Acceptance

The local skirmish runs exclusively via `CnCNew.Net`/`CnCNew.Server`, with reference values identical to Phase 24 (client-paced, without delay). The multi-client probe is hash-identical for all the mixes named. The dependency rules are enforced by a test; the server runs without game data. The final report names numbers with commit hashes and what is not verified. At the end check `docs/handoffs/HANDOFF-PHASE26.md` and update the "starting point".

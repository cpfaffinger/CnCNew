> **Stage:** 4 – Multiplayer · **Milestone:** S4-M1 Protocol, room, relay in the process · **Phase:** 25
> **Status:** planned
> **Predecessor:** HANDOFF-PHASE24.md · **Successor:** HANDOFF-PHASE26.md · **Results in:** docs/NETWORK-PROTOCOL.md (new), docs/GAME-SHELL.md, docs/ARCHITECTURE.md
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 25): Protocol, room (lobby) and relay server in the same process – the local skirmish runs via the network path, AI slots included

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`** (decisions, target architecture, register of bottlenecks, rules for all phases), `docs/ARCHITECTURE.md`, `docs/SIMULATION-CORE.md` (session state, replay), `docs/PRESENTATION-INPUT.md` (command intent), `docs/GAME-SHELL.md` (skirmish setup) and the final report of Phase 24.
Prerequisite: Phase 24 is completed – AI state in the session hash, savegame, `MatchDescription`, two-stage command path, `ITickDriver`, lockstep probe with artificial delay.

## Honest status (starting point)

There is **no network code** in the repo: no project, no protocol, no dependency. After Phase 24 the session is built so that it can be driven by tick bundles, but the only driver with delay is a probe in the same object. The skirmish setup of the shell builds the game directly (`LiveMatch.Create`); there is no room that anyone could join.

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

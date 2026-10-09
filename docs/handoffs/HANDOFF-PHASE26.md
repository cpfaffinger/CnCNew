N+1 handoff (Phase 26): Dedicated server and real transport – UDP, clock synchronisation, adaptive buffers, eight clients over the network

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`** (decisions, target architecture, register of bottlenecks, rules for all phases of the programme), `docs/ARCHITECTURE.md`, `docs/NETWORK-PROTOCOL.md` (created in Phase 25) and the final report of Phase 25.
Prerequisite: Phase 25 is completed – a local skirmish runs exclusively via `CnCNew.Net`/`CnCNew.Server` with the loopback transport, and the multi-client test in the same process (2, 4, 8 clients, AI slots mixed) yields tick-by-tick identical hashes on all clients. If that is not verified, this phase does not begin.

## Honest status (starting point)

After Phase 25 the protocol exists, but **not a single byte has left a machine yet**. Everything ran over queues in the same process with a clock set by hand. Therefore unverified are: the behaviour under delay, jitter and packet loss, the clock synchronisation between server and client, the bandwidth demand, the behaviour of a client that does not keep up with the tick rate, and whether eight real processes stay in sync for hours. The latency numbers in `docs/MULTIPLAYER-PLAN.md` §3 are calculated, not measured.

## Decisions that are fixed (`docs/MULTIPLAYER-PLAN.md` §2)

- The server is a **relay**: it does not simulate, loads no game data and waits for no client when closing a tick.
- The transport is **UDP with its own delivery guarantee**, exclusively with the BCL (`System.Net.Sockets`). No new NuGet dependency without a verified justification.
- Wall-clock time exists only in `CnCNew.Net`/`CnCNew.Server` (pacing, clock synchronisation). It never reaches the simulation; the simulation is driven by tick bundles alone.

## Brief (in this order)

1. **Reproduce the starting state.** Build, all test programs, re-measure and record the reference values of Phase 25 (local skirmish via the network path, multi-client loopback).
2. **UDP transport (`CnCNew.Net`, behind `ITransport`).**
   - Connection setup with a stateless cookie: the server creates state only once the client has sent the cookie back (no amplification attack, no memory for forged senders).
   - Two channels over one connection: *reliable ordered* (lobby, control, command intents) with sequence, acknowledgement and retransmission; *urgent* for tick bundles: every packet carries all bundles that the receiver has not acknowledged yet (bounded, counted), so that a single loss costs no waiting time.
   - Fragmentation of large messages (match description, later snapshot) with an upper size limit; MTU-safe packet size (assumption 1,200 bytes of payload, justify in the docs).
   - Keep-alive, timeout, clean disconnect. Every number (retry interval, time limits) is configuration with a documented default.
   - The parser is strict: every field with length and value range check, unknown message types are counted and discarded, never silently interpreted.
3. **Network simulator for real sockets.** `tools/CnCNew.NetTool proxy`: a UDP intermediary with configurable delay, jitter, loss, duplication and reordering per direction, reproducible from a seed. All measurements of this phase run with it; a third-party tool is not needed.
4. **Clock synchronisation and pacing.**
   - The client estimates the offset to the tick clock of the server (ping/pong, moving median) and plays tick N as soon as bundle N is present *and* its playback time is reached.
   - The playback buffer per client is adaptive: target size from the measured arrival jitter (e.g. 99 % quantile), kept in milliseconds, not in whole ticks. Corrections by slightly speeding up or slowing down the playback rate (a few percent at most), never by jumps.
   - Catching up: a client that is behind computes several ticks per frame (limit from Phase 23/24) until it reaches the target distance.
5. **Dedicated server as a program (`tools/CnCNew.DedicatedServer`).** One process, one UDP port, several simultaneous rooms (lobby + match), connections are assigned to the room. Configuration from a JSON file and the command line (port, maximum rooms, maximum players per room = 8, tick rate default, time limits). Console output as structured lines. The server references neither `CnCNew.Content` nor `CnCNew.Game`, `CnCNew.Presentation` or Stride; a source/reference test enforces that (model: `SourceRules`).
6. **Rule for slow and disconnected clients.** The server closes ticks by its clock. For a client that falls behind or loses the connection, a documented, configurable rule applies (proposal as default, the decision lies with the project owner, `docs/MULTIPLAYER-PLAN.md` §7): only display a lag of up to 2 s; above that throttle the game speed for everyone for up to 10 s; after that disconnect. The dropping out of a player is a **system command in the tick bundle** (new command type, increase `CommandTypes.Version`) that every simulation executes identically; what happens to his objects is fixed there and tested.
7. **Measurements (every number from a run, quiet machine).**
   - Eight real client processes (headless, commands from a seed-driven command generator) against the server process via the proxy, for every line of the matrix: delay 10/40/80/150 ms one way, jitter 0/10/30 ms, loss 0/1/3 %. Result per line: hashes of all clients identical (yes/no), time from command intent to execution (median, 95 %, 99 %), buffer level, number of stalls, bytes per second and client, retransmissions.
   - Endurance run: eight clients, 60 minutes of game time, 40 ms / 10 ms / 1 %: no desync, no memory growth.
   - Two machines in the LAN (needs the human and a second machine; do not claim it if it did not take place).
   - Comparison with the calculation in `docs/MULTIPLAYER-PLAN.md` §3: name deviations.
8. **Docs:** `docs/NETWORK-PROTOCOL.md` (packet format, channels, state machines, all time limits), `docs/DEDICATED-SERVER.md` (start, configuration, rules), `docs/MULTIPLAYER-PLAN.md` (update register and measured values), README, ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md` (commands, reference values).

## Bottlenecks and showstoppers of this phase

- **Hash includes pending commands.** `SimulationWorld.Submit` counts every accepted command into the state hash immediately (`_commandSum`). A client may therefore submit the commands of a bundle only immediately before the step of its tick, never on arrival. The test "bundles arrive in any order and at any time, hashes stay identical" is mandatory.
- **Garbage collection and frame rate.** A client that stalls because of a garbage collection or a long frame must not slow anyone down; it catches up. The catch-up speed from Phase 23 is the hard limit for that.
- **Clock drift.** Two machines do not run equally fast; the playback control must compensate drift without oscillating (test with an artificially skewed clock).
- **Operating system pacing.** `Thread.Sleep` has coarse resolution under Windows; the tick clock of the server needs a documented, measured solution (deviation of the tick intervals as a histogram in the report).

## Out of scope

Rejoin, spectators and desync diagnosis (Phase 27); securing and operating the open port (Phase 28); user interface (Phase 29). Encryption belongs to Phase 28, but the cookie handshake already belongs here.

## Acceptance

Eight client processes play a complete match via the dedicated server with mixed human and AI slots; all hashes are identical over the whole matrix; the latency table is measured; a slow and a disconnected client do not slow down the others beyond the documented rule. The final report names numbers with commit hashes and explicitly what is not verified (in particular every test that would have needed a second machine or a human). At the end check `docs/handoffs/HANDOFF-PHASE27.md` and update the "starting point" section with the measured state, do not rewrite it.

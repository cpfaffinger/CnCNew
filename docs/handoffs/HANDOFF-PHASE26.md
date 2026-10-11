> **Stage:** 4 – Multiplayer · **Milestone:** S4-M2 Dedicated server, UDP, clock synchronisation, eight clients · **Phase:** 26
> **Status:** current – starting point continued on 11 October 2026 after Phase 25 (section "Starting point after Phase 25"), not begun; needs the second physical machine (D-10)
> **Predecessor:** HANDOFF-PHASE25.md · **Successor:** HANDOFF-PHASE27.md · **Results in:** docs/NETWORK-PROTOCOL.md, docs/DEDICATED-SERVER.md (new)
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 26): Dedicated server and real transport – UDP, clock synchronisation, adaptive buffers, eight clients over the network

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`** (decisions, target architecture, register of bottlenecks, rules for all phases of the programme), `docs/ARCHITECTURE.md`, `docs/NETWORK-PROTOCOL.md` (created in Phase 25) and the final report of Phase 25.
Prerequisite: Phase 25 is completed – a local skirmish runs exclusively via `CnCNew.Net`/`CnCNew.Server` with the loopback transport, and the multi-client test in the same process (2, 4, 8 clients, AI slots mixed) yields tick-by-tick identical hashes on all clients. If that is not verified, this phase does not begin.

## Honest status (starting point)

After Phase 25 the protocol exists, but **not a single byte has left a machine yet**. Everything ran over queues in the same process with a clock set by hand. Therefore unverified are: the behaviour under delay, jitter and packet loss, the clock synchronisation between server and client, the bandwidth demand, the behaviour of a client that does not keep up with the tick rate, and whether eight real processes stay in sync for hours. The latency numbers in `docs/MULTIPLAYER-PLAN.md` §3 are calculated, not measured.

### Starting point after Phase 25 (11 October 2026, state `71f477e`; re-measure)

This section continues the starting point above with what Phase 25 left; where the two differ, this one is newer. Log of Phase 25: `docs/NETWORK-PROTOCOL.md` (the protocol, the room, the match, the client, the local skirmish, the measurements of §10), `docs/SIMULATION-CORE.md` §46, `docs/PRESENTATION-INPUT.md` §13.6, `docs/GAME-SHELL.md` §14; register E12, E15, E16 in `docs/MULTIPLAYER-PLAN.md` §4.

**The prerequisite holds:** a local skirmish runs exclusively through `CnCNew.Net`/`CnCNew.Server` over the loopback (a layer rule forbids the client every other way), and the multi-client probe in one process - 1, 2, 4 and 8 clients, the mixes 1+7, 2+6, 4+4 and 8+0 on three eight-player maps, with and without delay and jitter, twice each: 48 runs - is hash-identical on all clients after every tick, with the server's recording replaying into the same session hash. **Not a single byte has left the process**, and no human has seen a match through the relay in a window.

**What exists, by name** (so that this phase puts a socket under it instead of rebuilding it):

- **`CnCNew.Net`** (references `CnCNew.Simulation` only): `Protocol` (version 1, the limits), 25 message records with `MessageCodec.Encode`/`TryDecode` (never throws for what a peer sent; `Frame` puts a `u32` length in front for a byte stream), `CommandWire` (the simulation's command codec on the wire; `Stamp` writes player and tick), `BuildIdentity` (commit and modified flag from assembly metadata written by `CnCNew.Net.csproj`), `INetClock`/`ManualNetClock`, **`IConnection`** (whole messages, reliable and ordered per connection: `Send`, `TryReceive`, `IsOpen`, `Close`) and `LoopbackNetwork` (delay and jitter per direction from its own generator), `LockstepClient` (handshake, room, bundles in order; counts what it discards).
- **`CnCNew.Server`** (references `CnCNew.Net` only; a layer rule forbids game data, file access, the simulation's types other than the match description's namespace, and any clock, timer or thread of its own): `RelayServer(clock, RelayOptions)` with `Attach(connection)` and `Pump()` - single-threaded, pumped by its host. Room → loading → match → ended; server-paced by the injected clock in microseconds, client-paced with exactly one member; `Recording`; `Discarded`, `IntentsDeferred`, `IntentsDropped`, `LastAgreedTick`, `Desync`.
- **`CnCNew.Game.Lockstep`**: `LockstepDriver` (`ITickDriver` and `ICommandScheduler` in one; `PlaybackBufferTicks` is a **fixed** buffer - the adaptive control of this phase replaces it; server-paced it has no interpolation fraction and releases ticks as bundles arrive, without a clock of its own), `LocalRelay` (the embedded server of a local game), `LoopbackProbe` (the multi-client probe).
- **`tools/CnCNew.NetTool`**: `loopback-match`, `probe-suite`, `local-cost`, `identity`. `tests/CnCNew.Net.Tests`: 17 tests at 30, 60 and 120 Hz.

**For this phase in particular:**

- **A transport implements `IConnection`.** The protocol assumes whole messages, reliable and ordered per connection; over UDP that is this phase's reliability layer. Nothing above the connection changes. A real clock implements `INetClock` - there is none in the tree yet, and the layer rule of `Net.Tests` (no `DateTime`, `Stopwatch`, `Timer`, `Thread`, `Task` in `CnCNew.Net` and `CnCNew.Server`) will name the one file that reads it as its exception.
- **The server waits for nobody** in a server-paced room, and a client releases a tick when its bundle is there: a client slower than the tick rate falls behind without limit, and nothing measures or reports it. The tick maximum of one client (140 to 149 ms in the eight-player profile, decision D-3) is such a moment.
- **Modified builds:** the handshake rejects a development build unless `RelayOptions.AllowModifiedBuilds` (the embedded server sets it). The standalone server needs the developer switch the Phase 25 brief names; the first member's identity defines the room's.
- **Pause and speed** in a server-paced room are granted to whoever asks; there is no rule yet (Phase 29).
- **Hash reports** are sent every tick (`RelayOptions.HashInterval` 1): 14 bytes per tick and client. The interval is a parameter of the match start.
- **Limits not yet enforced as policy:** an oversized or malformed message is counted and dropped, the connection stays; a flood beyond 256 queued intents of a player is dropped and counted. Closing or banning is Phase 28.
- **Player numbering** follows the slots that take part, in slot order (`MatchDescription.Resolve` and the server apply the same rule); a client learns its number from `MatchStart`. `LiveMatch` can view from any slot (`MatchSetup.LocalSlot`), but `LiveMatch.Create` always opens its own embedded server - joining a foreign server needs a second entry (this phase or 29).

**Measured at this state** (`docs/NETWORK-PROTOCOL.md` §10, §11): tier 4 with 40 of 40 steps ok (4,615 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite 0 / 0; the long run; the three client checks, which now play through the relay; `check-match`; `session-check` 19 of 19; `net-probe`; the staged scenes). Campaign at 60 Hz (missions stay local): reachability clean 24/24 and 24/24, audit 24/24 with end probes 13/24 and 15/24, objective driver victory 24/24 and defeat 24/24 - as before the phase. Performance of the simulation, the whole `scripts/perf-profile.ps1` before (`e96d93f`) and after in the same hour, every run with the same final hash on both sides: means within -2.4 % and +1.6 % (8 players, 30 game minutes: 839 against 847 µs per tick, 99 % 2.1 ms on both sides, maximum 140 against 145 ms, allocation 8.5 KB per tick).

**The yardstick** (`baselines/reference-manifest.json` at `e0d6e01`, 60 Hz): unchanged in every result quantity since Phase 31 - retail reference match FactionGLA at tick 32,520, final hash `6052051714309B17`, chain `84077720F511C1ED`; stress test `9EE9F730D124C3BD` / `F338463BE66D99C0`; 16 pairings 14 decided. Changed by Phase 25: 20 `commandStreamSha256` values (one cause: match description encoding 2). Versions: protocol **1**; match description encoding **2**; skirmish session state **2**; replay 14, `CommandTypes.Version` 16, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, `SkirmishSaveGame` 1. Compatibility baseline 713.

**Still open from before and due before this phase:** the second physical machine with a different CPU and .NET version (register E1, decision D-10) - the reference manifest has run on one machine only, and this phase's acceptance (eight real processes in sync) presupposes it.

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

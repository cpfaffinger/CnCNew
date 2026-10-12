> **Stage:** 4 – Multiplayer · **Milestone:** S4-M3 Desync diagnosis, rejoin, spectators, server replay · **Phase:** 27
> **Status:** done on one machine on 12 October 2026 (`a8aa667`, `9875e18`, `2e74381`, `9d680fc`, `c0440ee`, `d42886f`, `db5a236`, `90b20ec`, `6ea31ae`, `8f6f94f`); votes not built; results in `docs/NETWORK-PROTOCOL.md` §18 and §19 and `docs/NETWORK-DIAGNOSTICS.md`; what it left is the "Starting point after Phase 27" of `HANDOFF-PHASE28.md`
> **Predecessor:** HANDOFF-PHASE26.md · **Successor:** HANDOFF-PHASE28.md · **Results in:** docs/NETWORK-DIAGNOSTICS.md (new), docs/NETWORK-PROTOCOL.md, docs/DEDICATED-SERVER.md
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 27): Robustness on the network – desync diagnosis, rejoin, spectators, server replay

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md`, `docs/SIMULATION-CORE.md` (snapshot, state hash, replay) and the final report of Phase 26.
Prerequisite: Phase 26 is completed (eight client processes via the dedicated server, latency table measured).

## Honest status (starting point)

After Phase 26 a match runs as long as nothing goes wrong. For the three cases that are certain to occur in operation there is nothing yet:

- **Desync.** The server compares hashes and detects a deviation, but nobody can say *which object on which machine from which tick on* deviates. Without this tool every desync report of a player is worthless.
- **Connection loss.** A disconnected player drops out (Phase 26); he cannot come back.
- **Spectators and recording.** The server has all tick bundles but does not write them as a replay; spectators do not exist.

Verified numbers on the snapshot (old, re-measure): the world snapshot is JSON (`JsonState`), around 485–776 bytes per object, in the stress test 10–12 MB with 13,220 objects, restore 0.7 s (`docs/SIMULATION-CORE.md`). For the transfer to a rejoining client this is unmeasured.

### Starting point after Phase 26 (12 October 2026, state `f25fa7a`; re-measure)

This section continues the starting point above with what Phase 26 left; where the two differ, this one is newer. Log of Phase 26: `docs/NETWORK-PROTOCOL.md` §12 (the UDP
transport), §13 (clock synchronisation, playback, pacing), §14 (members that fall behind, leaving), §15 (the tools), §16 and §17 (measurements); `docs/DEDICATED-SERVER.md`;
`docs/SIMULATION-CORE.md` §47 (the leave command); register E1, E16, E19, E20 in `docs/MULTIPLAYER-PLAN.md` §4.

**The prerequisite holds on one machine:** eight client processes played whole matches through the dedicated server and a UDP proxy with identical hashes on every line of
the matrix, and the latency table is measured. **It does not hold across machines:** no second physical machine has taken part (E1, D-10), nothing has crossed a cable, and
no client with a window has joined a server.

**What exists, by name** (so that this phase builds on it instead of beside it):

- **`CnCNew.Net.Udp`**: `UdpFormat` (the datagram layout), `UdpLink` (one connection as a state machine without a socket: the reliable byte stream of framed messages and the
  urgent messages that ride along until acknowledged; `IConnection`), `UdpClientEndpoint` and `UdpServerEndpoint` (the stateless cookie handshake; `TryAccept`,
  `TryTakeClosed`), `UdpSocketChannel`, `DatagramImpairer` and `MemoryDatagramNetwork` (loss, duplication, reordering from a seed - for tests and the proxy). **A large
  message is simply a long stretch of the reliable stream** (window 256 segments of 1,159 bytes, no congestion control): a snapshot for a rejoin goes that way, and its
  transfer time on a slow line is unmeasured. `UdpOptions.MaxMessageBytes` is 64 MiB.
- **Which channel:** urgent are bundles and pongs downward, intents, hash reports, pings, tick requests and the end report upward; everything else is reliable. **Order
  holds per channel only** - whatever this phase adds must not depend on its position between two bundles unless it travels in a bundle (the leave of a player does).
- **`CnCNew.Net`**: protocol **version 2** (29 messages; `Hello` carries the room name), `ClockSync` (median of ping samples of the server's game time), `PlaybackControl`
  (the playback position follows the server's clock by a measured delay; steered by rate), `LockstepClient` (given a clock it pings; `Sync`, `CompletedTick`, `Lag`,
  `Leave()`), `SystemNetClock` and `FineTimer` in `RealTime.cs`, the one file of the network layers that reads real time.
- **`CnCNew.Server`**: `RelayServer` (the lag rule: `RelayOptions.LagReportMs`/`LagThrottleMs`/`ThrottleLimitMs`/`ThrottleSpeedPercent`; `TryTakeEvent`; `TickIntervals`;
  `GameMicros`, `EffectiveSpeedPercent`; a leave becomes a `PlayerLeaveCommand` of the next bundle with origin connection 0), `RoomDirectory` (rooms by name behind one
  endpoint). **A dropped member is gone:** its `Peer` is removed and its player leaves the match by command - a seat held for a rejoin (this phase) has to change exactly
  that: when the leave command is issued, and what the room remembers of the member.
- **`CnCNew.Game.Lockstep`**: `HeadlessClient` (protocol client, world, driver, command path, command generator; `KeepHashes`/`HashChain`), `LoopbackProbe` (loopback or UDP
  in memory; `ProbeDisturbance`: leave, cut, freeze, slow, hitch), `LockstepDriver` (server-paced: `Playback`, `BehindTicks`, `Stalls`; without a clock a tick is due when its
  bundle is there). **A watcher is a member without a seat** (player 0): it receives the same bundles at the same time as the players - the delayed spectator of this phase
  is new.
- **`tools/CnCNew.DedicatedServer`** (one port, rooms by name, JSON and command line, one line per event) and **`tools/CnCNew.NetTool`**: `proxy`, `client` (a client
  process; can leave, vanish, freeze, be slow, hitch), `net-match` and `net-matrix` (the harness), `pacing`; `scripts/check-net-match.ps1` is the step `net-match` of the
  gate. **The server records every bundle in memory** (`RelayServer.Recording`) and writes nothing: the server replay of this phase is a file of what is already there.

**For this phase in particular:**

- **Hash reports** are one per tick and client (14 bytes, three copies: about 6 KB/s upward). The partial sums of the brief make every report larger; the interval
  (`RelayOptions.HashInterval`, the server's `hashInterval`) is the lever, and its effect on how late a desync is noticed is to be measured.
- **The desync path as it is:** the relay ends the match at the first tick two reports differ for and sends every reported hash of that tick (`Desync`); nothing more.
- **The rule's open points** (D-17): a client that can only keep up with the slowed game was carried for 14.4 s in three incidents; a clean close after the end of a match
  is logged as `ConnectionLost`.
- **The window client cannot join a server:** `LiveMatch.Create` opens its own embedded relay. A second entry (`LiveMatch.Join` over a `UdpClientEndpoint`, with the driver
  given a `SystemNetClock`) is small - `HeadlessClient` shows every step - and is needed at the latest for Phase 29.
- **Tests that measure real time** belong in `NetTool` (they need a quiet machine); `Net.Tests` run on a clock set by hand, except two tests on loopback sockets with hard
  time limits. Do not write source files while a tier of `scripts/test.ps1` runs: its steps build when they are reached.

**Measured at this state** (`docs/NETWORK-PROTOCOL.md` §16 and §17): the matrix 36 of 36 lines hash-identical (eight client processes, 100 s each; delay 10/40/80/150 ms,
jitter 0/10/30 ms, loss 0/1/3 %); order to execution twice the delay plus 7 to 19 ms on a quiet line, up to plus 65 ms with 30 ms of jitter; 6 to 10 KB/s per client and
direction; the server's ticks 98.7 % within 1 ms. A whole standard match of 60 game minutes (216,000 ticks, eight client processes, 40 ms / 10 ms / 1 %): one hash chain on all clients, the server agreed on every tick, 90,631 orders, order to execution median 116 / 99 % 166 ms, never slowed, nobody dropped; the server's memory grows by about 20 MB per hour (it keeps the recording). Tier 4 with 41 of 41 steps ok (4,573 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite 0 / 0; the long run; the client checks; `check-match`; `session-check`; `net-probe`; `net-match`; the staged scenes) - on the second run: the first found a test that assumed 60 Hz (`f25fa7a`). `NetTool probe-suite` 48 of 48. Campaign at 60 Hz (missions stay local): reachability clean 24/24 and 24/24, audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and 15/24, objective driver victory 24/24 and defeat 24/24 - as before the phase. Performance of the simulation, the whole `scripts/perf-profile.ps1` before (`71f477e`) and after: the same final hashes, means within -1.0 % and +1.2 % (8 players: 849 against 841 µs per tick, maximum 137 against 139 ms).

**The yardstick** (`baselines/reference-manifest.json` at `4cbf535`, 60 Hz): unchanged in every result quantity since Phase 31 - retail reference match FactionGLA at tick
32,520, final hash `6052051714309B17`, chain `84077720F511C1ED`; stress test `9EE9F730D124C3BD` / `F338463BE66D99C0`; 16 pairings 14 decided. Changed by Phase 26: the two
version fields and 22 `commandStreamSha256` values (one cause: command table 17 and replay format 15 for the leave command). Versions: protocol **2**, transport **1**;
`CommandTypes.Version` **17**, replay **15**; match description encoding 2, skirmish session state 2, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3,
`SkirmishSaveGame` 1. Compatibility baseline 713.

**Still open from before:** the second physical machine with a different CPU and .NET version (E1, D-10). A machine-only substitute for part of it is a second runtime
environment on the same hardware: the Windows-built binaries run unchanged under Linux, so a server or a client under WSL can take part in a match with Windows clients -
it needs the .NET 10 runtime in the WSL distribution (a download; the owner's word).

## Brief (in this order)

1. **Reproduce the starting state**, re-measure the reference values of Phase 26.
2. **Desync diagnosis.**
   - Besides the total hash, the hash report of the clients carries the partial sums that the world keeps separately anyway (objects, timers, pending commands, random streams, per AI player the AI state from Phase 24, script state). At the first deviating tick the server reports *which partial sum* deviates on *which client*.
   - On a desync report every client immediately writes a diagnostics package: match description, all received tick bundles as a replay, its hash chain, a snapshot of the current tick and a table "object ID → object hash". None of it contains original data.
   - `tools/CnCNew.NetTool desync-diff <paket-a> <paket-b>`: replays the replay locally, finds the first deviating tick against both hash chains and names the deviating objects and fields (snapshot comparison field by field).
   - Test: a deliberately built-in error (test switch that changes a field of an object on one client) is found by the tool exactly down to tick, object and field.
3. **Snapshot for the transfer.** Measure first: size and write/read time of the session snapshot (world + AI) in the late 8-player match, raw and with `BrotliStream` (BCL). If the compressed JSON snapshot is below the limits set in the plan (proposal: ≤ 2 MB, writing + compressing ≤ 150 ms, reading ≤ 1 s), the format stays; otherwise a compact binary format with its own version field and the same fields (test: JSON and binary snapshot produce the same hash).
4. **Rejoin.**
   - At checkpoint ticks the server knows the hash confirmed by everyone. A client that rejoins receives the snapshot of a checkpoint **from a fellow player via the server**, loads it, computes the hash and compares it with that of the server – a wrong or manipulated snapshot is detected this way without the server having to simulate. After that the client replays the bundles since the checkpoint at catch-up speed.
   - Fallback path without a donor: replaying all bundles from tick 0 (only sensible as long as the catch-up speed from Phase 23 manages it in acceptable time; the limit is in the docs).
   - Recognition via a session token from the handshake; the deadline within which a seat is kept free is configuration. The rule for the dropped player from Phase 26 is extended accordingly (seat held / finally dropped), as system commands in the bundle.
   - Tests: disconnect and rejoin at three points in time (early, middle, late) with proxy disturbances; hashes identical afterwards; a corrupted donor snapshot is rejected.
5. **Spectators.** A spectator is a client without a player seat: it receives the same bundles delayed by a configurable period (default e.g. 120 s, against eavesdropping by fellow players), joins via the same snapshot path and sends no commands (the server discards them, counted). The display without an own player (fog, selection) is the subject of Phase 29; here the headless proof is enough.
6. **Server replay.** At the end of a match (also after an abort) the server writes the match description and all bundles in the replay format; `SimTool` and the client play the file and reach the hashes of the clients. AI commands are not in the bundle (the AI runs in the simulation, Phase 24); the replay therefore contains the AI slots of the match description, and playback lets the AI run along.
7. **Pause and votes.** Pause on request (number and duration limited), resume; everything as system commands or server state, none of it touches the simulation outside the bundles.
8. **Docs:** `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md`, new `docs/NETWORK-DIAGNOSTICS.md` (diagnostics package, tool, procedure for a desync report), `docs/MULTIPLAYER-PLAN.md`, README, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md`.

## Bottlenecks and showstoppers of this phase

- **Completeness of the snapshot.** A rejoin uncovers every gap in the snapshot that a match running straight through never shows (state that lives only in memory). The test "load the snapshot at every checkpoint of a 60-minute match and continue playing, hash stays identical" runs for all factions.
- **Cost of the checkpoint.** Writing a snapshot must not make the donor stall visibly; measure the time per checkpoint and set it against the tick budget. If necessary, write the snapshot spread over several ticks from an immutable copy – never from the running world.
- **Hash report rhythm.** Too rarely: a desync is noticed late. Too often: bandwidth. The rhythm is measured and documented.

## Acceptance

A built-in desync is found exactly down to tick, object and field. A disconnected client rejoins and is hash-identical afterwards. A spectator follows a match with a delay. The server writes a replay that reproduces the hashes. The final report names numbers (snapshot sizes and times, rejoin duration per point in time, bandwidth of the hash reports) with commit hashes and what is not verified. At the end check `docs/handoffs/HANDOFF-PHASE28.md` and update the "starting point".

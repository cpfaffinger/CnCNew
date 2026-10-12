> **Stage:** 4 – Multiplayer · **Milestone:** S4-M4 Server operation and security · **Phase:** 28
> **Status:** current – starting point continued on 12 October 2026 after Phase 27 (section "Starting point after Phase 27"), not begun
> **Predecessor:** HANDOFF-PHASE27.md · **Successor:** HANDOFF-PHASE29.md · **Results in:** docs/DEDICATED-SERVER.md, docs/NETWORK-PROTOCOL.md
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 28): Server operation and security – open port, abuse limits, encryption, package, sustained load

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md`, `docs/NETWORK-DIAGNOSTICS.md` and the final report of Phase 27.
Prerequisite: Phase 27 is completed.

## Honest status (starting point)

The dedicated server works under benevolent conditions. But a program with an open UDP port on the internet is addressed by strangers before the first player arrives. So far there is: a cookie handshake and a strict parser (Phase 26). What is missing: any limit against overload, any authentication of the packets after the handshake, any protection of a room password, a package that someone can start without a development environment, and any statement about how many rooms a server carries.

### Starting point after Phase 27 (12 October 2026, state `8f6f94f`; re-measure)

This section continues the starting point above with what Phase 27 left; where the two differ, this one is newer. Log of Phase 27: `docs/NETWORK-PROTOCOL.md` §18 (a
held seat and the rejoin, watchers, the recording as a file, the rules of the pace, the window client joining a server) and §19 (measurements);
`docs/NETWORK-DIAGNOSTICS.md` (a desync named; the hash interval; what a state costs); `docs/DEDICATED-SERVER.md` §2, §3, §6, §8; `docs/DETERMINISM.md` §2d;
`docs/SIMULATION-CORE.md` §48; register E1, E17, E18 in `docs/MULTIPLAYER-PLAN.md` §4; decisions D-10, D-17, D-19, D-23 in `docs/plan/PROGRAMME.md` §7.

**The prerequisite holds on one machine.** A match survives a lost connection, a late watcher and a broken client, and says what broke. No second physical machine
has taken part and nothing has crossed a cable (E1, D-10); what exists instead is a second operating system on the same CPU.

**What exists, by name** (so that this phase builds on it instead of beside it):

- **Protocol version 3** (34 messages): `Hello` carries a rejoin token and a watch flag, `Welcome` the token; `DesyncDetail`, `DesyncReport`, `SnapshotRequest`,
  `SnapshotOffer`, `SeatState`; reject reasons `SeatNotHeld`, `CannotCatchUp`. Transport version 1 unchanged. **The token is the only thing that proves "I sat here"** -
  64 bits, sent in clear text in both directions. Whoever reads the line can take a seat that is held, and can supersede a connection that stands. That is exactly what
  the authenticated connection of this phase (item 5) has to close: after the key exchange the token must travel encrypted, or be replaced by the session key.
- **`RelayServer`** is two files (`RelayServer.cs`, `RelayServer.Robustness.cs`). New options: `LagRecoveryMs`, `SeatHoldMs`, `SpectatorDelayMs`, `AllowLateWatchers`,
  `CatchUpLimitTicks`, `CatchUpLimitMs`, `PausesPerPlayer`, `MaxPauseMs`, `HostSetsSpeed`. Every member has a position in the recording (`NextBundle`); the server
  sends from the recording, at most 512 ticks ahead of what a member has completed. **What the room keeps in memory and never limits:** the recording (about 20 MB per
  hour; 3.3 MB as a file), the hash every tick was agreed on for the last 30 s plus the spectator delay, one state per joiner while it is passed on (up to 64 MiB by
  the protocol - the largest measured was 775 KB). The "memory cap per room" of item 3 means these.
- **A state is passed on unread**: a member's `SnapshotOffer` of up to 64 MiB is held and forwarded when its hash is the agreed one. A member can make the server hold
  64 MiB per joiner by offering garbage; the limit of item 3 belongs here (the measured need is under 1 MB).
- **`RoomDirectory.TryTakeFinished`** hands a finished room to the host once; `tools/CnCNew.DedicatedServer` writes `<room>-<n>.replay` and a note with the last
  agreed tick and hash (`--replay-dir`), and has the options `--seat-hold-ms`, `--spectator-delay-ms`, `--allow-late-watchers`, `--lag-recovery-ms`,
  `--pauses-per-player`, `--max-pause-ms`, `--anyone-sets-speed`. The file name is built from the room name (letters, digits, `-`, `_`; 40 characters); the
  directory is never left. Retention of these files is nobody's yet (item 6).
- **`CnCNew.Game.Lockstep`**: `SessionTransfer` (the session state packed with Brotli; refuses what unpacks to more than four times the protocol's limit and reports
  damaged input as invalid data), `SessionDiagnostics` (a client's ring of hash parts and object hashes; the package), `DesyncPackageDiff`, `RemoteRelay` (socket,
  transport, protocol client, system clock), `HeadlessClient` (rejoin token, watch, `CorruptAtTick`, `DamageNextSnapshot`), `LoopbackProbe` (disturbances `Reconnect`,
  `Corrupt`, `DamageSnapshot`; late watchers). `LiveMatch.Join(data, remote, viewport)` is the window client's entry to a server; the client's options are `--join
  ADDRESS:PORT --room NAME --seat N | --watch`.
- **`tools/CnCNew.NetTool`**: `client --rejoin-at-tick/--rejoin-after-ms/--corrupt-at-tick/--diagnostics/--watch`; `net-match --disturb seat:rejoin|corrupt:tick`,
  `--late-watchers N --late-after-seconds S`, `--expect-desync`, `--linux server,2,4` (processes under WSL), `--rate-kbps N --queue-kb N` (a narrow line in the
  proxy); `desync-diff`, `replay-check`, `snapshot-cost`. Every process states its operating system.
- **Checks:** `scripts/check-net-robustness.ps1` (step `net-robust`), `scripts/check-net-join.ps1` (`net-join`), `scripts/check-net-cross-os.ps1` (`net-cross-os`,
  tier 4; **NOT RUN** with exit code 3 where WSL or its runtime is missing - a step for something the machine lacks is shown as not run, never as passed; use the
  same for whatever this phase cannot run everywhere).

**For this phase in particular:**

- **Linux is proven as far as one machine can:** the server and clients built under Windows run unchanged under Linux in WSL (`Debian`, .NET runtime 10.0.12 in
  `~/.dotnet`, installed with the owner's word for this purpose - nothing else was installed), the reference manifest passes there, and mixed matches agree. Item 7
  asks for a self-contained `linux-x64` package: build it under Windows and run it in the same distribution; do not install an SDK there.
- **No congestion control** (item 3 names limits, the transport none): a state of 90 KB on a line of 256 kbit/s takes 6 s per leg, fills the line's queue and delays
  the orders of the member that sends it (95 % within 0.83 s instead of 0.2 s). Bytes per second per connection as a limit must not throttle a state below what a
  rejoin needs, or must pace it.
- **The lag rule's weak points are closed as configuration** (`LagRecoveryMs` 30 s; a close after the end is `Closed`); the test "maliciously slow client" of this
  phase's bottlenecks starts from there: a member that stays just below `LagThrottleMs` is never slowed for, one that recovers for less than 30 s uses up one 10 s.
- **Still unchecked target coordinates** (D-5): a command with a target beyond about 524,000 units makes a movement overflow on every client alike - no desync, but an
  exception in every simulation. It was reserved for Phase 27 or 28 and was not touched.
- **Hash reports are four fifths of a client's upstream** (7.9 of 9.9 KB/s); an interval of 4 costs three ticks of detection and saves 6 KB/s (proposal, D-23).
- **Votes are not built** (D-23); "kicking by the room creator" of item 6 is the first thing of that kind.
- **The 24-hour run of item 8** cannot run in one piece where a background run ends after two hours: run it as pieces of under two hours against one server process
  that stays up (started detached, its log the intermediate result), and say how many hours were reached.

**Measured at this state** (`docs/NETWORK-PROTOCOL.md` §19): the matrix 36 of 36 lines hash-identical (median of order to execution +0.3 ms against Phase 26),
12 of 12 at 30 and 120 Hz and on a second map; a standard match of 60 game minutes with rejoins at minute 10, 30 and 55 and a late watcher - nine of nine clients in
the same state, the three back in time after 0.6, 1.2 and 1.6 s, the server's replay (3.3 MB) played to the agreed hash; a rejoin at minute five on five lines: 0.4 s
(40 ms / 10 ms / 1 %), 2.1 s (150 / 30 / 3), 1.7 s (80 / 30 / 10), 4.8 s (1 Mbit/s), 21 s (256 kbit/s). **Tier 4 with 44 of 44 steps ok** at `8f6f94f` (5,194 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite; the long run; the client checks; `check-match`; `session-check`; `net-probe`; `net-match`; the new `net-robust`, `net-join` and `net-cross-os`; the staged scenes). `NetTool probe-suite` 48 of 48. Campaign at 60 Hz (missions stay local): reachability clean 24/24 and 24/24, audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and 15/24, objective driver victory 24/24 and defeat 24/24 - as before the phase. Performance of the simulation, the whole `scripts/perf-profile.ps1` before (`f25fa7a`) and after: the same final hashes, means within -2.4 % and +2.2 % (8 players: 832 against 834 µs per tick, maximum 139 against 146 ms).

**The yardstick** (`baselines/reference-manifest.json` at `4cbf535`, 60 Hz): untouched by Phase 27 - no manifest commit. Retail reference match FactionGLA at tick
32,520, final hash `6052051714309B17`, chain `84077720F511C1ED`; stress test `9EE9F730D124C3BD` / `F338463BE66D99C0`; 16 pairings 14 decided. Versions: protocol
**3**, transport 1; `CommandTypes.Version` 17, replay 15, match description encoding 2, skirmish session state 2, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame`
3, `SkirmishSaveGame` 1; desync package 1. Compatibility baseline 713.

**Still open from before:** the second physical machine with a different CPU (E1, D-10) - no machine-only substitute is left for it; the decisions D-17 to D-19 and
D-23; no lobby screen (Phase 29); no human has seen a match through a server.

## Decisions that are fixed

- **No anti-cheat against map reveal.** In the lockstep model every client knows the full state; that is a documented limit of the model (`docs/MULTIPLAYER-PLAN.md` §2), not a task of this phase. What the server can provide: no client can send commands for another player (the server assigns the player ID per connection), the server determines the game speed, every simulation rejects invalid commands in the same way, modified state shows up as a desync.
- **BCL cryptography only** (`ECDiffieHellman`, `AesGcm` or `ChaCha20Poly1305`, `HMACSHA256`). No cryptography of our own, no new dependency.
- **The server distributes no original data** and no maps. A room names map and fingerprints; whoever does not have them cannot join.

## Brief (in this order)

1. **Reproduce the starting state**, re-measure the reference values of Phase 27.
2. **Write the threat model** (`docs/DEDICATED-SERVER.md`, its own section): attackers without a connection (flood, forged senders, amplification), attackers with a connection (message flood, oversized or malformed messages, lobby abuse, deliberate stalling), eavesdroppers on the network. Per line: measure, test, residual risk.
3. **Limits and throttling.** Packets per second and sender before the handshake; messages and bytes per second per connection; command intents per tick and player; maximum message and fragment sizes; connections per address; rooms and players per server; memory cap per room (bundle buffer for spectators and rejoin). Every limit is configuration with a documented default; every violation is counted and leads to a defined reaction (discard, disconnect), never to unbounded memory or computing effort.
4. **Fuzzing of the parser.** A test program (`tests/CnCNew.Net.Tests`, suite `Fuzz`) sends random and deliberately corrupted packets generated from a seed (truncated, length fields above the limit, swapped types, old sequences, repetitions) against the server and client parser: no exception leaves the parser, no state changes because of an invalid packet, memory does not grow. A corpus of the cases found remains as a regression test.
5. **Authenticated and encrypted connection.** Session key per connection from an ECDH exchange in the handshake; all packets after that with AEAD (sequence number as nonce, protection against replay). Room password only as challenge-response (`HMACSHA256` over a server nonce), never in clear text. The server owns a permanent key pair; the client shows its fingerprint on first contact and remembers it (trust on first use). Document honestly what this protects against (eavesdropping, forging from outside the path) and what it does not (active attacker at the very first contact).
6. **Administration.** Configuration file, ban list (address/key), room password, kicking by the room creator; log as structured lines with configurable retention. IP addresses are personal data: the default is truncated storage, the docs say so.
7. **Package.** `dotnet publish` of the server self-contained for `win-x64` and `linux-x64`; the server has no Windows or Stride dependency (test via the project references). Start and configuration guide in `docs/DEDICATED-SERVER.md`. Prove the Linux run (WSL or a container is enough; do not claim it if it did not take place).
8. **Capacity and sustained load.** Headless clients (command generator from a seed) fill N rooms of eight players each: measure CPU, memory and bandwidth per room, from that a capacity table; 24-hour run with constantly restarting matches, disconnects and rejoins: no memory growth, no crash, no hanging room. The run needs a bounded timeout and intermediate results (see `CLAUDE.md`, background runs).
9. **Version rule in operation.** What must match when joining (protocol version, `CommandTypes.Version`, replay format, simulation version from Phase 25, definition and map fingerprint) and how the client explains a deviation to the human is in the docs; the server never offers a "join anyway" path.
10. **Docs:** `docs/DEDICATED-SERVER.md`, `docs/NETWORK-PROTOCOL.md`, `docs/MULTIPLAYER-PLAN.md`, README, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md`; extend `scripts/audit-vulnerable.ps1` by the server project.

## Bottlenecks and showstoppers of this phase

- **Cost of the encryption** per packet with eight clients and 60 bundles per second: measure, do not assume.
- **Nonce reuse** on retransmission: every transmission needs a fresh packet sequence, even if the content is the same (test).
- **Time limits as attack surface:** a client that stays exactly below every limit must not hold up a room permanently (test "maliciously slow client" against the rule from Phase 26).

## Out of scope

Ranking, accounts, matchmaking, central server list, NAT traversal (clients connect outbound to a server with an open port), kernel anti-cheat. Whoever wants that decides it as a scope change of its own.

## Acceptance

The fuzz run finds no crash and no memory growth; the limits demonstrably take effect; the connection is authenticated and encrypted; the server runs as a standalone package under Windows and Linux; the capacity table and the 24-hour run are measured. The final report names numbers with commit hashes, the residual risk per line of the threat model and what is not verified. At the end check `docs/handoffs/HANDOFF-PHASE29.md` and update the "starting point".

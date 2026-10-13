# Stage 4 – Multiplayer

As of: 10 October 2026, state `2777a90`. Frame: [PROGRAMME.md](PROGRAMME.md). The technical frame – decisions, target
architecture, latency model, register of bottlenecks E1 to E24, rules – is `docs/MULTIPLAYER-PLAN.md`; the briefs are
`docs/handoffs/HANDOFF-PHASE25.md` to `-29.md` (planned, with their "Starting point" to be continued at the end of the
respective previous phase). **Nothing of this stage is built.** Its preparatory work (measurement basis, performance,
tick rate, fixed point, 60 Hz) is Stage 2 and is done; the session core (Phase 24) is the gate into this stage.

## 1. Goal, architecture, the owner's wording

Skirmishes with up to eight players (humans and AI slots mixed) through a dedicated server; the local skirmish runs over
the same path with an embedded server; a slow player does not hold up the others.

**Decided architecture (`docs/MULTIPLAYER-PLAN.md` §2):** lockstep through a **relay**. What travels is commands, not
state; every client computes the full world; the server keeps the tick clock, assigns every command intent to the next
open tick, sends one bundle per tick to everyone (also an empty one) and **waits for nobody when closing a tick**; it does
not simulate and loads no game data (cheap, legally clean, portable). The AI runs in the simulation of every client and is
part of the hashed session state; the server knows of an AI only that its slot is occupied. One match path: local
skirmish = embedded server + loopback + one client, client-paced; network match = the same code with UDP, server-paced.

How the owner's requirements map onto it:

| Requirement (10 October 2026) | In the architecture |
|---|---|
| "The client gets its data from the server" | The client gets the **tick bundles** (the commands of all humans and of the system per tick) from the server and computes the state itself. **Decided (D-11, 10 October 2026):** commands stay the match path; the **hybrid** is a co-simulating server as an option (§1a, S4-M6) that uses its own state for rejoin, arbitration and spectating, never for driving the match. |
| "The server does the lockstep so that a slow player does not hold up all the others" | Server-paced tick clock; a client that is behind catches up (measured catch-up speed 26-fold at 60 Hz); the rule for slow and disconnected clients (display up to 2 s, throttle up to 10 s, then disconnect – default, D-17) and the drop as a system command in the bundle. |
| "Tick rates" | The rate is a value of the world and of the match description (Stage 2); 60 Hz is the product rate; two machines play together only with the same rate (§2.7). |
| "Client-server architecture" | `CnCNew.Net` (messages, transport, lockstep driver), `CnCNew.Server` (room and relay logic, no game data), `tools/CnCNew.DedicatedServer`; layer rules in §2.4 and enforced by a source test. |

### 1a. Commands, state or hybrid – the assessment behind D-11

What an RTS of this size can carry: a match has hundreds to a few thousand objects (the stress test 13,220; `MD_GLA05`
2,249), a world snapshot is 3.2 to 9.0 MB as JSON (Phase 18), and the product rate is 60 ticks per second. A **state
stream** as the match path would mean the server computes the world and sends every client what changed per tick; even
with delta encoding that is tens of kilobytes per tick for a late 8-player match, hundreds of kilobits per second per
client, and the server must simulate every room with game data (and with it the legal and operational weight of
original content on foreign machines). Every shipped RTS of this kind – the original included (`GameSpy`/LAN lockstep),
Age of Empires, StarCraft, Supreme Commander – transmits **commands** and lets every machine compute the same world: a
few hundred bytes per tick, no server simulation, and a replay is the command stream. The price is the one this plan
pays anyway: strict determinism (Stage 2 has it, measured) and an input delay of one round trip plus buffer, mitigated by
immediate local feedback (Phase 29). State transfer is a *shooter's* model (few objects, prediction and rollback per
player), not an RTS's.

What a **hybrid** can add, and why it is worth having as an option: a server that *also* simulates the match – not to
drive it, but to *know* it – can (1) hand a **rejoining client a snapshot without a donor** (today Phase 27 takes it from a
fellow player, which costs that player a checkpoint write and trusts his state until the hash check); (2) **arbitrate a
desync** (with three or more machines the majority hash says who deviated; a co-simulating server says so with two and
names the object); (3) serve **spectators authoritatively** and record a **replay with periodic state** so that a replay
browser can seek without replaying from tick 0; (4) reject a command that the simulation would reject anyway before it
reaches the bundle (a small, honest piece of what "anti-cheat" can mean in lockstep – map reveal stays impossible to
prevent, §2.8). Its costs are known: the operator must equip the server with game data from his own copy (`install.ps1`
exists), one room costs around one core share of 4 % at 60 Hz with eight AIs (Phase 23 gate: 0.7 ms per tick), the
server loses the property "runs without game data" **only when the option is on**, and the Linux build must carry the
simulation (pure .NET, no Stride – it already does in the tools). Nothing of this changes the protocol of Phases 25 and
26: the server consumes the same bundles a client does.

Decision D-11 therefore: the match path is commands (as planned); the co-simulating server is **S4-M6, an option after
S4-M3**, switched on per server by configuration, and Phases 25 to 27 keep the seam open for it (the server logic
consumes bundles through the same interface a client's lockstep driver does, so a simulation can be attached to a room).

**Not in scope** (§1): networked campaign or co-op missions; accounts, ladder, player search, central server list; NAT
traversal and connections between players (clients connect outbound to a server with an open port); GameSpy/WOL
compatibility; protection against map reveal (not preventable in lockstep, §2.8); transfer of custom maps; binary
compatibility with old mods and savegames.

## 2. Prerequisites from the other stages

| Prerequisite | From | State |
|---|---|---|
| Session core: AI in the session state, `MatchDescription`, two-stage command path, `ITickDriver`, lockstep probe | S2-M7 (Phase 24) | current, not begun |
| Fidelity package A (F-03 to F-05) merged before Phase 24 records values | S1-M11 (D-12) | open |
| A second physical machine with a different CPU passes the manifest | S2-M1 (D-10, E1) | open; Phase 26 ran without it and claims nothing that needs it |
| Decisions D-11 (commands versus state), D-17 to D-20 | PROGRAMME.md §7 | defaults named |
| Fellow players and a second machine for the acceptances | project owner | to be provided (Phases 26, 29) |
| Nothing from Phases 15 to 17 is accepted by a human (E24): a network game on top of a game with unverified playability conceals causes | S1-M11 | open; R1 does not wait for Stage 4, but Phase 29 should not come before the single-player acceptance |

## 3. Milestones

### S4-M1 Protocol, room, relay in the process — done (Phase 25, 11 October 2026)

- **Result** (`docs/NETWORK-PROTOCOL.md`): every deliverable below is built. Acceptance as measured: the local skirmish runs through the embedded relay only (a layer
  rule forbids the client any other way), with the replay bytes and the session hash of direct submission and every order executed in the tick it was given; the
  reference manifest is unchanged in every result quantity (one manifest commit for the description's encoding: 20 of 1,925 fields); the multi-client probe is
  hash-identical in 48 of 48 runs (1, 2, 4 and 8 clients; 1+7, 2+6, 4+4, 8+0; three eight-player maps; with and without delay and jitter; twice each) and every recording
  replays into the clients' session hash; the dependency rules are a test; the server runs without game data. **Named rests:** no socket and no second process (S4-M2);
  the playback buffer is fixed; the colour of a slot is carried, not drawn; AI strength is counted, not obeyed; a client slower than a server-paced room falls behind
  unmeasured; nobody has seen a match through the relay in a window.

- **Deliverables (`HANDOFF-PHASE25.md`):** simulation version (commit hash and "modified" flag as assembly metadata) in
  match description and handshake; `docs/NETWORK-PROTOCOL.md` first, then versioned binary messages with upper limits
  (handshake, room, match: command intent, tick bundle, hash report, pause, drop, desync, end); `ITransport` and
  `LoopbackTransport` with delay, jitter and reordering from a test seed; `CnCNew.Server` room → loading → match → end,
  server-paced and client-paced, player ID stamped from the connection, intents limited per tick and player, hash reports
  compared, bundles recorded; the lockstep driver of the client with a fixed playback buffer; the **local skirmish over the
  network path** (the skirmish setup becomes the room; save/load kept); `NetTool loopback-match` with 2, 4 and 8 clients
  and the mixes 1+7, 2+6, 4+4, 8+0 on three 8-player maps, with and without disturbance, twice each; the old direct path
  removed (tools stay headless on the session).
- **Acceptance:** the local skirmish runs exclusively over `CnCNew.Net`/`CnCNew.Server` with reference values identical to
  Phase 24; the multi-client probe is hash-identical for all mixes; the server runs without game data; the dependency rules
  are enforced by a test.
- **Bottlenecks:** time of submission (pending commands enter the hash – a bundle enters the world only immediately before
  its tick); order within a bundle (human from the bundle, AI local, one canonical order); start at tick 0 from the match
  description alone; the local skirmish must not get worse (time from intent to execution, tick cost).

### S4-M2 Dedicated server, UDP, clock synchronisation, eight clients — done on one machine (Phase 26, 12 October 2026)

- **Result** (`docs/NETWORK-PROTOCOL.md` §12 to §17, `docs/DEDICATED-SERVER.md`, `docs/SIMULATION-CORE.md` §47): every deliverable below is built except the measurement on
  two machines. Acceptance as measured, on one machine with real processes and real sockets of the loopback interface: eight client processes (six players, two watchers;
  two computer players in every simulation) played whole matches through the dedicated server and a UDP proxy - **36 of 36 lines of the matrix hash-identical on all
  clients**, a standard match of an hour of game time (216,000 ticks on every client, one hash chain, 90,631 orders, never slowed), the latency table measured (order to execution: twice the delay plus 7 to 65 ms depending on
  jitter and loss); a client that hitches or leaves slows nobody, a lost connection slows the others for 8 s at half speed, a client that cannot keep up is dropped. The
  rule's numbers are the plan's proposal as configuration (D-17), the leave of a player is the original's behaviour as a command of the bundle (D-18); both decisions stay
  the owner's. **Named rests:** no second physical machine and no LAN (E1, D-10); nothing measured on a real network; the window client cannot join a server yet (S4-M5);
  a client that can only keep up with the slowed game costs the others more than the rule's 10 s (three incidents, 14.4 s); bandwidth is 6 to 10 KB/s per client and
  direction, untuned; nobody has seen a match through the server in a window.

- **Deliverables (`HANDOFF-PHASE26.md`):** UDP transport behind `ITransport` with a stateless cookie handshake, a reliable
  ordered channel and an urgent channel that carries every unacknowledged bundle, fragmentation with limits, keep-alive and
  timeout, a strict parser; `NetTool proxy` as the network simulator (delay, jitter, loss, duplication, reordering from a
  seed); clock synchronisation (offset by moving median) and an adaptive playback buffer in milliseconds, corrected by a few
  percent of playback rate, never by jumps; catching up; `tools/CnCNew.DedicatedServer` (one process, one port, several
  rooms, JSON and command-line configuration, structured log lines, no `CnCNew.Content`/`Game`/presentation/Stride – source
  test); the rule for slow and disconnected clients with the drop as a system command (`CommandTypes.Version` raised); the
  measurement matrix (eight headless client processes through the proxy: delay 10/40/80/150 ms, jitter 0/10/30 ms, loss
  0/1/3 %; hashes identical, intent-to-execution median/95 %/99 %, buffer level, stalls, bytes per second, retransmissions),
  an endurance run of 60 game minutes, two machines in the LAN (needs the human), the comparison with the calculated
  latency table of `docs/MULTIPLAYER-PLAN.md` §3; `docs/DEDICATED-SERVER.md`.
- **Acceptance:** eight client processes play a complete match with mixed human and AI slots through the dedicated
  server; all hashes identical over the whole matrix; the latency table measured; a slow and a disconnected client do not
  slow down the others beyond the documented rule.
- **Bottlenecks:** hash includes pending commands; garbage collection and long frames (a client catches up); clock drift
  without oscillation; coarse `Thread.Sleep` under Windows (tick interval histogram in the report).

### S4-M3 Desync diagnosis, rejoin, spectators, server replay — done on one machine (Phase 27, 12 October 2026)

- **Result** (commits `a8aa667`, `9875e18`, `2e74381`, `9d680fc`, `c0440ee`, `d42886f`, `db5a236`, `90b20ec`, `6ea31ae`, `8f6f94f`; `docs/NETWORK-PROTOCOL.md` §18 and §19, `docs/NETWORK-DIAGNOSTICS.md`, `docs/DEDICATED-SERVER.md`): protocol version 3. A desync is
  named by the relay down to the parts of the hash and by `NetTool desync-diff` down to tick, object and field (a built-in error, on the fixture and with real
  processes). A lost player keeps its seat for a configured time and comes back with a token; the state comes from a member through the server, which passes it on
  only with the hash all agreed on; three rejoins in a match of an hour (minute 10, 30, 55: in time again after 0.6, 1.2 and 1.6 s), nine of nine clients in the same
  state. Watchers on a delay and into a running match. The server writes every match as a replay file that plays to the clients' hash. Pause limited per player and
  in length, the speed the host's. The window client joins a server (`LiveMatch.Join`, checked against a client without a window). The 60 Hz matrix again 36 of 36
  lines identical (median +0.3 ms against Phase 26), 12 of 12 at 30 and 120 Hz and on a second map. Linux under WSL: the manifest 50 of 50, and one match of
  Windows and Linux processes.
- **Deviations from the brief, named:** the partial sums are not in every hash report but asked for after a desync (a client keeps them for its last 1,024 ticks);
  the state format stays JSON packed with Brotli (measured: within every limit); the replay is played by `NetTool replay-check` and `SkirmishReplay.Play`, the
  client has no replay viewer (S3-M3); **votes are not built** (D-23); the script state has no part of its own.
- **Named rests:** the second physical machine and a real network (E1, D-10); no lobby screen - the window client is told its room on the command line (S4-M5); the
  server's defaults hold no seat and delay nobody until D-19 is decided; no congestion control, so a state on a line of 256 kbit/s takes 15 s and delays the
  orders of both ends meanwhile (S4-M4); D-5 (command check against the map bounds) was not touched and goes to Phase 28.

*The plan as it stood:*


- **Deliverables (`HANDOFF-PHASE27.md`):** hash reports with the partial sums of the world (objects, timers, pending
  commands, random streams, AI state per player, script state); the diagnostics package on a desync (match description,
  bundles as a replay, hash chain, snapshot, object hash table – nothing of it contains original data); `NetTool
  desync-diff` down to tick, object and field (a built-in error is found exactly); the snapshot for transfer measured
  (JSON raw and Brotli; limits ≤ 2 MB, write and compress ≤ 150 ms, read ≤ 1 s; otherwise a compact binary format with the
  same hash); rejoin from a fellow player's checkpoint snapshot through the server, verified by hash, then catch-up over the
  bundles since; a fallback from tick 0; the seat held for a configured time (D-19); spectators as clients without a seat
  with a delayed bundle stream (default 120 s); the server replay at the end of every match (also after an abort), playable
  by `SimTool` and the client to the same hashes; pause and votes as system commands; `docs/NETWORK-DIAGNOSTICS.md`.
- **Acceptance:** a built-in desync found down to tick, object and field; a disconnected client rejoins and is
  hash-identical afterwards; a spectator follows with a delay; the server replay reproduces the hashes.
- **Bottlenecks:** completeness of the snapshot (the test "load at every checkpoint of a 60-minute match and continue,
  hash identical" for all factions); cost of the checkpoint on the donor; hash report rhythm against bandwidth. D-5 (command
  check against the map bounds) is decided here or in Phase 28.

### S4-M4 Server operation and security — done on one machine (Phase 28, 13 October 2026)

- **Result** (commits `6cc6501`, `10e6ab0`, `98796a6`, `26f20bd`, `e76e19b`, `43c4a0e`, `6e2f951`, `c1946aa`; `docs/NETWORK-PROTOCOL.md` §20 and §21, `docs/DEDICATED-SERVER.md` §9 to §13,
  `docs/SIMULATION-CORE.md` §49): protocol version 4, transport version 2. Every datagram after the handshake is sealed (AES-256-GCM, keys from an ECDH exchange
  signed by the server's permanent key, a replay window); a client pins the server's fingerprint or trusts on first use; the rejoin token travels sealed only. What
  a stranger or a member may cost is bounded, counted and has a defined reaction (handshakes, key exchanges, connections per address, bytes and messages per
  connection, the state, the match length, the recording, time limits). A room password as challenge and response, a player key proved by a signature, bans by
  address, network and key, the host's kick, logs with retention and shortened addresses. A player's command may not point beyond a coordinate limit (D-5 closed);
  only orders of a player pass a wire; somebody who watches decides nothing. The threat model has 15 lines with measure, test and residual risk. The suite "Fuzz":
  33 million changed inputs without an exception. The server as a self-contained package for `win-x64` and `linux-x64`, the Linux one run in WSL without a .NET
  runtime in its environment.
- **Measured on one machine:** the matrix over the sealed transport 36 of 36 lines hash-identical with order to execution unchanged and +1.5 KB/s per client and
  direction; sealing 0.3 to 0.6 µs per datagram, a handshake 1.7 to 2.0 ms of the server's core; a rejoin on five lines as before, with the limits in force; a
  match of an hour with three rejoins; capacity about 1.1 % of one core and 0.7 MB per room of eight, 64 rooms at 74 %; the packaged server, started detached, ran for nine hours, 8 h 25 min of it under load in five pieces - 1,280 rooms opened and closed, 1,000 of 1,000 load matches and 280 of 280 real matches finished in step, 294 of 294 rejoins, the working set at 54 MB at the end as at the start, no hanging room (its first attempt found a server that closed every new room after an hour of running, fixed in `c1946aa`); tier 4 with 47 of 47 steps ok at `c1946aa`.
- **Found on the way and fixed:** the server's replay file after a skipped command; a watcher's wrong hash ending the match; a lost bundle larger than a datagram
  waiting a retransmission time; a key of the point (0, 0) throwing out of the handshake; a client replaced by its own second connect at the turn of a cookie's
  time slot.
- **Open:** a hostile load from outside, a real network and the second physical machine (E1, D-10); the 24-hour run in one piece; D-24 (a player that lies about
  its hash), D-25 (the player key by default), D-26 (the limits' numbers); a listing of rooms and a host in the client (S4-M5).

- **Deliverables (`HANDOFF-PHASE28.md`):** the threat model in `docs/DEDICATED-SERVER.md`; limits and throttling (packets
  per sender before the handshake, messages and bytes per connection, intents per tick and player, sizes, connections per
  address, rooms and players per server, memory per room), every violation counted with a defined reaction; a fuzz suite
  against both parsers with a regression corpus; authenticated and encrypted connections with BCL cryptography only (ECDH
  session key, AEAD with the sequence as nonce, room password as challenge-response, server key pair with trust on first
  use); administration (configuration, ban list, kick, structured log with retention, truncated IP storage by default);
  `dotnet publish` self-contained for `win-x64` and `linux-x64` with the Linux run proven (D-20); capacity and a 24-hour
  sustained run with restarts, disconnects and rejoins; the version rule in operation (no "join anyway");
  `scripts/audit-vulnerable.ps1` covers the server.
- **Acceptance:** no crash and no memory growth in the fuzz run; limits take effect; the connection is authenticated and
  encrypted; the server runs as a standalone package under Windows and Linux; the capacity table and the 24-hour run are
  measured.

### S4-M5 Multiplayer in the client, latency perception, acceptance with humans — planned (Phase 29)

- **Deliverables (`HANDOFF-PHASE29.md`):** shell: main menu → Multiplayer → direct connection (`NetworkDirectConnect.wnd`)
  → room list → room (`LanLobbyMenu.wnd`/`LanGameOptionsMenu.wnd`: slots human/AI/open/closed/spectator, faction, team,
  colour, start position, map, starting money, ready, start, chat) → shared loading screen → match → result → room;
  "Host game" starts the embedded server; the local skirmish is the same room with one human; every join deviation shown
  with its cause; golden hashes of the new screens; in-game indicators (connection quality, waiting for a player, dropped
  and returned, pause with requester, desync screen pointing to the diagnostics package, chat, spectator view with
  `ObserverQuit.wnd`); `--net-stats`; **latency perception**: immediate local feedback from the pre-check (Phase 24),
  interpolation of turret angle, barrel pitch, construction progress, health bar and suspension, a measured and switchable
  extrapolation of own moving units, histograms click → execution and click → first visible reaction for 0/20/40/80 ms;
  acceptance with humans (`docs/MULTIPLAYER-FINDINGS.md`): one human against AI over the embedded server, two humans in the
  LAN, two to eight over a dedicated server on the internet with AI slots, disconnect and rejoin, spectators, pause, a
  deliberately slow machine.
- **Acceptance:** humans have worked through the checklist; the findings are in `docs/MULTIPLAYER-FINDINGS.md`; the
  latency histograms before and after are measured.
- **Bottlenecks:** the presentation reads the running world – immediate feedback must never show or change what only the
  execution knows and influences no hash; standing orders with delay come neither twice nor late; smoke tests prove no
  usability. The modern skin of these screens is S3-M5.

### S4-M6 Co-simulating server as an option — open (hybrid, D-11)

- **Deliverables:** a server mode (configuration, off by default) in which a room also runs the simulation from the
  match description and consumes its own bundles through the lockstep driver's interface; the operator provides game
  data from his own installation (`scripts/install.ps1`; the server refuses the mode without content and says so); with
  the mode on: rejoin from the server's checkpoint snapshot (no donor), desync arbitration with the object named from the
  server's state, authoritative spectating, a replay with periodic state for seeking; cost per room measured and in the
  capacity table of S4-M4; the dependency rule "`CnCNew.Server` loads no game data" becomes "only in this mode, through
  one named seam" and is enforced for the default mode by the source test.
- **Acceptance:** the same match with the mode on and off is hash-identical on every client; a rejoin without a donor is
  hash-identical afterwards; a built-in desync is named by the server; the room cost with the mode on is measured.
- **Depends on:** S4-M3 (rejoin, diagnostics, server replay); the seam kept open in S4-M1 and S4-M2.

### R4 Multiplayer release — planned (Phase 30)

Passed when (PROGRAMME.md §6): Phase 29's acceptance by humans; the server package for Windows and Linux; the version and
compatibility rule for savegames, replays and the network protocol from the first release on (every later change of tick
rate, fixed-point format, snapshot, replay or protocol is a break for players and a decision of its own); the clean-room
build of client and server on a fresh machine; the complete test matrix including the rate-equivalence suite and the
network matrix. `docs/handoffs/HANDOFF-PHASE30.md` is the brief; its single-player items are R1, its patch items S3-M7.

## 4. Register of bottlenecks

The measured state of E1 to E24 is in `docs/MULTIPLAYER-PLAN.md` §4. Still open and owned by this stage: E11 (AI state
outside the hash – Phase 24), E12 and E13 (time of submission, synchronous command path – Phases 24/25), E14 (`LiveGame`
owns the time – Phase 24), E15 (match input incomplete – Phases 24/25), E16 (no network code – Phases 25/26), E17 (snapshot
transfer – Phase 27), E18 (desync tool – Phase 27), E19 (open UDP port – Phases 26/28; closed as far as one machine can in Phase 28), E23 (user interface – Phase 29),
E24 (nothing from Phases 15 to 17 accepted by a human – Stage 1). Owned by Stage 2 and still open: E1 (second machine),
E4/E20 (tick maximum, allocation).

# Network protocol (version 2)

Phase 25 (11 October 2026: the protocol, the room, the relay, the local skirmish through it - §1 to §11) and Phase 26 (11 October 2026: the UDP transport, clock
synchronisation and the adaptive playback delay, the rule for members that fall behind, a player leaving, the standalone server - §12 to §16). Frame:
`docs/MULTIPLAYER-PLAN.md` §2 (lockstep through a relay), `docs/SIMULATION-CORE.md` §45 to §47 (session core, the leave command), `docs/PRESENTATION-INPUT.md` §13 (command
path, tick source), `docs/DEDICATED-SERVER.md` (the server as a program). Code: `src/CnCNew.Net` (messages, transport interface, loopback, client, clock synchronisation and
playback control), `src/CnCNew.Net/Udp` (the UDP transport), `src/CnCNew.Server` (room and relay, the directory of rooms), `src/CnCNew.Game/Lockstep` (lockstep driver,
embedded server, the client without a window, the multi-client probe), `tools/CnCNew.DedicatedServer`, `tools/CnCNew.NetTool`.

**What version 2 added to version 1:** the room name in the hello; `Ping`/`Pong`, `LagState`, `Leave` (messages 26 to 29); the drop reason "too slow" and the reject reason
"server full"; the leave of a player as a command inside a bundle (command table 17, replay format 15). A version 1 client is rejected with the reason "protocol version".

## 1. Model

A match is simulated by every client. The server **relays**: it assigns every command intent to a tick, stamps the player of the connection it came from, and sends the
commands of each tick - the tick's **bundle** - to everybody. A client runs tick N when bundle N has arrived, submits its commands immediately before the step, and reports the
session hash afterwards. The server does not simulate, holds no game data and does not even decode a command.

- **Computer players are not on the wire.** They are slots of the match description; every client lets them decide in its own simulation, where they are part of the hashed
  session state (`docs/SIMULATION-CORE.md` §45.1).
- **One match path.** The local skirmish runs through the same handshake, room, start and bundles: an embedded server, a loopback connection, one client (§7).
- **Nothing falls back silently.** A message of an unknown type, above its limit, malformed, or not fitting the phase or the sender is counted and discarded
  (`RelayServer.Discarded`, `LockstepClient.Discarded`, each with the last reason); it is never answered with a guess.

## 2. Versions and identity

Two builds can play together only if all of this is equal; the handshake compares it.

| What | Where | Value now |
|---|---|---|
| Protocol version | `Protocol.Version` | 2 |
| Transport version (UDP datagrams, §12) | `UdpFormat.Version` | 1 |
| Command table | `CommandTypes.Version` | 17 |
| Replay format (the command codec) | `ReplayFormat.CurrentVersion` | 15 |
| World state | `WorldSnapshot.Version` | 10 |
| Match description encoding | `MatchDescription.EncodingVersion` | 2 |
| Definition fingerprint | of the client's game data | 64 hex digits |
| **Simulation identity** | `BuildIdentity.Current.Id` | the commit the binaries were built from, with `+modified` when the tree differed |

**The simulation identity.** Two clients with the same data and the same format versions but different simulation code are certain to diverge. The build writes the commit
(`git rev-parse HEAD`) and a "modified" flag (changes under `src/` or in the build files against that commit) as assembly metadata of `CnCNew.Net`
(`CnCNew.Net.csproj`); `BuildIdentity` reads them. Without git the identity is `unknown+modified`.

- The **first** member of a room defines what the room plays with (identity, format versions, definitions); every later member must be equal in all of them or is rejected
  with the reason.
- A **modified** build is a development state: the server admits it only when it is told to (`RelayOptions.AllowModifiedBuilds`). The embedded server of a local game does;
  a public server does not unless a developer switches it on (`CnCNew.DedicatedServer --allow-modified-builds`, `docs/DEDICATED-SERVER.md`).
- The identity of the room goes into the match description (`MatchDescription.SimulationId`, encoding 2). A client that is handed a description with another identity than
  its own refuses it (`MatchDescription.Resolve`). **An empty identity means "not stated"**: a match that never leaves its process (tools, tests, the runs of the reference
  manifest) states none, which is why a recorded description - and with it the manifest - does not change with every commit.

## 3. Messages

A message is `u16 type` followed by its fields, little endian. Texts are `u16` length plus UTF-8; byte blocks are `u32` length plus bytes. A transport that carries a byte
stream puts `u32 length` in front of every message (`MessageCodec.Frame`); the loopback carries whole messages. **Limits:** 64 KiB per message; a start request and a match
start may add a session state of up to 64 MiB; a name 64 bytes, a chat line 512, a path 260, a command 8 KiB, a description 16 KiB, 8 slots, 16 connections, 64 intents of
one player per tick.

| No. | Message | Direction | Fields |
|---|---|---|---|
| 1 | `Hello` | client → server | protocol version, simulation identity, command table, replay format, world state, description encoding, definition fingerprint, name, room name (64 bytes; a server with one room ignores it). A hello of another protocol version is read up to its version number, so that it can be rejected with the reason |
| 2 | `Welcome` | server → client | connection id, host flag |
| 3 | `Reject` | server → client | reason (`RejectReason`), text. After a hello the connection is closed; later it refuses one request and the connection stays |
| 4 | `RoomConfig` | host → server | room settings: tick rate, map path, terrain fingerprint, max ticks, start money, grace and science interval (original frames), optionally seeds, slots (kind, faction, team, colour, start position, AI strength, AI composition) |
| 5 | `SlotRequest` | client → server | slot (255: leave), faction, team, colour, start position |
| 6 | `Ready` | client → server | flag |
| 7 | `RoomState` | server → all | phase, settings (never the seeds), members (connection, name, slot, ready, host) |
| 8 | `StartRequest` | host → server | optionally a resume state: tick, session hash, commands so far per human player, session state |
| 9 | `MatchStart` | server → each | the canonical bytes of the match description, the receiver's player number (0: watches), pacing, hash interval, optionally the resume state |
| 10 / 11 | `LoadProgress` / `LoadProgressState` | client → server / server → all | percent / connection, percent |
| 12 | `Loaded` | client → server | session hash at the start tick |
| 13 | `AllLoaded` | server → all | start tick |
| 14 / 15 | `Chat` / `ChatMessage` | client → server / server → all | text / connection, text |
| 16 | `Intent` | client → server | receipt number of the client, command bytes |
| 17 | `TickRequest` | client → server | tick (client-paced rooms only) |
| 18 | `TickBundle` | server → all | tick, commands: player, order number, origin connection, receipt, command bytes. Also empty |
| 19 | `HashReport` | client → server | tick, session hash |
| 20 / 21 | `PaceRequest` / `PaceState` | client → server / server → all | paused, speed in percent (25 to 800) / the same and who asked |
| 22 | `PlayerDropped` | server → all | player, connection, reason (left, connection lost, protocol violation, desync, too slow). A notice for the people; what it means for the simulation is a command in a bundle (§14) |
| 23 | `Desync` | server → all | tick, the hash of every connection that reported |
| 24 / 25 | `EndReport` / `MatchEnd` | client → server / server → all | tick, winner / tick, winner, reason |
| 26 / 27 | `Ping` / `Pong` | client → server / server → client | the client's clock in microseconds, the last tick it completed / the client's clock as sent, the server's game time in microseconds, the speed it advances at in percent (0: it stands) (§13) |
| 28 | `LagState` | server → all | connection, how far it is behind in milliseconds of game time (0: caught up), whether the game is slowed for it (§14) |
| 29 | `Leave` | client → server | none: this client leaves the match by choice (§14) |

**A command on the wire** is the simulation's own command codec (`ReplayFormat.EncodeCommand`: `i64 tick, u16 type, i32 player, u64 sequence`, then the fields of the type) -
the protocol invents no second encoding. A client sends an intent with tick 0 and sequence 0. The server checks the header (length, a known command type that is not the
leave command, which is the server's to give), writes the
**player of the connection** and the tick of the bundle into it (`CommandWire.Stamp`) and passes the rest on untouched: nobody can give a command for somebody else. The
sequence number stays 0 on the wire. The server counts an **order number** per player; the simulation's sequence number is assigned by every client's session when the
command is submitted, in the order of the bundle (`docs/SIMULATION-CORE.md` §45.5) - which is the same on every client because the bundle is.

## 4. The room (`RelayServer`)

State machine **room → loading → match → ended**; the server is authoritative, a client shows the `RoomState` it was sent, never what it asked for.

- **Host** is the first member (the next one when it leaves). Only the host sets the room and starts the match. A room nobody has set yet states the server's default tick
  rate (`RelayOptions.DefaultTickRate`, 60) and no slots; it admits at most `RelayOptions.MaxConnections` members (16, the protocol's limit).
- **Slots.** A `Human` slot is a seat: a member takes it with `SlotRequest` and brings faction, team, colour and start position; a taken seat and a taken start position are
  refused. `Computer`, `Closed` and `Spectator` slots are set by the host. A member without a seat watches.
- **Start.** Every human slot must have a member and every seated member but the host must be ready. The server builds the `MatchDescription` from the settings, the
  room's identity and definitions and the **seeds it chooses** (`RelayOptions.SeedSource`; a host may state them only on a server that allows it - the embedded one, for a
  savegame or a test). "Random" factions and start positions are decided by that seed on every client alike (`MatchDescription.Resolve`). Each member gets `MatchStart`
  with its player number: the slots that take part are numbered from 1 in slot order, the rule `Resolve` applies.
- **AI strength** is carried in the slot and the description. The built-in computer player has one strength; a client counts a slot that asks for another
  (`ResolvedMatch.AiStrengthsIgnored`) and says so in the notes of its session. A room that shows a strength box must say that it is ignored; the setup screen of this
  phase offers the strengths only with the original AI, which exists for local matches only.
- **Loading.** Every client builds the match from the bytes and reports its session hash at the start tick. When all have reported the server compares them: equal - and
  equal to the saved hash of a resumed match - or the match ends before it begins as a desync.
- **Start from a saved state.** `StartRequest` may carry a session state (`SkirmishMatchSession.Save`), its tick, its session hash and the number of commands every human
  player has submitted so far. The server passes it on with `MatchStart`, starts the tick count there and continues the order numbers.

## 5. The match

**Pacing** is decided at the start: a room with exactly one member is **client-paced**, every other one **server-paced**.

- *Server-paced:* the server closes tick N when its clock says so - tick rate times speed, accumulated in microseconds - and waits for nobody. The clock is injected
  (`INetClock`); nothing in `CnCNew.Net` or `CnCNew.Server` reads wall-clock time by itself.
- *Client-paced:* the tick is closed when the one client asks (`TickRequest`). A single-player game has no added delay and keeps pause, speed and the fast forward of
  tests; the server's clock is not involved at all.

**Closing a tick.** The server takes, per player in ascending player number, the intents that have arrived, in the order of arrival (at most 64 per player and tick; the
rest wait for the next tick, counted), stamps them, numbers them and sends the bundle - also when it is empty. **The order inside a bundle is the order of submission** on
every client: by player, then by order number.

**The order of a tick on a client** (`LiveGame.Advance`): the per-tick bookkeeping (standing orders give their commands) → the driver closes or awaits the tick → the bundle
is submitted (`CommandPath.SubmitDue`) → the session steps: the system's commands, then every computer player in player order, then the world. The world executes the
commands of a tick by (player, sequence) whatever the order of submission, and validation of one player's command does not depend on another player's pending ones - so a
human's command from the bundle and a computer player's local command end up in the same overall order on every client. A bundle enters the world **only immediately
before its tick**: pending commands are part of the state hash, so a bundle submitted early on one client would make its hashes differ.

**Hash reports.** Every `HashInterval` ticks (1 in this phase) a client reports the session hash after the step. The server compares the reports of a tick: when all are
there and equal the tick is agreed (`LastAgreedTick`); two that differ are a **desync** - the server sends every reported hash of that tick to everybody and ends the
match. Which object and field differ is the diagnosis of Phase 27.

**The end.** A client whose simulation says the match is over reports tick and winner; when all reports agree the server ends the match. A member that leaves is announced
(`PlayerDropped`), and the leave of its player is a command of the next bundle (§14); when nobody is left the match is abandoned.

**The recording** (`RelayServer.Recording`): the description and every bundle. It is enough to play the match again without any client: the commands of the humans come
from the bundles, the computer players decide again (`SkirmishReplay.Play`, §8).

## 6. The client (`LockstepClient`, `LockstepDriver`)

`LockstepClient` (in `CnCNew.Net`) is the protocol's client end: handshake, room, and in a match the bundles in order. It owns no simulation. Given a clock it asks the
server for its tick clock at an interval and keeps the estimate (§13); without one - the local game - it asks nothing.

`LockstepDriver` (in `CnCNew.Game.Lockstep`) is the game's **tick source and command scheduler in one** - `ITickDriver` and `ICommandScheduler` of
`docs/PRESENTATION-INPUT.md` §13 -, because here they are one thing: a tick may run when its bundle has arrived, and the bundle is what the scheduler hands out.

- An order (`CommandPath.Issue`) is pre-checked locally as before and sent as an intent under its receipt number. It comes back in a bundle with its origin and receipt, is
  submitted from there and answered by receipt number (`CommandResolution`). A command of another machine has no receipt here and no pre-check to deviate from.
- Client-paced: the accumulator is the driver's own; when a tick is due it sends the held orders, asks for the tick and steps as soon as the bundle is there. Pause and
  speed are granted at once.
- Server-paced: a tick is released when its bundle has arrived **and its playback time is reached** (§13): the playback position follows the server's tick clock by a delay
  measured from the arrivals, in milliseconds. A client that fell behind finds several ticks due and plays them in one frame, up to the frame's limit. Pause and speed are
  requests to the server; any player may ask, and the answer goes to everybody.
- The lockstep probe (`--input-delay-ticks N`) is part of the driver: it holds every order N ticks before it sends it.

## 7. The local skirmish

`LiveMatch.Create` opens a `LocalRelay`: a `RelayServer` that admits development builds and takes the host's seeds, a loopback connection without delay, one
`LockstepClient`. The setup becomes room settings, the viewer takes his seat (or none: auto-play and spectator matches are rooms whose one member watches), the server
starts the match and hands back the description; the client builds the session from it, reports its hash and takes every tick and every command from the relay from then on.
`LiveMatch.Restore` does the same with a resume state. **There is no second way for a client to start a skirmish.** Missions stay local (their presentation reports back to
the script VM, `docs/PRESENTATION-INPUT.md` §13.4) and use the local driver and scheduler; headless tools and tests may step a session directly - they are the reference.

**Who is local** is not in the description. `MatchSetup.LocalSlot` names the viewer's slot (default 0): whose orders the input gives, whose fog is shown, where the camera
starts.

## 8. Checks

- `tests/CnCNew.Net.Tests`: every message survives its bytes; unknown types, oversized and truncated messages are counted and dropped by both ends; the command stamp
  against the simulation's codec; the loopback keeps the order per connection under delay and jitter; the room (host, seats, refusals, start conditions, seeds); both pacings;
  hash agreement and a provoked desync; the intent limit; a match of several clients over a synthetic fixture; the layer rules (project references, no game data in the
  server, no wall clock in either layer).
- `tools/CnCNew.NetTool loopback-match`: H clients, each with its own world, against one server in one process, on real maps; the "humans" are command generators (a
  `SkirmishAi` that decides on its own client only and sends its commands as intents). After every tick the session hashes of all worlds are compared; at the end the
  server's recording is played again (`SkirmishReplay.Play` without the audit of the computer players, whose commands the recording does not hold) and must end in the same
  session hash.

## 9. Limits of this version

No encryption, no authentication beyond the cookie of §12.3, no flood protection beyond the per-tick limit and the connection limit, no congestion control (Phase 28); no
rejoin, no spectator joining a running match, no desync diagnosis below "this tick" (27); pause and speed in a server-paced room may be asked by any player without a rule
(29); the setup screen is not yet a room others can join, and the window client has no entry to a foreign server - only the client without a window joins one (29); a
room's listing does not exist: a client must know the name of its room (29).

## 10. Measured (11 October 2026, 60 Hz, build `0b93f31`)

**The multi-client probe** (`NetTool probe-suite game/content --minutes 2 --jobs 2`, 591 s): three eight-player maps (Twilight Flame, Fortress Avalanche, Iron Dragon) x the
mixes 1+7, 2+6, 4+4, 8+0 (clients + computer players) x (no delay | 40 ms delay and up to 60 ms jitter each way, playback buffer 3 ticks) x twice = **48 runs of 7,200
ticks, 48 of 48** with the session hashes of all clients equal after every tick, every tick agreed by the server, nothing discarded on either end, and the server's
recording played as a replay ending in the clients' session hash. The two repeats of every run end in the same hash (0 of 24 pairs differ).

| Mix (clients + computer players) | Pacing | Commands relayed in 2 game minutes (three maps) | Order to execution, no delay | with delay and jitter | Messages per run |
|---|---|---|---|---|---|
| 1 + 7 | client | 80 to 147 | 0 ticks | 0 ticks (the one client paces: a delayed bundle delays the tick, not the order) | 21,700 |
| 2 + 6 | server | 246 to 388 | 1 tick | mean 13.0, at most 15 ticks | 29,100 |
| 4 + 4 | server | 518 to 641 | 1 tick | mean 13.2, at most 15 ticks | 58,200 |
| 8 + 0 | server | 918 to 1,141 | 1 tick | mean 13.1, at most 15 ticks | 116,400 |

With a server-paced room an order is executed in the tick after it was given at best (it reaches the server while that tick is open); 40 ms each way, up to 60 ms of
jitter and three ticks of buffer make it 13 ticks, around 217 ms. With delay and jitter a match is another match than without (the orders arrive in other ticks) - equal
between its clients and between its repeats, not equal to the plain one. A match of eight clients for two game minutes takes 46 to 48 s of one core (eight worlds).

**The cost of the network path in a local skirmish** (`NetTool local-cost`, Twilight Flame, eight players, 18,000 ticks, three alternating rounds): 621.7 µs per tick
stepped directly, 644.1 µs through the relay (medians) - **+22 µs per tick, 3.6 %**, for three messages and 40 bytes per tick (tick request, empty bundle, hash report) and
the session hash of every tick. Both end in the same session hash. **Order to execution in a local game: the same tick**, as before the phase (test *Match of one client*:
largest distance 0 ticks; `HudTests`: replay bytes and session hash of a real match through the relay equal direct submission).

**Memory of n worlds in one process:** 10.8 MB of managed memory per world of that match after one game minute (1, 2, 4 and 8 worlds: 10.8, 21.7, 43.4, 86.8 MB); catalogue
and terrain are shared and not counted.

**Not measured:** anything over a real network; more than two game minutes per probe run; the probe at 30 and 120 Hz on real maps (the synthetic matches of `Net.Tests` run
at all three rates); the behaviour of a client that cannot keep up with a server-paced room.

## 11. Closing measurements of Phase 25 (11 October 2026, state `71f477e`, Windows, Release)

**Tier 4** (`pwsh scripts/test.ps1 -Tier full -KeepGoing`, 4,615 s): 40 of 40 steps ok - clean build 0 warnings; every test program; compat-check 713; `Simulation.Tests`
at 30, 60 and 120 Hz, with real data (rate-equivalence suite 0 / 0) and the long run; `Game.Tests` and `Presentation.Tests` with real data at 30, 60 and 120 Hz - every
skirmish of them through the embedded relay; `Net.Tests` at 30, 60 and 120 Hz; `input-manifest check` identical; `reference-manifest check --set full` 50 of 50
identical (a second time in the tier 2 run after the manifest was written); the three client checks (`check-spectator`, `check-dozer-build -Quick`,
`check-build-hotkeys`), which play through the relay; `check-match`; `session-check` 19 of 19; `net-probe` (four clients and four computer players with delay and jitter);
the staged scenes. **Campaign** (missions stay local): reachability clean 24/24 and 24/24, `audit` 24 of 24 identical on a second run and after save/restore with end
probes 13/24 and 15/24, `drive` victory 24/24 and defeat 24/24.

**Performance of the simulation** (`scripts/perf-profile.ps1`, the whole profile with the binaries of `e96d93f` and of this state one after the other in the same hour;
the profile steps sessions directly, so it measures whether the phase touched the tick, not the network path - that is §10): every run ends in the same final hash on both
sides.

| Run | Mean per tick before / after | Change | Maximum before / after |
|---|---|---|---|
| small, 2 players | 216.0 / 210.7 µs | -2.4 % | 44.5 / 44.1 ms |
| medium, 2 players | 265.2 / 260.7 µs | -1.7 % | 45.9 / 44.6 ms |
| medium, 4 players | 491.4 / 495.3 µs | +0.8 % | 48.7 / 45.7 ms |
| large, 4 players | 366.6 / 360.2 µs | -1.7 % | 45.8 / 45.7 ms |
| large, 8 players | 839.1 / 847.0 µs | +0.9 % | 140.4 / 145.2 ms |
| `MD_USA05`, 100 s / 300 s | 2,060 / 2,054 and 1,808 / 1,838 µs | -0.3 % / +1.6 % | 132 / 127 and 126 / 125 ms |
| `MD_GLA05`, 100 s / 300 s | 2,917 / 2,938 and 2,607 / 2,598 µs | +0.7 % / -0.3 % | 148 / 127 and 128 / 125 ms |

The means lie within -2.4 % and +1.6 % with both signs: the phase is not measurable in the tick of the simulation. The tick maximum of the eight-player run (140 to
145 ms, decision D-3) is unchanged; a client-paced room simply takes that long for that tick, a server-paced one closes eight to nine ticks meanwhile and the client
catches up afterwards (not measured over a real network).

**Not run:** the determinism matrix and Linux; the survey over 320 matches; the commands-only agent; the multi-client probe at 30 and 120 Hz and for more than two game
minutes; `sync-perf` of the game layer; `scripts/acceptance.ps1`. **The second physical machine (register E1, decision D-10) is still open.** **No human has seen** a match
through the relay, a team set in the setup screen, or a skirmish resumed through a room.

## 12. The UDP transport (Phase 26; `CnCNew.Net.Udp`)

The protocol of §3 assumes whole messages, reliable and ordered per connection. Over UDP that guarantee is this layer's own. It is written with `System.Net.Sockets` alone; nothing
above `IConnection` knows which transport carries it. Every multi-byte value is little-endian. A datagram that violates anything stated here is **dropped as a whole and
counted** (`Malformed`); nothing in it is applied.

### 12.1 Size

A datagram carries at most **1,200 bytes** (`UdpFormat.MaxDatagramBytes`). IPv6 guarantees 1,280 bytes per packet; minus 40 bytes of IPv6 header and 8 of UDP that leaves 1,232,
and the remaining 32 bytes are room for a tunnel on the way. IPv4 paths below 1,228 bytes of MTU exist but are rare; the number is the one QUIC uses for the same reason. Nothing
here relies on IP fragmentation.

### 12.2 Datagram header

| Offset | Field | |
|---|---|---|
| 0 | `u16` magic `0xC2CE` | anything else is not ours |
| 2 | `u8` transport version, **1** | another version is dropped and counted |
| 3 | `u8` kind | 1 request, 2 challenge, 3 connect, 4 accept, 5 data, 6 close, 7 refuse |

### 12.3 Connection setup with a stateless cookie

| Kind | Direction | Body | Length |
|---|---|---|---|
| request | client → server | `u64` client nonce, 20 zero bytes | 32 |
| challenge | server → client | `u64` client nonce, `u32` time slot, 16 bytes cookie | 32 |
| connect | client → server | `u64` client nonce, `u32` time slot, 16 bytes cookie | 32 |
| accept | server → client | `u64` client nonce, `u64` connection token | 20 |
| refuse | server → client | `u64` client nonce, `u8` reason (1: the server is full) | 13 |
| close | both | `u64` connection token | 12 |

- The **cookie** is the first 16 bytes of HMAC-SHA-256 over (`0x01`, the sender's address bytes, its port, the client nonce, the time slot) with a secret only the server
  knows. The time slot is the server's clock divided by the cookie lifetime (default 10 s); a cookie of the current or the previous slot is valid.
- The server keeps **no state** for a request: it answers with a challenge of the same length as the request (no amplification) and forgets. Only a connect with a valid cookie
  - which proves that the sender receives at the address it claims - creates a connection. A connect with an invalid or expired cookie is answered with a fresh challenge.
- The **connection token** is the first 8 bytes of the same HMAC with the prefix `0x02`; every later datagram of the connection carries it, and a datagram from the right address
  with the wrong token is dropped. A repeated connect (its accept was lost) derives the same token and is answered again; a connect with another nonce from the same address
  replaces the old connection (the client restarted).
- The client repeats request and connect every `ConnectRetryMs` (250) and gives up after `ConnectTimeoutMs` (5,000). Messages sent before the connection stands wait in the
  reliable channel.
- A request is exactly 32 bytes with zero padding; every other length, or a non-zero padding byte, is malformed.

### 12.4 Data datagrams

After the 4-byte header:

| Field | |
|---|---|
| `u64` connection token | |
| `u32` send time | the sender's clock in milliseconds, low 32 bits |
| `u32` echo time | the newest send time received from the peer |
| `u16` echo hold | milliseconds between receiving that datagram and sending this one; `0xFFFF`: nothing to echo |
| `u32` reliable acknowledgement | the next segment number expected (everything below it has arrived) |
| `u32` acknowledgement mask | bit *i*: segment (acknowledgement + 1 + *i*) has arrived |
| `u32` urgent acknowledgement | the next urgent message number expected |

34 bytes in all; then sections until the datagram ends:

| Section | Layout | Limits |
|---|---|---|
| `u8` 1: reliable segment | `u32` segment number, `u16` length, bytes | length 1 to 1,159 |
| `u8` 2: urgent fragment | `u32` message number, `u16` fragment index, `u16` fragment count, `u16` length, bytes | count 1 to 64, index below count, length 1 to 1,155 |

An unknown section kind, a length of 0 or beyond the datagram, or trailing bytes make the datagram malformed. A datagram with no section is an acknowledgement or a keep-alive.
The round-trip time of a connection is measured from the echo fields (now − echo time − echo hold) and smoothed as TCP does (`srtt`, `rttvar`).

### 12.5 The reliable channel

Everything `IConnection.Send` is given: lobby, control, command intents, hash reports. The messages form **one byte stream** - each message preceded by its `u32` length -
that is cut into numbered segments; a large message (the match description, a saved state for a resumed match) simply spans many segments, which is the fragmentation of this
channel.

- **Sender:** at most `WindowSegments` (256) segments are unacknowledged at a time. A segment is sent again when it is unacknowledged for the retransmission time
  (`srtt + 4 · rttvar`, between `MinRetransmitMs` 30 and `MaxRetransmitMs` 2,000; `InitialRetransmitMs` 200 before the first measurement; doubled per repetition of the same
  segment, at most 16-fold), or at once - one time per gap - when the acknowledgement mask shows two later segments arrived (fast retransmission).
- **Receiver:** segments are accepted up to `WindowSegments` ahead of the next expected one, held until the gap closes, and appended to the stream in order; a duplicate is
  counted and ignored. From the stream the receiver takes whole messages. A length prefix above the connection's message limit (64 MiB + 64 KiB, the protocol's largest message)
  is a violation: the connection is closed. The buffer grows with the bytes that actually arrive, never with what a prefix announces.
- There is no congestion control beyond the fixed window (a match is a few kilobytes per second; operating an open port is Phase 28).

### 12.6 The urgent channel

Everything `IConnection.SendUrgent` is given - in this protocol exactly the **tick bundles**, server to client. An urgent message is numbered; it is delivered in number order,
once, like a reliable one, but it is sent differently: **every datagram that carries a new urgent message also carries every urgent message the peer has not acknowledged
yet**, oldest first, as far as the datagram has room (`UrgentRedundancyBytes`, default: the rest of one datagram; what does not fit is counted in `UrgentOmitted` and follows
with the next datagram or the retransmission timer). A single lost datagram therefore costs no waiting: the next one, one tick later, brings the missing bundle. A message
larger than one datagram is cut into fragments (at most 64, so 73,920 bytes) that are sent once in consecutive datagrams and again by the retransmission timer. Without new
messages (pause, the last tick) unacknowledged urgent messages are repeated by the same retransmission time as segments.

**Order holds per channel, not between the two.** The protocol tolerates it: a client takes bundles in the loading phase as well as in the match, and everything else the server
sends (room state, pace, lag, drop notice, end) does not depend on its position between two bundles - what a dropped player means for the simulation is a command *inside* a
bundle (§14). The loopback transport has the same two lanes, so the probes in one process exercise the overtaking too.

### 12.7 Keep-alive, timeout, close

A connection that has sent nothing for `KeepAliveMs` (1,000) sends an empty data datagram; one that has received nothing for `TimeoutMs` (10,000) is closed (`IsOpen` false; the
relay drops the member with `ConnectionLost`). Acknowledgements go out with the next flush of the host loop.

`Close()` closes the connection for its owner at once, but **what was sent before the close is still delivered**: the reliable channel goes on sending and repeating until
everything is acknowledged or `LingerMs` (2,000) have passed - a rejection or the end of a match would be of no use otherwise. Then the close datagram goes out three times,
one per flush; the peer closes at the first it receives. A closed end takes nothing in any more except the acknowledgements it waits for.

### 12.8 Numbers

All of them are fields of `UdpOptions` with these defaults: `ConnectRetryMs` 250, `ConnectTimeoutMs` 5,000, `CookieLifetimeMs` 10,000, `KeepAliveMs` 1,000, `TimeoutMs` 10,000,
`LingerMs` 2,000, `InitialRetransmitMs` 200, `MinRetransmitMs` 30, `MaxRetransmitMs` 2,000, `WindowSegments` 256, `UrgentRedundancyBytes` 1,155, `UrgentWindow` 1,024 (urgent messages
unacknowledged or ahead; beyond it the connection is closed - the peer has not acknowledged for 17 s at 60 Hz), `MaxMessageBytes` 67,174,400, `MaxConnections` 256 (server).

### 12.9 Counters

Per connection (`UdpLinkStatistics`): datagrams and bytes sent and received, segments sent and retransmitted, urgent messages sent, urgent repetitions, urgent omissions,
duplicates received, malformed datagrams, smoothed round-trip time. Per server endpoint: requests, challenges, connects accepted, cookies rejected, connections refused,
datagrams from unknown peers, malformed datagrams.

## 13. Clock synchronisation, playback and pacing (Phase 26)

In a server-paced room the server closes ticks by its clock and waits for nobody. Three things follow: a client has to know the server's tick clock, it has to decide when to
play a tick whose bundle has arrived, and the server's host has to wake up when a tick is due.

### 13.1 The server's clock (`Ping`, `Pong`, `ClockSync`)

The server's **game time** is the number of ticks it has closed times the tick length (in whole microseconds: 16,666 at 60 Hz, as the server counts) plus what has
accumulated toward the next tick; it advances with the game speed - the set speed, halved while the game is slowed (§14), zero in a pause. A client sends a `Ping` with its
own clock every `PingIntervalMs` (200; every 50 ms while it has no estimate in a match); the `Pong` returns that clock and states the game time and the speed.

One exchange gives one sample: the local time halfway between sending and receiving, and the game time stated. The estimate of the game time at a local time is the
**median** of what the last `SyncSamples` (9) samples say for that time - each extrapolated with the stated speed -, so one exchange with a lopsided way moves nothing. A
change of the stated speed empties the window: samples of another speed say nothing about this one, and until three new ones are there the client has no estimate. The
median of the round-trip times of the window is the client's ping.

A constant error of the estimate does not matter (§13.2 measures against the same estimate it steers by); only its noise does, which the median bounds to a fraction of the
jitter.

### 13.2 When a tick is played (`PlaybackControl`)

The server closes tick N at game time (N + 1) tick lengths; the bundle arrives after the way and its jitter. The client keeps a **playback position** - a clock of its own in
game time - that follows the server's clock by the **playback delay**: tick N is due when the position has reached (N + 1) tick lengths. It runs when it is due *and* its
bundle is there (`LockstepDriver.IsDue`).

- **The delay is measured, in microseconds, not set in ticks.** For every bundle the client notes its *lateness*: the estimated game time at its arrival minus the game time
  the server closed it at. Over the last `JitterWindowMs` (10 s) the delay is the `JitterPerMille` (990) quantile of the lateness plus `MarginMs` (4). On a quiet line that
  is the way plus the margin; with jitter it covers 99 % of the arrivals; with loss it grows by about a tick, because the bundle of a lost datagram comes with the next one
  (§12.6).
- **The position is steered by rate, never by a jump.** It advances with the local clock times the game speed times (1 + adjustment); the adjustment is the deviation from
  the target divided by `CorrectionTimeMs` (1 s) plus a slowly accumulated share (time constant `IntegralTimeMs`, 5 s) that takes up what stays - a local clock that runs
  fast needs a lasting correction -, capped at `MaxRateAdjustPerMille` (50: five percent). The second term is slow against the first, so the control settles instead of
  swinging (tested with a clock skewed by 1 %, a hundred times a bad crystal).
- **Two exceptions set the position directly:** the first time there is an estimate *and* `MinArrivals` (8) measured arrivals - a delay taken from nothing would be wrong for
  the first second -, and a deviation beyond `ResyncMs` (1 s: the way changed, the process was suspended), which is counted (`Resyncs`).
- **Without an estimate** (no clock, the first moments of a match, after a change of speed) a tick is due as soon as its bundle is there.
- **The client's own hitches are not the network's jitter.** A bundle the client takes in after it stood still itself for more than `HitchMs` (50: a long frame, a garbage
  collection) is played but not measured - when it arrived is unknown within that time.
- **Catching up.** The position is a clock: it goes on while the client stands. After a hitch several ticks are due at once and are played in one frame, up to the frame's
  limit (`LiveGame`'s ticks per frame; 8 in the client without a window), until the client is back at the position. Nothing is dropped and nobody else is slowed; a client
  that stays behind is the business of §14.
- **Interpolation.** The fraction a frame interpolates with is the position between the last tick played and the next (`LockstepDriver.Fraction`).
- **A wait** (`LockstepDriver.Stalls`) is a tick whose playback time was reached before its bundle: by construction about one tick in a hundred on a line with jitter. The
  tick is played as soon as the bundle arrives, a few milliseconds late, and its lateness is in the window.
- `LockstepDriver.DelayTicks` - how many ticks after the next an order given now is expected to execute - is the playback delay plus half the round trip, in ticks, plus the
  tick the server has open: an estimate that moves with the line. The tick an order really gets is the server's decision.

### 13.3 What that costs an order

From the order to its execution on the client that gave it: the way to the server, the wait for the tick the server has open (half a tick on average), the way back, and
the playback delay minus the way back - in sum **the way up, half a tick, and the playback delay**. The measured table is §16.

### 13.4 The server's loop and the operating system's timer

A host loop (`CnCNew.DedicatedServer`) takes in datagrams, pumps the rooms, sends, and then **waits on its socket** until the next tick of any room is due, at most
`IdleWaitMicros` (2 ms): an arriving datagram wakes it at once, and nothing depends on a sleep being exact - a room closes its ticks by the clock, so a loop that wakes late
closes the tick late, or two at once (a *burst*), and the clients' playback delay absorbs it as jitter.

How late the loop wakes is the operating system's timer. **Windows wakes a waiting thread on its timer interrupt, every 15.6 ms by default** - nearly a whole tick at 60 Hz.
While a `FineTimer` is held the process asks for 1 ms (`timeBeginPeriod(1)`); Linux needs nothing. Measured on the development machine (Windows 11, `NetTool pacing`, a wake-up
every 16,666 µs for 3 s per way):

| Way of waiting | late by: mean | median | 95 % | 99 % | max |
|---|---|---|---|---|---|
| `Thread.Sleep` | 7.89 ms | 7.81 | 15.03 | 15.77 | 16.23 |
| wait on the socket (`select` with a timeout) | 8.04 ms | 7.92 | 15.32 | 16.07 | 16.14 |
| wait on the socket, fine timer held | 0.37 ms | 0.29 | 0.97 | 1.25 | 1.32 |
| the same in steps of at most 2 ms (the server's loop) | 0.49 ms | 0.43 | 1.02 | 1.23 | 1.24 |

The server holds the fine timer by default (`"fineTimer": true`; the `listening` line says whether it was granted). Every room keeps a histogram of the time between two
closings as deviation from the tick length (`RelayServer.TickIntervals`: below 0.25, 0.5, 1, 2, 4, 8, 16 ms and above, the bursts, the maximum); the server writes it with
every status line and when the room closes, and the measurements of §16 report it. `SystemNetClock` (a monotonic stopwatch) and `FineTimer` are the content of
`src/CnCNew.Net/RealTime.cs`, the one file of the two network layers that reads real time - the layer rule of `Net.Tests` names it as its only exception.

## 14. Members that fall behind, and a player that leaves (Phase 26)

### 14.1 The rule

The server knows how far every member is: the last tick it completed, from its hash reports and its pings. **Lag** is the game time between that tick and the last one the
server closed. A healthy client is behind by its playback delay plus the way of its reports - 100 to 400 ms on the lines of §16.

| Lag of a member | What the server does | Option (default) |
|---|---|---|
| from 500 ms | tells everybody (`LagState`: who, how far), repeated every 250 ms while it lasts - **only shown** | `LagReportMs` (500) |
| above 2 s, a player | the game runs at **half speed for everybody** (`PaceState` states the speed the ticks really close at); it stays slowed until that player is below half the limit | `LagThrottleMs` (2,000), `ThrottleSpeedPercent` (50) |
| above 2 s for 10 s of the server's clock | the member is **dropped** (`PlayerDropped`, reason "too slow"; the connection is closed) | `ThrottleLimitMs` (10,000) |

- The 10 s count on until the member is back below the report limit: one that hovers at the limit is dropped, not carried forever.
- A **watcher** slows nobody; it is only dropped after the same time.
- A **lost connection** is noticed by the transport after `TimeoutMs` (10 s without a datagram) and dropped with "connection lost" - until then it is a member that falls
  behind and the rule applies to it: shown, then the game slowed.
- **What the others lose is bounded:** per incident at most `ThrottleLimitMs` × (1 − `ThrottleSpeedPercent`) = 5 s of game time, spread over 10 s. A client's own hitch of
  half a second - the tick maximum of the eight-player profile is 140 ms (decision D-3) - is far below every limit and slows nobody (tested).
- Every number is configuration (`RelayOptions`, the server's file and command line); with `LagThrottleMs` 0 the server neither slows nor drops for lag.

**The numbers are a proposal** (`docs/MULTIPLAYER-PLAN.md` §7: "2 s shown, then slowed for up to 10 s, then disconnected"); the decision is the project owner's. What is
built is the mechanism with these defaults.

### 14.2 The leave is a command

`PlayerDropped` is a notice for the people. For the simulation the server puts a **leave command** of that player into the next bundle it closes
(`PlayerLeaveCommand`, command type 34; `CommandWire.Leave`): the player's own last command, with the next order number of that player and no origin connection, so every
client executes it in the same tick, a replay of the recording contains it, and nobody has to agree on anything outside the bundles. A client can never give it - as an intent
it is refused and counted.

What it does is the original's `MSG_SELF_DESTRUCT` (`docs/SIMULATION-CORE.md` §47): after `Leave` (the client said it leaves) the first living player allied with it in both
directions inherits its objects and its money, and what is left dies; after a lost connection, a drop for lag, or without such an ally, everything of the player dies -
riders are put out of its containers first, a tech building goes to the neutral player. A player that vanishes while the match loads leaves in the first tick.

## 15. Real sockets and real processes (Phase 26)

- **`tools/CnCNew.DedicatedServer`** - the relay as a program: one process, one UDP port, several rooms (`docs/DEDICATED-SERVER.md`).
- **`RoomDirectory`** (`CnCNew.Server`) - several rooms behind one transport endpoint: a connection belongs to the room its hello names; the first hello that names a room
  opens it, the room is removed with its last connection; a hello for one room more than `MaxRooms` is rejected ("server full"); a connection that says no hello within
  `HelloTimeoutMs` (5 s), or something else first, is closed.
- **`HeadlessClient`** (`CnCNew.Game.Lockstep`) - a client without a window: the protocol client, its own world, the lockstep driver, the command path; its "human" is a
  command generator. The multi-client probe, the tests and `NetTool client` host it.
- **`NetTool proxy`** - the network simulator for real sockets: clients send to the proxy as to the server; per client it opens a socket of its own toward the server, and
  both directions pass the same disturbance the tests use in memory (`DatagramImpairer`: delay, jitter, loss, duplication, reordering from a seed).
- **`NetTool client`** - a client process: joins a room of a server, takes its seat or watches, plays to a tick, writes what it measured as JSON: hash chain, order-to-execution
  times, playback delay, waits, bytes, repetitions, memory. It can be told to leave, to vanish without a word, to freeze, to be too slow, or to hitch.
- **`NetTool net-match`** - the harness: starts the server, the proxy and one client process per member on this machine, waits, and compares: every undisturbed client played
  to the end, **all hash chains are equal**, the server agreed on every tick and reported no desync, no client discarded anything. **`NetTool net-matrix`** runs it for every
  line of a matrix and writes the table. **`scripts/check-net-match.ps1`** is the short form in the gate (`net-match` in `scripts/test.ps1`): one match with delay, jitter and
  loss, one with a client process that vanishes.
- **`NetTool pacing`** - the measurement of §13.4.

Tests (`tests/CnCNew.Net.Tests`, 34 at 30, 60 and 120 Hz): the datagram layout and its refusals; the cookie handshake; both channels under loss, duplication and reordering,
reproducibly from a seed; time limits, a clean close that still delivers; noise and damaged datagrams; the transport on real loopback sockets; clock synchronisation and
playback control against a simulated line (way, jitter, loss, a skewed clock, a line that gets worse); the rule for members that fall behind; leaving; **a match over lossy
UDP in memory - bundles arrive in any order and at any time, the session hashes stay equal in every tick**; matches in which a client leaves, loses its line, freezes, is
too slow or hitches; the rooms of one server; **a match over real sockets with the server stack as the program runs it**; the references of the dedicated server.

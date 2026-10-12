# Network protocol (version 4)

Phase 25 (11 October 2026: the protocol, the room, the relay, the local skirmish through it - §1 to §11) and Phase 26 (11 October 2026: the UDP transport, clock
synchronisation and the adaptive playback delay, the rule for members that fall behind, a player leaving, the standalone server - §12 to §17) and Phase 27 (12 October
2026: a held seat and the rejoin, watchers on a delay and late ones, the recording as a replay file, the rules of the pace, the window client joining a server, a desync
named by its parts - §18 and §19; `docs/NETWORK-DIAGNOSTICS.md`) and Phase 28 (12 October 2026: the sealed transport and the server's key, limits with counted
reactions, a room password, removing a member, orders nobody can give, who decides - §20 and §21; `docs/DEDICATED-SERVER.md` §9 to §13). Frame:
`docs/MULTIPLAYER-PLAN.md` §2 (lockstep through a relay), `docs/SIMULATION-CORE.md` §45 to §47 (session core, the leave command), `docs/PRESENTATION-INPUT.md` §13 (command
path, tick source), `docs/DEDICATED-SERVER.md` (the server as a program). Code: `src/CnCNew.Net` (messages, transport interface, loopback, client, clock synchronisation and
playback control), `src/CnCNew.Net/Udp` (the UDP transport), `src/CnCNew.Server` (room and relay, the directory of rooms), `src/CnCNew.Game/Lockstep` (lockstep driver,
embedded server, the client without a window, the multi-client probe), `tools/CnCNew.DedicatedServer`, `tools/CnCNew.NetTool`.

**What version 2 added to version 1:** the room name in the hello; `Ping`/`Pong`, `LagState`, `Leave` (messages 26 to 29); the drop reason "too slow" and the reject reason
"server full"; the leave of a player as a command inside a bundle (command table 17, replay format 15). A version 1 client is rejected with the reason "protocol version".

**What version 3 added to version 2** (§18): a rejoin token and a watch flag in the hello, the token in the welcome; `DesyncDetail`, `DesyncReport`, `SnapshotRequest`,
`SnapshotOffer`, `SeatState` (messages 30 to 34); the reject reasons "seat not held" and "cannot catch up". No format of the simulation changed: command table 17,
replay format 15 and every reference value are those of Phase 26. A version 2 client is rejected with the reason "protocol version".

**What version 4 added to version 3** (§20), together with **transport version 2**: every datagram after the handshake is sealed, and the handshake carries a key
exchange signed by the server's permanent key; the room verifier in the hello, `PasswordChallenge`, `PasswordResponse`, `Kick` (messages 35 to 37); the reject reasons
"bad password" and "kicked", the drop reasons "kicked" and "flood", the end reason "server limit". No format of the simulation changed. A version 3 client is
rejected with the reason "protocol version"; a datagram of transport version 1 is dropped and counted.

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
| Protocol version | `Protocol.Version` | 3 |
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
| 30 to 34 | `DesyncDetail`, `DesyncReport`, `SnapshotRequest`, `SnapshotOffer`, `SeatState` | | version 3: §18.1, with the two fields it added to `Hello` and the one it added to `Welcome` |

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

No encryption, no authentication beyond the cookie of §12.3 and the rejoin token of §18.2, no flood protection beyond the per-tick limit and the connection limit, no
congestion control (Phase 28); no votes (§18.5); the setup screen is not yet a room others can join, and the window client reaches a foreign server only by its command
line - there is no lobby screen (29); a room's listing does not exist: a client must know the name of its room (29). *Closed by Phase 27 (§18):* rejoin, a watcher
joining a running match, the desync diagnosis below "this tick", a rule for pause and speed, the window client's entry to a foreign server.

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
| 2 | `u8` transport version, **1** (Phase 26) - **2 since Phase 28** (§20.1) | another version is dropped and counted |
| 3 | `u8` kind | 1 request, 2 challenge, 3 connect, 4 accept, 5 data, 6 close, 7 refuse |

### 12.3 Connection setup with a stateless cookie

*(Transport version 1. Version 2 keeps the cookie and adds a key exchange to connect and accept, a second refuse reason, and seals data and close: the table of
§20.1 replaces the one below; the rules under it hold unchanged.)*

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
- The **connection token** is the first 8 bytes of the same HMAC with the prefix `0x02` - **since Phase 28 over the address and the client nonce only, not the time
  slot** (§21.6: with the slot in it, a client that held the cookies of two slots was replaced by its own second connect); every later datagram of the connection
  carries it, and a datagram from the right address with the wrong token is dropped. A repeated connect (its accept was lost) derives the same token and is answered again; a connect with another nonce from the same address
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
  (`srtt` plus the largest of `4 · rttvar`, a quarter of `srtt` and 15 ms - on a line without jitter the variance goes to zero, and a timer equal to the round trip fires
  just before every acknowledgement -, between `MinRetransmitMs` 30 and `MaxRetransmitMs` 2,000; `InitialRetransmitMs` 200 before the first measurement; doubled per
  repetition of the same segment, at most 16-fold), or at once - one time per gap - when the acknowledgement mask shows two later segments arrived (fast retransmission).
- **Receiver:** segments are accepted up to `WindowSegments` ahead of the next expected one, held until the gap closes, and appended to the stream in order; a duplicate is
  counted and ignored. From the stream the receiver takes whole messages. A length prefix above the connection's message limit (64 MiB + 64 KiB, the protocol's largest message)
  is a violation: the connection is closed. The buffer grows with the bytes that actually arrive, never with what a prefix announces.
- There is no congestion control beyond the fixed window (a match is a few kilobytes per second; operating an open port is Phase 28).

### 12.6 The urgent channel

Everything `IConnection.SendUrgent` is given: **what a running match must not wait for behind a lost packet** - server to client the tick bundles and the pongs; client to
server the command intents, the hash reports, the pings, the tick requests of a client-paced room and the report of the end. Everything else (hello, room, start, loading,
chat, pace, lag and drop notices, leave, desync, end) goes the reliable way. An urgent message is numbered; it is delivered in number order, once, like a reliable one, but
it is sent differently: **every datagram that goes out - with a new urgent message, with reliable segments, or as an acknowledgement - also carries the urgent messages
the peer has not acknowledged yet**, oldest first - each at most `UrgentRedundantSends` (3) more times, not more often than every `UrgentRepeatSpacingMs` (8; copies sent in
one breath share the fate of a burst of loss), and within a budget of bytes per flush (`UrgentRedundancyBytes`: one datagram in Phases 26 and 27, **four
datagrams' worth and whole messages of several fragments since Phase 28**, §21.5; what does not fit is counted in `UrgentOmitted` and follows with the next
datagram or the retransmission timer). A single lost datagram therefore costs no waiting: the
next one, one tick later, brings the missing bundle. Three more copies survive every loss a playable line has (at 3 % all four are lost once in a million, and then the
retransmission time repeats the message); without the bound a datagram carried everything sent during a round trip - measured with eight clients, 8.7 KB/s down per
client at 10 ms delay and 39.7 KB/s at 150 ms.

**Why the client's reports are urgent too.** The first measurement had them on the reliable channel, where a lost packet holds back everything behind it for a
retransmission time: at 150 ms delay and 1 % loss the server saw healthy clients fall behind by seconds (517 lag notices in 100 s, the game slowed for 4 s), an order
took up to 2.8 s, and the clients' clock samples were wrong. With intents, hash reports and pings on the urgent channel the same line has 8 notices, no slowing, and
the 99 % value of an order is 362 ms instead of 2,760. A message
larger than one datagram is cut into fragments (at most 64) that are sent in consecutive datagrams; since Phase 28 such a message is repeated early like a small one
when all of it fits the budget, and otherwise by the retransmission timer (before, always by the timer - §21.5 is what that cost). Without new
messages (pause, the last tick) unacknowledged urgent messages are repeated by the same retransmission time as segments.

**Order holds per channel, not between the two.** The protocol tolerates it: a client takes bundles in the loading phase as well as in the match, and everything else the server
sends (room state, pace, lag, drop notice, end) does not depend on its position between two bundles - what a dropped player means for the simulation is a command *inside* a
bundle (§14). The loopback transport has the same two lanes, so the probes in one process exercise the overtaking too.

### 12.7 Keep-alive, timeout, close

A connection that has sent nothing for `KeepAliveMs` (1,000) sends an empty data datagram; one that has received nothing for `TimeoutMs` (10,000) is closed (`IsOpen` false; the
relay drops the member with `ConnectionLost`). An acknowledgement rides with the next datagram that goes out anyway; when none has gone for `AckDelayMs` (20) it gets one
of its own. An end that sends every tick therefore acknowledges for free.

`Close()` closes the connection for its owner at once, but **what was sent before the close is still delivered**: the reliable channel goes on sending and repeating until
everything is acknowledged or `LingerMs` (2,000) have passed - a rejection or the end of a match would be of no use otherwise. Then the close datagram goes out three times,
one per flush; the peer closes at the first it receives. A closed end takes nothing in any more except the acknowledgements it waits for.

### 12.8 Numbers

All of them are fields of `UdpOptions` with these defaults: `ConnectRetryMs` 250, `ConnectTimeoutMs` 5,000, `CookieLifetimeMs` 10,000, `KeepAliveMs` 1,000, `TimeoutMs` 10,000,
`LingerMs` 2,000, `InitialRetransmitMs` 200, `MinRetransmitMs` 30, `MaxRetransmitMs` 2,000, `WindowSegments` 256, `AckDelayMs` 20, `UrgentRedundancyBytes` 1,155, `UrgentRedundantSends` 3, `UrgentRepeatSpacingMs` 8, `UrgentWindow` 1,024 (urgent messages
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
  swinging (tested with a clock skewed by 1 %, a hundred times a bad crystal). It works only on deviations below `IntegralBandMs` (20) and contributes at most
  `MaxIntegralPerMille` (15): a large correction - the delay changed - is the first term's work and must not be remembered to overshoot when it is over (the first
  measurement with eight processes showed exactly that: the position ran ahead for seconds after the start-up, and 4.7 % of the ticks waited for their bundle).
- **Two exceptions set the position directly:** the first time there is an estimate *and* `MinArrivals` (8) measured arrivals - a delay taken from nothing would be wrong for
  the first second -, and a deviation beyond `ResyncMs` (1 s: the way changed, the process was suspended), which is counted (`Resyncs`).
- **Without an estimate** (no clock, the first moments of a match, after a change of speed) a tick is due as soon as its bundle is there.
- **The client's own frames are not the network's jitter.** A client looks for messages once per frame, so a bundle is noticed up to a frame after it arrived. The usual
  frame (the median of the last 31 times between two looks) is part of what the delay has to cover and stays in the lateness; what a longer frame adds beyond twice the
  usual one - a long tick of the client's own simulation - is taken off; and a bundle taken in after the client stood still for more than `HitchMs` (50: a garbage
  collection, a very long frame) is played but not measured at all.
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

The server knows how far every member is: the last tick it completed, from its hash reports and its pings. The game time between that tick and the last one the server
closed is not yet lag: a healthy client is behind by its playback delay plus the way of its reports all the time - 50 to 500 ms on the lines of §16. **Lag is what goes
beyond the least that member showed in the last two windows of 5 s** (at most a second is taken off: a member that is always further behind than that is behind).

| Lag of a member | What the server does | Option (default) |
|---|---|---|
| from 500 ms | tells everybody (`LagState`: who, how far), repeated every 250 ms while it lasts, withdrawn below half the limit - **only shown** | `LagReportMs` (500) |
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

## 16. Measured over real sockets (Phase 26, 12 October 2026, 60 Hz, build `1844afa`, Windows, Release)

Everything here is **one machine** (the development machine: 8 processors, Windows 11): the dedicated server, the proxy and every client are processes of their own that
talk over UDP sockets of the loopback interface, and the proxy does to the datagrams what a line would. **No byte has crossed a cable**, and no second machine with another
CPU has taken part - register E1 of `docs/MULTIPLAYER-PLAN.md` is as open as before. What is measured is the protocol, the transport, the control and the processes, not a
real network's burst losses or route changes.

**Set-up of every match:** `Maps/Twilight Flame`, eight participants - **six players, each a client process with a command generator, and two computer players** that
every client simulates -, plus **two watching client processes**: eight client processes in all. The clients are warmed up (a throw-away match of ten game seconds before
they connect) and look for messages every 4 ms. Delay and jitter are per direction; loss is per datagram and direction.

### 16.1 The matrix (`NetTool net-matrix`, 100 s of game time per line: 6,000 ticks on each of eight clients)

| Delay | Jitter | Loss | Hashes identical | Order to execution: median | 95 % | 99 % | Round trip | Playback delay | Waits (of 48,000 ticks) | Up B/s | Down B/s | Repetitions client / server segments / server bundles |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 10 ms | 0 ms | 0 % | yes (8 of 8) | 33 ms | 36 | 116 | 20 ms | 15 ms | 614 | 6,031 | 6,003 | 0 / 57 / 54,010 |
| 10 ms | 0 ms | 1 % | yes (8 of 8) | 43 ms | 54 | 65 | 18 ms | 24 ms | 464 | 6,254 | 6,312 | 0 / 43 / 60,473 |
| 10 ms | 0 ms | 3 % | yes (8 of 8) | 50 ms | 55 | 86 | 20 ms | 30 ms | 149 | 6,241 | 6,050 | 0 / 44 / 60,097 |
| 10 ms | 10 ms | 0 % | yes (8 of 8) | 49 ms | 53 | 58 | 30 ms | 24 ms | 110 | 7,680 | 7,573 | 0 / 36 / 99,360 |
| 10 ms | 10 ms | 1 % | yes (8 of 8) | 50 ms | 64 | 70 | 31 ms | 30 ms | 381 | 7,543 | 7,608 | 1 / 35 / 95,059 |
| 10 ms | 10 ms | 3 % | yes (8 of 8) | 63 ms | 69 | 87 | 28 ms | 37 ms | 278 | 7,464 | 7,399 | 0 / 41 / 92,723 |
| 10 ms | 30 ms | 0 % | yes (8 of 8) | 77 ms | 87 | 101 | 52 ms | 44 ms | 119 | 9,187 | 9,011 | 1 / 18 / 140,480 |
| 10 ms | 30 ms | 1 % | yes (8 of 8) | 81 ms | 93 | 108 | 50 ms | 45 ms | 244 | 9,236 | 9,349 | 2 / 21 / 140,260 |
| 10 ms | 30 ms | 3 % | yes (8 of 8) | 82 ms | 100 | 113 | 52 ms | 50 ms | 388 | 9,175 | 9,052 | 0 / 18 / 138,791 |
| 40 ms | 0 ms | 0 % | yes (8 of 8) | 99 ms | 107 | 129 | 80 ms | 45 ms | 248 | 9,911 | 10,046 | 1 / 39 / 156,070 |
| 40 ms | 0 ms | 1 % | yes (8 of 8) | 100 ms | 116 | 134 | 80 ms | 54 ms | 546 | 9,845 | 9,427 | 0 / 35 / 156,099 |
| 40 ms | 0 ms | 3 % | yes (8 of 8) | 107 ms | 119 | 168 | 80 ms | 60 ms | 166 | 9,879 | 9,496 | 0 / 33 / 156,078 |
| 40 ms | 10 ms | 0 % | yes (8 of 8) | 107 ms | 118 | 119 | 90 ms | 54 ms | 67 | 9,916 | 10,103 | 1 / 13 / 156,118 |
| 40 ms | 10 ms | 1 % | yes (8 of 8) | 114 ms | 121 | 134 | 92 ms | 60 ms | 387 | 9,890 | 9,800 | 1 / 25 / 156,126 |
| 40 ms | 10 ms | 3 % | yes (8 of 8) | 116 ms | 128 | 137 | 90 ms | 66 ms | 285 | 9,879 | 9,507 | 0 / 16 / 156,201 |
| 40 ms | 30 ms | 0 % | yes (8 of 8) | 135 ms | 150 | 154 | 95 ms | 74 ms | 107 | 9,877 | 9,773 | 0 / 13 / 156,214 |
| 40 ms | 30 ms | 1 % | yes (8 of 8) | 134 ms | 152 | 164 | 70 ms | 75 ms | 266 | 9,872 | 9,665 | 0 / 17 / 156,212 |
| 40 ms | 30 ms | 3 % | yes (8 of 8) | 145 ms | 155 | 182 | 96 ms | 79 ms | 377 | 9,912 | 9,789 | 0 / 18 / 156,274 |
| 80 ms | 0 ms | 0 % | yes (8 of 8) | 167 ms | 182 | 187 | 160 ms | 86 ms | 189 | 9,910 | 10,022 | 1 / 12 / 156,288 |
| 80 ms | 0 ms | 1 % | yes (8 of 8) | 182 ms | 189 | 250 | 160 ms | 94 ms | 413 | 9,853 | 9,491 | 0 / 16 / 156,348 |
| 80 ms | 0 ms | 3 % | yes (8 of 8) | 183 ms | 193 | 213 | 160 ms | 100 ms | 160 | 9,841 | 9,189 | 1 / 17 / 156,377 |
| 80 ms | 10 ms | 0 % | yes (8 of 8) | 184 ms | 198 | 201 | 171 ms | 94 ms | 71 | 9,918 | 10,091 | 1 / 12 / 156,325 |
| 80 ms | 10 ms | 1 % | yes (8 of 8) | 194 ms | 202 | 219 | 170 ms | 100 ms | 387 | 9,911 | 9,914 | 1 / 14 / 156,353 |
| 80 ms | 10 ms | 3 % | yes (8 of 8) | 200 ms | 206 | 222 | 172 ms | 107 ms | 276 | 9,882 | 9,458 | 2 / 23 / 156,324 |
| 80 ms | 30 ms | 0 % | yes (8 of 8) | 215 ms | 233 | 237 | 166 ms | 114 ms | 145 | 9,907 | 10,021 | 6 / 7 / 156,389 |
| 80 ms | 30 ms | 1 % | yes (8 of 8) | 217 ms | 235 | 247 | 192 ms | 116 ms | 239 | 9,908 | 9,934 | 5 / 16 / 156,431 |
| 80 ms | 30 ms | 3 % | yes (8 of 8) | 224 ms | 238 | 256 | 193 ms | 120 ms | 389 | 9,857 | 9,297 | 7 / 17 / 156,413 |
| 150 ms | 0 ms | 0 % | yes (8 of 8) | 316 ms | 320 | 331 | 300 ms | 155 ms | 300 | 9,891 | 9,794 | 8 / 5 / 156,618 |
| 150 ms | 0 ms | 1 % | yes (8 of 8) | 325 ms | 335 | 340 | 300 ms | 164 ms | 476 | 9,849 | 9,439 | 8 / 1 / 156,641 |
| 150 ms | 0 ms | 3 % | yes (8 of 8) | 333 ms | 343 | 382 | 300 ms | 170 ms | 152 | 9,878 | 9,456 | 10 / 8 / 156,689 |
| 150 ms | 10 ms | 0 % | yes (8 of 8) | 331 ms | 335 | 341 | 311 ms | 164 ms | 107 | 9,877 | 9,714 | 8 / 24 / 156,632 |
| 150 ms | 10 ms | 1 % | yes (8 of 8) | 333 ms | 341 | 578 | 312 ms | 172 ms | 367 | 9,868 | 9,528 | 9 / 14 / 157,124 |
| 150 ms | 10 ms | 3 % | yes (8 of 8) | 335 ms | 351 | 364 | 311 ms | 177 ms | 293 | 9,891 | 9,537 | 10 / 26 / 156,734 |
| 150 ms | 30 ms | 0 % | yes (8 of 8) | 353 ms | 368 | 372 | 332 ms | 184 ms | 113 | 9,911 | 10,049 | 8 / 7 / 156,738 |
| 150 ms | 30 ms | 1 % | yes (8 of 8) | 358 ms | 369 | 382 | 288 ms | 186 ms | 283 | 9,892 | 9,787 | 9 / 17 / 156,748 |
| 150 ms | 30 ms | 3 % | yes (8 of 8) | 363 ms | 382 | 400 | 333 ms | 190 ms | 386 | 9,871 | 9,422 | 9 / 18 / 156,771 |

- **Hashes: 36 of 36 lines identical on all eight clients** - every client reported the same chain over the session hashes of all 6,000 ticks, and the server agreed on
  every tick. 15,763 orders went through the relay. No desync, no discarded message, no malformed datagram, no resynchronisation of a playback position, the game never
  slowed.
- **Order to execution** (from the moment a client gives an order to the tick its own simulation executes it in): the median is **twice the delay plus 7 to 19 ms** on a
  line without jitter and loss, plus 20 to 43 ms with 1 to 3 % loss or 10 ms of jitter, plus 53 to 65 ms with 30 ms of jitter - the way up, on average half a tick until
  the server closes one, and the playback delay, which is the way back plus what jitter and loss need. The 99 % value lies within 60 ms of the median on 32 of the 36
  lines; on three it is 62 to 83 ms above it, and on one (150/10/1) 245 ms - single orders in the first seconds.
- **The playback delay** follows the line in milliseconds, the same at every delay: the way plus 5 ms on a quiet line, plus 14 ms with 10 ms of jitter or 1 % loss, plus
  20 ms with 3 % loss, plus 27 ms with 10 ms of jitter and 3 % loss, plus 34 to 40 ms with 30 ms of jitter.
- **Waits** (a tick whose playback time came before its bundle; it is played a few milliseconds later): 9,942 of 1,728,000 ticks, 0.58 %; the worst line 1.3 %.
- **Bandwidth per client: 6 to 10 KB/s in each direction** (UDP payload; 60 datagrams per second each way), independent of delay and loss. Upward it is the hash report of
  every tick with its three copies; downward the bundles with theirs. The interval of the hash reports is a parameter of the server (`hashInterval`); nothing here is tuned
  for a narrow line.
- **Repetitions:** on the reliable channel the eight clients together repeated 0 to 10 segments per line and the server 1 to 57; every bundle rode along three more times
  as designed (the last number of the table).
- **The server's ticks** (216,355 closings over the 36 matches): 98.7 % within 1 ms of the tick length, 99.9 % within 2 ms, the worst 12.2 ms, no burst.
- **The clients:** no gen-2 collection in any match; the longest frame of any client 139 ms - the tick maximum of the eight-player simulation (decision D-3), which the
  playback absorbs: the client plays the ticks it missed in the next frames.

### 16.2 Against the calculation of `docs/MULTIPLAYER-PLAN.md` §3

The plan estimated, for a round trip of 30 ms at 60 Hz: round trip 30 + wait for the open tick 8 + playback buffer of one tick 17 + display lag of the interpolation 17 =
72 ms. Measured without the display (a client without a window has none): **33 ms at a round trip of 20 ms, 49 ms at 30 ms with 10 ms of jitter**. The calculation's 55 ms
without the display term were too high on a quiet line, because the buffer is not a whole tick but the measured jitter plus 4 ms; they are about right when the line
jitters by 10 ms; and they are too low for a line with 30 ms of jitter (77 ms at a round trip of 52 ms), where the buffer has to be two ticks. The display lag of the
window client is Phase 29.

### 16.3 A client that hitches, leaves, vanishes, freezes or is too slow (120 s of game time, 40 ms / 10 ms / 1 %, the rule's defaults)

| What happens to one client at tick 1,800 | The other seven | The server | The game ran slowed for |
|---|---|---|---|
| it stands still for 500 ms once | in step to the end, all eight | one lag notice, nobody dropped | 0 ms |
| it says it leaves (`Leave`) | in step to the end | `left ... reason=Left` at tick 1,806; the leave is in that tick's bundle | 0 ms |
| its process vanishes without a word | in step to the end | shown as behind; slowed from 2 s on; `ConnectionLost` at tick 2,164 when the transport's 10 s without a datagram are over | 7,997 ms |
| its process freezes (the socket stays open) | in step to the end | the same: `ConnectionLost` at tick 2,166 | 8,005 ms |
| a player's client needs 25 ms more per tick than it has | in step to the end | slowed three times (the client catches up at half speed and falls behind again at full speed); dropped as `TooSlow` at tick 2,992, 20 s of game time after it began | 14,358 ms in three incidents |
| a watcher's client needs 25 ms more per tick | in step to the end | never slowed; the watcher dropped as `TooSlow` at tick 2,737 | 0 ms |

In every case the seven others reported one hash chain and the server agreed on every tick: the leave of the player - everything it owned died in the same tick on every
client - is part of what they agree on. What the others lost is what the rule says: nothing for a hitch and a leave, 8 s at half speed (4 s of game time) for a lost
connection, and for a client that cannot keep up 14.4 s at half speed in three incidents: after the first it had caught up below the report limit and the count of 10 s
began again, the second and the third together reached it. That is the rule as documented - and a point to look at when the decision is made: a client that can only
keep up with the slowed game costs the others more than one that is simply gone.

### 16.4 A whole standard match (`NetTool net-match --minutes 60`, 40 ms / 10 ms / 1 %)

Eight client processes (six players, two watchers; two computer players), **60 minutes of game time - the whole length of a standard match, 216,000 ticks** - through the server and the proxy: **all eight clients reported the same hash chain**, the server agreed on every tick (`lastAgreedTick=215999`, no desync, nothing discarded), 90,631 orders went through the relay. Order to execution: median 116 ms, 95 % 120 ms, 99 % 166 ms (the worst single order 241 ms) at a round trip of 91 ms; playback delay 59 ms on average; 14,275 of 1,728,000 ticks (0.83 %) waited a few milliseconds for their bundle; no resynchronisation; the game never slowed, nobody was shown as behind, nobody dropped. 10.6 KB/s up and 15.4 KB/s down per client (the late match has more commands in its bundles). The server closed 215,999 ticks: 99.2 % within 1 ms of the tick length, the worst 6.7 ms late, no burst.

**Memory:** every client's managed memory went from 207 MB to 237 to 240 MB in the hour (the world of a long match has more objects; the tool also keeps its samples) and its working set from about 345 MB to 317 MB - nothing grows without bound; 14 to 16 gen-2 collections per client in the hour, the longest client frame 135 ms. **The server's managed memory grew from 1 MB to 22 MB**, linearly: it keeps every bundle of the match as its recording (`RelayServer.Recording`) - by design until Phase 27 writes it out, and the reason an hour-long match costs the server about 20 MB.

### 16.5 What these measurements do not show

- **A second machine.** Two computers with different CPUs and .NET versions have not played together; whether their simulations agree is what the reference manifest on
  such a machine would show first (E1, D-10). The Linux runtime under WSL was not available in this phase (no .NET in the distribution; installing it is a download that
  needs the owner's word).
- **A real network:** burst loss, route changes, an asymmetric line, a NAT, the MTU of a tunnel.
- **A window.** No client with a renderer has joined a server; the display lag, the interpolation against the playback position and what a player sees when the game is
  slowed are Phase 29.
- **Other tick rates and maps.** The matrix ran at 60 Hz on one map; `Net.Tests` run at 30, 60 and 120 Hz on a synthetic fixture.
- **More than an hour, more than one room at a time under load, and hostile clients** (Phase 28).

## 17. Closing measurements of Phase 26 (12 October 2026, state `f25fa7a`, Windows, Release)

**Tier 4** (`pwsh scripts/test.ps1 -Tier full -KeepGoing`): 41 of 41 steps ok (4,573 s) on `f25fa7a` - clean build 0 warnings; every test program; compat-check 713; `Simulation.Tests` at 30, 60 and 120 Hz, with real data (rate-equivalence suite 0 / 0) and the long run; `Game.Tests` and `Presentation.Tests` with real data at 30, 60 and 120 Hz; `Net.Tests` (34 tests) at 30, 60 and 120 Hz; `input-manifest check` identical; `reference-manifest check --set full` 50 of 50 identical; the three client checks; `check-match`; `session-check`; `net-probe`; **`net-match`** (the dedicated server, the proxy and four client processes: a match with delay, jitter and loss, and one with a client that vanishes); the staged scenes. The first run of the tier, on `1844afa`, had 40 of 41 steps ok: `net-30` failed in a test whose "too slow" client was 25 ms per tick at every rate - too slow at 60 and 120 Hz, fast enough at 30 Hz, where a tick is 33 ms; the test was corrected (`f25fa7a`) and the whole tier run again.

**The probes of Phase 25, re-measured** (one process, the loopback): `NetTool probe-suite` 48 of 48 runs hash-identical on all clients in every tick and equal to their replay (1+7, 2+6, 4+4, 8+0 on three eight-player maps, with and without delay and jitter, twice each; 562 s) - now with the adaptive playback delay on the probe's clock: an order of a server-paced run without delay is executed one tick after it was given, with 40 ms delay and 60 ms jitter 11.9 ticks on average (13 with the fixed buffer of Phase 25). `NetTool local-cost` (eight players, five game minutes, three rounds): the local skirmish through the embedded relay costs 653 µs per tick against 621 µs directly (+32 µs; +22 µs in Phase 25), both ending in the same session hash; an order of the local game is still executed in the tick it was given.

**Campaign** (missions stay local): Campaign at 60 Hz (missions stay local): reachability clean 24/24 and 24/24, audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and 15/24, objective driver victory 24/24 and defeat 24/24 - as before the phase.

**Performance of the simulation** (`scripts/perf-profile.ps1` with the binaries of `71f477e` and of this state one after the other): every run ends in the same final hash on both sides; the means lie within -1.0 % and +1.2 % (eight players, 30 game minutes: 849.1 against 841.1 µs per tick, 99 % 2.1 ms on both sides, maximum 137 against 139 ms, allocation 8.4 KB per tick; `MD_GLA05` 2,605 against 2,587 µs). The phase is not measurable in the tick of the simulation; the tick maximum (decision D-3) is unchanged.

**Not run:** the determinism matrix; the survey over 320 matches; the commands-only agent; `sync-perf` of the game layer; `scripts/acceptance.ps1`; the matrix at 30 and
120 Hz and on other maps. **Not possible in this phase:** a second physical machine (E1, D-10) and with it the LAN measurement; Linux as a second environment (the WSL
distribution of the development machine has no .NET runtime). **No human has seen** a match through the dedicated server.

## 18. When something goes wrong, and when somebody comes late (Phase 27)

Phase 26 ran a match as long as nothing went wrong. This section is what the protocol does about the three things that are certain to happen in operation: a client's
state diverges, a connection is lost, somebody wants to watch. **Protocol version 3.** The transport is unchanged (version 1).

### 18.1 What is new on the wire

| No. | Message | Direction | Fields |
|---|---|---|---|
| 1 | `Hello` | client → server | as before, and: **rejoin token** (`u64`; 0: none), **watch** flag (ask to watch a match that already runs) |
| 2 | `Welcome` | server → client | as before, and: the **rejoin token** of this connection (0: this server hands out none) |
| 30 | `DesyncDetail` | client → server | tick, the parts of this client's session hash at that tick: name (48 bytes) and value, at most 32. Sent once, after a `Desync` |
| 31 | `DesyncReport` | server → all | tick, the parts in which the clients differ, with the value of every connection that sent its detail |
| 32 | `SnapshotRequest` | server → one client | request number: hand over the state of your session, for somebody who joins |
| 33 | `SnapshotOffer` | client → server | request number, a resume state (as in a start request: tick, session hash, orders so far per human player, the state) |
| 34 | `SeatState` | server → all | player, what became of its seat (held, returned, given up), milliseconds it is held for |

`RejectReason` 12 `SeatNotHeld` (the token opens no seat: never in this match, or the time is over) and 13 `CannotCatchUp` (no member could hand over a state and the match
is too long to play again; the state built does not hash to the match; the match was not built in time). All seven travel on the reliable channel; none depends on its
place between two bundles. A snapshot offer may carry a state of up to 64 MiB like a start request.

### 18.2 A seat that is held, and the way back into a running match

**The token.** A server that holds seats (`RelayOptions.SeatHoldMs` > 0) or admits latecomers gives every connection a token in its `Welcome`, drawn from the server's
seed source (the dedicated server: the system's cryptographic generator). It is the only proof of "I am the client that sat here"; it is 64 bits, sent once in each
direction, and never to another member.

**Held.** In a server-paced match a player whose connection is **lost** (transport timeout; superseded, see below) does not leave the match: its seat is held for
`SeatHoldMs`. The room tells everybody (`SeatState` held) and logs `seat-held`; the simulation is told nothing - the player's units stand and fight as units without
orders do, and nobody else is slowed (a held seat has no member that could be behind). A player that **leaves by choice** (`Leave`), is dropped for a protocol violation
or for being too slow gets no held seat. When the time is over the room logs `seat-given-up`, tells everybody, and only now issues the `PlayerLeaveCommand` in the next
bundle (§14.2) - "seat held" changes *when* that command comes, not what it does.

**Back.** A hello with a token while the match runs: if the token is that of a held seat, the connection gets the seat's player number (`seat-returned`). If the token is
that of a connection that still stands - the client was restarted, or came back on another address before the transport noticed the old one is dead - the old connection
is closed as lost and its seat held and returned in the same step (`superseded`); the first rejoin over real sockets found that case. A token nobody was given is turned
away with `SeatNotHeld`. The joiner is `Joining`: it is sent no bundles, is not asked for hashes and is not measured by the lag rule.

**The state comes from a member, checked by the server without simulating.** The room asks one member that plays in time for its state (`SnapshotRequest`; the first by
connection number that was not asked yet). The member writes the state of its session between two ticks - at a tick whose predecessor it reported a hash for - and
sends it with that tick and its session hash (`SnapshotOffer`). The server never looks into the state. It waits until **every** member that plays in time has reported
the tick before the state's tick, and passes the state on only if the stated hash is the one they agreed on. A state with another hash - damaged on its way, made up,
or written by a client that has itself diverged - goes nowhere (`snapshot-refused`), and the next member is asked; so is the next one when a member does not answer
within 30 s (`snapshot-late`) or disconnects. The joiner gets an ordinary `MatchStart` with the description, its player number and the state as resume state, builds
the session, and reports `Loaded` with the hash of what it built. If that is not the agreed hash the joiner is turned away (`join-refused`, `CannotCatchUp`) and the
seat is held again for the rest of its time: nobody plays on a state that does not hash to the match. A client that cannot even read the state reports hash 0, which is
the same refusal.

**Catching up.** After a correct `Loaded` the room sends `AllLoaded` with the state's tick (`joined-late`) and then every bundle since, from its recording, at most 512
ticks ahead of what the joiner has completed. The joiner plays them as fast as its frame allows (bundles of the past are due at once) and is `CatchingUp` until it is
less than the report limit behind: until then it is not waited for in the agreement of hashes (what it reports is still compared with the hash the others agreed on,
and a difference is a desync like any other) and does not slow the game. A joiner that has not caught up after `CatchUpLimitMs` (five minutes) is dropped as too slow.
The bundles a server closed before it began to send to the joiner are not counted as late arrivals by the joiner's playback (§13.2); before that was so, a joiner
played a second behind the others for half a minute.

**Without a member that can give its state** (none is left that plays in time, or all were refused) the joiner is sent the match as it began and every bundle since -
the fallback of the brief. It is allowed up to `CatchUpLimitTicks` (432,000: two hours at 60 Hz) and refused beyond (`CannotCatchUp`). At the catch-up speed measured in
Phase 23 (26-fold at 60 Hz for eight players) an hour of match is a little over two minutes.

**The order numbers** of a player continue: the state carries, per human player, how many commands the session has taken from it, as a saved match does (§5).

### 18.3 Watchers: on a delay, and late

A watcher is a member without a seat, as before; what it sends as orders is discarded and counted, as before.

**Delay** (`RelayOptions.SpectatorDelayMs`): in a server-paced match every bundle goes to a member without a seat that much later than to the players - the room keeps a
position per member in its recording (`NextBundle`) and sends up to the tick that was closed the delay ago. The watcher's clock answer (`Pong`) is behind by the same
time, so its playback follows the delayed stream like any stream. Its hash reports arrive late and are compared with the hash the players agreed on for that tick, which
the room remembers for the delay and thirty seconds more: a watcher that diverges is found like a player. A delayed watcher is not part of the agreement, of the end of
the match or of the lag rule. When the match ends the players are told at once; a delayed watcher is told when it has been sent the last bundle, and the room stays
until it has. A local game (one member, client-paced) has no delay.

**Late** (`RelayOptions.AllowLateWatchers`): a hello with the watch flag while the match runs is admitted without a seat. Without a delay it joins exactly as a
returning player does, by the state of a member. **With a delay it must not be handed the present** - the state of a member is the match as it is now, which is what
the delay withholds - so it is sent the match as it began and plays the recording up to its delayed position (the catch-up limit applies). A server that does not admit
latecomers answers `MatchRunning`, as before.

### 18.4 The recording as a file

`RelayServer.Recording` holds the description and every bundle, as since Phase 25. `CnCNew.Net.RecordingFile.ToReplay` writes them in the simulation's own replay
format (version 15): the description in the header, the commands of the bundles each with its player's order number as sequence, no computer players' commands - they
are slots of the description and decide again when the file is played (`SkirmishReplay.Play`). A leave is a command of a bundle and therefore in the file. The room
writes nothing itself (the layer rule: `CnCNew.Server` opens no file); `RoomDirectory.TryTakeFinished` hands a finished room to its host once - when the match ended,
or when everybody walked away from it - and the dedicated server writes the file and a note beside it (`docs/DEDICATED-SERVER.md` §8). The server's memory still grows
with the match (about 20 MB an hour, §16.4); it is released when the room is closed, and the file is what stays. A match that was continued from a saved state does not
begin at tick 0 and gets no file.

`CnCNew.NetTool replay-check <content> <file>` plays the file and compares the session hash with the last one the clients of that match agreed on, from the note. The
relay never simulated; that its file leads to the clients' hash is what makes it the record of the match.

### 18.5 The pace: pause, speed, and the rule for members that fall behind

**In a server-paced match** only players may ask for a pause or a speed (`PaceRequest` of a watcher is refused, `InvalidRequest`). A player may **pause**
`PausesPerPlayer` times a match (3); a pause ends when any player ends it or after `MaxPauseMs` (60 s; 0: no limit), whichever is first (`paused`, `resumed` in the
log). The **speed** is the host's to set unless `HostSetsSpeed` is off. A local game keeps its own pause and speed without limits. None of it is a command: pause and
speed are the server's clock, and the simulation sees ticks.

**Votes are not built.** The brief names them next to the pause; nothing in this phase needs one, and what a vote would decide (kick a player, end a match, change the
speed) is the owner's to say (D-23). The place for it is here: a request, a state the server broadcasts, a result that is either server state (speed, pause) or a
command of a bundle (a leave).

**The lag rule** (§14.1) had two weak points on record (D-17). (1) A member that can only keep up with the slowed game recovered whenever the game was slowed, its count
of slowed time began again, and three incidents carried it for 14.4 s instead of 10. Now a member's count is forgotten only after it has been below the report limit
for `LagRecoveryMs` (30 s; 0 restores the rule of Phase 26): a member that falls behind again within that time uses up the rest of the same 10 s. (2) A connection that
closed after the match had ended was logged as `ConnectionLost`; it is logged as `Closed` and told to the others as a leave. The numbers stay a proposal.

### 18.6 The window client joins a server

`CnCNew.Game.Lockstep.RemoteRelay` is what `LocalRelay` is for the embedded server: a UDP socket, the transport, the protocol client and the system's clock
(`SystemNetClock`). `LiveMatch.Join(data, remote, viewport)` builds the match the server describes - or loads the state a member handed over -, reports its hash, and
from then on takes every tick and every command from the server through the same `LockstepDriver` as a local match; the driver is server-paced and follows the server's
clock (§13.2). A joined match hands over its own state when the server asks, answers a desync with the parts of its hash and writes its package (§18.7). There is **no
lobby screen**: the client is told the room on its command line (`--join ADDRESS:PORT --room NAME --seat N` or `--watch`), enters it, takes the seat as the host set it
and waits for the host's start. The lobby is Phase 29.

### 18.7 A desync, named

The relay ends the match at the first tick two hash reports differ for and tells everybody (`Desync`), as before. New: every client then sends the **parts** of its
session hash at that tick (`DesyncDetail`), the relay compares them and tells everybody in which parts the clients differ (`DesyncReport`; `desync-parts` in the log,
repeated as further details arrive), and every client writes a **package** from which `CnCNew.NetTool desync-diff` names the tick, the objects and the fields. The parts
are not in every hash report - that would have made every report several times larger for the one tick in a million that needs them; a client keeps the parts of its
last 1,024 ticks instead and is asked when it matters. The parts, the package, the tool and the procedure are `docs/NETWORK-DIAGNOSTICS.md`.

### 18.8 Checks

`tests/CnCNew.Net.Tests/Phase27Tests.cs` (five tests, on a clock set by hand; at 30, 60 and 120 Hz): a lost client takes its held seat again over the loopback and over
the UDP transport in memory with loss, and plays on in step; with a hash every fourth tick; too late, the seat is given up; a damaged state is turned away by the one
who loads it and by the relay; a state with another hash than the agreed one goes nowhere; a made-up token opens no seat; a token whose connection still stands takes
the seat over; watchers on a delay and late ones play the same states, their orders are discarded, one that diverged is found; a desync is named by the relay down to
the part and by the packages down to object and field; the recording as a file replays into the clients' hash; the rules of the pace; the recovery time of the lag
rule; a close after the end.

With real processes on real sockets (`docs/TESTING.md`): `scripts/check-net-robustness.ps1` (step `net-robust`: a rejoin, a late watcher and the server's replay played
again; a watcher on a delay; a desync and `desync-diff`), `scripts/check-net-join.ps1` (step `net-join`: the window client against a client without a window on a
dedicated server), `scripts/check-net-cross-os.ps1` (step `net-cross-os`: server and two clients under Linux in WSL, two under Windows; a rejoin across the boundary).

## 19. Measured in Phase 27 (12 October 2026, 60 Hz unless stated, build `8f6f94f`, Windows, Release)

One machine (AMD Ryzen 3 PRO 4350GE, four cores), every process its own: the dedicated server, the UDP proxy, one `NetTool client` per member, on the loopback
interface; quiet machine, warmed-up processes. As in §16, nothing here crossed a cable.

### 19.1 A rejoin on five lines (`net-match --disturb 1:rejoin:18000:3000`)

Three players and five computer players on Twilight Flame. In minute five (tick 18,000) the connection of player 2 dies without a word; three seconds later the same
process is back with its token, before the server's timeout noticed the dead connection. The state it is handed is 3.4 MB of JSON, packed 84 to 97 KB; the giver
needs 36 to 47 ms to write and pack it. Times are from the moment the server has the token.

| Line (delay / jitter / loss, rate) | State passed on by the server after | Joiner stands at the state after | then behind by | Joiner plays in time again after | All clients in the same state at the end |
|---|---|---|---|---|---|
| 40 ms / 10 ms / 1 % | 0.15 s | 0.36 s | 24 ticks | 0.36 s | yes |
| 150 ms / 30 ms / 3 % | 0.80 s | 1.67 s | 107 ticks | 2.07 s | yes |
| 80 ms / 30 ms / 10 % | 0.48 s | 1.40 s | 89 ticks | 1.69 s | yes |
| 40 ms / 10 ms / 1 %, 1 Mbit/s | 1.69 s | 4.37 s | 265 ticks | 4.76 s | yes |
| 40 ms / 10 ms / 1 %, 256 kbit/s | 6.04 s | 15.06 s | 844 ticks | 21.36 s | yes |

- **On a line of a megabit or more a player is back in the match two to five seconds after its client connects again**, on a good line in under half a second. The
  time is the state travelling twice (member to server, server to joiner) over the reliable channel, which has no congestion control: on a narrow line the state
  fills the queue of the line and is partly sent again.
- **At 256 kbit/s it takes 15 s to load and 21 s to be in time**, and for that time the line is busy with the state: the joiner's orders are executed 2 to 3 s late
  until it has caught up, and the member that gives the state has its own orders delayed while it sends (95 % of its orders within 0.83 s instead of 0.2 s). It
  works, and nobody else is slowed for it. That is why the wait for a member's state is 30 s (it was 10 s before this measurement). A state of a late match is 230 KB
  packed (`docs/NETWORK-DIAGNOSTICS.md` §7) - two and a half times this one.
- The first measurement of this table found the joiner playing a second behind for half a minute on the lossy lines (in time only after 12.7 s and 7.7 s): its
  playback counted the backlog as late arrivals. With the backlog left out (§18.2) it is 2.1 s and 1.7 s.
- While the dead connection still stood, the lag rule slowed the others for 1.2 to 3.3 s (§14.1) - the price of a client that vanishes, not of the rejoin: the
  slowing ends the moment the token arrives.

### 19.2 The matrix again (`NetTool net-matrix`, as §16.1: eight client processes, 100 s per line)

The protocol, the relay's sending (a position per member) and the playback changed in this phase; the matrix is the comparison with Phase 26.

| Delay | Jitter | Loss | Hashes identical | Order to execution: median | 95 % | 99 % | Phase 26: median / 99 % | Playback delay | Waits (of 48,000 ticks) | Up B/s | Down B/s | Phase 26: up / down |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 10 ms | 0 ms | 0 % | yes (8 of 8) | 33 ms | 39 | 60 | 33 / 116 | 15 ms | 712 | 6,051 | 6,218 | 6,031 / 6,003 |
| 10 ms | 0 ms | 1 % | yes (8 of 8) | 44 ms | 51 | 70 | 43 / 65 | 24 ms | 442 | 6,271 | 6,344 | 6,254 / 6,312 |
| 10 ms | 0 ms | 3 % | yes (8 of 8) | 50 ms | 53 | 69 | 50 / 86 | 30 ms | 157 | 6,261 | 6,145 | 6,241 / 6,050 |
| 10 ms | 10 ms | 0 % | yes (8 of 8) | 49 ms | 53 | 60 | 49 / 58 | 24 ms | 158 | 7,685 | 7,712 | 7,680 / 7,573 |
| 10 ms | 10 ms | 1 % | yes (8 of 8) | 50 ms | 66 | 83 | 50 / 70 | 30 ms | 399 | 7,554 | 7,658 | 7,543 / 7,608 |
| 10 ms | 10 ms | 3 % | yes (8 of 8) | 64 ms | 70 | 102 | 63 / 87 | 37 ms | 304 | 7,507 | 7,483 | 7,464 / 7,399 |
| 10 ms | 30 ms | 0 % | yes (8 of 8) | 79 ms | 92 | 103 | 77 / 101 | 44 ms | 113 | 9,248 | 9,380 | 9,187 / 9,011 |
| 10 ms | 30 ms | 1 % | yes (8 of 8) | 80 ms | 90 | 101 | 81 / 108 | 46 ms | 254 | 9,189 | 8,959 | 9,236 / 9,349 |
| 10 ms | 30 ms | 3 % | yes (8 of 8) | 83 ms | 100 | 103 | 82 / 113 | 50 ms | 378 | 9,180 | 9,074 | 9,175 / 9,052 |
| 40 ms | 0 ms | 0 % | yes (8 of 8) | 99 ms | 102 | 108 | 99 / 129 | 45 ms | 288 | 9,880 | 9,783 | 9,911 / 10,046 |
| 40 ms | 0 ms | 1 % | yes (8 of 8) | 100 ms | 116 | 146 | 100 / 134 | 54 ms | 434 | 9,859 | 9,532 | 9,845 / 9,427 |
| 40 ms | 0 ms | 3 % | yes (8 of 8) | 106 ms | 117 | 135 | 107 / 168 | 60 ms | 143 | 9,857 | 9,318 | 9,879 / 9,496 |
| 40 ms | 10 ms | 0 % | yes (8 of 8) | 107 ms | 118 | 122 | 107 / 119 | 54 ms | 59 | 9,882 | 9,827 | 9,916 / 10,103 |
| 40 ms | 10 ms | 1 % | yes (8 of 8) | 115 ms | 119 | 125 | 114 / 134 | 59 ms | 398 | 9,879 | 9,688 | 9,890 / 9,800 |
| 40 ms | 10 ms | 3 % | yes (8 of 8) | 117 ms | 131 | 135 | 116 / 137 | 67 ms | 281 | 9,863 | 9,378 | 9,879 / 9,507 |
| 40 ms | 30 ms | 0 % | yes (8 of 8) | 139 ms | 151 | 162 | 135 / 154 | 74 ms | 110 | 9,891 | 9,907 | 9,877 / 9,773 |
| 40 ms | 30 ms | 1 % | yes (8 of 8) | 141 ms | 158 | 186 | 134 / 164 | 76 ms | 233 | 9,870 | 9,631 | 9,872 / 9,665 |
| 40 ms | 30 ms | 3 % | yes (8 of 8) | 145 ms | 158 | 170 | 145 / 182 | 79 ms | 391 | 9,882 | 9,519 | 9,912 / 9,789 |
| 80 ms | 0 ms | 0 % | yes (8 of 8) | 167 ms | 182 | 189 | 167 / 187 | 86 ms | 239 | 9,898 | 9,891 | 9,910 / 10,022 |
| 80 ms | 0 ms | 1 % | yes (8 of 8) | 181 ms | 187 | 204 | 182 / 250 | 95 ms | 407 | 9,898 | 9,794 | 9,853 / 9,491 |
| 80 ms | 0 ms | 3 % | yes (8 of 8) | 183 ms | 198 | 206 | 183 / 213 | 100 ms | 164 | 9,911 | 9,709 | 9,841 / 9,189 |
| 80 ms | 10 ms | 0 % | yes (8 of 8) | 183 ms | 199 | 202 | 184 / 201 | 94 ms | 65 | 9,901 | 9,891 | 9,918 / 10,091 |
| 80 ms | 10 ms | 1 % | yes (8 of 8) | 192 ms | 201 | 204 | 194 / 219 | 99 ms | 382 | 9,861 | 9,537 | 9,911 / 9,914 |
| 80 ms | 10 ms | 3 % | yes (8 of 8) | 200 ms | 211 | 227 | 200 / 222 | 107 ms | 288 | 9,885 | 9,547 | 9,882 / 9,458 |
| 80 ms | 30 ms | 0 % | yes (8 of 8) | 216 ms | 232 | 242 | 215 / 237 | 115 ms | 95 | 9,908 | 10,032 | 9,907 / 10,021 |
| 80 ms | 30 ms | 1 % | yes (8 of 8) | 215 ms | 233 | 246 | 217 / 247 | 115 ms | 252 | 9,871 | 9,605 | 9,908 / 9,934 |
| 80 ms | 30 ms | 3 % | yes (8 of 8) | 222 ms | 237 | 250 | 224 / 256 | 119 ms | 388 | 9,897 | 9,607 | 9,857 / 9,297 |
| 150 ms | 0 ms | 0 % | yes (8 of 8) | 316 ms | 320 | 331 | 316 / 331 | 155 ms | 603 | 9,921 | 10,033 | 9,891 / 9,794 |
| 150 ms | 0 ms | 1 % | yes (8 of 8) | 326 ms | 335 | 352 | 325 / 340 | 164 ms | 422 | 9,868 | 9,515 | 9,849 / 9,439 |
| 150 ms | 0 ms | 3 % | yes (8 of 8) | 333 ms | 336 | 369 | 333 / 382 | 170 ms | 155 | 9,891 | 9,551 | 9,878 / 9,456 |
| 150 ms | 10 ms | 0 % | yes (8 of 8) | 332 ms | 337 | 339 | 331 / 341 | 164 ms | 175 | 9,845 | 9,469 | 9,877 / 9,714 |
| 150 ms | 10 ms | 1 % | yes (8 of 8) | 332 ms | 343 | 352 | 333 / 578 | 170 ms | 382 | 9,924 | 10,032 | 9,868 / 9,528 |
| 150 ms | 10 ms | 3 % | yes (8 of 8) | 336 ms | 350 | 354 | 335 / 364 | 177 ms | 265 | 9,921 | 9,836 | 9,891 / 9,537 |
| 150 ms | 30 ms | 0 % | yes (8 of 8) | 355 ms | 368 | 380 | 353 / 372 | 184 ms | 73 | 9,883 | 9,796 | 9,911 / 10,049 |
| 150 ms | 30 ms | 1 % | yes (8 of 8) | 356 ms | 369 | 381 | 358 / 382 | 186 ms | 269 | 9,886 | 9,701 | 9,892 / 9,787 |
| 150 ms | 30 ms | 3 % | yes (8 of 8) | 364 ms | 380 | 416 | 363 / 400 | 190 ms | 378 | 9,893 | 9,600 | 9,871 / 9,422 |

**36 of 36 lines hash-identical on all clients**, the server agreed on every tick; 16,195 orders, no resync, never slowed, no gen-2 collection. Against Phase 26,
over the 36 lines: the median of order to execution +0.3 ms, the 99 % value -10 ms, upstream +5 B/s, downstream +17 B/s per client - the same within what two runs
of one line differ by.

**Other rates and another map** (four lines each, the same eight processes):

| Rate, map | Line | Hashes identical | Order to execution: median / 95 % / 99 % | Playback delay | Waits | Up / down B/s |
|---|---|---|---|---|---|---|
| 30 Hz, Twilight Flame | 10 / 0 / 0 | yes (8 of 8) | 33 / 35 / 67 ms | 16 ms | 100 | 2,473 / 2,664 |
| | 40 / 10 / 1 | yes | 132 / 150 / 168 ms | 68 ms | 248 | 5,519 / 6,206 |
| | 80 / 30 / 1 | yes | 232 / 246 / 270 ms | 120 ms | 197 | 5,733 / 6,147 |
| | 150 / 30 / 3 | yes | 394 / 408 / 434 ms | 203 ms | 224 | 5,753 / 6,302 |
| 120 Hz, Twilight Flame | 10 / 0 / 0 | yes (8 of 8) | 30 / 50 / 124 ms | 16 ms | 457 | 14,144 / 14,777 |
| | 40 / 10 / 1 | yes | 105 / 111 / 117 ms | 55 ms | 523 | 18,830 / 17,156 |
| | 80 / 30 / 1 | yes | 210 / 223 / 227 ms | 113 ms | 279 | 18,817 / 17,036 |
| | 150 / 30 / 3 | yes | 353 / 368 / 374 ms | 192 ms | 372 | 18,870 / 17,162 |
| 60 Hz, Fortress Avalanche | 10 / 0 / 0 | yes (8 of 8) | 33 / 36 / 56 ms | 15 ms | 193 | 6,087 / 6,318 |
| | 40 / 10 / 1 | yes | 118 / 122 / 136 ms | 60 ms | 373 | 9,957 / 10,276 |
| | 80 / 30 / 1 | yes | 222 / 279 / 453 ms | 118 ms | 253 | 9,940 / 10,070 |
| | 150 / 30 / 3 | yes | 362 / 379 / 386 ms | 190 ms | 366 | 9,918 / 9,781 |

All twelve lines are hash-identical. An order waits for a tick to end: at 30 Hz it is executed 15 to 30 ms later than at 60 Hz on the same line, at 120 Hz about
10 ms earlier; the bandwidth follows the number of bundles (about half at 30 Hz, about twice at 120 Hz). The second map behaves like the first.

### 19.3 A whole standard match with three rejoins (`NetTool net-match --minutes 60`, 40 ms / 10 ms / 1 %)

As §16.4 - six players, two watchers, two computer players, 216,000 ticks - and: the connections of players 2, 3 and 4 die at minute 10, 30 and 55 and come back
five seconds later with their tokens; a third watcher joins the running match at minute 20; the server holds seats for five minutes and writes its replay.

| | Minute 10 | Minute 30 | Minute 55 |
|---|---|---|---|
| State on the wire (packed) | 198 KB | 469 KB | 775 KB (7.9 MB of JSON) |
| State passed on by the server after | 0.31 s | 0.47 s | 0.59 s |
| Joiner stands at the state after | 0.59 s | 1.07 s | 1.42 s |
| Joiner plays in time again after | 0.59 s | 1.20 s | 1.58 s |

**Nine of nine clients end in the same state** (the six that played from the start with one hash chain over all 216,000 ticks, the three that rejoined and the late
watcher with the same final session hash), the server agreed on every tick, 63,282 orders, order to execution median 116 / 95 % 120 / 99 % 187 ms, no resync, no
desync. The others were slowed for 9.6 s in total - three times about three seconds while a dead connection still stood (§19.1). Per client 10.4 KB/s up and
14.2 KB/s down (the three states and the backlog of four joiners included). **The server's replay file** is 3.3 MB for the hour; `NetTool replay-check` plays it in
250 s and arrives at the hash the clients agreed on for the last tick. The server's managed memory was 23 MB at the end.

### 19.4 Two operating systems in one match

`scripts/check-net-cross-os.ps1` (`docs/DETERMINISM.md` §2d): the server, its proxy and two clients as Linux processes under WSL, two clients as Windows processes,
through the proxy with 40 ms / 10 ms / 1 % - one hash chain on all four over 2,700 ticks; and with a rejoin, where the Linux client loads the state a Windows client
wrote (13.6 KB packed, loaded after 0.41 s). Order to execution median 115 ms, as between Windows processes. The Linux reference manifest: 50 of 50 runs identical.

### 19.5 What these measurements do not show

Everything of §16.5 still holds: one machine, the loopback interface, a proxy instead of a network - and under WSL a virtual switch instead of a cable. In addition:
a rejoin was measured with a client that comes back after three or five seconds, not after minutes (the catch-up from the start of a long match is covered by a
test on the synthetic fixture only); the narrow line is a model (one queue, tail drop), not a measured DSL or mobile line; a watcher on a delay of 120 s was not
run with real processes (5 s was); no human took part, and the window client's joined match was played in a smoke test (§18.6).

### 19.6 Closing measurements of Phase 27 (state `8f6f94f`)

**Tier 4 with 44 of 44 steps ok** at `8f6f94f` (5,194 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite; the long run; the client checks; `check-match`; `session-check`; `net-probe`; `net-match`; the new `net-robust`, `net-join` and `net-cross-os`; the staged scenes). `NetTool probe-suite` 48 of 48. Campaign at 60 Hz (missions stay local): reachability clean 24/24 and 24/24, audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and 15/24, objective driver victory 24/24 and defeat 24/24 - as before the phase. Performance of the simulation, the whole `scripts/perf-profile.ps1` before (`f25fa7a`) and after: the same final hashes, means within -2.4 % and +2.2 % (8 players: 832 against 834 µs per tick, maximum 139 against 146 ms). The reference manifest is untouched: Phase 27 has no manifest commit.

## 20. An open port (Phase 28)

A server that anybody can reach is sent everything: datagrams that are not ours, floods, old datagrams sent again, messages a client would never send, orders no
player can give - and it is watched by whoever sits on the way. This section is what changed on the wire and in the room for that. The threat model, line by line with
measure, test and residual risk, is `docs/DEDICATED-SERVER.md` §9; the server's configuration and operation are §10 to §13 there. Everything here is in the layers below
and beside the simulation; of the simulation's formats only the command check changed (§20.6, `docs/SIMULATION-CORE.md` §49).

### 20.1 What is new on the wire

**Transport version 2** (it replaces §12.2 to §12.4 where they differ; a version 1 datagram is dropped and counted like any other datagram that is not ours):

| Kind | Direction | Body after the 4-byte header (§12.2, version **2**) | Length |
|---|---|---|---|
| request | client → server | `u64` client nonce, 20 zero bytes | 32 |
| challenge | server → client | `u64` client nonce, `u32` time slot, 16 bytes cookie | 32 |
| connect | client → server | `u64` client nonce, `u32` time slot, 16 bytes cookie, 64 bytes exchange key of the client | 96 |
| accept | server → client | `u64` client nonce, `u64` connection token, 64 bytes exchange key of the server, 64 bytes identity key of the server, 64 bytes signature | 212 |
| refuse | server → client | `u64` client nonce, `u8` reason (1: the server is full, 2: this address holds as many connections as it may) | 13 |
| data, close | both | `u64` connection token, `u64` datagram number, the sealed body, 16 bytes tag | 28 + body |

**Protocol version 4** (a version 3 client is rejected with the reason "protocol version" and a text that names both versions):

| | |
|---|---|
| `Hello` | two more fields at its end: the room verifier, 64 hexadecimal digits or empty, and the player key, 128 hexadecimal digits or empty (§20.5) |
| 35 `PasswordChallenge` | server → client: a nonce of 32 hexadecimal digits |
| 36 `PasswordResponse` | client → server: the answer of the password, 64 hexadecimal digits, and the signature of the player key, 128 hexadecimal digits or empty |
| 37 `Kick` | host → server: the connection id of the member to remove |
| reject reasons | 14 `BadPassword`, 15 `Kicked`, 16 `BadIdentity`, 17 `Banned` |
| drop reasons | 5 `Kicked`, 6 `Flood` |
| end reason | 3 `ServerLimit` - the server ended the match because it reached a limit of the room (§20.4) |

No format of the simulation changed: command table 17, replay format 15, world snapshot 10, match description encoding 2, session state 2, desync package 1.

### 20.2 The key exchange, and who the server is

- With its connect the client sends the public half of a key pair it made for this connection (ECDH on the curve P-256, X and Y as 32 bytes each). The server
  makes one of its own - only now, for a sender that returned its cookie (§12.3) - and answers with it, with the public half of its **identity key** and with a
  signature of the identity key (ECDSA P-256 with SHA-256, 64 bytes) over the **transcript**: a label, the client nonce, the connection token, both exchange keys and
  the identity key. The client checks the signature before anything else; an accept whose signature does not verify is counted (`Malformed`) and ignored, and the client
  goes on waiting for the real one.
- Both ends compute the shared secret and derive from it, with HKDF-SHA-256 salted with the hash of the transcript, **two keys of 256 bits: one per direction**. The
  two ends therefore never seal under the same key and number. The exchange keys are thrown away; a recording of the traffic cannot be opened later with the server's
  identity key (forward secrecy).
- **The identity key is the server.** `CnCNew.DedicatedServer` keeps it in a file (`keyFile`, PEM, made on the first start; without one the key lasts as long as the
  process, with a warning) and prints its **fingerprint** - SHA-256 of the 64 public bytes, in hexadecimal - on its first line. An operator publishes the fingerprint
  with the address.
- **Whom a client trusts** is the decision of the client's host, which the transport asks with the identity key before it uses the connection:
  a **pinned** fingerprint (`--server-key`; the harnesses pin the key their server printed) admits that key and no other; without one the client **trusts on first
  use** - `CnCNew.Game.Lockstep.KnownServers`, a file of address and fingerprint (default `%LOCALAPPDATA%\CnCNew\known-servers.json`), remembers the key of the first
  meeting and refuses the server when the key is another one later, with a message that names both fingerprints and says what to do. A refused server is told
  nothing: the client closes with `UdpCloseReason.Untrusted` before it sends its hello.
- What this is not: there is no certificate and nobody who vouches for a key. The first meeting of a client without a pinned fingerprint can be met by somebody
  else (§20.7).

### 20.3 A sealed datagram

- Everything after the handshake - data and close - is sealed with **AES-256-GCM**: the 12 clear bytes in front (magic, version, kind, connection token) are
  authenticated, everything behind the datagram number is encrypted and authenticated, the tag is 16 bytes. A datagram is **24 bytes longer** than in version 1; the
  sizes follow (`MaxPlainBytes` 1,176; a reliable segment carries up to 1,135 bytes, an urgent fragment up to 1,131).
- The **datagram number** counts from 1 per direction and is the nonce of the cipher (never twice under one key: a connection ends long before 2^64 datagrams). The
  receiver keeps a **window of 1,024 numbers** behind the highest it has seen: a number it has seen, or one older than the window, is a repetition - counted
  (`Replayed`) and dropped before anything of it is used. A datagram whose tag does not verify is counted (`Forged`) and dropped; **it does not even keep the
  connection alive**, and it cannot close it: a close is sealed like data, so only the two ends of a connection can end it before its timeout.
- The acknowledgement, the urgent messages and the segments of §12.4 are inside the sealed body, unchanged.
- **What travels sealed** is therefore everything of the protocol of §3: the hello with the rejoin token and the room verifier, the welcome with the token, chat,
  orders, hashes, states. **What somebody on the way still sees:** that the two addresses talk, when, and how many bytes; the 12 clear bytes; the handshake with the
  server's public identity key.
- All of it is the base class library (`ECDiffieHellman`, `ECDsa`, `AesGcm`, `HKDF`, `HMACSHA256`); the project wrote no cipher and added no package.

### 20.4 What a stranger and a member may cost

Every bound is an option with the default named here (transport: `UdpOptions`; room: `RelayOptions`; the server's configuration keys are in
`docs/DEDICATED-SERVER.md` §9, with the reaction and the counter of each). The transport's:

- **Handshake datagrams** are answered at most 10 a second per address (20 at once) and 2,000 a second for all senders together - the bound for senders that forge
  their address; what is beyond is dropped unanswered and counted (`RateLimited`). The table of addresses holds 4,096 and starts anew when it is full.
- **Key exchanges** - the one answer that costs the server milliseconds of a core (§21.2) - are done at most 100 a second for all senders together (400 at once).
  A sender that is put off sends its connect again, as after a lost datagram.
- **Connections per address:** 8 (people behind one router share an address); one more is refused with reason 2. **Connections in all:** 256.
- **Bytes a connection may send the server:** 256 KiB a second in the long run and 8 MiB at once (a session state is sent as fast as the line carries it, §21.3). A
  datagram beyond is dropped as the network would drop it; a peer that keeps that up for 10 s is closed (`UdpCloseReason.Flood`).
- **What waits for a peer that does not acknowledge** is bounded (twice the largest message); beyond it the connection is closed instead of growing.
- A **banned** address (the server's ban list) gets no answer at all, and its connections are closed when the ban is read.

The room's:

- **Messages per member:** 500 a second in the long run, 4,000 at once (a client at 120 Hz sends about 250 a second); a member beyond is dropped with `Flood`. The
  one client of a local, client-paced match is exempt - there is nobody to protect from it.
- **Time limits:** 10 s for the hello and for the answer to a password challenge; 180 s for loading (a member that does not finish is dropped with `TooSlow`, the
  others start); 20 s of game time without the hash of a tick from a member that plays along (dropped with `ProtocolViolation` - the others' reports must not wait for
  it for ever); 60 min for a lobby that never starts and for a room whose match is over.
- **Memory of a room:** a session state is at most 4 MiB (measured: at most 775 KB, §19; the wire format still allows 64 MiB, the room does not); a match is at most
  2 h of game time (a host that sets more is refused); the recording is at most 64 MiB of commands - a match that reaches it is ended with `ServerLimit` (a standard
  match of an hour with six busy players is 3.3 MB); the agreed hashes are kept for the window of §18 only; at most 64 intents per player and tick (§5); states are
  asked of members at most 6 times a minute (a join beyond is refused with `CannotCatchUp` and may try again).
- Everything the embedded relay of a local game (`LocalRelay`) does is exempt: its limits are 0.

### 20.5 A room with a password, a player with a key, and removing a member

- The **password never travels**, not even inside the sealed connection, and the server never has it. The client derives a **verifier**: HMAC-SHA-256 with the
  password as the key over a label and the room's name. The member that opens a room states the verifier in its hello, and the room keeps it.
- Whoever enters later is sent a `PasswordChallenge` with a nonce the room never used before, and answers with HMAC-SHA-256 with the verifier as the key over the
  nonce. The room compares in constant time. A wrong answer is rejected with `BadPassword`, counted, and the connection closed; an answer cannot be used again
  because the nonce is new every time.
- A client with a known **rejoin token** is not asked: the token was handed out inside the sealed connection to a member that had passed.
- What it is and is not: a way to keep a room among people who know a word. The server sees the verifier and could enter the room itself; a verifier is as strong
  as the password it was made from (somebody who has the verifier can try passwords against it without the server).
- **A player key** is a permanent key pair a client may have (ECDSA P-256; the window client keeps it in `player-key.pem` beside its settings and makes it on the
  first join, `--no-player-key` states none). It is what lets a server know "the same player as before" without an account - and keep that player out. The hello
  states the public half. The server then sends the same challenge as for a password, and the answer carries a **signature over a label, the nonce and the room's
  name**: a stated key is a proved one, and an answer somebody recorded fits no other challenge. A wrong or missing signature is rejected with `BadIdentity` and
  counted. The server knows the member by the key's **fingerprint** (SHA-256 of the public half), logs it with the join, refuses a fingerprint on its ban list with
  `Banned` - at the hello, and for members already in a room when the list changes - and, with `requirePlayerKey`, admits nobody who states none.
- What a player key is not: an account. A new key costs nothing, so a ban by key keeps out only who does not make one - on a server that requires keys a banned
  player is a stranger again with a new one, as with a new address. And it is a **permanent pseudonym**: every server a player joins sees the same fingerprint
  (personal data in the server's log, `docs/DEDICATED-SERVER.md` §10.4); whoever deletes the file is somebody new everywhere.
- **`Kick`:** the host names a connection; the member is told (`Reject` with `Kicked`), its connection is closed, the others see it dropped with `Kicked`, and in a
  running match its player leaves as after a lost connection (§14.2) - its seat is not held. The **origin** of the connection (its address without the port) and the
  member's **player key**, if it proved one, may not enter the room again as long as the room exists. Somebody else's kick request is refused with `NotHost`.

### 20.6 Orders nobody can give, and who decides

- **A command on the wire is an order of a player or it is nothing.** `CommandTypes.IsPlayerOrder` names the 24 of 34 command types a player gives; the other ten
  (spawn, destroy, damage, kill, the timers, give money, give science points, set relationship, the leave) are the system's. `CommandWire.IsPlausible` - asked by the
  relay for every intent before it is put into a bundle - refuses them like a command that is too long or has no header; the relay still decodes nothing.
- **A command that does not decode is left out by every client alike.** The relay passes the bytes on unread, so an order with a plausible header and garbage behind
  it reaches every client; each one fails to decode it in the same way, counts it (`LockstepDriver.UndecodableCommands`) and plays the tick without it. The server's
  replay file leaves it out the same way, so it still plays to the agreed hash.
- **A command with coordinates no map has is rejected by every simulation alike** (`docs/SIMULATION-CORE.md` §49): a point beyond 131,072 units or an angle beyond
  1,024 radians in an order of a player is `InvalidArguments` at submission - before it could make the movement overflow (decision D-5).
- **Somebody who watches decides nothing.** The hash of a tick is agreed, and the end of a match is declared, by the players that play along in time. A watcher's
  report is compared with what they agreed on; a watcher whose hash differs is told (`Desync`, so that it writes its package), counted (`WatcherDesyncs`), logged
  (`watcher-desync`) and dropped with `Desync`, and the match of the players goes on. A watcher that reports nothing, or never says the match is over, is waited for
  by nobody. A match that only computer players play is decided by those who watch it.
- **A player that lies about its hash still ends the match** - for everybody, as a desync: with two players nobody can say which one lies, and the relay cannot
  check. This is the residual risk of a relay that does not simulate (§20.7; a proposal is decision D-24).

### 20.7 What this does not protect

- **The first meeting.** A client without a pinned fingerprint that meets a server for the first time believes whoever answers. From then on that key is the server.
- **A server that is not honest.** The relay sees every order and every state in clear - it is one end of every sealed connection. It could withhold, delay or
  reorder the orders of a player (it cannot forge the result: every client simulates, and a server-made command of a player's type is still checked by every
  simulation), and it could hand a joiner a state of its own making only if the members' reported hashes matched it - they are the members' word, and the relay
  compares them.
- **A player that lies** (§20.6), and **cheats that need no lie**: every client holds the whole state of the match, so a changed client can show its user what the
  fog hides. Lockstep does not prevent that, and this project has no anti-cheat (out of scope, `CLAUDE.md`).
- **Volume.** The limits bound what one address and all senders together may make the server compute and remember; they do not make a line wider. A flood that
  fills the server's line is the network's to stop.
- **Traffic analysis**, and the handshake: somebody on the way sees that a client talks to this server, and for how long.

### 20.8 Checks

| Check | What it proves |
|---|---|
| `Net.Tests` "Sealed transport" | two ends derive the same keys and others do not; a tampered, truncated or replayed datagram is counted and changes nothing; a forged close does not close; a forged accept is ignored; a server with another key is refused before the hello; **somebody who records every datagram of a member can neither take its held seat nor displace its standing connection** |
| `Net.Tests` "Secrets stay off the wire" | no datagram of a whole session - handshake, hello, welcome, rejoin - contains the rejoin token, the room verifier, the password answer, the chat text or an order in clear |
| `Net.Tests` "Transport limits" | handshakes per address and in all, the ration of key exchanges, connections per address, a ban, bytes per connection, the backlog of a peer that does not acknowledge |
| `Net.Tests` "Relay limits" | a flood of messages, a silent connection, a member that never loads or never reports a hash, the match length, the state size, joins per minute, the recording, an idle lobby - each with its counter and its reaction |
| `Net.Tests` "Room password and kick" | the challenge and its answer, a wrong and a replayed answer, the rejoin without the question, the kick and the origin that may not come back |
| `Net.Tests` "Player keys" | a stated key is proved by its signature; a signature of another key, one for another nonce and none are refused; a banned key leaves the room and does not come back from another address; a server that requires a key; a kicked key from another address |
| `Net.Tests` "A member that stays just below every limit" | 2.9 s behind for a minute: nobody is slowed; hashes 19 s late for a minute: agreed 19 s late and no later; across either limit the member is dropped and the others run on |
| `Net.Tests` "Hostile members" | whole matches (the multi-client probe, UDP in memory) in which a member sends garbage orders and system commands, floods, withholds its hashes or is kicked: the others end in the same state and the replay plays to it |
| `Net.Tests` suite "Fuzz" | `docs/DEDICATED-SERVER.md` §9.4 |
| `scripts/check-net-join.ps1` | real processes: a client told another key refuses the server and says nothing to it; the window client remembers the key of its first meeting |
| `scripts/check-net-match.ps1`, `check-net-robustness.ps1`, `check-net-cross-os.ps1` | the checks of Phases 26 and 27, unchanged, now over the sealed transport with the server's key pinned |
| `scripts/check-net-load.ps1` | eight rooms at once with members that vanish, leave and come back; the server ends empty (`docs/DEDICATED-SERVER.md` §11) |

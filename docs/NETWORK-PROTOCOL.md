# Network protocol (version 1)

Phase 25 (11 October 2026). Frame: `docs/MULTIPLAYER-PLAN.md` §2 (lockstep through a relay), `docs/SIMULATION-CORE.md` §45 (session core), `docs/PRESENTATION-INPUT.md` §13
(command path, tick source). Code: `src/CnCNew.Net` (messages, transport interface, loopback, client), `src/CnCNew.Server` (room and relay), `src/CnCNew.Game/Lockstep`
(lockstep driver, embedded server). **There is no socket in this phase**: the only transport is the loopback inside one process; UDP, clock synchronisation and the standalone
server are Phase 26.

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
| Protocol version | `Protocol.Version` | 1 |
| Command table | `CommandTypes.Version` | 16 |
| Replay format (the command codec) | `ReplayFormat.CurrentVersion` | 14 |
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
  a public server does not unless a developer switches it on. In this phase the only server is the embedded one.
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
| 1 | `Hello` | client → server | protocol version, simulation identity, command table, replay format, world state, description encoding, definition fingerprint, name |
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
| 22 | `PlayerDropped` | server → all | player, connection, reason |
| 23 | `Desync` | server → all | tick, the hash of every connection that reported |
| 24 / 25 | `EndReport` / `MatchEnd` | client → server / server → all | tick, winner / tick, winner, reason |

**A command on the wire** is the simulation's own command codec (`ReplayFormat.EncodeCommand`: `i64 tick, u16 type, i32 player, u64 sequence`, then the fields of the type) -
the protocol invents no second encoding. A client sends an intent with tick 0 and sequence 0. The server checks the header (length, a known command type), writes the
**player of the connection** and the tick of the bundle into it (`CommandWire.Stamp`) and passes the rest on untouched: nobody can give a command for somebody else. The
sequence number stays 0 on the wire. The server counts an **order number** per player; the simulation's sequence number is assigned by every client's session when the
command is submitted, in the order of the bundle (`docs/SIMULATION-CORE.md` §45.5) - which is the same on every client because the bundle is.

## 4. The room (`RelayServer`)

State machine **room → loading → match → ended**; the server is authoritative, a client shows the `RoomState` it was sent, never what it asked for.

- **Host** is the first member (the next one when it leaves). Only the host sets the room and starts the match.
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
(`PlayerDropped`); when nobody is left the match is abandoned. What becomes of a dropped player's units is Phase 27.

**The recording** (`RelayServer.Recording`): the description and every bundle. It is enough to play the match again without any client: the commands of the humans come
from the bundles, the computer players decide again (`SkirmishReplay.Play`, §8).

## 6. The client (`LockstepClient`, `LockstepDriver`)

`LockstepClient` (in `CnCNew.Net`) is the protocol's client end: handshake, room, and in a match the bundles in order. It owns no simulation and no clock.

`LockstepDriver` (in `CnCNew.Game.Lockstep`) is the game's **tick source and command scheduler in one** - `ITickDriver` and `ICommandScheduler` of
`docs/PRESENTATION-INPUT.md` §13 -, because here they are one thing: a tick may run when its bundle has arrived, and the bundle is what the scheduler hands out.

- An order (`CommandPath.Issue`) is pre-checked locally as before and sent as an intent under its receipt number. It comes back in a bundle with its origin and receipt, is
  submitted from there and answered by receipt number (`CommandResolution`). A command of another machine has no receipt here and no pre-check to deviate from.
- Client-paced: the accumulator is the driver's own; when a tick is due it sends the held orders, asks for the tick and steps as soon as the bundle is there. Pause and
  speed are granted at once.
- Server-paced: a tick is released when its bundle has arrived and the newest bundle is at least `PlaybackBufferTicks` ahead - a **fixed** buffer against jitter in this
  phase (the adaptive one is Phase 26). Pause and speed are requests to the server; any player may ask, and the answer goes to everybody.
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

No socket, no encryption, no authentication, no flood protection beyond the per-tick limit (Phase 26 and 28); no clock synchronisation and no adaptive buffer (26); no
rejoin, no spectator joining a running match, no desync diagnosis below "this tick" (27); a dropped player's units stay as they are (27); pause and speed in a server-paced
room may be asked by any player without a rule (29); the setup screen is not yet a room others can join (29).

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

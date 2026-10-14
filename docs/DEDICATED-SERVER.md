# The dedicated server

Phase 26 (11 October 2026), extended by Phase 27 (12 October 2026: held seats, watchers on a delay and late ones, the replay file, the rules of the pace - §2, §3, §6,
§8) and Phase 28 (12 October 2026: the open port - threat model, limits and fuzzing §9; the server's key, bans, logs and the stop §10; capacity and endurance §11;
versions in operation §12; the self-contained package §13). `tools/CnCNew.DedicatedServer`: the relay of `docs/NETWORK-PROTOCOL.md` as a program of its own - **one process, one UDP port, several rooms**.
It relays and nothing else: it loads no game data, simulates nothing and does not decode a command. It references `CnCNew.Net` and `CnCNew.Server` only (through them the
simulation's command codec and match description); a test of `Net.Tests` enforces that it reaches neither `CnCNew.Content` nor `CnCNew.Game`, `CnCNew.Presentation` or a
renderer, and that its source names none of them.

**Since Phase 28** the connection is authenticated and encrypted, the server has a permanent key, and what a stranger or a member may cost is bounded and counted
(§9, `docs/NETWORK-PROTOCOL.md` §20). **Not yet:** a listing of rooms and a lobby in the client (Phase 29), accounts, votes (D-23), a majority rule against a player
that lies about its hash (D-24). **What was never run:** the server on a second computer, on a real network, or under a hostile load from outside - every
measurement and every check of this document ran on one machine (§11.4). **Linux as a host:** the self-contained `linux-x64` package was built under Windows and
its server played a match in a WSL distribution without a .NET runtime in its environment (§13); whether it is handed out is the owner's decision (D-20).

## 1. Start

```powershell
dotnet build -c Release tools/CnCNew.DedicatedServer
dotnet run -c Release --no-build --project tools/CnCNew.DedicatedServer -- --port 27960
dotnet run -c Release --no-build --project tools/CnCNew.DedicatedServer -- --config server.json --allow-modified-builds
```

Port 0 takes a free port; the first line says which. Stop it with Ctrl+C or SIGTERM (§10.5). Exit code 2: the configuration, the ban file or the port is unusable
(the reason is on standard error).

**Without a development environment** (Phase 28): `pwsh scripts/package-server.ps1 -Zip` builds a self-contained package for Windows and for Linux (§13). In it:

```
CnCNew.DedicatedServer.exe --config server.json        (Windows)
./CnCNew.DedicatedServer --config server.json          (Linux, after chmod +x)
```

with `server.sample.json` as the configuration to start from and `START.md` as the guide for whoever runs it: the first start, the key and the fingerprint to
publish (§10.2), the firewall, a systemd unit, what to keep private, how to remove somebody (§10.3). A server for players needs four keys of the configuration
besides the defaults: `keyFile`, `logDir`, `replayDir` if the matches are to be kept, and `banFile`.

## 2. Configuration

A JSON file (`--config`), overridden by the command line. **An unknown key in the file and an unknown option are errors**, not defaults.

| Key | Option | Default | Meaning |
|---|---|---|---|
| `address` | `--address` | `0.0.0.0` | the address the socket is bound to (an IPv6 address for IPv6) |
| `port` | `--port` | 27960 | UDP port; 0: a free one |
| `maxRooms` | `--max-rooms` | 16 | rooms at a time; a hello for one more is rejected ("server full") |
| `maxMembersPerRoom` | `--max-members` | 16 | members of a room: at most 8 play (the protocol's seats), the others watch |
| `defaultTickRate` | `--tick-rate` | 60 | what a room states before its host has set it (30, 60 or 120); the host's room settings decide |
| `allowModifiedBuilds` | `--allow-modified-builds` | false | admit clients built from a modified source tree - the developer switch (§4) |
| `hashInterval` | `--hash-interval` | 1 | every how many ticks the clients report their session hash |
| `helloTimeoutMs` | | 5,000 | a connection that has not said its hello after this time is closed |
| `lagReportMs` | `--lag-report-ms` | 500 | from this lag on everybody is told who is behind; 0: never |
| `lagThrottleMs` | `--lag-throttle-ms` | 2,000 | above this lag of a player the game is slowed for everybody; 0: the server neither slows nor drops for lag |
| `throttleLimitMs` | `--throttle-limit-ms` | 10,000 | a member that stays above the limit this long is dropped |
| `throttleSpeedPercent` | `--throttle-speed` | 50 | the share of the set game speed while the game is slowed |
| `lagRecoveryMs` | `--lag-recovery-ms` | 30,000 | a member's used share of `throttleLimitMs` is forgotten only after it kept up for this long; 0: at once (the rule of Phase 26) |
| `seatHoldMs` | `--seat-hold-ms` | 0 | how long the seat of a player whose connection was lost is kept for it to come back; 0: it leaves the match at once. Proposal for operation: 300,000 (five minutes) |
| `spectatorDelayMs` | `--spectator-delay-ms` | 0 | how far behind the players somebody without a seat is sent the match; 0: at the same time. Proposal for operation: 120,000 |
| `allowLateWatchers` | `--allow-late-watchers` | false | admit somebody without a seat to a match that already runs |
| `pausesPerPlayer` | `--pauses-per-player` | 3 | how often a player may pause a match |
| `maxPauseMs` | `--max-pause-ms` | 60,000 | a pause ends by itself after this time; 0: never |
| `hostSetsSpeed` | `--anyone-sets-speed` | true | only the host sets the game speed (the option turns it off) |
| `replayDir` | `--replay-dir` | (none) | where the recording of every match is written as a replay file when it is over (§8); empty: nowhere |
| `connectionTimeoutMs` | `--timeout-ms` | 10,000 | a connection without a datagram for this long is lost |
| `keepAliveMs` | | 1,000 | a silent connection sends a datagram this often |
| `maxConnections` | | 256 | connections of the port; one more is refused |
| `idleWaitMicros` | | 2,000 | the longest the loop sleeps when nothing is due |
| `fineTimer` | `--no-fine-timer` | true | ask Windows for a 1 ms timer while the server runs (§5) |
| `statusSeconds` | `--status-seconds` | 30 | every how many seconds a status line is written; 0: never |
| `exitAfterRooms` | `--exit-after-rooms` | 0 | stop after this many rooms have closed (for a harness); 0: run until stopped |
| `runSeconds` | `--run-seconds` | 0 | stop after this many seconds (for a harness) |

The seeds of every match and the key of the connection cookies come from the system's cryptographic random source; neither is configurable.

## 3. Rooms and rules

- **A client names its room in its hello.** The first hello that names a room opens it and makes its sender the host; the room is removed when its last connection is gone,
  and its name is free again. There is no listing: whoever wants to play together agrees on a name (the empty name is a room like any other).
- A room is a lobby and then a match (`docs/NETWORK-PROTOCOL.md` §4 and §5). The first member's build identity, format versions and definition fingerprint define what the
  room plays with; everybody after it must be equal or is rejected with the reason.
- **Ticks are closed by the server's clock and wait for nobody.** A member that falls behind is shown from `lagReportMs` on; above `lagThrottleMs` of a player the game
  runs at `throttleSpeedPercent` for everybody; after `throttleLimitMs` of that the member is dropped (`docs/NETWORK-PROTOCOL.md` §14). These defaults are the proposal of
  `docs/MULTIPLAYER-PLAN.md` §7; the decision is the project owner's.
- **A player that leaves, is dropped or loses its connection** leaves the match by a command in the next tick's bundle: by choice a living ally inherits, otherwise
  everything of the player dies (`docs/SIMULATION-CORE.md` §47). The match goes on for the others; when nobody is left it is abandoned.
- **With `seatHoldMs`** a player whose connection is *lost* keeps its seat for that time and may come back with the token its client was given: it is handed the state
  of another member, checked against the hash all agreed on, and plays on (`docs/NETWORK-PROTOCOL.md` §18.2). Only when the time is over does the player leave the
  match. A player that leaves by choice or is dropped by the rule gets no held seat. The defaults of this version hold no seat and delay nobody: what a public server
  should do is the owner's decision (D-19), and the proposals stand in the table.
- **Somebody without a seat watches**: with `spectatorDelayMs` behind the players by that time, with `allowLateWatchers` also when the match already runs (§18.3 there).
  A watcher's orders are discarded and counted.
- **Pause and speed** of a match of several clients: only players ask; a player has `pausesPerPlayer` pauses, a pause ends when any player ends it or after
  `maxPauseMs`; the speed is the host's (§18.5 there). There are no votes.
- Two clients that report different session hashes for a tick end the match as a desync; the server tells every reported hash, and then in which parts of the hash
  the clients differ (`docs/NETWORK-DIAGNOSTICS.md`).

## 4. Builds

A room's members must be the same build. A client built from a tree that differs from its commit states `<commit>+modified`; a server admits such clients only with
`--allow-modified-builds`. Released builds need nothing. The server itself does not have to be the clients' build - it only has to speak the protocol and transport
versions, which the `listening` line states.

## 5. The loop

Single-threaded: take in datagrams, route new connections, pump every room, send, write the events, wait **on the socket** until the next tick of a room is due (at most
`idleWaitMicros`). On Windows the wait is only as fine as the system's timer - 15.6 ms unless the process asks for less -, which is why the server holds a 1 ms timer by default
(`docs/NETWORK-PROTOCOL.md` §13.4 with the measurement: late by 8 ms on average without, 0.4 ms with). How evenly each room closed its ticks is in its `tickIntervals`.

## 6. Output

One line per event on standard output: `ts=<milliseconds since the start> event=<name> [room=<name>] key=value ...`; a value with a blank is quoted.

| Event | When | Details |
|---|---|---|
| `listening` | the port is bound | address, port, protocol and transport version, build, the limits and the rule's numbers, whether the fine timer is held, the operating system and runtime |
| `connected` / `disconnected` | a client passed the cookie handshake / its connection is forgotten | address; at the end the reason and the connection's counters (datagrams, bytes, repetitions, round trip) |
| `turned-away` | a connection never reached a room | reason: `no-hello`, `not-a-hello`, `server-full` |
| `room-opened` / `room-closed` | first hello of a name / last connection gone | at the end: phase, ticks, last agreed tick, intents, discarded messages, slowed time, members dropped for lag, desync, tick intervals |
| `joined` / `rejected` / `left` | a member of a room | connection, name, build / reason / player, reason (`Closed`: after the end of the match), phase, tick |
| `seat-held` / `seat-returned` / `seat-given-up` | a player's connection was lost / the player is back with its token / the time is over and the player leaves | player, name, time left / connection / tick |
| `superseded` | a token arrived whose old connection still stood: the old one is closed as lost | connection, player, the new connection |
| `snapshot-relayed` / `snapshot-refused` / `snapshot-late` | a member's state was passed on to a joiner / had another hash than the agreed one / did not come in time | to, from, tick, bytes / stated and agreed hash |
| `joined-late` / `join-refused` | somebody stands at a state of the running match and is sent the bundles since / was turned away | connection, player, name, the state's tick, the server's tick, whether on a delay / reason |
| `paused` / `resumed` | a player paused / somebody resumed, or the pause reached its limit | by, tick, pauses left |
| `match-started` / `match-running` / `match-ended` | the host started / all have loaded / the end | map, tick rate, participants, members, pacing / tick, winner, reason, last agreed tick |
| `behind` / `caught-up` | a member crosses the report or the slowing limit | connection, player, lag, whether it slows the game |
| `throttled` / `full-speed` | the game is slowed / runs at the set speed again | tick, speed |
| `desync` | two session hashes of one tick differ | tick, every reported hash |
| `desync-parts` | clients have sent the parts of their hash at that tick | tick, every part that differs with each connection's value (`docs/NETWORK-DIAGNOSTICS.md` §3) |
| `replay-written` / `replay-skipped` / `replay-failed` | a match is over and `replayDir` is set | file, bytes, ticks, last agreed tick and hash / why not / the error |
| `status` / `room-status` | every `statusSeconds` | rooms, connections, handshake counters, memory / phase, members, tick, last agreed tick, speed, tick intervals |
| `stopped` | the loop ends | rooms opened and closed |

## 7. Trying it on one machine

`tools/CnCNew.NetTool` plays whole matches against the server with client processes without a window, through a UDP proxy that delays, loses, duplicates and reorders
(`docs/NETWORK-PROTOCOL.md` §15 and §16):

```powershell
pwsh scripts/check-net-match.ps1
pwsh scripts/check-net-robustness.ps1   # a rejoin, a late and a delayed watcher, the replay file played again, a desync named down to the field
pwsh scripts/check-net-join.ps1         # the window client joins a match on the server
pwsh scripts/check-net-cross-os.ps1     # server and two clients under Linux in WSL, two clients under Windows
pwsh scripts/check-net-load.ps1         # eight rooms at once with members that vanish, leave and come back; needs no game data (§11)
pwsh scripts/check-server-package.ps1   # the self-contained packages, the sample configuration, the ban file, a match on the packaged server (§13)
dotnet run -c Release --project tools/CnCNew.NetTool -- net-match game/content --map "Maps/Twilight Flame/Twilight Flame.map" --humans 6 --ais 2 --watchers 2 --minutes 5 --delay 40 --jitter 10 --loss-percent 1 --out <directory outside the repository>
```

## 8. The replay file of a match

With `replayDir` the server writes, when a match is over - ended, broken by a desync, or left by everybody -, two files: `<room>-<number>.replay` and
`<room>-<number>.replay.json`. The first is the match in the simulation's replay format (`docs/NETWORK-PROTOCOL.md` §18.4): the match description and every command
the server sent, in the bundles' order; the computer players' commands are not in it, they decide again when the file is played. The second is a note: room, ticks,
the **last tick the clients agreed on and its hash**, whether the match broke and at which tick, the server's build and protocol. The room name in the file name is
reduced to letters, digits, `-` and `_`; the number counts the files of this run and skips names that exist. An hour of an eight-player match is a file of a few
megabytes at most (the 60 s of the check: 5 KB).

```powershell
dotnet run -c Release --project tools/CnCNew.NetTool -- replay-check game/content <file.replay>
```

plays the file and compares the session hash with the note's: the server did not simulate, and that its file arrives at the hash the clients agreed on is what makes
it the record of the match. A match that was continued from a saved state gets no file (`replay-skipped`): its recording does not begin at tick 0. The server keeps
the recording in memory while the room exists (about 20 MB per hour of match); the memory is released with the room.

## 9. An open port: threat model, limits, fuzzing (Phase 28)

The server relays. It holds no game data, simulates nothing, opens no file but its own key, ban list, logs and replay files, runs one thread and references the
base class library only. What can go wrong with it is therefore what somebody can send to its one UDP port, what a member of a room can do to the others, and what
somebody on the way can read or change. The wire formats are `docs/NETWORK-PROTOCOL.md` §20.

### 9.1 The threat model

Each line: what somebody tries - the measure - the test that shows the measure - what remains. "Tests" are suites of `tests/CnCNew.Net.Tests` unless a script is
named; the residual risks are meant as stated, none is hidden behind a default.

| # | Threat | Measure | Test | Residual risk |
|---|---|---|---|---|
| T1 | **Reflection and amplification:** forged sender addresses make the server send traffic to a victim | a request is answered with a challenge of the same length (32 bytes for 32); a connect is answered only with a valid cookie, which proves the sender receives at its address; no answer is larger than what was sent except the accept (212 for 96 bytes) to a proven address | "UDP handshake", "Transport limits", "Fuzz, transport" (a changed datagram opens no connection) | the server can be made to send as many bytes to a forged address as it is sent - factor 1, bounded by T2's 2,000 answers a second |
| T2 | **Handshake flood:** many requests or connects to exhaust CPU, memory or the connection table | no state for a request; 10 handshake datagrams a second per address (20 at once), 2,000 a second for all; the table of addresses bounded (4,096); **key exchanges** - the expensive answer, about 2 ms of a core each (§11.1) - rationed to 100 a second (400 at once) for all senders; 8 connections per address, 256 in all | "Transport limits" (500 requests at once: 20 answered; 400 forged addresses: about 50 answers and no state; 20 connects at once with a ration of 5) | at the ration the handshakes take a fifth of the server's one core, and honest newcomers wait in the same queue: a sender with many real addresses can keep new connections out for as long as it keeps sending (running matches go on). A flood that fills the line is not the server's to stop |
| T3 | **Malformed input:** datagrams and messages that are too short, too long, of another version, with lengths that lie | every datagram and message is checked field by field against an upper limit before anything is applied; what fails is counted and dropped as a whole; the largest message is bounded per type | suite "Fuzz" (§9.4): 33 million changed inputs, no exception; "Message codec", "UDP layout" | a defect the fuzzing did not reach. The parsers allocate nothing proportional to a length field before the bytes are there |
| T4 | **Reading along:** somebody on the way reads orders, chat, the rejoin token | everything after the handshake is sealed (AES-256-GCM, keys from an ECDH exchange per connection, forward secrecy) | "Sealed transport", "Secrets stay off the wire" (no datagram of a session contains the token, the verifier, the chat or an order) | who talks to the server, when and how much is visible; so is the server's public key |
| T5 | **Pretending to be the server** (somebody on the way answers in its place) | the server signs the handshake with its permanent key; a client with a pinned fingerprint takes no other; a client without one remembers the key of the first meeting and refuses a changed one | "Sealed transport" (a changed and an unknown key refused; a forged accept ignored), `check-net-join` (a client told another key says nothing to the server; the window client remembers the key) | **the first meeting of a client that was not told the fingerprint** is unprotected. A stolen key file is the server |
| T6 | **Taking over a connection or a seat:** forging datagrams of a member, replaying its old ones, using its rejoin token | every datagram is authenticated under the connection's key; a number per datagram with a window of 1,024 refuses repetitions; a close is sealed too; the rejoin token is only ever sent sealed | "Sealed transport": **somebody who recorded every datagram of a member can neither take its held seat nor displace its standing connection**; forged and replayed datagrams are counted and neither deliver nor keep the connection alive | a member whose machine is compromised gives its token away with everything else. Cutting a member's line (not forging - dropping) still makes it lag and be dropped by the rule of §3 |
| T7 | **A member floods the room** with messages, intents or bytes | 500 messages a second per member (4,000 at once), then dropped (`Flood`); 64 intents per player and tick, the rest discarded and counted; 256 KiB a second per connection, a peer beyond for 10 s is closed | "Relay limits", "Hostile members" (a flooding member is dropped, the others end in the same state) | within the limits a member can still be a nuisance: 64 orders a tick are relayed to everybody (about 5 KB a tick) |
| T8 | **A member stalls the room:** never finishes loading, never reports a hash, never says the match is over, never acknowledges - or stays just below each of these limits for good | loading 180 s then dropped; 20 s of game time without the hash of a tick then dropped; a watcher is waited for by nobody; the backlog of a peer that does not acknowledge is bounded, then the connection closed; the rule for members that fall behind (§3) - ticks are closed by the server's clock and wait for nobody | "Relay limits", "Hostile members" (withheld hashes), "Members that fall behind", **"A member that stays just below every limit"** (2.9 s behind for a minute: nobody slowed; hashes 19 s late: agreed 19 s late and no later; across either limit: dropped) | a member can keep the *agreement* on the ticks up to 20 s behind for as long as it likes: a desync is then found that much later, and a joiner waits that much longer for a state. A player that crosses the lag limit slows the match to half speed for 10 s once before it is dropped (the rule, decision D-17) |
| T9 | **A member makes the server remember without bound** | a state at most 4 MiB; a match at most 2 h of game time; the recording at most 64 MiB, then the match ends (`ServerLimit`); agreed hashes kept for a window; at most 16 members a room, 16 rooms; states asked at most 6 times a minute; a lobby or a finished room is emptied after 60 min; a connection without a hello after 5 s | "Relay limits"; `check-net-load` and the endurance run of §11.3 (working set and handles over hours) | a full server holds 16 rooms × (64 MiB recording + states in transit): about 1.2 GB at the bounds, far above what honest matches use (§11.2) |
| T10 | **Orders no player can give:** system commands (spawn, give money, kill), commands for another player, garbage behind a plausible header, coordinates that overflow | the relay stamps the player of the connection; only the 24 order types of a player pass a wire; a command that does not decode is left out by every client alike and by the replay; a point beyond 131,072 units is rejected by every simulation at submission (`docs/SIMULATION-CORE.md` §49) | "Hostile members" (garbage and system commands: every client leaves them out, the replay plays to the agreed hash), "Fuzz, commands" (1.5 million changed real orders per seed: no exception in the pre-check, the submission or the ticks after), `Simulation.Tests` "Command limits" | an order that is valid but senseless is the simulation's to reject, as from a local player. A defect in a command handler that the fuzzing did not reach would hit every client alike (no desync, but a crash of all) |
| T11 | **A member lies about its state** to end the match (a wrong hash) or to break a joiner (a wrong state) | a state is passed on only when its stated hash is the one all agreed on, and the joiner's own hash of what it built is compared; a **watcher's** wrong hash drops the watcher only | "Rejoin" (a state with another hash goes nowhere), "Watchers" (a watcher that diverged is dropped, the match goes on), "Fuzz, states" | **a player that reports a wrong hash ends the match for everybody as a desync** - the relay does not simulate and cannot tell who lies. Proposal D-24: with three or more players the majority decides and the minority is dropped (not built) |
| T12 | **Cheating by seeing more** (a changed client shows what the fog hides) | none | - | lockstep gives every client the whole state. Out of scope of the project (no anti-cheat); a spectator delay keeps *watchers* from helping a player (`spectatorDelayMs`) |
| T13 | **Entering a room one is not wanted in** | the room password as challenge and response (the password never travels); the host removes a member, and neither its origin nor its player key may come back to the room; the operator's ban list of addresses, networks and **player keys** (a key is proved by a signature over the room's nonce); `requirePlayerKey` | "Room password and kick" (wrong and replayed answers, the kick, the origin), "Transport limits" (a banned address gets no answer and its connection is closed), `check-server-package` (a ban file with a line that is no entry stops the server at its start), "Player keys" (a banned key leaves and does not come back from another address; a stolen or replayed proof is refused) | a kicked or banned person comes back from another address and with a new key - both cost nothing; there are no accounts. The server could enter a room itself (it holds the verifier) |
| T14 | **Personal data in the logs** | addresses are written as their network (`/24`, `/48`) and without port unless `logFullAddresses`; logs are deleted after `logRetentionDays` (14); replay files hold no addresses; chat is relayed and never logged | the address form: the server's own lines in `check-net-load` and `check-net-join` | player names and the fingerprints of player keys appear in `joined` lines - a fingerprint is a permanent pseudonym of a player across servers; a network of 256 addresses can still identify a household together with a time |
| T15 | **The supply chain** | no NuGet package in the server (base class library only); `scripts/audit-vulnerable.ps1` fails if the server references any package, and checks the solution for known advisories | the script; "Layer rules" (the server references `CnCNew.Net` and `CnCNew.Server` only) | the .NET runtime inside a package is as old as the SDK that built it: a package is rebuilt when a runtime update is published |
| T16 | **The list of rooms tells a stranger who plays** (Phase 29): which rooms exist, how many sit in them, the map, the build, whether a room is locked - and asking for it costs the server an answer | a list names no person, address or key; at most 64 rooms a list; a connection gets a list at most once a second and at most 120 times, then it is turned away; a browsing connection counts against the connections per address; `listRooms: false` (`--no-room-list`) lists nothing | "Room list" (nothing about people in the bytes; too soon, too often, not at all); "Fuzz, directory" | room names are chosen by the people who open them and are shown to everybody who asks - a name is not a secret; a locked room is still listed (with its lock) |

### 9.2 Limits and what happens at each

Every limit is configuration (`--config` key, also `--set key=value`); **0 turns a limit off** unless stated. The default is the proposal; D-17, D-19 and D-20
(the rule for members that fall behind, held seats and the spectator delay, the server under Linux) stay the owner's decisions and their keys keep the defaults of §2.

| Key | Default | What is bounded | At the limit | Counter (`status` / `room-closed`) |
|---|---|---|---|---|
| `maxConnections` | 256 | connections of the port | the connect is refused (reason 1) | `refused` |
| `maxConnectionsPerAddress` | 8 | connections of one address | the connect is refused (reason 2) | `refused` |
| `handshakesPerAddressPerSecond`, `handshakeBurst` | 10, 20 | requests and connects answered per address | dropped unanswered | `rateLimited` |
| `handshakesPerSecond` | 2,000 | requests and connects answered for all senders | dropped unanswered | `rateLimited` |
| `keyExchangesPerSecond`, `keyExchangeBurst` | 100, 400 | key exchanges (new connections) for all senders | the connect is dropped; the client sends it again | `rateLimited` |
| `inboundBytesPerSecond`, `inboundBurstBytes` | 262,144, 8,388,608 | bytes a connection sends the server | the datagram is dropped; after 10 s of that the connection is closed (`Flood`) | per connection `policed` in `disconnected` |
| `connectionTimeoutMs` | 10,000 | silence of a connection | the connection is lost | `disconnected reason=Timeout` |
| `helloTimeoutMs` | 5,000 | a connection without a hello | closed | `turnedAway`, `helloTimeouts` |
| `maxRooms` | 16 | rooms | a hello for one more is rejected (`ServerFull`) | `turnedAway` |
| `maxMembersPerRoom` | 16 | members of a room | rejected (`RoomFull`) | `rejected` lines |
| `requirePlayerKey` | false | who may enter at all: only clients that state and prove a player key | rejected (`BadIdentity`) | `rejected` lines |
| `messagesPerSecond`, `messageBurst` | 500, 4,000 | messages of a member | the member is dropped (`Flood`) | `floodDrops` |
| (protocol) | 64 | intents per player and tick | the rest is discarded | `discarded` |
| `loadLimitMs` | 180,000 | loading | members that have not loaded are dropped (`TooSlow`), the match starts | `loadTimeouts` |
| `hashReportLimitMs` | 20,000 | game time without the hash of a tick from a member that plays along | the member is dropped (`ProtocolViolation`) | `hashStalls` |
| `maxStateBytes` | 4,194,304 | a session state (start from a save, a joiner's state) | the state is refused; the next member is asked | `statesRefused` |
| `snapshotsPerMinute` | 6 | states asked of members | the join is refused (`CannotCatchUp`) | `joinsRateLimited` |
| `maxMatchSeconds` | 7,200 | game time a host may set | the room settings are refused | `rejected` lines |
| `maxRecordingBytes` | 67,108,864 | the commands of a match in memory | the match is ended (`ServerLimit`) | `recordingBytes`, `match-ended reason=ServerLimit` |
| `roomIdleLimitMs` | 3,600,000 | a lobby that never starts, a room whose match is over | everybody is sent home | `room-idle` line |
| `logRetentionDays`, `replayRetentionDays` | 14, 0 | age of log and replay files | deleted | `replays-expired` |

**The byte limit and a rejoin.** A joiner's state is the largest thing a member ever sends: 82 to 775 KB (`docs/NETWORK-PROTOCOL.md` §19). At 256 KiB a second in the long
run with 8 MiB at once the whole state is inside the burst, so the limit never throttles it; `docs/NETWORK-PROTOCOL.md` §21.3 repeats the rejoin measurement of
Phase 27 on five lines with the limit in force.

### 9.3 What the limits were not made for

They bound what the server computes and remembers per sender. They are not a matchmaking policy, not a reputation system and not a defence against volume. The
values are proposals from the measurements of §11 and of `docs/NETWORK-PROTOCOL.md` §16, §19 and §21 on one machine; an operator with other hardware or other
players changes them in the configuration.

### 9.4 Fuzzing

The suite "Fuzz" of `tests/CnCNew.Net.Tests` (`FuzzTests.cs`) feeds the parsers and the room changed inputs from a seed: every input is a real message, datagram,
order or state with a few bytes flipped, cut, spliced with another one, lengths set to their extremes, or random bytes. A run is reproducible by `--fuzz-seed N
--fuzz-cases N`; an input that throws is printed as hexadecimal with seed and number, and is added to the **corpus** in the source file, which every run plays first -
a finding stays a regression test. Each input is bounded in time (5 s) and allocation (8 MiB).

| Target | What an input reaches | Passes when |
|---|---|---|
| messages | `MessageCodec.TryDecode` | nothing throws; a refusal names its reason; what decodes encodes again to the same bytes |
| transport | `UdpFormat`, a link with its cipher, a server endpoint with a real client beside the fuzzer | nothing throws; no connection opens; nothing is delivered; the real client is still heard |
| relay | the room, from a member that watches, a stranger without a hello, the host and a player; a client fed by a hostile server | nothing throws; the match of the two honest clients stays agreed on every tick; the room's memory stays bounded; the hostile member is dropped and comes again |
| commands | `CommandWire`, the simulation's pre-check, the submission and the ticks after, inside a living match of three players | nothing throws; changed orders are refused by the wire, rejected by the simulation, or simulated |
| states | the session transfer of a rejoin | a damaged state is refused in the words a client understands, never with another exception |

Run on 12 October 2026 (state `10e6ab0`, Debug, Windows): the default seed in every `Net.Tests` run - 200,011 + 125,008 + 120,000 + 40,002 + 603 inputs, about
25 s -; and three further seeds with 4,000,011 + 2,500,008 + 3,000,000 + 1,500,002 + 12,003 inputs each, **33,036,072 inputs in all: no exception, no hang, no
unbounded growth.** Of the 4.5 million changed orders 77,028 were accepted and simulated, 2.64 million rejected by the simulation and 1.78 million refused by the
wire.

**Found while the suite was written, and by the tests and measurements of the phase** (each fixed, each with its test):

1. The server's replay file numbered a player's commands by the relay's order number. An order that no client could decode was skipped by the clients and counted
   by the file, so the file no longer played to the agreed hash (`RecordingFile`; "Hostile members").
2. A watcher's wrong hash ended the match of the players, and a watcher that never said "the match is over" kept a finished match open (`RelayServer`: only the
   players that play along decide; "Watchers", "Fuzz, relay").
3. A command of a type only the system gives (spawn, give money) passed the relay like an order (`CommandWire.IsPlausible`; "Hostile members").
4. **A key that is no point of the curve could throw out of the handshake.** Most such keys make the base class library throw a `CryptographicException`, which
   the transport caught; the point (0, 0) makes Windows throw a `PlatformNotSupportedException`, which it did not. A connect with a valid cookie and 64 zero bytes
   as its key would have ended the server's loop. Neither the fuzzing (which changes bytes of valid keys) nor the first tests produced that key; the test of the
   player key did. `UdpCrypto` now treats every exception a bad key can raise as a bad key ("UDP handshake": three keys that are no points of the curve).
5. The bundle of a busy tick, larger than a datagram, was not repeated early when it was lost (`docs/NETWORK-PROTOCOL.md` §21.5) - found by the matrix.
6. A client that held the cookies of two time slots was replaced by its own second connect (`docs/NETWORK-PROTOCOL.md` §21.6) - found by the rejoin measurement.
7. A server that had run for an hour closed every new room at once: the idle limit of a room counted from the start of the server's clock (§11.3) - found by the
   endurance run, after exactly one hour.

What the fuzzing does not show: inputs it did not generate - finding 4 is the proof. It is mutation from valid samples with a fixed budget, not coverage-guided;
the simulation's command handlers are reached only through orders the generator can still make valid (1.7 % of them). The 33 million inputs ran at state `10e6ab0`,
before the player key and the fixes 4 to 7; every `Net.Tests` run since repeats the default seed (485,000 inputs) on the current code.

## 10. Operation (Phase 28)

### 10.1 The keys of the configuration that are new

| Key | Option | Default | Meaning |
|---|---|---|---|
| `keyFile` | `--key-file` | (none) | the server's permanent key (PEM, PKCS#8, P-256); made on the first start. Without it the key lasts as long as the process, with a `warning` line |
| `banFile` | `--ban-file` | (none) | addresses and networks that get no answer, and player keys that are turned away (§10.3) |
| `requirePlayerKey` | `--require-player-key` | false | admit only clients that state a player key and prove it - what makes a ban by key hold as far as it can |
| `logDir` | `--log-dir` | (none) | one file per day besides standard output (§10.4) |
| `logRetentionDays` | | 14 | days a log file is kept; 0: for ever |
| `replayRetentionDays` | | 0 | days a replay file is kept; 0: for ever |
| `logFullAddresses` | `--log-full-addresses` | false | write addresses in full with their ports |
| the limits of §9.2 | `--set key=value`, `--max-connections-per-address` | | |

`--set key=value` sets any key of the file from the command line; an unknown key is an error there too.

### 10.2 The server's key

The `listening` line states `key=<fingerprint>`: 64 hexadecimal digits, the SHA-256 of the public key. Publish it with the address; a client started with
`--server-key <fingerprint>` takes no other server, a client without it remembers the key of its first meeting (`docs/NETWORK-PROTOCOL.md` §20.2). The key file is the
server: keep it readable for the server's account only. A new key makes every client that remembered the old one refuse the server - with a message that names both
fingerprints and the file to edit -, which is the point; announce a change of key before making it.

### 10.3 Bans

A text file, one entry a line: an address (`203.0.113.7`, `2001:db8::7`), a network (`203.0.113.0/24`, `2001:db8::/48`) or a player key (`key:` and the 64
hexadecimal digits of its fingerprint, as the `joined` line shows it); `#` starts a comment. The server reads it again within five seconds of a change, closes the
connections from those addresses and answers nothing from them any more, and tells the members whose key is on the list that they are banned and drops them
(`bans-loaded` with the count of entries, of connections closed and of members dropped; `banned` in the status counts the datagrams). A client whose key is banned
is turned away at its hello with the reason `Banned`. **A ban by key holds only against somebody who keeps the key:** a client may state none unless
`requirePlayerKey` is set, and may make a new one at any time (`docs/NETWORK-PROTOCOL.md` §20.5). **A line that is no entry is an error:** at the start the server refuses to run, later it keeps the list it had and says so
(`ban-file-error` with the line number) - a typing mistake never unbans everybody. A file that does not exist bans nobody. The host of a room removes a member with `Kick`; that keeps the member's origin
out of that room and is no ban of the server.

### 10.4 Logs, and what is in them

Every line goes to standard output and, with `logDir`, to `server-<yyyyMMdd>.log` (UTC) with the time of day in front; files older than `logRetentionDays` are
deleted at the start and at every change of day. **An address is personal data**: by default it is written as its network - the last byte of an IPv4 address and the
last 80 bits of an IPv6 address removed, `/24` or `/48` behind it - and without its port. `logFullAddresses` writes them in full; decide that with the law of the
place the server runs in, and keep the retention as short as the purpose allows. What else a log holds about a person: the name a player gave itself and the fingerprint of its
player key, if it stated one (`joined ... key=`) - the same fingerprint on every server the player joins -, and when it played. Chat is relayed and never written. Replay files hold orders and factions, no names and no addresses.

New events besides §6: `warning`, `bans-loaded`, `ban-file-error`, `room-locked` (a room has a password), `bad-password`, `kicked`, `flood`, `hash-stalled`,
`load-timeout`, `no-hello`, `room-idle`, `recording-limit`, `watcher-desync`, `bad-identity`, `banned`, `replays-expired`. The `status` line also carries `rateLimited`, `banned`, `forged`,
`replayed`, `workingSetKb`, `cpuMs`, `bytesIn`, `bytesOut`, `datagramsIn`, `datagramsOut`, `handles` (open descriptors under Linux), `threads`, `roomsOpened`,
`roomsClosed`, `connectsAccepted`; `room-closed` carries the room's counters of §9.2.

### 10.5 Stopping

Ctrl+C and SIGTERM stop the loop: every connection is told at once that it ends (the clients do not wait for a timeout), then `stopped`. The recording of a match
that was still running is not written - stop when `status` shows `rooms=0` if the replays matter. A restart with the same `keyFile` is the same server to its
clients; the rooms are gone (a match does not survive its server, and held seats are held in memory).

## 11. Capacity and endurance (Phase 28)

**Everything in this section was measured on one machine** - an AMD Ryzen 3 PRO 4350GE (four cores, eight threads, 3.5 GHz), Windows 11, the server of the
`win-x64` package, the load on the same machine over the loopback interface. It is the capacity of *this* computer with its load generator beside it; another
processor, a real network card and a real line will give other numbers, and nothing here was run on a second computer.

### 11.1 What a connection and a room cost

`NetTool seal-cost` and `NetTool load` (build `057c3a9`). The load generator fills N rooms with eight members each that speak the whole protocol through the sealed
transport - handshake, hello, seats, loading, a hash report for every tick, 1.5 orders a second per player, pings, the end - and simulate nothing; the relay does
not simulate either, so to the server such a member is a client. One match of 120 s per room, all rooms at once, no churn; the server's numbers are from its own
`status` lines while every room was in its match.

| Rooms of eight | Members | Server CPU (share of one core) | per room | Working set | Managed heap | Datagrams in + out / s | Order to bundle: median / 95 % / 99 % | Largest deviation of a tick | Load generator (cores) |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 8 | 4.4 % | 4.4 % | 51 MB | 3 MB | 1,034 | 16.7 / 18.3 / 18.6 ms | 10.1 ms | 0.06 |
| 8 | 64 | 16.6 % | 2.1 % | 55 MB | 8 MB | 8,264 | 17.0 / 18.1 / 18.7 ms | 8.8 ms | 0.22 |
| 16 | 128 | 28.7 % | 1.8 % | 65 MB | 13 MB | 16,533 | 16.7 / 18.7 / 19.8 ms | 8.8 ms | 0.49 |
| 32 | 256 | 42.6 % | 1.3 % | 75 MB | 24 MB | 33,047 | 16.8 / 19.0 / 20.0 ms | 9.6 ms | 0.99 |
| 64 | 512 | 73.7 % | 1.2 % | 97 MB | 46 MB | 65,794 | 17.0 / 20.8 / 22.5 ms | 20.8 ms | 1.63 |

- **CPU:** an empty server idles at about 2 % of a core (the loop wakes every 2 ms); every room of eight at 60 Hz adds **about 1.1 % of one core** (1.2 % up to
  32 rooms). All 64 rooms finished their match with every tick agreed; at 64 rooms the server stood at three quarters of its one core, 39 of 7,199 ticks of a room
  were more than 2 ms off their time (14 at 32 rooms) and one by 21 ms, and the 99 % value of an order's way to its bundle rose from 19 to 22 ms - with the load generator taking 1.6 of the
  machine's four cores beside it. **The server is one thread:** a second core does not help it, and the room count where it stops keeping its ticks on this
  processor is somewhere above 64 and was not searched for.
- **Memory:** 51 MB for the process, and **about 0.7 MB per room** while a match of two minutes runs (garbage included: after the 64 rooms had closed the
  process still held 98 MB until the next collection; §11.3 is what it does over hours). A room's recording grows with its match: 3.3 MB for an hour
  of six busy players (`docs/NETWORK-PROTOCOL.md` §19.3), bounded at 64 MiB (§9.2).
- **Bandwidth per room of eight:** on the loopback interface, where nothing is lost and every datagram is acknowledged at once, **46 KB/s in and 50 KB/s out**
  (5.8 and 6.2 KB/s per member). On a line with delay and loss a client's traffic is about twice that, because unacknowledged bundles and reports ride along
  (`docs/NETWORK-PROTOCOL.md` §21.1: 10.9 KB/s up and 10.7 KB/s down per client on average over the matrix, 11.4 KB/s at 40 ms and more) - plan with
  **about 90 KB/s in each direction per room of eight at 60 Hz**, half of it at 30 Hz, twice at 120 Hz, plus 28 bytes of UDP and IP header on each of about 1,000
  datagrams a second per room. A state for a joiner is a burst of 0.1 to 0.8 MB on top.
- **A new connection** costs the server 1.7 to 2.0 ms of its core (the key exchange and the signature); sealing and opening the datagrams of a room of eight costs
  0.3 ms per second (`docs/NETWORK-PROTOCOL.md` §21.2).
- **The defaults** - 16 rooms, 256 connections - are far inside this on this machine: 16 rooms of eight took 29 % of a core. They are defaults for a first server,
  not the machine's limit; raise `maxRooms` and `maxConnections` together.

What the load generator does not show: clients (its members report a constant hash), a network (loopback), and a hostile load - the limits of §9.2 were shown by
tests, not by a flood from outside.

### 11.2 How the endurance of a server is run here

The brief asks for 24 hours. A run of this project's agent ends after two hours, so the run is built from pieces - and that is also how anybody can repeat it:

1. **The server is started detached** from the shell that starts it (under Windows through WMI: `Invoke-CimMethod Win32_Process Create` with the command line
   `cmd /c "<package>\CnCNew.DedicatedServer.exe ... > server.out 2> server.err"`; a process started with `Start-Process` still belongs to the shell's job and
   ends with it). It gets `--run-seconds` so that it stops by itself with a `stopped` line, a key file, a log directory, a replay directory, held seats
   (`--seat-hold-ms 60000`), and the limits per address raised - all load comes from one address. Its `status` line once a minute is the intermediate result.
2. **The load is driven in pieces of under two hours**, each two things at once against that one server: `NetTool load` - eight rooms of eight members that
   simulate nothing, match after match of four minutes, in seven of ten matches a player that vanishes, says it leaves, or vanishes and comes back with its token -
   and, beside it, **real matches** one after another (`NetTool net-match --server 127.0.0.1:PORT --server-log server.out`): three client processes that simulate,
   five computer players and a watcher, through the proxy at 40 ms / 10 ms / 1 %, 90 s each, in turn with a rejoin, a vanished process, a leave, or nothing. Every
   real match must end with one hash chain on its clients and the server's agreement on every tick.
3. **The log is read at the end** (and in between): memory, handles, threads and CPU over time, the counters, and every room that hung - a room whose tick stood
   still over two status lines, or that was open for more than twenty minutes.

### 11.3 The endurance run of Phase 28 (13 October 2026, build `c1946aa`, the server of the `win-x64` package)

**The server ran for 9.00 hours and stopped by itself; 8 hours 25 minutes of that under load, in five pieces.**

| Hour | Rooms | Connections | Working set | Managed heap | Handles | Threads | CPU seconds | Rooms opened / closed | Connections accepted |
|---|---|---|---|---|---|---|---|---|---|
| 0.02 | 9 | 68 | 54 MB | 6.9 MB | 273 | 5 | 9 | 9 / 0 | 69 |
| 1.02 | 9 | 67 | 79 MB | 17.6 MB | 273 | 5 | 575 | 163 / 154 | 1,206 |
| 2.02 | 9 | 65 | 71 MB | 12.7 MB | 270 | 4 | 1,131 | 308 / 299 | 2,270 |
| 3.02 | 9 | 62 | 70 MB | 12.2 MB | 270 | 4 | 1,705 | 462 / 453 | 3,399 |
| 4.02 | 9 | 65 | 68 MB | 12.5 MB | 270 | 5 | 2,264 | 615 / 606 | 4,527 |
| 5.02 | 8 | 61 | 64 MB | 10.1 MB | 273 | 5 | 2,831 | 768 / 760 | 5,648 |
| 6.02 | 8 | 63 | 76 MB | 10.6 MB | 273 | 5 | 3,386 | 921 / 913 | 6,782 |
| 7.02 | 9 | 67 | 65 MB | 7.1 MB | 270 | 4 | 3,939 | 1,074 / 1,065 | 7,902 |
| 8.02 | 9 | 68 | 64 MB | 6.0 MB | 273 | 5 | 4,503 | 1,228 / 1,219 | 9,031 |
| 8.98 (idle) | 0 | 0 | 54 MB | 3.1 MB | 270 | 4 | 4,752 | 1,280 / 1,280 | 9,414 |

- **No growth:** the working set moved between 54 and 83 MB with the garbage collector (mean of the first quarter of the run 68.8 MB, of the last 65.7 MB) and was
  54 MB again when the last room had closed - what it was after the first minute. Handles 269 to 276 throughout, four to six threads.
- **No hanging room, no crash:** 1,280 rooms opened and 1,280 closed; 1,280 matches started, ran and ended as finished; no room's tick stood still over two status lines and none was
  open for more than twenty minutes (a match is four); the server wrote 1,280 replay files (119 MB) and stopped with its `stopped` line.
- **The load:** 1,000 matches of members that simulate nothing (1,000 finished; 459 members vanished, 231 said they left, 224 of 224 that came back played on)
  and **280 real matches** of client processes (70 each with a rejoin, a vanished process, a leave and nothing): **280 of 280 ended with one hash chain on their
  clients and every tick agreed by the server.** In all 9,414 connections, 599 lost ones whose seat was held, 294 rejoins - each handed a state and each playing
  on -, 235 seats given up when their time was over, 301 leaves; no desync, no forged, malformed or rate-limited datagram, nobody refused; 5,243
  datagrams of connections the server no longer knew - the old connection of a client that had come back, the last datagrams of a member after its close - were
  counted (`unknownPeer`) and dropped.
- **CPU:** 4,742 s in 8.97 h - 14.7 % of one core on average for nine rooms (eight of them at 60 Hz with eight members each); about 367 KB/s in and 417 KB/s out.

**What the first attempt found.** The first run was started with build `057c3a9` and stopped after 78 minutes: at 60 minutes and 23 seconds of the server's
life every new room was closed the moment its first member entered it (`room-idle`), and it went on so. The limit for a lobby that never starts
(`roomIdleLimitMs`, an hour) measured from a time that was never set for a new room, so it measured from the start of the server's clock: **after an hour of
running, a server with the default limits admitted nobody.** No test had seen it, because the clock of every test started at zero. Fixed in `c1946aa`; the
clocks of the test fixtures of `Net.Tests` now start at three hours, and a test opens a room on a server that has run for two. The run above is the second one.

**What this run does not show:** 24 hours (9 were run; the first hour boundary, where the first attempt failed, is inside, a day's boundary - the daily log file,
the expiry of logs and replays - is not); more than nine rooms at a time for hours (the capacity runs of §11.1 lasted two minutes); a second computer, a real
network, or a hostile load; and members that simulate were three per real match - the eight-client matches are `docs/NETWORK-PROTOCOL.md` §21.

## 12. Versions in operation (Phase 28)

Three things must agree before a player can play on a server, and each failure has its own message - none ends in a timeout or a desync:

| What | Who compares | Stated where | A mismatch |
|---|---|---|---|
| **Transport version** (2) | every datagram | `listening transport=` | the datagram is dropped and counted; the client reports "no connection to the server" after its connect timeout (5 s). A client and a server of different transport versions cannot say more to each other than that - the version is the first thing that would have to be understood |
| **Protocol version** (4) | the room, on the hello | `listening protocol=` | `Reject` with `ProtocolVersion` and a text that names the server's and the client's version; the window client shows it and exits with code 4 |
| **Build identity, format versions, definition fingerprint** | the room, against its host's | `joined build=`, the hello | `Reject` with `SimulationVersion`, `ModifiedBuild`, `FormatVersion` or `Definitions`, naming both values: the members of one room must be the same build with the same game data |

Rules for an operator:

- **The server need not be the players' build.** It has to speak their transport and protocol versions; rooms of different client builds can run side by side on
  one server, because each room compares its members with its own host.
- **A change of the transport or protocol version is a new server for old clients.** Announce it; run the old and the new server on two ports for the time of the
  change if the players should not all update at once. Nothing in the protocol negotiates a version down - a mixed room would be two games.
- **A released build is one commit.** A build from a changed tree states `<commit>+modified` and is admitted only with `allowModifiedBuilds`, which is a developer's
  switch: two modified builds of the same commit are not known to be the same code. `scripts/package-server.ps1` says so when the tree is not clean.
- **A change of the server's key is a new server to every client that remembered the old one** (§10.2).
- A replay file states the build and the format versions it was recorded with; it plays with a client of that build (`docs/NETWORK-PROTOCOL.md` §18.4).

The history of the versions: protocol 1 (Phase 25), 2 (Phase 26), 3 (Phase 27), 4 (Phase 28); transport 1 (Phase 26), 2 (Phase 28). What each added is at the top
of `docs/NETWORK-PROTOCOL.md`.

## 13. The package (Phase 28)

```powershell
pwsh scripts/package-server.ps1 -Zip          # artifacts/server/win-x64, artifacts/server/linux-x64, and a zip of each
pwsh scripts/check-server-package.ps1         # builds both, checks their contents, plays a match on the win-x64 server
pwsh scripts/check-server-package.ps1 -Platform linux   # the linux-x64 server under WSL with an empty environment
```

- **Self-contained:** each package brings the .NET runtime it was published with (about 81 and 83 MB, 204 files) and needs none on the machine. Of this
  repository it holds five assemblies - `CnCNew.DedicatedServer`, `CnCNew.Server`, `CnCNew.Net`, and the two those reference for the command codec and the match
  description, `CnCNew.Simulation` and `CnCNew.Data` - and nothing of the client, the game layer, the importer or Stride; the check fails if another one appears.
  No NuGet package (`scripts/audit-vulnerable.ps1`).
- **Windows:** `CnCNew.DedicatedServer.exe`, Windows 10 or later, x64.
- **Linux:** `CnCNew.DedicatedServer` (`chmod +x` after unpacking a zip), x64, a distribution .NET 10 supports (run here: Debian 12 with glibc 2.36), and OpenSSL (`libssl`; run here: 3.0) for the cryptography of the
  connection. Built without culture data, so no ICU library is needed. SIGTERM stops it like Ctrl+C.
- Beside the program: `START.md` (the start guide: first start, the key and its fingerprint, firewall, a systemd unit to adapt, what to keep private),
  `server.sample.json`, `LICENSE.md`, `NOTICE.md`. Their sources are `tools/CnCNew.DedicatedServer/package/`.
- The packages are build results and are never committed (`artifacts/` is ignored). They contain no game data - the server never reads any.

**What was run** (12 October 2026, this machine): both packages built under Windows; the win-x64 server started as its own program with the sample configuration
(it made its key file and its log directory) and a match of three client processes, five computer players and a watcher played on it through the proxy with one
hash chain; the linux-x64 server started in the WSL distribution `Debian` (Debian 12, glibc 2.36, OpenSSL 3.0) **with an empty environment** - `env -i`, a `PATH`
of the system's directories, `HOME=/tmp`, no `DOTNET_ROOT`; the distribution has no system-wide .NET, and the runtime in the user's `~/.dotnet` is on no path the
process sees - and a match of three client processes under Windows played on it over the distribution's address, again with one hash chain.

**What that does not show:** the Windows package on a machine without a .NET installation (this machine has the SDK; a self-contained program does not look for
it, but that was not observed on a bare machine), another Linux distribution or an older glibc, a real network between the two sides (the WSL network is a virtual
switch on the same computer), and D-20 is a delivery decision of the owner - the package exists and runs, whether it is handed out is not decided here.

## 14. The server as a library, and "Host game" (Phase 29)

`CnCNew.Server.ServerCore` holds what a server is between its socket and its rooms: the transport endpoint (`UdpServerEndpoint`), the directory of rooms
(`RoomDirectory`) and one round of work (`Pump`: datagrams in, new connections to the directory, every room moved, datagrams out), `WaitMicros` (how long a host may
sleep), `CloseAll` (everybody is told that the server stops). `tools/CnCNew.DedicatedServer` runs it since Phase 29; so does a game that hosts.

| | The dedicated program | "Host game" (`CnCNew.Game.Lockstep.HostedServer`) |
| --- | --- | --- |
| runs in | a process of its own | a thread of the game, so its rooms close their ticks by the clock whatever the game's frames do |
| key | `keyFile`, made on the first start | `host-key.pem` in the game's network directory, made on the first hosting |
| builds admitted | released ones, unless `allowModifiedBuilds` | the build that hosts (a room compares everybody with its host) |
| rooms | `maxRooms` (16) | 4 |
| limits of transport and room | section 9.2 | the same defaults - a hosted game is a server with an open port |
| bans, logs, replay files, status lines | section 10 | none: no file is written; the events are kept as lines the game can read (`TryTakeLine`; the client prints them as `host-server:`) |
| stops | SIGTERM, `exitAfter...` | with the visit or the game; every connection is told |

The hosting player's own game is a client of that server like every other, through a socket. What "Host game" is not: a way to be found (the others need the
address and the port must be reachable - no NAT traversal, as before), and not a place for long-running operation (no bans, no logs, no replay).

Configuration added in Phase 29: `listRooms` (default true), option `--no-room-list`; the status line counts `listsServed` and `listsRefused`.

## 15. When something goes wrong (Phase 30)

- **A crash.** An exception nothing caught ends the server with exit code 70 and leaves `crash-<UTC time>-<process>.txt` in the log directory (the working
  directory without one): the build, the protocol and transport versions, the system, the arguments, the configuration (it holds no secret; the key and the ban
  list are files it only names) and the exception. Standard error gets `event=crash type=... report=<file>`. A service manager restarts the server; the report
  stays.
- **The support bundle.** `CnCNew.DedicatedServer --support-bundle FILE --config server.json` packs the log files and crash reports of the configured log
  directory, the configuration and a description into a ZIP and stops - the server is not started. It never holds the key file, the ban file (addresses are
  personal data) or replays; `CONTENTS.txt` lists what it holds. It is sent nowhere.
- `scripts/check-diagnostics.ps1` proves both on every gate of `net` (`docs/TESTING.md` §8). Troubleshooting for players and operators, including a match that
  diverged: `docs/TROUBLESHOOTING.md`.

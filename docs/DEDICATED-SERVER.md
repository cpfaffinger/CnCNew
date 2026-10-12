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

Port 0 takes a free port; the first line says which. Stop it with Ctrl+C. Exit code 2: the configuration or the port is unusable (the reason is on standard error).

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
| T8 | **A member stalls the room:** never finishes loading, never reports a hash, never says the match is over, never acknowledges | loading 180 s then dropped; 20 s of game time without the hash of a tick then dropped; a watcher is waited for by nobody; the backlog of a peer that does not acknowledge is bounded, then the connection closed; the rule for members that fall behind (§3) | "Relay limits", "Hostile members" (withheld hashes), "Members that fall behind" | a player that plays as slowly as the rule just allows slows the match to half speed for up to 10 s at a time (the rule's weak point, decision D-17) |
| T9 | **A member makes the server remember without bound** | a state at most 4 MiB; a match at most 2 h of game time; the recording at most 64 MiB, then the match ends (`ServerLimit`); agreed hashes kept for a window; at most 16 members a room, 16 rooms; states asked at most 6 times a minute; a lobby or a finished room is emptied after 60 min; a connection without a hello after 5 s | "Relay limits"; `check-net-load` and the endurance run of §11.3 (working set and handles over hours) | a full server holds 16 rooms × (64 MiB recording + states in transit): about 1.2 GB at the bounds, far above what honest matches use (§11.2) |
| T10 | **Orders no player can give:** system commands (spawn, give money, kill), commands for another player, garbage behind a plausible header, coordinates that overflow | the relay stamps the player of the connection; only the 24 order types of a player pass a wire; a command that does not decode is left out by every client alike and by the replay; a point beyond 131,072 units is rejected by every simulation at submission (`docs/SIMULATION-CORE.md` §49) | "Hostile members" (garbage and system commands: every client leaves them out, the replay plays to the agreed hash), "Fuzz, commands" (1.5 million changed real orders per seed: no exception in the pre-check, the submission or the ticks after), `Simulation.Tests` "Command limits" | an order that is valid but senseless is the simulation's to reject, as from a local player. A defect in a command handler that the fuzzing did not reach would hit every client alike (no desync, but a crash of all) |
| T11 | **A member lies about its state** to end the match (a wrong hash) or to break a joiner (a wrong state) | a state is passed on only when its stated hash is the one all agreed on, and the joiner's own hash of what it built is compared; a **watcher's** wrong hash drops the watcher only | "Rejoin" (a state with another hash goes nowhere), "Watchers" (a watcher that diverged is dropped, the match goes on), "Fuzz, states" | **a player that reports a wrong hash ends the match for everybody as a desync** - the relay does not simulate and cannot tell who lies. Proposal D-24: with three or more players the majority decides and the minority is dropped (not built) |
| T12 | **Cheating by seeing more** (a changed client shows what the fog hides) | none | - | lockstep gives every client the whole state. Out of scope of the project (no anti-cheat); a spectator delay keeps *watchers* from helping a player (`spectatorDelayMs`) |
| T13 | **Entering a room one is not wanted in** | the room password as challenge and response (the password never travels); the host removes a member, and its origin may not come back to the room; the operator's ban list | "Room password and kick" (wrong and replayed answers, the kick, the origin), "Transport limits" (a banned address gets no answer and its connection is closed), `check-server-package` (a ban file with a line that is no entry stops the server at its start) | a kicked or banned person comes back from another address. The server could enter a room itself (it holds the verifier) |
| T14 | **Personal data in the logs** | addresses are written as their network (`/24`, `/48`) and without port unless `logFullAddresses`; logs are deleted after `logRetentionDays` (14); replay files hold no addresses; chat is relayed and never logged | the address form: "Transport limits" (a ban) and the server's own lines in `check-net-load` | player names appear in `joined` lines; a network of 256 addresses can still identify a household together with a time |
| T15 | **The supply chain** | no NuGet package in the server (base class library only); `scripts/audit-vulnerable.ps1` fails if the server references any package, and checks the solution for known advisories | the script; "Layer rules" (the server references `CnCNew.Net` and `CnCNew.Server` only) | the .NET runtime inside a package is as old as the SDK that built it: a package is rebuilt when a runtime update is published |

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

**Found while the suite was written** (each fixed, each with its test):

1. The server's replay file numbered a player's commands by the relay's order number. An order that no client could decode was skipped by the clients and counted
   by the file, so the file no longer played to the agreed hash (`RecordingFile`; "Hostile members").
2. A watcher's wrong hash ended the match of the players, and a watcher that never said "the match is over" kept a finished match open (`RelayServer`: only the
   players that play along decide; "Watchers", "Fuzz, relay").
3. A command of a type only the system gives (spawn, give money) passed the relay like an order (`CommandWire.IsPlausible`; "Hostile members").

What the fuzzing does not show: inputs it did not generate. It is mutation from valid samples with a fixed budget, not coverage-guided; the simulation's command
handlers are reached only through orders the generator can still make valid (1.7 % of them).

## 10. Operation (Phase 28)

### 10.1 The keys of the configuration that are new

| Key | Option | Default | Meaning |
|---|---|---|---|
| `keyFile` | `--key-file` | (none) | the server's permanent key (PEM, PKCS#8, P-256); made on the first start. Without it the key lasts as long as the process, with a `warning` line |
| `banFile` | `--ban-file` | (none) | addresses and networks that get no answer (§10.3) |
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

A text file, one entry a line: an address (`203.0.113.7`, `2001:db8::7`) or a network (`203.0.113.0/24`, `2001:db8::/48`); `#` starts a comment. The server reads it
again within five seconds of a change, closes the connections from those addresses and answers nothing from them any more (`bans-loaded` with the count; `banned`
in the status counts the datagrams). **A line that is no entry is an error:** at the start the server refuses to run, later it keeps the list it had and says so
(`ban-file-error` with the line number) - a typing mistake never unbans everybody. A file that does not exist bans nobody. The host of a room removes a member with `Kick`; that keeps the member's origin
out of that room and is no ban of the server.

### 10.4 Logs, and what is in them

Every line goes to standard output and, with `logDir`, to `server-<yyyyMMdd>.log` (UTC) with the time of day in front; files older than `logRetentionDays` are
deleted at the start and at every change of day. **An address is personal data**: by default it is written as its network - the last byte of an IPv4 address and the
last 80 bits of an IPv6 address removed, `/24` or `/48` behind it - and without its port. `logFullAddresses` writes them in full; decide that with the law of the
place the server runs in, and keep the retention as short as the purpose allows. What else a log holds about a person: the name a player gave itself (`joined`),
and when it played. Chat is relayed and never written. Replay files hold orders and factions, no names and no addresses.

New events besides §6: `warning`, `bans-loaded`, `ban-file-error`, `room-locked` (a room has a password), `bad-password`, `kicked`, `flood`, `hash-stalled`,
`load-timeout`, `no-hello`, `room-idle`, `recording-limit`, `watcher-desync`, `replays-expired`. The `status` line also carries `rateLimited`, `banned`, `forged`,
`replayed`, `workingSetKb`, `cpuMs`, `bytesIn`, `bytesOut`, `datagramsIn`, `datagramsOut`, `handles` (open descriptors under Linux), `threads`, `roomsOpened`,
`roomsClosed`, `connectsAccepted`; `room-closed` carries the room's counters of §9.2.

### 10.5 Stopping

Ctrl+C and SIGTERM stop the loop: every connection is told at once that it ends (the clients do not wait for a timeout), then `stopped`. The recording of a match
that was still running is not written - stop when `status` shows `rooms=0` if the replays matter. A restart with the same `keyFile` is the same server to its
clients; the rooms are gone (a match does not survive its server, and held seats are held in memory).

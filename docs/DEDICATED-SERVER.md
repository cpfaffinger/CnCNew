# The dedicated server

Phase 26 (11 October 2026), extended by Phase 27 (12 October 2026: held seats, watchers on a delay and late ones, the replay file, the rules of the pace - §2, §3, §6,
§8). `tools/CnCNew.DedicatedServer`: the relay of `docs/NETWORK-PROTOCOL.md` as a program of its own - **one process, one UDP port, several rooms**.
It relays and nothing else: it loads no game data, simulates nothing and does not decode a command. It references `CnCNew.Net` and `CnCNew.Server` only (through them the
simulation's command codec and match description); a test of `Net.Tests` enforces that it reaches neither `CnCNew.Content` nor `CnCNew.Game`, `CnCNew.Presentation` or a
renderer, and that its source names none of them.

**Not yet:** encryption, authentication, bans, rate limits beyond the connection and room limits, a listing of rooms, operation as a service
(`docs/MULTIPLAYER-PLAN.md`, Phases 28 and 29). The port of this version is for a LAN or a test, not for the open internet. **Linux as a host** is not set up as a
product (no package, no service unit), but the server built under Windows runs unchanged under Linux with the .NET runtime, and a check plays matches against it that
way (`scripts/check-net-cross-os.ps1`, `docs/DETERMINISM.md` §2d).

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

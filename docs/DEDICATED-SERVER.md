# The dedicated server

Phase 26 (11 October 2026). `tools/CnCNew.DedicatedServer`: the relay of `docs/NETWORK-PROTOCOL.md` as a program of its own - **one process, one UDP port, several rooms**.
It relays and nothing else: it loads no game data, simulates nothing and does not decode a command. It references `CnCNew.Net` and `CnCNew.Server` only (through them the
simulation's command codec and match description); a test of `Net.Tests` enforces that it reaches neither `CnCNew.Content` nor `CnCNew.Game`, `CnCNew.Presentation` or a
renderer, and that its source names none of them.

**Not yet:** encryption, authentication, bans, rate limits beyond the connection and room limits, a listing of rooms, operation as a service, Linux as a host
(`docs/MULTIPLAYER-PLAN.md`, Phases 27 to 29). The port of this version is for a LAN or a test, not for the open internet.

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
- Two clients that report different session hashes for a tick end the match as a desync; the server tells every reported hash.

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
| `listening` | the port is bound | address, port, protocol and transport version, build, the limits and the rule's numbers, whether the fine timer is held |
| `connected` / `disconnected` | a client passed the cookie handshake / its connection is forgotten | address; at the end the reason and the connection's counters (datagrams, bytes, repetitions, round trip) |
| `turned-away` | a connection never reached a room | reason: `no-hello`, `not-a-hello`, `server-full` |
| `room-opened` / `room-closed` | first hello of a name / last connection gone | at the end: phase, ticks, last agreed tick, intents, discarded messages, slowed time, members dropped for lag, desync, tick intervals |
| `joined` / `rejected` / `left` | a member of a room | connection, name, build / reason / player, reason, phase, tick |
| `match-started` / `match-running` / `match-ended` | the host started / all have loaded / the end | map, tick rate, participants, members, pacing / tick, winner, reason, last agreed tick |
| `behind` / `caught-up` | a member crosses the report or the slowing limit | connection, player, lag, whether it slows the game |
| `throttled` / `full-speed` | the game is slowed / runs at the set speed again | tick, speed |
| `desync` | two session hashes of one tick differ | tick, every reported hash |
| `status` / `room-status` | every `statusSeconds` | rooms, connections, handshake counters, memory / phase, members, tick, last agreed tick, speed, tick intervals |
| `stopped` | the loop ends | rooms opened and closed |

## 7. Trying it on one machine

`tools/CnCNew.NetTool` plays whole matches against the server with client processes without a window, through a UDP proxy that delays, loses, duplicates and reorders
(`docs/NETWORK-PROTOCOL.md` §15 and §16):

```powershell
pwsh scripts/check-net-match.ps1
dotnet run -c Release --project tools/CnCNew.NetTool -- net-match game/content --map "Maps/Twilight Flame/Twilight Flame.map" --humans 6 --ais 2 --watchers 2 --minutes 5 --delay 40 --jitter 10 --loss-percent 1 --out <directory outside the repository>
```

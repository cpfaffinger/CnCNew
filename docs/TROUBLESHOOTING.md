# Troubleshooting

For people who play or run a server, and for whoever helps them. What the programs leave behind when something goes wrong, where it is, what it holds - and
what to do in the common cases. Installation is in [INSTALLATION.md](INSTALLATION.md), the server in [DEDICATED-SERVER.md](DEDICATED-SERVER.md), which version
works with which in [COMPATIBILITY.md](COMPATIBILITY.md).

**Nothing leaves your machine by itself.** No program of CnCNew sends a report, a log or a statistic anywhere. Everything below is written to your disk; a support
bundle is made only when you ask for it, written where you say, and you decide whether to hand it on. None of it contains a file of the game.

## 1. Where things are

| What | Client (the game) | Dedicated server |
| --- | --- | --- |
| Log of a run | `%LOCALAPPDATA%\CnCNew\logs\client-<UTC time>-<process>.log` (the last 20 are kept; `--no-log` writes none) | standard output, and with `--log-dir DIR` one file per day `DIR/server-<date>.log` (kept `logRetentionDays`, 14) |
| Crash report | `%LOCALAPPDATA%\CnCNew\crashes\client-<UTC time>-<process>.txt` | `DIR/crash-<UTC time>-<process>.txt` in the log directory (or the working directory without one); exit code 70 |
| Desync package | `%LOCALAPPDATA%\CnCNew\diagnostics\` ([NETWORK-DIAGNOSTICS.md](NETWORK-DIAGNOSTICS.md) §4) | - (the server keeps the log lines `desync`, `desync-parts`) |
| Replay of a match | - | with `--replay-dir DIR`: `DIR/<room>-<n>.replay` and a note ([DEDICATED-SERVER.md](DEDICATED-SERVER.md) §8) |
| Settings, saved games | `%APPDATA%\CnCNew\` | the configuration file you started it with |
| Keys | `%LOCALAPPDATA%\CnCNew\player-key.pem`, `known-servers.json` | the file of `keyFile` |

`--net-dir DIR` moves everything of the first column but the settings to `DIR` (`--settings-dir DIR` moves the settings); the checks of the repository always do,
so a test never writes into your own directory.

A **log line** of the client is `ts=<milliseconds since start> stream=out|err text=<what the program wrote>`; a log line of the server is
`ts=<milliseconds> event=<name> key=value ...` ([DEDICATED-SERVER.md](DEDICATED-SERVER.md) §6). Addresses in the server's log are written as their network (`/24`,
`/48`) unless the operator asks for them in full - an address is personal data.

## 2. A crash

The client says `crash report: <file>` on its standard error and leaves the report; the server writes `event=crash type=... report=<file>` and ends with exit code
70. A report holds: the time, the operating system and the .NET runtime, the build (commit, and `+modified` for a build of a changed tree), the rules and the tick
rate, the content directory and the map, the arguments, the exception with its stack, and (client) the last 200 lines of the log; the server's report holds its
configuration instead (it has no secrets: the key and the ban list are files the configuration only names). It holds no file of the game and no state of a
world.

What to do: start again; if it happens again in the same place, make a support bundle (§3) and hand it to whoever helps you.

`scripts/check-diagnostics.ps1` proves on every gate run that both programs leave a report and a log, and what a bundle holds and does not hold.

## 3. The support bundle

```
CnCNew.Client.Stride --support-bundle C:\Temp\cncnew-support.zip [--content-dir <your content>]
CnCNew.DedicatedServer --support-bundle /tmp/server-support.zip --config server.json
```

The client's bundle holds `logs/`, `crashes/` and `diagnostics/` (desync packages) of its directory, `install.json` and the size and SHA-256 of the content
manifest when you name the content, `ABOUT.txt` (system, build, rules) and `CONTENTS.txt` (every file it holds). It never holds a key (`*.pem`), a saved game, a
file of the game or the content itself. The server's bundle holds its log files and crash reports, the configuration and the same two descriptions - never the
key file, the ban file (addresses) or replays. Open the ZIP and read `CONTENTS.txt` before you hand it on.

## 4. "My match has diverged" (desync)

In a match on a server every client reports the hash of its state after every tick (or every few ticks, `hashInterval`); the server compares them. When one
differs, the match stops for everybody with a message that a desync happened - it is never played on in two different states.

1. **Each player's client wrote a desync package** into its `diagnostics` directory: the parts of the state that differed, the replay of the match up to there, the
   state, the chain of hashes and the objects of the last two seconds. Ask one player whose client stood alone (the server's log line `desync-parts` names the
   connection) and one other for their packages - or for a support bundle each.
2. **Both must use the build the match was played with** (`info.json` of the package names it). The usual cause is not a defect but two different things playing
   together: another build, other game data, another rule profile. The server refuses those at the door (§5); a modified build (`allowModifiedBuilds`) is the
   exception, because two modified builds of one commit are not known to be the same code.
3. Whoever investigates runs `NetTool desync-diff <content> <package A> <package B>`: the first differing tick, the parts, the objects and - by playing each package's
   own recording again - the fields. The procedure in full is [NETWORK-DIAGNOSTICS.md](NETWORK-DIAGNOSTICS.md) §8.
4. The operator of the server has the log lines and, with `--replay-dir`, the replay file of the match: `NetTool replay-check <content> <file>` plays it to the
   last tick everybody agreed on.

A desync of the same match on the same build and data is a defect of this program; please report it with both packages.

## 5. The server refuses me

| Message | Meaning | What to do |
| --- | --- | --- |
| no connection to the server (after 5 s) | nothing answered: wrong address or port, a firewall, or another transport version (a transport mismatch cannot even be told) | check address, port and firewall; compare versions with the operator |
| `ProtocolVersion` | client and server speak different protocol versions | use a client of the server's version ([COMPATIBILITY.md](COMPATIBILITY.md)) |
| `SimulationVersion`, `ModifiedBuild`, `FormatVersion` | your build is not the room's | every player of a room uses the same build |
| `Definitions` | your game data or rule profile differs from the room's: another content (for example the patch layer), or `--rules` | use the same content and the same `--rules` as the host |
| a question about the server's key, or "the server's key changed" | you meet this server for the first time, or its key is not the one you remembered | accept the first time if you trust the address; a changed key on a known server is a reason to ask its operator before you go on |
| `BadPassword`, `Banned`, `Kicked`, `SeatNotHeld`, `CannotCatchUp` | as named | see [DEDICATED-SERVER.md](DEDICATED-SERVER.md) §10 |

## 6. Other common cases

- **The installation is refused** ("not the master"): the patcher accepts exactly one version of the game, the fresh English Steam installation; the report names
  every file that differs. See [INSTALLATION.md](INSTALLATION.md), "What the master check reports and what to do".
- **A saved game or a replay does not load**: it was written by another version, at another tick rate or with other game data or rules; the message says which.
  Saves and replays are bound to their version ([COMPATIBILITY.md](COMPATIBILITY.md)).
- **The game is slow**: the client's `--net-stats` shows what a click costs and how evenly the picture moves in a match of a server; `SimTool perf` and
  `scripts/perf-profile.ps1` measure the simulation ([PERFORMANCE.md](PERFORMANCE.md)). Measurements on a busy machine say little.
- **The window stays black or the program ends at once**: the client needs .NET 10 and Direct3D 11; the log of the run says how far it came.

## 7. Profiler switches

| Switch | What it measures |
| --- | --- |
| client `--net-stats [--net-stats-trace]` | in a match of a server: from a click to the first frame that shows it, to its execution, and the evenness of the frames ([MULTIPLAYER-FINDINGS.md](MULTIPLAYER-FINDINGS.md) section 2) |
| `SimTool perf ... --json FILE` | the cost of the simulation per tick, by phase, with allocations ([PERFORMANCE.md](PERFORMANCE.md)) |
| `SimTool perf-mission`, `path-bench`, `PresentationTool sync-perf`, `PresentationTool smoothness` | missions, pathfinding, the game layer, the drawn motion |
| `NetTool pacing`, `NetTool seal-cost`, `NetTool load` | the server's timer, the cost of the sealed transport, a server under load ([DEDICATED-SERVER.md](DEDICATED-SERVER.md) §11) |

## 8. Limits

- The client's crash handler sees exceptions of managed code. A failure inside native code (the graphics driver, the audio device) can end the process without a
  report; the log of the run then shows how far it came.
- A report and a bundle are text and logs as the program wrote them; they can hold file paths of your machine (your user name in a path). Read `CONTENTS.txt`
  and the files before you hand a bundle on.

# Network diagnostics: a desync, named

State: Phase 27 (12 October 2026). Protocol version 3 (`docs/NETWORK-PROTOCOL.md` §18.7). Code: `CnCNew.Simulation` (`SimulationWorld.Diagnostics.cs`,
`SkirmishMatchSession.StateHashParts`), `CnCNew.Game.Lockstep` (`SessionDiagnostics`, `DesyncPackageDiff`), `CnCNew.Server` (`RelayServer.TakeDetail`),
`tools/CnCNew.NetTool` (`desync-diff`, `replay-check`, `snapshot-cost`).

A lockstep match is the same on every machine because every machine computes it; when two machines compute something different, the match is over, and the only useful
question is **which object, on which machine, from which tick on**. Before this phase the server could say "the hashes of tick 51,203 differ" and nothing more. This
document is what exists now: the parts of the hash, what a client remembers, the package it writes, the tool that reads two packages, and what to do with a report.

**Since Phase 28** (`docs/NETWORK-PROTOCOL.md` §20.6): a desync of the match is what the *players* disagree about. A watcher whose hash differs from what the players
agreed on is told so with the same `Desync` message - it writes its package like anybody - and is then dropped (`watcher-desync` in the server's log,
`WatcherDesyncs`), and the match goes on; the procedure below applies to its package and one of a player. Everything here travels inside the sealed connection.

## 1. The parts of the session hash

The session hash (`docs/SIMULATION-CORE.md` §45.1) is one number over the world, every computer player and the session's counters. The world already keeps three sums
separately, because it updates its hash incrementally (§37 there). `SkirmishMatchSession.StateHashParts()` returns them by name:

| Part | What it covers |
|---|---|
| `world.objects` | the sum of the hashes of all objects - every object's fields and the state of each of its modules |
| `world.timers` | the sum over all timers |
| `world.commands` | the sum over the commands that are submitted and not yet executed |
| `world.global` | everything else of the world: tick, the identifier counters (objects, events, timers), the logic random streams, per player its money, power, sciences, relations and last sequence number, the vision hash |
| `ai.player<N>` | the whole state of the computer player of player N (`SkirmishAi.State.cs`), one part per computer player |
| `session.counters` | the victory tick, the winner, the end tick, the sequence counters of the system and of every human player |

Reading the parts changes nothing, and the state hash is composed exactly as before: no reference value moved. Two sessions whose hash differs differ in at least one
part. The parts are a first direction, not a location: a single changed hit point shows in `world.objects` **and** in `world.global`, because the world counts the
event that changed it. The script state of the brief has no part of its own - a skirmish of the built-in AI runs no scripts; a `ScriptedSkirmishSession` (the original
AI) does not go through the relay with more than one client yet.

`SimulationWorld.CopyObjectHashes` returns the hash every object contributes, in id order.

## 2. What a client remembers

`SessionDiagnostics` is a ring a client fills after every tick: the session hash and its parts for the last **1,024** ticks (17 s at 60 Hz), and the hash of every
object for the last **128** ticks (2 s). Nothing of it is sent while the match is in step. Its cost per tick is below what the measurement can tell apart (`NetTool snapshot-cost`, eight
players, minute 30: 441 µs for a tick with its session hash, 434 µs with the record on top - the difference is the scatter): the parts need the rest of the world
hashed once more, the object table is a copy of numbers the world already has.

Why not in every hash report, as the brief proposed: a report is 14 bytes and goes out three times (§12.6 of the protocol); with six to thirteen named parts it would be
five to ten times that, every tick, for every client, to answer a question that is asked once in a broken match. The ring answers it as well as long as the desync is
noticed within 1,024 ticks, which it is at any hash interval a server would use (§6).

## 3. What happens at a desync

1. The relay finds two different hash reports for a tick, ends the match and sends `Desync` (tick, every reported hash) to everybody - as since Phase 25. The server
   logs `desync tick=... reports=<connection>:<hash> ...`.
2. Every client answers with `DesyncDetail`: the parts of its hash at that tick, from its ring (none, if the tick is older than the ring).
3. The relay compares the parts by name and sends `DesyncReport` to everybody: every part in which the clients that answered do not all have one value, with each
   connection's value. The server logs `desync-parts tick=... parts="world.global 1:… 2:… 3:…; world.objects 1:… 2:… 3:…"` (again when a further detail arrives). With
   three or more clients this already says **which client** stands alone: here connection 3.
4. Every client that was given a directory for it (`HeadlessClient.DiagnosticsDirectory`, `LiveMatch.DiagnosticsDirectory`; `--diagnostics DIR` of `NetTool client` and
   of the window client) writes its **package**.

The relay still simulates nothing: it compares numbers the clients sent.

## 4. The package

A directory with five files (format `cncnew-desync-package`, version 1). None of it is game data: it is the state of this one match, and it is only as large as the
match's state.

| File | Content |
|---|---|
| `info.json` | format and version, the connection and player of this client, the tick of the desync, the tick the state stands at, the build identity, the hash reports the server sent |
| `replay.bin` | the match so far in the simulation's replay format: the match description in the header and every command the session has taken (the players' from the bundles, and what the computer players decided) |
| `state.bin` | the session state as the client stands (the format of a skirmish savegame's state, `docs/SIMULATION-CORE.md` §45.3) - usually a few ticks after the desync, because the client had played on when the report arrived |
| `hashes.tsv` | per tick of the ring: tick, session hash, every part as `name=value` |
| `objects.tsv` | per tick of the object ring: tick, object id, object hash |

A package of an eight-player match at minute 30 is about 4 MB, almost all of it `state.bin` (4.1 MB of JSON; packed it would be 230 KB, §7).

## 5. The tool: `NetTool desync-diff <content> <package A> <package B>`

It needs the game content, because it plays the match again. From the two packages it reports

1. **the first differing tick** - the first tick both hash chains hold whose hashes differ. This can be earlier than the tick the server reported when the server
   compares hashes at an interval;
2. **the parts** that differ at that tick;
3. **the objects** whose hashes differ at that tick, or that only one side has (when both object rings still hold the tick);
4. **the fields**: for each package it plays the package's own `replay.bin` to the tick `state.bin` stands at and compares the two states field by field (JSON paths;
   lists of things with an id are matched by id). Since Phase 36 the comparison is the one `SimTool rate-compare --fields` uses as well (`CnCNew.Game.Diagnostics.StateFields`,
   `docs/MATCH-ANALYSIS.md` section 5): the differences are listed in ordinal order of their paths, a list element only one side has as its paths with "(missing)". The client that is in step shows no difference: its state *is* the match played again. The client that diverged shows
   what diverged - by then possibly more than the first field, because a difference spreads.

Step 4 is the one that names the field, and it works for one package alone (`DesyncPackageDiff.FieldsAgainstReplay`): a single player's package against the match
played again from its own recording says whether that player's machine computed something else than the same build computes now.

The built-in error of the brief (`HeadlessClient.CorruptAtTick`, `ProbeDisturbanceKind.Corrupt`, `NetTool client --corrupt-at-tick T`, `net-match --disturb
seat:corrupt:T --expect-desync`): before tick T one client deals one point of damage to its own copy of the lowest-numbered living object. With real processes over the
proxy (`scripts/check-net-robustness.ps1`, 12 October 2026):

```
changed: object 1 (AmericaCommandCenter) at tick 900; desync reported at tick 900;
  parts: world.global 1:B92A… 2:B92A… 3:60C7…; world.objects 1:B747… 2:B747… 3:EE83…
First differing tick: 900
Parts of the session hash that differ: world.global, world.objects
Objects that differ at that tick: 1
Package A against its own recording played again, at tick 905: no difference - this client is in step
Package B against its own recording played again, at tick 906: 5 difference(s)
  stateHash: … -> …
  world.stateHash: … -> …
  world.state.nextEvent: 50 -> 51
  world.state.entityList[id=1].modules[0].state.health: 83886080000 -> 83869302784
  world.state.entityList[id=1].modules[6].state.nextWake: 1073741823 -> 1080
```

Tick, object and field are exact: `health` of object 1 is one point (2^24 in Q39.24) lower, the damage woke a module of the object, and the world counted one event
more. The test `Desync diagnosis` of `Net.Tests` asserts the same on the synthetic fixture.

## 6. How often to report a hash

The server asks for a hash every `hashInterval` ticks (`docs/DEDICATED-SERVER.md` §2; 1 by default). Rarer reports save upstream bandwidth and notice a desync
later. Measured with real processes (`NetTool net-match`, three players, five computer players, Twilight Flame, 60 Hz, through the proxy with 40 ms delay, 10 ms jitter,
1 % loss; build `8f6f94f`; for each interval a steady match of 60 s and one in which client 2 changes an object before tick 901):

| Hash interval | Upstream per client | of that the hash reports | Order to execution, median / 99 % | Desync reported at tick | later than the change | `desync-diff` finds |
|---|---|---|---|---|---|---|
| 1 | 9,860 B/s | about 7,900 B/s | 111 / 133 ms | 901 | 0 ticks | tick 901, object 1, `health` |
| 4 | 3,825 B/s | about 1,850 B/s | 116 / 136 ms | 903 | 2 ticks (33 ms) | tick 901, object 1, `health` |
| 15 | 2,360 B/s | about 390 B/s | 116 / 132 ms | 914 | 13 ticks (0.2 s) | tick 901, object 1, `health` |
| 60 | 1,972 B/s | under 100 B/s | 115 / 132 ms | 959 | 58 ticks (1 s) | tick 901, object 1, `health` |

(The hash reports' share is the difference to the interval of 60; downstream is 8.7 to 8.8 KB/s at every interval. Phase 26 had estimated the reports at 6 KB/s.)

**What follows.** The reports are four fifths of what a client sends. An interval of 4 takes 6 KB/s off every client's upstream and notices a desync at most three
ticks later; the diagnosis is the same at every interval, because the client's own ring holds the hash of every tick whatever the server asks for - `desync-diff`
names the tick of the change, not the tick of the report. The cost of a late report is only that the match runs on, already broken, for that long. Beyond the object
ring (128 ticks) the objects of the first differing tick are no longer in the package; the fields still are (§5). **Proposal: 4.** The default stays 1 until the
owner says otherwise: it is the server's configuration, and every check of the repository runs at 1.

A rejoin needs a state at a tick whose hash was reported (the relay passes a state on by the agreed hash of the tick before it), so with an interval above one a
member hands its state over at the next reported tick - up to `hashInterval` - 1 ticks later; the test of the rejoin runs at 1 and at 4.

## 7. What a state costs to hand over

The rejoin (`docs/NETWORK-PROTOCOL.md` §18.2) hands the session state of one member to another. The brief asked to measure before deciding on a format, against the
proposal "packed at most 2 MB, written and packed within 150 ms, read within 1 s". `NetTool snapshot-cost`, Twilight Flame, eight players, 60 Hz, Release, every value
the median of three (12 October 2026, build `8f6f94f`; the first seat stands idle, seven computer players play):

| Game minute | Objects | State | Packed | Giver: write + pack | Joiner: unpack + build | Built state hashes to the session |
|---|---|---|---|---|---|---|
| 5 | 154 | 3,314 KB | 82 KB | 31 + 3 ms | 9 + 67 ms | yes |
| 10 | 185 | 3,621 KB | 129 KB | 27 + 3 ms | 10 + 53 ms | yes |
| 15 | 188 | 3,865 KB | 168 KB | 19 + 4 ms | 10 + 104 ms | yes |
| 20 | 151 | 3,895 KB | 184 KB | 15 + 4 ms | 9 + 32 ms | yes |
| 25 | 132 | 4,016 KB | 206 KB | 20 + 5 ms | 11 + 42 ms | yes |
| 30 | 109 | 4,080 KB | 228 KB | 19 + 6 ms | 9 + 32 ms | yes |

In the endurance match of real processes (six players who give orders, `docs/NETWORK-PROTOCOL.md` §19.3) the state grew faster, because the recording of 72,669
orders is part of it: 198 KB packed at minute 10, 469 KB at minute 30, **775 KB packed (7.9 MB of JSON) at minute 55**, written and packed in 80 ms.

**Decision: the format stays.** The state is the JSON document of a skirmish savegame (session state version 2), packed with Brotli from the BCL
(`SessionTransfer.Pack`, quality "fastest"; four magic bytes `CNZ1` in front, and a state without them is taken as it is). The largest state measured - an hour of
a match of six people - is 2.6 times under the size limit and takes half the time allowed to write; reading stays 9 times under its limit; a binary format would add a second encoding of every field of the simulation, with its own
version and its own test, for nothing that was measured as a problem. The raw state grows with the match because the recording so far is part of it, as in a savegame.
The giver stops for the time to write and pack - 20 to 80 ms, one to five ticks at 60 Hz, once per join - which is below what the playback of the others takes for a
hitch (§13.2 of the protocol). `Unpack` refuses what unpacks to more than four times the largest state the protocol carries, and reports damaged input as invalid data.

That a restored session continues exactly like the one it was taken from is the subject of `SimTool session-check` (19 runs, 77 saves, tier 4, since Phase 24); the six
states of the table above were each built again and hashed to the session they came from.

## 8. Procedure for a desync report

1. Take the server's log lines `desync` and `desync-parts` of the room: the tick, and which connection stands alone in which part.
2. Get the package of that client and of one other. (Players' machines write it where the client was told to; a server operator has only the log and the replay file.)
3. `NetTool desync-diff <content> <A> <B>` with the content and the build the match was played with (`info.json` names the build).
4. Read the last block first. A package that differs from its own recording played again: that machine computed something else than this one does now - the fields say
   where; the first differing tick and the object ring say when. **Both** packages in step with their own recordings, and still different hashes: the two clients did
   not play the same commands - compare the two `replay.bin` (the bundles differed, which is a defect of the relay or the transport, not of the simulation).
5. Play the server's replay file (`NetTool replay-check`, `docs/DEDICATED-SERVER.md` §8): it stops where the server stopped; the hash it reaches at the last agreed tick
   must be the agreed one.

## 9. Limits

- **Only what a skirmish session describes.** A field that is not part of the session state is not in a package, and a difference in it is invisible until it changes
  one that is. That is the same completeness the savegame and the rejoin rest on.
- **The object ring is two seconds.** A desync that is noticed later than 128 ticks after it began (a hash interval above 128) still gets its tick from the hash ring,
  but not its objects; the field comparison of step 4 still works.
- **A package is written by the client it is about.** A client that lies writes a lying package. The server's file and the other clients' packages do not depend on it.
- **Not verified:** no desync between two different machines has ever occurred or been diagnosed; every desync here was built in. The packages of the window client are
  written by the same code but no check breaks a match of the window client on purpose.

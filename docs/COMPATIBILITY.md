# Versions and compatibility

What a build of CnCNew accepts from another one - saved games, replays, a server, a match - and the rule proposed for the first release. The rule itself is a
decision of the project owner (D-31 in `docs/plan/PROGRAMME.md` §7); this document states what the code does today and what the proposal would mean.

## 1. The versions (state of Phase 30)

| Thing | Version | Where it is checked | An older or newer one |
| --- | ---: | --- | --- |
| Command table (`CommandTypes.Version`) | 17 | replay, match description, save | refused with both numbers named |
| World snapshot (`WorldSnapshot.Version`) | 10 | every save and state that embeds a world | refused |
| Replay (`ReplayFormat`) | 15 | `ReplayFormat.Read` | refused |
| Mission snapshot | 3 | campaign saves | refused |
| Campaign savegame (`SaveGame`) | 3 | load list of the main menu | refused, with the reason in the list |
| Skirmish savegame (`SkirmishSaveGame`) | 1 | load list | refused |
| Skirmish session state | 2 | save, rejoin, a watcher's state | refused |
| Match description encoding | 2 | every match that crosses a process | refused |
| Desync package | 1 | `NetTool desync-diff` | refused |
| Network protocol | 5 | the room, on the hello | `Reject ProtocolVersion` naming both |
| Network transport | 2 | every datagram | dropped; the client reports no connection |
| Runtime content manifest | schema 2 | `ContentFileSystem.Open` | schema 1 is read with a warning; anything else refused |
| Install record (`install.json`) | 1 | the patcher | a directory with another record is not treated as an installation |

Besides the versions, four identities bind a save, a replay and a match to what made it:

- **the tick rate** (30, 60 or 120 Hz; a save of one rate does not load at another),
- **the definition fingerprint** - the compiled game data, the model table, the tick rate and the rule profile folded together (`GameData.Fingerprint`): another
  content (the patch layer, for one) or another `--rules` is another fingerprint,
- **the terrain fingerprint** of the map,
- **the build identity** (commit and "modified" flag) of a match opened through a server (`MatchDescription.SimulationId`) - two builds with the same versions but
  other code would diverge, so a room admits only its host's build.

## 2. What that means today

Nothing is compatible across a version change: a save, a replay or a state of another version is refused with a message that names the version, never read
half and never guessed at. A save also needs the same rate, game data and rules; a match needs the same build. Within one build everything is exact: a replay
plays to the same hash, a save continues to the same hash (the reference manifest and `SimTool session-check` prove it on every full run).

Over the phases the versions moved often (the replay format from 1 to 15 since Phase 4). That was intended while there were no players.

## 3. The proposal for the first release (decision D-31 of the owner)

From the first release on, players keep saves and replays and play with each other across machines, so a version change has a cost for them. Proposed:

1. **A change of tick rate, fixed-point format, world snapshot, replay format, command table, match description or network protocol/transport is a break**, made
   only with a decision of its own (as the brief of Phase 30 requires) and announced with the release.
2. **A release says which versions it reads.** It reads its own versions; it may read older saves and replays by a converter of its own when one is written, and
   says so per format - there is no general promise.
3. **Replays are bound to their build**: a replay plays with the build that recorded it (the header states it). Keeping old builds available is the way to
   watch old replays, not reading them with new code.
4. **The server and the client are versioned by protocol and transport**: a server speaks one protocol version; an operator who changes it runs the old and the
   new server side by side for the time of the change ([DEDICATED-SERVER.md](DEDICATED-SERVER.md) §12).
5. **Rule profiles are content, not versions**: a new adopted fix changes the profile's fingerprint, not a version; the original profile never changes by it.

What the owner decides: whether (1) applies from the first release on or later; whether old saves are converted (2); how long old builds are kept (3).

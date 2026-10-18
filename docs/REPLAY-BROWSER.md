# The replay browser: recording, list, playback

Stage 3, milestone S3-M3 (`docs/plan/STAGE-3-CHANGES.md`), Phase 35 (brief `docs/handoffs/HANDOFF-PHASE35.md`). The client records every skirmish it plays - locally or
through a server - as a replay file beside the savegames, lists the files in the shell with the state of each against this game, and plays a file back with pause, speed,
seeking to checkpoints and the view of one player or of everything. **Done by machine, on one machine; no human has seen the list or watched a playback.**

**Presentation and game layer only.** No line of the simulation changed: no format, no version and no reference value moved, and both manifests are untouched
(`CommandTypes.Version` 17, replay 15, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description encoding 3, session state 3, protocol 6, transport 2). The replay
file of the game layer wraps the simulation's replay of format 15 as it is.

Layers: `src/CnCNew.Game/Replays/` (file, store, compatibility, recorder, playback, words), `src/CnCNew.Game/Shell/Screens/ReplayScreens.cs` (the list and the rename popup),
`LiveMatch` (a playback mode), `SpectatorController`/`SpectatorHud` (the seek keys and the replay line), `src/CnCNew.Client.Stride/ClientRuntime.cs` (recording at the end of a
match, playing from the shell, rebuilding the scene at a seek).

## 1. The file (`MatchRecordingFormat`, `.cncreplay`, version 1)

| Part | Content |
| --- | --- |
| `CNCREPLY`, u32 version | the file of the game layer; another version is refused as "other format" |
| u32 length, header | UTF-8 JSON with a fixed property order (`MatchRecordingInfo`): what the list shows and what the file is judged by - without playing it |
| u32 length, the match | the simulation's replay (`ReplayFormat` 15): the match description in its header, every command of the session (`SkirmishMatchSession.ToReplay`) |
| SHA-256 | over everything before it; a changed byte or a cut end is "damaged" |

The header: when it was recorded (UTC; the only wall-clock value, read by nothing but the list), local or on a server, map, tick rate, rule profile, definition and terrain
fingerprints, the build the match description states, the four format versions, the player this game played, the players (faction, the name a person gave in a room, team,
colour, human or computer with its AI as the tools spell it), **how the match ended** (section 2), the winner, the end tick, the tick the recording began at, **the session hash
every five seconds of game time** and at the end, the final world hash, and the score screen's counters per player. The header is a summary; what a playback trusts is the
simulation's replay inside, and a header that does not describe its match (another end tick, map, rate, definitions or build than the replay inside) is refused as damaged.

A file of a 70-second match of two players is 4.6 KB; the size grows with the commands (the session hash list adds 26 bytes per five seconds). Measured sizes of long
matches are in section 7.

## 2. Recording (`MatchRecorder`)

Every match of a `LiveMatch` with the simulation's own AI is recorded: a local skirmish, a match of a server joined through the menus, a spectator's match of computer players,
and a match continued from a savegame (from the tick it was saved at). The recorder reads the match and writes nothing into it: it keeps the score screen's counters
(`MatchStatistics`) and the session hash every `HashIntervalSeconds` (5) of game time, and when the match ends it writes the file into the store under a new name - the date and
the map, `2026-10-16 1423 Alpine Assault`, with `(2)` when that is taken. The client writes it when it ends the match (`ClientRuntime.FinishRunning`) and, for a match still
running when the program closes, at the close (marked "abandoned").

**How it ended** (`MatchRecorder.EndingOf`, in this order): a desync the server reported (the replay is this game's own match up to there - it plays to the hash this game
had, not to the others'); the player left by choice; the server ended the match for another reason than its rules (a limit); the rules or the server's normal end
("completed": a winner or the time limit); the connection closed; otherwise the game was closed while the match ran. Every ending has its own words in the list
(`ReplayText.EndingLine`: "China won", "No winner (time limit)", "Out of sync at 5:12", "Connection lost at 3:40", ...).

**Where:** the store is the directory `Replays` beside `Saves` under the menus' settings directory (`--settings-dir`, by default `%APPDATA%\CnCNew`). A match started without the
menus (`--match`, `--spectate`, `--join`) records only with `--replay-dir DIR`.

**A match of a server** is recorded from the bundles this client played: its session holds every command it submitted, the people's from the bundles and what the computer
players decided. Both games of a match record the same commands and play to the same end (`NetShellTests`, section 6), and the server's file of the same match - what the relay
sent, `RecordingFile.ToReplay` - plays to the same final session and world hash as the client's replay (test "The server's file of the same match").

**Not recorded:** a match of the original AI (`ScriptedSkirmishSession` - its truth is its state, there is no playback of it), campaign missions (not replays: script actions are
direct world changes, `docs/CAMPAIGN-AND-SAVEGAMES.md`), staged scenes, and a playback itself.

## 3. The list (`ReplayMenuScreen` on `ReplayMenu.wnd`)

Main menu > Load/Replay > **Replay** (the button of the original's submenu, which had no handler here; `MainMenu.cpp`, `buttonReplayID`: the submenu plays
`MainMenuLoadReplayMenuBackTransition` backwards and the shell pushes `Menus/ReplayMenu.wnd`). The screen follows `ReplayMenu.cpp` (`PopulateReplayFileListbox`,
`ReplayMenuSystem`, `ReplayMenuInput`; rewritten):

- **The list** `ListboxReplayFiles` with the layout's four columns: name, date, **state**, map. The original's third column was the version of the game that recorded the file;
  here it is the state of the file against this game (section 4) - "Playable", "Damaged", "Other build", "30 Hz", "Other rules", "Map missing", ... A file this game cannot
  play is in the original's grey (128, 128, 128), the others white. The newest recording first (by the date in its header, then by name); files that cannot be read last, by
  name. The first row is chosen when the list opens, as in the original.
- **The details** of the chosen file - name, date, local or on a server, map, length, how it ended, the players (who was a computer, who played here), what the server said,
  and **why it does not play** - stand beside the list in a window of this project's own (`ReplayMenuScreen.DetailsLayout`, a static text in the free part of the button column
  between the third button and "Back", in the layout's 800 x 600 coordinates). The original showed none of it (its "extra" column with length and players is commented out).
- **Load Replay** (or a double click) plays the chosen file; a file that does not play is refused over the list with its reason (`MessageBox.wnd`, "Cannot Play Replay") and
  nothing starts. Nothing chosen: the original's message (`GUI:NoFileSelected`, `GUI:PleaseSelectAFile`).
- **Delete Replay** asks the original's question (`GUI:DeleteFile`, `GUI:AreYouSureDelete`) and deletes the file.
- The third button - the original **copied** a file to the desktop - **renames** it here (label "Rename"): `ReplayRenameScreen` on `PopupReplay.wnd`, the original's popup that
  named the last replay after a match. Its list shows the replays that exist, a click on one copies its name into the field (as in `PopupReplay.cpp`), Enter or the button takes
  the name. A name that is empty, longer than 64 characters, begins or ends with a blank or a dot, or holds `/ \ : * ? " < > |` is refused with the reason, and so is a name
  another file has. Escape and "Back" close the popup.
- **Back** and Escape lead to the main menu.

## 4. Compatibility (`ReplayCompatibility.Judge`)

A replay is bound to its simulation version and its profile fingerprint; **there is no migration** (decision D-31 is open for after the first release, `docs/COMPATIBILITY.md`
§3). The checks in their order - the first that differs is the reason:

| Kind | When | The reason says |
| --- | --- | --- |
| damaged | the file does not read: checksum, a cut end, a header that does not parse or does not describe its match | that it is damaged |
| other format | another version of the file or of the simulation's replay inside | both versions |
| other simulation | another command table, world state or match description encoding | both versions |
| other build | the match description states a build identity that is not this game's | both builds (twelve digits of the commit, "(modified)") - "every build plays only its own replays" |
| other rate | another tick rate | both rates and `--tick-rate N` that plays it |
| other rules | another rule profile | both profiles (a long list of fixes as "the original with N fixes") and `--rules NAME` |
| other data | another definition fingerprint | "other game data" |
| map missing | this game has no map of that path | the map |
| other map | (at playback) the map's terrain is not the one the match was played on | the map |

**Every build plays only its own replays.** The build identity is in the match description of every match a server opened, and the local skirmish runs through the
embedded server (Phase 25), so a local replay names its build too. Two builds with the same formats but other code would diverge in the playback; refusing them is the honest
answer before D-31. The consequence a person feels: after an update every older replay is "Other build". A build of a modified tree is `<commit>+modified`, and two different
modified trees of the same commit state the same identity - as in a network match, this is not told apart (Phase 25).

## 5. Playback (`ReplayPlayback`, `LiveMatch` in playback mode)

The match is built again from the description in the replay's header; the commands of the human players are fed in at their ticks with their recorded sequence numbers;
the computer players and the system decide again - as `SkirmishReplay.Play`, but shown. The presentation is a `LiveMatch` in spectator mode (`LiveMatch.ForPlayback`): no
relay, the local tick driver (pause and speed are the presentation's), a command sink that refuses every order, the camera at the base of the player who recorded it.

**Checking:** at every tick for which the recording kept a session hash the playback compares its own (`HashesChecked`, `HashMismatches`, `FirstMismatchTick`); at the end
it compares the final session hash and the score screen (end tick, winner, the defeated, and - for a recording that began at tick 0 - every counter). The HUD's replay line
shows how many recorded hashes matched.

**Checkpoints:** the playback keeps the session state (`SkirmishMatchSession.Save`, packed with Brotli as for a rejoin, with the counters of the score screen) every
`CheckpointSeconds` of game time - 30 by default, a multiple of the five-second hash interval so that every checkpoint lies on a recorded hash; tick 0 is the description itself.
They are built on the first playback, in memory (the brief allowed either; the file stays small and needs no rewrite). **A seek** goes to a target tick: from the nearest kept
checkpoint at or before it - restored and checked against its hash - or on from where the playback stands, when that is nearer; it steps forward without drawing, then puts a
fresh presentation in front of the session (`Generation`). Pause, speed and the view carry over. The snapshot is the truth: a restored session goes on as the uninterrupted one
(the session-check of Phase 24), and the test checks it against the recording's own hashes.

**Controls** (the observer HUD of `docs/SPECTATOR-MODE.md`, with a third row of buttons and the replay's line when it shows a playback):

| Key | Button | Effect |
| --- | --- | --- |
| `Home` | Start | back to tick 0 |
| `PageUp` | << Back | the previous checkpoint (within a second after a checkpoint the one before it, so that pressing twice goes back twice) |
| `PageDown` | Next >> | the next checkpoint |
| Space, `1` `2` `4` `8` | Pause, 1x ... 8x | pause and speed of the presentation |
| `F` | Fog | everything, then the view of each player in turn, then everything |
| `Esc` | | back to the replay list |

The replay line: `REPLAY 0:35 / 1:10   checkpoint 4 of 7   No winner (time limit)` and "N recorded hashes matched" (or how many differ, in red).

**The end:** the shell shows the score screen with the playback's result and over it what the playback found ("The playback reached the recorded end: China won." or that it
did not, with the count of differing hashes); OK leads back to the replay list (`GameShell.ReplayFinished`, `ReplayStopped`, `ReplayFailed`).

**The original's playback controls** (`ReplayControl.wnd`: slower, faster, pause, next frame) are not used: the observer HUD already carries pause and speed, and seeking was
not the original's. Named as a deviation.

## 6. Frameworks instead of a person, and what they measured

| Check | What it does | Result (16 October 2026, this machine) |
| --- | --- | --- |
| `Game.Tests` suite `ReplayBrowserTests`, without data | the file read and written again; a changed byte at 37 places, a cut end, another version, another kind of file refused; the store's names, order, rename and delete rules; every compatibility reason found in its order with words of its own; every ending with words | green |
| the same, with real data, at 30, 60 and 120 Hz | a 70-second skirmish (a human with group orders against the built-in AI on Alpine Assault) recorded as the client records it, played back: every one of the 15 recorded hashes, the final hash and the score screen as recorded; three seeks back and forth (and one forward from tick 0, one back to tick 0, one beyond the end), each at the recorded hash, pause, speed and view kept, and from each the recorded end reached; the server's file of the same match to the same end; refusals of another build, another rule profile (a real game data loaded with `--rules fixes`), a changed byte, another rate, a missing map, another map, a header that lies; the list in six resolutions (9 states, 54 hashes in `tests/CnCNew.Game.Tests/baselines/replay-screens.txt`, no two alike, the same at every rate); a `ShellScript` person: a skirmish of the menus recorded, found in the list, played to the end with two seeks back and one forward, renamed and deleted | green at 30, 60 and 120 Hz (about 31 s each) |
| `NetShellTests` "Host game and join" | both games of a match of a server through real loopback sockets record it; each replay names Alice and Bob, is "network", "completed", ends at the hash both reached and plays to it; both recorded the same commands | green |
| `scripts/check-replay-browser.ps1` (step `replay-browser` of `scripts/test.ps1`; gate of `game` and `client`, tier 4) | the window client by `--shell-script`: a 40-second skirmish recorded, the list with a damaged file beside it, the damaged file refused with its reason, the recording played at 8x with PageUp, PageUp, PageDown, to the end, renamed, deleted; NOT RUN (exit code 3) without a display | ok in 68 s at 60 Hz: three seeks at the recorded hash (116 to 279 ms each, scene rebuilt), the end at the recorded hash and result |

## 7. Measurements of long matches

`ReplayBrowserTests --replay-measure-minutes N` (Release, 60 Hz, this machine, 16 October 2026, not on a quiet machine - the times are an order of magnitude): eight computer
players on Twilight Flame (America, China, GLA and five generals), seed 11, recorded as a spectator's match, then opened, played to the end without seeking and sought four
times.

| | 10 game minutes | 60 game minutes |
| --- | --- | --- |
| ticks | 36,000 | 216,000 (the time limit; no winner) |
| commands in the replay | 5,752 | 35,686 |
| the file | 287 KB | 1.67 MB |
| writing it at the end of the match | 4 ms | 32 ms |
| opening it (checks, map, tick 0) | 226 ms | 231 ms |
| playing it to the end with the presentation, nothing drawn | 33.8 s | 177.9 s |
| recorded hashes compared / differing | 125 / 0 | 725 / 0 |
| checkpoints kept / memory they take | 21 / 1.9 MB | 121 / 38.0 MB |
| a seek to a checkpoint (restore, no step) | 209 to 273 ms | 239 to 317 ms |
| a seek 20 s past a checkpoint (restore and 1,200 ticks) | 1,043 ms | 1,715 ms |

A seek costs the restore of a state (a quarter of a second for eight players) and the ticks from the checkpoint to the target without drawing; the farthest target lies one
interval (30 s, 1,800 ticks at 60 Hz) past its checkpoint - about 2.5 s for eight players late in a match. In the window the scene is built again on top (measured 116 to 279
ms per seek for two players, section 6, including the restore). The checkpoints of an hour of eight players take 38 MB of memory; they are dropped when the playback ends.

## 7a. The analysis of a replay (Phase 36)

The list's fifth button, "Analysis" (below "Back"), and the score screen's "Analysis" after a playback open the analysis of the replay (`docs/MATCH-ANALYSIS.md`): the
replay is checked as for a playback (`ReplayPlayback.Prepare`), played again without a picture, every recorded hash compared - a replay that leaves its recorded match
gives no analysis - and shown on six pages. `SimTool match-analysis <content> <file.cncreplay> --out <directory>` writes the same analysis as JSON, TSV and an HTML page.

## 7b. The observer of a replay (Phase 37)

A playback has the observer features of S3-M2 (docs/SPECTATOR-MODE.md §14): the view of every player (`F4`, `F5` to `F12`), the research tab (`R`) and the event ticker.
The ticker belongs to the playback (`ReplayPlayback.Ticker`): it is told every tick the playback steps, also the ticks a seek steps without drawing, and only ticks it has
not seen - so after any seek it shows the lines of the straight playback up to the frame the playback stands at, none twice. The client's line at the end of a playback
names its lines (`replay: end ..., ticker lines N`).

## 8. Deviations, named

- The list's third column shows the state of the file instead of the version of the game; the third button renames instead of copying to the desktop; the details window is
  this project's own.
- The original named a replay at the score screen (`ButtonSaveReplay`, `PopupReplay.wnd`) and kept only the last one unnamed; here every match is kept under a name of
  its date and map, and renamed in the list. The score screen's "Save Replay" button stays disabled.
- Checkpoints are built on the first playback and kept in memory, not stored in the file.
- `ReplayControl.wnd` is not used (section 5).
- The new texts are this project's own English, as those of the multiplayer screens.

## 9. Not verified - what only a person can judge

- Whether the list is easy to read: the columns of the layout hold name, date, state and map; whether the details window in the button column is large enough and readable at
  the resolutions people use (it is laid out in the 800 x 600 coordinates of the layout and scaled with it). The hashes prove that the pictures do not change, not that they
  look right.
- Whether the playback controls feel right: that a seek of 0.1 to 0.3 s with the scene rebuilt is acceptable, that the camera stays where it was, that the speeds suit.
- Sound during a playback (the effects of the playback play as in a spectator's match; not listened to).
- A replay of a match on a real network between two machines, a desync and a lost connection recorded in a window (the endings are tested by their facts, and a desync is
  recorded by the same path as any end).

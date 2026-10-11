# Campaigns, mission flow and savegames (Phase 9, part A items 3–5)

The logic layer for campaigns and savegames lies in `CnCNew.Game` (`Campaign/`, `Save/`); it does not depend on Stride or
the shell and can be tested headless (`tests/CnCNew.Game.Tests/CampaignTests.cs`). The mission simulation itself is in
`docs/SCRIPT-ENGINE.md`. **Not wired:** the main menu entries "Campaign" and "Load game" stay disabled
(`Shell/Screens/MainMenuScreen.cs`); the screens (campaign selection, briefing, load list) are presentation work of the next phase,
the interfaces for them are finished (§7).

## 1. Campaign catalog (`CampaignCatalog`)

`Data/INI/Campaign.ini` is listed in the load plan as "not loaded" (`LoadPlan.cs`), so it is not part of the compiled
definition graph. The format is a fixed, flat INI subset (`Key Value` or `Key = Value`, `;` comments, `Campaign` and
`Mission` blocks with `END`); `CampaignCatalog.Parse` reads it in the layer that owns the campaign flow. Keys according to
`CampaignManager::parseMissionPart` (`CampaignManager.cpp`) plus the Zero Hour keys of the file: campaign `CampaignNameLabel`,
`FirstMission`, `FinalVictoryMovie`, `IsChallengeCampaign`, `PlayerFaction`; mission `Map`, `NextMission`, `IntroMovie`,
`ObjectiveLine0..4`, `BriefingVoice`, `UnitNames0..2`, `LocationNameLabel`, `VoiceLength`, `GeneralName`. **Unknown keys** are collected in
`UnknownKeys` (never silently skipped), unclosed/surplus blocks are an error.

`Validate(mapExists)` reports: missing first mission, dangling `NextMission`, duplicate names, missing maps, unreachable missions
(cycles end the run instead of making it endless).

## 2. Progress (`CampaignProgress`)

Immutable record: campaign, difficulty (`ScriptDifficulty`), current mission, completed missions (sorted), attempts per
mission, finished flag. Transitions: `Begin` → `Started()` (counts an attempt) → `Won(campaign)` (mission completed, the next one becomes
current, after the last one `Finished`) or `Lost()` (state stays, repetition of the same mission). `Describe`/`Load` in the
canonical state form (`IStateWriter`/`IStateReader`).

**Unlocks:** not modelled – every campaign and every mission of the chain is selectable. The order of the challenge campaigns and
the profile unlocking of the original shell are a matter of the profile/the shell and not verified here.

## 3. Flow (`CampaignController`, `MissionRun`)

`MissionContext` = compiled templates + definition fingerprint + "load map" (decoupled from the file system and `GameData`;
`MissionContext.From(GameData)` for the game, lambdas for tests). `MissionRun.Load` builds terrain and `MissionSession`; `Restore`
restores them from a `MissionSnapshot`.

`CampaignController.StartCurrentMission(maxTicks, humanSideName)` counts the attempt and starts the mission with difficulty and
attempt count (`MISSION_ATTEMPTS`); the logic seed depends on mission and attempt, so a repetition is not the run of the
last attempt. `humanSideName` (optional, default empty) passes `MissionOptions.HumanSideName` through — for maps that do not mark a side
as `playerIsHuman` themselves (the Generals' Challenge maps), the host must name the side of the human, see
`docs/SCRIPT-ENGINE.md` §7; the general selection itself is a matter of the shell (§7 below).
`Conclude(run)` reads `MissionSession.Outcome` (set by the script): victory → `Won`, defeat → `Lost`; a run without a result or of a
foreign mission is rejected. `ResumeCurrentMission(state)` continues a saved run without counting an attempt.

## 4. State formats

### `MissionSnapshot` (Simulation, Version 2)

*The heading names the version of Phase 10; the current one is version **3** (since Phase 22, fixed point Q39.24). Phase 23 did not raise any version.*

UTF-8 JSON, fixed key order, integers only, no paths/timestamps. Fields: `format` (`cncnew-mission-state`), `version`,
`tickRate`, `programFingerprint` (structural checksum of the map script), `definitionFingerprint`, `terrainFingerprint`, `options`
(seed, difficulty, tick limit, presentation latency, combat interval, mission attempts, since version 2 `humanSideName`), `stateHash` (world + script), `session`
(combat counter, command sequences per player), `script` (`ScriptEngine.Describe`: script/group state, counters, flags, named
objects, teams, object lists, follow-up orders, attack priorities, damage history, outcome, counts of the missing types), `world`
(the **unchanged** `WorldSnapshot`, version 7, embedded as raw JSON). `Restore` builds the world from the snapshot, derives the script program
anew from the map, loads the script state and checks both hashes. Rejected (`SnapshotFormatException`): different version, different
definitions, different terrain, different map script, flipped/missing bytes.

Measured: saved and restored, a mission continues tick by tick hash-identically; save → load → save
reproduces the bytes.

### `SaveGame` (Game, Version 3)

One file, one UTF-8 JSON document: `format` (`cncnew-savegame`), `version`, `title`, `mapPath` (virtual), `campaign`, `mission`,
`difficulty`, `tick`, `tickRate` (since version 2), `definitionFingerprint`, `stateHash`, `stateSha256` (SHA-256 over the embedded state – covers every byte, including
the presentation stream, which the simulation hash deliberately does not contain), `progress` (`CampaignProgress` or empty), `state` (the
`MissionSnapshot`, raw). The same session in the same tick yields the same bytes; loading + saving again reproduces the file.

`SaveGame.ReadHeader` reads only the header (load list). `Load` reports understandably **why** a file does not load:
"Not a CnCNew savegame", "Unsupported savegame version N (this game reads version 3)", "Unsupported tick rate N Hz in the savegame",
"written at 60 Hz; this session runs at 30 Hz" (rate of the savegame against the rate of the session, in both directions),
"written with different game data" (definition fingerprint), "belongs to campaign X, which this game does not have",
"damaged or truncated", "checksum does not match", "state does not match its header" (tick, rate or state hash of the header do not match the
embedded state).

**Version 3 – fixed point Q39.24 (7 October 2026, `docs/FIXED-POINT-Q39-24.md`).** The embedded mission state carries fixed-point values with 24 instead of 16 fractional bits; states of versions 1 and 2 are rejected by their version, never read with the new scale.

**Version 2 – the envelope carries its rate (6 October 2026, `docs/TICKRATE-MIGRATION.md` §14.9).** `tick` counts ticks of the world that was saved, and
says nothing without a rate; version 1 wrote no rate (a savegame of a different rate was noticed only indirectly, through the fingerprint, which contains
the rate). Since version 2 `tickRate` (ticks per second: 30, 60 or 120) stands directly behind `tick`. Rules:

- **Version 1 is rejected, never reinterpreted.** Reading a file without a rate as "30 Hz" would be a silent assumption; the existing version check
  rejects it with "Unsupported savegame version 1 (this game reads version 3)", in the load list the slot appears with this reason. There is no
  conversion of old savegames (binary compatibility with old savegames is not in scope).
- **The rate is checked before the fingerprint**, so that the message names the cause. A savegame of 60 Hz loads only in a session with 60 Hz.
- **Since the switch of the default to 60 Hz (Phase 23, `e8632bb`, 8 October 2026) the client without an option rejects a savegame written at 30 Hz** ("This savegame was written at 30 Hz;
  this session runs at 60 Hz. It cannot be loaded."). It loads only in a session with 30 Hz – client with `--tick-rate 30` – and continues there at 30 Hz. The shell offers no choice of the rate
  and does not load a state with its own rate afterwards; there is no conversion. No byte of the format has changed, no version was raised (`SaveGame` 3, `MissionSnapshot` 3,
  `WorldSnapshot` 10); according to `docs/MULTIPLAYER-PLAN.md` §6 rule 7 this is permissible before the first release and is named here as a limit. Tests with a fixture written at 30 Hz:
  `Tests.TickRateStatesWrittenAtThirtyOutliveTheDefault` (Simulation) and `CampaignTests` "Savegame: the header carries the tick rate …" (`docs/TICKRATE-MIGRATION.md` §15.5.3).
- **Header and state must agree:** after restoring, tick and rate of the header are compared with the world (the embedded
  `MissionSnapshot` carries its rate itself); a header with a forged rate or a wrong tick is rejected.
- **Lists show game time instead of the tick:** `SaveGameHeader.GameTime` (`m:ss`, whole seconds from tick and rate, minutes run beyond 59) –
  the same text for the same point in time at every rate. Load screen (`LoadGameScreen`) and pause menu (`MissionPauseMenu`) show
  "Title (m:ss)" instead of "Title (tick N)". No checked-in screen hash contains a load list with entries; the 54 screen hashes are unchanged.

**Outside this format:** skirmish matches - since Phase 24 they have a format of their own (§8). What follows is the reason as it stood until then: the skirmish AI kept state outside the world that this format does not carry;
a skirmish savegame would therefore not continue identically. A savegame never corresponds to a `ReplayFile` (the bug
`SharedReadyFrame` is fixed, but missions are not reproducible as a replay anyway, `docs/SCRIPT-ENGINE.md` §3).

### `SaveGameStore`

`<Slot>.cncsave` in a directory: atomic writing (temporary file, then move), `List()` reports damaged files with a
reason instead of throwing, slot names cannot leave the directory, `Delete`. The storage location is chosen by the shell (settings), not the
store.

## 5. Automated check

`tests/CnCNew.Game.Tests/CampaignTests.cs` (synthetic data): parser, validation, progress incl. JSON round trip, control flow with
defeat/repetition/attempt counter/seed, save → load → identical continuation through a victory, byte canonicity,
rejection of foreign/damaged/truncated files, store; since version 2 the rate of the envelope (loading at 30 and at 60 Hz, cross-wise rejection in
both directions, version 1, missing and unsupported rate, forged header, same game time in the list at both rates). With `--content-dir`: the real `Campaign.ini` (17 campaigns, 85 missions,
0 unknown keys, the only anomaly the map `Maps/CampEADemo/CampEADemo.map` missing in the retail content).

## 6. Tool `CnCNew.CampaignTool`

```powershell
dotnet run --project tools/CnCNew.CampaignTool -- list game/content
dotnet run -c Release --project tools/CnCNew.CampaignTool -- audit game/content --ticks 900
dotnet run -c Release --project tools/CnCNew.CampaignTool -- audit game/content --map MD_USA01 --explain
dotnet run -c Release --project tools/CnCNew.CampaignTool -- audit game/content --map GC_LaserGeneral --human-side PlyrLaserGeneral
dotnet run -c Release --project tools/CnCNew.CampaignTool -- playthrough game/content --campaign USA
```

`--human-side <Name>` (`audit` and `playthrough`): passes `MissionOptions.HumanSideName` through, for maps without a `playerIsHuman` side of their own
(`docs/SCRIPT-ENGINE.md` §7); a name that matches no side of the map falls back to the default with a note (never silently).

**Game time in the output (6 October 2026):** `drive` and `audit` write, below every map line in which an end was reached, a separate line
`game time (m:ss at 30 Hz): victory after 0:26, defeat after 0:07` and at the end `Budget: N ticks are m:ss of game time (m:ss) at 30 Hz.`; `playthrough`
names the game time in parentheses behind the tick. The map lines themselves (`victory REACHED … @Tick`, `win@Tick`, `lose@Tick`) and the summary lines
("… identical on a second run …") are unchanged byte for byte, because they are parsed; ticks stay ticks of the chosen rate (`--tick-rate N`).
**Since Phase 23** `CampaignTool` and `SimTool` run at 60 Hz without `--tick-rate` (the lines then say "at 60 Hz", and a tick is 1/60 s); the default budgets are game time
(`drive` 2,000 s, `audit` 60 s, `playthrough` 30 s; `--seconds`/`--minutes`, `--ticks` stays as a raw value). Tick counts in this document (`--ticks 900`, "1,500 ticks", "330–360 ticks") are
historical and measured at 30 Hz. What the campaign tools yield at 30, 60 and 120 Hz: `docs/SCRIPT-ENGINE.md` §9, last subsection.

`audit`: every unique campaign map – two fresh runs, save/restore, end probes victory/defeat, then the
aggregation of the missing action/condition types, unresolvable references and object types that cannot be created; exit code ≠ 0 on
non-determinism, continuation deviation or load error. `--explain` names, per end not reached, the end scripts and their
false conditions. `playthrough`: plays a campaign through (start mission, write savegame and reload it, eliminate
enemies, take over the progress).

As of 1 October 2026: `CampaignTool list` – 17 campaigns, 85 missions, 0 unknown keys, one problem (the EA demo map
`CampEADemo` is missing in the retail content). `CampaignTool audit` – 24 maps, 24/24 deterministic, 24/24 identical after save/restore (table
and end probes: `docs/SCRIPT-ENGINE.md` §9). The size of a mission state is around 3 MB (2,991,420 bytes for the original map `USA01` after
1,500 ticks, measurement by the tool `SimTool mission-run`; the world state dominates).

## 7. Connection to the shell (Phase 10, item 2 – implemented)

`src/CnCNew.Game/Shell/Screens/CampaignScreens.cs`, `src/CnCNew.Game/Shell/MissionPresentation.cs` and the extensions in
`GameShell.cs`/`MainMenuScreen.cs` wire this section completely:

- **Campaign selection** (since Phase 16 as in the original, `docs/GAME-SHELL.md` §13): `MainMenu.wnd`'s `ButtonUSA`/`ButtonGLA`/`ButtonChina` open the difficulty question **in the main menu** (`MapBorder4`:
  Easy/Medium/Hard/Back), the choice calls `GameShell.BeginCampaign` → `CampaignProgress.Begin` + `CampaignController.StartCurrentMission`; `ButtonChallenge` asks the same and then opens
  `ChallengeSelectScreen` (`ChallengeMenu.wnd`: the nine playable `GeneralPersona`s of `ChallengeMode.ini` on the `GeneralPosition0..11` buttons, biography and Play after the choice, the chosen difficulty applies).
  Loading is the Load button of the main menu (`LoadGameScreen`). Zero Hour never uses `SinglePlayerMenu.wnd` and `DifficultySelect.wnd`, and the shell no longer does either.
- **Briefing**: `MissionBriefingScreen` reuses the real `SinglePlayerLoadScreen.wnd` (whose `ObjectivesWin` group with
  `StaticTextLine0..4` is already meant for exactly `ObjectiveLabels`; `StaticTextCameoText0..3` carries `LocationLabel` plus
  `UnitNames0..2`). `ShellPhase.MissionBriefing` holds the screen for `max(VoiceLengthSeconds, 2)` simulated seconds
  (no click skip, exactly like the shipped file – it declares no input callback); `IntroMovie`/`BriefingVoice`
  appear as a named placeholder in the progress text (no Bink decoder, permanent limit). Most real
  USA/GLA/China missions carry no `ObjectiveLine` entries at all in `Campaign.ini` (only `TRAINING` does) – an empty
  line is then correct behaviour, not an error.
- **Load list**: `LoadGameScreen` (`SaveLoad.wnd`, from the main menu via "Load/Replay" → "Load Game") lists
  `SaveGameStore.List()`, loads through the existing confirmation dialogs of the file; a `SaveGameException` shows
  `GameShell.ShowError` as `MessageBox.wnd` (new plain-text variant, because the error message is never a CSF key).
- **Saving in the game**: `MissionPauseMenu` (`src/CnCNew.Game/Hud/MissionPauseMenu.cs`) combines `QuitMenu.wnd` (like
  `MatchController`) with `PopupSaveLoad.wnd` behind its `ButtonSaveLoad`; the slot name comes from the entered
  description (made file-name safe), overwriting asks through the existing `OverwriteConfirmParent`.
  Independent of `MatchController`/`LiveMatch` (those stay skirmish-specific, see `docs/handoffs/HANDOFF-PHASE10.md`).
- **Mission completion**: `GameShell.MissionFinished` calls `CampaignController.Conclude` and puts `MissionBannerScreen`
  (`Victorious.wnd`/`Defeat.wnd`, pure graphics without buttons in the original) under `MissionResultScreen` (`ScoreScreen.wnd`
  reused, because the real victory/defeat files have no buttons at all); `ButtonContinue` becomes "Next
  Mission"/"Retry Mission"/"Campaign Complete" depending on the outcome, `ButtonOk` stays "Main Menu".
- **`ScriptOutput` playback and feedback to the VM**: `MissionPresentationDirector` (new) drains
  `ScriptEngine.DrainOutputs()` per tick, tracks speech/movie (placeholder duration, no audio/video length data set
  available), screen text (`DISPLAY_TEXT`/`SHOW_MILITARY_CAPTION`/`INGAME_POPUP_MESSAGE`, real CSF resolution through a
  callback) and radar events (`RADAR_CREATE_EVENT`/`OBJECT_CREATE_RADAR_EVENT`/`TEAM_CREATE_RADAR_EVENT`, real
  world position). Camera movements (`MOVE_CAMERA_TO` among others) are tracked with their real seconds-parameter duration and
  `CompletePresentation` is called exactly at this point in time (`BeforeStep`/`AfterStep`, timed exactly like the
  engine's own `PresentationLatencyTicks` self-resolution) – with a test against a real camera movement sequence
  (`CnCNew.Game.Tests` → `MissionPresentationTests`). `ApplyCameraTo` plays the script camera on a real
  `RtsCamera` (since Phase 16 over the duration of the script, plus input lock, letterbox, fades, counters, script sound and counters per output type: `docs/SCRIPT-ENGINE.md` §7a); `BuildOverlayCommands` delivers the screen text as `UiDrawCommand`s, which the existing
  `CnCNew.Client.Stride` `UiRenderer` can already draw (no new renderer needed). `MissionUiBridge` forwards
  selection changes to `ScriptEngine.SelectNamed` (`NAMED_SELECTED`) and passes `SignalUiInteraction` straight through
  (`FLAG`); a fixed mapping "this button → this flag" does not exist (the mission metadata carries that nowhere),
  that stays a decision of the respective host code.
- **Delay without a director (Phase 11):** `CampaignController.StartCurrentMission(maxTicks, humanSideName, presentationLatencyTicks)` passes
  `MissionOptions.PresentationLatencyTicks` through; a host without a `MissionPresentationDirector` thereby sets a delay fallback for
  `HAS_FINISHED_*`/`CAMERA_MOVEMENT_FINISHED` (default 0 = finished immediately, as headless). Test: `CampaignTests` → "a host without a presentation director …".
- **Mission HUD (Phase 11):** a campaign mission can now be played with the full skirmish command bar – `LiveGame`/`LiveMission`/`MissionController`, see
  `docs/GAME-HUD.md` §14. `MissionPresentationDirector`/`MissionUiBridge` hang in it; `MissionPauseMenu` stays the save/load dialog of the mission.
- **Generals' Challenge general choice (Phase 11, extended in Phase 12):** `ChallengeSides.HumanSideFor(map)` (`CnCNew.Game/Campaign`) determines the human side of a challenge map, and
  `CampaignController.StartCurrentMission` calls it for `IsChallenge` campaigns when the host names no side (`ChallengeSelectScreen` thereby automatically obtains the right
  side, without code of its own). Measurement (`CampaignTool sides`): seven of the eight challenge maps mark their side themselves (`ThePlayer`, `playerIsHuman`) – there the map decides;
  `GC_LaserGeneral` marks none and defines only its opponents (`PlyrAmericaLaserGeneral` is the enemy whose production buildings the victory script wants to see destroyed). **Phase 12 – persona faction:**
  `MissionOptions.HumanFaction` (mission state 2, `humanFaction` is saved only when a value is set) carries the faction of the chosen general; `ScriptProgram.WithHumanSide(side, faction)` thereby replaces
  the faction of the human side and, on a map without a human side (`GC_LaserGeneral`), appends a **synthetic side** `ThePlayer` (enemy of every non-civilian map side and
  vice versa; `ChallengeSides.SyntheticHumanSide`); the program checksum stays that of the map, so that a savegame recognises the program, the options say how it was adapted.
  `CampaignCatalog.WithPersonas(ChallengeMode.ini)` fills the `PlayerFaction` of every challenge campaign without one of its own from `GeneralPersonaN` (`PlayerTemplate` + `Campaign`; personas with
  `Campaign = unimplemented` and campaigns with their own `PlayerFaction` stay unchanged), `GameShell` reads `Data/INI/ChallengeMode.ini` at start. Measurement: `GC_LaserGeneral` now has its own
  human side and can be won and lost on the script path (objective driver, `docs/SCRIPT-ENGINE.md` §9); `CampaignTool drive|audit` pass the persona through.
  Measured: `CampaignTool playthrough --campaign CHALLENGE_0` wins Mission01–05 by annihilation (victory after 330–360 ticks).

## 8. Skirmish savegame (Phase 24)

Since Phase 24 a running skirmish can be saved and continued. The statement of §4 and of `docs/SIMULATION-CORE.md` §18 that it is "deliberately not supported" is superseded:
the built-in computer player's state is part of the session state (`docs/SIMULATION-CORE.md` §45.1), and the session writes and restores itself (§45.3).

### `SkirmishSaveGame` (Game, format `cncnew-skirmish-savegame`, version 1)

A format of its own, so that a mission save and a skirmish save cannot be taken for each other (`SaveGame.ReadHeader` refuses a skirmish save and the other way round). One
UTF-8 JSON document with a fixed property order, like `SaveGame`:

| Field | Content |
|---|---|
| `format`, `version` | `cncnew-skirmish-savegame`, 1 |
| `title`, `mapPath`, `tick`, `tickRate` | what the load list shows (game time, not ticks) and what is needed to load the map |
| `definitionFingerprint`, `stateHash` | the definitions the save belongs to; the session hash at the moment of saving |
| `session` | `builtin` (a `SkirmishMatchSession`) or `original` (a `ScriptedSkirmishSession` with the original AI) |
| `viewer` | who the local viewer was: player, auto-play or spectator - a matter of this machine, not of the match |
| `stateSha256`, `state` | checksum and the session state: match description, counters, every computer player, the recording so far, the world |

`SaveGameHeader.Kind` tells the two kinds apart (`Mission`, the default, or `Skirmish`); `SaveGameStore.List` reads either. A file of another version, another tick rate,
other definitions, another terrain or with a changed byte is refused with a message fit to show (`SaveGameException`).

- `LiveMatch.Save(title)` writes the save of a running match; `LiveMatch.Restore(data, bytes, viewport)` loads the map the save names, restores the session and puts a fresh
  presentation in front of it. Since Phase 25 the restored match is a room of the embedded relay that **starts from the saved state** (`docs/NETWORK-PROTOCOL.md` §4, §7):
  the start request carries the state, its tick, its session hash and the commands every human has given so far; the relay starts its tick count there and compares the hash
  the client reports after loading with the saved one. The session state inside a save is version 2 (its match description has encoding 2); a save written in Phase 24 is
  refused with a message. Camera, selection, control groups and effects are not saved: the viewer starts at his start position with nothing selected.
- **Shell:** the main menu's load list (`LoadGameScreen`) lists skirmish saves beside mission saves; loading one goes straight to `ShellPhase.InMatch` and hands the host a
  `ResumeMatchOutput`.
- **Client:** the "Save/Load" button of the in-game menu (`QuitMenu.wnd`, Escape) saves a running skirmish to the slot `skirmish` and prints a line on the console. There
  is no dialog for a name or a slot, and no loading from inside a running match - both are the main menu's.

**Test** (`CampaignShellTests`, real data): a skirmish with a human slot at start position 1, a chosen colour and a computer player of a random faction is played for 45
seconds, saved, listed by the store as a skirmish, loaded through `Main menu -> Load -> select -> confirm` and continued; the same group order is given to both. The loaded
match has the session hash of the uninterrupted one in every tick of the next 20 seconds, its computer player goes on giving orders, and its replay equals the uninterrupted
one. A changed byte, another tick rate and the mission reader are refused.

**Limits.** A match of the original AI with a slot played by the built-in one still cannot be saved (`ScriptedSkirmishSession.Save` refuses; not converted). A save holds the
whole recording, so its size grows with the match (0.4 to 0.8 MB for two players, 3.5 MB for eight after five minutes, `docs/SIMULATION-CORE.md` §45.3). Nobody has pressed
the button or loaded a skirmish in a window.

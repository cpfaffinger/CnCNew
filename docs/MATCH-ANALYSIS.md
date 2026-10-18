# Match analysis with graphics (S3-M4, Phase 36)

The statistics of a skirmish over its course - money, income, spending, army value and composition, losses and kills, buildings, upgrades, sciences, special powers
and superweapons, commands per minute, the regions of the map a player holds, where the armies stood and where they fought - computed from what the simulation
tells every tick, exported as JSON and TSV, drawn as an HTML report by a tool and as a screen in the client after a match and from the replay list. Stage 3 milestone
S3-M4 (`docs/plan/STAGE-3-CHANGES.md`); brief `docs/handoffs/HANDOFF-PHASE36.md`.

**Nothing of the simulation changed.** The analysis reads the events of every tick and the world after it; it writes nothing into either and keeps no state in them.
Both manifests are unchanged; no version of a simulation format moved.

## 1. The core: one analyser for a running match and for a replay

`CnCNew.Game.Analysis.MatchAnalyzer` is told every tick of a skirmish (`Observe(TickResult, SimulationWorld)`) and gives a `MatchAnalysis` when the match has ended
(`Finish`). Two hosts feed it:

- `LiveMatchAnalysis` attaches to a running `LiveMatch` (its `TickStepped` event) - the client does this for every skirmish of the built-in AI it records, and the score
  screen offers the result;
- `ReplayAnalysis` plays a replay file of the replay browser again without a picture: the file is checked as for a playback (`ReplayPlayback.Prepare`: compatibility,
  the replay inside, the map), the match is built again from its description, the humans' commands are fed in at their ticks and the computer players decide again.
  Every tick for which the recording kept a session hash is compared; **a replay that leaves its recorded match gives no analysis** (`ReplayRefusedException`).

The same match gives the same analysis from both hosts - a test compares the export byte for byte (`MatchAnalysisTests`, section 6).

**What is counted** (where every value comes from):

| Quantity | Source |
| --- | --- |
| money | `SimulationWorld.MoneyOf` at the sample |
| credits collected, income per minute | `SupplyDeposited` of the player's supply centre (the income over the last game minute, `IncomeWindow` of the observer panels) |
| spent | the list price (`BuildCost`) of every unit when `ProductionCompleted` reports it, of every building when its construction begins (`ConstructionStarted`, less `ConstructionCancelled`), of every upgrade when it completes (`UpgradeCompleted`) - **not what the money paid**: cost modifiers and the refund of a cancelled unit are not seen |
| army value, its composition | the living units of the kinds infantry, vehicle and aircraft that are not workers (`SpectatorStats.Classify`), at list price, at every sample |
| units and buildings built | `ProductionCompleted` (by the factory's owner and the template in the event's value), `ConstructionCompleted` |
| losses and kills | `EntityDied` of a unit, worker or building; a kill counts for the killer's player only when the victim is somebody else's (`Object::scoreTheKill`) |
| buildings sold, captured | `StructureSold`, `StructureCaptured` (new owner as the player, old owner as the value) |
| upgrades, sciences | `UpgradeCompleted`, `ScienceCompleted` |
| special powers, superweapons | `SpecialPowerUsed`; a power of a building of the kind `FS_SUPERWEAPON` is a superweapon. The place: the target object's position, or for a power aimed at a place the target of its command (joined by tick, player and power when the analysis ends) |
| commands per minute | the commands of the match (the session's recording), by player and whole minute of game time; a computer player's commands count as well, the system's do not |
| regions held | the map in 8 x 8 regions; a region is held by the player whose army and finished buildings in it are worth most (no tie, workers do not count) |
| movement heat map | every army unit at every sample, in a 32 x 32 grid over the map, per player |
| fights heat map | the place of every loss, in the same grid |
| power | production and consumption at the sample, as the observer panels read them |

**When:** the world is sampled every five seconds of game time at the tick that ends an original frame - frame 0, 150, 300, ... - and once more at the end. Five
seconds are 150 original frames, a whole number of ticks at 30, 60 and 120 Hz, so the samples are taken at the same game times at every rate. Every time of the
export is a frame of the original (30 to a second of game time), never a tick.

**What is not counted** (it cannot be had from the events and the world without a change of the simulation): the money a player actually paid (cost modifiers,
refunds of cancelled production), money from other sources than supply (oil derricks, bounty, the cash hack, a sold building's refund - the world shows them only
inside the money), the place of a unit whose object was gone from the world when its death was read (its position is written as -1; counted in
`unresolvedEvents` when even the object's owner is unknown), what a unit fired at or how much damage it did, the line of sight.

**The score screen's counters were wrong, and are fixed** (`31b03c2`): `MatchStatistics` counted only events that carry a player, and the deaths, the finished
units and the supply deposits carry none - the score screen showed 0 units built, lost and destroyed and 0 resources for everybody. The analysis, which reads the owner of
the object, found it: its numbers did not agree with the score screen. The counters now count by the owner as the analysis does (the score screen's "buildings destroyed"
is filled as well). Still not as the original: its `ScoreKeeper` counts only objects of the kinds `SCORE`, `SCORE_CREATE` and the scoring building masks (fidelity register
F-42 in `docs/plan/STAGE-1-PORT.md`).

## 2. The export

`MatchAnalysisFormat` writes four files; `report.html` comes beside them from the tool:

- `analysis.json` - format `cncnew-match-analysis`, `schemaVersion` 1: the match (map, rate, rules, start and end frame, duration, winner, how it ended, the source),
  the constants (`sampleSeconds` 5, `regionsPerSide` 8, `heatGridSize` 32, the map's extent), every player (name, faction, team, computer and its AI, colour, result,
  collected, spent, kills, the counts, commands per minute, units built and lost by type, buildings built and lost by type), every sample, every event with its
  frame and place, the heat maps;
- `players.tsv`, `samples.tsv`, `events.tsv` - the same as tables with a header row.

The export is a function of the analysis: invariant numbers, `\n` line ends, the analysis' own order (players by slot, samples by time and player, events as the
simulation raised them, tables of types by value, count and name), no wall time and no culture - the same file gives the same bytes on every run, and under the
cultures de-DE, ar-SA and tr-TR (a test). A tab or a line break in a player's name is written as a space in the tables.

**The tool:**

```powershell
dotnet run -c Release --project tools/CnCNew.SimTool -- match-analysis game/content "<replay>.cncreplay" --out <directory outside the repo> [--title TEXT]
```

The game data is loaded at the rate and with the rule profile the file names; the tool prints the players' totals and the SHA-256 and size of every file it writes
(two runs over the same file print the same lines). A replay of a real match is a file of the user - it never goes into the repository, nor does its analysis.

## 3. The graphical report

`MatchReport.ToHtml` makes one HTML page: the players' table; seven timelines (money, income per minute, spent, army value, regions held, units and buildings lost,
commands per minute); the army composition of every player as stacked bars (infantry, vehicles, aircraft); the loss exchange; the two heat maps (movement in the
colour of the player who stood there most, fights in red with rings for superweapon strikes; north up as on the minimap); the most built and most lost units; the
list of upgrades, sciences, powers, captures and defeats. Inline SVG and CSS, no script, nothing loaded from elsewhere, light and dark colours; integer coordinates
only. The charts' numbers (the axis tops rounded to 1, 2, 2.5 or 5 times a power of ten, the axis steps, the heat intensities by an integer square root) are computed
once by `AnalysisCharts` and drawn by both the page and the screen, so the two show the same.

## 4. The screen in the client

`MatchAnalysisScreen` lies on the original's `ScoreScreen.wnd`: its backdrop, its header line (`ChallengeWinLossText`: "Analysis - page n of 6: title", the map,
the length, the winner), its buttons - the save-replay button is "Previous page", the buddy button "Next page", OK leads back. The original has no layout for
charts, so they are drawn by the screen itself inside the backdrop (fill rectangles, lines, texts of the draw list). Six pages: economy (money, income), army (army
value, regions), spending and losses, composition and exchange, the map (the two heat maps), activity (commands per minute and the list of events; Up and Down
scroll). Left and Right turn the pages, Escape leads back.

- **After a match:** the client analyses every recorded skirmish while it runs; the score screen's save-replay button - in the original it saved the replay, which
  this game does by itself - says "Analysis" and opens the screen.
- **From the replay list:** a button "Analysis" below "Back" (a copy of the list's third button, made from the shipped layout at run time, not a layout of this
  project's text) opens the screen, which computes the analysis first - `ReplayAnalysis`, `MatchAnalysisScreen.TicksPerUpdate` (600) ticks a frame - while the header
  shows how far it has come. A refused replay is a message with the reason.
- **After a playback:** the score screen offers the analysis of the replay that was played (the same way as from the list).

## 5. The shared field comparison (S3-M4 (c))

`CnCNew.Game.Diagnostics.StateFields` is the one field comparison of the diagnosis tools: a described state is flattened into paths and values - through
`IStateWriter` (`StateFields.Of`) or from a saved document (`StateFields.OfJson`) - and two such tables are compared path by path in ordinal order (`Compare`, with rules
for ignored paths, equality and paths only one side has). An element of an array that is an object whose first field is its `id` is addressed as `[id=N]`, so one
missing element does not shift the others. `SimTool rate-compare --fields` compares one match at two rates with it (its rule of equality knows ticks and values per
tick), `NetTool desync-diff` compares a client's saved state with the match played again from its recording (the embedded recording is skipped).

## 6. Frameworks instead of a human

`Game.Tests -- --content-dir game/content --only MatchAnalysisTests` (step `game` of `scripts/test.ps1`, at 30, 60 and 120 Hz in tier 4):

- the field comparison, the charts' numbers and the export format without game data (the same bytes under three other cultures);
- a skirmish of 240 s (a human with group orders against the built-in AI with the rush profile on Alpine Assault) analysed while it runs and from its replay: the same
  export byte for byte; two analyses of the replay the same bytes in every file; every sample on the frame grid;
- what the analysis holds agrees with the match: collected equal to the score screen's resources, and the units and buildings built and lost at least the score
  screen's (which counts, since Phase 37, only the original's `SCORE` kinds - F-42), the money of the last sample equal to the
  world's, the human's commands equal to the replay's, every loss an event, no more kills than losses, the movement map counting every army unit of every sample;
- a replay whose recorded hash the match does not reach gives no analysis; a replay of another build is refused;
- the report: the same page twice, every chart and both heat maps in it, no script, no address;
- the screen in six resolutions (the score screen with its button, six pages, the computing state, the analysis of a replay, the replay list with its button) pinned
  by hashes in `tests/CnCNew.Game.Tests/baselines/analysis-screens.txt`, no two alike;
- a `ShellScript` person who plays a skirmish, opens the analysis from the score screen, turns its pages by button and key, goes back, opens the analysis of the
  recorded match from the replay list, waits for it and turns its pages.

The window check `scripts/check-replay-browser.ps1` (step `replay-browser`) opens the analysis from the score screen and from the list in the window client as well. Its
first run found a defect of the shell (`f0371ee`): only the top screen reads the shell's clock, so a row of the replay list chosen just before the analysis was opened
and chosen again right after it was closed counted as a double click and played the file. A screen shown again now takes the shell's time and forgets its last click
(`ReplayBrowserTests` has a test that fails without the fix).

**What only a person can judge** - that the report and the screen are readable, that the charts show the course of the match a person remembers, that the colours
can be told apart - is named here and in `docs/ACCEPTANCE-MACHINE.md`, never counted as passed.

## 7. Measured

Measured on one machine (AMD Ryzen 3 PRO 4350GE), 60 Hz, Release, 17 October 2026; the replays are files outside the repository (made by
`Game.Tests -- --only ReplayBrowserTests --replay-measure-minutes N --replay-measure-out FILE`, eight computer players on Twilight Flame):

| | 240 s, a human and the rush AI (the test match) | 10 game minutes, eight players | 60 game minutes, eight players |
| --- | --- | --- | --- |
| replay file | - | 287 KB | 1.67 MB |
| played again and analysed (`SimTool match-analysis`) | part of the test (30 s for its four runs) | 28.8 / 28.7 s | 135.6 / 137.6 s |
| samples / events | 92 / 10 | 968 / 648 | 5,768 / 1,503 |
| `analysis.json` | 80 KB | 689 KB | 3.04 MB |
| `samples.tsv` / `events.tsv` | - | 61 / 39 KB | 360 / 91 KB |
| `report.html` | - | 175 KB | 582 KB |
| two runs | the same bytes | the same SHA-256 of all five files | the same SHA-256 of all five files |

At the three rates: `MatchAnalysisTests` green at 30, 60 and 120 Hz; the test match ends in the same original frame within one (6,615 / 6,616 / 6,615), its samples lie
on the same frames, and its export differs between the rates (the rate is in it, and the match is the same game, not the same bits - `docs/TICKRATE-MIGRATION.md`).

**From the replay list** the screen computes the analysis 600 ticks a frame: the hour of eight players takes about as long as the tool (some 2 1/4 minutes on this
machine), the screen drawing about three frames a second meanwhile - a person would judge whether that is acceptable; the analysis is not kept beside the replay.

The window check (`check-replay-browser`, 65 s): the analysis opened from the score screen and from the list, a page turned in each.

## 8. What the analysis showed on its first matches

- **The score screen's counters** (section 1): fixed.
- **Both GLA computer players of the measured eight-player match on Twilight Flame collected nothing** (the 10-minute match of section 7): each built two
  barracks and never a supply stash, spent its 10,000 starting credits within three minutes and was defeated in the end. A behaviour of the built-in AI
  (S3-M1, `docs/SKIRMISH-AI.md`) - reported, not investigated; the survey of Phase 34 had already seen GLA win rarely.
- The colours of the players come from the factions' `PreferredColor` (`TeamColors`): in an eight-player match two blues and two reds are hard to tell apart - a person
  would judge it.

## 8a. Phase 37: the ticker reads the core, and a building keeps its place

The observer's event ticker (`MatchTicker`, docs/SPECTATOR-MODE.md §14) is this core read while a match runs: `MatchAnalyzer.EventCount`/`EventAt` give the events so
far, and `PlacePowers` gives a power aimed at a place its command's target at once instead of at the end. The core now notes where a building stands when it appears: a
building that dies and is removed within one tick has its place in the event and in the heat map of the fights (before, such a loss had no place - in the 420 s match of
`ObserverTests` all eight buildings lost). The analysis screen's map page changed with it (`baselines/analysis-screens.txt`, six hashes).

## 9. Limits and what is open

- The values are list prices; what the money really paid is not seen (section 1).
- The analysis after a match covers the match from the tick the client attached (a match continued from a save: from the save); the analysis of its replay covers it
  from tick 0.
- A match of the original AI is neither recorded nor analysed (it has no replay that plays).
- The analysis is kept in memory with the score screen; it is not written beside the replay - the list computes it again from the file.
- The modern skin of the screens is S3-M5.

## 8b. Phase 38: the header line, the chart titles, and the modern skin

Drawing the analysis screen in both skins for the readability check of `docs/UI-MODERN.md` found two defects of Phase 36, each fixed in a commit of its own with its hashes:
the screen never showed its header line - `ScoreScreen.wnd` hides `ChallengeWinLossText`, the screen set its text without showing it, so no page title, map or winner
was drawn (`7e6458c`; the tests and the window check had read the text, not the picture); and a timeline's title ran into its top axis label ("Money" over "10 000",
`5ce44a2`: the plot begins below the title line, measured with the font it is drawn in). 42 of the 54 hashes of `baselines/analysis-screens.txt` changed with them. In the
modern skin the pages are drawn with the skin's palette and type scale (`baselines/analysis-screens-modern.txt`, 54).

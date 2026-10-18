# Spectator mode: AI against AI in the window

As of: 8 October 2026. A skirmish between two computer players that runs in the client and that a human only watches – like a replay, but live.
**Built and measured without a window; neither seen nor operated by a human.** The pictures of the check script prove that something is drawn, not that it
looks right or is readable (checklist in §11).

## 1. What the mode is

- **Nobody plays.** `MatchSetup.Spectator` makes every slot a computer player (`SkirmishSlot.IsHuman = false`); the session is the same `SkirmishMatchSession` that
  `SimTool ai-match` runs without a window. A test compares the state hash of the watched match with that of the pure session of the same seed (equal after 60 s
  of game time, despite clicks, keys, speed and fog changes of the spectator).
- **The spectator writes nothing.** The command sink of the match is `SpectatorCommandSink` in spectator mode: it rejects every command without building it. Left clicks
  into the world and all keys except the spectator keys and the arrow keys do not reach the input layer of the player in the first place. `SkirmishMatchSession.RecordedCommands`
  contains after every run only commands of the system and of the computer players (test and check script).
- **Vision.** The default is the whole map without fog; switchable to the vision of player 1 or 2 (`LiveGame.ShowWorldAs`). That changes only the presentation
  (projector and fog grid), the vision of the simulation stays untouched.
- **End.** The usual winner detection of the session (`World.TryGetWinner`, every 15 original frames); after that the game stands still, and below the header bar is the winner
  (`WINNER: …`) or, after the tick budget has run out, `NO WINNER (time limit)`.

Layers: `CnCNew.Presentation/Spectator` (camera of the AI, zoom helpers; no Stride), `CnCNew.Game/Spectator` (statistics, control, HUD as a draw list; no Stride),
`CnCNew.Client.Stride` (wiring, frame cost measurement). The simulation is extended by exactly one reading access: `SkirmishMatchSession.RecordedCommands` hands out the
command list that is kept anyway. No new state, no new hash; input and reference manifest are unchanged (§12).

## 2. Start, options, keys

```powershell
pwsh scripts/demo-spectator.ps1                                   # fresh seed, one of the open pairings (§8)
pwsh scripts/demo-spectator.ps1 -Seed 7 -Map "Alpine Assault" -FactionA FactionAmerica -FactionB FactionChina
pwsh scripts/demo-spectator.ps1 -Speed 2 -Size 1920x1080 -MaxZoom 2000 -TickRate 60
pwsh scripts/check-spectator.ps1                                  # the same without a window, with checks and pictures (§12)
```

Parameters of `demo-spectator.ps1`: `-Map`, `-FactionA`, `-FactionB`, `-Seed`, `-TickRate` (30/60/120; if not given, the default of the client), `-MaxZoom`, `-Speed`,
`-Size`, `-ContentDir`, `-Mute`, `-NoBuild`. The script prints seed, map, armies, the result of the survey for the pairing and the keys.

Options of the client (together with `--match <map> --faction <A> --opponent <B>`):

| Option | Meaning |
| --- | --- |
| `--spectate` | Spectator mode: both factions are computer players, the observer HUD replaces that of the player |
| `--seed N` | Seed of the match: `AiSeed = N`, `WorldSeed = N xor C0FFEE06` – the same as `SimTool ai-match --seed N` |
| `--spectator-max-zoom H` | highest camera height above ground; if not given, the height from which the whole map is in the picture |
| `--spectator-speed X` | game speed at the start (1 = real time) |
| `--tick-rate N` | simulation rate as everywhere |

Keys (each is also on its button; `SpectatorController.KeyTable`):

| Key | Effect |
| --- | --- |
| `P` `U` `L` `E` | panel production / units / losses / economy; `Tab` next panel; `H` panels off/on |
| `F1` `F2` | follow the simulated camera of AI 1 / AI 2 (once more or `F3`: free camera; scrolling releases the following as well) |
| `J` `K` | jump once to the camera of AI 1 / AI 2 |
| `O` | overview of the whole map (once more: back to the previous view) |
| `F` | fog: everything → vision of player 1 → vision of player 2 → everything |
| Space bar, `1` `2` `4` `8` | pause, speed 1× / 2× / 4× / 8× |
| Mouse wheel | zoom, 12 % of the height per notch, up to the spectator limit |
| Arrow keys, screen edge, right drag | scroll (the camera control of the player; the step grows with the height); dragging the middle button: rotate; click on the radar: go there |
| `Esc` | quit |

**Speed and pause** are the existing switches of `LiveGame` (`Speed`, `Paused`) - since Phase 24 requests to the game's tick driver (`ITickDriver`, `docs/PRESENTATION-INPUT.md` §13.2), which the local driver grants at once: `Advance` accumulates `seconds × Speed` and catches up at most eight
original frames per call. At 60 frames per second, 8× is four original frames per frame, so within the budget; a machine that cannot manage the ticks falls
behind instead of jumping. The speed is not an input of the simulation.

## 3. The observer HUD

The model for **what** is shown is the observer interface of StarCraft II; the look is not.

- **Header bar** (top centre): both players side by side – number, faction, color (bar at the edge), money, income per minute, army value, units (of which army),
  power produced/consumed (red on shortage); between them game time, speed and fog vision. More than two players appear as rows below.
- **Panel** (top left), four tabs with key hint: production (per building the head of the queue with progress and `+N` for further ones, construction sites with
  progress), units (army by type with count and value), losses (kills and their value, lost units and buildings by type and value), economy.
- **Radar** (bottom left): the minimap of the client; on it one frame per AI in its color – the ground section that a player would see at default zoom at the place
  the simulated camera is currently looking at (thicker when the main view follows it). The white frame is the main camera.
- **Button bar** (bottom centre, where a player has the command bar): tabs, follow AI 1/2, free camera, overview, fog, pause, speed. Every button is clickable
  and carries its key.
- **In the world**: a diamond in the color of the player where its simulated camera is looking, with the kind of the focus point (`AI 1: Construction`).

**Placeholders.** Everything except the little pictures of the objects is a placeholder: flat rectangles, one font (Arial), English labels, no artwork of the
observer command bar of the original (`ControlBar.wnd` has observer windows; they are not used here). The little pictures are the button images of the original
command buttons (`CommandButton … ButtonImage` of the button that builds the object); an object without such a button appears only with its template name. Names are
template names (`AmericaInfantryRanger`), not localised display names. The area outside the map is the clear color of the renderer in the overview.

## 4. Where every value comes from

`SpectatorStats` (game layer) collects from the events of every tick what the simulation does not keep itself, and reads the rest from the world. No wall time; time is
the tick with the rate of the world (`world.Time.TicksPerSecond`). Fixed order (sorted tables), so the same panels in every run of the same match (test).

| Value | Source |
| --- | --- |
| Money | `world.MoneyOf(player)`, whole credits |
| Income per minute | sum of the `SupplyDeposited` events of the last 60 **game** seconds (`IncomeWindow`: one bucket per game second; in the first minute the sum so far, no extrapolation). Credited to the owner of the supply center |
| collected in total | sum of all `SupplyDeposited` |
| Supply vehicles | living own objects with `KindOf HARVESTER` or a supply module (`SupplyTruckAIUpdate`, `WorkerAIUpdate`) |
| Supply centers | finished own buildings with `SupplyCenterDockUpdate` |
| Power | like the command bar (`CommandBarModel.Hud`): consumption = negative `EnergyProduction` of the finished own buildings; production = `world.EnergyProductionOf` + consumption |
| Units / army / workers | living own objects: `STRUCTURE` → building; `PROJECTILE` → does not count; `DOZER` or `HARVESTER` → worker; otherwise `INFANTRY`/`VEHICLE`/`AIRCRAFT` → army (`SpectatorStats.Classify`) |
| Army value, value per type | sum of the `BuildCost` of the templates |
| Production | per finished building `ProductionUpdate.Queue`: head of the queue, progress `(BuildTicks − RemainingTicks) / BuildTicks`, length |
| Construction | own buildings with status `UnderConstruction`: `ConstructionWork / BuildTicks` |
| Losses | `EntityDied`, once per object, by class and template, value = `BuildCost`. Owner and template from the world, alternatively from the note at `EntitySpawned` (an object that dies and disappears in the same tick) |
| Kills | the same events: the owner of the causer (`SimEvent.Other`) if it is another player (`Object::scoreTheKill`) |
| Buildings built | `ConstructionCompleted` |
| Winner | `SkirmishMatchSession.Winner` |

The panels are rebuilt every five original frames (`PanelIntervalFrames`), not per frame.

**Rates.** `IncomeWindow` yields the same number for the same deposits at the same game time at 30, 60 and 120 ticks per second (test without game data). With real
data a test checks at 30 **and** 60 Hz that the displayed income equals the sum of the last game minute formed independently from the events and that the
panel counts game seconds. The matches themselves run differently at 30 and 60 Hz (expected until Phase 23): in the test run player 2 collects nothing after 170 s at 30 Hz,
3,900 credits at 60 Hz.

## 5. The "camera" of the AI

A computer player has no camera. `AiCamera` (`CnCNew.Presentation/Spectator`) answers instead: *Where would a player have to look in order to give these commands?*

**Source.** After every tick `SpectatorController` reads the entries of `SkirmishMatchSession.RecordedCommands` added since the last tick (the list from which
`ToReplay` builds the replay). The simulation reports every rejection as `CommandRejected` with player and command type; if in a tick **all** commands of one type of one
player are rejected, they do not count (the AI repeats rejected commands every time it thinks – otherwise a special power that is not ready constantly pulled the camera to a
place where nothing happens). If only some are rejected, the event does not say which; then all remain.

**Focus point per command** (heaviest last; per tick the heaviest counts):

| Kind | Command | Place | Hold time |
| --- | --- | --- | --- |
| `Supply` | `DockCommand` | the dock | 2 s |
| `Move` | `SetMoveTargetCommand`, `MoveAndEnterCommand`, `EvacuateCommand`, fewer than three attack moves | target or object or centre of the units | 2 s |
| `Research` | `PurchaseUpgradeCommand` at a building | the building | 2 s |
| `Production` | `QueueProductionCommand` | the production building | 3 s |
| `Upkeep` | `ResumeConstructCommand`, `RepairCommand`, `CaptureCommand`, `SellCommand` | the building | 3 s |
| `Construction` | `ConstructCommand` | the building site | 4 s |
| `Power` | `UseSpecialPowerCommand` | target location, target object or the source | 5 s |
| `Attack` | three or more `AttackMoveCommand` in one tick (a wave) | the **centre of the commanded units**, anew per tick, as long as they live | 6 s |

A science purchase has no place and moves nothing. The supply center appears as a building site (when it is placed) and as a dock.

**Switching.** A new focus point takes over if it is heavier than the current one, if the current one has had its hold time, or if it continues it (the same wave,
the same building – then without a new hold time, so that a long attack does not displace everything else forever). Otherwise it is discarded.

**Smoothing.** The view position glides exponentially towards the target per tick (time constant 15 original frames = 0.5 s); if the target is farther than 600 units away,
it cuts (a player would press a key or click on the radar). All times are original frames, converted with the ticks per original frame of the world: the same
commands at the same game time give, at 30, 60 and 120 Hz, the same switching time and the same position up to the path of one original frame (test). The
main view that follows an AI camera glides after it on frame time, so that it does not jerk in the tick rhythm.

**Both cameras at once: frames on the radar instead of picture-in-picture.** What is built is the second of the two possibilities of the brief: both AI cameras as coloured frames
on the radar and as marks in the world, plus following (`F1`/`F2`) and jumping (`J`/`K`). A second and third 3D window cannot be had cheaply in this
renderer: `LiveMatchHost` has one camera in one compositor slot, and the world layers prepare **one** view per frame – `TerrainLayer` switches its chunks by
`RtsCamera.VisibleChunks` of the one camera, `ObjectLayerCore` selects and discards instances against its frustum and fills its buffers for that, `FxLayer` likewise. Two
further views would mean rebuilding `IWorldLayer` for several cameras and filling the object layer three times per frame; the measurement in §7 (drawing is the part
that grows with the number of objects in the picture) speaks against doing that in passing. It is possible as a task of its own, not as part of this one.

## 6. Camera and far zoom

- `RtsCamera.SetMaxHeightAboveGround` raises the upper limit of the camera height for the spectator (never below `MaxCameraHeight` of the game data, 310); the camera of a
  player is unchanged (test). The default limit is `SpectatorCamera.OverviewHeight`: the smallest height from which, at 70° pitch, all four corners of the map boundary are in the picture with
  3 % margin (bisection on a probe camera). Alpine Assault at 1280×720: **3,275** compared with 310.
- `O` sets 70° pitch, this height and the map centre and remembers the previous view.
- The scroll step grows with the height (otherwise the view would crawl), and the mouse wheel changes the height by 12 % per notch instead of by a fixed 10 units.

**What the renderers do in the distance** (read and looked at in the pictures of the check script):

- *Far plane:* `RtsCamera.BuildMatrices` puts it behind the farthest corner of the height field; that holds from above as well (test "the far camera still sees the whole height field").
- *Terrain:* selection of the chunks only by the frustum, no distance limit; in the overview all chunks of the playable map are on (test), on average 132 instead of 8.
- *Fog/shroud:* one texture with one texel per cell for the whole map, independent of the camera; "everything" sets all cells to visible.
- *Objects:* only frustum selection, no distance limit and no detail levels – in the overview every object of the map is drawn (455 instead of 25).
- *Effects:* frustum selection; nothing conspicuous.
- *Camera constraint:* `W3DView::calcCameraConstraints` holds the look-at point in the map centre as soon as the edge of the picture is wider than half the map. Far up
  it is therefore no longer possible to scroll – left that way intentionally (one sees everything anyway).

One thing was **fixed** in the process: when leaving the overview, the zoom has to be set first and the look-at point restored afterwards, otherwise the constraint of the
still distant camera holds it in the map centre. Otherwise nothing is broken in the pictures; whether textures flicker in the distance can only be seen by a human in the running picture.

## 7. Frame cost, measured

`scripts/check-spectator.ps1`, Alpine Assault, America against China, seed 7, 1280×720, smoke test (every frame computes 1/30 s, the match runs at speed 3 to 8 meanwhile),
this machine, Release, two runs (the first with start speed 4, the second with 3). Measured is the **CPU time of the client** per frame with a stopwatch around `Update`
(simulation, presentation, layers) and around `Draw` (handing over the scene and HUD); what the graphics card needs afterwards is not included. "Far" is every height above
twice the player limit; in both runs those are the 699 frames of the overview.

| | Run | Frames | Update median | Draw median | Update 95 % | Draw 95 % | Objects drawn | Triangles | Terrain chunks |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| near (player zoom) | 1 | 2,309 | 5.1 ms | 3.6 ms | 49.9 ms | 26.8 ms | 25 | 5,055 | 8 |
| near (player zoom) | 2 | 2,309 | 5.9 ms | 5.2 ms | 64.8 ms | 42.9 ms | 27 | 5,205 | 9 |
| far (overview) | 1 | 699 | 7.7 ms | 19.4 ms | 27.6 ms | 49.9 ms | 455 | 34,684 | 132 |
| far (overview) | 2 | 699 | 7.8 ms | 13.4 ms | 21.3 ms | 36.8 ms | 433 | 33,070 | 132 |

Reading: drawing costs two and a half to five times as much in the overview as at player zoom, Update and Draw together 21 to 27 ms per frame in the median (so about 35 to
45 frames per second in this measurement; near, above 90). The triangle count is small; what is expensive is the number of individually submitted objects (over 400 instead of about 25). The
two runs scatter by a third, and the means lie above the medians (far 17 and 42 ms Draw), because individual frames load models or compute several
ticks – the numbers are an order of magnitude, not a measurement series. Not measured: the graphics card, other maps, matches with eight players, a quiet machine.

## 8. The outcome must not be predictable

**How the original brings randomness into a match** (Zero Hour sources under `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/`):

- *The seed.* `GameClient/GUI/GUICallbacks/Menus/SkirmishGameOptionsMenu.cpp` sets `TheSkirmishGameInfo->setSeed(GetTickCount())` when the skirmish menu is opened (line
  1351) – the milliseconds since the start of Windows – and passes it to `InitGameLogicRandom` at the start (`reallyDoStart`, line 434; `Common/RandomValue.cpp`).
  A replay stores it and sets it again (`Common/Recorder.cpp`). The original is therefore deterministic like us; **the randomness is the seed.**
- *Where the AI rolls dice* (`GameLogicRandomValue`): `AIPlayer::selectTeamToBuild` (`GameLogic/AI/AIPlayer.cpp`, line 1702) chooses uniformly among the buildable teams
  with the highest priority; the priority rises and falls with success and failure of the team (`Common/RTS/Team.cpp`). `AIPlayer::doUpgradesAndSkills` (line 2969)
  draws once a `SkillSet` of the side (`AIData.ini`) and buys its sciences in order. `AIPlayer::computeSuperweaponTarget` (line 1202) rolls the
  search direction, `AISkirmishPlayer::computeSuperweaponTarget` (`AISkirmishPlayer.cpp`, line 1145) the approach path (centre, flank, back door) for cluster mines. In addition
  random delays per unit in `AIGroup.cpp`, the target choice on "easy" and branches at waypoints in `AIStates.cpp`, random script timers in
  `ScriptEngine::setTimer`. There is **no** randomness in the choice of the enemy (`AISkirmishPlayer::acquireEnemy`: the nearest base) and in the build list
  (`AISkirmishPlayer::adjustBuildList`: the `SkirmishBuildList` of the side; `RotateSkirmishBases` is `No` in the data).
- *Scripts.* The skirmish AI of the original is above all script-driven: if the map has no scripts of its own, `GameLogic/Map/SidesList.cpp` loads
  `Data\Scripts\SkirmishScripts.scb`, `Player::initFromDict` (`Common/RTS/Player.cpp`) copies scripts and teams per computer player. Which random actions the file
  contains has not been evaluated here.

**How we do it.** `SkirmishMatchOptions.AiSeed` and `WorldSeed`; every AI receives `AiSeed + Slot × 7919` (`SkirmishMatchSession.Start`). `SkirmishAi` has one stream
(`_random`) with two consumers: the start angle of the placement search per ring (`FindPlacement`) and the uniform choice of the next army unit among the
affordable ones with fulfilled prerequisites (`ProduceArmy`). The world seed feeds the random stream of the world. `SkirmishScripts.scb` is not executed.
Without a seed given, the seeds are fixed (`SkirmishMatchOptions.Standard`): **every match of the same pairing would run the same.**

**What the demo script does.** It draws a seed per start with `Get-Random` (random source of the system, in the script, never in the simulation), prints it and takes
`-Seed N` for exact repetition. The client seed is the same as with `SimTool ai-match --seed N`. Without a pairing given, the script chooses one of the pairings whose
outcome was open in the survey (320 matches, 20 seeds per pairing, 30 Hz) – victories of the first / of the second faction / undecided:

| Map | Pairing | Result |
| --- | --- | --- |
| Armored Fury | China – GLA | 13 / 4 / 3 |
| Bitter Winter | China – GLA | 6 / 14 / 0 |
| Armored Fury | America – China | 8 / 3 / 9 |
| Alpine Assault | America – China | 12 / 1 / 7 |
| Death Valley | ChinaTank – AirForce | 4 / 3 / 13 |

The survey comes from the brief and has not been repeated here; measured is one match (Alpine Assault, America – China, seed 7): America wins at tick
8,910, final hash `D53EDB8AFD67B61B` after 9,211 ticks – the same in `SimTool ai-match --seed 7` and in the client as spectator (smoke test with speed changes, fog switch and
clicks). "Undecided" means: no winner within the tick budget – in the window that would be one hour of game time.

**Proposal, not built** (changes the behaviour of the simulation and thus every reference value of the AI runs; decision of the project owner): closest to the original
and most visible in the picture would be two additional rolls from the existing stream of the AI – (a) **the approach path per wave** (centre, flank or back door, like
`doTeamFollowSkirmishApproachPath` with the waypoints of the map; today every unit of a wave gets an `AttackMoveCommand` to the same target point, there is no choice
of the path), and (b) **the choice of a `SkillSet`** of the side at the beginning instead of the fixed rule "the cheapest purchasable science"
(`SkirmishAi.BuyScience`). Both are single rolls per wave or per match, no new state except the chosen value. It is not needed for
the goal of this demo: the survey shows that the outcome already changes with the seed alone.

## 9. Difficulty levels of the AI

**With us: no.** `SkirmishAi` knows no difficulty (`SkirmishAiOptions` has only `AttackThreshold`, `MaxAttackThreshold`, `EconomyFirst`); there is one
computer player, which corresponds to `DIFFICULTY_NORMAL` (`docs/SIMULATION-CORE.md` §35: "none of the effects verified in the data applies to a computer player in the
skirmish"). The shell accordingly offers one computer army (`GUI:MediumAI`, `ShellSkirmishText.ComputerArmy`); `SimTool ai-match` has no option for it.

**In the original: three levels** – "Easy Army", "Medium Army", "Hard Army" (`SLOT_EASY_AI`, `SLOT_MED_AI`, `SLOT_BRUTAL_AI`; the latter is `DIFFICULTY_HARD`, there is no
fourth level). `GameLogic::startNewGame` (`GameLogic/System/GameLogic.cpp`, lines 1429–1431) writes the level as `skirmishDifficulty` into the player data,
`Player::initFromDict` passes it to the AI. What differs:

- **Scripts and teams per level – the actual lever.** `ScriptEngine::executeScript` (`GameLogic/ScriptEngine/ScriptEngine.cpp`, lines 6964–6966) executes a script
  only if its switch `isEasy`/`isNormal`/`isHard` matches the level of the player; `TeamPrototype::evaluateProductionCondition` (`Common/RTS/Team.cpp`, from line
  1156) filters the production conditions of the teams in the same way. Which teams and attacks a level has is thus in `SkirmishScripts.scb`, not in the code.
- **Collectors:** `AIPlayer::checkForSupplyCenter` (`AIPlayer.cpp`, lines 190–220) takes `ResourceGatherersEasy/Normal/Hard` of the side plus one. In the shipped
  data the three values per side are equal (2 for USA and China, 5 for the GLA) – without effect.
- **Target choice:** on "easy" a team gets no common target (`Team::setTeamTargetObject`, `Team.cpp` line 1510) and an attack squad chooses its victim
  at random (`AIAttackSquadState::chooseVictim`, `AIStates.cpp` from line 5970); "normal" and "hard" have rules of their own.
- **Not per level:** the time values of `AIData.ini` (`StructureSeconds 0`, `TeamSeconds 10`, `Wealthy 7000`, `Poor 2000`, `StructuresWealthyRate`/`TeamsWealthyRate
  2.0`, `…PoorRate 0.6`) apply to all.
- **No money or build speed bonus in the skirmish.** The bonuses per level (`WeaponBonus SOLO_AI_EASY/NORMAL/HARD RATE_OF_FIRE` 80/100/120 %,
  `HumanSoloPlayerHealthBonus_Easy/Normal/Hard` 150/100/80 %) are applied by `Player::friend_applyDifficultyBonusesForObject` only in the single-player mode of the campaign
  (`isInSinglePlayerGame`). A money or production cost bonus per level was not found in `Player.cpp`.

Levels with us would therefore mean above all: executing the skirmish scripts. That is not part of this task.

## 10. Limits

- No human has seen or operated the mode. Readability, colors, font sizes, the feel of the camera and of the following are unassessed.
- Income counts only deposits of supplies. Hackers, black markets, oil derricks, sales and crates produce no `SupplyDeposited` and are missing.
- The production panel shows units and construction sites, no running upgrades and sciences.
- The spectator cannot select or inspect anything (no health bars, no unit info).
- Sound: Eva and the music still belong to player 1 (the HUD of the player runs along invisibly, because sound and input script depend on it).
- When switching the fog vision, the presentation forgets what the previous vision had remembered (ghost images of buildings seen); explored scenery is re-read from the
  vision map.
- The header bar shows two players side by side, further ones as rows; following and jumping exist for AI 1 and 2.
- If only some commands of one type are rejected in a tick, all count for the AI camera (§5).
- No picture-in-picture (§5). Far up it is not possible to scroll (§6). Outside the map the picture is a single color.
- No entry in the shell: the mode starts only through the command line or the script. No saving, no replay export from the window.
- At 60 and 120 Hz the mode runs (test at 60 Hz; a smoke test with `-TickRate 60`), but the course of a match is a different one there than at 30 Hz, and the
  survey of the pairings applies to 30 Hz.

## 11. Checklist for a human

Start: `pwsh scripts/demo-spectator.ps1 -Seed 7 -Map "Alpine Assault" -FactionA FactionAmerica -FactionB FactionChina -Speed 2` (about five minutes of game time until the victory).

1. **Start.** The window shows the base of player 1 without fog; header bar with both factions, money 10,000; the game time runs. Is everything readable (size, contrast)?
2. **Panels.** Press `P`, `U`, `L`, `E`: does the tab change, is the highlighting right, do the little pictures match the names, do the progress bars run?
3. **Zoom.** Mouse wheel backwards: does it go far beyond the usual zoom, does the terrain flicker, are objects or tiles missing, does it stutter?
4. **Overview.** `O`: whole map in the picture? `O` once more: back to the same place?
5. **Fog.** `F` three times: everything → only vision of player 1 → only vision of player 2 → everything. Do radar and world agree?
6. **AI camera.** `F1`: does the view follow the blue frame of the radar, and does one see there what the AI is currently doing (building site, barracks, the wave)? Does the following
   seem calm or does it jump too often? `F2` for the other AI, `J`/`K` to jump, an arrow key releases the following.
7. **Speed.** The space bar stops (PAUSED in the middle), `4` and `8` accelerate, `1` normal. Does the picture stay smooth at 8×?
8. **Buttons.** Click every button of the lower bar once; a click on the radar moves the view.
9. **Command nothing.** Left and right click on units and ground: nothing may be selected and nothing may happen.
10. **End.** Wait until the victory: does `WINNER: …` appear, does the game stand still, do the losses of the panel agree with what was seen?
11. **Randomness.** Start twice without `-Seed`: different seeds, a different course? Repeat once with the printed seed: the same course?

## 12. Tests and checks

- `tests/CnCNew.Presentation.Tests/SpectatorTests.cs` (without game data): zoom limit of the player and of the spectator, far plane, overview height, focus point per command,
  weight and hold time, wave with a centre that moves along, gliding and cut, same camera at 30/60/120 Hz.
- `tests/CnCNew.Game.Tests/SpectatorTests.cs` (`--only SpectatorTests`): income per game minute at three rates; keys and buttons; with game data: two runs
  of the same match deliver the same report (panels, cameras, hash) and the panels have data; nothing from the spectator reaches the simulation (hash equal to the pure
  session, command list of equal length, sink rejects); fog switch; overview, limits, following; draw list at three resolutions and click on buttons; income against
  the events at 30 and 60 Hz.
- `scripts/check-spectator.ps1`: the real client without a window with `scripts/input/spectator.txt`; checks on the output (lines `spectator …`) that the match ran,
  both AIs commanded, no spectator command was recorded, all panels, fog visions and speeds were on, the AI cameras found focus points and the camera reached the
  overview height; stores ten named PNGs under `%TEMP%\cncnew-spectator-check`.
- Unchanged: `SimTool input-manifest check` and `reference-manifest check --set quick` (the simulation computes nothing differently), the golden hashes of the existing screens.

## 13. The playback of a replay (Phase 35)

A replay is played in this mode (`docs/REPLAY-BROWSER.md` §5): the same observer HUD, the same keys, and the fog switch `F` runs through every player of the recording.
When the controller shows a playback (`SpectatorController.Replay`) the button bar has a third row - Start (`Home`), << Back (`PageUp`), Next >> (`PageDown`) - and the
replay's line: where the playback stands in the recording, the checkpoint, how the recording ended and how many recorded hashes matched. A seek is asked for by the
controller (`SeekRequest`) and carried out by the host, which builds the scene again for the playback's new presentation and gives it a new controller that takes over the
view of the old one (`TakeViewFrom`: tab, panels, camera, followed camera, fog, speed, pause). Escape in a playback of the menus leads back to the replay list. Not seen by a
human.

## 14. The observer features of S3-M2 (Phase 37)

**The views.** `F4` shows everything, `F5` to `F12` the view of player 1 to 8 (their fog; a number the match does not have changes nothing); a row of small buttons above
the button bar does the same ("All", "P1" ... with the player's colour). `F` still cycles. The view is a matter of the presentation (`LiveGame.ShowWorldAs`), never of the
simulation. A seek of a replay keeps it (`TakeViewFrom`), with the tab and the ticker's switch. The free camera is the one of Phase 23 (`F3`, scrolling, the radar).

**The research tab** (`R`): per player the upgrades it holds (player-wide, in the catalogue's order; the engine's veterancy upgrades left out), how many upgrades its objects
hold, its sciences - read from the world (`SimulationWorld.PlayerUpgrades`, `Entity.CompletedUpgrades`, `PlayerSciences`). Nothing is shown as "in research": the
simulation completes an upgrade when it is bought (`SimulationWorld.CompleteUpgrade`), where the original takes its build time in the production queue
(`ProductionUpdate::queueUpgrade`) - fidelity register F-43; showing a research in progress needs that change of the simulation first. The production tab (`P`) shows the
queues and constructions as before.

**The event ticker** (`MatchTicker`, top right under the bar): the moments that matter, from the core of the match analysis (`MatchAnalyzer`, docs/MATCH-ANALYSIS.md)
read while the match runs - the **first blood** (the first unit or building of a player killed by another player), a **building lost** (and to whom), a **superweapon**
fired (a power of a building of the kind `FS_SUPERWEAPON`; the place is its target), a building **captured** (from whom), a player **defeated**. Each line has its game time
(a frame of the original) and, but for a defeat, its place: a click on a line, or `G` for the latest one, moves the camera there (free camera, the overview ends); `T` hides
the ticker. The HUD shows the latest six lines up to the frame the match stands at.

- A playback owns its ticker (`ReplayPlayback.Ticker`): it is told every tick the playback steps - the ticks a seek steps without drawing as well - and only ticks it has
  not seen. A seek back therefore shows exactly the lines up to the frame sought, a seek forward the lines up to there, and no line twice: the lines are a function of the
  match, not of the viewer's seeking.
- A building that dies and is removed within one tick still has its place: the analysis core notes where a building stands when it appears (Phase 37; it also puts these
  losses into the analysis' heat map of the fights).
- A match of the original AI gets a ticker as well (the core reads the world and the events, not the session) - built for it, not tested with it.

**Measured** (`ObserverTests`, a match of two built-in computer players with the rush profile on Alpine Assault, seed 11, 420 s): 9 lines - the first blood at 1:40,
then eight buildings of America lost to China between 3:22 and 4:44, all with places; the same lines on two runs; **the same nine lines at 30, 60 and 120 Hz, in the same
frames**; the replay of the match has the same ticker, and after seeks to 300, 60, 390, 30, 0 and 210 s it shows the lines of the straight playback up to there. The HUD
is pinned in six resolutions for eight states (the five tabs, a player's view, after a jump to an event, the ticker off) at each of the three rates: 144 hashes in
`tests/CnCNew.Game.Tests/baselines/observer-screens.txt` (per rate, because the match is the same game at every rate, not the same bits), no two alike at a rate. A
`ShellScript` person opens the replay from the list, chooses the view of player 1 and the research tab, plays at 8x until the ticker has two lines, goes to the latest
event, seeks back and finds the view and the tab kept, chooses everything again and leaves. The window check of the replay browser chooses the view of player 1 and the
research tab in the real client and finds them kept across its three seeks.

**What only a human can judge** (S3-M2's acceptance): whether a person names the players' intentions from the display alone; whether the ticker's words, the view
buttons and the research tab are readable; whether a jump to an event shows what happened there. Not built: following or jumping to the simulated camera of players
3 to 8 (`F1`/`F2`, `J`/`K` serve the first two); a picture-in-picture view; the network spectator beyond Phases 27 and 29; localised names (template names, as before).

## 15. The observer in the modern skin (Phase 38)

The observer HUD (`SpectatorHud`) is drawn in the skin of its `MatchController` (`docs/UI-MODERN.md`): in the original skin with the colours and fonts it always had (the 144
hashes of `baselines/observer-screens.txt` unchanged), in the modern skin with the palette (panel, border, text, muted, accent, good, bad) and the type scale (Segoe UI;
caption, body, control and title steps scaled with the viewport) and no drop shadow. The same eight states at 30, 60 and 120 Hz are pinned in the modern skin
(`baselines/observer-screens-modern.txt`, 144). A replay's observer takes the skin of the menus that opened it. Not judged by a human: whether the modern panels read
better over the battlefield.

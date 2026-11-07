# The owner's first acceptance: what it found

The report of 1 November 2026 (commit `d3451bb76e`, the second PC with sound, 60 Hz): 115 goals, 79 passed, 21 failed, 8 skipped, 7 open.
Every finding below is fixed or named as a limit. `scripts/acceptance/accepted.json` holds what is settled, and the suite does not ask it again (`docs/ACCEPTANCE.md`, "What is asked again").

The causes are worth reading together: most of them are places where the port was right on paper and no check looked at the same thing a person did.

## Effects and projectiles

| Goal | What the owner saw | Cause | Fix |
|---|---|---|---|
| UNIT-11, FX-03, FX-04, FX-05, SND-02, UNIT-12 | The muzzle flash and the flame keep to the hull, not the turret, and not to the barrel's height | `AttachedEffects` read a bone from the rest pose of the model and moved it by the object only | `b620c178`: `ModelBoneTable.FindPosed` with the turret's angles; the fire effect gets the bone's matrix |
| UNIT-12, FX-04 | The Tomahawk drives on the ground | See `docs/SIMULATION-CORE.md` (F-96 to F-98) | `0f1eb5f4`: a missile flies its thrust locomotor: it climbs, cruises and comes down |
| FX-05 | The rocket trooper's rocket starts at 0 degrees and steers hard to the target | See F-96 to F-98 | `ef6a2f61`, `736c3808`: a projectile starts at its launch bone with the turret's heading; a unit without a turret turns to its victim before it fires |
| FX-02 | Casings jump far too high | `VelOrthoZ = 7.00 2.00` in the data: the original returns the upper value without a draw when the range is inverted, the port drew between the two | `c2c5bcc5`; 377 ranges of `ParticleSystem.ini` have this shape |

## Missions

| Goal | What the owner saw | Cause | Fix |
|---|---|---|---|
| MISS-02, SND-05 | Eva says "upgrade complete" when a building is ready | The patch grants an upgrade to every new building, and the cue took any upgrade for a research | `c280c5b6`: the event names its producer, and only a research is announced |
| SND-06, MISS-02, MISS-01 | The intro movie of a mission is "way off", no camera travel | The camera clamped or lost most of `W3DView`'s script camera: the ground level, the zoom, the look, the ease times | `678e2ccd`: `ScriptedCamera`, checked against the original's formula and `PresentationTool camera-script` |
| MISS-02 | The picture stays dark after a fade | A finished fade stayed at its minimum | `e5c6a5ec` |
| MISS-07 | Grey-scale does nothing | The swizzle put red into alpha too, so the additive blend gave red squared | `7c8bbaa4`: the original's luminance, in a shader compiled at run time |
| RATE-04 | The script does not seem to run properly | A mission started from the menu had a presentation latency of 0: `CAMERA_MOVEMENT_FINISHED` was true at once, in the way a person starts a mission and in no check | `9f9279a5` |
| MISS-05 | The movie after choosing a difficulty does not play | It went under the modern skin's canvas | `48987f0b` |
| MISS-09 | The briefing history looks strange | Its captions did not wrap | `1cc20e49` |

## Input and the command bar

| Goal | What the owner saw | Cause | Fix |
|---|---|---|---|
| INPUT-01 | Right click sets a rally point; the marker is a yellow polygon | The patch gives units an upgrade queue, and "has a queue" was the test for a factory | `ad5f8ad2`: `AUTO_RALLYPOINT`, as `InGameUI`; the marker is the original's model |
| HUD-03, INPUT-04, SHELL-03 | The build sound plays only on a click | Hot keys took another path | `ad5f8ad2`: `GUIClick` or `GUIClickDisabled` |
| SHELL-03 | What cannot be built yet is not grey | Cameos were laid over the button in full colour | `4c95134b` |
| HUD-02 | The tooltip rebuilds with every pixel | Every movement restarted the delay | `82f02f3c` |
| HUD-07 | The radar's frame lags | It was part of a picture composed at 4 Hz | `0a06e771` |
| INPUT-05 | The selection box shows while a building turns | Any drag drew it | `06d315d5` |
| INPUT-03 | More room to zoom out | The owner's wish | `6196b5a7`: half as far again |
| HUD-09 | A remote address cannot be typed | The client never switched on text input; scripts type characters directly, so no check saw it | `016fa10f` |

## Skins and menus

| Goal | What the owner saw | Fix |
|---|---|---|
| SKIN-02, HUD-06 | The bar has black rectangles left and right | `bdde606c`: `ModernControlBar`, a band at every aspect ratio, no scheme art |
| SHELL-12, SHELL-03, SKIN-01, SKIRM-07, SHELL-04, SHELL-06 | No transparency, no real columns, odd sliders, uneven icons | `2f83fe21`: translucent panels over the shell map, flat gadgets, screens that stay together |
| SKIN-03 | The main menu's buttons stay under the options; the classic combo box is drawn wrongly | `b81f0555` |
| SHELL-03, REPLAY-01 | No tactical analysis, the analysis is not pretty | `3e195387` |

## Motion and the computer player

| Goal | What the owner saw | Cause | Fix |
|---|---|---|---|
| UNIT-02 | Tanks behind infantry stutter | A step that landed in a soldier's circle turned 45 degrees, and the path pulled it back | `4d4aa4f8`: the original's blocked movers, once per frame |
| UNIT-19 | A jet lands with a hard vertical cut | The height was a straight line, and `Lift` read as 0 for every locomotor | `17532afa`, `6164c38b` |
| SPEC-08 | The computer builds its supply centre inside the supply | Two boxes laid across each other have no corner inside the other, and the port lacked the original's preflight | `315ee20d`. It costs the computer players income: 8 of the 16 pairings end within 25 minutes, 12 before, and 15 on the same state without this fix (`docs/SIMULATION-CORE.md` §77) |
| UNIT-10 | The tank starts inside the ramp | The unit took the factory's height (`35d43449`, fixed); the rest is the original, which never reads a model for a height: the treads sink into the ramp by up to 4 units | Fixed in part, the rest a limit |

## The suite and the machine

| Goal | What the owner saw | Fix |
|---|---|---|
| UNIT-18, FX-06, SND-04, WORLD-11 | A scene needs too much work, or ends too early, or starts away from its subject | `e3b6ca92`: scenes that are ready at once |
| Many | Waiting for buildings in the acceptance | `06a91598`: the rule `quick-build`, four times as fast, in the acceptance only |
| WORLD-06 | Micro lags in a match | `19703f60`: a model read when its object first appeared stopped that frame for 15 to 40 ms. The models of the sides that play are read before the match, as `GameClient::preloadAssets` does, and the connection of a match of a server is kept alive meanwhile. `--frame-report` says what a long frame did |
| SKIRM-08 | Reading the folder of maps takes long | `bd50b6bb`: the maps are read side by side |

After the report, found by `-Probe` while its fixes were checked: a mission's window came after 46 s on this machine, because the preload of WORLD-06 took a mission's civilian player for an army and read every prop of the data. `PreloadedModels` reads what stands on the map and the armies of the sides that play, 2,285 instead of 6,032 models, and the window comes after 33 s. A probe that took its picture before the window existed wrote a black pixel and said "runs"; it waits for the window now.

## Limits, not findings

- **WORLD-08**, softer shadows: the original's stencil shadows have a hard edge, and so do these. A soft edge would be a feature of its own.
- **WORLD-05**, the Crusader in the model viewer: a limit of the viewer, known since Phase 30. In a match the turret sits where it belongs.
- **UNIT-06**, riders start in the middle of the vehicle: the bones `EXITSTART01` to `03` lie inside the hull, and `exitObjectViaDoor` sets the rider there. That is the original.
- **SHELL-01**, the Chinook's door in the shell map: no draw module of a Chinook has a door state. The patch's `AVChinookAG` has a mesh `BUNKER01` beside the fuselage that nothing hides, and it reads as an open door. Judged from the source and the data, not against a picture of the original.
- **MISS-04**, an uncovered map and GLA voices in `GC_ChemGeneral`: both are the map's. `CINE_Intro` says `MAP_REVEAL_ALL_PERM` for the player, `CINE_END` takes it back, and the map then stays explored and dark beyond what the units see, as `undoRevealMapForPlayerPermanently` leaves it. The voices are the enemy general's taunts (`Taunts_Toxin`) in the same scripts. `CampaignShellTests` holds the two states. `260a7438` was first written down as the fix of this goal, and it is not: this map marks its own side, and `ChallengeSides.OfDirectStart` matters on `GC_LaserGeneral`, which marks nobody. Tier 4 found that out, because the test written with it asserted the wrong thing.
- **The briefing movie at 16:9** is stretched to the window, as `W3DGameWinDefaultDraw` does, so its globe is oval.

## The owner's playtest of 3 November 2026

A skirmish played on `8e6655d1`, nine findings. Each is reproduced by a program, fixed and pinned by a test on real data (`PlaytestHudTests`, `ShellTests`, `RadiusCursorTests`, `UiRenderingTests`); what only a person can judge is named.

| # | What the owner saw | Cause | Fix |
|---|---|---|---|
| 1 | The game stops for a moment after the intro before the menu stands | The scene behind the menus was built when the menu was reached, and its first picture made every shader of the scene and the menu: one frame of 6 s on this machine (`--frame-report`). Its load read every army of the map's sides, 1,862 models, though nobody builds there | `2c009dd7`: the frame before the intro's first picture builds the scene, lays it out, steps it one original frame and draws it and the menu under black, as the original loads the shell before its movies; the scene reads what stands on its map (`LiveGame.PreloadsArmies`; the original preloads nothing unless `m_preloadAssets`). The longest frame after the intro: 99 ms. The window stays black for those seconds before the intro instead |
| 2 | The game speed of the skirmish setup stands at its start, does nothing and shows no number | The slider was not wired | `d89345c5`: `SkirmishGameOptionsMenu`'s range 15 to 61 and its number (`setFPSTextBox`, "--" above 60), kept in the settings (schema 5); a match of this machine alone runs that much faster or slower |
| 3 | The percent of a building under construction is cut off in the middle | The label's clip was the height of the smaller font | `913d132b` |
| 4 | The picture before a match is very bright and looks wrong | The wrong screen: `SinglePlayerLoadScreen` with the campaign's page, where `GameLogic::getLoadScreen` gives a skirmish `MultiPlayerLoadScreen` | `3a346c49`: the dark skirmish background, the players in their house colours, no bar for a computer player, the local general's portrait, features and name (`MultiPlayerLoadScreen::init`) |
| 5 | A dozer at the edge of a barracks cannot be clicked | The pick took the first object whose circle held the point | `75f25821`: the ray of the view against each object's geometry, the nearest hit (`W3DView::pickDrawable`) |
| 6 | The general's window has the wrong skin, no transparency, no mark on what was bought, no grey without points | The modern skin drew it flat | `e2a8b838`: its original art in both skins, an owned science in colour, one without points grey |
| 7 | The pointer when units enter a building; no attack pointer over an occupied building; the guard pointer lacks its circle and shield | The pointer asked only the container, not `ActionManager::canEnterObject`'s rule (another player's building with riders inside is attacked, not entered); the radius cursor was never drawn and `RadiusCursorRadius` never read | `877dcffb`: `SimulationWorld.CanEnter`, the order's own test, decides the pointer; `8c8a5b15`, `fb53842c`: `InGameUI::setRadiusCursor`, `RadiusDecal::update` |
| 8 | Nothing is seen that was not uncovered; the minimap is black | `GameLogic::startNewGame` explores the map for every player of a match of several players unless `Multiplayer.ini` says `UseShroud`, and the shipped data say no | `1320e708`: `ExploreMapCommand` at tick 0; `9716081a`: what the map placed stands darkened from the start (the ghosts of the reveal), units out of sight stay hidden. The built-in AI took the explored map for sites it could build on (`b3136a86`, §82.2) |
| 9 | The general's powers at the side show no recharge, the pointer does not change, the button does not press | The recharge was a wipe of black at 160, which a linear-light blend leaves a third as dark as the original's gamma blend: a recharging power looked ready, and it was disabled; the overlay states were not drawn | `fd8ddec8`: the original's inverse clock (`UiClock`, `drawRemainingRectClock`) on the command bar, the queue and the bar at the side; black laid over the picture at the alpha that darkens as the gamma blend does (`UiAlphaCurves.BlackOver`); `Cameo_hilited`, `Cameo_push`. A click on a ready power entered its mode before as well. The bar stands where its layout puts it in both skins; no offset was found, and only a person can say what was meant |

Not judged by a program: whether the clock, the darkened map and the load screen look like the original's; the train's whistle on a collision and the carriages' clatter are not built (`docs/SIMULATION-CORE.md` §81.7).

## The owner's playtest of 4 November 2026

A skirmish on `aed7048c`, twelve findings. Each is read in the original's source. Eight are fixed and pinned by a test on real data (`PlaytestHudTests`, `InputTests`, `WorldRealData` "every map brings its own light"); three are the original's behaviour and stay; one cause (3 and 12) was built, measured and held back until an older rate difference (F-138) was fixed, and is adopted in Phase 61 (`39e53c30`, `docs/SIMULATION-CORE.md` §85.2).

| # | What the owner saw | Cause | Fix |
|---|---|---|---|
| 1 | Units ordered into a building are inside at once, from anywhere | The client sent the instant `EnterContainerCommand`, which fixtures and tools use | `71b5a5a3`: the order is `MoveAndEnterCommand` (`aiEnter`, `MSG_ENTER`): the unit walks to the building and enters at its door; a wire no longer lets the instant entry through (`CommandTypes.IsPlayerOrder`) |
| 2 | Who sits in a building is not shown, nobody can be let out alone or all together | The port lacked `CB_CONTEXT_STRUCTURE_INVENTORY` for a garrisonable building without a command set of its own (the civilian buildings) | `f2277dd1`: `ControlBar::populateStructureInventory` - ten places of `Command_StructureExit` with the cameo of who sits there, a click lets that one out, Stop and Evacuate; a neutral building shows its places disabled. A transport already showed its riders (`doTransportInventoryUI`) |
| 3 | Three Comanches at the rally point stand pixel-exactly in one another | The port gave aircraft no goal: `AIInternalMoveToState::onEnter` adjusts every move, and for an aircraft that hovers or has wings `Pathfinder::adjustDestination` keeps its box of goal cells (`getRadiusAndCenter`) clear of every other one's (`checkDestination`, `updateAircraftGoal`) | Adopted in Phase 61 (`39e53c30`, §85.2) after F-138: `SimulationWorld.AdjustAircraftDestination` for an order, the rally point and a dock's approach (`AIDockApproachState`); test `RealHelicoptersTakeGoalsOfTheirOwn` at 30, 60 and 120 Hz |
| 4 | A ranger with the capture upgrade shows no capture pointer over a neutral building | The context of the original asks `ACTIONTYPE_CAPTURE_BUILDING` after entering and attacking (`CommandTranslator::evaluateContextCommand`, `handleCaptureBuildingCommand`); the port had no such context | `ec40f966`: over a neutral tech building the pointer is capture and a click captures; a civilian building is entered, an enemy's attacked. `2e5dbe32`: `IMMUNE_TO_CAPTURE` was never refused (`canCaptureBuilding`). The pointer comes once the ability has recharged after the upgrade, as `getPercentReady` asks |
| 5 | The shadow a helicopter throws is a little too hard on the ground | The decoder skipped a map's `GlobalLighting`: every map was lit with `GameData.ini`'s afternoon and drew its stencil shadows in the engine's grey | `6767eed6`: `WorldHeightMap::ParseLightingDataChunk` - the map's time of day, its lights and its shadow colour (`setShadowColor`). Of the 150 shipped maps 47 play in the morning, 3 in the evening, 15 at night, and 86 name a shadow colour of their own, most of them lighter and bluish |
| 6 | The button to select an idle worker does nothing | `SELECT_NEXT_IDLE_WORKER` had no handler | `ec40f966`: the idle dozers in the order they became idle (`DozerPrimaryIdleState`, `InGameUI::addIdleWorker`), the next one selected and looked at (`selectNextIdleWorker`), the button enabled only while one is idle (`updateIdleWorker`) |
| 7 | A drone ordered for a vehicle is not shown, its button and the other drones' are not grey | Not all of it: the order stands in the vehicle's queue and the drone buttons are greyed while it is built (`NOT_QUEUEABLE`). Missing were `Object::affectedByUpgrade` (a vehicle with a scout drone offers no other drone, `ConflictsWith`) and the group: a click with several vehicles selected gave the drone to the first only | `ec40f966`: both, as `getCommandAvailability` and `AIGroup::queueUpgrade` do. Several vehicles selected show no queue, as in the original |
| 8 | Comanches fly straight over their target instead of attacking from range; they do not use the gun on a building when the rockets are empty | The first: a player's attack order treated every aircraft as a jet on an attack run. The second is the original's | `ec40f966`: a helicopter (`HOVER`) approaches and hovers where it is in range (`AIAttackApproachTargetState`). The weapon choice stays: the Comanche's data say `PreferredAgainst = SECONDARY VEHICLE STRUCTURE`, and `WeaponSet::chooseBestWeaponForTarget` keeps a preferred weapon "if they are merely reloading" - the missiles reload by themselves (`AutoReloadsClip = Yes`), so against a building or a tank the Comanche waits for them |
| 9 | Helicopters fire only rockets at tanks, the gun only at infantry | The original's, as in 8: the cannon is preferred against `INFANTRY`, the missiles against `VEHICLE STRUCTURE` | none |
| 10 | The space bar does not jump to the last event | `VIEW_LAST_RADAR_EVENT` had no handler | `ec40f966`: the camera looks at the last radar event (`Radar::getLastEventLoc`): a building finished, an object's upgrade done, an attack (rationed as its message). Phase 62 (F-137) adds the rest of the original's radar events: scripts, a beacon, a unit lost, infiltration (hijack, car bomb, capture, hack, sabotage), stealth found or lost, a battle plan, an overcharge that gave out |
| 11 | The computer does not give up; it fights to its last unit | The original's: surrender is compiled out (`ALLOW_SURRENDER` is defined nowhere), and a player is defeated when it has no objects left (`VictoryConditions::hasSinglePlayerBeenDefeated` with `NOUNITS` and `NOBUILDINGS`) | none |
| 12 | The computer's Chinooks hover over the supply center and collect nothing | Reproduced on Alpine Assault (`SimTool ai-match`, a trace of the supply AI): the Chinooks do collect and deliver, but those that wait for a warehouse hovered in the one that docked, so three of four looked idle | As 3: a dock's approach takes a goal cell of its own (`39e53c30`) |

Not judged by a program: whether the shadows and the maps' light look like the original's; how the capture, the idle worker and the space bar feel.

## The owner's playtest of 7 November 2026

Build `9f04c34a` on a handheld-sized test PC; 23 findings, each read in the original where it has the feature, each fixed or named as a limit; **no human has seen the fixes.** `docs/SIMULATION-CORE.md` §86.7 to §86.10 has the causes.

| # | Finding | Cause | Fix |
|---|---|---|---|
| 1, 5 | The interface is still the old stock one; make the new skin the default | the modern skin was the default for a first start only; a saved `display.skin = original` stayed | settings schema 6 sets it for every file |
| 2 | A pause does not stop the buildings' motion (the radar tower's shadow, the dish); the music runs on | the picture's own motion ran on real time; the port ducked the music, the original pauses it | `LiveGame.AnimationSeconds`, the mixer holds the track |
| 3 | The pause menu's Load/Save, Options and Restart do nothing | the HUD only raised an event; the client handled one fixed Save slot | the original's screens and questions, Exit too |
| 4 | Windowed fullscreen | missing | `display.mode` = `borderlessFullscreen` |
| 6, 13 | The reactor lacks details; an upgraded reactor shows no rods | the INI parser dropped every `Key value` line without `=` inside a module or state (the rods' states); the presentation never read `SubObjectsUpgrade` | parser fix, `SubObjectOverrides` |
| 7 | A depot under the fog is almost invisible | the fog factor passed as a colour constant reached the surface in linear light (0.23 of the brightness instead of 0.5) | `FogDimming` |
| 8 | No fog on the minimap | the minimap texture was read as linear light | an sRGB texture |
| 9 | Starting cash to 75k and 100k | | the shared list |
| 10, 11 | Select buildings like units; production distributed over them; the rally point for all | the original selects one building at a time | structure groups, `ProductionBalancer` (docs/PRESENTATION-INPUT.md 5a) |
| 12 | Tab through the abilities of a mixed selection | | Tab subgroups |
| 14 | Units garrison up to the building's middle | the port skipped structures in the touch rule | the wall |
| 15 | A control group is not shown at the health bar | not drawn | the group number |
| 16 | A sniper on guard walks at vehicles | the guard did not test `UNATTACKABLE` (a vehicle's hulk) | the original's test |
| 17 | E and EE print no message | missing | `GUI:SelectedAcrossScreen` and the two others |
| 18 | The Patriots show no beams when they pass a target on | not drawn | **open** (F-158) |
| 19 | The enemy stops collecting | three economy stops of the built-in AI | `docs/SKIRMISH-AI.md` 7d |
| 20 | A fuel bomb never explodes | the gas made in the air never fell | F-155 |
| 21 | The Strategy Center's plans do not switch | they switched; the bar showed nothing (no pressed button, no message, no sound) | the pressed button, the line, the sounds; the doors' speed open (F-157) |
| 22 | The Comanche does not fire its cannon at buildings while its missiles reload | the original's rule: a preferred weapon is kept while it reloads | a rule fix |
| 23 | Debug options: quick build, returning neutral buildings, general points | | `MatchDebugOptions`, protocol 9 |
| 24 | Choose the AI (old or new, aggressive, turtle, eco) in the setup | the engine was a command-line option | the AI options screen |

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

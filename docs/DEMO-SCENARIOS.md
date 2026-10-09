# Staged demos for auditing

As of: 7 October 2026. Three small behaviour scenes and twelve factory exits (separate section below) on a real map (Alpine Assault, origin = first start position), each of which isolates **one** behaviour. The same definition
(`CnCNew.Game/Live/DemoScenarios.cs`) runs in the client and headless; `LiveScenario` (`CnCNew.Game/Live/LiveScenario.cs`) places exactly the named objects (no bases, no economy),
reveals the map for both sides and issues commands through the same `StandingOrders` book as a player. The scenes are **test benches**: a failed check is a finding
about the state of the simulation/presentation, not an error of the tool. All three have passed since the fixes of 4 October 2026; the visual check by a human is outstanding.

**Unit of time.** All times of these scenes, of the audit output (`t=`, "tick", "ticks") and of the options `-Every`/`--every`/`--ticks` are **original frames** (1/30 s of game time),
not simulation ticks. The scenes are written in original frames (`DemoScenario.Frames`, `ScenarioOrder.Frame`), the audit takes one sample per original frame and converts every tick that
the simulation hands out when reading it in; in the smoke test of the client one drawn frame is one original frame. At 30 Hz an original frame is one tick, at 60 Hz (`-TickRate 60` on
`audit-scenarios.ps1` and `capture-scenarios.ps1`, `--tick-rate 60` on the client and `scenario-audit`) it is two – the numbers below mean the same game time at every rate
(`docs/TICKRATE-MIGRATION.md` §14).

| Scene | Shows | State of the audit (numbers: original frames, measured on 6 October 2026 at 30 Hz) |
| --- | --- | --- |
| `capture-building` | Ranger walks to the enemy barracks, unpacks, raises the flag (`RAISING_FLAG`), packs, the building changes sides | **green.** Staged ability (`docs/SIM-STAGED-CAPTURE.md`): approach from frame 12, `UNPACKING` from frame 139 (90 frames = 3 s), `RAISING_FLAG` from frame 229 (600 frames = 20 s), capture in frame 828, `PACKING` from frame 829 (60 frames = 2 s). At 60 Hz the same frames (approach from frame 11); the number of flashes during the raising of the flag is a different one there (49 at 30 Hz, 99 at 60 Hz – `docs/TICKRATE-MIGRATION.md` §5.1, Phase 23) |
| `tank-duel` | Crusader against Battlemaster, at an angle to each other; the turret has to turn before the first shot, stays aimed at the opponent until its death and turns back after the recentre time; a Sentry Drone scans while idle | **green.** Turret and attack state in the simulation (`docs/SIM-TURRET-AIM.md`): the Crusader only fires in frame 11 (45° at 180° per second, i.e. 6° per original frame), every shot with 0° error, in **every** sample until the death within 5°; after the death (frame 382) the turret holds its `RecenterTime` of 60 frames (2 s) and turns back from frame 444; the drone scans 9 times (hold time 31 frames, rest of 8 frames in between). At 60 Hz: first shot in frame 10, death in frame 380, turning back from frame 441 |
| `jet-strafe` | The airfield builds two Raptors; take-off on the runway of the model, approach, missiles at two Missile Defenders, fire, circling, return flight, landing, reloading | **green.** Wing model (`docs/SIM-JET-FLIGHT.md`): minimum speed, limited turn rate, circling; runway points from the airfield model; both Raptors take off, fire 4 missiles, land and reload |

(Note on the choice of targets: according to `Armor.ini` (`StingerSoldierArmor`), Stinger soldiers are **invulnerable to jet missiles**, which is why the "rocket trooper" here is the Missile Defender.)

## Running

```powershell
.\scripts\demo-scenario.ps1                              # WITHOUT a name: lists the scenes and starts nothing (until 7 October 2026 this silently started tank-duel)
.\scripts\demo-scenario.ps1 -Scenario tank-duel          # window, skirmish HUD (commands of the player are possible)
.\scripts\demo-capture-building.ps1                      # visual test in the window, one script per scene
.\scripts\demo-tank-duel.ps1
.\scripts\demo-jet-strafe.ps1
.\scripts\capture-scenarios.ps1                          # all three as an image sequence (PNG every N original frames) + index.html under %TEMP%\cncnew-scenario-frames
.\scripts\capture-scenarios.ps1 -Scenario tank-duel -Every 5 -Size 1920x1080
.\scripts\demo.ps1 scenario -Scenario jet-strafe -SmokeTest
.\scripts\audit-scenarios.ps1                            # check all three headless, output under %TEMP%\cncnew-scenarios
.\scripts\audit-scenarios.ps1 -Scenario tank-duel -Every 10
.\scripts\audit-scenarios.ps1 -TickRate 60                # the same checks with 60 simulation ticks per second; -Every and all output remain original frames
dotnet run -c Release --project tools/CnCNew.PresentationTool -- scenario-audit game/content --name capture-building --debug-fire ranger,barracks
```

`scripts/capture-world.ps1` contains one capture per scene (`scenario-tank-duel`, `scenario-capture`, `scenario-jet`). `Game.Tests` (`ScenarioTests`) checks with real data that every scene
places its objects, executes its commands and delivers the same state hash twice; it does **not** check the findings named above (otherwise the suite would be red).

## What a green audit means

- `capture-building`: staged ability after `SpecialAbilityUpdate.cpp` (approach, `UNPACKING` UnpackTime, `RAISING_FLAG` PreparationTime, capture, `PACKING` PackTime).
- `tank-duel`: no shot with more than 5° turret error; the turret has turned before the first shot (turret state and fire clearance in the simulation, `TurretAI`); in every sample (one per original frame) between the command and the death of the victim (also in the reload gaps) the turret stays within 5°; after the death the turret holds exactly `RecenterTime` (game time from the data) and then turns back; the Sentry Drone standing aside turns around randomly while idle (angle range, hold time, rest interval) and the drawn turret as well as `TURRET_ROTATE` follow the simulated angle.
- `jet-strafe`: no standstill in the air for more than 10 original frames (a third of a second; minimum speed and circling of the wing locomotor).

## Spectator demo (AI against AI)

Not a staged scene but a whole skirmish that one watches: `pwsh scripts/demo-spectator.ps1` starts, in the window, a match of two computer players without a player
(whole map without fog, wide zoom and map overview, following the simulated camera of an AI, observer panels for production, units, losses and economy). On every start
the script draws a new seed and prints it; `-Seed N` repeats exactly the same match. `pwsh scripts/check-spectator.ps1` runs the same mode without a window with pressed
spectator keys, checks the output of the client and puts ten images under `%TEMP%\cncnew-spectator-check`. Everything else (keys, origin of the values, limits, checklist for the
visual check) is in `docs/SPECTATOR-MODE.md`; none of it has been seen by a human.
## Factory exits (`exit-<fraktion>-<gebäude>`, 7 October 2026)

Question of the project owner: do freshly built units drive out of their factory, or do they "clip" out? Twelve scenes (`CnCNew.Game/Live/FactoryExits.cs`) place, per faction
(`america`, `china`, `gla`), the command center (builds dozers or workers), the barracks (Ranger, Red Guard, Rebel) and the war factory or the arms dealer (Crusader,
Battlemaster, Scorpion) individually close to the camera (`exit-america-warfactory` …) or all three in one picture (`exit-america-all`, plus a construction worker, power plant and supply depot –
the build-key test uses the same scene). Each building receives three build orders one after another from frame 30. All buildings stand at the default angle: the exit points east; the camera looks from the south-east
at the door side, so in the picture the unit drives out towards the bottom right.

```powershell
.\scripts\demo-factory-exits.ps1                                    # window: America, all three buildings; prints what to look out for
.\scripts\demo-factory-exits.ps1 -Faction China -Building warfactory
.\scripts\demo-factory-exits.ps1 -Faction GLA -Building barracks -TickRate 60
.\scripts\audit-scenarios.ps1 -Scenario exits [-Faction America]    # the same nine single scenes without a window, with numbers
.\scripts\capture-scenarios.ps1 -Scenario exit-america-warfactory   # image sequence (900 original frames, one every 15)
```

### Yardstick: the original

Sources: `ProductionUpdate.cpp` (`ProductionUpdate::update`, `updateDoors`), `DefaultProductionExitUpdate.cpp` and `QueueProductionExitUpdate.cpp` (`exitObjectViaDoor`,
`getNaturalRallyPoint`), `SupplyCenterProductionExitUpdate.cpp`, `AIStates.cpp` (`AIFollowPathState` for `aiFollowExitProductionPath`).

1. When the build order is finished, `ProductionUpdate::update` reserves a door. If the building has door animations (`NumDoorAnimations > 0`), the door starts to open
   (`DOOR_1_OPENING` for `DoorOpeningTime`); the unit is **only created when the door is fully open** (`m_doorWaitOpenFrame != 0`, `DOOR_1_WAITING_OPEN`). The door stays
   open for `DoorWaitOpenTime` after the last unit and then closes over `DoorCloseTime` (`DOOR_1_CLOSING`).
2. `exitObjectViaDoor` puts the unit on the `UnitCreatePoint` rotated into the building – that is **inside the building** (barracks: the centre of the building) –, with the orientation of the building.
3. The unit receives an exit path (`aiFollowExitProductionPath`): first `NaturalRallyPoint`, pushed outwards by two pathfinding cells (20 units) in its own direction
   (`getNaturalRallyPoint(…, offset = TRUE)`; according to the comment of the data the point always lies on the edge of the building), then – if set – the rally point of the player.
   The unit's own factory is not an obstacle on this path (`ignoreObstacle`).
4. `QueueProductionExitUpdate` (Chinese barracks) lets `ExitDelay` (300 ms) pass after every unit so that the Red Guards built in pairs come out one after the other.

| Building | Exit module | `UnitCreatePoint` → `NaturalRallyPoint` (building coordinates) | Half edge lengths | Door: opening / open / closing (frames) |
| --- | --- | --- | --- | --- |
| `AmericaCommandCenter` | Default | (−18, 35) → (60, 35) | 60 × 70 | 45 / 90 / 45 |
| `AmericaBarracks` | Default | (0, 0) → (55, 0) | 55 × 45 | no door |
| `AmericaWarFactory` | Default | (−10, −30) → (53, −30) | 53 × 60 | 98 / 90 / 120 |
| `ChinaCommandCenter` | Default | (−18, 40) → (60, 40) | 60 × 63 | 90 / 90 / 90 |
| `ChinaBarracks` | Queue (`ExitDelay` 300 ms) | (0, −25) → (36, −25) | 36 × 44 | no door |
| `ChinaWarFactory` | Default | (0, −27) → (57, −27) | 57 × 61 | 120 / 60 / 150 |
| `GLACommandCenter` | Default | (25, 0) → (65, 0) | 65 × 65 | no door |
| `GLABarracks` | Default | (0, 0) → (42, 0) | 42 × 52 | no door |
| `GLAArmsDealer` | Default | (−20, −15) → (40, −15) | 40 × 62 | 60 / 90 / 60 |

### What the audit measures and checks (`tools/CnCNew.PresentationTool/FactoryExitAudit.cs`)

Per original frame, in building coordinates (x towards the exit side): frame and place of appearance of every built unit, its path in the first three seconds (every 15 frames), the first frame
outside the building box and the position there, the resting place, whether it is drawn in every frame, and the timeline of the door states of the building. Checks: (a) appearance at the
`UnitCreatePoint` (±1.5); (b) for buildings with a door: the door shows `DOOR_1_WAITING_OPEN` when the unit appears, and after the last unit `DOOR_1_CLOSING` and "closed" follow;
(c) the unit leaves the box via the exit side (x ≥ half length, at most 20 beside the line of the `NaturalRallyPoint`); (d) it comes to rest, outside the
box, at most 130 from the end of the exit path (the exit point itself or a place beside it), and no two units of a building rest inside each other (distance at least
0.9 of the sum of their bounding radii); (e) it is drawn in every frame; (f) one unit appears per order.

### Finding and correction (measured 8 October 2026, 30 and 60 Hz; **not seen by a human**)

| Check | Before the correction of the simulation (27 units, nine buildings) | With the exit path (`docs/SIMULATION-CORE.md` §42) |
| --- | --- | --- |
| (a) place of appearance | 27 of 27 exactly at the `UnitCreatePoint`, inside the building – as in the original | unchanged |
| (b) door | open (`WAITING_OPEN`) at all five buildings with a door when the unit appears; closes after the last one (e.g. `AmericaWarFactory`: opens from frame 231, open from 330, unit in frame 330, closes from 1020, closed from 1141). Before that, the door was still `OPENING` for one frame when the **first** unit appeared (fixed in `StructureConditionTracker`: lead time `DoorOpeningTime` + 1 tick) | unchanged |
| (c) exit | **0 of 27**: no unit moves. It stays standing at the `UnitCreatePoint` inside the building | **27 of 27** leave the box via the door side on the line of the `NaturalRallyPoint`; first frame outside: command center America +93, China +93 to +94, GLA +52; barracks America +86, China +47 to +48, GLA +66; war factory America +64, China +69, arms dealer +46 frames after the appearance – the same numbers at 30 and at 60 Hz |
| (d) stopping point | not applicable: distance to the end of the exit path 54 to 96 units, three units on the same point inside each other | the unit built last stands on the exit point (distance 0.0); the earlier ones have made room and stand 19 to 77 beside it, all outside the box, no two inside each other; each one comes to rest (resting places at 60 Hz as at 30 Hz) |
| (e) drawn | 27 of 27 in every frame | unchanged |
| (f) quantity | 27 orders deliver **one** unit each – including the Chinese barracks, which in the original builds **two** Red Guards per order (`QuantityModifier = ChinaInfantryRedguard 2`, typed, not applied; only reported) | unchanged |

What the project owner saw as "clips straight out" was therefore: after being built, the dozer stood **inside** the command center and only drove on a command – then straight through the
building. The deviation lay in the **simulation** (`ProductionUpdate.Update` gave the unit no path), not in the presentation. Since the commit for §42 every unit drives out through the
door side by itself, makes room for the next one and, with a rally point set, continues from there. Deviations that remain, only reported: the unit is created as soon as
the order is finished, not only after `DoorOpeningTime` (the presentation opens the door correspondingly earlier for this, the whole door cycle lies `DoorOpeningTime` ahead of the original);
the quantity of the Red Guards; trucks of the supply depot and `SpawnBehavior` creations do not use the exit path.

State of the audit (8 October 2026): before the commit for §42 all twelve scenes are red (three failures per building, nine in the combined scenes: (c)/(d) per unit); with it,
`audit-scenarios.ps1 -Scenario exits` (nine single scenes) and the three combined scenes `exit-<fraktion>-all` pass at 30 Hz and with `-TickRate 60`, as do the three classic scenes. The
commit changes the simulation at 30 Hz (new hashes in 23 of 50 runs of the reference manifest, numbers in §42.4); the manifest is rewritten in a commit of its own. In the
combined scenes the timeline of the doors deviates between 30 and 60 Hz by one frame in two entries (e.g. `AmericaCommandCenter`: `CLOSING` from 271 against 270, closed from 616 against 615) –
presentation, only reported.

### Checklist for the human (`scripts/demo-factory-exits.ps1`)

1. The unit appears inside the building, not in front of it and not on the roof.
2. Buildings with a door (both command centers except GLA, all three factories): the door is fully open when the unit appears, and closes again afterwards.
3. The unit drives/walks out through the door side (bottom right in the picture) by itself and stops shortly in front of the building.
4. The second and third unit place themselves beside the first, not inside it.
5. Does the model of the unit sit visibly correctly in the door during the exit (height, direction), or does it protrude through wall or roof?
6. With a rally point set (select the building, right-click on the ground): first out, then to the rally point.

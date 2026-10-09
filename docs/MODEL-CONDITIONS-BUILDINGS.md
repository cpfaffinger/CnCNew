# Model conditions of the buildings, docks, doors and pack states

As of: 4 October 2026 (re-measure). Belongs to `docs/PRESENTATION-OBJECTS.md` (state derivation) and `docs/CLIENT-OBJECTS.md`
(world presentation); the trigger is `docs/DEMO-FINDINGS.md` (model conditions that are never set show wrong or missing
model parts). This document covers the 27 names of the building, door, dock, pack and world states that `PresentationTool model-audit
--structures` reported as "never set" (25 are now derived, 2 not derivable).

## 1. Result

`dotnet run -c Release --project tools/CnCNew.PresentationTool -- model-audit <dir> --structures`:

| | before | after |
|---|---|---|
| names used by states | 106 | 106 |
| set by the presentation (or `ModelConditionUpgrade`) | 52 | 77 |
| never set | **54** | **29** |

25 names leave the list: `DOOR_1..4_OPENING`, `DOOR_1..4_CLOSING`, `DOOR_1..4_WAITING_OPEN`, `DOOR_1..3_WAITING_TO_CLOSE`
(15 door names), `CONSTRUCTION_COMPLETE`, `DOCKING_ACTIVE`, `PACKING`, `UNPACKING`, `CAPTURED`, `SMOLDERING`, `POST_COLLAPSE`,
`SPECIAL_DAMAGED`, `ENEMYNEAR`, `OVER_WATER`. From the brief **two** names stay open (section 5): `PREORDER` and
`DOCKING_BEGINNING`. The remaining 27 names of the list do not belong to this brief (unit states: pre-attack, continuous fire, jets,
free fall, crushing, climbing, steering, kinds of explosion, …).

`PACKING`/`UNPACKING` are covered **only partly**: 22 of the 58 objects with these states (4 deploy units, 5 hackers, 8
particle cannon variants, 5 repair drones) get them, the 36 objects with `SpecialAbilityUpdate` (Ranger, Colonel Burton, Black
Lotus, Rebel, Red Guard, CIA officer and their variants) do not – see section 5. `DOOR_1_OPENING` is likewise covered only
partly (section 5: Baikonur, flight deck, container doors).

## 2. Principle

In the original the client sets these bits imperatively from within many modules. Here every bit is a pure function of the state that a
simulation module holds anyway, or – where the bits follow a timer that the simulation does not know – a small presentation
tracking that is advanced once per simulated tick. Neither changes the simulation.

* `StructureConditions.Apply` (`src/CnCNew.Presentation/Objects/StructureConditions.cs`, called at the end of `ConditionDeriver.Derive`)
  reads the state of the respective module: silo hatch, strategy center, Spectre, tech building, `EnemyNearUpdate`, collapse, helicopter
  wreck, particle cannon, train, deploy unit, hacker, docks. Stateless, deterministic, recomputable at any time.
* `StructureConditionTracker` (same file, called by `FrameProjector.CaptureEntity`, the result is united with the derivation)
  holds a small table per entity: the production doors, `CONSTRUCTION_COMPLETE`, the door latches of the bomber and of the
  checkpoint, the repair arm of the drone. The table is advanced via timestamps, not via tick counters; a gap
  between two samples (object out of vision) loses no transition. `Prune` clears entries of vanished entities (like `TurretTracker`).
* The simulation was extended **read-only** only (one commit, `feat(simulation): read-only state …`): public properties on modules
  that already hold the state, plus the door times of `ProductionUpdate` (`ProductionData`, `Opaque` in the catalog, therefore read from the
  original text – no catalog change, no hash of the definitions changes) and `LocomotorInfo.IsHover`. No snapshot field, no
  replay field, no tick changes. Measured: retail reference match winner FactionGLA at tick 13,890, final hash `76F22CB0CA79BEEF`, chain
  `35F54CAD40A43E6E` (unchanged); stress test final hash `56882F763EABEF67`, chain `B8E663493F2C73D5` (unchanged).

## 3. The doors of the production buildings

Source: `ProductionUpdate.cpp` (`update`, `updateDoors`, `setHoldDoorOpen`), `ParkingPlaceBehavior.cpp` (`buildInfo`, `reserveDoorForExit`,
`reserveSpace`, `releaseSpace`, `purgeDead`), data `NumDoorAnimations`, `DoorOpeningTime`, `DoorWaitOpenTime`, `DoorCloseTime`,
`ConstructionCompleteDuration` (milliseconds, as `parseDurationUnsignedInt` rounded up to ticks).

**Original.** If the unit is finished and the exit is free (`reserveDoorForExit`), `update` opens the door of the exit (`DOOR_n_OPENING`),
sets `CONSTRUCTION_COMPLETE`, and **creates the unit only when the door is completely open** (`m_doorWaitOpenFrame != 0`). After that
`updateDoors` holds the door open for `DoorWaitOpenTime` (`DOOR_n_WAITING_OPEN`, renewed with every further unit), closes it
for `DoorCloseTime` (`DOOR_n_CLOSING`) and then clears all bits. If the door is still closing at the next finished item, it jumps
back to "waiting open". `setHoldDoorOpen` (airfield) holds a door open and opens a closed one immediately. An exit always supplies
door 1 (`DefaultProductionExitUpdate`, `QueueProductionExitUpdate`, `SupplyCenterProductionExitUpdate`, `SpawnPointProductionExitUpdate`);
only `ParkingPlaceBehavior` supplies the door of the first free parking space (door = row times column, in the order of `buildInfo`).

**Simulation.** `ProductionUpdate` has no door: the unit comes into being in the tick in which the entry is finished. The tracking therefore runs
on the clock of the simulation and lets the door begin **`DoorOpeningTime` before the appearance** of the unit (the remaining time of the
first entry of the queue is known: `ProductionUpdate.TryGetHead`). The door is thus open when the unit appears; from
then on `updateDoors` applies step by step (same transitions, same strict comparisons, same jump back of a closing door,
same holding open). The whole sequence is shifted forward by `DoorOpeningTime` against the original (opening begins earlier,
closing too); the total duration of the unit is that of the simulation. **That is an approximation**: it shows the unit through an open
door instead of out of a closed one; the simulation lets nothing wait for the door. If the exit cannot release
(`IProductionExit.IsFreeToExitFor`: airfield full, queue exit in the pause), nothing opens; a finished item that
waits holds the door open (like the repetition of `update` in the original). `PRODUCED_AT_HELIPAD` needs no door (`DOOR_NONE_NEEDED`).

**Airfield.** The parking space `k` has door `k+1`. A door is "held" as long as the space is occupied or reserved for an exit
(`ParkingPlaceBehavior.OccupantOf`, `IsReservedForExit`); held, it opens and stays open (`setHoldDoorOpen`). When the space becomes free,
the waiting time has mostly long expired and the door closes immediately (likewise in the original: `now - m_doorWaitOpenFrame > m_doorWaitOpenTime`).

**`CONSTRUCTION_COMPLETE`.** Begins together with the opening of the door (or in the tick of creation if the building has no doors) and ends
when more than `ConstructionCompleteDuration` has passed.

Buildings whose `ProductionUpdate` has no `NumDoorAnimations` (propaganda center, decoys of the arms dealers) never show door bits in the original
either, although their models contain door states: `ProductionUpdate::update` touches the doors only with `m_numDoorAnimations > 0`.

## 4. The names in detail

| Name | Status | Source in the original (file, function) | simulation state read |
|---|---|---|---|
| `DOOR_1_OPENING`, `_WAITING_OPEN`, `_CLOSING` (command centers, war factories, arms dealers, airfields: the 34 objects with `NumDoorAnimations` above zero) | implemented (approximation, section 3) | `ProductionUpdate.cpp` `update`, `updateDoors` | `ProductionUpdate` head of the queue, `Data` (door times), `IProductionExit` |
| `DOOR_1..4_*` of the airfield | implemented (approximation) | `ParkingPlaceBehavior.cpp` `buildInfo`, `reserveSpace`, `releaseSpace`; `ProductionUpdate.cpp` `setHoldDoorOpen` | `ParkingPlaceBehavior.OccupantOf`, `IsReservedForExit`, `SpaceCount` |
| `DOOR_1_OPENING/_WAITING_OPEN/_WAITING_TO_CLOSE/_CLOSING` of the nuclear missile | implemented | `MissileLauncherBuildingUpdate.cpp` `switchToState` | `MissileLauncherBuildingUpdate.Door` |
| `DOOR_1..3_OPENING/_WAITING_TO_CLOSE/_CLOSING` of the strategy center | implemented | `BattlePlanUpdate.cpp` `setStatus` (door n = plan n) | `BattlePlanUpdate.Status`, new `CurrentPlan` |
| `DOOR_1_OPENING/_CLOSING` of the Spectre | implemented | `SpectreGunshipUpdate.cpp` `initiateIntentToDoSpecialPower`, `update` (orbit), `disengageAndDepartAO` | `SpectreGunshipUpdate.Status` |
| `DOOR_1_OPENING/_CLOSING` of the cargo planes and bombers | implemented (with latch) | `DeliverPayloadAIUpdate.cpp` `DeliveringState::onEnter`/`onExit` | new `DeliverPayloadAIUpdate.DoorsOpen`; "already opened once" is remembered by the tracker |
| `DOOR_1_OPENING/_CLOSING` of the checkpoint | implemented (with latch) | `CheckpointUpdate.cpp` `update` (only on a change of friend/enemy) | new `CheckpointUpdate.AllyNear`, `EnemyNear` |
| `CONSTRUCTION_COMPLETE` | implemented | `ProductionUpdate.cpp` `update` | like doors, `ConstructionCompleteTicks` |
| `DOCKING_ACTIVE` (docker) | implemented | `DockUpdate.cpp` `onDockReached`/`onExitReached`; `AIDock.cpp` `AIDockMoveToDockState::onExit` | new `AIUpdateInterface.IsDockActive` (state `ProcessDock`) |
| `DOCKING_ACTIVE` and `DOCKING` (dock) | implemented | `DockUpdate.cpp` `onEnterReached`/`onDockReached`/`onExitReached` (set the same bits on `me` and `docker`) | new `SimulationWorld.GetDockActivity` (`DockerInside`, active docker in the state `ProcessDock`) |
| `PACKING`/`UNPACKING` of the deploy units (nuke cannon, Patriot-like ones) | implemented | `DeployStyleAIUpdate.cpp` `setMyState` | new `DeployStyleAIUpdate.IsUnpacking`/`IsPacking` |
| `PACKING`/`UNPACKING` (+ `FIRING_A`) of the hackers | implemented | `HackInternetAIUpdate.cpp` `UnpackingState`, `HackInternetState`, `PackingState` | new `HackInternetAIUpdate.IsUnpacking`/`IsPacking`, `IsHacking` |
| `PACKING`/`UNPACKING`, `DEPLOYED` of the particle cannon | implemented | `ParticleUplinkCannonUpdate.cpp` `update`, `setLogicalStatus` | new `ParticleUplinkCannonUpdate.StatusAt` (clock, readiness of the power, beam times) |
| `PACKING`/`UNPACKING` (+ `FIRING_B`/`FIRING_C`) of the repair drone | implemented (with latch) | `SlavedUpdate.cpp` `onObjectCreated`, `endRepair`, `setRepairState`, `setRepairModelConditionStates` | new `SlavedUpdate.Repair`, `CanRepair` |
| `CAPTURED` | implemented | `TechBuildingBehavior.cpp` `update`, `onDie` | owner is a playable side (`SimulationWorld.IsPlayableSide`), object is alive |
| `SMOLDERING` | implemented | `FlammableUpdate.cpp` `update` (sets `BURNED` and `SMOLDERING` together, nothing clears them) | object status `BURNED` |
| `POST_COLLAPSE` (collapse) | implemented; clears `RUBBLE` like the original | `StructureCollapseUpdate.cpp` `update`; `StructureToppleUpdate.cpp` `update` | `StructureCollapseUpdate.IsCollapsed`, new `StructureToppleUpdate.IsToppled` (see section 6) |
| `POST_COLLAPSE` (avalanche boulders) | implemented | `TensileFormationUpdate.cpp` `update` (every run after the triggering) | `TensileFormationUpdate.Enabled` |
| `SPECIAL_DAMAGED` | implemented | `HelicopterSlowDeathUpdate.cpp` `update` (after the impact) | new `HelicopterSlowDeathBehavior.HasHitGround` |
| `ENEMYNEAR` | implemented | `EnemyNearUpdate.cpp` `update` | `EnemyNearUpdate.IsEnemyNear` |
| `OVER_WATER` (hover) | implemented | `Locomotor.cpp` `moveTowardsPositionHover` | new `LocomotorInfo.IsHover` (`Appearance = HOVER`), `TerrainMap.IsUnderwater` at the position |
| `OVER_WATER` (train in the tunnel) | implemented | `RailroadGuideAIUpdate.cpp` `update` (position more than 3 below the ground) | `RailroadBehavior` present, height of the entity against `GroundHeightAt` |
| `DOCKING_BEGINNING` | **not derivable** | `DockUpdate.cpp` `onEnterReached`..`onDockReached` | see section 5 |
| `PREORDER` | **not derivable** | `PreorderCreate.cpp` `onBuildComplete`, `Player.cpp` (`playerIsPreorder`) | see section 5 |
| `DOOR_4_WAITING_TO_CLOSE` | is set by no module and used by no state | – | – |

## 5. Not derivable, and why

* **`DOCKING_BEGINNING`.** Set between `onEnterReached` and `onDockReached`, i.e. as long as the docker drives from the entry to the dock point.
  In the simulation entry, dock point and exit coincide (`DockMachine`: no bones, all paths are the position of the
  docker), `MoveToEntry` and `MoveToDock` pass in the same tick. The span has no tick of length. It is needed only by
  alias states of Jarmen Kell (`MOVING DOCKING_BEGINNING` is an alias of `MOVING`), so nothing is visibly missing. Whoever needs the
  stretch has to give the simulation dock geometry.
* **`PREORDER`.** `PreorderCreate::onBuildComplete` sets the bit if `Player::didPlayerPreorder()` holds; that is the keyword
  `playerIsPreorder` of the player dictionary of a map, set from the online profile of a player who pre-ordered the game.
  The simulation does not have this property, offline it is never true. The states are the pre-order decoration of the command centers
  and stay unused – this is how the original also shows them to a player without a pre-order.
* **`PACKING`/`UNPACKING` for `SpecialAbilityUpdate`** (36 objects: Ranger, Colonel Burton, Black Lotus, Rebel, Red Guard and
  variants). The original runs an unpack/approach/pack state machine before the ability
  (`SpecialAbilityUpdate.cpp` `startUnpacking`, `startPacking`, `handlePackingProcessing`); the simulation has for the module only the
  recharge (`SpecialAbilityUpdate : SpecialPowerModuleBase`, class comment: "none of that per-ability staging is modelled").
  Without the phase there is no state from which a bit could be read. Missing: the phases with their times (`UnpackTime`, `PackTime`,
  `PackUnpackVariationFactor`, one random value per phase) and the approach.
* **`DOOR_1_OPENING` for `BaikonurLaunchPower`** (23 objects, above all cinematic props). `doSpecialPower` without a location sets the bit and
  never clears it; the sequence with a location (`doSpecialPowerAtLocation`) does not set it. The simulation does not record whether the power was once
  used without a location (`BaikonurLaunchPower.Apply` then only returns). A bit for that would be a new, serialised field (hash change in
  runs that use the power this way), which was deliberately not created here.
* **`DOOR_2..`/`DOOR_1_*` of the aircraft carrier** (`FlightDeckBehavior.cpp`, one object). The simulation has an empty
  `FlightDeckBehavior` (class comment: shell), the carrier launches and lands nothing.
* **`DOOR_1_OPENING`/`_CLOSING` of the amphibious transport and the ferry** (`OpenContain.cpp` `exitObjectViaDoor`, `exitObjectInAHurry`, one to two
  objects). The containers of the simulation remove occupants without a door phase; `DoorOpenTime` is `Opaque` in the catalog and is read nowhere.

## 6. Anomalies and limits

* **`StructureToppleUpdate` reaches `TOPPLESTATE_DONE` (fixed).** With the retail `StructuralIntegrity` of 0.75 the angular acceleration
  of the first tick is `0.02 · sin(0.001) · 0.25 ≈ 5 · 10⁻⁶` and lies below the resolution of `Fixed` (1/65536); with `Fixed` fields the
  velocity stayed zero (`TallTower02` was still `Toppling` after 60,000 ticks). Angle and angular velocity now carry 32 additional
  fractional bits (the product of the three `Fixed` raw values is exact at 48 bits); a tower with the retail values lies flat after about 104 ticks of fall
  (`ln(π/2 / 0.001) / √(0.02 · 0.25)`), the snapshot writes the additional bits only if they are non-zero (standing buildings
  keep their state hash). The test now demands `POST_COLLAPSE` for `TallTower02` unconditionally.
* **The production doors are an approximation** (section 3): shifted forward by `DoorOpeningTime`, the unit appears through the open door.
  A human has to check whether the opening of the door matches the animation length of the models (`DoorOpeningTime` is the game time, the
  model plays its animation in this time).
* **`OVER_WATER` for hover** is changed by the original only as long as the object moves; standing, it keeps the last value. Here the
  bit follows the position. The same, unless an object is placed on land or water without movement. Every helicopter with `Appearance = HOVER`
  gets the bit over water (as in the original; no state uses it).
* **Particle cannon:** the reset case of the original (a ready cannon whose clock is reset packs itself) is not read:
  idle. The antenna phases come from the clock, not from a stored state, exactly as in the original.
* **Repair drone:** the arm follows `setRepairModelConditionStates` with one bit at a time: `PACKING` (rest), `UNPACKING` (unpacking and
  readiness afterwards), `FIRING_B` (extending and welding), `FIRING_C` (retracting and readiness afterwards). The two `FIRING_*` are
  therefore here those of the repair arm, not those of the weapon derivation (`ConditionDeriver.Weapon`).
* **`OVER_WATER` without terrain:** without a `TerrainMap` there is no water, a hover vehicle then never shows the bit.
* **Hacker:** `FIRING_A` during hacking is set with this derivation (`HackInternetState::onEnter`), without `USING_WEAPON_A`.
* **Checkpoint and bomber** need the sampling of every tick to keep the latch; a checkpoint that the player sees only after
  a change shows the state of the first change seen.
* **Fresh sweep:** `RealSweep` (all 2,115 retail objects, 6 ticks, `FrameProjector`) throws nothing and shows `CAPTURED` ×7 (tech
  buildings with player ownership), `PACKING` ×4 (drones), `DOOR_1_OPENING` ×1 (an airfield to which an aircraft gives its space),
  `DOOR_1_CLOSING` ×1 (checkpoint with an enemy in sight) and `SPECIAL_DAMAGED` ×2 (CINE Comanches that come into being as a wreck).

## 7. Tests and measurements

`tests/CnCNew.Presentation.Tests/StructureConditionTests.cs` (a suite of its own, 11 synthetic tests, with `--content-dir` a twelfth):

* Door cycle of the war factory tick by tick against `DoorOpeningTime`/`DoorWaitOpenTime`/`DoorCloseTime`/`ConstructionCompleteDuration`;
  a second unit holds the door open; a closing door jumps back; buildings without doors show nothing.
* Airfield: holding a space opens the door and holds it, release closes immediately; production opens the door of the first free space.
* Checkpoint (no bit without a change; friend: `OPENING`; enemy: `CLOSING`) and `ENEMYNEAR`.
* Supply dock: `DOCKING` and `DOCKING_ACTIVE` on docker and dock over the same ticks (depot and center), no `DOCKING_BEGINNING`.
* Bomber, particle cannon (stages and bits tick by tick, with firing), repair drone (order of the arm bits `UNPACKING` → `FIRING_B` → `FIRING_C` → `PACKING`), hover over a lake.
* Gaps between samples and `Prune`.
* With retail data: `AmericaWarFactory` (door times 98/90/120 ticks, `CONSTRUCTION_COMPLETE` 45 ticks, timeline
  `204: OPENING CONSTRUCTION_COMPLETE | 303: WAITING_OPEN | 394: CLOSING | 515: empty`), strategy center (door 2 for "Hold the Line"),
  nuclear missile (door before readiness), nuke cannon (unfolding), oil derrick (`CAPTURED`), tree (`SMOLDERING`), bank (collapse → `POST_COLLAPSE`
  instead of `RUBBLE`), hacker (setting up → `FIRING_A` → packing), Comanche, train in the tunnel, and the sweep over all objects.

The state of all suites at completion: simulation (plain and with `--content-dir`), presentation, Game (`--content-dir`), Ui, Data
(`--content-dir`) passed; clean solution build 11 warnings (the explained Stride notes), 0 new. Reference match and stress test
unchanged (section 2).

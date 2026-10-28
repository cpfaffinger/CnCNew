# The attack's line of sight (F-55)

This document is the reading behind F-55 (`docs/plan/STAGE-1-PORT.md` §3, Reforged R-09): what the original does when a unit that needs a line of sight
attacks, where each piece belongs in the port, and the steps of the fix. Sources read: the EA source under `references/ea-generals-zero-hour/GeneralsMD/Code` (abbreviated `GE/` for `GameEngine/Source/`, `GED/` for
`GameEngineDevice/Source/`), GeneralsGameCode under `references/generals-game-code/Core/...` (one fix), and the port at `src/`.
Line numbers are the files as they are today.

## 0. Corrections to the register entry

* `AttackUsesLineOfSight` is a key of **`AIData.ini`** (`TAiData::m_attackUsesLineOfSight`, field table `GE/GameLogic/AI/AI.cpp:173`,
  default `true` at `AI.cpp:929`), not of `GameData`. The shipped file sets `Yes` (`Data/INI/Default/AIData.ini:31`). The default and the
  shipped value are the same, so a named constant `true` is faithful whichever way the port reads it.
* The original has **no ballistic or indirect-fire exception in code**. The terrain origin is the same for every weapon
  (`Weapon::getFiringLineOfSightOrigin`, `Weapon.cpp:3058-3086`: shooter z + `getMaxHeightAbovePosition()`, the projectile launch point is
  commented out). The exception is in the data: artillery and missile launchers simply do not carry the KindOf
  (`AmericaVehicleTomahawk`, `ChinaVehicleInfernoCannon`, `ChinaVehicleNukeLauncher`, `GLAVehicleScudLauncher`, also `ChinaInfantryRedguard`,
  `ChinaVehicleTroopCrawler`, every aircraft do not; Crusader, Paladin, Overlord, Ranger, Rebel, Missile Defender, Rocket Buggy, Humvee,
  Quad Cannon, Gattling Tank, Burton, Jarmen Kell do).
* Count: a naive parser over `game/content/Data/INI` finds 254 Object/ChildObject/Reskin blocks with the kind (104 INFANTRY, 121 VEHICLE,
  22 STRUCTURE, 22 IMMOBILE - Patriot, Fire Base, Gattling Cannon, every GLA tunnel network -, 7 PORTABLE_STRUCTURE, 10
  SPAWNS_ARE_THE_WEAPONS, 5 HUGE_VEHICLE, no AIRCRAFT). The register's 245 is a different count (probably without reskins/map overlays);
  re-measure with the catalogue. 104 blocks carry `CAN_SEE_THROUGH_STRUCTURE` (fences, GLA holes, bus stops, ...).

## 1. When the original checks the view

### 1.1 The one predicate: `Pathfinder::isAttackViewBlockedByObstacle(attacker, attackerPos, victim, victimPos)`
`GE/GameLogic/AI/AIPathfind.cpp:9384-9454`. Returns "blocked" only if all of:

1. `TheAI->getAiData()->m_attackUsesLineOfSight` (9393) - else false.
2. `attacker->isKindOf(KINDOF_ATTACK_NEEDS_LINE_OF_SIGHT)` (9400) - else false. Nothing about the victim's kind, the weapon or relations.
3. **Terrain part** (`#define LOS_TERRAIN`, 9407-9428): `w = attacker->getCurrentWeapon()`, set to NULL for `KINDOF_IMMOBILE` attackers
   ("can't move around it"); with a weapon, blocked if `!w->isClearGoalFiringLineOfSightTerrain(attacker, attackerPos, victim)` (or the
   `victimPos` overload when there is no victim object). The weapon is used only to reach the function; its properties do not matter.
4. **Obstacle part** (9430-9453): walk the pathfind cells from `attackerPos` to `victimPos` (`iterateCellsAlongLine`) with
   `attackBlockedByObstacleCallback`; layer = the victim's layer (ground without a victim); if the attacker is not on `LAYER_GROUND` (bridge,
   wall, rooftop) the first 3 cells are skipped (`skipCount = 3`, 9440-9449) and the attacker's layer is walked when the victim is on ground.
   `victimCell = getCell(layer, &victimPos)` (may be NULL off the extent).

Note: the obstacle walk always runs from `attackerPos` to `victimPos` as passed (callers pass object positions), the terrain part from the
attacker's top to the victim's **centre**.

### 1.2 The terrain test
* `Weapon::isClearGoalFiringLineOfSightTerrain` (`GE/GameLogic/Object/Weapon.cpp:3120-3144`): origin = `goalPos` with z += attacker
  `getMaxHeightAbovePosition()` (`Geometry.cpp:215-228`: sphere -> major radius, box/cylinder -> height); victim point = victim position with
  z += `getZDeltaToCenterPosition()` (`Geometry.cpp:249-252`: 0 for a sphere, half the height otherwise); for a position, the position itself
  (its z as stored - `computeAttackPath` sets it to the layer height, `AIUpdate.cpp:1929`).
* `PartitionManager::isClearLineOfSightTerrain(NULL, origin, NULL, victimPos)` (`GE/GameLogic/Object/PartitionManager.cpp:4496-4568`): with
  object arguments it adds `getMaxHeightAbovePosition()` to **both** ends (top to top); then `TheTerrainLogic->isClearLineOfSight` ->
  `W3DTerrainLogic::isClearLineOfSight` (`GED/W3DDevice/GameLogic/W3DTerrainLogic.cpp:265`) -> `BaseHeightMapRenderObjClass::isClearLineOfSight`
  (`GED/W3DDevice/GameClient/BaseHeightMap.cpp:1011-1197`, the `DO_BRESENHAM` branch):
  * reads the **logic** height map (`getLogicHeightMap()`), vertex grid with `MAP_XY_FACTOR` = 10 and border:
    `start = floor(pos / 10) + border`, `end` likewise (1028-1031);
  * Bresenham over vertex squares, `numpixels = max(|dx|, |dy|)` steps, **start square checked, end square never** (`curpixel < numpixels`,
    1093); same square (`numpixels == 0`) -> clear;
  * the height of a square = **max of its four corner vertices** x `MAP_HEIGHT_SCALE` (10/16) (1104-1109) - not the interpolated surface;
  * the sight height starts at `pos.z` and grows by `dz / numpixels` per step (1084-1087, 1127);
  * blocked when `height > z + 0.5` (`LOS_FUDGE`, 1113-1118);
  * stops (clear) when the walk leaves the map (`x < 0 || y < 0 || x >= xExtent-1 || y >= yExtent-1`, 1095-1102) and when
    `z >= getMaxHeight() && zinc > 0` (1122-1125); `getMaxHeight()` = the highest vertex of the whole height map x scale
    (`BaseHeightMap.cpp:1820-1831`).
  * There is no bridge or cliff handling in the terrain test - only heights. Bridges matter only through the layer of the obstacle walk.

### 1.3 Which objects are obstacles (the cells the walk tests)
`Pathfinder::classifyObjectFootprint` (`AIPathfind.cpp:4117-4196`): never mines, projectiles, bridge towers; a template with `FenceWidth > 0`
and not `DEFENSIVE_WALL` goes through `classifyFence` (fence cells are obstacles too); otherwise only `KINDOF_STRUCTURE`, not mobile, not
`GeometryIsSmall`, not more than one cell (10) above terrain unless `BLAST_CRATER`. **All owners count, allies and own structures included.**
`PathfindCell::setTypeAsObstacle` (1445-1478): only a cell that is `CELL_CLEAR` or `CELL_IMPASSABLE` becomes an obstacle - **a cliff or water
cell under a structure stays cliff/water and does not block the view**; a structure in `BODY_RUBBLE` makes the cell `CELL_RUBBLE` (does not
block); the cell keeps **one** obstacle id - the first structure that claimed it (a cell already an obstacle is not overwritten) - and
`m_obstacleIsTransparent = obstacle->isKindOf(KINDOF_CAN_SEE_THROUGH_STRUCTURE)`. `removeObstacle` (1497-1508) clears the cell only when the
leaving object is that owner - and then clears it even if a second footprint overlaps (a quirk).

`attackBlockedByObstacleCallback` (`AIPathfind.cpp:9310-9358`): a cell blocks when it is `CELL_OBSTACLE` and its obstacle is **none of**: the
attacker; the victim; the victim's slaver (`SlavedUpdateInterface::getSlaverID`); the attacker's container; the attacker's slaver; a
transparent obstacle; the obstacle that covers the victim's cell ("victim is inside the bounds of another object ... usually on the edge",
9345-9353). The `@todo` about the victim's container is not done: the building a victim is garrisoned in is the victim's cell owner, so it does
not block.

`iterateCellsAlongLine` (9109-9205): `worldToCell` = `floor(pos / 10)` clamped to the extent; a 4-connected Bresenham - start and end cell
included, and on every minor-axis step the intermediate cell is visited as well; a cell outside the extent ends the walk as **not blocked**
(`getCell == NULL -> return 0`). For a non-ground layer `getCell` returns the layer cell if present, else the ground cell.

### 1.4 The callers and their extra conditions
| Where | Function | Condition before the call | Effect |
|---|---|---|---|
| `GE/GameLogic/AI/AIStates.cpp:1103-1157` | `outOfWeaponRangeObject` (state condition of `AIM_AT_TARGET` and `FIRE_WEAPON` of the attack machine, normal and forced, `AIStates.cpp:274-330`; also `TURRETAI_FIRE` of the turret machine, `TurretAI.cpp:91-101`) | `!weapon->isContactWeapon()` and `onGround` and `!victim->isSignificantlyAboveTerrain()`; `onGround` = `ai->isDoingGroundMovement()` or `KINDOF_IMMOBILE` or `KINDOF_SPAWNS_ARE_THE_WEAPONS` or contained in a structure or a non-airborne container (1114-1132); ignored while the weapon `hasLeechRange()` (1140) | blocked counts as out of range -> `CHASE_TARGET` |
| `AIStates.cpp:1193-1238` | `outOfWeaponRangePosition` (attack on a position) | `onGround` as above (no contact or airborne test) | blocked -> `CHASE_TARGET` |
| `AIStates.cpp:2594-2605` | `AIAttackApproachTargetState::onEnter` | in range, `ai->isDoingGroundMovement()`, victim not significantly above terrain | in range and clear -> `STATE_SUCCESS` (aim) without moving |
| `AIStates.cpp:2701-2712` | `...ApproachTargetState::updateInternal` (object) | `m_stopIfInRange` (= not following an attack path), in range, ground movement, victim not airborne | stop the approach when clear |
| `AIStates.cpp:2731-2742` | same (position) | `m_stopIfInRange`, in range, ground movement | stop when clear |
| `AIStates.cpp:3068-3090` | `AIAttackPursueTargetState::updateInternal` | ground movement, victim not airborne | only the speed matching of a turreted pursuit (blocked -> full speed) |
| `GE/GameLogic/Object/Update/AIUpdate.cpp:1835-1868` | `AIUpdateInterface::computeAttackPath` | in range; ground movement; for an object victim also not airborne | in range and clear -> no path needed |
| `AIPathfind.cpp:10621-10622`, `10726-10734` | `Pathfinder::findAttackPath` | candidate within goal range; in the A* only if `checkLOS` (victim NULL or not significantly above terrain) | the place to move to |
| `PartitionManager.cpp:5340-5351` | `PartitionFilterLineOfSight::allow` via `Pathfinder::isViewBlockedByObstacle` (`AIPathfind.cpp:9361-9380`) | acquisition, see 1.5 | candidate rejected |

`isContactWeapon` (`Weapon.cpp:555-567`, shipped branch): `attackRange * 1.05 < 10`. `isDoingGroundMovement` (`AIUpdate.cpp:2363-...`):
false for air-only locomotor sets, no current locomotor, an air-capable current locomotor, `DISABLED_HELD`, an airborne object allowed to fall;
true for an unmanned helipad unit. `isWithinAttackRange` itself does **not** test the view (`Weapon.cpp:2176-2232`; the comment in
`TurretAI.cpp:847-850` saying it does is stale).

### 1.5 Target acquisition
`AIUpdateInterface::getNextMoodTarget` (`AIUpdate.cpp:4609-4614`): with the global flag and the KindOf, `flags |= AI::CAN_SEE`.
`AI::findClosestEnemy` (`GE/GameLogic/AI/AI.cpp:654-655`) then adds `PartitionFilterLineOfSight` (filter order: live map enemies, reject
buildings, within attack range (human, called by AI), LOS, can attack, fog (human), insignificant buildings, stealth; filters are pure, so the
order is only cost). `PartitionFilterLineOfSight::allow` (`PartitionManager.cpp:5340-5351`):
1. `isClearLineOfSightTerrain(me, myPos, other, otherPos)` - **top of the attacker to top of the candidate**, for every candidate including
   aircraft and for IMMOBILE attackers too;
2. then `Pathfinder::isViewBlockedByObstacle(me, other)`: false when the candidate is significantly above terrain; otherwise the full
   `isAttackViewBlockedByObstacle(me, myPos, other, otherPos)` - i.e. the terrain test again (weapon origin to the victim's centre; skipped for
   IMMOBILE) plus the obstacle walk.
`EnemyNearUpdate` uses `CAN_SEE` too (not attack). The turret's idle look (`TurretAI::friend_checkForIdleMoodTarget`) and the approach/pursuit
states' temporary turret targets (`getNextMoodTarget(true,false)`, `AIStates.cpp:2780`, `3109`) go through the same function.

### 1.6 GeneralsGameCode
`Core/GameEngine/Source/GameLogic/AI/AIPathfind.cpp`, inside the `if (w)` branch, behind `!RETAIL_COMPATIBLE_CRC`: "Don't consider the attack
blocked if it is a contact weapon" (`return false` for `w->isContactWeapon()`, so suicide units touching a building detonate) -
`docs/GGC-FIXES-CATALOGUE.md:190` marks it "not applicable" because the port has no view test; once F-55 is in, it becomes a rule-profile
candidate (it is also Reforged R-09). Note it skips the obstacle walk too, not only the terrain part.

## 2. What the original does when the view is blocked

* **In the attack state** (object or position, normal or forced): the condition sends `AIM`/`FIRE` to `CHASE_TARGET`. For an `IMMOBILE` unit
  `CHASE_TARGET` is a `FailureState` (`AIStates.cpp:394-403`) - the attack ends; an immobile unit with the kind (Patriot, Fire Base, Gattling
  Cannon, tunnel) gives up a victim behind a structure (it ignores terrain). A portable structure (rider turret) falls back to aim
  (`ContinueState`, 333-351). Otherwise the pursue state (object only; turreted, allowed to chase) or the approach state runs.
* **Approach** (`AIAttackApproachTargetState`, `AIStates.cpp:2410-2514`, `2555-2789`): `computePath` calls `ai->requestAttackPath(victimID,
  &victimCentre)` (object) or `requestAttackPath(INVALID_ID, &goal)` (position, only when forced to repath); recomputation at most every
  `MIN_RECOMPUTE_TIME` = 10 frames (`AIStateMachine.h:479`) unless the path is gone or the unit is stuck, and for an object only when the victim
  moved (`isSamePosition`). A **human player's unit that acquired the victim itself** (`CMD_FROM_AI`, not `isAllowedToChase`, not retaliating,
  not a contact weapon) fails in `onEnter` (2610-2620): it drops the blocked victim instead of moving. A computer unit does not chase an airborne
  aircraft unless hunting.
* **The clear place** (`AIUpdateInterface::doPathfind` `AIUpdate.cpp:441-464` -> `computeAttackPath` 1789-2019 -> `Pathfinder::findAttackPath`
  `AIPathfind.cpp:10554-10906`):
  1. quick check (10596-10635): up to 9 points every half cell straight toward the victim (4.5 cells); the first that is a valid movement cell,
     passes `checkDestination` (the unit fits, no other unit's goal), is within `isGoalPosWithinAttackRange` (range - 2.5, min range + 2.5,
     `Weapon.cpp:2265-2312`) and not view-blocked -> a two-node path;
  2. otherwise an A* outward from the unit toward the victim's cell with a limit of 2,500 examined cells (`ATTACK_CELL_LIMIT`), heuristic
     `10 * euclid(cell, victimCell) - attackDistance/2` clamped at 0 (`examineNeighboringCells`, 6317-6326; `attackDistance` = the weapon's
     attack distance + 3 cells); the **first popped cell** that is in goal range, passes `checkDestination`, is not the start cell, is not within
     half a cell of the unit, and is not view-blocked (only tested when the victim is NULL or not significantly above terrain) is the goal;
     vehicles then strip back up to 12 cells past cells blocked by allies/enemies (10783-10839); the path is built to it;
  3. if the A* finds nothing it returns NULL; `doPathfind` then sends the unit on an ordinary path to a pathable place next to the victim,
     ignoring the victim as obstacle (`adjustToPossibleDestination`, 455-463), and `m_stopIfInRange` (= not an attack path) makes the approach
     stop as soon as the unit is in range **with a clear view** (2701-2712) - so it still finds a clear place by walking.
  4. If the found goal is not in goal range (the vehicle strip-back), a short move becomes a `findClosestPath` to the unit's own position
     (1982-1997).
  While following an attack path the approach does not stop early (`m_stopIfInRange = !ai->isAttackPath()`, 2674); it stops at the path's end
  and the machine returns to aim, which re-tests the view.
* **Acquisition**: a blocked candidate is skipped; the closest **visible** enemy is taken (or by priority with an attack info). A human unit
  looks only within weapon range, so a human unit whose only enemy in range is behind a building acquires nothing.
* **Forced attack on the ground**: `outOfWeaponRangePosition` with `victim = NULL` - obstacle walk to the point (no victim exclusions except the
  obstacle covering the point's cell) and terrain to the point's z; the approach repaths through `requestAttackPath(INVALID_ID, ...)` only when
  forced (no path / stuck), otherwise walks its old path and stops in range with a clear view. **The port has no attack-on-position command**, so
  this branch has no consumer today.
* **Pursuit** (turreted chase of a moving victim): blocked only changes the speed matching to "as fast as possible".

## 3. Where it belongs in the port, what exists, what is missing

| Piece | Port location | Exists | Missing |
|---|---|---|---|
| Global flag | `src/CnCNew.Simulation/Skirmish/Original/AiData.cs:37` lists `AttackUsesLineOfSight` among "other keys" (validated, not read); `AIData` is a compatibility definition (`baselines/compatibility.json:5486-5494`, 27 entries); `GameDataSchema.cs` has nothing (correctly - it is not GameData) | parse-validated only | the acquisition file already keeps AIData values as named constants (`SimulationWorld.Acquisition.cs:21-45`); a `private const bool AttackUsesLineOfSight = true` with the same comment is the consistent step (typing AIData is a separate, input-manifest-changing job) |
| KindOf bits | `NameTables.KindOf` has `ATTACK_NEEDS_LINE_OF_SIGHT`, `CAN_SEE_THROUGH_STRUCTURE`, `IMMOBILE`, `SPAWNS_ARE_THE_WEAPONS`; `ObjectTemplate.IsKindOf(string)` | yes | nothing (consider caching the bit on `ObjectTemplate` for speed - a new public property changes the input manifest, per the CLAUDE.md pitfall) |
| Geometry heights | `ObjectTemplate.GeometryHeight`, `GeometryMajorRadius`, `IsSphere`, `CenterZDelta` (`Templates/ObjectTemplate.cs:152-178`) | yes | a `MaxHeightAbovePosition` helper (sphere -> major radius, else height) |
| Terrain heights | `TerrainMap` (`Terrain/TerrainMap.cs`): raw vertex bytes `_heights`, `_vertexWidth/_vertexHeight`, `_border`, `HeightScale`; `HeightAt` (195-216) interpolates | data yes | the original's test: a Bresenham over vertex squares with the max of four corners, the global max height, the off-map stop. `HasHeightLineOfSight` (255-270, used only by `StealthDetectorUpdate`) is **not** it: it samples the interpolated surface at cell intervals with at most 64 samples and no fudge - write a new `IsClearLineOfSight(x0,y0,z0,x1,y1,z1)` beside it (fixed point; integer Bresenham; `zinc = dz / numpixels` as `Fixed`) |
| Obstacles in pathfind cells | `NavigationGrid` (`Terrain/NavigationGrid.cs`): `Blocks` (136-137: STRUCTURE, not MOBILE, not small), `Apply` (209-226) keeps a **count** per cell (`ushort[] _obstacles`), `IsBlockedByObstacle(cx,cy)` (246); footprints added at spawn/removed at destroy (`World/SimulationWorld.cs:1650`, `1919`) | cells yes | (1) **which object** owns a cell - the callback needs the id to except attacker, victim, slavers, container, the victim cell's owner; (2) transparency (`CAN_SEE_THROUGH_STRUCTURE`); (3) the original's rule that cliff/water cells are never obstacles; (4) `Blocks` lacks the original's mine/projectile/bridge-tower/airborne exclusions and the fence path (check whether any shipped LOS-relevant structure differs); (5) the footprint shape is the port's own (quarter-cell inflate), so the cells differ from the original's anyway - a named limit |
| Slaver / container ids | `ISlavedUpdate.Slaver` (`Modules/BuiltIn/SlaveModules.cs:35`), `Entity.ContainedBy` | yes | - (contained objects acquire nothing in the port, `MayAct`, `Acquisition.cs:247-249`, so the container exception has no consumer yet) |
| "airborne", "ground movement" | `IsSignificantlyAboveTerrainAtFrameEnd` (`World/SimulationWorld.CreationLists.cs:348-353`); locomotor surfaces on the template | yes | an `IsDoingGroundMovement` stand-in (locomotor not air) |
| Contact weapon | - | no | `range * 1.05 < 10` on `WeaponInfo` (pure, no manifest) |
| Layers (bridges, walls) | none - the port has no pathfind layers | - | `skipCount = 3` and the victim-layer walk are not portable; a named limit |
| Attack gate (acquired attacks) | `SimulationWorld.Acquisition.cs` `Engage` (298-345) uses `CanAttack(..., out inRange)` (509-531) | range only | `inRange &&= !AttackViewBlocked(...)` with the `outOfWeaponRangeObject` conditions; this also covers the turret-only mode (the turret machine uses the same condition) |
| Acquisition filter | `FindMoodTarget` (393-431) / `ConsiderMoodTarget` (448-479) | range, kind, attackable, structures | the `PartitionFilterLineOfSight` test (terrain top-to-top for every candidate, then the obstacle/terrain test for non-airborne ones), after the existing cheap tests |
| Ordered attacks | `FireWeaponCommand` pre-check `SimulationWorld.cs:856-867`, execution `1202-...`; `ChooseWeapon` (`SimulationWorld.WeaponSlots.cs:183-248`) is range-only; `WithinWeaponRange` (138-157) | range only | a rejection (new `CommandRejection` value at the end - "the original's out of range"; size the rejection array) when the source's view is blocked, in pre-check and execution alike |
| The approach of an ordered attack | **in the presentation**: `src/CnCNew.Presentation/Input/StandingOrders.cs:142-182` (`UpdateAttack`: `SimulationQueries.InRange` -> fire, else `PursueCommand` to the victim) and `SimulationQueries.InRange` (`Presentation/Input/SimulationQueries.cs:70-76`, range only) | range only | a public world query (`IsAttackViewBlocked(unit, victim)`) used by `InRange`/`CanFireAt`, so a blocked unit keeps pursuing instead of sending fire commands that are refused; for the clear place, see step (e) |
| The approach of a computer unit | `Engage` (324-344): attack-move only, `StartInternalMove(victimX, victimY)`, chase refresh every 15 frames, leash 2 x acquire radius | walks to the victim | stop only when in range **and** clear (falls out of the gate change); an attack-position search for the full fidelity |
| Script / module fire | `ScriptEngine.Actions.cs:529`, `705` (`FireWeaponCommand` from scripts and AI); `TryFireActiveWeapon` / `TryFireWeaponSlot` (`WeaponSlots.cs:402-435`, module attacks: "no visibility check") | - | decide per caller; the original's module attacks (e.g. `AssistedTargetingUpdate`) go through the attack state machine and so are view-gated - check per module before adding the gate |

**Side finding (not F-55):** the port's `StealthDetectorUpdate` refuses to detect behind terrain (`Modules/BuiltIn/Phase5BlockerModules.cs:598-600`,
`645`, added in Phase 10); the original's `StealthDetectorUpdate.cpp:189-194` has no line-of-sight filter at all. That is a fidelity deviation
of its own (worth a register entry).

## 4. The steps (one cause each)

| Step | What | State |
|---|---|---|
| (a) | The terrain line, `TerrainMap.IsClearLineOfSight`: the vertex Bresenham of 1.2 in fixed point | done (Phase 41), test `TerrainLineOfSightFollowsTheOriginal`; no behaviour change |
| (b) | The obstacle walk, `NavigationGrid.IsAttackLineBlocked`: the cell walk of 1.3 with the exceptions of `attackBlockedByObstacleCallback`; per cell the count, the sum of the ids and the opaque count of the footprints covering it | done (Phase 41), test `AttackLineBlockedByStructures`; no behaviour change |
| (c) | The attack gate, `SimulationWorld.AttackViewBlocks`/`IsAttackViewBlocked`: the conditions of `outOfWeaponRangeObject` (not a contact weapon, the attacker on the ground, the victim not significantly above the terrain), applied to the units' own attacks and turrets (`CanAttack`) and to an ordered attack (`ChooseWeaponInView`: a blocked view is `OutOfRange`, as the original counts it); the client's `InRange` keeps approaching while the view is blocked | Phase 41, test `AttackNeedsAClearView` |
| (d) | Acquisition skips a blocked candidate (`PartitionFilterLineOfSight`: terrain top to top for every candidate, then the step-(c) test for one on the ground) | open |
| (e) | The approach moves to a place with a clear view (`Pathfinder::findAttackPath`: nine half-cell steps toward the victim, then a search of at most 2,500 cells; failing that, walk next to the victim and stop where in range and clear) | open |

Differences named: where structure footprints overlap, the original keeps the first that claimed a cell and the port the set of them (a cell of several blocks
unless the victim's cell is covered by the same set) - so a restored world answers as a live one does; the footprints are the port's own (a quarter cell grown),
not `classifyObjectFootprint`'s; the port has no pathfind layers (bridges, walls), so `skipCount` and the victim's layer do not apply; a weapon's leech range is not
modelled; a rider in a container attacks nothing in the port, so the container case of "on the ground" has no consumer.

## 5. Risks
* **Rate invariance.** Positions are read at the frame end; terrain is static. The obstacle grid changes in `Destroys`/`Spawns`, after
  `Acquisition` (`World/TickPhase.cs`). At 30 Hz a structure removed in frame f is seen by every unit from frame f+1 on; at 60 Hz a
  structure removed in the first tick of a world frame is already gone for a unit whose own frame begins in the second tick (units decide on
  their own frame grid, offset by their spawn tick). This is the same frame-convention question as every other
  discrete state; add the scenes of (c) to (e) to the rate-equivalence suite and expect at least one case that needs "read the grid as of the
  frame start" or a decision in the frame's first tick.
* **Determinism.** Fixed point only; integer Bresenham; owner per cell order-independent (sum/count); the search orders by (cost, cell index);
  no `Dictionary` iteration (the grid's memo dictionary is keyed lookup only). The view test reads no randomness.
* **Performance.** The terrain walk is range/10 squares (15-40 for most weapons), the obstacle walk the same in cells; acquisition calls it per
  candidate per mood check, the gate per decision frame per engaged unit. Measure with `scripts/perf-profile.ps1`; the attack-position search
  (e) is the expensive part (up to 2,500 cells, plus a view test per accepted candidate).
* **Behaviour shift.** Large: the reference match is metastable and will move; balance not to be judged.

## 6. Not determined
* The exact `checkDestination` / `validMovementPosition` semantics the port's free-place logic should map to, and the original's
  `internal_classifyObjectFootprint` cell shape versus the port's footprint (I did not compare them cell by cell).
* Whether `getCell(layer, x, y)` for a non-ground layer changes anything on shipped maps with bridges (the port has no layers).
* Whether the port models `hasLeechRange` (grep found nothing named so); whether any port module attack (`TryFireActiveWeapon` callers) must be
  view-gated - each such module needs its original read.
* The register's 245 count versus the 254 blocks found by a naive parse; re-measure with the catalogue.
* How often overlapping structure footprints occur on shipped maps (the order-independent owner rule differs from the original only there).

# Firing from inside a container (F-60)

This document is the reading behind F-60 (`docs/plan/STAGE-1-PORT.md` §3): riders of a garrison, a transport, an Overlord or a Helix fire from it in the
original and never in the port. It was written at the start of Phase 41, before any of it was built; F-61 (`docs/STEALTH-GARRISON.md`) comes first.

Read-only research, 28 October 2026. Original = `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine` (paths below relative to `Source/GameLogic/`
unless they start with `Include/`). Port = the worktree `src\CnCNew.Simulation` at `22eebc6`. Game data: `game/content/Data/INI`, names only.

---

## 1. What the original does

### 1.1 Who may fire: `isPassengerAllowedToFire(id)` per container

The gate is `Object::isAbleToAttack` (Object/Object.cpp 3189-3192): a contained object can attack only if
`containedBy->getContain()->isPassengerAllowedToFire(getID())`. The answer per container class:

| Container | Function | Rule |
|---|---|---|
| `OpenContain` (base; tunnel, cave, parachute, heal, rider-change, internet-hack inherit it) | Contain/OpenContain.cpp 1151-1163 | `m_passengerAllowedToFire` (from `PassengersAllowedToFire`, default No, OpenContain.cpp 76/105/152; set to TRUE by `PassengersFireUpgrade`, Upgrade/PassengersFireUpgrade.cpp 60-64, never cleared); if the container is itself contained, the outer container's `isPassengerAllowedToFire()` (no id) decides. |
| `GarrisonContain` | GarrisonContain.cpp 1715-1724 | Always TRUE (the data flag is ignored) unless the building is `DISABLED_SUBDUED`. |
| `TransportContain` | TransportContain.cpp 594-623 | Rider must be `INFANTRY`; if the transport rides an Overlord-style container (`isSpecialOverlordStyleContainer`) that container decides with the id; else `OpenContain::isPassengerAllowedToFire()`. |
| `OverlordContain` | OverlordContain.cpp 577-593 | Rider must be `INFANTRY` or `PORTABLE_STRUCTURE`; FALSE when the Overlord is itself contained ("nested containment voids firing"); else the transport/open flag. |
| `HelixContain` | HelixContain.cpp 364-381 | FALSE when the Helix is contained; its portable structure always TRUE; an `INFANTRY` rider asks `TransportContain`; anything else FALSE. |
| `TunnelContain`, `CaveContain`, `ParachuteContain`, `HealContain` | (no override) | OpenContain flag, never set in data -> riders never fire. |

Kinds: there is no kind gate for a garrison (only its `AllowInsideKindOf`, default `INFANTRY`, GarrisonContain.cpp 58, plus `KINDOF_NO_GARRISON`).

Shipped users (game data scan, names only):
- `GarrisonContain`: 200 modules (all fire). `IsEnclosingContainer = No`: `AmericaFireBase`, `AirF_`/`Lazr_`/`SupW_AmericaFireBase` (4). `MobileGarrison = Yes`:
  `CabooseFullOfTerrorists`, `TrainCar04`, `TrainEngineVerySlow`. `ImmuneToClearBuildingAttacks = Yes`: 15 (fire bases, GLA palaces, China bunkers, `Boss_Bunker`).
- `PassengersAllowedToFire = Yes` on `TransportContain` (26): all Humvees (`AmericaVehicleHumvee` and the general variants, CINE/`Humvee1` variants), all
  `*GLAVehicleBattleBus*`, `ChinaVehicleListeningOutpost` (+ Infa/Nuke/Tank), `Infa_ChinaVehicleTroopCrawler` (only the Infantry general's), `AirF_AmericaVehicleChinook`,
  `ChinaTankOverlordBattleBunker` (+ Nuke/Tank).
- On `OverlordContain` (10): every Overlord/Emperor/Avenger (`PassengersInTurret = Yes` on the five Overlord/Emperor ones).
- On `HelixContain`: Yes on `AmericaJetSpectreGunship*` (6 variants) and `Infa_ChinaHelixBattleBunker`; No on the five `*ChinaVehicleHelix`, which get it through
  `PassengersFireUpgrade` (the five Helix templates carry that upgrade module).
- `InternetHackContain`: `PassengersAllowedToFire = No` on all four internet centres - the hackers never fire (they have no damage weapon anyway). The brief's
  "hackers via the flag" is the flag's inherited existence only.
- `WeaponBonusPassedToPassengers = Yes`: the six Battle Bus templates. `ArmedRidersUpgradeMyWeaponSet = Yes`: Battle Buses, Listening Outposts,
  `Infa_ChinaVehicleTroopCrawler`, `AirF_AmericaVehicleChinook` (their `PLAYER_UPGRADE` weapon sets hold dummy weapons, e.g. `BattleBusPassengerDummyWeapon`,
  `ListeningOutpostUpgradedDummyWeapon`).
- Tunnel networks (12 templates): no flag - riders do not fire.

The Overlord's and the Helix's portable structures (gattling cannon, propaganda tower, battle bunker) are riders too, not enclosed (`OverlordContain::isEnclosingContainerFor`
494-503 for the first rider; `HelixContain::isEnclosingContainerFor` 329-340), and fire by the rules above. The riders of a battle bunker on an Overlord ask through
`TransportContain` -> the Overlord (Overlord-style redirection).

### 1.2 HELD and firing

Every firing container sets `DISABLED_HELD` on the rider (`GarrisonContain::onContaining` 1623, `TransportContain::onContaining` 288, `HelixContain::onContaining` 399; tunnel,
cave, parachute, mob nexus too). HELD does not stop the shot:
- `AIUpdateInterface::getDisabledTypesToProcess` returns `DISABLED_HELD` (Include/GameLogic/Module/AIUpdate.h 306): the AI update, i.e. idle acquisition and the
  attack state machine, runs on a held object (the GameLogic loop, System/GameLogic.cpp 3702/3742, skips a module only for a disabled type it does not process).
  Also processing HELD: `StealthUpdate`, `StealthDetectorUpdate`, `AutoHealBehavior`, `CountermeasuresBehavior`; physics, poison, slow death etc. process all.
- HELD makes `Object::isMobile()` false (Object.cpp 2902-2911, any disabled bit), so `privateMoveToPosition` and friends refuse (AIUpdate.cpp 2890 etc.), the
  approach/pursue states cannot compute a path (AIStates.cpp 2415, 2844), and `isDoingGroundMovement` is false (AIUpdate.cpp 2389).
- `AIAttackAimAtTargetState::update` (AIStates.cpp 5101-5125): a HELD attacker fails the state as soon as its victim (or position) is out of range, "so it can fire on
  other targets".
- `AIGroup` skips HELD members for every move-like group order (AIGroup.cpp 299, 325, 359, ... 2139).
- Riders get `OBJECT_STATUS_UNSELECTABLE`, enclosed ones also `MASKED` (`Object::onContainedBy`, Object.cpp 695-707), and lose their own look
  (`handlePartitionCellMaintenance`). An enclosed rider is unregistered from the partition manager and its drawable hidden (`OpenContain::addOrRemoveObjFromWorld`
  223-285, called from `addToContain` 344-347 only when `isEnclosingContainerFor(rider)`).

### 1.3 Where the rider stands and fires from

`Weapon::calcProjectileLaunchPosition` (Object/Weapon.cpp 2937-2955): a launcher in an enclosing container launches from its own transform (its position), not a
bone of its model. Range checks (`Weapon::isWithinAttackRange`) also measure from the rider's position. So the rider's position is what matters:

**Enclosing garrison** (`GarrisonContain`, `IsEnclosingContainer` default TRUE, Include/GameLogic/Module/GarrisonContain.h 120):
- Garrison points: `MAX_GARRISON_POINTS = 40` (GarrisonContain.h 188), three condition sets PRISTINE / DAMAGED / REALLY_DAMAGED (h 224-228).
  `loadGarrisonPoints` (cpp 1306-1405), called when the first rider enters (`recalcApparentControllingPlayer` 1286-1303): all 120 points start at the building's
  position; then the drawable is switched to {GARRISONED}, {GARRISONED, DAMAGED}, {GARRISONED, REALLY_DAMAGED} (on top of whatever flags it had - e.g. night/snow;
  GARRISONED is never cleared between the three steps) and `getMultiLogicalBonePosition("FIREPOINT", 40, ...)` reads `FIREPOINT01..FIREPOINTnn` (stops at the first
  missing one; `W3DModelDraw::getPristineBonePositionsForConditionState`, `%s%02d` from 1) converted to world coordinates. The table is computed once and cached
  ("assumes that we will never move"); points beyond the bone count stay at the building's centre and are candidates too.
- Condition index (`findConditionIndex` 235-276): PRISTINE, DAMAGED, REALLY_DAMAGED and RUBBLE -> REALLY_DAMAGED.
- `findClosestFreeGarrisonPointIndex` (94-128): the free point with the smallest 3D squared distance to the target, first index on ties.
- A rider without a victim stands at the building's centre (`onContaining` 1632-1633; `removeObjectFromGarrisonPoint` 430-499 puts it back at the centre).
- Placement by victim:
  - Acquisition range test (`WeaponSet::getAbleToUseWeaponAgainstTarget`, Object/WeaponSet.cpp 605-690): for an enclosing garrison the range is tested from
    `calcBestGarrisonPosition` (280-306) = the closest *free* point (the rider's own occupied point is not free - quirk), via
    `isSourceObjectWithGoalPositionWithinAttackRange`. If no free point exists, `withinAttackRange` stays FALSE. A contained source out of range gets
    `ATTACKRESULT_INVALID_SHOT` (WeaponSet.cpp 677-681) - riders never "attack after moving".
  - Entering the aim state (`AIAttackAimAtTargetState::onEnter`, AIStates.cpp 4904-4945): `attemptBestFirePointPosition` (312-391) takes the rider off its point,
    puts it on the closest free point, and keeps it there only if the weapon is in range from it; else back to the centre and the state fails.
  - The building's update, every logic frame (`GarrisonContain::update` 909-972, `UPDATE_SLEEP_NONE`; `matchObjectsToGarrisonPoints` 977-1000):
    `removeInvalidObjectsFromGarrisonPoints` (588-631: a rider whose goal object is out of range, or that is not `OBJECT_STATUS_IS_ATTACKING`, goes back to the
    centre), `addValidObjectsToGarrisonPoints` (637-676: a rider with a current victim or victim position gets the best point), `updateEffects` (muzzle flash),
    `trackTargets` (682-777: a rider at a point moves to a free point closer to its victim's current position, strictly closer).
  - `redeployOccupants` (785-826) on every model-condition change (`OpenContain::monitorConditionChanges` 1172-1196) re-matches; the garrison points themselves are
    not reloaded (only station points are).
- The attack machine's first state is AIM (AIStates.cpp 307-319, "so things that cannot move will be able to shoot"); out of range the CHASE/APPROACH states fail
  for an immobile rider (approach `onEnter` 2555-2650 tests range from the rider's *current* position, then `computePath` fails because not mobile).
- Mobile garrison (trains): `moveObjectsWithMe` (1729-1742) puts every rider on the container's position each frame (garrison points are cached at the first
  entry, which the code asserts against for mobile garrisons with bones).

**Non-enclosing garrison** (the four fire bases): riders are not removed from the world, stay visible and targetable, sit at `STATION01..` bones
(`loadStationGarrisonPoints` 1982-2061, read once for {GARRISONED}, up to `ContainMax`), one station each in entry order (`pickAStationForMe` 1051-1067), kept there every
frame (`positionObjectsAtStationGarrisonPoints` 1007-1045). No garrison-point juggling, no GarrisonGun drawable. On leaving, z is put back on the ground (1659-1665).

**Transports** (`OpenContain::redeployOccupants` 1198-1222 / `putObjAtNextFirePoint` 1227-1295): called on every add (`addToContain` 350), on every transform change of
the container (`containReactToTransformChange` 192-196, i.e. every move) and on model-condition change. `FIREPOINTnn` bones of the container (up to 32), riders in entry
order get points 1, 2, ... (round robin); no bones -> the container's position. `PassengersInTurret` (Overlord): the bone is taken on `TURRET_MAIN`
(`getSingleLogicalBonePositionOnTurret`), so the portable structure turns with the turret; a non-enclosed rider gets the whole transform (position and heading).
`HelixContain::redeployOccupants` (136-150): every rider at the Helix's position + 8 in z; the portable structure follows position and heading in `update` (122-132).

### 1.4 The rider's AI while contained

- Idle acquisition: `AIUpdateInterface::getNextMoodTarget` (AIUpdate.cpp 4495-4672) as for any unit (mood check rate, offset, team target), with these differences:
  - Search radius: `AI::getAdjustedVisionRangeForObject` (AI/AI.cpp 805-860): for a contained object the range is `getLargestWeaponRange()` (weapon range *with*
    bonuses, `Weapon::getAttackRange` -> `computeBonus`, Weapon.cpp 2360-2365) instead of the vision range, and the mood switch (Sleep -> 0, Alert/Aggressive factors)
    is skipped (it is in the `else`). Then `getNextMoodTarget` adds the container's bounding circle radius (4589-4594).
  - A stealthed rider without `AutoAcquireEnemiesWhenIdle ... Stealthed` may still acquire when its container lets it fire (4526-4538).
  - Flags as usual: `WITHIN_ATTACK_RANGE` and `UNFOGGED` for a human player's unit called by AI; the candidate filter calls `getAbleToAttackSpecificObject`, i.e. the
    garrison-point range test above.
  - A Passive computer unit returns its last attacker (4597-4609) - independent of containment.
- Turning: the aim state turns a turret or (with a zero min-speed locomotor) the body (5017-5073); the rider is hidden. The port gates no body facing, so this does
  not matter there.
- Orders: the rider cannot be selected. A player's or script's attack order on the container is forwarded to every rider that can attack the target:
  `AIGroup::groupAttackObject` (AIGroup.cpp 2149-2177, `getAbleToAttackSpecificObject` per rider, then `aiAttackObject`/`aiForceAttackObject`) and
  `groupAttackPosition` (2236-2260); `TransportAIUpdate::privateAttackObject/ForceAttackObject/AttackPosition` (Object/Update/AIUpdate/TransportAIUpdate.cpp 67-190, only
  for `CMD_FROM_PLAYER`/`CMD_FROM_SCRIPT`, skipping a disabled portable structure); `ChinookAIUpdate` re-issues its victim to passengers every 10 frames (1115-1140) and on
  orders (1435-1470). `Object::isAbleToAttack` makes a weaponless transport with allowed riders "able to attack" (Object.cpp 3296-3300), and the weapon-set test passes
  through riders (WeaponSet.cpp 740-760).
- `TransportContain::letRidersUpgradeWeaponSet` (TransportContain.cpp ~220-275): with `ArmedRidersUpgradeMyWeaponSet` the transport's `WEAPONSET_PLAYER_UPGRADE` is set
  while any infantry rider has a non-contact damage weapon (gives the Battle Bus etc. its dummy weapon, hence an attack range to approach to).

### 1.5 Weapon bonus, range, vision

- `WEAPONBONUSCONDITION_GARRISONED` is set on entry and cleared on exit by `GarrisonContain` (1629 / 1669) and `HelixContain` (398 / 425) only - **not** by
  `TransportContain` (Humvee, Battle Bus, Overlord bunker riders get none). GameData.ini: `WeaponBonus = GARRISONED RANGE 133%` and `GARRISONED DAMAGE 125%`; no
  weapon of `Weapon.ini` has its own GARRISONED line.
- `WeaponBonusPassedToPassengers` (Battle Bus): `Weapon::computeBonus` (Weapon.cpp 1822-1841) ORs the container's whole bonus condition set into the rider's.
- The mood radius adds the container's bounding circle radius (above); the weapon range itself is not extended (the old code that did is commented out, Weapon.cpp 2366-2371).
- The rider's own vision is switched off while contained; the garrisoned building passes to the rider's player (`recalcApparentControllingPlayer` 1206-1304,
  `setTeam`) and sees with its own vision - that is F-61. The building gets `OBJECT_STATUS_CAN_ATTACK` (1626) and loses it when empty (1693).
- Attack view (F-55): a rider of a structure or of a container that is not an airborne target counts as on the ground (`outOfWeaponRangeObject` AIStates.cpp 1127-1132,
  `outOfWeaponRangePosition` 1215-1220); the attacker's own container is never an obstacle (`lineBlockedByObstacleCallback`/`attackBlockedByObstacleCallback`,
  AI/AIPathfind.cpp 9278-9280, 9333-9335).
- Muzzle flash (client): a `GarrisonGun` drawable per occupied point (enclosing garrisons only, `putObjectAtGarrisonPoint` 189-211), oriented to the victim
  (`trackTargets`), `FIRING_A` for `LOGICFRAMES_PER_SECOND / 7` = 4 frames after a shot of the previous frame, none for `DAMAGE_POISON` (`updateEffects` 832-904).

### 1.6 How a garrisoned shooter can be hurt

An enclosed rider is not in the partition manager, so no area damage finds it, and `getAbleToAttackSpecificObject` refuses a victim in an enclosing container
(WeaponSet.cpp 569-574). It is hurt only by:
- the container's death: `OpenContain::onDie` (857-870) -> `processDamageToContained` (1441-1474): `MaxHealth * DamagePercentToUnits`, `DAMAGE_UNRESISTABLE`, death
  `BURNED` if `BurnedDeathToUnits` else `NORMAL`, and a kill when the percentage is 1.0 and it survived; then `killRidersWhoAreNotFreeToExit`, `removeAllContained`.
  (Battle Bus slow death: `processDamageToContained(m_percentDamageToPassengers)`, BattleBusSlowDeathBehavior.cpp 179.)
- `DAMAGE_KILL_GARRISONED` (`ActiveBody::attemptDamage` 445-480; estimate 296-303): kills `floor(amount)` riders of a garrisonable, non-immune container. Shipped:
  weapon `MicrowaveTankBuildingClearer`.
- `GarrisonHitKill*` of a projectile (`DumbProjectileBehavior::projectileHandleCollision` 481-516, `MissileAIUpdate` 343): on hitting a garrisoned, non-immune building
  kill up to `GarrisonHitKillCount` riders of the required kinds, play the FX, destroy the projectile without its explosion. Shipped: `RangerFlashBangGrenade` (10),
  `DragonTankFlameProjectile`(+Upgraded) 2, `ToxinTruckStreamProjectile`(+Upgraded) 2, `Chem_`/`GC_Chem_ToxinStreamProjectile` 2, `GC_Slth_JarmenKellBullet` 1.
- `AllowAttackGarrisonedBldgs` makes `estimateWeaponDamage` non-zero against a garrisoned building (Weapon.cpp 613-620) so those weapons target it: 17 weapons
  (Dragon tank flames, toxin truck guns, ranger flash bang, rebel/tunnel Chem guns, `GC_Slth_JarmenKellRifle`).
- Combat drop (`AIRappelState`, AIStates.cpp 439-466, 575-600): kills up to two enemies in the building.
- Damage to the building itself does not reach the riders while it lives; at `REALLYDAMAGED` (unless `GARRISONABLE_UNTIL_DESTROYED`) all are ordered out
  (`onBodyDamageStateChange` 1748-1758).

A non-enclosed rider (fire base station, Overlord/Helix portable structure, parachute rider) stays in the partition and can be targeted and hit directly.

### 1.7 Exit

`GarrisonContain::onRemoving` (1653-1711): off its garrison point (back to the centre) or off its station (z to the ground), GARRISONED cleared, HELD cleared, the building
back to its original team and `CAN_ATTACK` cleared when empty; then `exitObjectViaDoor` (1497-1610) places it and gives `aiFollowPath` - which replaces the attack state.
`TransportContain::onRemoving` (330-...): HELD cleared, exit bone, orientation, velocity. `HelixContain::onRemoving` clears GARRISONED and HELD.

---

## 2. What the port has and lacks

| Piece | Port today | Where it belongs |
|---|---|---|
| Firing gate | `MayAct` excludes every contained object (World/SimulationWorld.Acquisition.cs 245-249) | `MayAct` asks a new `IContainerModule.IsPassengerAllowedToFire(world, rider)` (default false), implemented per class in ContainModules.cs / ContainChainModules.cs / TransportModules.cs (Helix) |
| The flag | `ContainModuleBase.PassengersAllowedToFire` (ContainModules.cs 121-125), set only by `InternetHackContain`, `OverlordContain`, `RiderChangeContain` (ContainChainModules.cs 99, 361, 189) and `PassengersFireUpgrade` (122). **Plain `TransportContain` and `HelixContain` never read it from data** (`TransportContainData.Parse` 440-449 has no such key) - the Humvee, Battle Bus, Listening Outpost, Chinook, battle bunker, Spectre would stay false | `TransportContainData` (and Helix data) parse `PassengersAllowedToFire` |
| Nesting rules (Overlord/Helix contained -> no fire; infantry-only; portable structure) | absent | per-class implementation above |
| HELD | `DisabledFlagsAt` adds `Held` for riders of a container with `HoldsRiders` (World/SimulationWorld.Scripting.cs 95-100; ModuleFramework.cs 748-753; `HealContain` false, RetailMatchModules.cs 62). `AIUpdateInterface` processes `Held` (Phase5Modules.cs 646). Acquisition is a world phase (`StepAcquisition`, Acquisition.cs 123-146), not gated by disabled types; `MayAct` checks EMP and the script-HELD bit only | fine as is: the acquisition must ignore container-HELD (as the original's AI processes HELD) |
| Search radius of a rider | `AcquireRadius` (Acquisition.cs 368-381): weapon reach for a non-chaser; no container radius | add the container's `BoundingCircleRadius`; for a contained unit ignore Sleep/Passive range zeroing (`FindMoodTarget` 395-430 returns null for Sleep/Passive first) and the stealth veto in `ModeOf` (254-262) when the container allows firing |
| Range from a fire point | `WithinWeaponRange` (WeaponSlots.cs 138-157) measures from `source.X/Y` (`FrameEnd` above 30 Hz) | for an enclosing garrison: from the best free garrison point (`calcBestGarrisonPosition`); otherwise from the rider's position, which must be kept on its fire point |
| No chase for riders | `MayChase` (Acquisition.cs 363) - a computer player's contained unit would try to walk | `MayChase` false when contained |
| Garrison points | none: no `FIREPOINT`/`STATION` bone anywhere in the code; GarrisonContain doc says "garrison points/positions (art-derived, not INI data)" not modelled (ContainModules.cs 258-268) | bone table: `CnCNew.Game/Data/ModelGeometryLoader.cs` (it already reads exit and airfield bones, exact transform, 1/16 unit) -> `Templates/ModelGeometry.cs`; occupancy state in `GarrisonContain` |
| Rider positions | set once to the container's frame-end position on `Add` (ContainModules.cs 141-155); riders of a moving transport are not moved; Overlord's portable structure follows position/heading (ContainChainModules.cs 396-415); ferry moves its riders (FerryModules.cs 475) | `ContainModuleBase` (fire points / centre), `GarrisonContain` (points/stations), Helix (+8 z) |
| GARRISONED bonus | weapon bonus conditions exist (`Entity.WeaponBonusConditions`, World/Entity.cs 141-142; `WeaponInfo.RangeWith/DamageWith`, GameData global table); GARRISONED never set | `GarrisonContain.Add/Remove`, `HelixContain.Add/Remove` |
| `WeaponBonusPassedToPassengers` | typed Opaque, unread (Data/Registry/ModuleCatalog.cs 493 ff.) | one helper "effective bonus conditions of a shooter" used by every reader of `WeaponBonusConditions` for a shot (Combat.cs 24, Projectiles.cs 51, WeaponSlots.cs 86/152, AttackView.cs 145, Turrets.cs 469, FiringTracker 184, CreationLists 187) |
| Attack view of a rider | AttackView.cs 28-31: "a contained object attacks nothing here"; excepts the attacker's container as obstacle already (93) | onGround = true when the container is a structure or not an airborne target |
| Orders through the container | `FireWeaponCommand` on a building is rejected `NoWeapon` (SimulationWorld.cs 856-866); client cannot pick contained entities (Presentation/Input/EntityPicker.cs 49) | `FireWeaponCommand` (and attack-position / force-attack paths) with a container as source: forward to each rider that may fire and can attack (as `NoteOrderedAttack`), like `AIGroup::groupAttackObject` and `TransportAIUpdate` |
| `ArmedRidersUpgradeMyWeaponSet` | typed Opaque, unread (ModuleCatalog.cs 678, 817; ContainModules.cs 459-461 says "no WeaponBonus system", out of date) | `TransportContain.Add/Remove` sets/clears the `PLAYER_UPGRADE` weapon set flag |
| Portable structures firing (Overlord gattling, Helix gattling, Spectre gattling) | riders, hence excluded by `MayAct` too (the Spectre places its gattling into its container, AirWeaponModules.cs 1183-1193) - **the Overlord's gattling upgrade cannot fire in the port today** (not run, read from code) | covered by the same gate (Overlord: portable allowed; Helix: portable always) |
| Victims inside containers | `IsAttackable` (Acquisition.cs 289-291) and area damage (Combat.cs 63) skip every contained object; the original skips only enclosed ones | separate small cause: enclosure per container (FieldEffectModules.cs 62-77 already has `EnclosedAnywhere` logic; typed `IsEnclosingContainer` needed) |
| Clearing garrisons | `KILL_GARRISONED` returns `NoEffect` in the body (Bodies.cs 112); `GarrisonHitKill*` typed, "not applied" (ProjectileModules.cs 59, 408); `AllowAttackGarrisonedBldgs` mentioned only in a comment (WeaponSlots.cs 119) | Bodies.cs, projectile collision, weapon damage estimate - its own finding (see 3, step 9) |
| Building ownership / CAN_ATTACK | absent (no `SetOwner` from a container; only capture and scripts call it) | F-61 |
| Presentation | contained entities are not drawn (Presentation/Objects/FrameProjector.cs 199); `ConditionDeriver.cs` 104 sets GARRISONED; no GarrisonGun, no station riders | `FrameProjector` + snapshot of point occupancy |
| Container death damage | `ContainModuleBase.OnDie` (ContainModules.cs 166-178) uses `rider.Body.Health * DamagePercentToUnits`, death `Normal`, no kill at 100 % - the original uses **MaxHealth**, `BURNED` with `BurnedDeathToUnits`, and kills at 1.0 | side finding (own cause; changes results wherever a garrison/transport with riders dies) |

---

## 3. Proposed plan (one cause per step)

General rules for every step: decisions on the original 30 Hz frame (the rider's acquisition already runs in the first tick of the rider's own frame,
Acquisition.cs 137-140; the garrison's point matching must run in the first tick of the container's own frame); positions read at `FrameEnd` above 30 Hz; fixed point
only (`Fixed`, `FixedTrig`, bone poses from the exact loader); garrison point arrays as fixed-index arrays (ties -> lowest index, exactly as the original); riders in
entry order (`List<EntityId>`, as now); no dictionary iteration in a decision; new state written only when it differs from the default, so runs without
garrisons keep their hashes.

0. **Type the keys (no behaviour).** `PassengersAllowedToFire` in `TransportContainData` and the Helix data (sets the flag at spawn); `IsEnclosingContainer`,
   `MobileGarrison`, `ImmuneToClearBuildingAttacks` in `GarrisonContainData`; `WeaponBonusPassedToPassengers`, `PassengersInTurret`,
   `ArmedRidersUpgradeMyWeaponSet`, `BurnedDeathToUnits` as typed fields. Compatibility baseline entries leave Opaque (`baselines/compatibility.json`, review). May change
   the input manifest if module data records are part of it (a new field on a catalogue type does). Test: data tests read the flags of a synthetic fixture.

1. **Fire-point bones (no behaviour; fingerprint).** `ModelGeometryLoader`: for every `GarrisonContain` object `FIREPOINT01..40` of the states the original uses
   ({GARRISONED}, {GARRISONED, DAMAGED}, {GARRISONED, REALLYDAMAGED}) and `STATION01..ContainMax` of {GARRISONED} for non-enclosing ones; for every
   transport/Overlord/Helix `FIREPOINT01..32` of the empty state (on the turret for `PassengersInTurret`). Part of the world fingerprint -> a manifest commit that
   changes only `commandStreamSha256`/`snapshotSha256` fields (as the master-installation step of 9 October). Not determined here: which shipped models carry these
   bones, and whether a draw module's instance scale applies (the loader's audit must count them); the night/snow model variants the original would have cached
   at first entry (proposal: the day/no-snow states; register the deviation).

2. **`IsPassengerAllowedToFire` per container (no behaviour).** Pure functions per class, as 1.1; unit tests: garrison yes / subdued no; Humvee infantry yes, vehicle no;
   tunnel no; Overlord gattling yes, Overlord inside a Helix no; Helix gattling yes, Helix infantry only with the upgrade; bunker rider on an Overlord asks the Overlord.

3. **GARRISONED and passed bonuses.** Set/clear the bit in `GarrisonContain` and `HelixContain` Add/Remove; one helper `ShotConditions(source)` that ORs the
   container's conditions when `WeaponBonusPassedToPassengers`, used by every reader listed in section 2. Test: a rider's range query gives 133 %, damage 125 %; a
   Battle Bus rider inherits the bus's ENTHUSIASTIC. Hash-only change for runs with garrisoned riders (own manifest commit if any field moves).

4. **Rider positions.** Garrison: per building a 40-entry occupancy array (rider id, target id, place frame) and the condition index; riders without a victim at the
   centre. Transports: riders at the container's fire points in entry order (round robin from 1, every tick the container's pose changed - a pure function of the
   container's position/angle, so no per-rider state is needed); Helix riders at the Helix + 8 z; Overlord's portable structure on the turret bone. Non-enclosing
   garrison: station per rider in entry order. Above 30 Hz the rider's position is derived from the container's `FrameEnd`. Test: a scene with a synthetic building
   template whose geometry has two fire points (east/west) - without a victim the rider is at the centre; a Humvee rider moves with the Humvee. Changes hashes of
   runs with riders (rider coordinates are state).

5. **Firing from inside (the behaviour change).** `MayAct` lets a contained rider act when its container allows it; `AcquireRadius` = largest weapon range
   (with step 3's conditions) + container bounding circle radius, mood Sleep/Passive range zeroing and the stealth veto skipped for a contained rider; `CanAttack`'s
   range test from `calcBestGarrisonPosition` (closest *free* point, own point excluded, no free point -> out of range; including the 40 - k centre points) for an
   enclosing garrison, else from the rider's position; `MayChase` false; `Engage` places the rider on the best point before the range test
   (`attemptBestFirePointPosition`) and drops the victim when out of range (the HELD branch of the aim state); the garrison's per-frame matching
   (`removeInvalid`/`addValid`/`trackTargets`) in the container's first frame tick, reading victims at `FrameEnd`; `AttackView` treats the rider as on the ground; on
   exit the acquisition state is cleared (`EndEngagement`). `GarrisonContain` must stay awake while it has riders (today it sleeps forever without healing).
   Synthetic scenes that fail before (no shot today): (a) a rifleman in a two-point building, enemy east: it fires, from the east point; enemy moves around to the
   west: it changes point; (b) enemy beyond range from the centre but within 133 % range from the near point: it fires; (c) Humvee with a rifleman fires; a
   plain transport (flag No) and a tunnel do not; (d) Overlord with gattling fires at infantry; the Overlord inside a Helix does not; (e) all points occupied: a
   new rider does not acquire (the original's quirk); (f) the same first-shot frame, point and shot count at 30, 60 and 120 Hz (also a rate-equivalence case);
   (g) save/restore in the middle of a garrison fight continues identically. **Moves most results**: every mission run with scripted garrisons, every run with
   Overlord gattling/Helix upgrades or loaded Humvees/Battle Buses (the built-in `SkirmishAi` does not garrison; whether it loads transports or buys the gattling was
   not determined) - its own manifest commit; reference match driver seed re-checked.

6. **Orders through the container.** A `FireWeaponCommand` (and force-attack/attack-position) whose source is a container with allowed riders forwards to every rider
   that can attack the target (`AIGroup::groupAttackObject`, `TransportAIUpdate`), as ordered attacks; the Chinook's 10-frame re-issue. Test: a garrisoned building ordered
   to fire at a target in range of a point: the riders fire; out of range: no rider is ordered. The client side (cursor on a garrisoned building) follows.

7. **`ArmedRidersUpgradeMyWeaponSet`.** The transport's `PLAYER_UPGRADE` weapon set while an infantry rider with a non-contact damage weapon is inside. Test: a Battle Bus
   with a rifleman has the dummy weapon set, empty it has none.

8. **Presentation (drawing only).** The snapshot carries the occupied garrison points (rider, point position, victim); `FrameProjector` draws a `GarrisonGun` at each
   occupied point of an enclosing garrison, oriented to the victim, with `FIRING_A` for 4 original frames after a shot (none for poison); fire-base riders drawn at
   their stations. Picture pins of scenes with garrisons change.

9. **Not F-60 but needed next (register separately):** F-61 (building passes to its riders' player - without it an enemy never acquires a neutral garrisoned
   building, `IsAttackable`/`AreEnemies`, and a human's garrison has no vision of its own; with step 5 alone a garrison is an unassailable gun: doing F-61
   before step 5 avoids an unbalanced intermediate manifest); clearing garrisons (`KILL_GARRISONED`, `GarrisonHitKill*`, `AllowAttackGarrisonedBldgs`); the
   container-death damage of `OnDie` (MaxHealth, burned, kill at 100 %); the enclosure test for victims (`IsAttackable`, area damage) so fire-base riders and portable
   structures can be hit.

Rate-invariance notes: the garrison point choice depends on the victim's position - read it at `FrameEnd` in the container's first frame tick, and place in
`Engage` at the rider's frame start, so 60/120 Hz pick the same point as 30 Hz; the muzzle-flash window is in original frames; transport fire points follow the
container's frame-end pose, never a mid-frame interpolation.

---

## 4. Not determined

- Which shipped building/vehicle models have `FIREPOINT`/`STATION` bones and how many (needs the loader's audit); whether instance scale enters
  `convertBonePosToWorldPos`.
- The update order of the container and its riders within one original frame (the original's update list); the plan fixes an order (rider decides at its frame
  start, container matches at its frame start) that must be checked against 30 Hz traces of the original only by reasoning.
- Which garrison points the original caches when the first rider enters at night or in snow (it keeps those flags).
- Whether the built-in `SkirmishAi`, the original-AI session or the reference-match driver ever loads a firing transport or buys the Overlord gattling (decides
  which pairings move).
- Whether the port's Spectre gunship gattling fires through a path that bypasses `MayAct` (AirWeaponModules.cs 1013-1200 not read in full).
- `MobNexusContain`, `RailedTransportContain`, `RiderChangeContain` were not read for firing (no shipped `PassengersAllowedToFire = Yes` on them).

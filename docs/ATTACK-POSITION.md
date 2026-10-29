# An attack on a place (`AI_ATTACK_POSITION`)

Read on 29 October 2026 for the rest of F-60 (`groupAttackPosition` through a container, `docs/FIRE-FROM-CONTAINERS.md`) and registered as F-64
(`docs/plan/STAGE-1-PORT.md` §3). Nothing of it is built; this is the starting point of the work. Sources: EA's released code
(`references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source`), read, never copied.

## 1. What the original does

**Who orders an attack on a place.** Everything ends in `AIUpdateInterface::aiAttackPosition(pos, maxShots, source)`:

| Caller | Place | Shots |
|---|---|---|
| The player's force-fire on the ground (`MSG_DO_FORCE_ATTACK_GROUND`, `GameLogicDispatch.cpp` 1255-1280): a group that is not idle locks the primary weapon temporarily first | the ground clicked | no limit |
| A `FIRE_WEAPON` button with `NEED_TARGET_POS` (`MSG_DO_WEAPON_AT_LOCATION`): the button's slot locked temporarily | the ground clicked | the button's `MaxShotsToFire` |
| A `FIRE_WEAPON` button without a target (`MSG_DO_WEAPON`): the button's slot locked temporarily | each member's own position (`groupAttackPosition(NULL)`) | the button's `MaxShotsToFire` |
| A `FIRE_WEAPON` button with `ATTACK_OBJECTS_POSITION` on an object (`Object::doCommandButtonAtObject`) | the object's position | the button's |
| `FireWeaponPower` (`doSpecialPower`, `doSpecialPowerAtLocation`): reloads all ammunition first and aims every turret at the place | the place, or the own position | `MaxShotsToFire` |
| `AIGroup::groupAttackPosition` passes the order to the riders of a container whose passengers may fire (when they can use a weapon against the place, here or after moving) and to slaves without freedom (`SpawnBehavior::orderSlavesToAttackPosition`); `TransportAIUpdate`, `ChinookAIUpdate` pass it to their riders | the same place | the same |
| `ObjectCreationList` (a created object attacks the secondary position), `SpectreGunshipUpdate` (the gattling), `FlightDeckBehavior` (the planes of a carrier), `GameLogic` (a script's attacked position) | | |

Shipped `FIRE_WEAPON` buttons at a place or at the own place (the port's client refuses all of them as unsupported today): `Command_ChinaDragonTankFireWall`,
`Command_GLAToxinTractorContaminateGround`, `Command_AmericaVehicleComancheFireRocketPods`, `Command_DisarmMinesAtPosition`, `Command_BattleshipFire` (place);
`Command_DetonateConvoyTruckNuke`, `Command_DetonateFakeBuilding`, `Command_BombTruckDetonateNow`, `Demo_Command_TertiarySuicide`,
`Slth_Command_BattleBusBuildSlthTrap` (own place).

**`privateAttackPosition`** (`AIUpdate.cpp` 3508): a current weapon with a `ContinueAttackRange` (the mine clearer) first looks for the closest object it may attack within
that range of the place - ignoring stealth while it looks - and attacks that object instead; finding none, it fires one shot only. A contact weapon whose place cannot be
reached on a path gets a reachable place within 100 instead. Then the state machine is cleared, the path destroyed, the goal position set (clipped to the map) and
`AI_ATTACK_POSITION` entered with no goal object; the current weapon's `maxShotCount` is set after the state is entered (its `onEnter` resets it to no limit).

**`AIAttackState` on a place** (`AIStates.cpp` 5494-5690): `onEnter` - sleeping mood: success; under construction: failure; every weapon out of ammunition (not a projectile):
failure; `chooseBestWeaponForTarget(NULL, PREFER_MOST_DAMAGE)`; nothing chosen: failure; the weapon's shot count reset to no limit; `IS_ATTACKING` and the model condition
`ATTACKING`. `update` - out of ammunition: failure; the weapon chosen again every frame; a weapon locked on entry that is no longer current: failure; **the shot count used up: failure**
(the attack ends, the unit is idle); otherwise the attack sub-machine.

**The sub-machine on a place** (`AttackStateMachine`, 270-360): AIM -> FIRE -> AIM with the one condition `outOfWeaponRangePosition` -> CHASE. `outOfWeaponRangePosition`
(1193): a unit on the ground (ground movement, immobile, a stinger site, or a rider of a building or of a container on the ground) whose view to the place is blocked by an
obstacle (`isAttackViewBlockedByObstacle`), or the place outside `Weapon::isWithinAttackRange(source, pos)` (the attack range squared against the distance by
`ATTACK_RANGE_CALC_TYPE`, not nearer than the minimum range). CHASE walks towards the place and stops once in range; an immobile unit fails. AIM turns the turret or the body
to the place; FIRE fires `fireCurrentWeapon(pos)` (`Weapon::fireWeapon(source, pos)`), each shot decrementing `m_maxShotCount` and the clip.

## 2. What the port has

- No command for an attack on a place: the client's force-fire on the ground and every `FIRE_WEAPON` button at a place or at the own place end as "unsupported"
  (`CommandModeController`: `ContextAction.ForceAttackGround`, `GuiCommandKind.FireWeapon` without an object target).
- The player's attack on an object is a standing order of the client that sends `PursueCommand` and `FireWeaponCommand`s (`StandingOrders`), not an AI state.
- `FireWeaponPower` is a stand-in: it fires the current weapon at the place whenever the slot is ready (`IWorldServices.FireCurrentWeaponAtPosition`), without the approach, the
  turn or the turret.
- The scripts' `FIRE_WEAPON` button with a target object fires at it; a button at a place or at the own place is not executed.

## 3. The plan (one cause each, a test that fails before at 30, 60 and 120 Hz, a manifest commit where a field moves)

1. **The state.** `AttackPositionCommand(Player, Seq, Tick, Units, X, Y, OwnPosition, Slot, MaxShots)` - a new command type (`CommandTypes.Version`, replay format,
   `IsPlayerOrder`; D-5's bounds on the place). In the world an attack on a place beside the guard and the acquisition (`Entity.AttackPlace`, written only when present):
   the weapon choice against no victim, the range test to the place (minimum range included), the line of sight of a unit on the ground (`F-55`'s obstacle walk), the
   approach that stops in range, the turret or body turned to the place, the shot (`FireWeaponAt` with no victim, clip, delay, pre-attack, the firing condition), the shot
   count, the temporary weapon lock that ends with it; decided in the first tick of the unit's own frame of the original, read at the frame end.
2. **The client.** Force-fire on the ground and the `FIRE_WEAPON` buttons at a place, at the own place and with `ATTACK_OBJECTS_POSITION` send it.
3. **Through containers and slaves.** `groupAttackPosition` gives the order to the riders that may fire and to slaves without freedom (the end of F-60's step 6).
4. **The other callers.** `FireWeaponPower` (the stand-in replaced), the scripts' `FIRE_WEAPON` buttons at a place, `ObjectCreationList`'s attack, the mine clearer's
   continue range.

Expected to move: mission runs whose scripts or creation lists use a place attack (`FireWeaponPower` is used by one shipped object, `AmericaMiscUnit.ini`); the AI pairings
only if the built-in AI or the original AI's library orders one.

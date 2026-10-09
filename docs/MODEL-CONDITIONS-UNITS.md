# Model conditions of units, weapons, movement and death

As of: 4 October 2026 (Phase 15, world presentation). Subject: the model condition bits (`MODELCONDITION_*`) that units
use and that the presentation never set so far (`PresentationTool model-audit <dir> --structures`, section
"model condition names used by states"). Supplements `docs/PRESENTATION-OBJECTS.md` (bits that follow from the simulation state) with the
names of the units; the names of the buildings, doors and docks are in the sister work.

The original sets these bits **imperatively** from many modules (`Object::setModelConditionState`, `Drawable::clearAndSetModelConditionFlags`).
Here every bit is either a pure function of the simulation state (`ConditionDeriver`, file `ConditionDeriver.Units.cs`) or is set by a
small, purely presentational state machine (`UnitConditionTracker`) that is fed once per
simulation tick with the events (`FrameProjector.Observe`) and the captured object state (`FrameProjector.Advance`). Neither ever changes the
simulation; the tracker holds **no** simulation state (no snapshot, no hash). Where the bit needs a history that the simulation does not
keep, the state machine is a documented rebuild of the reference function, not an invention. Where the simulation does not know the triggering behaviour at all,
the bit stays unset and is listed below with the exact gap (never a silent substitute).

Reference sources (only read and rewritten, paths under `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/`):
`Object/FiringTracker.cpp`, `Object/Weapon.cpp`, `Object/WeaponSet.cpp`, `Object/Object.cpp`, `Object/Update/AIUpdate/JetAIUpdate.cpp`,
`Object/Update/AnimationSteeringUpdate.cpp`, `Object/Update/PhysicsUpdate.cpp`, `Object/Update/ToppleUpdate.cpp`, `Object/Update/TensileFormationUpdate.cpp`,
`Object/Update/SpecialAbilityUpdate.cpp`, `Object/Update/AIUpdate/MissileAIUpdate.cpp`, `Object/Update/AIUpdate/RailroadGuideAIUpdate.cpp`, `Object/Locomotor.cpp`,
`Object/Contain/ParachuteContain.cpp`, `Object/Contain/RiderChangeContain.cpp`, `Object/Die/CrushDie.cpp`, `Object/Behavior/SlowDeathBehavior.cpp`,
`Object/Behavior/BattleBusSlowDeathBehavior.cpp`, `Object/Body/ActiveBody.cpp`, `AI/AIStates.cpp`, `AI/TurretAI.cpp`, `AI/AIGroup.cpp`.

## 1. Result

`model-audit --structures` (retail base data): names that states use: 106; set by the presentation or a `ModelConditionUpgrade`:
**52 → 71**; never set: **54 → 35**. 19 names are newly set, 9 more are, with reasons, not derivable (table 3).

Newly set (table 2): `CONTINUOUS_FIRE_SLOW/MEAN/FAST`, `JETAFTERBURNER`, `JETEXHAUST`, `FREEFALL`, `SPLATTED`, `FRONTCRUSHED`, `BACKCRUSHED`, `TOPPLED`,
`CLIMBING`, `OVER_WATER`, `CENTER_TO_LEFT`, `CENTER_TO_RIGHT`, `LEFT_TO_CENTER`, `RIGHT_TO_CENTER`, `TURRET_ROTATE`, `SECOND_LIFE`, `SPECIAL_CHEERING`.
In addition `PARACHUTING` is now derived from the parachute (before only via the object status bit `PARACHUTING`, which the simulation never sets).

## 2. Implemented

| Bit | Reference (file, function) | Implementation here | Derivation |
|---|---|---|---|
| `CONTINUOUS_FIRE_SLOW/MEAN/FAST` | `FiringTracker.cpp` `shotFired`, `update`, `speedUp`, `coolDown`; thresholds `ContinuousFireOne/Two/Coast` in `Weapon.cpp` (default `INT_MAX`/0) | `UnitConditionTracker.ShotFired`/`Update`; `WeaponInfo.ContinuousFireOneShots/TwoShots/CoastTicks` (only read, `WeaponCatalog.Build`) | State machine per shooter: consecutive shots at the same target (`WeaponFired` event, target in `Other`; a target change within the coast time keeps counting), `> One` → MEAN, `> Two` → FAST (exclusive), after `next possible shot + Coast` the level drops to SLOW and one second later to nothing. The "next possible shot" (`getPossibleNextShotFrame`) is supplied by `Entity.ReadyTickOf(Slot)` of the slot that fired in this tick |
| `JETAFTERBURNER` | `JetAIUpdate.cpp` `friend_enableAfterburners`; on in `JetPauseBeforeTakeoffState::update`, off in `JetTakeoffOrLandingState::onExit` | `JetAIUpdate.AfterburnersOn` (read only) → `ConditionDeriver.UnitStates` | Phase `PauseBeforeTakeoff` after the restart of the countdown (no aircraft ahead any more) and phase `TakingOff`; jets only (the `HeliAIStateMachine` does not know the pause state) |
| `JETEXHAUST` | `JetAIUpdate.cpp` `JetAIUpdate::update` (speed > 0 and `ALLOW_AIR_LOCO`) | `ConditionDeriver.UnitStates` | `JetAIUpdate.AllowAirLoco && Entity.Speed != 0` |
| `PARACHUTING` (chute) / `FREEFALL`, `PARACHUTING` (paratrooper) | `ParachuteContain.cpp` `onContaining`, `update` (`clearAndSetModelConditionState(FREEFALL, PARACHUTING)` on opening) | `ConditionDeriver.UnitStates` with `ParachuteContain.Opened` | opened chute: PARACHUTING; occupant of a chute: FREEFALL until it opens, then PARACHUTING |
| `FREEFALL` (avalanche boulder) | `TensileFormationUpdate.cpp` `update` (`m_life < 100` and height change > 0.2) | `TensileFormationUpdate.Life` (read only), `UnitConditionTracker.Update` | `Enabled`, `0 < Life < 100` and `|ΔZ| > 0.2` against the previous tick |
| `SPLATTED` | `PhysicsUpdate.cpp` `PhysicsBehavior::update` (fall damage kills, death type `SPLATTED`) | `UnitConditionTracker.Died` | `EntityDied` event with death type `SPLATTED`; stays set (the original never clears it) |
| `FRONTCRUSHED`, `BACKCRUSHED` | `CrushDie.cpp` `crushLocationCheck`, `CrushDie::onDie` | `UnitConditionTracker.Died`/`CrushLocation` | `EntityDied` with death type `CRUSHED` on an object with `CrushDie`: of centre, front and rear crush point (half the major radius along the facing) the one nearest to the crusher (`Other`) wins; without a crusher `TOTAL_CRUSH` |
| `TOPPLED` | `ToppleUpdate.cpp` `applyTopplingForce`; `RiderChangeContain.cpp` `onRemoving` (`ScuttleStatus`, `TOPPLED` in all retail data) | `ToppleUpdate.HasToppled`, `RiderChangeContain.IsScuttled` → `ConditionDeriver.UnitStates` | toppled object (not with `KillWhenStartToppling`, there it dies before the line); combat bike without a rider |
| `CLIMBING` | `AIStates.cpp` `AIFollowPathState::onEnter`/`update` (moving object on `CELL_CLIFF`) | `ConditionDeriver.UnitStates` | `Speed != 0 || HasMoveTarget` and `TerrainMap.CellAt(...)` has `CellFlags.Cliff` |
| `OVER_WATER` | `Locomotor.cpp` `moveTowardsPositionHover`; `RailroadGuideAIUpdate.cpp` (train in the tunnel: `z < ground - 3`) | `LocomotorInfo.IsHover` (`Appearance = HOVER`, only read), `ConditionDeriver.UnitStates` | hover locomotion and `TerrainMap.IsUnderwater(x, y)`; train (`RailroadBehavior`) with `Z < ground - 3` |
| `CENTER_TO_LEFT`, `LEFT_TO_CENTER`, `CENTER_TO_RIGHT`, `RIGHT_TO_CENTER` | `AnimationSteeringUpdate.cpp` `update`; turn direction `PhysicsBehavior::getTurning` from `Locomotor.cpp` `rotateObjAroundLocoPivot` (`TURN_POSITIVE`/`NEGATIVE`) | `UnitConditionTracker.StepSteering`/`TurnOf`; `AnimationSteeringUpdate.MinTransitionTicks` (`MinTransitionTime`, now read typed) | State machine of the original with a minimum hold time; "turning" means: the facing has changed in this tick by exactly the turn rate of the locomotor (this is how `AIUpdateInterface` turns with a more distant target); positive = left |
| `TURRET_ROTATE` | `TurretAI.cpp` `friend_turnTowardsAngle` | `TurretTracker.IsRotating` (reads `Entity.TurretOf`) | the turret is further than one step from the target angle (otherwise it snaps in) |
| `SECOND_LIFE` | `BattleBusSlowDeathBehavior.cpp` `update` (after the landing of the thrown bus, never cleared) | `BattleBusSlowDeathBehavior.HasLanded` (read only) → `ConditionDeriver.UnitStates` | the first death has begun and ended and the bus lies on the ground (a real death in the air ends the update, the bus stays up: no bit) |
| `SPECIAL_CHEERING` | `AIGroup.cpp` `groupCheer`, `Object.cpp` `setSpecialModelConditionState` (three seconds) | `FrameProjector.Cheer(ids)` → `UnitConditionTracker.Cheer` | 90 ticks from the call; a new call restarts the time. The original triggers it only via the test key `ALL_CHEER`; the client does not yet call `Cheer` for the selection (`MetaAction.AllCheer` exists) |

All names are in `ConditionDeriver.ProducedNames` (own lines at the end of the set); `model-audit` then counts only real gaps.

## 3. Not derivable (deliberately not set)

| Bit | Who sets it in the original | What the simulation does not know |
|---|---|---|
| `EXPLODED_FLAILING`, `EXPLODED_BOUNCING` | `SlowDeathBehavior::beginSlowDeath` (`FlingForce`: impulse, `MODELCONDITION_EXPLODED_FLAILING`) and `update` (FLAILING → BOUNCING on ground contact, `isAboveTerrain`) | `FlingForce`/`FlingPitch` are typed but unused (comment of `SlowDeathBehavior`: a physics impulse on death): the object is never flung into the air, there is no flight and no impact. Prerequisite: fling physics of the `PhysicsBehavior` with height |
| `JAMMED` | `ActiveBody::onSubdualChange` → `MissileAIUpdate::projectileNowJammed` (missile jammed by ECM with subdual damage ≥ maximum health) | Subdual damage is not modelled (`ActiveBody` reports `NoEffect`); `MissileAIUpdate` knows no jamming |
| `PANICKING` | `AIPanicState`/`AIMoveAwayFromRepulsorsState` (`AIStates.cpp`) | `TEAM_PANIC` starts an ordinary waypoint follower in the script VM (not distinguishable from `FOLLOW_WAYPOINTS`); the flight from repulsors does not exist. Prerequisite: a separate panic state in the script follower (`MissionSnapshot`) or in the AI |
| `RAPPELLING` | `AIRappelState` (Chinook combat drop `AI_RAPPEL_INTO`), also backwards on cliffs (`AIFollowPathState`) | Combat drop and rappelling are not simulated (`ChinookAIUpdate`: "the drop is not simulated"); there is no reverse driving |

`RAISING_FLAG` was listed here until the staged building capture; since then `CaptureConditions` derives it (`docs/SIM-STAGED-CAPTURE.md` §5).
`PREATTACK_A/B/C` was listed here until the pre-attack delay of the simulation (`PreAttackDelay`, `docs/SIM-FIRING-TRACKER.md` §3); `ConditionDeriver.Weapon` now sets it from `SimulationWorld.PreAttackSlotOf`.

## 4. Verification

* Test program `tests/CnCNew.Presentation.Tests/UnitConditionTests.cs`, synthetic fixture (Gattling weapon with `ContinuousFireOne/Two/Coast`, vehicle with
  `AnimationSteeringUpdate` on terrain, hover over a lake, cliff cell, airfield with jet, chute with paratrooper, tree with `ToppleUpdate`, bus with
  `UndeadBody`/`BattleBusSlowDeathBehavior`, car with `CrushDie`, avalanche boulder on a slope): every implemented bit has a test via the `FrameProjector`
  (events as in the live game) or, where the event has to be built specially (death, crushing), via the tracker. Real data (`--content-dir`): Gattling tank
  (`none > MEAN > FAST > SLOW > none`, the state `ATTACKING CONTINUOUS_FIRE_*` is chosen), combat bike (dismounting the rider `TOPPLED`, all four lean bits on the map
  Alpine Assault), crushed car (`CarCamaro` chooses the model `FRONTCRUSHED BACKCRUSHED`), Raptor (`JETEXHAUST`), battle bus (`SECOND_LIFE` after the landing).
* Reference match (`SimTool reference-match`) unchanged: winner FactionGLA at tick 13,890, final hash `76F22CB0CA79BEEF`, chain `35F54CAD40A43E6E`; stress test unchanged
  `56882F763EABEF67`, chain `B8E663493F2C73D5`. The simulation only gets read-only properties and typed, unused weapon/locomotor/module fields (no
  serialised state, no new behaviour).

## 5. Limits and findings

* **The trackers are presentation, not simulation.** They know only what happened since the start of the projection: after loading a saved game
  lean state, `SPLATTED`/`CRUSHED` markers and cheering start empty (the derived bits come from the simulation state and survive loading). Objects outside the vision
  are not captured; a drop of the firing level is caught up at the next capture.
* **`CONTINUOUS_FIRE_*` comes from the simulation** (`docs/SIM-FIRING-TRACKER.md` §2): the levels are the weapon bonus conditions `CONTINUOUS_FIRE_MEAN/FAST` of the object and speed up the weapon
  (Gattling: 200 % and 400 %); the `UnitConditionTracker` no longer counts itself, the levels survive loading a saved game.
* **`ATTACKING` is never set by the simulation** (`IS_ATTACKING` has no writer), although `ConditionDeriver` derives the bit. Many states are named
  `ATTACKING <X>` (e.g. `ATTACKING CONTINUOUS_FIRE_MEAN`); the state selection then takes the best partial match, which is enough for the Gattling states but does not hit all combinations.
* **`SPLATTED` and `FRONTCRUSHED`/`BACKCRUSHED` are hardly reachable in today's simulation** (the code is tested and ready): `ParachuteContain.OnDie` kills the occupant after the
  fall damage with `DEATH_NORMAL` (a death type `SPLATTED` results only if the fall damage already kills), and `SquishCollide` crushes only infantry, while only vehicles carry `CrushDie`
  (no retail object has both); the crushing of vehicles by `PhysicsBehavior::onCollide` is missing.
* **Paratroopers are not drawn.** `FrameProjector` skips contained objects; the occupant of a `ParachuteContain` is contained, so its states
  `FREEFALL`/`PARACHUTING` are missing in the picture although they are derived. The original draws it at a bone position of the chute (`ParachuteContain::updateOffsetsFromBones`).
  In addition the original hides the chute until it opens (`draw->setDrawableHidden(!m_opened)`), here it shows the default state.
* `OVER_WATER` knows only the position, not the movement history: a hover object that was never moved and starts in the water already has the bit here, in the original only after the
  first movement. Helicopters and parachutes also carry `Appearance = HOVER` and get the bit over water, as in the original (without a state that uses it).
* `CLIMBING` does not know the addition "not pinched" (`getPinched`); `RAPPELLING` (backwards on cliffs) is dropped for lack of reverse driving.
* `TURRET_ROTATE` follows the turret state of the simulation (`docs/SIM-TURRET-AIM.md`): the turret turns as soon as a fire order points it at the target, holds, returns; no idle scan.
* The lean detection takes the turn from the facing change per tick. Other facing changes (taxi, evasion) with exactly the turn rate would
  count as steering; they affect only objects with `AnimationSteeringUpdate` (combat bikes).

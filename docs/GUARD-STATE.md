# Guarding as a state of the simulation (F-58)

This document is the reading behind F-58 (`docs/plan/STAGE-1-PORT.md` §3, Reforged R-35), written at the start of Phase 41, before any of it was built.

Read 28 October 2026. Original = `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine` (paths below are relative to it).
Port = `src/` at `a1ac2af`. Reforged = `references/zero-hour-reforged` at `e19c19d`. Data values from `game/content/Data/INI/Default/AIData.ini`
(the top-level `Data/INI/AIData.ini` sets no guard key).

## 0. Data

| Key (`AI.cpp:155-161`, `TAiData`) | shipped | in frames of the original |
|---|---|---|
| `GuardInnerModifierAI` | 1.1 | - |
| `GuardOuterModifierAI` | 1.333 | - |
| `GuardInnerModifierHuman` | 1.8 | - |
| `GuardOuterModifierHuman` | 2.2 | - |
| `GuardChaseUnitsDuration` (`m_guardChaseUnitFrames`, `parseDurationUnsignedInt`) | 10000 ms | 300 |
| `GuardEnemyScanRate` | 500 ms | 15 |
| `GuardEnemyReturnScanRate` | 1000 ms | 30 |
| `MaxRetaliationDistance` (`AI.cpp:204`) | 200 | - |
| `RetaliationFriendsRadius` (`AI.cpp:205`) | 120 | - |
| `AlertRangeModifier` / `AggressiveRangeModifier` | 1.1 / 1.5 | - |

**The ranges** - `AI::getAdjustedVisionRangeForObject` (`AI/AI.cpp:804-864`): vision range x (human: inner 1.8 / outer 2.2; computer: 1.1 / 1.333)
when `AI_VISIONFACTOR_OWNERTYPE`; the inner factor when `AI_VISIONFACTOR_GUARDINNER` is also set. Then: a **contained** object's range is replaced by its
largest weapon range; otherwise, only for a **computer** player's unit with `AI_VISIONFACTOR_MOOD`, sleep -> 0, alert x1.1, aggressive x1.5. "Human" is
`MM_Controller_Player` of the mood matrix.
- "inner range" = `getStdGuardRange` = OWNERTYPE|MOOD|GUARDINNER (`AIGuard.cpp:201-207`, same in `AIGuardRetaliate.cpp:214-220`, `AITNGuard.cpp:183-189`).
- "outer range" = OWNERTYPE|MOOD (no GUARDINNER).
- Note: `AIUpdateInterface::getNextMoodTarget` (`Object/Update/AIUpdate.cpp:4580-4581`) also uses the outer factor for the idle search radius ("Use Guard Outer").
  The port's §33.5 (`docs/SIMULATION-CORE.md:1509-1511`) is right that this has no effect for a human's idle unit (weapon range only); it does have effect
  in the guard, which the port lacks.

## 1. The original's guard machines, state by state

### 1.1 Entering (all three)

`AIUpdateInterface::aiDoCommand` (`Object/Update/AIUpdate.cpp:2703-2832`):
- `AICMD_GUARD_POSITION/_OBJECT/_TUNNEL_NETWORK/_AREA`: if the main machine is in `AI_GUARD_RETALIATE`, `state->clear()` first (Kris, 18 Aug 2003 fix
  "units move to zero"), then `privateGuardPosition/Object/TunnelNetwork/Area` (`AIUpdate.cpp:4038-4141`).
- `privateGuard*` returns at once for an immobile object (`isMobile() == FALSE`) or a `KINDOF_PROJECTILE`; position: a player's point is clipped to the map
  extent (`findClosestEdgePoint`); sets `m_locationToGuard` / `m_objectToGuard` / `m_areaToGuard` (+ centre as location, object cleared), `m_guardMode`,
  `getStateMachine()->clear()`, `setLastCommandSource(cmdSource)`, `setState(AI_GUARD)` (tunnel: `AI_GUARD_TUNNEL_NETWORK`).
- **Two-slot target type** (`AIUpdate.h:362-363`, `m_guardTargetType[2]`): a guard order writes slot 1 if it is `NONE`, else slot 0; leaving a guard state
  (`AIGuardState::onExit`, `AIStates.cpp:6722-6729`) shifts slot 0 into 1. So a new guard order given while guarding is read correctly after the old
  state's exit. `privateGuardPosition` does **not** clear `m_objectToGuard` (harmless: the type decides).
- `AIGuardState::onEnter` (`AI/AIStates.cpp:6696-6717`): new `AIGuardMachine`, given location/object/area by the target type, the guard mode;
  `initDefaultState()` (the first defined state, INNER, `StateMachine.cpp:505-506`), then `setState(AI_GUARD_RETURN)`.
  With no nemesis INNER -> OUTER -> GET_CRATE -> RETURN chain through in onEnter (`StateMachine::internalSetState` follows the return value at once,
  `StateMachine.cpp:620-651`), so RETURN is entered twice (the random return-scan offset is drawn twice from the logic stream).
- `AIGuardState::update` (`AIStates.cpp:6732-6754`) runs the sub-machine with the main machine locked. Success/failure of `AI_GUARD` -> `AI_IDLE`
  (`AIStates.cpp:723`); in practice the guard machine never exits, the guard lasts until another command.
- Machine owner's AI is sleepy (`#define SLEEPY_AI`, `AIUpdate.cpp:74`): a sleeping state's transition conditions are only tested when the AI update runs
  (`StateMachine::updateStateMachine`, `StateMachine.cpp:419-475`); damage does not wake the AI. A sleeping guard IDLE therefore answers an attacker at
  its next scan (<= 15 frames).

### 1.2 `AIGuardMachine` (position, object, area) - `AI/AIGuard.cpp`

States (`AIGuard.h` enum, 5000..5005) and table (`AIGuard.cpp:187-192`, `defineState(id, state, onSuccess, onFailure, conditions)`):

| State | success -> | failure -> | condition |
|---|---|---|---|
| INNER | OUTER | OUTER | attacked -> ATTACK_AGGRESSOR |
| RETURN | IDLE | INNER | attacked -> ATTACK_AGGRESSOR |
| IDLE | INNER | RETURN | attacked -> ATTACK_AGGRESSOR |
| OUTER | GET_CRATE | GET_CRATE | - |
| GET_CRATE | RETURN | RETURN | - |
| ATTACK_AGGRESSOR | INNER | INNER | - |

The centre is always the guarded object's current position if there is one, else the position (an area: its centre).

- **`lookForInnerTarget`** (`AIGuard.cpp:210-311`): not able to attack -> no. Team with `m_attackCommonTarget` and a team target -> that one. Otherwise the
  **closest** object (`getClosestObject(FROM_CENTER_2D)`) within the inner range of the centre, filters: enemies + `PartitionFilterPossibleToAttack
  (ATTACK_NEW_TARGET, CMD_FROM_AI)` + same map status; for `isEnterGuard` templates instead "possible to enter" (+ enemies + hijackable for
  `isHijackGuard`, else neutrals); `GUARDMODE_GUARD_FLYING_UNITS_ONLY` adds `PartitionFilterIsFlying`. **No fog filter** (Reforged adds one).
  **Area**: the range is the polygon's radius, the centre its bounds centre, filter `PartitionFilterPolygonTrigger` (point in polygon, xy); and the
  scan is skipped entirely unless some object entered/left a trigger area or a non-projectile/non-inert object was created/destroyed within the last
  `GuardEnemyScanRate` frames (`AIGuard.cpp:273-282`; `GameLogic::updateObjectsChangedTriggerAreas`, `Object.cpp:589, 614, 2630, 2664`).
  **Polygon radius defect** (`Map/PolygonTrigger.cpp:286-289`): `halfHeight = (hi.y + lo.y) / 2` (a plus), so the radius is
  `sqrt(((hi.y+lo.y)/2)^2 + ((hi.x-lo.x)/2)^2)` - grows with the area's y position. Centre = bounds centre (`PolygonTrigger.cpp:419-433`).
- **ExitConditions** (`AIGuard.cpp:119-161`): exit if no goal object (when `ExitIfNoUnitFound`), if `frame >= giveUpFrame` (`ExitIfExpiredDuration`),
  if the **victim's** 2D distance from the centre exceeds the radius (`ExitIfOutsideRadius`). Checked in `AIAttackState::onEnter` and every `update`
  (`AIStates.cpp:5505, 5582`).
- **INNER** (`AIGuard.cpp:386-479`): enter-guard templates run an `AIEnterState` on the nemesis; others an `AIAttackState(follow=false)` on the nemesis with
  exit {outside inner radius of the centre, no unit}. The centre follows a guarded object every update. Exit clears the team target.
- **OUTER** (`AIGuard.cpp:510-596`): `GUARDMODE_GUARD_WITHOUT_PURSUIT` -> success at once (no chase). Else attack the nemesis with exit {outer range
  (area: at least the area radius, centre the area centre), give-up = now + 300 frames, no unit}. Each update: while the victim is within the **inner**
  range of the centre the give-up frame is reset to now + 300.
- **GET_CRATE** (`AIGuard.cpp:776-802`): `checkForCrateToPickup` (`AIUpdate.cpp:900-921`) clears `m_crateCreated` **before** looking it up, so it never
  finds a crate: the state always succeeds at once. (Dead code in the original; only the IDLE->GET_CRATE->RETURN hop remains.)
- **RETURN** (`AIGuard.cpp:627-672`): an `AIInternalMoveToState` to the centre (area centre), `adjustDestination` for ground movers; scan every 30 frames
  (first after random 0..30) with `lookForInnerTarget` - a find is failure -> INNER. Arrival -> IDLE. (Comment: RETURN "is more like an attack move".)
- **IDLE** (`AIGuard.cpp:703-765`, `isGuardIdle()` true): first scan after random 0..15 frames, then every 15; a pending crate notice -> GET_CRATE; a
  target -> INNER; a guarded object that moved more than 2 cells (|dx| or |dy| > 20) since the last noted position -> RETURN. `m_guardeePos` is never
  initialised in `onEnter` and not saved (whether the first comparison fires could not be determined - depends on pool memory).
- **ATTACK_AGGRESSOR** (`AIGuard.cpp:813-881`): nemesis = the body's last damage source (`getLastDamageInfo()->in.m_sourceID`, also a healer's -
  the condition below filters to enemies); `AIAttackState(follow=TRUE)`, exit {victim outside the **inner** range of the centre, 300 frames, no unit}.
  Then INNER (which re-checks the same nemesis with the inner radius) -> OUTER.
- **The condition `hasAttackedMeAndICanReturnFire`** (`AIGuard.cpp:64-113`): `getClearableLastAttacker()` (`ActiveBody.h:95-96`: the last damage source
  unless cleared) is read **and cleared** on every test; true when it exists, is an enemy, not effectively dead, the owner can attack and
  `getAbleToAttackSpecificObject(ATTACK_NEW_TARGET, CMD_FROM_AI)` is possible (or possible after moving). The last damage info is overwritten only when
  the previous damage was not this or the last frame, with the vehicle/infantry/faction-structure preference (`ActiveBody.cpp:567-590`); healing also
  writes it (`ActiveBody.cpp:839-842`).
- **Chasing for a human:** `AIAttackPursueTargetState`/`ApproachTargetState` fail a human's unit whose last command came from the AI
  (`AIStates.cpp:2606-2620, 2969-2980`). A player's guard order sets the source to player, a script's to script - so a guard chases inside its radii.
- **Save/CRC:** every `crc()` of the guard machines and states is empty; nothing of the guard enters the CRC directly (positions and the attack do).
  `xfer`: machine version 2 (`StateMachine::xfer` + target, nemesis, position, trigger area **by name**); RETURN/IDLE save their scan times;
  INNER/OUTER/AGGRESSOR rebuild by `onEnter()` in `loadPostProcess` - except OUTER, whose `loadPostProcess` body starts with a stray class name
  (`{ AIGuardOuterState` / `onEnter();`, `AIGuard.cpp:504-507`) and so declares a function instead of calling it (Reforged documents the same; the
  loaded OUTER state has no attack and succeeds on its first update). `AIUpdateInterface::xfer` saves location, object and area name (`AIUpdate.cpp:5041-5051`).
- **Other readers:** `DeployStyleAIUpdate` deploys while `isInGuardIdleState()` (`Object/Update/AIUpdate/DeployStyleAIUpdate.cpp:117, 151`);
  `JetAIUpdate::aiDoCommand` allows reload-interrupt-and-resume for guard commands (`JetAIUpdate.cpp:2472-2480`); a guarding unit is **not idle** - so
  it does not join a retaliation (`ActiveBody::shouldRetaliate`) and script "idle" tests see it as busy.
- **How it ends:** any other command (`getStateMachine()->clear()` + new state); `AIGuardState::onExit` deletes the machine and shifts the target type.

### 1.3 `AIGuardRetaliateMachine` - `AI/AIGuardRetaliate.cpp` (Zero Hour, Kris Morness, July 2003)

Entered **only** by `ActiveBody::attemptDamage` (`Object/Body/ActiveBody.cpp:688-724`) through `aiGuardRetaliate(damager, them->getPosition(),
NO_MAX_SHOTS_LIMIT, CMD_FROM_AI)` -> `privateGuardRetaliate` (`AIUpdate.cpp:3463-3479`: clear, goal object = damager, goal position = own position,
`setState(AI_GUARD_RETALIATE)`). Preconditions:
- the damaged object's controlling player is `PLAYER_HUMAN` with **logical retaliation mode** on (`Player::isLogicalRetaliationModeEnabled`, default
  FALSE, `Player.cpp:533`); the mode is switched by `MSG_ENABLE_RETALIATION_MODE` (`GameLogicDispatch.cpp:627-639`), which the local player's client posts
  on every frame divisible by 30 whenever its option (`GlobalData::m_clientRetaliationModeEnabled`, default TRUE, options "Retaliation") differs
  (`Player.cpp:741-757`) - a recorded network message;
- `shouldRetaliateAgainstAggressor` (`ActiveBody.cpp:731-757`): damager exists, is not an airborne target, is an enemy, within 200 (bounding sphere 2D),
  damaged object's player human, damaged object not a drone;
- for every object of the player and its allies (`PartitionFilterPlayerAffiliation(ALLOW_ALLIES, true)` = own included, on map) within 120 + the
  damaged object's bounding radius: `shouldRetaliate` (`ActiveBody.cpp:761-794`: not `CANNOT_RETALIATE`, not `IMMOBILE`, not a drone, has AI and
  **`isIdle()`**, not stealthed-undetected, not using an ability), not immobile, can attack the damager.

Table (`AIGuardRetaliate.cpp:191-196`; default = first = ATTACK_AGGRESSOR):

| State | success -> | failure -> | condition |
|---|---|---|---|
| ATTACK_AGGRESSOR | RETURN | RETURN | - |
| RETURN | IDLE | INNER | attacked -> ATTACK_AGGRESSOR |
| IDLE | INNER | **exit machine (success)** -> `AI_IDLE` | attacked -> ATTACK_AGGRESSOR |
| INNER | OUTER | OUTER | - |
| OUTER | GET_CRATE | GET_CRATE | - |
| GET_CRATE | RETURN | RETURN | - |

- Exit conditions (`AIGuardRetaliate.cpp:118-171`) add: exit also when the **retaliator itself** is further than the inner range from the post.
- AGGRESSOR (`765-837`): nemesis = the goal object (the damager), else the last non-healing damage source if an enemy; radius = outer + inner range
  (human: 4.0 x vision) from the post, 300 frames (not refreshed), follow=false.
- INNER radius 1.5 x inner range; OUTER radius 0.67 x (outer + inner), 300 frames refreshed while the victim is within the inner range (no
  WITHOUT_PURSUIT test, commented out).
- `lookForInnerTarget` (`223-304`) adds `PartitionFilterRejectBuildings` (no ordinary buildings), centre = the post.
- IDLE scans **once** (after random 0..15): a target -> INNER, none -> the machine exits and the unit is idle again.
- The human's "no chase from an AI command" rule is lifted while in `AI_GUARD_RETALIATE` (`AIStates.cpp:2610, 2972`).
- Save as the guard machine (version 2; nemesis, position); crc empty.

### 1.4 `AITNGuardMachine` - `AI/AITNGuard.cpp` (`TEAM_GUARD_IN_TUNNEL_NETWORK` only)

Entered by `aiGuardTunnelNetwork` -> `privateGuardTunnelNetwork` -> `AI_GUARD_TUNNEL_NETWORK` -> `AITunnelNetworkGuardState::onEnter`
(`AIStates.cpp:6981-6997`; `setState(AI_GUARD_RETURN)` = `AI_TN_GUARD_RETURN`, same value 5003). Table (`AITNGuard.cpp:166-171`):
RETURN (success IDLE, failure INNER, attacked -> AGGRESSOR), IDLE (INNER / RETURN), INNER (OUTER / OUTER, attacked -> AGGRESSOR), OUTER -> GET_CRATE ->
RETURN, AGGRESSOR -> RETURN.
- Exit condition: only the give-up frame (`TunnelNetworkExitConditions`, 300 frames) - no radius.
- `lookForInnerTarget` (`192-264`): team target; the player's `TunnelTracker::getCurNemesis()`; for each tunnel of the player: the tunnel's own AI
  goal if an enemy; else whoever damaged a tunnel within the last 15 frames (enemy, attackable with `ATTACK_TUNNEL_NETWORK_GUARD`) - becomes the team
  target and the tunnels' nemesis.
- INNER (`328-435`): attack the nemesis; with no nemesis take the team target or the tunnels' nemesis, or one scan of the inner range around itself.
- RETURN (`567-639`, an `AIEnterState`): contained -> success; a team target or tunnel nemesis -> failure (attack); else enter the tunnel nearest to
  itself; no tunnel -> failure.
- IDLE (`670-739`): scan every 15 frames; a target while inside a tunnel -> leave through the tunnel nearest the nemesis (`exitObjectInAHurry`, wait
  while the exit is busy); outside a tunnel and a tunnel exists -> RETURN.
- AGGRESSOR (`787-855`, follow=TRUE) updates the tunnels' nemesis while firing.
- **Original defect:** `AITNGuardOuterState::update` (`495-521`) dereferences `m_attackState` without the null test the plain guard has - a crash when
  OUTER was entered without an attack state (Reforged's comment: a crash at address 0x20). `AITunnelNetworkGuardState::update` fails the guard when
  the owner is out of ammo (`AIStates.cpp:7024-7029`).

## 2. Every place that enters a guard, and the port today

| Original | Enters | Port today |
|---|---|---|
| Command bar `GUARD`, `GUARD_WITHOUT_PURSUIT`, `GUARD_FLYING_UNITS_ONLY` (`GUICommandTranslator.cpp:211-258, 438-452`) -> `MSG_DO_GUARD_POSITION` (point, mode); `MSG_DO_GUARD_OBJECT` only for a button with `NEED_OBJECT_TARGET` - the three shipped buttons have `NEED_TARGET_POS` (`CommandButton.ini` Command_Guard*), so a player never guards an object with shipped data. The two mode buttons are on `ChinaJetMIGCommandSet` and `AmericaJetRaptorCommandSet` only. | `GameLogicDispatch.cpp:836-860` -> `AIGroup::groupGuardPosition/Object` (`AIGroup.cpp:2502-2558`): every member `aiGuardPosition(same point)`, `CMD_FROM_PLAYER` | `CommandModeController.cs:464-469, 576-597`: all three buttons are one `CommandModeKind.Guard`; each unit gets a plain move (`Move`) and a client entry `StandingOrderKind.Guard` (`StandingOrders.cs:18-19`), whose update does nothing (`StandingOrders.cs:128-129`). The rest is the idle acquisition: a human's unit takes only what is in weapon range (`SimulationWorld.Acquisition.cs:357-377`). No mode, no object. |
| Double click guard (`CommandXlat.cpp:3658-3668, 3721-3731`, option `m_doubleClickAttackMove`) | `MSG_DO_GUARD_POSITION`, normal mode | not built (`OptionsScreen.cs:10` names the option as without effect) |
| Script `NAMED_GUARD` (`ScriptActions.cpp:1885-1901`): leave group, normal locomotor set, guard own position | position | `ScriptEngine.Actions.cs:307-308`: `Stop` |
| `TEAM_GUARD` (`1906-1924`): each member guards its own position | position | `ScriptEngine.Actions.cs:300-306`: `Stop` |
| `TEAM_GUARD_POSITION` (`1929-1945`): group guards the waypoint | position | `ScriptEngine.Actions.cs:310-313`: walk to the waypoint (follower `position:`), then nothing |
| `TEAM_GUARD_OBJECT` (`1950-1965`) | object | `ScriptEngine.Actions.cs:315-319`: treated like `TEAM_ATTACK_NAMED` (fire at the named object if an enemy, else attack-move to it) - wrong for a friendly object |
| `TEAM_GUARD_AREA` (`1970-1985`) | area (polygon) | `ScriptEngine.Actions.cs:321-325`: attack-move to the **vertex mean** (`AreaCentre`, `:751-757`; the original uses the bounds centre), then the follower ends |
| `UNIT_GUARD_FOR_FRAMECOUNT` (`4817-4832`): guard own position + sequential timer | position | `ScriptEngine.Sequential.cs:87-94`: `Stop` + timer |
| `TEAM_GUARD_FOR_FRAMECOUNT` - dispatched to `doTeamIdleForFramecount` (`ScriptActions.cpp:7233-7235`); `doTeamGuardForFramecount` (`4857-4875`) is unreachable | none (idle) | `ScriptEngine.Sequential.cs:75-85`: idle + timer - **correct** |
| `TEAM_GUARD_SUPPLY_CENTER` -> `AIPlayer::guardSupplyCenter` (`AIPlayer.cpp:891-925`): attacked supply centre first (`isSupplySourceAttacked`), else `findSupplyCenter`; point 0.8 x radius toward the enemy; `groupGuardPosition`, `CMD_FROM_SCRIPT` | position | `ScriptEngine.Phase13.cs:155-157, 459-483`: `MoveTo` the point; the "attacked centre first" step is missing |
| `TEAM_GUARD_IN_TUNNEL_NETWORK` (`6258-6274`) | tunnel network guard | `ScriptEngine.TeamAbilities.cs:143-147, 436-443`: enter the nearest own tunnel, no guard state (`docs/SCRIPT-ENGINE.md:640`) |
| AI players: only through scripts. `SkirmishScripts.scb` uses `TEAM_GUARD_AREA` 671 times (`[Skirmish]MyInnerPerimeter`, `MyOuterPerimeter`, `EnemyInnerPerimeter`, `CombatZone`; `docs/SKIRMISH-AI-ORIGINAL.md:59`) plus team create scripts "guard the base"; `AIPlayer::guardSupplyCenter` above. `AISkirmishPlayer` and `Team.cpp` call no guard. | area / position | the original-AI option (`ScriptedSkirmishSession`) runs those scripts through the same `ScriptEngine` mapping as above. The built-in `SkirmishAi` (`src/CnCNew.Simulation/Skirmish/SkirmishAi*.cs`) never guards (it is this project's AI, Stage 3). |
| `FlightDeckBehavior` (`FlightDeckBehavior.cpp:1388-1393, 1453-1454, 1491-1492`): a carrier ordered to guard a position passes `aiGuardPosition(CMD_FROM_AI)` to its planes | position | the port's `FlightDeckBehavior` (`Phase12Modules.cs`) has no order propagation at all (not examined further) |
| `ActiveBody::attemptDamage` retaliation (`ActiveBody.cpp:688-724`) | retaliate | nothing; `Bodies.cs:155-169` keeps `LastDamageSource`/`LastDamageTick` with the "this or last frame" rule but no "cleared" flag |
| `JetAIUpdate` (`JetAIUpdate.cpp:2472-2480`) and `DeployStyleAIUpdate` (`:117, 151`) read the guard | - | no guard to read |

Static use counts in the campaign maps: not determined here (the action names in `.map`/`.scb` are not reliably stored as text; `MapTool
script-library <content> --show TEAM_GUARD` gives them).

## 3. Proposed plan (one cause per step)

**Built (28 October 2026), docs/SIMULATION-CORE.md §55.10:** step 1 with the object field of step 2 (`GuardCommand`, `CommandTypes.Version` 18, replay 16), step 3 without the
original's trigger-area gate, step 4, step 6 with the mode on for every human player (no option yet), and Reforged's R-35 as the community fix `guard-answers-only-when-idle`
(`GameFix` 21); the guard's look and attack have no fog filter (a victim need not be visible). Open: step 5 (the tunnel network), step 7 (`DeployStyleAIUpdate` would need the
guard's own moves to pack the unit first), the parts of step 8 that are the client's (the retaliation option).

**Step 7, read again (28 October 2026):** `DeployStyleAIUpdate::update` deploys while `isInGuardIdleState()` exactly as when a victim is in range, and packs whenever the unit
tries to move (`isWaitingForPath() || getPath()`) - also for the guard's own walk back and its chase, whose move state lives on and waits while the locomotor's goal is held
(`setLocomotorGoalNone` every frame of DEPLOY, UNDEPLOY, ALIGNING_TURRETS). The port's module packs only for an order (`OrderMove`) and, while it packs or unpacks, stops the
internal move (`StopInternalMove`), which ends the move a guard's RETURN or chase began - so step 7 needs first a way to hold an internal move during the pack without losing
it (the same gap makes a deployed unit of a computer player chase on an attack-move without packing). Step 5 (the tunnel network guard) is used only by the original AI's
skirmish script library (`SkirmishScripts.scb`, 4 actions), not by a mission.

General for every step:
- **Where:** a world-side state like `AcquisitionState` (`SimulationWorld.Guard.cs`, `Entity.Guard`), not a client book; written into the snapshot only
  when present (precedent: `acquisition`, new optional fields keep `WorldSnapshot` 10). A guarding unit is **not** `IsIdle`
  (`Phase5Modules.cs:689`): idle acquisition must not run in parallel with the guard (the turret's idle look does, as in `TurretAI::friend_checkForIdleMoodTarget`);
  check every consumer of `IsIdle` (34 uses) - `ScriptEngine.Sequential.cs:127`, the skirmish AI's idle tests.
- **The attack inside the guard** reuses the acquisition's `Engage` with a chase allowed (the human's guard chases - its command source is the player),
  a centre and exit conditions; `EndAttack` / any new order ends the guard (`OrderMove`, `Phase5Modules.cs:755`).
- **Rate invariance:** the machine decides in the first tick of the unit's own original frame (`SimulationWorld.Acquisition.cs:137-139`, frame
  convention `docs/SIMULATION-CORE.md` §43); scan rates 15/30, give-up 300 are original frames converted once (`Time.OriginalFrames`); the random first
  scan (`GameLogicRandomValue(0, rate)`) drawn as a hash of (seed, entity, frame number) in original frames and scaled, like `MoodCheckOffset`
  (`Acquisition.cs:273-286`) - the port then does not consume the logic stream (the original draws, RETURN twice on entry); positions, "victim inside
  the radius", "guarded object moved > 20" are read at the frame end (`Entity.FrameEnd`); the state chain of one update runs inside one decision.
  Tests at 30, 60 and 120 Hz with the same frame of each event.
- **Data:** `AIData.ini` is untyped (§33.5); the guard needs `GuardInner/OuterModifierAI/Human`, `GuardChaseUnitsDuration`, `GuardEnemyScanRate`,
  `GuardEnemyReturnScanRate` (and step 5 `MaxRetaliationDistance`, `RetaliationFriendsRadius`). Either named constants with the shipped values (as
  §33.5 does today) or a first step that types `AIData` (`Skirmish/Original/AiData.cs:36` already names the keys).

| # | Cause | What | Formats | Test that fails before |
|---|---|---|---|---|
| 1 | The guard command and the position guard | `GuardCommand(Player, Seq, Tick, ImmutableArray<EntityId> Units, Fixed X, Fixed Y, GuardMode Mode)` for `MSG_DO_GUARD_POSITION` to a group (all members the same point, like `groupGuardPosition`; the RETURN move adjusts the destination per unit, F-04's single-move adjustment); the machine INNER/OUTER/RETURN/IDLE/ATTACK_AGGRESSOR with GET_CRATE as the pass-through it is; both modes; `Entity.LastDamageCleared` for the clearable attacker. Client: the three buttons send it with their mode, `StandingOrderKind.Guard` removed. Immobile objects and projectiles refused in the pre-check; D-5 bounds on the point. | new command type 35: `CommandTypes.Version` 17 -> 18, `IsKnown` bound, `IsPlayerOrder` includes it (25 of 35; `docs/NETWORK-PROTOCOL.md` §20.6 count), `ReplayFormat.CurrentVersion` 15 -> 16 (codec), the fuzz corpus; the protocol version need not change (the hello compares the command table and replay versions, `LockstepClient.cs:53`), but every room/replay of 17 is refused. Manifest: `commandStreamSha256` of the runs that embed the description (22 fields last time) + the two version fields; no result moves until scripts use the state (step 4). `WorldSnapshot` stays 10 (optional field). | synthetic flat map, a human's tank guards P; an enemy at a distance between weapon range and 1.8 x vision from P: the guard drives to it, kills it, returns to P. Today it stands. Second scene: the enemy flees beyond 2.2 x vision / lingers in the outer ring for 300 frames - the guard gives up and returns. Third: a computer player's unit uses 1.1 / 1.333 (and x1.1 / x1.5 for alert / aggressive teams). Fourth: `WITHOUT_PURSUIT` never leaves the inner ring; `FLYING_UNITS_ONLY` ignores a ground enemy. Fifth: an attacker outside the weapon range hits the guarding unit at its post - it answers (ATTACK_AGGRESSOR). Save/restore mid-chase continues bit-identically; a move order ends the guard. |
| 2 | Guard object | `GuardCommand` with a target object (or a second form); the centre follows the object; IDLE notices a move of > 2 cells and returns. Only scripts and `MSG_DO_GUARD_OBJECT` (no shipped button) reach it. | the same command type if built in step 1 with an object field (else a second type and another version step - build it into step 1) | a guard of a friendly truck that drives off: the guard follows and attacks an enemy near the truck, not near the old point. |
| 3 | Guard area | the polygon: `PartitionFilterPolygonTrigger` (point in polygon), range = the polygon radius with the original's formula (`hi.y + lo.y`), centre = bounds centre, the "objects changed trigger areas within 15 frames" gate (needs a world-level frame stamp of trigger-area entries/exits and object creation/destruction of non-projectile, non-inert objects); the area travels by name in the snapshot. Only scripts reach it. | none beyond step 1 (internal call from the script engine) | an area whose y position makes the defective radius differ from the true one: an enemy inside the polygon is attacked, one outside the polygon but inside the radius is not; with no trigger-area change for > 15 frames an enemy that stands still inside is not looked for (the original's gate). |
| 4 | The scripts | `NAMED_GUARD` (leave group, normal locomotor set), `TEAM_GUARD`, `UNIT_GUARD_FOR_FRAMECOUNT` -> guard own position; `TEAM_GUARD_POSITION`, `_OBJECT`, `_AREA`, `TEAM_GUARD_SUPPLY_CENTER` (+ the attacked centre first) -> guard with `CMD_FROM_SCRIPT`. `TEAM_GUARD_FOR_FRAMECOUNT` stays idle. `AreaCentre` for other actions stays (check separately whether the vertex mean is right for `TEAM_ATTACK_AREA`: the original's `privateAttackArea`, `AIUpdate.cpp:3658-3677`, only stores the area and enters `AI_ATTACK_AREA`; how that state picks its point was not read). | none; **manifest commit**: mission runs whose scripts guard (count to be measured); the original-AI option changes a lot (671 `TEAM_GUARD_AREA` uses) but is not in the manifest | a synthetic mission script with `TEAM_GUARD_AREA` / `UNIT_GUARD_FOR_FRAMECOUNT`: the team holds the area and attacks an intruder; today it attack-moves to the centre once and stands. Campaign checks (objective driver, audit, reachability) after it. |
| 5 | The tunnel network guard | `AITNGuardMachine`: the tunnel tracker's nemesis (the port needs a per-player "current nemesis" of its tunnels and "damaged within 15 frames" from the tunnels' last damage), leave through the tunnel nearest the nemesis, return into the nearest tunnel. The original's null dereference in `AITNGuardOuterState::update` must become a defined outcome (treat as success, as the plain guard does) - a documented deviation. | none; manifest if a mission run uses it | rebels in a tunnel network, an enemy attacks one tunnel: they come out of the tunnel nearest the attacker, fight, go back in. Today they sit inside. |
| 6 | Player retaliation (`AIGuardRetaliateMachine`) | the per-player logical retaliation flag; the hook in damage (`ActiveBody.cpp:688-724`: human player, enemy, not airborne, <= 200, allies within 120 + radius, idle, mobile, can attack); the retaliate machine with its own radii (4.0 / 1.5 / 0.67 x (outer + inner)), the "retaliator leaves its inner range" exit, no buildings, IDLE exits to idle. A guard order while retaliating clears it first. | the flag is player state set by a message in the original: either a `SetRetaliationCommand` (another command type, another version step - better folded into step 1's version change) or a slot field of the match description (encoding 3 -> 4) for the start value plus the command for changes. The client sends the option (default on). Affects every match with a human; manifest runs with human players (if any; to be checked - the reference match's driver and the mission runs' human side) move. | a human's idle tank 100 units from an ally that a computer's tank (in its range, out of the human tank's weapon range) shoots: the idle tank drives up and fights, gives up after 300 frames or when it leaves 1.8 x vision of where it stood, returns, and is idle again; with the flag off it stays. |
| 7 | The modules that read the guard | `DeployStyleAIUpdate` deploys in guard IDLE; `JetAIUpdate` treats guard commands as reload-resumable (and a parked jet's pending guard); `FlightDeckBehavior` passes a guard order to its planes (needs its order propagation, which the port lacks). | none | a Nuke Cannon on guard deploys at its post while idle; a Raptor on guard resumes after reloading. |
| 8 | Docs and the client's standing order | remove `StandingOrderKind.Guard`; `docs/SIMULATION-CORE.md` §33.5 (`GuardOuterModifierHuman` now has effect), a new section for the guard, `docs/SCRIPT-ENGINE.md` rows, the register F-58 closed, R-35 moved on. | - | - |

Steps 1 to 3 change no result of the existing manifests beyond the command-table version fields (nothing guards in them before step 4); 4 and 6 are
the behaviour changes with their own manifest commits.

## 4. R-35 - "a guarding unit alternates between returning and shooting back"

**Confirmed as the original's behaviour - kind B, a rule-profile candidate.** In the original table (`AIGuard.cpp:187-188`) INNER and RETURN carry the
`attackAggressors` condition. A unit walking back to its post (RETURN, an internal move) that is hit leaves the move at its next update for
ATTACK_AGGRESSOR, whose exit radius is the **inner** range around the post (`AIGuard.cpp:832-838`). An attacker standing outside that ring (artillery,
or the enemy the guard had chased out into the outer ring) makes the attack exit at once -> INNER (same radius, exits at once) -> OUTER (outer radius,
300 frames: it shoots / chases) -> GET_CRATE -> RETURN (the move starts again, `adjustDestination` and a new path) -> hit again -> ATTACK_AGGRESSOR ...
Inside INNER the same condition makes every new hit by anyone switch the target (the clearable attacker is cleared on each test). The retaliation
machine has the condition on RETURN too (`AIGuardRetaliate.cpp:192`).

Reforged's change (`references/zero-hour-reforged/.../AIGuard.cpp`, diff against the original): `attackAggressors` removed from INNER and RETURN ("that
condition fought the movement itself, so a guarding unit under fire stuttered between driving and shooting"), and from the retaliation machine's RETURN.
As a `GameFix` it writes no new state (a table difference only). It can be measured only after F-58 step 1 (the port has no guard to change).

Reforged changes beside it, sorted for later (not R-35): a fog filter on the guard's look (the original guard sees into fog - **B**, a candidate; note
the port's acquisition already requires the player's vision, so step 1 must deliberately **not** apply `CanPlayerSee` in the guard's look to stay
faithful); a guard radius carried by the player's order (**D**); a deployed unit at its post does not return (**B**/D); crate notice after the target
check (dead code in the original, **A**); the OUTER `loadPostProcess` fix (**A** for the port - its save is its own); the tunnel guard null check and
"no tunnel is not a target" (`AITNGuard.cpp`, **B** - the first is a crash in the original that the port must define anyway); line of sight for
retaliation (**B**/D).

## 5. Not determined

- Whether `AIGuardIdleState::m_guardeePos` (never initialised, not saved) makes the first IDLE scan of an object guard return at once.
- What `follow = TRUE` changes inside `AttackStateMachine` for the guard's ATTACK_AGGRESSOR (the flag reaches `AIAttackPursueTargetState` /
  `AIAttackApproachTargetState`, `AIStates.cpp:300-400`; not read to the end).
- How often the campaign maps use each guard action, and which runs of the reference manifest would move in steps 4 and 6 (measure:
  `MapTool script-library`, `SimTool reference-manifest results`).
- Whether any manifest run has a player the port treats as human (relevant to step 6).
- `privateAttackArea`'s centre (vertex mean vs bounds centre in the port's `TEAM_ATTACK_AREA`).

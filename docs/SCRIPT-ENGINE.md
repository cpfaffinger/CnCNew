# Script VM and mission session (Phase 9, part A items 2 and 5; Phase 10–12: extensions)

This document describes the **execution** of the World Builder scripts (decoding: `docs/MAP-SCRIPTS.md`) in
`CnCNew.Simulation`: the script VM (`ScriptEngine`), the mission session (`MissionSession`), its saveable state
(`MissionSnapshot`) and the automated headless checks. Campaign flow and savegames are in
`docs/CAMPAIGN-AND-SAVEGAMES.md`.

Sources (algorithm understood, rewritten, not copied): `GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/`
`ScriptEngine.cpp` (`update`, `executeScript`, `evaluateConditions`, `executeActions`, `evaluateCounter/Flag/Timer`,
`setCounter/Timer/adjustTimer`, `enable/disableScript`, `callSubroutine`, `checkConditionsForTeamNames`,
`AttackPriorityInfo`), `ScriptConditions.cpp` (every implemented condition, function in the table §4),
`ScriptActions.cpp` (every implemented action, function in the table §5), `AI/AIPlayer.cpp` (`newMap`,
`buildStructureNow`), `Common/RTS/Team.cpp` (`allInside`, `someInsideSomeOutside`, `hasAnyBuildFacility`).

## 1. Layering

| Building block | Location | Task |
| --- | --- | --- |
| `ScriptProgram` | `Simulation/Scripting` | immutable script data of a map, reshaped for execution: scripts/groups in run order, name indices with the first-hit rule of `findScript`/`findGroup`, players (sides), team prototypes (units, transport, origin), waypoints, waypoint paths (`waypointPathLabel1..3` + links, loop detection), trigger areas (polygon, even-odd ray test), placed objects; structural checksum (`Fingerprint`) |
| `ScriptEngine` | `Simulation/Scripting` | the mutable state and the tick: counters/timers, flags, script/group activation, evaluation schedule, named objects, team instances, object type lists, follow-up orders (paths, hunt, attack), attack priority sets, damage history, outcome; `Describe`/`Load`/`StateHash` |
| `ScriptCoverage` | `Simulation/Scripting` | the support table (executed / passed on / **recorded** / missing) for all 109 condition and 344 action types; `Validate()` checks it against `MapScriptCatalog` |
| `MissionSession` | `Simulation/Mission` | builds world, players, relationships, placed objects, build lists and human starting base from a map; one tick = scripts → automatic combat → world step; the only command acceptor with one sequence counter per player |
| `MissionSnapshot` | `Simulation/Mission` | canonical, versioned state (world snapshot + script state + session counters) |
| `MissionScenario`, `MissionProbe` | `Simulation/Testing` | headless runs, save/load continuation, end probe for victory/defeat |

Since Phase 11 `ScriptEngine` has been spread over partial files (one `partial class`, one state): `ScriptEngine.cs` (core, tick, state, `Describe`/`Load`), `.Conditions.cs`/`.Actions.cs`
(the switch of the basic types), `.Extended.cs` (the `SKIRMISH_*`, vision and special power conditions, `Observe` extensions), `.ExtendedActions.cs` (reveal, garrison, build switches,
team build/merge, waypoint variants of the command buttons), `.Sequential.cs` (sequential scripts), `.Powers.cs` (named special powers, named reveals, recruiting, recorded items),
`.TeamEvents.cs` (team event scripts), `.TeamAbilities.cs` (Phase 12: override relationships, the `TEAM_ALL_USE_COMMANDBUTTON_*` family, hunt with command button, transport loading, panic, facing, stealth switch,
unmanned/held, rank/priority/stance values). On an unknown type the switches of the basic types call the extensions one after another; only when none is responsible is the type counted as
missing. New state (attack history per player, triggered special powers, team build orders, sequence runners, named reveals, recorded items, team event state) is written in
`Describe`/`Load` only when it is not empty (the form of a run without these properties stays byte-identical).

The VM depends only on `CnCNew.Data` (`SourceRules` applies): no wall time, no ambient random numbers (random timers draw from
`SimulationWorld.LogicRandom`), no `float`/`double`, no file access. All collections that enter state or hash are
`Sorted*` with ordinal comparison.

## 2. One tick

`MissionSession.Step()` = `ScriptEngine.Update(tick)` → `SimulationWorld.Step()` (in it the target acquisition of the units, `docs/SIMULATION-CORE.md` §33) → `ScriptEngine.Observe(events)`.
Until Phase 16 `DriveCombat(tick)`, the combat driver of the session, stood in between; it has been dropped.

Since Phase 11 `ScriptEngine.Update` ends with `UpdateFollowers` → `ProcessTeamEvents` (§5b) → `ProcessSequences` (§5a) → `ProcessTeamBuilds`; the set "objects that received a
command in this tick" (`_busyThisTick`) is emptied at the beginning, because a command takes effect only in the world step, but the sequence logic must treat the object as busy immediately, as the original does.
The core follows `ScriptEngine::update`: endgame timer (`VICTORY`/`DEFEAT` start 120 updates
`FRAMES_TO_SHOW_WIN_LOSE_MESSAGE`, after that no script evaluation runs any more, `QUICKVICTORY` 1 update; one update is one original frame, see below); counters with `isCountdownTimer`
count down to −1; then per player script list the top-level scripts, after that the active groups not marked as a subroutine,
each without subroutines. `ExecuteScript` observes `IsActive`, difficulty flags, `DelayEvaluationSeconds`
(next evaluation time), evaluates the OR-of-AND tree with short circuit and executes true or false actions;
`IsOneShot` deactivates the script after the true branch (on the false branch only in the case without team reference – as in the original).
Conditions that name a team parameter are evaluated per instance of the team (`checkConditionsForTeamNames`: prototypes that can be instantiated
several times take precedence over singletons); `<This Team>`, `<This Player>` and `<Local Player>` are resolved.
`CALL_SUBROUTINE` calls groups (only active subroutine groups) and single scripts (only subroutines), recursion depth 32
(exceeding it is counted as a diagnostic).

**Frame counters and tick rate (Phase 20, changed in Phase 23; `docs/TICKRATE-MIGRATION.md` §1 case (f), §8, §15.3).** The tick rate is a value of the world; the VM computes through `_world.Time`. Since
Phase 23 the clock of the VM is the original frame (next section). What the VM **counts down per update** is therefore in frames and is not scaled: timers (`SET_TIMER`,
`SET_RANDOM_TIMER`; `SET_MILLISECOND_TIMER` and relatives: seconds rounded up to whole original frames), the waiting time of the `*_FOR_FRAMECOUNT` actions and the endgame timer
(`EndGameDelayFrames` = 120 updates; `QUICKVICTORY` the next update). What the VM compares with the **tick of the world** or hands to the world as a duration is still *scaled*:
`DelayEvaluationSeconds` and the first evaluation tick of delayed scripts (`NameHash % 61` frames), `teamInitialIdleFrames`, the presentation latency (`PresentationLatencyTicks`, in frames despite
the name), its own rhythms (new target every 30 frames, capture retry, supply scan), `SCRIPTING_OVERRIDE_HULK_LIFETIME` and the countdown of a special power. All these values lie on
the grid of the frames, so the VM hits them exactly. *Not scaled* is what leaves the simulation as text: the `Arguments` of a `ScriptOutput` are the parameters of the map, a frame count
among them (`CAMERA_FADE_*`, `CAMERA_BW_MODE_*`) is at every rate a number of original frames (1/30 s); `ScriptOutput.Tick` is a tick of the world. The value of a timer (`CounterValue`) is
at every rate a number of **original frames**.

### The clock of the VM is the original frame (Phase 23)

The original calls `ScriptEngine::update` exactly once per logic frame (`GameLogic.cpp`, `GameLogic::update`: `TheScriptEngine->UPDATE()` before the objects), and the maps are written against that: they
compare running timers with frame counts (`COUNTER`), do arithmetic on timers (`INCREMENT_COUNTER`), count their own evaluations and query edges "since the previous evaluation". None of this can
be converted to a different step size. The VM therefore uses the **escape "original grid"** (Ausweg „Originalraster“; `docs/handoffs/HANDOFF-PHASE23.md`, list in `docs/SIMULATION-CORE.md` §41): `ScriptEngine.Update(tick)` is
still called in every tick, but works only on the ticks that begin an original frame (`Time.IsOriginalFrameBoundary`: every tick at 30 Hz, every second at 60 Hz, every fourth at
120 Hz). At 30 Hz this changes nothing.

**The finding that triggered this** (`MD_USA02`, objective driver at 30 and 60 Hz): `Global Timers` sets the timer `Convoy Trucks` with `SET_TIMER` to 16,000 frames, `Truck01 Deploy` waits for
`COUNTER(Convoy Trucks < 15000)`. At 30 Hz these are 1,001 frames (33.4 s). With a timer in ticks the value stood at 32,000 at 60 Hz and fell below the frame literal
15,000 only after 17,001 ticks (283.4 s): the first truck appeared at 326.5 s instead of 76.5 s, the victory script `END_Final_` ran at 340.57 s instead of 90.60 s, the defeat script at 529.52 s instead of 412.03 s.

| What | runs | Why |
|---|---|---|
| everything in `ScriptEngine.Update`: endgame timer, expiry of the presentation latency, `PruneTeams`, timers, all scripts, `UpdateFollowers`, `UpdateButtonHunts`, `ProcessTeamEvents`, `ProcessSequences`, `ProcessTeamBuilds`, `UpdateTeamQueues`, `UpdatePhase13` (capture, waiting build orders, booby traps, bridge states) | once per original frame | one update of the original; counters, edges ("idle twice in a row", "for one update", "command issued in this update") and "one command per update" thus keep their meaning in game time |
| `ScriptEngine.Observe` (events of the world: damage, death of a team member, entering a building, new unit, special power, upgrade) | in every tick after the world step | nothing that happens between two updates is lost; the VM reads the collected tables on the next update |
| "has this named object ever existed" (`NoteNamedObjectsThatExist`, `didUnitExist`) | in every tick | an object that appears and disappears again between two updates otherwise counts as never created |
| the information the VM gives the world (attack priorities, mood: `AcquisitionAdvisor`), movement, command acceptance, target acquisition | in every tick (world) | for them the escape is not permissible; the commands of the VM are accepted like any other command in the world step of the tick in which they were issued |
| `_tick`, emptying `_busyThisTick` | in every tick | pure bookkeeping of the call; a state that is saved between two frames continues identically |

- **`FREEZE_TIME`.** The world stands still, its tick stops, the host keeps calling `MissionSession.Step` at the pace of the rate. The VM counts these calls itself (`BeginsFrame`, state `frozenPhase`, written
  only when not equal to 0 – never at 30 Hz) and thus stays on the frame even at standstill: a timer of 30 frames lasts, frozen, one second of steps at every rate.
- **`UNIT_EMPTIED`** compares with the **previous frame** (`ScriptConditions::evaluateUnitHasEmptied`: `m_frameNumber == frameNum - 1`), i.e. with the tick `Rate/30` back.
- **Edges** (`NAMED_/TEAM_ENTERED/EXITED_AREA*`, `PLAYER_LOST_OBJECT_TYPE`, bridge state): the comparison is with the previous evaluation, and at every rate that lies at the same point in game time.
  If a unit enters and leaves an area between two frames, the VM sees that at 60/120 Hz as little as at 30 Hz, where these intermediate positions do not exist at all.
- **Standing orders of the VM** (`UpdateFollowers`: arrival at the waypoint, re-issue of the march command, the shot of an attack command through `FireAtOrdered`) run with the VM per frame. This is the
  command cadence of the scripts, not the movement or the target acquisition of the units: the number of commands per game time is at every rate that of 30 Hz, an arrival is noticed at most `Rate/30 − 1` ticks
  later.
- **State and formats.** At 30 Hz no byte changes, there is no version increase (`MissionSnapshot` 3). At 60/120 Hz three fields of the script state have a new **meaning** without
  a new format: `counters[].value` of a timer, `sequences[].queue[].wait` and `endTimer` count frames instead of ticks. Saved states of these rates do not exist (golden values arise only with
  the switch to 60 Hz); a state written with an older build at 60/120 Hz would see its timers expire twice or four times as fast.
- **Consumers outside the simulation:** only `MissionPresentationDirector.CounterForDisplay` reads the value of a timer; it no longer converts. `EndGameDelay` (ticks of the world until `IsEnded`)
  stays as a property for hosts.
- **Tests** (`Tests.TickRate.ScriptClock.cs`, `Tests.TickRate.ScriptEvents.cs`, each at 30, 60 and 120 Hz in one run): counter per evaluation, frame and second timers, timer against frame literal
  (the pattern of `MD_USA02`), sequence waiting time, `FREEZE_TIME`, saving between two frames (also frozen), `UNIT_EMPTIED`, area edge and death between two frames, a named object
  that lived only between two frames. Measurement across all 24 campaign maps: `docs/TICKRATE-MIGRATION.md` §15.3.

`Observe` receives the events of the tick: `EntityDamaged` → last attacker of an object (owner, type; basis of the
`*_ATTACKED_BY_*` conditions, never reset like `getLastDamageInfo`), `EntityContained` → last player who entered a building.

### Objects are created immediately

The original creates map objects on loading and script objects in the action itself (`newObject`), not at the end of the tick. The world
registers spawns at the end of `Step`; `SimulationWorld.MaterializePendingSpawns()` brings that forward (map loading, `CREATE_*`,
reinforcement). Without that `PLAYER_ALL_DESTROYED` would be true for every player in the first tick and every mission lost (this is how it was
discovered on the first real run).

## 3. Commands and world access

Commands to units (`MOVE_*`, paths, hunt, attack) are **normal player commands** (`SetMoveTargetCommand`) of the owner through
`MissionSession.Issue` – i.e. with the same validation, the same sequence counter (strictly increasing per player across all
command sources) and the same rejection reasons. Direct world changes that no command covers use the world interface:
`RequestSpawn`, `Kill`, `RequestDestroy`, `Damage`, `AddMoney`/`WithdrawMoney`, as well as new `SimulationWorld.SetMoney`, `SetRelationship`
and `TransferOwnership` (also moves the energy production) in `SimulationWorld.Scripting.cs`.

**Consequence:** A mission is **not** reproducible as a `ReplayFile` (script actions are not recorded commands). The truth
is the snapshot (`MissionSnapshot`); the proof is "saved and restored, it continues hash-identically".

## 4. Conditions

Support according to `ScriptCoverage` (numbers of the table: `ScriptSupportTableIsConsistent`, printed in the test run). Every row names the
reference function in `ScriptConditions.cpp`/`ScriptEngine.cpp`.

| Type | Reference | Note |
| --- | --- | --- |
| `CONDITION_TRUE/FALSE`, `COUNTER`, `FLAG`, `TIMER_EXPIRED` | `ScriptEngine::evaluateCondition/Counter/Flag/Timer` | `FLAG` is also fulfilled by a UI interaction (`SignalUiInteraction`), a timer never started has not expired |
| `PLAYER_ALL_DESTROYED`, `…_ALL_BUILDFACILITIES_DESTROYED` | `evaluateAllDestroyed/AllBuildFacilitiesDestroyed` | build facility = structure with `ProductionUpdate`/`DozerAIUpdate` (§8) |
| `PLAYER_HAS_CREDITS` | `evaluatePlayerHasCredits` | parameter on the left of the comparison, as there |
| `PLAYER_HAS_N_OR_FEWER_(FACTION_)BUILDINGS`, `PLAYER_HAS_(NO_)POWER` | `evaluatePlayerHasNOrFewer*`, `evaluatePlayerHasPower` | power parameter order (player first); power = production ≥ 0 |
| `PLAYER_HAS_OBJECT_COMPARISON`, `…_UNIT_TYPE_IN_TRIGGER_AREA`, `…_UNIT_KIND_IN_TRIGGER_AREA`, `BUILT_BY_PLAYER`, `PLAYER_LOST_OBJECT_TYPE` | `evaluatePlayerUnitCondition`, `…UnitTypeInArea`, `…UnitKindInArea`, `evaluateBuiltByPlayer`, `evaluatePlayerLostObjectType` | object types: named list (`OBJECTLIST_ADDOBJECTTYPE`) or single type |
| `PLAYER_ACQUIRED_SCIENCE`, `START_POSITION_IS`, `MISSION_ATTEMPTS`, `SKIRMISH_PLAYER_FACTION`, `PLAYER_DESTROYED_N_BUILDINGS_PLAYER` | `evaluateScienceAcquired`, `evaluateSkirmishStartPosition`, `evaluateMissionAttempts`, `evaluateSkirmishPlayerIsFaction`, `evaluatePlayerDestroyedNOrMoreBuildings` | attempts come from the campaign progress; the last one is an unimplemented stub in the original that **always returns FALSE**, and is executed the same way here |
| `NAMED_DESTROYED/NOT_DESTROYED/DYING/TOTALLY_DEAD/CREATED/OWNED_BY_PLAYER`, `UNIT_HEALTH`, `UNIT_HAS_OBJECT_STATUS` | `evaluateNamedUnit*`, `evaluateNamedCreated/OwnedByPlayer`, `evaluateUnitHealth`, `evaluateUnitHasObjectStatus` | "has existed" (`didUnitExist`) holds as soon as the object once lived in the world |
| `NAMED_INSIDE/OUTSIDE_AREA`, `NAMED_ENTERED/EXITED_AREA`, `TEAM_INSIDE_AREA_ENTIRELY/PARTIALLY`, `TEAM_OUTSIDE_AREA_ENTIRELY`, `TEAM_ENTERED/EXITED_AREA_*` | `evaluateNamedInsideArea`, `Team::allInside`, `Team::someInsideSomeOutside` | surface mask of the parameter against `Locomotor.Surfaces`; entry/exit remember the state of the last evaluation |
| `TEAM_HAS_UNITS`, `TEAM_DESTROYED`, `TEAM_CREATED`, `TEAM_OWNED_BY_PLAYER` | `evaluateHasUnits`, `evaluateIsDestroyed`, `evaluateTeamCreated`, `evaluateTeamOwnedByPlayer` | |
| `NAMED_/TEAM_ATTACKED_BY_PLAYER/OBJECTTYPE` | `evaluateNamed/TeamAttackedByPlayer/Type` | attacker is read after the world step (§2) |
| `NAMED_/TEAM_REACHED_WAYPOINTS_END` | `evaluateNamed/TeamReachedWaypointsEnd` | "any member" for teams |
| `TEAM_ALL_HAS_OBJECT_STATUS`, `TEAM_SOME_HAVE_OBJECT_STATUS` | `evaluateTeamHasObjectStatus` | |
| `BUILDING_ENTERED_BY_PLAYER` | `evaluateBuildingEntered` | last entering player |
| `MULTIPLAYER_ALLIED_VICTORY/DEFEAT`, `MULTIPLAYER_PLAYER_DEFEAT` | `evaluateMultiplayer*` (`VictoryConditions`) | relative to the viewer (`LocalPlayer`) |
| `HAS_FINISHED_SPEECH/AUDIO/VIDEO`, `MUSIC_TRACK_HAS_COMPLETED`, `CAMERA_MOVEMENT_FINISHED`, `NAMED_SELECTED` | `evaluate*HasCompleted`, `isCameraMovementFinished`, `evaluateNamedSelected` | host state (§6) |

| `SKIRMISH_PLAYER_HAS_UNITS_IN_AREA`, `SKIRMISH_PLAYER_IS_OUTSIDE_AREA`, `SKIRMISH_NAMED_AREA_EXIST` | `evaluateSkirmishPlayerHasUnitsInArea`, `…IsOutsideArea`, `…NamedAreaExists` | the player has a living, non-inert non-projectile object in the area; "outside" requires that player and area exist; the cache of the original (`didEnterOrExit`) is pure acceleration and is dropped |
| `SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER` | `evaluateSkirmishPlayerHasBeenAttackedByPlayer`, `ActiveBody::attemptDamage` (`Player::setAttackedBy`) | pairs (victim owner, attacker owner) from the `EntityDamaged` events, never reset |
| `SKIRMISH_VALUE_IN_AREA` | `evaluateSkirmishValueInArea` | sum of the `BuildCost` of the living, non-inert objects of the player in the area against the comparison |
| `SKIRMISH_SPECIAL_POWER_READY`, `SKIRMISH_COMMAND_BUTTON_READY_ALL/PARTIAL` | `evaluateSkirmishSpecialPowerIsReady`, `evaluateSkirmishCommandButtonIsReady` | any finished object of the player that is not under construction carries the power ready (`ISpecialPowerModule.IsReady`, science present); the "disabled" test of the original is dropped (§8); button: only special power buttons |
| `SKIRMISH_PLAYER_HAS_COMPARISON_GARRISONED`, `SKIRMISH_PLAYER_HAS_DISCOVERED_PLAYER` | `evaluateSkirmishPlayerHasComparisonGarrisoned`, `…HasDiscoveredPlayer` | occupied `GarrisonContain` objects of the player; any object of the first player in a cell that the second currently sees |
| `PLAYER_TRIGGERED_SPECIAL_POWER`(`_FROM_NAMED`) | `evaluatePlayerSpecialPowerFromUnitTriggered`, `ScriptEngine::isSpecialPowerTriggered` (`removeFromList = TRUE`) | from `SpecialPowerUsed` events; the condition **consumes** the entry, as in the original |
| `NAMED_BUILDING_IS_EMPTY`, `NAMED_DISCOVERED`, `TEAM_DISCOVERED` | `evaluateIsBuildingEmpty`, `evaluateNamedDiscovered`, `evaluateTeamDiscovered` | container without occupants; "discovered" = in a cell visible to the player (or his own) and not stealthed-without-detection |
| `TYPE_SIGHTED`, `ENEMY_SIGHTED` | `evaluateTypeSighted`, `evaluateEnemySighted` | objects in the `VisionRange` circle of the observer, living, not stealthed (unless detected/disguised); relationship 0 enemy/1 neutral/2 friend |

All other condition types are **missing**: evaluation = `false` and counter `MissingConditionHits[type]` (never silently).

## 5. Actions

| Group | Types | Reference |
| --- | --- | --- |
| State | `SET_FLAG`, `SET_COUNTER`, `INCREMENT/DECREMENT_COUNTER`, `SET_TIMER`, `SET_MILLISECOND_TIMER`, `SET_RANDOM_TIMER`, `SET_RANDOM_MSEC_TIMER`, `STOP_TIMER`, `RESTART_TIMER`, `ADD_TO/SUB_FROM_MSEC_TIMER`, `ENABLE/DISABLE_SCRIPT`, `CALL_SUBROUTINE`, `OBJECTLIST_ADD/REMOVEOBJECTTYPE`, `NO_OP` | `ScriptEngine.cpp` (`setFlag` … `callSubroutine`), `doObjectTypeListMaintenance` |
| Outcome | `VICTORY`, `DEFEAT`, `QUICKVICTORY` | `doVictory/doDefeat/doQuickVictory` |
| Players | `PLAYER_SET_MONEY`, `PLAYER_GIVE_MONEY`, `PLAYER_RELATES_PLAYER`, `PLAYER_GRANT_SCIENCE`, `PLAYER_KILL`, `PLAYER_TRANSFER_OWNERSHIP_PLAYER`, `NAMED_/TEAM_TRANSFER…` | `doSetMoney`, `doGiveMoney`, `updatePlayerRelationTowardPlayer`, … |
| Objects/teams | `NAMED_KILL/DELETE/DAMAGE`, `TEAM_KILL/DELETE(_LIVING)`, `DAMAGE_MEMBERS_OF_TEAM`, `CREATE_OBJECT`, `UNIT_SPAWN_NAMED_LOCATION_ORIENTATION`, `CREATE_NAMED/UNNAMED_ON_TEAM_AT_WAYPOINT`, `CREATE_REINFORCEMENT_TEAM` | `doNamedKill` …, `doCreateObject`, `createUnitOnTeamAt`, `doCreateReinforcements` |
| Commands | `MOVE_NAMED_UNIT_TO`, `MOVE_TEAM_TO`, `*_FOLLOW_WAYPOINTS(_EXACT)`, `*_STOP`, `*_GUARD*`, `*_HUNT`, `PLAYER_HUNT`, `*_ATTACK_*` | `doMoveToWaypoint`, `doTeamFollowWaypoints`, … (see §8) |
| Attack priorities | `SET_ATTACK_PRIORITY_THING/KIND_OF`, `SET_DEFAULT_ATTACK_PRIORITY`, `NAMED_/TEAM_APPLY_ATTACK_PRIORITY_SET` | `setPriorityThing/Kind/Default`, `AttackPriorityInfo::getPriority` |
| Command button abilities | `NAMED_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)`, `TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)` | `doNamed/TeamUseCommandButtonAbility(OnNamed)`: triggers the named `CommandButton` (`Command = SPECIAL_POWER*`) as an ordinary `UseSpecialPowerCommand` of the carrier (`CommandCatalog.TryGetSpecialPowerAbility`); a button of a different command kind or an unknown name is a diagnostic case, not an execution (§8) |
| Command buttons, other kinds | `PLAYER_UPGRADE`/`OBJECT_UPGRADE` (buy the named upgrade through `PurchaseUpgradeCommand`), `FIRE_WEAPON` (with target object: `FireWeaponCommand` of the carrier from the slot of the button, the slot is locked; without a target, at a position or with `ATTACK_OBJECTS_POSITION`: `AttackPositionCommand`, Phase 43; `SWITCH_WEAPON` locks permanently) | `Object::doCommandButton*`; every other command kind ("has command SELL, not executed") and an unknown button ("unknown command button") are each a separate counted diagnostic case |
| Command button abilities, waypoints | `NAMED_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT`, `…_USING_WAYPOINT_PATH` (for the particle beam along the path, otherwise only the nearest path point), `TEAM_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT` | `doNamedUseCommandButtonAbilityAtWaypoint`, `…UsingWaypointPath`, `doTeamUseCommandButtonAbilityAtWaypoint`; flying along the path exists only for the particle beam (§8 item 23) |
| Special powers directly | `NAMED_FIRE_SPECIAL_POWER_AT_WAYPOINT`/`_AT_NAMED`, `SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST`, `NAMED_SET/ADD_SPECIAL_POWER_COUNTDOWN`, `NAMED_STOP/START_SPECIAL_POWER_COUNTDOWN` | `doNamedFireSpecialPowerAtWaypoint/AtNamed`, `doSkirmishFireSpecialPowerAtMostCost`, `doNamedSet/AddSpecialPowerCountdown`, `doNamedStopSpecialPowerCountdown` (`SetReadyFrame`, `PauseCountdown`) |
| Map vision | `MAP_REVEAL_ALL(_PERM/_UNDO_PERM)`, `MAP_SHROUD_ALL`, `MAP_REVEAL/SHROUD_AT_WAYPOINT`, `MAP_REVEAL_PERMANENTLY_AT_WAYPOINT`/`MAP_UNDO_REVEAL_PERMANENTLY_AT_WAYPOINT` | `doRevealMapEntire(Permanently)`, `doShroudMapEntire`, `doRevealMapAtWaypoint`, `doShroudMapAtWaypoint`, `doRevealMapAtWaypointPermanent` (`docs/SIMULATION-CORE.md` §26); empty player name = the viewer (`getHumanPlayerMask`) |
| Containers | `NAMED_ENTER_NAMED`, `TEAM_ENTER_NAMED`, `NAMED/TEAM_GARRISON_SPECIFIC_BUILDING`, `NAMED/TEAM_GARRISON_NEAREST_BUILDING`, `PLAYER_GARRISON_ALL_BUILDINGS`, `NAMED_EXIT_ALL`, `TEAM_EXIT_ALL`, `EXIT_SPECIFIC_BUILDING`, `NAMED_EXIT_BUILDING`, `TEAM_EXIT_ALL_BUILDINGS`, `PLAYER_EXIT_ALL_BUILDINGS` | `doNamedEnterNamed`, `doTeamEnterNamed`, `doUnit/TeamGarrisonSpecificBuilding`, `doUnit/TeamGarrisonNearestBuilding`, `Player::garrisonAllUnits`, `doNamedExitAll`, `doTeamExitAll`, `doExitSpecificBuilding`, `doUnitExitBuilding`, `doTeamExitAllBuildings`; entering = `MoveAndEnterCommand`, leaving = `ExitContainerCommand`/`EvacuateCommand` |
| Construction and economy | `PLAYER_ENABLE/DISABLE_BASE/UNIT_CONSTRUCTION`, `PLAYER_PURCHASE_SCIENCE`, `AI_PLAYER_BUILD_UPGRADE`, `WAREHOUSE_SET_VALUE`, `BUILD_TEAM`, `TEAM_MERGE_INTO_TEAM`, `RECRUIT_TEAM`, `TEAM_AVAILABLE_FOR_RECRUITMENT`, `IDLE_ALL_UNITS` | `doPlayerEnable/Disable…Construction` (`Player::allowedToBuild`), `doPlayerPurchaseScience`, `doBuildUpgrade`/`AIPlayer::buildUpgrade` (player upgrade through the ordinary purchase, without factory search), `doSetWarehouseValue`, `doBuildTeam`/`AIPlayer::buildSpecificAITeam`, `doMergeTeamIntoTeam`, `doRecruitTeam`/`AIPlayer::recruitSpecificAITeam`/`Team::tryToRecruit`, `doIdleAllPlayerUnits` |
| Sequential scripts | `TEAM/UNIT_EXECUTE_SEQUENTIAL_SCRIPT(_LOOPING)`, `TEAM/UNIT_STOP_SEQUENTIAL_SCRIPT`, `TEAM/UNIT_GUARD/IDLE_FOR_FRAMECOUNT`, `TEAM_SPIN_FOR_FRAMECOUNT`, `TEAM_WAIT_FOR_NOT_CONTAINED_ALL/PARTIAL`, `SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_ALL/PARTIAL` | §5a |
| Override relationships (Phase 12) | `TEAM_SET/REMOVE_OVERRIDE_RELATION_TO_TEAM`, `TEAM_SET/REMOVE_OVERRIDE_RELATION_TO_PLAYER`, `PLAYER_SET/REMOVE_OVERRIDE_RELATION_TO_TEAM`, `TEAM_REMOVE_ALL_OVERRIDE_RELATIONS` | `doTeamSetOverrideRelationToTeam`, `…ToPlayer`, `doPlayerSetOverrideRelationToTeam`, `doTeamRemoveAllOverrideRelations` (`Team::setOverrideTeamRelationship`/`setOverridePlayerRelationship`, `Team::getRelationship` with the order team→team, team→player, player→team, then player relationship). In the world: `SimulationWorld.RelationshipOverride` (a hook that can supply the relationship per object pair; sorted, saved state in the `ScriptEngine`); combat, target choice and `AreEnemies(Entity, Entity)` use `RelationshipBetween` |
| Command button family (Phase 12) | `TEAM_HUNT_WITH_COMMAND_BUTTON`, `TEAM_ALL_USE_COMMANDBUTTON_ON_NAMED`, `…_ON_NEAREST_ENEMY_UNIT/_ENEMY_BUILDING/_ENEMY_BUILDING_CLASS/_KINDOF/_GARRISONED_BUILDING/_OBJECTTYPE`, `TEAM_PARTIAL_USE_COMMANDBUTTON` | `doTeamHuntWithCommandButton`, `doTeamUseCommandButtonOnNamed`, `doTeamUseCommandButtonOnNearest*`, `doTeamPartialUseCommandButton`; the button goes to the carriers as an ordinary command (special power, `FIRE_WEAPON`, upgrade); `…ON_NEAREST_*` chooses per carrier the nearest matching target, the hunt sets an order (`_buttonHunts`) that searches for the nearest target anew every few ticks (the shell `CommandButtonHuntUpdate` carries the button, not the search) |
| Team movement and state (Phase 12) | `TEAM_LOAD_TRANSPORTS`, `TEAM_MOVE_TOWARDS_NEAREST_OBJECT_TYPE`, `TEAM_GUARD_IN_TUNNEL_NETWORK`, `TEAM_PANIC`, `NAMED/TEAM_FACE_WAYPOINT`, `NAMED/TEAM_SET_STEALTH_ENABLED`, `NAMED/TEAM_SET_UNMANNED_STATUS`, `DELETE_ALL_UNMANNED`, `TEAM_INCREASE/DECREASE_PRIORITY` | `doTeamLoadTransports`, `doTeamMoveTowardsNearestObjectType`, `doTeamGuardInTunnelNetwork`, `doTeamPanic`, `doNamed/TeamFaceWaypoint`, `doNamed/TeamSetStealthEnabled`, `doNamed/TeamSetUnmannedStatus`, `doDeleteAllUnmanned`, `TeamPrototype::increaseAIPriorityForSuccess`/`decreaseAIPriorityForFailure` (§8 items 19–22 for the approximations; the priority is read only by `RECRUIT_TEAM`) |
| Team facing, sale, settings (Phase 13) | `TEAM_FACE_NAMED`, `PLAYER_SELL_EVERYTHING`; recorded: `NAMED_SET_STOPPING_DISTANCE`, `NAMED_SET_REPULSOR`, `SET_BASE_CONSTRUCTION_SPEED` | `doTeamFaceNamed` (every member stops and faces the object; without turn rate), `doPlayerSellEverything` → `Player::sellEverythingUnderTheSun` (one `SellCommand` per structure, the world checks as for a player), `doNamedSetStoppingDistance`/`doNamedSetRepulsor`/`doSetBaseConstructionSpeed` (no consumer; `RecordedSetting`) |
| Map and AI actions (Phase 13, continued) | `NAMED_SET_TOPPLE_DIRECTION`, `TECHTREE_MODIFY_BUILDABILITY_OBJECT`, `RESUME_SUPPLY_TRUCKING`, `LOCALDEFEAT`, `UNIT_MOVE_TOWARDS_NEAREST_OBJECT_TYPE`, `TEAM_CAPTURE_NEAREST_UNOWNED_FACTION_UNIT`, `SKIRMISH_MOVE_TO_APPROACH_PATH`, `SKIRMISH_FOLLOW_APPROACH_PATH`, `TEAM_GUARD_SUPPLY_CENTER`, `AI_PLAYER_BUILD_SUPPLY_CENTER`, `TEAM_SET_STATE`, `NAMED_FACE_NAMED`, `UNIT_DESTROY_ALL_CONTAINED`, `NAMED_RECEIVE_UPGRADE`, `PLAYER_REPAIR_NAMED_STRUCTURE`; without effect as in the original: `PLAYER_CREATE_TEAM_FROM_CAPTURED_UNITS`, `TEAM_COLLECT_NEARBY_FOR_TEAM`, `DEBUG_CRASH_BOX` | `ScriptEngine.Phase13.cs`: `setToppleDirection` (table that `ToppleUpdate`/`StructureToppleUpdate` read through `IWorldServices`), `setBuildableStatusOverride` (`SimulationWorld.BuildableOf`), `setUnitsShouldIdleOrResume(false)` (`SupplyCarrierAI.ForceWanting`), `doLocalDefeat` (= defeat), `doMoveUnitTowardsNearest`, `doTeamCaptureNearestUnownedFactionUnit` (the members walk to the nearest unmanned object, whoever reaches it takes it over and it counts as *captured*, `Entity.Captured`), `doTeamMoveToSkirmishApproachPath`/`doTeamFollowSkirmishApproachPath` (path `label + (start index of the enemy + 1)`, `ScriptPlayer.StartIndex`; enemy = the viewer, the fallback of the original), `AIPlayer::guardSupplyCenter`/`buildBySupplies`/`findSupplyCenter` (warehouse with enough stock, without an own money generator nearby, not much closer to the enemy; build site in growing rings, a construction worker builds, without money the order waits), `doSetTeamState`, `doNamedFaceNamed`, `doDestroyAllContained`, `doUnitReceiveUpgrade`, `Player::repairStructure` (the nearest construction worker receives the repair command) |
| Settings without a consumer (Phase 13, continued) | `NAMED_SET_ATTITUDE`, `TEAM_SET_REPULSOR`, `SET_STOPPING_DISTANCE`, `CHOOSE_VICTIM_ALWAYS_USES_NORMAL`, `SET_CAVE_INDEX`, `PLAYER_ADD_SKILLPOINTS`, `PLAYER_SELECT_SKILLSET` (recorded) | one value each in `RecordedSetting` (key `ACTION|subject`), in the state only when set |
| Conditions (Phase 13, continued) | `TEAM_STATE_IS`/`_IS_NOT`, `NAMED_HAS_FREE_CONTAINER_SLOTS`, `UNIT_EMPTIED`, `PLAYER_POWER_COMPARE_PERCENT`, `PLAYER_EXCESS_POWER_COMPARE_VALUE`, `SKIRMISH_UNOWNED_FACTION_UNIT_EXISTS`, `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE`, `SUPPLY_SOURCE_SAFE`/`_ATTACKED`, `PLAYER_BUILT_UPGRADE(_FROM_NAMED)`, `PLAYER_COMPLETED_SPECIAL_POWER(_FROM_NAMED)`, `PLAYER_MIDWAY_SPECIAL_POWER(_FROM_NAMED)`, `PLAYER_HAS_SCIENCEPURCHASEPOINTS`, `PLAYER_CAN_PURCHASE_SCIENCE`, `SKIRMISH_SUPPLIES_VALUE_WITHIN_DISTANCE`, `SKIRMISH_PLAYER_HAS_PREREQUISITE_TO_BUILD`, `SKIRMISH_PLAYER_HAS_COMPARISON_CAPTURED_UNITS`; always false as in the original: `OBSOLETE_SCRIPT_1/2`, `DEFUNCT_PLAYER_SELECTED_GENERAL(_FROM_NAMED)`, `*_COMPLETED_SEQUENTIAL_EXECUTION` | `ScriptEngine.Phase13.cs`: the state string of `TEAM_SET_STATE`; power ratio from the negative `EnergyProduction` values of the objects (the simulation keeps only the sum); `evaluateUnitHasEmptied` with the state per object; the consuming lists `isUpgradeComplete`/`isSpecialPowerComplete` from the events `UpgradeCompleted`/`SpecialPowerCompleted` (`SpecialPowerCompletionDie` reports its carrier with creator); `AIPlayer::isSupplySourceSafe/Attacked` (radius 250 from `AI.cpp`, the two-second cache of the original is dropped); the answer of the tech building condition is remembered permanently as in the original; `PLAYER_MIDWAY_…`: the original never calls `notifyOfMidwaySpecialPower`, the list stays empty |
| Held states, traps, bridges (Phase 14) | `NAMED_SET_HELD`, `PLAYER_DISABLE_FACTORIES`, `PLAYER_ENABLE_FACTORIES`, `SET_TRAIN_HELD`, `NAMED_SET_EVAC_LEFT_OR_RIGHT`, `NAMED_SET_BOOBYTRAPPED`, `TEAM_SET_BOOBYTRAPPED`; conditions `BRIDGE_BROKEN`, `BRIDGE_REPAIRED`; the script name follows a takeover (`ObjectNameTransferred`) | `ScriptEngine.Phase13.cs`: `Entity.ScriptDisableMask` (bit 1 `DISABLED_HELD`: AI and production rest; bit 2 `OBJECT_STATUS_SCRIPT_DISABLED`: production stands still), `RailroadBehavior.SetHeld`, `GarrisonContain.EvacDisposition`, `_pendingTraps` (trap object at a random point of the victim's outline, `StickyBombUpdate.InitStickyBombAtOffset`), `_bridgeStates` (true for one update after the change into/out of rubble); details and sources: `docs/SIMULATION-CORE.md` §30.4 |
| **Recorded** | `PLAYER_AFFECT_RECEIVING_EXPERIENCE`, `OBJECT_ALLOW_BONUSES`; since Phase 12 `TEAM_SET_ATTITUDE`, `TEAM_WANDER`, `TEAM_WANDER_IN_PLACE`, `PLAYER_SET_RANKLEVELLIMIT`, `PLAYER_SET_RANKLEVEL`, `PLAYER_ADD_RANKLEVEL`, `SCRIPTING_OVERRIDE_HULK_LIFETIME` | the value is stored (`SkillPointsModifierOf`, switches, stance, rank, held state, hulk lifetime), but **no part of the simulation reads it** (no scaling of experience gain per player, no difficulty bonuses, no AI stance, no rank system, no hulk list; `NAMED_SET_HELD` has been executed since Phase 14); separate counter `RecordedActions`, neither "executed" nor "missing" (§8) |
| **Passed on** (presentation) | camera, sound, speech, movie, music, radar, UI text, fade, countdown displays, input lock among others (129 types) | as `ScriptOutput` (tick, action, parameters as text) to the client; `SPEECH_PLAY`/movie/music/camera movement count as running for `PresentationLatencyTicks` |

All other action types are **missing**: no effect, counter `MissingActionHits[type]`. As of Phase 14 (`CampaignTool coverage`): conditions 109 of 109 executed; actions **187 executed, 129 passed on, 19 recorded, 9 missing**
(`SKIRMISH_BUILD_BUILDING`, `SKIRMISH_BUILD_BASE_DEFENSE_FRONT/_FLANK`, `SKIRMISH_BUILD_STRUCTURE_FRONT/_FLANK`, `SKIRMISH_ATTACK_NEAREST_GROUP_WITH_VALUE`, `SKIRMISH_PERFORM_COMMANDBUTTON_ON_MOST_VALUABLE_OBJECT`, `AI_PLAYER_BUILD_TYPE_NEAREST_TEAM`,
`NAMED_FIRE_WEAPON_FOLLOWING_WAYPOINT_PATH`); none of the nine occurs in a campaign map (`CampaignTool usage` empty). The first six belong to `AISkirmishPlayer` (build list, base radius, side `AIData`), which the simulation with
the `SkirmishAi` substitute (§5c) does not share.

References that cannot be resolved at runtime (unknown player/team/waypoint/area/object type/science, type not instantiable,
parameter list too short, recursion) are counted by `Diagnostics`; `ExplainScript(name)` supplies, per condition of a script, the current
result (tool/test, changes edge-triggered conditions).

## 5a. Sequential scripts (Phase 11)

Source: `ScriptEngine::evaluateAndProgressAllSequentialScripts`, `appendSequentialScript`, `cleanupSequentialScript`, `setSequentialTimer`, `ScriptActions::doTeam/UnitStartSequentialScript`,
`doTeam/UnitStopSequentialScript`, `doTeam/UnitIdle/GuardForFramecount`, `doTeamSpinForFramecount`. This is the way campaign scripts lead a team or a unit through a
command list (422 scripts on 21 of the 24 maps use `TEAM_EXECUTE_SEQUENTIAL_SCRIPT`).

- A **runner** (`SequenceRunner`) belongs to a team or an object and has a queue of scripts (`SequenceItem`: script, repetitions, command pointer, waiting time, "hold"). A
  further call for the same team/object appends at the back. `TEAM_EXECUTE_SEQUENTIAL_SCRIPT` stops the team first (`groupIdle`).
- Every update (one original frame, §2): if the group is **idle** (no move target, no standing follow-up order, no command issued in this update) and no waiting time is open (`FramesToWait < 1`), or if the
  waiting time is exactly 0, **one** action of the true list of the script runs (the conditions of the script are never evaluated). If the group stays idle after that, the same runner continues in
  the same update (up to 256 steps); a command (movement, follow-up order, entry) makes it busy, a `*_FOR_FRAMECOUNT` action sets the waiting time in frames, which then counts down per
  update (N frames hold the sequence for N + 1 updates).
- End of the script: `Loops != 0` appends a fresh copy at the back (`-1` = forever; `…_LOOPING(n)` means n runs, internally n−1 repetitions); after that the script drops out of the queue.
  A team without living members or a dead/destroyed object ends the runner together with all scripts waiting behind it (`cleanupSequentialScript(…, TRUE)`).
- `TEAM_WAIT_FOR_NOT_CONTAINED_ALL/PARTIAL` and `SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_ALL/PARTIAL` are handled by the runner itself (hold mark instead of action); outside a sequential script
  they are inadmissible as in the original ("should never be used") and are counted as a diagnostic. `<This Team>`/`<This Object>` resolve to the team/object of the runner.
- **State:** the runner is saved (`sequences`, only if present); the continuation test (`ScriptSequentialScripts`) covers a running runner.
- **Deviation:** "idle" here is "no move target and no follow-up order" – a unit that shoots or executes an ability counts as idle (the automatic combat of the
  session knows no AI state); `AIGroup::isIdle` is exactly "all movable members idle".

## 5b. Team event scripts (Phase 11)

Source: `Team::updateState`, `Team::notifyTeamOfObjectDeath`, `ScriptEngine::runScript`; keys `teamOnCreateScript`, `teamOnIdleScript`, `teamEnemySightedScript`, `teamAllClearScript`,
`teamOnDestroyedScript`, `teamDestroyedThreshold`, `teamOnUnitDestroyedScript` (`ScriptTeamPrototype`). The scripts run as a subroutine with the team as `<This Team>`
(`CallSubroutine`; a name that is not a subroutine or is missing is counted as a diagnostic). Without them wave counters and many mission ends are unreachable (e.g. `MD_CHI04`: the counter of the
sixth wave depends on `teamOnUnitDestroyedScript`).

- **Created:** runs once, as soon as the team is `Created`; after that the threshold for "destroyed" is set: `curUnits − curUnits·threshold`, limited to 0…`curUnits−1` (default threshold 0 therefore
  means "already at the first loss report", as in the original).
- **Unit destroyed:** per `EntityDied` of a member, executed at the beginning of the next update (the queue is saved).
- **Destroyed:** when the number of living members changes and the threshold is reached; runs once (`threshold = −1` after that).
- **Enemy sighted/all clear:** per update (one original frame), whether any living member has an enemy in its own `VisionRange`; the change triggers the script (only as long as at least one member lives).
- **Idle:** idle twice in a row (`isIdle && wasIdle`) triggers it, again on every further update.
- State per team (`eventsCreateHandled`, `eventsCurUnits`, `eventsThreshold`, `eventsSeeEnemy`, `eventsWasIdle`) is written only for prototypes with at least one event script.

## 5c. The `SKIRMISH_*` family: decision (Phase 11)

Two ways were open (`docs/handoffs/HANDOFF-PHASE11.md` item 1): port the family or hook in `SkirmishAi` for opponents of the challenge maps. **Decided: port.** Rationale:
(a) the conditions are small, purely reading and can be delimited per function in `ScriptConditions.cpp` (§4); (b) they are in the map scripts themselves and control the behaviour of the
map sides there (the most frequent missing condition on the challenge maps, 37,227 hits for `SKIRMISH_PLAYER_HAS_UNITS_IN_AREA`) – `SkirmishAi` would be a substitute *instead of* the script logic and
would bypass the mission data instead of executing them; (c) the skirmish AI of the original (`AISkirmishPlayer`) is the execution of exactly these scripts plus build queue anyway, not something
of its own beside them. **What is missing:** `SKIRMISH_SUPPLIES_VALUE_WITHIN_DISTANCE`, `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE`, `SKIRMISH_UNOWNED_FACTION_UNIT_EXISTS`, `SKIRMISH_PLAYER_HAS_PREREQUISITE_TO_BUILD`,
`SKIRMISH_PLAYER_HAS_COMPARISON_CAPTURED_UNITS`, `SUPPLY_SOURCE_SAFE/ATTACKED` (supply source model of the AI), as well as the actions `SKIRMISH_MOVE_TO_APPROACH_PATH`/`SKIRMISH_FOLLOW_APPROACH_PATH`,
`AI_PLAYER_BUILD_SUPPLY_CENTER`, `TEAM_GUARD_SUPPLY_CENTER` – they depend on the AI player of the original, which the campaign session does not have (the opponents are purely
scripted).

## 6. Host interface

`RegisterNamed`, `AssignToTeam` (session, on placing), `SignalUiInteraction(flag)`, `SelectNamed(name)`,
`CompletePresentation(name)`, `DrainOutputs()`. Without a client `PresentationLatencyTicks = 0` applies: speech/movie/camera are
"finished" immediately (headless runs); a client may set a latency or report `CompletePresentation` itself.

## 7. Mission session

- **Players** per named side (side 0 = neutral/`PlayerId.System`), faction only if it is a real `PlayerTemplate` (otherwise a
  note). **Relationships** from `playerAllies`/`playerEnemies`; everything not named is neutral (default of `Player::getRelationship`).
- **Placed objects** belong to the player of their `originalOwner` team (without team: neutral), with `objectInitialHealth`,
  `objectName` (→ named object) and team membership. Templates that are not instantiable or unknown are counted by `SkippedObjects`
  (main reason: map overlay definitions that the retail base data do not contain, and modules still missing).
- **Build list** (`AIPlayer::newMap`/`buildStructureNow`): for computer players the `InitiallyBuilt` entries are created finished, named after
  `buildingName`, with initial health.
- **Starting base of human players:** if a human side with a real faction owns nothing on the map (Generals' Challenge maps),
  it starts like a skirmish player: base building on `Player_N_Start` plus starting vehicles in a ring.
- **Local side** (`<Local Player>`, `MULTIPLAYER_*` conditions): `MissionSession.LocalPlayerOf` takes the first side that the map itself
  sets `playerIsHuman`; `MissionOptions.HumanSideName` (version 2 of the mission state) lets the host name an arbitrary side by name
  instead — deliberately independent of `playerIsHuman`, because the Generals' Challenge maps model the chosen general side as an ordinary AI side
  and leave the "the human is this" decision to the host (`GC_LaserGeneral` marks no side as human). A name that does not exist falls back to
  the same default and leaves a note in `MissionSession.Notes`, never silently. `CampaignController.StartCurrentMission` and
  `CampaignTool audit|playthrough --human-side <Name>` pass the choice through; the actual general selection stays a matter of the shell connection
  (`docs/handoffs/HANDOFF-PHASE10.md` part 2).
- **Combat** (since Phase 17, `docs/SIMULATION-CORE.md` §33): the units choose their victims themselves (`AutoAcquireEnemiesWhenIdle`, `MoodAttackCheckRate`); the former combat driver
  `DriveCombat` (every armed object shot every three ticks at the nearest enemy in range, even while walking and regardless of its `AutoAcquire`) has been dropped. The script engine is the
  `IAcquisitionAdvisor` of the world: it supplies the attack priority (`AttackPriorityInfo`; priority 0 is never attacked, one point deduction per 100 units of distance) and the mood of the team
  (`teamAggressiveness` of the map, overridden by `TEAM_SET_ATTITUDE`): sleeping and passive teams of a computer player acquire nothing, alert and aggressive ones turn every
  move command of the script into an attack move (`MAA_Action_To_AttackMove`) and search 1.1 or 1.5 times as far. Hunt, team and area attacks (`UpdateFollowers`) are attack moves
  (`AttackMoveCommand`) to the target; the attack on a named object additionally sends the shot as a command as soon as a weapon reaches (`FireAtOrdered`), because the named victim need not be the one
  that the unit would choose itself. `MissionOptions.CombatIntervalTicks` is no longer an interval: 0 switches off the target acquisition of the mission (tests), every other value leaves it on
  (name and snapshot field stay, `MissionSnapshot` 2). `MissionCombatStats.Shots` now counts all `WeaponFired` events.

## 7a. Mission in the client: what `--mission` does and what can be seen of the `ScriptOutput`s (Phase 16, item 5)

**Flow.** `CnCNew.Client.Stride --mission <Karte>` loads the map with `MissionRun.Load` (the same session as the headless tests and `CampaignTool`), the side of the human is
`MissionSession.LocalPlayerOf` (or `--human-side`). Every logic tick of the client is `MissionPresentationDirector.BeforeStep` → `MissionSession.Step()` → `AfterStep` (`LiveMission.StepSimulation`);
the console report at the end names frames, world tick and session steps – equal in all measurement runs, so the script VM runs every tick. A loading screen or briefing belongs to the shell
(`--shell`), `--mission` begins directly with tick 0 of the map.

**Timing behaviour.** `--mission` starts with `PresentationLatencyTicks = LiveMission.ClientPresentationLatencyTicks` (1,800 ticks = 60 s). The director reports the real end itself
(`CompletePresentation`): camera moves after their script duration, speech/sound/movie after a placeholder duration of 3 s (there is no length specification per sound event), music immediately. The latency is only
the limit after which the VM no longer waits, so that nothing hangs forever. **Open:** missions that the shell starts (`GameShell` → `CampaignController.StartCurrentMission()`) still run with
latency 0 – `CAMERA_MOVEMENT_FINISHED`/`HAS_FINISHED_*` hold immediately there, the camera moves nevertheless (time-controlled sequences like those of `GC_ChemGeneral` are not affected by this).

**Stopped time (`FREEZE_TIME`/`UNFREEZE_TIME`, second session).** This is a simulation process, not a host process: `GameLogic::update` executes `ScriptEngine::UPDATE` and returns on
`isTimeFrozenScript()` – objects, combat and the frame counter stand still, the scripts keep running (timers `m_counters[i].value--` per call, sequential scripts, `UNFREEZE_TIME` itself). Exactly that is what
`MissionSession.Step` does now (`ScriptEngine.TimeFrozen`, set in `Forward` and cleared by `End` = `forceUnfreezeTime`; saved under `timeFrozen`, only while set, so
all other snapshots and hashes stay unchanged). The host must **not** stop the simulation itself for this: then there would also be no script any more that thaws. But it needs a
clock that keeps running: `MissionPresentationDirector.Clock` = world tick + number of frozen steps (without freezing equal to the world tick); camera moves, fades, subtitles,
grey veil and motion blur run on it, so `CAMERA_MOVEMENT_FINISHED` releases even with a standing world. Limits: commands that a script gives in a frozen step are
collected in the queue and executed on thawing (the original commands directly; a repeated move command is harmless, a repeated spawn would not be); scripts evaluated with a
delay (`DelayEvaluationSeconds`) compare with the world tick and wait as in the original until time runs on; `CAMERA_MOD_FREEZE_TIME` (the view freezes the logic during
a camera move) is not represented, no campaign map uses it.

**What the director makes of the outputs** (`src/CnCNew.Game/Shell/MissionPresentation.cs`, without Stride; the client reads only this state):

- **Camera** (`ApplyCameraTo(RtsCamera, seconds)`, per frame from `LiveGame.OnAdvanced`; 0 when paused): move to the waypoint over the script duration with soft ease-in and ease-out
  (`ScriptActions::doMoveCameraTo` → `View::moveCameraTo`; the ease times of the script are replaced by a fixed S-curve), cut with viewing direction/zoom/pitch
  (`doSetupCamera`), zoom and pitch as a factor of the default values (`doZoomCamera`, `doPitchCamera`, `cameraModFinalZoom/Pitch`), rotation (`doRotateCamera`), look toward the waypoint
  (`cameraModLookToward`; the sign of the camera angle is measured at the camera, not assumed), reset (`doResetCamera`), following a named object
  (`doCameraFollowNamed`). Without an active script move the camera stays with the player. Limits: zoom and pitch are limited to the playable range of the `RtsCamera` (close-ups of the
  cutscenes are thereby further away than in the original), `MOVE_CAMERA_ALONG_WAYPOINT_PATH` moves in a straight line to the first waypoint.
- **Input lock** (`DISABLE_INPUT`/`ENABLE_INPUT`, `ScriptActions::doDisableInput`): `MissionController` sets `MatchController.WorldInputEnabled`; everything except Esc (game menu) is discarded, the
  pointer does not scroll, on locking, selection, command modes and held keys are cleared. A victory or a defeat lifts the lock.
- **Letterbox and fades:** `CAMERA_LETTERBOX_BEGIN/END` hide the command bar and draw two bars (`doLetterBoxMode`: `HideControlBar`), `CAMERA_FADE_ADD/SUBTRACT/MULTIPLY` a
  screen-filling surface (white or black) with the course rise – hold – fall; `CAMERA_FADE_SATURATE` has no representation.
- **Text** (since the mission text session according to the data of the original, details in the section "Screen texts according to `InGameUI`" below): `DISPLAY_TEXT` as a message line top left, `SHOW_MILITARY_CAPTION` as a
  typed caption bottom left, `DISPLAY_CINEMATIC_TEXT` centred at 0.9 of the height, counter and countdown (`DISPLAY_COUNTER`, `DISPLAY_COUNTDOWN_TIMER`, value from `ScriptEngine.CounterValue`, countdown as minutes:seconds)
  at `NamedTimerCountdownPosition`, `INGAME_POPUP_MESSAGE` as a message window with OK button, result banner on `VICTORY`/`DEFEAT` ("MISSION ACCOMPLISHED"/"MISSION FAILED", invented placeholder text in a fixed font).
  The former hint line during the lock no longer exists (the original shows nothing).
- **Sound: playback** (`MissionPresentationDirector.AudioControl.PlayScripted` ← `GameAudio.PlayScripted`): `SPEECH_PLAY`, `SOUND_PLAY_NAMED`, `PLAY_SOUND_EFFECT`, `PLAY_SOUND_EFFECT_AT` play the named sound event without
  position (`AudioDirector.PlayUi`), `MUSIC_SET_TRACK` starts the track (`AudioDirector.SetMusicTrack`), after that the playlist of the side continues. For speech the director shows the subtitle
  `DIALOGEVENT:<Name>Subtitle` like `doSpeechPlay` (as a mission caption, 8 s; only if the table has it, it is not empty and does not begin with `*` – the English retail table hides 2,454 of 2,455) and next to it a marked
  placeholder with the event name. What does not start (unknown name, no file) is listed with a reason under "problems".
- **Movie:** `MOVIE_PLAY_FULLSCREEN`/`MOVIE_PLAY_RADAR` show the movie name as a text placeholder (no Bink decoder, permanent limit).
- **Radar:** `RADAR_CREATE_EVENT`, `OBJECT_/TEAM_CREATE_RADAR_EVENT` are drawn as a white, blinking dot on the minimap (`RadarBlipsForDisplay`, 8 s). In the measurement runs exactly one
  radar event occurred (`MD_USA01`, tick 3,191); it was handed to the minimap, but not specifically checked in the image.

Second session (all output types that campaign maps emit, see the tables below; `Game/Shell/MissionOutputCatalog.cs` keeps state and description/reason per type):

- **Sound** (`IMissionAudioControl`, implemented by `GameAudio`): volumes, event override, muting, removal, object sound, Eva switch, ambient pause – `docs/GAME-AUDIO.md`, section "Script control of the sound" („Skriptsteuerung des Tons“).
- **Camera** (`ApplyCameraTo`): `SCREEN_SHAKE(type 0..5)` calls `RtsCamera.Shake` with the `GameData` strength of the type (`ShakeSubtleIntensity` … `ShakeCineInsaneIntensity`, `CameraShakeIntensities`) at the look-at point
  (`W3DView::shake`: full strength, decaying with 0.75 per logic frame and reversing; the original draws the direction from the client random, here a golden-angle sequence); `MAP_SWITCH_BORDER(n)` sets
  `RtsCamera.ActiveBoundary` (the camera stays within the frame of this map boundary; a boundary that the map does not have is reported; the minimap keeps boundary 0); `CAMERA_LOOK_TOWARD_OBJECT` turns
  to the position of the object and holds `duration + hold time` as a camera movement; `CAMERA_TETHER_NAMED` is approximated as following (play ignored).
- **Screen filters** (`MissionPresentationDirector.PostEffects`, drawn by `ScreenFilters` in the Stride client above the finished world and below the HUD): `CAMERA_BW_MODE_BEGIN/END(frames)` ramps the
  grey share over the frames (`ScreenBWFilter::set`); `CAMERA_MOTION_BLUR_JUMP(waypoint, saturate)` is the counting automaton of `ScreenMotionBlurFilter::postRender`: +5 per logic frame up to 60 (12 frames), at the
  apex a cut of the camera to the waypoint (`TheTacticalView->lookAt`), then −5 down to 0 (zoom factor `sqrt(1 − counter/60 · 0.9)`, up to 30 further layers with 1 % zoom and alpha 0x15, with the
  saturating variant 2 % and additive). One filter replaces the other (`setViewFilter`). **Approximations, not copies:** the grey veil is the **red channel** alone (`SwizzleMode.RRRR` is all that
  the sprite batch offers in channel mixing; the original mixes 0.30/0.59/0.11), the image looks as if photographed through a red filter; the blur sums the layers over sprite drawings
  of the copied image surface instead of over texture coordinates in the shader. Whether both look good can only be judged by a human (image sample below).
- **Objects** (`IMissionObjectEffects`, `LiveMission` → `FrameProjector`): `NAMED_FLASH`/`TEAM_FLASH` (house colour) and `…_WHITE` flash `seconds · 2` times every 15 logic frames (`Drawable::setFlashCount`,
  `DRAWABLE_FRAMES_PER_FLASH`), every flash is the tint envelope 0 rise/4 fall of the capture (`SelectionFlashTracker.StartScripted/Advance`), the colour unsaturated (`colorFlash(&color)`);
  `NAMED_CUSTOM_COLOR` replaces the house colour of an object (`FrameProjector.SetHouseColor`, value ARGB, 0 = back); that also affects the minimap dots, as in the original (`Radar.cpp`).
- **Command bar and HUD** (`IMissionHudControl`, `MissionController`): `COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE(button, object type)` removes the button from the command set of the object type **for every object that
  uses this set** (`GameLogic::setControlBarOverride`, key set name + slot; `CommandBarModel.RemoveButton/AddButton`, state in `ControlBarScriptState`), `…_ADD_BUTTON_…_SLOT` puts it into
  a slot (1-based); `PLAYER_SCIENCE_AVAILABILITY` (only for the local side) makes sciences in `PurchaseScience` buttons hidden/locked; `OPTIONS_SET_DRAWICON_UI_MODE` omits the
  healing icons; `DISABLE_/ENABLE_SPECIAL_POWER_DISPLAY` and `NAMED_HIDE_/NAMED_SHOW_SPECIAL_POWER_DISPLAY` switch the **superweapon list** (top right "Name: m:ss" per power with a public
  timer, new in this session, `SuperweaponTimers`, `docs/GAME-HUD.md` §14) off and on entirely or per object; `UNIT_/TEAM_AFFECT_OBJECT_PANEL_FLAGS` has a consumer for **Selectable** (`EntityPicker.SetScriptSelectable`: not selectable, drops out of the selection), the other flags
  (`Enabled`, `Powered`, `Indestructible`, `Unsellable`, `AI Recruitable`, `Player Targetable`) need simulation state and are listed as a problem in the report.

**Screen texts according to `InGameUI` (Phase 16, mission texts).** Sources: `InGameUI.cpp` (`message`/`addMessageText`, `update`, `postDraw`, `militarySubtitle`, `addNamedTimer`, `popupMessage`), `ScriptActions.cpp`
(`doDisplayText`, `doMilitaryCaption`, `doDisplayCinematicText`, `doInGamePopupMessage`, `doDisplayCounter`/`…CountdownTimer`), `W3DDisplay.cpp` (`draw`: cinematic text), `InGamePopupMessage.cpp`; rewritten, not copied.
The values come from the data: `InGameUiSettings` (`CnCNew.Ui/Text`, the typed `InGameUI.ini`: positions, colours, times) and `Language.ini` (fonts; `InGameUI::init` overwrites every font of the block with the
Language slot of the same name, always, because the default name of a slot is never empty – measured, size and bold agree in the retail file, only the unquoted name "Courier New" of the INI becomes "Courier" through
`parseAsciiString`), combined in `InGameUiStyle` (`CnCNew.Game/Data`), scaled like every text (`GlobalLanguage::adjustFontSize`, `LanguageFonts.Resolve`; 1280x720: Arial 10 → 14, Courier New 12 → 17).
Without `MissionPresentationDirector.TextStyle` (or without a text measure) no text is drawn and the first text output is a reported problem – never an invented size. A label that the host cannot resolve
appears as itself and is listed under "problems" (`label … has no text`).

| Output | Path in the original | Location | Font, colour | Time and behaviour |
| --- | --- | --- | --- | --- |
| `DISPLAY_TEXT` | `InGameUI::message` → `addMessageText` | `MessagePosition` (10, 22) in **unscaled** pixels, one line below the other (height of the font), oldest on top | `MessageFont` (Arial 10 bold), `MessageColor1`/`MessageColor2` alternating (compared with the colour of the previous line, even a faded one), shadow black with the alpha of the line | at most 6 lines; `MessageDelayMS / 30 / 1000` is **integer 0**, the line loses `floor(age · 0.01)` alpha per step from the first frame on (nothing up to age 99, then 1, from 200 on 2 each) and disappears at age 277 (9.2 s) |
| `SHOW_MILITARY_CAPTION` | `militarySubtitle`, `m_militarySubtitle` in `update`/`postDraw` | `MilitaryCaptionPosition` (10, 340), x scaled with width/800, y with height/600; one line below the other, an empty line has no height | first line `MilitaryCaptionTitleFont` (Courier New bold), further ones `MilitaryCaptionFont`; `MilitaryCaptionColor` (white), shadow black | typewriter: first character after `MilitaryCaptionDelayMS` (`Language.ini`, 750 ms = 22 frames), then every `MilitaryCaptionSpeed` (1) frame – the comparison is "less than", so one character per **second** frame –; a line break begins the next line and waits 22 frames again; at most 4 lines (rest discarded); block cursor (height of the font, 0.8 wide) behind the last character, toggles every 10 frames; after the duration (`30 · ms / 1000` frames) the colour loses `floor((frame − end) · 0.1)` alpha per step, when it falls below 0 it is gone (76 steps); a new caption replaces the old one; empty text or duration ≤ 0 creates none (reported); `MilitaryCaptionRandomizeTyping` has no effect in the original |
| `DISPLAY_CINEMATIC_TEXT` | `doDisplayCinematicText`, `W3DDisplay::draw` | top edge at `0.9 · height`, centred, wrap at width − 20 (`UiSentenceLayout`, centred layout; x = 20 if wider than the screen) | the font from the script string `Name - Size:N [Bold]` (adjusted), white, no shadow | `30 · seconds` frames; the splitting of the string is faulty in the original (breaks at spaces in the name), cleanly split here; no campaign script uses the action |
| `DISPLAY_COUNTER` / `…COUNTDOWN_TIMER` | `addNamedTimer`, `postDraw` | `NamedTimerCountdownPosition` (0.90 / 0.666 of the image area), per line **upward** by the font height, order by name; from x ≥ 0.5 on right-aligned (`reverseXDir`) | `NamedTimerCountdownNormalFont`/`…Color` (Arial 10, white); at 0:00 `…ReadyFont` and blinking between colour and `…FlashColor` every `…FlashDuration` (15 frames), flash colour in the odd periods | text "Label m:ss" (countdown, `floor(frames / 30)`, never negative) or "Label value" (counter, raw value); `DISABLE_COUNTDOWN_TIMER_DISPLAY` hides all |
| Superweapon list | `postDraw` | `SuperweaponCountdownPosition` (0.90 / 0.01), `docs/GAME-HUD.md` §14 | `SuperweaponCountdown*Font` (ready bold), house colour, flash colour `…FlashColor` | as there; "..." from 0.82 of the height on |
| `INGAME_POPUP_MESSAGE` | `popupMessage` → `InGamePopupMessageInit` | x/y in percent (0..100) of the image area, width ≥ 50 pixels; text wrap width − 14, OK bottom right (`MissionPopupWindow`) | the windows from `InGamePopupMessage.wnd` (font of the template; text colour 254 instead of `PopupMessageColor`) | stays until OK, Enter or Escape (on release); with pause mark the mission pauses (`LiveGame.Paused`) and the window is modal; without it the world outside the window stays operable |
| `SPEECH_PLAY` | `doSpeechPlay`: subtitle `DIALOGEVENT:<Name>Subtitle` through `militarySubtitle` (8000 ms), only if present, not empty, no leading `*` | like the mission caption | like it | in English almost never (2,454 of 2,455 with `*`); next to it the placeholder "(speech: Name)" bottom centre in the `MessageFont`, 3 s |
| Movie | – (placeholder, no Bink) | bottom centre (in the letterbox in the lower bar) | `MessageFont` of the data, white | 3 s; marked placeholder |

All times run on the presentation clock (`MissionPresentationDirector.Clock`, world tick plus frozen steps), i.e. on while time is stopped; in the original `update` decrements `lifetime`/`blockBeginFrame`/`incrementOnFrame`
per frame for this. The state is a pure function of the frames since the beginning (`UiMessageLog`, `MilitaryCaptionState` in `Shell/MissionText.cs`, one `Update(frame)` per step), the draw lists are built by `MissionTextOverlay`; both can be
tested without renderer and world. **Limits:** the original updates per drawn frame, here per logic step (visible when fading, not with the typewriter);
the result banner is invented. **Since 5 October 2026 (third session) brought in line with the original:** (1) the **typing click**: every typed character (no line break) requests
`MilitarySubtitlesTyping` from the sound of the host (`InGameUI::update`: `static AudioEventRTS click("MilitarySubtitlesTyping")`); the report names `mission caption typing: N characters typed,
M clicks started (…)`, without sound `M = 0` with a reason. (2) the **letterbox** according to `W3DDisplay::renderLetterBox`: two black rectangles of the height `(height − 9/16 · width) / 2` at top and bottom
(800x600: 75 pixels, 1024x768: 96, 1280x1024: 152; at 16:9 and wider **none**, the rectangles would lie outside the image), their alpha is the fade level (`LETTER_BOX_FADE_TIME` 1000 ms,
here 30 logic steps of the presentation clock), at the end only the **upper** bar fades out (the call for the lower one is commented out in the original, it disappears immediately);
order as in `W3DDisplay::draw`: messages, caption and timers (`InGameUI::postDraw`), above them the bars, above them the cinematic text (`MissionPresentationDirector.LetterboxBars`,
`LetterboxFadeLevel`). The command bar still disappears immediately (`ScriptActions::doLetterBoxMode`: `HideControlBar`). (3) the **message list** is one: the lines of the HUD
(`HudMessageFeed`, `docs/GAME-HUD.md` §8) and `DISPLAY_TEXT` share `UiMessageLog` (`UseMessages`). **Not verified:** the appearance compared to the original and in motion (`docs/TEXT-AUDIT.md` §7 items 8–12), the message window in the real game
(no campaign map opens it in the first 4000 frames; `Training01` opens it only later, on a player action), cinematic text (used by no map).
**Counters.** Every output type has in the catalog (`MissionOutputCatalog`) exactly one of three states: *displayed* (a consumer in the client acts where the player sees it, hears it or is
restricted by it), *not displayed* (with a reason, see below) or *handled elsewhere* (the simulation does it itself; only `FREEZE_TIME`/`UNFREEZE_TIME`). The director counts per output type `emitted`
(emitted by the script), `forwarded` (handed to a consumer: camera, overlay, sound, radar, input, command bar/HUD, object layer, screen filters) and `displayed` (the consumer exists in this
host **and** the type has a consumer: the camera was applied, the overlay drawn, the lock queried, the minimap fetched the dots, the renderer queried the filters, a
command bar/object layer is connected; sound counts only if a voice started or the control took effect). A headless run without a host therefore reports "passed on", but never "displayed"; a
type without a consumer has `displayed` 0, even if a model keeps its value (`PLAYER_SCIENCE_AVAILABILITY`). `NotDisplayed = emitted − displayed − handledElsewhere`. The client writes the counters
on exit and in the smoke test to the console (`mission: …`, `mission outputs: … displayed …, handled elsewhere …, not displayed …`,
`mission output <TYPE>: emitted …, forwarded …, displayed …, handled elsewhere …, not displayed … (how/why)`, `mission output problems: …`, `mission presentation: input locked for … ticks, locked at exit …`,
`mission screen filters: …`, plus the line `audio script control: …` of the audio report); `--mission-journal N` additionally prints the first N displayable outputs with tick.
`CampaignTool outputs <Inhalt> [--maps]` counts statically which passed-on types the 24 campaign maps use and in which state they are (exit code 1 if a type is missing in the catalog).

**Measurement (5 October 2026, second session; `--smoke-test --mission … --size 1280x720 --mute`, Release, fixed time steps; freshly measured):**

| Map | Frames / VM steps | World tick at the end | Side of the human | Types | emitted | passed on | displayed | elsewhere | not displayed | Input locked (ticks) | locked at the end |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `GC_ChemGeneral` | 3000 / 3000 | 3000 | `ThePlayer` (FactionAmericaAirForceGeneral) | 14 | 29 | 29 | **29** | 0 | **0** | 750 | no |
| `MD_GLA03` | 3000 / 3000 | 3000 | `ThePlayer` (FactionGLA) | 43 | 124 | 95 | **87** | 0 | **37** | 1750 | no |
| `MD_USA01` | 3900 / 3900 | **3223** | `The_Player` (FactionAmerica) | 51 | 223 | 191 | **173** | **2** | **48** | 3132 | no |

(The measurement of 4 October, before this session: displayed 17 / 35 / 72 and not displayed 12 / 89 / 151.) Missing action or condition hits: 0 on all three maps. Reported
problems: `GC_ChemGeneral` none; `MD_GLA03`: `ENABLE_OBJECT_SOUND` names an object that does not exist then (×1); `MD_USA01`: `MUSIC_SET_TRACK 'Silence'` does not start (×1, the silence does not exist as a
track), `PLAY_SOUND_EFFECT_AT 'Cin_CombatCycleMainLoop'` does not start (×1: the same script muted the event at tick 75 with `SOUND_DISABLE_TYPE` and made it audible again by override only at tick 555), `UNIT_AFFECT_OBJECT_PANEL_FLAGS` "Indestructible" without
a consumer (×2). `MD_USA01` shows the stopped time: 3900 frames, but world tick 3223 – for 677 steps the world stood still while the grey veil and the camera move of the spy shot ran (the
client reports 677 frames drawn through the filter). `GC_ChemGeneral` has the motion blur from tick 675 on: 23 filtered frames, as the counting automaton prescribes.

Image sample (`--filmstrip`, PNGs viewed outside the repo): grey veil in `MD_USA01` (red-channel grey: snow and rock bright, water/vegetation dark – a spy camera impression, not a
luminance image), ray blur onto the headquarters in `GC_ChemGeneral` at the time of the cut. **Not judged:** whether the blur equals that of the original (layer sum instead of shader), how the
shaking of the camera looks in motion (single frames do not show a shift of a few pixels), and the sound (no hearing). **Noticed and not a subject of this session:** the first shot
of `MD_USA01` (tick 0–400, `SETUP_CAMERA(CINE_CameraPos01, 0.2, -0.45, …)`) shows above the rock only the clear colour (cornflower blue) instead of the ground – whether the camera lies so low/flat there that the
terrain in front of it is not drawn, or whether it is clipped, is **not investigated**; it is none of the outputs treated here and belongs to the camera findings.

**All output types of the 24 campaign maps** (`CampaignTool outputs <Inhalt> [--maps]`: static count of the passed-on action types in the scripts, plus the state from
`MissionOutputCatalog`; 92 types, 6,977 uses; **69 displayed, 2 handled by the simulation, 21 not displayed**; before 35 with and 57 without a consumer; 37 passed-on types
occur in no campaign map and are only in the catalog):

**With a consumer in the client** (state "displayed"; uses = actions in the scripts of all campaign maps, maps = maps with at least one use):

| `ScriptOutput` type | Consumer | Uses | Maps |
| --- | --- | ---: | --- |
| `COMMANDBAR_ADD_BUTTON_OBJECTTYPE_SLOT` | command bar (`ControlBarScriptState`) | 7 | MD_CHI02, MD_CHI04, MD_GLA02, MD_USA05 |
| `COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE` | command bar (`ControlBarScriptState`) | 148 | 14 of 24 |
| `CAMERA_BW_MODE_BEGIN` | screen filter (`PostEffects`, Stride `ScreenFilters`) | 5 | MD_USA01, MD_USA05 |
| `CAMERA_BW_MODE_END` | screen filter (`PostEffects`, Stride `ScreenFilters`) | 5 | MD_USA01, MD_USA05 |
| `CAMERA_MOTION_BLUR_JUMP` | screen filter (`PostEffects`, Stride `ScreenFilters`) | 12 | GC_AirGeneral, GC_ChemGeneral, GC_LaserGeneral, GC_Stealth, GC_SuperWeaponsGeneral, GC_TankGeneral, MD_USA05, Training01 |
| `DISABLE_INPUT` | input lock (`WorldInputEnabled`) | 139 | 24 of 24 |
| `ENABLE_INPUT` | input lock (`WorldInputEnabled`) | 35 | 24 of 24 |
| `OPTIONS_SET_DRAWICON_UI_MODE` | HUD (`IMissionHudControl`) | 65 | 16 of 24 |
| `UNIT_AFFECT_OBJECT_PANEL_FLAGS` | HUD (`IMissionHudControl`) | 32 | GC_LaserGeneral, MD_CHI01, MD_GLA02, MD_GLA03, MD_GLA04, MD_USA01, MD_USA03, MD_USA04 |
| `DISABLE_SPECIAL_POWER_DISPLAY` | HUD: superweapon list (`SuperweaponTimers`) | 3 | GC_ChinaBoss, MD_GLA01, MD_GLA03 |
| `ENABLE_SPECIAL_POWER_DISPLAY` | HUD: superweapon list (`SuperweaponTimers`) | 3 | GC_ChinaBoss, MD_GLA03 |
| `NAMED_HIDE_SPECIAL_POWER_DISPLAY` | HUD: superweapon list (`SuperweaponTimers`) | 31 | GC_LaserGeneral, GC_NukeGeneral, GC_Stealth, GC_SuperWeaponsGeneral, MD_CHI01, MD_CHI04, MD_CHI05, MD_GLA03, MD_USA04 |
| `NAMED_SHOW_SPECIAL_POWER_DISPLAY` | HUD: superweapon list (`SuperweaponTimers`) | 20 | GC_LaserGeneral, GC_NukeGeneral, GC_Stealth, GC_SuperWeaponsGeneral, MD_CHI05, MD_GLA03, MD_USA04 |
| `CAMERA_FOLLOW_NAMED` | camera (`ApplyCameraTo`) | 2 | MD_USA03 |
| `CAMERA_LOOK_TOWARD_OBJECT` | camera (`ApplyCameraTo`) | 9 | GC_NukeGeneral, MD_GLA01, Training01 |
| `CAMERA_LOOK_TOWARD_WAYPOINT` | camera (`ApplyCameraTo`) | 11 | GC_ChinaBoss, MD_CHI02, MD_CHI04, MD_GLA02, MD_GLA03, MD_GLA04, MD_USA01, MD_USA02, MD_USA03, MD_USA05 |
| `CAMERA_MOD_FINAL_LOOK_TOWARD` | camera (`ApplyCameraTo`) | 3 | MD_GLA05, Training01 |
| `CAMERA_MOD_LOOK_TOWARD` | camera (`ApplyCameraTo`) | 82 | 14 of 24 |
| `CAMERA_MOD_SET_FINAL_PITCH` | camera (`ApplyCameraTo`) | 15 | GC_NukeGeneral, MD_CHI01, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA01, MD_USA03, Training01 |
| `CAMERA_MOD_SET_FINAL_ZOOM` | camera (`ApplyCameraTo`) | 14 | MD_CHI01, MD_CHI03, MD_CHI05, MD_GLA03, MD_GLA04, MD_USA01, MD_USA03, Training01 |
| `CAMERA_STOP_FOLLOW` | camera (`ApplyCameraTo`) | 4 | MD_USA03 |
| `CAMERA_STOP_TETHER_NAMED` | camera (`ApplyCameraTo`) | 2 | Training01 |
| `CAMERA_TETHER_NAMED` | camera (`ApplyCameraTo`) | 2 | Training01 |
| `MOVE_CAMERA_ALONG_WAYPOINT_PATH` | camera (`ApplyCameraTo`) | 1 | MD_GLA03 |
| `MOVE_CAMERA_TO` | camera (`ApplyCameraTo`) | 152 | 23 of 24 |
| `PITCH_CAMERA` | camera (`ApplyCameraTo`) | 60 | MD_CHI02, MD_CHI04, MD_CHI05, MD_GLA01, MD_GLA03, MD_GLA05, MD_USA01, MD_USA02, MD_USA03, MD_USA04, MD_USA05, Training01 |
| `RESET_CAMERA` | camera (`ApplyCameraTo`) | 25 | 17 of 24 |
| `ROTATE_CAMERA` | camera (`ApplyCameraTo`) | 54 | 23 of 24 |
| `SETUP_CAMERA` | camera (`ApplyCameraTo`) | 152 | 23 of 24 |
| `ZOOM_CAMERA` | camera (`ApplyCameraTo`) | 91 | 17 of 24 |
| `MAP_SWITCH_BORDER` | camera: map border (`ActiveBoundary`) | 31 | GC_ChinaBoss, MD_CHI01, MD_CHI04, MD_CHI05, MD_GLA01, MD_GLA05, MD_USA01, MD_USA03, MD_USA04, MD_USA05, Training01 |
| `SCREEN_SHAKE` | camera: shake (`RtsCamera.Shake`) | 96 | MD_CHI01, MD_GLA01, MD_GLA04, MD_USA01, MD_USA03, MD_USA05 |
| `OBJECT_CREATE_RADAR_EVENT` | minimap (blinking dot) | 152 | GC_ChinaBoss, MD_CHI02, MD_CHI03, MD_CHI05, MD_GLA01, MD_GLA02, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA01, MD_USA03, MD_USA04, MD_USA05 |
| `RADAR_CREATE_EVENT` | minimap (blinking dot) | 88 | MD_CHI02, MD_CHI04, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA02, MD_USA03, Training01 |
| `TEAM_CREATE_RADAR_EVENT` | minimap (blinking dot) | 11 | MD_CHI05, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA03, MD_USA04 |
| `NAMED_CUSTOM_COLOR` | object layer (`FrameProjector`) | 198 | GC_LaserGeneral, MD_CHI05, MD_GLA01, MD_GLA03, MD_GLA04, MD_GLA05 |
| `NAMED_FLASH` | object layer (`FrameProjector`) | 76 | MD_CHI01, MD_CHI02, MD_GLA02, MD_GLA03, MD_USA04, MD_USA05, Training01 |
| `NAMED_FLASH_WHITE` | object layer (`FrameProjector`) | 32 | MD_GLA01, MD_GLA04, MD_GLA05, MD_USA01, MD_USA04, MD_USA05 |
| `TEAM_FLASH` | object layer (`FrameProjector`) | 3 | MD_CHI01, MD_GLA05 |
| `TEAM_FLASH_WHITE` | object layer (`FrameProjector`) | 1 | MD_GLA04 |
| `CAMERA_FADE_ADD` | overlay (bars, fade) | 14 | GC_ChinaBoss, GC_LaserGeneral, MD_CHI01, MD_GLA01, MD_GLA03, MD_USA01, MD_USA05, Training01 |
| `CAMERA_FADE_MULTIPLY` | overlay (bars, fade) | 166 | 24 of 24 |
| `CAMERA_LETTERBOX_BEGIN` | overlay (bars, fade) | 144 | 24 of 24 |
| `CAMERA_LETTERBOX_END` | overlay (bars, fade) | 37 | 24 of 24 |
| `DISABLE_COUNTDOWN_TIMER_DISPLAY` | overlay (text) | 64 | 20 of 24 |
| `DISPLAY_COUNTDOWN_TIMER` | overlay (text) | 3 | MD_CHI02, MD_GLA05, MD_USA05 |
| `DISPLAY_COUNTER` | overlay (text) | 3 | MD_CHI03, MD_GLA04 |
| `HIDE_COUNTDOWN_TIMER` | overlay (text) | 4 | MD_CHI02, MD_GLA05, MD_USA05 |
| `HIDE_COUNTER` | overlay (text) | 7 | MD_CHI03, MD_GLA04, MD_GLA05 |
| `INGAME_POPUP_MESSAGE` | overlay (text) | 14 | Training01 |
| `SHOW_MILITARY_CAPTION` | overlay (text) | 269 | 17 of 24 |
| `MUSIC_SET_VOLUME` | sound: bus factor (`SetScriptVolume`) | 24 | GC_ChemGeneral, GC_LaserGeneral, GC_Stealth, GC_SuperWeaponsGeneral, GC_TankGeneral, MD_CHI05, MD_GLA02, MD_GLA05, MD_USA01, MD_USA03, MD_USA04, MD_USA05 |
| `SOUND_SET_VOLUME` | sound: bus factor (`SetScriptVolume`) | 281 | 23 of 24 |
| `SPEECH_SET_VOLUME` | sound: bus factor (`SetScriptVolume`) | 188 | GC_AirGeneral, GC_NukeGeneral, GC_TankGeneral, MD_CHI02, MD_GLA01, MD_GLA03, MD_GLA05, MD_USA03 |
| `AUDIO_OVERRIDE_VOLUME_TYPE` | sound: switch (`AudioDirector.Script`) | 63 | GC_LaserGeneral, MD_CHI01, MD_CHI04, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA01, MD_USA03, MD_USA04, MD_USA05 |
| `AUDIO_RESTORE_VOLUME_ALL_TYPE` | sound: switch (`AudioDirector.Script`) | 21 | 15 of 24 |
| `DISABLE_OBJECT_SOUND` | sound: switch (`AudioDirector.Script`) | 31 | MD_CHI01, MD_CHI03, MD_CHI05, MD_GLA01, MD_GLA03, MD_GLA05, MD_USA01, MD_USA03 |
| `ENABLE_OBJECT_SOUND` | sound: switch (`AudioDirector.Script`) | 64 | MD_CHI02, MD_CHI03, MD_CHI05, MD_GLA01, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA03 |
| `EVA_SET_ENABLED_DISABLED` | sound: switch (`AudioDirector.Script`) | 68 | 17 of 24 |
| `SOUND_DISABLE_TYPE` | sound: switch (`AudioDirector.Script`) | 300 | 14 of 24 |
| `SOUND_ENABLE_ALL` | sound: switch (`AudioDirector.Script`) | 20 | GC_ChinaBoss, GC_NukeGeneral, MD_CHI01, MD_CHI02, MD_CHI03, MD_CHI04, MD_GLA02, MD_GLA03, MD_GLA04, MD_USA01, MD_USA03, MD_USA04, MD_USA05 |
| `SOUND_ENABLE_TYPE` | sound: switch (`AudioDirector.Script`) | 21 | MD_CHI02, MD_USA01, MD_USA03 |
| `SOUND_REMOVE_ALL_DISABLED` | sound: switch (`AudioDirector.Script`) | 17 | MD_CHI01, MD_CHI04, MD_CHI05, MD_GLA02, MD_GLA03, MD_USA01, MD_USA03, MD_USA04, MD_USA05 |
| `SOUND_REMOVE_TYPE` | sound: switch (`AudioDirector.Script`) | 3 | MD_USA02 |
| `MUSIC_SET_TRACK` | sound: playback (`GameAudio.PlayScripted`) | 710 | 24 of 24 |
| `PLAY_SOUND_EFFECT` | sound: playback (`GameAudio.PlayScripted`) | 120 | 18 of 24 |
| `PLAY_SOUND_EFFECT_AT` | sound: playback (`GameAudio.PlayScripted`) | 69 | GC_AirGeneral, MD_CHI02, MD_CHI04, MD_GLA03, MD_USA01, MD_USA03, MD_USA05 |
| `SOUND_PLAY_NAMED` | sound: playback (`GameAudio.PlayScripted`) | 1 | MD_GLA02 |
| `SPEECH_PLAY` | sound: playback (`GameAudio.PlayScripted`) | 1668 | 24 of 24 |

**Handled by the simulation** (state "elsewhere"):

| `ScriptOutput` type | Uses | Maps |
| --- | ---: | --- |
| `FREEZE_TIME` | 3 | MD_USA01, Training01 |
| `UNFREEZE_TIME` | 3 | MD_USA01, Training01 |

**Not displayed** (state "not displayed", reason in the list below):

| `ScriptOutput` type | Uses | Maps |
| --- | ---: | --- |
| `CAMEO_FLASH` | 4 | MD_GLA02 |
| `CAMERA_ADD_SHAKER_AT` | 2 | MD_GLA01, MD_GLA02 |
| `CAMERA_MOD_FREEZE_ANGLE` | 1 | GC_ChinaBoss |
| `DEBUG_MESSAGE_BOX` | 3 | GC_LaserGeneral, MD_GLA03, MD_GLA04 |
| `DISABLE_BORDER_SHROUD` | 27 | 14 of 24 |
| `DISABLE_SCORING` | 24 | 15 of 24 |
| `DRAW_SKYBOX_BEGIN` | 24 | 14 of 24 |
| `DRAW_SKYBOX_END` | 19 | MD_CHI02, MD_CHI04, MD_CHI05, MD_GLA01, MD_GLA02, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA01, MD_USA03, MD_USA04, MD_USA05, Training01 |
| `ENABLE_BORDER_SHROUD` | 19 | MD_CHI01, MD_CHI02, MD_CHI03, MD_CHI05, MD_GLA01, MD_GLA02, MD_GLA03, MD_GLA05, MD_USA01, MD_USA02, MD_USA03, MD_USA04, MD_USA05 |
| `ENABLE_SCORING` | 20 | 15 of 24 |
| `OPTIONS_SET_OCCLUSION_MODE` | 73 | 18 of 24 |
| `OPTIONS_SET_PARTICLE_CAP_MODE` | 70 | 18 of 24 |
| `OVERSIZE_TERRAIN` | 52 | 15 of 24 |
| `PLAYER_EXCLUDE_FROM_SCORE_SCREEN` | 60 | MD_CHI01, MD_CHI02, MD_CHI04, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA01, MD_USA02, MD_USA03, Training01 |
| `PLAYER_SCIENCE_AVAILABILITY` | 201 | MD_CHI01, MD_CHI02, MD_CHI03, MD_CHI04, MD_GLA01, MD_GLA02, MD_GLA03, MD_GLA04, MD_USA01, MD_USA02, MD_USA03, MD_USA04, Training01 |
| `RADAR_ENABLE` | 2 | MD_GLA04, Training01 |
| `RADAR_FORCE_ENABLE` | 11 | MD_CHI01, MD_CHI02, MD_CHI04, MD_GLA01, MD_GLA03, MD_GLA04, MD_USA02, MD_USA03, MD_USA04 |
| `RESIZE_VIEW_GUARDBAND` | 35 | MD_CHI02, MD_CHI04, MD_CHI05, MD_GLA01, MD_GLA02, MD_GLA03, MD_USA01, MD_USA02, MD_USA03, MD_USA04, MD_USA05 |
| `SET_FPS_LIMIT` | 49 | 15 of 24 |
| `SHOW_WEATHER` | 7 | MD_CHI05, MD_USA03 |
| `WATER_CHANGE_HEIGHT_OVER_TIME` | 2 | MD_CHI04, MD_GLA04 |

**Reasons for "not displayed"** (the wording per type is in `MissionOutputCatalog.cs`):

- **Scoring** (`ENABLE_SCORING`, `DISABLE_SCORING`, `PLAYER_EXCLUDE_FROM_SCORE_SCREEN`): the mission result screen (`MissionResultScreen`) shows one line without numbers. With scoring
  switched off the original counts nothing (`ScoreKeeper` asks `isScoringEnabled()`) and does not list sides with `setListInScoreScreen(false)`; the director keeps both (`ScoringEnabled`,
  `ScoreScreenExcluded`), a statistics screen would read it there.
- **Sciences** (`PLAYER_SCIENCE_AVAILABILITY`, 201 uses in 13 maps): the command bar knows the availability (`ControlBarScriptState`; "Hidden" = the `PurchaseScience` button is missing,
  "Disabled" = locked; test against the retail data), but the client has no Generals Powers window in which one buys sciences – so there is nothing to see. Applies only to the local side;
  other sides buy through the AI/simulation, which does not read this (in the report as the problem "availability of a side other than the local player").
- **Radar** (`RADAR_ENABLE`, `RADAR_FORCE_ENABLE`, …): the client knows no radar facility rule, the minimap is always on; there is nothing to force.
- **Sky and border** (`DRAW_SKYBOX_*`, `ENABLE_/DISABLE_BORDER_SHROUD`, `OVERSIZE_TERRAIN`, `RESIZE_VIEW_GUARDBAND`): the sky body `new_skybox` is drawn by no layer (the sky
  above the horizon is the clear colour); the fog texture covers only the playable area, the terrain behind it takes its border value, a separate shadow ring for the active boundary does not exist;
  the terrain layer draws the whole height field including the border (nothing to "oversize"); the client clips no drawables at a guard band. The director keeps `SkyboxRequested` and
  `BorderShroudEnabled`.
- **Render options** (`OPTIONS_SET_OCCLUSION_MODE`, `OPTIONS_SET_PARTICLE_CAP_MODE`, `SET_FPS_LIMIT`): options of the original (markers for units behind buildings, dynamic particle limit,
  frame rate limit) that the client does not have or does not let scripts control.
- **Camera and world** (`CAMERA_ADD_SHAKER_AT`: the rotational shaker needs a camera roll offset that `RtsCamera` does not have; `CAMERA_MOD_FREEZE_ANGLE`; `WATER_CHANGE_HEIGHT_OVER_TIME`: the water plane
  has a fixed height, the original also shifts the logical water level; `SHOW_WEATHER`: neither snow nor rain is drawn; `CAMEO_FLASH`: no blinking of button images; `DEBUG_MESSAGE_BOX`).

Tests: `tests/CnCNew.Game.Tests/MissionEffectsTests.cs` (catalog complete; script outputs → sound, objects, HUD with dummies, "displayed needs the consumer"; the clock runs while time is stopped;
shaking; map border; the counting automata of grey veil and blur; with `--content-dir`: `MD_USA01` 3900 frames through the real HUD and the real camera, removed and re-inserted command buttons
in the bar of a selected object, sciences and selection, house colour and flashing in the projected image), `AudioDirectorTests` (script control at the rendered output), `Audio.Tests`
(`SetVoiceVolume`), `Presentation.Tests` (`SelectionFlashTracker.StartScripted`), `Simulation.Tests` (stopped time, save/load frozen), as well as
`tests/CnCNew.Game.Tests/MissionPresentationTests.cs` (letterbox, lock, text, counters, time course of a camera movement, "displayed needs a host") and `MissionHudTests.cs` (real
map `GC_ChemGeneral`: lock and letterbox of the opening, discarded clicks, return after around 25 s, camera at the end above the own base, result banner); `scripts/check-dozer-build.ps1`
checks the same in the real client (`docs/GAME-HUD.md` §17).

**Finding while measuring (first session):** `MD_GLA03` ended the client in the first frame with an unhandled `InvalidDataException` – the model `cineexplbox` contains only a collision box and no mesh.
`ModelAssetCache` now treats an unusable model like a missing one (reported once, counted, nothing drawn).

**Still open (speech and camera):** no sound length (the subtitles exist, the English hides almost all of them) – `HAS_FINISHED_AUDIO` holds after 3 s, longer announcements overlap the next one; camera paths along the
waypoint links, the ease times of the script; `CAMERA_MOD_FREEZE_TIME`, `CAMERA_MOD_SET_FINAL_SPEED_MULTIPLIER`, `CAMERA_MOD_SET_ROLLING_AVERAGE`, `CAMERA_SET_DEFAULT`,
`CAMERA_ENABLE_/DISABLE_SLAVE_MODE`, `CAMERA_MOVE_HOME`, `MOVE_CAMERA_TO_SELECTION` (used by no campaign map); the states of the script changes (command bar, house colours, selection lock,
volumes) are host state and do **not** travel along when a mission is saved/loaded (`MissionSnapshot` knows only the simulation and the script VM): after loading the command bar applies again
unchanged, until the script sets the state anew.

### Time and tick rate of the mission presentation (Phase 21)

The director still uses the tick as a clock, but no longer assumes that it lasts 1/30 s. `MissionPresentationDirector.Clock` is the tick of the world plus the frozen steps (`FREEZE_TIME`), in ticks of the rate of the session; `FrameClock` is the same clock in **original frames** (1/30 s). At 30 Hz both are equal, and everything is unchanged (checksums: `docs/TICKRATE-MIGRATION.md` §14.2).

| Output | Unit of the script or of the data | Conversion |
|---|---|---|
| `CAMERA_FADE_*` (rise, hold, fall) | original frames (`ScriptEngine::setFade`) | `Time.OriginalFrames(…)` on acceptance; the course runs on `Clock` |
| `CAMERA_BW_MODE_BEGIN/END` | original frames | the ramp counts one frame per original frame of the presentation clock |
| Zoom blur (`CAMERA_MOTION_BLUR_*`) | +5/−5 per original frame | one step per original frame |
| Camera movements (seconds) | seconds | `DurationTicks`: rounded to whole original frames, then into ticks; the remaining time for zoom/pitch/look divides by `Time.TicksPerSecond` |
| `SHOW_MILITARY_CAPTION`, speech subtitles | milliseconds, `MilitaryCaptionDelayMS`, `MilitaryCaptionSpeed` (frames) | `MilitaryCaptionState` computes in original frames and is updated once per original frame (`AdvanceText(FrameClock)`); the typing sound follows that |
| `DISPLAY_TEXT` (message lines) | age in frames, one update per frame | stamp `FrameOf(tick)`, one update per original frame |
| `DISPLAY_CINEMATIC_TEXT` | seconds | expiry in original frames (`(long)seconds * OriginalFrame.PerSecond`) |
| Placeholders for speech, sound, movie (3 s), radar event (8 s) | own durations | constants in original frames, compared in ticks of the world |
| Letterbox fade (`LETTER_BOX_FADE_TIME` 1000 ms) | wall time in the original | `LetterboxFadeFrames = OriginalFrame.PerSecond` original frames of the **presentation clock** (see below) |
| named counters | timer value of the VM in original frames (since Phase 23, §2) | `CounterForDisplay` passes the value through |
| `PresentationLatencyTicks` | original frames (unit of the option) | `LiveMission.ClientPresentationLatencyFrames = 60 * OriginalFrame.PerSecond`; the VM scales (comparison with the tick of the world) |

**Deviation from the brief (deliberate):** the brief of Phase 21 calls the letterbox fade "as wall time as in the original". It stays on the presentation clock: this way it stands still with a paused game, stays reproducible in tests and recordings and frame-identical at 30 Hz; in seconds it lasts equally long at every rate (measured 1.0333 / 1.0167 / 1.0083 s at 30 / 60 / 120 Hz up to full coverage). Whoever wants to attach it to wall time changes `LetterboxFadeLevel` – and with it the recordings of the mission intros.

**Time scale test** (`tests/CnCNew.Game.Tests/TimeScaleTests.cs`, synthetic mission each at 30, 60 and 120 Hz, tolerance one original frame): camera move of 5 s (until the director ends it and until `CAMERA_MOVEMENT_FINISHED` reaches the scripts), military subtitle (first character 0.7667 s, all nine characters 2.0667 s, gone after 7.5333 s – equal at all three rates), message line (9.2333 s), cinematic text (4.0000 s), speech placeholder (3 s), script fade (black after 1.0000 s, over after 3.0000 s), letterbox, greyscale ramp (1.0000 s), radar event (8 s).

**Not verified:** whether a mission intro looks and sounds right at 60 Hz. The test measures durations; no human has seen and heard it. `InputLockedTicks` and the line "input locked for N ticks" of the report still count ticks of the rate.

## 8. Deviations and approximations (complete)

1. **Build facility** (`ThingTemplate::isBuildFacility`): computed in the original from the prerequisites of other templates; here: structure with
   `ProductionUpdate` or `DozerAIUpdate`.
2. **Building count** (`Player::countBuildings`): living `STRUCTURE` objects (faction count: additionally `MP_COUNT_FOR_VICTORY`).
3. **First evaluation of delayed scripts:** the original draws `GameLogicRandomValue(0, 2 s)`; here an offset derived from the script name
   in the same range (consumes no random stream that a restored world would not repeat).
4. **Attacker history** reads owner/type after the world step; an attacker that died in the same tick is stored with an unknown
   owner (the original stores the player mask at the time of the damage).
5. **Commands as movement:** `*_HUNT`, `*_ATTACK_*`, `*_GUARD_*` and path following move the unit; shooting is taken over by the automatic
   combat of the session. `*_STOP`/`*_GUARD` (without target) halt. Formations and team speed (`asTeam`) have existed since Phase 13 (item 23); an attack movement
   with intermediate targets does not exist.
6. **Reinforcement** (`CREATE_REINFORCEMENT_TEAM`, `ScriptEngine.Reinforcements.cs`, Phase 13): without `teamTransport` the units of the team (`teamUnitMaxCount` each) appear at the origin (or at the destination) and
   walk to the destination; with `teamTransport` the transports are created at the origin, the team is loaded (`teamStartsFull`: already at spawn; otherwise it boards at the destination), `DeliverPayloadAIUpdate` flies
   the approach and drops (landing or parachute through `ParachuteContain`), and `teamTransportsExit` then sends the transport off the map. Paratroopers land on the ground below the
   drop point; the remainders of the original (formation offset on dropping, Chinook landing with ramp) are not reproduced.
7. **Shell modules** `CommandButtonHuntUpdate` (since Phase 12 triggered and executed by `TEAM_HUNT_WITH_COMMAND_BUTTON`, §5) and `LockWeaponCreate` (primary slot;
   other slots: unsupported configuration): the object can exist. `SpecialAbilityUpdate` has likewise been a shell since this phase:
   the module carries only the shared reload mechanism of every special power (`SpecialPowerModuleBase`); the actual approach/packing/
   preparation/ability-object staging of the original (`SpecialAbilityUpdate.cpp`: Ranger abilities, Colonel Burton, Jarmen Kell, the
   GLA Rebel's building capture/trap laying, the Hacker's money theft) is not simulated — `NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY*` trigger the power
   and start its reload (an observable, real effect: readiness, pauses, shared timers), the staged effect itself does not happen.
8. **Command buttons resolved globally:** `NAMED_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)` searches in the original in the `CommandSet` of the carrier, `TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)` in the global button registration (`TheControlBar->findCommandButton`); because button names in the
   original content are unique, `CommandCatalog.TryGetSpecialPowerAbility` resolves both variants the same way (a global mapping
   button name → `SpecialPowerTemplate`, only for `Command = SPECIAL_POWER*` buttons). The waypoint variants
   (`*_AT_WAYPOINT`, `*_USING_WAYPOINT_PATH`) have been executed since Phase 11 (§5, §8 item 13); the `TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_*`/`TEAM_PARTIAL_USE_COMMANDBUTTON` family has been executed since Phase 12 (§5).
9. **Mission without replay** (§3).
10. **Local side without `playerIsHuman`:** `MissionOptions.HumanSideName` overrides the default, see §7.
11. Map and campaign requirements that the original obtains from the game run (difficulty per player, camera/movie times,
   progress profile) come from `MissionOptions`/host.

12. **`getTeamNamed`:** the caller instance of a team (script/sequence context) takes precedence over other instances of the same prototype (`FindTeam`/`TeamsNamed` do that). Since Phase 13 the remaining rule
    of the original also applies: a **non-active** singleton team (`ScriptTeam.Created` = `isActive`) is `NULL` – `TEAM_DESTROYED`, member queries and actions do not see it; singletons are created in advance on loading without
    start actions and become visible only through activation (`TEAM_CREATED` event).
13. **Special powers:** `NAMED_FIRE_SPECIAL_POWER_AT_*` and the waypoint abilities go as an ordinary `UseSpecialPowerCommand` through the usual readiness, science and
    target checks (the original calls `doSpecialPowerAtLocation` on the module and does not check the readiness); `…_USING_WAYPOINT_PATH` uses only the nearest path point as the location.
    `SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST` searches for the location among the positions of hostile objects (sum of `BuildCost` in the effect radius, at least 50) instead of on the 10×10 grid of the original
    in a random direction and takes all enemies of the player instead of `getSkirmishEnemyPlayer`.
14. **Garrison:** `MayGarrison` maps `isKindOf(STRUCTURE) && player == 0 || player == own` with "last entered"; `…NEAREST_BUILDING` sorts by distance to the leader/to the unit
    (internet centers only for money hackers); `PLAYER_GARRISON_ALL_BUILDINGS` counts free slots across the order, the original checks only the state at the command.
15. **`BUILD_TEAM`** (Phase 13: `ScriptEngine.TeamQueues.cs`, after `AIPlayer::buildSpecificAITeam`/`checkQueuedTeams`/`checkReadyTeams`): every finished factory of the team owner that may produce the unit
    (`CanQueueProductionTemplate`), one command per tick per order, preferably an idle one; the team is in the **build queue** and is invisible (`Created` false). A built unit joins through
    `EntitySpawned` and walks to the home location (`aiFollowExitProductionPath`). When all types are finished, the team goes into the **ready queue**; if `teamInitialIdleFrames` expire (0: never) without
    the minimum being built or in production, the team is dissolved. As long as a member is idle, a team with `teamExecutesActionsOnCreate` executes the true actions of its
    `teamProductionCondition` script (`<This Team>` = the team). Still without build list assignment and priority/queue jumping.
16. **`RECRUIT_TEAM`:** like `Team::tryToRecruit` (priority, `teamIsAIRecruitable`, `TEAM_AVAILABLE_FOR_RECRUITMENT`, default team = `team<player name>` always recruitable); since Phase 13 the team begins in the
    ready queue: the members go to the home location, it becomes active when all are idle, after 60 seconds or – with start actions – as soon as a member is idle. "Build variants" and held
    units are not considered; without `teamHome` nothing is recruited.
17. **Recorded instead of executed:** `PLAYER_AFFECT_RECEIVING_EXPERIENCE`, `OBJECT_ALLOW_BONUSES` and since Phase 12 `TEAM_SET_ATTITUDE`, `TEAM_WANDER(_IN_PLACE)`, `PLAYER_SET_RANKLEVELLIMIT`,
    `PLAYER_SET_RANKLEVEL`, `PLAYER_ADD_RANKLEVEL`, `SCRIPTING_OVERRIDE_HULK_LIFETIME` (§5; read accessors `AttitudeOf`, `RankLevelOf`, `RankLimit`, `HulkLifetimeOverrideTicks`, `SkillPointsModifierOf`). Whoever evaluates them later
    introduces a consumer (AI stance, rank system, hulk list, experience gain); until then they count neither as executed nor as missing.
18. **Button readiness** (`SKIRMISH_COMMAND_BUTTON_READY_*`, `SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_*`): special power buttons through the power; since Phase 13 also upgrade buttons (`PLAYER_UPGRADE`/`OBJECT_UPGRADE`),
    whose readiness is reported by `IUpgradeModule.WouldUpgrade` of the carrier's modules (original: `isReady` → `canUpgrade`); other button types are not ready.
19. **Override relationships** (Phase 12): the tables (team→team, team→player, player→team) belong to the `ScriptEngine`, the world queries them through `RelationshipOverride`; the team assignment of an object is cached per tick
    and member count (`TeamOfEntity`, first team by id wins with multiple membership). The original attaches the override to the `Team` object, which owns an object only once.
20. **Command button family** (Phase 12): `…ON_NEAREST_*` takes as the centre the integer mean of the living members, checks the eight nearest candidates in turn until the world accepts the button for the first carrier,
    and then lets all carriers use the same button on the same target (the original chooses one target per carrier through `PartitionManager::getClosestObject` with its own filters); the hunt with a special power button searches
    every `max(ScanRate, 1 s)` ticks for the nearest enemy in `ScanRange` for an idle hunter (`CommandButtonHuntUpdate::scanClosestTarget`), the hunt with a weapon button is `TEAM_HUNT`; enter buttons (hijack,
    car bomb, sabotage) and all other command kinds are rejected as in the original and counted as a diagnostic.
21. **Transport** (Phase 12): `TEAM_LOAD_TRANSPORTS` distributes passengers by `TransportSlotCount` first-fit (largest first) instead of the packing optimisation of `doLoadAllTransports`; there is still no
    reinforcement transport (item 6): `CINE_*` aircraft of the campaign maps are not instantiable and the Chinook delivery of `CreateReinforcementTeam` is missing.
22. **Minor state** (Phase 12): `TEAM_PANIC` runs the path from the nearest waypoint on, without the panic locomotor set; `NAMED/TEAM_FACE_WAYPOINT` sets the facing immediately instead of with a turn rate;
    `NAMED/TEAM_SET_UNMANNED_STATUS` transfers the object to the neutral player (no `DISABLED_UNMANNED` state), `DELETE_ALL_UNMANNED` removes it afterwards; `NAMED/TEAM_SET_STEALTH_ENABLED` sets a switch on the
    `StealthUpdate` (`ScriptUnstealthed`, saved only when set); `TEAM_INCREASE/DECREASE_PRIORITY` changes the priority only for `RECRUIT_TEAM`, `BUILD_TEAM` does not read it (item 15);
    `TEAM_GUARD_IN_TUNNEL_NETWORK` goes to the nearest own tunnel and enters it (without a guard state in the network).
23. **Open VM semantics (as of Phase 14):** done are `getTeamNamed` for non-active singleton teams (item 12), team build with queue and start script (item 15), recruiting with gathering at the home location
    (item 16), the readiness of upgrade buttons (item 18), **formations** (`TEAM_FOLLOW_WAYPOINTS(_EXACT)` with `asTeam`: group offset, common team waypoint, speed of the slowest member via
    `AIUpdateInterface.SpeedCap`), the **path flight** of `NAMED_USE_COMMANDBUTTON_ABILITY_USING_WAYPOINT_PATH` for the particle beam (`ParticleUplinkCannonUpdate.PrepareScriptedPath`), `FIRE_WEAPON` with `WeaponSlot`
    (weapon slot model, `docs/SIMULATION-CORE.md` §30.6; the command button slot is locked) and "idle" including the state of the AI (`IsIdle`: also no own order of the script AI, aircraft through
    `JetAIUpdate.IsIdle` not yet). Deliberately open: a complete team state machine of the original (§5a), the path flight for missiles other than the beam and the nine missing actions (§5).
    No objective driver blocker depends on them (§9).

## 9. Headless check (measurement)

*The tick counts of this section up to and including "Closing session of Phase 17" are historical and measured at 30 Hz (60,000 ticks = 2,000 s, 15,000 ticks = 500 s, `audit --ticks 900` = 30 s). Since Phase 23 the tools run at 60 Hz by default and take budgets as game time (`--seconds`, `--minutes`; `--ticks` stays as a raw value of the rate of the run); the state of Phase 23 is at the end of the section.*

Tools: `CnCNew.CampaignTool audit|list|playthrough` (`docs/CAMPAIGN-AND-SAVEGAMES.md` §6) and `SimTool mission-run`.
Per mission: two fresh runs (hash chain identical), save/restore after half (identical), **end probe**
(`MissionProbe`): after 150 ticks all objects of the enemies (victory) or of the viewer and his allies (defeat)
are destroyed and the script result is awaited for 600 ticks; a result that the mission reaches itself before the probe does not count.

**`CampaignTool reachability`** (static, Phase 11): for every script that executes `VICTORY`/`QUICKVICTORY` or `DEFEAT`, the prerequisites of the chain are collected – conditions
(`FLAG`, `COUNTER`, `TIMER_EXPIRED` need a script that sets the value; all other condition types are leaves), enabling (`ENABLE_SCRIPT`/`CALL_SUBROUTINE`, active from the beginning or called by the engine as a
team event script) and the scripts that occur in it – and checked against `ScriptCoverage`. Result per end script: **clean** (all types on the best chain implemented), **restricted**
(fires, but a script of the chain executes an unimplemented action) or **blocked** (a condition of the chain is not implemented or reads something that nobody sets). This is an upper bound of what the VM
can reach, **not** a proof that a player can win: state conditions (team destroyed, object dead, area) count as satisfiable. `CampaignTool script --map … --name …` shows a script and all that name it.

**State after Phase 12 (2 October 2026, Release, 900 ticks per run, freshly measured; comparison with the state at the end of Phase 11):**

| Measure | End of Phase 11 | End of Phase 12 |
| --- | --- | --- |
| `ScriptCoverage` conditions executed / missing | 80 / 29 | 80 / 29 (unchanged) |
| `ScriptCoverage` actions executed / passed on / recorded / missing | 131 / 129 / 2 / 82 | 160 / 129 / 10 / 45 (Phase 13: **162 / 129 / 13 / 40**) |
| Scripts executed (24 maps) | 2,403 | 2,403 |
| Actions executed / passed on | 7,775 / 2,453 | 7,821 / 2,452 |
| Hits on missing action types (share of the executions) | 116 (1.1 %) | **27 (0.3 %)** |
| Hits on missing condition types | 6,411 | 6,411 |
| End probes by elimination: victory / defeat | 10/24 / 15/24 | **13/24** / 15/24 |
| **Objective driver on the script path: victory / defeat** (default 12,000 ticks; Phase 13: 60,000) | 10/24 / 16/24 | **22/24 / 24/24** (Phase 13: 24/24 / 24/24) |
| Static reachability of the victory scripts: clean / restricted / blocked | 21 / 3 / 0 | **24 / 0 / 0** |
| Static reachability of the defeat scripts: clean / restricted / blocked | 24 / 0 / 0 | 24 / 0 / 0 |
| Deterministic (two runs) / identical after save+load | 24/24 / 24/24 | 24/24 / 24/24 |
| Module catalog implemented / instantiability (retail objects) | 91/205 / 1,624 (76.8 %) | **98/205 / 1,680 (79.4 %)** |

(The three "restricted" victory chains of the end of Phase 11 (`MD_GLA04`, `MD_GLA05`, `MD_CHI05`) have become clean through the Phase 12 actions.
The 25th map of the list, `Maps/CampEADemo/CampEADemo.map`, is missing in the retail content.) Before/after per
single measurement, 3,000 ticks without continuation probe: see below.

**State after Phase 13 (3 October 2026, Release, 900 ticks, freshly measured):** 24/24 deterministic, 24/24 identical after save/load; 2,406 scripts, 7,828 actions executed, 2,452 passed on,
27 hits on missing action types (0.3 %), 6,411 on missing condition types; elimination end probes victory 13/24, defeat 15/24 (all unchanged compared to the end of Phase 12, the team queues
do not change the probe results); objective driver **victory 24/24, defeat 24/24** (60,000 ticks); static reachability victory and defeat clean 24/24.

**State after Phase 14 (3 October 2026, Release, freshly measured; all 183 simulation modules, 2,115 of 2,115 objects instantiable, weapon slot model):** `CampaignTool audit --ticks 900` (24 maps, run individually per campaign,
because the total run time on a loaded machine was over an hour): 24/24 deterministic, 24/24 identical after save/load, 6,007 scripts executed, **0 hits on missing action and condition types**;
elimination end probes victory 13/24, defeat 15/24 (unchanged compared to Phase 13). Objective driver (`drive`, 60,000 ticks) **victory 24/24, defeat 24/24**; static reachability victory and defeat clean 24/24. The way there had
two setbacks that were a consequence of the new modules and not of the driver: (1) `MD_GLA03` – the opponent now has flying helicopters, Raptor jets and battleships; they kill the particle cannon captured by the driver and the units
placed on the mainland before the intro (flag `INTRO_DONE`, tick 1,751) enables the `MAINLAND` scripts. The driver therefore implements a presence condition only when its script is live (`Leaf.Deferred = !ready`),
and repairs captured superweapon carriers and placed units every tick (`KeepCapturedCarriersAlive`, counted as a world write). (2) `MD_GLA04` (defeat) – the cheapest branch brings a unit into `TF_Area`, which through
`If Captured`/`If Got Enough Toxins` irreversibly starts the victory end; with the same leaf count the planner now prefers plans without placement into an area (`Leaf.Risky`). Tool additions: `drive --watch-flag NAME[,NAME]`
(flag/script state, `area:NAME` lists the objects in the area), `--watch` shows positions and the nearest enemies of injured objects, `coverage` prints the counters. The objective driver still replaces player actions with world interventions
– "yes" verifies the script chain, not the playability.

**Per map** (audit with 900 ticks; end probes: `@N` = result reached N ticks after the probe, "not reached" = end scripts present, end not reached; actions = executed / passed on / missing hits;
objects = created/placed; per campaign with the persona faction from `ChallengeMode.ini`, `CampaignTool audit` passes it through):

| Campaign | Mission | Map | Objects | Scripts executed | Actions | Victory probe | Defeat probe |
| --- | --- | --- | --- | --- | --- | --- | --- |
| TRAINING | Mission01 | Training01 | 1448/2040 | 43 | 43 / 57 / 4 | @360 | not reached |
| CHALLENGE_0 | Mission01 | GC_ChemGeneral | 682/983 | 59 | 753 / 21 / 0 | @210 | @90 |
| CHALLENGE_0 | Mission02 | GC_NukeGeneral | 1642/2300 | 71 | 553 / 33 / 0 | @180 | @90 |
| CHALLENGE_0 | Mission03 | GC_SuperWeaponsGeneral | 584/681 | 75 | 547 / 21 / 0 | @180 | @90 |
| CHALLENGE_0 | Mission04 | GC_TankGeneral | 1563/1573 | 932 | 505 / 926 / 0 | @180 | @90 |
| CHALLENGE_0 | Mission05 | GC_Stealth | 770/961 | 81 | 486 / 20 / 0 | @180 | @90 |
| CHALLENGE_0 | Mission06 | GC_LaserGeneral | 1837/4556 | 94 | 516 / 44 / 0 | @180 | @90 |
| CHALLENGE_0 | Mission07 | GC_ChinaBoss | 1245/1551 | 63 | 661 / 18 / 0 | @300 | not reached |
| CHALLENGE_1 | Mission06 | GC_AirGeneral | 994/1077 | 51 | 563 / 29 / 0 | @210 | @90 |
| MD_USA_1_DEMO | Mission01 | MD_USA01 | 1703/2846 | 38 | 77 / 102 / 1 | not reached | @151 |
| MD_USA_2_DEMO | Mission01 | MD_USA02 | 1221/3158 | 59 | 110 / 134 / 0 | not reached | not reached |
| MD_GLA_3_DEMO | Mission01 | MD_GLA03 | 1193/1753 | 61 | 383 / 78 / 0 | not reached | @182 |
| USA | Mission03 | MD_USA03 | 1628/2153 | 42 | 103 / 70 / 0 | not reached | @181 |
| USA | Mission04 | MD_USA04 | 1195/2027 | 85 | 320 / 49 / 6 | @243 | not reached |
| USA | Mission05 | MD_USA05 | 1689/2757 | 74 | 222 / 108 / 0 | not reached | not reached |
| GLA | Mission01 | MD_GLA01 | 1315/1780 | 110 | 239 / 172 / 2 | not reached | @181 |
| GLA | Mission02 | MD_GLA02 | 661/1923 | 55 | 263 / 49 / 4 | @180 | not reached |
| GLA | Mission04 | MD_GLA04 | 2503/4271 | 88 | 294 / 97 / 4 | not reached | not reached |
| GLA | Mission05 | MD_GLA05 | 2122/5813 | 78 | 340 / 60 / 0 | not reached | not reached |
| China | Mission01 | MD_CHI01 | 1156/3168 | 17 | 53 / 42 / 0 | @241 | @181 |
| China | Mission02 | MD_CHI02 | 1646/2321 | 85 | 260 / 161 / 4 | @241 | @241 |
| China | Mission03 | MD_CHI03 | 1475/2768 | 44 | 286 / 25 / 0 | not reached | not reached |
| China | Mission04 | MD_CHI04 | 1624/2038 | 22 | 20 / 37 / 0 | not reached | @213 |
| China | Mission05 | MD_CHI05 | 1867/2749 | 79 | 231 / 99 / 2 | not reached | @181 |

**Honest assessment:** every map loads, runs deterministically, continues identically after save/load, the end scripts of all 24 maps are statically reachable with nothing but implemented types, and the
**objective driver reaches victory and defeat on all 24 maps on its own script path** (table further below). The elimination end probes (annihilation only) are still weaker (victory 13/24),
because many maps require objectives (e.g. `MD_USA03`: `END_Final_`/`Win` depend on `TIMER_EXPIRED`, which objective counters set), which an annihilation does not work through – the driver works through them. The two victories that were missing in
Phase 12 (`MD_GLA03`: `ParticleUplinkCannonUpdate`, `MD_CHI04`: wave mission) have been reached since Phase 13. **This verifies the script chain, not the playability:** the driver replaces player actions with world interventions.

**Blocker list (state at the end of Phase 12; static scripts/maps according to `CampaignTool reachability`).** Actions still missing (all in the chains without effect on the 24 end paths):
`SKIRMISH_MOVE_TO_APPROACH_PATH` 10/3, `SKIRMISH_FOLLOW_APPROACH_PATH` 7/3, `AI_PLAYER_BUILD_SUPPLY_CENTER` 5/1, `SET_TRAIN_HELD` 5/1, `TEAM_CAPTURE_NEAREST_UNOWNED_FACTION_UNIT` 5/4,
`TEAM_GUARD_SUPPLY_CENTER` 5/1, `PLAYER_SELECT_SKILLSET` 4/2, `RESUME_SUPPLY_TRUCKING` 4/1, `NAMED_SET_TOPPLE_DIRECTION` 3/3, `PLAYER_SELL_EVERYTHING` 3/2, `SET_BASE_CONSTRUCTION_SPEED` 3/1,
`TEAM_SET_BOOBYTRAPPED` 3/1, `NAMED_SET_BOOBYTRAPPED` 2/1, `NAMED_SET_EVAC_LEFT_OR_RIGHT` 2/1, `NAMED_SET_REPULSOR` 2/2 and five single cases (`LOCALDEFEAT`, `NAMED_SET_STOPPING_DISTANCE`, `TEAM_FACE_NAMED`,
`TECHTREE_MODIFY_BUILDABILITY_OBJECT`, `UNIT_MOVE_TOWARDS_NEAREST_OBJECT_TYPE`); at runtime (900 ticks) the hits are `TEAM_SET_BOOBYTRAPPED` 6, `NAMED_SET_TOPPLE_DIRECTION` 6, `RESUME_SUPPLY_TRUCKING` 4,
`PLAYER_SELECT_SKILLSET` 4. Missing conditions at runtime: `SUPPLY_SOURCE_SAFE` 3,600, `PLAYER_BUILT_UPGRADE` 900, `PLAYER_COMPLETED_SPECIAL_POWER` 900, `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE` 900,
`PLAYER_POWER_COMPARE_PERCENT` 81, `TEAM_STATE_IS` 30 (one map each; unchanged). Before Phase 12 the groups `TEAM_HUNT_WITH_COMMAND_BUTTON` 53/12, `TEAM_SET_ATTITUDE` 42/11,
`TEAM_LOAD_TRANSPORTS` 33/8, `TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_OBJECTTYPE` 30/5, `TEAM_SET_OVERRIDE_RELATION_TO_PLAYER` 27/5, `PLAYER_SET_OVERRIDE_RELATION_TO_TEAM` 19/6, `TEAM_PANIC` 18/4 were at the top; they are
executed or recorded (§5). Unresolvable references (as of Phase 12; the `CINE_U04_AmericaJetCargoPlane_*` transports have been instantiable since Phase 13, `CINE_U06_CruiseMissileFLY` stays blocked on `JetAIUpdate`): `AmericaJetRaptor`,
`ChinaVehicleHelix`, `FirestormSmall` among others; attack priority sets from the unloaded skirmish script library `SkirmishScripts.scb`; `BUILD_TEAM: no factory for …`; rejected upgrade buttons.
Map objects with a missing template (roads, sidewalks, trees from map overlays, `GLADemoTrap`/`GLAStingerSite` among others) are still skipped and counted (several hundred per map).
**Reinforcement with transport** (handoff item 3, Phase 13): `CREATE_REINFORCEMENT_TEAM` with `teamTransport`/`teamStartsFull` creates the transports at the start point (`ScriptEngine.Reinforcements.cs`),
loads the team (`LoadIntoTeamTransports`) and sends them through `DeliverPayloadAIUpdate` (approach, drop, re-approach, return flight off the map; `ParachuteContain` for parachute droppers, the crew
dies with a shot-down aircraft); `JetSlowDeathBehavior` and `CountermeasuresBehavior` (decoys divert guided weapons) belong to it (`SIMULATION-CORE.md` §28). Formations
(`*_FOLLOW_WAYPOINTS` with `asTeam`) and the path flight of the particle beam have been implemented since the continuation of Phase 13 (item 23).

**Objective driver (`CampaignTool drive`, `MissionObjectiveDriver` + `ObjectiveDriver`/`ObjectiveDriver.Leaves`; Phase 11 first draft, Phase 12 goal-directed; default since Phase 13 60,000 ticks per run, before 12,000):** the driver
reaches the outcome of a mission on its **own script path**, without annihilating a side: it counts only when a `VICTORY`/`DEFEAT` action of a mission script itself fires. It is
test/tool code (like `MissionProbe` it writes directly into the world, commands run through the session counter) and deterministic (test `ScriptObjectiveDriverPlansBackwards`,
`…MovesCountersTheRightWay`, `ScriptExplainLeavesTheEngineUntouched`).

Mode of operation (backwards from the end script, re-planned every 15 ticks, a pure function value of the engine state – `ExplainScript` has been state-neutral since Phase 12 and restores area state, counters, triggered
powers, missing conditions and diagnostics after the evaluation):

1. **Plan per end script.** A script fires when it is active (and, as a subroutine, called) and an OR branch of its conditions holds. A timer/counter/flag condition needs a script that
   sets the value (planned recursively, cycles block only this path); everything else is a **leaf** (world state that the driver can establish) or a **blocker** (no intervention known). Alternatives
   (several enablers, setters, OR branches) are ordered by blockers, then leaves, the cheapest path wins; team event scripts count as called as soon as any team that names them has a
   living member.
2. **Opposite direction.** Scripts that reach the other outcome (`_tainted`: scripts with the opposite end action and all whose targets lead *exclusively* there – distributors like
   `Win_Loss_Enable - X` enable both ends and therefore do not count) are not fulfilled; a leaf that would annihilate the side that is supposed to win is held back until for a while
   nothing else makes progress. **Defence leaves** (only in the victory case): units of hostile sides that stand where the mission loses on arrival (escape area, base) are removed – the substitute for
   the defending player.
3. **Counters direction-aware:** a counter is set only in the direction that fulfils the condition (`>`/`<`/`=`), not set blindly.
4. **Standstill.** Progress is what changes the script fingerprint signature (which scripts/groups are on, flags, counters, which timers have been started – not the timer value); if it stands
   still and no intervention takes hold, the run has stalled. Then the **union** of all feasible alternatives is fulfilled instead of the cheapest path (timers and waves run until they expire).
5. **Leaves (`ObjectiveDriver.Leaves`):** destroy/own a named object, destroy/annihilate a team, defeat a player, pull the number of objects of a type/a kind (possibly in an area) to the nearest value
   that fulfils the comparison (destroy surplus ones or put an existing object of the player into the area, otherwise create a new one), team into/out of an area (march command, after two unsuccessful attempts
   put down), money, science, change of owner, object state, health. Edge conditions (enter/leave area, object type lost) are held back until their script is live. Everything else
   is listed as a blocker in `Unmet`, never silently skipped.
6. **Diagnosis on failure:** `Unmet` names per end script what the plan still saw open, the trace of the scripts passed through (state, subroutine/delay, conditions) and the living objects of both
   sides at the end (`Survivors`: template counts, most frequent first – what a "destroy everything" leaf could not remove). `CampaignTool drive --explain --trace` prints that; `template`, `objects` and
   `transports` show map content, object lists and transports.

**Measurement (final, `CampaignTool drive`, Release, 60,000 ticks, persona as in the game; freshly measured on 3 October 2026):** **victory on the script path 24/24, defeat 24/24** (state at the end of Phase 12: victory 22/24;
end of Phase 11: victory 10/24, defeat 16/24):

| Campaign | Mission | Map | Victory on the script path | Defeat on the script path |
| --- | --- | --- | --- | --- |
| TRAINING | Mission01 | Training01 | yes – `Player Wins` @420 (1 intervention) | yes – `Player Loses` @2716 (2 interventions) |
| CHALLENGE_0 | Mission01 | GC_ChemGeneral | yes – `END_Final_USA` @270 (1 intervention) | yes – `Lose_END_Final_USA` @150 (1 intervention) |
| CHALLENGE_0 | Mission02 | GC_NukeGeneral | yes – `END_Final_USA` @240 (1 intervention) | yes – `Lose_END_Final_USA` @150 (1 intervention) |
| CHALLENGE_0 | Mission03 | GC_SuperWeaponsGeneral | yes – `END_Final_USA` @240 (1 intervention) | yes – `Lose_END_Final_USA` @150 (1 intervention) |
| CHALLENGE_0 | Mission04 | GC_TankGeneral | yes – `END_Final_USA` @240 (1 intervention) | yes – `Lose_END_Final_USA` @150 (1 intervention) |
| CHALLENGE_0 | Mission05 | GC_Stealth | yes – `END_Final_USA` @240 (2 interventions) | yes – `Lose_END_Final_USA` @150 (1 intervention) |
| CHALLENGE_0 | Mission06 | GC_LaserGeneral | yes – `END_Final_USA` @240 (1 intervention) | yes – `Lose_END_Final_USA` @150 (1 intervention) |
| CHALLENGE_0 | Mission07 | GC_ChinaBoss | yes – `End the mission` @360 (4 interventions) | yes – `Lose_END_Final_USA` @2431 (2 interventions) |
| CHALLENGE_1 | Mission06 | GC_AirGeneral | yes – `END_Final_GLA` @270 (1 intervention) | yes – `Lose_END_Final_GLA` @150 (1 intervention) |
| MD_USA_1_DEMO | Mission01 | MD_USA01 | yes – `END_Final_USA` @785 (1 intervention) | yes – `Lose_END_Final_USA` @211 (1 intervention) |
| MD_USA_2_DEMO | Mission01 | MD_USA02 | yes – `END_Final_` @2718 (4 interventions) | yes – `Lose_END_Final_` @12361 (30 interventions) |
| MD_GLA_3_DEMO | Mission01 | MD_GLA03 | yes – `CINE_ENDVictory` @3613 (12 interventions) | yes – `Lose_END_Final_` @242 (1 intervention) |
| USA | Mission03 | MD_USA03 | yes – `CINE_AVVictory` @1509 (3 interventions) | yes – `Lose_END_Final_` @241 (1 intervention) |
| USA | Mission04 | MD_USA04 | yes – `END_Final_USA` @303 (9 interventions) | yes – `Lose_END_Final_USA` @617 (20 interventions) |
| USA | Mission05 | MD_USA05 | yes – `END_Fade 04` @1443 (4 interventions) | yes – `Lose_END_Final_` @1280 (6 interventions) |
| GLA | Mission01 | MD_GLA01 | yes – `Cheer1Done` @526 (3 interventions) | yes – `Lose_END_Final_` @241 (1 intervention) |
| GLA | Mission02 | MD_GLA02 | yes – `END_Final_` @240 (1 intervention) | yes – `Lose_END_Final_` @572 (5 interventions) |
| GLA | Mission04 | MD_GLA04 | yes – `CINE_END` @1022 (1 intervention) | yes – `Lose_END_Final_` @241 (1 intervention) |
| GLA | Mission05 | MD_GLA05 | yes – `CINE_END04END` @1394 (1 intervention) | yes – `Lose_END_Final_` @1036 (1 intervention) |
| China | Mission01 | MD_CHI01 | yes – `END_Final_` @301 (1 intervention) | yes – `Lose_END_Final_` @241 (1 intervention) |
| China | Mission02 | MD_CHI02 | yes – `END_Final_` @301 (4 interventions) | yes – `Lose_END_Final_` @301 (1 intervention) |
| China | Mission03 | MD_CHI03 | yes – `Cheer1Done` @1722 (6 interventions) | yes – `Lose_END_Final_` @4471 (1 intervention) |
| China | Mission04 | MD_CHI04 | yes – `END_Final_` @48348 (13 interventions) | yes – `Lose_END_Final_` @661 (3 interventions) |
| China | Mission05 | MD_CHI05 | yes – `CINE_ENDVictory` @2662 (20 interventions) | yes – `Lose_END_Final_` @241 (1 intervention) |

**How the two earlier "no" were closed (Phase 13):**

- **`MD_GLA03` (victory):** the victory script waits for the particle uplink beam to hit the immortal carrier. `ParticleUplinkCannonUpdate` (`SIMULATION-CORE.md` §28) provides beam times, sweep and damage pulses,
  the driver catches the carrier (`SKIRMISH_SPECIAL_POWER_READY` leaf: seize the power at the carrier, charge and trigger it) – 12 interventions, victory `CINE_ENDVictory` @3,613.
- **`MD_CHI04` (victory):** the wave mission needs `UndeadBody` (two lives), `BattleBusSlowDeathBehavior`, `SubObjectsUpgrade` and `AnimationSteeringUpdate` on the wave vehicles; with them the
  wave chains close, `END_Final_` fires at tick 48,348 (13 interventions). Therefore the default of `drive` is now 60,000 ticks (before 12,000).

**What "yes" does not mean:** the driver replaces player actions with world interventions (placed units, removed opponents in defence areas, set owners, signals for UI flags, e.g. in `Training01`);
"yes" verifies that the *script chain* of the mission runs to the outcome and the VM masters all types on this path – not that a player can actually win the mission with the implemented
units. Long runs: `MD_USA02` loses only at tick 11,476 (30 interventions), `MD_CHI03` at 4,471.

**Longer measurement (`CampaignTool audit --ticks 3000 --no-continuation`, 24 maps, Release, 2 October 2026):** 24/24 deterministic; 7,119 scripts executed, 13,021 actions executed, 7,152 passed on, **35 hits
on missing action types (0.3 %)**, 21,181 on missing condition types (of which `SUPPLY_SOURCE_SAFE` 12,000 on one map, `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE`, `PLAYER_COMPLETED_SPECIAL_POWER` and
`PLAYER_BUILT_UPGRADE` 3,000 each); elimination end probes victory 13/24, defeat 15/24. Before (end of Phase 11, before `AI_PLAYER_BUILD_UPGRADE`): 7,115 scripts, 12,931 actions, 7,163 passed on, 167 hits on missing
action types (0.8 %), 21,181 on missing condition types. Most frequent missing actions now: `TEAM_SET_BOOBYTRAPPED` 6, `NAMED_SET_TOPPLE_DIRECTION` 6, `TECHTREE_MODIFY_BUILDABILITY_OBJECT` 5, `RESUME_SUPPLY_TRUCKING` 4,
`PLAYER_SELECT_SKILLSET` 4 (before `AI_PLAYER_BUILD_UPGRADE` 25/9, `NAMED_SET_HELD` 13/4, `NAMED_SET_UNMANNED_STATUS` 10/1, `TEAM_SET_OVERRIDE_RELATION_TO_PLAYER` 10/1, `TEAM_SET_OVERRIDE_RELATION_TO_TEAM` 9/5, `TEAM_WANDER` 9/2,
`PLAYER_SET_RANKLEVELLIMIT` 8/8 – all implemented or recorded since).

**Repetition after Phase 13 (3 October 2026, `audit --ticks 3000 --no-continuation`, per campaign in parallel, 24 maps):** 24/24 deterministic and identical after save/restore; 7,124 scripts, 13,016 actions
executed, 7,155 passed on, 35 hits on missing action types (0.3 %), 21,181 on missing condition types; elimination end probes victory 13/24, defeat 15/24 – practically unchanged compared to Phase 12; so the
team queues and the `getTeamNamed` rule do not shift the maps.

### Commands-only mode of the objective driver (Phase 13: handoff item 1, "playability instead of world interventions")

`CampaignTool drive --commands-only [--outcome victory|defeat]` (`MissionObjectiveDriver.Run(…, commandsOnly: true)`, `ObjectiveDriver.Commands.cs`) writes **nothing directly into the world**: every leaf carries a kind
(`LeafKind`: world write, command, UI signal), world writes (killing, placing, money, science, change of owner, health, creating, revealing, the defence leaves) are **declined, counted and
reported** (`ObjectiveDriveResult.Declined`, line `declined world write` in `Unmet`); the result report now separates commands (`Commands`: move commands, production commands, the commands of the agent) and
world writes (`WorldWrites`) – also in normal mode (`drive` shows "N interventions: C commands, W world writes").

What a player does is taken over by: (1) "destroy X" leaves send the armed, movable units of the viewer with an attack move to the nearest victim and give it to them as an attack command (since Phase 17: `Assault`/`DriveAssaults`; before, move commands and the combat loop of the session, `DriveCombat`,
shoots as soon as targets are in range); (2) "number of type X" leaves start production at an own producer (`QueueProductionCommand` after `CanQueueProductionTemplate`), units outside the area
go there on foot (no teleport); (3) **economy and army are played by the skirmish agent**: `SkirmishAi` (`CnCNew.Simulation/Skirmish`, "plays only through typed commands") controls the viewer through the
command counter of the session (`command with { Sequence, Tick }`, so that scripts, driver and agent share one counter); it builds base and economy, produces, gathers an army and attacks in waves, with the positions
of the enemy structures from the map start as hints (what a player sees on the minimap). The driver holds back its attack commands until the agent is in the attack (units arriving singly die singly).
Without a faction with a starting building (e.g. `Training01`) there is no agent.

**Measurement (3 October 2026, Release, `--commands-only --outcome victory --ticks 15000`, 24 maps):** **0 world writes on all maps, victory through the own scripts on 1 of 24** – and there (`MD_GLA04`,
`CINE_END` @1022) the script ends without any action of the player; on the remaining 23 maps not reached:

| Campaign | Mission | Map | Victory with commands only (15,000 ticks) | Commands (driver + agent) |
| --- | --- | --- | --- | --- |
| CHALLENGE_1 | Mission06 | GC_AirGeneral | not reached | 4529 |
| CHALLENGE_0 | Mission01 | GC_ChemGeneral | not reached | 619 |
| CHALLENGE_0 | Mission07 | GC_ChinaBoss | not reached | 7899 |
| CHALLENGE_0 | Mission06 | GC_LaserGeneral | not reached | 1070 |
| CHALLENGE_0 | Mission02 | GC_NukeGeneral | not reached | 3417 |
| CHALLENGE_0 | Mission05 | GC_Stealth | not reached | 3534 |
| CHALLENGE_0 | Mission03 | GC_SuperWeaponsGeneral | not reached | 6325 |
| CHALLENGE_0 | Mission04 | GC_TankGeneral | not reached | 3469 |
| China | Mission01 | MD_CHI01 | not reached | 3003 |
| China | Mission02 | MD_CHI02 | not reached | 7292 |
| China | Mission03 | MD_CHI03 | not reached | 2871 |
| China | Mission04 | MD_CHI04 | not reached | 353 |
| China | Mission05 | MD_CHI05 | not reached | 8389 |
| GLA | Mission01 | MD_GLA01 | not reached | 35 |
| GLA | Mission02 | MD_GLA02 | not reached | 376 |
| MD_GLA_3_DEMO | Mission01 | MD_GLA03 | not reached | 3860 |
| GLA | Mission04 | MD_GLA04 | yes – script ends by itself @1022 | 104 |
| GLA | Mission05 | MD_GLA05 | not reached | 929 |
| MD_USA_1_DEMO | Mission01 | MD_USA01 | not reached | 813 |
| MD_USA_2_DEMO | Mission01 | MD_USA02 | not reached | 2191 |
| USA | Mission03 | MD_USA03 | not reached | 12 |
| USA | Mission04 | MD_USA04 | not reached | 13330 |
| USA | Mission05 | MD_USA05 | not reached | 557 |
| TRAINING | Mission01 | Training01 | not reached | 0 |

**Why it fails (honest, not fixed):** (a) The agent is `SkirmishAi`, not a campaign player: on `GC_ChemGeneral` it builds a base and produces 97 units, attacks with **one** wave, loses it against the prefabricated
GLA base (tunnel networks, bunkers, quad cannons) and has no army any more after that (`agent: … 1 attack waves … army now 0`). (b) Many objectives are world states that only a write reaches: money, science,
owner, UI flags, reveal – they are listed as `declined world write` in `Unmet`. (c) Campaign maps require procedures (escorting convoys, capturing buildings, script timings) that neither the agent nor the
leaves know as commands. (d) 15,000 ticks (8.3 game minutes) are tight for base construction plus attack; longer runs cost minutes per map (the agent plans every few ticks). **A "player agent" that really wins the
campaign is the subject of the next handoff** (`docs/handoffs/HANDOFF-PHASE14.md`): attack waves with retreat and reinforcement, capture (`CaptureCommand`), special powers, an AI that reads mission objectives.
Normal mode stays at victory and defeat 24/24 with world interventions (`Interventions` per map: table above); the commands-only mode is the yardstick by which the distance to playability can be measured
(tests `ScriptCommandsOnlyDriverUsesOrdersNotWrites`, `…DeclinesWhatOnlyAWriteCanDo`: on a synthetic map it reaches victory with commands only, and money is declined and named).

**State after Phase 14 (4 October 2026, Release, run individually per campaign with 15,000 ticks, `drive --commands-only --outcome victory`):** unchanged **1 of 24 maps** (`MD_GLA04`, script ends by itself @1022, 0 world writes, 86 commands); the
other 23 maps "not reached", among them all challenge maps (7 + `GC_AirGeneral`), `Training01`, `MD_CHI01–05`, `MD_GLA01/02/03/05`, `MD_USA01–05`. The new modules (aircraft, crates, structures, weapon slots) have thus not
reduced the distance to playability: the causes (a)–(d) above apply unchanged; whether the opponent, who now actually flies and shoots (Raptor, Comanche, battleships), hits the agent earlier was not investigated. The USA maps
need over an hour per run in commands-only mode too (14,864 commands on `MD_USA04`). The yardstick stays the subject of the next handoff (`docs/handoffs/HANDOFF-PHASE15.md`, item 1).

**State on the evening of 4 October 2026 (Release, per map individually, `drive --commands-only --outcome victory --ticks 15000`, 2,400 s time limit each; `SkirmishAi` with supply center at the warehouse, `docs/SIMULATION-CORE.md` §32):**
unchanged **1 map** (`MD_GLA04`, @1022, 92 commands, 0 world writes); 22 maps "not reached" with 0 world writes (`Training01`: 0 commands – the agent finds nothing to do there), `MD_USA01` aborted after 2,400 s
(no result). The time limit of 15,000 ticks is **not** the reason: four maps (`GC_ChemGeneral`, `GC_NukeGeneral`, `GC_TankGeneral`, `MD_CHI01`) ran with `--ticks 60000` (and the AI attacking in
groups) likewise without victory, with 1,945 to 11,243 commands. So the AI corrections, which decide 12 instead of 5 of 16 skirmish runs, do not change this number: the agent builds and attacks, but it
fulfils no mission objectives – that stays the core of Phase 17.

Newly measured on the same day (state `096290b`, before the AI corrections; the driver and the audit do not use `SkirmishAi`): `drive` (60,000 ticks) **victory 24/24, defeat 24/24**; `audit --ticks 900` 24/24
deterministic, 24/24 identical after save/load, 0 hits on missing types, elimination end probes **victory 13/24, defeat 15/24** (run individually per map, 24 maps including `Training01`).
### Phase 17: the first unfulfilled condition per map as a command (5 October 2026, measured)

Starting question of the handoff: not more time, but expressing per map the first unfulfilled condition of the victory chain (`drive --commands-only --outcome victory --explain`) as a command. The before-measurement
(state `4eea97b`, 15,000 ticks, per map individually, 8 runs in parallel): **1 of 24** (`MD_GLA04`, the script ends by itself), `MD_USA01` aborted after 1,800 s. On 9 of the 24 maps the first open
condition was a **declined world write**, on the others an attack leaf that the agent did not win.

**What the commands-only mode now does instead of a world write (`ObjectiveDriver.Commands.cs`, `ObjectiveDriver.Leaves.cs`):**

| Condition of the chain | before (commands-only) | now |
| --- | --- | --- |
| Number of a type/a kind of an **enemy** too high (`PLAYER_HAS_OBJECT_COMPARISON`, `…_IN_TRIGGER_AREA`), `PLAYER_HAS_N_OR_FEWER_(FACTION_)BUILDINGS`, `PLAYER_LOST_OBJECT_TYPE`, `SKIRMISH_PLAYER_IS_OUTSIDE_AREA`, team events "destroyed"/"member destroyed" | declined ("destroy the surplus …") | **attack**: the victims become the target of the army; every armed unit receives its nearest victim as an attack command (`FireWeaponCommand`, as soon as in range – units of a human do not choose buildings and neutral objects themselves) and, if the agent does not lead it, an `AttackMoveCommand` there |
| `NAMED_OWNED_BY_PLAYER` for the viewer | declined ("hand … to …") | **`CaptureCommand`** of a unit that carries the capture ability (`SpecialAbilityUpdate` with `SPECIAL_*_CAPTURE_BUILDING`); if the ability is paused, the triggering upgrade is bought (`UnpauseSpecialPowerUpgrade.TriggeredBy`, `PurchaseUpgradeCommand`; the agent holds back the price, `SkirmishAi.ObjectiveReserve`), if such a unit is missing, the cheapest one is produced; a building not yet seen gets a scout |
| `SKIRMISH_SPECIAL_POWER_READY` without an own carrier | declined ("capture a carrier …") | capture of the carrier as above; an own carrier still charging is waited for (no instant charging) |
| `SKIRMISH_PLAYER_HAS_UNITS_IN_AREA` | declined ("bring a unit …") | march command of an own unit into the area |
| `NAMED_DISCOVERED`, `TEAM_DISCOVERED`, `SKIRMISH_PLAYER_HAS_DISCOVERED_PLAYER` (viewer sees) | declined (reveal map) | the nearest own unit goes there |
| `PLAYER_ACQUIRED_SCIENCE` (viewer) | declined | `PurchaseScienceCommand` |
| `NAMED_/TEAM_ATTACKED_BY_PLAYER`, `SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER` (viewer attacks) | declined (damage point) | real attack |
| `UNIT_HEALTH` of a hostile object downward | declined | attack |
| Flag that no script sets (UI button) | declined | **UI signal** (`LeafKind.Signal`): the player presses the button; no world write |

Two tightenings belong to it, so that "command" stays honest: a commands-only run commands **only units of the viewer** (a march command to the unit of another side is declined and reported as
`order '…' of another side` – before, it was issued silently), and it never attacks its own side. The reduced instant capture of the simulation (every unit that stands next to a building) is not used
by the driver on real data; only in the synthetic fixture, which knows no unit with an ability. Still declined and named: money (`PLAYER_HAS_CREDITS`), foreign ownership, health upward, creating
without a producer.

**Agent (`SkirmishAi` as economy and army leader of the viewer):** the victims of the attack leaves are its attack target (`Objectives`; one army, one target, instead of two command givers on the same
units); a side without production buildings attacks with everything it has as soon as a target is named (`CommitEverything` – otherwise a fixed mission army waited below the attack threshold forever);
until the first supply center stands, nothing else is begun (`EconomyFirst`). Two bugs of the AI that showed up only on mission maps are fixed: warehouses of a non-hostile side count as
warehouses (mission maps give the `SupplyDock`s to a civilian player, the agent knew "0 warehouses" and sent no gatherer), and a supply center that finds no place next to its source three times
is built at the base (on `GC_ChemGeneral` none was ever built, the starting money went into power plants and barracks). Neither changes the 16 AI-versus-AI runs (hashes identical, `SIMULATION-CORE.md` §33).

**Per map: first unfulfilled condition before, what is tried now, result (merged state `2a66661` with target acquisition of the units in the simulation; 0 world writes on all maps):**

| Map | first unfulfilled condition before | now | 15,000 ticks | 60,000 ticks |
| --- | --- | --- | --- | --- |
| Training01 | attack on `SCUD Storm Target` (no army, no agent: the side owns nothing initially) | unchanged, 0 commands | not reached | not measured |
| GC_ChemGeneral | destroy all build facilities of `SkirmishGLA`; agent without supply center | attack target of the agent, economy stands, army loses | not reached | not reached |
| GC_NukeGeneral | all build facilities of `AIOpponent` | as above | not reached | not reached |
| GC_SuperWeaponsGeneral | all build facilities of `AIOpponent` | as above | not reached | not reached |
| GC_TankGeneral | all build facilities of `AIOpponent` | as above (infantry against Gatling cannons, warehouse at the start exhausted) | not reached | not reached |
| GC_Stealth | everything of `AIOpponent`, `AIOpponent2` | as above | not reached | not reached |
| GC_LaserGeneral | everything of `PlyrAmericaLaserGeneral` | as above | not reached | not reached |
| GC_ChinaBoss | **declined:** surplus of `Boss_Airfield/_Barracks/_WarFactory/_CommandCenter/_VehicleDozer` | attack on these buildings | not reached | not reached |
| GC_AirGeneral | everything of `AirGen_Grainger` | attack target of the agent | not reached | not reached |
| MD_USA01 | (before-run aborted) | attack: `GLA_Forces_LF` must keep no building in `Launch_Facility_Area` | not reached | not measured |
| MD_USA02 | **declined:** surplus `GLA_Buildings` of `GLA_attackers` | attack; `Truck01` belongs to another side (command declined) | not reached | not measured |
| MD_USA03 | `Burton` in `End Game Mountain Area`, three warehouse buildings | Burton does not (yet) belong to the viewer: command declined | not reached | not measured |
| MD_USA04 | attack on six named GLA buildings | attack, army does not arrive | not reached | run aborted after 3,300 s |
| MD_USA05 | **declined:** hand over four toxin buildings | capture (buy upgrade, send Rangers) | not reached | not reached |
| MD_GLA01 | 4 `ShiekLimo` of `PlyrGLA` in `AirfieldArea` | march commands (one limousine still lives) | not reached | not reached |
| MD_GLA02 | **declined:** hand over `Primary enemy CC` | capture; no unit with the ability and no producer (declined, named) | not reached | not measured |
| MD_GLA03 | **declined:** unit in `MAINLAND`, capture carrier of the particle cannon | march, scout, capture | not reached | not reached |
| MD_GLA04 | – (script ends by itself) | – | **yes** @1022 | – |
| MD_GLA05 | everything of `AIOpponent` | attack target of the agent | not reached | not reached |
| MD_CHI01 | everything of `GLA_Enemy` | attack target of the agent | not reached | **yes** – `END_Final_` @18,492 |
| MD_CHI02 | **declined:** surplus `Factory List` of `BG1`–`BG4` | attack | not reached | **yes** – `END_Final_` @29,605 |
| MD_CHI03 | attack on `STATUE-00…` | attack | not reached | not reached |
| MD_CHI04 | **declined:** destroy member of `Wave 6a Team 5` | attack as soon as the wave exists (defence mission: there is nothing to attack yet) | not reached | not reached |
| MD_CHI05 | **declined:** surplus of the GLA building types of `ENEMY_GLA` | attack | not reached | not reached |

**Result:** at 15,000 ticks unchanged **1 of 24**; with 60,000 ticks (18 maps measured) **3 maps** (`MD_GLA04`, `MD_CHI01`, `MD_CHI02`) – the first two victories that the agent wins
with commands only (before, with 60,000 ticks it won none of the four re-measured maps, among them `MD_CHI01`). On the state before the merge of the target acquisition (only the driver changes, `e2ea1a9`) the same three
maps (`MD_CHI01` @18,747, `MD_CHI02` @23,238). Run times: the target acquisition in the simulation replaces the combat loop of the session; `MD_USA01` needs 972 s instead of over 1,800 s for 15,000 ticks,
`Training01` 170 s instead of 672 s (each measured under parallel load, not a performance statement).

**What it fails on now (measured, not fixed):** no run ends any more at a declined write that a player could give as a command (remaining declines: units of other sides on
`MD_USA02`/`MD_USA03`, missing capture unit on `MD_GLA02`). It fails on combat and on the economy: (a) the challenge opponents are pre-built fortresses with six-figure balances; the agent
produces randomly from what it can pay for at the moment – predominantly infantry –, and loses them wave after wave at the base defence (`GC_TankGeneral`: after 48,000 ticks 1 unit, 25 credits, the
warehouse at the start is empty); (b) it does not develop a second warehouse when the first is empty (the same cause as the five stalemates of the AI runs, `SIMULATION-CORE.md` §33.6); (c) mission procedures (escort on
`MD_GLA01`, defence waves on `MD_CHI04`, island maps with transport on `MD_USA04`, units that the script hands over only later, on `MD_USA03`/`Training01`) are known neither to agent nor leaf. The next step
is therefore army composition and supply of the agent (range against base defence, value instead of chance, second supply center), after that the procedure per map.

Tests: `ScriptCommandsOnlyDriverAttacksAnEnemySurplus`, `…CapturesInsteadOfHandingOver`, `…OrdersOnlyItsOwnUnits` (plus the UI signal) next to the two existing ones. The normal mode of the driver is
unchanged: **victory 24/24, defeat 24/24** (60,000 ticks, merged state), audit 24/24 deterministic and identical after save/load, 0 hits on missing types, elimination end probes 13/24 and
15/24, reachability clean 24/24 and 24/24.

**Closing session of Phase 17 (Abschluss-Sitzung Phase 17; 6 October 2026, state `6eb7ae4`, all 24 maps at 15,000 and 60,000 ticks, per map individually, 6 runs in parallel):** with the supply rule of the AI (`docs/SIMULATION-CORE.md` §36.1) unchanged **1 of 24** at 15,000 ticks (`MD_GLA04`) and **3 of 24** at 60,000 ticks (`MD_GLA04` @1022, `MD_CHI01` @18,492, `MD_CHI02` @29,605 – the same ticks as before, the rule does not intervene in these runs); `MD_USA01` and `MD_USA04` aborted after 3,600 s at 60,000 ticks, the remaining 19 "not reached" with 0 world writes. With that, `Training01`, `MD_USA02`, `MD_USA03` and `MD_GLA02` have also been measured with 60,000 ticks for the first time (not reached). The army selection by value and range made the agent worse (60,000 ticks: 2 instead of 3, `MD_CHI02` lost) and has been withdrawn (§36.3). Normal mode of the driver unchanged victory 24/24, defeat 24/24.

### Phase 23: campaign tools at 30, 60 and 120 Hz, commands-only agent (8 and 9 October 2026, measured)

Since Phase 23 the VM has worked on the grid of the original frames (§2, "The clock of the VM is the original frame"); with that the campaign tools are comparable between the rates. Default budgets are
now game time: `drive` 2,000 s, `audit` 60 s, `playthrough` 30 s.

| Run | State | Result |
| --- | --- | --- |
| `CampaignTool drive`, 24 maps, 30 / 60 / 120 Hz | `3b44506` (after round 2) | victory 24/24 and defeat 24/24 at every rate, on every map the same end script, deviation 0.00 s at 60 and 120 Hz (e.g. `MD_CHI04`: victory in tick 48,258 / 96,516 / 193,032, the same frame) |
| `CampaignTool drive`, 24 maps, 30 / 60 / 120 Hz | `cca6f92` (after the AI and movement corrections) | 24/24 and 24/24 at every rate; deviation 0.00 s on all maps **except** `MD_USA02`, defeat 421.03 against 420.03 s (−1.00 s at 60 and 120 Hz; **not investigated**) |
| `CampaignTool audit`, 30 Hz (900 ticks) and 60 Hz (1,800 ticks) | `3b44506`, `cca6f92` | 24/24 deterministic and identical after save/load, end probes victory 13/24, defeat 15/24 at both rates |
| `CampaignTool reachability` | `3b44506`, `cca6f92` | victory clean 24/24, defeat clean 24/24 |
| `drive`, `audit`, `reachability` at the default rate 60 Hz | trial state of the switch, **before** round 4 (`docs/TICKRATE-MIGRATION.md` §15.6.4) | 24/24 and 24/24; audit 24/24, end probes 13/24 and 15/24, 0 hits on missing types; clean 24/24 |
| `reachability`, `drive` at 30 / 60 / 120 Hz, `audit` at 60 Hz | published state `39e41df` (after round 4 and the switch, clean content) | clean 24/24 and 24/24; `drive` 24/24 and 24/24 at every rate, deviation 0.00 s except `MD_USA02` defeat −1.00 s at 60 and 120 Hz; audit 24/24, end probes 13/24 and 15/24 |

The cause of the one earlier deviation (`MD_USA02`: victory at 60 Hz after 340.57 instead of 90.60 s) was `SET_TIMER` in ticks against a frame literal of the map; it is fixed with the clock of the VM
(`docs/TICKRATE-MIGRATION.md` §15.3).

**Commands-only agent** (`drive --commands-only --outcome victory`, 30 Hz; `docs/AI-OBSERVED-2026-10-08.md` §11.6). The AI corrections of the phase (the AI saves for vehicles, own target places,
exchange value rule) change the agent, which uses the `SkirmishAi` as army leader:

| `--commands-only`, `--minutes 34` | `MD_GLA04` | `MD_CHI01` | `MD_CHI02` |
| --- | --- | --- | --- |
| before the AI corrections | victory @1,022 | victory @18,252 | victory @29,250 |
| after the AI corrections | victory @1,022 | victory @16,452 | **not reached** in 34 game minutes |
| after the AI corrections, `--minutes 60` | – | – | **victory @78,429 (43:34)** |

`MD_CHI02` is therefore still won, but needs **43:34 instead of 16:15 game minutes** (tick 78,429 instead of 29,250 at 30 Hz): the earlier victory followed from a lost wave, after which the next one
took a different path; the new army does not lose its mass and needed a rule of its own to defer an unreachable target (`05b2c1f`, only for an agent with mission objectives).
Why it takes 14 minutes longer than the earlier run has not been investigated. The ticks of the row "before the AI corrections" deviate from the values of Phase 17 above (`MD_CHI01` @18,492,
`MD_CHI02` @29,605), because Phase 22 and the factory exit lie in between. **Not measured:** the remaining 21 maps in commands-only mode after the AI corrections (the statement "3 of 24 at
2,000 s" has not been repeated for the final state – verified are the three maps of the table, of which `MD_CHI02` only with a longer budget). At 60 Hz (published state `39e41df`, `--minutes 34`): `MD_GLA04` victory @2,044, `MD_CHI01` victory @32,056, `MD_CHI02` not reached – the same three outcomes as at 30 Hz with that budget.

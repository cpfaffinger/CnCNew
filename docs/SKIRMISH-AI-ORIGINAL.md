# The skirmish AI of the original: inventory, design, implementation, measurement

As of 9 October 2026, branch `worktree-agent-a8c216dbdadd0c201` (base `master` `39e41df`); first version 8 October 2026, branch `worktree-agent-a7fb7c6042062e3e9` (base `main` `2b11488`).
Order of the project owner: take over the AI of the original with its scripts and the three difficulty levels, reproducible from a seed, and keep the own `SkirmishAi` beside it. On
9 October the order was extended: bring the original AI to the point where it **can** be the default, and deliver the basis for the decision which AI is the product AI before phase 24
makes the AI state part of the session hash and the savegame. This document separates strictly between **measured**, **implemented but not assessed** and **not done**. No human has
seen a match of this AI; the client can now show one (§6.6), the default of every tool, test and client is still the built-in AI.

**Summary.** The AI of the original is two thirds data: `SkirmishScripts.scb` (3,957 scripts, 1,340 team templates) says *what* is built and where teams go; the C++ part
(`AISkirmishPlayer` on top of `AIPlayer`) carries it out: build list, dozers, gatherers, team selection, choice of the enemy, defence ring. Before the first order the script VM of the
project already executed all 33 condition types of the library and 48 of its 56 action types; six action types with 624 uses were missing - exactly the ones that hang on the C++
part - and the C++ part itself. Both exist as a **selectable** second AI (`SkirmishAiKind.Original`, three difficulty levels per player); the built-in AI stays the default and
bit-identical (input manifest and reference manifest identical, §7.1). The whole AI state is in the state, the hash and the savegame of its session from the start; that is now
tested for every tick, also between two frames at 60 and 120 Hz (§6.5). The decision basis is at the end: **"Decision basis: which AI is the default AI?"**.

## 1. How the original runs a skirmish computer player

Sources: `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/…`; every statement names file and function. Read and rewritten, nothing copied.

### 1.1 Start of a match

| Step | Where | Content |
| --- | --- | --- |
| Sides of the map become templates | `Source/GameLogic/Map/SidesList.cpp`, `SidesList::prepareForMP_or_Skirmish` | All sides of the map move to `m_skirmishSides`; only `FactionCivilian` sides stay in the side list. If **no** template side has scripts of its own, `data\Scripts\SkirmishScripts.scb` is read: script lists (`PlayerScriptsList`), player names (`ScriptsPlayers`, `ParsePlayersDataChunk`) and teams (`ScriptTeams`, `ParseTeamsDataChunk`); the lists are assigned to the template sides **by player name**, the teams of the library replace those of the map |
| One player per slot | `Source/GameLogic/System/GameLogic.cpp`, `GameLogic::startNewGame` | Side `player<Slot>` with `playerAllies`/`playerEnemies` by team number, `multiplayerStartIndex`, default team `teamplayer<Slot>`; in a skirmish `playerIsSkirmish` and from the slot state `skirmishDifficulty` (`SLOT_EASY_AI` → easy, `SLOT_MED_AI` → normal, `SLOT_BRUTAL_AI` → hard) |
| Scripts and teams per player | `Source/Common/RTS/Player.cpp`, `Player::initFromDict` | The template side whose faction has the same **side** as the player supplies script list and teams; both are copied and qualified with the start position (`ScriptList::duplicateAndQualify`, `Parameter::qualify` in `ScriptEngine/Scripts.cpp`: script, group, team, counter, flag and subroutine names get the number appended, a side parameter with the template name becomes the player name, `<This Team>` stays). For the teams `teamOnCreateScript`, `teamOnIdleScript`, `teamOnUnitDestroyedScript`, `teamOnDestroyedScript`, `teamEnemySightedScript`, `teamAllClearScript`, `teamProductionCondition` and `teamGenericScriptHook0..15` are qualified as well. A **human** player gets the list of the civilian template side this way. If no template side of the own side is found, the player becomes "quietly … nonskirmish" - it runs without AI |
| Victory conditions | `GameLogic::startNewGame` | With more than one team the 26 scripts of `MultiplayerScripts.scb` are put in front of the neutral side (announcements, `VICTORY`/`DEFEAT`/`LOCALDEFEAT` for the local player) |
| Base | `Source/GameLogic/AI/AISkirmishPlayer.cpp`, `AISkirmishPlayer::newMap`, `adjustBuildList` | The `SkirmishBuildList` of the side from `AIData.ini` is copied, moved so that its command centre lies on the start position, and turned by 135° (with `RotateSkirmishBases = Yes` additionally by the ninth of the map; shipped `No`). The angles of the structures are not turned along. The command centre stands at once (`buildStructureNow`); `computeCenterAndRadiusOfBase` remembers centre and radius of the list. **Peculiarity:** `adjustBuildList` tests for every entry the template of the *first* entry - the twelve shipped lists begin with the command centre, so everything is moved |

### 1.2 What C++ does per frame (`AIPlayer::update`)

| Part | Where (`AI/AIPlayer.cpp` unless named otherwise) | Behaviour |
| --- | --- | --- |
| Base building | `AISkirmishPlayer::doBaseBuilding`, `processBaseBuilding` | Every two seconds (at once when a structure was finished): one structure per round. A **priority entry** (asked for by a script) comes first, above it a power plant when power is short; then automatic entries with rebuilds left. Destroyed or captured structures wait `RebuildDelayTimeSeconds` (30 s). Sites without a dozer get a new one. Before that `isLocationSafe`: no enemy within `SupplyCenterSafeRadius` (300) |
| Building with a dozer | `AIPlayer::buildStructureWithDozer` | Dozer, full money, place; if the place is not legal, growing squares are searched (skirmish: up to 120 cells, step four cells). `BuildAssistant::buildObjectNow` checks **no prerequisites**; if the dozer does not reach the place, it is moved there |
| Dozer | `findDozer`, `queueDozer` | Nearest idle dozer; a worker that carries supplies is not taken; if there is none, one is ordered as a priority order |
| Gatherers | `checkForSupplyCenter`, `queueSupplyTruck` | A finished supply centre wants `ResourceGatherers<Level>` + 1 gatherers as long as a stocked, non-hostile warehouse lies within `SUPPLY_CENTER_CLOSE_DIST` (20 cells); missing ones are built as a priority order, loose gatherers are docked again |
| Supply centres | `buildBySupplies`, `findSupplyCenter`, `isSupplySourceSafe` | Only on a script's order (`AI_PLAYER_BUILD_SUPPLY_CENTER`): warehouse with enough supplies, without an own centre beside it, not much nearer to the enemy; the place beside it as a priority entry |
| Recruit before training | `AIPlayer::queueUnits`, `Team::tryToRecruit` | Every order of a team first takes fitting units from recruitable teams of lower priority - above all from the default team, where the units of disbanded teams stand - within `MaxRecruitRadius` (500) of home or base centre; only then units are trained, one unit at a time per order |
| Teams | `AISkirmishPlayer::doTeamBuilding`, `AIPlayer::selectTeamToBuild`, `isAGoodIdeaToBuildTeam`, `isPossibleToBuildTeam`, `queueUnits`, `checkQueuedTeams`, `checkReadyTeams` | When the team timer has run out (`TeamSeconds` 10 s; ÷ 0.6 below `Poor` 2,000, ÷ 2 above `Wealthy` 7,000): among all team templates of the player whose **production condition** (a script, `TeamPrototype::evaluateProductionCondition` in `Common/RTS/Team.cpp`) holds, that do not yet have `teamMaxInstances` instances, are not being built and are possible (a factory for every type, one of them free, money for `TeamResourcesToStart` = 10 % of the mean cost), the highest `teamProductionPriority` wins; among equals `GameLogicRandomValue(0, count-1)` **draws**. Before that `selectTeamToReinforce` gets its turn: a team in the field with `teamAutoReinforce` and a higher priority that misses a unit gets one. Teams the AI chose itself are only begun at free factories; after `teamInitialIdleFrames` a team is disbanded when its minimum is neither built nor in work |
| Team scripts | `Common/RTS/Player.cpp` `Player::update` → `Team::updateGenericScripts` (`Team.cpp`) | Every team instance tries its `teamGenericScriptHook` scripts per frame (conditions with the team as `<This Team>`, then actions; a one-shot script only once per instance). **The attacks hang here**: e.g. `China Attack Sequence Flank` on the team `China N 3RD Wave Tanks` |
| A team's own scripts | `Team::updateState` → `ScriptEngine::runScript(name, team)` | On create, idle, enemy sighted, all clear, destroyed, unit destroyed: the script runs as a subroutine with the team as calling team (`m_callingTeam`), which `executeScript` leaves alone - it only sets `m_conditionTeam`. `getTeamNamed(THIS_TEAM)` returns the calling team first |
| Common victim | `Common/RTS/Team.cpp`, `Team::setTeamTargetObject`/`getTeamTargetObject`; `AIAttackFireWeaponState::onEnter`, `AIHuntState::update` (`AI/AIStates.cpp`); `AIUpdateInterface::getNextMoodTarget` (`Object/Update/AIUpdate.cpp`) | The members of a team with `teamAttackCommonTarget` attack one victim: the first member that fires names it, a hunting member names or follows it, every member that looks for a victim takes it before its own look. Dropped when dead, stealthed, contained or an aircraft. **Not on the easy level** |
| Skills | `AIPlayer::doUpgradesAndSkills` | With purchase points: the skill set is **drawn** once (`GameLogicRandomValue(0, limit)` over the non-empty `SkillSet` blocks of the side), then its sciences are bought in order |
| Enemy | `AISkirmishPlayer::getAiEnemy`, `acquireEnemy` | Every five seconds: the enemy stays as long as it has units and a build facility; otherwise the one with the nearest base; one that another AI already attacks counts 500 farther, one that attacks me 25 nearer |
| Defence ring | `AISkirmishPlayer::buildAIBaseDefenseStructure` | On the circle base radius + `SkirmishBaseDefenseExtraDistance` (150): "front" towards the path `Center<n>`, "flank" alternately `Backdoor<n>` and `Flank<n>`; the first on the line, further ones alternately left and right at a distance of four structure radii, up to 60° |
| Superweapons | `AISkirmishPlayer::computeSuperweaponTarget`, `AIPlayer::computeSuperweaponTarget` | Target by value within the effect radius; cluster mines **drawn** onto one of the three own approaches |
| Bridges | `updateBridgeRepair`, `AISkirmishPlayer::checkBridges` | A dozer repairs a destroyed bridge on an approach path |

### 1.3 What the scripts do

Measured with `MapTool script-library <content>` (§2). Per faction about 320 scripts in 30 to 32 groups and 94 to 126 team templates. The scripts

- build the base in a fixed sequence (`SKIRMISH_BUILD_BUILDING`, 382 uses - in the build lists **only the command centre** is automatic), supply centres at the warehouses
  (`AI_PLAYER_BUILD_SUPPLY_CENTER`, 164), defences (`SKIRMISH_BUILD_BASE_DEFENSE_FRONT/_FLANK`, `SKIRMISH_BUILD_STRUCTURE_FRONT/_FLANK`, together 223) and upgrades (`AI_PLAYER_BUILD_UPGRADE`, 166);
- keep an escalation counter (`_ESCALATION`) and flags on which the production conditions of the waves hang (`_CHINA_3RD_WAVE_N` …);
- define the waves as teams with priority, minimum and maximum count, build time limit (`teamInitialIdleFrames`, e.g. 3,000 frames for four Battlemasters), create script
  (e.g. "guard the base") and generic scripts (attack sequence over `Center`/`Flank`/`Backdoor`, `SKIRMISH_MOVE_TO_APPROACH_PATH` 69, `SKIRMISH_FOLLOW_APPROACH_PATH` 54, `TEAM_HUNT` 150,
  `TEAM_GUARD_AREA` 671 with `[Skirmish]MyInnerPerimeter`/`MyOuterPerimeter`/`EnemyInnerPerimeter` and `CombatZone`);
- trigger special powers (`SKIRMISH_SPECIAL_POWER_READY` 239 conditions, `SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST` 107), garrison buildings, capture oil derricks and unmanned vehicles;
- **give themselves money** (`PLAYER_GIVE_MONEY`, 155 uses), see §1.4.

### 1.4 How the difficulty enters

| Lever | Where | Effect in a skirmish |
| --- | --- | --- |
| Script flags easy/normal/hard | `ScriptEngine::executeScript` against `Player::getPlayerDifficulty`; for production conditions `TeamPrototype::evaluateProductionCondition` | **the main lever.** Of 3,957 scripts 986 run on all levels, 1,419 on normal and hard, 826 only hard, 438 only normal, 266 only easy, 22 easy and normal. Per faction: easy 90 to 113 scripts, normal 220 to 244, hard 253 to 269 |
| Money gifts | Scripts, e.g. `China AI Bonus Early/Mid/Late/Really Late` (hard only) | The hard army receives 55,000 credits in total at escalation 5, 20, 40 and 75; all levels 1,600 at the start ("Give AI money to cover bug"); normal and hard 4,000 per base expansion |
| Gatherers per centre | `AIPlayer::checkForSupplyCenter`, `AIData.ini` `ResourceGatherersEasy/Normal/Hard` | exists, but in the shipped data the same on all levels per side (2, GLA 5) |
| Common team victim | `Team::setTeamTargetObject` (`Team.cpp`) | the easy level sets none (implemented, §6.1) |
| Victim choice of a squad | `AIAttackSquadState::chooseVictim` (`AI/AIStates.cpp`) | easy random, normal the nearest, hard all the same - **but only for an attack on a squad** (`TEAM_ATTACK_TEAM`, `NAMED_ATTACK_TEAM`), which `SkirmishScripts.scb` never orders (0 uses, measured): no effect in a skirmish of the shipped library |
| Weapon and health bonuses `SOLO_AI_*` | `Player::friend_applyDifficultyBonusesForObject` | **no** - only in the campaign (`docs/SIMULATION-CORE.md` §35) |

### 1.5 Where chance enters

The logic draws from exactly one stream (`GameLogicRandomValue`); `InitGameLogicRandom(seed)` is set with the seed of the game information (`GameNetwork/LANAPICallbacks.cpp`,
`GameSpy/StagingRoomGameInfo.cpp`), which the host takes from the wall clock (`LANAPI.cpp`: `setSeed(GetTickCount())`). After that everything is deterministic. Draws of the AI: team
choice among equal priority (`AIPlayer::selectTeamToBuild`), skill set (`doUpgradesAndSkills`), cluster mine approach (`AISkirmishPlayer::computeSuperweaponTarget`), the first evaluation of
delayed scripts (`ScriptEngine.cpp`, 3 calls), random timers of the scripts (`SET_RANDOM_TIMER`, `SET_RANDOM_MSEC_TIMER`, 15 uses), plus 26 calls in the state machines of the units
(`AIStates.cpp`, `AIGuard*.cpp`, `AIGroup.cpp`).

### 1.6 How the original limits the cost of the scripts

`ScriptEngine::update` (`ScriptEngine.cpp`) walks, once per logic frame, every script of every side: outside groups, then in every group that is active and no subroutine group
(`executeScripts`). `ScriptEngine::executeScript` leaves a script at once when it is not active, not meant for the difficulty of the current player, or not yet due
(`TheGameLogic->getFrame() < pScript->getFrameToEvaluate()`; a script with "delay evaluation seconds" is due again `delaySeconds * LOGICFRAMES_PER_SECOND` later, and its first
evaluation is offset by `GameLogicRandomValue(0, 2*LOGICFRAMES_PER_SECOND)` in `checkConditionsForTeamNames`). Subroutine scripts only run when called; a one-shot script deactivates
itself. **There is no further staggering** ("evaluate every N frames") in the engine. Everything of this the VM of the project did before this order.

What the reference does beyond that sits in the conditions (`ScriptConditions.cpp`), per condition *instance* (`Condition::m_customData`/`m_customFrame`, or the integer of a parameter):

| Condition | Where | What is remembered | Here |
| --- | --- | --- | --- |
| `SKIRMISH_SPECIAL_POWER_READY` | `evaluateSkirmishSpecialPowerIsReady` | when not ready: the frame the nearest module becomes ready, at most ten seconds ahead (`pPower->friend_setInt(nextFrame)`); false until then | **implemented** for a session of the original AI, as state (`ScriptEngine.AiConditionMemory.cs`); it changes when a power is noticed |
| `SUPPLY_SOURCE_SAFE` | `evaluateSkirmishSupplySourceSafe` | the answer for two seconds | **implemented** likewise |
| `BUILT_BY_PLAYER`, `PLAYER_HAS_OBJECT_COMPARISON` | `evaluateBuiltByPlayer`, `evaluatePlayerUnitCondition` | the answer until an object is created or destroyed (`getFrameObjectCountChanged`) | not as memory: it does not change an answer that is counted exactly; the count itself was made cheap (§7.3) |
| `…_UNIT_TYPE_IN_TRIGGER_AREA`, `…_UNIT_KIND_IN_AREA`, `SKIRMISH_VALUE_IN_AREA`, `SKIRMISH_PLAYER_HAS_UNITS_IN_AREA` | same file | the answer until the object count changes or a team of the player enters or leaves the area | not as memory, for the same reason (the reference can answer from an older state in rare cases; the engine counts) |
| `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE` | `evaluateSkirmishPlayerTechBuildingWithinDistancePerimeter` | the first answer, for ever | was implemented before (`_techBuildingAnswers`) |

A mission evaluates the first two on every look as before: its reference values are fixed and the memory would be new state in its snapshot.

## 2. The script library in numbers (`MapTool script-library <content>`, measured)

| | `SkirmishScripts.scb` | `MultiplayerScripts.scb` |
| --- | --- | --- |
| Size | 2,272,629 bytes, container `CkMp` | 10,381 bytes |
| Template players / script lists | 14 / 14 (neutral, 12 factions, `PlyrCivilian`) | 14 / 14 (only the neutral list has scripts) |
| Scripts | 3,957 (1,810 subroutines, 1,902 one-shot, 366 initially inactive, 1,047 with an evaluation delay) in 378 groups | 26 |
| Conditions | 6,956 in 33 types | 55 in 7 types |
| Actions | 11,571 true + 26 false in 56 types | 45 in 7 types |
| Team templates | **1,340 - the teams are in the `.scb`**, not in the maps | 14 (default teams only) |
| of these | 1,285 with production condition, 1,313 with create script, 818 with destroyed script, 1,765 set generic script hooks, 469 recruitable, 26 with `teamAutoReinforce`, 18 singletons, 0 with a home waypoint; the key `teamAttackCommonTarget` on 608, `teamVeterancy` on 4 | - |
| further chunks | `ObjectsList`, `PolygonTriggers`, `WaypointsList` (empty, reported as skipped) | likewise |

**Coverage of the VM before the first order** (uses in the skirmish library):

| | Types | Uses |
| --- | --- | --- |
| Conditions executed | 33 of 33 | 6,956 |
| Actions executed | 48 | 10,885 |
| Actions forwarded (`MUSIC_SET_TRACK`) | 1 | 76 |
| Actions recorded (`TEAM_SET_ATTITUDE`; the mood has acted through the target acquisition since phase 17) | 1 | 12 |
| **Actions missing** | **6** | **624** |

The six: `SKIRMISH_BUILD_BUILDING` 382, `SKIRMISH_BUILD_STRUCTURE_FLANK` 97, `SKIRMISH_BUILD_STRUCTURE_FRONT` 55, `SKIRMISH_BUILD_BASE_DEFENSE_FRONT` 46,
`SKIRMISH_BUILD_BASE_DEFENSE_FLANK` 25, `AI_PLAYER_BUILD_TYPE_NEAREST_TEAM` 19. Added to that came what the count by types does **not** show: the qualified area names
(`[Skirmish]MyOuterPerimeter` 400, `MyInnerPerimeter` 362, `EnemyInnerPerimeter` 50, `EnemyOuterPerimeter` 18 uses - the VM did not know them), `<This Player's Enemy>`,
the difficulty per player, the 1,765 generic team scripts and the production conditions (both are only run by `AIPlayer`/`Player::update`).
**Now:** in a session of the original AI 54 action types with 11,509 uses executed, 0 missing; missing types hit at run time: 0 in every match of every survey of this document (§7).

**What the maps supply** (64 skirmish maps with at least two start positions): not a single one brings scripts of a template side of its own (so the library is attached everywhere);
7 civilian script lists. Complete for **every** start position: `InnerPerimeter<n>` 64 maps, `OuterPerimeter<n>` 63, path `Center<n>` 61, `Flank<n>` 62, `Backdoor<n>` 60;
`CombatZone` 61. **62 maps have the twelve template sides, two only the three of the base game** (`Armored Fury`, `Scorched Earth`): there a general's player gets no scripts, as in the
original (a named option that is not the reference gives it the library's, §6.2 item 11).

**`MultiplayerScripts.scb`** (26 scripts on the neutral side): 7 condition types and 7 action types - timers (`SET_MILLISECOND_TIMER` 19, `SET_COUNTER` 3), announcements
(`MUSIC_SET_TRACK` 10, `SPEECH_PLAY` 10) and one each of `VICTORY`, `DEFEAT`, `LOCALDEFEAT` for the local player. It decides nothing about who has won - that is
`VictoryConditions::update`, which its conditions (`MULTIPLAYER_ALLIED_VICTORY` …) only read - it announces it and ends the game for the local human. It is not attached; the session
decides the winner itself with the rule `SkirmishMatchSession` uses (every slot player that still has objects is an ally of the first such player). Whether that rule equals
`VictoryConditions` in every corner has not been compared here.

## 3. `AIData.ini` (`SimTool ai-data <content>`, measured)

The definition compiler carries `AIData` on as a compatibility definition (`baselines/compatibility.json`, category `definition`, 27; **nothing changes in the baseline**).
`CnCNew.Simulation/Skirmish/Original/AiData.cs` reads strictly from it what the AI needs: 14 scalar keys (`StructureSeconds` 0, `TeamSeconds` 10, `Wealthy` 7,000,
`Poor` 2,000, the four rate factors 2 and 0.6, `TeamResourcesToStart` 0.1, `MaxRecruitRadius` 500, `SkirmishBaseDefenseExtraDistance` 150, `RotateSkirmishBases` no,
`SupplyCenterSafeRadius` 300, `RebuildDelayTimeSeconds` 30), 12 `SideInfo` blocks (gatherers per level, `BaseDefenseStructure1`, two skill sets each with seven sciences) and
12 `SkirmishBuildList` blocks with together **214 structures, of which exactly one per list - the command centre - is automatic**. 28 further valid keys of the table this
catalogue does not read and lists (`UnreadKeys`); an unknown key, a missing mandatory key, an unreadable number or a structure without `Location` is an error.

**Why a catalogue of its own:** the input manifest hashes `TemplateCatalog` as a whole; a new member there would have changed it. `AiData` stands beside it and in no manifest.
The strict typing in the definition compiler (a `DefinitionKind` with schema, then the baseline entry `AIData` goes and the definition snapshot changes its hash) is
prepared, but a change of its own that needs review.

## 4. The built-in `SkirmishAi` in comparison

`src/CnCNew.Simulation/Skirmish/SkirmishAi.cs` is a development of its own: a thinking step every 15 frames in a fixed order, roles from modules and `KindOf`,
planning only with what was seen and remembered. Its state (memory, order tables, own random stream) lies **outside** snapshot and hash (register E11 in
`docs/MULTIPLAYER-PLAN.md`), `SkirmishMatchSession.CreateSave()` throws. Since the first viewing by the project owner (`docs/AI-OBSERVED-2026-10-08.md`) it saves for vehicles,
composes its army by exchange value and decides 305 of 320 matches of the survey at 30 Hz and 301 of 320 at 60 Hz. No difficulty levels. The commands-only agent of the missions
uses it as well.

| | `SkirmishAi` (built in) | Original AI |
| --- | --- | --- |
| Decisions | code | data (scripts) + code |
| Knowledge | only what was seen and remembered | everything (as the original: no sight check) |
| State in snapshot/hash | no | yes, complete (tested at every tick) |
| Difficulty | none | three levels per player |
| Session | `SkirmishMatchSession`, reproducible as a replay | `ScriptedSkirmishSession`, like a mission **no** replay; the truth is the state |
| Map objects | only warehouses | structures and warehouses of the map (§6.4) |

## 5. Design: alternatives and recommendation

**(a) Faithful port** - the C++ core rewritten, the library executed by the existing VM. For it: the scripts are finished, tuned data for twelve factions and three
levels; the VM already covered them to 94 % of the uses; the difficulty comes along "for free". Against it: the session can no longer be reproduced as a command replay (scripts
write directly into the world), and the script evaluation costs time per tick (§7.3).

**(b) Hybrid/improved** - own logic where the original is weak. As a *kind of AI of its own* or an option, never silently in "original".

**(c) Only extend the built-in AI** - discards the 1,340 tuned teams; the measurement in `docs/SIMULATION-CORE.md` §36.3 shows how expensive an own army selection is to find.

**Recommended and implemented: (a) as a selectable second AI, (b) as a list of proposals (§8).** Decisions:

- **Determinism.** The simulation is always deterministic. The "switch" of the order is the **seed in the match setup** (`ScriptedSkirmishOptions.Seed` → world seed → `LogicRandom`):
  a test fixes it, a normal game draws it outside the simulation. All random draws of the AI go through this one stream, as in the original.
- **State in the hash from the start** (in anticipation of phase 24): the C++ part lives in the `ScriptEngine` (`ScriptEngine.AiPlayer.cs` and the `ScriptEngine.Ai*.cs` files);
  `Describe`/`Load`/`StateHash` contain build list, timers, enemy, skill set, defence angles, production and hook state, the condition memory, the common victims and the
  reinforcement orders in a fixed order. `WorldSnapshot` is unchanged (version 10); the session state has a format of its own, `cncnew-scripted-skirmish-state` version 1.
- **Opt-in.** `ScriptEngineOptions.Ai`/`MissionOptions.Ai` are `null` for every mission; without them no new path takes another way. `SkirmishMatchSession` is untouched.
- **Time.** The AI thinks on ticks that begin a frame of the original; its deadlines are tick deadlines from frames/seconds through the time service (`SourceRules` enforces that).
  It shares the grid of the script VM, which evaluates on frame boundaries at every rate (`docs/SCRIPT-ENGINE.md` §2).
- **Cost budget.** The aim is the order of magnitude of the built-in AI. Scripts and AI for eight players are now below the budget of 150 µs per tick at 60 Hz (§7.3).

## 6. What is implemented

### 6.1 Building blocks

| Building block | Place | Task |
| --- | --- | --- |
| `SageMapDecoder.DecodeScriptLibrary`, `MapScriptLibrary` | `CnCNew.Formats`, `CnCNew.Data/World` | decode the `.scb` (three chunks, everything else reported as skipped) |
| `AiData` | `Simulation/Skirmish/Original` | §3 |
| `SkirmishScriptAttachment` | `Simulation/Skirmish/Original` | §1.1 as a pure transformation of data: map + library + slots → `MapData` (side 0 neutral, sides 1..N the slots, then civilians) |
| `ScriptEngine.AiPlayer.cs` | `Simulation/Scripting` | §1.2 as far as not excepted below; the six actions; qualified areas; current player; difficulty per list |
| `ScriptEngine.AiConditionMemory.cs` | `Simulation/Scripting` | §1.6: when two conditions are looked at again |
| `ScriptEngine.AiTeamTarget.cs` | `Simulation/Scripting` | the common victim of a team (`Team::setTeamTargetObject`); the world asks through `IAcquisitionAdvisor.CommonTargetOf`/`NoteFiring` |
| `ScriptEngine.AiReinforce.cs` | `Simulation/Scripting` | `AIPlayer::selectTeamToReinforce`, `AIUpdateInterface::joinTeam` |
| `ScriptedSkirmishSession` | `Simulation/Skirmish/Original` | session on top of `MissionSession`: start money, start units, victory, save/load; a slot can also be played by the built-in AI (comparison); `Players`, `RecordedCommands` for observers |
| `SkirmishAiKind { Builtin, Original }`, `ScriptDifficulty { Easy, Normal, Hard }` per slot | same place | choice |
| `MatchAi`, `MatchSetup.Ai`, `LiveMatch.Scripted` | `CnCNew.Game/Live` | the game layer runs either session; `GameData.LoadOriginalAi` reads library and AI data from the content |

### 6.2 Deviations from the original (complete)

For each deliberate deviation: what the original does, what it would take, what it costs.

1. **Prerequisites apply.** The original builds a priority entry whatever its prerequisites: `AIPlayer::buildStructureWithDozer` calls `TheBuildAssistant->buildObjectNow`, which
   places the object without `canMakeUnit`. Here a construction is an ordinary player command; consequence: instead of the *first* open priority entry the first one the rules
   allow now is taken; one without its prerequisite waits without blocking the others. *To follow the original:* a construction path for the AI that skips the prerequisite test
   (a flag on `ConstructCommand` or a direct world write) - a rule exception only for a computer player, which a mixed match (human and AI under the same rules) makes visible.
   The scripts order their buildings in an order that respects the prerequisites, so the difference shows only when a prerequisite structure is destroyed. Not done; cost of doing
   it: one command field and a new `CommandTypes.Version`.
2. **No moving of the dozer** when it does not reach the building place.
3. **Map objects:** the default is only structures and warehouses of the map (§6.4). The original creates everything. `--all-map-objects`
   (`ScriptedSkirmishOptions.AllMapObjects`) does the same at a cost measured on Alpine Assault of 7.5 ms per tick, 3.4 ms of it `RailroadBehavior`. To make it the default the
   scenery modules would have to become cheap first (trees, lamps, sound objects, trains: several hundred objects per map whose updates cost more than the rest of the match).
4. **Map explored** for computer players. The original does not ask the shroud when the AI places a structure: `AIPlayer::buildStructureWithDozer` tests
   `TheBuildAssistant->isLocationLegalToBuild` with `CLEAR_PATH | TERRAIN_RESTRICTIONS | NO_OBJECT_OVERLAP`, without the `SHROUD_REVEALED` a human's placement carries
   (`InGameUI.cpp`, `PlaceEventTranslator.cpp`). Here the construction command requires an explored cell, so the session explores the map for a computer player
   (`World.ExploreMap`). *To follow the original exactly:* the placement test without the shroud for a computer player; same kind of rule exception as item 1. Not done.
5. **Gatherers:** implemented by effect (as many gatherers as all supplied centres together want; idle ones to the nearest stocked warehouse), not per centre with a preferred dock.
   Centres are looked for in the world, not only in the build list.
6. **Factory search** over all own production structures and their `CommandSet` instead of only over the build list.
7. **Science points on a timer.** The original gives a player skill points for what it destroys (`Player::addSkillPointsForKill`, `addSkillPoints`; levels from `Rank.ini`) and
   purchase points per rank (`Player::addSciencePurchasePoints`).
   There is no rank system in the simulation; the session gives every living player one purchase point every 2,700 frames (90 s), as `SkirmishMatchSession` does. *To follow the
   original:* the rank system (skill points per kill by `ExperienceValue`, `Rank.ini` thresholds, purchase points per rank) - a rule of the game for humans as well, not a part
   of the AI; it belongs to a phase of its own.
8. **Start money** 10,000 from the options (the setup screen of the original sets it), as for the built-in AI.
9. **Not implemented:** bridge repair (`AIPlayer::updateBridgeRepair`, `AISkirmishPlayer::checkBridges`: needs the bridge state per approach path and a repair order for a dozer;
   whether a map of the surveys ever needs it has not been measured); following a GLA rebuild hole (`processBaseBuilding`
   finds the hole of a destroyed structure through `RebuildHoleBehavior` and waits for it instead of sending a worker: here the entry is rebuilt by a worker after the delay if the
   hole failed, and the hole rebuilds by itself otherwise - the visible difference is a second site when both happen); cluster mines on the own approaches
   (`AISkirmishPlayer::computeSuperweaponTarget` draws one of `Center`/`Flank`/`Backdoor` for a power of type `SPECIAL_CLUSTER_MINES` or `NUKE_SPECIAL_CLUSTER_MINES`: here the mines go where every other superweapon
   goes, to the most valuable place of the enemy); `MultiplayerScripts.scb` (§2: announcements only). `teamVeterancy` needs nothing: the reference reads the key into
   `TeamTemplateInfo::m_veterancy` (`Team.cpp`) and no code reads the field. `AIAttackSquadState::chooseVictim` has no caller in a skirmish of the shipped library (§1.4).
10. **Human in a slot:** the first slot is tested (the client's skirmish against the original AI, `tests/CnCNew.Game.Tests`); the start base of a human takes the start position by
    the number of humans, not by the slot, so a human in another slot is not supported.
11. **Named option, not the reference:** `ScriptedSkirmishOptions.LibraryScriptsForMissingSides` (`--library-for-missing-sides`): a player whose side has no template side on the
    map takes the library's template player of its side (`Skirmish<Side>`). Off by default; the attachment report says when it was used.
12. **First evaluation of delayed scripts:** the reference draws the offset (0 to 2 s) from the logic random stream when the scripts are prepared; the engine derives it from the
    script name, because a restored engine is constructed again and must not draw again. The spread is the same, the individual offsets are not.
13. What the VM approximated before (`docs/SCRIPT-ENGINE.md` §8) still applies: formations, `SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST` without a grid, garrison etc.; the
    acquisition of the units is the one of the simulation (`docs/SIMULATION-CORE.md` §33), so "a member takes the team's victim" means: when it can attack it from where it is, or
    is on an attack-move and may chase.

**A defect found and fixed on the way** (commit `b3c4b4e`): the VM kept one variable where the reference keeps two (`m_callingTeam`, `m_conditionTeam`), and a called script whose
conditions name no team lost `<This Team>`. The create scripts of the teams - 1,313 of 1,340 prototypes have one - therefore did nothing with `<This Team>`
(`TEAM_HUNT <This Team>`, `TEAM_GUARD_AREA <This Team> …`). Fixed for a session of the original AI. **The same defect exists in the mission VM and is not fixed there**: fixing it
changes mission runs of the reference manifest and is a decision and a manifest commit of its own.

### 6.3 Changes to existing VM files

`ScriptEngine.cs`: option `Ai`, `InitializeAi()` in the constructor, `UpdateAiPlayers()` after the script loop, difficulty per list, `<This Player>` through
`CurrentScriptPlayer()`, `<This Player's Enemy>`, `DescribeAi`/`LoadAi`, the condition ordinal, the calling team and the condition team kept apart for an AI session.
`ScriptEngine.Actions.cs`/`.Conditions.cs`/`.Extended.cs`/`.Phase13.cs`/`.TeamAbilities.cs`: six times `_program.FindArea` → `FindAreaQualified`, the switch `TryExecuteAiAction`,
`AiObserveSpawn`, `SkirmishEnemy()`, `BuildBySupplies` with an AI branch, `HuntVictim`. `ScriptEngine.ExtendedActions.cs`: `BuildTeam(…, idleOnly)`, holding back the team orders,
reinforcement orders. `ScriptProgram.cs`: `GenericScripts`, `AutoReinforce`, `AttackCommonTarget`. `ScriptCoverage.cs`: `SkirmishAiActions` (the static table for missions stays
187/129/19/9). `MissionSession.cs`: `MissionOptions.Ai`, `CommandObserver`. `IAcquisitionAdvisor`: `CommonTargetOf`, `NoteFiring` (defaults: nothing). Without `Ai` every one of
these paths is the old one.

### 6.4 Map objects: measured reason

Alpine Assault, China against GLA, 4,000 ticks with a probe (`ai-match … --profile --all-map-objects`, first order, 30 Hz): 7,519 µs per tick, of it `RailroadBehavior` 3,424 µs
(13 calls per tick - the train of the map), 596 `StealthUpdate` calls per tick, world hash 1,011 µs; the map brings 397 trees, 22 lamps, 68 sound objects. Without these objects
(default): 165 objects at the end instead of 714 and about 1.25 ms per tick. No script of the library refers to them. `--all-map-objects`
(`ScriptedSkirmishOptions.AllMapObjects`) restores the state of the original.

### 6.5 Tests

`tests/CnCNew.Formats.Tests` (library decoder, three cases). `tests/CnCNew.Simulation.Tests/Tests.OriginalSkirmishAi.cs`, synthetic: `AIData` strict (four error cases);
attaching like the original (qualification, allies, human, map with scripts of its own, missing template side, the named option); a match of two AIs from a test library (command
centre on the start position, list turned by 135°, `SKIRMISH_BUILD_BUILDING`, difficulty per player, `[Skirmish]MyInnerPerimeter`, team choice, both soldiers of a team, create
script with `<This Team>` hunting, generic script, second run equal, save/load equal, foreign program rejected); rebuilding and front defence; seed (the same equal, both skill sets
over six seeds) and 30/60 Hz; the condition memory of a special power at 30 and 60 Hz; the common victim (hard has one, two members attack it together, easy never has one, saved
and restored); reinforcement (the team that asks gets a soldier, the other does not); **save at any tick** at 30, 60 and 120 Hz - six save points per rate, offsets inside the
frame, the restored session hashes the same, writes the same document again and continues tick for tick like an uninterrupted one. With `--content-dir`: the real library on Alpine
Assault (numbers from §2/§3, 0 missing types, bases grow, same seed equal, other seed different, save and continue equal); the same match at 30, 60 and 120 Hz for 6,600 frames;
a fight at 60 and 120 Hz saved between two frames at four points, restored and continued: equal at every compared tick. `tests/CnCNew.Game.Tests`: the spectator on a match of
the original AI (commands of both armies, panels, 0 missing types, deterministic), a human against the original AI, the three armies in the skirmish setup when the original AI is
selected (the default setup and its golden hashes unchanged).

**How the session hash relates to `SimulationWorld.StateHash`:** `ScriptedSkirmishSession.StateHash()` mixes `MissionSession.StateHash()` - which is the pair
(`World.StateHash`, `ScriptEngine.StateHash()`) - with the victory tick and the winner. `World.StateHash` is the incremental world hash (`--verify-incremental` checks it against a
full recomputation; `perf --ai original` does that check at the end of its run: equal). `ScriptEngine.StateHash()` hashes `Describe`, the same walk that writes the savegame, so
whatever is saved is hashed and the other way round; the AI state is part of it. Derived tables (teams per prototype, first team of an object, prototype and side lookups, generic
script units) are rebuilt from state and are in neither.

### 6.6 Use

```powershell
dotnet build -c Release tools/CnCNew.SimTool
dotnet build -c Release tools/CnCNew.MapTool
dotnet run -c Release --no-build --project tools/CnCNew.MapTool -- script-library <content> [--verbose | --show "<text in the team or script name>"]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- ai-data <content>
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- ai-match <content> --map "Maps/Alpine Assault/Alpine Assault.map" --factions FactionChina,FactionGLA `
    --ai original --difficulty medium [--ai-b builtin|original] [--difficulty-b easy|medium|hard] [--seed N] [--max-ticks N | --minutes M] [--trace N] [--profile] [--no-verify] [--all-map-objects] [--library-for-missing-sides]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- ai-survey <content> --out <tsv> --seeds N --minutes 25 --jobs 4 --ai original --difficulty hard --difficulty-b easy
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- ai-census <content> --out <tsv> --seeds N --jobs 4 --ai original --difficulty hard
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- perf <content> --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ai original --difficulty medium --minutes 30 [--script-detail]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- rate-compare <content> --original-diff FactionChina,FactionGLA --rates 30,60 --difficulty medium --frames 27000 --fields
```

`--ai`/`--difficulty` apply to the first player and, when `--ai-b`/`--difficulty-b` are missing, to all; without one of the four options `ai-match`, `ai-survey`, `ai-census` and
`perf` are unchanged (built-in AI in `SkirmishMatchSession`). `ai-match` checks a second fresh run and a continuation from a savegame (session hash every 30 ticks).

**Client and spectator** (default unchanged: the built-in AI):

```powershell
# watch two armies of the original AI, a hard one against an easy one
pwsh scripts/demo-spectator.ps1 -Ai original -DifficultyA hard -DifficultyB easy
pwsh scripts/demo-spectator.ps1 -Ai original -DifficultyA hard -DifficultyB hard -Map "Alpine Assault" -FactionA FactionChina -FactionB FactionGLA -Seed 7
# play against a hard army of the original AI
dotnet run --project src/CnCNew.Client.Stride -c Release -- --match "Maps/Alpine Assault/Alpine Assault.map" --content-dir game/content --faction FactionAmerica --opponent FactionChina --ai original --difficulty hard
# the shell with the three armies of the original in the skirmish setup
dotnet run --project src/CnCNew.Client.Stride -c Release -- --content-dir game/content --ai original
# headless check of the spectator, one pass per AI
pwsh scripts/check-spectator.ps1
```

`--ai` and `--difficulty` of the client take one value for all slots or a comma list per slot in slot order (the first entry is the first slot, which the computer plays with
`--autoplay` or `--spectate`). `scripts/check-spectator.ps1` passes for both AIs (9 October 2026, seed 7, Alpine Assault, America hard against China easy for the original AI:
both armies give orders, the panels have data, 297 game seconds, 0 missing script types).

## 7. Measurement (9 October 2026, Release, 60 Hz unless stated; state `c581685` for the simulation, `6bad1cd` for tools and game layer)

Every survey is `SimTool ai-survey` over the 16 pairings of the reference manifest (four maps × four pairings) with a budget of 25 game minutes, counted from the written TSV files;
"open" is a match without a winner in that time. Intervals are exact binomial 95 % intervals (Clopper-Pearson). The numbers of the first version of this document (8 October,
30 Hz, before later simulation changes) are in §7.6 for comparison.

### 7.1 The built-in AI is unchanged

`SimTool input-manifest check`: identical. `reference-manifest check --set quick --jobs 3`: 9 runs identical after every simulation commit of this order; `--set full --jobs 5` at the
end: **50 runs identical**. Never `write`. The rate-equivalence suite (`Simulation.Tests -- --content-dir <content> --only RateEquivalence`, run as part of the whole program): all ten groups
pass, 0 / 0. `Simulation.Tests`: all pass at `--tick-rate 30`, `60` and `120` and with `--content-dir`. `Game.Tests` (Release, `--content-dir`): all 167 pass, the golden screen
hashes (`tests/CnCNew.Game.Tests/baselines/screens.txt`) unchanged. `Presentation.Tests` (with content) and `Ui.Tests`: all pass. The objective driver on the sample
(`CampaignTool drive <content> --map "/MD_USA02.map"`, `"/MD_CHI04.map"`, `"/GC_ChinaBoss.map"`), before and after: the same end script at the same tick for victory and defeat on
all three (`END_Final_` @5,436 / `Lose_END_Final_` @25,202; `END_Final_` @96,516 / `Lose_END_Final_` @546; `End the mission` @720 / `Lose_END_Final_USA` @4,862). Solution build and
every built project: 0 warnings.

### 7.2 The surveys for the decision

**(a) Original medium against original medium, 16 pairings × 20 seeds:**

| Rate | Matches | Decided | Slot 1 / slot 2 | Open | Duration of decided matches: mean / median (range) |
| --- | --- | --- | --- | --- | --- |
| 60 Hz | 320 | **202 (63 %)** | 126 / 76 | 118 | 11.0 / 9.2 game minutes (5.3 to 24.8) |
| 30 Hz | 320 | 205 (64 %) | 129 / 76 | 115 | 10.9 / 9.3 (5.2 to 24.8) |

The built-in AI decides 301 of 320 at 60 Hz and 305 of 320 at 30 Hz (`docs/SIMULATION-CORE.md` §40). **Per faction at 60 Hz** (wins of all its matches): FactionGLA 134 of 160
(84 %), FactionChinaTankGeneral 49 of 80 (61 %), FactionAmerica 18 of 160 (11 %), FactionChina 1 of 160 (1 %), FactionAmericaAirForceGeneral 0 of 80. Per pairing (slot 1 / slot 2 /
open): China-GLA 0/20/0, 0/18/2, 0/17/3, 0/20/0 (Alpine Assault, Armored Fury, Bitter Winter, Death Valley); GLA-America 19/0/1, 19/0/1, 13/0/7, 8/0/12; ChinaTank-AirForce 11/0/9,
0/0/20, 18/0/2, 20/0/0; America-China 7/0/13, 0/1/19, 1/0/19, 10/0/10. **The outcome is decided by the faction, not by the match**, and America against China is open in 61 of 80
matches. That is not a statement about the balance of the original: it says that in this simulation the scripts of one faction lead to a winning army far more often than those of
another (§7.5 shows what the armies build), and it is the strongest reason not to make this AI the default yet.

**(b) The levels against each other**, both seat orders, 16 pairings × 3 seeds per seat order (96 matches per pair of levels; the order asked for at least 10 seeds - the computing
time of this session did not allow it, see §9):

| Pair | Matches | Stronger level wins | Weaker level wins | Open | Share of the decided matches | Share of all matches |
| --- | --- | --- | --- | --- | --- | --- |
| hard against easy | 96 | **85** | 0 | 11 | 100 % [96, 100] | 89 % [80, 94] |
| medium against easy | 96 | **80** | 1 | 15 | 99 % [93, 100] | 83 % [74, 90] |
| hard against medium | 96 | **40** | 18 | 38 | 69 % [55, 80] | 42 % [32, 52] |

**The levels order: easy < medium < hard.** Easy is far below the other two (it won one of 192 matches). Hard against medium is clear among the decided matches (the interval lies
above one half) but 38 of 96 stay open, and by seat: hard in slot 1 wins 25 : 8, hard in slot 2 wins 15 : 10 - the pairing (which faction sits where) weighs as much as the level.

**(c) The original AI against the built-in AI**, both seat orders (easy: 10 seeds with the original in slot 1 and 3 with it in slot 2; medium and hard: 3 seeds per seat order):

| Original level | Matches | Original wins | Built-in wins | Open | Original's share of the decided matches |
| --- | --- | --- | --- | --- | --- |
| easy | 208 | 0 | **199** | 9 | 0 % [0, 2] |
| medium | 96 | **45** | 22 | 29 | 67 % [55, 78] |
| hard | 96 | **51** | 13 | 32 | 80 % [68, 89] |

The built-in AI lies between the easy and the medium army. It plays in the same session with its own knowledge (only what it has seen); it is a comparison of two implementations
in this simulation, not with the original game. In the first version (before the fix of `<This Team>`, §6.2) the medium army lost to it 17 : 24.

**(d) The rate question.** `SimTool rate-distribution` on the two surveys of (a) (the tool and the limits of the built-in AI, `docs/SIMULATION-CORE.md` §40): **36 tests passed, 0
failed, 3 not testable**. The same seed at 30 and 60 Hz: the same outcome in 291 of 320 matches, the same outcome and an end within one second in **212 of 320** (the same end time
exactly in 200); the built-in AI: 317 and 287 of 320. First lasting difference of single matches (`rate-compare --original-diff A,B --rates 30,60 --difficulty medium --frames 27000
--fields`, every described field of every object after every frame): FactionChina against FactionGLA equal through frame 8,160 (272 s), then a shot-down MiG falls differently
(`JetSlowDeathBehavior.fallSpeed`; the same frame at 120 Hz); FactionAmerica against FactionChinaTankGeneral equal through frame 1,801 (60 s), then the shroud range of a spy
satellite ping differs (`DynamicShroudClearingRangeUpdate`). Both are modules of the world that the rate work lists as open, not the AI: the AI and the script VM decide on the frame
grid. A match of two original AIs is more exposed to them than one of the built-in AI because it uses more of the game (aircraft, special powers).

**(f) Determinism.** The same seed twice: 32 of 32 matches (16 pairings, seeds 1 and 2, a second process) with the same session hash, end tick, outcome and count of executed
actions. Every survey has as many different end hashes as matches. Save and restore: §6.5 (tests), and `ai-match` checks a continuation from a savegame on every run.
**Missing script types hit at run time: 0** in all 1,360 matches of the surveys above (`ai-survey` counts a match with one as failed; failed: 0).

### 7.3 Cost per tick (`SimTool perf`, quiet machine, 60 Hz, alternating runs)

| Run | Tick: mean / 99 % / maximum after warm-up | Scripts and AI: mean (99 %) | Allocation per tick | Share of the tick |
| --- | --- | --- | --- | --- |
| 8 original AIs (medium), Twilight Flame, 30 game minutes | 795 / 2,300 / 68,383 µs and 773 / 2,202 / 65,253 µs (two runs) | **118.5 (603) and 110.1 (564) µs** | 11.1 KB (7.4 KB of it in scripts and AI) | 14.9 % and 14.2 % | |
| 8 built-in AIs, same map and time | 867 / 2,349 / 185,758 µs and 846 / 2,232 / 172,837 µs | 36.3 (248) and 35.6 (242) µs | 7.8 and 8.0 KB (3.5 to 3.7 KB in the AI) | 4.2 % | |
| 2 original AIs (medium), Alpine Assault, America against China, 25 game minutes | 238 / 992 / 21,033 µs and 240 / 966 / 14,608 µs | 30.0 (218) and 31.6 (222) µs | 3.2 KB | 12.6 % and 13.2 % | |
| 2 built-in AIs, same | 151 / 636 / 29,691 µs and 151 / 577 / 27,381 µs | 8.4 (131) and 8.8 (145) µs | 2.0 KB | 5.6 % and 5.8 % | |
| 8 original AIs, before this order (commit `c97578e`: the tool, no change of the engine) | 850 / 2,122 / 67,226 µs | 199.0 (947) µs | 62.5 KB | 23.4 % | |

**The budget of this order - scripts and AI for 8 players at most 150 µs per tick at 60 Hz - is met: 110 to 119 µs** (before: 199 µs on the same run, and 1,123 µs in the
first version at 30 Hz). The aim "the order of magnitude of the built-in AI" is met in that sense and not in the strict one: the original AI costs **3.1 to 3.3 times** the built-in AI
for eight players (36 µs) and 3.6 times for two (30 against 8.6 µs). The whole tick of this match of eight original AIs is not more expensive than that of eight built-in AIs (773 to 795 against
846 to 867 µs), although it ends with more objects (405 against 211; neither match has a winner in 30 minutes); the two are different matches, and where the world's time goes in
each was not broken down. Allocation: 11.1 KB per tick against 7.9 KB; two Gen-2 collections
in the run of the original AI (the first tick allocates 1.6 MB: the script program and the base). The largest tick after warm-up is the world's in three of four runs (path
searches, 65 to 186 ms - the known maximum of `docs/PERFORMANCE.md` §6c) and once the scripts' (64 ms in one frame: a placement search of the AI). The 8-player match of the original
AI gives the same session hash in both runs (`7506227A1048DF7D`).

What was done, in this order: (1) what the original does and the engine did not (§1.6: the two condition memories) - behaviour, own commit `b833526`; (2) pure work that changes no
result (`a3dfb5f`, `fe1d379`): team instances per prototype instead of a walk over every team per script; counters and flags looked up by hash; "active, due, for this difficulty"
asked before a script is set up; prototypes, generic script units and side info looked up once; team events, team builds and the generic scripts without LINQ, copies and
allocations; counting objects of one template by reference; no sort of the world per hunting unit. **Proof that (2) changes nothing:** the same survey before and after - 80 matches
medium against medium and 32 hard against easy - 112 of 112 with identical session hash, end tick, outcome and count of executed actions, twice (once per commit), plus an 8-player
run with identical session hash and world hash chain. The mission VM shares the code: reference manifest and objective driver as in §7.1.

Where the rest goes (8 hard AIs, `--script-detail`, which itself costs two clock reads per condition): of 162 µs per tick with the detail on: the script loop 62.6 (conditions 37.7 - `PLAYER_HAS_OBJECT_COMPARISON` 8.2, `FLAG` 4.6, `BUILT_BY_PLAYER` 4.4,
`SKIRMISH_SPECIAL_POWER_READY` 4.3, `SKIRMISH_VALUE_IN_AREA` 4.3, `PLAYER_POWER_COMPARE_PERCENT` 3.9; actions 4.3), the AI's team building 21.7 (production conditions of about a
hundred team prototypes per player every two seconds), the team builds 20.4 (every open order looks for a recruit and a factory each frame), the generic team scripts 13.4, team
queues 7.9, housekeeping 7.1, team events 6.7, skills 6.0, base building 5.7, followers 5.5. The hard level costs more than the medium one because it has about three times the
teams. What would lower it further without changing a result: an index of the production conditions by the flags and counters they read, and looking for recruits only when a unit
or a team changed - neither done.

### 7.4 What the rules of the C++ half changed (16 pairings × 5 seeds, medium against medium at 60 Hz; 32 matches hard against easy)

| State | Medium against medium: decided of 80 | Hard against easy (hard / easy / open) |
| --- | --- | --- |
| before this order (`39e41df`) | 52 | 75 / 0 / 5 of 80 with hard in slot 1, 70 / 0 / 10 of 80 with hard in slot 2 |
| with the condition memories (`b833526`) | 52 | 30 / 0 / 2 of 32 |
| with `<This Team>` for a team's own scripts (`b3c4b4e`) | 52 | 29 / 0 / 3 of 32 |
| with the common victim and reinforcements (`8c17f6e`) | 49 | 29 / 0 / 3 of 32 |

The share of decided matches did not move (52, 52, 52, 49 of 80; the difference is within what five seeds resolve). What moved is the strength against the built-in AI (§7.2 c: from
17 : 24 to 45 : 22 for the medium army) and the game itself: matches got shorter (median 10.2 → 9.3 minutes) because the teams leave the base when their create script says so.

### 7.5 What a match looks like in numbers (`SimTool ai-census … --ai original --difficulty <level>`, mirror matches, 16 pairings × 2 seeds, medians per player)

| | easy | medium | hard |
| --- | --- | --- | --- |
| First barracks (game second) | 72 | 23 | 21 |
| First war factory | 82 | 60 | 96 |
| First own combat unit within 400 of an enemy structure | 216 | 128 | 139 |
| Attack waves (such an arrival after 30 s without one) | 2 | 2 | 3 |
| Teams activated | 4 | 9 | 26 |
| Teams disbanded (build time ran out) | 0 | 4 | 7 |
| Structures built | 7 | 14 | 26 |
| Units trained in the first ten minutes | 18 | 40 | 73 |
| Units trained in the match | 30 | 50 | 118 |
| Objects lost | 35 | 48 | 120 |
| Money after ten minutes | 25,312 | 300 | 3,124 |
| Money at the end | 24,145 | 1,535 | 5,455 |
| Players that never reached an enemy structure (of 64) | 12 | 4 | 5 |
| Special powers used in the 32 matches | 0 | spy satellite 280, particle cannon 4, Scud storm 1 | Black Lotus capture 838, spy satellite 301, particle cannon 11, Scud storm 9, neutron missile 4 |

The easy army builds a barracks after 72 seconds, trains 18 units in ten minutes and **sits on 25,000 credits**: it does not spend what it collects. The medium army spends
everything; the hard army builds almost twice the structures and units and gets the money gifts of its scripts. What the armies are made of in the first ten minutes (mean per
match, medium): GLA 22.9 Scorpions, 9.6 tunnel defenders and 7.3 Quad Cannons; China 12.4 Red Guards, 8.1 Gattling tanks and 4.9 Battlemasters; America 14.5 Rangers, 9.2 Humvees and
4.4 missile defenders; the tank general 10.1 Battlemasters and 8.0 Gattling tanks; the air force general 7.9 Chinooks, 6.5 Rangers and 3.8 Humvees. On the hard level GLA trains 56.1
Scorpions in ten minutes, China 15.1 Battlemasters. **GLA fields about four times the tanks of China at the same time** - which is what the faction table of §7.2 shows. Whether the
original game produces the same ratio (and wins with it) has not been compared; if it does not, the cause is in what the simulation lets the GLA scripts afford (build times, costs,
the 1,600 and 4,000 credit gifts, salvage), and that is the first thing to find before this AI becomes the default.

**Anatomy of an open match** (Alpine Assault, FactionChina against FactionAmerica, medium, seed 3, 14 game minutes, `ai-match … --trace 3600`): China loses its command centre and
both dozers around minute 5 to 10 and can build neither again (dozers come from the command centre); it keeps 9 structures and 6 supply trucks and ends with 14,100 unspent credits.
America has 16 structures and 18 units, but 12 of them stand in teams that were never completed and so never activated (`USA N 4TH Wave Fast Attack`: 7 of 10 units) - it has 740
credits, the warehouses of the map are nearly empty (259 boxes left) and 14,433 training orders were rejected for lack of money; its completed teams are guards. Nobody attacks,
nobody is eliminated. The same picture - a broke winner whose waves wait for units it cannot pay, against a loser that cannot rebuild - explains the open matches of America against
China. In the reference a team whose build time has run out and whose minimum is built goes anyway (`AIPlayer::checkQueuedTeams`); the same rule is implemented here
(`UpdateBuildingTeam`), so the difference lies before that: which units count as the minimum, or how much money the original AI has at that time. Not traced further.

### 7.6 The first version for comparison (8 October 2026, 30 Hz, state `3c9e267`)

Medium against medium 49 of 80 decided; hard against easy 87 of 96 for hard, 0 for easy; medium against the built-in AI 17 : 24 with 23 open; 8 original AIs 1,123 µs of scripts and
AI per tick at 30 Hz (the VM then also ran on every tick at 60 Hz), 2,790 µs per tick in total.

## 8. Proposals beyond the original (each only as an option or a kind of AI of its own, never in "original")

| # | Proposal | Status and reason | Measurable by |
| --- | --- | --- | --- |
| 1 | **Generals on maps without their template side** get scripts | **implemented as a named option** (`LibraryScriptsForMissingSides`, `--library-for-missing-sides`): the library's template player of the general's own side, not the base side (the base side's teams name units the general cannot train). Off by default. Effect not surveyed | share of decided matches on `Armored Fury`, `Scorched Earth` |
| 2 | **Script evaluation on demand** | the cost aim was reached without it (§7.3) by making each evaluation cheap; an index of scripts by the counters and flags they read remains possible and would need the same proof | `perf --ai original`, identical session hashes |
| 3 | **Missing rules of the C++ part** | done: common victim, reinforcements, the two condition memories, `<This Team>`; open: bridge repair, rebuild hole, cluster mines (§6.2 item 9) | surveys of §7 |
| 4 | **Economy without warehouses** as a kind "improved": when the supplies in reach run out, prefer the money structures of the side (the build list contains them) instead of waiting for the script sequence | open matches end with empty warehouses and money near zero (§7.5) | share of open matches, money over time (`--trace`, `ai-census`) |
| 5 | **Army value instead of fixed waves** as a kind of AI of its own on the framework of the teams | the built-in AI composes by exchange value since 8 October; the original's waves are fixed | survey kind against kind |
| 6 | **Lift the built-in `SkirmishAi` onto the same state framework** (`Describe`/`Load`/hash), so that register E11 is settled for both AIs | E11 is solved for the original AI, open for the built-in one - this is the work of phase 24 if the built-in AI stays the default | save/load test as §6.5 |
| 7 | **Sight instead of omniscience** as an option of the original AI (place only on what is explored, choose the enemy only by what was seen) | the original knows no fog; fairer for humans | survey against the built-in AI |
| 8 | **An attack with what there is**: a team that has waited longer than a limit for units it cannot pay goes with what it has (also below its minimum) | the anatomy of an open match in §7.5 | share of open matches |

## 9. Open and not verified

- **No human has seen a match of this AI.** Whether bases look sensible, waves arrive as waves, defences stand right, a hard army *feels* hard and fair, and whether it looks like
  the original: unknown. The demo command is in §6.6.
- **No comparison with the running original game** (build times, wave times, win shares per faction). "Like the original" means here: implemented after the source code, with the
  deviations of §6.2.
- **The win shares per faction** (§7.2 a: GLA 84 %, China 1 %) and **the open matches** (37 %) are measured and not explained beyond §7.5.
- **Seeds per survey:** the level and kind-against-kind surveys have 3 seeds per seat order (96 matches per pair) instead of the 10 the order asked for; the medium-against-medium
  surveys have the full 20 seeds at both rates.
- **The mission VM has the `<This Team>` defect** that was fixed for the skirmish AI (§6.2); fixing it changes manifest runs.
- **Cost:** the maximum of a tick is not addressed (it is the world's: path searches); `--all-map-objects` stays expensive.
- More than two players: the 8-player runs of §7.3 (no missing types, deterministic by session hash on two runs); no survey. Teams/allies: only the attachment is tested, no
  match. Human in a slot: the first slot only.
- 120 Hz: the tests of §6.5 and one `rate-compare` pairing; no survey.
- `ai-survey` columns for the original AI: `commands` is the count of executed script actions, `firstAttack` the first tick with an own combat unit closer than 400 to an enemy
  structure (the built-in AI reports its own wave count there) - not comparable between the two kinds of AI.
- `docs/GAME-SHELL.md`, `docs/SPECTATOR-MODE.md`, `docs/SCRIPT-ENGINE.md` and `docs/SIMULATION-CORE.md` do not yet describe the original AI in the client, the three armies of the
  setup screen and the new options (other packages are translating those documents at the moment; this document is the reference until they are brought up to date).

## Decision basis: which AI is the default AI?

The question for the project owner before phase 24: which AI's state becomes part of the hashed, saved session state of the product - and therefore which AI every reference value,
every demo and the multiplayer slots use.

| | Built-in `SkirmishAi` | Original AI (`ScriptedSkirmishSession`) |
| --- | --- | --- |
| **Fidelity** | none: an own development; plays "a" skirmish | the scripts of the original run as shipped (0 missing types in 1,360 matches); the C++ half is rewritten after the source with the deviations of §6.2; **not compared with the running game** |
| **Difficulty levels** | one strength | three, and they order: easy < medium < hard (hard against easy 85 : 0, medium against easy 80 : 1, hard against medium 40 : 18 of 96 each) |
| **Decided matches** (16 pairings × 20 seeds, 25 minutes, 60 Hz) | 301 of 320 (94 %) | **202 of 320 (63 %)** |
| **Balance between factions** | not assessed; ChinaTank beats AirForce 70 of 80 (30 Hz, round 3) | **decided by the faction**: GLA wins 84 % of its matches, China 1 %, AirForce 0 % |
| **Strength** | between the easy and the medium army | medium beats the built-in AI 45 : 22, hard 51 : 13, easy loses 0 : 199 |
| **Cost** (8 players, 60 Hz, scripts/AI per tick) | 36 µs mean, 245 µs at 99 %; 7.9 KB allocated per tick | 110 to 119 µs mean, 564 to 603 µs at 99 %; 11.1 KB per tick - **3.1 to 3.3 times** the built-in AI, within the budget of 150 µs |
| **State in hash and savegame** | **no** (register E11): memory, order tables and its random stream are outside; `CreateSave()` throws. Phase 24 has to build this | **yes**, tested at every tick and inside a frame at 60 and 120 Hz; nothing to build |
| **Replay** | a match is a command replay | no: scripts write into the world; the truth is the state (as for a mission). A lockstep session has to run the scripts on every machine, which they do deterministically |
| **Rate equality** (same seed at 30 and 60 Hz: same outcome / end within 1 s, of 320) | 317 / 287 | 291 / 212; `rate-distribution` 36 passed, 0 failed |
| **Knowledge** | only what it has seen and remembers | everything (the original knows no fog) |
| **Map objects** | warehouses only | structures and warehouses; everything only with `--all-map-objects` (7.5 ms per tick) |
| **Where it runs** | everywhere (default) | tools, tests, client (`--ai original`), spectator, shell setup (three armies) - all opt-in |
| **What is missing** | levels; E11 | bridge repair, rebuild hole, cluster mines; the rank system (science points on a timer); prerequisites apply; an explanation for the faction table and the open matches; a human's eye |

**What switching the default would change.** The 16 AI runs, the three 8-player runs and the 64-map skirmish overview of `baselines/reference-manifest.json` are matches of the
built-in AI: all of them get new values (or stay as "built-in" runs beside new "original" runs - then nothing changes and the manifest grows). The reference match, the stress test
and the 27 mission runs are untouched. Tests that name the built-in AI's behaviour (`Tests.Skirmish.cs`, the rate-equivalence group `ai`, the distribution comparison of
`docs/SIMULATION-CORE.md` §40, `Game.Tests` suites that play a default match, `check-spectator` pass 1) keep working as long as the built-in AI stays selectable; their role changes
from "the product" to "the other AI". Documents: `CLAUDE.md`/`AGENTS.md`, `docs/SIMULATION-CORE.md` §19/§36/§40, `docs/AI-OBSERVED-2026-10-08.md`, `docs/SPECTATOR-MODE.md`,
`docs/GAME-SHELL.md` (the setup screen then always offers three armies), `docs/MULTIPLAYER-PLAN.md` (E11 closed for the default), `docs/handoffs/HANDOFF-PHASE24.md`. The commands-only
agent of the missions uses the built-in AI and would keep it.

**Recommendation: do not switch the default now; build phase 24 for a session whose AI state is an opaque, hashed part of the session - and treat the original AI as the first
tenant of that slot.** Reasons:

1. *The original AI is not ready to be the product:* 37 % of its matches do not end in 25 minutes and the outcome is fixed by the faction. Until that is explained - a defect of the
   simulation under the scripts, or the original's own balance - a default switch would put a known unknown into every reference value.
2. *The work phase 24 would do twice is small on the original's side.* Its state is already in hash and savegame; the open E11 work is the built-in AI's. Phase 24 can therefore
   define the session state with "AI state: a described, hashed block per session" and prove it with `ScriptedSkirmishSession` today, without deciding the default. If the owner
   later keeps the built-in AI, proposal 6 (§8) is the remaining work; if he switches, nothing is left to do.
3. *The original AI already gives the product what the built-in one cannot:* three levels that order, and a medium and hard opponent that beat the built-in AI. That is an argument
   for making it **selectable for a human now** (it is: `--ai original`, the setup screen with the option) and for the owner to look at it - not yet for making it the measure of
   every test.
4. *What decides in the end is what only a human can judge* (below), and nobody has looked.

If the owner wants the switch nevertheless, the levels and the saved state are the reasons that carry it; the open matches and the faction table are the price.

**What only a human can judge, and how to see it:**

- Does a hard army feel hard and fair, an easy one beatable but alive? `dotnet run --project src/CnCNew.Client.Stride -c Release -- --match "Maps/Alpine Assault/Alpine Assault.map"
  --content-dir game/content --faction FactionAmerica --opponent FactionGLA --ai original --difficulty hard` (and `easy`, `medium`).
- Does it look like the original - base layout, the order of the buildings, waves along the approach paths, defences on the ring?
  `pwsh scripts/demo-spectator.ps1 -Ai original -DifficultyA hard -DifficultyB easy` (keys `F1`/`F2` follow each AI, `O` shows the whole map, `P`/`U`/`L`/`E` the panels).
- Is the faction table real? Watch `pwsh scripts/demo-spectator.ps1 -Ai original -Map "Alpine Assault" -FactionA FactionChina -FactionB FactionGLA -Seed 3 -Speed 4` and
  `… -FactionA FactionAmerica -FactionB FactionChina -Seed 3 -Speed 4` (the open match of §7.5).

**The exact steps of the switch, if the answer is yes** (one commit plus one manifest commit):

1. `LiveMatch.Create`/`MatchSetup` (`src/CnCNew.Game/Live/LiveMatch.cs`): a setup without `Ai` means the original AI on the medium level for every computer slot;
   `GameShell.SkirmishAi` defaults to `Original` (the setup screen then offers the three armies; rewrite its golden hashes in `tests/CnCNew.Game.Tests/baselines/screens.txt` and
   the test `LocalizedSetupText`, which asserts the single "Medium Army" entry).
2. `SimTool`: `OriginalAi.Requested` returns true unless `--ai builtin` is given, so that `ai-match`, `ai-survey`, `ai-census` and `perf` play the original AI by default;
   `ReferenceManifest` plays its AI pairings, 8-player runs and the skirmish overview through `OriginalAi.Start` (keep the built-in runs under new ids if both are wanted).
3. `scripts/demo-spectator.ps1`, `scripts/check-spectator.ps1`, `scripts/demo-skirmish.ps1`: default `-Ai original`.
4. Commit ("feat: the original skirmish AI is the default AI"), then in a commit of its own with that cause:
   `dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest write <content> baselines/reference-manifest.json --set full`, run twice and compared.
5. Bring the documents named above up to date and close register E11 for the default AI in `docs/MULTIPLAYER-PLAN.md`.

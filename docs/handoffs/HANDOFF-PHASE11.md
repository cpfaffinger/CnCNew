# N+1 handoff: Finish Phase 9 (continuation) – remaining script families, campaign shell polish, mission HUD

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, `README.md`, `docs/ARCHITECTURE.md`,
`docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§9 "Status" and "Known limits"), `docs/SCRIPT-ENGINE.md` (especially §8/§9),
`docs/CAMPAIGN-AND-SAVEGAMES.md`, `docs/MAP-SCRIPTS.md`, `docs/SIMULATION-CORE.md` (§12, §19–§25), `docs/GAME-RULES.md` §10/§11,
`docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/PRESENTATION-INPUT.md`. `docs/handoffs/HANDOFF-PHASE9.md` and `docs/handoffs/HANDOFF-PHASE10.md` remain as
history; their numbers are outdated, their open items are – revised and with the current state – below.

This brief still continues `IMPLEMENTATION-PLAN.md` §9. **§9 is not yet completed:** the acceptance demands "all
skirmish maps can be started and ended, every campaign mission passes its essential success and failure paths, save/load continues
an identical simulation state". Skirmish start/end and save/load are now verified; **the rest – missions
pass their success/failure paths on the path intended by the script, not just by the elimination probe – is the core of this brief.**

## Honest status (starting point, as of 1 October 2026)

Four parallel packages have been merged since `docs/handoffs/HANDOFF-PHASE10.md` (commits `7579d8f`…`b5d618d` shell connection, `01d3d72`…`c4d1128`
collision, `849aa07`…`1cf7e84` script command-button abilities, `fb17f71`…`3be6684` build/rule limits; four merge commits
`b5d618d`/`d96ca36`/`bc0ca37`/`6daba54`). Conflicts in `baselines/compatibility.json`, `docs/SIMULATION-CORE.md` (§23–§25),
`docs/CAMPAIGN-AND-SAVEGAMES.md` and two hard-coded test expectations were resolved by hand; the complete test matrix (twelve
test programs, synthetic and with `--content-dir`) is green.

**Finished and measured:**

- **Collision**: `ToppleUpdate`, `StructureToppleUpdate`, `HeightDieUpdate`, real `CrushDie`/`SquishCollide` via
  footprint overlap, a deliberately simple local avoidance movement (`docs/SIMULATION-CORE.md` §23).
  Instantiability thereby 70.6 % → 75.1 %.
- **Script command-button abilities**: `NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)`, `SpecialAbilityUpdate` as an honest shell,
  selectable local side for maps without `playerIsHuman` (`docs/SIMULATION-CORE.md` §24, `docs/SCRIPT-ENGINE.md` §5/§7).
- **Build/rule limits**: prerequisite/ability check in `QueueProduction`, `MaxSimultaneousOfType`, `OverchargeCommand`,
  reduced `CaptureCommand`, veteran `ArmorSet`/`WeaponSet`, height line of sight for detectors, the missing read APIs
  (`CheckConstruct`, `ProductionUpdate.Queue`, `AvailableOnFrame`, `UpgradeMuxModule.Executed`, `IsDock`); `CommandTypes.Version`
  9 → **10** (new codes 28/29, rejection reasons 35–38). All 64 skirmish maps with ≥2 start positions start and end without errors
  (`SimTool skirmish-sweep`); the 16-map AI overview is re-measured (`docs/SIMULATION-CORE.md` §25). Skirmish savegames
  are explicitly not supported (`SkirmishMatchSession.CreateSave()` throws `NotSupportedException`, tested) – replay/checkpoint
  already covers finished/running skirmish games.
- **Campaign shell**: main menu → faction/challenge selection → difficulty → real mission with real briefing text; load list,
  saving in the game, mission completion (victory/defeat); `ScriptOutput` playback (camera, text, radar) actually plays and
  reports back to the VM via `CompletePresentation`/`SelectNamed`/`SignalUiInteraction` – with a test against a real, timed
  camera movement sequence (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7).

**Freshly measured after today's merge of all four packages** (to be confirmed anew before this phase, in particular `CampaignTool audit`,
which has not yet seen the build/rule limit changes – the last measurement in `docs/SCRIPT-ENGINE.md` §9 comes only from the
script command-button package):

- Module catalogue **91 of 205** implemented; instantiability **1,624 of 2,115 (76.8 %)**; module uses covered
  **15,843 of 19,553 (81.0 %)**; compatibility baseline **760** entries (`IniTool compat-baseline`, rewritten, checked).
- Retail reference match (`SimTool reference-match`): winner FactionGLA at tick 19,200, final hash `28BE35C1174E369F`; two fresh runs
  and a continuation from tick 9,750 yield identical hashes (winner and hash change compared with earlier states because the
  random production of the scenario now respects prerequisites – not a regression finding, see `docs/SIMULATION-CORE.md` §25).
- `dotnet build CnCNew.slnx`: 0 errors, 21 known warnings with a **clean** `CnCNew.Client.Stride` `bin`/`obj`.
- All twelve test programs from `CLAUDE.md` pass, synthetic and with `--content-dir .local/phase2-import-a`.

## Not done (brief of this phase)

1. **Decide the `SKIRMISH_*` script family** (postponed since `docs/handoffs/HANDOFF-PHASE10.md`, still open): by far the
   most frequent missing conditions on the challenge maps (`SKIRMISH_PLAYER_HAS_UNITS_IN_AREA` 37,227 hits,
   `SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER` 37,220, `SKIRMISH_SPECIAL_POWER_READY` 36,907, `SKIRMISH_VALUE_IN_AREA` 15,432 –
   `docs/SCRIPT-ENGINE.md` §9). Two viable ways, both acceptable, but **make the decision and document it**:
   a) port the `Skirmish` family of the conditions/actions (source: `GeneralsMD/.../ScriptEngine/ScriptConditions.cpp`/
   `ScriptActions.cpp`, search for `SKIRMISH_`); b) use the existing, already working `SkirmishAi` as a replacement for
   the challenge opponent AI (check whether it can already be hooked into missions today, or what is missing).
2. **The remaining, most frequently missing actions** (order by hits, `docs/SCRIPT-ENGINE.md` §9, to be re-measured after the
   build/rule limits merge, since the ranking can shift):
   - `TEAM_GARRISON_SPECIFIC_BUILDING`/`TEAM_GARRISON_NEAREST_BUILDING`/`TEAM_/NAMED_GARRISON_*` family, `*_EXIT_*`,
     `NAMED_ENTER_NAMED` (garrison – uses the existing `GarrisonContain`/`TransportContain` infrastructure, but without
     script-controlled triggering).
   - `MAP_REVEAL_ALL_PERM`/`MAP_REVEAL_AT_WAYPOINT`/`MAP_SHROUD_*` (needs permanent reveal in
     `src/CnCNew.Simulation/World/SimulationWorld.Vision.cs` – today only tick-based vision, no "permanently revealed" mark;
     a small additive extension there, no rebuild).
   - `BUILD_TEAM`/`TEAM_MERGE_INTO_TEAM`/`RECRUIT_TEAM` (must hook into the existing production/team infrastructure, now with
     the new prerequisite checks from Phase 10 – check whether a script-triggered build should respect the same rules as
     a player command, presumably yes).
   - `WAREHOUSE_SET_VALUE`, `TEAM_SET_OVERRIDE_RELATION_*`, `OBJECT_ALLOW_BONUSES`,
     `PLAYER_ENABLE_BASE_CONSTRUCTION`/`PLAYER_ENABLE_UNIT_CONSTRUCTION` (needs a build release switch per player, which does not
     exist today – a small additive addition to `SimulationWorld`), `PLAYER_AFFECT_RECEIVING_EXPERIENCE`,
     `PLAYER_PURCHASE_SCIENCE`.
   - `NAMED_USE_COMMANDBUTTON_ABILITY_(AT_WAYPOINT|USING_WAYPOINT_PATH)`, `TEAM_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT`,
     `TEAM_USE_COMMANDBUTTON_ON_NEAREST_*` (path variants of the command-button abilities built in Phase 10).
3. **Missing conditions beyond the `SKIRMISH_*` family**: `PLAYER_TRIGGERED_SPECIAL_POWER`/`*_COMPLETED_*`/`*_MIDWAY_*` (needs
   a `SpecialPowerUsed` event – check what the simulation already reports as a `SimEvent` today when a special power is triggered),
   `TYPE_SIGHTED`, `NAMED_DISCOVERED`/`TEAM_DISCOVERED` (vision/discovery conditions, presumably use `VisionMap` directly).
4. **Clarify unknown command buttons from `docs/SCRIPT-ENGINE.md` §9**: `Command_GLAInfantryJarmenKellSnipeVehicleAttack` (40 hits,
   presumably a `FIRE_WEAPON`/`SWITCH_WEAPON` ability, not a special power) and three `Command_Upgrade*` buttons (`PLAYER_UPGRADE`,
   not `SPECIAL_POWER`) – `CommandCatalog.TryGetSpecialPowerAbility` is to treat these cases cleanly as "another kind of command" instead of as
   an error, with its own counted path.
5. **Reinforcement with transport** (`teamTransport`, `teamStartsFull`, `CINE_*` aircraft, parachute) and **real formations/
   attack movement** (`*_FOLLOW_WAYPOINTS` with `asTeam`) – blocks several missions completely (`docs/SCRIPT-ENGINE.md` §9:
   reinforcement transport is not simulated).
6. **Generals' Challenge general selection in the shell**: `ChallengeSelectScreen` must still map the chosen general onto the side name
   of the respective map (different per map, e.g. `PlyrLaserGeneral` → `GC_LaserGeneral`) and pass it through via
   `StartCurrentMission(humanSideName:)` (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7, "Still open"). The engine side is in place.
7. **A mission HUD** (camera/selection/command bar) in parallel with `LiveMatch`/`MatchController`, which stay skirmish-typed.
   `MissionPresentationDirector`/`MissionUiBridge`/`MissionPauseMenu` are deliberately headless testable, renderer-neutral building blocks
   for it (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7), but not a complete replacement: a campaign mission can today be started and ended from the
   shell, but not played with a real command bar/production. Check whether `MatchController` is extended for that
   or a `MissionController` of its own is built.
8. **`PresentationLatencyTicks` is missing as a parameter** in `CampaignController.StartCurrentMission` (known limit from Phase 10,
   `docs/CAMPAIGN-AND-SAVEGAMES.md` §7) – a small additive parameter would make sense in case a future host does not bring a
   `MissionPresentationDirector` along.
9. **After every action/condition group:** update `ScriptCoverage`, rewrite `docs/SCRIPT-ENGINE.md` §4/§5/§8/§9
   (table, percentages, most frequent gaps), run `CampaignTool audit --ticks 900` and `playthrough` again, record before/
   after numbers. **Goal:** each of the 24 campaign maps reaches victory and defeat on the path intended by its own
   script (not just by `MissionProbe` elimination); where that does not succeed after best efforts, name honestly why
   (missing condition/action, map overlay missing, etc.), do not silently stop at the elimination probe.

## Non-negotiable rules (unchanged, see `CLAUDE.md`)

- Unknown keys/modules/enums/references never fall back silently to defaults; every case is explicit, counted,
  documented and secured against `baselines/compatibility.json`. New script actions/conditions: enter in `ScriptCoverage`
  (test `ScriptSupportTableIsConsistent` checks the names), cite the source function (`ScriptActions.cpp`/
  `ScriptConditions.cpp`, never wholesale), test in `tests/CnCNew.Simulation.Tests/Tests.Scripting.cs`, line in
  `docs/SCRIPT-ENGINE.md` §4/§5 (deviations in §8). New modules: catalogue schema, binding, test, entry in
  `docs/SIMULATION-CORE.md`, baseline (`IniTool compat-baseline`) – changes to it are subject to review.
- Determinism: sorted collections, no wall-clock time, no `float`/`double`, no `Random.Shared`; `SourceRules` applies to the
  whole script VM. Every new script state belongs in `ScriptEngine.Describe`/`Load`, otherwise a restored
  mission deviates (the continuation test catches that).
- `TreatWarningsAsErrors=true`; 21 explained warnings are the reference value for a **clean** build (see `CLAUDE.md`); every
  other new warning is an error until it is explained.
- Snapshot/replay/mission formats: changes increase the respective version (`WorldSnapshot` 7, replay 7, `MissionSnapshot` 2,
  `CommandTypes.Version` 10); older ones are rejected with a clear message.
- Third-party code only to be read, not copied; cite every ported action/condition individually with file and function. That applies
  especially to the `SKIRMISH_*` family, in case you decide for port instead of replacement.
- Always re-measure inventory numbers, never adopt them from this document – in particular `CampaignTool audit`, which has not yet
  been re-run after the build/rule limits merge.
- Work only locally, no remote, push nothing; several commits each closed in subject matter; working tree clean at the end; at the end
  write and commit `docs/handoffs/HANDOFF-PHASE12.md`.
- **Preserve line endings:** the repo is predominantly LF, individual files (`README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`,
  `docs/IMPLEMENTATION-PLAN.md` among others) are CRLF; when editing by script use `newline=''`, check `git diff --stat`.
- In practice: with parallel work on several agents/worktrees `ScriptEngine.Actions.cs`/`ScriptEngine.Conditions.cs` share
  the same switch statement – **that is the most frequent merge conflict of this phase.** Either work one after the other or define
  exactly beforehand which agent gets which action/condition block, and after merging reconcile `baselines/compatibility.json`
  as well as the hard-coded module list in `tests/CnCNew.Data.Tests/Program.cs` (`ModuleCatalogIsExplicitAndConsistent`) by hand
  again (both have already come into conflict twice in this phase). Run large edit scripts as a file in the scratchpad
  instead of complex heredocs; a running audit process locks the Release DLLs; `tools/CnCNew.UiRenderTool` stays
  outside the solution.

## Final report

Numbers with commit hashes, implemented script actions/conditions with source references, the `SKIRMISH_*` decision and its
justification, determinism/test/continuation results, the `CampaignTool audit` table before/after (in particular how many
missions now reach victory/defeat on the intended path instead of by the elimination probe), instantiation rate and
blocker list before/after, known limits, `git status` and the confirmation "no remote, nothing pushed".

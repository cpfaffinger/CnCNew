using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>Phase 11: the <c>SKIRMISH_*</c> conditions, sight and special-power conditions, map reveal, containers, build switches, team merge/build and button variants.</summary>
static partial class Tests
{
    private static Entity? NamedIn(MissionSession session, string name)
        => session.Engine.NamedObjects.Where(item => item.Key == name).Select(item => session.World.FindEntity(new EntityId(item.Value))).FirstOrDefault();

    public static void ScriptSkirmishConditions()
    {
        // PlayerA has a soldier inside LeftArea (x 0..200); PlayerB has one far outside it, within gun range of nobody.
        var session = StartMission([
            Scr.Script("InArea", [Scr.And(Scr.If("SKIRMISH_PLAYER_HAS_UNITS_IN_AREA", Scr.Side(PlayerA), Scr.Area("LeftArea")))], [Scr.Do("SET_FLAG", Scr.Flag("aInside"), Scr.Bool(true))]),
            Scr.Script("BInside", [Scr.And(Scr.If("SKIRMISH_PLAYER_HAS_UNITS_IN_AREA", Scr.Side(PlayerB), Scr.Area("LeftArea")))], [Scr.Do("SET_FLAG", Scr.Flag("bInside"), Scr.Bool(true))]),
            Scr.Script("BOutside", [Scr.And(Scr.If("SKIRMISH_PLAYER_IS_OUTSIDE_AREA", Scr.Side(PlayerB), Scr.Area("LeftArea")))], [Scr.Do("SET_FLAG", Scr.Flag("bOutside"), Scr.Bool(true))]),
            Scr.Script("AOutside", [Scr.And(Scr.If("SKIRMISH_PLAYER_IS_OUTSIDE_AREA", Scr.Side(PlayerA), Scr.Area("LeftArea")))], [Scr.Do("SET_FLAG", Scr.Flag("aOutside"), Scr.Bool(true))]),
            Scr.Script("NoArea", [Scr.And(Scr.If("SKIRMISH_PLAYER_IS_OUTSIDE_AREA", Scr.Side(PlayerB), Scr.Area("NoSuchArea")))], [Scr.Do("SET_FLAG", Scr.Flag("ghost"), Scr.Bool(true))]),
            Scr.Script("Exists", [Scr.And(Scr.If("SKIRMISH_NAMED_AREA_EXIST", Scr.Side(PlayerA), Scr.Area("LeftArea")))], [Scr.Do("SET_FLAG", Scr.Flag("exists"), Scr.Bool(true))]),
            Scr.Script("Value", [Scr.And(Scr.If("SKIRMISH_VALUE_IN_AREA", Scr.Side(PlayerA), Scr.Compare(Scr.GreaterEqual), Scr.Int(25), Scr.Area("LeftArea")))], [Scr.Do("SET_FLAG", Scr.Flag("value25"), Scr.Bool(true))]),
            Scr.Script("TooMuch", [Scr.And(Scr.If("SKIRMISH_VALUE_IN_AREA", Scr.Side(PlayerA), Scr.Compare(Scr.GreaterEqual), Scr.Int(26), Scr.Area("LeftArea")))], [Scr.Do("SET_FLAG", Scr.Flag("value26"), Scr.Bool(true))]),
            Scr.Script("Attacked", [Scr.And(Scr.If("SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER", Scr.Side(PlayerA), Scr.Side(PlayerB)))], [Scr.Do("SET_FLAG", Scr.Flag("aHit"), Scr.Bool(true))]),
            Scr.Script("AttackedB", [Scr.And(Scr.If("SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER", Scr.Side(PlayerB), Scr.Side(PlayerA)))], [Scr.Do("SET_FLAG", Scr.Flag("bHit"), Scr.Bool(true))]),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Mine"), Placed("BetaSoldier", 550m, 380m, "teamB", "Theirs")]);
        var engine = session.Engine;
        Run(session, 5);
        Check.That(engine.FlagValue("aInside") && !engine.FlagValue("bInside"), "HAS_UNITS_IN_AREA sees the soldier inside, not the one outside.");
        Check.That(engine.FlagValue("bOutside") && !engine.FlagValue("aOutside"), "IS_OUTSIDE_AREA is the negation when player and area exist.");
        Check.That(!engine.FlagValue("ghost"), "An unknown area makes IS_OUTSIDE_AREA false, as in the reference (area must exist).");
        Check.That(engine.FlagValue("exists"), "NAMED_AREA_EXIST finds the trigger area.");
        Check.That(engine.FlagValue("value25") && !engine.FlagValue("value26"), "VALUE_IN_AREA sums the build cost (25) of the live objects in the area.");
        Check.That(!engine.FlagValue("aHit") && !engine.FlagValue("bHit"), "Nobody has attacked anybody yet.");
        Check.That(engine.MissingConditionHits.Count == 0, "None of these conditions is counted as missing: " + string.Join(", ", engine.MissingConditionHits.Keys));

        // Bring the enemy into gun range: both sides were hurt by the other, and the history never resets.
        var attack = StartMission([
            Scr.Script("Attacked", [Scr.And(Scr.If("SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER", Scr.Side(PlayerA), Scr.Side(PlayerB)))], [Scr.Do("SET_FLAG", Scr.Flag("aHit"), Scr.Bool(true))]),
            Scr.Script("AttackedB", [Scr.And(Scr.If("SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER", Scr.Side(PlayerB), Scr.Side(PlayerA)))], [Scr.Do("SET_FLAG", Scr.Flag("bHit"), Scr.Bool(true))]),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Mine"), Placed("BetaSoldier", 140m, 200m, "teamB", "Theirs")]);
        Run(attack, 60);
        Check.That(attack.Engine.FlagValue("aHit") && attack.Engine.FlagValue("bHit"), "After a firefight each side has been attacked by the other.");
    }

    public static void ScriptSpecialPowerAndSightConditions()
    {
        var session = StartMission([
            Scr.Script("Ready", [Scr.And(Scr.If("SKIRMISH_SPECIAL_POWER_READY", Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.SpecialPower, "AlphaInstantPower")))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("ready"))]),
            Scr.Script("Use", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY", Scr.Unit("Hero"), Scr.Ability("Cmd_AlphaPower"))], oneShot: true, delaySeconds: 1),
            Scr.Script("Triggered", [Scr.And(Scr.If("PLAYER_TRIGGERED_SPECIAL_POWER", Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.SpecialPower, "AlphaInstantPower")))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("triggered"))]),
            Scr.Script("TriggeredByWrongUnit", [Scr.And(Scr.If("PLAYER_TRIGGERED_SPECIAL_POWER_FROM_NAMED", Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.SpecialPower, "AlphaInstantPower"), Scr.Unit("Other")))], [Scr.Do("SET_FLAG", Scr.Flag("wrong"), Scr.Bool(true))]),
            Scr.Script("Sighted", [Scr.And(Scr.If("TYPE_SIGHTED", Scr.Unit("Hero"), Scr.Type("AlphaSoldier"), Scr.Side(PlayerA)))], [Scr.Do("SET_FLAG", Scr.Flag("sees"), Scr.Bool(true))]),
            Scr.Script("EnemyFar", [Scr.And(Scr.If("ENEMY_SIGHTED", Scr.Unit("Hero"), Scr.Int(0), Scr.Side(PlayerB)))], [Scr.Do("SET_FLAG", Scr.Flag("farEnemy"), Scr.Bool(true))]),
            Scr.Script("EnemyNear", [Scr.And(Scr.If("ENEMY_SIGHTED", Scr.Unit("Hero2"), Scr.Int(0), Scr.Side(PlayerB)))], [Scr.Do("SET_FLAG", Scr.Flag("nearEnemy"), Scr.Bool(true))]),
            Scr.Script("DiscoveredFar", [Scr.And(Scr.If("NAMED_DISCOVERED", Scr.Unit("Far"), Scr.Side(PlayerA)))], [Scr.Do("SET_FLAG", Scr.Flag("farSeen"), Scr.Bool(true))]),
            Scr.Script("DiscoveredNear", [Scr.And(Scr.If("NAMED_DISCOVERED", Scr.Unit("Near"), Scr.Side(PlayerA)))], [Scr.Do("SET_FLAG", Scr.Flag("nearSeen"), Scr.Bool(true))]),
            Scr.Script("TeamNear", [Scr.And(Scr.If("TEAM_DISCOVERED", Scr.Team("teamB"), Scr.Side(PlayerA)))], [Scr.Do("SET_FLAG", Scr.Flag("teamSeen"), Scr.Bool(true))]),
        ], objects: [Placed("AlphaHero", 100m, 100m, "teamA", "Hero"), Placed("AlphaSoldier", 140m, 100m, "teamA", "Mate"), Placed("AlphaHero", 100m, 300m, "teamA", "Other"),
            Placed("AlphaHero", 500m, 20m, "teamA", "Hero2"), Placed("BetaSoldier", 560m, 20m, "teamB", "Near"), Placed("BetaSoldier", 500m, 380m, "teamB", "Far")]);
        var engine = session.Engine;
        Run(session, 20);
        Check.That(engine.CounterValue("ready") > 0, "An unused power is ready for SKIRMISH_SPECIAL_POWER_READY.");
        var readyBefore = engine.CounterValue("ready");
        Run(session, 80);
        Check.That(engine.CounterValue("triggered") == 1, $"PLAYER_TRIGGERED_SPECIAL_POWER is consumed by the first evaluation that sees it (got {engine.CounterValue("triggered")}).");
        Check.That(!engine.FlagValue("wrong"), "The FROM_NAMED variant only matches the unit that used the power.");
        Check.That(engine.FlagValue("sees"), "TYPE_SIGHTED: the hero has an AlphaSoldier of PlayerA in its vision range.");
        Check.That(!engine.FlagValue("farEnemy") && engine.FlagValue("nearEnemy"), "ENEMY_SIGHTED needs the enemy inside the unit's vision range.");
        Check.That(engine.FlagValue("nearSeen") && !engine.FlagValue("farSeen"), "NAMED_DISCOVERED follows the player's shroud: the near enemy soldier is in sight of PlayerA, the far one is not.");
        Check.That(engine.FlagValue("teamSeen"), "TEAM_DISCOVERED is true when any member is in sight.");
        Check.That(engine.MissingConditionHits.Count == 0, "No condition counted as missing: " + string.Join(", ", engine.MissingConditionHits.Keys));
        _ = readyBefore;

        // The state belongs to the snapshot: a restored mission with a pending (unconsumed) trigger continues identically.
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Use", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY", Scr.Unit("Hero"), Scr.Ability("Cmd_AlphaPower"))], oneShot: true),
            Scr.Script("Later", [Scr.And(Scr.If("COUNTER", Scr.Counter("late"), Scr.Compare(Scr.GreaterEqual), Scr.Int(1)), Scr.If("PLAYER_TRIGGERED_SPECIAL_POWER", Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.SpecialPower, "AlphaInstantPower")))],
                [Scr.Do("SET_FLAG", Scr.Flag("consumed"), Scr.Bool(true))]),
            Scr.Script("Arm", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_TIMER", Scr.Counter("t"), Scr.Int(100))], oneShot: true),
            Scr.Script("Fire", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("t")))], [Scr.Do("SET_COUNTER", Scr.Counter("late"), Scr.Int(1))], oneShot: true),
        ], objects: [Placed("AlphaHero", 100m, 100m, "teamA", "Hero")]);
        var (original, restored, bytes) = MissionScenario.VerifyContinuation(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, 300);
        Check.That(original.Chain == restored.Chain && original.FinalHash == restored.FinalHash, $"A mission holding an unconsumed special-power trigger continues identically after save/restore ({bytes} bytes).");
    }

    public static void ScriptMapRevealAndShroud()
    {
        var far = (X: Fixed.FromInt(550), Y: Fixed.FromInt(350));
        var session = StartMission([
            Scr.Script("Reveal", [Scr.And(Scr.If("FLAG", Scr.Flag("go"), Scr.Bool(true)))], [Scr.Do("MAP_REVEAL_ALL_PERM", Scr.Side(PlayerA))], oneShot: true),
            Scr.Script("Undo", [Scr.And(Scr.If("FLAG", Scr.Flag("undo"), Scr.Bool(true)))], [Scr.Do("MAP_REVEAL_ALL_UNDO_PERM", Scr.Side(PlayerA))], oneShot: true),
            Scr.Script("Shroud", [Scr.And(Scr.If("FLAG", Scr.Flag("shroud"), Scr.Bool(true)))], [Scr.Do("MAP_SHROUD_ALL", Scr.Side(PlayerA))], oneShot: true),
            Scr.Script("Peek", [Scr.And(Scr.If("FLAG", Scr.Flag("peek"), Scr.Bool(true)))], [Scr.Do("MAP_REVEAL_AT_WAYPOINT", Scr.Waypoint("Far"), Scr.Real(50m), Scr.Side(PlayerA))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Scout")]);
        var world = session.World;
        var viewer = new PlayerId(1);
        Run(session, 6);
        Check.That(!world.IsPositionVisibleTo(viewer, far.X, far.Y) && !world.IsPositionExploredBy(viewer, far.X, far.Y), "The far corner starts shrouded.");
        session.Engine.SignalUiInteraction("peek");
        Run(session, 4);
        Check.That(world.IsPositionExploredBy(viewer, Fixed.FromInt(400), Fixed.FromInt(200)) && !world.IsPositionVisibleTo(viewer, Fixed.FromInt(400), Fixed.FromInt(200)), "MAP_REVEAL_AT_WAYPOINT is a quick look: the area becomes explored (fogged), not visible.");
        session.Engine.SignalUiInteraction("go");
        Run(session, 4);
        Check.That(world.IsPositionVisibleTo(viewer, far.X, far.Y), "MAP_REVEAL_ALL_PERM makes every cell visible.");
        Run(session, 30);
        Check.That(world.IsPositionVisibleTo(viewer, far.X, far.Y), "The permanent reveal survives the periodic vision refresh.");
        Check.That(!world.IsPositionVisibleTo(new PlayerId(2), far.X, far.Y), "Only the named player's map is revealed.");

        // The permanent mark is part of the world state: save, restore, continue identically.
        var bytes = MissionSnapshot.Write(session);
        var (map, fixture, terrain) = MissionMap([], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Scout")]);
        _ = (map, fixture, terrain, bytes);
        var copy = MissionSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, terrain, StartMissionMap(session));
        Check.That(copy.World.IsPositionVisibleTo(viewer, far.X, far.Y), "A restored mission keeps the permanent reveal.");
        Check.That(copy.World.StateHash == session.World.StateHash, "...and has the same world hash.");

        session.Engine.SignalUiInteraction("undo");
        Run(session, 8);
        Check.That(!world.IsPositionVisibleTo(viewer, far.X, far.Y) && world.IsPositionExploredBy(viewer, far.X, far.Y), "UNDO_PERM drops the permanent mark: not visible any more, still explored.");
        session.Engine.SignalUiInteraction("shroud");
        Run(session, 4);
        Check.That(!world.IsPositionExploredBy(viewer, far.X, far.Y), "MAP_SHROUD_ALL forgets everything the player had explored.");
    }

    private static MapData StartMissionMap(MissionSession session) => session.Map;

    public static void ScriptBuildSwitchesAndSciences()
    {
        var session = StartMission([
            Scr.Script("Cash", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("PLAYER_GIVE_MONEY", Scr.Side(PlayerA), Scr.Int(500))], oneShot: true),
            Scr.Script("Off", [Scr.And(Scr.If("FLAG", Scr.Flag("off"), Scr.Bool(true)))], [Scr.Do("PLAYER_DISABLE_UNIT_CONSTRUCTION", Scr.Side(PlayerA)), Scr.Do("PLAYER_DISABLE_BASE_CONSTRUCTION", Scr.Side(PlayerA))], oneShot: true),
            Scr.Script("On", [Scr.And(Scr.If("FLAG", Scr.Flag("on"), Scr.Bool(true)))], [Scr.Do("PLAYER_ENABLE_UNIT_CONSTRUCTION", Scr.Side(PlayerA)), Scr.Do("PLAYER_ENABLE_BASE_CONSTRUCTION", Scr.Side(PlayerA))], oneShot: true),
        ], objects: [Placed("AlphaCenter", 100m, 100m, "teamA", "Hq")]);
        var viewer = new PlayerId(1);
        var center = NamedIn(session, "Hq")!;
        Run(session, 2);
        Check.That(session.World.CanBuildUnits(viewer) && session.World.CanBuildStructures(viewer), "Construction is allowed by default.");
        var accepted = session.Issue(viewer, (sequence, tick) => new CnCNew.Simulation.Commands.QueueProductionCommand(viewer, sequence, tick, center.Id, "AlphaSoldier"));
        Check.That(accepted.Accepted, "A soldier can be queued while unit construction is enabled.");
        session.Engine.SignalUiInteraction("off");
        Run(session, 2);
        Check.That(!session.World.CanBuildUnits(viewer) && !session.World.CanBuildStructures(viewer), "PLAYER_DISABLE_*_CONSTRUCTION switches both off.");
        var refused = session.Issue(viewer, (sequence, tick) => new CnCNew.Simulation.Commands.QueueProductionCommand(viewer, sequence, tick, center.Id, "AlphaSoldier"));
        Run(session, 1);
        Check.That(session.World.RejectionCounts.Sum() > 0, "While unit construction is disabled, the production command is rejected by the world (accepted at submission: " + refused.Accepted + ").");
        var snapshot = MissionSnapshot.Write(session);
        var (_, fixture, terrain) = MissionMap([], objects: [Placed("AlphaCenter", 100m, 100m, "teamA", "Hq")]);
        var copy = MissionSnapshot.Restore(snapshot, fixture.Templates, fixture.Fingerprint, terrain, StartMissionMap(session));
        Check.That(!copy.World.CanBuildUnits(viewer) && !copy.World.CanBuildStructures(viewer), "The switches are part of the saved world state.");
        session.Engine.SignalUiInteraction("on");
        Run(session, 2);
        Check.That(session.World.CanBuildUnits(viewer) && session.World.CanBuildStructures(viewer), "PLAYER_ENABLE_*_CONSTRUCTION switches them on again.");
    }

    public static void ScriptContainerAndGarrisonActions()
    {
        var session = StartMission([
            Scr.Script("Garrison", [Scr.And(Scr.If("FLAG", Scr.Flag("garrison"), Scr.Bool(true)))], [Scr.Do("NAMED_GARRISON_SPECIFIC_BUILDING", Scr.Unit("S1"), Scr.Unit("Bunk"))], oneShot: true),
            Scr.Script("TeamNearest", [Scr.And(Scr.If("FLAG", Scr.Flag("team"), Scr.Bool(true)))], [Scr.Do("TEAM_GARRISON_NEAREST_BUILDING", Scr.Team("Squad"))], oneShot: true),
            Scr.Script("Leave", [Scr.And(Scr.If("FLAG", Scr.Flag("leave"), Scr.Bool(true)))], [Scr.Do("NAMED_EXIT_BUILDING", Scr.Unit("S1"))], oneShot: true),
            Scr.Script("Empty", [Scr.And(Scr.If("FLAG", Scr.Flag("empty"), Scr.Bool(true)))], [Scr.Do("EXIT_SPECIFIC_BUILDING", Scr.Unit("Bunk"))], oneShot: true),
            Scr.Script("IsFull", [Scr.And(Scr.If("SKIRMISH_PLAYER_HAS_COMPARISON_GARRISONED", Scr.Side(PlayerA), Scr.Compare(Scr.GreaterEqual), Scr.Int(1)))], [Scr.Do("SET_FLAG", Scr.Flag("garrisoned"), Scr.Bool(true))]),
            Scr.Script("IsEmpty", [Scr.And(Scr.If("NAMED_BUILDING_IS_EMPTY", Scr.Unit("Bunk")))], [Scr.Do("SET_FLAG", Scr.Flag("bunkEmpty"), Scr.Bool(true))]),
        ], objects: [Placed("AlphaBunker", 160m, 200m, "teamA", "Bunk"), Placed("AlphaSoldier", 100m, 200m, "Squad", "S1"), Placed("AlphaSoldier", 100m, 220m, "Squad", "S2")]);
        var bunker = NamedIn(session, "Bunk")!;
        Run(session, 3);
        Check.That(bunker.Container is { ContainCount: 0 } && session.Engine.FlagValue("bunkEmpty"), "The bunker starts empty (NAMED_BUILDING_IS_EMPTY).");
        session.Engine.SignalUiInteraction("garrison");
        Run(session, 400);
        Check.That(bunker.Container!.ContainCount == 1 && NamedIn(session, "S1")!.ContainedBy == bunker.Id, $"NAMED_GARRISON_SPECIFIC_BUILDING walked the soldier into the bunker (count {bunker.Container.ContainCount}).");
        Check.That(session.Engine.FlagValue("garrisoned"), "SKIRMISH_PLAYER_HAS_COMPARISON_GARRISONED counts the occupied garrison.");
        session.Engine.SignalUiInteraction("leave");
        Run(session, 20);
        Check.That(bunker.Container.ContainCount == 0 && !NamedIn(session, "S1")!.ContainedBy.IsValid, "NAMED_EXIT_BUILDING lets the soldier out again.");
        session.Engine.SignalUiInteraction("team");
        Run(session, 500);
        Check.That(bunker.Container.ContainCount == 2, $"TEAM_GARRISON_NEAREST_BUILDING filled the bunker's two slots with the team's infantry (count {bunker.Container.ContainCount}; diagnostics {string.Join("; ", session.Engine.Diagnostics.Select(pair => pair.Key + "=" + pair.Value))}; team {string.Join(",", session.Engine.TeamInstances.Select(team => team.Prototype + ":" + team.Members.Count))}).");
        session.Engine.SignalUiInteraction("empty");
        Run(session, 20);
        Check.That(bunker.Container.ContainCount == 0, "EXIT_SPECIFIC_BUILDING evacuates everybody.");
        Check.That(session.Engine.MissingActionHits.Count == 0 && session.World.RejectionCounts.Sum() == 0, "No action counted as missing, no command rejected: " + string.Join(", ", session.Engine.MissingActionHits.Keys) + " / " + string.Join("; ", session.Engine.Diagnostics.Keys));
    }

    public static void ScriptWarehouseAndTeamActions()
    {
        var session = StartMission([
            Scr.Script("Value", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("WAREHOUSE_SET_VALUE", Scr.Unit("Store"), Scr.Int(250))], oneShot: true),
            Scr.Script("Merge", [Scr.And(Scr.If("FLAG", Scr.Flag("merge"), Scr.Bool(true)))], [Scr.Do("TEAM_MERGE_INTO_TEAM", Scr.Team("teamA"), Scr.Team("teamB"))], oneShot: true),
        ], objects: [Placed("ScriptWarehouse", 300m, 300m, "", "Store"), Placed("AlphaSoldier", 100m, 200m, "teamA", "S1"), Placed("BetaSoldier", 500m, 300m, "teamB", "B1")]);
        var store = NamedIn(session, "Store")!;
        Check.That(session.World.WarehouseBoxes(store.Id) == 5, "The warehouse starts with its 5 boxes.");
        Run(session, 2);
        Check.That(session.World.WarehouseBoxes(store.Id) == 3, $"WAREHOUSE_SET_VALUE 250 with 100 per box leaves ceil(2.5) = 3 boxes (got {session.World.WarehouseBoxes(store.Id)}).");
        session.Engine.SignalUiInteraction("merge");
        Run(session, 2);
        var soldier = NamedIn(session, "S1")!;
        Check.That(soldier.Owner == new PlayerId(2), "TEAM_MERGE_INTO_TEAM moves the members to the destination team and its owner.");
        Check.That(!session.Engine.TeamInstances.First(team => team.Prototype == "teamA").Members.Any(), "The merged team is empty afterwards (it stays addressable for TEAM_DESTROYED).");
    }

    public static void ScriptBuildTeamProducesTheTeam()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Build", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("PLAYER_GIVE_MONEY", Scr.Side(PlayerA), Scr.Int(1000)), Scr.Do("BUILD_TEAM", Scr.Team("Gang"))], oneShot: true),
            Scr.Script("Done", [Scr.And(Scr.If("TEAM_CREATED", Scr.Team("Gang")))], [Scr.Do("SET_FLAG", Scr.Flag("gangReady"), Scr.Bool(true))]),
        ], objects: [Placed("AlphaCenter", 100m, 100m, "teamA", "Hq")]);
        MapProperties Gang() => new([Scr.Text("teamName", "Gang"), Scr.Text("teamOwner", PlayerA), Scr.Flag("teamIsSingleton", true),
            Scr.Text("teamUnitType1", "AlphaSoldier"), new MapProperty("teamUnitMinCount1", MapPropertyType.Int, 2, 0m, string.Empty), new MapProperty("teamUnitMaxCount1", MapPropertyType.Int, 3, 0m, string.Empty)]);
        map = map with { Teams = [..map.Teams, Gang()] };
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, 20);
        Check.That(!session.Engine.FlagValue("gangReady"), "The team is not created before its units exist.");
        Run(session, 600);
        var gang = session.Engine.TeamInstances.First(team => team.Prototype == "Gang");
        Check.That(gang.Members.Count == 3 && gang.Created && session.Engine.FlagValue("gangReady"),
            $"BUILD_TEAM queued and produced the team's soldiers, which joined and activated it (members {gang.Members.Count}, created {gang.Created}; diagnostics: {string.Join("; ", session.Engine.Diagnostics.Keys)}).");
        var (original, restored, _) = MissionScenario.VerifyContinuation(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, 400);
        Check.That(original.Chain == restored.Chain && original.FinalHash == restored.FinalHash, "A mission with a running team build continues identically after save/restore.");
    }

    public static void ScriptOtherCommandButtonsAndWaypointAbilities()
    {
        var session = StartMission([
            Scr.Script("Fire", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY_ON_NAMED", Scr.Unit("Shooter"), Scr.Ability("Cmd_AlphaFire"), Scr.Unit("Victim"))], oneShot: true),
            Scr.Script("At", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT", Scr.Unit("Hero"), Scr.Ability("Cmd_AlphaPower"), Scr.Waypoint("Mid"))], oneShot: true),
            Scr.Script("Odd", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY", Scr.Unit("Hero"), Scr.Ability("Cmd_AlphaSellNotPower"))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 100m, "teamA", "Shooter"), Placed("BetaSoldier", 130m, 100m, "teamB", "Victim"), Placed("AlphaHero", 100m, 300m, "teamA", "Hero")]);
        var victim = NamedIn(session, "Victim")!;
        Run(session, 3);
        Check.That(victim.Body!.Health < victim.Body.MaxHealth, "A FIRE_WEAPON button used on a named target fires the carrier's weapon at it.");
        Check.That(session.Engine.Diagnostics.ContainsKey("command button 'Cmd_AlphaSellNotPower' has command SELL, not executed"), "A button of another command kind is diagnosed under its own message.");
        Check.That(session.Engine.Diagnostics.Keys.All(key => !key.Contains("is not a special-power", StringComparison.Ordinal)), "The old catch-all message is gone.");
        Check.That(!session.Engine.MissingActionHits.ContainsKey("NAMED_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT"), "The waypoint variant is implemented.");
        Check.That(session.World.RejectionCounts.Sum() == 0 || session.Engine.Diagnostics.Count > 0, "Every refusal is visible (rejection or diagnostic).");
    }

    public static void ScriptSequentialScripts()
    {
        // "Patrol" runs on the Squad team one instruction at a time: instant instructions run in the same tick, the move waits for arrival, the idle waits for its timer.
        var patrol = Scr.Script("Patrol", [], [
            Scr.Do("SET_FLAG", Scr.Flag("step1"), Scr.Bool(true)),
            Scr.Do("MOVE_TEAM_TO", Scr.Team("Squad"), Scr.Waypoint("Mid")),
            Scr.Do("SET_FLAG", Scr.Flag("arrived"), Scr.Bool(true)),
            Scr.Do("TEAM_IDLE_FOR_FRAMECOUNT", Scr.Team("Squad"), Scr.Int(60)),
            Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("laps"))], active: false);
        var session = StartMission([
            Scr.Script("Start", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_EXECUTE_SEQUENTIAL_SCRIPT_LOOPING", Scr.Team("Squad"), Scr.Script("Patrol"), Scr.Int(3))], oneShot: true),
            patrol,
            Scr.Script("Unit", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("UNIT_EXECUTE_SEQUENTIAL_SCRIPT", Scr.Unit("Lone"), Scr.Script("LoneSteps"))], oneShot: true),
            Scr.Script("LoneSteps", [], [Scr.Do("SET_FLAG", Scr.Flag("loneStarted"), Scr.Bool(true)), Scr.Do("NAMED_STOP", Scr.Unit("<This Object>")), Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("loneDone"))], active: false),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "Squad", "S1"), Placed("AlphaSoldier", 100m, 300m, "teamA", "Lone")]);
        var engine = session.Engine;
        Run(session, 3);
        Check.That(engine.FlagValue("step1"), "The first instruction of the sequential script ran right away.");
        Check.That(!engine.FlagValue("arrived"), "The next instruction waits until the move has finished (the team is not idle).");
        Check.That(engine.FlagValue("loneStarted") && engine.CounterValue("loneDone") == 1, "A unit sequence runs its instructions in one tick while the unit stays idle, with <This Object> naming the unit.");
        Run(session, 400);
        Check.That(engine.FlagValue("arrived"), "After the team reached the waypoint the sequence went on.");
        var soldier = NamedIn(session, "S1")!;
        Check.That(soldier.X > Fixed.FromInt(200), $"The soldier walked to the waypoint (x = {soldier.X}).");
        Check.That(engine.CounterValue("laps") >= 1, $"The idle timer ended and the last instruction ran (laps {engine.CounterValue("laps")}).");
        Run(session, 3000);
        Check.That(engine.CounterValue("laps") == 3, $"A looping sequential script with 3 runs stops after exactly 3 laps (got {engine.CounterValue("laps")}).");
        Check.That(engine.MissingActionHits.Count == 0, "No action counted as missing: " + string.Join(", ", engine.MissingActionHits.Keys));

        // Stopping and killing the team end the runner; the state is saved and restored with the runner in flight.
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Start", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_EXECUTE_SEQUENTIAL_SCRIPT_LOOPING", Scr.Team("Squad"), Scr.Script("Patrol"), Scr.Int(0))], oneShot: true),
            patrol,
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "Squad", "S1")]);
        var (original, restored, bytes) = MissionScenario.VerifyContinuation(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, 700);
        Check.That(original.Chain == restored.Chain && original.FinalHash == restored.FinalHash, $"A mission with a sequential script in flight continues identically after save/restore ({bytes} bytes).");
        var stopper = StartMission([
            Scr.Script("Start", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_EXECUTE_SEQUENTIAL_SCRIPT_LOOPING", Scr.Team("Squad"), Scr.Script("Patrol"), Scr.Int(0))], oneShot: true),
            patrol,
            Scr.Script("Stop", [Scr.And(Scr.If("FLAG", Scr.Flag("halt"), Scr.Bool(true)))], [Scr.Do("TEAM_STOP_SEQUENTIAL_SCRIPT", Scr.Team("Squad"))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "Squad", "S1")]);
        Run(stopper, 5);
        stopper.Engine.SignalUiInteraction("halt");
        Run(stopper, 600);
        Check.That(!stopper.Engine.FlagValue("arrived"), "TEAM_STOP_SEQUENTIAL_SCRIPT ends the runner: the instruction after the move never ran.");
    }

    public static void ScriptSpecialPowerActionsRevealsAndRecording()
    {
        var power = Scr.Named(MapScriptParameterType.SpecialPower, "AlphaInstantPower");
        var session = StartMission([
            Scr.Script("Countdown", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_SET_SPECIAL_POWER_COUNTDOWN", Scr.Unit("Hero"), power, Scr.Int(4))], oneShot: true),
            Scr.Script("Ready", [Scr.And(Scr.If("SKIRMISH_SPECIAL_POWER_READY", Scr.Side(PlayerA), power))], [Scr.Do("SET_FLAG", Scr.Flag("ready"), Scr.Bool(true))]),
            Scr.Script("Fire", [Scr.And(Scr.If("FLAG", Scr.Flag("ready"), Scr.Bool(true)))], [Scr.Do("NAMED_FIRE_SPECIAL_POWER_AT_NAMED", Scr.Unit("Hero"), power, Scr.Unit("Target"))], oneShot: true),
            Scr.Script("Fired", [Scr.And(Scr.If("PLAYER_TRIGGERED_SPECIAL_POWER_FROM_NAMED", Scr.Side(PlayerA), power, Scr.Unit("Hero")))], [Scr.Do("SET_FLAG", Scr.Flag("fired"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Look", [Scr.And(Scr.If("FLAG", Scr.Flag("look"), Scr.Bool(true)))], [Scr.Do("MAP_REVEAL_PERMANENTLY_AT_WAYPOINT", Scr.Waypoint("Far"), Scr.Real(60m), Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.RevealName, "FarLook"))], oneShot: true),
            Scr.Script("Unlook", [Scr.And(Scr.If("FLAG", Scr.Flag("unlook"), Scr.Bool(true)))], [Scr.Do("MAP_UNDO_REVEAL_PERMANENTLY_AT_WAYPOINT", Scr.Named(MapScriptParameterType.RevealName, "FarLook"))], oneShot: true),
            Scr.Script("Record", [Scr.And(Scr.If("FLAG", Scr.Flag("record"), Scr.Bool(true)))], [Scr.Do("PLAYER_AFFECT_RECEIVING_EXPERIENCE", Scr.Side(PlayerA), Scr.Real(2m)), Scr.Do("OBJECT_ALLOW_BONUSES", Scr.Bool(false))], oneShot: true),
        ], objects: [Placed("AlphaHero", 100m, 100m, "teamA", "Hero"), Placed("BetaSoldier", 300m, 100m, "teamB", "Target")]);
        var engine = session.Engine;
        var viewer = new PlayerId(1);
        var far = (X: Fixed.FromInt(400), Y: Fixed.FromInt(200));
        Run(session, 60);
        Check.That(!engine.FlagValue("ready"), "A countdown set to 4 s keeps the power from being ready after 2 s.");
        Run(session, 90);
        Check.That(engine.FlagValue("ready") && engine.FlagValue("fired"), "After the countdown the power is ready and NAMED_FIRE_SPECIAL_POWER_AT_NAMED used it (and the trigger condition saw the unit).");
        Check.That(session.World.RejectionCounts.Sum() == 0, "The use was accepted: " + string.Join("; ", engine.Diagnostics.Keys));
        Check.That(!world(session).IsPositionVisibleTo(viewer, far.X, far.Y), "The far waypoint is out of sight before the reveal.");
        engine.SignalUiInteraction("look");
        Run(session, 8);
        Check.That(world(session).IsPositionVisibleTo(viewer, far.X, far.Y), "MAP_REVEAL_PERMANENTLY_AT_WAYPOINT keeps the area in sight.");
        engine.SignalUiInteraction("unlook");
        Run(session, 8);
        Check.That(!world(session).IsPositionVisibleTo(viewer, far.X, far.Y), "The named undo takes the reveal back.");
        engine.SignalUiInteraction("record");
        Run(session, 2);
        Check.That(engine.RecordedActions == 2 && engine.SkillPointsModifierOf(viewer) == Fixed.FromInt(2), "The experience scale and the bonus switch are recorded, not executed.");
        var recordOnly = StartMission([Scr.Script("Record", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("PLAYER_AFFECT_RECEIVING_EXPERIENCE", Scr.Side(PlayerA), Scr.Real(2m)), Scr.Do("OBJECT_ALLOW_BONUSES", Scr.Bool(false))], oneShot: true)]);
        Run(recordOnly, 2);
        Check.That(recordOnly.Engine.ExecutedActions == 0 && recordOnly.Engine.RecordedActions == 2 && recordOnly.Engine.MissingActionHits.Count == 0, "Recorded actions are neither counted as executed nor as missing.");
        Check.That(ScriptCoverage.ActionSupport("OBJECT_ALLOW_BONUSES") == ScriptSupport.Recorded, "The support table names them as recorded.");
    }

    private static CnCNew.Simulation.World.SimulationWorld world(MissionSession session) => session.World;

    public static void ScriptRecruitTeam()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Recruit", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("RECRUIT_TEAM", Scr.Team("Strike"), Scr.Real(0m))], oneShot: true),
            Scr.Script("Ready", [Scr.And(Scr.If("TEAM_CREATED", Scr.Team("Strike")))], [Scr.Do("SET_FLAG", Scr.Flag("strike"), Scr.Bool(true))]),
            Scr.Script("Hold", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_AVAILABLE_FOR_RECRUITMENT", Scr.Team("Squad"), Scr.Bool(false))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamPlayerA", "Free1"), Placed("AlphaSoldier", 120m, 200m, "teamPlayerA", "Free2"), Placed("AlphaSoldier", 140m, 200m, "teamPlayerA", "Free3"), Placed("AlphaSoldier", 100m, 100m, "Squad", "Squad1")]);
        MapProperties Strike() => new([Scr.Text("teamName", "Strike"), Scr.Text("teamOwner", PlayerA), Scr.Flag("teamIsSingleton", true), Scr.Text("teamHome", "Far"),
            new MapProperty("teamProductionPriority", MapPropertyType.Int, 5, 0m, string.Empty),
            Scr.Text("teamUnitType1", "AlphaSoldier"), new MapProperty("teamUnitMinCount1", MapPropertyType.Int, 1, 0m, string.Empty), new MapProperty("teamUnitMaxCount1", MapPropertyType.Int, 2, 0m, string.Empty)]);
        map = map with { Teams = [..map.Teams, Strike()] };
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, 400);
        var strike = session.Engine.TeamInstances.First(team => team.Prototype == "Strike");
        Check.That(strike.Created && strike.Members.Count == 2 && session.Engine.FlagValue("strike"), $"RECRUIT_TEAM took the two (MaxCount) free soldiers (members {strike.Members.Count}, diagnostics {string.Join("; ", session.Engine.Diagnostics.Keys)}).");
        Check.That(!strike.Members.Contains(NamedIn(session, "Squad1")!.Id.Value), "A unit of another team that was not made recruitable stays where it is.");
        var recruit = session.World.FindEntity(new EntityId(strike.Members[0]))!;
        Check.That(recruit.X > Fixed.FromInt(300), $"The recruits walk to the team's home waypoint (x = {recruit.X}).");
        var (original, restored, _) = MissionScenario.VerifyContinuation(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, 300);
        Check.That(original.Chain == restored.Chain && original.FinalHash == restored.FinalHash, "A recruited team continues identically after save/restore.");
    }

    public static void ScriptTeamEventScripts()
    {
        // Team "Wave" (singleton, owner PlayerB) has event scripts; its two soldiers fight PlayerA's soldier in range. Subroutines count what happens.
        MapScript Sub(string name, string counter) => Scr.Script(name, [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter(counter))], subroutine: true);
        var (map, fixture, terrain) = MissionMap([
            Sub("OnCreate", "created"), Sub("OnUnit", "unitDied"), Sub("OnGone", "gone"), Sub("OnSight", "sighted"), Sub("OnClear", "clear"),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Hunter"), Placed("BetaSoldier", 200m, 200m, "Wave", "W1"), Placed("BetaSoldier", 210m, 220m, "Wave", "W2")]);
        MapProperties Wave() => new([Scr.Text("teamName", "Wave"), Scr.Text("teamOwner", PlayerB), Scr.Flag("teamIsSingleton", true),
            Scr.Text("teamOnCreateScript", "OnCreate"), Scr.Text("teamOnUnitDestroyedScript", "OnUnit"), Scr.Text("teamOnDestroyedScript", "OnGone"),
            Scr.Text("teamEnemySightedScript", "OnSight"), Scr.Text("teamAllClearScript", "OnClear")]);
        map = map with { Teams = [..map.Teams, Wave()] };
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        var engine = session.Engine;
        Run(session, 3);
        Check.That(engine.CounterValue("created") == 1, $"The team's create script ran once when the team was active (got {engine.CounterValue("created")}).");
        Run(session, 4);
        Check.That(engine.CounterValue("sighted") == 1 && engine.CounterValue("clear") == 0, "The enemy-sighted script ran when the Hunter came into the soldiers' vision range.");
        session.World.Kill(NamedIn(session, "W1")!.Id);
        Run(session, 3);
        Check.That(engine.CounterValue("unitDied") == 1, $"Every member's death runs the unit-destroyed script (got {engine.CounterValue("unitDied")}).");
        Check.That(engine.CounterValue("gone") == 1, "With the default threshold (clamped to units - 1, Team::updateState) the destroyed script runs as soon as the team has lost a unit.");
        session.World.Kill(NamedIn(session, "W2")!.Id);
        Run(session, 3);
        Check.That(engine.CounterValue("unitDied") == 2 && engine.CounterValue("gone") == 1, $"The second death runs the unit-destroyed script again but not the destroyed script a second time (died {engine.CounterValue("unitDied")}, gone {engine.CounterValue("gone")}).");
        Run(session, 30);
        Check.That(engine.CounterValue("gone") == 1 && engine.CounterValue("created") == 1, "Neither script repeats.");
        var (original, restored, _) = MissionScenario.VerifyContinuation(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, 200);
        Check.That(original.Chain == restored.Chain && original.FinalHash == restored.FinalHash, "A mission with team event state continues identically after save/restore.");
    }

    public static void ScriptObjectiveDriver()
    {
        // "Win" needs the enemy's Target dead and a flag a walker sets by reaching an area; "Lose" needs the Hero dead. The driver satisfies the world conditions, the scripts end the mission.
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Arrive", [Scr.And(Scr.If("NAMED_INSIDE_AREA", Scr.Unit("Walker"), Scr.Area("LeftArea")))], [Scr.Do("SET_FLAG", Scr.Flag("arrived"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Win", [Scr.And(Scr.If("NAMED_DESTROYED", Scr.Unit("Target")), Scr.If("FLAG", Scr.Flag("arrived"), Scr.Bool(true)))], [Scr.Do("VICTORY")], oneShot: true),
            Scr.Script("Lose", [Scr.And(Scr.If("NAMED_DESTROYED", Scr.Unit("Hero")))], [Scr.Do("DEFEAT")], oneShot: true),
        ], objects: [Placed("AlphaHero", 100m, 100m, "teamA", "Hero"), Placed("AlphaSoldier", 400m, 300m, "teamA", "Walker"), Placed("BetaSoldier", 520m, 380m, "teamB", "Target")]);
        var win = MissionObjectiveDriver.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, true, maxTicks: 3000);
        Check.That(win.ReachedByScript && win.Outcome == MissionOutcome.Victory && win.OutcomeScript == "Win", $"The driver reached the victory through the mission's own 'Win' script (outcome {win.Outcome} by '{win.OutcomeScript}', log: {string.Join("; ", win.Log)}).");
        Check.That(win.Interventions >= 2 && win.Log.Any(line => line.Contains("send named 'Walker'", StringComparison.Ordinal)) && win.Log.Any(line => line.Contains("destroy named 'Target'", StringComparison.Ordinal)), "It sent the walker into the area and destroyed the enemy's target.");
        var lose = MissionObjectiveDriver.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, false, maxTicks: 3000);
        Check.That(lose.ReachedByScript && lose.Outcome == MissionOutcome.Defeat && lose.OutcomeScript == "Lose", $"...and the defeat through 'Lose' (outcome {lose.Outcome} by '{lose.OutcomeScript}').");
        var again = MissionObjectiveDriver.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, true, maxTicks: 3000);
        Check.That(again.OutcomeTick == win.OutcomeTick && again.Log.SequenceEqual(win.Log), "A second run of the driver is identical.");
    }
}

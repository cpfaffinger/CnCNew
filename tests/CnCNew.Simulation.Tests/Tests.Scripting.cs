using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>A tiny builder for decoded World Builder scripts, so a test states what a map designer would have clicked together.</summary>
static class Scr
{
    public static MapScriptParameter Int(int value) => new(MapScriptParameterType.Int, value, 0m, string.Empty, 0m, 0m, 0m);
    public static MapScriptParameter Bool(bool value) => new(MapScriptParameterType.Boolean, value ? 1 : 0, 0m, string.Empty, 0m, 0m, 0m);
    public static MapScriptParameter Real(decimal value) => new(MapScriptParameterType.Real, 0, value, string.Empty, 0m, 0m, 0m);
    public static MapScriptParameter Named(MapScriptParameterType type, string text) => new(type, 0, 0m, text, 0m, 0m, 0m);
    public static MapScriptParameter Counter(string name) => Named(MapScriptParameterType.Counter, name);
    public static MapScriptParameter Flag(string name) => Named(MapScriptParameterType.Flag, name);
    public static MapScriptParameter Side(string name) => Named(MapScriptParameterType.Side, name);
    public static MapScriptParameter Team(string name) => Named(MapScriptParameterType.Team, name);
    public static MapScriptParameter Unit(string name) => Named(MapScriptParameterType.Unit, name);
    public static MapScriptParameter Type(string name) => Named(MapScriptParameterType.ObjectType, name);
    public static MapScriptParameter Waypoint(string name) => Named(MapScriptParameterType.Waypoint, name);
    public static MapScriptParameter Area(string name) => Named(MapScriptParameterType.TriggerArea, name);
    public static MapScriptParameter Script(string name) => Named(MapScriptParameterType.Script, name);
    public static MapScriptParameter Ability(string name) => Named(MapScriptParameterType.CommandButtonAbility, name);
    public static MapScriptParameter Compare(int op) => new(MapScriptParameterType.Comparison, op, 0m, string.Empty, 0m, 0m, 0m);
    public const int Less = 0, LessEqual = 1, Equal = 2, GreaterEqual = 3, Greater = 4, NotEqual = 5;

    public static MapCondition If(string name, params MapScriptParameter[] parameters)
    {
        var id = MapScriptCatalog.ConditionTypeNames.IndexOf(name);
        if (id < 0) throw new ArgumentException($"Unknown condition {name}.");
        return new MapCondition(id, name, [..parameters]);
    }

    public static MapScriptAction Do(string name, params MapScriptParameter[] parameters)
    {
        var id = MapScriptCatalog.ScriptActionTypeNames.IndexOf(name);
        if (id < 0) throw new ArgumentException($"Unknown action {name}.");
        return new MapScriptAction(id, name, [..parameters]);
    }

    public static MapOrCondition And(params MapCondition[] conditions) => new([..conditions]);

    public static MapScript Script(string name, MapOrCondition[] conditions, MapScriptAction[] actions, MapScriptAction[]? otherwise = null, bool active = true, bool oneShot = false,
        bool subroutine = false, int delaySeconds = 0, bool easy = true, bool normal = true, bool hard = true)
        => new(name, string.Empty, string.Empty, string.Empty, active, oneShot, easy, normal, hard, subroutine, delaySeconds, [..conditions], [..actions], [..otherwise ?? []]);

    public static MapProperty Text(string key, string value) => new(key, MapPropertyType.Ascii, 0, 0m, value);
    public static MapProperty Flag(string key, bool value, int _ = 0) => new(key, MapPropertyType.Bool, value ? 1 : 0, 0m, string.Empty);
}

static partial class Tests
{
    private const string PlayerA = "PlayerA";
    private const string PlayerB = "PlayerB";

    private static IReadOnlyDictionary<string, string> ScriptFiles() => new Dictionary<string, string>(SkirmishFiles())
    {
        ["Data/INI/Object/ScriptAbilityFixture.ini"] = """
        SpecialPower AlphaInstantPower
          ReloadTime = 0
        End
        Object AlphaHero
          Side = Alpha
          KindOf = SELECTABLE INFANTRY
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          BuildCost = 10
          BuildTime = 1
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = AlphaInstantPower
          End
        End
        Object AlphaBunker
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE
          VisionRange = 60.0
          ShroudClearingRange = 60.0
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 2
          End
        End
        Object ScriptWarehouse
          KindOf = STRUCTURE
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SupplyWarehouseDockUpdate Dock01
            NumberApproachPositions = 1
            StartingBoxes = 5
          End
        End
        CommandButton Cmd_AlphaPower
          Command = SPECIAL_POWER
          SpecialPower = AlphaInstantPower
        End
        Upgrade Upgrade_AlphaPlayer
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End
        CommandButton Cmd_AlphaFire
          Command = FIRE_WEAPON
          WeaponSlot = SECONDARY
        End
        CommandButton Cmd_AlphaSellNotPower
          Command = SELL
        End
        """,
    };

    /// <summary>
    /// A 60x40-cell map with three sides (neutral, PlayerA human, PlayerB enemy of A), two waypoints, an area around the left spawn and two teams;
    /// <paramref name="scripts"/> run in PlayerA's list (index 1), <paramref name="neutral"/> in the neutral list (index 0).
    /// </summary>
    private static (MapData Map, SyntheticFixture Fixture, TerrainMap Terrain) MissionMap(MapScript[] scripts, MapScriptGroup[]? groups = null, MapObject[]? objects = null, MapScript[]? neutral = null, (string Label, (string Name, decimal X, decimal Y)[] Points)[]? paths = null)
    {
        var fixture = new SyntheticFixture(files: ScriptFiles());
        var area = new MapPolygonTrigger("LeftArea", string.Empty, 7, false, false, 0, [new MapPoint(0, 0, 0), new MapPoint(200, 0, 0), new MapPoint(200, 400, 0), new MapPoint(0, 400, 0)]);
        var baseMap = SyntheticMaps.Build(60, 40, 1, triggers: [area], waypoints: [("Spawn", 100m, 200m, 0m), ("Far", 400m, 200m, 0m), ("Mid", 250m, 200m, 0m)]);
        // Waypoint paths: extra waypoints, the first labelled with the path name (waypointPathLabel1), linked in order by waypointID.
        var pathObjects = ImmutableArray.CreateBuilder<MapObject>();
        var pathLinks = ImmutableArray.CreateBuilder<MapWaypointLink>();
        var nextId = 100;
        foreach (var (label, points) in paths ?? [])
        {
            var first = true;
            var previous = 0;
            foreach (var (pointName, x, y) in points)
            {
                var id = nextId++;
                var props = ImmutableArray.CreateBuilder<MapProperty>();
                props.Add(new MapProperty("waypointID", MapPropertyType.Int, id, 0m, string.Empty));
                props.Add(Scr.Text("waypointName", label + "." + pointName));
                if (first) props.Add(Scr.Text("waypointPathLabel1", label));
                pathObjects.Add(new MapObject("*Waypoints/Waypoint", x, y, 0m, 0m, 0, new MapProperties(props.ToImmutable())));
                if (!first) pathLinks.Add(new MapWaypointLink(previous, id));
                first = false;
                previous = id;
            }
        }
        baseMap = baseMap with { Objects = [..baseMap.Objects, ..pathObjects], WaypointLinks = pathLinks.ToImmutable() };
        MapSide Side(string name, bool human, string allies, string enemies)
            => new(new MapProperties([Scr.Text("playerName", name), Scr.Flag("playerIsHuman", human), Scr.Text("playerAllies", allies), Scr.Text("playerEnemies", enemies)]), []);
        var sides = ImmutableArray.Create(
            new MapSide(new MapProperties([Scr.Text("playerName", string.Empty)]), []),
            Side(PlayerA, true, string.Empty, PlayerB),
            Side(PlayerB, false, string.Empty, PlayerA));
        MapProperties Team(string name, string owner, bool singleton) => new([Scr.Text("teamName", name), Scr.Text("teamOwner", owner), Scr.Flag("teamIsSingleton", singleton)]);
        var map = baseMap with
        {
            Sides = sides,
            Teams = [Team("teamA", PlayerA, true), Team("teamB", PlayerB, true), Team("Raiders", PlayerB, false), Team("Squad", PlayerA, true), Team("teamPlayerA", PlayerA, true)],
            Objects = [..baseMap.Objects, ..objects ?? []],
            PlayerScripts = [new MapScriptList([..neutral ?? []], []), new MapScriptList([..scripts], [..groups ?? []]), new MapScriptList([], [])],
        };
        return (map, fixture, TerrainMap.Build(map, "mission"));
    }

    private static MapObject Placed(string template, decimal x, decimal y, string team, string name = "")
    {
        var properties = ImmutableArray.CreateBuilder<MapProperty>();
        properties.Add(Scr.Text("originalOwner", team));
        if (name.Length > 0) properties.Add(Scr.Text("objectName", name));
        return new MapObject(template, x, y, 0m, 0m, 0, new MapProperties(properties.ToImmutable()));
    }

    private static MissionSession StartMission(MapScript[] scripts, MapScriptGroup[]? groups = null, MapObject[]? objects = null, MissionOptions? options = null, MapScript[]? neutral = null, (string Label, (string Name, decimal X, decimal Y)[] Points)[]? paths = null)
    {
        var (map, fixture, terrain) = MissionMap(scripts, groups, objects, neutral, paths);
        return MissionSession.Start(options ?? MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
    }

    private static void Run(MissionSession session, int ticks)
    {
        for (var index = 0; index < ticks && !session.IsOver; index++) session.Step();
    }

    public static void ScriptSupportTableIsConsistent()
    {
        Check.That(ScriptCoverage.Validate().IsEmpty, "Every name of the support tables is a real condition/action type: " + string.Join(", ", ScriptCoverage.Validate()));
        var conditions = MapScriptCatalog.ConditionTypeNames.GroupBy(ScriptCoverage.ConditionSupport).ToDictionary(group => group.Key, group => group.Count());
        var actions = MapScriptCatalog.ScriptActionTypeNames.GroupBy(ScriptCoverage.ActionSupport).ToDictionary(group => group.Key, group => group.Count());
        Console.WriteLine($"       conditions: {conditions.GetValueOrDefault(ScriptSupport.Executed)} executed, {conditions.GetValueOrDefault(ScriptSupport.Missing)} missing of {MapScriptCatalog.ConditionTypeNames.Length}; " +
                          $"actions: {actions.GetValueOrDefault(ScriptSupport.Executed)} executed, {actions.GetValueOrDefault(ScriptSupport.Forwarded)} forwarded, {actions.GetValueOrDefault(ScriptSupport.Recorded)} recorded, {actions.GetValueOrDefault(ScriptSupport.Missing)} missing of {MapScriptCatalog.ScriptActionTypeNames.Length}");
        // The five condition types and the action types the retail usage table ranks first are all implemented.
        foreach (var name in new[] { "CONDITION_TRUE", "TIMER_EXPIRED", "FLAG", "COUNTER", "PLAYER_HAS_OBJECT_COMPARISON" })
            Check.That(ScriptCoverage.ConditionSupport(name) == ScriptSupport.Executed, $"{name} is implemented.");
        foreach (var name in new[] { "ENABLE_SCRIPT", "OBJECTLIST_ADDOBJECTTYPE", "SET_MILLISECOND_TIMER", "SET_FLAG", "CALL_SUBROUTINE", "SET_COUNTER" })
            Check.That(ScriptCoverage.ActionSupport(name) == ScriptSupport.Executed, $"{name} is implemented.");
        Check.That(ScriptCoverage.ActionSupport("SPEECH_PLAY") == ScriptSupport.Forwarded, "Speech is forwarded to the client.");
        Check.That(ScriptCoverage.ActionSupport("NAMED_SET_TOPPLE_DIRECTION") == ScriptSupport.Missing, "An unimplemented action is reported as missing.");
    }

    public static void ScriptEngineCountersFlagsAndTimers()
    {
        // Script 'Start' runs once: sets a counter, a flag and a 2 s timer. 'Tick' counts while the timer runs; 'Expired' fires when it ends.
        var session = StartMission([
            Scr.Script("Start", [Scr.And(Scr.If("CONDITION_TRUE"))],
                [Scr.Do("SET_COUNTER", Scr.Counter("c"), Scr.Int(5)), Scr.Do("SET_FLAG", Scr.Flag("f"), Scr.Bool(true)), Scr.Do("SET_MILLISECOND_TIMER", Scr.Counter("t"), Scr.Real(2m)),
                 Scr.Do("INCREMENT_COUNTER", Scr.Int(3), Scr.Counter("c")), Scr.Do("DECREMENT_COUNTER", Scr.Int(1), Scr.Counter("c"))], oneShot: true),
            Scr.Script("Expired", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("t")), Scr.If("FLAG", Scr.Flag("f"), Scr.Bool(true)), Scr.If("COUNTER", Scr.Counter("c"), Scr.Compare(Scr.Equal), Scr.Int(7)))],
                [Scr.Do("SET_COUNTER", Scr.Counter("done"), Scr.Int(1))], oneShot: true),
            Scr.Script("NeverStarted", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("other")))], [Scr.Do("SET_COUNTER", Scr.Counter("bad"), Scr.Int(1))]),
        ]);
        var engine = session.Engine;
        Run(session, 1);
        Check.That(engine.CounterValue("c") == 7 && engine.FlagValue("f"), "Set, increment and decrement leave the counter at 7; the flag is set.");
        Check.That(engine.CounterIsTimer("t") && engine.CounterValue("t") == 60, $"A 2 s timer holds 60 frames right after it was set (got {engine.CounterValue("t")}).");
        Check.That(!engine.IsScriptActive("Start"), "A one-shot script deactivates itself after it fired.");
        Run(session, 58);
        Check.That(engine.CounterValue("done") == 0, "The timer has not expired after 59 ticks.");
        Run(session, 4);
        Check.That(engine.CounterValue("done") == 1, $"The timer expired ('done' = {engine.CounterValue("done")}, timer {engine.CounterValue("t")}).");
        Check.That(engine.CounterValue("bad") == 0 && !engine.CounterIsTimer("other"), "A timer that was never started is not expired.");
        Run(session, 120);
        Check.That(engine.CounterValue("t") == -1, "Timers count down to -1 and stop.");
    }

    public static void ScriptEngineActivationDifficultyAndSubroutines()
    {
        var subroutine = Scr.Script("Sub", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("subs"))], subroutine: true);
        var group = new MapScriptGroup("Calls", true, false, [
            Scr.Script("CallIt", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("CALL_SUBROUTINE", Scr.Script("Sub")), Scr.Do("CALL_SUBROUTINE", Scr.Script("Library"))], oneShot: true)]);
        var library = new MapScriptGroup("Library", true, true, [Scr.Script("L1", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(10), Scr.Counter("subs"))]), Scr.Script("L2", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(100), Scr.Counter("subs"))])]);
        var session = StartMission([
            subroutine,
            Scr.Script("Sleeper", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_COUNTER", Scr.Counter("slept"), Scr.Int(1))], active: false),
            Scr.Script("Waker", [Scr.And(Scr.If("FLAG", Scr.Flag("wake"), Scr.Bool(true)))], [Scr.Do("ENABLE_SCRIPT", Scr.Script("Sleeper"))], oneShot: true),
            Scr.Script("EasyOnly", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_COUNTER", Scr.Counter("easy"), Scr.Int(1))], normal: false, hard: false, oneShot: true),
            Scr.Script("NormalOnly", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_COUNTER", Scr.Counter("normal"), Scr.Int(1))], easy: false, hard: false, oneShot: true),
            Scr.Script("Else", [Scr.And(Scr.If("FLAG", Scr.Flag("never"), Scr.Bool(true)))], [], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("else"))], oneShot: true),
            Scr.Script("Delayed", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("delayed"))], delaySeconds: 1),
        ], [group, library]);
        var engine = session.Engine;
        Run(session, 3);
        Check.That(engine.CounterValue("subs") == 111, $"A subroutine script and a subroutine group both ran once when called (got {engine.CounterValue("subs")}); a subroutine never runs on its own.");
        Check.That(engine.CounterValue("slept") == 0 && !engine.IsScriptActive("Sleeper"), "An inactive script does not run.");
        Check.That(engine.CounterValue("normal") == 1 && engine.CounterValue("easy") == 0, "Difficulty flags gate scripts: the Normal game runs the normal-only script, not the easy-only one.");
        Check.That(engine.CounterValue("else") == 1 && !engine.IsScriptActive("Else"), "A false condition runs the false actions once; a one-shot script ends after them.");
        var delayedAtStart = engine.CounterValue("delayed");
        Run(session, 120);
        Check.That(engine.CounterValue("delayed") - delayedAtStart is >= 3 and <= 4, $"A 1 s delayed script evaluates about once a second (got {engine.CounterValue("delayed")} after 4 s).");
        engine.SignalUiInteraction("wake");
        engine.SignalUiInteraction("none");
        Run(session, 1);
        Check.That(engine.IsScriptActive("Sleeper") && !engine.FlagValue("wake"), "A UI interaction satisfies a FLAG condition for one evaluation round without setting the flag (ScriptEngine::evaluateFlag).");
        session.Engine.Describe(new CnCNew.Simulation.State.HashStateWriter());

        var easy = StartMission([Scr.Script("EasyOnly", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_COUNTER", Scr.Counter("easy"), Scr.Int(1))], normal: false, hard: false, oneShot: true)],
            options: MissionOptions.Standard with { Difficulty = ScriptDifficulty.Easy });
        Run(easy, 2);
        Check.That(easy.Engine.CounterValue("easy") == 1, "On Easy the easy-only script runs.");
    }

    public static void ScriptEngineWorldActionsAndConditions()
    {
        var session = StartMission([
            // Teams and units: two soldiers for PlayerB's 'Raiders' at the far waypoint, named one of them; a named barrel of A stands at the spawn.
            Scr.Script("Spawn", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("CREATE_NAMED_ON_TEAM_AT_WAYPOINT", Scr.Unit("Boss"), Scr.Type("BetaSoldier"), Scr.Team("Raiders"), Scr.Waypoint("Far")),
                Scr.Do("CREATE_UNNAMED_ON_TEAM_AT_WAYPOINT", Scr.Type("BetaSoldier"), Scr.Team("Raiders"), Scr.Waypoint("Far")),
                Scr.Do("PLAYER_SET_MONEY", Scr.Side(PlayerA), Scr.Int(1234)), Scr.Do("PLAYER_GIVE_MONEY", Scr.Side(PlayerA), Scr.Int(-34)),
                Scr.Do("PLAYER_RELATES_PLAYER", Scr.Side(PlayerA), Scr.Side(PlayerB), Scr.Int((int)Relationship.Allies))], oneShot: true),
            Scr.Script("HaveRaiders", [Scr.And(Scr.If("TEAM_HAS_UNITS", Scr.Team("Raiders")), Scr.If("NAMED_CREATED", Scr.Unit("Boss")))], [Scr.Do("SET_FLAG", Scr.Flag("raiders"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Two", [Scr.And(Scr.If("PLAYER_HAS_OBJECT_COMPARISON", Scr.Side(PlayerB), Scr.Compare(Scr.GreaterEqual), Scr.Int(2), Scr.Type("BetaSoldier")))],
                [Scr.Do("SET_FLAG", Scr.Flag("two"), Scr.Bool(true)), Scr.Do("NAMED_KILL", Scr.Unit("Boss")), Scr.Do("NAMED_TRANSFER_OWNERSHIP_PLAYER", Scr.Unit("Crate"), Scr.Side(PlayerB))], oneShot: true),
            Scr.Script("Owned", [Scr.And(Scr.If("NAMED_OWNED_BY_PLAYER", Scr.Unit("Crate"), Scr.Side(PlayerB)))], [Scr.Do("SET_FLAG", Scr.Flag("crateMoved"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Dead", [Scr.And(Scr.If("NAMED_DESTROYED", Scr.Unit("Boss")))], [Scr.Do("SET_FLAG", Scr.Flag("bossDead"), Scr.Bool(true)), Scr.Do("TEAM_DELETE", Scr.Team("Raiders")), Scr.Do("NAMED_DELETE", Scr.Unit("Crate"))], oneShot: true),
            Scr.Script("Credits", [Scr.And(Scr.If("PLAYER_HAS_CREDITS", Scr.Int(1200), Scr.Compare(Scr.LessEqual), Scr.Side(PlayerA)))], [Scr.Do("SET_FLAG", Scr.Flag("credits"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Victory", [Scr.And(Scr.If("FLAG", Scr.Flag("bossDead"), Scr.Bool(true)), Scr.If("PLAYER_ALL_DESTROYED", Scr.Side(PlayerB)))], [Scr.Do("VICTORY")], oneShot: true),
        ], objects: [Placed("AlphaBarracks", 100m, 100m, "teamA", "Crate")]);
        var engine = session.Engine;
        var world = session.World;
        // Objects a script creates exist at once (as in the reference), so every later script of the same tick already sees them.
        Run(session, 1);
        Check.That(engine.FlagValue("raiders"), "Units created on a team are members of it and named.");
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(1200), "Money is set, then reduced by a negative gift.");
        Check.That(world.RelationshipOf(new PlayerId(1), new PlayerId(2)) == Relationship.Allies && world.RelationshipOf(new PlayerId(2), new PlayerId(1)) == Relationship.Enemies, "A relation is changed in one direction only.");
        Check.That(engine.FlagValue("two"), "The object comparison counts the created units as the team owner's.");
        Check.That(engine.FlagValue("credits"), "The credit comparison has the parameter on the left.");
        Check.That(engine.FlagValue("crateMoved"), "A named object changes owner.");
        Run(session, 4);
        Check.That(engine.FlagValue("bossDead"), "A killed named object counts as destroyed. " + string.Join(";", engine.ExplainScript("Dead").Select(e => e.Label + "=" + e.Result)) + " named " + string.Join(",", engine.NamedObjects.Select(n => n.Key)) + " ents " + string.Join(",", world.Entities.Select(e => e.Template.Name + (e.EffectivelyDead ? "(dead)" : ""))) + " tick " + world.Tick);
        Check.That(engine.Outcome == MissionOutcome.Victory && engine.OutcomeTick >= 0, "VICTORY sets the outcome once every object of the opponent is gone.");
        Check.That(!session.IsOver, "The mission runs on for the end-game delay after a victory.");
        Run(session, ScriptEngine.EndGameDelayTicks + 5);
        Check.That(engine.IsEnded && session.IsOver, "The end-game delay ends the mission.");
    }

    public static void ScriptEngineOrdersAreRealCommands()
    {
        var session = StartMission([
            Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("CREATE_NAMED_ON_TEAM_AT_WAYPOINT", Scr.Unit("Runner"), Scr.Type("AlphaSoldier"), Scr.Team("teamA"), Scr.Waypoint("Spawn"))], oneShot: true),
            Scr.Script("March", [Scr.And(Scr.If("NAMED_CREATED", Scr.Unit("Runner")))], [Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit("Runner"), Scr.Waypoint("Mid"))], oneShot: true),
            Scr.Script("Arrived", [Scr.And(Scr.If("NAMED_OUTSIDE_AREA", Scr.Unit("Runner"), Scr.Area("LeftArea")))], [Scr.Do("SET_FLAG", Scr.Flag("outside"), Scr.Bool(true))], oneShot: true),
        ]);
        Run(session, 600);
        var runner = session.World.Entities.First(entity => entity.Template.Name == "AlphaSoldier");
        Check.That(runner.X > Fixed.FromInt(200), $"The unit walked out of the area toward the waypoint (x = {runner.X}).");
        Check.That(session.Engine.FlagValue("outside"), "The area condition saw it leave.");
        Check.That(session.World.RejectionCounts.Sum() == 0, "Script orders were accepted by the normal command validation.");
    }

    public static void ScriptEngineMissingTypesAreCounted()
    {
        var session = StartMission([
            Scr.Script("Odd", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_SET_TOPPLE_DIRECTION", Scr.Unit("x"), Scr.Int(0)), Scr.Do("SPEECH_PLAY", Scr.Named(MapScriptParameterType.Dialog, "Hello"), Scr.Bool(false)),
                Scr.Do("CALL_SUBROUTINE", Scr.Script("Nope")), Scr.Do("CREATE_NAMED_ON_TEAM_AT_WAYPOINT", Scr.Unit("u"), Scr.Type("NoSuchObject"), Scr.Team("teamA"), Scr.Waypoint("Spawn"))], oneShot: true),
            Scr.Script("Sighted", [Scr.And(Scr.If("SUPPLY_SOURCE_SAFE", Scr.Side(PlayerA), Scr.Int(0)))], [Scr.Do("SET_FLAG", Scr.Flag("seen"), Scr.Bool(true))]),
        ]);
        Run(session, 3);
        var engine = session.Engine;
        Check.That(engine.MissingActionHits.GetValueOrDefault("NAMED_SET_TOPPLE_DIRECTION") == 1, "An unimplemented action is counted, not skipped silently.");
        Check.That(engine.MissingConditionHits.GetValueOrDefault("SUPPLY_SOURCE_SAFE") == 3 && !engine.FlagValue("seen"), "An unimplemented condition is counted on every evaluation and evaluates to false.");
        Check.That(engine.ForwardedActions == 1, "Speech is forwarded.");
        var output = engine.DrainOutputs();
        Check.That(output.Length == 1 && output[0].Action == "SPEECH_PLAY" && output[0].Arguments[0] == "Hello", "The forwarded action reaches the client with its parameters.");
        Check.That(engine.DrainOutputs().IsEmpty, "Outputs are drained.");
        Check.That(engine.Diagnostics.ContainsKey("call of undefined script 'Nope'") && engine.Diagnostics.ContainsKey("unknown object type 'NoSuchObject'"),
            "Unresolvable references are diagnosed: " + string.Join("; ", engine.Diagnostics.Keys));
    }


    public static void ScriptEngineCommandButtonAbilities()
    {
        // Cmd_AlphaPower (Command = SPECIAL_POWER, SpecialPower = AlphaInstantPower) is a global command button, resolved the same way for the
        // NAMED_* and TEAM_* variants (docs/SCRIPT-ENGINE.md §8); Cmd_AlphaSellNotPower is a real button of a different command type.
        var session = StartMission([
            Scr.Script("UseNamed", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY", Scr.Unit("Hero"), Scr.Ability("Cmd_AlphaPower"))], oneShot: true),
            Scr.Script("UseOnNamed", [Scr.And(Scr.If("CONDITION_TRUE"))],
                [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY_ON_NAMED", Scr.Unit("Hero2"), Scr.Ability("Cmd_AlphaPower"), Scr.Unit("Target"))], oneShot: true),
            Scr.Script("UseTeam", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_USE_COMMANDBUTTON_ABILITY", Scr.Team("teamA"), Scr.Ability("Cmd_AlphaPower"))], oneShot: true),
            Scr.Script("UseTeamOnNamed", [Scr.And(Scr.If("CONDITION_TRUE"))],
                [Scr.Do("TEAM_USE_COMMANDBUTTON_ABILITY_ON_NAMED", Scr.Team("teamA"), Scr.Ability("Cmd_AlphaPower"), Scr.Unit("Target"))], oneShot: true),
            Scr.Script("BadAbility", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY", Scr.Unit("Hero"), Scr.Ability("Cmd_AlphaSellNotPower"))], oneShot: true),
            Scr.Script("UnknownAbility", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY", Scr.Unit("Hero"), Scr.Ability("Cmd_NoSuchButton"))], oneShot: true),
        ], objects: [Placed("AlphaHero", 100m, 100m, "teamA", "Hero"), Placed("AlphaHero", 140m, 100m, "teamA", "Hero2"), Placed("BetaSoldier", 100m, 300m, "teamB", "Target")]);
        Run(session, 1);
        var engine = session.Engine;
        Check.That(engine.ExecutedActions >= 6, "Every ability action (named, on-named, team, team-on-named, the two diagnosed ones) counts as executed, not missing.");
        Check.That(!engine.MissingActionHits.ContainsKey("NAMED_USE_COMMANDBUTTON_ABILITY") && !engine.MissingActionHits.ContainsKey("TEAM_USE_COMMANDBUTTON_ABILITY_ON_NAMED"),
            "The implemented ability actions are no longer counted as missing.");
        Check.That(session.World.RejectionCounts.Sum() == 0, "The resolved special-power uses were accepted as ordinary player commands (ready, unpaused, no required science).");
        Check.That(engine.Diagnostics.ContainsKey("command button 'Cmd_AlphaSellNotPower' has command SELL, not executed"), "A real but non-special-power button is diagnosed, never silently ignored.");
        Check.That(engine.Diagnostics.ContainsKey("unknown command button 'Cmd_NoSuchButton'"), "An unknown button name is diagnosed under its own message.");
    }

    public static void ScriptEngineHumanSideSelection()
    {
        // Neither side of this map sets playerIsHuman = No for PlayerB, but PlayerA is the first human side per MissionMap; requesting PlayerB
        // explicitly must still win over that default (docs/SCRIPT-ENGINE.md §7), and an unknown name must fall back, noted, not silently ignored.
        Check.That(MissionSession.LocalPlayerOf(StartMission([]).Program) == new PlayerId(1), "With no request, the first human side (PlayerA) is the default.");
        var chosen = StartMission([], options: MissionOptions.Standard with { HumanSideName = PlayerB });
        Check.That(MissionSession.LocalPlayerOf(chosen.Program, PlayerB) == new PlayerId(2), "An explicit human side overrides the map's own default.");
        var unknown = StartMission([], options: MissionOptions.Standard with { HumanSideName = "NoSuchSide" });
        Check.That(MissionSession.LocalPlayerOf(unknown.Program, "NoSuchSide") == new PlayerId(1), "An unresolvable name falls back to the map default.");
        Check.That(unknown.Notes.Any(note => note.Contains("NoSuchSide", StringComparison.Ordinal)), "The fallback is recorded as a note, not silent: " + string.Join("; ", unknown.Notes));
    }

    public static void ScriptEngineDamageHistoryAreasAndPaths()
    {
        // A soldier of each side 40 units apart (the gun reaches 80): the session's combat driver makes them shoot each other.
        var session = StartMission([
            Scr.Script("ByPlayer", [Scr.And(Scr.If("NAMED_ATTACKED_BY_PLAYER", Scr.Unit("A1"), Scr.Side(PlayerB)))], [Scr.Do("SET_FLAG", Scr.Flag("byPlayer"), Scr.Bool(true))], oneShot: true),
            Scr.Script("ByType", [Scr.And(Scr.If("NAMED_ATTACKED_BY_OBJECTTYPE", Scr.Unit("A1"), Scr.Type("BetaSoldier")))], [Scr.Do("SET_FLAG", Scr.Flag("byType"), Scr.Bool(true))], oneShot: true),
            Scr.Script("TeamByPlayer", [Scr.And(Scr.If("TEAM_ATTACKED_BY_PLAYER", Scr.Team("teamB"), Scr.Side(PlayerA)))], [Scr.Do("SET_FLAG", Scr.Flag("teamHit"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Kind", [Scr.And(Scr.If("PLAYER_HAS_COMPARISON_UNIT_KIND_IN_TRIGGER_AREA", Scr.Side(PlayerA), Scr.Compare(Scr.GreaterEqual), Scr.Int(1), Scr.Named(MapScriptParameterType.KindOf, "INFANTRY"), Scr.Area("LeftArea")))],
                [Scr.Do("SET_FLAG", Scr.Flag("inArea"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Built", [Scr.And(Scr.If("BUILT_BY_PLAYER", Scr.Named(MapScriptParameterType.ObjectType, "AlphaSoldier"), Scr.Side(PlayerA)))], [Scr.Do("SET_FLAG", Scr.Flag("built"), Scr.Bool(true))], oneShot: true),
            Scr.Script("NotBuilt", [Scr.And(Scr.If("BUILT_BY_PLAYER", Scr.Named(MapScriptParameterType.ObjectType, "BetaBarracks"), Scr.Side(PlayerA)))], [Scr.Do("SET_FLAG", Scr.Flag("notBuilt"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Status", [Scr.And(Scr.If("UNIT_HAS_OBJECT_STATUS", Scr.Unit("A1"), Scr.Named(MapScriptParameterType.ObjectStatus, "UNDER_CONSTRUCTION")))], [Scr.Do("SET_FLAG", Scr.Flag("status"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Stats", [Scr.And(Scr.If("PLAYER_DESTROYED_N_BUILDINGS_PLAYER", Scr.Side(PlayerA), Scr.Int(1), Scr.Side(PlayerB)))], [Scr.Do("SET_FLAG", Scr.Flag("stub"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Factions", [Scr.And(Scr.If("SKIRMISH_PLAYER_FACTION", Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.FactionName, "Nowhere")))], [Scr.Do("SET_FLAG", Scr.Flag("faction"), Scr.Bool(true))], oneShot: true),
            // A walker on a two-waypoint path.
            Scr.Script("Start", [Scr.And(Scr.If("NAMED_CREATED", Scr.Unit("Walker")))], [Scr.Do("NAMED_FOLLOW_WAYPOINTS", Scr.Unit("Walker"), Scr.Named(MapScriptParameterType.WaypointPath, "Route"))], oneShot: true),
            Scr.Script("Arrived", [Scr.And(Scr.If("NAMED_REACHED_WAYPOINTS_END", Scr.Unit("Walker"), Scr.Named(MapScriptParameterType.WaypointPath, "Route")))], [Scr.Do("SET_FLAG", Scr.Flag("arrived"), Scr.Bool(true))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 300m, "teamA", "A1"), Placed("BetaSoldier", 140m, 300m, "teamB", "B1"), Placed("AlphaSoldier", 100m, 100m, "teamA", "Walker")],
            paths: [("Route", [("Mid", 130m, 100m), ("Far", 220m, 100m)])]);
        var engine = session.Engine;
        Run(session, 900);
        Check.That(engine.FlagValue("byPlayer") && engine.FlagValue("byType"), "The last attacker of an object is remembered by player and by type.");
        Check.That(engine.FlagValue("teamHit"), "A team counts as attacked when a member was.");
        Check.That(engine.FlagValue("inArea"), "The kind condition counts live infantry of the player in the area.");
        Check.That(engine.FlagValue("built") && !engine.FlagValue("notBuilt"), "BUILT_BY_PLAYER is about objects the player owns.");
        Check.That(!engine.FlagValue("status") && !engine.FlagValue("stub") && !engine.FlagValue("faction"), "Object status, the reference's stub condition and a foreign faction are false.");
        Check.That(engine.FlagValue("arrived"), "A unit that followed a path to its end reports it.");
        Check.That(engine.MissingConditionHits.Count == 0, "None of these conditions is missing: " + string.Join(",", engine.MissingConditionHits.Keys));
    }

    public static void ScriptEngineAttackPrioritiesAndReinforcements()
    {
        // Two targets in range of the shooter: the nearer barracks and the farther soldier. With a priority set that prefers soldiers the farther one is shot first.
        var session = StartMission([
            Scr.Script("Setup", [Scr.And(Scr.If("NAMED_CREATED", Scr.Unit("Shooter")))], [
                Scr.Do("SET_ATTACK_PRIORITY_THING", Scr.Named(MapScriptParameterType.AttackPrioritySet, "Soldiers"), Scr.Type("BetaSoldier"), Scr.Int(5)),
                Scr.Do("SET_DEFAULT_ATTACK_PRIORITY", Scr.Named(MapScriptParameterType.AttackPrioritySet, "Soldiers"), Scr.Int(0)),
                Scr.Do("NAMED_APPLY_ATTACK_PRIORITY_SET", Scr.Unit("Shooter"), Scr.Named(MapScriptParameterType.AttackPrioritySet, "Soldiers")),
                Scr.Do("CREATE_REINFORCEMENT_TEAM", Scr.Team("Raiders"), Scr.Waypoint("Mid"))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 100m, "teamA", "Shooter"), Placed("BetaBarracks", 130m, 100m, "teamB", "Near"), Placed("BetaSoldier", 170m, 100m, "teamB", "Far")]);
        var engine = session.Engine;
        Run(session, 2);
        var shooter = session.World.FindEntity(new EntityId(engine.NamedObjects.First(item => item.Key == "Shooter").Value))!;
        var near = session.World.FindEntity(new EntityId(engine.NamedObjects.First(item => item.Key == "Near").Value))!;
        var far = session.World.FindEntity(new EntityId(engine.NamedObjects.First(item => item.Key == "Far").Value))!;
        Check.That(engine.AttackPriority(shooter, far) == 5 && engine.AttackPriority(shooter, near) == 0, "The set gives soldiers priority 5 and everything else the set's default.");
        Check.That(engine.AttackPriority(far, shooter) == ScriptEngine.DefaultAttackPriority, "An object without a set uses the default priority.");
        Run(session, 30);
        Check.That(far.Body!.Health < far.Body.MaxHealth && near.Body!.Health == near.Body.MaxHealth, $"The shooter went for the higher-priority target although another one was nearer (far {far.Body.Health}/{far.Body.MaxHealth}, near {near.Body!.Health}/{near.Body!.MaxHealth}, shots {session.CombatStats}).");
    }

    public static void ScriptEngineSnapshotContinuesIdentically()
    {
        MapScript[] Scripts() => [
            Scr.Script("Spawn", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("CREATE_NAMED_ON_TEAM_AT_WAYPOINT", Scr.Unit("Runner"), Scr.Type("AlphaSoldier"), Scr.Team("teamA"), Scr.Waypoint("Spawn")),
                Scr.Do("CREATE_NAMED_ON_TEAM_AT_WAYPOINT", Scr.Unit("Foe"), Scr.Type("BetaSoldier"), Scr.Team("teamB"), Scr.Waypoint("Far")),
                Scr.Do("SET_RANDOM_MSEC_TIMER", Scr.Counter("r"), Scr.Real(2m), Scr.Real(9m)), Scr.Do("OBJECTLIST_ADDOBJECTTYPE", Scr.Named(MapScriptParameterType.ObjectTypeList, "Soldiers"), Scr.Type("AlphaSoldier"))], oneShot: true),
            Scr.Script("March", [Scr.And(Scr.If("NAMED_CREATED", Scr.Unit("Runner")), Scr.If("NAMED_CREATED", Scr.Unit("Foe")))],
                [Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit("Runner"), Scr.Waypoint("Far")), Scr.Do("NAMED_HUNT", Scr.Unit("Foe"))], oneShot: true),
            Scr.Script("Count", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("r")))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("hits")), Scr.Do("SET_MILLISECOND_TIMER", Scr.Counter("r"), Scr.Real(3m))]),
        ];
        var (map, fixture, terrain) = MissionMap(Scripts());
        var (original, restored, bytes) = MissionScenario.VerifyContinuation(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, 900);
        Check.That(original.Chain == restored.Chain && original.FinalHash == restored.FinalHash,
            $"A saved and restored mission continues hash-identically (chain {original.Chain:X16} vs {restored.Chain:X16}, {bytes} bytes).");
        Check.That(original.ExecutedScripts > 0 && original.Shots + original.Entities > 0, "The mission did something.");

        var again = MissionScenario.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, 900);
        Check.That(again.Chain == original.Chain && again.FinalHash == original.FinalHash, "Two fresh runs of the same mission are identical.");
        var other = MissionScenario.Run(MissionOptions.Standard with { WorldSeed = 12345 }, fixture.Templates, fixture.Fingerprint, terrain, map, 900);
        Check.That(other.Chain != original.Chain, "A different logic seed changes the script timers and so the run.");

        // Save, restore and save again: the byte stream is canonical.
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, 200);
        var first = MissionSnapshot.Write(session);
        var copy = MissionSnapshot.Restore(first, fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(MissionSnapshot.Write(copy).AsSpan().SequenceEqual(first), "Restoring and saving again reproduces the snapshot byte for byte.");
        Check.That(copy.Engine.CounterValue("r") == session.Engine.CounterValue("r") && copy.Engine.NamedObjects.SequenceEqual(session.Engine.NamedObjects), "Counters and named objects survive the round trip.");
    }

    public static void ScriptEngineSnapshotRejection()
    {
        var scripts = new[] { Scr.Script("S", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_COUNTER", Scr.Counter("c"), Scr.Int(1))], oneShot: true) };
        var (map, fixture, terrain) = MissionMap(scripts);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, 10);
        var bytes = MissionSnapshot.Write(session);
        Check.That(MissionSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, terrain, map).Engine.CounterValue("c") == 1, "The intact snapshot restores.");

        Check.Throws<SnapshotFormatException>(() => MissionSnapshot.Restore(bytes.AsSpan(0, bytes.Length / 2), fixture.Templates, fixture.Fingerprint, terrain, map), "corrupt");
        var flipped = (byte[])bytes.Clone();
        var at = Array.LastIndexOf(flipped, (byte)'c', flipped.Length - 100);
        flipped[at] ^= 1;
        Check.Throws<SnapshotFormatException>(() => MissionSnapshot.Restore(flipped, fixture.Templates, fixture.Fingerprint, terrain, map), "");
        Check.Throws<SnapshotFormatException>(() => MissionSnapshot.Restore(bytes, fixture.Templates, "0000", terrain, map), "different definitions");
        var otherMap = map with { PlayerScripts = [.. map.PlayerScripts.Select(list => list with { Scripts = [.. list.Scripts, Scr.Script("Extra", [], [])] })] };
        Check.Throws<SnapshotFormatException>(() => MissionSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, terrain, otherMap), "script program");
        var text = System.Text.Encoding.UTF8.GetString(bytes).Replace($"\"version\":{MissionSnapshot.Version},", "\"version\":99,");
        Check.Throws<SnapshotFormatException>(() => MissionSnapshot.Restore(System.Text.Encoding.UTF8.GetBytes(text), fixture.Templates, fixture.Fingerprint, terrain, map), "Unsupported mission state version");
    }
}

static partial class Tests
{
    public static void MissionBlockerShellModules()
    {
        var files = new Dictionary<string, string>(SyntheticFixture.Files)
        {
            ["Data/INI/Object/Shells.ini"] = """
            Object Hunter
              KindOf = INFANTRY SELECTABLE
              Body = ActiveBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = CommandButtonHuntUpdate Hunt01
              End
              Behavior = LockWeaponCreate Lock01
                SlotToLock = PRIMARY
              End
            End
            Object Switcher
              KindOf = INFANTRY SELECTABLE
              Body = ActiveBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = LockWeaponCreate Lock01
                SlotToLock = SECONDARY
              End
            End
            """,
        };
        var fixture = new SyntheticFixture(files: files);
        Check.That(fixture.Templates.TryGet("Hunter", out var hunter) && hunter.IsInstantiable, "An object with the command-button hunt shell and a locked primary slot can be created.");
        Check.That(fixture.Templates.TryGet("Switcher", out var switcher) && !switcher.IsInstantiable && switcher.MissingModules.Any(name => name.StartsWith("LockWeaponCreate", StringComparison.Ordinal)),
            "Locking any other slot is an unsupported configuration and named as missing: " + string.Join(",", switcher?.MissingModules ?? []));
    }
}

using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>The computer players of a mission: every side that is not human has an <c>AIPlayer</c> (F-91, docs/SKIRMISH-AI-ORIGINAL.md §10).</summary>
static partial class Tests
{
    private static readonly PlayerId HumanOfAiMission = new(1);
    private static readonly PlayerId ComputerOfAiMission = new(2);

    private static MapSide AiMissionSide(string name, bool human, string faction, string enemy, ImmutableArray<MapBuildListEntry> buildList, params MapProperty[] extra)
        => new(new MapProperties([Scr.Text("playerName", name), Scr.Flag("playerIsHuman", human), Scr.Text("playerFaction", faction), Scr.Text("playerAllies", string.Empty), Scr.Text("playerEnemies", enemy), ..extra]), buildList);

    /// <summary>
    /// The map of <see cref="MissionMap"/> with factions on its two sides: <see cref="PlayerA"/> is human (<c>FactionAlpha</c>), <see cref="PlayerB"/> the computer (<c>FactionBeta</c>) with the given
    /// build list and teams and with <paramref name="scripts"/> in its own script list.
    /// </summary>
    private static (MapData Map, SyntheticFixture Fixture, TerrainMap Terrain) AiMission(MapScript[] scripts, MapObject[]? objects = null, MapBuildListEntry[]? buildList = null, MapProperties[]? teams = null,
        IReadOnlyDictionary<string, string>? files = null, MapProperty[]? computerSide = null, MapScript[]? human = null)
    {
        var (map, fixture, _) = MissionMap(human ?? [], objects: objects, extraFiles: files);
        map = map with
        {
            Sides = [map.Sides[0], AiMissionSide(PlayerA, true, "FactionAlpha", PlayerB, []), AiMissionSide(PlayerB, false, "FactionBeta", PlayerA, [..buildList ?? []], computerSide ?? [])],
            Teams = [..map.Teams, ..teams ?? []],
            PlayerScripts = [map.PlayerScripts[0], map.PlayerScripts[1], new MapScriptList([..scripts], [])],
        };
        return (map, fixture, TerrainMap.Build(map, "mission"));
    }

    private static MissionSession StartAiMission(MapScript[] scripts, MapObject[]? objects = null, MapBuildListEntry[]? buildList = null, MapProperties[]? teams = null,
        IReadOnlyDictionary<string, string>? files = null, MapProperty[]? computerSide = null, MissionOptions? options = null)
    {
        var (map, fixture, terrain) = AiMission(scripts, objects, buildList, teams, files, computerSide);
        return MissionSession.Start(options ?? MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
    }

    /// <summary><c>Player::init</c>: a template without money gives <c>DefaultStartingCash</c>; <c>Player::initFromDict</c>: <c>playerStartMoney</c> comes on top.</summary>
    public static void MissionPlayersStartWithTheDefaultCash()
    {
        var files = new Dictionary<string, string>
        {
            ["Data/INI/GameData.ini"] = "GameData\n  SellPercentage = 50%\n  DefaultStartingCash = 3000\nEnd\n",
            ["Data/INI/Object/PoorFixture.ini"] = "PlayerTemplate FactionPoor\n  Side = Beta\n  PlayableSide = Yes\nEnd\n",
        };
        var (map, fixture, terrain) = AiMission([], files: files, computerSide: [IntProperty("playerStartMoney", 250)]);
        var money = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(fixture.Templates.DefaultStartingCash == 3000, "The key of GameData.ini is read.");
        Check.That(money.World.MoneyOf(HumanOfAiMission) == Fixed.FromInt(500), $"A template with money keeps it ({money.World.MoneyOf(HumanOfAiMission)}).");
        Check.That(money.World.MoneyOf(ComputerOfAiMission) == Fixed.FromInt(750), $"The map's playerStartMoney comes on top ({money.World.MoneyOf(ComputerOfAiMission)}).");

        var poor = map with { Sides = [map.Sides[0], map.Sides[1], AiMissionSide(PlayerB, false, "FactionPoor", PlayerA, [], IntProperty("playerStartMoney", 250))] };
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, TerrainMap.Build(poor, "mission"), poor);
        Check.That(session.World.MoneyOf(ComputerOfAiMission) == Fixed.FromInt(3250), $"A template without money gives the default cash, the map's money on top ({session.World.MoneyOf(ComputerOfAiMission)}).");

        var unknown = map with { Sides = [map.Sides[0], map.Sides[1], AiMissionSide(PlayerB, false, "FactionNobody", PlayerA, [])] };
        var nobody = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, TerrainMap.Build(unknown, "mission"), unknown);
        Check.That(nobody.World.MoneyOf(ComputerOfAiMission) == Fixed.Zero, "A faction that is no template is initialised like the neutral player: without money.");

        Check.Throws<TemplateBuildException>(() => _ = new SyntheticFixture(TestRate.Default, files: new Dictionary<string, string>(ScriptFiles()) { ["Data/INI/GameData.ini"] = "GameData\n  DefaultStartingCash = plenty\nEnd\n" }), "DefaultStartingCash");
    }

    /// <summary>
    /// <c>ProductionUpdate::update</c>: a unit that may not be built any more is cancelled and refunded when it comes to the head of the queue; a dozer is not. <c>PLAYER_DISABLE_UNIT_CONSTRUCTION</c> did not
    /// touch a queue before.
    /// </summary>
    public static void ADisabledFactoryRefundsItsUnitsButNotItsDozers()
    {
        var session = StartMission([Scr.Script("Off", [Scr.And(Scr.If("FLAG", Scr.Flag("off"), Scr.Bool(true)))], [Scr.Do("PLAYER_DISABLE_UNIT_CONSTRUCTION", Scr.Side(PlayerA))], oneShot: true)],
            objects: [Placed("AlphaCenter", 100m, 100m, "teamA", "Hq")]);
        Run(session, session.Frames(2));
        session.World.SetMoney(HumanOfAiMission, Fixed.FromInt(1000));
        var hq = NamedIn(session, "Hq")!;
        var production = hq.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.ProductionUpdate>().First();
        foreach (var template in new[] { "AlphaSoldier", "AlphaDozer" })
            Check.That(session.Issue(HumanOfAiMission, (sequence, tick) => new CnCNew.Simulation.Commands.QueueProductionCommand(HumanOfAiMission, sequence, tick, hq.Id, template)).Accepted, $"{template} is queued.");
        Run(session, 1);
        Check.That(session.World.MoneyOf(HumanOfAiMission) == Fixed.FromInt(955) && production.Queue.Length == 2, $"Both are paid for ({session.World.MoneyOf(HumanOfAiMission)}).");
        session.Engine.SignalUiInteraction("off");
        Run(session, session.Frames(2));
        Check.That(!session.World.CanBuildUnits(HumanOfAiMission), "The script switched units off.");
        Check.That(session.World.MoneyOf(HumanOfAiMission) == Fixed.FromInt(980), $"The soldier at the head of the queue is refunded ({session.World.MoneyOf(HumanOfAiMission)}).");
        Check.That(production.Queue.Length == 1 && session.World.Templates[production.Queue[0].TemplateIndex].Name == "AlphaDozer", "The dozer stays in the queue.");
        Run(session, session.Frames(60));
        Check.That(production.Queue.Length == 0 && session.World.Entities.Any(entity => entity.Template.Name == "AlphaDozer"), "And is built.");
    }

    // ------------------------------------------------------------------------------------------------ the AIPlayer of a mission

    private static IReadOnlyDictionary<string, string> AiMissionFiles() => new Dictionary<string, string>
    {
        ["Data/INI/Default/AIData.ini"] = AiDataText(),
        ["Data/INI/Science.ini"] = AiScienceText,
    };

    private static MapBuildListEntry AiEntry(string template, decimal x, decimal y, bool built = false, int rebuilds = 0, string name = "", string script = "")
        => new(name, template, x, y, 0m, built, rebuilds, script, 100, true, false, true);

    private static MapProperties AiTeam(string name, string unit, int count, string condition = "", int priority = 0, int instances = 1, bool singleton = false, string home = "", int idleFrames = 0)
        => new([
            Scr.Text("teamName", name), Scr.Text("teamOwner", PlayerB), Scr.Flag("teamIsSingleton", singleton), IntProperty("teamMaxInstances", instances),
            Scr.Text("teamUnitType1", unit), IntProperty("teamUnitMinCount1", count), IntProperty("teamUnitMaxCount1", count), Scr.Text("teamProductionCondition", condition),
            IntProperty("teamProductionPriority", priority), Scr.Text("teamHome", home), IntProperty("teamInitialIdleFrames", idleFrames),
        ]);

    private static MapScript OnFlag(string flag, params MapScriptAction[] actions) => Scr.Script("On " + flag, [Scr.And(Scr.If("FLAG", Scr.Flag(flag), Scr.Bool(true)))], actions);

    private static MapScript AtStart(params MapScriptAction[] actions) => Scr.Script("At start", [Scr.And(Scr.If("CONDITION_TRUE"))], actions, oneShot: true);

    private static readonly MapScriptAction EnableUnits = Scr.Do("PLAYER_ENABLE_UNIT_CONSTRUCTION", Scr.Side(PlayerB));

    private static int CountOf(MissionSession session, string template) => session.World.Entities.Count(entity => entity.Template.Name == template && !entity.DestroyRequested && !entity.EffectivelyDead);

    /// <summary>
    /// <c>Player::setPlayerType</c>, <c>AIPlayer::AIPlayer</c>, <c>AIPlayer::newMap</c>: every side that is not human has an AI. Its list is the map's; every production object it owns on the map joins the head of the
    /// list with no rebuilds, the oldest first, an entry not built at the start gets one rebuild for its first build, and its unit production starts off. Before, a computer player of a mission had none of it.
    /// </summary>
    public static void MissionComputerPlayersHaveAnAi()
    {
        var session = StartAiMission([],
            objects: [Placed("BetaBarracks", 300m, 100m, "teamB", "Yard1"), Placed("BetaBarracks", 340m, 100m, "teamB", "Yard2"), Placed("AlphaBarracks", 100m, 100m, "teamA", "Ours"), Placed("BetaSoldier", 300m, 200m, "teamB")],
            buildList: [AiEntry("BetaCenter", 400m, 100m, built: true, rebuilds: 2, name: "Hq"), AiEntry("BetaPlant", 400m, 160m, rebuilds: 1), AiEntry("BetaPlant", 440m, 160m, rebuilds: -1)],
            files: AiMissionFiles());
        var (human, computer) = (HumanOfAiMission, ComputerOfAiMission);
        Check.That(!session.Engine.IsSkirmish && session.Engine.HasAiPlayers, "A mission's computer players are no skirmish AI.");
        Check.That(session.Engine.AiBuildListOf(human).IsEmpty, "The human player has no AI.");
        var list = session.Engine.AiBuildListOf(computer);
        Check.That(list.Select(entry => entry.Template).SequenceEqual(["BetaBarracks", "BetaBarracks", "BetaCenter", "BetaPlant", "BetaPlant"]), $"The production objects of the map come first ({string.Join(", ", list.Select(entry => entry.Template))}).");
        Check.That(list[0].ObjectId == NamedIn(session, "Yard1")!.Id.Value && list[1].ObjectId == NamedIn(session, "Yard2")!.Id.Value, "The oldest object heads the list.");
        Check.That(list[2].ObjectId == NamedIn(session, "Hq")!.Id.Value, "What the list builds at the start stands, under its name.");
        Check.That(session.Engine.AiRebuildsOf(computer).SequenceEqual([0, 0, 2, 2, -1]), $"Placed objects have no rebuilds, a first build costs one that is added, unlimited stays ({string.Join(", ", session.Engine.AiRebuildsOf(computer))}).");
        Check.That(!session.World.CanBuildUnits(computer) && session.World.CanBuildStructures(computer), "\"turn off ai production by default\": units are off, structures on.");
        Check.That(session.World.CanBuildUnits(human), "The human player's are on.");
        Check.That(session.World.HolesRebuild, "A hole rebuilds in a mission, as in every game of the original.");
        var (centreX, centreY) = session.Engine.AiBaseCentreOf(computer);
        Check.That(centreX == Fixed.FromInt((300 + 340 + 400 + 400 + 440) / 5) && centreY == Fixed.FromInt((100 + 100 + 100 + 160 + 160) / 5), $"The base centre is the mean of the list ({centreX}, {centreY}).");
    }

    /// <summary>
    /// <c>AIPlayer::processBaseBuilding</c>: an entry with rebuilds left and no object is built by a dozer; a destroyed one waits <c>RebuildDelayTimeSeconds</c> and comes back while it has rebuilds, under the name of its
    /// entry, and runs the entry's script when it is finished. Base building needs no script and no unit production.
    /// </summary>
    public static void MissionAiBuildsAndRebuildsItsBase()
    {
        var session = StartAiMission(
            [OnFlag("kill", Scr.Do("NAMED_KILL", Scr.Unit("Plant"))), Scr.Script("Done", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("finished"))], subroutine: true)],
            objects: [Placed("BetaDozer", 300m, 120m, "teamB", "Digger"), Placed("BetaCenter", 300m, 100m, "teamB", "Hq")],
            buildList: [AiEntry("BetaPlant", 360m, 160m, rebuilds: 1, name: "Plant", script: "Done")],
            files: AiMissionFiles());
        Run(session, session.Frames(150));
        Check.That(CountOf(session, "BetaPlant") == 1 && NamedIn(session, "Plant") is { } first && !first.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction), $"The dozer builds the entry ({CountOf(session, "BetaPlant")}; diagnostics: {string.Join("; ", session.Engine.Diagnostics.Keys)}).");
        Check.That(session.Engine.CounterValue("finished") == 1, "The finished structure ran the script of its entry.");
        Check.That(session.Engine.AiRebuildsOf(ComputerOfAiMission)[^1] == 1, "The first build used one of the two rebuilds.");
        session.Engine.SignalUiInteraction("kill");
        Run(session, session.Frames(50));
        Check.That(CountOf(session, "BetaPlant") == 0, "It is gone, and two seconds of RebuildDelayTimeSeconds have not passed.");
        Run(session, session.Frames(300));
        Check.That(CountOf(session, "BetaPlant") == 1 && NamedIn(session, "Plant") is not null, "It comes back, under its name.");
        Check.That(session.Engine.AiRebuildsOf(ComputerOfAiMission)[^1] == 0 && session.Engine.CounterValue("finished") == 2, "The last rebuild is used.");
        session.Engine.SignalUiInteraction("kill");
        Run(session, session.Frames(450));
        Check.That(CountOf(session, "BetaPlant") == 0, "Without rebuilds it stays gone.");
    }

    /// <summary>
    /// <c>AIPlayer::findDozer</c>, <c>queueDozer</c>: a player that needs a structure and has no dozer orders one, also with unit production off ("turn on unit building for a moment"), and the factory is the object the
    /// map placed. What it cannot pay is left for the next entry of the list.
    /// </summary>
    public static void MissionAiOrdersADozerWithUnitsOff()
    {
        var session = StartAiMission([], objects: [Placed("BetaCenter", 300m, 100m, "teamB", "Hq")],
            buildList: [AiEntry("BetaBarracks", 360m, 160m, rebuilds: 1), AiEntry("BetaPlant", 400m, 160m, rebuilds: 1)], files: AiMissionFiles());
        session.World.SetMoney(ComputerOfAiMission, Fixed.FromInt(85));
        Check.That(!session.World.CanBuildUnits(ComputerOfAiMission), "Units are off.");
        Run(session, session.Frames(100));
        Check.That(CountOf(session, "BetaDozer") == 1, $"The dozer is trained ({CountOf(session, "BetaDozer")}; diagnostics: {string.Join("; ", session.Engine.Diagnostics.Keys)}).");
        Check.That(!session.World.CanBuildUnits(ComputerOfAiMission), "Units are off again.");
        Run(session, session.Frames(120));
        Check.That(CountOf(session, "BetaBarracks") == 0 && CountOf(session, "BetaPlant") == 1, $"The 65 credits left do not pay the barracks at the head of the list, the plant behind it is built ({CountOf(session, "BetaBarracks")}, {CountOf(session, "BetaPlant")}).");
    }

    /// <summary>
    /// <c>Player::buildSpecificTeam</c>: a human player has no AI and builds no team, and <c>buildSpecificAITeam</c> builds nothing while unit production is off. Two ways to the same team do not build it twice.
    /// </summary>
    public static void BuildTeamBelongsToTheAi()
    {
        var teams = new[]
        {
            AiTeam("Gang", "BetaSoldier", 2, condition: "Gang wanted", priority: 3),
            new MapProperties([Scr.Text("teamName", "Theirs"), Scr.Text("teamOwner", PlayerA), Scr.Flag("teamIsSingleton", false), IntProperty("teamMaxInstances", 1), Scr.Text("teamUnitType1", "AlphaSoldier"), IntProperty("teamUnitMinCount1", 1), IntProperty("teamUnitMaxCount1", 1)]),
        };
        var session = StartAiMission(
            [
                Scr.Script("Gang wanted", [Scr.And(Scr.If("FLAG", Scr.Flag("choose"), Scr.Bool(true)))], [], subroutine: true),
                AtStart(Scr.Do("BUILD_TEAM", Scr.Team("Gang")), Scr.Do("BUILD_TEAM", Scr.Team("Theirs"))),
                OnFlag("on", EnableUnits, Scr.Do("BUILD_TEAM", Scr.Team("Gang"))),
            ],
            objects: [Placed("BetaCenter", 300m, 100m, "teamB", "Hq"), Placed("AlphaCenter", 100m, 100m, "teamA", "Ours")],
            teams: teams, files: AiMissionFiles());
        Run(session, session.Frames(60));
        Check.That(CountOf(session, "BetaSoldier") == 0 && session.Engine.Diagnostics.ContainsKey("BUILD_TEAM 'Gang': unit construction disabled"), "With unit production off a script's team is refused.");
        Check.That(CountOf(session, "AlphaSoldier") == 0 && session.Engine.Diagnostics.ContainsKey("BUILD_TEAM 'Theirs': a human player has no AI"), "A human player builds no team.");
        session.Engine.SignalUiInteraction("on");
        Run(session, session.Frames(300));
        var gang = session.Engine.TeamInstances.Single(team => team.Prototype == "Gang");
        Check.That(gang.Created && gang.Members.Count == 2 && CountOf(session, "BetaSoldier") == 2, $"Once it is on the team is built ({gang.Members.Count}, {CountOf(session, "BetaSoldier")}; diagnostics: {string.Join("; ", session.Engine.Diagnostics.Keys)}).");
    }

    /// <summary>
    /// <c>AIPlayer::doTeamBuilding</c>, <c>selectTeamToBuild</c>: with unit production on, a team whose production condition holds is built by the AI itself, the highest priority first, and only as many instances as
    /// the prototype allows. A singleton's team always exists, so the timer never chooses it. The team goes through the queues once.
    /// </summary>
    public static void MissionAiTrainsTheTeamsOfItsConditions()
    {
        var teams = new[]
        {
            AiTeam("Low", "BetaSoldier", 1, condition: "Wanted", priority: 1),
            AiTeam("High", "BetaDozer", 1, condition: "Wanted", priority: 5),
            AiTeam("Never", "BetaSoldier", 5, condition: "Not wanted", priority: 9),
            AiTeam("Lone", "BetaSoldier", 5, condition: "Wanted", priority: 8, singleton: true),
        };
        var session = StartAiMission(
            [
                Scr.Script("Wanted", [Scr.And(Scr.If("CONDITION_TRUE"))], [], subroutine: true),
                Scr.Script("Not wanted", [Scr.And(Scr.If("FLAG", Scr.Flag("never"), Scr.Bool(true)))], [], subroutine: true),
                AtStart(EnableUnits),
            ],
            objects: [Placed("BetaCenter", 300m, 100m, "teamB", "Hq"), Placed("BetaDozer", 320m, 140m, "teamB", "First")],
            teams: teams, files: AiMissionFiles());
        Run(session, session.Frames(60));
        Check.That(CountOf(session, "BetaDozer") == 2 && CountOf(session, "BetaSoldier") == 0, $"The highest priority that is allowed comes first ({CountOf(session, "BetaDozer")}, {CountOf(session, "BetaSoldier")}; diagnostics: {string.Join("; ", session.Engine.Diagnostics.Keys)}).");
        Run(session, session.Frames(600));
        var low = session.Engine.TeamInstances.Single(team => team.Prototype == "Low");
        Check.That(low.Created && low.Members.Count == 1 && CountOf(session, "BetaSoldier") == 1, $"Then the next one, once ({low.Members.Count}, {CountOf(session, "BetaSoldier")}).");
        Check.That(CountOf(session, "BetaDozer") == 2, "A team at its limit is not built again.");
    }

    /// <summary><c>AIPlayer::onUnitProduced</c> matches an order by its factory: what a script creates while the unit is being trained fills no order, however alike the two are.</summary>
    public static void ATeamTakesTheUnitOfItsFactory()
    {
        var session = StartAiMission([AtStart(EnableUnits, Scr.Do("BUILD_TEAM", Scr.Team("Gang")))], objects: [Placed("BetaCenter", 300m, 100m, "teamB", "Hq")],
            teams: [AiTeam("Gang", "BetaSoldier", 1, priority: 2)], files: AiMissionFiles());
        Run(session, session.Frames(6));
        Check.That(session.World.Templates.TryGet("BetaSoldier", out var soldier), "The fixture has the soldier.");
        var stray = session.World.RequestSpawn(soldier.Index, ComputerOfAiMission, Fixed.FromInt(100), Fixed.FromInt(300), Fixed.Zero, Fixed.Zero);
        Run(session, session.Frames(300));
        var gang = session.Engine.TeamInstances.Single(team => team.Prototype == "Gang");
        Check.That(gang.Created && gang.Members.Count == 1 && !gang.Members.Contains(stray.Value) && CountOf(session, "BetaSoldier") == 2, $"The team has the trained soldier, not the other one ({gang.Members.Count}, {CountOf(session, "BetaSoldier")}).");
    }

    /// <summary>
    /// <c>doTeamBuilding</c> of <c>AIPlayer</c> looks at the queues every five seconds (<c>AISkirmishPlayer</c> every two) and its team timer has no cap of three seconds; <c>SET_BASE_CONSTRUCTION_SPEED</c> sets it.
    /// </summary>
    public static void MissionAiTeamTimerIsTheScripts()
    {
        var teams = new[] { AiTeam("A", "BetaSoldier", 1, condition: "Wanted", instances: 9), };
        var session = StartAiMission(
            [
                Scr.Script("Wanted", [Scr.And(Scr.If("CONDITION_TRUE"))], [], subroutine: true),
                AtStart(EnableUnits, Scr.Do("SET_BASE_CONSTRUCTION_SPEED", Scr.Side(PlayerB), Scr.Int(40))),
            ],
            objects: [Placed("BetaCenter", 300m, 100m, "teamB", "Hq")], teams: teams, files: AiMissionFiles());
        var computer = ComputerOfAiMission;
        var first = -1L;
        for (var frame = 0; frame < 300 && first < 0; frame++)
        {
            Run(session, session.Frames(1));
            if (session.Engine.AiTeamReadyTick(computer) > session.Frames(1)) first = session.Engine.AiTeamReadyTick(computer) - session.World.Tick;
        }
        Check.That(first > session.Frames(37 * 30) && first <= session.Frames(40 * 30), $"The timer is the script's 40 seconds, no less ({session.World.InFrames(first)} frames).");
    }

    /// <summary><c>AIPlayer::queueUnits</c>: a work order first recruits from the player's default team around the team's home, and the recruit walks there; only what is missing is trained.</summary>
    public static void MissionAiRecruitsAroundTheHomeOfTheTeam()
    {
        var team = AiTeam("Strike", "BetaSoldier", 2, priority: 5, home: "Far");
        var session = StartAiMission([AtStart(EnableUnits, Scr.Do("BUILD_TEAM", Scr.Team("Strike")))],
            objects: [Placed("BetaCenter", 100m, 100m, "teamB", "Hq"), Placed("BetaSoldier", 420m, 200m, "teamPlayerB", "Near"), Placed("BetaSoldier", 100m, 300m, "teamPlayerB", "Remote")],
            teams: [team, new MapProperties([Scr.Text("teamName", "teamPlayerB"), Scr.Text("teamOwner", PlayerB), Scr.Flag("teamIsSingleton", true)])], files: AiMissionFiles());
        Run(session, session.Frames(30));
        var strike = session.Engine.TeamInstances.Single(instance => instance.Prototype == "Strike");
        Check.That(strike.Members.Contains(NamedIn(session, "Near")!.Id.Value), "The unit near the team's home is recruited (the base centre is far from it).");
        Run(session, session.Frames(600));
        Check.That(strike.Created && strike.Members.Count == 2 && CountOf(session, "BetaSoldier") == 2, $"Nothing more than the team is made ({strike.Members.Count}, {CountOf(session, "BetaSoldier")}).");
        var near = NamedIn(session, "Near")!;
        Check.That(DistanceBetween(near, 400m, 200m) < 60m, $"The recruit walks to the home of the team ({near.X}, {near.Y}).");
    }

    private static decimal DistanceBetween(Entity entity, decimal x, decimal y)
    {
        var dx = entity.X.ToDecimal() - x;
        var dy = entity.Y.ToDecimal() - y;
        return Math.Abs(dx) + Math.Abs(dy);
    }

    /// <summary><c>AIPlayer::doUpgradesAndSkills</c>: "Non-skirmish default to 0" - the first skill set is bought from without a draw, unless <c>PLAYER_SELECT_SKILLSET</c> chose another.</summary>
    public static void MissionAiBuysTheSciencesOfItsSkillSet()
    {
        MissionSession Start(params MapScript[] scripts) => StartAiMission(scripts, objects: [Placed("BetaCenter", 300m, 100m, "teamB", "Hq")], files: AiMissionFiles());
        var plain = Start();
        var chosen = Start(AtStart(Scr.Do("PLAYER_SELECT_SKILLSET", Scr.Side(PlayerB), Scr.Int(2))));
        foreach (var session in new[] { plain, chosen })
        {
            Run(session, session.Frames(4));
            session.World.AddSciencePoints(ComputerOfAiMission, 1);
            Run(session, session.Frames(4));
            Check.That(session.World.SciencePointsOf(ComputerOfAiMission) == 0, "The point is spent.");
        }
        static bool Bought(MissionSession session, string science) => session.World.Sciences.TryGet(science, out var info) && session.World.PlayerSciences(ComputerOfAiMission).Has(info.Index);
        Check.That(Bought(plain, "SCIENCE_One") && !Bought(plain, "SCIENCE_Three"), "Without a choice it is the first set.");
        Check.That(Bought(chosen, "SCIENCE_Three") && !Bought(chosen, "SCIENCE_One"), "With one it is the chosen set.");
    }

    /// <summary>
    /// The six actions of the build list. <c>AIPlayer</c> does not support the five of the skirmish ("Solo ai doesn't support"): nothing happens and nothing counts as missing. <c>AI_PLAYER_BUILD_TYPE_NEAREST_TEAM</c>
    /// is <c>AIPlayer</c>'s: a place near the team goes to the head of the list as a priority entry, built once.
    /// </summary>
    public static void MissionAiAndTheBuildActions()
    {
        var session = StartAiMission(
            [
                AtStart(Scr.Do("SKIRMISH_BUILD_BUILDING", Scr.Type("BetaPlant")), Scr.Do("SKIRMISH_BUILD_BASE_DEFENSE_FRONT"), Scr.Do("SKIRMISH_BUILD_BASE_DEFENSE_FLANK"),
                    Scr.Do("SKIRMISH_BUILD_STRUCTURE_FRONT", Scr.Type("BetaPlant")), Scr.Do("SKIRMISH_BUILD_STRUCTURE_FLANK", Scr.Type("BetaPlant"))),
                OnFlag("near", Scr.Do("AI_PLAYER_BUILD_TYPE_NEAREST_TEAM", Scr.Side(PlayerB), Scr.Type("BetaPlant"), Scr.Team("teamB"))),
            ],
            objects: [Placed("BetaCenter", 300m, 100m, "teamB", "Hq"), Placed("BetaDozer", 320m, 140m, "teamB", "Digger")],
            buildList: [AiEntry("BetaPlant", 360m, 160m)], files: AiMissionFiles());
        Run(session, session.Frames(60));
        Check.That(session.Engine.MissingActionHits.Count == 0, "None of them is missing: " + string.Join(", ", session.Engine.MissingActionHits.Keys));
        Check.That(session.Engine.Diagnostics.ContainsKey("SKIRMISH_BUILD_BUILDING: a mission's AI does not support it"), "The original's message has its place.");
        var before = session.Engine.AiBuildListOf(ComputerOfAiMission).Length;
        session.Engine.SignalUiInteraction("near");
        Run(session, session.Frames(2));
        var list = session.Engine.AiBuildListOf(ComputerOfAiMission);
        Check.That(list.Length == before + 1 && list[0] is { Template: "BetaPlant", Priority: true }, "The action makes a priority entry at the head of the list.");
    }

    /// <summary>
    /// The state of a mission's AI is saved and hashed: a session saved at any tick, also inside a frame above 30 Hz, continues identically, while it builds its base and trains a team, and a second run is identical.
    /// </summary>
    public static void MissionAiContinuesFromAnyTick()
    {
        var (map, fixture, terrain) = AiMission(
            [
                Scr.Script("Wanted", [Scr.And(Scr.If("CONDITION_TRUE"))], [], subroutine: true),
                AtStart(EnableUnits, Scr.Do("BUILD_TEAM", Scr.Team("Gang"))),
                Scr.Script("Named", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("finished"))], subroutine: true),
            ],
            objects: [Placed("BetaCenter", 300m, 100m, "teamB", "Hq"), Placed("BetaDozer", 320m, 140m, "teamB", "Digger")],
            buildList: [AiEntry("BetaPlant", 360m, 160m, rebuilds: 2, name: "Plant", script: "Named"), AiEntry("BetaBarracks", 400m, 160m, rebuilds: 1)],
            teams: [AiTeam("Gang", "BetaSoldier", 2, priority: 2, instances: 2), AiTeam("Army", "BetaSoldier", 1, condition: "Wanted", priority: 1, instances: 3)],
            files: AiMissionFiles());
        var total = checked((int)fixture.Templates.Rate.Time.OriginalFrames(240));
        var (first, second, _) = MissionScenario.VerifyContinuation(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, total);
        Check.That(first.Chain == second.Chain && first.FinalHash == second.FinalHash, "A mission saved halfway continues identically.");
        var step = checked((int)fixture.Templates.Rate.Time.TicksPerOriginalFrame) + 1;
        for (var saved = step; saved < total / 2; saved += total / 7 + step)
        {
            var original = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            while (original.World.Tick < saved) original.Step();
            var restored = MissionSnapshot.Restore(MissionSnapshot.Write(original), fixture.Templates, fixture.Fingerprint, terrain, map);
            Check.That(restored.StateHash() == original.StateHash(), $"The state of tick {saved} is what was saved.");
            for (var tick = saved; tick < saved + fixture.Templates.Rate.Time.OriginalFrames(200); tick++)
            {
                original.Step();
                restored.Step();
                Check.That(restored.StateHash() == original.StateHash(), $"Saved at tick {saved}, the session departs at tick {tick}.");
            }
        }
    }
}

using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Skirmish.Original;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

/// <summary>The original skirmish AI (docs/SKIRMISH-AI-ORIGINAL.md): its data, the attachment of the script library to the slots of a match, and matches played by it.</summary>
static partial class Tests
{
    private static string AiSideData(string side) => $$"""
          SideInfo {{side}}
            ResourceGatherersEasy = 1
            ResourceGatherersNormal = 2
            ResourceGatherersHard = 3
            BaseDefenseStructure1 = {{side}}Plant
            SkillSet1
              Science = SCIENCE_One
              Science = SCIENCE_Two
            End
            SkillSet2
              Science = SCIENCE_Three
            End
          End
          SkirmishBuildList {{side}}
            Structure {{side}}Center
              Location = X:100.00 Y:100.00
              Rebuilds = 0
              Angle = -135.00
              InitiallyBuilt = No
              AutomaticallyBuild = Yes
            END ;Structure {{side}}Center
            Structure {{side}}Plant
              Location = X:100.00 Y:160.00
              Rebuilds = 0
              Angle = 0.00
              InitiallyBuilt = No
              AutomaticallyBuild = No
            END
            Structure {{side}}Barracks
              Location = X:160.00 Y:100.00
              Rebuilds = 0
              Angle = 0.00
              InitiallyBuilt = No
              AutomaticallyBuild = No
            END
          End
        """;

    private static string AiDataText(string extraTopLevel = "", string extraSide = "") => $$"""
        AIData
          StructureSeconds = 0.0
          TeamSeconds = 2
          Wealthy = 7000
          Poor = 100
          StructuresWealthyRate = 2.0
          StructuresPoorRate = 0.6
          TeamsWealthyRate = 2.0
          TeamsPoorRate = 0.6
          TeamResourcesToStart = 0.1
          MaxRecruitRadius = 500.0
          SkirmishBaseDefenseExtraDistance = 40.0
          RotateSkirmishBases = No
          SupplyCenterSafeRadius = 30.0
          RebuildDelayTimeSeconds = 2
          GuardInnerModifierAI = 1.1
          {{extraTopLevel}}
        {{AiSideData("Alpha")}}
        {{AiSideData("Beta")}}
          {{extraSide}}
        End
        """;

    private const string AiScienceText = "Science SCIENCE_One\n  SciencePurchasePointCost = 1\nEnd\nScience SCIENCE_Two\n  SciencePurchasePointCost = 1\nEnd\nScience SCIENCE_Three\n  SciencePurchasePointCost = 1\nEnd\n";

    private static Dictionary<string, string> AiFiles(string aiData)
        => new(SkirmishFiles()) { ["Data/INI/Default/AIData.ini"] = aiData, ["Data/INI/Science.ini"] = AiScienceText };

    private static AiData? BuildAiData(string text)
    {
        var files = AiFiles(text);
        return AiData.TryBuild(new SyntheticFixture(TestRate.Default, files: files).Graph);
    }

    public static void OriginalAiDataIsReadStrictly()
    {
        Check.That(AiData.TryBuild(new SyntheticFixture(TestRate.Default, files: SkirmishFiles()).Graph) is null, "Content without an AIData block has no AI data (and says so by returning nothing).");
        var data = BuildAiData(AiDataText())!;
        Check.That(data.TeamSeconds == Fixed.FromInt(2) && data.Wealthy == 7000 && data.Poor == 100 && data.TeamsPoorRate == Fixed.FromDecimal(0.6m) && data.RebuildDelaySeconds == 2 && !data.RotateSkirmishBases
                   && data.SkirmishBaseDefenseExtraDistance == Fixed.FromInt(40), "The scalar keys the AI uses are read.");
        Check.That(data.UnreadKeys.SequenceEqual(["GuardInnerModifierAI"]), $"A valid key the AI does not use is listed, not dropped ({string.Join(", ", data.UnreadKeys)}).");
        var alpha = data.FindSide("Alpha")!;
        Check.That(alpha is { GatherersEasy: 1, GatherersNormal: 2, GatherersHard: 3, BaseDefenseStructure: "AlphaPlant" }, "Side info: gatherers by difficulty and the base defence.");
        Check.That(alpha.SkillSets.Length == 5 && alpha.SkillSets[0].SequenceEqual(["SCIENCE_One", "SCIENCE_Two"]) && alpha.SkillSets[1].SequenceEqual(["SCIENCE_Three"]) && alpha.SkillSets[2].IsEmpty,
            "Skill sets keep every science in order (a repeated key is a list here, not a last-wins scalar).");
        var list = data.BuildLists["Beta"];
        Check.That(list.Length == 3 && list[0] is { Template: "BetaCenter", AutomaticallyBuild: true, InitiallyBuilt: false } && list[0].AngleDegrees == Fixed.FromInt(-135)
                   && list[1].X == Fixed.FromInt(100) && list[1].Y == Fixed.FromInt(160) && !list[1].AutomaticallyBuild, "Build list entries with location, angle and flags.");
        Check.Throws<InvalidDataException>(() => BuildAiData(AiDataText(extraTopLevel: "NoSuchAiKey = 3")), "unknown key 'NoSuchAiKey'");
        Check.Throws<InvalidDataException>(() => BuildAiData(AiDataText(extraSide: "SideInfo Gamma\n    Gatherers = 2\n  End")), "unknown key 'Gatherers' in SideInfo Gamma");
        Check.Throws<InvalidDataException>(() => BuildAiData(AiDataText(extraSide: "SkirmishBuildList Gamma\n    Structure GammaCenter\n      Rebuilds = 0\n    End\n  End")), "has no Location");
        Check.Throws<InvalidDataException>(() => BuildAiData(AiDataText().Replace("TeamSeconds = 2", "TeamSeconds = soon", StringComparison.Ordinal)), "is not a number");
    }

    private static MapProperty IntProperty(string key, int value) => new(key, MapPropertyType.Int, value, 0m, string.Empty);

    /// <summary>The scripts and teams of one template player of the test library; every name starts with the side's letter so that the two lists can be told apart after qualification.</summary>
    private static (MapScriptList Scripts, MapProperties[] Teams) AiLibrarySide(string side)
    {
        var letter = side[..1];
        var owner = "Skirmish" + side;
        MapScript[] scripts =
        [
            Scr.Script(letter + " Base", [Scr.And(Scr.If("CONDITION_TRUE"))],
            [
                Scr.Do("SKIRMISH_BUILD_BUILDING", Scr.Type(side + "Plant")), Scr.Do("SKIRMISH_BUILD_BUILDING", Scr.Type(side + "Barracks")),
                Scr.Do("SET_FLAG", Scr.Flag(letter + " Started"), Scr.Bool(true)),
            ], oneShot: true),
            Scr.Script(letter + " Hard Only", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_FLAG", Scr.Flag(letter + " Hard"), Scr.Bool(true))], oneShot: true, easy: false, normal: false),
            Scr.Script(letter + " Mine", [Scr.And(Scr.If("SKIRMISH_NAMED_AREA_EXIST", Scr.Side(owner), Scr.Area("[Skirmish]MyInnerPerimeter")))],
                [Scr.Do("SET_FLAG", Scr.Flag(letter + " Area"), Scr.Bool(true))], oneShot: true),
            Scr.Script(letter + " Defense", [Scr.And(Scr.If("FLAG", Scr.Flag(letter + " Attacking"), Scr.Bool(true)))], [Scr.Do("SKIRMISH_BUILD_BASE_DEFENSE_FRONT")], oneShot: true),
            Scr.Script(letter + " Attack Condition", [Scr.And(Scr.If("FLAG", Scr.Flag(letter + " Started"), Scr.Bool(true)))], [], subroutine: true),
            Scr.Script(letter + " Attack Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_HUNT", Scr.Team("<This Team>")), Scr.Do("SET_FLAG", Scr.Flag(letter + " Attacking"), Scr.Bool(true))], subroutine: true),
            Scr.Script(letter + " Mark", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_FLAG", Scr.Flag(letter + " Marked"), Scr.Bool(true))], oneShot: true, subroutine: true),
            Scr.Script(letter + " Power", [Scr.And(Scr.If("SKIRMISH_SPECIAL_POWER_READY", Scr.Side(owner), Scr.Named(MapScriptParameterType.SpecialPower, side + "Shared")))],
                [Scr.Do("SET_FLAG", Scr.Flag(letter + " Power"), Scr.Bool(true))], oneShot: true),
        ];
        var team = new MapProperties(
        [
            Scr.Text("teamName", letter + " Attack"), Scr.Text("teamOwner", owner), Scr.Flag("teamIsSingleton", false), IntProperty("teamMaxInstances", 1),
            Scr.Text("teamUnitType1", side + "Soldier"), IntProperty("teamUnitMinCount1", 2), IntProperty("teamUnitMaxCount1", 2),
            Scr.Text("teamProductionCondition", letter + " Attack Condition"), IntProperty("teamProductionPriority", 5), Scr.Text("teamOnCreateScript", letter + " Attack Go"),
            Scr.Text("teamGenericScriptHook0", letter + " Mark"),
        ]);
        var defaultTeam = new MapProperties([Scr.Text("teamName", "team" + owner), Scr.Text("teamOwner", owner), Scr.Flag("teamIsSingleton", true)]);
        return (new MapScriptList([..scripts], []), [defaultTeam, team]);
    }

    private static MapScriptLibrary AiLibrary()
    {
        var (alpha, alphaTeams) = AiLibrarySide("Alpha");
        var (beta, betaTeams) = AiLibrarySide("Beta");
        var civilian = new MapScriptList([Scr.Script("Music", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_FLAG", Scr.Flag("Playing"), Scr.Bool(true))], oneShot: true)], []);
        return new MapScriptLibrary("CkMp",
            [new MapScriptLibraryPlayer(string.Empty, MapProperties.Empty), new MapScriptLibraryPlayer("SkirmishAlpha", MapProperties.Empty), new MapScriptLibraryPlayer("SkirmishBeta", MapProperties.Empty),
                new MapScriptLibraryPlayer("PlyrCivilian", MapProperties.Empty)],
            [..alphaTeams, ..betaTeams], [new MapScriptList([], []), alpha, beta, civilian], []);
    }

    /// <summary>A skirmish map as World Builder leaves it: the neutral side, a civilian side and one template side per playable side, each with its default team and no scripts.</summary>
    private static MapData AiSkirmishMap(MapScriptList? ownAlphaScripts = null)
    {
        MapPolygonTrigger Square(string name, int id, int x) => new(name, string.Empty, id, false, false, 0, [new MapPoint(x - 90, 110, 0), new MapPoint(x + 90, 110, 0), new MapPoint(x + 90, 290, 0), new MapPoint(x - 90, 290, 0)]);
        var map = SyntheticMaps.Build(60, 40, 1, triggers: [Square("InnerPerimeter1", 1, 100), Square("InnerPerimeter2", 2, 500)],
            waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        MapSide Side(string name, string faction) => new(new MapProperties([Scr.Text("playerName", name), Scr.Flag("playerIsHuman", false), Scr.Text("playerFaction", faction), Scr.Text("playerAllies", string.Empty), Scr.Text("playerEnemies", string.Empty)]), []);
        MapProperties Team(string owner) => new([Scr.Text("teamName", "team" + owner), Scr.Text("teamOwner", owner), Scr.Flag("teamIsSingleton", true)]);
        var empty = new MapScriptList([], []);
        return map with
        {
            Sides = [Side(string.Empty, string.Empty), Side("PlyrCivilian", "FactionCivilian"), Side("SkirmishAlpha", "FactionAlpha"), Side("SkirmishBeta", "FactionBeta")],
            Teams = [Team(string.Empty), Team("PlyrCivilian"), Team("SkirmishAlpha"), Team("SkirmishBeta")],
            PlayerScripts = [empty, empty, ownAlphaScripts ?? empty, empty],
        };
    }

    private static string? AiSideOfFaction(string faction) => faction switch { "FactionAlpha" => "Alpha", "FactionBeta" => "Beta", "FactionCivilian" => "Civilian", _ => null };

    public static void OriginalAiScriptLibraryIsAttachedLikeTheReference()
    {
        var map = AiSkirmishMap();
        var library = AiLibrary();
        ImmutableArray<SkirmishScriptSlot> slots = [new("FactionAlpha", "Alpha", 1, false, 0), new("FactionAlpha", "Alpha", 0, false, 0), new("FactionBeta", "Beta", 2, true, 0)];
        var attached = SkirmishScriptAttachment.Attach(map, library, slots, AiSideOfFaction, out var report);
        Check.That(report.LibraryAttached && report.Notes.IsEmpty, $"A map without skirmish scripts of its own gets the library ({string.Join("; ", report.Notes)}).");
        Check.That(attached.Sides.Select(side => side.Properties.GetText("playerName")).SequenceEqual([string.Empty, "player0", "player1", "player2", "PlyrCivilian"]),
            "Sides: neutral, one per slot in slot order, then the civilians; the template sides are gone.");
        Check.That(attached.Sides[1].Properties.GetText("playerEnemies") == "player1 player2" && attached.Sides[1].Properties.GetText("playerAllies") == string.Empty
                   && attached.Sides[1].Properties.TryGetInt("multiplayerStartIndex", out var start) && start == 1 && attached.Sides[3].Properties.GetBool("playerIsHuman"), "Without teams everybody is an enemy; start index and human flag are carried.");
        var first = attached.PlayerScripts[1];
        Check.That(first.Scripts.Select(script => script.Name).Take(2).SequenceEqual(["A Base1", "A Hard Only1"]) && attached.PlayerScripts[2].Scripts[0].Name == "A Base0",
            "Every script name gets the start position as qualifier, so two players of the same side do not share a script.");
        var mine = first.Scripts.Single(script => script.Name == "A Mine1").OrConditions[0].AndConditions[0];
        Check.That(mine.Parameters[0].Text == "player0" && mine.Parameters[1].Text == "[Skirmish]MyInnerPerimeter", "A side parameter naming the template player becomes the slot's player; an area name is left alone.");
        var go = first.Scripts.Single(script => script.Name == "A Attack Go1");
        Check.That(go.Actions[0].Parameters[0].Text == "<This Team>" && go.Actions[1].Parameters[0].Text == "A Attacking1", "<This Team> stays, flags are qualified.");
        var team = attached.Teams.Single(value => value.GetText("teamName") == "A Attack1");
        Check.That(team.GetText("teamOwner") == "player0" && team.GetText("teamProductionCondition") == "A Attack Condition1" && team.GetText("teamOnCreateScript") == "A Attack Go1"
                   && team.GetText("teamGenericScriptHook0") == "A Mark1", "Teams of the template player are copied to the slot with qualified name and script references.");
        Check.That(attached.Teams.Count(value => value.GetText("teamName") == "teamplayer0") == 1 && attached.Teams.Any(value => value.GetText("teamName") == "teamPlyrCivilian")
                   && !attached.Teams.Any(value => value.GetText("teamOwner") == "SkirmishAlpha"), "Each slot has its default team; civilian teams stay; template teams do not.");
        Check.That(attached.PlayerScripts[3].Scripts.Single().Name == "Music2" && !attached.Teams.Any(value => value.GetText("teamOwner") == "player2" && value.GetText("teamName") != "teamplayer2"),
            "A human slot gets the civilian template's scripts and no AI teams.");
        var program = ScriptProgram.Build(attached);
        Check.That(program.FindTeam("A Attack1") is { GenericScripts: ["A Mark1"], ProductionPriority: 5, MaxInstances: 1 } && program.FindPlayer("player1") is { StartIndex: 0 } second && second.Id == new PlayerId(2),
            "The script program reads the generic hooks; the player of slot i is player i + 1.");

        // Allies by team number.
        ImmutableArray<SkirmishScriptSlot> teams = [new("FactionAlpha", "Alpha", 0, false, 1), new("FactionBeta", "Beta", 1, false, 1), new("FactionBeta", "Beta", 2, false, 2)];
        var allied = SkirmishScriptAttachment.Attach(map, library, teams, AiSideOfFaction, out _);
        Check.That(allied.Sides[1].Properties.GetText("playerAllies") == "player1" && allied.Sides[1].Properties.GetText("playerEnemies") == "player2", "Slots with the same team number are allies.");

        // A map that brings scripts for a template side keeps them: the library is not attached at all.
        var own = AiSkirmishMap(new MapScriptList([Scr.Script("Own", [Scr.And(Scr.If("CONDITION_TRUE"))], [], oneShot: true)], []));
        var kept = SkirmishScriptAttachment.Attach(own, library, [new("FactionAlpha", "Alpha", 0, false, 0), new("FactionBeta", "Beta", 1, false, 0)], AiSideOfFaction, out var ownReport);
        Check.That(!ownReport.LibraryAttached && ownReport.Notes.Length == 1 && kept.PlayerScripts[1].Scripts.Single().Name == "Own0" && kept.PlayerScripts[2].Scripts.IsEmpty,
            "Map scripts win over the library, and the report says so.");
        var unscripted = SkirmishScriptAttachment.Attach(map, library, [new("FactionAlpha", "Alpha", 0, false, 0, Scripted: false), new("FactionBeta", "Beta", 1, false, 0)], AiSideOfFaction, out _);
        Check.That(unscripted.PlayerScripts[1].Scripts.IsEmpty && unscripted.PlayerScripts[2].Scripts.Length > 0, "A slot another AI plays gets no scripts.");
        var noSide = SkirmishScriptAttachment.Attach(map, library, [new("FactionGamma", "Gamma", 0, false, 0), new("FactionBeta", "Beta", 1, false, 0)], AiSideOfFaction, out var gammaReport);
        Check.That(noSide.PlayerScripts[1].Scripts.IsEmpty && gammaReport.Notes.Single().Contains("Gamma", StringComparison.Ordinal), "A side without a template player is reported, never silently scriptless.");
    }

    private static (ScriptedSkirmishSession Session, SyntheticFixture Fixture, TerrainMap Terrain, MapData Map, MapScriptLibrary Library, AiData Data) StartOriginalSkirmish(
        ScriptDifficulty alpha = ScriptDifficulty.Hard, ScriptDifficulty beta = ScriptDifficulty.Easy, uint seed = 11, SimulationRateChoice rate = default)
    {
        var files = AiFiles(AiDataText());
        var fixture = new SyntheticFixture(rate.Rate ?? TestRate.Default, files: files);
        var map = AiSkirmishMap();
        var terrain = TerrainMap.Build(map, "skirmish-original");
        var library = AiLibrary();
        var data = AiData.TryBuild(fixture.Graph)!;
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        var slots = ImmutableArray.Create(new ScriptedSkirmishSlot(participants.Single(value => value.Side == "Alpha"), alpha), new ScriptedSkirmishSlot(participants.Single(value => value.Side == "Beta"), beta));
        var options = new ScriptedSkirmishOptions(seed, fixture.Templates.Rate.Time.OriginalFrames(6_000), 60, 2_000, 0, AllMapObjects: true);
        return (ScriptedSkirmishSession.Start(options, fixture.Templates, fixture.Fingerprint, terrain, map, library, data, slots), fixture, terrain, map, library, data);
    }

    /// <summary>An optional rate for a test start (a struct so that it can be a default parameter).</summary>
    private readonly record struct SimulationRateChoice(CnCNew.Simulation.Time.SimulationRate? Rate);

    private static void StepFrames(ScriptedSkirmishSession session, long frames)
    {
        var until = session.World.Tick + session.World.Time.OriginalFrames(frames);
        while (session.World.Tick < until && !session.IsOver) session.Step();
    }

    private static int CountOwned(ScriptedSkirmishSession session, int player, string template, bool finishedOnly = true)
        => session.World.Entities.Count(entity => entity.Owner == new PlayerId(player) && !entity.EffectivelyDead && entity.Template.Name == template
                                                  && (!finishedOnly || !entity.HasStatus(ObjectStatusBits.UnderConstruction)));

    public static void OriginalAiPlaysASkirmishFromItsScripts()
    {
        var (session, fixture, terrain, map, library, data) = StartOriginalSkirmish();
        var engine = session.Engine;
        Check.That(engine.HasAiPlayers && CountOwned(session, 1, "AlphaCenter") == 1 && CountOwned(session, 2, "BetaCenter") == 1 && CountOwned(session, 1, "AlphaDozer") == 1,
            "At the start every AI has the command centre of its build list standing and the start units of its faction.");
        var centre = session.World.Entities.Single(entity => entity.Template.Name == "AlphaCenter");
        Check.That(centre.X == Fixed.FromInt(100) && centre.Y == Fixed.FromInt(200), $"The build list is moved so that its command centre is on the start position ({centre.X}, {centre.Y}).");
        var list = engine.AiBuildListOf(new PlayerId(1));
        // The plant is 60 north of the centre in the list; the base is turned by 135 degrees, so it ends up south-west.
        Check.That(list.Length == 3 && list[1].Template == "AlphaPlant" && (list[1].X - Fixed.FromDecimal(57.57m)).Abs() < Fixed.One && (list[1].Y - Fixed.FromDecimal(157.57m)).Abs() < Fixed.One,
            $"The rest of the list is turned by 135 degrees around the centre ({list[1].X}, {list[1].Y}).");

        StepFrames(session, 400);
        Check.That(engine.FlagValue("A Started0") && engine.FlagValue("B Started1"), "Each player runs its own qualified copy of the library's scripts.");
        Check.That(engine.FlagValue("A Hard0") && !engine.FlagValue("B Hard1"), "A script for the hard army only runs for the hard slot, not for the easy one: difficulty is per player.");
        Check.That(engine.FlagValue("A Area0") && engine.FlagValue("B Area1"), "[Skirmish]MyInnerPerimeter is the inner perimeter of the player's own start position.");
        Check.That(CountOwned(session, 1, "AlphaPlant") >= 1 && CountOwned(session, 1, "AlphaBarracks") == 1 && CountOwned(session, 2, "BetaBarracks") == 1,
            $"SKIRMISH_BUILD_BUILDING makes the dozer build the listed structures (pending: {string.Join("; ", engine.AiPendingBuilds(new PlayerId(1)))}).");
        Check.That(engine.AiEnemyOf(new PlayerId(1)) is null || engine.AiEnemyOf(new PlayerId(1)) == new PlayerId(2), "The only enemy is the enemy.");

        StepFrames(session, 900);
        Check.That(engine.FlagValue("A Attacking0"), "A team whose production condition holds is selected, trained and, once complete, runs its create script.");
        Check.That(engine.FlagValue("A Marked0"), "The generic script hook of the team ran for its instance.");
        var attackers = engine.TeamInstances.Single(team => team.Prototype == "A Attack0");
        Check.That(attackers.Members.Count == 2 || CountOwned(session, 1, "AlphaSoldier") < 2, $"The team has both its soldiers: the start soldier, recruited, and a trained one - a start unit is not taken for a trained one ({attackers.Members.Count}).");
        Check.That(System.Text.Encoding.UTF8.GetString(session.Save()).Contains("\"path\":\"hunt\"", StringComparison.Ordinal) || CountOwned(session, 1, "AlphaSoldier") == 0,
            "The create script of the team is run with the team as <This Team>: its TEAM_HUNT sends the members hunting.");
        Check.That(engine.AiEnemyOf(new PlayerId(1)) == new PlayerId(2), "A front defence without an approach path looks towards the acquired enemy.");
        Check.That(engine.MissingActionHits.Count == 0 && engine.MissingConditionHits.Count == 0, $"No script type without implementation was hit ({string.Join(", ", engine.MissingActionHits.Keys)}).");
        Check.That(!engine.Diagnostics.Keys.Any(key => key.Contains("not run by the skirmish AI", StringComparison.Ordinal)), "Skirmish actions found their AI player.");

        // Determinism: the same seed plays the same match; saved and restored it continues the same way.
        var saved = session.Save();
        var hashAtSave = session.StateHash();
        var restored = ScriptedSkirmishSession.Restore(saved, fixture.Templates, fixture.Fingerprint, terrain, map, library, data);
        Check.That(restored.StateHash() == hashAtSave, "A restored session has the state hash of the saved one, AI included.");
        StepFrames(session, 600);
        StepFrames(restored, 600);
        Check.That(restored.StateHash() == session.StateHash() && restored.World.StateHash == session.World.StateHash, "Saved, restored and continued it is tick for tick the same match.");
        var (again, _, _, _, _, _) = StartOriginalSkirmish();
        StepFrames(again, 1_900);
        Check.That(again.StateHash() == session.StateHash(), "A second fresh run with the same seed reaches the same state.");
        Check.Throws<CnCNew.Simulation.Snapshots.SnapshotFormatException>(() => ScriptedSkirmishSession.Restore(saved, fixture.Templates, fixture.Fingerprint, terrain, AiSkirmishMap(new MapScriptList([Scr.Script("Own", [], [])], [])), library, data),
            "different script program");
    }

    public static void OriginalAiRebuildsAndDefends()
    {
        var (session, _, _, _, _, _) = StartOriginalSkirmish();
        StepFrames(session, 400);
        var plants = CountOwned(session, 1, "AlphaPlant");
        Check.That(plants >= 1, "The plant stands.");
        // A structure a script asked for is rebuilt after RebuildDelayTimeSeconds when it is lost.
        foreach (var plant in session.World.Entities.Where(entity => entity.Owner == new PlayerId(1) && entity.Template.Name == "AlphaPlant").ToArray()) session.World.Kill(plant.Id);
        StepFrames(session, 30);
        Check.That(CountOwned(session, 1, "AlphaPlant", finishedOnly: false) == 0, "The plant is gone and the rebuild delay holds.");
        StepFrames(session, 400);
        Check.That(CountOwned(session, 1, "AlphaPlant") >= 1, $"The plant is rebuilt (pending: {string.Join("; ", session.Engine.AiPendingBuilds(new PlayerId(1)))}).");
        StepFrames(session, 900);
        // SKIRMISH_BUILD_BASE_DEFENSE_FRONT adds the side's base defence (a plant in the fixture) on the circle around the base, towards the enemy.
        var list = session.Engine.AiBuildListOf(new PlayerId(1));
        Check.That(list.Length == 4 && list[0] is { Template: "AlphaPlant", Priority: true } && list[0].X > Fixed.FromInt(100), $"The front defence is a new priority entry east of the base ({list.Length} entries, first at {list[0].X}).");
    }

    /// <summary>
    /// <c>evaluateSkirmishSpecialPowerIsReady</c> remembers in the condition when to look again: the frame the nearest module becomes ready, at most ten seconds ahead. A power that
    /// becomes ready earlier by other means is not noticed before that frame.
    /// </summary>
    public static void OriginalAiRemembersWhenToLookAtAPowerAgain()
    {
        foreach (var rate in new[] { CnCNew.Simulation.Time.SimulationRate.Hz30, CnCNew.Simulation.Time.SimulationRate.Hz60 })
        {
            var (session, fixture, terrain, map, library, data) = StartOriginalSkirmish(rate: new SimulationRateChoice(rate));
            var world = session.World;
            var alpha = new PlayerId(1);
            Check.That(world.SpecialPowers.TryGet("AlphaShared", out var power), "The fixture has the shared power of the command centre.");
            StepFrames(session, 20);
            Check.That(!session.Engine.FlagValue("A Power0"), "The power recharges for ten seconds: the condition is false at the start.");
            var state = System.Text.Encoding.UTF8.GetString(session.Save());
            Check.That(state.Contains("\"aiConditionMemory\"", StringComparison.Ordinal), "The frame of the next look is part of the saved state.");
            // Ready now, by a route the condition does not see (the reference: any change of the module's ready frame after the look).
            world.SetSharedReadyFrame(alpha, power.Index, world.Tick);
            var restored = ScriptedSkirmishSession.Restore(session.Save(), fixture.Templates, fixture.Fingerprint, terrain, map, library, data);
            StepFrames(session, 200);
            Check.That(!session.Engine.FlagValue("A Power0"), $"Before the remembered frame the condition is not evaluated again, although the power is ready ({rate}).");
            StepFrames(session, 90);
            Check.That(session.Engine.FlagValue("A Power0"), $"From the remembered frame on it is looked at again and is true ({rate}).");
            StepFrames(restored, 290);
            Check.That(restored.StateHash() == session.StateHash(), $"A session restored between the two looks continues identically: the memory is state ({rate}).");
        }
    }

    public static void OriginalAiSeedsAndRates()
    {
        // The seed is the one source of the AI's random decisions: the skill set a player chooses is drawn from it.
        var skillSets = new SortedSet<string>(StringComparer.Ordinal);
        for (uint number = 1; number <= 6; number++)
        {
            var seed = unchecked(number * 2654435761u);
            var (session, _, _, _, _, _) = StartOriginalSkirmish(seed: seed);
            session.World.AddSciencePoints(new PlayerId(1), 1);
            StepFrames(session, 5);
            var first = session.StateHash();
            var (twin, _, _, _, _, _) = StartOriginalSkirmish(seed: seed);
            twin.World.AddSciencePoints(new PlayerId(1), 1);
            StepFrames(twin, 5);
            Check.That(twin.StateHash() == first, $"Seed {seed} twice gives the same state.");
            skillSets.Add(System.Text.Encoding.UTF8.GetString(session.Save()).Split("\"skillSet\":")[1][..1]);
        }
        Check.That(skillSets.SetEquals(["0", "1"]), $"Over six seeds both skill sets of the side are chosen ({string.Join(", ", skillSets)}): different seeds, different courses.");

        // The AI thinks on the frame grid of the original: at 60 Hz its timers run in the same game time.
        var (at30, _, _, _, _, _) = StartOriginalSkirmish();
        var (at60, _, _, _, _, _) = StartOriginalSkirmish(rate: new SimulationRateChoice(CnCNew.Simulation.Time.SimulationRate.Hz60));
        long FirstBarracks(ScriptedSkirmishSession session)
        {
            while (!session.IsOver && CountOwned(session, 1, "AlphaBarracks", finishedOnly: false) == 0) session.Step();
            return session.World.Time.ToOriginalFrames(session.World.Tick);
        }
        var (frames30, frames60) = (FirstBarracks(at30), FirstBarracks(at60));
        // The AI looks at its build list every two seconds; what the world does in between (a dozer arriving, a site finishing) may differ by a few ticks between rates, so the same
        // decision can fall on the next look: one such interval is the tolerance.
        Check.That(Math.Abs(frames30 - frames60) <= 62, $"The barracks is started at the same game time at 30 and 60 Hz, within one look of the AI (frame {frames30} and {frames60}).");
    }

    /// <summary>The shipped library on a shipped map: decoding, attachment, a short match of two original AIs, determinism and save/restore.</summary>
    public static void RealOriginalSkirmishAi(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var data = AiData.TryBuild(graph);
        Check.That(data is { Sides.Length: 12, BuildLists.Count: 12 } && data.BuildLists.Values.Sum(list => list.Length) == 214 && data.BuildLists.Values.All(list => list.Count(entry => entry.AutomaticallyBuild) == 1),
            "AIData.ini: twelve sides, twelve build lists with 214 structures, and only the command centre of each is built without a script.");
        MapScriptLibrary library;
        using (var stream = content.OpenRead("Data/Scripts/SkirmishScripts.scb")) library = SageMapDecoder.DecodeScriptLibrary(stream);
        Check.That(library.Players.Length == 14 && library.ScriptLists.Length == 14 && library.Teams.Length == 1340, $"SkirmishScripts.scb: 14 players, 14 lists, 1,340 teams ({library.Players.Length}, {library.ScriptLists.Length}, {library.Teams.Length}).");
        var used = library.ScriptLists.SelectMany(list => list.Scripts.Concat(list.Groups.SelectMany(group => group.Scripts))).SelectMany(script => script.Actions.Concat(script.FalseActions)).Select(action => action.TypeName).Distinct().ToArray();
        var notExecuted = used.Where(name => ScriptCoverage.ActionSupportWithSkirmishAi(name) is not ScriptSupport.Executed).Order(StringComparer.Ordinal).ToArray();
        Check.That(notExecuted.SequenceEqual(["MUSIC_SET_TRACK", "TEAM_SET_ATTITUDE"]), $"With the skirmish AI every action type of the library is executed but the forwarded music and the recorded attitude ({string.Join(", ", notExecuted)}).");

        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, MapPath);
        var everyone = ReferenceMatchScenario.Participants(catalog, out _);
        var slots = ImmutableArray.Create(new ScriptedSkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"), ScriptDifficulty.Hard), new ScriptedSkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA"), ScriptDifficulty.Normal));
        // 150 seconds of game time; the budget of the options is in ticks of the session's rate.
        var matchOptions = ScriptedSkirmishOptions.Standard with { Seed = 5, MaxTicks = TestRate.Frames(4_500) };
        ScriptedSkirmishSession Start() => ScriptedSkirmishSession.Start(matchOptions, catalog, fingerprint, terrain, map, library, data!, slots);
        var session = Start();
        Check.That(session.Mission.Program.Players.Select(player => player.Name).Take(3).SequenceEqual([string.Empty, "player0", "player1"]) && session.Mission.Program.Teams.Length > 200,
            $"The match program has the two slots and their qualified teams ({session.Mission.Program.Teams.Length} teams).");
        byte[]? saved = null;
        while (!session.IsOver)
        {
            if (session.World.Tick == TestRate.Frames(3_000)) saved = session.Save();
            session.Step();
        }
        var engine = session.Engine;
        Check.That(engine.MissingActionHits.Count == 0 && engine.MissingConditionHits.Count == 0, $"No script type without implementation is hit ({string.Join(", ", engine.MissingActionHits.Keys.Concat(engine.MissingConditionHits.Keys))}).");
        for (var player = 1; player <= 2; player++)
        {
            var structures = session.World.Entities.Count(entity => entity.Owner == new PlayerId(player) && !entity.EffectivelyDead && entity.Template.IsKindOf("STRUCTURE"));
            Check.That(structures >= 5, $"Player {player} has built a base from its scripts ({structures} structures after 150 seconds; pending {string.Join("; ", engine.AiPendingBuilds(new PlayerId(player)))}).");
        }
        Check.That(engine.AiEnemyOf(new PlayerId(1)) == new PlayerId(2) && engine.AiEnemyOf(new PlayerId(2)) == new PlayerId(1), "Each AI has acquired the other as its enemy.");
        var again = Start();
        while (!again.IsOver) again.Step();
        Check.That(again.StateHash() == session.StateHash(), "The same seed plays the same match.");
        var restored = ScriptedSkirmishSession.Restore(saved!, catalog, fingerprint, terrain, map, library, data!);
        while (!restored.IsOver) restored.Step();
        Check.That(restored.StateHash() == session.StateHash(), "Saved after 100 seconds, restored and continued, it ends in the same state.");
        var other = ScriptedSkirmishSession.Start(matchOptions with { Seed = 6 }, catalog, fingerprint, terrain, map, library, data!, slots);
        while (!other.IsOver) other.Step();
        Check.That(other.StateHash() != session.StateHash(), "Another seed plays another match.");
    }

    /// <summary>
    /// The shipped library on a shipped map at 30, 60 and 120 Hz: the same match. After the logic frames of the original every object is where the 30 Hz match has it - the AI
    /// thinks on the frame grid of the original and what it orders is carried out on the frames of the objects.
    /// </summary>
    public static void RealOriginalSkirmishAiAcrossRates(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var data = AiData.TryBuild(graph)!;
        MapScriptLibrary library;
        using (var stream = content.OpenRead("Data/Scripts/SkirmishScripts.scb")) library = SageMapDecoder.DecodeScriptLibrary(stream);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, MapPath);
        const int Frames = 7_200; // four minutes: the bases stand, the first teams have marched and fought

        ScriptedSkirmishSession Start(CnCNew.Simulation.Time.SimulationRate rate)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var slots = ImmutableArray.Create(new ScriptedSkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"), ScriptDifficulty.Hard), new ScriptedSkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA"), ScriptDifficulty.Normal));
            return ScriptedSkirmishSession.Start(ScriptedSkirmishOptions.Standard with { Seed = 5, MaxTicks = rate.Time.OriginalFrames(Frames) }, catalog, rate.Combine(fingerprint), terrain, map, library, data, slots);
        }

        // What a match is made of, in units that do not name a tick: who exists, whose it is, where it stands and how it is. The health of a construction site is left out: above
        // 30 Hz its first work step counts a whole frame, so between two frames the site is a share of a frame ahead and complete in the tick of the 30 Hz step.
        static List<string> Picture(ScriptedSkirmishSession session)
        {
            var lines = new List<string>();
            foreach (var entity in session.World.Entities)
            {
                // Projectiles in flight are left out: the stream of a toxin truck stands two to five raw steps (3e-7 units) beside the 30 Hz one for the three frames it lives
                // (frame 4030, catalogs without model geometry as here; not traced). What they hit and how hard is in the health of everything else.
                if (entity.Template.IsKindOf("PROJECTILE")) continue;
                var building = entity.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction);
                lines.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{entity.Id.Value} {entity.Template.Name} owner {entity.Owner.Value} at {entity.X.Raw} {entity.Y.Raw} {entity.Z.Raw} angle {entity.Angle.Raw} moving {entity.HasMoveTarget} in {entity.ContainedBy.Value} dead {entity.EffectivelyDead} building {building} health {(building ? 0 : entity.Body?.Health.Raw ?? 0)}"));
            }
            for (var player = 1; player <= 2; player++)
                lines.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"player {player} money {session.World.MoneyOf(new PlayerId(player)).Raw} pending {string.Join(";", session.Engine.AiPendingBuilds(new PlayerId(player)))}"));
            return lines;
        }

        var sessions = new[] { CnCNew.Simulation.Time.SimulationRate.Hz30, CnCNew.Simulation.Time.SimulationRate.Hz60, CnCNew.Simulation.Time.SimulationRate.Hz120 }.Select(rate => (Rate: rate, Session: Start(rate))).ToArray();
        // A difference counts once it has lasted three frames, the rule of "SimTool rate-compare --fields": a state one rate reaches a share of a frame before the other and the
        // other catches up with is no other match.
        const int Lasting = 3;
        var streak = new int[sessions.Length];
        var transient = 0;
        for (var frame = 0; frame < Frames; frame++)
        {
            foreach (var (rate, session) in sessions)
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) session.Step();
            var reference = Picture(sessions[0].Session);
            for (var other = 1; other < sessions.Length; other++)
            {
                var (rate, session) = sessions[other];
                var picture = Picture(session);
                if (picture.SequenceEqual(reference))
                {
                    streak[other] = 0;
                    continue;
                }
                transient++;
                if (++streak[other] < Lasting) continue;
                var index = Enumerable.Range(0, Math.Min(picture.Count, reference.Count)).FirstOrDefault(at => picture[at] != reference[at], Math.Min(picture.Count, reference.Count));
                Check.That(false, $"From frame {frame - Lasting + 1} on the match at {rate.TicksPerSecond} Hz is the 30 Hz match (after frame {frame}: 30 Hz: {(index < reference.Count ? reference[index] : "-")}; {rate.TicksPerSecond} Hz: {(index < picture.Count ? picture[index] : "-")}).");
            }
        }
        var structures = sessions[0].Session.World.Entities.Count(entity => entity.Owner.Value is 1 or 2 && !entity.EffectivelyDead && entity.Template.IsKindOf("STRUCTURE"));
        Console.WriteLine($"       original AI across rates: {Frames} frames, {sessions[0].Session.World.Entities.Count()} objects and {structures} structures of the two players at the end, equal at 30, 60 and 120 Hz ({transient} frame ends with a difference that did not last {Lasting} frames)");
        Check.That(structures >= 10, $"The compared match is one in which both players have built ({structures} structures).");
    }
}

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
using CnCNew.Simulation.World;

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
            Scr.Text("teamGenericScriptHook0", letter + " Mark"), Scr.Flag("teamAttackCommonTarget", true), Scr.Flag("teamAutoReinforce", side == "Alpha"),
        ]);
        var defaultTeam = new MapProperties([Scr.Text("teamName", "team" + owner), Scr.Text("teamOwner", owner), Scr.Flag("teamIsSingleton", true)]);
        return (new MapScriptList([..scripts], []), [defaultTeam, team]);
    }

    private static MapScriptLibrary AiLibrary(MapScript[]? alphaExtra = null)
    {
        var (alpha, alphaTeams) = AiLibrarySide("Alpha");
        if (alphaExtra is not null) alpha = alpha with { Scripts = [..alpha.Scripts, ..alphaExtra] };
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

        // The named option that is not the reference: a map without the template side of a player's side (two shipped maps for the generals) takes the library's template player.
        var bare = map with { Sides = [..map.Sides.Where(side => side.Properties.GetText("playerName") != "SkirmishBeta")], Teams = [..map.Teams.Where(team => team.GetText("teamOwner") != "SkirmishBeta")], PlayerScripts = [..map.PlayerScripts.Take(3)] };
        ImmutableArray<SkirmishScriptSlot> two = [new("FactionAlpha", "Alpha", 0, false, 0), new("FactionBeta", "Beta", 1, false, 0)];
        var asReference = SkirmishScriptAttachment.Attach(bare, library, two, AiSideOfFaction, out var bareReport);
        Check.That(asReference.PlayerScripts[2].Scripts.IsEmpty && bareReport.Notes.Single().Contains("runs without scripts", StringComparison.Ordinal), "By default such a player has no scripts, as in the reference.");
        var byOption = SkirmishScriptAttachment.Attach(bare, library, two, AiSideOfFaction, out var optionReport, libraryForMissingSides: true);
        Check.That(byOption.PlayerScripts[2].Scripts.Any(script => script.Name == "B Base1") && byOption.Teams.Any(team => team.GetText("teamName") == "B Attack1" && team.GetText("teamOwner") == "player1")
                   && optionReport.Notes.Single().Contains("not what the reference does", StringComparison.Ordinal), "With the option it gets the library's scripts and teams for its side, and the report says that this is not the reference.");
    }

    private static (ScriptedSkirmishSession Session, SyntheticFixture Fixture, TerrainMap Terrain, MapData Map, MapScriptLibrary Library, AiData Data) StartOriginalSkirmish(
        ScriptDifficulty alpha = ScriptDifficulty.Hard, ScriptDifficulty beta = ScriptDifficulty.Easy, uint seed = 11, SimulationRateChoice rate = default, MapScript[]? alphaLibraryScripts = null)
    {
        var files = AiFiles(AiDataText());
        var fixture = new SyntheticFixture(rate.Rate ?? TestRate.Default, files: files);
        var map = AiSkirmishMap();
        var terrain = TerrainMap.Build(map, "skirmish-original");
        var library = AiLibrary(alphaLibraryScripts);
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

        // The hunt is looked for once a second while the 30 seconds run: who hunts may have fallen by their end.
        var sawHunt = false;
        for (var second = 0; second < 30; second++)
        {
            StepFrames(session, 30);
            sawHunt |= System.Text.Encoding.UTF8.GetString(session.Save()).Contains("\"path\":\"hunt\"", StringComparison.Ordinal);
        }
        Check.That(engine.FlagValue("A Attacking0"), "A team whose production condition holds is selected, trained and, once complete, runs its create script.");
        Check.That(engine.FlagValue("A Marked0"), "The generic script hook of the team ran for its instance.");
        var attackers = engine.TeamInstances.Single(team => team.Prototype == "A Attack0");
        Check.That(attackers.Members.Count == 2 || CountOwned(session, 1, "AlphaSoldier") < 2, $"The team has both its soldiers: the start soldier, recruited, and a trained one - a start unit is not taken for a trained one ({attackers.Members.Count}).");
        Check.That(sawHunt || LiveTeamMembers(session, "A Attack0").Length == 0,
            $"The create script of the team is run with the team as <This Team>: its TEAM_HUNT sends the members hunting (or all of them have fallen: {LiveTeamMembers(session, "A Attack0").Length} alive, {CountOwned(session, 1, "AlphaSoldier")} soldiers).");
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

    private static Entity[] LiveTeamMembers(ScriptedSkirmishSession session, string prototype)
        => [..session.Engine.TeamInstances.Where(team => team.Prototype == prototype).SelectMany(team => team.Members)
            .Select(id => session.World.FindEntity(new EntityId(id))).Where(entity => entity is { EffectivelyDead: false, DestroyRequested: false }).Select(entity => entity!)];

    /// <summary>
    /// <c>Team::setTeamTargetObject</c>: the members of a team with <c>teamAttackCommonTarget</c> attack one victim together - on the normal and the hard level, never on the easy one.
    /// </summary>
    public static void OriginalAiTeamsAttackOneVictimTogetherExceptOnEasy()
    {
        var (session, fixture, terrain, map, library, data) = StartOriginalSkirmish(alpha: ScriptDifficulty.Hard, beta: ScriptDifficulty.Easy);
        var engine = session.Engine;
        var world = session.World;
        var hardSet = false;
        var easySet = false;
        var together = false;
        byte[]? saved = null;
        ulong hashAtSave = 0;
        for (var frame = 0; frame < 2_400 && !session.IsOver && !(hardSet && together && saved is not null); frame++)
        {
            StepFrames(session, 1);
            foreach (var team in engine.TeamInstances)
            {
                if (!team.CommonAttackTarget.IsValid) continue;
                if (team.Prototype == "B Attack1") easySet = true;
                if (team.Prototype != "A Attack0") continue;
                var victim = world.FindEntity(team.CommonAttackTarget);
                Check.That(victim is null || victim.Owner == new PlayerId(2), "The common victim of the team of the hard army is an object of its enemy.");
                hardSet = true;
                if (saved is null) { saved = session.Save(); hashAtSave = session.StateHash(); }
                var attacking = LiveTeamMembers(session, "A Attack0").Where(member => world.AcquiredVictimOf(member).IsValid).ToArray();
                if (attacking.Length >= 2 && attacking.All(member => world.AcquiredVictimOf(member) == team.CommonAttackTarget)) together = true;
            }
        }
        Check.That(hardSet, "A member of the hard team that fires or hunts names the victim of the team.");
        Check.That(together, "Two members of the team attack the victim of the team at the same time.");
        Check.That(!easySet, "The easy army never has a common victim (Team::setTeamTargetObject returns for DIFFICULTY_EASY).");
        Check.That(System.Text.Encoding.UTF8.GetString(saved!).Contains("\"commonTarget\"", StringComparison.Ordinal), "The victim of the team is part of the saved state.");
        var restored = ScriptedSkirmishSession.Restore(saved, fixture.Templates, fixture.Fingerprint, terrain, map, library, data);
        Check.That(restored.StateHash() == hashAtSave, "Restored, the session hashes as it did when it was saved.");
        var (twin, _, _, _, _, _) = StartOriginalSkirmish(alpha: ScriptDifficulty.Hard, beta: ScriptDifficulty.Easy);
        while (twin.World.Tick < session.World.Tick) twin.Step();
        while (restored.World.Tick < session.World.Tick) restored.Step();
        Check.That(twin.StateHash() == session.StateHash() && restored.StateHash() == session.StateHash(), "A second run and the restored run reach the same state.");
    }

    /// <summary><c>AIPlayer::selectTeamToReinforce</c>: a team in the field that asks for it (<c>teamAutoReinforce</c>) gets a lost unit back, one at a time; another team does not.</summary>
    public static void OriginalAiReinforcesATeamInTheField()
    {
        var (session, _, _, _, _, _) = StartOriginalSkirmish(alpha: ScriptDifficulty.Normal, beta: ScriptDifficulty.Normal);
        var world = session.World;
        while (!session.IsOver && !(session.Engine.FlagValue("A Attacking0") && session.Engine.FlagValue("B Attacking1"))) session.Step();
        Check.That(session.Engine.FlagValue("A Attacking0") && session.Engine.FlagValue("B Attacking1"), "Both attack teams are in the field.");
        var alpha = LiveTeamMembers(session, "A Attack0");
        var beta = LiveTeamMembers(session, "B Attack1");
        Check.That(alpha.Length == 2 && beta.Length == 2, $"Each team has its two soldiers: the start soldier, recruited, and a trained one - the start units are not taken for trained ones ({alpha.Length}, {beta.Length}).");
        var newest = world.Entities.Max(entity => entity.Id.Value);
        world.Kill(alpha[0].Id);
        world.Kill(beta[0].Id);
        var reinforced = false;
        for (var frame = 0; frame < 600 && !session.IsOver && !reinforced; frame++)
        {
            StepFrames(session, 1);
            reinforced = LiveTeamMembers(session, "A Attack0").Any(member => member.Id.Value > newest);
        }
        Check.That(reinforced, "The team that asks for reinforcements got a new soldier.");
        Check.That(!LiveTeamMembers(session, "B Attack1").Any(member => member.Id.Value > newest), "The team that does not ask stays as it is.");
        Check.That(session.Engine.MissingActionHits.Count == 0 && !session.Engine.Diagnostics.Keys.Any(key => key.Contains("not modelled", StringComparison.Ordinal)), "Nothing about the reinforcement is reported as missing.");
    }

    /// <summary>
    /// What phase 24 relies on: a session of the original AI saved at any tick - also between two logic frames of the original at 60 and 120 Hz, where the AI and the scripts do not
    /// run - and restored continues tick for tick with the same session hash, and its saved document is the same document again.
    /// </summary>
    public static void OriginalAiSessionContinuesFromAnyTick()
    {
        foreach (var rate in new[] { CnCNew.Simulation.Time.SimulationRate.Hz30, CnCNew.Simulation.Time.SimulationRate.Hz60, CnCNew.Simulation.Time.SimulationRate.Hz120 })
        {
            var (session, fixture, terrain, map, library, data) = StartOriginalSkirmish(alpha: ScriptDifficulty.Hard, beta: ScriptDifficulty.Normal, rate: new SimulationRateChoice(rate));
            var perFrame = session.World.Time.TicksPerOriginalFrame;
            // Save points in frames of the original plus an offset inside the frame: base building, team building, the first fight, late.
            long[] frames = [1, 37, 151, 420, 800, 1_333];
            var copies = new List<(ScriptedSkirmishSession Restored, long Tick)>();
            var soldiers = 0;
            for (var index = 0; index < frames.Length; index++)
            {
                var tick = session.World.Time.OriginalFrames(frames[index]) + index % perFrame;
                while (session.World.Tick < tick && !session.IsOver) session.Step();
                soldiers = Math.Max(soldiers, session.World.Entities.Count(entity => entity.Template.Name.EndsWith("Soldier", StringComparison.Ordinal)));
                var saved = session.Save();
                var restored = ScriptedSkirmishSession.Restore(saved, fixture.Templates, fixture.Fingerprint, terrain, map, library, data);
                Check.That(restored.StateHash() == session.StateHash(), $"{rate}: restored at tick {tick} (offset {index % perFrame} inside its frame) the session hashes the same.");
                Check.That(restored.Save().AsSpan().SequenceEqual(saved), $"{rate}: the restored session writes the document it was read from (tick {tick}).");
                copies.Add((restored, tick));
            }
            // Every copy is stepped to where the original is now, comparing on the way with a second original.
            var (twin, _, _, _, _, _) = StartOriginalSkirmish(alpha: ScriptDifficulty.Hard, beta: ScriptDifficulty.Normal, rate: new SimulationRateChoice(rate));
            var end = session.World.Tick + session.World.Time.OriginalFrames(240);
            while (session.World.Tick < end && !session.IsOver) session.Step();
            foreach (var (restored, from) in copies)
            {
                while (twin.World.Tick < from) twin.Step();
                var compared = 0;
                while (restored.World.Tick < session.World.Tick && !restored.IsOver)
                {
                    restored.Step();
                    twin.Step();
                    compared++;
                    if (restored.StateHash() != twin.StateHash() || restored.World.StateHash != twin.World.StateHash)
                        throw new InvalidOperationException($"{rate}: restored at tick {from}, the session differs from an uninterrupted one at tick {restored.World.Tick}.");
                }
                // Since a computer player's units walk at what they see the match may be over before the last save: a copy saved at its end has nothing to step.
                Check.That((compared > 0 || from >= session.World.Tick) && restored.StateHash() == session.StateHash(), $"{rate}: restored at tick {from} and continued for {compared} ticks it is the uninterrupted match.");
                (twin, _, _, _, _, _) = StartOriginalSkirmish(alpha: ScriptDifficulty.Hard, beta: ScriptDifficulty.Normal, rate: new SimulationRateChoice(rate));
            }
            // The session hash is the world hash and the engine hash (which contains the AI) and the decision of the session: a change in any of them shows.
            var before = session.StateHash();
            session.World.AddSciencePoints(new PlayerId(1), 1);
            Check.That(session.StateHash() != before, $"{rate}: a change of the world changes the session hash.");
            Check.That(soldiers >= 2 && session.Engine.HasAiPlayers, $"The match had teams to save ({soldiers} soldiers).");
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
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var data = AiData.TryBuild(graph);
        Check.That(data is { Sides.Length: 12, BuildLists.Count: 12 } && data.BuildLists.Values.Sum(list => list.Length) == 214 && data.BuildLists.Values.All(list => list.Count(entry => entry.AutomaticallyBuild) == 1),
            "AIData.ini: twelve sides, twelve build lists with 214 structures, and only the command centre of each is built without a script.");
        MapScriptLibrary library;
        using (var stream = content.OpenRead("Data/Scripts/SkirmishScripts.scb")) library = SageMapDecoder.DecodeScriptLibrary(stream);
        Check.That(library.Players.Length == 14 && library.ScriptLists.Length == 14 && library.Teams.Length == 1340, $"SkirmishScripts.scb: 14 players, 14 lists, 1,340 teams ({library.Players.Length}, {library.ScriptLists.Length}, {library.Teams.Length}).");
        var used = library.ScriptLists.SelectMany(list => list.Scripts.Concat(list.Groups.SelectMany(group => group.Scripts))).SelectMany(script => script.Actions.Concat(script.FalseActions)).Select(action => action.TypeName).Distinct().ToArray();
        var notExecuted = used.Where(name => ScriptCoverage.ActionSupport(name) is not ScriptSupport.Executed).Order(StringComparer.Ordinal).ToArray();
        Check.That(notExecuted.SequenceEqual(["MUSIC_SET_TRACK"]), $"With the skirmish AI every action type of the library is executed but the forwarded music ({string.Join(", ", notExecuted)}).");

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
    /// The shipped library in a fight at 60 and 120 Hz: saved between two logic frames of the original at several points of the match, restored and continued, the session has the
    /// hash of the uninterrupted one at every compared tick (every seventh, so that every offset inside a frame is met) and at the end.
    /// </summary>
    public static void RealOriginalSkirmishAiContinuesFromAnyTick(string directory)
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
        foreach (var rate in new[] { CnCNew.Simulation.Time.SimulationRate.Hz60, CnCNew.Simulation.Time.SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var print = rate.Combine(fingerprint);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var slots = ImmutableArray.Create(new ScriptedSkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica"), ScriptDifficulty.Hard),
                new ScriptedSkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA"), ScriptDifficulty.Normal));
            const int Frames = 9_000; // five minutes
            ScriptedSkirmishSession Start() => ScriptedSkirmishSession.Start(ScriptedSkirmishOptions.Standard with { Seed = 9, MaxTicks = rate.Time.OriginalFrames(Frames) }, catalog, print, terrain, map, library, data, slots);
            var session = Start();
            var perFrame = session.World.Time.TicksPerOriginalFrame;
            long[] frames = [900, 3_601, 6_002, 8_003];
            var hashes = new Dictionary<long, ulong>();
            var saves = new List<(byte[] Document, long Tick)>();
            var next = 0;
            while (!session.IsOver)
            {
                if (next < frames.Length && session.World.Tick == rate.Time.OriginalFrames(frames[next]) + (next + 1) % perFrame)
                {
                    saves.Add((session.Save(), session.World.Tick));
                    next++;
                }
                session.Step();
                if (session.World.Tick % 7 == 0 || session.IsOver) hashes[session.World.Tick] = session.StateHash();
            }
            Check.That(saves.Count == frames.Length, $"{rate}: all {frames.Length} save points were reached.");
            Check.That(session.Engine.MissingActionHits.Count == 0 && session.Engine.MissingConditionHits.Count == 0, $"{rate}: no script type without implementation was hit.");
            foreach (var (document, tick) in saves)
            {
                var restored = ScriptedSkirmishSession.Restore(document, catalog, print, terrain, map, library, data);
                var compared = 0;
                while (!restored.IsOver)
                {
                    restored.Step();
                    if (!hashes.TryGetValue(restored.World.Tick, out var expected)) continue;
                    compared++;
                    if (restored.StateHash() == expected) continue;
                    // Where and when: an uninterrupted session is run to the save point again, a second copy is restored, and the two are stepped side by side until
                    // their saved documents differ for the first time.
                    var differsAt = restored.World.Tick;
                    var again = Start();
                    while (again.World.Tick < tick) again.Step();
                    var copy = ScriptedSkirmishSession.Restore(document, catalog, print, terrain, map, library, data);
                    static string Document(ScriptedSkirmishSession session) => System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(session.Save()), "\"stateHash\":\"[0-9A-F]+\"", "\"stateHash\":\"\"");
                    // Derived state the document does not carry: the footprints on the navigation grid, compared cell by cell at the save point.
                    var gridNote = "the navigation grids agree at the save point";
                    if (again.World.Navigation is { } liveGrid && copy.World.Navigation is { } copyGrid)
                    {
                        var (cells, firstCell) = (0, "");
                        for (var cy = 0; cy < liveGrid.Terrain.CellsY; cy++)
                            for (var cx = 0; cx < liveGrid.Terrain.CellsX; cx++)
                                if (liveGrid.IsBlockedByObstacle(cx, cy) != copyGrid.IsBlockedByObstacle(cx, cy))
                                {
                                    if (cells++ == 0) firstCell = $"({cx}, {cy}) blocked in the {(liveGrid.IsBlockedByObstacle(cx, cy) ? "uninterrupted" : "restored")} session only";
                                }
                        if (cells > 0) gridNote = $"the navigation grids differ at the save point in {cells} cells, first {firstCell}";
                    }
                    var (live, other) = (Document(again), Document(copy));
                    while (live == other && again.World.Tick < differsAt)
                    {
                        again.Step();
                        copy.Step();
                        (live, other) = (Document(again), Document(copy));
                    }
                    var at = 0;
                    while (at < live.Length && at < other.Length && live[at] == other[at]) at++;
                    var from = Math.Max(0, at - 400);
                    throw new InvalidOperationException($"{rate}: saved at tick {tick} (offset {tick % perFrame} inside its frame), restored and continued, the session differs at tick {differsAt}. "
                        + $"{gridNote}. The saved documents differ first at tick {again.World.Tick}, character {at}: uninterrupted '...{live[from..Math.Min(live.Length, at + 2600)]}' against restored '...{other[from..Math.Min(other.Length, at + 2600)]}'.");
                }
                Check.That(compared > 100, $"{rate}: saved at tick {tick}, restored and continued, the session is the uninterrupted one at {compared} compared ticks and at the end.");
            }
        }
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
        // Three minutes and twenty seconds: the bases stand, the first teams have marched and fought. Not longer with these catalogs: since the teams' own scripts send them out
        // (<This Team>), an air fight begins - at frame 6,779 until Phase 39, at frame 6,102 since its fidelity package B -, and above 30 Hz - with catalogs without model
        // geometry, as here - a MiG is hit otherwise and falls elsewhere than at 30 Hz (not traced; the projectile note below is the same family). With the catalogs of the
        // product the same match is equal in every field for all 7,200 frames at 30 against 60 and 120 Hz (measured at 26bd7b5):
        // `SimTool rate-compare <content> --original-diff FactionChina,FactionGLA --rates 30,120 --difficulty hard --frames 7200 --fields`.
        const int Frames = 6_000;

        ScriptedSkirmishSession Start(CnCNew.Simulation.Time.SimulationRate rate)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
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
                // A train covers its way in shares of a frame: where the frames end it stands the rounding of those shares apart at the rates (what it hits is in the health of the rest).
                if (entity.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.RailroadBehavior>().Any()) continue;
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
    // ------------------------------------------------------------------------------------------------ the C++ half, part by part (docs/SKIRMISH-AI-ORIGINAL.md §6.2)

    /// <summary>A map of the fixture with more cliffs, by world cell (the height cells are one further in, the border).</summary>
    private static MapData AiSkirmishMapWithCliffs(IEnumerable<(int X, int Y)> cliffs)
    {
        var plain = AiSkirmishMap();
        var built = SyntheticMaps.Build(60, 40, 1, cliffs: cliffs.Select(cell => (cell.X + 1, cell.Y + 1)), triggers: plain.Triggers, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        return built with { Sides = plain.Sides, Teams = plain.Teams, PlayerScripts = plain.PlayerScripts };
    }

    /// <summary>A match of two AIs on a variant of the fixture: more objects and another AI data text, a library of its own, a map of its own.</summary>
    private static (ScriptedSkirmishSession Session, SyntheticFixture Fixture, TerrainMap Terrain, MapData Map, MapScriptLibrary Library, AiData Data) StartAiVariant(string aiData, string extraObjects, MapScriptLibrary? library = null,
        MapData? map = null, CnCNew.Simulation.Time.SimulationRate? rate = null, int startMoney = 2_000, uint seed = 11, bool humanFirst = false, bool humanSecond = false)
    {
        var files = AiFiles(aiData);
        if (extraObjects.Length > 0) files["Data/INI/Object/AiVariant.ini"] = extraObjects;
        var fixture = new SyntheticFixture(rate ?? TestRate.Default, files: files);
        map ??= AiSkirmishMap();
        var terrain = TerrainMap.Build(map, "skirmish-original");
        library ??= AiLibrary();
        var data = AiData.TryBuild(fixture.Graph)!;
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        var slots = ImmutableArray.Create(new ScriptedSkirmishSlot(participants.Single(value => value.Side == "Alpha"), ScriptDifficulty.Hard, IsHuman: humanFirst),
            new ScriptedSkirmishSlot(participants.Single(value => value.Side == "Beta"), ScriptDifficulty.Easy, IsHuman: humanSecond));
        var options = new ScriptedSkirmishOptions(seed, fixture.Templates.Rate.Time.OriginalFrames(60_000), 60, startMoney, 0, AllMapObjects: true);
        return (ScriptedSkirmishSession.Start(options, fixture.Templates, fixture.Fingerprint, terrain, map, library, data, slots), fixture, terrain, map, library, data);
    }

    /// <summary>The test library with other scripts for Alpha and none for Beta, which stands still: both have their default teams only.</summary>
    private static MapScriptLibrary AiLibraryWithAlphaScripts(params MapScript[] scripts) => AiLibraryWithAlpha(scripts, []);

    private static MapScriptLibrary AiLibraryWithAlpha(MapScript[] scripts, MapProperties[] teams)
    {
        var (beta, betaTeams) = (new MapScriptList([], []), new[] { new MapProperties([Scr.Text("teamName", "teamSkirmishBeta"), Scr.Text("teamOwner", "SkirmishBeta"), Scr.Flag("teamIsSingleton", true)]) });
        var defaultTeam = new MapProperties([Scr.Text("teamName", "teamSkirmishAlpha"), Scr.Text("teamOwner", "SkirmishAlpha"), Scr.Flag("teamIsSingleton", true)]);
        var civilian = new MapScriptList([], []);
        return new MapScriptLibrary("CkMp",
            [new MapScriptLibraryPlayer(string.Empty, MapProperties.Empty), new MapScriptLibraryPlayer("SkirmishAlpha", MapProperties.Empty), new MapScriptLibraryPlayer("SkirmishBeta", MapProperties.Empty),
                new MapScriptLibraryPlayer("PlyrCivilian", MapProperties.Empty)],
            [defaultTeam, ..teams, ..betaTeams], [new MapScriptList([], []), new MapScriptList([..scripts], []), beta, civilian], []);
    }

    private const string AiTechObject = """
        Object AlphaTech
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 60
          BuildTime = 1
          Prerequisites
            Object = AlphaBarracks
          End
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
        End
        """;

    private static string AiDataWithAlphaStructure(string template, decimal x = 160m, decimal y = 40m)
    {
        var structure = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Structure {template}\n      Location = X:{x:F2} Y:{y:F2}\n      Rebuilds = 0\n      Angle = 0.00\n      InitiallyBuilt = No\n      AutomaticallyBuild = No\n    END\n    ");
        return AiDataText().Replace("Structure AlphaPlant", structure + "Structure AlphaPlant", StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>processBaseBuilding</c> takes the first open priority entry whatever the rules say, and <c>buildObjectNow</c> tests its prerequisites (<c>isPossibleToMakeUnit</c>, <c>Player::canBuild</c>):
    /// nothing is built, and the entries behind it wait. Before, the first entry the rules allowed was taken, and the one behind the blocked entry stood.
    /// </summary>
    public static void OriginalAiWaitsForTheFirstOpenPriorityEntry()
    {
        var library = AiLibraryWithAlphaScripts(Scr.Script("A Tech", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SKIRMISH_BUILD_BUILDING", Scr.Type("AlphaTech")), Scr.Do("SKIRMISH_BUILD_BUILDING", Scr.Type("AlphaPlant"))], oneShot: true));
        var (session, fixture, _, _, _, _) = StartAiVariant(AiDataWithAlphaStructure("AlphaTech"), AiTechObject, library);
        StepFrames(session, 300);
        Check.That(CountOwned(session, 1, "AlphaTech", finishedOnly: false) == 0, "The first entry lacks its prerequisite and is not built: buildObjectNow refuses it.");
        Check.That(CountOwned(session, 1, "AlphaPlant", finishedOnly: false) == 0, $"The entry behind it waits (pending: {string.Join("; ", session.Engine.AiPendingBuilds(new PlayerId(1)))}).");
        Check.That(session.Engine.AiPendingBuilds(new PlayerId(1)).Any(line => line.StartsWith("AlphaTech: MissingPrerequisite", StringComparison.Ordinal)), $"It waits for the prerequisite ({string.Join("; ", session.Engine.AiPendingBuilds(new PlayerId(1)))}).");

        // The prerequisite appears: first the one entry, then the one behind it.
        Check.That(fixture.Templates.TryGet("AlphaBarracks", out var barracks), "The fixture has the barracks.");
        session.World.RequestSpawn(barracks.Index, new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(300), Fixed.Zero, Fixed.Zero);
        StepFrames(session, 300);
        Check.That(CountOwned(session, 1, "AlphaTech") == 1 && CountOwned(session, 1, "AlphaPlant") >= 1, $"With its prerequisite the entry is built and the one behind it follows (pending: {string.Join("; ", session.Engine.AiPendingBuilds(new PlayerId(1)))}).");
    }

    /// <summary>
    /// The end of <c>buildStructureWithDozer</c>: a dozer that has no way to any place around the entry stands on the entry's place at once ("Dozer unable to reach building. Teleporting.") and
    /// builds there. A dozer with a way somewhere near gets that place instead. Before, the order went out and the dozer never arrived.
    /// </summary>
    public static void OriginalAiMovesADozerThatCannotReachTheSite()
    {
        foreach (var rate in new[] { CnCNew.Simulation.Time.SimulationRate.Hz30, CnCNew.Simulation.Time.SimulationRate.Hz60, CnCNew.Simulation.Time.SimulationRate.Hz120 })
        {
            // The dozer starts at (151, 200): cell (15, 20) is left free inside a ring of cliffs, too small for any structure.
            var ring = new List<(int X, int Y)>();
            for (var x = 14; x <= 16; x++)
                for (var y = 19; y <= 21; y++)
                    if (x != 15 || y != 20) ring.Add((x, y));
            var (session, _, _, _, _, _) = StartAiVariant(AiDataText(), string.Empty, AiLibrary(), AiSkirmishMapWithCliffs(ring), rate);
            var dozer = session.World.Entities.Single(entity => entity.Owner == new PlayerId(1) && entity.Template.Name == "AlphaDozer");
            Check.That(TerrainMap.CellOf(dozer.X) == 15 && TerrainMap.CellOf(dozer.Y) == 20, $"{rate}: the dozer starts inside the ring ({dozer.X}, {dozer.Y}).");
            var plant = session.Engine.AiBuildListOf(new PlayerId(1)).First(entry => entry.Template == "AlphaPlant");
            StepFrames(session, 300);
            Check.That(CountOwned(session, 1, "AlphaPlant") >= 1, $"{rate}: the plant stands (pending: {string.Join("; ", session.Engine.AiPendingBuilds(new PlayerId(1)))}).");
            Check.That(session.World.Entities.Any(entity => entity.Template.Name == "AlphaPlant" && entity.Owner == new PlayerId(1) && entity.X == plant.X && entity.Y == plant.Y), $"{rate}: on the place of its entry.");
            Check.That(session.World.Entities.Single(entity => entity.Id == dozer.Id).X < Fixed.FromInt(140), $"{rate}: the dozer is out of its ring.");
        }

        // A way somewhere near: the place of the entry lies behind a wall of cliffs, the structure goes to the first place on the dozer's side.
        var wall = Enumerable.Range(0, 40).Select(y => (8, y));
        var (walled, _, _, _, _, _) = StartAiVariant(AiDataText(), string.Empty, AiLibrary(), AiSkirmishMapWithCliffs(wall));
        var wanted = walled.Engine.AiBuildListOf(new PlayerId(1)).First(entry => entry.Template == "AlphaPlant");
        Check.That(wanted.X < Fixed.FromInt(80), $"The place of the entry lies west of the wall ({wanted.X}).");
        StepFrames(walled, 300);
        var built = walled.World.Entities.First(entity => entity.Template.Name == "AlphaPlant" && entity.Owner == new PlayerId(1));
        Check.That(built.X >= Fixed.FromInt(90) && CountOwned(walled, 1, "AlphaPlant") >= 1, $"It stands on the dozer's side of the wall ({built.X}, {built.Y}).");
        Check.That(walled.Engine.AiBuildListOf(new PlayerId(1)).Any(entry => entry.Template == "AlphaPlant" && entry.X == built.X && entry.ObjectId == built.Id.Value), "The entry has moved with it.");
    }

    /// <summary>
    /// <c>buildStructureWithDozer</c> does not ask the shroud (<c>SHROUD_REVEALED</c> is the human's placement), and nothing else of the AI looks at what is explored: its map starts shrouded as a
    /// human's does, and it builds on ground it has never seen. Before, the session explored the map for it.
    /// </summary>
    public static void OriginalAiBuildsOnGroundItHasNotSeen()
    {
        var library = AiLibraryWithAlphaScripts(Scr.Script("A Far", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SKIRMISH_BUILD_BUILDING", Scr.Type("AlphaBarracks"))], oneShot: true));
        // 200 units to one side of the centre in the list: turned by 135 degrees it stands at (241, 59), beyond what the centre and the start units see.
        var (session, _, _, _, _, _) = StartAiVariant(AiDataText().Replace("Location = X:160.00 Y:100.00", "Location = X:-100.00 Y:100.00", StringComparison.Ordinal), string.Empty, library);
        var far = session.Engine.AiBuildListOf(new PlayerId(1)).Single(entry => entry.Template == "AlphaBarracks");
        Check.That(!session.World.IsPositionExploredBy(new PlayerId(1), far.X, far.Y), $"The place ({far.X}, {far.Y}) is shrouded for the computer player at the start.");
        StepFrames(session, 400);
        Check.That(CountOwned(session, 1, "AlphaBarracks") == 1, $"It builds there all the same (pending: {string.Join("; ", session.Engine.AiPendingBuilds(new PlayerId(1)))}).");
    }

    private const string AiSupplyObjects = """
        Object AlphaSupplyCenter
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE FS_SUPPLY_CENTER
          BuildCost = 100
          BuildTime = 1
          CommandSet = AlphaSupplySet
          Geometry = BOX
          GeometryMajorRadius = 12.0
          GeometryMinorRadius = 12.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
          Behavior = SupplyCenterProductionExitUpdate Exit01
            UnitCreatePoint = X: 0.0 Y: 0.0 Z: 0.0
            NaturalRallyPoint = X: 20.0 Y: 0.0 Z: 0.0
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 1
            SpawnReplaceDelay = 9999
            SpawnTemplateName = AlphaTruck
            OneShot = Yes
            CanReclaimOrphans = No
            SlavesHaveFreeWill = Yes
          End
          Behavior = SupplyCenterDockUpdate Dock01
            NumberApproachPositions = 4
          End
          Behavior = SupplyCenterCreate Create01
          End
        End
        Object AlphaTruck
          Side = Alpha
          KindOf = SELECTABLE VEHICLE HARVESTER
          BuildCost = 10
          BuildTime = 1
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SupplyTruckAIUpdate AI01
            MaxBoxes = 2
            SupplyCenterActionDelay = 100
            SupplyWarehouseActionDelay = 100
            SupplyWarehouseScanDistance = 400.0
          End
        End
        Object NeutralDepot
          KindOf = STRUCTURE SUPPLY_SOURCE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SupplyWarehouseDockUpdate Dock01
            NumberApproachPositions = 4
            StartingBoxes = 500
          End
          Behavior = SupplyWarehouseCreate Create01
          End
        End
        CommandButton Cmd_AlphaTruck
          Command = UNIT_BUILD
          Object = AlphaTruck
        End
        CommandSet AlphaSupplySet
          1 = Cmd_AlphaTruck
        End
        """;

    /// <summary>
    /// <c>checkForSupplyCenter</c>, <c>queueSupplyTruck</c>, <c>onUnitProduced</c>: a centre wants the gatherers of the side and level plus the one it comes with, each is sent to it and remembers
    /// it as its dock, and when the centre is gone and another stands the loose gatherers are taken back one by one instead of new ones trained. Before, the gatherers had no dock of their own.
    /// </summary>
    public static void OriginalAiKeepsItsGatherersPerCentre()
    {
        var library = AiLibraryWithAlphaScripts(Scr.Script("A Supply", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SKIRMISH_BUILD_BUILDING", Scr.Type("AlphaSupplyCenter"))], oneShot: true));
        var (session, fixture, terrain, map, _, data) = StartAiVariant(AiDataWithAlphaStructure("AlphaSupplyCenter"), AiSupplyObjects, library);
        Check.That(fixture.Templates.TryGet("NeutralDepot", out var depot), "The fixture has the depot.");
        session.World.RequestSpawn(depot.Index, new PlayerId(0), Fixed.FromInt(100), Fixed.FromInt(345), Fixed.Zero, Fixed.Zero);
        var player = new PlayerId(1);
        ImmutableArray<Entity> Trucks() => [..session.World.Entities.Where(entity => entity.Owner == player && entity.Template.Name == "AlphaTruck" && !entity.EffectivelyDead)];
        StepFrames(session, 900);
        var centres = session.Engine.AiSupplyCentersOf(player);
        Check.That(centres.Length == 1 && centres[0] is { Desired: 4, Current: 4 }, $"The hard army's centre wants three gatherers and the one it comes with ({string.Join(", ", centres)}).");
        var centre = centres[0].ObjectId;
        Check.That(Trucks().Length == 4, $"It has them ({Trucks().Length}).");
        Check.That(Trucks().All(truck => ScriptEngine.PreferredDockOf(truck)?.Value == centre), $"Each remembers the centre as its dock ({string.Join(", ", Trucks().Select(truck => ScriptEngine.PreferredDockOf(truck)?.Value))}).");
        Check.That(session.World.MoneyOf(player) > Fixed.FromInt(1_800), $"They bring money ({session.World.MoneyOf(player)}).");

        // Saved here, the rest of the test runs on the restored session.
        var restored = ScriptedSkirmishSession.Restore(session.Save(), fixture.Templates, fixture.Fingerprint, terrain, map, library, data);
        Check.That(restored.StateHash() == session.StateHash(), "Orders and counts are part of the state.");
        session = restored;

        var trucks = Trucks().Length;
        session.World.Kill(new CnCNew.Simulation.Ids.EntityId(centre));
        StepFrames(session, 1_200);
        var rebuilt = session.Engine.AiSupplyCentersOf(player).Single();
        Check.That(rebuilt.ObjectId != 0 && rebuilt.ObjectId != centre && CountOwned(session, 1, "AlphaSupplyCenter") == 1, $"The centre is rebuilt ({rebuilt}).");
        Check.That(Trucks().Count(truck => ScriptEngine.PreferredDockOf(truck)?.Value == rebuilt.ObjectId) >= trucks && rebuilt.Current >= trucks,
            $"The loose gatherers are back at the new centre ({string.Join(", ", Trucks().Select(truck => ScriptEngine.PreferredDockOf(truck)?.Value))}, {rebuilt}).");
    }

    private const string AiYardObjects = """
        Upgrade Upgrade_AlphaDrill
          Type = PLAYER
          BuildTime = 1.0
          BuildCost = 50
        End
        Object AlphaYard
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 60
          BuildTime = 1
          CommandSet = AlphaYardSet
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
        End
        Object AlphaGuard
          Side = Alpha
          KindOf = SELECTABLE INFANTRY
          BuildCost = 25
          BuildTime = 1
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 80.0
            InitialHealth = 80.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        CommandButton Cmd_AlphaGuard
          Command = UNIT_BUILD
          Object = AlphaGuard
        End
        CommandButton Cmd_AlphaDrill
          Command = PLAYER_UPGRADE
          Upgrade = Upgrade_AlphaDrill
        End
        CommandSet AlphaYardSet
          1 = Cmd_AlphaGuard
          2 = Cmd_AlphaDrill
        End
        """;

    /// <summary>
    /// <c>findFactory</c> and <c>buildUpgrade</c> walk the build list: a production building of the player that the list does not hold is no factory of the AI. A team waits for it, and so does an
    /// upgrade, until a building of the list can do the work. Before, every production building of the player counted.
    /// </summary>
    public static void OriginalAiLooksForFactoriesInItsBuildList()
    {
        var team = new MapProperties(
        [
            Scr.Text("teamName", "A Guards"), Scr.Text("teamOwner", "SkirmishAlpha"), Scr.Flag("teamIsSingleton", false), IntProperty("teamMaxInstances", 1),
            Scr.Text("teamUnitType1", "AlphaGuard"), IntProperty("teamUnitMinCount1", 1), IntProperty("teamUnitMaxCount1", 1),
            Scr.Text("teamProductionCondition", "A Guards Condition"), IntProperty("teamProductionPriority", 5),
        ]);
        MapScript[] Scripts(params MapScriptAction[] actions) =>
        [
            Scr.Script("A Guards Condition", [Scr.And(Scr.If("CONDITION_TRUE"))], [], subroutine: true),
            Scr.Script("A Drill", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("AI_PLAYER_BUILD_UPGRADE", Scr.Side("SkirmishAlpha"), Scr.Named(MapScriptParameterType.Upgrade, "Upgrade_AlphaDrill"))]),
            Scr.Script("A Start", [Scr.And(Scr.If("CONDITION_TRUE"))], actions, oneShot: true),
        ];
        var data = AiDataWithAlphaStructure("AlphaYard");

        var (outside, fixture, _, _, _, _) = StartAiVariant(data, AiYardObjects, AiLibraryWithAlpha(Scripts(), [team]));
        Check.That(fixture.Templates.TryGet("AlphaYard", out var yard), "The fixture has the yard.");
        Check.That(fixture.Templates.Upgrades.TryGet("Upgrade_AlphaDrill", out var drill), "The fixture has the upgrade.");
        outside.World.RequestSpawn(yard.Index, new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(330), Fixed.Zero, Fixed.Zero);
        StepFrames(outside, 900);
        Check.That(CountOwned(outside, 1, "AlphaYard") == 1 && CountOwned(outside, 1, "AlphaGuard") == 0, $"A yard the build list does not hold trains nothing for the AI ({CountOwned(outside, 1, "AlphaGuard")}).");
        Check.That(!outside.World.PlayerUpgrades(new PlayerId(1)).Has(drill.Index), "Nor does it research.");

        var (listed, _, _, _, _, _) = StartAiVariant(data, AiYardObjects, AiLibraryWithAlpha(Scripts(Scr.Do("SKIRMISH_BUILD_BUILDING", Scr.Type("AlphaYard"))), [team]));
        StepFrames(listed, 900);
        Check.That(CountOwned(listed, 1, "AlphaGuard") == 1, $"The yard of the build list trains the team ({CountOwned(listed, 1, "AlphaGuard")}).");
        Check.That(listed.World.PlayerUpgrades(new PlayerId(1)).Has(drill.Index), "And researches the upgrade.");
    }

    private const string AiHoleObjects = """
        Object AlphaWorker
          Side = Alpha
          KindOf = SELECTABLE INFANTRY DOZER
          BuildCost = 10
          BuildTime = 1
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DozerAIUpdate AI01
            RepairHealthPercentPerSecond = 10%
          End
        End
        Object AlphaHole
          Side = Alpha
          KindOf = IMMOBILE STRUCTURE REBUILD_HOLE
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 2.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = RebuildHoleBehavior Hole01
            WorkerObjectName = AlphaWorker
            WorkerRespawnDelay = 6000
            HoleHealthRegen%PerSecond = 0.5%
          End
        End
        Object AlphaShack
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 60
          BuildTime = 1
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = RebuildHoleExposeDie Die01
            HoleName = AlphaHole
            HoleMaxHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """;

    private static uint AiEntryObject(ScriptedSkirmishSession session, string template) => session.Engine.AiBuildListOf(new PlayerId(1)).First(entry => entry.Template == template).ObjectId;

    /// <summary>
    /// <c>processBaseBuilding</c> finds the hole of a destroyed structure by <c>getSpawnerID</c> and the entry waits for it; <c>RebuildHoleBehavior</c> sends its worker and raises the structure free
    /// of charge; <c>onStructureProduced</c> gives the entry the new building. In the round that finds the hole a priority entry still counts as missing, as in the reference: with a
    /// dozer and the money it gets a second site. Before, the hole stayed a hole for ever and the AI rebuilt beside it after the delay.
    /// </summary>
    public static void OriginalAiWaitsForTheHole()
    {
        var library = AiLibraryWithAlphaScripts(Scr.Script("A Shack", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SKIRMISH_BUILD_BUILDING", Scr.Type("AlphaShack"))], oneShot: true));
        foreach (var rate in new[] { CnCNew.Simulation.Time.SimulationRate.Hz30, CnCNew.Simulation.Time.SimulationRate.Hz60, CnCNew.Simulation.Time.SimulationRate.Hz120 })
        {
            var (session, fixture, terrain, map, _, data) = StartAiVariant(AiDataWithAlphaStructure("AlphaShack"), AiHoleObjects, library, rate: rate);
            var player = new PlayerId(1);
            StepFrames(session, 300);
            var first = AiEntryObject(session, "AlphaShack");
            Check.That(first != 0 && CountOwned(session, 1, "AlphaShack") == 1, $"{rate}: the structure stands.");

            // No money, so that the round that finds the hole cannot start a second site.
            session.World.SetMoney(player, Fixed.Zero);
            session.World.Kill(new CnCNew.Simulation.Ids.EntityId(first));
            StepFrames(session, 100);
            var waiting = AiEntryObject(session, "AlphaShack");
            var hole = session.World.Entities.SingleOrDefault(entity => entity.Template.Name == "AlphaHole");
            Check.That(hole is not null && waiting == hole.Id.Value, $"{rate}: the entry holds the hole ({waiting}).");

            // Saved while the worker builds: the hole's state travels.
            session.World.SetMoney(player, Fixed.FromInt(1_000));
            StepFrames(session, 90);
            Check.That(session.World.Entities.Any(entity => entity.Template.Name == "AlphaWorker"), $"{rate}: the worker is at work.");
            var restored = ScriptedSkirmishSession.Restore(session.Save(), fixture.Templates, fixture.Fingerprint, terrain, map, library, data);
            Check.That(restored.StateHash() == session.StateHash(), $"{rate}: the state of the hole is saved.");
            session = restored;

            StepFrames(session, 400);
            var rebuilt = AiEntryObject(session, "AlphaShack");
            Check.That(CountOwned(session, 1, "AlphaShack") == 1, $"{rate}: one structure stands again, no second site ({CountOwned(session, 1, "AlphaShack", finishedOnly: false)}).");
            Check.That(session.World.MoneyOf(player) >= Fixed.FromInt(1_000), $"{rate}: a hole rebuilds free of charge ({session.World.MoneyOf(player)}).");
            Check.That(session.World.Entities.Any(entity => entity.Id.Value == rebuilt && entity.Template.Name == "AlphaShack"), $"{rate}: the entry holds the rebuilt structure ({rebuilt}).");
            Check.That(!session.World.Entities.Any(entity => entity.Template.Name is "AlphaHole" or "AlphaWorker"), $"{rate}: hole and worker are gone.");
        }

        // A worker that falls: the wait starts over, the next one takes up the site that stands.
        var (lost, _, _, _, _, _) = StartAiVariant(AiDataWithAlphaStructure("AlphaShack"), AiHoleObjects, library);
        StepFrames(lost, 300);
        lost.World.SetMoney(new PlayerId(1), Fixed.Zero);
        lost.World.Kill(new CnCNew.Simulation.Ids.EntityId(AiEntryObject(lost, "AlphaShack")));
        StepFrames(lost, 190);
        var site = lost.World.Entities.Single(entity => entity.Template.Name == "AlphaShack");
        Check.That(site.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction), "The worker is at work on the site.");
        lost.World.Kill(lost.World.Entities.Single(entity => entity.Template.Name == "AlphaWorker").Id);
        StepFrames(lost, 300);
        Check.That(lost.World.Entities.Single(entity => entity.Template.Name == "AlphaShack").Id == site.Id && CountOwned(lost, 1, "AlphaShack") == 1, "The next worker finishes the same site.");
        Check.That(!lost.World.Entities.Any(entity => entity.Template.Name is "AlphaHole" or "AlphaWorker"), "Hole and worker are gone.");

        // With the money, the round that finds the hole builds beside it.
        var (rich, _, _, _, _, _) = StartAiVariant(AiDataWithAlphaStructure("AlphaShack"), AiHoleObjects, library);
        StepFrames(rich, 300);
        rich.World.Kill(new CnCNew.Simulation.Ids.EntityId(AiEntryObject(rich, "AlphaShack")));
        StepFrames(rich, 600);
        Check.That(CountOwned(rich, 1, "AlphaShack") == 2, $"The priority entry of the round that finds the hole gets a second site, and the hole rebuilds too ({CountOwned(rich, 1, "AlphaShack", finishedOnly: false)}).");
    }

    private const string AiBridgeObject = """
        Object TestBridge
          IsBridge = Yes
          KindOf = STRUCTURE IMMOBILE BRIDGE LANDMARK_BRIDGE
          Geometry = BOX
          GeometryMajorRadius = 40.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 5.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = BridgeBehavior Bridge01
            LateralScaffoldSpeed = 100.0
            VerticalScaffoldSpeed = 70.0
          End
        End
        """;

    /// <summary>A timer that runs out at <paramref name="frames"/>, and the one action after it.</summary>
    private static MapScript[] AiAfter(int frames, params MapScriptAction[] actions) =>
    [
        Scr.Script("A Clock Start", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_TIMER", Scr.Counter("A Clock"), Scr.Int(frames))], oneShot: true),
        Scr.Script("A After", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("A Clock")))], actions, oneShot: true),
    ];

    /// <summary>
    /// <c>doTeamFollowSkirmishApproachPath</c> asks <c>checkBridges</c>: a broken bridge that would join the team and the path goes into the repair queue and a dozer is kept for it. The order then
    /// comes to nothing: <c>canRepairObject</c> refuses bridges ("repairing bridges cut"), in the original as here. Before, no bridge was looked at.
    /// </summary>
    public static void OriginalAiKeepsADozerForABrokenBridge()
    {
        MapData Map()
        {
            var map = AiSkirmishMapWithCliffs(Enumerable.Range(0, 40).Select(y => (30, y)));
            var path = new MapObject("*Waypoints/Waypoint", 400m, 200m, 0m, 0m, 0, new MapProperties([new MapProperty("waypointID", MapPropertyType.Int, 100, 0m, string.Empty), Scr.Text("waypointName", "Center2.Far"), Scr.Text("waypointPathLabel1", "Center2")]));
            return map with { Objects = [..map.Objects, path, new MapObject("TestBridge", 305m, 200m, 0m, 0m, 0, MapProperties.Empty)] };
        }
        var follow = Scr.Do("SKIRMISH_FOLLOW_APPROACH_PATH", Scr.Team("teamplayer"), Scr.SkirmishPath("Center"), Scr.Bool(false));
        var (session, fixture, terrain, map, library, data) = StartAiVariant(AiDataText(), AiBridgeObject, AiLibraryWithAlphaScripts(AiAfter(60, follow)), Map());
        var bridgeId = session.World.Bridges.Single().ObjectId;
        StepFrames(session, 30);
        session.World.Kill(bridgeId);
        StepFrames(session, 90);
        var player = new PlayerId(1);
        var (structures, dozer) = session.Engine.AiRepairOf(player);
        Check.That(structures.SequenceEqual([bridgeId.Value]), $"The broken bridge is in the repair queue ({string.Join(", ", structures)}).");
        Check.That(session.World.Entities.Any(entity => entity.Id.Value == dozer && entity.Template.Name == "AlphaDozer"), $"A dozer is kept for it ({dozer}).");
        var restored = ScriptedSkirmishSession.Restore(session.Save(), fixture.Templates, fixture.Fingerprint, terrain, map, library, data);
        Check.That(restored.StateHash() == session.StateHash(), "The repair queue is state.");
        StepFrames(session, 300);
        // (A kept dozer that falls is replaced: the two soldiers may meet across the cliffs.)
        var (still, keptNow) = session.Engine.AiRepairOf(player);
        Check.That(still.SequenceEqual([bridgeId.Value]) && session.World.Entities.Any(entity => entity.Id.Value == keptNow && entity.Template.Name == "AlphaDozer"),
            $"It stays that way: nobody can repair a bridge ({string.Join(", ", still)}; {keptNow}, was {dozer}).");

        // Nothing is queued while the bridge stands: a way that is simply not there is nobody's business.
        var (whole, _, _, _, _, _) = StartAiVariant(AiDataText(), AiBridgeObject, AiLibraryWithAlphaScripts(AiAfter(60, follow)), Map());
        StepFrames(whole, 120);
        Check.That(whole.Engine.AiRepairOf(player).Structures.IsEmpty, "A bridge that stands is not repaired.");
    }

    /// <summary>
    /// <c>doPlayerRepairStructure</c> for an AI player goes through the same queue (<c>repairStructure</c>, <c>updateBridgeRepair</c>): a dozer is kept, repairs until the structure is pristine, and
    /// goes home to the base centre, and it stays kept. Before, the nearest dozer got the order and was free again.
    /// </summary>
    public static void OriginalAiRepairsThroughItsQueue()
    {
        var library = AiLibraryWithAlphaScripts(AiAfter(400, Scr.Do("PLAYER_REPAIR_NAMED_STRUCTURE", Scr.Side("SkirmishAlpha"), Scr.Unit("Hurt"))));
        var (session, _, _, _, _, _) = StartAiVariant(AiDataText(), string.Empty, library);
        var player = new PlayerId(1);
        var centre = session.World.Entities.Single(entity => entity.Owner == player && entity.Template.Name == "AlphaCenter");
        session.Engine.RegisterNamed("Hurt", centre.Id);
        session.World.Damage(centre.Id, new DamageInfo(CnCNew.Simulation.Ids.EntityId.Invalid, Fixed.FromInt(450), DamageTypes.Explosion, DeathTypes.Normal, false));
        StepFrames(session, 500);
        Check.That(session.Engine.AiRepairOf(player).Structures.SequenceEqual([centre.Id.Value]) || centre.Body!.Health == centre.Body.MaxHealth, $"The structure is in the queue ({centre.Body!.Health}).");
        var dozer = session.Engine.AiRepairOf(player).Dozer;
        Check.That(dozer != 0, "A dozer is kept for it.");
        StepFrames(session, 1_500);
        Check.That(centre.Body!.Health == centre.Body.MaxHealth, $"It is repaired ({centre.Body.Health}).");
        var (structures, kept) = session.Engine.AiRepairOf(player);
        Check.That(structures.IsEmpty && kept == dozer, $"The queue is empty and the dozer stays kept ({string.Join(", ", structures)}; {kept}).");
    }

    private const string AiPowerObjects = """
        SpecialPower TestStrike
          Enum = SPECIAL_DAISY_CUTTER
          ReloadTime = 1000
        End
        SpecialPower TestMines
          Enum = SPECIAL_CLUSTER_MINES
          ReloadTime = 1000
        End
        Object AlphaSilo
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 10
          BuildTime = 1
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
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = TestStrike
          End
          Behavior = SpecialAbility Power02
            SpecialPowerTemplate = TestMines
          End
        End
        Object BetaKeep
          Side = Beta
          KindOf = SELECTABLE STRUCTURE COMMANDCENTER
          BuildCost = 1000
          BuildTime = 1
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
        End
        Object BetaHut
          Side = Beta
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 200
          BuildTime = 1
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
        End
        """;

    /// <summary>The place the script's power goes to, from a match of a silo on the fixture.</summary>
    private static CnCNew.Simulation.Commands.UseSpecialPowerCommand AiPowerShot(string power, uint seed, MapData map, Action<ScriptedSkirmishSession, SyntheticFixture>? prepare = null)
    {
        var fire = Scr.Do("SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST", Scr.Side("SkirmishAlpha"), Scr.Named(MapScriptParameterType.SpecialPower, power));
        var (session, fixture, _, _, _, _) = StartAiVariant(AiDataText(), AiPowerObjects, AiLibraryWithAlphaScripts(AiAfter(120, fire)), map, seed: seed);
        fixture.Templates.TryGet("AlphaSilo", out var silo);
        session.World.RequestSpawn(silo.Index, new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(300), Fixed.Zero, Fixed.Zero);
        prepare?.Invoke(session, fixture);
        session.RecordCommands();
        StepFrames(session, 200);
        var shots = session.RecordedCommands.OfType<CnCNew.Simulation.Commands.UseSpecialPowerCommand>().Where(command => command.SpecialPower == power).ToArray();
        Check.That(shots.Length == 1, $"The ready holder fires once ({shots.Length}).");
        return shots[0];
    }

    /// <summary>
    /// <c>computeSuperweaponTarget</c>: a superweapon goes to the place of most value on the grid over the enemy's structures, where a command centre counts a tenth ("cannot be killed by any
    /// superweapon"), and cluster mines go on the player's own entrances, one of three drawn. Before, the most expensive object was the target, and mines went there too.
    /// </summary>
    public static void OriginalAiAimsItsSuperweaponsAsTheOriginal()
    {
        void Beta(ScriptedSkirmishSession session, SyntheticFixture fixture)
        {
            fixture.Templates.TryGet("BetaKeep", out var keep);
            fixture.Templates.TryGet("BetaHut", out var hut);
            session.World.RequestSpawn(keep.Index, new PlayerId(2), Fixed.FromInt(540), Fixed.FromInt(60), Fixed.Zero, Fixed.Zero);
            session.World.RequestSpawn(hut.Index, new PlayerId(2), Fixed.FromInt(420), Fixed.FromInt(340), Fixed.Zero, Fixed.Zero);
        }
        var shot = AiPowerShot("TestStrike", 11, AiSkirmishMap(), Beta);
        var (dx, dy) = (shot.X - Fixed.FromInt(420), shot.Y - Fixed.FromInt(340));
        Check.That(dx.Abs() < Fixed.FromInt(60) && dy.Abs() < Fixed.FromInt(60), $"The hut is worth more than the keep at a tenth of its cost ({shot.X}, {shot.Y}).");

        // The three entrances of player 1: the mines stand on the edge of the base towards the nearest waypoint of a path, which is what is drawn.
        var plain = AiSkirmishMap();
        MapObject Path(int id, string label, decimal x, decimal y) => new("*Waypoints/Waypoint", x, y, 0m, 0m, 0, new MapProperties([new MapProperty("waypointID", MapPropertyType.Int, id, 0m, string.Empty), Scr.Text("waypointName", label + ".Point"), Scr.Text("waypointPathLabel1", label)]));
        var map = plain with { Objects = [..plain.Objects, Path(100, "Center1", 400m, 200m), Path(101, "Flank1", 100m, 390m), Path(102, "Backdoor1", 100m, 10m)] };
        var directions = new SortedSet<string>(StringComparer.Ordinal);
        for (uint seed = 1; seed <= 12; seed++)
        {
            var mine = AiPowerShot("TestMines", unchecked(seed * 0x9E3779B1u), map);
            var (mx, my) = (mine.X - Fixed.FromInt(100), mine.Y - Fixed.FromInt(200));
            // The base of the fixture is centred off the start position, so the direction is told by its sign.
            directions.Add(mx > my.Abs() ? "centre" : my > Fixed.Zero ? "flank" : "backdoor");
        }
        Check.That(directions.Count == 3, $"Over twelve seeds all three entrances are mined ({string.Join(", ", directions)}).");
    }

    /// <summary>A human takes its slot's start position, whichever slot it is. Before, it took the position by the count of humans, the first one, on top of the computer player of that slot.</summary>
    public static void OriginalAiLeavesAHumanItsSlot()
    {
        var (session, _, _, _, _, _) = StartAiVariant(AiDataText(), string.Empty, humanSecond: true);
        var human = session.World.Entities.Single(entity => entity.Template.Name == "BetaCenter");
        Check.That(human.Owner == new PlayerId(2) && human.X == Fixed.FromInt(500) && human.Y == Fixed.FromInt(200), $"The human of the second slot starts at the second position ({human.X}, {human.Y}).");
        var computer = session.World.Entities.Single(entity => entity.Template.Name == "AlphaCenter");
        Check.That(computer.Owner == new PlayerId(1) && computer.X == Fixed.FromInt(100), "The computer player of the first slot has the first position.");
        Check.That(session.World.Entities.Count(entity => entity.Template.Name == "BetaSoldier" && entity.X > Fixed.FromInt(400)) == 1, "The human's start units stand around its base.");
        StepFrames(session, 300);
        Check.That(CountOwned(session, 1, "AlphaPlant") >= 1 && session.Engine.MissingActionHits.Count == 0, "The AI of the other slot plays on.");
    }

    /// <summary>
    /// <c>checkConditionsForTeamNames</c>: the first evaluation of a delayed script is offset by a draw of the logic random stream, so the seed moves it, and a restored engine does not draw again.
    /// Before, the offset came from the script's name, the same for every seed.
    /// </summary>
    public static void OriginalAiDrawsTheFirstEvaluationOfDelayedScripts()
    {
        var library = AiLibraryWithAlphaScripts(Scr.Script("A Delayed", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_FLAG", Scr.Flag("A Seen"), Scr.Bool(true))], oneShot: true, delaySeconds: 5));
        var frames = new SortedSet<long>();
        for (uint seed = 1; seed <= 12; seed++)
        {
            var (session, _, _, _, _, _) = StartAiVariant(AiDataText(), string.Empty, library, seed: unchecked(seed * 0x9E3779B1u));
            var frame = 0L;
            while (!session.Engine.FlagValue("A Seen0") && frame < 100)
            {
                StepFrames(session, 1);
                frame++;
            }
            Check.That(frame <= 61, $"The offset is at most two seconds ({frame}).");
            frames.Add(frame);
        }
        Check.That(frames.Count >= 4, $"The seed moves the first evaluation ({string.Join(", ", frames)}).");

        var (saved, fixture, terrain, map, _, data) = StartAiVariant(AiDataText(), string.Empty, library, seed: 0x9E3779B1u);
        var restored = ScriptedSkirmishSession.Restore(saved.Save(), fixture.Templates, fixture.Fingerprint, terrain, map, library, data);
        Check.That(restored.StateHash() == saved.StateHash(), "A restored engine has the drawn offsets and the stream that follows them: it does not draw again.");
        StepFrames(saved, 90);
        StepFrames(restored, 90);
        Check.That(restored.StateHash() == saved.StateHash(), "And goes on alike.");
    }

    /// <summary>
    /// <c>buildSpecificAITeam</c> and <c>recruitSpecificAITeam</c>: a team a script asks for needs a factory for every type or there is no team; the required and the optional units of a type are
    /// two orders, trained side by side at two factories; a recruit needs no home. Before, the order waited for a factory for ever, one unit was trained at a time, and nothing was recruited.
    /// </summary>
    public static void OriginalAiOrdersTeamsAsTheReference()
    {
        MapProperties Team(string name, int minimum, int maximum, string unit) => new(
        [
            Scr.Text("teamName", name), Scr.Text("teamOwner", "SkirmishAlpha"), Scr.Flag("teamIsSingleton", false), IntProperty("teamMaxInstances", 1),
            Scr.Text("teamUnitType1", unit), IntProperty("teamUnitMinCount1", minimum), IntProperty("teamUnitMaxCount1", maximum),
        ]);
        MapScript[] scripts =
        [
            Scr.Script("A Barracks", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SKIRMISH_BUILD_BUILDING", Scr.Type("AlphaBarracks"))], oneShot: true),
            ..AiAfter(300, Scr.Do("BUILD_TEAM", Scr.Team("A Pair")), Scr.Do("BUILD_TEAM", Scr.Team("A Beyond"))),
        ];
        var library = AiLibraryWithAlpha(scripts, [Team("A Pair", 1, 2, "AlphaSoldier"), Team("A Beyond", 1, 1, "AlphaGuard")]);
        var (session, _, _, _, _, _) = StartAiVariant(AiDataText(), AiYardObjects, library);
        session.RecordCommands();
        StepFrames(session, 420);
        var trained = session.RecordedCommands.OfType<CnCNew.Simulation.Commands.QueueProductionCommand>().Where(command => command.Template == "AlphaSoldier").ToArray();
        Check.That(trained.Length == 2 && trained[0].Producer != trained[1].Producer && trained[1].Tick - trained[0].Tick <= session.World.Time.OriginalFrames(1),
            $"The required and the optional soldier are trained side by side ({string.Join(", ", trained.Select(command => $"{command.Producer.Value}@{command.Tick}"))}).");
        Check.That(session.Engine.Diagnostics.Keys.Any(key => key.Contains("'A Beyond0': factories or tech missing", StringComparison.Ordinal)), "A team whose unit no factory can make does not come into being.");

        var recruit = AiLibraryWithAlpha(AiAfter(30, Scr.Do("RECRUIT_TEAM", Scr.Team("A Pair"), Scr.Real(0m))), [Team("A Pair", 1, 1, "AlphaSoldier") with { Items = [..Team("A Pair", 1, 1, "AlphaSoldier").Items, IntProperty("teamProductionPriority", 5)] }]);
        var (recruiting, _, _, _, _, _) = StartAiVariant(AiDataText(), string.Empty, recruit);
        StepFrames(recruiting, 60);
        Check.That(recruiting.Engine.TeamInstances.Single(team => team.Prototype == "A Pair0").Members.Count == 1, "A team without a home recruits, and its recruit walks to the origin of the map as in the reference.");
    }

    /// <summary>The session creates every object of the map, as the reference does; the option that leaves the scenery out is a named one. Before, the scenery was left out by default.</summary>
    public static void OriginalAiCreatesEveryMapObject()
    {
        Check.That(ScriptedSkirmishOptions.Standard.AllMapObjects, "The standard match has all the objects of the map.");
        const string Tree = """
            Object TestTree
              KindOf = IMMOBILE
              Geometry = CYLINDER
              GeometryMajorRadius = 4.0
              GeometryHeight = 10.0
              GeometryIsSmall = Yes
              Body = ActiveBody Body01
                MaxHealth = 10.0
                InitialHealth = 10.0
              End
              Behavior = DestroyDie Destroy01
              End
            End
            """;
        var plain = AiSkirmishMap();
        var map = plain with { Objects = [..plain.Objects, new MapObject("TestTree", 300m, 200m, 0m, 0m, 0, MapProperties.Empty)] };
        var files = AiFiles(AiDataText());
        files["Data/INI/Object/AiVariant.ini"] = Tree;
        var fixture = new SyntheticFixture(TestRate.Default, files: files);
        var terrain = TerrainMap.Build(map, "skirmish-original");
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        var slots = ImmutableArray.Create(new ScriptedSkirmishSlot(participants.Single(value => value.Side == "Alpha")), new ScriptedSkirmishSlot(participants.Single(value => value.Side == "Beta")));
        var options = ScriptedSkirmishOptions.Standard with { MaxTicks = 100 };
        var all = ScriptedSkirmishSession.Start(options, fixture.Templates, fixture.Fingerprint, terrain, map, AiLibrary(), AiData.TryBuild(fixture.Graph)!, slots);
        Check.That(all.World.Entities.Any(entity => entity.Template.Name == "TestTree"), "The tree stands.");
        var without = ScriptedSkirmishSession.Start(options with { AllMapObjects = false }, fixture.Templates, fixture.Fingerprint, terrain, map, AiLibrary(), AiData.TryBuild(fixture.Graph)!, slots);
        Check.That(!without.World.Entities.Any(entity => entity.Template.Name == "TestTree"), "The named option leaves it out.");
    }

    /// <summary>The script's <c>HACK_INTERNET</c> button makes a hacker earn money, also when it asks in every frame. Before, the button was counted as not executed and no credit came in.</summary>
    public static void OriginalAiHackersHack()
    {
        const string Hacker = """
            CommandButton Cmd_AlphaHack
              Command = HACK_INTERNET
            End
            Object AlphaHacker
              Side = Alpha
              KindOf = SELECTABLE INFANTRY
              BuildCost = 25
              BuildTime = 1
              Locomotor = SET_NORMAL SkirmishLoco
              Body = ActiveBody Body01
                MaxHealth = 50.0
                InitialHealth = 50.0
              End
              Behavior = HackInternetAIUpdate Hack01
                UnpackTime = 300
                PackTime = 300
                PackUnpackVariationFactor = 0.0
                CashUpdateDelay = 1000
                CashUpdateDelayFast = 500
                RegularCashAmount = 5
                VeteranCashAmount = 6
                EliteCashAmount = 8
                HeroicCashAmount = 10
                XpPerCashUpdate = 1
              End
              Behavior = DestroyDie Destroy01
              End
            End
            """;
        var library = AiLibraryWithAlphaScripts(Scr.Script("A Hack", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_USE_COMMANDBUTTON_ABILITY", Scr.Team("teamplayer"), Scr.Ability("Cmd_AlphaHack"))]));
        var (session, fixture, _, _, _, _) = StartAiVariant(AiDataText(), Hacker, library);
        Check.That(fixture.Templates.TryGet("AlphaHacker", out var template), "The fixture has the hacker.");
        var hacker = session.World.RequestSpawn(template.Index, new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(300), Fixed.Zero, Fixed.Zero);
        session.World.MaterializePendingSpawns();
        session.Engine.AssignToTeam("teamplayer0", hacker);
        var money = session.World.MoneyOf(new PlayerId(1));
        StepFrames(session, 300);
        Check.That(session.World.MoneyOf(new PlayerId(1)) >= money + Fixed.FromInt(25), $"The hacker has earned for seconds, the script asking all the while ({session.World.MoneyOf(new PlayerId(1))}, from {money}).");
    }
}

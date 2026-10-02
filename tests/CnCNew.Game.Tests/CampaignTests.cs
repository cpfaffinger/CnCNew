using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CnCNew.Content;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Game.Campaign;
using CnCNew.Game.Save;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Testing;

namespace CnCNew.Game.Tests;

/// <summary>Campaign.ini parsing, campaign progress, mission flow and the savegame format, on synthetic data; the real Campaign.ini when a content directory is given.</summary>
internal static class CampaignTests
{
    private const string Ini = """
        ; comment
        Campaign ALPHA
          CampaignNameLabel CAMPAIGN:ALPHA ; trailing comment
          FirstMission Mission01
          FinalVictoryMovie AlphaWon
          IsChallengeCampaign yes
          PlayerFaction FactionX
          Mission Mission01
            Map Maps\One\One.map
            IntroMovie IntroOne
            NextMission Mission02
            ObjectiveLine0 OBJ:0
            ObjectiveLine1 = OBJ:1
            UnitNames0 OBJECT:Tank
            VoiceLength = 17
            GeneralName GUI:General
          END
          Mission Mission02
            Map Maps\Two\Two.map
          END
        END

        Campaign BETA
          FirstMission M1
          Mission M1
            Map Maps\Loop.map
            NextMission M2
          END
          Mission M2
            Map Maps\Loop2.map
            NextMission M1
            Bogus Yes
          END
          Mission Lonely
            Map Maps\Nowhere.map
            NextMission Missing
          END
        END
        """;

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(CampaignTests));
        harness.Run("Campaign.ini subset parses into campaigns and missions", ParsesCatalog);
        harness.Run("Validation names dangling, cyclic, unreachable and missing-map problems", ValidatesCatalog);
        harness.Run("Progress advances on victory, stays on defeat and round trips as JSON", ProgressTransitions);
        harness.Run("Controller: start, win, lose, retry, attempts and seeds", ControllerFlow);
        harness.Run("Savegame: write, list, load and continue identically", SaveLoadContinues);
        harness.Run("Savegame: canonical bytes, rejection of foreign, damaged and truncated files", SaveRejection);
        harness.Run("Savegame store: atomic save, listing with problems, delete", StoreBehaves);
        var contentDir = ContentDir(args);
        if (contentDir is null) Console.WriteLine("SKIP CampaignTests real data (pass --content-dir <dir>)");
        else harness.Run("Real Campaign.ini: 17 campaigns parse, chains close, maps exist", () => RealCatalog(contentDir));
        return harness.Failures;
    }

    private static string? ContentDir(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void ParsesCatalog()
    {
        var catalog = CampaignCatalog.Parse(Ini);
        TestHarness.Assert(catalog.Campaigns.Length == 2, $"two campaigns (got {catalog.Campaigns.Length})");
        var alpha = catalog.Find("alpha")!;
        TestHarness.Assert(alpha.IsChallenge && alpha.PlayerFaction == "FactionX" && alpha.FinalVictoryMovie == "AlphaWon" && alpha.NameLabel == "CAMPAIGN:ALPHA", "campaign keys, comments stripped, names are case-insensitive");
        var first = alpha.FindMission("Mission01")!;
        TestHarness.Assert(first.MapPath == "Maps/One/One.map", $"backslashes become virtual paths (got {first.MapPath})");
        TestHarness.Assert(first.ObjectiveLabels.SequenceEqual(["OBJ:0", "OBJ:1"]) && first.UnitNames.SequenceEqual(["OBJECT:Tank"]) && first.VoiceLengthSeconds == 17 && first.GeneralName == "GUI:General" && first.IntroMovie == "IntroOne", "mission keys, both `Key Value` and `Key = Value`");
        TestHarness.Assert(alpha.Sequence.Select(mission => mission.Name).SequenceEqual(["Mission01", "Mission02"]), "sequence follows NextMission");
        TestHarness.Assert(alpha.NextOf(alpha.Sequence[1]) is null && alpha.NextOf(first)!.Name == "Mission02", "next mission, none after the last");
        TestHarness.Assert(catalog.UnknownKeys.SequenceEqual(["BETA.M2.Bogus"]), $"an unknown key is reported, not skipped silently (got {string.Join(",", catalog.UnknownKeys)})");
        TestHarness.Throws<InvalidDataException>(() => CampaignCatalog.Parse("Campaign X\n  FirstMission A\n"), "an unclosed block is refused");
        TestHarness.Throws<InvalidDataException>(() => CampaignCatalog.Parse("END\n"), "END without a block is refused");
        TestHarness.Throws<InvalidDataException>(() => CampaignCatalog.Parse("Mission A\nEND\n"), "a mission outside a campaign is refused");
    }

    private static void ValidatesCatalog()
    {
        var catalog = CampaignCatalog.Parse(Ini);
        var problems = catalog.Validate(path => path is "Maps/One/One.map" or "Maps/Two/Two.map" or "Maps/Loop.map" or "Maps/Loop2.map");
        TestHarness.Assert(problems.Any(text => text.Contains("Lonely") && text.Contains("unknown 'Missing'")), "a dangling NextMission is named");
        TestHarness.Assert(problems.Any(text => text.Contains("Lonely") && text.Contains("cannot be reached")), "an unreachable mission is named");
        TestHarness.Assert(problems.Any(text => text.Contains("Nowhere.map")), "a map missing from the content is named");
        TestHarness.Assert(!problems.Any(text => text.Contains("ALPHA")), "a sound campaign has no problems");
        TestHarness.Assert(catalog.Find("BETA")!.Sequence.Length == 2, "a cycle ends the walk instead of looping forever");
    }

    private static void ProgressTransitions()
    {
        var campaign = CampaignCatalog.Parse(Ini).Find("ALPHA")!;
        var progress = CampaignProgress.Begin(campaign, ScriptDifficulty.Hard);
        TestHarness.Assert(progress.CurrentMission == "Mission01" && !progress.Finished && progress.Completed.IsEmpty, "a new campaign starts at its first mission");
        progress = progress.Started().Started();
        TestHarness.Assert(progress.AttemptsOf("Mission01") == 2, "each start counts as an attempt");
        TestHarness.Assert(progress.Lost().CurrentMission == "Mission01", "a lost mission keeps the state");
        var won = progress.Won(campaign);
        TestHarness.Assert(won.CurrentMission == "Mission02" && won.Completed.Contains("Mission01") && !won.Finished, "a won mission completes and advances");
        var finished = won.Started().Won(campaign);
        TestHarness.Assert(finished.Finished && finished.Completed.Count == 2, "after the last mission the campaign is finished");
        TestHarness.Throws<InvalidOperationException>(() => finished.Started(), "a finished campaign cannot be started again");

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            var writer = new JsonStateWriter(json);
            writer.BeginObject(null);
            won.Describe(writer);
            writer.EndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        var loaded = CampaignProgress.Load(new JsonStateReader(document.RootElement));
        TestHarness.Assert(loaded.Campaign == won.Campaign && loaded.Difficulty == ScriptDifficulty.Hard && loaded.CurrentMission == "Mission02" && loaded.Completed.SetEquals(won.Completed)
                           && loaded.Attempts.SequenceEqual(won.Attempts) && loaded.Finished == won.Finished, "progress survives its JSON form");
    }

    // ---------------------------------------------------------------------------------------------------- synthetic missions

    private static MapScriptParameter Named(MapScriptParameterType type, string text) => new(type, 0, 0m, text, 0m, 0m, 0m);
    private static MapScriptParameter Number(int value) => new(MapScriptParameterType.Int, value, 0m, string.Empty, 0m, 0m, 0m);
    private static MapScriptParameter Seconds(decimal value) => new(MapScriptParameterType.Real, 0, value, string.Empty, 0m, 0m, 0m);

    private static MapScript Script(string name, MapCondition condition, params MapScriptAction[] actions)
        => new(name, string.Empty, string.Empty, string.Empty, true, true, true, true, true, false, 0, [new MapOrCondition([condition])], [..actions], []);

    private static MapCondition If(string name, params MapScriptParameter[] parameters) => new(MapScriptCatalog.ConditionTypeNames.IndexOf(name), name, [..parameters]);
    private static MapScriptAction Do(string name, params MapScriptParameter[] parameters) => new(MapScriptCatalog.ScriptActionTypeNames.IndexOf(name), name, [..parameters]);

    /// <summary>A 30x30-cell map with a human and an enemy side, one named barrel each, and <paramref name="scripts"/> in the human's list.</summary>
    private static MapData SyntheticMap(params MapScript[] scripts)
    {
        const int cells = 30, border = 1, width = cells + 2 * border;
        var heights = new byte[width * width];
        var stride = (width + 7) / 8;
        var blend = new MapBlendTiles(8, [], [], [], [], [..new byte[stride * width]], stride, 0, 0, [], [], [], []);
        MapProperty Text(string key, string value) => new(key, MapPropertyType.Ascii, 0, 0m, value);
        MapSide Side(string name, bool human, string enemies) => new(new MapProperties([Text("playerName", name), new MapProperty("playerIsHuman", MapPropertyType.Bool, human ? 1 : 0, 0m, string.Empty), Text("playerEnemies", enemies)]), []);
        MapProperties Team(string name, string owner) => new([Text("teamName", name), Text("teamOwner", owner), new MapProperty("teamIsSingleton", MapPropertyType.Bool, 1, 0m, string.Empty)]);
        MapObject Barrel(string team, string name, decimal x) => new("Barrel", x, 100m, 0m, 0m, 0, new MapProperties([Text("originalOwner", team), Text("objectName", name)]));
        return new MapData("synthetic", new MapHeightField(width, width, border, [new MapBoundary(cells, cells)], [..heights]), blend, MapProperties.Empty,
            [new MapSide(new MapProperties([Text("playerName", string.Empty)]), []), Side("Hero", true, "Villain"), Side("Villain", false, "Hero")],
            [Team("teamHero", "Hero"), Team("teamVillain", "Villain")], [Barrel("teamHero", "HeroBarrel", 50m), Barrel("teamVillain", "VillainBarrel", 200m)], [], [],
            [new MapScriptList([], []), new MapScriptList([..scripts], []), new MapScriptList([], [])], []);
    }

    private static readonly MapScript[] WinAndLose =
    [
        Script("Win", If("NAMED_DESTROYED", Named(MapScriptParameterType.Unit, "VillainBarrel")), Do("VICTORY")),
        Script("Lose", If("NAMED_DESTROYED", Named(MapScriptParameterType.Unit, "HeroBarrel")), Do("DEFEAT")),
        Script("Retry", If("MISSION_ATTEMPTS", Named(MapScriptParameterType.Side, "Hero"), new(MapScriptParameterType.Comparison, 3, 0m, string.Empty, 0m, 0m, 0m), Number(2)),
            Do("SET_FLAG", Named(MapScriptParameterType.Flag, "second try"), new(MapScriptParameterType.Boolean, 1, 0m, string.Empty, 0m, 0m, 0m))),
    ];

    private static (MissionContext Context, CampaignCatalog Catalog, CampaignDefinition Campaign, SyntheticFixture Fixture) SyntheticCampaign()
    {
        var fixture = new SyntheticFixture();
        var maps = new Dictionary<string, MapData>(StringComparer.OrdinalIgnoreCase)
        {
            ["Maps/One/One.map"] = SyntheticMap(WinAndLose),
            ["Maps/Two/Two.map"] = SyntheticMap(WinAndLose),
        };
        var context = new MissionContext(fixture.Templates, fixture.Fingerprint, path => maps.TryGetValue(path, out var map) ? map : throw new FileNotFoundException(path));
        var catalog = CampaignCatalog.Parse("Campaign TWO\n  FirstMission A\n  Mission A\n    Map Maps\\One\\One.map\n    NextMission B\n  END\n  Mission B\n    Map Maps\\Two\\Two.map\n  END\nEND\n");
        return (context, catalog, catalog.Find("TWO")!, fixture);
    }

    private static void Step(MissionRun run, int ticks)
    {
        for (var index = 0; index < ticks && !run.Session.IsOver; index++) run.Session.Step();
    }

    private static void Destroy(MissionRun run, string name)
    {
        var id = run.Session.Engine.NamedObjects.First(item => item.Key == name).Value;
        run.Session.World.Kill(new CnCNew.Simulation.Ids.EntityId(id));
    }

    private static void ControllerFlow()
    {
        var (context, _, campaign, _) = SyntheticCampaign();
        var controller = new CampaignController(context, campaign, CampaignProgress.Begin(campaign, ScriptDifficulty.Normal));
        var run = controller.StartCurrentMission();
        TestHarness.Assert(controller.Progress.AttemptsOf("A") == 1 && run.Mission!.Name == "A", "starting counts the attempt");
        Step(run, 5);
        TestHarness.Throws<InvalidOperationException>(() => controller.Conclude(run), "a running mission has no conclusion");
        Destroy(run, "HeroBarrel");
        Step(run, 10);
        TestHarness.Assert(run.Session.Outcome == MissionOutcome.Defeat, "the defeat script ended the mission");
        TestHarness.Assert(controller.Conclude(run) == MissionConclusion.Defeat && controller.Progress.CurrentMission == "A", "defeat keeps the current mission");

        var retry = controller.StartCurrentMission();
        Step(retry, 3);
        TestHarness.Assert(controller.Progress.AttemptsOf("A") == 2 && retry.Session.Engine.FlagValue("second try"), "the retry is attempt two and the script sees it");
        TestHarness.Assert(retry.Session.Options.WorldSeed != run.Session.Options.WorldSeed, "a retry does not repeat the seed of the last try");
        TestHarness.Assert(retry.Session.Options.PresentationLatencyTicks == 0, "no presentation latency by default");
        Destroy(retry, "VillainBarrel");
        Step(retry, 10);
        TestHarness.Assert(controller.Conclude(retry) == MissionConclusion.Victory && controller.Progress.CurrentMission == "B" && controller.Progress.Completed.Contains("A"), "victory advances the campaign");

        var delayed = new CampaignController(context, campaign, CampaignProgress.Begin(campaign, ScriptDifficulty.Normal)).StartCurrentMission(presentationLatencyTicks: 90);
        TestHarness.Assert(delayed.Session.Options.PresentationLatencyTicks == 90, "a host without a presentation director can set the presentation latency of the mission it starts");

        var last = controller.StartCurrentMission();
        Step(last, 3);
        Destroy(last, "VillainBarrel");
        Step(last, 10);
        Step(last, 200);
        TestHarness.Assert(last.Session.IsOver, "the mission is over after the end-game delay");
        controller.Conclude(last);
        TestHarness.Assert(controller.IsFinished, "the campaign finishes after its last mission");
        TestHarness.Throws<InvalidOperationException>(() => controller.StartCurrentMission(), "nothing to start after the end");
    }

    private static void SaveLoadContinues()
    {
        var (context, catalog, campaign, _) = SyntheticCampaign();
        var controller = new CampaignController(context, campaign, CampaignProgress.Begin(campaign, ScriptDifficulty.Hard));
        var run = controller.StartCurrentMission();
        Step(run, 40);
        var save = SaveGame.Write(run, controller.Progress, "Mission A, tick 40");
        var header = SaveGame.ReadHeader(save);
        TestHarness.Assert(header.Title == "Mission A, tick 40" && header.Campaign == "TWO" && header.Mission == "A" && header.Tick == 40 && header.MapPath == "Maps/One/One.map" && header.Difficulty == ScriptDifficulty.Hard, "the header describes the save");

        var loaded = SaveGame.Load(save, context, catalog);
        TestHarness.Assert(loaded.Progress is not null && loaded.Progress.Difficulty == ScriptDifficulty.Hard && loaded.Progress.AttemptsOf("A") == 1 && loaded.Run.Mission!.Name == "A", "progress and mission come back");
        TestHarness.Assert(loaded.Run.Session.StateHash() == run.Session.StateHash(), "the restored mission has the saved state hash");
        var chainOriginal = 0UL;
        var chainLoaded = 0UL;
        for (var tick = 0; tick < 200; tick++)
        {
            if (tick == 20) { Destroy(run, "VillainBarrel"); Destroy(loaded.Run, "VillainBarrel"); }
            chainOriginal = StateHash.Mix(chainOriginal, run.Session.Step().StateHash);
            chainLoaded = StateHash.Mix(chainLoaded, loaded.Run.Session.Step().StateHash);
        }
        TestHarness.Assert(chainOriginal == chainLoaded && run.Session.StateHash() == loaded.Run.Session.StateHash(), "original and loaded mission run identically, through a victory");
        TestHarness.Assert(loaded.Run.Session.Outcome == MissionOutcome.Victory, "the victory happened in the loaded run");

        var resumed = new CampaignController(context, campaign, loaded.Progress!);
        var again = resumed.ResumeCurrentMission(MissionSnapshot.Write(run.Session));
        TestHarness.Assert(resumed.Progress.AttemptsOf("A") == 1 && again.Session.StateHash() == run.Session.StateHash(), "resuming a run does not count a new attempt");
    }

    private static void SaveRejection()
    {
        var (context, catalog, campaign, fixture) = SyntheticCampaign();
        var controller = new CampaignController(context, campaign, CampaignProgress.Begin(campaign, ScriptDifficulty.Normal));
        var run = controller.StartCurrentMission();
        Step(run, 10);
        var save = SaveGame.Write(run, controller.Progress, "t");
        TestHarness.Assert(SaveGame.Write(run, controller.Progress, "t").AsSpan().SequenceEqual(save), "two saves of one state are byte-identical");
        var restored = SaveGame.Load(save, context, catalog).Run;
        TestHarness.Assert(SaveGame.Write(restored, controller.Progress, "t").AsSpan().SequenceEqual(save), "loading and saving again reproduces the file byte for byte");
        TestHarness.Assert(!Encoding.UTF8.GetString(save).Contains("\\\\", StringComparison.Ordinal) && !Encoding.UTF8.GetString(save).Contains(":/", StringComparison.Ordinal), "no absolute paths in a save");

        void Refused(byte[] bytes, string part, string why)
        {
            try { SaveGame.Load(bytes, context, catalog); }
            catch (SaveGameException exception) when (exception.Message.Contains(part, StringComparison.OrdinalIgnoreCase)) { return; }
            catch (Exception exception) { throw new InvalidOperationException($"{why}: expected a SaveGameException containing '{part}', got {exception.GetType().Name}: {exception.Message}"); }
            throw new InvalidOperationException($"{why}: the save was accepted");
        }

        Refused(save.AsSpan(0, save.Length / 2).ToArray(), "damaged", "a truncated file");
        Refused("not json"u8.ToArray(), "damaged", "garbage");
        Refused("{\"format\":\"something-else\"}"u8.ToArray(), "Not a CnCNew savegame", "a foreign document");
        Refused(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(save).Replace("\"version\":1,", "\"version\":7,", StringComparison.Ordinal)), "Unsupported savegame version 7", "another version");
        var otherData = new MissionContext(fixture.Templates, "00" + context.Fingerprint, context.LoadMap);
        try { SaveGame.Load(save, otherData, catalog); throw new InvalidOperationException("other game data accepted"); }
        catch (SaveGameException exception) { TestHarness.Assert(exception.Message.Contains("different game data"), "other game data is named"); }
        Refused(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(save).Replace("\"campaign\":\"TWO\"", "\"campaign\":\"NOPE\"", StringComparison.Ordinal)), "campaign", "a campaign this game does not have");
        var flipped = (byte[])save.Clone();
        var at = Array.LastIndexOf(flipped, (byte)'1', flipped.Length - 50);
        flipped[at] = (byte)(flipped[at] == '1' ? '2' : '1');
        Refused(flipped, "", "a flipped byte");
    }

    private static void StoreBehaves()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-saves-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (context, catalog, campaign, _) = SyntheticCampaign();
            var controller = new CampaignController(context, campaign, CampaignProgress.Begin(campaign, ScriptDifficulty.Normal));
            var run = controller.StartCurrentMission();
            Step(run, 5);
            var store = new SaveGameStore(directory);
            TestHarness.Assert(store.List().IsEmpty, "an absent directory lists nothing");
            store.Save("beta", SaveGame.Write(run, controller.Progress, "second"));
            store.Save("alpha", SaveGame.Write(run, controller.Progress, "first"));
            File.WriteAllText(Path.Combine(directory, "broken" + SaveGameStore.Extension), "{ not a save");
            var list = store.List();
            TestHarness.Assert(list.Select(slot => slot.Name).SequenceEqual(["alpha", "beta", "broken"]), "slots are listed in name order");
            TestHarness.Assert(list[0].Header!.Title == "first" && list[2].Header is null && list[2].Problem is not null, "a damaged slot is listed with its problem instead of throwing");
            TestHarness.Assert(!File.Exists(Path.Combine(directory, "alpha" + SaveGameStore.Extension + ".tmp")), "no temporary file is left behind");
            store.Save("alpha", SaveGame.Write(run, controller.Progress, "overwritten"));
            TestHarness.Assert(store.List()[0].Header!.Title == "overwritten", "saving over a slot replaces it");
            TestHarness.Assert(SaveGame.Load(store.Read("beta"), context, catalog).Run.Session.Options.Difficulty == ScriptDifficulty.Normal, "a stored save loads");
            TestHarness.Throws<ArgumentException>(() => store.PathOf("../escape"), "slot names cannot escape the directory");
            TestHarness.Throws<SaveGameException>(() => store.Read("missing"), "reading a missing slot says so");
            TestHarness.Assert(store.Delete("beta") && !store.Delete("beta"), "delete reports whether there was something");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void RealCatalog(string contentDir)
    {
        var content = ContentFileSystem.Open(contentDir);
        using var stream = content.OpenRead("Data/INI/Campaign.ini");
        var catalog = CampaignCatalog.Parse(stream);
        Console.WriteLine($"  Campaign.ini: {catalog.Campaigns.Length} campaigns, {catalog.Campaigns.Sum(campaign => campaign.Missions.Length)} missions");
        TestHarness.Assert(catalog.Campaigns.Length == 17, $"17 campaigns (got {catalog.Campaigns.Length})");
        TestHarness.Assert(catalog.Campaigns.Sum(campaign => campaign.Missions.Length) == 85, "85 missions");
        using var personaStream = content.OpenRead("Data/INI/ChallengeMode.ini");
        using var personaReader = new StreamReader(personaStream);
        var withPersonas = catalog.WithPersonas(personaReader.ReadToEnd());
        TestHarness.Assert(withPersonas.Find("CHALLENGE_0")!.PlayerFaction == "FactionAmericaAirForceGeneral" && withPersonas.Find("CHALLENGE_1")!.PlayerFaction == "FactionGLAToxinGeneral" && withPersonas.Find("CHALLENGE_2")!.PlayerFaction == "FactionChinaNukeGeneral", "each challenge campaign gets the faction of its general persona (ChallengeMode.ini), CHALLENGE_0 keeps its own");
        TestHarness.Assert(withPersonas.Campaigns.Where(campaign => !campaign.IsChallenge).All(campaign => campaign.PlayerFaction.Length == 0), "story campaigns have no persona");
        TestHarness.Assert(catalog.UnknownKeys.IsEmpty, "no unknown keys: " + string.Join(", ", catalog.UnknownKeys));
        var problems = catalog.Validate(content.Exists);
        // The EA demo map is not part of the retail install; everything else must be consistent.
        TestHarness.Assert(problems.Length == 1 && problems[0].Contains("CampEADemo"), "only the EA demo map is absent: " + string.Join("; ", problems));
        TestHarness.Assert(catalog.Find("USA")!.Sequence.Length == 5 && catalog.Find("CHALLENGE_0")!.IsChallenge, "campaign chains are complete");

        // Generals Challenge: a map that marks its own human side decides itself; GC_LaserGeneral marks none and gets a synthetic human side with the persona faction.
        MapData Load(string path)
        {
            using var mapStream = content.OpenRead(path);
            return SageMapDecoder.Decode(mapStream);
        }
        var challengeMaps = catalog.Campaigns.Where(campaign => campaign.IsChallenge).SelectMany(campaign => campaign.Sequence).Select(mission => mission.MapPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var sides = challengeMaps.ToDictionary(path => path, path => ChallengeSides.HumanSideFor(Load(path)), StringComparer.OrdinalIgnoreCase);
        TestHarness.Assert(challengeMaps.Length == 8, $"eight distinct challenge maps (got {challengeMaps.Length})");
        TestHarness.Assert(sides["Maps/GC_LaserGeneral/GC_LaserGeneral.map"] == ChallengeSides.SyntheticHumanSide, "the laser map without a human side gets the synthetic human side: " + sides["Maps/GC_LaserGeneral/GC_LaserGeneral.map"]);;
        TestHarness.Assert(sides["Maps/GC_Stealth/GC_Stealth.map"].Length == 0 && sides["Maps/GC_TankGeneral/GC_TankGeneral.map"].Length == 0, "maps that mark ThePlayer as human need no host choice");
        foreach (var (path, side) in sides)
            if (side.Length > 0 && side != ChallengeSides.SyntheticHumanSide) TestHarness.Assert(ScriptProgram.Build(Load(path)).FindPlayer(side) is not null, $"{path}: the chosen side exists");

        // The persona replaces the human side faction; on the laser map a human side is added that fights the opponent general and is fought by it.
        const string persona = "FactionAmericaAirForceGeneral";
        var laser = ScriptProgram.Build(Load("Maps/GC_LaserGeneral/GC_LaserGeneral.map")).WithHumanSide(ChallengeSides.SyntheticHumanSide, persona);
        var added = laser.FindPlayer(ChallengeSides.SyntheticHumanSide);
        TestHarness.Assert(added is { IsHuman: true } && added.Faction == persona && added.Enemies.Contains("PlyrAmericaLaserGeneral") && !added.Enemies.Contains("PlyrCivilian"), "the added human side has the persona faction and is hostile to the opponent, not to civilians");
        TestHarness.Assert(laser.FindPlayer("PlyrAmericaLaserGeneral")!.Enemies.Contains(ChallengeSides.SyntheticHumanSide), "the opponent general is hostile to the added side");
        TestHarness.Assert(laser.Fingerprint == ScriptProgram.Build(Load("Maps/GC_LaserGeneral/GC_LaserGeneral.map")).Fingerprint, "the program fingerprint stays the map one");
        var chem = ScriptProgram.Build(Load("Maps/GC_ChemGeneral/GC_ChemGeneral.map")).WithHumanSide(string.Empty, persona);
        TestHarness.Assert(chem.FindPlayer("ThePlayer")!.Faction == persona && chem.Players.Length == ScriptProgram.Build(Load("Maps/GC_ChemGeneral/GC_ChemGeneral.map")).Players.Length, "a map with its own human side only gets the persona faction, no extra side");
    }
}

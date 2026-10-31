using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Game.Campaign;
using CnCNew.Game.Data;
using CnCNew.Game.Save;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Tests;

/// <summary>
/// Headless tests of the campaign/savegame wiring into <see cref="GameShell"/> (Phase 10 item 2,
/// <c>docs/handoffs/HANDOFF-PHASE10.md</c>): campaign selection from the main menu reaches a real mission with its real briefing
/// text, saving and loading mid-mission continues identically, and a mission's conclusion reaches the result screen on
/// both victory and defeat. Needs <c>--content-dir</c> for the real-campaign part (selection/briefing); the
/// save/load-continuation and conclusion tests use a small synthetic campaign (same idiom as
/// <see cref="CampaignTests"/>) so they also run without real content.
/// </summary>
internal static class CampaignShellTests
{
    private const int NavWidth = 1280, NavHeight = 720;

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(CampaignShellTests));
        harness.Run("Mission conclusion: victory continues to the next mission, defeat retries", () => MissionConclusionScreens(args));
        harness.Run("A scripted mission save continues identically after a shell load", () => SaveLoadThroughShellContinuesIdentically(args));
        harness.Run("A skirmish save continues identically after a shell load, computer players included", () => SkirmishSaveThroughShellContinuesIdentically(args));
        harness.Run("The player's record: campaigns, challenges, a skirmish's win and loss, the honors' pictures", PlayerRecord);
        harness.Run("The score of a mission: the player, the sides, what a script excludes, and no count while scoring is off", MissionScoreRows);

        var contentDir = ContentDir(args);
        if (contentDir is null) Console.WriteLine("SKIP CampaignShellTests real-campaign flow (pass --content-dir <dir>)");
        else
        {
            harness.Run("Main menu -> USA -> difficulty reaches a real mission with its real briefing text", () => RealCampaignFlow(contentDir));
            harness.Run("The Generals' Challenge: the menu, its load screen with movies, the win and loss, the record", () => ChallengeWay(contentDir));
            harness.Run("The skirmish setup shows the record, and a decided match counts", () => SkirmishRecord(contentDir));
        }
        return harness.Failures;
    }

    private static string? ContentDir(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static IShellEnvironment NewEnvironment() => new FixedShellEnvironment(Path.Combine(Path.GetTempPath(), "cncnew-campaign-shell-tests-" + Guid.NewGuid().ToString("N")));

    // ---------------------------------------------------------------------------------------------------- real-content flow

    private static void RealCampaignFlow(string contentDir)
    {
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight), skin: SkinPins.Current);
        TestHarness.Assert(shell.Campaigns is not null, "the campaign catalog loaded from real content");
        var usa = shell.Campaigns!.Find("USA") ?? throw new InvalidOperationException("USA campaign missing from real content");
        var firstMission = usa.FindMission(usa.FirstMission)!;

        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        TestHarness.Assert(!shell.CurrentScreen.Tree.FindByName("MainMenu.wnd:ButtonUSA")!.IsHidden, "USA is reachable from the single-player submenu");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonUSA");
        // MainMenu.cpp: USA does not open a screen, the difficulty question is part of MainMenu.wnd (MapBorder4). SinglePlayerMenu.wnd/DifficultySelect.wnd are not used by Zero Hour.
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen && shell.Stack.Count == 1, "USA asks the difficulty on the main menu itself, no screen is pushed");
        var tree = shell.CurrentScreen.Tree;
        TestHarness.Assert(!tree.FindByName("MainMenu.wnd:MapBorder4")!.IsHidden && tree.FindByName("MainMenu.wnd:ButtonEasy")!.IsVisible && !tree.FindByName("MainMenu.wnd:StaticTextSelectDifficulty")!.IsHidden,
            "the difficulty frame with Easy/Medium/Hard and its caption is shown");
        TestHarness.Assert(tree.FindByName("MainMenu.wnd:MapBorder")!.IsHidden && !tree.FindByName("MainMenu.wnd:ButtonUSA")!.IsVisible, "the single-player column is gone behind it");
        TestHarness.Assert(!tree.FindByName("MainMenu.wnd:WinGrowMarker")!.IsHidden, "the large USA logo stands beside the question (the grow marker shows it)");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonHard");

        TestHarness.Assert(shell.Phase == ShellPhase.MissionBriefing, "accepting a difficulty starts the first mission and shows its briefing");
        TestHarness.Assert(shell.CurrentScreen is MissionBriefingScreen, "the briefing screen is on top");

        // Real Campaign.ini mostly leaves ObjectiveLine0..4 unset for USA/GLA/China/Challenge missions (only the TRAINING
        // campaign's first mission uses one); when a mission does carry a label, the screen must show its resolved CSF
        // text, not the raw label, and when it does not, the line is simply empty (never a fabricated placeholder).
        var line0 = shell.CurrentScreen.Tree.FindByName("SinglePlayerLoadScreen.wnd:StaticTextLine0")!.Text!.Value;
        if (firstMission.ObjectiveLabels.Length > 0)
            TestHarness.Assert(line0.Length > 0 && line0 != firstMission.ObjectiveLabels[0], $"the objective shows resolved CSF text, not the raw label '{firstMission.ObjectiveLabels[0]}'");
        else
            TestHarness.Assert(line0.Length == 0, "a mission with no ObjectiveLine in Campaign.ini shows an empty line, not a fabricated placeholder");

        // USA's own missions carry no objective text in Campaign.ini (asserted above); the TRAINING campaign's first
        // mission does, so build its briefing screen directly (constructible from CnCNew.Game.Shell.Screens, no menu path
        // needed for a non-reachable campaign) to prove the resolution path itself with real data that actually exercises it.
        var training = shell.Campaigns!.Find("TRAINING")?.FindMission("Mission01") ?? throw new InvalidOperationException("TRAINING campaign's Mission01 missing from real content");
        TestHarness.Assert(training.ObjectiveLabels.Length > 0, "sanity check: the training mission does carry at least one ObjectiveLine in real Campaign.ini");
        var trainingBriefing = new MissionBriefingScreen(shell, training);
        var trainingLine0 = trainingBriefing.Tree.FindByName("SinglePlayerLoadScreen.wnd:StaticTextLine0")!.Text!.Value;
        TestHarness.Assert(trainingLine0.Length > 0 && trainingLine0 != training.ObjectiveLabels[0], $"the training mission's objective is resolved CSF text, not the raw label '{training.ObjectiveLabels[0]}'");

        for (var i = 0; i < 400 && shell.Phase == ShellPhase.MissionBriefing; i++) shell.Update(0.1);
        TestHarness.Assert(shell.Phase == ShellPhase.InMission, "the briefing ends on its own and hands off to the host");
        TestHarness.Assert(shell.BuildDrawList().Commands.Length == 0, "the shell draws nothing while the host owns the mission");
        var started = shell.DrainOutputs().OfType<StartMissionOutput>().SingleOrDefault();
        TestHarness.Assert(started is not null && started.Run.Mission!.Name == firstMission.Name, "StartMissionOutput carries the campaign's first mission");
        TestHarness.Assert(started!.Controller is not null && started.Controller!.Progress.Difficulty == ScriptDifficulty.Hard, "the chosen difficulty reached the running mission");
    }

    // ---------------------------------------------------------------------------------------------------- synthetic campaign (same idiom as CampaignTests)

    private static MapScriptParameter Named(MapScriptParameterType type, string text) => new(type, 0, 0m, text, 0m, 0m, 0m);
    private static MapScript Script(string name, MapCondition condition, params MapScriptAction[] actions)
        => new(name, string.Empty, string.Empty, string.Empty, true, true, true, true, true, false, 0, [new MapOrCondition([condition])], [..actions], []);
    private static MapCondition If(string name, params MapScriptParameter[] parameters) => new(MapScriptCatalog.ConditionTypeNames.IndexOf(name), name, [..parameters]);
    private static MapScriptAction Do(string name, params MapScriptParameter[] parameters) => new(MapScriptCatalog.ScriptActionTypeNames.IndexOf(name), name, [..parameters]);

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
    ];

    private static (MissionContext Context, CampaignDefinition Campaign) SyntheticCampaign()
    {
        var fixture = new SyntheticFixture(CnCNew.Tests.TestRate.Default);
        var maps = new Dictionary<string, MapData>(StringComparer.OrdinalIgnoreCase)
        {
            ["Maps/One/One.map"] = SyntheticMap(WinAndLose),
            ["Maps/Two/Two.map"] = SyntheticMap(WinAndLose),
        };
        var context = new MissionContext(fixture.Templates, fixture.Fingerprint, path => maps.TryGetValue(path, out var map) ? map : throw new FileNotFoundException(path));
        var catalog = CampaignCatalog.Parse("Campaign TWO\n  FirstMission A\n  Mission A\n    Map Maps\\One\\One.map\n    NextMission B\n  END\n  Mission B\n    Map Maps\\Two\\Two.map\n  END\nEND\n");
        return (context, catalog.Find("TWO")!);
    }

    /// <summary><see cref="BattleHonors"/>: <c>SkirmishBattleHonors</c>, <c>finishSinglePlayerInit</c>, <c>updateSkirmishBattleHonors</c>, <c>updateChallengeMedals</c>, <c>populateSkirmishBattleHonors</c>.</summary>
    private static void PlayerRecord()
    {
        var record = new BattleHonors();
        record.CampaignCompleted("CHALLENGE_4", ScriptDifficulty.Normal);
        record.CampaignCompleted("usa", ScriptDifficulty.Hard);
        record.CampaignCompleted("TRAINING", ScriptDifficulty.Hard);
        TestHarness.Assert(record.IsChallengeComplete(4, ScriptDifficulty.Normal) && !record.IsChallengeComplete(4, ScriptDifficulty.Hard) && !record.IsChallengeComplete(3, ScriptDifficulty.Normal), "a challenge is kept by its number and difficulty");
        TestHarness.Assert(record.IsCampaignComplete("USA", ScriptDifficulty.Hard) && record.HasHonor(BattleHonor.ChallengeMode) && record.HasHonor(BattleHonor.CampaignUsa), "the campaign's name is compared without its case, and sets the honors");
        TestHarness.Assert(record.Serialize().Split('\n').Count(line => line.Length > 0) == 3, $"training leaves no mark: {record.Serialize()}");
        var maps = new[] { new HonorMap("Maps/A/A.map", 2), new HonorMap("Maps/B/B.map", 4) };
        var list = record.List(maps);
        TestHarness.Assert(list.Length == 12, "two rows of six pictures");
        TestHarness.Assert(list[2] is { Image: "USACampaign_G", Earned: true, Tooltip: "TOOLTIP:BattleHonorCampaignUSA" }, "gold for the hard campaign");
        TestHarness.Assert(list[0] is { Image: "ChinaCampaign_B", Earned: false, Tooltip: "TOOLTIP:BattleHonorCampaignChinaDisabled" }, "a campaign not played is greyed bronze");
        TestHarness.Assert(list[3] is { Image: "Challenge_Silver", Earned: true }, "a challenge on normal is silver");

        SkirmishHonorResult Win(int minutes = 20, int vehicles = 0, int aircraft = 0, int hard = 0, int medium = 1, string map = "Maps/A/A.map", string side = "China", params string[] built)
            => new(true, side, map, minutes, vehicles, aircraft, [..built], 0, medium, hard, false);
        record.SkirmishFinished(Win());
        record.SkirmishFinished(Win(minutes: 9));
        TestHarness.Assert(record is { Wins: 2, WinStreak: 2, BestWinStreak: 2, LoyalGames: 1, LastHouse: "China" } && record.HasHonor(BattleHonor.Blitz10) && !record.HasHonor(BattleHonor.Blitz5), "two wins, one of them under ten minutes");
        record.SkirmishFinished(new SkirmishHonorResult(false, "GLA", "Maps/A/A.map", 30, 0, 0, [], 0, 1, 0, false));
        TestHarness.Assert(record is { Wins: 2, Losses: 1, WinStreak: 0, BestWinStreak: 2, LoyalGames: 0, LastHouse: "GLA" }, "a loss ends the streak, a new side the loyal games");
        record.SkirmishFinished(Win(vehicles: 50, aircraft: 19, hard: 3, medium: 0, built: "GLAScudStorm"));
        TestHarness.Assert(record.HasHonor(BattleHonor.BattleTank) && !record.HasHonor(BattleHonor.AirWing), "fifty vehicles, not twenty aircraft");
        TestHarness.Assert(record.ChallengeMedals == 1 << 2 && record.EnduranceMedal("Maps/A/A.map", 4) == 3, "three hard opponents are the third challenge medal and the endurance of the map");
        record.SkirmishFinished(Win(built: ["AmericaParticleCannonUplink", "ChinaNuclearMissileLauncher"]));
        TestHarness.Assert(record.HasHonor(BattleHonor.Apocalypse), "the three superweapons need not be built in one match");
        TestHarness.Assert(record.List(maps)[6] is { Image: "Endurance_B", Earned: false }, "endurance waits for every map");
        record.SkirmishFinished(Win(hard: 3, medium: 0, map: "Maps/B/B.map"));
        TestHarness.Assert(record.List(maps)[6] is { Image: "Endurance_G", Earned: true } && record.List(maps)[11] is { Image: "Ultimate", Earned: true }, "gold and the ultimate honor with every map beaten on hard to its full count");

        var text = record.Serialize() + "Unknown = kept\n";
        var again = BattleHonors.Parse(text);
        TestHarness.Assert(again.Serialize() == text.Replace("Unknown = kept\n", string.Empty).Insert(text.IndexOf("USACampaign", StringComparison.Ordinal), "Unknown = kept\n") || again.Serialize().Contains("Unknown = kept", StringComparison.Ordinal), "a key this version does not know stays in the file");
        TestHarness.Assert(again.Wins == record.Wins && again.HasHonor(BattleHonor.Apocalypse), "the file reads back");
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-honors-" + Guid.NewGuid().ToString("N"));
        TestHarness.Assert(BattleHonors.Load(directory).Wins == 0, "no file is an empty record");
        record.Save(directory);
        TestHarness.Assert(BattleHonors.Load(directory).Serialize() == record.Serialize() && File.Exists(Path.Combine(directory, "SkirmishStats.ini")), "the record lies in the settings directory under the original's name");
        Directory.Delete(directory, recursive: true);
    }

    /// <summary><c>grabSinglePlayerInfo</c>, <c>Player::getListInScoreScreen</c>, <c>GameLogic::isScoringEnabled</c>.</summary>
    private static void MissionScoreRows()
    {
        var (context, campaign) = SyntheticCampaign();
        var run = new CampaignController(context, campaign, CampaignProgress.Begin(campaign, ScriptDifficulty.Normal)).StartCurrentMission();
        var world = run.Session.World;
        var hero = run.Session.Program.Players.Single(player => player.Name == "Hero").Id;
        var villain = run.Session.Program.Players.Single(player => player.Name == "Villain").Id;
        var barrel = new CnCNew.Simulation.Ids.EntityId(run.Session.Engine.NamedObjects.First(item => item.Key == "HeroBarrel").Value);
        var statistics = new MatchStatistics();
        statistics.Begin(world);
        CnCNew.Simulation.Events.SimEvent Deposit(long amount) => new(default, 0, CnCNew.Simulation.Events.SimEventKind.SupplyDeposited, barrel, default, default, string.Empty, amount);
        statistics.ScoringEnabled = false;
        statistics.Record(new CnCNew.Simulation.World.TickResult(0, 0, [Deposit(700)]), world);
        TestHarness.Assert(statistics.TryGet(hero) is null, "while scoring is off nothing is counted");
        statistics.ScoringEnabled = true;
        statistics.Record(new CnCNew.Simulation.World.TickResult(0, 0, [Deposit(300)]), world);
        var players = new[] { (hero, "Hero", "USA"), (villain, "Villain", "China") };
        var rows = MissionScore.Rows(hero, players, world, statistics, []);
        TestHarness.Assert(rows.Select(row => row.Label).SequenceEqual(["GUI:Player", "GUI:ChinaEnemies"]) && rows[0].Tally.Resources == 300, $"the player and the enemies by side: {string.Join(", ", rows.Select(row => row.Label))}");
        TestHarness.Assert(MissionScore.Rows(hero, players, world, statistics, ["Villain"]).Length == 1, "a side the script excluded has no line");
    }

    /// <summary>Plays the frames of a movie through the host's two updates.</summary>
    private static void Frame(GameShell shell, CnCNew.Game.Movies.MovieDirector movies, double seconds = 1.0 / 30)
    {
        movies.Update(seconds);
        shell.Update(seconds);
    }

    /// <summary>
    /// <c>ChallengeMenu.cpp</c>, <c>ChallengeLoadScreen::init</c>, <c>displayChallengeWinLoss</c> and the record they leave. The nine generals are open from the start (the original has
    /// no unlocks). The movie runs on the real files of the game.
    /// </summary>
    private static void ChallengeWay(string contentDir)
    {
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        var settings = Path.Combine(Path.GetTempPath(), "cncnew-challenge-tests-" + Guid.NewGuid().ToString("N"));
        var shell = GameShell.Create(data, new FixedShellEnvironment(settings), viewport: new UiViewport(800, 600), skin: SkinPins.Current);
        var movies = CnCNew.Game.Movies.MovieDirector.For(data, "English", null, null);
        shell.Movies = movies;

        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonChallenge");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonEasy");
        TestHarness.Assert(shell.CurrentScreen is ChallengeSelectScreen, "the challenge menu opens");
        var tree = shell.CurrentScreen.Tree;
        var open = Enumerable.Range(0, 12).Count(index => tree.FindByName($"ChallengeMenu.wnd:GeneralPosition{index}") is { IsHidden: false });
        TestHarness.Assert(open == 9, $"nine generals are open from the start, none waits for another ({open})");

        // The pointer over a general previews him; the bio of a shell without animations stands at once.
        var pos2 = tree.FindByName("ChallengeMenu.wnd:GeneralPosition2")!;
        var name = tree.FindByName("ChallengeMenu.wnd:BioNameEntry")!;
        shell.DrainOutputs();
        shell.HandleInput(new MouseMoveEvent(100_000, new Vector2(pos2.Rect.CenterX, pos2.Rect.CenterY)));
        var nukeName = name.Text!.Value;
        TestHarness.Assert(nukeName.Length > 0 && !nukeName.StartsWith("GUI:", StringComparison.Ordinal) && tree.FindByName("ChallengeMenu.wnd:BioPortrait")!.EnabledImage0 is { Length: > 0 }, "the general under the pointer shows his name and portrait");
        TestHarness.Assert(shell.DrainOutputs().OfType<PlayUiSoundOutput>().Any(sound => sound.EventName == "GUILogoMouseOver"), "the pointer over a general has its sound");
        ShellTestInput.Click(shell, "ChallengeMenu.wnd:GeneralPosition0");
        var chosen = name.Text!.Value;
        TestHarness.Assert(chosen != nukeName, "the click chose the general under it");
        TestHarness.Assert(shell.DrainOutputs().OfType<PlayUiSoundOutput>().Any(sound => sound.EventName == shell.Campaigns!.Personas[0].PreviewSound), "the choice plays his preview line");
        shell.HandleInput(new MouseMoveEvent(100_100, new Vector2(pos2.Rect.CenterX, pos2.Rect.CenterY)));
        TestHarness.Assert(name.Text!.Value == nukeName, "another general under the pointer previews him");
        shell.HandleInput(new MouseMoveEvent(100_200, new Vector2(5, 5)));
        TestHarness.Assert(name.Text!.Value == chosen, "and leaving him shows the chosen one again");
        for (var update = 0; update < 12; update++) shell.Update(1.0 / 30);
        TestHarness.Assert(shell.DrainOutputs().OfType<PlayUiSoundOutput>().Any(sound => sound.EventName == "Taunts_GCAnnouncer01"), "\"Choose your general\" is said ten updates after the screen opened");

        ShellTestInput.Click(shell, "ChallengeMenu.wnd:ButtonPlay");
        TestHarness.Assert(shell.Phase == ShellPhase.MissionBriefing && shell.CurrentScreen is ChallengeLoadScreen, "the challenge's load screen comes up");
        var player = shell.Campaigns!.Personas[0];
        var load = shell.CurrentScreen.Tree;
        TestHarness.Assert(load.FindByName("ChallengeLoadScreen.wnd:ParentChallengeLoadScreen")!.Video is not null, "the background movie plays in the root window");
        TestHarness.Assert(load.FindByName("ChallengeLoadScreen.wnd:PortraitMovieLeft")!.IsHidden && load.FindByName("ChallengeLoadScreen.wnd:OverlayVs")!.IsHidden, "at the start nothing else stands on it");
        var sounds = new List<string>();
        var shownPortrait = false;
        var shownVs = false;
        for (var step = 0; step < 3000 && shell.Phase == ShellPhase.MissionBriefing; step++)
        {
            Frame(shell, movies);
            sounds.AddRange(shell.DrainOutputs().OfType<PlayUiSoundOutput>().Select(sound => sound.EventName));
            if (shell.Phase != ShellPhase.MissionBriefing) break;
            shownPortrait |= load.FindByName("ChallengeLoadScreen.wnd:PortraitMovieLeft") is { IsHidden: false, Video: not null } && load.FindByName("ChallengeLoadScreen.wnd:PortraitMovieRight")!.Video is not null;
            shownVs |= load.FindByName("ChallengeLoadScreen.wnd:OverlayVs") is { IsHidden: false, Video: not null };
        }
        TestHarness.Assert(shell.Phase == ShellPhase.InMission, "the mission starts when the movie has ended");
        TestHarness.Assert(shownPortrait && shownVs, $"the portraits and the VS played (portraits {shownPortrait}, vs {shownVs})");
        var opponent = shell.Campaigns.PersonaNamed(shell.Campaigns.Find(player.Campaign)!.Missions[0].GeneralName)!;
        TestHarness.Assert(sounds.IndexOf(player.NameSound) >= 0 && sounds.IndexOf(player.NameSound) < sounds.IndexOf("Taunts_GCAnnouncer12") && sounds.IndexOf("Taunts_GCAnnouncer12") < sounds.IndexOf(opponent.NameSound),
            $"the player's name, the VS and the opponent's name are called in this order: {string.Join(", ", sounds)}");
        TestHarness.Assert(sounds.Count(opponent.Taunts.Contains) == 1 && sounds.IndexOf(opponent.NameSound) < sounds.FindIndex(opponent.Taunts.Contains), "the opponent taunts once, last");
        TestHarness.Assert(movies.Failed.Count == 0, "every movie of the screen played: " + string.Join(", ", movies.Failed));

        // A machine without movies: everything at once, with the large portraits.
        var bare = GameShell.Create(data, new FixedShellEnvironment(settings), viewport: new UiViewport(800, 600), skin: SkinPins.Current);
        ShellTestInput.Click(bare, "MainMenu.wnd:ButtonSinglePlayer");
        ShellTestInput.Click(bare, "MainMenu.wnd:ButtonChallenge");
        ShellTestInput.Click(bare, "MainMenu.wnd:ButtonEasy");
        ShellTestInput.Click(bare, "ChallengeMenu.wnd:GeneralPosition0");
        ShellTestInput.Click(bare, "ChallengeMenu.wnd:ButtonPlay");
        var pieces = bare.CurrentScreen.Tree;
        TestHarness.Assert(pieces.FindByName("ChallengeLoadScreen.wnd:PortraitLeft") is { IsHidden: false, EnabledImage0: var large } && large == player.BioPortraitLarge
            && pieces.FindByName("ChallengeLoadScreen.wnd:BioNameEntryRight")!.Text!.Value.Length > 0, "without a movie every piece stands at once");

        // Won and lost: the opponent's picture and words on the challenge's own background, and the campaign's last mission leaves its mark.
        var (context, _) = SyntheticCampaign();
        var catalog = CampaignCatalog.Parse("Campaign CHALLENGE_0\n  IsChallengeCampaign yes\n  FirstMission A\n  Mission A\n    Map Maps\\One\\One.map\n    GeneralName " + opponent.BioName + "\n  END\nEND\n");
        var challenge = catalog.Find("CHALLENGE_0")!;
        var controller = new CampaignController(context, challenge, CampaignProgress.Begin(challenge, ScriptDifficulty.Hard));
        var lost = controller.StartCurrentMission();
        StepUntilOver(lost, 5);
        Destroy(lost, "HeroBarrel");
        StepUntilOver(lost, 200);
        shell.MissionFinished(lost, controller);
        var result = shell.CurrentScreen.Tree;
        TestHarness.Assert(result.FindByName("ScoreScreen.wnd:BigPortrait") is { IsHidden: false, EnabledImage0: var victorious } && victorious == opponent.VictoriousImage, "a loss shows the victorious opponent");
        TestHarness.Assert(result.FindByName("ScoreScreen.wnd:GadgetParent")!.IsHidden && result.FindByName("ScoreScreen.wnd:ParentScoreScreen")!.EnabledImage0 == "GeneralsChallengeWinLoss", "on the challenge's own background, without the score");
        TestHarness.Assert(result.FindByName("ScoreScreen.wnd:ChallengeWinLossText")!.Text!.Value.Length > 0 && result.FindByName("ScoreScreen.wnd:GeneralRemarks") is { IsHidden: false }, "with his words");
        TestHarness.Assert(shell.DrainOutputs().OfType<PlayUiSoundOutput>().Any(sound => sound.EventName == opponent.LossSound), "and his line for a loss");
        TestHarness.Assert(!shell.Honors.IsChallengeComplete(0, ScriptDifficulty.Hard), "a loss is no completed challenge");
        var won = controller.StartCurrentMission();
        StepUntilOver(won, 5);
        Destroy(won, "VillainBarrel");
        StepUntilOver(won, 200);
        shell.MissionFinished(won, controller);
        TestHarness.Assert(shell.CurrentScreen.Tree.FindByName("ScoreScreen.wnd:BigPortrait") is { EnabledImage0: var defeated } && defeated == opponent.DefeatedImage, "a win shows the defeated opponent");
        TestHarness.Assert(shell.Honors.IsChallengeComplete(0, ScriptDifficulty.Hard) && BattleHonors.Load(settings).IsChallengeComplete(0, ScriptDifficulty.Hard), "the last mission won is kept, on its difficulty, in the settings directory");
        Directory.Delete(settings, recursive: true);
    }

    /// <summary><c>populateSkirmishBattleHonors</c>, and <c>populatePlayerInfo</c> at the end of a match. The layout's Reset button is hidden in the game's data, so nobody can press it.</summary>
    private static void SkirmishRecord(string contentDir)
    {
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        var settings = Path.Combine(Path.GetTempPath(), "cncnew-record-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, BattleHonors.FileName), "Wins = 12\nLosses = 3\nWinStreak = 4\nBestWinStreak = 7\nUSACampaign_2 = yes\n");
        var shell = GameShell.Create(data, new FixedShellEnvironment(settings), viewport: new UiViewport(800, 600), skin: SkinPins.Current);
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSkirmish");
        TestHarness.Assert(shell.CurrentScreen is SkirmishSetupScreen, "the setup opens");
        var tree = shell.CurrentScreen.Tree;
        string Value(string name) => tree.FindByName($"SkirmishGameOptionsMenu.wnd:StaticText{name}Value")!.Text!.Value;
        TestHarness.Assert(Value("Wins") == "12" && Value("Losses") == "3" && Value("Streak") == "4" && Value("BestStreak") == "7", "the numbers of the record");
        var images = shell.BuildDrawList().Commands.OfType<UiImageDraw>().ToArray();
        TestHarness.Assert(images.Any(image => image.Image == "USACampaign_G" && image.Tint == CnCNew.Ui.Wnd.WndColor.White), "a medal earned stands in its colours");
        TestHarness.Assert(images.Any(image => image.Image == "ChinaCampaign_B" && image.Tint.R == 80), "one not earned is grey");
        var usa = images.First(image => image.Image == "USACampaign_G").Destination;
        var box = tree.FindByName("SkirmishGameOptionsMenu.wnd:ListboxInfo")!.Rect;
        TestHarness.Assert(box.Contains(usa.X, usa.Y) && box.Contains(usa.Right - 1, usa.Bottom - 1), "inside the list");

        ShellTestInput.Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonStart");
        TestHarness.Assert(shell.Phase == ShellPhase.MatchLoading, "the match starts");
        var human = new CnCNew.Simulation.World.PlayerConfiguration(new CnCNew.Simulation.Ids.PlayerId(1), "Human", "FactionChina");
        var computer = new CnCNew.Simulation.World.PlayerConfiguration(new CnCNew.Simulation.Ids.PlayerId(2), "Computer", "FactionGLA", IsComputer: true);
        shell.MatchFinished(new MatchResult([new MatchResultRow(human, false, false, null), new MatchResultRow(computer, false, false, null)], 600));
        TestHarness.Assert(shell.Honors.Wins == 12 && shell.Honors.Losses == 3, "who leaves a match that goes on neither won nor lost");

        var again = GameShell.Create(data, new FixedShellEnvironment(settings), viewport: new UiViewport(800, 600), skin: SkinPins.Current);
        ShellTestInput.Click(again, "MainMenu.wnd:ButtonSinglePlayer");
        ShellTestInput.Click(again, "MainMenu.wnd:ButtonSkirmish");
        ShellTestInput.Click(again, "SkirmishGameOptionsMenu.wnd:ButtonStart");
        again.MatchFinished(new MatchResult([new MatchResultRow(human, true, false, null), new MatchResultRow(computer, false, true, null)], 600));
        TestHarness.Assert(again.Honors is { Wins: 13, WinStreak: 5, BestWinStreak: 7, LastHouse: "China" } && again.Honors.HasHonor(BattleHonor.Streak) && again.Honors.HasHonor(BattleHonor.Blitz5), "a win counts, with the honors of its length and streak");
        TestHarness.Assert(BattleHonors.Load(settings).Wins == 13, "and is written at once");
        Directory.Delete(settings, recursive: true);
    }

    private static void Destroy(MissionRun run, string name)
    {
        var id = run.Session.Engine.NamedObjects.First(item => item.Key == name).Value;
        run.Session.World.Kill(new CnCNew.Simulation.Ids.EntityId(id));
    }

    /// <summary>Steps the mission for at most <paramref name="maxFrames"/> original frames (1/30 s of game time each).</summary>
    private static void StepUntilOver(MissionRun run, int maxFrames)
    {
        var maxTicks = CnCNew.Tests.TestTime.Frames(run.Session.World, maxFrames);
        for (var i = 0; i < maxTicks && !run.Session.IsOver; i++) run.Session.Step();
    }

    private static void MissionConclusionScreens(string[] args)
    {
        var contentDir = ContentDir(args);
        if (contentDir is null) { Console.WriteLine("  SKIP: needs real content for the result screen's WND layouts (pass --content-dir <dir>)"); return; }
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight), skin: SkinPins.Current);

        var (context, campaign) = SyntheticCampaign();
        var controller = new CampaignController(context, campaign, CampaignProgress.Begin(campaign, ScriptDifficulty.Normal));
        var run = controller.StartCurrentMission();
        StepUntilOver(run, 5);
        Destroy(run, "HeroBarrel");
        StepUntilOver(run, 200);
        TestHarness.Assert(run.Session.Outcome == MissionOutcome.Defeat, "the synthetic mission lost as scripted");

        shell.MissionFinished(run, controller);
        TestHarness.Assert(shell.Phase == ShellPhase.MissionResults, "a finished mission shows the mission result phase");
        TestHarness.Assert(shell.CurrentScreen is MissionResultScreen, "the result screen is on top of the banner");
        var continueButton = shell.CurrentScreen.Tree.FindByName("ScoreScreen.wnd:ButtonContinue")!;
        TestHarness.Assert(continueButton.IsEnabled && continueButton.Text!.Value == "Retry Mission", "defeat offers to retry the same mission");

        ShellTestInput.Click(shell, "ScoreScreen.wnd:ButtonContinue");
        TestHarness.Assert(shell.Phase == ShellPhase.MissionBriefing, "retry starts the mission again and shows its briefing");
        TestHarness.Assert(controller.Progress.AttemptsOf("A") == 2, "the retry counted a new attempt");

        for (var i = 0; i < 400 && shell.Phase == ShellPhase.MissionBriefing; i++) shell.Update(0.1);
        var retryRun = shell.DrainOutputs().OfType<StartMissionOutput>().Single().Run;
        StepUntilOver(retryRun, 5);
        Destroy(retryRun, "VillainBarrel");
        StepUntilOver(retryRun, 200);
        TestHarness.Assert(retryRun.Session.Outcome == MissionOutcome.Victory, "the retry won as scripted");

        shell.MissionFinished(retryRun, controller);
        var nextButton = shell.CurrentScreen.Tree.FindByName("ScoreScreen.wnd:ButtonContinue")!;
        TestHarness.Assert(nextButton.IsEnabled && nextButton.Text!.Value == "Next Mission", "victory with a mission left offers to continue");
        ShellTestInput.Click(shell, "ScoreScreen.wnd:ButtonContinue");
        TestHarness.Assert(shell.Phase == ShellPhase.MissionBriefing && controller.Progress.CurrentMission == "B", "continuing starts the next mission");

        for (var i = 0; i < 400 && shell.Phase == ShellPhase.MissionBriefing; i++) shell.Update(0.1);
        var finalRun = shell.DrainOutputs().OfType<StartMissionOutput>().Single().Run;
        StepUntilOver(finalRun, 5);
        Destroy(finalRun, "VillainBarrel");
        StepUntilOver(finalRun, 200);
        shell.MissionFinished(finalRun, controller);
        TestHarness.Assert(controller.IsFinished, "the campaign is finished after its last mission");
        var finishedButton = shell.CurrentScreen.Tree.FindByName("ScoreScreen.wnd:ButtonContinue")!;
        TestHarness.Assert(!finishedButton.IsEnabled, "a finished campaign has nothing left to continue to");
        ShellTestInput.Click(shell, "ScoreScreen.wnd:ButtonOk");
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "Main Menu returns to the main menu");
    }

    /// <summary>
    /// Save/load through <see cref="GameShell"/> needs a mission whose definition fingerprint matches
    /// <see cref="GameShell"/>'s own <c>MissionContext</c> (built from the same <see cref="GameData"/>), which a synthetic
    /// fixture's templates never do - so, unlike <see cref="MissionConclusionScreens"/> (which hands the shell an
    /// already-built <see cref="MissionRun"/> and never asks it to reconstruct one from a save), this test runs a real
    /// campaign mission. It does not force a win/loss (that is <see cref="MissionConclusionScreens"/>'s and
    /// <see cref="CampaignTests.SaveLoadContinues"/>'s job); it only proves the shell's own load path
    /// (<c>Main Menu -> Load/Replay -> Load Game -> select -> confirm</c>) reproduces the exact saved tick/state and then
    /// continues identically to the original.
    /// </summary>
    private static void SaveLoadThroughShellContinuesIdentically(string[] args)
    {
        var contentDir = ContentDir(args);
        if (contentDir is null) { Console.WriteLine("  SKIP: needs real content (pass --content-dir <dir>)"); return; }
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight), skin: SkinPins.Current);
        var usa = shell.Campaigns!.Find("USA") ?? throw new InvalidOperationException("USA campaign missing from real content");

        var context = MissionContext.From(data);
        var controller = new CampaignController(context, usa, CampaignProgress.Begin(usa, ScriptDifficulty.Normal));
        var run = controller.StartCurrentMission();
        StepUntilOver(run, 40);
        var tickAtSave = run.Session.World.Tick;
        var bytes = SaveGame.Write(run, controller.Progress, "mid-mission save");
        shell.Saves!.Save("midgame", bytes);

        // Main menu -> Load/Replay -> Load Game -> select the slot -> confirm (the same path a player uses).
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonLoadReplay");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonLoadGame");
        TestHarness.Assert(shell.CurrentScreen is LoadGameScreen, "Load Game opens the save list");
        var list = shell.CurrentScreen.Tree.FindByName("SaveLoad.wnd:ListboxGames")!;
        ShellTestInput.ClickAt(shell, list.Rect.X + 20, list.Rect.Y + 8);
        ShellTestInput.Click(shell, "SaveLoad.wnd:ButtonLoad");
        ShellTestInput.Click(shell, "SaveLoad.wnd:ButtonLoadConfirm");

        TestHarness.Assert(shell.Phase == ShellPhase.InMission, "loading a save goes straight to InMission (no briefing: this is a resume, not a new start)");
        var loaded = shell.DrainOutputs().OfType<StartMissionOutput>().Single();
        TestHarness.Assert(loaded.Run.Session.World.Tick == tickAtSave, "the loaded mission resumes at the saved tick");
        TestHarness.Assert(loaded.Run.Session.StateHash() == run.Session.StateHash(), "the loaded mission has the saved state hash");
        TestHarness.Assert(loaded.Controller is not null && loaded.Controller!.Progress.Difficulty == ScriptDifficulty.Normal && loaded.Controller.Progress.AttemptsOf(run.Mission!.Name) == 1,
            "the loaded controller carries the saved campaign progress");

        ulong chainOriginal = StateHash.Seed, chainLoaded = StateHash.Seed;
        for (var tick = 0; tick < CnCNew.Tests.TestTime.Frames(run.Session.World, 120) && !run.Session.IsOver && !loaded.Run.Session.IsOver; tick++)
        {
            chainOriginal = StateHash.Mix(chainOriginal, run.Session.Step().StateHash);
            chainLoaded = StateHash.Mix(chainLoaded, loaded.Run.Session.Step().StateHash);
        }
        TestHarness.Assert(chainOriginal == chainLoaded, "the mission loaded through the shell continues byte-for-byte identically to the original");
    }

    /// <summary>
    /// Phase 24: a running skirmish - a human slot and a computer player - is saved, listed by the store as a skirmish, loaded through the main menu's load list and continued.
    /// The continued match has the session hash (world and AI) of the uninterrupted one in every tick, and an order of the human is taken by both alike. A save of another tick
    /// rate, of other definitions or with a changed byte is refused.
    /// </summary>
    private static void SkirmishSaveThroughShellContinuesIdentically(string[] args)
    {
        var contentDir = ContentDir(args);
        if (contentDir is null) { Console.WriteLine("  SKIP: needs real content (pass --content-dir <dir>)"); return; }
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight), skin: SkinPins.Current);
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var options = SkirmishMatchOptions.StandardAt(data.Rate);
        var setup = new CnCNew.Game.Live.MatchSetup(mapPath, "FactionAmerica", [CnCNew.Game.Live.MatchSetup.RandomFaction], options)
        {
            Details = [new CnCNew.Game.Live.MatchSlotDetails(Team: 0, Color: 2, StartPosition: 1), new CnCNew.Game.Live.MatchSlotDetails(Team: 0, Color: -1, StartPosition: 0)],
        };
        var viewport = new Vector2(1280, 720);
        var live = CnCNew.Game.Live.LiveMatch.Create(data, setup, viewport);
        TestHarness.Assert(live.Match.Slots[0].StartPosition == 1 && live.Match.Slots[1].StartPosition == 0 && live.Match.Slots[0].Color == 2, "the setup's start positions and colours reach the match description");
        TestHarness.Assert(live.Match.Slots[1].Participant.Faction.Name.Length > 0 && live.Players[1].Faction == live.Match.Slots[1].Participant.Faction.Name, "a random faction is decided by the description, not by the screen");
        var frame = CnCNew.Tests.TestTime.FrameSeconds;
        for (var index = 0; index < 30 * 45; index++) live.Advance(frame);
        var tickAtSave = live.Tick;
        var bytes = live.Save("mid-match save");
        shell.Saves!.Save("skirmish", bytes);
        var slot = shell.Saves.List().Single();
        TestHarness.Assert(slot.Header is { Kind: SaveGameKind.Skirmish } header && header.Tick == tickAtSave && header.MapPath == mapPath && header.Title == "mid-match save",
            "the store lists the skirmish save with its kind, tick and map");

        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonLoadReplay");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonLoadGame");
        var list = shell.CurrentScreen.Tree.FindByName("SaveLoad.wnd:ListboxGames")!;
        ShellTestInput.ClickAt(shell, list.Rect.X + 20, list.Rect.Y + 8);
        ShellTestInput.Click(shell, "SaveLoad.wnd:ButtonLoad");
        ShellTestInput.Click(shell, "SaveLoad.wnd:ButtonLoadConfirm");
        TestHarness.Assert(shell.Phase == ShellPhase.InMatch, "loading a skirmish save goes straight to InMatch");
        var resume = shell.DrainOutputs().OfType<ResumeMatchOutput>().Single();
        var loaded = CnCNew.Game.Live.LiveMatch.Restore(data, resume.Save, viewport);
        TestHarness.Assert(loaded.Tick == tickAtSave && loaded.Session.StateHash() == live.Session.StateHash(), "the loaded skirmish resumes at the saved tick with the saved session hash");
        TestHarness.Assert(loaded.HumanFaction == live.HumanFaction && loaded.Players.SequenceEqual(live.Players) && loaded.Match.Slots[0].StartPosition == 1 && !loaded.Setup.AutoPlay,
            "the loaded skirmish has the players, the start positions and the viewer of the saved one");

        // An order of the human in both, then ten seconds of play: the continuation takes orders and its computer player goes on deciding like the uninterrupted one.
        foreach (var match in new[] { live, loaded })
        {
            var units = match.World.Entities.Where(entity => entity.Owner == match.Human && !entity.Template.IsKindOf("STRUCTURE") && !entity.EffectivelyDead).Select(entity => entity.Id).Take(3).ToArray();
            TestHarness.Assert(units.Length > 0, "the human has a unit to order");
            var home = match.Terrain.StartPositions[1];
            match.Commands.Issue((sequence, tick) => new CnCNew.Simulation.Commands.GroupMoveCommand(match.Human, sequence, tick, [..units], home.X + CnCNew.Simulation.Numerics.Fixed.FromInt(120), home.Y, false));
        }
        var aiCommandsBefore = loaded.Session.Ais[1]!.Stats.Commands;
        for (var index = 0; index < 30 * 20; index++)
        {
            live.Advance(frame);
            loaded.Advance(frame);
            if (live.Tick != loaded.Tick || live.Session.StateHash() != loaded.Session.StateHash())
                throw new InvalidOperationException($"the loaded skirmish differs from the uninterrupted one at tick {live.Tick} (loaded: {loaded.Tick})");
        }
        TestHarness.Assert(loaded.Session.Ais[1]!.Stats.Commands > aiCommandsBefore, "the computer player of the loaded skirmish goes on giving orders");
        TestHarness.Assert(CnCNew.Simulation.Replay.ReplayFormat.Write(loaded.Session.ToReplay()).AsSpan().SequenceEqual(CnCNew.Simulation.Replay.ReplayFormat.Write(live.Session.ToReplay())),
            "the loaded skirmish yields the whole replay of the match");

        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var tampered = System.Text.Encoding.UTF8.GetBytes(text.Replace("\"endTick\":", "\"endTick\":1", StringComparison.Ordinal));
        TestHarness.Throws<SaveGameException>(() => CnCNew.Game.Live.LiveMatch.Restore(data, tampered, viewport), "a skirmish save with a changed byte is refused");
        var otherRate = GameData.Load(contentDir, rate: data.Rate.TicksPerSecond == 30 ? SimulationRate.Hz60 : SimulationRate.Hz30);
        TestHarness.Throws<SaveGameException>(() => CnCNew.Game.Live.LiveMatch.Restore(otherRate, bytes, viewport), "a skirmish save of another tick rate is refused");
        TestHarness.Throws<SaveGameException>(() => SaveGame.ReadHeader(bytes), "a skirmish save is not taken for a mission save");
    }
}

/// <summary>Shared click-through input helpers (same timestamps-advance-monotonically pattern as <see cref="ShellTests"/>'s
/// private ones, pulled out so this file does not depend on that file's internals).</summary>
internal static class ShellTestInput
{
    private static long _clockMs;

    public static void Click(GameShell shell, string decoratedName)
    {
        var window = shell.CurrentScreen.Tree.FindByName(decoratedName) ?? throw new InvalidOperationException($"Window '{decoratedName}' not found on {shell.CurrentScreen.GetType().Name}.");
        ClickAt(shell, window.Rect.CenterX, window.Rect.CenterY);
    }

    public static void ClickAt(GameShell shell, int x, int y)
    {
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(x, y)));
        shell.HandleInput(new MouseButtonEvent(++_clockMs, MouseButton.Left, true, new Vector2(x, y)));
        shell.HandleInput(new MouseButtonEvent(++_clockMs, MouseButton.Left, false, new Vector2(x, y)));
    }
}

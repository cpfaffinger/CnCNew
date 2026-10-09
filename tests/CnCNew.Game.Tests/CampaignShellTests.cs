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

        var contentDir = ContentDir(args);
        if (contentDir is null) Console.WriteLine("SKIP CampaignShellTests real-campaign flow (pass --content-dir <dir>)");
        else harness.Run("Main menu -> USA -> difficulty reaches a real mission with its real briefing text", () => RealCampaignFlow(contentDir));
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
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
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
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));

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
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
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

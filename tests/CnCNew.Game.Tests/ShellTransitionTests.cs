using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Tests;

/// <summary>
/// The shell with window transitions (<c>GameWindowTransitions</c> port) against the shipped layouts: the main menu flashes in, submenus and
/// screens are entered and left through their groups, input on what is still hidden is not delivered, the navigation buttons are refused while a
/// transition runs, and every sequence ends in exactly the state the transition-free shell shows (the state the golden hashes pin).
/// </summary>
internal static class ShellTransitionTests
{
    private const int Width = 1280, Height = 720;
    private const double Frame = 1.0 / WindowFrames;
    private const int WindowFrames = 30;
    private static long _clockMs;
    private static readonly SortedSet<string> UsedGroups = new(StringComparer.Ordinal);
    private static readonly SortedSet<string> MissingWindows = new(StringComparer.Ordinal);

    private static void Note(GameShell shell)
    {
        foreach (var screen in shell.Stack)
        {
            if (screen.Transitions is not { } transitions) continue;
            foreach (var name in transitions.RequestedGroups) UsedGroups.Add(name);
            foreach (var name in transitions.MissingWindows) MissingWindows.Add(name);
        }
    }

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ShellTransitionTests));
        var contentDir = ContentDir(args);
        if (contentDir is null)
        {
            Console.WriteLine("SKIP ShellTransitionTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(contentDir);
        harness.Run("The main menu fades in from black, flashes its logo and buttons in, and ends in the transition-free state", () => MainMenuEntry(data));
        harness.Run("Hidden buttons take no input and navigation is refused while a transition runs", () => InputDuringTransitions(data));
        harness.Run("Submenus switch through their groups and end in the transition-free state", () => Submenus(data));
        harness.Run("A hovered faction button shows its logo and takes it away again", () => FactionLogos(data));
        harness.Run("Opening and closing a screen flashes the menu out, fades the screen in and replays the entry on return", () => ScreenRoundTrip(data));
        harness.Run("Every campaign button flashes the menu out and opens its screen; closing it replays the entry", () => CampaignButtons(data));
        harness.Run("The skirmish score screen counts its numbers up and ends on the real values", () => ScoreScreenCountsUp(data));
        harness.Run("Two runs of the same frames draw byte-identical sequences", () => Deterministic(data));
        harness.Run("Without transitions nothing animates and no group is ever created", () => NoTransitionsNoAnimation(data));
        harness.Run("Every group the shell asks for exists and every window of it is in the screen", () => GroupsAreKnown(data));
        return harness.Failures;
    }

    // ------------------------------------------------------------------------------------------------- helpers

    private static IShellEnvironment NewEnvironment() => new FixedShellEnvironment(Path.Combine(Path.GetTempPath(), "cncnew-shell-transition-tests-" + Guid.NewGuid().ToString("N")));

    private static GameShell NewShell(GameData data, bool animate) => GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(Width, Height), animateTransitions: animate);

    private static int Settle(GameShell shell, int limit = 600)
    {
        for (var frame = 0; frame < limit; frame++)
        {
            if (!shell.IsAnimating) { Note(shell); return frame; }
            shell.Update(Frame);
        }
        throw new InvalidOperationException($"The shell was still animating after {limit} frames ({shell.CurrentScreen.GetType().Name}: {shell.CurrentScreen.Transitions?.CurrentGroup}).");
    }

    private static void Step(GameShell shell, int frames)
    {
        for (var frame = 0; frame < frames; frame++) shell.Update(Frame);
    }

    private static string Dump(GameShell shell) => shell.BuildDrawList().Dump();

    /// <summary>The first lines that differ between two draw list dumps, for the message of a failed comparison.</summary>
    private static string Diff(string expected, string actual)
    {
        var a = expected.Split((char)10);
        var b = actual.Split((char)10);
        var onlyExpected = a.Except(b).Take(3);
        var onlyActual = b.Except(a).Take(3);
        return $" (only expected: [{string.Join(" | ", onlyExpected)}]; only actual: [{string.Join(" | ", onlyActual)}])";
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    private static UiWindow Window(GameShell shell, string name) => shell.CurrentScreen.Tree.FindByName(name) ?? throw new InvalidOperationException($"Window '{name}' not found.");

    private static void Click(GameShell shell, string name)
    {
        var window = Window(shell, name);
        ClickAt(shell, window.Rect.CenterX, window.Rect.CenterY);
    }

    private static void ClickAt(GameShell shell, int x, int y)
    {
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(x, y)));
        shell.HandleInput(new MouseButtonEvent(++_clockMs, MouseButton.Left, true, new Vector2(x, y)));
        shell.HandleInput(new MouseButtonEvent(++_clockMs, MouseButton.Left, false, new Vector2(x, y)));
    }

    private static void MoveTo(GameShell shell, string name)
    {
        var window = Window(shell, name);
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(window.Rect.CenterX, window.Rect.CenterY)));
    }

    private static void MoveAway(GameShell shell) => shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(2, 2)));

    private static string? ContentDir(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string Visible(GameShell shell, params string[] names) => string.Join(",", names.Select(name => Window(shell, name).IsHidden ? "-" : "+"));

    // ------------------------------------------------------------------------------------------------- tests

    private static void MainMenuEntry(GameData data)
    {
        var plain = NewShell(data, animate: false);
        var restingMain = Dump(plain);

        var shell = NewShell(data, animate: true);
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "starts on the main menu");
        TestHarness.Assert(shell.CurrentScreen.Transitions is not null, "the animated shell gives the menu a transition handler");
        TestHarness.Assert(shell.IsAnimating, "the entry is a running transition");
        TestHarness.Assert(shell.CurrentScreen.Transitions!.CurrentGroup == "FadeWholeScreen" && shell.CurrentScreen.Transitions.PendingGroup == "MainMenuDefaultMenuLogoFade",
            "the first menu of a run fades in from black and then flashes in");

        // Frame 0: everything the logo group controls is hidden and the screen is black.
        var names = new[] { "MainMenu.wnd:Logo", "MainMenu.wnd:MapBorder2", "MainMenu.wnd:ButtonSinglePlayer", "MainMenu.wnd:ButtonExit" };
        TestHarness.Assert(Visible(shell, names) == "-,-,-,-", $"logo, border and buttons are hidden at the start: {Visible(shell, names)}");
        var first = shell.BuildDrawList();
        TestHarness.Assert(first.Commands.OfType<UiFillRect>().Any(fill => fill.Rect == new UiRect(0, 0, Width, Height) && fill.Color.A >= 250),
            "the first drawn frame is covered by the black fade");

        var sounds = new List<string>();
        var firstVisible = new Dictionary<string, int>();
        var frames = 0;
        while (shell.IsAnimating && frames < 400)
        {
            shell.Update(Frame);
            shell.BuildDrawList(); // effects note a missing image when they draw
            frames++;
            sounds.AddRange(shell.DrainOutputs().OfType<PlayUiSoundOutput>().Select(output => output.EventName));
            foreach (var name in names)
                if (!firstVisible.ContainsKey(name) && !Window(shell, name).IsHidden) firstVisible[name] = frames;
        }
        TestHarness.Assert(frames < 400, "the entry ends");
        TestHarness.Assert(frames is > 45 and < 120, $"fade-in (29 frames) plus the logo group (27 frames) take about {frames} frames");
        TestHarness.Assert(firstVisible["MainMenu.wnd:ButtonSinglePlayer"] < firstVisible["MainMenu.wnd:ButtonExit"],
            "the buttons come in one after the other: single player before exit");
        TestHarness.Assert(shell.CurrentScreen.Transitions!.MissingWindows.Count == 0, $"no window of the groups is missing: {string.Join(",", shell.CurrentScreen.Transitions.MissingWindows)}");
        TestHarness.Assert(string.Join(",", shell.CurrentScreen.Transitions.MissingImages) == "Gradient",
            $"the only image the flash groups ask for and the retail data lacks is the button gradient (the original draws nothing for it, too): [{string.Join(",", shell.CurrentScreen.Transitions.MissingImages)}]");
        TestHarness.Assert(sounds.Contains("GUIBoarderFadeIn") && sounds.Contains("GUIButtonsFadeIn"), $"the entry plays its sounds ({string.Join(",", sounds.Distinct())})");
        TestHarness.Assert(Dump(shell) == restingMain, "after the entry the menu draws exactly what the transition-free menu draws");
        Console.WriteLine($"  main menu entry: {frames} frames ({frames * CnCNew.Tests.TestTime.FrameSeconds:F2} s); first visible frames: " + string.Join(", ", firstVisible.OrderBy(pair => pair.Value).Select(pair => $"{pair.Key[(pair.Key.IndexOf(':') + 1)..]} {pair.Value}")));
    }

    private static void InputDuringTransitions(GameData data)
    {
        var shell = NewShell(data, animate: true);
        Step(shell, 3);
        // Still black and hidden: a click where the single player button will be does nothing.
        var rect = Window(shell, "MainMenu.wnd:ButtonSinglePlayer").Rect;
        ClickAt(shell, rect.CenterX, rect.CenterY);
        var main = (MainMenuScreen)shell.CurrentScreen;
        TestHarness.Assert(Window(shell, "MainMenu.wnd:MapBorder2").IsHidden || !Window(shell, "MainMenu.wnd:ButtonSinglePlayer").IsSelected, "a hidden button is not pressed");
        Settle(shell);
        TestHarness.Assert(!Window(shell, "MainMenu.wnd:MapBorder").IsVisible, "the click on the hidden button did not open the single-player submenu");
        TestHarness.Assert(Window(shell, "MainMenu.wnd:MapBorder2").IsVisible, "the root menu is still shown");

        // A navigation click starts a transition; the next ones are refused until it has finished (dontAllowTransitions).
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        TestHarness.Assert(shell.IsAnimating, "single player starts a transition");
        Step(shell, 2);
        // Escape asks to go back at once, a click on Credits to open it: the root's buttons are still flashing out, but both are refused.
        shell.HandleInput(new KeyEvent(++_clockMs, KeyCode.Escape, IsDown: true));
        Click(shell, "MainMenu.wnd:ButtonCredits");
        TestHarness.Assert(shell.CurrentScreen == main, "still on the main menu");
        Settle(shell);
        TestHarness.Assert(shell.CurrentScreen == main, "no screen was opened by the refused click");
        TestHarness.Assert(Window(shell, "MainMenu.wnd:MapBorder").IsVisible && Window(shell, "MainMenu.wnd:MapBorder2").IsHidden, "the early Escape was refused: the single-player submenu is shown");
        // Once the transition is over the same inputs work.
        shell.HandleInput(new KeyEvent(++_clockMs, KeyCode.Escape, IsDown: true));
        Settle(shell);
        TestHarness.Assert(Window(shell, "MainMenu.wnd:MapBorder2").IsVisible && Window(shell, "MainMenu.wnd:MapBorder").IsHidden, "Escape after the transition goes back to the root");
    }

    private static void Submenus(GameData data)
    {
        // Final states of the transition-free shell for every submenu.
        var plain = NewShell(data, animate: false);
        var restingMain = Dump(plain);
        Click(plain, "MainMenu.wnd:ButtonSinglePlayer");
        var restingSingle = Dump(plain);
        Click(plain, "MainMenu.wnd:ButtonSingleBack");
        var plainLoad = NewShell(data, animate: false);
        Click(plainLoad, "MainMenu.wnd:ButtonLoadReplay");
        var restingLoad = Dump(plainLoad);

        var shell = NewShell(data, animate: true);
        Settle(shell);
        TestHarness.Assert(Dump(shell) == restingMain, "settled root");
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        // The root flashes out while the single-player group waits behind it.
        TestHarness.Assert(shell.CurrentScreen.Transitions!.CurrentGroup == "MainMenuDefaultMenuBack" && shell.CurrentScreen.Transitions.CurrentIsReversed, "the root's Back group plays reversed first");
        TestHarness.Assert(shell.CurrentScreen.Transitions.PendingGroup == "MainMenuSinglePlayerMenu", "single player waits behind it");
        var mid = shell.BuildDrawList();
        TestHarness.Assert(mid.Commands.OfType<UiBorderRect>().Any() || mid.Commands.OfType<UiFillRect>().Any(), "a stand-in is drawn while the menu changes");
        var frames = Settle(shell);
        TestHarness.Assert(frames is > 15 and < 90, $"the submenu change takes {frames} frames");
        TestHarness.Assert(Dump(shell) == restingSingle, "settled single-player submenu equals the transition-free one");
        Settle(shell);
        Click(shell, "MainMenu.wnd:ButtonSingleBack");
        Settle(shell);
        TestHarness.Assert(Dump(shell) == restingMain, "back on the root, settled, equals the transition-free root");

        // Escape goes back from a submenu as well.
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        Settle(shell);
        shell.HandleInput(new KeyEvent(++_clockMs, KeyCode.Escape, IsDown: true));
        Settle(shell);
        TestHarness.Assert(Dump(shell) == restingMain, "Escape from the submenu returns to the root");

        var loadShell = NewShell(data, animate: true);
        Settle(loadShell);
        Click(loadShell, "MainMenu.wnd:ButtonLoadReplay");
        Settle(loadShell);
        TestHarness.Assert(Dump(loadShell) == restingLoad, "settled load/replay submenu equals the transition-free one");
        TestHarness.Assert(loadShell.CurrentScreen.Transitions!.MissingWindows.Count == 0, "no missing windows");
        Click(loadShell, "MainMenu.wnd:ButtonLoadReplayBack");
        Settle(loadShell);
        TestHarness.Assert(Dump(loadShell) == restingMain, "Back from the load/replay submenu returns to the root");
        Note(loadShell);
    }

    private static void FactionLogos(GameData data)
    {
        var shell = NewShell(data, animate: true);
        Settle(shell);
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        Settle(shell);
        var transitions = shell.CurrentScreen.Transitions!;
        TestHarness.Assert(Visible(shell, "MainMenu.wnd:WinFactionUS", "MainMenu.wnd:WinFactionUSMedium") == "+,-", "the small logo row is shown at rest");

        MoveTo(shell, "MainMenu.wnd:ButtonUSA");
        TestHarness.Assert(transitions.CurrentGroup == "MainMenuFactionUS" && !transitions.CurrentIsReversed, "hovering USA starts its logo group");
        var drawing = false;
        for (var frame = 0; frame < 6; frame++)
        {
            shell.Update(Frame);
            drawing |= shell.BuildDrawList().Commands.OfType<UiImageDraw>().Any(image => image.Destination.Width > 0 && image.Destination.Intersects(Window(shell, "MainMenu.wnd:WinFactionUSMedium").Rect) && Window(shell, "MainMenu.wnd:WinFactionUS").IsHidden);
        }
        TestHarness.Assert(drawing, "the logo image scales up while the window itself is hidden");
        Settle(shell);
        TestHarness.Assert(Visible(shell, "MainMenu.wnd:WinFactionUS", "MainMenu.wnd:WinFactionUSMedium") == "-,+", "the medium logo is shown while hovered");

        MoveAway(shell);
        TestHarness.Assert(transitions.CurrentIsReversed, "leaving the button reverses the logo group");
        Settle(shell);
        TestHarness.Assert(Visible(shell, "MainMenu.wnd:WinFactionUS", "MainMenu.wnd:WinFactionUSMedium") == "+,-", "the logo is back to its small state");

        // Sliding from USA to GLA: the USA logo plays out, then the GLA logo comes.
        MoveTo(shell, "MainMenu.wnd:ButtonUSA");
        Step(shell, 1);
        MoveTo(shell, "MainMenu.wnd:ButtonGLA");
        Settle(shell);
        TestHarness.Assert(Visible(shell, "MainMenu.wnd:WinFactionGLAMedium", "MainMenu.wnd:WinFactionUSMedium") == "+,-", "only the GLA logo remains after moving from USA to GLA");
    }

    private static void ScreenRoundTrip(GameData data)
    {
        var plain = NewShell(data, animate: false);
        Click(plain, "MainMenu.wnd:ButtonSinglePlayer");
        Click(plain, "MainMenu.wnd:ButtonSkirmish");
        var restingSkirmish = Dump(plain);
        var restingMain = Dump(NewShell(data, animate: false));

        var shell = NewShell(data, animate: true);
        Settle(shell);
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        Settle(shell);
        Click(shell, "MainMenu.wnd:ButtonSkirmish");
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "the skirmish screen waits for the menu to flash out");
        TestHarness.Assert(shell.IsAnimating, "the flash-out runs");
        Step(shell, 1);
        Click(shell, "MainMenu.wnd:ButtonUSA");
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "a second choice during the flash-out is refused");
        var steps = 0;
        while (shell.CurrentScreen is MainMenuScreen && steps++ < 300) shell.Update(Frame);
        TestHarness.Assert(shell.CurrentScreen is SkirmishSetupScreen, "the skirmish setup opens when the flash-out is over");
        TestHarness.Assert(steps is > 8 and < 120, $"the flash-out takes {steps} frames");
        var skirmish = shell.CurrentScreen;
        TestHarness.Assert(skirmish.Transitions!.CurrentGroup == "SkirmishGameOptionsMenuFade", "the setup fades in");
        TestHarness.Assert(Window(shell, "SkirmishGameOptionsMenu.wnd:SubParent").IsHidden, "its content is hidden until the fade reaches its peak");
        Step(shell, 5);
        TestHarness.Assert(!Window(shell, "SkirmishGameOptionsMenu.wnd:SubParent").IsHidden, "and shown at the peak (frame 5)");
        var fade = shell.BuildDrawList().Commands.OfType<UiFillRect>().Where(fill => fill.Rect == Window(shell, "SkirmishGameOptionsMenu.wnd:SubParent").Rect && fill.Color is { R: 0, G: 0, B: 0 }).ToArray();
        TestHarness.Assert(fade.Length >= 1, "the black fade rectangle covers the content");
        Settle(shell);
        var skirmishOnly = shell.CurrentScreen.BuildDrawList().Dump();
        TestHarness.Assert(skirmishOnly == plain.CurrentScreen.BuildDrawList().Dump(), "the settled setup screen equals the transition-free one");

        // Back: reversed fade, then the menu again, playing its entry once more.
        Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonBack");
        TestHarness.Assert(shell.CurrentScreen is SkirmishSetupScreen && shell.CurrentScreen.IsClosing, "closing waits for the fade-out");
        ClickAt(shell, 40, 40);
        var closing = 0;
        while (shell.CurrentScreen is SkirmishSetupScreen && closing++ < 100) shell.Update(Frame);
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "the menu is back");
        TestHarness.Assert(closing is >= 5 and <= 15, $"the fade-out takes about 10 frames ({closing})");
        TestHarness.Assert(shell.CurrentScreen.Transitions!.CurrentGroup == "MainMenuDefaultMenuLogoFade", "the revealed menu replays its entry");
        TestHarness.Assert(Window(shell, "MainMenu.wnd:ButtonSinglePlayer").IsHidden, "its buttons flash in again");
        Settle(shell);
        TestHarness.Assert(Dump(shell) == restingMain, "settled again on the plain root menu (not the single-player submenu)");

        // The load game screen uses the same path.
        Click(shell, "MainMenu.wnd:ButtonLoadReplay");
        Settle(shell);
        Click(shell, "MainMenu.wnd:ButtonLoadGame");
        var load = 0;
        while (shell.CurrentScreen is MainMenuScreen && load++ < 300) shell.Update(Frame);
        TestHarness.Assert(shell.CurrentScreen is LoadGameScreen, "load game opens after the flash-out");
        TestHarness.Assert(shell.CurrentScreen.Transitions!.CurrentGroup == "SaveLoadMenuFade", "and fades in");
        Settle(shell);
        Click(shell, "SaveLoad.wnd:ButtonBack");
        TestHarness.Assert(shell.CurrentScreen.IsClosing, "Back closes with the fade-out");
        Settle(shell);
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "back on the main menu");

        // The quit dialog is no shell push in the original: it opens at once, without any wait.
        Click(shell, "MainMenu.wnd:ButtonExit");
        TestHarness.Assert(shell.CurrentScreen is MessageBoxScreen, "the quit dialog opens immediately");
    }

    private static void CampaignButtons(GameData data)
    {
        // The difficulty question of USA/GLA/China/Challenge is part of MainMenu.wnd (MapBorder4, ButtonEasy/Medium/Hard/DiffBack, MainMenu.cpp), not a screen of its own.
        var plain = NewShell(data, animate: false);
        var restingMain = Dump(plain);
        Click(plain, "MainMenu.wnd:ButtonSinglePlayer");
        var restingSingle = Dump(plain);
        foreach (var button in new[] { "ButtonUSA", "ButtonGLA", "ButtonChina", "ButtonChallenge" })
        {
            Click(plain, $"MainMenu.wnd:{button}");
            var restingDifficulty = Dump(plain);
            TestHarness.Assert(plain.CurrentScreen is MainMenuScreen && plain.Stack.Count == 1, $"{button}: without animation the difficulty question is shown at once, on the main menu");
            TestHarness.Assert(restingDifficulty != restingSingle, $"{button}: the difficulty question looks different from the single-player column");
            Click(plain, "MainMenu.wnd:ButtonDiffBack");
            // The Challenge branch's groups fade the title logo out and never bring it back (see MainMenuScreen.OpenDifficulty), so only the other three restore the column exactly.
            var backFromDifficulty = Dump(plain);
            TestHarness.Assert((backFromDifficulty == restingSingle) == (button != "ButtonChallenge"), $"{button}: Back restores the single-player column (transition-free), the Challenge branch without the logo");

            var shell = NewShell(data, animate: true);
            Settle(shell);
            Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
            Settle(shell);
            Click(shell, $"MainMenu.wnd:{button}");
            TestHarness.Assert(shell.CurrentScreen is MainMenuScreen && shell.IsAnimating, $"{button}: the column gives way with a transition");
            Settle(shell);
            TestHarness.Assert(shell.CurrentScreen is MainMenuScreen && shell.Stack.Count == 1, $"{button}: the difficulty question belongs to the main menu");
            TestHarness.Assert(Dump(shell) == restingDifficulty, $"{button}: the settled difficulty question equals the transition-free one" + Diff(restingDifficulty, Dump(shell)));
            TestHarness.Assert(shell.CurrentScreen.Transitions!.MissingWindows.Count == 0, $"{button}: no missing windows");
            Click(shell, "MainMenu.wnd:ButtonDiffBack");
            Settle(shell);
            TestHarness.Assert(Dump(shell) == backFromDifficulty, $"{button}: Back, settled, equals the transition-free single-player column" + Diff(backFromDifficulty, Dump(shell)));

            // Escape from the question goes back as well.
            Click(shell, $"MainMenu.wnd:{button}");
            Settle(shell);
            shell.HandleInput(new KeyEvent(++_clockMs, KeyCode.Escape, IsDown: true));
            Settle(shell);
            TestHarness.Assert(Dump(shell) == backFromDifficulty, $"{button}: Escape from the difficulty question returns to the single-player column");

            Click(shell, $"MainMenu.wnd:{button}");
            Settle(shell);
            var menu = shell.CurrentScreen.Transitions!;
            Click(shell, "MainMenu.wnd:ButtonEasy");
            TestHarness.Assert(shell.CurrentScreen is MainMenuScreen && shell.IsAnimating, $"{button}: choosing a difficulty plays the exit groups first");
            var fades = menu.CurrentGroup == "FadeWholeScreen" || menu.PendingGroup == "FadeWholeScreen";
            var steps = 0;
            while (shell.CurrentScreen is MainMenuScreen && steps++ < 300) shell.Update(Frame);
            if (button == "ButtonChallenge")
            {
                TestHarness.Assert(shell.CurrentScreen is ChallengeSelectScreen, "Challenge opens the challenge menu after the question");
                TestHarness.Assert(menu.RequestedGroups.Contains("MainMenuDifficultyMenuBack") && !fades, "Challenge reverses the difficulty groups, no fade to black");
                Settle(shell);
                Click(shell, "ChallengeMenu.wnd:GeneralPosition0");
                Click(shell, "ChallengeMenu.wnd:ButtonPlay");
            }
            else
            {
                TestHarness.Assert(fades, $"{button}: the screen fades to black before the mission starts");
            }
            TestHarness.Assert(shell.Phase == ShellPhase.MissionBriefing && shell.CurrentScreen is MissionBriefingScreen, $"{button}: the mission briefing follows");
            for (var i = 0; i < 400 && shell.Phase == ShellPhase.MissionBriefing; i++) shell.Update(0.1);
            var started = shell.DrainOutputs().OfType<StartMissionOutput>().SingleOrDefault();
            TestHarness.Assert(started?.Controller is { } controller && controller.Progress.Difficulty == CnCNew.Simulation.Scripting.ScriptDifficulty.Easy, $"{button}: the chosen difficulty reached the mission");
        }

        // Challenge and back: the menu rests on the root again.
        var round = NewShell(data, animate: true);
        Settle(round);
        Click(round, "MainMenu.wnd:ButtonSinglePlayer");
        Settle(round);
        Click(round, "MainMenu.wnd:ButtonChallenge");
        Settle(round);
        Click(round, "MainMenu.wnd:ButtonMedium");
        var more = 0;
        while (round.CurrentScreen is MainMenuScreen && more++ < 300) round.Update(Frame);
        TestHarness.Assert(round.CurrentScreen is ChallengeSelectScreen, "the challenge menu opened");
        Settle(round);
        Click(round, "ChallengeMenu.wnd:ButtonBack");
        Settle(round);
        TestHarness.Assert(round.CurrentScreen is MainMenuScreen, "back on the main menu");
        Settle(round);
        TestHarness.Assert(Dump(round) == restingMain, "after the challenge round trip the menu rests on the root again" + Diff(restingMain, Dump(round)));
        Note(round);
    }

    private static void ScoreScreenCountsUp(GameData data)
    {
        var setup = new MatchSetup("Maps/Alpine Assault/Alpine Assault.map", "FactionAmerica", ["FactionChina"], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard with { MaxTicks = 600 });
        var live = LiveMatch.Create(data, setup, new Vector2(Width, Height));
        var stats = new MatchStatistics();
        while (!live.Session.IsOver) stats.Record(live.Session.Step());

        var plain = NewShell(data, animate: false);
        plain.MatchFinished(live.Session, stats);
        var resting = plain.BuildDrawList().Dump();

        var shell = NewShell(data, animate: true);
        Settle(shell);
        shell.MatchFinished(live.Session, stats);
        TestHarness.Assert(shell.CurrentScreen is ScoreScreenScreen, "the score screen is shown");
        TestHarness.Assert(shell.CurrentScreen.Transitions!.CurrentGroup == "ScoreScreenShow", "ScoreScreenShow starts with the screen");
        TestHarness.Assert(Window(shell, "ScoreScreen.wnd:StaticTextPlayer0").IsHidden && Window(shell, "ScoreScreen.wnd:StaticTextResources0").IsHidden, "the row cells are hidden before the first frame");
        var textDuring = new List<string>();
        var shown = new List<bool>();
        var frames = 0;
        var sounds = 0;
        while (shell.IsAnimating && frames < 400)
        {
            shell.Update(Frame);
            frames++;
            sounds += shell.DrainOutputs().OfType<PlayUiSoundOutput>().Count(output => output.EventName is "GUIScoreScreenTick" or "GUIScoreScreenPictures");
            textDuring.Add(Window(shell, "ScoreScreen.wnd:StaticTextPlayer0").IsHidden ? "-" : "+");
            shown.Add(Window(shell, "ScoreScreen.wnd:StaticTextResources0").IsHidden);
        }
        TestHarness.Assert(frames is > 40 and < 200, $"the score rows take about {frames} frames (the last row starts at frame 48)");
        TestHarness.Assert(textDuring[0] == "+" && shown[0] && shown[40], "the player name appears on frame 1, the last column only at frame 48");
        TestHarness.Assert(!shown[^1], "and all shown at the end");
        TestHarness.Assert(sounds > 0, "the counters and pictures play their sounds");
        TestHarness.Assert(shell.CurrentScreen.BuildDrawList().Dump() == resting, "the settled score screen equals the transition-free one (final numbers, no fabricated values)");
        TestHarness.Assert(shell.CurrentScreen.Transitions.MissingWindows.Count == 0, $"no missing windows: {string.Join(",", shell.CurrentScreen.Transitions.MissingWindows)}");
        Note(shell);
    }

    private static string Sequence(GameData data)
    {
        _clockMs = 0;
        var shell = NewShell(data, animate: true);
        var hashes = new StringBuilder();
        void Capture() => hashes.Append(Hash(Dump(shell))).Append('\n');
        for (var frame = 0; frame < 20; frame++) { Capture(); shell.Update(Frame); }
        Settle(shell);
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        for (var frame = 0; frame < 40; frame++) { Capture(); shell.Update(Frame); }
        MoveTo(shell, "MainMenu.wnd:ButtonGLA");
        for (var frame = 0; frame < 15; frame++) { Capture(); shell.Update(Frame); }
        return Hash(hashes.ToString());
    }

    private static void Deterministic(GameData data)
    {
        var first = Sequence(data);
        var second = Sequence(data);
        TestHarness.Assert(first == second, $"sequence hashes differ: {first} vs {second}");
        Console.WriteLine($"  sequence hash {first}");
    }

    private static void NoTransitionsNoAnimation(GameData data)
    {
        var shell = NewShell(data, animate: false);
        TestHarness.Assert(shell.TransitionTable is null && shell.CurrentScreen.Transitions is null, "no table, no handler");
        TestHarness.Assert(!shell.IsAnimating, "nothing animates");
        var before = Dump(shell);
        Step(shell, 90);
        TestHarness.Assert(Dump(shell) == before, "time passing changes nothing on the plain menu");
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        TestHarness.Assert(!Window(shell, "MainMenu.wnd:MapBorder").IsHidden && Window(shell, "MainMenu.wnd:MapBorder2").IsHidden, "submenus switch at once");
    }

    private static void GroupsAreKnown(GameData data)
    {
        // The earlier tests walked every screen that has transitions; Settle noted their groups. The table is the shipped one, so an unknown name would have thrown.
        var table = NewShell(data, animate: true).TransitionTable ?? throw new InvalidOperationException("animated shell without table");
        TestHarness.Assert(UsedGroups.Count > 0, "groups were noted");
        TestHarness.Assert(UsedGroups.All(name => table.Find(name) is not null), "every requested group is in WindowTransitions.ini");
        TestHarness.Assert(MissingWindows.Count == 0, $"no window of a requested group is missing from its screen: {string.Join(",", MissingWindows)}");
        Console.WriteLine($"  groups the shell plays: {UsedGroups.Count} of {table.Groups.Length}: {string.Join(", ", UsedGroups)}");
    }
}

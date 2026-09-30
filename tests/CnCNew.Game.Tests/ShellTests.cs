using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// Headless, deterministic tests of the Stride-free game shell (package G1): every shipped menu screen it drives,
/// clicked through exactly like a player would, at several resolutions, with a golden hash of each resulting
/// <see cref="UiDrawList.Dump"/> pinned in <c>baselines/screens.txt</c>. Set the environment variable
/// <c>CNCNEW_UPDATE_BASELINES=1</c> to (re)write that file instead of comparing against it, after a deliberate change.
/// </summary>
internal static class ShellTests
{
    private static readonly (int Width, int Height)[] Resolutions =
    [
        (800, 600), (1024, 768), (1280, 720), (1920, 1080), (2560, 1080), (640, 960), // last one: narrow, portrait-ish
    ];

    private const int NavWidth = 1280, NavHeight = 720;
    private const string BaselinePath = "tests/CnCNew.Game.Tests/baselines/screens.txt";

    public static int Run(string[] args)
    {
        var contentDir = ContentDir(args);
        var harness = new TestHarness(nameof(ShellTests));
        if (contentDir is null)
        {
            Console.WriteLine("SKIP ShellTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }

        var data = GameData.Load(contentDir);
        var baselines = LoadBaselines();
        var updating = System.Environment.GetEnvironmentVariable("CNCNEW_UPDATE_BASELINES") == "1";

        harness.Run("Menu shell walks every screen and draws cleanly", () => WalkScreens(data, baselines, updating));
        harness.Run("Options persist and reload", () => OptionsPersist(data));
        harness.Run("Boot loading screen reaches the main menu", () => BootFlow(contentDir));

        if (updating) SaveBaselines(baselines);
        return harness.Failures;
    }

    // ------------------------------------------------------------------------------------------------- screen walk

    private static void WalkScreens(GameData data, Dictionary<string, string> baselines, bool updating)
    {
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "starts on the main menu");
        Capture(shell, "MainMenu", baselines, updating);

        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        Capture(shell, "MainMenu-SinglePlayer", baselines, updating);

        Click(shell, "MainMenu.wnd:ButtonSkirmish");
        TestHarness.Assert(shell.CurrentScreen is SkirmishSetupScreen, "skirmish button opens setup");
        Capture(shell, "SkirmishSetup", baselines, updating);

        Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonSelectMap");
        TestHarness.Assert(shell.CurrentScreen is SkirmishMapSelectScreen, "select-map button opens the map picker");
        Capture(shell, "SkirmishMapSelect", baselines, updating);

        // Click near the top of the list box to hit its first row, then accept.
        var list = shell.CurrentScreen.Tree.FindByName("SkirmishMapSelectMenu.wnd:ListboxMap")!;
        ClickAt(shell, list.Rect.X + 20, list.Rect.Y + 8);
        Click(shell, "SkirmishMapSelectMenu.wnd:ButtonOK");
        TestHarness.Assert(shell.CurrentScreen is SkirmishSetupScreen, "OK returns to setup");
        Capture(shell, "SkirmishSetup-MapChosen", baselines, updating);
        var mapDisplay = shell.CurrentScreen.Tree.FindByName("SkirmishGameOptionsMenu.wnd:TextEntryMapDisplay")!.Text!.Value;
        TestHarness.Assert(mapDisplay != "(no map selected)", "a map was chosen");

        Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonStart");
        TestHarness.Assert(shell.Phase == ShellPhase.MatchLoading, "Start moves to match loading");
        TestHarness.Assert(shell.CurrentScreen is LoadingScreen, "match loading shows the single-player load screen");
        Capture(shell, "MatchLoading", baselines, updating);

        shell.Update(1.0 / 30);
        var outputs = shell.DrainOutputs();
        var startMatch = outputs.OfType<StartMatchOutput>().SingleOrDefault();
        TestHarness.Assert(startMatch is not null, "exactly one StartMatchOutput after one drawn frame");
        TestHarness.Assert(shell.Phase == ShellPhase.InMatch, "shell hands off to the host");
        TestHarness.Assert(shell.BuildDrawList().Commands.Length == 0, "the shell draws nothing while the host owns the match");

        // Run a short, real skirmish to completion (host stands in for LiveMatch here) and show the results.
        var setup = startMatch!.Setup with { Options = startMatch.Setup.Options with { MaxTicks = 600 } };
        var live = LiveMatch.Create(data, setup, new Vector2(NavWidth, NavHeight));
        var stats = new MatchStatistics();
        while (!live.Session.IsOver) stats.Record(live.Session.Step());
        shell.MatchFinished(live.Session, stats);
        TestHarness.Assert(shell.CurrentScreen is ScoreScreenScreen, "MatchFinished shows the score screen");
        Capture(shell, "ScoreScreen", baselines, updating);

        Click(shell, "ScoreScreen.wnd:ButtonOk");
        TestHarness.Assert(shell.Phase == ShellPhase.Menus && shell.CurrentScreen is MainMenuScreen, "Ok returns to the main menu");

        Click(shell, "MainMenu.wnd:ButtonCredits");
        TestHarness.Assert(shell.CurrentScreen is CreditsScreen, "credits button opens the credits screen");
        Capture(shell, "Credits", baselines, updating);
        Key(shell, KeyCode.Escape);
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "escape leaves the credits screen");

        Click(shell, "MainMenu.wnd:ButtonExit");
        TestHarness.Assert(shell.CurrentScreen is MessageBoxScreen, "exit opens the quit confirmation");
        Capture(shell, "QuitConfirm", baselines, updating);
        Click(shell, "QuitMessageBox.wnd:ButtonNo");
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "No cancels the quit confirmation");
        TestHarness.Assert(shell.DrainOutputs().OfType<ExitRequestedOutput>().Any() == false, "No never emits ExitRequestedOutput");

        Click(shell, "MainMenu.wnd:ButtonExit");
        Click(shell, "QuitMessageBox.wnd:ButtonYes");
        TestHarness.Assert(shell.DrainOutputs().OfType<ExitRequestedOutput>().Any(), "Yes emits ExitRequestedOutput");
    }

    /// <summary>Every registered <c>SYSTEMCALLBACK</c>/<c>INPUTCALLBACK</c> name the shipped screens use must be wired; this is the "zero unregistered" gate.</summary>
    private static void Capture(GameShell shell, string name, Dictionary<string, string> baselines, bool updating)
    {
        foreach (var (width, height) in Resolutions)
        {
            shell.Resize(width, height);
            var list = shell.BuildDrawList();
            TestHarness.Assert(list.Statistics.UnresolvedLabels == 0, $"{name}@{width}x{height}: {list.Statistics.UnresolvedLabels} unresolved label(s)");
            var key = $"{name}@{width}x{height}";
            var hash = Sha256(list.Dump());
            if (updating) baselines[key] = hash;
            else
            {
                TestHarness.Assert(baselines.TryGetValue(key, out var expected), $"{key}: no baseline recorded (run with CNCNEW_UPDATE_BASELINES=1 once, then review the diff)");
                TestHarness.Assert(baselines[key] == hash, $"{key}: draw list hash changed ({baselines.GetValueOrDefault(key)} -> {hash})");
            }
        }
        foreach (var screen in shell.Stack)
            TestHarness.Assert(screen.Tree.UnregisteredCallbacks.Count == 0,
                $"{name}: unregistered callback(s) {string.Join(", ", screen.Tree.UnregisteredCallbacks.Keys)}");
        shell.Resize(NavWidth, NavHeight);
    }

    // ------------------------------------------------------------------------------------------------- options persistence

    private static void OptionsPersist(GameData data)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-shell-tests-" + Guid.NewGuid().ToString("N"));
        var environment = new FixedShellEnvironment(directory);
        var shell = GameShell.Create(data, environment, viewport: new UiViewport(NavWidth, NavHeight));
        Click(shell, "MainMenu.wnd:ButtonOptions");
        TestHarness.Assert(shell.CurrentScreen is OptionsScreen, "options button opens the options screen");

        var tree = shell.CurrentScreen.Tree;
        tree.Post(UiEventKind.ComboSelected, tree.FindByName("OptionsMenu.wnd:ComboBoxResolution")!, value: 1);
        tree.Dispatch();
        var slider = tree.FindByName("OptionsMenu.wnd:SliderMusicVolume")!;
        slider.SetSliderValue(slider.Slider!.Maximum);
        tree.Post(UiEventKind.SliderDone, slider, value: slider.Slider!.Maximum);
        tree.Dispatch();

        Click(shell, "OptionsMenu.wnd:ButtonAccept");
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "accept closes the options screen");
        var applied = shell.Settings;
        TestHarness.Assert(applied.Audio.Music > 0.99f, "the music slider change was applied");

        var reloaded = GameShell.Create(data, environment, viewport: new UiViewport(NavWidth, NavHeight));
        TestHarness.Assert(reloaded.Settings.Display.Width == applied.Display.Width && reloaded.Settings.Display.Height == applied.Display.Height, "resolution persisted across a fresh shell");
        TestHarness.Assert(Math.Abs(reloaded.Settings.Audio.Music - applied.Audio.Music) < 0.01f, "music volume persisted across a fresh shell");

        Directory.Delete(directory, recursive: true);
    }

    // ------------------------------------------------------------------------------------------------- boot flow

    private static void BootFlow(string contentDir)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-shell-tests-boot-" + Guid.NewGuid().ToString("N"));
        var shell = GameShell.CreateLoading(contentDir, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight));
        TestHarness.Assert(shell.Phase == ShellPhase.Boot, "starts in Boot");
        var initialList = shell.BuildDrawList();
        TestHarness.Assert(initialList.Commands.Length > 0, "the boot load screen draws something immediately");

        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (shell.Phase == ShellPhase.Boot && DateTime.UtcNow < deadline)
        {
            shell.Update(0.1);
            Thread.Sleep(20);
        }
        TestHarness.Assert(shell.Phase == ShellPhase.Menus, "boot finishes and reaches the main menu");
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "the main menu is shown after boot");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    // ------------------------------------------------------------------------------------------------- helpers

    private static IShellEnvironment NewEnvironment() => new FixedShellEnvironment(Path.Combine(Path.GetTempPath(), "cncnew-shell-tests-" + Guid.NewGuid().ToString("N")));

    private static void Click(GameShell shell, string decoratedName)
    {
        var window = shell.CurrentScreen.Tree.FindByName(decoratedName) ?? throw new InvalidOperationException($"Window '{decoratedName}' not found on {shell.CurrentScreen.GetType().Name}.");
        ClickAt(shell, window.Rect.CenterX, window.Rect.CenterY);
    }

    private static long _clockMs;

    private static void ClickAt(GameShell shell, int x, int y)
    {
        var t1 = ++_clockMs;
        shell.HandleInput(new MouseMoveEvent(t1, new Vector2(x, y)));
        var t2 = ++_clockMs;
        shell.HandleInput(new MouseButtonEvent(t2, MouseButton.Left, true, new Vector2(x, y)));
        var t3 = ++_clockMs;
        shell.HandleInput(new MouseButtonEvent(t3, MouseButton.Left, false, new Vector2(x, y)));
    }

    private static void Key(GameShell shell, KeyCode key) => shell.HandleInput(new KeyEvent(++_clockMs, key, IsDown: true));

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static Dictionary<string, string> LoadBaselines()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(BaselinePath)) return result;
        foreach (var line in File.ReadAllLines(BaselinePath))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var equals = line.IndexOf('=');
            if (equals < 0) continue;
            result[line[..equals]] = line[(equals + 1)..];
        }
        return result;
    }

    private static void SaveBaselines(Dictionary<string, string> baselines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)!);
        var lines = baselines.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}");
        File.WriteAllLines(BaselinePath, lines);
        Console.WriteLine($"Wrote {baselines.Count} baseline hash(es) to {BaselinePath}.");
    }

    private static string? ContentDir(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

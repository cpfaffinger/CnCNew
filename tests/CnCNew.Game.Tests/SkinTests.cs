using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Skins;
using UiScaleMode = CnCNew.Ui.Layout.UiScaleMode;

namespace CnCNew.Game.Tests;

/// <summary>
/// The skins of docs/UI-MODERN.md with real data: the original skin draws the shipped layouts themselves (R1); the options, "More options" and the HUD of a match are
/// pinned in both skins in six resolutions (<c>baselines/skin-screens.txt</c>, R2; every other screen is pinned by its own suite, once per skin); the settings choose the
/// skin and keep it; and a person by script switches the skin in the options and walks main menu, skirmish, score, analysis and replay list with its playback (R4).
/// </summary>
internal static class SkinTests
{
    private static readonly (int Width, int Height)[] Resolutions = [(800, 600), (1024, 768), (1280, 720), (1920, 1080), (2560, 1080), (640, 960)];
    private const string BaselinePath = "tests/CnCNew.Game.Tests/baselines/skin-screens.txt";
    private const int NavWidth = 1280, NavHeight = 720;

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        var contentDir = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var harness = new TestHarness(nameof(SkinTests));
        if (contentDir is null)
        {
            Console.WriteLine("SKIP SkinTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        var baselines = SkinPins.Load(BaselinePath);
        var updating = System.Environment.GetEnvironmentVariable("CNCNEW_UPDATE_BASELINES") == "1";
        harness.Run("R1: the original skin draws every shipped layout of the shell as it is, laid out as before skins existed", () => OriginalIsTheShippedLayouts(data));
        harness.Run("R2: the options, More options and the HUD of a match in both skins and six resolutions", () => Pinned(data, baselines, updating));
        harness.Run("The settings choose the skin, the run's --skin is kept until the person chooses, and More options writes what it shows", () => SettingsChooseTheSkin(data));
        harness.Run("R4: a person by script switches to the modern skin in the options and walks skirmish, score, analysis and the replay list with its playback", () => PersonSwitchesTheSkin(data));
        if (updating) SkinPins.Save(BaselinePath, baselines);
        return harness.Failures;
    }

    private static string Directory(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-skin-tests-" + Guid.NewGuid().ToString("N"), name);
        System.IO.Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Delete(string directory) => System.IO.Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);

    private static void OriginalIsTheShippedLayouts(GameData data)
    {
        var directory = Directory("original");
        var shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight));
        TestHarness.Assert(shell.Skin == UiSkin.Original && shell.Settings.Display.Skin == UiSkinName.Original, "a shell without settings draws the original skin (D-21)");
        foreach (var name in ShellLayoutNames.All)
            TestHarness.Assert(ReferenceEquals(shell.Layout(name), shell.Layouts.Get(name)), $"{name}: the original skin's layout is the shipped one itself");
        TestHarness.Assert(shell.ScaleOptions == UiScaleOptions.Original with { Mode = UiScaleMode.Uniform }, $"the menus are laid out uniformly as before ({shell.ScaleOptions})");
        Delete(directory);
    }

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static void Pin(string name, Func<int, int, UiDrawList> draw, Dictionary<string, string> baselines, bool updating)
    {
        foreach (var (width, height) in Resolutions)
        {
            var list = draw(width, height);
            TestHarness.Assert(list.Statistics.UnresolvedLabels == 0, $"{name}@{width}x{height}: {list.Statistics.UnresolvedLabels} unresolved label(s)");
            var key = $"{name}@{width}x{height}";
            var hash = Sha256(list.Dump());
            if (updating) baselines[key] = hash;
            else TestHarness.Assert(baselines.TryGetValue(key, out var expected) && expected == hash, $"{key}: draw list hash changed ({baselines.GetValueOrDefault(key) ?? "none recorded"} -> {hash})");
        }
    }

    private static void Pinned(GameData data, Dictionary<string, string> baselines, bool updating)
    {
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
        {
            var directory = Directory("pins-" + skin.Id);
            var shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight), skin: skin);
            ShellTestInput.Click(shell, "MainMenu.wnd:ButtonOptions");
            shell.CurrentScreen.Tree.PointerLeft();
            UiDrawList Shell(int width, int height)
            {
                shell.Resize(width, height);
                return shell.BuildDrawList();
            }
            TestHarness.Assert(shell.CurrentScreen is OptionsScreen && shell.CurrentScreen.Tree.FindByName(OptionsScreen.MoreOptionsButton) is { IsVisible: true } more
                && more.Text?.Value == "More options", $"{skin}: the options offer More options");
            Pin($"Options-{skin.Id}", Shell, baselines, updating);
            shell.Resize(NavWidth, NavHeight);
            ShellTestInput.Click(shell, OptionsScreen.MoreOptionsButton);
            shell.CurrentScreen.Tree.PointerLeft();
            var screen = shell.CurrentScreen as MoreOptionsScreen ?? throw new InvalidOperationException("More options did not open");
            TestHarness.Assert(screen.HotkeyRows >= 40, $"{skin}: the hotkey view lists the keys ({screen.HotkeyRows})");
            Pin($"MoreOptions-{skin.Id}", Shell, baselines, updating);
            TestHarness.Assert(shell.Stack.All(item => item.Tree.UnregisteredCallbacks.Count == 0), $"{skin}: every callback of the screens is registered");
            Delete(directory);

            // The HUD of a match a few seconds in: the command bar with the skin's type, in the modern skin kept in proportion with its band beside it.
            var map = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
            var setup = new MatchSetup(map, "FactionAmerica", ["FactionChina"], CnCNew.Simulation.Testing.SkirmishMatchOptions.StandardAt(data.Rate)) { Seed = 3 };
            var live = LiveMatch.Create(data, setup, new Vector2(NavWidth, NavHeight));
            var hud = new MatchController(live, GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = skin.Name } });
            for (var step = 0; step < 8; step++) live.Advance(0.25);
            hud.Update(0.25);
            TestHarness.Assert(hud.Skin == skin, $"{skin}: the HUD takes the skin of the settings");
            Pin($"Hud-{skin.Id}", hud.BuildDrawList, baselines, updating);
            var band = hud.BuildDrawList(1920, 1080).Commands.OfType<UiFillRect>().Count(fill => fill.Color == ModernLayout.Palette.Canvas);
            TestHarness.Assert(skin.IsModern ? band == 2 : band == 0, $"{skin}: the band beside the command bar at 16:9 ({band} fills)");

            // The same HUD with a line in the message list and the money display's tooltip (Phase 39): the modern skin draws both in its type and palette.
            hud.Messages.ClientNote("a line of the message list");
            var money = hud.WindowRect("ControlBar.wnd:MoneyDisplay") ?? throw new InvalidOperationException("no money display");
            hud.Process(new MouseMoveEvent(10_016, new Vector2(money.CenterX, money.CenterY)));
            for (var step = 0; step < 4; step++)
            {
                live.Advance(0.25);
                hud.Update(0.25);
            }
            var tipped = hud.BuildDrawList(NavWidth, NavHeight).Commands;
            var palette = ModernLayout.Palette;
            TestHarness.Assert(tipped.OfType<UiTextDraw>().Any(text => text.Text.Contains("a line of the message list", StringComparison.Ordinal)), $"{skin}: the message list shows the line");
            TestHarness.Assert(skin.IsModern == tipped.OfType<UiFillRect>().Any(fill => fill.Color == palette.Panel), $"{skin}: the tooltip is a panel of the modern palette only in the modern skin");
            Pin($"HudTooltip-{skin.Id}", hud.BuildDrawList, baselines, updating);
        }
        TestHarness.Assert(baselines.Count == 48 && baselines.Values.Distinct(StringComparer.Ordinal).Count() == baselines.Count, $"no two of the {baselines.Count} pictures are the same");
    }

    private static void SettingsChooseTheSkin(GameData data)
    {
        var directory = Directory("settings");
        new SettingsStore(directory).Save(GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = UiSkinName.Modern } });
        var fromFile = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight));
        TestHarness.Assert(fromFile.Skin == UiSkin.Modern && fromFile.ScaleOptions.Mode == UiScaleMode.Anchor, "settings that say modern give the modern skin, laid out anchored");
        TestHarness.Assert(!ReferenceEquals(fromFile.Layout(ShellLayoutNames.MainMenu), fromFile.Layouts.Get(ShellLayoutNames.MainMenu))
            && ReferenceEquals(fromFile.Layout(ShellLayoutNames.MainMenu), fromFile.Layout(ShellLayoutNames.MainMenu)), "derived once at run time, then kept");
        Delete(directory);

        // The run's --skin stays while settings are saved for another reason (the skirmish setup keeps its last map), and goes when the person chooses in the options.
        directory = Directory("override");
        var shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight), skin: UiSkin.Modern);
        TestHarness.Assert(shell.Skin == UiSkin.Modern && shell.Settings.Display.Skin == UiSkinName.Original, "the run's skin is the modern one, the settings still say original");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSkirmish");
        ShellTestInput.Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonStart");
        TestHarness.Assert(shell.Phase == ShellPhase.MatchLoading && shell.Skin == UiSkin.Modern, "saving the skirmish memory keeps the run's skin (the defect the readability check found)");
        Delete(directory);

        directory = Directory("more");
        shell = GameShell.Create(data, new FixedShellEnvironment(directory, audioDevices: ["Speakers (Test)", "Headphones (Test)"]), viewport: new UiViewport(NavWidth, NavHeight));
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonOptions");
        ShellTestInput.Click(shell, OptionsScreen.MoreOptionsButton);
        var script = ShellScript.Parse(
        [
            "wait screen MoreOptionsScreen",
            "expect combo MoreOptions.wnd:ComboSkin shows Original",
            "expect combo MoreOptions.wnd:ComboAudioDevice shows System default",
            "select MoreOptions.wnd:ComboSkin Modern",
            "select MoreOptions.wnd:ComboDisplayMode Window without frame",
            "select MoreOptions.wnd:ComboAspect Wider",
            "select MoreOptions.wnd:ComboTextScale 125 %",
            "select MoreOptions.wnd:ComboAudioDevice Headphones (Test)",
            "click MoreOptions.wnd:ButtonAccept",
            "wait screen OptionsScreen",
            "expect skin modern",
            "click OptionsMenu.wnd:ButtonAccept",
            "wait screen MainMenuScreen",
            "expect skin modern",
        ], "more-options");
        for (var frame = 0; frame < 400 && !script.IsFinished; frame++)
        {
            script.Advance(shell, 0.05);
            shell.Update(0.05);
            _ = shell.BuildDrawList();
        }
        foreach (var line in script.Report().Where(line => line.Contains("FAILED", StringComparison.Ordinal))) Console.WriteLine($"       {line}");
        TestHarness.Assert(script.IsFinished && !script.Failed, $"the script runs through ({script.StepsDone}/{script.StepCount})");
        var saved = new SettingsStore(directory).Load().Settings;
        TestHarness.Assert(saved.Display.Skin == UiSkinName.Modern && saved.Display.Mode == DisplayMode.Borderless && saved.Display.Aspect == AspectHandling.KeepVertical
            && saved.Accessibility.UiTextScalePercent == 125 && saved.Audio.Device == "Headphones (Test)", $"More options saved what it showed ({saved.Display}, {saved.Accessibility}, {saved.Audio.Device})");
        TestHarness.Assert(shell.ScaleOptions.TextScale == 1.25m && shell.CurrentScreen.Tree.Options.TextScale == 1.25m, "the text scale reaches the screens built again");
        TestHarness.Assert(shell.DrainOutputs().OfType<ApplyResolutionOutput>().Any(output => output.Mode == DisplayMode.Borderless), "the display mode goes to the host");
        Delete(directory);
    }

    private static void PersonSwitchesTheSkin(GameData data)
    {
        var directory = Directory("person");
        var shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight));
        shell.SkirmishMatchSeconds = 30;
        var script = ShellScript.Load(PersonScriptPath);
        var host = new ReplayBrowserTests.Host(data, shell, script);
        host.Attach();
        for (var frame = 0; frame < 20_000 && !script.IsFinished; frame++) host.Frame(0.25);
        foreach (var line in script.Report().TakeLast(4)) Console.WriteLine($"       {line}");
        TestHarness.Assert(script.IsFinished && !script.Failed, $"the script runs through ({script.StepsDone}/{script.StepCount})");
        TestHarness.Assert(new SettingsStore(directory).Load().Settings.Display.Skin == UiSkinName.Modern, "the choice is kept in the settings");
        var again = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight));
        TestHarness.Assert(again.Skin == UiSkin.Modern, "the next start of the game is in the modern skin");
        Delete(directory);
    }

    /// <summary>The scripted person of R4 (docs/UI-MODERN.md section 9); <c>scripts/check-skins.ps1</c> walks the same file in the window client.</summary>
    private const string PersonScriptPath = "scripts/shell/skin-person.txt";
}

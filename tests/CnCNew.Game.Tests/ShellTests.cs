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
using CnCNew.Ui.Wnd;

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
        harness.Run("Main menu shows only what MainMenuInit leaves visible", () => MainMenuInitialState(data));
        harness.Run("Buttons draw hilite and pushed images under the pointer and drop them when it leaves", () => ButtonStates(data));
        harness.Run("Options: sliders draw their boxes and drag, static texts have no hover fill", () => OptionsSlidersAndLabels(data));
        harness.Run("Quit dialog text takes the header template size, not the 1920x1080 layout scale", () => QuitDialogFonts(data));
        harness.Run("Skirmish setup and options show localized words, not internal names or the layout's placeholders", () => LocalizedSetupText(data));

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

    // ------------------------------------------------------------------------------------------------- demo findings (shell look)

    private static readonly string[] MainGroupLabels = ["GUI:Credits", "GUI:Exit", "GUI:Multiplayer", "GUI:Options", "GUI:ReplayMenu", "GUI:SinglePlayer"];

    private static string[] LabelsDrawn(GameShell shell)
        => [.. shell.BuildDrawList().Commands.OfType<UiTextDraw>().Select(text => text.LabelKey ?? text.Text).Distinct().OrderBy(key => key, StringComparer.Ordinal)];

    /// <summary>
    /// <c>MainMenuInit</c> hides every drop-down group but the main one and (<c>showSelectiveButtons(SHOW_NONE)</c>) the six
    /// per-faction "recent save"/"load game" buttons left of the button column; none of them is <c>HIDDEN</c> in the file.
    /// </summary>
    private static void MainMenuInitialState(GameData data)
    {
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
        var tree = shell.CurrentScreen.Tree;
        foreach (var name in new[] { "ButtonUSARecentSave", "ButtonUSALoadGame", "ButtonGLARecentSave", "ButtonGLALoadGame", "ButtonChinaRecentSave", "ButtonChinaLoadGame",
                     "MapBorder", "MapBorder1", "MapBorder3", "MapBorder4", "ButtonGetUpdate", "ButtonGetMapPack" })
            TestHarness.Assert(!tree.FindByName($"MainMenu.wnd:{name}")!.IsVisible, $"{name} is hidden in the initial main menu");
        TestHarness.Assert(tree.FindByName("MainMenu.wnd:MapBorder2")!.IsVisible, "the main button group is visible");
        var labels = LabelsDrawn(shell);
        TestHarness.Assert(labels.SequenceEqual(MainGroupLabels), $"the initial main menu draws exactly its six buttons' texts, got: {string.Join(", ", labels)}");

        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        TestHarness.Assert(!LabelsDrawn(shell).Contains("GUI:RecentSave") && !LabelsDrawn(shell).Contains("GUI:LoadGame"), "the faction save buttons stay hidden in the single-player group");
        Click(shell, "MainMenu.wnd:ButtonSingleBack");
        labels = LabelsDrawn(shell);
        TestHarness.Assert(labels.SequenceEqual(MainGroupLabels), $"back in the main group: {string.Join(", ", labels)}");
    }

    private static HashSet<string> ImagesOver(UiDrawList list, UiRect rect)
        => [.. list.Commands.OfType<UiImageDraw>().Where(draw => draw.Destination.Intersects(rect)).Select(draw => draw.Image)];

    private static void ButtonStates(GameData data)
    {
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
        var tree = shell.CurrentScreen.Tree;
        var button = tree.FindByName("MainMenu.wnd:ButtonSinglePlayer")!;
        var enabled = button.Definition.GetDrawData(WndDrawDataKind.Enabled)[0].Image!;
        var hilite = button.Definition.GetDrawData(WndDrawDataKind.Hilite)[0].Image!;
        var pushed = button.Definition.GetDrawData(WndDrawDataKind.Hilite)[1].Image!;
        TestHarness.Assert(new[] { enabled, hilite, pushed }.Distinct().Count() == 3, $"the shipped button has three different left images ({enabled}, {hilite}, {pushed})");
        HashSet<string> Images() => ImagesOver(shell.BuildDrawList(), button.Rect);
        WndColor TextColor() => shell.BuildDrawList().Commands.OfType<UiTextDraw>().Single(text => text.LabelKey == "GUI:SinglePlayer").Color;
        var (x, y) = (button.Rect.CenterX, button.Rect.CenterY);

        TestHarness.Assert(Images().Contains(enabled) && !Images().Contains(hilite) && !Images().Contains(pushed), "idle: ENABLEDDRAWDATA");
        TestHarness.Assert(TextColor() == button.Definition.TextColors.Enabled, "idle: enabled text colour");
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(x, y)));
        TestHarness.Assert(Images().Contains(hilite) && !Images().Contains(enabled) && !Images().Contains(pushed), "hovered: HILITEDRAWDATA");
        TestHarness.Assert(TextColor() == button.Definition.TextColors.Hilite && TextColor() != button.Definition.TextColors.Enabled, "hovered: hilite text colour");
        shell.HandleInput(new MouseButtonEvent(++_clockMs, MouseButton.Left, true, new Vector2(x, y)));
        TestHarness.Assert(Images().Contains(pushed) && !Images().Contains(enabled) && !Images().Contains(hilite), "pressed: the pushed images of HILITEDRAWDATA");
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(4, 4)));
        shell.HandleInput(new MouseButtonEvent(++_clockMs, MouseButton.Left, false, new Vector2(4, 4)));
        TestHarness.Assert(Images().Contains(enabled) && !Images().Contains(hilite) && !Images().Contains(pushed), "released outside: back to ENABLEDDRAWDATA");
        TestHarness.Assert(tree.FindByName("MainMenu.wnd:MapBorder2")!.IsVisible, "released outside: no click");

        // A click that switches the group hides the button while it is hovered; it must not come back hilited.
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        Click(shell, "MainMenu.wnd:ButtonSingleBack");
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(4, 4)));
        foreach (var name in new[] { "ButtonSinglePlayer", "ButtonExit", "ButtonOptions" })
            TestHarness.Assert(!tree.FindByName($"MainMenu.wnd:{name}")!.IsHilited, $"{name} is not left hilited after the sub-menu round trip");
        TestHarness.Assert(Images().Contains(enabled) && !Images().Contains(hilite), "the single-player button draws enabled again");

        // A dialog on top takes the pointer: the button underneath loses its hilite, also after the dialog closes.
        var options = tree.FindByName("MainMenu.wnd:ButtonOptions")!;
        Click(shell, "MainMenu.wnd:ButtonOptions");
        TestHarness.Assert(shell.CurrentScreen is OptionsScreen && !options.IsHilited && !options.IsSelected, "the options button under the options screen is not hilited");
        Click(shell, "OptionsMenu.wnd:ButtonBack");
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(4, 4)));
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen && tree.AllWindows.All(window => !window.IsHilited && !(window.Type == WndWindowType.PushButton && window.IsSelected)),
            "no main menu window is hilited or pressed with the pointer away");
    }

    /// <summary>
    /// The skirmish setup listed the internal template names (<c>FactionAmerica</c>), <c>Color 1</c> and <c>None</c>; the original lists
    /// <c>PlayerTemplate.DisplayName</c> labels (<c>INI:FactionAmerica</c> = "USA"), <c>MultiplayerColor.TooltipName</c> labels (<c>Color:Gold</c>) and
    /// <c>Team:n</c>. The options menu showed the word "Entry" (the layout's TEXT of the two network entry fields) where OptionsMenuInit puts values.
    /// </summary>
    private static void LocalizedSetupText(GameData data)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-shell-tests-" + Guid.NewGuid().ToString("N"));
        var shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight));
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        Click(shell, "MainMenu.wnd:ButtonSkirmish");
        var tree = shell.CurrentScreen.Tree;
        string[] Entries(string name) => [.. tree.FindByName($"SkirmishGameOptionsMenu.wnd:{name}")!.ComboBox!.Entries];
        var armies = Entries("ComboBoxPlayerTemplate0");
        foreach (var expected in new[] { "USA", "China", "GLA", "Random" })
            TestHarness.Assert(armies.Contains(expected), $"the army list has '{expected}': {string.Join(", ", armies)}");
        TestHarness.Assert(armies.All(name => !name.StartsWith("Faction", StringComparison.Ordinal)), $"no internal template name in the army list: {string.Join(", ", armies)}");
        var colors = Entries("ComboBoxColor0");
        TestHarness.Assert(colors.Length == 8 && colors.SequenceEqual(["Gold", "Red", "Blue", "Green", "Orange", "Cyan", "Purple", "Pink"]), $"the colors are the multiplayer color names in file order: {string.Join(", ", colors)}");
        TestHarness.Assert(Entries("ComboBoxTeam0").SequenceEqual(["None", "1", "2", "3", "4"]), "the teams are Team:0..Team:4");
        TestHarness.Assert(Entries("ComboBoxPlayer1")[0] == "Open", "the open slot is GUI:Open");
        TestHarness.Assert(shell.TextSource!.RawNameHits.Count == 0, $"no label fell back to a raw name: {string.Join(", ", shell.TextSource.RawNameHits.Keys)}");

        shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight));
        Click(shell, "MainMenu.wnd:ButtonOptions");
        foreach (var name in new[] { "TextEntryHTTPProxy", "TextEntryFirewallPortOverride" })
            TestHarness.Assert(shell.CurrentScreen.Tree.FindByName($"OptionsMenu.wnd:{name}")!.TextEntry!.Text.Length == 0, $"{name} starts empty, not with the layout's word 'Entry'");
    }

    private static void OptionsSlidersAndLabels(GameData data)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-shell-tests-" + Guid.NewGuid().ToString("N"));
        var shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight));
        Click(shell, "MainMenu.wnd:ButtonOptions");
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(4, 4)));
        var tree = shell.CurrentScreen.Tree;

        foreach (var name in new[] { "SliderMusicVolume", "SliderSFXVolume", "SliderVoiceVolume", "SliderScrollSpeed", "SliderGamma" })
        {
            var window = tree.FindByName($"OptionsMenu.wnd:{name}")!;
            var filled = window.Definition.GetDrawData(WndDrawDataKind.Disabled)[0].Image!;
            var blank = window.Definition.GetDrawData(WndDrawDataKind.Disabled)[1].Image!;
            var boxes = shell.BuildDrawList().Commands.OfType<UiImageDraw>().Where(draw => draw.Destination.Intersects(window.Rect) && (draw.Image == filled || draw.Image == blank)).ToArray();
            TestHarness.Assert(window.IsVisible && boxes.Length >= 8, $"{name} draws its row of boxes ({boxes.Length})");
            TestHarness.Assert(boxes.All(box => box.Destination.X >= window.Rect.X && box.Destination.Right <= window.Rect.Right), $"{name}: the boxes stay inside the slider");
        }

        // Static texts carry MOUSETRACK and a green hilite fill in the shipped file; hovering them must not draw it.
        var idle = shell.BuildDrawList().Dump();
        foreach (var name in new[] { "MusicVolumeLabel", "SFXVolumeLabel", "VoiceVolumeLabel", "ScrollSpeedLabel", "GammaLabel", "ResolutionLabel", "DetailLabel" })
        {
            var label = tree.FindByName($"OptionsMenu.wnd:{name}")!;
            TestHarness.Assert(label.Type == WndWindowType.StaticText && (label.Definition.Style & WndStyle.MouseTrack) != 0, $"{name} is a static text with MOUSETRACK");
            shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(label.Rect.X + 12, label.Rect.CenterY)));
            TestHarness.Assert(!label.IsHilited && shell.BuildDrawList().Dump() == idle, $"hovering {name} changes nothing");
        }

        // Drag the music slider from a quarter to three quarters: value, boxes and the working settings follow.
        var slider = tree.FindByName("OptionsMenu.wnd:SliderMusicVolume")!;
        var full = slider.Definition.GetDrawData(WndDrawDataKind.Disabled)[0].Image!;
        int Filled() => shell.BuildDrawList().Commands.OfType<UiImageDraw>().Count(draw => draw.Image == full && draw.Destination.Intersects(slider.Rect));
        var rowY = slider.Rect.Y + 3;
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(slider.Rect.X + slider.Rect.Width / 4, rowY)));
        TestHarness.Assert(slider.IsHilited && shell.BuildDrawList().Dump() != idle, "hovering a slider hilites it (frame row and thumb)");
        shell.HandleInput(new MouseButtonEvent(++_clockMs, MouseButton.Left, true, new Vector2(slider.Rect.X + slider.Rect.Width / 4, rowY)));
        var (quarterValue, quarterFilled) = (slider.Slider!.Value, Filled());
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(slider.Rect.X + slider.Rect.Width * 3 / 4, rowY)));
        shell.HandleInput(new MouseButtonEvent(++_clockMs, MouseButton.Left, false, new Vector2(slider.Rect.X + slider.Rect.Width * 3 / 4, rowY)));
        TestHarness.Assert(quarterValue is > 15 and < 35 && slider.Slider.Value is > 65 and < 85, $"the slider follows the pointer ({quarterValue} -> {slider.Slider.Value})");
        TestHarness.Assert(Filled() > quarterFilled && quarterFilled > 0, $"more boxes are filled after the drag ({quarterFilled} -> {Filled()})");
        var dragged = slider.Slider.Value;
        Click(shell, "OptionsMenu.wnd:ButtonAccept");
        TestHarness.Assert(Math.Abs(shell.Settings.Audio.Music - dragged / 100f) < 0.011f, $"the dragged value is applied ({shell.Settings.Audio.Music} for {dragged})");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// <c>QuitMessageBox.wnd</c>/<c>MessageBox.wnd</c> are authored at 1920x1080 (the other shell screens at 800x600) and take
    /// their fonts from header templates. Template point sizes are relative to the 800 pixel wide default display
    /// (<c>GlobalLanguage::adjustFontSize</c>), so at 1920x1080 LabelSmall (8) is 14, not 8, and Title (20) is 36.
    /// </summary>
    private static void QuitDialogFonts(GameData data)
    {
        foreach (var (width, height, title, message, button) in new[] { (1920, 1080, 36, 14, 21), (1280, 720, 24, 9, 14), (800, 600, 20, 8, 12) })
        {
            var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(width, height));
            var mainButton = shell.CurrentScreen.Tree.FindByName("MainMenu.wnd:ButtonExit")!;
            Click(shell, "MainMenu.wnd:ButtonExit");
            var tree = shell.CurrentScreen.Tree;
            var titleWindow = tree.FindByName("QuitMessageBox.wnd:StaticTextTitle")!;
            var messageWindow = tree.FindByName("QuitMessageBox.wnd:StaticTextMessage")!;
            TestHarness.Assert(messageWindow.Definition.Rect.CreationWidth == 1920 && messageWindow.Definition.HeaderTemplate == "LabelSmall" && titleWindow.Definition.HeaderTemplate == "Title",
                "the quit dialog is a 1920x1080 layout with header template fonts");
            TestHarness.Assert(titleWindow.Font.Size == title && messageWindow.Font.Size == message && tree.FindByName("QuitMessageBox.wnd:ButtonYes")!.Font.Size == button,
                $"{width}x{height}: title {titleWindow.Font.Size} (want {title}), message {messageWindow.Font.Size} (want {message}), button {tree.FindByName("QuitMessageBox.wnd:ButtonYes")!.Font.Size} (want {button})");
            var drawn = shell.BuildDrawList().Commands.OfType<UiTextDraw>().Where(text => text.LabelKey == "GUI:QuitPopupMessage").ToArray();
            TestHarness.Assert(drawn.Length > 0 && drawn.All(text => text.Font.Size == message), $"{width}x{height}: the message is drawn at size {message}");
            // The same template family in an 800x600 layout: MainButton (15) on the main menu scales by the same factor.
            TestHarness.Assert(mainButton.Font.Size * 20 / 15 == title, $"{width}x{height}: dialog and main menu fonts share one scale (MainButton {mainButton.Font.Size}, Title {title})");
        }
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

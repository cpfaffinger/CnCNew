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

        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        var baselines = LoadBaselines();
        var updating = System.Environment.GetEnvironmentVariable("CNCNEW_UPDATE_BASELINES") == "1";

        // The screen hashes of baselines/screens.txt do not depend on the simulation rate (measured equal at 30, 60 and 120 Hz; the match behind the score screen
        // is bounded in game time), so the walk runs on the suite's data.
        harness.Run("Menu shell walks every screen and draws cleanly", () => WalkScreens(data, baselines, updating));
        harness.Run("Options persist and reload", () => OptionsPersist(data));
        harness.Run("The score counters count by the owner of an object: units built, supplies collected, losses and kills", () => ScoreCountersByOwner(data));
        harness.Run("Boot loading screen reaches the main menu", () => BootFlow(contentDir));
        harness.Run("Main menu shows only what MainMenuInit leaves visible", () => MainMenuInitialState(data));
        harness.Run("Buttons draw hilite and pushed images under the pointer and drop them when it leaves", () => ButtonStates(data));
        harness.Run("Options: sliders draw their boxes and drag, static texts have no hover fill", () => OptionsSlidersAndLabels(data));
        harness.Run("Quit dialog: Yes and No stand at the first and the last button place, and the text takes the header template size", () => QuitDialogFonts(data));
        harness.Run("Main menu: the custom mission button of the retail layout has its text from Data/Patch.str and is inert", () => CustomMissionButton(data));
        harness.Run("Skirmish setup and options show localized words, not internal names or the layout's placeholders", () => LocalizedSetupText(data));
        harness.Run("Skirmish setup with the original AI offers the three armies of the original and starts a match of that AI", () => OriginalAiArmies(data));
        harness.Run("A window with TOOLTIPTEXT shows the pointer tooltip after the delay with the Mouse.ini style", () => PointerTooltip(data));
        harness.Run("Credits lines use the Language.ini credits fonts at the adjusted size", () => CreditsFonts(data));
        harness.Run("Shell::push replaces the screen below, overlays keep it: only what the original shows is drawn", () => ScreenStackDrawing(data));
        harness.Run("Escape leaves the load game screen without any gadget focus", () => LoadGameEscapeWithoutFocus(data));
        harness.Run("Challenge menu hides its biography until a general is picked, then shows the persona's text", () => ChallengeMenuBio(data));

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
        TestHarness.Assert(mapDisplay.Length > 0, "a map was chosen");

        Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonStart");
        TestHarness.Assert(shell.Phase == ShellPhase.MatchLoading, "Start moves to match loading");
        TestHarness.Assert(shell.CurrentScreen is LoadingScreen, "match loading shows the single-player load screen");
        Capture(shell, "MatchLoading", baselines, updating);

        shell.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var outputs = shell.DrainOutputs();
        var startMatch = outputs.OfType<StartMatchOutput>().SingleOrDefault();
        TestHarness.Assert(startMatch is not null, "exactly one StartMatchOutput after one drawn frame");
        TestHarness.Assert(shell.Phase == ShellPhase.InMatch, "shell hands off to the host");
        TestHarness.Assert(shell.BuildDrawList().Commands.Length == 0, "the shell draws nothing while the host owns the match");

        // Run a short, real skirmish to completion (host stands in for LiveMatch here) and show the results.
        // Twenty seconds of game time (600 ticks at 30 Hz): the score screen below is hashed, and its numbers are those of the game time played.
        var setup = startMatch!.Setup with { Options = startMatch.Setup.Options with { MaxTicks = data.Rate.Time.WholeSecondsToTicks(20) } };
        var live = LiveMatch.Create(data, setup, new Vector2(NavWidth, NavHeight));
        var stats = new MatchStatistics();
        while (!live.Session.IsOver) stats.Record(live.Session.Step(), live.Session.World);
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

    // ------------------------------------------------------------------------------------------------- screen stack (Shell::push hides the screen below)

    /// <summary>
    /// <c>Shell::push</c> runs the shutdown of the top screen, which ends in <c>layout->hide(TRUE)</c> (<c>MainMenuShutdown</c>), before the new layout
    /// is created: skirmish setup, credits, the challenge menu and the load game screen replace the main menu on screen. The options menu, the skirmish map
    /// picker and message boxes are layered over the screen below instead. The draw list must hold exactly what the original shows, nothing of a hidden
    /// screen (the main menu's button texts used to shine through the new windows).
    /// </summary>
    private static void ScreenStackDrawing(GameData data)
    {
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
        var main = shell.CurrentScreen;
        var mainAlone = main.BuildDrawList().Commands.Length;
        TestHarness.Assert(mainAlone > 0 && shell.BuildDrawList().Commands.Length == mainAlone, "the main menu alone is drawn on an empty stack");

        void OnlyTopIsDrawn(string what)
        {
            var top = shell.CurrentScreen;
            TestHarness.Assert(shell.Stack.Count > 1 && shell.Stack[0] == main, $"{what}: the main menu is still on the stack below");
            TestHarness.Assert(shell.BuildDrawList().Dump() == top.BuildDrawList().Dump(), $"{what}: only the topmost screen is drawn, nothing of the main menu shows through");
            TestHarness.Assert(!LabelsDrawn(shell).Intersect(MainGroupLabels).Any(), $"{what}: none of the main menu's button texts is drawn: {string.Join(", ", LabelsDrawn(shell).Intersect(MainGroupLabels))}");
        }

        void OverlayKeepsBelow(string what, ShellScreen below)
        {
            var top = shell.CurrentScreen;
            var expected = below.BuildDrawList().Commands.Length + top.BuildDrawList().Commands.Length;
            TestHarness.Assert(shell.BuildDrawList().Commands.Length == expected, $"{what}: the screen below stays visible behind the overlay ({shell.BuildDrawList().Commands.Length} commands, expected {expected})");
        }

        // skirmish setup replaces the main menu; the map picker is an overlay of the setup, not of the main menu
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        Click(shell, "MainMenu.wnd:ButtonSkirmish");
        TestHarness.Assert(shell.CurrentScreen is SkirmishSetupScreen, "skirmish setup is open");
        OnlyTopIsDrawn("skirmish setup");
        var setup = shell.CurrentScreen;
        Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonSelectMap");
        TestHarness.Assert(shell.CurrentScreen is SkirmishMapSelectScreen, "the map picker is open");
        OverlayKeepsBelow("map picker", setup);
        TestHarness.Assert(!LabelsDrawn(shell).Intersect(MainGroupLabels).Any(), "map picker: still nothing of the main menu");
        // showSkirmishGameOptionsUnderlyingGUIElements(FALSE): the setup's map preview, faction/colour/team combos and start button are hidden while the picker lies over them
        var setupTree = setup.Tree;
        foreach (var name in new[] { "MapWindow", "ButtonStart", "ButtonSelectMap", "ComboBoxColor0", "ComboBoxTeam3", "ComboBoxPlayerTemplate1", "StaticTextTeam" })
            TestHarness.Assert(!setupTree.FindByName($"SkirmishGameOptionsMenu.wnd:{name}")!.IsVisible, $"map picker: {name} of the setup is hidden");
        TestHarness.Assert(setupTree.FindByName("SkirmishGameOptionsMenu.wnd:ComboBoxPlayer1")!.IsVisible, "map picker: the player column stays");
        TestHarness.Assert(!setupTree.FindByName("SkirmishGameOptionsMenu.wnd:ButtonBack")!.IsEnabled && !setupTree.FindByName("SkirmishGameOptionsMenu.wnd:ButtonReset")!.IsEnabled, "map picker: Back and Reset of the setup are disabled");
        Click(shell, "SkirmishMapSelectMenu.wnd:ButtonBack");
        TestHarness.Assert(shell.CurrentScreen is SkirmishSetupScreen, "Back closes the map picker");
        foreach (var name in new[] { "MapWindow", "ButtonStart", "ButtonSelectMap", "ComboBoxColor0", "ComboBoxTeam1", "ComboBoxPlayerTemplate1", "StaticTextTeam" })
            TestHarness.Assert(setupTree.FindByName($"SkirmishGameOptionsMenu.wnd:{name}")!.IsVisible, $"after the map picker: {name} of the setup is back");
        TestHarness.Assert(setupTree.FindByName("SkirmishGameOptionsMenu.wnd:ButtonBack")!.IsEnabled && setupTree.FindByName("SkirmishGameOptionsMenu.wnd:ButtonReset")!.IsEnabled, "after the map picker: Back and Reset are enabled again");
        OnlyTopIsDrawn("skirmish setup after the map picker");
        Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonBack");
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "back on the main menu, which starts over on its root group (MainMenuInit runs again)");
        TestHarness.Assert(shell.Stack.Count == 1 && shell.BuildDrawList().Dump() == shell.CurrentScreen.BuildDrawList().Dump(), "the main menu draws alone again");

        // options and the quit box are layered over the main menu
        Click(shell, "MainMenu.wnd:ButtonOptions");
        TestHarness.Assert(shell.CurrentScreen is OptionsScreen, "options are open");
        OverlayKeepsBelow("options", main);
        Click(shell, "OptionsMenu.wnd:ButtonBack");
        Click(shell, "MainMenu.wnd:ButtonExit");
        TestHarness.Assert(shell.CurrentScreen is MessageBoxScreen, "the quit box is open");
        OverlayKeepsBelow("quit box", main);
        Click(shell, "QuitMessageBox.wnd:ButtonNo");

        // credits
        Click(shell, "MainMenu.wnd:ButtonCredits");
        TestHarness.Assert(shell.CurrentScreen is CreditsScreen, "credits are open");
        OnlyTopIsDrawn("credits");
        Key(shell, KeyCode.Escape);

        // load game (Load, then Load Game)
        Click(shell, "MainMenu.wnd:ButtonLoadReplay");
        Click(shell, "MainMenu.wnd:ButtonLoadGame");
        TestHarness.Assert(shell.CurrentScreen is LoadGameScreen, "load game is open");
        OnlyTopIsDrawn("load game");
        Click(shell, "SaveLoad.wnd:ButtonBack");
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "back on the main menu after load game");

        // challenge menu: the difficulty question is part of the main menu, then the menu replaces it
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        Click(shell, "MainMenu.wnd:ButtonChallenge");
        Click(shell, "MainMenu.wnd:ButtonMedium");
        TestHarness.Assert(shell.CurrentScreen is ChallengeSelectScreen, "the challenge menu is open");
        OnlyTopIsDrawn("challenge menu");
        Click(shell, "ChallengeMenu.wnd:ButtonBack");
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen && shell.Stack.Count == 1, "back on the main menu after the challenge menu");
    }

    /// <summary>
    /// <c>ChallengeMenuInit</c> hides the biography panel and the Play button until a general is chosen (<c>setGeneralBio</c>); the panel's four lines were the layout's own
    /// placeholders (<c>GUI:BioNameEntry</c> … drawn as unresolved labels) before the persona text came from <c>ChallengeMode.ini</c>.
    /// </summary>
    private static void ChallengeMenuBio(GameData data)
    {
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        Click(shell, "MainMenu.wnd:ButtonChallenge");
        Click(shell, "MainMenu.wnd:ButtonHard");
        TestHarness.Assert(shell.CurrentScreen is ChallengeSelectScreen, "the challenge menu is open");
        var tree = shell.CurrentScreen.Tree;
        UiWindow Win(string name) => tree.FindByName($"ChallengeMenu.wnd:{name}")!;
        TestHarness.Assert(!Win("GeneralsBioParent").IsVisible && !Win("ButtonPlay").IsVisible, "the biography panel and Play are hidden before a pick");
        TestHarness.Assert(shell.BuildDrawList().Statistics.UnresolvedLabels == 0, "the menu draws no unresolved label before a pick");
        for (var index = 0; index < 12; index++)
            TestHarness.Assert(Win($"GeneralPosition{index}").IsVisible == index < 9, $"GeneralPosition{index}: shown exactly for the nine playable generals");

        Click(shell, "ChallengeMenu.wnd:GeneralPosition2");
        TestHarness.Assert(Win("GeneralsBioParent").IsVisible && Win("ButtonPlay").IsVisible, "the panel and Play appear with a pick");
        foreach (var line in new[] { "BioNameEntry", "BioDOBEntry", "BioBirthplaceEntry", "BioStrategyEntry" })
        {
            var text = Win(line).Text!.Value;
            TestHarness.Assert(text.Length > 0 && !text.StartsWith("GUI:", StringComparison.Ordinal), $"{line} shows resolved text, got '{text}'");
        }
        TestHarness.Assert(shell.BuildDrawList().Statistics.UnresolvedLabels == 0, "the menu draws no unresolved label after a pick");
        Click(shell, "ChallengeMenu.wnd:ButtonPlay");
        TestHarness.Assert(shell.Phase == ShellPhase.MissionBriefing, "Play starts the picked general's campaign");
        for (var i = 0; i < 400 && shell.Phase == ShellPhase.MissionBriefing; i++) shell.Update(0.1);
        var started = shell.DrainOutputs().OfType<StartMissionOutput>().Single();
        TestHarness.Assert(started.Controller!.Campaign.Name == "CHALLENGE_2" && started.Controller.Progress.Difficulty == CnCNew.Simulation.Scripting.ScriptDifficulty.Hard, "position 2 plays CHALLENGE_2 with the difficulty asked on the main menu");
    }

    /// <summary>
    /// <c>PopupSaveLoad.cpp</c>: <c>SaveLoadMenuInput</c> handles Escape (it presses Back) and hangs on the <c>SaveLoadMenu</c> child, not on the root of the file;
    /// the layout's init focuses that child. Without it Escape only worked after a gadget had been clicked or tabbed to.
    /// </summary>
    private static void LoadGameEscapeWithoutFocus(GameData data)
    {
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
        Click(shell, "MainMenu.wnd:ButtonLoadReplay");
        Click(shell, "MainMenu.wnd:ButtonLoadGame");
        TestHarness.Assert(shell.CurrentScreen is LoadGameScreen, "load game is open");
        TestHarness.Assert(shell.CurrentScreen.Tree.Focus is null, "no gadget has the focus (the case that failed)");
        Key(shell, KeyCode.Escape);
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "Escape closes the load game screen without any gadget focus");

        // With a gadget focused it works as before.
        Click(shell, "MainMenu.wnd:ButtonLoadReplay");
        Click(shell, "MainMenu.wnd:ButtonLoadGame");
        Key(shell, KeyCode.Tab);
        TestHarness.Assert(shell.CurrentScreen.Tree.Focus is not null, "Tab focused a gadget");
        Key(shell, KeyCode.Escape);
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "Escape still works with a focused gadget");
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
        // SkirmishGameOptionsMenuInit lists GUI:Open, GUI:Closed, GUI:EasyAI, GUI:MediumAI, GUI:HardAI; the built-in AI plays the three armies by its strength (Phase 33,
        // docs/SKIRMISH-AI.md) and adds its three tactics profiles. The second slot starts as the medium army, the name field holds GUI:Player.
        for (var slot = 1; slot <= 7; slot++)
            TestHarness.Assert(Entries($"ComboBoxPlayer{slot}").SequenceEqual(["Open", "Closed", "Easy Army", "Medium Army", "Hard Army", "Rush Army", "Turtle Army", "Eco Army"]),
                $"opponent slot {slot} offers open, closed, the three armies and the three profiles: {string.Join(", ", Entries($"ComboBoxPlayer{slot}"))}");
        TestHarness.Assert(tree.FindByName("SkirmishGameOptionsMenu.wnd:ComboBoxPlayer1")!.ComboBox!.SelectedIndex == 3 && tree.FindByName("SkirmishGameOptionsMenu.wnd:ComboBoxPlayer2")!.ComboBox!.SelectedIndex == 0, "the second slot starts as the medium army, the others open");
        TestHarness.Assert(tree.FindByName("SkirmishGameOptionsMenu.wnd:TextEntryPlayerName")!.TextEntry!.Text == "Player", "the player name is GUI:Player");
        // getPreferredMap / getDefaultMap: without a remembered map the first multiplayer map of the catalog (lower-case path order) is chosen; the setup never says "no map".
        var expectedMap = shell.MapCatalog.Values.Where(entry => entry.IsMultiplayer).Select(entry => entry.VirtualPath).OrderBy(path => path.Replace('/', '\\').ToLowerInvariant(), StringComparer.Ordinal).First();
        var shownMap = tree.FindByName("SkirmishGameOptionsMenu.wnd:TextEntryMapDisplay")!.Text!.Value;
        TestHarness.Assert(shownMap == shell.MapNames.DisplayName(expectedMap, shell.MapCatalog, shell.TextSource) && shownMap.Length > 0 && !shownMap.Contains("no map", StringComparison.OrdinalIgnoreCase), $"the default map is preselected and named: '{shownMap}' ({expectedMap})");
        TestHarness.Assert(!shell.BuildDrawList().Commands.OfType<UiTextDraw>().Any(text => text.Text.Contains("Computer", StringComparison.Ordinal) || text.Text.Contains("o map selected", StringComparison.Ordinal)), "neither 'Computer' nor '(no map selected)' is on the screen");
        TestHarness.Assert(shell.TextSource!.RawNameHits.Count == 0, $"no label fell back to a raw name: {string.Join(", ", shell.TextSource.RawNameHits.Keys)}");

        // Every entry of a slot does what it says: open and closed are no opponent (Start then does nothing), the army is exactly one computer player.
        var slotOne = tree.FindByName("SkirmishGameOptionsMenu.wnd:ComboBoxPlayer1")!;
        foreach (var empty in new[] { 0, 1 })
        {
            tree.Post(UiEventKind.ComboSelected, slotOne, value: empty);
            tree.Dispatch();
            Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonStart");
            TestHarness.Assert(shell.Phase != ShellPhase.MatchLoading && shell.CurrentScreen is SkirmishSetupScreen, $"slot entry '{Entries("ComboBoxPlayer1")[empty]}' is no opponent: Start does nothing");
        }
        // The turtle profile (entry 6): a computer army whose tactics travel into the setup.
        tree.Post(UiEventKind.ComboSelected, slotOne, value: 6);
        // The room: the team and colour boxes of the two slots travel into the match (phase 25).
        tree.Post(UiEventKind.ComboSelected, tree.FindByName("SkirmishGameOptionsMenu.wnd:ComboBoxTeam0")!, value: 2);
        tree.Post(UiEventKind.ComboSelected, tree.FindByName("SkirmishGameOptionsMenu.wnd:ComboBoxTeam1")!, value: 2);
        tree.Post(UiEventKind.ComboSelected, tree.FindByName("SkirmishGameOptionsMenu.wnd:ComboBoxColor1")!, value: 5);
        tree.Dispatch();
        Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonStart");
        TestHarness.Assert(shell.Phase == ShellPhase.MatchLoading, "the computer army entry is an opponent: Start loads the match");
        shell.BuildDrawList(); // the match is handed off after one drawn frame of the load screen
        shell.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var started =shell.DrainOutputs().OfType<StartMatchOutput>().Single();
        TestHarness.Assert(started.Setup.Opponents.Length == 1, $"one computer army entry starts one computer player, got {started.Setup.Opponents.Length}");
        TestHarness.Assert(started.Setup.AiOf(1) is { Kind: CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Builtin, Difficulty: CnCNew.Simulation.Scripting.ScriptDifficulty.Normal, Tactics: CnCNew.Simulation.Skirmish.AiTactics.Turtle },
            $"the turtle entry is the built-in AI with the turtle profile at medium strength ({started.Setup.AiOf(1)})");
        TestHarness.Assert(started.Setup.Details.Length == 2 && started.Setup.Details[0] is { Team: 2, Color: 0, StartPosition: 0 } && started.Setup.Details[1] is { Team: 2, Color: 5, StartPosition: 1 },
            "the setup carries team, colour and start position of every slot that takes part");

        shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight));
        Click(shell, "MainMenu.wnd:ButtonOptions");
        foreach (var name in new[] { "TextEntryHTTPProxy", "TextEntryFirewallPortOverride" })
            TestHarness.Assert(shell.CurrentScreen.Tree.FindByName($"OptionsMenu.wnd:{name}")!.TextEntry!.Text.Length == 0, $"{name} starts empty, not with the layout's word 'Entry'");
    }

    /// <summary>
    /// With <see cref="GameShell.SkirmishAi"/> set to the original AI (the client's <c>--ai original</c>) an opponent slot lists what <c>SkirmishGameOptionsMenuInit</c> lists:
    /// <c>GUI:Open</c>, <c>GUI:Closed</c>, <c>GUI:EasyAI</c>, <c>GUI:MediumAI</c>, <c>GUI:HardAI</c>, and the match it starts names the original AI with the chosen army per slot.
    /// The default setup (<see cref="LocalizedSetupText"/>, the screen baselines) is the one "Medium Army" as before.
    /// </summary>
    private static void OriginalAiArmies(GameData data)
    {
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
        TestHarness.Assert(shell.SkirmishAi == CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Builtin, "the shell offers the built-in AI unless its host says otherwise");
        shell.SkirmishAi = CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Original;
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        Click(shell, "MainMenu.wnd:ButtonSkirmish");
        var tree = shell.CurrentScreen.Tree;
        string[] Entries(string name) => [.. tree.FindByName($"SkirmishGameOptionsMenu.wnd:{name}")!.ComboBox!.Entries];
        for (var slot = 1; slot <= 7; slot++)
            TestHarness.Assert(Entries($"ComboBoxPlayer{slot}").SequenceEqual(["Open", "Closed", "Easy Army", "Medium Army", "Hard Army"]), $"opponent slot {slot} offers the three armies of the original: {string.Join(", ", Entries($"ComboBoxPlayer{slot}"))}");
        TestHarness.Assert(tree.FindByName("SkirmishGameOptionsMenu.wnd:ComboBoxPlayer1")!.ComboBox!.SelectedIndex == 3, "the second slot starts as the medium army");
        TestHarness.Assert(shell.TextSource!.RawNameHits.Count == 0, $"the labels of the armies come from the string table: {string.Join(", ", shell.TextSource.RawNameHits.Keys)}");

        // Every army entry is an opponent of that level (the preselected map has two start positions: one opponent).
        foreach (var (entry, level) in new[] { (2, CnCNew.Simulation.Scripting.ScriptDifficulty.Easy), (3, CnCNew.Simulation.Scripting.ScriptDifficulty.Normal), (4, CnCNew.Simulation.Scripting.ScriptDifficulty.Hard) })
        {
            tree.Post(UiEventKind.ComboSelected, tree.FindByName("SkirmishGameOptionsMenu.wnd:ComboBoxPlayer1")!, value: entry);
            tree.Dispatch();
            Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonStart");
            TestHarness.Assert(shell.Phase == ShellPhase.MatchLoading, "Start loads the match");
            shell.BuildDrawList();
            shell.Update(CnCNew.Tests.TestTime.FrameSeconds);
            var started = shell.DrainOutputs().OfType<StartMatchOutput>().Single().Setup;
            TestHarness.Assert(started.UsesOriginalAi && started.Opponents.Length == 1, $"the match runs the original AI with one opponent ({started.Opponents.Length})");
            TestHarness.Assert(started.AiOf(1) == new MatchAi(CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Original, level), $"the opponent has the army its slot chose: {started.AiOf(1)} for entry {entry}");
            if (entry == 4) break;
            shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
            shell.SkirmishAi = CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Original;
            Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
            Click(shell, "MainMenu.wnd:ButtonSkirmish");
            tree = shell.CurrentScreen.Tree;
        }
    }

    /// <summary>
    /// Credits.cpp draws TITLE lines in <c>CreditsTitleFont</c> (Arial 22), MINORTITLE in <c>CreditsMinorTitleFont</c> (Arial 16 bold) and NORMAL in
    /// <c>CreditsNormalFont</c> (Arial 14), each through <c>adjustFontSize</c>; the shell used to draw Arial 24 bold / 16 bold / 12.
    /// </summary>
    private static void CreditsFonts(GameData data)
    {
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
        Click(shell, "MainMenu.wnd:ButtonCredits");
        for (var i = 0; i < 400; i++) shell.Update(0.05);
        var fonts = shell.BuildDrawList().Commands.OfType<UiTextDraw>().Select(text => text.Font).Where(font => font.Name == "Arial").Distinct().ToArray();
        TestHarness.Assert(fonts.Length > 0, "credits lines are on screen after 20 seconds of scrolling");
        // 1280x720 Uniform: the 800x600 scale is 1.2; sizes are the point sizes of Language.ini times 1.2, floored.
        var allowed = new[] { new UiFont("Arial", 26, false), new UiFont("Arial", 19, true), new UiFont("Arial", 16, false) };
        TestHarness.Assert(fonts.All(allowed.Contains), $"only the three credits fonts appear: {string.Join(", ", fonts)}");
    }

    private static void PointerTooltip(GameData data)
    {
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        var button = shell.CurrentScreen.Tree.FindByName("MainMenu.wnd:ButtonUSA")!;
        TestHarness.Assert(button.Definition.TooltipText is { Length: > 0 }, "the USA button carries a TOOLTIPTEXT");
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(button.Rect.CenterX, button.Rect.CenterY)));
        TestHarness.Assert(!shell.BuildDrawList().Commands.Any(command => command is UiFillRect { Color: { R: 20, G: 20, B: 20 } }), "no tooltip before the delay");
        shell.Update(2.0);
        var commands = shell.BuildDrawList().Commands;
        var fill = commands.OfType<UiFillRect>().Last(rect => rect.Color == new WndColor(20, 20, 20, 255));
        TestHarness.Assert(commands.OfType<UiBorderRect>().Any(border => border.Rect == fill.Rect && border.Color == new WndColor(60, 60, 155, 255)), "the box has the Mouse.ini border color");
        shell.TextSource!.TryResolve(button.Definition.TooltipText!, out var expected);
        var shown = string.Join(" ", commands.OfType<UiTextDraw>().Where(text => text.Color == new WndColor(220, 220, 220, 255)).Select(text => text.Text.Trim()));
        TestHarness.Assert(shown == string.Join(" ", expected.Split(' ', StringSplitOptions.RemoveEmptyEntries)), $"the tooltip shows the resolved label text: '{shown}'");
        TestHarness.Assert(fill.Rect.X > button.Rect.CenterX && fill.Rect.Y >= button.Rect.CenterY - 1, "the box sits right of and below the pointer");
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(2, 2)));
        TestHarness.Assert(!shell.BuildDrawList().Commands.Any(command => command is UiFillRect { Color: { R: 20, G: 20, B: 20 } }), "leaving the window hides it");
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
    /// <c>QuitMessageBox.wnd</c>/<c>MessageBox.wnd</c> take their fonts from header templates. Template point sizes are relative to the 800 pixel wide
    /// default display (<c>GlobalLanguage::adjustFontSize</c>), so at 1920x1080 LabelRegular (10) is 18 and Title (20) is 36. The buttons the caller asks for
    /// stand at the layout's button places (<c>GameWindowManager::gogoMessageBox</c>).
    /// </summary>
    private static void QuitDialogFonts(GameData data)
    {
        foreach (var (width, height, title, message, button) in new[] { (1920, 1080, 36, 18, 27), (1280, 720, 24, 12, 18), (800, 600, 20, 10, 15) })
        {
            var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(width, height));
            var mainButton = shell.CurrentScreen.Tree.FindByName("MainMenu.wnd:ButtonExit")!;
            Click(shell, "MainMenu.wnd:ButtonExit");
            var tree = shell.CurrentScreen.Tree;
            var titleWindow = tree.FindByName("QuitMessageBox.wnd:StaticTextTitle")!;
            var messageWindow = tree.FindByName("QuitMessageBox.wnd:StaticTextMessage")!;
            // The retail QuitMessageBox.wnd: an 800x600 layout, Title (20) over a LabelRegular (10) message, MainButton (15) buttons.
            TestHarness.Assert(messageWindow.Definition.Rect is { CreationWidth: 800, CreationHeight: 600 } && messageWindow.Definition.HeaderTemplate == "LabelRegular" && titleWindow.Definition.HeaderTemplate == "Title",
                "the quit dialog is the retail 800x600 layout with header template fonts");
            // gogoMessageBox: Yes stands at Ok's place, No (without Cancel) at Cancel's; the layout parks Yes one row higher and No in the middle.
            var yes = tree.FindByName("QuitMessageBox.wnd:ButtonYes")!;
            var no = tree.FindByName("QuitMessageBox.wnd:ButtonNo")!;
            var okPlace = tree.FindByName("QuitMessageBox.wnd:ButtonOk")!.Definition.Rect;
            var cancelPlace = tree.FindByName("QuitMessageBox.wnd:ButtonCancel")!.Definition.Rect;
            TestHarness.Assert(yes.DesignRect.Left == okPlace.Left && yes.DesignRect.Top == okPlace.Top && yes.DesignRect.Width == yes.Definition.Rect.Width
                               && no.DesignRect.Left == cancelPlace.Left && no.DesignRect.Top == cancelPlace.Top && no.DesignRect.Width == no.Definition.Rect.Width,
                $"{width}x{height}: Yes takes the first button place and No the last ({yes.DesignRect} / {no.DesignRect})");
            TestHarness.Assert(yes.IsVisible && no.IsVisible && yes.Rect.Y == no.Rect.Y && yes.Rect.Right <= no.Rect.X, $"{width}x{height}: the two buttons stand in one row, Yes left of No ({yes.Rect} / {no.Rect})");
            TestHarness.Assert(titleWindow.Font.Size == title && messageWindow.Font.Size == message && tree.FindByName("QuitMessageBox.wnd:ButtonYes")!.Font.Size == button,
                $"{width}x{height}: title {titleWindow.Font.Size} (want {title}), message {messageWindow.Font.Size} (want {message}), button {tree.FindByName("QuitMessageBox.wnd:ButtonYes")!.Font.Size} (want {button})");
            var drawn = shell.BuildDrawList().Commands.OfType<UiTextDraw>().Where(text => text.LabelKey == "GUI:QuitPopupMessage").ToArray();
            TestHarness.Assert(drawn.Length > 0 && drawn.All(text => text.Font.Size == message), $"{width}x{height}: the message is drawn at size {message}");
            // The same template family in an 800x600 layout: MainButton (15) on the main menu scales by the same factor.
            TestHarness.Assert(mainButton.Font.Size * 20 / 15 == title, $"{width}x{height}: dialog and main menu fonts share one scale (MainButton {mainButton.Font.Size}, Title {title})");
        }
    }

    /// <summary>
    /// The retail <c>MainMenu.wnd</c> comes from <c>PatchWindow.big</c> and has a button the main string table has no text for; the text is in <c>Data/Patch.str</c>.
    /// The label must resolve (no "missing label" on the single-player menu) and the button must not react: this build has no screen behind it.
    /// </summary>
    private static void CustomMissionButton(GameData data)
    {
        var text = data.LoadText("English");
        TestHarness.Assert(text.TryGet("GUI:CustomMission", out var caption) && caption.Length > 0, "the label of the custom mission button resolves");
        var shell = GameShell.Create(data, NewEnvironment(), viewport: new UiViewport(NavWidth, NavHeight));
        Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        var button = shell.CurrentScreen.Tree.FindByName("MainMenu.wnd:ButtonCustomMission");
        TestHarness.Assert(button is { IsVisible: true, IsEnabled: false }, "the button is shown and disabled");
        var before = shell.CurrentScreen;
        Click(shell, "MainMenu.wnd:ButtonCustomMission");
        TestHarness.Assert(ReferenceEquals(shell.CurrentScreen, before), "a click on it opens nothing");
    }

    // ------------------------------------------------------------------------------------------------- options persistence

    /// <summary>
    /// Most events of the simulation name only their object - a finished unit its factory, a deposit the truck, a death the victim and the killer - and carry no player; the
    /// counters must read the owner from the world. They did not, and showed 0 units built, 0 lost and 0 resources for everybody.
    /// </summary>
    private static void ScoreCountersByOwner(GameData data)
    {
        var setup = new MatchSetup("Maps/Alpine Assault/Alpine Assault.map", "FactionAmerica", ["FactionChina"],
            SkirmishMatchOptions.StandardAt(data.Rate) with { MaxTicks = data.Rate.Time.WholeSecondsToTicks(150) }) { Seed = 3 };
        var live = LiveMatch.Create(data, setup, new Vector2(NavWidth, NavHeight));
        var stats = new MatchStatistics();
        stats.Begin(live.Session.World);
        while (!live.Session.IsOver) stats.Record(live.Session.Step(), live.Session.World);
        var computer = stats.TryGet(live.Session.Players[1].Id);
        TestHarness.Assert(computer is { UnitsBuilt: > 0, Resources: > 0, BuildingsBuilt: > 0 },
            $"the computer player built units and collected supplies ({computer?.UnitsBuilt} units, {computer?.Resources} credits, {computer?.BuildingsBuilt} buildings)");
        var counted = live.Session.World.Entities.Count(entity => entity.Owner == live.Session.Players[1].Id && CnCNew.Game.Spectator.SpectatorStats.Classify(entity.Template) is CnCNew.Game.Spectator.SpectatorUnitClass.Army or CnCNew.Game.Spectator.SpectatorUnitClass.Worker && !entity.EffectivelyDead);
        TestHarness.Assert(computer!.UnitsBuilt <= counted + computer.UnitsLost + 2, $"no more units built than it has and lost, beside the ones it started with ({computer.UnitsBuilt}, {counted}, {computer.UnitsLost})");
    }

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

        // The alternate mouse setup (left selects, right orders) is this client's default: the box is checked, a click on it switches to the original's setup.
        var alternate = tree.FindByName("OptionsMenu.wnd:CheckAlternateMouse")!;
        TestHarness.Assert(shell.Settings.Controls.AlternateMouse && alternate.IsSelected, "the alternate mouse box is checked by default");
        Click(shell, "OptionsMenu.wnd:CheckAlternateMouse");
        TestHarness.Assert(!alternate.IsSelected && shell.Settings.Controls.AlternateMouse, "the click unchecks the box; nothing is applied before Accept");

        Click(shell, "OptionsMenu.wnd:ButtonAccept");
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "accept closes the options screen");
        var applied = shell.Settings;
        TestHarness.Assert(applied.Audio.Music > 0.99f, "the music slider change was applied");
        TestHarness.Assert(!applied.Controls.AlternateMouse, "the original's mouse setup was applied");

        var reloaded = GameShell.Create(data, environment, viewport: new UiViewport(NavWidth, NavHeight));
        TestHarness.Assert(reloaded.Settings.Display.Width == applied.Display.Width && reloaded.Settings.Display.Height == applied.Display.Height, "resolution persisted across a fresh shell");
        TestHarness.Assert(Math.Abs(reloaded.Settings.Audio.Music - applied.Audio.Music) < 0.01f, "music volume persisted across a fresh shell");
        TestHarness.Assert(!reloaded.Settings.Controls.AlternateMouse, "the mouse setup persisted across a fresh shell");
        Click(reloaded, "MainMenu.wnd:ButtonOptions");
        TestHarness.Assert(!reloaded.CurrentScreen.Tree.FindByName("OptionsMenu.wnd:CheckAlternateMouse")!.IsSelected, "and the box shows it");

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

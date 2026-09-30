using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Skirmish;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Text;
using LayoutScaleMode = CnCNew.Ui.Layout.UiScaleMode;

namespace CnCNew.Game.Shell;

/// <summary>What the shell is currently showing. See <c>docs/GAME-SHELL.md</c> for the full state diagram.</summary>
public enum ShellPhase
{
    /// <summary><see cref="GameData.Load"/> is running on a worker thread; the boot load screen is shown.</summary>
    Boot,
    /// <summary>Main menu and the dialogs reachable from it (options, skirmish setup, credits, quit confirmation).</summary>
    Menus,
    /// <summary>A skirmish was configured; the match load screen is shown for exactly one frame before <see cref="StartMatchOutput"/> fires.</summary>
    MatchLoading,
    /// <summary>The host owns the match now (<c>LiveMatch</c>/the HUD package); the shell draws nothing until <see cref="GameShell.MatchFinished"/>.</summary>
    InMatch,
    /// <summary>The score screen, built from the finished match.</summary>
    Results,
}

/// <summary>
/// The Stride-free game shell state machine: boot/loading, main menu, options, skirmish setup and its map picker,
/// credits, quit confirmation, match hand-off, and the score screen. The host drives it with
/// <see cref="HandleInput"/>/<see cref="Update"/>/<see cref="Resize"/> and reads <see cref="BuildDrawList"/> plus
/// <see cref="DrainOutputs"/> every frame; nothing here touches Stride, a swap chain or an audio device directly.
/// </summary>
public sealed class GameShell
{
    private readonly List<ShellScreen> _stack = [];
    private readonly List<ShellOutput> _outputs = [];
    private readonly SettingsStore _settingsStore;
    private Task<GameData>? _loadTask;
    private volatile string _loadStage = "";
    private ContentFileSystem? _bootContent;
    private MatchSetup? _pendingMatchSetup;
    private bool _loadingFrameShown;

    private GameShell(IShellEnvironment environment, string language, UiViewport viewport)
    {
        Environment = environment;
        Language = language;
        Viewport = viewport;
        _settingsStore = new SettingsStore(environment.SettingsDirectory);
        var loaded = _settingsStore.Load();
        Settings = loaded.Settings;
        ScaleOptions = UiScaleOptions.Original with { Mode = LayoutScaleMode.Uniform };
    }

    public GameData Data { get; private set; } = null!;
    public IShellEnvironment Environment { get; }
    public string Language { get; }
    public ShellPhase Phase { get; private set; }
    public GameSettings Settings { get; private set; }
    public UiViewport Viewport { get; private set; }
    public UiScaleOptions ScaleOptions { get; }
    public UiTreeServices TreeServices { get; private set; } = null!;
    public ShellLayouts Layouts { get; private set; } = null!;
    public ShellTextSource? TextSource { get; private set; }
    public LocalizedText? RawText { get; private set; }
    public ImmutableDictionary<string, ShellMapEntry> MapCatalog { get; private set; } = ImmutableDictionary<string, ShellMapEntry>.Empty;
    public ShellMapNames MapNames { get; } = new();
    public ImmutableArray<CreditsLine> Credits { get; private set; } = [];
    public IReadOnlyList<ShellScreen> Stack => _stack;
    public ShellScreen CurrentScreen => _stack[^1];

    /// <summary>0..100, only meaningful during <see cref="ShellPhase.Boot"/>; based on the three fixed stages <see cref="GameData.Load"/> reports.</summary>
    public int LoadProgressPercent => _loadStage switch
    {
        "Opening content" => 0,
        "Compiling definitions" => 33,
        "Building catalogs" => 66,
        _ => _loadTask?.IsCompletedSuccessfully == true ? 100 : 0,
    };

    /// <summary>Starts already past Boot, with <paramref name="data"/> compiled; used by tests and by a host that loads content itself.</summary>
    public static GameShell Create(GameData data, IShellEnvironment environment, string language = "English", UiViewport? viewport = null)
    {
        var shell = new GameShell(environment, language, viewport ?? new UiViewport(GameSettings.Default.Display.Width, GameSettings.Default.Display.Height));
        if (viewport is null) shell.Viewport = FitViewport(environment, shell.Settings);
        shell.FinishLoading(data);
        return shell;
    }

    /// <summary>Starts in <see cref="ShellPhase.Boot"/>: <see cref="GameData.Load"/> runs on a worker thread while the boot load screen shows its stage.</summary>
    public static GameShell CreateLoading(string contentDirectory, IShellEnvironment environment, string language = "English", UiViewport? viewport = null)
    {
        var shell = new GameShell(environment, language, viewport ?? new UiViewport(GameSettings.Default.Display.Width, GameSettings.Default.Display.Height));
        if (viewport is null) shell.Viewport = FitViewport(environment, shell.Settings);
        var bootContent = ContentFileSystem.Open(contentDirectory);
        shell._bootContent = bootContent;
        shell.Layouts = ShellLayouts.Load(bootContent, language, ShellLayoutNames.ShellGameLoadScreen);
        shell.RawText = GameData.LoadText(bootContent, language);
        shell.TextSource = new ShellTextSource(shell.RawText);
        shell.TreeServices = new UiTreeServices(new ShellTextMeasure()) { Text = shell.TextSource, HeaderTemplates = shell.Layouts.HeaderTemplates };
        shell.Phase = ShellPhase.Boot;
        shell.Push(new LoadingScreen(shell, ShellLayoutNames.ShellGameLoadScreen));
        shell._loadTask = Task.Run(() => GameData.Load(contentDirectory, stage => shell._loadStage = stage));
        return shell;
    }

    private static UiViewport FitViewport(IShellEnvironment environment, GameSettings? settings)
    {
        var display = settings?.Display ?? GameSettings.Default.Display;
        if (environment.DesktopWidth <= 0 || environment.DesktopHeight <= 0) return new UiViewport(display.Width, display.Height);
        var fit = Resolutions.BestFit(display.Width, display.Height, environment.DesktopWidth, environment.DesktopHeight);
        return new UiViewport(fit.Result.Width, fit.Result.Height);
    }

    private void FinishLoading(GameData data)
    {
        Data = data;
        Layouts = ShellLayouts.Load(data.Content, Language, [.. ShellLayoutNames.All]);
        RawText = data.LoadText(Language);
        TextSource = new ShellTextSource(RawText);
        TreeServices = new UiTreeServices(new ShellTextMeasure())
        {
            Text = TextSource,
            HeaderTemplates = Layouts.HeaderTemplates,
            Images = new ShellImageLookup(data.Images),
        };
        MapCatalog = ShellMapCatalog.Load(data.Content, data.Maps);
        Credits = ShellCredits.Load(data.Content);
        Phase = ShellPhase.Menus;
        Replace(new MainMenuScreen(this));
        _bootContent = null;
    }

    // ------------------------------------------------------------------------------------------------- host-facing loop

    public void Resize(int width, int height)
    {
        Viewport = new UiViewport(width, height);
        foreach (var screen in _stack) screen.Tree.SetViewport(Viewport, ScaleOptions);
    }

    public void HandleInput(InputEvent evt)
    {
        if (_stack.Count == 0) return;
        var tree = CurrentScreen.Tree;
        switch (evt)
        {
            case MouseMoveEvent m: tree.MouseMove((int)m.Position.X, (int)m.Position.Y); break;
            case MouseButtonEvent b:
                var button = b.Button switch { MouseButton.Left => UiMouseButton.Left, MouseButton.Right => UiMouseButton.Right, _ => UiMouseButton.Middle };
                if (b.IsDown) tree.MouseDown((int)b.Position.X, (int)b.Position.Y, button);
                else tree.MouseUp((int)b.Position.X, (int)b.Position.Y, button);
                break;
            case MouseWheelEvent w: tree.MouseWheel((int)w.Position.X, (int)w.Position.Y, (int)MathF.Round(w.Notches)); break;
            case KeyEvent { IsDown: true } k: tree.KeyDown(MapKey(k.Key)); break;
            case TextInputEvent t: tree.Character(t.Character); break;
            case FocusLostEvent: tree.SetFocus(null); break;
        }
        tree.Dispatch();
    }

    private static UiKey MapKey(KeyCode key) => key switch
    {
        KeyCode.Enter or KeyCode.KeypadEnter => UiKey.Enter,
        KeyCode.Escape => UiKey.Escape,
        KeyCode.Space => UiKey.Space,
        KeyCode.Tab => UiKey.Tab,
        KeyCode.Left => UiKey.Left,
        KeyCode.Right => UiKey.Right,
        KeyCode.Up => UiKey.Up,
        KeyCode.Down => UiKey.Down,
        KeyCode.Home => UiKey.Home,
        KeyCode.End => UiKey.End,
        KeyCode.PageUp => UiKey.PageUp,
        KeyCode.PageDown => UiKey.PageDown,
        KeyCode.Backspace => UiKey.Backspace,
        KeyCode.Delete => UiKey.Delete,
        _ => UiKey.Other,
    };

    private double _clockMs;

    public void Update(double dtSeconds)
    {
        _clockMs += dtSeconds * 1000;
        if (Phase == ShellPhase.Boot)
        {
            if (_loadTask is { IsCompleted: true } task)
            {
                if (task.IsFaulted) throw task.Exception!.GetBaseException();
                FinishLoading(task.Result);
            }
            else if (_stack.Count > 0) ((LoadingScreen)CurrentScreen).SetProgress(LoadProgressPercent);
        }
        else if (Phase == ShellPhase.MatchLoading && _loadingFrameShown)
        {
            var setup = _pendingMatchSetup!;
            _pendingMatchSetup = null;
            Phase = ShellPhase.InMatch;
            _outputs.Add(new StartMatchOutput(setup));
        }
        foreach (var screen in _stack) screen.Update(dtSeconds);
        if (_stack.Count > 0) CurrentScreen.Tree.AdvanceTo((long)_clockMs);
    }

    public UiDrawList BuildDrawList()
    {
        if (Phase == ShellPhase.InMatch) return new UiDrawList([], new UiDrawStatistics(0, 0, 0, 0, ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal)));
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        int windows = 0, unresolvedLabels = 0, unresolvedSizes = 0, border = 0;
        var custom = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var screen in _stack)
        {
            var list = screen.BuildDrawList();
            commands.AddRange(list.Commands);
            windows += list.Statistics.WindowsDrawn;
            unresolvedLabels += list.Statistics.UnresolvedLabels;
            unresolvedSizes += list.Statistics.UnresolvedImageSizes;
            border += list.Statistics.BorderStatusWindows;
            foreach (var (name, count) in list.Statistics.CustomDraws) custom[name] = custom.GetValueOrDefault(name) + count;
        }
        if (Phase == ShellPhase.MatchLoading) _loadingFrameShown = true;
        return new UiDrawList(commands.ToImmutable(), new UiDrawStatistics(windows, unresolvedLabels, unresolvedSizes, border, custom.ToImmutableSortedDictionary(StringComparer.Ordinal)));
    }

    public ImmutableArray<ShellOutput> DrainOutputs()
    {
        var result = ImmutableArray.CreateRange(_outputs);
        _outputs.Clear();
        return result;
    }

    /// <summary>The host calls this once its <c>LiveMatch</c>/<c>SkirmishMatchSession</c> is over, with the statistics tracker it fed while the match ran (or null if it fed none).</summary>
    public void MatchFinished(SkirmishMatchSession session, MatchStatistics? statistics = null)
    {
        Phase = ShellPhase.Results;
        Replace(new ScoreScreenScreen(this, MatchResult.From(session, statistics)));
    }

    // ------------------------------------------------------------------------------------------------- screen navigation (called by screens)

    internal void PlaySound(string eventName) => _outputs.Add(new PlayUiSoundOutput(eventName));

    internal void OpenOptions() => Push(new OptionsScreen(this));
    internal void OpenSkirmishSetup() => Push(new SkirmishSetupScreen(this));
    internal void OpenSkirmishMapSelect(SkirmishSetupScreen owner) => Push(new SkirmishMapSelectScreen(this, owner));
    internal void OpenCredits() => Push(new CreditsScreen(this));

    internal void OpenQuitConfirm() => Push(new MessageBoxScreen(this, ShellLayoutNames.QuitMessageBox, "GUI:QuitPopupTitle", "GUI:QuitPopupMessage",
        ShellMessageBoxButtons.YesNo, onAccept: () => _outputs.Add(new ExitRequestedOutput()), onDecline: CloseTop));

    internal void CloseTop() => Pop();

    internal void ReturnToMainMenuFromResults()
    {
        Phase = ShellPhase.Menus;
        Replace(new MainMenuScreen(this));
    }

    internal void ApplySettings(GameSettings settings)
    {
        Settings = settings;
        var issues = _settingsStore.Save(settings);
        _outputs.Add(new SettingsChangedOutput(settings));
        _outputs.Add(new ApplyResolutionOutput(settings.Display.Mode, settings.Display.Width, settings.Display.Height, settings.Display.VSync));
        _ = issues; // validation issues are informational only; Normalize() already applied them to what was saved.
    }

    internal void BeginMatchLoading(MatchSetup setup)
    {
        _pendingMatchSetup = setup;
        _loadingFrameShown = false;
        Phase = ShellPhase.MatchLoading;
        Replace(new LoadingScreen(this, ShellLayoutNames.SinglePlayerLoadScreen));
    }

    internal string FormatMoney(int amount) => RawText is { } text && text.TryGet("GUI:StartingMoneyFormat", out _) ? text.Format("GUI:StartingMoneyFormat", amount) : $"${amount}";

    private void Push(ShellScreen screen) => _stack.Add(screen);

    private void Pop()
    {
        if (_stack.Count == 0) return;
        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        top.OnClosed();
    }

    private void Replace(ShellScreen screen)
    {
        while (_stack.Count > 0) Pop();
        Push(screen);
    }
}

using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Game.Campaign;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Save;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Skirmish;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Text;
using CnCNew.Ui.Transitions;
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
    /// <summary>A campaign mission was chosen: <c>SinglePlayerLoadScreen.wnd</c> shows its objectives/units/location while a
    /// placeholder stands in for the intro movie and briefing voice (no Bink decoder, see <c>docs/CAMPAIGN-AND-SAVEGAMES.md</c>).
    /// Held for <see cref="CampaignMission.VoiceLengthSeconds"/> of simulated time, then <see cref="StartMissionOutput"/> fires.</summary>
    MissionBriefing,
    /// <summary>The host owns the mission now (steps <c>MissionRun.Session</c> and drains its <c>ScriptEngine</c> outputs); the
    /// shell draws nothing until <see cref="GameShell.MissionFinished"/>.</summary>
    InMission,
    /// <summary>The victory/defeat banner plus the mission result screen (next mission/retry/main menu), built from the
    /// finished mission.</summary>
    MissionResults,
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

    private MissionRun? _pendingMission;
    private CampaignController? _pendingMissionController;
    private double _briefingElapsed;
    private double _briefingDuration;

    private readonly IUiTextMeasure _textMeasure;
    private readonly bool _animateTransitions;
    private bool _mainMenuShownBefore;
    private TooltipPainter? _tooltips;

    private GameShell(IShellEnvironment environment, string language, UiViewport viewport, IUiTextMeasure? textMeasure, bool animateTransitions)
    {
        _textMeasure = textMeasure ?? new ShellTextMeasure();
        _animateTransitions = animateTransitions;
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

    /// <summary>
    /// <c>Data/INI/WindowTransitions.ini</c>, read strictly; <see langword="null"/> when the shell was created without animations. Screens create
    /// their <see cref="ShellScreen.Transitions"/> from it, so a null table means every group change is a hard cut (the state the golden hashes and
    /// the click-through tests were recorded in).
    /// </summary>
    public WindowTransitionTable? TransitionTable { get; private set; }

    /// <summary>Some screen of the stack still plays a window transition (or is about to open the screen it flashed out for).</summary>
    public bool IsAnimating => _stack.Any(screen => screen.IsAnimating);

    /// <summary>True the first time a main menu is built in this shell's life (the original fades the very first one in from black, <c>FirstTimeRunningTheGame</c>).</summary>
    internal bool TakeFirstMainMenu()
    {
        var first = !_mainMenuShownBefore;
        _mainMenuShownBefore = true;
        return first;
    }

    /// <summary><c>Data/INI/Campaign.ini</c> (not part of the compiled definition graph, see <c>docs/CAMPAIGN-AND-SAVEGAMES.md</c> §1);
    /// <see langword="null"/> only while still booting.</summary>
    public CampaignCatalog? Campaigns { get; private set; }

    /// <summary>Savegame files under <see cref="IShellEnvironment.SaveDirectory"/>; <see langword="null"/> only while still booting.</summary>
    public SaveGameStore? Saves { get; private set; }

    /// <summary>
    /// What the multiplayer screens reach the machine through (docs/GAME-SHELL.md section 15); null leaves the main menu's Multiplayer button inert, as in every
    /// shell that was not given one (the headless tests of the other screens).
    /// </summary>
    public IShellNetwork? Network { get; private set; }
    private Func<GameData, IShellNetwork>? _networkFactory;

    /// <summary>The visit to a server that is under way: from the address screen until the person leaves the server again, across matches.</summary>
    public ShellNetSession? Net { get; private set; }
    private ShellNetSession? _pendingNet;
    private bool _netStartSent;

    private MissionContext MissionContext => _missionContext ??= MissionContext.From(Data);
    private MissionContext? _missionContext;

    /// <summary>0..100, only meaningful during <see cref="ShellPhase.Boot"/>; based on the three fixed stages <see cref="GameData.Load"/> reports.</summary>
    public int LoadProgressPercent => _loadStage switch
    {
        "Opening content" => 0,
        "Compiling definitions" => 33,
        "Building catalogs" => 66,
        _ => _loadTask?.IsCompletedSuccessfully == true ? 100 : 0,
    };

    /// <summary>
    /// Starts already past Boot, with <paramref name="data"/> compiled; used by tests and by a host that loads content itself.
    /// <paramref name="textMeasure"/> must be the metrics of the font the host draws with: text is centred and wrapped
    /// from these widths, so the synthetic <see cref="ShellTextMeasure"/> (the default, for headless tests) puts every
    /// label off centre under a real rasterizer.
    /// </summary>
    public static GameShell Create(GameData data, IShellEnvironment environment, string language = "English", UiViewport? viewport = null, IUiTextMeasure? textMeasure = null, bool animateTransitions = false,
        IShellNetwork? network = null)
    {
        var shell = new GameShell(environment, language, viewport ?? new UiViewport(GameSettings.Default.Display.Width, GameSettings.Default.Display.Height), textMeasure, animateTransitions);
        if (viewport is null) shell.Viewport = FitViewport(environment, shell.Settings);
        shell.Network = network;
        shell.FinishLoading(data);
        return shell;
    }

    /// <summary>Starts in <see cref="ShellPhase.Boot"/>: <see cref="GameData.Load"/> runs on a worker thread while the boot load screen shows its stage.</summary>
    public static GameShell CreateLoading(string contentDirectory, IShellEnvironment environment, string language = "English", UiViewport? viewport = null, IUiTextMeasure? textMeasure = null, bool animateTransitions = false, CnCNew.Simulation.Time.SimulationRate? rate = null,
        Func<GameData, IShellNetwork>? network = null)
    {
        var shell = new GameShell(environment, language, viewport ?? new UiViewport(GameSettings.Default.Display.Width, GameSettings.Default.Display.Height), textMeasure, animateTransitions);
        if (viewport is null) shell.Viewport = FitViewport(environment, shell.Settings);
        var bootContent = ContentFileSystem.Open(contentDirectory);
        shell._bootContent = bootContent;
        shell.Layouts = ShellLayouts.Load(bootContent, language, ShellLayoutNames.ShellGameLoadScreen);
        shell.RawText = GameData.LoadText(bootContent, language);
        shell.TextSource = new ShellTextSource(shell.RawText);
        shell.TreeServices = new UiTreeServices(shell._textMeasure) { Text = shell.TextSource, HeaderTemplates = shell.Layouts.HeaderTemplates };
        shell._networkFactory = network;
        shell.Phase = ShellPhase.Boot;
        shell.Push(new LoadingScreen(shell, ShellLayoutNames.ShellGameLoadScreen));
        shell._loadTask = Task.Run(() => GameData.Load(contentDirectory, stage => shell._loadStage = stage, rate: rate));
        return shell;
    }

    private static UiViewport FitViewport(IShellEnvironment environment, GameSettings? settings)
    {
        var display = settings?.Display ?? GameSettings.Default.Display;
        if (environment.DesktopWidth <= 0 || environment.DesktopHeight <= 0) return new UiViewport(display.Width, display.Height);
        var fit = Resolutions.BestFit(display.Width, display.Height, environment.DesktopWidth, environment.DesktopHeight);
        return new UiViewport(fit.Result.Width, fit.Result.Height);
    }

    private static WindowTransitionTable LoadTransitionTable(ContentFileSystem content)
    {
        const string path = "Data/INI/WindowTransitions.ini";
        // Animations were asked for: a missing or malformed file is an error, not a quiet switch to hard cuts.
        using var stream = content.OpenRead(path);
        using var reader = new StreamReader(stream);
        return WindowTransitionTable.Parse(reader.ReadToEnd(), path);
    }

    private void FinishLoading(GameData data)
    {
        Data = data;
        _tooltips = new TooltipPainter(data.Graph);
        Layouts = ShellLayouts.Load(data.Content, Language, [.. ShellLayoutNames.All]);
        RawText = data.LoadText(Language);
        TextSource = new ShellTextSource(RawText);
        TreeServices = new UiTreeServices(_textMeasure)
        {
            Text = TextSource,
            HeaderTemplates = Layouts.HeaderTemplates,
            Images = new ShellImageLookup(data.Images),
        };
        MapCatalog = ShellMapCatalog.Load(data.Content, data.Maps);
        Credits = ShellCredits.Load(data.Content);
        if (_animateTransitions) TransitionTable = LoadTransitionTable(data.Content);
        const string campaignPath = "Data/INI/Campaign.ini";
        if (data.Content.Exists(campaignPath))
        {
            using var stream = data.Content.OpenRead(campaignPath);
            Campaigns = CampaignCatalog.Parse(stream);
            const string personaPath = "Data/INI/ChallengeMode.ini";
            if (data.Content.Exists(personaPath))
            {
                using var personas = data.Content.OpenRead(personaPath);
                using var reader = new StreamReader(personas);
                Campaigns = Campaigns.WithPersonas(reader.ReadToEnd());
            }
        }
        Saves = new SaveGameStore(Environment.SaveDirectory);
        Replays = new CnCNew.Game.Replays.ReplayStore(Environment.ReplayDirectory);
        Network ??= _networkFactory?.Invoke(data);
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
        if (_stack.Count == 0 || CurrentScreen.IsClosing) return;
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
        else if (Phase == ShellPhase.MatchLoading && _pendingNet is { } loading)
        {
            // The shared load screen: the host of the shell builds the match (after the screen was drawn once); the connection is moved here, and the bars show
            // what every member's game reported.
            if (_loadingFrameShown && !_netStartSent)
            {
                _netStartSent = true;
                _outputs.Add(new StartNetMatchOutput(loading));
            }
            if (loading.Relay is { } relay)
            {
                relay.Pump();
                (CurrentScreen as NetLoadingScreen)?.SetProgress(relay.Client.LoadProgress);
            }
        }
        else if (Phase == ShellPhase.MatchLoading && _loadingFrameShown)
        {
            var setup = _pendingMatchSetup!;
            _pendingMatchSetup = null;
            Phase = ShellPhase.InMatch;
            _outputs.Add(new StartMatchOutput(setup));
        }
        else if (Phase == ShellPhase.MissionBriefing)
        {
            _briefingElapsed += dtSeconds;
            if (_briefingElapsed >= _briefingDuration)
            {
                var run = _pendingMission!;
                var controller = _pendingMissionController;
                _pendingMission = null;
                _pendingMissionController = null;
                Phase = ShellPhase.InMission;
                _outputs.Add(new StartMissionOutput(run, controller));
            }
        }
        if (Phase is ShellPhase.Menus or ShellPhase.Results) Net?.Lobby.Pump();
        foreach (var screen in _stack.ToArray()) screen.Update(dtSeconds);
        if (_stack.Count > 0 && CurrentScreen.ExitFinished) Pop();
        if (_stack.Count > 0) CurrentScreen.Tree.AdvanceTo((long)_clockMs);
    }

    public UiDrawList BuildDrawList()
    {
        if (Phase is ShellPhase.InMatch or ShellPhase.InMission) return new UiDrawList([], new UiDrawStatistics(0, 0, 0, 0, ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal)));
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        int windows = 0, unresolvedLabels = 0, unresolvedSizes = 0, border = 0;
        var custom = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (var index = FirstDrawnScreen(); index < _stack.Count; index++)
        {
            var screen = _stack[index];
            var list = screen.BuildDrawList();
            commands.AddRange(list.Commands);
            if (screen.Transitions is { } transitions)
            {
                var overlay = new List<UiDrawCommand>();
                transitions.AppendDraw(overlay);
                commands.AddRange(overlay);
            }
            windows += list.Statistics.WindowsDrawn;
            unresolvedLabels += list.Statistics.UnresolvedLabels;
            unresolvedSizes += list.Statistics.UnresolvedImageSizes;
            border += list.Statistics.BorderStatusWindows;
            foreach (var (name, count) in list.Statistics.CustomDraws) custom[name] = custom.GetValueOrDefault(name) + count;
        }
        // Mouse::drawTooltip lies over every window.
        if (_tooltips is not null && _stack.Count > 0 && CurrentScreen.Tree.Tooltip is { } tooltip)
            commands.AddRange(_tooltips.Build(tooltip, Viewport, ScaleOptions, _textMeasure));
        if (Phase == ShellPhase.MatchLoading) _loadingFrameShown = true;
        return new UiDrawList(commands.ToImmutable(), new UiDrawStatistics(windows, unresolvedLabels, unresolvedSizes, border, custom.ToImmutableSortedDictionary(StringComparer.Ordinal)));
    }

    /// <summary>
    /// The lowest screen of the stack that is on screen: the topmost one that hides what lies below it (<see cref="ShellScreen.HidesScreensBelow"/>) and
    /// everything layered over it. A screen under that one is shut down and hidden in the original (<c>Shell::push</c>, <c>MainMenuShutdown</c> ends in
    /// <c>layout-&gt;hide(TRUE)</c>), so its windows draw nothing and the windows of the screen above lie on whatever the shell shows behind the menus (the
    /// shell map of the original, which this build does not have: black).
    /// </summary>
    internal int FirstDrawnScreen()
    {
        var index = _stack.Count - 1;
        while (index > 0 && !_stack[index].HidesScreensBelow) index--;
        return Math.Max(index, 0);
    }

    public ImmutableArray<ShellOutput> DrainOutputs()
    {
        var result = ImmutableArray.CreateRange(_outputs);
        _outputs.Clear();
        return result;
    }

    /// <summary>The host calls this once its <c>LiveMatch</c>/<c>SkirmishMatchSession</c> is over, with the statistics tracker it fed while the match ran (or null if it fed none).</summary>
    public void MatchFinished(SkirmishMatchSession session, MatchStatistics? statistics = null) => MatchFinished(MatchResult.From(session, statistics));

    /// <summary>The same for a result the host has put together itself (a match with the original AI has no <c>SkirmishMatchSession</c>).</summary>
    public void MatchFinished(MatchResult result) => MatchFinished(result, null);

    /// <summary>
    /// The same, with what a server said when the match did not end by its own rules (<paramref name="netNote"/>: the connection was lost, the games were out of
    /// sync, the server reached a limit) - shown as a message over the score screen. After a match of a server the score screen leads back to its room.
    /// </summary>
    public void MatchFinished(MatchResult result, string? netNote)
    {
        _returnToReplays = false;
        if (Net is { } net) net.Relay = null;
        _pendingNet = null;
        Phase = ShellPhase.Results;
        Replace(new ScoreScreenScreen(this, result));
        if (netNote is { Length: > 0 }) Tell(NetText.TitleMatchEnded, netNote, CloseTop);
    }

    // ------------------------------------------------------------------------------------------------- multiplayer (docs/GAME-SHELL.md section 15)

    internal void OpenMultiplayer() => Push(new NetworkDirectConnectScreen(this));

    internal void OpenNetLobby(ShellNetSession session)
    {
        Net?.Dispose();
        Net = session;
        Push(new NetLobbyScreen(this, session));
    }

    internal void OpenNetRoom(ShellNetSession session) => Push(new NetRoomScreen(this, session));
    internal void OpenNetHostPopup(NetLobbyScreen owner, string playerName) => Push(new NetHostPopupScreen(this, owner, playerName));
    internal void OpenNetJoinPopup(NetLobbyScreen owner, string room) => Push(new NetJoinPopupScreen(this, owner, room));
    internal void OpenNetMapSelect(NetRoomScreen owner) => Push(new SkirmishMapSelectScreen(this, owner, ShellLayoutNames.LanMapSelectMenu));

    /// <summary>A question in our own words over the current screen (<c>MessageBox.wnd</c>, Yes and No); the callbacks close it.</summary>
    internal void AskYesNo(string title, string message, Action yes, Action no)
        => Push(new MessageBoxScreen(this, ShellLayoutNames.MessageBox, title, message, plainMessage: true, ShellMessageBoxButtons.YesNo, yes, no));

    /// <summary>A question of the original's own words over the current screen (<c>MessageBox.wnd</c>, Yes and No, CSF labels); the callbacks close it.</summary>
    internal void AskYesNoLabels(string titleLabel, string messageLabel, Action yes, Action no)
        => Push(new MessageBoxScreen(this, ShellLayoutNames.MessageBox, titleLabel, messageLabel, ShellMessageBoxButtons.YesNo, yes, no));

    /// <summary>A message of the original's own words over the current screen (<c>MessageBox.wnd</c>, OK only, CSF labels); OK closes it.</summary>
    internal void TellLabels(string titleLabel, string messageLabel)
        => Push(new MessageBoxScreen(this, ShellLayoutNames.MessageBox, titleLabel, messageLabel, ShellMessageBoxButtons.OkOnly, CloseTop));

    /// <summary>A message in our own words over the current screen (<c>MessageBox.wnd</c>, OK only); the callback closes it.</summary>
    internal void Tell(string title, string message, Action ok)
        => Push(new MessageBoxScreen(this, ShellLayoutNames.MessageBox, title, message, plainMessage: true, ShellMessageBoxButtons.OkOnly, ok));

    /// <summary>Ends the visit to a server: the connection (and a server this game hosted for it) is closed and the address screen is the top again.</summary>
    internal void CloseNet()
    {
        Net?.Dispose();
        Net = null;
        _pendingNet = null;
        while (_stack.Count > 1 && CurrentScreen is not NetworkDirectConnectScreen) Pop(reveal: false);
        if (_stack.Count > 0) CurrentScreen.OnRevealed();
    }

    /// <summary>The game ends: a visit to a server that is under way is closed (the room is told; a server this game hosts stops).</summary>
    public void Shutdown()
    {
        Net?.Dispose();
        Net = null;
        _pendingNet = null;
    }

    /// <summary>The host of the room started the match: the connection leaves the lobby and the shared load screen stands until the host of the shell reports.</summary>
    internal void BeginNetMatch(ShellNetSession session)
    {
        var room = session.Lobby.Room;
        var rows = room is null ? [] : NetLoadingScreen.RowsOf(room);
        var mapPath = room?.Settings.MapPath ?? string.Empty;
        var mapName = mapPath.Length == 0 ? string.Empty : MapCatalog.ContainsKey(mapPath) ? MapNames.DisplayName(mapPath, MapCatalog, TextSource) : Path.GetFileNameWithoutExtension(mapPath);
        session.Relay = session.Lobby.TakeRelay();
        _pendingNet = session;
        _netStartSent = false;
        _loadingFrameShown = false;
        Phase = ShellPhase.MatchLoading;
        Replace(new NetLoadingScreen(this, rows, mapName));
    }

    /// <summary>The host of the shell built the match and the server let it begin: the shell draws nothing until <see cref="MatchFinished(MatchResult, string?)"/>.</summary>
    public void NetMatchStarted()
    {
        if (Phase != ShellPhase.MatchLoading || _pendingNet is null) throw new InvalidOperationException("No match of a server is loading.");
        _pendingNet = null;
        Phase = ShellPhase.InMatch;
    }

    /// <summary>The match could not be built or begun here: back in front of the server's rooms, with the reason.</summary>
    public void NetMatchFailed(string message)
    {
        if (_pendingNet is not { } session) throw new InvalidOperationException("No match of a server is loading.");
        _pendingNet = null;
        session.Relay?.Dispose();
        session.Relay = null;
        ReturnToNet(session);
        Tell(NetText.TitleCannotEnter, message, CloseTop);
    }

    /// <summary>After a match of a server: main menu, address screen and the server's front are stacked again, and the lobby enters the room it came from.</summary>
    private void ReturnToNet(ShellNetSession session)
    {
        Phase = ShellPhase.Menus;
        Replace(new MainMenuScreen(this));
        Push(new NetworkDirectConnectScreen(this));
        session.Lobby.ReturnToRoom();
        Push(new NetLobbyScreen(this, session));
    }

    /// <summary>
    /// Which AI the skirmish setup offers for a computer slot. The default is the simulation's own AI with its one "Medium Army" entry, as before. With
    /// <see cref="CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Original"/> - set by the host (the client's <c>--ai original</c>) before the setup screen is opened - a slot
    /// offers the original's three armies (<c>GUI:EasyAI</c>, <c>GUI:MediumAI</c>, <c>GUI:HardAI</c>) and the match runs the original AI (docs/SKIRMISH-AI-ORIGINAL.md).
    /// </summary>
    public CnCNew.Simulation.Skirmish.Original.SkirmishAiKind SkirmishAi { get; set; } = CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Builtin;

    /// <summary>
    /// The host calls this once <c>run.Session.IsOver</c> (victory or defeat): applies the outcome to the campaign
    /// (<see cref="CampaignController.Conclude"/>) and shows the victory/defeat banner plus the result screen
    /// (<c>docs/CAMPAIGN-AND-SAVEGAMES.md</c> §7). A run without a controller (should not happen once a mission is only ever
    /// reached through the campaign flow below) is reported as a defeat-less dead end: the result screen only offers Main Menu.
    /// </summary>
    public void MissionFinished(MissionRun run, CampaignController? controller)
    {
        ArgumentNullException.ThrowIfNull(run);
        var conclusion = controller is null ? (MissionConclusion?)null : controller.Conclude(run);
        Phase = ShellPhase.MissionResults;
        Replace(new MissionBannerScreen(this, conclusion));
        Push(new MissionResultScreen(this, run, controller, conclusion));
    }

    // ------------------------------------------------------------------------------------------------- campaign and savegame navigation (called by screens)

    internal void OpenChallengeSelect(ScriptDifficulty difficulty) => Push(new ChallengeSelectScreen(this, difficulty));
    internal void OpenLoadGame() => Push(new LoadGameScreen(this));

    // ------------------------------------------------------------------------------------------------- replays (docs/REPLAY-BROWSER.md)

    /// <summary>The replay files the client records after every skirmish and the replay list shows (beside the savegames); null before the content is loaded.</summary>
    public CnCNew.Game.Replays.ReplayStore? Replays { get; private set; }

    /// <summary>
    /// What this game is when a replay is judged: its definitions, rate, rule profile and build. A test of the screens sets a fixed one, so that a reason naming a build
    /// does not change with every commit.
    /// </summary>
    public CnCNew.Game.Replays.ReplayMachine ReplayMachine
    {
        get => _replayMachine ?? CnCNew.Game.Replays.ReplayMachine.Of(Data);
        set => _replayMachine = value;
    }

    private CnCNew.Game.Replays.ReplayMachine? _replayMachine;

    /// <summary>
    /// A check's short skirmish (the client's <c>--skirmish-seconds N</c>): the setup screen gives a match this length of game time instead of the standard one (0, the
    /// default: the standard length). Nothing else changes.
    /// </summary>
    public int SkirmishMatchSeconds { get; set; }

    // Set while a replay runs or its score screen stands: the way back leads to the replay list, not to the bare main menu.
    private bool _returnToReplays;

    internal void OpenReplayMenu() => Push(new ReplayMenuScreen(this));
    internal void OpenReplayRename(ReplayMenuScreen owner, string name) => Push(new ReplayRenameScreen(this, owner, name));

    /// <summary>
    /// Plays a replay of the list: a file this game cannot play is refused here with its reason (over the list); otherwise the shell hands the host the file
    /// (<see cref="StartReplayOutput"/>) and draws nothing until the playback ends.
    /// </summary>
    internal void BeginReplay(string name)
    {
        byte[] bytes;
        CnCNew.Game.Replays.MatchRecording recording;
        try
        {
            bytes = Replays!.Read(name);
            recording = CnCNew.Game.Replays.MatchRecordingFormat.Read(bytes);
        }
        catch (CnCNew.Game.Replays.ReplayRefusedException refusal)
        {
            ShowError(CnCNew.Game.Replays.ReplayText.CannotPlayTitle, refusal.Message);
            return;
        }
        var judged = CnCNew.Game.Replays.ReplayCompatibility.Judge(recording.Info, ReplayMachine);
        if (!judged.IsPlayable)
        {
            ShowError(CnCNew.Game.Replays.ReplayText.CannotPlayTitle, judged.Reason);
            return;
        }
        _returnToReplays = true;
        Phase = ShellPhase.InMatch;
        _outputs.Add(new StartReplayOutput(name, bytes));
    }

    /// <summary>The host's playback reached the end of the recording: the score screen with the playback's result, and over it what the playback found (<paramref name="note"/>).</summary>
    public void ReplayFinished(MatchResult result, string? note)
    {
        _returnToReplays = true;
        Phase = ShellPhase.Results;
        Replace(new ScoreScreenScreen(this, result));
        if (note is { Length: > 0 }) Tell(CnCNew.Game.Replays.ReplayText.PlaybackTitle, note, CloseTop);
    }

    /// <summary>The viewer left a playback before its end: back to the replay list.</summary>
    public void ReplayStopped()
    {
        _returnToReplays = false;
        Phase = ShellPhase.Menus;
        Replace(new MainMenuScreen(this));
        Push(new ReplayMenuScreen(this));
    }

    /// <summary>The host could not open the replay the shell handed it (the map's terrain differs, the file changed since it was listed): back to the list, with the reason.</summary>
    public void ReplayFailed(string message)
    {
        ReplayStopped();
        ShowError(CnCNew.Game.Replays.ReplayText.CannotPlayTitle, message);
    }

    /// <summary>Starts (or restarts) the current mission of <paramref name="controller"/> and shows its briefing
    /// (<see cref="ShellPhase.MissionBriefing"/>); <c>VoiceLengthSeconds</c> sets how long the briefing holds (floor of 2 s so
    /// a mission that does not set it is still readable, no Bink decoder so the intro movie stays a placeholder).</summary>
    internal void BeginMission(CampaignController controller)
    {
        var run = controller.StartCurrentMission();
        _pendingMission = run;
        _pendingMissionController = controller;
        _briefingElapsed = 0;
        _briefingDuration = Math.Max(2.0, run.Mission?.VoiceLengthSeconds ?? 0);
        Phase = ShellPhase.MissionBriefing;
        Replace(new MissionBriefingScreen(this, run.Mission!));
    }

    internal void BeginCampaign(CampaignDefinition campaign, ScriptDifficulty difficulty)
        => BeginMission(new CampaignController(MissionContext, campaign, CampaignProgress.Begin(campaign, difficulty)));

    /// <summary>Loads a savegame and goes straight to <see cref="ShellPhase.InMission"/> (no briefing: resuming is not a new
    /// start). Throws <see cref="SaveGameException"/> with a message fit to show the player when the file does not load.</summary>
    internal void LoadSaveAndContinue(string slot)
    {
        var bytes = Saves!.Read(slot);
        if (SkirmishSaveGame.IsSkirmishSave(bytes))
        {
            // Checked here, so that a file that does not fit is refused in the load screen; the host restores the session with the map.
            SkirmishSaveGame.Load(bytes, Data.Rate.TicksPerSecond, Data.Fingerprint);
            Phase = ShellPhase.InMatch;
            _outputs.Add(new ResumeMatchOutput(bytes, slot));
            return;
        }
        var loaded = SaveGame.Load(bytes, MissionContext, Campaigns);
        CampaignController? controller = null;
        if (loaded.Progress is { } progress && Campaigns?.Find(progress.Campaign) is { } campaign) controller = new CampaignController(MissionContext, campaign, progress);
        Phase = ShellPhase.InMission;
        _outputs.Add(new StartMissionOutput(loaded.Run, controller));
    }

    /// <summary>After a mission result: victory and not the last mission continues with the next one; defeat retries the
    /// current one. Both re-enter <see cref="ShellPhase.MissionBriefing"/>; a finished campaign returns to the main menu.</summary>
    internal void ContinueOrRetryMission(CampaignController controller)
    {
        if (controller.IsFinished) { ReturnToMainMenuFromResults(); return; }
        BeginMission(controller);
    }

    internal void ReturnToMainMenuFromMissionResults() => ReturnToMainMenuFromResults();

    /// <summary>Shows a plain-text error dialog (<c>MessageBox.wnd</c>, OK only) on top of the current screen, e.g. for a
    /// <see cref="SaveGameException"/> message, which is never a CSF label.</summary>
    internal void ShowError(string title, string message) => Push(new MessageBoxScreen(this, ShellLayoutNames.MessageBox, title, message, plainMessage: true, ShellMessageBoxButtons.OkOnly, CloseTop));

    // ------------------------------------------------------------------------------------------------- screen navigation (called by screens)

    internal void PlaySound(string eventName) => _outputs.Add(new PlayUiSoundOutput(eventName));

    internal void OpenOptions() => Push(new OptionsScreen(this));
    internal void OpenSkirmishSetup() => Push(new SkirmishSetupScreen(this));
    internal void OpenSkirmishMapSelect(SkirmishSetupScreen owner) => Push(new SkirmishMapSelectScreen(this, owner));
    internal void OpenCredits() => Push(new CreditsScreen(this));

    internal void OpenQuitConfirm() => Push(new MessageBoxScreen(this, ShellLayoutNames.QuitMessageBox, "GUI:QuitPopupTitle", "GUI:QuitPopupMessage",
        ShellMessageBoxButtons.YesNo, onAccept: () => _outputs.Add(new ExitRequestedOutput()), onDecline: CloseTop));

    internal void CloseTop()
    {
        if (_stack.Count > 0 && CurrentScreen.BeginExit()) return;
        Pop();
    }

    internal void ReturnToMainMenuFromResults()
    {
        if (_returnToReplays)
        {
            ReplayStopped();
            return;
        }
        if (Net is { } net && Network is not null)
        {
            ReturnToNet(net);
            return;
        }
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

    private void Push(ShellScreen screen)
    {
        // The covered screen no longer sees the pointer; without this its last hovered button stays hilited underneath.
        if (_stack.Count > 0) CurrentScreen.Tree.PointerLeft();
        _stack.Add(screen);
    }

    private void Pop(bool reveal = true)
    {
        if (_stack.Count == 0) return;
        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        top.OnClosed();
        if (_stack.Count == 0) return;
        // A covered screen's clock stood still; brought up again it reads the shell's time, and a click before it was covered is not the first of a double click.
        CurrentScreen.Tree.AdvanceTo((long)_clockMs);
        CurrentScreen.Tree.ForgetLastClick();
        if (reveal) CurrentScreen.OnRevealed();
    }

    private void Replace(ShellScreen screen)
    {
        while (_stack.Count > 0) Pop(reveal: false);
        Push(screen);
    }
}

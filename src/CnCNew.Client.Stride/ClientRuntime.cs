using System.Numerics;
using CnCNew.Client.Stride.Rendering;
using CnCNew.Client.Stride.Ui;
using CnCNew.Game.Campaign;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Graphics;

namespace CnCNew.Client.Stride;

/// <summary>
/// What the Stride shell around the presentation layer does every frame, in one place: input from the device is translated and handed to whoever owns the screen (the shell's
/// menus, or the HUD of the running match or mission), the 3D world layers are refreshed by <see cref="LiveMatchHost"/>, and the 2D lists the presentation layer builds
/// (<see cref="GameShell.BuildDrawList"/>, <see cref="MatchController.BuildDrawList"/>, <see cref="MissionController.BuildDrawList"/>) are drawn over it with one
/// <see cref="UiRenderer"/>. The shell's outputs (<see cref="StartMatchOutput"/>, <see cref="StartMissionOutput"/>, <see cref="ExitRequestedOutput"/>, the display and settings
/// changes) are carried out here, and a finished match or mission is reported back to the shell exactly once (<see cref="GameShell.MatchFinished"/>,
/// <see cref="GameShell.MissionFinished"/>). The runtime never touches simulation state; it only reads what the controllers expose and forwards typed input.
/// </summary>
internal sealed class ClientRuntime : IDisposable
{
    private readonly global::Stride.Engine.Game _game;
    private readonly GameData _data;
    private readonly UiRenderer _ui;
    private readonly StrideInputTranslator _input = new();
    private readonly GameShell? _shell;
    private GameSettings _settings;
    private RunningGame? _running;
    private long _clockMs;
    private double _nextDots;
    private readonly SpriteBatch _minimapBatch;

    private ClientRuntime(global::Stride.Engine.Game game, GameData data, GameSettings settings, GameShell? shell)
    {
        _game = game;
        _data = data;
        _settings = settings;
        _shell = shell;
        _ui = new UiRenderer(game.GraphicsDevice, data.Content, data.Images, "English");
        _minimapBatch = new SpriteBatch(game.GraphicsDevice);
        _ui.CustomDraw = DrawCustom;
    }

    /// <summary>The shell (main menu and everything behind it); it starts matches and missions itself.</summary>
    public static ClientRuntime ForShell(global::Stride.Engine.Game game, GameData data, string settingsDirectory, int width, int height)
    {
        var shell = GameShell.Create(data, new FixedShellEnvironment(settingsDirectory, width, height), "English", new CnCNew.Ui.Layout.UiViewport(width, height));
        var runtime = new ClientRuntime(game, data, shell.Settings, shell);
        return runtime;
    }

    /// <summary>A skirmish match started from the command line, with its HUD.</summary>
    public static ClientRuntime ForMatch(global::Stride.Engine.Game game, GameData data, LiveMatch match)
    {
        var runtime = new ClientRuntime(game, data, GameSettings.Default, null);
        runtime._running = runtime.Start(match, null);
        return runtime;
    }

    /// <summary>A campaign mission started from the command line, with its HUD.</summary>
    public static ClientRuntime ForMission(global::Stride.Engine.Game game, GameData data, LiveMission mission)
    {
        var runtime = new ClientRuntime(game, data, GameSettings.Default, null);
        runtime._running = runtime.Start(mission, null);
        return runtime;
    }

    /// <summary>Selects every object the human owns in the running match (a capture aid, <c>--select-own</c>).</summary>
    public void SelectOwn() => _selectOwnPending = true;

    private bool _selectOwnPending;

    private void ApplyPendingSelection()
    {
        if (!_selectOwnPending || _running?.Match is not { } hud) return;
        var human = _running.Host.Match.Human;
        var own = _running.Host.Match.World.Entities.Where(entity => entity.Owner == human && !entity.EffectivelyDead).OrderBy(entity => entity.Id.Value).Select(entity => entity.Id).ToArray();
        if (own.Length == 0) return; // the start objects appear with the first simulated tick
        _selectOwnPending = false;
        hud.Input.Selection.Replace(own);
    }

    public bool ExitRequested { get; private set; }

    /// <summary>The running game's world host, or <see langword="null"/> while the shell is showing (lets the caller title the window).</summary>
    public LiveGame? Game => _running?.Host.Match;

    /// <summary>Why the display should change (a new resolution chosen in the options screen), or <see langword="null"/>.</summary>
    public (int Width, int Height)? PendingResolution { get; private set; }

    /// <summary>The overlay text a headless run can read (smoke tests): how many UI commands the last frame drew and how many custom draws were left to the host.</summary>
    public UiRenderStats LastFrame => _ui.LastFrame;

    public void Update(GameTime gameTime, InputManager input, int width, int height, double? fixedSeconds)
    {
        var seconds = fixedSeconds ?? gameTime.Elapsed.TotalSeconds;
        _clockMs += (long)Math.Round(seconds * 1000);
        var events = input.HasMouse || input.Events.Count > 0 ? _input.Poll(input, _clockMs) : [];
        if (_running is { } running)
        {
            running.Step(events, seconds, width, height, gameTime, _game.GraphicsContext.CommandList, fixedSeconds);
            ApplyPendingSelection();
            if (_clockMs >= _nextDots)
            {
                _nextDots = _clockMs + 250;
                running.Host.UpdateMinimap(_game.GraphicsContext.CommandList, MinimapDots(running.Host.Match.Frame));
            }
            if (running.TakeFinished() && _shell is not null) FinishRunning(running);
            return;
        }
        if (_shell is null) return;
        if (width > 0 && height > 0 && (_shell.Viewport.Width != width || _shell.Viewport.Height != height)) _shell.Resize(width, height);
        foreach (var inputEvent in events) _shell.HandleInput(inputEvent);
        _shell.Update(seconds);
        foreach (var output in _shell.DrainOutputs()) Carry(output);
    }

    private void Carry(ShellOutput output)
    {
        switch (output)
        {
            case StartMatchOutput start:
            {
                var size = BackBufferSize();
                _running = Start(LiveMatch.Create(_data, start.Setup, size), null);
                break;
            }
            case StartMissionOutput mission:
            {
                var text = _data.LoadText("English");
                var live = LiveMission.Create(_data, mission.Run, BackBufferSize(), label => text.TryGet(label, out var resolved) ? resolved : label);
                _running = Start(live, mission);
                break;
            }
            case ExitRequestedOutput:
                ExitRequested = true;
                break;
            case SettingsChangedOutput changed:
                _settings = changed.Settings;
                break;
            case ApplyResolutionOutput resolution:
                PendingResolution = (resolution.Width, resolution.Height);
                break;
        }
    }

    /// <summary>One dot per object the viewer currently sees, in its team colour (the projector already left out what fog and shroud hide).</summary>
    private static List<CnCNew.Presentation.Minimap.MinimapDot> MinimapDots(CnCNew.Presentation.Objects.RenderFrame frame)
    {
        var dots = new List<CnCNew.Presentation.Minimap.MinimapDot>();
        foreach (var drawable in frame.Drawables)
            if (drawable.Entity.IsValid && (drawable.IsUnit || drawable.IsStructure))
                dots.Add(new CnCNew.Presentation.Minimap.MinimapDot(new Vector2(drawable.Position.X, drawable.Position.Y), new CnCNew.Presentation.Terrain.Rgba32(drawable.TeamColor.Red, drawable.TeamColor.Green, drawable.TeamColor.Blue, 255)));
        return dots;
    }

    private Vector2 BackBufferSize()
    {
        var back = _game.GraphicsDevice.Presenter.BackBuffer;
        return new Vector2(back.Width, back.Height);
    }

    private RunningGame Start(LiveGame game, StartMissionOutput? mission)
    {
        var host = new LiveMatchHost(_game, _data, game);
        var running = game switch
        {
            LiveMission live => new RunningGame(host, null, new MissionController(live, _settings, "English", _ui.TextMeasure), mission),
            _ => new RunningGame(host, new MatchController(game, _settings, "English", _ui.TextMeasure), null, null),
        };
        running.Attach();
        return running;
    }

    /// <summary>The match or mission is over: tell the shell (which shows the score or the banner and result screens), drop the world and show the shell again.</summary>
    private void FinishRunning(RunningGame running)
    {
        _running = null;
        var game = running.Host.Match;
        if (running.Mission is not null && running.Start is { } start)
            _shell!.MissionFinished(start.Run, start.Controller);
        else if (game is LiveMatch match)
            _shell!.MatchFinished(match.Session, running.Statistics);
        _game.SceneSystem.SceneInstance = null;
        running.Dispose();
    }

    /// <summary>
    /// The windows the control bar leaves to the host (<c>docs\GAME-HUD.md</c> §5): <c>W3DLeftHUDDraw</c> is the minimap frame, drawn letterboxed with the aspect-preserving rule
    /// <see cref="MatchController"/> uses to turn clicks back into world positions; <c>Viewport3D</c> needs nothing (the world is already behind the HUD); every other callback is
    /// counted by the renderer and stays undrawn.
    /// </summary>
    private void DrawCustom(GraphicsContext context, UiCustomDraw draw)
    {
        if (draw.Callback != "W3DLeftHUDDraw" || _running is not { } running) return;
        var image = running.Host.Match.Minimap;
        var scale = Math.Min(draw.Rect.Width / (float)image.Width, draw.Rect.Height / (float)image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        var destination = new global::Stride.Core.Mathematics.RectangleF(draw.Rect.X + (draw.Rect.Width - width) / 2, draw.Rect.Y + (draw.Rect.Height - height) / 2, width, height);
        _minimapBatch.Begin(context, SpriteSortMode.Deferred, BlendStates.Opaque, _game.GraphicsDevice.SamplerStates.LinearClamp, DepthStencilStates.None);
        _minimapBatch.Draw(running.Host.MinimapTexture, destination, global::Stride.Core.Mathematics.Color.White);
        _minimapBatch.End();
    }

    public void Draw(GraphicsContext context, GraphicsDevice device)
    {
        var back = device.Presenter.BackBuffer;
        var commandList = context.CommandList;
        commandList.SetRenderTargetAndViewport(null, back);
        if (_running is null) commandList.Clear(back, global::Stride.Core.Mathematics.Color.Black);
        var list = _running is { } running ? running.BuildDrawList(back.Width, back.Height) : _shell?.BuildDrawList();
        if (list is not null) _ui.Draw(context, list, back.Width, back.Height);
    }

    public void Dispose()
    {
        _running?.Dispose();
        _running = null;
        _minimapBatch.Dispose();
        _ui.Dispose();
    }

    /// <summary>One running match or mission: the world host, the HUD controller that drives it and the way it ends.</summary>
    private sealed class RunningGame(LiveMatchHost host, MatchController? match, MissionController? mission, StartMissionOutput? start) : IDisposable
    {
        private bool _finishedReported;

        public void Attach()
        {
            if (Host.Match is LiveMatch) Host.Match.TickStepped += result => Statistics.Record(result);
        }

        public LiveMatchHost Host { get; } = host;
        public MatchController? Match { get; } = match;
        public MissionController? Mission { get; } = mission;
        public StartMissionOutput? Start { get; } = start;

        /// <summary>The per-player counts the score screen shows, fed by every simulated tick of a skirmish match.</summary>
        public MatchStatistics Statistics { get; } = new();
        private MatchController Hud => Match ?? Mission!.Hud;

        /// <summary>Input first, then the simulation and HUD clocks, then the world layers; a mission's controller advances the match itself.</summary>
        public void Step(List<CnCNew.Presentation.Input.InputEvent> events, double seconds, int width, int height, GameTime gameTime, CommandList commandList, double? fixedSeconds)
        {
            foreach (var inputEvent in events)
            {
                if (Mission is { } missionController) missionController.Process(inputEvent);
                else Match!.Process(inputEvent);
            }
            Host.Update(gameTime, commandList, width, height, fixedSeconds, elapsed =>
            {
                if (Mission is { } missionController) missionController.Update(elapsed);
                else
                {
                    Host.Match.Advance(elapsed);
                    Match!.Update(elapsed);
                }
            });
        }

        /// <summary><see langword="true"/> once, the frame the match or mission is over and its end-game delay has passed.</summary>
        public bool TakeFinished()
        {
            if (_finishedReported || !Host.Match.IsOver) return false;
            if (Mission is { } controller && controller.Mission.Outcome == CnCNew.Simulation.Scripting.MissionOutcome.None) return false;
            _finishedReported = true;
            return true;
        }

        public CnCNew.Ui.Runtime.UiDrawList BuildDrawList(int width, int height) => Mission is { } controller ? controller.BuildDrawList(width, height) : Match!.BuildDrawList(width, height);

        public void Dispose() => Host.Dispose();
    }
}

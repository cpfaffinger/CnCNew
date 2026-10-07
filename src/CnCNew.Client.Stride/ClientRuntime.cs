using System.Numerics;
using CnCNew.Client.Stride.Audio;
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
/// Sound lives as long as the runtime (<see cref="ClientAudio"/>): the shell's menu theme and clicks, and for a running game everything <c>GameAudio.Attach</c> connects.
/// </summary>
internal sealed class ClientRuntime : IDisposable
{
    private readonly global::Stride.Engine.Game _game;
    private readonly GameData _data;
    private readonly UiRenderer _ui;
    private readonly StrideInputTranslator _input = new();
    private readonly GameShell? _shell;
    private GameSettings _settings;
    // --mouse classic|alternate: overrides the mouse setup of the settings for every game this run starts (null: the settings decide).
    private bool? _alternateMouse;
    private GameSettings GameplaySettings => _alternateMouse is { } alternate ? _settings with { Controls = _settings.Controls with { AlternateMouse = alternate } } : _settings;
    private RunningGame? _running;
    private long _clockMs;
    private double _nextDots;
    private readonly SpriteBatch _minimapBatch;
    private readonly ClientAudio _audio;

    private ClientRuntime(global::Stride.Engine.Game game, GameData data, GameSettings settings, Func<UiRenderer, GameShell>? shell, ClientAudioOptions audio)
    {
        _game = game;
        _data = data;
        _ui = new UiRenderer(game.GraphicsDevice, data.Content, data.Images, "English");
        // The shell lays its text out with the renderer's own glyph metrics; with any other measure labels sit off centre.
        _shell = shell?.Invoke(_ui);
        _settings = _shell?.Settings ?? settings;
        _audio = new ClientAudio(data, _settings.Audio, audio);
        _minimapBatch = new SpriteBatch(game.GraphicsDevice);
        _filters = new ScreenFilters(game.GraphicsDevice);
        _ui.CustomDraw = DrawCustom;
        _cursor = new SoftwareCursor(game.GraphicsDevice, data);
    }

    private readonly SoftwareCursor _cursor;
    private readonly ScreenFilters _filters;
    private Vector2? _pointer;
    private Vector2? _stagedPointer;
    private (float X, float Y)[]? _stagedWaypoints;

    /// <summary>The cursor line of the console report (cursors loaded, missing files, frames, the cursor of the last frame).</summary>
    public string CursorReport => _cursor.Report();

    /// <summary>
    /// A capture aid (<c>--cursor-at x,y</c>): a fixed-step run takes no device input, so the pointer is put here by a synthetic mouse move every frame,
    /// through the same event path a real mouse takes.
    /// </summary>
    public void StagePointer(Vector2 position) => _stagedPointer = position;

    /// <summary>
    /// A capture aid (<c>--waypoints "dx,dy;dx,dy;..."</c>, with <c>--select-own</c>): once the selection is made, Alt is held and one waypoint per offset
    /// (metres from the first selected unit that can move) is ordered through the input layer, as a player's Alt-clicks would.
    /// </summary>
    public void StageWaypoints((float X, float Y)[] offsets) => _stagedWaypoints = offsets;

    /// <summary>The shell (main menu and everything behind it); it starts matches and missions itself.</summary>
    public static ClientRuntime ForShell(global::Stride.Engine.Game game, GameData data, string settingsDirectory, int width, int height, ClientAudioOptions audio, bool animateTransitions = true, bool? alternateMouse = null)
    {
        // animateTransitions: the menu animates like the original (Data/INI/WindowTransitions.ini, docs/GAME-SHELL.md section 11); --no-transitions switches to hard cuts.
        var runtime = new ClientRuntime(game, data, GameSettings.Default,
            ui => GameShell.Create(data, new FixedShellEnvironment(settingsDirectory, width, height), "English", new CnCNew.Ui.Layout.UiViewport(width, height), ui.TextMeasure, animateTransitions), audio) { _alternateMouse = alternateMouse };
        runtime._audio.Game.EnterShell();
        return runtime;
    }

    /// <summary>A skirmish match started from the command line, with its HUD.</summary>
    public static ClientRuntime ForMatch(global::Stride.Engine.Game game, GameData data, LiveMatch match, ClientAudioOptions audio, bool? alternateMouse = null)
    {
        var runtime = new ClientRuntime(game, data, GameSettings.Default, null, audio) { _alternateMouse = alternateMouse };
        runtime._running = runtime.Start(match, null);
        return runtime;
    }

    /// <summary>A staged demonstration (<c>--scenario</c>), with the skirmish HUD.</summary>
    public static ClientRuntime ForScenario(global::Stride.Engine.Game game, GameData data, LiveScenario scenario, ClientAudioOptions audio, bool? alternateMouse = null)
    {
        var runtime = new ClientRuntime(game, data, GameSettings.Default, null, audio) { _alternateMouse = alternateMouse };
        runtime._running = runtime.Start(scenario, null);
        return runtime;
    }

    /// <summary>A campaign mission started from the command line, with its HUD.</summary>
    public static ClientRuntime ForMission(global::Stride.Engine.Game game, GameData data, LiveMission mission, ClientAudioOptions audio, bool? alternateMouse = null)
    {
        var runtime = new ClientRuntime(game, data, GameSettings.Default, null, audio) { _alternateMouse = alternateMouse };
        runtime._running = runtime.Start(mission, null);
        return runtime;
    }

    /// <summary>Selects every object the human owns in the running match (a capture aid, <c>--select-own</c>).</summary>
    public void SelectOwn() => _selectOwnPending = true;

    private bool _selectOwnPending;

    private CnCNew.Client.Stride.Testing.InputScript? _script;
    private int _frame;

    /// <summary>Replays a scripted input sequence into the running game (<c>--input-script</c>, a test facility; see <see cref="CnCNew.Client.Stride.Testing.InputScript"/>).</summary>
    public void UseInputScript(CnCNew.Client.Stride.Testing.InputScript script)
    {
        if (_running is not { } running) throw new InvalidOperationException("--input-script needs a running match or mission (--match, --mission or --scenario).");
        _script = script;
        script.Attach(running.Host.Match, running.Hud);
    }

    /// <summary>
    /// What the mission did in this run, for the console: whether the session (and with it the script VM) stepped every logic tick, the viewer's side, the outcome, and per
    /// <c>ScriptOutput</c> type how often the scripts emitted it and how often the client showed it (<c>docs/SCRIPT-ENGINE.md</c> §7a). Empty without a mission.
    /// </summary>
    public IReadOnlyList<string> FinishMission(int journalLines)
    {
        if (_running?.Host.Match is not LiveMission mission) return [];
        var session = mission.Session;
        var engine = session.Engine;
        var lines = new List<string>
        {
            $"mission: map {mission.Run.MapPath}, human side '{session.Program.Players.FirstOrDefault(player => player.Id == mission.Human)?.Name}' (player {mission.Human.Value}, faction {mission.HumanFaction}), presentation latency {session.Options.PresentationLatencyTicks} original frames ({session.Options.PresentationLatencyTicks / CnCNew.Data.OriginalFrame.PerSecond} s)",
            $"mission: {_frame} frames, world tick {mission.Tick} at {mission.World.Time.Rate}, session steps {_running.MissionSteps} (script VM updates), outcome {mission.Outcome}, over {mission.IsOver}, missing action hits {engine.MissingActionHits.Values.Sum()}, missing condition hits {engine.MissingConditionHits.Values.Sum()}",
        };
        lines.AddRange(mission.Director.Report());
        lines.Add($"mission screen filters: {_filters.FramesFiltered} frames drawn through the grey-scale or the motion blur; camera shake offset at exit {mission.Camera.ShakeOffset.Length():F2}, border {mission.Camera.ActiveBoundary}");
        foreach (var entry in mission.Director.Journal.Take(journalLines)) lines.Add($"mission journal: {entry}");
        return lines;
    }

    /// <summary>The input replay's report for the console, or nothing without a script; call at exit.</summary>
    public IReadOnlyList<string> FinishInputScript() => _script?.Report() ?? [];

    /// <summary>Targets of the input script that could not be resolved (the process then exits with an error code).</summary>
    public int UnresolvedInputSteps => _script?.Unresolved ?? 0;

    private void ApplyPendingSelection()
    {
        if (!_selectOwnPending || _running?.Match is not { } hud) return;
        var human = _running.Host.Match.Human;
        var own = _running.Host.Match.World.Entities.Where(entity => entity.Owner == human && !entity.EffectivelyDead).OrderBy(entity => entity.Id.Value).Select(entity => entity.Id).ToArray();
        if (own.Length == 0) return; // the start objects appear with the first simulated tick
        _selectOwnPending = false;
        hud.Input.Selection.Replace(own);
        if (_stagedWaypoints is not { } offsets) return;
        _stagedWaypoints = null;
        var world = _running.Host.Match.World;
        if (hud.Input.Selection.OwnSelected(world).FirstOrDefault(SimulationQueries.CanMove) is not { } mover)
        {
            Console.Error.WriteLine("warning: --waypoints: nothing in the selection can move; no waypoint was ordered.");
            return;
        }
        var origin = SimulationQueries.GroundOf(mover);
        hud.Input.DispatchMeta(MetaAction.BeginWaypoints, world, _running.Host.Match.Camera);
        foreach (var (dx, dy) in offsets)
            hud.Input.Commands.IssueContext(world, new ContextHint(ContextAction.AddWaypoint, CursorKind.Waypoint, CnCNew.Simulation.Ids.EntityId.Invalid, new System.Numerics.Vector3(origin.X + dx, origin.Y + dy, 0f)), null);
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
        // A fixed-step run (smoke test, captures) takes no device input: wherever the real mouse happens to be must not scroll the camera, or neither the
        // picture nor the sound (the listener follows the camera) of two runs would be the same.
        var events = fixedSeconds is null && (input.HasMouse || input.Events.Count > 0) ? _input.Poll(input, _clockMs, width, height) : [];
        _frame++;
        if (fixedSeconds is null)
        {
            // The cursor is drawn where the device says the pointer is (in back-buffer pixels, like the events), inside the window only; the operating system cursor is hidden while ours can stand in.
            var position = new Vector2(input.MousePosition.X * width, input.MousePosition.Y * height);
            _pointer = input.HasMouse && position.X >= 0 && position.Y >= 0 && position.X < width && position.Y < height ? position : null;
            if (_game.IsMouseVisible == _cursor.Usable) _game.IsMouseVisible = !_cursor.Usable;
        }
        else if (_stagedPointer is { } staged)
        {
            events.Add(new CnCNew.Presentation.Input.MouseMoveEvent(_clockMs, staged));
            _pointer = staged;
        }
        // Scripted input enters exactly where the device's does, so it meets the same HUD layout, text measure and update/draw order.
        if (_script is not null && _running is not null) events.AddRange(_script.EventsFor(_frame, _clockMs));
        if (_running is { } running)
        {
            running.Step(events, seconds, width, height, gameTime, _game.GraphicsContext.CommandList, fixedSeconds);
            _script?.Observe();
            ApplyPendingSelection();
            if (_clockMs >= _nextDots)
            {
                _nextDots = _clockMs + 250;
                var dots = MinimapDots(running.Host.Match.Frame);
                // Radar events of the mission scripts: a white dot that blinks at the minimap's refresh rate while the event lives.
                if (running.Host.Match is LiveMission mission && _clockMs / 250 % 2 == 0)
                    foreach (var blip in mission.Director.RadarBlipsForDisplay())
                        dots.Add(new CnCNew.Presentation.Minimap.MinimapDot(new Vector2(blip.X, blip.Y), new CnCNew.Presentation.Terrain.Rgba32(255, 255, 255, 255)));
                running.Host.UpdateMinimap(_game.GraphicsContext.CommandList, dots);
            }
            if (running.TakeFinished() && _shell is not null) FinishRunning(running);
            _audio.Update(seconds);
            return;
        }
        if (_shell is not null)
        {
            if (width > 0 && height > 0 && (_shell.Viewport.Width != width || _shell.Viewport.Height != height)) _shell.Resize(width, height);
            foreach (var inputEvent in events) _shell.HandleInput(inputEvent);
            _shell.Update(seconds);
            foreach (var output in _shell.DrainOutputs()) Carry(output);
        }
        _audio.Update(seconds);
    }

    /// <summary>Closes an audio capture and returns the audio report of the run (output, sources, mixer, everything that could not be played) for the console; call at exit.</summary>
    public IReadOnlyList<string> FinishAudio() => _audio.Finish();

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
                _audio.Game.ApplySettings(changed.Settings.Audio);
                break;
            case PlayUiSoundOutput sound:
                _audio.Game.PlayUi(sound.EventName);
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
            LiveMission live => new RunningGame(host, null, new MissionController(live, GameplaySettings, "English", _ui.TextMeasure), mission),
            _ => new RunningGame(host, new MatchController(game, GameplaySettings, "English", _ui.TextMeasure), null, null),
        };
        running.Attach();
        _audio.Game.Attach(game, running.Hud);
        // The mission scripts' speech, sounds, music and volumes go to the same director as everything else.
        if (game is LiveMission scripted) scripted.Director.AudioControl = _audio.Game;
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
        _audio.Game.EnterShell();
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
        // The mission scripts' screen filters (grey-scale, motion blur) go over the finished world and under the HUD. Asking for them is what makes the director count them as displayed.
        if (_running?.Host.Match is LiveMission filtered && filtered.Director.PostEffects is { Any: true } effects) _filters.Apply(context, back, effects);
        var list = _running is { } running ? running.BuildDrawList(back.Width, back.Height) : _shell?.BuildDrawList();
        _ui.Begin(context, back.Width, back.Height);
        if (list is not null)
            foreach (var command in list.Commands) _ui.Execute(command);
        // The cursor lies on top of everything: the shell and the in-game menu show the arrow, a running game its context cursor.
        if (_pointer is { } pointer) _cursor.Draw(_ui, _running?.Hud.Cursor ?? CursorState.Arrow, pointer, _clockMs);
        _ui.End();
    }

    public void Dispose()
    {
        _running?.Dispose();
        _running = null;
        _audio.Dispose();
        _cursor.Dispose();
        _filters.Dispose();
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
            if (Host.Match is LiveMission) Host.Match.TickStepped += _ => MissionSteps++;
        }

        /// <summary>How often the mission session stepped (each step runs the script VM once): equal to the world tick when the scripts ran every logic tick.</summary>
        public long MissionSteps { get; private set; }

        public LiveMatchHost Host { get; } = host;
        public MatchController? Match { get; } = match;
        public MissionController? Mission { get; } = mission;
        public StartMissionOutput? Start { get; } = start;

        /// <summary>The per-player counts the score screen shows, fed by every simulated tick of a skirmish match.</summary>
        public MatchStatistics Statistics { get; } = new();
        public MatchController Hud => Match ?? Mission!.Hud;

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

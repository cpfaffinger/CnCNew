using System.Security.Cryptography;
using System.Collections.Immutable;
using CnCNew.Client.Stride;
using CnCNew.Client.Stride.Rendering;
using CnCNew.Content;
using CnCNew.Game.Campaign;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Testing;
using CnCNew.W3D;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;

var smokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
var contentDirectory = GetArgument(args, "--content-dir");
var modelPath = GetArgument(args, "--model-path");
var capturePath = GetArgument(args, "--capture");
var matchMap = GetArgument(args, "--match");
// --list-scenarios: the staged scenes --scenario accepts, one per line (name, title), and nothing else; scripts/demo-scenario.ps1 shows this when it is called without a name.
if (args.Contains("--list-scenarios", StringComparer.OrdinalIgnoreCase))
{
    foreach (var entry in CnCNew.Game.Live.DemoScenarios.All) Console.WriteLine($"{entry.Name,-26} {entry.Title}");
    return;
}
var scenarioName = GetArgument(args, "--scenario");
var scenario = scenarioName is null ? null : CnCNew.Game.Live.DemoScenarios.Find(scenarioName)
    ?? throw new ArgumentException($"Unknown scenario '{scenarioName}'. Known: {string.Join(", ", CnCNew.Game.Live.DemoScenarios.All.Select(value => value.Name))}.");
if (scenario is not null) Console.WriteLine($"scenario {scenario.Name}: {scenario.Title}. {scenario.Description}");
if (scenario is not null) matchMap = scenario.Map;
var missionMap = GetArgument(args, "--mission");
// --input-script FILE replays scripted pointer/key input through the real client path (a test facility, docs/GAME-HUD.md §17); fixed time steps only.
var inputScriptPath = GetArgument(args, "--input-script");
// --tick-rate N: the simulation rate of the session (30, 60 or 120 Hz; the default is the game layer's, 30 Hz until the product rate changes). Everything shown and heard
// lasts the same game time at every rate; an unsupported rate is refused by the simulation's own message.
var tickRate = GetArgument(args, "--tick-rate") is { } rateText
    ? CnCNew.Simulation.Time.SimulationRate.FromTicksPerSecond(int.Parse(rateText, System.Globalization.CultureInfo.InvariantCulture))
    : GameData.DefaultSessionRate;
// --mouse classic|alternate: the mouse setup of this run (alternate, the default: left selects, right orders; classic: the original's default, left selects and orders).
bool? alternateMouse = GetArgument(args, "--mouse")?.ToLowerInvariant() switch
{
    null => null,
    "alternate" => true,
    "classic" => false,
    var other => throw new ArgumentException($"--mouse takes 'alternate' or 'classic', not '{other}'."),
};
// --audio-capture DIR renders the mixer into DIR/audio.wav instead of a device; a smoke test never opens a device (it renders and discards unless a capture is asked for).
var audioOptions = new CnCNew.Client.Stride.Audio.ClientAudioOptions(smokeTest, GetArgument(args, "--audio-capture"), args.Contains("--mute", StringComparer.OrdinalIgnoreCase));
var shellMode = args.Contains("--shell", StringComparer.OrdinalIgnoreCase) || (contentDirectory is not null && modelPath is null && matchMap is null && missionMap is null);
var liveOptions = (matchMap ?? missionMap) is null ? null : new LiveOptions(
    (matchMap ?? missionMap)!,
    GetArgument(args, "--faction") ?? "FactionAmerica",
    (GetArgument(args, "--opponent") ?? "FactionChina").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToImmutableArray(),
    int.Parse(GetArgument(args, "--seed") ?? "1"),
    int.Parse(GetArgument(args, "--frames") ?? "90"),
    GetArgument(args, "--size") ?? "1280x720",
    args.Contains("--autoplay", StringComparer.OrdinalIgnoreCase),
    GetArgument(args, "--look"),
    GetArgument(args, "--angle"),
    GetArgument(args, "--pitch"),
    GetArgument(args, "--height"))
{
    IsMission = missionMap is not null,
    HumanSide = GetArgument(args, "--human-side") ?? string.Empty,
    Scenario = scenario,
    SelectOwn = args.Contains("--select-own", StringComparer.OrdinalIgnoreCase),
    // --spectate: nobody plays; --faction and --opponent are both computer players and the observer HUD replaces the player's (docs/SPECTATOR-MODE.md).
    Spectate = args.Contains("--spectate", StringComparer.OrdinalIgnoreCase),
    SpectatorMaxZoom = GetArgument(args, "--spectator-max-zoom") is { } maxZoom ? float.Parse(maxZoom, System.Globalization.CultureInfo.InvariantCulture) : null,
    SpectatorSpeed = GetArgument(args, "--spectator-speed") is { } speed ? double.Parse(speed, System.Globalization.CultureInfo.InvariantCulture) : 1,
};
if (liveOptions is { Spectate: true } && (matchMap is null || scenario is not null))
{
    throw new ArgumentException("--spectate requires --match.");
}

if (matchMap is not null && missionMap is not null)
{
    throw new ArgumentException("--match and --mission exclude each other.");
}

if (shellMode && (matchMap is not null || missionMap is not null || modelPath is not null))
{
    throw new ArgumentException("--shell excludes --match, --mission and --model-path.");
}

if ((matchMap ?? missionMap) is not null && contentDirectory is null)
{
    throw new ArgumentException("--match and --mission require --content-dir.");
}

if (modelPath is not null && contentDirectory is null)
{
    throw new ArgumentException("--model-path requires --content-dir.");
}

if (inputScriptPath is not null && (!smokeTest || (matchMap ?? missionMap) is null))
{
    throw new ArgumentException("--input-script requires --smoke-test and one of --match, --mission or --scenario.");
}

if (capturePath is not null && !smokeTest)
{
    throw new ArgumentException("--capture requires --smoke-test.");
}

if (contentDirectory is not null)
{
    var content = ContentFileSystem.Open(contentDirectory);
    if (content.FileCount == 0 || content.EnumerateFiles().Any(path => path.EndsWith(".big", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidDataException("The runtime content directory is empty or still contains BIG archives.");
    }
}

using (var game = new CnCNewGame(smokeTest, contentDirectory, modelPath, capturePath, liveOptions, shellMode, GetArgument(args, "--size") ?? "1280x720", int.Parse(GetArgument(args, "--frames") ?? "90"),
    audioOptions, GetArgument(args, "--filmstrip"), int.Parse(GetArgument(args, "--every") ?? "30"), GetArgument(args, "--cursor-at"), GetArgument(args, "--waypoints"),
    inputScriptPath is null ? null : CnCNew.Client.Stride.Testing.InputScript.Load(inputScriptPath), int.Parse(GetArgument(args, "--mission-journal") ?? "0"),
    !args.Contains("--no-transitions", StringComparer.OrdinalIgnoreCase), tickRate, alternateMouse))
{
    game.Run();
    if (game.UnresolvedInputSteps > 0) Environment.ExitCode = 3;
}

if (capturePath is not null)
{
    using var capture = File.OpenRead(Path.GetFullPath(capturePath));
    Console.WriteLine($"Captured '{capturePath}' SHA-256={Convert.ToHexString(SHA256.HashData(capture))}.");
}

static string? GetArgument(string[] arguments, string name)
{
    var index = Array.FindIndex(arguments, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
    {
        return null;
    }

    return index + 1 < arguments.Length
        ? arguments[index + 1]
        : throw new ArgumentException($"{name} requires a value.");
}

/// <param name="Map">Virtual map path or a unique part of it (for example "Alpine Assault").</param>
/// <param name="Frames">Smoke test only: rendered frames before the capture; each is one original frame (1/30 s) of game time at every tick rate.</param>
/// <param name="AutoPlay">The human slot is played by the computer (--autoplay).</param>
/// <param name="Look">Camera target "x,y" in world units (--look); Angle/Pitch in degrees, Height in world units above ground.</param>
internal sealed record LiveOptions(string Map, string Faction, ImmutableArray<string> Opponents, int Seed, int Frames, string Size,
    bool AutoPlay, string? Look, string? Angle, string? Pitch, string? Height)
{
    /// <summary>The map is a campaign mission played by its own scripts (--mission) instead of a skirmish (--match).</summary>
    public bool IsMission { get; init; }

    /// <summary>The side the human plays in a mission whose map marks none (--human-side; see <c>MissionOptions.HumanSideName</c>).</summary>
    public string HumanSide { get; init; } = string.Empty;

    /// <summary>Selects everything the human owns once the match has started (--select-own; for captures of the selection feedback, a player would click).</summary>
    public bool SelectOwn { get; init; }

    /// <summary>A staged demonstration instead of a skirmish (--scenario NAME; the map is the scenario's).</summary>
    public CnCNew.Game.Live.DemoScenario? Scenario { get; init; }

    /// <summary>Nobody plays the skirmish (--spectate): every slot is a computer player, the viewer watches with the observer HUD.</summary>
    public bool Spectate { get; init; }

    /// <summary>The spectator's zoom-out limit as a height above ground (--spectator-max-zoom); null: the height from which the whole map is on screen.</summary>
    public float? SpectatorMaxZoom { get; init; }

    /// <summary>The game speed a spectated match starts with (--spectator-speed; 1 is real time).</summary>
    public double SpectatorSpeed { get; init; } = 1;
}

internal sealed class CnCNewGame(
    bool smokeTest,
    string? contentDirectory,
    string? modelPath,
    string? capturePath,
    LiveOptions? liveOptions,
    bool shellMode,
    string windowSize,
    int smokeFrames,
    CnCNew.Client.Stride.Audio.ClientAudioOptions audioOptions,
    string? filmstripDirectory = null,
    int filmstripEvery = 30,
    string? cursorAt = null,
    string? waypoints = null,
    CnCNew.Client.Stride.Testing.InputScript? inputScript = null,
    int missionJournalLines = 0,
    bool animateShell = true,
    CnCNew.Simulation.Time.SimulationRate? tickRate = null,
    bool? alternateMouse = null) : Game
{
    /// <summary>
    /// The fixed step of a smoke test: every rendered frame advances the game by one original frame (1/30 s), whatever the tick rate - one tick at 30 Hz, two at 60 Hz.
    /// So <c>--frames</c>, <c>--every</c> and the frame numbers of an input script mean the same game time at every rate, and a capture at frame N shows the same moment.
    /// </summary>
    private const double SmokeStepSeconds = 1.0 / CnCNew.Data.OriginalFrame.PerSecond;

    public int UnresolvedInputSteps { get; private set; }

    private ClientRuntime? _runtime;

    /// <summary>The capture aids of a fixed-step run: --cursor-at "x,y" (pointer in window pixels) and --waypoints "dx,dy;dx,dy;..." (with --select-own).</summary>
    private void ApplyStaging()
    {
        static float Number(string text) => float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        if (_runtime is null) return;
        if (cursorAt is not null)
        {
            var parts = cursorAt.Split(',');
            if (parts.Length != 2) throw new ArgumentException("--cursor-at expects \"x,y\".");
            _runtime.StagePointer(new System.Numerics.Vector2(Number(parts[0]), Number(parts[1])));
        }
        if (waypoints is not null)
        {
            _runtime.StageWaypoints([..waypoints.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(pair =>
            {
                var parts = pair.Split(',');
                if (parts.Length != 2) throw new ArgumentException("--waypoints expects \"dx,dy;dx,dy;...\".");
                return (Number(parts[0]), Number(parts[1]));
            })]);
        }
    }
    private int _renderedFrames;
    private bool _captured;
    private int _lastFilmFrame = -1;
    private W3dStrideScene? _w3dScene;

    protected override void Initialize()
    {
        AutoLoadDefaultSettings = false;
        var size = windowSize.Split('x');
        GraphicsDeviceManager.PreferredBackBufferWidth = int.Parse(size[0]);
        GraphicsDeviceManager.PreferredBackBufferHeight = int.Parse(size[1]);
        base.Initialize();
        Window.Title = "CnCNew — Stride Direct3D 11 W3D Viewer";
    }

    protected override async Task LoadContent()
    {
        await base.LoadContent();
        // The UI and the pointer live in back-buffer pixels; a window client area of another size (desktop too small, display scaling) is worth knowing about.
        Console.WriteLine($"display: back buffer {GraphicsDevice.Presenter.BackBuffer.Width}x{GraphicsDevice.Presenter.BackBuffer.Height}, window client area {Window.ClientBounds.Width}x{Window.ClientBounds.Height}");
        if (liveOptions is not null)
        {
            LoadLiveMatch(liveOptions);
            ApplyStaging();
            if (inputScript is not null) _runtime!.UseInputScript(inputScript);
            return;
        }

        if (shellMode)
        {
            var data = GameData.Load(contentDirectory!, step => Console.WriteLine($"loading: {step}"), rate: tickRate);
            var back = GraphicsDevice.Presenter.BackBuffer;
            _runtime = ClientRuntime.ForShell(this, data, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CnCNew"), back.Width, back.Height, audioOptions, animateShell, alternateMouse);
            Window.Title = "CnCNew";
            ApplyStaging();
            return;
        }

        if (contentDirectory is null || modelPath is null)
        {
            return;
        }

        var content = ContentFileSystem.Open(contentDirectory);
        using var stream = content.OpenRead(modelPath);
        var sourceMeshes = W3dMeshDecoder.Decode(W3dFileReader.Read(stream))
            .Where(mesh => mesh.Triangles.Count > 0)
            .ToArray();
        if (sourceMeshes.Length == 0)
        {
            throw new InvalidDataException($"W3D model '{modelPath}' contains no renderable meshes.");
        }

        var scene = new Scene();
        _w3dScene = new W3dStrideScene(GraphicsDevice, content);
        scene.Entities.Add(_w3dScene.Build(sourceMeshes));

        var compositor = GraphicsCompositorHelper.CreateDefault(false);
        var meshFeature = compositor.RenderFeatures.OfType<MeshRenderFeature>().Single();
        var stageSelector = meshFeature.RenderStageSelectors.OfType<MeshTransparentRenderStageSelector>().Single();
        meshFeature.PipelineProcessors.Add(new W3dDepthWritePipelineProcessor(
            _w3dScene.DepthWrites,
            stageSelector.OpaqueRenderStage,
            stageSelector.TransparentRenderStage));
        SceneSystem.GraphicsCompositor = compositor;

        var camera = new CameraComponent
        {
            Slot = Services.GetSafeServiceAs<SceneSystem>().GraphicsCompositor.Cameras[0].ToSlotId(),
            NearClipPlane = 0.05f,
            FarClipPlane = 100,
        };
        var cameraEntity = new Entity("Camera") { camera };
        cameraEntity.Transform.Position = new Vector3(0, 0.35f, 4);
        scene.Entities.Add(cameraEntity);

        var lightEntity = new Entity("Key light") { new LightComponent() };
        lightEntity.Transform.Rotation = Quaternion.RotationYawPitchRoll(-0.8f, -0.7f, 0);
        scene.Entities.Add(lightEntity);
        SceneSystem.SceneInstance = new SceneInstance(Services, scene);
        Window.Title = $"CnCNew — {modelPath} — {sourceMeshes.Length} meshes — {_w3dScene.TextureCount} textures";

        if (smokeTest)
        {
            Console.WriteLine(
                $"Loaded {sourceMeshes.Length} W3D meshes as {_w3dScene.RenderBatchCount} ordered batches, " +
                $"{_w3dScene.MaterialPassCount} executed passes, {_w3dScene.TextureCount} cached textures, " +
                $"{_w3dScene.AnimatedBufferCount} animated mapper buffers and {_w3dScene.MissingTextureCount} missing textures.");
            if (_w3dScene.MissingTextureCount > 0)
            {
                throw new InvalidDataException(
                    $"Smoke model '{modelPath}' has {_w3dScene.MissingTextureCount} unresolved texture reference(s).");
            }
        }
    }

    private void LoadLiveMatch(LiveOptions options)
    {
        var data = GameData.Load(contentDirectory!, step => Console.WriteLine($"loading: {step}"), rate: tickRate);
        Console.WriteLine($"session: simulation rate {data.Rate}");
        var mapPath = data.Maps.FirstOrDefault(path => path.Equals(options.Map, StringComparison.OrdinalIgnoreCase))
            ?? data.Maps.SingleOrDefault(path => path.Contains(options.Map, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Map '{options.Map}' was not found or is ambiguous.");
        if (options.IsMission)
        {
            var viewport = GraphicsDevice.Presenter.BackBuffer;
            // The director reports when a scripted camera move, speech or movie really ended (CompletePresentation); the latency is only the bound after which the VM stops waiting.
            var run = MissionRun.Load(MissionContext.From(data), mapPath, MissionOptions.Standard with { HumanSideName = options.HumanSide, PresentationLatencyTicks = LiveMission.ClientPresentationLatencyFrames });
            var text = data.LoadText("English");
            var mission = LiveMission.Create(data, run, new System.Numerics.Vector2(viewport.Width, viewport.Height), label => text.TryGet(label, out var resolved) ? resolved : label);
            ApplyCamera(mission.Camera, options);
            _runtime = ClientRuntime.ForMission(this, data, mission, audioOptions, alternateMouse);
            Window.Title = $"CnCNew — mission {mapPath} — {mission.HumanFaction}";
            return;
        }
        var back = GraphicsDevice.Presenter.BackBuffer;
        if (options.Scenario is { } scenario)
        {
            var staged = LiveScenario.Create(data, scenario, new System.Numerics.Vector2(back.Width, back.Height));
            staged.Camera.CenterOn(staged.Anchor + new System.Numerics.Vector2(scenario.LookX, scenario.LookY));
            staged.Camera.SetAngle((options.Angle is { } angleText ? float.Parse(angleText, System.Globalization.CultureInfo.InvariantCulture) : scenario.CameraAngleDegrees) * MathF.PI / 180);
            staged.Camera.SetPitch((options.Pitch is { } pitchText ? float.Parse(pitchText, System.Globalization.CultureInfo.InvariantCulture) : scenario.CameraPitchDegrees) * MathF.PI / 180);
            staged.Camera.SetHeightAboveGround(options.Height is { } heightText ? float.Parse(heightText, System.Globalization.CultureInfo.InvariantCulture) : scenario.CameraHeight);
            staged.Camera.SettleZoom();
            ApplyCamera(staged.Camera, options with { Angle = null, Pitch = null, Height = null });
            _runtime = ClientRuntime.ForScenario(this, data, staged, audioOptions, alternateMouse);
            if (options.SelectOwn) _runtime.SelectOwn();
            Window.Title = $"CnCNew — {scenario.Name}: {scenario.Title}";
            return;
        }
        var setup = new MatchSetup(mapPath, options.Faction, options.Opponents, SkirmishMatchOptions.Standard) { Seed = options.Seed, AutoPlay = options.AutoPlay, Spectator = options.Spectate };
        var match = LiveMatch.Create(data, setup, new System.Numerics.Vector2(back.Width, back.Height));
        ApplyCamera(match.Camera, options);
        if (options.Spectate)
        {
            _runtime = ClientRuntime.ForSpectator(this, data, match, audioOptions, new CnCNew.Game.Spectator.SpectatorOptions(options.SpectatorMaxZoom, options.SpectatorSpeed, options.Seed));
            Console.WriteLine($"spectator: seed {options.Seed}, map {mapPath}, {options.Faction} against {string.Join(", ", options.Opponents)}");
            Window.Title = $"CnCNew — spectator — {mapPath} — {options.Faction} vs {string.Join(", ", options.Opponents)} — seed {options.Seed}";
            return;
        }
        _runtime = ClientRuntime.ForMatch(this, data, match, audioOptions, alternateMouse);
        if (options.SelectOwn) _runtime.SelectOwn();
        Window.Title = $"CnCNew — {mapPath} — {options.Faction} vs {string.Join(", ", options.Opponents)}";
    }

    private static void ApplyCamera(CnCNew.Presentation.Camera.RtsCamera camera, LiveOptions options)
    {
        static float Number(string text) => float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        if (options.Look is { } look)
        {
            var parts = look.Split(',');
            camera.CenterOn(new System.Numerics.Vector2(Number(parts[0]), Number(parts[1])));
        }

        if (options.Angle is { } angle) camera.SetAngle(Number(angle) * MathF.PI / 180);
        if (options.Pitch is { } pitch) camera.SetPitch(Number(pitch) * MathF.PI / 180);
        if (options.Height is { } height) camera.SetHeightAboveGround(Number(height));
        camera.SettleZoom();
    }

    protected override void UnloadContent()
    {
        if (_runtime is not null)
        {
            foreach (var line in _runtime.FinishAudio()) Console.WriteLine(line);
            foreach (var line in _runtime.FinishMission(missionJournalLines)) Console.WriteLine(line);
            foreach (var line in _runtime.FinishInputScript()) Console.WriteLine(line);
            foreach (var line in _runtime.FinishSpectator()) Console.WriteLine(line);
            UnresolvedInputSteps = _runtime.UnresolvedInputSteps;
        }
        _runtime?.Dispose();
        _runtime = null;
        _w3dScene?.Dispose();
        _w3dScene = null;
        base.UnloadContent();
    }

    private readonly System.Diagnostics.Stopwatch _costWatch = new();
    private double _updateMilliseconds;

    protected override void Update(GameTime gameTime)
    {
        _costWatch.Restart();
        UpdateFrame(gameTime);
        _updateMilliseconds = _costWatch.Elapsed.TotalMilliseconds;
    }

    private void UpdateFrame(GameTime gameTime)
    {
        base.Update(gameTime);
        if (_runtime is not null)
        {
            var back = GraphicsDevice.Presenter.BackBuffer;
            _runtime.Update(gameTime, Input, back.Width, back.Height, smokeTest ? SmokeStepSeconds : null);
            if (_runtime.Game is { } game && liveOptions is not { Spectate: true }) Window.Title = $"CnCNew — {game.HumanFaction}";
            if (_runtime.PendingResolution is { } resolution)
            {
                GraphicsDeviceManager.PreferredBackBufferWidth = resolution.Width;
                GraphicsDeviceManager.PreferredBackBufferHeight = resolution.Height;
                GraphicsDeviceManager.ApplyChanges();
            }
            if (_runtime.ExitRequested || (smokeTest && ++_renderedFrames >= smokeFrames && (capturePath is null || _captured)))
            {
                if (smokeTest) Console.WriteLine($"ui: {_runtime.LastFrame.Format()}; missing images [{string.Join(", ", _runtime.LastFrame.MissingImageNames.Take(12))}]; missing textures [{string.Join(", ", _runtime.LastFrame.MissingTextureFiles.Take(12))}]");
                if (smokeTest) Console.WriteLine($"cursor: {_runtime.CursorReport}");
                Exit();
            }

            return;
        }

        _w3dScene?.Update(
            GraphicsContext.CommandList,
            smokeTest ? _renderedFrames / 60d : gameTime.Total.TotalSeconds);
        if (smokeTest && ++_renderedFrames >= (capturePath is null ? 10 : 16) && (capturePath is null || _captured))
        {
            Exit();
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        _costWatch.Restart();
        base.Draw(gameTime);
        _runtime?.Draw(GraphicsContext, GraphicsDevice);
        _runtime?.NoteFrameCost(_updateMilliseconds, _costWatch.Elapsed.TotalMilliseconds);
        // --filmstrip DIR --every N: a PNG every N rendered frames (frame-00030.png ...), so a staged demo can be judged as a sequence without a window.
        if (filmstripDirectory is not null && filmstripEvery > 0 && _renderedFrames > 0 && _renderedFrames % filmstripEvery == 0 && _lastFilmFrame != _renderedFrames)
        {
            _lastFilmFrame = _renderedFrames;
            Directory.CreateDirectory(Path.GetFullPath(filmstripDirectory));
            using var film = File.Create(Path.Combine(Path.GetFullPath(filmstripDirectory), $"frame-{_renderedFrames:D5}.png"));
            GraphicsDevice.Presenter.BackBuffer.Save(GraphicsContext.CommandList, film, ImageFileType.Png);
        }

        if (!_captured && capturePath is not null && _renderedFrames >= (liveOptions?.Frames ?? (shellMode ? smokeFrames : 12)))
        {
            var fullPath = Path.GetFullPath(capturePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var output = File.Create(fullPath);
            GraphicsDevice.Presenter.BackBuffer.Save(GraphicsContext.CommandList, output, ImageFileType.Png);
            _captured = true;
        }
    }
}

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
var missionMap = GetArgument(args, "--mission");
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
};
if (matchMap is not null && missionMap is not null)
{
    throw new ArgumentException("--match and --mission exclude each other.");
}

if ((matchMap ?? missionMap) is not null && contentDirectory is null)
{
    throw new ArgumentException("--match and --mission require --content-dir.");
}

if (modelPath is not null && contentDirectory is null)
{
    throw new ArgumentException("--model-path requires --content-dir.");
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

using (var game = new CnCNewGame(smokeTest, contentDirectory, modelPath, capturePath, liveOptions))
{
    game.Run();
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
/// <param name="Frames">Smoke test only: rendered frames at a fixed 30 fps before the capture.</param>
/// <param name="AutoPlay">The human slot is played by the computer (--autoplay).</param>
/// <param name="Look">Camera target "x,y" in world units (--look); Angle/Pitch in degrees, Height in world units above ground.</param>
internal sealed record LiveOptions(string Map, string Faction, ImmutableArray<string> Opponents, int Seed, int Frames, string Size,
    bool AutoPlay, string? Look, string? Angle, string? Pitch, string? Height)
{
    /// <summary>The map is a campaign mission played by its own scripts (--mission) instead of a skirmish (--match).</summary>
    public bool IsMission { get; init; }

    /// <summary>The side the human plays in a mission whose map marks none (--human-side; see <c>MissionOptions.HumanSideName</c>).</summary>
    public string HumanSide { get; init; } = string.Empty;
}

internal sealed class CnCNewGame(
    bool smokeTest,
    string? contentDirectory,
    string? modelPath,
    string? capturePath,
    LiveOptions? liveOptions) : Game
{
    private LiveMatchHost? _live;
    private int _renderedFrames;
    private bool _captured;
    private W3dStrideScene? _w3dScene;

    protected override void Initialize()
    {
        AutoLoadDefaultSettings = false;
        var size = (liveOptions?.Size ?? "1280x720").Split('x');
        GraphicsDeviceManager.PreferredBackBufferWidth = int.Parse(size[0]);
        GraphicsDeviceManager.PreferredBackBufferHeight = int.Parse(size[1]);
        base.Initialize();
        Window.Title = "CnCNew — Stride Direct3D 11 W3D Viewer";
    }

    protected override async Task LoadContent()
    {
        await base.LoadContent();
        if (liveOptions is not null)
        {
            LoadLiveMatch(liveOptions);
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
        var data = GameData.Load(contentDirectory!, step => Console.WriteLine($"loading: {step}"));
        var mapPath = data.Maps.FirstOrDefault(path => path.Equals(options.Map, StringComparison.OrdinalIgnoreCase))
            ?? data.Maps.SingleOrDefault(path => path.Contains(options.Map, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Map '{options.Map}' was not found or is ambiguous.");
        if (options.IsMission)
        {
            var viewport = GraphicsDevice.Presenter.BackBuffer;
            var run = MissionRun.Load(MissionContext.From(data), mapPath, MissionOptions.Standard with { HumanSideName = options.HumanSide });
            var mission = LiveMission.Create(data, run, new System.Numerics.Vector2(viewport.Width, viewport.Height));
            ApplyCamera(mission.Camera, options);
            _live = new LiveMatchHost(this, data, mission);
            Window.Title = $"CnCNew — mission {mapPath} — {mission.HumanFaction}";
            return;
        }
        var setup = new MatchSetup(mapPath, options.Faction, options.Opponents, SkirmishMatchOptions.Standard) { Seed = options.Seed, AutoPlay = options.AutoPlay };
        var back = GraphicsDevice.Presenter.BackBuffer;
        var match = LiveMatch.Create(data, setup, new System.Numerics.Vector2(back.Width, back.Height));
        ApplyCamera(match.Camera, options);
        _live = new LiveMatchHost(this, data, match);
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
        _live?.Dispose();
        _live = null;
        _w3dScene?.Dispose();
        _w3dScene = null;
        base.UnloadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (_live is not null)
        {
            var back = GraphicsDevice.Presenter.BackBuffer;
            _live.Update(gameTime, GraphicsContext.CommandList, back.Width, back.Height, smokeTest ? 1.0 / 30 : null);
            if (smokeTest && ++_renderedFrames >= liveOptions!.Frames && (capturePath is null || _captured))
            {
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
        base.Draw(gameTime);
        if (!_captured && capturePath is not null && _renderedFrames >= (liveOptions?.Frames ?? 12))
        {
            var fullPath = Path.GetFullPath(capturePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var output = File.Create(fullPath);
            GraphicsDevice.Presenter.BackBuffer.Save(GraphicsContext.CommandList, output, ImageFileType.Png);
            _captured = true;
        }
    }
}

using System.Diagnostics;
using System.Numerics;
using CnCNew.Client.Stride.Layers.Fx;
using CnCNew.Client.Stride.Rendering;
using CnCNew.Game.Live;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Terrain;
using Stride.Graphics;

namespace CnCNew.Client.Stride.Layers;

/// <summary>
/// Draws the presentation FX package of a live match: particle systems (camera-facing and ground-aligned sprites, streaks), tracers and
/// ground decals. Each frame it captures <see cref="FxExecutor.Capture"/>, builds quads into one shared dynamic vertex buffer and points a
/// handful of pooled one-mesh entities at ranges of it. The entities live in the normal scene, so the compositor orders them against the
/// terrain and object layers: blended runs are placed on the view axis at their depth for the transparent stage's back-to-front sort.
/// Details, limits and the W3D sources: <c>docs/CLIENT-FX.md</c>.
/// </summary>
internal sealed class FxLayer : IWorldLayer
{
    private readonly FxStatsTotals _totals = new();
    private readonly Func<Vector3, float, bool> _isVisible;
    private FxGeometry? _geometry;
    private FxMaterials? _materials;
    private FxRunPool? _pool;
    private FxBuilder? _builder;
    private FxDemo? _demo;
    private Frustum _frustum;
    private bool _printed;

    public FxLayer() => _isVisible = IsVisible;

    public string Name => "fx";

    /// <summary>The numbers of the last rendered frame.</summary>
    public FxStats Stats { get; private set; }

    /// <summary>
    /// The last captured frame. <c>FxExecutor.Capture</c> hands out ray and view-shake requests once, and this layer is the only consumer of
    /// capture, so whoever needs them (camera shake, beam drawables) reads them here.
    /// </summary>
    public FxFrame? LastFrame { get; private set; }

    public void Attach(RenderServices services, LiveMatch match)
    {
        _geometry = new FxGeometry(services.GraphicsDevice);
        _materials = new FxMaterials(services);
        _pool = new FxRunPool(services.Scene, _geometry, _materials);
        _builder = new FxBuilder(_geometry, match.Heights, services.GraphicsDevice.ColorSpace == ColorSpace.Linear);
        _demo = FxDemo.FromEnvironment(out var bench);
        if (bench)
        {
            Console.WriteLine(FxDemo.RunBenchmark(_builder, _geometry, match.Camera));
        }
    }

    public void Update(in WorldFrame frame)
    {
        var builder = _builder ?? throw new InvalidOperationException("The effect layer is not attached.");
        var geometry = _geometry!;
        var pool = _pool!;
        var camera = frame.Match.Camera;
        _frustum = camera.Frustum;
        _demo?.Tick(frame.Match);

        var captureStart = Stopwatch.GetTimestamp();
        var fx = frame.Match.Fx.Capture(_isVisible);
        var buildStart = Stopwatch.GetTimestamp();
        LastFrame = fx;

        var view = FxView.From(camera);
        builder.Build(fx, in view, in _frustum);
        pool.BeginFrame();
        var lastDepth = view.Far * 0.98f;
        for (var run = 0; run < builder.RunCount; run++)
        {
            pool.Draw(builder.MaterialOf(run), builder.FirstQuadOf(run), builder.QuadCountOf(run), builder.RunCenter(run, run, ref lastDepth));
        }

        pool.EndFrame();
        var uploadBytes = geometry.Upload(frame.CommandList);
        var end = Stopwatch.GetTimestamp();

        var counts = builder.Counts;
        Stats = new FxStats(
            fx.Particles.Particles.Length, fx.Particles.Culled, counts.ParticlesDrawn, counts.Streaks, counts.Tracers, counts.Decals, counts.Batches,
            counts.Runs, builder.RunCount, counts.Quads, uploadBytes, counts.DroppedQuads, counts.SkippedDrawable, counts.SkippedSmudge,
            counts.SkippedUnusable, counts.SkippedShortStreaks, counts.DeferredItems, fx.Lights.Length, fx.RayEffects.Length,
            Stopwatch.GetElapsedTime(buildStart, end).TotalMilliseconds, Stopwatch.GetElapsedTime(captureStart, buildStart).TotalMilliseconds);
        _totals.Materials = _materials!.Count;
        _totals.PoolSlots = pool.SlotCount;
        _totals.Add(Stats);
    }

    public void Dispose()
    {
        if (!_printed && _totals.Frames > 0 && Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            _printed = true;
            Console.WriteLine(_totals.Format(Stats));
        }

        _pool?.Dispose();
        _geometry?.Dispose();
        _pool = null;
        _geometry = null;
        _materials = null;
    }

    /// <summary>The culling hook of the particle capture: a box around the particle against the camera frustum (no allocations).</summary>
    private bool IsVisible(Vector3 position, float size)
    {
        var reach = new Vector3(Math.Max(size, 10f));
        return _frustum.Intersects(new Aabb(position - reach, position + reach));
    }
}

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using CnCNew.Game.Live;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Fx;

namespace CnCNew.Client.Stride.Layers.Fx;

/// <summary>
/// Diagnostic mode for looking at every effect class without waiting for a fight: environment variable <c>CNCNEW_FX_DEMO</c>.
/// <list type="bullet">
/// <item><c>ring</c> (or <c>1</c>): every 45 logic frames a ring of effects is placed around the camera target through the public API of
/// <c>match.Fx</c> - the first particle system template of each blend/kind/alignment class (sorted by name), the first FX lists with a
/// tracer, and scorch decals.</item>
/// <item><c>bench</c>: builds a synthetic frame of 10,000 particles (mixed materials, mixed alignment, 100 streaks) a few hundred times and
/// prints the CPU time of <see cref="FxBuilder.Build"/>; nothing is spawned.</item>
/// </list>
/// Nothing here runs unless the variable is set.
/// </summary>
internal sealed class FxDemo
{
    public const string VariableName = "CNCNEW_FX_DEMO";

    private const int PeriodFrames = 45;
    private const float RingRadius = 70f;

    private readonly bool _ring;
    private long _nextFrame;
    private List<ParticleSystemTemplate>? _templates;
    private List<string>? _tracerLists;

    private FxDemo(bool ring) => _ring = ring;

    public static FxDemo? FromEnvironment(out bool bench)
    {
        bench = false;
        var value = Environment.GetEnvironmentVariable(VariableName);
        if (string.IsNullOrWhiteSpace(value) || value == "0")
        {
            return null;
        }

        if (value.Equals("bench", StringComparison.OrdinalIgnoreCase))
        {
            bench = true;
            return null;
        }

        if (value is "1" || value.Equals("ring", StringComparison.OrdinalIgnoreCase))
        {
            return new FxDemo(true);
        }

        throw new InvalidOperationException($"{VariableName}='{value}' is not one of: ring, 1, bench, 0.");
    }

    public void Tick(LiveGame match)
    {
        if (!_ring || match.Fx.Frame < _nextFrame)
        {
            return;
        }

        _nextFrame = match.Fx.Frame + PeriodFrames;
        Spawn(match);
    }

    private void Spawn(LiveGame match)
    {
        var fx = match.Fx;
        _templates ??= PickTemplates(fx.Catalog);
        if (_tracerLists is null)
        {
            _tracerLists = PickTracerLists(fx.Catalog);
            Console.WriteLine("fx demo ring (clockwise from east): "
                + string.Join(", ", _templates.Select(t => $"{t.Name}[{t.Shader}/{t.Kind}{(t.IsGroundAligned ? "/ground" : string.Empty)}]"))
                + " | tracers: " + string.Join(", ", _tracerLists));
        }

        var target = match.Camera.Target;
        var center = new Vector3(target.X, target.Y, match.Heights.HeightAt(target.X, target.Y));
        var slots = _templates.Count + _tracerLists.Count + 4;
        var slot = 0;

        Vector3 Place(float radius)
        {
            var angle = slot++ * (MathF.Tau / slots);
            var x = center.X + MathF.Cos(angle) * radius;
            var y = center.Y + MathF.Sin(angle) * radius;
            return new Vector3(x, y, match.Heights.HeightAt(x, y));
        }

        foreach (var template in _templates)
        {
            fx.Particles.Spawn(template, new ParticleSpawn(Place(RingRadius)));
        }

        foreach (var list in _tracerLists)
        {
            var from = Place(RingRadius * 0.6f) + new Vector3(0, 0, 12);
            var invocation = new FxInvocation(from) { Secondary = from + new Vector3(0, -80, -4), PrimarySpeed = 6f };
            fx.ExecuteFxListAtPosition(list, invocation, match.FxView);
        }

        for (var index = 0; index < 4; index++)
        {
            fx.Decals.AddScorch(Place(RingRadius * 0.4f), 14f + index * 6f, index, fx.Frame);
        }
    }

    private static List<ParticleSystemTemplate> PickTemplates(FxCatalog catalog)
    {
        var seen = new HashSet<(ParticleShader, ParticleKind, bool)>();
        var picked = new List<ParticleSystemTemplate>();
        foreach (var template in catalog.ParticleSystems.Values)
        {
            if (string.IsNullOrEmpty(template.ParticleName) || template.Shader == ParticleShader.None)
            {
                continue;
            }

            if (seen.Add((template.Shader, template.Kind, template.IsGroundAligned)))
            {
                picked.Add(template);
            }
        }

        return picked;
    }

    private static List<string> PickTracerLists(FxCatalog catalog)
    {
        var picked = new List<string>();
        foreach (var (name, list) in catalog.FxLists)
        {
            if (list.Nuggets.Any(nugget => nugget is TracerFxNugget))
            {
                picked.Add(name);
                if (picked.Count == 2)
                {
                    break;
                }
            }
        }

        return picked;
    }

    /// <summary>The synthetic CPU benchmark of <c>bench</c> mode; returns a one-line result.</summary>
    public static string RunBenchmark(FxBuilder builder, FxGeometry geometry, RtsCamera camera)
    {
        const int particleCount = 10_000;
        const int iterations = 400;
        var view = FxView.From(camera);
        var frustum = camera.Frustum;
        var batches = ImmutableArray.Create(
            new ParticleBatchKey("EXSmokNew1.tga", ParticleShader.Alpha, ParticleKind.Particle, false),
            new ParticleBatchKey("EXExplo01.tga", ParticleShader.Additive, ParticleKind.Particle, false),
            new ParticleBatchKey("EXDustA.tga", ParticleShader.Alpha, ParticleKind.Particle, true),
            new ParticleBatchKey("EXDebris.tga", ParticleShader.AlphaTest, ParticleKind.Particle, false),
            new ParticleBatchKey("EXStreak.tga", ParticleShader.Additive, ParticleKind.Streak, false));
        var particles = ImmutableArray.CreateBuilder<ParticleInstance>(particleCount);
        var random = new Random(12345);
        var target = camera.Target;
        for (var index = 0; index < particleCount; index++)
        {
            var streak = index % 100 < 10;
            var batch = streak ? 4 : index % 4;
            var system = streak ? 1000 + index / 100 : index % 64;
            var position = new Vector3(target.X + (random.NextSingle() - 0.5f) * 400f, target.Y + (random.NextSingle() - 0.5f) * 400f, target.Z + random.NextSingle() * 60f);
            particles.Add(new ParticleInstance(position, 4f + random.NextSingle() * 12f, new Vector4(random.NextSingle(), random.NextSingle(), random.NextSingle(), 0.5f + random.NextSingle() * 0.5f),
                random.NextSingle() * MathF.Tau, batch, system, (uint)index));
        }

        var frame = new FxFrame(0, new ParticleFrame(0, particles.ToImmutable(), batches, 0), [], [], [], [], []);
        builder.Build(frame, in view, in frustum);
        var start = Stopwatch.GetTimestamp();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            builder.Build(frame, in view, in frustum);
        }

        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds / iterations;
        return string.Create(CultureInfo.InvariantCulture,
            $"fx bench: {particleCount} live particles, {builder.Counts.ParticlesDrawn} drawn, {builder.Counts.Quads} quads, {builder.Counts.Runs} runs, {ms:F3} ms per build (avg of {iterations})");
    }
}

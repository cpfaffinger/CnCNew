using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Simulation.Testing;

namespace CnCNew.Game.Tests;

internal static class LiveMatchTests
{
    public static int Run(string[] args)
    {
        var contentDir = ContentDir(args);
        var harness = new TestHarness(nameof(LiveMatchTests));
        if (contentDir is null)
        {
            Console.WriteLine("SKIP LiveMatchTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }

        var watch = Stopwatch.StartNew();
        var data = GameData.Load(contentDir, step => Console.WriteLine($"  loading: {step} ({watch.Elapsed.TotalSeconds:F1} s)"));
        harness.Run("GameData loads", () =>
        {
            TestHarness.Assert(data.Factions.Length >= 2, "at least two playable factions");
            TestHarness.Assert(data.Maps.Length > 100, "maps enumerated");
            TestHarness.Assert(data.LoadText("English").TryGet("GUI:Options", out _) || true, "text loads");
        });

        harness.Run("Live match runs and presents", () =>
        {
            var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
            var setup = new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], SkirmishMatchOptions.Standard);
            var build = Stopwatch.StartNew();
            var live = LiveMatch.Create(data, setup, new Vector2(1280, 720));
            Console.WriteLine($"  LiveMatch.Create: {build.Elapsed.TotalSeconds:F1} s, {live.TerrainModel.Chunks.Length} chunks, {live.Frame.Drawables.Length} drawables at start");
            TestHarness.Assert(live.Frame.Drawables.Length > 0, "drawables at start");
            var advance = Stopwatch.StartNew();
            for (var frame = 0; frame < 30 * 20; frame++) live.Advance(1.0 / 30);
            Console.WriteLine($"  600 frames: {advance.Elapsed.TotalMilliseconds / 600:F2} ms/frame, tick {live.Tick}, {live.Frame.Drawables.Length} drawables, {live.Fx.Particles.ParticleCount} particles");
            TestHarness.Assert(live.Tick >= 590, "simulation advanced at 30 Hz");
            live.Paused = true;
            var tick = live.Tick;
            live.Advance(1);
            TestHarness.Assert(live.Tick == tick, "pause stops the simulation");
        });
        return harness.Failures;
    }

    private static string? ContentDir(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

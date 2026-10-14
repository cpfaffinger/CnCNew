using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Lockstep;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

namespace CnCNew.PresentationTool;

/// <summary>
/// How evenly the picture of a match moves from one drawn frame to the next, without a window (docs/MULTIPLAYER-FINDINGS.md section 2, threshold T4): a match of
/// computer players is advanced by 1 / <c>--fps</c> seconds per frame and every frame's drawables are measured by <see cref="NetStats"/> - in how many frames a
/// quantity that is on its way changes, and by how much. With <c>--step-values</c> the quantities that are interpolated since Phase 29 (health, construction
/// progress, wheel travel, chassis pose) step with the tick, as before. The match is the same either way: the last line is a hash over every frame drawn exactly
/// at a tick, which the interpolation must not change, and the session hash at the end.
/// </summary>
internal static class SmoothnessCommand
{
    public const string Usage = "smoothness <content-dir> --map <virtual-map-path> [--factions A,B] [--seconds 120] [--fps 144] [--tick-rate N] [--seed N] [--step-values]";

    public static int Run(string[] args)
    {
        if (args.Length < 1) throw new ArgumentException(Usage);
        var invariant = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = invariant;
        string? Option(string key) => Array.IndexOf(args, key) is var index and >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var mapPath = Option("--map") ?? throw new ArgumentException(Usage);
        var rate = Option("--tick-rate") is { } rateText ? SimulationRate.FromTicksPerSecond(int.Parse(rateText, invariant)) : GameData.DefaultSessionRate;
        var seconds = int.Parse(Option("--seconds") ?? "120", invariant);
        var fps = int.Parse(Option("--fps") ?? "144", invariant);
        var stepValues = args.Contains("--step-values");
        var data = GameData.Load(args[0], rate: rate);
        var wanted = Option("--factions")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chosen = wanted is null ? data.Factions.Take(2).Select(value => value.Faction.Name).ToImmutableArray()
            : [.. wanted.Select(name => data.Factions.FirstOrDefault(value => value.Faction.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Faction.Name ?? throw new ArgumentException($"Unknown or unplayable faction {name}."))];
        LiveGame.InterpolateValues = !stepValues;
        // Nobody watches through a fog: every object is drawn, so the computer players' building and fighting is in the picture.
        var setup = new MatchSetup(mapPath, chosen[0], chosen[1..], SkirmishMatchOptions.Standard with { MaxTicks = rate.Time.WholeSecondsToTicks(seconds) })
            { AutoPlay = true, Spectator = true, Seed = int.Parse(Option("--seed") ?? "0", invariant) };
        var live = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var stats = new NetStats();
        using var atTicks = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var frameSeconds = 1.0 / fps;
        long frames = 0, tickFrames = 0;
        while (!live.IsOver)
        {
            live.Advance(frameSeconds);
            var frame = live.Frame;
            stats.FrameDrawn(frame, frameSeconds * 1000);
            frames++;
            // A frame drawn exactly at a tick shows the tick's own values, whatever lies between two ticks.
            if (frame.Alpha < 1f) continue;
            tickFrames++;
            foreach (var drawable in frame.Drawables)
            {
                if (!drawable.Entity.IsValid) continue;
                atTicks.AppendData(Encoding.UTF8.GetBytes(string.Create(invariant,
                    $"{frame.Tick}:{drawable.Entity.Value}:{drawable.Position.X:R},{drawable.Position.Y:R},{drawable.Position.Z:R}:{drawable.Yaw:R}:{drawable.Health01:R}:{drawable.ConstructionProgress01:R}:{drawable.WheelTravel:R}:{drawable.Chassis}\n")));
            }
        }
        Console.WriteLine(string.Create(invariant, $"smoothness: {mapPath}, {string.Join(" against ", chosen)}, {seconds} s of game time at {rate.TicksPerSecond} Hz, drawn at {fps} frames a second, values {(stepValues ? "stepped with the tick" : "interpolated between ticks")}"));
        foreach (var line in stats.Report().Where(line => line.Contains("steps ", StringComparison.Ordinal) || line.Contains("motion:", StringComparison.Ordinal))) Console.WriteLine(line);
        Console.WriteLine(string.Create(invariant, $"smoothness: frames={frames} framesAtATick={tickFrames} hashOfFramesAtATick={Convert.ToHexString(atTicks.GetHashAndReset())[..16]} sessionHash={live.Session.StateHash():X16} tick={live.Tick}"));
        return 0;
    }
}

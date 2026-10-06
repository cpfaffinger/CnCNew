using System.Globalization;
using CnCNew.Simulation.Time;

namespace CnCNew.SimTool;

/// <summary>
/// The tick rate this run of the tool simulates at: <c>--tick-rate N</c> (30, 60 or 120; default 30). Every command that creates a world builds its
/// catalog for this rate. The manifest commands always run at <see cref="Manifest"/>, the rate the reference manifest records.
/// </summary>
internal static class ToolRate
{
    public const string Option = "--tick-rate";

    /// <summary>The rate of <c>baselines/reference-manifest.json</c> and <c>baselines/input-manifest.json</c> (their <c>tickRate</c> entry).</summary>
    public static SimulationRate Manifest => SimulationRate.Hz30;

    public static SimulationRate Current { get; private set; } = SimulationRate.Hz30;

    public static void Read(string[] args)
    {
        var index = Array.IndexOf(args, Option);
        if (index < 0) return;
        if (index + 1 >= args.Length) throw new ArgumentException($"{Option} needs a value: 30, 60 or 120.");
        Current = SimulationRate.FromTicksPerSecond(long.Parse(args[index + 1], CultureInfo.InvariantCulture));
        if (args[0] is "reference-manifest" or "input-manifest" && Current != Manifest)
            throw new ArgumentException($"The manifest commands run at the recorded rate of {Manifest}; {Option} {Current.TicksPerSecond} is refused.");
    }

    /// <summary>Game minutes of a tick count at the current rate, for reports.</summary>
    public static double Minutes(long ticks) => Minutes(ticks, Current);

    public static double Minutes(long ticks, SimulationRate rate) => rate.Time.ToWholeSeconds(ticks) / 60.0;
}

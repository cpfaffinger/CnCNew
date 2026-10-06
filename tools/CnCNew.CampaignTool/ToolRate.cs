using System.Globalization;
using CnCNew.Simulation.Time;

namespace CnCNew.CampaignTool;

/// <summary>
/// The tick rate this run of the tool simulates at: <c>--tick-rate N</c> (30, 60 or 120; default 30). The catalog every mission world runs on is built
/// for this rate; tick budgets on the command line stay ticks of that rate.
/// </summary>
internal static class ToolRate
{
    public const string Option = "--tick-rate";

    public static SimulationRate Current { get; private set; } = SimulationRate.Hz30;

    public static void Read(string[] args)
    {
        var index = Array.IndexOf(args, Option);
        if (index < 0) return;
        if (index + 1 >= args.Length) throw new ArgumentException($"{Option} needs a value: 30, 60 or 120.");
        Current = SimulationRate.FromTicksPerSecond(long.Parse(args[index + 1], CultureInfo.InvariantCulture));
    }

    /// <summary>Game minutes of a tick count at the current rate, for reports.</summary>
    public static double Minutes(long ticks) => Current.Time.ToWholeSeconds(ticks) / 60.0;
}

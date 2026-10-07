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

    /// <summary>
    /// A grace period (logic frames of the original, the unit of <c>SkirmishMatchOptions.GraceTicks</c>) capped at a quarter of a run budget that is
    /// given in ticks of <paramref name="rate"/>: the budget share is turned into frames before the two are compared, so the cap is the same game time at
    /// every rate. 30 Hz: <c>Math.Min(graceFrames, maxTicks / 4)</c>.
    /// </summary>
    public static long GraceWithinBudget(long graceFrames, long maxTicks, SimulationRate rate) => Math.Min(graceFrames, rate.Time.ToOriginalFrames(maxTicks / 4));

    /// <summary>Game minutes of a tick count at the current rate, for reports.</summary>
    public static double Minutes(long ticks) => Minutes(ticks, Current);

    /// <summary>
    /// A run budget in ticks of the current rate. <paramref name="ticksOption"/> (<c>--ticks</c>/<c>--max-ticks</c>) is the raw figure; <c>--seconds S</c>
    /// and <c>--minutes M</c> state game time. Without any of them the default applies: <paramref name="defaultOriginalFrames"/> logic frames of the
    /// original (the tick count of the same game time at 30 Hz), so a default run covers the same game time at every rate.
    /// </summary>
    public static long Budget(string[] args, string ticksOption, long defaultOriginalFrames)
    {
        var given = new[] { ticksOption, SecondsOption, MinutesOption }.Where(option => Array.IndexOf(args, option) >= 0).ToArray();
        if (given.Length > 1) throw new ArgumentException($"{string.Join(" and ", given)} both state the run budget; give one.");
        if (given.Length == 0) return Current.Time.OriginalFrames(defaultOriginalFrames);
        var index = Array.IndexOf(args, given[0]);
        if (index + 1 >= args.Length) throw new ArgumentException($"{given[0]} needs a value.");
        var value = long.Parse(args[index + 1], CultureInfo.InvariantCulture);
        if (value < 0) throw new ArgumentException($"{given[0]} must not be negative.");
        return given[0] == ticksOption ? value : Current.Time.WholeSecondsToTicks(given[0] == MinutesOption ? checked(value * SecondsPerMinute) : value);
    }

    public const string SecondsOption = "--seconds";
    public const string MinutesOption = "--minutes";
    private const long SecondsPerMinute = 60;

    public static double Minutes(long ticks, SimulationRate rate) => rate.Time.ToWholeSeconds(ticks) / 60.0;
}

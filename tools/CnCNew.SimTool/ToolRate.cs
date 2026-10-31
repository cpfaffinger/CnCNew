using System.Globalization;
using CnCNew.Simulation.Time;

namespace CnCNew.SimTool;

/// <summary>
/// The tick rate this run of the tool simulates at: <c>--tick-rate N</c> (30, 60 or 120); without the option the default of the game layer
/// (<c>GameData.DefaultSessionRate</c>, the product rate). Every command that creates a world builds its catalog for this rate. A command that checks a
/// manifest runs at the rate the file records (<see cref="UseRecorded"/>): the same binary checks a manifest of any supported rate, and an option that
/// names another rate than the file is refused.
/// </summary>
internal static class ToolRate
{
    public const string Option = "--tick-rate";

    /// <summary>The rate of a run that names none.</summary>
    public static SimulationRate Default => CnCNew.Game.Data.GameData.DefaultSessionRate;

    public static SimulationRate Current { get; private set; } = Default;

    /// <summary>True when <c>--tick-rate</c> was given.</summary>
    public static bool Given { get; private set; }

    /// <summary><c>--rules product|original|fixes|NAME,...</c>: the rules this run simulates with (docs/GGC-FIXES.md); the product's without the option.</summary>
    public static CnCNew.Simulation.Rules.RuleProfile Rules { get; private set; } = CnCNew.Simulation.Rules.RuleProfile.Product;

    public static void Read(string[] args)
    {
        var rules = Array.IndexOf(args, "--rules");
        if (rules >= 0)
        {
            if (rules + 1 >= args.Length) throw new ArgumentException("--rules needs a value: product, original, fixes or the names of fixes.");
            Rules = CnCNew.Simulation.Rules.RuleProfile.Parse(args[rules + 1]);
        }
        var index = Array.IndexOf(args, Option);
        if (index < 0) return;
        if (index + 1 >= args.Length) throw new ArgumentException($"{Option} needs a value: 30, 60 or 120.");
        Current = SimulationRate.FromTicksPerSecond(long.Parse(args[index + 1], CultureInfo.InvariantCulture));
        Given = true;
    }

    /// <summary>
    /// Makes the rate a manifest file records the rate of this run. The file decides; <c>--tick-rate</c> may repeat its rate and is refused when it names another
    /// one (a check at another rate than the recorded one compares nothing).
    /// </summary>
    public static void UseRecorded(long recordedTicksPerSecond, string file)
    {
        if (!Rules.IsProduct)
            throw new ArgumentException($"{file} is measured with the product's rules; --rules {Rules.Name} is refused. The product is the yardstick.");
        var recorded = SimulationRate.FromTicksPerSecond(recordedTicksPerSecond);
        if (Given && Current != recorded)
            throw new ArgumentException($"{file} records {recorded}; {Option} {Current.TicksPerSecond} is refused. A manifest is checked at the rate it was written at.");
        Current = recorded;
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

    /// <summary>Whole game seconds of a tick count at the current rate, for the parameters a manifest states next to its tick budgets.</summary>
    public static long GameSeconds(long ticks) => Current.Time.ToWholeSeconds(ticks);
}

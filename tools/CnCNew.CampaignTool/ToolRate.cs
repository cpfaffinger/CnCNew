using System.Globalization;
using CnCNew.Simulation.Time;

namespace CnCNew.CampaignTool;

/// <summary>
/// The tick rate this run of the tool simulates at: <c>--tick-rate N</c> (30, 60 or 120); without the option the default of the game layer
/// (<c>GameData.DefaultSessionRate</c>, the product rate). The catalog every mission world runs on is built
/// for this rate. A budget given as <c>--ticks</c> is a raw tick count of that rate; <c>--seconds</c>/<c>--minutes</c> and the defaults are game time.
/// </summary>
internal static class ToolRate
{
    public const string Option = "--tick-rate";

    public static SimulationRate Current { get; private set; } = CnCNew.Game.Data.GameData.DefaultSessionRate;

    /// <summary><c>--rules product|original|fixes|NAME,...</c>: the rules of the missions of this run (docs/GGC-FIXES.md); the product's without the option.</summary>
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
    }

    /// <summary>Game minutes of a tick count at the current rate, for reports.</summary>
    public static double Minutes(long ticks) => Current.Time.ToWholeSeconds(ticks) / 60.0;

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

    /// <summary>The game time of a tick count at the current rate as <c>m:ss</c> (whole seconds; minutes are not wrapped into hours), for reports next to the tick.</summary>
    public static string GameTime(long ticks)
    {
        var seconds = Current.Time.ToWholeSeconds(Math.Max(0, ticks));
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:D2}");
    }
}

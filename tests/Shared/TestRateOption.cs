namespace CnCNew.Tests;

/// <summary>
/// The tick rate the default test worlds of a test program are built at, as the plain number the process was started with: the original's 30 Hz unless the
/// program gets <c>--tick-rate N</c> or the environment variable <c>CNCNEW_TEST_TICK_RATE=N</c> (30, 60 or 120). It is an opt-in probe that shows which
/// expectations really count ticks; the reference stays 30 Hz. The value is read from the process itself, because static fixtures of a test class may be
/// built before the first statement of the program runs. This file has no reference to the simulation, so that a test program without one (the UI tests)
/// accepts and reports the same option; <c>TestRate</c> (TestTime.cs) turns the number into a <c>SimulationRate</c>.
/// </summary>
internal static class TestRateOption
{
    public const string Option = "--tick-rate";
    public const string Variable = "CNCNEW_TEST_TICK_RATE";

    /// <summary>The rate of the original, which every recorded 30 Hz value (golden hashes, hand-computed tick numbers) belongs to.</summary>
    public const int OriginalTicksPerSecond = CnCNew.Data.OriginalFrame.PerSecond;

    public static int TicksPerSecond { get; } = Select();

    /// <summary>True when the default test worlds run at the original's rate.</summary>
    public static bool IsOriginal => TicksPerSecond == OriginalTicksPerSecond;

    /// <summary>Writes the first line of a probe run; nothing at the original's rate, so that the output of a default run is unchanged.</summary>
    public static void Announce(string? note = null)
    {
        if (IsOriginal) return;
        Console.WriteLine($"Default test worlds run at {TicksPerSecond} Hz ({Option}/{Variable}): a probe, the reference is {OriginalTicksPerSecond} Hz.{(note is null ? "" : " " + note)}");
    }

    private static int Select()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, Option);
        if (index >= 0 && index + 1 >= arguments.Length) throw new ArgumentException($"{Option} needs a value (30, 60 or 120).");
        var text = index >= 0 ? arguments[index + 1] : Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(text)) return OriginalTicksPerSecond;
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ticksPerSecond) || ticksPerSecond is not (30 or 60 or 120))
            throw new ArgumentException($"{Option}/{Variable} needs 30, 60 or 120, got '{text}'.");
        return ticksPerSecond;
    }
}

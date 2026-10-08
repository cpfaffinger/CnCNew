namespace CnCNew.Tests;

/// <summary>
/// The tick rate the default test worlds of a test program are built at, as the plain number the process was started with: the product's default rate
/// (<c>CnCNew.ProductRate</c>, the one definition, linked in as source) unless the program gets <c>--tick-rate N</c> or the environment variable
/// <c>CNCNEW_TEST_TICK_RATE=N</c> (30, 60 or 120). Every test program is green at each of the three. The value is read from the process itself, because static fixtures of a test class may be
/// built before the first statement of the program runs. This file has no reference to the simulation, so that a test program without one (the UI tests)
/// accepts and reports the same option; <c>TestRate</c> (TestTime.cs) turns the number into a <c>SimulationRate</c>.
/// </summary>
internal static class TestRateOption
{
    public const string Option = "--tick-rate";
    public const string Variable = "CNCNEW_TEST_TICK_RATE";

    /// <summary>The rate of the original: one tick is one logic frame of the original there, and a value written per frame is the value per tick.</summary>
    public const int OriginalTicksPerSecond = CnCNew.Data.OriginalFrame.PerSecond;

    /// <summary>The rate of a run that names none: the product's default.</summary>
    public const int DefaultTicksPerSecond = CnCNew.ProductRate.TicksPerSecond;

    public static int TicksPerSecond { get; } = Select();

    /// <summary>True when the default test worlds run at the original's rate.</summary>
    public static bool IsOriginal => TicksPerSecond == OriginalTicksPerSecond;

    /// <summary>Writes the first line of a run at another rate than the default; nothing at the default rate, so that the output of a default run has no such line.</summary>
    public static void Announce(string? note = null)
    {
        if (TicksPerSecond == DefaultTicksPerSecond) return;
        Console.WriteLine($"Default test worlds run at {TicksPerSecond} Hz ({Option}/{Variable}); the default rate is {DefaultTicksPerSecond} Hz.{(note is null ? "" : " " + note)}");
    }

    private static int Select()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, Option);
        if (index >= 0 && index + 1 >= arguments.Length) throw new ArgumentException($"{Option} needs a value (30, 60 or 120).");
        var text = index >= 0 ? arguments[index + 1] : Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(text)) return DefaultTicksPerSecond;
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ticksPerSecond) || ticksPerSecond is not (30 or 60 or 120))
            throw new ArgumentException($"{Option}/{Variable} needs 30, 60 or 120, got '{text}'.");
        return ticksPerSecond;
    }
}

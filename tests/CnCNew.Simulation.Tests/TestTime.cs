using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The rate the default test worlds and catalogs are built at. It is the product's default rate (<c>CnCNew.ProductRate</c>, the one definition, linked in as
/// source because this project does not reference the game layer) unless the program is started with <c>--tick-rate N</c> or the environment variable
/// <c>CNCNEW_TEST_TICK_RATE=N</c> (30, 60 or 120); the program is green at each of the three.
/// The value is read from the process itself, because static fixtures of <c>Tests</c> may be built before the first statement of the program runs.
/// Tests that are about a particular rate (Tests.TickRate*.cs, the unit conversions in Tests.Basics.cs) name that rate and do not use this.
/// </summary>
static class TestRate
{
    public const string Option = "--tick-rate";
    public const string Variable = "CNCNEW_TEST_TICK_RATE";

    /// <summary>The rate of a run that names none: the product's default.</summary>
    public static SimulationRate Product { get; } = SimulationRate.FromTicksPerSecond(CnCNew.ProductRate.TicksPerSecond);

    public static SimulationRate Default { get; } = Select();

    /// <summary>The time service of <see cref="Default"/>, for expectations about catalog data where no world is at hand.</summary>
    public static SimulationTime Time => Default.Time;

    /// <summary>True when the default test worlds run at the original's rate; raw 30 Hz values (golden hashes, recorded tick numbers) hold only then.</summary>
    public static bool IsOriginal => Default == SimulationRate.Original;

    /// <summary>Original frames (1/30 s of game time each) in ticks of <see cref="Default"/>.</summary>
    public static int Frames(long originalFrames) => checked((int)Default.Time.OriginalFrames(originalFrames));

    /// <summary>Ticks of <see cref="Default"/> as whole original frames (rounded down).</summary>
    public static long InFrames(long ticks) => Default.Time.ToOriginalFrames(ticks);

    private static SimulationRate Select()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, Option);
        var text = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(text)) return Product;
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ticksPerSecond))
            throw new ArgumentException($"{Option}/{Variable} needs a whole number, got '{text}'.");
        return SimulationRate.FromTicksPerSecond(ticksPerSecond);
    }
}

/// <summary>
/// Game time in ticks of the world a test runs. A number that means time - a duration from the data, a delay, a budget "run this long until X has happened" -
/// is written in original frames (1/30 s) and converted here; at 30 Hz the value is the number itself. A number that really means ticks (scheduler order,
/// "one step later", snapshot fields, recorded replay lengths) stays a plain number.
/// </summary>
static class TestTime
{
    /// <summary>Original frames (1/30 s of game time each) in ticks of <paramref name="world"/>.</summary>
    public static int Frames(this SimulationWorld world, long originalFrames) => checked((int)world.Time.OriginalFrames(originalFrames));

    /// <summary>Original frames in ticks of the world of <paramref name="session"/>.</summary>
    public static int Frames(this MissionSession session, long originalFrames) => session.World.Frames(originalFrames);

    /// <summary>Original frames in ticks of the world of <paramref name="session"/>.</summary>
    public static int Frames(this SkirmishMatchSession session, long originalFrames) => session.World.Frames(originalFrames);

    /// <summary>A speed or step written per original frame (the unit of a 30 Hz tick), per tick of <paramref name="world"/>. At 30 Hz the value itself.</summary>
    public static CnCNew.Simulation.Numerics.Fixed PerFrame(this SimulationWorld world, CnCNew.Simulation.Numerics.Fixed perOriginalFrame) => world.Time.PerOriginalFrameToPerTick(perOriginalFrame);

    /// <summary>Ticks of <paramref name="world"/> as whole original frames (rounded down), for expectations written on the original's grid.</summary>
    public static long InFrames(this SimulationWorld world, long ticks) => world.Time.ToOriginalFrames(ticks);
}

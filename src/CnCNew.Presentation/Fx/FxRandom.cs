namespace CnCNew.Presentation.Fx;

/// <summary>
/// The presentation-side replacement for <c>GameClientRandomValue</c>/<c>GameClientRandomValueReal</c>: a SplitMix64 generator
/// (Steele/Lea/Flood 2014) with an explicit seed, so a replay of the same event stream reproduces every particle byte for byte.
/// It never touches the simulation's <c>LogicRandom</c> and is never seeded from wall time.
/// </summary>
public sealed class FxRandom(ulong seed)
{
    private const float UnitScale = 1f / 16777215f;
    private ulong _state = seed;

    public ulong State => _state;

    public ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A value in [0, 1] (both inclusive) with 24 significant bits, like the reference's inclusive real range.</summary>
    public float NextUnit() => (NextUInt64() >> 40) * UnitScale;

    /// <summary><c>GameClientRandomValueReal(low, high)</c>: inclusive on both ends.</summary>
    public float NextReal(float low, float high) => low + (high - low) * NextUnit();

    /// <summary><c>GameClientRandomValue(low, high)</c>: an integer in [low, high], both inclusive.</summary>
    public int NextInt(int low, int high)
    {
        if (high <= low) return low;
        var span = (ulong)((long)high - low + 1);
        return (int)(low + (long)(NextUInt64() % span));
    }
}

/// <summary><c>GameClientRandomVariable::DistributionType</c> (<c>RandomValue.cpp DistributionTypeNames</c>).</summary>
public enum FxDistribution { Constant, Uniform, Gaussian, Triangular, LowBias, HighBias }

/// <summary>
/// <c>GameClientRandomVariable</c>. <see cref="Sample"/> follows <c>GameClientRandomVariable::getValue</c> exactly: a CONSTANT with
/// <c>low == high</c> returns <c>low</c> without drawing; a CONSTANT with <c>low != high</c> falls through to UNIFORM; UNIFORM draws
/// even when <c>low == high</c>; the other distributions are unimplemented in the reference and return 0 (the parser reports them).
/// </summary>
public readonly record struct FxRandomVariable(float Low, float High, FxDistribution Distribution)
{
    /// <summary>The zero-initialised reference default (the class has no constructor because it lives in unions).</summary>
    public static FxRandomVariable Zero => new(0f, 0f, FxDistribution.Constant);

    public static FxRandomVariable Constant(float value) => new(value, value, FxDistribution.Constant);

    public float Sample(FxRandom random) => Distribution switch
    {
        FxDistribution.Constant when Low == High => Low,
        FxDistribution.Constant or FxDistribution.Uniform => random.NextReal(Low, High),
        _ => 0f,
    };
}

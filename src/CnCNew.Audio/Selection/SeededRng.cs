namespace CnCNew.Audio.Selection;

/// <summary>
/// The audio layer's own deterministic random source (the reference uses a separate <c>GameAudioRandomValue</c> stream
/// for exactly this reason: audio choices must not disturb the logic RNG). Algorithm: xorshift64* (Marsaglia 2003,
/// multiplier from Vigna, "An experimental exploration of Marsaglia's xorshift generators", 2016) seeded through one
/// SplitMix64 step so that small seeds give well-mixed states. Not thread-safe; one instance per consumer.
/// </summary>
public sealed class SeededRng
{
    private ulong _state;

    public SeededRng(ulong seed)
    {
        var z = seed + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        _state = z == 0 ? 0x9E3779B97F4A7C15UL : z;
    }

    public ulong State => _state;

    public ulong NextUInt64()
    {
        var x = _state;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        _state = x;
        return x * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>Uniform integer in [<paramref name="low"/>, <paramref name="high"/>] (both inclusive, like <c>GameAudioRandomValue</c>).</summary>
    public int NextInt(int low, int high)
    {
        if (high <= low) return low;
        var range = (ulong)((long)high - low + 1);
        return (int)(low + (long)(NextUInt64() % range));
    }

    /// <summary>Uniform float in [<paramref name="low"/>, <paramref name="high"/>] with 24-bit resolution (<c>GameAudioRandomValueReal</c>).</summary>
    public float NextFloat(float low, float high)
    {
        if (high <= low) return low;
        var unit = (NextUInt64() >> 40) / 16777215f;
        return low + (high - low) * unit;
    }
}

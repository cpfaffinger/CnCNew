using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.State;

/// <summary>
/// Structured, name-based state description. One <c>Describe</c> routine feeds both the tick hash
/// (<see cref="HashStateWriter"/>) and the canonical snapshot (<see cref="JsonStateWriter"/>), so what is hashed and what is
/// saved cannot drift apart. Objects inside arrays have a <c>null</c> name.
/// </summary>
public interface IStateWriter
{
    void BeginObject(string? name);
    void EndObject();
    void BeginArray(string name);
    void EndArray();
    void Bool(string name, bool value);
    void Int(string name, long value);
    void UInt(string name, ulong value);
    void Fixed(string name, Fixed value);
    void Text(string name, string value);
}

public interface IStateReader
{
    bool Bool(string name);
    long Int(string name);
    ulong UInt(string name);
    Fixed Fixed(string name);
    string Text(string name);
    bool Has(string name);
    IStateReader Object(string name);
    IReadOnlyList<IStateReader> Array(string name);
}

/// <summary>Order-sensitive 64-bit state hash. Non-cryptographic: it detects divergence, it does not resist attacks.</summary>
public static class StateHash
{
    public const ulong Seed = 0x243F6A8885A308D3UL;

    /// <summary>One SplitMix64 step over the running value and the next input word.</summary>
    public static ulong Mix(ulong hash, ulong value)
    {
        unchecked
        {
            var z = hash + value + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public static ulong MixText(ulong hash, string value)
    {
        hash = Mix(hash, (ulong)value.Length);
        foreach (var character in value) hash = Mix(hash, character);
        return hash;
    }
}

public sealed class HashStateWriter : IStateWriter
{
    private ulong _hash = StateHash.Seed;
    public ulong Hash => _hash;
    public void Reset() => _hash = StateHash.Seed;

    public void BeginObject(string? name) => _hash = StateHash.Mix(_hash, 0xB1);
    public void EndObject() => _hash = StateHash.Mix(_hash, 0xE1);
    public void BeginArray(string name) => _hash = StateHash.Mix(_hash, 0xB2);
    public void EndArray() => _hash = StateHash.Mix(_hash, 0xE2);
    public void Bool(string name, bool value) => _hash = StateHash.Mix(_hash, value ? 0xF1UL : 0xF0UL);
    public void Int(string name, long value) => _hash = StateHash.Mix(_hash, unchecked((ulong)value));
    public void UInt(string name, ulong value) => _hash = StateHash.Mix(_hash, value);
    public void Fixed(string name, Fixed value) => _hash = StateHash.Mix(_hash, unchecked((ulong)value.Raw));
    public void Text(string name, string value) => _hash = StateHash.MixText(_hash, value);
}

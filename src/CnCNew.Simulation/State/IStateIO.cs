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

    /// <summary>
    /// <see langword="false"/> for a writer that drops every name (the hash writers: only values and structure reach the hash). A description that has to build a name at run
    /// time (<c>"tower" + index</c>) may then pass any name and save the allocation; it must still build the real one for every writer that keeps names.
    /// </summary>
    bool KeepsNames => true;
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

    /// <summary>The fold <see cref="HashStateWriter"/> performs, over words that were written down first: <c>Mix</c> word by word, starting from <paramref name="hash"/>.</summary>
    public static ulong Fold(ulong hash, ReadOnlySpan<ulong> words)
    {
        foreach (var word in words) hash = Mix(hash, word);
        return hash;
    }

    /// <summary>
    /// Folds several independent word streams, each from <see cref="Seed"/>: stream <c>i</c> is <c>words[ends[i - 1]..ends[i]]</c> (the first starts at 0) and its hash goes to
    /// <c>hashes[i]</c>. Every stream is folded exactly as <see cref="Fold"/> would fold it – same words, same order, same function – so the values are those of the sequential
    /// fold. The only difference is the schedule: one <c>Mix</c> depends on the previous one of its own stream, which leaves a processor waiting for most of a fold's time; four
    /// streams advance side by side here, so four such chains overlap. A stream that ends hands its lane to the next one.
    /// </summary>
    public static void FoldStreams(ReadOnlySpan<ulong> words, ReadOnlySpan<int> ends, Span<ulong> hashes)
    {
        var next = 0;
        int index0 = -1, index1 = -1, index2 = -1, index3 = -1;
        int position0 = 0, position1 = 0, position2 = 0, position3 = 0;
        int end0 = 0, end1 = 0, end2 = 0, end3 = 0;
        ulong hash0 = Seed, hash1 = Seed, hash2 = Seed, hash3 = Seed;
        while (true)
        {
            // A lane whose stream is finished reports its hash and takes the next stream; empty streams are answered on the spot.
            while (index0 < 0 || position0 == end0)
            {
                if (index0 >= 0) hashes[index0] = hash0;
                if (next == ends.Length) { index0 = -1; break; }
                index0 = next; position0 = next == 0 ? 0 : ends[next - 1]; end0 = ends[next]; hash0 = Seed; next++;
            }
            while (index1 < 0 || position1 == end1)
            {
                if (index1 >= 0) hashes[index1] = hash1;
                if (next == ends.Length) { index1 = -1; break; }
                index1 = next; position1 = next == 0 ? 0 : ends[next - 1]; end1 = ends[next]; hash1 = Seed; next++;
            }
            while (index2 < 0 || position2 == end2)
            {
                if (index2 >= 0) hashes[index2] = hash2;
                if (next == ends.Length) { index2 = -1; break; }
                index2 = next; position2 = next == 0 ? 0 : ends[next - 1]; end2 = ends[next]; hash2 = Seed; next++;
            }
            while (index3 < 0 || position3 == end3)
            {
                if (index3 >= 0) hashes[index3] = hash3;
                if (next == ends.Length) { index3 = -1; break; }
                index3 = next; position3 = next == 0 ? 0 : ends[next - 1]; end3 = ends[next]; hash3 = Seed; next++;
            }
            // Fewer than four streams are left at the very end. An idle lane then reads along with a busy one and its result is dropped (its index stays negative), so the
            // loop below keeps one shape.
            var length = int.MaxValue;
            var busy = -1;
            if (index0 >= 0) { length = Math.Min(length, end0 - position0); busy = position0; }
            if (index1 >= 0) { length = Math.Min(length, end1 - position1); busy = position1; }
            if (index2 >= 0) { length = Math.Min(length, end2 - position2); busy = position2; }
            if (index3 >= 0) { length = Math.Min(length, end3 - position3); busy = position3; }
            if (busy < 0) break;
            if (index0 < 0) position0 = busy;
            if (index1 < 0) position1 = busy;
            if (index2 < 0) position2 = busy;
            if (index3 < 0) position3 = busy;
            var lane0 = words.Slice(position0, length);
            var lane1 = words.Slice(position1, length);
            var lane2 = words.Slice(position2, length);
            var lane3 = words.Slice(position3, length);
            for (var index = 0; index < lane0.Length && index < lane1.Length && index < lane2.Length && index < lane3.Length; index++)
            {
                hash0 = Mix(hash0, lane0[index]);
                hash1 = Mix(hash1, lane1[index]);
                hash2 = Mix(hash2, lane2[index]);
                hash3 = Mix(hash3, lane3[index]);
            }
            position0 += length;
            position1 += length;
            position2 += length;
            position3 += length;
        }
    }
}

/// <summary>
/// Writes down the words <see cref="HashStateWriter"/> would mix, in the same order, without mixing them: every call is one word, a text its length and then one word per
/// character. <see cref="StateHash.Fold"/> over the result is <see cref="HashStateWriter.Hash"/>. The buffer is reused; nothing is allocated once it has grown to its working size.
/// </summary>
public sealed class WordStateWriter : IStateWriter
{
    private ulong[] _words = new ulong[4096];
    private int _count;

    public int Count => _count;
    public ReadOnlySpan<ulong> Words => _words.AsSpan(0, _count);
    public bool KeepsNames => false;
    public void Reset() => _count = 0;

    /// <summary>Drops the words written after the first <paramref name="count"/>.</summary>
    public void Truncate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)count, (uint)_count);
        _count = count;
    }

    /// <summary>Appends words that an earlier description wrote into a writer of this kind.</summary>
    public void Append(ReadOnlySpan<ulong> words)
    {
        var count = _count;
        var target = _words;
        if (target.Length - count < words.Length) target = Grow(words.Length);
        words.CopyTo(target.AsSpan(count));
        _count = count + words.Length;
    }

    private void Add(ulong word)
    {
        var words = _words;
        var count = _count;
        if ((uint)count >= (uint)words.Length) words = Grow(1);
        words[count] = word;
        _count = count + 1;
    }

    private ulong[] Grow(int needed)
    {
        Array.Resize(ref _words, Math.Max(_words.Length * 2, _count + needed));
        return _words;
    }

    public void BeginObject(string? name) => Add(0xB1);
    public void EndObject() => Add(0xE1);
    public void BeginArray(string name) => Add(0xB2);
    public void EndArray() => Add(0xE2);
    public void Bool(string name, bool value) => Add(value ? 0xF1UL : 0xF0UL);
    public void Int(string name, long value) => Add(unchecked((ulong)value));
    public void UInt(string name, ulong value) => Add(value);
    public void Fixed(string name, Fixed value) => Add(unchecked((ulong)value.Raw));

    public void Text(string name, string value)
    {
        var count = _count;
        var words = _words;
        if (words.Length - count <= value.Length) words = Grow(value.Length + 1);
        words[count] = (ulong)value.Length;
        var target = words.AsSpan(count + 1, value.Length);
        for (var index = 0; index < target.Length; index++) target[index] = value[index];
        _count = count + 1 + value.Length;
    }
}

public sealed class HashStateWriter : IStateWriter
{
    private ulong _hash = StateHash.Seed;
    public ulong Hash => _hash;
    public void Reset() => _hash = StateHash.Seed;
    public bool KeepsNames => false;

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

using System.Collections;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// The counters and the flags of a script engine by name. What the state shows and what a walk yields is the table in ordinal name order, exactly as the sorted dictionary it replaces;
/// a lookup by name goes through a hash table kept in step with it instead of through string comparisons down a tree. Nothing about the content differs.
/// </summary>
internal sealed class NamedTable<TValue> : IEnumerable<KeyValuePair<string, TValue>>
{
    private readonly SortedDictionary<string, TValue> _sorted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TValue> _byName = new(StringComparer.Ordinal);

    public int Count => _sorted.Count;

    /// <summary>The values in no particular order: only for work whose result does not depend on the order.</summary>
    public Dictionary<string, TValue>.ValueCollection Unordered => _byName.Values;

    public bool TryGetValue(string name, out TValue value) => _byName.TryGetValue(name, out value!);

    public TValue? GetValueOrDefault(string name) => _byName.TryGetValue(name, out var value) ? value : default;

    public TValue this[string name]
    {
        set
        {
            _sorted[name] = value;
            _byName[name] = value;
        }
    }

    public void Clear()
    {
        _sorted.Clear();
        _byName.Clear();
    }

    public SortedDictionary<string, TValue>.Enumerator GetEnumerator() => _sorted.GetEnumerator();
    IEnumerator<KeyValuePair<string, TValue>> IEnumerable<KeyValuePair<string, TValue>>.GetEnumerator() => _sorted.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _sorted.GetEnumerator();
}

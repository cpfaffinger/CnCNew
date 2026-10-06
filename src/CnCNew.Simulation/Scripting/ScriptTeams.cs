using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace CnCNew.Simulation.Scripting;

/// <summary>Counts the changes to "which object is in which team instance": one counter for all teams of an engine. Not state.</summary>
internal sealed class MembershipStamp
{
    public long Version;
}

/// <summary>
/// The member ids of a team instance in the order they joined. Read like a list; only the script engine changes it, and every change that alters the content
/// advances the engine's <see cref="MembershipStamp"/>, so that what was derived from the memberships (the object-to-team table) is known to be current
/// without comparing it.
/// </summary>
public sealed class TeamMembers : IReadOnlyList<uint>
{
    private readonly List<uint> _ids = [];

    internal MembershipStamp? Stamp { get; set; }

    public int Count => _ids.Count;
    public uint this[int index] => _ids[index];
    public bool Contains(uint id) => _ids.Contains(id);
    public List<uint>.Enumerator GetEnumerator() => _ids.GetEnumerator();
    IEnumerator<uint> IEnumerable<uint>.GetEnumerator() => _ids.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _ids.GetEnumerator();

    internal void Add(uint id)
    {
        _ids.Add(id);
        Touch();
    }

    internal bool Remove(uint id)
    {
        if (!_ids.Remove(id)) return false;
        Touch();
        return true;
    }

    internal int RemoveAll(Predicate<uint> match)
    {
        var removed = _ids.RemoveAll(match);
        if (removed > 0) Touch();
        return removed;
    }

    internal void Clear()
    {
        if (_ids.Count == 0) return;
        _ids.Clear();
        Touch();
    }

    private void Touch()
    {
        if (Stamp is { } stamp) stamp.Version++;
    }
}

/// <summary>
/// The team instances of an engine by id, enumerated in ascending id order (what the sorted dictionary it replaces did), as a plain list: walking it neither
/// allocates nor chases tree nodes. Adding and removing a team advances the <see cref="MembershipStamp"/>.
/// </summary>
internal sealed class TeamTable(MembershipStamp stamp)
{
    private readonly List<ScriptTeam> _teams = [];

    /// <summary>The instances in ascending id order. Not to be changed by the caller; a walk must not add or remove teams (take a copy first).</summary>
    public List<ScriptTeam> Values => _teams;

    public bool TryGetValue(int id, [MaybeNullWhen(false)] out ScriptTeam team)
    {
        var index = Find(id);
        team = index >= 0 ? _teams[index] : null;
        return index >= 0;
    }

    public ScriptTeam this[int id]
    {
        set
        {
            var index = Find(id);
            if (index >= 0) _teams[index] = value;
            else _teams.Insert(~index, value);
            stamp.Version++;
        }
    }

    public bool Remove(int id)
    {
        var index = Find(id);
        if (index < 0) return false;
        _teams.RemoveAt(index);
        stamp.Version++;
        return true;
    }

    public void Clear()
    {
        _teams.Clear();
        stamp.Version++;
    }

    private int Find(int id)
    {
        var low = 0;
        var high = _teams.Count - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var value = _teams[middle].Id;
            if (value == id) return middle;
            if (value < id) low = middle + 1;
            else high = middle - 1;
        }
        return ~low;
    }
}

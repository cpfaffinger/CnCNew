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
    private static readonly List<ScriptTeam> None = [];
    private readonly List<ScriptTeam> _teams = [];
    // The same instances by the name of their prototype, each list in ascending id order: derived from _teams, kept in step with it.
    private readonly Dictionary<string, List<ScriptTeam>> _byPrototype = new(StringComparer.Ordinal);

    /// <summary>The instances in ascending id order. Not to be changed by the caller; a walk must not add or remove teams (take a copy first).</summary>
    public List<ScriptTeam> Values => _teams;

    /// <summary>The instances of one prototype in ascending id order - what a walk over <see cref="Values"/> that keeps the teams of that prototype yields. Not to be changed by the caller.</summary>
    public List<ScriptTeam> InstancesOf(string prototype) => _byPrototype.TryGetValue(prototype, out var list) ? list : None;

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
            if (index >= 0)
            {
                Unlist(_teams[index]);
                _teams[index] = value;
            }
            else _teams.Insert(~index, value);
            if (!_byPrototype.TryGetValue(value.Prototype, out var list)) _byPrototype[value.Prototype] = list = [];
            var at = list.Count;
            while (at > 0 && list[at - 1].Id > value.Id) at--;
            list.Insert(at, value);
            stamp.Version++;
        }
    }

    public bool Remove(int id)
    {
        var index = Find(id);
        if (index < 0) return false;
        Unlist(_teams[index]);
        _teams.RemoveAt(index);
        stamp.Version++;
        return true;
    }

    public void Clear()
    {
        _teams.Clear();
        _byPrototype.Clear();
        stamp.Version++;
    }

    private void Unlist(ScriptTeam team)
    {
        if (_byPrototype.TryGetValue(team.Prototype, out var list)) list.Remove(team);
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

using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace CnCNew.Simulation.World;

/// <summary>
/// The entities of a world: looked up by id, enumerated in ascending id order.
/// <para>
/// The order is the point. A plain dictionary enumerates in the order of its internal slots: a running world that lost objects and created others visits the
/// newcomers in the freed slots, a world restored from a snapshot (which registers in id order) visits them last. Every loop over the entities whose effect
/// depends on the order - the first match wins, effects applied in sequence, random numbers drawn per hit - could then decide differently in a continuous
/// world and in a restored one, and the order itself was a property of the runtime's dictionary. Here the order is the id order by construction, in both.
/// </para>
/// <para>
/// Ids only grow, so a new entity is appended. A removed entity leaves a hole that enumerations skip; <see cref="Compact"/> closes the holes at a point of the
/// tick where no enumeration is in flight. As with the dictionary, removing while enumerating is allowed (the removed entity is skipped if it was not reached
/// yet) and adding while enumerating is not relied upon. An enumeration that survives a reorganisation throws instead of visiting the wrong entities.
/// </para>
/// </summary>
internal sealed class EntityTable : ICollection<Entity>, IReadOnlyCollection<Entity>
{
    private const int CompactMinimumHoles = 64;

    private readonly Dictionary<uint, Entity> _byId = [];
    private Entity?[] _slots = new Entity?[256];
    private uint[] _ids = new uint[256];
    private int _used;
    private int _holes;
    private int _version;

    public int Count => _byId.Count;

    /// <summary>The entities in ascending id order (the table itself; kept so that call sites read like the dictionary they replaced).</summary>
    public EntityTable Values => this;

    public bool TryGetValue(uint id, [MaybeNullWhen(false)] out Entity entity) => _byId.TryGetValue(id, out entity);
    public Entity? GetValueOrDefault(uint id) => _byId.GetValueOrDefault(id);
    public bool ContainsKey(uint id) => _byId.ContainsKey(id);

    public void Add(uint id, Entity entity)
    {
        _byId.Add(id, entity);
        if (_used == _slots.Length)
        {
            Array.Resize(ref _slots, _used * 2);
            Array.Resize(ref _ids, _used * 2);
        }
        var index = _used;
        if (index > 0 && _ids[index - 1] >= id)
        {
            // Not the largest id so far (ids only grow, so this is a table filled out of order): keep the order, and stop enumerations that are in flight.
            index = ~Array.BinarySearch(_ids, 0, _used, id);
            if (index < 0) throw new InvalidOperationException($"Entity id {id} is already in the table.");
            Array.Copy(_slots, index, _slots, index + 1, _used - index);
            Array.Copy(_ids, index, _ids, index + 1, _used - index);
            _version++;
        }
        _slots[index] = entity;
        _ids[index] = id;
        _used++;
    }

    public bool Remove(uint id)
    {
        if (!_byId.Remove(id)) return false;
        var index = Array.BinarySearch(_ids, 0, _used, id);
        if (index < 0 || _slots[index] is null) throw new InvalidOperationException($"Entity id {id} is missing from the ordered table.");
        _slots[index] = null;
        _holes++;
        return true;
    }

    /// <summary>Closes the holes removed entities left, once they are at least half of the table. Only to be called where no enumeration is in flight.</summary>
    public void Compact()
    {
        if (_holes < CompactMinimumHoles || _holes * 2 < _used) return;
        var write = 0;
        for (var read = 0; read < _used; read++)
        {
            if (_slots[read] is not { } entity) continue;
            _slots[write] = entity;
            _ids[write] = _ids[read];
            write++;
        }
        Array.Clear(_slots, write, _used - write);
        _used = write;
        _holes = 0;
        _version++;
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    public bool Contains(Entity item) => item is not null && _byId.TryGetValue(item.Id.Value, out var found) && ReferenceEquals(found, item);

    public void CopyTo(Entity[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (arrayIndex < 0 || array.Length - arrayIndex < Count) throw new ArgumentException("The destination is too small.", nameof(array));
        for (var index = 0; index < _used; index++)
            if (_slots[index] is { } entity) array[arrayIndex++] = entity;
    }

    bool ICollection<Entity>.IsReadOnly => true;
    void ICollection<Entity>.Add(Entity item) => throw new NotSupportedException("Entities are registered by the world.");
    void ICollection<Entity>.Clear() => throw new NotSupportedException("Entities are removed by the world.");
    bool ICollection<Entity>.Remove(Entity item) => throw new NotSupportedException("Entities are removed by the world.");

    public struct Enumerator : IEnumerator<Entity>
    {
        private readonly EntityTable _table;
        private readonly int _version;
        private int _index;
        private Entity? _current;

        internal Enumerator(EntityTable table)
        {
            _table = table;
            _version = table._version;
            _index = -1;
            _current = null;
        }

        public readonly Entity Current => _current!;
        readonly object IEnumerator.Current => _current!;

        public bool MoveNext()
        {
            if (_version != _table._version) throw new InvalidOperationException("The entity table was reorganised while it was enumerated.");
            var slots = _table._slots;
            while (++_index < _table._used)
            {
                if (slots[_index] is not { } entity) continue;
                _current = entity;
                return true;
            }
            _current = null;
            return false;
        }

        public void Reset() => throw new NotSupportedException();
        public readonly void Dispose() { }
    }
}

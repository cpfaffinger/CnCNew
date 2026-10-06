using CnCNew.Simulation.Ids;

namespace CnCNew.Simulation.World;

/// <summary>
/// Entities in ascending id order, as one view of the world's table: the answer a scan over <see cref="SimulationWorld.Entities"/> with the list's filter
/// gives, members and order. Read-only for callers; the world changes it where the table or the filtered property changes. A caller must not change the
/// list's membership while it walks it (take a copy first, as with the table).
/// </summary>
public sealed class EntityList
{
    private Entity[] _items = new Entity[8];
    private int _count;

    public int Count => _count;
    public Entity this[int index] => index < _count ? _items[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public ReadOnlySpan<Entity> AsSpan() => new(_items, 0, _count);

    internal void Insert(Entity entity)
    {
        var id = entity.Id.Value;
        var index = _count;
        // Ids only grow, so a new entity is the last one; an owner change or a table filled out of order lands in between.
        if (_count > 0 && _items[_count - 1].Id.Value >= id)
        {
            index = Find(id);
            if (index >= 0) throw new InvalidOperationException($"Entity id {id} is already in the list.");
            index = ~index;
        }
        if (_count == _items.Length) Array.Resize(ref _items, _count * 2);
        if (index < _count) Array.Copy(_items, index, _items, index + 1, _count - index);
        _items[index] = entity;
        _count++;
    }

    internal void Remove(Entity entity)
    {
        var index = Find(entity.Id.Value);
        if (index < 0) throw new InvalidOperationException($"Entity id {entity.Id.Value} is missing from the list.");
        _count--;
        if (index < _count) Array.Copy(_items, index + 1, _items, index, _count - index);
        _items[_count] = null!;
    }

    private int Find(uint id)
    {
        var low = 0;
        var high = _count - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var value = _items[middle].Id.Value;
            if (value == id) return middle;
            if (value < id) low = middle + 1;
            else high = middle - 1;
        }
        return ~low;
    }
}

/// <summary>
/// Views of the entity table that replace scans over all entities: the entities of one owner and the entities of one kind, each in ascending id order.
/// They are caches of what the table says, not state: neither hashed nor saved, built by one pass over the table when first asked for (so a world that never
/// asks pays nothing, and a restored world builds the same lists from the same table) and kept exact afterwards at the three places where the answer can
/// change - an entity is registered, removed, or changes its owner.
/// </summary>
public sealed partial class SimulationWorld
{
    private static readonly EntityList NoEntities = new();

    private Dictionary<int, EntityList>? _byOwner;
    private readonly Dictionary<string, EntityList> _byKind = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The registered entities whose <see cref="Entity.Owner"/> is <paramref name="owner"/>, in ascending id order: exactly the entities a scan over
    /// <see cref="Entities"/> with that test visits, in that order. Objects requested but not spawned yet are not in it, objects marked for destruction are
    /// until the world removes them - as in the table.
    /// </summary>
    public EntityList EntitiesOwnedBy(PlayerId owner)
    {
        if (_byOwner is not { } index)
        {
            _byOwner = index = [];
            foreach (var entity in _entities.Values) OwnerList(index, entity.Owner).Insert(entity);
        }
        return index.TryGetValue(owner.Value, out var list) ? list : NoEntities;
    }

    /// <summary>
    /// The registered entities whose template has the kind (<see cref="Templates.ObjectTemplate.IsKindOf"/>, so without regard to case), in ascending id
    /// order: exactly what a scan over <see cref="Entities"/> with that test visits. A template's kinds never change, so only registering and removing
    /// entities changes the answer.
    /// </summary>
    public EntityList EntitiesOfKind(string kind)
    {
        if (_byKind.TryGetValue(kind, out var list)) return list;
        list = new EntityList();
        foreach (var entity in _entities.Values)
            if (entity.Template.IsKindOf(kind)) list.Insert(entity);
        _byKind.Add(kind, list);
        return list;
    }

    private static EntityList OwnerList(Dictionary<int, EntityList> index, PlayerId owner)
    {
        if (!index.TryGetValue(owner.Value, out var list)) index.Add(owner.Value, list = new EntityList());
        return list;
    }

    /// <summary>Called where an entity enters the table (<c>Register</c>).</summary>
    private void IndexEntity(Entity entity)
    {
        if (_byOwner is { } index) OwnerList(index, entity.Owner).Insert(entity);
        foreach (var (kind, list) in _byKind)
            if (entity.Template.IsKindOf(kind)) list.Insert(entity);
    }

    /// <summary>Called where an entity leaves the table (<c>ProcessDestroys</c>).</summary>
    private void UnindexEntity(Entity entity)
    {
        if (_byOwner is { } index) OwnerList(index, entity.Owner).Remove(entity);
        foreach (var (kind, list) in _byKind)
            if (entity.Template.IsKindOf(kind)) list.Remove(entity);
    }

    /// <summary>The one place that assigns <see cref="Entity.Owner"/> of a registered entity, so that the owner lists follow.</summary>
    private void SetOwner(Entity entity, PlayerId newOwner)
    {
        if (entity.Owner == newOwner) return;
        if (_byOwner is { } index && entity.Registered)
        {
            OwnerList(index, entity.Owner).Remove(entity);
            entity.Owner = newOwner;
            OwnerList(index, newOwner).Insert(entity);
        }
        else entity.Owner = newOwner;
    }
}

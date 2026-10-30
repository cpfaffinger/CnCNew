using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>The <c>SpecialPowerType</c> of a <see cref="SpecialAbilityUpdate"/>: what its trigger does (<c>triggerAbilityEffect</c>).</summary>
internal enum AbilityKind : byte
{
    None,
    InfantryCapture,
    BlackLotusCapture,
    TimedCharges,
    RemoteCharges,
    TankHunterTnt,
    BoobyTrap,
}

/// <summary>
/// F-83: what the abilities other than a capture do. The machine of approach, unpacking, preparation, trigger and packing is <c>SpecialAbilityStaging.cs</c>; here are the targets each ability
/// takes (<c>ActionManager::canDoSpecialPowerAtObject</c>), its special objects (<c>createSpecialObject</c>, <c>validateSpecialObjects</c>, <c>killSpecialObjects</c>) and its trigger.
/// </summary>
public sealed partial class SpecialAbilityUpdate
{
    private static readonly int BoobyTrappedBit = ObjectStatusBits.Index("BOOBY_TRAPPED");

    /// <summary>The objects this ability has out in the world: the charges it has planted (<c>m_specialObjectIDList</c>), in the order they were made.</summary>
    private readonly List<EntityId> _specialObjects = [];

    /// <summary>How many of its special objects exist (<c>getSpecialObjectCount</c>).</summary>
    public int SpecialObjectCount(IWorldServices world)
    {
        ValidateSpecialObjects(world);
        return _specialObjects.Count;
    }

    private AbilityKind KindOf(IWorldServices world)
    {
        if (PowerIndex < 0) return AbilityKind.None;
        var type = world.SpecialPowers[PowerIndex].Type;
        foreach (var (name, kind) in Kinds)
            if (type.Equals(name, StringComparison.OrdinalIgnoreCase)) return kind;
        return AbilityKind.None;
    }

    private static readonly (string Name, AbilityKind Kind)[] Kinds =
    [
        ("SPECIAL_INFANTRY_CAPTURE_BUILDING", AbilityKind.InfantryCapture), ("SPECIAL_BLACKLOTUS_CAPTURE_BUILDING", AbilityKind.BlackLotusCapture),
        ("SPECIAL_TIMED_CHARGES", AbilityKind.TimedCharges), ("SPECIAL_REMOTE_CHARGES", AbilityKind.RemoteCharges),
        ("SPECIAL_TANKHUNTER_TNT_ATTACK", AbilityKind.TankHunterTnt), ("SPECIAL_BOOBY_TRAP", AbilityKind.BoobyTrap),
    ];

    /// <summary><c>ActionManager::canDoSpecialPowerAtObject</c> and its siblings for this ability's kind: what the order may name.</summary>
    private bool TakesTarget(IWorldServices world, in SpecialPowerTarget target)
    {
        var kind = KindOf(world);
        if (target.Kind == SpecialPowerTargetKind.None) return kind == AbilityKind.RemoteCharges;
        if (target.Kind != SpecialPowerTargetKind.Object || world.FindEntity(target.Entity) is not { EffectivelyDead: false } victim) return false;
        var relation = world.RelationshipOf(Entity.Owner, victim.Owner);
        switch (kind)
        {
            case AbilityKind.InfantryCapture or AbilityKind.BlackLotusCapture:
                return StagedCaptureRules.CanCapture(world, Entity, victim) && CrateModules.AiOf(Entity) is not null;
            case AbilityKind.TankHunterTnt:
                return victim.Template.IsKindOf("STRUCTURE") || (victim.Template.IsKindOf("VEHICLE") && !victim.Template.IsKindOf("AIRCRAFT"));
            case AbilityKind.BoobyTrap:
                return victim.Template.IsKindOf("STRUCTURE") && relation != Relationship.Enemies;
            case AbilityKind.TimedCharges or AbilityKind.RemoteCharges:
                if (victim.Template.IsKindOf("BRIDGE") || victim.Template.IsKindOf("BRIDGE_TOWER")) return false;
                if (!victim.Template.IsKindOf("STRUCTURE") && !victim.Template.IsKindOf("VEHICLE")) return false;
                if (SpecialObjectCount(world) >= data.MaxSpecialObjects) return false;
                // "Restrict the unit from placing more than one charge on the same building", and from placing timed charges where it has remote ones, or the other way round.
                foreach (var module in Entity.ModuleArray)
                    if (module is SpecialAbilityUpdate { HasChargeKind: true } charges && charges.HasChargeOn(world, victim.Id)) return false;
                return true;
            default:
                return false;
        }
    }

    private bool HasChargeKind => data.SpecialObject.Length > 0;

    private bool HasChargeOn(IWorldServices world, EntityId victim)
    {
        ValidateSpecialObjects(world);
        foreach (var id in _specialObjects)
            if (world.FindEntity(id) is { } charge && charge.ModuleArray.OfType<StickyBombUpdate>().FirstOrDefault() is { } sticky && sticky.Target == victim) return true;
        return false;
    }

    /// <summary><c>createSpecialObject</c>: over the limit a persistent kind makes none, another kind removes what it has. The object appears where the unit stands, with its heading.</summary>
    private EntityId CreateSpecialObject(IWorldServices world, Entity? stickTo)
    {
        ValidateSpecialObjects(world);
        if (_specialObjects.Count >= data.MaxSpecialObjects)
        {
            if (data.SpecialObjectsPersistent) return EntityId.Invalid;
            KillSpecialObjects(world);
        }
        if (!world.Templates.TryGet(data.SpecialObject, out var template)) return EntityId.Invalid;
        var made = world.RequestSpawn(template.Index, Entity.Owner, Entity.X, Entity.Y, Entity.Z, Entity.Angle,
            new SpawnExtras(Fixed.Zero, StickTo: stickTo?.Id ?? default, Bomber: Entity.Id, Producer: Entity.Id));
        _specialObjects.Add(made);
        world.MarkDirty(Entity);
        return made;
    }

    /// <summary><c>validateSpecialObjects</c>: charges go off, are cleared or are shot; what is gone leaves the list and frees its place. An object made in this tick is still on its way.</summary>
    private void ValidateSpecialObjects(IWorldServices world)
    {
        for (var index = _specialObjects.Count - 1; index >= 0; index--)
        {
            if (world.FindEntity(_specialObjects[index]) is not null || world.IsSpawnPending(_specialObjects[index])) continue;
            _specialObjects.RemoveAt(index);
            world.MarkDirty(Entity);
        }
    }

    /// <summary><c>killSpecialObjects</c>.</summary>
    private void KillSpecialObjects(IWorldServices world)
    {
        foreach (var id in _specialObjects)
            if (world.FindEntity(id) is { } made) world.RequestDestroy(made.Id);
        if (_specialObjects.Count > 0) world.MarkDirty(Entity);
        _specialObjects.Clear();
    }

    /// <summary>The ability's own part of <c>triggerAbilityEffect</c>; false when the rest of the trigger (losing stealth) is not to happen.</summary>
    private bool TriggerKind(IWorldServices world, Entity? target, AbilityKind kind)
    {
        switch (kind)
        {
            case AbilityKind.TankHunterTnt or AbilityKind.TimedCharges or AbilityKind.BoobyTrap:
                if (target is null) return false;
                // "Whoops, it was mined. Cancel if it or us is now dead."
                if (StickyBombUpdate.CheckAndDetonateBoobyTrap(world, target, Entity) && (target.EffectivelyDead || Entity.EffectivelyDead)) return false;
                // "We can't double booby trap something."
                if (kind == AbilityKind.BoobyTrap && target.HasStatus(BoobyTrappedBit)) return false;
                CreateSpecialObject(world, target);
                return true;
            case AbilityKind.RemoteCharges:
                if (target is not null && StickyBombUpdate.CheckAndDetonateBoobyTrap(world, target, Entity) && (target.EffectivelyDead || Entity.EffectivelyDead)) return false;
                if (target is null)
                {
                    // "If there is no target object nor position, then we are detonating the existing charges": the unit keeps its stealth.
                    var fired = false;
                    foreach (var id in _specialObjects.ToArray())
                    {
                        if (world.FindEntity(id)?.ModuleArray.OfType<StickyBombUpdate>().FirstOrDefault() is not { } sticky) continue;
                        sticky.Detonate(world);
                        fired = true;
                    }
                    return !fired;
                }
                CreateSpecialObject(world, target);
                return true;
            default:
                return true;
        }
    }

    private void DescribeSpecialObjects(IStateWriter writer)
    {
        if (_specialObjects.Count == 0) return;
        writer.BeginArray("specialObjects");
        foreach (var id in _specialObjects)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadSpecialObjects(IStateReader reader)
    {
        _specialObjects.Clear();
        if (!reader.Has("specialObjects")) return;
        foreach (var item in reader.Array("specialObjects")) _specialObjects.Add(new EntityId(checked((uint)item.UInt("id"))));
    }
}

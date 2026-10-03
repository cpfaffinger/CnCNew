using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

// ObjectCreationList.cpp: ObjectCreationList::create, GenericObjectCreationNugget::create/reallyCreate/doStuffToObj.
// The terrain, physics, pathfinding and drawable steps of the reference do not exist in the simulation yet; every such
// step that changes what a list produces is reported through CreationSkipped instead of being dropped (docs/SIMULATION-CORE.md).
public sealed partial class SimulationWorld
{
    public EntityId CreateFromList(Entity source, string listName, EntityId requester, bool transferPreviousHealth)
        => CreateFromList(source, listName, requester, transferPreviousHealth, source.X, source.Y, source.Z);

    public EntityId CreateFromList(Entity source, string listName, EntityId requester, bool transferPreviousHealth, Fixed originX, Fixed originY, Fixed originZ)
    {
        ArgumentNullException.ThrowIfNull(source);
        var origin = (X: originX, Y: originY, Z: originZ);
        Emit(SimEventKind.CreationListRequested, source.Id, requester, listName, transferPreviousHealth ? 1 : 0);
        if (!_templates.CreationLists.TryGet(listName, out var list))
        {
            Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, listName, CreationSkipReasons.UnknownList);
            return EntityId.Invalid;
        }

        var first = EntityId.Invalid;
        foreach (var nugget in list.Nuggets)
        {
            var created = ExecuteNugget(source, nugget, origin);
            if (!first.IsValid) first = created;
        }

        if (first.IsValid && transferPreviousHealth && source.Body is ActiveBody body)
        {
            var amount = body.MaxHealth - body.PreviousHealth;
            if (amount > Fixed.Zero) UpdateSetup(first, setup => setup with { TransferDamage = amount, TransferSource = requester });
        }
        return first;
    }

    private void UpdateSetup(EntityId id, Func<SpawnSetup, SpawnSetup> change)
    {
        for (var index = _spawnQueue.Count - 1; index >= 0; index--)
        {
            if (_spawnQueue[index].Id != id) continue;
            _spawnQueue[index] = _spawnQueue[index] with { Setup = change(_spawnQueue[index].Setup) };
            return;
        }
        throw new InvalidOperationException($"Spawn {id.Value} is not queued.");
    }

    private EntityId ExecuteNugget(Entity source, CreationNugget nugget, (Fixed X, Fixed Y, Fixed Z) origin)
    {
        if (nugget.Kind != CreationNuggetKind.CreateObject)
        {
            Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, nugget.Kind.ToString(), CreationSkipReasons.NuggetNotSimulated);
            return EntityId.Invalid;
        }
        if (nugget.SkipIfSignificantlyAirborne && IsSignificantlyAboveTerrain(source)) return EntityId.Invalid;
        if (nugget.Objects.IsEmpty)
        {
            Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, nugget.Kind.ToString(), CreationSkipReasons.NoObjectNamed);
            return EntityId.Invalid;
        }
        // reallyCreate: "don't spawn useful objects for dead players". The neutral player is always active.
        if (nugget.RequiresLivePlayer && !source.Owner.IsSystem && IsPlayerDefeated(source.Owner)) return EntityId.Invalid;

        var owner = source.Owner;
        var first = EntityId.Invalid;
        var container = EntityId.Invalid;
        if (nugget.PutInContainer.Length > 0)
        {
            if (!_templates.TryGet(nugget.PutInContainer, out var containerTemplate))
                Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, nugget.PutInContainer, CreationSkipReasons.UnknownTemplate);
            else if (!containerTemplate.IsInstantiable)
            {
                Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, nugget.PutInContainer, CreationSkipReasons.ContainerNotInstantiable);
                return EntityId.Invalid;
            }
            else
            {
                // The container is created before its contents (newObject) but positioned last (doStuffToObj), so its slot is filled in below.
                container = RequestSpawn(containerTemplate.Index, owner, origin.X, origin.Y, origin.Z, Fixed.Zero);
                first = container;
            }
        }
        ReportUnsimulatedSettings(source, nugget);

        for (var index = 0; index < nugget.Count; index++)
        {
            var pick = LogicRandom.Range(0, nugget.Objects.Length - 1);
            var name = nugget.Objects[pick];
            if (!_templates.TryGet(name, out var template))
            {
                Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, name, CreationSkipReasons.UnknownTemplate);
                continue;
            }
            if (!template.IsInstantiable)
            {
                Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, name, CreationSkipReasons.TemplateNotInstantiable);
                continue;
            }

            var (x, y) = (origin.X, origin.Y);
            if (nugget.SpreadFormation)
            {
                // findPositionAround without terrain or obstacles accepts the innermost ring: one random start angle, minRadius away.
                var minRadius = LogicRandom.Range(nugget.MinDistanceA, nugget.MinDistanceB);
                var start = LogicRandom.Range(Fixed.Zero, FixedTrig.TwoPi);
                if (minRadius <= nugget.MaxDistance)
                {
                    x += minRadius * FixedTrig.Cos(start);
                    y += minRadius * FixedTrig.Sin(start);
                }
            }
            var id = PlaceAndQueue(source, nugget, template, owner, x, y, origin.Z, container);
            if (!first.IsValid) first = id;
        }

        if (container.IsValid)
        {
            var (setup, placement) = Configure(source, nugget, _templates[QueuedTemplate(container)], origin.X, origin.Y, origin.Z);
            UpdateSetup(container, _ => setup);
            UpdatePlacement(container, placement);
        }
        return first;
    }

    private int QueuedTemplate(EntityId id)
    {
        foreach (var pending in _spawnQueue)
            if (pending.Id == id) return pending.Template;
        throw new InvalidOperationException($"Spawn {id.Value} is not queued.");
    }

    private void UpdatePlacement(EntityId id, (Fixed X, Fixed Y, Fixed Z, Fixed Angle) placement)
    {
        for (var index = _spawnQueue.Count - 1; index >= 0; index--)
        {
            if (_spawnQueue[index].Id != id) continue;
            _spawnQueue[index] = _spawnQueue[index] with { X = placement.X, Y = placement.Y, Z = placement.Z, Angle = placement.Angle };
            return;
        }
    }

    private EntityId PlaceAndQueue(Entity source, CreationNugget nugget, ObjectTemplate template, PlayerId owner, Fixed x, Fixed y, Fixed z, EntityId container)
    {
        var (setup, placement) = Configure(source, nugget, template, x, y, z);
        var id = RequestSpawn(template.Index, owner, placement.X, placement.Y, placement.Z, placement.Angle);
        UpdateSetup(id, _ => setup with { Container = container });
        return id;
    }

    /// <summary>The part of <c>doStuffToObj</c> that can be modeled: health, lifetime, veterancy, containment and placement.</summary>
    private (SpawnSetup Setup, (Fixed X, Fixed Y, Fixed Z, Fixed Angle) Placement) Configure(Entity source, CreationNugget nugget, ObjectTemplate template, Fixed baseX, Fixed baseY, Fixed baseZ)
    {
        var offset = Rotate(nugget.OffsetX, nugget.OffsetY, source.Angle);
        var (x, y, z) = (baseX + offset.X, baseY + offset.Y, baseZ + nugget.OffsetZ);
        var angle = Fixed.Zero;
        var disposition = nugget.Disposition;
        var healthPercent = LogicRandom.Range(nugget.MinHealth, nugget.MaxHealth);
        if (disposition.HasFlag(CreationDisposition.LikeExisting)) angle = source.Angle;
        if (disposition.HasFlag(CreationDisposition.OnGroundAligned))
        {
            angle = LogicRandom.Range(Fixed.Zero, FixedTrig.TwoPi);
            z = Fixed.Zero;
        }
        if (disposition.HasFlag(CreationDisposition.SendItOut))
        {
            angle = LogicRandom.Range(Fixed.Zero, FixedTrig.TwoPi);
            z = Fixed.Zero;
        }
        if ((disposition & (CreationDisposition.SendItFlying | CreationDisposition.SendItUp | CreationDisposition.RandomForce)) != 0) angle = source.Angle;

        var lifetimeMax = nugget.MaxLifetimeTicks;
        var setup = new SpawnSetup(true, checked((int)(healthPercent * Fixed.FromInt(100)).Floor()),
            nugget.InheritsVeterancy && template.IsTrainable, nugget.InheritsVeterancy ? source.VeterancyLevel : 0,
            lifetimeMax > 0 ? nugget.MinLifetimeTicks : 0, lifetimeMax > 0 ? lifetimeMax : 0,
            EntityId.Invalid, nugget.ContainInsideSourceObject ? source.Id : EntityId.Invalid, Fixed.Zero, EntityId.Invalid,
            Enslaver: template.Modules.Any(spec => spec.Binding.Name is "SlavedUpdate" or "MobMemberSlavedUpdate") ? source.Id : EntityId.Invalid);
        return (setup, (x, y, z, angle));
    }

    private static (Fixed X, Fixed Y) Rotate(Fixed x, Fixed y, Fixed angle)
    {
        if (x == Fixed.Zero && y == Fixed.Zero) return (Fixed.Zero, Fixed.Zero);
        var (sin, cos) = (FixedTrig.Sin(angle), FixedTrig.Cos(angle));
        return (x * cos - y * sin, x * sin + y * cos);
    }

    private void ReportUnsimulatedSettings(Entity source, CreationNugget nugget)
    {
        if (nugget.InvulnerableTicks > 0)
            Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, "InvulnerableTime", CreationSkipReasons.SettingNotSimulated);
        if (nugget.DiesOnBadLand)
            Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, "DiesOnBadLand", CreationSkipReasons.SettingNotSimulated);
    }

    /// <summary><c>Thing::isSignificantlyAboveTerrain</c>: higher than three frames of free fall above the terrain height (flat 0 without terrain).</summary>
    public bool IsSignificantlyAboveTerrain(Entity entity) => IsSignificantlyAbove(entity.Z, GroundHeightAt(entity.X, entity.Y));

    private bool IsSignificantlyAbove(Fixed z, Fixed ground) => z - ground > Fixed.FromDecimal(-9m * _templates.Gravity / 900m);
}

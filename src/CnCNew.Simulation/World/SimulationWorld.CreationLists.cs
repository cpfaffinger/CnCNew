using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

// ObjectCreationList.cpp: ObjectCreationList::create, GenericObjectCreationNugget::create/reallyCreate/doStuffToObj.
// The terrain, physics, pathfinding and drawable steps of the reference do not exist in the simulation yet; every such
// step that changes what a list produces is reported through CreationSkipped instead of being dropped (docs/SIMULATION-CORE.md).
public sealed partial class SimulationWorld
{
    public EntityId CreateFromList(Entity source, string listName, EntityId requester, bool transferPreviousHealth)
        => CreateFromList(source, listName, requester, transferPreviousHealth, source.X, source.Y, source.Z);

    public EntityId CreateFromList(Entity source, string listName, EntityId requester, bool transferPreviousHealth, Fixed originX, Fixed originY, Fixed originZ)
        => CreateFromList(source, listName, requester, transferPreviousHealth, originX, originY, originZ, originX, originY, originZ, true);

    public EntityId CreateFromList(Entity source, string listName, EntityId requester, bool transferPreviousHealth, Fixed originX, Fixed originY, Fixed originZ, Fixed targetX, Fixed targetY, Fixed targetZ, bool createOwner)
    {
        ArgumentNullException.ThrowIfNull(source);
        var origin = (X: originX, Y: originY, Z: originZ);
        var target = (X: targetX, Y: targetY, Z: targetZ);
        Emit(SimEventKind.CreationListRequested, source.Id, requester, listName, transferPreviousHealth ? 1 : 0);
        if (!_templates.CreationLists.TryGet(listName, out var list))
        {
            Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, listName, CreationSkipReasons.UnknownList);
            return EntityId.Invalid;
        }

        var first = EntityId.Invalid;
        foreach (var nugget in list.Nuggets)
        {
            EntityId created;
            if (nugget.Kind == CreationNuggetKind.DeliverPayload) created = ExecuteDeliverPayload(source, nugget, origin, target, createOwner);
            else if (nugget.Kind == CreationNuggetKind.FireWeapon && nugget.Weapon.Length > 0)
            {
                // FireWeaponNugget::create: createAndFireTempWeapon(weapon, primaryObj, secondary): the source fires once at the second point.
                FireTempWeapon(source, nugget.Weapon, target.X, target.Y, target.Z);
                created = EntityId.Invalid;
            }
            else created = ExecuteNugget(source, nugget, origin);
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

    /// <summary>
    /// <c>DeliverPayloadNugget::create</c> (<c>ObjectCreationList.cpp</c>): <c>FormationSize</c> transports start at the creation point (the formation is laid out sideways of the line from the
    /// target to the start, alternating sides, <c>FormationSpacing</c> apart), each carrying the payload objects (optionally packed into <c>PutInContainer</c> objects) and told to fly the run
    /// (<see cref="Modules.BuiltIn.DeliverPayloadAIUpdate.DeliverPayload"/>). With <paramref name="createOwner"/> false the source object is the transport. The first transport/payload of the
    /// whole nugget reports to <c>SpecialPowerCompletionDie</c>. Not modelled: <c>StartAtMaxSpeed</c> (a physics push; the kinematic plane starts at its speed) and the delivery decal.
    /// </summary>
    private EntityId ExecuteDeliverPayload(Entity source, CreationNugget nugget, (Fixed X, Fixed Y, Fixed Z) origin, (Fixed X, Fixed Y, Fixed Z) target, bool createOwner)
    {
        var spec = nugget.Delivery!;
        ObjectTemplate? transportTemplate = null;
        if (createOwner)
        {
            if (!_templates.TryGet(spec.Transport, out transportTemplate)) { Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, spec.Transport, CreationSkipReasons.UnknownTemplate); return EntityId.Invalid; }
            if (!transportTemplate.IsInstantiable) { Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, spec.Transport, CreationSkipReasons.TemplateNotInstantiable); return EntityId.Invalid; }
        }
        ObjectTemplate? containerTemplate = null;
        if (spec.PutInContainer.Length > 0)
        {
            if (!_templates.TryGet(spec.PutInContainer, out containerTemplate)) { Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, spec.PutInContainer, CreationSkipReasons.UnknownTemplate); return EntityId.Invalid; }
            if (!containerTemplate.IsInstantiable) { Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, spec.PutInContainer, CreationSkipReasons.ContainerNotInstantiable); return EntityId.Invalid; }
        }
        var payloads = new List<ObjectTemplate>();
        foreach (var (name, count) in spec.Payload)
        {
            if (!_templates.TryGet(name, out var payloadTemplate)) { Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, name, CreationSkipReasons.UnknownTemplate); return EntityId.Invalid; }
            if (!payloadTemplate.IsInstantiable) { Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, name, CreationSkipReasons.TemplateNotInstantiable); return EntityId.Invalid; }
            for (var index = 0; index < count; index++) payloads.Add(payloadTemplate);
        }

        // The formation offsets: the unit vector from the target to the start, turned 90 degrees both ways and added to itself (as the reference computes them).
        Fixed ccwX = Fixed.Zero, ccwY = Fixed.Zero, cwX = Fixed.Zero, cwY = Fixed.Zero;
        if (spec.FormationSize > 1)
        {
            var dx = origin.X - target.X;
            var dy = origin.Y - target.Y;
            var length = Fixed.Sqrt(dx * dx + dy * dy);
            if (length > Fixed.Zero) { dx /= length; dy /= length; }
            ccwX = dx - dy;
            ccwY = dx + dy;
            cwX = dx + dy;
            cwY = dy - dx;
        }

        var run = spec.Run;
        var firstTransport = EntityId.Invalid;
        for (var formation = 0; formation < spec.FormationSize; formation++)
        {
            var multiplier = Fixed.FromInt((formation + 1) / 2 * spec.FormationSpacing.Floor());
            var offsetX = (formation % 2 != 0 ? ccwX : cwX) * multiplier;
            var offsetY = (formation % 2 != 0 ? ccwY : cwY) * multiplier;
            var startX = origin.X + offsetX;
            var startY = origin.Y + offsetY;
            var moveX = target.X + offsetX;
            var moveY = target.Y + offsetY;
            var targetX = target.X + offsetX * (Fixed.One - spec.ConvergenceFactor);
            var targetY = target.Y + offsetY * (Fixed.One - spec.ConvergenceFactor);
            if (spec.ErrorRadius > Fixed.One && formation > 0)
            {
                var radius = LogicRandom.Range(Fixed.Zero, spec.ErrorRadius);
                var angle = LogicRandom.Range(Fixed.Zero, FixedTrig.TwoPi);
                targetX += radius * FixedTrig.Cos(angle);
                targetY += radius * FixedTrig.Sin(angle);
            }
            var orient = FixedTrig.Atan2(moveY - startY, moveX - startX);
            if (run.DeliveryDistance > Fixed.Zero)
            {
                var slop = run.DeliveryDistance * Fixed.FromDecimal(1.5m);
                startX -= FixedTrig.Cos(orient) * slop;
                startY -= FixedTrig.Sin(orient) * slop;
            }
            // DelayDeliveryMax is a duration in logic frames of the original (INI::parseDurationUnsignedInt): the delay is drawn in frames and only then scaled to ticks, so it
            // stays on the original's grid at every tick rate.
            long delay = spec.DelayDeliveryMaxTicks > 0 ? Time.OriginalFrames(LogicRandom.Range(0, checked((int)Time.ToOriginalFrames(spec.DelayDeliveryMaxTicks)))) : 0;
            var order = new Modules.BuiltIn.DeliveryOrder(moveX, moveY, targetX, targetY, run, delay);

            var transport = source.Id;
            var creatorOfTransport = formation == 0 ? source.Id : EntityId.Invalid;
            if (createOwner)
            {
                var z = spec.StartAtPreferredHeight && transportTemplate!.Locomotor is { } locomotor ? GroundHeightAt(startX, startY) + locomotor.PreferredHeight : origin.Z;
                transport = RequestSpawn(transportTemplate!.Index, source.Owner, startX, startY, z, orient);
                UpdateSetup(transport, setup => setup with { Delivery = order, SetCreator = true, Creator = creatorOfTransport });
            }
            else if (AiOf(source) is Modules.BuiltIn.DeliverPayloadAIUpdate owner)
            {
                owner.DeliverPayload(this, order);
                foreach (var module in source.ModuleArray)
                    if (module is Modules.BuiltIn.SpecialPowerCompletionDie completion) completion.SetCreator(creatorOfTransport);
            }
            else
            {
                Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, "DeliverPayloadAIUpdate", CreationSkipReasons.TemplateNotInstantiable);
                return EntityId.Invalid;
            }
            if (!firstTransport.IsValid) firstTransport = transport;

            var carried = 0;
            foreach (var payloadTemplate in payloads)
            {
                var creator = formation == 0 && carried == 0 ? source.Id : EntityId.Invalid;
                var holder = transport;
                // The container (a parachute) is queued before what it carries: spawns appear in the order they were requested, and a rider whose container does not exist yet
                // stays outside (the paradrop of the special power dropped nobody).
                if (containerTemplate is not null)
                {
                    var container = RequestSpawn(containerTemplate.Index, source.Owner, startX, startY, origin.Z, orient);
                    UpdateSetup(container, setup => setup with { Container = transport, SetCreator = true, Creator = creator });
                    holder = container;
                }
                var payload = RequestSpawn(payloadTemplate.Index, source.Owner, startX, startY, origin.Z, orient);
                UpdateSetup(payload, setup => setup with { Container = holder, SetCreator = true, Creator = creator });
                carried++;
            }
        }
        return firstTransport;
    }

    public EntityId LaunchProjectileObject(Entity launcher, int templateIndex, WeaponInfo weapon, Fixed startX, Fixed startY, Fixed startZ, Fixed targetX, Fixed targetY, Fixed targetZ)
    {
        var id = Ids.AllocateEntity();
        var launch = new Modules.ProjectileLaunch(weapon.Index, launcher.Id, EntityId.Invalid, targetX, targetY, targetZ, launcher.WeaponBonusConditions);
        _spawnQueue.Add(new PendingSpawn(id, templateIndex, launcher.Owner, startX, startY, startZ, launcher.Angle, default(SpawnSetup) with { Launch = launch }));
        EmitAt(SimEventKind.ProjectileLaunched, id, launcher.Id, weapon.Name, 0, targetX, targetY, targetZ);
        return id;
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
        // ObjectCreationList.cpp reallyCreate: obj->setProducer(sourceObj). Only recorded for objects with a module that reads it.
        var producer = source.Id;
        var wantsProducer = template.Modules.Any(spec => Modules.ProducerAwareModules.Contains(spec.Name));
        UpdateSetup(id, _ => setup with { Container = container, Producer = wantsProducer ? producer : default });
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

        // ObjectCreationList::create(..., lifetimeFrames): a lifetime handed in by the caller (FireOCLAfterWeaponCooldownUpdate) replaces the nugget's own.
        var lifetimeMin = _listLifetimeOverride > 0 ? _listLifetimeOverride : nugget.MinLifetimeTicks;
        var lifetimeMax = _listLifetimeOverride > 0 ? _listLifetimeOverride : nugget.MaxLifetimeTicks;
        var setup = new SpawnSetup(true, checked((int)(healthPercent * Fixed.FromInt(100)).Floor()),
            nugget.InheritsVeterancy && template.IsTrainable, nugget.InheritsVeterancy ? source.VeterancyLevel : 0,
            lifetimeMax > 0 ? lifetimeMin : 0, lifetimeMax > 0 ? lifetimeMax : 0,
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

    /// <summary>The height of three logic frames of the original in free fall (<c>-(3*3) * m_gravity</c>); the same at every tick rate. Resolved once when the world is created.</summary>
    private readonly Fixed _significantHeight;

    private bool IsSignificantlyAbove(Fixed z, Fixed ground) => z - ground > _significantHeight;
}

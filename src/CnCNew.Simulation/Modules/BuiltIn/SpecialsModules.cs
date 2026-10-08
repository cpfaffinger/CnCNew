using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The unit and building specials that are not crates: the sticky bomb and booby trap (<c>StickyBombUpdate</c>), the GPS scrambler's stealth grant
/// (<c>GrantStealthBehavior</c>), the wandering object (<c>WanderAIUpdate</c>) and the toxin stream bookkeeping (<c>ProjectileStreamUpdate</c>). Each class cites its
/// reference file; what the simulation cannot express is stated there.
/// </summary>
internal static class SpecialsModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<StickyBombData>("StickyBombUpdate", ModuleOwnerKind.Object, (configuration, _) => StickyBombData.Parse(configuration), data => new StickyBombUpdate(data)),
        new ModuleBinding<GrantStealthData>("GrantStealthBehavior", ModuleOwnerKind.Object, (configuration, _) => GrantStealthData.Parse(configuration), data => new GrantStealthBehavior(data)),
        new ModuleBinding<AIUpdateData>("WanderAIUpdate", ModuleOwnerKind.Object, (configuration, _) => AIUpdateData.Parse(configuration), data => new WanderAIUpdate(data)),
        new ModuleBinding<NoFieldsData>("ProjectileStreamUpdate", ModuleOwnerKind.Object, (_, _) => NoFieldsData.Instance, _ => new ProjectileStreamUpdate()),
    ];

    public static Fixed DistanceSquared(Entity a, Entity b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}

// ============================================================================================================ StickyBombUpdate

/// <summary>
/// <c>StickyBombUpdateModuleData</c>: <c>OffsetZ</c> (default 10, the bomb rides on a vehicle's roof), the optional <c>GeometryBasedDamageWeapon</c> a booby trap deals
/// its damage with and its <c>GeometryBasedDamageFX</c>. <c>AttachToTargetBone</c> is a model detail.
/// </summary>
public sealed record StickyBombData(Fixed OffsetZ, string GeometryWeapon, string GeometryFx) : ModuleData
{
    public static StickyBombData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Real(configuration, "OffsetZ", Fixed.FromInt(10)),
        ModuleConfigReader.Text(configuration, "GeometryBasedDamageWeapon"),
        ModuleConfigReader.Text(configuration, "GeometryBasedDamageFX"));
}

/// <summary>
/// <c>StickyBombUpdate.cpp</c> (the timed charge on a victim, the remote C4 and the GLA booby trap): <see cref="InitStickyBomb"/> attaches the bomb to a victim — it rides at the
/// victim's position (plus <c>OffsetZ</c> on a mobile one; an immobile victim keeps the bomb where the bomber or the script put it, on the ground), and a <c>BOOBY_TRAP</c> kind
/// marks the victim <c>BOOBY_TRAPPED</c> so that touching it can set the trap off (<see cref="CheckAndDetonateBoobyTrap"/>). A timed bomb dies by its own <c>LifetimeUpdate</c>
/// (its die modules fire the weapon). <see cref="Detonate"/> clears the status and kills the bomb; with a <c>GeometryBasedDamageWeapon</c> it first damages everything
/// around the victim by the weapon's primary/secondary damage with both radii widened by the victim's bounding circle, regardless of <c>RadiusDamageAffects</c>.
/// The bomb follows its victim every tick and is removed when the victim is dead; a victim that dies while booby trapped sets the trap off first
/// (<c>Object::onDie</c>'s <c>checkAndDetonateBoobyTrap(NULL)</c>, called from the world's death handler). Not modelled: the ping sound, the producer link
/// (<c>setProducer</c>: nothing here creates a bomb from a weapon, so the bomber's goal object is never read at creation — callers use <see cref="InitStickyBomb"/>).
/// </summary>
public sealed class StickyBombUpdate(StickyBombData data) : SimModule, IUpdateModule
{
    private static readonly int BoobyTrappedBit = ObjectStatusBits.Index("BOOBY_TRAPPED");

    private long _nextWake = SimulationTiming.SleepForever;
    private EntityId _target;

    public override string ModuleName => "StickyBombUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary>The victim the bomb is attached to (invalid until <see cref="InitStickyBomb"/>).</summary>
    public EntityId Target => _target;

    private bool IsBoobyTrap => Entity.Template.IsKindOf("BOOBY_TRAP");

    /// <summary>
    /// <c>StickyBombUpdate::initStickyBomb(target, bomber, specificPos)</c>: attaches the (freshly created) bomb to <paramref name="target"/>. With <paramref name="hasPosition"/> the bomb
    /// sits at (<paramref name="x"/>, <paramref name="y"/>) on the ground — the script actions <c>NAMED_SET_BOOBYTRAPPED</c>/<c>TEAM_SET_BOOBYTRAPPED</c> place it at a random point of the victim's
    /// perimeter, see <see cref="InitStickyBombAtOffset"/>; without it an immobile victim and a <paramref name="bomber"/> put the bomb where the bomber stands, everything else on the victim.
    /// </summary>
    public void InitStickyBomb(IWorldServices world, Entity target, Entity? bomber, bool hasPosition = false, Fixed x = default, Fixed y = default)
    {
        _target = target.Id;
        if (hasPosition)
        {
            Entity.X = x;
            Entity.Y = y;
            Entity.Z = world.GroundHeightAt(x, y);
        }
        else if (target.Template.IsKindOf("IMMOBILE") && bomber is not null)
        {
            Entity.X = bomber.X;
            Entity.Y = bomber.Y;
            Entity.Z = world.GroundHeightAt(bomber.X, bomber.Y);
        }
        else
        {
            Entity.X = target.X;
            Entity.Y = target.Y;
            Entity.Z = target.Z + data.OffsetZ;
        }
        if (IsBoobyTrap) target.Status |= 1UL << BoobyTrappedBit;
        world.MarkDirty(target);
        world.MarkDirty(Entity);
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    /// <summary><see cref="InitStickyBomb"/> at a point given relative to the victim (the perimeter offset of the booby-trap script actions; the victim's heading is the caller's business).</summary>
    public void InitStickyBombAtOffset(IWorldServices world, Entity target, Fixed offsetX, Fixed offsetY)
        => InitStickyBomb(world, target, null, true, target.X + offsetX, target.Y + offsetY);

    public long Update(IWorldServices world)
    {
        if (!_target.IsValid) return SimulationTiming.SleepForever;
        var target = world.FindEntity(_target);
        if (target is null) return SimulationTiming.SleepNone;
        if (target.EffectivelyDead)
        {
            // A booby-trapped victim set the trap off when it died (the world calls CheckAndDetonateBoobyTrap from its death handler); any bomb still here is just removed.
            world.RequestDestroy(Entity.Id);
            return SimulationTiming.SleepNone;
        }
        if (target.Template.IsKindOf("IMMOBILE")) Entity.Z = world.GroundHeightAt(Entity.X, Entity.Y);
        else
        {
            Entity.X = target.X;
            Entity.Y = target.Y;
            Entity.Z = target.Z + data.OffsetZ;
        }
        return SimulationTiming.SleepNone;
    }

    /// <summary><c>StickyBombUpdate::detonate</c>.</summary>
    public void Detonate(IWorldServices world)
    {
        var victim = _target.IsValid ? world.FindEntity(_target) : null;
        if (data.GeometryWeapon.Length > 0 && victim is not null && world.Weapons.TryGet(data.GeometryWeapon, out var weapon))
        {
            // The weapon's radius is the reach beyond the victim's own outline: widen both radii by its bounding circle and hurt everybody in range, whoever they are.
            var circle = victim.Template.BoundingCircleRadius;
            var widened = weapon with
            {
                PrimaryRadius = weapon.PrimaryRadius + circle,
                SecondaryRadius = weapon.SecondaryRadius + circle,
                AffectsMask = WeaponInfo.AffectsSelf | WeaponInfo.AffectsAllies | WeaponInfo.AffectsEnemies | WeaponInfo.AffectsNeutrals,
            };
            world.DealWeaponDamage(Entity, widened, null, victim.X, victim.Y, victim.Z);
            if (data.GeometryFx.Length > 0) world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.GeometryFx, (widened.SecondaryRadius).Raw, victim.X, victim.Y, victim.Z);
        }
        if (IsBoobyTrap && victim is not null)
        {
            victim.Status &= ~(1UL << BoobyTrappedBit);
            world.MarkDirty(victim);
        }
        world.Kill(Entity.Id);
    }

    /// <summary>
    /// <c>Object::checkAndDetonateBoobyTrap</c>: when <paramref name="trapped"/> is <c>BOOBY_TRAPPED</c> and carries a booby-trap bomb, the bomb goes off — unless the bomb's owner is
    /// an ally of <paramref name="victim"/> ("friends don't touch friends' boobies"). The reference finds the bomb by distance and producer; the simulation by its stored target.
    /// Returns whether a bomb went off.
    /// </summary>
    public static bool CheckAndDetonateBoobyTrap(IWorldServices world, Entity trapped, Entity? victim)
    {
        if (!trapped.HasStatus(BoobyTrappedBit)) return false;
        foreach (var candidate in world.Entities)
        {
            if (candidate.DestroyRequested || !candidate.Template.IsKindOf("BOOBY_TRAP")) continue;
            foreach (var module in candidate.Modules)
            {
                if (module is not StickyBombUpdate bomb || bomb._target != trapped.Id) continue;
                if (victim is not null && world.RelationshipOf(candidate.Owner, victim.Owner) == Relationship.Allies) return false;
                bomb.Detonate(world);
                return true;
            }
        }
        return false;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        if (_target.IsValid) writer.UInt("target", _target.Value);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _target = reader.Has("target") ? new EntityId(checked((uint)reader.UInt("target"))) : EntityId.Invalid;
    }
}

// ============================================================================================================ GrantStealthBehavior

/// <summary><c>GrantStealthBehaviorModuleData</c>: the scan radius grows from <c>StartRadius</c> by <c>RadiusGrowRate</c> per tick up to <c>FinalRadius</c>; <c>KindOf</c> is what gets stealth. The radius particle system is presentation.</summary>
public sealed record GrantStealthData(Fixed StartRadius, Fixed FinalRadius, Fixed RadiusGrowRate, ImmutableArray<string> KindOf) : ModuleData
{
    public static GrantStealthData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Real(configuration, "StartRadius", Fixed.Zero), ModuleConfigReader.Real(configuration, "FinalRadius", Fixed.Zero),
        configuration.Time.PerOriginalFrameToPerTick(ModuleConfigReader.Real(configuration, "RadiusGrowRate", Fixed.Zero)), ModuleConfigReader.Flags(configuration, "KindOf")?.Names ?? []);
}

/// <summary>
/// <c>GrantStealthBehavior::update</c> (the GPS scrambler's marker object): every tick the scan radius grows by <c>RadiusGrowRate</c>; every living, allied object of an accepted
/// kind inside it (other than the marker itself) receives <see cref="StealthUpdate.ReceiveGrant"/> permanently; when the radius reaches <c>FinalRadius</c> the marker is
/// destroyed. Objects without a <c>StealthUpdate</c> are not affected, as in the reference. Not modelled: the visible riders of a <c>RiderChangeContain</c>.
/// </summary>
public sealed class GrantStealthBehavior(GrantStealthData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private Fixed _radius = data.StartRadius;

    public override string ModuleName => "GrantStealthBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    public long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        _radius += data.RadiusGrowRate;
        var finalScan = false;
        if (_radius >= data.FinalRadius)
        {
            _radius = data.FinalRadius;
            finalScan = true;
        }
        foreach (var candidate in world.Entities)
        {
            if (candidate.Id == Entity.Id || candidate.EffectivelyDead || candidate.DestroyRequested || candidate.ContainedBy.IsValid) continue;
            if (world.RelationshipOf(Entity.Owner, candidate.Owner) != Relationship.Allies) continue;
            var dx = candidate.X - Entity.X;
            var dy = candidate.Y - Entity.Y;
            if (!SquaredReach.Of(_radius).Contains(dx * dx + dy * dy)) continue;
            if (!data.KindOf.Any(candidate.Template.IsKindOf)) continue;
            foreach (var module in candidate.Modules)
                if (module is StealthUpdate stealth) stealth.ReceiveGrant(world, true, 0);
        }
        if (!finalScan) return SimulationTiming.SleepNone;
        world.RequestDestroy(Entity.Id);
        return SimulationTiming.SleepForever;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Fixed("radius", _radius);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _radius = reader.Fixed("radius");
    }
}

// ============================================================================================================ WanderAIUpdate

/// <summary>
/// <c>WanderAIUpdate::update</c> (the panicking civilian, the fire-wall segments): whenever the object stands idle it walks to a point 5..50 units further along both axes (one draw
/// per axis from the logic stream, x first), then runs the ordinary AI update; it never sleeps.
/// </summary>
public sealed class WanderAIUpdate(AIUpdateData data) : AIUpdateInterface(data)
{
    public override string ModuleName => "WanderAIUpdate";

    public override void OnSpawn(IWorldServices world) => NextWake = world.Tick + SimulationTiming.SleepNone;

    public override long Update(IWorldServices world)
    {
        // The reference looks once per logic frame. Above 30 Hz the look - two draws and an order - happens in the tick a frame begins with: an object whose order fails
        // stays idle and would otherwise draw twice and four times as often per game time, shifting every later draw of the world. The walk itself is stepped every tick.
        if (IsIdle && (world.Time.TicksPerOriginalFrame == 1 || world.Time.IsOriginalFrameBoundary(world.Tick)))
        {
            var x = Entity.X + Fixed.FromInt(world.LogicRandom.Range(5, 50));
            var y = Entity.Y + Fixed.FromInt(world.LogicRandom.Range(5, 50));
            OrderMove(world, x, y, false);
        }
        base.Update(world);
        return SimulationTiming.SleepNone;
    }
}

// ============================================================================================================ ProjectileStreamUpdate

/// <summary>
/// <c>ProjectileStreamUpdate.cpp</c>: the stream object of a spray weapon (the toxin tractor's, the flamethrower's) remembers the projectiles the weapon fired in a circular
/// buffer of 20-odd slots, with a hole wherever the target changed, so the stream drawing can connect them; leading projectiles that are gone are dropped from the front, and
/// the stream removes itself once its buffer is empty and the object that owns it no longer exists. The module is the bookkeeping and the drawing's <see cref="Points"/>; it has no
/// gameplay effect and no data. The weapon fire path of the world does not create stream objects yet (<c>Weapon::ProjectileStreamName</c> is not typed), so a stream is
/// only fed by <see cref="AddProjectile"/>.
/// </summary>
public sealed class ProjectileStreamUpdate : SimModule, IUpdateModule
{
    /// <summary><c>MAX_PROJECTILE_STREAM</c>.</summary>
    public const int MaxProjectileStream = 20;

    private readonly EntityId[] _projectiles = new EntityId[MaxProjectileStream];
    private long _nextWake = -1;
    private int _firstValid;
    private int _nextFree;
    private EntityId _owner;
    private EntityId _targetObject;
    private bool _hasTargetPosition;
    private Fixed _targetX;
    private Fixed _targetY;
    private Fixed _targetZ;

    public override string ModuleName => "ProjectileStreamUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    /// <summary>The weapon's object that feeds this stream (<c>m_owningObject</c>).</summary>
    public EntityId Owner => _owner;

    /// <summary>Number of slots in use, holes included.</summary>
    public int Count => (_nextFree - _firstValid + MaxProjectileStream) % MaxProjectileStream;

    /// <summary><c>ProjectileStreamUpdate::addProjectile</c>: appends the projectile, inserting a hole first when the victim (or the aim position) differs from the previous shot's.</summary>
    public void AddProjectile(IWorldServices world, EntityId source, EntityId projectile, EntityId victim, bool hasPosition, Fixed x, Fixed y, Fixed z)
    {
        if (!_owner.IsValid) _owner = source;
        if (victim.IsValid)
        {
            if (victim != _targetObject) { Push(EntityId.Invalid); _targetObject = victim; }
            _hasTargetPosition = false;
            (_targetX, _targetY, _targetZ) = (Fixed.Zero, Fixed.Zero, Fixed.Zero);
        }
        else if (hasPosition)
        {
            if (!_hasTargetPosition || _targetX != x || _targetY != y || _targetZ != z) { Push(EntityId.Invalid); (_targetX, _targetY, _targetZ) = (x, y, z); }
            _hasTargetPosition = true;
            _targetObject = EntityId.Invalid;
        }
        Push(projectile);
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
        world.MarkDirty(Entity);
    }

    private void Push(EntityId id)
    {
        _projectiles[_nextFree] = id;
        _nextFree = (_nextFree + 1) % MaxProjectileStream;
        // The reference only asserts that the buffer never fills; when it would, the oldest slot is given up so the ring stays consistent.
        if (_nextFree == _firstValid) _firstValid = (_firstValid + 1) % MaxProjectileStream;
    }

    /// <summary><c>ProjectileStreamUpdate::getAllPoints</c>: the positions of the live projectiles from the oldest on, (0, 0, 0) for a hole or a vanished projectile.</summary>
    public IEnumerable<(Fixed X, Fixed Y, Fixed Z)> Points(IWorldServices world)
    {
        var owner = world.FindEntity(_owner);
        for (var index = _firstValid; index != _nextFree; index = (index + 1) % MaxProjectileStream)
        {
            var projectile = _projectiles[index].IsValid ? world.FindEntity(_projectiles[index]) : null;
            if (projectile is null) { yield return (Fixed.Zero, Fixed.Zero, Fixed.Zero); continue; }
            var z = projectile.Z;
            if (owner is not null && owner.Template.IsKindOf("VEHICLE"))
            {
                // The stream skims along the roof of a vehicle it is close to.
                var dx = owner.X - projectile.X;
                var dy = owner.Y - projectile.Y;
                var reach = owner.Template.GeometryMajorRadius * Fixed.FromDecimal(1.5m);
                if (SquaredReach.Of(reach).Contains(dx * dx + dy * dy)) z = Fixed.Max(z, owner.Z + owner.Template.GeometryHeight + Fixed.FromDecimal(0.5m));
            }
            yield return (projectile.X, projectile.Y, z);
        }
    }

    public long Update(IWorldServices world)
    {
        // cullFrontOfList: only gone projectiles at the front are dropped; a hole is a gone projectile too, so it goes as well once it reaches the front.
        while (_firstValid != _nextFree && (!_projectiles[_firstValid].IsValid || world.FindEntity(_projectiles[_firstValid]) is null))
            _firstValid = (_firstValid + 1) % MaxProjectileStream;
        if (_firstValid == _nextFree && _owner.IsValid && world.FindEntity(_owner) is null) world.RequestDestroy(Entity.Id);
        return SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        if (!_owner.IsValid && _firstValid == _nextFree) return;
        writer.Int("firstValid", _firstValid);
        writer.Int("nextFree", _nextFree);
        writer.UInt("owner", _owner.Value);
        writer.UInt("targetObject", _targetObject.Value);
        writer.Bool("hasTargetPosition", _hasTargetPosition);
        writer.Fixed("targetX", _targetX);
        writer.Fixed("targetY", _targetY);
        writer.Fixed("targetZ", _targetZ);
        writer.BeginArray("projectiles");
        foreach (var id in _projectiles)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        if (!reader.Has("owner")) return;
        _firstValid = checked((int)reader.Int("firstValid"));
        _nextFree = checked((int)reader.Int("nextFree"));
        _owner = new EntityId(checked((uint)reader.UInt("owner")));
        _targetObject = new EntityId(checked((uint)reader.UInt("targetObject")));
        _hasTargetPosition = reader.Bool("hasTargetPosition");
        _targetX = reader.Fixed("targetX");
        _targetY = reader.Fixed("targetY");
        _targetZ = reader.Fixed("targetZ");
        var list = reader.Array("projectiles");
        if (list.Count != MaxProjectileStream || (uint)_firstValid >= MaxProjectileStream || (uint)_nextFree >= MaxProjectileStream) throw new InvalidDataException("Projectile stream state out of range.");
        for (var index = 0; index < MaxProjectileStream; index++) _projectiles[index] = new EntityId(checked((uint)list[index].UInt("id")));
    }
}

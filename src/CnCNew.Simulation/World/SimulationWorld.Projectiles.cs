using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// Flight time of shots (<c>WeaponTemplate::fireWeaponTemplate</c>, <c>WeaponStore::setDelayedDamage</c>/<c>update</c>,
/// <c>WeaponStore::handleProjectileDetonation</c>). A weapon without a projectile object lands its damage <c>ceil(distance / WeaponSpeed)</c>
/// ticks after it fired (at once when that is below one tick); a weapon with one launches the object, whose projectile module flies it and
/// detonates it (<see cref="IProjectileModule"/>). See docs/GAME-RULES.md §6i for the fixed-point rules and the documented deviations.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>One pending <c>WeaponDelayedDamageInfo</c>, kept in insertion order (the reference's list order).</summary>
    private readonly record struct DelayedDamage(long DueTick, int Weapon, EntityId Source, EntityId Victim, Fixed X, Fixed Y, Fixed Z, uint Conditions);

    private readonly List<DelayedDamage> _delayedDamage = [];
    private readonly List<DelayedDamage> _dueDamage = [];

    /// <summary>Delayed shots still in flight (diagnostics and leak checks).</summary>
    public int PendingDelayedDamage => _delayedDamage.Count;

    public void EmitAt(SimEventKind kind, EntityId entity, EntityId other, string text, long value, Fixed x, Fixed y, Fixed z)
        => _events.Enqueue(new SimEvent(Ids.AllocateEvent(), _tick, kind, entity, other, PlayerId.System, text, value, true, x, y, z));

    /// <summary>
    /// <c>WeaponTemplate::fireWeaponTemplate</c> after the range checks: scatter, then either the delayed or immediate hit of a weapon without
    /// a projectile, or the launch of its projectile object. A victim replaces the given position with its own. Returns the damage the victim
    /// took right now (0 when the hit is delayed or a projectile flies). A projectile template that is not instantiable is never dropped
    /// silently: it raises <see cref="SimEventKind.CreationSkipped"/> (<see cref="CreationSkipReasons.ProjectileNotInstantiable"/>) and the shot
    /// lands like a projectile-less one after <c>distance / WeaponSpeed</c>.
    /// </summary>
    public Fixed FireWeaponAt(Entity source, WeaponInfo weapon, Entity? victim, Fixed x, Fixed y, Fixed z)
    {
        if (victim is not null) { x = victim.X; y = victim.Y; z = victim.Z; }
        var conditions = source.WeaponBonusConditions;
        // Weapon::privateFireWeapon starts with this: a networked weapon asks its neighbours to join in.
        if (weapon.RequestAssistRange > Fixed.Zero && victim is not null) ProcessRequestAssistance(source, weapon, victim);

        // Scatter (fireWeaponTemplate): a random point within the radius, on the ground. It only redirects a projectile; a delayed hit keeps
        // its victim (the reference leaves victimObj set, "victimObj = NULL" is commented out).
        var destinationX = x;
        var destinationY = y;
        var destinationZ = z;
        var scatter = Fixed.Zero;
        var infantry = victim is not null && victim.Template.IsKindOf("INFANTRY");
        if (weapon.ScatterRadius > Fixed.Zero || (weapon.ScatterRadiusVsInfantry > Fixed.Zero && infantry))
        {
            scatter = weapon.ScatterRadius;
            if (victim is not null && victim.Template.IsKindOf("STRUCTURE")) destinationZ = victim.Z + victim.Template.CenterZDelta;
            if (weapon.ScatterRadiusVsInfantry > Fixed.Zero && infantry) scatter += weapon.ScatterRadiusVsInfantry;
            scatter = LogicRandom.Range(Fixed.Zero, scatter);
            var angle = LogicRandom.Range(Fixed.Zero, FixedTrig.TwoPi);
            destinationX += scatter * FixedTrig.Cos(angle);
            destinationY += scatter * FixedTrig.Sin(angle);
            destinationZ = GroundHeightAt(destinationX, destinationY);
        }

        var projectileIndex = _templates.ProjectileTemplates.IsDefaultOrEmpty ? -1 : _templates.ProjectileTemplates[weapon.Index];
        if (projectileIndex >= 0 && _templates[projectileIndex].IsInstantiable)
        {
            var launchVictim = scatter > Fixed.Zero ? EntityId.Invalid : victim?.Id ?? EntityId.Invalid;
            var id = Ids.AllocateEntity();
            var launch = new ProjectileLaunch(weapon.Index, source.Id, launchVictim, destinationX, destinationY, destinationZ, conditions);
            // Weapon::positionProjectileForLaunch uses the launcher's weapon bone; without art the launch point is the launcher's geometry centre.
            _spawnQueue.Add(new PendingSpawn(id, projectileIndex, source.Owner, source.X, source.Y, source.Z + source.Template.CenterZDelta, source.Angle,
                default(SpawnSetup) with { Launch = launch }));
            EmitAt(SimEventKind.ProjectileLaunched, id, source.Id, weapon.Name, 0, destinationX, destinationY, destinationZ);
            return Fixed.Zero;
        }
        if (projectileIndex >= 0)
            Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, _templates[projectileIndex].Name, CreationSkipReasons.ProjectileNotInstantiable);

        var victimId = weapon.DamageDealtAtSelfPosition ? EntityId.Invalid : victim?.Id ?? EntityId.Invalid;
        var damageX = weapon.DamageDealtAtSelfPosition ? source.X : x;
        var damageY = weapon.DamageDealtAtSelfPosition ? source.Y : y;
        var damageZ = weapon.DamageDealtAtSelfPosition ? source.Z : z;
        var delay = FlightTicks(source, x, y, z, weapon.WeaponSpeed);
        if (delay == 0)
            return DealDamageCore(source.Id, source, EntityId.Invalid, source.Id, conditions, weapon, victimId.IsValid ? victim : null, damageX, damageY, damageZ);
        var due = _tick + delay;
        _delayedDamage.Add(new DelayedDamage(due, weapon.Index, source.Id, victimId, damageX, damageY, damageZ, conditions));
        EmitAt(SimEventKind.ProjectileLaunched, EntityId.Invalid, source.Id, weapon.Name, due, damageX, damageY, damageZ);
        return Fixed.Zero;
    }

    /// <summary>
    /// <c>delayInFrames = |victimPos - sourcePos| / WeaponSpeed</c>: 0 (hit now) below one tick, otherwise <c>REAL_TO_INT_CEIL</c> of it,
    /// computed exactly on the raw fixed-point values (the distance itself is the floored fixed-point square root). A speed of 0 or less —
    /// three shipped weapons without damage, where the reference divides by zero — hits at once.
    /// </summary>
    internal static long FlightTicks(Entity source, Fixed x, Fixed y, Fixed z, Fixed speed)
    {
        if (speed <= Fixed.Zero) return 0;
        var dx = x - source.X;
        var dy = y - source.Y;
        var dz = z - source.Z;
        var distance = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance < speed) return 0;
        return (distance.Raw + speed.Raw - 1) / speed.Raw;
    }

    /// <summary><c>WeaponStore::update</c>: every delayed hit due by now lands, in insertion order (the reference runs it at the end of the frame).</summary>
    private void ProcessDelayedDamage(long tick)
    {
        if (_delayedDamage.Count == 0) return;
        _dueDamage.Clear();
        var kept = 0;
        for (var index = 0; index < _delayedDamage.Count; index++)
        {
            var entry = _delayedDamage[index];
            if (entry.DueTick <= tick) _dueDamage.Add(entry);
            else _delayedDamage[kept++] = entry;
        }
        _delayedDamage.RemoveRange(kept, _delayedDamage.Count - kept);
        foreach (var entry in _dueDamage)
        {
            var weapon = _templates.Weapons[entry.Weapon];
            var source = FindEntity(entry.Source);
            var victim = entry.Victim.IsValid ? FindEntity(entry.Victim) : null;
            var dealt = DealDamageCore(entry.Source, source, EntityId.Invalid, entry.Source, entry.Conditions, weapon, victim, entry.X, entry.Y, entry.Z);
            var (x, y, z) = victim is null ? (entry.X, entry.Y, entry.Z) : (victim.X, victim.Y, victim.Z);
            EmitAt(SimEventKind.WeaponImpact, EntityId.Invalid, entry.Source, weapon.Name, dealt.Raw, x, y, z);
        }
    }

    /// <summary>
    /// <c>WeaponStore::handleProjectileDetonation</c> (<c>fireProjectileDetonationWeapon</c>): the weapon detonates at the projectile's position
    /// without a victim (radius damage only), with the launcher's weapon bonus conditions of the launch, credited to the launcher. The
    /// detonation effect and creation list (<c>ProjectileDetonationFX</c>/<c>OCL</c>) are requested at that position.
    /// </summary>
    public void DetonateProjectile(Entity projectile, WeaponInfo weapon, EntityId launcher, uint conditions, bool damage = true)
    {
        MarkDirty(projectile);
        if (damage) DealDamageCore(projectile.Id, projectile, launcher, launcher, conditions, weapon, null, projectile.X, projectile.Y, projectile.Z);
        if (weapon.DetonationFx.Length > 0)
            EmitAt(SimEventKind.FxRequested, projectile.Id, launcher, weapon.DetonationFx, 0, projectile.X, projectile.Y, projectile.Z);
        if (weapon.DetonationOcl.Length > 0) CreateFromList(projectile, weapon.DetonationOcl, launcher, false);
        EmitAt(SimEventKind.WeaponImpact, projectile.Id, launcher, weapon.Name, 0, projectile.X, projectile.Y, projectile.Z);
    }

    private void DescribeDelayedDamage(IStateWriter writer)
    {
        writer.BeginArray("delayedDamage");
        foreach (var entry in _delayedDamage)
        {
            writer.BeginObject(null);
            writer.Int("due", entry.DueTick);
            writer.Int("weapon", entry.Weapon);
            writer.UInt("source", entry.Source.Value);
            writer.UInt("victim", entry.Victim.Value);
            writer.Fixed("x", entry.X);
            writer.Fixed("y", entry.Y);
            writer.Fixed("z", entry.Z);
            writer.UInt("conditions", entry.Conditions);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadDelayedDamage(IStateReader state)
    {
        foreach (var item in state.Array("delayedDamage"))
        {
            var weapon = checked((int)item.Int("weapon"));
            if (weapon < 0 || weapon >= _templates.Weapons.Weapons.Length) throw new InvalidDataException("Delayed damage references an unknown weapon.");
            _delayedDamage.Add(new DelayedDamage(item.Int("due"), weapon, new EntityId(checked((uint)item.UInt("source"))), new EntityId(checked((uint)item.UInt("victim"))),
                item.Fixed("x"), item.Fixed("y"), item.Fixed("z"), checked((uint)item.UInt("conditions"))));
        }
    }
}

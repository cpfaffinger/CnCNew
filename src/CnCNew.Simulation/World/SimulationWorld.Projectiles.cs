using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// Flight time of shots (<c>WeaponTemplate::fireWeaponTemplate</c>, <c>WeaponStore::setDelayedDamage</c>/<c>update</c>,
/// <c>WeaponStore::handleProjectileDetonation</c>). A weapon without a projectile object lands its damage <c>ceil(distance / WeaponSpeed)</c>
/// logic frames of the original after it fired (at once when that is below one frame; <see cref="FlightTicks"/>); a weapon with one launches the object, whose projectile module flies it and
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
        var (sourceX, sourceY, sourceZ) = (source.X, source.Y, source.Z);
        var sourceAngle = source.Angle;
        if (_firstFrameTicks != TickOrder.Next)
        {
            sourceAngle = source.FrameEndAngle(_tick);
            // Above 30 Hz the shot goes from where the shooter's begun logic frame of the original ends to where the victim's ends (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3,
            // rule 3): the positions both have when the 30 Hz step fires, after their whole step of that frame.
            if (victim is not null) (x, y, z) = victim.FrameEnd(_tick);
            (sourceX, sourceY, sourceZ) = source.FrameEnd(_tick);
        }
        // A contained rider fires from its fire point or station in a garrison, from its transport's fire point in any other (ShooterPosition, F-60).
        var contained = source.ContainedBy.IsValid;
        if (contained) (sourceX, sourceY, sourceZ) = FiringPointOf(source);
        var conditions = WeaponBonusOf(source);
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
            _spawnQueue.Add(new PendingSpawn(id, projectileIndex, source.Owner, sourceX, sourceY, sourceZ + source.Template.CenterZDelta, sourceAngle,
                default(SpawnSetup) with { Launch = launch }));
            EmitAt(SimEventKind.ProjectileLaunched, id, source.Id, weapon.Name, 0, destinationX, destinationY, destinationZ);
            return Fixed.Zero;
        }
        if (projectileIndex >= 0)
            Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, _templates[projectileIndex].Name, CreationSkipReasons.ProjectileNotInstantiable);

        var victimId = weapon.DamageDealtAtSelfPosition ? EntityId.Invalid : victim?.Id ?? EntityId.Invalid;
        var damageX = weapon.DamageDealtAtSelfPosition ? sourceX : x;
        var damageY = weapon.DamageDealtAtSelfPosition ? sourceY : y;
        var damageZ = weapon.DamageDealtAtSelfPosition ? sourceZ : z;
        var delay = !contained ? FlightTicks(source.X, source.Y, source.Z, x, y, z, weapon.WeaponSpeed) : FlightTicks(sourceX, sourceY, sourceZ, x, y, z, weapon.WeaponSpeed);
        if (delay == 0)
            return DealDamageCore(source.Id, source, EntityId.Invalid, source.Id, conditions, weapon, victimId.IsValid ? victim : null, damageX, damageY, damageZ);
        var due = _tick + delay;
        _delayedDamage.Add(new DelayedDamage(due, weapon.Index, source.Id, victimId, damageX, damageY, damageZ, conditions));
        EmitAt(SimEventKind.ProjectileLaunched, EntityId.Invalid, source.Id, weapon.Name, due, damageX, damageY, damageZ);
        return Fixed.Zero;
    }


    public void FireTempWeapon(Entity source, string weaponName, Fixed x, Fixed y, Fixed z)
    {
        if (!_templates.Weapons.TryGet(weaponName, out var weapon))
        {
            Emit(SimEventKind.CreationSkipped, source.Id, EntityId.Invalid, weaponName, CreationSkipReasons.UnknownTemplate);
            return;
        }
        FireWeaponAt(source, weapon, null, x, y, z);
    }

    /// <summary>
    /// <c>Object::fireCurrentWeapon(const Coord3D*)</c> / <c>fireCurrentWeapon(victim)</c>: fires the object's current weapon (the locked slot, else the primary one) at a position, or at
    /// <paramref name="victim"/> when one is given, if it is ready (clip and delay are kept exactly as <see cref="FireWeaponCommand"/> keeps them); false when the slot holds no weapon or is
    /// still reloading. No range check, as the reference has none for these.
    /// </summary>
    public bool FireCurrentWeaponAtPosition(Entity source, Fixed x, Fixed y, Fixed z, Entity? victim = null)
    {
        UpdateWeaponSet(source);
        if (ActiveWeaponSet(source) is not { } set) return false;
        // WeaponSet::chooseBestWeaponForTarget without a victim: "only Primary attack the ground" unless a weapon lock pins another slot.
        var slot = source.WeaponLock != WeaponLockKind.None ? source.CurrentWeaponSlot : WeaponSlot.Primary;
        if (!set.HasWeapon(slot) || _tick < source.ReadyTickOf(slot)) return false;
        var weapon = ConsumeShot(source, slot, victim?.Id ?? EntityId.Invalid);
        var damage = FireWeaponAt(source, weapon, victim, x, y, z);
        Emit(SimEventKind.WeaponFired, source.Id, victim?.Id ?? EntityId.Invalid, weapon.Name, damage.Raw, source.Owner);
        return true;
    }

    /// <summary>
    /// <c>delayInFrames = |victimPos - sourcePos| / WeaponSpeed</c>: 0 (hit now) below one logic frame, otherwise <c>REAL_TO_INT_CEIL</c> of it,
    /// computed exactly on the raw fixed-point values (the distance itself is the floored fixed-point square root). A speed of 0 or less —
    /// three shipped weapons without damage, where the reference divides by zero — hits at once.
    /// <para>
    /// The reference counts in its logic frames, so the count is taken in logic frames of the original — against the way of one frame — and only then scaled to ticks: "at once"
    /// stays "closer than one frame of flight" and the delay stays on the original's grid at every tick rate. The way of one frame is the speed per frame of the 30 Hz catalog
    /// (<see cref="CnCNew.Simulation.Time.ProjectileFrameGrid.PerTickToPerOriginalFrame"/>), not the speed per tick times the ticks per frame: that product is rounded differently in its last bits and
    /// moved the count by a whole frame where the distance is a multiple of the speed.
    /// </para>
    /// </summary>
    internal long FlightTicks(Fixed fromX, Fixed fromY, Fixed fromZ, Fixed x, Fixed y, Fixed z, Fixed speed)
    {
        if (speed <= Fixed.Zero) return 0;
        var dx = x - fromX;
        var dy = y - fromY;
        var dz = z - fromZ;
        var distance = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
        var perFrame = Modules.BuiltIn.ProjectileModules.FrameSpeed(Time, speed).Raw;
        if (distance.Raw < perFrame) return 0;
        return Time.OriginalFrames((distance.Raw + perFrame - 1) / perFrame);
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

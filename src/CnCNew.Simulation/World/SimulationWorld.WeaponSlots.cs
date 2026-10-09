using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// The outcome of <see cref="SimulationWorld.ChooseWeapon"/>: the slot to fire when <see cref="Rejection"/> is 0, otherwise why no shot is possible (the
/// slot then names the weapon that was chosen but cannot fire yet, or <see cref="WeaponSlot.Best"/> when none qualified).
/// </summary>
public readonly record struct WeaponChoice(int Slot, CommandRejection Rejection)
{
    public bool CanFire => Rejection == 0;
}

/// <summary>
/// Weapon slots: the per-slot clips, reload timers and locks of an object (<c>WeaponSet</c>, <c>Weapon</c> in the reference) and the weapon choice of an attack
/// (<c>WeaponSet::chooseBestWeaponForTarget</c>). See docs/SIMULATION-CORE.md for the model and its approximations.
/// </summary>
public sealed partial class SimulationWorld
{
    private static readonly int PlayerUpgradeSetBit = NameTablesPhase5Index("PLAYER_UPGRADE");
    private static readonly int DeployDamageType = DamageIndex("DEPLOY");
    private static readonly int DisarmDamageType = DamageIndex("DISARM");

    private static int NameTablesPhase5Index(string name)
        => CnCNew.Data.Definitions.NameTablesPhase5.WeaponSetCondition.TryIndexOf(name, out var index) ? index : throw new InvalidOperationException($"{name} is not a weapon set condition.");

    private static int DamageIndex(string name)
        => CnCNew.Data.Definitions.NameTables.DamageType.TryIndexOf(name, out var index) ? index : throw new InvalidOperationException($"{name} is not a damage type.");

    // ------------------------------------------------------------------------------------------------------ the active set

    private static readonly int CarBombSetBit = NameTablesPhase5Index("CARBOMB");
    private static readonly int CrateUpgradeOneSetBit = NameTablesPhase5Index("CRATEUPGRADE_ONE");

    /// <summary>
    /// <c>Object::getWeaponSetFlags</c> as far as this simulation raises flags: <c>VETERAN</c>/<c>ELITE</c>/<c>HERO</c> follow the veterancy level (exclusive,
    /// <c>Object::onVeterancyLevelChanged</c>), <c>PLAYER_UPGRADE</c> follows <c>WeaponSetUpgrade</c>, <c>CARBOMB</c> follows <c>IS_CARBOMB</c>
    /// (<c>ConvertToCarBombCrateCollide</c> sets the weapon-set flag together with the status and nothing ever clears either) and <c>CRATEUPGRADE_ONE</c>/<c>_TWO</c> follow
    /// <see cref="Entity.CrateWeaponLevel"/> (<c>SalvageCrateCollide</c>; one flag at a time, as the reference swaps them). The remaining conditions a <c>WeaponSet</c> can name
    /// (<c>VEHICLE_HIJACK</c>, <c>MINE_CLEARING_DETAIL</c>, <c>WEAPON_RIDER*</c>) are never raised.
    /// </summary>
    public static uint WeaponSetFlagsOf(Entity entity)
    {
        var flags = 0u;
        if (entity.VeterancyLevel > 0) flags |= 1u << (entity.VeterancyLevel - 1);
        if (entity.HasPlayerUpgradeWeapon) flags |= 1u << PlayerUpgradeSetBit;
        if (entity.CrateWeaponLevel > 0) flags |= 1u << (CrateUpgradeOneSetBit + entity.CrateWeaponLevel - 1);
        if (entity.HasStatus(CarBombStatusBit)) flags |= 1u << CarBombSetBit;
        return flags;
    }

    /// <summary><c>ThingTemplate::findWeaponTemplateSet(object flags)</c> (<c>SparseMatchFinder</c>); null when the object declares no weapon set.</summary>
    public static WeaponSetInfo? ActiveWeaponSet(Entity entity)
    {
        var flags = WeaponSetFlagsOf(entity);
        return flags == 0 ? entity.Template.DefaultWeaponSet : WeaponSetResolver.Find(entity.Template.WeaponSets, flags);
    }

    /// <summary>The weapon index (into the <see cref="WeaponCatalog"/>) of the active set's primary slot, -1 when unarmed in that slot.</summary>
    public static int ActiveWeaponIndex(Entity entity) => ActiveWeaponSet(entity)?.Primary ?? -1;

    /// <summary>The weapon index in one slot of the active set, -1 when the slot is empty.</summary>
    public static int WeaponIndexIn(Entity entity, int slot) => ActiveWeaponSet(entity) is { } set && set.HasWeapon(slot) ? set.Weapons[slot] : -1;

    /// <summary><c>Object::hasAnyDamageWeapon</c>: some slot of the active set deals damage.</summary>
    public static bool HasAnyDamageWeapon(Entity entity, WeaponCatalog weapons)
    {
        if (ActiveWeaponSet(entity) is not { } set) return false;
        foreach (var index in set.Weapons)
            if (index >= 0 && (weapons[index].PrimaryDamage > Fixed.Zero || weapons[index].SecondaryDamage > Fixed.Zero)) return true;
        return false;
    }

    /// <summary>The longest attack range over the active set's weapons (with the entity's weapon bonuses); 0 when unarmed. What a unit has to approach to, to use any of its weapons.</summary>
    public Fixed MaxWeaponRange(Entity entity)
    {
        var range = Fixed.Zero;
        if (ActiveWeaponSet(entity) is not { } set) return range;
        foreach (var index in set.Weapons)
            if (index >= 0) range = Fixed.Max(range, _templates.Weapons[index].RangeWith(_templates.GlobalWeaponBonus, entity.WeaponBonusConditions));
        return range;
    }

    /// <summary>Some weapon of the active set could fire this tick (a cheap precheck before looking for a target; <see cref="ChooseWeapon"/> decides for a specific one).</summary>
    public bool IsAnyWeaponReady(Entity entity)
    {
        if (ActiveWeaponSet(entity) is not { } set) return false;
        for (var slot = 0; slot < WeaponSlot.Count; slot++)
            if (set.HasWeapon(slot) && _tick >= EffectiveReadyTick(entity, slot, set)) return true;
        return false;
    }

    /// <summary>The ready tick of a slot; slots built for another weapon set count as freshly loaded (the set change has not been applied to the entity yet).</summary>
    private static long EffectiveReadyTick(Entity entity, int slot, WeaponSetInfo set)
        => entity.WeaponSlots is { } state && state.SetIndex != set.Index ? 0 : entity.ReadyTickOf(slot);

    // ------------------------------------------------------------------------------------------------------ weapon choice

    /// <summary>
    /// <c>getVictimAntiMask</c>: the <c>WeaponAntiMaskType</c> a weapon needs to hit <paramref name="victim"/> (missiles, projectiles, mines, airborne vehicles/infantry/
    /// parachutes, else the ground). "Airborne" is <see cref="IsSignificantlyAboveTerrain"/>, standing in for <c>OBJECT_STATUS_AIRBORNE_TARGET</c>.
    /// </summary>
    private int VictimAntiMask(Entity victim)
    {
        var template = victim.Template;
        return template.AirborneTargetAntiMask == template.GroundTargetAntiMask || !IsSignificantlyAboveTerrainAtFrameEnd(victim) ? template.GroundTargetAntiMask : template.AirborneTargetAntiMask;
    }

    /// <summary>
    /// <c>WeaponTemplate::estimateWeaponTemplateDamage</c> (through <c>Object::estimateDamage</c>): the primary damage after the weapon's bonuses and the victim's armor,
    /// with the reference's nonzero stand-ins (flame against shrubbery, deploy against anything grounded, disarm against mines). Not modeled: the garrison cases
    /// (<c>DAMAGE_SURRENDER</c>, <c>AllowAttackGarrisonedBldgs</c>, sniper at an empty structure).
    /// </summary>
    private Fixed EstimateDamage(Entity source, WeaponInfo weapon, Entity victim)
    {
        var template = victim.Template;
        if (template.IsKindOf("SHRUBBERY")) return weapon.DeathType == DeathTypes.Burned ? Fixed.One : Fixed.Zero;
        if (weapon.DamageType == DisarmDamageType) return template.IsKindOf("MINE") || template.IsKindOf("BOOBY_TRAP") || template.IsKindOf("DEMOTRAP") ? Fixed.One : Fixed.Zero;
        if (weapon.DamageType == DeployDamageType && !IsSignificantlyAboveTerrainAtFrameEnd(victim)) return Fixed.One;
        var amount = weapon.DamageWith(_templates.GlobalWeaponBonus, source.WeaponBonusConditions, weapon.PrimaryDamage);
        return ActiveArmor(victim).Adjust(weapon.DamageType, amount);
    }

    /// <summary>The weapon may be aimed at this victim at all: it targets its kind and would do damage (or is unresistable, which "is allowed to do zero damage").</summary>
    private bool CanTarget(Entity source, WeaponInfo weapon, Entity victim, int victimAntiMask)
        => (weapon.AntiMask & victimAntiMask) != 0 && (EstimateDamage(source, weapon, victim) > Fixed.Zero || weapon.DamageType == DamageTypes.Unresistable);

    /// <summary><c>Weapon::isWithinAttackRange</c> with the center distance this world uses everywhere: within the attack range and not inside the minimum attack range.</summary>
    private bool WithinWeaponRange(Entity source, WeaponInfo weapon, Entity victim)
    {
        var dx = victim.X - source.X;
        var dy = victim.Y - source.Y;
        if (_firstFrameTicks != TickOrder.Next)
        {
            // Above 30 Hz both objects are read where the logic frame of the original they have begun ends (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3, rule 3): the
            // answer is the one the 30 Hz step gives after the whole step of both, in every tick of that frame, instead of flipping somewhere inside it.
            var (victimX, victimY, _) = victim.FrameEnd(_tick);
            var (sourceX, sourceY, _) = source.FrameEnd(_tick);
            dx = victimX - sourceX;
            dy = victimY - sourceY;
        }
        var distanceSquared = dx * dx + dy * dy;
        var reach = weapon.RangeWith(_templates.GlobalWeaponBonus, source.WeaponBonusConditions);
        if (!SquaredReach.Of(reach).Contains(distanceSquared)) return false;
        var minimum = weapon.MinimumAttackRange;
        // Weapon.cpp: "distSqr < minAttackRangeSqr - 0.5f" (the shipped, non-RATIONALIZE_ATTACK_RANGE build).
        return minimum <= Fixed.Zero || !SquaredReach.ContainsStrictly(minimum, distanceSquared, Fixed.FromDecimal(0.5m));
    }

    private static bool IsPreferredAgainst(WeaponSetInfo set, int slot, Entity victim)
    {
        foreach (var kind in set.PreferredAgainst[slot])
            if (victim.Template.IsKindOf(kind)) return true;
        return false;
    }

    /// <summary>
    /// Which weapon slot fires at <paramref name="victim"/> now, a reduced <c>WeaponSet::chooseBestWeaponForTarget</c> (criteria <c>PREFER_MOST_DAMAGE</c>) that also
    /// answers "can it fire this tick" because this world has no approach step: a pure function of the entity's state, so command validation and execution agree.
    /// <para>
    /// A pinned slot (<see cref="Entity.WeaponLock"/>) overrides the choice, and an explicit <paramref name="slot"/> (a command button's <c>WeaponSlot</c>) names the
    /// weapon directly; a permanent lock still beats an explicit slot (<c>setWeaponLock(LOCKED_TEMPORARILY)</c> never overrides <c>LOCKED_PERMANENTLY</c>). Otherwise, from
    /// the tertiary slot down (so a tie goes to the lower slot): a slot qualifies when <paramref name="origin"/> may choose it (<c>AutoChooseSources</c>), its
    /// <c>AntiMask</c> fits the victim and it would damage the victim's armor; of the qualifying slots those within range compete, ready ones first, by damage —
    /// except that a weapon <c>PreferredAgainst</c> the victim's kind always wins and counts as ready while it merely reloads (the shot then waits for the weapon
    /// rather than falling back to another). Several slots never fire in one command: like the reference, one shot per object and attack update; the others take
    /// over on later ticks while the first reloads.
    /// </para>
    /// <para>
    /// Differences from the reference: the range is a filter (no weapon is chosen to be walked into range), <c>PREFER_LONGEST_RANGE</c> and the turret/pitch
    /// conditions are not modeled, and <c>AutoChooseSources</c> is evaluated as the shipped code does — see the comment at <c>sources</c> below.
    /// </para>
    /// </summary>
    public WeaponChoice ChooseWeapon(Entity source, Entity victim, FireOrigin origin, int slot = WeaponSlot.Best)
    {
        var set = ActiveWeaponSet(source);
        if (set is null || set.WeaponCount == 0) return new(WeaponSlot.Best, CommandRejection.NoWeapon);
        var victimMask = VictimAntiMask(victim);
        var state = source.WeaponSlots;
        var stale = state is not null && state.SetIndex != set.Index;

        var effective = slot;
        if (state is not null && state.Lock != WeaponLockKind.None && (!stale || set.LockSharedAcrossSets))
        {
            if (state.Lock == WeaponLockKind.Permanent || slot == WeaponSlot.Best) effective = state.Current;
        }

        if (effective >= 0)
        {
            if (!set.HasWeapon(effective)) return new(effective, CommandRejection.NoWeapon);
            var weapon = _templates.Weapons[set.Weapons[effective]];
            if (!CanTarget(source, weapon, victim, victimMask)) return new(effective, CommandRejection.InvalidWeaponTarget);
            if (_tick < EffectiveReadyTick(source, effective, set)) return new(effective, CommandRejection.WeaponNotReady);
            return WithinWeaponRange(source, weapon, victim) ? new(effective, 0) : new(effective, CommandRejection.OutOfRange);
        }

        var found = -1;
        var foundDamage = Fixed.Zero;
        var backup = -1;
        var backupDamage = Fixed.Zero;
        var anyEligible = false;
        var allEligibleReloading = true;
        var filteredByTarget = false;
        for (var candidate = WeaponSlot.Count - 1; candidate >= 0; candidate--)
        {
            if (!set.HasWeapon(candidate)) continue;
            // WeaponSet.cpp chooseBestWeaponForTarget: "if ((okSrcs & (1 << cmdSource)) == 0) { if (!(okSrcs & CMD_DEFAULT_SWITCH_WEAPON)) continue; }". The second test uses the
            // enum value CMD_DEFAULT_SWITCH_WEAPON (4), not its mask bit, so it actually reads the FROM_AI bit: a weapon any source may not choose is still chosen by every source
            // when FROM_AI is set. Reproduced as shipped, because the retail balance (which secondary weapons fire for a player-ordered attack) depends on it.
            var sources = set.AutoChoose[candidate];
            if ((sources & (1u << (int)origin)) == 0 && (sources & CommandSourceBits.FromAi) == 0) continue;
            var weapon = _templates.Weapons[set.Weapons[candidate]];
            if (!CanTarget(source, weapon, victim, victimMask)) { filteredByTarget = true; continue; }
            anyEligible = true;
            var ready = _tick >= EffectiveReadyTick(source, candidate, set);
            if (ready) allEligibleReloading = false;
            if (!WithinWeaponRange(source, weapon, victim)) continue;
            var damage = EstimateDamage(source, weapon, victim);
            if (IsPreferredAgainst(set, candidate, victim))
            {
                damage = Fixed.MaxValue;
                ready = true;
            }
            if (!ready)
            {
                if (damage >= backupDamage) { backupDamage = damage; backup = candidate; }
            }
            else if (damage >= foundDamage)
            {
                foundDamage = damage;
                found = candidate;
            }
        }
        if (found >= 0)
            return _tick >= EffectiveReadyTick(source, found, set) ? new(found, 0) : new(found, CommandRejection.WeaponNotReady);
        if (backup >= 0) return new(backup, CommandRejection.WeaponNotReady);
        if (anyEligible) return new(WeaponSlot.Best, allEligibleReloading ? CommandRejection.WeaponNotReady : CommandRejection.OutOfRange);
        return new(WeaponSlot.Best, filteredByTarget ? CommandRejection.InvalidWeaponTarget : CommandRejection.NoWeapon);
    }

    // ------------------------------------------------------------------------------------------------------ firing

    private static void StoreSlot(Entity entity, int slot, int clip, long ready)
    {
        if (slot == 0)
        {
            entity.WeaponClip = clip;
            entity.WeaponReadyTick = ready;
            return;
        }
        entity.WeaponSlots!.Clip[slot] = clip;
        entity.WeaponSlots.Ready[slot] = ready;
    }

    private static void StoreReady(Entity entity, int slot, long ready)
    {
        if (slot == 0) entity.WeaponReadyTick = ready;
        else entity.WeaponSlots!.Ready[slot] = ready;
    }

    /// <summary>
    /// <c>WeaponSet::updateWeaponSet</c>'s weapon creation: a full clip (<c>loadAmmoNow</c>) and a weapon ready at <paramref name="readyAt"/> for each slot of
    /// <paramref name="set"/>; slots without a weapon hold -1. The slots' last-fire ticks reset, as the reference allocates new <c>Weapon</c> objects.
    /// </summary>
    private void FillSlots(Entity entity, WeaponSetInfo set, long readyAt)
    {
        for (var slot = 0; slot < WeaponSlot.Count; slot++)
        {
            var index = set.Weapons[slot];
            if (index < 0) StoreSlot(entity, slot, -1, 0);
            else StoreSlot(entity, slot, ClipCapacity(_templates.Weapons[index]), readyAt);
            if (entity.WeaponSlots is { } state) state.LastFire[slot] = -1;
        }
    }

    /// <summary>
    /// Applies a change of the weapon set flags to an object with several weapons (<c>WeaponSet::updateWeaponSet</c>): when the flags now select another set, its weapons are
    /// built fresh (full clips, ready now) and the lock is released unless the new set shares it. A no-op for a single-weapon object, which keeps its clip.
    /// </summary>
    internal void UpdateWeaponSet(Entity entity)
    {
        if (entity.WeaponSlots is not { } state || ActiveWeaponSet(entity) is not { } set || state.SetIndex == set.Index) return;
        RebuildWeaponSlots(entity, state, set);
    }

    private void RebuildWeaponSlots(Entity entity, WeaponSlotState state, WeaponSetInfo set)
    {
        if (state.SetIndex != set.Index && !set.LockSharedAcrossSets)
        {
            state.Lock = WeaponLockKind.None;
            state.Current = WeaponSlot.Primary;
        }
        state.SetIndex = set.Index;
        FillSlots(entity, set, _tick);
        MarkDirty(entity);
    }

    /// <summary>
    /// The weapon set flags changed (<c>WeaponSetUpgrade</c> raised or cleared <c>PLAYER_UPGRADE</c>, a salvage crate raised <c>CRATEUPGRADE_*</c>, a terrorist made a vehicle a
    /// <c>CARBOMB</c>): every weapon of the (possibly new) set is loaded and ready now. A single-weapon object refills its one clip.
    /// </summary>
    public void WeaponSetFlagsChanged(Entity entity)
    {
        if (ActiveWeaponSet(entity) is not { } set) return;
        if (entity.WeaponSlots is { } state)
        {
            RebuildWeaponSlots(entity, state, set);
            return;
        }
        if (set.Primary < 0) return;
        entity.WeaponClip = ClipCapacity(_templates.Weapons[set.Primary]);
        entity.WeaponReadyTick = _tick;
        MarkDirty(entity);
    }

    /// <summary>
    /// <c>Weapon::privateFireWeapon</c>'s bookkeeping for one shot from <paramref name="slot"/> (the caller has confirmed the slot may fire): the clip loses a round
    /// (an empty one has finished reloading and is full again first), the next shot waits <c>DelayBetweenShots</c> or, once the clip is empty, <c>ClipReloadTime</c>
    /// (<c>ShareWeaponReloadTime</c> sets every slot's wait to that one), the slot becomes the current weapon, and an emptied clip releases a temporary lock
    /// (<c>Object::fireCurrentWeapon</c>: "if (reloaded) releaseWeaponLock(LOCKED_TEMPORARILY)"). Returns the weapon that fires.
    /// </summary>
    private WeaponInfo ConsumeShot(Entity source, int slot, EntityId victim)
    {
        var set = ActiveWeaponSet(source)!;
        var weapon = _templates.Weapons[set.Weapons[slot]];
        var bonuses = _templates.GlobalWeaponBonus;
        var conditions = source.WeaponBonusConditions;
        var clip = source.ClipOf(slot);
        if (clip == 0) clip = ClipCapacity(weapon);
        source.LastFireTick = _tick;
        if (clip != int.MaxValue) clip--;
        var emptied = clip == 0;
        // A RETURN_TO_BASE weapon that ran dry waits for the airfield reload (JetAIUpdate refills the clip and sets the ready tick then).
        // A ClipReloadTime of 0 reloads at once; the next shot then comes with the next logic frame of the original, not with the next tick.
        var ready = emptied
            ? weapon.ReloadsAtBase ? SimulationTiming.SleepForever : _tick + Time.ZeroIsNextFrame(weapon.ClipReloadWith(Time, bonuses, conditions))
            : _tick + weapon.DelayBetweenShotsWith(Time, bonuses, conditions);
        StoreSlot(source, slot, clip, ready);
        if (source.WeaponSlots is { } state)
        {
            state.LastFire[slot] = _tick;
            state.Current = slot;
            if (emptied && state.Lock == WeaponLockKind.Temporary) state.Lock = WeaponLockKind.None;
            if (set.ShareReloadTime)
                for (var other = 0; other < WeaponSlot.Count; other++)
                    if (set.HasWeapon(other)) StoreReady(source, other, ready);
        }
        NoteShotFired(source, weapon, slot, victim);
        MarkDirty(source);
        return weapon;
    }

    // ------------------------------------------------------------------------------------------------------ module services

    /// <summary>
    /// A weapon cannot become ready before <paramref name="until"/> (an EMP, a paralysis): every slot's ready tick is raised to it. With <paramref name="onlyArmed"/> an unarmed
    /// primary slot is left alone (<c>DISABLED_EMP</c> only matters to an object with a weapon).
    /// </summary>
    public static void DelayWeaponsUntil(Entity entity, long until, bool onlyArmed)
    {
        if ((!onlyArmed || entity.WeaponClip != -1) && entity.WeaponReadyTick < until) entity.WeaponReadyTick = until;
        if (entity.WeaponSlots is not { } state) return;
        for (var slot = 1; slot < WeaponSlot.Count; slot++)
            if (state.Clip[slot] != -1 && state.Ready[slot] < until) state.Ready[slot] = until;
    }

    /// <summary>
    /// <c>Object::reloadAllAmmo(true)</c> (an aircraft that has landed at its airfield): every clipped weapon of the active set is full again and ready now (a ready tick
    /// already in the past stays). Weapons with an unlimited clip are untouched.
    /// </summary>
    public void ReloadAllAmmo(Entity entity)
    {
        UpdateWeaponSet(entity);
        if (ActiveWeaponSet(entity) is not { } set) return;
        for (var slot = 0; slot < WeaponSlot.Count; slot++)
        {
            if (!set.HasWeapon(slot)) continue;
            var weapon = _templates.Weapons[set.Weapons[slot]];
            if (entity.ClipOf(slot) == int.MaxValue || weapon.ClipSize <= 0) continue;
            StoreSlot(entity, slot, weapon.ClipSize, Math.Min(entity.ReadyTickOf(slot), _tick));
        }
        MarkDirty(entity);
    }

    /// <summary>
    /// One shot of <paramref name="slot"/> at <paramref name="victim"/> on behalf of the object itself (a module's attack order with the slot locked, e.g.
    /// <c>AssistedTargetingUpdate</c>): the slot's weapon must exist in the active set, be loaded and ready, be able to hit the victim and have it in range. The clip,
    /// reload and last-shot bookkeeping are the slot's own. False when no shot goes out.
    /// </summary>
    public bool TryFireWeaponSlot(Entity source, int slot, Entity victim)
    {
        if (source.DestroyRequested || source.EffectivelyDead || victim.DestroyRequested || victim.Body is null) return false;
        UpdateWeaponSet(source);
        if (ActiveWeaponSet(source) is not { } set || !set.HasWeapon(slot)) return false;
        var weapon = _templates.Weapons[set.Weapons[slot]];
        if (_tick < EffectiveReadyTick(source, slot, set) || !CanTarget(source, weapon, victim, VictimAntiMask(victim)) || !WithinWeaponRange(source, weapon, victim)) return false;
        // The module runs its own attack sequence (it gives up when a shot fails), so the turret is told to turn but never holds the shot back.
        AimTurretsAt(source, slot, victim);
        var fired = ConsumeShot(source, slot, victim.Id);
        var damage = FireWeaponAt(source, fired, victim, victim.X, victim.Y, victim.Z);
        Emit(SimEventKind.WeaponFired, source.Id, victim.Id, fired.Name, damage.Raw, source.Owner);
        NoteTurretShot(source, slot);
        return true;
    }

    /// <summary>
    /// The attack half of a <see cref="FireWeaponCommand"/> for an object's own weapon choice, issued by a module instead of the player (<c>CMD_FROM_AI</c>): the best weapon of the
    /// active set against the victim fires when it is ready and the victim is in range. No visibility check (the module acts for the object itself, not for a player).
    /// </summary>
    public bool TryFireActiveWeapon(Entity source, Entity victim)
    {
        if (source.DestroyRequested || source.EffectivelyDead || victim.DestroyRequested || victim.Body is null) return false;
        UpdateWeaponSet(source);
        var choice = ChooseWeapon(source, victim, FireOrigin.Ai);
        if (!choice.CanFire || !GateAllowsFire(source, victim)) return false;
        if (!AimTurretsAt(source, choice.Slot, victim)) { NoteTurretWait(); return false; }
        var weapon = ConsumeShot(source, choice.Slot, victim.Id);
        var damage = FireWeaponAt(source, weapon, victim, victim.X, victim.Y, victim.Z);
        Emit(SimEventKind.WeaponFired, source.Id, victim.Id, weapon.Name, damage.Raw, source.Owner);
        NoteTurretShot(source, choice.Slot);
        return true;
    }

    // ------------------------------------------------------------------------------------------------------ locks

    /// <summary>
    /// <c>WeaponSet::setWeaponLock</c>: makes <paramref name="slot"/> the current weapon and pins it. A temporary lock never replaces a permanent one. False when the active
    /// set has no weapon there. An object whose weapon sets hold a single weapon has nothing to choose between, so the lock is accepted without being stored.
    /// </summary>
    public bool SetWeaponLock(Entity entity, int slot, WeaponLockKind kind)
    {
        if (kind == WeaponLockKind.None) throw new ArgumentOutOfRangeException(nameof(kind), "Use ReleaseWeaponLock to release a lock.");
        if (ActiveWeaponSet(entity) is not { } set || !set.HasWeapon(slot)) return false;
        if (entity.WeaponSlots is not { } state) return true;
        UpdateWeaponSet(entity);
        if (kind == WeaponLockKind.Permanent || state.Lock != WeaponLockKind.Permanent)
        {
            state.Current = slot;
            state.Lock = kind;
            if (entity.Registered) MarkDirty(entity); // a lock taken while the object spawns is part of its first hash anyway
        }
        return true;
    }

    /// <summary><c>WeaponSet::releaseWeaponLock</c>: a permanent release clears every lock, a temporary one only a temporary lock.</summary>
    public void ReleaseWeaponLock(Entity entity, WeaponLockKind kind)
    {
        if (entity.WeaponSlots is not { } state || state.Lock == WeaponLockKind.None) return;
        if (kind == WeaponLockKind.None) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == WeaponLockKind.Temporary && state.Lock != WeaponLockKind.Temporary) return;
        state.Lock = WeaponLockKind.None;
        MarkDirty(entity);
    }
}

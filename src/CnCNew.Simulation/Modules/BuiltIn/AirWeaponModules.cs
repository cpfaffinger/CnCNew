using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The weapon and pulse modules of the air group: <c>EMPUpdate</c>/<c>LeafletDropBehavior</c> (disabling pulses), <c>BunkerBusterBehavior</c>, <c>NeutronBlastBehavior</c>,
/// <c>NeutronMissileUpdate</c>/<c>NeutronMissileSlowDeathBehavior</c> (the neutron missile and its blast), <c>MissileLauncherBuildingUpdate</c> (the silo doors) and <c>SpectreGunshipUpdate</c>
/// (the gunship's orbit and weapons). Each cites its reference file at the class and names what is approximated.
/// </summary>
internal static class AirWeaponModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<EmpData>("EMPUpdate", ModuleOwnerKind.Object, (configuration, _) => EmpData.Parse(configuration), data => new EmpUpdate(data)),
        new ModuleBinding<LeafletData>("LeafletDropBehavior", ModuleOwnerKind.Object, (configuration, _) => LeafletData.Parse(configuration), data => new LeafletDropBehavior(data)),
        new ModuleBinding<BunkerBusterData>("BunkerBusterBehavior", ModuleOwnerKind.Object, BunkerBusterData.Parse, data => new BunkerBusterBehavior(data)),
        new ModuleBinding<NeutronBlastData>("NeutronBlastBehavior", ModuleOwnerKind.Object, (configuration, _) => NeutronBlastData.Parse(configuration), data => new NeutronBlastBehavior(data)),
        new ModuleBinding<NeutronMissileData>("NeutronMissileUpdate", ModuleOwnerKind.Object, (configuration, _) => NeutronMissileData.Parse(configuration), data => new NeutronMissileUpdate(data)),
        new ModuleBinding<NeutronSlowDeathData>("NeutronMissileSlowDeathBehavior", ModuleOwnerKind.Object, (configuration, _) => NeutronSlowDeathData.Parse(configuration), data => new NeutronMissileSlowDeathBehavior(data)),
        new ModuleBinding<MissileLauncherData>("MissileLauncherBuildingUpdate", ModuleOwnerKind.Object, (configuration, _) => MissileLauncherData.Parse(configuration), data => new MissileLauncherBuildingUpdate(data)),
        new ModuleBinding<SpectreData>("SpectreGunshipUpdate", ModuleOwnerKind.Object, (configuration, _) => SpectreData.Parse(configuration), data => new SpectreGunshipUpdate(data)),
    ];

    private static readonly ImmutableArray<string> FactionStructureKinds =
        ["FS_FACTORY", "FS_BASE_DEFENSE", "FS_TECHNOLOGY", "FS_SUPPLY_DROPZONE", "FS_SUPERWEAPON", "FS_BLACK_MARKET", "FS_SUPPLY_CENTER", "FS_STRATEGY_CENTER", "FS_FAKE",
            "FS_INTERNET_CENTER", "FS_ADVANCED_TECH", "FS_BARRACKS", "FS_WARFACTORY", "FS_AIRFIELD"];

    /// <summary><c>Object::isFactionStructure</c>: the object is of one of the <c>KINDOFMASK_FS</c> kinds.</summary>
    public static bool IsFactionStructure(Entity entity) => FactionStructureKinds.Any(entity.Template.IsKindOf);

    /// <summary>Every live, registered entity in id order (a stable snapshot; effects may kill while it is walked).</summary>
    public static Entity[] Snapshot(IWorldServices world) => [..world.Entities.Where(value => !value.DestroyRequested).OrderBy(value => value.Id.Value)];

    /// <summary><c>FROM_BOUNDINGSPHERE_3D</c> as <c>SimulationWorld.DealWeaponDamage</c> measures it: the centre distance minus the candidate's bounding sphere is below <paramref name="radius"/>.</summary>
    public static bool WithinSphere3D(Entity candidate, Fixed x, Fixed y, Fixed z, Fixed radius)
    {
        var dx = candidate.X - x;
        var dy = candidate.Y - y;
        var dz = candidate.Z + candidate.Template.CenterZDelta - z;
        var distanceSquared = dx * dx + dy * dy + dz * dz;
        var sphere = candidate.Template.BoundingSphereRadius;
        var shrunkSquared = distanceSquared;
        if (sphere > Fixed.Zero)
        {
            var reach = radius + sphere;
            if (!SquaredReach.Of(reach).Contains(distanceSquared)) return false;
            var shrunk = Fixed.Sqrt(distanceSquared) - sphere;
            shrunkSquared = shrunk > Fixed.Zero ? shrunk * shrunk : Fixed.Zero;
        }
        return SquaredReach.Of(radius).ContainsStrictly(shrunkSquared);
    }

    /// <summary><c>Object::isAirborneTarget</c> as far as it can be known here: flying clearly above the terrain, or flagged so.</summary>
    public static bool IsAirborne(IWorldServices world, Entity entity) => entity.HasStatus(ObjectStatusBits.Index("AIRBORNE_TARGET")) || world.IsSignificantlyAboveTerrain(entity);

    /// <summary><c>Object::setDisabledUntil(DISABLED_EMP, ...)</c>: the object neither moves nor shoots until <paramref name="until"/>.</summary>
    public static void DisableUntil(IWorldServices world, Entity victim, long until)
    {
        victim.EmpDisabledUntil = until;
        // A weapon cannot become ready while the object is disabled (the movement is stopped by the AI update itself).
        SimulationWorld.DelayWeaponsUntil(victim, until, true);
        world.MarkDirty(victim);
    }

    /// <summary><c>ContainModuleInterface::killAllContained</c>: every passenger leaves the container and dies.</summary>
    public static void KillAllContained(IWorldServices world, Entity container)
    {
        if (container.Container is not { } contain) return;
        foreach (var id in contain.Contained.ToArray())
        {
            if (world.FindEntity(id) is not { } rider) continue;
            contain.Remove(world, rider, false);
            world.Kill(rider.Id);
        }
    }

    public static ArmorInfo ArmorOf(Entity entity)
    {
        if (entity.CrateArmorLevel > 0 && entity.Template.CrateUpgradeArmor[entity.CrateArmorLevel] is { } crate) return crate;
        if (entity.VeterancyLevel > 0 && entity.Template.VeteranArmor[entity.VeterancyLevel] is { } veteran) return veteran;
        return entity.HasPlayerUpgradeArmor && entity.Template.PlayerUpgradeArmor is { } upgraded ? upgraded : entity.Template.DefaultArmor;
    }

    /// <summary>A duration in milliseconds as unrounded ticks (<c>INI::parseDurationReal</c>).</summary>
    public static Fixed FixedTicks(RatedModuleConfiguration configuration, string name)
    {
        var field = ModuleConfigReader.Find(configuration, name);
        if (field is null) return Fixed.Zero;
        return field.Value.Value is GameTime time
            ? configuration.Time.DurationMillisecondsToFixedTicks(time.Milliseconds)
            : throw new InvalidDataException($"{field.Location.Source}({field.Location.Line}): '{name}' of module '{configuration.Name}' is not a duration.");
    }

    public static Fixed Length(Fixed x, Fixed y, Fixed z) => Fixed.Sqrt(x * x + y * y + z * z);
}

// ================================================================================================================ EMPUpdate

/// <summary><c>EMPUpdateModuleData</c> (<c>EMPUpdate.h</c>) in simulation units. The colors, the spark system, <c>SparksPerCubicFoot</c> and the (commented-out) victim kind filter are presentation.</summary>
public sealed record EmpData(long LifetimeTicks, long StartFadeTicks, Fixed StartScale, long DisabledDurationTicks, Fixed TargetScaleMax, Fixed TargetScaleMin, Fixed EffectRadius,
    uint RejectMask, bool DoesNotAffectMyOwnBuildings) : ModuleData
{
    public static EmpData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "Lifetime", 1),
        ModuleConfigReader.Ticks(configuration, "StartFadeTime", 0),
        ModuleConfigReader.Real(configuration, "StartScale", Fixed.One),
        ModuleConfigReader.Ticks(configuration, "DisabledDuration", 0),
        ModuleConfigReader.Real(configuration, "TargetScaleMax", Fixed.One),
        ModuleConfigReader.Real(configuration, "TargetScaleMin", Fixed.One),
        ModuleConfigReader.Real(configuration, "EffectRadius", Fixed.FromInt(200)),
        checked((uint)ObjectStatusBits.Mask(ModuleConfigReader.Flags(configuration, "DoesNotAffect"), NameTablesPhase5.WeaponAffectsMask)),
        ModuleConfigReader.Bool(configuration, "DoesNotAffectMyOwnBuildings", false));
}

/// <summary>
/// <c>EMPUpdate.cpp</c>: the electromagnetic pulse object. It lives <c>Lifetime</c> ticks (and kills itself then), grows toward a random target scale (a presentation value, kept for the
/// drawable) and, <c>StartFadeTime</c> ticks after its creation, disables everything in <c>EffectRadius</c> (the bounding spheres count, <see cref="AirWeaponModules.WithinSphere3D"/>):
/// infantry are never affected; an airborne aircraft that is not <c>EMP_HARDENED</c> is killed outright; a structure is only affected when it is a faction structure; anything else is
/// disabled for <c>DisabledDuration</c> (<see cref="Entity.EmpDisabledUntil"/>: it stands still and its weapon cannot fire), except allies of the pulse when <c>DoesNotAffect</c> lists
/// <c>ALLIES</c> and own structures with <c>DoesNotAffectMyOwnBuildings</c>. Two draws of the logic stream at creation (target scale and heading) as the reference constructor.
/// Not modeled: the patch-1.01 restriction to airborne victims when the firing aircraft's victim was airborne (the pulse does not know its producer), the spark particle systems, the
/// drawable's scale and tint.
/// </summary>
public sealed class EmpUpdate(EmpData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private long _dieTick;
    private long _playTick;
    private bool _fired;
    private Fixed _scale = data.StartScale;
    private Fixed _targetScale;

    public override string ModuleName => "EMPUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary>The pulse has done its disabling (<c>doDisableAttack</c> ran).</summary>
    public bool HasPulsed => _fired;

    /// <summary>The current scale of the pulse (<c>m_currentScale</c>), for the drawable.</summary>
    public Fixed Scale => _scale;

    public override void OnSpawn(IWorldServices world)
    {
        _dieTick = world.Tick + data.LifetimeTicks;
        _playTick = world.Tick + data.StartFadeTicks;
        _targetScale = world.LogicRandom.Range(data.TargetScaleMin, data.TargetScaleMax);
        Entity.Angle = world.LogicRandom.Range(-FixedTrig.Pi, FixedTrig.Pi);
        _nextWake = world.Tick + SimulationTiming.SleepNone;
    }

    public long Update(IWorldServices world)
    {
        // "m_currentScale += (m_targetScale - m_currentScale) * 0.05f" is a share per logic frame of the original: taken on the pulse's frame ticks only (counted back from the
        // tick it dies in, which lies whole frames after its creation), so the scale after every frame is the scale of 30 Hz. Every tick at 30 Hz.
        var ticksPerFrame = world.Time.TicksPerOriginalFrame;
        if (ticksPerFrame == 1 || (_dieTick - world.Tick) % ticksPerFrame == 0) _scale += (_targetScale - _scale) * Fixed.FromDecimal(0.05m);
        if (!_fired && world.Tick >= _playTick)
        {
            _fired = true;
            DoDisableAttack(world);
        }
        if (world.Tick >= _dieTick)
        {
            world.Kill(Entity.Id);
            return SimulationTiming.SleepForever;
        }
        return SimulationTiming.SleepNone;
    }

    private void DoDisableAttack(IWorldServices world)
    {
        if (data.EffectRadius <= Fixed.Zero) return;
        var until = world.Tick + data.DisabledDurationTicks;
        foreach (var victim in AirWeaponModules.Snapshot(world))
        {
            if (victim == Entity || victim.EffectivelyDead || victim.ContainedBy.IsValid) continue;
            if (!AirWeaponModules.WithinSphere3D(victim, Entity.X, Entity.Y, Entity.Z, data.EffectRadius)) continue;
            var structure = victim.Template.IsKindOf("STRUCTURE");
            if (data.DoesNotAffectMyOwnBuildings && structure && victim.Owner == Entity.Owner) continue;
            if (!victim.Template.IsKindOf("VEHICLE") && !structure && !victim.Template.IsKindOf("SPAWNS_ARE_THE_WEAPONS")) continue; // people are not disabled
            if (victim.Template.IsKindOf("AIRCRAFT") && AirWeaponModules.IsAirborne(world, victim))
            {
                if (victim.Template.IsKindOf("EMP_HARDENED")) continue;
                world.Kill(victim.Id);
                continue;
            }
            if (structure)
            {
                if (!AirWeaponModules.IsFactionStructure(victim)) continue;
            }
            else if ((data.RejectMask & WeaponInfo.AffectsAllies) != 0 && world.RelationshipOf(victim.Owner, Entity.Owner) == Relationship.Allies) continue;
            AirWeaponModules.DisableUntil(world, victim, until);
        }
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("dieTick", _dieTick);
        writer.Int("playTick", _playTick);
        writer.Bool("fired", _fired);
        writer.Fixed("scale", _scale);
        writer.Fixed("targetScale", _targetScale);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _dieTick = reader.Int("dieTick");
        _playTick = reader.Int("playTick");
        _fired = reader.Bool("fired");
        _scale = reader.Fixed("scale");
        _targetScale = reader.Fixed("targetScale");
    }
}

// ================================================================================================================ LeafletDropBehavior

/// <summary><c>LeafletDropBehaviorModuleData</c> (<c>EMPUpdate.h</c>): the delay, the disable time and the radius; the leaflet particle system is presentation.</summary>
public sealed record LeafletData(long DelayTicks, long DisabledDurationTicks, Fixed AffectRadius) : ModuleData
{
    public static LeafletData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "Delay", 1),
        ModuleConfigReader.Ticks(configuration, "DisabledDuration", 0),
        ModuleConfigReader.Real(configuration, "AffectRadius", Fixed.FromInt(60)));
}

/// <summary>
/// <c>EMPUpdate.cpp</c> (<c>LeafletDropBehavior</c>): the propaganda leaflet bomb disables the enemy infantry and vehicles within <c>AffectRadius</c> for <c>DisabledDuration</c>
/// (<see cref="Entity.EmpDisabledUntil"/>, as <c>DISABLED_EMP</c>) — as the reference's update is written, once the delay has not yet run out its first update puts the module to sleep
/// for good, so the effect happens when the bomb dies (<c>onDie</c>); after the delay the update would repeat it every tick. Not modeled: the leaflet particle system.
/// </summary>
public sealed class LeafletDropBehavior(LeafletData data) : SimModule, IUpdateModule, IDieModule
{
    private long _nextWake = -1;
    private long _startTick;

    public override string ModuleName => "LeafletDropBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
    {
        _startTick = world.Tick + data.DelayTicks;
        // The first update comes one logic frame of the original after the bomb appeared (at 30 Hz the next tick): with the default Delay of one frame it then acts,
        // as at 30 Hz; one tick after the creation the delay would not have run out above 30 Hz and the module would sleep for good.
        _nextWake = world.Tick + world.Time.TicksPerOriginalFrame;
    }

    public long Update(IWorldServices world)
    {
        if (world.Tick < _startTick) return SimulationTiming.SleepForever;
        DoDisableAttack(world);
        return SimulationTiming.SleepNone;
    }

    public void OnDie(IWorldServices world, in DamageInfo info) => DoDisableAttack(world);

    private void DoDisableAttack(IWorldServices world)
    {
        if (data.AffectRadius <= Fixed.Zero) return;
        var until = world.Tick + data.DisabledDurationTicks;
        foreach (var victim in AirWeaponModules.Snapshot(world))
        {
            if (victim == Entity || victim.EffectivelyDead || victim.ContainedBy.IsValid) continue;
            if (!AirWeaponModules.WithinSphere3D(victim, Entity.X, Entity.Y, Entity.Z, data.AffectRadius)) continue;
            if (!victim.Template.IsKindOf("INFANTRY") && !victim.Template.IsKindOf("VEHICLE")) continue;
            if (world.RelationshipOf(victim.Owner, Entity.Owner) != Relationship.Enemies) continue;
            AirWeaponModules.DisableUntil(world, victim, until);
        }
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("startTick", _startTick);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _startTick = reader.Int("startTick");
    }
}

// ================================================================================================================ BunkerBusterBehavior

/// <summary>
/// <c>BunkerBusterBehaviorModuleData</c> (<c>BunkerBusterBehavior.cpp</c>). The seismic effect radius/magnitude (terrain heaving) are presentation; the crash effect repeats every
/// <c>CrashThroughBunkerFXFrequency</c> ticks while the bomb digs in.
/// </summary>
public sealed record BunkerBusterData(string UpgradeRequired, string DetonationFx, string CrashFx, long CrashFxFrequencyTicks, string ShockwaveWeapon, string OccupantDamageWeapon) : ModuleData
{
    public static BunkerBusterData Parse(RatedModuleConfiguration configuration, UpgradeCatalog upgrades)
    {
        _ = upgrades;
        return new BunkerBusterData(
            ModuleConfigReader.Text(configuration, "UpgradeRequired"),
            ModuleConfigReader.Text(configuration, "DetonationFX"),
            ModuleConfigReader.Text(configuration, "CrashThroughBunkerFX"),
            // The module data constructor: 4 frames.
            ModuleConfigReader.Ticks(configuration, "CrashThroughBunkerFXFrequency", 4),
            ModuleConfigReader.Text(configuration, "ShockwaveWeaponTemplate"),
            ModuleConfigReader.Text(configuration, "OccupantDamageWeaponTemplate"));
    }
}

/// <summary>
/// <c>BunkerBusterBehavior.cpp</c>: the bunker-buster bomb. While it digs in (the missile's kill-self wait) it plays <c>CrashThroughBunkerFX</c> every
/// <c>CrashThroughBunkerFXFrequency</c> ticks (<c>frame % frequency == 1</c>); when it dies (<c>onDie</c> → <c>bustTheBunker</c>) and the owner has <c>UpgradeRequired</c> (when one is named),
/// everybody inside the targeted building is hurt or killed (<c>OccupantDamageWeaponTemplate</c>: 100 damage of the weapon's type to each occupant, who is forced out first; without
/// that weapon they simply die) — only for containers a bunker buster busts (<c>isBustable</c>: garrison and tunnel containers) — the detonation effect plays on the target (else on the
/// bomb) and the shockwave weapon fires there. The victim is the one the bomb's missile follows. Not modeled: the seismic simulation of the terrain.
/// </summary>
public sealed class BunkerBusterBehavior(BunkerBusterData data) : SimModule, IUpdateModule, IDieModule
{
    /// <summary>The remainder the effect fires on (<c>getFrame() % frequency == 1</c>), in logic frames of the original.</summary>
    private const long CrashFxPhaseFrames = 1;

    private long _nextWake = -1;
    private EntityId _victim;

    public override string ModuleName => "BunkerBusterBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    public long Update(IWorldServices world)
    {
        var missile = Entity.ModuleArray.OfType<MissileAIUpdate>().FirstOrDefault();
        if (missile is null) return SimulationTiming.SleepNone; // only a smart bomb (one with an AI) picks up a victim and digs in
        if (!_victim.IsValid && missile.Victim.IsValid) _victim = missile.Victim;
        // BunkerBusterBehavior.cpp, update: getFrame() % m_crashThroughBunkerFXFrequency == 1 – period (data) and phase (one frame) both scale.
        if (data.CrashFxFrequencyTicks > 0 && world.Tick % data.CrashFxFrequencyTicks == world.Time.OriginalFrames(CrashFxPhaseFrames) && missile.IsKillingSelf && data.CrashFx.Length > 0)
            world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.CrashFx, 0, Entity.X, Entity.Y, Entity.Z);
        return SimulationTiming.SleepNone;
    }

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        // The adopted fix bunker-buster-only-when-killing-self (docs/GGC-FIXES.md): only a bomb whose missile kills itself at the target busts.
        if (world.Templates.Rules.Has(Rules.GameFix.BunkerBusterOnlyWhenKillingSelf) && Entity.ModuleArray.OfType<MissileAIUpdate>().FirstOrDefault() is not { IsKillingSelf: true }) return;
        if (data.UpgradeRequired.Length > 0)
        {
            if (!world.Upgrades.TryGet(data.UpgradeRequired, out var upgrade) || !world.PlayerUpgrades(Entity.Owner).Has(upgrade.Index)) return;
        }
        var target = world.FindEntity(_victim);
        var forFx = Entity;
        if (target is not null)
        {
            forFx = target;
            if (target.Container is { } contain && IsBustable(target))
            {
                if (data.OccupantDamageWeapon.Length > 0 && world.Weapons.TryGet(data.OccupantDamageWeapon, out var occupantWeapon))
                {
                    var harm = new DamageInfo(Entity.Id, Fixed.FromInt(100), occupantWeapon.DamageType, occupantWeapon.DeathType, false);
                    // harmAndForceExitAllContained: the list is read from the front again after every occupant (a dying occupant can change it).
                    for (var guard = 0; guard < 1024 && contain.Contained.Count > 0; guard++)
                    {
                        if (world.FindEntity(contain.Contained[0]) is not { } occupant) break;
                        contain.Remove(world, occupant, true);
                        world.Damage(occupant.Id, harm);
                    }
                }
                else AirWeaponModules.KillAllContained(world, target);
            }
        }
        if (data.DetonationFx.Length > 0) world.EmitAt(SimEventKind.FxRequested, forFx.Id, EntityId.Invalid, data.DetonationFx, 0, forFx.X, forFx.Y, forFx.Z);
        if (data.ShockwaveWeapon.Length > 0 && world.Weapons.TryGet(data.ShockwaveWeapon, out var shockwave))
            world.FireWeaponAt(forFx, shockwave, null, forFx.X, forFx.Y, forFx.Z);
    }

    /// <summary><c>ContainModuleInterface::isBustable</c>: <c>GarrisonContain</c>, <c>TunnelContain</c> and <c>CaveContain</c> are; every other container is not.</summary>
    private static bool IsBustable(Entity container) => container.Container is GarrisonContain or TunnelContain;

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.UInt("victim", _victim.Value);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _victim = new EntityId(checked((uint)reader.UInt("victim")));
    }
}

// ================================================================================================================ NeutronBlastBehavior

/// <summary><c>NeutronBlastBehaviorModuleData</c> (<c>NeutronBlastBehavior.h</c>); a missing <c>AffectAirborne</c>/<c>AffectAllies</c> is the reference default (true).</summary>
public sealed record NeutronBlastData(Fixed BlastRadius, bool AffectAirborne, bool AffectAllies) : ModuleData
{
    public static NeutronBlastData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Real(configuration, "BlastRadius", Fixed.FromInt(10)),
        ModuleConfigReader.Bool(configuration, "AffectAirborne", true),
        ModuleConfigReader.Bool(configuration, "AffectAllies", true));
}

/// <summary>
/// <c>NeutronBlastBehavior.cpp</c>: when the neutron shell dies, everything alive within <c>BlastRadius</c> (centre distance, 2D) except the shell itself loses its crew: infantry die, a
/// container's passengers die, a combat bike (<c>CLIFF_JUMPER</c>) dies, any other vehicle that is not a drone becomes unmanned — it stops and goes to the neutral player
/// (<c>setTeam(neutral)</c>); the reference also sets <c>DISABLED_UNMANNED</c> (no disabled-state model: the ownership change is what makes the vehicle useless) and deselects it. Airborne
/// objects and aircraft are skipped unless <c>AffectAirborne</c>; allies unless <c>AffectAllies</c>.
/// </summary>
public sealed class NeutronBlastBehavior(NeutronBlastData data) : SimModule, IUpdateModule, IDieModule
{
    private long _nextWake = SimulationTiming.SleepForever;

    public override string ModuleName => "NeutronBlastBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public long Update(IWorldServices world) => SimulationTiming.SleepForever;

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        var radiusSquared = SquaredReach.Of(data.BlastRadius);
        foreach (var candidate in AirWeaponModules.Snapshot(world))
        {
            if (candidate == Entity || candidate.EffectivelyDead) continue;
            var dx = candidate.X - Entity.X;
            var dy = candidate.Y - Entity.Y;
            if (!radiusSquared.Contains(dx * dx + dy * dy)) continue;
            if (!data.AffectAirborne && (candidate.Template.IsKindOf("AIRCRAFT") || AirWeaponModules.IsAirborne(world, candidate))) continue;
            BlastObject(world, candidate);
        }
    }

    private void BlastObject(IWorldServices world, Entity victim)
    {
        if (!data.AffectAllies && world.RelationshipOf(Entity.Owner, victim.Owner) == Relationship.Allies) return;
        if (victim.Template.IsKindOf("INFANTRY")) world.Kill(victim.Id);
        AirWeaponModules.KillAllContained(world, victim);
        if (!victim.Template.IsKindOf("VEHICLE") || victim.Template.IsKindOf("DRONE")) return;
        if (victim.Template.IsKindOf("CLIFF_JUMPER"))
        {
            world.Kill(victim.Id);
            return;
        }
        if (Array.Find(victim.ModuleArray, module => module is AIUpdateInterface) is AIUpdateInterface ai) ai.OrderIdle(world);
        world.TransferOwnership(victim.Id, PlayerId.System);
    }

    public override void Describe(IStateWriter writer) => writer.Int("nextWake", _nextWake);
    public override void Load(IStateReader reader) => _nextWake = reader.Int("nextWake");
}

// ================================================================================================================ NeutronMissileUpdate

/// <summary>
/// <c>NeutronMissileUpdateModuleData</c> (<c>NeutronMissileUpdate.cpp</c>). The speed factors are per tick (the reference adds <c>speed · direction</c> to the velocity every frame), the
/// turn rate converts from degrees per second; <c>SpecialJitterDistance</c> (a drawable shake), the delivery decal and its radius are presentation.
/// </summary>
public sealed record NeutronMissileData(Fixed DistanceBeforeTurning, Fixed MaxTurnPerTick, Fixed ForwardDamping, Fixed RelativeSpeed, Fixed TargetFromDirectlyAbove, string LaunchFx, string IgnitionFx,
    long SpecialSpeedTicks, Fixed SpecialSpeedHeight, Fixed SpecialAccelFactor) : ModuleData
{
    public static NeutronMissileData Parse(RatedModuleConfiguration configuration)
    {
        var rate = ModuleConfigReader.Find(configuration, "MaxTurnRate") is null ? FixedTrig.TwoPi * 100 : configuration.Time.DegreesPerSecondToRadiansPerTick(ModuleConfigReader.Real(configuration, "MaxTurnRate", Fixed.Zero).ToDecimal());
        return new NeutronMissileData(
            ModuleConfigReader.Real(configuration, "DistanceToTravelBeforeTurning", Fixed.Zero),
            rate,
            ModuleConfigReader.Real(configuration, "ForwardDamping", Fixed.Zero),
            ModuleConfigReader.Real(configuration, "RelativeSpeed", Fixed.One),
            ModuleConfigReader.Real(configuration, "TargetFromDirectlyAbove", Fixed.Zero),
            ModuleConfigReader.Text(configuration, "LaunchFX"),
            ModuleConfigReader.Text(configuration, "IgnitionFX"),
            ModuleConfigReader.Ticks(configuration, "SpecialSpeedTime", 0),
            ModuleConfigReader.Real(configuration, "SpecialSpeedHeight", Fixed.Zero),
            ModuleConfigReader.Real(configuration, "SpecialAccelFactor", Fixed.One));
    }
}

/// <summary>
/// <c>NeutronMissileUpdate.cpp</c>: the missile of the neutron/nuclear superweapon, flown by hand (no locomotor). Fired from its silo
/// (<see cref="IProjectileModule.Launch"/>: <c>projectileFireAtObjectOrPosition</c>), the first update <b>launches</b> it — a launcher that is gone makes it vanish — fires the launch and
/// ignition effects, arms the warhead and starts the attack; the intermediate point is the target raised by <c>TargetFromDirectlyAbove</c>. In the <b>attack</b> it climbs along its heading
/// (straight up from the silo) for <c>DistanceToTravelBeforeTurning</c>, then turns by at most <c>MaxTurnRate</c> per tick toward the intermediate point (toward the target once that was
/// reached within the missile's bounding sphere, where it also turns straight down at half the speed), accelerating by <c>RelativeSpeed</c> along its heading minus
/// <c>ForwardDamping</c> of the velocity; for <c>SpecialSpeedTime</c> after the launch the height follows <c>SpecialSpeedHeight · (SpecialAccelFactor · t)² / SpecialAccelFactor</c> over the
/// start height instead (<c>t</c> the elapsed share). An armed missile that reaches the ground dies (<c>detonate</c> → <c>kill</c>, so the die modules — <c>HeightDieUpdate</c>,
/// <c>NeutronMissileSlowDeathBehavior</c> — run). The turn is computed without arc functions: the heading rotates toward the goal in the plane of both vectors by the turn limit
/// (or snaps when the angle is smaller). Not modeled: the jitter of the model, the exhaust particle system, the delivery decal, collisions with objects.
/// <para>
/// The flight runs on the frame grid of the original, on the missile's own phase (docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md): <c>RelativeSpeed</c> and <c>ForwardDamping</c> are a thrust
/// and a damping share per logic frame, applied together in one step, so the velocity is a velocity per frame and changes once per frame exactly as at 30 Hz — as do the turn (the
/// limit of one frame), the special climb, the distance before turning, the intermediate point and the ground. The launch is decided one frame after the shot, every further step a
/// frame later; the velocity of a frame is flown in equal shares over its ticks. At 30 Hz a frame is a tick and nothing differs.
/// </para>
/// </summary>
public sealed class NeutronMissileUpdate(NeutronMissileData data) : SimModule, IUpdateModule, IProjectileModule
{
    private enum MissileState : byte { PreLaunch, Launch, Attack, Dead }

    private long _nextWake = SimulationTiming.SleepForever;
    private MissileState _state = MissileState.PreLaunch;
    private EntityId _launcher;
    private Point3 _target;
    private Point3 _intermediate;
    private Point3 _velocity;
    private Point3 _direction = new(Fixed.Zero, Fixed.Zero, Fixed.One);
    private bool _launched;
    private bool _armed;
    private bool _reachedIntermediate = true;
    private Fixed _noTurnLeft = data.DistanceBeforeTurning;
    private long _launchTick;
    private Fixed _heightAtLaunch;
    private bool _leftGround;
    // The ticks of the current frame already flown (0: this tick decides the frame's velocity). Always 0 at 30 Hz (and then not written).
    private int _subStep;

    public override string ModuleName => "NeutronMissileUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public EntityId Launcher => _launcher;
    public bool IsArmed => _armed;

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public void Launch(IWorldServices world, in ProjectileLaunch launch)
    {
        _launcher = launch.Launcher;
        _velocity = default;
        _target = new Point3(launch.X, launch.Y, launch.Z);
        _intermediate = _target with { Z = _target.Z + data.TargetFromDirectlyAbove };
        _state = MissileState.Launch;
        _subStep = 0;
        // The launch is decided one logic frame after the shot (the next tick at 30 Hz).
        world.WakeAt(Entity, this, world.Tick + world.Time.OriginalFrames(SimulationTiming.SleepNone));
    }

    public long Update(IWorldServices world)
    {
        if (_state is MissileState.PreLaunch or MissileState.Dead) return SimulationTiming.SleepForever;
        var time = world.Time;
        if (_subStep != 0)
        {
            // Between two frames: one more share of the frame's velocity – unless the frame was completed from outside (Entity.CompleteFrame: nothing of it is left).
            if (Entity.IsInsideFrameStep)
                Entity.CoverShare(world.Tick, time.FrameShare(_velocity.X, _subStep), time.FrameShare(_velocity.Y, _subStep), time.FrameShare(_velocity.Z, _subStep));
            _subStep = time.NextSubStep(_subStep);
            world.MarkDirty(Entity);
            return SimulationTiming.SleepNone;
        }
        if (!_reachedIntermediate)
        {
            var sphere = Entity.Template.BoundingSphereRadius;
            var distance = AirWeaponModules.Length(_intermediate.X - Entity.X, _intermediate.Y - Entity.Y, _intermediate.Z - Entity.Z);
            if (SquaredReach.Compare(distance, sphere) <= 0)
            {
                _reachedIntermediate = true;
                Entity.X = _intermediate.X;
                Entity.Y = _intermediate.Y;
                Entity.Z = _intermediate.Z;
                var speed = AirWeaponModules.Length(_velocity.X, _velocity.Y, _velocity.Z);
                _velocity = new Point3(Fixed.Zero, Fixed.Zero, -speed / Fixed.FromInt(2));
            }
        }
        var before = new Point3(Entity.X, Entity.Y, Entity.Z);
        var wasAttacking = _state == MissileState.Attack;
        switch (_state)
        {
            case MissileState.Launch:
                if (!DoLaunch(world)) return SimulationTiming.SleepForever;
                break;
            case MissileState.Attack:
                DoAttack(world);
                break;
        }
        if (_noTurnLeft > Fixed.Zero && wasAttacking)
            _noTurnLeft -= AirWeaponModules.Length(Entity.X - before.X, Entity.Y - before.Y, Entity.Z - before.Z);
        world.MarkDirty(Entity);
        var ground = world.GroundHeightAt(Entity.X, Entity.Y);
        if (Entity.Z > ground) _leftGround = true;
        // onCollide(NULL) when the missile is no longer above the terrain; it starts on a launch pad above the ground, so a missile that never rose is not on its way down yet.
        if (_state is MissileState.Attack && _leftGround && Entity.Z <= ground)
        {
            // onCollide(NULL): the armed warhead blows up on the ground.
            Detonate(world);
            return SimulationTiming.SleepForever;
        }
        if (_state == MissileState.Dead) return SimulationTiming.SleepForever;
        if (time.TicksPerOriginalFrame != 1)
        {
            // Everything above judged the whole step of this frame, as at 30 Hz; the missile itself flies the first share of it now and the rest on the next ticks.
            // The rest stays readable on the object: who looks at the missile in this tick sees it where this frame ends, as at 30 Hz.
            Entity.X = before.X;
            Entity.Y = before.Y;
            Entity.Z = before.Z;
            Entity.SetAhead(world.Tick, _velocity.X, _velocity.Y, _velocity.Z);
            Entity.CoverShare(world.Tick, time.FrameShare(_velocity.X, 0), time.FrameShare(_velocity.Y, 0), time.FrameShare(_velocity.Z, 0));
            _subStep = time.NextSubStep(0);
        }
        return SimulationTiming.SleepNone;
    }

    /// <summary><c>doLaunch</c>; false when the launcher is gone (the missile removes itself).</summary>
    private bool DoLaunch(IWorldServices world)
    {
        if (!_launched)
        {
            if (world.FindEntity(_launcher) is null)
            {
                _launcher = EntityId.Invalid;
                world.RequestDestroy(Entity.Id);
                _state = MissileState.Dead;
                return false;
            }
            _launched = true;
            if (data.TargetFromDirectlyAbove != Fixed.Zero) _reachedIntermediate = false;
            if (data.LaunchFx.Length > 0) world.EmitAt(SimEventKind.FxRequested, Entity.Id, _launcher, data.LaunchFx, 0, Entity.X, Entity.Y, Entity.Z);
            _heightAtLaunch = Entity.Z;
            _launchTick = world.Tick;
        }
        Entity.X += _velocity.X;
        Entity.Y += _velocity.Y;
        Entity.Z += _velocity.Z;
        if (data.IgnitionFx.Length > 0) world.EmitAt(SimEventKind.FxRequested, Entity.Id, _launcher, data.IgnitionFx, 0, Entity.X, Entity.Y, Entity.Z);
        _state = MissileState.Attack;
        _armed = true;
        return true;
    }

    private void DoAttack(IWorldServices world)
    {
        var speed = data.RelativeSpeed;
        if (data.TargetFromDirectlyAbove != Fixed.Zero && _reachedIntermediate) speed *= Fixed.One / Fixed.FromInt(2);
        if (_noTurnLeft <= Fixed.Zero) _direction = TurnToward(_reachedIntermediate ? _target : _intermediate, data.MaxTurnPerTick * world.Time.TicksPerOriginalFrame);
        _velocity = new Point3(
            _velocity.X + speed * _direction.X - data.ForwardDamping * _velocity.X,
            _velocity.Y + speed * _direction.Y - data.ForwardDamping * _velocity.Y,
            _velocity.Z + speed * _direction.Z - data.ForwardDamping * _velocity.Z);
        var position = new Point3(Entity.X, Entity.Y, Entity.Z);
        var elapsed = world.Tick - _launchTick;
        if (data.SpecialSpeedTicks > 0 && elapsed < data.SpecialSpeedTicks)
        {
            // "now <= frameAtLaunch + specialSpeedTime", then "elapsed < specialSpeedTime": the climb along the special height curve replaces the velocity.
            var fraction = Fixed.FromInt(elapsed) / Fixed.FromInt(data.SpecialSpeedTicks);
            var factor = Fixed.Max(data.SpecialAccelFactor, Fixed.FromDecimal(0.01m));
            var scaled = factor * fraction;
            var height = _heightAtLaunch + scaled * scaled / factor * data.SpecialSpeedHeight;
            _velocity = new Point3(Fixed.Zero, Fixed.Zero, height - position.Z);
        }
        Entity.X = position.X + _velocity.X;
        Entity.Y = position.Y + _velocity.Y;
        Entity.Z = position.Z + _velocity.Z;
        Entity.Angle = FixedTrig.Atan2(_direction.Y, _direction.X);
    }

    /// <summary><c>calcTransform</c>: the heading turned toward <paramref name="goal"/> by at most the turn limit of one logic frame, in the plane of the two directions.</summary>
    private Point3 TurnToward(Point3 goal, Fixed limit)
    {
        var to = new Point3(goal.X - Entity.X, goal.Y - Entity.Y, goal.Z - Entity.Z);
        var length = AirWeaponModules.Length(to.X, to.Y, to.Z);
        if (length <= Fixed.Zero) return _direction;
        to = new Point3(to.X / length, to.Y / length, to.Z / length);
        var dot = Fixed.Min(Fixed.One, Fixed.Max(-Fixed.One, _direction.X * to.X + _direction.Y * to.Y + _direction.Z * to.Z));
        // The angle between the vectors is below the limit exactly when the dot product is above the limit's cosine (a limit of a half turn or more always snaps).
        if (limit >= FixedTrig.Pi || dot >= FixedTrig.Cos(limit)) return to;
        var side = new Point3(to.X - _direction.X * dot, to.Y - _direction.Y * dot, to.Z - _direction.Z * dot);
        var sideLength = AirWeaponModules.Length(side.X, side.Y, side.Z);
        if (sideLength <= Fixed.Zero) return to;
        var cos = FixedTrig.Cos(limit);
        var sin = FixedTrig.Sin(limit);
        var turned = new Point3(_direction.X * cos + side.X / sideLength * sin, _direction.Y * cos + side.Y / sideLength * sin, _direction.Z * cos + side.Z / sideLength * sin);
        var turnedLength = AirWeaponModules.Length(turned.X, turned.Y, turned.Z);
        return turnedLength <= Fixed.Zero ? to : new Point3(turned.X / turnedLength, turned.Y / turnedLength, turned.Z / turnedLength);
    }

    /// <summary><c>detonate</c>: killed (not destroyed) so its die modules run.</summary>
    private void Detonate(IWorldServices world)
    {
        _state = MissileState.Dead;
        world.Kill(Entity.Id);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("state", (int)_state);
        writer.UInt("launcher", _launcher.Value);
        DumbProjectileBehavior.WritePoint(writer, "target", _target);
        DumbProjectileBehavior.WritePoint(writer, "intermediate", _intermediate);
        DumbProjectileBehavior.WritePoint(writer, "velocity", _velocity);
        DumbProjectileBehavior.WritePoint(writer, "direction", _direction);
        writer.Bool("launched", _launched);
        writer.Bool("armed", _armed);
        writer.Bool("reachedIntermediate", _reachedIntermediate);
        writer.Fixed("noTurnLeft", _noTurnLeft);
        writer.Int("launchTick", _launchTick);
        writer.Fixed("heightAtLaunch", _heightAtLaunch);
        writer.Bool("leftGround", _leftGround);
        if (_subStep != 0) writer.Int("subStep", _subStep);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        var state = reader.Int("state");
        if (state < 0 || state > (int)MissileState.Dead) throw new InvalidDataException("Neutron missile state out of range.");
        _state = (MissileState)state;
        _launcher = new EntityId(checked((uint)reader.UInt("launcher")));
        _target = DumbProjectileBehavior.ReadPoint(reader, "target");
        _intermediate = DumbProjectileBehavior.ReadPoint(reader, "intermediate");
        _velocity = DumbProjectileBehavior.ReadPoint(reader, "velocity");
        _direction = DumbProjectileBehavior.ReadPoint(reader, "direction");
        _launched = reader.Bool("launched");
        _armed = reader.Bool("armed");
        _reachedIntermediate = reader.Bool("reachedIntermediate");
        _noTurnLeft = reader.Fixed("noTurnLeft");
        _launchTick = reader.Int("launchTick");
        _heightAtLaunch = reader.Fixed("heightAtLaunch");
        _leftGround = reader.Bool("leftGround");
        _subStep = reader.Has("subStep") ? checked((int)reader.Int("subStep")) : 0;
    }
}

// ================================================================================================================ NeutronMissileSlowDeathBehavior

/// <summary>One of the nine blast stages of <c>NeutronMissileSlowDeathBehavior</c> (<c>BlastInfo</c>): delays are unrounded ticks (<c>parseDurationReal</c>), <c>PushForce</c> belongs to a commented-out physics push.</summary>
public sealed record NeutronBlast(bool Enabled, Fixed DelayTicks, Fixed ScorchDelayTicks, Fixed InnerRadius, Fixed OuterRadius, Fixed MaxDamage, Fixed MinDamage, Fixed ToppleSpeed);

/// <summary><c>NeutronMissileSlowDeathBehaviorModuleData</c> (<c>NeutronMissileSlowDeathUpdate.cpp</c>): the slow-death base, the ground effect list and the blast stages; the scorch mark size is presentation.</summary>
public sealed record NeutronSlowDeathData(SlowDeathData Base, string GroundFx, ImmutableArray<NeutronBlast> Blasts) : ModuleData
{
    public static NeutronSlowDeathData Parse(RatedModuleConfiguration configuration)
    {
        var blasts = ImmutableArray.CreateBuilder<NeutronBlast>();
        for (var stage = 1; stage <= 9; stage++)
        {
            var prefix = $"Blast{stage}";
            blasts.Add(new NeutronBlast(
                ModuleConfigReader.Bool(configuration, prefix + "Enabled", false),
                AirWeaponModules.FixedTicks(configuration, prefix + "Delay"), AirWeaponModules.FixedTicks(configuration, prefix + "ScorchDelay"),
                ModuleConfigReader.Real(configuration, prefix + "InnerRadius", Fixed.Zero), ModuleConfigReader.Real(configuration, prefix + "OuterRadius", Fixed.Zero),
                ModuleConfigReader.Real(configuration, prefix + "MaxDamage", Fixed.Zero), ModuleConfigReader.Real(configuration, prefix + "MinDamage", Fixed.Zero),
                ModuleConfigReader.Real(configuration, prefix + "ToppleSpeed", Fixed.Zero)));
        }
        return new NeutronSlowDeathData(SlowDeathData.Parse(configuration), ModuleConfigReader.Text(configuration, "FXList"), blasts.ToImmutable());
    }
}

/// <summary>
/// <c>NeutronMissileSlowDeathUpdate.cpp</c>: the explosion of the neutron missile. It is a <c>SlowDeathBehavior</c> (lottery weight, destruction delay and variance, both draws as the
/// base class), and from the moment it is chosen it plays <c>FXList</c> on the ground below and runs its enabled blast stages: a stage whose delay (in unrounded ticks since the activation, strictly
/// greater) has passed topples every object within its outer radius (2D) away from the centre at <c>ToppleSpeed</c> (<see cref="ToppleUpdate"/>) and hurts it — the full <c>MaxDamage</c> within
/// the inner radius, falling off linearly to the outer radius (<c>1 − (d − inner)/(outer − inner + 0.01)</c> of the maximum, at least <c>MinDamage</c>; <c>d</c> the 3D distance) as
/// <c>EXPLOSION</c>/<c>EXPLODED</c> damage credited to the missile after the object's armor; nothing happens for 0 damage. The scorch stage only blackens (<c>MODELCONDITION_BURNED</c>, the
/// shadows of shrubbery): presentation, not modeled, like the scorch mark and the physics push (commented out in the reference).
/// </summary>
public sealed class NeutronMissileSlowDeathBehavior(NeutronSlowDeathData data) : DieModuleBase(data.Base.Mux), IUpdateModule, ISlowDeathCandidate
{
    private static readonly int ExplodedDeath = NameTables.DeathType.TryIndexOf("EXPLODED", out var index) ? index : DeathTypes.Normal;

    private long _nextWake = -1;
    private bool _activated;
    private long _destructionTick;
    private long _activationTick;
    private readonly bool[] _completedBlasts = new bool[9];

    public override string ModuleName => "NeutronMissileSlowDeathBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public bool IsApplicable(in DamageInfo info) => Mux.IsApplicable(Entity, info);
    public int ProbabilityWeight => Math.Max(data.Base.ProbabilityModifier, 1);

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public override void OnDie(IWorldServices world, in DamageInfo info) => SlowDeathLottery.Run(world, Entity, info, IsApplicable(info));

    public void BeginSlowDeath(IWorldServices world)
    {
        _activated = true;
        // SlowDeathBehavior.cpp, SlowDeathBehavior::beginSlowDeath: GameLogicRandomValue(0, m_sinkDelayVariance) / (0, m_destructionDelayVariance) –
        // both drawn in frames of the original, then scaled; the destruction is at least one frame away.
        _ = data.Base.SinkDelayTicks + world.Time.DrawFrames(world.LogicRandom, 0, data.Base.SinkDelayVarianceTicks);
        var destruction = data.Base.DestructionDelayTicks + world.Time.DrawFrames(world.LogicRandom, 0, data.Base.DestructionDelayVarianceTicks);
        _destructionTick = world.Tick + world.Time.AtLeastOneFrame(destruction);
        // The explosion starts with the update after the death: one logic frame of the original later (the next tick at 30 Hz), not one tick.
        world.WakeAt(Entity, this, world.Tick + world.Time.OriginalFrames(SimulationTiming.SleepNone));
    }

    public long Update(IWorldServices world)
    {
        if (!_activated) return SimulationTiming.SleepForever;
        if (world.Tick >= _destructionTick) world.RequestDestroy(Entity.Id);
        if (_activationTick == 0)
        {
            _activationTick = world.Tick;
            if (data.GroundFx.Length > 0) world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.GroundFx, 0, Entity.X, Entity.Y, world.GroundHeightAt(Entity.X, Entity.Y));
        }
        // The reference compares whole elapsed frames with the unrounded delay (currFrame - m_activationFrame > delay): the elapsed time is taken in
        // whole frames of the original, so a stage goes off on the same frame boundary at every rate.
        var elapsed = Fixed.FromInt(world.Time.FloorToFrame(world.Tick - _activationTick));
        for (var stage = 0; stage < data.Blasts.Length; stage++)
        {
            var blast = data.Blasts[stage];
            if (!blast.Enabled || _completedBlasts[stage]) continue;
            if (elapsed > blast.DelayTicks)
            {
                DoBlast(world, blast);
                _completedBlasts[stage] = true;
            }
        }
        return SimulationTiming.SleepNone;
    }

    private void DoBlast(IWorldServices world, NeutronBlast blast)
    {
        if (blast.OuterRadius <= Fixed.Zero) return;
        var outerSquared = SquaredReach.Of(blast.OuterRadius);
        foreach (var other in AirWeaponModules.Snapshot(world))
        {
            if (other == Entity || other.EffectivelyDead || other.ContainedBy.IsValid) continue;
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            // The adopted fix neutron-blast-bounding-sphere (docs/GGC-FIXES.md): FROM_BOUNDINGSPHERE_3D instead of the original's FROM_CENTER_2D.
            var inReach = world.Templates.Rules.Has(Rules.GameFix.NeutronBlastBoundingSphere)
                ? AirWeaponModules.WithinSphere3D(other, Entity.X, Entity.Y, Entity.Z, blast.OuterRadius)
                : outerSquared.Contains(dx * dx + dy * dy);
            if (!inReach) continue;
            var dz = other.Z - Entity.Z;
            if (Array.Find(other.ModuleArray, module => module is ToppleUpdate) is ToppleUpdate topple) topple.ApplyTopplingForce(world, dx, dy, blast.ToppleSpeed);
            var distance = AirWeaponModules.Length(dx, dy, dz);
            Fixed amount;
            if (distance <= blast.InnerRadius) amount = blast.MaxDamage;
            else
            {
                var percent = Fixed.One - (distance - blast.InnerRadius) / (blast.OuterRadius - blast.InnerRadius + Fixed.FromDecimal(0.01m));
                amount = Fixed.Max(blast.MaxDamage * percent, blast.MinDamage);
            }
            if (amount == Fixed.Zero || other.Body is null) continue;
            world.Damage(other.Id, new DamageInfo(Entity.Id, AirWeaponModules.ArmorOf(other).Adjust(DamageTypes.Explosion, amount), DamageTypes.Explosion, ExplodedDeath, false));
        }
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("activated", _activated);
        writer.Int("destructionTick", _destructionTick);
        writer.Int("activationTick", _activationTick);
        writer.BeginArray("completed");
        foreach (var done in _completedBlasts)
        {
            writer.BeginObject(null);
            writer.Bool("done", done);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _activated = reader.Bool("activated");
        _destructionTick = reader.Int("destructionTick");
        _activationTick = reader.Int("activationTick");
        var completed = reader.Array("completed");
        for (var index = 0; index < _completedBlasts.Length && index < completed.Count; index++) _completedBlasts[index] = completed[index].Bool("done");
    }
}

// ================================================================================================================ MissileLauncherBuildingUpdate

/// <summary><c>MissileLauncherBuildingUpdateModuleData</c> (<c>MissileLauncherBuildingUpdate.h</c>): the power it serves, the door timing and the effect lists played at each door state; the idle audio is presentation.</summary>
public sealed record MissileLauncherData(string PowerName, long DoorOpenTicks, long DoorWaitOpenTicks, long DoorCloseTicks, string OpeningFx, string OpenFx, string WaitingToCloseFx, string ClosingFx, string ClosedFx) : ModuleData
{
    public static MissileLauncherData Parse(RatedModuleConfiguration configuration) => new(
        SpecialPowerModules.RequiredPower(configuration),
        ModuleConfigReader.Ticks(configuration, "DoorOpenTime", 0), ModuleConfigReader.Ticks(configuration, "DoorWaitOpenTime", 0), ModuleConfigReader.Ticks(configuration, "DoorCloseTime", 0),
        ModuleConfigReader.Text(configuration, "DoorOpeningFX"), ModuleConfigReader.Text(configuration, "DoorOpenFX"), ModuleConfigReader.Text(configuration, "DoorWaitingToCloseFX"),
        ModuleConfigReader.Text(configuration, "DoorClosingFX"), ModuleConfigReader.Text(configuration, "DoorClosedFX"));
}

/// <summary>
/// <c>MissileLauncherBuildingUpdate.cpp</c>: the door of a missile silo follows the recharge of its superweapon. The door opens (<c>DOOR_OPENING</c>) <c>DoorOpenTime</c> before the power is
/// ready and is fully open (<c>DOOR_OPEN</c>) one tick ahead of it (it is forced open the moment the power is ready); when the power is used (<c>initiateIntentToDoSpecialPower</c>, via
/// <see cref="ISpecialPowerHandler"/>) the door waits open for <c>DoorWaitOpenTime</c> (<c>DOOR_WAITING_TO_CLOSE</c>), then closes for <c>DoorCloseTime</c> (at most half the time left until the power is
/// ready again, <c>DOOR_CLOSING</c>) and is closed again. The effect list of each state is requested when the state is entered. The module has no gameplay effect of its own: the door is
/// <see cref="Door"/> for the presentation (the model conditions and the idle sound of the reference), nothing waits for it.
/// </summary>
public sealed class MissileLauncherBuildingUpdate(MissileLauncherData data) : SimModule, IUpdateModule, ISpecialPowerHandler
{
    public enum DoorState : byte { Closed, Opening, Open, WaitingToClose, Closing }

    private long _nextWake = -1;
    private int _power = -1;
    private DoorState _door = DoorState.Closed;
    private DoorState _timeoutState = DoorState.Closed;
    private long _timeoutTick;

    public override string ModuleName => "MissileLauncherBuildingUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary>The door's state (<c>m_doorState</c>).</summary>
    public DoorState Door => _door;

    public override void OnSpawn(IWorldServices world)
    {
        _power = world.SpecialPowers.TryGet(data.PowerName, out var info)
            ? info.Index
            : throw new InvalidOperationException($"'{Entity.Template.Name}': special power '{data.PowerName}' is not a SpecialPower definition.");
        _nextWake = world.Tick + SimulationTiming.SleepNone;
    }

    public bool Handles(IWorldServices world, int powerIndex) => powerIndex == _power;

    public void Initiate(IWorldServices world, int powerIndex, in SpecialPowerTarget target) => SwitchTo(world, DoorState.WaitingToClose);

    public long Update(IWorldServices world)
    {
        // Under construction any decision about the door could be wrong (the power module's recharge is not set yet).
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction)) return SimulationTiming.SleepNone;
        if (SpecialPowerRouting.FindModule(Entity, _power) is not SpecialPowerModuleBase power) return SimulationTiming.SleepNone;
        var now = world.Tick;
        var readyTick = power.AvailableOnFrame(world);
        var startOpening = readyTick >= data.DoorOpenTicks ? readyTick - data.DoorOpenTicks : 0;
        if (_timeoutTick != 0 && now > _timeoutTick) SwitchTo(world, _timeoutState);
        if (_door != DoorState.Open && power.IsReady(world)) SwitchTo(world, DoorState.Open);
        else if (_door == DoorState.Closed && now >= startOpening) SwitchTo(world, DoorState.Opening);
        return SimulationTiming.SleepNone;
    }

    private void SwitchTo(IWorldServices world, DoorState state)
    {
        if (_door == state) return;
        var now = world.Tick;
        string fx;
        switch (state)
        {
            case DoorState.Closed:
                _timeoutTick = 0;
                _timeoutState = DoorState.Closed;
                fx = data.ClosedFx;
                break;
            case DoorState.Opening:
                // "we want this to be done BEFORE the power is ready, so end it one frame ahead".
                _timeoutTick = SpecialPowerRouting.FindModule(Entity, _power) is SpecialPowerModuleBase opening ? opening.AvailableOnFrame(world) - TickOrder.Next : 0;
                _timeoutState = DoorState.Open;
                fx = data.OpeningFx;
                break;
            case DoorState.Open:
                _timeoutTick = 0;
                _timeoutState = DoorState.Open;
                fx = data.OpenFx;
                break;
            case DoorState.WaitingToClose:
                _timeoutTick = now + data.DoorWaitOpenTicks;
                _timeoutState = DoorState.Closing;
                fx = data.WaitingToCloseFx;
                break;
            default:
                _timeoutTick = now + data.DoorCloseTicks;
                if (SpecialPowerRouting.FindModule(Entity, _power) is SpecialPowerModuleBase closing)
                {
                    // Halved in frames of the original (the reference halves a frame count), then scaled.
                    var half = world.Time.OriginalFrames(world.Time.ToOriginalFrames(closing.AvailableOnFrame(world) - now) / 2);
                    if (_timeoutTick > now + half) _timeoutTick = now + half;
                }
                _timeoutState = DoorState.Closed;
                fx = data.ClosingFx;
                break;
        }
        _door = state;
        if (fx.Length > 0) world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, fx, 0, Entity.X, Entity.Y, Entity.Z);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("power", _power);
        writer.Int("door", (int)_door);
        writer.Int("timeoutState", (int)_timeoutState);
        writer.Int("timeoutTick", _timeoutTick);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _power = checked((int)reader.Int("power"));
        var door = reader.Int("door");
        var timeout = reader.Int("timeoutState");
        if (door < 0 || door > (int)DoorState.Closing || timeout < 0 || timeout > (int)DoorState.Closing) throw new InvalidDataException("Door state out of range.");
        _door = (DoorState)door;
        _timeoutState = (DoorState)timeout;
        _timeoutTick = reader.Int("timeoutTick");
    }
}

// ================================================================================================================ SpectreGunshipUpdate

/// <summary>
/// <c>SpectreGunshipUpdateModuleData</c> (<c>SpectreGunshipUpdate.cpp</c>) in simulation units; the constructor defaults are the reference's (radius 200, reticle 25, orbit radius 250, strafing 20,
/// slope 0.7, random offset 20, firing rate 10 ticks). The two radius decals and the strafe particle system are presentation, except that the particle system's presence enables the strafing of the aim point.
/// </summary>
public sealed record SpectreData(string PowerName, string GattlingTemplate, string HowitzerWeapon, long HowitzerFiringRateTicks, long OrbitTicks, long HowitzerFollowLagTicks, Fixed AttackAreaRadius,
    Fixed StrafingIncrement, Fixed OrbitInsertionSlope, Fixed RandomOffsetForHowitzer, Fixed TargetingReticleRadius, Fixed GunshipOrbitRadius, bool Strafes) : ModuleData
{
    public static SpectreData Parse(RatedModuleConfiguration configuration) => new(
        SpecialPowerModules.RequiredPower(configuration),
        ModuleConfigReader.Text(configuration, "GattlingTemplateName"),
        ModuleConfigReader.Text(configuration, "HowitzerWeaponTemplate"),
        configuration.Time.AtLeastOneFrame(ModuleConfigReader.Ticks(configuration, "HowitzerFiringRate", 10)),
        ModuleConfigReader.Ticks(configuration, "OrbitTime", 0),
        ModuleConfigReader.Ticks(configuration, "HowitzerFollowLag", 0),
        ModuleConfigReader.Real(configuration, "AttackAreaRadius", Fixed.FromInt(200)),
        // m_strafingIncrement: distance per logic frame of the original (INI::parseReal, applied once per update).
        configuration.Time.PerOriginalFrameToPerTick(ModuleConfigReader.Real(configuration, "StrafingIncrement", Fixed.FromInt(20))),
        ModuleConfigReader.Real(configuration, "OrbitInsertionSlope", Fixed.FromDecimal(0.7m)),
        ModuleConfigReader.Real(configuration, "RandomOffsetForHowitzer", Fixed.FromInt(20)),
        ModuleConfigReader.Real(configuration, "TargetingReticleRadius", Fixed.FromInt(25)),
        ModuleConfigReader.Real(configuration, "GunshipOrbitRadius", Fixed.FromInt(250)),
        ModuleConfigReader.Text(configuration, "GattlingStrafeFXParticleSystem").Length > 0);
}

/// <summary>
/// <c>SpectreGunshipUpdate.cpp</c>: the Spectre gunship object. The special power (<see cref="ISpecialPowerHandler"/>, called by the carrier's deployment with the target) puts the gunship on its
/// way: it <b>inserts</b> from where it stands toward a point on a circle of <c>GunshipOrbitRadius</c> around the target, ahead of it by the orbit-insertion slope (between 0.5 and 0.8 of the way
/// between "straight at the target" and "a quarter turn around it") — recomputed every tick like the reference's <c>aiMoveToPosition</c>, which makes it circle — at the transit locomotor
/// (<c>SET_PANIC</c>) with its gattling held (<c>DISABLED_PARALYZED</c>: no fire), and starts to <b>orbit</b> (<c>SET_NORMAL</c>, gattling released) as soon as it is closer than the orbit radius,
/// for <c>OrbitTime</c> ticks. While orbiting, every <c>HowitzerFiringRate</c> ticks it looks for the nearest enemy that is not stealthed-undetected, not airborne and farther from the ship than
/// three quarters of the orbit radius inside the targeting reticle around the (constrained) override point — computer players also anywhere in the attack area — and the gattling attacks it
/// (else the strafing aim point); the gattling's aim point creeps toward the target by <c>StrafingIncrement</c> per tick while it fires, and once it has stayed on it longer than
/// <c>HowitzerFollowLag</c> ticks each cycle also fires the howitzer weapon at the aim point with a random offset (<c>RandomOffsetForHowitzer</c>). After the orbit it <b>departs</b> straight
/// ahead at the transit locomotor and is deleted once it left the map; the gattling is deleted with the cleanup. Movement is kinematic (turn rate and speed of the locomotor; the height is the
/// locomotor's preferred height at once); the gattling is a free-standing entity that follows the ship (put into its container when the container accepts it). Not modeled: fog of war (the
/// reference lets it fire only at what its player sees), the command-from-script variant, the override-destination command from the player, the radius decals, afterburner and door
/// model conditions.
/// </summary>
public sealed class SpectreGunshipUpdate(SpectreData data) : SimModule, IUpdateModule, ISpecialPowerHandler
{
    public enum GunshipStatus : byte { Inserting, Orbiting, Departing, Idle }

    private static readonly Fixed OrbitSlopeMax = Fixed.FromDecimal(0.8m);
    private static readonly Fixed OrbitSlopeMin = Fixed.FromDecimal(0.5m);
    private static readonly Fixed FairDistanceShare = Fixed.FromDecimal(0.75m);

    /// <summary>Own value: "the gattling is firing" (the reference's <c>OBJECT_STATUS_IS_FIRING_WEAPON</c>) means its last shot is at most two logic frames of the original old.</summary>
    private const long GattlingFiringWindowFrames = 2;

    private long _nextWake = SimulationTiming.SleepForever;
    private int _power = -1;
    private GunshipStatus _status = GunshipStatus.Idle;
    private Point3 _initialTarget;
    private Point3 _overrideTarget;
    private Point3 _satellite;
    private Point3 _gattlingTarget;
    private Point3 _positionToShoot;
    private long _okToFireHowitzerCounter;
    private long _orbitEscapeTick;
    private EntityId _gattling;
    private bool _gattlingPlaced;
    /// <summary>The step of the running logic frame of the original of the ship's flight, flown in shares per tick; empty at 30 Hz.</summary>
    private FlightFrameStep _flight;

    public override string ModuleName => "SpectreGunshipUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public GunshipStatus Status => _status;
    public EntityId Gattling => _gattling;
    public Point3 InitialTarget => _initialTarget;

    /// <summary><c>isActive</c>: the gunship is inserting or orbiting.</summary>
    public bool IsActive => _status < GunshipStatus.Departing;

    public override void OnSpawn(IWorldServices world)
    {
        _power = world.SpecialPowers.TryGet(data.PowerName, out var info)
            ? info.Index
            : throw new InvalidOperationException($"'{Entity.Template.Name}': special power '{data.PowerName}' is not a SpecialPower definition.");
        _satellite = new Point3(Entity.X, Entity.Y, Entity.Z);
        _nextWake = SimulationTiming.SleepForever;
        // Created by a carrier's SpectreGunshipDeploymentUpdate: the carrier fired the gunship's power at the target right after creating it.
        if (SpectreGunshipDeploymentUpdate.TryTakePending(world, Entity, out var x, out var y))
            Initiate(world, _power, new SpecialPowerTarget(SpecialPowerTargetKind.Location, EntityId.Invalid, x, y));
    }

    public bool Handles(IWorldServices world, int powerIndex) => powerIndex == _power;

    /// <summary><c>initiateIntentToDoSpecialPower</c> (not fired by a script): the target position becomes the initial target, the override and the gattling's aim point; the gattling is created
    /// and held; the power's recharge starts (<c>markSpecialPowerTriggered</c>).</summary>
    public void Initiate(IWorldServices world, int powerIndex, in SpecialPowerTarget target)
    {
        var (x, y) = target.Kind == SpecialPowerTargetKind.Object && world.FindEntity(target.Entity) is { } victim ? (victim.X, victim.Y) : (target.X, target.Y);
        var ground = world.GroundHeightAt(x, y);
        _initialTarget = new Point3(x, y, ground);
        _overrideTarget = _initialTarget;
        _gattlingTarget = _initialTarget;
        _positionToShoot = _initialTarget;
        _okToFireHowitzerCounter = 0;
        _strafeLeft = 0;
        _status = GunshipStatus.Inserting;
        _flight.Cancel();
        _flight.Publish(world.Time, world.Tick, Entity);
        CleanUp(world);
        if (data.GattlingTemplate.Length > 0 && world.Templates.TryGet(data.GattlingTemplate, out var template) && template.IsInstantiable)
        {
            _gattling = world.RequestSpawn(template.Index, Entity.Owner, Entity.X, Entity.Y, Entity.Z, Entity.Angle);
            _gattlingPlaced = false;
        }
        SpecialPowerRouting.FindModule(Entity, powerIndex)?.Trigger(world);
        // A gunship that is still being created is scheduled when it is registered; a registered one is woken through the world (which compares with the current wake tick).
        if (Entity.Registered)
        {
            world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
            world.MarkDirty(Entity);
        }
        else _nextWake = world.Tick + SimulationTiming.SleepNone;
    }

    private LocomotorInfo? Transit => Entity.Template.LocomotorSets.TryGetValue("SET_PANIC", out var panic) ? panic : Entity.Template.Locomotor;

    public long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead)
        {
            // "the gunship must have gotten shot down": the strike is over.
            if (_status != GunshipStatus.Idle) { _status = GunshipStatus.Idle; CleanUp(world); }
            return SimulationTiming.SleepForever;
        }
        if (_status == GunshipStatus.Idle) return SimulationTiming.SleepForever;
        var gattling = world.FindEntity(_gattling);
        if (gattling is not null) FollowShip(world, gattling);

        // The flight is the 30 Hz step, decided where a logic frame of the original begins (the howitzer's rhythm lies on the same frames) and flown in shares per tick; the
        // aim point of the orbit, the switch to the orbit and the check of the departure belong to the decision and see the ship where the 30 Hz step puts it.
        var decides = !_flight.Continue(Entity) && world.Time.IsOriginalFrameBoundary(world.Tick);
        (Fixed X, Fixed Y, Fixed Turn) step = default;
        if (_status is GunshipStatus.Inserting or GunshipStatus.Orbiting)
        {
            var locomotor = _status == GunshipStatus.Inserting ? Transit : Entity.Template.Locomotor;
            if (decides)
            {
                UpdateSatellite();
                step = Fly(world, locomotor, _satellite.X, _satellite.Y);
                ConstrainOverride();
                var distanceToTarget = AirModules.Distance2D(Entity.X, Entity.Y, _initialTarget.X, _initialTarget.Y);
                if (_status == GunshipStatus.Inserting && distanceToTarget < data.GunshipOrbitRadius)
                {
                    _status = GunshipStatus.Orbiting;
                    _orbitEscapeTick = world.Tick + data.OrbitTicks;
                }
            }
            else HoldHeight(world, locomotor);
        }
        if (_status == GunshipStatus.Orbiting) Orbit(world, gattling);
        else if (_status == GunshipStatus.Departing)
        {
            if (decides) step = Depart(world);
            else HoldHeight(world, Transit);
        }
        if (decides) _flight.Begin(world.Time, Entity, step.X, step.Y, Fixed.Zero, step.Turn);
        // Observers read the ship where the frame it has begun ends.
        _flight.Publish(world.Time, world.Tick, Entity);
        world.MarkDirty(Entity);
        return SimulationTiming.SleepNone;
    }

    private void FollowShip(IWorldServices world, Entity gattling)
    {
        if (!_gattlingPlaced)
        {
            _gattlingPlaced = true;
            if (Entity.Container is { } contain && !gattling.ContainedBy.IsValid && contain.IsValidContainerFor(world, gattling, true)) contain.Add(world, gattling);
        }
        if (gattling.ContainedBy.IsValid) return;
        gattling.X = Entity.X;
        gattling.Y = Entity.Y;
        gattling.Z = Entity.Z;
        world.MarkDirty(gattling);
    }

    /// <summary>The orbit point of the reference: the target plus the orbit radius along "perigee · n1 + apogee · n2" (perigee: target to ship, apogee: a quarter turn anticlockwise from it).</summary>
    private void UpdateSatellite()
    {
        var px = Entity.X - _initialTarget.X;
        var py = Entity.Y - _initialTarget.Y;
        var length = Fixed.Sqrt(px * px + py * py);
        if (length > Fixed.Zero) { px /= length; py /= length; }
        else { px = Fixed.One; py = Fixed.Zero; }
        var n1 = Fixed.Min(OrbitSlopeMax, Fixed.Max(OrbitSlopeMin, data.OrbitInsertionSlope));
        var n2 = Fixed.One - n1;
        var dx = px * n1 + -py * n2;
        var dy = py * n1 + px * n2;
        _satellite = new Point3(_initialTarget.X + dx * data.GunshipOrbitRadius, _initialTarget.Y + dy * data.GunshipOrbitRadius, _satellite.Z);
    }

    /// <summary>The override point stays within <c>AttackAreaRadius − TargetingReticleRadius</c> of the initial target.</summary>
    private void ConstrainOverride()
    {
        var limit = data.AttackAreaRadius - data.TargetingReticleRadius;
        var dx = _initialTarget.X - _overrideTarget.X;
        var dy = _initialTarget.Y - _overrideTarget.Y;
        var length = Fixed.Sqrt(dx * dx + dy * dy);
        if (length <= limit || length <= Fixed.Zero) return;
        _overrideTarget = _overrideTarget with { X = _initialTarget.X - dx / length * limit, Y = _initialTarget.Y - dy / length * limit };
    }

    /// <summary>
    /// Kinematic flight, one step of a logic frame of the original: turn toward the point by the locomotor's turn of a frame, change the speed by its acceleration of a frame, advance
    /// by the travel of a frame, hold the preferred height. Returns the step (travel and turn) for <see cref="FlightFrameStep.Begin"/>; the speed stays a value per tick.
    /// </summary>
    private (Fixed X, Fixed Y, Fixed Turn) Fly(IWorldServices world, LocomotorInfo? locomotor, Fixed goalX, Fixed goalY)
    {
        if (locomotor is null) return default;
        var time = world.Time;
        var heading = Entity.Angle;
        var desired = FixedTrig.Atan2(goalY - Entity.Y, goalX - Entity.X);
        var difference = FixedTrig.AngleDifference(Entity.Angle, desired);
        var turn = FlightFrames.PerFrame(time, locomotor.TurnRatePerTick);
        Entity.Angle = turn > Fixed.Zero && difference.Abs() > turn ? FixedTrig.Normalize(Entity.Angle + (difference.IsNegative ? -turn : turn)) : desired;
        Entity.Speed = Entity.Speed < locomotor.Speed
            ? Fixed.Min(locomotor.Speed, Entity.Speed + FlightFrames.PerFrame(time, locomotor.Acceleration))
            : Fixed.Max(locomotor.Speed, Entity.Speed - FlightFrames.PerFrame(time, locomotor.Braking));
        var stepX = FixedTrig.Cos(Entity.Angle) * FlightFrames.PerFrame(time, Entity.Speed);
        var stepY = FixedTrig.Sin(Entity.Angle) * FlightFrames.PerFrame(time, Entity.Speed);
        Entity.X += stepX;
        Entity.Y += stepY;
        Entity.Z = world.GroundHeightAt(Entity.X, Entity.Y) + locomotor.PreferredHeight;
        // At 30 Hz a frame is a tick: there is nothing to spread and the turn is not measured.
        return (stepX, stepY, time.TicksPerOriginalFrame > 1 ? FixedTrig.AngleDifference(heading, Entity.Angle) : Fixed.Zero);
    }

    /// <summary>Between two decisions the ship keeps its height over the ground it flies over.</summary>
    private void HoldHeight(IWorldServices world, LocomotorInfo? locomotor)
    {
        if (locomotor is not null) Entity.Z = world.GroundHeightAt(Entity.X, Entity.Y) + locomotor.PreferredHeight;
    }

    private void Orbit(IWorldServices world, Entity? gattling)
    {
        if (world.Tick >= _orbitEscapeTick)
        {
            // disengageAndDepartAO: cease fire, head off the map along the heading at the transit locomotor.
            CleanUp(world);
            _status = GunshipStatus.Departing;
            return;
        }
        if (world.Tick % data.HowitzerFiringRateTicks == 0)
        {
            _positionToShoot = _overrideTarget;
            var victim = FindTarget(world, _overrideTarget.X, _overrideTarget.Y, data.TargetingReticleRadius);
            // The wide-range auto-acquire is switched off for human players: the ship needs babysitting.
            if (victim is null && world.IsComputerPlayer(Entity.Owner))
            {
                victim = FindTarget(world, _initialTarget.X, _initialTarget.Y, data.AttackAreaRadius);
                if (victim is not null) _positionToShoot = new Point3(victim.X, victim.Y, victim.Z);
            }
            _currentVictim = victim?.Id ?? EntityId.Invalid;
            // The counter counts logic frames of the original (Strafe), the lag of the data is a duration on the frame grid.
            if (_okToFireHowitzerCounter > world.Time.ToOriginalFrames(data.HowitzerFollowLagTicks) && data.HowitzerWeapon.Length > 0 && world.Weapons.TryGet(data.HowitzerWeapon, out var howitzer))
            {
                var offset = data.RandomOffsetForHowitzer;
                var x = _gattlingTarget.X + world.LogicRandom.Range(-offset, offset);
                var y = _gattlingTarget.Y + world.LogicRandom.Range(-offset, offset);
                world.FireWeaponAt(Entity, howitzer, null, x, y, _gattlingTarget.Z);
                world.Emit(SimEventKind.WeaponFired, Entity.Id, EntityId.Invalid, howitzer.Name, 0);
            }
        }
        if (gattling is not null)
        {
            var victim = world.FindEntity(_currentVictim);
            if (victim is not null && !victim.EffectivelyDead) FireGattling(world, gattling, victim, victim.X, victim.Y, victim.Z);
            else FireGattling(world, gattling, null, _gattlingTarget.X, _gattlingTarget.Y, _gattlingTarget.Z);
            if (data.Strafes && world.Tick - gattling.LastFireTick <= world.Time.OriginalFrames(GattlingFiringWindowFrames) && gattling.LastFireTick >= 0) Strafe(world);
        }
    }

    private EntityId _currentVictim;

    /// <summary>
    /// The gattling winds its aim toward where it should shoot, one <c>StrafingIncrement</c> per logic frame of the original; staying on it counts toward the howitzer follow-up, one
    /// per frame. The 30 Hz step, decided where a frame of the original begins (as the flight and the howitzer's rhythm): the aim point is on its goal when the rest is less than
    /// the increment of a frame; an increment is wound in equal shares per tick.
    /// </summary>
    private void Strafe(IWorldServices world)
    {
        var ticks = world.Time.TicksPerOriginalFrame;
        if (!world.Time.IsOriginalFrameBoundary(world.Tick))
        {
            if (_strafeLeft == 0) return;
            _strafeLeft--;
            _gattlingTarget = new Point3(_gattlingTarget.X + _strafeShare.X, _gattlingTarget.Y + _strafeShare.Y, _gattlingTarget.Z + _strafeShare.Z);
            return;
        }
        if (_strafeLeft > 0)
        {
            // the gattling paused in the middle of a frame: the increment that was decided is finished first
            _gattlingTarget = new Point3(_gattlingTarget.X + _strafeShare.X * (long)_strafeLeft, _gattlingTarget.Y + _strafeShare.Y * (long)_strafeLeft, _gattlingTarget.Z + _strafeShare.Z * (long)_strafeLeft);
            _strafeLeft = 0;
        }
        var increment = FlightFrames.PerFrame(world.Time, data.StrafingIncrement);
        var dx = _positionToShoot.X - _gattlingTarget.X;
        var dy = _positionToShoot.Y - _gattlingTarget.Y;
        var dz = _positionToShoot.Z - _gattlingTarget.Z;
        var distance = AirWeaponModules.Length(dx, dy, dz);
        if (distance < increment)
        {
            _gattlingTarget = _positionToShoot;
            _okToFireHowitzerCounter++;
            return;
        }
        _okToFireHowitzerCounter = 0;
        var scale = increment / distance;
        var step = new Point3(dx * scale, dy * scale, dz * scale);
        if (ticks > 1)
        {
            // the first tick takes what the division leaves, so the shares add up to the increment
            _strafeLeft = checked((int)(ticks - TickOrder.Next));
            _strafeShare = new Point3(Fixed.FromRaw(step.X.Raw / ticks), Fixed.FromRaw(step.Y.Raw / ticks), Fixed.FromRaw(step.Z.Raw / ticks));
            step = new Point3(step.X - _strafeShare.X * (long)_strafeLeft, step.Y - _strafeShare.Y * (long)_strafeLeft, step.Z - _strafeShare.Z * (long)_strafeLeft);
        }
        _gattlingTarget = new Point3(_gattlingTarget.X + step.X, _gattlingTarget.Y + step.Y, _gattlingTarget.Z + step.Z);
    }

    // The rest of the aim point's increment of the running logic frame of the original: ticks left and the share per tick (nothing at 30 Hz).
    private int _strafeLeft;
    private Point3 _strafeShare;

    /// <summary>The nearest valid enemy within <paramref name="radius"/> (the bounding sphere counts) of the point.</summary>
    private Entity? FindTarget(IWorldServices world, Fixed x, Fixed y, Fixed radius)
    {
        Entity? best = null;
        var bestDistance = Fixed.MaxValue;
        foreach (var candidate in AirWeaponModules.Snapshot(world))
        {
            if (candidate.EffectivelyDead || candidate.ContainedBy.IsValid || candidate.Body is null) continue;
            if (world.RelationshipOf(Entity.Owner, candidate.Owner) != Relationship.Enemies) continue;
            if (candidate.HasStatus(ObjectStatusBits.Index("STEALTHED")) && !candidate.HasStatus(ObjectStatusBits.Index("DETECTED"))) continue;
            if (AirWeaponModules.IsAirborne(world, candidate)) continue; // the weapons are AntiGround
            var dx = candidate.X - x;
            var dy = candidate.Y - y;
            var distance = Fixed.Sqrt(dx * dx + dy * dy);
            var reach = Fixed.Max(Fixed.Zero, distance - candidate.Template.BoundingCircleRadius);
            if (reach > radius) continue;
            // isFairDistanceFromShip: the ship does not shoot at what it flies over.
            if (AirModules.Distance2D(Entity.X, Entity.Y, candidate.X, candidate.Y) <= data.GunshipOrbitRadius * FairDistanceShare) continue;
            if (reach >= bestDistance) continue;
            bestDistance = reach;
            best = candidate;
        }
        return best;
    }

    /// <summary>One shot of the gattling's current weapon when it is ready (<c>Object::fireCurrentWeapon</c>: the slot's own clip, ready tick and last-shot bookkeeping).</summary>
    private static void FireGattling(IWorldServices world, Entity gattling, Entity? victim, Fixed x, Fixed y, Fixed z) => world.FireCurrentWeaponAtPosition(gattling, x, y, z, victim);

    /// <summary>Straight ahead along the heading until the ship has left the map, then it is deleted (without a terrain: once it is far from the target).</summary>
    private (Fixed X, Fixed Y, Fixed Turn) Depart(IWorldServices world)
    {
        var step = Fly(world, Transit, Entity.X + FixedTrig.Cos(Entity.Angle) * Fixed.FromInt(99999), Entity.Y + FixedTrig.Sin(Entity.Angle) * Fixed.FromInt(99999));
        var gone = world.Terrain is not null
            ? FlightModules.IsOffMap(world, Entity.X, Entity.Y)
            : AirModules.Distance2D(Entity.X, Entity.Y, _initialTarget.X, _initialTarget.Y) > data.GunshipOrbitRadius * 4;
        if (!gone) return step;
        world.RequestDestroy(Entity.Id);
        _status = GunshipStatus.Idle;
        return step;
    }

    /// <summary><c>cleanUp</c>: the gattling is destroyed.</summary>
    private void CleanUp(IWorldServices world)
    {
        if (_gattling.IsValid && world.FindEntity(_gattling) is { } gattling) world.RequestDestroy(gattling.Id);
        _gattling = EntityId.Invalid;
        _gattlingPlaced = false;
    }

    public override void OnRemove(IWorldServices world) => CleanUp(world);

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("power", _power);
        writer.Int("status", (int)_status);
        DumbProjectileBehavior.WritePoint(writer, "initial", _initialTarget);
        DumbProjectileBehavior.WritePoint(writer, "override", _overrideTarget);
        DumbProjectileBehavior.WritePoint(writer, "satellite", _satellite);
        DumbProjectileBehavior.WritePoint(writer, "gattlingTarget", _gattlingTarget);
        DumbProjectileBehavior.WritePoint(writer, "shootAt", _positionToShoot);
        writer.Int("howitzerCounter", _okToFireHowitzerCounter);
        writer.Int("orbitEscape", _orbitEscapeTick);
        writer.UInt("gattling", _gattling.Value);
        writer.Bool("gattlingPlaced", _gattlingPlaced);
        writer.UInt("victim", _currentVictim.Value);
        _flight.Describe(writer, "flightStep");
        if (_strafeLeft > 0)
        {
            writer.Int("strafeLeft", _strafeLeft);
            DumbProjectileBehavior.WritePoint(writer, "strafeShare", _strafeShare);
        }
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _power = checked((int)reader.Int("power"));
        var status = reader.Int("status");
        if (status < 0 || status > (int)GunshipStatus.Idle) throw new InvalidDataException("Gunship status out of range.");
        _status = (GunshipStatus)status;
        _initialTarget = DumbProjectileBehavior.ReadPoint(reader, "initial");
        _overrideTarget = DumbProjectileBehavior.ReadPoint(reader, "override");
        _satellite = DumbProjectileBehavior.ReadPoint(reader, "satellite");
        _gattlingTarget = DumbProjectileBehavior.ReadPoint(reader, "gattlingTarget");
        _positionToShoot = DumbProjectileBehavior.ReadPoint(reader, "shootAt");
        _okToFireHowitzerCounter = reader.Int("howitzerCounter");
        _orbitEscapeTick = reader.Int("orbitEscape");
        _gattling = new EntityId(checked((uint)reader.UInt("gattling")));
        _gattlingPlaced = reader.Bool("gattlingPlaced");
        _currentVictim = new EntityId(checked((uint)reader.UInt("victim")));
        _flight.Load(reader, "flightStep");
        _strafeLeft = reader.Has("strafeLeft") ? checked((int)reader.Int("strafeLeft")) : 0;
        _strafeShare = _strafeLeft > 0 ? DumbProjectileBehavior.ReadPoint(reader, "strafeShare") : default;
    }
}

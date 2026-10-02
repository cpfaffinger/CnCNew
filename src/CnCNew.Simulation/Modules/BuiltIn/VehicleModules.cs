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
/// The vehicle modules the campaign's GLA waves need (<c>docs/SCRIPT-ENGINE.md</c> §9, Phase 13): the two-lives body of the Battle Bus (<c>UndeadBody</c>) with its
/// throw-and-land first death (<c>BattleBusSlowDeathBehavior</c>), and two modules that only drive model animation (<c>SubObjectsUpgrade</c>, <c>AnimationSteeringUpdate</c>).
/// </summary>
internal static class VehicleModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<UndeadBodyData>("UndeadBody", ModuleOwnerKind.Object, (configuration, _) => UndeadBodyData.Parse(configuration), data => new UndeadBody(data)),
        new ModuleBinding<BattleBusData>("BattleBusSlowDeathBehavior", ModuleOwnerKind.Object, (configuration, _) => BattleBusData.Parse(configuration), data => new BattleBusSlowDeathBehavior(data)),
        new ModuleBinding<ModelConditionUpgradeData>("SubObjectsUpgrade", ModuleOwnerKind.Object, ModelConditionUpgradeData.Parse, data => new SubObjectsUpgrade(data)),
        new ModuleBinding<NoFieldsData>("AnimationSteeringUpdate", ModuleOwnerKind.Object, (_, _) => NoFieldsData.Instance, _ => new AnimationSteeringUpdate()),
    ];
}

// ================================================================================================================ UndeadBody

/// <summary><c>UndeadBodyModuleData</c> (<c>UndeadBody.cpp</c>): an <c>ActiveBody</c> plus the maximum health of the second life.</summary>
public sealed record UndeadBodyData(BodyData Body, Fixed SecondLifeMaxHealth) : ModuleData
{
    public static UndeadBodyData Parse(ModuleConfiguration configuration) => new(BodyData.Parse(configuration), ModuleConfigReader.Real(configuration, "SecondLifeMaxHealth", Fixed.One));
}

/// <summary>
/// <c>UndeadBody.cpp</c>: the body of an object that has two lives. While on its first life, health-damaging damage that would kill it (unresistable damage never counts) is cut to leave one hit
/// point, is taken, and then starts the second life: the maximum health becomes <c>SecondLifeMaxHealth</c> and is fully restored, and the slow-death modules of the object that apply to the
/// damage draw lots (their <c>ProbabilityModifier</c>) for which of them <em>begins</em> — a module that cannot tell this from a real death by its own flag (the bus's) plays its special first
/// death. Not modeled: the second-life armor set (<c>ARMORSET_SECOND_LIFE</c>; the simulation has no armor-set conditions beyond the player upgrade and the veterancy levels).
/// </summary>
public sealed class UndeadBody(UndeadBodyData data) : ActiveBody(data.Body, "UndeadBody")
{
    private bool _secondLife;

    /// <summary>The first life is over (the body started its second life).</summary>
    public bool IsSecondLife => _secondLife;

    public override DamageOutcome AttemptDamage(IWorldServices world, in DamageInfo info)
    {
        var startSecondLife = false;
        var applied = info;
        if (info.DamageType != DamageTypes.Unresistable && !_secondLife && info.Amount >= Health && IsHealthDamaging(info.DamageType) && !Entity.EffectivelyDead)
        {
            applied = info with { Amount = Fixed.Min(info.Amount, Health - Fixed.One) };
            startSecondLife = true;
        }
        var outcome = base.AttemptDamage(world, applied);
        if (startSecondLife) StartSecondLife(world, applied);
        return outcome;
    }

    /// <summary><c>IsHealthDamagingDamage</c> (<c>Damage.h</c>): everything except status effects, subdual, the pilot kill and the garrison kill.</summary>
    private static bool IsHealthDamaging(int type) => type != DamageTypes.Status && !DamageTypes.IsSubdual(type) && type != DamageTypes.KillPilot && type != DamageTypes.KillGarrisoned;

    private void StartSecondLife(IWorldServices world, in DamageInfo info)
    {
        _secondLife = true;
        AdjustMaxHealth(world, data.SecondLifeMaxHealth - MaxHealth, "FULLY_HEAL");
        var damage = info;
        var candidates = Entity.Modules.OfType<ISlowDeathCandidate>().Where(candidate => candidate.IsApplicable(damage)).ToArray();
        var total = candidates.Sum(candidate => candidate.ProbabilityWeight);
        if (total <= 0) return;
        var roll = world.LogicRandom.Range(1, total);
        foreach (var candidate in candidates)
        {
            roll -= candidate.ProbabilityWeight;
            if (roll > 0) continue;
            candidate.BeginSlowDeath(world);
            return;
        }
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        if (_secondLife) writer.Bool("secondLife", true);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        _secondLife = reader.Has("secondLife") && reader.Bool("secondLife");
    }
}

// ================================================================================================================ BattleBusSlowDeathBehavior

/// <summary>
/// <c>BattleBusSlowDeathBehaviorModuleData</c> (<c>BattleBusSlowDeathBehavior.cpp</c>): the shared slow-death fields plus the throw of the first death and its effects.
/// <c>ThrowForce</c> is the shock the bus receives (divided by the mass of its <c>PhysicsBehavior</c>, one tick's acceleration).
/// </summary>
public sealed record BattleBusData(SlowDeathData Base, string FxStartUndeath, string OclStartUndeath, string FxHitGround, string OclHitGround, Fixed ThrowForce, Fixed PercentDamageToPassengers, long EmptyHulkDelayTicks) : ModuleData
{
    public static BattleBusData Parse(ModuleConfiguration configuration) => new(
        SlowDeathData.Parse(configuration),
        ModuleConfigReader.Text(configuration, "FXStartUndeath"), ModuleConfigReader.Text(configuration, "OCLStartUndeath"),
        ModuleConfigReader.Text(configuration, "FXHitGround"), ModuleConfigReader.Text(configuration, "OCLHitGround"),
        ModuleConfigReader.Real(configuration, "ThrowForce", Fixed.One), ModuleConfigReader.Real(configuration, "PercentDamageToPassengers", Fixed.Zero),
        ModuleConfigReader.Ticks(configuration, "EmptyHulkDestructionDelay", 0));
}

/// <summary>
/// <c>BattleBusSlowDeathBehavior.cpp</c>: a <c>SlowDeathBehavior</c> that is picked twice. Started by the second life of the bus's <see cref="UndeadBody"/> (not by a death) it
/// plays <c>FXStartUndeath</c>/<c>OCLStartUndeath</c>, halts the bus, throws it into the air (an upward shock of <c>ThrowForce / mass</c>) and hurts every passenger by
/// <c>PercentDamageToPassengers</c> of its maximum health; once it touched the ground again (checked after ten ticks, so not on the way up) it plays <c>FXHitGround</c>/<c>OCLHitGround</c>
/// and stays where it fell — with <c>EmptyHulkDestructionDelay</c> it is killed (penalty damage, the one death type the module ignores) when nobody has been inside for that long. Started by a real
/// death (<c>onDie</c> sets its flag first) it is an ordinary slow death with its destruction delay. The throw is kinematic (the reference pushes the body through
/// <c>PhysicsBehavior</c>: no random spin, no 2D drift), and the reference's <c>DISABLED_HELD</c> on the landed hulk has no counterpart (the bus is halted, nothing more).
/// </summary>
public sealed class BattleBusSlowDeathBehavior(BattleBusData data) : DieModuleBase(data.Base.Mux), IUpdateModule, ISlowDeathCandidate
{
    private const long GroundCheckDelay = 10;
    private const long EmptyHulkCheckDelay = 15;

    private long _nextWake = -1;
    private bool _realDeath;
    private bool _inFirstDeath;
    private bool _activated;
    private long _groundCheckTick;
    private long _penaltyDeathTick;
    private long _destructionTick;
    private Fixed _verticalSpeed;

    private static readonly int PenaltyDamageType = DamageTypes.Penalty;
    private static readonly int Extra4DeathType = NameTables.DeathType.TryIndexOf("EXTRA_4", out var index) ? index : DeathTypes.Normal;

    public override string ModuleName => "BattleBusSlowDeathBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public bool IsApplicable(in DamageInfo info) => Mux.IsApplicable(Entity, info);
    public int ProbabilityWeight => Math.Max(data.Base.ProbabilityModifier, 1);

    public override void OnSpawn(IWorldServices world) => _nextWake = data.EmptyHulkDelayTicks > 0 ? world.Tick + SimulationTiming.SleepNone : SimulationTiming.SleepForever;

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        _realDeath = true;
        _inFirstDeath = false;
        SlowDeathLottery.Run(world, Entity, info, IsApplicable(info));
    }

    public void BeginSlowDeath(IWorldServices world)
    {
        if (_realDeath)
        {
            // SlowDeathBehavior::beginSlowDeath: both draws, then the destruction timer.
            _ = data.Base.SinkDelayTicks + world.LogicRandom.Range(0, checked((int)data.Base.SinkDelayVarianceTicks));
            var destruction = data.Base.DestructionDelayTicks + world.LogicRandom.Range(0, checked((int)data.Base.DestructionDelayVarianceTicks));
            _activated = true;
            _destructionTick = world.Tick + Math.Max(destruction, SimulationTiming.SleepNone);
            world.WakeAt(Entity, this, _destructionTick);
            return;
        }
        _inFirstDeath = true;
        _groundCheckTick = world.Tick + GroundCheckDelay;
        Effects(world, data.FxStartUndeath, data.OclStartUndeath);
        Entity.HasMoveTarget = false;
        Entity.Speed = Fixed.Zero;
        var mass = Entity.ModuleArray.OfType<PhysicsBehavior>().FirstOrDefault()?.Data.Mass ?? Fixed.One;
        _verticalSpeed = mass > Fixed.Zero ? data.ThrowForce / mass : Fixed.Zero;
        if (data.PercentDamageToPassengers > Fixed.Zero && Entity.Container is { } contain)
            foreach (var riderId in contain.Contained.ToArray())
                if (world.FindEntity(riderId) is { Body: { } body } rider)
                    world.Damage(rider.Id, new DamageInfo(Entity.Id, body.MaxHealth * data.PercentDamageToPassengers, DamageTypes.Unresistable, DeathTypes.Normal, false));
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public long Update(IWorldServices world)
    {
        if (_inFirstDeath)
        {
            Entity.Z += _verticalSpeed;
            _verticalSpeed += world.Templates.GravityPerTickSquared;
            var ground = FlightModules.GroundOrDefault(world, Entity.X, Entity.Y, Fixed.Zero);
            if (Entity.Z < ground) Entity.Z = ground;
            world.MarkDirty(Entity);
            if (world.Tick > _groundCheckTick && Entity.Z <= ground)
            {
                _inFirstDeath = false;
                Effects(world, data.FxHitGround, data.OclHitGround);
                Entity.HasMoveTarget = false;
                Entity.Speed = Fixed.Zero;
                return data.EmptyHulkDelayTicks == 0 ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;
            }
            return SimulationTiming.SleepNone;
        }
        if (_realDeath)
        {
            if (_activated && world.Tick >= _destructionTick) world.RequestDestroy(Entity.Id);
            return SimulationTiming.SleepForever;
        }
        // Neither death is running: wake now and then to see whether the hulk is empty.
        if (Entity.Container is not { } passengers) return SimulationTiming.SleepForever;
        if (data.EmptyHulkDelayTicks == 0) return SimulationTiming.SleepForever;
        if (_penaltyDeathTick != 0)
        {
            if (passengers.ContainCount > 0) _penaltyDeathTick = 0;
            else if (world.Tick > _penaltyDeathTick)
            {
                world.Damage(Entity.Id, new DamageInfo(EntityId.Invalid, Fixed.One, PenaltyDamageType, Extra4DeathType, true));
                return SimulationTiming.SleepForever;
            }
        }
        else if (passengers.ContainCount == 0) _penaltyDeathTick = world.Tick + data.EmptyHulkDelayTicks;
        return EmptyHulkCheckDelay;
    }

    private void Effects(IWorldServices world, string fx, string ocl)
    {
        if (fx.Length > 0) world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, fx, 0, Entity.X, Entity.Y, Entity.Z);
        if (ocl.Length > 0) world.CreateFromList(Entity, ocl, EntityId.Invalid, false);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("realDeath", _realDeath);
        writer.Bool("firstDeath", _inFirstDeath);
        writer.Bool("activated", _activated);
        writer.Int("groundCheckTick", _groundCheckTick);
        writer.Int("penaltyDeathTick", _penaltyDeathTick);
        writer.Int("destructionTick", _destructionTick);
        writer.Fixed("verticalSpeed", _verticalSpeed);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _realDeath = reader.Bool("realDeath");
        _inFirstDeath = reader.Bool("firstDeath");
        _activated = reader.Bool("activated");
        _groundCheckTick = reader.Int("groundCheckTick");
        _penaltyDeathTick = reader.Int("penaltyDeathTick");
        _destructionTick = reader.Int("destructionTick");
        _verticalSpeed = reader.Fixed("verticalSpeed");
    }
}

// ================================================================================================================ presentation modules

/// <summary><c>SubObjectsUpgrade.cpp</c>: shows and hides named sub-objects of the model when its upgrade completes (<c>ShowSubObjects</c>, <c>HideSubObjects</c>) — pure presentation, the simulation only keeps the upgrade state.</summary>
public sealed class SubObjectsUpgrade(ModelConditionUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "SubObjectsUpgrade";
    protected override void Activate(IWorldServices world) { }
}

/// <summary><c>AnimationSteeringUpdate.cpp</c>: switches the model between the turn animations (<c>CENTER_TO_LEFT</c>, …) from the physics' turning state — presentation only, no state and no effect here.</summary>
public sealed class AnimationSteeringUpdate : SimModule
{
    public override string ModuleName => "AnimationSteeringUpdate";
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

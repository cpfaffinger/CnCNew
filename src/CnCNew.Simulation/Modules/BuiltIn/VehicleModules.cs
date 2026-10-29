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
        new ModuleBinding<AnimationSteeringData>("AnimationSteeringUpdate", ModuleOwnerKind.Object, (configuration, _) => AnimationSteeringData.Parse(configuration), data => new AnimationSteeringUpdate(data)),
    ];
}

// ================================================================================================================ UndeadBody

/// <summary><c>UndeadBodyModuleData</c> (<c>UndeadBody.cpp</c>): an <c>ActiveBody</c> plus the maximum health of the second life.</summary>
public sealed record UndeadBodyData(BodyData Body, Fixed SecondLifeMaxHealth) : ModuleData
{
    public static UndeadBodyData Parse(RatedModuleConfiguration configuration) => new(BodyData.Parse(configuration), ModuleConfigReader.Real(configuration, "SecondLifeMaxHealth", Fixed.One));
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
        // The rule fix undead-lethal-by-armour: the blow is lethal by estimateDamage (after the armour), not by its raw amount.
        var lethal = world.Templates.Rules.Has(Rules.GameFix.UndeadLethalByArmour) ? world.AdjustDamageByArmor(Entity, info.DamageType, info.Amount) : info.Amount;
        if (info.DamageType != DamageTypes.Unresistable && !_secondLife && lethal >= Health && IsHealthDamaging(info.DamageType) && !Entity.EffectivelyDead)
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
    public static BattleBusData Parse(RatedModuleConfiguration configuration) => new(
        SlowDeathData.Parse(configuration),
        ModuleConfigReader.Text(configuration, "FXStartUndeath"), ModuleConfigReader.Text(configuration, "OCLStartUndeath"),
        ModuleConfigReader.Text(configuration, "FXHitGround"), ModuleConfigReader.Text(configuration, "OCLHitGround"),
        configuration.Time.PerOriginalFrameToPerTick(ModuleConfigReader.Real(configuration, "ThrowForce", Fixed.One)), ModuleConfigReader.Real(configuration, "PercentDamageToPassengers", Fixed.Zero),
        ModuleConfigReader.Ticks(configuration, "EmptyHulkDestructionDelay", 0));
}

/// <summary>
/// <c>BattleBusSlowDeathBehavior.cpp</c>: a <c>SlowDeathBehavior</c> that is picked twice. Started by the second life of the bus's <see cref="UndeadBody"/> (not by a death) it
/// plays <c>FXStartUndeath</c>/<c>OCLStartUndeath</c>, halts the bus, throws it into the air (an upward shock of <c>ThrowForce / mass</c>) and hurts every passenger by
/// <c>PercentDamageToPassengers</c> of its maximum health; once it touched the ground again (checked after ten ticks, so not on the way up) it plays <c>FXHitGround</c>/<c>OCLHitGround</c>
/// and stays where it fell — with <c>EmptyHulkDestructionDelay</c> it is killed (penalty damage, the one death type the module ignores) when nobody has been inside for that long. Started by a real
/// death (<c>onDie</c> sets its flag first) it is an ordinary slow death with its destruction delay. The throw is kinematic (the reference pushes the body through
/// <c>PhysicsBehavior</c>: no random spin, no 2D drift). The landed hulk goes idle and is held (<c>aiIdle</c>, <c>setDisabled(DISABLED_HELD)</c>; F-53): it acquires nothing and its AI
/// does not move it.
/// </summary>
public sealed class BattleBusSlowDeathBehavior(BattleBusData data) : DieModuleBase(data.Base.Mux), IUpdateModule, ISlowDeathCandidate
{
    /// <summary>The base behaviour's midpoint and phases (F-37).</summary>
    private readonly SlowDeathClock _clock = new();

    /// <summary><c>getDisabledTypesToProcess</c> (a SlowDeathBehavior).</summary>
    public DisabledMask DisabledTypesToProcess => DisabledMask.All;

    /// <summary><c>GROUND_CHECK_DELAY = 10</c> and <c>EMPTY_HULK_CHECK_DELAY = 15</c> (BattleBusSlowDeathBehavior.cpp), in logic frames of the original.</summary>
    private const long GroundCheckDelayFrames = 10;
    private const long EmptyHulkCheckDelayFrames = 15;

    private long _nextWake = -1;
    private bool _realDeath;
    private bool _inFirstDeath;
    private bool _activated;
    private long _groundCheckTick;
    private long _penaltyDeathTick;
    private long _destructionTick;
    private Fixed _verticalSpeed;
    // The throw is one step of the original per logic frame of the original (height += speed, then speed += gravity), in its units: the vertical speed is
    // a distance per FRAME at every rate, the height covers the frame's step tick by tick, gravity and the landing conclude the frame on its last tick.
    // At rest (unwritten) between two frames, which is always at 30 Hz.
    private FramePhase _framePhase;

    private static readonly int PenaltyDamageType = DamageTypes.Penalty;
    private static readonly int Extra4DeathType = NameTables.DeathType.TryIndexOf("EXTRA_4", out var index) ? index : DeathTypes.Normal;

    public override string ModuleName => "BattleBusSlowDeathBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public bool IsApplicable(in DamageInfo info) => Mux.IsApplicable(Entity, info);
    public int ProbabilityWeight => Math.Max(data.Base.ProbabilityModifier, 1);

    /// <summary>
    /// The bus has hit the ground after its throw: <c>update</c> sets <c>MODELCONDITION_SECOND_LIFE</c> at that moment and never clears it. No flag is kept for it; it is read off the state: the first
    /// death has begun and ended (<c>_groundCheckTick</c> is set, <c>_inFirstDeath</c> is not) and the bus lies on the ground (a real death in mid-air ends the update, the bus stays up there).
    /// </summary>
    public bool HasLanded(IWorldServices world) => _groundCheckTick != 0 && !_inFirstDeath && !world.IsSignificantlyAboveTerrain(Entity);

    // The first look at the hulk comes one logic frame of the original after the bus appeared (the original's first update), the later ones every 15 frames
    // from there, at the same game times at every rate. (A death that falls on such a tick is taken up by that very update, at 30 Hz and above.)
    public override void OnSpawn(IWorldServices world) => _nextWake = data.EmptyHulkDelayTicks > 0 ? world.Tick + world.Time.TicksPerOriginalFrame : SimulationTiming.SleepForever;

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
            // SlowDeathBehavior::beginSlowDeath: its three draws, the initial phase (F-37), then the destruction timer.
            _destructionTick = world.Tick + _clock.Begin(world, Entity, data.Base);
            _activated = true;
            world.WakeAt(Entity, this, _clock.NextDue(_destructionTick));
            return;
        }
        _inFirstDeath = true;
        _groundCheckTick = world.Tick + world.Time.OriginalFrames(GroundCheckDelayFrames);
        Effects(world, data.FxStartUndeath, data.OclStartUndeath);
        Entity.HasMoveTarget = false;
        Entity.Speed = Fixed.Zero;
        var mass = Entity.ModuleArray.OfType<PhysicsBehavior>().FirstOrDefault()?.Data.Mass ?? Fixed.One;
        _verticalSpeed = mass > Fixed.Zero ? world.Time.PerTickAsPerOriginalFrame(data.ThrowForce) / mass : Fixed.Zero;
        _framePhase.Reset();
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
            Entity.Z += world.Time.FrameSlice(_verticalSpeed, _framePhase.Next(world.Time));
            var frameEnds = _framePhase.AtBoundary;
            if (frameEnds) _verticalSpeed += world.Time.PerTickSquaredAsPerOriginalFrameSquared(world.Templates.GravityPerTickSquared);
            var ground = FlightModules.GroundOrDefault(world, Entity.X, Entity.Y, Fixed.Zero);
            if (Entity.Z < ground) Entity.Z = ground;
            world.MarkDirty(Entity);
            if (frameEnds && world.Tick > _groundCheckTick && Entity.Z <= ground)
            {
                _inFirstDeath = false;
                Effects(world, data.FxHitGround, data.OclHitGround);
                // "And stop us forever": aiIdle, the physics scrubbed, DISABLED_HELD.
                world.EndAttack(Entity);
                Entity.HasMoveTarget = false;
                Entity.Speed = Fixed.Zero;
                Entity.ScriptDisableMask |= Entity.ScriptHeld;
                return data.EmptyHulkDelayTicks == 0 ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;
            }
            return SimulationTiming.SleepNone;
        }
        // "If a real death, we aren't allowed to do anything": SlowDeathBehavior::update.
        if (_realDeath)
        {
            if (!_activated) return SimulationTiming.SleepForever;
            if (world.Tick < _destructionTick)
            {
                _clock.Step(world, Entity, data.Base);
                return _clock.NextDue(_destructionTick) - world.Tick;
            }
            _clock.Final(world, Entity, data.Base);
            world.RequestDestroy(Entity.Id);
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
        return world.Time.OriginalFrames(EmptyHulkCheckDelayFrames);
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
        if (_activated) _clock.Describe(writer);
        writer.Int("groundCheckTick", _groundCheckTick);
        writer.Int("penaltyDeathTick", _penaltyDeathTick);
        writer.Int("destructionTick", _destructionTick);
        writer.Fixed("verticalSpeed", _verticalSpeed);
        _framePhase.Describe(writer, "framePhase");
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _realDeath = reader.Bool("realDeath");
        _inFirstDeath = reader.Bool("firstDeath");
        _activated = reader.Bool("activated");
        if (_activated) _clock.Load(reader);
        _groundCheckTick = reader.Int("groundCheckTick");
        _penaltyDeathTick = reader.Int("penaltyDeathTick");
        _destructionTick = reader.Int("destructionTick");
        _verticalSpeed = reader.Fixed("verticalSpeed");
        _framePhase.Load(reader, "framePhase");
    }
}

// ================================================================================================================ presentation modules

/// <summary><c>SubObjectsUpgrade.cpp</c>: shows and hides named sub-objects of the model when its upgrade completes (<c>ShowSubObjects</c>, <c>HideSubObjects</c>) — pure presentation, the simulation only keeps the upgrade state.</summary>
public sealed class SubObjectsUpgrade(ModelConditionUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "SubObjectsUpgrade";
    protected override void Activate(IWorldServices world) { }
}

/// <summary><c>AnimationSteeringUpdateModuleData</c>: <c>MinTransitionTime</c> (the reference's <c>m_transitionFrames</c>, a duration in whole ticks, default 0) is how long each turn animation state is held.</summary>
public sealed record AnimationSteeringData(long MinTransitionTicks) : ModuleData
{
    public static AnimationSteeringData Parse(RatedModuleConfiguration configuration) => new(ModuleConfigReader.Ticks(configuration, "MinTransitionTime", 0));
}

/// <summary>
/// <c>AnimationSteeringUpdate.cpp</c>: switches the model between the turn animations (<c>CENTER_TO_LEFT</c>, …) from the physics' turning state — presentation only, no state and no effect here.
/// The presentation (<c>UnitConditionTracker</c>) ports the state machine and reads <see cref="MinTransitionTicks"/>.
/// </summary>
public sealed class AnimationSteeringUpdate(AnimationSteeringData data) : SimModule
{
    public override string ModuleName => "AnimationSteeringUpdate";

    /// <summary><c>MinTransitionTime</c> in ticks.</summary>
    public long MinTransitionTicks => data.MinTransitionTicks;
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

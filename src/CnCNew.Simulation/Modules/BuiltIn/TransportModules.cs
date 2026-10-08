using System.Collections.Immutable;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// Transports, tunnels and the helicopter death (phase 8, E1): <c>TransportAIUpdate</c>, <c>ChinookAIUpdate</c> and
/// <c>HelicopterSlowDeathBehavior</c>; the two contain modules <c>TunnelContain</c> and <c>HelixContain</c> are bound with the other contain modules.
/// Rules and deviations: docs/GAME-RULES.md §6j.
/// </summary>
internal static class TransportModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<AIUpdateData>("TransportAIUpdate", ModuleOwnerKind.Object, (configuration, _) => AIUpdateData.Parse(configuration), data => new TransportAIUpdate(data)),
        new ModuleBinding<ChinookData>("ChinookAIUpdate", ModuleOwnerKind.Object, (configuration, _) => ChinookData.Parse(configuration), data => new ChinookAIUpdate(data)),
        new ModuleBinding<HelicopterSlowDeathData>("HelicopterSlowDeathBehavior", ModuleOwnerKind.Object, (configuration, _) => HelicopterSlowDeathData.Parse(configuration), data => new HelicopterSlowDeathBehavior(data)),
    ];
}

// ================================================================================================== TransportAIUpdate

/// <summary>
/// <c>TransportAIUpdate.cpp</c>: an <see cref="AIUpdateInterface"/> whose attack orders are passed on to passengers allowed to fire
/// (<c>privateAttackObject</c> and siblings). Firing from inside a container is not modeled anywhere yet, so what remains is the movement AI
/// plus the load/unload orders every AI shares here (<see cref="AIUpdateInterface.OrderEnter"/> for passengers, <see cref="AIUpdateInterface.OrderEvacuate"/>
/// for the transport); <c>getAiFreeToExit</c> is always "free to exit", as in the reference.
/// </summary>
public sealed class TransportAIUpdate(AIUpdateData data) : AIUpdateInterface(data)
{
    public override string ModuleName => "TransportAIUpdate";
}

// ==================================================================================================== ChinookAIUpdate

/// <summary><c>ChinookAIUpdateModuleData</c>: the supply-truck fields plus the rappel and rope settings of the combat drop (typed, the drop is not simulated).</summary>
public sealed record ChinookData(SupplyCarrierData Carrier) : ModuleData
{
    public static ChinookData Parse(RatedModuleConfiguration configuration) => new(SupplyCarrierData.Parse(configuration));
}

/// <summary>
/// <c>ChinookAIUpdate.cpp</c>: a <c>SupplyTruckAIUpdate</c> that flies. The supply brain is the supply truck's (<see cref="SupplyMachine"/>), with the
/// Chinook's own dock delays and the <c>UpgradedSupplyBoost</c> once the player owns <c>Upgrade_AmericaSupplyLines</c>
/// (<c>ChinookAIUpdate::getUpgradedSupplyBoost</c>). Flight: the Chinook keeps <c>PreferredHeight</c> above the ground while it flies and changes
/// height by one locomotor speed per logic frame of the original (the reference's <c>ChinookTakeoffOrLandingState</c> lets the locomotor lift it); passengers only leave a
/// landed Chinook (<c>getAiFreeToExit</c>), so an evacuation order lands it first and it takes off again when it moves on. The combat drop
/// (rappelling), the airfield healing and the head-off-map state are not simulated.
/// </summary>
internal sealed class ChinookAIUpdate(ChinookData data) : SupplyCarrierAI(data.Carrier)
{
    private bool _landing;
    /// <summary>The change of height of the running logic frame of the original, flown in shares per tick; empty at 30 Hz.</summary>
    private FlightFrameStep _climb;

    public override string ModuleName => "ChinookAIUpdate";
    public override long UpgradedSupplyBoost(IWorldServices world) => BoostIfUpgraded(world, "Upgrade_AmericaSupplyLines");

    /// <summary>"Flying, even if we start on the ground" (<c>m_flightStatus = CHINOOK_FLYING</c>): a fresh Chinook climbs to its height at once.</summary>
    public override void OnSpawn(IWorldServices world)
    {
        base.OnSpawn(world);
        NextWake = world.Tick + SimulationTiming.SleepNone;
    }

    /// <summary>The Chinook stands on the ground.</summary>
    public bool Landed(IWorldServices world) => Entity.Z <= world.GroundHeightAt(Entity.X, Entity.Y);

    protected override bool ReadyToUnload(IWorldServices world)
    {
        _landing = true;
        return Landed(world);
    }

    public override long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        var sleep = base.Update(world);
        if (!EvacuatePending) _landing = false;
        // The change of height is the 30 Hz step (one locomotor speed per logic frame of the original, at most the rest of the way), decided once per frame and flown in shares
        // per tick: the Chinook is on the ground, and lets its passengers out, in the same frame at every rate.
        if (_climb.Continue(Entity))
        {
            world.MarkDirty(Entity);
            return SimulationTiming.SleepNone;
        }
        var locomotor = Entity.Template.Locomotor;
        var ground = world.GroundHeightAt(Entity.X, Entity.Y);
        var target = _landing ? ground : ground + (locomotor?.PreferredHeight ?? Fixed.Zero);
        var climb = FlightFrames.PerFrame(world.Time, locomotor?.Speed ?? Fixed.Zero);
        if (Entity.Z != target && climb > Fixed.Zero)
        {
            var from = Entity.Z;
            Entity.Z = Entity.Z < target ? Fixed.Min(target, Entity.Z + climb) : Fixed.Max(target, Entity.Z - climb);
            _climb.Begin(world.Time, Entity, Fixed.Zero, Fixed.Zero, Entity.Z - from, Fixed.Zero);
            world.MarkDirty(Entity);
        }
        return Entity.Z != target ? SimulationTiming.SleepNone : sleep;
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        writer.Bool("landing", _landing);
        _climb.Describe(writer, "climbStep");
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        _landing = reader.Bool("landing");
        _climb.Load(reader, "climbStep");
    }
}

// ======================================================================================== HelicopterSlowDeathBehavior

/// <summary>
/// <c>HelicopterSlowDeathBehaviorModuleData</c> in the units of the original's logic frame at every tick rate (radians and world units per frame, unrounded frames): the spiral is integrated in steps of one frame of the original (<see cref="HelicopterSlowDeathBehavior"/>), and with the very numbers of the 30 Hz catalog
/// every sum and comparison of that step comes out the same at 60 and 120 Hz. The particle, bone and sound fields are presentation.
/// </summary>
public sealed record HelicopterSlowDeathData(SlowDeathData Base, Fixed SpiralOrbitTurnRate, Fixed SpiralOrbitForwardSpeed, Fixed SpiralOrbitForwardSpeedDamping,
    Fixed MinSelfSpin, Fixed MaxSelfSpin, Fixed SelfSpinUpdateDelay, Fixed SelfSpinUpdateAmount, Fixed FallHowFast, Fixed MinBladeFlyOffDelay, Fixed MaxBladeFlyOffDelay,
    string OclEjectPilot, string FxBlade, string OclBlade, string FxHitGround, string OclHitGround, string FxFinalBlowUp, string OclFinalBlowUp,
    Fixed DelayFromGroundToFinalDeath, string FinalRubbleObject) : ModuleData
{
    public static HelicopterSlowDeathData Parse(RatedModuleConfiguration configuration)
    {
        Fixed Real(string name, Fixed fallback) => ModuleConfigReader.Real(configuration, name, fallback);
        Fixed Radians(string name) => SimulationTiming.DegreesToRadians(Real(name, Fixed.Zero).ToDecimal());
        // Per logic frame of the original, whatever the rate of the catalog (at 30 Hz the catalog's own time service).
        var original = SimulationRate.Original.Time;
        var ticksPerSecond = original.TicksPerSecondFixed;
        return new HelicopterSlowDeathData(
            SlowDeathData.Parse(configuration),
            Radians("SpiralOrbitTurnRate") / ticksPerSecond,
            Real("SpiralOrbitForwardSpeed", Fixed.Zero) / ticksPerSecond,
            Real("SpiralOrbitForwardSpeedDamping", Fixed.One),
            Radians("MinSelfSpin") / ticksPerSecond,
            Radians("MaxSelfSpin") / ticksPerSecond,
            FixedTicks(configuration, original, "SelfSpinUpdateDelay"),
            Radians("SelfSpinUpdateAmount"),
            Real("FallHowFast", Fixed.Zero),
            FixedTicks(configuration, original, "MinBladeFlyOffDelay"),
            FixedTicks(configuration, original, "MaxBladeFlyOffDelay"),
            ModuleConfigReader.Text(configuration, "OCLEjectPilot"),
            ModuleConfigReader.Text(configuration, "FXBlade"),
            ModuleConfigReader.Text(configuration, "OCLBlade"),
            ModuleConfigReader.Text(configuration, "FXHitGround"),
            ModuleConfigReader.Text(configuration, "OCLHitGround"),
            ModuleConfigReader.Text(configuration, "FXFinalBlowUp"),
            ModuleConfigReader.Text(configuration, "OCLFinalBlowUp"),
            FixedTicks(configuration, original, "DelayFromGroundToFinalDeath"),
            ModuleConfigReader.Text(configuration, "FinalRubbleObject"));
    }

    /// <summary><c>INI::parseDurationReal</c>: milliseconds to unrounded ticks of <paramref name="time"/> (the original's: unrounded logic frames).</summary>
    private static Fixed FixedTicks(RatedModuleConfiguration configuration, SimulationTime time, string name)
        => ModuleConfigReader.Find(configuration, name)?.Value.Value is Data.Definitions.GameTime duration
            ? time.DurationMillisecondsToFixedTicks(duration.Milliseconds) : Fixed.Zero;
}

/// <summary>
/// <c>HelicopterSlowDeathBehavior</c> (<c>HelicopterSlowDeathUpdate.cpp</c>): a <c>SlowDeathBehavior</c> that takes part in the same sibling lottery and,
/// once chosen, spirals the helicopter down — each tick it turns by its self spin (ping-ponging between <c>MinSelfSpin</c> and <c>MaxSelfSpin</c> every
/// <c>SelfSpinUpdateDelay</c>), moves along its orbit angle at the damped <c>SpiralOrbitForwardSpeed</c> while the orbit angle turns by
/// <c>SpiralOrbitTurnRate</c>, and falls with <c>FallHowFast</c> of the gravity (the locomotor's lift is set to <c>-gravity · (1 - FallHowFast)</c>). The
/// blade flies off after a random <c>Min/MaxBladeFlyOffDelay</c> (one real logic draw: <c>FXBlade</c>, <c>OCLBlade</c>, and <c>OCLEjectPilot</c> for a
/// veteran or better); at one unit above the ground it hits (<c>FXHitGround</c>, <c>OCLHitGround</c>) and <c>DelayFromGroundToFinalDeath</c> later it
/// blows up (<c>FXFinalBlowUp</c>, <c>OCLFinalBlowUp</c>), leaves the <c>FinalRubbleObject</c> and is destroyed. The motion is kinematic: the reference
/// pushes the body with a force through <c>PhysicsBehavior</c> (mass, friction); here the orbit speed moves it directly.
/// </summary>
public sealed class HelicopterSlowDeathBehavior(HelicopterSlowDeathData data) : DieModuleBase(data.Base.Mux), IUpdateModule, ISlowDeathCandidate
{
    private long _nextWake = -1;
    private bool _activated;
    private long _destructionTick;
    private Fixed _forwardAngle;
    private Fixed _forwardSpeed;
    private Fixed _selfSpin;
    private bool _selfSpinTowardsMax;
    private long _lastSelfSpinUpdate;
    private Fixed _bladeFlyOff;
    private long _hitGroundTick;
    /// <summary>The speed of the fall per logic frame of the original, negative.</summary>
    private Fixed _fallSpeed;
    /// <summary>The step of the running frame of the spiral, flown in shares per tick; empty at 30 Hz.</summary>
    private FlightFrameStep _spiral;

    public override string ModuleName => "HelicopterSlowDeathBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public bool IsApplicable(in DamageInfo info) => Mux.IsApplicable(Entity, info);
    public int ProbabilityWeight => Math.Max(data.Base.ProbabilityModifier, 1);

    /// <summary>The wreck has hit the ground and is held there (<c>m_hitGroundFrame</c> set): the reference sets <c>MODELCONDITION_SPECIAL_DAMAGED</c> ("has the blades no on anymore") at that moment and never clears it.</summary>
    public bool HasHitGround => _hitGroundTick != 0;

    public override void OnDie(IWorldServices world, in DamageInfo info) => SlowDeathLottery.Run(world, Entity, info, IsApplicable(info));

    public void BeginSlowDeath(IWorldServices world)
    {
        _activated = true;
        // SlowDeathBehavior::beginSlowDeath: both variance draws happen, the sink animation is presentation.
        _ = data.Base.SinkDelayTicks + world.Time.DrawFrames(world.LogicRandom, 0, data.Base.SinkDelayVarianceTicks);
        var destruction = data.Base.DestructionDelayTicks + world.Time.DrawFrames(world.LogicRandom, 0, data.Base.DestructionDelayVarianceTicks);
        _destructionTick = Math.Min(world.Tick + world.Time.AtLeastOneFrame(destruction), SimulationTiming.SleepForever);
        _bladeFlyOff = world.LogicRandom.Range(data.MinBladeFlyOffDelay, data.MaxBladeFlyOffDelay);
        _forwardAngle = Entity.Angle;
        _forwardSpeed = data.SpiralOrbitForwardSpeed;
        _selfSpin = data.MinSelfSpin;
        _selfSpinTowardsMax = true;
        _lastSelfSpinUpdate = 0;
        _hitGroundTick = 0;
        _fallSpeed = Fixed.Zero;
        _spiral.Cancel();
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public long Update(IWorldServices world)
    {
        if (!_activated) return SimulationTiming.SleepForever;
        if (world.Tick >= _destructionTick)
        {
            world.RequestDestroy(Entity.Id);
            return SimulationTiming.SleepForever;
        }
        if (_hitGroundTick == 0) Spiral(world);
        // The wait on the ground is compared once per logic frame of the original, counted from the impact (the delay is in unrounded frames: compared every tick it would end
        // on a tick between two frames).
        if (_hitGroundTick != 0 && world.Time.IsOriginalFrameBoundary(world.Tick - _hitGroundTick)
            && Fixed.FromInt(world.Time.ToOriginalFrames(world.Tick - _hitGroundTick)) > data.DelayFromGroundToFinalDeath)
        {
            if (data.FxFinalBlowUp.Length > 0) world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.FxFinalBlowUp, 0, Entity.X, Entity.Y, Entity.Z);
            if (data.OclFinalBlowUp.Length > 0) world.CreateFromList(Entity, data.OclFinalBlowUp, EntityId.Invalid, false);
            SpawnRubble(world);
            world.RequestDestroy(Entity.Id);
            return SimulationTiming.SleepForever;
        }
        return SimulationTiming.SleepNone;
    }

    /// <summary>
    /// The spiral down is the 30 Hz step, decided once per logic frame of the original and flown in shares per tick: the self spin of a frame, the orbit's travel and turn of a frame,
    /// the damping of the orbit speed (a factor per frame, applied once per frame) and the fall. Spin, orbit speed and the speed of the fall are values per frame, the data are the
    /// numbers of the 30 Hz catalog (<see cref="HelicopterSlowDeathData"/>): the step is the same arithmetic at every rate. The self spin changes when a frame begins.
    /// </summary>
    private void Spiral(IWorldServices world)
    {
        if (_spiral.Continue(Entity))
        {
            world.MarkDirty(Entity);
            return;
        }
        var spin = _selfSpin;
        Entity.Angle = FixedTrig.Normalize(Entity.Angle + spin);
        if (data.SelfSpinUpdateDelay > Fixed.Zero && Fixed.FromInt(world.Time.ToOriginalFrames(world.Tick - _lastSelfSpinUpdate)) > data.SelfSpinUpdateDelay)
        {
            var change = SimulationRate.Original.Time.PerSecondToPerTick(data.SelfSpinUpdateAmount);
            if (_selfSpinTowardsMax)
            {
                _selfSpin += change;
                if (_selfSpin > data.MaxSelfSpin) { _selfSpin = data.MaxSelfSpin; _selfSpinTowardsMax = false; }
            }
            else
            {
                _selfSpin -= change;
                if (_selfSpin < data.MinSelfSpin) { _selfSpin = data.MinSelfSpin; _selfSpinTowardsMax = true; }
            }
            _lastSelfSpinUpdate = world.Tick;
        }
        var stepX = FixedTrig.Cos(_forwardAngle) * _forwardSpeed;
        var stepY = FixedTrig.Sin(_forwardAngle) * _forwardSpeed;
        Entity.X += stepX;
        Entity.Y += stepY;
        _forwardAngle = FixedTrig.Normalize(_forwardAngle + data.SpiralOrbitTurnRate);
        _forwardSpeed *= data.SpiralOrbitForwardSpeedDamping;
        BladeCountdown(world);
        _fallSpeed += FlightFrames.GravityPerFrameSquared(world.Templates) * data.FallHowFast;
        Entity.Z += _fallSpeed;
        var ground = world.GroundHeightAt(Entity.X, Entity.Y);
        if (Entity.Z <= ground + Fixed.One)
        {
            Entity.Z = Fixed.Max(Entity.Z, ground);
            _hitGroundTick = world.Tick;
            if (data.FxHitGround.Length > 0) world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.FxHitGround, 0, Entity.X, Entity.Y, Entity.Z);
            if (data.OclHitGround.Length > 0) world.CreateFromList(Entity, data.OclHitGround, EntityId.Invalid, false);
        }
        // The frame that reaches the ground puts the wreck there at once, as the 30 Hz step does; every other frame is flown in shares.
        else _spiral.Begin(world.Time, Entity, stepX, stepY, _fallSpeed, spin);
        world.MarkDirty(Entity);
    }

    /// <summary>
    /// The blade's countdown, one step per logic frame of the original: the delay is drawn over the unrounded frames of the data (the same range and so the same draw at every tick
    /// rate) and counted down by one per frame, as the reference counts it down once per update.
    /// </summary>
    private void BladeCountdown(IWorldServices world)
    {
        if (_bladeFlyOff <= Fixed.Zero) return;
        _bladeFlyOff -= Fixed.One;
        if (_bladeFlyOff > Fixed.Zero) return;
        if (data.FxBlade.Length > 0) world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.FxBlade, 0, Entity.X, Entity.Y, Entity.Z);
        if (data.OclBlade.Length > 0) world.CreateFromList(Entity, data.OclBlade, EntityId.Invalid, false);
        if (data.OclEjectPilot.Length > 0 && Entity.VeterancyLevel > 0) world.CreateFromList(Entity, data.OclEjectPilot, EntityId.Invalid, false);
    }

    private void SpawnRubble(IWorldServices world)
    {
        if (data.FinalRubbleObject.Length == 0) return;
        if (!world.Templates.TryGet(data.FinalRubbleObject, out var rubble))
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.FinalRubbleObject, CreationSkipReasons.UnknownTemplate);
        else if (!rubble.IsInstantiable)
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.FinalRubbleObject, CreationSkipReasons.TemplateNotInstantiable);
        else world.RequestSpawn(rubble.Index, Entity.Owner, Entity.X, Entity.Y, Entity.Z, Entity.Angle);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("activated", _activated);
        writer.Int("destructionTick", _destructionTick);
        writer.Fixed("forwardAngle", _forwardAngle);
        writer.Fixed("forwardSpeed", _forwardSpeed);
        writer.Fixed("selfSpin", _selfSpin);
        writer.Bool("selfSpinTowardsMax", _selfSpinTowardsMax);
        writer.Int("lastSelfSpinUpdate", _lastSelfSpinUpdate);
        writer.Fixed("bladeFlyOff", _bladeFlyOff);
        writer.Int("hitGroundTick", _hitGroundTick);
        writer.Fixed("fallSpeed", _fallSpeed);
        _spiral.Describe(writer, "spiralStep");
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _activated = reader.Bool("activated");
        _destructionTick = reader.Int("destructionTick");
        _forwardAngle = reader.Fixed("forwardAngle");
        _forwardSpeed = reader.Fixed("forwardSpeed");
        _selfSpin = reader.Fixed("selfSpin");
        _selfSpinTowardsMax = reader.Bool("selfSpinTowardsMax");
        _lastSelfSpinUpdate = reader.Int("lastSelfSpinUpdate");
        _bladeFlyOff = reader.Fixed("bladeFlyOff");
        _hitGroundTick = reader.Int("hitGroundTick");
        _fallSpeed = reader.Fixed("fallSpeed");
        _spiral.Load(reader, "spiralStep");
    }
}

// ========================================================================================================= HelixContain

/// <summary><c>HelixContainModuleData</c> = <c>TransportContainModuleData</c> + <c>PayloadTemplateName</c> (unused by the shipped data; a template naming one
/// is refused explicitly, the nested creation at construction time is not modeled) + <c>ShouldDrawPips</c> (UI).</summary>
public sealed record HelixContainData(TransportContainData Transport) : ModuleData
{
    public static HelixContainData Parse(RatedModuleConfiguration configuration)
    {
        if (ModuleConfigReader.All(configuration, "PayloadTemplateName").Any()) throw new UnsupportedModuleConfigurationException("PayloadTemplateName");
        return new HelixContainData(TransportContainData.Parse(configuration));
    }
}

/// <summary>
/// <c>HelixContain.cpp</c>: a <c>TransportContain</c> with one extra place for a <c>PORTABLE_STRUCTURE</c> (the gattling cannon or propaganda tower a
/// Helix carries on its back): the first portable structure is always accepted, does not use a slot, follows the Helix's position and heading every
/// update, dies with the Helix (<c>onDie</c>: <c>kill</c>) and disappears with it (<c>onDelete</c>). A second one replaces nothing (it takes a normal
/// slot check, as the reference falls through to <c>TransportContain</c>). Not modeled: the garrisoned weapon bonus and the damage-state mirroring
/// (<c>onBodyDamageStateChange</c> is visual).
/// </summary>
public sealed class HelixContain(HelixContainData data) : TransportContain(data.Transport)
{
    private EntityId _portable;

    public override string ModuleName => "HelixContain";
    public EntityId PortableStructure => _portable;

    public override bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity)
    {
        if (candidate.Template.IsKindOf("PORTABLE_STRUCTURE") && !_portable.IsValid && !Entity.EffectivelyDead) return true;
        return base.IsValidContainerFor(world, candidate, checkCapacity);
    }

    public override void Add(IWorldServices world, Entity candidate)
    {
        if (candidate.Template.IsKindOf("PORTABLE_STRUCTURE") && !_portable.IsValid)
        {
            _portable = candidate.Id;
            candidate.ContainedBy = Entity.Id;
            candidate.HasMoveTarget = false;
            candidate.X = Entity.X;
            candidate.Y = Entity.Y;
            candidate.Z = Entity.Z;
            candidate.Angle = Entity.Angle;
            world.WakeAt(Entity, this, world.Tick);
            return;
        }
        base.Add(world, candidate);
    }

    public override void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        if (candidate.Id == _portable)
        {
            _portable = EntityId.Invalid;
            candidate.ContainedBy = EntityId.Invalid;
            return;
        }
        base.Remove(world, candidate, positionAtContainer);
    }

    public override long Update(IWorldServices world)
    {
        if (_portable.IsValid)
        {
            if (world.FindEntity(_portable) is { } portable && !portable.DestroyRequested)
            {
                portable.X = Entity.X;
                portable.Y = Entity.Y;
                portable.Z = Entity.Z;
                portable.Angle = Entity.Angle;
                world.MarkDirty(portable);
            }
            else _portable = EntityId.Invalid;
        }
        var sleep = base.Update(world);
        return _portable.IsValid ? SimulationTiming.SleepNone : sleep;
    }

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (_portable.IsValid) world.Kill(_portable);
        base.OnDie(world, info);
    }

    public override void OnRemove(IWorldServices world)
    {
        if (_portable.IsValid) world.RequestDestroy(_portable);
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        writer.UInt("portable", _portable.Value);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        _portable = new EntityId(checked((uint)reader.UInt("portable")));
    }
}

// ======================================================================================================== TunnelContain

/// <summary>
/// <c>TunnelContainModuleData</c>: the die filter and <c>TimeForFullHeal</c> (<c>parseDurationReal</c>) in unrounded logic frames of the original at every tick rate (the number of the
/// 30 Hz catalog: a rider is healed once per frame of the original, by the slice the 30 Hz division yields). The shared <c>OpenContain</c> fields are typed; the capacity is
/// <c>MaxTunnelCapacity</c> of <c>GameData.ini</c>.
/// </summary>
public sealed record TunnelContainData(DieMuxData DieMux, Fixed TicksForFullHeal) : ModuleData
{
    public static TunnelContainData Parse(RatedModuleConfiguration configuration) => new(
        DieMuxData.Parse(configuration),
        ModuleConfigReader.Find(configuration, "TimeForFullHeal")?.Value.Value is Data.Definitions.GameTime time ? SimulationRate.Original.Time.DurationMillisecondsToFixedTicks(time.Milliseconds) : Fixed.Zero);
}

/// <summary>
/// <c>TunnelContain.cpp</c> with the player's <see cref="TunnelTracker"/>: the tunnel stores its passengers in its owner's network, so everybody in any
/// tunnel of the player can leave through any other one. A tunnel registers itself when it is created (<c>onObjectCreated</c>) — also under
/// construction — and unregisters when it dies (<c>onDie</c>, die filter applies), is removed (<c>onDelete</c>) or is sold (<c>onSelling</c>). When the
/// last tunnel goes, everyone inside is destroyed (the cave-in, <c>onTunnelDestroyed</c>), except on a sale, which lets them out first; otherwise
/// the riders that entered the dead tunnel are reassigned to the oldest remaining one. Each tunnel's update heals every rider like
/// <c>healObject</c> — a sliver of <c>MaxHealth / TimeForFullHeal</c> per logic frame of the original, full health once inside that long; with several tunnels the network
/// is healed once per tunnel, exactly as in the reference. Leaving puts a rider at the tunnel and walks it to a random point
/// <c>BoundingCircleRadius · [1, 1.5]</c> away (<c>scatterToNearbyPosition</c>, two logic draws).
/// <para>
/// Entry rules: not aircraft (<c>TunnelTracker::isValidContainerFor</c>), room in the network (<c>MaxTunnelCapacity</c>), and — standing in for
/// <c>ActionManager::canEnterObject</c> — only the tunnel owner's own objects, only into a living, finished tunnel. Not modeled: the nemesis tracking
/// for the tunnel defenders, <c>harmAndForceExitAllContained</c> (bunker busters), capture.
/// </para>
/// </summary>
public sealed class TunnelContain(TunnelContainData data) : SimModule, IUpdateModule, IDieModule, IContainerModule, IWorldAttached, ISellModule
{
    private static readonly int SoldBit = ObjectStatusBits.Index("SOLD");
    private long _nextWake = -1;
    private bool _registered;
    private TunnelTracker? _tracker;
    private int _capacity;

    public override string ModuleName => "TunnelContain";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public bool IsRegistered => _registered;

    public int ContainMax => _capacity;
    public int ContainCount => _tracker?.ContainCount ?? 0;
    public IReadOnlyList<EntityId> Contained => (IReadOnlyList<EntityId>?)_tracker?.Riders ?? [];

    public void Attach(IWorldServices world)
    {
        _tracker = world.TunnelSystemOf(Entity.Owner);
        _capacity = world.Templates.MaxTunnelCapacity;
    }

    public override void OnSpawn(IWorldServices world)
    {
        world.TunnelSystemOf(Entity.Owner).AddTunnel(Entity.Id);
        _registered = true;
        _nextWake = world.Tick + SimulationTiming.SleepNone;
    }

    public bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity)
    {
        if (!_registered || Entity.EffectivelyDead || Entity.HasStatus(ObjectStatusBits.UnderConstruction) || Entity.HasStatus(SoldBit)) return false;
        if (candidate.Template.IsKindOf("AIRCRAFT") || candidate.Owner != Entity.Owner) return false;
        var tracker = world.TunnelSystemOf(Entity.Owner);
        return !checkCapacity || tracker.ContainCount < world.Templates.MaxTunnelCapacity;
    }

    public void Add(IWorldServices world, Entity candidate)
    {
        world.TunnelSystemOf(Entity.Owner).AddRider(candidate.Id, world.Tick);
        candidate.ContainedBy = Entity.Id;
        candidate.HasMoveTarget = false;
        candidate.X = Entity.X;
        candidate.Y = Entity.Y;
        world.WakeAt(Entity, this, world.Tick);
    }

    public void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        world.TunnelSystemOf(Entity.Owner).RemoveRider(candidate.Id);
        candidate.ContainedBy = EntityId.Invalid;
        if (positionAtContainer) ScatterToNearbyPosition(world, candidate);
    }

    /// <summary><c>TunnelContain::scatterToNearbyPosition</c>.</summary>
    private void ScatterToNearbyPosition(IWorldServices world, Entity rider)
    {
        var angle = world.LogicRandom.Range(Fixed.Zero, FixedTrig.TwoPi);
        var minRadius = Entity.Template.BoundingCircleRadius;
        var distance = world.LogicRandom.Range(minRadius, minRadius + minRadius / Fixed.FromInt(2));
        var x = Entity.X + distance * FixedTrig.Cos(angle);
        var y = Entity.Y + distance * FixedTrig.Sin(angle);
        rider.Angle = FixedTrig.Normalize(angle);
        var ai = rider.Modules.OfType<AIUpdateInterface>().FirstOrDefault();
        if (ai is not null && rider.Template.Locomotor is not null)
        {
            rider.X = Entity.X;
            rider.Y = Entity.Y;
            rider.Z = world.GroundHeightAt(Entity.X, Entity.Y);
            ai.OrderMove(world, x, y, false);
        }
        else
        {
            rider.X = x;
            rider.Y = y;
            rider.Z = world.GroundHeightAt(x, y);
        }
        world.MarkDirty(rider);
    }

    public long Update(IWorldServices world)
    {
        var tracker = world.TunnelSystemOf(Entity.Owner);
        if (tracker.ContainCount == 0) return SimulationTiming.SleepNone;
        foreach (var riderId in tracker.Riders.ToArray())
        {
            var rider = world.FindEntity(riderId);
            if (rider is null || rider.DestroyRequested || rider.EffectivelyDead) { tracker.RemoveRider(riderId); continue; }
            if (rider.Body is null) continue;
            // One slice per logic frame of the original, counted from the tick the rider entered (Originalraster: the reference heals once per frame; slices per tick are
            // smaller numbers that do not add up to the slice of a frame, and a rider that enters between two frames would be healed in one frame more).
            var ticksInside = world.Tick - tracker.EnteredAt(riderId);
            if (!world.Time.IsOriginalFrameBoundary(ticksInside)) continue;
            var inside = world.Time.ToOriginalFrames(ticksInside);
            var amount = data.TicksForFullHeal <= Fixed.Zero || Fixed.FromInt(inside) >= data.TicksForFullHeal
                ? rider.Body.MaxHealth
                : world.Time.SharePerTick(rider.Body.MaxHealth, data.TicksForFullHeal);
            if (rider.Body.Health < rider.Body.MaxHealth)
                world.Damage(rider.Id, new DamageInfo(EntityId.Invalid, amount, DamageTypes.Healing, DeathTypes.None, false));
        }
        return SimulationTiming.SleepNone;
    }

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!data.DieMux.IsApplicable(Entity, info) || !_registered) return;
        TunnelDestroyed(world);
    }

    /// <summary><c>onDelete</c>: a removal without death still unregisters (and caves in the network when it was the last tunnel).</summary>
    public override void OnRemove(IWorldServices world)
    {
        if (_registered) TunnelDestroyed(world);
    }

    /// <summary><c>onSelling</c>: the last tunnel lets everybody out, then the tunnel unregisters (after the kick-out, so no cave-in happens).</summary>
    public void OnSelling(IWorldServices world)
    {
        if (!_registered) return;
        if (world.TunnelSystemOf(Entity.Owner).TunnelCount == 1) world.EvacuateContainer(Entity);
        TunnelDestroyed(world);
    }

    /// <summary><c>TunnelTracker::onTunnelDestroyed</c>.</summary>
    private void TunnelDestroyed(IWorldServices world)
    {
        _registered = false;
        var tracker = world.TunnelSystemOf(Entity.Owner);
        if (tracker.RemoveTunnel(Entity.Id))
        {
            // Cave in: everybody inside is destroyed (destroyObject, no death).
            foreach (var riderId in tracker.Riders.ToArray())
            {
                if (world.FindEntity(riderId) is { } rider) rider.ContainedBy = EntityId.Invalid;
                world.RequestDestroy(riderId);
            }
            tracker.Clear();
            return;
        }
        var survivor = tracker.Tunnels[0];
        foreach (var riderId in tracker.Riders)
            if (world.FindEntity(riderId) is { } rider && rider.ContainedBy == Entity.Id)
            {
                rider.ContainedBy = survivor;
                world.MarkDirty(rider);
            }
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("registered", _registered);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _registered = reader.Bool("registered");
    }
}

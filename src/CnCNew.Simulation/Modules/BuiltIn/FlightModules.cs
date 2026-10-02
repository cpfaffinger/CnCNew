using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The aircraft modules scripted reinforcements and the cargo planes need (<c>docs/SCRIPT-ENGINE.md</c> §5/§8, Phase 13): <c>DeliverPayloadAIUpdate</c> (the approach/drop/leave
/// machine of a transport plane), <c>ParachuteContain</c> (the chute a dropped unit rides), <c>JetSlowDeathBehavior</c> (a shot-down plane) and
/// <c>CountermeasuresBehavior</c> (flares that divert missiles). Each cites its reference file; the physics of the reference (forces, suspension, bone transforms) is
/// replaced by kinematics and stated at the module.
/// </summary>
internal static class FlightModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<JetSlowDeathData>("JetSlowDeathBehavior", ModuleOwnerKind.Object, (configuration, _) => JetSlowDeathData.Parse(configuration), data => new JetSlowDeathBehavior(data)),
        new ModuleBinding<CountermeasuresData>("CountermeasuresBehavior", ModuleOwnerKind.Object, CountermeasuresData.Parse, data => new CountermeasuresBehavior(data)),
        new ModuleBinding<ParachuteData>("ParachuteContain", ModuleOwnerKind.Object, (configuration, _) => ParachuteData.Parse(configuration), data => new ParachuteContain(data)),
        new ModuleBinding<ParticleUplinkData>("ParticleUplinkCannonUpdate", ModuleOwnerKind.Object, (configuration, _) => ParticleUplinkData.Parse(configuration), data => new ParticleUplinkCannonUpdate(data)),
        new ModuleBinding<DeliverPayloadData>("DeliverPayloadAIUpdate", ModuleOwnerKind.Object, (configuration, _) => DeliverPayloadData.Parse(configuration), data => new DeliverPayloadAIUpdate(data)),
    ];

    /// <summary>True when the entity stands outside the playable area (<c>Object::isOffMap</c>); without a terrain nothing is off the map.</summary>
    public static bool IsOffMap(IWorldServices world, Fixed x, Fixed y)
        => world.Terrain is { } terrain && (x < Fixed.Zero || y < Fixed.Zero || x > terrain.ExtentX || y > terrain.ExtentY);

    /// <summary>The terrain height under a point, or the given fallback when the point is off the map.</summary>
    public static Fixed GroundOrDefault(IWorldServices world, Fixed x, Fixed y, Fixed fallback) => IsOffMap(world, x, y) ? fallback : world.GroundHeightAt(x, y);

    /// <summary>The nearest point of the map's rim (<c>TerrainLogic::findClosestEdgePoint</c>); the point itself without a terrain.</summary>
    public static (Fixed X, Fixed Y) ClosestEdgePoint(IWorldServices world, Fixed x, Fixed y)
    {
        if (world.Terrain is not { } terrain) return (x, y);
        var clampedX = Fixed.Min(Fixed.Max(x, Fixed.Zero), terrain.ExtentX);
        var clampedY = Fixed.Min(Fixed.Max(y, Fixed.Zero), terrain.ExtentY);
        if (clampedX != x || clampedY != y) return (clampedX, clampedY);
        var toLeft = x;
        var toRight = terrain.ExtentX - x;
        var toBottom = y;
        var toTop = terrain.ExtentY - y;
        var best = Fixed.Min(Fixed.Min(toLeft, toRight), Fixed.Min(toBottom, toTop));
        if (best == toLeft) return (Fixed.Zero, y);
        if (best == toRight) return (terrain.ExtentX, y);
        return best == toBottom ? (x, Fixed.Zero) : (x, terrain.ExtentY);
    }
}

// ================================================================================================================ JetSlowDeathBehavior

/// <summary>
/// <c>JetSlowDeathBehaviorModuleData</c> (<c>JetSlowDeathBehavior.cpp</c>) in simulation units. <c>RollRate</c>, <c>RollRateDelta</c>, <c>PitchRate</c> and <c>DeathLoopSound</c> only turn the model or play
/// a sound (typed by the catalog schema, no consumer); the fall itself is <see cref="FallHowFast"/>.
/// </summary>
public sealed record JetSlowDeathData(SlowDeathData Base, string FxOnGroundDeath, string OclOnGroundDeath, string FxInitialDeath, string OclInitialDeath, long DelaySecondaryTicks,
    string FxSecondary, string OclSecondary, string FxHitGround, string OclHitGround, long DelayFinalBlowUpTicks, string FxFinalBlowUp, string OclFinalBlowUp, Fixed FallHowFast) : ModuleData
{
    public static JetSlowDeathData Parse(ModuleConfiguration configuration) => new(
        SlowDeathData.Parse(configuration),
        ModuleConfigReader.Text(configuration, "FXOnGroundDeath"), ModuleConfigReader.Text(configuration, "OCLOnGroundDeath"),
        ModuleConfigReader.Text(configuration, "FXInitialDeath"), ModuleConfigReader.Text(configuration, "OCLInitialDeath"),
        ModuleConfigReader.Ticks(configuration, "DelaySecondaryFromInitialDeath", 0),
        ModuleConfigReader.Text(configuration, "FXSecondary"), ModuleConfigReader.Text(configuration, "OCLSecondary"),
        ModuleConfigReader.Text(configuration, "FXHitGround"), ModuleConfigReader.Text(configuration, "OCLHitGround"),
        ModuleConfigReader.Ticks(configuration, "DelayFinalBlowUpFromHitGround", 0),
        ModuleConfigReader.Text(configuration, "FXFinalBlowUp"), ModuleConfigReader.Text(configuration, "OCLFinalBlowUp"),
        ModuleConfigReader.Real(configuration, "FallHowFast", Fixed.Zero));
}

/// <summary>
/// <c>JetSlowDeathBehavior.cpp</c>: a <c>SlowDeathBehavior</c> for aircraft. A jet that dies on the ground only plays its ground effects and disappears (<c>onDie</c> without the
/// lottery); in the air it joins the sibling lottery and, once chosen, stops steering (<c>setMaxTurnRate(0)</c>), plays <c>FXInitialDeath</c>/<c>OCLInitialDeath</c> and falls
/// with <c>FallHowFast</c> of the gravity (the reference lowers the locomotor's lift to <c>-gravity · (1 - FallHowFast)</c>, i.e. the net pull is that share) while it keeps flying
/// straight at its speed. <c>DelaySecondaryFromInitialDeath</c> after the start the secondary effects run once; at the ground it hits (<c>FXHitGround</c>/<c>OCLHitGround</c>) and
/// <c>DelayFinalBlowUpFromHitGround</c> later it blows up (<c>FXFinalBlowUp</c>/<c>OCLFinalBlowUp</c>) and is destroyed. As in the reference the base class's destruction timer
/// (<c>DestructionDelay</c>) destroys the object whatever the phase, and the rest of that update still runs. Not modeled: rolling and pitching the model, the death loop sound,
/// the tree collision (<c>hitATree</c>), the deck-height status of carriers.
/// </summary>
public sealed class JetSlowDeathBehavior(JetSlowDeathData data) : DieModuleBase(data.Base.Mux), IUpdateModule, ISlowDeathCandidate
{
    private long _nextWake = -1;
    private bool _activated;
    private long _destructionTick;
    private long _deathTick;
    private long _groundTick;
    private Fixed _fallSpeed;

    public override string ModuleName => "JetSlowDeathBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public bool IsApplicable(in DamageInfo info) => Mux.IsApplicable(Entity, info);
    public int ProbabilityWeight => Math.Max(data.Base.ProbabilityModifier, 1);

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!world.IsSignificantlyAboveTerrain(Entity))
        {
            Effects(world, data.FxOnGroundDeath, data.OclOnGroundDeath);
            world.RequestDestroy(Entity.Id);
            return;
        }
        SlowDeathLottery.Run(world, Entity, info, IsApplicable(info));
    }

    public void BeginSlowDeath(IWorldServices world)
    {
        _activated = true;
        // SlowDeathBehavior::beginSlowDeath: both variance draws happen, the sink animation is presentation.
        _ = data.Base.SinkDelayTicks + world.LogicRandom.Range(0, checked((int)data.Base.SinkDelayVarianceTicks));
        var destruction = data.Base.DestructionDelayTicks + world.LogicRandom.Range(0, checked((int)data.Base.DestructionDelayVarianceTicks));
        _destructionTick = world.Tick + Math.Max(destruction, SimulationTiming.SleepNone);
        _deathTick = world.Tick;
        _groundTick = 0;
        _fallSpeed = Fixed.Zero;
        Entity.HasMoveTarget = false;
        Effects(world, data.FxInitialDeath, data.OclInitialDeath);
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public long Update(IWorldServices world)
    {
        if (!_activated) return SimulationTiming.SleepForever;
        if (world.Tick >= _destructionTick) world.RequestDestroy(Entity.Id);
        if (_groundTick == 0)
        {
            Entity.X += FixedTrig.Cos(Entity.Angle) * Entity.Speed;
            Entity.Y += FixedTrig.Sin(Entity.Angle) * Entity.Speed;
            _fallSpeed += world.Templates.GravityPerTickSquared * data.FallHowFast;
            Entity.Z += _fallSpeed;
            var ground = FlightModules.GroundOrDefault(world, Entity.X, Entity.Y, Fixed.Zero);
            if (Entity.Z <= ground)
            {
                Entity.Z = ground;
                Entity.Speed = Fixed.Zero;
                _groundTick = world.Tick;
                Effects(world, data.FxHitGround, data.OclHitGround);
            }
            world.MarkDirty(Entity);
            if (_deathTick != 0 && world.Tick - _deathTick >= data.DelaySecondaryTicks)
            {
                Effects(world, data.FxSecondary, data.OclSecondary);
                _deathTick = 0;
            }
        }
        else if (world.Tick - _groundTick >= data.DelayFinalBlowUpTicks)
        {
            Effects(world, data.FxFinalBlowUp, data.OclFinalBlowUp);
            world.RequestDestroy(Entity.Id);
            return SimulationTiming.SleepForever;
        }
        return SimulationTiming.SleepNone;
    }

    private void Effects(IWorldServices world, string fx, string ocl)
    {
        if (fx.Length > 0) world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, fx, 0, Entity.X, Entity.Y, Entity.Z);
        if (ocl.Length > 0) world.CreateFromList(Entity, ocl, EntityId.Invalid, false);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("activated", _activated);
        writer.Int("destructionTick", _destructionTick);
        writer.Int("deathTick", _deathTick);
        writer.Int("groundTick", _groundTick);
        writer.Fixed("fallSpeed", _fallSpeed);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _activated = reader.Bool("activated");
        _destructionTick = reader.Int("destructionTick");
        _deathTick = reader.Int("deathTick");
        _groundTick = reader.Int("groundTick");
        _fallSpeed = reader.Fixed("fallSpeed");
    }
}

// ================================================================================================================ CountermeasuresBehavior

/// <summary>What a missile and the weapon code need of a plane's countermeasures (<c>CountermeasuresBehaviorInterface</c>).</summary>
public interface ICountermeasuresModule
{
    /// <summary><c>Object::hasCountermeasures</c>: the module's upgrade is active.</summary>
    bool IsActive { get; }

    /// <summary><c>reportMissileForCountermeasures</c>: a missile was fired at the object; decides now whether it will be diverted.</summary>
    void ReportMissile(IWorldServices world, Entity missile);

    /// <summary><c>calculateCountermeasureToDivertTo</c>: the flare of the newest volley that is closest to the object, or invalid.</summary>
    EntityId CalculateCountermeasureToDivertTo(IWorldServices world, Entity victim);
}

/// <summary><c>CountermeasuresBehaviorModuleData</c> (<c>CountermeasuresBehavior.h</c>) in simulation units; <c>FlareBoneBaseName</c> places the flare on a bone (presentation).</summary>
public sealed record CountermeasuresData(UpgradeMuxData UpgradeMux, string FlareTemplate, Fixed EvasionRate, int VolleySize, Fixed VolleyArcRadians, Fixed VolleyVelocityFactor,
    long TicksBetweenVolleys, int NumberOfVolleys, long ReloadTicks, long MissileDecoyTicks, long ReactionTicks) : ModuleData
{
    public static CountermeasuresData Parse(ModuleConfiguration configuration, UpgradeCatalog upgrades) => new(
        UpgradeMuxData.Parse(configuration, upgrades),
        ModuleConfigReader.Text(configuration, "FlareTemplateName"),
        ModuleConfigReader.Real(configuration, "EvasionRate", Fixed.Zero),
        checked((int)ModuleConfigReader.Int(configuration, "VolleySize", 0)),
        SimulationTiming.DegreesToRadians(ModuleConfigReader.Real(configuration, "VolleyArcAngle", Fixed.Zero).ToDecimal()),
        ModuleConfigReader.Real(configuration, "VolleyVelocityFactor", Fixed.One),
        ModuleConfigReader.Ticks(configuration, "DelayBetweenVolleys", 0),
        checked((int)ModuleConfigReader.Int(configuration, "NumberOfVolleys", 0)),
        ModuleConfigReader.Ticks(configuration, "ReloadTime", 0),
        ModuleConfigReader.Ticks(configuration, "MissileDecoyDelay", 0),
        ModuleConfigReader.Ticks(configuration, "ReactionLaunchLatency", 0));
}

/// <summary>
/// <c>CountermeasuresBehavior.cpp</c>. Once the module's upgrade is active the plane carries <c>NumberOfVolleys · VolleySize</c> flares. A missile fired at the plane
/// (<see cref="ReportMissile"/>, called from the projectile launch for <c>SMALL_MISSILE</c> objects) is diverted with probability <c>EvasionRate</c> (one real draw of the
/// logic stream); a diverted missile turns to a flare <c>MissileDecoyDelay</c> ticks later (<see cref="MissileAIUpdate.SetFramesTillCountermeasureDiversionOccurs"/>) and the first
/// volley leaves <c>ReactionLaunchLatency</c> ticks after the first diverted report. While the plane is airborne a volley of <c>VolleySize</c> flares is launched every
/// <c>DelayBetweenVolleys</c> ticks until the flares run out; <c>ReloadTime</c> (0: only at an airfield, which does not exist here) restocks them. The flare is a real object
/// (<c>FlareTemplateName</c>, short-lived by its own <c>LifetimeUpdate</c>) that the module moves: it leaves the plane at <c>VolleyVelocityFactor</c> times the plane's speed along the
/// heading turned by the volley's spread (<c>VolleyArcAngle</c>), slows down at the flare locomotor's braking and sinks at its <c>SpeedLimitZ</c> — the reference pushes it with a
/// force through <c>PhysicsBehavior</c>. Not modeled: placing the flare on the model's flare bone (<c>FlareBoneBaseName</c>), <c>MustReloadAtAirfield</c> (no airfield reload),
/// the supersonic locomotor set that cannot launch flares.
/// </summary>
public sealed class CountermeasuresBehavior(CountermeasuresData data) : SimModule, IUpdateModule, IUpgradeModule, ICountermeasuresModule
{
    private readonly UpgradeMuxState _mux = new(data.UpgradeMux);
    private readonly List<Flare> _flares = [];
    private long _nextWake = -1;
    private int _available = checked(data.NumberOfVolleys * data.VolleySize);
    private int _diverted;
    private int _incoming;
    private long _reactionTick;
    private long _nextVolleyTick;
    private long _reloadTick;

    private sealed record Flare(EntityId Id, long AddedTick, Fixed VelocityX, Fixed VelocityY);

    public override string ModuleName => "CountermeasuresBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public bool IsActive => _mux.Executed;

    /// <summary>How many missiles were reported and how many of them were marked for diversion (read by tests and tools).</summary>
    public (int Incoming, int Diverted) MissileCounts => (_incoming, _diverted);

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public void TryActivate(IWorldServices world, UpgradeMask keyMask)
    {
        if (_mux.AttemptUpgrade(world, Entity, keyMask, _ => { })) world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public void TryReset(IWorldServices world, UpgradeMask keyMask) => _mux.ResetUpgrade(keyMask);

    public void ReportMissile(IWorldServices world, Entity missile)
    {
        _incoming++;
        if (_available + _flares.Count <= 0) return;
        if (world.LogicRandom.Range(Fixed.Zero, Fixed.One) >= data.EvasionRate) return;
        var projectile = missile.ModuleArray.OfType<MissileAIUpdate>().FirstOrDefault();
        if (projectile is null) return;
        projectile.SetFramesTillCountermeasureDiversionOccurs(world, data.MissileDecoyTicks);
        _diverted++;
        if (_flares.Count == 0 && _reactionTick == 0) _reactionTick = world.Tick + Math.Max(data.ReactionTicks, 1);
    }

    public EntityId CalculateCountermeasureToDivertTo(IWorldServices world, Entity victim)
    {
        var considered = Math.Max(data.VolleySize, 1);
        var best = EntityId.Invalid;
        var bestDistance = Fixed.MaxValue;
        for (var index = _flares.Count - 1; index >= 0 && considered-- > 0; index--)
        {
            if (world.FindEntity(_flares[index].Id) is not { } flare) continue;
            var dx = flare.X - victim.X;
            var dy = flare.Y - victim.Y;
            var distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = flare.Id;
        }
        return best;
    }

    public long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead || !_mux.Executed) return SimulationTiming.SleepForever;
        var now = world.Tick;
        MoveFlares(world, now);
        if (world.IsSignificantlyAboveTerrain(Entity) && _available > 0)
        {
            if (_reactionTick != 0 && now >= _reactionTick)
            {
                LaunchVolley(world, now);
                _nextVolleyTick = now + data.TicksBetweenVolleys;
                _reactionTick = 0;
            }
            if (_nextVolleyTick != 0 && now >= _nextVolleyTick)
            {
                LaunchVolley(world, now);
                _nextVolleyTick = now + data.TicksBetweenVolleys;
            }
        }
        if (_available == 0 && data.ReloadTicks > 0)
        {
            if (_reloadTick == 0) _reloadTick = now + data.ReloadTicks;
            else if (_reloadTick <= now)
            {
                _available = checked(data.NumberOfVolleys * data.VolleySize);
                _reloadTick = 0;
                _nextVolleyTick = 0;
            }
        }
        return SimulationTiming.SleepNone;
    }

    /// <summary>Forgets flares that are gone and carries the others along: horizontal speed bleeds off at the flare locomotor's braking, the flare sinks at its <c>SpeedLimitZ</c>.</summary>
    private void MoveFlares(IWorldServices world, long now)
    {
        for (var index = _flares.Count - 1; index >= 0; index--)
        {
            var flare = _flares[index];
            var entity = world.FindEntity(flare.Id);
            if (entity is null || entity.DestroyRequested)
            {
                // A spawn requested this tick is registered at its end: only a flare that had its chance to appear can be gone.
                if (now > flare.AddedTick) _flares.RemoveAt(index);
                continue;
            }
            var locomotor = entity.Template.Locomotor;
            var braking = locomotor?.Braking ?? Fixed.Zero;
            var speed = Fixed.Sqrt(flare.VelocityX * flare.VelocityX + flare.VelocityY * flare.VelocityY);
            var scale = speed <= braking || speed == Fixed.Zero ? Fixed.Zero : (speed - braking) / speed;
            var velocityX = flare.VelocityX * scale;
            var velocityY = flare.VelocityY * scale;
            _flares[index] = flare with { VelocityX = velocityX, VelocityY = velocityY };
            entity.X += velocityX;
            entity.Y += velocityY;
            var ground = FlightModules.GroundOrDefault(world, entity.X, entity.Y, Fixed.Zero);
            entity.Z = Fixed.Max(ground, entity.Z - (locomotor?.SpeedLimitZ ?? Fixed.Zero));
            world.MarkDirty(entity);
        }
    }

    private void LaunchVolley(IWorldServices world, long now)
    {
        if (!world.Templates.TryGet(data.FlareTemplate, out var template) || !template.IsInstantiable)
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.FlareTemplate, CreationSkipReasons.UnknownTemplate);
            _available = 0;
            return;
        }
        var size = data.VolleySize;
        for (var index = 0; index < size && _available > 0; index++)
        {
            // Two flares leave at the extreme spread, one straight along the heading, more are divided equally.
            var ratio = size == 1 ? Fixed.Zero : Fixed.FromInt(index) / Fixed.FromInt(size - 1) * Fixed.FromInt(2) - Fixed.One;
            var angle = FixedTrig.Normalize(Entity.Angle + ratio * data.VolleyArcRadians);
            // "if velocity < 1 then -10": a parked plane throws its flares backwards.
            var speed = Entity.Speed * Fixed.FromInt(SimulationTiming.LogicFramesPerSecond) < Fixed.One ? -Fixed.FromInt(10) / Fixed.FromInt(SimulationTiming.LogicFramesPerSecond) : Entity.Speed;
            var id = world.RequestSpawn(template.Index, Entity.Owner, Entity.X, Entity.Y, Entity.Z, Entity.Angle);
            _flares.Add(new Flare(id, now, FixedTrig.Cos(angle) * speed * data.VolleyVelocityFactor, FixedTrig.Sin(angle) * speed * data.VolleyVelocityFactor));
            _available--;
        }
    }

    public override void Describe(IStateWriter writer)
    {
        _mux.Describe(writer);
        writer.Int("nextWake", _nextWake);
        writer.Int("available", _available);
        writer.Int("diverted", _diverted);
        writer.Int("incoming", _incoming);
        writer.Int("reactionTick", _reactionTick);
        writer.Int("nextVolleyTick", _nextVolleyTick);
        writer.Int("reloadTick", _reloadTick);
        writer.BeginArray("flares");
        foreach (var flare in _flares)
        {
            writer.BeginObject(null);
            writer.UInt("id", flare.Id.Value);
            writer.Int("added", flare.AddedTick);
            writer.Fixed("vx", flare.VelocityX);
            writer.Fixed("vy", flare.VelocityY);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _mux.Load(reader);
        _nextWake = reader.Int("nextWake");
        _available = checked((int)reader.Int("available"));
        _diverted = checked((int)reader.Int("diverted"));
        _incoming = checked((int)reader.Int("incoming"));
        _reactionTick = reader.Int("reactionTick");
        _nextVolleyTick = reader.Int("nextVolleyTick");
        _reloadTick = reader.Int("reloadTick");
        _flares.Clear();
        foreach (var item in reader.Array("flares")) _flares.Add(new Flare(new EntityId(checked((uint)item.UInt("id"))), item.Int("added"), item.Fixed("vx"), item.Fixed("vy")));
    }
}

// ================================================================================================================ ParachuteContain

/// <summary>
/// <c>ParachuteContainModuleData</c> (<c>ParachuteContain.cpp</c>) on top of the shared <c>OpenContain</c> fields. <c>PitchRateMax</c>/<c>RollRateMax</c> (the sway of the model),
/// <c>LowAltitudeDamping</c> and the open sound are presentation; <c>KillWhenLandingInWaterSlop</c> is the height above the water a landing still counts as in the water.
/// </summary>
public sealed record ParachuteData(OpenContainData Base, Fixed OpenDistance, Fixed FreeFallDamagePercent, Fixed WaterSlop) : ModuleData
{
    public static ParachuteData Parse(ModuleConfiguration configuration) => new(
        OpenContainData.Parse(configuration, []),
        ModuleConfigReader.Real(configuration, "ParachuteOpenDist", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "FreeFallDamagePercent", Fixed.FromDecimal(0.5m)),
        ModuleConfigReader.Real(configuration, "KillWhenLandingInWaterSlop", Fixed.FromInt(10)));
}

/// <summary>Whether a container may be told to drop its payload at an exact spot (<c>ContainModuleInterface::setOverrideDestination</c>).</summary>
public interface IOverrideDestination
{
    void SetOverrideDestination(Fixed x, Fixed y);
}

/// <summary>
/// <c>ParachuteContain.cpp</c>: the chute object that carries one infantry or <c>PARACHUTABLE</c> rider down. It falls freely under gravity until it dropped
/// <c>ParachuteOpenDist</c> below its start height (a chute released closer to the ground than twice that distance pretends to start at twice that distance above it), then
/// opens and descends at the chute locomotor's <c>SpeedLimitZ</c>; with a landing override (<c>DeliverPayload</c> with <c>ParachuteDirectly</c>) it also steers to that spot at the
/// locomotor's speed, otherwise it comes straight down. Touching the ground releases the rider (idle, <c>onRemoving</c>), kills the chute so that its own slow death plays, and kills a
/// rider that landed outside the map, in the water or on ground its locomotor surface cannot cross (<c>PATHFIND_CELL</c> cliff/water/impassable). A chute shot down in the air drops its rider,
/// who takes <c>FreeFallDamagePercent</c> of the maximum health as falling damage and — since there is no body physics to carry the fall to the ground — dies of the impact. While it is
/// itself carried (<c>ContainedBy</c>, held in the transport) it does nothing. Not modeled: swaying (pitch/roll), bone attachment offsets, bridge layers.
/// </summary>
public sealed class ParachuteContain(ParachuteData data) : ContainModuleBase(data.Base), IOverrideDestination
{
    private static readonly int FallingDamageType = NameTables.DamageType.TryIndexOf("FALLING", out var index) ? index : DamageTypes.Unresistable;
    private static readonly int SplattedDeathType = NameTables.DeathType.TryIndexOf("SPLATTED", out var index) ? index : DeathTypes.Normal;

    private bool _started;
    private bool _opened;
    private Fixed _startZ;
    private Fixed _fallSpeed;
    private bool _hasOverride;
    private Fixed _overrideX;
    private Fixed _overrideY;

    public override string ModuleName => "ParachuteContain";
    public override int ContainMax => 1;
    public bool Opened => _opened;

    public override void OnSpawn(IWorldServices world) => NextWake = world.Tick + SimulationTiming.SleepNone;

    public void SetOverrideDestination(Fixed x, Fixed y)
    {
        _hasOverride = true;
        _overrideX = x;
        _overrideY = y;
    }

    public override bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity)
    {
        if (Entity.EffectivelyDead || !PassesKindOfAndRelationship(world, candidate)) return false;
        // Infantry always ride a chute, whatever their slot count (paradropped pilots); anything else needs a slot count or the PARACHUTABLE kind.
        if (candidate.Template.TransportSlotCount == 0 && !candidate.Template.IsKindOf("INFANTRY") && !candidate.Template.IsKindOf("PARACHUTABLE")) return false;
        return Riders.Count == 0;
    }

    public override void Add(IWorldServices world, Entity candidate)
    {
        base.Add(world, candidate);
        candidate.Z = Entity.Z;
    }

    public override long Update(IWorldServices world)
    {
        PruneDead(world);
        // A chute that is itself cargo (held in the plane) just waits.
        if (Entity.ContainedBy.IsValid) return SimulationTiming.SleepNone;
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        var rider = Riders.Count > 0 ? world.FindEntity(Riders[0]) : null;
        if (rider is null)
        {
            // "If we have lost our passenger for whatever reason, die early."
            world.Kill(Entity.Id);
            return SimulationTiming.SleepForever;
        }

        var ground = FlightModules.GroundOrDefault(world, Entity.X, Entity.Y, Fixed.Zero);
        if (!_started)
        {
            _started = true;
            _startZ = Entity.Z;
            if (_startZ - ground < Fixed.FromInt(2) * data.OpenDistance) _startZ = ground + Fixed.FromInt(2) * data.OpenDistance;
        }
        var locomotor = Entity.Template.Locomotor;
        if (!_opened)
        {
            _fallSpeed += world.Templates.GravityPerTickSquared;
            Entity.Z += _fallSpeed;
            if ((_startZ - Entity.Z).Abs() >= data.OpenDistance)
            {
                _opened = true;
                _fallSpeed = -(locomotor?.SpeedLimitZ ?? Fixed.Zero);
            }
        }
        else
        {
            Entity.Z += _fallSpeed;
            if (_hasOverride && locomotor is not null)
            {
                var dx = _overrideX - Entity.X;
                var dy = _overrideY - Entity.Y;
                var distance = Fixed.Sqrt(dx * dx + dy * dy);
                if (distance <= locomotor.Speed) { Entity.X = _overrideX; Entity.Y = _overrideY; }
                else if (distance > Fixed.Zero)
                {
                    Entity.X += dx * locomotor.Speed / distance;
                    Entity.Y += dy * locomotor.Speed / distance;
                }
            }
        }
        ground = FlightModules.GroundOrDefault(world, Entity.X, Entity.Y, Fixed.Zero);
        world.MarkDirty(Entity);
        if (Entity.Z <= ground)
        {
            Entity.Z = ground;
            Land(world, rider);
            return SimulationTiming.SleepForever;
        }
        rider.X = Entity.X;
        rider.Y = Entity.Y;
        rider.Z = Entity.Z;
        world.MarkDirty(rider);
        return SimulationTiming.SleepNone;
    }

    /// <summary><c>onCollide(NULL)</c> + <c>onRemoving</c>: the rider steps off, goes idle, and the chute is killed so that its slow death runs.</summary>
    private void Land(IWorldServices world, Entity rider)
    {
        Remove(world, rider, true);
        rider.Z = Entity.Z;
        rider.HasMoveTarget = false;
        world.Emit(SimEventKind.EntityUncontained, rider.Id, Entity.Id, string.Empty, 0);
        world.MarkDirty(rider);
        if (LandedBadly(world, rider)) world.Kill(rider.Id);
        world.Kill(Entity.Id);
    }

    private bool LandedBadly(IWorldServices world, Entity rider)
    {
        if (FlightModules.IsOffMap(world, rider.X, rider.Y)) return true;
        if (world.Terrain is not { } terrain) return false;
        if (terrain.TryWaterLevel(rider.X, rider.Y, out var level) && rider.Z <= level + data.WaterSlop && terrain.IsUnderwater(rider.X, rider.Y)) return true;
        // Cliff, water and impassable terrain cells kill (a structure's footprint is an obstacle, not terrain, and does not).
        var cellX = TerrainMap.CellOf(rider.X);
        var cellY = TerrainMap.CellOf(rider.Y);
        return terrain.IsInside(cellX, cellY) && !NavigationGrid.SurfaceAllows(terrain.CellAt(cellX, cellY), LocomotorSurfaces.Ground);
    }

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (world.IsSignificantlyAboveTerrain(Entity) && Riders.Count > 0 && world.FindEntity(Riders[0]) is { } rider)
        {
            Remove(world, rider, true);
            world.Emit(SimEventKind.EntityUncontained, rider.Id, Entity.Id, string.Empty, 0);
            if (data.FreeFallDamagePercent > Fixed.Zero && rider.Body is { } body)
                world.Damage(rider.Id, new DamageInfo(info.Source, body.MaxHealth * data.FreeFallDamagePercent, FallingDamageType, SplattedDeathType, false));
            // No body physics carries the fall to the ground: the impact is lethal.
            world.Kill(rider.Id);
        }
        base.OnDie(world, info);
    }

    public override void Describe(IStateWriter writer)
    {
        DescribeContained(writer);
        writer.Bool("started", _started);
        writer.Bool("opened", _opened);
        writer.Fixed("startZ", _startZ);
        writer.Fixed("fallSpeed", _fallSpeed);
        if (_hasOverride)
        {
            writer.Bool("override", true);
            writer.Fixed("overrideX", _overrideX);
            writer.Fixed("overrideY", _overrideY);
        }
    }

    public override void Load(IStateReader reader)
    {
        LoadContained(reader);
        _started = reader.Bool("started");
        _opened = reader.Bool("opened");
        _startZ = reader.Fixed("startZ");
        _fallSpeed = reader.Fixed("fallSpeed");
        _hasOverride = reader.Has("override");
        _overrideX = _hasOverride ? reader.Fixed("overrideX") : Fixed.Zero;
        _overrideY = _hasOverride ? reader.Fixed("overrideY") : Fixed.Zero;
    }
}

// ================================================================================================================ DeliverPayloadAIUpdate

/// <summary>
/// <c>DeliverPayloadAIUpdateModuleData</c> (<c>DeliverPayloadAIUpdate.h</c>): the keys scripted reinforcements use (<c>deliverPayloadViaModuleData</c>). <c>DeliveryDecal</c> and
/// <c>DeliveryDecalRadius</c> draw the target circle (presentation); the dive, strafe and visible-payload settings belong to the creation-list nugget, not to this module.
/// </summary>
public sealed record DeliverPayloadData(AIUpdateData Base, long DoorDelayTicks, string PutInContainer, Fixed DeliveryDistance, int MaxAttempts, long DropDelayTicks, Point3 DropOffset, Point3 DropVariance) : ModuleData
{
    public static DeliverPayloadData Parse(ModuleConfiguration configuration) => new(
        AIUpdateData.Parse(configuration),
        ModuleConfigReader.Ticks(configuration, "DoorDelay", 0),
        ModuleConfigReader.Text(configuration, "PutInContainer"),
        ModuleConfigReader.Real(configuration, "DeliveryDistance", Fixed.Zero),
        checked((int)ModuleConfigReader.Int(configuration, "MaxAttempts", 0)),
        ModuleConfigReader.Ticks(configuration, "DropDelay", 0),
        ModuleConfigReader.Vector(configuration, "DropOffset"),
        ModuleConfigReader.Vector(configuration, "DropVariance"));
}

/// <summary>
/// <c>DeliverPayloadAIUpdate.cpp</c> as used by scripted reinforcements (<see cref="DeliverViaModuleData"/>, <c>deliverPayloadViaModuleData</c>): the plane flies to the drop
/// target (<c>ApproachState</c>), and once it is within <c>DeliveryDistance</c> opens its doors and, every <c>DropDelay</c> ticks after <c>DoorDelay</c>, lets the first of its
/// riders leave (<c>DeliveringState</c>: the rider is put at the plane's position plus <c>DropOffset</c> and the random <c>DropVariance</c>, where its own parachute takes over);
/// when nothing is left it flies on in a straight line until it leaves the map (<c>HeadOffMapState</c>) and is deleted (<c>CleanUpState</c>). A plane that overflew the target
/// (<c>ConsiderNewApproachState</c>) turns around at <c>2.2 ·</c> its minimum turn radius for up to <c>MaxAttempts</c> new passes, and one that left the map while doing so is hidden
/// for the time a turn takes and re-enters at the nearest edge (<c>RecoverFromOffMapState</c>). From head-off-map on it no longer answers movement orders. Flight is kinematic
/// through the ordinary move machine of <see cref="AIUpdateInterface"/> at the locomotor's <c>PreferredHeight</c>. Not modeled: the dive and strafe of bombers, visible payload
/// bones, <c>SelfDestructObject</c> and the hack that kills a plane that was turned off its exit course (the nugget-driven data, no scripted use).
/// </summary>
public sealed class DeliverPayloadAIUpdate(DeliverPayloadData data) : AIUpdateInterface(data.Base)
{
    private enum Phase : byte { None, Approach, Delivering, ConsiderNewApproach, RecoverFromOffMap, HeadOffMap }

    private Phase _phase;
    private Fixed _targetX;
    private Fixed _targetY;
    private bool _freeToExit;
    private bool _accepting = true;
    private long _dropDelayLeft;
    private int _attempts;
    private long _reentryTick;

    public override string ModuleName => "DeliverPayloadAIUpdate";

    /// <summary><c>getAiFreeToExit</c>: the doors are open and the plane is alive.</summary>
    public bool FreeToExit => _freeToExit && !Entity.EffectivelyDead;

    /// <summary>The plane is on its delivery run.</summary>
    public bool Delivering => _phase != Phase.None;

    /// <summary><c>getPutInContainerTemplateViaModuleData</c>: the template riders are packed into (the parachute), empty for none.</summary>
    public string PutInContainer => data.PutInContainer;

    public override void OnSpawn(IWorldServices world)
    {
        base.OnSpawn(world);
        NextWake = SimulationTiming.SleepForever;
    }

    /// <summary>Once the plane heads off the map it ignores every order (<c>m_acceptingCommands</c>).</summary>
    public override void OrderMove(IWorldServices world, Fixed x, Fixed y, bool fromPlayer)
    {
        if (_accepting) base.OrderMove(world, x, y, fromPlayer);
    }

    /// <summary><c>deliverPayloadViaModuleData</c>: fly to (<paramref name="x"/>, <paramref name="y"/>), drop everything inside, leave.</summary>
    public void DeliverViaModuleData(IWorldServices world, Fixed x, Fixed y)
    {
        _targetX = x;
        _targetY = y;
        _attempts = 0;
        _freeToExit = false;
        _accepting = true;
        StartPhase(world, Phase.Approach);
        world.WakeAt(Entity, this, world.Tick);
    }

    public override long Update(IWorldServices world)
    {
        if (_phase != Phase.None)
        {
            if (Entity.EffectivelyDead) _phase = Phase.None;
            else
            {
                RunPhase(world);
                ClimbToHeight(world);
            }
        }
        var sleep = base.Update(world);
        return _phase != Phase.None ? SimulationTiming.SleepNone : sleep;
    }

    private void ClimbToHeight(IWorldServices world)
    {
        if (Entity.Template.Locomotor is not { } locomotor) return;
        var preferred = locomotor.PreferredHeight;
        if (preferred <= Fixed.Zero) return;
        Entity.Z = FlightModules.GroundOrDefault(world, Entity.X, Entity.Y, Fixed.Zero) + preferred;
        world.MarkDirty(Entity);
    }

    private void StartPhase(IWorldServices world, Phase phase)
    {
        _phase = phase;
        switch (phase)
        {
            case Phase.Approach:
                base.OrderMove(world, _targetX, _targetY, false);
                break;
            case Phase.Delivering:
                _dropDelayLeft = data.DoorDelayTicks;
                break;
            case Phase.ConsiderNewApproach:
                ConsiderNewApproach(world);
                break;
            case Phase.RecoverFromOffMap:
                StopInternalMove();
                Entity.Speed = Fixed.Zero;
                _reentryTick = world.Tick + TurnTicks();
                break;
            case Phase.HeadOffMap:
                HeadOffMap(world);
                break;
        }
    }

    private void RunPhase(IWorldServices world)
    {
        switch (_phase)
        {
            case Phase.Approach:
                if (CloseEnoughToTarget()) StartPhase(world, Phase.Delivering);
                else if (!Entity.HasMoveTarget) StartPhase(world, Phase.ConsiderNewApproach);
                break;
            case Phase.Delivering:
                RunDelivering(world);
                break;
            case Phase.ConsiderNewApproach:
                if (FlightModules.IsOffMap(world, Entity.X, Entity.Y)) StartPhase(world, Phase.RecoverFromOffMap);
                else if (!Entity.HasMoveTarget) StartPhase(world, Phase.Approach);
                break;
            case Phase.RecoverFromOffMap:
                if (world.Tick < _reentryTick) break;
                var (edgeX, edgeY) = FlightModules.ClosestEdgePoint(world, Entity.X, Entity.Y);
                Entity.X = edgeX;
                Entity.Y = edgeY;
                Entity.Angle = FixedTrig.Atan2(_targetY - edgeY, _targetX - edgeX);
                Entity.Speed = Fixed.Zero;
                world.MarkDirty(Entity);
                StartPhase(world, Phase.Approach);
                break;
            case Phase.HeadOffMap:
                if (FlightModules.IsOffMap(world, Entity.X, Entity.Y) || !Entity.HasMoveTarget)
                {
                    _phase = Phase.None;
                    world.RequestDestroy(Entity.Id);
                }
                break;
        }
    }

    private bool CloseEnoughToTarget()
    {
        var dx = Entity.X - _targetX;
        var dy = Entity.Y - _targetY;
        return data.DeliveryDistance * data.DeliveryDistance > dx * dx + dy * dy;
    }

    private void RunDelivering(IWorldServices world)
    {
        if (_dropDelayLeft > 0)
        {
            _dropDelayLeft--;
            return;
        }
        _freeToExit = true;
        _dropDelayLeft = data.DropDelayTicks;
        if (!CloseEnoughToTarget())
        {
            _freeToExit = false;
            StartPhase(world, Phase.ConsiderNewApproach);
            return;
        }
        var item = Entity.Container is { } contain ? contain.Contained.Select(world.FindEntity).FirstOrDefault(rider => rider is not null) : null;
        if (item is null)
        {
            _freeToExit = false;
            StartPhase(world, Phase.HeadOffMap);
            return;
        }
        Drop(world, Entity.Container!, item);
    }

    private void Drop(IWorldServices world, IContainerModule contain, Entity item)
    {
        contain.Remove(world, item, true);
        world.Emit(SimEventKind.EntityUncontained, item.Id, Entity.Id, string.Empty, 0);
        var x = Entity.X + data.DropOffset.X;
        var y = Entity.Y + data.DropOffset.Y;
        var z = Entity.Z + data.DropOffset.Z;
        if (data.DropVariance.X > Fixed.Zero) x += world.LogicRandom.Range(-data.DropVariance.X, data.DropVariance.X);
        if (data.DropVariance.Y > Fixed.Zero) y += world.LogicRandom.Range(-data.DropVariance.Y, data.DropVariance.Y);
        if (data.DropVariance.Z > Fixed.Zero) z += world.LogicRandom.Range(-data.DropVariance.Z, data.DropVariance.Z);
        item.X = x;
        item.Y = y;
        item.Z = z;
        item.HasMoveTarget = false;
        world.MarkDirty(item);
    }

    /// <summary>The reference estimates the turn radius from the speed and the turn rate and flies on <c>2.2</c> radii before turning back.</summary>
    private Fixed MinTurnRadius()
    {
        var locomotor = Entity.Template.Locomotor;
        return locomotor is { TurnRatePerTick.Raw: > 0 } ? locomotor.Speed / locomotor.TurnRatePerTick : Fixed.FromInt(999999);
    }

    private long TurnTicks()
    {
        var locomotor = Entity.Template.Locomotor;
        return locomotor is { Speed.Raw: > 0 } ? Math.Max(1, (MinTurnRadius() / locomotor.Speed).Ceiling()) : 1;
    }

    private void ConsiderNewApproach(IWorldServices world)
    {
        _attempts++;
        if (_attempts > data.MaxAttempts)
        {
            StartPhase(world, Phase.HeadOffMap);
            return;
        }
        var distance = MinTurnRadius() * Fixed.FromDecimal(2.2m);
        base.OrderMove(world, Entity.X + FixedTrig.Cos(Entity.Angle) * distance, Entity.Y + FixedTrig.Sin(Entity.Angle) * distance, false);
    }

    private void HeadOffMap(IWorldServices world)
    {
        _accepting = false;
        // "just keep moving straight ahead till we exit the map": a point 1.2 map diagonals away along the heading.
        var reach = world.Terrain is { } terrain
            ? Fixed.Sqrt(terrain.ExtentX * terrain.ExtentX + terrain.ExtentY * terrain.ExtentY) * Fixed.FromDecimal(1.2m)
            : Fixed.FromInt(100000);
        base.OrderMove(world, Entity.X + FixedTrig.Cos(Entity.Angle) * reach, Entity.Y + FixedTrig.Sin(Entity.Angle) * reach, false);
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        if (_phase == Phase.None && _accepting && !_freeToExit) return;
        writer.Int("deliverPhase", (int)_phase);
        writer.Fixed("deliverX", _targetX);
        writer.Fixed("deliverY", _targetY);
        writer.Bool("freeToExit", _freeToExit);
        writer.Bool("accepting", _accepting);
        writer.Int("dropDelayLeft", _dropDelayLeft);
        writer.Int("attempts", _attempts);
        writer.Int("reentryTick", _reentryTick);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        if (!reader.Has("deliverPhase")) return;
        var phase = reader.Int("deliverPhase");
        if (phase < 0 || phase > (int)Phase.HeadOffMap) throw new InvalidDataException("Delivery phase out of range.");
        _phase = (Phase)phase;
        _targetX = reader.Fixed("deliverX");
        _targetY = reader.Fixed("deliverY");
        _freeToExit = reader.Bool("freeToExit");
        _accepting = reader.Bool("accepting");
        _dropDelayLeft = reader.Int("dropDelayLeft");
        _attempts = checked((int)reader.Int("attempts"));
        _reentryTick = reader.Int("reentryTick");
    }
}

// ================================================================================================================ ParticleUplinkCannonUpdate

/// <summary>
/// <c>ParticleUplinkCannonUpdateModuleData</c> (<c>ParticleUplinkCannonUpdate.cpp</c>) in simulation units. The charge, antenna and ready times, the bones, flares, sounds and scorch marks are
/// presentation and typed only; what drives the effect is the beam's timing (<c>BeamTravelTime</c>, <c>WidthGrowTime</c>, <c>TotalFiringTime</c>), its path (<c>SwathOfDeath*</c>, the manual driving speed)
/// and its damage (<c>DamagePerSecond</c>, <c>TotalDamagePulses</c>, <c>DamageType</c>, <c>DeathType</c>, <c>DamageRadiusScalar</c> on the radius of the beam object <c>ParticleBeamLaserName</c>).
/// </summary>
public sealed record ParticleUplinkData(string PowerName, string BeamObject, string RemnantObject, string GroundHitFx, string BeamLaunchFx, long LaunchFxTicks, long WidthGrowTicks, long BeamTravelTicks,
    long TotalFiringTicks, Fixed SwathDistance, Fixed SwathAmplitude, long TotalScorchMarks, int TotalDamagePulses, Fixed DamagePerSecond, int DamageType, int DeathType, Fixed DamageRadiusScalar,
    Fixed ManualDrivingSpeed) : ModuleData
{
    public static ParticleUplinkData Parse(ModuleConfiguration configuration)
    {
        var damageType = ModuleConfigReader.Text(configuration, "DamageType");
        var deathType = ModuleConfigReader.Text(configuration, "DeathType");
        return new ParticleUplinkData(
            ModuleConfigReader.Text(configuration, "SpecialPowerTemplate"), ModuleConfigReader.Text(configuration, "ParticleBeamLaserName"), ModuleConfigReader.Text(configuration, "DamagePulseRemnantObjectName"),
            ModuleConfigReader.Text(configuration, "GroundHitFX"), ModuleConfigReader.Text(configuration, "BeamLaunchFX"), ModuleConfigReader.Ticks(configuration, "DelayBetweenLaunchFX", 0),
            ModuleConfigReader.Ticks(configuration, "WidthGrowTime", 0), ModuleConfigReader.Ticks(configuration, "BeamTravelTime", 0), ModuleConfigReader.Ticks(configuration, "TotalFiringTime", 0),
            ModuleConfigReader.Real(configuration, "SwathOfDeathDistance", Fixed.Zero), ModuleConfigReader.Real(configuration, "SwathOfDeathAmplitude", Fixed.Zero),
            ModuleConfigReader.Int(configuration, "TotalScorchMarks", 0), checked((int)ModuleConfigReader.Int(configuration, "TotalDamagePulses", 0)),
            ModuleConfigReader.Real(configuration, "DamagePerSecond", Fixed.Zero),
            damageType.Length == 0 ? DamageTypes.Explosion : NameTables.DamageType.TryIndexOf(damageType, out var damageIndex) ? damageIndex : throw new InvalidDataException($"Unknown damage type '{damageType}'."),
            deathType.Length == 0 ? DeathTypes.Normal : NameTables.DeathType.TryIndexOf(deathType, out var deathIndex) ? deathIndex : throw new InvalidDataException($"Unknown death type '{deathType}'."),
            ModuleConfigReader.Real(configuration, "DamageRadiusScalar", Fixed.Zero), ModuleConfigReader.Real(configuration, "ManualDrivingSpeed", Fixed.Zero) / Fixed.FromInt(SimulationTiming.LogicFramesPerSecond));
    }
}

/// <summary>
/// <c>ParticleUplinkCannonUpdate.cpp</c>: the beam of the Particle Uplink Cannon. The power module on the same object (<c>SpecialAbility</c> with <c>UpdateModuleStartsAttack</c>) keeps the recharge;
/// this module is the handler that carries out a use: the beam is born <c>BeamTravelTime</c> after the order, then for <c>TotalFiringTime</c> it burns at its target — every
/// <c>lifetime / TotalDamagePulses</c> a pulse of <c>TotalFiringTime · DamagePerSecond / TotalDamagePulses</c> hits every living object (friend and foe alike) within the beam's radius
/// (half the beam object's <c>OuterBeamWidth</c>, growing from nothing over <c>WidthGrowTime</c> and shrinking the same way after <c>TotalFiringTime</c>) times <c>DamageRadiusScalar</c>
/// — and the use ends when the beam has decayed. A computer-controlled owner (and so a scripted use) lets the beam sweep an S curve across the target
/// (<c>SwathOfDeathDistance</c> long, <c>SwathOfDeathAmplitude</c> wide, along the line from the building to the target, with the reference's own rotation formula); a human owner
/// drives it, and without a driving command the beam stays where it was aimed. The ground-hit effect and the remnant object follow each pulse. Not modeled: the antenna/charge
/// stages (presentation, only visible through the power's own readiness), the shroud reveal around the beam, the abort of a beam whose building is underpowered or disabled, the
/// scripted waypoint path, the sounds and the scorch marks.
/// </summary>
public sealed class ParticleUplinkCannonUpdate(ParticleUplinkData data) : SimModule, IUpdateModule, ISpecialPowerHandler
{
    private enum Beam : byte { None, Born, Decaying, Dead }

    private long _nextWake = -1;
    private long _startAttack;
    private long _startDecay;
    private long _decayStart;
    private Beam _beam;
    private bool _manual;
    private Point3 _initial;
    private Point3 _current;
    private Point3 _destination;
    private int _pulses;
    private long _nextPulse;
    private long _scorches;
    private long _nextScorch;
    private long _nextLaunchFx;

    public override string ModuleName => "ParticleUplinkCannonUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary><c>isPowerCurrentlyInUse</c>: an order is running (the beam is on its way, burning or decaying).</summary>
    public bool InUse => _startAttack != 0;

    /// <summary>Where the beam burns right now (valid while <see cref="InUse"/> and the beam is born).</summary>
    public Point3 BeamPosition => _current;

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public bool Handles(IWorldServices world, int powerIndex) => world.SpecialPowers.TryGet(data.PowerName, out var info) && info.Index == powerIndex;

    public void Initiate(IWorldServices world, int powerIndex, in SpecialPowerTarget target)
    {
        var aimed = target.Kind == SpecialPowerTargetKind.Object ? world.FindEntity(target.Entity) : null;
        var x = aimed?.X ?? target.X;
        var y = aimed?.Y ?? target.Y;
        _initial = new Point3(x, y, world.GroundHeightAt(x, y));
        _current = _initial;
        _destination = _initial;
        // All human players "drive" the beam; a computer-controlled owner (and so a scripted use) lets it sweep.
        _manual = !world.IsComputerPlayer(Entity.Owner);
        _startAttack = Math.Max(world.Tick, 1);
        _startDecay = _startAttack + data.TotalFiringTicks;
        _decayStart = 0;
        _beam = Beam.None;
        _pulses = 0;
        _scorches = 0;
        SpecialPowerRouting.FindModule(Entity, powerIndex)?.Trigger(world);
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public long Update(IWorldServices world)
    {
        if (_startAttack == 0) return SimulationTiming.SleepForever;
        if (Entity.EffectivelyDead || Entity.HasStatus(ObjectStatusBits.Sold)) { Reset(); return SimulationTiming.SleepForever; }
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction)) return SimulationTiming.SleepNone;
        var now = world.Tick;
        if (_startAttack > now) return SimulationTiming.SleepNone;
        var birth = _startAttack + data.BeamTravelTicks;
        var decayStart = _startDecay + data.BeamTravelTicks;
        var death = decayStart + data.WidthGrowTicks;
        switch (_beam)
        {
            case Beam.None:
                if (birth <= now)
                {
                    _beam = Beam.Born;
                    _scorches = 0;
                    _nextScorch = now;
                    _pulses = 0;
                    _nextPulse = now;
                }
                break;
            case Beam.Born:
                if (decayStart <= now)
                {
                    _beam = Beam.Decaying;
                    _decayStart = now;
                }
                break;
            case Beam.Decaying:
                if (death <= now)
                {
                    _beam = Beam.Dead;
                    Reset();
                    return SimulationTiming.SleepForever;
                }
                break;
        }
        if (_beam is Beam.Born or Beam.Decaying && birth <= now && now <= death)
        {
            MoveBeam(world, now, birth, death);
            var radius = data.DamageRadiusScalar * BeamRadius(world, now, birth);
            if (_nextScorch <= now)
            {
                _scorches++;
                if (data.GroundHitFx.Length > 0) world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.GroundHitFx, 0, _current.X, _current.Y, _current.Z);
                _nextScorch = data.TotalScorchMarks > 0 ? birth + (death - birth) * _scorches / data.TotalScorchMarks : death + 1;
            }
            if (_nextPulse <= now && data.TotalDamagePulses > 0)
            {
                _pulses++;
                Pulse(world, radius);
                _nextPulse = birth + (death - birth) * _pulses / data.TotalDamagePulses;
            }
        }
        if (_startDecay > now && data.BeamLaunchFx.Length > 0 && _nextLaunchFx <= now)
        {
            world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.BeamLaunchFx, 0, Entity.X, Entity.Y, Entity.Z);
            _nextLaunchFx = now + Math.Max(data.LaunchFxTicks, 1);
        }
        return SimulationTiming.SleepNone;
    }

    private void Reset()
    {
        _startAttack = 0;
        _startDecay = 0;
        _beam = Beam.None;
    }

    /// <summary><c>LaserUpdate::getCurrentLaserRadius</c>: the beam object's template radius times the width scalar (growing after birth, shrinking after the decay began).</summary>
    private Fixed BeamRadius(IWorldServices world, long now, long birth)
    {
        if (!world.Templates.TryGet(data.BeamObject, out var beam) || data.WidthGrowTicks <= 0) return Fixed.Zero;
        var scalar = _beam == Beam.Decaying
            ? Fixed.One - Fixed.FromInt(now - _decayStart) / Fixed.FromInt(data.WidthGrowTicks)
            : Fixed.Min(Fixed.One, Fixed.FromInt(now - birth) / Fixed.FromInt(data.WidthGrowTicks));
        return beam.LaserBeamRadius * Fixed.Max(Fixed.Zero, scalar);
    }

    /// <summary>The beam's position this tick: the S curve of the reference (with its rotation by the angle between the building→target line and the offset's own direction), or a drive towards the destination.</summary>
    private void MoveBeam(IWorldServices world, long now, long birth, long death)
    {
        if (!_manual)
        {
            var factor = death > birth ? Fixed.FromInt(now - birth) / Fixed.FromInt(death - birth) : Fixed.Zero;
            var radians = factor * FixedTrig.TwoPi - FixedTrig.Pi;
            var along = factor * data.SwathDistance - data.SwathDistance / Fixed.FromInt(2);
            var across = FixedTrig.Sin(radians) * data.SwathAmplitude;
            var toTargetX = _initial.X - Entity.X;
            var toTargetY = _initial.Y - Entity.Y;
            var aimLength = Fixed.Sqrt(toTargetX * toTargetX + toTargetY * toTargetY);
            var offsetX = along + aimLength;
            var offsetY = across;
            var length = Fixed.Sqrt(offsetX * offsetX + offsetY * offsetY);
            if (length > Fixed.Zero && aimLength > Fixed.Zero)
            {
                var dot = toTargetX / aimLength * (offsetX / length) + toTargetY / aimLength * (offsetY / length);
                var limit = Fixed.FromDecimal(0.99999m);
                dot = Fixed.Min(limit, Fixed.Max(-limit, dot));
                var angle = FixedTrig.Atan2(Fixed.Sqrt(Fixed.One - dot * dot), dot);
                if (toTargetY >= Fixed.Zero) angle = -angle;
                _current = _current with { X = Entity.X + length * FixedTrig.Cos(-angle), Y = Entity.Y + length * FixedTrig.Sin(-angle) };
            }
        }
        else
        {
            var dx = _destination.X - _current.X;
            var dy = _destination.Y - _current.Y;
            var distance = Fixed.Sqrt(dx * dx + dy * dy);
            var speed = Fixed.Min(data.ManualDrivingSpeed, distance);
            if (distance > Fixed.Zero) _current = _current with { X = _current.X + dx * speed / distance, Y = _current.Y + dy * speed / distance };
        }
        _current = _current with { Z = world.GroundHeightAt(_current.X, _current.Y) };
    }

    /// <summary>One damage pulse: every living object with a body within <paramref name="radius"/> (2D from its centre) takes the pulse's share of <c>TotalFiringTime · DamagePerSecond</c>.</summary>
    private void Pulse(IWorldServices world, Fixed radius)
    {
        var seconds = Fixed.FromInt(data.TotalFiringTicks) / Fixed.FromInt(SimulationTiming.LogicFramesPerSecond);
        var amount = seconds * data.DamagePerSecond / Fixed.FromInt(data.TotalDamagePulses);
        var limit = radius * radius;
        foreach (var entity in world.Entities.OrderBy(value => value.Id.Value).ToArray())
        {
            if (entity.EffectivelyDead || entity.DestroyRequested || entity.Body is null) continue;
            var dx = entity.X - _current.X;
            var dy = entity.Y - _current.Y;
            if (dx * dx + dy * dy > limit) continue;
            world.Damage(entity.Id, new DamageInfo(Entity.Id, amount, data.DamageType, data.DeathType, false));
        }
        if (data.RemnantObject.Length > 0 && world.Templates.TryGet(data.RemnantObject, out var remnant))
        {
            if (remnant.IsInstantiable) world.RequestSpawn(remnant.Index, Entity.Owner, _current.X, _current.Y, _current.Z, Fixed.Zero);
            else world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.RemnantObject, CreationSkipReasons.TemplateNotInstantiable);
        }
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        if (_startAttack == 0) return;
        writer.Int("startAttack", _startAttack);
        writer.Int("startDecay", _startDecay);
        writer.Int("decayStart", _decayStart);
        writer.Int("beam", (int)_beam);
        writer.Bool("manual", _manual);
        DumbProjectileBehavior.WritePoint(writer, "initial", _initial);
        DumbProjectileBehavior.WritePoint(writer, "current", _current);
        DumbProjectileBehavior.WritePoint(writer, "destination", _destination);
        writer.Int("pulses", _pulses);
        writer.Int("nextPulse", _nextPulse);
        writer.Int("scorches", _scorches);
        writer.Int("nextScorch", _nextScorch);
        writer.Int("nextLaunchFx", _nextLaunchFx);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        Reset();
        if (!reader.Has("startAttack")) return;
        _startAttack = reader.Int("startAttack");
        _startDecay = reader.Int("startDecay");
        _decayStart = reader.Int("decayStart");
        var beam = reader.Int("beam");
        if (beam < 0 || beam > (int)Beam.Dead) throw new InvalidDataException("Uplink beam state out of range.");
        _beam = (Beam)beam;
        _manual = reader.Bool("manual");
        _initial = DumbProjectileBehavior.ReadPoint(reader, "initial");
        _current = DumbProjectileBehavior.ReadPoint(reader, "current");
        _destination = DumbProjectileBehavior.ReadPoint(reader, "destination");
        _pulses = checked((int)reader.Int("pulses"));
        _nextPulse = reader.Int("nextPulse");
        _scorches = reader.Int("scorches");
        _nextScorch = reader.Int("nextScorch");
        _nextLaunchFx = reader.Int("nextLaunchFx");
    }
}

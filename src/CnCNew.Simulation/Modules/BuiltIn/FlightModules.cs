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

    /// <summary>The corner of the map farthest from a point (<c>TerrainLogic::findFarthestEdgePoint</c>): the far side in both axes; the point itself without a terrain.</summary>
    public static (Fixed X, Fixed Y) FarthestEdgePoint(IWorldServices world, Fixed x, Fixed y)
    {
        if (world.Terrain is not { } terrain) return (x, y);
        return (x < terrain.ExtentX / Fixed.FromInt(2) ? terrain.ExtentX : Fixed.Zero, y < terrain.ExtentY / Fixed.FromInt(2) ? terrain.ExtentY : Fixed.Zero);
    }

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
    public static JetSlowDeathData Parse(RatedModuleConfiguration configuration) => new(
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
    /// <summary>The speed of the fall per logic frame of the original, negative.</summary>
    private Fixed _fallSpeed;
    /// <summary>The step of the running frame of the fall, flown in shares per tick; empty at 30 Hz.</summary>
    private FlightFrameStep _fall;

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
        // SlowDeathBehavior.cpp, SlowDeathBehavior::beginSlowDeath: GameLogicRandomValue(0, m_sinkDelayVariance) / (0, m_destructionDelayVariance) –
        // both drawn in frames of the original, then scaled; the destruction is at least one frame away.
        _ = data.Base.SinkDelayTicks + world.Time.DrawFrames(world.LogicRandom, 0, data.Base.SinkDelayVarianceTicks);
        var destruction = data.Base.DestructionDelayTicks + world.Time.DrawFrames(world.LogicRandom, 0, data.Base.DestructionDelayVarianceTicks);
        _destructionTick = world.Tick + world.Time.AtLeastOneFrame(destruction);
        _deathTick = world.Tick;
        _groundTick = 0;
        _fallSpeed = Fixed.Zero;
        Entity.HasMoveTarget = false;
        Effects(world, data.FxInitialDeath, data.OclInitialDeath);
        _fall.Cancel();
        // The wreck falls from where it is: what its AI had left of a frame is dropped.
        _fall.Publish(world.Time, world.Tick, Entity);
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public long Update(IWorldServices world)
    {
        if (!_activated) return SimulationTiming.SleepForever;
        if (world.Tick >= _destructionTick) world.RequestDestroy(Entity.Id);
        if (_groundTick == 0)
        {
            // The fall is the 30 Hz step (the speed of the fall grows once per logic frame of the original and is a value per frame), flown in shares per tick.
            if (!_fall.Continue(Entity))
            {
                var stepX = FixedTrig.Cos(Entity.Angle) * FlightFrames.PerFrame(world.Time, Entity.Speed);
                var stepY = FixedTrig.Sin(Entity.Angle) * FlightFrames.PerFrame(world.Time, Entity.Speed);
                Entity.X += stepX;
                Entity.Y += stepY;
                _fallSpeed += FlightFrames.GravityPerFrameSquared(world.Templates) * data.FallHowFast;
                Entity.Z += _fallSpeed;
                var ground = FlightModules.GroundOrDefault(world, Entity.X, Entity.Y, Fixed.Zero);
                if (Entity.Z <= ground)
                {
                    Entity.Z = ground;
                    Entity.Speed = Fixed.Zero;
                    _groundTick = world.Tick;
                    Effects(world, data.FxHitGround, data.OclHitGround);
                }
                else _fall.Begin(world.Time, Entity, stepX, stepY, _fallSpeed, Fixed.Zero);
            }
            _fall.Publish(world.Time, world.Tick, Entity);
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
        _fall.Describe(writer, "fallStep");
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _activated = reader.Bool("activated");
        _destructionTick = reader.Int("destructionTick");
        _deathTick = reader.Int("deathTick");
        _groundTick = reader.Int("groundTick");
        _fallSpeed = reader.Fixed("fallSpeed");
        _fall.Load(reader, "fallStep");
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
    public static CountermeasuresData Parse(RatedModuleConfiguration configuration, UpgradeCatalog upgrades) => new(
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
    public bool WouldUpgrade(UpgradeMask keyMask) => _mux.WouldUpgrade(keyMask);

    /// <summary><c>CountermeasuresBehavior::reloadCountermeasures</c>: <c>JetAIUpdate</c> restocks the flares the moment the aircraft has landed (<c>TO_PARKING</c> taxi).</summary>
    public void ReloadAtAirfield()
    {
        _available = checked(data.NumberOfVolleys * data.VolleySize);
        _reloadTick = 0;
        _nextVolleyTick = 0;
    }

    public void ReportMissile(IWorldServices world, Entity missile)
    {
        _incoming++;
        if (_available + _flares.Count <= 0) return;
        if (world.LogicRandom.Range(Fixed.Zero, Fixed.One) >= data.EvasionRate) return;
        var projectile = missile.ModuleArray.OfType<MissileAIUpdate>().FirstOrDefault();
        if (projectile is null) return;
        projectile.SetFramesTillCountermeasureDiversionOccurs(world, data.MissileDecoyTicks);
        _diverted++;
        if (_flares.Count == 0 && _reactionTick == 0) _reactionTick = world.Tick + world.Time.AtLeastOneFrame(data.ReactionTicks);
    }

    public EntityId CalculateCountermeasureToDivertTo(IWorldServices world, Entity victim)
    {
        Fixed? DistanceOf(EntityId id)
        {
            if (world.FindEntity(id) is not { } flare) return null;
            var dx = flare.X - victim.X;
            var dy = flare.Y - victim.Y;
            return dx * dx + dy * dy;
        }
        return PickFlare([.._flares.Select(flare => flare.Id)], data.VolleySize, DistanceOf);
    }

    /// <summary>
    /// <c>CountermeasuresBehavior::calculateCountermeasureToDivertTo</c>, the walk as the original makes it (F-46): from the newest flare backwards, at most
    /// <c>max(VolleySize, 1)</c> passes, stepping back only past a flare that no longer exists (<c>--it</c> in its <c>else</c>) - an existing flare is weighed again on the next
    /// pass, so the newest flare that still exists is the one, whatever the distances of the older ones. <paramref name="distance"/> is null for a flare that is gone; the
    /// walk stops at the oldest flare instead of reading before it.
    /// </summary>
    public static EntityId PickFlare(IReadOnlyList<EntityId> flares, int volleySize, Func<EntityId, Fixed?> distance)
    {
        var passes = Math.Max(volleySize, 1);
        var best = EntityId.Invalid;
        var bestDistance = Fixed.MaxValue;
        var index = flares.Count - 1;
        while (index >= 0 && passes-- > 0)
        {
            if (distance(flares[index]) is { } found)
            {
                if (found < bestDistance)
                {
                    bestDistance = found;
                    best = flares[index];
                }
            }
            else index--;
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
            var velocityX = flare.VelocityX;
            var velocityY = flare.VelocityY;
            // The braking is the 30 Hz step: it takes the braking of a whole logic frame of the original off the speed once per frame, counted from the flare's first move
            // (the tick after it was launched); in between the flare keeps its speed and moves every tick.
            if (world.Time.IsOriginalFrameBoundary(now - flare.AddedTick - TickOrder.Next))
            {
                var braking = FlightFrames.PerFrame(world.Time, locomotor?.Braking ?? Fixed.Zero);
                var speed = Fixed.Sqrt(velocityX * velocityX + velocityY * velocityY);
                var scale = speed <= braking || speed == Fixed.Zero ? Fixed.Zero : (speed - braking) / speed;
                velocityX *= scale;
                velocityY *= scale;
                _flares[index] = flare with { VelocityX = velocityX, VelocityY = velocityY };
            }
            entity.X += velocityX;
            entity.Y += velocityY;
            var ground = FlightModules.GroundOrDefault(world, entity.X, entity.Y, Fixed.Zero);
            var sink = locomotor?.SpeedLimitZ ?? Fixed.Zero;
            entity.Z = Fixed.Max(ground, entity.Z - sink);
            var stride = world.Time.TicksPerOriginalFrame;
            if (stride > 1)
            {
                // A missile that follows the flare reads it where its frame ends (Entity.FrameEnd): the ticks of the flare's frame still to come at the speed it keeps for that frame.
                var left = stride - 1 - (now - flare.AddedTick - TickOrder.Next) % stride;
                entity.SetAhead(now, velocityX * left, velocityY * left, Fixed.Max(ground - entity.Z, -(sink * left)));
            }
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
            var speed = Entity.Speed * world.Time.TicksPerSecondFixed < Fixed.One ? world.Time.PerSecondToPerTick(-Fixed.FromInt(10)) : Entity.Speed;
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
    public static ParachuteData Parse(RatedModuleConfiguration configuration) => new(
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
    /// <summary>The speed of the free fall and then of the sinking, per logic frame of the original, negative.</summary>
    private Fixed _fallSpeed;
    /// <summary>The step of the running frame of the descent, flown in shares per tick; empty at 30 Hz.</summary>
    private FlightFrameStep _descent;
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
            // The first look of the original comes one logic frame after the chute appeared (the next tick at 30 Hz); the descent itself starts with the next tick.
            if (world.Tick < Entity.SpawnTick + world.Time.TicksPerOriginalFrame) return SimulationTiming.SleepNone;
            // "If we have lost our passenger for whatever reason, die early."
            world.Kill(Entity.Id);
            return SimulationTiming.SleepForever;
        }

        // The descent is the 30 Hz step, decided once per logic frame of the original (the speed of the fall and of the sinking is a value per frame, the landing spot is reached within
        // one frame of travel) and flown in shares per tick; between two decisions only the rest of the frame is flown.
        if (_descent.Continue(Entity))
        {
            _descent.Publish(world.Time, world.Tick, Entity);
            world.MarkDirty(Entity);
            return Carry(world, rider);
        }
        var ground = FlightModules.GroundOrDefault(world, Entity.X, Entity.Y, Fixed.Zero);
        if (!_started)
        {
            _started = true;
            _startZ = Entity.Z;
            if (_startZ - ground < Fixed.FromInt(2) * data.OpenDistance) _startZ = ground + Fixed.FromInt(2) * data.OpenDistance;
        }
        var locomotor = Entity.Template.Locomotor;
        var before = (Entity.X, Entity.Y, Entity.Z);
        if (!_opened)
        {
            _fallSpeed += FlightFrames.GravityPerFrameSquared(world.Templates);
            Entity.Z += _fallSpeed;
            if ((_startZ - Entity.Z).Abs() >= data.OpenDistance)
            {
                _opened = true;
                _fallSpeed = -FlightFrames.PerFrame(world.Time, locomotor?.SpeedLimitZ ?? Fixed.Zero);
            }
        }
        else
        {
            Entity.Z += _fallSpeed;
            if (_hasOverride && locomotor is not null)
            {
                var speed = FlightFrames.PerFrame(world.Time, locomotor.Speed);
                var dx = _overrideX - Entity.X;
                var dy = _overrideY - Entity.Y;
                var distance = Fixed.Sqrt(dx * dx + dy * dy);
                if (distance <= speed) { Entity.X = _overrideX; Entity.Y = _overrideY; }
                else if (distance > Fixed.Zero)
                {
                    Entity.X += dx * speed / distance;
                    Entity.Y += dy * speed / distance;
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
        _descent.Begin(world.Time, Entity, Entity.X - before.X, Entity.Y - before.Y, Entity.Z - before.Z, Fixed.Zero);
        _descent.Publish(world.Time, world.Tick, Entity);
        return Carry(world, rider);
    }

    /// <summary>The rider hangs under the chute, every tick.</summary>
    private long Carry(IWorldServices world, Entity rider)
    {
        rider.X = Entity.X;
        rider.Y = Entity.Y;
        rider.Z = Entity.Z;
        // The rider is read where the chute's frame ends, too.
        if (world.Time.TicksPerOriginalFrame > 1) rider.SetAhead(world.Tick, Entity.AheadX, Entity.AheadY, Entity.AheadZ);
        world.MarkDirty(rider);
        return SimulationTiming.SleepNone;
    }

    /// <summary><c>onCollide(NULL)</c> + <c>onRemoving</c>: the rider steps off, goes idle, and the chute is killed so that its slow death runs.</summary>
    private void Land(IWorldServices world, Entity rider)
    {
        Remove(world, rider, true);
        rider.Z = Entity.Z;
        if (world.Time.TicksPerOriginalFrame > 1) rider.SetAhead(world.Tick, Fixed.Zero, Fixed.Zero, Fixed.Zero);
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
        _descent.Describe(writer, "descentStep");
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
        _descent.Load(reader, "descentStep");
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
    public static DeliverPayloadData Parse(RatedModuleConfiguration configuration) => new(
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
/// What a creation-list <c>DeliverPayload</c> nugget (or the scripted reinforcement path) tells a transport: where to fly (<paramref name="MoveToX"/>/<paramref name="MoveToY"/>), where the
/// payload is meant to land (<paramref name="TargetX"/>/<paramref name="TargetY"/>), the run's data and a delay before the start (<c>DelayDeliveryMax</c>, the transport is disabled until then).
/// </summary>
public readonly record struct DeliveryOrder(Fixed MoveToX, Fixed MoveToY, Fixed TargetX, Fixed TargetY, DeliveryRun Run, long DelayTicks);

/// <summary>
/// <c>DeliverPayloadAIUpdate.cpp</c>: the plane flies to the delivery point (<c>ApproachState</c>, a move order at the locomotor's <c>PreferredHeight</c>), and once it is within
/// <c>DeliveryDistance</c> of the target (<c>PreOpenDistance</c> more while it is still flying towards it, <c>isCloseEnoughToTarget</c>) opens its doors and, every <c>DropDelay</c> ticks after
/// <c>DoorDelay</c>, delivers (<c>DeliveringState</c>): the first rider leaves (put at the plane's position plus <c>DropOffset</c> and the random <c>DropVariance</c>; a parachute it sits
/// in is told to land at the target with <c>ParachuteDirectly</c>; a bomb falls under gravity, <see cref="PhysicsBehavior"/>), or with <c>FireWeapon</c> the plane fires its weapon at the target
/// and the rider is deleted, or <c>VisibleItemsDroppedPerInterval</c> visible payload objects are created (<c>VisiblePayloadTemplateName</c>; a missile is launched at the target with
/// <c>VisiblePayloadWeaponTemplate</c>, a bomb falls). When nothing is left it flies on in a straight line until it leaves the map (<c>HeadOffMapState</c>, or vanishes at once with
/// <c>SelfDestructObject</c>) and is deleted (<c>CleanUpState</c>). A plane that overflew the target (<c>ConsiderNewApproachState</c>) turns around at <c>2.2 ·</c> its minimum turn
/// radius for up to <c>MaxAttempts</c> new passes, and one that left the map while doing so is hidden for the time a turn takes and re-enters at the nearest edge
/// (<c>RecoverFromOffMapState</c>). From head-off-map on it no longer answers movement orders. A bomber with <c>DiveStartDistance</c> strafes: while it is between the dive start and end
/// distance it fires its weapon at a point sliding along its heading over the target (<c>StrafeLength</c> is the weapon's own reach and not read). Flight is kinematic through the
/// ordinary move machine of <see cref="AIUpdateInterface"/>; the dive's altitude change, <c>InheritTransportVelocity</c> (a physics push), the exit pitch, the drop bones and the delivery decal are
/// not modelled, nor is the hack that kills a plane that was turned off its exit course. <see cref="DeliverViaModuleData"/> is <c>deliverPayloadViaModuleData</c> (scripted reinforcements),
/// <see cref="DeliverPayload"/> is <c>deliverPayload</c> (creation lists).
/// </summary>
public sealed class DeliverPayloadAIUpdate(DeliverPayloadData data) : AIUpdateInterface(data.Base)
{
    private enum Phase : byte { None, Approach, Delivering, ConsiderNewApproach, RecoverFromOffMap, HeadOffMap, Delay }

    private enum Dive : byte { Post, Pre, Diving }

    private Phase _phase;
    private DeliveryRun? _run;
    private Fixed _moveToX;
    private Fixed _moveToY;
    private Fixed _targetX;
    private Fixed _targetY;
    private bool _freeToExit;
    private bool _accepting = true;
    private long _dropDelayLeft;
    private int _attempts;
    private long _reentryTick;
    private long _startTick;
    private int _visibleDelivered;
    private Dive _dive;
    private Fixed _previousDistance2;

    public override string ModuleName => "DeliverPayloadAIUpdate";

    /// <summary><c>getAiFreeToExit</c>: the doors are open and the plane is alive.</summary>
    public bool FreeToExit => _freeToExit && !Entity.EffectivelyDead;

    /// <summary>The plane is on its delivery run.</summary>
    public bool Delivering => _phase != Phase.None;

    /// <summary><c>DeliveringState</c>: the doors are opening or open (<c>onEnter</c> sets <c>MODELCONDITION_DOOR_1_OPENING</c>, <c>onExit</c> swaps it for <c>DOOR_1_CLOSING</c>).</summary>
    public bool DoorsOpen => _phase == Phase.Delivering;

    /// <summary><c>getPutInContainerTemplateViaModuleData</c>: the template riders are packed into (the parachute), empty for none.</summary>
    public string PutInContainer => data.PutInContainer;

    public override void OnSpawn(IWorldServices world)
    {
        base.OnSpawn(world);
        NextWake = SimulationTiming.SleepForever;
    }

    /// <summary>Once the plane heads off the map it ignores every order (<c>m_acceptingCommands</c>).</summary>
    public override void OrderMove(IWorldServices world, Fixed x, Fixed y, bool fromPlayer, bool attackMove = false)
    {
        if (_accepting) base.OrderMove(world, x, y, fromPlayer, attackMove);
    }

    /// <summary><c>deliverPayloadViaModuleData</c>: fly to (<paramref name="x"/>, <paramref name="y"/>), drop everything inside, leave. The run is built from the module's own keys.</summary>
    public void DeliverViaModuleData(IWorldServices world, Fixed x, Fixed y)
    {
        var run = new DeliveryRun(data.DeliveryDistance, Fixed.Zero, data.MaxAttempts, data.DropDelayTicks, data.DropOffset.X, data.DropOffset.Y, data.DropOffset.Z,
            data.DropVariance.X, data.DropVariance.Y, data.DropVariance.Z, false, 0, 0, string.Empty, string.Empty, false, false, Fixed.Zero, Fixed.Zero, false, Fixed.Zero);
        DeliverPayload(world, new DeliveryOrder(x, y, x, y, run, 0));
    }

    /// <summary><c>deliverPayload(moveToPos, targetPos, data)</c>: the creation list hands the plane its run.</summary>
    public void DeliverPayload(IWorldServices world, in DeliveryOrder order)
    {
        _run = order.Run;
        _moveToX = order.MoveToX;
        _moveToY = order.MoveToY;
        _targetX = order.TargetX;
        _targetY = order.TargetY;
        _attempts = 0;
        _visibleDelivered = 0;
        _freeToExit = false;
        _accepting = true;
        _previousDistance2 = Fixed.Zero;
        _dive = order.Run.DiveStartDistance <= Fixed.Zero ? Dive.Post : Dive.Pre;
        if (order.DelayTicks > 0)
        {
            _phase = Phase.Delay;
            _startTick = world.Tick + order.DelayTicks;
        }
        else StartPhase(world, Phase.Approach);
        world.WakeAt(Entity, this, world.Tick);
    }

    public override long Update(IWorldServices world)
    {
        if (_phase != Phase.None)
        {
            if (Entity.EffectivelyDead) _phase = Phase.None;
            else
            {
                if (_phase != Phase.Delay) Strafe(world);
                RunPhase(world);
                if (_phase != Phase.Delay) ClimbToHeight(world);
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
                base.OrderMove(world, _moveToX, _moveToY, false);
                break;
            case Phase.Delivering:
                // DeliverPayloadAIUpdate.cpp: m_dropDelayLeft = getDoorDelay(), counted down once per update – DoorDelay + 1 frames of the original until the first drop.
                _dropDelayLeft = world.Time.CountdownReload(data.DoorDelayTicks);
                break;
            case Phase.ConsiderNewApproach:
                ConsiderNewApproach(world);
                break;
            case Phase.RecoverFromOffMap:
                StopInternalMove();
                Entity.Speed = Fixed.Zero;
                _reentryTick = world.Tick + TurnTicks(world);
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
            case Phase.Delay:
                if (world.Tick >= _startTick) StartPhase(world, Phase.Approach);
                break;
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
                Entity.Angle = FixedTrig.Atan2(_moveToY - edgeY, _moveToX - edgeX);
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

    /// <summary><c>isCloseEnoughToTarget</c>: inside <c>DeliveryDistance</c> of the target, plus <c>PreOpenDistance</c> while the plane is flying towards it.</summary>
    private bool CloseEnoughToTarget()
    {
        var run = _run!;
        var dx = Entity.X - _targetX;
        var dy = Entity.Y - _targetY;
        var distance2 = dx * dx + dy * dy;
        var inbound = _previousDistance2 > distance2;
        _previousDistance2 = distance2;
        var allowed = inbound ? run.DeliveryDistance + run.PreOpenDistance : run.DeliveryDistance;
        return SquaredReach.Of(allowed).ContainsStrictly(distance2);
    }

    /// <summary>The dive logic that runs whatever the state (<c>DeliverPayloadAIUpdate::update</c>): from the dive start the plane strafes, until it is inside the dive end distance.</summary>
    private void Strafe(IWorldServices world)
    {
        if (_dive == Dive.Post || _run is not { } run) return;
        var dx = _targetX - Entity.X;
        var dy = _targetY - Entity.Y;
        if (_dive == Dive.Pre)
        {
            if (SquaredReach.Of(run.DiveStartDistance).Contains(dx * dx + dy * dy)) _dive = Dive.Diving;
            return;
        }
        var dz = FlightModules.GroundOrDefault(world, _targetX, _targetY, Fixed.Zero) - Entity.Z;
        var distance = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance <= run.DiveEndDistance) { _dive = Dive.Post; return; }
        if (!run.Strafes || SimulationWorld.ActiveWeaponIndex(Entity) < 0 || run.DiveStartDistance <= run.DiveEndDistance) return;
        // strafePoint = target - 0.33 * v + v with v = heading * diveRatio * 100 (the plane flies on at constant speed, so the heading is the velocity direction).
        var ratio = (run.DiveStartDistance - distance) / (run.DiveStartDistance - run.DiveEndDistance);
        var reach = ratio * Fixed.FromInt(100) * Fixed.FromDecimal(0.67m);
        var x = _targetX + FixedTrig.Cos(Entity.Angle) * reach;
        var y = _targetY + FixedTrig.Sin(Entity.Angle) * reach;
        world.FireCurrentWeaponAtPosition(Entity, x, y, FlightModules.GroundOrDefault(world, x, y, Fixed.Zero));
    }

    private void RunDelivering(IWorldServices world)
    {
        var run = _run!;
        if (_dropDelayLeft > 0)
        {
            _dropDelayLeft--;
            return;
        }
        _freeToExit = true;
        _dropDelayLeft = world.Time.CountdownReload(run.DropDelayTicks); // m_dropDelayLeft = getDropDelay(): a drop every DropDelay + 1 frames of the original
        if (!CloseEnoughToTarget())
        {
            _freeToExit = false;
            StartPhase(world, Phase.ConsiderNewApproach);
            return;
        }
        var item = Entity.Container is { } contain ? contain.Contained.Select(world.FindEntity).FirstOrDefault(rider => rider is not null) : null;
        if (item is null && _visibleDelivered >= run.VisibleNumBones)
        {
            _freeToExit = false;
            StartPhase(world, Phase.HeadOffMap);
            return;
        }
        if (item is not null) DropContained(world, Entity.Container!, item, run);
        if (_visibleDelivered < run.VisibleNumBones) DropVisible(world, run);
    }

    private void DropContained(IWorldServices world, IContainerModule contain, Entity item, DeliveryRun run)
    {
        if (run.FireWeapon)
        {
            // owner->fireCurrentWeapon(target + dropOffset); the item is only the weapon's stand-in and is deleted.
            world.FireCurrentWeaponAtPosition(Entity, _targetX + run.DropOffsetX, _targetY + run.DropOffsetY, FlightModules.GroundOrDefault(world, _targetX, _targetY, Fixed.Zero) + run.DropOffsetZ);
            contain.Remove(world, item, true);
            world.RequestDestroy(item.Id);
            return;
        }
        contain.Remove(world, item, true);
        world.Emit(SimEventKind.EntityUncontained, item.Id, Entity.Id, string.Empty, 0);
        var x = Entity.X + run.DropOffsetX;
        var y = Entity.Y + run.DropOffsetY;
        var z = Entity.Z + run.DropOffsetZ;
        if (run.DropVarianceX > Fixed.Zero) x += world.LogicRandom.Range(-run.DropVarianceX, run.DropVarianceX);
        if (run.DropVarianceY > Fixed.Zero) y += world.LogicRandom.Range(-run.DropVarianceY, run.DropVarianceY);
        if (run.DropVarianceZ > Fixed.Zero) z += world.LogicRandom.Range(-run.DropVarianceZ, run.DropVarianceZ);
        item.X = x;
        item.Y = y;
        item.Z = z;
        item.HasMoveTarget = false;
        if (run.ParachuteDirectly && item.Modules.OfType<IOverrideDestination>().FirstOrDefault() is { } parachute) parachute.SetOverrideDestination(_targetX, _targetY);
        // A bomb has no locomotor and falls: PhysicsBehavior takes over (the reference leaves it to the physics module, too).
        if (item.Modules.OfType<PhysicsBehavior>().FirstOrDefault() is { } physics && item.Template.Locomotor is null) physics.StartFall(world, Fixed.Zero, Fixed.Zero);
        // "smthu->SetTargetPosition(*ai->getMoveToPos())": a smart bomb (the MOAB) steers towards the point the plane was sent to.
        if (item.Modules.OfType<SmartBombTargetHomingUpdate>().FirstOrDefault() is { } homing)
            homing.SetTargetPosition(world, _moveToX, _moveToY, FlightModules.GroundOrDefault(world, _moveToX, _moveToY, Fixed.Zero));
        world.MarkDirty(item);
    }

    /// <summary>The visible payload: each of <c>VisibleItemsDroppedPerInterval</c> items is created at the plane; a missile is fired at the target, anything else falls.</summary>
    private void DropVisible(IWorldServices world, DeliveryRun run)
    {
        var attempts = run.VisibleItemsDroppedPerInterval;
        while (attempts > 0 && _visibleDelivered < run.VisibleNumBones)
        {
            attempts--;
            _visibleDelivered++;
            if (run.VisiblePayloadTemplate.Length == 0) continue;
            if (!world.Templates.TryGet(run.VisiblePayloadTemplate, out var template) || !template.IsInstantiable)
            {
                world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, run.VisiblePayloadTemplate, CreationSkipReasons.TemplateNotInstantiable);
                continue;
            }
            var targetZ = FlightModules.GroundOrDefault(world, _targetX, _targetY, Fixed.Zero);
            if (template.Modules.Any(module => module.Name is "MissileAIUpdate" or "DumbProjectileBehavior"))
            {
                if (run.VisiblePayloadWeapon.Length == 0 || !world.Weapons.TryGet(run.VisiblePayloadWeapon, out var weapon))
                {
                    world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, run.VisiblePayloadWeapon.Length > 0 ? run.VisiblePayloadWeapon : run.VisiblePayloadTemplate, CreationSkipReasons.UnknownTemplate);
                    continue;
                }
                world.LaunchProjectileObject(Entity, template.Index, weapon, Entity.X, Entity.Y, Entity.Z, _targetX, _targetY, targetZ);
            }
            else world.RequestSpawn(template.Index, Entity.Owner, Entity.X, Entity.Y, Entity.Z, Entity.Angle);
        }
    }

    /// <summary>The reference estimates the turn radius from the speed and the turn rate and flies on <c>2.2 ·</c> radii before turning back.</summary>
    private Fixed MinTurnRadius()
    {
        var locomotor = Entity.Template.Locomotor;
        return locomotor is { TurnRatePerTick.Raw: > 0 } ? locomotor.Speed / locomotor.TurnRatePerTick : Fixed.FromInt(999999);
    }

    /// <summary>
    /// <c>DeliverPayloadAIUpdate::calcMinTurnRadius</c> (<c>timeToTravelThatDist</c>): the time to fly one turn radius, rounded up to whole logic frames of the original, at least one.
    /// </summary>
    private long TurnTicks(IWorldServices world)
    {
        var locomotor = Entity.Template.Locomotor;
        return locomotor is { Speed.Raw: > 0 } ? world.Time.AtLeastOneFrame(world.Time.CeilingToFrame((MinTurnRadius() / locomotor.Speed).Ceiling())) : world.Time.TicksPerOriginalFrame;
    }

    private void ConsiderNewApproach(IWorldServices world)
    {
        _attempts++;
        if (_attempts > _run!.MaxAttempts)
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
        if (_run is { SelfDestructObject: true })
        {
            // "this makes delivery unit pop away like a bubble"
            _phase = Phase.None;
            world.RequestDestroy(Entity.Id);
            return;
        }
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
        writer.Fixed("deliverMoveX", _moveToX);
        writer.Fixed("deliverMoveY", _moveToY);
        writer.Bool("freeToExit", _freeToExit);
        writer.Bool("accepting", _accepting);
        writer.Int("dropDelayLeft", _dropDelayLeft);
        writer.Int("attempts", _attempts);
        writer.Int("reentryTick", _reentryTick);
        writer.Int("deliverStart", _startTick);
        writer.Int("visibleDelivered", _visibleDelivered);
        writer.Int("diveState", (int)_dive);
        writer.Fixed("previousDistance2", _previousDistance2);
        if (_run is { } run) run.Describe(writer);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        if (!reader.Has("deliverPhase")) return;
        var phase = reader.Int("deliverPhase");
        if (phase < 0 || phase > (int)Phase.Delay) throw new InvalidDataException("Delivery phase out of range.");
        _phase = (Phase)phase;
        _targetX = reader.Fixed("deliverX");
        _targetY = reader.Fixed("deliverY");
        _moveToX = reader.Fixed("deliverMoveX");
        _moveToY = reader.Fixed("deliverMoveY");
        _freeToExit = reader.Bool("freeToExit");
        _accepting = reader.Bool("accepting");
        _dropDelayLeft = reader.Int("dropDelayLeft");
        _attempts = checked((int)reader.Int("attempts"));
        _reentryTick = reader.Int("reentryTick");
        _startTick = reader.Int("deliverStart");
        _visibleDelivered = checked((int)reader.Int("visibleDelivered"));
        var dive = reader.Int("diveState");
        if (dive < 0 || dive > (int)Dive.Diving) throw new InvalidDataException("Dive state out of range.");
        _dive = (Dive)dive;
        _previousDistance2 = reader.Fixed("previousDistance2");
        _run = reader.Has("runDistance") ? DeliveryRun.Load(reader) : null;
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
    Fixed ManualDrivingSpeed, Fixed ManualFastDrivingSpeed = default, long BeginChargeTicks = 0, long RaiseAntennaTicks = 0, long ReadyDelayTicks = 0) : ModuleData
{
    public static ParticleUplinkData Parse(RatedModuleConfiguration configuration)
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
            ModuleConfigReader.Real(configuration, "DamageRadiusScalar", Fixed.Zero), ModuleConfigReader.Real(configuration, "ManualDrivingSpeed", Fixed.Zero) / configuration.Time.TicksPerSecondFixed,
            ModuleConfigReader.Real(configuration, "ManualFastDrivingSpeed", Fixed.Zero) / configuration.Time.TicksPerSecondFixed,
            ModuleConfigReader.Ticks(configuration, "BeginChargeTime", 0), ModuleConfigReader.Ticks(configuration, "RaiseAntennaTime", 0), ModuleConfigReader.Ticks(configuration, "ReadyDelayTime", 0));
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
/// sounds and the scorch marks. A script can fire it along a waypoint path (<see cref="PrepareScriptedPath"/>): the beam then starts at the first waypoint and drives from waypoint to
/// waypoint at <c>ManualFastDrivingSpeed</c> (the path is followed in order; the reference picks a random link at a fork) and stays at the last one.
/// </summary>
public sealed class ParticleUplinkCannonUpdate(ParticleUplinkData data) : SimModule, IUpdateModule, ISpecialPowerHandler
{
    private enum Beam : byte { None, Born, Decaying, Dead }

    /// <summary>Own value: a scripted waypoint path waits this many logic frames of the original for the use of the power that follows it.</summary>
    private const long PendingPathFrames = 3;

    private long _nextWake = -1;
    private long _startAttack;
    private long _startDecay;
    private long _decayStart;
    private Beam _beam;
    private bool _manual;
    private List<Point3>? _pendingPath;
    private long _pendingPathTick;
    private List<Point3>? _path;
    private int _pathIndex;
    private Point3 _initial;
    private Point3 _current;
    private Point3 _destination;
    private int _pulses;
    private long _nextPulse;
    private long _scorches;
    private long _nextScorch;
    private long _nextLaunchFx;
    // The rest of the driven beam's step of the running logic frame of the original: ticks left and the share per tick (nothing at 30 Hz).
    private int _driveLeft;
    private Fixed _driveX;
    private Fixed _driveY;

    public override string ModuleName => "ParticleUplinkCannonUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary><c>isPowerCurrentlyInUse</c>: an order is running (the beam is on its way, burning or decaying).</summary>
    public bool InUse => _startAttack != 0;

    /// <summary>Where the beam burns right now (valid while <see cref="InUse"/> and the beam is born).</summary>
    public Point3 BeamPosition => _current;

    /// <summary><c>PUCStatus</c>: the logical stage of the cannon (<c>setLogicalStatus</c>); the reference derives the antenna stages from the clock alone, so they are read here and not stored.</summary>
    public enum UplinkStatus : byte { Idle, Charging, Preparing, AlmostReady, ReadyToFire, Firing, PostFire, Packing }

    /// <summary>
    /// The stage <c>ParticleUplinkCannonUpdate::update</c> would put the cannon into at <paramref name="world"/>'s tick, without changing anything: a beam in progress is firing, then post-firing while it
    /// decays and packing until the beam is gone; otherwise the clock decides from the power's ready tick backwards (<c>ReadyDelayTime</c> almost ready, <c>RaiseAntennaTime</c> before that preparing,
    /// <c>BeginChargeTime</c> before that charging). A sold, unfinished or destroyed cannon is idle. The reference's reset case (a ready cannon whose timer was reset packs up) is not read: it is idle.
    /// <c>UNPACKING</c> belongs to <see cref="UplinkStatus.Preparing"/>, <c>DEPLOYED</c> to almost ready through post-fire and <c>PACKING</c> to <see cref="UplinkStatus.Packing"/>.
    /// </summary>
    public UplinkStatus StatusAt(IWorldServices world)
    {
        if (Entity.HasStatus(ObjectStatusBits.Sold) || Entity.HasStatus(ObjectStatusBits.UnderConstruction) || Entity.EffectivelyDead) return UplinkStatus.Idle;
        var now = world.Tick;
        if (_startAttack != 0 && _startAttack <= now)
        {
            if (_startDecay + data.WidthGrowTicks <= now) return UplinkStatus.Packing;
            return _startDecay <= now ? UplinkStatus.PostFire : UplinkStatus.Firing;
        }
        if (!world.SpecialPowers.TryGet(data.PowerName, out var info) || SpecialPowerRouting.FindModule(Entity, info.Index) is not SpecialPowerModuleBase power) return UplinkStatus.Idle;
        var readyToFire = power.IsReady(world) ? now : power.AvailableOnFrame(world);
        var almostReady = readyToFire - data.ReadyDelayTicks;
        var raiseAntenna = almostReady - data.RaiseAntennaTicks;
        var beginCharge = raiseAntenna - data.BeginChargeTicks;
        if (readyToFire <= now) return UplinkStatus.ReadyToFire;
        if (almostReady <= now) return UplinkStatus.AlmostReady;
        if (raiseAntenna <= now) return UplinkStatus.Preparing;
        return beginCharge <= now ? UplinkStatus.Charging : UplinkStatus.Idle;
    }

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public bool Handles(IWorldServices world, int powerIndex) => world.SpecialPowers.TryGet(data.PowerName, out var info) && info.Index == powerIndex;

    /// <summary>
    /// <c>initiateIntentToDoSpecialPower(…, way, COMMAND_FIRED_BY_SCRIPT)</c>: the next use (the command a script is about to submit) follows these waypoints instead of sweeping; the offer expires
    /// after a few ticks if no use arrives.
    /// </summary>
    public void PrepareScriptedPath(IWorldServices world, IReadOnlyList<Point3> waypoints)
    {
        _pendingPath = [..waypoints];
        _pendingPathTick = world.Tick;
        world.MarkDirty(Entity);
    }

    public void Initiate(IWorldServices world, int powerIndex, in SpecialPowerTarget target)
    {
        _path = null;
        _pathIndex = 0;
        if (_pendingPath is { Count: > 0 } offered && world.Tick - _pendingPathTick <= world.Time.OriginalFrames(PendingPathFrames)) _path = offered;
        _pendingPath = null;
        world.MarkDirty(Entity);
        var aimed = target.Kind == SpecialPowerTargetKind.Object ? world.FindEntity(target.Entity) : null;
        var x = _path is null ? aimed?.X ?? target.X : _path[0].X;
        var y = _path is null ? aimed?.Y ?? target.Y : _path[0].Y;
        _initial = new Point3(x, y, world.GroundHeightAt(x, y));
        _current = _initial;
        _destination = _path is { Count: > 1 } ? _path[1] : _initial;
        _pathIndex = _path is { Count: > 1 } ? 1 : 0;
        // All human players "drive" the beam; a computer-controlled owner (and so a scripted use) lets it sweep; a scripted waypoint path drives it as well.
        _manual = _path is not null || !world.IsComputerPlayer(Entity.Owner);
        _startAttack = Math.Max(world.Tick, 1);
        _startDecay = _startAttack + data.TotalFiringTicks;
        _decayStart = 0;
        _beam = Beam.None;
        _pulses = 0;
        _scorches = 0;
        _driveLeft = 0;
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
                // The n-th share of the beam's life is cut off in frames of the original, then scaled (the same frame at every rate).
                _nextScorch = data.TotalScorchMarks > 0 ? birth + world.Time.OriginalFrames(world.Time.ToOriginalFrames(death - birth) * _scorches / data.TotalScorchMarks) : death + TickOrder.Next;
            }
            if (_nextPulse <= now && data.TotalDamagePulses > 0)
            {
                _pulses++;
                Pulse(world, radius);
                // ParticleUplinkCannonUpdate.cpp, update: m_nextDamagePulseFrame = orbitalBirthFrame + nextFactor * (orbitalDeathFrame - orbitalBirthFrame), in frames of the original.
                _nextPulse = birth + world.Time.OriginalFrames(world.Time.ToOriginalFrames(death - birth) * _pulses / data.TotalDamagePulses);
            }
        }
        if (_startDecay > now && data.BeamLaunchFx.Length > 0 && _nextLaunchFx <= now)
        {
            world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.BeamLaunchFx, 0, Entity.X, Entity.Y, Entity.Z);
            _nextLaunchFx = now + world.Time.AtLeastOneFrame(data.LaunchFxTicks);
        }
        return SimulationTiming.SleepNone;
    }

    private void Reset()
    {
        _startAttack = 0;
        _startDecay = 0;
        _beam = Beam.None;
        _path = null;
        _pathIndex = 0;
        _driveLeft = 0;
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
        else if (_driveLeft > 0)
        {
            // the rest of the step of the running logic frame of the original
            _driveLeft--;
            _current = _current with { X = _current.X + _driveX, Y = _current.Y + _driveY };
        }
        else
        {
            // The drive is the 30 Hz step, decided once per logic frame of the original: the beam covers the travel of a frame, at most the rest of the way to its destination, and
            // heads for the next waypoint from the following frame on. A beam that switched when the rest is one tick of travel would lose less of a frame at every waypoint.
            var dx = _destination.X - _current.X;
            var dy = _destination.Y - _current.Y;
            var distance = Fixed.Sqrt(dx * dx + dy * dy);
            var speed = Fixed.Min(FlightFrames.PerFrame(world.Time, _path is null ? data.ManualDrivingSpeed : data.ManualFastDrivingSpeed), distance);
            if (_path is not null && distance <= speed && _pathIndex + 1 < _path.Count) _destination = _path[++_pathIndex];
            if (distance > Fixed.Zero)
            {
                var stepX = dx * speed / distance;
                var stepY = dy * speed / distance;
                var ticks = world.Time.TicksPerOriginalFrame;
                if (ticks > 1)
                {
                    // flown in equal shares per tick; the first tick takes what the division leaves, so the shares add up to the step
                    _driveLeft = checked((int)(ticks - TickOrder.Next));
                    _driveX = Fixed.FromRaw(stepX.Raw / ticks);
                    _driveY = Fixed.FromRaw(stepY.Raw / ticks);
                    stepX -= _driveX * (long)_driveLeft;
                    stepY -= _driveY * (long)_driveLeft;
                }
                _current = _current with { X = _current.X + stepX, Y = _current.Y + stepY };
            }
        }
        _current = _current with { Z = world.GroundHeightAt(_current.X, _current.Y) };
    }

    /// <summary>One damage pulse: every living object with a body within <paramref name="radius"/> (2D from its centre) takes the pulse's share of <c>TotalFiringTime · DamagePerSecond</c>.</summary>
    private void Pulse(IWorldServices world, Fixed radius)
    {
        var seconds = Fixed.FromInt(data.TotalFiringTicks) / world.Time.TicksPerSecondFixed;
        var amount = seconds * data.DamagePerSecond / Fixed.FromInt(data.TotalDamagePulses);
        var limit = SquaredReach.Of(radius);
        foreach (var entity in world.Entities.OrderBy(value => value.Id.Value).ToArray())
        {
            if (entity.EffectivelyDead || entity.DestroyRequested || entity.Body is null) continue;
            var dx = entity.X - _current.X;
            var dy = entity.Y - _current.Y;
            if (!limit.Contains(dx * dx + dy * dy)) continue;
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
        if (_pendingPath is not null)
        {
            writer.Int("pendingPathTick", _pendingPathTick);
            writer.BeginArray("pendingPath");
            foreach (var point in _pendingPath) { writer.BeginObject(null); DumbProjectileBehavior.WritePoint(writer, "p", point); writer.EndObject(); }
            writer.EndArray();
        }
        if (_startAttack == 0) return;
        writer.Int("startAttack", _startAttack);
        writer.Int("startDecay", _startDecay);
        writer.Int("decayStart", _decayStart);
        writer.Int("beam", (int)_beam);
        writer.Bool("manual", _manual);
        if (_path is not null)
        {
            writer.Int("pathIndex", _pathIndex);
            writer.BeginArray("path");
            foreach (var point in _path) { writer.BeginObject(null); DumbProjectileBehavior.WritePoint(writer, "p", point); writer.EndObject(); }
            writer.EndArray();
        }
        DumbProjectileBehavior.WritePoint(writer, "initial", _initial);
        DumbProjectileBehavior.WritePoint(writer, "current", _current);
        DumbProjectileBehavior.WritePoint(writer, "destination", _destination);
        writer.Int("pulses", _pulses);
        writer.Int("nextPulse", _nextPulse);
        writer.Int("scorches", _scorches);
        writer.Int("nextScorch", _nextScorch);
        writer.Int("nextLaunchFx", _nextLaunchFx);
        if (_driveLeft > 0)
        {
            writer.Int("driveLeft", _driveLeft);
            writer.Fixed("driveX", _driveX);
            writer.Fixed("driveY", _driveY);
        }
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        Reset();
        _pendingPath = null;
        if (reader.Has("pendingPath"))
        {
            _pendingPathTick = reader.Int("pendingPathTick");
            _pendingPath = [];
            foreach (var item in reader.Array("pendingPath")) _pendingPath.Add(DumbProjectileBehavior.ReadPoint(item, "p"));
        }
        if (!reader.Has("startAttack")) return;
        _startAttack = reader.Int("startAttack");
        _startDecay = reader.Int("startDecay");
        _decayStart = reader.Int("decayStart");
        var beam = reader.Int("beam");
        if (beam < 0 || beam > (int)Beam.Dead) throw new InvalidDataException("Uplink beam state out of range.");
        _beam = (Beam)beam;
        _manual = reader.Bool("manual");
        if (reader.Has("path"))
        {
            _pathIndex = checked((int)reader.Int("pathIndex"));
            _path = [];
            foreach (var item in reader.Array("path")) _path.Add(DumbProjectileBehavior.ReadPoint(item, "p"));
        }
        _initial = DumbProjectileBehavior.ReadPoint(reader, "initial");
        _current = DumbProjectileBehavior.ReadPoint(reader, "current");
        _destination = DumbProjectileBehavior.ReadPoint(reader, "destination");
        _pulses = checked((int)reader.Int("pulses"));
        _nextPulse = reader.Int("nextPulse");
        _scorches = reader.Int("scorches");
        _nextScorch = reader.Int("nextScorch");
        _nextLaunchFx = reader.Int("nextLaunchFx");
        if (reader.Has("driveLeft"))
        {
            _driveLeft = checked((int)reader.Int("driveLeft"));
            _driveX = reader.Fixed("driveX");
            _driveY = reader.Fixed("driveY");
        }
    }
}

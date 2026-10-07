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
/// Phase 10 (docs/HANDOFF-PHASE10.md, part B point 9): the three modules that fall over or die by height, none of which the
/// reference's collision system reached before (docs/SIMULATION-CORE.md §20). <see cref="ToppleUpdate"/> and
/// <see cref="StructureToppleUpdate"/> are driven by the footprint collision added in
/// <c>World/SimulationWorld.Collision.cs</c> and by ordinary damage respectively; <see cref="HeightDieUpdate"/> is a plain
/// per-tick height check with no collision dependency at all.
/// </summary>
internal static class ToppleAndFallModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<HeightDieData>("HeightDieUpdate", ModuleOwnerKind.Object, (configuration, _) => HeightDieData.Parse(configuration), data => new HeightDieUpdate(data)),
        new ModuleBinding<StructureToppleData>("StructureToppleUpdate", ModuleOwnerKind.Object, (configuration, _) => StructureToppleData.Parse(configuration), data => new StructureToppleUpdate(data)),
        new ModuleBinding<ToppleUpdateData>("ToppleUpdate", ModuleOwnerKind.Object, (configuration, _) => ToppleUpdateData.Parse(configuration), data => new ToppleUpdate(data)),
    ];

    /// <summary><c>GeometryInfo::getMaxHeightAbovePosition</c>: a sphere is centred on its position (top = major radius above it); a cylinder/box is anchored at its base (top = its full height).</summary>
    internal static Fixed MaxHeightAbovePosition(ObjectTemplate template)
        => template.Geometry.Equals("SPHERE", StringComparison.OrdinalIgnoreCase) ? template.GeometryMajorRadius : template.GeometryHeight;
}

// ========================================================================================== ToppleUpdate

/// <summary>
/// <c>ToppleUpdateModuleData</c> field set (<c>ToppleUpdate.cpp buildFieldParse</c>). <c>ToppleFX</c>/<c>BounceFX</c> and
/// <c>ReorientToppledRubble</c> (a cosmetic repositioning of the flattened 3D model) are Opaque: nothing here renders a
/// model, so only the fall itself, the eventual death and the stump swap are simulated.
/// </summary>
public sealed record ToppleUpdateData(
    string StumpName, bool KillWhenStartToppled, bool KillWhenToppled, bool KillStumpWhenToppled, bool ToppleLeftOrRightOnly,
    Fixed InitialVelocityPercent, Fixed InitialAccelPercent, Fixed BounceVelocityPercent) : ModuleData
{
    public static ToppleUpdateData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Text(configuration, "StumpName"),
        ModuleConfigReader.Bool(configuration, "KillWhenStartToppling", false),
        ModuleConfigReader.Bool(configuration, "KillWhenFinishedToppling", true),
        ModuleConfigReader.Bool(configuration, "KillStumpWhenToppled", false),
        ModuleConfigReader.Bool(configuration, "ToppleLeftOrRightOnly", false),
        ModuleConfigReader.Real(configuration, "InitialVelocityPercent", Fixed.FromDecimal(0.2m)),
        ModuleConfigReader.Real(configuration, "InitialAccelPercent", Fixed.FromDecimal(0.01m)),
        ModuleConfigReader.Real(configuration, "BounceVelocityPercent", Fixed.FromDecimal(0.3m)));
}

/// <summary>
/// <c>ToppleUpdate.cpp</c>: an upright object that is pushed over (<see cref="ApplyTopplingForce"/>, called from
/// <c>SimulationWorld.Collision.cs</c>'s <c>TryTopple</c> when a crusher above level 1 runs into it — <c>onCollide</c> in
/// the reference) swings from a fixed angular velocity and acceleration until it either bounces to a stop or, with
/// <c>KillWhenStartToppling</c>, dies the instant it starts. On stopping it dies with the <c>TOPPLED</c> death type
/// (<c>deathByToppling</c>) unless <c>KillWhenFinishedToppling</c> is off, and optionally kills its stump the same way.
/// Not modelled: the per-tick 3D rotation and the planar realignment of <c>applyTopplingForce</c> (presentation only, no
/// 3D transform exists here) and the bounce/topple FX. <c>ToppleLeftOrRightOnly</c> still affects the chosen fall
/// direction (snapped to whichever of the object's own left/right is closer to the push), since that changes where the
/// death and stump end up, not just how the model looks.
/// </summary>
public sealed class ToppleUpdate(ToppleUpdateData data) : SimModule, IUpdateModule
{
    // Damage.h HUGE_DAMAGE_AMOUNT; ToppleUpdate.cpp deathByToppling uses it with DAMAGE_UNRESISTABLE/DEATH_TOPPLED.
    private static readonly Fixed HugeDamage = Fixed.FromInt(999999);
    private static readonly Fixed AngularLimit = FixedTrig.HalfPi - FixedTrig.Pi / Fixed.FromInt(64);
    /// <summary><c>VELOCITY_BOUNCE_LIMIT = 0.01f</c> (ToppleUpdate.cpp, <c>ToppleUpdate::update</c>): radians per logic frame of the original.</summary>
    private static readonly Fixed VelocityBounceLimitPerFrame = Fixed.FromDecimal(0.01m);
    private static readonly int ToppledDeathType = NameTables.DeathType.TryIndexOf("TOPPLED", out var toppled) ? toppled : throw new InvalidOperationException("TOPPLED is not a death type.");

    private enum State : byte { Upright, Falling, Down }

    private long _nextWake = SimulationTiming.SleepForever;
    private State _state = State.Upright;
    private Fixed _toppleAngle;
    private Fixed _angularVelocity;
    private Fixed _angularAcceleration;
    private Fixed _angularAccumulation;
    private EntityId _stumpId = EntityId.Invalid;
    // The fall is the recursion of the original, once per frame of the original and in its units (radians per frame, per frame²); above 30 Hz the angle
    // covers the step of a frame tick by tick. Both fields are at rest (zero, unwritten) between two frames, which is always at 30 Hz.
    private FramePhase _framePhase;
    private Fixed _frameStep;

    public override string ModuleName => "ToppleUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    /// <summary><c>ToppleUpdate::isAbleToBeToppled</c>.</summary>
    public bool IsAbleToBeToppled => _state == State.Upright;

    /// <summary>The fall has ended: the object lies (<c>TOPPLE_DOWN</c>). Read by the rate-equivalence suite.</summary>
    public bool IsDown => _state == State.Down;

    /// <summary>How far the object has fallen: 0 upright, close to a quarter turn when it lies (<c>m_angularAccumulation</c>). Read by the rate-equivalence suite.</summary>
    public Fixed ToppleAngle => _angularAccumulation;

    /// <summary>
    /// The object was pushed over (falling or down): <c>applyTopplingForce</c> sets <c>MODELCONDITION_TOPPLED</c> when the fall starts and never clears it. An object with
    /// <c>KillWhenStartToppling</c> dies before that line, so it never shows it. Read by the presentation.
    /// </summary>
    public bool HasToppled => _state == State.Falling || (_state == State.Down && !data.KillWhenStartToppled);

    /// <summary>
    /// The entry of the callers that pass a speed per tick (<c>SimulationWorld.Collision.cs</c>, <c>TryTopple</c>: <c>Entity.Speed</c>).
    /// </summary>
    public void ApplyTopplingForce(IWorldServices world, Fixed directionX, Fixed directionY, Fixed speed)
        // PHASE 23 MERGE: the one conversion from a speed per tick. Remove the factor (and let every caller use ApplyTopplingSpeed) once TryTopple passes
        // the pusher's speed per frame of the original; until then a caller that already passes a value per frame must call ApplyTopplingSpeed.
        => ApplyTopplingSpeed(world, directionX, directionY, world.Time.PerTickToPerOriginalFrame(speed));

    /// <summary>
    /// <c>Object::topple</c> / <c>ToppleUpdate::applyTopplingForce</c>: <paramref name="directionX"/>/<paramref name="directionY"/> point from the pusher to this object;
    /// <paramref name="speedPerOriginalFrame"/> is the pusher's speed in distance per logic frame of the original (the unit of the reference and of the data:
    /// <c>ToppleForce</c>, <c>ToppleSpeed</c>), whatever the rate of the world.
    /// </summary>
    public void ApplyTopplingSpeed(IWorldServices world, Fixed directionX, Fixed directionY, Fixed speedPerOriginalFrame)
    {
        var speed = speedPerOriginalFrame;
        if (!IsAbleToBeToppled || Entity.EffectivelyDead) return;
        if (data.KillWhenStartToppled)
        {
            _state = State.Down;
            world.WakeAt(Entity, this, SimulationTiming.SleepForever);
            world.Kill(Entity.Id);
            return;
        }
        if (world.TryGetToppleDirectionOverride(Entity, out var overrideX, out var overrideY)) { directionX = overrideX; directionY = overrideY; }
        var pushAngle = directionX == Fixed.Zero && directionY == Fixed.Zero ? Entity.Angle : FixedTrig.Atan2(directionY, directionX);
        _toppleAngle = data.ToppleLeftOrRightOnly ? ClosestPerpendicular(Entity.Angle, pushAngle) : pushAngle;
        _angularVelocity = speed * data.InitialVelocityPercent;
        _angularAcceleration = speed * data.InitialAccelPercent;
        _angularAccumulation = Fixed.Zero;
        _framePhase.Reset();
        _frameStep = Fixed.Zero;
        _state = State.Falling;
        if (data.StumpName.Length != 0) SpawnStump(world);
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    /// <summary><c>angleClosestTo</c>: the object can only fall to its own left or right (fences, streetlights), whichever is nearer the push direction.</summary>
    private static Fixed ClosestPerpendicular(Fixed facing, Fixed pushAngle)
    {
        var left = FixedTrig.Normalize(facing + FixedTrig.HalfPi);
        var right = FixedTrig.Normalize(facing - FixedTrig.HalfPi);
        return FixedTrig.AngleDifference(pushAngle, left).Abs() < FixedTrig.AngleDifference(pushAngle, right).Abs() ? left : right;
    }

    private void SpawnStump(IWorldServices world)
    {
        if (!world.Templates.TryGet(data.StumpName, out var template))
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.StumpName, CreationSkipReasons.UnknownTemplate);
            return;
        }
        if (!template.IsInstantiable)
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.StumpName, CreationSkipReasons.TemplateNotInstantiable);
            return;
        }
        _stumpId = world.RequestSpawn(template.Index, Entity.Owner, Entity.X, Entity.Y, Entity.Z, Entity.Angle);
    }

    public long Update(IWorldServices world)
    {
        if (_state != State.Falling) return SimulationTiming.SleepForever;
        // The step of this frame of the original is decided by its first tick, covered by all of its ticks, and concluded (bounce or acceleration) by its last.
        var subTick = _framePhase.Next(world.Time);
        if (subTick == TickOrder.Next)
        {
            _frameStep = _angularVelocity;
            if (_angularAccumulation + _frameStep > AngularLimit) _frameStep = AngularLimit - _angularAccumulation;
        }
        _angularAccumulation += world.Time.FrameShare(_frameStep, subTick);
        if (!_framePhase.AtBoundary) return SimulationTiming.SleepNone;
        if (_angularAccumulation >= AngularLimit && _angularVelocity > Fixed.Zero)
        {
            _angularVelocity = -(_angularVelocity * data.BounceVelocityPercent);
            if (_angularVelocity.Abs() < VelocityBounceLimitPerFrame)
            {
                _angularVelocity = Fixed.Zero;
                _state = State.Down;
                if (data.KillWhenToppled) world.Damage(Entity.Id, new DamageInfo(EntityId.Invalid, HugeDamage, DamageTypes.Unresistable, ToppledDeathType, false));
                if (data.KillStumpWhenToppled && _stumpId.IsValid) world.Damage(_stumpId, new DamageInfo(EntityId.Invalid, HugeDamage, DamageTypes.Unresistable, ToppledDeathType, false));
                return SimulationTiming.SleepForever;
            }
            // Bounced: still falling, next tick's acceleration is skipped exactly as in the reference (the hit already consumed this tick).
        }
        else
        {
            _angularVelocity += _angularAcceleration;
        }
        return SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("state", (int)_state);
        writer.Fixed("toppleAngle", _toppleAngle);
        writer.Fixed("angularVelocity", _angularVelocity);
        writer.Fixed("angularAcceleration", _angularAcceleration);
        writer.Fixed("angularAccumulation", _angularAccumulation);
        writer.UInt("stump", _stumpId.Value);
        if (_framePhase.AtBoundary) return;
        _framePhase.Describe(writer, "framePhase");
        writer.Fixed("frameStep", _frameStep);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _state = (State)reader.Int("state");
        _toppleAngle = reader.Fixed("toppleAngle");
        _angularVelocity = reader.Fixed("angularVelocity");
        _angularAcceleration = reader.Fixed("angularAcceleration");
        _angularAccumulation = reader.Fixed("angularAccumulation");
        _stumpId = new EntityId(checked((uint)reader.UInt("stump")));
        _framePhase.Load(reader, "framePhase");
        _frameStep = reader.Has("frameStep") ? reader.Fixed("frameStep") : Fixed.Zero;
    }
}

// ========================================================================================== StructureToppleUpdate

/// <summary>
/// <c>StructureToppleUpdateModuleData</c> field set (<c>StructureToppleUpdate.cpp buildFieldParse</c>). Every FX list, the
/// burst-FX cadence (<c>Min/MaxToppleBurstDelay</c>) and the damage-FX type gate are presentation and stay Opaque; the
/// <c>OCL</c>/<c>AngleFX</c> custom parsers (per-phase debris spawns, per-angle effects) are Opaque for the same reason —
/// debris dressing, not gameplay state.
/// </summary>
public sealed record StructureToppleData(
    DieMuxData Mux, long MinToppleDelayTicks, long MaxToppleDelayTicks, Fixed StructuralIntegrity, Fixed StructuralDecay, string CrushingWeapon) : ModuleData
{
    public static StructureToppleData Parse(RatedModuleConfiguration configuration) => new(
        DieMuxData.Parse(configuration),
        ModuleConfigReader.Ticks(configuration, "MinToppleDelay", 0),
        ModuleConfigReader.Ticks(configuration, "MaxToppleDelay", 0),
        ModuleConfigReader.Real(configuration, "StructuralIntegrity", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "StructuralDecay", Fixed.One),
        ModuleConfigReader.Text(configuration, "CrushingWeaponName"));
}

/// <summary>
/// <c>StructureToppleUpdate.cpp</c>: once the die filter applies, the building waits a random delay
/// (<c>beginStructureTopple</c>, direction computed from the killer's position, with a deterministic jitter drawn from
/// <see cref="IWorldServices.LogicRandom"/> in place of the reference's non-synced <c>GameLogicRandomValueReal</c> —
/// same distribution, same generator), then swings from upright to flat over an accelerating, structural-integrity-decayed
/// angle (<c>update</c>'s <c>TOPPLESTATE_TOPPLING</c> branch) while firing <c>CrushingWeaponName</c> along the ground it
/// is falling onto. The reference fires a dense grid of shots across the whole falling face each tick
/// (<c>doDamageLine</c>'s width sweep); this ports only the leading-edge shot that advances with <c>m_lastCrushedLocation</c>
/// and skips the perpendicular sweep (<see cref="CreationSkipReasons.SettingNotSimulated"/>, raised once) — less area
/// damage under a collapsing building, not none. FX, the burst cadence and the OCL debris spawns are not modelled (Opaque
/// fields, see <see cref="StructureToppleData"/>); the building does not change the model-condition state the reference
/// uses to mark the collapse as finished, since nothing here renders a model.
/// </summary>
public sealed class StructureToppleUpdate(StructureToppleData data) : SimModule, IUpdateModule, IDieModule
{
    /// <summary><c>TOPPLE_ACCELERATION_FACTOR = 0.02f</c> (StructureToppleUpdate.cpp, <c>update</c>): radians per logic frame² of the original.</summary>
    private static readonly Fixed AccelFactorPerFrameSquared = Fixed.FromDecimal(0.02m);
    private static readonly Fixed ThetaCeiling = FixedTrig.Pi / Fixed.FromInt(6);
    private static readonly Fixed EighthPi = FixedTrig.Pi / Fixed.FromInt(8);

    private const int FineBits = 32;
    private const long FineMask = (1L << FineBits) - 1;
    /// <summary>0.001 with the fine fraction bits (those of Fixed plus <see cref="FineBits"/>), rounded to nearest.</summary>
    private const long StartAngleFine = ((1L << (Fixed.FractionBits + FineBits)) + 500) / 1000;
    /// <summary>The product of three raw values carries three times the fraction bits of Fixed; this many of them are below the fine resolution.</summary>
    private const int ProductExcessBits = 2 * Fixed.FractionBits - FineBits;

    private enum State : byte { Standing, WaitingForToppleStart, Toppling, WaitingForDone, Done }

    private long _nextWake = SimulationTiming.SleepForever;
    private State _state = State.Standing;
    private long _toppleFrame;
    private long _doneAtTick;
    private Fixed _directionX;
    private Fixed _directionY;
    // Angle and angular velocity carry 32 more fraction bits than Fixed: with the retail StructuralIntegrity of 0.75 the first acceleration (0.02 * sin(0.001) * 0.25, about 5e-6)
    // was below one step of a Fixed with 16 fraction bits, and a Fixed velocity would have stayed zero for ever (the tower never fell).
    private long _velocityFine;
    private long _angleFine;
    private Fixed _structuralIntegrity;
    private Fixed _lastCrushedDistance;
    private Fixed _buildingHeight;
    private bool _gridSweepWarned;

    public override string ModuleName => "StructureToppleUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary><c>TOPPLESTATE_DONE</c>: the building lies flat and the reference has set <c>MODELCONDITION_POST_COLLAPSE</c> (and cleared <c>RUBBLE</c>).</summary>
    public bool IsToppled => _state == State.Done;

    /// <summary>How far the building has fallen: 0 upright, a quarter turn flat.</summary>
    public Fixed Angle => Fixed.FromRaw(_angleFine >> FineBits);

    public override void OnSpawn(IWorldServices world)
    {
        _buildingHeight = ToppleAndFallModules.MaxHeightAbovePosition(Entity.Template);
        _nextWake = SimulationTiming.SleepForever;
    }

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (_state != State.Standing || !data.Mux.IsApplicable(Entity, info)) return;
        var delay = Math.Max(0L, world.Time.DrawFrames(world.LogicRandom, data.MinToppleDelayTicks, data.MaxToppleDelayTicks));
        _toppleFrame = world.Tick + delay;
        var attacker = world.FindEntity(info.Source);
        Fixed angle;
        if (attacker is null)
            angle = world.LogicRandom.Range(Fixed.Zero, FixedTrig.TwoPi);
        else
            angle = FixedTrig.Normalize(FixedTrig.Atan2(Entity.Y - attacker.Y, Entity.X - attacker.X) + world.LogicRandom.Range(-EighthPi, EighthPi));
        if (world.TryGetToppleDirectionOverride(Entity, out var scriptedX, out var scriptedY) && (scriptedX != Fixed.Zero || scriptedY != Fixed.Zero)) angle = FixedTrig.Atan2(scriptedY, scriptedX);
        _directionX = FixedTrig.Cos(angle);
        _directionY = FixedTrig.Sin(angle);
        _state = State.WaitingForToppleStart;
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public long Update(IWorldServices world)
    {
        switch (_state)
        {
            case State.WaitingForToppleStart:
                if (world.Tick >= _toppleFrame)
                {
                    _state = State.Toppling;
                    _structuralIntegrity = data.StructuralIntegrity;
                    _angleFine = StartAngleFine; // the reference's literal starting value 0.001 (StructureToppleUpdate ctor): a tiny nudge so sin(angle) below is not permanently zero.
                }
                return SimulationTiming.SleepNone;
            case State.Toppling:
                StepToppling(world);
                return SimulationTiming.SleepNone;
            case State.WaitingForDone:
                if (world.Tick >= _doneAtTick) { _state = State.Done; return SimulationTiming.SleepForever; }
                return SimulationTiming.SleepNone;
            default:
                return SimulationTiming.SleepForever;
        }
    }

    private void StepToppling(IWorldServices world)
    {
        // TOPPLE_ACCELERATION_FACTOR * sin(angle) * (1 - integrity): the exact product of the three raw values, cut to the fine resolution (toward negative
        // infinity; at 16 fraction bits nothing is cut, the product has exactly the fine bits).
        var product = (Int128)world.Time.PerOriginalFrameSquaredToPerTickSquared(AccelFactorPerFrameSquared).Raw * FixedTrig.Sin(Angle).Raw * (Fixed.One - _structuralIntegrity).Raw;
        _velocityFine = checked(_velocityFine + (long)(product >> ProductExcessBits));
        if (_structuralIntegrity > Fixed.Zero)
        {
            _structuralIntegrity *= data.StructuralDecay;
            if (_structuralIntegrity < Fixed.Zero) _structuralIntegrity = Fixed.Zero;
        }
        _angleFine += _velocityFine;
        ApplyCrushingDamage(world, FixedTrig.HalfPi - Angle);
        var flat = FixedTrig.HalfPi.Raw << FineBits;
        if (_angleFine >= flat)
        {
            _velocityFine -= _angleFine - flat;
            _angleFine = flat;
            ApplyCrushingDamage(world, Fixed.Zero);
            _state = State.WaitingForDone;
            _doneAtTick = world.Tick;
        }
    }

    /// <summary><c>StructureToppleUpdate::applyCrushingDamage</c>, leading-edge shot only (see the class doc comment for the omitted width sweep).</summary>
    private void ApplyCrushingDamage(IWorldServices world, Fixed theta)
    {
        if (theta > ThetaCeiling || data.CrushingWeapon.Length == 0) return;
        if (!world.Weapons.TryGet(data.CrushingWeapon, out var weapon))
            throw new InvalidDataException($"StructureToppleUpdate references the unknown weapon '{data.CrushingWeapon}'.");
        var maxDistance = _buildingHeight * (Fixed.One - FixedTrig.Sin(theta));
        if (maxDistance <= _lastCrushedDistance) return;
        _lastCrushedDistance = maxDistance;
        if (!_gridSweepWarned)
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, "StructureToppleUpdate crushing grid sweep", CreationSkipReasons.SettingNotSimulated);
            _gridSweepWarned = true;
        }
        var x = Entity.X + maxDistance * _directionX;
        var y = Entity.Y + maxDistance * _directionY;
        world.FireWeaponAt(Entity, weapon, null, x, y, world.GroundHeightAt(x, y));
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("state", (int)_state);
        writer.Int("toppleFrame", _toppleFrame);
        writer.Int("doneAtTick", _doneAtTick);
        writer.Fixed("directionX", _directionX);
        writer.Fixed("directionY", _directionY);
        // The Fixed part under the names the state always had; the extra bits only once there are any (a standing building's state is unchanged).
        writer.Fixed("velocity", Fixed.FromRaw(_velocityFine >> FineBits));
        writer.Fixed("accumulatedAngle", Angle);
        if ((_velocityFine & FineMask) != 0) writer.Int("velocityFine", _velocityFine & FineMask);
        if ((_angleFine & FineMask) != 0) writer.Int("angleFine", _angleFine & FineMask);
        writer.Fixed("structuralIntegrity", _structuralIntegrity);
        writer.Fixed("lastCrushedDistance", _lastCrushedDistance);
        writer.Bool("gridSweepWarned", _gridSweepWarned);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _state = (State)reader.Int("state");
        _toppleFrame = reader.Int("toppleFrame");
        _doneAtTick = reader.Int("doneAtTick");
        _directionX = reader.Fixed("directionX");
        _directionY = reader.Fixed("directionY");
        _velocityFine = (reader.Fixed("velocity").Raw << FineBits) + (reader.Has("velocityFine") ? reader.Int("velocityFine") : 0);
        _angleFine = (reader.Fixed("accumulatedAngle").Raw << FineBits) + (reader.Has("angleFine") ? reader.Int("angleFine") : 0);
        _structuralIntegrity = reader.Fixed("structuralIntegrity");
        _lastCrushedDistance = reader.Fixed("lastCrushedDistance");
        _gridSweepWarned = reader.Bool("gridSweepWarned");
        _buildingHeight = ToppleAndFallModules.MaxHeightAbovePosition(Entity.Template);
    }
}

// ========================================================================================== HeightDieUpdate

/// <summary><c>HeightDieUpdateModuleData</c> field set. <c>DestroyAttachedParticlesAtHeight</c> is a presentation cleanup hint (no particle systems exist) and stays Opaque.</summary>
public sealed record HeightDieData(Fixed TargetHeightAboveTerrain, bool TargetHeightIncludesStructures, bool OnlyWhenMovingDown, bool SnapToGroundOnDeath, long InitialDelayTicks) : ModuleData
{
    public static HeightDieData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Real(configuration, "TargetHeight", Fixed.Zero),
        ModuleConfigReader.Bool(configuration, "TargetHeightIncludesStructures", false),
        ModuleConfigReader.Bool(configuration, "OnlyWhenMovingDown", false),
        ModuleConfigReader.Bool(configuration, "SnapToGroundOnDeath", false),
        ModuleConfigReader.Ticks(configuration, "InitialDelay", 0));
}

/// <summary>
/// <c>HeightDieUpdate.cpp</c>: once <c>InitialDelay</c> has passed and the object is not inside a container, it dies as
/// soon as it is below its target height (terrain height plus <c>TargetHeight</c>, or — with
/// <c>TargetHeightIncludesStructures</c> — the tallest nearby structure's height if that is greater), unless
/// <c>OnlyWhenMovingDown</c> is set and the object's Z did not just decrease. Not modelled: the bridge/tunnel layer height
/// the reference adds on top of flat terrain (no pathfind layers exist here) and the attached-particle cleanup
/// (<c>DestroyAttachedParticlesAtHeight</c>, pure presentation).
/// </summary>
public sealed class HeightDieUpdate(HeightDieData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private bool _hasDied;
    private long _earliestDeathTick = -1;
    private Fixed _lastZ = Fixed.FromInt(-1);

    public override string ModuleName => "HeightDieUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    public long Update(IWorldServices world)
    {
        if (_earliestDeathTick < 0) _earliestDeathTick = world.Tick + data.InitialDelayTicks;
        if (world.Tick < _earliestDeathTick) return SimulationTiming.SleepNone;
        if (Entity.ContainedBy.IsValid) { _lastZ = Entity.Z; return SimulationTiming.SleepNone; }

        if (!_hasDied)
        {
            var directionOk = !data.OnlyWhenMovingDown || Entity.Z < _lastZ;
            var terrainHeight = world.GroundHeightAt(Entity.X, Entity.Y);
            var targetHeight = terrainHeight + data.TargetHeightAboveTerrain;
            if (data.TargetHeightIncludesStructures)
            {
                var range = Entity.Template.BoundingCircleRadius;
                var tallest = Fixed.Zero;
                foreach (var other in world.Entities)
                {
                    if (other.Id == Entity.Id || !other.Template.IsKindOf("STRUCTURE")) continue;
                    var dx = other.X - Entity.X;
                    var dy = other.Y - Entity.Y;
                    if (!SquaredReach.Of(range).Contains(dx * dx + dy * dy)) continue;
                    var height = ToppleAndFallModules.MaxHeightAbovePosition(other.Template);
                    if (height > tallest) tallest = height;
                }
                if (tallest > data.TargetHeightAboveTerrain) targetHeight = tallest + terrainHeight;
            }
            if (Entity.Z < targetHeight && directionOk)
            {
                if (data.SnapToGroundOnDeath || Entity.Z < terrainHeight) Entity.Z = terrainHeight;
                world.Kill(Entity.Id);
                _hasDied = true;
            }
        }
        _lastZ = Entity.Z;
        return SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("hasDied", _hasDied);
        writer.Int("earliestDeathTick", _earliestDeathTick);
        writer.Fixed("lastZ", _lastZ);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _hasDied = reader.Bool("hasDied");
        _earliestDeathTick = reader.Int("earliestDeathTick");
        _lastZ = reader.Fixed("lastZ");
    }
}

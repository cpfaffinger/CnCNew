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

// ========================================================================================== PhysicsBehavior / SquishCollide

/// <summary>
/// The <c>PhysicsBehavior</c> fields the module carries, with the defaults of <c>PhysicsBehaviorModuleData</c>. The shock, friction and fall fields are what the flight of a shocked
/// object reads (F-63, docs/SHOCK-WAVE.md): the frictions per logic frame of the original (<c>parseFrictionPerSec</c>: the value per second times 1/30), the shock rates in
/// radians per frame, <c>MinFallHeightForDamage</c> as the height (the original turns it into a speed with the gravity, <c>heightToSpeed</c>). The crash-weapon keys have no consumer.
/// </summary>
public sealed record PhysicsData(Fixed Mass, bool AllowBouncing, bool AllowCollideForce, bool KillWhenRestingOnGround) : ModuleData
{
    public Fixed ShockResistance { get; init; }
    public Fixed ShockMaxYaw { get; init; } = Fixed.FromDecimal(0.05m);
    public Fixed ShockMaxPitch { get; init; } = Fixed.FromDecimal(0.025m);
    public Fixed ShockMaxRoll { get; init; } = Fixed.FromDecimal(0.025m);
    public Fixed ForwardFriction { get; init; } = Fixed.FromDecimal(0.15m);
    public Fixed LateralFriction { get; init; } = Fixed.FromDecimal(0.15m);
    public Fixed ZFriction { get; init; } = Fixed.FromDecimal(0.8m);
    public Fixed AerodynamicFriction { get; init; }
    public Fixed CenterOfMassOffset { get; init; }
    public Fixed MinFallHeightForDamage { get; init; } = Fixed.FromInt(40);
    public Fixed FallHeightDamageFactor { get; init; } = Fixed.One;
    public Fixed PitchRollYawFactor { get; init; } = Fixed.FromInt(2);

    public static PhysicsData Parse(RatedModuleConfiguration configuration)
    {
        var frame = Fixed.FromInt(CnCNew.Data.OriginalFrame.PerSecond);
        Fixed PerFrame(string key, Fixed fallback) => ModuleConfigReader.Find(configuration, key) is not null ? ModuleConfigReader.Real(configuration, key, Fixed.Zero) / frame : fallback;
        var defaults = new PhysicsData(Fixed.One, false, true, false);
        return new PhysicsData(
            ModuleConfigReader.Real(configuration, "Mass", Fixed.One),
            ModuleConfigReader.Bool(configuration, "AllowBouncing", false),
            ModuleConfigReader.Bool(configuration, "AllowCollideForce", true),
            ModuleConfigReader.Bool(configuration, "KillWhenRestingOnGround", false))
        {
            ShockResistance = ModuleConfigReader.Real(configuration, "ShockResistance", defaults.ShockResistance),
            ShockMaxYaw = ModuleConfigReader.Real(configuration, "ShockMaxYaw", defaults.ShockMaxYaw),
            ShockMaxPitch = ModuleConfigReader.Real(configuration, "ShockMaxPitch", defaults.ShockMaxPitch),
            ShockMaxRoll = ModuleConfigReader.Real(configuration, "ShockMaxRoll", defaults.ShockMaxRoll),
            ForwardFriction = PerFrame("ForwardFriction", defaults.ForwardFriction),
            LateralFriction = PerFrame("LateralFriction", defaults.LateralFriction),
            ZFriction = PerFrame("ZFriction", defaults.ZFriction),
            AerodynamicFriction = PerFrame("AerodynamicFriction", defaults.AerodynamicFriction),
            CenterOfMassOffset = ModuleConfigReader.Real(configuration, "CenterOfMassOffset", defaults.CenterOfMassOffset),
            MinFallHeightForDamage = ModuleConfigReader.Real(configuration, "MinFallHeightForDamage", defaults.MinFallHeightForDamage),
            FallHeightDamageFactor = ModuleConfigReader.Real(configuration, "FallHeightDamageFactor", defaults.FallHeightDamageFactor),
            PitchRollYawFactor = ModuleConfigReader.Real(configuration, "PitchRollYawFactor", defaults.PitchRollYawFactor),
        };
    }
}

/// <summary>
/// <c>PhysicsUpdate.cpp</c>: the reference module acts on forces (<c>applyForce</c>, <c>applyShock</c>, <c>applyMotiveForce</c>) and on terrain contact, and sleeps until one arrives. This
/// simulation has no force source (no knockback, no bouncing debris, no locomotor thrust), so the module is the reference's resting state — except for one force, gravity on an object that is
/// let go in the air (a bomb leaving its plane, <see cref="StartFall"/>): it then falls with <c>GravityPerTickSquared</c> and the horizontal speed it was given, lands on the terrain and rests
/// there (<c>KillWhenRestingOnGround</c> kills it; the bomb's <c>HeightDieUpdate</c> normally ends it a tick earlier, one unit above the ground). Ground movement comes from the move-order system.
/// </summary>
public sealed class PhysicsBehavior(PhysicsData data) : SimModule, IUpdateModule
{
    /// <summary><c>getDisabledTypesToProcess</c>.</summary>
    public DisabledMask DisabledTypesToProcess => DisabledMask.All;

    private long _nextWake = SimulationTiming.SleepForever;
    private bool _falling;
    private Fixed _velocityX;
    private Fixed _velocityY;
    private Fixed _velocityZ;
    // The ticks of the current frame of the original already fallen (0: this tick takes the frame's step). Always 0 at 30 Hz (and then not written).
    private int _subStep;
    // The flight of a shocked object (F-63): the acceleration waiting for the next step, the rates of yaw, pitch and roll per frame, the pitch and roll the object has,
    // whether it may bounce (applyRandomRotation allows it until a bounce gives nothing back) and whether it was above the ground after the last step.
    private bool _flying;
    private Fixed _accelX;
    private Fixed _accelY;
    private Fixed _accelZ;
    private Fixed _yawRate;
    private Fixed _pitchRate;
    private Fixed _rollRate;
    private Fixed _pitch;
    private Fixed _roll;
    private bool _bounce;
    private bool _wasAirborne;
    // The stun (F-63, step 3): IS_STUNNED, and the two model conditions the original sets on the object - STUNNED_FLAILING from the shock until a landing while stunned, STUNNED
    // from that landing until the stun ends. The stun's end clears STUNNED only, so a unit whose stun ends before it lands keeps flailing (the original's own defect).
    private bool _stunned;
    private bool _flailing;
    private bool _stunnedPose;

    public override string ModuleName => "PhysicsBehavior";
    public PhysicsData Data => data;
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary>The object is in free fall.</summary>
    public bool Falling => _falling;

    /// <summary>
    /// Lets the object go: it keeps (<paramref name="velocityX"/>, <paramref name="velocityY"/>) per logic frame of the original horizontally and accelerates downwards. The fall is
    /// stepped once per frame, on the object's own phase (the first step one frame after the release), exactly as at 30 Hz: gravity per frame² is added to the velocity per frame,
    /// the landing is judged on the whole step. The step of a frame is fallen in equal shares over its ticks (docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md).
    /// </summary>
    /// <summary>The object flies after a shock (F-63): its movement waits until it rests on the ground again.</summary>
    public bool Flying => _flying;

    /// <summary><c>getIsStunned</c>: the object's locomotor does nothing (<c>Locomotor::locoUpdate_moveTowards*</c>).</summary>
    public bool Stunned => _stunned;

    /// <summary>The model condition <c>STUNNED_FLAILING</c> the shock set (cleared only by a landing while stunned).</summary>
    public bool ShowsFlailing => _flailing;

    /// <summary>The model condition <c>STUNNED</c>: from a landing while stunned until the stun ends.</summary>
    public bool ShowsStunned => _stunnedPose;

    /// <summary>The pitch and roll a shock gave the object, in radians (0 for an upright object).</summary>
    public (Fixed Pitch, Fixed Roll) Tilt => (_pitch, _roll);

    /// <summary>
    /// <c>Object::attemptDamage</c>'s shock wave on this object: <c>applyShock</c> (the force less the <c>ShockResistance</c> share, divided by the mass, waits for the next step as an
    /// acceleration) and <c>applyRandomRotation</c> (unless the object sticks to the ground: bouncing allowed, and the yaw, pitch and roll rates each gain their <c>ShockMax</c> times
    /// a draw of the logic stream between -1 and 1, in that order). The flight is stepped once per logic frame of the original from the next one on, like a fall.
    /// </summary>
    public void ApplyShock(IWorldServices world, Fixed forceX, Fixed forceY, Fixed forceZ, bool stickToGround)
    {
        var resisted = Fixed.One - Fixed.Min(Fixed.One, Fixed.Max(Fixed.Zero, data.ShockResistance));
        var mass = data.Mass > Fixed.Zero ? data.Mass : Fixed.One;
        _accelX += forceX * resisted / mass;
        _accelY += forceY * resisted / mass;
        _accelZ += forceZ * resisted / mass;
        // Object::attemptDamage: setStunned(true) and STUNNED_FLAILING.
        _stunned = true;
        _flailing = true;
        if (!stickToGround)
        {
            _bounce = true;
            _yawRate += data.ShockMaxYaw * world.LogicRandom.Range(-Fixed.One, Fixed.One);
            _pitchRate += data.ShockMaxPitch * world.LogicRandom.Range(-Fixed.One, Fixed.One);
            _rollRate += data.ShockMaxRoll * world.LogicRandom.Range(-Fixed.One, Fixed.One);
        }
        if (!_flying)
        {
            _flying = true;
            Entity.InPhysicsFlight = true;
            _wasAirborne = Entity.Z > world.GroundHeightAt(Entity.X, Entity.Y);
            if (!_falling) _subStep = 0;
            world.WakeAt(Entity, this, world.Tick + world.Time.OriginalFrames(SimulationTiming.SleepNone));
        }
        world.MarkDirty(Entity);
    }

    /// <summary>
    /// <c>doStuffToObj</c>'s throw of a created object (F-66): <c>setMass</c> for a piece of debris (<paramref name="mass"/> above zero), <c>applyForce</c> (the force divided by the mass
    /// waits for the next step as an acceleration), <c>setYawRate</c>, <c>setRollRate</c>, <c>setPitchRate</c>, <c>setAngles(orientation, 0, 0)</c> and, for <paramref name="bounce"/>,
    /// <c>setAllowBouncing(true)</c>. The flight is the shocked object's without the stun, stepped once per logic frame of the original from the next one on.
    /// </summary>
    public void Launch(IWorldServices world, Fixed forceX, Fixed forceY, Fixed forceZ, Fixed yawRate, Fixed pitchRate, Fixed rollRate, bool rates, bool bounce, Fixed mass)
    {
        if (mass > Fixed.Zero) _mass = mass;
        var effective = Mass;
        _accelX += forceX / effective;
        _accelY += forceY / effective;
        _accelZ += forceZ / effective;
        if (rates)
        {
            (_yawRate, _pitchRate, _rollRate) = (yawRate, pitchRate, rollRate);
            (_pitch, _roll) = (Fixed.Zero, Fixed.Zero);
        }
        if (bounce) _bounce = true;
        if (!_flying)
        {
            _flying = true;
            Entity.InPhysicsFlight = true;
            _wasAirborne = Entity.Z > world.GroundHeightAt(Entity.X, Entity.Y);
            if (!_falling) _subStep = 0;
            world.WakeAt(Entity, this, world.Tick + world.Time.OriginalFrames(SimulationTiming.SleepNone));
        }
        world.MarkDirty(Entity);
    }

    /// <summary>The mass the forces divide by: the module's, or the one a debris nugget set (<c>setMass</c>).</summary>
    private Fixed Mass => _mass > Fixed.Zero ? _mass : data.Mass > Fixed.Zero ? data.Mass : Fixed.One;

    /// <summary><c>setMass</c> of a debris nugget (0: the module's own).</summary>
    private Fixed _mass;

    public void StartFall(IWorldServices world, Fixed velocityX, Fixed velocityY)
    {
        _falling = true;
        _velocityX = velocityX;
        _velocityY = velocityY;
        _velocityZ = Fixed.Zero;
        _subStep = 0;
        world.WakeAt(Entity, this, world.Tick + world.Time.OriginalFrames(SimulationTiming.SleepNone));
    }

    public long Update(IWorldServices world)
    {
        if (_flying && !_falling) return Fly(world);
        if (!_falling) return SimulationTiming.SleepForever;
        var time = world.Time;
        if (_subStep != 0)
        {
            // Between two frames: one more share of the frame's step.
            Entity.X += time.FrameShare(_velocityX, _subStep);
            Entity.Y += time.FrameShare(_velocityY, _subStep);
            Entity.Z += time.FrameShare(_velocityZ, _subStep);
            _subStep = time.NextSubStep(_subStep);
            world.MarkDirty(Entity);
            return SimulationTiming.SleepNone;
        }
        _velocityZ += ProjectileModules.GravityPerFrameSquared(world);
        var beforeX = Entity.X;
        var beforeY = Entity.Y;
        var beforeZ = Entity.Z;
        Entity.X += _velocityX;
        Entity.Y += _velocityY;
        Entity.Z += _velocityZ;
        var ground = world.GroundHeightAt(Entity.X, Entity.Y);
        if (Entity.Z <= ground)
        {
            Entity.Z = ground;
            _falling = false;
            world.MarkDirty(Entity);
            if (data.KillWhenRestingOnGround) world.Kill(Entity.Id);
            return SimulationTiming.SleepForever;
        }
        if (time.TicksPerOriginalFrame != 1)
        {
            // The landing was judged on the whole step of this frame, as at 30 Hz; the object itself falls the first share of it now and the rest on the next ticks.
            Entity.X = beforeX + time.FrameShare(_velocityX, 0);
            Entity.Y = beforeY + time.FrameShare(_velocityY, 0);
            Entity.Z = beforeZ + time.FrameShare(_velocityZ, 0);
            _subStep = time.NextSubStep(0);
        }
        world.MarkDirty(Entity);
        return SimulationTiming.SleepNone;
    }

    private static readonly Fixed MinNonAeroFriction = Fixed.FromDecimal(0.01m);
    private static readonly Fixed MaxFriction = Fixed.FromDecimal(0.99m);
    private static readonly Fixed GroundDamping = Fixed.One - Fixed.FromDecimal(0.15m);
    private static readonly Fixed BounceDamping = Fixed.FromDecimal(0.7m);
    private static readonly Fixed VelocityThreshold = Fixed.FromDecimal(0.001m);
    private static readonly Fixed MinStiffness = Fixed.FromDecimal(0.01m);
    private static readonly Fixed SteepFall = Fixed.FromInt(3);
    private static readonly Fixed TinyDelta = Fixed.FromDecimal(0.01m);
    private static readonly int FallingDamageType = NameTables.DamageType.TryIndexOf("FALLING", out var falling) ? falling : DamageTypes.Unresistable;
    private static readonly int SplattedDeathType = NameTables.DeathType.TryIndexOf("SPLATTED", out var splatted) ? splatted : DeathTypes.Normal;

    /// <summary>
    /// One update of <c>PhysicsBehavior::update</c> for a shocked object, the step of a logic frame of the original (fallen in equal shares over the frame's ticks above 30 Hz):
    /// gravity; on or near the ground the rates damped by 0.85 and the lateral and forward friction against the object's facing, in the air the aerodynamic friction; the
    /// velocity (components under 0.001 become 0) moves the object; yaw turns its facing, pitch and roll are kept (the centre of mass bends the pitch towards straight down or
    /// up); the ground: <c>handleBounce</c> (the stiffness of the ground gives back the downward speed of a falling object that may bounce, the rates are damped by 0.7, an
    /// object on its back is turned over and righted after the bounce), the object on the ground, one that sticks to it kept there; the falling damage of a steep landing
    /// (<c>DAMAGE_FALLING</c>, <c>DEATH_SPLATTED</c>, by the speed beyond <c>MinFallHeightForDamage</c>'s, times the mass and <c>FallHeightDamageFactor</c>). The flight ends when
    /// the object rests on the ground without speed and without rates.
    /// </summary>
    private long Fly(IWorldServices world)
    {
        var time = world.Time;
        if (_subStep != 0)
        {
            Entity.CoverShare(world.Tick, time.FrameShare(_stepX, _subStep), time.FrameShare(_stepY, _subStep), time.FrameShare(_stepZ, _subStep));
            _subStep = time.NextSubStep(_subStep);
            world.MarkDirty(Entity);
            return _subStep == 0 && !_flying ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;
        }

        var stick = Entity.Template.Locomotor is { StickToGround: true };
        _accelZ += ProjectileModules.GravityPerFrameSquared(world);
        if (!world.IsSignificantlyAboveTerrain(Entity))
        {
            Damp(GroundDamping);
            if (_velocityX != Fixed.Zero || _velocityY != Fixed.Zero)
            {
                var dirX = FixedTrig.Cos(Entity.Angle);
                var dirY = FixedTrig.Sin(Entity.Angle);
                var lateral = _velocityX * -dirY + _velocityY * dirX;
                var forward = _velocityX * dirX + _velocityY * dirY;
                var lf = Clamp(data.LateralFriction, MinNonAeroFriction, MaxFriction);
                var ff = Clamp(data.ForwardFriction, MinNonAeroFriction, MaxFriction);
                _accelX -= lf * lateral * -dirY + ff * forward * dirX;
                _accelY -= lf * lateral * dirX + ff * forward * dirY;
            }
        }
        else
        {
            var aero = Clamp(data.AerodynamicFriction, Fixed.Zero, MaxFriction);
            _accelX -= _velocityX * aero;
            _accelY -= _velocityY * aero;
            _accelZ -= _velocityZ * aero;
            Damp(Fixed.One - aero);
        }
        _velocityX = Small(_velocityX + _accelX);
        _velocityY = Small(_velocityY + _accelY);
        _velocityZ = Small(_velocityZ + _accelZ);
        (_accelX, _accelY, _accelZ) = (Fixed.Zero, Fixed.Zero, Fixed.Zero);

        var (startX, startY, oldZ) = (Entity.X, Entity.Y, Entity.Z);
        var x = startX + _velocityX;
        var y = startY + _velocityY;
        var z = oldZ + _velocityZ;
        // The stun ends at rest or when the object is not significantly above the terrain - tested where it stood before this step (the original applies its new matrix only
        // at the end of the update), so a unit thrown from the ground loses its stun in its first step. Only STUNNED is cleared.
        if (_stunned && ((_velocityX.Abs() < StunRelief && _velocityY.Abs() < StunRelief && _velocityZ.Abs() < StunRelief) || !world.IsSignificantlyAboveTerrain(Entity)))
        {
            _stunned = false;
            _stunnedPose = false;
        }
        // handleBounce and testStunnedUnitForDestruction read the object's orientation before this step's rotation, for the same reason.
        var uprightBefore = Upright;
        if (_yawRate != Fixed.Zero || _pitchRate != Fixed.Zero || _rollRate != Fixed.Zero)
        {
            var pitchRate = _pitchRate * data.PitchRollYawFactor;
            if (data.CenterOfMassOffset != Fixed.Zero)
            {
                var halfPi = FixedTrig.Pi / Fixed.FromInt(2);
                pitchRate *= FixedTrig.Sin(data.CenterOfMassOffset > Fixed.Zero ? halfPi - _pitch : _pitch - halfPi);
            }
            _roll = FixedTrig.Normalize(_roll + _rollRate * data.PitchRollYawFactor);
            _pitch = FixedTrig.Normalize(_pitch + pitchRate);
            Entity.Angle = FixedTrig.Normalize(Entity.Angle + _yawRate * data.PitchRollYawFactor);
        }

        var ground = world.GroundHeightAt(x, y);
        var bounced = false;
        var bounceZ = Fixed.Zero;
        if (_bounce && z <= ground)
        {
            var stiffness = Clamp(world.Templates.GroundStiffness, MinStiffness, MaxFriction);
            if (oldZ > ground && _velocityZ < Fixed.Zero) bounceZ = _velocityZ.Abs() * stiffness;
            Damp(BounceDamping);
            if (_velocityZ < Fixed.Zero)
            {
                // handleBounce: an object on its back is turned over (roll pi), the pitch is not flipped as well.
                _roll = uprightBefore ? Fixed.Zero : FixedTrig.Pi;
                _pitch = Fixed.Zero;
            }
            bounced = bounceZ > Fixed.Zero;
            if (!bounced) _bounce = data.AllowBouncing;
            else if (StunnedUnitIsDestroyed(world, startX, startY, uprightBefore))
            {
                world.Kill(Entity.Id);
                world.MarkDirty(Entity);
            }
        }
        var activeVelocityZ = _velocityZ;
        if (z <= ground)
        {
            _velocityZ += ground - z;
            if (_velocityZ > Fixed.Zero) _velocityZ = Fixed.Zero;
            z = ground;
            // "When a stunned object hits the ground the first time": STUNNED_FLAILING becomes STUNNED.
            if (_stunned)
            {
                _flailing = false;
                _stunnedPose = true;
            }
        }
        else if (stick) z = ground;
        if (bounced) (_pitch, _roll) = (Fixed.Zero, Fixed.Zero);
        if (bounced && _bounce) _accelZ += bounceZ;

        var airborne = z > ground;
        var fell = _wasAirborne && !airborne;
        _wasAirborne = airborne;
        (_stepX, _stepY, _stepZ) = (x - startX, y - startY, z - oldZ);
        if (time.TicksPerOriginalFrame != 1)
        {
            // The step was judged whole, as at 30 Hz; the object covers the first share of it now and the rest on the next ticks of the frame. Observers read where the
            // frame ends (Entity.FrameEnd: a radiation field's pulse, an area weapon), as they do for a mover.
            Entity.SetAhead(world.Tick, _stepX, _stepY, _stepZ);
            Entity.CoverShare(world.Tick, time.FrameShare(_stepX, 0), time.FrameShare(_stepY, 0), time.FrameShare(_stepZ, 0));
            _subStep = time.NextSubStep(0);
        }
        else (Entity.X, Entity.Y, Entity.Z) = (x, y, z);
        world.MarkDirty(Entity);

        if (fell)
        {
            // v = sqrt(2 g h): the speed a fall from MinFallHeightForDamage reaches.
            var minimumSpeed = Fixed.Sqrt((Fixed.FromInt(2) * ProjectileModules.GravityPerFrameSquared(world) * data.MinFallHeightForDamage).Abs());
            var net = -activeVelocityZ - minimumSpeed;
            if (net > Fixed.Zero
                && (_velocityX.Abs() <= TinyDelta || activeVelocityZ.Abs() >= _velocityX.Abs() * SteepFall)
                && (_velocityY.Abs() <= TinyDelta || activeVelocityZ.Abs() >= _velocityY.Abs() * SteepFall))
                world.Damage(Entity.Id, new DamageInfo(Entity.Id, net * Mass * data.FallHeightDamageFactor, FallingDamageType, SplattedDeathType, false));
        }

        // At rest on the ground: the flight is over and the movement takes the object again. What is left of the rates is dropped - the original lets them decay on (by 0.85 a
        // frame on the ground, never quite to zero), turning the object imperceptibly while its locomotor already drives it; the port's movement waits for the flight's end.
        if (!airborne && _velocityX == Fixed.Zero && _velocityY == Fixed.Zero && _velocityZ == Fixed.Zero && _accelX == Fixed.Zero && _accelY == Fixed.Zero && _accelZ == Fixed.Zero)
        {
            (_yawRate, _pitchRate, _rollRate) = (Fixed.Zero, Fixed.Zero, Fixed.Zero);
            _flying = false;
            Entity.InPhysicsFlight = false;
            _bounce = false;
            // KILL_WHEN_RESTING_ON_GROUND: a piece of debris that has come to rest dies (and its slow death removes it).
            if (data.KillWhenRestingOnGround) world.Kill(Entity.Id);
            return _subStep == 0 ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;
        }
        return SimulationTiming.SleepNone;
    }

    // The step the object takes in the frame being flown (its shares are covered over the frame's ticks above 30 Hz).
    private Fixed _stepX;
    private Fixed _stepY;
    private Fixed _stepZ;

    private bool Upright => FixedTrig.Cos(_pitch) * FixedTrig.Cos(_roll) > Fixed.Zero;

    private static readonly Fixed StunRelief = Fixed.FromDecimal(0.5m);

    /// <summary>
    /// <c>testStunnedUnitForDestruction</c>, at a bounce: a stunned object dies when it lands on its back, off the map, or - with an AI - on a cliff or under water without a
    /// locomotor for that surface; read where it stood before the step, as the original does.
    /// </summary>
    private bool StunnedUnitIsDestroyed(IWorldServices world, Fixed x, Fixed y, bool upright)
    {
        if (!_stunned) return false;
        if (!upright) return true;
        if (FlightModules.IsOffMap(world, x, y)) return true;
        if (CrateModules.AiOf(Entity) is null || world.Terrain is not { } terrain) return false;
        var surfaces = Entity.Template.Locomotor?.Surfaces ?? LocomotorSurfaces.None;
        var (cx, cy) = (Terrain.TerrainMap.CellOf(x), Terrain.TerrainMap.CellOf(y));
        if (terrain.IsInside(cx, cy) && (terrain.CellAt(cx, cy) & Terrain.CellFlags.Cliff) != 0 && (surfaces & LocomotorSurfaces.Cliff) == 0) return true;
        return terrain.IsUnderwater(x, y) && (surfaces & LocomotorSurfaces.Water) == 0;
    }

    private void Damp(Fixed factor)
    {
        _yawRate *= factor;
        _pitchRate *= factor;
        _rollRate *= factor;
    }

    private static Fixed Clamp(Fixed value, Fixed low, Fixed high) => Fixed.Min(high, Fixed.Max(low, value));

    private static Fixed Small(Fixed value) => value.Abs() < VelocityThreshold ? Fixed.Zero : value;

    private void DescribeFlight(IStateWriter writer)
    {
        writer.Bool("flying", true);
        writer.Fixed("flightVelocityX", _velocityX);
        writer.Fixed("flightVelocityY", _velocityY);
        writer.Fixed("flightVelocityZ", _velocityZ);
        writer.Fixed("accelX", _accelX);
        writer.Fixed("accelY", _accelY);
        writer.Fixed("accelZ", _accelZ);
        writer.Fixed("yawRate", _yawRate);
        writer.Fixed("pitchRate", _pitchRate);
        writer.Fixed("rollRate", _rollRate);
        writer.Fixed("pitch", _pitch);
        writer.Fixed("roll", _roll);
        writer.Bool("bounce", _bounce);
        writer.Bool("wasAirborne", _wasAirborne);
        writer.Bool("stunned", _stunned);
        if (_subStep == 0) return;
        writer.Int("flightSubStep", _subStep);
        writer.Fixed("stepX", _stepX);
        writer.Fixed("stepY", _stepY);
        writer.Fixed("stepZ", _stepZ);
    }

    private void LoadFlight(IStateReader reader)
    {
        _flying = reader.Has("flying");
        if (!_flying) return;
        _velocityX = reader.Fixed("flightVelocityX");
        _velocityY = reader.Fixed("flightVelocityY");
        _velocityZ = reader.Fixed("flightVelocityZ");
        _accelX = reader.Fixed("accelX");
        _accelY = reader.Fixed("accelY");
        _accelZ = reader.Fixed("accelZ");
        _yawRate = reader.Fixed("yawRate");
        _pitchRate = reader.Fixed("pitchRate");
        _rollRate = reader.Fixed("rollRate");
        _pitch = reader.Fixed("pitch");
        _roll = reader.Fixed("roll");
        _bounce = reader.Bool("bounce");
        _wasAirborne = reader.Bool("wasAirborne");
        _stunned = reader.Bool("stunned");
        if (!reader.Has("flightSubStep")) return;
        _subStep = checked((int)reader.Int("flightSubStep"));
        _stepX = reader.Fixed("stepX");
        _stepY = reader.Fixed("stepY");
        _stepZ = reader.Fixed("stepZ");
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        if (_mass > Fixed.Zero) writer.Fixed("mass", _mass);
        if (_flailing) writer.Bool("flailing", true);
        if (_stunnedPose) writer.Bool("stunnedPose", true);
        if (_flying) DescribeFlight(writer);
        if (!_falling) return;
        writer.Bool("falling", true);
        writer.Fixed("velocityX", _velocityX);
        writer.Fixed("velocityY", _velocityY);
        writer.Fixed("velocityZ", _velocityZ);
        if (_subStep != 0) writer.Int("subStep", _subStep);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _falling = reader.Has("falling");
        _velocityX = _falling ? reader.Fixed("velocityX") : Fixed.Zero;
        _velocityY = _falling ? reader.Fixed("velocityY") : Fixed.Zero;
        _velocityZ = _falling ? reader.Fixed("velocityZ") : Fixed.Zero;
        _subStep = _falling && reader.Has("subStep") ? checked((int)reader.Int("subStep")) : 0;
        _mass = reader.Has("mass") ? reader.Fixed("mass") : Fixed.Zero;
        _flailing = reader.Has("flailing");
        _stunnedPose = reader.Has("stunnedPose");
        LoadFlight(reader);
        Entity.InPhysicsFlight = _flying;
    }
}

/// <summary>
/// <c>SquishCollide.cpp</c>: <c>onCollide</c> crushes an infantry unit a moving vehicle runs over. The simulation has no collision
/// detection (objects do not push, block or overlap-test each other yet), so <c>onCollide</c> is never called and the module carries no
/// state; it exists so the vehicles that have it can be instantiated. Collisions and crushing are phase 6.
/// </summary>
public sealed class SquishCollide : SimModule
{
    public override string ModuleName => "SquishCollide";
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// ========================================================================================== GenerateMinefieldBehavior

public sealed record GenerateMinefieldData(
    UpgradeMuxData UpgradeMux, string MineName, string UpgradedMineName, string UpgradedTrigger, string GenerationFx, Fixed? DistanceAroundObject,
    bool GenerateOnlyOnDeath, bool BorderOnly, bool SmartBorder, bool SmartBorderSkipInterior, bool AlwaysCircular, bool Upgradable,
    Fixed RandomJitter, Fixed SkipIfUnderStructure) : ModuleData
{
    public static GenerateMinefieldData Parse(RatedModuleConfiguration configuration, UpgradeCatalog upgrades) => new(
        UpgradeMuxData.Parse(configuration, upgrades),
        ModuleConfigReader.Text(configuration, "MineName"),
        ModuleConfigReader.Text(configuration, "UpgradedMineName"),
        ModuleConfigReader.Text(configuration, "UpgradedTriggeredBy"),
        ModuleConfigReader.Text(configuration, "GenerationFX"),
        ModuleConfigReader.Find(configuration, "DistanceAroundObject") is null ? null : ModuleConfigReader.Real(configuration, "DistanceAroundObject", Fixed.Zero),
        ModuleConfigReader.Bool(configuration, "GenerateOnlyOnDeath", false),
        ModuleConfigReader.Bool(configuration, "BorderOnly", true),
        ModuleConfigReader.Bool(configuration, "SmartBorder", false),
        ModuleConfigReader.Bool(configuration, "SmartBorderSkipInterior", true),
        ModuleConfigReader.Bool(configuration, "AlwaysCircular", false),
        ModuleConfigReader.Bool(configuration, "Upgradable", false),
        ModuleConfigReader.Real(configuration, "RandomJitter", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "SkipIfThisMuchUnderStructure", Fixed.FromDecimal(0.33m)));
}

/// <summary>
/// <c>GenerateMinefieldBehavior.cpp</c>: lays a ring of mine objects around the owner, either when the module's <c>TriggeredBy</c> upgrade
/// completes or, with <c>GenerateOnlyOnDeath</c>, when the owner dies; an <c>Upgradable</c> minefield swaps its mines for the
/// <c>UpgradedMineName</c> ones once the owner has completed <c>UpgradedTriggeredBy</c>. Ported: the <c>m_generated</c>/<c>m_upgraded</c>
/// state machine, <c>update()</c> (polls every tick while an upgrade is still possible, as the reference does), the ring geometry of
/// <c>placeMines</c> with <c>SmartBorder</c> + circular footprint (all 63 uses in the shipped data), the draw order of the logic
/// stream (jitter x, jitter y, orientation per mine) and the "mostly under a structure" veto (the mine's footprint shrunk to
/// <c>SkipIfThisMuchUnderStructure</c> of its radius against every STRUCTURE, approximated by bounding circles).
/// Not modeled, and never silently: terrain vetoes (water, cliffs; no terrain yet), <c>setScootParms</c> (the mine's slide from the producer, part of
/// <c>MinefieldBehavior</c>), and the rectangular-border / footprint-fill layouts (no shipped use); those report
/// <see cref="CreationSkipReasons.SettingNotSimulated"/>. Mines that are not instantiable themselves (<c>MinefieldBehavior</c> is a module of its own)
/// report <see cref="CreationSkipReasons.TemplateNotInstantiable"/> once per placement.
/// </summary>
public sealed class GenerateMinefieldBehavior(GenerateMinefieldData data) : SimModule, IUpdateModule, IUpgradeModule, IDieModule
{
    private readonly UpgradeMuxState _mux = new(data.UpgradeMux);
    private readonly List<EntityId> _mines = [];
    private long _nextWake = -1;
    private bool _generated;
    private bool _upgraded;
    private UpgradeMask? _upgradedMask;

    public override string ModuleName => "GenerateMinefieldBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
        => _nextWake = data.Upgradable ? world.Tick + SimulationTiming.SleepNone : SimulationTiming.SleepForever;

    public void TryActivate(IWorldServices world, UpgradeMask keyMask) => _mux.AttemptUpgrade(world, Entity, keyMask, PlaceMines);
    public void TryReset(IWorldServices world, UpgradeMask keyMask) => _mux.ResetUpgrade(keyMask);
    public bool WouldUpgrade(UpgradeMask keyMask) => _mux.WouldUpgrade(keyMask);

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (data.GenerateOnlyOnDeath) PlaceMines(world);
    }

    public long Update(IWorldServices world)
    {
        if (_upgraded || !data.Upgradable) return SimulationTiming.SleepForever;
        if (_generated && Entity.CompletedUpgrades.TestForAny(UpgradedMask(world)))
        {
            _upgraded = true;
            foreach (var id in _mines)
                if (world.FindEntity(id) is not null) world.RequestDestroy(id);
            _mines.Clear();
            _generated = false;
            PlaceMines(world);
        }
        return SimulationTiming.SleepNone;
    }

    private UpgradeMask UpgradedMask(IWorldServices world)
        => _upgradedMask ??= data.UpgradedTrigger.Length == 0 ? UpgradeMask.Empty : world.Upgrades.MaskOf([data.UpgradedTrigger]);

    private static Fixed BoundingRadius(ObjectTemplate template) => template.BoundingCircleRadius;

    private void PlaceMines(IWorldServices world)
    {
        if (_generated) return;
        _generated = true;

        var mineName = _upgraded ? data.UpgradedMineName : data.MineName;
        if (!world.Templates.TryGet(mineName, out var mine))
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, mineName, CreationSkipReasons.UnknownTemplate);
            return;
        }

        if (!data.SmartBorder || (!data.AlwaysCircular && Entity.Template.Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase)))
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid,
                data.SmartBorder ? "GenerateMinefieldBehavior rectangular border" : data.BorderOnly ? "GenerateMinefieldBehavior plain border" : "GenerateMinefieldBehavior footprint fill",
                CreationSkipReasons.SettingNotSimulated);
            return;
        }

        var mineRadius = BoundingRadius(mine);
        if (!mine.IsInstantiable || mineRadius <= Fixed.Zero)
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, mineName, mine.IsInstantiable ? CreationSkipReasons.NoObjectNamed : CreationSkipReasons.TemplateNotInstantiable);
        else
        {
            if (!data.SmartBorderSkipInterior) PlaceMineAt(world, mine, mineRadius, Entity.X, Entity.Y);
            var radius = (data.SmartBorderSkipInterior ? BoundingRadius(Entity.Template) : mineRadius) + mineRadius;
            var limit = data.DistanceAroundObject ?? world.Templates.StandardMinefieldDistance;
            do
            {
                PlaceRing(world, mine, mineRadius, radius);
                radius += mineRadius * 2L;
            }
            while (radius < limit);
        }

        if (data.GenerationFx.Length != 0) world.Emit(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, data.GenerationFx, 0);
    }

    private void PlaceRing(IWorldServices world, ObjectTemplate mine, Fixed mineRadius, Fixed radius)
    {
        var mineDiameter = mineRadius * 2L;
        var count = Math.Max(1L, (FixedTrig.TwoPi * radius / mineDiameter).Ceiling());
        var increment = FixedTrig.TwoPi / Fixed.FromInt(count);
        var limit = FixedTrig.TwoPi - increment / Fixed.FromInt(2);
        var jitter = mineRadius * data.RandomJitter;
        for (var angle = Fixed.Zero; angle < limit; angle += increment)
        {
            var x = Entity.X + radius * FixedTrig.Cos(angle);
            var y = Entity.Y + radius * FixedTrig.Sin(angle);
            x += world.LogicRandom.Range(-jitter, jitter);
            y += world.LogicRandom.Range(-jitter, jitter);
            PlaceMineAt(world, mine, mineRadius, x, y);
        }
    }

    private void PlaceMineAt(IWorldServices world, ObjectTemplate mine, Fixed mineRadius, Fixed x, Fixed y)
    {
        var orientation = world.LogicRandom.Range(-FixedTrig.Pi, FixedTrig.Pi);
        var reach = mineRadius * data.SkipIfUnderStructure;
        foreach (var other in world.Entities)
        {
            if (!other.Template.IsKindOf("STRUCTURE")) continue;
            var (dx, dy) = (other.X - x, other.Y - y);
            var limit = BoundingRadius(other.Template) + reach;
            if (SquaredReach.Of(limit).ContainsStrictly(dx * dx + dy * dy)) return;
        }
        // GenerateMinefieldBehavior.cpp: mine->setProducer(producer) — the mine stops regenerating when this object is gone.
        var id = world.RequestSpawn(mine.Index, Entity.Owner, x, y, Entity.Z, orientation, new SpawnExtras(Fixed.Zero, Producer: Entity.Id));
        if (data.Upgradable) _mines.Add(id);
    }

    public override void Describe(IStateWriter writer)
    {
        _mux.Describe(writer);
        writer.Int("nextWake", _nextWake);
        writer.Bool("generated", _generated);
        writer.Bool("upgraded", _upgraded);
        writer.BeginArray("mines");
        foreach (var id in _mines)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _mux.Load(reader);
        _nextWake = reader.Int("nextWake");
        _generated = reader.Bool("generated");
        _upgraded = reader.Bool("upgraded");
        _mines.Clear();
        foreach (var entry in reader.Array("mines")) _mines.Add(new EntityId(checked((uint)entry.UInt("id"))));
    }
}

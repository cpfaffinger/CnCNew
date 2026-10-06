using System.Collections.Immutable;
using CnCNew.Data.Registry;
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
/// The two projectile modules (<c>ProjectileUpdateInterface</c>): <c>DumbProjectileBehavior</c> (a shell on a four-point Bézier arc) and
/// <c>MissileAIUpdate</c> (a steered missile). Both detonate through <see cref="IWorldServices.DetonateProjectile"/>. There is no collision
/// system yet (phase-8 item 9): a projectile detonates at the end of its path, when it reaches its goal, when it hits the ground or when its
/// lifespan or fuel runs out — never on an object it passes. Rules, fixed-point choices and deviations: docs/GAME-RULES.md §6i.
/// </summary>
internal static class ProjectileModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<DumbProjectileData>("DumbProjectileBehavior", ModuleOwnerKind.Object, (configuration, _) => DumbProjectileData.Parse(configuration), data => new DumbProjectileBehavior(data)),
        new ModuleBinding<MissileData>("MissileAIUpdate", ModuleOwnerKind.Object, (configuration, _) => MissileData.Parse(configuration), data => new MissileAIUpdate(data)),
        new ModuleBinding<PointDefenseLaserData>("PointDefenseLaserUpdate", ModuleOwnerKind.Object, (configuration, _) => PointDefenseLaserData.Parse(configuration), data => new PointDefenseLaserUpdate(data)),
    ];

    /// <summary>The shared end of both projectiles: detonate (radius damage, effect, creation list), then kill with <c>DEATH_DETONATED</c> or destroy.</summary>
    internal static void KillDetonated(IWorldServices world, Entity projectile)
    {
        if (projectile.Body is not { } body) { world.RequestDestroy(projectile.Id); return; }
        world.Damage(projectile.Id, new DamageInfo(EntityId.Invalid, body.MaxHealth, DamageTypes.Unresistable, DeathTypes.Detonated, false));
    }
}

// ================================================================================================ DumbProjectileBehavior

/// <summary><c>DumbProjectileBehaviorModuleData</c>. <c>GarrisonHitKill*</c> act on a collision with a garrisoned building, which cannot happen without collisions (typed, not applied).</summary>
public sealed record DumbProjectileData(long MaxLifespanTicks, bool TumbleRandomly, bool DetonateCallsKill, Fixed FirstHeight, Fixed SecondHeight,
    Fixed FirstPercentIndent, Fixed SecondPercentIndent, Fixed FlightPathAdjustPerTick) : ModuleData
{
    // DEFAULT_MAX_LIFESPAN = 10 * LOGICFRAMES_PER_SECOND.
    private const long DefaultMaxLifespanFrames = 10 * SimulationRate.OriginalFramesPerSecond;

    public static DumbProjectileData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "MaxLifespan", DefaultMaxLifespanFrames),
        ModuleConfigReader.Bool(configuration, "TumbleRandomly", false),
        ModuleConfigReader.Bool(configuration, "DetonateCallsKill", false),
        ModuleConfigReader.Real(configuration, "FirstHeight", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "SecondHeight", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "FirstPercentIndent", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "SecondPercentIndent", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "FlightPathAdjustDistPerSecond", Fixed.Zero) / configuration.Time.TicksPerSecondFixed);
}

/// <summary>
/// <c>DumbProjectileBehavior.cpp</c>. Launch (<c>projectileFireAtObjectOrPosition</c>): the speed is the weapon's <c>WeaponSpeed</c>, or with
/// <c>ScaleWeaponSpeed</c> interpolated between <c>MinWeaponSpeed</c> and <c>WeaponSpeed</c> by where the 2D range lies between the minimum and the
/// unmodified attack range; the aim point is the victim's geometry centre (or the given position). <c>calcFlightPath</c>: four control points —
/// start, end, and two points at <c>FirstPercentIndent</c>/<c>SecondPercentIndent</c> of the way, raised to <c>FirstHeight</c>/<c>SecondHeight</c> above
/// the highest terrain between start and end (never below either end); the number of path points is <c>ceil(length / speed)</c> with the
/// reference's approximate Bézier length (<c>BezierSegment::getApproximateLength</c>, tolerance 1). Each update puts the shell on the next point;
/// the update after the last point (or once <c>MaxLifespan</c> is over) detonates it. <c>FlightPathAdjustDistPerSecond</c> drags the end point
/// after a moving victim and recomputes the curve with the same point count.
/// <para>
/// Fixed-point choices: the path points are evaluated from the Bernstein form at <c>t = i / (n - 1)</c> (the reference steps a binary32 forward
/// difference iterator, whose accumulated error is not reproducible); the first and last point are exact. <c>n = 1</c> — undefined behavior in the
/// reference (<c>BezFwdIterator</c> leaves the point at the origin and the orientation reads past the end) — flies straight to the end point.
/// The highest intervening terrain is sampled every half cell along the line (<c>estimateTerrainExtremesAlongLine</c> walks the partition cells).
/// </para>
/// </summary>
public sealed class DumbProjectileBehavior(DumbProjectileData data) : SimModule, IUpdateModule, IProjectileModule
{
    private static readonly Fixed LengthTolerance = Fixed.One;
    private static readonly Fixed AdjustEpsilonSquared = Fixed.FromDecimal(0.1m);
    private long _nextWake = -1;
    private EntityId _launcher;
    private EntityId _victim;
    private int _weapon = -1;
    private uint _conditions;
    private long _lifespanTick;
    private Point3 _start;
    private Point3 _end;
    private Point3 _first;
    private Point3 _second;
    private int _segments;
    private Fixed _speed;
    private int _step;
    private bool _detonated;

    public override string ModuleName => "DumbProjectileBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public EntityId Launcher => _launcher;

    /// <summary>The number of path points (<c>m_flightPathSegments</c>); detonation is one update after the last point.</summary>
    public int Segments => _segments;

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public void Launch(IWorldServices world, in ProjectileLaunch launch)
    {
        _launcher = launch.Launcher;
        _victim = launch.Victim;
        _weapon = launch.Weapon;
        _conditions = launch.Conditions;
        _lifespanTick = world.Tick + data.MaxLifespanTicks;
        var weapon = world.Weapons[_weapon];
        var victim = _victim.IsValid ? world.FindEntity(_victim) : null;
        var aim = victim is null ? new Point3(launch.X, launch.Y, launch.Z) : Center(victim);
        _speed = weapon.WeaponSpeed;
        if (weapon.ScaleWeaponSpeed && weapon.AttackRange > weapon.MinimumAttackRange)
        {
            var dx = aim.X - Entity.X;
            var dy = aim.Y - Entity.Y;
            var range = Fixed.Sqrt(dx * dx + dy * dy);
            var ratio = (range - weapon.MinimumAttackRange) / (weapon.AttackRange - weapon.MinimumAttackRange);
            _speed = ratio * (weapon.WeaponSpeed - weapon.MinWeaponSpeed) + weapon.MinWeaponSpeed;
        }
        // TumbleRandomly draws three logic numbers for the physics spin rates (presentation, but the draws keep the stream aligned).
        if (data.TumbleRandomly && Entity.Modules.Any(module => module is PhysicsBehavior))
        {
            var limit = Fixed.One / FixedTrig.Pi;
            for (var draw = 0; draw < 3; draw++) world.LogicRandom.Range(-limit, limit);
        }
        _start = new Point3(Entity.X, Entity.Y, Entity.Z);
        _end = aim;
        if (!CalcFlightPath(world, true))
        {
            world.RequestDestroy(Entity.Id);
            return;
        }
        _step = 0;
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    private static Point3 Center(Entity entity) => new(entity.X, entity.Y, entity.Z + entity.Template.CenterZDelta);

    private bool CalcFlightPath(IWorldServices world, bool recalcSegments)
    {
        if (!HighestTerrainAlongLine(world, _start, _end, out var highest)) return false;
        highest = Fixed.Max(highest, Fixed.Max(_start.Z, _end.Z));
        _first = new Point3(_start.X + (_end.X - _start.X) * data.FirstPercentIndent, _start.Y + (_end.Y - _start.Y) * data.FirstPercentIndent, highest + data.FirstHeight);
        _second = new Point3(_start.X + (_end.X - _start.X) * data.SecondPercentIndent, _start.Y + (_end.Y - _start.Y) * data.SecondPercentIndent, highest + data.SecondHeight);
        if (recalcSegments)
        {
            var length = ApproximateLength(_start, _first, _second, _end, 0);
            _segments = _speed <= Fixed.Zero ? 1 : checked((int)Math.Max(1, (length.Raw + _speed.Raw - 1) / _speed.Raw));
        }
        return true;
    }

    /// <summary>False when the line leaves the map (<c>estimateTerrainExtremesAlongLine</c> returns "not on map"); without terrain the ground is flat at 0.</summary>
    private static bool HighestTerrainAlongLine(IWorldServices world, Point3 from, Point3 to, out Fixed highest)
    {
        highest = Fixed.Zero;
        if (world.Terrain is not { } terrain) return true;
        if (!OnMap(terrain, from) || !OnMap(terrain, to)) return false;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Fixed.Sqrt(dx * dx + dy * dy);
        var steps = Math.Max(1L, (length / Fixed.FromInt(TerrainMap.CellSize / 2)).Ceiling());
        highest = Fixed.MinValue;
        for (var index = 0L; index <= steps; index++)
        {
            var x = from.X + dx * index / Fixed.FromInt(steps);
            var y = from.Y + dy * index / Fixed.FromInt(steps);
            highest = Fixed.Max(highest, world.GroundHeightAt(x, y));
        }
        return true;
    }

    private static bool OnMap(TerrainMap terrain, Point3 point)
        => point.X >= Fixed.Zero && point.Y >= Fixed.Zero && point.X <= terrain.ExtentX && point.Y <= terrain.ExtentY;

    /// <summary><c>BezierSegment::getApproximateLength</c>: halve until the control polygon is within the tolerance of the chord, then take the mean of both.</summary>
    internal static Fixed ApproximateLength(Point3 p0, Point3 p1, Point3 p2, Point3 p3, int depth)
    {
        var chord = Length(p0, p3);
        var polygon = Length(p0, p1) + Length(p1, p2) + Length(p2, p3);
        if (polygon - chord > LengthTolerance && depth < 24)
        {
            var (a0, a1, a2, a3, b1, b2) = SplitHalf(p0, p1, p2, p3);
            return ApproximateLength(p0, a1, a2, a3, depth + 1) + ApproximateLength(a3, b1, b2, p3, depth + 1);
            // a0 == p0 by construction.
        }
        return (chord + polygon) / Fixed.FromInt(2);
    }

    /// <summary><c>BezierSegment::splitSegmentAtT(0.5)</c> (de Casteljau).</summary>
    private static (Point3, Point3, Point3, Point3, Point3, Point3) SplitHalf(Point3 p0, Point3 p1, Point3 p2, Point3 p3)
    {
        var p01 = Mid(p0, p1);
        var p12 = Mid(p1, p2);
        var p23 = Mid(p2, p3);
        var left = Mid(p01, p12);
        var right = Mid(p12, p23);
        var middle = Mid(left, right);
        return (p0, p01, left, middle, right, p23);
    }

    private static Point3 Mid(Point3 a, Point3 b) => new((a.X + b.X) / Fixed.FromInt(2), (a.Y + b.Y) / Fixed.FromInt(2), (a.Z + b.Z) / Fixed.FromInt(2));

    private static Fixed Length(Point3 a, Point3 b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var dz = b.Z - a.Z;
        return Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Point <paramref name="index"/> of the <see cref="Segments"/> path points (Bernstein form, exact end points).</summary>
    internal Point3 PathPoint(int index)
    {
        if (_segments <= 1 || index >= _segments - 1) return _end;
        if (index <= 0) return _start;
        var t = Fixed.FromInt(index) / Fixed.FromInt(_segments - 1);
        var u = Fixed.One - t;
        var b0 = u * u * u;
        var b1 = Fixed.FromInt(3) * u * u * t;
        var b2 = Fixed.FromInt(3) * u * t * t;
        var b3 = t * t * t;
        return new Point3(
            b0 * _start.X + b1 * _first.X + b2 * _second.X + b3 * _end.X,
            b0 * _start.Y + b1 * _first.Y + b2 * _second.Y + b3 * _end.Y,
            b0 * _start.Z + b1 * _first.Z + b2 * _second.Z + b3 * _end.Z);
    }

    public long Update(IWorldServices world)
    {
        if (_detonated) return SimulationTiming.SleepForever;
        if (_lifespanTick != 0 && world.Tick >= _lifespanTick) { Detonate(world); return SimulationTiming.SleepForever; }
        if (_step >= _segments) { Detonate(world); return SimulationTiming.SleepForever; }
        if (_victim.IsValid && data.FlightPathAdjustPerTick > Fixed.Zero && world.FindEntity(_victim) is { DestroyRequested: false } victim)
        {
            var target = Center(victim);
            var dx = target.X - _end.X;
            var dy = target.Y - _end.Y;
            var dz = target.Z - _end.Z;
            var movedSquared = dx * dx + dy * dy + dz * dz;
            if (movedSquared > AdjustEpsilonSquared)
            {
                var moved = Fixed.Sqrt(movedSquared);
                var step = Fixed.Min(moved, data.FlightPathAdjustPerTick);
                _end = new Point3(_end.X + dx * step / moved, _end.Y + dy * step / moved, _end.Z + dz * step / moved);
                if (!CalcFlightPath(world, false)) { Detonate(world); return SimulationTiming.SleepForever; }
            }
        }
        var point = PathPoint(_step);
        var previous = _step > 0 ? PathPoint(_step - 1) : _start;
        if (point.X != previous.X || point.Y != previous.Y) Entity.Angle = FixedTrig.Atan2(point.Y - previous.Y, point.X - previous.X);
        Entity.X = point.X;
        Entity.Y = point.Y;
        Entity.Z = point.Z;
        _step++;
        return SimulationTiming.SleepNone;
    }

    /// <summary><c>DumbProjectileBehavior::detonate</c>: once only; the weapon detonates, then the shell dies (<c>DetonateCallsKill</c>) or is destroyed.</summary>
    private void Detonate(IWorldServices world)
    {
        if (_detonated) return;
        _detonated = true;
        if (_weapon >= 0)
        {
            world.DetonateProjectile(Entity, world.Weapons[_weapon], _launcher, _conditions);
            if (data.DetonateCallsKill) ProjectileModules.KillDetonated(world, Entity);
            else world.RequestDestroy(Entity.Id);
        }
        else ProjectileModules.KillDetonated(world, Entity);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.UInt("launcher", _launcher.Value);
        writer.UInt("victim", _victim.Value);
        writer.Int("weapon", _weapon);
        writer.UInt("conditions", _conditions);
        writer.Int("lifespanTick", _lifespanTick);
        WritePoint(writer, "start", _start);
        WritePoint(writer, "end", _end);
        WritePoint(writer, "first", _first);
        WritePoint(writer, "second", _second);
        writer.Int("segments", _segments);
        writer.Fixed("speed", _speed);
        writer.Int("step", _step);
        writer.Bool("detonated", _detonated);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _launcher = new EntityId(checked((uint)reader.UInt("launcher")));
        _victim = new EntityId(checked((uint)reader.UInt("victim")));
        _weapon = checked((int)reader.Int("weapon"));
        _conditions = checked((uint)reader.UInt("conditions"));
        _lifespanTick = reader.Int("lifespanTick");
        _start = ReadPoint(reader, "start");
        _end = ReadPoint(reader, "end");
        _first = ReadPoint(reader, "first");
        _second = ReadPoint(reader, "second");
        _segments = checked((int)reader.Int("segments"));
        _speed = reader.Fixed("speed");
        _step = checked((int)reader.Int("step"));
        _detonated = reader.Bool("detonated");
    }

    internal static void WritePoint(IStateWriter writer, string name, Point3 point)
    {
        // The three names are built only for a writer that keeps them (the snapshot); the hash drops names and this runs for every touched projectile and aircraft each tick.
        if (!writer.KeepsNames)
        {
            writer.Fixed(name, point.X);
            writer.Fixed(name, point.Y);
            writer.Fixed(name, point.Z);
            return;
        }
        writer.Fixed(name + "X", point.X);
        writer.Fixed(name + "Y", point.Y);
        writer.Fixed(name + "Z", point.Z);
    }

    internal static Point3 ReadPoint(IStateReader reader, string name) => new(reader.Fixed(name + "X"), reader.Fixed(name + "Y"), reader.Fixed(name + "Z"));
}

// ======================================================================================================= MissileAIUpdate

/// <summary>
/// <c>MissileAIUpdateModuleData</c> in simulation units: velocities per tick, durations in ticks, the lock distance defaults to 75, the kill-self
/// delay to 3 ticks (the reference constructor). <c>DistanceToTargetBeforeDiving</c> steers the preferred-height flight of cruise missiles and
/// <c>DistanceScatterWhenJammed</c> the reaction to ECM jamming; neither exists here (typed, not applied). <c>GarrisonHitKill*</c>: see
/// <see cref="DumbProjectileData"/>.
/// </summary>
public sealed record MissileData(bool TryToFollowTarget, long FuelLifetimeTicks, long IgnitionDelayTicks, Fixed InitialVelocity, Fixed DistanceToTravelBeforeTurning,
    Fixed LockDistance, string IgnitionFx, bool UseWeaponSpeed, bool DetonateOnNoFuel, bool DetonateCallsKill, long KillSelfDelayTicks) : ModuleData
{
    public static MissileData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Bool(configuration, "TryToFollowTarget", true),
        ModuleConfigReader.Ticks(configuration, "FuelLifetime", 0),
        ModuleConfigReader.Ticks(configuration, "IgnitionDelay", 0),
        ModuleConfigReader.Real(configuration, "InitialVelocity", Fixed.Zero) / configuration.Time.TicksPerSecondFixed,
        ModuleConfigReader.Real(configuration, "DistanceToTravelBeforeTurning", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "DistanceToTargetForLock", Fixed.FromInt(75)),
        ModuleConfigReader.Text(configuration, "IgnitionFX"),
        ModuleConfigReader.Bool(configuration, "UseWeaponSpeed", false),
        ModuleConfigReader.Bool(configuration, "DetonateOnNoFuel", false),
        ModuleConfigReader.Bool(configuration, "DetonateCallsKill", false),
        ModuleConfigReader.Ticks(configuration, "KillSelfDelay", 3));
}

/// <summary>
/// <c>MissileAIUpdate.cpp</c> as a kinematic state machine (the reference drives a THRUST locomotor through <c>PhysicsBehavior</c>):
/// <list type="number">
/// <item><description><b>Launch</b> (<c>doLaunchState</c>): the missile coasts along its launch direction — the launcher's heading, raised by
/// <c>2 · max(0, dz) / max(1, xy distance)</c> and normalised — at <c>InitialVelocity</c> (with <c>UseWeaponSpeed</c> the weapon speed, which then
/// also caps speed and acceleration) until <c>IgnitionDelay</c> ticks have passed since the launch.</description></item>
/// <item><description><b>Ignition</b>: the warhead arms, <c>IgnitionFX</c> is requested, fuel lasts <c>FuelLifetime</c> ticks (0: forever).</description></item>
/// <item><description><b>Attack without turning</b> until <c>DistanceToTravelBeforeTurning</c> is flown (measured from the previous tick's position,
/// as the reference does before moving), then <b>Attack</b>: with fuel the missile accelerates by the locomotor's acceleration up to its speed and
/// turns at will (<c>setMaxTurnRate(BIGNUM)</c>) towards its goal — the victim's position while it follows one
/// (<c>TryToFollowTarget</c>), otherwise the aim point 10 above the ground (<c>APPROACH_HEIGHT</c>, only with a lock distance).</description></item>
/// <item><description>Within the <b>lock distance</b> (2D; halved for a position shot, which then aims at the ground point again) it switches to
/// <b>Kill</b> and flies straight at the goal (the braking "cheat" of the reference); arriving there detonates it.</description></item>
/// <item><description>Out of fuel: detonate (<c>DetonateOnNoFuel</c>) or coast without acceleration or turning under gravity (<c>GameData.ini</c>).
/// A followed victim that is gone makes it run out of fuel at once and remove itself without damage (<c>airborneTargetGone</c>).</description></item>
/// <item><description>After detonating it waits <c>KillSelfDelay</c> ticks, then dies (<c>DetonateCallsKill</c>) or is destroyed.</description></item>
/// </list>
/// Collision stand-ins (no collision system): an armed missile that reaches its goal point in any attack state, or the ground, detonates there;
/// an unarmed one reaching the ground is destroyed; below height 0 it is destroyed (<c>newPos.z &lt; 0</c>). A missile marked by a plane's <c>CountermeasuresBehavior</c> gives up its victim for the closest flare after the decoy delay and then does no damage. Not modeled:
/// jamming, preferred-height cruise flight, bridge layers. All vectors are <see cref="Fixed"/>; directions are normalised with the
/// fixed-point square root.
/// </summary>
public sealed class MissileAIUpdate(MissileData data) : SimModule, IUpdateModule, IProjectileModule
{
    private static readonly Fixed ApproachHeight = Fixed.FromInt(10);

    internal enum MissileState : byte { PreLaunch, Launch, Ignition, AttackNoTurn, Attack, Kill, KillSelf, Dead }

    private long _nextWake = -1;
    private MissileState _state = MissileState.PreLaunch;
    private long _stateTick;
    private EntityId _launcher;
    private EntityId _victim;
    private int _weapon = -1;
    private uint _conditions;
    private bool _armed;
    private bool _tracking;
    private long _fuelExpiration = long.MaxValue;
    private Fixed _noTurnDistanceLeft;
    private Point3 _previous;
    private Point3 _goal;
    private Point3 _originalTarget;
    private Point3 _direction;
    private Fixed _speed;
    private Fixed _maxSpeed;
    private Fixed _acceleration;
    private Fixed _verticalSpeed;
    private long _decoyTick;
    private bool _noDamage;

    public override string ModuleName => "MissileAIUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public EntityId Launcher => _launcher;
    internal MissileState State => _state;

    /// <summary><c>AIUpdateInterface::getCurrentVictim</c> of the missile: the object it follows (invalid for a position shot).</summary>
    public EntityId Victim => _victim;

    /// <summary>The missile is in its kill-self wait after detonating (<c>OBJECT_STATUS_MISSILE_KILLING_SELF</c>).</summary>
    internal bool IsKillingSelf => _state == MissileState.KillSelf;

    public override void OnSpawn(IWorldServices world)
    {
        _noTurnDistanceLeft = data.DistanceToTravelBeforeTurning;
        _stateTick = world.Tick;
        _nextWake = SimulationTiming.SleepForever;
    }

    public void Launch(IWorldServices world, in ProjectileLaunch launch)
    {
        _launcher = launch.Launcher;
        _weapon = launch.Weapon;
        _conditions = launch.Conditions;
        var weapon = world.Weapons[_weapon];
        var locomotor = Entity.Template.Locomotor;
        var initial = data.InitialVelocity;
        _maxSpeed = locomotor?.Speed ?? initial;
        _acceleration = locomotor?.Acceleration ?? Fixed.Zero;
        if (data.UseWeaponSpeed || _maxSpeed <= Fixed.Zero)
        {
            // UseWeaponSpeed: m_maxAccel = weaponSpeed, setMaxSpeed(weaponSpeed). A missile without any speed (no locomotor, no initial velocity)
            // would hang in the air forever here; it flies at the weapon speed instead (documented stand-in).
            if (data.UseWeaponSpeed) initial = weapon.WeaponSpeed;
            _maxSpeed = weapon.WeaponSpeed;
            _acceleration = weapon.WeaponSpeed;
        }

        var dz = launch.Z - Entity.Z;
        var dx = launch.X - Entity.X;
        var dy = launch.Y - Entity.Y;
        var xyDistance = Fixed.Max(Fixed.Sqrt(dx * dx + dy * dy), Fixed.One);
        var zFactor = dz > Fixed.Zero ? dz / xyDistance : Fixed.Zero;
        _direction = Normalize(new Point3(FixedTrig.Cos(Entity.Angle), FixedTrig.Sin(Entity.Angle), zFactor * Fixed.FromInt(2)));
        _speed = initial > Fixed.Zero ? initial : Fixed.Zero;
        SwitchTo(world, MissileState.Launch);

        var victim = launch.Victim.IsValid ? world.FindEntity(launch.Victim) : null;
        if (victim is not null && data.TryToFollowTarget)
        {
            _tracking = true;
            _victim = victim.Id;
            _goal = new Point3(victim.X, victim.Y, victim.Z);
            _originalTarget = _goal;
        }
        else
        {
            _originalTarget = new Point3(launch.X, launch.Y, launch.Z);
            _goal = data.LockDistance > Fixed.Zero ? _originalTarget with { Z = launch.Z + ApproachHeight } : _originalTarget;
            _victim = EntityId.Invalid;
        }
        _previous = new Point3(Entity.X, Entity.Y, Entity.Z);
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    private void SwitchTo(IWorldServices world, MissileState state)
    {
        if (_state == state) return;
        _state = state;
        _stateTick = world.Tick;
    }

    public long Update(IWorldServices world)
    {
        if (_state == MissileState.PreLaunch || _state == MissileState.Dead) return SimulationTiming.SleepForever;
        var position = new Point3(Entity.X, Entity.Y, Entity.Z);
        if (_noTurnDistanceLeft > Fixed.Zero && _state >= MissileState.Ignition)
        {
            _noTurnDistanceLeft -= Distance(position, _previous);
            _previous = position;
        }
        if (_decoyTick != 0 && _decoyTick <= world.Tick) DivertToCountermeasure(world);
        if (Entity.Z < Fixed.Zero)
        {
            world.RequestDestroy(Entity.Id);
            SwitchTo(world, MissileState.Dead);
            return SimulationTiming.SleepForever;
        }

        var accelerate = false;
        var steer = false;
        switch (_state)
        {
            case MissileState.Launch:
                if (world.Tick - _stateTick < data.IgnitionDelayTicks) break;
                SwitchTo(world, MissileState.Ignition);
                Ignite(world);
                accelerate = true;
                break;
            case MissileState.Ignition:
                Ignite(world);
                accelerate = true;
                break;
            case MissileState.AttackNoTurn:
            case MissileState.Attack:
                if (!Attack(world, _state == MissileState.Attack, out accelerate, out steer)) return Finish(world);
                break;
            case MissileState.Kill:
                if (world.Tick >= _fuelExpiration)
                {
                    if (data.DetonateOnNoFuel) Detonate(world);
                    else TargetGone(world);
                    return Finish(world);
                }
                if (_tracking && FollowedVictim(world) is null) { TargetGone(world); return Finish(world); }
                accelerate = true;
                steer = true;
                break;
            case MissileState.KillSelf:
                if (world.Tick - _stateTick < data.KillSelfDelayTicks) return SimulationTiming.SleepNone;
                if (_weapon >= 0)
                {
                    if (data.DetonateCallsKill) world.Kill(Entity.Id);
                    else world.RequestDestroy(Entity.Id);
                }
                SwitchTo(world, MissileState.Dead);
                return SimulationTiming.SleepForever;
        }
        if (_state is MissileState.KillSelf or MissileState.Dead) return Finish(world);
        Move(world, accelerate, steer);
        return Finish(world);
    }

    private long Finish(IWorldServices world) => _state == MissileState.Dead ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;

    /// <summary><c>MissileAIUpdate::setFramesTillCountermeasureDiversionOccurs</c>: <paramref name="frames"/> ticks from now the missile gives up its victim for a decoy flare.</summary>
    public void SetFramesTillCountermeasureDiversionOccurs(IWorldServices world, long frames) => _decoyTick = world.Tick + Math.Max(frames, 1);

    /// <summary>
    /// The diversion of <c>MissileAIUpdate::update</c>: the missile no longer does damage and, when its victim has a flare out, flies at the closest flare of the newest volley
    /// (<c>calculateCountermeasureToDivertTo</c>) instead, as if it had been shot at it.
    /// </summary>
    private void DivertToCountermeasure(IWorldServices world)
    {
        _decoyTick = 0;
        _noDamage = true;
        if (world.FindEntity(_victim) is not { } victim) return;
        var counter = victim.ModuleArray.OfType<ICountermeasuresModule>().FirstOrDefault();
        if (counter is null || !counter.CalculateCountermeasureToDivertTo(world, victim).IsValid) return;
        var flare = world.FindEntity(counter.CalculateCountermeasureToDivertTo(world, victim));
        if (flare is null) return;
        _victim = flare.Id;
        _tracking = true;
        _goal = new Point3(flare.X, flare.Y, flare.Z);
        _originalTarget = _goal;
    }

    /// <summary><c>doIgnitionState</c>: arm, request the ignition effect, start the fuel clock, attack without turning.</summary>
    private void Ignite(IWorldServices world)
    {
        if (data.IgnitionFx.Length > 0) world.Emit(SimEventKind.FxRequested, Entity.Id, _launcher, data.IgnitionFx, 0);
        _armed = true;
        _fuelExpiration = data.FuelLifetimeTicks > 0 ? world.Tick + data.FuelLifetimeTicks : long.MaxValue;
        SwitchTo(world, MissileState.AttackNoTurn);
    }

    /// <summary><c>doAttackState</c>; false when the missile left the attack (detonated or gave up).</summary>
    private bool Attack(IWorldServices world, bool turnOk, out bool accelerate, out bool steer)
    {
        accelerate = false;
        steer = false;
        var fueled = world.Tick < _fuelExpiration;
        if (!fueled && data.DetonateOnNoFuel)
        {
            Detonate(world);
            return false;
        }
        if (_tracking && FollowedVictim(world) is null)
        {
            TargetGone(world);
            return false;
        }
        if (data.LockDistance > Fixed.Zero)
        {
            var lockDistance = _tracking ? data.LockDistance : data.LockDistance / Fixed.FromInt(2);
            var dx = _goal.X - Entity.X;
            var dy = _goal.Y - Entity.Y;
            if (dx * dx + dy * dy < lockDistance * lockDistance)
            {
                if (!_tracking) _goal = _originalTarget;
                SwitchTo(world, MissileState.Kill);
                accelerate = fueled;
                steer = true;
                return true;
            }
        }
        if (_noTurnDistanceLeft <= Fixed.Zero) SwitchTo(world, MissileState.Attack);
        accelerate = fueled;
        steer = fueled && turnOk;
        return true;
    }

    private Entity? FollowedVictim(IWorldServices world)
    {
        var victim = world.FindEntity(_victim);
        if (victim is null || victim.DestroyRequested) return null;
        _goal = new Point3(victim.X, victim.Y, victim.Z);
        return victim;
    }

    /// <summary><c>airborneTargetGone</c>: out of fuel now, remove itself after the kill-self delay, no detonation.</summary>
    private void TargetGone(IWorldServices world)
    {
        _fuelExpiration = world.Tick;
        SwitchTo(world, MissileState.KillSelf);
    }

    private void Move(IWorldServices world, bool accelerate, bool steer)
    {
        if (accelerate) _speed = Fixed.Min(_maxSpeed, _speed + _acceleration);
        var position = new Point3(Entity.X, Entity.Y, Entity.Z);
        var fueled = world.Tick < _fuelExpiration;
        if (steer && _state is MissileState.Attack or MissileState.Kill)
        {
            var toGoal = new Point3(_goal.X - position.X, _goal.Y - position.Y, _goal.Z - position.Z);
            var remaining = Norm(toGoal);
            if (remaining <= _speed)
            {
                Place(world, _goal);
                if (_armed) Detonate(world);
                return;
            }
            _direction = new Point3(toGoal.X / remaining, toGoal.Y / remaining, toGoal.Z / remaining);
            _verticalSpeed = Fixed.Zero;
        }
        var next = new Point3(position.X + _direction.X * _speed, position.Y + _direction.Y * _speed, position.Z + _direction.Z * _speed);
        if (!fueled && _armed)
        {
            // Coasting after the fuel is gone: gravity takes over (PhysicsBehavior), no thrust and no turning.
            _verticalSpeed += world.Templates.GravityPerTickSquared;
            next = next with { Z = next.Z + _verticalSpeed };
        }
        var ground = world.GroundHeightAt(next.X, next.Y);
        if (next.Z < ground)
        {
            // projectileHandleCollision(NULL): an armed warhead detonates on the ground; an unarmed missile is inert and slides along it.
            next = next with { Z = ground };
            if (_armed)
            {
                Place(world, next);
                Detonate(world);
                return;
            }
        }
        Place(world, next);
    }

    private void Place(IWorldServices world, Point3 point)
    {
        if (point.X != Entity.X || point.Y != Entity.Y) Entity.Angle = FixedTrig.Atan2(point.Y - Entity.Y, point.X - Entity.X);
        Entity.X = point.X;
        Entity.Y = point.Y;
        Entity.Z = point.Z;
        world.MarkDirty(Entity);
    }

    /// <summary><c>MissileAIUpdate::detonate</c>: the weapon detonates (and with <c>MissileCallsOnDie</c> the missile dies), then the kill-self wait begins.</summary>
    private void Detonate(IWorldServices world)
    {
        if (_state is MissileState.KillSelf or MissileState.Dead) return;
        if (_weapon >= 0)
        {
            var weapon = world.Weapons[_weapon];
            world.DetonateProjectile(Entity, weapon, _launcher, _conditions, !_noDamage);
            if (weapon.DieOnDetonate) ProjectileModules.KillDetonated(world, Entity);
        }
        else if (!_noDamage) ProjectileModules.KillDetonated(world, Entity);
        SwitchTo(world, MissileState.KillSelf);
    }

    private static Fixed Distance(Point3 a, Point3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static Fixed Norm(Point3 v) => Fixed.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    private static Point3 Normalize(Point3 v)
    {
        var length = Norm(v);
        return length <= Fixed.Zero ? new Point3(Fixed.One, Fixed.Zero, Fixed.Zero) : new Point3(v.X / length, v.Y / length, v.Z / length);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("state", (int)_state);
        writer.Int("stateTick", _stateTick);
        writer.UInt("launcher", _launcher.Value);
        writer.UInt("victim", _victim.Value);
        writer.Int("weapon", _weapon);
        writer.UInt("conditions", _conditions);
        writer.Bool("armed", _armed);
        writer.Bool("tracking", _tracking);
        writer.Int("fuelExpiration", _fuelExpiration == long.MaxValue ? -1 : _fuelExpiration);
        writer.Fixed("noTurnDistanceLeft", _noTurnDistanceLeft);
        DumbProjectileBehavior.WritePoint(writer, "previous", _previous);
        DumbProjectileBehavior.WritePoint(writer, "goal", _goal);
        DumbProjectileBehavior.WritePoint(writer, "original", _originalTarget);
        DumbProjectileBehavior.WritePoint(writer, "direction", _direction);
        writer.Fixed("speed", _speed);
        writer.Fixed("maxSpeed", _maxSpeed);
        writer.Fixed("acceleration", _acceleration);
        writer.Fixed("verticalSpeed", _verticalSpeed);
        if (_decoyTick != 0) writer.Int("decoyTick", _decoyTick);
        if (_noDamage) writer.Bool("noDamage", true);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        var state = reader.Int("state");
        if (state < 0 || state > (int)MissileState.Dead) throw new InvalidDataException("Missile state out of range.");
        _state = (MissileState)state;
        _stateTick = reader.Int("stateTick");
        _launcher = new EntityId(checked((uint)reader.UInt("launcher")));
        _victim = new EntityId(checked((uint)reader.UInt("victim")));
        _weapon = checked((int)reader.Int("weapon"));
        _conditions = checked((uint)reader.UInt("conditions"));
        _armed = reader.Bool("armed");
        _tracking = reader.Bool("tracking");
        var fuel = reader.Int("fuelExpiration");
        _fuelExpiration = fuel < 0 ? long.MaxValue : fuel;
        _noTurnDistanceLeft = reader.Fixed("noTurnDistanceLeft");
        _previous = DumbProjectileBehavior.ReadPoint(reader, "previous");
        _goal = DumbProjectileBehavior.ReadPoint(reader, "goal");
        _originalTarget = DumbProjectileBehavior.ReadPoint(reader, "original");
        _direction = DumbProjectileBehavior.ReadPoint(reader, "direction");
        _speed = reader.Fixed("speed");
        _maxSpeed = reader.Fixed("maxSpeed");
        _acceleration = reader.Fixed("acceleration");
        _verticalSpeed = reader.Fixed("verticalSpeed");
        _decoyTick = reader.Has("decoyTick") ? reader.Int("decoyTick") : 0;
        _noDamage = reader.Has("noDamage") && reader.Bool("noDamage");
    }
}


// =============================================================================================== PointDefenseLaserUpdate

/// <summary><c>PointDefenseLaserUpdateModuleData</c>: the laser weapon, the target kinds (primary before secondary), the scan interval in ticks and the scan range.</summary>
public sealed record PointDefenseLaserData(string Weapon, ImmutableArray<string> PrimaryTargetTypes, ImmutableArray<string> SecondaryTargetTypes, long ScanTicks, Fixed ScanRange) : ModuleData
{
    public static PointDefenseLaserData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Text(configuration, "WeaponTemplate"),
        ModuleConfigReader.Flags(configuration, "PrimaryTargetTypes")?.Names ?? [],
        ModuleConfigReader.Flags(configuration, "SecondaryTargetTypes")?.Names ?? [],
        ModuleConfigReader.Ticks(configuration, "ScanRate", 0),
        ModuleConfigReader.Real(configuration, "ScanRange", Fixed.Zero));
}

/// <summary>
/// <c>PointDefenseLaserUpdate.cpp</c>: shoots down enemy projectiles (and, with <c>SecondaryTargetTypes</c>, infantry) now that projectiles are objects.
/// Every <c>ScanRate</c> ticks it picks the closest enemy of the primary kinds within the laser's range, else of the secondary kinds, else the closest
/// one outside the range but inside <c>ScanRange</c> (all 2D, <c>FROM_CENTER_2D</c>; equal distances by entity id — the reference's partition order is
/// not reproducible); it skips undetected stealth and ground targets for a weapon without <c>AntiGround</c>. Between scans it fires at the tracked
/// target whenever the laser is ready (<c>DelayBetweenShots</c>, no weapon bonus); a target that dies or leaves the range is dropped and the next scan
/// comes after <c>GameLogicRandomValue(0, 3)</c> ticks (0: at once). The shot is a regular weapon firing (<see cref="IWorldServices.FireWeaponAt"/>).
/// </summary>
public sealed class PointDefenseLaserUpdate(PointDefenseLaserData data) : SimModule, IUpdateModule
{
    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED");
    private static readonly int DetectedBit = ObjectStatusBits.Index("DETECTED");
    private long _nextWake = -1;
    private EntityId _target;
    private bool _inRange;
    private long _nextScan;
    private long _nextShot;

    // Which of the two target lists a template belongs to, by template index: 0 not looked at yet, 1 primary, 2 secondary, 3 neither. A template's kinds and
    // the module data never change, so this is a cache of a fixed answer - not state: it is not described and fills again after a restore.
    private const byte ClassPrimary = 1;
    private const byte ClassSecondary = 2;
    private const byte ClassNone = 3;
    private byte[] _targetClass = [];

    public override string ModuleName => "PointDefenseLaserUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
    {
        if (!world.Weapons.TryGet(data.Weapon, out _)) throw new InvalidDataException($"PointDefenseLaserUpdate references the unknown weapon '{data.Weapon}'.");
        _nextWake = world.Tick + SimulationTiming.SleepNone;
    }

    public long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        var weapon = world.Weapons.TryGet(data.Weapon, out var info) ? info : throw new InvalidDataException($"PointDefenseLaserUpdate references the unknown weapon '{data.Weapon}'.");
        if (_nextScan > 0)
        {
            _nextScan--;
            FireWhenReady(world, weapon);
            return SimulationTiming.SleepNone;
        }
        _nextScan = data.ScanTicks;
        if (ScanClosestTarget(world, weapon)) FireWhenReady(world, weapon);
        return SimulationTiming.SleepNone;
    }

    private void FireWhenReady(IWorldServices world, WeaponInfo weapon)
    {
        var target = _target.IsValid ? world.FindEntity(_target) : null;
        if (target is { DestroyRequested: true }) target = null;
        if (target is not null)
        {
            if (Distance2D(target) < weapon.AttackRange) _inRange = true;
            else if (_inRange)
            {
                _nextScan = world.LogicRandom.Range(0, 3);
                _target = EntityId.Invalid;
                if (_nextScan == 0)
                {
                    ScanClosestTarget(world, weapon);
                    _nextScan = data.ScanTicks;
                    target = null;
                }
            }
            else _inRange = false;
        }
        if (_nextShot > 0)
        {
            _nextShot--;
            return;
        }
        if (target is null || !_inRange) return;
        if (!target.EffectivelyDead)
        {
            var damage = world.FireWeaponAt(Entity, weapon, target, target.X, target.Y, target.Z);
            world.Emit(SimEventKind.WeaponFired, Entity.Id, target.Id, weapon.Name, damage.Raw);
            _nextShot = weapon.DelayBetweenShotsTicks;
        }
        if (target.EffectivelyDead)
        {
            _nextScan = world.LogicRandom.Range(0, 3);
            _target = EntityId.Invalid;
            if (_nextScan == 0)
            {
                ScanClosestTarget(world, weapon);
                _nextScan = data.ScanTicks;
            }
        }
    }

    private Fixed Distance2D(Entity other)
    {
        var dx = other.X - Entity.X;
        var dy = other.Y - Entity.Y;
        return Fixed.Sqrt(dx * dx + dy * dy);
    }

    /// <summary><c>scanClosestTarget</c>: primary in range, secondary in range, primary out of range, secondary out of range.</summary>
    private bool ScanClosestTarget(IWorldServices world, WeaponInfo weapon)
    {
        // The candidates are the entities of the listed kinds (the world's kind lists) instead of every entity of the world. Each pick below is the minimum
        // of (distance, id) over its candidates - Closer breaks ties by id itself -, and an out-of-range pick is only used when its list has no in-range
        // candidate at all, in which case it was never skipped. So the result does not depend on the order the candidates come in, and a candidate that
        // has two of the listed kinds and comes twice changes nothing the second time.
        var picks = default(ScanPicks);
        var scanSquared = data.ScanRange * data.ScanRange;
        foreach (var kind in data.PrimaryTargetTypes) ScanCandidates(world, weapon, world.EntitiesOfKind(kind), scanSquared, ref picks);
        foreach (var kind in data.SecondaryTargetTypes) ScanCandidates(world, weapon, world.EntitiesOfKind(kind), scanSquared, ref picks);
        var pick = picks.InRange0 ?? picks.InRange1;
        _inRange = pick is not null;
        pick ??= picks.OutOfRange0 ?? picks.OutOfRange1;
        _target = pick?.Id ?? EntityId.Invalid;
        if (pick is null) _inRange = false;
        return pick is not null;
    }

    /// <summary>Index 0 is the primary list, 1 the secondary one.</summary>
    private struct ScanPicks
    {
        public Entity? InRange0, InRange1, OutOfRange0, OutOfRange1;
        public Fixed InDistance0, InDistance1, OutDistance0, OutDistance1;
    }

    private void ScanCandidates(IWorldServices world, WeaponInfo weapon, EntityList candidates, Fixed scanSquared, ref ScanPicks picks)
    {
        foreach (var other in candidates.AsSpan())
        {
            if (other.DestroyRequested || other == Entity || other.ContainedBy.IsValid) continue;
            var targetClass = TargetClassOf(other.Template);
            if (targetClass == ClassNone) continue;
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            if (dx * dx + dy * dy > scanSquared) continue;
            if (!world.IsSignificantlyAboveTerrain(other) && !weapon.AntiGround) continue;
            if (world.RelationshipOf(Entity.Owner, other.Owner) != Relationship.Enemies) continue;
            if (other.HasStatus(StealthedBit) && !other.HasStatus(DetectedBit)) continue;
            var distance = Distance2D(other);
            if (targetClass == ClassPrimary)
            {
                if (distance <= weapon.AttackRange)
                {
                    if (Closer(picks.InRange0, picks.InDistance0, other, distance)) { picks.InRange0 = other; picks.InDistance0 = distance; }
                }
                else if (picks.InRange0 is null && Closer(picks.OutOfRange0, picks.OutDistance0, other, distance))
                {
                    picks.OutOfRange0 = other;
                    picks.OutDistance0 = distance;
                }
            }
            else if (distance <= weapon.AttackRange)
            {
                if (Closer(picks.InRange1, picks.InDistance1, other, distance)) { picks.InRange1 = other; picks.InDistance1 = distance; }
            }
            else if (picks.InRange1 is null && Closer(picks.OutOfRange1, picks.OutDistance1, other, distance))
            {
                picks.OutOfRange1 = other;
                picks.OutDistance1 = distance;
            }
        }
    }

    /// <summary>Primary before secondary, as the scan asked each time: <c>PrimaryTargetTypes</c> any kind matches, else <c>SecondaryTargetTypes</c>, else neither.</summary>
    private byte TargetClassOf(ObjectTemplate template)
    {
        var index = template.Index;
        if (index >= _targetClass.Length) Array.Resize(ref _targetClass, Math.Max(index + 1, _targetClass.Length * 2));
        var known = _targetClass[index];
        if (known != 0) return known;
        known = ClassNone;
        foreach (var kind in data.PrimaryTargetTypes)
            if (template.IsKindOf(kind)) { known = ClassPrimary; break; }
        if (known == ClassNone)
            foreach (var kind in data.SecondaryTargetTypes)
                if (template.IsKindOf(kind)) { known = ClassSecondary; break; }
        _targetClass[index] = known;
        return known;
    }

    private static bool Closer(Entity? best, Fixed bestDistance, Entity candidate, Fixed distance)
        => best is null || distance < bestDistance || (distance == bestDistance && candidate.Id.Value < best.Id.Value);

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.UInt("target", _target.Value);
        writer.Bool("inRange", _inRange);
        writer.Int("nextScan", _nextScan);
        writer.Int("nextShot", _nextShot);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _target = new EntityId(checked((uint)reader.UInt("target")));
        _inRange = reader.Bool("inRange");
        _nextScan = reader.Int("nextScan");
        _nextShot = reader.Int("nextShot");
    }
}
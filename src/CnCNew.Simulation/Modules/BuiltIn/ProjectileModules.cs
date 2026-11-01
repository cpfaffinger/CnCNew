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
    /// <summary>
    /// <c>DumbProjectileBehavior::projectileHandleCollision</c> with a garrisoned building (F-60, clearing a garrison): up to <paramref name="count"/> living riders of the kinds asked
    /// for (<c>isKindOfMulti</c>), in the order they are in it, die and the launcher scores them; when one died the FX plays at the building and the shell is destroyed without
    /// its explosion (true). Nothing happens - and the shell explodes as usual - when the victim holds no rider it may kill or is immune to clearing.
    /// </summary>
    public static bool ClearGarrison(IWorldServices world, Entity shell, EntityId launcher, EntityId victim, int count, ImmutableArray<string> required, ImmutableArray<string> forbidden, string fx)
    {
        if (count <= 0 || world.FindEntity(victim) is not { Container: { CanBeCleared: true } contain } building) return false;
        var killed = 0;
        foreach (var id in contain.Contained.ToArray())
        {
            if (killed >= count) break;
            if (world.FindEntity(id) is not { EffectivelyDead: false } rider) continue;
            if (required.Any(kind => !rider.Template.IsKindOf(kind)) || forbidden.Any(rider.Template.IsKindOf)) continue;
            world.Damage(rider.Id, new DamageInfo(launcher, Fixed.Zero, DamageTypes.Unresistable, DeathTypes.Normal, true));
            killed++;
        }
        if (killed == 0) return false;
        if (fx.Length > 0) world.EmitAt(SimEventKind.FxRequested, building.Id, EntityId.Invalid, fx, 0, building.X, building.Y, building.Z);
        world.RequestDestroy(shell.Id);
        return true;
    }

    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<DumbProjectileData>("DumbProjectileBehavior", ModuleOwnerKind.Object, (configuration, _) => DumbProjectileData.Parse(configuration), data => new DumbProjectileBehavior(data)),
        new ModuleBinding<MissileData>("MissileAIUpdate", ModuleOwnerKind.Object, (configuration, _) => MissileData.Parse(configuration), data => new MissileAIUpdate(data)),
        new ModuleBinding<PointDefenseLaserData>("PointDefenseLaserUpdate", ModuleOwnerKind.Object, (configuration, _) => PointDefenseLaserData.Parse(configuration), data => new PointDefenseLaserUpdate(data)),
    ];

    /// <summary>
    /// A weapon speed per tick as the speed per logic frame of the original, the unit the reference flies in and the 30 Hz catalog holds; the "instant" sentinel
    /// (<see cref="WeaponInfo.InstantSpeed"/>) is a value per frame already and stays itself. 30 Hz: the argument.
    /// </summary>
    internal static Fixed FrameSpeed(SimulationTime time, Fixed speed)
        => time.TicksPerOriginalFrame == 1 || speed == WeaponInfo.InstantSpeed ? speed : time.PerTickToPerOriginalFrame(speed);

    /// <summary>Gravity per logic frame² of the original: the value the 30 Hz catalog computes (<see cref="TemplateCatalog.GravityPerTickSquared"/> at 30 Hz), at every rate.</summary>
    internal static Fixed GravityPerFrameSquared(IWorldServices world) => SimulationRate.Original.Time.PerSecondSquaredToPerTickSquared(world.Templates.Gravity);

    /// <summary>
    /// A followed object where the frame it has begun ends (<see cref="Entity.FrameEnd"/>, docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3): what the 30 Hz projectile sees of a
    /// victim that moved before it in the tick. 30 Hz: the victim's position.
    /// </summary>
    internal static Point3 FrameEnd(Entity entity, long tick)
    {
        var (x, y, z) = entity.FrameEnd(tick);
        return new Point3(x, y, z);
    }

    /// <summary>
    /// The stand-in for <c>PhysicsBehavior</c>'s collision of a projectile with the object it was fired at (<c>projectileHandleCollision</c>; the port has no collision system): a point
    /// with the projectile's radius meets the victim's box or cylinder, from its feet to its top, where the frame it has begun ends.
    /// </summary>
    internal static bool Touches(Entity victim, long tick, Point3 point, Fixed radius)
    {
        var (x, y, z) = victim.FrameEnd(tick);
        var template = victim.Template;
        var height = template.Geometry.Equals("SPHERE", StringComparison.OrdinalIgnoreCase) ? template.GeometryMajorRadius : template.GeometryHeight;
        if (point.Z < z || point.Z > z + height) return false;
        var (dx, dy) = (point.X - x, point.Y - y);
        if (template.IsBox)
        {
            var angle = victim.FrameEndAngle(tick);
            var (cos, sin) = (FixedTrig.Cos(angle), FixedTrig.Sin(angle));
            return (dx * cos + dy * sin).Abs() <= template.GeometryMajorRadius + radius && (dy * cos - dx * sin).Abs() <= template.GeometryMinorRadius + radius;
        }
        return SquaredReach.Of(template.GeometryMajorRadius + radius).Contains(dx * dx + dy * dy);
    }

    /// <summary>The shared end of both projectiles: detonate (radius damage, effect, creation list), then kill with <c>DEATH_DETONATED</c> or destroy.</summary>
    internal static void KillDetonated(IWorldServices world, Entity projectile)
    {
        if (projectile.Body is not { } body) { world.RequestDestroy(projectile.Id); return; }
        world.Damage(projectile.Id, new DamageInfo(EntityId.Invalid, body.MaxHealth, DamageTypes.Unresistable, DeathTypes.Detonated, false));
    }
}

// ================================================================================================ DumbProjectileBehavior

/// <summary>
/// <c>DumbProjectileBehaviorModuleData</c>. <c>GarrisonHitKill*</c> act when the shell hits a garrisoned building (F-60, clearing a garrison): the port's shells collide with nothing on
/// their way, so it is applied when the shell reaches its victim and that is the building.
/// </summary>
public sealed record DumbProjectileData(long MaxLifespanTicks, bool TumbleRandomly, bool DetonateCallsKill, Fixed FirstHeight, Fixed SecondHeight,
    Fixed FirstPercentIndent, Fixed SecondPercentIndent, Fixed FlightPathAdjustPerTick) : ModuleData
{
    public int GarrisonHitKillCount { get; init; }
    public ImmutableArray<string> GarrisonHitKillRequiredKindOf { get; init; } = [];
    public ImmutableArray<string> GarrisonHitKillForbiddenKindOf { get; init; } = [];
    public string GarrisonHitKillFx { get; init; } = string.Empty;

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
        ModuleConfigReader.Real(configuration, "FlightPathAdjustDistPerSecond", Fixed.Zero) / configuration.Time.TicksPerSecondFixed)
    {
        GarrisonHitKillCount = checked((int)ModuleConfigReader.Int(configuration, "GarrisonHitKillCount", 0)),
        GarrisonHitKillRequiredKindOf = ModuleConfigReader.Flags(configuration, "GarrisonHitKillRequiredKindOf")?.Names ?? [],
        GarrisonHitKillForbiddenKindOf = ModuleConfigReader.Flags(configuration, "GarrisonHitKillForbiddenKindOf")?.Names ?? [],
        GarrisonHitKillFx = ModuleConfigReader.Text(configuration, "GarrisonHitKillFX"),
    };
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
/// The path is defined on the frame grid of the original (docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md): the speed is the speed per logic frame, so the path has one point per frame of
/// flight at every tick rate; the first point is taken one frame after the launch, every further one a frame later, and the detonation comes one frame after the last point. Within a
/// frame the shell flies the way to that frame's point in equal shares and reaches it with the frame's last tick. Impact time and point are those of 30 Hz; at 30 Hz a frame is a
/// tick and nothing differs.
/// </para>
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
    // Ticks flown since the path point of this frame: 0 on the tick that takes a point. Always 0 at 30 Hz (and then not written).
    private int _subStep;
    private bool _detonated;

    public override string ModuleName => "DumbProjectileBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public EntityId Launcher => _launcher;

    /// <summary>The number of path points (<c>m_flightPathSegments</c>), one per logic frame of the original; detonation is one frame after the last point.</summary>
    public int Segments => _segments;

    /// <summary>
    /// <c>m_orientToFlightPath</c>: the shell's nose along the step it flies, the step to the second point before the first has been taken, "as if it were on frame 1". A tumbling shell has
    /// no such pitch (<c>TumbleRandomly</c>).
    /// </summary>
    public Fixed FlightPitch
    {
        get
        {
            if (_segments < 2 || data.TumbleRandomly) return Fixed.Zero;
            var last = Math.Clamp(_step - 1, 1, _segments - 1);
            var (from, to) = (PathPoint(last - 1), PathPoint(last));
            var (dx, dy) = (to.X - from.X, to.Y - from.Y);
            return FixedTrig.Atan2(to.Z - from.Z, Fixed.Sqrt(dx * dx + dy * dy));
        }
    }

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
        var aim = victim is null ? new Point3(launch.X, launch.Y, launch.Z) : Center(victim, world.Tick);
        // Per logic frame of the original at every rate: the path gets one point per frame of flight.
        _speed = ProjectileModules.FrameSpeed(world.Time, weapon.WeaponSpeed);
        if (weapon.ScaleWeaponSpeed && weapon.AttackRange > weapon.MinimumAttackRange)
        {
            var dx = aim.X - Entity.X;
            var dy = aim.Y - Entity.Y;
            var range = Fixed.Sqrt(dx * dx + dy * dy);
            var ratio = (range - weapon.MinimumAttackRange) / (weapon.AttackRange - weapon.MinimumAttackRange);
            var slowest = ProjectileModules.FrameSpeed(world.Time, weapon.MinWeaponSpeed);
            _speed = ratio * (_speed - slowest) + slowest;
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
        _subStep = 0;
        // The first point is taken one logic frame after the launch (the next tick at 30 Hz); it is the launch point itself.
        world.WakeAt(Entity, this, world.Tick + world.Time.OriginalFrames(SimulationTiming.SleepNone));
    }

    // The victim where the frame it has begun ends (Entity.FrameEnd): what the 30 Hz shell sees of a victim that moved before it in the tick. 30 Hz: its position.
    private static Point3 Center(Entity entity, long tick)
    {
        var (x, y, z) = entity.FrameEnd(tick);
        return new Point3(x, y, z + entity.Template.CenterZDelta);
    }

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
        if (_subStep != 0) return FlyTowardNextPoint(world);
        if (_lifespanTick != 0 && world.Tick >= _lifespanTick) { Detonate(world); return SimulationTiming.SleepForever; }
        if (_step >= _segments)
        {
            if (!ProjectileModules.ClearGarrison(world, Entity, _launcher, _victim, data.GarrisonHitKillCount, data.GarrisonHitKillRequiredKindOf, data.GarrisonHitKillForbiddenKindOf, data.GarrisonHitKillFx))
                Detonate(world);
            return SimulationTiming.SleepForever;
        }
        if (_victim.IsValid && data.FlightPathAdjustPerTick > Fixed.Zero && world.FindEntity(_victim) is { DestroyRequested: false } victim)
        {
            var target = Center(victim, world.Tick);
            var dx = target.X - _end.X;
            var dy = target.Y - _end.Y;
            var dz = target.Z - _end.Z;
            var movedSquared = dx * dx + dy * dy + dz * dz;
            if (movedSquared > AdjustEpsilonSquared)
            {
                var moved = Fixed.Sqrt(movedSquared);
                var step = Fixed.Min(moved, data.FlightPathAdjustPerTick * world.Time.TicksPerOriginalFrame);
                _end = new Point3(_end.X + dx * step / moved, _end.Y + dy * step / moved, _end.Z + dz * step / moved);
                if (!CalcFlightPath(world, false)) { Detonate(world); return SimulationTiming.SleepForever; }
            }
        }
        var point = PathPoint(_step);
        var previous = _step > 0 ? PathPoint(_step - 1) : _start;
        if (point.X != previous.X || point.Y != previous.Y) Entity.Angle = FixedTrig.Atan2(point.Y - previous.Y, point.X - previous.X);
        // "see if we' still in the bridge's xy area": a shell that sinks through a deck blows up on it.
        if (world.ProjectileHitsDeck(point.X, point.Y, previous.Z, point.Z, out var deckZ))
        {
            (Entity.X, Entity.Y, Entity.Z) = (point.X, point.Y, deckZ);
            Entity.SetAhead(world.Tick, Fixed.Zero, Fixed.Zero, Fixed.Zero);
            Detonate(world);
            return SimulationTiming.SleepForever;
        }
        var ticks = world.Time.TicksPerOriginalFrame;
        if (ticks == 1)
        {
            Entity.X = point.X;
            Entity.Y = point.Y;
            Entity.Z = point.Z;
        }
        else FlyToward(world.Tick, point, ticks);
        _step++;
        _subStep = world.Time.NextSubStep(0);
        return SimulationTiming.SleepNone;
    }

    /// <summary>
    /// Between two frames of the original: one more share of the way to the point of this frame (<c>_step - 1</c>), which the shell reaches with the last tick of the frame — the
    /// state at the frame boundary is the one of 30 Hz. The heading stays that of the frame. Never runs at 30 Hz.
    /// </summary>
    private long FlyTowardNextPoint(IWorldServices world)
    {
        FlyToward(world.Tick, PathPoint(_step - 1), world.Time.TicksPerOriginalFrame - _subStep);
        _subStep = world.Time.NextSubStep(_subStep);
        return SimulationTiming.SleepNone;
    }

    /// <summary>Flies one of <paramref name="sharesLeft"/> equal shares of the way that is left to <paramref name="point"/>; the last share lands on it exactly.</summary>
    private void FlyToward(long tick, Point3 point, int sharesLeft)
    {
        if (sharesLeft <= 1)
        {
            Entity.X = point.X;
            Entity.Y = point.Y;
            Entity.Z = point.Z;
            Entity.SetAhead(tick, Fixed.Zero, Fixed.Zero, Fixed.Zero);
            return;
        }
        var left = Fixed.FromInt(sharesLeft);
        Entity.X += (point.X - Entity.X) / left;
        Entity.Y += (point.Y - Entity.Y) / left;
        Entity.Z += (point.Z - Entity.Z) / left;
        // The rest of the frame stays readable on the object (Entity.FrameEndX …): who looks at the shell in this tick sees it at the frame's point, as at 30 Hz.
        Entity.SetAhead(tick, point.X - Entity.X, point.Y - Entity.Y, point.Z - Entity.Z);
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
        if (_subStep != 0) writer.Int("subStep", _subStep);
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
        _subStep = reader.Has("subStep") ? checked((int)reader.Int("subStep")) : 0;
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
/// delay to 3 ticks (the reference constructor). <c>DistanceToTargetBeforeDiving</c> ends the preferred height of a cruise missile (<see cref="MissileData.DiveDistance"/>);
/// <c>DistanceScatterWhenJammed</c>, the reaction to ECM jamming, does not exist here (typed, not applied). <c>GarrisonHitKill*</c> act as a shell's do
/// (<see cref="DumbProjectileData"/>, <c>MissileAIUpdate::projectileHandleCollision</c>): when an armed missile reaches its victim and that is a garrisoned building.
/// </summary>
public sealed record MissileData(bool TryToFollowTarget, long FuelLifetimeTicks, long IgnitionDelayTicks, Fixed InitialVelocity, Fixed DistanceToTravelBeforeTurning,
    Fixed LockDistance, string IgnitionFx, bool UseWeaponSpeed, bool DetonateOnNoFuel, bool DetonateCallsKill, long KillSelfDelayTicks) : ModuleData
{
    /// <summary><c>DistanceToTargetBeforeDiving</c>: this close to its goal a missile gives up its locomotor's preferred height (<c>setUsePreciseZPos</c>).</summary>
    public Fixed DiveDistance { get; init; }

    public int GarrisonHitKillCount { get; init; }
    public ImmutableArray<string> GarrisonHitKillRequiredKindOf { get; init; } = [];
    public ImmutableArray<string> GarrisonHitKillForbiddenKindOf { get; init; } = [];
    public string GarrisonHitKillFx { get; init; } = string.Empty;

    public static MissileData Parse(RatedModuleConfiguration configuration)
    {
        var velocity = ModuleConfigReader.Real(configuration, "InitialVelocity", Fixed.Zero);
        var data = new MissileData(
            ModuleConfigReader.Bool(configuration, "TryToFollowTarget", true),
            ModuleConfigReader.Ticks(configuration, "FuelLifetime", 0),
            ModuleConfigReader.Ticks(configuration, "IgnitionDelay", 0),
            velocity / configuration.Time.TicksPerSecondFixed,
            ModuleConfigReader.Real(configuration, "DistanceToTravelBeforeTurning", Fixed.Zero),
            ModuleConfigReader.Real(configuration, "DistanceToTargetForLock", Fixed.FromInt(75)),
            ModuleConfigReader.Text(configuration, "IgnitionFX"),
            ModuleConfigReader.Bool(configuration, "UseWeaponSpeed", false),
            ModuleConfigReader.Bool(configuration, "DetonateOnNoFuel", false),
            ModuleConfigReader.Bool(configuration, "DetonateCallsKill", false),
            ModuleConfigReader.Ticks(configuration, "KillSelfDelay", 3))
        {
            DiveDistance = ModuleConfigReader.Real(configuration, "DistanceToTargetBeforeDiving", Fixed.Zero),
            GarrisonHitKillCount = checked((int)ModuleConfigReader.Int(configuration, "GarrisonHitKillCount", 0)),
            GarrisonHitKillRequiredKindOf = ModuleConfigReader.Flags(configuration, "GarrisonHitKillRequiredKindOf")?.Names ?? [],
            GarrisonHitKillForbiddenKindOf = ModuleConfigReader.Flags(configuration, "GarrisonHitKillForbiddenKindOf")?.Names ?? [],
            GarrisonHitKillFx = ModuleConfigReader.Text(configuration, "GarrisonHitKillFX"),
        };
        MissileFrames.Register(data, configuration.Time, velocity);
        return data;
    }
}

/// <summary>
/// The initial velocity of a missile per logic frame of the original for the catalogs built above 30 Hz, as the 30 Hz catalog holds it. The per-tick value of a faster catalog is
/// rounded once for that rate; times the ticks per frame it misses the 30 Hz value by the last bit, and a missile that starts a raw step slower flies beside the 30 Hz missile
/// for its whole flight. Derived data keyed by the catalog's own <see cref="MissileData"/> instances, like <see cref="LocomotorFrames"/>: no field of a catalog type, no state, not
/// hashed. Data no builder registered (hand-made in a test) gets its per-tick value scaled.
/// </summary>
public static class MissileFrames
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MissileData, System.Runtime.CompilerServices.StrongBox<Fixed>> Known = new();

    /// <summary>Called by <see cref="MissileData.Parse"/> with the source number in units per second. Nothing to do at 30 Hz.</summary>
    public static void Register(MissileData data, SimulationTime timing, Fixed initialVelocityPerSecond)
    {
        if (timing.TicksPerOriginalFrame == 1) return;
        // The same expression as MissileData.Parse, with the time service of the original's rate.
        Known.AddOrUpdate(data, new System.Runtime.CompilerServices.StrongBox<Fixed>(initialVelocityPerSecond / SimulationRate.Original.Time.TicksPerSecondFixed));
    }

    /// <summary><see cref="MissileData.InitialVelocity"/> per logic frame of the original; at 30 Hz the value itself.</summary>
    public static Fixed InitialVelocity(MissileData data, SimulationTime time)
    {
        var ticks = time.TicksPerOriginalFrame;
        if (ticks == 1) return data.InitialVelocity;
        return Known.TryGetValue(data, out var known) ? known.Value : data.InitialVelocity * ticks;
    }
}

/// <summary>
/// <c>MissileAIUpdate.cpp</c> with the part of its <c>AIUpdateInterface</c>, its <c>Locomotor</c> and its <c>PhysicsBehavior</c> a missile uses (docs/SIMULATION-CORE.md §75):
/// <list type="number">
/// <item><description><b>Launch</b> (<c>projectileFireAtObjectOrPosition</c>): the missile looks along the x axis of the launch matrix, raised by <c>2 · max(0, dz) / max(1, xy distance)</c>, and
/// takes the launcher's velocity and <c>InitialVelocity</c> along that line. It coasts until <c>IgnitionDelay</c> is over.</description></item>
/// <item><description><b>Ignition</b>: the warhead arms, <c>IgnitionFX</c> is requested, fuel lasts <c>FuelLifetime</c> (0: forever).</description></item>
/// <item><description><b>Attack without turning</b> until <c>DistanceToTravelBeforeTurning</c> is flown (measured from the previous frame's position, as the reference does before moving):
/// the locomotor's turn rate is held at 0, so the thrust stays on the nose. A Tomahawk leaves its rack 70 degrees up and goes that way for 80 units.</description></item>
/// <item><description><b>Attack</b>: <c>Locomotor::moveTowardsPositionThrust</c>. The thrust points where the velocity will meet the goal, at most <c>MaxThrustAngle</c> off the nose, the nose
/// follows the velocity at the locomotor's turn rate, and the velocity relaxes to the top speed by the acceleration (<c>accelVec = thrustDir * maxAccel - curVel * damping</c>). With a
/// <c>PreferredHeight</c> the goal's height is a share (<c>PreferredHeightDamping</c>) of the way from where the missile is to that height above the surface, until it is within
/// <c>DistanceToTargetBeforeDiving</c> of its goal: a cruise missile climbs, holds its height, and comes down on the target.</description></item>
/// <item><description>Within the <b>lock distance</b> (2D; halved for a position shot, which then aims at the ground point again) it switches to <b>Kill</b>: the braking "cheat" of the reference,
/// straight at the goal at its speed, its nose turning three times as fast; arriving there detonates it.</description></item>
/// <item><description>Out of fuel: detonate (<c>DetonateOnNoFuel</c>) or coast without thrust or turning. Gravity acts all the time.
/// A followed victim that is gone makes it run out of fuel at once and remove itself without damage (<c>airborneTargetGone</c>).</description></item>
/// <item><description>After detonating it waits <c>KillSelfDelay</c> ticks, then dies (<c>DetonateCallsKill</c>) or is destroyed.</description></item>
/// </list>
/// Collision stand-ins (no collision system): an armed missile detonates when it meets the object it was fired at (<see cref="ProjectileModules.Touches"/>) - only that object - or when it
/// reaches its goal point; on the ground it detonates unless it came down more than a cell above its goal and more than three cells from it (<c>projectileHandleCollision(NULL)</c>); an
/// unarmed one slides along the ground; below height 0 it is destroyed (<c>newPos.z &lt; 0</c>). A missile marked by a plane's <c>CountermeasuresBehavior</c> gives up its victim for the
/// closest flare after the decoy delay and then does no damage. Not modeled: jamming, the frames a missile waits for its path before the locomotor acts, a damaged locomotor. All
/// vectors are <see cref="Fixed"/>; directions are normalised with the fixed-point square root; a rotation toward a direction is the sum of the two axes, not a matrix.
/// <para>
/// The state machine runs on the frame grid of the original, on the missile's own phase (docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md): velocity, top speed and acceleration are kept per
/// logic frame (and per frame²) at every tick rate; the first step is decided one frame after the launch and every further one a frame later, from the same state as at 30 Hz. The step a
/// frame decides is flown in equal shares over the ticks of that frame; an arrival or a hit on the ground ends the flight in the tick that decides it, as at 30 Hz. At 30 Hz a frame is a
/// tick and nothing differs.
/// </para>
/// </summary>
public sealed class MissileAIUpdate(MissileData data) : SimModule, IUpdateModule, IProjectileModule
{
    /// <summary><c>getDisabledTypesToProcess</c> (an AIUpdate).</summary>
    public DisabledMask DisabledTypesToProcess => DisabledMask.Held;

    private static readonly Fixed ApproachHeight = Fixed.FromInt(10);

    // isNearlyZero of Locomotor.cpp, and the speed below which PhysicsBehavior::update clamps a component to zero.
    private static readonly Fixed Nearly = Fixed.FromDecimal(0.001m);

    // MIN_VEL of locoUpdate_moveTowardsPosition: a cell per second, per frame.
    private static readonly Fixed BrakingMinimum = Fixed.FromInt(TerrainMap.CellSize) / Fixed.FromInt(SimulationRate.OriginalFramesPerSecond);

    // MAX_FRICTION of PhysicsUpdate.cpp.
    private static readonly Fixed MostFriction = Fixed.FromDecimal(0.99m);

    // calcSlowDownDist: "use a little fudge so that things can stop 'on a dime' more easily".
    private static readonly Fixed SlowDownFudge = Fixed.FromDecimal(1.05m);

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
    // The x axis of the missile's matrix, a unit vector, and PhysicsBehavior::m_vel per logic frame of the original.
    private Point3 _forward;
    private Point3 _velocity;
    private Fixed _maxSpeed;
    private Fixed _minSpeed;
    private Fixed _acceleration;
    private Fixed _braking;
    private Fixed _turnRate;
    // What the missile was fired at: it collides with that, also when it does not follow it (TryToFollowTarget = No).
    private EntityId _firedAt;
    // Locomotor::setUsePreciseZPos: the missile has given up its preferred height for good.
    private bool _preciseHeight;
    // WAS_AIRBORNE_LAST_FRAME: only coming down from the air is a hit on the ground.
    private bool _airborne;
    private long _decoyTick;
    private bool _noDamage;
    // The step of the current frame and the ticks of it already flown (0: this tick decides a step). Always 0 at 30 Hz (and then not written).
    private Point3 _frameStep;
    private int _subStep;
    // aiFollowWaypointPath: the waypoints still to fly, the one the goal stands on first. Empty for every missile no script sends along a path.
    private readonly List<(Fixed X, Fixed Y)> _route = [];

    public override string ModuleName => "MissileAIUpdate";

    /// <summary>
    /// <c>aiFollowWaypointPath</c> of a missile: it flies from waypoint to waypoint at its height and comes down on the last one, where it detonates. The original flies the waypoints
    /// with its locomotor's preferred height; a missile here flies at its goal, so a waypoint on the way keeps the height the missile has.
    /// </summary>
    public void FollowPath(IWorldServices world, ImmutableArray<(Fixed X, Fixed Y)> path)
    {
        if (path.IsDefaultOrEmpty) return;
        _route.Clear();
        _route.AddRange(path);
        _victim = EntityId.Invalid;
        _tracking = false;
        GoToWaypoint(world);
        world.MarkDirty(Entity);
    }

    private void GoToWaypoint(IWorldServices world)
    {
        var (x, y) = _route[0];
        _goal = new Point3(x, y, _route.Count > 1 ? Entity.Z : world.GroundHeightAt(x, y));
    }
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public EntityId Launcher => _launcher;
    internal MissileState State => _state;

    /// <summary><c>AIUpdateInterface::getCurrentVictim</c> of the missile: the object it follows (invalid for a position shot).</summary>
    public EntityId Victim => _victim;

    /// <summary>The missile is in its kill-self wait after detonating (<c>OBJECT_STATUS_MISSILE_KILLING_SELF</c>).</summary>
    internal bool IsKillingSelf => _state == MissileState.KillSelf;

    /// <summary>The elevation of the missile's nose, radians above the horizon: the x axis of its matrix.</summary>
    public Fixed FlightPitch => FixedTrig.Atan2(_forward.Z, Fixed.Sqrt(_forward.X * _forward.X + _forward.Y * _forward.Y));

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
        // Everything the missile flies with is per logic frame of the original (the unit of the reference and of the 30 Hz catalog), whatever the tick rate: a speed per tick times
        // the ticks per frame, an acceleration per tick² times their square. At 30 Hz the factors are 1.
        var time = world.Time;
        var perFrame = time.TicksPerOriginalFrame;
        // The initial velocity as the 30 Hz catalog holds it (200 units per second: raw 111848107 per frame at 30 Hz, twice 55924053 at 60 Hz).
        var initial = MissileFrames.InitialVelocity(data, time);
        // Speed and acceleration of the locomotor as the 30 Hz catalog holds them (LocomotorFrames): rebuilt from the per-tick values of a faster catalog the acceleration of a
        // stream projectile was two raw steps off (2.4 per frame²: raw 40265318 at 30 Hz, 40265320 from 60 Hz), and the stream flew a few raw steps beside the 30 Hz stream.
        var frame = locomotor is null || perFrame == 1 ? null : LocomotorFrames.Of(locomotor, perFrame);
        _maxSpeed = locomotor is null ? initial : frame?.Speed ?? locomotor.Speed;
        _minSpeed = locomotor is null ? Fixed.Zero : frame?.MinSpeed ?? locomotor.MinSpeed;
        _acceleration = locomotor is null ? Fixed.Zero : frame?.Acceleration ?? locomotor.Acceleration;
        _turnRate = locomotor is null ? Fixed.Zero : frame?.TurnRate ?? locomotor.TurnRatePerTick;
        // The catalog gives a locomotor without Braking its acceleration; every THRUST locomotor states it, and 0 means none (moveTowardsPositionThrust).
        _braking = locomotor is { BrakesByData: true } ? frame?.Braking ?? locomotor.Braking : Fixed.Zero;
        if (data.UseWeaponSpeed || _maxSpeed <= Fixed.Zero)
        {
            // UseWeaponSpeed: m_maxAccel = weaponSpeed, setMaxSpeed(weaponSpeed), both caps on the locomotor's own values. A missile without any speed (no locomotor, no initial
            // velocity) would hang in the air forever here; it flies at the weapon speed instead (documented stand-in).
            var weaponSpeed = ProjectileModules.FrameSpeed(time, weapon.WeaponSpeed);
            if (data.UseWeaponSpeed) initial = weaponSpeed;
            _maxSpeed = _maxSpeed > Fixed.Zero ? Fixed.Min(_maxSpeed, weaponSpeed) : weaponSpeed;
            _acceleration = _acceleration > Fixed.Zero ? Fixed.Min(_acceleration, weaponSpeed) : weaponSpeed;
        }

        var dz = launch.Z - Entity.Z;
        var dx = launch.X - Entity.X;
        var dy = launch.Y - Entity.Y;
        var xyDistance = Fixed.Max(Fixed.Sqrt(dx * dx + dy * dy), Fixed.One);
        var zFactor = dz > Fixed.Zero ? dz / xyDistance : Fixed.Zero;
        // The x axis of the matrix the launch gave the missile, raised for a victim above it (projectileFireAtObjectOrPosition).
        _forward = Normalize(new Point3(launch.ForwardX, launch.ForwardY, launch.ForwardZ + zFactor * Fixed.FromInt(2)));
        // transferVelocityTo, then the force of the launch.
        var push = initial > Fixed.Zero ? initial : Fixed.Zero;
        _velocity = new Point3(launch.VelocityX + _forward.X * push, launch.VelocityY + _forward.Y * push, launch.VelocityZ + _forward.Z * push);
        SwitchTo(world, MissileState.Launch);
        Face();

        var victim = launch.Victim.IsValid ? world.FindEntity(launch.Victim) : null;
        _firedAt = victim?.Id ?? EntityId.Invalid;
        if (victim is not null && data.TryToFollowTarget)
        {
            _tracking = true;
            _victim = victim.Id;
            _goal = ProjectileModules.FrameEnd(victim, world.Tick);
            _originalTarget = _goal;
        }
        else
        {
            _originalTarget = new Point3(launch.X, launch.Y, launch.Z);
            _goal = data.LockDistance > Fixed.Zero ? _originalTarget with { Z = launch.Z + ApproachHeight } : _originalTarget;
            _victim = EntityId.Invalid;
        }
        _previous = new Point3(Entity.X, Entity.Y, Entity.Z);
        _airborne = Entity.Z > world.GroundHeightAt(Entity.X, Entity.Y);
        _subStep = 0;
        // The first step is decided one logic frame after the launch (the next tick at 30 Hz).
        world.WakeAt(Entity, this, world.Tick + time.OriginalFrames(SimulationTiming.SleepNone));
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
        if (_subStep != 0)
        {
            FlyShare(world);
            return SimulationTiming.SleepNone;
        }
        var killing = _state == MissileState.Kill;
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

        var thrust = false;
        var turn = false;
        switch (_state)
        {
            case MissileState.Launch:
                if (world.Tick - _stateTick < data.IgnitionDelayTicks) break;
                SwitchTo(world, MissileState.Ignition);
                Ignite(world);
                thrust = true;
                break;
            case MissileState.Ignition:
                Ignite(world);
                thrust = true;
                break;
            case MissileState.AttackNoTurn:
            case MissileState.Attack:
                if (!Attack(world, _state == MissileState.Attack, out thrust, out turn)) return Finish(world);
                break;
            case MissileState.Kill:
                if (world.Tick >= _fuelExpiration)
                {
                    if (data.DetonateOnNoFuel) Detonate(world);
                    else TargetGone(world);
                    return Finish(world);
                }
                if (_tracking && FollowedVictim(world) is null) { TargetGone(world); return Finish(world); }
                thrust = true;
                turn = true;
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
        // The status OBJECT_STATUS_BRAKING is set by doKillState: the frame of the lock is flown as any other.
        Move(world, thrust, turn, killing);
        return Finish(world);
    }

    private long Finish(IWorldServices world) => _state == MissileState.Dead ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;

    /// <summary><c>MissileAIUpdate::setFramesTillCountermeasureDiversionOccurs</c>: <paramref name="frames"/> ticks from now the missile gives up its victim for a decoy flare.</summary>
    public void SetFramesTillCountermeasureDiversionOccurs(IWorldServices world, long frames) => _decoyTick = world.Tick + world.Time.AtLeastOneFrame(frames);

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
        _firedAt = flare.Id;
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
    private bool Attack(IWorldServices world, bool turnOk, out bool thrust, out bool turn)
    {
        thrust = false;
        turn = false;
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
        thrust = fueled;
        turn = fueled && turnOk;
        var dx = _goal.X - Entity.X;
        var dy = _goal.Y - Entity.Y;
        if (data.LockDistance > Fixed.Zero)
        {
            var lockDistance = _tracking ? data.LockDistance : data.LockDistance / Fixed.FromInt(2);
            if (SquaredReach.Of(lockDistance).ContainsStrictly(dx * dx + dy * dy))
            {
                if (!_tracking) _goal = _originalTarget;
                SwitchTo(world, MissileState.Kill);
                return true;
            }
        }
        // "Am I close enough to the target to ignore my preferred height setting?" Once only: the locomotor keeps the flag.
        if (!_preciseHeight && Entity.Template.Locomotor is { PreferredHeight: var preferred } && preferred > Fixed.Zero && _route.Count <= 1
            && SquaredReach.Of(data.DiveDistance).ContainsStrictly(dx * dx + dy * dy)) _preciseHeight = true;
        if (_noTurnDistanceLeft <= Fixed.Zero) SwitchTo(world, MissileState.Attack);
        return true;
    }

    private Entity? FollowedVictim(IWorldServices world)
    {
        var victim = world.FindEntity(_victim);
        if (victim is null || victim.DestroyRequested) return null;
        _goal = ProjectileModules.FrameEnd(victim, world.Tick);
        return victim;
    }

    /// <summary><c>airborneTargetGone</c>: out of fuel now, remove itself after the kill-self delay, no detonation.</summary>
    private void TargetGone(IWorldServices world)
    {
        _fuelExpiration = world.Tick;
        SwitchTo(world, MissileState.KillSelf);
    }

    /// <summary>
    /// One frame of <c>AIUpdateInterface::doLocomotor</c> and <c>PhysicsBehavior::update</c>. <paramref name="thrust"/>: the locomotor may accelerate; <paramref name="turn"/>: it may turn;
    /// <paramref name="killing"/>: <c>OBJECT_STATUS_BRAKING</c>, the missile goes straight at its goal at its speed instead of along its velocity.
    /// </summary>
    private void Move(IWorldServices world, bool thrust, bool turn, bool killing)
    {
        var position = new Point3(Entity.X, Entity.Y, Entity.Z);
        var speed = Norm(_velocity);
        if (turn && _state is MissileState.Attack or MissileState.Kill)
        {
            var toGoal = Between(position, _goal);
            var remaining = Norm(toGoal);
            if (remaining <= speed && _route.Count > 1)
            {
                // AIFollowWaypointPathState: on to the next waypoint.
                _route.RemoveAt(0);
                GoToWaypoint(world);
                remaining = Fixed.Max(Norm(Between(position, _goal)), Fixed.One);
            }
            if (remaining <= speed)
            {
                Place(world, _goal);
                // projectileHandleCollision with its victim: a garrisoned building loses riders instead of taking the explosion.
                if (_armed && (!_victim.IsValid || !ClearGarrison(world, _victim))) Detonate(world);
                return;
            }
        }

        var push = Thrust(world, position, speed, thrust, turn, killing);
        var friction = Fixed.Min(Entity.ModuleArray.OfType<PhysicsBehavior>().FirstOrDefault()?.AerodynamicFriction ?? Fixed.Zero, MostFriction);
        var gravity = ProjectileModules.GravityPerFrameSquared(world);
        _velocity = new Point3(
            Settled(_velocity.X + push.X - _velocity.X * friction),
            Settled(_velocity.Y + push.Y - _velocity.Y * friction),
            Settled(_velocity.Z + push.Z + gravity - _velocity.Z * friction));
        var next = killing ? Cheat(position, speed) : new Point3(position.X + _velocity.X, position.Y + _velocity.Y, position.Z + _velocity.Z);
        if (_armed && world.FindEntity(_firedAt) is { DestroyRequested: false } struck && ProjectileModules.Touches(struck, world.Tick, next, Entity.Template.GeometryMajorRadius))
        {
            Place(world, next);
            if (!ClearGarrison(world, _firedAt)) Detonate(world);
            return;
        }
        if (world.ProjectileHitsDeck(next.X, next.Y, position.Z, next.Z, out var deckZ))
        {
            // MissileAIUpdate::detonate's neighbour: "ensure we are slightly above the bridge" - blown up on the deck.
            next = next with { Z = deckZ };
            if (_armed)
            {
                Place(world, next);
                Detonate(world);
                return;
            }
        }
        var ground = world.GroundHeightAt(next.X, next.Y);
        if (next.Z <= ground)
        {
            // "do not allow object to pass through the ground": the z velocity loses the excess, a missile never bounces up.
            var excess = ground - next.Z;
            _velocity = _velocity with { Z = Fixed.Min(_velocity.Z + excess, Fixed.Zero) };
            next = next with { Z = ground };
            var cameDown = _airborne;
            _airborne = false;
            // projectileHandleCollision(NULL): an armed warhead detonates on the ground; an unarmed missile is inert and slides along it.
            if (cameDown && _armed && !HitTheGroundElsewhere(next))
            {
                Place(world, next);
                Detonate(world);
                return;
            }
        }
        else _airborne = true;
        Fly(world, position, next);
    }

    /// <summary>"We're above our target goal... we're somewhere else": a ground hit more than a cell above the goal and more than three cells from it is no hit.</summary>
    private bool HitTheGroundElsewhere(Point3 point)
    {
        var offset = Between(_goal, point);
        return offset.Z > Fixed.FromInt(TerrainMap.CellSize) && Norm(offset) > Fixed.FromInt(3 * TerrainMap.CellSize);
    }

    private bool ClearGarrison(IWorldServices world, EntityId victim)
        => ProjectileModules.ClearGarrison(world, Entity, _launcher, victim, data.GarrisonHitKillCount, data.GarrisonHitKillRequiredKindOf, data.GarrisonHitKillForbiddenKindOf, data.GarrisonHitKillFx);

    /// <summary><c>locoUpdate_moveTowardsPosition</c>, "Projectiles cheat in 3 dimensions": straight at the goal at the speed the missile has, at least a cell a second, never past it.</summary>
    private Point3 Cheat(Point3 position, Fixed speed)
    {
        var way = Between(position, _goal);
        var distance = Norm(way);
        if (distance <= Nearly) return position;
        var step = Fixed.Min(Fixed.Max(speed, BrakingMinimum), distance);
        return new Point3(position.X + way.X / distance * step, position.Y + way.Y / distance * step, position.Z + way.Z / distance * step);
    }

    /// <summary>
    /// <c>Locomotor::moveTowardsPositionThrust</c>: turns the nose and gives what the thrust does to the velocity this frame (the motive force of a missile of any mass, per frame²). The
    /// locomotor's <c>m_maxAccel</c> is the acceleration when <paramref name="thrust"/>, else 0, and <c>m_maxTurnRate</c> the turn rate when <paramref name="turn"/>, else 0
    /// (<c>doLaunchState</c>, <c>doIgnitionState</c>, <c>doAttackState</c>, <c>doKillState</c>).
    /// </summary>
    private Point3 Thrust(IWorldServices world, Point3 position, Fixed speed, bool thrust, bool turn, bool killing)
    {
        var locomotor = Entity.Template.Locomotor;
        var acceleration = thrust ? _acceleration : Fixed.Zero;
        var turnRate = turn ? _turnRate : Fixed.Zero;
        var forwardSpeed = ForwardSpeed();
        var desired = _maxSpeed;
        if (_braking > Fixed.Zero)
        {
            var (dx, dy) = (_goal.X - position.X, _goal.Y - position.Y);
            if (Fixed.Sqrt(dx * dx + dy * dy) < SlowDownDistance(forwardSpeed, _minSpeed, _braking)) desired = _minSpeed;
        }

        // "If we have a preferred flight height, and we haven't been told explicitly to ignore it"
        var goal = _goal;
        if (locomotor is { PreferredHeight: var preferred } && preferred != Fixed.Zero && !_preciseHeight)
            goal = goal with { Z = position.Z + (preferred + world.SurfaceHeightAt(position.X, position.Y) - position.Z) * locomotor.PreferredHeightDamping };

        var speedDelta = desired - forwardSpeed;
        var push = speedDelta > Fixed.Zero || _braking == Fixed.Zero ? acceleration : -_braking;
        var wanted = DirectionToApplyThrust(position, goal, push, ProjectileModules.GravityPerFrameSquared(world));
        // A missile without a locomotor is the stand-in of Launch: it thrusts where it wants to go.
        var thrustLimit = turn || locomotor is null ? locomotor?.MaxThrustAngle ?? FixedTrig.Pi : Fixed.Zero;
        var thrustDirection = RotateToward(_forward, wanted, thrustLimit, out var thrustTurned);
        if (speed.Abs() >= Nearly)
        {
            // "note that we are trying to orient in the direction of our vel, not the dir of our thrust." Braking: "align to target, cause that's where we're going anyway."
            var look = killing ? Between(position, _goal) : _velocity;
            if (!killing || Dot(look, look).Abs() >= Nearly) _forward = RotateToward(_forward, look, killing ? turnRate * 3 : turnRate, out _);
        }

        if (speedDelta == Fixed.Zero && !thrustTurned) return default;
        var top = _maxSpeed > Fixed.Zero ? _maxSpeed : Fixed.FromDecimal(0.01m);
        var damping = Fixed.Min(Fixed.Max(push / top, Fixed.Zero), Fixed.One);
        return new Point3(thrustDirection.X * push - _velocity.X * damping, thrustDirection.Y * push - _velocity.Y * damping, thrustDirection.Z * push - _velocity.Z * damping);
    }

    /// <summary><c>calcSlowDownDist</c>.</summary>
    private static Fixed SlowDownDistance(Fixed speed, Fixed goal, Fixed braking)
    {
        var delta = speed - goal;
        return delta <= Fixed.Zero ? Fixed.Zero : Fixed.MultiplyDivide(delta, delta, braking.Abs()) / Fixed.FromInt(2) * SlowDownFudge;
    }

    /// <summary><c>PhysicsBehavior::getForwardSpeed3D</c>, which is what it is: the root of the summed squares of the products of velocity and nose, signed by their sum.</summary>
    private Fixed ForwardSpeed()
    {
        var (x, y, z) = (_velocity.X * _forward.X, _velocity.Y * _forward.Y, _velocity.Z * _forward.Z);
        var speed = Fixed.Sqrt(x * x + y * y + z * z);
        return (x + y + z).IsNegative ? -speed : speed;
    }

    /// <summary><c>calcDirectionToApplyThrust</c>: the direction that brings the velocity (with this frame's gravity) closest to the way to the goal; straight at the goal without a useful solution.</summary>
    private Point3 DirectionToApplyThrust(Point3 position, Point3 goal, Fixed push, Fixed gravity)
    {
        var toGoal = Between(position, goal);
        var lengthSquared = Dot(toGoal, toGoal);
        if (lengthSquared.Abs() < Nearly) return _forward;
        var velocity = _velocity with { Z = _velocity.Z + gravity };
        var distance = Fixed.Sqrt(lengthSquared);
        var speed = Norm(velocity);
        var denominator = (speed - push) * (speed + push);
        if (denominator.Abs() >= Nearly)
        {
            var time = Fixed.MultiplyDivide(distance, speed + push, denominator);
            var other = Fixed.MultiplyDivide(distance, speed - push, denominator);
            if (time >= Fixed.Zero || other >= Fixed.Zero)
            {
                // "choose the smallest positive t."
                if (time < Fixed.Zero || (other >= Fixed.Zero && other < time)) time = other;
                if (time.Abs() >= Nearly)
                {
                    var wanted = new Point3(toGoal.X / time - velocity.X, toGoal.Y / time - velocity.Y, toGoal.Z / time - velocity.Z);
                    if (Norm(wanted) > Fixed.Zero) return Normalize(wanted);
                }
            }
        }
        return Normalize(toGoal);
    }

    /// <summary>
    /// <c>tryToRotateVector3D</c>: <paramref name="current"/> turned toward <paramref name="goal"/> by at most <paramref name="limit"/> radians; <paramref name="turned"/> says the two differ.
    /// A limit near zero leaves it as it is. Two opposite directions have no plane to turn in, and stay.
    /// </summary>
    private static Point3 RotateToward(Point3 current, Point3 goal, Fixed limit, out bool turned)
    {
        turned = false;
        if (limit.Abs() < Nearly || Norm(goal) <= Fixed.Zero) return current;
        var from = Normalize(current);
        var to = Normalize(goal);
        var cosine = Fixed.Max(-Fixed.One, Fixed.Min(Dot(from, to), Fixed.One));
        turned = cosine < Fixed.One;
        if (limit >= FixedTrig.Pi || cosine >= FixedTrig.Cos(limit)) return to;
        var across = new Point3(to.X - from.X * cosine, to.Y - from.Y * cosine, to.Z - from.Z * cosine);
        var length = Norm(across);
        if (length <= Fixed.Zero) return current;
        var (cos, sin) = (FixedTrig.Cos(limit), FixedTrig.Sin(limit));
        return Normalize(new Point3(from.X * cos + across.X / length * sin, from.Y * cos + across.Y / length * sin, from.Z * cos + across.Z / length * sin));
    }

    /// <summary>"when vel gets tiny, just clamp to zero"</summary>
    private static Fixed Settled(Fixed value) => value.Abs() < Nearly ? Fixed.Zero : value;

    private static Point3 Between(Point3 from, Point3 to) => new(to.X - from.X, to.Y - from.Y, to.Z - from.Z);

    private static Fixed Dot(Point3 a, Point3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>The object's heading follows its matrix; straight up or down it keeps the last one.</summary>
    private void Face()
    {
        if (_forward.X != Fixed.Zero || _forward.Y != Fixed.Zero) Entity.Angle = FixedTrig.Atan2(_forward.Y, _forward.X);
    }

    /// <summary>The step of this frame: taken at once at 30 Hz, otherwise flown in equal shares over the ticks of the frame, with the heading of the frame.</summary>
    private void Fly(IWorldServices world, Point3 from, Point3 to)
    {
        if (world.Time.TicksPerOriginalFrame == 1)
        {
            Place(world, to);
            return;
        }
        Face();
        _frameStep = new Point3(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        // The rest of the frame stays readable on the object (Entity.FrameEndX …): who looks at the missile in this tick sees it where this frame ends, as at 30 Hz.
        Entity.SetAhead(world.Tick, _frameStep.X, _frameStep.Y, _frameStep.Z);
        FlyShare(world);
    }

    private void FlyShare(IWorldServices world)
    {
        var time = world.Time;
        // A frame completed from outside (Entity.CompleteFrame) has nothing left to cover.
        if (Entity.IsInsideFrameStep)
            Entity.CoverShare(world.Tick, time.FrameShare(_frameStep.X, _subStep), time.FrameShare(_frameStep.Y, _subStep), time.FrameShare(_frameStep.Z, _subStep));
        _subStep = time.NextSubStep(_subStep);
        world.MarkDirty(Entity);
    }

    private void Place(IWorldServices world, Point3 point)
    {
        Face();
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
        if (_route.Count > 0)
        {
            writer.BeginArray("route");
            foreach (var (x, y) in _route)
            {
                writer.BeginObject(null);
                writer.Fixed("x", x);
                writer.Fixed("y", y);
                writer.EndObject();
            }
            writer.EndArray();
        }
        DumbProjectileBehavior.WritePoint(writer, "original", _originalTarget);
        DumbProjectileBehavior.WritePoint(writer, "forward", _forward);
        DumbProjectileBehavior.WritePoint(writer, "velocity", _velocity);
        writer.Fixed("maxSpeed", _maxSpeed);
        writer.Fixed("minSpeed", _minSpeed);
        writer.Fixed("acceleration", _acceleration);
        writer.Fixed("braking", _braking);
        writer.Fixed("turnRate", _turnRate);
        writer.UInt("firedAt", _firedAt.Value);
        writer.Bool("airborne", _airborne);
        if (_preciseHeight) writer.Bool("preciseHeight", true);
        if (_decoyTick != 0) writer.Int("decoyTick", _decoyTick);
        if (_noDamage) writer.Bool("noDamage", true);
        if (_subStep != 0)
        {
            writer.Int("subStep", _subStep);
            DumbProjectileBehavior.WritePoint(writer, "frameStep", _frameStep);
        }
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
        _route.Clear();
        if (reader.Has("route")) foreach (var item in reader.Array("route")) _route.Add((item.Fixed("x"), item.Fixed("y")));
        _originalTarget = DumbProjectileBehavior.ReadPoint(reader, "original");
        _forward = DumbProjectileBehavior.ReadPoint(reader, "forward");
        _velocity = DumbProjectileBehavior.ReadPoint(reader, "velocity");
        _maxSpeed = reader.Fixed("maxSpeed");
        _minSpeed = reader.Fixed("minSpeed");
        _acceleration = reader.Fixed("acceleration");
        _braking = reader.Fixed("braking");
        _turnRate = reader.Fixed("turnRate");
        _firedAt = new EntityId(checked((uint)reader.UInt("firedAt")));
        _airborne = reader.Bool("airborne");
        _preciseHeight = reader.Has("preciseHeight") && reader.Bool("preciseHeight");
        _decoyTick = reader.Has("decoyTick") ? reader.Int("decoyTick") : 0;
        _noDamage = reader.Has("noDamage") && reader.Bool("noDamage");
        _subStep = reader.Has("subStep") ? checked((int)reader.Int("subStep")) : 0;
        _frameStep = _subStep != 0 ? DumbProjectileBehavior.ReadPoint(reader, "frameStep") : default;
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
    /// <summary><c>m_nextScanFrames = GameLogicRandomValue( 0, 3 )</c> (PointDefenseLaserUpdate.cpp, <c>fireWhenReady</c>): the upper bound of the wait before the next scan, in logic frames of the original.</summary>
    private const int RescanMaxFrames = 3;

    private long _nextScan;
    private long _nextShot;
    /// <summary>Above 30 Hz only: the frame's deciding tick was a scan without a target, the shot delay rests for the frame.</summary>
    private bool _quietFrame;

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
        // The laser looks and fires in the ticks its owner's logic frames of the original begin with, and reads the owner and every target where the frame each has begun
        // ends (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md 1.3, rule 3): a look in every tick sees a projectile a share of a frame's step earlier than the 30 Hz step and shoots it
        // down a frame early. The counters fall every tick and lie on the frame grid. 30 Hz: every tick, the positions.
        var decide = world.Time.BeginsFrameOf(Entity, world.Tick);
        if (_nextScan > 0)
        {
            _nextScan--;
            if (decide) _quietFrame = false;
            if (!_quietFrame) FireWhenReady(world, weapon, decide);
            return SimulationTiming.SleepNone;
        }
        if (!decide)
        {
            if (_nextShot > 0 && !_quietFrame) _nextShot--;
            return SimulationTiming.SleepNone;
        }
        _nextScan = world.Time.CountdownReload(data.ScanTicks);
        // A scan that finds nothing does not count the shot delay down in its update (fireWhenReady is not reached): at 30 Hz that is a whole logic frame of the original
        // without a count, so above 30 Hz the remaining ticks of this frame do not count either. Never set at 30 Hz.
        if (ScanClosestTarget(world, weapon))
        {
            _quietFrame = false;
            FireWhenReady(world, weapon, true);
        }
        else _quietFrame = world.Time.TicksPerOriginalFrame > TickOrder.Next;
        return SimulationTiming.SleepNone;
    }

    private void FireWhenReady(IWorldServices world, WeaponInfo weapon, bool decide)
    {
        if (!decide)
        {
            if (_nextShot > 0) _nextShot--;
            return;
        }
        _frameTick = world.Tick;
        var target = _target.IsValid ? world.FindEntity(_target) : null;
        if (target is { DestroyRequested: true }) target = null;
        if (target is not null)
        {
            if (Distance2D(target) < weapon.AttackRange) _inRange = true;
            else if (_inRange)
            {
                var wait = world.LogicRandom.Range(0, RescanMaxFrames);
                _nextScan = world.Time.CountdownReload(world.Time.OriginalFrames(wait));
                _target = EntityId.Invalid;
                if (wait == 0)
                {
                    ScanClosestTarget(world, weapon);
                    _nextScan = world.Time.CountdownReload(data.ScanTicks);
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
            _nextShot = world.Time.CountdownReload(weapon.DelayBetweenShotsTicks);
        }
        if (target.EffectivelyDead)
        {
            var wait = world.LogicRandom.Range(0, RescanMaxFrames);
            _nextScan = world.Time.CountdownReload(world.Time.OriginalFrames(wait));
            _target = EntityId.Invalid;
            if (wait == 0)
            {
                ScanClosestTarget(world, weapon);
                _nextScan = world.Time.CountdownReload(data.ScanTicks);
            }
        }
    }

    // The tick of the look that is running: where "the frame an object has begun" is read. Not state.
    private long _frameTick;

    private Fixed Distance2D(Entity other)
    {
        var (otherX, otherY, _) = other.FrameEnd(_frameTick);
        var (ownX, ownY, _) = Entity.FrameEnd(_frameTick);
        var dx = otherX - ownX;
        var dy = otherY - ownY;
        return Fixed.Sqrt(dx * dx + dy * dy);
    }

    /// <summary><c>scanClosestTarget</c>: primary in range, secondary in range, primary out of range, secondary out of range.</summary>
    private bool ScanClosestTarget(IWorldServices world, WeaponInfo weapon)
    {
        _frameTick = world.Tick;
        // The candidates are the entities of the listed kinds (the world's kind lists) instead of every entity of the world. Each pick below is the minimum
        // of (distance, id) over its candidates - Closer breaks ties by id itself -, and an out-of-range pick is only used when its list has no in-range
        // candidate at all, in which case it was never skipped. So the result does not depend on the order the candidates come in, and a candidate that
        // has two of the listed kinds and comes twice changes nothing the second time.
        var picks = default(ScanPicks);
        var scanSquared = SquaredReach.Of(data.ScanRange);
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

    private void ScanCandidates(IWorldServices world, WeaponInfo weapon, EntityList candidates, SquaredReach scanSquared, ref ScanPicks picks)
    {
        foreach (var other in candidates.AsSpan())
        {
            if (other.DestroyRequested || other == Entity || other.ContainedBy.IsValid) continue;
            var targetClass = TargetClassOf(other.Template);
            if (targetClass == ClassNone) continue;
            var (otherX, otherY, _) = other.FrameEnd(_frameTick);
            var (ownX, ownY, _) = Entity.FrameEnd(_frameTick);
            var dx = otherX - ownX;
            var dy = otherY - ownY;
            if (!scanSquared.Contains(dx * dx + dy * dy)) continue;
            if (!world.IsSignificantlyAboveTerrainAtFrameEnd(other) && !weapon.AntiGround) continue;
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
        if (_quietFrame) writer.Bool("quietFrame", true);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _target = new EntityId(checked((uint)reader.UInt("target")));
        _inRange = reader.Bool("inRange");
        _nextScan = reader.Int("nextScan");
        _nextShot = reader.Int("nextShot");
        _quietFrame = reader.Has("quietFrame") && reader.Bool("quietFrame");
    }
}
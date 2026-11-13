using CnCNew.Data.Definitions;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// Phase 10 footprint collision: the two reference effects that were deferred for lack of collision detection
/// (<c>docs/SIMULATION-CORE.md</c> §20, "<c>ToppleUpdate</c>/<c>CrushDie</c> with real collision") plus a bounded local
/// avoidance step for ground path-following (not in the reference, see <see cref="AvoidLocalBlockers"/>). Movement itself
/// stays in <c>AIUpdateInterface</c> (<c>Modules/BuiltIn/Phase5Modules.cs</c>); that module calls back into these two
/// methods right after it changes a mover's position, which is the only place ground positions change outside explicit
/// teleports (dock arrival, container exit).
/// </summary>
public sealed partial class SimulationWorld
{
    // Damage.cpp DamageTypeFlags::s_bitNameList: "CRUSH" is bit 1. DamageTypes (SimulationWorld/Damage.cs) does not carry
    // it as a named constant because, until this file, nothing in the simulation produced crush damage.
    private static readonly int CrushDamageType = NameTables.DamageType.TryIndexOf("CRUSH", out var crush)
        ? crush : throw new InvalidOperationException("CRUSH is not a damage type.");

    // The victim-side crush test in SquishCollide.cpp forces the victim's geometry radius to 1.0 ("so the tank has to
    // actually hit the infantry") regardless of its real bounding circle.
    private static readonly Fixed SquishVictimRadius = Fixed.One;

    // Rotation offsets AvoidLocalBlockers tries in order: right first (arbitrary but deterministic), then left, each
    // pair wider than the last. Four attempts bound the search; a fifth, unmodified attempt is the caller's fallback.
    private static readonly Fixed[] AvoidanceAngles =
    [
        -FixedTrig.HalfPi / Fixed.FromInt(2), FixedTrig.HalfPi / Fixed.FromInt(2),
        -FixedTrig.HalfPi, FixedTrig.HalfPi,
    ];

    /// <inheritdoc cref="IWorldServices.AvoidLocalBlockers"/>
    public (Fixed Dx, Fixed Dy) AvoidLocalBlockers(Entity mover, Fixed dx, Fixed dy)
    {
        if (Navigation is null || (dx == Fixed.Zero && dy == Fixed.Zero)) return (dx, dy);
        if (Navigation.Probe is not { } probe) return AvoidLocalBlockersCore(mover, dx, dy);
        probe.Started(CnCNew.Simulation.Terrain.MovementWork.LocalAvoidance);
        var result = AvoidLocalBlockersCore(mover, dx, dy);
        probe.Ended(CnCNew.Simulation.Terrain.MovementWork.LocalAvoidance);
        return result;
    }

    private (Fixed Dx, Fixed Dy) AvoidLocalBlockersCore(Entity mover, Fixed dx, Fixed dy)
    {
        // The object an approach order aims at is never an obstacle (the reference's AI ignores its goal object when it steers), or a hijacker could never touch its car.
        var ai = AiOf(mover);
        // A rider that has just come out of its transport walks through whoever stands at the door (OpenContain::exitObjectViaDoor, setIgnoreCollisionTime).
        if (ai is not null && ai.IgnoresCollisions(_tick)) return (dx, dy);
        var goal = ai?.IgnoredObstacle ?? EntityId.Invalid;
        if (!StepBlocked(mover, dx, dy, goal)) return (dx, dy);
        foreach (var angle in AvoidanceAngles)
        {
            var cos = FixedTrig.Cos(angle);
            var sin = FixedTrig.Sin(angle);
            var rx = dx * cos - dy * sin;
            var ry = dx * sin + dy * cos;
            if (!StepBlocked(mover, rx, ry, goal)) return (rx, ry);
        }
        return (dx, dy);
    }

    /// <summary>Clearance around a standing unit that a detour keeps (about half a path cell), so the path cells do not clip its circle.</summary>
    private static readonly Fixed StandingBlockerMargin = Fixed.FromInt(5);

    /// <inheritdoc cref="IWorldServices.StationaryBlockersAhead"/>
    public IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)> StationaryBlockersAhead(Entity mover, Fixed dx, Fixed dy)
    {
        if (Navigation is null || (dx == Fixed.Zero && dy == Fixed.Zero)) return [];
        if (Navigation.Probe is not { } probe) return StationaryBlockersAheadCore(mover, dx, dy);
        probe.Started(CnCNew.Simulation.Terrain.MovementWork.StationaryBlockers);
        var result = StationaryBlockersAheadCore(mover, dx, dy);
        probe.Ended(CnCNew.Simulation.Terrain.MovementWork.StationaryBlockers);
        return result;
    }

    // The bounding circle of a template (ObjectTemplate.BoundingCircleRadius: a string comparison and, for a box, a square root), computed once per template of this world and looked
    // up by the template's index. Every circle test of a movement step reads it for both objects of a pair. The table belongs to the world and not to the template, because the
    // catalogs are hashed field by field into the input manifest; a template is immutable, so the value cannot go stale.
    private Fixed[] _circleRadius = new Fixed[256];
    private bool[] _circleKnown = new bool[256];

    private Fixed CircleRadius(Entity entity)
    {
        var index = entity.Template.Index;
        var known = _circleKnown;
        return (uint)index < (uint)known.Length && known[index] ? _circleRadius[index] : RememberCircleRadius(entity.Template);
    }

    private Fixed RememberCircleRadius(Templates.ObjectTemplate template)
    {
        var index = template.Index;
        if (index < 0) return template.BoundingCircleRadius;
        if (index >= _circleKnown.Length)
        {
            var size = Math.Max(index + 1, _circleKnown.Length * 2);
            Array.Resize(ref _circleRadius, size);
            Array.Resize(ref _circleKnown, size);
        }
        var radius = template.BoundingCircleRadius;
        _circleRadius[index] = radius;
        _circleKnown[index] = true;
        return radius;
    }

    // The answer of StationaryBlockersAhead, reused from call to call: the caller plans its detour at once and keeps nothing.
    private readonly List<(Fixed X, Fixed Y, Fixed Radius)> _stationaryCircles = [];
    private readonly Fixed[] _sampleX = new Fixed[4];
    private readonly Fixed[] _sampleY = new Fixed[4];

    // Before the exact circle test (rounded fixed-point squares) a pair is dropped when one coordinate alone is further apart than the limit plus one unit. The unit of slack is far more
    // than the rounding of a product (half a raw step), so a pair dropped here fails the exact test as well, whether that one compares with "<" or "<=". A negative limit skips the shortcut.
    private static readonly long ScanSlack = Fixed.One.Raw;

    private static bool FurtherThan(Fixed a, Fixed b, Fixed limit)
    {
        var difference = a.Raw - b.Raw;
        if (difference < 0) difference = -difference;
        return limit.Raw >= 0 && difference > limit.Raw + ScanSlack;
    }

    /// <summary>
    /// A unit that stands in the way: one of <see cref="_mobiles"/> (so it has a locomotor) that is alive, not carried, not the mover or its goal, and neither moving nor ordered to.
    /// An object that is gone this frame (<c>destroyObject</c>: a pilot that has climbed into its vehicle, a crate that was collected) stands in nobody's way: at 30 Hz the earlier
    /// object's update destroys it before a later mover looks, above 30 Hz the look in the frame's first tick comes before the step that ends in it (#304).
    /// </summary>
    // The widest bounding circle among the mobile objects this world has seen (as a magnitude, it only grows). With it a scan can drop an object by its position alone, before it
    // reads anything else of it: an object further away on one axis than the mover's radius plus this radius plus the slack fails the exact test with its own, smaller radius too.
    private long _widestMobile;

    private static long Magnitude(Fixed value) => value.Raw < 0 ? -value.Raw : value.Raw;

    /// <summary>One object on a deck and the other under it, or on another deck: they pass each other (the original's collisions are three-dimensional, the port's circles are not).</summary>
    private static bool OnAnotherLevel(Entity a, Entity b) => a.Layer != b.Layer && (a.Z - b.Z).Abs() > CnCNew.Simulation.Terrain.TerrainBridge.LayerCloseEnough;

    private static bool StandsStill(Entity other, Entity mover, EntityId goal)
        => !(other.HasMoveTarget || other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.DestroyRequested || other.ContainedBy.IsValid || other.Speed != Fixed.Zero || PassesThrough(mover, other));

    /// <summary>
    /// <c>INFANTRY_MOVES_THROUGH_INFANTRY</c> of <c>Pathfinder::checkForMovement</c>: "Infantry can run through infantry", so standing infantry is nothing an infantry's way goes round.
    /// </summary>
    private static bool PassesThrough(Entity mover, Entity other) => mover.Template.IsKindOf("INFANTRY") && other.Template.IsKindOf("INFANTRY");

    private IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)> StationaryBlockersAheadCore(Entity mover, Fixed dx, Fixed dy)
    {
        if (_firstFrameTicks != TickOrder.Next) return StationaryBlockersAheadAtFrameEnd(mover, dx, dy);
        var goal = AiOf(mover)?.IgnoredObstacle ?? EntityId.Invalid;
        var moverRadius = CircleRadius(mover);
        // The step is the look-ahead; the line from the mover to its end is tested at four points. They and their bounding box depend on the mover only.
        Fixed minX = Fixed.MaxValue, maxX = Fixed.MinValue, minY = Fixed.MaxValue, maxY = Fixed.MinValue;
        for (var sample = 1; sample <= 4; sample++)
        {
            var x = mover.X + dx * Fixed.FromInt(sample) / Fixed.FromInt(4);
            var y = mover.Y + dy * Fixed.FromInt(sample) / Fixed.FromInt(4);
            _sampleX[sample - 1] = x;
            _sampleY[sample - 1] = y;
            minX = Fixed.Min(minX, x);
            maxX = Fixed.Max(maxX, x);
            minY = Fixed.Min(minY, y);
            maxY = Fixed.Max(maxY, y);
        }

        var blocked = false;
        var mobiles = _mobiles;
        var reach = Magnitude(moverRadius) + _widestMobile + Magnitude(StandingBlockerMargin) + ScanSlack;
        long nearX = minX.Raw - reach, farX = maxX.Raw + reach, nearY = minY.Raw - reach, farY = maxY.Raw + reach;
        for (var index = 0; index < mobiles.Count; index++)
        {
            var other = mobiles[index];
            if (other.X.Raw < nearX || other.X.Raw > farX || other.Y.Raw < nearY || other.Y.Raw > farY) continue;
            if (!StandsStill(other, mover, goal) || OnAnotherLevel(mover, other)) continue;
            var limit = moverRadius + CircleRadius(other) + StandingBlockerMargin;
            if (limit.Raw >= 0 && (other.X.Raw < minX.Raw - limit.Raw - ScanSlack || other.X.Raw > maxX.Raw + limit.Raw + ScanSlack || other.Y.Raw < minY.Raw - limit.Raw - ScanSlack || other.Y.Raw > maxY.Raw + limit.Raw + ScanSlack)) continue;
            for (var sample = 0; sample < 4 && !blocked; sample++)
            {
                var ddx = other.X - _sampleX[sample];
                var ddy = other.Y - _sampleY[sample];
                if (SquaredReach.Of(limit).ContainsStrictly(ddx * ddx + ddy * ddy)) blocked = true;
            }

            if (blocked) break;
        }

        if (!blocked) return [];
        var circles = _stationaryCircles;
        circles.Clear();
        var around = Fixed.FromInt(150);
        var aroundSquared = SquaredReach.Of(around);
        for (var index = 0; index < mobiles.Count; index++)
        {
            var other = mobiles[index];
            if (!StandsStill(other, mover, goal) || FurtherThan(other.X, mover.X, around) || FurtherThan(other.Y, mover.Y, around) || OnAnotherLevel(mover, other)) continue;
            var ddx = other.X - mover.X;
            var ddy = other.Y - mover.Y;
            if (!aroundSquared.Contains(ddx * ddx + ddy * ddy)) continue;
            circles.Add((other.X, other.Y, CircleRadius(other) + moverRadius + StandingBlockerMargin));
        }

        return circles;
    }

    /// <summary>Whether stepping <paramref name="mover"/> to its position plus (<paramref name="dx"/>, <paramref name="dy"/>) would land inside another live, mobile entity's bounding circle (the <paramref name="goal"/> object excepted).</summary>
    private bool StepBlocked(Entity mover, Fixed dx, Fixed dy, EntityId goal)
    {
        if (_firstFrameTicks != TickOrder.Next) return StepBlockedAtFrameEnd(mover, dx, dy, goal);
        var x = mover.X + dx;
        var y = mover.Y + dy;
        var moverRadius = CircleRadius(mover);
        var mobiles = _mobiles;
        var reach = Magnitude(moverRadius) + _widestMobile + ScanSlack;
        long nearX = x.Raw - reach, farX = x.Raw + reach, nearY = y.Raw - reach, farY = y.Raw + reach;
        for (var index = 0; index < mobiles.Count; index++)
        {
            var other = mobiles[index];
            if (other.X.Raw < nearX || other.X.Raw > farX || other.Y.Raw < nearY || other.Y.Raw > farY) continue;
            if (other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.ContainedBy.IsValid || OnAnotherLevel(mover, other)) continue;
            var limit = moverRadius + CircleRadius(other);
            if (FurtherThan(other.X, x, limit) || FurtherThan(other.Y, y, limit)) continue;
            var ddx = other.X - x;
            var ddy = other.Y - y;
            if (SquaredReach.Of(limit).ContainsStrictly(ddx * ddx + ddy * ddy) && !IsNoObstacleOnTheMove(mover, other)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------------------------------------ a unit on the move in the way

    private static readonly Fixed SharedDirection = Fixed.FromDecimal(0.25m);
    private static readonly Fixed SamePlaceSquared = Fixed.FromDecimal(0.01m);
    private static readonly Fixed HeadOnLimit = FixedTrig.HalfPi / Fixed.FromInt(2);
    private static readonly Fixed GoalIgnoresBlockers = Fixed.FromInt(CnCNew.Simulation.Terrain.TerrainMap.CellSize);

    /// <summary>The other unit is on its way, seen where the frame it has begun ends (at 30 Hz: not on its last step when it had its turn in this tick).</summary>
    private bool IsOnTheMove(Entity other)
        => other.HasMoveTarget && !(_firstFrameTicks != TickOrder.Next && other.AheadTick + TickOrder.Next >= _tick && AiOf(other) is { IsArrivingWithThisFrame: true });

    /// <summary>
    /// A unit on the move is no obstacle the step goes round: <c>AIUpdateInterface::processCollision</c> lets the two overlap, and the mover takes the speed of one it follows
    /// (<see cref="StepBlockedMovers"/>). Only one that comes towards the mover stays in the way: the original answers that with <c>aiMoveAwayFromUnit</c>, here the step goes round it.
    /// </summary>
    public bool IsNoObstacleOnTheMove(Entity mover, Entity other)
    {
        if (!IsOnTheMove(other)) return false;
        var (x, y, _) = other.FrameEnd(_tick);
        var angle = other.FrameEndAngle(_tick);
        return !BlockedBy(mover, mover.X, mover.Y, mover.Angle, other, x, y, angle, 0) || IsMovingAwayFrom(mover.X, mover.Y, x, y, angle);
    }

    /// <summary>The first check of <c>calculateMaxBlockedSpeed</c>, the other way round: not "running into us".</summary>
    private static bool IsMovingAwayFrom(Fixed moverX, Fixed moverY, Fixed otherX, Fixed otherY, Fixed otherAngle)
        => (otherX - moverX) * FixedTrig.Cos(otherAngle) + (otherY - moverY) * FixedTrig.Sin(otherAngle) >= Fixed.Zero;

    /// <summary><c>Object::canCrushOrSquish</c> as far as a moving unit needs it: "just run over them".</summary>
    private bool CanRunOver(Entity mover, Entity other)
        => mover.Template.CrusherLevel > other.Template.CrushableLevel && RelationshipOf(mover.Owner, other.Owner) != Relationship.Allies;

    /// <summary><c>AIUpdateInterface::hasHigherPathPriority</c>: dozers before vehicles before infantry, then the unit in front, then the lower id.</summary>
    private static bool HasHigherPathPriority(Entity mover, Fixed x, Fixed y, Fixed angle, Entity other, Fixed otherX, Fixed otherY, Fixed otherAngle)
    {
        var (dozer, hisDozer) = (mover.Template.IsKindOf("DOZER"), other.Template.IsKindOf("DOZER"));
        if (dozer != hisDozer) return dozer;
        if (mover.Template.IsKindOf("VEHICLE") && other.Template.IsKindOf("INFANTRY")) return true;
        if (mover.Template.IsKindOf("INFANTRY") && other.Template.IsKindOf("VEHICLE")) return false;
        var (ourX, ourY) = (FixedTrig.Cos(angle), FixedTrig.Sin(angle));
        var (hisX, hisY) = (FixedTrig.Cos(otherAngle), FixedTrig.Sin(otherAngle));
        if (ourX * hisX + ourY * hisY <= Fixed.Zero) return mover.Id.Value < other.Id.Value;
        var ahead = (ourX + hisX) * (otherX - x) + (ourY + hisY) * (otherY - y);
        return ahead != Fixed.Zero ? ahead < Fixed.Zero : mover.Id.Value < other.Id.Value;
    }

    /// <summary><c>AIUpdateInterface::blockedBy</c> for another unit on the move: only what lies ahead of the mover within 45 degrees, and on its own way too, holds it up.</summary>
    private bool BlockedBy(Entity mover, Fixed x, Fixed y, Fixed angle, Entity other, Fixed otherX, Fixed otherY, Fixed otherAngle, long framesBlocked)
    {
        if ((mover.MoveTargetX - x).Abs() < GoalIgnoresBlockers && (mover.MoveTargetY - y).Abs() < GoalIgnoresBlockers) return false;
        if (CanRunOver(mover, other)) return false;
        if (other.Template.Locomotor is { } locomotor && (locomotor.Surfaces & CnCNew.Simulation.Templates.LocomotorSurfaces.Air) != 0) return false;
        // "don't collide": a unit that reverses is held up by nobody, whatever lies in front of it (blockedBy, after the check of the other's ground movement).
        if (AiOf(mover) is { IsMovingBackwards: true }) return false;
        var (ourX, ourY) = (FixedTrig.Cos(angle), FixedTrig.Sin(angle));
        var (hisX, hisY) = (FixedTrig.Cos(otherAngle), FixedTrig.Sin(otherAngle));
        var sameWay = ourX * hisX + ourY * hisY;
        if (mover.Template.IsKindOf("INFANTRY") && other.Template.IsKindOf("INFANTRY") && sameWay <= SharedDirection) return false;
        var (dx, dy) = (x - otherX, y - otherY);
        var distance = dx * dx + dy * dy;
        if (distance < SamePlaceSquared) return HasHigherPathPriority(mover, x, y, angle, other, otherX, otherY, otherAngle);
        if (framesBlocked > SimulationRate.OriginalFramesPerSecond && sameWay <= Fixed.Zero) return false;
        var collision = FixedTrig.AngleDifference(angle, FixedTrig.Atan2(-dy, -dx)).Abs();
        if (collision > FixedTrig.HalfPi) return false;
        if (collision <= HeadOnLimit) return true;
        if (sameWay <= Fixed.Zero) return false;
        if (FixedTrig.AngleDifference(otherAngle, FixedTrig.Atan2(dy, dx)).Abs() <= HeadOnLimit) return false;
        // "See if we're running into each other."
        var (nextX, nextY) = (dx + ourX - hisX, dy + ourY - hisY);
        return distance > nextX * nextX + nextY * nextY && !HasHigherPathPriority(mover, x, y, angle, other, otherX, otherY, otherAngle);
    }

    /// <summary>
    /// <c>PartitionManager::update</c> and <c>AIUpdateInterface::processCollision</c>: once per logic frame of the original, when every object has moved, each unit on the move learns whether a unit on
    /// its own way holds it up (the ground plans overlap, the other lies ahead and moves on) and how fast it may then drive; its next frame's decision reads that. In one pass, at the frame's end, so
    /// no unit sees another at a moment of its own, at any rate.
    /// </summary>
    /// <remarks>After the delayed damage of the tick: a unit killed in a frame is no obstacle at the end of that frame at any rate.</remarks>
    private void StepBlockedMovers(long tick)
    {
        if (!Time.IsOriginalFrameBoundary(tick + TickOrder.Next)) return;
        var stride = Time.TicksPerOriginalFrame;
        var mobiles = _mobiles;
        for (var index = 0; index < mobiles.Count; index++)
        {
            var mover = mobiles[index];
            if (AiOf(mover) is not { } ai) continue;
            // Under way: an order given in the last tick of a frame above 30 Hz is the next frame's, in which a unit that turns on the spot has no speed yet either.
            var moving = mover.HasMoveTarget && !mover.EffectivelyDead && !mover.ContainedBy.IsValid && mover.Template.Locomotor is { } locomotor && (locomotor.Surfaces & CnCNew.Simulation.Templates.LocomotorSurfaces.Air) == 0
                && ai.SpeedPerOriginalFrame(stride) > Fixed.Zero;
            var blocked = false;
            var limit = Fixed.MaxValue;
            if (moving && !ai.IgnoresCollisions(tick)) blocked = HeldBehindAMover(mover, ai, stride, out limit);
            ai.SetBlocked(this, blocked, limit);
        }
    }

    private bool HeldBehindAMover(Entity mover, AIUpdateInterface ai, int stride, out Fixed limit)
    {
        limit = Fixed.MaxValue;
        var blocked = false;
        var goal = ai.IgnoredObstacle;
        var (x, y, _) = mover.FrameEnd(_tick);
        var angle = mover.FrameEndAngle(_tick);
        // Per pair and from the templates: a box reaches beyond its circle, and _widestMobile remembers units long dead, which a restored world does not.
        var extent = GroundPlanExtent(mover.Template);
        var ours = new SiteShape(mover.Template, x, y, angle);
        var mobiles = _mobiles;
        for (var index = 0; index < mobiles.Count; index++)
        {
            var other = mobiles[index];
            var (otherX, otherY, _) = other.FrameEnd(_tick);
            var reach = extent + GroundPlanExtent(other.Template) + ScanSlack;
            if (Magnitude(otherX - x) > reach || Magnitude(otherY - y) > reach) continue;
            if (other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.ContainedBy.IsValid || !other.HasMoveTarget || OnAnotherLevel(mover, other)) continue;
            var otherAngle = other.FrameEndAngle(_tick);
            if (!SiteShape.Collide(ours, new SiteShape(other.Template, otherX, otherY, otherAngle)) || !BlockedBy(mover, x, y, angle, other, otherX, otherY, otherAngle, ai.BlockedFrames)) continue;
            // Running into us: the step goes round it (IsNoObstacleOnTheMove).
            if (!IsMovingAwayFrom(x, y, otherX, otherY, otherAngle)) continue;
            // "let them get their path"
            if (AiOf(other) is not { WaitingForPath: false } otherAi) continue;
            var speed = otherAi.SpeedPerOriginalFrame(stride);
            if (speed == Fixed.Zero) continue;
            blocked = true;
            limit = Fixed.Min(limit, MaxBlockedSpeed(x, y, angle, otherX, otherY, otherAngle, speed));
        }
        return blocked;
    }

    private static long GroundPlanExtent(Templates.ObjectTemplate template)
        => Magnitude(template.GeometryMajorRadius) + (template.IsBox ? Magnitude(template.GeometryMinorRadius) : 0);

    /// <summary><c>AIUpdateInterface::calculateMaxBlockedSpeed</c>: as fast as the other moves away from the mover, in the direction the mover drives towards it.</summary>
    private static Fixed MaxBlockedSpeed(Fixed x, Fixed y, Fixed angle, Fixed otherX, Fixed otherY, Fixed otherAngle, Fixed otherSpeed)
    {
        var (toX, toY) = (otherX - x, otherY - y);
        var length = Fixed.Sqrt(toX * toX + toY * toY);
        if (length == Fixed.Zero) return Fixed.Zero;
        (toX, toY) = (toX / length, toY / length);
        var away = (toX * FixedTrig.Cos(otherAngle) + toY * FixedTrig.Sin(otherAngle)) * otherSpeed;
        var towards = toX * FixedTrig.Cos(angle) + toY * FixedTrig.Sin(angle);
        return towards <= Fixed.Zero ? Fixed.MaxValue : Fixed.Max(Fixed.Zero, away) / towards;
    }

    /// <inheritdoc cref="IWorldServices.MoversInTheWay"/>
    public IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)> MoversInTheWay(Entity mover)
    {
        var circles = _stationaryCircles;
        circles.Clear();
        var moverRadius = CircleRadius(mover);
        var around = Fixed.FromInt(150);
        var mobiles = _mobiles;
        for (var index = 0; index < mobiles.Count; index++)
        {
            var other = mobiles[index];
            if (other.Id == mover.Id || other.EffectivelyDead || other.ContainedBy.IsValid || OnAnotherLevel(mover, other)) continue;
            if (other.Template.Locomotor is { } locomotor && (locomotor.Surfaces & CnCNew.Simulation.Templates.LocomotorSurfaces.Air) != 0) continue;
            var (otherX, otherY, _) = other.FrameEnd(_tick);
            if (FurtherThan(otherX, mover.X, around) || FurtherThan(otherY, mover.Y, around)) continue;
            circles.Add((otherX, otherY, CircleRadius(other) + moverRadius + StandingBlockerMargin));
        }
        return circles;
    }

    // ------------------------------------------------------------------------------------------------ above 30 Hz
    //
    // A mover decides the step of a logic frame of the original in the first tick of its frame (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3, rule 3). What the 30 Hz step sees of
    // another mover in that tick is the other after its whole step if it ran earlier in the tick, and before it otherwise: the end of the frame the other has begun
    // (Entity.FrameEnd), and "standing" if the step it is covering is the last of its move. The two scans below are the scans above with exactly that view; they never run at
    // 30 Hz, where the rest of a frame is always zero and the scans above are untouched.

    private bool StandsStillAtFrameEnd(Entity other, Entity mover, EntityId goal)
    {
        if (other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.DestroyRequested || other.ContainedBy.IsValid || PassesThrough(mover, other)) return false;
        if (!other.HasMoveTarget) return other.Speed == Fixed.Zero;
        // Covering the last step of its move: the 30 Hz step has already put it on its target, where it stands.
        return other.AheadTick + TickOrder.Next >= _tick && AiOf(other) is { IsArrivingWithThisFrame: true };
    }

    private IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)> StationaryBlockersAheadAtFrameEnd(Entity mover, Fixed dx, Fixed dy)
    {
        var goal = AiOf(mover)?.IgnoredObstacle ?? EntityId.Invalid;
        var moverRadius = CircleRadius(mover);
        Fixed minX = Fixed.MaxValue, maxX = Fixed.MinValue, minY = Fixed.MaxValue, maxY = Fixed.MinValue;
        for (var sample = 1; sample <= 4; sample++)
        {
            var x = mover.X + dx * Fixed.FromInt(sample) / Fixed.FromInt(4);
            var y = mover.Y + dy * Fixed.FromInt(sample) / Fixed.FromInt(4);
            _sampleX[sample - 1] = x;
            _sampleY[sample - 1] = y;
            minX = Fixed.Min(minX, x);
            maxX = Fixed.Max(maxX, x);
            minY = Fixed.Min(minY, y);
            maxY = Fixed.Max(maxY, y);
        }

        var blocked = false;
        var mobiles = _mobiles;
        var tick = _tick;
        for (var index = 0; index < mobiles.Count && !blocked; index++)
        {
            var other = mobiles[index];
            if (!StandsStillAtFrameEnd(other, mover, goal) || OnAnotherLevel(mover, other)) continue;
            var (otherX, otherY, _) = other.FrameEnd(tick);
            var limit = moverRadius + CircleRadius(other) + StandingBlockerMargin;
            if (limit.Raw >= 0 && (otherX.Raw < minX.Raw - limit.Raw - ScanSlack || otherX.Raw > maxX.Raw + limit.Raw + ScanSlack || otherY.Raw < minY.Raw - limit.Raw - ScanSlack || otherY.Raw > maxY.Raw + limit.Raw + ScanSlack)) continue;
            for (var sample = 0; sample < 4 && !blocked; sample++)
            {
                var ddx = otherX - _sampleX[sample];
                var ddy = otherY - _sampleY[sample];
                if (SquaredReach.Of(limit).ContainsStrictly(ddx * ddx + ddy * ddy)) blocked = true;
            }
        }

        if (!blocked) return [];
        var circles = _stationaryCircles;
        circles.Clear();
        var around = Fixed.FromInt(150);
        var aroundSquared = SquaredReach.Of(around);
        for (var index = 0; index < mobiles.Count; index++)
        {
            var other = mobiles[index];
            if (!StandsStillAtFrameEnd(other, mover, goal)) continue;
            var (otherX, otherY, _) = other.FrameEnd(tick);
            if (FurtherThan(otherX, mover.X, around) || FurtherThan(otherY, mover.Y, around)) continue;
            var ddx = otherX - mover.X;
            var ddy = otherY - mover.Y;
            if (!aroundSquared.Contains(ddx * ddx + ddy * ddy)) continue;
            circles.Add((otherX, otherY, CircleRadius(other) + moverRadius + StandingBlockerMargin));
        }

        return circles;
    }

    private bool StepBlockedAtFrameEnd(Entity mover, Fixed dx, Fixed dy, EntityId goal)
    {
        var x = mover.X + dx;
        var y = mover.Y + dy;
        var moverRadius = CircleRadius(mover);
        var mobiles = _mobiles;
        var tick = _tick;
        for (var index = 0; index < mobiles.Count; index++)
        {
            var other = mobiles[index];
            if (other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.ContainedBy.IsValid || OnAnotherLevel(mover, other)) continue;
            var (otherX, otherY, _) = other.FrameEnd(tick);
            var limit = moverRadius + CircleRadius(other);
            if (FurtherThan(otherX, x, limit) || FurtherThan(otherY, y, limit)) continue;
            var ddx = otherX - x;
            var ddy = otherY - y;
            if (SquaredReach.Of(limit).ContainsStrictly(ddx * ddx + ddy * ddy) && !IsNoObstacleOnTheMove(mover, other)) return true;
        }
        return false;
    }

    /// <inheritdoc cref="IWorldServices.ResolveFootprintCollisions"/>
    public void ResolveFootprintCollisions(Entity mover, Fixed stepX, Fixed stepY)
    {
        if (Navigation?.Probe is not { } probe)
        {
            ResolveFootprintCollisionsCore(mover, stepX, stepY);
            return;
        }
        probe.Started(CnCNew.Simulation.Terrain.MovementWork.FootprintCollisions);
        ResolveFootprintCollisionsCore(mover, stepX, stepY);
        probe.Ended(CnCNew.Simulation.Terrain.MovementWork.FootprintCollisions);
    }

    private void ResolveFootprintCollisionsCore(Entity mover, Fixed stepX, Fixed stepY)
    {
        if (mover.EffectivelyDead) return;
        if (mover.Template.CrusherLevel > 0)
        {
            // Only an object with a ToppleUpdate or a SquishCollide module or a crushable level can be toppled, squished or crushed; for every other object the calls below did nothing.
            var targets = _crushTargets;
            for (var index = 0; index < targets.Count; index++)
            {
                var other = targets[index];
                if (other.Id == mover.Id || other.EffectivelyDead) continue;
                if (!FootprintsOverlap(mover, other)) continue;
                TryTopple(mover, other);
                TrySquish(mover, other, stepX, stepY);
                TryCrush(mover, other, stepX, stepY);
            }
        }
        DeliverCollisions(mover);
    }

    // ------------------------------------------------------------------------------------------------ collide modules

    /// <summary>Every registered entity that has at least one <see cref="ICollideModule"/> (crates, hijackers, saboteurs, ...), in registration order.</summary>
    private readonly List<Entity> _collidables = [];

    /// <summary>
    /// Every registered entity with a locomotor, in ascending id order: the only objects the avoidance and standing-blocker scans of a movement step look at. An entity's template
    /// never changes, so membership is decided once, when the entity is registered.
    /// </summary>
    private readonly List<Entity> _mobiles = [];

    /// <summary>Every registered entity that has a <see cref="ToppleUpdate"/> or a <see cref="SquishCollide"/> module or a crushable level, in ascending id order: what a crusher can topple, squish or crush.</summary>
    private readonly List<Entity> _crushTargets = [];

    // Kept in ascending id order, so a live world and one restored from a snapshot (which registers in id order) visit the entities of a list in the same order: the order of the entity table.
    private static void InsertById(List<Entity> list, Entity entity)
    {
        var index = list.Count;
        while (index > 0 && list[index - 1].Id.Value > entity.Id.Value) index--;
        list.Insert(index, entity);
    }

    private static bool IsCrushTarget(Entity entity)
    {
        if (entity.Template.CrushableLevel < NotCrushable) return true;
        foreach (var module in entity.ModuleArray)
            if (module is ToppleUpdate or SquishCollide) return true;
        return false;
    }

    /// <summary>Called when an entity enters the entity table (the name is older than the other two lists).</summary>
    private void TrackCollidable(Entity entity)
    {
        entity.CollideModules = [..entity.ModuleArray.OfType<ICollideModule>()];
        if (entity.Template.Locomotor is not null)
        {
            InsertById(_mobiles, entity);
            _widestMobile = Math.Max(_widestMobile, Magnitude(CircleRadius(entity)));
        }
        if (IsCrushTarget(entity)) InsertById(_crushTargets, entity);
        if (entity.CollideModules.Length == 0) return;
        InsertById(_collidables, entity);
    }

    /// <summary>Called when an entity leaves the entity table.</summary>
    private void UntrackCollidable(Entity entity)
    {
        if (entity.Template.Locomotor is not null) _mobiles.Remove(entity);
        if (IsCrushTarget(entity)) _crushTargets.Remove(entity);
        if (entity.CollideModules.Length > 0) _collidables.Remove(entity);
    }

    /// <summary>
    /// <c>PartitionManager::checkForCollisions</c> (<c>obj->onCollide(other)</c>, then <c>other->onCollide(obj)</c>) for a mover whose footprint now overlaps another
    /// object's: the mover's own collide modules hear about every overlapping object, and every collide-module holder the mover overlaps hears about the mover.
    /// Only movers are tested, so an object standing still on a crate collects it as soon as it moves, not when the crate appears.
    /// </summary>
    private void DeliverCollisions(Entity mover)
    {
        var touched = false;
        if (mover.CollideModules.Length > 0)
            foreach (var other in _entities)
            {
                if (other.Id == mover.Id || !CanCollide(other) || !FootprintsOverlap(mover, other)) continue;
                touched = true;
                CallCollide(mover, other);
            }
        for (var index = 0; index < _collidables.Count; index++)
        {
            var holder = _collidables[index];
            if (holder.Id == mover.Id || !CanCollide(holder) || !CanCollide(mover) || !FootprintsOverlap(mover, holder)) continue;
            touched = true;
            CallCollide(holder, mover);
        }
        // PartitionContactList::processContactList: "re-dirtying collision": what touched something is looked at again in the next frame, whether it moves or not.
        if (touched && !mover.Template.IsKindOf("IMMOBILE")) _contacts.Add(mover.Id.Value);
    }

    // The objects whose last look found something they touch and that are looked at again, by ascending id; none in a world where nothing touches.
    private readonly SortedSet<uint> _contacts = [];
    private readonly List<uint> _contactScratch = [];

    /// <summary><c>PartitionManager::update</c> for what stands: a mover looks at its contacts with its step (<see cref="ResolveFootprintCollisions"/>), a standing object here, once per frame.</summary>
    private void StepContacts(long tick)
    {
        if (_contacts.Count == 0) return;
        _contactScratch.Clear();
        _contactScratch.AddRange(_contacts);
        foreach (var id in _contactScratch)
        {
            if (!_entities.TryGetValue(id, out var entity) || entity.DestroyRequested || entity.EffectivelyDead || entity.ContainedBy.IsValid)
            {
                _contacts.Remove(id);
                continue;
            }
            if (!IsFirstTickOfFrame(entity, tick) || entity.HasMoveTarget) continue;
            _contacts.Remove(id);
            DeliverCollisions(entity);
        }
    }

    /// <summary>
    /// <c>PartitionData::allocCoiArray</c> makes a new object dirty: its first partition update looks at what it stands on, unless it is immobile. Projectiles collide by their own flight here.
    /// </summary>
    private void CollideWhenCreated(Entity entity)
    {
        if (!entity.Registered || !CanCollide(entity) || entity.Template.IsKindOf("IMMOBILE") || entity.Template.IsKindOf("PROJECTILE")) return;
        DeliverCollisions(entity);
    }

    /// <inheritdoc cref="IWorldServices.CollideWhereItStands"/>
    public void CollideWhereItStands(Entity entity)
    {
        if (entity.Registered && CanCollide(entity)) DeliverCollisions(entity);
    }

    private static bool CanCollide(Entity entity) => !entity.DestroyRequested && !entity.ContainedBy.IsValid && !entity.HasStatus(NoCollisionsBit);

    /// <summary><c>Object::onCollide</c>: the entity's collide modules in module order; the loop ends the moment the object stops colliding (a module may set <c>NO_COLLISIONS</c> or destroy it).</summary>
    internal void CallCollide(Entity self, Entity other)
    {
        foreach (var module in self.CollideModules)
        {
            if (self.DestroyRequested || other.DestroyRequested || self.HasStatus(NoCollisionsBit)) break;
            module.OnCollide(this, other);
        }
    }

    private bool FootprintsOverlap(Entity a, Entity b)
    {
        if (OnAnotherLevel(a, b)) return false;
        if (_firstFrameTicks != TickOrder.Next) return FootprintsOverlapAtFrameEnd(a, b);
        var limit = CircleRadius(a) + CircleRadius(b);
        if (FurtherThan(a.X, b.X, limit) || FurtherThan(a.Y, b.Y, limit)) return false;
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return SquaredReach.Of(limit).Contains(dx * dx + dy * dy);
    }

    /// <summary>
    /// Above 30 Hz: the overlap test of a mover's collision check, which runs in the first tick of the mover's frame with the mover standing where that frame ends
    /// (<c>AIUpdateInterface.CollideWhereTheFrameEnds</c>). The other object is read where the frame it has begun ends: one that has had its turn in this tick after its
    /// whole step, one that has not after its previous one - what the 30 Hz step reads (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3, rule 3).
    /// </summary>
    private bool FootprintsOverlapAtFrameEnd(Entity a, Entity b)
    {
        var limit = CircleRadius(a) + CircleRadius(b);
        var (ax, ay, _) = a.FrameEnd(_tick);
        var (bx, by, _) = b.FrameEnd(_tick);
        if (FurtherThan(ax, bx, limit) || FurtherThan(ay, by, limit)) return false;
        var dx = ax - bx;
        var dy = ay - by;
        return SquaredReach.Of(limit).Contains(dx * dx + dy * dy);
    }

    /// <summary><c>ToppleUpdate.cpp onCollide</c>: a crusher above level 1 running into a toppable, upright object pushes it over, away from itself.</summary>
    private void TryTopple(Entity mover, Entity other)
    {
        if (mover.Template.CrusherLevel <= 1) return;
        // The first ToppleUpdate that can still be toppled.
        foreach (var module in other.ModuleArray)
        {
            if (module is not ToppleUpdate { IsAbleToBeToppled: true } topple) continue;
            // The force is the pusher's speed per logic frame of the original (Entity.Speed is per tick: half the push at 60 Hz).
            var stride = Time.TicksPerOriginalFrame;
            var speed = stride == 1 ? mover.Speed : AiOf(mover) is { } ai ? ai.SpeedPerOriginalFrame(stride) : mover.Speed * stride;
            // Above 30 Hz the test runs with the mover where its frame ends; the other object is read where its own begun frame ends (FootprintsOverlapAtFrameEnd).
            var (otherX, otherY, _) = other.FrameEnd(_tick);
            topple.ApplyTopplingForce(this, otherX - mover.X, otherY - mover.Y, speed);
            return;
        }
    }

    /// <summary><c>ThingTemplate::m_crushableLevel</c> of an object that does not say.</summary>
    private const int NotCrushable = 255;

    /// <summary><c>isVerySmall3D</c>: "unless I am moving right now, I may not crush anything".</summary>
    private static readonly Fixed CrushingStep = Fixed.FromDecimal(0.01m);

    /// <summary><c>PERP_RANGE</c> of <c>perpsLogicallyEqual</c>.</summary>
    private static readonly Fixed CrushPerpRange = Fixed.FromDecimal(0.15m);

    /// <summary>
    /// <c>PhysicsBehavior::checkForOverlapCollision</c>: a mover with a crusher level above the victim's crushable level kills it when it has passed the victim's crush point nearest its line of
    /// travel, of the front, the middle and the back, half the major radius apart, and no further from it than one and a half of that. The victim needs a physics module or to be immobile
    /// (<c>onCollide</c>), and an unmanned crusher crushes nothing. Not done: the first contact's damage of 0, which only triggers the minor <c>DamageFX</c>; a victim that survives no crush, so its
    /// crushed ends never lead the next crush. The ends a crushed car shows are the presentation's.
    /// </summary>
    private void TryCrush(Entity mover, Entity other, Fixed stepX, Fixed stepY)
    {
        if (mover.Template.CrusherLevel <= other.Template.CrushableLevel || (stepX.Abs() < CrushingStep && stepY.Abs() < CrushingStep)) return;
        if (RelationshipOf(mover.Owner, other.Owner) == Relationship.Allies || (mover.DisabledFlags(_tick) & DisabledMask.Unmanned) != 0) return;
        if (other.ContainedBy.IsValid || mover.ContainedBy == other.Id || IsAirborneTargetAtFrameEnd(other)) return;
        if (!other.Template.IsKindOf("IMMOBILE") && Array.Find(other.ModuleArray, module => module is PhysicsBehavior) is null) return;
        var (otherX, otherY, _) = other.FrameEnd(_tick);
        var offset = other.Template.GeometryMajorRadius / Fixed.FromInt(2);
        var angle = other.FrameEndAngle(_tick);
        var (pointX, pointY) = (FixedTrig.Cos(angle) * offset, FixedTrig.Sin(angle) * offset);
        var (dirX, dirY) = (FixedTrig.Cos(mover.Angle), FixedTrig.Sin(mover.Angle));
        CrushPoint Measure(Fixed x, Fixed y)
        {
            var (toX, toY) = (x - mover.X, y - mover.Y);
            var ray = toX * dirX + toY * dirY;
            var (perpX, perpY) = (ray * dirX - toX, ray * dirY - toY);
            return new CrushPoint(Fixed.Sqrt(perpX * perpX + perpY * perpY), Fixed.Sqrt(toX * toX + toY * toY), ray);
        }
        var point = NearestCrushPoint(Measure(otherX + pointX, otherY + pointY), Measure(otherX, otherY), Measure(otherX - pointX, otherY - pointY));
        // "(1.5x)^2 means if we are outside the crushed front point, we will not auto get the back point."
        if (!point.Ray.IsNegative || point.Length >= offset + offset / Fixed.FromInt(2)) return;
        Damage(other.Id, new DamageInfo(mover.Id, HugeDamage, CrushDamageType, DeathTypes.Crushed, false));
    }

    /// <summary>A crush point seen from the crusher: how far off its line of travel, how far away, and how far along its heading.</summary>
    private readonly record struct CrushPoint(Fixed Perp, Fixed Length, Fixed Ray);

    /// <summary>"Now find the shortest. Use the straightline distance to crush point as tie breaker."</summary>
    private static CrushPoint NearestCrushPoint(CrushPoint front, CrushPoint centre, CrushPoint back)
    {
        static bool Equal(CrushPoint one, CrushPoint two) => (one.Perp - two.Perp).Abs() <= CrushPerpRange;
        static CrushPoint Closer(CrushPoint one, CrushPoint two) => one.Length < two.Length ? one : two;
        if (front.Perp <= centre.Perp && front.Perp <= back.Perp)
            return Equal(front, centre) ? Closer(front, centre) : Equal(front, back) ? Closer(front, back) : front;
        if (back.Perp <= centre.Perp && back.Perp <= front.Perp)
            return Equal(back, centre) ? Closer(back, centre) : Equal(back, front) ? Closer(back, front) : back;
        return Equal(centre, front) ? Closer(centre, front) : Equal(centre, back) ? Closer(centre, back) : centre;
    }

    /// <summary>
    /// <c>SquishCollide.cpp onCollide</c>: a crusher the victim does not consider an ally, overlapping the victim's 1.0-radius
    /// crush point and still closing on it, crushes the victim outright. "Closing on it" uses the mover's actual displacement
    /// this tick (<paramref name="stepX"/>/<paramref name="stepY"/>, exactly what was just added to its position) rather than
    /// a velocity rebuilt from <c>Angle</c>/<c>Speed</c> through <c>Sin</c>/<c>Cos</c>: close to a victim lying exactly on the
    /// heading, the rebuilt vector's fixed-point rounding can disagree in sign with the true step, which the real displacement
    /// cannot.
    /// </summary>
    private void TrySquish(Entity mover, Entity other, Fixed stepX, Fixed stepY)
    {
        var squishable = false;
        foreach (var module in other.ModuleArray)
            if (module is SquishCollide) { squishable = true; break; }
        if (!squishable) return;
        if (RelationshipOf(other.Owner, mover.Owner) == Relationship.Allies) return;
        var limit = CircleRadius(mover) + SquishVictimRadius;
        // Above 30 Hz the victim is read where its own begun frame ends, like every object of this test (FootprintsOverlapAtFrameEnd); 30 Hz: its position.
        var (otherX, otherY, _) = other.FrameEnd(_tick);
        var dx = otherX - mover.X;
        var dy = otherY - mover.Y;
        if (!SquaredReach.Of(limit).Contains(dx * dx + dy * dy)) return;
        // The position just before this tick's step: a victim this close is "approached" unless the mover was moving away
        // from it even a moment ago.
        var beforeDx = otherX - (mover.X - stepX);
        var beforeDy = otherY - (mover.Y - stepY);
        if (dx * dx + dy * dy >= beforeDx * beforeDx + beforeDy * beforeDy) return;
        Damage(other.Id, new DamageInfo(mover.Id, HugeDamage, CrushDamageType, DeathTypes.Crushed, false));
    }
}

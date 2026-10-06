using CnCNew.Data.Definitions;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;

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
        var goal = AiOf(mover)?.GoalObject ?? EntityId.Invalid;
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

    /// <summary>A unit that stands in the way: one of <see cref="_mobiles"/> (so it has a locomotor) that is alive, not carried, not the mover or its goal, and neither moving nor ordered to.</summary>
    // The widest bounding circle among the mobile objects this world has seen (as a magnitude, it only grows). With it a scan can drop an object by its position alone, before it
    // reads anything else of it: an object further away on one axis than the mover's radius plus this radius plus the slack fails the exact test with its own, smaller radius too.
    private long _widestMobile;

    private static long Magnitude(Fixed value) => value.Raw < 0 ? -value.Raw : value.Raw;

    private static bool StandsStill(Entity other, Entity mover, EntityId goal)
        => !(other.HasMoveTarget || other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.ContainedBy.IsValid || other.Speed != Fixed.Zero);

    private IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)> StationaryBlockersAheadCore(Entity mover, Fixed dx, Fixed dy)
    {
        var goal = AiOf(mover)?.GoalObject ?? EntityId.Invalid;
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
            if (!StandsStill(other, mover, goal)) continue;
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
            if (!StandsStill(other, mover, goal) || FurtherThan(other.X, mover.X, around) || FurtherThan(other.Y, mover.Y, around)) continue;
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
            if (other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.ContainedBy.IsValid) continue;
            var limit = moverRadius + CircleRadius(other);
            if (FurtherThan(other.X, x, limit) || FurtherThan(other.Y, y, limit)) continue;
            var ddx = other.X - x;
            var ddy = other.Y - y;
            if (SquaredReach.Of(limit).ContainsStrictly(ddx * ddx + ddy * ddy)) return true;
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
            // Only an object with a ToppleUpdate or a SquishCollide module can be toppled or squished; for every other object the two calls below did nothing.
            var targets = _crushTargets;
            for (var index = 0; index < targets.Count; index++)
            {
                var other = targets[index];
                if (other.Id == mover.Id || other.EffectivelyDead) continue;
                if (!FootprintsOverlap(mover, other)) continue;
                TryTopple(mover, other);
                TrySquish(mover, other, stepX, stepY);
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

    /// <summary>Every registered entity that has a <see cref="ToppleUpdate"/> or a <see cref="SquishCollide"/> module, in ascending id order: what a crusher can topple or squish.</summary>
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
        if (mover.CollideModules.Length > 0)
            foreach (var other in _entities)
            {
                if (other.Id == mover.Id || !CanCollide(other) || !FootprintsOverlap(mover, other)) continue;
                CallCollide(mover, other);
            }
        for (var index = 0; index < _collidables.Count; index++)
        {
            var holder = _collidables[index];
            if (holder.Id == mover.Id || !CanCollide(holder) || !CanCollide(mover) || !FootprintsOverlap(mover, holder)) continue;
            CallCollide(holder, mover);
        }
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
        var limit = CircleRadius(a) + CircleRadius(b);
        if (FurtherThan(a.X, b.X, limit) || FurtherThan(a.Y, b.Y, limit)) return false;
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
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
            topple.ApplyTopplingForce(this, other.X - mover.X, other.Y - mover.Y, mover.Speed);
            return;
        }
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
        var dx = other.X - mover.X;
        var dy = other.Y - mover.Y;
        if (!SquaredReach.Of(limit).Contains(dx * dx + dy * dy)) return;
        // The position just before this tick's step: a victim this close is "approached" unless the mover was moving away
        // from it even a moment ago.
        var beforeDx = other.X - (mover.X - stepX);
        var beforeDy = other.Y - (mover.Y - stepY);
        if (dx * dx + dy * dy >= beforeDx * beforeDx + beforeDy * beforeDy) return;
        Damage(other.Id, new DamageInfo(mover.Id, HugeDamage, CrushDamageType, DeathTypes.Crushed, false));
    }
}

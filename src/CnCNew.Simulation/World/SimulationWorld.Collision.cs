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
        var goal = AiOf(mover)?.GoalObject ?? EntityId.Invalid;
        var moverRadius = mover.Template.BoundingCircleRadius;
        var blocked = false;
        foreach (var other in Entities)
        {
            if (other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.Template.Locomotor is null || other.ContainedBy.IsValid || other.HasMoveTarget || other.Speed != Fixed.Zero) continue;
            var limit = moverRadius + other.Template.BoundingCircleRadius + StandingBlockerMargin;
            // The step is the look-ahead; the line from the mover to its end is tested at four points.
            for (var sample = 1; sample <= 4 && !blocked; sample++)
            {
                var ddx = other.X - (mover.X + dx * Fixed.FromInt(sample) / Fixed.FromInt(4));
                var ddy = other.Y - (mover.Y + dy * Fixed.FromInt(sample) / Fixed.FromInt(4));
                if (ddx * ddx + ddy * ddy < limit * limit) blocked = true;
            }

            if (blocked) break;
        }

        if (!blocked) return [];
        var circles = new List<(Fixed X, Fixed Y, Fixed Radius)>();
        var around = Fixed.FromInt(150);
        foreach (var other in Entities)
        {
            if (other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.Template.Locomotor is null || other.ContainedBy.IsValid || other.HasMoveTarget || other.Speed != Fixed.Zero) continue;
            var ddx = other.X - mover.X;
            var ddy = other.Y - mover.Y;
            if (ddx * ddx + ddy * ddy > around * around) continue;
            circles.Add((other.X, other.Y, other.Template.BoundingCircleRadius + moverRadius + StandingBlockerMargin));
        }

        return circles;
    }

    /// <summary>Whether stepping <paramref name="mover"/> to its position plus (<paramref name="dx"/>, <paramref name="dy"/>) would land inside another live, mobile entity's bounding circle (the <paramref name="goal"/> object excepted).</summary>
    private bool StepBlocked(Entity mover, Fixed dx, Fixed dy, EntityId goal)
    {
        var x = mover.X + dx;
        var y = mover.Y + dy;
        var moverRadius = mover.Template.BoundingCircleRadius;
        foreach (var other in Entities)
        {
            if (other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.Template.Locomotor is null || other.ContainedBy.IsValid) continue;
            var limit = moverRadius + other.Template.BoundingCircleRadius;
            var ddx = other.X - x;
            var ddy = other.Y - y;
            if (ddx * ddx + ddy * ddy < limit * limit) return true;
        }
        return false;
    }

    /// <inheritdoc cref="IWorldServices.ResolveFootprintCollisions"/>
    public void ResolveFootprintCollisions(Entity mover, Fixed stepX, Fixed stepY)
    {
        if (mover.EffectivelyDead) return;
        if (mover.Template.CrusherLevel > 0)
            foreach (var other in Entities)
            {
                if (other.Id == mover.Id || other.EffectivelyDead) continue;
                if (!FootprintsOverlap(mover, other)) continue;
                TryTopple(mover, other);
                TrySquish(mover, other, stepX, stepY);
            }
        DeliverCollisions(mover);
    }

    // ------------------------------------------------------------------------------------------------ collide modules

    /// <summary>Every registered entity that has at least one <see cref="ICollideModule"/> (crates, hijackers, saboteurs, ...), in registration order.</summary>
    private readonly List<Entity> _collidables = [];

    private void TrackCollidable(Entity entity)
    {
        entity.CollideModules = [..entity.ModuleArray.OfType<ICollideModule>()];
        if (entity.CollideModules.Length == 0) return;
        // Kept in ascending id order, so a live world and one restored from a snapshot (which registers in id order) visit overlapping holders in the same order.
        var index = _collidables.Count;
        while (index > 0 && _collidables[index - 1].Id.Value > entity.Id.Value) index--;
        _collidables.Insert(index, entity);
    }

    private void UntrackCollidable(Entity entity)
    {
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
            foreach (var other in Entities)
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

    private static bool FootprintsOverlap(Entity a, Entity b)
    {
        var limit = a.Template.BoundingCircleRadius + b.Template.BoundingCircleRadius;
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy <= limit * limit;
    }

    /// <summary><c>ToppleUpdate.cpp onCollide</c>: a crusher above level 1 running into a toppable, upright object pushes it over, away from itself.</summary>
    private void TryTopple(Entity mover, Entity other)
    {
        if (mover.Template.CrusherLevel <= 1) return;
        var topple = other.Modules.OfType<ToppleUpdate>().FirstOrDefault(module => module.IsAbleToBeToppled);
        if (topple is null) return;
        topple.ApplyTopplingForce(this, other.X - mover.X, other.Y - mover.Y, mover.Speed);
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
        if (other.Modules.OfType<SquishCollide>().FirstOrDefault() is null) return;
        if (RelationshipOf(other.Owner, mover.Owner) == Relationship.Allies) return;
        var limit = mover.Template.BoundingCircleRadius + SquishVictimRadius;
        var dx = other.X - mover.X;
        var dy = other.Y - mover.Y;
        if (dx * dx + dy * dy > limit * limit) return;
        // The position just before this tick's step: a victim this close is "approached" unless the mover was moving away
        // from it even a moment ago.
        var beforeDx = other.X - (mover.X - stepX);
        var beforeDy = other.Y - (mover.Y - stepY);
        if (dx * dx + dy * dy >= beforeDx * beforeDx + beforeDy * beforeDy) return;
        Damage(other.Id, new DamageInfo(mover.Id, HugeDamage, CrushDamageType, DeathTypes.Crushed, false));
    }
}

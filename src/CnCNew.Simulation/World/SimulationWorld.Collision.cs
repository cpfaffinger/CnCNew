using CnCNew.Data.Definitions;
using CnCNew.Simulation.Ids;
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
        if (!StepBlocked(mover, dx, dy)) return (dx, dy);
        foreach (var angle in AvoidanceAngles)
        {
            var cos = FixedTrig.Cos(angle);
            var sin = FixedTrig.Sin(angle);
            var rx = dx * cos - dy * sin;
            var ry = dx * sin + dy * cos;
            if (!StepBlocked(mover, rx, ry)) return (rx, ry);
        }
        return (dx, dy);
    }

    /// <summary>Whether stepping <paramref name="mover"/> to its position plus (<paramref name="dx"/>, <paramref name="dy"/>) would land inside another live, mobile entity's bounding circle.</summary>
    private bool StepBlocked(Entity mover, Fixed dx, Fixed dy)
    {
        var x = mover.X + dx;
        var y = mover.Y + dy;
        var moverRadius = mover.Template.BoundingCircleRadius;
        foreach (var other in Entities)
        {
            if (other.Id == mover.Id || other.EffectivelyDead || other.Template.Locomotor is null || other.ContainedBy.IsValid) continue;
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
        if (mover.EffectivelyDead || mover.Template.CrusherLevel <= 0) return;
        foreach (var other in Entities)
        {
            if (other.Id == mover.Id || other.EffectivelyDead) continue;
            if (!FootprintsOverlap(mover, other)) continue;
            TryTopple(mover, other);
            TrySquish(mover, other, stepX, stepY);
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

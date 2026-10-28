using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// The fire points of a garrison (F-60, step 4, docs/FIRE-FROM-CONTAINERS.md): a rider of an enclosing garrison stands at the building's centre until it has a victim, then at the
/// free fire point of the model closest to it (<c>GarrisonContain</c>); a rider of a fire base stands at its station. Range, view and shot of such a rider are measured from where
/// it stands. The riders of every other container are still measured from their container (the stand-in of step 5, <see cref="FiringContainerOf"/>).
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>The object whose position a rider's range, view, search and shot are measured from: the rider itself in a garrison, its container in any other, itself outside.</summary>
    private Entity ShooterOf(Entity entity)
        => FiringContainerOf(entity) is { } container && container.Container is not GarrisonContain ? container : entity;

    /// <summary>The enclosing garrison the rider is inside, if any.</summary>
    private GarrisonContain? EnclosingGarrisonOf(Entity entity)
        => FiringContainerOf(entity)?.Container is GarrisonContain { IsEnclosing: true } garrison ? garrison : null;

    /// <summary>
    /// Where <see cref="WithinWeaponRange"/> measures from (<c>WeaponSet::getAbleToUseWeaponAgainstTarget</c>): a rider of an enclosing garrison at a point from its point; one at the
    /// centre from the free point closest to the victim (<c>calcBestGarrisonPosition</c>) - false when every point is taken; everybody else from <see cref="ShooterOf"/>, read where its
    /// begun frame ends above 30 Hz.
    /// </summary>
    private bool TryRangeOrigin(Entity source, Fixed victimX, Fixed victimY, Fixed victimZ, out Fixed x, out Fixed y)
    {
        if (EnclosingGarrisonOf(source) is { } garrison && garrison.PointOf(source.Id) < 0)
        {
            var found = garrison.TryBestPosition(this, victimX, victimY, victimZ, out var position);
            (x, y) = (position.X, position.Y);
            return found;
        }

        var shooter = ShooterOf(source);
        (x, y, _) = _firstFrameTicks != TickOrder.Next ? shooter.FrameEnd(_tick) : (shooter.X, shooter.Y, shooter.Z);
        return true;
    }

    /// <summary><c>attemptBestFirePointPosition</c> as the attack state enters: a rider of an enclosing garrison at the centre takes the free point closest to its victim.</summary>
    private void PlaceAtFirePoint(Entity rider, Entity victim)
    {
        if (EnclosingGarrisonOf(rider) is not { } garrison || garrison.PointOf(rider.Id) >= 0) return;
        var (x, y, z) = victim.FrameEnd(_tick);
        garrison.PutAtBestPoint(this, rider, victim.Id, x, y, z);
    }

    public void MatchGarrisonPoints(Entity building, GarrisonContain garrison)
    {
        // removeInvalidObjectsFromGarrisonPoints: a rider at a point that attacks nobody, or whose victim is out of its current weapon's range from there, goes back to the centre.
        foreach (var id in garrison.RidersAtPoints().ToArray())
        {
            if (FindEntity(id) is not { } rider) continue;
            var victim = VictimOf(rider);
            var valid = victim is not null && CurrentWeapon(rider) is { } weapon && WithinWeaponRange(rider, weapon, victim);
            if (!valid) garrison.RemoveFromPoint(this, rider);
        }

        // addValidObjectsToGarrisonPoints, in the order the riders came in.
        foreach (var id in garrison.Contained.ToArray())
            if (FindEntity(id) is { } rider && VictimOf(rider) is { } victim)
            {
                var (x, y, z) = victim.FrameEnd(_tick);
                garrison.PutAtBestPoint(this, rider, victim.Id, x, y, z);
            }

        // trackTargets: a rider at a point moves to a free point closer to its victim.
        foreach (var id in garrison.Contained.ToArray())
            if (FindEntity(id) is { } rider && VictimOf(rider) is { } victim)
            {
                var (x, y, z) = victim.FrameEnd(_tick);
                garrison.TrackTarget(this, rider, victim.Id, x, y, z);
            }

        _ = building;
    }

    /// <summary><c>AIUpdateInterface::getCurrentVictim</c>: the living object the unit attacks, or null.</summary>
    private Entity? VictimOf(Entity entity)
        => entity.Acquisition is { Victim.IsValid: true } state && FindEntity(state.Victim) is { DestroyRequested: false, EffectivelyDead: false } victim ? victim : null;
}

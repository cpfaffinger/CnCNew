using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// The fire points of a garrison (F-60, step 4, docs/FIRE-FROM-CONTAINERS.md): a rider of an enclosing garrison stands at the building's centre until it has a victim, then at the
/// free fire point of the model closest to it (<c>GarrisonContain</c>); a rider of a fire base stands at its station. Range, view and shot of such a rider are measured from where
/// it stands. A transport's rider stands at the transport's fire point of its place among the riders, a Helix's above the Helix (<see cref="ShooterPosition"/>).
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>
    /// <c>Weapon::computeBonus</c>'s conditions: the object's own, and those of the container it rides in when that passes its bonus on (<c>WeaponBonusPassedToPassengers</c>, the Battle
    /// Bus; the container's own conditions, not its container's). Every weapon calculation of a shooter - range, damage, reload, pre-attack delay - reads these.
    /// </summary>
    public uint WeaponBonusOf(Entity source)
        => source.ContainedBy.IsValid && FindEntity(source.ContainedBy) is { Container.PassesWeaponBonus: true } container
            ? source.WeaponBonusConditions | container.WeaponBonusConditions
            : source.WeaponBonusConditions;

    /// <summary><c>HelixContain::redeployOccupants</c>: its riders stand 8 above it.</summary>
    private static readonly Fixed HelixRiderHeight = Fixed.FromInt(8);

    /// <summary>
    /// Where a rider's range, view, search and shot are measured from - where its begun frame ends above 30 Hz when <paramref name="frameEnd"/>. Outside a container and in a garrison the
    /// object itself (a garrison puts its riders at their points and stations). In any other container where <c>OpenContain::redeployOccupants</c> puts it, which the original
    /// does every time the container's transform changes: a Helix's rider 8 above the Helix; a transport's at the transport's <c>FIREPOINTnn</c> of its place among the riders in
    /// the order they came in, the points used round from the first (<c>putObjAtNextFirePoint</c>), turned with the turret about its bone for <c>PassengersInTurret</c>
    /// (<c>getSingleLogicalBonePositionOnTurret</c>); without points the container's position. Derived from the container's pose, not kept: after a rider leaves a standing
    /// transport the others take their new points at once, the original at the transport's next move (docs/FIRE-FROM-CONTAINERS.md §3, step 4).
    /// </summary>
    private (Fixed X, Fixed Y, Fixed Z) ShooterPosition(Entity entity, bool frameEnd)
    {
        if (FiringContainerOf(entity) is not { } container || container.Container is GarrisonContain)
            return frameEnd ? entity.FrameEnd(_tick) : (entity.X, entity.Y, entity.Z);
        var (x, y, z) = frameEnd ? container.FrameEnd(_tick) : (container.X, container.Y, container.Z);
        if (container.Container is HelixContain helix) return helix.PortableStructure == entity.Id ? (x, y, z) : (x, y, z + HelixRiderHeight);
        if (!_templates.Geometry.FirePoints.TryGetValue(container.Template.Name, out var points) || points.Transport.IsEmpty) return (x, y, z);
        var riders = container.Container is ContainModuleBase own ? own.Contained : container.Container!.Contained;
        var place = 0;
        while (place < riders.Count && riders[place] != entity.Id) place++;
        var pose = points.Transport[place % points.Transport.Length];
        var (px, py) = (pose.X, pose.Y);
        if (points.TurretPivot is { } pivot && container.TurretOf(0) is { } turret)
        {
            var (turnCos, turnSin) = (FixedTrig.Cos(turret.Angle), FixedTrig.Sin(turret.Angle));
            var (dx, dy) = (px - pivot.X, py - pivot.Y);
            (px, py) = (pivot.X + dx * turnCos - dy * turnSin, pivot.Y + dx * turnSin + dy * turnCos);
        }

        var angle = frameEnd ? container.FrameEndAngle(_tick) : container.Angle;
        var (cos, sin) = (FixedTrig.Cos(angle), FixedTrig.Sin(angle));
        return (x + px * cos - py * sin, y + px * sin + py * cos, z + pose.Z);
    }

    /// <summary>The enclosing garrison the rider is inside, if any.</summary>
    private GarrisonContain? EnclosingGarrisonOf(Entity entity)
        => FiringContainerOf(entity)?.Container is GarrisonContain { IsEnclosing: true } garrison ? garrison : null;

    /// <summary>
    /// Where <see cref="WithinWeaponRange"/> measures from (<c>WeaponSet::getAbleToUseWeaponAgainstTarget</c>): a rider of an enclosing garrison at a point from its point; one at the
    /// centre from the free point closest to the victim (<c>calcBestGarrisonPosition</c>) - false when every point is taken; everybody else from <see cref="ShooterPosition"/>, read where its
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

        (x, y, _) = ShooterPosition(source, _firstFrameTicks != TickOrder.Next);
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

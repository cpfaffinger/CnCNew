using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// Where a projectile starts and which way it points (<c>Weapon::calcProjectileLaunchPosition</c>, <c>Weapon::positionProjectileForLaunch</c>, docs/SIMULATION-CORE.md §75): the launch bone of
/// the barrel the slot has come to, turned with the turret that carries the slot - pitched about its pitch bone, then turned about its angle bone - and with the object's heading. The
/// original also takes the object's tilt on the ground, which the simulation does not have (a named difference).
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>A launch point in the world and the unit vector of the launcher's x axis there.</summary>
    private readonly record struct LaunchPose(Fixed X, Fixed Y, Fixed Z, Fixed ForwardX, Fixed ForwardY, Fixed ForwardZ);

    /// <summary>The launch of <paramref name="weapon"/> from an object standing at the given place with the given heading; null for a template without launch points.</summary>
    private LaunchPose? LaunchPoseOf(Entity source, WeaponInfo weapon, Fixed x, Fixed y, Fixed z, Fixed heading)
    {
        if (!_templates.Geometry.WeaponLaunches.TryGetValue(source.Template.Name, out var points)) return null;
        var slot = SlotOfWeapon(source, weapon);
        var launch = points.Slots[slot];
        // A slot without barrels has no launch position: the identity (Weapon::calcProjectileLaunchPosition, "ProjectileLaunchPos not found").
        var barrel = launch.Barrels.IsDefaultOrEmpty ? LaunchBarrel.Origin : launch.Barrels[UseBarrel(source, slot, launch.Barrels.Length, weapon)];
        var (px, py, pz) = (barrel.X, barrel.Y, barrel.Z);
        var (fx, fy, fz) = (barrel.ForwardX, barrel.ForwardY, barrel.ForwardZ);
        if (TurretOfSlot(source, slot) is { } carried)
        {
            var turret = carried.Index < launch.Turrets.Length ? launch.Turrets[carried.Index] : default;
            if (turret.ArtAngle != Fixed.Zero)
            {
                (px, py) = TurnAboutZ(px, py, turret.ArtAngle);
                (fx, fy) = TurnAboutZ(fx, fy, turret.ArtAngle);
            }
            if (turret.ArtPitch != Fixed.Zero)
            {
                (px, pz) = PitchUp(px, pz, turret.ArtPitch);
                (fx, fz) = PitchUp(fx, fz, turret.ArtPitch);
            }
            // The pristine bone is the front and centre position of the point: the turret's pitch about its pitch bone first, then its turn about its angle bone.
            var (dx, dz) = PitchUp(px - turret.PitchX, pz - turret.PitchZ, carried.Pitch);
            (px, pz) = (dx + turret.PitchX, dz + turret.PitchZ);
            (fx, fz) = PitchUp(fx, fz, carried.Pitch);
            var (ex, ey) = TurnAboutZ(px - turret.AngleX, py - turret.AngleY, carried.Angle);
            (px, py) = (ex + turret.AngleX, ey + turret.AngleY);
            (fx, fy) = TurnAboutZ(fx, fy, carried.Angle);
        }

        var (worldX, worldY) = TurnAboutZ(px, py, heading);
        var (forwardX, forwardY) = TurnAboutZ(fx, fy, heading);
        var length = Fixed.Sqrt(forwardX * forwardX + forwardY * forwardY + fz * fz);
        if (length <= Fixed.Zero) return new LaunchPose(x + worldX, y + worldY, z + pz, FixedTrig.Cos(heading), FixedTrig.Sin(heading), Fixed.Zero);
        return new LaunchPose(x + worldX, y + worldY, z + pz, forwardX / length, forwardY / length, fz / length);
    }

    private static (Fixed X, Fixed Y) TurnAboutZ(Fixed x, Fixed y, Fixed angle)
    {
        var (cos, sin) = (FixedTrig.Cos(angle), FixedTrig.Sin(angle));
        return (x * cos - y * sin, x * sin + y * cos);
    }

    /// <summary>A rotation about the y axis by minus <paramref name="pitch"/>: a positive pitch raises the x axis.</summary>
    private static (Fixed X, Fixed Z) PitchUp(Fixed x, Fixed z, Fixed pitch)
    {
        var (cos, sin) = (FixedTrig.Cos(pitch), FixedTrig.Sin(pitch));
        return (x * cos - z * sin, x * sin + z * cos);
    }

    /// <summary>The slot <paramref name="weapon"/> fires from: the current one when it holds it, else the first that does; primary for a weapon of no slot (<c>createAndFireTempWeapon</c>).</summary>
    private int SlotOfWeapon(Entity source, WeaponInfo weapon)
    {
        if (ActiveWeaponSet(source) is not { } set) return WeaponSlot.Primary;
        var current = source.CurrentWeaponSlot;
        if (set.HasWeapon(current) && set.Weapons[current] == weapon.Index) return current;
        for (var slot = 0; slot < WeaponSlot.Count; slot++)
            if (set.HasWeapon(slot) && set.Weapons[slot] == weapon.Index) return slot;
        return WeaponSlot.Primary;
    }

    /// <summary><c>AIUpdateInterface::getWhichTurretForWeaponSlot</c>: the first turret that carries the slot, with its angle and pitch where its begun frame ends.</summary>
    private static (int Index, Fixed Angle, Fixed Pitch)? TurretOfSlot(Entity source, int slot)
    {
        if (source.Turrets is not { } states || source.Template.Turrets is not { } set) return null;
        for (var index = 0; index < states.Length; index++)
        {
            if (states[index] is not { } state || set[index] is not { } info || !info.Controls(slot)) continue;
            return state.SubSteps != 0 ? (index, state.GoalAngle, state.GoalPitch) : (index, state.Angle, state.Pitch);
        }

        return null;
    }

    /// <summary>
    /// <c>Weapon::privateFireWeapon</c>: the barrel of this shot, and on to the next one after <c>ShotsPerBarrel</c> shots; a barrel beyond the state's count starts over. An object with
    /// one barrel has nothing to keep.
    /// </summary>
    private int UseBarrel(Entity source, int slot, int barrels, WeaponInfo weapon)
    {
        if (barrels <= 1) return 0;
        var state = source.Barrels;
        var (current, shots) = state is null ? (0, weapon.ShotsPerBarrel) : (state.Current[slot], state.ShotsLeft[slot]);
        if (current >= barrels) (current, shots) = (0, weapon.ShotsPerBarrel);
        var used = current;
        if (--shots <= 0) (current, shots) = (current + 1, weapon.ShotsPerBarrel);
        state ??= source.Barrels = new BarrelState(weapon.ShotsPerBarrel);
        state.Current[slot] = current;
        state.ShotsLeft[slot] = shots;
        MarkDirty(source);
        return used;
    }
}

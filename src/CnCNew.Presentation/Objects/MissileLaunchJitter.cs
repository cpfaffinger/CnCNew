using System.Numerics;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// The shake of a missile right after its launch (<c>NeutronMissileUpdate::doAttack</c>): for every logic frame of the attack before <c>SpecialSpeedTime</c> has passed since
/// <c>doLaunch</c>, the drawable's instance matrix is a translation by <c>(0, y, z)</c> with y and z each drawn in [-1, 1] and scaled by
/// <c>(1 - elapsed / SpecialSpeedTime) · SpecialJitterDistance</c>, the vector turned by the missile's matrix; once the time is over the instance matrix is cleared. The
/// original draws the two values from the logic stream, but notes that they need not be synced - so they come from a hash of (object, frame since the launch) here, which keeps
/// pictures reproducible and the simulation untouched. The instance matrix is applied in the object's own space (<c>Drawable::getTransformMatrix * instance</c>), after the vector
/// was already turned by the matrix once, so the offset in the world is the vector turned twice.
/// </summary>
public static class MissileLaunchJitter
{
    /// <summary>The shake's offset in the world, or zero outside the shake.</summary>
    /// <param name="id">The missile's object id.</param>
    /// <param name="ticksSinceLaunch">Ticks since the tick of <c>doLaunch</c> (the projector's tick minus the launch tick).</param>
    /// <param name="specialSpeedTicks"><c>SpecialSpeedTime</c> in ticks.</param>
    /// <param name="ticksPerFrame">Ticks per logic frame of the original (1 at 30 Hz).</param>
    /// <param name="distance"><c>SpecialJitterDistance</c>.</param>
    /// <param name="rotation">The missile's drawn rotation (row vectors: chassis pitch, then heading).</param>
    public static Vector3 Offset(uint id, long ticksSinceLaunch, long specialSpeedTicks, int ticksPerFrame, float distance, Matrix4x4 rotation)
    {
        if (distance <= 0f || specialSpeedTicks <= 0 || ticksPerFrame <= 0 || ticksSinceLaunch < 0) return Vector3.Zero;
        // The attack decides once per frame, the first time one frame after the launch; the frame of the launch itself has no instance matrix yet.
        var frames = ticksSinceLaunch / ticksPerFrame;
        var elapsed = frames * ticksPerFrame;
        if (frames < 1 || elapsed >= specialSpeedTicks) return Vector3.Zero;
        var amplitude = (1f - (float)elapsed / specialSpeedTicks) * distance;
        var local = new Vector3(0f, Unit(id, frames * 2) * amplitude, Unit(id, frames * 2 + 1) * amplitude);
        return Vector3.TransformNormal(Vector3.TransformNormal(local, rotation), rotation);
    }

    /// <summary>A value in [-1, 1] from a hash of (object, draw).</summary>
    private static float Unit(uint id, long draw)
    {
        unchecked
        {
            var value = id * 2654435761u ^ (uint)draw * 40503u ^ 0x9E3779B9u;
            value ^= value >> 15;
            value *= 2246822519u;
            value ^= value >> 13;
            return (value >> 8) / 16777215f * 2f - 1f;
        }
    }
}

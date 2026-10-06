using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// The turret angle and pitch of every object that has a turret (<c>AIUpdateInterface::getTurretRotAndPitch</c>, what <c>W3DModelDraw::handleClientTurretPositioning</c> draws). The
/// simulation owns the turret now (<see cref="Entity.TurretOf"/>: it turns at the turret's rates toward the victim of the latest attack order, holds, recentres and decides when the
/// weapon may fire; docs/SIM-TURRET-AIM.md), so this class only reads that state and remembers which turrets were still turning for <c>MODELCONDITION_TURRET_ROTATE</c>
/// (<see cref="IsRotating"/>). It guesses nothing and never influences the simulation.
/// </summary>
public sealed class TurretTracker
{
    private readonly Dictionary<(uint Entity, int Turret), bool> _rotating = [];

    /// <summary>
    /// <c>TurretAI::friend_turnTowardsAngle</c> sets <c>MODELCONDITION_TURRET_ROTATE</c> on the owner while a turret has not reached the angle it turns to and clears it as soon as it snaps there:
    /// true when either turret of the object was turning in the latest <see cref="Update"/>.
    /// </summary>
    public bool IsRotating(uint entity)
        => (_rotating.TryGetValue((entity, 0), out var main) && main) || (_rotating.TryGetValue((entity, 1), out var alt) && alt);

    /// <summary>The turret's angle relative to the body and its pitch (radians), or null when the object has no such turret.</summary>
    public (float Angle, float Pitch)? Update(Entity entity, int turretIndex)
    {
        if (entity.TurretOf(turretIndex) is not { } reading)
        {
            _rotating.Remove((entity.Id.Value, turretIndex));
            return null;
        }

        _rotating[(entity.Id.Value, turretIndex)] = reading.Rotating;
        return (ToFloat(reading.Angle), ToFloat(reading.Pitch));
    }

    /// <summary>Forgets the objects that no longer exist, so the table does not grow over a long match.</summary>
    public void Prune(Func<uint, bool> exists)
    {
        foreach (var key in _rotating.Keys.Where(key => !exists(key.Entity)).ToArray()) _rotating.Remove(key);
    }

    private static float ToFloat(Fixed value) => FixedConvert.ToSingle(value);
}

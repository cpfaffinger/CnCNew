using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// What the scripts add to the units' own target choice (<see cref="SimulationWorld.AcquisitionAdvisor"/>): the attack priority set an object was given and the initial attitude of
/// its team (<c>teamAggressiveness</c> of the map's team, <c>m_initialTeamAttitude</c>; what a script or a transport gives one object is <see cref="Entity.OwnAttitude"/>).
/// </summary>
public sealed partial class ScriptEngine : IAcquisitionAdvisor
{
    public bool TryAttackPriority(Entity attacker, Entity target, out int priority)
    {
        priority = DefaultAttackPriority;
        if (!_prioritySetOf.TryGetValue(attacker.Id.Value, out var name) || !_prioritySets.ContainsKey(name)) return false;
        priority = AttackPriority(attacker, target);
        return true;
    }

    public Attitude AttitudeOf(Entity entity)
    {
        if (TeamOfEntity(entity.Id.Value) is not { } team) return Attitude.Normal;
        var value = _program.FindTeam(team.Prototype)?.Aggressiveness ?? 0;
        // AIUpdateInterface::getMoodMatrixValue: an unknown mood counts as normal.
        return value is >= (int)Attitude.Sleep and <= (int)Attitude.Aggressive ? (Attitude)value : Attitude.Normal;
    }
}

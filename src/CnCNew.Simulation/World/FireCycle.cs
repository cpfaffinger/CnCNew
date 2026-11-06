using CnCNew.Simulation.Ids;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.World;

/// <summary>The two firing states of the original's attack machine (F-140): <c>AIM_AT_TARGET</c> and <c>FIRE_WEAPON</c> of <c>AttackStateMachine</c>, <c>TURRETAI_AIM</c> and <c>TURRETAI_FIRE</c> of <c>TurretStateMachine</c>.</summary>
internal enum FireCyclePhase : byte
{
    /// <summary><c>AIAttackAimAtTargetState</c> / <c>TurretAIAimTurretState</c>: the machine's default state; it succeeds into the fire state once the weapon points at its target.</summary>
    Aim = 1,

    /// <summary><c>AIAttackFireWeaponState</c>: fires once when the weapon is ready and goes back to aiming, or goes back at once when it is not.</summary>
    Fire = 2,
}

/// <summary>
/// The state of one attack machine's firing half (F-140, docs/SIMULATION-CORE.md): which of the two states it is in and from which tick its update runs. The original's machine runs
/// one state's update per logic frame and enters the next state at the end of it, so the update of a state entered in a frame comes in the next one: after a shot the weapon aims in
/// the next frame and fires in the one after, and a weapon fires on every second frame at the most. Present only while an attack runs it, so every object that does not attack keeps
/// its serialized form.
/// </summary>
internal sealed class FireCycle
{
    /// <summary>The state the machine is in.</summary>
    public FireCyclePhase Phase;

    /// <summary>The first tick of the attacker's own logic frame of the original from which the state's update runs: the frame after the one the state was entered in, or that frame itself when it was entered before the update of its frame (an order).</summary>
    public long Due;

    /// <summary>The victim of a body's attack on an object the machine runs for (<c>AIAttackState</c> builds a new machine for every attack); invalid for a turret's machine and an attack on a place.</summary>
    public EntityId Victim;

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject("fireCycle");
        writer.Int("phase", (long)Phase);
        writer.Int("due", Due);
        if (Victim.IsValid) writer.UInt("victim", Victim.Value);
        writer.EndObject();
    }

    public static FireCycle Load(IStateReader reader)
    {
        var cycle = new FireCycle
        {
            Phase = (FireCyclePhase)checked((byte)reader.Int("phase")),
            Due = reader.Int("due"),
            Victim = reader.Has("victim") ? new EntityId(checked((uint)reader.UInt("victim"))) : EntityId.Invalid,
        };
        if (cycle.Phase is not (FireCyclePhase.Aim or FireCyclePhase.Fire) || cycle.Due < 0) throw new InvalidDataException("Attack machine state out of range in snapshot.");
        return cycle;
    }
}

/// <summary>Every field of a <see cref="FireCycle"/> (or none), compared before and after a step.</summary>
internal readonly record struct FireCycleStamp(bool Present, FireCyclePhase Phase, long Due, EntityId Victim)
{
    public static FireCycleStamp Of(FireCycle? cycle) => cycle is null ? default : new(true, cycle.Phase, cycle.Due, cycle.Victim);
}

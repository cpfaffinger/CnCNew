using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// A unit's attack on a place (<c>AI_ATTACK_POSITION</c> with its <c>AIAttackState</c>, docs/ATTACK-POSITION.md, F-64): the place, the shots it may still fire, the slot locked when the
/// attack began, and whether it walks toward the place. Present only while the unit attacks a place, so a unit that never does keeps its serialized form.
/// </summary>
internal sealed class AttackPlaceState
{
    /// <summary>The machine's goal position (clipped to the map, on the ground).</summary>
    public Fixed X;
    public Fixed Y;
    public Fixed Z;

    /// <summary><c>Weapon::m_maxShotCount</c> of the attack: the shots it may still fire; -1 for no limit.</summary>
    public int ShotsLeft = -1;

    /// <summary><c>m_lockedWeaponOnEnter</c>: the slot that was locked when the attack began (-1: none); when another weapon becomes the current one the attack ends.</summary>
    public int LockedSlot = -1;

    /// <summary>The unit walks toward the place (<c>AIAttackApproachTargetState</c>).</summary>
    public bool Chasing;

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject("attackPlace");
        writer.Fixed("x", X);
        writer.Fixed("y", Y);
        writer.Fixed("z", Z);
        writer.Int("shotsLeft", ShotsLeft);
        writer.Int("lockedSlot", LockedSlot);
        if (Chasing) writer.Bool("chasing", true);
        writer.EndObject();
    }

    public static AttackPlaceState Load(IStateReader reader)
    {
        var state = new AttackPlaceState
        {
            X = reader.Fixed("x"),
            Y = reader.Fixed("y"),
            Z = reader.Fixed("z"),
            ShotsLeft = checked((int)reader.Int("shotsLeft")),
            LockedSlot = checked((int)reader.Int("lockedSlot")),
            Chasing = reader.Has("chasing") && reader.Bool("chasing"),
        };
        if (state.ShotsLeft < -1 || state.LockedSlot < -1 || state.LockedSlot >= WeaponSlot.Count) throw new InvalidDataException("Attack on a place out of range in snapshot.");
        return state;
    }
}

/// <summary>Every mutable field of an <see cref="AttackPlaceState"/> (or none), compared before and after a step.</summary>
internal readonly record struct AttackPlaceStamp(bool Present, int ShotsLeft, bool Chasing)
{
    public static AttackPlaceStamp Of(AttackPlaceState? state) => state is null ? default : new(true, state.ShotsLeft, state.Chasing);
}

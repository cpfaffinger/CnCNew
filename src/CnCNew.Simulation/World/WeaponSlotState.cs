using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary><c>WeaponLockType</c> (WeaponSet.h): whether the current weapon slot is pinned against automatic weapon choice.</summary>
public enum WeaponLockKind : byte
{
    /// <summary><c>NOT_LOCKED</c>.</summary>
    None = 0,
    /// <summary><c>LOCKED_TEMPORARILY</c>: a command button or special ability pinned the slot; ends when the clip empties or the unit is given a move order.</summary>
    Temporary = 1,
    /// <summary><c>LOCKED_PERMANENTLY</c>: <c>LockWeaponCreate</c> or a <c>SWITCH_WEAPON</c> button; only an explicit release or a weapon set change ends it.</summary>
    Permanent = 2,
}

/// <summary>
/// The weapon state of an object whose weapon sets hold more than one weapon (<see cref="ObjectTemplate.HasMultipleWeaponSlots"/>): what
/// <c>WeaponSet</c>/<c>Weapon</c> keep per slot (<c>m_ammoInClip</c>, <c>m_whenWeCanFireAgain</c>, <c>m_lastFireFrame</c>) plus the current slot and lock
/// (<c>m_curWeapon</c>, <c>m_curWeaponLockedStatus</c>) and the weapon set the slots were built for (<c>m_curWeaponTemplateSet</c>). Slot 0 (primary) keeps its
/// clip and ready tick in <see cref="Entity.WeaponClip"/>/<see cref="Entity.WeaponReadyTick"/>, exactly like a single-weapon object, so the arrays here use the
/// slot number as index and leave index 0 of <see cref="Clip"/> and <see cref="Ready"/> unused. Objects with one weapon never get one of these and keep the
/// pre-slot snapshot shape.
/// </summary>
internal sealed class WeaponSlotState
{
    /// <summary>Rounds left per slot; -1 when the slot holds no weapon, <see cref="int.MaxValue"/> for an unlimited clip.</summary>
    public readonly int[] Clip = [-1, -1, -1];

    /// <summary>Tick at which each slot may fire again (shot delay or reload, whichever applies); unused for slot 0.</summary>
    public readonly long[] Ready = [0, 0, 0];

    /// <summary>Tick of each slot's last shot, -1 for never.</summary>
    public readonly long[] LastFire = [-1, -1, -1];

    /// <summary>The slot <c>Object::getCurrentWeapon</c> would fire (<c>m_curWeapon</c>): the last chosen or locked slot.</summary>
    public int Current;

    public WeaponLockKind Lock;

    /// <summary>Index of the <see cref="WeaponSetInfo"/> the slots were built for; compared with the set the entity's flags select to notice a set change.</summary>
    public int SetIndex = -1;

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject("weaponSlots");
        writer.Int("set", SetIndex);
        writer.Int("current", Current);
        writer.Int("lock", (long)Lock);
        writer.Int("clip1", Clip[1]);
        writer.Int("clip2", Clip[2]);
        writer.Int("ready1", Ready[1]);
        writer.Int("ready2", Ready[2]);
        writer.Int("fire0", LastFire[0]);
        writer.Int("fire1", LastFire[1]);
        writer.Int("fire2", LastFire[2]);
        writer.EndObject();
    }

    public static WeaponSlotState Load(IStateReader reader, ObjectTemplate template)
    {
        var state = new WeaponSlotState
        {
            SetIndex = checked((int)reader.Int("set")),
            Current = checked((int)reader.Int("current")),
            Lock = (WeaponLockKind)checked((byte)reader.Int("lock")),
        };
        state.Clip[1] = checked((int)reader.Int("clip1"));
        state.Clip[2] = checked((int)reader.Int("clip2"));
        state.Ready[1] = reader.Int("ready1");
        state.Ready[2] = reader.Int("ready2");
        state.LastFire[0] = reader.Int("fire0");
        state.LastFire[1] = reader.Int("fire1");
        state.LastFire[2] = reader.Int("fire2");
        if (state.SetIndex < -1 || state.SetIndex >= template.WeaponSets.Length) throw new InvalidDataException("Weapon slots reference an unknown weapon set.");
        if (state.Current is < 0 or >= WeaponSlot.Count) throw new InvalidDataException("Current weapon slot out of range.");
        if (state.Lock > WeaponLockKind.Permanent) throw new InvalidDataException("Unknown weapon lock kind.");
        return state;
    }
}

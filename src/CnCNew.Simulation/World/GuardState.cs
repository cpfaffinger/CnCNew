using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.World;

/// <summary>The states of <c>AIGuardMachine</c> (<c>AIGuard.h</c>); <c>GET_CRATE</c> never holds (its crate is never found) and is passed through.</summary>
internal enum GuardPhase : byte { Inner = 1, Outer = 2, Return = 3, Idle = 4, Aggressor = 5 }

/// <summary>
/// A unit's guard (<c>AI_GUARD</c> with its <c>AIGuardMachine</c>, docs/GUARD-STATE.md, F-58): what it guards - a position or an object -, in which mode, which state of the machine it is
/// in and what that state remembers. The attack of the attacking states runs through the unit's <see cref="AcquisitionState"/> (its victim and chase). Present only while the unit
/// guards, so a unit that never guards keeps its serialized form.
/// </summary>
internal sealed class GuardState
{
    /// <summary><c>m_locationToGuard</c>: the guarded position (the guarded object's position when the guard began, for an object).</summary>
    public Fixed X;
    public Fixed Y;

    /// <summary><c>m_objectToGuard</c>: the guarded object; invalid for a position.</summary>
    public EntityId Object;

    /// <summary><c>m_areaToGuard</c>: the guarded trigger area by name (empty: none); <see cref="X"/>, <see cref="Y"/> are then its bounds centre.</summary>
    public string Area = string.Empty;

    public GuardMode Mode;

    public GuardPhase Phase;

    /// <summary><c>AIGuardMachine::m_nemesisID</c>: the enemy the attacking states attack.</summary>
    public EntityId Nemesis;

    /// <summary><c>ExitConditions::m_attackGiveUpFrame</c> of OUTER and ATTACK_AGGRESSOR (a tick); 0 for none.</summary>
    public long GiveUpTick;

    /// <summary>RETURN's <c>m_nextReturnScanTime</c> and IDLE's <c>m_nextEnemyScanTime</c> (a tick).</summary>
    public long NextScanTick;

    /// <summary>IDLE's <c>m_guardeePos</c>: where the guarded object was last noted.</summary>
    public Fixed GuardeeX;
    public Fixed GuardeeY;

    /// <summary>
    /// <c>ActiveBody::getClearableLastAttacker</c>: the guard reads the body's last damage record and clears it; this is the tick of the record it cleared last (-1: none), so a record of
    /// a later tick is new.
    /// </summary>
    public long ClearedDamageTick = -1;

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject("guard");
        writer.Fixed("x", X);
        writer.Fixed("y", Y);
        if (Object.IsValid) writer.UInt("object", Object.Value);
        if (Area.Length > 0) writer.Text("area", Area);
        if (Mode != GuardMode.Normal) writer.Int("mode", (int)Mode);
        writer.Int("phase", (int)Phase);
        if (Nemesis.IsValid) writer.UInt("nemesis", Nemesis.Value);
        if (GiveUpTick != 0) writer.Int("giveUpTick", GiveUpTick);
        writer.Int("nextScanTick", NextScanTick);
        if (Object.IsValid)
        {
            writer.Fixed("guardeeX", GuardeeX);
            writer.Fixed("guardeeY", GuardeeY);
        }
        if (ClearedDamageTick >= 0) writer.Int("clearedDamageTick", ClearedDamageTick);
        writer.EndObject();
    }

    public static GuardState Load(IStateReader reader)
    {
        var state = new GuardState
        {
            X = reader.Fixed("x"),
            Y = reader.Fixed("y"),
            Object = reader.Has("object") ? new EntityId(checked((uint)reader.UInt("object"))) : EntityId.Invalid,
            Area = reader.Has("area") ? reader.Text("area") : string.Empty,
            Mode = reader.Has("mode") ? (GuardMode)checked((byte)reader.Int("mode")) : GuardMode.Normal,
            Phase = (GuardPhase)checked((byte)reader.Int("phase")),
            Nemesis = reader.Has("nemesis") ? new EntityId(checked((uint)reader.UInt("nemesis"))) : EntityId.Invalid,
            GiveUpTick = reader.Has("giveUpTick") ? reader.Int("giveUpTick") : 0,
            NextScanTick = reader.Int("nextScanTick"),
            ClearedDamageTick = reader.Has("clearedDamageTick") ? reader.Int("clearedDamageTick") : -1,
        };
        if (state.Object.IsValid)
        {
            state.GuardeeX = reader.Fixed("guardeeX");
            state.GuardeeY = reader.Fixed("guardeeY");
        }
        if (!Enum.IsDefined(state.Mode) || !Enum.IsDefined(state.Phase)) throw new InvalidDataException("Guard mode or state out of range in snapshot.");
        if (state.NextScanTick < 0 || state.GiveUpTick < 0) throw new InvalidDataException("Negative guard tick in snapshot.");
        return state;
    }
}

/// <summary>Every mutable field of a <see cref="GuardState"/> (or none), compared before and after a step to learn whether the entity's serialized state changed.</summary>
internal readonly record struct GuardStamp(bool Present, GuardPhase Phase, EntityId Nemesis, long GiveUpTick, long NextScanTick, Fixed GuardeeX, Fixed GuardeeY, long ClearedDamageTick)
{
    public static GuardStamp Of(GuardState? state)
        => state is null ? default : new(true, state.Phase, state.Nemesis, state.GiveUpTick, state.NextScanTick, state.GuardeeX, state.GuardeeY, state.ClearedDamageTick);
}

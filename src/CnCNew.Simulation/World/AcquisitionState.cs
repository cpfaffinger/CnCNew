using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.World;

/// <summary>
/// What a unit's own attack decisions need to remember (docs/SIMULATION-CORE.md §33): the victim it is attacking, for an attack-move (<c>AI_ATTACK_MOVE_TO</c>) where the march
/// goes, and when it looks for a victim next. An object that never looks for victims by itself has none, so it keeps its serialized form (and hash).
/// </summary>
internal sealed class AcquisitionState
{
    /// <summary>The victim of the unit's attack (<c>AIUpdateInterface::m_currentVictimID</c>); invalid for none.</summary>
    public EntityId Victim;

    /// <summary>The victim was named by a fire or pursue order of the unit's command giver, not chosen by the unit: the world then neither fires at it by itself nor looks for another.</summary>
    public bool Ordered;

    /// <summary>Tick of the latest such order; an ordered attack that gets no further order for a while is over.</summary>
    public long OrderedTick;

    /// <summary>The unit is on an attack-move to (<see cref="GoalX"/>, <see cref="GoalY"/>).</summary>
    public bool AttackMove;

    public Fixed GoalX;
    public Fixed GoalY;

    /// <summary>
    /// Which of the unit's states looks for victims right now (0 none, else an <c>AcquireMode</c>): the reference resets the mood check whenever a state that looks is entered
    /// (<c>AIIdleState::onEnter</c>, <c>TurretAIIdleState::onEnter</c>, the idle state inside <c>AIAttackMoveToState</c>), so a change of this value schedules a check two frames ahead.
    /// </summary>
    public byte Mode;

    /// <summary><c>AIUpdateInterface::m_nextMoodCheckTime</c>: the tick of the unit's next look for a victim; meaningful while <see cref="Mode"/> is set.</summary>
    public long NextCheck;

    /// <summary><c>m_randomlyOffsetMoodCheck</c>: the interval after the next look is shifted by up to half a check rate, once, so units that became idle together do not look together forever.</summary>
    public bool Offset;

    /// <summary>The march of the attack-move is suspended for a fight (<c>m_attackMoveMachine</c> left its idle state); it resumes toward the goal once the victim is gone.</summary>
    public bool Halted;

    /// <summary>The unit walks toward its victim (<c>AIAttackApproachTargetState</c>).</summary>
    public bool Chasing;

    /// <summary>Tick the chase was last pointed at the victim's position.</summary>
    public long ChaseTick;

    public bool IsDefault => !Victim.IsValid && !AttackMove && Mode == 0;

    public void ClearVictim()
    {
        Victim = EntityId.Invalid;
        Ordered = false;
        OrderedTick = 0;
        Chasing = false;
        ChaseTick = 0;
    }

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject("acquisition");
        writer.UInt("victim", Victim.Value);
        if (Ordered)
        {
            writer.Bool("ordered", true);
            writer.Int("orderedTick", OrderedTick);
        }
        if (AttackMove)
        {
            writer.Bool("attackMove", true);
            writer.Fixed("goalX", GoalX);
            writer.Fixed("goalY", GoalY);
            if (Halted) writer.Bool("halted", true);
        }
        if (Mode != 0)
        {
            writer.Int("mode", Mode);
            writer.Int("nextCheck", NextCheck);
            if (Offset) writer.Bool("offset", true);
        }
        if (Chasing)
        {
            writer.Bool("chasing", true);
            writer.Int("chaseTick", ChaseTick);
        }
        writer.EndObject();
    }

    public static AcquisitionState Load(IStateReader reader)
    {
        var state = new AcquisitionState { Victim = new EntityId(checked((uint)reader.UInt("victim"))) };
        if (reader.Has("ordered"))
        {
            state.Ordered = reader.Bool("ordered");
            state.OrderedTick = reader.Int("orderedTick");
        }
        if (reader.Has("attackMove"))
        {
            state.AttackMove = reader.Bool("attackMove");
            state.GoalX = reader.Fixed("goalX");
            state.GoalY = reader.Fixed("goalY");
            state.Halted = reader.Has("halted") && reader.Bool("halted");
        }
        if (reader.Has("mode"))
        {
            state.Mode = checked((byte)reader.Int("mode"));
            state.NextCheck = reader.Int("nextCheck");
            state.Offset = reader.Has("offset") && reader.Bool("offset");
            if (state.Mode is 0 or > 3 || state.NextCheck < 0) throw new InvalidDataException("Acquisition mode out of range in snapshot.");
        }
        if (reader.Has("chasing"))
        {
            state.Chasing = reader.Bool("chasing");
            state.ChaseTick = reader.Int("chaseTick");
        }
        if (state.IsDefault) throw new InvalidDataException("An acquisition state without a victim, an attack-move or a pending look is never written.");
        if ((state.Ordered || state.Chasing) && !state.Victim.IsValid) throw new InvalidDataException("An ordered attack or a chase needs a victim.");
        if (state.OrderedTick < 0 || state.ChaseTick < 0) throw new InvalidDataException("Negative acquisition tick in snapshot.");
        return state;
    }
}

/// <summary>Every mutable field of an <see cref="AcquisitionState"/> (or none), compared before and after a step to learn whether the entity's serialized state changed.</summary>
internal readonly record struct AcquisitionStamp(bool Present, EntityId Victim, bool Ordered, long OrderedTick, bool AttackMove, Fixed GoalX, Fixed GoalY, bool Halted, bool Chasing, long ChaseTick, byte Mode, long NextCheck, bool Offset)
{
    public static AcquisitionStamp Of(AcquisitionState? state)
        => state is null ? default : new(true, state.Victim, state.Ordered, state.OrderedTick, state.AttackMove, state.GoalX, state.GoalY, state.Halted, state.Chasing, state.ChaseTick, state.Mode, state.NextCheck, state.Offset);
}

/// <summary>
/// What the host of a world may add to the units' own target choice: the attack priorities and moods that map scripts set (<c>AttackPriorityInfo</c>, <c>AttitudeType</c>). A world without
/// one treats every unit as <see cref="Attitude.Normal"/> without a priority set.
/// </summary>
public interface IAcquisitionAdvisor
{
    /// <summary><c>AttackPriorityInfo::getPriority</c> for the set <paramref name="attacker"/> was given; false when it has none (<c>getDefaultAttackInfo</c>: the closest enemy wins).</summary>
    bool TryAttackPriority(Entity attacker, Entity target, out int priority);

    /// <summary><c>AIUpdateInterface::getAttitude</c>: the mood of the unit's team.</summary>
    Attitude AttitudeOf(Entity entity);
}

/// <summary><c>AttitudeType</c> (AI.h), same values as the reference and as the <c>teamAggressiveness</c> of a map's team.</summary>
public enum Attitude
{
    Sleep = -2,
    Passive = -1,
    Normal = 0,
    Alert = 1,
    Aggressive = 2,
}

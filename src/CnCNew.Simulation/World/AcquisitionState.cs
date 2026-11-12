using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

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

    /// <summary>
    /// <c>aiForceAttackObject</c> of a spawner's slave (<c>SpawnBehavior::orderSlavesToAttackTarget</c>, F-65): the slave attacks the victim its spawner was ordered to, walking at it,
    /// until the victim is gone or out of its reach for good; its own look does not replace it.
    /// </summary>
    public bool Forced;

    /// <summary>A forced attack that may also be on a neutral object: a bored builder's on a mine (<c>findMine</c>: <c>ALLOW_ENEMIES | ALLOW_NEUTRAL</c>, F-69).</summary>
    public bool Neutral;

    /// <summary>
    /// <c>m_originalVictimPos</c> and the controlling player of the first victim of an attack with a weapon that has a <c>ContinueAttackRange</c> (F-69): the next victim is looked for
    /// around this place, among that player's objects. Set at the first shot, kept while the attack goes on from victim to victim; <see cref="ContinueOwner"/> 0 when unset.
    /// </summary>
    public Fixed ContinueX;
    public Fixed ContinueY;
    public int ContinueOwner;

    /// <summary>
    /// The firing half of the attack machine on <see cref="Victim"/> for a weapon the body aims (F-140; a weapon on a turret has its turret's, <see cref="TurretState.Cycle"/>); null while the
    /// machine has not reached its aim state (no attack, or the victim out of range).
    /// </summary>
    public FireCycle? Cycle;

    /// <summary>
    /// The ordered attack was named by a fire order (not only by a pursuit): its attack machine steps in the frames of the unit whether an order arrives in that frame or not (F-140).
    /// <see cref="OrderOrigin"/> is the source of the latest fire order, whose weapon choice it uses.
    /// </summary>
    public bool FireOrdered;
    public FireOrigin OrderOrigin;

    /// <summary>
    /// A fire order arrived since the attack's last shot: the machine may fire it. Every fire order of the command giver stands for one shot, which the machine fires in its own
    /// frame (F-140) - the order stream gives an order while the weapon is ready, and the machine fires on its cycle.
    /// </summary>
    public bool ShotPending;

    /// <summary>
    /// <c>Weapon::m_maxShotCount</c> of an attack ordered with a limit (<c>aiAttackObject(victim, maxShots)</c>: <c>AssistedTargetingUpdate</c>'s clip, a script's <c>FIRE_WEAPON</c> button,
    /// <see cref="Commands.FireWeaponCommand.MaxShots"/>): the shots still to fire - <c>NO_MAX_SHOTS_LIMIT</c> counts down from 2^31 - 1 as in the original -, 0 for an attack whose command
    /// giver orders every shot. Each shot orders the next one itself; the last one ends the attack.
    /// </summary>
    public int ShotsLeft;

    /// <summary>
    /// <c>AIAttackState::m_lockedWeaponOnEnter</c> of an attack with <see cref="ShotsLeft"/>: the weapon slot that was locked when the attack was ordered, -1 for none. "If anything ever changes
    /// that weapon, we exit attack mode immediately" - such as the chooser after the shot that empties the clip released a temporary lock.
    /// </summary>
    public int LockedSlot = -1;

    public bool IsDefault => !Victim.IsValid && !AttackMove && Mode == 0;

    /// <summary><c>Weapon::m_leechWeaponRangeActive</c>: a leech-range weapon has fired or begun its wind-up in this attack.</summary>
    public bool LeechRange;

    public void ClearVictim()
    {
        Victim = EntityId.Invalid;
        LeechRange = false;
        Forced = false;
        Neutral = false;
        Ordered = false;
        OrderedTick = 0;
        Chasing = false;
        ChaseTick = 0;
        Cycle = null;
        FireOrdered = false;
        OrderOrigin = FireOrigin.Player;
        ShotPending = false;
        ShotsLeft = 0;
        LockedSlot = -1;
    }

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject("acquisition");
        if (LeechRange) writer.Bool("leechRange", true);
        writer.UInt("victim", Victim.Value);
        if (Ordered)
        {
            writer.Bool("ordered", true);
            writer.Int("orderedTick", OrderedTick);
        }
        if (FireOrdered)
        {
            writer.Bool("fireOrdered", true);
            writer.Int("orderOrigin", (long)OrderOrigin);
            if (ShotPending) writer.Bool("shotPending", true);
            if (ShotsLeft > 0) writer.Int("shotsLeft", ShotsLeft);
            if (LockedSlot >= 0) writer.Int("lockedSlot", LockedSlot);
        }
        Cycle?.Describe(writer);
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
        if (Forced) writer.Bool("forced", true);
        if (Neutral) writer.Bool("neutral", true);
        if (ContinueOwner != 0)
        {
            writer.Int("continueOwner", ContinueOwner);
            writer.Fixed("continueX", ContinueX);
            writer.Fixed("continueY", ContinueY);
        }
        writer.EndObject();
    }

    public static AcquisitionState Load(IStateReader reader)
    {
        var state = new AcquisitionState { Victim = new EntityId(checked((uint)reader.UInt("victim"))), LeechRange = reader.Has("leechRange") && reader.Bool("leechRange") };
        if (reader.Has("ordered"))
        {
            state.Ordered = reader.Bool("ordered");
            state.OrderedTick = reader.Int("orderedTick");
        }
        if (reader.Has("fireOrdered"))
        {
            state.FireOrdered = reader.Bool("fireOrdered");
            state.OrderOrigin = (FireOrigin)checked((byte)reader.Int("orderOrigin"));
            state.ShotPending = reader.Has("shotPending") && reader.Bool("shotPending");
            state.ShotsLeft = reader.Has("shotsLeft") ? checked((int)reader.Int("shotsLeft")) : 0;
            if (state.ShotsLeft < 0) throw new InvalidDataException("Negative shot limit in snapshot.");
            state.LockedSlot = reader.Has("lockedSlot") ? checked((int)reader.Int("lockedSlot")) : -1;
            if (state.LockedSlot < -1 || state.LockedSlot >= WeaponSlot.Count || (state.LockedSlot >= 0 && state.ShotsLeft == 0))
                throw new InvalidDataException("The locked weapon of an attack out of range in snapshot.");
            if (!Enum.IsDefined(state.OrderOrigin)) throw new InvalidDataException("Origin of a fire order out of range in snapshot.");
        }
        if (reader.Has("fireCycle")) state.Cycle = FireCycle.Load(reader.Object("fireCycle"));
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
        state.Forced = reader.Has("forced") && reader.Bool("forced");
        state.Neutral = reader.Has("neutral") && reader.Bool("neutral");
        if (reader.Has("continueOwner"))
        {
            state.ContinueOwner = checked((int)reader.Int("continueOwner"));
            state.ContinueX = reader.Fixed("continueX");
            state.ContinueY = reader.Fixed("continueY");
        }
        if (state.Neutral && !state.Forced) throw new InvalidDataException("Only a forced attack may be on a neutral object.");
        if (state.IsDefault) throw new InvalidDataException("An acquisition state without a victim, an attack-move or a pending look is never written.");
        if (state.Forced && (!state.Victim.IsValid || state.Ordered)) throw new InvalidDataException("A slave's forced attack needs a victim and no order.");
        if ((state.Ordered || state.Chasing) && !state.Victim.IsValid) throw new InvalidDataException("An ordered attack or a chase needs a victim.");
        if ((state.Cycle is not null || state.FireOrdered) && !state.Victim.IsValid) throw new InvalidDataException("An attack machine or a fire order needs a victim.");
        if (state.FireOrdered && !state.Ordered) throw new InvalidDataException("A fire order is an ordered attack.");
        if (state.OrderedTick < 0 || state.ChaseTick < 0) throw new InvalidDataException("Negative acquisition tick in snapshot.");
        return state;
    }
}

/// <summary>Every mutable field of an <see cref="AcquisitionState"/> (or none), compared before and after a step to learn whether the entity's serialized state changed.</summary>
internal readonly record struct AcquisitionStamp(bool Present, EntityId Victim, bool Ordered, long OrderedTick, bool AttackMove, Fixed GoalX, Fixed GoalY, bool Halted, bool Chasing, long ChaseTick, byte Mode, long NextCheck, bool Offset, bool Forced, bool Neutral,
    int ContinueOwner, Fixed ContinueX, Fixed ContinueY, FireCycleStamp Cycle, bool FireOrdered, FireOrigin OrderOrigin, bool ShotPending, int ShotsLeft,
    int LockedSlot)
{
    public static AcquisitionStamp Of(AcquisitionState? state)
        => state is null ? default : new(true, state.Victim, state.Ordered, state.OrderedTick, state.AttackMove, state.GoalX, state.GoalY, state.Halted, state.Chasing, state.ChaseTick, state.Mode, state.NextCheck, state.Offset, state.Forced, state.Neutral,
            state.ContinueOwner, state.ContinueX, state.ContinueY, FireCycleStamp.Of(state.Cycle), state.FireOrdered, state.OrderOrigin, state.ShotPending, state.ShotsLeft,
            state.LockedSlot);
}

/// <summary>
/// What the host of a world may add to the units' own target choice: the attack priorities and moods that map scripts set (<c>AttackPriorityInfo</c>, <c>AttitudeType</c>). A world without
/// one treats every unit as <see cref="Attitude.Normal"/> without a priority set.
/// </summary>
public interface IAcquisitionAdvisor
{
    /// <summary><c>AttackPriorityInfo::getPriority</c> for the set <paramref name="attacker"/> was given; false when it has none (<c>getDefaultAttackInfo</c>: the closest enemy wins).</summary>
    bool TryAttackPriority(Entity attacker, Entity target, out int priority);

    /// <summary><c>TeamTemplateInfo::m_initialTeamAttitude</c> of the unit's team: its attitude unless it has an own one (<see cref="Entity.OwnAttitude"/>).</summary>
    Attitude AttitudeOf(Entity entity);

    /// <summary>
    /// <c>getNextMoodTarget</c>, "check if team auto targets same victim": the victim the unit's team attacks together (<c>Team::getTeamTargetObject</c>), when the team does that and
    /// the unit's mood allows it; <see langword="null"/> otherwise. The world still decides whether the unit can attack it.
    /// </summary>
    Entity? CommonTargetOf(Entity entity) => null;

    /// <summary><c>AIAttackFireWeaponState::onEnter</c>: the unit fires at a victim of its own choice; a team that attacks together and has no victim yet takes this one.</summary>
    void NoteFiring(Entity attacker, Entity victim) { }
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

using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// The units' own target acquisition and the attack-move (docs/SIMULATION-CORE.md §33). The reference lets every object with an AI module look for a victim by itself
/// (<c>AIUpdateInterface::getNextMoodTarget</c>): while it idles (<c>AIIdleState::update</c>), through its idle turret while the body does something else
/// (<c>TurretAI::friend_checkForIdleMoodTarget</c>) and on an attack-move (<c>AIAttackMoveToState::update</c>), which stops for the fight and marches on afterwards. Here that is one
/// pass per tick over the objects that can acquire, in id order, after the objects have moved; a unit that has a victim fires its best weapon at it whenever it may, exactly like an
/// accepted <see cref="FireWeaponCommand"/> of origin <see cref="FireOrigin.Ai"/> (turret, pre-attack delay, clip and reload included).
/// </summary>
public sealed partial class SimulationWorld
{
    // AIData.ini is not a typed definition yet; these are the values of the shipped file (the reference's defaults in AI.cpp differ in places) and are named in the document as constants.

    /// <summary><c>GuardOuterModifierAI</c> (AIData.ini 1.333): how far beyond its vision a computer player's unit looks for a victim it then walks to.</summary>
    private static readonly Fixed AcquireVisionFactorComputer = Fixed.FromDecimal(1.333m);

    /// <summary><c>AlertRangeModifier</c> (AIData.ini 1.1).</summary>
    private static readonly Fixed AlertRangeFactor = Fixed.FromDecimal(1.1m);

    /// <summary><c>AggressiveRangeModifier</c> (AIData.ini 1.5).</summary>
    private static readonly Fixed AggressiveRangeFactor = Fixed.FromDecimal(1.5m);

    /// <summary><c>AttackPriorityDistanceModifier</c> (AIData.ini 100): the distance that lowers a victim's attack priority by one.</summary>
    private static readonly Fixed AttackPriorityDistanceStep = Fixed.FromInt(100);

    /// <summary>
    /// An attack named by fire or pursue orders is over when no such order arrived for this long (logic frames of the original, 1.5 s) after the unit could fire again (a command
    /// giver only orders a shot the weapon can take). An own value of this simulation.
    /// </summary>
    private const long OrderedAttackGraceFrames = 45;

    /// <summary>
    /// <c>ForceIdleMSEC</c> (AIData.ini 67 ms, "2 frames"; <c>AI.cpp</c> field table → <c>m_forceIdleFramesCount</c>, used by <c>AIUpdateInterface::resetNextMoodCheckTime</c>):
    /// how long a unit has been in a state that looks for victims before its first look, in logic frames of the original.
    /// </summary>
    private const long ForceIdleFrames = 2;

    /// <summary>A chase is pointed at the victim's current position this often (logic frames of the original, 0.5 s). An own rhythm of this simulation.</summary>
    private const long ChaseRefreshFrames = 15;

    // The three above in ticks of this world, resolved once when the world is created.
    private readonly long _orderedAttackGraceTicks;
    private readonly long _forceIdleTicks;
    private readonly long _chaseRefreshTicks;

    /// <summary>A chased victim that is further away than this many acquisition radii has escaped.</summary>
    private const int ChaseLeashFactor = 2;

    private static readonly int SoldStatus = ObjectStatusBits.Index("SOLD");

    /// <summary>Ids (ascending) of the objects that take part: every object whose AI may acquire while idle, plus the ones with an attack state of their own (<see cref="Entity.Acquisition"/>).</summary>
    private readonly SortedSet<uint> _acquirers = [];

    private readonly List<uint> _acquirerScratch = [];

    /// <summary>The script engine of a mission (attack priorities, moods); null for a world without one.</summary>
    public IAcquisitionAdvisor? AcquisitionAdvisor { get; set; }

    /// <summary>
    /// False switches the units' own choice of victims off: nothing fires unless a command says so, and an attack-move only marches. A switch of the host (scripted tests), not part of
    /// the snapshot; the default is on.
    /// </summary>
    public bool SelfAcquisition { get; set; } = true;

    /// <summary>The victim the unit attacks right now, by its own choice or by order (<c>AIUpdateInterface::getCurrentVictim</c>); invalid for none.</summary>
    public EntityId AcquiredVictimOf(Entity entity) => entity.Acquisition?.Victim ?? EntityId.Invalid;

    /// <summary>The unit is on an attack-move (<c>AI_ATTACK_MOVE_TO</c>): marching, or stopped on the way for a fight.</summary>
    public bool IsAttackMoving(Entity entity) => entity.Acquisition is { AttackMove: true };

    /// <summary>The goal of the unit's attack-move, if it is on one.</summary>
    public (Fixed X, Fixed Y)? AttackMoveGoalOf(Entity entity) => entity.Acquisition is { AttackMove: true } state ? (state.GoalX, state.GoalY) : null;

    private void RegisterAcquirer(Entity entity)
    {
        if (AiOf(entity) is { } ai && ((ai.AutoAcquire & AutoAcquire.Idle) != 0 || entity.Acquisition is not null || entity.Guard is not null)) _acquirers.Add(entity.Id.Value);
    }

    public void BeginAttackMove(Entity entity, Fixed x, Fixed y)
    {
        entity.Acquisition = new AcquisitionState { AttackMove = true, GoalX = x, GoalY = y };
        _acquirers.Add(entity.Id.Value);
        MarkDirty(entity);
    }

    /// <summary>A new AI command ends whatever the unit was attacking and an attack-move (<c>AIAttackState::onExit</c>, <c>AIAttackMoveToState::onExit</c>).</summary>
    private void ClearAcquisition(Entity entity)
    {
        if (entity.Acquisition is null) return;
        entity.Acquisition = null;
        MarkDirty(entity);
    }

    /// <summary>
    /// An accepted fire or pursue order of the unit's command giver names its victim (<c>aiAttackObject</c>): the unit is attacking by order, so it does not pick victims by itself
    /// until the orders stop.
    /// </summary>
    /// <summary>
    /// <c>AIGroup::groupAttackObjectPrivate</c>: "a garrisoned container capable of firing" - one with riders whose container lets them fire (<c>isPassengerAllowedToFire()</c>) - passes an
    /// attack order on to its riders (F-60, step 6).
    /// </summary>
    public bool PassesAttackToRiders(Entity unit) => unit.Container is { Contained.Count: > 0 } container && container.IsPassengerAllowedToFire(this, null);

    /// <summary>For the client's cursor: some rider of <paramref name="container"/> could attack <paramref name="victim"/>, and <paramref name="inRange"/> whether one reaches it from where it is.</summary>
    public bool RidersCanAttack(Entity container, Entity victim, out bool inRange)
    {
        inRange = false;
        if (!PassesAttackToRiders(container)) return false;
        var can = false;
        foreach (var id in container.Container!.Contained)
        {
            if (FindEntity(id) is not { EffectivelyDead: false } rider || !ContainerLetsFire(rider) || !CanAttack(rider, victim, false, out var reaches)) continue;
            can = true;
            inRange |= reaches;
        }
        return can;
    }

    /// <summary>
    /// The riders' half of <c>groupAttackObjectPrivate</c>: every rider whose <c>getAbleToAttackSpecificObject</c> is possible (here or after moving) gets <c>aiAttackObject</c> on the
    /// victim. A rider cannot move, so its attack is the engagement of its own look with this victim: it fires while the victim is in reach of its fire point and ends the attack
    /// when it is not, as the original's attack state fails for a rider that cannot approach.
    /// </summary>
    private void PassAttackToRiders(Entity container, Entity victim)
    {
        foreach (var id in container.Container!.Contained)
        {
            if (FindEntity(id) is not { EffectivelyDead: false, DestroyRequested: false } rider || AiOf(rider) is not { } ai) continue;
            if (!ContainerLetsFire(rider) || !IsAttackable(rider, victim) || !CanAttack(rider, victim, false, out _)) continue;
            EndGuard(rider);
            var state = rider.Acquisition ??= new AcquisitionState();
            if (state.Chasing) ai.StopInternalMove();
            state.Victim = victim.Id;
            state.Ordered = false;
            state.AttackMove = false;
            state.Chasing = false;
            state.ChaseTick = 0;
            AimTurretsForAttack(rider, victim, TurretAimOrder.Attack);
            _acquirers.Add(rider.Id.Value);
            MarkDirty(rider);
        }
    }

    private void NoteOrderedAttack(Entity unit, Entity victim)
    {
        if (AiOf(unit) is not { } ai) return;
        EndGuard(unit);
        var state = unit.Acquisition ??= new AcquisitionState();
        if (state.Chasing) ai.StopInternalMove();
        state.Victim = victim.Id;
        state.Ordered = true;
        state.OrderedTick = _tick;
        state.Chasing = false;
        state.ChaseTick = 0;
        _acquirers.Add(unit.Id.Value);
        MarkDirty(unit);
    }

    // ------------------------------------------------------------------------------------------------------ the per-tick step

    private void StepAcquisition(long tick)
    {
        if (_acquirers.Count == 0) return;
        // A shot may kill, and a death may create objects: walk a copy.
        _acquirerScratch.Clear();
        _acquirerScratch.AddRange(_acquirers);
        foreach (var id in _acquirerScratch)
        {
            if (!_entities.TryGetValue(id, out var entity) || entity.DestroyRequested || entity.EffectivelyDead || AiOf(entity) is not { } ai)
            {
                _acquirers.Remove(id);
                continue;
            }

            // A unit decides in the ticks its logic frames of the original begin with (docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md §2); it reads everything it looks at where
            // that frame ends, so the ticks in between have nothing to decide that the 30 Hz step decides. At 30 Hz every tick begins a frame.
            if (_firstFrameTicks != TickOrder.Next && (tick - entity.SpawnTick) % _firstFrameTicks != 0) continue;
            var before = AcquisitionStamp.Of(entity.Acquisition);
            var guardBefore = GuardStamp.Of(entity.Guard);
            // A guarding unit's attacks are its guard's (AI_GUARD replaces the idle state the unit's own acquisition belongs to).
            if (entity.Guard is { } guard)
            {
                if (MayAct(entity, tick) && TurretsLetAttack(entity)) StepGuard(entity, ai, guard, tick);
                else if (entity.Acquisition is { Victim.IsValid: true } held) EndEngagement(entity, ai, held);
            }
            else StepAcquirer(entity, ai, tick);
            if (entity.Acquisition is { IsDefault: true }) entity.Acquisition = null;
            if (AcquisitionStamp.Of(entity.Acquisition) != before || GuardStamp.Of(entity.Guard) != guardBefore) MarkDirty(entity);
            if (entity.Acquisition is null && entity.Guard is null && (ai.AutoAcquire & AutoAcquire.Idle) == 0) _acquirers.Remove(id);
        }
    }

    private enum AcquireMode : byte { None = 0, Idle = 1, Turret = 2, AttackMove = 3 }

    private void StepAcquirer(Entity entity, AIUpdateInterface ai, long tick)
    {
        var state = entity.Acquisition;
        // Object::isAbleToAttack: weapons on a switched-off turret acquire nobody, and an attack the unit runs itself ends (cannotPossiblyAttackObject).
        if (!MayAct(entity, tick) || !TurretsLetAttack(entity))
        {
            if (state is { Victim.IsValid: true, Ordered: false }) EndEngagement(entity, ai, state);
            return;
        }

        UpdateWeaponSet(entity);
        var force = false;
        if (state is { Victim.IsValid: true })
        {
            var victim = FindEntity(state.Victim);
            var attackable = victim is not null && IsAttackable(entity, victim);
            if (state.Ordered)
            {
                // The command giver fires; the world only notices when its attack is over.
                // "No order for longer than the grace" is noticed in the step after the grace: a logic frame of the original at every rate, not a tick.
                if (attackable && tick - Math.Max(state.OrderedTick, EarliestReadyTick(entity, tick)) < _orderedAttackGraceTicks + _turretFrameTicks) return;
                state.ClearVictim();
            }
            else
            {
                if (attackable && Engage(entity, ai, state, victim!, ModeOf(entity, ai, state), tick)) return;
                EndEngagement(entity, ai, state);
                // AIAttackMoveToState::update (forceRetargetThisFrame): on an attack-move the next victim is looked for at once. An idle unit or turret enters its idle state again,
                // whose look is due two frames later.
                if (state.AttackMove) force = true;
                else if (state.Mode != 0) (state.NextCheck, state.Offset) = (tick + _forceIdleTicks, true);
            }
        }

        var mode = SelfAcquisition ? ModeOf(entity, ai, state) : AcquireMode.None;
        if (mode == AcquireMode.None)
        {
            if (state is not null) (state.Mode, state.NextCheck, state.Offset) = (0, 0, false);
        }
        else
        {
            state ??= entity.Acquisition = new AcquisitionState();
            if (state.Mode != (byte)mode)
            {
                // resetNextMoodCheckTime: a state that looks for victims was entered.
                state.Mode = (byte)mode;
                state.NextCheck = tick + _forceIdleTicks;
                state.Offset = true;
            }

            // getNextMoodTarget: the common victim of the unit's team comes before the unit's own look and does not wait for the look to be due.
            if (AcquisitionAdvisor is { } teams && teams.CommonTargetOf(entity) is { } common && IsAttackable(entity, common)
                && CanAttack(entity, common, mode == AcquireMode.Turret, out var commonInRange)
                && (commonInRange || (mode == AcquireMode.AttackMove && MayChase(entity, entity.Template.IsKindOf("AIRCRAFT")))))
            {
                state.Victim = common.Id;
                AimTurretsForAttack(entity, common, TurretAimOrder.Attack);
                if (Engage(entity, ai, state, common, mode, tick)) return;
                EndEngagement(entity, ai, state);
            }

            if (force || tick >= state.NextCheck)
            {
                if (!force)
                {
                    // getNextMoodTarget: the next look is one check rate away, the first one after a reset shifted by up to half a rate.
                    var rate = ai.MoodCheckTicks;
                    state.NextCheck = tick + rate + (state.Offset ? MoodCheckOffset(entity, tick, rate) : 0);
                    state.Offset = false;
                }

                if (FindMoodTarget(entity, ai, mode) is { } found)
                {
                    state.Victim = found.Id;
                    AimTurretsForAttack(entity, found, TurretAimOrder.Attack);
                    if (Engage(entity, ai, state, found, mode, tick)) return;
                    EndEngagement(entity, ai, state);
                }
            }
        }

        if (state is not { AttackMove: true }) return;
        if (state.Halted)
        {
            // The fight is over: AIAttackMoveToState repaths and marches on.
            state.Halted = false;
            if (ai.StartInternalMove(state.GoalX, state.GoalY))
            {
                ai.BeginMoveWithNextFrame(this);
                WakeAt(entity, ai, Math.Min(ai.NextWake, tick + _turretFrameTicks));
            }
            else state.AttackMove = false;
        }
        else if (!entity.HasMoveTarget) state.AttackMove = false;
    }

    /// <summary>
    /// Objects that cannot act do not acquire: under construction, sold, disabled (EMP, held by a script) or inside a container that does not let them fire (<c>Object::isAbleToAttack</c>,
    /// F-60). The container's own hold (<c>DISABLED_HELD</c> of every rider) is no stop: the original's AI processes <c>DISABLED_HELD</c>
    /// (<c>AIUpdateInterface::getDisabledTypesToProcess</c>), so a held rider still looks for victims and fires.
    /// </summary>
    private bool MayAct(Entity entity, long tick)
        => !entity.HasStatus(ObjectStatusBits.UnderConstruction) && !entity.HasStatus(SoldStatus) && entity.EmpDisabledUntil <= tick
           && (entity.ScriptDisableMask & Entity.ScriptHeld) == 0 && (!entity.ContainedBy.IsValid || ContainerLetsFire(entity));

    /// <summary>
    /// The container of a contained rider, null for an object in no container. Where a rider is measured from is <see cref="ShooterPosition"/>.
    /// </summary>
    private Entity? FiringContainerOf(Entity entity) => entity.ContainedBy.IsValid ? FindEntity(entity.ContainedBy) : null;

    /// <summary>Where a contained rider's shot leaves from (<c>Weapon::calcProjectileLaunchPosition</c> of an enclosed launcher): its <see cref="ShooterPosition"/> where its begun frame ends.</summary>
    private (Fixed X, Fixed Y, Fixed Z) FiringPointOf(Entity rider) => ShooterPosition(rider, _firstFrameTicks != TickOrder.Next);

    /// <summary>
    /// <c>Object::isAbleToAttack</c>'s gate for a contained object (F-60, docs/FIRE-FROM-CONTAINERS.md): the container lets this rider fire
    /// (<see cref="IContainerModule.IsPassengerAllowedToFire"/>). False for an object in no container.
    /// </summary>
    public bool ContainerLetsFire(Entity rider)
        => rider.ContainedBy.IsValid && FindEntity(rider.ContainedBy)?.Container is { } container && container.IsPassengerAllowedToFire(this, rider);

    /// <summary>
    /// Which of the reference's three callers of <c>getNextMoodTarget</c> applies: the attack-move, the idle state, or the idle turret of a unit whose body is busy. The idle callers need
    /// <c>AutoAcquireEnemiesWhenIdle = Yes</c> and, for a stealthed unit, the <c>Stealthed</c> bit ("Sorry, stealthed and not allowed to idle fire when stealthed").
    /// </summary>
    private AcquireMode ModeOf(Entity entity, AIUpdateInterface ai, AcquisitionState? state)
    {
        if (state is { AttackMove: true }) return AcquireMode.AttackMove;
        if ((ai.AutoAcquire & AutoAcquire.Idle) == 0) return AcquireMode.None;
        // "Being in a firing container is an exception to this veto" (getNextMoodTarget asks the container without an id).
        if (entity.HasStatus(StealthedStatus) && (ai.AutoAcquire & AutoAcquire.Stealthed) == 0
            && !(FiringContainerOf(entity)?.Container is { } container && container.IsPassengerAllowedToFire(this, null))) return AcquireMode.None;
        if (ai.IsIdle) return AcquireMode.Idle;
        return HasAimingTurret(entity) ? AcquireMode.Turret : AcquireMode.None;
    }

    /// <summary>
    /// <c>GameLogicRandomValue(-halfRate, halfRate)</c> of <c>getNextMoodTarget</c>. The reference draws from the shared logic stream; like the turrets' idle scan this hashes (world seed,
    /// entity, tick) instead, so the same inputs give the same value, replays and snapshot continuations agree and no other user of the logic stream is shifted.
    /// <para>
    /// The value is drawn in logic frames of the original (half the check rate in frames, as the reference draws it) and only then scaled to ticks, so the shift stays on the
    /// original's grid at every tick rate. The hash is seeded with the number of the logic frame the tick lies in, not with the tick: a unit that resets its mood check at the same
    /// game time draws the same shift at every rate (at 30 Hz the frame is the tick).
    /// </para>
    /// </summary>
    private long MoodCheckOffset(Entity entity, long tick, long rate)
    {
        var half = Time.ToOriginalFrames(rate) >> 1;
        if (half <= 0) return 0;
        unchecked
        {
            // frame + 1 keeps the hash input away from zero in the first frame: an offset of the hash, neither a time nor an order.
            var z = (((ulong)Streams.Seed << 32) | entity.Id.Value) + 0x9E3779B97F4A7C15UL * (ulong)(Time.FramesIn(tick) + 1);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return Time.OriginalFrames((long)(z % (ulong)(2 * half + 1)) - half);
        }
    }

    /// <summary>The victim is still one the unit may attack: alive, an enemy and visible to the unit's player (<c>getAbleToAttackSpecificObject(ATTACK_CONTINUED_TARGET, …)</c>).</summary>
    private bool IsAttackable(Entity entity, Entity victim)
        => !victim.DestroyRequested && !victim.EffectivelyDead && victim.Body is not null && !victim.ContainedBy.IsValid && AreEnemies(entity, victim)
           && !(victim.HasStatus(StealthedStatus) && !victim.HasStatus(DetectedStatus)) && CanPlayerSee(entity.Owner, victim);

    /// <summary>
    /// One tick of an attack on <paramref name="victim"/>: in range the unit stands (an attack-move stops for the fight, aircraft keep flying) and fires when its weapon may; out of range a
    /// computer player's unit on an attack-move walks to the victim (<c>AIAttackApproachTargetState</c>), everything else gives the victim up — a human player's unit that picked its
    /// victim itself may not chase it (<c>isAllowedToChase</c>), and an idle turret drops a victim that left its range (<c>m_targetWasSetByIdleMood</c>). False when the attack is over.
    /// </summary>
    private bool Engage(Entity entity, AIUpdateInterface ai, AcquisitionState state, Entity victim, AcquireMode mode, long tick)
    {
        var turretOnly = mode == AcquireMode.Turret;
        if (!CanAttack(entity, victim, turretOnly, out var inRange)) return false;
        var aircraft = entity.Template.IsKindOf("AIRCRAFT");
        if (inRange)
        {
            // The unit stops where the 30 Hz unit stops: after the whole step of the frame in which it came into range (nothing to do at 30 Hz).
            if (state.Chasing)
            {
                ai.StopInternalMoveAtFrameEnd(this);
                state.Chasing = false;
                state.ChaseTick = 0;
            }

            if (state.AttackMove && !state.Halted && !aircraft)
            {
                ai.StopInternalMoveAtFrameEnd(this);
                state.Halted = true;
            }

            // attemptBestFirePointPosition: a rider of an enclosing garrison steps to the fire point it was found in range from (TryRangeOrigin).
            PlaceAtFirePoint(entity, victim);
            AcquisitionAdvisor?.NoteFiring(entity, victim);
            AutoFire(entity, victim, turretOnly);
            return true;
        }

        if (mode != AcquireMode.AttackMove || !MayChase(entity, aircraft)) return false;
        var leash = AcquireRadius(entity, true) * ChaseLeashFactor;
        // The chase is decided after the updates of the tick: at 30 Hz both objects have taken their whole step by then. Above 30 Hz they are read where the frame they have
        // begun ends (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3, rule 3), and the walk starts one logic frame of the original later, as "the next tick" does at 30 Hz.
        var (victimX, victimY, _) = victim.FrameEnd(tick);
        var (ownX, ownY, _) = entity.FrameEnd(tick);
        var dx = victimX - ownX;
        var dy = victimY - ownY;
        if (!SquaredReach.Of(leash).Contains(dx * dx + dy * dy)) return false;
        if (!state.Chasing || tick - state.ChaseTick >= _chaseRefreshTicks || !entity.HasMoveTarget)
        {
            // A unit that needs a line of sight walks to where it can fire (findAttackPath, F-55), the others at the victim.
            var (goalX, goalY) = FindAttackPosition(entity, victim) ?? (victimX, victimY);
            if (!ai.StartInternalMove(goalX, goalY)) return false;
            ai.BeginMoveWithNextFrame(this);
            WakeAt(entity, ai, Math.Min(ai.NextWake, tick + _turretFrameTicks));
            state.Chasing = true;
            state.ChaseTick = tick;
            state.Halted = true;
            AimTurretsForAttack(entity, victim, TurretAimOrder.Pursuit);
        }

        return true;
    }

    private void EndEngagement(Entity entity, AIUpdateInterface ai, AcquisitionState state)
    {
        EndPreAttack(entity);
        StopTurrets(entity);
        if (state.Chasing) ai.StopInternalMoveAtFrameEnd(this);
        state.ClearVictim();
    }

    /// <summary>
    /// Only a computer player's ground unit walks to a victim it picked itself; a human player's unit stays where its player put it, and a rider never walks (it is held and not
    /// mobile, <c>Object::isMobile</c>; its attack state fails out of range, <c>AIAttackAimAtTargetState::update</c>).
    /// </summary>
    private bool MayChase(Entity entity, bool aircraft) => !aircraft && entity.Template.Locomotor is not null && IsComputerPlayer(entity.Owner) && !entity.ContainedBy.IsValid;

    /// <summary>
    /// <c>AI::getAdjustedVisionRangeForObject(obj, OWNERTYPE | MOOD)</c> for a unit that may walk to its victim: the vision range times the guard-outer factor, times the mood factor of a
    /// computer player's alert or aggressive team; never less than the reach of the unit's weapons. A unit that may not walk looks no further than its weapons reach
    /// (<c>AI::WITHIN_ATTACK_RANGE</c>).
    /// </summary>
    private Fixed AcquireRadius(Entity entity, bool chase)
    {
        var reach = MaxWeaponRange(entity);
        // AI::getAdjustedVisionRangeForObject gives a contained object its largest weapon range (with its bonuses), and getNextMoodTarget adds the container's bounding circle
        // "so that large buildings can auto acquire everything in outer ranges".
        if (FiringContainerOf(entity) is { } container) return reach + container.Template.BoundingCircleRadius;
        if (!chase) return reach;
        var vision = entity.Template.VisionRange * AcquireVisionFactorComputer;
        switch (AcquisitionAdvisor?.AttitudeOf(entity) ?? Attitude.Normal)
        {
            case Attitude.Alert: vision *= AlertRangeFactor; break;
            case Attitude.Aggressive: vision *= AggressiveRangeFactor; break;
        }

        return Fixed.Max(reach, vision);
    }

    /// <summary>The first tick from <paramref name="tick"/> on at which some weapon of the active set is ready, not looking further than the turrets' reload cap (an aircraft out of ammunition waits for its airfield).</summary>
    private long EarliestReadyTick(Entity entity, long tick)
    {
        if (ActiveWeaponSet(entity) is not { } set) return tick;
        var earliest = long.MaxValue;
        for (var slot = 0; slot < WeaponSlot.Count; slot++)
            if (set.HasWeapon(slot)) earliest = Math.Min(earliest, EffectiveReadyTick(entity, slot, set));
        return Math.Clamp(earliest, tick - _orderedAttackGraceTicks - _turretFrameTicks, tick + _turretReloadHoldCapTicks);
    }

    // ------------------------------------------------------------------------------------------------------ choosing the victim

    /// <summary>
    /// <c>AIUpdateInterface::getNextMoodTarget</c> with <c>AI::findClosestEnemy</c>: the closest enemy the unit can attack — or, for a unit with a script's attack priority set, the one
    /// with the highest priority after one point is taken off per <c>AttackPriorityDistanceModifier</c> of distance (priority 0 is never attacked). Ties go to the lower id.
    /// </summary>
    private Entity? FindMoodTarget(Entity entity, AIUpdateInterface ai, AcquireMode mode)
    {
        var computer = IsComputerPlayer(entity.Owner);
        var container = FiringContainerOf(entity);
        // "Human units don't have a mood": only a computer player's unit sleeps or stays passive. A passive unit of the reference answers its last attacker; that is not modeled.
        // A contained unit does not sleep: getAdjustedVisionRangeForObject gives it its weapon range instead of the mood's zero. Passive is getNextMoodTarget's own test and holds inside too.
        var attitude = computer ? AcquisitionAdvisor?.AttitudeOf(entity) ?? Attitude.Normal : Attitude.Normal;
        if ((attitude == Attitude.Sleep && container is null) || attitude == Attitude.Passive) return null;
        var aircraft = entity.Template.IsKindOf("AIRCRAFT");
        var chase = mode == AcquireMode.AttackMove && MayChase(entity, aircraft);
        var radius = AcquireRadius(entity, chase);
        if (radius <= Fixed.Zero) return null;
        var limit = SquaredReach.Of(radius);
        var turretOnly = mode == AcquireMode.Turret;
        var buildings = (ai.AutoAcquire & AutoAcquire.AttackBuildings) != 0;
        // A rider looks from where it stands (ShooterPosition); its container's bounding circle is in the radius.
        // Above 30 Hz the look measures from where the unit's begun frame ends to where each candidate's ends (rule 3 of the frame convention).
        var frameEnd = _firstFrameTicks != TickOrder.Next;
        var (ownX, ownY, _) = ShooterPosition(entity, frameEnd);
        var search = new MoodSearch { Limit = limit, TurretOnly = turretOnly, Chase = chase, Buildings = buildings, Computer = computer, BestDistance = Fixed.MaxValue, OwnX = ownX, OwnY = ownY, FrameEnd = frameEnd };

        if (RelationshipOverride is not null)
        {
            // A script set a relation between teams: whether two objects are enemies is then a question per pair of objects, so every object is a candidate.
            foreach (var candidate in _entities.Values) ConsiderMoodTarget(entity, candidate, ref search);
            return search.Best;
        }

        // Without such an override a victim must belong to a player the unit's player has as an enemy (IsAttackable -> AreEnemies), so only those players'
        // objects are looked at. The pick is the maximum of one strict order over the candidates - (effective priority, priority, nearer, lower id) for a
        // unit with a priority set, (nearer, lower id) otherwise, and which of the two applies depends on the unit alone -, so the order in which the
        // candidates come does not matter. The system player and unknown owners are nobody's enemy (RelationshipOf).
        foreach (var player in Configuration.Players)
        {
            if (!AreEnemies(entity.Owner, player.Id)) continue;
            foreach (var candidate in EntitiesOwnedBy(player.Id).AsSpan()) ConsiderMoodTarget(entity, candidate, ref search);
        }

        return search.Best;
    }

    private struct MoodSearch
    {
        public SquaredReach Limit;
        public bool TurretOnly;
        public bool Chase;
        public bool Buildings;
        public bool Computer;
        public Entity? Best;
        public Fixed BestDistance;
        public int BestEffective;
        public int BestPriority;
        public Fixed OwnX;
        public Fixed OwnY;
        public bool FrameEnd;
    }

    private void ConsiderMoodTarget(Entity entity, Entity candidate, ref MoodSearch search)
    {
        if (candidate.Owner == entity.Owner || candidate.Owner.IsSystem) return;
        var dx = candidate.X - search.OwnX;
        var dy = candidate.Y - search.OwnY;
        if (search.FrameEnd && candidate.IsInsideFrameStep)
        {
            var (x, y, _) = candidate.FrameEnd(_tick);
            dx = x - search.OwnX;
            dy = y - search.OwnY;
        }
        var distance = dx * dx + dy * dy;
        if (!search.Limit.Contains(distance)) return;
        if (candidate.Template.IsKindOf("PROJECTILE") || candidate.Template.IsKindOf("INERT") || candidate.Template.IsKindOf("MINE")) return;
        if (!IsAttackable(entity, candidate) || !AcquiresStructure(entity, candidate, search.Buildings, search.Computer) || IsHiddenStealthGarrison(entity, candidate)) return;
        if (AcquisitionViewBlocked(entity, candidate)) return;
        if (!CanAttack(entity, candidate, search.TurretOnly, out var inRange) || (!inRange && !search.Chase)) return;

        if (AcquisitionAdvisor is { } advisor && advisor.TryAttackPriority(entity, candidate, out var priority))
        {
            if (priority == 0) return;
            var effective = (int)Math.Max(1, priority - (Fixed.Sqrt(distance) / AttackPriorityDistanceStep).Floor());
            var better = effective > search.BestEffective || (effective == search.BestEffective && priority > search.BestPriority)
                         || (effective == search.BestEffective && priority == search.BestPriority && IsNearer(candidate, distance, search.Best, search.BestDistance));
            if (!better) return;
            search.BestEffective = effective;
            search.BestPriority = priority;
        }
        else if (!IsNearer(candidate, distance, search.Best, search.BestDistance)) return;

        search.Best = candidate;
        search.BestDistance = distance;
    }

    private static bool IsNearer(Entity candidate, Fixed distance, Entity? best, Fixed bestDistance)
        => best is null || distance < bestDistance || (distance == bestDistance && candidate.Id.Value < best.Id.Value);

    /// <summary>
    /// <c>PartitionFilterInsignificantBuildings</c> (<c>AttackIgnoreInsignificantBuildings = Yes</c> in AIData.ini) and <c>PartitionFilterRejectBuildings</c>: an empty building that is no
    /// faction building is ignored by everybody; a unit with the <c>ATTACK_BUILDINGS</c> bit takes every other one. Without it (<c>findClosestEnemy</c> adds the filter) the building's
    /// apparent owner (<see cref="Modules.IContainerModule.ApparentOwnerFor"/>, F-54) must be the unit's player's enemy; then a computer player's unit takes it, and everybody else only
    /// base defenses and occupied buildings.
    /// </summary>
    private bool AcquiresStructure(Entity entity, Entity candidate, bool buildings, bool computer)
    {
        if (!candidate.Template.IsKindOf("STRUCTURE")) return true;
        if (candidate.Container is { ContainCount: 0 } && !IsFactionStructure(candidate.Template)) return false;
        if (buildings) return true;
        var apparent = candidate.Container?.ApparentOwnerFor(this, entity.Owner) ?? candidate.Owner;
        if (RelationshipOf(entity.Owner, apparent) != Relationship.Enemies) return false;
        return computer || candidate.Template.IsKindOf("FS_BASE_DEFENSE") || candidate.Container is { ContainCount: > 0 };
    }

    /// <summary>
    /// <c>PartitionFilterStealthedAndUndetected</c>'s branch for a container (F-54): a container whose riders are all of kind <c>STEALTH_GARRISON</c>, the first of them not
    /// <c>DETECTED</c>, is passed by when its apparent owner is the looking unit's enemy (a faction garrison, or one whose owner from before is hostile). An empty container is not
    /// hidden (the original read the first of an empty list there - GeneralsGameCode guards it).
    /// </summary>
    private bool IsHiddenStealthGarrison(Entity entity, Entity candidate)
    {
        if (candidate.Container is not { ContainCount: > 0 } container || container.ApparentOwnerFor(this, entity.Owner) is not { } apparent) return false;
        if (container.StealthUnitsContained(this) != container.ContainCount) return false;
        if (FindEntity(container.Contained[0]) is not { } first || first.HasStatus(DetectedStatus)) return false;
        return RelationshipOf(entity.Owner, apparent) == Relationship.Enemies;
    }

    /// <summary><c>Object::isFactionStructure</c>: any of the <c>FS_*</c> kinds.</summary>
    private static bool IsFactionStructure(ObjectTemplate template)
    {
        foreach (var kind in template.KindOf)
            if (kind.StartsWith("FS_", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// <c>getAbleToAttackSpecificObject(ATTACK_NEW_TARGET, victim, CMD_FROM_AI)</c>, reduced to the weapons: some weapon the unit may choose by itself can target the victim and would hurt
    /// it (a pinned slot is the only choice), and <paramref name="inRange"/> says whether such a weapon reaches it from here. With <paramref name="turretOnly"/> only weapons on a turret
    /// count (the body is busy).
    /// </summary>
    private bool CanAttack(Entity source, Entity victim, bool turretOnly, out bool inRange)
    {
        inRange = false;
        var set = ActiveWeaponSet(source);
        if (set is null || set.WeaponCount == 0) return false;
        var victimMask = VictimAntiMask(victim);
        var slots = source.WeaponSlots;
        var pinned = slots is not null && slots.Lock != WeaponLockKind.None && slots.SetIndex == set.Index ? slots.Current : -1;
        var can = false;
        for (var slot = 0; slot < WeaponSlot.Count; slot++)
        {
            if (!set.HasWeapon(slot) || (pinned >= 0 && slot != pinned)) continue;
            // The same test as ChooseWeapon for CMD_FROM_AI.
            if (pinned < 0 && (set.AutoChoose[slot] & CommandSourceBits.FromAi) == 0) continue;
            if (turretOnly && !IsOnAimingTurret(source, slot)) continue;
            var weapon = _templates.Weapons[set.Weapons[slot]];
            if (!CanTarget(source, weapon, victim, victimMask)) continue;
            can = true;
            // outOfWeaponRangeObject: a blocked view counts as out of range (F-55).
            if (WithinWeaponRange(source, weapon, victim) && !AttackViewBlocks(source, weapon, victim)) inRange = true;
        }

        return can;
    }

    private static bool IsOnAimingTurret(Entity entity, int slot)
    {
        if (entity.Template.Turrets is not { } set) return false;
        for (var index = 0; index < TurretSet.MaxTurrets; index++)
            if (set[index] is { Gates: true } info && (set.Linked || info.Controls(slot))) return true;
        return false;
    }

    /// <summary>
    /// The shot of an attack the unit runs itself: what an accepted <see cref="FireWeaponCommand"/> of origin <see cref="FireOrigin.Ai"/> does (weapon choice, fire gate, turret alignment,
    /// pre-attack delay, clip and reload, the <c>WeaponFired</c> event), without a command.
    /// </summary>
    private void AutoFire(Entity source, Entity victim, bool turretOnly)
    {
        if (!TurretsLetAttack(source)) return;
        var choice = ChooseWeapon(source, victim, FireOrigin.Ai);
        if (choice.Rejection != 0 || (turretOnly && !IsOnAimingTurret(source, choice.Slot)) || !GateAllowsFire(source, victim)) return;
        if (!AimTurretsAt(source, choice.Slot, victim, TurretAimOrder.Attack))
        {
            NoteTurretWait();
            return;
        }

        if (!PreAttackDone(source, _templates.Weapons[ActiveWeaponSet(source)!.Weapons[choice.Slot]], choice.Slot, victim)) return;
        var weapon = ConsumeShot(source, choice.Slot, victim.Id);
        var damage = FireWeaponAt(source, weapon, victim, victim.X, victim.Y, victim.Z);
        NoteTurretShot(source, choice.Slot);
        Emit(SimEventKind.WeaponFired, source.Id, victim.Id, weapon.Name, damage.Raw, source.Owner);
    }
}

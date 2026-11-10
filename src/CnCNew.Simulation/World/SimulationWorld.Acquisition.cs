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

    /// <summary><c>GuardOuterModifierHuman</c> (AIData.ini 2.2): how far beyond its vision a human player's unit looks; what it finds must be in weapon range as well.</summary>
    private static readonly Fixed AcquireVisionFactorHuman = Fixed.FromDecimal(2.2m);

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

    /// <summary>
    /// <c>AIUpdateInterface::isIdle</c> as a script reads it (F-21): the AI's state machine stands in <c>AI_IDLE</c>. An object without an AI is idle. <see cref="AIUpdateInterface.IsIdle"/>
    /// asks only whether the body has an order, which is what the unit's own look needs; a unit in an attack, on an attack-move or at an ability (<c>AI_BUSY</c>) is in another state.
    /// </summary>
    public bool IsAiIdle(Entity entity)
        => AiOf(entity) is not { } ai
           || (ai.IsIdle && !ai.HidesIdle && entity.Acquisition is not ({ Victim.IsValid: true } or { AttackMove: true }) && !entity.HasStatus(UsingAbilityBit));

    /// <summary>
    /// The condition of <c>InGameUI::addIdleWorker</c> (<c>DozerPrimaryIdleState::update</c>, <c>Player::becomingTeamMember</c>): the object's dozer brain stands in its idle state - the AI is idle
    /// (<see cref="IsAiIdle"/>), so no building, repair, supply run, move or fight is under way - and the object is not dying. A worker whose supply brain is in charge
    /// (<c>ActAsSupplyTruckState</c>) has left the dozer's idle state (<c>onExit</c> removes it from the list), however idle its AI is between two legs of a run.
    /// </summary>
    public bool IsIdleDozer(Entity entity)
        => !entity.EffectivelyDead && !entity.DestroyRequested && AiOf(entity) is { } ai && ai is not IWorkerBrains { ActsAsSupplyTruck: true } && IsAiIdle(entity);

    /// <summary>The unit is on an attack-move (<c>AI_ATTACK_MOVE_TO</c>): marching, or stopped on the way for a fight.</summary>
    public bool IsAttackMoving(Entity entity) => entity.Acquisition is { AttackMove: true };

    /// <summary>The goal of the unit's attack-move, if it is on one.</summary>
    public (Fixed X, Fixed Y)? AttackMoveGoalOf(Entity entity) => entity.Acquisition is { AttackMove: true } state ? (state.GoalX, state.GoalY) : null;

    /// <summary>
    /// <c>AIUpdateInterface::getAttitude</c>: the object's own attitude, else the initial attitude of its team. An unknown value counts as normal, as in
    /// <c>getMoodMatrixValue</c>. Only a computer player's unit has a mood: ask <see cref="IsComputerPlayer"/> first.
    /// </summary>
    public Attitude AttitudeOf(Entity entity)
    {
        if (entity.OwnAttitude is not { } own) return AcquisitionAdvisor?.AttitudeOf(entity) ?? Attitude.Normal;
        return own is >= (int)Attitude.Sleep and <= (int)Attitude.Aggressive ? (Attitude)own : Attitude.Normal;
    }

    /// <summary><c>AIUpdateInterface::setAttitude</c>.</summary>
    public void SetAttitude(Entity entity, int attitude)
    {
        if (entity.OwnAttitude == attitude) return;
        entity.OwnAttitude = attitude;
        MarkDirty(entity);
    }

    /// <summary><c>Object::setTeam</c>: "now, adjust the attitude of the unit to its new team".</summary>
    public void ClearOwnAttitude(Entity entity)
    {
        if (entity.OwnAttitude is null) return;
        entity.OwnAttitude = null;
        MarkDirty(entity);
    }

    /// <summary><c>AIUpdateInterface::wakeUpAndAttemptToTarget</c>: an idle unit looks for a victim now, the look after it shifted by up to half a rate.</summary>
    public void WakeUpAndAttemptToTarget(Entity entity)
    {
        if (AiOf(entity) is not { IsIdle: true } ai || (ai.AutoAcquire & AutoAcquire.Idle) == 0) return;
        var state = entity.Acquisition ??= new AcquisitionState();
        (state.Mode, state.NextCheck, state.Offset) = ((byte)AcquireMode.Idle, _tick, true);
        _acquirers.Add(entity.Id.Value);
        MarkDirty(entity);
    }

    /// <inheritdoc cref="IWorldServices.WakeEnemiesThatSee"/>
    public void WakeEnemiesThatSee(Entity revealed)
    {
        // Each enemy player's objects in the original; who is woken first changes nothing, as a wake only sets the time of the next look.
        foreach (var other in Entities)
        {
            if (other.EffectivelyDead || other.DestroyRequested || RelationshipOf(other.Owner, revealed.Owner) != Relationship.Enemies) continue;
            var vision = other.Template.VisionRange;
            var (dx, dy) = (other.X - revealed.X, other.Y - revealed.Y);
            if (FurtherThan(other.X, revealed.X, vision) || FurtherThan(other.Y, revealed.Y, vision) || !SquaredReach.Of(vision).Contains(dx * dx + dy * dy)) continue;
            WakeUpAndAttemptToTarget(other);
        }
    }

    /// <inheritdoc cref="IWorldServices.TransferAttack"/>
    /// <inheritdoc cref="IWorldServices.TransferBombs"/>
    public void TransferBombs(EntityId from, EntityId to)
    {
        foreach (var entity in Entities)
        {
            if (!entity.Template.IsKindOf("MINE")) continue;
            foreach (var module in entity.ModuleArray)
                if (module is Modules.BuiltIn.StickyBombUpdate bomb && bomb.Target == from) bomb.Retarget(this, to);
        }
    }

    public void TransferAttack(EntityId from, EntityId to)
    {
        if (!from.IsValid || !to.IsValid) return;
        foreach (var other in Entities)
        {
            var changed = false;
            if (other.Acquisition is { } state && state.Victim == from)
            {
                state.Victim = to;
                // AIUpdateInterface::transferAttack hands the attack on: the machine goes on with the new victim.
                if (state.Cycle is { } cycle) cycle.Victim = to;
                changed = true;
            }
            // "Transfer the turrets too this frame."
            if (other.Turrets is { } turrets)
                foreach (var turret in turrets)
                    if (turret is { AtPlace: false } && turret.Target == from)
                    {
                        turret.Target = to;
                        changed = true;
                    }
            if (changed) MarkDirty(other);
        }
    }

    /// <summary>
    /// <c>MM_Controller_AI | MM_Mood_Sleep</c>: "AI Units that are asleep actually ignore all commands" (<c>isAllowedToRespondToAiCommands</c>), acquire nothing while idle
    /// (<c>MAA_Affect_Range_IgnoreAll</c>) and do not fire (<c>AIAttackFireWeaponState::onEnter</c>).
    /// </summary>
    public bool IsAsleep(Entity entity) => IsComputerPlayer(entity.Owner) && AttitudeOf(entity) == Attitude.Sleep;

    private void RegisterAcquirer(Entity entity)
    {
        if (AiOf(entity) is { } ai && ((ai.AutoAcquire & AutoAcquire.Idle) != 0 || entity.Acquisition is not null || entity.Guard is not null || entity.AttackPlace is not null)) _acquirers.Add(entity.Id.Value);
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
    public void PassAttackToRiders(Entity container, Entity victim, bool onlyIdle = false)
    {
        foreach (var id in container.Container!.Contained)
        {
            if (FindEntity(id) is not { EffectivelyDead: false, DestroyRequested: false } rider || AiOf(rider) is not { } ai) continue;
            if (onlyIdle)
            {
                if (rider.Acquisition is { Victim.IsValid: true }) continue;
            }
            else if (!ContainerLetsFire(rider) || !IsAttackable(rider, victim) || !CanAttack(rider, victim, false, out _)) continue;
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

    private void NoteOrderedAttack(Entity unit, Entity victim, FireOrigin? fire = null)
    {
        if (AiOf(unit) is not { } ai) return;
        EndGuard(unit);
        var state = unit.Acquisition ??= new AcquisitionState();
        if (state.Chasing) ai.StopInternalMove();
        if (state.Victim != victim.Id) (state.FireOrdered, state.ShotPending) = (false, false);
        if (fire is { } origin) (state.FireOrdered, state.OrderOrigin, state.ShotPending) = (true, origin, true);
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
            var placeBefore = AttackPlaceStamp.Of(entity.AttackPlace);
            // An attack on a place replaces the unit's other states (AI_ATTACK_POSITION); it waits while the unit may not act.
            if (entity.AttackPlace is { } place)
            {
                if (MayAct(entity, tick)) StepAttackPlace(entity, ai, place, tick);
            }
            // A guarding unit's attacks are its guard's (AI_GUARD replaces the idle state the unit's own acquisition belongs to).
            else if (entity.Guard is { } guard)
            {
                if (MayAct(entity, tick) && TurretsLetAttack(entity)) StepGuard(entity, ai, guard, tick);
                else if (entity.Acquisition is { Victim.IsValid: true } held) EndEngagement(entity, ai, held);
            }
            else StepAcquirer(entity, ai, tick);
            if (entity.Acquisition is { IsDefault: true }) entity.Acquisition = null;
            if (AcquisitionStamp.Of(entity.Acquisition) != before || GuardStamp.Of(entity.Guard) != guardBefore || AttackPlaceStamp.Of(entity.AttackPlace) != placeBefore) MarkDirty(entity);
            if (entity.Acquisition is null && entity.Guard is null && entity.AttackPlace is null && (ai.AutoAcquire & AutoAcquire.Idle) == 0) _acquirers.Remove(id);
        }
    }

    public void DecideAfterLateOrder(Entity entity)
    {
        if (_firstFrameTicks == TickOrder.Next || !_acquirers.Contains(entity.Id.Value) || AiOf(entity) is not { } ai) return;
        var before = AcquisitionStamp.Of(entity.Acquisition);
        // The frame's first tick: the order belongs to the step that began there, and the look that follows counts from it.
        StepAcquirer(entity, ai, _tick + TickOrder.Next - _firstFrameTicks);
        if (entity.Acquisition is { IsDefault: true }) entity.Acquisition = null;
        if (AcquisitionStamp.Of(entity.Acquisition) != before) MarkDirty(entity);
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
            // A victim the attack was transferred to that the spawn phase creates later in this tick (a rebuild hole: the original creates it in the dying building's onDie).
            if (victim is null && IsSpawnPending(state.Victim)) return;
            var attackable = victim is not null && IsAttackable(entity, victim);
            if (state.Ordered)
            {
                // The command giver names the attack; its machine fires in the unit's frames (F-140), and the world notices when the attack is over.
                // "No order for longer than the grace" is noticed in the step after the grace: a logic frame of the original at every rate, not a tick.
                // A fire order may name a victim that is no enemy (the force-fire of a player): its attack goes on as long as any other attack would.
                var ordered = attackable || (state.FireOrdered && victim is not null && IsAttackable(entity, victim, enemiesOnly: false, forced: state.OrderOrigin == FireOrigin.Player));
                if (ordered && tick - Math.Max(state.OrderedTick, EarliestReadyTick(entity, tick)) < _orderedAttackGraceTicks + _turretFrameTicks)
                {
                    if (state.FireOrdered) StepOrderedFire(entity, state, victim!);
                    return;
                }
                state.ClearVictim();
            }
            else if (state.Forced)
            {
                // A slave's forced attack (F-65): it walks at its victim as the guard's attack does, without a fog filter, until it cannot attack it any more.
                if (victim is not null && (IsGuardVictim(entity, victim) || (state.Neutral && IsNeutralVictim(entity, victim)) || IsClearingVictim(entity, victim, ignoringStealth: !HasFiredAt(entity, victim)))
                    && GuardEngage(entity, ai, state, victim, tick)) return;
                if (ContinueAttack(entity, state, victim) is { } next && GuardEngage(entity, ai, state, next, tick)) return;
                EndEngagement(entity, ai, state);
                if (state.Mode != 0) (state.NextCheck, state.Offset) = (tick + _forceIdleTicks, true);
            }
            else
            {
                if (attackable && Engage(entity, ai, state, victim!, ModeOf(entity, ai, state), tick)) return;
                if (ContinueAttack(entity, state, victim) is { } next && Engage(entity, ai, state, next, ModeOf(entity, ai, state), tick)) return;
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
        => !entity.HasStatus(ObjectStatusBits.UnderConstruction) && !entity.HasStatus(SoldStatus) && !entity.IsDisabledByTime(tick) && !entity.Subdued
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
        // aiAttackObject of the idle state: the walk to the victim is the attack, not a move that makes the unit busy.
        if (state is { Chasing: true }) return AcquireMode.Idle;
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

    /// <summary>
    /// The victim is still one the unit may attack: alive, an enemy and not hidden (<c>getAbleToAttackSpecificObject(ATTACK_CONTINUED_TARGET, …)</c>). "Fog no longer prevents attacking at all. it prevents
    /// targeting in findClosestEnemy" (<c>WeaponSet.cpp</c>): only a human player's unit looks through the fog filter, in <see cref="ConsiderMoodTarget"/>.
    /// </summary>
    /// <para><paramref name="forced"/>: a player's fire order, which at a victim that is no enemy to look at is the force-fire (<see cref="StealthYieldsToForce"/>).</para>
    private bool IsAttackable(Entity entity, Entity victim, bool enemiesOnly = true, bool forced = false)
        => !victim.DestroyRequested && !victim.EffectivelyDead && victim.Body is not null && !victim.ContainedBy.IsValid && (!enemiesOnly || AreEnemies(entity, victim))
           && (!IsHiddenFrom(entity.Owner, victim) || (forced && StealthYieldsToForce(entity, victim))) && !IsNoTarget(victim);

    /// <summary>
    /// <c>WeaponSet::getAbleToAttackSpecificObject</c> for a forced attack (<c>ATTACK_NEW_TARGET_FORCED</c>, <c>ATTACK_CONTINUED_TARGET_FORCED</c>): stealth does not keep it off a victim of
    /// the attacker's own player (<c>sameOwnerForceAttack</c>), nor off a <c>DISGUISER</c> in disguise - "force-attack allows you to attack disguised things, which also happen to be
    /// stealthed". The port's fire orders carry no forced flag, and need none here: an undetected stealthed victim of the own player, or one in disguise as a non-enemy, is a victim a
    /// player can only name by force-fire.
    /// </summary>
    private bool StealthYieldsToForce(Entity entity, Entity victim)
        => victim.Owner == entity.Owner || (victim.Template.IsKindOf("DISGUISER") && victim.HasStatus(DisguisedStatus));

    /// <summary>
    /// A player may name <paramref name="entity"/> in a fire order: seen (<see cref="CanPlayerSee"/>), or a <c>DISGUISER</c> in disguise in a visible cell, which the force-fire may attack
    /// although its disguise hides it from the player (<see cref="StealthYieldsToForce"/>).
    /// </summary>
    public bool CanPlayerForceTarget(PlayerId player, Entity entity)
    {
        if (CanPlayerSee(player, entity)) return true;
        if (_vision is null || entity.ContainedBy.IsValid || !entity.Template.IsKindOf("DISGUISER") || !entity.HasStatus(DisguisedStatus)) return false;
        var (x, y, _) = entity.FrameEnd(_tick);
        return IsPositionVisibleTo(player, x, y) && !IsBeacon(entity);
    }

    /// <summary><c>WeaponSet::getAbleToAttackSpecificObject</c>: "an OBJECT_STATUS_MASKED object is not really a valid target", and "the KINDOF version, for things that aren't really game objects".</summary>
    private static bool IsNoTarget(Entity victim) => victim.HasStatus(MaskedBit) || victim.Template.IsKindOf("UNATTACKABLE");

    private static readonly int MaskedBit = ObjectStatusBits.Index("MASKED");
    private static readonly int NoAttackFromAiBit = ObjectStatusBits.Index("NO_ATTACK_FROM_AI");

    /// <summary>
    /// One tick of an attack on <paramref name="victim"/>: in range the unit stands (an attack-move stops for the fight, aircraft keep flying) and fires when its weapon may; out of range a
    /// computer player's unit walks to the victim (<c>AIAttackApproachTargetState</c>), from rest as on an attack-move (<c>AIIdleState::update</c> calls <c>aiAttackObject</c>), everything else
    /// gives the victim up — a human player's unit that picked its victim itself may not chase it (<c>isAllowedToChase</c>), and an idle turret drops a victim that left its range
    /// (<c>m_targetWasSetByIdleMood</c>). False when the attack is over.
    /// </summary>
    private bool Engage(Entity entity, AIUpdateInterface ai, AcquisitionState state, Entity victim, AcquireMode mode, long tick)
    {
        var turretOnly = mode == AcquireMode.Turret;
        if (IsAsleep(entity) || !CanAttack(entity, victim, turretOnly, out var inRange)) return false;
        var aircraft = entity.Template.IsKindOf("AIRCRAFT");
        // wantToSquishTarget: a computer's tank in range goes on towards the infantry with its turret firing.
        var squish = inRange && !turretOnly && WantsToSquish(entity, victim);
        if (inRange && !squish)
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
            NoteContinueOrigin(entity, state, victim);
            AutoFire(entity, victim, turretOnly);
            return true;
        }

        // outOfWeaponRangeObject and wantToSquishTarget take the body's machine to its chase (F-140); out of range a turret's fire state goes back to its aim.
        AttackCycleOutOfRange(entity, ref state.Cycle, turrets: !inRange);
        if (squish) AutoFire(entity, victim, turretOnly);
        if (turretOnly || !MayChase(entity, aircraft)) return squish;
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

    /// <summary><c>TAiData::m_aiCrushesInfantry</c> (<c>AIData.ini AICrushesInfantry</c>): the reference default and the shipped value.</summary>
    private const bool AiCrushesInfantry = true;

    /// <summary><c>wantToSquishTarget</c>: "I can only decide to crush-attack if I am attacking with a turreted weapon."</summary>
    private bool WantsToSquish(Entity entity, Entity victim)
    {
        if (!AiCrushesInfantry || victim.ContainedBy.IsValid || !IsComputerPlayer(entity.Owner) || entity.Template.IsKindOf("DONT_AUTO_CRUSH_INFANTRY")) return false;
        if (entity.Template.CrusherLevel == 0 || !IsOnAimingTurret(entity, WeaponSlot.Primary) || RelationshipOf(entity.Owner, victim.Owner) == Relationship.Allies) return false;
        return entity.Template.CrusherLevel > victim.Template.CrushableLevel || Array.Find(victim.ModuleArray, module => module is SquishCollide) is not null;
    }

    /// <inheritdoc cref="IWorldServices.OrderRidersToIdle"/>
    public void OrderRidersToIdle(Entity container)
    {
        if (container.Container is not { } contain) return;
        foreach (var id in contain.Contained)
            if (FindEntity(id) is { Acquisition: { Victim.IsValid: true } state } rider && Array.Find(rider.ModuleArray, module => module is AIUpdateInterface) is AIUpdateInterface ai)
                EndEngagement(rider, ai, state);
    }

    private void EndEngagement(Entity entity, AIUpdateInterface ai, AcquisitionState state)
    {
        EndPreAttack(entity);
        StopTurrets(entity);
        if (state.Chasing) ai.StopInternalMoveAtFrameEnd(this);
        state.ClearVictim();
        state.ContinueOwner = 0;
        (state.ContinueX, state.ContinueY) = (Fixed.Zero, Fixed.Zero);
    }

    /// <summary>The largest <c>ContinueAttackRange</c> of the weapons of the unit's active set (F-69); zero for every weapon but the builders' mine-clearing ones.</summary>
    private Fixed ContinueRangeOf(Entity entity)
    {
        var range = Fixed.Zero;
        if (ActiveWeaponSet(entity) is not { } set) return range;
        foreach (var index in set.Weapons)
            if (index >= 0 && _templates.Weapons[index].ContinueAttackRange > range) range = _templates.Weapons[index].ContinueAttackRange;
        return range;
    }

    /// <summary>The first shot of an attack with a continue range remembers where its first victim stood and whose it was (<c>m_originalVictimPos</c>, F-69).</summary>
    private void NoteContinueOrigin(Entity entity, AcquisitionState state, Entity victim)
    {
        if (state.ContinueOwner != 0 || ContinueRangeOf(entity) <= Fixed.Zero) return;
        var (x, y, _) = victim.FrameEnd(_tick);
        (state.ContinueX, state.ContinueY, state.ContinueOwner) = (x, y, victim.Owner.Value);
    }

    /// <summary>
    /// <c>AIAttackFireWeaponState::update</c>: after a weapon with a <c>ContinueAttackRange</c> destroyed its victim (or the victim is gone), the attack goes on at the closest object of
    /// the first victim's player within that range of where the first victim stood (between the centres, on the ground; ties by id) that the unit may attack - "after disarming a
    /// mine, look for additional mines". Null when there is none; the attack then ends.
    /// </summary>
    private Entity? ContinueAttack(Entity entity, AcquisitionState state, Entity? victim)
    {
        if (state.ContinueOwner == 0 || (victim is not null && !victim.EffectivelyDead && !victim.DestroyRequested)) return null;
        var range = ContinueRangeOf(entity);
        if (range <= Fixed.Zero) return null;
        Entity? best = null;
        var bestDistance = Fixed.Zero;
        var reach = SquaredReach.Of(range);
        foreach (var candidate in EntitiesOwnedBy(new PlayerId(state.ContinueOwner)).AsSpan())
        {
            // After the first shot: the original has cleared IGNORING_STEALTH before it looks for the next victim (AIAttackFireWeaponState::update).
            if (candidate == victim || !IsClearingVictim(entity, candidate, ignoringStealth: false) || !CanAttack(entity, candidate, false, out _)) continue;
            var dx = candidate.X - state.ContinueX;
            var dy = candidate.Y - state.ContinueY;
            var distance = dx * dx + dy * dy;
            if (!reach.Contains(distance)) continue;
            if (best is null || distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        if (best is null) return null;
        state.Victim = best.Id;
        // AIAttackFireWeaponState::update sets the machine's goal object: the machine goes on.
        if (state.Cycle is { } cycle) cycle.Victim = best.Id;
        state.Neutral = state.Neutral || RelationshipOf(entity.Owner, best.Owner) == Relationship.Neutral;
        AimTurretsForAttack(entity, best, TurretAimOrder.Attack);
        return best;
    }

    /// <summary>
    /// Only a computer player's ground unit walks to a victim it picked itself; a human player's unit stays where its player put it, and a rider never walks (it is held and not
    /// mobile, <c>Object::isMobile</c>; its attack state fails out of range, <c>AIAttackAimAtTargetState::update</c>).
    /// </summary>
    private bool MayChase(Entity entity, bool aircraft) => !aircraft && entity.Template.Locomotor is not null && IsComputerPlayer(entity.Owner) && !entity.ContainedBy.IsValid;

    /// <summary>
    /// <c>AI::getAdjustedVisionRangeForObject(obj, OWNERTYPE | MOOD)</c> for a unit that may walk to its victim: the vision range times the guard-outer factor, times the mood factor of a
    /// computer player's alert or aggressive team. A unit that may not walk looks no further than its weapons reach (<c>AI::WITHIN_ATTACK_RANGE</c>) and its vision times the human factor.
    /// </summary>
    private Fixed AcquireRadius(Entity entity, bool chase)
    {
        var reach = MaxWeaponRange(entity);
        // AI::getAdjustedVisionRangeForObject gives a contained object its largest weapon range (with its bonuses), and getNextMoodTarget adds the container's bounding circle
        // "so that large buildings can auto acquire everything in outer ranges".
        if (FiringContainerOf(entity) is { } container) return reach + container.Template.BoundingCircleRadius;
        // localApplyBattlePlanBonusesToObject: Search & Destroy sets the vision range of the army, not its shroud alone.
        var seen = entity.Template.VisionRange * BattlePlanSightScalarOf(entity);
        if (!chase) return IsComputerPlayer(entity.Owner) ? reach : Fixed.Min(reach, seen * AcquireVisionFactorHuman);
        var vision = seen * AcquireVisionFactorComputer;
        if (IsComputerPlayer(entity.Owner))
        {
            switch (AttitudeOf(entity))
            {
                case Attitude.Alert: vision *= AlertRangeFactor; break;
                case Attitude.Aggressive: vision *= AggressiveRangeFactor; break;
            }
        }

        return vision;
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
        // "we are doing a special ability. Shouldn't auto-acquire a target at this time."
        if (entity.HasStatus(UsingAbilityBit)) return null;
        var computer = IsComputerPlayer(entity.Owner);
        var container = FiringContainerOf(entity);
        // "Human units don't have a mood": only a computer player's unit sleeps or stays passive. The sleeper never looks: AIIdleState and TurretAI test MAA_Affect_Range_IgnoreAll
        // before they call getNextMoodTarget, also inside a container, where the range would not be the mood's zero. Passive is getNextMoodTarget's own test and holds inside too.
        var attitude = computer ? AttitudeOf(entity) : Attitude.Normal;
        if (attitude == Attitude.Sleep) return null;
        // A passive unit answers its last attacker: "Do not allow units that healed me to get acquired!"
        if (attitude == Attitude.Passive)
            return !entity.LastDamageHealing && FindEntity(entity.LastDamageSource) is { } attacker && IsAttackable(entity, attacker) ? attacker : null;
        var aircraft = entity.Template.IsKindOf("AIRCRAFT");
        var chase = mode != AcquireMode.Turret && MayChase(entity, aircraft);
        var radius = AcquireRadius(entity, chase);
        if (radius <= Fixed.Zero) return null;
        var turretOnly = mode == AcquireMode.Turret;
        var buildings = (ai.AutoAcquire & AutoAcquire.AttackBuildings) != 0;
        // A rider looks from where it stands (ShooterPosition); its container's bounding circle is in the radius.
        // Above 30 Hz the look measures from where the unit's begun frame ends to where each candidate's ends (rule 3 of the frame convention).
        var frameEnd = _firstFrameTicks != TickOrder.Next;
        var (ownX, ownY, _) = ShooterPosition(entity, frameEnd);
        var collect = PicksByDamagePotential;
        var search = new MoodSearch { Radius = radius, OwnRadius = CircleRadius(entity), TurretOnly = turretOnly, Chase = chase, Buildings = buildings, Computer = computer, BestDistance = Fixed.MaxValue, OwnX = ownX, OwnY = ownY, FrameEnd = frameEnd, Collect = collect };
        if (collect) _victimCandidates.Clear();

        if (RelationshipOverride is not null)
        {
            // A script set a relation between teams: whether two objects are enemies is then a question per pair of objects, so every object is a candidate.
            foreach (var candidate in _entities.Values) ConsiderMoodTarget(entity, candidate, ref search);
            return collect ? PickByDamagePotential(entity, radius) : search.Best;
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

        return collect ? PickByDamagePotential(entity, radius) : search.Best;
    }

    private struct MoodSearch
    {
        /// <summary>The rule fix <see cref="Rules.GameFix.TargetByDamagePotential"/>: the candidates are collected and ranked by value instead of kept as the closest.</summary>
        public bool Collect;
        public Fixed Radius;
        public Fixed OwnRadius;
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
        // FROM_BOUNDINGSPHERE_2D: the range and the distance are measured between the two bounding circles.
        var distance = dx * dx + dy * dy;
        var radii = search.OwnRadius + CircleRadius(candidate);
        if (!SquaredReach.Of(search.Radius + radii).Contains(distance)) return;
        // A mine is a target like any other (F-69): only a weapon with AntiMine can attack it (getVictimAntiMask) - the builders' mine-clearing weapons.
        if (candidate.Template.IsKindOf("PROJECTILE") || candidate.Template.IsKindOf("INERT")) return;
        // "This object is not currently auto-acquireable."
        if (candidate.HasStatus(NoAttackFromAiBit)) return;
        if (!IsAttackable(entity, candidate) || !AcquiresStructure(entity, candidate, search.Buildings, search.Computer) || IsHiddenStealthGarrison(entity, candidate)) return;
        // AI::UNFOGGED: "Instead of shroud affecting the ability to attack, it affects the ability to target" - for a human player's unit only.
        if (!search.Computer && !CanPlayerSee(entity.Owner, candidate)) return;
        if (AcquisitionViewBlocked(entity, candidate)) return;
        if (!CanAttack(entity, candidate, search.TurretOnly, out var inRange) || (!inRange && !search.Chase)) return;

        var gap = Fixed.Max(Fixed.Zero, Fixed.Sqrt(distance) - radii);
        if (AcquisitionAdvisor is { } advisor && advisor.TryAttackPriority(entity, candidate, out var priority))
        {
            if (priority == 0) return;
            // "check for garrisoned buildings/vehicles & see if a higher priority unit is inside."
            if (candidate.Container is { ContainCount: > 0 } inside)
                foreach (var id in inside.Contained)
                    if (FindEntity(id) is { } rider && advisor.TryAttackPriority(entity, rider, out var riderPriority) && riderPriority > priority) priority = riderPriority;
            var effective = (int)Math.Max(1, priority - (gap / AttackPriorityDistanceStep).Floor());
            if (search.Collect)
            {
                _victimCandidates.Add(new VictimCandidate { Entity = candidate, Gap = gap, Effective = effective, Priority = priority });
                return;
            }

            var better = effective > search.BestEffective || (effective == search.BestEffective && priority > search.BestPriority)
                         || (effective == search.BestEffective && priority == search.BestPriority && IsNearer(candidate, gap, search.Best, search.BestDistance));
            if (!better) return;
            search.BestEffective = effective;
            search.BestPriority = priority;
        }
        else if (search.Collect)
        {
            _victimCandidates.Add(new VictimCandidate { Entity = candidate, Gap = gap });
            return;
        }
        else if (!IsNearer(candidate, gap, search.Best, search.BestDistance)) return;

        search.Best = candidate;
        search.BestDistance = gap;
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
        if (candidate.Container is { } contain && (!contain.IsGarrisonable || contain.ContainCount == 0) && !IsFactionStructure(candidate.Template)) return false;
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
        if (!TurretsLetAttack(source) || source.Acquisition is not { } attack) return;
        var choice = ChooseWeapon(source, victim, FireOrigin.Ai);
        // A weapon that reloads still runs its machine (F-140): the aim state aims, the fire state fails back to it.
        if (choice.Slot < 0 || choice.Rejection is not (0 or CommandRejection.WeaponNotReady) || (turretOnly && !IsOnAimingTurret(source, choice.Slot)) || !GateAllowsFire(source, victim)) return;
        if (!AttackCycleFires(source, ref attack.Cycle, choice.Slot, victim, Fixed.Zero, Fixed.Zero, Fixed.Zero, ordered: false)) return;
        FireCycleShot(source, ref attack.Cycle, choice.Slot, victim, source.Owner);
    }
}

using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// <c>JetAIUpdateModuleData</c> (<c>JetAIUpdate.cpp</c>) in simulation units. <c>TakeoffDistForMaxLift</c> (unused by the reference's take-off lift, which scales with the runway
/// distance), <c>MinHeight</c> (an instance offset of the drawable) and the <c>Lockon*</c> fields (the lock-on cursor and the time missiles need to lock) are typed by the catalog schema and have
/// no consumer here.
/// </summary>
public sealed record JetAIData(AIUpdateData Base, Fixed OutOfAmmoDamageRatePerSecond, bool NeedsRunway, bool KeepsParkingSpaceWhenAirborne, long TakeoffPauseTicks, Fixed ParkingOffset,
    string AttackLocomotor, long AttackLocomotorPersistTicks, string ReturnLocomotor, long ReturnToBaseIdleTicks, Fixed SneakyOffset = default, long AttackersMissPersistTicks = 0) : ModuleData
{
    public static JetAIData Parse(RatedModuleConfiguration configuration)
    {
        string Set(string key)
        {
            var name = ModuleConfigReader.Text(configuration, key);
            return name.Length == 0 ? "SET_NORMAL" : name;
        }
        return new JetAIData(
            AIUpdateData.Parse(configuration),
            ModuleConfigReader.Real(configuration, "OutOfAmmoDamagePerSecond", Fixed.Zero),
            ModuleConfigReader.Bool(configuration, "NeedsRunway", true),
            ModuleConfigReader.Bool(configuration, "KeepsParkingSpaceWhenAirborne", true),
            ModuleConfigReader.Ticks(configuration, "TakeoffPause", 0),
            ModuleConfigReader.Real(configuration, "ParkingOffset", Fixed.Zero),
            Set("AttackLocomotorType"),
            ModuleConfigReader.Ticks(configuration, "AttackLocomotorPersistTime", 0),
            Set("ReturnForAmmoLocomotorType"),
            ModuleConfigReader.Ticks(configuration, "ReturnToBaseIdleTime", 0),
            ModuleConfigReader.Real(configuration, "SneakyOffsetWhenAttacking", Fixed.Zero),
            ModuleConfigReader.Ticks(configuration, "AttackersMissPersistTime", 0));
    }
}

/// <summary>
/// <c>JetAIUpdate.cpp</c>: aircraft that live on an airfield (<see cref="ParkingPlaceBehavior"/>). The reference is a state machine on top of the AI state machine whose states
/// are mirrored here as <see cref="JetPhase"/>:
/// <list type="bullet">
/// <item><description><b>Leaving the hangar</b> (<c>TAXI_FROM_HANGAR</c>): a produced aircraft (its airfield found by <see cref="ParkingPlaceBehavior.TryAdoptArrival"/>) drives from the
/// hangar to its space, turns to the parking direction (<c>ORIENT_FOR_PARKING_PLACE</c>) and fills its ammunition (<c>RELOAD_AMMO</c>). It cannot fly while it holds a space
/// (<c>ALLOW_AIR_LOCO</c> false, taxi locomotor <c>SET_TAXIING</c>); an aircraft without an airfield (placed or scripted) flies from the start.</description></item>
/// <item><description><b>Take-off</b>: a move order given to a parked aircraft is stored (<c>HAS_PENDING_COMMAND</c>) and starts <c>TAKING_OFF_AWAIT_CLEARANCE</c> (reserve space and the runway;
/// the second aircraft of a column waits as next-in-line), <c>TAXI_TO_TAKEOFF</c> (to the taxi point and runway start, or to the taxi point only when it queued), <c>PAUSE_BEFORE_TAKEOFF</c>
/// (face the runway, wait while the aircraft ahead still taxis, <c>TakeoffPause</c>, hand the runway on), <c>TAKING_OFF</c> (accelerate at the flight locomotor's acceleration along the runway,
/// climbing with the square of the covered runway fraction to the approach height, then on to the exit point), after which the stored order is carried out. It keeps its space
/// while airborne unless <c>KeepsParkingSpaceWhenAirborne = No</c>.</description></item>
/// <item><description><b>Return</b>: out of <c>RETURN_TO_BASE</c> ammunition while idle (with <c>ReturnForAmmoLocomotorType</c>), or idle for <c>ReturnToBaseIdleTime</c>, an aircraft flies to the
/// runway's approach point (<c>RETURNING_FOR_LANDING</c>; no own airfield: the closest allied one with a free space; none: it flies to where its airfield was and circles taking
/// <c>OutOfAmmoDamagePerSecond</c> while out of ammunition, looking for a new airfield every second — <c>RETURN_TO_DEAD_AIRFIELD</c>, <c>CIRCLING_DEAD_AIRFIELD</c>), waits for the runway
/// (<c>LANDING_AWAIT_CLEARANCE</c>), glides down at the locomotor's minimum speed and rolls out (<c>LANDING</c>), taxis to its space (<c>TAXI_FROM_LANDING</c>, reloads its countermeasures),
/// orients, and reloads its weapon for the time the missing share of the clip takes (<c>RELOAD_AMMO</c>). While parked or reloading it is healed by the airfield. Helicopters
/// (<c>NeedsRunway = No</c>, <c>PRODUCED_AT_HELIPAD</c>) rise from and settle onto the helipad vertically and need no space.</description></item>
/// <item><description><b>Orders</b>: <see cref="OrderMove"/> and <see cref="OrderEnter"/> (landing at an airfield) are the orders aircraft answer; they are stored while a take-off or landing
/// runs and carried out when it is over.</description></item>
/// <item><description><b>Locomotor sets</b>: <c>SET_TAXIING</c> while it cannot fly, the <c>AttackLocomotorType</c> set for <c>AttackLocomotorPersistTime</c> after it fired, the
/// <c>ReturnForAmmoLocomotorType</c> set while returning for ammunition (<see cref="ObjectTemplate.LocomotorSets"/>); a set the object does not declare falls back to <c>SET_NORMAL</c>.</description></item>
/// </list>
/// <b>Kinematics instead of physics and pathfinding</b>: ground movement follows straight segments between the layout points at the taxi locomotor's speed (turning in place at its turn
/// rate, stopping on the spot — its braking is huge); take-off and landing set the height as a function of the covered distance (the reference's lift ramp
/// <c>m_maxLift · ratio²</c> and the glide to the runway); free flight is the ordinary air movement of <see cref="AIUpdateInterface"/> with the height approaching the locomotor's
/// <c>PreferredHeight</c> by the locomotor's lift (net of gravity) and a quarter of its speed on the way down. A fixed-wing aircraft (WINGS locomotor) flies with a minimum speed, a limited turn rate and circles when it has nothing to do (<c>JetAIUpdate.Flight.cs</c>,
/// <c>docs/SIM-JET-FLIGHT.md</c>); every other aircraft holds its position in the air; the landing aligns the heading to the runway at its start (no turn onto the approach). A guard waits for the take-off and is interrupted by an empty magazine and given again after the reload (F-58); not modeled: the same for a hunt order (the
/// port has none), <c>notifyVictimIsDead</c> (no attack state), the lock-on cursor, afterburners and landing sounds, aircraft-carrier decks, the sneaky targeting offset, the progressive
/// refill of the clip while reloading (the clip is full when the reload time is over).
/// </summary>
public sealed partial class JetAIUpdate(JetAIData data) : AIUpdateInterface(data.Base)
{
    public enum JetPhase : byte
    {
        None, TaxiFromHangar, AwaitTakeoff, TaxiToTakeoff, PauseBeforeTakeoff, TakingOff, ReturningForLanding, AwaitLanding, Landing, TaxiFromLanding, OrientForParking, ReloadAmmo,
        ReturnToDeadAirfield, CirclingDeadAirfield,
    }

    private enum PendingKind : byte { None, Move, Land, Guard }

    private static readonly Fixed ParkedOrientationTolerance = Fixed.FromDecimal(0.001m);
    private static readonly Fixed Misaligned = FixedTrig.PiFraction(1, 6); // as the ground movement of AIUpdateInterface
    private static readonly Fixed Epsilon = Fixed.FromFraction(1, 64);
    // Own speeds of the kinematic stand-in, written as distances per logic frame of the original and scaled where they are used (untouched at 30 Hz).
    /// <summary>The taxi speed of an aircraft without an active locomotor.</summary>
    private static readonly Fixed FallbackTaxiSpeedPerFrame = Fixed.One;
    /// <summary><c>m_ultraAccurateSlideIntoPlaceFactor</c> of a wing's locomotor: within this many frames of travel of its point the flight goes straight at it.</summary>
    private static readonly Fixed SlideIntoPlaceFrames = Fixed.FromInt(2);

    /// <summary>The slowest horizontal approach of a helicopter to its landing spot.</summary>
    private static readonly Fixed MinHelicopterApproachSpeedPerFrame = Fixed.One;
    /// <summary>The slowest landing speed of a winged aircraft.</summary>
    private static readonly Fixed MinLandingSpeedPerFrame = Fixed.One / Fixed.FromInt(2);
    /// <summary><c>TINY_ACCEL</c> of <c>Locomotor::calcLiftToUseAtPt</c>.</summary>
    private static readonly Fixed TinyLift = Fixed.FromDecimal(0.001m);

    private JetPhase _phase;
    private bool _allowAir = true;
    private EntityId _airfield;
    private Fixed _homeX;
    private Fixed _homeY;
    private PendingKind _pending;
    private bool _pendingAttackMove;
    private Fixed _pendingX;
    private Fixed _pendingY;
    private bool _pendingFromPlayer;
    private EntityId _pendingTarget;
    private readonly List<Point3> _path = [];
    private int _pathIndex;
    private long _whenTick;
    private long _transferTick;
    private EntityId _waitedFor;
    private bool _resetTimer;
    private long _reloadDoneTick;
    private long _returnTick;
    private bool _useReturnLocomotor;
    private long _attackExpireTick;
    // m_attackersMissExpireFrame: while it lies ahead, attackers aim behind the aircraft and nothing on a projectile's way meets it.
    private long _attackersMissExpireTick;
    private long _checkAirfieldTick;
    /// <summary>The climb speed of the height control, per logic frame of the original.</summary>
    private Fixed _verticalSpeed;
    private ParkingInfo _leg;
    /// <summary>The step the states decided for the running logic frame of the original, flown in shares per tick; empty at 30 Hz, where a frame is a tick.</summary>
    private FlightFrameStep _frame;

    public override string ModuleName => "JetAIUpdate";

    public JetPhase Phase => _phase;

    /// <summary><c>ALLOW_AIR_LOCO</c>: the aircraft may fly (it holds no space it still has to leave).</summary>
    public bool AllowAirLoco => _allowAir;

    /// <summary><c>friend_isTakeoffOrLandingInProgress</c> (the take-off/landing flags the states set, derived from the phase).</summary>
    public bool TakeoffOrLandingInProgress => _phase is JetPhase.TaxiFromHangar or JetPhase.AwaitTakeoff or JetPhase.TaxiToTakeoff or JetPhase.PauseBeforeTakeoff or JetPhase.TakingOff
        or JetPhase.AwaitLanding or JetPhase.Landing or JetPhase.TaxiFromLanding or JetPhase.OrientForParking;

    /// <summary>The airfield that produced (or took in) the aircraft (<c>Object::getProducerID</c>).</summary>
    public EntityId Airfield => _airfield;

    /// <summary><c>setProducer(nullptr)</c> of <c>defectAllParkedUnits</c>: the captured airfield is no longer this aircraft's.</summary>
    internal void ForgetAirfield(EntityId airfield)
    {
        if (_airfield == airfield) _airfield = EntityId.Invalid;
    }

    /// <summary><c>HAS_PENDING_COMMAND</c>.</summary>
    public bool HasPendingCommand => _pending != PendingKind.None;

    /// <summary>
    /// <c>friend_enableAfterburners</c>: <c>JetPauseBeforeTakeoffState::update</c> switches them on once the countdown to take-off (re)starts (no aircraft ahead is still taxiing) and
    /// <c>JetTakeoffOrLandingState::onExit</c> off again, so they burn from then to the end of the run on the runway. Only jets (<c>HeliAIStateMachine</c> has no pause state). Read by the presentation (<c>JETAFTERBURNER</c>).
    /// </summary>
    public bool AfterburnersOn => !Helicopter && (_phase == JetPhase.TakingOff || (_phase == JetPhase.PauseBeforeTakeoff && _resetTimer));

    /// <summary><c>isReloading</c>.</summary>
    public bool IsReloading => _phase == JetPhase.ReloadAmmo;

    /// <summary><c>isTaxiingToParking</c>.</summary>
    public bool IsTaxiingToParking => _phase is JetPhase.TaxiFromHangar or JetPhase.TaxiFromLanding or JetPhase.OrientForParking or JetPhase.ReloadAmmo or JetPhase.AwaitTakeoff
        or JetPhase.TaxiToTakeoff or JetPhase.PauseBeforeTakeoff or JetPhase.TakingOff;

    /// <summary>The scripting view of <c>isIdle</c>: no stored order and not in any airfield state (the reference reports "busy" between take-off and landing steps).</summary>
    public override bool IsIdle => _pending == PendingKind.None && _phase == JetPhase.None && base.IsIdle;

    private bool HelipadKind => Entity.Template.IsKindOf("PRODUCED_AT_HELIPAD");

    /// <summary>The <c>HeliAIStateMachine</c> flies it: vertical take-off and landing instead of a runway.</summary>
    private bool Helicopter => !data.NeedsRunway || HelipadKind;

    protected override LocomotorInfo? SelectedLocomotor
    {
        get
        {
            var normal = Entity.Template.Locomotor;
            if (!_allowAir) return SetOf("SET_TAXIING") ?? normal;
            if (_attackExpireTick != 0) return SetOf(data.AttackLocomotor) ?? normal;
            if (_useReturnLocomotor) return SetOf(data.ReturnLocomotor) ?? normal;
            return normal;
        }
    }

    private LocomotorInfo? SetOf(string name)
        => name == "SET_NORMAL" ? Entity.Template.Locomotor : Entity.Template.LocomotorSets.TryGetValue(name, out var found) ? found : null;

    // ------------------------------------------------------------------------------------------------------------------ creation

    public override void OnSpawn(IWorldServices world)
    {
        base.OnSpawn(world);
        NextWake = world.Tick + SimulationTiming.SleepNone;
        _homeX = Entity.X;
        _homeY = Entity.Y;
        // JetAIUpdate::getProducerLocation: with no airfield that holds a space for it the aircraft simply flies.
        var parking = ParkingPlaceBehavior.TryAdoptArrival(world, Entity, out var atHelipad, out var parked);
        if (parking is null) return;
        _airfield = parking.Entity.Id;
        _homeX = parking.Entity.X;
        _homeY = parking.Entity.Y;
        _allowAir = false;
        // FlightDeckBehavior::buildInfo: the jets a carrier comes with stand on their spaces.
        if (parked) return;
        if (atHelipad || !data.NeedsRunway)
        {
            // aiMoveToPosition(parking space): the order waits while the helicopter lifts off; a rally point order replaces it.
            _pending = PendingKind.Move;
            _pendingX = Entity.X;
            _pendingY = Entity.Y;
            _pendingFromPlayer = false;
            _phase = JetPhase.AwaitTakeoff;
            return;
        }
        if (parking.TryGetInfo(world, Entity.Id, out var info))
        {
            Entity.Angle = info.HangarInternalOrientation;
            _leg = info;
            _path.Clear();
            if (OnDeck)
            {
                // "Skip the first node because it's the creation location."
                _path.AddRange(parking.CreationLocations(world, Entity.Id).Skip(1));
                _path.Add(info.RunwayPrep);
            }
            else _path.Add(info.ParkingSpace);
            _pathIndex = 0;
            _phase = JetPhase.TaxiFromHangar;
        }
    }

    /// <summary><c>OBJECT_STATUS_DECK_HEIGHT_OFFSET</c>: the aircraft belongs to a carrier's deck, and the deck's rules of taxiing hold.</summary>
    private bool OnDeck => Entity.HasStatus(DeckHeightBit);

    private static readonly int DeckHeightBit = ObjectStatusBits.Index("DECK_HEIGHT_OFFSET");

    /// <summary>The stored order is a landing (<c>friend_getPendingCommandType</c> is <c>AICMD_ENTER</c>).</summary>
    public bool IsOrderedToLand => _pending == PendingKind.Land;

    /// <summary><c>friend_purgePendingCommand</c>.</summary>
    public void PurgePending()
    {
        _pending = PendingKind.None;
        _pendingAttackMove = false;
    }

    /// <summary><c>OBJECT_STATUS_REASSIGN_PARKING</c>: the deck gave the aircraft a space further forward, "so simply move to the new parking spot which was just switched from under us".</summary>
    public void TaxiToNewSpace(IWorldServices world, ParkingPlaceBehavior deck)
    {
        if (!deck.TryGetInfo(world, Entity.Id, out var info)) return;
        Enter(JetPhase.TaxiFromHangar);
        _leg = info;
        _path.Add(info.RunwayPrep);
    }

    /// <summary><c>JetOrHeliTaxiState::update</c>: "keep checking to see if there is a better spot as it moves forward".</summary>
    private void LookForABetterSpace(IWorldServices world, ParkingPlaceBehavior parking)
    {
        if (OnDeck && parking.CalcBestParkingAssignment(world, Entity.Id, out var best)) _path.Add(best);
    }

    public override void OnRemove(IWorldServices world)
    {
        // JetAIUpdate::onDelete: the space is given back.
        if (ParkingOf(world) is { } parking) parking.ReleaseSpace(world, Entity);
    }

    // ------------------------------------------------------------------------------------------------------------------ helpers

    private ParkingPlaceBehavior? ParkingOf(IWorldServices world)
    {
        if (!_airfield.IsValid) return null;
        var field = world.FindEntity(_airfield);
        if (field is null || field.EffectivelyDead || field.DestroyRequested || !field.Template.IsKindOf("FS_AIRFIELD") || field.HasStatus(ObjectStatusBits.Sold)) return null;
        return Array.Find(field.ModuleArray, module => module is ParkingPlaceBehavior) as ParkingPlaceBehavior;
    }

    /// <summary>
    /// <c>isOutOfSpecialReloadAmmo</c>: the object has at least one weapon in its active set that needs the airfield to reload (<c>RETURN_TO_BASE</c>) and every such weapon is out of ammo
    /// (a Raptor's missile slot empty, whatever its gun still holds).
    /// </summary>
    public bool IsOutOfSpecialReloadAmmo(IWorldServices world)
    {
        var specials = 0;
        var empty = 0;
        for (var slot = 0; slot < WeaponSlot.Count; slot++)
        {
            var index = SimulationWorld.WeaponIndexIn(Entity, slot);
            if (index < 0 || !world.Weapons[index].ReloadsAtBase) continue;
            specials++;
            if (Entity.ClipOf(slot) == 0) empty++;
        }
        return specials > 0 && empty == specials;
    }

    private void Enter(JetPhase phase)
    {
        _phase = phase;
        _path.Clear();
        _pathIndex = 0;
    }

    private Fixed GroundHere(IWorldServices world) => world.GroundHeightAt(Entity.X, Entity.Y);

    /// <summary>The height of the surface the aircraft stands or rolls on at its position: the apron of its airfield (the model's tarmac lies at the airfield's own height, which on a slope differs from the terrain under the aircraft), else the terrain.</summary>
    private Fixed SurfaceHere(IWorldServices world) => ParkingOf(world) is { } parking ? parking.ApronHeight(world, Entity.X, Entity.Y) : GroundHere(world);

    /// <summary>
    /// One tick of ground movement along <see cref="_path"/> at the taxi locomotor's speed: the aircraft turns in place until it points within π/6 of the next point, then drives
    /// straight at it (<c>BasicJetTaxiLocomotor</c> brakes at once, so the last step lands on the point). True once the last point is reached; the aircraft then stands exactly on it.
    /// </summary>
    private bool TaxiAlongPath(IWorldServices world)
    {
        if (_pathIndex >= _path.Count) return true;
        var taxi = ActiveLocomotor;
        var target = _path[_pathIndex];
        var last = _pathIndex == _path.Count - 1;
        // Travel and turn of one logic frame of the original (the 30 Hz step; Update flies it in shares per tick).
        var speed = FallbackTaxiSpeedPerFrame;
        var turn = Fixed.Zero;
        if (taxi is not null) FlightFrameConstants(world.Time, taxi, out speed, out _, out _, out turn, out _);
        var dx = target.X - Entity.X;
        var dy = target.Y - Entity.Y;
        var distance = Fixed.Sqrt(dx * dx + dy * dy);
        if (distance <= (last ? Epsilon : Fixed.Max(speed, Fixed.One)))
        {
            Entity.X = target.X;
            Entity.Y = target.Y;
            Entity.Z = target.Z;
            SetFlightFrameSpeed(world.Time, Fixed.Zero);
            _pathIndex++;
            world.MarkDirty(Entity);
            return _pathIndex >= _path.Count;
        }
        var desired = FixedTrig.Atan2(dy, dx);
        var difference = FixedTrig.AngleDifference(Entity.Angle, desired);
        Entity.Angle = turn > Fixed.Zero && difference.Abs() > turn ? FixedTrig.Normalize(Entity.Angle + (difference.IsNegative ? -turn : turn)) : desired;
        var aligned = FixedTrig.AngleDifference(Entity.Angle, desired).Abs() <= Misaligned;
        var step = aligned ? Fixed.Min(speed, distance) : Fixed.Zero;
        SetFlightFrameSpeed(world.Time, step);
        Entity.X += FixedTrig.Cos(Entity.Angle) * step;
        Entity.Y += FixedTrig.Sin(Entity.Angle) * step;
        Entity.Z = SurfaceHere(world);
        world.MarkDirty(Entity);
        return false;
    }

    /// <summary>Turns in place by the current locomotor's turn of one logic frame of the original; true when the heading equals <paramref name="orientation"/> (within the reference's 0.001 rad).</summary>
    private bool TurnTo(SimulationTime time, Fixed orientation)
    {
        var difference = FixedTrig.AngleDifference(Entity.Angle, orientation);
        if (difference.Abs() <= ParkedOrientationTolerance) return true;
        var turn = Fixed.Zero;
        if (ActiveLocomotor is { } locomotor) FlightFrameConstants(time, locomotor, out _, out _, out _, out turn, out _);
        Entity.Angle = turn > Fixed.Zero && difference.Abs() > turn ? FixedTrig.Normalize(Entity.Angle + (difference.IsNegative ? -turn : turn)) : orientation;
        return FixedTrig.AngleDifference(Entity.Angle, orientation).Abs() <= ParkedOrientationTolerance;
    }

    // ------------------------------------------------------------------------------------------------------------------ orders

    /// <summary><c>aiDoCommand</c> for a move: stored while a take-off or landing runs or the aircraft is parked (which starts the take-off), carried out at once while it flies.</summary>
    public override void OrderMove(IWorldServices world, Fixed x, Fixed y, bool fromPlayer, bool attackMove = false)
    {
        if (TakeoffOrLandingInProgress)
        {
            // The stored order replaces whatever was stored, a guard included.
            world.EndGuard(Entity);
            Store(PendingKind.Move, x, y, fromPlayer, EntityId.Invalid, attackMove);
            return;
        }
        if (!_allowAir)
        {
            world.EndGuard(Entity);
            Store(PendingKind.Move, x, y, fromPlayer, EntityId.Invalid, attackMove);
            BeginTakeoff();
            return;
        }
        _pending = PendingKind.None;
        _returnTick = 0;
        _phase = JetPhase.None;
        _useReturnLocomotor = false;
        base.OrderMove(world, x, y, fromPlayer, attackMove);
    }

    /// <summary><c>privateEnter</c>: entering an airfield means landing there; anything else cannot be entered by an aircraft.</summary>
    public override void OrderEnter(IWorldServices world, Entity container, bool fromPlayer)
    {
        if (container.Template.IsKindOf("FS_AIRFIELD") && Array.Find(container.ModuleArray, module => module is ParkingPlaceBehavior) is not null) OrderLand(world, container, fromPlayer);
    }

    /// <summary><c>doLandingCommand</c>: land at <paramref name="airfield"/> (taking off first when parked somewhere else).</summary>
    public void OrderLand(IWorldServices world, Entity airfield, bool fromPlayer)
    {
        if (_phase is JetPhase.AwaitLanding or JetPhase.Landing) return;
        if (airfield.EffectivelyDead || !airfield.Template.IsKindOf("FS_AIRFIELD") || world.RelationshipOf(Entity.Owner, airfield.Owner) != Relationship.Allies) return;
        if (TakeoffOrLandingInProgress)
        {
            world.EndGuard(Entity);
            Store(PendingKind.Land, Fixed.Zero, Fixed.Zero, fromPlayer, airfield.Id);
            return;
        }
        if (!_allowAir)
        {
            // isParkedAt: already standing at that airfield, nothing to do.
            if (_airfield == airfield.Id) return;
            world.EndGuard(Entity);
            Store(PendingKind.Land, Fixed.Zero, Fixed.Zero, fromPlayer, airfield.Id);
            BeginTakeoff();
            return;
        }
        world.EndGuard(Entity);
        BeginLanding(world, airfield);
    }

    /// <summary>
    /// <c>aiDoCommand</c> for a guard (<c>AICMD_GUARD_POSITION</c>, <c>_OBJECT</c>, <c>_AREA</c>, <c>_RETALIATE</c>): stored while a take-off or landing runs or the aircraft is parked
    /// (which starts the take-off) - false, the world holds the guard until <see cref="ExecutePending"/> gives it again; while the aircraft flies the guard begins at once - true.
    /// </summary>
    public bool TakeGuardNow(IWorldServices world)
    {
        if (TakeoffOrLandingInProgress)
        {
            Store(PendingKind.Guard, Fixed.Zero, Fixed.Zero, true, EntityId.Invalid);
            return false;
        }
        if (!_allowAir)
        {
            Store(PendingKind.Guard, Fixed.Zero, Fixed.Zero, true, EntityId.Invalid);
            BeginTakeoff();
            return false;
        }
        _pending = PendingKind.None;
        _returnTick = 0;
        _phase = JetPhase.None;
        _useReturnLocomotor = false;
        return true;
    }

    private void Store(PendingKind kind, Fixed x, Fixed y, bool fromPlayer, EntityId target, bool attackMove = false)
    {
        _pending = kind;
        _pendingAttackMove = attackMove;
        _pendingX = x;
        _pendingY = y;
        _pendingFromPlayer = fromPlayer;
        _pendingTarget = target;
    }

    private void BeginTakeoff()
    {
        // A parked aircraft with nothing to do looks once per logic frame; an order cuts that frame short (there is nothing of it to fly), so the take-off starts in the tick the order is given.
        if (_phase == JetPhase.None) _frame.Cancel();
        StopInternalMove();
        Enter(JetPhase.AwaitTakeoff);
    }

    private void BeginLanding(IWorldServices world, Entity airfield)
    {
        if (Array.Find(airfield.ModuleArray, module => module is ParkingPlaceBehavior) is not ParkingPlaceBehavior parking) return;
        if (!HelipadKind && !parking.ReserveSpace(world, Entity, data.ParkingOffset, out _)) return;
        if (ParkingOf(world) is { } old && !ReferenceEquals(old, parking)) old.ReleaseSpace(world, Entity);
        // JetAIUpdate's landing moves the aircraft's goal along its path to the pad (updateGoal): the goal it held in the field is free from now on.
        Entity.Goal = null;
        _airfield = airfield.Id;
        _pending = PendingKind.None;
        _useReturnLocomotor = false;
        BeginReturnForLanding(world);
    }

    // ------------------------------------------------------------------------------------------------------------------ the update

    /// <inheritdoc/>
    public override void StopForDisable(IWorldServices world)
    {
        SetFlightFrameSpeed(world.Time, Fixed.Zero);
        world.MarkDirty(Entity);
    }

    public override long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        // The first frame of a new aircraft begins one logic frame of the original after it appeared (at 30 Hz that is the next tick): its frames are those of whatever created it
        // in the first tick of its own frame, and its state after every frame is the 30 Hz state (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3, rule 7).
        var stride = world.Time.TicksPerOriginalFrame;
        if (stride > 1 && world.Tick < Entity.SpawnTick + stride) return SimulationTiming.SleepNone;
        if (Entity.IsDisabledByTime(world.Tick) || (Entity.ScriptDisableMask & Entity.ScriptHeld) != 0)
        {
            SetFlightFrameSpeed(world.Time, Fixed.Zero);
            return SimulationTiming.SleepNone;
        }
        UpdateAttackLocomotor(world);
        UpdateAttackersMiss(world);
        // The airfield states are the reference's state machine, which steps once per logic frame: a state change costs a frame, and what a state moves (taxi, turn, take-off run,
        // landing) is the 30 Hz step, decided once per frame and flown in shares per tick. Between two decisions only the rest of the decided frame is flown.
        if (_frame.Continue(Entity))
        {
            if (_phase == JetPhase.CirclingDeadAirfield) DamageWhileCircling(world);
            world.MarkDirty(Entity);
            return FlyOn(world, false, default);
        }
        var parking = ParkingOf(world);
        var before = (Entity.X, Entity.Y, Entity.Z, Entity.Angle);

        switch (_phase)
        {
            case JetPhase.None:
                IdleLogic(world, parking);
                break;
            case JetPhase.TaxiFromHangar:
                if (parking is null) { Enter(JetPhase.None); _allowAir = true; break; }
                LookForABetterSpace(world, parking);
                if (TaxiAlongPath(world)) Enter(JetPhase.OrientForParking);
                break;
            case JetPhase.AwaitTakeoff:
                AwaitTakeoff(world, parking);
                break;
            case JetPhase.TaxiToTakeoff:
                if (parking is null) { Enter(JetPhase.PauseBeforeTakeoff); break; }
                if (TaxiAlongPath(world)) EnterPause(world, parking);
                break;
            case JetPhase.PauseBeforeTakeoff:
                PauseBeforeTakeoff(world, parking);
                break;
            case JetPhase.TakingOff:
                TakeOff(world, parking);
                break;
            case JetPhase.ReturningForLanding:
                if (parking is null) BeginReturnForLanding(world);
                else if (!Entity.HasMoveTarget) Enter(JetPhase.AwaitLanding);
                break;
            case JetPhase.AwaitLanding:
                AwaitLanding(world, parking);
                break;
            case JetPhase.Landing:
                Land(world, parking);
                break;
            case JetPhase.TaxiFromLanding:
                if (parking is null) { BeginDeadAirfield(); break; }
                LookForABetterSpace(world, parking);
                if (TaxiAlongPath(world)) Enter(JetPhase.OrientForParking);
                break;
            case JetPhase.OrientForParking:
                Orient(world, parking);
                break;
            case JetPhase.ReloadAmmo:
                Reload(world, parking);
                break;
            case JetPhase.ReturnToDeadAirfield:
                if (!Entity.HasMoveTarget) EnterCircling(world);
                break;
            case JetPhase.CirclingDeadAirfield:
                CircleDeadAirfield(world);
                break;
        }

        if (_allowAir && _phase is JetPhase.None or JetPhase.ReturningForLanding or JetPhase.ReturnToDeadAirfield or JetPhase.CirclingDeadAirfield) ControlHeight(world);
        // At 30 Hz a frame is a tick: there is nothing to spread and the step is not measured.
        var step = world.Time.TicksPerOriginalFrame > 1
            ? (Entity.X - before.X, Entity.Y - before.Y, Entity.Z - before.Z, FixedTrig.AngleDifference(before.Angle, Entity.Angle))
            : default;
        // Every decision takes a frame, the look of a parked aircraft with nothing to do included (what it starts by itself - a stored order, the take-off of a healed
        // helicopter - starts a frame after the state before it, as at 30 Hz); an order to a parked aircraft ends that frame (BeginTakeoff).
        return FlyOn(world, true, step);
    }

    /// <summary>
    /// What runs every tick after the states: the circling and the move order of the flight. With <paramref name="decided"/> the states have just applied the step of a
    /// whole logic frame of the original (<paramref name="step"/>); it is spread over the frame's ticks last, so that everything before sees the object where the 30 Hz step puts it.
    /// </summary>
    private long FlyOn(IWorldServices world, bool decided, (Fixed X, Fixed Y, Fixed Z, Fixed Turn) step)
    {
        // A fighter cannot stop in the air: with no destination (and while it waits for the runway) it circles.
        var circles = ShouldCircle(_phase);
        var moves = _phase is JetPhase.None or JetPhase.ReturningForLanding or JetPhase.ReturnToDeadAirfield;
        // A frame of winged flight is flown to its end by whichever of the two steers. When neither does in this tick, the aircraft still flies the rest of the frame (its order ended in the
        // middle of it) unless a state has taken the movement over (the landing began, the aircraft is on the ground): then the rest is dropped.
        if (WingedStepRunning && !circles && !(moves && Entity.HasMoveTarget && ActiveLocomotor is { } wings && FliesWithWings(wings)))
        {
            if (_allowAir && (moves || _phase is JetPhase.AwaitLanding or JetPhase.CirclingDeadAirfield)) ContinueWingedStep();
            else CancelWingedStep();
        }
        if (circles) CircleStep(world);
        // Forgotten where a frame is decided only: an order that ends in this frame's decision names the point to circle (OnWingedArrival), and the ticks that fly the rest of
        // that frame must not drop it before the next decision circles it.
        else if (decided) _circling = false;
        if (moves) base.Update(world);
        if (decided) _frame.Begin(world.Time, Entity, step.X, step.Y, step.Z, step.Turn);
        if (world.Time.TicksPerOriginalFrame > 1)
        {
            // Observers read the aircraft where the frame it has begun ends: the rest of the states' step (height, taxi, take-off, landing) with that of the winged flight or of
            // the helicopter's movement.
            var winged = WingedStepRest;
            var ground = GroundStepRest(world.Tick);
            _frame.Publish(world.Time, world.Tick, Entity, winged.X + ground.X, winged.Y + ground.Y);
            // And the heading its turn of this frame ends with: a missile leaves a turning fighter with the heading of the 30 Hz step.
            if (WingedStepTurnsTo(out var heading)) Entity.SetAheadAngle(heading);
        }
        return SimulationTiming.SleepNone;
    }

    private void UpdateAttackLocomotor(IWorldServices world)
    {
        if (data.AttackLocomotor == "SET_NORMAL") return;
        var now = world.Tick;
        // Own minimum (the reference has none): the attack set lasts at least one frame of the original, because 0 means "not attacking" here.
        if (Entity.LastFireTick == now) _attackExpireTick = now + world.Time.AtLeastOneFrame(data.AttackLocomotorPersistTicks);
        else if (_attackExpireTick != 0 && now >= _attackExpireTick) _attackExpireTick = 0;
    }

    /// <summary>
    /// <c>JetAIUpdate::update</c>: "if (jet->testStatus(OBJECT_STATUS_IS_ATTACKING))" the attackers miss until <c>AttackersMissPersistTime</c> after it, else the time runs out. Asked once per logic
    /// frame of the original of the aircraft, at its first tick, as the original's update is.
    /// </summary>
    private void UpdateAttackersMiss(IWorldServices world)
    {
        if (data.AttackersMissPersistTicks == 0 || !world.Time.BeginsFrameOf(Entity, world.Tick)) return;
        if (Entity.Acquisition is { Victim.IsValid: true } || Entity.AttackPlace is not null) _attackersMissExpireTick = world.Tick + data.AttackersMissPersistTicks;
        else if (_attackersMissExpireTick != 0 && world.Tick >= _attackersMissExpireTick) _attackersMissExpireTick = 0;
    }

    /// <summary>
    /// <c>getSneakyTargetingOffset</c>: while the attackers miss, where they aim - <c>SneakyOffsetWhenAttacking</c> along the aircraft's heading (behind it, the data's offset is negative).
    /// </summary>
    internal bool SneakyOffset(long tick, out Fixed dx, out Fixed dy)
    {
        (dx, dy) = (Fixed.Zero, Fixed.Zero);
        if (_attackersMissExpireTick == 0 || tick >= _attackersMissExpireTick) return false;
        var angle = Entity.FrameEndAngle(tick);
        (dx, dy) = (FixedTrig.Cos(angle) * data.SneakyOffset, FixedTrig.Sin(angle) * data.SneakyOffset);
        return true;
    }

    /// <summary>The idle branch of <c>JetAIUpdate::update</c>: healing, the ammunition return, the stored order and the return after <c>ReturnToBaseIdleTime</c>.</summary>
    private void IdleLogic(IWorldServices world, ParkingPlaceBehavior? parking)
    {
        var now = world.Tick;
        // A held guard is the stored order of an idle aircraft (HAS_PENDING_COMMAND), not a state it is busy with.
        if (!base.IsIdle && !world.IsGuardHeld(Entity))
        {
            parking?.SetHealee(world, Entity, false);
            _returnTick = 0;
            // ALLOW_INTERRUPT_AND_RESUME_OF_CUR_STATE_FOR_RELOAD: a guard whose magazine is empty is stored, the aircraft flies home to reload and gives it again afterwards.
            if (_allowAir && world.GuardResumesAfterReload(Entity) && IsOutOfSpecialReloadAmmo(world))
            {
                world.HoldGuard(Entity);
                Store(PendingKind.Guard, Fixed.Zero, Fixed.Zero, true, EntityId.Invalid);
                _useReturnLocomotor = true;
                BeginReturnForLanding(world);
            }
            return;
        }
        if (parking is not null)
        {
            if (!_allowAir && _pending == PendingKind.None && HelipadKind && Entity.Body is { } body && body.Health == body.MaxHealth)
            {
                // a healed helicopter takes off again by itself
                parking.SetHealee(world, Entity, false);
                BeginTakeoff();
                // GameFix.HealedHelicopterToRally: and flies to its airfield's rally point, as a move stored for after the take-off (aiDoCommand during TAKEOFF_IN_PROGRESS).
                if (world.Templates.Rules.Has(Rules.GameFix.HealedHelicopterToRally) && world.FindEntity(_airfield) is { } producer)
                    foreach (var module in producer.Modules)
                        if (module is ProductionUpdate production && production.TryGetRally(out var rallyX, out var rallyY))
                        {
                            Store(PendingKind.Move, rallyX, rallyY, fromPlayer: false, EntityId.Invalid);
                            break;
                        }
                return;
            }
            parking.SetHealee(world, Entity, !_allowAir);
        }
        if (IsOutOfSpecialReloadAmmo(world) && _allowAir)
        {
            _returnTick = 0;
            _useReturnLocomotor = true;
            BeginReturnForLanding(world);
        }
        else if (_pending != PendingKind.None) ExecutePending(world);
        else if (_returnTick != 0 && now >= _returnTick && _allowAir)
        {
            _returnTick = 0;
            _useReturnLocomotor = false;
            BeginReturnForLanding(world);
        }
        else if (_returnTick == 0 && data.ReturnToBaseIdleTicks > 0 && _allowAir) _returnTick = now + data.ReturnToBaseIdleTicks;
    }

    private void ExecutePending(IWorldServices world)
    {
        if (!_allowAir)
        {
            BeginTakeoff();
            return;
        }
        var kind = _pending;
        var target = _pendingTarget;
        var (x, y, fromPlayer, attackMove) = (_pendingX, _pendingY, _pendingFromPlayer, _pendingAttackMove);
        _pending = PendingKind.None;
        _pendingAttackMove = false;
        _returnTick = 0;
        if (kind == PendingKind.Move) base.OrderMove(world, x, y, fromPlayer, attackMove);
        else if (kind == PendingKind.Land && world.FindEntity(target) is { } airfield) BeginLanding(world, airfield);
        else if (kind == PendingKind.Guard) world.ResumeGuard(Entity);
    }

    // ------------------------------------------------------------------------------------------------------------------ take-off

    private void AwaitTakeoff(IWorldServices world, ParkingPlaceBehavior? parking)
    {
        if (parking is null) { Enter(JetPhase.TaxiToTakeoff); return; }
        if (Helicopter)
        {
            // HeliAIStateMachine: TAKING_OFF_AWAIT_CLEARANCE is a SuccessState.
            Enter(JetPhase.TakingOff);
            return;
        }
        if (!parking.ReserveSpace(world, Entity, data.ParkingOffset, out _)) { Enter(JetPhase.None); return; }
        if (!parking.ReserveRunway(world, Entity.Id, false))
        {
            // "If we're trying to take off an aircraft carrier and fail to reserve a runway, it's because we need to be at the front of the carrier queue."
            if (!OnDeck) return;
            if (parking.CalcBestParkingAssignment(world, Entity.Id, out var best))
            {
                _path.Clear();
                _pathIndex = 0;
                _path.Add(best);
            }
            TaxiAlongPath(world);
            return;
        }
        if (!parking.TryGetInfo(world, Entity.Id, out var info)) { Enter(JetPhase.None); return; }
        _leg = info;
        Enter(JetPhase.TaxiToTakeoff);
        // JetOrHeliTaxiState FROM_PARKING: the taxi point, then the runway start (the taxi point only when it had to queue); on a deck the front space is the runway start.
        _path.Add(info.RunwayPrep);
        if (info.RunwayStart.X != info.RunwayPrep.X || info.RunwayStart.Y != info.RunwayPrep.Y) _path.Add(info.RunwayStart);
    }

    private void EnterPause(IWorldServices world, ParkingPlaceBehavior parking)
    {
        Enter(JetPhase.PauseBeforeTakeoff);
        _whenTick = 0;
        _transferTick = 0;
        _waitedFor = EntityId.Invalid;
        _resetTimer = false;
        if (parking.TryGetInfo(world, Entity.Id, out var info)) _leg = info;
    }

    private void PauseBeforeTakeoff(IWorldServices world, ParkingPlaceBehavior? parking)
    {
        if (parking is null) { Enter(JetPhase.TakingOff); return; }
        var now = world.Tick;
        // AIFaceState towards the runway end.
        var facing = TurnTo(world.Time, FixedTrig.Atan2(_leg.RunwayEnd.Y - Entity.Y, _leg.RunwayEnd.X - Entity.X));
        world.MarkDirty(Entity);
        if (FindWaiter(world, parking)) return;
        if (!_resetTimer)
        {
            // everyone else is ready: the countdown (re)starts.
            _whenTick = now + data.TakeoffPauseTicks;
            // Order, not time: after the next step of the states, or after the jet that was waited for had its step. The states step once per logic frame of the original.
            if (!_waitedFor.IsValid)
            {
                _waitedFor = Entity.Id;
                _transferTick = now + world.Time.OriginalFrames(TickOrder.Next);
            }
            else _transferTick = now + world.Time.OriginalFrames(2 * TickOrder.Next);
            _resetTimer = true;
        }
        if (now >= _transferTick) parking.TransferRunwayToNext(world, Entity.Id);
        if (now >= _whenTick && facing) Enter(JetPhase.TakingOff);
    }

    /// <summary>
    /// <c>JetPauseBeforeTakeoffState::findWaiter</c>: another aircraft holds a runway of this airfield and is still taxiing to take off. Added because the physical spacing of the
    /// reference (the next aircraft needs longer to taxi than the runway run takes) does not exist in the computed layout: it also waits while an aircraft of the same runway is
    /// still on the runway part of its take-off.
    /// </summary>
    private bool FindWaiter(IWorldServices world, ParkingPlaceBehavior parking)
    {
        var column = parking.SpaceOf(Entity.Id) % Math.Max(parking.Data.Cols, 1);
        if (world.Templates.Rules.Has(Rules.GameFix.TakeoffWaitsForOneJet))
        {
            // The rule fix takeoff-waits-for-one-jet (findJetToWaitFor): the first jet taxiing on another runway; it is waited for only if it is the one saved first.
            for (var runway = 0; runway < parking.RunwayCount; runway++)
            {
                if (runway == column) continue;
                var other = world.FindEntity(parking.RunwayReservation(world, runway));
                if (other is null || other == Entity) continue;
                if (Array.Find(other.ModuleArray, module => module is JetAIUpdate) is not JetAIUpdate jet || jet._phase != JetPhase.TaxiToTakeoff) continue;
                if (!_waitedFor.IsValid) _waitedFor = other.Id;
                if (_waitedFor == other.Id) return true;
                break;
            }
            return SameRunwayStillRolling(world, parking, column);
        }
        for (var runway = 0; runway < parking.RunwayCount; runway++)
        {
            var other = world.FindEntity(parking.RunwayReservation(world, runway));
            if (other is null || other == Entity) continue;
            if (Array.Find(other.ModuleArray, module => module is JetAIUpdate) is not JetAIUpdate jet || jet._phase != JetPhase.TaxiToTakeoff) continue;
            if (!_waitedFor.IsValid) _waitedFor = other.Id;
            return true;
        }
        return SameRunwayStillRolling(world, parking, column);
    }

    private bool SameRunwayStillRolling(IWorldServices world, ParkingPlaceBehavior parking, int column)
    {
        for (var space = 0; space < parking.SpaceCount; space++)
        {
            if (space % Math.Max(parking.Data.Cols, 1) != column || world.FindEntity(parking.OccupantOf(space)) is not { } other || other == Entity) continue;
            if (Array.Find(other.ModuleArray, module => module is JetAIUpdate) is JetAIUpdate { _phase: JetPhase.TakingOff, _pathIndex: 0 }) return true;
        }
        return false;
    }

    /// <summary><c>JetTakeoffOrLandingState</c> (jets: along the runway, rising with the square of the covered distance) and <c>HeliTakeoffOrLandingState</c> (helicopters: straight up).</summary>
    private void TakeOff(IWorldServices world, ParkingPlaceBehavior? parking)
    {
        var flight = Entity.Template.Locomotor;
        var helicopter = Helicopter;
        if (_path.Count == 0)
        {
            _allowAir = true;
            _useReturnLocomotor = false;
            if (parking is null || flight is null) { Enter(JetPhase.None); return; }
            if (helicopter)
                _path.Add(new Point3(Entity.X, Entity.Y, parking.ApronHeight(world, Entity.X, Entity.Y) + parking.Data.ApproachHeight));
            else
            {
                if (!parking.TryGetInfo(world, Entity.Id, out var info) && !parking.ReserveSpace(world, Entity, data.ParkingOffset, out info)) { Enter(JetPhase.None); return; }
                _leg = info;
                _path.Add(info.RunwayEnd);
                _path.Add(info.RunwayExit);
            }
            _pathIndex = 0;
            _verticalSpeed = Fixed.Zero;
            SetFlightFrameSpeed(world.Time, Fixed.Zero);
        }
        if (parking is null || flight is null) { FinishTakeoff(world, null); return; }
        // Everything in units of one logic frame of the original, with the constants the 30 Hz step computes with.
        FlightFrameConstants(world.Time, flight, out var topSpeed, out var acceleration, out _, out _, out _);
        if (helicopter)
        {
            var rise = _path[0];
            FlightVerticalConstants(world.Time, flight, out var limitZ, out _);
            var climb = Fixed.Max(limitZ, topSpeed / Fixed.FromInt(4));
            Entity.Z = Fixed.Min(Entity.Z + climb, rise.Z);
            world.MarkDirty(Entity);
            if (Entity.Z >= rise.Z) FinishTakeoff(world, parking);
            return;
        }
        parking.TransferRunwayToNext(world, Entity.Id);
        // The run in units of one logic frame of the original: speed and acceleration of a frame, the point is reached within one frame of travel.
        var step = Fixed.Max(topSpeed, Fixed.One);
        var target = _path[_pathIndex];
        var toTarget = AirModules.Distance2D(Entity.X, Entity.Y, target.X, target.Y);
        if (toTarget <= step)
        {
            if (_pathIndex == _path.Count - 1)
            {
                Entity.X = target.X;
                Entity.Y = target.Y;
                FinishTakeoff(world, parking);
                return;
            }
            _pathIndex++;
            target = _path[_pathIndex];
        }
        Entity.Angle = FixedTrig.Atan2(target.Y - Entity.Y, target.X - Entity.X);
        var speed = Fixed.Min(topSpeed, FlightFrameSpeed(world.Time) + acceleration);
        SetFlightFrameSpeed(world.Time, speed);
        Entity.X += FixedTrig.Cos(Entity.Angle) * speed;
        Entity.Y += FixedTrig.Sin(Entity.Angle) * speed;
        // Lift of the reference: m_maxLift · ratio², ratio the covered share of the runway. Here the height is that share squared of the approach height.
        var ratio = Fixed.One;
        if (_pathIndex == 0 && _leg.RunwayTakeoffDistance > Fixed.Zero)
        {
            var remaining = AirModules.Distance2D(Entity.X, Entity.Y, _leg.RunwayEnd.X, _leg.RunwayEnd.Y);
            ratio = Fixed.Min(Fixed.One, Fixed.Max(Fixed.Zero, Fixed.One - remaining / _leg.RunwayTakeoffDistance));
        }
        Entity.Z = parking.ApronHeight(world, Entity.X, Entity.Y) + parking.Data.ApproachHeight * ratio * ratio;
        world.MarkDirty(Entity);
    }

    private void FinishTakeoff(IWorldServices world, ParkingPlaceBehavior? parking)
    {
        Enter(JetPhase.None);
        _allowAir = true;
        _returnTick = 0;
        if (parking is not null)
        {
            parking.ReleaseRunway(world, Entity.Id);
            if (!HelipadKind && !data.KeepsParkingSpaceWhenAirborne) parking.ReleaseSpace(world, Entity);
            parking.SetHealee(world, Entity, false);
        }
        // The stored order is carried out at once (the reference does it in the next idle update): no tick in which the aircraft looks idle in the air.
        if (_pending != PendingKind.None) ExecutePending(world);
    }

    // ------------------------------------------------------------------------------------------------------------------ return and landing

    private void BeginReturnForLanding(IWorldServices world)
    {
        StopInternalMove();
        var parking = ParkingOf(world);
        if (parking is null)
        {
            // "nuke the producer id, since it's dead", then look for another airfield.
            _airfield = EntityId.Invalid;
            parking = FindSuitableAirfield(world);
            if (parking is null)
            {
                BeginDeadAirfield();
                return;
            }
            _airfield = parking.Entity.Id;
        }
        ParkingInfo info = default;
        if (!HelipadKind && !parking.ReserveSpace(world, Entity, data.ParkingOffset, out info))
        {
            BeginDeadAirfield();
            return;
        }
        _leg = info;
        Enter(JetPhase.ReturningForLanding);
        var goal = HelipadKind ? parking.HeliPark(world) : data.NeedsRunway ? info.RunwayApproach : info.ParkingSpace;
        StartInternalMove(goal.X, goal.Y);
    }

    /// <summary><c>findSuitableAirfield</c>: the closest allied, complete, living airfield with a free space.</summary>
    private ParkingPlaceBehavior? FindSuitableAirfield(IWorldServices world)
    {
        ParkingPlaceBehavior? best = null;
        var bestDistance = Fixed.MaxValue;
        foreach (var candidate in world.Entities.OrderBy(value => value.Id.Value))
        {
            if (candidate.EffectivelyDead || candidate.DestroyRequested || !candidate.Template.IsKindOf("FS_AIRFIELD")) continue;
            if (candidate.HasStatus(ObjectStatusBits.UnderConstruction) || candidate.HasStatus(ObjectStatusBits.Sold)) continue;
            if (world.RelationshipOf(Entity.Owner, candidate.Owner) != Relationship.Allies) continue;
            if (Array.Find(candidate.ModuleArray, module => module is ParkingPlaceBehavior) is not ParkingPlaceBehavior parking || !parking.IsReady) continue;
            if (!HelipadKind && !parking.HasFreeSpaceOrOwns(Entity.Id)) continue;
            var dx = candidate.X - Entity.X;
            var dy = candidate.Y - Entity.Y;
            var distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = parking;
        }
        return best;
    }

    private void BeginDeadAirfield()
    {
        StopInternalMove();
        Enter(JetPhase.ReturnToDeadAirfield);
        StartInternalMove(_homeX, _homeY);
    }

    private void EnterCircling(IWorldServices world)
    {
        // JetOrHeliCirclingDeadAirfieldState::onEnter: an aircraft that merely idled (not out of ammunition) and never had an airfield just goes back to idle.
        if (!IsOutOfSpecialReloadAmmo(world) && !_airfield.IsValid)
        {
            Enter(JetPhase.None);
            _useReturnLocomotor = false;
            return;
        }
        Enter(JetPhase.CirclingDeadAirfield);
        _checkAirfieldTick = world.Tick + world.Time.TicksPerSecond;
    }

    /// <summary>The damage of an aircraft that circles its dead airfield: a share per second, taken every tick.</summary>
    private void DamageWhileCircling(IWorldServices world)
    {
        if (data.OutOfAmmoDamageRatePerSecond <= Fixed.Zero || Entity.Body is not { } body) return;
        var amount = world.Time.PerSecondToPerTick(data.OutOfAmmoDamageRatePerSecond * body.MaxHealth);
        world.Damage(Entity.Id, new DamageInfo(EntityId.Invalid, amount, DamageTypes.Unresistable, DeathTypes.Normal, false));
    }

    private void CircleDeadAirfield(IWorldServices world)
    {
        DamageWhileCircling(world);
        if (world.Tick < _checkAirfieldTick) return;
        _checkAirfieldTick = world.Tick + world.Time.TicksPerSecond;
        var parking = FindSuitableAirfield(world);
        if (parking is null) return;
        _airfield = parking.Entity.Id;
        // the new airfield returns the aircraft (idle + out of ammunition, or idle too long): the idle logic starts the landing again.
        Enter(JetPhase.None);
    }

    private void AwaitLanding(IWorldServices world, ParkingPlaceBehavior? parking)
    {
        if (parking is null) { BeginReturnForLanding(world); return; }
        ParkingInfo info = default;
        if (!HelipadKind && !parking.ReserveSpace(world, Entity, data.ParkingOffset, out info)) { Enter(JetPhase.None); return; }
        _leg = info;
        if (Helicopter)
        {
            Enter(JetPhase.Landing);
            return;
        }
        if (!parking.ReserveRunway(world, Entity.Id, true)) return; // the winged aircraft circles meanwhile (ShouldCircle)
        // JetTakeoffOrLandingState: over the approach point onto the end of the runway, then the roll-out to its start. The state changes in the frame of the clearance and the
        // aircraft flies on: no frame of the flight stands still for it.
        _verticalSpeed = Fixed.Zero;
        Enter(JetPhase.Landing);
        _path.Add(info.RunwayApproach);
        // "Assigned to an aircraft carrier which has separate landing strips."
        _path.Add(OnDeck ? info.RunwayLandingStart : info.RunwayEnd);
        _path.Add(OnDeck ? info.RunwayLandingEnd : info.RunwayStart);
        Land(world, parking);
    }

    /// <summary>
    /// <c>Locomotor::calcLiftToUseAtPt</c> in the landing's ultra-accurate mode with <c>PRECISE_Z_POS</c> (<c>JetTakeoffOrLandingState::onEnter</c>): the height of the next point of the path is the goal, the
    /// lift is limited by the locomotor's and brakes in time ("see how far we need to slow to dead stop"), and <c>PhysicsBehavior::update</c> adds gravity and puts the aircraft on the ground.
    /// One step of a logic frame of the original, in units of that frame; <see cref="_verticalSpeed"/> is signed here.
    /// </summary>
    private void SinkTowards(IWorldServices world, LocomotorInfo flight, Fixed goalZ, Fixed ground)
    {
        FlightVerticalConstants(world.Time, flight, out var limitZ, out var grossLift);
        var gravity = FlightFrames.GravityPerFrameSquared(world.Templates);
        var speed = _verticalSpeed;
        var brake = Fixed.Max(Fixed.Zero, grossLift + gravity) * Fixed.FromInt(2);
        if (!speed.IsNegative) brake = -brake;
        var acceleration = Fixed.Zero;
        if (brake.Abs() > TinyLift)
        {
            var toGo = goalZ - Entity.Z;
            // The original's brake distance, "the old, looks-incorrect-to-me-but-works calc".
            if ((speed * speed / brake.Abs()).Abs() > toGo.Abs()) acceleration = brake;
            // Absent, the limit is the original's 999999: no limit.
            else if (limitZ > Fixed.Zero && speed.Abs() > limitZ) acceleration = limitZ - speed;
            else acceleration = (toGo - speed) * Fixed.FromInt(2);
        }
        var lift = Fixed.Max(-grossLift, Fixed.Min(grossLift * Fixed.FromInt(3), acceleration - gravity));
        _verticalSpeed = speed + lift + gravity;
        Entity.Z += _verticalSpeed;
        if (Entity.Z > ground) return;
        Entity.Z = ground;
        _verticalSpeed = Fixed.Zero;
    }

    private void Land(IWorldServices world, ParkingPlaceBehavior? parking)
    {
        var flight = Entity.Template.Locomotor;
        if (parking is null || flight is null) { BeginReturnForLanding(world); return; }
        // Speeds and their change in units of one logic frame of the original, with the constants the 30 Hz step computes with.
        FlightFrameConstants(world.Time, flight, out var topSpeed, out var acceleration, out var braking, out var turn, out var slowest);
        if (Helicopter)
        {
            // HeliTakeoffOrLandingState (landing): settle vertically on the helipad or the space.
            var spot = HelipadKind ? parking.HeliPark(world) : _leg.ParkingSpace;
            var toSpot = AirModules.Distance2D(Entity.X, Entity.Y, spot.X, spot.Y);
            if (toSpot > Fixed.Zero)
            {
                var move = Fixed.Min(toSpot, Fixed.Max(slowest, MinHelicopterApproachSpeedPerFrame));
                Entity.X += (spot.X - Entity.X) * move / toSpot;
                Entity.Y += (spot.Y - Entity.Y) * move / toSpot;
            }
            FlightVerticalConstants(world.Time, flight, out var limitZ, out _);
            var descend = Fixed.Max(limitZ, topSpeed / Fixed.FromInt(4));
            Entity.Z = Fixed.Max(spot.Z, Entity.Z - descend);
            SetFlightFrameSpeed(world.Time, Fixed.Zero);
            world.MarkDirty(Entity);
            if (toSpot <= Fixed.One && Entity.Z <= spot.Z)
            {
                Entity.X = spot.X;
                Entity.Y = spot.Y;
                Entity.Z = spot.Z;
                _allowAir = false;
                Enter(JetPhase.ReloadAmmo);
                BeginReload(world);
            }
            return;
        }
        var target = _path[_pathIndex];
        // Path::computeFlightDistToGoal: a point left behind on the way to the next one is passed; the aircraft does not go back for it.
        if (_pathIndex == 0 && (Entity.X - target.X) * (_path[1].X - target.X) + (Entity.Y - target.Y) * (_path[1].Y - target.Y) > Fixed.Zero) target = _path[++_pathIndex];
        var minSpeed = Fixed.Max(slowest, MinLandingSpeedPerFrame);
        var speed = FlightFrameSpeed(world.Time);
        // loco->setMaxSpeed(loco->getMinSpeed()): the landing is flown at the minimum speed.
        speed = speed > minSpeed
            ? Fixed.Max(minSpeed, speed - Fixed.Max(braking, acceleration))
            : Fixed.Min(minSpeed, speed + acceleration);
        SetFlightFrameSpeed(world.Time, speed);
        var distance = AirModules.Distance2D(Entity.X, Entity.Y, target.X, target.Y);
        var step = Fixed.Min(speed, distance);
        var airborne = _pathIndex < _path.Count - 1;
        if (airborne && distance > speed * SlideIntoPlaceFrames)
        {
            // setUltraAccurate: the flight turns at twice its rate ("monster turning ability") and slides into place from two frames away, so the arc does not orbit its point.
            var desired = FixedTrig.Atan2(target.Y - Entity.Y, target.X - Entity.X);
            var difference = FixedTrig.AngleDifference(Entity.Angle, desired);
            var limit = turn * 2;
            Entity.Angle = turn > Fixed.Zero && difference.Abs() > limit ? FixedTrig.Normalize(Entity.Angle + (difference.IsNegative ? -limit : limit)) : desired;
            Entity.X += FixedTrig.Cos(Entity.Angle) * step;
            Entity.Y += FixedTrig.Sin(Entity.Angle) * step;
        }
        else if (distance > Fixed.Zero)
        {
            if (!airborne) Entity.Angle = FixedTrig.Atan2(target.Y - Entity.Y, target.X - Entity.X);
            Entity.X += (target.X - Entity.X) * step / distance;
            Entity.Y += (target.Y - Entity.Y) * step / distance;
        }
        var ground = parking.ApronHeight(world, Entity.X, Entity.Y);
        // ParkingPlaceBehavior::calcPPInfo: the approach point lies ApproachHeight above the end of the runway.
        SinkTowards(world, flight, _pathIndex == 0 ? ground + parking.Data.ApproachHeight : ground, ground);
        world.MarkDirty(Entity);
        if (distance > Fixed.Max(step, Epsilon)) return;
        Entity.X = target.X;
        Entity.Y = target.Y;
        _pathIndex++;
        if (_pathIndex < _path.Count) return;
        // Rolled out at the runway start: stop, give the runway back, taxi to the space.
        SetFlightFrameSpeed(world.Time, Fixed.Zero);
        Entity.Z = ground;
        _allowAir = false;
        _useReturnLocomotor = false;
        parking.ReleaseRunway(world, Entity.Id);
        var info = _leg;
        Enter(JetPhase.TaxiFromLanding);
        if (Array.Find(Entity.ModuleArray, module => module is CountermeasuresBehavior) is CountermeasuresBehavior flares) flares.ReloadAtAirfield();
        // JetOrHeliTaxiState TO_PARKING: the taxi point, then the space; on a deck the runway's taxi points, then the best space there is.
        if (OnDeck)
        {
            _path.AddRange(parking.TaxiLocations(world, Entity.Id));
            parking.CalcBestParkingAssignment(world, Entity.Id, out var best);
            _path.Add(parking.TryGetInfo(world, Entity.Id, out var space) ? space.RunwayPrep : best);
            return;
        }
        _path.Add(info.RunwayPrep);
        _path.Add(info.ParkingSpace);
    }

    private void Orient(IWorldServices world, ParkingPlaceBehavior? parking)
    {
        if (parking is null) { Enter(JetPhase.None); return; }
        if (HelipadKind)
        {
            Enter(JetPhase.ReloadAmmo);
            BeginReload(world);
            return;
        }
        if (!parking.ReserveSpace(world, Entity, data.ParkingOffset, out var info)) { Enter(JetPhase.None); return; }
        _leg = info;
        if (OnDeck)
        {
            // "Magically position it correctly."
            Entity.X = info.RunwayPrep.X;
            Entity.Y = info.RunwayPrep.Y;
        }
        if (!TurnTo(world.Time, info.ParkingOrientation))
        {
            world.MarkDirty(Entity);
            return;
        }
        Entity.Angle = info.ParkingOrientation;
        SetFlightFrameSpeed(world.Time, Fixed.Zero);
        world.MarkDirty(Entity);
        Enter(JetPhase.ReloadAmmo);
        BeginReload(world);
    }

    // ------------------------------------------------------------------------------------------------------------------ reloading

    private void BeginReload(IWorldServices world)
    {
        // JetOrHeliReloadAmmoState::onEnter: the slowest weapon sets the time, "bias by amount empty"; a weapon without a clip never needs a reload.
        long total = 0;
        for (var slot = 0; slot < WeaponSlot.Count; slot++)
        {
            var index = SimulationWorld.WeaponIndexIn(Entity, slot);
            if (index < 0) continue;
            var weapon = world.Weapons[index];
            var clip = Entity.ClipOf(slot);
            if (weapon.ClipSize <= 0 || clip == int.MaxValue) continue;
            var time = weapon.ClipReloadWith(world.Time, world.Templates.GlobalWeaponBonus, Entity.WeaponBonusConditions);
            // rt = (rt * needed) / clipSize is cut off in frames of the original, then scaled.
            total = Math.Max(total, world.Time.OriginalFrames(world.Time.ToOriginalFrames(time) * (weapon.ClipSize - Math.Max(clip, 0)) / weapon.ClipSize));
        }
        // JetOrHeliReloadAmmoState::onEnter: if (m_reloadTime < 1) m_reloadTime = 1 – one frame of the original.
        _reloadDoneTick = world.Tick + world.Time.AtLeastOneFrame(total);
        _useReturnLocomotor = false;
    }

    private void Reload(IWorldServices world, ParkingPlaceBehavior? parking)
    {
        parking?.SetHealee(world, Entity, !_allowAir);
        if (world.Tick < _reloadDoneTick) return;
        world.ReloadAllAmmo(Entity);
        _returnTick = 0;
        // Idle again: a stored order (given while it reloaded) is carried out by IdleLogic.
        Enter(JetPhase.None);
    }

    // ------------------------------------------------------------------------------------------------------------------ flight height

    /// <summary>
    /// Kinematic stand-in for the physics locomotor's height control: an aircraft that may fly climbs to <c>ground + PreferredHeight</c> with the locomotor's lift net of gravity (limited to a
    /// half of its speed) and sinks at the larger of <c>SpeedLimitZ</c> and a quarter of its speed. One step of a logic frame of the original: it is called when the states decide
    /// and its change of height is flown in shares per tick with the rest of the frame's step.
    /// </summary>
    private void ControlHeight(IWorldServices world)
    {
        var locomotor = ActiveLocomotor;
        if (locomotor is null || locomotor.PreferredHeight <= Fixed.Zero) return;
        var target = GroundHere(world) + locomotor.PreferredHeight;
        var time = world.Time;
        if (Entity.Z < target)
        {
            // Everything per logic frame of the original, the climb speed included (_verticalSpeed): "an eighth of the speed" is used as an acceleration and "half the speed" as
            // the cap of the climb speed, which only means the same climb at every rate when the speed is the travel of a frame.
            var gravity = -FlightFrames.GravityPerFrameSquared(world.Templates);
            FlightFrameConstants(time, locomotor, out var speed, out _, out _, out _, out _);
            FlightVerticalConstants(time, locomotor, out _, out var lift);
            var net = lift > gravity ? lift - gravity : lift;
            if (net <= Fixed.Zero) net = Fixed.Max(speed / Fixed.FromInt(8), Epsilon);
            _verticalSpeed = Fixed.Min(_verticalSpeed + net, Fixed.Max(speed / Fixed.FromInt(2), net));
            Entity.Z = Fixed.Min(Entity.Z + _verticalSpeed, target);
            if (Entity.Z == target) _verticalSpeed = Fixed.Zero;
        }
        else if (Entity.Z > target)
        {
            _verticalSpeed = Fixed.Zero;
            FlightFrameConstants(time, locomotor, out var topSpeed, out _, out _, out _, out _);
            FlightVerticalConstants(time, locomotor, out var limitZ, out _);
            Entity.Z = Fixed.Max(target, Entity.Z - Fixed.Max(limitZ, topSpeed / Fixed.FromInt(4)));
        }
        else return;
        world.MarkDirty(Entity);
    }

    // ------------------------------------------------------------------------------------------------------------------ state

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        writer.Int("jetPhase", (int)_phase);
        writer.Bool("allowAir", _allowAir);
        writer.UInt("airfield", _airfield.Value);
        writer.Fixed("homeX", _homeX);
        writer.Fixed("homeY", _homeY);
        writer.Int("pending", (int)_pending);
        if (_pending != PendingKind.None)
        {
            writer.Fixed("pendingX", _pendingX);
            writer.Fixed("pendingY", _pendingY);
            writer.Bool("pendingFromPlayer", _pendingFromPlayer);
            writer.UInt("pendingTarget", _pendingTarget.Value);
            // Written only for a stored attack-move, so every other pending order keeps its serialized form.
            if (_pendingAttackMove) writer.Bool("pendingAttackMove", true);
        }
        writer.Int("pathIndex", _pathIndex);
        writer.BeginArray("jetPath");
        foreach (var point in _path)
        {
            writer.BeginObject(null);
            writer.Fixed("x", point.X);
            writer.Fixed("y", point.Y);
            writer.Fixed("z", point.Z);
            writer.EndObject();
        }
        writer.EndArray();
        writer.Int("whenTick", _whenTick);
        writer.Int("transferTick", _transferTick);
        writer.UInt("waitedFor", _waitedFor.Value);
        writer.Bool("resetTimer", _resetTimer);
        writer.Int("reloadDone", _reloadDoneTick);
        writer.Int("returnTick", _returnTick);
        writer.Bool("returnLoco", _useReturnLocomotor);
        writer.Int("attackExpire", _attackExpireTick);
        if (_attackersMissExpireTick != 0) writer.Int("attackersMissExpire", _attackersMissExpireTick);
        writer.Int("checkAirfield", _checkAirfieldTick);
        writer.Fixed("verticalSpeed", _verticalSpeed);
        DescribeFlight(writer);
        _frame.Describe(writer, "frameStep");
        writer.BeginObject("leg");
        WritePoint(writer, "parking", _leg.ParkingSpace);
        WritePoint(writer, "prep", _leg.RunwayPrep);
        writer.Fixed("orientation", _leg.ParkingOrientation);
        WritePoint(writer, "start", _leg.RunwayStart);
        WritePoint(writer, "end", _leg.RunwayEnd);
        WritePoint(writer, "approach", _leg.RunwayApproach);
        WritePoint(writer, "exit", _leg.RunwayExit);
        WritePoint(writer, "hangar", _leg.HangarInternal);
        writer.Fixed("hangarOrientation", _leg.HangarInternalOrientation);
        writer.Fixed("takeoffDistance", _leg.RunwayTakeoffDistance);
        writer.EndObject();
    }

    private static void WritePoint(IStateWriter writer, string name, Point3 point)
    {
        // The three names are built only for a writer that keeps them (the snapshot); the hash drops names and this runs for every touched aircraft each tick.
        if (!writer.KeepsNames)
        {
            writer.Fixed(name, point.X);
            writer.Fixed(name, point.Y);
            writer.Fixed(name, point.Z);
            return;
        }
        writer.Fixed(name + "X", point.X);
        writer.Fixed(name + "Y", point.Y);
        writer.Fixed(name + "Z", point.Z);
    }

    private static Point3 ReadPoint(IStateReader reader, string name) => new(reader.Fixed(name + "X"), reader.Fixed(name + "Y"), reader.Fixed(name + "Z"));

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        var phase = reader.Int("jetPhase");
        if (phase < 0 || phase > (int)JetPhase.CirclingDeadAirfield) throw new InvalidDataException("Jet phase out of range.");
        _phase = (JetPhase)phase;
        _allowAir = reader.Bool("allowAir");
        _airfield = new EntityId(checked((uint)reader.UInt("airfield")));
        _homeX = reader.Fixed("homeX");
        _homeY = reader.Fixed("homeY");
        var pending = reader.Int("pending");
        if (pending < 0 || pending > (int)PendingKind.Guard) throw new InvalidDataException("Jet pending order out of range.");
        _pending = (PendingKind)pending;
        if (_pending != PendingKind.None)
        {
            _pendingX = reader.Fixed("pendingX");
            _pendingY = reader.Fixed("pendingY");
            _pendingFromPlayer = reader.Bool("pendingFromPlayer");
            _pendingTarget = new EntityId(checked((uint)reader.UInt("pendingTarget")));
            _pendingAttackMove = reader.Has("pendingAttackMove") && reader.Bool("pendingAttackMove");
        }
        else
        {
            _pendingX = _pendingY = Fixed.Zero;
            _pendingFromPlayer = false;
            _pendingTarget = EntityId.Invalid;
        }
        _pathIndex = checked((int)reader.Int("pathIndex"));
        _path.Clear();
        foreach (var item in reader.Array("jetPath")) _path.Add(new Point3(item.Fixed("x"), item.Fixed("y"), item.Fixed("z")));
        _whenTick = reader.Int("whenTick");
        _transferTick = reader.Int("transferTick");
        _waitedFor = new EntityId(checked((uint)reader.UInt("waitedFor")));
        _resetTimer = reader.Bool("resetTimer");
        _reloadDoneTick = reader.Int("reloadDone");
        _returnTick = reader.Int("returnTick");
        _useReturnLocomotor = reader.Bool("returnLoco");
        _attackExpireTick = reader.Int("attackExpire");
        _attackersMissExpireTick = reader.Has("attackersMissExpire") ? reader.Int("attackersMissExpire") : 0;
        _checkAirfieldTick = reader.Int("checkAirfield");
        _verticalSpeed = reader.Fixed("verticalSpeed");
        LoadFlight(reader);
        _frame.Load(reader, "frameStep");
        var leg = reader.Object("leg");
        _leg = new ParkingInfo(ReadPoint(leg, "parking"), ReadPoint(leg, "prep"), leg.Fixed("orientation"), ReadPoint(leg, "start"), ReadPoint(leg, "end"), ReadPoint(leg, "approach"),
            ReadPoint(leg, "exit"), ReadPoint(leg, "hangar"), leg.Fixed("hangarOrientation"), leg.Fixed("takeoffDistance"));
    }
}

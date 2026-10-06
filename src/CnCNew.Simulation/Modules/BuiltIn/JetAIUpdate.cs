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
/// distance), <c>MinHeight</c> (an instance offset of the drawable), <c>SneakyOffsetWhenAttacking</c>/<c>AttackersMissPersistTime</c> (aim offset of attackers) and the <c>Lockon*</c> fields (the
/// lock-on cursor and the time missiles need to lock) are typed by the catalog schema and have no consumer here.
/// </summary>
public sealed record JetAIData(AIUpdateData Base, Fixed OutOfAmmoDamageRatePerSecond, bool NeedsRunway, bool KeepsParkingSpaceWhenAirborne, long TakeoffPauseTicks, Fixed ParkingOffset,
    string AttackLocomotor, long AttackLocomotorPersistTicks, string ReturnLocomotor, long ReturnToBaseIdleTicks) : ModuleData
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
            ModuleConfigReader.Ticks(configuration, "ReturnToBaseIdleTime", 0));
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
/// <c>docs/SIM-JET-FLIGHT.md</c>); every other aircraft holds its position in the air; the landing aligns the heading to the runway at its start (no turn onto the approach). Not modeled: the interrupt-and-resume of a guard/hunt order for reloading (there are no
/// such orders), <c>notifyVictimIsDead</c> (no attack state), the lock-on cursor, afterburners and landing sounds, aircraft-carrier decks, the sneaky targeting offset, the progressive
/// refill of the clip while reloading (the clip is full when the reload time is over).
/// </summary>
public sealed partial class JetAIUpdate(JetAIData data) : AIUpdateInterface(data.Base)
{
    public enum JetPhase : byte
    {
        None, TaxiFromHangar, AwaitTakeoff, TaxiToTakeoff, PauseBeforeTakeoff, TakingOff, ReturningForLanding, AwaitLanding, Landing, TaxiFromLanding, OrientForParking, ReloadAmmo,
        ReturnToDeadAirfield, CirclingDeadAirfield,
    }

    private enum PendingKind : byte { None, Move, Land }

    private static readonly Fixed ParkedOrientationTolerance = Fixed.FromDecimal(0.001m);
    private static readonly Fixed Misaligned = FixedTrig.PiFraction(1, 6); // as the ground movement of AIUpdateInterface
    private static readonly Fixed Epsilon = Fixed.FromFraction(1, 64);
    // Own speeds of the kinematic stand-in, written as distances per logic frame of the original and scaled where they are used (untouched at 30 Hz).
    /// <summary>The taxi speed of an aircraft without an active locomotor.</summary>
    private static readonly Fixed FallbackTaxiSpeedPerFrame = Fixed.One;
    /// <summary>The slowest horizontal approach of a helicopter to its landing spot.</summary>
    private static readonly Fixed MinHelicopterApproachSpeedPerFrame = Fixed.One;
    /// <summary>The slowest landing speed of a winged aircraft.</summary>
    private static readonly Fixed MinLandingSpeedPerFrame = Fixed.One / Fixed.FromInt(2);

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
    private long _checkAirfieldTick;
    private Fixed _verticalSpeed;
    private Fixed _glideHeight;
    private Fixed _glideLength;
    private ParkingInfo _leg;

    public override string ModuleName => "JetAIUpdate";

    public JetPhase Phase => _phase;

    /// <summary><c>ALLOW_AIR_LOCO</c>: the aircraft may fly (it holds no space it still has to leave).</summary>
    public bool AllowAirLoco => _allowAir;

    /// <summary><c>friend_isTakeoffOrLandingInProgress</c> (the take-off/landing flags the states set, derived from the phase).</summary>
    public bool TakeoffOrLandingInProgress => _phase is JetPhase.TaxiFromHangar or JetPhase.AwaitTakeoff or JetPhase.TaxiToTakeoff or JetPhase.PauseBeforeTakeoff or JetPhase.TakingOff
        or JetPhase.AwaitLanding or JetPhase.Landing or JetPhase.TaxiFromLanding or JetPhase.OrientForParking;

    /// <summary>The airfield that produced (or took in) the aircraft (<c>Object::getProducerID</c>).</summary>
    public EntityId Airfield => _airfield;

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
        var parking = ParkingPlaceBehavior.TryAdoptArrival(world, Entity, out var atHelipad);
        if (parking is null) return;
        _airfield = parking.Entity.Id;
        _homeX = parking.Entity.X;
        _homeY = parking.Entity.Y;
        _allowAir = false;
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
            _path.Add(info.ParkingSpace);
            _pathIndex = 0;
            _phase = JetPhase.TaxiFromHangar;
        }
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
        var speed = taxi?.Speed ?? world.Time.PerOriginalFrameToPerTick(FallbackTaxiSpeedPerFrame);
        var dx = target.X - Entity.X;
        var dy = target.Y - Entity.Y;
        var distance = Fixed.Sqrt(dx * dx + dy * dy);
        if (distance <= (last ? Epsilon : Fixed.Max(speed, Fixed.One)))
        {
            Entity.X = target.X;
            Entity.Y = target.Y;
            Entity.Z = target.Z;
            Entity.Speed = Fixed.Zero;
            _pathIndex++;
            world.MarkDirty(Entity);
            return _pathIndex >= _path.Count;
        }
        var desired = FixedTrig.Atan2(dy, dx);
        var difference = FixedTrig.AngleDifference(Entity.Angle, desired);
        var turn = taxi?.TurnRatePerTick ?? Fixed.Zero;
        Entity.Angle = turn > Fixed.Zero && difference.Abs() > turn ? FixedTrig.Normalize(Entity.Angle + (difference.IsNegative ? -turn : turn)) : desired;
        var aligned = FixedTrig.AngleDifference(Entity.Angle, desired).Abs() <= Misaligned;
        var step = aligned ? Fixed.Min(speed, distance) : Fixed.Zero;
        Entity.Speed = step;
        Entity.X += FixedTrig.Cos(Entity.Angle) * step;
        Entity.Y += FixedTrig.Sin(Entity.Angle) * step;
        Entity.Z = SurfaceHere(world);
        world.MarkDirty(Entity);
        return false;
    }

    /// <summary>Turns in place at the current locomotor's turn rate; true when the heading equals <paramref name="orientation"/> (within the reference's 0.001 rad).</summary>
    private bool TurnTo(Fixed orientation)
    {
        var difference = FixedTrig.AngleDifference(Entity.Angle, orientation);
        if (difference.Abs() <= ParkedOrientationTolerance) return true;
        var turn = ActiveLocomotor?.TurnRatePerTick ?? Fixed.Zero;
        Entity.Angle = turn > Fixed.Zero && difference.Abs() > turn ? FixedTrig.Normalize(Entity.Angle + (difference.IsNegative ? -turn : turn)) : orientation;
        return FixedTrig.AngleDifference(Entity.Angle, orientation).Abs() <= ParkedOrientationTolerance;
    }

    // ------------------------------------------------------------------------------------------------------------------ orders

    /// <summary><c>aiDoCommand</c> for a move: stored while a take-off or landing runs or the aircraft is parked (which starts the take-off), carried out at once while it flies.</summary>
    public override void OrderMove(IWorldServices world, Fixed x, Fixed y, bool fromPlayer, bool attackMove = false)
    {
        if (TakeoffOrLandingInProgress)
        {
            Store(PendingKind.Move, x, y, fromPlayer, EntityId.Invalid, attackMove);
            return;
        }
        if (!_allowAir)
        {
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
            Store(PendingKind.Land, Fixed.Zero, Fixed.Zero, fromPlayer, airfield.Id);
            return;
        }
        if (!_allowAir)
        {
            // isParkedAt: already standing at that airfield, nothing to do.
            if (_airfield == airfield.Id) return;
            Store(PendingKind.Land, Fixed.Zero, Fixed.Zero, fromPlayer, airfield.Id);
            BeginTakeoff();
            return;
        }
        BeginLanding(world, airfield);
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
        StopInternalMove();
        Enter(JetPhase.AwaitTakeoff);
    }

    private void BeginLanding(IWorldServices world, Entity airfield)
    {
        if (Array.Find(airfield.ModuleArray, module => module is ParkingPlaceBehavior) is not ParkingPlaceBehavior parking) return;
        if (!HelipadKind && !parking.ReserveSpace(world, Entity, data.ParkingOffset, out _)) return;
        if (ParkingOf(world) is { } old && !ReferenceEquals(old, parking)) old.ReleaseSpace(world, Entity);
        _airfield = airfield.Id;
        _pending = PendingKind.None;
        _useReturnLocomotor = false;
        BeginReturnForLanding(world);
    }

    // ------------------------------------------------------------------------------------------------------------------ the update

    public override long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        if (Entity.EmpDisabledUntil > world.Tick || (Entity.ScriptDisableMask & Entity.ScriptHeld) != 0)
        {
            Entity.Speed = Fixed.Zero;
            return SimulationTiming.SleepNone;
        }
        UpdateAttackLocomotor(world);
        var parking = ParkingOf(world);

        switch (_phase)
        {
            case JetPhase.None:
                IdleLogic(world, parking);
                break;
            case JetPhase.TaxiFromHangar:
                if (parking is null) { Enter(JetPhase.None); _allowAir = true; break; }
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

        var flying = _phase is JetPhase.None or JetPhase.ReturningForLanding or JetPhase.ReturnToDeadAirfield or JetPhase.CirclingDeadAirfield;
        if (_allowAir && flying) ControlHeight(world);
        // A fighter cannot stop in the air: with no destination (and while it waits for the runway) it circles.
        if (ShouldCircle(_phase)) CircleStep(world);
        else _circling = false;
        if (_phase is JetPhase.None or JetPhase.ReturningForLanding or JetPhase.ReturnToDeadAirfield) base.Update(world);
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

    /// <summary>The idle branch of <c>JetAIUpdate::update</c>: healing, the ammunition return, the stored order and the return after <c>ReturnToBaseIdleTime</c>.</summary>
    private void IdleLogic(IWorldServices world, ParkingPlaceBehavior? parking)
    {
        var now = world.Tick;
        if (!base.IsIdle)
        {
            parking?.SetHealee(world, Entity, false);
            _returnTick = 0;
            return;
        }
        if (parking is not null)
        {
            if (!_allowAir && _pending == PendingKind.None && HelipadKind && Entity.Body is { } body && body.Health == body.MaxHealth)
            {
                // a healed helicopter takes off again by itself
                parking.SetHealee(world, Entity, false);
                BeginTakeoff();
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
        if (!parking.ReserveRunway(world, Entity.Id, false)) return;
        if (!parking.TryGetInfo(world, Entity.Id, out var info)) { Enter(JetPhase.None); return; }
        _leg = info;
        Enter(JetPhase.TaxiToTakeoff);
        // JetOrHeliTaxiState FROM_PARKING: the taxi point, then the runway start (the taxi point only when it had to queue).
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
        var facing = TurnTo(FixedTrig.Atan2(_leg.RunwayEnd.Y - Entity.Y, _leg.RunwayEnd.X - Entity.X));
        world.MarkDirty(Entity);
        if (FindWaiter(world, parking)) return;
        if (!_resetTimer)
        {
            // everyone else is ready: the countdown (re)starts.
            _whenTick = now + data.TakeoffPauseTicks;
            if (!_waitedFor.IsValid)
            {
                _waitedFor = Entity.Id;
                _transferTick = now + TickOrder.Next;
            }
            else _transferTick = now + 2 * TickOrder.Next;   // order, not time: after the jet that was waited for had its step
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
        for (var runway = 0; runway < parking.RunwayCount; runway++)
        {
            var other = world.FindEntity(parking.RunwayReservation(world, runway));
            if (other is null || other == Entity) continue;
            if (Array.Find(other.ModuleArray, module => module is JetAIUpdate) is not JetAIUpdate jet || jet._phase != JetPhase.TaxiToTakeoff) continue;
            if (!_waitedFor.IsValid) _waitedFor = other.Id;
            return true;
        }
        var column = parking.SpaceOf(Entity.Id) % Math.Max(parking.Data.Cols, 1);
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
            Entity.Speed = Fixed.Zero;
        }
        if (parking is null || flight is null) { FinishTakeoff(world, null); return; }
        if (helicopter)
        {
            var rise = _path[0];
            var climb = Fixed.Max(flight.SpeedLimitZ, flight.Speed / Fixed.FromInt(4));
            Entity.Z = Fixed.Min(Entity.Z + climb, rise.Z);
            world.MarkDirty(Entity);
            if (Entity.Z >= rise.Z) FinishTakeoff(world, parking);
            return;
        }
        parking.TransferRunwayToNext(world, Entity.Id);
        var step = Fixed.Max(flight.Speed, Fixed.One);
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
        Entity.Speed = Fixed.Min(flight.Speed, Entity.Speed + flight.Acceleration);
        Entity.X += FixedTrig.Cos(Entity.Angle) * Entity.Speed;
        Entity.Y += FixedTrig.Sin(Entity.Angle) * Entity.Speed;
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

    private void CircleDeadAirfield(IWorldServices world)
    {
        if (data.OutOfAmmoDamageRatePerSecond > Fixed.Zero && Entity.Body is { } body)
        {
            var amount = world.Time.PerSecondToPerTick(data.OutOfAmmoDamageRatePerSecond * body.MaxHealth);
            world.Damage(Entity.Id, new DamageInfo(EntityId.Invalid, amount, DamageTypes.Unresistable, DeathTypes.Normal, false));
        }
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
        // From here on a straight glide to the runway end, then the roll-out to its start.
        _glideHeight = Fixed.Max(Fixed.Zero, Entity.Z - parking.ApronHeight(world, Entity.X, Entity.Y));
        _glideLength = AirModules.Distance2D(Entity.X, Entity.Y, info.RunwayEnd.X, info.RunwayEnd.Y);
        Enter(JetPhase.Landing);
        Entity.Angle = FixedTrig.Atan2(info.RunwayEnd.Y - Entity.Y, info.RunwayEnd.X - Entity.X);
        _path.Add(info.RunwayEnd);
        _path.Add(info.RunwayStart);
    }

    private void Land(IWorldServices world, ParkingPlaceBehavior? parking)
    {
        var flight = Entity.Template.Locomotor;
        if (parking is null || flight is null) { BeginReturnForLanding(world); return; }
        if (Helicopter)
        {
            // HeliTakeoffOrLandingState (landing): settle vertically on the helipad or the space.
            var spot = HelipadKind ? parking.HeliPark(world) : _leg.ParkingSpace;
            var toSpot = AirModules.Distance2D(Entity.X, Entity.Y, spot.X, spot.Y);
            if (toSpot > Fixed.Zero)
            {
                var move = Fixed.Min(toSpot, Fixed.Max(flight.MinSpeed, world.Time.PerOriginalFrameToPerTick(MinHelicopterApproachSpeedPerFrame)));
                Entity.X += (spot.X - Entity.X) * move / toSpot;
                Entity.Y += (spot.Y - Entity.Y) * move / toSpot;
            }
            var descend = Fixed.Max(flight.SpeedLimitZ, flight.Speed / Fixed.FromInt(4));
            Entity.Z = Fixed.Max(spot.Z, Entity.Z - descend);
            Entity.Speed = Fixed.Zero;
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
        var minSpeed = Fixed.Max(flight.MinSpeed, world.Time.PerOriginalFrameToPerTick(MinLandingSpeedPerFrame));
        // loco->setMaxSpeed(loco->getMinSpeed()): the landing is flown at the minimum speed.
        Entity.Speed = Entity.Speed > minSpeed
            ? Fixed.Max(minSpeed, Entity.Speed - Fixed.Max(flight.Braking, flight.Acceleration))
            : Fixed.Min(minSpeed, Entity.Speed + flight.Acceleration);
        var distance = AirModules.Distance2D(Entity.X, Entity.Y, target.X, target.Y);
        var step = Fixed.Min(Entity.Speed, distance);
        if (distance > Fixed.Zero)
        {
            Entity.Angle = FixedTrig.Atan2(target.Y - Entity.Y, target.X - Entity.X);
            Entity.X += (target.X - Entity.X) * step / distance;
            Entity.Y += (target.Y - Entity.Y) * step / distance;
        }
        var ground = parking.ApronHeight(world, Entity.X, Entity.Y);
        if (_pathIndex == 0)
        {
            var remaining = AirModules.Distance2D(Entity.X, Entity.Y, _leg.RunwayEnd.X, _leg.RunwayEnd.Y);
            var fraction = _glideLength > Fixed.Zero ? Fixed.Min(Fixed.One, remaining / _glideLength) : Fixed.Zero;
            Entity.Z = ground + _glideHeight * fraction;
        }
        else Entity.Z = ground;
        world.MarkDirty(Entity);
        if (distance > Fixed.Max(step, Epsilon)) return;
        Entity.X = target.X;
        Entity.Y = target.Y;
        _pathIndex++;
        if (_pathIndex < _path.Count) return;
        // Rolled out at the runway start: stop, give the runway back, taxi to the space.
        Entity.Speed = Fixed.Zero;
        Entity.Z = ground;
        _allowAir = false;
        _useReturnLocomotor = false;
        parking.ReleaseRunway(world, Entity.Id);
        var info = _leg;
        Enter(JetPhase.TaxiFromLanding);
        if (Array.Find(Entity.ModuleArray, module => module is CountermeasuresBehavior) is CountermeasuresBehavior flares) flares.ReloadAtAirfield();
        // JetOrHeliTaxiState TO_PARKING: the taxi point, then the space.
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
        if (!TurnTo(info.ParkingOrientation))
        {
            world.MarkDirty(Entity);
            return;
        }
        Entity.Angle = info.ParkingOrientation;
        Entity.Speed = Fixed.Zero;
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
    /// half of its speed) and sinks at the larger of <c>SpeedLimitZ</c> and a quarter of its speed.
    /// </summary>
    private void ControlHeight(IWorldServices world)
    {
        var locomotor = ActiveLocomotor;
        if (locomotor is null || locomotor.PreferredHeight <= Fixed.Zero) return;
        var target = GroundHere(world) + locomotor.PreferredHeight;
        if (Entity.Z < target)
        {
            var gravity = -world.Templates.GravityPerTickSquared;
            var net = locomotor.Lift > gravity ? locomotor.Lift - gravity : locomotor.Lift;
            if (net <= Fixed.Zero) net = Fixed.Max(locomotor.Speed / Fixed.FromInt(8), Epsilon);
            _verticalSpeed = Fixed.Min(_verticalSpeed + net, Fixed.Max(locomotor.Speed / Fixed.FromInt(2), net));
            Entity.Z = Fixed.Min(Entity.Z + _verticalSpeed, target);
            if (Entity.Z == target) _verticalSpeed = Fixed.Zero;
        }
        else if (Entity.Z > target)
        {
            _verticalSpeed = Fixed.Zero;
            Entity.Z = Fixed.Max(target, Entity.Z - Fixed.Max(locomotor.SpeedLimitZ, locomotor.Speed / Fixed.FromInt(4)));
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
        writer.Int("checkAirfield", _checkAirfieldTick);
        writer.Fixed("verticalSpeed", _verticalSpeed);
        writer.Fixed("glideHeight", _glideHeight);
        writer.Fixed("glideLength", _glideLength);
        DescribeFlight(writer);
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
        if (pending < 0 || pending > (int)PendingKind.Land) throw new InvalidDataException("Jet pending order out of range.");
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
        _checkAirfieldTick = reader.Int("checkAirfield");
        _verticalSpeed = reader.Fixed("verticalSpeed");
        _glideHeight = reader.Fixed("glideHeight");
        _glideLength = reader.Fixed("glideLength");
        LoadFlight(reader);
        var leg = reader.Object("leg");
        _leg = new ParkingInfo(ReadPoint(leg, "parking"), ReadPoint(leg, "prep"), leg.Fixed("orientation"), ReadPoint(leg, "start"), ReadPoint(leg, "end"), ReadPoint(leg, "approach"),
            ReadPoint(leg, "exit"), ReadPoint(leg, "hangar"), leg.Fixed("hangarOrientation"), leg.Fixed("takeoffDistance"));
    }
}

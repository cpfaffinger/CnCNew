using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary><c>FlightDeckBehaviorModuleData</c>: the parking of the deck, and how the aircraft are launched and replaced. Times in ticks of the rate.</summary>
public sealed record FlightDeckData(ParkingPlaceData Parking, string Payload, ImmutableStrings CatapultSystems, long CleanupTicks, long HumanFollowTicks, long ReplacementTicks, long DockAnimationTicks,
    long LaunchWaveTicks, long LaunchRampTicks, long LowerRampTicks, long CatapultFireTicks) : ModuleData
{
    public static FlightDeckData Parse(RatedModuleConfiguration configuration)
    {
        var parking = new ParkingPlaceData(
            checked((int)ModuleConfigReader.Int(configuration, "NumSpacesPerRunway", 0)),
            checked((int)ModuleConfigReader.Int(configuration, "NumRunways", 0)),
            ModuleConfigReader.Real(configuration, "ApproachHeight", Fixed.Zero),
            ModuleConfigReader.Real(configuration, "LandingDeckHeightOffset", Fixed.Zero),
            true, false,
            ModuleConfigReader.Real(configuration, "HealAmountPerSecond", Fixed.Zero));
        return new FlightDeckData(parking, ModuleConfigReader.Text(configuration, "PayloadTemplate"),
            new ImmutableStrings(ModuleConfigReader.Text(configuration, "Runway1CatapultSystem"), ModuleConfigReader.Text(configuration, "Runway2CatapultSystem")),
            ModuleConfigReader.Ticks(configuration, "ParkingCleanupPeriod", 0), ModuleConfigReader.Ticks(configuration, "HumanFollowPeriod", 0),
            ModuleConfigReader.Ticks(configuration, "ReplacementDelay", 0), ModuleConfigReader.Ticks(configuration, "DockAnimationDelay", 0),
            ModuleConfigReader.Ticks(configuration, "LaunchWaveDelay", 0), ModuleConfigReader.Ticks(configuration, "LaunchRampDelay", 0),
            ModuleConfigReader.Ticks(configuration, "LowerRampDelay", 0), ModuleConfigReader.Ticks(configuration, "CatapultFireDelay", 0));
    }
}

/// <summary>The two names a carrier's runways carry per runway.</summary>
public readonly record struct ImmutableStrings(string First, string Second)
{
    public string this[int index] => index == 0 ? First : Second;
}

/// <summary>What a carrier was told to do, which its aircraft carry out (<c>FlightDeckBehavior::m_designatedCommand</c>).</summary>
public enum DeckOrder : byte { None, Guard, AttackPosition, AttackObject, AttackMove, Idle }

/// <summary>
/// <c>FlightDeckBehavior.cpp</c>: an aircraft carrier. It is an airfield whose spaces lie in columns along its runways, with a space of the front row the only place to take off from: it comes with a
/// jet on every space, moves the jets forward as the front empties, launches a jet of each runway per wave while it has an order, replaces what it lost from its production, and heals what stands on it.
/// "The carrier is a dumb stump that only propagates orders to its aircraft": it has no AI of its own, and <see cref="TakeOrder"/> is its <c>aiDoCommand</c>. Reservations of a runway are kept per
/// take-off and landing, which is the one thing that <see cref="ParkingPlaceBehavior"/> keeps in one; there is no line for the runway, "aircraft carrier controls this functionality with an iron fist".
/// Without a model's bones the deck's points are computed, as the airfield's are.
/// </summary>
public sealed class FlightDeckBehavior(FlightDeckData deck) : ParkingPlaceBehavior(deck.Parking)
{
    /// <summary>The distance of <c>isInPositionToTakeoff</c>: <c>distanceSqr &lt; 10</c>.</summary>
    private static readonly SquaredReach InPosition = SquaredReach.OfSquare(Fixed.FromInt(10));

    private const long Forever = long.MaxValue;
    private static readonly int NoAttackBit = ObjectStatusBits.Index("NO_ATTACK");

    private readonly EntityId[] _landingUser = new EntityId[Math.Max(0, deck.Parking.Cols)];
    private readonly long[] _nextWave = new long[Math.Max(0, deck.Parking.Cols)];
    private readonly long[] _rampUpTick = new long[Math.Max(0, deck.Parking.Cols)];
    private readonly long[] _catapultTick = new long[Math.Max(0, deck.Parking.Cols)];
    private readonly long[] _lowerRampTick = new long[Math.Max(0, deck.Parking.Cols)];
    private readonly bool[] _rampUp = new bool[Math.Max(0, deck.Parking.Cols)];
    private bool _built;
    private long _nextCleanup;
    private long _nextAllowedProduction;
    private DeckOrder _order;
    private EntityId _orderTarget;
    private Fixed _orderX;
    private Fixed _orderY;

    public override string ModuleName => "FlightDeckBehavior";

    /// <summary><c>FlightDeckBehavior::getExitPosition</c> and <c>getNaturalRallyPoint</c> answer false: a carrier draws no rally line.</summary>
    public override bool TryGetRallyLineStart(IWorldServices world, out Point3 exit, out bool hasExit, out Point3 natural)
    {
        exit = natural = default;
        hasExit = false;
        return false;
    }

    /// <summary>Whether a runway's ramp stands up (<c>m_rampUp</c>), which the model shows as its door.</summary>
    public bool RampIsUp(int runway) => _rampUp[runway];

    /// <summary>The ramp has come down again after a launch: the model's door is closing, or closed.</summary>
    public bool RampHasComeDown(int runway) => !_rampUp[runway] && _nextWave[runway] != 0;

    /// <summary>What the carrier was last told.</summary>
    public DeckOrder Order => _order;

    // ------------------------------------------------------------------------------------------------------------------ geometry

    private FlightDeckLayout? Layout(IWorldServices world)
    {
        var geometry = world.Templates.Geometry;
        if (geometry.IsEmpty) return null;
        if (!geometry.FlightDecks.TryGetValue(Entity.Template.Name, out var layout)) throw new InvalidDataException($"The model geometry has no deck for the carrier '{Entity.Template.Name}'.");
        return layout;
    }

    private Point3 OnDeck(IWorldServices world, Fixed u, Fixed v)
    {
        var (cos, sin) = (FixedTrig.Cos(Entity.Angle), FixedTrig.Sin(Entity.Angle));
        var (x, y) = (Entity.X + u * cos - v * sin, Entity.Y + u * sin + v * cos);
        return new Point3(x, y, ApronHeight(world, x, y));
    }

    private Point3 FromBone(IWorldServices world, BonePose bone) => OnDeck(world, bone.X, bone.Y);

    private Fixed Heading(BonePose bone) => FixedTrig.Normalize(Entity.Angle + bone.Heading);

    /// <summary>Without a model: the spaces of a runway one behind the other from the bow, the take-off run ahead of the first, the landing run along the deck from the stern, the hangar amidships.</summary>
    private (Fixed U, Fixed V) Computed(int row, int column)
    {
        var a = Entity.Template.GeometryMajorRadius > Fixed.Zero ? Entity.Template.GeometryMajorRadius : Fixed.FromInt(100);
        var b = Entity.Template.GeometryMinorRadius > Fixed.Zero ? Entity.Template.GeometryMinorRadius : Fixed.FromInt(40);
        var cols = Math.Max(Data.Cols, 1);
        var v = Fixed.FromInt(2 * column - (cols - 1)) * b / Fixed.FromInt(2);
        return (a - a * Fixed.FromInt(row) / Fixed.FromInt(Math.Max(Data.Rows, 1)), v);
    }

    private Point3 ComputedStart(IWorldServices world, int column) { var (u, v) = Computed(0, column); return OnDeck(world, u, v); }

    private Point3 ComputedEnd(IWorldServices world, int column) { var (u, v) = Computed(0, column); return OnDeck(world, u + u, v); }

    /// <summary><c>calcPPInfo</c>.</summary>
    public override ParkingInfo InfoOfSpace(IWorldServices world, int index)
    {
        var cols = Math.Max(Data.Cols, 1);
        var (row, column) = (index / cols, index % cols);
        Point3 prep, start, end, landingStart, landingEnd, hangar;
        Fixed orientation, hangarOrientation;
        if (Layout(world) is { } layout)
        {
            prep = FromBone(world, layout.Spaces[index]);
            orientation = Heading(layout.Spaces[index]);
            (start, end) = (FromBone(world, layout.TakeoffStart[column]), FromBone(world, layout.TakeoffEnd[column]));
            (landingStart, landingEnd) = (FromBone(world, layout.LandingStart[column]), FromBone(world, layout.LandingEnd[column]));
            hangar = FromBone(world, layout.Creation[column][0]);
            hangarOrientation = Heading(layout.Creation[column][0]);
        }
        else
        {
            var (u, v) = Computed(row, column);
            prep = OnDeck(world, u, v);
            orientation = Entity.Angle;
            (start, end) = (ComputedStart(world, column), ComputedEnd(world, column));
            (landingStart, landingEnd) = (OnDeck(world, -u - Fixed.One, v), OnDeck(world, Fixed.Zero, v));
            hangar = OnDeck(world, Fixed.Zero, v);
            hangarOrientation = Entity.Angle;
        }
        var approachDistance = Fixed.FromDecimal(0.75m);
        var exit = new Point3(end.X + (end.X - start.X) * approachDistance, end.Y + (end.Y - start.Y) * approachDistance, end.Z + Data.ApproachHeight);
        var approach = new Point3(landingStart.X + (landingStart.X - landingEnd.X) * approachDistance, landingStart.Y + (landingStart.Y - landingEnd.Y) * approachDistance, landingStart.Z + Data.ApproachHeight);
        var takeoff = AirModules.Distance2D(start.X, start.Y, end.X, end.Y);
        var occupant = OccupantOf(index);
        for (var runway = 0; runway < RunwayCount; runway++)
            if (occupant.IsValid && RunwayUser(runway) == occupant) start = prep;
        return new ParkingInfo(prep, prep, orientation, start, end, approach, exit, hangar, hangarOrientation, takeoff, landingStart, landingEnd);
    }

    protected override Fixed ApronOrientation(IWorldServices world, int index) => InfoOfSpace(world, index).ParkingOrientation;

    /// <inheritdoc/>
    public override IReadOnlyList<Point3> TaxiLocations(IWorldServices world, EntityId aircraft) => Bones(world, aircraft, layout => layout.Taxi);

    /// <inheritdoc/>
    public override IReadOnlyList<Point3> CreationLocations(IWorldServices world, EntityId aircraft) => Bones(world, aircraft, layout => layout.Creation);

    private Point3[] Bones(IWorldServices world, EntityId aircraft, Func<FlightDeckLayout, System.Collections.Immutable.ImmutableArray<System.Collections.Immutable.ImmutableArray<BonePose>>> list)
    {
        var space = SpaceOf(aircraft);
        if (space < 0 || Layout(world) is not { } layout) return [];
        return [..list(layout)[space % Math.Max(Data.Cols, 1)].Select(bone => FromBone(world, bone))];
    }

    // ------------------------------------------------------------------------------------------------------------------ runways

    /// <summary><c>purgeDead</c> of the landing reservations; the airfield's covers the rest.</summary>
    public override void Purge(IWorldServices world)
    {
        base.Purge(world);
        for (var index = 0; index < _landingUser.Length; index++)
            if (_landingUser[index].IsValid && world.FindEntity(_landingUser[index]) is not { EffectivelyDead: false, DestroyRequested: false }) _landingUser[index] = EntityId.Invalid;
    }

    /// <summary><c>reserveRunway</c>: only a space of the front row takes off ("you can't take off from the back!"), any space lands; a runway has one user of either kind, and no line.</summary>
    public override bool ReserveRunway(IWorldServices world, EntityId aircraft, bool forLanding)
    {
        Purge(world);
        var space = SpaceOf(aircraft);
        var cols = Math.Max(Data.Cols, 1);
        if (space < 0 || (!forLanding && space >= cols)) return false;
        var runway = space % cols;
        var user = forLanding ? _landingUser[runway] : RunwayUser(runway);
        if (user == aircraft) return true;
        if (user.IsValid) return false;
        if (forLanding) _landingUser[runway] = aircraft;
        else SetRunwayUser(runway, aircraft);
        return true;
    }

    /// <summary>"Aircraft carrier controls this functionality with an iron fist."</summary>
    public override void TransferRunwayToNext(IWorldServices world, EntityId aircraft) { }

    public override void ReleaseRunway(IWorldServices world, EntityId aircraft)
    {
        Purge(world);
        for (var runway = 0; runway < _landingUser.Length; runway++)
        {
            if (RunwayUser(runway) == aircraft) SetRunwayUser(runway, EntityId.Invalid);
            if (_landingUser[runway] == aircraft) _landingUser[runway] = EntityId.Invalid;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ parking

    private static JetAIUpdate? JetOf(Entity? jet) => jet is null ? null : Array.Find(jet.ModuleArray, module => module is JetAIUpdate) as JetAIUpdate;

    /// <summary><c>isAbleToGiveUpParkingSpace</c>: "if we're airborne or non-existant, someone else can have my spot if they need it".</summary>
    private bool IsAbleToGiveUpSpace(IWorldServices world, Entity? jet)
    {
        if (jet is null || world.IsAirborneTarget(jet)) return true;
        if (JetOf(jet) is not { } ai || ai.IsIdle || ai.IsTaxiingToParking) return false;
        if (ai.IsOrderedToLand || _order == DeckOrder.Idle)
        {
            ai.PurgePending();
            return false;
        }
        for (var runway = 0; runway < RunwayCount; runway++)
            if (RunwayUser(runway) == jet.Id) return false;
        return true;
    }

    /// <summary><c>isInPositionToTakeoff</c>.</summary>
    private bool IsInPositionToTakeoff(IWorldServices world, Entity jet, int frontSpace)
    {
        var prep = InfoOfSpace(world, frontSpace).RunwayPrep;
        var (dx, dy) = (jet.X - prep.X, jet.Y - prep.Y);
        return InPosition.Contains(dx * dx + dy * dy);
    }

    /// <summary><c>isAbleToMoveForward</c>.</summary>
    private static bool IsAbleToMoveForward(IWorldServices world, Entity jet)
        => JetOf(jet) is { } ai && !world.IsAirborneTarget(jet) && (ai.IsIdle || ai.IsReloading);

    /// <summary>
    /// <c>calcBestParkingAssignment</c>: "search for the front-most available space that doesn't have any planes blocking it"; the jet swaps its space with the one that holds it, if there is a better one.
    /// </summary>
    public override bool CalcBestParkingAssignment(IWorldServices world, EntityId aircraft, out Point3 position)
    {
        var mine = SpaceOf(aircraft);
        position = default;
        if (mine < 0) return false;
        position = InfoOfSpace(world, mine).RunwayPrep;
        var cols = Math.Max(Data.Cols, 1);
        var runway = mine % cols;
        var best = -1;
        for (var index = 0; index < mine; index++)
        {
            if (index % cols != runway) continue;
            var other = world.FindEntity(OccupantOf(index));
            if (other is null || IsAbleToGiveUpSpace(world, other))
            {
                if (best < 0) best = index;
            }
            else best = -1;
        }
        if (best < 0) return false;
        var bumped = OccupantOf(best);
        SetOccupant(mine, world.FindEntity(bumped) is null ? EntityId.Invalid : bumped);
        SetOccupant(best, aircraft);
        position = InfoOfSpace(world, best).RunwayPrep;
        return true;
    }

    // ------------------------------------------------------------------------------------------------------------------ the update

    /// <summary><c>buildInfo(createUnits)</c>: a jet on every space, from the first moment the carrier stands.</summary>
    private void Build(IWorldServices world)
    {
        _built = true;
        if (deck.Payload.Length == 0 || !world.Templates.TryGet(deck.Payload, out var payload)) return;
        for (var space = 0; space < SpaceCount; space++)
        {
            var info = InfoOfSpace(world, space);
            ExpectParkedArrival(world, payload, info.RunwayPrep, space);
            world.RequestSpawn(payload.Index, Entity.Owner, info.RunwayPrep.X, info.RunwayPrep.Y, info.RunwayPrep.Z, info.ParkingOrientation);
        }
    }

    public override long Update(IWorldServices world)
    {
        var result = base.Update(world);
        if (!IsReady) return result;
        if (!_built) Build(world);
        // The deck decides once per logic frame of the original; the jets it speaks to decide there too.
        if (!world.Time.IsOriginalFrameBoundary(world.Tick)) return result;
        var now = world.Tick;
        if (now >= _nextCleanup) MoveForward(world, now);
        Replace(world, now);
        var hasAircraft = false;
        for (var space = 0; space < SpaceCount && !hasAircraft; space++) hasAircraft = OccupantOf(space).IsValid;
        if (hasAircraft) Entity.Status &= ~(1UL << NoAttackBit);
        else Entity.Status |= 1UL << NoAttackBit;
        for (var runway = 0; runway < _rampUp.Length; runway++) LaunchWave(world, runway, now);
        return result;
    }

    /// <summary>"Periodically, we want to look at all the aircraft assignments and keep them near the front of the carrier at all times."</summary>
    private void MoveForward(IWorldServices world, long now)
    {
        _nextCleanup = now + deck.CleanupTicks;
        var cols = Math.Max(Data.Cols, 1);
        Span<bool> complete = stackalloc bool[cols];
        for (var space = 0; space + cols < SpaceCount; space++)
        {
            var away = world.FindEntity(OccupantOf(space));
            if (away is not null && !IsAbleToGiveUpSpace(world, away)) continue;
            var runway = space % cols;
            if (complete[runway] || world.FindEntity(OccupantOf(space + cols)) is not { } parked || !IsAbleToMoveForward(world, parked)) continue;
            SetOccupant(space, parked.Id);
            SetOccupant(space + cols, away?.Id ?? EntityId.Invalid);
            JetOf(parked)?.TaxiToNewSpace(world, this);
            // "We want to lag the additional planes instead of moving them all simultaneously."
            complete[runway] = true;
            _nextCleanup = now + deck.HumanFollowTicks;
        }
    }

    /// <summary>"Handle automatic production of lost aircraft": one at a time, not before the delay is over.</summary>
    private void Replace(IWorldServices world, long now)
    {
        var empty = false;
        for (var space = 0; space < SpaceCount && !empty; space++) empty = !OccupantOf(space).IsValid;
        if (!empty || now < _nextAllowedProduction || deck.Payload.Length == 0 || !world.Templates.TryGet(deck.Payload, out var payload)) return;
        if (Array.Find(Entity.ModuleArray, module => module is ProductionUpdate) is not ProductionUpdate production || production.Queue.Length != 0) return;
        world.QueueWithoutPay(Entity, production, payload);
        _nextAllowedProduction = now + deck.ReplacementTicks + deck.DockAnimationTicks;
    }

    private void LaunchWave(IWorldServices world, int runway, long now)
    {
        var jet = world.FindEntity(OccupantOf(runway));
        if (jet is not null && !IsAbleToGiveUpSpace(world, jet) && IsInPositionToTakeoff(world, jet, runway) && HasTakeoffOrders(world) && _nextWave[runway] <= now)
        {
            // "Handle making the ramp going up and holding the launch until it's completely up."
            if (!_rampUp[runway])
            {
                _rampUp[runway] = true;
                _rampUpTick[runway] = now + deck.LaunchRampTicks;
                _lowerRampTick[runway] = Forever;
            }
            if (_rampUpTick[runway] <= now)
            {
                PropagateTo(world, jet);
                _nextWave[runway] = now + deck.LaunchWaveTicks;
                _catapultTick[runway] = now + deck.CatapultFireTicks;
                _lowerRampTick[runway] = now + deck.LowerRampTicks;
            }
        }
        if (_catapultTick[runway] <= now)
        {
            _catapultTick[runway] = Forever;
            if (runway < 2 && deck.CatapultSystems[runway].Length > 0)
            {
                var start = InfoOfSpace(world, runway).RunwayPrep;
                world.EmitAt(SimEventKind.DeckCatapult, Entity.Id, EntityId.Invalid, deck.CatapultSystems[runway], runway, start.X, start.Y, start.Z);
            }
        }
        if (_rampUp[runway] && _lowerRampTick[runway] <= now) _rampUp[runway] = false;
    }

    // ------------------------------------------------------------------------------------------------------------------ orders

    /// <summary><c>hasTakeoffOrders</c>: a victim that is gone ends the order.</summary>
    private bool HasTakeoffOrders(IWorldServices world)
    {
        switch (_order)
        {
            case DeckOrder.Guard or DeckOrder.AttackPosition or DeckOrder.AttackMove:
                return true;
            case DeckOrder.AttackObject:
                if (world.FindEntity(_orderTarget) is { EffectivelyDead: false }) return true;
                _order = DeckOrder.None;
                _orderTarget = EntityId.Invalid;
                return false;
            default:
                return false;
        }
    }

    /// <summary><c>aiDoCommand</c> of an order that is not the AI's own: "the only time we care about anything is if we were ordered to attack something or attack move".</summary>
    public void TakeOrder(IWorldServices world, DeckOrder order, EntityId target, Fixed x, Fixed y)
    {
        (_order, _orderTarget, _orderX, _orderY) = (order, target, x, y);
        // propagateOrdersToPlanes: "we just ordered the carrier to stop, so order all the planes that are out to return!"
        for (var space = 0; space < SpaceCount; space++)
            if (world.FindEntity(OccupantOf(space)) is { } jet && IsAbleToGiveUpSpace(world, jet)) PropagateTo(world, jet);
    }

    /// <summary><c>propagateOrderToSpecificPlane</c>.</summary>
    private void PropagateTo(IWorldServices world, Entity jet)
    {
        switch (_order)
        {
            case DeckOrder.Guard:
                world.GuardPlaceFor(jet, _orderX, _orderY);
                break;
            case DeckOrder.AttackPosition:
                world.AttackPlaceFor(jet, _orderX, _orderY, 0);
                break;
            case DeckOrder.AttackObject:
                if (world.FindEntity(_orderTarget) is { } victim) world.AttackFor(jet, victim);
                break;
            case DeckOrder.AttackMove:
                JetOf(jet)?.OrderMove(world, _orderX, _orderY, false, attackMove: true);
                break;
            case DeckOrder.Idle:
                JetOf(jet)?.OrderLand(world, Entity, false);
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ state

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        writer.Bool("built", _built);
        writer.Int("nextCleanup", _nextCleanup);
        writer.Int("nextAllowedProduction", _nextAllowedProduction);
        writer.Int("deckOrder", (int)_order);
        writer.UInt("deckTarget", _orderTarget.Value);
        writer.Fixed("deckX", _orderX);
        writer.Fixed("deckY", _orderY);
        writer.BeginArray("deckRunways");
        for (var runway = 0; runway < _rampUp.Length; runway++)
        {
            writer.BeginObject(null);
            writer.UInt("landing", _landingUser[runway].Value);
            writer.Int("nextWave", _nextWave[runway]);
            writer.Int("rampUpTick", _rampUpTick[runway]);
            writer.Int("catapultTick", _catapultTick[runway]);
            writer.Int("lowerRampTick", _lowerRampTick[runway]);
            writer.Bool("rampUp", _rampUp[runway]);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        _built = reader.Bool("built");
        _nextCleanup = reader.Int("nextCleanup");
        _nextAllowedProduction = reader.Int("nextAllowedProduction");
        var order = reader.Int("deckOrder");
        if (order < 0 || order > (int)DeckOrder.Idle) throw new InvalidDataException("Deck order out of range.");
        _order = (DeckOrder)order;
        _orderTarget = new EntityId(checked((uint)reader.UInt("deckTarget")));
        _orderX = reader.Fixed("deckX");
        _orderY = reader.Fixed("deckY");
        var runways = reader.Array("deckRunways");
        if (runways.Count != _rampUp.Length) throw new InvalidDataException("Runway count does not match the deck.");
        for (var runway = 0; runway < runways.Count; runway++)
        {
            _landingUser[runway] = new EntityId(checked((uint)runways[runway].UInt("landing")));
            _nextWave[runway] = runways[runway].Int("nextWave");
            _rampUpTick[runway] = runways[runway].Int("rampUpTick");
            _catapultTick[runway] = runways[runway].Int("catapultTick");
            _lowerRampTick[runway] = runways[runway].Int("lowerRampTick");
            _rampUp[runway] = runways[runway].Bool("rampUp");
        }
    }
}

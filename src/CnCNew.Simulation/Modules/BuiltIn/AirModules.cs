using System.Collections.Immutable;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The airfield group: <c>ParkingPlaceBehavior</c> (parking spaces, runways, healing and the production exit of an airfield) and <c>JetAIUpdate</c> (taxi, take-off, landing, reloading and
/// returning to base of aircraft), plus the weapon/pulse modules of <see cref="AirWeaponModules"/>. The reference places everything on bones of the building model (<c>Runway1Parking1</c>,
/// <c>RunwayStart1</c>, ...) and drives the aircraft through pathfinding and a physics locomotor; neither exists here, so the geometry is computed (<see cref="ParkingPlaceBehavior"/>) and the
/// movement is kinematic (<see cref="JetAIUpdate"/>). Both are stated at the classes.
/// </summary>
internal static class AirModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<ParkingPlaceData>("ParkingPlaceBehavior", ModuleOwnerKind.Object, (configuration, _) => ParkingPlaceData.Parse(configuration), data => new ParkingPlaceBehavior(data)),
        new ModuleBinding<JetAIData>("JetAIUpdate", ModuleOwnerKind.Object, (configuration, _) => JetAIData.Parse(configuration), data => new JetAIUpdate(data)),
        ..AirWeaponModules.Bindings,
    ];

    public static Point3 Lerp(Point3 from, Point3 to, Fixed fraction)
        => new(from.X + (to.X - from.X) * fraction, from.Y + (to.Y - from.Y) * fraction, from.Z + (to.Z - from.Z) * fraction);

    public static Fixed Distance2D(Fixed ax, Fixed ay, Fixed bx, Fixed by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        return Fixed.Sqrt(dx * dx + dy * dy);
    }
}

// ================================================================================================================ ParkingPlaceBehavior

/// <summary><c>ParkingPlaceBehaviorModuleData</c> (<c>ParkingPlaceBehavior.h</c>); <c>NumRows</c>/<c>NumCols</c> default to 0 (no spaces), as the reference constructor.</summary>
public sealed record ParkingPlaceData(int Rows, int Cols, Fixed ApproachHeight, Fixed LandingDeckHeightOffset, bool HasRunways, bool ParkInHangars, Fixed HealAmountPerSecond) : ModuleData
{
    public static ParkingPlaceData Parse(ModuleConfiguration configuration) => new(
        checked((int)ModuleConfigReader.Int(configuration, "NumRows", 0)),
        checked((int)ModuleConfigReader.Int(configuration, "NumCols", 0)),
        ModuleConfigReader.Real(configuration, "ApproachHeight", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "LandingDeckHeightOffset", Fixed.Zero),
        ModuleConfigReader.Bool(configuration, "HasRunways", false),
        ModuleConfigReader.Bool(configuration, "ParkInHangars", false),
        ModuleConfigReader.Real(configuration, "HealAmountPerSecond", Fixed.Zero));
}

/// <summary><c>ParkingPlaceBehaviorInterface::PPInfo</c>: where one aircraft parks and which way it drives and flies to take off and land.</summary>
public readonly record struct ParkingInfo(Point3 ParkingSpace, Point3 RunwayPrep, Fixed ParkingOrientation, Point3 RunwayStart, Point3 RunwayEnd, Point3 RunwayApproach, Point3 RunwayExit,
    Point3 HangarInternal, Fixed HangarInternalOrientation, Fixed RunwayTakeoffDistance);

/// <summary>
/// <c>ParkingPlaceBehavior.cpp</c>: the airfield. It owns <c>NumRows · NumCols</c> parking spaces (an aircraft keeps one from the moment it leaves the production door until it is
/// destroyed or lands elsewhere — <c>reserveSpace</c>/<c>releaseSpace</c>), one runway per column that only one aircraft uses at a time with a single waiting place for take-off
/// (<c>reserveRunway</c>, <c>transferRunwayReservationToNextInLineForTakeoff</c>, <c>releaseRunway</c>), heals the aircraft parked on it (<c>HealAmountPerSecond</c>, every 6 ticks like
/// <c>HEAL_RATE_FRAMES</c>) and is the production exit of aircraft (<c>reserveDoorForExit</c>/<c>exitObjectViaDoor</c>: a produced aircraft appears at its space's hangar position, a
/// <c>PRODUCED_AT_HELIPAD</c> helicopter at the helipad and needs no space). The destruction of the airfield kills everything that is parked or taking off or landing on it
/// (<c>killAllParkedUnits</c>). Dead occupants are purged from spaces, runways and the healing list on every update.
/// <para>
/// <b>Geometry</b> (replaces the model bones, which the simulation does not have): in the building's own frame (heading <c>θ</c>, major/minor geometry radii <c>a</c>/<c>b</c>) space
/// (row, column) lies at <c>u = (row − (rows−1)/2)·a</c>, <c>v = side·b/2</c> with <c>side = −1</c> for the first and <c>+1</c> for the second half of the columns, facing outwards
/// (<c>θ + side·π/2</c>); the hangar position is 40 % of that way from the centre; the taxi point (<c>Prep</c>) is 40 beyond the footprint on the same side; the runway of the column starts
/// at <c>u = −a − 40</c> and ends <c>3a</c> further along the heading, <c>b + 80</c> off the centre, and the approach point lies <c>0.75</c> runway lengths past its end at
/// <c>ApproachHeight</c>. The helipad is the building's centre. Documented synthetic layout; the reference reads all of it from the model. Where the host supplies the model's bones
/// (<see cref="ModelGeometry"/>, read from the base pose of the draw model by the client layer), the airfield uses exactly the points of <c>buildInfo</c> instead: the parking, hangar and
/// taxi points per space, <c>RunwayStart</c>/<c>RunwayEnd</c> per runway, <c>HeliPark01</c> for the helipad, with the height of the apron (the airfield's own height plus the bone's).
/// </para>
/// Not modeled: <c>defectAllParkedUnits</c> (a captured airfield's parked units change sides: no module is told of an owner change), carrier decks (<c>FlightDeckBehavior</c> is a shell),
/// the helicopter rally point, the door-hold of <c>ProductionUpdate::setHoldDoorOpen</c> (presentation).
/// </summary>
public sealed class ParkingPlaceBehavior(ParkingPlaceData data) : SimModule, IUpdateModule, IDieModule, IProductionExit
{
    /// <summary><c>HEAL_RATE_FRAMES = LOGICFRAMES_PER_SECOND / 5</c>: "don't really need to autoheal every frame".</summary>
    private const int HealRateTicks = SimulationTiming.LogicFramesPerSecond / 5;

    private static readonly int DeckHeightBit = ObjectStatusBits.Index("DECK_HEIGHT_OFFSET");

    private sealed record Arrival(int Template, Fixed X, Fixed Y, PlayerId Owner, long Tick, int Space);

    private long _nextWake = -1;
    private readonly EntityId[] _occupant = new EntityId[Math.Max(0, data.Rows * data.Cols)];
    private readonly bool[] _reservedForExit = new bool[Math.Max(0, data.Rows * data.Cols)];
    private readonly EntityId[] _runwayUser = new EntityId[data.HasRunways ? Math.Max(0, data.Cols) : 0];
    private readonly EntityId[] _runwayNext = new EntityId[data.HasRunways ? Math.Max(0, data.Cols) : 0];
    private readonly bool[] _runwayWasInLine = new bool[data.HasRunways ? Math.Max(0, data.Cols) : 0];
    private readonly List<EntityId> _healing = [];
    private readonly List<Arrival> _arrivals = [];
    private long _nextHeal = -1;

    public override string ModuleName => "ParkingPlaceBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public ParkingPlaceData Data => data;

    /// <summary>The airfield is complete and not being sold (<c>buildInfo</c> waits for both).</summary>
    public bool IsReady => !Entity.HasStatus(ObjectStatusBits.UnderConstruction) && !Entity.HasStatus(ObjectStatusBits.Sold);

    public int SpaceCount => _occupant.Length;
    public int RunwayCount => _runwayUser.Length;

    /// <summary>Who occupies a space (invalid: free). Index = row · columns + column.</summary>
    public EntityId OccupantOf(int space) => _occupant[space];

    /// <summary>Whether a space is held back for a unit that is about to leave the production door.</summary>
    public bool IsReservedForExit(int space) => _reservedForExit[space];

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    // ------------------------------------------------------------------------------------------------------------------ geometry

    private static Fixed Half(Fixed value) => value / Fixed.FromInt(2);

    private (Fixed A, Fixed B) Extents()
    {
        var a = Entity.Template.GeometryMajorRadius;
        var b = Entity.Template.GeometryMinorRadius;
        return (a > Fixed.Zero ? a : Fixed.FromInt(50), b > Fixed.Zero ? b : Fixed.FromInt(30));
    }

    private Point3 ToWorld(IWorldServices world, Fixed u, Fixed v, Fixed height)
    {
        var cos = FixedTrig.Cos(Entity.Angle);
        var sin = FixedTrig.Sin(Entity.Angle);
        var x = Entity.X + u * cos - v * sin;
        var y = Entity.Y + u * sin + v * cos;
        return new Point3(x, y, world.GroundHeightAt(x, y) + height);
    }

    /// <summary>The model's points of this airfield, or null where the host supplied no model geometry (the computed layout then applies); a table without this airfield or with other dimensions is refused.</summary>
    private AirfieldLayout? ModelLayout(IWorldServices world)
    {
        var geometry = world.Templates.Geometry;
        if (geometry.IsEmpty) return null;
        if (!geometry.Airfields.TryGetValue(Entity.Template.Name, out var layout)) throw new InvalidDataException($"The model geometry has no layout for the airfield '{Entity.Template.Name}'.");
        if (layout.Rows != data.Rows || layout.Cols != data.Cols)
            throw new InvalidDataException($"The model geometry of '{Entity.Template.Name}' is for {layout.Rows}x{layout.Cols} spaces, the module has {data.Rows}x{data.Cols}.");
        return layout;
    }

    /// <summary>A bone of the airfield model in the world: the object's frame rotated by its heading, at the apron height (<c>Object::getSingleLogicalBonePosition</c>).</summary>
    private Point3 FromBone(BonePose bone)
    {
        var cos = FixedTrig.Cos(Entity.Angle);
        var sin = FixedTrig.Sin(Entity.Angle);
        return new Point3(Entity.X + bone.X * cos - bone.Y * sin, Entity.Y + bone.X * sin + bone.Y * cos, Entity.Z + bone.Z + data.LandingDeckHeightOffset);
    }

    private Fixed BoneHeading(BonePose bone) => FixedTrig.Normalize(Entity.Angle + bone.Heading);

    /// <summary>The height of the apron the aircraft taxi on: the airfield's own height (the model's tarmac lies at its origin) plus the deck offset. Terrain-relative where no model geometry exists.</summary>
    public Fixed ApronHeight(IWorldServices world, Fixed x, Fixed y)
        => world.Templates.Geometry.IsEmpty ? world.GroundHeightAt(x, y) + data.LandingDeckHeightOffset : Entity.Z + data.LandingDeckHeightOffset;

    /// <summary>The helipad position of <c>PRODUCED_AT_HELIPAD</c> aircraft (<c>HeliPark01</c>): the model's bone, else the building's centre.</summary>
    public Point3 HeliPark(IWorldServices world)
        => ModelLayout(world) is { HeliPark: { } pad } ? FromBone(pad) : ToWorld(world, Fixed.Zero, Fixed.Zero, Data.LandingDeckHeightOffset);

    /// <summary><c>calcPPInfo</c> for the space at <paramref name="index"/> (the runway is the one of its column).</summary>
    public ParkingInfo InfoOfSpace(IWorldServices world, int index)
    {
        if (ModelLayout(world) is { } layout) return InfoFromModel(world, layout, index);
        var (a, b) = Extents();
        var rows = Math.Max(data.Rows, 1);
        var cols = Math.Max(data.Cols, 1);
        var row = index / cols;
        var column = index % cols;
        var side = Fixed.FromInt(column * 2 < cols ? -1 : 1);
        var rowU = Fixed.FromInt(2 * row - (rows - 1)) * a / Fixed.FromInt(2);
        var parkV = side * Half(b);
        var orientation = FixedTrig.Normalize(Entity.Angle + side * FixedTrig.HalfPi);
        var deck = data.LandingDeckHeightOffset;
        var parking = ToWorld(world, rowU, parkV, deck);
        var hangar = ToWorld(world, rowU * Fixed.FromDecimal(0.4m), parkV * Fixed.FromDecimal(0.4m), deck);
        var prep = ToWorld(world, rowU, side * (b + Fixed.FromInt(40)), deck);
        var length = a * 3;
        var startU = -a - Fixed.FromInt(40);
        var runwayV = side * (b + Fixed.FromInt(80));
        var start = ToWorld(world, startU, runwayV, deck);
        var end = ToWorld(world, startU + length, runwayV, deck);
        var approach = new Point3(end.X + (end.X - start.X) * Fixed.FromDecimal(0.75m), end.Y + (end.Y - start.Y) * Fixed.FromDecimal(0.75m), end.Z + data.ApproachHeight);
        if (!data.HasRunways) { start = end = prep; approach = prep; }
        var takeoff = AirModules.Distance2D(start.X, start.Y, end.X, end.Y);
        return new ParkingInfo(data.ParkInHangars ? hangar : parking, prep, data.ParkInHangars ? orientation : orientation, start, end, approach, approach, hangar, orientation, takeoff);
    }

    /// <summary><c>calcPPInfo</c> from the model's bones: the space's own points and its column's runway; the approach point lies 0.75 runway lengths past the end at <c>ApproachHeight</c>.</summary>
    private ParkingInfo InfoFromModel(IWorldServices world, AirfieldLayout layout, int index)
    {
        var cols = Math.Max(data.Cols, 1);
        var column = index % cols;
        var parking = FromBone(layout.Parking[index]);
        var hangar = FromBone(layout.Hangar[index]);
        var prep = FromBone(layout.Prep[index]);
        var start = FromBone(layout.RunwayStart[column]);
        var end = FromBone(layout.RunwayEnd[column]);
        var approach = new Point3(end.X + (end.X - start.X) * Fixed.FromDecimal(0.75m), end.Y + (end.Y - start.Y) * Fixed.FromDecimal(0.75m), end.Z + data.ApproachHeight);
        if (!data.HasRunways) { start = end = prep; approach = prep; }
        var takeoff = AirModules.Distance2D(start.X, start.Y, end.X, end.Y);
        var parkOrientation = BoneHeading(data.ParkInHangars ? layout.Hangar[index] : layout.Parking[index]);
        return new ParkingInfo(data.ParkInHangars ? hangar : parking, prep, parkOrientation, start, end, approach, approach, hangar, BoneHeading(layout.Hangar[index]), takeoff);
    }

    /// <summary>The parking orientation of an apron space as the offset of <c>reserveSpace</c> uses it.</summary>
    private Fixed ApronOrientation(IWorldServices world, int index)
    {
        if (ModelLayout(world) is { } layout) return BoneHeading(layout.Parking[index]);
        var cols = Math.Max(data.Cols, 1);
        var side = Fixed.FromInt(index % cols * 2 < cols ? -1 : 1);
        return FixedTrig.Normalize(Entity.Angle + side * FixedTrig.HalfPi);
    }

    // ------------------------------------------------------------------------------------------------------------------ reservations

    public int SpaceOf(EntityId id)
    {
        if (!id.IsValid) return -1;
        for (var index = 0; index < _occupant.Length; index++)
            if (_occupant[index] == id) return index;
        return -1;
    }

    private int EmptySpace()
    {
        if (!IsReady) return -1;
        for (var index = 0; index < _occupant.Length; index++)
            if (!_occupant[index].IsValid && !_reservedForExit[index]) return index;
        return -1;
    }

    /// <summary><c>hasReservedSpace</c>.</summary>
    public bool HasReservedSpace(EntityId id) => SpaceOf(id) >= 0;

    /// <summary>Whether <paramref name="id"/> could reserve a space here right now (its own, or a free one): the test <c>PartitionFilterHasParkingPlace</c> applies.</summary>
    public bool HasFreeSpaceOrOwns(EntityId id) => IsReady && (SpaceOf(id) >= 0 || EmptySpace() >= 0);

    /// <summary><c>purgeDead</c>: occupants, runway users and healees that are gone or dead lose their place.</summary>
    public void Purge(IWorldServices world)
    {
        for (var index = 0; index < _occupant.Length; index++)
        {
            if (!_occupant[index].IsValid) continue;
            var occupant = world.FindEntity(_occupant[index]);
            if (occupant is null || occupant.EffectivelyDead || occupant.DestroyRequested)
            {
                _occupant[index] = EntityId.Invalid;
                _reservedForExit[index] = false;
            }
        }
        for (var index = 0; index < _runwayUser.Length; index++)
        {
            if (_runwayUser[index].IsValid && world.FindEntity(_runwayUser[index]) is not { EffectivelyDead: false, DestroyRequested: false })
            {
                _runwayUser[index] = EntityId.Invalid;
                _runwayWasInLine[index] = false;
            }
            if (_runwayNext[index].IsValid && world.FindEntity(_runwayNext[index]) is not { EffectivelyDead: false, DestroyRequested: false }) _runwayNext[index] = EntityId.Invalid;
        }
        if (_healing.RemoveAll(id => world.FindEntity(id) is not { EffectivelyDead: false, DestroyRequested: false }) > 0) ResetHealWake(world);
    }

    /// <summary>
    /// <c>reserveSpace</c>: the aircraft's own space, else the first free one; false when the airfield is not ready or full. <paramref name="info"/> is the aircraft's
    /// <see cref="ParkingInfo"/> with <paramref name="parkingOffset"/> added along the parking orientation (JetAIUpdate's <c>ParkingOffset</c>).
    /// </summary>
    public bool ReserveSpace(IWorldServices world, Entity aircraft, Fixed parkingOffset, out ParkingInfo info)
    {
        info = default;
        if (!IsReady) return false;
        Purge(world);
        var index = SpaceOf(aircraft.Id);
        if (index < 0)
        {
            index = EmptySpace();
            if (index < 0) return false;
        }
        _occupant[index] = aircraft.Id;
        _reservedForExit[index] = false;
        if (data.LandingDeckHeightOffset != Fixed.Zero) aircraft.Status |= 1UL << DeckHeightBit;
        info = InfoOfSpace(world, index);
        if (parkingOffset != Fixed.Zero)
        {
            var orientation = ApronOrientation(world, index);
            info = info with { ParkingSpace = info.ParkingSpace with { X = info.ParkingSpace.X + parkingOffset * FixedTrig.Cos(orientation), Y = info.ParkingSpace.Y + parkingOffset * FixedTrig.Sin(orientation) } };
        }
        return true;
    }

    /// <summary>The info of the space an aircraft holds (<c>calcPPInfo</c> through <c>findPPI</c>), including the take-off queue rule: an aircraft that waited for the runway starts at the taxi point.</summary>
    public bool TryGetInfo(IWorldServices world, EntityId aircraft, out ParkingInfo info)
    {
        info = default;
        var index = SpaceOf(aircraft);
        if (index < 0) return false;
        info = InfoOfSpace(world, index);
        var runway = index % Math.Max(data.Cols, 1);
        if (runway < _runwayUser.Length && _runwayUser[runway] == aircraft && _runwayWasInLine[runway]) info = info with { RunwayStart = info.RunwayPrep };
        return true;
    }

    /// <summary><c>releaseSpace</c>.</summary>
    public void ReleaseSpace(IWorldServices world, Entity aircraft)
    {
        Purge(world);
        for (var index = 0; index < _occupant.Length; index++)
            if (_occupant[index] == aircraft.Id)
            {
                _occupant[index] = EntityId.Invalid;
                _reservedForExit[index] = false;
            }
        aircraft.Status &= ~(1UL << DeckHeightBit);
    }

    /// <summary><c>getRunwayReservation</c>.</summary>
    public EntityId RunwayReservation(IWorldServices world, int runway)
    {
        Purge(world);
        return _runwayUser[runway];
    }

    /// <summary>
    /// <c>reserveRunway</c>: the aircraft (which must hold a space) takes its column's runway when it is free; a take-off request that finds it taken becomes the single next-in-line
    /// and is told no (<c>return false; // yes, that's right</c>), a landing is simply refused. An airfield without runways never makes anybody wait.
    /// </summary>
    public bool ReserveRunway(IWorldServices world, EntityId aircraft, bool forLanding)
    {
        Purge(world);
        var space = SpaceOf(aircraft);
        if (space < 0) return false;
        if (_runwayUser.Length == 0) return true;
        var runway = space % Math.Max(data.Cols, 1);
        if (_runwayUser[runway] == aircraft) return true;
        if (!_runwayUser[runway].IsValid)
        {
            _runwayUser[runway] = aircraft;
            if (_runwayNext[runway] == aircraft)
            {
                _runwayNext[runway] = EntityId.Invalid;
                _runwayWasInLine[runway] = true;
            }
            else _runwayWasInLine[runway] = false;
            return true;
        }
        if (!forLanding && !_runwayNext[runway].IsValid) _runwayNext[runway] = aircraft;
        return false;
    }

    /// <summary><c>transferRunwayReservationToNextInLineForTakeoff</c>: the runway passes to the waiting aircraft while the previous one starts its run.</summary>
    public void TransferRunwayToNext(IWorldServices world, EntityId aircraft)
    {
        Purge(world);
        for (var index = 0; index < _runwayUser.Length; index++)
            if (_runwayUser[index] == aircraft && _runwayNext[index].IsValid)
            {
                _runwayUser[index] = _runwayNext[index];
                _runwayWasInLine[index] = true;
                _runwayNext[index] = EntityId.Invalid;
            }
    }

    /// <summary><c>releaseRunway</c>.</summary>
    public void ReleaseRunway(IWorldServices world, EntityId aircraft)
    {
        Purge(world);
        for (var index = 0; index < _runwayUser.Length; index++)
        {
            if (_runwayUser[index] == aircraft)
            {
                _runwayUser[index] = EntityId.Invalid;
                _runwayWasInLine[index] = false;
            }
            if (_runwayNext[index] == aircraft) _runwayNext[index] = EntityId.Invalid;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ healing

    private void ResetHealWake(IWorldServices world) => _nextHeal = _healing.Count == 0 ? -1 : world.Tick + HealRateTicks;

    /// <summary><c>setHealee</c>: an aircraft parked on the airfield gets healed; leaving (or taking off) stops it.</summary>
    public void SetHealee(IWorldServices world, Entity aircraft, bool add)
    {
        if (add)
        {
            if (_healing.Contains(aircraft.Id)) return;
            _healing.Add(aircraft.Id);
            ResetHealWake(world);
        }
        else if (_healing.Remove(aircraft.Id)) ResetHealWake(world);
    }

    public long Update(IWorldServices world)
    {
        Purge(world);
        _arrivals.RemoveAll(arrival => world.Tick - arrival.Tick > 2);
        if (_nextHeal >= 0 && world.Tick >= _nextHeal)
        {
            _nextHeal = world.Tick + HealRateTicks;
            var amount = Fixed.FromInt(HealRateTicks) * data.HealAmountPerSecond / Fixed.FromInt(SimulationTiming.LogicFramesPerSecond);
            foreach (var id in _healing.ToArray())
                if (world.FindEntity(id) is { } patient) world.Damage(patient.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
        }
        return SimulationTiming.SleepNone;
    }

    // ------------------------------------------------------------------------------------------------------------------ death

    /// <summary>
    /// <c>onDie</c> → <c>killAllParkedUnits</c>: every aircraft on a space dies unless it is airborne and not taking off or landing.
    /// </summary>
    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        Purge(world);
        foreach (var id in _occupant.Where(value => value.IsValid).ToArray())
        {
            if (world.FindEntity(id) is not { EffectivelyDead: false } aircraft) continue;
            var transitioning = Array.Find(aircraft.ModuleArray, module => module is JetAIUpdate) is JetAIUpdate jet && jet.TakeoffOrLandingInProgress;
            var airborne = Array.Find(aircraft.ModuleArray, module => module is JetAIUpdate) is JetAIUpdate { AllowAirLoco: true };
            if (airborne && !transitioning) continue;
            world.Kill(aircraft.Id);
        }
        Purge(world);
    }

    // ------------------------------------------------------------------------------------------------------------------ production exit

    public bool IsFreeToExit => IsReady && EmptySpace() >= 0;

    /// <summary><c>hasAvailableSpaceFor</c>: a <c>PRODUCED_AT_HELIPAD</c> object always fits, anything else needs a free space.</summary>
    public bool IsFreeToExitFor(ObjectTemplate produced) => IsReady && (produced.IsKindOf("PRODUCED_AT_HELIPAD") || EmptySpace() >= 0);

    public (Fixed X, Fixed Y, Fixed Z, Fixed Angle) ClaimExit(IWorldServices world) => ClaimExitCore(world, null);

    /// <summary>
    /// <c>reserveDoorForExit</c> + <c>exitObjectViaDoor</c>: an aircraft takes the first free space (held back until it exists) and appears at its hangar position facing its
    /// parking direction; a helicopter appears at the helipad. The arrival is remembered so that the new aircraft's <c>JetAIUpdate</c> can recognise its airfield
    /// (<see cref="TryAdoptArrival"/>) — the reference knows the producer from <c>Object::setProducer</c>.
    /// </summary>
    public (Fixed X, Fixed Y, Fixed Z, Fixed Angle) ClaimExit(IWorldServices world, ObjectTemplate produced) => ClaimExitCore(world, produced);

    private (Fixed X, Fixed Y, Fixed Z, Fixed Angle) ClaimExitCore(IWorldServices world, ObjectTemplate? produced)
    {
        if (produced is not null && produced.IsKindOf("PRODUCED_AT_HELIPAD"))
        {
            var pad = HeliPark(world);
            _arrivals.Add(new Arrival(produced.Index, pad.X, pad.Y, Entity.Owner, world.Tick, -1));
            return (pad.X, pad.Y, pad.Z, Entity.Angle);
        }
        var index = EmptySpace();
        if (index < 0) return (Entity.X, Entity.Y, Entity.Z, Entity.Angle);
        _reservedForExit[index] = true;
        var info = InfoOfSpace(world, index);
        if (produced is not null) _arrivals.Add(new Arrival(produced.Index, info.HangarInternal.X, info.HangarInternal.Y, Entity.Owner, world.Tick, index));
        return (info.HangarInternal.X, info.HangarInternal.Y, info.HangarInternal.Z, info.HangarInternalOrientation);
    }

    /// <summary>
    /// Finds the airfield whose door just let <paramref name="aircraft"/> out (same owner, product and position, claimed within the last ticks) and gives it its space
    /// (<c>exitObjectViaDoor</c>: the reserved space becomes the aircraft's). Null when the aircraft did not come out of an airfield.
    /// </summary>
    public static ParkingPlaceBehavior? TryAdoptArrival(IWorldServices world, Entity aircraft, out bool atHelipad)
    {
        atHelipad = false;
        foreach (var candidate in world.Entities.OrderBy(value => value.Id.Value))
        {
            if (candidate.Owner != aircraft.Owner || !candidate.Template.IsKindOf("FS_AIRFIELD")) continue;
            if (Array.Find(candidate.ModuleArray, module => module is ParkingPlaceBehavior) is not ParkingPlaceBehavior parking) continue;
            var arrival = parking._arrivals.Find(value => value.Template == aircraft.Template.Index && value.X == aircraft.X && value.Y == aircraft.Y);
            if (arrival is null) continue;
            parking._arrivals.Remove(arrival);
            atHelipad = aircraft.Template.IsKindOf("PRODUCED_AT_HELIPAD");
            if (!atHelipad)
            {
                var space = arrival.Space;
                if (space < 0 || space >= parking._occupant.Length || !parking._reservedForExit[space] || parking._occupant[space].IsValid) return null;
                parking._occupant[space] = aircraft.Id;
                parking._reservedForExit[space] = false;
                if (parking.Data.LandingDeckHeightOffset != Fixed.Zero) aircraft.Status |= 1UL << DeckHeightBit;
            }
            return parking;
        }
        return null;
    }

    // ------------------------------------------------------------------------------------------------------------------ state

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("nextHeal", _nextHeal);
        writer.BeginArray("spaces");
        for (var index = 0; index < _occupant.Length; index++)
        {
            writer.BeginObject(null);
            writer.UInt("occupant", _occupant[index].Value);
            writer.Bool("reserved", _reservedForExit[index]);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("runways");
        for (var index = 0; index < _runwayUser.Length; index++)
        {
            writer.BeginObject(null);
            writer.UInt("user", _runwayUser[index].Value);
            writer.UInt("next", _runwayNext[index].Value);
            writer.Bool("wasInLine", _runwayWasInLine[index]);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("healing");
        foreach (var id in _healing)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("arrivals");
        foreach (var arrival in _arrivals)
        {
            writer.BeginObject(null);
            writer.Int("template", arrival.Template);
            writer.Fixed("x", arrival.X);
            writer.Fixed("y", arrival.Y);
            writer.Int("owner", arrival.Owner.Value);
            writer.Int("tick", arrival.Tick);
            writer.Int("space", arrival.Space);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _nextHeal = reader.Int("nextHeal");
        var spaces = reader.Array("spaces");
        if (spaces.Count != _occupant.Length) throw new InvalidDataException("Parking space count does not match the module.");
        for (var index = 0; index < spaces.Count; index++)
        {
            _occupant[index] = new EntityId(checked((uint)spaces[index].UInt("occupant")));
            _reservedForExit[index] = spaces[index].Bool("reserved");
        }
        var runways = reader.Array("runways");
        if (runways.Count != _runwayUser.Length) throw new InvalidDataException("Runway count does not match the module.");
        for (var index = 0; index < runways.Count; index++)
        {
            _runwayUser[index] = new EntityId(checked((uint)runways[index].UInt("user")));
            _runwayNext[index] = new EntityId(checked((uint)runways[index].UInt("next")));
            _runwayWasInLine[index] = runways[index].Bool("wasInLine");
        }
        _healing.Clear();
        foreach (var item in reader.Array("healing")) _healing.Add(new EntityId(checked((uint)item.UInt("id"))));
        _arrivals.Clear();
        foreach (var item in reader.Array("arrivals"))
            _arrivals.Add(new Arrival(checked((int)item.Int("template")), item.Fixed("x"), item.Fixed("y"), new PlayerId(checked((int)item.Int("owner"))), item.Int("tick"), checked((int)item.Int("space"))));
    }
}

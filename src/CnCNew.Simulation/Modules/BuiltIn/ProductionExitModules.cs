using CnCNew.Data.Registry;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>A point in a producer's local space (<c>UnitCreatePoint</c>, <c>NaturalRallyPoint</c>).</summary>
public readonly record struct Point3(Fixed X, Fixed Y, Fixed Z);

/// <summary>
/// <c>UnitCreatePoint</c>/<c>NaturalRallyPoint</c> of the three exit modules plus the two <c>QueueProductionExitUpdate</c> pacing keys.
/// <c>UseSpawnRallyPoint</c> and <c>GrantTemporaryStealth</c> are typed by the catalog but have no consumer.
/// </summary>
public sealed record ProductionExitData(Point3 UnitCreatePoint, long ExitDelayTicks, int InitialBurst, Point3 NaturalRallyPoint = default) : ModuleData
{
    public static ProductionExitData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Vector(configuration, "UnitCreatePoint"),
        ModuleConfigReader.Ticks(configuration, "ExitDelay", 0),
        checked((int)ModuleConfigReader.Int(configuration, "InitialBurst", 0)),
        ModuleConfigReader.Vector(configuration, "NaturalRallyPoint"));
}

/// <summary>
/// Shared exit geometry (<c>DefaultProductionExitUpdate::exitObjectViaDoor</c> and its two siblings): the new object appears at the
/// producer's transformed <c>UnitCreatePoint</c> on the ground, facing the producer's own orientation, and is sent along its exit path:
/// through the door to the transformed <c>NaturalRallyPoint</c> (<see cref="TryGetExitPath"/>) and from there to the player's rally point, if one is set.
/// </summary>
public abstract class ProductionExitBase(ProductionExitData exitData) : SimModule, IProductionExit
{
    /// <summary><c>getNaturalRallyPoint(…, offset = TRUE)</c>: the point is pushed <c>2 * PATHFIND_CELL_SIZE_F</c> further along its own direction.</summary>
    private static readonly Fixed RallyOffset = Fixed.FromInt(2 * Terrain.TerrainMap.CellSize);

    /// <summary>Own value: the door point of the exit path lies one pathfinding cell outside <c>NaturalRallyPoint</c> (which is on the building's edge), so its cell is not the building's.</summary>
    private static readonly Fixed DoorOffset = Fixed.FromInt(Terrain.TerrainMap.CellSize);

    protected ProductionExitData Data { get; } = exitData;

    /// <summary>
    /// <c>getNaturalRallyPoint</c>: the exit path in world space. The point of the data lies on the building's edge ("NaturalRallyPointX must always match GeometryMajorRadius" in
    /// the retail data); the reference moves it two pathfinding cells along its own direction in model space (so a point that is not on the x axis also drifts sideways) before it
    /// transforms it - that is the exit point. The door point is the same point moved one cell only: the unit passes it on its way, so it leaves through the door side even when
    /// it has to stop beside the exit point because another unit stands there. A module whose data has no rally point (0, 0, 0) has no exit path.
    /// </summary>
    public virtual bool TryGetExitPath(out Fixed doorX, out Fixed doorY, out Fixed x, out Fixed y)
    {
        var point = Data.NaturalRallyPoint;
        doorX = x = Entity.X;
        doorY = y = Entity.Y;
        if (point.X == Fixed.Zero && point.Y == Fixed.Zero) return false;
        var length = Fixed.Sqrt(point.X * point.X + point.Y * point.Y + point.Z * point.Z);
        var cos = FixedTrig.Cos(Entity.Angle);
        var sin = FixedTrig.Sin(Entity.Angle);
        (Fixed X, Fixed Y) World(Fixed offset)
        {
            var px = point.X + point.X / length * offset;
            var py = point.Y + point.Y / length * offset;
            return (Entity.X + px * cos - py * sin, Entity.Y + px * sin + py * cos);
        }
        (doorX, doorY) = World(DoorOffset);
        (x, y) = World(RallyOffset);
        return true;
    }

    public virtual bool IsFreeToExit => true;

    public virtual bool ForcesSupplyWanting => false;

    public virtual (Fixed X, Fixed Y, Fixed Z, Fixed Angle) ClaimExit(IWorldServices world)
    {
        _ = world;
        var point = Data.UnitCreatePoint;
        var cos = FixedTrig.Cos(Entity.Angle);
        var sin = FixedTrig.Sin(Entity.Angle);
        return (Entity.X + point.X * cos - point.Y * sin, Entity.Y + point.X * sin + point.Y * cos, Entity.Z, Entity.Angle);
    }

    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

/// <summary><c>DefaultProductionExitUpdate.cpp</c>: always free; there is no door and no burst limit.</summary>
public sealed class DefaultProductionExitUpdate(ProductionExitData data) : ProductionExitBase(data)
{
    public override string ModuleName => "DefaultProductionExitUpdate";
}

/// <summary>
/// <c>SupplyCenterProductionExitUpdate.cpp</c>: the same creation point as the default exit; the fresh truck is told to start supplying at once. The reference also sends it over
/// the (not offset) <c>NaturalRallyPoint</c> first; here the supply AI takes the truck over directly, so this exit reports no exit point.
/// </summary>
public sealed class SupplyCenterProductionExitUpdate(ProductionExitData data) : ProductionExitBase(data)
{
    public override string ModuleName => "SupplyCenterProductionExitUpdate";
    public override bool ForcesSupplyWanting => true;

    public override bool TryGetExitPath(out Fixed doorX, out Fixed doorY, out Fixed x, out Fixed y)
    {
        doorX = x = Entity.X;
        doorY = y = Entity.Y;
        return false;
    }
}

/// <summary>
/// <c>QueueProductionExitUpdate.cpp</c>: units leave in bursts. The first <c>InitialBurst</c> units may leave back to back; after that
/// every exit starts an <c>ExitDelay</c> during which the door is closed (<c>isFreeToExit</c>: bursting || delay == 0). The counter runs
/// down once per tick exactly as the reference update does, and the producer holds a finished unit until the door is free.
/// </summary>
public sealed class QueueProductionExitUpdate(ProductionExitData exitData) : ProductionExitBase(exitData), IUpdateModule
{
    private long _nextWake = -1;
    private long _delay;
    private int _burst = exitData.InitialBurst;

    public override string ModuleName => "QueueProductionExitUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public override bool IsFreeToExit => _burst > 0 || _delay == 0;

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    public override (Fixed X, Fixed Y, Fixed Z, Fixed Angle) ClaimExit(IWorldServices world)
    {
        var position = base.ClaimExit(world);
        _delay = Data.ExitDelayTicks;
        if (_burst > 0) _burst--;
        return position;
    }

    public long Update(IWorldServices world)
    {
        _ = world;
        if (IsFreeToExit) _delay = 0;
        else _delay--;
        return SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("delay", _delay);
        writer.Int("burst", _burst);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _delay = reader.Int("delay");
        _burst = checked((int)reader.Int("burst"));
    }
}

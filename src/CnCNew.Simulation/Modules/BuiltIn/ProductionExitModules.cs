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
/// The rally point, <c>UseSpawnRallyPoint</c> and <c>GrantTemporaryStealth</c> are typed by the catalog but have no consumer: they steer
/// the produced unit's AI (exit path, movement) or stealth, which phase 6 owns.
/// </summary>
public sealed record ProductionExitData(Point3 UnitCreatePoint, long ExitDelayTicks, int InitialBurst) : ModuleData
{
    public static ProductionExitData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Vector(configuration, "UnitCreatePoint"),
        ModuleConfigReader.Ticks(configuration, "ExitDelay", 0),
        checked((int)ModuleConfigReader.Int(configuration, "InitialBurst", 0)));
}

/// <summary>
/// Shared exit geometry (<c>DefaultProductionExitUpdate::exitObjectViaDoor</c> and its two siblings): the new object appears at the
/// producer's transformed <c>UnitCreatePoint</c> on the ground, facing the producer's own orientation.
/// </summary>
public abstract class ProductionExitBase(ProductionExitData exitData) : SimModule, IProductionExit
{
    protected ProductionExitData Data { get; } = exitData;

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

/// <summary><c>SupplyCenterProductionExitUpdate.cpp</c>: the same exit as the default one (the stealth grant and rally movement are AI/stealth, see <see cref="ProductionExitData"/>).</summary>
public sealed class SupplyCenterProductionExitUpdate(ProductionExitData data) : ProductionExitBase(data)
{
    public override string ModuleName => "SupplyCenterProductionExitUpdate";
    public override bool ForcesSupplyWanting => true;
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

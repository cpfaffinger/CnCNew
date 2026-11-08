using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ChinookHeadingFiles() => new Dictionary<string, string>(SpiralFiles())
    {
        ["Data/INI/Object/ChinookHeading.ini"] = """
        Locomotor SlowRotorLoco
          Surfaces = AIR
          AirborneTargetingHeight = 30
          Speed = 140
          Acceleration = 900
          Braking = 900
          TurnRate = 90
          MinTurnSpeed = 0
          PreferredHeight = 100
          Appearance = HOVER
        End
        Object SlowLifter
          KindOf = VEHICLE AIRCRAFT TRANSPORT HARVESTER
          Locomotor = SET_NORMAL SlowRotorLoco
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = ChinookAIUpdate AI01
            MaxBoxes = 2
            SupplyCenterActionDelay = 0
            SupplyWarehouseActionDelay = 0
            SupplyWarehouseScanDistance = 700
          End
          Behavior = TransportContain Transport01
            Slots = 4
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    /// <summary>
    /// F-157: a Chinook that turns publishes the heading its frame ends with, as the ground movement does, so that whoever reads it in the frame's first tick - a death: the helicopter spins
    /// on from the heading it died with - reads the heading of the 30 Hz step. The Chinook's second step (its climb) publishes the rest of the frame after the movement and cleared the heading
    /// (Bitter Winter ChinaTank against AirForce, frame 21,075: a shot-down Chinook died 0.026 radians short of the 30 Hz heading).
    /// </summary>
    public static void AChinookPublishesTheHeadingItsFrameEndsWith()
    {
        List<long>? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder) = FlightRateWorld(rate, ChinookHeadingFiles());
            feeder.Spawn(1, "SlowLifter", x: 500, y: 500);
            StepFrames(world, 3);
            var chinook = Only(world, "SlowLifter");
            feeder.SetMoveTarget(1, chinook.Id.Value, 100, 500);
            var headings = new List<long>();
            for (var frame = 0; frame < 30; frame++)
            {
                for (var tick = 0; tick < factor; tick++)
                {
                    world.Step();
                    // After the frame's first tick a reader sees the heading the frame ends with.
                    if (tick == 0) headings.Add(chinook.FrameEndAngle(world.Tick).Raw);
                }
            }
            Check.That(headings.Distinct().Count() > 5, $"{rate}: the Chinook turns toward its goal ({string.Join(", ", headings.Take(10))}).");
            reference ??= headings;
            SameSeries(reference, headings, $"The heading a Chinook's frame ends with, read in its first tick, at {rate}");
        }
    }
}

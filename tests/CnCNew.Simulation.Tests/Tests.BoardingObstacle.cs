using System.Linq;
using CnCNew.Simulation.Ids;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> BoardingFiles() => new Dictionary<string, string>(TransportFiles())
    {
        ["Data/INI/Object/BoardingFixture.ini"] = """
        Object WideHauler
          KindOf = VEHICLE SELECTABLE TRANSPORT
          Geometry = CYLINDER
          GeometryMajorRadius = 12.0
          GeometryMinorRadius = 12.0
          Locomotor = SET_NORMAL WalkLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = TransportAIUpdate AI01
          End
          Behavior = TransportContain Transport01
            Slots = 5
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object WideGrunt
          KindOf = INFANTRY SELECTABLE
          TransportSlotCount = 1
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryMinorRadius = 6.0
          Locomotor = SET_NORMAL WalkLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    /// <summary>
    /// Infantry ordered into a standing transport on a map with a path grid. The transport's own bounding circle used to block the last steps (it was an obstacle like any other
    /// unit), so the boarder walked circles around it and never got in; the reference tells the pathfinder to ignore the object an enter order aims at
    /// (<c>AIEnterState::onEnter</c>: <c>ai->ignoreObstacle(goal)</c>). Three boarders from three sides get in, in the same logic frame at 30, 60 and 120 Hz.
    /// </summary>
    public static void BoardersWalkIntoTheirTransport()
    {
        long[]? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, BoardingFiles(), NavTerrain());
            feeder.Spawn(1, "WideHauler", x: 100, y: 100);
            feeder.Spawn(1, "WideGrunt", x: 30, y: 100);
            feeder.Spawn(1, "WideGrunt", x: 100, y: 30);
            feeder.Spawn(1, "WideGrunt", x: 170, y: 150);
            for (var tick = 0; tick < factor; tick++) world.Step();
            var hauler = world.Entities.Single(value => value.Template.Name == "WideHauler");
            var grunts = world.Entities.Where(value => value.Template.Name == "WideGrunt").OrderBy(value => value.Id.Value).ToList();
            foreach (var grunt in grunts) Check.That(feeder.MoveAndEnter(1, grunt.Id.Value, hauler.Id.Value).Accepted, $"{rate}: the grunt is ordered in.");
            var entered = new long[grunts.Count];
            Array.Fill(entered, -1);
            for (var frame = 0; frame < 450; frame++)
            {
                for (var tick = 0; tick < factor; tick++) world.Step();
                for (var index = 0; index < grunts.Count; index++)
                    if (entered[index] < 0 && grunts[index].ContainedBy == hauler.Id) entered[index] = frame;
            }
            Check.That(entered.All(frame => frame >= 0), $"{rate}: every boarder got into the transport within 15 s (frames {string.Join(", ", entered)}; -1: not in: {string.Join(" ", grunts.Select(grunt => $"({grunt.X},{grunt.Y} moving {grunt.HasMoveTarget})"))}).");
            if (reference is null)
            {
                reference = entered;
                Console.WriteLine($"       boarding: three boarders enter a standing transport in frames {string.Join(", ", entered)}");
            }
            else Check.That(entered.SequenceEqual(reference), $"{rate}: the boarders enter in the logic frames of 30 Hz ({string.Join(", ", entered)} against {string.Join(", ", reference)}).");
        }
    }
}

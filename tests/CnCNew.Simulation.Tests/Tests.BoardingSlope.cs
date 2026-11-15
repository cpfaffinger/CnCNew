using System.Collections.Generic;
using System.Linq;

/// <summary>
/// #305: a boarder goes into the container it touches when the two overlap in height (<c>OpenContain::onCollide</c>, the partition manager's three-dimensional collision). Above 30 Hz
/// the overlap was asked in the frame's first tick with the boarder a share of its step lower on a slope than the 30 Hz boarder, which has taken its whole step: it went in a frame
/// later (Alpine Assault ChinaTank against AirForce, a Red Guard climbing to a transport at frame 19,865).
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> BoardingSlopeFiles() => new Dictionary<string, string>(TransportFiles())
    {
        ["Data/INI/Object/BoardingSlopeFixture.ini"] = """
        Object SlopeHauler
          KindOf = VEHICLE SELECTABLE TRANSPORT
          Geometry = CYLINDER
          GeometryMajorRadius = 12.0
          GeometryMinorRadius = 12.0
          GeometryHeight = 4.0
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
        End
        Object SlopeGrunt
          KindOf = INFANTRY SELECTABLE
          TransportSlotCount = 1
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryMinorRadius = 6.0
          GeometryHeight = 7.6
          Locomotor = SET_NORMAL WalkLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>A boarder climbs a slope to a transport standing above it; it goes in in the same frame at 30, 60 and 120 Hz, from four starting places.</summary>
    public static void ABoarderClimbingToItsTransportGoesInAsAt30Hz()
    {
        var reference = new Dictionary<int, long>();
        foreach (var (rate, factor) in WorldRates)
            for (var start = 0; start <= 3; start++)
            {
                // Half a unit of height per unit eastwards: the boarder's top reaches the transport's base 15.2 units before it, inside a frame's step of one unit.
                var (world, feeder, _) = RatedWorld(rate, BoardingSlopeFiles(), NavTerrain(height: (x, _) => x * 8));
                feeder.Spawn(1, "SlopeHauler", x: 150, y: 100);
                feeder.Spawn(1, "SlopeGrunt", x: 40 + start, y: 100);
                for (var tick = 0; tick < factor; tick++) world.Step();
                var hauler = world.Entities.Single(value => value.Template.Name == "SlopeHauler");
                var grunt = world.Entities.Single(value => value.Template.Name == "SlopeGrunt");
                Check.That(feeder.MoveAndEnter(1, grunt.Id.Value, hauler.Id.Value).Accepted, $"{rate}: the grunt is ordered in.");
                var entered = -1L;
                for (var frame = 0; frame < 450 && entered < 0; frame++)
                {
                    for (var tick = 0; tick < factor; tick++) world.Step();
                    if (grunt.ContainedBy == hauler.Id) entered = frame;
                }
                Check.That(entered >= 0, $"{rate}, start {start}: the grunt gets into the transport.");
                if (!reference.TryGetValue(start, out var expected)) reference[start] = entered;
                else Check.That(entered == expected, $"{rate}, start {start}: the grunt goes in in the frame of 30 Hz ({entered} against {expected}).");
            }
    }
}

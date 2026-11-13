using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

/// <summary>
/// #303: a rider that gets into a transport in its own update of a frame's first tick, and is put out again by the transport's death later in the updates of the same tick
/// (<c>OpenContain::scatterToNearbyPosition</c>). At 30 Hz the woken rider decides again in that tick; above 30 Hz the entry had held its next decision to the end of the frame it
/// was covering (<c>SettleFrame</c>), so it walked away a frame late (Alpine Assault America against China, a Red Guard at frame 9,312).
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ScatterAfterBoardingFiles() => new Dictionary<string, string>(BoardingFiles())
    {
        ["Data/INI/Object/ScatterAfterBoarding.ini"] = """
        Object DyingHauler
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
          Behavior = LifetimeUpdate Life01
            MinLifetime = 20000
            MaxLifetime = 20000
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    /// <summary>
    /// A grunt is ordered into a transport that dies by its lifetime in the frame the grunt gets in (the order is timed by a first run with a transport that lives on). The grunt is put
    /// out and walks off in the same frame of the original at 30, 60 and 120 Hz, and stands where the 30 Hz grunt stands at the end of every frame after.
    /// </summary>
    public static void ARiderPutOutInTheFrameItBoardedWalksOffAsAt30Hz()
    {
        const long death = 600;
        List<(Fixed X, Fixed Y)>? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            long Boarded(string hauler, long orderFrame, List<(Fixed X, Fixed Y)>? after, out long exitFrame)
            {
                var (world, feeder, _) = RatedWorld(rate, ScatterAfterBoardingFiles(), NavTerrain());
                feeder.Spawn(1, "WideGrunt", x: 30, y: 100);
                feeder.Spawn(1, hauler, x: 100, y: 100);
                world.Step();
                var grunt = world.Entities.Single(value => value.Template.Name == "WideGrunt");
                var transport = world.Entities.Single(value => value.Template.Name == hauler);
                while (world.Tick < world.Time.OriginalFrames(orderFrame)) world.Step();
                Check.That(feeder.MoveAndEnter(1, grunt.Id.Value, transport.Id.Value).Accepted, $"{rate}: the grunt is ordered in.");
                long entered = -1;
                exitFrame = -1;
                while (world.Tick < world.Time.OriginalFrames(death + 60))
                {
                    var tick = world.Tick;
                    var events = world.Step().Events;
                    if (entered < 0 && grunt.ContainedBy == transport.Id) entered = world.Time.ToOriginalFrames(tick);
                    if (exitFrame < 0 && events.Any(value => value.Kind == SimEventKind.EntityUncontained && value.Entity == grunt.Id)) exitFrame = world.Time.ToOriginalFrames(tick);
                    if (entered < 0 && exitFrame >= 0) entered = exitFrame;
                    if (after is not null && exitFrame >= 0 && world.Time.IsOriginalFrameBoundary(world.Tick)) after.Add((grunt.X, grunt.Y));
                }
                return entered;
            }

            // The first run finds the frame the grunt gets in; the second gives the order that much earlier that it gets in when the transport dies.
            var probe = Boarded("WideHauler", 30, null, out _);
            Check.That(probe > 30, $"{rate}: the grunt gets into a transport that lives on (frame {probe}).");
            var positions = new List<(Fixed X, Fixed Y)>();
            var entered = Boarded("DyingHauler", 30 + death - probe, positions, out var exit);
            System.Console.WriteLine($"       {rate}: in at frame {entered}, out at frame {exit}, {positions.Count} frames after; first {(positions.Count > 0 ? $"{positions[0].X.ToDecimal():0.000},{positions[0].Y.ToDecimal():0.000}" : "-")}, last {(positions.Count > 0 ? $"{positions[^1].X.ToDecimal():0.000},{positions[^1].Y.ToDecimal():0.000}" : "-")}");
            Check.That(entered == death && exit == death, $"{rate}: the grunt gets in and is put out in the frame the transport dies (in {entered}, out {exit}, death {death}).");
            Check.That(positions.Count > 10 && positions[^1] != positions[0], $"{rate}: the grunt walks off.");
            if (reference is null)
            {
                reference = positions;
                continue;
            }
            var parted = Enumerable.Range(0, Math.Min(positions.Count, reference.Count)).FirstOrDefault(index => (positions[index].X - reference[index].X).Abs() > Fixed.FromDecimal(0.001m) || (positions[index].Y - reference[index].Y).Abs() > Fixed.FromDecimal(0.001m), -1);
            Check.That(parted < 0 && positions.Count == reference.Count, $"{rate}: the grunt stands where it stands at 30 Hz at the end of every frame after it was put out (first difference {parted} frames after: {(parted >= 0 ? $"{positions[parted].X.ToDecimal()},{positions[parted].Y.ToDecimal()} against {reference[parted].X.ToDecimal()},{reference[parted].Y.ToDecimal()}" : "-")}).");
        }
    }
}

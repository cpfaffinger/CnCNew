using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;

/// <summary>
/// <c>SlowDeathBehavior::update</c> sinks the body after the logic frame's step: a body that still walks while it sinks ends every frame <c>SinkRate</c> below the ground where the
/// step ends, at 30 Hz. Above 30 Hz the shares of the step put it back on the ground in every tick, and the sink of the frame's first tick was lost with the last share.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> SinkingWalkerFiles() => new Dictionary<string, string>(TransportFiles())
    {
        ["Data/INI/Object/SinkingWalker.ini"] = """
        Object SinkingWalker
          KindOf = INFANTRY SELECTABLE
          Locomotor = SET_NORMAL WalkLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 20000
            SinkDelay = 0
            SinkRate = 3
          End
        End
        """,
    };

    /// <summary>
    /// A body that walks while its slow death runs sinks one <c>SinkRate</c> per frame under the ground it stands on, where each frame ends, at 30, 60 and 120 Hz. Before, the
    /// last share of every frame stood it on the ground again above 30 Hz.
    /// </summary>
    public static void ASinkingBodyThatWalksEndsEachFrameBelowTheGround()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, SinkingWalkerFiles(), NavTerrain());
            Check.That(feeder.Spawn(1, "SinkingWalker", owner: 1, x: 40, y: 100).Accepted, $"{rate}: the walker spawns.");
            for (var tick = 0; tick < factor; tick++) world.Step();
            var walker = world.Entities.Single(value => value.Template.Name == "SinkingWalker");
            var ground = walker.Z;
            Check.That(feeder.SetMoveTarget(1, walker.Id.Value, 160, 100).Accepted, $"{rate}: the walker is ordered on.");
            world.Step();
            for (var tick = 1; tick < factor; tick++) world.Step();
            walker.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.SlowDeathBehavior>().Single().BeginSlowDeath(world);
            var sunk = new List<Fixed>();
            var x = walker.X;
            var perFrame = world.Time.TicksPerOriginalFrame;
            for (var frame = 0; frame < 40 && world.FindEntity(walker.Id) is not null; frame++)
            {
                for (var tick = 0; tick < factor; tick++) world.Step();
                // Frames in which it has walked on: its step is the frame's.
                if (frame >= 6 && walker.X > x) sunk.Add(ground - walker.Z);
                x = walker.X;
            }
            Check.That(sunk.Count >= 10, $"{rate}: the body walks on while it sinks ({sunk.Count} frames; {perFrame} ticks to a frame).");
            Check.That(sunk.All(value => value == Fixed.FromInt(3) / Fixed.FromInt(30)), $"{rate}: every frame ends one sink below the ground ({string.Join(", ", sunk.Take(4))}).");
        }
    }
}

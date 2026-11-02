using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// <c>PartitionManager::queueUndoShroudReveal</c>: a look goes on for <c>UnlookPersistDuration</c> after the object stopped looking from there, and an object looks again only when it enters
/// another partition cell. Before, the fog closed in the frame a scout left or died.
/// </summary>
static partial class Tests
{
    private const string LookIni = """
        Object LookScout
          KindOf = VEHICLE SELECTABLE
          VisionRange = 30.0
          ShroudClearingRange = 30.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        """;

    public static void ALookGoesOnAfterItsObjectIsGone()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, feeder, fixture) = RatedWorld(rate, new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/Look.ini"] = LookIni }, NavTerrain());
            feeder.Spawn(1, "LookScout", owner: 1, x: 100, y: 100);
            for (var step = 0; step < world.Frames(6); step++) world.Step();
            var scout = world.Entities.Single(value => value.Template.Name == "LookScout");
            var player = new PlayerId(1);
            Check.That(world.IsPositionVisibleTo(player, Fixed.FromInt(100), Fixed.FromInt(100)), $"{rate}: it sees where it stands.");

            world.Kill(scout.Id);
            for (var step = 0; step < world.Frames(10) && world.FindEntity(scout.Id) is not null; step++) world.Step();
            Check.That(world.IsPositionVisibleTo(player, Fixed.FromInt(100), Fixed.FromInt(100)), $"{rate}: its last look stays.");

            var saved = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, NavTerrain());
            var gone = 0L;
            for (var step = 0; step < world.Frames(60); step++)
            {
                world.Step();
                saved.Step();
                Check.That(world.StateHash == saved.StateHash, $"{rate}: a save keeps it, step {step}.");
                if (gone == 0 && !world.IsPositionVisibleTo(player, Fixed.FromInt(100), Fixed.FromInt(100))) gone = world.InFrames(world.Tick);
            }
            Check.That(gone > 0, $"{rate}: and then the fog closes.");
            Check.That(gone >= 30 && gone <= 50, $"{rate}: one second after, the fixture's default ({gone}).");
        }
    }
}

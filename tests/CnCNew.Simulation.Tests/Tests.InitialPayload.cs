using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// <c>TransportContain::createPayload</c>: a Troop Crawler comes out of the factory full of Red Guards. <c>InitialPayload</c> was read for a bike's rider only, so every other transport came empty.
/// </summary>
static partial class Tests
{
    private const string PayloadIni = """
        Object PayloadSoldier
          KindOf = INFANTRY SELECTABLE
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        Object PayloadCrawler
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = TransportContain Contain01
            Slots = 8
            InitialPayload = PayloadSoldier 8
          End
        End
        """;

    public static void ATransportComesWithItsPayload()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, feeder, fixture) = RatedWorld(rate, new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/Payload.ini"] = PayloadIni });
            feeder.Spawn(1, "PayloadCrawler", owner: 1, x: 100, y: 100);
            for (var step = 0; step < rate.TicksPerOriginalFrame + 1; step++) world.Step();
            var saved = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
            for (var step = 0; step < world.Frames(5); step++)
            {
                world.Step();
                saved.Step();
                Check.That(world.StateHash == saved.StateHash, $"{rate}: a save in the first frames keeps the payload, step {step}.");
            }
            var crawler = world.Entities.Single(value => value.Template.Name == "PayloadCrawler");
            Check.That(crawler.Container!.Contained.Count == 8, $"{rate}: eight soldiers sit in it ({crawler.Container.Contained.Count}).");
            Check.That(world.Entities.Count(value => value.Template.Name == "PayloadSoldier") == 8 && world.Entities.Where(value => value.Template.Name == "PayloadSoldier").All(value => value.Owner == crawler.Owner),
                $"{rate}: and they are its owner's.");
        }
    }
}

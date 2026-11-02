using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
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
        Armor SecondSkin
          Armor = DEFAULT 50%
        End
        Object SecondLifeBus
          KindOf = VEHICLE SELECTABLE
          ArmorSet
            Conditions = None
          End
          ArmorSet
            Conditions = SECOND_LIFE
            Armor = SecondSkin
          End
          Body = UndeadBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
            SecondLifeMaxHealth = 100.0
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

    /// <summary><c>ArmorSet Conditions = SECOND_LIFE</c>: the Battle Bus wears its own armour after its first death.</summary>
    public static void ASecondLifeWearsItsArmour()
    {
        var (world, feeder, _) = RatedWorld(SimulationRate.FromTicksPerSecond(60), new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/Payload.ini"] = PayloadIni });
        feeder.Spawn(1, "SecondLifeBus", owner: 1, x: 100, y: 100);
        world.Step();
        var bus = world.Entities.Single(value => value.Template.Name == "SecondLifeBus");
        void Hit(int amount) => world.Damage(bus.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(amount), 0, DeathTypes.Normal, false));
        Hit(10);
        Check.That(bus.Body!.Health == Fixed.FromInt(90), $"The first life has no armour ({bus.Body.Health}).");
        Hit(500);
        Check.That(bus.Body is CnCNew.Simulation.Modules.BuiltIn.UndeadBody { IsSecondLife: true }, "A lethal blow begins the second life.");
        var health = bus.Body.Health;
        Hit(10);
        Check.That(health - bus.Body.Health == Fixed.FromInt(5), $"The second life takes half ({health - bus.Body.Health}).");
    }

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

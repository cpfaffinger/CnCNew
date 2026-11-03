using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private const string RevealWakesIni = """
        Object RevealGunner
          KindOf = SELECTABLE VEHICLE
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AcqSlowGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 10000
          End
        End
        Object RevealCloak
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = StealthUpdate Stealth01
            InnateStealth = Yes
            OrderIdleEnemiesToAttackMeUponReveal = Yes
          End
        End
        Object PlainCloak
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = StealthUpdate Stealth01
            InnateStealth = Yes
          End
        End
        """;

    /// <summary>
    /// <c>StealthUpdate::markAsDetected</c> with <c>OrderIdleEnemiesToAttackMeUponReveal</c> (<c>setWakeupIfInRange</c>): an enemy within its vision range of a detected object looks for a victim at
    /// once, in the same frame at 30, 60 and 120 Hz; without the key it waits for its next look, ten seconds away.
    /// </summary>
    public static void ARevealWakesTheEnemiesThatSeeIt()
    {
        long Attacked(SimulationRate rate, string cloak)
        {
            var files = new Dictionary<string, string>(AcquisitionFiles()) { ["Data/INI/Object/RevealWakes.ini"] = RevealWakesIni };
            var fixture = new CnCNew.Simulation.Testing.SyntheticFixture(rate, files: files);
            System.Collections.Immutable.ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, CnCNew.Simulation.Testing.SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "RevealGunner", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, cloak, owner: 2, x: 60, y: 0);
            for (var frame = 0; frame < 60; frame++)
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var gunner = world.Entities.Single(value => value.Template.Name == "RevealGunner");
            var hidden = world.Entities.Single(value => value.Template.Name == cloak);
            Check.That(hidden.HasStatus(ObjectStatusBits.Index("STEALTHED")) && !world.AcquiredVictimOf(gunner).IsValid, $"{rate}: the {cloak} is hidden and nobody attacks it.");
            hidden.Modules.OfType<StealthUpdate>().Single().MarkDetected(world, world.Frames(900));
            for (var frame = 1; frame <= 120; frame++)
            {
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
                if (world.AcquiredVictimOf(gunner) == hidden.Id) return frame;
            }
            return -1;
        }

        var frames = new List<long>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var frame = Attacked(rate, "RevealCloak");
            Check.That(frame is > 0 and <= 5, $"{rate}: the gunner that sees the revealed object attacks it at once (frame {frame}).");
            frames.Add(frame);
            Check.That(Attacked(rate, "PlainCloak") < 0, $"{rate}: without the key it waits for its next look.");
        }
        Check.That(frames.Distinct().Count() == 1, $"The attack begins in the same frame at every rate ({string.Join(", ", frames)}).");
    }
}

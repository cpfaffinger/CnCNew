using System.Collections.Generic;
using System.Linq;

/// <summary>
/// #305: <c>ProductionUpdate::update</c> compares the frames a unit has been under construction with <c>calcTimeToBuild</c> of that frame, once per logic frame of the original. Above
/// 30 Hz the port asked every tick, and a power consumer lost in the frame's first tick after the factory's update shortened the build in the frame's second tick already: the unit
/// came out a frame early (Alpine Assault China against GLA, a factory's unit at frame 43,686 after a Gattling cannon died).
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ProductionPowerFrameFiles() => new Dictionary<string, string>(RetailModuleFiles())
    {
        ["Data/INI/GameData.ini"] = "GameData\n  MinLowEnergyProductionSpeed = 0.5\n  MaxLowEnergyProductionSpeed = 0.8\n  LowEnergyPenaltyModifier = 1.0\nEnd\n",
        ["Data/INI/Object/ProductionPowerFrame.ini"] = """
        Object PpfPlant
          KindOf = STRUCTURE
          EnergyProduction = 10
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
        End
        Object PpfMortalConsumer
          KindOf = STRUCTURE
          EnergyProduction = -20
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = LifetimeUpdate Life01
            MinLifetime = 1500
            MaxLifetime = 1500
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    /// <summary>
    /// A factory builds a 30-frame unit at half speed (half the supply); the consumer that halves it dies by its lifetime after the factory's update when the build has run more
    /// than 30 frames. The unit comes out in the same frame at 30, 60 and 120 Hz.
    /// </summary>
    public static void ALostConsumerShortensABuildFromTheNextFrame()
    {
        var frames = new List<long>();
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, ProductionPowerFrameFiles());
            feeder.Spawn(1, "DefaultExitFactory", x: 300, y: 300);
            feeder.Spawn(1, "PpfPlant", x: 100, y: 100);
            feeder.Spawn(1, "PpfMortalConsumer", x: 100, y: 200);
            world.Step();
            feeder.GiveMoney(1, 1000);
            world.Step();
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            var factory = world.Entities.Single(entity => entity.Template.Name == "DefaultExitFactory");
            var consumer = world.Entities.Single(entity => entity.Template.Name == "PpfMortalConsumer");
            Check.That(consumer.Id.Value > factory.Id.Value, $"{rate}: the consumer updates after the factory.");
            Check.That(feeder.QueueProduction(1, factory.Id.Value, "QuickUnit").Accepted, $"{rate}: the unit is queued.");
            long died = -1, built = -1;
            while (built < 0 && world.Tick < world.Time.OriginalFrames(200))
            {
                var tick = world.Tick;
                world.Step();
                if (died < 0 && consumer.EffectivelyDead) died = world.Time.ToOriginalFrames(tick);
                if (built < 0 && CountOf(world, "QuickUnit") > 0) built = world.Time.ToOriginalFrames(tick);
            }
            System.Console.WriteLine($"       {rate}: the consumer dies in frame {died}, the unit comes out in frame {built}");
            Check.That(died > 31 && built > died, $"{rate}: the consumer dies after 30 frames of the build, and the unit comes out after it (died {died}, built {built}).");
            frames.Add(built);
        }
        Check.That(frames.Distinct().Count() == 1, $"The unit comes out in the same frame at every rate ({string.Join(", ", frames)}).");
    }
}

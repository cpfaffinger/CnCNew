using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// <c>SlowDeathBehavior::beginSlowDeath</c> and <c>update</c>: a body is flung into the air, its timers wait while it flies, and it goes into the ground from its sink frame on. The port kept the
/// keys and did neither: a dead soldier lay where he fell and vanished whole.
/// </summary>
static partial class Tests
{
    private const string FlingIni = """
        Object FlungSoldier
          KindOf = INFANTRY SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 5.0
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 4000
            SinkDelay = 2000
            SinkRate = 30
            FlingForce = 8
            FlingPitch = 60
          End
        End
        Object SunkenTank
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 3000
            SinkDelay = 1000
            SinkRate = 30
          End
        End
        """;

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) FlingWorld(SimulationRate rate)
    {
        var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/Fling.ini"] = FlingIni });
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        world.Step();
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        return (world, feeder, fixture);
    }

    public static void ABodyIsFlungAndSinks()
    {
        var ends = new List<long>();
        var downs = new List<long>();
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, feeder, _) = FlingWorld(rate);
            feeder.Spawn(1, "FlungSoldier", owner: 1, x: 300, y: 300);
            for (var step = 0; step < world.Frames(3); step++) world.Step();
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            var soldier = world.Entities.Single(value => value.Template.Name == "FlungSoldier");
            var id = soldier.Id;
            var death = soldier.Modules.OfType<SlowDeathBehavior>().Single();
            var ground = soldier.Z;
            var (startX, startY) = (soldier.X, soldier.Y);
            var born = world.Tick;
            world.Kill(id);
            Check.That(soldier.Z == ground + Fixed.One, $"{rate}: the body starts a little above the ground.");
            Check.That(death.ShowsFlailing, $"{rate}: EXPLODED_FLAILING from the death on.");
            var peak = Fixed.Zero;
            var down = 0L;
            var lowest = ground;
            while (world.FindEntity(id) is not null && world.Tick < born + world.Frames(600))
            {
                world.Step();
                if (world.FindEntity(id) is null) break;
                peak = Fixed.Max(peak, soldier.Z - ground);
                lowest = Fixed.Min(lowest, soldier.Z);
                if (down == 0 && death.ShowsBouncing) down = world.Time.ToOriginalFrames(world.Tick - 1 - born);
            }
            var end = world.Time.ToOriginalFrames(world.Tick - 1 - born);
            Check.That(peak > Fixed.FromInt(5), $"{rate}: the body flies ({peak}).");
            Check.That(soldier.X != startX || soldier.Y != startY, $"{rate}: it comes down somewhere else.");
            Check.That(down > 0 && down < 120, $"{rate}: it lands ({down}).");
            // 120 frames of the delay plus the frames in the air.
            Check.That(end > 120 && end <= 120 + down + 2, $"{rate}: the timers waited while it flew ({end}, in the air {down}).");
            Check.That(lowest < ground - Fixed.FromInt(50), $"{rate}: it sank ({ground - lowest}).");
            ends.Add(end);
            downs.Add(down);
        }
        Check.That(ends.Distinct().Count() == 1, $"The end falls in the same frame at every rate ({string.Join(", ", ends)}; down {string.Join(", ", downs)}).");
        Check.That(downs.Distinct().Count() == 1, $"The landing falls in the same frame at every rate ({string.Join(", ", downs)}).");
    }

    public static void AHulkSinksFromItsSinkFrame()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, feeder, _) = FlingWorld(rate);
            feeder.Spawn(1, "SunkenTank", owner: 1, x: 300, y: 300);
            world.Step();
            var tank = world.Entities.Single(value => value.Template.Name == "SunkenTank");
            var ground = tank.Z;
            world.Kill(tank.Id);
            for (var step = 0; step < world.Frames(29); step++) world.Step();
            Check.That(tank.Z == ground, $"{rate}: it stands until its sink frame.");
            for (var step = 0; step < world.Frames(11); step++) world.Step();
            var sunk = ground - tank.Z;
            Check.That(sunk >= Fixed.FromInt(9) && sunk <= Fixed.FromInt(12), $"{rate}: one unit a frame from then on ({sunk}).");
        }
    }

    /// <summary>A world saved in the air or in the ground goes on as the one that was not.</summary>
    public static void AFlungBodySurvivesASave()
    {
        foreach (var save in new[] { 5, 60, 100 })
        {
            var (world, feeder, fixture) = FlingWorld(SimulationRate.FromTicksPerSecond(60));
            feeder.Spawn(1, "FlungSoldier", owner: 1, x: 300, y: 300);
            world.Step();
            world.Kill(world.Entities.Single(value => value.Template.Name == "FlungSoldier").Id);
            for (var step = 0; step < world.Frames(save); step++) world.Step();
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
            for (var step = 0; step < world.Frames(200); step++)
            {
                world.Step();
                restored.Step();
                Check.That(world.StateHash == restored.StateHash, $"Saved at frame {save}: the same at step {step}.");
            }
        }
    }
}

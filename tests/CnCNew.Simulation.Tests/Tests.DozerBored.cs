using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-67, the bored dozer (<c>DozerAIUpdate.cpp</c>, <c>DozerPrimaryIdleState::update</c>, <c>findObjectToRepair</c>): a dozer that stands idle without a task for more than
/// <c>BoredTime</c> repairs the closest hurt structure of its player within <c>BoredRange</c>.
/// </summary>
static partial class Tests
{
    private const string DozerBoredIni = """
        Locomotor BoredLoco
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 30.0
          Acceleration = 30.0
          Braking = 30.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Object BoredDozer
          KindOf = SELECTABLE VEHICLE DOZER
          Locomotor = SET_NORMAL BoredLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DozerAIUpdate AI01
            RepairHealthPercentPerSecond = 10%
            BoredTime = 5000
            BoredRange = 150
          End
        End
        Object BoredHut
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 500.0
          End
        End
        Object BoredHole
          KindOf = SELECTABLE STRUCTURE REBUILD_HOLE
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 100.0
          End
        End
        """;

    /// <summary>
    /// The dozer drives 60 units and stands; 150 frames of the original after the frame it arrived in it goes to the hut 100 units away (not to the farther, more hurt hut beyond
    /// its range, not to the enemy's hut beside it, not to the hole next to it) and repairs it - in the same frame at 30, 60 and 120 Hz; a snapshot taken while it waits continues
    /// identically. Before, a dozer never repaired by itself.
    /// </summary>
    public static void DozerRepairsWhenBored()
    {
        (long Bored, long Healed) Run(SimulationRate rate, bool save)
        {
            var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/DozerBored.ini"] = DozerBoredIni });
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "BoredDozer", owner: 1, x: 100, y: 100).Accepted, "The dozer spawns.");
            Check.That(feeder.Spawn(1, "BoredHut", owner: 1, x: 260, y: 100).Accepted, "The near hut spawns.");
            Check.That(feeder.Spawn(1, "BoredHut", owner: 1, x: 160, y: 380).Accepted, "The far hut spawns.");
            Check.That(feeder.Spawn(2, "BoredHut", owner: 2, x: 200, y: 140).Accepted, "The enemy's hut spawns.");
            Check.That(feeder.Spawn(1, "BoredHole", owner: 1, x: 170, y: 60).Accepted, "The hole spawns.");
            world.Step();
            var dozer = world.Entities.Single(value => value.Template.Name == "BoredDozer");
            var near = world.Entities.Single(value => value.Template.Name == "BoredHut" && value.X == Fixed.FromInt(260));
            var others = world.Entities.Where(value => value != near && value != dozer).ToArray();
            var ai = dozer.Modules.OfType<DozerAIUpdate>().Single();
            Check.That(feeder.SetMoveTarget(1, dozer.Id.Value, 160, 100).Accepted, "The dozer is sent 60 units on.");
            var start = world.Tick;
            long Frame() => (world.Tick - start + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
            long arrived = -1, bored = -1, healed = -1;
            for (var step = 0; step < world.Frames(400) && healed < 0; step++)
            {
                if (save && arrived >= 0 && step == world.Frames(100))
                {
                    var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                    for (var follow = 0; follow < restored.Frames(80); follow++)
                        Check.That(restored.Step().StateHash == world.Step().StateHash, $"{rate}: the restored world waits and repairs as the live one (step {follow}).");
                }
                world.Step();
                if (arrived < 0 && !dozer.HasMoveTarget) arrived = Frame();
                if (bored < 0 && ai.HasBuilderTask) bored = Frame();
                if (healed < 0 && near.Body!.Health > Fixed.FromInt(500)) healed = Frame();
            }
            Check.That(arrived > 0 && bored > arrived, $"{rate}: the dozer arrives, then gets bored ({arrived}, {bored}).");
            Check.That(healed > bored, $"{rate}: it repairs the near hut ({healed}).");
            Check.That(others.All(value => value.Body!.Health < value.Body.MaxHealth && (value.Template.Name != "BoredHut" || value.Body.Health == Fixed.FromInt(500))),
                $"{rate}: nothing else is repaired.");
            Check.That(!ai.LastCommandFromPlayer, $"{rate}: the repair is not the player's order.");
            return (bored - arrived, healed);
        }

        var reference = Run(SimulationRate.Hz30, false);
        Check.That(reference.Bored == 150, $"The dozer is bored 150 frames after it arrived (the last frame it moved and 151 frames after it; {reference.Bored}).");
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = Run(rate, false);
            Check.That(other == reference, $"{rate}: bored and repairing in the same frames ({other} against {reference}).");
        }
        Run(TestRate.Default, true);
    }
}

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
        (long Bored, long Healed, long Again) Run(SimulationRate rate, bool save)
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
            // Frames of the dozer's own grid (from the tick it appeared in), which is a tick off the world's above 30 Hz: the decisions it takes lie on it.
            long Frame() => (world.Tick - dozer.SpawnTick + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame - (start - dozer.SpawnTick) / world.Time.TicksPerOriginalFrame;
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
            // F-72: the repair ends (the hut is whole) in the first tick of a frame above 30 Hz; the idle state is entered then, and the dozer is bored again 150 frames later
            // when the hut is hurt once more - not a frame earlier.
            long done = -1, again = -1;
            for (var step = 0; step < world.Frames(800) && again < 0; step++)
            {
                world.Step();
                if (done < 0 && !ai.HasBuilderTask)
                {
                    done = Frame();
                    Check.That(feeder.Damage(near.Id.Value, 300).Accepted, $"{rate}: the hut is hurt again.");
                }
                if (done >= 0 && again < 0 && ai.HasBuilderTask) again = Frame();
            }
            Check.That(done > healed && again > done, $"{rate}: the repair ends and the dozer repairs again ({done}, {again}).");
            return (bored - arrived, healed, again - done);
        }

        var reference = Run(SimulationRate.Hz30, false);
        Check.That(reference.Bored == 150, $"The dozer is bored 150 frames after it arrived (the last frame it moved and 151 frames after it; {reference.Bored}).");
        Check.That(reference.Again == 151, $"The dozer is bored again 151 frames after the frame its repair ended in ({reference.Again}).");
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = Run(rate, false);
            Check.That(other == reference, $"{rate}: bored and repairing in the same frames ({other} against {reference}).");
        }
        Run(TestRate.Default, true);
    }

    /// <summary>
    /// A structure first hurt in the frame the dozer looks is not hurt for its look: at 30 Hz the look comes with the frame's update, before what strikes in that frame, and above 30 Hz it stands at
    /// the frame's end and must not see further. The dozer takes the repair a whole <c>BoredTime</c> later at 30, 60 and 120 Hz. Before, the look saw the hurt a frame early above 30 Hz.
    /// </summary>
    public static void BoredDozerSeesNoHurtOfItsOwnFrame()
    {
        long Run(SimulationRate rate)
        {
            var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/DozerBored.ini"] = DozerBoredIni });
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "BoredDozer", owner: 1, x: 160, y: 100).Accepted && feeder.Spawn(1, "BoredHut", owner: 1, x: 260, y: 100).Accepted, "Dozer and hut spawn.");
            world.Step();
            var dozer = world.Entities.Single(value => value.Template.Name == "BoredDozer");
            var hut = world.Entities.Single(value => value.Template.Name == "BoredHut");
            var ai = dozer.Modules.OfType<DozerAIUpdate>().Single();
            var perFrame = world.Time.TicksPerOriginalFrame;
            long Frame() => (world.Tick - 1 - dozer.SpawnTick) / perFrame;
            // The first repair makes the hut whole; the idle state is entered when it ends.
            long done = -1;
            for (var step = 0; step < world.Frames(800) && done < 0; step++)
            {
                world.Step();
                if (ai.HasBuilderTask) continue;
                if (hut.Body!.Health == hut.Body.MaxHealth) done = Frame();
            }
            Check.That(done > 0, $"{rate}: the first repair ends.");
            // The next look comes 151 frames later: the hut is struck after the first tick of that frame.
            while (Frame() < done + 151 || (world.Tick - 1 - dozer.SpawnTick) % perFrame != 0) world.Step();
            Check.That(feeder.Damage(hut.Id.Value, 300).Accepted, $"{rate}: the hut is hurt in the frame of the look.");
            for (var step = 0; step < world.Frames(100); step++) world.Step();
            Check.That(!ai.HasBuilderTask, $"{rate}: the look has not seen it.");
            long again = -1;
            for (var step = 0; step < world.Frames(100) && again < 0; step++)
            {
                world.Step();
                if (ai.HasBuilderTask) again = Frame();
            }
            Check.That(again > 0, $"{rate}: the next look takes the repair.");
            return again - done;
        }

        var reference = Run(SimulationRate.Hz30);
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = Run(rate);
            Check.That(other == reference, $"{rate}: the repair is taken in the same frame ({other} against {reference}).");
        }
    }

    /// <summary>
    /// A structure hurt in the first tick of the frame the dozer looks in, before the dozer's update (here by a command, which runs before every update), is seen by the 30 Hz look and
    /// is taken for repair in that look - at 60 and 120 Hz as well, where the look stands at the frame's end. Before, a hurt anywhere in the frame was held back to the next look,
    /// a whole <c>BoredTime</c> later (Alpine Assault GLA against America, frame 15,967: the poison of a command centre hurts it in its own update, before the dozer's).
    /// </summary>
    public static void BoredDozerSeesAHurtBeforeItsUpdate()
    {
        long Run(SimulationRate rate)
        {
            var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/DozerBored.ini"] = DozerBoredIni });
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "BoredDozer", owner: 1, x: 160, y: 100).Accepted && feeder.Spawn(1, "BoredHut", owner: 1, x: 260, y: 100).Accepted, "Dozer and hut spawn.");
            world.Step();
            var dozer = world.Entities.Single(value => value.Template.Name == "BoredDozer");
            var hut = world.Entities.Single(value => value.Template.Name == "BoredHut");
            var ai = dozer.Modules.OfType<DozerAIUpdate>().Single();
            var perFrame = world.Time.TicksPerOriginalFrame;
            long Frame() => (world.Tick - 1 - dozer.SpawnTick) / perFrame;
            long done = -1;
            for (var step = 0; step < world.Frames(800) && done < 0; step++)
            {
                world.Step();
                if (ai.HasBuilderTask) continue;
                if (hut.Body!.Health == hut.Body.MaxHealth) done = Frame();
            }
            Check.That(done > 0, $"{rate}: the first repair ends.");
            // Stand before the first tick of the frame of the next look (151 frames on) and hurt the hut with a command, which runs before the dozer's update.
            while (Frame() < done + 150 || (world.Tick - 1 - dozer.SpawnTick) % perFrame != perFrame - 1) world.Step();
            Check.That(feeder.Damage(hut.Id.Value, 300).Accepted, $"{rate}: the hut is hurt in the first tick of the frame of the look.");
            long again = -1;
            for (var step = 0; step < world.Frames(300) && again < 0; step++)
            {
                world.Step();
                if (ai.HasBuilderTask) again = Frame();
            }
            Check.That(again > 0, $"{rate}: the repair is taken.");
            return again - done;
        }

        var reference = Run(SimulationRate.Hz30);
        Check.That(reference <= 160, $"At 30 Hz the look that comes with the hurt takes the repair ({reference} frames after the first repair ended).");
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = Run(rate);
            Check.That(other == reference, $"{rate}: the repair is taken in the same frame ({other} against {reference}).");
        }
    }

    /// <summary>
    /// The look of an idle dozer sees the world as its update meets it in the frame's first tick: a structure that a second dozer (a higher id, so its update comes after) heals to full
    /// health in that same tick is still hurt for the look, as at 30 Hz. Above 30 Hz the look stands at the frame's end and read the healed structure. The first dozer takes the repair
    /// in the same frame at 30, 60 and 120 Hz for every moment the second one's heal falls in; at 30 Hz one of them is the very frame of the look.
    /// </summary>
    public static void BoredDozerLooksBeforeAHealOfTheSameTick()
    {
        (long Task, long Full) Run(SimulationRate rate, long lookFrame, long orderBefore)
        {
            var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/DozerBored.ini"] = DozerBoredIni });
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "BoredDozer", owner: 1, x: 160, y: 100).Accepted && feeder.Spawn(1, "BoredHut", owner: 1, x: 260, y: 100).Accepted
                && feeder.Spawn(1, "BoredDozer", owner: 1, x: 262, y: 118).Accepted, "Two dozers and a hut spawn.");
            world.Step();
            var first = world.Entities.First(value => value.Template.Name == "BoredDozer");
            var second = world.Entities.Last(value => value.Template.Name == "BoredDozer");
            var hut = world.Entities.Single(value => value.Template.Name == "BoredHut");
            Check.That(first.Id.Value < second.Id.Value, "The dozer that looks is the first in the order of updates.");
            // The hut is hurt by one unit of health only: the second dozer's first slice makes it whole.
            world.Damage(hut.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(499), DamageTypes.Healing, DeathTypes.None, false));
            var ai = first.Modules.OfType<DozerAIUpdate>().Single();
            var perFrame = world.Time.TicksPerOriginalFrame;
            long Frame() => (world.Tick - 1 - first.SpawnTick) / perFrame;
            long task = -1, full = -1;
            for (var step = 0; step < world.Frames(400) && (task < 0 || full < 0); step++)
            {
                if (orderBefore >= 0 && Frame() == lookFrame - orderBefore - 1 && (world.Tick - 1 - first.SpawnTick) % perFrame == perFrame - 1)
                    Check.That(feeder.Repair(1, second.Id.Value, hut.Id.Value).Accepted, $"{rate}: the second dozer is sent to repair.");
                world.Step();
                if (task < 0 && ai.HasBuilderTask) task = Frame();
                if (full < 0 && hut.Body!.Health == hut.Body.MaxHealth) full = Frame();
            }
            return (task, full);
        }

        // The look comes with nobody's heal: the frame it takes the repair in.
        var alone = Run(SimulationRate.Hz30, 0, -1);
        Check.That(alone.Task > 100, $"The first dozer takes the repair at its look ({alone.Task}).");
        var met = false;
        foreach (var before in new long[] { 0, 1, 2, 3 })
        {
            var reference = Run(SimulationRate.Hz30, alone.Task, before);
            met |= reference.Full == alone.Task;
            foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
            {
                var other = Run(rate, alone.Task, before);
                Check.That(other == reference, $"{rate}: look and heal fall in the same frames ({other} against {reference}, the second dozer sent {before} frames before the look).");
            }
        }
        Check.That(met, "One of the orders makes the second dozer's heal fall in the frame of the look.");
    }
}

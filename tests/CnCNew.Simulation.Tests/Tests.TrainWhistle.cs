using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

/// <summary>
/// <c>RailroadBehavior::onCollide</c>, "Alert all the players of recent disaster": a train car that runs into something with physics raises <see cref="SimEventKind.TrainWhistle"/>
/// (the presentation plays the car's <c>WhistleSound</c> for it). Structures that die at once, objects without physics and things far off the track whistle nothing.
/// </summary>
static partial class Tests
{
    /// <summary>The frames of the original (counted from the first full frame) in which a whistle was raised, with the entity and the one it ran into.</summary>
    private static List<(int Frame, uint Car, uint Other)> TrainWhistles(SimulationRate rate, params (string Template, int Player, int X, int Y)[] spawns)
    {
        var (world, fixture, _) = RatedStructuresWorld(rate);
        foreach (var spawn in spawns) RequestSpawn(world, fixture, spawn.Template, spawn.Player, spawn.X, spawn.Y);
        var whistles = new List<(int, uint, uint)>();
        // The train is placed in the world's first tick, so its frames are the world's: the series is read where they end (as RateInvarianceTrain does).
        for (var tick = 1; tick < world.Time.TicksPerOriginalFrame; tick++) world.Step();
        for (var frame = 0; frame < 400; frame++)
            for (var tick = 0; tick < (frame == 0 ? 1 : world.Time.TicksPerOriginalFrame); tick++)
                foreach (var item in world.Step().Events)
                    if (item.Kind == SimEventKind.TrainWhistle) whistles.Add((frame, item.Entity.Value, item.Other.Value));
        return whistles;
    }

    private static Fixed ConductorSpeedAtFrame(SimulationRate rate)
    {
        var (world, fixture, _) = RatedStructuresWorld(rate);
        RequestSpawn(world, fixture, "SwLoco", 0, 50, 100);
        for (var tick = 0; tick < world.Frames(60); tick++) world.Step();
        return world.Entities.Where(entity => entity.Template.Name == "SwLoco").Select(entity => entity.Modules.OfType<RailroadBehavior>().Single().ConductorSpeedPerFrame).Single();
    }

    public static void TrainWhistlesWhenItRunsIntoSomething()
    {
        var reference = TrainWhistles(SimulationRate.Hz30, ("SwHeavy", 2, 95, 104), ("SwLoco", 0, 50, 100));
        Check.That(reference.Count > 0, "A locomotive that grinds along a vehicle raises the whistle.");
        Check.That(reference.All(item => item.Car != item.Other), "The whistle names the car and what it ran into.");
        foreach (var (rate, name) in new[] { (SimulationRate.Hz60, "60 Hz"), (SimulationRate.Hz120, "120 Hz") })
        {
            var other = TrainWhistles(rate, ("SwHeavy", 2, 95, 104), ("SwLoco", 0, 50, 100));
            Check.That(other.Select(item => item.Frame).SequenceEqual(reference.Select(item => item.Frame)),
                "The whistle comes in the same frames of the original at " + name + ": " + string.Join(",", other.Select(item => item.Frame).Distinct()) + " instead of " + string.Join(",", reference.Select(item => item.Frame).Distinct()) + ".");
        }

        // The clatter's volume is the conductor's speed per frame of the original over ten: the same number at every rate.
        var speeds = new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 }.Select(ConductorSpeedAtFrame).ToArray();
        Check.That(speeds[0] > Fixed.One && (speeds[1] - speeds[0]).Abs() <= Fixed.FromRaw(4) && (speeds[2] - speeds[0]).Abs() <= Fixed.FromRaw(4),
            "The conductor's speed per frame of the original is the same at 30, 60 and 120 Hz: " + string.Join(", ", speeds) + ".");

        Check.That(TrainWhistles(SimulationRate.Hz30, ("SwHeavy", 2, 140, 160), ("SwLoco", 0, 50, 100)).Count == 0, "Something 60 units off the track is not run into, so nothing whistles.");
        Check.That(TrainWhistles(SimulationRate.Hz30, ("SwWalker", 2, 100, 100), ("SwLoco", 0, 50, 100)).Count == 0, "An object without physics is no collision (the reference returns before the whistle).");
        Check.That(TrainWhistles(SimulationRate.Hz30, ("SwPlant", 1, 170, 100), ("SwLoco", 0, 50, 100)).Count == 0, "A base structure is destroyed without the whistle (the reference returns first).");
    }
}

static partial class Tests
{
    /// <summary>A locomotive the vehicle of the test below may enter: its own player's transport for vehicles.</summary>
    private const string TrainBoardingIni = """
        Object SwBoardLoco
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = RailroadBehavior Rail01
            PathPrefixName = Railroad
            RunningGarrisonSpeedMax = 1
            KillSpeedMin = 1
            Friction = 0.995
            IsLocomotive = Yes
            SpeedMax = 3
            Acceleration = 1.1
            WaitAtStationTime = 1000
            Braking = 0.9
            CarriageTemplateName = SwCar
            CarriageTemplateName = SwCar
          End
          Behavior = TransportContain Transport01
            Slots = 4
            AllowInsideKindOf = VEHICLE
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SwBoarder
          KindOf = SELECTABLE VEHICLE
          TransportSlotCount = 1
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 40.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """;

    /// <summary>
    /// <c>RailroadBehavior::onCollide</c>: a unit whose <c>getEnterTarget()</c> is the car of a train that waits at its station (slower than <c>RunningGarrisonSpeedMax</c>) is let through
    /// before the whistle and before the shove ("let those trying to board pass through unhindered"). Two copies of the same staging differ only in that one vehicle means to enter the
    /// locomotive and the other drives to the same place: the first stands where its own movement leaves it, the second is shoved off. The locomotive is a transport of the vehicle's
    /// player, so the vehicle may enter it: an order to enter a car without a contain module does nothing (<c>privateEnter</c>, issue #375).
    /// </summary>

    public static void TrainLetsABoarderThrough()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            (Fixed X, Fixed Y, int Whistles) Stage(bool boarding)
            {
                var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(StructuresFiles()) { ["Data/INI/Object/TrainBoardingFixture.ini"] = TrainBoardingIni });
                var terrain = StructuresTerrain(false);
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, StructuresPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
                RequestSpawn(world, fixture, "SwBoardLoco", 2, 50, 100);
                world.Step();
                Entity Loco() => world.Entities.Single(entity => entity.Template.Name == "SwBoardLoco");
                for (var tick = 0; tick < world.Frames(900) && Loco().Modules.OfType<RailroadBehavior>().Single().Conductor != RailroadBehavior.ConductorStates.WaitAtStation; tick++) world.Step();
                var loco = Loco();
                Check.That(loco.Modules.OfType<RailroadBehavior>().Single().Conductor == RailroadBehavior.ConductorStates.WaitAtStation, "Setup: the locomotive waits at its station.");
                Check.That(fixture.Templates.TryGet("SwBoarder", out var heavyInfo), "Setup: the vehicle exists.");
                world.RequestSpawn(heavyInfo.Index, new PlayerId(2), loco.X, loco.Y + Fixed.FromInt(3), Fixed.Zero, Fixed.Zero);
                world.Step();
                var heavy = world.Entities.Single(entity => entity.Template.Name == "SwBoarder");
                var ai = heavy.Modules.OfType<AIUpdateInterface>().Single();
                if (boarding) ai.OrderEnter(world, loco, true);
                else ai.OrderMove(world, loco.X, loco.Y, true);
                // Held, the vehicle's own update does not run: it neither completes the entry nor walks, so only the train acts on it.
                world.SetScriptDisable(heavy, Entity.ScriptHeld, true);
                Check.That(ai.EnterTarget == (boarding ? loco.Id : EntityId.Invalid), "Setup: only the boarder has the locomotive as its enter target.");
                var whistles = 0;
                for (var tick = 0; tick < world.Frames(3); tick++)
                {
                    foreach (var item in world.Step().Events)
                        if (item.Kind == SimEventKind.TrainWhistle && item.Entity == loco.Id) whistles++; }
                Check.That(loco.Modules.OfType<RailroadBehavior>().Single().Conductor == RailroadBehavior.ConductorStates.WaitAtStation, "Setup: it still waits.");
                return (heavy.X, heavy.Y, whistles);
            }

            var boarder = Stage(true);
            var bystander = Stage(false);
            Check.That(bystander.Whistles > 0 && boarder.Whistles == 0, $"At {rate.TicksPerSecond} Hz the bystander is whistled at and the boarder is not ({bystander.Whistles}, {boarder.Whistles}).");
            Check.That(boarder.X != bystander.X || boarder.Y != bystander.Y, $"At {rate.TicksPerSecond} Hz the shove leaves the boarder alone: both stand at ({boarder.X}, {boarder.Y}).");
        }
    }
}

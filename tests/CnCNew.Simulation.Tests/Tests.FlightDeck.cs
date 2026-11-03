using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static readonly IReadOnlyDictionary<string, string> DeckFiles = new Dictionary<string, string>(AirFixtureFiles)
    {
        ["Data/INI/Object/DeckFixture.ini"] = """
        Object DeckTestCarrier
          Side = Alpha
          KindOf = STRUCTURE IMMOBILE FS_AIRFIELD SELECTABLE AIRCRAFT_CARRIER CAN_ATTACK
          VisionRange = 2000.0
          ShroudClearingRange = 2000.0
          Geometry = BOX
          GeometryMajorRadius = 200.0
          GeometryMinorRadius = 50.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = FlightDeckBehavior Deck01
            NumRunways = 2
            NumSpacesPerRunway = 2
            HealAmountPerSecond = 60.0
            ApproachHeight = 50.0
            LandingDeckHeightOffset = 45.0
            ParkingCleanupPeriod = 533
            HumanFollowPeriod = 333
            PayloadTemplate = AirTestJet
            ReplacementDelay = 1000
            DockAnimationDelay = 500
            LaunchWaveDelay = 3000
            LaunchRampDelay = 667
            LowerRampDelay = 600
            CatapultFireDelay = 750
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 5
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object DeckTestVictim
          Side = Alpha
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, Entity Carrier, FlightDeckBehavior Deck) DeckWorld(SimulationRate rate)
    {
        var fixture = new SyntheticFixture(rate, files: DeckFiles);
        var map = CnCNew.Simulation.Terrain.TerrainMap.Build(SyntheticMaps.Build(300, 300, 1, (_, _) => 0), "air");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 9, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, map.Fingerprint), fixture.Templates, map);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "DeckTestCarrier", x: 1000, y: 1000);
        feeder.Spawn(2, "DeckTestVictim", x: 2200, y: 1000);
        for (var tick = 0; tick < 3 * rate.TicksPerOriginalFrame; tick++) world.Step();
        var carrier = world.Entities.Single(value => value.Template.Name == "DeckTestCarrier");
        return (world, feeder, carrier, carrier.Modules.OfType<FlightDeckBehavior>().Single());
    }

    /// <summary><c>FlightDeckBehavior::buildInfo</c>: a jet on every space, owned as the carrier is, on the deck and on the ground of nobody's airfield.</summary>
    public static void ACarrierComesWithItsJets()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, _, carrier, deck) = DeckWorld(rate);
            var jets = world.Entities.Where(value => value.Template.Name == "AirTestJet").ToArray();
            Check.That(jets.Length == 4 && jets.All(jet => jet.Owner == carrier.Owner), $"{rate}: four spaces, four jets ({jets.Length}).");
            for (var space = 0; space < deck.SpaceCount; space++) Check.That(deck.OccupantOf(space).IsValid, $"{rate}: space {space} is taken.");
            Check.That(jets.All(jet => !JetOf(jet).AllowAirLoco && jet.Z == carrier.Z + Fixed.FromInt(45)), $"{rate}: they stand on the deck ({jets[0].Z}).");
            Check.That(jets.All(jet => jet.Status != 0), $"{rate}: they carry the deck's height.");
        }
    }

    /// <summary>
    /// <c>hasTakeoffOrders</c>, <c>LaunchWave</c>: an attack order launches the front row after the ramp, not the jets behind it, which move up; the jet that is lost is replaced; and the deck does it in the
    /// same frames at every rate.
    /// </summary>
    public static void ACarrierLaunchesWaves()
    {
        var launches = new List<long>();
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, _, carrier, deck) = DeckWorld(rate);
            var victim = world.Entities.Single(value => value.Template.Name == "DeckTestVictim");
            var jets = world.Entities.Where(value => value.Template.Name == "AirTestJet").OrderBy(value => value.Id.Value).ToArray();
            var front = deck.OccupantOf(0);
            var behind = deck.OccupantOf(2);
            Check.That(world.Submit(new PursueCommand(carrier.Owner, 1000, world.Tick, carrier.Id, victim.Id, victim.X, victim.Y)).Accepted, $"{rate}: the carrier takes an attack order.");
            world.Step();
            Check.That(deck.Order == DeckOrder.AttackObject, $"{rate}: it keeps it.");
            Check.That(jets.All(jet => JetOf(jet).Phase == JetAIUpdate.JetPhase.None), $"{rate}: nobody moves before the ramp is up.");
            var started = world.Tick;
            StepUntil(world, () => JetOf(world.FindEntity(front)!).Phase != JetAIUpdate.JetPhase.None, 300, $"{rate}: the front jet never left.");
            var after = (world.Tick - started) / rate.TicksPerOriginalFrame;
            launches.Add(after);
            Check.That(after is >= 15 and <= 25, $"{rate}: the launch waits for the ramp ({after} frames).");
            Check.That(JetOf(world.FindEntity(behind)!).Phase == JetAIUpdate.JetPhase.None, $"{rate}: the row behind stays.");
            StepUntil(world, () => world.IsAirborneTarget(world.FindEntity(front)!), 900, $"{rate}: the front jet never flew.");
            Check.That(deck.RampIsUp(0) || !deck.RampIsUp(0), $"{rate}: the ramp is readable.");

            StepUntil(world, () => deck.OccupantOf(0) == behind, 900, $"{rate}: the jet behind never got the front space.");
            StepUntil(world, () => JetOf(world.FindEntity(behind)!).Phase == JetAIUpdate.JetPhase.None, 900, $"{rate}: the jet behind never reached its new space.");

            world.Kill(front);
            StepUntil(world, () => world.Entities.Count(value => value.Template.Name == "AirTestJet") == 4, 1800, $"{rate}: the lost jet was never replaced.");
            var replacement = world.Entities.Where(value => value.Template.Name == "AirTestJet").MaxBy(value => value.Id.Value)!;
            StepUntil(world, () => deck.SpaceOf(replacement.Id) >= 0, 60, $"{rate}: the replacement has no space.");
            Check.That(carrier.Body!.Health == carrier.Body.MaxHealth, $"{rate}: nothing else happened to the carrier.");
        }
        Check.That(launches.Distinct().Count() == 1, $"The launch falls into the same frame at every rate ({string.Join(", ", launches)}).");
    }

    /// <summary>The state of a deck survives a save: its order, its reservations and its timers.</summary>
    public static void ACarrierIsSaved()
    {
        var rate = SimulationRate.FromTicksPerSecond(60);
        var (world, _, carrier, _) = DeckWorld(rate);
        var victim = world.Entities.Single(value => value.Template.Name == "DeckTestVictim");
        Check.That(world.Submit(new PursueCommand(carrier.Owner, 1000, world.Tick, carrier.Id, victim.Id, victim.X, victim.Y)).Accepted, "The order is accepted.");
        for (var step = 0; step < world.Frames(60); step++) world.Step();
        var fixture = new SyntheticFixture(rate, files: DeckFiles);
        var terrain = CnCNew.Simulation.Terrain.TerrainMap.Build(SyntheticMaps.Build(300, 300, 1, (_, _) => 0), "air");
        var restored = CnCNew.Simulation.Snapshots.WorldSnapshot.Restore(CnCNew.Simulation.Snapshots.WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, terrain);
        Check.That(restored.StateHash == world.StateHash, "The deck round-trips.");
        for (var step = 0; step < world.Frames(600); step++)
        {
            world.Step();
            restored.Step();
            Check.That(restored.StateHash == world.StateHash, $"The restored deck goes on alike (step {step}).");
        }
    }

    /// <summary><c>AICMD_IDLE</c>, <c>JetTakeoffOrLandingState</c>, <c>JetOrHeliTaxiState</c>: the jets that are out come back over the deck's own landing strip and stand on a space of their runway.</summary>
    public static void ACarrierCallsItsJetsHome()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, _, carrier, deck) = DeckWorld(rate);
            var victim = world.Entities.Single(value => value.Template.Name == "DeckTestVictim");
            var front = deck.OccupantOf(0);
            Check.That(world.Submit(new PursueCommand(carrier.Owner, 1000, world.Tick, carrier.Id, victim.Id, victim.X, victim.Y)).Accepted, $"{rate}: the order is accepted.");
            StepUntil(world, () => world.IsAirborneTarget(world.FindEntity(front)!), 900, $"{rate}: the front jet never flew.");
            for (var step = 0; step < world.Frames(120); step++) world.Step();
            deck.TakeOrder(world, DeckOrder.Idle, EntityId.Invalid, Fixed.Zero, Fixed.Zero);
            var jet = world.FindEntity(front)!;
            StepUntil(world, () => !JetOf(jet).AllowAirLoco, 3600, $"{rate}: the jet never landed.");
            StepUntil(world, () => JetOf(jet).Phase == JetAIUpdate.JetPhase.None, 1800, $"{rate}: the jet never parked ({JetOf(jet).Phase}).");
            Check.That(jet.Z == carrier.Z + Fixed.FromInt(45), $"{rate}: it stands on the deck ({jet.Z}).");
            Check.That(deck.SpaceOf(jet.Id) >= 0, $"{rate}: it has a space.");
            var info = deck.InfoOfSpace(world, deck.SpaceOf(jet.Id));
            Check.That(jet.X == info.RunwayPrep.X && jet.Y == info.RunwayPrep.Y, $"{rate}: on its space ({jet.X}, {jet.Y} against {info.RunwayPrep.X}, {info.RunwayPrep.Y}).");
        }
    }
}

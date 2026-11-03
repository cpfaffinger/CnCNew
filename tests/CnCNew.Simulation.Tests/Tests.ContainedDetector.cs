using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

static partial class Tests
{
    private const string ContainedDetectorIni = """
        Object RidingSpotter
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = StealthDetectorUpdate Detector01
            DetectionRate = 500
            DetectionRange = 50.0
          End
        End
        Object RidingSpotterThatSees
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = StealthDetectorUpdate Detector01
            DetectionRate = 500
            DetectionRange = 50.0
            CanDetectWhileContained = Yes
          End
        End
        """;

    /// <summary>
    /// <c>StealthDetectorUpdate::update</c>: "Are we eligible to detect stealth while in a container?" A detector in a transport detects only with <c>CanDetectWhileContained</c>.
    /// </summary>
    public static void ADetectorInATransportDetectsOnlyWhenAllowed()
    {
        bool Detects(string spotter)
        {
            var files = new Dictionary<string, string>(Phase5Files()) { ["Data/INI/Object/ContainedDetector.ini"] = ContainedDetectorIni };
            var fixture = new SyntheticFixture(TestRate.Default, files: files);
            var world = NewWorld(fixture);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Chinook", x: 0, y: 0);
            feeder.Spawn(1, spotter, x: 0, y: 0);
            feeder.Spawn(2, "Cloaked", x: 20, y: 0);
            world.Step();
            var transport = world.Entities.Single(value => value.Template.Name == "Chinook");
            var rider = world.Entities.Single(value => value.Template.Name == spotter);
            var enemy = world.Entities.Single(value => value.Template.Name == "Cloaked");
            Check.That(world.TryEnterContainer(rider, transport), $"{spotter} boards the transport.");
            for (var i = 0; i < world.Frames(40); i++) world.Step();
            Check.That(enemy.HasStatus(ObjectStatusBits.Index("STEALTHED")), "The enemy is stealthed.");
            return enemy.HasStatus(ObjectStatusBits.Index("DETECTED"));
        }

        Check.That(!Detects("RidingSpotter"), "A detector without CanDetectWhileContained detects nothing from inside a transport.");
        Check.That(Detects("RidingSpotterThatSees"), "With CanDetectWhileContained it detects from inside.");
    }
}

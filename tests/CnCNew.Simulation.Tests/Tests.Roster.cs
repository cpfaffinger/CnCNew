using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// GarrisonContain::onObjectCreated: a garrison with an InitialRoster comes with its occupants (the caboose full of terrorists).
static partial class Tests
{
    private const string RosterIni = """
        Object RosterTerrorist
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object RosterCaboose
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 30.0
          GeometryMinorRadius = 30.0
          GeometryHeight = 30.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 10
            InitialRoster = RosterTerrorist 4
          End
        End
        Object RosterCarriage
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 8
            InitialRoster = RosterTerrorist 0
          End
        End
        """;

    public static void AGarrisonComesWithItsRoster()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/Roster.ini"] = RosterIni });
        var terrain = TerrainMap.Build(SyntheticMaps.Build(40, 40, 1), "roster");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "RosterCaboose", owner: 1, x: 100, y: 100);
        feeder.Spawn(1, "RosterCarriage", owner: 1, x: 300, y: 100);
        world.Step();
        var caboose = world.Entities.Single(value => value.Template.Name == "RosterCaboose");
        var garrison = caboose.Modules.OfType<GarrisonContain>().Single();

        // Halfway in: the state of what has been asked for survives a snapshot.
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(WorldSnapshot.Write(world)), "The roster on its way survives a snapshot.");

        for (var tick = 0; tick < world.Frames(4); tick++) world.Step();
        Check.That(garrison.ContainCount == 4, $"It comes with the four of its roster ({garrison.ContainCount}).");
        Check.That(world.Entities.Count(value => value.Template.Name == "RosterTerrorist" && !value.ContainedBy.IsValid) == 0, "Nobody stands outside.");
        Check.That(world.Entities.Where(value => value.Template.Name == "RosterTerrorist").All(value => value.Owner == caboose.Owner), "They are its owner's.");
        var carriage = world.Entities.Single(value => value.Template.Name == "RosterCarriage").Modules.OfType<GarrisonContain>().Single();
        Check.That(carriage.ContainCount == 0, "A roster of none is nobody.");
    }
}

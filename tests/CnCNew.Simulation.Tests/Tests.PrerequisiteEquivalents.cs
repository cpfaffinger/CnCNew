using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// #468: <c>ProductionPrerequisite::isSatisfied</c> counts the player's objects with <c>Player::countObjectsByThingTemplate</c>, which compares templates by
/// <c>ThingTemplate::isEquivalentTo</c> - a reskin of the named object, or an object the named one lists among its <c>BuildVariations</c>, meets the line. The
/// world asked that; the command bar and the built-in AI compared names, so they could disagree with it. All three now ask
/// <see cref="SimulationWorld.PrerequisitesSatisfied(PlayerId, CnCNew.Simulation.Templates.ObjectTemplate)"/>. The shipped data has no prerequisite whose
/// alternative has an equivalent, so the case is synthetic.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> PrerequisiteEquivalentFiles()
    {
        var files = new Dictionary<string, string>(ConstructionFiles())
        {
            ["Data/INI/Object/PrerequisiteEquivalents.ini"] = """
            ObjectReskin PlantReskin Plant
            End
            Object Generator
              KindOf = SELECTABLE STRUCTURE
              BuildVariations = Plant
              Body = StructureBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
            End
            Object Pylon
              KindOf = SELECTABLE STRUCTURE
              BuildCost = 50
              BuildTime = 1
              Geometry = BOX
              GeometryMajorRadius = 10.0
              GeometryMinorRadius = 10.0
              GeometryHeight = 10.0
              GeometryIsSmall = No
              Body = StructureBody Body01
                MaxHealth = 200.0
                InitialHealth = 200.0
              End
              Behavior = DestroyDie Destroy01
              End
              Prerequisites
                Object = Generator
              End
            End
            """,
        };
        return files;
    }

    public static void PrerequisiteIsMetByAnEquivalentObject()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: PrerequisiteEquivalentFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.GiveMoney(1, 1000);
        world.Step();
        Check.That(world.Templates.TryGet("Tower", out var tower), "The fixture has the tower.");
        Check.That(world.Templates.TryGet("Pylon", out var pylon), "The fixture has the pylon.");
        var one = new PlayerId(1);
        Check.That(!world.PrerequisitesSatisfied(one, tower) && world.MissingPrerequisites(one, tower).Length == 1, "Without a power plant the tower's line is missing.");

        Check.That(feeder.Spawn(1, "PlantReskin", owner: 1, x: 200, y: 0).Accepted, "The reskin spawns.");
        world.Step();
        Check.That(world.PrerequisitesSatisfied(one, tower) && world.MissingPrerequisites(one, tower).IsEmpty && world.PlayerHasPrerequisitesFor(one, "Tower"),
            "A reskin of the power plant meets the tower's line (isEquivalentTo: this reskinned from that).");
        Check.That(!world.PrerequisitesSatisfied(new PlayerId(2), tower), "It is its owner's only.");

        // An object that lists the plant among its build variations is met by the plant.
        Check.That(!world.PrerequisitesSatisfied(one, pylon), "The reskin is no build variation of the generator.");
        Check.That(feeder.Spawn(1, "Plant", owner: 1, x: 400, y: 0).Accepted, "A plant spawns.");
        world.Step();
        Check.That(world.PrerequisitesSatisfied(one, pylon), "A plant meets a line that names an object listing it among its BuildVariations.");

        Check.That(feeder.Spawn(1, "Dozer", owner: 1, x: 0, y: 0).Accepted, "A dozer spawns.");
        world.Step();
        var dozer = world.Entities.Single(value => value.Template.Name == "Dozer");
        var order = feeder.Construct(1, dozer.Id.Value, "Tower", 0, 200);
        Check.That(order.Accepted, $"The dozer may build the tower ({order.Reason}).");
    }
}

using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// #469: <c>ThingTemplate::parsePrerequisites</c> appends the lines of a block to <c>m_prereqInfo</c>, so an object with two <c>Prerequisites</c> blocks needs the lines of both;
/// a map's <c>map.ini</c> (<c>INI_LOAD_CREATE_OVERRIDES</c>) clears them first. The port read the first block only.
/// </summary>
static partial class Tests
{
    public static void PrerequisiteBlocksAppend()
    {
        IReadOnlyDictionary<string, string> Files(bool map)
        {
            var files = new Dictionary<string, string>(ConstructionFiles())
            {
                ["Data/INI/Object/PrerequisiteBlocks.ini"] = """
                Object Yard
                  KindOf = SELECTABLE STRUCTURE
                  Body = StructureBody Body01
                    MaxHealth = 100.0
                    InitialHealth = 100.0
                  End
                End
                Object Depot
                  KindOf = SELECTABLE STRUCTURE
                  Body = StructureBody Body01
                    MaxHealth = 100.0
                    InitialHealth = 100.0
                  End
                  Prerequisites
                    Object = Plant
                  End
                  Prerequisites
                    Object = Yard
                  End
                End
                """,
            };
            if (map) files["Maps/Blocks/map.ini"] = "Object Depot\n  Prerequisites\n    Object = Plant\n  End\nEnd\n";
            return files;
        }

        foreach (var map in new[] { false, true })
        {
            var fixture = new SyntheticFixture(TestRate.Default, files: Files(map));
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            Check.That(world.Templates.TryGet("Depot", out var depot), "The depot exists.");
            var lines = string.Join(" / ", depot.Prerequisites.Select(line => string.Join(" ", line.Alternatives)));
            Check.That(lines == (map ? "Plant" : "Plant / Yard"), $"{(map ? "With" : "Without")} the map's override the depot needs '{lines}'.");
            Check.That(feeder.Spawn(1, "Plant", owner: 1, x: 200, y: 0).Accepted, "A plant spawns.");
            world.Step();
            Check.That(world.PrerequisitesSatisfied(new PlayerId(1), depot) == map, $"{(map ? "With" : "Without")} the override a plant alone {(map ? "is" : "is not")} enough.");
            Check.That(feeder.Spawn(1, "Yard", owner: 1, x: 400, y: 0).Accepted, "A yard spawns.");
            world.Step();
            Check.That(world.PrerequisitesSatisfied(new PlayerId(1), depot), "A plant and a yard meet both blocks.");
        }
    }
}

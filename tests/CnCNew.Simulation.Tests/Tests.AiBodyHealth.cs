using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

static partial class Tests
{
    /// <summary>
    /// Real data (#437): the built-in AI's composition rule reads the health of every body type. It read only <c>BodyData</c>, so a Battle Bus (<c>UndeadBody</c>) and a Stinger Site
    /// (<c>HiveStructureBody</c>) had health 0 to it - never hurt, never worth hurting. The Battle Bus is judged by both its lives (400 + 650), the Stinger Site by its structure body,
    /// and for every body module type of the data the AI's health of a template equals the maximum health of an object the world makes of it (both lives of an <c>UndeadBody</c>).
    /// At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealSkirmishAiReadsEveryBodysHealth(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var terrain = TerrainMap.Build(map, MapPath);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = 1, WorldSeed = 1 ^ 0xC0FFEE06 }, catalog, fingerprint, terrain, map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica"))]);
            var ai = session.Ais[0] ?? throw new InvalidOperationException("The GLA slot has no built-in AI.");

            var bus = catalog.Templates.Single(template => template.Name == "GLAVehicleBattleBus");
            var undead = bus.Modules.Select(module => module.Data).OfType<UndeadBodyData>().Single();
            Check.That(ai.DescribeHealth(bus) == undead.Body.MaxHealth + undead.SecondLifeMaxHealth && ai.DescribeHealth(bus) > Fixed.Zero,
                $"{rate}: the AI judges the Battle Bus by both lives ({ai.DescribeHealth(bus)}, expected {undead.Body.MaxHealth} + {undead.SecondLifeMaxHealth}).");
            var stinger = catalog.Templates.Single(template => template.Name == "GLAStingerSite");
            var hive = stinger.Modules.Select(module => module.Data).OfType<HiveStructureBodyData>().Single();
            Check.That(ai.DescribeHealth(stinger) == hive.Body.MaxHealth && ai.DescribeHealth(stinger) > Fixed.Zero,
                $"{rate}: the AI judges the Stinger Site by its body ({ai.DescribeHealth(stinger)}, expected {hive.Body.MaxHealth}).");

            // One template per body module type the data use (the first buildable one without build variations, else the first), made by the world beside the map's centre.
            var world = session.World;
            var bodyTypes = new SortedDictionary<string, ObjectTemplate>(StringComparer.Ordinal);
            foreach (var template in catalog.Templates.Where(template => template.BuildVariations.IsDefaultOrEmpty).OrderBy(template => template.BuildCost > Fixed.Zero ? 0 : 1).ThenBy(template => template.Index))
                foreach (var module in template.Modules)
                    if (module.Name.EndsWith("Body", StringComparison.Ordinal)) bodyTypes.TryAdd(module.Name, template);
            Check.That(bodyTypes.ContainsKey("UndeadBody") && bodyTypes.ContainsKey("HiveStructureBody") && bodyTypes.ContainsKey("ActiveBody") && bodyTypes.ContainsKey("StructureBody"),
                $"{rate}: the data use the body types {string.Join(", ", bodyTypes.Keys)}.");
            var centre = (X: terrain.ExtentX / Fixed.FromInt(2), Y: terrain.ExtentY / Fixed.FromInt(2));
            var made = new List<(string Body, ObjectTemplate Template, CnCNew.Simulation.Ids.EntityId Id)>();
            var offset = 0;
            foreach (var (body, template) in bodyTypes)
                made.Add((body, template, world.RequestSpawn(template.Index, session.Players[0].Id, centre.X + Fixed.FromInt(offset += 120), centre.Y, Fixed.Zero, Fixed.Zero)));
            session.Step();
            foreach (var (body, template, id) in made)
            {
                var entity = world.FindEntity(id) ?? throw new InvalidOperationException($"{rate}: {template.Name} ({body}) was not made.");
                var expected = entity.Body?.MaxHealth ?? Fixed.Zero;
                if (template.Modules.Select(module => module.Data).OfType<UndeadBodyData>().FirstOrDefault() is { } second) expected += second.SecondLifeMaxHealth;
                Check.That(ai.DescribeHealth(template) == expected, $"{rate}: the AI's health of {template.Name} ({body}) is {ai.DescribeHealth(template)}, the world made it with {expected}.");
            }
        }
    }
}

using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// The owner's playtest of 14 November 2026 (issues 340 and 341): the built-in computer players took no neutral tech building and put nobody into a building. The skirmish library of the
    /// original does both (its "Tech Capture Team" researches the capture upgrade and takes the nearest tech building, its garrison teams enter buildings near the base). China against the
    /// United States on Flash Fire, which has oil derricks near both bases: within ten game minutes at least one neutral capturable building belongs to a player, and (on that map and on Alpine
    /// Assault) infantry stands inside a garrisonable building near an own structure. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealComputerPlayersCaptureAndGarrison(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var failures = new List<string>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            foreach (var mapName in new[] { "Flash Fire", "Alpine Assault" })
            {
                var mapPath = $"Maps/{mapName}/{mapName}.map";
                MapData map;
                using (var stream = content.OpenRead(mapPath)) map = SageMapDecoder.Decode(stream);
                const uint Seed = 1;
                var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = Seed, WorldSeed = Seed ^ 0xC0FFEE06 }, catalog, fingerprint, TerrainMap.Build(map, mapPath), map.Objects,
                    [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica"))]);
                var world = session.World;
                var players = session.Players.Select(player => player.Id).ToHashSet();
                var neutral = new HashSet<EntityId>();
                var captured = false;
                var garrisoned = false;
                while (world.Tick < rate.TicksPerSecond * 60 * 16 && !session.IsOver && !((captured || mapName != "Flash Fire") && garrisoned))
                {
                    session.Step();
                    if (world.Tick % rate.TicksPerSecond != 0) continue;
                    if (world.Tick == rate.TicksPerSecond * 5) foreach (var entity in world.Entities.Where(entity => entity.Owner.IsSystem && entity.Template.IsKindOf("CAPTURABLE") && entity.Template.IsKindOf("STRUCTURE"))) neutral.Add(entity.Id);
                    if (!captured) captured = neutral.Any(id => world.FindEntity(id) is { } entity && players.Contains(entity.Owner));
                    if (!garrisoned)
                        garrisoned = world.Entities.Any(entity => entity.Container is GarrisonContain { ContainCount: > 0 } contain && contain.Contained.Any(rider => world.FindEntity(rider) is { } inside && players.Contains(inside.Owner))
                                                                  && entity.Template.IsKindOf("STRUCTURE") && !entity.Template.IsKindOf("FS_BASE_DEFENSE"));
                }
                if (neutral.Count == 0) failures.Add($"{rate} {mapName}: the map has no neutral capturable building after five seconds");
                if (mapName == "Flash Fire" && !captured) failures.Add($"{rate} {mapName}: no neutral building captured");
                if (!garrisoned) failures.Add($"{rate} {mapName}: nobody garrisoned");
            }
        }
        Check.That(failures.Count == 0, string.Join("; ", failures));
    }
}

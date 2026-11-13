using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.Views;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data (#319, the owner's playtest of 13 November 2026): a computer player with two supply centers sent every gatherer to one source and flew each load the whole way to its first
    /// center. America against China on Alpine Assault (seed 3), 15 game minutes at 30, 60 and 120 Hz: America builds a second center beside the other source at about minute 9, and the
    /// first source runs dry at about minute 11. Before the fix the first center's Chinooks (their home base was the center they delivered to last) gathered at the second source and flew
    /// 941 units back to the first center with every load, and the new Chinooks were made at the first center and stayed with it. Now every gatherer belongs to a center that has supplies
    /// in reach (<c>AIPlayer::onUnitProduced</c>, <c>queueSupplyTruck</c>): once two centers stand, (1) both centers receive deliveries while both have supplies in reach, and (2) at most
    /// two loads per gatherer (the one on board when it is given its new center, and the first one after) go to a center more than 300 units farther from the warehouse it was gathered at
    /// than the nearest own center.
    /// </summary>
    public static void RealComputerGatherersWorkTheSourceOfTheirOwnCenter(string directory)
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
            const uint Seed = 3;
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = Seed, WorldSeed = Seed ^ 0xC0FFEE06 }, catalog, fingerprint, TerrainMap.Build(map, MapPath), map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
            var world = session.World;
            var id = session.Players[0].Id;
            static long Distance(Entity a, Entity b) => (long)Math.Sqrt((double)((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)).Floor());
            bool IsGatherer(Entity entity) => entity.Owner == id && !entity.EffectivelyDead && entity.Template.IsKindOf("HARVESTER") && !entity.Template.IsKindOf("STRUCTURE");
            Entity[] Centers() => world.Entities.Where(entity => entity.Owner == id && !entity.EffectivelyDead && entity.Template.IsKindOf("CASH_GENERATOR") && entity.Template.IsKindOf("STRUCTURE")
                && !entity.HasStatus(ObjectStatusBits.UnderConstruction)).ToArray();
            bool Fed(Entity center) => world.Entities.Any(entity => entity.Template.IsKindOf("SUPPLY_SOURCE") && !entity.EffectivelyDead && entity.WarehouseSupplyBoxes() > 0
                && Distance(entity, center) <= 200 + (center.Template.BoundingCircleRadius + entity.Template.BoundingCircleRadius).Floor());
            var boxes = new Dictionary<uint, int>();
            var gatheredAt = new Dictionary<uint, Entity>();
            var far = new Dictionary<uint, int>();
            var farLoads = new List<string>();
            var bothFedDeliveries = new Dictionary<uint, int>();
            var deliveries = 0;
            var bothFed = false;
            var end = rate.TicksPerSecond * 60 * 15;
            while (world.Tick < end && !session.IsOver)
            {
                var result = session.Step();
                foreach (var source in world.Entities)
                {
                    if (!source.Template.IsKindOf("SUPPLY_SOURCE")) continue;
                    var now = source.WarehouseSupplyBoxes();
                    // A warehouse that gives a box gives it to a gatherer docked at it; the others beside it wait for it. Each of them works this warehouse.
                    if (boxes.TryGetValue(source.Id.Value, out var before) && now < before)
                        foreach (var gatherer in world.Entities.Where(IsGatherer))
                            if (Distance(gatherer, source) < 200) gatheredAt[gatherer.Id.Value] = source;
                    boxes[source.Id.Value] = now;
                }
                var centers = Centers();
                if (centers.Length < 2 || result.Events is not { } events) continue;
                var fedNow = centers.Count(Fed) >= 2;
                bothFed |= fedNow;
                foreach (var ev in events)
                {
                    if (ev.Kind != SimEventKind.SupplyDeposited || ev.Entity == ev.Other || world.FindEntity(ev.Entity) is not { } carrier || carrier.Owner != id) continue;
                    if (world.FindEntity(ev.Other) is not { } center || !gatheredAt.TryGetValue(carrier.Id.Value, out var source)) continue;
                    deliveries++;
                    if (fedNow) bothFedDeliveries[center.Id.Value] = bothFedDeliveries.GetValueOrDefault(center.Id.Value) + 1;
                    var nearest = centers.Min(own => Distance(own, source));
                    if (Distance(center, source) <= nearest + 300) continue;
                    far[carrier.Id.Value] = far.GetValueOrDefault(carrier.Id.Value) + 1;
                    farLoads.Add($"tick {world.Tick}: {carrier.Id.Value} from {source.Id.Value} to {center.Id.Value} ({Distance(center, source)} units, nearest {nearest})");
                }
            }
            Check.That(bothFed, $"{rate}: America stood with two supply centers that had supplies in reach (centers {Centers().Length}).");
            Check.That(bothFedDeliveries.Count >= 2, $"{rate}: while both centers had supplies in reach both received loads ({string.Join(", ", bothFedDeliveries.Select(pair => $"{pair.Key}: {pair.Value}"))}).");
            Check.That(deliveries >= 20, $"{rate}: America delivered after its second center ({deliveries} loads).");
            Check.That(far.Values.All(count => count <= 2), $"{rate}: no gatherer flies its loads past a nearer own center ({string.Join(", ", far.Select(pair => $"{pair.Key}: {pair.Value}"))} of {deliveries} loads; {string.Join("; ", farLoads)}).");
        }
    }
}

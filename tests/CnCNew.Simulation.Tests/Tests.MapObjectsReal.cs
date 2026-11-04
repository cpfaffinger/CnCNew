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
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// F-129: a skirmish of the built-in AI on Alpine Assault holds every object of the map that is a logic object in a match of several players - each where the map puts it, the
    /// train among them, which runs - and leaves the trees, props and fluff to the drawing. Until Phase 60 only the supply warehouses were there. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealSkirmishPlacesItsMap(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        string? first = null;
        foreach (var (rate, _) in WorldRates)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate), catalog, fingerprint, TerrainMap.Build(map, MapPath), map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
            var placed = map.Objects.Where(item => MapObjectPlacement.Classify(item, catalog, out _) == MapObjectPlacement.Kind.Placed).ToArray();
            var drawn = map.Objects.Count(item => MapObjectPlacement.Classify(item, catalog, out _) == MapObjectPlacement.Kind.DrawnOnly);
            session.Step();
            var world = session.World;
            var missing = placed.Where(item => catalog.TryGet(item.Name, out var template) && !world.Entities.Any(entity => entity.Template == template
                && (entity.X - Fixed.FromDecimal(item.X)).Abs() < Fixed.One && (entity.Y - Fixed.FromDecimal(item.Y)).Abs() < Fixed.One)).Select(item => item.Name).Distinct().ToArray();
            Check.That(session.MapObjectsPlaced == placed.Length && missing.Length == 0, $"{rate}: {session.MapObjectsPlaced} of {placed.Length} objects of the map placed ({drawn} drawn only); missing: {string.Join(", ", missing.Take(8))}.");
            var train = world.Entities.FirstOrDefault(entity => entity.Modules.OfType<RailroadBehavior>().Any(rail => rail.IsLocomotive));
            Check.That(train is not null && world.Entities.Any(entity => entity.Template.IsKindOf("STRUCTURE") && entity.Owner.IsSystem && entity.Template.Name.Contains("Civilian", StringComparison.Ordinal) || entity.Template.IsKindOf("CAPTURABLE") && entity.Owner.IsSystem),
                $"{rate}: the train and the neutral buildings are there.");
            var (startX, startY) = (train!.X, train.Y);
            for (var tick = 0; tick < rate.Time.OriginalFrames(600); tick++) session.Step();
            Check.That(train.X != startX || train.Y != startY, $"{rate}: the train runs.");
            var placedNow = string.Join(";", placed.Select(item => item.Name).Order(StringComparer.Ordinal));
            first ??= placedNow;
            Check.That(placedNow == first, $"{rate}: the same objects at every rate.");
            Console.WriteLine($"       {rate}: {placed.Length} placed, {drawn} drawn only, {world.Entities.Count(entity => entity.Owner.IsSystem)} neutral objects after 20 s");
        }
    }
}

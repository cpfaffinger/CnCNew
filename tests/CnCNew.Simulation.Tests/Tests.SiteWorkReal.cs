using System.Security.Cryptography;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Modules;
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
    /// The owner's finding of 8 November 2026: a building put down on a tree leaves the tree standing, and the ground under it is not levelled. On Alpine Assault a dozer begins a power
    /// plant on a tree on sloping ground. The original removes the trees and props the footprint touches (<c>BuildAssistant::clearRemovableForConstruction</c>) and lowers the terrain under the
    /// structure to one level (<c>TerrainLogic::flattenTerrain</c>) before the structure is put on the ground. The world answers with the site in <c>ClearedSites</c> (what the viewer takes the
    /// trees off the map by) and its own heights: the highest vertex of the footprint ends no higher than the level, the structure stands on the level, the map every world starts from is
    /// untouched, a saved and restored world has the same heights and hash. The same at 30, 60 and 120 Hz.
    /// </summary>
    public static void ABuildingClearsItsTreesAndFlattensItsGround(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        string? firstFootprint = null;
        foreach (var (rate, _) in WorldRates)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var terrain = TerrainMap.Build(map, MapPath);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate), catalog, fingerprint, terrain, map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica"), IsHuman: true), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
            for (var tick = 0; tick < rate.Time.OriginalFrames(30); tick++) session.Step();
            var world = session.World;
            Check.That(!ReferenceEquals(world.Terrain, terrain) && world.Terrain!.IsFork && !terrain.IsFork, $"{rate}: the world works on a fork of the map's terrain.");
            var player = session.Players[0].Id;
            var dozer = world.Entities.First(entity => entity.Owner == player && entity.Template.IsKindOf("DOZER"));
            Check.That(catalog.TryGet("AmericaPowerPlant", out var plant), "The power plant is a template.");
            Check.That(plant!.IsBox, "The power plant is a box.");
            var treeRadius = Fixed.FromInt(14);
            // The shroud is not what is tested: the site's ground rules are asked for, and the order is not judged by the player's view.
            world.SetValidatesOwnSites(player);

            // The nearest tree of the map to the dozer on which the structure may be begun and whose footprint lies on a slope.
            var trees = map.Objects.Where(item => MapObjectPlacement.Classify(item, catalog, out _) == MapObjectPlacement.Kind.DrawnOnly && catalog.TryGet(item.Name, out var template) && template.IsKindOf("OPTIMIZED_TREE"))
                .Select(item => (X: Fixed.FromDecimal(item.X), Y: Fixed.FromDecimal(item.Y))).ToArray();
            Check.That(trees.Length > 100, $"{rate}: the map has trees ({trees.Length}).");
            (Fixed X, Fixed Y)? chosen = null;
            var reasons = new SortedDictionary<string, int>();
            foreach (var tree in trees.OrderBy(value => (value.X - dozer.X) * (value.X - dozer.X) + (value.Y - dozer.Y) * (value.Y - dozer.Y)))
            {
                var code = world.CheckBuildGround(plant, tree.X, tree.Y, Fixed.Zero);
                reasons[code.ToString()] = reasons.GetValueOrDefault(code.ToString()) + 1;
                if (code != LegalBuildCode.Ok) continue;
                var heights = Footprint(world, plant, tree.X, tree.Y).Select(point => world.Terrain.HeightAt(point.X, point.Y)).ToArray();
                if (heights.Length < 4 || heights.Max() - heights.Min() < Fixed.One) continue;
                chosen = tree;
                break;
            }
            Check.That(chosen is not null, $"{rate}: a tree on a slope within reach of the dozer ({string.Join(", ", reasons.Select(pair => $"{pair.Key} {pair.Value}"))}; dozer at {dozer.X.ToDecimal():F0},{dozer.Y.ToDecimal():F0}).");
            var (x, y) = chosen!.Value;
            var vertices = Footprint(world, plant, x, y);
            var before = vertices.Select(point => world.Terrain.HeightAt(point.X, point.Y)).ToArray();
            var touching = trees.Count(tree => new ClearedSite(true, plant.GeometryMajorRadius, plant.GeometryMinorRadius, x, y, Fixed.Zero, false).Touches(tree.X, tree.Y, treeRadius));
            // The computer player may have begun a building of its own by now, elsewhere: what counts is what this order adds.
            var (sitesBefore, loweredBefore) = (world.ClearedSites.Count, world.Terrain.LoweredVertices);

            Check.That(session.Issue(player, (sequence, tick) => new ConstructCommand(player, sequence, tick, dozer.Id, "AmericaPowerPlant", x, y, Fixed.Zero)).Accepted, $"{rate}: the order to build is accepted.");
            for (var tick = 0; tick < rate.Time.OriginalFrames(3); tick++) session.Step();
            Check.That(world.ClearedSites.Count == sitesBefore + 1, $"{rate}: the building's site is remembered ({world.ClearedSites.Count} after {sitesBefore}).");
            var site = world.ClearedSites[sitesBefore];
            Check.That(site.Box && site.X == x && site.Y == y && site.Major == plant.GeometryMajorRadius && site.Minor == plant.GeometryMinorRadius, $"{rate}: the site is the footprint of the plant.");
            Check.That(site.Touches(x, y, treeRadius), $"{rate}: the tree the plant stands on is touched by its site.");
            Check.That(touching >= 1 && !site.Touches(x + plant.GeometryMajorRadius + treeRadius + Fixed.FromInt(5), y, treeRadius), $"{rate}: {touching} trees touch the site, one at a good distance does not.");

            var after = vertices.Select(point => world.Terrain.HeightAt(point.X, point.Y)).ToArray();
            Check.That(world.Terrain.LoweredVertices > loweredBefore && terrain.LoweredVertices == 0, $"{rate}: the world's terrain was lowered ({world.Terrain.LoweredVertices} vertices), the map's was not.");
            Check.That(after.Zip(before).All(pair => pair.First <= pair.Second), $"{rate}: no vertex under the plant was raised.");
            Check.That(after.Max() < before.Max(), $"{rate}: the highest vertex came down ({before.Max().ToDecimal():F2} to {after.Max().ToDecimal():F2}).");
            Check.That(after.Max() - after.Min() < before.Max() - before.Min(), $"{rate}: the ground under the plant is flatter ({(before.Max() - before.Min()).ToDecimal():F2} to {(after.Max() - after.Min()).ToDecimal():F2}).");
            var centre = world.Entities.First(entity => entity.Template == plant);
            Check.That(centre.Z == world.GroundHeightAt(x, y), $"{rate}: the structure stands on the flattened ground.");
            var footprint = string.Join(",", after.Select(value => value.Raw));
            firstFootprint ??= footprint;
            Check.That(footprint == firstFootprint, $"{rate}: the same flattened heights at every rate.");

            // The state: the world's heights and the list come back from a save, in the same hash.
            var restored = SkirmishMatchSession.Restore(session.Save(), catalog, fingerprint, TerrainMap.Build(map, MapPath));
            Check.That(restored.World.ClearedSites.SequenceEqual(world.ClearedSites), $"{rate}: the restored world has the site.");
            Check.That(restored.World.Terrain!.CopyRawHeights().AsSpan().SequenceEqual(world.Terrain.CopyRawHeights()), $"{rate}: the restored world has the same heights.");
            Check.That(restored.StateHash() == session.StateHash(), $"{rate}: the restored session has the same hash.");
            Console.WriteLine($"       {rate}: {touching} trees under the plant, {world.Terrain.LoweredVertices - loweredBefore} vertices lowered, the footprint {(before.Max() - before.Min()).ToDecimal():F2} -> {(after.Max() - after.Min()).ToDecimal():F2} high");
        }
    }

    /// <summary>The height vertices (world positions) inside the unturned footprint of a box.</summary>
    private static List<(Fixed X, Fixed Y)> Footprint(SimulationWorld world, ObjectTemplate template, Fixed x, Fixed y)
    {
        var points = new List<(Fixed, Fixed)>();
        var (major, minor) = (template.GeometryMajorRadius, template.GeometryMinorRadius);
        for (var i = (int)((x - major) / Fixed.FromInt(10)).Floor(); i <= (int)((x + major) / Fixed.FromInt(10)).Floor() + 1; i++)
            for (var j = (int)((y - minor) / Fixed.FromInt(10)).Floor(); j <= (int)((y + minor) / Fixed.FromInt(10)).Floor() + 1; j++)
            {
                var (px, py) = (Fixed.FromInt(i * 10), Fixed.FromInt(j * 10));
                if ((px - x).Abs() <= major && (py - y).Abs() <= minor) points.Add((px, py));
            }
        return points;
    }
}

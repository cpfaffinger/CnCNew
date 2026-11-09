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
    /// The owner's finding of 8 November 2026: on Alpine Assault a bulldozer that stands on the hill above the train's tunnel dies when the train passes under it. The original's physics
    /// engine calls <c>onCollide</c> only for a pair whose geometry meets in height as well (<c>PartitionManager</c>, the z-intersection test before every collide test), and inside
    /// a tunnel the locomotive runs below the terrain. On the real map: a dozer on the ground over a deep tunnel segment is left alone (not hurt, not shoved, no whistle); the same
    /// dozer on the open track is hit. At 30, 60 and 120 Hz.
    /// </summary>
    public static void ATrainUnderAHillLeavesWhatStandsOnItAlone(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        foreach (var (rate, _) in WorldRates)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate), catalog, fingerprint, TerrainMap.Build(map, MapPath), map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
            session.Step();
            var world = session.World;
            var train = world.Entities.First(entity => entity.Modules.OfType<RailroadBehavior>().Any(rail => rail.IsLocomotive));
            var rail = train.Modules.OfType<RailroadBehavior>().First();
            var dozerTemplate = (catalog.TryGet("AmericaVehicleDozer", out var dozerInfo) ? dozerInfo.Index : throw new InvalidOperationException());
            var owner = world.Entities.First(entity => !entity.Owner.IsSystem).Owner;

            // Run until the locomotive is in the situation, then put a dozer on the ground under it and watch the next second.
            (bool Hit, bool Whistled, Fixed Depth) Probe(Func<bool> ready)
            {
                for (var tick = 0; !ready(); tick++)
                {
                    Check.That(tick < rate.Time.OriginalFrames(30 * 600), $"{rate}: the train reaches the place in ten minutes.");
                    session.Step();
                }
                var (x, y) = (train.X, train.Y);
                var depth = world.GroundHeightAt(x, y) - train.Z;
                var id = world.RequestSpawn(dozerTemplate, owner, x, y, world.GroundHeightAt(x, y), Fixed.Zero);
                session.Step();
                var dozer = world.Entities.First(entity => entity.Id == id);
                var (px, py, health) = (dozer.X, dozer.Y, dozer.Body!.Health);
                var whistled = false;
                for (var tick = 0; tick < rate.Time.OriginalFrames(10); tick++)
                    foreach (var item in session.Step().Events)
                        whistled |= item.Kind == CnCNew.Simulation.Events.SimEventKind.TrainWhistle && item.Other == id;
                var hit = dozer.EffectivelyDead || dozer.Body.Health < health || dozer.X != px || dozer.Y != py;
                return (hit, whistled, depth);
            }

            var under = Probe(() => rail.InTunnel && world.GroundHeightAt(train.X, train.Y) - train.Z > Fixed.FromInt(30));
            Check.That(!under.Hit && !under.Whistled, $"{rate}: a dozer on the hill {under.Depth.ToDecimal():F1} above the train in its tunnel is neither hurt nor shoved nor whistled at.");
            var open = Probe(() => !rail.InTunnel && (world.GroundHeightAt(train.X, train.Y) - train.Z).Abs() < Fixed.One && world.Time.IsOriginalFrameBoundary(world.Tick));
            Check.That(open.Hit && open.Whistled, $"{rate}: a dozer on the open track is hit (hit {open.Hit}, whistle {open.Whistled}).");
            Console.WriteLine($"       {rate}: over a tunnel {under.Depth.ToDecimal():F1} deep left alone, on the open track hit");
        }
    }
}

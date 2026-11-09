using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;

namespace CnCNew.Game.Tests;

/// <summary>
/// The owner's finding of 8 November 2026: a building put down on a tree leaves the tree standing, and the ground under it is not levelled. On real data: a dozer begins a power plant on a
/// tree of Alpine Assault; the tree leaves the drawn scene, the terrain mesh is made again where the ground came down, and the camera's height sampler agrees with the simulation.
/// </summary>
internal static class SiteWorkTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(SiteWorkTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP SiteWorkTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("A power plant begun on a tree: the tree is gone from the scene, the terrain mesh and the height sampler follow the flattened ground", () => PlantOnATree(data));
        return harness.Failures;
    }

    private static void PlantOnATree(GameData data)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var live = LiveMatch.Create(data, new MatchSetup(mapPath, "FactionAmerica", ["FactionChina"], SkirmishMatchOptions.Standard), new Vector2(1280, 720));
        void Run(double seconds)
        {
            for (var frame = 0; frame < seconds * CnCNew.Tests.TestTime.FramesPerSecond; frame++) live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
        }
        var (human, world) = (live.Human, live.World);
        // The trees of this map stand far from the start base: the shroud is not what is tested, so the player sees the whole map.
        world.RevealMapPermanently(human, true);
        Run(2);
        var dozer = world.Entities.First(entity => entity.Owner == human && entity.Template.Name == "AmericaVehicleDozer");
        var scenery = StaticScenery.Build(live.Map.Objects, data.Visuals).Items;
        var trees = scenery.Where(item => item.Visual.IsKindOf("OPTIMIZED_TREE")).OrderBy(item => Vector2.DistanceSquared(new Vector2(item.Position.X, item.Position.Y), new Vector2((float)dozer.X.ToDecimal(), (float)dozer.Y.ToDecimal()))).ToArray();
        TestHarness.Assert(trees.Length > 100, $"the map has trees ({trees.Length})");

        // The nearest tree whose place is a legal site for the plant, on ground that is not level.
        ConstructCommand? order = null;
        SceneryItem? tree = null;
        var tried = new SortedDictionary<string, int>();
        foreach (var candidate in trees.Take(1500))
        {
            var (x, y) = (FixedConvert(candidate.Position.X), FixedConvert(candidate.Position.Y));
            var command = new ConstructCommand(human, 0, 0, dozer.Id, "AmericaPowerPlant", x, y, Fixed.Zero);
            var verdict = world.Check(command);
            if (!verdict.Accepted && world.Templates.TryGet("AmericaPowerPlant", out var plantTemplate)) tried["site:" + world.CheckBuildSite(dozer, plantTemplate, x, y, Fixed.Zero)] = tried.GetValueOrDefault("site:" + world.CheckBuildSite(dozer, plantTemplate, x, y, Fixed.Zero)) + 1;
            tried[verdict.Accepted ? "accepted" : verdict.Reason.ToString()] = tried.GetValueOrDefault(verdict.Accepted ? "accepted" : verdict.Reason.ToString()) + 1;
            if (!verdict.Accepted) continue;
            var corners = new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }.Select(corner => world.GroundHeightAt(x + Fixed.FromInt(corner.Item1 * 25), y + Fixed.FromInt(corner.Item2 * 25))).ToArray();
            if (corners.Max() - corners.Min() < Fixed.FromDecimal(0.8m)) continue;
            (order, tree) = (command, candidate);
            break;
        }
        TestHarness.Assert(order is not null && tree is not null, $"a tree on a slope where the plant may be begun ({string.Join(", ", tried.Select(pair => $"{pair.Key} {pair.Value}"))}; dozer at {dozer.X.ToDecimal():F0},{dozer.Y.ToDecimal():F0})");
        var placed = order!;
        bool Drawn(SceneryItem item) => live.Frame.Drawables.Any(drawable => drawable.SceneryIndex == item.Index);
        TestHarness.Assert(Drawn(tree!), "before the building the tree is in the scene");
        var before = live.TerrainModel;
        var heightBefore = live.Heights.HeightAt(tree!.Position.X, tree.Position.Y);
        live.TakeChangedTerrainChunks();

        TestHarness.Assert(live.Commands.Issue((sequence, tick) => placed with { Sequence = sequence, Tick = tick }).Accepted, "the plant is ordered");
        Run(3);
        TestHarness.Assert(world.ClearedSites.Count >= 1, "the world remembers the site");
        TestHarness.Assert(!Drawn(tree), "the tree the plant stands on is gone from the scene");
        TestHarness.Assert(trees.Where(item => !Drawn(item)).Count() >= 1 && trees.Count(item => Drawn(item)) > trees.Length / 2, "only the trees near the plant left, not all of them");

        var heightAfter = live.Heights.HeightAt(tree.Position.X, tree.Position.Y);
        var simulated = (float)world.GroundHeightAt(placed.X, placed.Y).ToDecimal();
        TestHarness.Assert(Math.Abs(live.Heights.HeightAt((float)placed.X.ToDecimal(), (float)placed.Y.ToDecimal()) - simulated) < 0.01f, "the camera's height sampler agrees with the simulation on the ground of the plant");
        TestHarness.Assert(heightAfter <= heightBefore, $"the ground did not rise ({heightBefore:F2} to {heightAfter:F2})");

        var changed = live.TakeChangedTerrainChunks();
        TestHarness.Assert(changed.Length >= 1 && changed.Length <= 4, $"the terrain mesh made {changed.Length} chunks again");
        var lowered = changed.Any(chunk => live.TerrainModel.Chunks[chunk].Base.Positions.Max(position => position.Z) < before.Chunks[chunk].Base.Positions.Max(position => position.Z)
            || live.TerrainModel.Chunks[chunk].Base.Positions.Sum(position => position.Z) < before.Chunks[chunk].Base.Positions.Sum(position => position.Z));
        TestHarness.Assert(lowered, "a changed chunk has lower ground than before");
        TestHarness.Assert(Enumerable.Range(0, before.Chunks.Length).Where(chunk => !changed.Contains(chunk)).All(chunk => ReferenceEquals(before.Chunks[chunk], live.TerrainModel.Chunks[chunk])), "the other chunks are the ones they were");
        Console.WriteLine($"       the plant at {placed.X.ToDecimal():F0},{placed.Y.ToDecimal():F0}: ground {heightBefore:F2} -> {heightAfter:F2} at the tree, chunks made again: {string.Join(",", changed)}");
    }

    private static Fixed FixedConvert(float value) => CnCNew.Presentation.FixedConvert.FromSingle(value);
}

using System.Collections.Immutable;
using CnCNew.Game.Data;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

/// <summary>
/// Real data (#410): the soldiers of a GLA Stinger Site stand under its net. <c>SpawnPointProductionExitUpdate::exitObjectViaDoor</c> puts a new spawn on the first free bone
/// <c>SpawnPoint01</c>, <c>02</c>, <c>03</c> of the site's model (<c>initializeBonePositions</c>), some 16 units from its centre inside its 36-unit footprint, and holds it there. Before,
/// the port had no bones and put them on the site's bounding circle, beyond its sandbags. The soldiers fire from there at an enemy in range, and a soldier that dies is replaced at the
/// bone it left, at 30, 60 and 120 Hz.
/// </summary>
internal static class StingerSpawnPointTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(StingerSpawnPointTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP StingerSpawnPointTests (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Real data (#410): the stinger site's soldiers stand at its spawn bones under the net, fire from there and are replaced there, at 30, 60 and 120 Hz", () => UnderTheNet(data));
        return harness.Failures;
    }

    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "GLA"), new(new PlayerId(2), "America")];

    private static void Assert(bool condition, string message) => TestHarness.Assert(condition, message);

    private static Fixed Distance(Fixed x, Fixed y, (Fixed X, Fixed Y) point) => Fixed.Sqrt((x - point.X) * (x - point.X) + (y - point.Y) * (y - point.Y));

    /// <summary>What one run saw: per soldier the farthest it came from its bone, the frame of the first hit on the tank, and the replacement's distance from the dead soldier's bone.</summary>
    private sealed record Outcome(Fixed FarthestFromBone, long FirstHit, Fixed ReplacementFromBone);

    private static void UnderTheNet(GameData data)
    {
        var bones = data.Templates.Geometry.SpawnPoints.TryGetValue("GLAStingerSite", out var found) ? found : [];
        Assert(bones.Length == 3, $"the stinger site's model has three spawn bones ({bones.Length})");
        foreach (var bone in bones)
        {
            var reach = Fixed.Sqrt(bone.X * bone.X + bone.Y * bone.Y);
            Assert(reach < Fixed.FromInt(20), $"a spawn bone lies under the net, {reach} from the centre (footprint 36)");
        }
        foreach (var variant in new[] { "GLAStingerSiteNoHole", "Chem_GLAStingerSite", "Demo_GLAStingerSite", "Slth_GLAStingerSite" })
            Assert(data.Templates.Geometry.SpawnPoints.TryGetValue(variant, out var other) && other.Length == 3, $"{variant} has its three spawn bones");

        Outcome? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var templates = TemplateCatalog.Build(data.Graph, CnCNew.Simulation.Modules.ModuleBindingTable.Default, rate, data.Templates.Geometry, data.Rules);
            var outcome = Run(templates, rate, bones);
            Console.WriteLine($"       {rate}: farthest from a bone {outcome.FarthestFromBone}, first hit in frame {outcome.FirstHit}, replacement {outcome.ReplacementFromBone} from the bone");
            Assert(outcome.FarthestFromBone < Fixed.FromInt(2), $"{rate}: every soldier stands at its bone ({outcome.FarthestFromBone} from it)");
            Assert(outcome.FirstHit > 0, $"{rate}: the soldiers hit the tank in range from under the net ({outcome.FirstHit})");
            Assert(outcome.ReplacementFromBone < Fixed.FromInt(2), $"{rate}: a dead soldier is replaced at its bone ({outcome.ReplacementFromBone} from it)");
            reference ??= outcome;
            Assert(outcome.FirstHit == reference.FirstHit, $"{rate}: the first hit in the frame of 30 Hz ({outcome.FirstHit} against {reference.FirstHit})");
        }
    }

    private static Outcome Run(TemplateCatalog templates, SimulationRate rate, ImmutableArray<BonePose> bones)
    {
        var terrain = TerrainMap.Build(CnCNew.Simulation.Testing.FlatMap.Build(120, 120, 1), "stinger-net");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, Players, "stinger-net", "stinger-net", terrain.Fingerprint), templates, terrain);
        var sequence = new Dictionary<int, ulong>();
        ulong Next(int player) => sequence[player] = sequence.GetValueOrDefault(player) + 1;
        void Spawn(int player, string template, int x, int y, Fixed angle)
            => Assert(world.Submit(new SpawnCommand(new PlayerId(player), Next(player), world.Tick, template, new PlayerId(player), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, angle)).Accepted,
                $"{template} spawns");

        // A turned site: the bones turn with it.
        var siteAngle = FixedTrig.Pi / Fixed.FromInt(3);
        Spawn(1, "GLAStingerSite", 300, 600, siteAngle);
        for (var step = 0; step < world.Time.OriginalFrames(30); step++) world.Step();
        var site = world.Entities.Single(value => value.Template.Name == "GLAStingerSite");
        var (cos, sin) = (FixedTrig.Cos(site.Angle), FixedTrig.Sin(site.Angle));
        var points = bones.Select(pose => (X: site.X + pose.X * cos - pose.Y * sin, Y: site.Y + pose.X * sin + pose.Y * cos)).ToArray();
        Fixed FromNearestBone(Entity soldier) => points.Min(point => Distance(soldier.X, soldier.Y, point));

        var soldiers = world.Entities.Where(value => value.Template.Name == "GLAInfantryStingerSoldier").OrderBy(value => value.Id.Value).ToArray();
        Assert(soldiers.Length == 3, $"{rate}: the site has its three soldiers ({soldiers.Length})");
        // One soldier per bone.
        var taken = soldiers.Select(soldier => Array.FindIndex(points, point => Distance(soldier.X, soldier.Y, point) < Fixed.FromInt(2))).ToArray();
        Assert(taken.Distinct().Count() == 3 && taken.All(value => value >= 0), $"{rate}: each soldier has a bone of its own ({string.Join(",", taken)})");

        // An enemy in the rockets' range (225).
        Spawn(2, "AmericaTankCrusader", 500, 600, FixedTrig.Pi);
        world.Step();
        var tank = world.Entities.Single(value => value.Template.Name == "AmericaTankCrusader");
        var farthest = Fixed.Zero;
        long firstHit = -1;
        var start = world.Tick;
        for (var step = 0; step < world.Time.OriginalFrames(300); step++)
        {
            world.Step();
            foreach (var soldier in soldiers)
                if (!soldier.EffectivelyDead) farthest = Fixed.Max(farthest, FromNearestBone(soldier));
            if (firstHit < 0 && tank.Body!.Health < tank.Body.MaxHealth) firstHit = (world.Tick - start + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
        }
        if (!tank.EffectivelyDead) world.Kill(tank.Id);

        // One soldier dies; SpawnReplaceDelay (30 s) later its replacement stands at the bone it left.
        var victim = soldiers.First(soldier => !soldier.EffectivelyDead);
        var victimBone = points[Array.FindIndex(points, point => Distance(victim.X, victim.Y, point) < Fixed.FromInt(2))];
        var before = world.Entities.Where(value => value.Template.Name == "GLAInfantryStingerSoldier").Select(value => value.Id).ToHashSet();
        world.Kill(victim.Id);
        Entity? replacement = null;
        for (var step = 0; step < world.Time.OriginalFrames(30 * 40) && replacement is null; step++)
        {
            world.Step();
            replacement = world.Entities.FirstOrDefault(value => value.Template.Name == "GLAInfantryStingerSoldier" && !before.Contains(value.Id));
        }
        Assert(replacement is not null, $"{rate}: the dead soldier is replaced");
        for (var step = 0; step < world.Time.OriginalFrames(60); step++) world.Step();
        return new Outcome(farthest, firstHit, Distance(replacement!.X, replacement.Y, victimBone));
    }
}

using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Data.World;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// Real data, #451: Comanches a player orders to attack a ground unit (the client's attack order, <see cref="StandingOrders"/>) fly to a place within their weapon's range and hover there,
/// each in a box of goal cells of its own, and fire - not onto their victim. The original's approach of an airborne attacker (<c>AIUpdateInterface::computeAttackPath</c>: "airborne
/// objects don't pathfind") flies to <c>Weapon::computeApproachTarget</c>: 0.9 of the weapon's range from the victim, on the line from the victim to the attacker, adjusted to the nearest
/// box of goal cells that no other aircraft holds and from which the weapon reaches (<c>Pathfinder::adjustTargetDestination</c>), and takes that box (<c>updateGoal</c>). At 30, 60 and 120 Hz.
/// </summary>
internal static class HelicopterAttackRealTests
{
    private static readonly PlayerId Me = new(1);
    private static readonly PlayerId Enemy = new(2);

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP Helicopter attack real-data tests (pass --content-dir <dir>)");
            return 0;
        }
        var content = ContentFileSystem.Open(args[index + 1]);
        var graph = DefinitionCompiler.Compile(new ContentSource(content), ModuleRegistry.Default,
            DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var t = new TestHarness(nameof(HelicopterAttackRealTests));
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            t.Run($"#451 at {rate}: four Comanches ordered to attack a Battlemaster hover within range, each in a box of its own, and fire",
                () => Attack(catalog, fingerprint, rate, "ChinaTankBattleMaster", 4));
            t.Run($"#451 at {rate}: one Comanche ordered to attack a Scorpion hovers within range and fires",
                () => Attack(catalog, fingerprint, rate, "GLATankScorpion", 1));
        }
        return t.Failures;
    }

    private static void Attack(TemplateCatalog catalog, string fingerprint, SimulationRate rate, string victimTemplate, int count)
    {
        var players = ImmutableArray.Create(new PlayerConfiguration(Me, "Me"), new PlayerConfiguration(Enemy, "Enemy"));
        var terrain = FlatTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 5, players, "real-heli-attack", fingerprint, terrain.Fingerprint), catalog, terrain);
        var sink = new InputTests.WorldSink(world, []);
        var orders = new StandingOrders();
        ulong system = 0;
        void Spawn(string template, PlayerId owner, int x, int y)
            => Assert(world.Submit(new SpawnCommand(PlayerId.System, ++system, world.Tick, template, owner, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template} spawns");
        for (var index = 0; index < count; index++) Spawn("AmericaVehicleComanche", Me, 2000 + 60 * index, 2000);
        Spawn(victimTemplate, Enemy, 2350, 2400);
        world.Step();
        var helis = world.Entities.Where(value => value.Template.Name == "AmericaVehicleComanche").OrderBy(value => value.Id.Value).ToList();
        var victim = world.Entities.Single(value => value.Template.Name == victimTemplate);
        Assert(helis.Count == count, $"{count} Comanches");
        // Let them lift off first.
        for (var tick = 0; tick < world.Time.OriginalFrames(60); tick++) world.Step();

        foreach (var heli in helis)
            orders.Set(heli.Id, new StandingOrder(StandingOrderKind.Attack, victim.Id, new Vector2(2350, 2400), [], world.Tick));
        var fired = new HashSet<uint>();
        var firstShot = new Dictionary<uint, float>();
        var closest = helis.ToDictionary(heli => heli.Id.Value, _ => float.MaxValue);
        var range = 200f;
        var circles = helis.ToDictionary(heli => heli.Id.Value, heli => (float)(heli.Template.BoundingCircleRadius + victim.Template.BoundingCircleRadius).ToDecimal());
        float Gap(Entity heli) => Vector2.Distance(Ground(heli), Ground(victim));
        string Where() => string.Join("; ", helis.Select(heli => $"{heli.Id.Value}@({heli.X.ToDecimal():0.#}, {heli.Y.ToDecimal():0.#}) d {Gap(heli):0.#}{(heli.HasMoveTarget ? " moving" : "")}"));
        var settled = false;
        for (var tick = 0; tick < world.Time.OriginalFrames(30 * 30) && victim.Body!.Health > Fixed.Zero; tick++)
        {
            orders.Update(world, Me, make => sink.Issue(make));
            foreach (var simEvent in world.Step().Events)
                if (simEvent.Kind == SimEventKind.WeaponFired && helis.Any(heli => heli.Id == simEvent.Entity) && fired.Add(simEvent.Entity.Value))
                    firstShot[simEvent.Entity.Value] = Gap(world.FindEntity(simEvent.Entity)!);
            foreach (var heli in helis) closest[heli.Id.Value] = Math.Min(closest[heli.Id.Value], Gap(heli));
            if (!settled && fired.Count == count && helis.All(heli => !heli.HasMoveTarget))
            {
                settled = true;
                Console.WriteLine($"       {rate} settled at tick {world.Tick}: {Where()}");
                foreach (var heli in helis)
                {
                    var gap = Gap(heli);
                    Assert(gap <= range + circles[heli.Id.Value], $"{rate}: Comanche {heli.Id.Value} hovers within its weapon's range ({gap:0.#}; {Where()})");
                    Assert(gap >= 0.8f * range - 10f, $"{rate}: Comanche {heli.Id.Value} hovers near its range, not over its victim ({gap:0.#}; {Where()})");
                    // Alone it takes the approach target itself: 0.9 of the range (less a quarter cell) from the victim, within a cell.
                    if (count == 1) Assert(Math.Abs(gap - 0.9f * (range - 2.5f)) <= 10f, $"{rate}: the Comanche hovers at 0.9 of its range from its victim ({gap:0.#})");
                }
                for (var a = 0; a < helis.Count; a++)
                    for (var b = a + 1; b < helis.Count; b++)
                    {
                        var dx = (helis[a].X - helis[b].X).Abs();
                        var dy = (helis[a].Y - helis[b].Y).Abs();
                        Assert(dx >= Fixed.FromInt(40) || dy >= Fixed.FromInt(40), $"{rate}: Comanches {helis[a].Id.Value} and {helis[b].Id.Value} hold boxes of their own ({Where()})");
                    }
            }
        }
        Console.WriteLine($"       {rate} end at tick {world.Tick}: {Where()}; first shots at {string.Join(", ", firstShot.Select(pair => $"{pair.Key}:{pair.Value:0}"))}; closest {string.Join(", ", closest.Select(pair => $"{pair.Key}:{pair.Value:0}"))}");
        Assert(fired.Count == count, $"{rate}: every Comanche fires ({fired.Count} of {count})");
        Assert(settled, $"{rate}: every Comanche came to a hover while it attacked ({Where()})");
        Assert(helis.All(heli => closest[heli.Id.Value] >= 0.8f * range - 10f), $"{rate}: no Comanche flew onto its victim (closest {string.Join(", ", closest.Values.Select(value => value.ToString("0")))})");
    }

    /// <summary>A flat map of 400 x 400 cells (4,000 units), so the movement has its terrain and its grid.</summary>
    private static TerrainMap FlatTerrain()
    {
        const int cells = 400, border = 1;
        var width = cells + 2 * border;
        var stride = (width + 7) / 8;
        var blend = new MapBlendTiles(8, [], [], [], [], [..new byte[stride * width]], stride, 0, 0, [], [], [], []);
        var map = new MapData("heli-attack", new MapHeightField(width, width, border, [new MapBoundary(cells, cells)], [..new byte[width * width]]), blend,
            MapProperties.Empty, [], [], [], [], [], [], []);
        return TerrainMap.Build(map, "heli-attack");
    }

    private static Vector2 Ground(Entity entity) => new((float)entity.X.ToDecimal(), (float)entity.Y.ToDecimal());

    private sealed class ContentSource(ContentFileSystem content) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
        public bool Exists(string virtualPath) => content.Exists(virtualPath);
    }
}

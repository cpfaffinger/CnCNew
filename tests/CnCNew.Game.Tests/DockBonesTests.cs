using System.Collections.Immutable;
using CnCNew.Game.Data;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

/// <summary>
/// Real data (#354): the docks of the shipped models take their bones (<c>DockUpdate::loadDockPositions</c>). An American Chinook takes its boxes standing at the civilian supply
/// warehouse's <c>DockAction</c> bone and unloads them standing at the American supply center's, having come in by their <c>DockStart</c>; a Chinese supply truck takes its boxes at the
/// warehouse's bone too and unloads at its boneless Chinese supply center beside it; a damaged Crusader is repaired standing at the American war factory's <c>DockAction</c>.
/// </summary>
internal static class DockBonesTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(DockBonesTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP DockBonesTests (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Real data (#354): supply trucks take and unload their boxes at the docks' action bones", () => SupplyTrucks(data));
        harness.Run("Real data (#354): a Crusader is repaired at the war factory's action bone", () => WarFactoryRepair(data));
        return harness.Failures;
    }

    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "America"), new(new PlayerId(2), "China")];

    private sealed class Bench(GameData data)
    {
        private static readonly TerrainMap Terrain = TerrainMap.Build(CnCNew.Simulation.Testing.FlatMap.Build(160, 160, 1), "dock-bones");

        public SimulationWorld World { get; } = new(new WorldConfiguration(new WorldId(1), CnCNew.Tests.TestRate.Default, 7, Players, "dock-bones", data.Fingerprint, Terrain.Fingerprint),
            data.Templates, Terrain);

        private readonly Dictionary<int, ulong> _sequence = [];

        public ulong Next(int player) => _sequence[player] = _sequence.GetValueOrDefault(player) + 1;

        public Entity Spawn(int player, string template, int x, int y)
        {
            Assert(World.Submit(new SpawnCommand(new PlayerId(player), Next(player), World.Tick, template, new PlayerId(player), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template} spawns.");
            World.Step();
            return World.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).Last();
        }

        public void Dock(int player, Entity actor, Entity dock)
            => Assert(World.Submit(new DockCommand(new PlayerId(player), Next(player), World.Tick, actor.Id, dock.Id)).Accepted, $"{actor.Template.Name} is ordered to {dock.Template.Name}.");

        /// <summary>A bone of <paramref name="dock"/>'s model in the world.</summary>
        public (Fixed X, Fixed Y) Bone(Entity dock, Func<DockBones, BonePose?> pick)
        {
            var pose = pick(World.Templates.Geometry.Docks[dock.Template.Name]) ?? throw new InvalidOperationException($"{dock.Template.Name} lacks the bone.");
            var (cos, sin) = (FixedTrig.Cos(dock.Angle), FixedTrig.Sin(dock.Angle));
            return (dock.X + pose.X * cos - pose.Y * sin, dock.Y + pose.X * sin + pose.Y * cos);
        }
    }

    private static void Assert(bool condition, string message) => TestHarness.Assert(condition, message);

    private static Fixed Distance(Entity entity, (Fixed X, Fixed Y) point)
    {
        var (dx, dy) = (entity.X - point.X, entity.Y - point.Y);
        return Fixed.Sqrt(dx * dx + dy * dy);
    }

    private static AIUpdateInterface AiOf(Entity entity) => entity.Modules.OfType<AIUpdateInterface>().First();

    /// <summary>Where each truck stood at each dock when the dock began to work on it (<c>onDockReached</c>), and how close it came to the dock's way in.</summary>
    private sealed record Visit(Entity Truck, Entity Dock)
    {
        public (Fixed X, Fixed Y)? Served { get; set; }
        public Fixed ClosestToStart { get; set; } = Fixed.MaxValue;
    }

    private static void SupplyTrucks(GameData data)
    {
        var bench = new Bench(data);
        var warehouse = bench.Spawn(0, "SupplyWarehouse", 800, 800);
        var american = bench.Spawn(1, "AmericaSupplyCenter", 400, 800);
        var chinese = bench.Spawn(2, "ChinaSupplyCenter", 1200, 800);
        var americanTruck = bench.Spawn(1, "AmericaVehicleChinook", 500, 600);
        var chineseTruck = bench.Spawn(2, "ChinaVehicleSupplyTruck", 1100, 600);
        bench.Dock(1, americanTruck, warehouse);
        bench.Dock(2, chineseTruck, warehouse);
        var visits = new[] { new Visit(americanTruck, warehouse), new Visit(americanTruck, american), new Visit(chineseTruck, warehouse), new Visit(chineseTruck, chinese) };
        var deposits = new Dictionary<EntityId, (Fixed X, Fixed Y)>();
        var start = new Dictionary<EntityId, (Fixed X, Fixed Y)>
        {
            [warehouse.Id] = bench.Bone(warehouse, bones => bones.Start), [american.Id] = bench.Bone(american, bones => bones.Start),
        };
        for (var step = 0; step < bench.World.Time.OriginalFrames(30 * 240) && deposits.Count < 2; step++)
        {
            var result = bench.World.Step();
            foreach (var visit in visits)
            {
                var ai = AiOf(visit.Truck);
                if (ai.DockingAt != visit.Dock.Id) continue;
                if (start.TryGetValue(visit.Dock.Id, out var way)) visit.ClosestToStart = Fixed.Min(visit.ClosestToStart, Distance(visit.Truck, way));
                if (visit.Served is null && ai.IsDockActive) visit.Served = (visit.Truck.X, visit.Truck.Y);
            }
            foreach (var deposit in result.Events.Where(value => value.Kind == SimEventKind.SupplyDeposited))
                if (bench.World.FindEntity(deposit.Entity) is { } truck) deposits.TryAdd(truck.Id, (truck.X, truck.Y));
        }
        foreach (var visit in visits)
            Console.WriteLine($"       {visit.Truck.Template.Name} at {visit.Dock.Template.Name}: served at {visit.Served?.ToString() ?? "-"}, closest to DockStart {visit.ClosestToStart}");
        Assert(deposits.Count == 2, $"both trucks brought boxes to their centers ({deposits.Count})");
        Fixed Off((Fixed X, Fixed Y)? at, (Fixed X, Fixed Y) point) => at is { } value ? Fixed.Sqrt((value.X - point.X) * (value.X - point.X) + (value.Y - point.Y) * (value.Y - point.Y)) : Fixed.MaxValue;
        var close = Fixed.FromInt(6);
        var warehouseAction = bench.Bone(warehouse, bones => bones.Action);
        var americanAction = bench.Bone(american, bones => bones.Action);
        Assert(Off(visits[0].Served, warehouseAction) <= close && visits[0].ClosestToStart <= close, $"the American truck came in by the warehouse's DockStart and took its boxes at its DockAction {warehouseAction}");
        Assert(Off(visits[1].Served, americanAction) <= close && visits[1].ClosestToStart <= close, $"the American truck came in by the center's DockStart and unloaded at its DockAction {americanAction}");
        Assert(Off(visits[2].Served, warehouseAction) <= close, $"the Chinese truck took its boxes at the warehouse's DockAction {warehouseAction}");
        // The Chinese supply center has no dock bones: the truck unloads where it waited, beside the building, not at its centre.
        Assert(Off(visits[3].Served, (chinese.X, chinese.Y)) > chinese.Template.GeometryMajorRadius / Fixed.FromInt(2), $"the Chinese truck unloaded beside its boneless center ({visits[3].Served})");
    }

    private static void WarFactoryRepair(GameData data)
    {
        var bench = new Bench(data);
        var factory = bench.Spawn(1, "AmericaWarFactory", 600, 600);
        var tank = bench.Spawn(1, "AmericaTankCrusader", 700, 400);
        var body = tank.Body!;
        Assert(bench.World.Submit(new DamageCommand(PlayerId.System, bench.Next(0), bench.World.Tick, tank.Id, body.MaxHealth / Fixed.FromInt(2), 0, 0)).Accepted, "the Crusader is damaged.");
        bench.World.Step();
        var hurt = body.Health;
        bench.Dock(1, tank, factory);
        (Fixed X, Fixed Y)? healedAt = null;
        var way = bench.Bone(factory, bones => bones.Start);
        var closest = Fixed.MaxValue;
        for (var step = 0; step < bench.World.Time.OriginalFrames(30 * 120) && healedAt is null; step++)
        {
            bench.World.Step();
            if (AiOf(tank).DockingAt == factory.Id) closest = Fixed.Min(closest, Distance(tank, way));
            if (body.Health > hurt) healedAt = (tank.X, tank.Y);
        }
        var action = bench.Bone(factory, bones => bones.Action);
        Console.WriteLine($"       AmericaTankCrusader at AmericaWarFactory: repaired at {healedAt?.ToString() ?? "-"}, the action bone {action}, closest to DockStart {closest}");
        Assert(healedAt is { } at && Fixed.Sqrt((at.X - action.X) * (at.X - action.X) + (at.Y - action.Y) * (at.Y - action.Y)) <= Fixed.FromInt(6) && closest <= Fixed.FromInt(6),
            "the Crusader came in by the war factory's DockStart and is repaired standing at its DockAction");
    }
}

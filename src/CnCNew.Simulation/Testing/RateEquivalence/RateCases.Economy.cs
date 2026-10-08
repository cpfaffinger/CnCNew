using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

public static partial class RateCases
{
    private static RateCaseGroup EconomyCases(RateCatalogs catalogs)
    {
        var builder = new GroupBuilder(catalogs, Economy);
        foreach (var (dozer, structure) in new[] { ("AmericaVehicleDozer", "AmericaPowerPlant"), ("ChinaVehicleDozer", "ChinaPowerPlant"), ("GLAInfantryWorker", "GLABarracks"), ("AmericaVehicleDozer", "AmericaBarracks") })
            builder.Add($"build {structure}", scene => MeasureConstruction(scene, dozer, structure), dozer, structure);
        foreach (var (factory, unit) in new[]
                 {
                     ("AmericaBarracks", "AmericaInfantryRanger"), ("AmericaWarFactory", "AmericaTankCrusader"), ("AmericaCommandCenter", "AmericaVehicleDozer"), ("ChinaBarracks", "ChinaInfantryRedguard"),
                     ("ChinaWarFactory", "ChinaTankBattleMaster"), ("GLABarracks", "GLAInfantryRebel"), ("GLAArmsDealer", "GLAVehicleTechnical"),
                 })
            builder.Add($"produce {unit}", scene => MeasureProduction(scene, factory, unit), factory, unit);
        foreach (var (factory, unit) in new[]
                 {
                     ("AmericaCommandCenter", "AmericaVehicleDozer"), ("AmericaBarracks", "AmericaInfantryRanger"), ("AmericaWarFactory", "AmericaTankCrusader"),
                     ("ChinaCommandCenter", "ChinaVehicleDozer"), ("ChinaBarracks", "ChinaInfantryRedguard"), ("ChinaWarFactory", "ChinaTankBattleMaster"),
                     ("GLACommandCenter", "GLAInfantryWorker"), ("GLABarracks", "GLAInfantryRebel"), ("GLAArmsDealer", "GLATankScorpion"),
                 })
            builder.Add($"production exit {factory}", scene => MeasureProductionExit(scene, factory, unit), factory, unit);
        foreach (var (structure, upgrade) in new[]
                 {
                     ("AmericaBarracks", "Upgrade_AmericaRangerFlashBangGrenade"), ("ChinaBarracks", "Upgrade_InfantryCaptureBuilding"), ("ChinaWarFactory", "Upgrade_ChinaChainGuns"),
                     ("GLABarracks", "Upgrade_InfantryCaptureBuilding"), ("AmericaPowerPlant", "Upgrade_AmericaAdvancedControlRods"),
                 })
            builder.Add($"upgrade {upgrade} at {structure}", scene => MeasureUpgrade(scene, structure, upgrade), structure);
        foreach (var structure in new[] { "AmericaPowerPlant", "ChinaBarracks", "GLAArmsDealer" })
            builder.Add($"sell {structure}", scene => MeasureSale(scene, structure), structure);
        var warehouse = catalogs.Reference.Templates.Where(template => template.IsInstantiable && template.Modules.Any(module => module.Name == "SupplyWarehouseDockUpdate"))
            .OrderBy(template => template.Name, StringComparer.Ordinal).Select(template => template.Name).FirstOrDefault(name => name == "SupplyWarehouse") ?? "SupplyWarehouse";
        foreach (var (center, carrier) in new[] { ("ChinaSupplyCenter", "ChinaVehicleSupplyTruck"), ("AmericaSupplyCenter", "AmericaVehicleChinook"), ("GLASupplyStash", "GLAInfantryWorker") })
            builder.Add($"supply {carrier}", scene => MeasureSupply(scene, center, carrier, warehouse), center, carrier, warehouse);
        foreach (var source in new[] { "GLABlackMarket", "TechOilDerrick", "ChinaInfantryHacker" })
            builder.Add($"income {source}", scene => MeasureIncome(scene, source), source);
        builder.NotCovered("economy: build times of every structure and unit (they are catalog durations on the 30 Hz grid, read with one conversion; the cases above run seven producers and four building sites), several dozers on one site, power shortage slowing production");
        builder.NotCovered("economy: supply drop zone, cash bounty, the hacker's rank bonus over time (five minutes show the first rank only)");
        return builder.Done();
    }

    private static long FirstEvent(TickResult result, SimEventKind kind, EntityId entity)
    {
        foreach (var item in result.Events)
            if (item.Kind == kind && (!entity.IsValid || item.Entity == entity)) return item.Tick;
        return -1;
    }

    /// <summary>A builder next to an empty place: the walk, the start of the site and the building time of the data.</summary>
    private static ImmutableArray<RateQuantity> MeasureConstruction(RateScene scene, string dozerName, string structureName)
    {
        var quantities = new RateQuantities(scene);
        var dozer = scene.Spawn(dozerName, 1, 2000, 2000);
        scene.GiveMoney(1, 20000);
        scene.StepFrames(1);
        scene.Align();
        var start = scene.World.Tick;
        scene.Order(1, (owner, sequence, now) => new ConstructCommand(owner, sequence, now, dozer.Id, structureName, Fixed.FromInt(2200), Fixed.FromInt(2000), Fixed.Zero));
        long started = -1, completed = -1;
        scene.StepUntil(result =>
        {
            if (started < 0) started = FirstEvent(result, SimEventKind.ConstructionStarted, EntityId.Invalid);
            if (completed < 0) completed = FirstEvent(result, SimEventKind.ConstructionCompleted, EntityId.Invalid);
            return completed >= 0;
        }, RateScene.Seconds(240));
        quantities.Event("order to construction started", started >= 0 ? started - start : -1);
        quantities.Exact("construction started to completed", started >= 0 && completed >= 0 ? completed - started : -1);
        quantities.Event("order to completed", completed >= 0 ? completed - start : -1);
        return quantities.Done();
    }

    /// <summary>Two units queued at once: the production time of the data, the second unit, the walk to the rally point.</summary>
    private static ImmutableArray<RateQuantity> MeasureProduction(RateScene scene, string factoryName, string unitName)
    {
        var quantities = new RateQuantities(scene);
        var factory = scene.Spawn(factoryName, 1, 2000, 2000);
        scene.GiveMoney(1, 50000);
        scene.StepFrames(1);
        scene.Align();
        var start = scene.World.Tick;
        for (var index = 0; index < 2; index++) scene.Order(1, (owner, sequence, now) => new QueueProductionCommand(owner, sequence, now, factory.Id, unitName));
        var done = new List<long>();
        var seen = new List<Entity>();
        var appeared = new List<long>();
        long settled = -1;
        scene.StepUntil(result =>
        {
            foreach (var item in result.Events)
                if (item.Kind == SimEventKind.ProductionCompleted && item.Entity == factory.Id) done.Add(item.Tick - start);
            foreach (var entity in scene.World.Entities)
                if (entity.SpawnTick >= start && entity.Template.Name == unitName && !seen.Contains(entity))
                {
                    seen.Add(entity);
                    appeared.Add(scene.World.Tick - TickOrder.Next - start);
                }
            if (seen.Count >= 2 && settled < 0 && seen.All(unit => !unit.HasMoveTarget)) settled = scene.World.Tick - TickOrder.Next - start;
            return settled >= 0;
        }, RateScene.Seconds(240));
        quantities.Exact("queue to first production completed", done.Count > 0 ? done[0] : -1);
        quantities.Exact("first to second production completed", done.Count > 1 ? done[1] - done[0] : -1);
        quantities.Event("queue to first unit outside", appeared.Count > 0 ? appeared[0] : -1);
        quantities.Event("queue to both units standing", settled);
        return quantities.Done();
    }

    /// <summary>
    /// Three units queued at once and no rally point: each drives out of the factory along its exit path, the later ones ask the earlier ones to make room
    /// (docs/SIMULATION-CORE.md §42). Read at the end of every logic frame of the original, where the state is the state of the 30 Hz step: the frame a unit is outside the
    /// building's box for the first time, the frame all three stand, and where they stand.
    /// </summary>
    private static ImmutableArray<RateQuantity> MeasureProductionExit(RateScene scene, string factoryName, string unitName)
    {
        const int Units = 3;
        var quantities = new RateQuantities(scene);
        var factory = scene.Spawn(factoryName, 1, 2000, 2000);
        scene.GiveMoney(1, 50000);
        scene.StepFrames(1);
        scene.Align();
        var start = scene.World.Tick;
        for (var index = 0; index < Units; index++) scene.Order(1, (owner, sequence, now) => new QueueProductionCommand(owner, sequence, now, factory.Id, unitName));
        var major = factory.Template.GeometryMajorRadius;
        var minor = factory.Template.GeometryMinorRadius;
        var cos = FixedTrig.Cos(factory.Angle);
        var sin = FixedTrig.Sin(factory.Angle);
        bool Outside(Entity unit)
        {
            var dx = unit.X - factory.X;
            var dy = unit.Y - factory.Y;
            return (dx * cos + dy * sin).Abs() > major || (dy * cos - dx * sin).Abs() > minor;
        }
        var seen = new List<Entity>();
        var outside = new long[Units];
        Array.Fill(outside, -1);
        long settled = -1;
        var budget = RateScene.Seconds(240);
        for (long frame = 0; frame < budget && settled < 0; frame++)
        {
            scene.StepFrames(1);
            foreach (var entity in scene.World.Entities)
                if (entity.SpawnTick >= start && entity.Owner == factory.Owner && entity.Template.Name == unitName && !seen.Contains(entity)) seen.Add(entity);
            for (var index = 0; index < seen.Count && index < Units; index++)
                if (outside[index] < 0 && Outside(seen[index])) outside[index] = scene.Time.OriginalFrames(frame);
            if (seen.Count >= Units && seen.All(unit => !unit.HasMoveTarget && Outside(unit))) settled = scene.Time.OriginalFrames(frame);
        }
        for (var index = 0; index < Units; index++) quantities.Event($"order to unit {index + 1} outside the footprint", outside[index]);
        quantities.Event("order to all units at rest outside", settled);
        for (var index = 0; index < Units; index++)
            quantities.Distance($"unit {index + 1} rest distance from the factory", index < seen.Count ? RateMeasure.Distance(seen[index], factory) : Fixed.Zero, settled >= 0);
        quantities.Distance("rest distance unit 1 to unit 3", seen.Count >= Units ? RateMeasure.Distance(seen[0], seen[Units - 1]) : Fixed.Zero, settled >= 0);
        quantities.Distance("rest distance unit 2 to unit 3", seen.Count >= Units ? RateMeasure.Distance(seen[1], seen[Units - 1]) : Fixed.Zero, settled >= 0);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureUpgrade(RateScene scene, string structureName, string upgrade)
    {
        var quantities = new RateQuantities(scene);
        var structure = scene.Spawn(structureName, 1, 2000, 2000);
        scene.GiveMoney(1, 50000);
        scene.StepFrames(1);
        scene.Align();
        var start = scene.World.Tick;
        var accepted = scene.Send(1, (owner, sequence, now) => new PurchaseUpgradeCommand(owner, sequence, now, upgrade, structure.Id));
        if (!accepted.Accepted) accepted = scene.Send(1, (owner, sequence, now) => new PurchaseUpgradeCommand(owner, sequence, now, upgrade, null));
        if (!accepted.Accepted) throw new InvalidOperationException($"The upgrade {upgrade} was rejected: {accepted.Reason}.");
        long completed = -1;
        scene.StepUntil(result =>
        {
            foreach (var item in result.Events)
                if (item.Kind == SimEventKind.UpgradeCompleted && item.Text == upgrade) completed = item.Tick - start;
            return completed >= 0;
        }, RateScene.Seconds(240));
        quantities.Exact("purchase to upgrade completed", completed);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureSale(RateScene scene, string structureName)
    {
        var quantities = new RateQuantities(scene);
        var structure = scene.Spawn(structureName, 1, 2000, 2000);
        scene.StepFrames(RateScene.Seconds(1));
        scene.Align();
        var start = scene.World.Tick;
        var before = scene.World.MoneyOf(scene.One);
        scene.Order(1, (owner, sequence, now) => new SellCommand(owner, sequence, now, structure.Id));
        long sold = -1;
        scene.StepUntil(result =>
        {
            sold = FirstEvent(result, SimEventKind.StructureSold, structure.Id);
            return sold >= 0;
        }, RateScene.Seconds(60));
        quantities.Exact("order to refund", sold >= 0 ? sold - start : -1);
        quantities.Amount("refund", scene.World.MoneyOf(scene.One) - before, sold >= 0);
        return quantities.Done();
    }

    private static Entity SpawnNeutral(RateScene scene, string template, int x, int y)
    {
        var tick = scene.World.Tick;
        scene.Order(0, (_, sequence, now) => new SpawnCommand(PlayerId.System, sequence, now, template, PlayerId.System, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero));
        scene.StepFrames(1);
        return scene.Find(template, tick) ?? throw new InvalidOperationException($"{template} did not appear.");
    }

    /// <summary>One carrier between a warehouse and a supply center 400 units apart, for five minutes.</summary>
    private static ImmutableArray<RateQuantity> MeasureSupply(RateScene scene, string centerName, string carrierName, string warehouseName)
    {
        var quantities = new RateQuantities(scene);
        var center = scene.Spawn(centerName, 1, 2000, 2000);
        var warehouse = SpawnNeutral(scene, warehouseName, 2400, 2000);
        var carrier = scene.Spawn(carrierName, 1, 2150, 2100);
        scene.StepFrames(RateScene.Seconds(2));
        scene.Align();
        var start = scene.World.Tick;
        var before = scene.World.MoneyOf(scene.One);
        scene.Order(1, (owner, sequence, now) => new DockCommand(owner, sequence, now, carrier.Id, warehouse.Id));
        var deposits = new List<long>();
        var steps = scene.Time.WholeSecondsToTicks(300);
        for (long step = 1; step <= steps; step++)
            foreach (var item in scene.Step().Events)
                if (item.Kind == SimEventKind.SupplyDeposited && item.Entity == carrier.Id) deposits.Add(item.Tick - start);
        quantities.Event("order to first deposit", deposits.Count > 0 ? deposits[0] : -1);
        quantities.Event("round trip (first to second deposit)", deposits.Count > 1 ? deposits[1] - deposits[0] : -1);
        quantities.Event("fifth deposit", deposits.Count > 4 ? deposits[4] : -1);
        quantities.Count("deposits in 5 min", deposits.Count);
        quantities.Amount("income in 5 min", scene.World.MoneyOf(scene.One) - before);
        return quantities.Done();
    }

    /// <summary>An object that pays by itself (black market, oil derrick) or on its own order (hacker), for five minutes.</summary>
    private static ImmutableArray<RateQuantity> MeasureIncome(RateScene scene, string sourceName)
    {
        var quantities = new RateQuantities(scene);
        var source = scene.Spawn(sourceName, 1, 2000, 2000);
        scene.Align();
        var start = scene.World.Tick;
        var before = scene.World.MoneyOf(scene.One);
        if (source.Modules.OfType<HackInternetAIUpdate>().FirstOrDefault() is { } hacker) hacker.StartHacking(scene.World);
        var payments = new List<long>();
        var money = before;
        var steps = scene.Time.WholeSecondsToTicks(300);
        for (long step = 1; step <= steps; step++)
        {
            scene.Step();
            var now = scene.World.MoneyOf(scene.One);
            if (now > money) payments.Add(scene.World.Tick - TickOrder.Next - start);
            money = now;
        }
        quantities.Exact("first payment", payments.Count > 0 ? payments[0] : -1);
        quantities.Exact("second payment", payments.Count > 1 ? payments[1] : -1);
        quantities.Exact("tenth payment", payments.Count > 9 ? payments[9] : -1);
        quantities.Count("payments in 5 min", payments.Count);
        quantities.Amount("income in 5 min", money - before);
        return quantities.Done();
    }
}

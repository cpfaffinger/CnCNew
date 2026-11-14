using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// #337, <c>DozerActionPickActionPosState</c>, <c>DozerActionMoveToActionPosState</c>: a dozer that cannot reach its site never gives the task up. It picks a place on the circle of the
/// site's bounding sphere, goes there, and builds as soon as it is within <c>MIN_ACTION_TOLERANCE</c> (70) of that place - from where its approach ended, however far that is from the
/// site's centre; a move that ends farther away is picked again, for ever.
/// </summary>
static partial class Tests
{
    private static (SimulationWorld World, Feeder Feeder, uint[] Wall) DozerReachWorld(SimulationRate rate, int siteY)
    {
        var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1), "reach");
        var (world, feeder, _) = RatedWorld(rate, BuildSiteFiles(), terrain);
        world.RevealMapPermanently(new PlayerId(1), true);
        world.ExploreMap(new PlayerId(1));
        feeder.GiveMoney(1, 1000);
        feeder.Spawn(1, "Dozer", owner: 1, x: 300, y: 200);
        for (var tick = 0; tick < world.Frames(3); tick++) world.Step();
        // A wall of thin bars across the whole map closes the way; the site is the player's own choice (a computer player's order is asked for no path).
        world.SetValidatesOwnSites(new PlayerId(1));
        foreach (var x in new[] { 60, 300, 540 }) { var r = feeder.Spawn(0, "SiteThinBar", owner: 2, x: x, y: 300); Check.That(r.Accepted, $"wall {x}: {r.Reason}"); }
        for (var tick = 0; tick < world.Frames(3); tick++) world.Step();
        var dozer = world.Entities.First(value => value.Template.Name == "Dozer");
        Check.That(feeder.Construct(1, dozer.Id.Value, "Plant", 300, siteY).Accepted, $"{rate}: the order is accepted.");
        world.Step();
        return (world, feeder, world.Entities.Where(value => value.Template.Name == "SiteThinBar").Select(value => value.Id.Value).ToArray());
    }

    public static void DozerBuildsFromWhereItsApproachEnds()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var (world, _, _) = DozerReachWorld(rate, 335);
            for (var tick = 0; tick < world.Frames(30 * 30); tick++) world.Step();
            var plant = world.Entities.FirstOrDefault(value => value.Template.Name == "Plant");
            var dozer = world.Entities.First(value => value.Template.Name == "Dozer");
            Check.That(plant is not null && !IsSite(plant), $"{rate}: the structure is built although the dozer stands at the wall, {plant?.Y.ToDecimal() - dozer.Y.ToDecimal()} units from its centre (site under construction: {(plant is null ? "none" : IsSite(plant).ToString())}, work {plant?.ConstructionWork}).");
        }
    }

    public static void DozerNeverGivesUpAnUnreachableSite()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, wall) = DozerReachWorld(rate, 480);
            for (var tick = 0; tick < world.Frames(60 * 30); tick++) world.Step();
            var plant = world.Entities.First(value => value.Template.Name == "Plant");
            Check.That(IsSite(plant) && plant.ConstructionWork == 0, $"{rate}: the site waits behind the wall.");
            foreach (var id in wall) feeder.Kill(id);
            for (var tick = 0; tick < world.Frames(60 * 30); tick++) world.Step();
            plant = world.Entities.First(value => value.Template.Name == "Plant");
            Check.That(!IsSite(plant), $"{rate}: after a minute behind a wall the dozer builds when the wall is gone (work {plant.ConstructionWork}).");
        }
    }
    /// <summary>A real dozer or worker ordered to build on the far side of a wall of enemy gun emplacements, as close to it as the structure allows; true when the structure stands within a minute, and where the builder ended.</summary>
    private static (bool Built, double BuilderY, double SiteY) RealBuilderAtAWall(RealFireWorld data, SimulationRate rate, string builder, string center, string building)
    {
        var catalog = TemplateCatalog.Build(data.Graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "Builder"), new PlayerConfiguration(new PlayerId(2), "Wall"));
        var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "real-reach");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-reach", data.Fingerprint, terrain.Fingerprint), catalog, terrain);
        ulong sequence = 0;
        world.RevealMapPermanently(new PlayerId(1), true);
        world.SetValidatesOwnSites(new PlayerId(1));
        Check.That(world.Submit(new GiveMoneyCommand(PlayerId.System, ++sequence, world.Tick, new PlayerId(1), Fixed.FromInt(10000))).Accepted, "Money is given.");
        SpawnReal(world, ref sequence, center, 1, 100, 100);
        SpawnReal(world, ref sequence, builder, 1, 600, 400);
        var worker = world.Entities.Last(value => value.Template.Name == builder);
        Check.That(catalog.TryGet("ChinaGattlingCannon", out var wall), "The wall template exists.");
        Check.That(catalog.TryGet(building, out var site), "The building template exists.");
        var half = Fixed.Max(wall!.GeometryMajorRadius, wall.GeometryMinorRadius);
        for (var x = 0; x <= 1200; x += 2 * (int)half.ToDecimal()) SpawnReal(world, ref sequence, "ChinaGattlingCannon", 2, x, 700, step: false);
        for (var tick = 0; tick < rate.Time.OriginalFrames(3); tick++) world.Step();
        var siteY = Fixed.FromInt(700) + half + site!.BoundingSphereRadius + Fixed.FromInt(4);
        Check.That(world.Submit(new ConstructCommand(new PlayerId(1), ++sequence, world.Tick, worker.Id, building, Fixed.FromInt(600), siteY, Fixed.Zero)).Accepted, $"{builder}: the order is accepted.");
        for (var tick = 0; tick < rate.Time.OriginalFrames(60 * 30); tick++) world.Step();
        var placed = world.Entities.FirstOrDefault(value => value.Template.Name == building && value.Owner.Value == 1);
        return (placed is not null && !placed.HasStatus(ObjectStatusBits.UnderConstruction), (double)worker.Y.ToDecimal(), (double)siteY.ToDecimal());
    }

    public static void RealBuildersBuildFromTheirWall(string directory)
    {
        var data = OpenRealFireData(directory);
        foreach (var (builder, center, building) in new[] { ("AmericaVehicleDozer", "AmericaCommandCenter", "AmericaPowerPlant"), ("ChinaVehicleDozer", "ChinaCommandCenter", "ChinaPowerPlant"), ("GLAInfantryWorker", "GLACommandCenter", "GLABarracks") })
            foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
            {
                var (built, y, siteY) = RealBuilderAtAWall(data, rate, builder, center, building);
                Console.WriteLine($"       {rate} {builder}: ends at y {y:F0}, the site's centre at {siteY:F0}, built: {built}");
                Check.That(built, $"{rate}: the {builder} builds {building} from where its approach ends (y {y:F0}, the site at {siteY:F0}).");
            }
    }
}

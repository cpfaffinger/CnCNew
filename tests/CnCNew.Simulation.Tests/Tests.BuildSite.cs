using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-18: a building site is legal by the rules of <c>BuildAssistant::isLocationLegalToBuild</c> and refused for their reasons.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> BuildSiteFiles() => new Dictionary<string, string>(ConstructionFiles())
    {
        ["Data/INI/GameData.ini"] = """
        GameData
          SellPercentage = 50%
          AllowedHeightVariationForBuilding = 10.0
          MinDistFromEdgeOfMapForBuild = 30.0
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder) BuildSiteWorld(SimulationRate rate, TerrainMap terrain)
    {
        var (world, feeder, _) = RatedWorld(rate, BuildSiteFiles(), terrain);
        world.RevealMapPermanently(new PlayerId(1), true);
        world.ExploreMap(new PlayerId(1));
        feeder.GiveMoney(1, 1000);
        feeder.Spawn(1, "Dozer", owner: 1, x: 100, y: 100);
        for (var tick = 0; tick < world.Frames(3); tick++) world.Step();
        return (world, feeder);
    }

    private static LegalBuildCode SiteCode(SimulationWorld world, int x, int y, decimal angle = 0m)
        => world.Templates.TryGet("Plant", out var plant) ? world.CheckBuildSite(new PlayerId(1), plant, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.FromDecimal(angle)) : throw new InvalidOperationException("No Plant.");

    /// <summary>
    /// The terrain rules. Off the map, closer to the edge than <c>MinDistFromEdgeOfMapForBuild</c>, on a cliff or on water: <c>LBC_RESTRICTED_TERRAIN</c>; a slope whose samples differ by more
    /// than <c>AllowedHeightVariationForBuilding</c>: <c>LBC_NOT_FLAT_ENOUGH</c>; a turned footprint is sampled turned. The command is refused for the same sites. Before, a slope and the edge
    /// were no reason, and the client had one text for all of it.
    /// </summary>
    public static void BuildSiteTerrainRules()
    {
        foreach (var (rate, _) in WorldRates)
        {
            // A hill rises from column 34 on by more than 6 units per cell, with a cliff at (10, 40) and flat ground elsewhere.
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, (x, _) => x >= 34 ? Math.Min(255, (x - 34) * 10) : 0, [(10, 40)]), "site");
            var (world, feeder) = BuildSiteWorld(rate, terrain);
            Check.That(SiteCode(world, 200, 200) == LegalBuildCode.Ok, $"{rate}: flat open ground is a site.");
            Check.That(SiteCode(world, 200, 25) == LegalBuildCode.RestrictedTerrain, $"{rate}: too close to the map's edge ({SiteCode(world, 200, 25)}).");
            Check.That(SiteCode(world, 590, 200) == LegalBuildCode.RestrictedTerrain, $"{rate}: too close to the far edge ({SiteCode(world, 590, 200)}).");
            Check.That(SiteCode(world, 5000, 60) == LegalBuildCode.RestrictedTerrain, $"{rate}: off the map.");
            Check.That(SiteCode(world, 95, 395) == LegalBuildCode.RestrictedTerrain, $"{rate}: on a cliff ({SiteCode(world, 95, 395)}).");
            Check.That(SiteCode(world, 450, 200) == LegalBuildCode.NotFlatEnough, $"{rate}: on the hill ({SiteCode(world, 450, 200)}).");
            Check.That(feeder.Construct(1, 1, "Plant", 450, 200).Reason == CommandRejection.InvalidPlacement, $"{rate}: the order is refused on the hill.");
            Check.That(feeder.Construct(1, 1, "Plant", 200, 25).Reason == CommandRejection.InvalidPlacement, $"{rate}: the order is refused at the edge.");
            Check.That(feeder.Construct(1, 1, "Plant", 200, 200).Accepted, $"{rate}: the order is accepted on flat ground.");
        }
    }
}

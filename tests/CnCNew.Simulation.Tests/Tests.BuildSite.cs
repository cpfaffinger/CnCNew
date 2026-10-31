using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
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
          SupplyBuildBorder = 20.0
        End
        """,
        ["Data/INI/Object/BuildSiteFixture.ini"] = """
        Object SiteDock
          KindOf = SELECTABLE STRUCTURE SUPPLY_SOURCE
          Geometry = BOX
          GeometryMajorRadius = 30.0
          GeometryMinorRadius = 30.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
        End
        Object SiteRock
          KindOf = SELECTABLE IMMOBILE
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
        End
        Object SiteShrub
          KindOf = SHRUBBERY
          Geometry = CYLINDER
          GeometryMajorRadius = 3.0
          GeometryHeight = 10.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        Object SiteFactory
          KindOf = SELECTABLE STRUCTURE IMMOBILE
          FactoryExitWidth = 60.0
          Geometry = BOX
          GeometryMajorRadius = 30.0
          GeometryMinorRadius = 30.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
        End
        Object SiteSpot
          KindOf = SELECTABLE STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
        End
        Object SiteCenter
          KindOf = SELECTABLE STRUCTURE CANNOT_BUILD_NEAR_SUPPLIES
          BuildCost = 100
          BuildTime = 1
          Geometry = BOX
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
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

    private static LegalBuildCode SiteCode(SimulationWorld world, int x, int y, decimal angle = 0m, string template = "Plant")
        => world.Templates.TryGet(template, out var site) ? world.CheckBuildSite(world.Entities.First(value => value.Template.Name == "Dozer"), site, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.FromDecimal(angle)) : throw new InvalidOperationException($"No {template}.");

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
            Check.That(SiteCode(world, 200, 200, template: "SiteSpot") == LegalBuildCode.Ok, $"{rate}: a small circle has no sample at three cells.");
            Check.That(SiteCode(world, 200, 25) == LegalBuildCode.RestrictedTerrain, $"{rate}: too close to the map's edge ({SiteCode(world, 200, 25)}).");
            Check.That(SiteCode(world, 590, 200) == LegalBuildCode.RestrictedTerrain, $"{rate}: too close to the far edge ({SiteCode(world, 590, 200)}).");
            Check.That(SiteCode(world, 5000, 60) == LegalBuildCode.RestrictedTerrain, $"{rate}: off the map.");
            Check.That(SiteCode(world, 106, 395) == LegalBuildCode.RestrictedTerrain, $"{rate}: over a cliff ({SiteCode(world, 106, 395)}).");
            Check.That(SiteCode(world, 95, 395) == LegalBuildCode.NoClearPath, $"{rate}: no way onto a cliff, which is asked first ({SiteCode(world, 95, 395)}).");
            Check.That(SiteCode(world, 450, 200) == LegalBuildCode.NotFlatEnough, $"{rate}: on the hill ({SiteCode(world, 450, 200)}).");
            Check.That(feeder.Construct(1, 1, "Plant", 450, 200).Reason == CommandRejection.InvalidPlacement, $"{rate}: the order is refused on the hill.");
            Check.That(feeder.Construct(1, 1, "Plant", 200, 25).Reason == CommandRejection.InvalidPlacement, $"{rate}: the order is refused at the edge.");
            Check.That(feeder.Construct(1, 1, "Plant", 200, 200).Accepted, $"{rate}: the order is accepted on flat ground.");
        }
    }

    /// <summary>
    /// A shipped supply dock is so large that its border and the computer player's bib push the first legal spot of a supply center some 14 cells out: the search walks its whole square in one
    /// think (<c>AIPlayer::buildBySupplies</c>). With the 400 checks of an ordinary placement it ended before the first legal spot, and no computer player collected anything.
    /// </summary>
    public static void RealComputerPlayersBuildBesideTheDock(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        var everyone = ReferenceMatchScenario.Participants(catalog, out _);
        var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { MaxTicks = catalog.Rate.Time.WholeSecondsToTicks(150) }, catalog, fingerprint, TerrainMap.Build(map, MapPath), map.Objects,
            [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica"))]);
        while (!session.IsOver) session.Step();
        var built = session.World.Entities.Where(entity => entity.Template.Name is "ChinaSupplyCenter" or "AmericaSupplyCenter").Select(entity => entity.Template.Name).Distinct().Count();
        Check.That(built == 2, $"Both computer players have raised a supply center within 150 s ({built}).");
    }

    /// <summary>
    /// The objects under a site. An immobile object, a disabled unit and an enemy are in the way; shrubbery is removed when the building is begun, and the builder's own units walk off to a place
    /// around the site; a factory keeps the way out of its door free. The same draws and places at 30, 60 and 120 Hz. Before, only the footprints of structures counted: an enemy soldier stood in the
    /// building, a unit in the footprint stayed there and a building could close a factory's door.
    /// </summary>
    public static void BuildSiteClearsItsGround()
    {
        string? reference = null;
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder) = BuildSiteWorld(rate, TerrainMap.Build(SyntheticMaps.Build(60, 60, 1), "site"));
            feeder.Spawn(1, "SiteRock", owner: 0, x: 300, y: 100);
            feeder.Spawn(1, "SiteShrub", owner: 0, x: 400, y: 100);
            feeder.Spawn(1, "SiteFactory", owner: 1, x: 300, y: 300);
            feeder.Spawn(2, "Grunt", owner: 2, x: 200, y: 400);
            feeder.Spawn(1, "Grunt", owner: 1, x: 300, y: 450);
            world.Step();
            Check.That(SiteCode(world, 300, 100) == LegalBuildCode.ObjectsInTheWay, $"{rate}: an immobile object ({SiteCode(world, 300, 100)}).");
            Check.That(SiteCode(world, 200, 400) == LegalBuildCode.ObjectsInTheWay, $"{rate}: an enemy ({SiteCode(world, 200, 400)}).");
            Check.That(SiteCode(world, 400, 100) == LegalBuildCode.Ok, $"{rate}: shrubbery is no obstacle ({SiteCode(world, 400, 100)}).");
            Check.That(SiteCode(world, 300, 450) == LegalBuildCode.Ok, $"{rate}: the builder's own unit makes room ({SiteCode(world, 300, 450)}).");
            // The factory's exit lies in front of it, along its heading (+x): 330 to 390, as wide as the factory's minor radius.
            Check.That(SiteCode(world, 365, 300) == LegalBuildCode.ObjectsInTheWay, $"{rate}: the way out of the factory ({SiteCode(world, 365, 300)}).");
            Check.That(SiteCode(world, 235, 300) == LegalBuildCode.ObjectsInTheWay == false, $"{rate}: behind the factory ({SiteCode(world, 235, 300)}).");
            var mine = world.Entities.Single(value => value.Template.Name == "Grunt" && value.Owner.Value == 1);
            world.SetScriptDisable(mine, Entity.ScriptDisabled, true);
            Check.That(SiteCode(world, 300, 450) == LegalBuildCode.ObjectsInTheWay, $"{rate}: a disabled unit cannot make room ({SiteCode(world, 300, 450)}).");
            world.SetScriptDisable(mine, Entity.ScriptDisabled, false);

            feeder.Spawn(1, "Dozer", owner: 1, x: 420, y: 140);
            world.Step();
            var second = world.Entities.Last(value => value.Template.Name == "Dozer").Id.Value;
            Check.That(feeder.Construct(1, second, "Plant", 400, 100).Accepted, $"{rate}: the order on the shrub is accepted.");
            world.Step();
            world.Step();
            Check.That(world.Entities.All(value => value.Template.Name != "SiteShrub"), $"{rate}: the shrub is gone.");
            Check.That(feeder.Construct(1, 1, "Plant", 300, 450).Accepted, $"{rate}: the order is accepted.");
            for (var tick = 0; tick < world.Frames(120); tick++) world.Step();
            Check.That(!mine.HasMoveTarget, $"{rate}: the soldier has arrived.");
            var (dx, dy) = (mine.X.Raw - Fixed.FromInt(300).Raw, mine.Y.Raw - Fixed.FromInt(450).Raw);
            Check.That(Math.Abs(dx) > Fixed.FromInt(10).Raw || Math.Abs(dy) > Fixed.FromInt(10).Raw, $"{rate}: the soldier has left the footprint.");
            var line = $"{mine.X.Raw},{mine.Y.Raw}";
            reference ??= line;
            Check.That(line == reference, $"{rate}: the soldier stands where it stands at 30 Hz ({line} against {reference}).");
        }
    }

    /// <summary>
    /// The shroud and the way. A place the player has seen but does not look at now is no site for a player (<c>CELLSHROUD_FOGGED</c> is not <c>CELLSHROUD_CLEAR</c>), and ground the builder cannot
    /// reach is none either. A computer player's order is asked for neither. Before, explored ground was enough and a site behind a wall of cliffs was legal.
    /// </summary>
    public static void BuildSiteNeedsSightAndAWay()
    {
        foreach (var (rate, _) in WorldRates)
        {
            // A wall of cliffs along column 30.
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, cliffs: Enumerable.Range(0, 60).Select(cy => (31, cy + 1))), "site");
            var (world, feeder, _) = RatedWorld(rate, BuildSiteFiles(), terrain);
            feeder.GiveMoney(1, 1000);
            feeder.Spawn(1, "Dozer", owner: 1, x: 100, y: 100);
            for (var tick = 0; tick < world.Frames(3); tick++) world.Step();
            Check.That(SiteCode(world, 100, 500) == LegalBuildCode.Shroud, $"{rate}: ground nobody has seen ({SiteCode(world, 100, 500)}).");
            world.ExploreMap(new PlayerId(1));
            Check.That(SiteCode(world, 100, 500) == LegalBuildCode.Shroud, $"{rate}: fogged ground ({SiteCode(world, 100, 500)}).");
            world.RevealMapPermanently(new PlayerId(1), true);
            for (var tick = 0; tick < world.Frames(3); tick++) world.Step();
            Check.That(SiteCode(world, 100, 500) == LegalBuildCode.Ok, $"{rate}: ground the player looks at ({SiteCode(world, 100, 500)}).");
            Check.That(SiteCode(world, 500, 100) == LegalBuildCode.NoClearPath, $"{rate}: behind the cliffs ({SiteCode(world, 500, 100)}).");
        }
    }

    /// <summary>
    /// <c>CANNOT_BUILD_NEAR_SUPPLIES</c>: a supply center may not touch a supply source's footprint grown by <c>SupplyBuildBorder</c>; other buildings may, and so may a supply center beyond the
    /// border. A turned site is measured turned. Before, a supply center could stand against the supplies.
    /// </summary>
    public static void BuildSiteKeepsItsDistanceToSupplies()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder) = BuildSiteWorld(rate, TerrainMap.Build(SyntheticMaps.Build(60, 60, 1), "site"));
            feeder.Spawn(1, "SiteDock", owner: 0, x: 300, y: 300);
            world.Step();
            // The dock reaches to 330, its border to 350: a center of radius 20 touches it up to 370.
            Check.That(SiteCode(world, 365, 300, template: "SiteCenter") == LegalBuildCode.TooCloseToSupplies, $"{rate}: inside the border ({SiteCode(world, 365, 300, template: "SiteCenter")}).");
            Check.That(SiteCode(world, 375, 300, template: "SiteCenter") == LegalBuildCode.Ok, $"{rate}: beyond the border ({SiteCode(world, 375, 300, template: "SiteCenter")}).");
            Check.That(SiteCode(world, 365, 300) == LegalBuildCode.Ok, $"{rate}: any other building may stand there ({SiteCode(world, 365, 300)}).");
            // Turned by 45 degrees the center's corner reaches 28 towards the dock: it touches the border up to 378.
            Check.That(SiteCode(world, 375, 300, 0.7854m, "SiteCenter") == LegalBuildCode.TooCloseToSupplies, $"{rate}: a turned site is measured turned ({SiteCode(world, 375, 300, 0.7854m, "SiteCenter")}).");
        }
    }
}

using System.Collections.Immutable;

namespace CnCNew.Game.Live;

/// <summary>One production building of a factory-exit scene: where it stands (metres east/north of the scenario origin) and what it builds.</summary>
public sealed record FactoryExitSite(string Building, string Tag, string Template, string Produces, float X, float Y);

/// <summary>
/// The staged scenes that show how freshly built units leave their factory (<c>scripts/demo-factory-exits.ps1</c>, <c>PresentationTool scenario-audit</c>): per faction the command centre
/// builds dozers or workers, the barracks infantry and the war factory tanks, three orders each, one after another, so the exit can be watched repeatedly. A scene shows one building
/// (<c>exit-america-warfactory</c>) or all three of a faction (<c>exit-america-all</c>, which also places a builder, a power plant and a supply centre behind them). Every building keeps its default heading: the exit
/// (<c>UnitCreatePoint</c> to <c>NaturalRallyPoint</c>) points east, to the right of the default camera.
/// </summary>
public static class FactoryExits
{
    /// <summary>How many units every building of a scene is told to build.</summary>
    public const int OrdersPerBuilding = 3;

    public static ImmutableArray<string> Factions { get; } = ["america", "china", "gla"];

    public static ImmutableArray<string> Buildings { get; } = ["commandcenter", "barracks", "warfactory"];

    private static readonly ImmutableDictionary<string, (string Faction, string Builder, ImmutableArray<string> Support, ImmutableArray<FactoryExitSite> Sites)> Table =
        new Dictionary<string, (string, string, ImmutableArray<string>, ImmutableArray<FactoryExitSite>)>(StringComparer.OrdinalIgnoreCase)
        {
            ["america"] = ("FactionAmerica", "AmericaVehicleDozer", ["AmericaPowerPlant", "AmericaSupplyCenter"],
            [
                new("commandcenter", "commandcenter", "AmericaCommandCenter", "AmericaVehicleDozer", 0, 0),
                new("barracks", "barracks", "AmericaBarracks", "AmericaInfantryRanger", 0, -240),
                new("warfactory", "warfactory", "AmericaWarFactory", "AmericaTankCrusader", 0, 240),
            ]),
            ["china"] = ("FactionChina", "ChinaVehicleDozer", ["ChinaPowerPlant", "ChinaSupplyCenter"],
            [
                new("commandcenter", "commandcenter", "ChinaCommandCenter", "ChinaVehicleDozer", 0, 0),
                new("barracks", "barracks", "ChinaBarracks", "ChinaInfantryRedguard", 0, -240),
                new("warfactory", "warfactory", "ChinaWarFactory", "ChinaTankBattleMaster", 0, 240),
            ]),
            ["gla"] = ("FactionGLA", "GLAInfantryWorker", ["GLASupplyStash"],
            [
                new("commandcenter", "commandcenter", "GLACommandCenter", "GLAInfantryWorker", 0, 0),
                new("barracks", "barracks", "GLABarracks", "GLAInfantryRebel", 0, -240),
                new("warfactory", "warfactory", "GLAArmsDealer", "GLATankScorpion", 0, 240),
            ]),
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    public static ImmutableArray<DemoScenario> All { get; } =
        [..Factions.SelectMany(faction => Buildings.Append("all").Select(building => Build(faction, building)))];

    /// <summary>The scene name for a faction (<c>america</c>, <c>china</c>, <c>gla</c>) and a building (<c>commandcenter</c>, <c>barracks</c>, <c>warfactory</c>, <c>all</c>).</summary>
    public static string NameOf(string faction, string building) => $"exit-{faction.ToLowerInvariant()}-{building.ToLowerInvariant()}";

    /// <summary>The buildings of a factory-exit scene, or empty for any other scenario.</summary>
    public static ImmutableArray<FactoryExitSite> SitesOf(DemoScenario scenario)
    {
        var parts = scenario.Name.Split('-');
        if (parts.Length != 3 || parts[0] != "exit" || !Table.TryGetValue(parts[1], out var entry)) return [];
        return parts[2] == "all" ? entry.Sites : [..entry.Sites.Where(site => site.Building == parts[2])];
    }

    /// <summary>The tag of the <paramref name="number"/>-th unit (from 1) a site is told to build.</summary>
    public static string UnitTag(FactoryExitSite site, int number) => $"{site.Tag}-unit{number}";

    private static DemoScenario Build(string faction, string building)
    {
        var (side, builder, support, allSites) = Table[faction];
        var all = building == "all";
        // A single building stands at the origin, so the camera of every single scene is the same close look at the exit side.
        ImmutableArray<FactoryExitSite> sites = all ? allSites : [allSites.Single(site => site.Building == building) with { X = 0, Y = 0 }];
        var units = sites.Select(site => new ScenarioUnit(site.Tag, site.Template, 1, site.X, site.Y, 0f)).ToList();
        if (all)
        {
            // The whole base: a builder, and behind the factories what unlocks most of their buttons (power, supplies), so the construction menu can be tried, too.
            units.Add(new ScenarioUnit("builder", builder, 1, -150, -120, 0f));
            for (var index = 0; index < support.Length; index++) units.Add(new ScenarioUnit($"support{index + 1}", support[index], 1, -300, 130 - 260 * index, 0f));
        }
        var orders = new List<ScenarioOrder>();
        foreach (var site in sites)
            for (var number = 1; number <= OrdersPerBuilding; number++)
                orders.Add(new ScenarioOrder(30, site.Tag, ScenarioOrderKind.Produce, UnitTag(site, number), Template: site.Produces));
        var what = string.Join(", ", sites.Select(site => $"{site.Template} baut {OrdersPerBuilding}-mal {site.Produces}"));
        return new DemoScenario(
            NameOf(faction, building), all ? $"Fabrikausfahrten {side}" : $"Fabrikausfahrt {sites[0].Template}",
            $"{what}. Zu sehen: wo die Einheit erscheint, ob das Tor vorher offen ist, ob sie durch die Torseite (rechts im Bild) hinausfährt und wo sie stehen bleibt.",
            all ? 1800 : 1500, [..units], [..orders],
            all ? 40 : 45, 0, 0f, all ? 50f : 40f, all ? 560f : 230f)
        {
            StartMoney = 30000,
            HumanFaction = side,
            OpponentFaction = side == "FactionChina" ? "FactionAmerica" : "FactionChina",
        };
    }
}

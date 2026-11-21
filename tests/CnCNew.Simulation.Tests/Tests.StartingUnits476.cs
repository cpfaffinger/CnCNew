using CnCNew.Content;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// #476: where a player's starting units stand.
static partial class Tests
{
    private const string AlpineAssault476 = "Maps/Alpine Assault/Alpine Assault.map";

    /// <summary>
    /// #476 (the owner's playtest): the starting units stood on an evenly spaced ring at the command centre's bounding circle plus 30 around the start. <c>GameLogic.cpp
    /// placeNetworkBuildingsForPlayer</c> moves the start half the building's bounding sphere along -y, takes the map's <c>Player_N_Rally</c> waypoint instead when there is one (no shipped skirmish map has one; a user's map may), and
    /// places every starting unit where <c>PartitionManager::findPositionAround</c> finds room 0.7 to 1.3 times that sphere around it, from a random start angle of the logic stream.
    /// America against GLA on Alpine Assault with a rally waypoint added beside each start and on Alpine Assault as it is: the patch's starting object of each player stands in that band
    /// around the rally waypoint or the moved start, and the builder its creation list makes stands where it stood. At every rate, twice alike; and a search with no room - inside the
    /// command centre, and thirty more units around a rally point - makes nothing where there is none and throws nothing.
    /// </summary>
    public static void RealStartingUnitsStandAroundTheRallyPoint(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var source = RealGraph(directory);
        MapData alpine;
        using (var stream = content.OpenRead(AlpineAssault476)) alpine = SageMapDecoder.Decode(stream);
        var plain = TerrainMap.Build(alpine, AlpineAssault476);
        Check.That(FindWaypoint476(plain, "Player_1_Rally") is null && FindWaypoint476(plain, "Player_2_Rally") is null, "Alpine Assault has no rally waypoint.");
        // No shipped skirmish map names a Player_N_Rally waypoint (a user's map may): Alpine Assault with one for each start, 150 units towards the map's middle.
        var rallied = WithRallyPoints(alpine, plain);
        var maps = new[] { ("rally", rallied), ("plain", alpine) };
        foreach (var (rate, _) in WorldRates)
        {
            var catalog = TemplateCatalog.Build(source.Graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            foreach (var (kind, map) in maps)
            {
                var first = StartingPlaces(map, catalog, source.Fingerprint, $"{rate} {kind}");
                var second = StartingPlaces(map, catalog, source.Fingerprint, $"{rate} {kind}");
                Check.That(first.SequenceEqual(second), $"{rate} {kind}: two runs place alike ({string.Join("; ", first)} / {string.Join("; ", second)}).");
            }
            NoRoomMakesNothing(rallied, catalog, source.Fingerprint, $"{rate} rally");
        }
    }

    /// <summary>The map with a <c>Player_N_Rally</c> waypoint beside each <c>Player_N_Start</c>, 150 units towards the middle of the map.</summary>
    private static MapData WithRallyPoints(MapData map, TerrainMap terrain)
    {
        var (middleX, middleY) = (terrain.ExtentX.ToDecimal() / 2, terrain.ExtentY.ToDecimal() / 2);
        var added = new List<MapObject>();
        var id = 9_000;
        foreach (var start in map.Objects.Where(value => value.IsWaypoint && value.Properties.GetText("waypointName") is { } name && TerrainMap.TryParseStart(name, out _)))
        {
            var (dx, dy) = ((double)(middleX - start.X), (double)(middleY - start.Y));
            var length = Math.Sqrt(dx * dx + dy * dy);
            var name = start.Properties.GetText("waypointName")!.Replace("_Start", "_Rally", StringComparison.Ordinal);
            var properties = new MapProperties([..start.Properties.Items.Select(item => item.Key switch
            {
                "waypointName" => item with { Text = name },
                "waypointID" => item with { Integer = id++ },
                _ => item,
            })]);
            added.Add(start with { X = start.X + (decimal)(dx / length * 150), Y = start.Y + (decimal)(dy / length * 150), Properties = properties });
        }
        return map with { Objects = [..map.Objects, ..added] };
    }

    private static TerrainWaypoint? FindWaypoint476(TerrainMap terrain, string name) => terrain.Waypoints.FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.Ordinal));

    private static SkirmishMatchSession StartAmericaAgainstGla(MapData map, TemplateCatalog catalog, string fingerprint)
    {
        var everyone = ReferenceMatchScenario.Participants(catalog, out _);
        return SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate), catalog, fingerprint, TerrainMap.Build(map, AlpineAssault476), map.Objects,
            [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA"))]);
    }

    /// <summary>The original's centre and band for the player at start position <paramref name="index"/>, written out here, not taken from the port's helper.</summary>
    private static (decimal X, decimal Y, decimal Min, decimal Max, bool Rally) ExpectedBand(TerrainMap terrain, int index, ObjectTemplate building)
    {
        var start = terrain.StartPositions[index];
        var radius = building.BoundingSphereRadius.ToDecimal();
        var rally = FindWaypoint476(terrain, start.Name.Replace("_Start", "_Rally", StringComparison.Ordinal));
        var (x, y) = rally is { } point ? (point.X.ToDecimal(), point.Y.ToDecimal()) : (start.X.ToDecimal(), start.Y.ToDecimal() - radius / 2);
        return (x, y, radius * 0.7m, radius * 1.3m, rally is not null);
    }

    private static decimal Distance476(decimal ax, decimal ay, decimal bx, decimal by)
    {
        var (dx, dy) = ((double)(ax - bx), (double)(ay - by));
        return (decimal)Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Starts the match, checks the band of each player's starting object and its builder, and returns where they stood.</summary>
    private static List<string> StartingPlaces(MapData map, TemplateCatalog catalog, string fingerprint, string label)
    {
        var session = StartAmericaAgainstGla(map, catalog, fingerprint);
        var terrain = session.World.Terrain!;
        session.Step();
        var world = session.World;
        var places = new List<string>();
        var builders = new Dictionary<PlayerId, (decimal X, decimal Y)>();
        foreach (var slot in session.Match.Slots)
        {
            var participant = slot.Participant;
            Check.That(catalog.TryGet(participant.BaseBuilding, out var building), $"{label}: no template {participant.BaseBuilding}.");
            var band = ExpectedBand(terrain, slot.StartPosition, building);
            var things = world.Entities.Where(entity => entity.Owner == slot.Player && participant.Faction.StartingUnits.Contains(entity.Template.Name)).ToList();
            Check.That(things.Count == participant.Faction.StartingUnits.Length && things.Count > 0,
                $"{label}: {things.Count} of {participant.Faction.StartingUnits.Length} starting units of {participant.Faction.Name}.");
            foreach (var thing in things)
            {
                var distance = Distance476(thing.X.ToDecimal(), thing.Y.ToDecimal(), band.X, band.Y);
                // findPositionAround's rings are five units apart from 0.7 R up to at most 1.3 R; the fixed-point cosine leaves a fraction of a unit.
                Check.That(distance >= band.Min - 0.01m && distance <= band.Max + 0.01m,
                    $"{label}: {thing.Template.Name} of {participant.Faction.Name} stands {distance:0.00} from the {(band.Rally ? "rally waypoint" : "moved start")} ({band.X:0.0}, {band.Y:0.0}); the band is {band.Min:0.00} to {band.Max:0.00}.");
                places.Add($"{thing.Template.Name}@{thing.X.Raw},{thing.Y.Raw}");
                builders[slot.Player] = (thing.X.ToDecimal(), thing.Y.ToDecimal());
            }
        }
        // The patch's starting object makes the builder with its OCLUpdate and goes (it lives two frames): the builder stands where it stood.
        for (var tick = 0; tick < catalog.Rate.Time.OriginalFrames(10); tick++) session.Step();
        foreach (var slot in session.Match.Slots)
        {
            var builder = world.Entities.Single(entity => entity.Owner == slot.Player && entity.Template.Name is "AmericaVehicleDozer" or "GLAInfantryWorker");
            var (x, y) = builders[slot.Player];
            var off = Distance476(builder.X.ToDecimal(), builder.Y.ToDecimal(), x, y);
            Check.That(off < 1m, $"{label}: the builder {builder.Template.Name} stands {off:0.00} from where its starting object stood.");
        }
        return places;
    }

    /// <summary>A search with no room makes nothing and throws nothing: inside the command centre, and thirty more tanks around the first player's rally point.</summary>
    private static void NoRoomMakesNothing(MapData map, TemplateCatalog catalog, string fingerprint, string label)
    {
        var session = StartAmericaAgainstGla(map, catalog, fingerprint);
        session.Step();
        var world = session.World;
        var slot = session.Match.Slots[0];
        Check.That(catalog.TryGet(slot.Participant.BaseBuilding, out var building), $"{label}: no template {slot.Participant.BaseBuilding}.");
        var centre = world.Entities.Single(entity => entity.Owner == slot.Player && entity.Template.Name == building.Name);
        var band = ExpectedBand(world.Terrain!, slot.StartPosition, building);
        var sequence = 1_000_000UL;
        int Tanks() => world.Entities.Count(entity => entity.Owner == slot.Player && entity.Template.Name == "AmericaTankCrusader");
        Check.That(world.Submit(new PlaceStartingUnitCommand(PlayerId.System, sequence++, world.Tick, "AmericaTankCrusader", slot.Player, centre.X, centre.Y, Fixed.Zero, Fixed.FromInt(10),
            Fixed.Zero)).Accepted, $"{label}: a placement inside the command centre is accepted.");
        world.Step();
        Check.That(Tanks() == 0, $"{label}: no room inside the command centre makes no tank ({Tanks()}).");
        var (min, max) = (Fixed.FromDecimal(band.Min), Fixed.FromDecimal(band.Max));
        for (var index = 0; index < 30; index++)
            Check.That(world.Submit(new PlaceStartingUnitCommand(PlayerId.System, sequence++, world.Tick, "AmericaTankCrusader", slot.Player, Fixed.FromDecimal(band.X), Fixed.FromDecimal(band.Y),
                min, max, Fixed.Zero)).Accepted, $"{label}: placement {index} is accepted.");
        world.Step();
        var tanks = world.Entities.Where(entity => entity.Owner == slot.Player && entity.Template.Name == "AmericaTankCrusader").ToList();
        Check.That(tanks.Count is > 0 and <= 30, $"{label}: {tanks.Count} of 30 crowded tanks found room.");
        foreach (var tank in tanks)
        {
            var distance = Distance476(tank.X.ToDecimal(), tank.Y.ToDecimal(), band.X, band.Y);
            Check.That(distance >= band.Min - 0.01m && distance <= band.Max + 0.01m, $"{label}: a crowded tank stands {distance:0.00} away, outside {band.Min:0.00} to {band.Max:0.00}.");
        }
        Check.That(!world.Check(new PlaceStartingUnitCommand(PlayerId.System, sequence, world.Tick, "AmericaTankCrusader", slot.Player, centre.X, centre.Y, Fixed.Zero,
            PlaceStartingUnitCommand.RadiusLimit + Fixed.One, Fixed.Zero)).Accepted, $"{label}: a radius beyond the limit is refused.");
        Check.That(!world.Check(new PlaceStartingUnitCommand(slot.Player, sequence, world.Tick, "AmericaTankCrusader", slot.Player, centre.X, centre.Y, Fixed.Zero, Fixed.One, Fixed.Zero)).Accepted,
            $"{label}: a player may not place a starting unit.");
    }
}

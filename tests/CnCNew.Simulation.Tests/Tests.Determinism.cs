using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Terrain;

/// <summary>Phase 18: determinism across machines (docs/DETERMINISM-FINDINGS.md). Every finding of the read-only search is confirmed or refuted here before it is fixed.</summary>
static partial class Tests
{
    /// <summary>
    /// The cultures the simulation must not notice: German (decimal comma), Turkish (dotless i), Arabic and Swedish (other minus signs, Swedish also sorts 'Ö' after 'Z'),
    /// and a made-up culture whose number format shares nothing with the invariant one.
    /// </summary>
    internal static ImmutableArray<CultureInfo> HostileCultures()
    {
        var hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        hostile.NumberFormat.NegativeSign = "~";
        hostile.NumberFormat.NumberDecimalSeparator = "#";
        hostile.NumberFormat.NumberGroupSeparator = "'";
        return [CultureInfo.GetCultureInfo("de-DE"), CultureInfo.GetCultureInfo("tr-TR"), CultureInfo.GetCultureInfo("ar-SA"), CultureInfo.GetCultureInfo("sv-SE"), hostile];
    }

    internal static T UnderCulture<T>(CultureInfo culture, Func<T> body)
    {
        var (current, ui, threadDefault, threadDefaultUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture, CultureInfo.DefaultThreadCurrentCulture, CultureInfo.DefaultThreadCurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
            CultureInfo.CurrentUICulture = ui;
            CultureInfo.DefaultThreadCurrentCulture = threadDefault;
            CultureInfo.DefaultThreadCurrentUICulture = threadDefaultUi;
        }
    }

    internal static string CultureLabel(CultureInfo culture) => culture.Name.Length > 0 ? culture.Name : "made-up";

    /// <summary>
    /// D4: the hashed script state held a culture-sorted table (production priorities by team name), a diagnosis with a culture-formatted number, and the forwarded
    /// presentation arguments formatted coordinates with the culture of the machine. The same mission must hash and report the same under every culture.
    /// </summary>
    public static void ScriptStateIsCultureIndependent()
    {
        (ulong Hash, string Outputs, string Diagnostics) Play()
        {
            var (map, fixture, terrain) = MissionMap([
                Scr.Script("Set", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                    // Two prototypes whose names sort differently by culture ('Ö' before 'Z' in German, after it in Swedish) and by code point.
                    Scr.Do("TEAM_INCREASE_PRIORITY", Scr.Team("Öland")),
                    Scr.Do("TEAM_INCREASE_PRIORITY", Scr.Team("Zulu")),
                    Scr.Do("TEAM_INCREASE_PRIORITY", Scr.Team("alpha")),
                    // A negative number inside a diagnosis: the minus sign is a property of the culture.
                    Scr.Do("TECHTREE_MODIFY_BUILDABILITY_OBJECT", Scr.Type("AlphaSoldier"), Scr.Int(-1)),
                    // A forwarded action with coordinates: the decimal separator is a property of the culture.
                    Scr.Do("SPEECH_PLAY", Scr.Coord(1.5m, -2.25m, 0m), Scr.Real(-0.5m), Scr.Int(-7))], oneShot: true),
            ], objects: [Placed("AlphaSoldier", 100m, 200m, "\u00D6land"), Placed("AlphaSoldier", 110m, 200m, "Zulu"), Placed("AlphaSoldier", 120m, 200m, "alpha")]);
            MapProperties Team(string name) => new([Scr.Text("teamName", name), Scr.Text("teamOwner", PlayerA), Scr.Flag("teamIsSingleton", true),
                new MapProperty("teamProductionPrioritySuccessIncrease", MapPropertyType.Int, 3, 0m, string.Empty)]);
            map = map with { Teams = [..map.Teams, Team("Öland"), Team("Zulu"), Team("alpha")] };
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            var outputs = new List<string>();
            for (var tick = 0; tick < 5; tick++)
            {
                session.Step();
                foreach (var output in session.Engine.DrainOutputs()) outputs.Add(output.Action + "(" + string.Join("|", output.Arguments) + ")");
            }
            Check.That(session.Program.FindTeam("Zulu") is { } zulu && session.Engine.ProductionPriorityOf(zulu) == zulu.ProductionPriority + 3, "The fixture changes the production priorities.");
            return (session.StateHash(), string.Join(";", outputs), string.Join(";", session.Engine.Diagnostics.Keys));
        }

        var reference = UnderCulture(CultureInfo.InvariantCulture, Play);
        Check.That(reference.Diagnostics.Contains("buildable status -1 out of range", StringComparison.Ordinal), $"The fixture produces the diagnosis with a negative number ({reference.Diagnostics}).");
        Check.That(reference.Outputs.Contains("SPEECH_PLAY(1.5,-2.25,0|-0.5|-7)", StringComparison.Ordinal), $"The fixture forwards coordinates and numbers in invariant notation ({reference.Outputs}).");
        var differing = new List<string>();
        foreach (var culture in HostileCultures())
        {
            var result = UnderCulture(culture, Play);
            if (result != reference)
                differing.Add($"{CultureLabel(culture)}: hash {(result.Hash == reference.Hash ? "equal" : "differs")}, outputs {(result.Outputs == reference.Outputs ? "equal" : result.Outputs)}, diagnostics {(result.Diagnostics == reference.Diagnostics ? "equal" : result.Diagnostics)}");
        }
        Check.That(differing.Count == 0, "The script state depends on the culture of the machine:\n         " + string.Join("\n         ", differing));
    }

    /// <summary>
    /// D7: waypoints were sorted by name, x and y only. Waypoints that share those (the World Builder lets a designer stack two of the same name) then came out in
    /// an order that depended on their order in the file and on the runtime's unstable sort. The same set of waypoints must give the same terrain.
    /// </summary>
    public static void WaypointOrderIsTotal()
    {
        // 48 stacked waypoints of one name (more than the insertion-sort threshold of Array.Sort) that differ only in height, plus a few distinct ones.
        var stacked = Enumerable.Range(0, 48).Select(index => ("Stack", 100m, 200m, (decimal)(index * 7 % 48))).ToArray();
        (string, decimal, decimal, decimal)[] others = [("Alpha", 10m, 10m, 0m), ("Zulu", 300m, 50m, 0m), ("Stack", 100m, 210m, 0m)];
        var forward = TerrainMap.Build(SyntheticMaps.Build(60, 40, 1, waypoints: [..others, ..stacked]), "stack");
        var backward = TerrainMap.Build(SyntheticMaps.Build(60, 40, 1, waypoints: [..stacked.Reverse(), ..others.Reverse()]), "stack");
        Check.That(forward.Waypoints.Length == 51 && backward.Waypoints.Length == 51, "Every waypoint is kept.");
        // The synthetic builder numbers the waypoints in file order; the comparison below leaves the id out because it differs between the two files by construction.
        var same = forward.Waypoints.Select(point => (point.Name, point.X, point.Y, point.Z)).SequenceEqual(backward.Waypoints.Select(point => (point.Name, point.X, point.Y, point.Z)));
        Check.That(same, "The same waypoints in another file order give another waypoint order: "
            + string.Join(" ", forward.Waypoints.Skip(1).Take(6).Select(point => point.Z.ToDecimal())) + " ... against " + string.Join(" ", backward.Waypoints.Skip(1).Take(6).Select(point => point.Z.ToDecimal())) + " ...");
        for (var index = 1; index < forward.Waypoints.Length; index++)
            Check.That(TerrainMap.CompareWaypoints(forward.Waypoints[index - 1], forward.Waypoints[index]) < 0, "The order is strict: no two waypoints tie.");
    }

    /// <summary>
    /// D3: the entity table was a dictionary. After objects died and others were created, a running world enumerated it in the order of the table's free slots,
    /// a world restored from a snapshot in id order. Every loop over the entities with a side effect that depends on the order (first match wins, effects in
    /// sequence) could then decide differently in the two worlds. The enumeration must be the same, and it must be the id order.
    /// </summary>
    public static void EntityEnumerationIsOrderedById()
    {
        var fixture = new SyntheticFixture();
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        for (var index = 0; index < 8; index++) feeder.Spawn(1, "Barrel");
        for (var tick = 0; tick < 3; tick++) world.Step();
        Check.That(world.Metrics.Entities == 8, "Fixture: eight barrels.");
        feeder.Destroy(2);
        feeder.Destroy(5);
        feeder.Destroy(6);
        for (var tick = 0; tick < 3; tick++) world.Step();
        for (var index = 0; index < 4; index++) feeder.Spawn(1, "Barrel");
        for (var tick = 0; tick < 3; tick++) world.Step();
        Check.That(world.Metrics.Entities == 9, $"Fixture: three barrels died, four were created ({world.Metrics.Entities}).");
        var live = world.Entities.Select(entity => entity.Id.Value).ToArray();
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint).Entities.Select(entity => entity.Id.Value).ToArray();
        Check.That(live.SequenceEqual(restored), $"A running world enumerates its entities as {string.Join(",", live)}, the same world restored from a snapshot as {string.Join(",", restored)}.");
        Check.That(live.SequenceEqual(live.Order()), $"The enumeration is the id order ({string.Join(",", live)}).");

        // Removal while enumerating stays legal (the dictionary allowed it): the removed entity is skipped, the rest is visited once, in order.
        var seen = new List<uint>();
        foreach (var entity in world.Entities)
        {
            seen.Add(entity.Id.Value);
            if (entity.Id.Value == 3) { feeder.Destroy(4); feeder.Destroy(1); }
        }
        Check.That(seen.SequenceEqual(live), "Requesting a destroy inside the loop does not disturb it.");
        for (var tick = 0; tick < 3; tick++) world.Step();
        Check.That(world.Entities.Select(entity => entity.Id.Value).SequenceEqual(live.Where(id => id is not (1 or 4))) && world.Entities.Count == 7 && world.FindEntity(new CnCNew.Simulation.Ids.EntityId(4)) is null,
            "Destroyed entities leave the enumeration and the lookup.");
    }

    /// <summary>
    /// D8: the tick hash is maintained incrementally; a state change without <c>MarkDirty</c> leaves a stale entity hash that no one notices until the object
    /// is marked for another reason. The tools verify against a full recomputation only every few thousand ticks. Here the stress scenario is verified in
    /// every tick, and the verified run must hash like the unverified one (verification refreshes the caches, so it must not be what makes a run correct).
    /// </summary>
    public static void IncrementalHashHoldsInEveryTick()
    {
        var fixture = new SyntheticFixture();
        var replay = new StressScenario(new StressOptions(6_000, 600, 0xC0FFEE01, 0x5EED0001, 1_500, 1_000)).Record(fixture);
        var plain = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        var verified = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true, VerifyIncrementalEvery = 1 });
        Check.That(plain.TicksExecuted >= 6_000 && plain.TickHashes.AsSpan().SequenceEqual(verified.TickHashes), "Verifying the incremental hash in every tick does not change a tick hash of the stress scenario.");
    }

    /// <summary>
    /// D8, found on <c>MD_GLA05</c> (the mission restored at tick 150 left the continuous run at tick 900): <c>WAREHOUSE_SET_VALUE</c> changed the stock of a
    /// supply dock and set the entity's dirty flag by hand instead of calling <c>MarkDirty</c>. The flag without the entry in the world's dirty list is worse
    /// than no flag: every later <c>MarkDirty</c> returns early, the entity's cached hash stays stale for good, and the tick hash only becomes right when
    /// something recomputes it in full - writing a snapshot does. A mission hashed differently depending on whether a save had been written.
    /// </summary>
    public static void ScriptedWarehouseValueReachesTheHash()
    {
        MissionSession Start() => StartMission([
            Scr.Script("Value", [Scr.And(Scr.If("FLAG", Scr.Flag("go"), Scr.Bool(true)))], [Scr.Do("WAREHOUSE_SET_VALUE", Scr.Unit("Store"), Scr.Int(250))], oneShot: true),
        ], objects: [Placed("ScriptWarehouse", 300m, 300m, "", "Store"), Placed("AlphaSoldier", 100m, 200m, "teamA", "S1")]);
        var plain = Start();
        var saved = Start();
        // The script fires on a quiet tick, long after the warehouse was created (a fresh entity is in the dirty list anyway).
        foreach (var session in new[] { plain, saved })
        {
            Run(session, 20);
            session.Engine.SignalUiInteraction("go");
            Run(session, 3);
        }
        Check.That(plain.World.WarehouseBoxes(NamedIn(plain, "Store")!.Id) == 3, "Fixture: the script changed the warehouse's stock.");
        // Writing a save must be an observation: the mission that was saved hashes like the one that was not.
        var bytes = MissionSnapshot.Write(saved);
        Check.That(saved.StateHash() == plain.StateHash(), "A mission hashes differently after a save was written: a cached entity hash was stale.");
        var restored = MissionSnapshot.Restore(bytes, saved.World.Templates, saved.World.Configuration.DefinitionFingerprint, saved.World.Terrain!, saved.Map);
        for (var tick = 0; tick < 30; tick++)
        {
            var expected = plain.Step().StateHash;
            Check.That(saved.Step().StateHash == expected && restored.Step().StateHash == expected, $"Saved, restored and undisturbed mission agree in tick {plain.World.Tick - 1}.");
            plain.World.VerifyIncrementalHash();
        }
    }

    /// <summary>
    /// D5: the terrain fingerprint is what a replay, a snapshot and (later) a joining machine compare to know they stand on the same map. It covered heights,
    /// cells, water and the names and positions of the waypoints, but neither the waypoints' ids and links (the paths units and scripts follow) nor the objects
    /// placed on the map (start bases, supply warehouses, scripted units). Two maps that differ in those must not share a fingerprint.
    /// </summary>
    public static void TerrainFingerprintCoversWaypointLinksAndMapObjects()
    {
        MapObject Waypoint(string name, decimal x, int id) => new("*Waypoints/Waypoint", x, 50m, 0m, 0m, 0,
            new MapProperties([new MapProperty("waypointID", MapPropertyType.Int, id, 0m, string.Empty), new MapProperty("waypointName", MapPropertyType.Ascii, 0, 0m, name)]));
        MapObject Placed(string template, decimal x, decimal angle, string owner) => new(template, x, 80m, 0m, angle, 0, new MapProperties([new MapProperty("originalOwner", MapPropertyType.Ascii, 0, 0m, owner)]));
        var basis = SyntheticMaps.Build(40, 30, 1) with
        {
            Objects = [Waypoint("A", 10m, 1), Waypoint("B", 20m, 2), Waypoint("C", 30m, 3), Placed("SupplyDock", 100m, 0m, "teamA")],
            WaypointLinks = [new MapWaypointLink(1, 2)],
        };
        string Print(MapData map) => TerrainMap.Build(map, "fingerprint").Fingerprint;
        var reference = Print(basis);
        Check.That(Print(basis with { Objects = [..basis.Objects] }) == reference, "The same map has the same fingerprint.");
        var variants = new (string What, MapData Map)[]
        {
            ("a waypoint link that leads elsewhere", basis with { WaypointLinks = [new MapWaypointLink(1, 3)] }),
            ("waypoint ids exchanged", basis with { Objects = [Waypoint("A", 10m, 2), Waypoint("B", 20m, 1), Waypoint("C", 30m, 3), basis.Objects[3]] }),
            ("a map object moved", basis with { Objects = [..basis.Objects.Take(3), Placed("SupplyDock", 110m, 0m, "teamA")] }),
            ("a map object turned", basis with { Objects = [..basis.Objects.Take(3), Placed("SupplyDock", 100m, 1.5m, "teamA")] }),
            ("a map object of another type", basis with { Objects = [..basis.Objects.Take(3), Placed("SupplyPile", 100m, 0m, "teamA")] }),
            ("a map object of another owner", basis with { Objects = [..basis.Objects.Take(3), Placed("SupplyDock", 100m, 0m, "teamB")] }),
            ("a map object more", basis with { Objects = [..basis.Objects, Placed("SupplyDock", 200m, 0m, "teamA")] }),
        };
        var unnoticed = variants.Where(variant => Print(variant.Map) == reference).Select(variant => variant.What).ToArray();
        Check.That(unnoticed.Length == 0, "The terrain fingerprint does not notice: " + string.Join("; ", unnoticed) + ".");
    }
}

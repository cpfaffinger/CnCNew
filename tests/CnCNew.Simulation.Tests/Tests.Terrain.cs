using System.Collections.Immutable;
using System.Linq;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>Builds small synthetic <see cref="MapData"/> instances (no original map content).</summary>
static class SyntheticMaps
{
    /// <summary>
    /// A <paramref name="cellsX"/> x <paramref name="cellsY"/> playable map with the given border. <paramref name="height"/> gives the raw height byte per
    /// vertex (x, y in whole-field coordinates); cliff cells are given in height-cell coordinates.
    /// </summary>
    public static MapData Build(int cellsX, int cellsY, int border, Func<int, int, int>? height = null, IEnumerable<(int X, int Y)>? cliffs = null,
        IEnumerable<MapPolygonTrigger>? triggers = null, IEnumerable<(string Name, decimal X, decimal Y, decimal Z)>? waypoints = null)
    {
        var width = cellsX + 2 * border;
        var fieldHeight = cellsY + 2 * border;
        var heights = new byte[width * fieldHeight];
        for (var y = 0; y < fieldHeight; y++)
            for (var x = 0; x < width; x++) heights[y * width + x] = (byte)(height?.Invoke(x, y) ?? 0);
        var stride = (width + 7) / 8;
        var flags = new byte[stride * fieldHeight];
        foreach (var (cx, cy) in cliffs ?? [])
            flags[cy * stride + (cx >> 3)] |= (byte)(1 << (cx & 7));
        var blend = new MapBlendTiles(8, [], [], [], [], [..flags], stride, 0, 0, [], [], [], []);
        var objects = ImmutableArray.CreateBuilder<MapObject>();
        var id = 1;
        foreach (var (name, x, y, z) in waypoints ?? [])
        {
            var properties = new MapProperties([
                new MapProperty("waypointID", MapPropertyType.Int, id++, 0m, string.Empty),
                new MapProperty("waypointName", MapPropertyType.Ascii, 0, 0m, name)]);
            objects.Add(new MapObject("*Waypoints/Waypoint", x, y, z, 0m, 0, properties));
        }
        return new MapData("synthetic", new MapHeightField(width, fieldHeight, border, [new MapBoundary(cellsX, cellsY)], [..heights]), blend,
            MapProperties.Empty, [], [], objects.ToImmutable(), [..triggers ?? []], [], [], []);
    }

    public static MapPolygonTrigger Water(string name, int level, params (int X, int Y)[] points)
        => new(name, string.Empty, 1, true, false, 0, [..points.Select(point => new MapPoint(point.X, point.Y, level))]);
}

static partial class Tests
{
    public static void TerrainHeightSampling()
    {
        // Border 1: world (0,0) is vertex (1,1). Heights ramp 8 units of 0.625 m per vertex along x.
        var map = SyntheticMaps.Build(4, 4, 1, (x, _) => x * 8);
        var terrain = TerrainMap.Build(map, "ramp");
        Check.That(terrain.CellsX == 4 && terrain.CellsY == 4, "Cell counts follow the boundary.");
        Check.That(terrain.ExtentX == Fixed.FromInt(40) && terrain.ExtentY == Fixed.FromInt(40), "Extent is cells times the cell size.");
        // Vertex (1,1) holds x*8 = 8 raw = 5.0 m.
        Check.That(terrain.HeightAt(Fixed.Zero, Fixed.Zero) == Fixed.FromInt(5), "World origin samples vertex (border, border).");
        Check.That(terrain.HeightAt(Fixed.FromInt(10), Fixed.Zero) == Fixed.FromInt(10), "One cell east is one ramp step higher.");
        Check.That(terrain.HeightAt(Fixed.FromInt(5), Fixed.FromInt(7)) == Fixed.FromDecimal(7.5m), "Linear interpolation across a cell.");
        Check.That(terrain.HeightAt(Fixed.FromInt(-100), Fixed.FromInt(3)) == Fixed.FromInt(0), "Positions west of the height field clamp to its first column.");
        Check.That(terrain.HeightAt(Fixed.FromInt(1000), Fixed.FromInt(3)) == Fixed.FromInt(25), "Positions east of the height field clamp to its last column.");

        // A single raised vertex exercises the triangle split: vertex (2,2) = 16 raw (10 m), the rest 0.
        var spike = TerrainMap.Build(SyntheticMaps.Build(3, 3, 1, (x, y) => x == 2 && y == 2 ? 16 : 0), "spike");
        Check.That(spike.HeightAt(Fixed.FromInt(10), Fixed.FromInt(10)) == Fixed.FromInt(10), "The spike vertex itself.");
        // Inside cell (0,0) of world space (vertices (1,1)..(2,2)), the diagonal runs (1,1)-(2,2): both triangles meet the spike on it.
        Check.That(spike.HeightAt(Fixed.FromInt(5), Fixed.FromInt(5)) == Fixed.FromInt(5), "The diagonal is the average of its end vertices.");
        Check.That((spike.HeightAt(Fixed.FromInt(9), Fixed.FromInt(1)) - Fixed.One).Abs() < Fixed.FromRaw(64),
            "Beside the flat corner (2,1) the lower triangle rises only with the spike's share: 0.1 * 16 raw * 0.625 = 1 m.");
    }

    public static void TerrainCellClassification()
    {
        var cliffs = new[] { (2, 1) };
        var map = SyntheticMaps.Build(4, 3, 1, cliffs: cliffs,
            triggers: [SyntheticMaps.Water("Lake", 0, (20, 0), (40, 0), (40, 30), (20, 30))],
            height: (x, _) => x >= 3 ? 0 : 20);
        var terrain = TerrainMap.Build(map, "mixed");
        // The cliff bit is addressed in height-cell coordinates (border offset applied), so world cell (1,0) is height cell (2,1).
        Check.That(terrain.CellAt(1, 0).HasFlag(CellFlags.Cliff), "Cliff flag comes from the blend tile bit mask with the border applied.");
        Check.That(!terrain.CellAt(0, 0).HasFlag(CellFlags.Cliff), "Other cells are not cliffs.");
        // Water covers world x 20..40; heights there: x>=3 (world cells >=2) are 0 -> below level 0? equal is not below.
        Check.That(!terrain.CellAt(2, 0).HasFlag(CellFlags.Water), "Terrain exactly at the water level is not underwater.");

        var deep = TerrainMap.Build(SyntheticMaps.Build(4, 3, 1, (x, _) => x >= 3 ? 0 : 40,
            triggers: [SyntheticMaps.Water("Lake", 10, (20, 0), (40, 0), (40, 30), (20, 30))]), "deep");
        Check.That(deep.CellAt(2, 1).HasFlag(CellFlags.Water) && deep.CellAt(3, 2).HasFlag(CellFlags.Water), "Cells with a corner below the level are water.");
        Check.That(!deep.CellAt(0, 0).HasFlag(CellFlags.Water), "Cells outside the polygon are dry.");
        Check.That(deep.IsUnderwater(Fixed.FromInt(30), Fixed.FromInt(15)) && !deep.IsUnderwater(Fixed.FromInt(5), Fixed.FromInt(15)), "IsUnderwater follows polygon and terrain height.");
        Check.That(deep.TryWaterLevel(Fixed.FromInt(30), Fixed.FromInt(15), out var level) && level == Fixed.FromInt(10), "The water level is the z of the first polygon point.");
        Check.That(!deep.TryWaterLevel(Fixed.FromInt(10), Fixed.FromInt(15), out _), "No water outside the polygon.");

        var overlapping = TerrainMap.Build(SyntheticMaps.Build(4, 3, 1, (_, _) => 0,
            triggers: [SyntheticMaps.Water("Low", 5, (0, 0), (40, 0), (40, 30), (0, 30)), SyntheticMaps.Water("High", 9, (10, 10), (30, 10), (30, 20), (10, 20))]), "overlap");
        Check.That(overlapping.TryWaterLevel(Fixed.FromInt(20), Fixed.FromInt(15), out var top) && top == Fixed.FromInt(9), "The highest level among overlapping areas wins.");
        Check.That(overlapping.TryWaterLevel(Fixed.FromInt(2), Fixed.FromInt(2), out var bottom) && bottom == Fixed.FromInt(5), "A single area gives its own level.");
    }

    public static void TerrainInWorldSnapshotAndReplay()
    {
        var fixture = new SyntheticFixture();
        var terrain = TerrainMap.Build(SyntheticMaps.Build(4, 4, 1, (x, _) => x * 8), "ramp");
        var other = TerrainMap.Build(SyntheticMaps.Build(4, 4, 1, (x, _) => x * 4), "flatter");
        var configuration = new WorldConfiguration(new WorldId(1), 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint);
        Check.Throws<ArgumentException>(() => new SimulationWorld(configuration, fixture.Templates), "terrain fingerprint");
        Check.Throws<ArgumentException>(() => new SimulationWorld(configuration, fixture.Templates, other), "terrain fingerprint");

        var world = new SimulationWorld(configuration, fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Barrel", x: 10, y: 0);
        world.Step();
        var barrel = world.FindEntity(new EntityId(1))!;
        Check.That(barrel.Z == Fixed.FromInt(10), "A spawn at height 0 is placed on the terrain (10 m one cell east of the origin).");
        Check.That(world.IsSignificantlyAboveTerrain(barrel) == false, "An entity on the ground is not above the terrain.");

        var flat = new SimulationWorld(new WorldConfiguration(new WorldId(1), 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        new Feeder(flat).Spawn(1, "Barrel", x: 10, y: 0);
        flat.Step();
        Check.That(flat.FindEntity(new EntityId(1))!.Z == Fixed.Zero && flat.Terrain is null, "Without terrain the ground stays at height 0.");

        var bytes = WorldSnapshot.Write(world);
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, terrain);
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), "A snapshot with terrain round-trips byte-identically.");
        Check.Throws<SnapshotFormatException>(() => WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint), "terrain");
        Check.Throws<SnapshotFormatException>(() => WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, other), "terrain");

        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, 30, fixture.Fingerprint, SyntheticFixture.FixtureId, 1, new WorldId(1), TwoPlayers, 10, 5, terrain.Fingerprint);
        var replay = new ReplayFile(header, [], []);
        var read = ReplayFormat.Read(ReplayFormat.Write(replay));
        Check.That(read.Header.TerrainFingerprint == terrain.Fingerprint, "The replay header carries the terrain fingerprint.");
        Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(read, fixture.Templates, fixture.Fingerprint), "terrain");
        var result = ReplayRunner.Run(read, fixture.Templates, fixture.Fingerprint, terrain: terrain);
        Check.That(result.TicksExecuted == 10, "A replay runs on the terrain it names.");
    }

    public static void TerrainStartsAndFingerprint()
    {
        (string, decimal, decimal, decimal)[] waypoints =
        [
            ("Player_2_Start", 30m, 30m, 0m), ("Player_1_Start", 10m, 10m, 0m), ("Center", 20m, 20m, 0m), ("Player_x_Start", 1m, 1m, 0m), ("Player_10_Start", 5m, 5m, 0m),
        ];
        var map = SyntheticMaps.Build(4, 4, 1, (x, y) => x + y, waypoints: waypoints);
        var terrain = TerrainMap.Build(map, "starts");
        Check.That(terrain.StartPositions.Select(start => start.Name).SequenceEqual(["Player_1_Start", "Player_2_Start", "Player_10_Start"]),
            "Start positions are ordered numerically by player index; malformed names are not starts.");
        Check.That(terrain.FindWaypoint("center") is { X: var cx } && cx == Fixed.FromInt(20), "Waypoint lookup ignores case.");
        Check.That(terrain.FindWaypoint("Nowhere") is null, "An unknown waypoint is absent, never defaulted.");

        var again = TerrainMap.Build(SyntheticMaps.Build(4, 4, 1, (x, y) => x + y, waypoints: waypoints.Reverse().ToArray()), "starts");
        // The waypoint table is canonical (sorted); the fingerprint is not order-blind any more (D5): it covers the waypoint ids and the map objects in file
        // order, because that order decides the ids the placed objects get. The same objects in another file order are another map.
        Check.That(terrain.Waypoints.Select(point => (point.Name, point.X, point.Y, point.Z)).SequenceEqual(again.Waypoints.Select(point => (point.Name, point.X, point.Y, point.Z))),
            "The waypoint table does not depend on waypoint object order.");
        Check.That(terrain.Fingerprint != again.Fingerprint, "Another object order in the file is another fingerprint.");
        Check.That(terrain.Fingerprint == TerrainMap.Build(SyntheticMaps.Build(4, 4, 1, (x, y) => x + y, waypoints: waypoints), "starts").Fingerprint, "The same file gives the same fingerprint.");
        var changed = TerrainMap.Build(SyntheticMaps.Build(4, 4, 1, (x, y) => x + y + (x == 2 && y == 2 ? 1 : 0), waypoints: waypoints), "starts");
        Check.That(terrain.Fingerprint != changed.Fingerprint, "A single height byte changes the fingerprint.");
        Check.That(terrain.Fingerprint.Length == 64, "The fingerprint is a SHA-256 hex string.");

        var oversized = TerrainMap.Build(SyntheticMaps.Build(4, 4, 1) with
        {
            HeightField = new MapHeightField(6, 6, 1, [new MapBoundary(9, 4)], [..Enumerable.Range(0, 36).Select(i => (byte)(i % 6 * 10))]),
        }, "oversized");
        Check.That(oversized.CellsX == 9 && oversized.CellsY == 4, "A boundary larger than the height field keeps its declared extent.");
        Check.That(oversized.HeightAt(Fixed.FromInt(85), Fixed.FromInt(5)) == oversized.HeightAt(Fixed.FromInt(45), Fixed.FromInt(5)) && oversized.HeightAt(Fixed.FromInt(45), Fixed.FromInt(5)) > oversized.HeightAt(Fixed.FromInt(5), Fixed.FromInt(5)), "Cells beyond the height field read the clamped edge height.");
        Check.Throws<InvalidDataException>(() => TerrainMap.Build(SyntheticMaps.Build(4, 4, 1) with
        {
            HeightField = new MapHeightField(6, 6, 1, [new MapBoundary(0, 4)], [..new byte[36]]),
        }, "empty"), "empty");
    }

    /// <summary>
    /// Phase 10: <see cref="TerrainMap.HasHeightLineOfSight"/>, the detector/sight height check. All heights below use raw byte 40 = 25 m
    /// (<c>40 * 0.625</c>) so the numbers are exact; observer and target are always far enough apart (180 units, 18 cells) to need several
    /// of the method's samples, not just its endpoints.
    /// </summary>
    public static void TerrainHeightLineOfSight()
    {
        // A flat plain has nothing to block any line of sight, however far.
        var flat = TerrainMap.Build(SyntheticMaps.Build(20, 4, 1), "flat");
        Check.That(flat.HasHeightLineOfSight(Fixed.FromInt(10), Fixed.FromInt(15), Fixed.FromInt(2), Fixed.FromInt(190), Fixed.FromInt(15), Fixed.FromInt(2)),
            "Flat terrain blocks nothing.");
        Check.That(flat.HasHeightLineOfSight(Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.FromInt(5), Fixed.Zero, Fixed.Zero), "Two points closer than one cell apart always see each other.");

        // A 25 m ridge (vertices 10..12, i.e. world x roughly 90..110) sits squarely between two ground-level (2 m eye height) points.
        var ridged = TerrainMap.Build(SyntheticMaps.Build(20, 4, 1, (x, _) => x is >= 10 and <= 12 ? 40 : 0), "ridge");
        Check.That(!ridged.HasHeightLineOfSight(Fixed.FromInt(10), Fixed.FromInt(15), Fixed.FromInt(2), Fixed.FromInt(190), Fixed.FromInt(15), Fixed.FromInt(2)),
            "A 25 m ridge blocks a 2 m-high line of sight straight through it.");
        // The same ridge never reduces the view along a path that does not cross it.
        Check.That(ridged.HasHeightLineOfSight(Fixed.FromInt(10), Fixed.FromInt(5), Fixed.FromInt(2), Fixed.FromInt(50), Fixed.FromInt(5), Fixed.FromInt(2)),
            "Terrain elsewhere on the map does not block an unrelated line of sight.");
        // An observer standing far above the ridge (e.g. on a cliff) sees past it to a ground-level target; a ground-level observer cannot.
        Check.That(ridged.HasHeightLineOfSight(Fixed.FromInt(10), Fixed.FromInt(15), Fixed.FromInt(300), Fixed.FromInt(190), Fixed.FromInt(15), Fixed.FromInt(2)),
            "A high enough observer sees over the ridge to a target beyond it ('sees further over lower terrain').");

        // A 1.25 m bump (raw 2) halfway along stays below a constant 2 m-high line of sight and never blocks it.
        var bumped = TerrainMap.Build(SyntheticMaps.Build(20, 4, 1, (x, _) => x == 11 ? 2 : 0), "bump");
        Check.That(bumped.HasHeightLineOfSight(Fixed.FromInt(10), Fixed.FromInt(15), Fixed.FromInt(2), Fixed.FromInt(190), Fixed.FromInt(15), Fixed.FromInt(2)),
            "A bump below the sight line does not block it.");

        // The sample cap bounds the cost of a very long line; it does not change the blocked/clear outcome of any case above.
        Check.That(TerrainMap.MaxLineOfSightSamples > 0, "The sample cap is a positive, documented limit.");
    }
}

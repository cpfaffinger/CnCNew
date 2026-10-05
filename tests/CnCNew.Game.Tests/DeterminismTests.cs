using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.Snapshots;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Game.Data;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

namespace CnCNew.Game.Tests;

/// <summary>
/// Phase 18, determinism across machines on real data (docs/DETERMINISM-FINDINGS.md): what the read-only search suspected is confirmed or refuted here against the
/// retail content. The synthetic counterparts are in <c>tests/CnCNew.Simulation.Tests/Tests.Determinism.cs</c>.
/// <c>--determinism-full</c> lets every world restored from a checkpoint run to the end of its match instead of <see cref="TailTicks"/> ticks.
/// </summary>
internal static class DeterminismTests
{
    private const long TailTicks = 2_000;
    private const string AlpineAssault = "Maps/Alpine Assault/Alpine Assault.map";

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(DeterminismTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP DeterminismTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }

        var directory = args[index + 1];
        var full = args.Contains("--determinism-full");
        var data = GameData.Load(directory);

        harness.Run("D4: definitions, bone table, map and a mission are the same under de-DE, tr-TR, ar-SA, sv-SE and a made-up culture", () =>
        {
            (string Definitions, string World, string Terrain, string Program, ulong Chain, ulong Final) Play()
            {
                // Everything from the files on: compile, bone table, catalog, map decoding, terrain, 900 mission ticks.
                var content = ContentFileSystem.Open(directory);
                var compilerOptions = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
                var graph = DefinitionCompiler.Compile(new ContentDefinitionSource(content), ModuleRegistry.Default, compilerOptions);
                var geometry = ModelGeometryLoader.Load(graph, ObjectVisualCatalog.Build(graph), content);
                var templates = TemplateCatalog.Build(graph, ModuleBindingTable.Default, geometry);
                var definitions = DefinitionSnapshotWriter.ComputeFingerprint(graph).Sha256;
                var fingerprint = geometry.Combine(definitions);
                using var stream = content.OpenRead("Maps/GC_ChemGeneral/GC_ChemGeneral.map");
                var map = SageMapDecoder.Decode(stream);
                var terrain = TerrainMap.Build(map, "Maps/GC_ChemGeneral/GC_ChemGeneral.map");
                var session = MissionSession.Start(MissionOptions.Standard with { MaxTicks = 0 }, templates, fingerprint, terrain, map);
                var chain = StateHash.Seed;
                while (session.World.Tick < 900 && !session.IsOver) chain = StateHash.Mix(chain, session.Step().StateHash);
                return (definitions, fingerprint, terrain.Fingerprint, session.Program.Fingerprint, chain, session.StateHash());
            }

            var reference = UnderCulture(CultureInfo.InvariantCulture, Play);
            Console.WriteLine($"       invariant: definitions {reference.Definitions[..16]}, world {reference.World[..16]}, terrain {reference.Terrain[..16]}, chain {reference.Chain:X16}, final {reference.Final:X16}");
            TestHarness.Assert(reference.World == data.Fingerprint, "the invariant run builds the world the suite loaded");
            foreach (var culture in HostileCultures())
            {
                var result = UnderCulture(culture, Play);
                var name = culture.Name.Length > 0 ? culture.Name : "made-up";
                Console.WriteLine($"       {name}: {(result == reference ? "identical" : $"DIFFERENT {result}")}");
                TestHarness.Assert(result == reference, $"under {name} the inputs or the mission differ: {result} instead of {reference}");
            }
        });

        harness.Run("D2 and D7: every real of every shipped map converts like the runtime cast; waypoints that tie in name and position", () =>
        {
            long reals = 0, differing = 0, waypoints = 0, tiedGroups = 0, tiedWaypoints = 0, undefinedGroups = 0;
            var distinct = new HashSet<int>();
            string? firstDifference = null;
            var tiedMaps = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in data.Maps)
            {
                using var stream = data.Content.OpenRead(path);
                var map = SageMapDecoder.Decode(stream, bits =>
                {
                    reals++;
                    if (!distinct.Add(bits)) return;
                    var cast = (decimal)BitConverter.Int32BitsToSingle(bits);
                    var routine = SingleBits.ToDecimal(bits);
                    Span<int> left = stackalloc int[4];
                    Span<int> right = stackalloc int[4];
                    decimal.GetBits(cast, left);
                    decimal.GetBits(routine, right);
                    if (left.SequenceEqual(right)) return;
                    differing++;
                    firstDifference ??= $"{path}: bits {bits:X8}: cast {cast.ToString(CultureInfo.InvariantCulture)}, routine {routine.ToString(CultureInfo.InvariantCulture)}";
                });
                // D7: the old comparison ordered waypoints by name, x and y only. Count the groups it could not order, and those whose members really differ.
                var groups = map.Objects.Where(value => value.IsWaypoint && !string.IsNullOrEmpty(value.Properties.GetText("waypointName")))
                    .GroupBy(value => (Name: value.Properties.GetText("waypointName")!, value.X, value.Y)).ToArray();
                waypoints += groups.Sum(group => group.Count());
                foreach (var group in groups.Where(group => group.Count() > 1))
                {
                    tiedGroups++;
                    tiedWaypoints += group.Count();
                    tiedMaps.Add(path);
                    if (group.Select(value => (value.Z, Id: value.Properties.TryGetInt("waypointID", out var id) ? id : -1)).Distinct().Count() > 1) undefinedGroups++;
                }
            }
            Console.WriteLine($"       {data.Maps.Length} maps: {reals} reals ({distinct.Count} distinct bit patterns), {differing} convert differently from the runtime cast");
            Console.WriteLine($"       {waypoints} named waypoints: {tiedGroups} groups ({tiedWaypoints} waypoints) tie in name, x and y on {tiedMaps.Count} maps; {undefinedGroups} of them differ in height or id{(tiedMaps.Count > 0 ? ": " + string.Join(", ", tiedMaps.Take(8)) : string.Empty)}");
            TestHarness.Assert(differing == 0, $"SingleBits.ToDecimal differs from (decimal)float: {firstDifference}");
            TestHarness.Assert(reals > 100_000, $"the maps contain reals ({reals})");
        });

        return harness.Failures;
    }

    private static ImmutableArray<CultureInfo> HostileCultures()
    {
        var hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        hostile.NumberFormat.NegativeSign = "~";
        hostile.NumberFormat.NumberDecimalSeparator = "#";
        hostile.NumberFormat.NumberGroupSeparator = "'";
        return [CultureInfo.GetCultureInfo("de-DE"), CultureInfo.GetCultureInfo("tr-TR"), CultureInfo.GetCultureInfo("ar-SA"), CultureInfo.GetCultureInfo("sv-SE"), hostile];
    }

    private static T UnderCulture<T>(CultureInfo culture, Func<T> body)
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

    internal static (MapData Map, TerrainMap Terrain) LoadMap(GameData data, string path)
    {
        using var stream = data.Content.OpenRead(path);
        var map = SageMapDecoder.Decode(stream);
        return (map, TerrainMap.Build(map, path));
    }

    private static void Report(string name, (int Restores, long TicksCompared, string? Divergence) result)
    {
        Console.WriteLine($"       {name}: {result.Restores} restores, {result.TicksCompared} ticks compared, {(result.Divergence is null ? "identical" : result.Divergence)}");
        TestHarness.Assert(result.Divergence is null, $"{name}: {result.Divergence}");
    }

    /// <summary>
    /// Plays the replay once and keeps a snapshot every <paramref name="interval"/> ticks; then restores every snapshot into a fresh world and lets it run on
    /// (to the end, or <paramref name="tailTicks"/> ticks), comparing every tick hash with the continuous run.
    /// </summary>
    private static (int Restores, long TicksCompared, string? Divergence) RestoreAtEveryCheckpoint(ReplayFile replay, TemplateCatalog templates, string fingerprint, TerrainMap? terrain, int interval, long tailTicks)
    {
        var snapshots = new List<(long Tick, byte[] Bytes)>();
        var continuous = ReplayRunner.Run(replay, templates, fingerprint, new ReplayRunOptions
        {
            RecordTickHashes = true,
            AfterTick = (world, _) => { if (world.Tick % interval == 0) snapshots.Add((world.Tick, WorldSnapshot.Write(world))); },
        }, terrain);
        long compared = 0;
        string? divergence = null;
        foreach (var (tick, bytes) in snapshots)
        {
            var restored = WorldSnapshot.Restore(bytes, templates, fingerprint, terrain);
            var stop = tailTicks > 0 ? Math.Min(continuous.EndTick, tick + tailTicks) : continuous.EndTick;
            var resumed = ReplayRunner.Run(replay, restored, new ReplayRunOptions { RecordTickHashes = true, StopTick = stop });
            for (var offset = 0; offset < resumed.TickHashes.Length; offset++)
            {
                compared++;
                if (resumed.TickHashes[offset] == continuous.TickHashes[tick + offset]) continue;
                divergence ??= $"the world restored at tick {tick} differs from the continuous one at tick {tick + offset}";
                break;
            }
        }
        return (snapshots.Count, compared, divergence);
    }

    private static (int Restores, long TicksCompared, string? Divergence) RestoreMissionAtEveryCheckpoint(GameData data, MapData map, TerrainMap terrain, long ticks, int interval, long tailTicks)
    {
        var options = MissionOptions.Standard with { MaxTicks = 0 };
        var session = MissionSession.Start(options, data.Templates, data.Fingerprint, terrain, map);
        var hashes = new List<ulong>();
        var snapshots = new List<(long Tick, byte[] Bytes)>();
        while (session.World.Tick < ticks && !session.IsOver)
        {
            hashes.Add(session.Step().StateHash);
            if (session.World.Tick % interval == 0) snapshots.Add((session.World.Tick, MissionSnapshot.Write(session)));
        }
        long compared = 0;
        string? divergence = null;
        foreach (var (tick, bytes) in snapshots)
        {
            var restored = MissionSnapshot.Restore(bytes, data.Templates, data.Fingerprint, terrain, map);
            var stop = tailTicks > 0 ? Math.Min(hashes.Count, tick + tailTicks) : hashes.Count;
            while (restored.World.Tick < stop && !restored.IsOver)
            {
                var at = restored.World.Tick;
                compared++;
                if (restored.Step().StateHash == hashes[(int)at]) continue;
                divergence ??= $"the mission restored at tick {tick} differs from the continuous one at tick {at}";
                break;
            }
        }
        return (snapshots.Count, compared, divergence);
    }
}

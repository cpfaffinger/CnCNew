using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
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
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.W3D;

namespace CnCNew.Game.Tests;

/// <summary>
/// Phase 18, determinism across machines on real data (docs/DETERMINISM-FINDINGS.md): what the read-only search suspected is confirmed or refuted here against the
/// retail content. The synthetic counterparts are in <c>tests/CnCNew.Simulation.Tests/Tests.Determinism.cs</c>.
/// <c>--determinism-full</c> lets every world restored from a checkpoint run to the end of its match instead of <see cref="TailTicks"/> ticks and verifies the
/// incremental hash on every campaign and challenge map; <c>--determinism-only D8</c> runs the tests of one finding.
/// </summary>
internal static class DeterminismTests
{
    private const long TailTicks = 2_000;
    private const string AlpineAssault = "Maps/Alpine Assault/Alpine Assault.map";

    /// <summary>
    /// SHA-256 of the airfield bone table (<c>ModelGeometry.Fingerprint</c>) of the retail content. It is part of the world fingerprint; a different value on another machine means the inputs differ before a single tick ran.
    /// The table is hashed as raw fixed-point values: the value changed with the resolution (phase 22, 24 fraction bits; at 16 it was 93D5819B…F1DA47). The poses are the same numbers, quantised as before. Since phase 31 the table also carries the exit bones of the door containers (F-03); with the airfields alone it was EA1915EB…DAC858. Since phase 41 it carries the fire points and stations of garrisons and transports (F-60, step 1); before, 4ECE1638…A0C980A. Since the turret pivot of PassengersInTurret (F-60, step 4) it is 843CCD2C…1591514; with the points alone E1A2A799…88E484C7.
    /// </summary>
    private const string BoneTableGolden = "C3B640AD4934F845DED0513362A95F709AACD5D1AF4B17DF22C8EEA8DB236D6F";

    public static int Run(string[] args)
    {
        // --determinism-only D8 runs just the tests of one finding.
        var harness = new TestHarness(nameof(DeterminismTests)) { OnlyPrefix = Array.IndexOf(args, "--determinism-only") is var only and >= 0 && only + 1 < args.Length ? args[only + 1] : null };
        harness.Run("D1: a synthetic skeleton's rest poses are computed exactly, rounded half-to-even, and agree with the single-precision path", () =>
        {
            static W3dPivot Pivot(string name, int parent, Vector3 translation, Quaternion rotation) => new(name, parent, translation, Vector3.Zero, rotation);
            var quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
            var twelfth = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 6);
            var skeleton = W3dSkeleton.FromHierarchy(new W3dHierarchy(0x00040001, "SYNTH", Vector3.Zero,
            [
                Pivot("ROOTTRANSFORM", -1, new Vector3(999f, 999f, 999f), quarter),      // bone 0 is the identity whatever the file says
                Pivot("Base", 0, new Vector3(10f, 20f, 0f), quarter),
                Pivot("Child", 1, new Vector3(5f, 0f, 2f), Quaternion.Identity),
                Pivot("Turned", 0, Vector3.Zero, twelfth),
                Pivot("Arm", 3, new Vector3(16f, 0f, 0f), Quaternion.Identity),
                Pivot("Ties", 0, new Vector3(1.03125f, 1.09375f, -1.03125f), Quaternion.Identity),
                Pivot("Back", 0, Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI)),
                Pivot("Tilted", 2, new Vector3(0.3f, -7.77f, 1.234f), Quaternion.Normalize(new Quaternion(0.1f, -0.2f, 0.3f, 0.9f))),
            ], []));
            BonePose Pose(string name) => ModelGeometryLoader.RestPose(skeleton, skeleton.FindBone(name));
            static Fixed Sixteenths(int count) => Fixed.FromDecimal(count / 16m);
            static Fixed Heading(int count) => Fixed.FromDecimal(count / 1024m);

            TestHarness.Assert(Pose("ROOTTRANSFORM") == new BonePose(Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero), $"bone 0 is the identity: {Pose("ROOTTRANSFORM")}");
            // π/2 = 1608.495... steps of 1/1024 rad: the boundary at 1608.5 is 4.6e-6 rad away, the reason the arc tangent is computed far finer than the table.
            TestHarness.Assert(Pose("Base") == new BonePose(Sixteenths(160), Sixteenths(320), Fixed.Zero, Heading(1608)), $"Base: {Pose("Base")}");
            // (5, 0, 2) turned a quarter about z and moved by (10, 20, 0).
            TestHarness.Assert(Pose("Child") == new BonePose(Sixteenths(160), Sixteenths(400), Sixteenths(32), Heading(1608)), $"Child: {Pose("Child")}");
            // 16 along x turned by 30 degrees: (13.856, 8, 0) -> 222/16 and 128/16; the heading 0.5236 rad -> 536/1024.
            TestHarness.Assert(Pose("Arm") == new BonePose(Sixteenths(222), Sixteenths(128), Fixed.Zero, Heading(536)), $"Arm: {Pose("Arm")}");
            // Exact halves of a sixteenth go to the even neighbour: 16.5 -> 16, 17.5 -> 18, -16.5 -> -16.
            TestHarness.Assert(Pose("Ties") == new BonePose(Sixteenths(16), Sixteenths(18), Sixteenths(-16), Fixed.Zero), $"Ties: {Pose("Ties")}");
            // Half a turn: +π or -π by the sign of a sine that is zero up to rounding; both round to ±3217/1024.
            TestHarness.Assert(Pose("Back").Heading.Abs() == Heading(3217), $"Back: {Pose("Back")}");

            // Every bone against the single-precision matrices the table used to be read from (the two paths may only differ on a rounding boundary; none of these is one).
            for (var bone = 0; bone < skeleton.BoneCount; bone++)
            {
                var matrix = skeleton.RestWorldTransforms[bone];
                var exact = ModelGeometryLoader.RestPose(skeleton, bone);
                static Fixed Old(float value, decimal step) => Fixed.FromDecimal(decimal.Round((decimal)value * step) / step);
                var single = new BonePose(Old(matrix.M41, 16m), Old(matrix.M42, 16m), Old(matrix.M43, 16m), Old(MathF.Atan2(matrix.M12, matrix.M11), 1024m));
                if (skeleton.BoneNames[bone] == "Back") continue;
                TestHarness.Assert(exact == single, $"{skeleton.BoneNames[bone]}: exact {exact}, single precision {single}");
            }

            // The whole-number arc tangent against the double-precision one over a spread of vectors in all quadrants.
            var state = 0x2545F491u;
            var compared = 0;
            for (var sample = 0; sample < 20_000; sample++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                var y = ((int)(state & 0xFFFF) - 0x8000) / 256f;
                var x = ((int)((state >> 16) & 0xFFFF) - 0x8000) / 256f;
                var angle = Math.Atan2(y, x) * 1024.0;
                if (Math.Abs(angle - Math.Floor(angle) - 0.5) < 1e-6) continue;
                var steps = ModelGeometryLoader.HeadingSteps(BitConverter.SingleToInt32Bits(y), BitConverter.SingleToInt32Bits(x));
                TestHarness.Assert(steps == (long)Math.Round(angle, MidpointRounding.ToEven), $"atan2({y}, {x}): {steps} steps, double precision {angle}");
                compared++;
            }
            TestHarness.Assert(compared > 19_000, $"the arc tangent was compared ({compared})");
        });

        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP DeterminismTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }

        var directory = args[index + 1];
        var full = args.Contains("--determinism-full");
        var data = GameData.Load(directory, rate: CnCNew.Tests.TestRate.Default);

        harness.Run("D1: the bone table of the retail airfields has its golden hash", () =>
        {
            var geometry = data.Templates.Geometry;
            Console.WriteLine($"       bone table: {geometry.Airfields.Count} airfields, SHA-256 {geometry.Fingerprint}");
            TestHarness.Assert(geometry.Fingerprint == BoneTableGolden, $"the bone table changed: {geometry.Fingerprint} instead of {BoneTableGolden}");
        });

        harness.Run("D3: a world restored at every checkpoint runs like the continuous one (reference match, two AI matches)", () =>
        {
            var reference = new ReferenceMatchScenario(ReferenceMatchOptions.StandardAt(data.Rate)).Record(data.Templates, data.Fingerprint);
            Report("reference match", RestoreAtEveryCheckpoint(reference, data.Templates, data.Fingerprint, null, 1_000, full ? 0 : TailTicks));
            foreach (var factions in new[] { "FactionChina,FactionGLA", "FactionAmerica,FactionChina" })
            {
                var (map, terrain) = LoadMap(data, AlpineAssault);
                var chosen = factions.Split(',').Select(name => data.Factions.First(value => value.Faction.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToImmutableArray();
                var replay = new SkirmishMatchScenario(SkirmishMatchOptions.Standard with { MaxTicks = 45_000 }).Record(data.Templates, data.Fingerprint, terrain, map.Objects, chosen);
                Report($"AI match Alpine Assault {factions}", RestoreAtEveryCheckpoint(replay, data.Templates, data.Fingerprint, terrain, 1_000, full ? 0 : TailTicks));
            }
        });

        harness.Run("D3: a mission restored every 150 ticks runs like the continuous one (MD_USA05, MD_GLA05)", () =>
        {
            foreach (var name in new[] { "MD_USA05", "MD_GLA05" })
            {
                var (map, terrain) = LoadMap(data, $"Maps/{name}/{name}.map");
                // MD_GLA05 restored at tick 150 left the continuous run at tick 900 before the entity table was ordered: the tail has to reach that far.
                Report($"mission {name}", RestoreMissionAtEveryCheckpoint(data, map, terrain, full ? 3_000 : 1_500, 150, full ? 0 : 900));
            }
        });

        harness.Run("D8: the incremental hash equals the full hash in every tick (reference match, AI match, two missions; bounded)", () =>
        {
            void Verify(string name, ReplayFile replay, TerrainMap? terrain, long ticks)
            {
                long? stop = full ? null : ticks;
                var plain = ReplayRunner.Run(replay, data.Templates, data.Fingerprint, new ReplayRunOptions { RecordTickHashes = true, StopTick = stop }, terrain);
                var verified = ReplayRunner.Run(replay, data.Templates, data.Fingerprint, new ReplayRunOptions { RecordTickHashes = true, StopTick = stop, VerifyIncrementalEvery = 1 }, terrain);
                Console.WriteLine($"       {name}: {verified.TicksExecuted} ticks verified, final hash {verified.FinalHash:X16}");
                TestHarness.Assert(plain.TickHashes.AsSpan().SequenceEqual(verified.TickHashes), $"{name}: verifying in every tick changes a tick hash");
            }

            Verify("reference match", new ReferenceMatchScenario(ReferenceMatchOptions.StandardAt(data.Rate)).Record(data.Templates, data.Fingerprint), null, 6_000);
            var (alpine, alpineTerrain) = LoadMap(data, AlpineAssault);
            var chosen = new[] { "FactionChina", "FactionGLA" }.Select(name => data.Factions.First(value => value.Faction.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToImmutableArray();
            Verify("AI match Alpine Assault FactionChina,FactionGLA",
                new SkirmishMatchScenario(SkirmishMatchOptions.Standard with { MaxTicks = 45_000 }).Record(data.Templates, data.Fingerprint, alpineTerrain, alpine.Objects, chosen), alpineTerrain, 9_000);
            // MD_GLA05 is the mission that showed the finding: WAREHOUSE_SET_VALUE at tick 855 changed five supply docks without MarkDirty.
            // --determinism-full verifies every campaign and challenge map for 1,500 ticks instead.
            var missions = full
                ? data.Maps.Where(path => path.Split('/') is [_, var folder, _] && (folder.StartsWith("MD_", StringComparison.OrdinalIgnoreCase) || folder.StartsWith("GC_", StringComparison.OrdinalIgnoreCase))).Select(path => (Path: path, Ticks: 1_500L)).ToArray()
                : [("Maps/MD_USA05/MD_USA05.map", 600L), ("Maps/GC_ChemGeneral/GC_ChemGeneral.map", 1_500L), ("Maps/MD_GLA05/MD_GLA05.map", 1_200L)];
            var failures = new List<string>();
            foreach (var (path, ticks) in missions)
            {
                var (map, terrain) = LoadMap(data, path);
                var options = MissionOptions.Standard with { MaxTicks = 0 };
                MissionSession plain, verified;
                try
                {
                    plain = MissionSession.Start(options, data.Templates, data.Fingerprint, terrain, map);
                    verified = MissionSession.Start(options, data.Templates, data.Fingerprint, terrain, map);
                }
                catch (Exception exception) when (full)
                {
                    Console.WriteLine($"       mission {path}: not started ({exception.Message})");
                    continue;
                }
                try
                {
                    while (plain.World.Tick < ticks && !plain.IsOver)
                    {
                        var expected = plain.Step().StateHash;
                        var actual = verified.Step().StateHash;
                        verified.World.VerifyIncrementalHash();
                        TestHarness.Assert(expected == actual, $"verifying in every tick changes the hash of tick {plain.World.Tick - 1}");
                    }
                    Console.WriteLine($"       mission {path}: {verified.World.Tick} ticks verified, {verified.World.Metrics.Entities} entities, final hash {verified.StateHash():X16}");
                }
                catch (InvalidOperationException exception)
                {
                    Console.WriteLine($"       mission {path}: {exception.Message}");
                    failures.Add($"{path}: {exception.Message}");
                }
            }
            TestHarness.Assert(failures.Count == 0, $"{failures.Count} mission(s) with a stale incremental hash: {string.Join(" || ", failures)}");
        });

        harness.Run("D4: definitions, bone table, map and a mission are the same under de-DE, tr-TR, ar-SA, sv-SE and a made-up culture", () =>
        {
            (string Definitions, string World, string Terrain, string Program, ulong Chain, ulong Final) Play()
            {
                // Everything from the files on: compile, bone table, catalog, map decoding, terrain, 900 mission ticks.
                var content = ContentFileSystem.Open(directory);
                var compilerOptions = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
                var graph = DefinitionCompiler.Compile(new ContentDefinitionSource(content), ModuleRegistry.Default, compilerOptions);
                var geometry = ModelGeometryLoader.Load(graph, ObjectVisualCatalog.Build(graph), content);
                var templates = TemplateCatalog.Build(graph, ModuleBindingTable.Default, CnCNew.Tests.TestRate.Default, geometry, CnCNew.Simulation.Rules.RuleProfile.Product);
                var definitions = DefinitionSnapshotWriter.ComputeFingerprint(graph).Sha256;
                var fingerprint = templates.Rules.Combine(templates.Rate.Combine(geometry.Combine(definitions))); // as GameData.Load builds it: the tick rate is part of the world fingerprint
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

        harness.Run("D5: every shipped map is identified by the hash of its file; the terrain fingerprint of the reference missions", () =>
        {
            var hashes = data.Maps.Select(data.MapFileHash).ToArray();
            TestHarness.Assert(hashes.All(hash => hash.Length == 64 && hash.All(digit => digit is (>= '0' and <= '9') or (>= 'A' and <= 'F'))), "a map file hash is 64 upper-case hex digits");
            foreach (var path in new[] { AlpineAssault, "Maps/MD_USA05/MD_USA05.map", "Maps/MD_GLA05/MD_GLA05.map" })
            {
                using var stream = data.Content.OpenRead(path);
                var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                TestHarness.Assert(actual == data.MapFileHash(path), $"{path}: the manifest hash is the hash of the file");
                var (_, terrain) = LoadMap(data, path);
                Console.WriteLine($"       {path}: file {actual[..16]}, terrain fingerprint {terrain.Fingerprint[..16]}");
            }
            Console.WriteLine($"       {hashes.Length} maps, {hashes.Distinct(StringComparer.Ordinal).Count()} distinct file hashes");
            TestHarness.Assert(hashes.Length >= 150, $"the shipped maps are listed ({hashes.Length})");
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

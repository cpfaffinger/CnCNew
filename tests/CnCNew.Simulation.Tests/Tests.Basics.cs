using System.Collections.Immutable;
using System.Text.Json;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.Schema;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

static partial class Tests
{
    public static void FixedPoint()
    {
        Check.That(Fixed.One.Raw == 65536 && Fixed.FromInt(3).Raw == 196608, "Scale is 16 fractional bits.");
        Check.That(Fixed.FromDecimal(1.5m).Raw == 98304, "1.5 is exact.");
        // Ties round away from zero in every operation.
        Check.That(Fixed.FromRaw(1) * Fixed.FromRaw(32768) == Fixed.FromRaw(1), "1 * 0.5 rounds 0.5 up to 1 raw.");
        Check.That(Fixed.FromRaw(-1) * Fixed.FromRaw(32768) == Fixed.FromRaw(-1), "-1 * 0.5 rounds away from zero.");
        Check.That(Fixed.FromRaw(3) * Fixed.FromRaw(21845) == Fixed.FromRaw(1), "3 * 0.33333 = 0.99999 raw rounds to 1.");
        Check.That(Fixed.FromInt(1) / Fixed.FromInt(3) == Fixed.FromRaw(21845), "1/3 rounds to 21845.33 -> 21845.");
        Check.That(Fixed.FromInt(2) / Fixed.FromInt(3) == Fixed.FromRaw(43691), "2/3 rounds to 43690.67 -> 43691.");
        Check.That(Fixed.FromInt(-2) / Fixed.FromInt(3) == Fixed.FromRaw(-43691), "-2/3 rounds symmetrically.");
        Check.That(Fixed.FromRaw(1) / Fixed.FromInt(2) == Fixed.FromRaw(1), "0.5 raw tie divides away from zero.");
        Check.That(Fixed.FromDecimal(0.000007629394531250m).Raw == 1, "Decimal half raw rounds away from zero (2^-17 = 0.5 raw).");
        Check.That(Fixed.FromDecimal(-0.000007629394531250m).Raw == -1, "Negative decimal half raw rounds away from zero.");
        Check.That(Fixed.FromDecimal(100000.123456m).ToDecimal() == 100000.123459m || Math.Abs(Fixed.FromDecimal(100000.123456m).ToDecimal() - 100000.123456m) < 0.00001m, "Round trip within one raw unit.");
        foreach (var raw in new long[] { 0, 1, -1, 65535, 65536, -65537, 123456789, long.MaxValue / 3 })
            Check.That(Fixed.FromDecimal(Fixed.FromRaw(raw).ToDecimal()).Raw == raw, $"Raw {raw} must survive a decimal round trip.");
        Check.That(Fixed.FromRaw(-65537).Floor() == -2 && Fixed.FromRaw(-65537).Ceiling() == -1 && Fixed.FromRaw(-98304).Round() == -2 && Fixed.FromRaw(98304).Round() == 2, "Floor, ceiling and round on negatives.");
        Check.That(Fixed.FromRaw(65536).Floor() == 1 && Fixed.FromRaw(65536).Ceiling() == 1 && Fixed.FromRaw(65537).Ceiling() == 2, "Whole values are unchanged.");
        Check.That(Fixed.Max(Fixed.One, Fixed.Zero) == Fixed.One && Fixed.Min(Fixed.One, -Fixed.One) == -Fixed.One && (-Fixed.One).Abs() == Fixed.One, "Min, max and abs.");
        Check.Throws<OverflowException>(() => _ = Fixed.MaxValue + Fixed.One, "overflow");
        Check.Throws<OverflowException>(() => _ = Fixed.MinValue - Fixed.One, "overflow");
        Check.Throws<OverflowException>(() => _ = Fixed.FromInt(long.MaxValue), "overflow");
        Check.Throws<OverflowException>(() => _ = Fixed.MaxValue * Fixed.FromInt(2), "range");
        Check.Throws<OverflowException>(() => _ = Fixed.FromDecimal(1e15m), "fit");
        Check.Throws<DivideByZeroException>(() => _ = Fixed.One / Fixed.Zero, "");
        Check.That(Fixed.FromInt(1_000_000_000) * Fixed.FromInt(1_000) == Fixed.FromInt(1_000_000_000_000), "128-bit intermediates keep large products exact.");
        Check.That(Fixed.MaxValue / Fixed.One == Fixed.MaxValue, "Division by one is exact at the limit.");
    }

    public static void Timing()
    {
        Check.That(SimulationTiming.LogicFramesPerSecond == 30, "LOGICFRAMES_PER_SECOND is 30.");
        Check.That(SimulationTiming.SleepForever == 0x3fffffff && SimulationTiming.SleepNone == 1, "Sleep constants equal UpdateModule.h.");
        long[][] cases = [[0, 0], [1, 1], [33, 1], [34, 2], [100, 3], [999, 30], [1000, 30], [1001, 31], [1034, 32], [4294967295, 128849019]];
        foreach (var pair in cases)
        {
            Check.That(SimulationTiming.DurationMillisecondsToTicks(pair[0]) == pair[1], $"{pair[0]} ms -> {pair[1]} ticks (integer).");
            Check.That(SimulationTiming.DurationMillisecondsToTicks((decimal)pair[0]) == pair[1], $"{pair[0]} ms -> {pair[1]} ticks (decimal).");
        }
        Check.That(SimulationTiming.DurationMillisecondsToTicks(33.3m) == 1 && SimulationTiming.DurationMillisecondsToTicks(33.4m) == 2 && SimulationTiming.DurationMillisecondsToTicks(0.001m) == 1, "Fractional milliseconds round up.");
        Check.Throws<ArgumentOutOfRangeException>(() => SimulationTiming.DurationMillisecondsToTicks(-1L), "negative");
        Check.Throws<ArgumentOutOfRangeException>(() => SimulationTiming.DurationMillisecondsToTicks(-0.5m), "negative");
        Check.Throws<OverflowException>(() => SimulationTiming.DurationMillisecondsToTicks(long.MaxValue), "");
        Check.That(SimulationTiming.DurationMillisecondsToFixedTicks(1000m) == Fixed.FromInt(30) && SimulationTiming.DurationMillisecondsToFixedTicks(50m) == Fixed.FromDecimal(1.5m), "Unrounded durations.");
        Check.That(SimulationTiming.SecondsToTicks(1m) == 30 && SimulationTiming.SecondsToTicks(0.01m) == 1 && SimulationTiming.SecondsToTicks(0m) == 0, "Seconds to ticks round up.");
        Check.That(SimulationTiming.PerSecondToPerTick(30m) == Fixed.One && SimulationTiming.PerSecondToPerTick(15m) == Fixed.FromDecimal(0.5m), "Velocity per tick.");
        Check.That(SimulationTiming.PerSecondSquaredToPerTickSquared(900m) == Fixed.One, "Acceleration per tick squared.");
        Check.That(SimulationTiming.DegreesToRadians(180m).Raw == 205887 && SimulationTiming.DegreesToRadians(0m) == Fixed.Zero && SimulationTiming.DegreesToRadians(-90m).Raw == -102944, "Degrees to radians uses pi to 28 digits, rounded to nearest.");
        Check.That(SimulationTiming.DegreesPerSecondToRadiansPerTick(180m * 30m).Raw == 205887, "Angular velocity per tick.");
        Check.That(SimulationTiming.RatioToFixed(1.25m) == Fixed.FromDecimal(1.25m), "Percent ratio.");
    }

    public static void FloatDeviation()
    {
        // The reference computes (UnsignedInt)ceilf((Real)msec * (30.0f / 1000.0f)) in binary32. Count where that differs from
        // the exact integer rule; the deviation is documented, bounded and deliberately in favour of the exact rule.
        var perMsec = 30.0f / 1000.0f;
        var differences = new List<(long Milliseconds, long Float, long Exact)>();
        for (long ms = 0; ms <= 5_000_000; ms++)
        {
            var reference = (long)MathF.Ceiling((float)ms * perMsec);
            var exact = SimulationTiming.DurationMillisecondsToTicks(ms);
            if (reference != exact) differences.Add((ms, reference, exact));
        }
        Console.WriteLine($"       binary32 reference vs exact ticks: {differences.Count} of 5000001 durations (0 to about 83 minutes) differ; first: "
            + string.Join(", ", differences.Take(6).Select(value => $"{value.Milliseconds}ms {value.Float}->{value.Exact}")));
        Check.That(differences.All(value => value.Float == value.Exact - 1), "Where they differ, binary32 rounds one tick down (24-bit mantissa) - never up.");
        Check.That(differences.All(value => value.Milliseconds >= 4_000_000), "All durations below 4,000,000 ms (about 66 minutes) convert identically.");
        Check.That(differences.Count < 50_000, "The deviation must stay a small fraction of the values.");
        FloatDeviationCount = differences.Count;
    }

    public static int FloatDeviationCount { get; private set; }

    private static bool Same(uint[] actual, params uint[] expected) => actual.AsSpan().SequenceEqual(expected);

    public static void GoldenRandom()
    {
        // Values from scratchpad/golden_rng.py, a separate transcription of RandomValue.cpp (see docs/SIMULATION-CORE.md).
        void Raw(uint seed, uint[] expected, uint[] stateAfter)
        {
            var random = new SageRandom(seed);
            foreach (var value in expected) Check.That(random.NextUInt32() == value, $"seed {seed:X}: raw sequence differs.");
            var state = new uint[6];
            random.CopyState(state);
            Check.That(Same(state, stateAfter), $"seed {seed:X}: state after the sequence differs.");
        }
        Raw(0, [0x559a51ed, 0x274ea7f5, 0xe827f7e0, 0x76b65632, 0x7f1b3feb, 0xeae8d5ee, 0x5101186f, 0x6575225a],
            [0x6575225a, 0x147409ea, 0xae5bc769, 0xb4111aec, 0x1d2f1ab9, 0x6fdf3b6c]);
        Raw(1, [0x559a51f3, 0x274ea80a, 0xe827f818, 0x76b656b0, 0x7f1b40e7, 0xeae8d7bc, 0x51011b87, 0x65752761],
            [0x65752761, 0x14740bd9, 0xae5bc80e, 0xb4111b19, 0x1d2f1ac2, 0x6fdf3b6d]);
        Raw(0x12345678, [0xc2d458bd, 0xa599bfce, 0xe39ae223, 0x6c78e547, 0x6aa05e13, 0xc55ce28d, 0xa2ec9bc6, 0xea93d7c5],
            [0xea93d7c5, 0x47a73bfe, 0x6a1782c4, 0xe7444e04, 0xc10624f1, 0x821391e4]);
        Raw(0xFFFFFFFF, [0x559a51e7, 0x274ea7e0, 0xe827f7a8, 0x76b655b4, 0x7f1b3eef, 0xeae8d420, 0x51011557, 0x65751d53],
            [0x65751d53, 0x147407fb, 0xae5bc6c4, 0xb4111abf, 0x1d2f1ab0, 0x6fdf3b6b]);

        var initial = new uint[6];
        new SageRandom(0).CopyState(initial);
        Check.That(Same(initial, 0xf22d0e56, 0x883126e9, 0xc624dd2f, 0x0702c49c, 0x9e353f7d, 0x6fdf3b64), "Seed 0 reproduces the reference constants.");

        void Ranges(uint seed, int lo, int hi, int[] expected)
        {
            var random = new SageRandom(seed);
            foreach (var value in expected) Check.That(random.Range(lo, hi) == value, $"seed {seed:X}: range [{lo},{hi}] differs.");
        }
        Ranges(0, 0, 99, [77, 29, 72, 6, 67, 62, 87, 22, 72, 73, 18, 74]);
        Ranges(1, 0, 99, [83, 50, 28, 32, 19, 24, 79, 9, 74, 76, 86, 62]);
        Ranges(0x12345678, 0, 99, [53, 50, 67, 43, 39, 61, 66, 73, 82, 88, 87, 11]);
        Ranges(0xFFFFFFFF, 0, 99, [71, 8, 16, 80, 15, 0, 95, 35, 70, 70, 50, 86]);
        Ranges(0, -5, 5, [-2, -1, -4, 1, -1, 5, -2, -4, -2, -4, 0, 4]);
        Ranges(0x12345678, -5, 5, [4, -5, -5, -3, 0, -2, -5, -3, -3, 3, -2, -4]);
        // delta wraps to zero for the full int range: the reference returns hi without drawing.
        var full = new SageRandom(0);
        Check.That(full.Range(int.MinValue, int.MaxValue) == int.MaxValue, "Full-range draw returns hi.");
        var stateAfterFull = new uint[6];
        full.CopyState(stateAfterFull);
        Check.That(Same(stateAfterFull, initial), "A zero delta must not consume a draw.");

        var million = new SageRandom(0x12345678);
        for (var index = 0; index < 1_000_000; index++) million.NextUInt32();
        var millionState = new uint[6];
        million.CopyState(millionState);
        Check.That(Same(millionState, 0xd020b74b, 0xdafd10ec, 0xe5c0df3d, 0x405d1ab8, 0x3717abd5, 0x8222d41c), "State after one million draws differs from the independent calculation.");

        // Carry chains (constructed states) including the full increment cascade.
        void Carry(uint[] start, uint expected, uint[] after)
        {
            var random = new SageRandom(0);
            random.SetState(start);
            Check.That(random.NextUInt32() == expected, "Carry-chain result differs.");
            var state = new uint[6];
            random.CopyState(state);
            Check.That(Same(state, after), "Carry-chain state differs.");
        }
        Carry([0, 0, 0, 0, 0, 0xFFFFFFFF], 0, [0, 0, 0, 0, 0, 0]);
        Carry([5, 0, 0, 0, 0, 0xFFFFFFFF], 5, [5, 0, 0, 0, 0, 0]);
        Carry([0x80000000, 0x80000000, 0x80000000, 0x80000000, 0x80000000, 0x80000000], 2, [2, 0x80000002, 1, 0x80000001, 0, 0x80000001]);
        var small = new SageRandom(0);
        small.SetState([1, 2, 3, 4, 5, 6]);
        Check.That(small.NextUInt32() == 0x15 && small.NextUInt32() == 0x5c && small.NextUInt32() == 0x111, "Small-state sequence differs.");
        var smallState = new uint[6];
        small.CopyState(smallState);
        Check.That(Same(smallState, 0x111, 0xb5, 0x6e, 0x3b, 0x1a, 0x9), "Small-state final state differs.");
    }

    public static void RandomStreamsBehave()
    {
        var streams = new RandomStreams(0xABCDEF01);
        var reference = new SageRandom(0xABCDEF01);
        // Every stream is seeded from the world seed (InitRandom) but advances independently.
        Check.That(streams.Logic.NextUInt32() == reference.NextUInt32(), "Logic stream equals a generator with the world seed.");
        Check.That(streams.Cosmetic.NextUInt32() == new SageRandom(0xABCDEF01).NextUInt32(), "Cosmetic stream starts from the same seed.");
        streams.Cosmetic.NextUInt32();
        streams.Cosmetic.NextUInt32();
        Check.That(streams.Logic.NextUInt32() == reference.NextUInt32(), "Drawing from the cosmetic stream must not move the logic stream.");
        Check.That(new RandomStreams(1).Logic.NextUInt32() != new RandomStreams(2).Logic.NextUInt32(), "Different seeds give different sequences.");

        var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            var writer = new JsonStateWriter(json);
            writer.BeginObject(null);
            streams.DescribeLogic(writer);
            streams.DescribePresentation(writer);
            writer.EndObject();
        }
        var copy = new RandomStreams(0);
        using var document = JsonDocument.Parse(stream.ToArray());
        var reader = new JsonStateReader(document.RootElement);
        copy.LoadLogic(reader);
        copy.LoadPresentation(reader);
        for (var index = 0; index < 20; index++)
        {
            Check.That(copy.Logic.NextUInt32() == streams.Logic.NextUInt32(), "Restored logic state continues identically.");
            Check.That(copy.Cosmetic.NextUInt32() == streams.Cosmetic.NextUInt32(), "Restored cosmetic state continues identically.");
            Check.That(copy.Audio.NextUInt32() == streams.Audio.NextUInt32(), "Restored audio state continues identically.");
        }
        var fixedRandom = new SageRandom(7);
        for (var index = 0; index < 1000; index++)
        {
            var value = fixedRandom.Range(Fixed.FromInt(2), Fixed.FromInt(5));
            Check.That(value >= Fixed.FromInt(2) && value <= Fixed.FromInt(5), "Fixed range stays inside [lo, hi].");
        }
        Check.That(fixedRandom.Range(Fixed.FromInt(5), Fixed.FromInt(5)) == Fixed.FromInt(5) && fixedRandom.Range(Fixed.FromInt(6), Fixed.FromInt(5)) == Fixed.FromInt(5), "Empty ranges return hi.");
    }

    public static void IdsAreMonotone()
    {
        var counters = new IdCounters();
        Check.That(!EntityId.Invalid.IsValid && EntityId.Invalid.Value == 0, "Id 0 is invalid.");
        Check.That(counters.AllocateEntity().Value == 1 && counters.AllocateEntity().Value == 2 && counters.AllocateEntity().Value == 3, "Entity ids start at 1 and increase by one.");
        Check.That(counters.AllocateEvent().Value == 1 && counters.AllocateTimer().Value == 1 && counters.AllocateCommand().Value == 1, "Every counter starts at 1.");
        Check.That(PlayerId.System.IsSystem && !new PlayerId(1).IsSystem, "Player 0 is the system.");
        var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            var writer = new JsonStateWriter(json);
            writer.BeginObject(null);
            counters.Describe(writer);
            writer.EndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        var restored = new IdCounters();
        restored.Load(new JsonStateReader(document.RootElement));
        Check.That(restored.AllocateEntity().Value == 4, "Counters round trip.");
        Check.Throws<InvalidDataException>(() =>
        {
            using var bad = JsonDocument.Parse("{\"nextEntity\":0,\"nextEvent\":1,\"nextTimer\":1,\"nextCommand\":1}");
            new IdCounters().Load(new JsonStateReader(bad.RootElement));
        }, "start at 1");
    }

    public static void BindingTable()
    {
        var registry = ModuleRegistry.Default;
        ModuleBindingTable Build(IEnumerable<IModuleBinding> bindings, ModuleRegistry? custom = null) => new(bindings, custom ?? registry);
        var all = BuiltInModules.Bindings;
        Check.That(Build(all).Bindings.Length == 122, "The production table binds one hundred and twenty-two modules (fifteen crate, collide and special modules,twelve phase-4, twenty-four phase-5, fourteen reference-match unlock, eleven special-power, eight supply-economy, one spawner, two repair, three combat, three power-plant, three projectile, five transport/tunnel, two mission-blocker shells, three topple/height-die collision, seven mission-object modules of phase 12, four aircraft modules the particle uplink cannon and four vehicle modules of phase 13).");
        Check.That(all.Select(value => value.Name).SequenceEqual(all.Select(value => value.Name).Order(StringComparer.Ordinal)), "The built-in list is sorted ordinally.");
        Check.That(ModuleBindingTable.Default.TryGet("destroydie", out var binding) && binding.Name == "DestroyDie", "Lookup is case-insensitive.");

        // Factory produces a working, typed module.
        var upgrades = new SyntheticFixture().Templates.Upgrades;
        var body = all.Single(value => value.Name == "ActiveBody");
        var data = body.ParseData(new ModuleConfiguration("ActiveBody", "T", ModuleOwnerKind.Object, [], new("x", 1, 1)), upgrades);
        Check.That(body.Create(data) is ActiveBody, "The ActiveBody binding creates an ActiveBody.");

        Check.Throws<ModuleBindingException>(() => Build(all.Concat([all[0]]).OrderBy(value => value.Name, StringComparer.Ordinal)), "Duplicate");
        Check.Throws<ModuleBindingException>(() => Build(all.Reverse()), "sorted");
        Check.Throws<ModuleBindingException>(() => Build(all.Take(all.Length - 1)), "no binding");
        var unknown = new ModuleBinding<InactiveBodyData>("ZZZUnknownModule", ModuleOwnerKind.Object, (_, _) => InactiveBodyData.Instance, _ => new InactiveBody());
        Check.Throws<ModuleBindingException>(() => Build(all.Append(unknown)), "unknown module name");
        var unimplemented = new ModuleBinding<InactiveBodyData>("DamDie", ModuleOwnerKind.Object, (_, _) => InactiveBodyData.Instance, _ => new InactiveBody());
        Check.Throws<ModuleBindingException>(() => Build(all.Append(unimplemented).OrderBy(value => value.Name, StringComparer.Ordinal)), "not implemented");
        var presentation = new ModuleBinding<InactiveBodyData>("W3DModelDraw", ModuleOwnerKind.Object, (_, _) => InactiveBodyData.Instance, _ => new InactiveBody());
        Check.Throws<ModuleBindingException>(() => Build(all.Append(presentation).OrderBy(value => value.Name, StringComparer.Ordinal)), "");
        var wrongOwner = all.Select(value => value.Name == "KeepObjectDie"
            ? new ModuleBinding<PlainDieData>("KeepObjectDie", ModuleOwnerKind.Fx, (configuration, _) => PlainDieData.Parse(configuration), data => new KeepObjectDie(data))
            : value);
        Check.Throws<ModuleBindingException>(() => Build(wrongOwner), "owner");
    }

    public static void FixtureTemplates()
    {
        var fixture = new SyntheticFixture();
        var catalog = fixture.Templates;
        Check.That(catalog.Templates.Select(value => value.Name).Contains("DefaultThingTemplate"), "The default template is an object too.");
        Check.That(catalog.Templates.Select(value => value.Id.ToString()).SequenceEqual(catalog.Templates.Select(value => value.Id.ToString()).Order(StringComparer.Ordinal)), "Template indices follow the ordinal id order.");
        Check.That(catalog.Templates.Select(value => value.Index).SequenceEqual(Enumerable.Range(0, catalog.Templates.Length)), "Indices are dense.");
        Check.That(catalog.Templates.All(value => value.IsInstantiable), "Every synthetic object is instantiable.");
        Check.That(catalog.TryGet("barrel", out var barrel) && barrel.Modules.Select(value => value.Name).SequenceEqual(["ActiveBody", "DestroyDie", "FXListDie", "CreateObjectDie"]), "Barrel modules keep declaration order.");
        Check.That(barrel.PresentationModules.SequenceEqual(["W3DModelDraw"]), "Draw modules are listed as presentation and never instantiated.");
        Check.That(catalog.TryGet("Prop", out var prop) && prop.Modules.Select(value => value.Name).SequenceEqual(["InactiveBody", "DestroyDie"]) && prop.PresentationModules.SequenceEqual(["W3DDefaultDraw"]), "An empty object inherits the default template.");
        Check.That(catalog.TryGet("Tank", out var tank) && tank.Modules.Select(value => value.Name).SequenceEqual(["ActiveBody", "InstantDeathBehavior"]), "InstantDeathBehavior (a die module) replaces the default DestroyDie.");
        Check.That(barrel.IsKindOf("STRUCTURE") && barrel.Side == "Civilian" && barrel.Geometry == "SPHERE" && barrel.GeometryMajorRadius == Fixed.One, "Template facts come from the typed definition.");

        // A module the simulation does not implement makes the object non-instantiable, and it is named.
        var files = new Dictionary<string, string>(SyntheticFixture.Files)
        {
            ["Data/INI/Object/Blocked.ini"] = "Object Blocked\n  Behavior = ParkingPlaceBehavior Park01\n  End\n  Behavior = SlavedUpdate Slave01\n  End\nEnd\n",
        };
        var graph = CnCNew.Data.Compilation.DefinitionCompiler.Compile(new InMemoryDefinitionSource(files), ModuleRegistry.Default,
            CnCNew.Data.Compilation.DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true });
        var blockedCatalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default);
        Check.That(blockedCatalog.TryGet("Blocked", out var blocked) && !blocked.IsInstantiable && blocked.MissingModules.SequenceEqual(["ParkingPlaceBehavior", "SlavedUpdate"]), "Missing modules are listed sorted and never dropped.");
        var report = ModuleCoverage.Compute(blockedCatalog);
        Check.That(report.Blocked == 1 && report.Blockers.Length == 2 && report.Blockers.All(value => value.ObjectsBlocked == 1 && value.ObjectsBlockedOnlyByThisModule == 0), "Coverage names each blocker.");
    }
}

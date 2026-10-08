using System.Text;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 20, stage 1: the tick rate is a value of the world. The conversions of the time service against the former static formulas (kept here as the
/// oracle), the supported rates, the catalog per rate, the rate check of the world, and snapshots and replays at 60 and 120 Hz.
/// </summary>
static partial class Tests
{
    // ------------------------------------------------------------------------------------ the oracle: SimulationTiming as it was before phase 20

    private static class FormerTiming
    {
        private const int LogicFramesPerSecond = 30;
        private const long MillisecondsPerSecond = 1000;
        private const decimal Pi = 3.1415926535897932384626433833m;

        public static long DurationMillisecondsToTicks(long milliseconds) => checked(milliseconds * LogicFramesPerSecond + (MillisecondsPerSecond - 1)) / MillisecondsPerSecond;
        public static long DurationMillisecondsToTicks(decimal milliseconds) => (long)decimal.Ceiling(milliseconds * LogicFramesPerSecond / MillisecondsPerSecond);
        public static Fixed DurationMillisecondsToFixedTicks(decimal milliseconds) => Fixed.FromDecimal(milliseconds * LogicFramesPerSecond / MillisecondsPerSecond);
        public static long SecondsToTicks(decimal seconds) => (long)decimal.Ceiling(seconds * LogicFramesPerSecond);
        public static Fixed PerSecondToPerTick(decimal unitsPerSecond) => Fixed.FromDecimal(unitsPerSecond / LogicFramesPerSecond);
        public static Fixed PerSecondSquaredToPerTickSquared(decimal unitsPerSecondSquared) => Fixed.FromDecimal(unitsPerSecondSquared / (LogicFramesPerSecond * LogicFramesPerSecond));
        public static Fixed DegreesPerSecondToRadiansPerTick(decimal degreesPerSecond) => Fixed.FromDecimal(degreesPerSecond * Pi / 180m / LogicFramesPerSecond);
    }

    /// <summary>A wide, deterministic sweep of decimal values: whole numbers, small fractions, values with many digits, both signs when asked for.</summary>
    private static IEnumerable<decimal> RateSweep(bool negative)
    {
        for (var whole = 0; whole <= 2_000; whole++) yield return whole;
        for (var step = 1; step <= 4_000; step++) yield return step * 0.0375m;
        for (var step = 1; step <= 2_000; step++) yield return step * 7.123456789m;
        foreach (var value in new[] { 0.0001m, 0.00001m, 0.3m, 1m / 3m, 2m / 3m, 33.3m, 33.4m, 66.6m, 66.7m, 99_999m, 999_999m, 1_234_567.891m, 4_294_967_295m }) yield return value;
        if (!negative) yield break;
        for (var step = 1; step <= 2_000; step++) yield return -step * 0.61m;
        yield return -64m;
        yield return -0.0001m;
    }

    public static void TickRateContractAtThirtyEqualsTheFormerFormulas()
    {
        var time = SimulationRate.Hz30.Time;
        Check.That(SimulationRate.OriginalFramesPerSecond == 30 && SimulationRate.Original == SimulationRate.Hz30 && time.TicksPerSecond == 30 && time.TicksPerOriginalFrame == 1,
            "The original rate is 30 Hz, one tick per logic frame.");
        var compared = 0;
        for (var milliseconds = 0L; milliseconds <= 120_000; milliseconds++)
        {
            Check.That(time.DurationMillisecondsToTicks(milliseconds) == FormerTiming.DurationMillisecondsToTicks(milliseconds), $"{milliseconds} ms (integer) differs from the former formula.");
            compared++;
        }
        foreach (var value in RateSweep(negative: false))
        {
            Check.That(time.DurationMillisecondsToTicks(value) == FormerTiming.DurationMillisecondsToTicks(value), $"{value} ms (decimal) differs from the former formula.");
            Check.That(time.DurationMillisecondsToFixedTicks(value) == FormerTiming.DurationMillisecondsToFixedTicks(value), $"{value} ms (unrounded) differs from the former formula.");
            Check.That(time.SecondsToTicks(value) == FormerTiming.SecondsToTicks(value), $"{value} s differs from the former formula.");
            compared += 3;
        }
        foreach (var value in RateSweep(negative: true))
        {
            Check.That(time.PerSecondToPerTick(value) == FormerTiming.PerSecondToPerTick(value), $"{value} per second differs from the former formula.");
            Check.That(time.PerSecondSquaredToPerTickSquared(value) == FormerTiming.PerSecondSquaredToPerTickSquared(value), $"{value} per second² differs from the former formula.");
            Check.That(time.DegreesPerSecondToRadiansPerTick(value) == FormerTiming.DegreesPerSecondToRadiansPerTick(value), $"{value} degrees per second differs from the former formula.");
            // What modules computed by hand: value / Fixed.FromInt(30).
            var raw = Fixed.FromDecimal(value);
            Check.That(time.PerSecondToPerTick(raw) == raw / Fixed.FromInt(30) && raw / time.TicksPerSecondFixed == raw / Fixed.FromInt(30), $"{value}: the run-time division differs from / Fixed.FromInt(30).");
            // Raw values per frame are untouched at 30 Hz: bit-identical to reading the raw value.
            Check.That(time.PerOriginalFrameToPerTick(value) == Fixed.FromDecimal(value) && time.PerOriginalFrameToPerTick(raw).Raw == raw.Raw, $"{value} per frame is changed at 30 Hz.");
            Check.That(time.PerOriginalFrameSquaredToPerTickSquared(value) == Fixed.FromDecimal(value) && time.PerOriginalFrameSquaredToPerTickSquared(raw).Raw == raw.Raw, $"{value} per frame² is changed at 30 Hz.");
            compared += 6;
        }
        for (var frames = -1_000L; frames <= 100_000; frames += 7)
        {
            Check.That(time.OriginalFrames(frames) == frames && time.ToOriginalFrames(frames) == frames, $"{frames} frames are not {frames} ticks at 30 Hz.");
            compared++;
        }
        Check.That(time.WholeSecondsToTicks(7) == 210 && time.ToWholeSeconds(13_529) == 450 && time.ToWholeMinutes(13_529) == 7 && time.ToMilliseconds(45) == 1_500 && time.TicksPerMinute == 1_800,
            "Whole seconds, minutes and milliseconds at 30 Hz.");
        Check.That(time.ToSeconds(45) == Fixed.FromDecimal(1.5m) && time.ToSecondsRational(45) == (45, 30), "Game seconds as a fixed-point value and as a rational.");
        Check.That(compared > 150_000, $"The sweep compared {compared} values.");
        Check.Throws<ArgumentOutOfRangeException>(() => time.DurationMillisecondsToTicks(-1L), "negative");
        Check.Throws<ArgumentOutOfRangeException>(() => time.DurationMillisecondsToTicks(-0.5m), "negative");
        Check.Throws<ArgumentOutOfRangeException>(() => time.SecondsToTicks(-1m), "negative");
        Check.Throws<OverflowException>(() => time.DurationMillisecondsToTicks(long.MaxValue), "");
    }

    public static void TickRateContractAtSixtyAndOneTwenty()
    {
        var thirty = SimulationRate.Hz30.Time;
        foreach (var (rate, factor) in new[] { (SimulationRate.Hz60, 2), (SimulationRate.Hz120, 4) })
        {
            var time = rate.Time;
            Check.That(time.TicksPerSecond == 30 * factor && time.TicksPerOriginalFrame == factor && time.TicksPerMinute == 1_800 * factor && !rate.IsOriginal, $"{rate}: ticks per second and per frame.");
            // Durations stay on the grid of the original: the 30 Hz tick count times the factor, never a finer rounding.
            for (var milliseconds = 0L; milliseconds <= 20_000; milliseconds++)
                Check.That(time.DurationMillisecondsToTicks(milliseconds) == factor * thirty.DurationMillisecondsToTicks(milliseconds), $"{rate}: {milliseconds} ms leaves the 30 Hz grid.");
            Check.That(time.DurationMillisecondsToTicks(50) == 2 * factor && time.DurationMillisecondsToTicks(1) == factor && time.DurationMillisecondsToTicks(0) == 0, $"{rate}: 50 ms is two frames of the original.");
            foreach (var value in RateSweep(negative: false))
            {
                Check.That(time.DurationMillisecondsToTicks(value) == factor * thirty.DurationMillisecondsToTicks(value), $"{rate}: {value} ms (decimal) leaves the 30 Hz grid.");
                Check.That(time.SecondsToTicks(value) == factor * thirty.SecondsToTicks(value), $"{rate}: {value} s leaves the 30 Hz grid.");
                Check.That(time.DurationMillisecondsToFixedTicks(value) == Fixed.FromDecimal(value * (30 * factor) / 1000m), $"{rate}: {value} ms unrounded.");
            }
            foreach (var value in RateSweep(negative: true))
            {
                Check.That(time.PerSecondToPerTick(value) == Fixed.FromDecimal(value / (30 * factor)), $"{rate}: {value} per second.");
                Check.That(time.PerSecondSquaredToPerTickSquared(value) == Fixed.FromDecimal(value / (900 * factor * factor)), $"{rate}: {value} per second².");
                Check.That(time.DegreesPerSecondToRadiansPerTick(value) == Fixed.FromDecimal(value * 3.1415926535897932384626433833m / 180m / (30 * factor)), $"{rate}: {value} degrees per second.");
                Check.That(time.PerOriginalFrameToPerTick(value) == Fixed.FromDecimal(value / factor), $"{rate}: {value} per frame.");
                Check.That(time.PerOriginalFrameSquaredToPerTickSquared(value) == Fixed.FromDecimal(value / (factor * factor)), $"{rate}: {value} per frame².");
                var raw = Fixed.FromDecimal(value);
                Check.That(time.PerOriginalFrameToPerTick(raw) == raw / Fixed.FromInt(factor) && time.PerOriginalFrameSquaredToPerTickSquared(raw) == raw / Fixed.FromInt(factor * factor), $"{rate}: {value} per frame (fixed).");
            }
            for (var frames = 0L; frames <= 10_000; frames++)
                Check.That(time.OriginalFrames(frames) == frames * factor && time.ToOriginalFrames(frames * factor) == frames && time.ToOriginalFrames(frames * factor + factor - 1) == frames
                    && time.IsOriginalFrameBoundary(frames * factor) && !time.IsOriginalFrameBoundary(frames * factor + 1), $"{rate}: {frames} frames.");
            // Integer division of the original's rate keeps its value in frames: 30 / 4 = 7 frames, so 14 ticks at 60 Hz (not 60 / 4 = 15).
            Check.That(time.OriginalFrames(SimulationRate.OriginalFramesPerSecond / 4) == 7 * factor, $"{rate}: 30 / 4 frames.");
            Check.That(time.WholeSecondsToTicks(7) == 210 * factor && time.ToWholeSeconds(450 * 30 * factor + 1) == 450 && time.ToMilliseconds(45 * factor) == 1_500 && time.ToSeconds(45 * factor) == Fixed.FromDecimal(1.5m),
                $"{rate}: whole seconds and milliseconds.");
            Check.Throws<OverflowException>(() => time.OriginalFrames(long.MaxValue / 2 + 1), "");
        }
    }

    public static void TickRateSupportedRates()
    {
        Check.That(ReferenceEquals(SimulationRate.FromTicksPerSecond(30), SimulationRate.Hz30) && ReferenceEquals(SimulationRate.FromTicksPerSecond(60), SimulationRate.Hz60)
            && ReferenceEquals(SimulationRate.FromTicksPerSecond(120), SimulationRate.Hz120), "There is one instance per supported rate.");
        foreach (var unsupported in new long[] { 0, 1, 15, 29, 31, 50, 59, 90, 119, 240, -30 })
        {
            Check.That(!SimulationRate.IsSupported(unsupported), $"{unsupported} Hz must not be supported.");
            Check.Throws<UnsupportedSimulationRateException>(() => SimulationRate.FromTicksPerSecond(unsupported), "Unsupported simulation rate");
        }
        const string fingerprint = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        Check.That(SimulationRate.Hz30.Combine(fingerprint) == fingerprint, "At the original rate the definition fingerprint is unchanged.");
        var sixty = SimulationRate.Hz60.Combine(fingerprint);
        var oneTwenty = SimulationRate.Hz120.Combine(fingerprint);
        Check.That(sixty.Length == 64 && oneTwenty.Length == 64 && sixty != fingerprint && oneTwenty != fingerprint && sixty != oneTwenty, "At 60 and 120 Hz the rate is part of the fingerprint.");
        Check.That(SimulationRate.Hz60.Combine(fingerprint) == sixty, "The combined fingerprint is a pure function.");
    }

    public static void TickRateCatalogIsBuiltPerRate()
    {
        var thirty = new SyntheticFixture(SimulationRate.Hz30);
        Check.That(thirty.Rate == SimulationRate.Hz30 && thirty.Templates.Rate == SimulationRate.Hz30 && thirty.Templates.Time == SimulationRate.Hz30.Time, "The fixture's catalog knows its rate.");
        foreach (var (rate, factor) in new[] { (SimulationRate.Hz60, 2), (SimulationRate.Hz120, 4) })
        {
            var fixture = new SyntheticFixture(rate);
            Check.That(fixture.Templates.Rate == rate && fixture.Fingerprint != thirty.Fingerprint, $"{rate}: the catalog carries the rate and another fingerprint.");
            Check.That(fixture.Templates.Templates.Length == thirty.Templates.Templates.Length && fixture.Templates.Weapons.Weapons.Length == thirty.Templates.Weapons.Weapons.Length, $"{rate}: the same definitions.");
            var buildTimes = 0;
            foreach (var template in thirty.Templates.Templates)
            {
                var other = fixture.Templates[template.Index];
                Check.That(other.Name == template.Name && other.BuildTicks == factor * template.BuildTicks, $"{rate}: BuildTime of {template.Name} is {other.BuildTicks}, expected {factor} x {template.BuildTicks}.");
                if (template.BuildTicks > 0) buildTimes++;
                if (template.Locomotor is { } locomotor)
                    Check.That(other.Locomotor is { } scaled && scaled.Speed < locomotor.Speed && scaled.Acceleration <= locomotor.Acceleration && scaled.CloseEnoughDist == locomotor.CloseEnoughDist,
                        $"{rate}: the locomotor of {template.Name} is per tick of the faster rate, distances are unchanged.");
            }
            Check.That(buildTimes > 0, "Fixture: there are build times to compare.");
            foreach (var weapon in thirty.Templates.Weapons.Weapons)
            {
                var other = fixture.Templates.Weapons.Weapons[weapon.Index];
                Check.That(other.ClipReloadTicks == factor * weapon.ClipReloadTicks && other.AttackRange == weapon.AttackRange && other.PrimaryDamage == weapon.PrimaryDamage,
                    $"{rate}: weapon {weapon.Name} reloads in {other.ClipReloadTicks} ticks, expected {factor} x {weapon.ClipReloadTicks}; range and damage are unchanged.");
            }
            Check.That(fixture.Templates.GravityPerTickSquared == rate.Time.PerSecondSquaredToPerTickSquared(fixture.Templates.Gravity) && fixture.Templates.Gravity == thirty.Templates.Gravity,
                $"{rate}: gravity per tick² follows the rate; gravity per second² is data.");
        }
    }

    private static readonly System.Collections.Immutable.ImmutableArray<PlayerConfiguration> RatePlayers = [new PlayerConfiguration(new PlayerId(1), "A"), new PlayerConfiguration(new PlayerId(2), "B")];

    public static void TickRateWorldRefusesACatalogOfAnotherRate()
    {
        var thirty = new SyntheticFixture(SimulationRate.Hz30);
        var sixty = new SyntheticFixture(SimulationRate.Hz60);
        var oneTwenty = new SyntheticFixture(SimulationRate.Hz120);
        SimulationWorld World(SimulationRate rate, SyntheticFixture fixture) => new(new WorldConfiguration(new WorldId(1), rate, 1, RatePlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        Check.Throws<ArgumentException>(() => World(SimulationRate.Hz30, sixty), "built for 60 Hz");
        Check.Throws<ArgumentException>(() => World(SimulationRate.Hz60, thirty), "built for 30 Hz");
        Check.Throws<ArgumentException>(() => World(SimulationRate.Hz120, sixty), "configured for 120 Hz");
        foreach (var (rate, fixture) in new[] { (SimulationRate.Hz30, thirty), (SimulationRate.Hz60, sixty), (SimulationRate.Hz120, oneTwenty) })
        {
            var world = World(rate, fixture);
            Check.That(world.Rate == rate && world.Time == rate.Time && ((CnCNew.Simulation.Modules.IWorldServices)world).Time == rate.Time, $"{rate}: the world carries the rate and its time service.");
            for (var tick = 0; tick < 200; tick++) world.Step();
            Check.That(world.Tick == 200, $"{rate}: the world steps.");
        }
    }

    public static void TickRateSnapshotsCarryTheirRate()
    {
        var thirty = new SyntheticFixture(SimulationRate.Hz30);
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate);
            var scenario = new StressScenario(ShortOptions);
            var replay = scenario.Record(fixture);
            Check.That(replay.Header.TickRate == rate.TicksPerSecond, $"{rate}: the replay header carries the rate.");
            var world = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
            Check.That(world.Rate == rate, $"{rate}: the world of a replay runs at the replay's rate.");
            ReplayRunner.Run(replay, world, new ReplayRunOptions { StopTick = 1_200 });
            var bytes = WorldSnapshot.Write(world);
            var text = Encoding.UTF8.GetString(bytes);
            Check.That(text.Contains($"\"tickRate\":{rate.TicksPerSecond},", StringComparison.Ordinal), $"{rate}: the snapshot carries the rate.");

            // Round trip: the restored world runs at the stored rate and continues identically.
            var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint);
            Check.That(restored.Rate == rate && restored.StateHash == world.StateHash && restored.Tick == world.Tick, $"{rate}: the snapshot restores at its rate.");
            Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), $"{rate}: a restored world writes the same bytes.");
            var straight = ReplayRunner.Run(replay, world).FinalHash;
            Check.That(ReplayRunner.Run(replay, restored).FinalHash == straight, $"{rate}: the continuation from the snapshot ends in the same hash.");

            // A catalog of another rate is refused, never stepped.
            try { WorldSnapshot.Restore(bytes, thirty.Templates, fixture.Fingerprint); throw new InvalidOperationException("A 30 Hz catalog restored a snapshot of another rate."); }
            catch (SnapshotFormatException exception) { Check.That(exception.Message.Contains("catalog was built for 30 Hz", StringComparison.Ordinal), "The rate mismatch is named: " + exception.Message); }
            // An unsupported rate is refused whatever the catalog.
            var fifty = Encoding.UTF8.GetBytes(text.Replace($"\"tickRate\":{rate.TicksPerSecond},", "\"tickRate\":50,", StringComparison.Ordinal));
            try { WorldSnapshot.Restore(fifty, fixture.Templates, fixture.Fingerprint); throw new InvalidOperationException("A 50 Hz snapshot was accepted."); }
            catch (SnapshotFormatException exception) { Check.That(exception.Message.Contains("Unsupported tick rate 50", StringComparison.Ordinal), "The unsupported rate is named: " + exception.Message); }
        }

        // 30 Hz: the snapshot of a 30 Hz world is refused by a 60 Hz catalog as well.
        var (fixture30, replay30, _) = ShortReplay(rate: SimulationRate.Hz30);
        var world30 = ReplayRunner.CreateWorld(replay30.Header, fixture30.Templates);
        ReplayRunner.Run(replay30, world30, new ReplayRunOptions { StopTick = 300 });
        var sixty = new SyntheticFixture(SimulationRate.Hz60);
        Check.Throws<SnapshotFormatException>(() => WorldSnapshot.Restore(WorldSnapshot.Write(world30), sixty.Templates, fixture30.Fingerprint), "written at 30 Hz");
    }

    public static void TickRateReplaysCarryTheirRate()
    {
        var thirty = new SyntheticFixture(SimulationRate.Hz30);
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate);
            var recorded = new StressScenario(ShortOptions).Record(fixture);
            var replay = ReplayFormat.Read(ReplayFormat.Write(recorded));
            Check.That(replay.Header.TickRate == rate.TicksPerSecond && ReplayRunner.RateOf(replay.Header) == rate, $"{rate}: the rate survives the replay file.");
            var first = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true, VerifyIncrementalEvery = 50 });
            var second = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
            Check.That(first.TicksExecuted == ShortOptions.Ticks && first.FinalHash == second.FinalHash && first.TickHashes.AsSpan().SequenceEqual(second.TickHashes), $"{rate}: two runs of the replay are identical.");

            // The catalog must be one of the replay's rate; an existing world of another rate is refused as well (audit W8).
            Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(replay, thirty.Templates, fixture.Fingerprint), "catalog was built for 30 Hz");
            var world30 = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Hz30, replay.Header.Seed, replay.Header.Players, SyntheticFixture.FixtureId, thirty.Fingerprint), thirty.Templates);
            Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(replay, world30), "the world runs at 30 Hz");
            Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(replay, fixture.Templates, thirty.Fingerprint), "fingerprint");
        }

        // An unsupported rate is refused before any world exists.
        var (fixture30, replay30, _) = ShortReplay(rate: SimulationRate.Hz30);
        var fifty = ReplayFormat.Read(ReplayFormat.Write(replay30 with { Header = replay30.Header with { TickRate = 50 } }));
        Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(fifty, fixture30.Templates, fixture30.Fingerprint), "50 Hz");
        Check.Throws<ReplayRejectedException>(() => ReplayRunner.CreateWorld(fifty.Header, fixture30.Templates), "50 Hz");
        Check.Throws<ReplayRejectedException>(() => ReplayRunner.CheckCompatibility(fifty.Header, fixture30.Fingerprint), "50 Hz");
        // A 30 Hz replay with a header that claims 60 Hz meets a 30 Hz catalog: refused by the catalog check.
        var claimed = ReplayFormat.Read(ReplayFormat.Write(replay30 with { Header = replay30.Header with { TickRate = 60 } }));
        Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(claimed, fixture30.Templates, fixture30.Fingerprint), "catalog was built for 30 Hz");
    }

    public static void TickRateMissionSnapshotsCarryTheirRate()
    {
        var scripts = new[] { Scr.Script("S", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_COUNTER", Scr.Counter("c"), Scr.Int(1))], oneShot: true) };
        var (_, fixture30, _) = MissionMap(scripts, rate: SimulationRate.Hz30);
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (map, fixture, terrain) = MissionMap(scripts, rate: rate);
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            Check.That(session.World.Rate == rate, $"{rate}: a mission runs at the rate of its catalog.");
            Run(session, 40);
            var bytes = MissionSnapshot.Write(session);
            var text = Encoding.UTF8.GetString(bytes);
            Check.That(text.StartsWith("{\"format\":", StringComparison.Ordinal) && text.Contains($"\"tickRate\":{rate.TicksPerSecond},\"programFingerprint\"", StringComparison.Ordinal), $"{rate}: the mission state carries the rate.");
            var restored = MissionSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, terrain, map);
            Check.That(restored.World.Rate == rate && restored.StateHash() == session.StateHash() && restored.Engine.CounterValue("c") == 1, $"{rate}: the mission state restores at its rate.");
            Run(session, 60);
            Run(restored, 60);
            Check.That(restored.StateHash() == session.StateHash(), $"{rate}: the restored mission continues identically.");

            try { MissionSnapshot.Restore(bytes, fixture30.Templates, fixture.Fingerprint, terrain, map); throw new InvalidOperationException("A 30 Hz catalog restored a mission state of another rate."); }
            catch (SnapshotFormatException exception) { Check.That(exception.Message.Contains("catalog was built for 30 Hz", StringComparison.Ordinal), "The rate mismatch is named: " + exception.Message); }
            var fifty = Encoding.UTF8.GetBytes(text.Replace($"\"tickRate\":{rate.TicksPerSecond},\"programFingerprint\"", "\"tickRate\":50,\"programFingerprint\"", StringComparison.Ordinal));
            try { MissionSnapshot.Restore(fifty, fixture.Templates, fixture.Fingerprint, terrain, map); throw new InvalidOperationException("A 50 Hz mission state was accepted."); }
            catch (SnapshotFormatException exception) { Check.That(exception.Message.Contains("Unsupported tick rate 50", StringComparison.Ordinal), "The unsupported rate is named: " + exception.Message); }
        }
    }

    /// <summary>
    /// The product's default rate is not part of any stored state (docs/TICKRATE-MIGRATION.md §15.5): a replay, a world snapshot and a mission snapshot written at 30 Hz
    /// name 30 Hz, their tick values (end tick, checkpoint interval, the mission's <c>maxTicks</c>) are ticks of that rate, and they run on at 30 Hz with a 30 Hz catalog
    /// whatever rate a new session would get. A catalog of any other rate - the default rate included, when it is not 30 Hz - refuses them with a message that names both rates;
    /// nothing is reinterpreted. The fixtures here are written at 30 Hz by name, so the test says the same before and after the default changes.
    /// </summary>
    public static void TickRateStatesWrittenAtThirtyOutliveTheDefault()
    {
        var written = SimulationRate.Hz30;
        var others = new[] { SimulationRate.Hz60, SimulationRate.Hz120 }.Append(TestRate.Product).Where(rate => rate != written).Distinct().ToArray();

        // Replay: the header's tick values are ticks at the header's rate.
        var (fixture, recorded, _) = ShortReplay(rate: written);
        var replay = ReplayFormat.Read(ReplayFormat.Write(recorded));
        Check.That(replay.Header.TickRate == 30 && replay.Header.EndTick == ShortOptions.Ticks && replay.Header.CheckpointInterval == ShortOptions.CheckpointInterval,
            $"The 30 Hz replay keeps its rate and its tick values ({replay.Header.TickRate} Hz, end {replay.Header.EndTick}, checkpoints every {replay.Header.CheckpointInterval}).");
        var straight = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(straight.TicksExecuted == ShortOptions.Ticks && straight.VerifiedCheckpoints == replay.Checkpoints.Length, $"The 30 Hz replay runs its {ShortOptions.Ticks} ticks at 30 Hz and verifies every checkpoint.");

        // World snapshot, taken half way: continues at 30 Hz to the same end.
        var world = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
        ReplayRunner.Run(replay, world, new ReplayRunOptions { StopTick = ShortOptions.Ticks / 2 });
        var snapshot = WorldSnapshot.Write(world);
        var restored = WorldSnapshot.Restore(snapshot, fixture.Templates, fixture.Fingerprint);
        Check.That(restored.Rate == written && restored.Tick == ShortOptions.Ticks / 2, $"The 30 Hz world state restores at 30 Hz at its tick ({restored.Rate}, tick {restored.Tick}).");
        Check.That(ReplayRunner.Run(replay, restored).FinalHash == straight.FinalHash, "The restored 30 Hz world continues to the hash of the uninterrupted 30 Hz run.");

        // Mission snapshot: maxTicks is a tick budget of the stored rate, presentationLatencyTicks a frame count, combatIntervalTicks a switch - stored as given, read as given.
        var scripts = new[] { Scr.Script("S", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_COUNTER", Scr.Counter("c"), Scr.Int(1))], oneShot: true) };
        var (map, missionFixture, terrain) = MissionMap(scripts, rate: written);
        var options = MissionOptions.Standard with { MaxTicks = 500, PresentationLatencyTicks = 7 };
        var session = MissionSession.Start(options, missionFixture.Templates, missionFixture.Fingerprint, terrain, map);
        Run(session, 200);
        var missionBytes = MissionSnapshot.Write(session);
        var missionText = Encoding.UTF8.GetString(missionBytes);
        Check.That(missionText.Contains("\"tickRate\":30,", StringComparison.Ordinal) && missionText.Contains("\"maxTicks\":500,", StringComparison.Ordinal) && missionText.Contains("\"presentationLatencyTicks\":7,", StringComparison.Ordinal)
            && missionText.Contains("\"combatIntervalTicks\":3,", StringComparison.Ordinal), "The 30 Hz mission state stores its rate and its tick options as they were given.");
        var mission = MissionSnapshot.Restore(missionBytes, missionFixture.Templates, missionFixture.Fingerprint, terrain, map);
        Check.That(mission.World.Rate == written && mission.Options.MaxTicks == 500 && mission.Options.PresentationLatencyTicks == 7 && mission.Options.CombatIntervalTicks == 3 && mission.StateHash() == session.StateHash(),
            $"The 30 Hz mission state restores at 30 Hz with the stored options ({mission.World.Rate}, maxTicks {mission.Options.MaxTicks}).");
        while (!session.IsOver) session.Step();
        while (!mission.IsOver) mission.Step();
        Check.That(mission.World.Tick == 500 && session.World.Tick == 500 && mission.StateHash() == session.StateHash(), $"Both end at the stored budget of 500 ticks of 30 Hz in the same state (tick {mission.World.Tick}).");

        foreach (var other in others)
        {
            var catalog = new SyntheticFixture(other);
            Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(replay, catalog.Templates, fixture.Fingerprint), $"recorded at 30 Hz; the catalog was built for {other}");
            Check.Throws<SnapshotFormatException>(() => WorldSnapshot.Restore(snapshot, catalog.Templates, fixture.Fingerprint), $"written at 30 Hz; the catalog was built for {other}");
            var (_, missionCatalog, _) = MissionMap(scripts, rate: other);
            Check.Throws<SnapshotFormatException>(() => MissionSnapshot.Restore(missionBytes, missionCatalog.Templates, missionFixture.Fingerprint, terrain, map), $"written at 30 Hz; the catalog was built for {other}");
        }
    }
}

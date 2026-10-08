using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;
using CnCNew.Tests;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The flash of a building that is being captured (<c>SelectionFlashTracker</c>, docs/SIM-STAGED-CAPTURE.md): every <c>CaptureFlash</c> event tints the building's drawable in the
/// capturer's saturated house colour, the tint decays within four ticks, the next flash starts it again; nothing else is tinted.
/// </summary>
internal static class CaptureFlashTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
    private static readonly ColorValue Red = new(255, 0, 0);
    private static readonly ColorValue Blue = new(0, 0, 255);

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/CaptureFlashFixture.ini"] = """
        SpecialPower SPECIAL_FlashCapture
          Enum = SPECIAL_INFANTRY_CAPTURE_BUILDING
          ReloadTime = 0
        End
        Object FlashStation
          KindOf = STRUCTURE
          Draw = W3DModelDraw ModuleTag_Draw
            ConditionState = NONE
              Model = FlashStation
            End
          End
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object FlashCapturer
          KindOf = SELECTABLE INFANTRY
          Draw = W3DModelDraw ModuleTag_Draw
            ConditionState = NONE
              Model = FlashCapturer
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SpecialAbility Cap01
            SpecialPowerTemplate = SPECIAL_FlashCapture
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate CapUpdate01
            SpecialPowerTemplate = SPECIAL_FlashCapture
            StartAbilityRange = 150.0
            UnpackTime = 500
            PreparationTime = 2000
            PackTime = 300
            DoCaptureFX = Yes
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("CaptureFlash");
        t.Run("the envelope holds the peak for two ticks and decays in quarters", Envelope);
        t.Run("the saturation is the reference's color * factor - factor / 2", Saturation);
        t.Run("every flash tints the building in the capturer's colour, decays to none and restarts; nothing else is tinted", () => Capture(true));
        t.Run("without selection flash settings no tint is projected", () => Capture(false));
        t.Run("a flash during the decay starts at the peak again; a dead capturer starts nothing", Restart);
        t.Run("a script's NAMED_FLASH plays a colour flash every 15th logic frame, seconds * 2 times, unsaturated", ScriptedFlash);
        t.Run("time scale: every flash of a capture shows the same envelope in game time at 30, 60 and 120 Hz; the sequence itself is the simulation's", TimeScaleBlinkSequence);
        return t.Failures;
    }

    /// <summary>
    /// The blink sequence of a capture as a whole at 30, 60 and 120 Hz (docs/TICKRATE-MIGRATION.md §14.4). The presentation's share is the envelope: every
    /// <c>CaptureFlash</c> event tints the building for the same game time, and nothing is tinted without an event. When the events come is the simulation's
    /// (<c>SpecialAbilityUpdate.StepCaptureFlash</c> advances its phase once per logic frame of the preparation, docs/RATE-INVARIANCE-TURRETS.md): the number of
    /// flashes of one preparation is the same at every rate.
    /// </summary>
    private static void TimeScaleBlinkSequence()
    {
        const double oneFrame = 1.0 / CnCNew.Data.OriginalFrame.PerSecond;
        var envelope = new List<(SimulationRate Rate, double Seconds)>();
        var span = new List<(SimulationRate Rate, double Seconds)>();
        var counts = new List<(SimulationRate Rate, int Count)>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, projector, capturer, station) = Stage(true, rate);
            Assert(world.Submit(new CaptureCommand(new PlayerId(1), 1, world.Tick, capturer, station)).Accepted, $"at {rate} the capture is accepted");
            var flashTicks = new List<long>();
            var tinted = new List<long>();
            for (var step = 0; step < TestTime.Frames(world, 200); step++)
            {
                var result = world.Step();
                projector.Observe(result.Events, world);
                projector.Advance(world);
                if (result.Events.Any(value => value.Kind == SimEventKind.CaptureFlash)) flashTicks.Add(world.Tick);
                if (projector.Project(1f).Drawables.Single(drawable => drawable.Entity == station).HasFlashTint) tinted.Add(world.Tick);
            }

            Assert(flashTicks.Count > 0 && world.FindEntity(station)!.Owner == new PlayerId(1), $"at {rate} the building flashed and changed hands");
            // Every tinted tick lies inside the envelope of a flash, and the envelope after the last flash (nothing restarts it) has its full length.
            var window = world.Time.OriginalFrames(SelectionFlashTracker.LastVisibleAge);
            Assert(tinted.All(tick => flashTicks.Any(flash => tick >= flash && tick <= flash + window + world.Time.TicksPerOriginalFrame - 1)), $"at {rate} the building is tinted only within {SelectionFlashTracker.LastVisibleAge} original frames after a flash");
            var afterLast = tinted.Count(tick => tick >= flashTicks[^1]);
            envelope.Add((rate, (double)afterLast / world.Time.TicksPerSecond));
            span.Add((rate, (double)(tinted[^1] - flashTicks[0]) / world.Time.TicksPerSecond));
            counts.Add((rate, flashTicks.Count));
        }

        string Text(IEnumerable<(SimulationRate Rate, double Seconds)> values) => string.Join(", ", values.Select(value => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{value.Rate}: {value.Seconds:F4} s")));
        Console.WriteLine($"       capture blink: the last flash is shown for {Text(envelope)}; first flash to the end of the last tint {Text(span)}; flashes of one preparation {string.Join(", ", counts.Select(value => $"{value.Rate}: {value.Count}"))}");
        Assert(counts.All(value => value.Count == counts[0].Count), $"one preparation flashes as often at every rate ({string.Join(", ", counts.Select(value => $"{value.Rate}: {value.Count}"))})");
        foreach (var (rate, seconds) in envelope)
            Assert(Math.Abs(seconds - envelope[0].Seconds) <= oneFrame + 1e-9, $"a flash is shown for {seconds:F4} s at {rate} and for {envelope[0].Seconds:F4} s at {envelope[0].Rate}: more than one original frame apart");
    }

    private static void Envelope()
    {
        var values = Enumerable.Range(-1, 8).Select(age => SelectionFlashTracker.Envelope(age)).ToArray();
        Assert(values.SequenceEqual([0f, 1f, 1f, 0.75f, 0.5f, 0.25f, 0f, 0f]), $"ages -1..6 give 0, 1, 1, 3/4, 1/2, 1/4, 0, 0 ({string.Join(" ", values)})");
    }

    private static void Saturation()
    {
        Assert(SelectionFlashTracker.Saturate(Red, 0.5f) == new Vector3(0.25f, -0.25f, -0.25f), "red at the retail factor 0.5 is (0.25, -0.25, -0.25)");
        Assert(SelectionFlashTracker.Saturate(new ColorValue(255, 255, 255), 0.5f) == new Vector3(0.25f), "white at 0.5 is a quarter on every channel");
        Assert(SelectionFlashTracker.Saturate(Blue, 2f) == new Vector3(-1f, -1f, 1f), "blue at 2 is (-1, -1, 1)");
    }

    private static (SimulationWorld World, FrameProjector Projector, EntityId Capturer, EntityId Station) Stage(bool withSettings, SimulationRate? rate = null)
    {
        var fixture = new SyntheticFixture(rate ?? CnCNew.Tests.TestRate.Default, files: Files());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), fixture.Templates.Rate, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
        ulong sequence = 0;
        world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, "FlashCapturer", new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(50), Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, "FlashStation", new PlayerId(2), Fixed.FromInt(200), Fixed.FromInt(50), Fixed.Zero, Fixed.Zero));
        world.Submit(new SetRelationshipCommand(PlayerId.System, ++sequence, world.Tick, new PlayerId(1), new PlayerId(2), Relationship.Enemies));
        world.Step();
        var capturer = world.Entities.Single(entity => entity.Template.Name == "FlashCapturer").Id;
        var station = world.Entities.Single(entity => entity.Template.Name == "FlashStation").Id;
        var colors = TeamColors.Resolve(Players, fixture.Graph, new Dictionary<int, ColorValue> { [1] = Red, [2] = Blue });
        var projector = new FrameProjector(ObjectVisualCatalog.Build(fixture.Graph),
            new FrameProjectorOptions { Colors = colors, SelectionFlash = withSettings ? new SelectionFlashSettings(0.5f) : null });
        projector.Advance(world);
        return (world, projector, capturer, station);
    }

    private static void Capture(bool withSettings)
    {
        var (world, projector, capturer, station) = Stage(withSettings);
        Assert(world.Submit(new CaptureCommand(new PlayerId(1), 1, world.Tick, capturer, station)).Accepted, "the capture is accepted");
        var peak = new Vector3(0.25f, -0.25f, -0.25f);
        var flashTicks = new List<long>();
        var tints = new Dictionary<long, Vector3>();
        for (var step = 0; step < TestTime.Frames(world, 200); step++)
        {
            var result = world.Step();
            projector.Observe(result.Events, world);
            projector.Advance(world);
            if (result.Events.Any(value => value.Kind == SimEventKind.CaptureFlash)) flashTicks.Add(projector.Tick);
            var frame = projector.Project(1f);
            var building = frame.Drawables.Single(drawable => drawable.Entity == station);
            tints[projector.Tick] = building.FlashTint;
            Assert(building.HasFlashTint == (building.FlashTint != Vector3.Zero), "HasFlashTint follows the tint");
            Assert(frame.Drawables.Where(drawable => drawable.Entity != station).All(drawable => !drawable.HasFlashTint), "only the building is tinted");
            Assert(projector.Project(0.5f).Drawables.Single(drawable => drawable.Entity == station).FlashTint == building.FlashTint, "the tint steps with the tick, not with the frame");
        }

        Assert(flashTicks.Count == 4, $"a preparation of 60 frames flashes four times ({string.Join(",", flashTicks)})");
        if (!withSettings)
        {
            Assert(tints.Values.All(tint => tint == Vector3.Zero), "no settings, no tint");
            return;
        }

        Assert(tints.Where(pair => pair.Key < flashTicks[0]).All(pair => pair.Value == Vector3.Zero), "no tint before the first flash");
        for (var index = 0; index < flashTicks.Count; index++)
        {
            var tick = flashTicks[index];
            var next = index + 1 < flashTicks.Count ? flashTicks[index + 1] : long.MaxValue;
            // The envelope is counted in logic frames of the original: age n is the tick n frames after the flash (the tick itself at 30 Hz).
            float[] expected = [1f, 1f, 0.75f, 0.5f, 0.25f, 0f, 0f];
            for (var age = 0; age < expected.Length && tick + TestTime.Frames(world, age) < next; age++)
                Assert(tints[tick + TestTime.Frames(world, age)] == peak * expected[age], $"flash {index + 1} at tick {tick}: age {age} shows {expected[age]} of the capturer's red ({tints[tick + TestTime.Frames(world, age)]})");
            if (index > 0) Assert(tints[tick] == peak, $"flash {index + 1} starts at the peak again");
        }

        var visible = TestTime.Frames(world, SelectionFlashTracker.LastVisibleAge + 1) - 1; // the last tick of the last visible frame after a flash
        Assert(flashTicks[1] - flashTicks[0] > visible && tints[flashTicks[1] - 1] == Vector3.Zero, "between the first two flashes the tint is gone");
        Assert(tints.Where(pair => pair.Key > flashTicks[^1] + visible).All(pair => pair.Value == Vector3.Zero), "no tint after the last flash has decayed");
        Assert(world.FindEntity(station)!.Owner == new PlayerId(1), "the building changed hands");
    }

    /// <summary><c>ScriptActions::doNamedFlash</c> + <c>Drawable::update</c>: the flash count is <c>seconds * 30 / 15</c>, a flash starts on every logic frame divisible by 15 while any are left, with the colour as it is.</summary>
    private static void ScriptedFlash()
    {
        var tracker = new SelectionFlashTracker();
        var white = Vector3.One;
        tracker.StartScripted(7, white, 2);
        Assert(tracker.ScriptedPending == 1 && tracker.TintAt(7, 0) == Vector3.Zero, "nothing happens until the clock reaches a flash frame");
        var starts = new List<long>();
        for (long tick = 1; tick <= 100; tick++)
        {
            var before = tracker.TintAt(7, tick - 1);
            tracker.Advance(tick);
            var tint = tracker.TintAt(7, tick);
            if (tint == white && before != white) starts.Add(tick);
            if (tick == 46) Assert(tracker.ScriptedPending == 1, "one flash of two seconds is still to come");
            if (tick == 61) Assert(tracker.ScriptedPending == 0, "all four flashes of two seconds are used up");
        }
        Assert(starts.SequenceEqual([15L, 30L, 45L, 60L]), $"two seconds are four flashes on frames 15, 30, 45 and 60 ({string.Join(", ", starts)})");
        Assert(tracker.TintAt(7, 60) == white && tracker.TintAt(7, 61) == white && tracker.TintAt(7, 62) == white * 0.75f && tracker.TintAt(7, 64) == white * 0.25f && tracker.TintAt(7, 65) == Vector3.Zero, "each flash holds the colour two ticks and decays in quarters");
        tracker.StartScripted(8, Vector3.One, 0);
        Assert(tracker.ScriptedPending == 0, "no seconds, no flash");
        tracker.StartScripted(9, Vector3.One, 5);
        tracker.Prune(70, id => id != 9);
        Assert(tracker.ScriptedPending == 0, "a flash of an object that is gone is forgotten");
    }

    private static void Restart()
    {
        var (world, _, capturer, station) = Stage(true);
        var colors = TeamColors.Resolve(Players, null, new Dictionary<int, ColorValue> { [1] = Red, [2] = Blue });
        var settings = new SelectionFlashSettings(0.5f);
        var tracker = new SelectionFlashTracker();
        SimEvent Flash(long tick, EntityId other) => new(new EventId(1), tick, SimEventKind.CaptureFlash, station, other, PlayerId.System, string.Empty, 0);
        var peak = SelectionFlashTracker.Saturate(Red, 0.5f);
        long F(long originalFrames) => TestTime.Frames(world, originalFrames); // the tick of an event in original frame n; TintAt and Prune take the original frame itself

        tracker.Observe(Flash(F(10), capturer), world, colors, settings);
        Assert(tracker.TintAt(station.Value, 9) == Vector3.Zero && tracker.TintAt(station.Value, 10) == peak && tracker.TintAt(station.Value, 13) == peak * 0.5f, "the first flash decays");
        tracker.Observe(Flash(F(13), capturer), world, colors, settings);
        Assert(tracker.TintAt(station.Value, 13) == peak && tracker.TintAt(station.Value, 15) == peak * 0.75f, "a flash during the decay restarts the envelope at the peak");
        Assert(tracker.TintAt(capturer.Value, 13) == Vector3.Zero, "the capturer itself does not flash");

        tracker.Observe(Flash(F(40), new EntityId(999)), world, colors, settings);
        Assert(tracker.TintAt(station.Value, 40) == Vector3.Zero, "a flash whose capturer is gone has no colour and starts nothing");
        tracker.Observe(new SimEvent(new EventId(2), F(50), SimEventKind.EntityDied, station, capturer, PlayerId.System, string.Empty, 0), world, colors, settings);
        Assert(tracker.TintAt(station.Value, 50) == Vector3.Zero, "other events start nothing");

        tracker.Observe(Flash(F(60), capturer), world, colors, settings);
        tracker.Prune(62, _ => true);
        Assert(tracker.TintAt(station.Value, 62) == peak * 0.75f, "pruning keeps a running flash");
        tracker.Prune(70, _ => true);
        Assert(tracker.TintAt(station.Value, 62) == Vector3.Zero, "pruning forgets a finished flash");
    }
}

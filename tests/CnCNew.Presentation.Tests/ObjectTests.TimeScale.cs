using System.Globalization;
using System.Numerics;
using CnCNew.Data;
using CnCNew.Data.World;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The time scale of the object presentation (docs/HANDOFF-PHASE21.md, item 9): what an object shows for a duration lasts the same game time at 30, 60 and 120 Hz. Each
/// test plays the same scene once per rate on a synthetic fixture, measures the duration in ticks of that world, converts it with the world's own rate and compares the
/// seconds; the tolerance is one original frame (1/30 s). Nothing here judges whether the simulation behaves the same at the faster rates - only how long the
/// presentation shows what the simulation did.
/// </summary>
static partial class ObjectTests
{
    private static readonly SimulationRate[] Rates = [SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120];
    private const double OneFrame = 1.0 / OriginalFrame.PerSecond;

    private static void RegisterTimeScale(Action<string, Action> test)
    {
        test("Time scale: the muzzle flash and the FIRING state last the same game time at 30, 60 and 120 Hz", TimeScaleMuzzleFlash);
        test("Time scale: the healing icon stays three seconds after the last heal at 30, 60 and 120 Hz", TimeScaleHealingIcon);
        test("Time scale: a scripted flash blinks at the same game times and decays over the same time at 30, 60 and 120 Hz", TimeScaleScriptedFlash);
        test("Time scale: the chassis settles onto a slope in the same game time at 30, 60 and 120 Hz", TimeScaleSuspension);
    }

    private static IReadOnlyDictionary<string, string> TimeScaleFiles() => new Dictionary<string, string>(Files())
    {
        ["Data/INI/Object/TimeScaleFixture.ini"] = """
        Object Patient
          KindOf = SELECTABLE VEHICLE
          Geometry = BOX
          GeometryMajorRadius = 3.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 2.0
          VisionRange = 50.0
          BuildTime = 1
          Draw = W3DModelDraw ModuleTag_Draw
            DefaultConditionState
              Model = PatientBody
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AutoHealBehavior Heal01
            StartsActive = Yes
            HealingAmount = 1
            HealingDelay = 20000
            StartHealingDelay = 2000
          End
        End
        """,
    };

    private sealed class TimeScaleRig
    {
        public TimeScaleRig(SimulationRate rate, TerrainMap? terrain = null)
        {
            var fixture = new SyntheticFixture(rate, files: TimeScaleFiles());
            Catalog = ObjectVisualCatalog.Build(fixture.Graph);
            World = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain?.Fingerprint ?? ""), fixture.Templates, terrain);
            Projector = new FrameProjector(Catalog, new FrameProjectorOptions { SelectionFlash = new SelectionFlashSettings(0.5f) });
        }

        public ObjectVisualCatalog Catalog { get; }
        public SimulationWorld World { get; }
        public FrameProjector Projector { get; }

        /// <summary>One simulation tick and its capture; the frame is the one a display would draw right after it.</summary>
        public RenderFrame Step()
        {
            var result = World.Step();
            Projector.Observe(result.Events, World);
            Projector.Advance(World);
            return Projector.Project(1f);
        }

        public double Seconds(long ticks) => (double)ticks / World.Time.TicksPerSecond;
        public long Ticks(double seconds) => (long)Math.Round(seconds * World.Time.TicksPerSecond);
    }

    private static string Format(IEnumerable<(SimulationRate Rate, double Seconds)> values)
        => string.Join(", ", values.Select(value => string.Create(CultureInfo.InvariantCulture, $"{value.Rate}: {value.Seconds:F4} s")));

    private static void SameWithinOneFrame(string what, List<(SimulationRate Rate, double Seconds)> values)
    {
        Console.WriteLine($"       {what}: {Format(values)}");
        var reference = values[0].Seconds;
        Check.That(reference > 0, $"{what}: the reference duration at {values[0].Rate} is positive ({reference}).");
        foreach (var (rate, seconds) in values)
            Check.That(Math.Abs(seconds - reference) <= OneFrame + 1e-9, $"{what} lasts {seconds:F4} s at {rate} and {reference:F4} s at {values[0].Rate}: more than one original frame apart.");
    }

    private static void TimeScaleMuzzleFlash()
    {
        var flash = new List<(SimulationRate, double)>();
        var firing = new List<(SimulationRate, double)>();
        foreach (var rate in Rates)
        {
            var rig = new TimeScaleRig(rate);
            var rover = Spawn(rig.World, "Rover", 1, 10, 10);
            var target = Spawn(rig.World, "Patient", 2, 50, 10);
            rig.Step();
            Submit(rig.World, sequence => new FireWeaponCommand(new PlayerId(1), sequence, rig.World.Tick, rover, target));
            long flashTicks = 0, firingTicks = 0;
            var seen = false;
            // A quarter of a second: the first shot and its flash, before the clip's second shot (DelayBetweenShots 300 ms).
            for (var tick = 0; tick < rig.Ticks(0.25); tick++)
            {
                var drawable = rig.Step().Drawables.First(value => value.Entity == rover);
                if (drawable.HasFired(0)) { flashTicks++; seen = true; }
                else if (seen) break;
                if (drawable.Conditions.Contains(ModelConditions.Firing[0])) firingTicks++;
            }

            flash.Add((rate, rig.Seconds(flashTicks)));
            firing.Add((rate, rig.Seconds(firingTicks)));
        }

        SameWithinOneFrame("muzzle flash after one shot", flash);
        SameWithinOneFrame("FIRING_A after one shot", firing);
        Check.That(firing.All(value => Math.Abs(value.Item2 - OneFrame) < 1e-9), $"FIRING shows for exactly one original frame at every rate ({Format(firing)}).");
    }

    private static void TimeScaleHealingIcon()
    {
        var shown = new List<(SimulationRate, double)>();
        foreach (var rate in Rates)
        {
            var rig = new TimeScaleRig(rate);
            var rover = Spawn(rig.World, "Rover", 1, 10, 10);
            var patient = Spawn(rig.World, "Patient", 2, 50, 10);
            var entity = rig.World.FindEntity(patient)!;
            // The patient heals itself two seconds after it appears and every twenty seconds from then on: hurt it shortly before the second heal, so exactly one heal
            // raises its health in the run (and the icon is never shown in the first three seconds of a game, Drawable::drawHealing).
            for (var tick = 0; tick < rig.Ticks(20); tick++) rig.Step();
            Submit(rig.World, sequence => new FireWeaponCommand(new PlayerId(1), sequence, rig.World.Tick, rover, patient));
            var health = entity.Body!.Health;
            long healedAt = -1, lastShown = -1;
            for (var tick = 0; tick < rig.Ticks(8); tick++)
            {
                var drawable = rig.Step().Drawables.First(value => value.Entity == patient);
                // The one heal after the shot; the next is twenty seconds away.
                if (entity.Body.Health > health && healedAt < 0) healedAt = rig.World.Tick;
                health = entity.Body.Health;
                if (drawable.ShowsHealing) lastShown = rig.World.Tick;
            }

            Check.That(healedAt > 0 && lastShown > healedAt, $"At {rate} the patient is healed once while it is still hurt and shows the icon (healed at tick {healedAt}, last shown at {lastShown}).");
            shown.Add((rate, rig.Seconds(lastShown - healedAt)));
        }

        SameWithinOneFrame("healing icon after the last heal", shown);
        Check.That(shown.All(value => Math.Abs(value.Item2 - 3.0) <= OneFrame + 1e-9), $"The icon stays three seconds ({Format(shown)}).");
    }

    private static void TimeScaleScriptedFlash()
    {
        var first = new List<(SimulationRate, double)>();
        var last = new List<(SimulationRate, double)>();
        var single = new List<(SimulationRate, double)>();
        var counts = new List<int>();
        foreach (var rate in Rates)
        {
            var rig = new TimeScaleRig(rate);
            var rover = Spawn(rig.World, "Rover", 1, 10, 10);
            // Start on a whole second so every rate asks at the same game time.
            while (rig.World.Tick % rig.World.Time.TicksPerSecond != 0) rig.Step();
            var start = rig.World.Tick;
            Check.That(rig.Projector.FlashScripted(rover, Vector3.One, 2, rig.World), "The flash is accepted.");
            long firstOn = -1, lastOn = -1, firstOff = -1;
            var edges = 0;
            var wasOn = false;
            for (var tick = 0; tick < rig.Ticks(4); tick++)
            {
                var on = rig.Step().Drawables.First(value => value.Entity == rover).HasFlashTint;
                if (on && !wasOn) edges++;
                if (on && firstOn < 0) firstOn = rig.World.Tick;
                if (!on && wasOn && firstOff < 0) firstOff = rig.World.Tick;
                if (on) lastOn = rig.World.Tick;
                wasOn = on;
            }

            counts.Add(edges);
            first.Add((rate, rig.Seconds(firstOn - start)));
            last.Add((rate, rig.Seconds(lastOn - start)));
            single.Add((rate, rig.Seconds(firstOff - firstOn)));
        }

        Check.That(counts.All(count => count == 4), $"Two seconds are four flashes at every rate ({string.Join(", ", counts)}).");
        SameWithinOneFrame("first flash after the request", first);
        SameWithinOneFrame("end of the last flash after the request", last);
        SameWithinOneFrame("one flash from its peak to nothing", single);
    }

    /// <summary>A 20 x 20 cell map that rises along x.</summary>
    private static TerrainMap SlopedTerrain()
    {
        const int cells = 20, border = 1, width = cells + 2 * border;
        var stride = (width + 7) / 8;
        var blend = new MapBlendTiles(8, [], [], [], [], [..new byte[stride * width]], stride, 0, 0, [], [], [], []);
        var heights = new byte[width * width];
        for (var y = 0; y < width; y++)
            for (var x = 0; x < width; x++)
                heights[y * width + x] = (byte)(4 * x);
        var map = new MapData("synthetic", new MapHeightField(width, width, border, [new MapBoundary(cells, cells)], [..heights]), blend, MapProperties.Empty, [], [], [], [], [], [], []);
        return TerrainMap.Build(map, "slope");
    }

    private static void TimeScaleSuspension()
    {
        var settle = new List<(SimulationRate, double)>();
        var rest = new List<float>();
        var motion = new SuspensionMotion(SuspensionKind.Wheels, 0.1f, 0.1f, 0.9f, 0.9f, 4f, 4f, 1f, 0.3f, 0f, true, 0.4f, -2.3f);
        foreach (var rate in Rates)
        {
            var rig = new TimeScaleRig(rate, SlopedTerrain());
            var rover = Spawn(rig.World, "Rover", 1, 100, 100);
            var entity = rig.World.FindEntity(rover)!;
            var visual = rig.Catalog["Rover"] with { Suspension = motion };
            var tracker = new SuspensionTracker();
            var start = rig.World.Tick;
            var poses = new List<(long Tick, float Pitch)>();
            for (var tick = 0; tick < rig.Ticks(6); tick++)
            {
                rig.World.Step();
                poses.Add((rig.World.Tick, tracker.Update(entity, visual, rig.World, 50f)!.Value.Pitch));
            }

            var final = poses[^1].Pitch;
            Check.That(Math.Abs(final) > 0.01f, $"At {rate} the body leans with the slope ({final} rad).");
            // The spring swings out: the last tick on which the pitch is further than one percent from where it comes to rest.
            var lastOff = poses.Last(pose => Math.Abs(pose.Pitch - final) > Math.Abs(final) * 0.01f).Tick;
            settle.Add((rate, rig.Seconds(lastOff - start)));
            rest.Add(final);
        }

        SameWithinOneFrame("chassis swing-out onto a slope", settle);
        Check.That(rest.All(value => Math.Abs(value - rest[0]) < 1e-5f), $"The body comes to rest at the same pitch at every rate ({string.Join(", ", rest)}).");
    }
}

file static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

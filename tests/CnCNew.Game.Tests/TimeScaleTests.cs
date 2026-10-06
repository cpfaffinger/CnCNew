using System.Globalization;
using System.Numerics;
using CnCNew.Data;
using CnCNew.Data.World;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Fx;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Time;
using CnCNew.Ui.Runtime;
using static CnCNew.Game.Tests.MissionPresentationTests;

namespace CnCNew.Game.Tests;

/// <summary>
/// The time scale of the game layer (docs/HANDOFF-PHASE21.md, item 9): everything a mission shows for a duration, and the clock the effects run on, lasts the same game
/// time at 30, 60 and 120 Hz. Each test plays the same script once per rate, counts the steps of that session, converts them with the session's own rate and compares the
/// seconds; the tolerance is one original frame (1/30 s). The synthetic tests need no content. With <c>--content-dir</c> a staged scene is also played at 30 and at 60 Hz
/// through <see cref="LiveGame"/>: the effect clock steps once per original frame and a particle system lives the same game time.
/// Nothing here judges how the simulation behaves at the faster rates, and nothing here says that it looks or sounds right - only that durations are equal.
/// </summary>
internal static class TimeScaleTests
{
    private static readonly SimulationRate[] Rates = [SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120];
    private const double OneFrame = 1.0 / OriginalFrame.PerSecond;

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(TimeScaleTests));
        harness.Run("A scripted camera move ends after the same game time at 30, 60 and 120 Hz", CameraMove);
        harness.Run("A military caption types, stands and fades over the same game time", MilitaryCaption);
        harness.Run("A message line, the cinematic text and the speech placeholder stay the same game time", Texts);
        harness.Run("A script fade, the letterbox fade and the grey-scale ramp take the same game time", FadesAndFilters);
        harness.Run("A radar event stays the same game time", RadarEvent);
        harness.Run("The game clock counts original frames and seconds the same at every rate", ClockArithmetic);
        var index = Array.IndexOf(args, "--content-dir");
        if (index >= 0 && index + 1 < args.Length)
        {
            var directory = args[index + 1];
            harness.Run("Real data: the effect clock steps once per original frame and a particle system lives the same game time at 30 and 60 Hz", () => EffectsOnTheFrameGrid(directory));
        }
        return harness.Failures;
    }

    private sealed class Play(SimulationRate rate, MapScript[] scripts, Func<string, string>? resolve = null)
    {
        public MissionSession Session { get; } = Start(scripts, null, rate: rate);
        public MissionPresentationDirector? DirectorOrNull { get; private set; }
        public long Steps { get; private set; }

        public MissionPresentationDirector Director => DirectorOrNull ??= new MissionPresentationDirector(Session, resolve)
        {
            TextMeasure = new ShellTextMeasure(), TextStyle = TextFixtures.Style, TextScaleOptions = TextFixtures.Options,
        };

        public void Step()
        {
            Director.BeforeStep();
            Session.Step();
            Director.AfterStep();
            Steps++;
        }

        public double Seconds => (double)Steps / rate.TicksPerSecond;

        /// <summary>Steps until <paramref name="reached"/> holds and returns the game seconds since the start; fails after <paramref name="limitSeconds"/>.</summary>
        public double Until(Func<MissionPresentationDirector, bool> reached, double limitSeconds, string what)
        {
            while (!reached(Director))
            {
                TestHarness.Assert(Seconds < limitSeconds, $"at {rate}: {what} was not reached within {limitSeconds} s");
                Step();
            }
            return Seconds;
        }
    }

    private static void Same(string what, IEnumerable<(SimulationRate Rate, double Seconds)> measured, double? expected = null)
    {
        var values = measured.ToArray();
        Console.WriteLine("       " + what + ": " + string.Join(", ", values.Select(value => string.Create(CultureInfo.InvariantCulture, $"{value.Rate}: {value.Seconds:F4} s"))));
        var reference = values[0].Seconds;
        TestHarness.Assert(reference > 0, $"{what}: the duration at {values[0].Rate} is positive");
        foreach (var (rate, seconds) in values)
            TestHarness.Assert(Math.Abs(seconds - reference) <= OneFrame + 1e-9, $"{what} takes {seconds:F4} s at {rate} and {reference:F4} s at {values[0].Rate}: more than one original frame apart");
        if (expected is { } target)
            foreach (var (rate, seconds) in values)
                TestHarness.Assert(Math.Abs(seconds - target) <= 2 * OneFrame + 1e-9, $"{what} takes {seconds:F4} s at {rate}, expected about {target} s");
    }

    private static IEnumerable<(SimulationRate, double)> PerRate(Func<SimulationRate, double> measure) => Rates.Select(rate => (rate, measure(rate))).ToArray();

    private static MapScriptParameter Label(string label) => Named(MapScriptParameterType.ObjectType, label);

    private static void CameraMove()
    {
        var waypoint = Waypoint("Target", 1, 400m, 500m);
        var intro = Script("Intro", If("CONDITION_TRUE"), [Do("MOVE_CAMERA_TO", Named(MapScriptParameterType.Waypoint, "Target"), Real(5m), Real(0), Real(0), Real(0))]);
        var followUp = Script("FollowUp", If("CAMERA_MOVEMENT_FINISHED"), [Do("SET_FLAG", Named(MapScriptParameterType.Flag, "camera_done"), Bool(true))], oneShot: false);
        double Measure(SimulationRate rate, bool scriptSide)
        {
            var session = Start([intro, followUp], [waypoint], MissionOptions.Standard with { PresentationLatencyTicks = 1_000_000 }, rate);
            var director = new MissionPresentationDirector(session);
            long steps = 0;
            void Step() { director.BeforeStep(); session.Step(); director.AfterStep(); steps++; }
            Step();
            TestHarness.Assert(director.CameraMoveActive, $"at {rate} the move is active after the first step");
            while (scriptSide ? !session.Engine.FlagValue("camera_done") : director.CameraMoveActive)
            {
                TestHarness.Assert(steps < 10 * rate.TicksPerSecond, $"at {rate} the camera move never ended");
                Step();
            }
            return (double)steps / rate.TicksPerSecond;
        }

        Same("camera move of 5 s until the director ends it", PerRate(rate => Measure(rate, false)), 5.0);
        Same("camera move of 5 s until CAMERA_MOVEMENT_FINISHED reaches the scripts", PerRate(rate => Measure(rate, true)), 5.0);
    }

    private static void MilitaryCaption()
    {
        var script = Script("Caption", If("CONDITION_TRUE"), [Do("SHOW_MILITARY_CAPTION", Label("MAP:Caption"), Int(5000))]);
        string Resolve(string label) => label == "MAP:Caption" ? "Title\nBody" : label;
        var first = new List<(SimulationRate, double)>();
        var typed = new List<(SimulationRate, double)>();
        var gone = new List<(SimulationRate, double)>();
        foreach (var rate in Rates)
        {
            var play = new Play(rate, [script], Resolve);
            play.Step();
            TestHarness.Assert(play.Director.MilitaryCaption is not null, $"at {rate} the caption exists after the first step");
            first.Add((rate, play.Until(director => director.TypingClicks >= 1, 5, "the first typed character")));
            typed.Add((rate, play.Until(director => director.TypingClicks >= 9, 10, "the last typed character")));
            gone.Add((rate, play.Until(director => director.MilitaryCaption is null, 20, "the end of the caption")));
            TestHarness.Assert(play.Director.TypingClicks == 9, $"at {rate} nine characters are typed, one click each ({play.Director.TypingClicks})");
        }

        Same("military caption: first character (MilitaryCaptionDelayMS 750)", first);
        Same("military caption: all nine characters typed", typed);
        Same("military caption of 5000 ms: gone after its fade", gone);
    }

    private static void Texts()
    {
        var text = Script("Text", If("CONDITION_TRUE"), [Do("DISPLAY_TEXT", Label("GUI:Line"))]);
        Same("message line until it has faded away", PerRate(rate =>
        {
            var play = new Play(rate, [text]);
            play.Step();
            TestHarness.Assert(play.Director.Messages is { IsEmpty: false }, $"at {rate} the line is there after the first step");
            return play.Until(director => director.Messages is { IsEmpty: true }, 30, "the end of the message line");
        }));

        var cinematic = Script("Cine", If("CONDITION_TRUE"), [Do("DISPLAY_CINEMATIC_TEXT", Label("GUI:Cine"), Named(MapScriptParameterType.ObjectType, "Arial - Size:12 [Bold]"), Int(4))]);
        Same("cinematic text of 4 s", PerRate(rate =>
        {
            var play = new Play(rate, [cinematic]);
            play.Step();
            TestHarness.Assert(play.Director.Cinematic is not null, $"at {rate} the cinematic text is there after the first step");
            return play.Until(director => director.Cinematic is null, 10, "the end of the cinematic text");
        }), 4.0);

        var speech = Script("Speech", If("CONDITION_TRUE"), [Do("SPEECH_PLAY", Label("VoiceEvent01"))]);
        Same("speech placeholder (three seconds)", PerRate(rate =>
        {
            var play = new Play(rate, [speech]);
            play.Step();
            TestHarness.Assert(play.Director.SubtitleEvent is not null, $"at {rate} the speech placeholder is there after the first step");
            return play.Until(director => director.SubtitleEvent is null, 10, "the end of the speech placeholder");
        }), 3.0);
    }

    private static void FadesAndFilters()
    {
        // Thirty frames up, thirty held, thirty down: frame counts of the map, original frames at every rate.
        var fade = Script("Fade", If("CONDITION_TRUE"), [Do("CAMERA_FADE_SUBTRACT", Real(0), Real(1), Int(30), Int(30), Int(30))]);
        static bool Covered(MissionPresentationDirector director, byte atLeast)
            => director.BuildOverlayCommands(1280, 720).OfType<UiFillRect>().Any(fill => fill.Rect.Width == 1280 && fill.Rect.Height == 720 && fill.Color.A >= atLeast);
        var full = new List<(SimulationRate, double)>();
        var over = new List<(SimulationRate, double)>();
        foreach (var rate in Rates)
        {
            var play = new Play(rate, [fade]);
            play.Step();
            full.Add((rate, play.Until(director => Covered(director, 255), 5, "the top of the fade")));
            play.Until(director => !Covered(director, 255), 5, "the end of the hold");
            over.Add((rate, play.Until(director => !Covered(director, 1), 5, "the end of the fade")));
        }
        Same("script fade: black after thirty frames", full, 1.0);
        Same("script fade: over after ninety frames", over, 3.0);

        var letterbox = Script("Letterbox", If("CONDITION_TRUE"), [Do("CAMERA_LETTERBOX_BEGIN")]);
        Same("letterbox fade to full (LETTER_BOX_FADE_TIME 1000 ms)", PerRate(rate =>
        {
            var play = new Play(rate, [letterbox]);
            play.Step();
            TestHarness.Assert(play.Director.Letterbox, $"at {rate} the letterbox is on after the first step");
            return play.Until(director => director.LetterboxFadeLevel >= 1f, 5, "the full letterbox");
        }), 1.0);

        var greyScale = Script("Grey", If("CONDITION_TRUE"), [Do("CAMERA_BW_MODE_BEGIN", Int(30))]);
        Same("grey-scale ramp of thirty frames", PerRate(rate =>
        {
            var play = new Play(rate, [greyScale]);
            play.Step();
            return play.Until(director => director.PostEffects.BlackWhite >= 1f, 5, "the full grey scale");
        }), 1.0);
    }

    private static void RadarEvent()
    {
        var radar = Script("Radar", If("CONDITION_TRUE"), [Do("RADAR_CREATE_EVENT", Coord(123m, 456m, 0m), Int(3))]);
        Same("radar event (eight seconds)", PerRate(rate =>
        {
            var play = new Play(rate, [radar]);
            play.Step();
            TestHarness.Assert(play.Director.RadarBlips.Length == 1, $"at {rate} the blip is there after the first step");
            return play.Until(director => director.RadarBlips.Length == 0, 20, "the end of the radar event");
        }), 8.0);
    }

    private static void ClockArithmetic()
    {
        foreach (var rate in Rates)
        {
            var perFrame = rate.TicksPerOriginalFrame;
            for (long tick = -3; tick < 500; tick++)
            {
                var clock = new CnCNew.Presentation.GameClock(tick, rate.TicksPerSecond, 0);
                TestHarness.Assert(clock.OriginalFrames == (long)Math.Floor(tick / (double)perFrame), $"{rate}: tick {tick} lies in original frame {Math.Floor(tick / (double)perFrame)} (got {clock.OriginalFrames})");
                TestHarness.Assert(clock.IsOriginalFrameBoundary == (clock.OriginalFrames * perFrame == tick), $"{rate}: tick {tick} begins a frame exactly when it is a multiple of {perFrame}");
                if (tick >= 0) TestHarness.Assert(clock.IsOriginalFrameBoundary == rate.Time.IsOriginalFrameBoundary(tick), $"{rate}: the clock and the time service agree on the frame boundaries (tick {tick})");
                if (tick >= 0) TestHarness.Assert(clock.OriginalFrames == rate.Time.ToOriginalFrames(tick), $"{rate}: the clock and the time service agree on the frame number (tick {tick})");
            }
            TestHarness.Assert(new CnCNew.Presentation.GameClock(rate.TicksPerSecond * 7L, rate.TicksPerSecond, 0.5).Seconds == 7 + 0.5 / rate.TicksPerSecond, $"{rate}: seconds are ticks and fraction over the rate");
            TestHarness.Assert(new CnCNew.Presentation.GameClock(rate.TicksPerSecond * 7L, rate.TicksPerSecond, 0).OriginalFrames == 7 * OriginalFrame.PerSecond, $"{rate}: seven seconds are 210 original frames");
        }
        // At the original's rate the frame is the tick: this is why nothing changes at 30 Hz.
        for (long tick = -3; tick < 100; tick++)
            TestHarness.Assert(new CnCNew.Presentation.GameClock(tick, 30, 0) is { IsOriginalFrameBoundary: true } clock && clock.OriginalFrames == tick, $"30 Hz: tick {tick} is original frame {tick}");
    }

    private static void EffectsOnTheFrameGrid(string directory)
    {
        var lifetimes = new List<(SimulationRate, double)>();
        var counts = new List<long>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60 })
        {
            var data = GameData.Load(directory, modelGeometry: false, rate: rate);
            TestHarness.Assert(data.Rate == rate, $"the data is built for {rate}");
            // The Ranger only walks for the first seconds: no effect of the scene itself is in the way.
            var live = LiveScenario.Create(data, DemoScenarios.CaptureBuilding, new Vector2(1280, 720));
            TestHarness.Assert(live.World.Time.Rate == rate, $"the world of the scene ticks at {rate}");
            var step = CnCNew.Tests.TestTime.TickSeconds(live.World);
            // The effect clock: one step per original frame of game time, whatever the tick rate.
            var frameAtStart = live.Fx.Frame;
            var tickAtStart = live.Tick;
            for (var tick = 0; tick < 2 * rate.TicksPerSecond; tick++)
            {
                live.Advance(step);
                var expected = frameAtStart + live.Clock.OriginalFrames - GameClockFrames(tickAtStart, rate);
                TestHarness.Assert(live.Fx.Frame == expected, $"at {rate}, tick {live.Tick}: the effect clock is at frame {live.Fx.Frame}, game time is at original frame {expected}");
            }
            TestHarness.Assert(live.Tick - tickAtStart == 2 * rate.TicksPerSecond, $"at {rate} one Advance of one tick length steps exactly one tick ({live.Tick - tickAtStart} ticks in {2 * rate.TicksPerSecond} steps)");

            // A particle system with a finite life, spawned by hand at the camera target: how long until nothing of it is left.
            var template = data.Fx.ParticleSystems.Values.OrderBy(value => value.Name, StringComparer.Ordinal)
                .First(value => value.SystemLifetime is > 0 and < 120 && value.Lifetime.High is > 0 and < 120 && value.BurstCount.High > 0);
            TestHarness.Assert(live.Fx.Particles.SystemCount == 0, $"at {rate} the scene has no effect of its own yet");
            TestHarness.Assert(live.Fx.Particles.Spawn(template, new ParticleSpawn(new Vector3(live.Anchor.X, live.Anchor.Y, 20f))) != 0, $"the system {template.Name} is created");
            long ticks = 0;
            var peak = 0;
            while (live.Fx.Particles.SystemCount > 0 || live.Fx.Particles.ParticleCount > 0)
            {
                live.Advance(step);
                ticks++;
                peak = Math.Max(peak, live.Fx.Particles.ParticleCount);
                TestHarness.Assert(ticks < 20 * rate.TicksPerSecond, $"at {rate} the particle system {template.Name} never ended");
            }
            TestHarness.Assert(peak > 0, $"at {rate} the system {template.Name} emitted particles");
            counts.Add(peak);
            lifetimes.Add((rate, (double)ticks / rate.TicksPerSecond));
            Console.WriteLine($"       {rate}: particle system {template.Name} (SystemLifetime {template.SystemLifetime} frames), peak {peak} particles");
        }

        Same("particle system from its creation to its last particle", lifetimes);
        TestHarness.Assert(counts.Distinct().Count() == 1, $"the same particles exist at both rates (peaks {string.Join(", ", counts)})");
    }

    private static long GameClockFrames(long tick, SimulationRate rate) => CnCNew.Presentation.GameClock.OriginalFrameOf(tick, rate.TicksPerOriginalFrame);
}

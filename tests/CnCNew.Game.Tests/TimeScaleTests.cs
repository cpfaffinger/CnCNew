using System.Globalization;
using System.Numerics;
using CnCNew.Data;
using CnCNew.Data.World;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Fx;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Wnd;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;
using static CnCNew.Game.Tests.MissionPresentationTests;

namespace CnCNew.Game.Tests;

/// <summary>
/// The time scale of the game layer (docs/handoffs/HANDOFF-PHASE21.md, item 9): everything a mission shows for a duration, and the clock the effects run on, lasts the same game
/// time at 30, 60 and 120 Hz. Each test plays the same script once per rate, counts the steps of that session, converts them with the session's own rate and compares the
/// seconds; the tolerance is one original frame (1/30 s). The synthetic tests need no content. With <c>--content-dir</c> a staged scene is also played at 30, 60 and 120 Hz
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
        harness.Run("The under-attack line of the message feed is locked out for the same game time (ten seconds)", UnderAttackLine);
        harness.Run("A superweapon line counts the same seconds down and flashes in the same rhythm", SuperweaponLine);
        harness.Run("A named countdown timer reads the same time and flashes in the same rhythm", NamedCountdown);
        harness.Run("The zoom blur of a camera jump rises and falls over the same game time", ZoomBlur);
        harness.Run("The game clock counts original frames and seconds the same at every rate", ClockArithmetic);
        var index = Array.IndexOf(args, "--content-dir");
        if (index >= 0 && index + 1 < args.Length)
        {
            var directory = args[index + 1];
            harness.Run("Real data: the effect clock steps once per original frame and a particle system lives the same game time at 30, 60 and 120 Hz", () => EffectsOnTheFrameGrid(directory));
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

    /// <summary><c>Radar::tryUnderAttackEvent</c> as the message list sees it (<see cref="HudMessageFeed"/>): the same object is hit in every tick; its line comes again ten seconds later.</summary>
    private static void UnderAttackLine()
    {
        Same("under-attack line: time until the next one for the same place", PerRate(rate =>
        {
            var (map, fixture, terrain) = Mission([], [Placed("Barrel", 100m, 100m, "teamHero", "Victim")], rate: rate);
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            session.Step();
            var world = session.World;
            var victim = world.Entities.Single(entity => entity.Template.Name == "Barrel");
            var feed = new HudMessageFeed(new UiMessageLog(TextFixtures.Style.Settings), new LocalizedText(new StringTable("time-scale", StringTableFormat.Csf, [])), fixture.Graph);
            long first = -1, second = -1;
            for (var tick = world.Tick; tick <= world.Tick + 12 * rate.TicksPerSecond && second < 0; tick++)
            {
                var before = feed.Shown;
                feed.Observe(new SimEvent(default, tick, SimEventKind.EntityDamaged, victim.Id, default, victim.Owner, string.Empty, 100), world, victim.Owner);
                if (feed.Shown == before) continue;
                if (first < 0) first = tick;
                else second = tick;
            }
            TestHarness.Assert(first == world.Tick && second > first, $"at {rate} the first hit prints a line and a later one again (first {first}, second {second})");
            return (double)(second - first) / rate.TicksPerSecond;
        }), 10.0);
    }

    /// <summary>The superweapon list (<see cref="SuperweaponTimers"/>): the seconds left of a recharging power and the flash of a ready one.</summary>
    private static void SuperweaponLine()
    {
        var files = new Dictionary<string, string>(SyntheticFixture.Files)
        {
            ["Data/INI/Object/TimeScaleClock.ini"] = "SpecialPower SPECIAL_Clock\n  Enum = SPECIAL_PARADROP_AMERICA\n  ReloadTime = 10000\n  PublicTimer = Yes\nEnd\n"
                + "Object ClockCenter\n  KindOf = STRUCTURE\n  Body = StructureBody Body01\n    MaxHealth = 500.0\n    InitialHealth = 500.0\n  End\n"
                + "  Behavior = SpecialAbility Clock01\n    SpecialPowerTemplate = SPECIAL_Clock\n  End\nEnd\n",
        };
        var halfway = new List<(SimulationRate, double)>();
        var ready = new List<(SimulationRate, double)>();
        var flash = new List<(SimulationRate, double)>();
        var readings = new List<string>();
        foreach (var rate in Rates)
        {
            var fixture = new SyntheticFixture(rate, files: files);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, [new PlayerConfiguration(new PlayerId(1), "Alpha")], SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            TestHarness.Assert(world.Submit(new SpawnCommand(PlayerId.System, 1, world.Tick, "ClockCenter", new PlayerId(1), Fixed.FromInt(50), Fixed.FromInt(50), Fixed.Zero, Fixed.Zero)).Accepted, $"at {rate} the carrier spawns");
            world.Step();
            var timers = new SuperweaponTimers();
            var measure = new ShellTextMeasure();
            var own = new WndColor(10, 200, 10, 255);
            long fiveAt = -1, readyAt = -1, whiteAt = -1;
            var perSecond = new List<string>();
            for (var tick = 0; tick < 13 * rate.TicksPerSecond; tick++)
            {
                var line = timers.Collect(world, _ => "Clock").Single();
                if (world.Tick % rate.TicksPerSecond == 0) perSecond.Add(line.Time);
                if (fiveAt < 0 && line.SecondsLeft <= 5) fiveAt = world.Tick;
                if (readyAt < 0 && line.Ready) readyAt = world.Tick;
                // The list is drawn on the game clock in original frames, as MatchController does.
                var frame = new CnCNew.Presentation.GameClock(world.Tick, rate.TicksPerSecond, 0).OriginalFrames;
                var colour = ((UiTextDraw)SuperweaponTimers.Draw([line], _ => own, frame, 1280, 720, 560, measure, TextFixtures.Style, TextFixtures.Options)[0]).Color;
                TestHarness.Assert(line.Ready || colour == own, $"at {rate} a recharging power does not flash (tick {world.Tick})");
                if (readyAt >= 0 && whiteAt < 0 && colour == WndColor.White) whiteAt = world.Tick;
                world.Step();
            }
            TestHarness.Assert(fiveAt > 0 && readyAt > fiveAt && whiteAt >= readyAt, $"at {rate} the power counts down, gets ready and flashes (5 s left at tick {fiveAt}, ready at {readyAt}, white at {whiteAt})");
            halfway.Add((rate, (double)fiveAt / rate.TicksPerSecond));
            ready.Add((rate, (double)readyAt / rate.TicksPerSecond));
            flash.Add((rate, (double)whiteAt / rate.TicksPerSecond));
            readings.Add(string.Join(" ", perSecond));
        }

        Same("superweapon line: five seconds left", halfway);
        Same("superweapon line: ready (ReloadTime 10000 ms)", ready, 10.0);
        Same("superweapon line: first white flash of the ready line (SuperweaponCountdownFlashDuration 500 ms)", flash);
        TestHarness.Assert(readings.Distinct().Count() == 1, $"the line reads the same on every whole second at every rate ({string.Join(" | ", readings)})");
    }

    /// <summary>A countdown timer of the scripts on the screen (<c>DISPLAY_COUNTDOWN_TIMER</c>): the value the VM counts in ticks reads the same m:ss, and the ready timer flashes on the same clock.</summary>
    private static void NamedCountdown()
    {
        var script = Script("Timer", If("CONDITION_TRUE"), [Do("SET_MILLISECOND_TIMER", Named(MapScriptParameterType.Counter, "T"), Real(5m)), Do("DISPLAY_COUNTDOWN_TIMER", Named(MapScriptParameterType.Counter, "T"), Label("GUI:Time"))]);
        var normal = new WndColor(255, 255, 0, 255);
        var flashColour = new WndColor(0, 255, 255, 255);
        var three = new List<(SimulationRate, double)>();
        var zero = new List<(SimulationRate, double)>();
        var flash = new List<(SimulationRate, double)>();
        var readings = new List<string>();
        foreach (var rate in Rates)
        {
            var play = new Play(rate, [script]);
            play.Step();
            UiTextDraw Timer() => play.Director.BuildOverlayCommands(1280, 720).OfType<UiTextDraw>().Single(draw => draw.Text.StartsWith("GUI:Time", StringComparison.Ordinal));
            TestHarness.Assert(Timer().Text is "GUI:Time 0:05" or "GUI:Time 0:04", $"at {rate} the timer of five seconds is on the screen ({Timer().Text})");
            var perSecond = new List<string>();
            double threeAt = -1, zeroAt = -1, flashAt = -1;
            // Game time since the step that set the timer.
            var set = play.Seconds;
            while (play.Seconds < 8)
            {
                var timer = Timer();
                if (play.Steps % rate.TicksPerSecond == 0) perSecond.Add(timer.Text);
                if (threeAt < 0 && timer.Text == "GUI:Time 0:03") threeAt = play.Seconds - set;
                if (zeroAt < 0 && timer.Text == "GUI:Time 0:00") zeroAt = play.Seconds - set;
                TestHarness.Assert(zeroAt >= 0 || timer.Color == normal, $"at {rate} a running timer keeps its colour ({timer.Text})");
                if (zeroAt >= 0 && flashAt < 0 && timer.Color == flashColour) flashAt = play.Seconds - set;
                play.Step();
            }
            TestHarness.Assert(threeAt > 0 && zeroAt > threeAt && flashAt >= zeroAt, $"at {rate} the timer runs down and flashes (0:03 at {threeAt} s, 0:00 at {zeroAt} s, flash colour at {flashAt} s)");
            three.Add((rate, threeAt));
            zero.Add((rate, zeroAt));
            flash.Add((rate, flashAt));
            readings.Add(string.Join(" ", perSecond.Select(text => text["GUI:Time ".Length..])));
        }

        Same("named countdown of 5 s: reads 0:03", three);
        Same("named countdown of 5 s: reads 0:00", zero);
        Same("named countdown: first flash colour after it ran out (NamedTimerCountdownFlashDuration 500 ms)", flash);
        TestHarness.Assert(readings.Distinct().Count() == 1, $"the timer reads the same on every whole second at every rate ({string.Join(" | ", readings)})");
    }

    /// <summary><c>CAMERA_MOTION_BLUR_JUMP</c>: the blur count rises by five per original frame to sixty, the camera cuts, and it falls again (<c>ScreenMotionBlurFilter</c>).</summary>
    private static void ZoomBlur()
    {
        var target = Waypoint("Target", 1, 400m, 500m);
        var jump = Script("Jump", If("CONDITION_TRUE"), [Do("CAMERA_MOTION_BLUR_JUMP", Named(MapScriptParameterType.Waypoint, "Target"), Bool(true))]);
        var apex = new List<(SimulationRate, double)>();
        var over = new List<(SimulationRate, double)>();
        foreach (var rate in Rates)
        {
            var session = Start([jump], [target], rate: rate);
            var director = new MissionPresentationDirector(session);
            long steps = 0;
            void Step() { director.BeforeStep(); session.Step(); director.AfterStep(); steps++; }
            // The script runs in this step; the filter advances on the ticks that begin an original frame.
            Step();
            long apexAt = -1, overAt = -1;
            while (overAt < 0)
            {
                TestHarness.Assert(steps < 5 * rate.TicksPerSecond, $"at {rate} the zoom blur never ended");
                var count = director.PostEffects.MotionBlurCount;
                TestHarness.Assert(count % 5 == 0 && count <= 60, $"at {rate} the blur count moves in steps of five up to sixty ({count})");
                if (apexAt < 0 && count == 60) apexAt = steps;
                if (apexAt >= 0 && count == 0) overAt = steps;
                else Step();
            }
            apex.Add((rate, (double)apexAt / rate.TicksPerSecond));
            over.Add((rate, (double)overAt / rate.TicksPerSecond));
        }

        Same("zoom blur: at its apex (sixty, twelve frames)", apex, 12.0 / OriginalFrame.PerSecond);
        Same("zoom blur: over (twelve frames up, eleven down)", over);
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
        foreach (var rate in Rates)
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
        TestHarness.Assert(counts.Distinct().Count() == 1, $"the same particles exist at every rate (peaks {string.Join(", ", counts)})");
    }

    private static long GameClockFrames(long tick, SimulationRate rate) => CnCNew.Presentation.GameClock.OriginalFrameOf(tick, rate.TicksPerOriginalFrame);
}

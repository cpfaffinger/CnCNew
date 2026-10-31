using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Game.Campaign;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Mission;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Text;
using CnCNew.Ui.Wnd;
using static CnCNew.Game.Tests.MissionPresentationTests;

namespace CnCNew.Game.Tests;

/// <summary>
/// The mission's on-screen texts the way <c>InGameUI</c> and <c>W3DDisplay</c> draw them (<c>docs/SCRIPT-ENGINE.md</c> §7a, <c>docs/TEXT-AUDIT.md</c> §4.5): the message lines, the typed military caption,
/// named timers and counters, the cinematic text, the popup state. The state machines (<see cref="UiMessageLog"/>, <see cref="MilitaryCaptionState"/>) are tested frame by frame against numbers worked out
/// from the reference; the director tests run a synthetic mission through real steps; the tests with <c>--content-dir</c> run the retail <c>MD_USA01</c> and <c>MD_CHI02</c> through the real HUD.
/// </summary>
internal static class MissionTextTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(MissionTextTests));
        harness.Run("Message lines stack oldest first, alternate their colors, drop the seventh and fade on the original's arithmetic", MessageLines);
        harness.Run("The military caption types after the start delay, one character per two frames, waits at line breaks and blinks its cursor", MilitaryCaptionTimeline);
        harness.Run("The military caption fades after its duration and is removed; an empty text or a duration of 0 is refused with a reason", MilitaryCaptionFadeAndRefusals);
        harness.Run("The caption stops at four lines and drops the rest", MilitaryCaptionLineLimit);
        harness.Run("Messages are drawn from the message position in the data font, one font height apart, colors alternating", MessageOverlay);
        harness.Run("The caption is drawn at its 800x600 position scaled to the screen: title font, body font, cursor block", MilitaryCaptionOverlay);
        harness.Run("Named timers and counters stack upward from their position, right-aligned in the right half, ready font and flash at 0:00", NamedTimerOverlay);
        harness.Run("Cinematic text parses its font, wraps 20 pixels short of the screen, centers at 0.9 of the height and ends after its seconds", CinematicText);
        harness.Run("Without a style or a measure the texts are not drawn and the first one is a reported problem; unresolved labels are counted", NoStyleIsReported);
        harness.Run("A new caption replaces the one on screen; a popup keeps its clamped placement until dismissed", ReplacementAndPopup);
        harness.Run("Fonts, sizes and positions follow the screen size and the text scale", ScalingFollowsTheScreen);
        harness.Run("A speech event shows its subtitle label as a caption for 8 seconds unless the text is missing, empty or starts with an asterisk", SpeechSubtitles);
        harness.Run("Letterbox bars cover what a 16:9 picture leaves free (none at 16:9), fade in over a second, lie over the messages and under the cinematic text; only the top bar fades out", LetterboxFollowsTheOriginal);
        harness.Run("Every typed character of the military caption asks the audio for the typing click; without audio the clicks are counted as not started", TypingClick);

        var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
        if (contentDirectory is not null)
        {
            var data = GameData.Load(contentDirectory, modelGeometry: false, rate: CnCNew.Tests.TestRate.Default);
            harness.Run("Real data: the typed InGameUI values and fonts reach the mission text style", () => RealStyle(data));
            harness.Run("Real data: the MD_USA01 opening types its caption in Courier New through the real HUD", () => RealCaption(data));
            harness.Run("Real data: the MD_CHI02 reinforcement countdown stands at the named timer position", () => RealCountdown(data));
            harness.Run("Real data: the message popup is laid out by InGamePopupMessage.wnd and closed by OK, Enter and Escape", () => RealPopupWindow(data));
            harness.Run("Real data: a pausing popup stops the mission and swallows input until it is closed; one that does not pause lets the world go on", () => RealPopupInTheMission(data));
            harness.Run("Real data: the MD_USA01 caption starts the retail typing click once per character through the real audio director", () => RealTypingClick(data));
            harness.Run("Real data: the mission HUD's own messages share the scripts' message list and stay on screen while the control bar is hidden", () => RealSharedMessageList(data));
        }
        return harness.Failures;
    }

    // ---------------------------------------------------------------------------------------------------- helpers

    private static InGameUiSettings Settings => TextFixtures.Style.Settings;

    /// <summary>
    /// One original frame of the mission (one tick at 30 Hz, two at 60 Hz): every count of this file is a frame count of the original's texts, which the
    /// director advances once per original frame.
    /// </summary>
    private static void Step(MissionPresentationDirector director, CnCNew.Simulation.Mission.MissionSession session)
    {
        for (var tick = 0; tick < CnCNew.Tests.TestTime.Frames(session.World, 1); tick++) StepTick(director, session);
    }

    /// <summary>Exactly one tick: the step in which a script runs, where an expectation is about that step and not about a time.</summary>
    private static void StepTick(MissionPresentationDirector director, CnCNew.Simulation.Mission.MissionSession session)
    {
        director.BeforeStep();
        session.Step();
        director.AfterStep();
    }

    private static (MissionSession Session, MissionPresentationDirector Director) Synthetic(MapScript[] scripts, Func<string, string>? resolve = null, bool styled = true)
    {
        var session = Start(scripts, null);
        var director = new MissionPresentationDirector(session, resolve) { TextMeasure = new ShellTextMeasure() };
        if (styled)
        {
            director.TextStyle = TextFixtures.Style;
            director.TextScaleOptions = TextFixtures.Options;
        }
        return (session, director);
    }

    private static MapScriptParameter Label(string label) => Named(MapScriptParameterType.ObjectType, label);

    private static ImmutableArray<UiTextDraw> Texts(MissionPresentationDirector director, int width = 1280, int height = 720)
        => [..director.BuildOverlayCommands(width, height).OfType<UiTextDraw>()];

    private static UiFont FontOf(InGameUiFontRole role, int width = 1280, int height = 720, UiScaleOptions? options = null)
        => TextFixtures.Style.Font(role, new UiViewport(width, height), options ?? TextFixtures.Options);

    // ---------------------------------------------------------------------------------------------------- the state machines

    private static void MessageLines()
    {
        var log = new UiMessageLog(Settings);
        TestHarness.Assert(log.IsEmpty && log.Lines.IsEmpty, "a new log is empty");
        log.Add("first", 100);
        log.Add("second", 100);
        log.Add("third", 100);
        TestHarness.Assert(log.Lines.Select(line => line.Text).SequenceEqual(["first", "second", "third"]), "the oldest line is first, so it is drawn at the top and the newest at the bottom");
        TestHarness.Assert(log.Lines.Select(line => line.Color).SequenceEqual([Settings.MessageColor1, Settings.MessageColor2, Settings.MessageColor1]), "colors alternate: MessageColor1 unless the previous line has it");
        for (var index = 4; index <= 7; index++) log.Add($"line{index}", 100);
        TestHarness.Assert(log.Lines.Length == UiMessageLog.Capacity && log.Lines[0].Text == "second" && log.Lines[^1].Text == "line7", "the log holds six lines; the seventh pushes the oldest out");

        // MessageDelayMS 75000 / 30 / 1000 is 0 in integer arithmetic: a line loses floor(age * 0.01) alpha per update from its first frame on.
        var fading = new UiMessageLog(Settings);
        fading.Add("fading", 1000);
        var alpha = new Dictionary<int, int>();
        var goneAt = -1;
        for (var age = 1; age <= 400; age++)
        {
            fading.Update(1000 + age);
            if (fading.IsEmpty) { goneAt = age; break; }
            alpha[age] = fading.Lines[0].Color.A;
        }
        TestHarness.Assert(alpha[99] == 255 && alpha[100] == 254 && alpha[199] == 155 && alpha[200] == 153 && alpha[276] == 1, $"alpha 255 until age 99, then -1 per update, from age 200 -2 per update (99:{alpha[99]} 100:{alpha[100]} 199:{alpha[199]} 200:{alpha[200]} 276:{alpha[276]})");
        TestHarness.Assert(goneAt == 277, $"the line is gone when the alpha would go below 0: age 277, about 9.2 seconds ({goneAt})");

        // The color of a new line is decided against the previous line's color at that moment, a faded one included.
        var mixed = new UiMessageLog(Settings);
        mixed.Add("a", 0);
        for (var frame = 1; frame <= 150; frame++) mixed.Update(frame);
        mixed.Add("b", 150);
        TestHarness.Assert(mixed.Lines.Count() == 2 && mixed.Lines[0].Color.A < 255 && mixed.Lines[1].Color == Settings.MessageColor2, "a previous line that has started to fade is not MessageColor1 any more, so the next line takes MessageColor2");
    }

    /// <summary>
    /// <c>W3DDisplay::renderLetterBox</c>: <c>drawFillRect(0, 0, w, (h - 9/16 w) / 2)</c> and the same from the bottom, with the alpha of the fade level (1000 ms); while it fades out
    /// only the top bar is drawn. <c>W3DDisplay::draw</c> renders the bars after the in-game UI and before the cinematic text.
    /// </summary>
    private static void LetterboxFollowsTheOriginal()
    {
        var black = new WndColor(0, 0, 0, 255);
        TestHarness.Assert(MissionPresentationDirector.LetterboxBars(800, 600, true, 1f).SequenceEqual([new UiFillRect(new UiRect(0, 0, 800, 75), black), new UiFillRect(new UiRect(0, 525, 800, 75), black)]), "800x600: two bars of (600 - 450) / 2 = 75 pixels");
        TestHarness.Assert(MissionPresentationDirector.LetterboxBars(1024, 768, true, 1f) is [UiFillRect { Rect: { Y: 0, Height: 96 } }, UiFillRect { Rect: { Y: 672, Height: 96 } }], "1024x768: 96 pixels");
        TestHarness.Assert(MissionPresentationDirector.LetterboxBars(1280, 1024, true, 1f) is [UiFillRect { Rect.Height: 152 }, UiFillRect { Rect: { Y: 872, Height: 152 } }], "1280x1024: 152 pixels");
        TestHarness.Assert(MissionPresentationDirector.LetterboxBars(1280, 720, true, 1f).IsEmpty && MissionPresentationDirector.LetterboxBars(1920, 1080, true, 1f).IsEmpty, "a 16:9 screen has no bars");
        TestHarness.Assert(MissionPresentationDirector.LetterboxBars(2560, 1080, true, 1f).IsEmpty, "a wider screen has none either (the original's rectangles lie outside the screen)");
        TestHarness.Assert(MissionPresentationDirector.LetterboxBars(800, 600, true, 0.5f) is [UiFillRect { Color.A: 127 }, UiFillRect { Color.A: 127 }], "the fade level is the alpha: (Int)(0.5 * 255) = 127");
        TestHarness.Assert(MissionPresentationDirector.LetterboxBars(800, 600, false, 0.5f) is [UiFillRect { Rect: { Y: 0, Height: 75 }, Color.A: 127 }], "fading out, only the top bar is drawn");
        TestHarness.Assert(MissionPresentationDirector.LetterboxBars(800, 600, true, 0f).IsEmpty && MissionPresentationDirector.LetterboxBars(800, 600, false, 0f).IsEmpty, "nothing at level 0");

        var begin = Script("Begin", If("CONDITION_TRUE"),
        [
            Do("CAMERA_LETTERBOX_BEGIN"), Do("DISPLAY_TEXT", Label("MAP:Line")),
            Do("DISPLAY_CINEMATIC_TEXT", Label("MAP:Cine"), Named(MapScriptParameterType.ObjectType, "Arial - Size:12"), Int(30)),
        ]);
        var end = new MapScript("End", string.Empty, string.Empty, string.Empty, true, true, true, true, true, false, 0,
            [new MapOrCondition([If("FLAG", Named(MapScriptParameterType.Flag, "end"), Bool(true))])], [Do("CAMERA_LETTERBOX_END")], []);
        var (session, director) = Synthetic([begin, end]);
        StepTick(director, session); // the fade runs on the presentation clock from the tick the script ran in
        TestHarness.Assert(director.Letterbox && director.LetterboxFadeLevel == 0f, $"the letterbox begins at fade level 0 ({director.LetterboxFadeLevel})");
        TestHarness.Assert(!director.BuildOverlayCommands(800, 600).OfType<UiFillRect>().Any(), "so nothing is drawn in its first step");
        for (var step = 0; step < 15; step++) Step(director, session);
        TestHarness.Assert(director.LetterboxFadeLevel == 0.5f && director.BuildOverlayCommands(800, 600).OfType<UiFillRect>().Count(fill => fill.Color.A == 127 && fill.Rect.Height == 75) == 2, $"half a second later the bars are half there ({director.LetterboxFadeLevel})");
        for (var step = 0; step < 15; step++) Step(director, session);
        var commands = director.BuildOverlayCommands(800, 600);
        var bars = commands.OfType<UiFillRect>().ToArray();
        TestHarness.Assert(director.LetterboxFadeLevel == 1f && bars.Length == 2 && bars.All(bar => bar.Color == black), "after a second they are black");
        var message = commands.IndexOf(commands.OfType<UiTextDraw>().Single(text => text.Text == "MAP:Line"));
        var cinematic = commands.IndexOf(commands.OfType<UiTextDraw>().Single(text => text.Text == "MAP:Cine"));
        var firstBar = commands.IndexOf(bars[0]);
        var lastBar = commands.IndexOf(bars[1]);
        TestHarness.Assert(message < firstBar && lastBar < cinematic, $"the message line is drawn before the bars (they cover it), the cinematic text after them (message {message}, bars {firstBar}/{lastBar}, cinematic {cinematic})");
        TestHarness.Assert(!director.BuildOverlayCommands(1280, 720).OfType<UiFillRect>().Any(), "the same letterbox draws no bar on a 16:9 screen");
        for (var step = 0; step < 40; step++) Step(director, session);
        TestHarness.Assert(director.LetterboxFadeLevel == 1f, "the level stays at 1");

        session.Engine.SignalUiInteraction("end");
        // The fade-out is measured from the tick the script ran in, and the script answers the signal in the VM's next update: the next tick at 30 Hz, up to a frame of ticks away otherwise.
        for (var tick = 0; tick < CnCNew.Tests.TestRate.TicksPerFrame && director.Letterbox; tick++) StepTick(director, session);
        for (var step = 0; step < 15; step++) Step(director, session);
        var closing = director.BuildOverlayCommands(800, 600).OfType<UiFillRect>().ToArray();
        TestHarness.Assert(!director.Letterbox && closing is [{ Rect: { Y: 0, Height: 75 }, Color.A: 127 }], $"ended: the top bar alone fades out ({closing.Length} bars, level {director.LetterboxFadeLevel})");
        for (var step = 0; step < 15; step++) Step(director, session);
        TestHarness.Assert(director.LetterboxFadeLevel == 0f && !director.BuildOverlayCommands(800, 600).OfType<UiFillRect>().Any(), "and is gone a second after the end");
    }

    /// <summary><c>InGameUI::update</c> plays <c>MilitarySubtitlesTyping</c> for every character it appends to the caption (not for a line break).</summary>
    private static void TypingClick()
    {
        var script = Script("Caption", If("CONDITION_TRUE"), [Do("SHOW_MILITARY_CAPTION", Label("MAP:Caption"), Int(5000))]);
        var (session, director) = Synthetic([script], label => label == "MAP:Caption" ? "Title\nBody" : label);
        var played = new List<(long Step, string Action, string Event)>();
        long now = 0;
        director.Audio = (action, name) => { played.Add((now, action, name)); return true; };
        for (now = 1; now <= 22; now++) Step(director, session);
        TestHarness.Assert(played.Count == 0, "no click during the start delay");
        Step(director, session);
        TestHarness.Assert(played is [(23, "SHOW_MILITARY_CAPTION", MissionPresentationDirector.TypingClickEvent)] && MissionPresentationDirector.TypingClickEvent == "MilitarySubtitlesTyping", "the first character clicks at frame 23");
        for (now = 24; now <= 100; now++) Step(director, session);
        TestHarness.Assert(played.Count == 9 && director.TypingClicks == 9 && director.TypingClicksStarted == 9, $"nine characters ('Title' and 'Body'), nine clicks, none for the line break ({played.Count}, {director.TypingClicks}, {director.TypingClicksStarted})");
        TestHarness.Assert(played.Take(5).Select(click => click.Step).SequenceEqual([23, 25, 27, 29, 31]), "one click every second frame while a line types");
        for (now = 101; now <= 260; now++) Step(director, session);
        TestHarness.Assert(played.Count == 9, "no click while the caption stands or fades");
        TestHarness.Assert(director.Report().Any(line => line == "mission caption typing: 9 characters typed, 9 clicks started (MilitarySubtitlesTyping)"), "the report names the numbers");

        var (silentSession, silent) = Synthetic([script], label => label == "MAP:Caption" ? "Title\nBody" : label);
        for (var step = 1; step <= 100; step++) Step(silent, silentSession);
        TestHarness.Assert(silent.TypingClicks == 9 && silent.TypingClicksStarted == 0, "without audio the clicks are counted, none started");
        TestHarness.Assert(silent.Report().Any(line => line == "mission caption typing: 9 characters typed, 0 clicks started (no audio connected)"), "and the report says why");
    }

    private static MilitaryCaptionState Begin(string text, long durationMs, long frame)
        => MilitaryCaptionState.Begin(text, durationMs, frame, Settings, 1, 750, out var problem) ?? throw new InvalidOperationException(problem);

    private static void MilitaryCaptionTimeline()
    {
        // Begin at 100: lifetime 100 + 150 (5000 ms), the first character after 22 frames of delay (incrementOnFrame < frame), then every second frame; the line break costs the delay again.
        var caption = Begin("AB\nCD", 5000, 100);
        string Typed(long frame)
        {
            // the caption is updated once per frame, so the state at a frame is the state after every update up to it
            return string.Join("|", caption.Lines);
        }
        var seen = new SortedDictionary<long, string>();
        for (var frame = 101L; frame <= 160; frame++)
        {
            caption.Update(frame);
            seen[frame] = Typed(frame);
        }
        TestHarness.Assert(seen[122] == "" && seen[123] == "A" && seen[124] == "A" && seen[125] == "AB" && seen[126] == "AB", $"first character at frame 123 (after the 22-frame delay), the next two frames later ({seen[122]}/{seen[123]}/{seen[125]})");
        TestHarness.Assert(seen[127] == "AB|" && seen[149] == "AB|" && seen[150] == "AB|C" && seen[151] == "AB|C" && seen[152] == "AB|CD", $"the line break at 127 waits 22 frames before the next line types ({seen[127]}/{seen[149]}/{seen[150]}/{seen[152]})");
        TestHarness.Assert(caption.TypedCharacters == 4 && caption.Index == 5, "four characters typed, the line break not counted");
        TestHarness.Assert(caption.FullText == "AB\nCD", "the full text is kept");

        // The cursor toggles every ten frames (blockBeginFrame + 9 < frame), one line of text so the line break does not set it.
        var blink = Begin("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 5000, 100);
        var drawn = new SortedDictionary<long, bool>();
        for (var frame = 101L; frame <= 131; frame++)
        {
            blink.Update(frame);
            drawn[frame] = blink.BlockDrawn;
        }
        TestHarness.Assert(drawn[109] && !drawn[110] && !drawn[119] && drawn[120] && drawn[129] && !drawn[130], "the cursor is shown, hidden at frame 110, shown at 120, hidden at 130");
    }

    private static void MilitaryCaptionFadeAndRefusals()
    {
        var caption = Begin("A", 1000, 10);
        TestHarness.Assert(caption.LastFrame == 40, "1000 ms are 30 frames");
        for (var frame = 11L; frame <= 40; frame++) caption.Update(frame);
        TestHarness.Assert(caption.Color.A == 255 && !caption.Finished, "no fade up to the last frame");
        // after the duration the alpha drops by floor(frames past the end * 0.1) per update: nothing for nine frames, then 1, 2, ... (255 - 250 = 5 left after 74 updates)
        var finishedAt = -1L;
        var at325 = -1;
        for (var frame = 41L; frame < 600; frame++)
        {
            caption.Update(frame);
            if (caption.Finished) { finishedAt = frame - 40; break; }
            if (frame - 40 == 75) at325 = caption.Color.A;
        }
        TestHarness.Assert(at325 == 3 && finishedAt == 76, $"alpha 3 after 75 frames of fading, removed at the 76th (alpha {at325}, finished {finishedAt})");

        TestHarness.Assert(MilitaryCaptionState.Begin(string.Empty, 5000, 0, Settings, 1, 750, out var emptyProblem) is null && emptyProblem!.Contains("empty", StringComparison.Ordinal), "an empty text creates no caption, with a reason");
        TestHarness.Assert(MilitaryCaptionState.Begin("x", 0, 0, Settings, 1, 750, out var durationProblem) is null && durationProblem!.Contains("not positive", StringComparison.Ordinal), "a duration of 0 creates no caption, with a reason");
    }

    private static void MilitaryCaptionLineLimit()
    {
        var caption = Begin("1\n2\n3\n4\n5\n6", 600_000, 0);
        for (var frame = 1L; frame <= 800; frame++) caption.Update(frame);
        TestHarness.Assert(caption.Lines.SequenceEqual(["1", "2", "3", "4"]), $"only four lines are typed, the rest is dropped ({string.Join("|", caption.Lines)})");
    }

    // ---------------------------------------------------------------------------------------------------- overlays

    private static void MessageOverlay()
    {
        var script = Script("Talk", If("CONDITION_TRUE"), [Do("DISPLAY_TEXT", Label("GUI:One")), Do("DISPLAY_TEXT", Label("GUI:Two"))]);
        var (session, director) = Synthetic([script], label => label.Replace("GUI:", string.Empty, StringComparison.Ordinal));
        Step(director, session);
        var texts = Texts(director);
        var font = FontOf(InGameUiFontRole.Message);
        var measure = new ShellTextMeasure();
        TestHarness.Assert(texts.Length == 2 && texts[0].Text == "One" && texts[1].Text == "Two", "both lines are drawn, the older one first");
        TestHarness.Assert(texts[0].X == 10 && texts[0].Y == 22 && texts[1].X == 10 && texts[1].Y == 22 + measure.LineHeight(font), $"from MessagePosition (10, 22), unscaled pixels, one font height apart ({texts[0].X},{texts[0].Y} / {texts[1].Y})");
        TestHarness.Assert(texts[0].Font == font && font.Name == "Arial" && font.Bold && font.Size == 14, $"the message font of Language.ini at the adjusted size 14 for 1280 pixels ({texts[0].Font})");
        TestHarness.Assert(texts[0].Color == Settings.MessageColor1 && texts[1].Color == Settings.MessageColor2 && texts[0].ShadowColor == new WndColor(0, 0, 0, 255), "MessageColor1 then MessageColor2, a black shadow");

        // A line that has faded draws with its alpha and a shadow of the same alpha.
        for (var step = 0; step < 150; step++) Step(director, session);
        var faded = Texts(director);
        TestHarness.Assert(faded.Length == 2 && faded[0].Color.A < 255 && faded[0].ShadowColor.A == faded[0].Color.A && faded[0].Color.A > 0, $"after 150 frames the lines have started to fade (alpha {faded[0].Color.A})");
    }

    private static void MilitaryCaptionOverlay()
    {
        var script = Script("Caption", If("CONDITION_TRUE"), [Do("SHOW_MILITARY_CAPTION", Label("MAP:Caption"), Int(5000))]);
        var (session, director) = Synthetic([script], label => label == "MAP:Caption" ? "Title\nBody" : label);
        var measure = new ShellTextMeasure();
        var title = FontOf(InGameUiFontRole.MilitaryCaptionTitle);
        var body = FontOf(InGameUiFontRole.MilitaryCaption);
        TestHarness.Assert(title.Name == "Courier New" && title.Bold && !body.Bold && title.Size == 17 && body.Size == 17, $"Courier New 12 at the adjusted size (title {title}, body {body})");

        for (var step = 1; step <= 22; step++) Step(director, session);
        TestHarness.Assert(director.MilitaryCaption is { } waiting && waiting.Lines.SequenceEqual([""]), "nothing is typed during the 22-frame start delay");
        Step(director, session);
        TestHarness.Assert(director.MilitaryCaption!.Lines.SequenceEqual(["T"]), "the first character at frame 23");
        for (var step = 24; step <= 62; step++) Step(director, session);
        TestHarness.Assert(director.MilitaryCaption!.Lines.SequenceEqual(["Title", "Body"]), $"two lines typed by frame 62 ({string.Join("|", director.MilitaryCaption.Lines)})");

        var commands = director.BuildOverlayCommands(1280, 720);
        var texts = commands.OfType<UiTextDraw>().ToArray();
        TestHarness.Assert(texts.Length == 2 && texts[0].Text == "Title" && texts[1].Text == "Body", "title and body are two draws");
        var x = (int)(10 * 1280 / 800f);
        var y = (int)(340 * 720 / 600f);
        TestHarness.Assert(texts[0].X == x && texts[0].Y == y && x == 16 && y == 408, $"MilitaryCaptionPosition 10,340 scaled from 800x600 to 1280x720 is 16,408 ({texts[0].X},{texts[0].Y})");
        TestHarness.Assert(texts[0].Font == title && texts[1].Font == body && texts[1].X == x && texts[1].Y == y + measure.LineHeight(title), "the title in the title font, the body one title-line height below in the body font");
        TestHarness.Assert(texts[0].Color == Settings.MilitaryCaptionColor && texts[0].ShadowColor == new WndColor(0, 0, 0, 255), "MilitaryCaptionColor, black drop color");
        var blocks = commands.OfType<UiFillRect>().Where(fill => fill.Rect.Y >= y).ToArray();
        TestHarness.Assert(director.MilitaryCaption.BlockDrawn == (blocks.Length == 1), "the cursor block is drawn exactly while the caption says so");
        if (blocks.Length == 1)
        {
            var lineHeight = measure.LineHeight(body);
            var expected = new UiRect(x + measure.MeasureWidth("Body", body), y + measure.LineHeight(title), (int)(lineHeight * 0.8f), lineHeight);
            TestHarness.Assert(blocks[0].Rect == expected, $"the block follows the last character, a font height high and 0.8 times as wide ({blocks[0].Rect} vs {expected})");
        }

        // The caption fades and ends: 150 frames of duration, then the alpha melts.
        for (var step = 63; step <= 150; step++) Step(director, session);
        TestHarness.Assert(director.MilitaryCaption is not null && Texts(director).All(text => text.Color.A == 255), "full alpha up to the end of the duration");
        for (var step = 151; step <= 200; step++) Step(director, session);
        TestHarness.Assert(director.MilitaryCaption is { Color.A: < 255 } && Texts(director).All(text => text.Color.A == director.MilitaryCaption.Color.A), "then the lines and their shadows fade together");
        for (var step = 201; step <= 240; step++) Step(director, session);
        TestHarness.Assert(director.MilitaryCaption is null && Texts(director).IsEmpty, "and the caption is gone");
    }

    private static void NamedTimerOverlay()
    {
        var script = Script("Timers", If("CONDITION_TRUE"),
        [
            Do("SET_COUNTER", Named(MapScriptParameterType.Counter, "A_Time"), Int(3000)),
            Do("SET_COUNTER", Named(MapScriptParameterType.Counter, "B_Zero"), Int(0)),
            Do("SET_COUNTER", Named(MapScriptParameterType.Counter, "C_Score"), Int(7)),
            Do("DISPLAY_COUNTDOWN_TIMER", Named(MapScriptParameterType.Counter, "A_Time"), Label("GUI:Time")),
            Do("DISPLAY_COUNTDOWN_TIMER", Named(MapScriptParameterType.Counter, "B_Zero"), Label("GUI:Zero")),
            Do("DISPLAY_COUNTER", Named(MapScriptParameterType.Counter, "C_Score"), Label("GUI:Score")),
        ]);
        var (session, director) = Synthetic([script]);
        var measure = new ShellTextMeasure();
        Step(director, session);
        var normal = FontOf(InGameUiFontRole.NamedTimerNormal);
        var ready = FontOf(InGameUiFontRole.NamedTimerReady);
        var texts = Texts(director);
        TestHarness.Assert(texts.Select(text => text.Text).SequenceEqual(["GUI:Time 1:40", "GUI:Zero 0:00", "GUI:Score 7"]), $"timers in name order: a countdown reads m:ss, a counter its value ({string.Join(" | ", texts.Select(text => text.Text))})");
        var startX = (int)(0.90m * 1280);
        var startY = (int)(0.666m * 720);
        TestHarness.Assert(startX == 1152 && startY == 479, "NamedTimerCountdownPosition 0.90, 0.666 of 1280x720");
        TestHarness.Assert(texts.All(text => text.X + measure.MeasureWidth(text.Text, text.Font) == startX), "in the right half of the screen the text ends at the position (reverseXDir)");
        TestHarness.Assert(texts[0].Y == startY && texts[1].Y == startY - measure.LineHeight(normal) && texts[2].Y == startY - measure.LineHeight(normal) - measure.LineHeight(ready), "each next timer stands one font height higher, the ready one with the ready font's height");
        TestHarness.Assert(texts[0].Font == normal && texts[1].Font == ready && texts[2].Font == normal && ready.Bold && ready.Size == 15, $"the countdown at 0:00 uses the ready font ({ready}); the others the normal one ({normal})");
        TestHarness.Assert(texts[0].Color == new WndColor(255, 255, 0, 255) && texts[0].ShadowColor == new WndColor(0, 0, 0, 255), "NamedTimerCountdownNormalColor and a black drop color");

        // The ready countdown flashes between its color and the flash color every 15 frames (odd periods: flash color).
        for (var step = 2; step <= 16; step++) Step(director, session);
        TestHarness.Assert(director.FrameClock == 16 && Texts(director)[1].Color == new WndColor(0, 255, 255, 255) && Texts(director)[0].Color == new WndColor(255, 255, 0, 255), "at frame 16 (second flash period) the ready timer shows the flash color, the others do not");
        for (var step = 17; step <= 31; step++) Step(director, session);
        TestHarness.Assert(director.FrameClock == 31 && Texts(director)[1].Color == new WndColor(255, 255, 0, 255), "at frame 31 it is back to its own color");

        var hide = Script("Hide", If("CONDITION_TRUE"), [Do("HIDE_COUNTER", Named(MapScriptParameterType.Counter, "C_Score")), Do("DISABLE_COUNTDOWN_TIMER_DISPLAY")]);
        var (hidingSession, hiding) = Synthetic([script, hide]);
        Step(hiding, hidingSession);
        TestHarness.Assert(Texts(hiding).IsEmpty, "DISABLE_COUNTDOWN_TIMER_DISPLAY hides every timer and counter");
    }

    private static void CinematicText()
    {
        TestHarness.Assert(MissionTextOverlay.TryParseCinematicFont("Arial - Size:14", out var plain, out _) && plain == new FontDescription("Arial", 14, false), "name and size");
        TestHarness.Assert(MissionTextOverlay.TryParseCinematicFont("Courier New - Size:12 [Bold]", out var bold, out _) && bold == new FontDescription("Courier New", 12, true), "a name with a space, and bold");
        TestHarness.Assert(!MissionTextOverlay.TryParseCinematicFont("Arial", out _, out var problem) && problem.Contains("Size", StringComparison.Ordinal), "a font without a size is refused with a reason");
        TestHarness.Assert(!MissionTextOverlay.TryParseCinematicFont("Arial - Size:big", out _, out _), "a size that is not a number is refused");

        var longText = string.Join(" ", Enumerable.Repeat("Cinematic", 60));
        var script = Script("Cine", If("CONDITION_TRUE"), [Do("DISPLAY_CINEMATIC_TEXT", Label("GUI:Cine"), Label("Arial - Size:12 [Bold]"), Int(4))]);
        var (session, director) = Synthetic([script], label => label == "GUI:Cine" ? longText : label);
        Step(director, session);
        var texts = Texts(director);
        var font = TextFixtures.Style.Language.Resolve(new FontDescription("Arial", 12, true), new UiViewport(1280, 720), TextFixtures.Options);
        var measure = new ShellTextMeasure();
        TestHarness.Assert(texts.Length > 1 && texts.All(text => text.Font == font && text.Color == new WndColor(255, 255, 255, 255) && !text.ShadowColor.IsVisible), $"{texts.Length} wrapped lines in the script's font, white, no shadow");
        TestHarness.Assert(texts[0].Y == (int)(720 * 0.9) && texts[1].Y == texts[0].Y + measure.LineHeight(font), "the block's top is at 0.9 of the screen height");
        TestHarness.Assert(texts.All(text => text.X >= 0 && text.X + measure.MeasureWidth(text.Text, font) <= 1280), "no line leaves the screen");
        var sentence = CnCNew.Ui.Rendering.UiSentenceLayout.Layout(longText, font, measure, 1280 - 20, true);
        TestHarness.Assert(texts.Select(text => text.Text).SequenceEqual(sentence.Lines.Select(line => line.Text).Where(line => line.Length > 0)), "the lines are those of the centered sentence layout wrapped at the width minus 20");

        for (var step = 2; step <= 119; step++) Step(director, session);
        TestHarness.Assert(director.Cinematic is not null, "still shown at frame 119 (4 seconds are 120 frames)");
        Step(director, session);
        TestHarness.Assert(director.Cinematic is null && Texts(director).IsEmpty, "gone at frame 120");

        var badFont = Script("Bad", If("CONDITION_TRUE"), [Do("DISPLAY_CINEMATIC_TEXT", Label("GUI:Cine"), Label("Arial"), Int(4))]);
        var (badSession, bad) = Synthetic([badFont]);
        Step(bad, badSession);
        TestHarness.Assert(bad.Cinematic is null && bad.Problems.Keys.Any(key => key.StartsWith("DISPLAY_CINEMATIC_TEXT", StringComparison.Ordinal)), "an unreadable font spec shows nothing and is reported");
    }

    private static void NoStyleIsReported()
    {
        var script = Script("Talk", If("CONDITION_TRUE"), [Do("DISPLAY_TEXT", Label("GUI:One")), Do("SHOW_MILITARY_CAPTION", Label("MAP:Cap"), Int(1000)), Do("DISPLAY_COUNTER", Named(MapScriptParameterType.Counter, "N"), Label("GUI:N"))]);
        var (session, director) = Synthetic([script], styled: false);
        Step(director, session);
        TestHarness.Assert(Texts(director).IsEmpty, "without a style no text is drawn");
        TestHarness.Assert(director.Problems.Keys.Any(key => key.StartsWith("DISPLAY_TEXT: no InGameUI style", StringComparison.Ordinal)) && director.Problems.Keys.Any(key => key.StartsWith("SHOW_MILITARY_CAPTION: no InGameUI style", StringComparison.Ordinal))
            && director.Problems.Keys.Any(key => key.StartsWith("on-screen text:", StringComparison.Ordinal)), $"each text output and the overlay report it ({string.Join("; ", director.Problems.Keys)})");
        var counts = director.Counts().ToDictionary(count => count.Action);
        TestHarness.Assert(counts["DISPLAY_TEXT"].Forwarded == 0 && counts["SHOW_MILITARY_CAPTION"].Forwarded == 0, "an output with no style is not forwarded, so it cannot count as displayed");

        var measureless = Synthetic([script]);
        measureless.Director.TextMeasure = null;
        Step(measureless.Director, measureless.Session);
        TestHarness.Assert(Texts0(measureless.Director).IsEmpty && measureless.Director.Problems.Keys.Any(key => key.Contains("no text measure", StringComparison.Ordinal)), "without a text measure nothing is drawn and it is reported");

        // A label the host cannot resolve comes back as itself: shown, and counted.
        var (labelSession, labels) = Synthetic([Script("Talk", If("CONDITION_TRUE"), [Do("DISPLAY_TEXT", Label("GUI:Nothing"))])]);
        Step(labels, labelSession);
        TestHarness.Assert(Texts(labels).Single().Text == "GUI:Nothing" && labels.Problems.Keys.Any(key => key.Contains("GUI:Nothing", StringComparison.Ordinal) && key.Contains("no text", StringComparison.Ordinal)), "an unknown label is shown as itself and listed as a problem");

        static ImmutableArray<UiTextDraw> Texts0(MissionPresentationDirector director) => [..director.BuildOverlayCommands(1280, 720).OfType<UiTextDraw>()];
    }

    private static void ReplacementAndPopup()
    {
        var first = Script("First", If("CONDITION_TRUE"), [Do("SHOW_MILITARY_CAPTION", Label("MAP:One"), Int(5000))]);
        var (session, director) = Synthetic([first], label => label == "MAP:One" ? "FirstText" : label);
        for (var step = 0; step < 40; step++) Step(director, session);
        var typed = director.MilitaryCaption!.TypedCharacters;
        TestHarness.Assert(typed > 3, "the first caption is being typed");

        var second = Script("Second", If("CONDITION_TRUE"), [Do("SHOW_MILITARY_CAPTION", Label("MAP:Two"), Int(5000))]);
        var (replaceSession, replace) = Synthetic([first, second], label => label == "MAP:One" ? "FirstText" : "SecondText");
        Step(replace, replaceSession);
        TestHarness.Assert(replace.MilitaryCaption!.FullText == "SecondText" && replace.MilitaryCaption.Lines.SequenceEqual([""]), "a later SHOW_MILITARY_CAPTION in the same step replaces the earlier one and starts typing from nothing");

        var popup = Script("Popup", If("CONDITION_TRUE"), [Do("INGAME_POPUP_MESSAGE", Label("GUI:Pop"), Int(150), Int(-5), Int(20), Int(1))]);
        var (popupSession, popupDirector) = Synthetic([popup], label => label == "GUI:Pop" ? "Hello" : label);
        Step(popupDirector, popupSession);
        TestHarness.Assert(popupDirector.Popup == new PopupMessage("Hello", 100, 0, 50, true), $"the position is clamped to 0..100 percent and the width to at least 50 pixels ({popupDirector.Popup})");
        for (var step = 0; step < 300; step++) Step(popupDirector, popupSession);
        TestHarness.Assert(popupDirector.Popup is not null, "a popup stays until it is closed");
        popupDirector.DismissPopup();
        TestHarness.Assert(popupDirector.Popup is null, "and dismissing it removes it");
    }

    private static void SpeechSubtitles()
    {
        string Resolve(string label) => label switch
        {
            "DIALOGEVENT:VoiceAvailableSubtitle" => "Shown subtitle",
            "DIALOGEVENT:VoiceHiddenSubtitle" => "*Hidden subtitle",
            "DIALOGEVENT:VoiceEmptySubtitle" => string.Empty,
            _ => label,
        };
        (long Frames, string? Text) Run(string voice)
        {
            var (session, director) = Synthetic([Script("Speak", If("CONDITION_TRUE"), [Do("SPEECH_PLAY", Label(voice), Bool(false))])], Resolve);
            Step(director, session);
            return (director.MilitaryCaption is { } caption ? caption.LastFrame : -1, director.MilitaryCaption?.FullText);
        }
        TestHarness.Assert(Run("VoiceAvailable") == (240, "Shown subtitle"), "a found, non-empty subtitle without an asterisk is typed as a caption for 8000 ms (240 frames)");
        TestHarness.Assert(Run("VoiceHidden").Text is null && Run("VoiceEmpty").Text is null && Run("VoiceUnknown").Text is null, "a subtitle that starts with an asterisk, is empty or does not exist shows nothing");
    }

    private static void ScalingFollowsTheScreen()
    {
        var script = Script("Caption", If("CONDITION_TRUE"), [Do("SHOW_MILITARY_CAPTION", Label("MAP:Caption"), Int(5000)), Do("DISPLAY_TEXT", Label("GUI:One"))]);
        var (session, director) = Synthetic([script], label => label == "MAP:Caption" ? "Title" : label);
        for (var step = 0; step < 30; step++) Step(director, session);
        foreach (var (width, height) in new[] { (800, 600), (1920, 1080), (2560, 1080) })
        {
            var texts = Texts(director, width, height);
            var caption = texts.Single(text => text.Text.StartsWith("T", StringComparison.Ordinal));
            var message = texts.Single(text => text.Text == "GUI:One");
            TestHarness.Assert(caption.X == (int)(10 * width / 800f) && caption.Y == (int)(340 * height / 600f), $"{width}x{height}: the caption position scales from 800x600 ({caption.X},{caption.Y})");
            TestHarness.Assert(caption.Font == FontOf(InGameUiFontRole.MilitaryCaptionTitle, width, height) && message.Font == FontOf(InGameUiFontRole.Message, width, height), $"{width}x{height}: the fonts are the adjusted ones ({caption.Font}, {message.Font})");
            TestHarness.Assert(message.X == 10 && message.Y == 22, $"{width}x{height}: the message position stays in pixels");
        }
        TestHarness.Assert(FontOf(InGameUiFontRole.Message, 800, 600).Size == 10 && FontOf(InGameUiFontRole.Message, 1920, 1080).Size > FontOf(InGameUiFontRole.Message, 1280, 720).Size, "fonts grow with the screen width");
        var scaled = new UiScaleOptions { TextScale = 2m };
        TestHarness.Assert(FontOf(InGameUiFontRole.Message, 1280, 720, scaled).Size == 2 * FontOf(InGameUiFontRole.Message).Size, "the accessibility text scale multiplies the size");
    }

    // ---------------------------------------------------------------------------------------------------- real data

    private static (LiveMission Live, MissionController Controller) LoadMission(GameData data, string map)
    {
        var text = data.LoadText("English");
        var run = MissionRun.Load(MissionContext.From(data), map, MissionOptions.Standard with { PresentationLatencyTicks = LiveMission.ClientPresentationLatencyFrames });
        var live = LiveMission.Create(data, run, new Vector2(1280, 720), label => text.TryGet(label, out var resolved) ? resolved : label);
        return (live, new MissionController(live, GameSettings.Default));
    }

    private static void RealStyle(GameData data)
    {
        {
            // doSpeechPlay shows DIALOGEVENT:<name>Subtitle as a military caption unless the text starts with '*': the patch's English table hides all of them.
            var subtitles = data.LoadText("English").Table.Entries.Where(entry => entry.Label.StartsWith("DIALOGEVENT:", StringComparison.OrdinalIgnoreCase) && entry.Label.EndsWith("Subtitle", StringComparison.OrdinalIgnoreCase)).ToArray();
            TestHarness.Assert(subtitles.Length == 2455 && subtitles.All(entry => entry.Text.StartsWith("*", StringComparison.Ordinal)), "the patch hides all 2455 subtitles by a leading asterisk");
        }
        var style = InGameUiStyle.FromGraph(data.Graph);
        TestHarness.Assert(style.Settings.MessagePosition == new InGameUiPixelPoint(10, 10) && style.Settings.MilitaryCaptionPosition == new InGameUiPixelPoint(10, 340), "the retail positions");
        var viewport = new UiViewport(1280, 720);
        var options = new UiScaleOptions();
        TestHarness.Assert(style.Font(InGameUiFontRole.MilitaryCaptionTitle, viewport, options) is { Name: "Courier New", Bold: true }
            && style.Font(InGameUiFontRole.MilitaryCaption, viewport, options) is { Name: "Courier New", Bold: false }
            && style.Font(InGameUiFontRole.Message, viewport, options) is { Name: "Arial", Bold: true }
            && style.Font(InGameUiFontRole.SuperweaponReady, viewport, options).Bold && !style.Font(InGameUiFontRole.SuperweaponNormal, viewport, options).Bold
            && !style.Font(InGameUiFontRole.NamedTimerReady, viewport, options).Bold, "the retail fonts: Courier New for the caption (bold title), Arial for messages (bold), superweapon ready bold");
        TestHarness.Assert(style.Language.Settings.MilitaryCaptionSpeed == 1 && style.Language.Settings.MilitaryCaptionDelayMilliseconds == 750, "typing speed 1, start delay 750 ms");
    }

    private static void RealCaption(GameData data)
    {
        var (live, controller) = LoadMission(data, "Maps/MD_USA01/MD_USA01.map");
        var director = live.Director;
        var style = controller.Hud.TextStyle;
        var title = style.Font(InGameUiFontRole.MilitaryCaptionTitle, new UiViewport(1280, 720), controller.Hud.ScaleOptions);
        var body = style.Font(InGameUiFontRole.MilitaryCaption, new UiViewport(1280, 720), controller.Hud.ScaleOptions);
        var typedFirst = false;
        var lineStates = new List<string>();
        for (var frame = 0; frame < 190; frame++)
        {
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            if (director.MilitaryCaption is { } caption)
            {
                var line = string.Join("|", caption.Lines);
                if (lineStates.Count == 0 || lineStates[^1] != line) lineStates.Add(line);
                typedFirst |= caption.Lines.Length == 2 && caption.Lines[1].Length > 0;
            }
        }
        TestHarness.Assert(director.MilitaryCaption is not null || typedFirst, "the opening shows its caption");
        TestHarness.Assert(typedFirst, $"the caption types its second line (states: {lineStates.Count}, last '{lineStates[^1]}')");
        TestHarness.Assert(lineStates[0].Length <= 1 && lineStates[^1] == "Somewhere in Southern Kazakhstan|En route to Baikonur", $"the label's two lines are typed one after the other ({lineStates[^1]})");
        // Draw it again at the typed state: two draws in Courier New at the scaled position.
        var restart = LoadMission(data, "Maps/MD_USA01/MD_USA01.map");
        for (var frame = 0; frame < 120; frame++) restart.Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var texts = restart.Controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Where(text => text.Font.Name == "Courier New").ToArray();
        TestHarness.Assert(texts.Length == 2 && texts[0].Text == "Somewhere in Southern Kazakhstan" && texts[0].Font == title && texts[1].Font == body && texts[0].X == 16 && texts[0].Y == 408, $"the real HUD draws the typed caption at 16,408 ({string.Join(" | ", texts.Select(text => $"{text.Font} '{text.Text}' {text.X},{text.Y}"))})");
        TestHarness.Assert(!restart.Live.Director.Problems.Keys.Any(key => key.Contains("on-screen text", StringComparison.Ordinal) || key.Contains("has no text", StringComparison.Ordinal) || key.StartsWith("SHOW_MILITARY_CAPTION", StringComparison.Ordinal)), $"no text problems ({string.Join("; ", restart.Live.Director.Problems.Keys)})");
    }

    private static void RealCountdown(GameData data)
    {
        var (live, controller) = LoadMission(data, "Maps/MD_CHI02/MD_CHI02.map");
        for (var frame = 0; frame < 2100; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var normal = controller.Hud.TextStyle.Font(InGameUiFontRole.NamedTimerNormal, new UiViewport(1280, 720), controller.Hud.ScaleOptions);
        var timer = controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().FirstOrDefault(text => text.Text.Contains("Arrive", StringComparison.OrdinalIgnoreCase));
        TestHarness.Assert(timer is not null, $"the reinforcement countdown is on screen ({string.Join(" | ", controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Select(text => text.Text))})");
        var measure = new MonospaceTextMeasure();
        TestHarness.Assert(timer!.Font == normal && timer.X + CnCNew.Ui.Rendering.UiSentenceLayout.Layout(timer.Text, timer.Font, measure, 0, false).Width == 1152 && timer.Y == 479, $"right-aligned at 0.90 of the width, 0.666 of the height, in the normal timer font ({timer.Font}, {timer.X},{timer.Y})");
        TestHarness.Assert(System.Text.RegularExpressions.Regex.IsMatch(timer.Text, @"\d+:\d\d$"), $"a countdown reads m:ss ({timer.Text})");
        TestHarness.Assert(!live.Director.Problems.Keys.Any(key => key.Contains("has no text", StringComparison.Ordinal) || key.Contains("on-screen text", StringComparison.Ordinal)), "no unresolved labels");
    }

    private static void RealTypingClick(GameData data)
    {
        var (live, controller) = LoadMission(data, "Maps/MD_USA01/MD_USA01.map");
        using var audio = CnCNew.Game.Audio.GameAudio.Create(data, options: new CnCNew.Game.Audio.AudioDirectorOptions { Seed = 1 });
        live.Director.AudioControl = audio;
        TestHarness.Assert(data.Audio.Find(MissionPresentationDirector.TypingClickEvent) is not null, "SoundEffects.ini has the MilitarySubtitlesTyping event");
        for (var frame = 0; frame < 190; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var director = live.Director;
        var expected = "Somewhere in Southern KazakhstanEn route to Baikonur".Length;
        TestHarness.Assert(director.TypingClicks == expected, $"one click asked per typed character of the two caption lines ({director.TypingClicks} of {expected})");
        TestHarness.Assert(director.TypingClicksStarted > 0, $"the audio director starts the click ({director.TypingClicksStarted} of {director.TypingClicks} started)");
        Console.WriteLine($"       typing clicks: {director.TypingClicks} asked, {director.TypingClicksStarted} started by the audio director");
    }

    /// <summary>
    /// <c>InGameUI::m_uiMessages</c> is one list: the game's own messages (here a refused placement) and <c>DISPLAY_TEXT</c> lines stand in it together, and <c>InGameUI::postDraw</c>
    /// draws it whether or not the control bar is hidden.
    /// </summary>
    private static void RealSharedMessageList(GameData data)
    {
        var (live, controller) = LoadMission(data, "Maps/MD_USA01/MD_USA01.map");
        var director = live.Director;
        TestHarness.Assert(ReferenceEquals(director.Messages, controller.Hud.Messages.Log) && !controller.Hud.OwnsMessageList, "the mission HUD hands its message list to the director");
        for (var frame = 0; frame < 30; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(director.Messages!.IsEmpty && controller.Hud.Messages.WithoutLine.ContainsKey("upgrade granted, not ordered"), "the radar the command center comes with is no 'Upgrade Complete' line (nobody ordered it)");
        // A line of the scripts' DISPLAY_TEXT stands in the same list: the HUD's line is the next, one font height below.
        director.Messages.Add("a line of the scripts", director.FrameClock); // the list counts original frames, as the director's own DISPLAY_TEXT does
        controller.Hud.Messages.Message("GUI:CantBuildThere");
        var font = controller.Hud.TextStyle.Font(InGameUiFontRole.Message, new UiViewport(1280, 720), controller.Hud.ScaleOptions);
        var list = controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Where(text => text.Text == "You can't build here").ToArray();
        var index = director.Messages.Lines.Length - 1;
        var expectedY = 10 + index * new MonospaceTextMeasure().LineHeight(font);
        Console.WriteLine($"       shared message list: {string.Join(" | ", director.Messages.Lines.Select(entry => entry.Text))}");
        TestHarness.Assert(director.Messages.Lines[^1].Text == "You can't build here" && list is [{ X: 10 } line] && line.Y == expectedY && line.Font == font,
            $"the line is drawn once, by the director, as line {index} of the list from the message position in the message font ({string.Join(" | ", list.Select(draw => $"{draw.X},{draw.Y} {draw.Font}"))} vs {expectedY} {font})");
        var before = director.Messages.Lines[^1].Color.A;
        for (var frame = 0; frame < 150; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(director.Messages.Lines.FirstOrDefault(entry => entry.Text == "You can't build here") is { Text: not null, Color.A: var after } && after < before, "the director fades it on its clock");
        for (var frame = 0; frame < 200; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(director.Messages.IsEmpty, "and removes it");
    }

    private static ImmutableArray<UiDrawCommand> DrawPopup(MissionPopupWindow popup) => popup.BuildCommands(1280, 720);

    private static void RealPopupWindow(GameData data)
    {
        var assets = HudAssets.Load(data, "English");
        var measure = new MonospaceTextMeasure();
        var viewport = new UiViewport(1280, 720);
        var popup = new MissionPopupWindow(assets, measure, new UiScaleOptions(), viewport);
        TestHarness.Assert(!popup.IsOpen && DrawPopup(popup).IsEmpty && !popup.Process(new MouseMoveEvent(0, new Vector2(10, 10))), "a closed popup draws nothing and takes no input");

        var text = "Build a Power Plant first, then a Barracks, and train an Infantry squad to defend the base against the first attack of the enemy.";
        var message = new PopupMessage(text, 50, 25, 300, false);
        popup.Show(message, viewport);
        var rect = popup.Rect;
        var ok = popup.OkRect;
        TestHarness.Assert(rect.X == 640 && rect.Y == 180 && rect.Width == 300, $"the window is at 50% and 25% of the screen and as wide as the script said ({rect})");
        TestHarness.Assert(rect.Height - ok.Height - 2 - 2 - 7 - 2 >= 2 * 10, $"the long text wraps to at least two lines inside the window ({rect.Height} high, button {ok.Height})");
        TestHarness.Assert(ok.X + ok.Width == rect.X + rect.Width - 2 && ok.Y + ok.Height == rect.Y + rect.Height - 2, $"the OK button sits 2 pixels from the right and bottom edges ({ok} in {rect})");
        var commands = DrawPopup(popup);
        TestHarness.Assert(commands.OfType<UiTextDraw>().Any(draw => draw.Text.Length > 0 && text.StartsWith(draw.Text.Trim(), StringComparison.Ordinal)), "the text is drawn");
        TestHarness.Assert(commands.OfType<UiBorderRect>().Any(border => border.Rect == rect && border.Color == new WndColor(47, 55, 168, 255)) && commands.OfType<UiFillRect>().Any(fill => fill.Rect == new UiRect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2) && fill.Color == new WndColor(0, 0, 0, 190)), "on the layout's translucent black panel with its blue border");
        TestHarness.Assert(commands.OfType<UiTextDraw>().Any(draw => draw.Text == "OK" || draw.LabelKey == "GUI:Ok"), $"the OK label is drawn ({string.Join(" | ", commands.OfType<UiTextDraw>().Select(draw => draw.Text))})");

        // Clicking inside but not on the button does not close it; the button does; a click elsewhere passes through (not modal).
        var inside = new Vector2(rect.X + 5, rect.Y + 5);
        TestHarness.Assert(popup.Process(new MouseMoveEvent(0, inside)), "a pointer over the window is taken");
        TestHarness.Assert(!popup.Process(new MouseMoveEvent(1, new Vector2(5, 5))), "a pointer elsewhere goes on to the world when the popup does not pause");
        var closed = 0;
        popup.Closed += () => closed++;
        TestHarness.Assert(popup.Process(new MouseButtonEvent(2, MouseButton.Left, true, inside)) && popup.Process(new MouseButtonEvent(3, MouseButton.Left, false, inside)) && popup.IsOpen && closed == 0, "a click on the text keeps the popup open");
        var onOk = new Vector2(ok.X + ok.Width / 2f, ok.Y + ok.Height / 2f);
        popup.Process(new MouseMoveEvent(4, onOk));
        TestHarness.Assert(popup.Process(new MouseButtonEvent(5, MouseButton.Left, true, onOk)) && popup.Process(new MouseButtonEvent(6, MouseButton.Left, false, onOk)), "the OK click is taken");
        TestHarness.Assert(!popup.IsOpen && closed == 1 && DrawPopup(popup).IsEmpty, "OK closes the popup, once");

        popup.Show(message, viewport);
        TestHarness.Assert(popup.Process(new KeyEvent(7, KeyCode.Enter, true)) && popup.IsOpen && popup.Process(new KeyEvent(8, KeyCode.Enter, false)) && !popup.IsOpen && closed == 2, "Enter closes it on key up");
        popup.Show(message, viewport);
        TestHarness.Assert(!popup.Process(new KeyEvent(9, KeyCode.A, true)) && popup.Process(new KeyEvent(10, KeyCode.Escape, false)) && !popup.IsOpen && closed == 3, "other keys pass through, Escape closes");

        var modal = new PopupMessage("Wait", 10, 10, 200, true);
        popup.Show(modal, viewport);
        TestHarness.Assert(popup.Process(new MouseMoveEvent(11, new Vector2(1200, 700))) && popup.Process(new MouseButtonEvent(12, MouseButton.Left, true, new Vector2(1200, 700))) && popup.Process(new KeyEvent(13, KeyCode.A, true)), "a popup that pauses the game swallows every event");
        popup.Process(new KeyEvent(14, KeyCode.Escape, false));
        TestHarness.Assert(!popup.IsOpen, "and still closes with Escape");

        // The layout follows the screen.
        popup.Show(message, viewport);
        var small = popup.BuildCommands(800, 600);
        TestHarness.Assert(popup.Rect.X == 400 && popup.Rect.Y == 150 && !small.IsEmpty, $"at 800x600 the same popup is at 400,150 ({popup.Rect})");
        popup.Hide();
    }

    private static void RealPopupInTheMission(GameData data)
    {
        var (live, controller) = LoadMission(data, "Maps/GC_ChemGeneral/GC_ChemGeneral.map");
        for (var frame = 0; frame < 30; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var director = live.Director;
        var tick = live.World.Tick;

        director.ShowPopup(new PopupMessage("A message that stops the game.", 40, 40, 260, true));
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(controller.Popup is { IsOpen: true } && live.Paused, "a pausing popup opens its window and pauses the game");
        var pausedAt = live.World.Tick;
        for (var frame = 0; frame < 30; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(live.World.Tick == pausedAt, "the world stands still while it is open");
        TestHarness.Assert(controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Any(draw => draw.Text.StartsWith("A message", StringComparison.Ordinal)), "the text is in the HUD's draw list");
        controller.Process(new MouseMoveEvent(100, new Vector2(300, 300)));
        controller.Process(new MouseButtonEvent(101, MouseButton.Left, true, new Vector2(300, 300)));
        controller.Process(new MouseButtonEvent(102, MouseButton.Left, false, new Vector2(300, 300)));
        TestHarness.Assert(controller.Hud.Input.Selection.Selected.IsEmpty && director.Popup is not null, "a click in the world is swallowed, the popup stays");
        var ok = controller.Popup!.OkRect;
        var onOk = new Vector2(ok.X + ok.Width / 2f, ok.Y + ok.Height / 2f);
        controller.Process(new MouseMoveEvent(103, onOk));
        controller.Process(new MouseButtonEvent(104, MouseButton.Left, true, onOk));
        controller.Process(new MouseButtonEvent(105, MouseButton.Left, false, onOk));
        TestHarness.Assert(director.Popup is null && controller.Popup is { IsOpen: false } && !live.Paused, "OK removes the popup on the script side and unpauses the game");
        for (var frame = 0; frame < 30; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(live.World.Tick > pausedAt, "and the mission goes on");
        TestHarness.Assert(!controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Any(draw => draw.Text.StartsWith("A message", StringComparison.Ordinal)), "the text is gone");

        // A popup that does not pause leaves the game running and the world reachable away from the window.
        director.ShowPopup(new PopupMessage("Just so you know.", 40, 40, 260, false));
        var before = live.World.Tick;
        for (var frame = 0; frame < 30; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(controller.Popup is { IsOpen: true } && !live.Paused && live.World.Tick > before, "a popup without the pause flag lets the game run");
        controller.Process(new KeyEvent(200, KeyCode.Escape, false));
        TestHarness.Assert(director.Popup is null && !controller.Hud.IsMenuOpen, "Escape closes the popup instead of opening the game menu");
    }
}
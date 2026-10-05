using System.Collections;
using CnCNew.Content;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Transitions;
using CnCNew.Ui.Wnd;
using static CnCNew.Ui.Tests.WndFixtures;

namespace CnCNew.Ui.Tests;

/// <summary>
/// Tests of the window transitions (<c>GameWindowTransitions</c> port): the strict <c>WindowTransitions.ini</c> reader, the maths of every
/// effect at sampled frames, the group sequencing of the handler (pending/reverse/remove/fire-once), the frame clock, the integration with a
/// real <see cref="UiWindowTree"/> and, with <c>--content-dir</c>, the shipped file against the shipped layouts.
/// </summary>
public static class TransitionTests
{
    private static int _failures;

    public static int Run(string[] args)
    {
        _failures = 0;
        Test(TableParsesTheShippedShape);
        Test(TableRejectsWhatItDoesNotKnow);
        Test(FlashHidesThenFadesInAFixedTable);
        Test(ButtonFlashFollowsItsFrameTable);
        Test(ButtonFlashReversedMirrorsTheTable);
        Test(FadeAndScaleUpUseTheirIncrements);
        Test(ScreenFadeAndFullFadeRampAlpha);
        Test(FactionScalesGrowTowardsTheirTargets);
        Test(CountUpTypeTextAndTextOnFrame);
        Test(ControlBarArrowSlidesThenFades);
        Test(ReverseSoundFiresOnItsFrame);
        Test(HandlerQueuesTheNextGroupBehindARunningOne);
        Test(HandlerReversePlaysAGroupBackwardsFromItsEnd);
        Test(HandlerRemoveAndFireOnceBehaveLikeTheOriginal);
        Test(HandlerClockTurnsSecondsIntoWholeFrames);
        Test(HandlerNeverIgnoresUnknownNames);
        Test(TransitionsDriveARealWindowTree);
        var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
        if (contentDirectory is not null) Test(() => RealData(contentDirectory));
        Console.WriteLine(_failures == 0 ? "Transition tests passed." : $"Transition tests: {_failures} failure(s).");
        return _failures;
    }

    private static void Test(Action test)
    {
        try { test(); }
        catch (Exception exception)
        {
            _failures++;
            Console.Error.WriteLine($"FAIL {test.Method.Name}: {exception}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (condition) return;
        _failures++;
        Console.Error.WriteLine($"FAIL: {message}");
    }

    private static void Equal<T>(T expected, T actual, string what)
    {
        var same = expected is System.Collections.IStructuralEquatable structural
            ? structural.Equals(actual, StructuralComparisons.StructuralEqualityComparer)
            : EqualityComparer<T>.Default.Equals(expected, actual);
        var shown = (object? value) => value is System.Collections.IEnumerable items and not string ? "[" + string.Join(", ", items.Cast<object?>()) + "]" : value?.ToString() ?? "null";
        Check(same, $"{what}: expected {shown(expected)}, got {shown(actual)}.");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Fakes

    private sealed class FakeWindow(string name, UiRect rect, bool hidden = false) : ITransitionWindow
    {
        public string Name => name;
        public UiRect Rect { get; set; } = rect;
        public bool IsHidden { get; private set; } = hidden;
        public string? EnabledImage { get; set; } = "Img-" + name;
        public string? DisabledImage { get; set; } = "Dis-" + name;
        public string Text { get; private set; } = "";
        public int HiddenWrites { get; private set; }
        public void SetHidden(bool value) { IsHidden = value; HiddenWrites++; }
        public void SetEnabledImage(string? image) => EnabledImage = image;
        public void SetText(string text) => Text = text;
        public void AppendButtonImages(int alpha, List<UiDrawCommand> output) => output.Add(new UiImageDraw($"button:{name}", Rect, new WndColor(255, 255, 255, (byte)alpha), false, false));
        public void AppendText(string text, List<UiDrawCommand> output) => output.Add(new UiTextDraw(text, null, new UiFont("t", 10, false), WndColor.White, WndColor.Undefined, Rect.X, Rect.Y, UiTextAlignment.Left, Rect));
    }

    private sealed class FakeHost : ITransitionHost
    {
        public Dictionary<string, FakeWindow> Windows { get; } = new(StringComparer.Ordinal);
        public List<string> Sounds { get; } = [];
        public int DisplayWidth { get; init; } = 800;
        public int DisplayHeight { get; init; } = 600;
        public string? ControlBarArrowImage { get; init; }

        public FakeWindow Add(string name, int x = 100, int y = 100, int w = 120, int h = 60, bool hidden = false)
            => Windows[name] = new FakeWindow(name, new UiRect(x, y, w, h), hidden);

        public ITransitionWindow? Find(string decoratedName) => Windows.GetValueOrDefault(decoratedName);
        public void PlaySound(string eventName) => Sounds.Add(eventName);
        public bool TryGetImageSize(string image, out int width, out int height)
        {
            (width, height) = (32, 24);
            return true;
        }
    }

    private static WindowTransitionTable Table(string text) => WindowTransitionTable.Parse(text, "test.ini");

    private static string Group(string name, string style, string window = "A", int delay = 0, bool fireOnce = false)
        => $"WindowTransition {name}\n  Window\n    WinName = {window}\n    Style = {style}\n    FrameDelay = {delay}\n  END\n{(fireOnce ? "  FireOnce = Yes\n" : "")}END\n";

    private static string Describe(IEnumerable<UiDrawCommand> commands) => string.Join("|", commands.Select(command => command switch
    {
        UiBorderRect border => $"border {border.Rect} a{border.Color.A}",
        UiFillRect fill => $"fill {fill.Rect} {fill.Color.R},{fill.Color.G},{fill.Color.B} a{fill.Color.A}",
        UiImageDraw image => $"image {image.Image} {image.Destination} a{image.Tint.A}",
        UiTextDraw text => $"text '{text.Text}'",
        _ => command.ToString(),
    }));

    private static string Draws(WindowTransitionHandler handler)
    {
        var output = new List<UiDrawCommand>();
        handler.AppendDraw(output);
        return Describe(output);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Table

    private static void TableParsesTheShippedShape()
    {
        var text = """
            ;; comment
            WindowTransition First
              Window
                WinName = A.wnd:One
                Style   = BUTTONFLASH
                FrameDelay = 5
              END
              Window
                Style = SCREENFADE
                FrameDelay = 0
              END
              FireOnce = YES
            END

            windowtransition second
              Window
                WinName = A.wnd:Two ; trailing comment
                Style = WinFade
              END
            END
            """;
        var table = Table(text);
        Equal(2, table.Groups.Length, "group count");
        var first = table.Find("FIRST") ?? throw new InvalidOperationException("case-insensitive Find");
        Equal(true, first.FireOnce, "FireOnce");
        Equal(2, first.Windows.Length, "windows");
        Equal(new TransitionWindowSpec("A.wnd:One", TransitionStyle.ButtonFlash, 5), first.Windows[0], "first window");
        Equal(new TransitionWindowSpec(null, TransitionStyle.ScreenFade, 0), first.Windows[1], "window-less screen fade");
        var second = table.Find("Second")!;
        Equal(false, second.FireOnce, "FireOnce default");
        Equal(new TransitionWindowSpec("A.wnd:Two", TransitionStyle.WinFade, 0), second.Windows[0], "default delay, lower-case keyword, comment stripped");
    }

    private static void TableRejectsWhatItDoesNotKnow()
    {
        void Rejects(string text, string expectedFragment)
        {
            try
            {
                Table(text);
                Check(false, $"Expected a format error containing '{expectedFragment}'.");
            }
            catch (WindowTransitionFormatException exception)
            {
                Check(exception.Message.Contains(expectedFragment, StringComparison.Ordinal) && exception.Message.StartsWith("test.ini:", StringComparison.Ordinal),
                    $"Message '{exception.Message}' should name the file and contain '{expectedFragment}'.");
            }
        }
        Rejects("WindowTransition G\n Window\n WinName = A\n Style = SPINNING\n END\nEND\n", "unknown style 'SPINNING'");
        Rejects("WindowTransition G\n Window\n WinName = A\n END\nEND\n", "without Style");
        Rejects("WindowTransition G\n Window\n Style = FLASH\n END\nEND\n", "needs a WinName");
        Rejects("WindowTransition G\n Window\n WinName = A\n Style = FLASH\n Colour = red\n END\nEND\n", "unknown Window key 'Colour'");
        Rejects("WindowTransition G\n Mode = fast\nEND\n", "unknown group key 'Mode'");
        Rejects("WindowTransition G\nEND\nWindowTransition g\nEND\n", "duplicate group 'g'");
        Rejects("WindowTransition G\n Window\n WinName = A\n Style = FLASH\n FrameDelay = -1\n END\nEND\n", "FrameDelay");
        Rejects("WindowTransition G\n FireOnce = maybe\nEND\n", "not a boolean");
        Rejects("WindowTransition G\n Window\n WinName = A\n Style = FLASH\n END\n", "not terminated");
        Rejects("Nonsense here\n", "expected 'WindowTransition <name>'");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Effects at sampled frames

    private static (FakeHost Host, WindowTransitionHandler Handler, FakeWindow Window) One(string style, bool fireOnce = false, int delay = 0, int x = 100, int y = 100, int w = 120, int h = 60)
    {
        var host = new FakeHost();
        var window = host.Add("A", x, y, w, h);
        var handler = new WindowTransitionHandler(Table(Group("G", style, "A", delay, fireOnce)), host);
        return (host, handler, window);
    }

    private static void FlashHidesThenFadesInAFixedTable()
    {
        var (host, handler, window) = One("FLASH");
        handler.SetGroup("G");
        Equal(true, window.IsHidden, "FLASH hides the window when the group starts");
        var hidden = new List<bool>();
        var draws = new List<string>();
        for (var frame = 1; frame <= 8; frame++)
        {
            handler.Step();
            hidden.Add(window.IsHidden);
            draws.Add(Draws(handler));
        }
        Equal("True,True,True,False,False,False,False,False", string.Join(",", hidden), "hidden per frame 1..8");
        Equal("border 101,101 118x60 a100|fill 101,101 118x60 255,255,255 a33", draws[0], "frame 1 stand-in");
        Equal("border 101,101 118x60 a200|fill 101,101 118x60 255,255,255 a99", draws[2], "frame 3 stand-in");
        Equal("border 101,101 118x60 a250|fill 101,101 118x60 255,255,255 a75", draws[3], "frame 4 stand-in");
        Equal("border 101,101 118x60 a250|fill 101,101 118x60 255,255,255 a10", draws[6], "frame 7 stand-in");
        Equal("", draws[7], "the end frame draws nothing");
        Equal(true, handler.IsFinished, "finished after 8 frames");
        Equal(new[] { "GUIBoarderFadeIn" }, host.Sounds.ToArray(), "one sound on frame 1");
    }

    private static void ButtonFlashFollowsItsFrameTable()
    {
        var (host, handler, window) = One("BUTTONFLASH", delay: 5);
        handler.SetGroup("G");
        Equal(true, window.IsHidden, "hidden at start");
        var hidden = new List<bool>();
        var draws = new Dictionary<int, string>();
        for (var frame = 1; frame <= 22; frame++)
        {
            handler.Step();
            hidden.Add(window.IsHidden);
            draws[frame] = Draws(handler);
        }
        // Frames before the delay do nothing; the effect's frame 1 is group frame 6.
        Equal("", draws[5], "nothing before the frame delay");
        Equal("border 100,100 120x60 a100|fill 100,100 120x60 255,255,255 a75", draws[6], "effect frame 1");
        Equal("border 100,100 120x60 a200|fill 100,100 120x60 255,255,255 a200", draws[8], "effect frame 3");
        Equal("image button:A 100,100 120x60 a255|border 100,100 120x60 a250|fill 100,100 120x60 255,255,255 a150", draws[9], "effect frame 4: button image under the flash");
        Equal("image button:A 100,100 120x60 a255|border 100,100 120x60 a250|fill 100,100 120x60 255,255,255 a15", draws[12], "effect frame 7");
        Equal("image button:A 100,100 120x60 a255", draws[14], "effect frame 9 shows the button image only");
        Equal("image button:A 100,100 120x60 a255|image Gradient 100,100 120x60 a100", draws[16], "effect frame 11: button plus gradient 100");
        Equal("image Gradient 100,100 120x60 a200", draws[17], "effect frame 12: gradient 200");
        Equal("image Gradient 100,100 120x60 a150", draws[18], "effect frame 13");
        Equal("image Gradient 100,100 120x60 a17", draws[21], "effect frame 16");
        Equal(true, hidden[..15].All(value => value), "hidden up to effect frame 10 (group frame 15)");
        Equal(false, hidden[15], "shown from effect frame 11");
        Equal(true, handler.IsFinished, "finished at effect frame 17 = group frame 22");
        Equal(new[] { "GUIButtonsFadeIn" }, host.Sounds.ToArray(), "one sound");
    }

    private static void ButtonFlashReversedMirrorsTheTable()
    {
        var (host, handler, window) = One("BUTTONFLASH");
        // Reverse of a group that is not running: the window is first brought to its end (shown), then flashes out.
        handler.Reverse("G");
        Equal(false, window.IsHidden, "shown after the implicit skip");
        Equal("G", handler.CurrentGroup, "the reversed group is current");
        Equal(true, handler.CurrentIsReversed, "and reversed");
        var hidden = new List<bool>();
        var draws = new List<string>();
        var guard = 0;
        while (!handler.IsIdle && guard++ < 60)
        {
            handler.Step();
            hidden.Add(window.IsHidden);
            draws.Add(Draws(handler));
        }
        Equal(true, handler.IsIdle, "the reversed group ends and is dropped");
        Equal(true, window.IsHidden, "a reversed button effect ends with the window hidden");
        Equal(17, hidden.Count, "frames 16..1, then 0");
        // First processed frame is 16 (group frame = total - 1): gradient 100 over the button, window visible.
        Equal(false, hidden[0], "frame 16 keeps the window visible");
        Equal("image Gradient 100,100 120x60 a100", draws[0], "frame 16 draws the mirrored gradient state (in 1)");
        Equal(0, host.Sounds.Count, "no fade-in sound while reversing");
    }

    private static void FadeAndScaleUpUseTheirIncrements()
    {
        var (_, fade, fadeWindow) = One("WINFADE");
        fade.SetGroup("G");
        for (var frame = 1; frame <= 9; frame++)
        {
            fade.Step();
            Equal($"image Img-A 100,100 120x60 a{25 * frame}", Draws(fade), $"fade frame {frame}");
            Equal(true, fadeWindow.IsHidden, "hidden while fading");
        }
        fade.Step();
        Equal(false, fadeWindow.IsHidden, "shown at the end");
        Equal(true, fade.IsFinished, "fade finished after 10 frames");

        var (host, scale, window) = One("WINSCALEUP", x: 100, y: 100, w: 120, h: 60);
        scale.SetGroup("G");
        scale.Step();
        // centre (160,130), increments 20x10: x = 160 - 20/2 = 150, y = 130 - 10/2 = 125, size 20x10.
        Equal("image Img-A 150,125 20x10 a255", Draws(scale), "scale-up frame 1");
        for (var frame = 2; frame <= 4; frame++) scale.Step();
        Equal("image Img-A 120,110 80x40 a255", Draws(scale), "scale-up frame 4");
        scale.Step();
        Equal("image Img-A 110,105 100x50 a255", Draws(scale), "scale-up frame 5");
        Equal(true, window.IsHidden, "still hidden on frame 5");
        scale.Step();
        Equal("", Draws(scale), "nothing on the last frame");
        Equal(false, window.IsHidden, "shown on the last frame");
        Equal(new[] { "GUILogoMouseOver" }, host.Sounds.ToArray(), "sound on frame 1");

        var (scoreHost, score, _) = One("SCORESCALEUP");
        score.SetGroup("G");
        score.Step();
        Equal(new[] { "GUIScoreScreenPictures" }, scoreHost.Sounds.ToArray(), "score scale-up has its own sound");
    }

    private static void ScreenFadeAndFullFadeRampAlpha()
    {
        var host = new FakeHost();
        var handler = new WindowTransitionHandler(Table("WindowTransition S\n Window\n Style = SCREENFADE\n FrameDelay = 0\n END\n FireOnce = Yes\nEND\n"), host);
        handler.SetGroup("S");
        handler.Step();
        Equal("fill 0,0 800x600 0,0,0 a8", Draws(handler), "screen fade frame 1");
        for (var frame = 2; frame <= 10; frame++) handler.Step();
        Equal("fill 0,0 800x600 0,0,0 a87", Draws(handler), "screen fade frame 10");
        for (var frame = 11; frame <= 15; frame++) handler.Step();
        Equal("fill 0,0 800x600 0,0,0 a131", Draws(handler), "screen fade frame 15");
        for (var frame = 16; frame <= 30; frame++) handler.Step();
        Equal(true, handler.IsFinished, "finished at frame 30");
        Equal("fill 0,0 800x600 0,0,0 a255", Draws(handler), "full black on the last frame");
        handler.Step();
        Equal("fill 0,0 800x600 0,0,0 a255", Draws(handler), "the last frame of a fire-once group is drawn once more");
        handler.Step();
        Equal("", Draws(handler), "then it is gone");
        Equal(true, handler.IsIdle, "idle");

        // Reversed: starts fully black and fades to clear (screen fade-in at game start).
        handler.Reverse("S");
        Equal(true, handler.CurrentIsReversed, "reversed");
        handler.Step();
        var top = (int)(1.0f / (30 - 1) * 255 * 29);
        Equal($"fill 0,0 800x600 0,0,0 a{top}", Draws(handler), "reverse frame 29");
        var steps = 1;
        while (!handler.IsIdle && steps < 80) { handler.Step(); steps++; }
        Equal(30, steps, "30 steps to clear");
        handler.Step();

        var (_, full, window) = One("FULLFADE", x: 10, y: 20, w: 200, h: 100);
        full.SetGroup("G");
        Equal(true, window.IsHidden, "hidden at start");
        var alphas = new List<int>();
        var hidden = new List<bool>();
        for (var frame = 1; frame <= 10; frame++)
        {
            full.Step();
            var output = new List<UiDrawCommand>();
            full.AppendDraw(output);
            alphas.Add(((UiFillRect)output[0]).Color.A);
            hidden.Add(window.IsHidden);
            if (frame == 5) Equal("fill 10,20 200x100 0,0,0 a255|border 10,20 200x100 a255", Describe(output), "peak frame: black plus blue frame");
        }
        Equal("51,102,153,204,255,204,153,102,51,0", string.Join(",", alphas), "full fade alpha ramp up then down");
        Equal("True,True,True,True,False,False,False,False,False,False", string.Join(",", hidden), "the window is revealed at the peak");
        Equal(true, full.IsFinished, "finished");
    }

    private static void FactionScalesGrowTowardsTheirTargets()
    {
        var host = new FakeHost();
        var logo = host.Add("M.wnd:Logo", 100, 100, 100, 100);
        var medium = host.Add("M.wnd:LogoMedium", 80, 80, 140, 140);
        var table = Table(Group("G", "MAINMENUMEDIUMSCALEUP", "M.wnd:Logo"));
        var handler = new WindowTransitionHandler(table, host);
        handler.SetGroup("G");
        // Growth per frame: (140 - 100) / 3 = 13 each axis, applied half to each side.
        handler.Step();
        Equal("image Img-M.wnd:Logo 94,94 112x112 a255", Draws(handler), "frame 1: x = 100 - 13*1/2 = 94, right = 200 + 6 = 206");
        handler.Step();
        Equal("image Img-M.wnd:Logo 87,87 126x126 a255", Draws(handler), "frame 2");
        handler.Step();
        Equal(true, logo.IsHidden, "the original is hidden at the end");
        Equal(false, medium.IsHidden, "and the medium window shown");
        Equal(true, handler.IsFinished, "finished after 3 frames");
        Equal(new[] { "GUILogoMouseOver" }, host.Sounds.ToArray(), "hover sound");
        // Reverse: both hidden at once, then the original is back at the start.
        handler.Reverse("G");
        Equal(true, handler.CurrentIsReversed, "reversed");
        var guard = 0;
        while (!handler.IsIdle && guard++ < 20) handler.Step();
        Equal(false, logo.IsHidden, "the logo is shown again after the reverse");
        Equal(true, medium.IsHidden, "the grow window is hidden again");

        var smallHost = new FakeHost();
        var small = smallHost.Add("M.wnd:Logo", 100, 100, 100, 100);
        var smallTarget = smallHost.Add("M.wnd:LogoSmall", 120, 120, 60, 60);
        var smallHandler = new WindowTransitionHandler(Table(Group("G", "MAINMENUSMALLSCALEDOWN", "M.wnd:Logo")), smallHost);
        smallHandler.SetGroup("G");
        Equal("Img-M.wnd:Logo", smallTarget.EnabledImage, "the small window receives the logo image");
        smallHandler.Step();
        // (60 - 100) / 6 = -6 per frame; half each side: x = 100 - (-6)/2 = 103, right = 200 + (-6)/2 = 197.
        Equal("image Img-M.wnd:Logo 103,103 94x94 a255", Draws(smallHandler), "small scale-down frame 1");
        for (var frame = 2; frame <= 6; frame++) smallHandler.Step();
        Equal(true, small.IsHidden, "original hidden at the end");
        Equal(false, smallTarget.IsHidden, "small window shown at the end");

        // MAINMENUSCALEUP needs its marker window; without it the effect ends at once instead of blocking a screen.
        var noMarker = new FakeHost();
        noMarker.Add("A");
        var lost = new WindowTransitionHandler(Table(Group("G", "MAINMENUSCALEUP")), noMarker);
        lost.SetGroup("G");
        Equal(true, lost.IsFinished, "no marker, no wait");
        var withMarker = new FakeHost();
        var button = withMarker.Add("A", 10, 10, 100, 50);
        var marker = withMarker.Add("MainMenu.wnd:WinGrowMarker", 210, 110, 200, 150);
        var grow = new WindowTransitionHandler(Table(Group("G", "MAINMENUSCALEUP")), withMarker);
        grow.SetGroup("G");
        Equal("Dis-A", marker.EnabledImage, "the marker takes the disabled image of the window");
        grow.Step();
        // increments: pos (200/5, 100/5) = (40, 20), size (100/5, 100/5) = (20, 20).
        Equal("image Dis-A 50,30 120x70 a255", Draws(grow), "frame 1 flies towards the marker");
        for (var frame = 2; frame <= 5; frame++) grow.Step();
        Equal(true, button.IsHidden, "button hidden at the end");
        Equal(false, marker.IsHidden, "marker shown at the end");
        Equal(new[] { "GUILogoSelect" }, withMarker.Sounds.ToArray(), "select sound");
    }

    private static void CountUpTypeTextAndTextOnFrame()
    {
        // COUNTUP: 250 counts in steps of 100 (250 / 100 = 2 frames), 17 in steps of 1.
        var host = new FakeHost();
        var big = host.Add("Big");
        SetText(big, "250");
        var small = host.Add("Small");
        SetText(small, "17");
        var zero = host.Add("Zero");
        SetText(zero, "0");
        var absent = host.Add("Absent", hidden: true);
        SetText(absent, "99");
        var handler = new WindowTransitionHandler(Table(
            "WindowTransition G\n Window\n WinName = Big\n Style = COUNTUP\n FrameDelay = 1\n END\n Window\n WinName = Small\n Style = COUNTUP\n FrameDelay = 1\n END\n Window\n WinName = Zero\n Style = COUNTUP\n FrameDelay = 1\n END\n Window\n WinName = Absent\n Style = COUNTUP\n FrameDelay = 1\n END\n FireOnce = Yes\nEND\n"), host);
        handler.SetGroup("G");
        Equal("0", big.Text, "counters start at 0");
        Equal(true, big.IsHidden, "and hidden");
        handler.Step();
        Equal(true, big.IsHidden, "the group frame before the counters' first frame shows nothing yet");
        Equal("0", zero.Text, "a zero counter shows its text");
        Equal(false, zero.IsHidden, "right on its first frame");
        handler.Step();
        Equal("100", big.Text, "250 after counting frame 1: first hundred");
        Equal("1", small.Text, "17 after counting frame 1");
        Equal(false, small.IsHidden, "shown while counting");
        handler.Step();
        Equal("250", big.Text, "the full text at the end frame");
        Equal("2", small.Text, "17 after counting frame 2");
        for (var frame = 3; frame <= 16; frame++) handler.Step();
        Equal("16", small.Text, "17 after counting frame 16");
        Equal(false, handler.IsFinished, "not finished before the last counting frame");
        handler.Step();
        Equal("17", small.Text, "the full text at counting frame 17");
        Equal(true, handler.IsFinished, "finished when the longest counter is");
        Equal(true, absent.IsHidden, "a window hidden at init stays hidden");
        Equal("99", absent.Text, "and keeps its text");
        Equal(17, host.Sounds.Count(sound => sound == "GUIScoreScreenTick"), "one tick per counting frame: 16 for 17, 1 for 250");

        // TEXTONFRAME: hidden at the start, shown on the frame after its delay; hidden windows stay out.
        var textHost = new FakeHost();
        var label = textHost.Add("Label");
        var skipped = textHost.Add("Skipped", hidden: true);
        var textHandler = new WindowTransitionHandler(Table("WindowTransition G\n Window\n WinName = Label\n Style = TEXTONFRAME\n FrameDelay = 3\n END\n Window\n WinName = Skipped\n Style = TEXTONFRAME\n FrameDelay = 0\n END\nEND\n"), textHost);
        textHandler.SetGroup("G");
        Equal(true, label.IsHidden, "label hidden at the start");
        textHandler.Step();
        textHandler.Step();
        textHandler.Step();
        Equal(true, label.IsHidden, "still hidden on group frame 3 (its start frame)");
        textHandler.Step();
        Equal(false, label.IsHidden, "shown on group frame 4 (delay + 1)");
        Equal(true, skipped.IsHidden, "the window that was hidden stays hidden");
        Equal(true, textHandler.IsFinished, "finished");

        // TYPETEXT: ABCD types A, AB, ABC and shows the window with the full text on frame 4.
        var typeHost = new FakeHost();
        var typed = typeHost.Add("Typed");
        SetText(typed, "ABCD");
        var typeHandler = new WindowTransitionHandler(Table(Group("G", "TYPETEXT", "Typed")), typeHost);
        typeHandler.SetGroup("G");
        Equal(true, typed.IsHidden, "hidden while typing");
        typeHandler.Step();
        Equal("text 'A'", Draws(typeHandler), "first character");
        typeHandler.Step();
        typeHandler.Step();
        Equal("text 'ABC'", Draws(typeHandler), "third frame");
        typeHandler.Step();
        Equal("", Draws(typeHandler), "no typed text on the last frame");
        Equal(false, typed.IsHidden, "window shown with its full text");
        Equal(3, typeHost.Sounds.Count(sound => sound == "GUITypeText"), "a type sound per typed frame");
        typeHandler.Reverse("G");
        var reversed = new List<string>();
        var guard = 0;
        while (!typeHandler.IsIdle && guard++ < 20)
        {
            typeHandler.Step();
            reversed.Add(Draws(typeHandler));
        }
        Equal("text 'ABC'|text 'AB'|text 'A'|", string.Join("|", reversed.Take(4)).TrimEnd('|') + "|", "reversed typing deletes one character per frame");
        Equal(true, typed.IsHidden, "hidden after the reverse");
    }

    private static void SetText(FakeWindow window, string text) => window.SetText(text);

    private static void ControlBarArrowSlidesThenFades()
    {
        var host = new FakeHost { ControlBarArrowImage = "Arrow" };
        host.Add("ControlBar.wnd:ButtonGeneral", 160, 480, 40, 40);
        var handler = new WindowTransitionHandler(Table("WindowTransition G\n Window\n Style = CONTROLBARARROW\n END\n FireOnce = Yes\nEND\n"), host);
        handler.SetGroup("G");
        handler.Step();
        // arrow 32x24; start y = -24 + 20 = -4; step = 480 / 16 = 30; x = 160 + 20 - 16 = 164.
        Equal("image Arrow 164,26 32x24 a255", Draws(handler), "frame 1 slides in");
        for (var frame = 2; frame <= 15; frame++) handler.Step();
        Equal("image Arrow 164,446 32x24 a255", Draws(handler), "frame 15");
        handler.Step();
        Equal("image Arrow 164,446 32x24 a255", Draws(handler), "frame 16 starts to fade from the last position");
        for (var frame = 17; frame <= 22; frame++) handler.Step();
        Equal(true, handler.IsFinished, "finished after 22 frames");

        var noImage = new WindowTransitionHandler(Table("WindowTransition G\n Window\n Style = CONTROLBARARROW\n END\nEND\n"), new FakeHost());
        noImage.SetGroup("G");
        Equal(true, noImage.IsFinished, "without arrow image or general button the group is finished at once");
    }

    private static void ReverseSoundFiresOnItsFrame()
    {
        var host = new FakeHost();
        host.Add("A");
        var handler = new WindowTransitionHandler(Table(Group("G", "REVERSESOUND")), host);
        handler.SetGroup("G");
        Equal(true, handler.IsFinished, "finished from the start when played forward");
        handler.Step();
        handler.Step();
        Equal(0, host.Sounds.Count, "a finished group is not updated, so a lone sound window is silent forward");
        handler.Reverse("G");
        var guard = 0;
        while (!handler.IsIdle && guard++ < 10) handler.Step();
        Equal(1, host.Sounds.Count(sound => sound == "GUITransitionFade"), "and once when reversed");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Handler

    private static void HandlerQueuesTheNextGroupBehindARunningOne()
    {
        var host = new FakeHost();
        var a = host.Add("A");
        var b = host.Add("B");
        var table = Table(Group("One", "FLASH", "A") + Group("Two", "FLASH", "B"));
        var handler = new WindowTransitionHandler(table, host);
        handler.SetGroup("One");
        handler.Step();
        handler.Step();
        handler.Step();
        handler.Step();
        handler.Step();
        Equal(false, a.IsHidden, "One is visible from frame 4 on");
        handler.SetGroup("Two");
        Equal("One", handler.CurrentGroup, "One keeps running (reversing)");
        Equal("Two", handler.PendingGroup, "Two is pending");
        Equal(true, handler.CurrentIsReversed, "One is reversed to make room");
        Equal(true, b.IsHidden, "Two's window is hidden by its init");
        var guard = 0;
        while (handler.CurrentGroup == "One" && guard++ < 40) handler.Step();
        Equal("Two", handler.CurrentGroup, "Two takes over when One has finished reversing");
        Equal(true, a.IsHidden, "One ends hidden");
        handler.Settle();
        Equal(false, b.IsHidden, "Two ends visible");
        Equal(true, handler.IsSettled, "settled at the end");

        // FireOnce groups are not reversed by a following group.
        var fireHost = new FakeHost();
        var f = fireHost.Add("A");
        var g = fireHost.Add("B");
        var fireHandler = new WindowTransitionHandler(Table(Group("One", "FLASH", "A", fireOnce: true) + Group("Two", "FLASH", "B")), fireHost);
        fireHandler.SetGroup("One");
        fireHandler.Step();
        fireHandler.SetGroup("Two");
        Equal(false, fireHandler.CurrentIsReversed, "a FireOnce group is not reversed");
        fireHandler.Settle();
        Equal(false, f.IsHidden, "One finished forward");
        Equal(false, g.IsHidden, "Two finished forward after it");

        // Immediate: the running group is skipped to its end and replaced at once.
        var imHost = new FakeHost();
        var x = imHost.Add("A");
        var y = imHost.Add("B");
        var imHandler = new WindowTransitionHandler(Table(Group("One", "BUTTONFLASH", "A") + Group("Two", "BUTTONFLASH", "B")), imHost);
        imHandler.SetGroup("One");
        imHandler.Step();
        imHandler.SetGroup("Two", immediate: true);
        Equal(false, x.IsHidden, "skipped group ends visible");
        Equal("Two", imHandler.CurrentGroup, "replaced immediately");
        Equal(true, y.IsHidden, "new group hidden at its start");
        imHandler.SetGroup("", immediate: true);
        Equal(null, imHandler.CurrentGroup, "an empty immediate group clears the slot");
    }

    private static void HandlerReversePlaysAGroupBackwardsFromItsEnd()
    {
        var host = new FakeHost();
        var a = host.Add("A");
        var b = host.Add("B");
        var table = Table("WindowTransition G\n Window\n WinName = A\n Style = FLASH\n FrameDelay = 0\n END\n Window\n WinName = B\n Style = FLASH\n FrameDelay = 6\n END\nEND\n");
        var handler = new WindowTransitionHandler(table, host);
        handler.Reverse("G");
        Equal(false, a.IsHidden, "A shown by the implicit skip");
        Equal(false, b.IsHidden, "B shown by the implicit skip");
        // total = 6 + 8 = 14: B (delay 6) runs from its frame 8 down to 0 while A (8 long) waits for frame 8.
        var aHiddenAt = -1;
        var bHiddenAt = -1;
        for (var frame = 1; frame <= 20 && !handler.IsIdle; frame++)
        {
            handler.Step();
            if (aHiddenAt < 0 && a.IsHidden) aHiddenAt = frame;
            if (bHiddenAt < 0 && b.IsHidden) bHiddenAt = frame;
        }
        Check(bHiddenAt > 0 && aHiddenAt > 0, "both windows end hidden");
        Check(bHiddenAt < aHiddenAt, $"B (frames 14..6) starts flashing out before A (frames 8..0): B hidden at {bHiddenAt}, A at {aHiddenAt}");
        Equal(true, handler.IsIdle, "the reversed group is dropped once finished");
        Equal(false, handler.AnyOverrun, "no group ran below frame 0");
    }

    private static void HandlerRemoveAndFireOnceBehaveLikeTheOriginal()
    {
        var host = new FakeHost();
        var a = host.Add("A");
        var handler = new WindowTransitionHandler(Table(Group("G", "FLASH", "A", fireOnce: true)), host);
        handler.SetGroup("G");
        handler.Step();
        Equal(true, a.IsHidden, "mid-flash");
        handler.Remove("G");
        Equal(false, a.IsHidden, "remove skips the group to its end state");
        Equal(null, handler.CurrentGroup, "and drops it");
        Equal(true, handler.IsFinished, "nothing runs");

        // FireOnce: dropped as soon as it finishes; the last frame stays drawable for one more step.
        var (_, once, window) = One("FLASH", fireOnce: true);
        once.SetGroup("G");
        for (var frame = 1; frame <= 8; frame++) once.Step();
        Equal(null, once.CurrentGroup, "FireOnce group leaves the slot when finished");
        Equal(false, window.IsHidden, "and the window is visible");

        // A pending group is dropped by Reverse of itself.
        var pendingHost = new FakeHost();
        pendingHost.Add("A");
        pendingHost.Add("B");
        var pendingHandler = new WindowTransitionHandler(Table(Group("One", "FLASH", "A") + Group("Two", "FLASH", "B")), pendingHost);
        pendingHandler.SetGroup("One");
        pendingHandler.Step();
        pendingHandler.SetGroup("Two");
        pendingHandler.Reverse("Two");
        Equal(null, pendingHandler.PendingGroup, "reversing the pending group cancels it");
    }

    private static void HandlerClockTurnsSecondsIntoWholeFrames()
    {
        var (_, handler, _) = One("FLASH");
        handler.SetGroup("G");
        Equal(0, handler.Advance(0.0), "no time, no frames");
        Equal(0, handler.Advance(0.03), "0.9 frames do not step");
        Equal(1, handler.Advance(0.004), "the carry completes the first frame");
        Equal(3, handler.Advance(0.1), "0.1 s are 3 frames at 30 per second");
        Equal(4L, handler.FramesStepped, "frames stepped");
        var carried = 0;
        for (var i = 0; i < 30; i++) carried += handler.Advance(1.0 / 60);
        Equal(15, carried, "30 half-frame ticks make 15 frames");
        Check(handler.IsFinished || handler.IsIdle, "the 8-frame flash is over");
        try
        {
            handler.Advance(-1);
            Check(false, "negative time must throw");
        }
        catch (ArgumentOutOfRangeException) { }
    }

    private static void HandlerNeverIgnoresUnknownNames()
    {
        var host = new FakeHost();
        var handler = new WindowTransitionHandler(Table(Group("Known", "FLASH", "A") + Group("Lost", "FLASH", "Gone.wnd:Nothing")), host);
        try
        {
            handler.SetGroup("Unknown");
            Check(false, "an unknown group must throw");
        }
        catch (KeyNotFoundException) { }
        try
        {
            handler.Reverse("Unknown");
            Check(false, "reversing an unknown group must throw");
        }
        catch (KeyNotFoundException) { }
        handler.SetGroup("Lost");
        Equal("Gone.wnd:Nothing", string.Join(",", handler.MissingWindows), "a window that is not there is recorded");
        handler.Settle();
        Equal(true, handler.IsSettled, "and the group still ends instead of hanging");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Integration with a real window tree

    private static string ThreeWindowLayout()
    {
        var threePiece = Draw("ENABLEDDRAWDATA", Entry("Left", "255 255 255 255", "0 0 0 0"), Entry("NoImage", "0 0 0 0", "0 0 0 0"), Entry("NoImage", "0 0 0 0", "0 0 0 0"),
                Entry("NoImage", "0 0 0 0", "0 0 0 0"), Entry("NoImage", "0 0 0 0", "0 0 0 0"), Entry("Middle", "255 255 255 255", "0 0 0 0"), Entry("Right", "255 255 255 255", "0 0 0 0"))
            + Draw("DISABLEDDRAWDATA", Entry("Left", "255 255 255 255", "0 0 0 0")) + Draw("HILITEDRAWDATA", Entry("Left", "255 255 255 255", "0 0 0 0"));
        return WndFile(Window(new Spec("USER", "T.wnd:Panel", 0, 0, 400, 300)
        {
            Status = "ENABLED+IMAGE",
            Children =
            [
                new Spec("PUSHBUTTON", "T.wnd:First", 20, 20, 220, 60) { Status = "ENABLED+IMAGE", Text = "Go", DrawData = threePiece },
                new Spec("PUSHBUTTON", "T.wnd:Second", 20, 80, 220, 120) { Status = "ENABLED", Text = "Stop" },
            ],
        }));
    }

    private static void TransitionsDriveARealWindowTree()
    {
        var images = new FakeImages(new Dictionary<string, (int, int)> { ["Left"] = (20, 40), ["Middle"] = (10, 40), ["Right"] = (20, 40), ["Gradient"] = (8, 8) });
        var layout = WndParser.Parse(ThreeWindowLayout(), "T.wnd");
        Check(layout.Diagnostics.All(diagnostic => diagnostic.IsExplained), "fixture diagnostics are explained");
        var tree = new UiWindowTree(new UiViewport(800, 600), UiScaleOptions.Original, new UiTreeServices(new MonoMeasure()) { Images = images });
        tree.AddLayout(layout);
        var sounds = new List<string>();
        var host = new UiTransitionHost(tree, sounds.Add);
        var table = Table("WindowTransition In\n Window\n WinName = T.wnd:Panel\n Style = FLASH\n FrameDelay = 0\n END\n Window\n WinName = T.wnd:First\n Style = BUTTONFLASH\n FrameDelay = 2\n END\nEND\n");
        var handler = new WindowTransitionHandler(table, host);
        var resting = tree.BuildDrawList().Dump();

        handler.SetGroup("In");
        Check(tree.FindByName("T.wnd:Panel")!.IsHidden && tree.FindByName("T.wnd:First")!.IsHidden, "the group hides both windows when it starts");
        Equal(0, tree.BuildDrawList().Commands.Length, "a hidden panel draws nothing");

        handler.Step();
        handler.Step();
        handler.Step();
        var duringFlash = new List<UiDrawCommand>();
        handler.AppendDraw(duringFlash);
        Check(duringFlash.Any(command => command is UiBorderRect), "the stand-in overlay is produced while windows are hidden");

        for (var i = 0; i < 8; i++) handler.Step();
        var mid = new List<UiDrawCommand>();
        handler.AppendDraw(mid);
        Check(mid.OfType<UiImageDraw>().Any(image => image.Image == "Left"), "the push button's left/middle/right images are drawn as the stand-in: " + Describe(mid));
        var tinted = tree.BuildPushButtonImages(tree.FindByName("T.wnd:First")!, 100).OfType<UiImageDraw>().ToArray();
        Check(tinted.Length >= 3 && tinted.All(image => image.Tint is { R: 255, G: 255, B: 255, A: 100 }), "PushButtonImageDrawThree tints every piece with the given alpha");

        handler.Settle();
        Equal(resting, tree.BuildDrawList().Dump(), "after the group the tree draws exactly its resting list");
        Equal(0, handler.MissingWindows.Count, "no missing windows");
        Check(sounds.Contains("GUIBoarderFadeIn") && sounds.Contains("GUIButtonsFadeIn"), "sounds were reported");

        // Hidden windows are not hit: a click during the fade-in must not select the button.
        handler = new WindowTransitionHandler(table, host);
        handler.SetGroup("In");
        handler.Step();
        var first = tree.FindByName("T.wnd:First")!;
        Check(first.IsHidden, "hidden again");
        tree.MouseDown(first.Rect.CenterX, first.Rect.CenterY, UiMouseButton.Left);
        tree.MouseUp(first.Rect.CenterX, first.Rect.CenterY, UiMouseButton.Left);
        Equal(0, tree.PendingEvents.Count, "input on a window that is still flashing in is not delivered");
        handler.Settle();
        tree.MouseDown(first.Rect.CenterX, first.Rect.CenterY, UiMouseButton.Left);
        tree.MouseUp(first.Rect.CenterX, first.Rect.CenterY, UiMouseButton.Left);
        Check(tree.PendingEvents.Any(e => e.Kind == UiEventKind.ButtonSelected), "after the transition the button works");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Shipped data

    private static void RealData(string contentDirectory)
    {
        var content = ContentFileSystem.Open(contentDirectory);
        const string path = "Data/INI/WindowTransitions.ini";
        string text;
        using (var stream = content.OpenRead(path))
        using (var reader = new StreamReader(stream)) text = reader.ReadToEnd();
        var table = WindowTransitionTable.Parse(text, path);
        var windows = table.Groups.SelectMany(group => group.Windows).ToArray();
        Console.WriteLine($"Window transitions: {table.Groups.Length} groups, {windows.Length} window entries; styles: "
                          + string.Join(", ", windows.GroupBy(window => window.Style).OrderBy(group => group.Key.ToString(), StringComparer.Ordinal).Select(group => $"{group.Key} {group.Count()}")));
        Equal(56, table.Groups.Length, "group count of the shipped file");
        Equal(15, Enum.GetValues<TransitionStyle>().Length, "style count");
        Check(windows.All(window => Enum.IsDefined(window.Style)), "every style is known");
        Equal(14, windows.Select(window => window.Style).Distinct().Count(), "14 of the 15 styles occur in the shipped data");
        Check(windows.All(window => window.Style != TransitionStyle.MainMenuSmallScaleDown), "MAINMENUSMALLSCALEDOWN is the one style the shipped file does not use");

        // Every layout, by file, to resolve decorated names against real windows.
        var layoutFiles = content.EnumerateFiles("Window/").Where(file => file.EndsWith(".wnd", StringComparison.OrdinalIgnoreCase)).ToArray();
        var known = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in layoutFiles)
        {
            using var stream = content.OpenRead(file);
            var layout = WndParser.Parse(stream, file);
            foreach (var window in layout.AllWindows())
                if (window.Name is { } name)
                {
                    var separator = name.IndexOf(':');
                    var layoutName = name[..separator];
                    if (!known.TryGetValue(layoutName, out var set)) known[layoutName] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(name);
                }
        }
        var unresolved = windows.Where(window => window.WindowName is { } windowName && !(known.TryGetValue(windowName[..windowName.IndexOf(':')], out var names) && names.Contains(windowName))).ToArray();
        var unresolvedByLayout = unresolved.GroupBy(window => window.WindowName![..window.WindowName!.IndexOf(':')]).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key} {group.Count()}").ToArray();
        Console.WriteLine($"  window names that do not resolve in the shipped layouts: {unresolved.Length} ({string.Join(", ", unresolvedByLayout)})");

        // Run every group forward and reversed against a host that has all its windows: nothing throws, nothing overruns, every group ends.
        var overruns = 0;
        var maxFrames = 0;
        foreach (var group in table.Groups)
        {
            var host = new FakeHost();
            foreach (var window in group.Windows.Where(window => window.WindowName is not null))
                if (!host.Windows.ContainsKey(window.WindowName!))
                {
                    var created = host.Add(window.WindowName!, 10, 10, 90, 40);
                    created.SetText("1234");
                }
            // Grow targets of the faction scale styles.
            foreach (var window in group.Windows)
            {
                if (window.Style == TransitionStyle.MainMenuMediumScaleUp) host.Add(window.WindowName + "Medium", 5, 5, 100, 50);
                if (window.Style == TransitionStyle.MainMenuSmallScaleDown) host.Add(window.WindowName + "Small", 20, 20, 50, 20);
                if (window.Style == TransitionStyle.MainMenuScaleUp) host.Add("MainMenu.wnd:WinGrowMarker", 200, 200, 120, 80);
                if (window.Style == TransitionStyle.ControlBarArrow) host.Add("ControlBar.wnd:ButtonGeneral", 300, 500, 40, 40);
            }
            var richHost = host;
            var handler = new WindowTransitionHandler(table, richHost);
            handler.SetGroup(group.Name);
            maxFrames = Math.Max(maxFrames, handler.Settle(2000));
            handler.Reverse(group.Name);
            handler.Settle(2000);
            if (handler.AnyOverrun) overruns++;
        }
        Equal(0, overruns, "no shipped group runs below frame 0 when reversed");
        Console.WriteLine($"  every group plays forward and reversed to the end; longest forward run {maxFrames} frames ({maxFrames / (double)WindowTransitionHandler.FramesPerSecond:F2} s)");
    }
}

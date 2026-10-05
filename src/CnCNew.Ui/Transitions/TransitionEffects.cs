using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Transitions;

/// <summary>
/// One effect of one window, stepped one frame at a time by a <see cref="TransitionWindow"/> (the base class <c>Transition</c> of
/// <c>GameWindowTransitions.h</c>). Frames are relative to the effect's own start (the group frame minus the frame delay);
/// <see cref="Update"/> runs forward from 0 to <see cref="FrameLength"/> and, after <see cref="Reverse"/>, backwards to 0.
/// An effect hides its window while it draws a stand-in on top (the overlay in <see cref="Draw"/>) and shows it again at the
/// end; a reversed effect ends with the window hidden. All frame numbers, alpha tables and size increments are the ones of the
/// original styles (<c>GameWindowTransitionsStyles.cpp</c>), re-implemented without its Direct3D calls.
/// </summary>
public abstract class TransitionEffect
{
    protected TransitionEffect(ITransitionHost host) => Host = host;

    protected ITransitionHost Host { get; }

    protected ITransitionWindow? Win { get; private set; }

    /// <summary>Window position and size captured at <see cref="Init"/> (the original reads them once and draws the stand-in there).</summary>
    protected UiRect Bounds { get; private set; }

    /// <summary>How many frames the effect takes; a few styles change it in <see cref="Init"/> (text length, counted value).</summary>
    public int FrameLength { get; protected set; }

    public bool IsFinished { get; protected set; }

    protected bool IsForward { get; set; } = true;

    protected int DrawState { get; set; } = -1;

    /// <summary>Binds the window and puts it into its start state (hidden for most styles, <c>init</c> calls <c>update(START)</c> in reverse mode).</summary>
    public abstract void Init(ITransitionWindow? win);

    public abstract void Update(int frame);

    public virtual void Reverse()
    {
        IsFinished = false;
        IsForward = false;
    }

    /// <summary>Appends the stand-in for the current frame (nothing for frames and styles that draw none).</summary>
    public abstract void Draw(List<UiDrawCommand> output);

    /// <summary>Jumps to the end state (<c>update(END)</c>); a no-op for a reversed effect, exactly like the original.</summary>
    public abstract void Skip();

    protected void Capture(ITransitionWindow? win)
    {
        if (win is null) return;
        Win = win;
        Bounds = win.Rect;
    }

    /// <summary>The shared <c>init</c> tail: run the start frame as if reversed (that hides the window), then arm the effect forward.</summary>
    protected void ArmForward()
    {
        IsForward = false;
        Update(0);
        IsFinished = false;
        IsForward = true;
        // The original dereferences a missing window (or leaves the effect undefined); an effect without its window has nothing to
        // animate and must not hold up the screen that waits for the group, the handler reports the missing name.
        if (Win is null && NeedsWindow) IsFinished = true;
    }

    /// <summary>False for the styles that act on the whole screen.</summary>
    protected virtual bool NeedsWindow => true;

    protected void CheckRange(int frame, int end, string style)
    {
        if (frame < 0 || frame > end) throw new ArgumentOutOfRangeException(nameof(frame), frame, $"{style}: frame is outside 0..{end}.");
    }

    protected static WndColor White(int alpha) => new(255, 255, 255, (byte)Math.Clamp(alpha, 0, 255));

    protected static WndColor Black(int alpha) => new(0, 0, 0, (byte)Math.Clamp(alpha, 0, 255));
}

/// <summary><c>FLASH</c>: a white rectangle flashes over the (hidden) window for three frames, then fades while the window is back (8 frames, sound <c>GUIBoarderFadeIn</c>).</summary>
public sealed class FlashEffect(ITransitionHost host) : TransitionEffect(host)
{
    private const int End = 8;
    private static readonly int[] BorderAlpha = [0, 100, 150, 200, 250, 250, 250, 250];
    private static readonly int[] FillAlpha = [0, 33, 66, 99, 75, 50, 25, 10];

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        Capture(win);
        ArmForward();
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "FLASH");
        DrawState = -1;
        switch (frame)
        {
            case 0:
                if (IsForward || Win is null) break;
                Win.SetHidden(true);
                IsFinished = true;
                break;
            case 1 or 2 or 3:
                if (frame == 1 && IsForward) Host.PlaySound("GUIBoarderFadeIn");
                if (Win is null) break;
                Win.SetHidden(true);
                DrawState = frame;
                break;
            case >= 4 and <= 7:
                if (Win is null) break;
                Win.SetHidden(false);
                DrawState = frame;
                break;
            case End:
                if (!IsForward || Win is null) break;
                Win.SetHidden(false);
                IsFinished = true;
                break;
        }
    }

    public override void Draw(List<UiDrawCommand> output)
    {
        if (DrawState is < 1 or > 7) return;
        var rect = new UiRect(Bounds.X + 1, Bounds.Y + 1, Bounds.Width - 2, Bounds.Height);
        output.Add(new UiBorderRect(rect, White(BorderAlpha[DrawState])));
        output.Add(new UiFillRect(rect, White(FillAlpha[DrawState])));
    }

    public override void Skip() => Update(End);
}

/// <summary>
/// <c>BUTTONFLASH</c>: a push button builds up with a white flash (frames 1..3, sound <c>GUIButtonsFadeIn</c>), settles into its own
/// image under a fading flash (4..7), waits (8..10, image only) and is finally unhidden under a fading gradient (11..16); 17 frames.
/// Reversed it plays the gradient and flash tables backwards (the draw state maps to its mirror frame).
/// </summary>
public sealed class ButtonFlashEffect(ITransitionHost host) : TransitionEffect(host)
{
    private const int End = 17;
    private const int GradeIn1 = 11, GradeIn2 = 12, GradeOut1 = 13, GradeOut2 = 14, GradeOut3 = 15, GradeOut4 = 16;
    private const int ShowBackground = 18;

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        Capture(win);
        ArmForward();
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "BUTTONFLASH");
        DrawState = -1;
        switch (frame)
        {
            case 0:
                if (IsForward || Win is null) break;
                Win.SetHidden(true);
                IsFinished = true;
                break;
            case 1:
                if (Win is null) break;
                Win.SetHidden(true);
                if (IsForward)
                {
                    Host.PlaySound("GUIButtonsFadeIn");
                    DrawState = frame;
                }
                else DrawState = 7;
                break;
            case 2:
                Hidden(IsForward ? 2 : 6);
                break;
            case 3:
                Hidden(IsForward ? 3 : 5);
                break;
            case 4:
                Hidden(4);
                break;
            case 5:
                Hidden(IsForward ? 5 : 3);
                break;
            case 6:
                Hidden(IsForward ? 6 : 2);
                break;
            case 7:
                Hidden(IsForward ? 7 : 1);
                break;
            case GradeIn1:
                Graded(hidden: !IsForward, IsForward ? GradeIn1 : GradeOut4);
                break;
            case GradeIn2:
                Graded(hidden: !IsForward, IsForward ? GradeIn2 : GradeOut3);
                break;
            case GradeOut1:
                Graded(hidden: !IsForward, IsForward ? GradeOut1 : GradeOut2);
                break;
            case GradeOut2:
                Graded(hidden: !IsForward, IsForward ? GradeOut2 : GradeOut1);
                break;
            case GradeOut3:
                Graded(hidden: false, IsForward ? GradeOut3 : GradeIn2);
                break;
            case GradeOut4:
                Graded(hidden: false, IsForward ? GradeOut4 : GradeIn1);
                break;
            case End:
                if (!IsForward || Win is null) break;
                Win.SetHidden(false);
                IsFinished = true;
                break;
        }
        if (frame is > 7 and < GradeIn1) DrawState = ShowBackground;
    }

    private void Hidden(int state)
    {
        if (Win is null) return;
        Win.SetHidden(true);
        DrawState = state;
    }

    private void Graded(bool hidden, int state)
    {
        if (Win is null) return;
        Win.SetHidden(hidden);
        DrawState = state;
    }

    public override void Draw(List<UiDrawCommand> output)
    {
        var rect = Bounds;
        void Flash(int border, int fill)
        {
            output.Add(new UiBorderRect(rect, White(border)));
            output.Add(new UiFillRect(rect, White(fill)));
        }
        void Gradient(int alpha)
        {
            // The retail data has no mapped image named "Gradient" (findImageByName returns NULL and drawImage(NULL) draws nothing); same here, but noted.
            if (!Host.HasImage("Gradient")) Host.NoteMissingImage("Gradient");
            else output.Add(new UiImageDraw("Gradient", rect, White(alpha), false, false));
        }
        void Button() => Win?.AppendButtonImages(255, output);

        switch (DrawState)
        {
            case 1: Flash(100, 75); break;
            case 2: Flash(150, 150); break;
            case 3: Flash(200, 200); break;
            case 4: Button(); Flash(250, 150); break;
            case 5: Button(); Flash(250, 100); break;
            case 6: Button(); Flash(250, 50); break;
            case 7: Button(); Flash(250, 15); break;
            case GradeIn1:
                if (IsForward) Button();
                Gradient(100);
                break;
            case GradeIn2: Gradient(200); break;
            case GradeOut1:
                if (!IsForward) Button();
                Gradient(150);
                break;
            case GradeOut2:
                if (!IsForward) Button();
                Gradient(100);
                break;
            case GradeOut3:
                if (!IsForward) Button();
                Gradient(50);
                break;
            case GradeOut4:
                if (!IsForward) Button();
                Gradient(17);
                break;
            case ShowBackground: Button(); break;
        }
    }

    public override void Skip() => Update(End);
}

/// <summary><c>WINFADE</c>: the window's own image fades in at alpha 25, 50, ... 225 while the window is hidden (10 frames).</summary>
public sealed class FadeEffect(ITransitionHost host) : TransitionEffect(host)
{
    private const int End = 10;

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        Capture(win);
        ArmForward();
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "WINFADE");
        DrawState = -1;
        switch (frame)
        {
            case 0:
                if (IsForward || Win is null) break;
                Win.SetHidden(true);
                IsFinished = true;
                break;
            case End:
                if (!IsForward || Win is null) break;
                Win.SetHidden(false);
                IsFinished = true;
                break;
            default:
                Win?.SetHidden(true);
                DrawState = frame;
                break;
        }
    }

    public override void Draw(List<UiDrawCommand> output)
    {
        if (Win?.EnabledImage is not { } image || DrawState is < 1 or > 9) return;
        output.Add(new UiImageDraw(image, Bounds, White(25 * DrawState), false, false));
    }

    public override void Skip() => Update(End);
}

/// <summary>
/// The two window scale-up styles share their maths: the window's image grows from its centre in six steps of a sixth of its size
/// (frames 1..5 drawn, the window is hidden meanwhile; 6 frames). <c>WINSCALEUP</c> fires <c>GUILogoMouseOver</c> on frame 1,
/// <c>SCORESCALEUP</c> <c>GUIScoreScreenPictures</c>.
/// </summary>
public sealed class ScaleUpEffect(ITransitionHost host, string soundOnFirstFrame) : TransitionEffect(host)
{
    private const int End = 6;
    private int _centerX, _centerY, _incrementX, _incrementY;

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        Capture(win);
        ArmForward();
        _centerX = Bounds.X + Bounds.Width / 2;
        _centerY = Bounds.Y + Bounds.Height / 2;
        _incrementX = Bounds.Width / End;
        _incrementY = Bounds.Height / End;
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "SCALEUP");
        DrawState = -1;
        switch (frame)
        {
            case 0:
                if (IsForward || Win is null) break;
                Win.SetHidden(true);
                IsFinished = true;
                break;
            case End:
                if (!IsForward || Win is null) break;
                Win.SetHidden(false);
                IsFinished = true;
                break;
            default:
                if (frame == 1 && IsForward) Host.PlaySound(soundOnFirstFrame);
                Win?.SetHidden(true);
                DrawState = frame;
                break;
        }
    }

    public override void Draw(List<UiDrawCommand> output)
    {
        if (Win?.EnabledImage is not { } image || DrawState <= 0 || DrawState >= End) return;
        var x = _centerX - _incrementX * DrawState / 2;
        var y = _centerY - _incrementY * DrawState / 2;
        output.Add(new UiImageDraw(image, new UiRect(x, y, _incrementX * DrawState, _incrementY * DrawState), WndColor.White, false, false));
    }

    public override void Skip() => Update(End);
}

/// <summary>
/// <c>MAINMENUSCALEUP</c>: the window's disabled image flies and grows into the grow marker (<c>MainMenu.wnd:WinGrowMarker</c>) in five
/// steps, then the marker window stays (sound <c>GUILogoSelect</c> on frame 1). Without a marker window nothing happens and the effect
/// is finished at once (the original would leave it undefined).
/// </summary>
public sealed class MainMenuScaleUpEffect(ITransitionHost host) : TransitionEffect(host)
{
    private const int End = 5;
    private ITransitionWindow? _grow;
    private UiRect _growBounds;
    private int _incrementPosX, _incrementPosY, _incrementSizeX, _incrementSizeY;
    private string? _image;

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        Capture(win);
        _grow = Host.Find("MainMenu.wnd:WinGrowMarker");
        if (_grow is null || Win is null)
        {
            IsFinished = true;
            return;
        }
        _growBounds = _grow.Rect;
        ArmForward();
        _incrementPosX = (_growBounds.X - Bounds.X) / End;
        _incrementPosY = (_growBounds.Y - Bounds.Y) / End;
        _incrementSizeX = (_growBounds.Width - Bounds.Width) / End;
        _incrementSizeY = (_growBounds.Height - Bounds.Height) / End;
        _image = Win.DisabledImage;
        _grow.SetEnabledImage(_image);
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "MAINMENUSCALEUP");
        DrawState = -1;
        switch (frame)
        {
            case 0:
                if (IsForward || Win is null || _grow is null) break;
                _grow.SetHidden(true);
                IsFinished = true;
                break;
            case End:
                if (!IsForward || Win is null || _grow is null) break;
                Win.SetHidden(true);
                _grow.SetHidden(false);
                IsFinished = true;
                break;
        }
        if (frame == 1) Host.PlaySound("GUILogoSelect");
        if (frame is > 0 and < End)
        {
            Win?.SetHidden(true);
            _grow?.SetHidden(true);
            DrawState = frame;
        }
    }

    public override void Draw(List<UiDrawCommand> output)
    {
        if (Win is null || _image is null || DrawState <= 0 || DrawState >= End) return;
        var x = Bounds.X + _incrementPosX * DrawState;
        var y = Bounds.Y + _incrementPosY * DrawState;
        output.Add(new UiImageDraw(_image, new UiRect(x, y, Bounds.Width + _incrementSizeX * DrawState, Bounds.Height + _incrementSizeY * DrawState), WndColor.White, false, false));
    }

    public override void Skip() => Update(End);
}

/// <summary>
/// <c>MAINMENUMEDIUMSCALEUP</c> / <c>MAINMENUSMALLSCALEDOWN</c>: the faction logo <c>WinFactionX</c> grows (or shrinks) around its centre towards
/// the window named like it plus <c>Medium</c> (or <c>Small</c>); at the end that window replaces it. Medium has 3 frames, small 6. The medium
/// style hides both windows when it is reversed and shows the original on its last reverse frame.
/// </summary>
public sealed class FactionScaleEffect(ITransitionHost host, bool medium) : TransitionEffect(host)
{
    private int End => medium ? 3 : 6;
    private ITransitionWindow? _grow;
    private int _incrementX, _incrementY;

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        Capture(win);
        if (Win is null) { IsFinished = true; return; }
        _grow = Host.Find(Win.Name + (medium ? "Medium" : "Small"));
        if (_grow is null) { IsFinished = true; return; }
        var grow = _grow.Rect;
        ArmForward();
        _incrementX = (grow.Width - Bounds.Width) / End;
        _incrementY = (grow.Height - Bounds.Height) / End;
        if (!medium) _grow.SetEnabledImage(Win.EnabledImage);
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, medium ? "MAINMENUMEDIUMSCALEUP" : "MAINMENUSMALLSCALEDOWN");
        DrawState = -1;
        if (frame == 0)
        {
            if (!IsForward && Win is not null && _grow is not null)
            {
                Win.SetHidden(false);
                _grow.SetHidden(true);
                IsFinished = true;
            }
        }
        else if (frame == End)
        {
            if (IsForward && Win is not null && _grow is not null)
            {
                Win.SetHidden(true);
                _grow.SetHidden(false);
                IsFinished = true;
            }
        }
        else
        {
            if (medium && frame == 1 && IsForward) Host.PlaySound("GUILogoMouseOver");
            Win?.SetHidden(true);
            _grow?.SetHidden(true);
            DrawState = frame;
        }
    }

    public override void Reverse()
    {
        base.Reverse();
        if (!medium) return;
        Win?.SetHidden(true);
        _grow?.SetHidden(true);
    }

    public override void Draw(List<UiDrawCommand> output)
    {
        if (Win?.EnabledImage is not { } image || DrawState <= 0 || DrawState >= End) return;
        var x = Bounds.X - _incrementX * DrawState / 2;
        var y = Bounds.Y - _incrementY * DrawState / 2;
        var x1 = Bounds.X + Bounds.Width + _incrementX * DrawState / 2;
        var y1 = Bounds.Y + Bounds.Height + _incrementY * DrawState / 2;
        output.Add(new UiImageDraw(image, UiRect.FromEdges(x, y, x1, y1), WndColor.White, false, false));
    }

    public override void Skip() => Update(End);
}

/// <summary><c>SCREENFADE</c> (<c>FadeWholeScreen</c>): a black rectangle over the whole display whose alpha ramps up over 30 frames (reversed: down); finishes at both ends.</summary>
public sealed class ScreenFadeEffect(ITransitionHost host) : TransitionEffect(host)
{
    protected override bool NeedsWindow => false;

    private const int End = 30;
    private const float Percent = 1.0f / (End - 1);

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        ArmForward();
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "SCREENFADE");
        if (frame is 0 or End) IsFinished = true;
        DrawState = frame;
    }

    public override void Draw(List<UiDrawCommand> output)
    {
        if (DrawState < 0) return;
        var alpha = Math.Min(255, (int)(Percent * 255 * DrawState));
        output.Add(new UiFillRect(new UiRect(0, 0, Host.DisplayWidth, Host.DisplayHeight), Black(alpha)));
    }

    public override void Skip() => Update(End);
}

/// <summary><c>FULLFADE</c>: a screen change fade on the window's rectangle - black ramps up to full over 5 frames (the window is hidden until the peak), then down again over 5 (10 frames); with a thin blue frame of the same alpha.</summary>
public sealed class FullFadeEffect(ITransitionHost host) : TransitionEffect(host)
{
    private const int End = 10;
    private const float Percent = 1.0f / (End / 2);

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        Capture(win);
        ArmForward();
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "FULLFADE");
        switch (frame)
        {
            case 0:
                if (IsForward || Win is null) break;
                Win.SetHidden(true);
                IsFinished = true;
                break;
            case End:
                if (!IsForward || Win is null) break;
                Win.SetHidden(false);
                IsFinished = true;
                break;
        }
        if (frame == End / 2) Win?.SetHidden(!IsForward);
        DrawState = frame;
    }

    public override void Draw(List<UiDrawCommand> output)
    {
        if (DrawState < 0) return;
        var alpha = Math.Min(255, (int)(DrawState > End / 2 ? Percent * 255 * (End - DrawState) : Percent * 255 * DrawState));
        output.Add(new UiFillRect(Bounds, Black(alpha)));
        output.Add(new UiBorderRect(Bounds, new WndColor(60, 60, 180, (byte)alpha)));
    }

    public override void Skip() => Update(End);
}

/// <summary><c>TEXTONFRAME</c>: the window (a score row label) appears on its start frame, with no draw of its own; a window that is hidden at init stays hidden (that is how rows of absent players stay empty).</summary>
public sealed class TextOnFrameEffect(ITransitionHost host) : TransitionEffect(host)
{
    private const int End = 1;

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        Capture(win);
        if (Win is null || Win.IsHidden)
        {
            IsFinished = true;
            IsForward = true;
            FrameLength = 0;
            return;
        }
        ArmForward();
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "TEXTONFRAME");
        switch (frame)
        {
            case 0:
                if (IsForward || Win is null) break;
                Win.SetHidden(true);
                IsFinished = true;
                break;
            case End:
                if (!IsForward || Win is null) break;
                Win.SetHidden(false);
                IsFinished = true;
                break;
        }
    }

    public override void Reverse()
    {
        base.Reverse();
        if (Win is null || !Win.IsHidden) return;
        IsFinished = true;
        FrameLength = 0;
    }

    public override void Draw(List<UiDrawCommand> output) { }

    public override void Skip()
    {
        if (!IsFinished) Update(End);
    }
}

/// <summary><c>REVERSESOUND</c>: no picture; only fires <c>GUITransitionFade</c> on its frame 1 (also forward, like the original) and is finished from the start unless reversed (2 frames).</summary>
public sealed class ReverseSoundEffect(ITransitionHost host) : TransitionEffect(host)
{
    private const int End = 2;

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        IsFinished = true;
        IsForward = true;
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "REVERSESOUND");
        switch (frame)
        {
            case 0:
                if (!IsForward) IsFinished = true;
                break;
            case 1:
                Host.PlaySound("GUITransitionFade");
                if (IsForward) IsFinished = true;
                break;
            case End:
                if (IsForward) IsFinished = true;
                break;
        }
    }

    public override void Draw(List<UiDrawCommand> output) { }

    public override void Skip()
    {
        if (!IsFinished) Update(End);
    }
}

/// <summary>
/// <c>COUNTUP</c>: a score number counts up from 0 in steps of 1, 100 or 1000 (one step per frame, at most 30 frames, sound
/// <c>GUIScoreScreenTick</c>), then shows its full text. A window that is hidden at init is left alone for the whole group
/// (the original would unhide it on its start frame).
/// </summary>
public sealed class CountUpEffect(ITransitionHost host) : TransitionEffect(host)
{
    private const int End = 30;
    private string _fullText = string.Empty;
    private int _value, _current, _step = 1;
    private bool _inert;

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        Capture(win);
        if (Win is null || Win.IsHidden)
        {
            _inert = true;
            IsForward = true;
            IsFinished = true;
            FrameLength = 0;
            return;
        }
        _fullText = Win.Text;
        ArmForward();
        _value = ParseLeadingInteger(_fullText);
        if (_value < End) { _step = 1; FrameLength = Math.Min(_value, End); }
        else if (_value / 100 < End) { _step = 100; FrameLength = Math.Min(_value / 100, End); }
        else { _step = 1000; FrameLength = Math.Min(_value / 1000, End); }
        _current = 0;
        Win.SetText("0");
    }

    /// <summary><c>atoi</c>: optional sign and digits at the start, 0 otherwise; saturates instead of overflowing.</summary>
    internal static int ParseLeadingInteger(string text)
    {
        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        var negative = false;
        if (index < text.Length && (text[index] is '+' or '-')) negative = text[index++] == '-';
        long value = 0;
        while (index < text.Length && text[index] is >= '0' and <= '9')
        {
            value = Math.Min(value * 10 + (text[index++] - '0'), int.MaxValue);
        }
        return (int)(negative ? -value : value);
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "COUNTUP");
        DrawState = -1;
        if (_inert || Win is null) return;
        switch (frame)
        {
            case 0:
                if (IsForward) break;
                _current = 0;
                Win.SetText("0");
                Win.SetHidden(true);
                IsFinished = true;
                break;
            case End:
                if (!IsForward) break;
                Win.SetHidden(false);
                IsFinished = true;
                break;
        }
        if (frame >= FrameLength) Win.SetHidden(false);
        if (frame > 0 && frame < FrameLength)
        {
            Win.SetHidden(false);
            DrawState = frame;
            Host.PlaySound("GUIScoreScreenTick");
            _current += _step;
            if (_current > _value) _current = _value;
            Win.SetText(_current.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (frame == FrameLength)
        {
            Win.SetText(_fullText);
            IsFinished = true;
        }
    }

    public override void Reverse()
    {
        if (Win is null || _inert)
        {
            IsForward = false;
            IsFinished = true;
            FrameLength = 0;
            return;
        }
        base.Reverse();
    }

    public override void Draw(List<UiDrawCommand> output) { }

    public override void Skip()
    {
        if (!IsFinished) Update(End);
    }
}

/// <summary>
/// <c>TYPETEXT</c>: the text of a static text is typed in one character per frame (sound <c>GUITypeText</c>), at most 30 frames; the typed
/// part is drawn like the window's own text while the window is hidden. Reversed it deletes one character per frame.
/// </summary>
public sealed class TypeTextEffect(ITransitionHost host) : TransitionEffect(host)
{
    private const int End = 30;
    private string _fullText = string.Empty;
    private string _partial = string.Empty;

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        Capture(win);
        ArmForward();
        if (Win is null) return;
        _fullText = Win.Text;
        FrameLength = Math.Min(_fullText.Length, End);
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "TYPETEXT");
        DrawState = -1;
        switch (frame)
        {
            case 0:
                if (IsForward || Win is null) break;
                Win.SetHidden(true);
                IsFinished = true;
                break;
            case End:
                if (!IsForward || Win is null) break;
                Win.SetHidden(false);
                IsFinished = true;
                break;
        }
        if (frame >= FrameLength) Win?.SetHidden(false);
        if (frame > 0 && frame < FrameLength && Win is not null)
        {
            Win.SetHidden(true);
            DrawState = frame;
            Host.PlaySound("GUITypeText");
            _partial = IsForward ? _partial + _fullText[frame - 1] : _partial.Length > 0 ? _partial[..^1] : _partial;
        }
        // The original only finishes on frame 30, which a text shorter than 30 characters never reaches (its group would never end).
        if (IsForward && frame == FrameLength && frame < End) IsFinished = true;
    }

    public override void Reverse()
    {
        base.Reverse();
        _partial = _fullText;
    }

    public override void Draw(List<UiDrawCommand> output)
    {
        if (Win is null || DrawState <= 0 || DrawState >= FrameLength) return;
        Win.AppendText(_partial, output);
    }

    public override void Skip() => Update(End);
}

/// <summary>
/// <c>CONTROLBARARROW</c>: the control bar's arrow image slides down onto the general button over 16 frames, then fades out over 6 (22 frames).
/// Needs the arrow image (<see cref="ITransitionHost.ControlBarArrowImage"/>) and <c>ControlBar.wnd:ButtonGeneral</c>; without either it
/// is finished at once, like the original.
/// </summary>
public sealed class ControlBarArrowEffect(ITransitionHost host) : TransitionEffect(host)
{
    protected override bool NeedsWindow => false;

    private const int BeginFade = 16;
    private const int End = 22;
    private const float Percent = 1.0f / BeginFade;
    private const float FadePercent = 1.0f / (End - BeginFade);
    private string? _arrow;
    private int _posX, _posY, _sizeX, _sizeY, _incrementY;

    public override void Init(ITransitionWindow? win)
    {
        FrameLength = End;
        ArmForward();
        _arrow = Host.ControlBarArrowImage;
        var general = Host.Find("ControlBar.wnd:ButtonGeneral");
        if (general is null || _arrow is null || !Host.TryGetImageSize(_arrow, out _sizeX, out _sizeY))
        {
            IsFinished = true;
            _arrow = null;
            return;
        }
        var rect = general.Rect;
        _incrementY = (int)(rect.Y * Percent);
        _posY = 0 - _sizeY + 20;
        _posX = rect.X + rect.Width / 2 - _sizeX / 2;
    }

    public override void Update(int frame)
    {
        CheckRange(frame, End, "CONTROLBARARROW");
        if (frame is 0 or End) IsFinished = true;
        DrawState = frame;
    }

    public override void Draw(List<UiDrawCommand> output)
    {
        if (_arrow is null || DrawState < 0) return;
        if (DrawState < BeginFade)
        {
            var y = _posY + _incrementY * DrawState;
            output.Add(new UiImageDraw(_arrow, new UiRect(_posX, y, _sizeX, _sizeY), WndColor.White, false, false));
            return;
        }
        var alpha = Math.Min(255, (int)((1 - FadePercent * (DrawState - BeginFade)) * 255));
        var yFade = _posY + _incrementY * (BeginFade - 1);
        output.Add(new UiImageDraw(_arrow, new UiRect(_posX, yFade, _sizeX, _sizeY), White(alpha), false, false));
    }

    public override void Skip() => Update(End);
}

/// <summary>Creates the effect of a style (<c>getTransitionForStyle</c>).</summary>
public static class TransitionEffects
{
    public static TransitionEffect Create(TransitionStyle style, ITransitionHost host) => style switch
    {
        TransitionStyle.Flash => new FlashEffect(host),
        TransitionStyle.ButtonFlash => new ButtonFlashEffect(host),
        TransitionStyle.WinFade => new FadeEffect(host),
        TransitionStyle.WinScaleUp => new ScaleUpEffect(host, "GUILogoMouseOver"),
        TransitionStyle.ScoreScaleUp => new ScaleUpEffect(host, "GUIScoreScreenPictures"),
        TransitionStyle.MainMenuScaleUp => new MainMenuScaleUpEffect(host),
        TransitionStyle.MainMenuMediumScaleUp => new FactionScaleEffect(host, medium: true),
        TransitionStyle.MainMenuSmallScaleDown => new FactionScaleEffect(host, medium: false),
        TransitionStyle.TypeText => new TypeTextEffect(host),
        TransitionStyle.ScreenFade => new ScreenFadeEffect(host),
        TransitionStyle.CountUp => new CountUpEffect(host),
        TransitionStyle.FullFade => new FullFadeEffect(host),
        TransitionStyle.TextOnFrame => new TextOnFrameEffect(host),
        TransitionStyle.ControlBarArrow => new ControlBarArrowEffect(host),
        TransitionStyle.ReverseSound => new ReverseSoundEffect(host),
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "No effect is implemented for this style."),
    };
}

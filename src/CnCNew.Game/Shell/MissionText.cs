using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CnCNew.Data;
using CnCNew.Game.Data;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell;

/// <summary>One line of the message list: the text and the color it is drawn in now (its alpha shrinks while it fades).</summary>
public readonly record struct UiMessageLine(string Text, WndColor Color);

/// <summary>
/// The message lines of <c>InGameUI</c> (<c>m_uiMessages</c>, <c>addMessageText</c>, the fade in <c>InGameUI::update</c>): what <c>DISPLAY_TEXT</c> prints at the top left. At most
/// <see cref="Capacity"/> lines, the newest first in the list and last on the screen; a new line takes <c>MessageColor1</c> unless the previous one has it, then <c>MessageColor2</c>
/// (compared with the color the previous line has at that moment, a faded line included, as in the original).
/// <para>
/// A line fades once it is older than <c>MessageDelayMS / 30 / 1000</c> logic frames - computed in <b>integer</b> arithmetic exactly as the original does, so it is 0 for the retail value
/// (75000 ms) and a line starts to lose alpha at once: each update takes <c>floor(age * 0.01)</c> from the alpha (nothing for 100 frames, then 1 per update, then 2 ...), the line is gone
/// when the alpha reaches 0 (about 9 seconds after it appeared). The original updates once per rendered frame; this class is updated once per original frame of the presentation clock
/// (<see cref="Update"/>, 1/30 s of game time whatever the tick rate), which is the same at 30 frames per second and slower to fade above it. Every frame number here is an original frame.
/// </para>
/// </summary>
public sealed class UiMessageLog(InGameUiSettings settings)
{
    /// <summary><c>InGameUI::MAX_UI_MESSAGES</c>.</summary>
    public const int Capacity = 6;

    private readonly string?[] _texts = new string?[Capacity];
    private readonly long[] _stamps = new long[Capacity];
    private readonly WndColor[] _colors = new WndColor[Capacity];

    public bool IsEmpty => _texts.All(text => text is null);

    /// <summary><c>InGameUI::addMessageText</c>: the new line goes to the front, the oldest falls out.</summary>
    public void Add(string text, long frame)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (var index = Capacity - 1; index >= 1; index--)
        {
            _texts[index] = _texts[index - 1];
            _stamps[index] = _stamps[index - 1];
            _colors[index] = _colors[index - 1];
        }
        _texts[0] = text;
        _stamps[0] = frame;
        _colors[0] = _texts[1] is null || _colors[1] == settings.MessageColor2 ? settings.MessageColor1 : settings.MessageColor2;
    }

    /// <summary>One update (<c>InGameUI::update</c>): lines past their delay lose alpha, a line without alpha is removed.</summary>
    public void Update(long frame)
    {
        var timeout = settings.MessageDelayMilliseconds / OriginalFrame.PerSecond / 1000;
        for (var index = Capacity - 1; index >= 0; index--)
        {
            if (_texts[index] is null) continue;
            var age = frame - _stamps[index];
            if (age <= timeout) continue;
            var amount = (int)(age * 0.01f);
            var color = _colors[index];
            var alpha = color.A - amount < 0 ? 0 : color.A - amount;
            _colors[index] = color with { A = (byte)alpha };
            if (alpha == 0) _texts[index] = null;
        }
    }

    /// <summary>The lines in drawing order: the oldest at the top, the newest at the bottom.</summary>
    public ImmutableArray<UiMessageLine> Lines
    {
        get
        {
            var lines = ImmutableArray.CreateBuilder<UiMessageLine>();
            for (var index = Capacity - 1; index >= 0; index--)
                if (_texts[index] is { } text) lines.Add(new UiMessageLine(text, _colors[index]));
            return lines.ToImmutable();
        }
    }
}

/// <summary>
/// The typed-out caption of <c>SHOW_MILITARY_CAPTION</c> (<c>InGameUI::militarySubtitle</c> and the <c>m_militarySubtitle</c> part of <c>InGameUI::update</c>): the text of the label is typed one
/// character at a time, the first line in the title font and the following ones in the caption font, a block cursor blinks behind the last character, and after its duration the
/// caption fades out. Everything counts in original frames (1/30 s) of the presentation clock, so the state at any frame is a function of the frames since it began.
/// <list type="bullet">
/// <item><description>The first character appears after <c>MilitaryCaptionDelayMS</c> (<c>Language.ini</c>, 750 ms = 22 frames; <c>(Int)(30 * 750 / 1000.0f)</c>), each next one
/// <c>MilitaryCaptionSpeed</c> (<c>Language.ini</c>, 1) frames after the previous. The test is <c>incrementOnFrame &lt; frame</c>, so a speed of 1 types one character every two frames.</description></item>
/// <item><description>A line break moves to the next line and waits the start delay again; at most <see cref="MaxLines"/> lines, the rest of the text is dropped
/// (<c>MAX_SUBTITLE_LINES</c>). There is no word wrap: the strings carry their own line breaks.</description></item>
/// <item><description>The cursor toggles every 10 frames (<c>blockBeginFrame + 9 &lt; frame</c>).</description></item>
/// <item><description>After <c>duration</c> milliseconds (<c>(Int)(30 * duration / 1000.0f)</c> frames) the alpha drops by <c>floor((frame - end) * 0.1)</c> per update until it would go below 0;
/// then the caption is removed.</description></item>
/// </list>
/// <c>MilitaryCaptionRandomizeTyping</c> is read but unused in the original (the code that used it is commented out); the typing click sound (<c>MilitarySubtitlesTyping</c>) is not played
/// here - <see cref="TypedCharacters"/> counts the characters so a host can.
/// </summary>
public sealed class MilitaryCaptionState
{
    public const int MaxLines = 4;
    private const int BlinkFrames = 10;

    private readonly string _text;
    private readonly long _lifetime;
    private readonly int _speed;
    private readonly int _delayFrames;
    private readonly List<StringBuilder> _lines = [new StringBuilder()];
    private long _blockBegin;
    private long _increment;
    private int _index;

    private MilitaryCaptionState(string text, long lifetime, long frame, int speed, int delayFrames, WndColor color)
    {
        _text = text;
        _lifetime = lifetime;
        _speed = speed;
        _delayFrames = delayFrames;
        Color = color;
        _blockBegin = frame;
        _increment = frame + delayFrames;
        BlockDrawn = true;
    }

    /// <summary>
    /// <c>InGameUI::militarySubtitle</c>. Returns <see langword="null"/> and a reason when the original would not create a caption: an empty text or a duration of 0 or less (it asserts
    /// and returns after having removed the previous caption).
    /// </summary>
    public static MilitaryCaptionState? Begin(string text, long durationMilliseconds, long frame, InGameUiSettings settings, int languageSpeed, int languageDelayMilliseconds, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);
        problem = null;
        if (text.Length == 0) { problem = "the caption text is empty"; return null; }
        if (durationMilliseconds <= 0) { problem = $"the caption duration {durationMilliseconds} ms is not positive"; return null; }
        var lifetime = frame + (long)(OriginalFrame.PerSecond * (float)durationMilliseconds / 1000f);
        var delay = (int)(OriginalFrame.PerSecond * (float)languageDelayMilliseconds / 1000f);
        return new MilitaryCaptionState(text.Replace("\r", string.Empty, StringComparison.Ordinal), lifetime, frame, languageSpeed, delay, settings.MilitaryCaptionColor);
    }

    /// <summary>The caption color now (its alpha shrinks while the caption fades out).</summary>
    public WndColor Color { get; private set; }

    /// <summary>Whether the block cursor is shown at this moment.</summary>
    public bool BlockDrawn { get; private set; }

    /// <summary>The fade has taken all alpha: the caption is gone.</summary>
    public bool Finished { get; private set; }

    /// <summary>How many characters were typed (line breaks excluded); a host plays the typing click once per new character.</summary>
    public int TypedCharacters { get; private set; }

    /// <summary>The last frame of the caption before it fades (<c>lifetime</c>).</summary>
    public long LastFrame => _lifetime;

    /// <summary>The full text of the label, typed or not.</summary>
    public string FullText => _text;

    /// <summary>The lines typed so far (the first is drawn in the title font, the others in the caption font).</summary>
    public ImmutableArray<string> Lines => [.._lines.Select(line => line.ToString())];

    /// <summary>The index of the next character to type.</summary>
    public int Index => _index;

    /// <summary>One update of <c>InGameUI::update</c> at <paramref name="frame"/>; call it once per original frame of the presentation clock.</summary>
    public void Update(long frame)
    {
        if (Finished) return;
        if (_lifetime < frame)
        {
            var amount = (int)((frame - _lifetime) * 0.1f);
            if (Color.A - amount < 0) Finished = true;
            else Color = Color with { A = (byte)(Color.A - amount) };
            return;
        }
        if (_blockBegin + BlinkFrames - 1 < frame)
        {
            _blockBegin = frame;
            BlockDrawn = !BlockDrawn;
        }
        if (_increment >= frame) return;
        var next = _text[_index];
        if (next == '\n')
        {
            if (_lines.Count < MaxLines)
            {
                _lines.Add(new StringBuilder());
                BlockDrawn = true;
                _increment = frame + _delayFrames;
            }
            else _index = _text.Length;
        }
        else
        {
            _lines[^1].Append(next);
            TypedCharacters++;
            _increment = frame + _speed;
        }
        _index++;
        if (_index >= _text.Length) _increment = _lifetime + 1;
    }
}

/// <summary>The cinematic text of <c>DISPLAY_CINEMATIC_TEXT</c> (<c>ScriptActions::doDisplayCinematicText</c>, <c>W3DDisplay::draw</c>): a centered, wrapped text near the bottom of the screen.</summary>
public sealed record CinematicTextState(string Text, FontDescription Font, long ExpiresAtFrame);

/// <summary>Builds the draw commands of the mission's own texts from the original's <c>InGameUI</c> and <c>W3DDisplay</c> rules (positions and colors from <c>InGameUI.ini</c>, fonts from <c>Language.ini</c>).</summary>
public static class MissionTextOverlay
{
    /// <summary><c>ScriptActions::doDisplayCinematicText</c> reads the font out of an editor string: <c>"Name - Size:12"</c>, with <c>" [Bold]"</c> appended for bold (<c>EditParameter::loadFontNames</c>).</summary>
    public static bool TryParseCinematicFont(string spec, out FontDescription font, out string problem)
    {
        font = FontDescription.Default;
        problem = string.Empty;
        const string marker = " - Size:";
        var at = spec.IndexOf(marker, StringComparison.Ordinal);
        if (at <= 0) { problem = $"font '{spec}' is not 'Name - Size:N [Bold]'"; return false; }
        var bold = spec.EndsWith("[Bold]", StringComparison.Ordinal);
        var sizeText = spec[(at + marker.Length)..].Replace("[Bold]", string.Empty, StringComparison.Ordinal).Trim();
        if (!int.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) || size <= 0) { problem = $"font '{spec}' has no point size"; return false; }
        font = new FontDescription(spec[..at].Trim(), size, bold);
        return true;
    }

    private static WndColor Shadow(WndColor color) => new(0, 0, 0, color.A);

    /// <summary><c>InGameUI::postDraw</c>, messages: from <c>MessagePosition</c> (unscaled pixels, as in the original) one line below the other, each as high as its font.</summary>
    public static ImmutableArray<UiDrawCommand> Messages(UiMessageLog log, InGameUiStyle style, UiScaleOptions options, IUiTextMeasure measure, int width, int height)
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var font = style.Font(InGameUiFontRole.Message, new UiViewport(width, height), options);
        var step = measure.LineHeight(font);
        var clip = new UiRect(0, 0, width, height);
        var y = style.Settings.MessagePosition.Y;
        foreach (var line in log.Lines)
        {
            commands.Add(new UiTextDraw(line.Text, null, font, line.Color, Shadow(line.Color), style.Settings.MessagePosition.X, y, UiTextAlignment.Left, clip));
            y += step;
        }
        return commands.ToImmutable();
    }

    /// <summary>
    /// <c>InGameUI::postDraw</c>, military caption: the typed lines at <c>MilitaryCaptionPosition</c> scaled from 800x600 to the screen (<c>militarySubtitle</c>), one below the other by their
    /// own height (an empty line has none), the block cursor behind the last character as high as the line's font and 0.8 times as wide.
    /// </summary>
    public static ImmutableArray<UiDrawCommand> MilitaryCaption(MilitaryCaptionState caption, InGameUiStyle style, UiScaleOptions options, IUiTextMeasure measure, int width, int height)
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var viewport = new UiViewport(width, height);
        var title = style.Font(InGameUiFontRole.MilitaryCaptionTitle, viewport, options);
        var body = style.Font(InGameUiFontRole.MilitaryCaption, viewport, options);
        var clip = new UiRect(0, 0, width, height);
        var x = (int)(style.Settings.MilitaryCaptionPosition.X * (width / 800f));
        var top = (int)(style.Settings.MilitaryCaptionPosition.Y * (height / 600f));
        var y = top;
        var lines = caption.Lines;
        var color = caption.Color;
        var blockY = y;
        var lastFont = title;
        for (var index = 0; index < lines.Length; index++)
        {
            var font = index == 0 ? title : body;
            lastFont = font;
            blockY = y;
            var text = lines[index];
            if (text.Length > 0) commands.Add(new UiTextDraw(text, null, font, color, Shadow(color), x, y, UiTextAlignment.Left, clip));
            y += text.Length == 0 ? 0 : measure.LineHeight(font);
        }
        if (caption.BlockDrawn)
        {
            var current = lines[^1];
            var blockX = x + (current.Length == 0 ? 0 : UiSentenceLayout.Layout(current, lastFont, measure, 0, false).Width);
            var blockHeight = measure.LineHeight(lastFont);
            commands.Add(new UiFillRect(new UiRect(blockX, blockY, (int)(blockHeight * 0.8f), blockHeight), color));
        }
        return commands.ToImmutable();
    }

    /// <summary>
    /// <c>W3DDisplay::draw</c>, cinematic text: wrapped 20 pixels short of the screen width and centered, its top at 0.9 of the screen height, in the font the script named (adjusted like every
    /// font), white without a shadow.
    /// </summary>
    public static ImmutableArray<UiDrawCommand> Cinematic(CinematicTextState text, InGameUiStyle style, UiScaleOptions options, IUiTextMeasure measure, int width, int height)
    {
        var font = style.Language.Resolve(text.Font, new UiViewport(width, height), options);
        return CenteredText(text.Text, font, measure, width, height, (int)(height * 0.9), new WndColor(255, 255, 255, 255), new WndColor(0, 0, 0, 0));
    }

    /// <summary>A block wrapped 20 pixels short of the screen width and centered on the screen (<c>setWordWrap(width - 20)</c>, <c>setWordWrapCentered</c>; <c>x = 20</c> when wider than the screen), its top at <paramref name="y"/>.</summary>
    public static ImmutableArray<UiDrawCommand> CenteredText(string text, UiFont font, IUiTextMeasure measure, int width, int height, int y, WndColor color, WndColor shadow)
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var sentence = UiSentenceLayout.Layout(text, font, measure, width - 20, true);
        var left = sentence.Width > width ? 20 : (width - sentence.Width) / 2;
        var step = measure.LineHeight(font);
        var clip = new UiRect(0, 0, width, height);
        foreach (var line in sentence.Lines)
        {
            if (line.Text.Length > 0) commands.Add(new UiTextDraw(line.Text, null, font, color, shadow, left + line.X, y, UiTextAlignment.Left, clip));
            y += step;
        }
        return commands.ToImmutable();
    }

    /// <summary>
    /// <c>InGameUI::postDraw</c>, named timers and counters: from <c>NamedTimerCountdownPosition</c> (shares of the screen) upward, one line per timer in name order, right-aligned when the
    /// position is in the right half. A countdown shows <c>text m:ss</c> and, at 0, the ready font and a flash between its color and <c>NamedTimerCountdownFlashColor</c> every flash period; a
    /// counter shows <c>text value</c>. A timer's value and <paramref name="frame"/> are original frames (the host converts the script VM's ticks).
    /// </summary>
    public static ImmutableArray<UiDrawCommand> NamedTimers(IEnumerable<(string Label, bool IsTimer, long Value)> timers, long frame, InGameUiStyle style, UiScaleOptions options, IUiTextMeasure measure, int width, int height)
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var settings = style.Settings;
        var viewport = new UiViewport(width, height);
        var normal = style.Font(InGameUiFontRole.NamedTimerNormal, viewport, options);
        var ready = style.Font(InGameUiFontRole.NamedTimerReady, viewport, options);
        var clip = new UiRect(0, 0, width, height);
        var reverse = settings.NamedTimerPosition.X >= 0.5m;
        var startX = (int)(settings.NamedTimerPosition.X * width);
        var y = (int)(settings.NamedTimerPosition.Y * height);
        var flashFrames = (int)settings.NamedTimerFlashFrames;
        var black = new WndColor(0, 0, 0, 255);
        foreach (var (label, isTimer, value) in timers)
        {
            var seconds = Math.Max(0L, value) / OriginalFrame.PerSecond;
            var isReady = isTimer && seconds == 0;
            var font = isReady ? ready : normal;
            var text = isTimer ? string.Create(CultureInfo.InvariantCulture, $"{label} {seconds / 60}:{seconds % 60:00}") : string.Create(CultureInfo.InvariantCulture, $"{label} {value}");
            var color = settings.NamedTimerNormalColor;
            // m_namedTimerUsedFlashColor starts true and flips every flash period, so the flash color shows in the odd periods.
            if (isReady && flashFrames > 0 && frame / flashFrames % 2 == 1) color = settings.NamedTimerFlashColor;
            var x = reverse ? startX - UiSentenceLayout.Layout(text, font, measure, 0, false).Width : startX;
            commands.Add(new UiTextDraw(text, null, font, color, black, x, y, UiTextAlignment.Left, clip));
            y -= measure.LineHeight(font);
        }
        return commands.ToImmutable();
    }
}

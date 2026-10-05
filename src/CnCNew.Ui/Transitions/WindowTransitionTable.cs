using System.Collections.Immutable;
using System.Globalization;

namespace CnCNew.Ui.Transitions;

/// <summary>
/// The effect styles of <c>WindowTransitions.ini</c> (<c>TransitionStyleNames</c> in <c>GameWindowTransitions.h</c>). The numeric
/// values are not part of any contract; the INI names are.
/// </summary>
public enum TransitionStyle
{
    Flash,
    ButtonFlash,
    WinFade,
    WinScaleUp,
    MainMenuScaleUp,
    TypeText,
    ScreenFade,
    CountUp,
    FullFade,
    TextOnFrame,
    MainMenuMediumScaleUp,
    MainMenuSmallScaleDown,
    ControlBarArrow,
    ScoreScaleUp,
    ReverseSound,
}

/// <summary>One <c>Window</c> block of a transition group: which window, which effect, and how many frames after the group start it begins.</summary>
/// <param name="WindowName">Decorated window name (<c>MainMenu.wnd:ButtonExit</c>); null for the two effects that act on the whole screen (<c>SCREENFADE</c>, <c>CONTROLBARARROW</c>).</param>
public sealed record TransitionWindowSpec(string? WindowName, TransitionStyle Style, int FrameDelay);

/// <summary>One <c>WindowTransition</c> block: a named list of window effects that start together (each after its own frame delay).</summary>
/// <param name="FireOnce">The group is dropped as soon as it has finished instead of being kept for a later reverse (<c>FireOnce = Yes</c>).</param>
public sealed record TransitionGroupSpec(string Name, ImmutableArray<TransitionWindowSpec> Windows, bool FireOnce);

/// <summary>A malformed or unknown entry in <c>WindowTransitions.ini</c> (the message names the file and the line).</summary>
public sealed class WindowTransitionFormatException(string message) : FormatException(message);

/// <summary>
/// The groups of <c>Data/INI/WindowTransitions.ini</c>, read strictly: an unknown key, an unknown style name, a missing
/// style, a duplicate group or an unterminated block is an error, never a default. The format is the one
/// <c>GameWindowTransitionsHandler::m_gameWindowTransitionsFieldParseTable</c> and <c>parseWindow</c> read
/// (<c>WindowTransition Name</c> ... <c>Window</c> blocks with <c>WinName</c>/<c>Style</c>/<c>FrameDelay</c> ... <c>FireOnce</c> ... <c>END</c>).
/// <para>
/// Difference to the original, deliberate: the original silently defaults a missing <c>Style</c> to the first style and a
/// missing <c>WinName</c> to the empty name; the shipped file has neither case (tested against the real data), so both are
/// errors here. <c>WinName</c> may only be absent for the styles that act on the whole screen.
/// </para>
/// </summary>
public sealed class WindowTransitionTable
{
    private static readonly ImmutableDictionary<string, TransitionStyle> Styles = new Dictionary<string, TransitionStyle>(StringComparer.OrdinalIgnoreCase)
    {
        ["FLASH"] = TransitionStyle.Flash,
        ["BUTTONFLASH"] = TransitionStyle.ButtonFlash,
        ["WINFADE"] = TransitionStyle.WinFade,
        ["WINSCALEUP"] = TransitionStyle.WinScaleUp,
        ["MAINMENUSCALEUP"] = TransitionStyle.MainMenuScaleUp,
        ["TYPETEXT"] = TransitionStyle.TypeText,
        ["SCREENFADE"] = TransitionStyle.ScreenFade,
        ["COUNTUP"] = TransitionStyle.CountUp,
        ["FULLFADE"] = TransitionStyle.FullFade,
        ["TEXTONFRAME"] = TransitionStyle.TextOnFrame,
        ["MAINMENUMEDIUMSCALEUP"] = TransitionStyle.MainMenuMediumScaleUp,
        ["MAINMENUSMALLSCALEDOWN"] = TransitionStyle.MainMenuSmallScaleDown,
        ["CONTROLBARARROW"] = TransitionStyle.ControlBarArrow,
        ["SCORESCALEUP"] = TransitionStyle.ScoreScaleUp,
        ["REVERSESOUND"] = TransitionStyle.ReverseSound,
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly ImmutableDictionary<string, TransitionGroupSpec> _byName;

    private WindowTransitionTable(ImmutableArray<TransitionGroupSpec> groups)
    {
        Groups = groups;
        _byName = groups.ToImmutableDictionary(group => group.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static WindowTransitionTable Empty { get; } = new([]);

    /// <summary>The groups in file order.</summary>
    public ImmutableArray<TransitionGroupSpec> Groups { get; }

    /// <summary>Group lookup by name, case-insensitive like <c>findGroup</c> (<c>compareNoCase</c>).</summary>
    public TransitionGroupSpec? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>Whether <paramref name="style"/> acts on the whole screen and therefore has no <c>WinName</c>.</summary>
    public static bool IsScreenStyle(TransitionStyle style) => style is TransitionStyle.ScreenFade or TransitionStyle.ControlBarArrow;

    public static bool TryParseStyle(string name, out TransitionStyle style) => Styles.TryGetValue(name, out style);

    public static WindowTransitionTable Parse(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);
        var groups = ImmutableArray.CreateBuilder<TransitionGroupSpec>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? groupName = null;
        var groupLine = 0;
        List<TransitionWindowSpec>? windows = null;
        var fireOnce = false;
        var inWindow = false;
        var windowLine = 0;
        string? winName = null;
        TransitionStyle? style = null;
        var delay = 0;

        WindowTransitionFormatException Error(int line, string message) => new($"{source}:{line}: {message}");

        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var raw = lines[index];
            var comment = raw.IndexOf(';');
            var line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0) continue;

            if (groupName is null)
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || !parts[0].Equals("WindowTransition", StringComparison.OrdinalIgnoreCase))
                    throw Error(lineNumber, $"expected 'WindowTransition <name>', found '{line}'.");
                if (!names.Add(parts[1])) throw Error(lineNumber, $"duplicate group '{parts[1]}'.");
                groupName = parts[1];
                groupLine = lineNumber;
                windows = [];
                fireOnce = false;
                continue;
            }

            if (line.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                if (inWindow)
                {
                    if (style is null) throw Error(windowLine, $"group '{groupName}': Window block without Style.");
                    if (winName is null && !IsScreenStyle(style.Value)) throw Error(windowLine, $"group '{groupName}': {style} needs a WinName.");
                    windows!.Add(new TransitionWindowSpec(winName, style.Value, delay));
                    inWindow = false;
                }
                else
                {
                    groups.Add(new TransitionGroupSpec(groupName, [.. windows!], fireOnce));
                    groupName = null;
                    windows = null;
                }
                continue;
            }

            if (!inWindow && line.Equals("Window", StringComparison.OrdinalIgnoreCase))
            {
                inWindow = true;
                windowLine = lineNumber;
                winName = null;
                style = null;
                delay = 0;
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals < 0) throw Error(lineNumber, $"expected 'Key = Value', found '{line}'.");
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (inWindow)
            {
                if (key.Equals("WinName", StringComparison.OrdinalIgnoreCase)) winName = value.Length == 0 ? throw Error(lineNumber, "empty WinName.") : value;
                else if (key.Equals("Style", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Styles.TryGetValue(value, out var parsed)) throw Error(lineNumber, $"unknown style '{value}'.");
                    style = parsed;
                }
                else if (key.Equals("FrameDelay", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out delay) || delay < 0)
                        throw Error(lineNumber, $"FrameDelay '{value}' is not a non-negative integer.");
                }
                else throw Error(lineNumber, $"unknown Window key '{key}'.");
            }
            else if (key.Equals("FireOnce", StringComparison.OrdinalIgnoreCase))
            {
                fireOnce = value.ToLowerInvariant() switch
                {
                    "yes" or "true" or "1" => true,
                    "no" or "false" or "0" => false,
                    _ => throw Error(lineNumber, $"FireOnce '{value}' is not a boolean."),
                };
            }
            else throw Error(lineNumber, $"unknown group key '{key}'.");
        }

        if (groupName is not null) throw Error(groupLine, $"group '{groupName}' is not terminated by END.");
        return new WindowTransitionTable(groups.ToImmutable());
    }
}

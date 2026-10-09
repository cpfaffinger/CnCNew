using System.Collections.Immutable;
using System.Globalization;

namespace CnCNew.Game.Hud;

/// <summary>A rectangle in the creation space of its scheme (<see cref="ControlBarSchemeInfo.CreationWidth"/> x <see cref="ControlBarSchemeInfo.CreationHeight"/>).</summary>
public readonly record struct SchemeRect(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Right <= Left || Bottom <= Top;
}

/// <summary>One <c>ImagePart</c> of a scheme (<c>Layer</c> 0 = drawn last/on top, per the file's own comment).</summary>
public sealed record ControlBarImagePart(int X, int Y, int Width, int Height, string ImageName, int Layer);

/// <summary>The four state images a scheme gives one of the control bar's own buttons (empty: the scheme names none).</summary>
public sealed record SchemeButtonImages(string Enabled, string Hilited, string Pushed, string Disabled)
{
    public static readonly SchemeButtonImages None = new(string.Empty, string.Empty, string.Empty, string.Empty);
}

/// <summary>
/// The part of a <c>ControlBarScheme &lt;Name&gt;</c> block (<c>Data/INI/ControlBarScheme.ini</c>) the HUD positions itself with: the
/// resolution the scheme was written for (<c>ScreenCreationRes</c>; every position of the block is in that space and the original scales it
/// to the display, <c>ControlBarScheme::init</c> and <c>ControlBarSchemeManager::setControlBarSchemeByPlayer</c>), the per-faction art, the
/// rectangles and state images of the windows <c>ControlBarScheme::init</c> places (money, power bar, options, idle worker, beacon,
/// communicator, generals button, the control bar toggle, the under-attack glow) and the three images it hands to other windows.
/// This is a UI placement file, not a compiled gameplay definition, so it is read with a small standalone parser here rather than
/// through <c>CnCNew.Data</c>. Keys the HUD does not use are counted in <see cref="ControlBarSchemeTable.UnreadKeys"/>, never dropped silently.
/// </summary>
public sealed record ControlBarSchemeInfo(
    string Name,
    string Side,
    SchemeRect Money,
    SchemeRect PowerBar,
    ImmutableArray<ControlBarImagePart> ImageParts,
    string RightHudImage = "")
{
    /// <summary><c>CONTROL_BAR_SCHEME_FOREGROUND_IMAGE_LAYERS</c> (ControlBarScheme.h): layers below this are drawn over the windows, the others under them.</summary>
    public const int ForegroundLayers = 3;

    public int CreationWidth { get; init; }
    public int CreationHeight { get; init; }

    public SchemeRect Options { get; init; }
    public SchemeRect Worker { get; init; }
    public SchemeRect Chat { get; init; }
    public SchemeRect Beacon { get; init; }
    public SchemeRect General { get; init; }
    public SchemeRect MinMax { get; init; }
    public SchemeRect UnderAttack { get; init; }

    public SchemeButtonImages OptionsImages { get; init; } = SchemeButtonImages.None;
    public SchemeButtonImages WorkerImages { get; init; } = SchemeButtonImages.None;
    public SchemeButtonImages ChatImages { get; init; } = SchemeButtonImages.None;
    public SchemeButtonImages BeaconImages { get; init; } = SchemeButtonImages.None;
    public SchemeButtonImages GeneralImages { get; init; } = SchemeButtonImages.None;
    /// <summary><c>ToggleButtonDownOn/In/Pushed</c>: the toggle of a control bar in its default (raised) stage (<c>ControlBar::setUpDownImages</c>).</summary>
    public SchemeButtonImages ToggleDownImages { get; init; } = SchemeButtonImages.None;
    /// <summary><c>UAttackButtonEnable</c> (the window at rest) and <c>UAttackButtonHightlited</c> (shown as its disabled image while the glow blinks).</summary>
    public SchemeButtonImages UnderAttackImages { get; init; } = SchemeButtonImages.None;

    public string ExpBarForegroundImage { get; init; } = string.Empty;
    public string QueueButtonImage { get; init; } = string.Empty;

    /// <summary>The parts <c>ControlBarScheme::drawBackground</c> draws, deepest layer first (layer 5 down to 3; file order within a layer).</summary>
    public IEnumerable<ControlBarImagePart> BackgroundParts => Ordered(part => part.Layer >= ForegroundLayers);

    /// <summary>The parts <c>ControlBarScheme::drawForeground</c> draws over the windows (layer 2 down to 0).</summary>
    public IEnumerable<ControlBarImagePart> ForegroundParts => Ordered(part => part.Layer < ForegroundLayers);

    private IEnumerable<ControlBarImagePart> Ordered(Func<ControlBarImagePart, bool> filter)
        => ImageParts.Select((value, index) => (value, index)).Where(item => filter(item.value)).OrderByDescending(item => item.value.Layer).ThenBy(item => item.index).Select(item => item.value);
}

public sealed class ControlBarSchemeTable
{
    private readonly ImmutableArray<ControlBarSchemeInfo> _schemes;

    private ControlBarSchemeTable(ImmutableArray<ControlBarSchemeInfo> schemes, ImmutableSortedDictionary<string, int> unreadKeys)
    {
        _schemes = schemes;
        UnreadKeys = unreadKeys;
    }

    public ImmutableArray<ControlBarSchemeInfo> Schemes => _schemes;

    /// <summary>
    /// Every key of the file the HUD does not use (border and clock colours, the generals-bar and toggle-up art, the arrow, the command marker,
    /// the powers window art, <c>AnimatingPart</c> blocks, and anything this parser does not know), with the number of schemes' lines that carry it.
    /// </summary>
    public ImmutableSortedDictionary<string, int> UnreadKeys { get; }

    /// <summary>The first scheme whose <c>Side</c> matches (ordinal, case-insensitive); the retail file lists the plain 8x6 layout
    /// before any general-power variant for the same side, so "first match" is the standard control bar.</summary>
    public bool TryGetForSide(string side, out ControlBarSchemeInfo scheme)
    {
        foreach (var candidate in _schemes)
            if (candidate.Side.Equals(side, StringComparison.OrdinalIgnoreCase))
            {
                scheme = candidate;
                return true;
            }
        scheme = null!;
        return false;
    }

    /// <summary>
    /// Parses every <c>ControlBarScheme ... End</c> block. The grammar has no <c>=</c> and no <c>;</c> terminators (unlike the
    /// compiled INI dialect): one statement per line, <c>Key</c> or <c>Key value</c> or <c>Key X:n Y:n</c>, comments from the first
    /// <c>;</c> to end of line, blocks closed by a bare <c>End</c>. <c>AnimatingPart</c> blocks are skipped by depth and counted.
    /// A scheme without <c>ScreenCreationRes</c> is refused: nothing in it could be placed (the original divides by it).
    /// </summary>
    public static ControlBarSchemeTable Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var schemes = ImmutableArray.CreateBuilder<ControlBarSchemeInfo>();
        var unread = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripComment(lines[index]);
            if (line.Length == 0) continue;
            var (key, rest) = SplitFirst(line);
            if (!key.Equals("ControlBarScheme", StringComparison.OrdinalIgnoreCase)) continue;
            var name = rest.Trim();
            index++;
            schemes.Add(ParseScheme(name, lines, ref index, unread));
        }
        return new ControlBarSchemeTable(schemes.ToImmutable(), unread.ToImmutableSortedDictionary(StringComparer.Ordinal));
    }

    private static ControlBarSchemeInfo ParseScheme(string name, string[] lines, ref int index, SortedDictionary<string, int> unread)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = ImmutableArray.CreateBuilder<ControlBarImagePart>();
        for (; index < lines.Length; index++)
        {
            var line = StripComment(lines[index]);
            if (line.Length == 0) continue;
            if (line.Equals("End", StringComparison.OrdinalIgnoreCase)) return Build(name, values, parts.ToImmutable(), unread);
            var (key, rest) = SplitFirst(line);
            if (key.Equals("ImagePart", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                parts.Add(ParseImagePart(lines, ref index));
            }
            else if (key.Equals("AnimatingPart", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                SkipBlock(lines, ref index);
                unread["AnimatingPart"] = unread.GetValueOrDefault("AnimatingPart") + 1;
            }
            else values[key] = rest.Trim();
        }
        throw new FormatException($"ControlBarScheme '{name}' is missing End.");
    }

    private static ControlBarSchemeInfo Build(string name, Dictionary<string, string> values, ImmutableArray<ControlBarImagePart> parts, SortedDictionary<string, int> unread)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Text(string key)
        {
            used.Add(key);
            return values.GetValueOrDefault(key, string.Empty);
        }
        SchemeRect Rect(string upperLeft, string lowerRight)
        {
            used.Add(upperLeft);
            used.Add(lowerRight);
            if (!values.TryGetValue(upperLeft, out var first) || !values.TryGetValue(lowerRight, out var second)) return default;
            var (left, top) = ParsePair(first);
            var (right, bottom) = ParsePair(second);
            return new SchemeRect(left, top, right, bottom);
        }
        SchemeButtonImages Images(string enabled, string hilited, string pushed, string disabled) => new(Text(enabled), Text(hilited), Text(pushed), disabled.Length == 0 ? string.Empty : Text(disabled));

        used.Add("ScreenCreationRes");
        if (!values.TryGetValue("ScreenCreationRes", out var resolution)) throw new FormatException($"ControlBarScheme '{name}' has no ScreenCreationRes.");
        var (width, height) = ParsePair(resolution);
        if (width <= 0 || height <= 0) throw new FormatException($"ControlBarScheme '{name}' has the ScreenCreationRes {width}x{height}.");

        var scheme = new ControlBarSchemeInfo(name, Text("Side"), Rect("MoneyUL", "MoneyLR"), Rect("PowerBarUL", "PowerBarLR"), parts, Text("RightHUDImage"))
        {
            CreationWidth = width,
            CreationHeight = height,
            Options = Rect("OptionsUL", "OptionsLR"),
            Worker = Rect("WorkerUL", "WorkerLR"),
            Chat = Rect("ChatUL", "ChatLR"),
            Beacon = Rect("BeaconUL", "BeaconLR"),
            General = Rect("GeneralUL", "GeneralLR"),
            MinMax = Rect("MinMaxUL", "MinMaxLR"),
            UnderAttack = Rect("UAttackUL", "UAttackLR"),
            // The key names are the file's own spelling ("Hightlited").
            OptionsImages = Images("OptionsButtonEnable", "OptionsButtonHightlited", "OptionsButtonPushed", "OptionsButtonDisabled"),
            WorkerImages = Images("IdleWorkerButtonEnable", "IdleWorkerButtonHightlited", "IdleWorkerButtonPushed", "IdleWorkerButtonDisabled"),
            ChatImages = Images("BuddyButtonEnable", "BuddyButtonHightlited", "BuddyButtonPushed", "BuddyButtonDisabled"),
            BeaconImages = Images("BeaconButtonEnable", "BeaconButtonHightlited", "BeaconButtonPushed", "BeaconButtonDisabled"),
            GeneralImages = Images("GeneralButtonEnable", "GeneralButtonHightlited", "GeneralButtonPushed", "GeneralButtonDisabled"),
            ToggleDownImages = Images("ToggleButtonDownOn", "ToggleButtonDownIn", "ToggleButtonDownPushed", string.Empty),
            UnderAttackImages = Images("UAttackButtonEnable", "UAttackButtonHightlited", "UAttackButtonPushed", string.Empty),
            ExpBarForegroundImage = Text("ExpBarForegroundImage"),
            QueueButtonImage = Text("QueueButtonImage"),
        };
        foreach (var key in values.Keys.Where(key => !used.Contains(key)))
        {
            // Counted under the spelling of its first occurrence, so the table does not depend on the order of the schemes' dictionaries.
            var canonical = unread.Keys.FirstOrDefault(existing => existing.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;
            unread[canonical] = unread.GetValueOrDefault(canonical) + 1;
        }
        return scheme;
    }

    private static ControlBarImagePart ParseImagePart(string[] lines, ref int index)
    {
        int x = 0, y = 0, width = 0, height = 0, layer = 0;
        var imageName = string.Empty;
        for (; index < lines.Length; index++)
        {
            var line = StripComment(lines[index]);
            if (line.Length == 0) continue;
            if (line.Equals("End", StringComparison.OrdinalIgnoreCase)) return new ControlBarImagePart(x, y, width, height, imageName, layer);
            var (key, rest) = SplitFirst(line);
            switch (key.ToUpperInvariant())
            {
                case "POSITION": (x, y) = ParsePair(rest); break;
                case "SIZE": (width, height) = ParsePair(rest); break;
                case "IMAGENAME": imageName = rest.Trim(); break;
                case "LAYER":
                    if (!int.TryParse(rest.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out layer)) throw new FormatException($"ImagePart layer '{rest.Trim()}' is not a number.");
                    break;
                default: throw new FormatException($"ImagePart has the unknown key '{key}'.");
            }
        }
        throw new FormatException("ImagePart is missing End.");
    }

    /// <summary>Skips a block by counting nested <c>... End</c> pairs (an <c>AnimatingPart</c> contains exactly one <c>ImagePart</c>).</summary>
    private static void SkipBlock(string[] lines, ref int index)
    {
        var depth = 1;
        for (; index < lines.Length; index++)
        {
            var line = StripComment(lines[index]);
            if (line.Length == 0) continue;
            var (key, _) = SplitFirst(line);
            if (key.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                if (--depth == 0) return;
            }
            else if (key.Equals("ImagePart", StringComparison.OrdinalIgnoreCase) || key.Equals("AnimatingPart", StringComparison.OrdinalIgnoreCase)) depth++;
        }
    }

    /// <summary>Parses <c>X:n Y:n</c> (order-independent, whitespace between the two labeled numbers).</summary>
    private static (int X, int Y) ParsePair(string text)
    {
        int? x = null, y = null;
        foreach (var token in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("X:", StringComparison.OrdinalIgnoreCase) && int.TryParse(token[2..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var xv)) x = xv;
            else if (token.StartsWith("Y:", StringComparison.OrdinalIgnoreCase) && int.TryParse(token[2..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var yv)) y = yv;
        }
        if (x is null || y is null) throw new FormatException($"'{text}' is not an 'X:n Y:n' pair.");
        return (x.Value, y.Value);
    }

    private static string StripComment(string line)
    {
        var comment = line.IndexOf(';');
        return (comment >= 0 ? line[..comment] : line).Trim();
    }

    private static (string Key, string Remainder) SplitFirst(string line)
    {
        var space = line.IndexOfAny([' ', '\t']);
        if (space < 0) return (line, string.Empty);
        return (line[..space], line[(space + 1)..]);
    }
}

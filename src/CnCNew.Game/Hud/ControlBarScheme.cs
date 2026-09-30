using System.Collections.Immutable;
using System.Globalization;

namespace CnCNew.Game.Hud;

/// <summary>A rectangle in the 1920x1080 creation space shared by <c>ControlBarScheme.ini</c> and <c>ControlBar.wnd</c>.</summary>
public readonly record struct SchemeRect(int Left, int Top, int Right, int Bottom);

/// <summary>One <c>ImagePart</c> of a scheme (<c>Layer</c> 0 = drawn last/on top, per the file's own comment).</summary>
public sealed record ControlBarImagePart(int X, int Y, int Width, int Height, string ImageName, int Layer);

/// <summary>
/// The part of a <c>ControlBarScheme &lt;Name&gt;</c> block (<c>Data/INI/ControlBarScheme.ini</c>) the HUD positions itself with: the
/// per-faction background art and the money/power bar rectangles. This is a UI placement file, not a compiled gameplay definition,
/// so it is read with a small standalone parser here rather than through <c>CnCNew.Data</c>. Toggle/chat/beacon button art,
/// <c>AnimatingPart</c> blocks and border tint colors beyond the plain background are not read: this package does not draw the
/// beacon, buddy chat or general-power popup art (out of scope for the human control surface, see docs/GAME-HUD.md).
/// </summary>
public sealed record ControlBarSchemeInfo(
    string Name,
    string Side,
    SchemeRect Money,
    SchemeRect PowerBar,
    ImmutableArray<ControlBarImagePart> ImageParts);

public sealed class ControlBarSchemeTable
{
    private readonly ImmutableArray<ControlBarSchemeInfo> _schemes;

    private ControlBarSchemeTable(ImmutableArray<ControlBarSchemeInfo> schemes) => _schemes = schemes;

    public ImmutableArray<ControlBarSchemeInfo> Schemes => _schemes;

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
    /// <c>;</c> to end of line, blocks closed by a bare <c>End</c>. <c>AnimatingPart</c> blocks (and any other unknown nested block)
    /// are skipped by depth, never guessed at.
    /// </summary>
    public static ControlBarSchemeTable Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var schemes = ImmutableArray.CreateBuilder<ControlBarSchemeInfo>();
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripComment(lines[index]);
            if (line.Length == 0) continue;
            var (key, rest) = SplitFirst(line);
            if (!key.Equals("ControlBarScheme", StringComparison.OrdinalIgnoreCase)) continue;
            var name = rest.Trim();
            index++;
            schemes.Add(ParseScheme(name, lines, ref index));
        }
        return new ControlBarSchemeTable(schemes.ToImmutable());
    }

    private static ControlBarSchemeInfo ParseScheme(string name, string[] lines, ref int index)
    {
        var side = string.Empty;
        SchemeRect money = default, power = default;
        var parts = ImmutableArray.CreateBuilder<ControlBarImagePart>();
        for (; index < lines.Length; index++)
        {
            var line = StripComment(lines[index]);
            if (line.Length == 0) continue;
            if (line.Equals("End", StringComparison.OrdinalIgnoreCase)) return new ControlBarSchemeInfo(name, side, money, power, parts.ToImmutable());
            var (key, rest) = SplitFirst(line);
            switch (key.ToUpperInvariant())
            {
                case "SIDE":
                    side = rest.Trim();
                    break;
                case "MONEYUL":
                    money = money with { Left = ParsePair(rest).X, Top = ParsePair(rest).Y };
                    break;
                case "MONEYLR":
                    money = money with { Right = ParsePair(rest).X, Bottom = ParsePair(rest).Y };
                    break;
                case "POWERBARUL":
                    power = power with { Left = ParsePair(rest).X, Top = ParsePair(rest).Y };
                    break;
                case "POWERBARLR":
                    power = power with { Right = ParsePair(rest).X, Bottom = ParsePair(rest).Y };
                    break;
                case "IMAGEPART":
                    index++;
                    parts.Add(ParseImagePart(lines, ref index));
                    break;
                case "ANIMATINGPART":
                    index++;
                    SkipBlock(lines, ref index);
                    break;
            }
        }
        throw new FormatException($"ControlBarScheme '{name}' is missing End.");
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
                case "LAYER": int.TryParse(rest.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out layer); break;
            }
        }
        throw new FormatException("ImagePart is missing End.");
    }

    /// <summary>Skips a block by counting nested <c>... End</c> pairs (an <c>AnimatingPart</c> contains exactly one <c>ImagePart</c>).</summary>
    private static void SkipBlock(string[] lines, ref int index)
    {
        var depth = 1;
        for (; index < lines.Length && depth > 0; index++)
        {
            var line = StripComment(lines[index]);
            if (line.Length == 0) continue;
            var (key, _) = SplitFirst(line);
            if (key.Equals("End", StringComparison.OrdinalIgnoreCase)) depth--;
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

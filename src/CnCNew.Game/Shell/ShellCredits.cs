using System.Collections.Immutable;
using CnCNew.Content;

namespace CnCNew.Game.Shell;

public enum CreditsStyle { Title, MinorTitle, Normal, Column }

/// <summary>One credits line: either a quoted literal or a CSF label (contains ':'), resolved when drawn. <c>&lt;BLANK&gt;</c> and <c>Blank</c> both become an empty line.</summary>
public sealed record CreditsLine(CreditsStyle Style, string? Text, bool IsLabel);

/// <summary>
/// A minimal reader of the <c>Credits</c> block in <c>Data/INI/Credits.ini</c> (format documented at the top of that
/// file): <c>Style = TITLE|MINORTITLE|NORMAL|COLUMN</c> switches the style of the <c>Text =</c> lines that follow, and a
/// bare <c>Blank</c> line inserts an empty row. This is not a general INI parser (that lives in <c>CnCNew.Data</c> for
/// typed game definitions); credits are a display-only script the shell reads directly. Column pairing (two
/// consecutive COLUMN texts side by side) is not reproduced: every line is rendered on its own row, which is an honest
/// simplification documented in <c>docs/GAME-SHELL.md</c>.
/// </summary>
public static class ShellCredits
{
    public const string VirtualPath = "Data/INI/Credits.ini";

    public static ImmutableArray<CreditsLine> Load(ContentFileSystem content)
    {
        if (!content.Exists(VirtualPath)) return [];
        string text;
        using (var stream = content.OpenRead(VirtualPath))
        using (var reader = new StreamReader(stream)) text = reader.ReadToEnd();

        var lines = ImmutableArray.CreateBuilder<CreditsLine>();
        var style = CreditsStyle.Normal;
        var inBlock = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var semicolon = rawLine.IndexOf(';');
            var line = (semicolon >= 0 ? rawLine[..semicolon] : rawLine).Trim();
            if (line.Length == 0) continue;
            if (!inBlock)
            {
                if (line.Equals("Credits", StringComparison.Ordinal)) inBlock = true;
                continue;
            }
            if (line.Equals("END", StringComparison.Ordinal)) break;
            if (line.Equals("Blank", StringComparison.OrdinalIgnoreCase)) { lines.Add(new CreditsLine(style, null, false)); continue; }
            var equals = line.IndexOf('=');
            if (equals < 0) continue;
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Equals("Style", StringComparison.OrdinalIgnoreCase))
            {
                style = value.ToUpperInvariant() switch
                {
                    "TITLE" => CreditsStyle.Title,
                    "MINORTITLE" => CreditsStyle.MinorTitle,
                    "COLUMN" => CreditsStyle.Column,
                    _ => CreditsStyle.Normal,
                };
            }
            else if (key.Equals("Text", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Equals("<BLANK>", StringComparison.OrdinalIgnoreCase)) lines.Add(new CreditsLine(style, null, false));
                else if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') lines.Add(new CreditsLine(style, value[1..^1], false));
                else lines.Add(new CreditsLine(style, value, true));
            }
            // ScrollRate/ScrollRateEveryFrames/ScrollDown/*Color header fields drive the retail scroll animation and
            // color per style; the shell scrolls at a fixed rate instead (see ShellCustomDraw), so they are read but unused.
        }
        return lines.ToImmutable();
    }
}

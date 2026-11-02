using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Content;

namespace CnCNew.Game.Hud;

/// <summary><c>Animation2D.ini</c>: the frames of the original's small animated icons, and which of them shows at a moment (<c>Anim2D::tryNextFrame</c> without its random start).</summary>
public sealed class Anim2DTable
{
    private sealed record Template(ImmutableArray<string> Images, long DelayMs, bool PingPong);

    private readonly Dictionary<string, Template> _templates = new(StringComparer.OrdinalIgnoreCase);

    public static Anim2DTable Load(ContentFileSystem content)
    {
        var table = new Anim2DTable();
        const string path = "Data/INI/Animation2D.ini";
        if (!content.Exists(path)) return table;
        using var reader = new StreamReader(content.OpenRead(path));
        string? name = null;
        var images = new List<string>();
        var (delay, pingPong) = (0L, false);
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Split(';', 2)[0].Trim();
            if (line.StartsWith("Animation ", StringComparison.OrdinalIgnoreCase) && !line.Contains('='))
            {
                name = line["Animation ".Length..].Trim();
                images.Clear();
                (delay, pingPong) = (0L, false);
                continue;
            }
            if (name is null) continue;
            if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                table._templates[name] = new Template([..images], Math.Max(1, delay), pingPong);
                name = null;
                continue;
            }
            var equals = line.IndexOf('=');
            if (equals < 0) continue;
            var (key, value) = (line[..equals].Trim(), line[(equals + 1)..].Trim());
            if (key.Equals("Image", StringComparison.OrdinalIgnoreCase)) images.Add(value);
            else if (key.Equals("AnimationDelay", StringComparison.OrdinalIgnoreCase)) delay = long.Parse(value, CultureInfo.InvariantCulture);
            else if (key.Equals("AnimationMode", StringComparison.OrdinalIgnoreCase)) pingPong = value.Equals("PING_PONG", StringComparison.OrdinalIgnoreCase);
        }
        return table;
    }

    /// <summary>
    /// The clock of a timed bomb: <c>ICON_BOMB_TIMED</c> has a picture per second, and <c>setMinFrame(numFrames - seconds - 1)</c> starts it at the seconds that are left; a longer
    /// fuse waits at the first picture. Null when the data has no such animation.
    /// </summary>
    public string? CountdownFrame(string name, long secondsLeft)
    {
        if (!_templates.TryGetValue(name, out var template) || template.Images.IsEmpty) return null;
        var pictures = template.Images.Length;
        return template.Images[pictures - 1 - (int)Math.Clamp(secondsLeft, 0, pictures - 1)];
    }

    /// <summary>The image of an animation at a time in milliseconds, its start moved by <paramref name="phase"/>; null when the data has no such animation.</summary>
    public string? FrameAt(string name, long clockMs, uint phase)
    {
        if (!_templates.TryGetValue(name, out var template) || template.Images.IsEmpty) return null;
        return template.Images[HudOverlayRenderer.Frame(template.Images.Length, clockMs / template.DelayMs + phase % (uint)template.Images.Length, template.PingPong)];
    }
}

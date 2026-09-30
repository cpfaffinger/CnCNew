using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;

namespace CnCNew.Ui.Images;

/// <summary><c>ImageStatus</c> bits of Image.h (<c>imageStatusNames</c>).</summary>
[Flags]
public enum MappedImageStatus
{
    None = 0,
    Rotated90Clockwise = 1,
    RawTexture = 2,
}

/// <summary>Normalized texture coordinates (0..1) of a mapped image, as <c>Image::parseImageCoords</c> computes them.</summary>
public readonly record struct UvRect(float Left, float Top, float Right, float Bottom);

/// <summary>
/// One <c>MappedImage</c> block: a sub-rectangle of a UI texture page. Pixel coordinates are exclusive at the right and
/// bottom edge (<c>Image::parseImageCoords</c> computes the size as <c>right - left</c>).
/// </summary>
public sealed record MappedImage(
    string Name,
    string TextureFile,
    int TextureWidth,
    int TextureHeight,
    int Left,
    int Top,
    int Right,
    int Bottom,
    MappedImageStatus Status,
    IniSourceLocation Location)
{
    public bool IsRotated => Status.HasFlag(MappedImageStatus.Rotated90Clockwise);

    /// <summary>
    /// Size of the image on screen. For images the packer stored rotated by 90 degrees clockwise the rectangle on the
    /// page is transposed, so <c>Image::parseImageStatus</c> swaps width and height.
    /// </summary>
    public int Width => IsRotated ? Bottom - Top : Right - Left;
    public int Height => IsRotated ? Right - Left : Bottom - Top;

    public UvRect Uv => new(
        Left / (float)TextureWidth,
        Top / (float)TextureHeight,
        Right / (float)TextureWidth,
        Bottom / (float)TextureHeight);
}

public sealed class MappedImageFormatException(string message) : FormatException(message);

/// <summary>
/// The mapped image collection (<c>ImageCollection</c>, Image.cpp) built from the compiled definition graph. The
/// retail load plan loads <c>Data/INI/MappedImages/TextureSize_512</c> and <c>HandCreated</c>; each <c>MappedImage</c>
/// block becomes a compatibility definition <c>mappedimage/&lt;name&gt;</c>, and a later block with the same name
/// updates the earlier image (<c>INI::parseMappedImageDefinition</c> reuses it). Names are looked up
/// case-insensitively (<c>nameToLowercaseKey</c>).
/// <para>
/// Every key is typed strictly after <c>Image::m_imageFieldParseTable</c>. Unlike the original, which keeps defaults
/// (texture size 0, full UV rectangle) for missing keys and then skips the division by a zero texture size, a missing
/// key, a non-positive texture size, an inverted or out-of-page rectangle and an unknown status name are errors.
/// </para>
/// </summary>
public sealed class MappedImageTable
{
    public const string DefinitionPrefix = "mappedimage/";
    private static readonly string[] RequiredKeys = ["Texture", "TextureWidth", "TextureHeight", "Coords", "Status"];

    private readonly Dictionary<string, MappedImage> _byName;

    private MappedImageTable(ImmutableArray<MappedImage> images)
    {
        Images = images;
        _byName = images.ToDictionary(image => image.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>All images in ordinal-ignore-case name order.</summary>
    public ImmutableArray<MappedImage> Images { get; }
    public int Count => Images.Length;

    public bool TryGet(string name, out MappedImage image)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out image!);
    }

    public MappedImage Get(string name)
        => TryGet(name, out var image) ? image : throw new KeyNotFoundException($"Mapped image '{name}' does not exist.");

    /// <summary>Images per texture file (case-insensitive file names), in canonical file name order.</summary>
    public ImmutableArray<(string TextureFile, int Images)> ImagesPerTexture()
        => [..Images.GroupBy(image => image.TextureFile, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Key, group.Count()))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)];

    public static MappedImageTable FromGraph(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return FromDefinitions(graph.Definitions);
    }

    public static MappedImageTable FromDefinitions(IEnumerable<GameDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var images = definitions
            .OfType<CompatibilityDefinition>()
            .Where(definition => definition.Id.Name.StartsWith(DefinitionPrefix, StringComparison.Ordinal))
            .Select(Parse)
            .OrderBy(image => image.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(image => image.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        return new MappedImageTable(images);
    }

    private static MappedImage Parse(CompatibilityDefinition definition)
    {
        var name = definition.OriginalName;
        var values = new Dictionary<string, DefinitionField>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in definition.Fields)
        {
            if (!RequiredKeys.Contains(field.Name, StringComparer.OrdinalIgnoreCase))
                throw Error(field.Location, name, $"'{field.Name}' is not a MappedImage key (Image::m_imageFieldParseTable: {string.Join(", ", RequiredKeys)}).");
            values[field.Name] = field;
        }
        foreach (var key in RequiredKeys)
            if (!values.ContainsKey(key))
                throw Error(definition.Location, name, $"required key '{key}' is missing.");

        var texture = Tokens(values["Texture"]);
        if (texture.Length != 1) throw Error(values["Texture"].Location, name, $"Texture = '{values["Texture"].Value.OriginalText}' must be one file name.");
        var width = PositiveInteger(values["TextureWidth"], name);
        var height = PositiveInteger(values["TextureHeight"], name);

        var coords = values["Coords"];
        if (!ValueGrammar.TryLabeled(coords.Value.OriginalText, ["Left", "Top", "Right", "Bottom"], [], out var numbers, out var rest) || rest.Length != 0)
            throw Error(coords.Location, name, $"Coords = '{coords.Value.OriginalText}' must be 'Left:n Top:n Right:n Bottom:n'.");
        var box = numbers.Select(value => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw Error(coords.Location, name, $"Coords component '{value}' is not an integer.")).ToArray();
        var (left, top, right, bottom) = (box[0], box[1], box[2], box[3]);
        if (left < 0 || top < 0 || right <= left || bottom <= top || right > width || bottom > height)
            throw Error(coords.Location, name, $"Coords {left},{top}-{right},{bottom} are not a non-empty rectangle inside the {width}x{height} page.");

        return new MappedImage(name, texture[0], width, height, left, top, right, bottom, ParseStatus(values["Status"], name), definition.Location);
    }

    /// <summary><c>INI::parseBitString32</c> over <c>imageStatusNames</c>: NONE or a blank-separated list of names.</summary>
    private static MappedImageStatus ParseStatus(DefinitionField field, string name)
    {
        var status = MappedImageStatus.None;
        foreach (var token in Tokens(field))
        {
            status |= token.ToUpperInvariant() switch
            {
                "NONE" => MappedImageStatus.None,
                "ROTATED_90_CLOCKWISE" => MappedImageStatus.Rotated90Clockwise,
                "RAW_TEXTURE" => MappedImageStatus.RawTexture,
                _ => throw Error(field.Location, name, $"'{token}' is not an image status (NONE, ROTATED_90_CLOCKWISE, RAW_TEXTURE)."),
            };
        }
        return status;
    }

    private static int PositiveInteger(DefinitionField field, string name)
    {
        var text = field.Value.OriginalText.Trim();
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
            throw Error(field.Location, name, $"{field.Name} = '{text}' must be a positive integer.");
        return value;
    }

    private static string[] Tokens(DefinitionField field) => ValueGrammar.Tokenize(field.Value.OriginalText);

    private static MappedImageFormatException Error(IniSourceLocation location, string image, string message)
        => new($"{location.Source}({location.Line}): MappedImage '{image}': {message}");
}

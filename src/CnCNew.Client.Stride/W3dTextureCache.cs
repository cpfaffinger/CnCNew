using CnCNew.Content;
using CnCNew.Images;
using CnCNew.W3D;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace CnCNew.Client.Stride;

internal sealed class W3dTextureCache(GraphicsDevice graphicsDevice, ContentFileSystem content) : IDisposable
{
    private readonly Dictionary<string, Texture> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingReferences = new(StringComparer.OrdinalIgnoreCase);
    private Texture? _fallback;
    private readonly W3dTexturePathCache _paths = new(content.Exists);

    public int LoadedCount => _textures.Count;
    public int MissingCount => _missingReferences.Count;

    /// <summary>
    /// While set, textures the original recolors for a team (TGA names starting with "ZHC", see <see cref="Layers.Objects.HouseColor"/>) resolve to
    /// a copy recolored with this color (cached per path and color). Callers set it around material creation and reset it to null.
    /// </summary>
    public CnCNew.Data.Definitions.ColorValue? HouseColor { get; set; }

    /// <summary>ZHC textures the recolor path could not handle (not a TGA or not the alpha-mask variant); logged once each.</summary>
    public int UnsupportedHouseColorCount => _unsupportedHouseColor.Count;
    private readonly HashSet<string> _unsupportedHouseColor = new(StringComparer.OrdinalIgnoreCase);

    public Texture Resolve(W3dTextureReference reference)
    {
        var resolution = _paths.Resolve(reference.Name);
        if (!resolution.Found)
        {
            if (_missingReferences.Add(reference.Name))
            {
                Console.Error.WriteLine($"warning: {resolution.Diagnostic} Using visible magenta fallback.");
            }

            return _fallback ??= Texture.New2D(
                graphicsDevice,
                2,
                2,
                PixelFormat.R8G8B8A8_UNorm,
                new[]
                {
                    new Color(255, 0, 255, 255), new Color(0, 0, 0, 255),
                    new Color(0, 0, 0, 255), new Color(255, 0, 255, 255),
                },
                TextureFlags.ShaderResource,
                GraphicsResourceUsage.Immutable);
        }

        var path = resolution.VirtualPath!;
        var recolor = HouseColor is { } team && Layers.Objects.HouseColor.IsHouseColorTexture(reference.Name) ? team : (CnCNew.Data.Definitions.ColorValue?)null;
        if (recolor is not null &&
            (!path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) || !Layers.Objects.HouseColor.IsAlphaMaskTexture(reference.Name)))
        {
            if (_unsupportedHouseColor.Add(reference.Name))
            {
                Console.Error.WriteLine($"warning: house-color texture '{reference.Name}' is not an alpha-mask TGA; it is drawn unrecolored.");
            }

            recolor = null;
        }

        var cacheKey = recolor is { } color ? $"{path}|hc{color.Red:X2}{color.Green:X2}{color.Blue:X2}" : path;
        if (_textures.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        using var stream = content.OpenRead(path);
        Texture texture;
        if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        {
            var image = TgaReader.Read(stream);
            if (recolor is { } teamColor)
            {
                image = image with { RgbaPixels = Layers.Objects.HouseColor.Recolor(image.RgbaPixels, teamColor) };
            }

            var format = graphicsDevice.ColorSpace == ColorSpace.Linear
                ? PixelFormat.R8G8B8A8_UNorm_SRgb
                : PixelFormat.R8G8B8A8_UNorm;
            texture = Texture.New2D(
                graphicsDevice,
                image.Width,
                image.Height,
                format,
                image.RgbaPixels,
                TextureFlags.ShaderResource,
                GraphicsResourceUsage.Immutable);
        }
        else
        {
            texture = Texture.Load(graphicsDevice, stream, loadAsSRGB: graphicsDevice.ColorSpace == ColorSpace.Linear);
        }

        _textures.Add(cacheKey, texture);
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
        _paths.Clear();
        _fallback?.Dispose();
        _fallback = null;
    }
}

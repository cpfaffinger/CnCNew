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
        if (_textures.TryGetValue(path, out var cached))
        {
            return cached;
        }

        using var stream = content.OpenRead(path);
        Texture texture;
        if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        {
            var image = TgaReader.Read(stream);
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

        _textures.Add(path, texture);
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

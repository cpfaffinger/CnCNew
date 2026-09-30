using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Images;
using CnCNew.Ui.Images;
using CnCNew.Ui.Rendering;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace CnCNew.Client.Stride.Ui;

/// <summary>
/// Atlas pages as GPU textures. Pages are plain <c>R8G8B8A8_UNorm</c> (coverage lives in alpha, colors are white, so the
/// sRGB/linear question never touches them) and are filled through the command list of the frame being drawn.
/// </summary>
internal sealed class AtlasPageTextures(GraphicsDevice device) : IAtlasPageSink, IDisposable
{
    private readonly List<Texture> _pages = [];

    /// <summary>The command list uploads go through; set by the renderer for the duration of a frame.</summary>
    public CommandList? CommandList { get; set; }

    public int PageCount => _pages.Count;

    public Texture Page(int index) => _pages[index];

    public void CreatePage(int page, int width, int height)
    {
        if (page != _pages.Count) throw new InvalidOperationException($"Atlas page {page} created out of order ({_pages.Count} exist).");
        _pages.Add(Texture.New2D(device, width, height, PixelFormat.R8G8B8A8_UNorm, TextureFlags.ShaderResource, 1, GraphicsResourceUsage.Default));
    }

    public void Upload(int page, int x, int y, int width, int height, byte[] rgba)
    {
        var commandList = CommandList ?? throw new InvalidOperationException("Atlas upload outside of a UiRenderer frame (no command list).");
        _pages[page].SetData(commandList, rgba, 0, 0, new ResourceRegion(x, y, 0, x + width, y + height, 1));
    }

    public void Dispose()
    {
        foreach (var page in _pages) page.Dispose();
        _pages.Clear();
    }
}

/// <summary>What a draw of a mapped image needs: the texture, the source texels (null = the whole texture) and the page.</summary>
internal readonly record struct UiImageSprite(Texture Texture, RectangleF? Source);

/// <summary>
/// Texture pages of the mapped image table, resolved like the original (<see cref="MappedImageTextures.Candidates"/>:
/// localized folder first). TGA pages are decoded with <see cref="TgaReader"/> (and kept on the CPU only for rotated images
/// that need <see cref="UiImagePixels.UprightRotated"/>), DDS pages go through <c>Texture.Load</c>. A page that is missing or
/// unreadable is remembered as such (and reported once); nothing is substituted silently.
/// </summary>
internal sealed class UiImageTextures(GraphicsDevice device, ContentFileSystem content, MappedImageTable table, string language) : IDisposable
{
    private readonly Dictionary<string, Page?> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UiImageSprite?> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Texture> _rotated = [];
    private readonly SortedSet<string> _sizeMismatches = new(StringComparer.Ordinal);

    public string Language { get; } = language;

    /// <summary>Pages whose real size differs from the declared <c>TextureWidth/Height</c> (source rectangles are scaled).</summary>
    public IReadOnlyCollection<string> SizeMismatches => _sizeMismatches;

    public int LoadedPages => _pages.Values.Count(page => page is not null);

    /// <summary>Resolves an image; <c>null</c> with <paramref name="failure"/> set to the texture file when the page cannot be used.</summary>
    public UiImageSprite? Resolve(MappedImage image, out string failure)
    {
        failure = "";
        if (_sprites.TryGetValue(image.Name, out var cached))
        {
            if (cached is null) failure = image.TextureFile;
            return cached;
        }
        var page = LoadPage(image.TextureFile, out var error);
        UiImageSprite? sprite = null;
        if (page is null)
        {
            failure = error ?? image.TextureFile;
        }
        else if (image.IsRotated)
        {
            if (page.Rgba is null)
            {
                failure = $"{image.TextureFile} (rotated image '{image.Name}' on a compressed page)";
                Report(failure);
            }
            else
            {
                var pixels = UiImagePixels.UprightRotated(image, page.Width, page.Height, page.Rgba);
                var texture = Texture.New2D(device, image.Width, image.Height, Format(), pixels, TextureFlags.ShaderResource, GraphicsResourceUsage.Immutable);
                _rotated.Add(texture);
                sprite = new UiImageSprite(texture, null);
            }
        }
        else
        {
            var sx = page.Width / (float)image.TextureWidth;
            var sy = page.Height / (float)image.TextureHeight;
            sprite = new UiImageSprite(page.Texture, new RectangleF(image.Left * sx, image.Top * sy, (image.Right - image.Left) * sx, (image.Bottom - image.Top) * sy));
        }
        _sprites.Add(image.Name, sprite);
        return sprite;
    }

    // Deliberately never an _SRgb format. Stride's SpriteBatch shader already decodes sRGB itself (vertex color and texture
    // sample alike) whenever the device is in Linear color space, and the bound render target re-encodes the result on
    // write; an _SRgb source view here would add a second, hardware-side decode on top of the shader's own one, darkening
    // every sample. See UiRenderer.ToColor for the matching choice on colors and the full explanation.
    private static PixelFormat Format() => PixelFormat.R8G8B8A8_UNorm;

    private Page? LoadPage(string textureFile, out string? error)
    {
        error = null;
        if (_pages.TryGetValue(textureFile, out var cached))
        {
            if (cached is null) error = textureFile;
            return cached;
        }
        var path = MappedImageTextures.Candidates(textureFile, Language).FirstOrDefault(content.Exists);
        Page? page = null;
        if (path is null)
        {
            Report($"{textureFile} (not found in {Language} or default textures)");
        }
        else
        {
            try
            {
                using var stream = content.OpenRead(path);
                if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                {
                    var tga = TgaReader.Read(stream);
                    var texture = Texture.New2D(device, tga.Width, tga.Height, Format(), tga.RgbaPixels, TextureFlags.ShaderResource, GraphicsResourceUsage.Immutable);
                    page = new Page(texture, texture.Width, texture.Height, tga.RgbaPixels);
                }
                else
                {
                    var texture = Texture.Load(device, stream, loadAsSRGB: false);
                    page = new Page(texture, texture.Width, texture.Height, null);
                }
                if (page.Width != TableWidth(textureFile) || page.Height != TableHeight(textureFile))
                {
                    _sizeMismatches.Add($"{textureFile}: file {page.Width}x{page.Height}, table {TableWidth(textureFile)}x{TableHeight(textureFile)}");
                    Console.Error.WriteLine($"warning: UI texture {path} is {page.Width}x{page.Height}, the image table declares {TableWidth(textureFile)}x{TableHeight(textureFile)}; source rectangles are scaled.");
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
            {
                Report($"{textureFile} ({exception.Message})");
            }
        }
        _pages.Add(textureFile, page);
        if (page is null) error = textureFile;
        return page;
    }

    private int TableWidth(string textureFile) => table.Images.First(image => image.TextureFile.Equals(textureFile, StringComparison.OrdinalIgnoreCase)).TextureWidth;

    private int TableHeight(string textureFile) => table.Images.First(image => image.TextureFile.Equals(textureFile, StringComparison.OrdinalIgnoreCase)).TextureHeight;

    private static void Report(string message) => Console.Error.WriteLine($"warning: UI texture unusable: {message}; drawing the magenta placeholder.");

    public void Dispose()
    {
        foreach (var page in _pages.Values) page?.Texture.Dispose();
        foreach (var texture in _rotated) texture.Dispose();
        _pages.Clear();
        _sprites.Clear();
        _rotated.Clear();
    }

    private sealed record Page(Texture Texture, int Width, int Height, byte[]? Rgba);
}

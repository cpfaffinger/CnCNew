using CnCNew.Images;

namespace CnCNew.Presentation.Terrain;

public enum TerrainTextureStatus
{
    Resolved,
    /// <summary>The map names a terrain type that <c>Terrain.ini</c> does not define (<c>findTerrain</c> returns null).</summary>
    UnknownTerrainType,
    /// <summary>The texture file does not exist in the runtime content.</summary>
    MissingFile,
    /// <summary>The file is not a 24/32-bit true-colour TGA (<c>WorldHeightMap::countTiles</c> rejects it).</summary>
    UndecodableFile,
}

/// <summary>
/// A terrain texture as <c>WorldHeightMap::readTiles</c> sees it: <see cref="Rgba"/> is top-down with the file's first pixel row at the
/// bottom, because the reference ignores the TGA origin bits and always treats the first stored row as the bottom row. For the usual
/// bottom-left-origin files that is the upright image; <see cref="FlippedVertically"/>/<see cref="FlippedHorizontally"/> record files whose
/// origin bits made the reference show them mirrored (reproduced here). <see cref="IdFieldBytes"/> &gt; 0 marks files whose image-id field
/// the reference would misread as pixels (not reproduced; counted).
/// </summary>
public sealed record TerrainTextureImage(string VirtualPath, int Width, int Height, byte[] Rgba, bool FlippedVertically, bool FlippedHorizontally, int IdFieldBytes)
{
    /// <summary><c>WorldHeightMap::countTiles</c>: the largest square of whole 64-pixel tiles, 0 above 10x10.</summary>
    public int SquareTiles
    {
        get
        {
            var tilesX = Width / TerrainAtlasBuilder.TilePixels;
            var tilesY = Height / TerrainAtlasBuilder.TilePixels;
            if (tilesX > 10 || tilesY > 10) return 0;
            var side = Math.Min(tilesX, tilesY);
            return side * side;
        }
    }
}

public sealed record TerrainTextureResult(TerrainTextureStatus Status, string TerrainName, string VirtualPath, TerrainTextureImage? Image, string Detail);

/// <summary>
/// Resolves map texture classes to decoded terrain textures through a caller-supplied file loader (<c>null</c> = file missing). Decoded
/// images are cached by virtual path, so building many maps decodes every texture once. Not thread-safe.
/// </summary>
public sealed class TerrainTextureLookup(TerrainTextureCatalog catalog, Func<string, byte[]?> loadFile)
{
    private readonly Dictionary<string, TerrainTextureResult> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TerrainTextureCatalog Catalog { get; } = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public TerrainTextureResult Resolve(string terrainName)
    {
        if (!Catalog.TryGetTexturePath(terrainName, out var path))
            return new TerrainTextureResult(TerrainTextureStatus.UnknownTerrainType, terrainName, string.Empty, null, "terrain type is not defined in Terrain.ini");
        if (!_cache.TryGetValue(path, out var cached))
        {
            cached = Load(path);
            _cache[path] = cached;
        }
        return cached with { TerrainName = terrainName };
    }

    private TerrainTextureResult Load(string path)
    {
        var bytes = loadFile(path);
        if (bytes is null) return new TerrainTextureResult(TerrainTextureStatus.MissingFile, string.Empty, path, null, "texture file is missing");
        TgaImage upright;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            upright = TgaReader.Read(stream);
        }
        catch (InvalidDataException exception)
        {
            return new TerrainTextureResult(TerrainTextureStatus.UndecodableFile, string.Empty, path, null, exception.Message);
        }
        var idField = bytes[0];
        var descriptor = bytes[17];
        var topOrigin = (descriptor & 0x20) != 0;
        var rightOrigin = (descriptor & 0x10) != 0;
        var pixels = upright.RgbaPixels;
        if (topOrigin || rightOrigin) pixels = Mirror(pixels, upright.Width, upright.Height, topOrigin, rightOrigin);
        return new TerrainTextureResult(TerrainTextureStatus.Resolved, string.Empty, path,
            new TerrainTextureImage(path, upright.Width, upright.Height, pixels, topOrigin, rightOrigin, idField), string.Empty);
    }

    private static byte[] Mirror(byte[] source, int width, int height, bool vertical, bool horizontal)
    {
        var result = new byte[source.Length];
        for (var y = 0; y < height; y++)
        {
            var sourceY = vertical ? height - 1 - y : y;
            for (var x = 0; x < width; x++)
            {
                var sourceX = horizontal ? width - 1 - x : x;
                Buffer.BlockCopy(source, (sourceY * width + sourceX) * 4, result, (y * width + x) * 4, 4);
            }
        }
        return result;
    }
}

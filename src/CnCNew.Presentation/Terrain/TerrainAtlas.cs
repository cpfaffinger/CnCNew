using System.Collections.Immutable;

namespace CnCNew.Presentation.Terrain;

/// <summary>One RGBA8 image level, rows top-down (row 0 is texture coordinate v = 0).</summary>
public sealed record TerrainAtlasLevel(int Width, int Height, ImmutableArray<byte> Rgba);

/// <summary>
/// The per-map terrain texture atlas: every texture class of the map as a square block of <c>TileWidth * 64</c> pixels with a 4-pixel
/// wrap-around border (<c>TerrainTextureClass::update</c>, <c>TILE_OFFSET</c> = 8), so bilinear filtering and the first mip levels do not
/// bleed across class edges. Mip levels are box-filtered like <c>D3DXFilterTexture(D3DX_FILTER_BOX)</c> in the reference.
/// </summary>
public sealed record TerrainAtlas(int Width, int Height, ImmutableArray<byte> Rgba)
{
    public long BaseBytes => (long)Width * Height * 4;

    /// <summary>Bytes of the full mip chain down to 1x1 (what a GPU texture with all mips occupies uncompressed).</summary>
    public long BytesWithMips
    {
        get
        {
            long total = 0;
            int width = Width, height = Height;
            while (true)
            {
                total += (long)width * height * 4;
                if (width == 1 && height == 1) return total;
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
        }
    }

    public TerrainAtlasLevel BaseLevel => new(Width, Height, Rgba);

    /// <summary>Box-filtered mip chain, level 0 first, down to 1x1. Odd sizes average the clamped 2x2 footprint.</summary>
    public ImmutableArray<TerrainAtlasLevel> BuildMipChain()
    {
        var levels = ImmutableArray.CreateBuilder<TerrainAtlasLevel>();
        var current = Rgba.ToArray();
        int width = Width, height = Height;
        levels.Add(BaseLevel);
        while (width > 1 || height > 1)
        {
            var nextWidth = Math.Max(1, width / 2);
            var nextHeight = Math.Max(1, height / 2);
            var next = new byte[nextWidth * nextHeight * 4];
            for (var y = 0; y < nextHeight; y++)
            {
                var y0 = Math.Min(2 * y, height - 1);
                var y1 = Math.Min(2 * y + 1, height - 1);
                for (var x = 0; x < nextWidth; x++)
                {
                    var x0 = Math.Min(2 * x, width - 1);
                    var x1 = Math.Min(2 * x + 1, width - 1);
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var sum = current[(y0 * width + x0) * 4 + channel] + current[(y0 * width + x1) * 4 + channel]
                                + current[(y1 * width + x0) * 4 + channel] + current[(y1 * width + x1) * 4 + channel];
                        next[(y * nextWidth + x) * 4 + channel] = (byte)((sum + 2) / 4);
                    }
                }
            }
            levels.Add(new TerrainAtlasLevel(nextWidth, nextHeight, [..next]));
            current = next;
            width = nextWidth;
            height = nextHeight;
        }
        return levels.ToImmutable();
    }
}

/// <summary>Where a texture class sits in the atlas: the top-left pixel of its tile block (border excluded) and its width in 64-pixel tiles.</summary>
public sealed record TerrainAtlasRegion(int ClassIndex, string ClassName, int X, int Y, int TileWidth)
{
    public int Pixels => TileWidth * TerrainAtlasBuilder.TilePixels;
}

/// <summary>
/// Packs square class blocks into a fixed-width atlas with a deterministic shelf packer (largest blocks first, then class order), the
/// same idea as <c>WorldHeightMap::updateTileTexturePositions</c>. The height is the used height rounded up to a multiple of 64.
/// </summary>
public static class TerrainAtlasBuilder
{
    /// <summary><c>TILE_PIXEL_EXTENT</c>: a source tile is 64x64 pixels and covers 2x2 height cells (32x32 pixels per cell).</summary>
    public const int TilePixels = 64;

    /// <summary>Half of <c>TILE_OFFSET</c>: the wrap-around border on every side of a class block.</summary>
    public const int Border = 4;

    /// <summary><c>TEXTURE_WIDTH</c>: the reference atlas width, also the unit of the stored cliff UV offsets.</summary>
    public const int ReferenceTextureWidth = 2048;

    public sealed record Block(int ClassIndex, string ClassName, int TileWidth, byte[] Rgba);

    public static (TerrainAtlas Atlas, ImmutableArray<TerrainAtlasRegion> Regions) Build(IReadOnlyList<Block> blocks, int atlasWidth)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (atlasWidth < TilePixels + 2 * Border) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
        var order = blocks.Select((block, index) => (block, index))
            .OrderByDescending(entry => entry.block.TileWidth)
            .ThenBy(entry => entry.block.ClassIndex)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.block)
            .ToArray();

        var placements = new List<(Block Block, int X, int Y)>(order.Length);
        int cursorX = 0, shelfY = 0, shelfHeight = 0;
        foreach (var block in order)
        {
            var size = block.TileWidth * TilePixels + 2 * Border;
            if (size > atlasWidth) throw new InvalidDataException($"Texture class '{block.ClassName}' ({size} px) does not fit an atlas of width {atlasWidth}.");
            if (cursorX + size > atlasWidth)
            {
                shelfY += shelfHeight;
                cursorX = 0;
                shelfHeight = 0;
            }
            placements.Add((block, cursorX + Border, shelfY + Border));
            cursorX += size;
            shelfHeight = Math.Max(shelfHeight, size);
        }
        var usedHeight = Math.Max(TilePixels, shelfY + shelfHeight);
        var height = (usedHeight + TilePixels - 1) / TilePixels * TilePixels;
        var pixels = new byte[checked(atlasWidth * height * 4)];
        var regions = ImmutableArray.CreateBuilder<TerrainAtlasRegion>(placements.Count);
        foreach (var (block, x, y) in placements)
        {
            var size = block.TileWidth * TilePixels;
            if (block.Rgba.Length != size * size * 4) throw new ArgumentException($"Block '{block.ClassName}' has {block.Rgba.Length} bytes, expected {size * size * 4}.", nameof(blocks));
            for (var row = -Border; row < size + Border; row++)
            {
                var sourceRow = (row + size) % size;
                for (var column = -Border; column < size + Border; column++)
                {
                    var sourceColumn = (column + size) % size;
                    Buffer.BlockCopy(block.Rgba, (sourceRow * size + sourceColumn) * 4, pixels, ((y + row) * atlasWidth + x + column) * 4, 4);
                }
            }
            regions.Add(new TerrainAtlasRegion(block.ClassIndex, block.ClassName, x, y, block.TileWidth));
        }
        regions.Sort((a, b) => a.ClassIndex.CompareTo(b.ClassIndex));
        return (new TerrainAtlas(atlasWidth, height, System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(pixels)), regions.ToImmutable());
    }
}

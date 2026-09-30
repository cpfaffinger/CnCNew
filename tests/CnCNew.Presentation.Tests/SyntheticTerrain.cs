using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Presentation.Tests;

/// <summary>Small synthetic maps, terrain textures and render options for the world presentation tests (no original data).</summary>
internal static class SyntheticTerrain
{
    public const string GrassClass = "TestGrass";
    public const string RockClass = "TestRock";

    /// <summary>A map of width x height vertices with the given heights; every cell uses tile value 0 unless <paramref name="tiles"/> says otherwise.</summary>
    public static MapData Map(int width, int height, int border, Func<int, int, byte> heights,
        short[]? tiles = null, short[]? blends = null, short[]? extraBlends = null, short[]? cliffIndices = null,
        ImmutableArray<MapBlendedTile> blendedTiles = default, ImmutableArray<MapCliffInfo> cliffInfos = default,
        ImmutableArray<MapTextureClass> classes = default, ImmutableArray<MapPolygonTrigger> triggers = default)
    {
        var cells = width * height;
        var field = new MapHeightField(width, height, border, [new MapBoundary(width - 1 - 2 * border, height - 1 - 2 * border)],
            [..Enumerable.Range(0, cells).Select(index => heights(index % width, index / width))]);
        var stride = (width + 7) / 8;
        var cliff = new byte[stride * height];
        for (var y = 0; y < height - 1; y++)
            for (var x = 0; x < width - 1; x++)
            {
                int a = field.HeightAt(x, y), b = field.HeightAt(x + 1, y), c = field.HeightAt(x, y + 1), d = field.HeightAt(x + 1, y + 1);
                if (Math.Max(Math.Max(a, b), Math.Max(c, d)) - Math.Min(Math.Min(a, b), Math.Min(c, d)) >= 16) cliff[y * stride + (x >> 3)] |= (byte)(1 << (x & 7));
            }
        var blend = new MapBlendTiles(7,
            [..tiles ?? new short[cells]], [..blends ?? new short[cells]], [..extraBlends ?? new short[cells]], [..cliffIndices ?? new short[cells]],
            [..cliff], stride, 8, 0,
            classes.IsDefault ? [new MapTextureClass(0, 4, 2, GrassClass), new MapTextureClass(4, 4, 2, RockClass)] : classes,
            [], blendedTiles.IsDefault ? [] : blendedTiles, cliffInfos.IsDefault ? [] : cliffInfos);
        return new MapData("synthetic", field, blend, MapProperties.Empty, [], [], [], triggers.IsDefault ? [] : triggers, [], [], []);
    }

    /// <summary>
    /// A 128x128 uncompressed 32-bit TGA, bottom-left origin, whose four 64x64 tiles are solid colours: tile (i, j) (i right, j up from
    /// the bottom) has red = 60 + 40 * (i + 2 * j) + <paramref name="shift"/>, green = 10 * i, blue = 10 * j.
    /// </summary>
    public static byte[] Tga(int shift = 0, bool topOrigin = false)
    {
        const int size = 128;
        var data = new byte[18 + size * size * 4];
        data[2] = 2;
        data[12] = size; data[13] = 0;
        data[14] = size; data[15] = 0;
        data[16] = 32;
        data[17] = (byte)(8 | (topOrigin ? 0x20 : 0));
        for (var storedRow = 0; storedRow < size; storedRow++)
            for (var column = 0; column < size; column++)
            {
                var row = topOrigin ? size - 1 - storedRow : storedRow; // row counted from the bottom of the image
                var color = TileColor(column / 64, row / 64, shift);
                var offset = 18 + (storedRow * size + column) * 4;
                data[offset] = color.B;
                data[offset + 1] = color.G;
                data[offset + 2] = color.R;
                data[offset + 3] = 255;
            }
        return data;
    }

    public static Rgba32 TileColor(int i, int j, int shift = 0) => new((byte)(60 + 40 * (i + 2 * j) + shift), (byte)(10 * i), (byte)(10 * j), 255);

    public static TerrainTextureLookup Lookup(bool rockTopOrigin = false)
    {
        var catalog = new TerrainTextureCatalog([new(GrassClass, "grass.tga"), new(RockClass, "rock.tga")]);
        return new TerrainTextureLookup(catalog, path => path switch
        {
            "Art/Terrain/grass.tga" => Tga(),
            "Art/Terrain/rock.tga" => Tga(shift: 1, topOrigin: rockTopOrigin),
            _ => null,
        });
    }

    /// <summary>One light straight down with ambient 0.2 and diffuse 0.6: an up-facing vertex gets 0.8.</summary>
    public static TerrainLighting Lighting { get; } = new("AFTERNOON",
        [new TerrainLight(new Vector3(0.2f), new Vector3(0.6f), new Vector3(0, 0, -1))]);

    public static WaterAppearance Water { get; } = new("AFTERNOON", "water.tga", new Rgba32(100, 120, 140, 200), new Rgba32(140, 140, 255, 255), 3f, 1f, false, 0.002f, 0.002f);

    public static TerrainRenderOptions Options(bool adjustCliffs = true) => new(Lighting, Water, adjustCliffs, true);

    /// <summary>Atlas colour at a normalized UV (nearest texel).</summary>
    public static Rgba32 Sample(TerrainAtlas atlas, Vector2 uv)
    {
        var x = Math.Clamp((int)MathF.Floor(uv.X * atlas.Width), 0, atlas.Width - 1);
        var y = Math.Clamp((int)MathF.Floor(uv.Y * atlas.Height), 0, atlas.Height - 1);
        var offset = (y * atlas.Width + x) * 4;
        return new Rgba32(atlas.Rgba[offset], atlas.Rgba[offset + 1], atlas.Rgba[offset + 2], atlas.Rgba[offset + 3]);
    }
}

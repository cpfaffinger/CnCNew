using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Terrain;

/// <summary>
/// The shadows of the clouds and the macro texture of the terrain (<c>TerrainShader2Stage</c>, <c>BaseHeightMapRenderObjClass::useCloud</c>): two textures laid over the map and multiplied
/// into terrain, roads and bridges. Both cover 31.5 cells; the clouds (<c>TSCloudMed.tga</c>) slide, not at night; the macro texture (<c>TSNoiseUrb.tga</c>) stands still.
/// </summary>
public sealed record TerrainClouds(bool Clouds, bool LightMap)
{
    public const string CloudTexture = "TSCloudMed.tga";
    public const string MacroTexture = "TSNoiseUrb.tga";

    /// <summary><c>STRETCH_FACTOR</c>: one over the world width of a texture, 63 half cells of 10 units.</summary>
    public const float Stretch = 1f / (63f * TerrainHeightSampler.CellSize / 2f);

    /// <summary><c>m_xSlidePerSecond</c>, <c>m_ySlidePerSecond</c>: the texture coordinates per second.</summary>
    public const float SlideX = -0.02f;
    public const float SlideY = 1.5f * SlideX;

    public static TerrainClouds FromDefinitions(CompiledDefinitionGraph graph)
    {
        var gameData = WorldDefinitionReader.GameData(graph);
        var lighting = TerrainLighting.FromDefinitions(graph);
        return new TerrainClouds(
            WorldDefinitionReader.Boolean(gameData, "UseCloudMap") && !lighting.TimeOfDay.Equals("NIGHT", StringComparison.OrdinalIgnoreCase),
            WorldDefinitionReader.Boolean(gameData, "UseLightMap"));
    }

    /// <summary>Where the clouds stand after a number of seconds of game time, as a fraction of the texture (<c>updateCloud</c>).</summary>
    public static (float U, float V) Offset(double seconds)
        => ((float)(seconds * SlideX - Math.Truncate(seconds * SlideX)), (float)(seconds * SlideY - Math.Truncate(seconds * SlideY)));

    /// <summary>
    /// Writes the RGBA picture <paramref name="source"/> shifted by a fraction of its size into <paramref name="target"/>, so that the target at (x, y) shows the source at
    /// (x + offsetU * width, y + offsetV * height), wrapping and bilinear. A picture that moves under fixed texture coordinates is what the original's moving coordinates look like: the material of the
    /// terrain has no coordinate that can change from frame to frame.
    /// </summary>
    public static void Shift(byte[] source, int width, int height, float offsetU, float offsetV, byte[] target)
    {
        var shiftX = offsetU * width;
        var shiftY = offsetV * height;
        var wholeX = (int)MathF.Floor(shiftX);
        var wholeY = (int)MathF.Floor(shiftY);
        var fractionX = (int)((shiftX - wholeX) * 256f);
        var fractionY = (int)((shiftY - wholeY) * 256f);
        var at = 0;
        for (var y = 0; y < height; y++)
        {
            var row0 = Wrap(y + wholeY, height) * width;
            var row1 = Wrap(y + wholeY + 1, height) * width;
            for (var x = 0; x < width; x++)
            {
                var column0 = Wrap(x + wholeX, width);
                var column1 = Wrap(x + wholeX + 1, width);
                for (var channel = 0; channel < 4; channel++)
                {
                    var top = source[(row0 + column0) * 4 + channel] * (256 - fractionX) + source[(row0 + column1) * 4 + channel] * fractionX;
                    var bottom = source[(row1 + column0) * 4 + channel] * (256 - fractionX) + source[(row1 + column1) * 4 + channel] * fractionX;
                    target[at++] = (byte)((top * (256 - fractionY) + bottom * fractionY) >> 16);
                }
            }
        }

        static int Wrap(int value, int size) => ((value % size) + size) % size;
    }
}

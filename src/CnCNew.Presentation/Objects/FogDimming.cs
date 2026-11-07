namespace CnCNew.Presentation.Objects;

/// <summary>
/// How much an object that stands in the fog is darkened, and how that factor has to be handed to a linear-light renderer.
/// <c>RTS3DScene::Customized_Render</c> draws a remembered object (a ghost) lit by the fogged light environment, which is the global light scaled by
/// <c>FogAlpha / ClearAlpha</c>, and an object with a drawable through the shroud texture whose fog level is <c>FogAlpha</c>: in both cases the picture of the object is
/// multiplied by about 127/255 in display values, so the object stays as readable as the fogged ground around it, which the original multiplies the same way. The terrain
/// layer squares the level for the linear pipeline (a fogged cell, one half, is a quarter of the light); an object takes the same square.
/// <para/>
/// A colour constant of a material (a <c>ComputeColor</c>) is read as sRGB and converted to linear light by the renderer in a linear colour space. A factor of 0.25 passed as such a
/// constant became 0.05 linear and a remembered structure showed at a quarter of its brightness instead of a half (display values: 43 for 184); the constant has to be encoded first.
/// </summary>
public static class FogDimming
{
    /// <summary>The factor on the light of a fogged object: the square of the fog level against the clear level (a fog level of 127 against 255 is 0.248).</summary>
    public static float LinearFactor(byte fogLevel, byte clearLevel)
    {
        if (clearLevel == 0) throw new ArgumentOutOfRangeException(nameof(clearLevel), "The clear level is zero.");
        var level = Math.Min(1f, fogLevel / (float)clearLevel);
        return level * level;
    }

    /// <summary>The factor of the original game data (<c>GameData.ini</c>: <c>FogAlpha</c> 127, <c>ClearAlpha</c> 255).</summary>
    public static float DefaultLinearFactor { get; } = LinearFactor(127, 255);

    public static float SrgbToLinear(float value)
        => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    public static float LinearToSrgb(float value)
        => value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;

    /// <summary>
    /// The channel to put into a colour constant so that a renderer that converts constants from sRGB to linear light multiplies the surface by <paramref name="authored"/>
    /// (what the same constant gives without fog, read the same way) times <paramref name="linearDim"/> (a factor on the light). With no dimming the channel is returned as it is.
    /// </summary>
    public static float ConstantFor(float authored, float linearDim)
        => linearDim == 1f ? authored : LinearToSrgb(SrgbToLinear(authored) * linearDim);
}

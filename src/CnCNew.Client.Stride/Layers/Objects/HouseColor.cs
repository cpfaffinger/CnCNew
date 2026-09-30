using CnCNew.Data.Definitions;
using Color = Stride.Core.Mathematics.Color;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// Team color on textures as the original does it: <c>W3DAssetManager.cpp remapAlphaTexture32Bit</c> with <c>DO_HUE_SHIFT</c> (called from
/// <c>Recolor_Mesh</c> for textures whose name starts with "ZHC"). The alpha channel of such a texture is a mask of the house-color area
/// (mask = 255 - alpha): masked pixels keep their value but take the hue of the team color and their saturation is scaled by the team
/// color's saturation; every output pixel is opaque. HSV conversions follow <c>colorspace.h RGB_To_HSV/HSV_To_RGB</c> (hue in degrees,
/// negative when undefined) but are written from the description, not copied.
/// </summary>
internal static class HouseColor
{
    /// <summary>True for texture names the original recolors ("ZHC" prefix, case-insensitive).</summary>
    public static bool IsHouseColorTexture(string textureName)
        => textureName.StartsWith("ZHC", StringComparison.OrdinalIgnoreCase);

    /// <summary>The fourth character selects the method ('A' alpha mask, 'D' palette only); only 'A' is supported.</summary>
    public static bool IsAlphaMaskTexture(string textureName)
        => textureName.Length > 3 && char.ToUpperInvariant(textureName[3]) == 'A';

    public static byte[] Recolor(byte[] rgba, ColorValue team)
    {
        var (teamHue, teamSaturation, _) = ToHsv(team.Red / 255f, team.Green / 255f, team.Blue / 255f);
        var result = new byte[rgba.Length];
        for (var offset = 0; offset + 3 < rgba.Length; offset += 4)
        {
            var mask = 255 - rgba[offset + 3];
            var red = rgba[offset];
            var green = rgba[offset + 1];
            var blue = rgba[offset + 2];
            if (mask != 0)
            {
                var (_, saturation, value) = ToHsv(red / 255f, green / 255f, blue / 255f);
                FromHsv(teamHue, saturation * teamSaturation, value, out var r, out var g, out var b);
                red = (byte)Math.Clamp((int)(r * 255f), 0, 255);
                green = (byte)Math.Clamp((int)(g * 255f), 0, 255);
                blue = (byte)Math.Clamp((int)(b * 255f), 0, 255);
            }

            result[offset] = red;
            result[offset + 1] = green;
            result[offset + 2] = blue;
            result[offset + 3] = 255;
        }

        return result;
    }

    /// <summary>
    /// Team color on vertices as the original does it: <c>W3DAssetManager.cpp Recolor_Vertex_Material</c> for meshes whose bare name
    /// starts with "HOUSECOLOR" (<c>_strnicmp(name, "HOUSECOLOR", 10)</c>). The reference overwrites the vertex material's ambient and
    /// diffuse with the team color; here the same effect is reached by tinting the RGB of the primary vertex color channel (and the
    /// secondary one, if the batch has a second material pass) that <see cref="ObjectRenderVertexBuilder"/> already bakes per vertex,
    /// keeping each vertex's original alpha.
    /// </summary>
    public static void TintVertexColors(CnCNew.Client.Stride.W3dRenderVertex[] vertices, ColorValue team, int passCount)
    {
        var tint = new Color(team.Red, team.Green, team.Blue);
        for (var i = 0; i < vertices.Length; i++)
        {
            ref var vertex = ref vertices[i];
            vertex.Color0 = new Color(tint.R, tint.G, tint.B, vertex.Color0.A);
            if (passCount > 1)
            {
                vertex.Color2 = new Color(tint.R, tint.G, tint.B, vertex.Color2.A);
            }
        }
    }

    internal static (float Hue, float Saturation, float Value) ToHsv(float r, float g, float b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var saturation = max != 0f ? (max - min) / max : 0f;
        if (saturation == 0f)
        {
            return (-1f, 0f, max);
        }

        var delta = max - min;
        float hue;
        if (r == max) hue = (g - b) / delta;
        else if (g == max) hue = 2f + (b - r) / delta;
        else hue = 4f + (r - g) / delta;
        hue *= 60f;
        if (hue < 0f) hue += 360f;
        return (hue, saturation, max);
    }

    internal static void FromHsv(float hue, float saturation, float value, out float r, out float g, out float b)
    {
        if (saturation == 0f)
        {
            r = g = b = value;
            return;
        }

        if (hue >= 360f) hue = 0f;
        var sector = hue / 60f;
        var index = (int)MathF.Floor(sector);
        var fraction = sector - index;
        var p = value * (1f - saturation);
        var q = value * (1f - saturation * fraction);
        var t = value * (1f - saturation * (1f - fraction));
        (r, g, b) = index switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };
    }
}

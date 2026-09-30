using System.Numerics;
using System.Runtime.InteropServices;

namespace CnCNew.Client.Stride.Layers.Terrain;

/// <summary>
/// One terrain vertex of the merged single-pass mesh (52 bytes, no Stride types so the geometry code can run without a GPU).
/// <see cref="Color0"/> is the static lighting in RGB and the weight of the blend tile in A; <see cref="Color1"/> carries the weight of the
/// 3-way tile in A. <see cref="Uv0"/>, <see cref="Uv1"/> and <see cref="Uv2"/> address the base, blend and 3-way tile in the atlas,
/// <see cref="Fog"/> is the position in the fog texture. Colours are packed R8G8B8A8 (R in the lowest byte).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct TerrainVertex
{
    public const int SizeInBytes = 52;

    public Vector3 Position;
    public uint Color0;
    public uint Color1;
    public Vector2 Uv0;
    public Vector2 Uv1;
    public Vector2 Uv2;
    public Vector2 Fog;
}

/// <summary>Water vertex (28 bytes): position, packed RGBA surface colour, texture coordinate, fog texture coordinate.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct WaterVertex
{
    public const int SizeInBytes = 28;

    public Vector3 Position;
    public uint Color;
    public Vector2 Uv;
    public Vector2 Fog;
}

internal static class PackedColor
{
    public static uint Pack(byte r, byte g, byte b, byte a) => r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    public static byte Alpha(uint packed) => (byte)(packed >> 24);

    public static uint WithAlpha(uint packed, byte alpha) => (packed & 0x00FFFFFFu) | ((uint)alpha << 24);

    /// <summary>
    /// 8-bit sRGB to linear decoding table for vertex colours. Stride does not convert vertex colours, but it samples sRGB atlas textures
    /// linearly and encodes the render target, so in a linear colour space the lighting must be decoded once on the CPU; the product then
    /// matches the gamma-space product the original renderer computes.
    /// </summary>
    public static byte[] SrgbDecodeTable()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255.0;
            var linear = c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            table[i] = (byte)Math.Clamp((int)Math.Round(linear * 255.0), 0, 255);
        }
        return table;
    }

    public static byte[] IdentityTable()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++) table[i] = (byte)i;
        return table;
    }
}

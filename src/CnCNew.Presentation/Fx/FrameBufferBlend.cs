using System.Numerics;

namespace CnCNew.Presentation.Fx;

/// <summary>
/// The colour space the effects are blended in. The original draws its particles with the W3D sprite shaders (<c>ShaderClass::_PresetAdditiveSpriteShader</c>, <c>..AlphaSpriteShader</c>,
/// <c>..MultiplicativeSpriteShader</c> in <c>shader.cpp</c>, chosen by <c>W3DParticleSystemManager::doParticles</c>) into a Direct3D 8 frame buffer, which knows no sRGB: texture, vertex
/// colour and frame buffer hold display values, and the blend unit works on those bytes - an additive puff is <c>source + target</c>, an alpha one
/// <c>alpha * source + (1 - alpha) * target</c>, a multiplying one <c>source * target</c>, all in display values. A renderer whose target stores sRGB and blends in linear light decodes the
/// target, blends and encodes again: two half-bright additive puffs over black add up to 176 instead of 255, a half-transparent white over black is 188 instead of 128. That made glowing
/// effects - the nuclear fallout's puddles - look like flat blotches. The client therefore draws the blended sprites in a pass whose target view has no sRGB conversion, with the texture read
/// as it is stored and the vertex colour as the particle gives it (<see cref="VertexChannel"/>); the ground decals and the alpha-tested sprites stay in the linear-light passes, where the
/// decoded vertex colour (<see cref="SrgbDecodeTable"/>) makes the product of texture and colour what it was.
/// </summary>
public static class FrameBufferBlend
{
    /// <summary>8-bit sRGB to linear light for vertex colours drawn in a linear-light pass (the hardware decodes the sRGB textures, but not the vertex colours).</summary>
    public static byte[] SrgbDecodeTable { get; } = BuildDecodeTable();

    public static float Decode(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    public static float Encode(float value) => value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;

    /// <summary>A colour channel of a particle as the byte its vertex carries: as it is in the pass that blends in display values, decoded in a linear-light pass.</summary>
    public static byte VertexChannel(float value, bool linearLightPass)
    {
        var raw = (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);
        return linearLightPass ? SrgbDecodeTable[raw] : raw;
    }

    /// <summary>
    /// One channel as the original's frame buffer blends it: <paramref name="source"/> is what the sprite shader puts out (texture times vertex colour, display values),
    /// <paramref name="alpha"/> its alpha, <paramref name="target"/> what the frame buffer holds; the result is saturated to a byte like the blend unit's.
    /// </summary>
    public static byte InDisplayValues(ParticleShader shader, float source, float alpha, byte target)
    {
        var below = target / 255f;
        return ToByte(Blend(shader, Math.Clamp(source, 0f, 1f), alpha, below));
    }

    /// <summary>
    /// The same channel through a target that stores sRGB and blends in linear light, as the client drew every effect until it was found (issue 356): the shader puts out
    /// <paramref name="linearSource"/> (the decoded texture times the decoded vertex colour), the target is decoded, blended and encoded again.
    /// </summary>
    public static byte InLinearLight(ParticleShader shader, float linearSource, float alpha, byte target)
    {
        var below = Decode(target / 255f);
        return ToByte(Encode(Math.Clamp(Blend(shader, Math.Clamp(linearSource, 0f, 1f), alpha, below), 0f, 1f)));
    }

    /// <summary>The sprite shader's output of one channel, in display values: the texel times the vertex colour (<c>D3DTOP_MODULATE</c>).</summary>
    public static float SpriteOutput(float texel, float vertexColor) => texel * vertexColor;

    /// <summary>A colour of a particle as the vertex of the pass that blends in display values carries it, packed R, G, B, A from the low byte.</summary>
    public static uint PackDisplay(Vector3 rgb, float alpha)
        => VertexChannel(rgb.X, false) | ((uint)VertexChannel(rgb.Y, false) << 8) | ((uint)VertexChannel(rgb.Z, false) << 16) | ((uint)VertexChannel(alpha, false) << 24);

    /// <summary>Decodes the colour channels of a vertex packed by <see cref="PackDisplay"/> for a linear-light pass; the alpha stays.</summary>
    public static uint ToLinearLight(uint packed)
    {
        var table = SrgbDecodeTable;
        return table[packed & 0xFF] | ((uint)table[(packed >> 8) & 0xFF] << 8) | ((uint)table[(packed >> 16) & 0xFF] << 16) | (packed & 0xFF000000u);
    }

    private static float Blend(ParticleShader shader, float source, float alpha, float target) => shader switch
    {
        ParticleShader.Additive => source + target,
        ParticleShader.Alpha => alpha * source + (1f - alpha) * target,
        ParticleShader.Multiply => source * target,
        ParticleShader.AlphaTest => source,
        _ => throw new ArgumentOutOfRangeException(nameof(shader), shader, "No blend for this shader."),
    };

    private static byte ToByte(float value) => (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);

    private static byte[] BuildDecodeTable()
    {
        var table = new byte[256];
        for (var index = 0; index < 256; index++) table[index] = (byte)(Decode(index / 255f) * 255f + 0.5f);
        return table;
    }
}

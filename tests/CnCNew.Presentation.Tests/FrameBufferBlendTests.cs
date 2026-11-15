using System.Numerics;
using CnCNew.Presentation.Fx;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// Issue 356: the client blended every effect in linear light (an sRGB target), the original adds, mixes and multiplies the display values of its frame buffer
/// (<c>SC_ADD_SPRITE</c>, <c>SC_ALPHA_SPRITE</c>, <c>SC_MUL_SPRITE</c> in <c>shader.cpp</c>, a Direct3D 8 frame buffer without sRGB). These tests measure what the old path gave against the
/// original's sums and pin what the client hands to its pass in display values: the particle's colour bytes as they are, and for the linear-light passes the decoded bytes of before.
/// </summary>
internal static class FrameBufferBlendTests
{
    public static int Run(string[] args)
    {
        var t = new TestHarness("FrameBufferBlend");
        t.Run("two half-bright additive puffs over black reach white in the original, 176 in linear light", AdditiveOverlap);
        t.Run("a glow of eight faint additive puffs builds to white in the original, to 99 in linear light", StackedGlow);
        t.Run("alpha and multiply in display values against linear light", AlphaAndMultiply);
        t.Run("the pass in display values gets the particle's colour as it is and reproduces the original's sums", DisplayPass);
        t.Run("a linear-light run decodes its vertex colour exactly as before", LinearRuns);
        t.Run("a surface shaded in linear light and encoded keeps its look over black and blends on the bytes (issue 359)", EncodedSurfaces);
        return t.Failures;
    }

    /// <summary>The bytes after drawing the same additive sprite <paramref name="layers"/> times over black, through each path; texel white, vertex colour <paramref name="vertex"/>.</summary>
    private static (byte Original, byte LinearLight, byte DisplayPass) Stack(ParticleShader shader, float vertex, float alpha, int layers, byte start = 0)
    {
        byte original = start, linear = start, display = start;
        // The old client: the decoded texel (white stays 1) times the decoded vertex colour.
        var linearSource = FrameBufferBlend.SpriteOutput(1f, FrameBufferBlend.VertexChannel(vertex, linearLightPass: true) / 255f);
        // The pass in display values: the raw texel times the raw vertex byte, blended on the stored bytes.
        var displaySource = FrameBufferBlend.SpriteOutput(1f, FrameBufferBlend.VertexChannel(vertex, linearLightPass: false) / 255f);
        for (var layer = 0; layer < layers; layer++)
        {
            original = FrameBufferBlend.InDisplayValues(shader, vertex, alpha, original);
            linear = FrameBufferBlend.InLinearLight(shader, linearSource, alpha, linear);
            display = FrameBufferBlend.InDisplayValues(shader, displaySource, alpha, display);
        }

        return (original, linear, display);
    }

    private static void AdditiveOverlap()
    {
        var one = Stack(ParticleShader.Additive, 128 / 255f, 1f, 1);
        Assert(one.Original == 128 && one.LinearLight == 128, $"one puff alone looks the same either way ({one.Original}, {one.LinearLight})");
        var two = Stack(ParticleShader.Additive, 128 / 255f, 1f, 2);
        Assert(two.Original == 255, $"the original adds the bytes: 128 + 128 saturates at {two.Original}");
        Assert(two.LinearLight == 176, $"in linear light the two only reach {two.LinearLight}");
        Assert(two.DisplayPass == two.Original, $"the pass in display values gives {two.DisplayPass}, the original's {two.Original}");
        var overGrey = Stack(ParticleShader.Additive, 128 / 255f, 1f, 1, start: 100);
        Assert(overGrey.Original == 228 && overGrey.LinearLight == 158 && overGrey.DisplayPass == 228, $"a puff of 128 over a ground of 100: original {overGrey.Original}, linear light {overGrey.LinearLight}, pass {overGrey.DisplayPass}");
    }

    private static void StackedGlow()
    {
        var glow = Stack(ParticleShader.Additive, 32 / 255f, 1f, 8);
        Assert(glow.Original == 255 && glow.DisplayPass == 255, $"eight puffs of 32 build to {glow.Original} in the original, {glow.DisplayPass} in the pass");
        Assert(glow.LinearLight == 99, $"in linear light they stay at {glow.LinearLight}: a flat blotch where the original glows");
    }

    private static void AlphaAndMultiply()
    {
        var alpha = Stack(ParticleShader.Alpha, 1f, 0.5f, 1);
        Assert(alpha.Original == 128 && alpha.DisplayPass == 128 && alpha.LinearLight == 188, $"half-transparent white over black: original {alpha.Original}, linear light {alpha.LinearLight}, pass {alpha.DisplayPass}");
        var multiply = Stack(ParticleShader.Multiply, 128 / 255f, 1f, 1, start: 128);
        Assert(multiply.Original == 64 && multiply.DisplayPass == 64, $"128 times 128 is {multiply.Original} in the original, {multiply.DisplayPass} in the pass");
        Assert(Math.Abs(multiply.LinearLight - multiply.Original) <= 4, $"a product hardly depends on the colour space: {multiply.LinearLight} in linear light");
    }

    private static void DisplayPass()
    {
        var packed = FrameBufferBlend.PackDisplay(new Vector3(0.5f, 0.25f, 1f), 0.75f);
        Assert(packed == (128u | (64u << 8) | (255u << 16) | (191u << 24)), $"R, G, B, A as the particle gives them: {packed:X8}");
        // Every byte of every channel through each blend: the pass computes the original's sum.
        foreach (var shader in new[] { ParticleShader.Additive, ParticleShader.Alpha, ParticleShader.Multiply })
        {
            for (var source = 0; source < 256; source += 5)
            {
                for (var target = 0; target < 256; target += 3)
                {
                    var vertex = source / 255f;
                    var expected = FrameBufferBlend.InDisplayValues(shader, vertex, 0.4f, (byte)target);
                    var drawn = FrameBufferBlend.InDisplayValues(shader, FrameBufferBlend.VertexChannel(vertex, linearLightPass: false) / 255f, 0.4f, (byte)target);
                    if (drawn != expected) { Assert(false, $"{shader} {source} over {target}: {drawn} against {expected}"); return; }
                }
            }
        }
    }

    private static void EncodedSurfaces()
    {
        // Every shaded value alone over black: the encoded output shows what the sRGB target showed, within one step of a byte.
        foreach (var shader in new[] { ParticleShader.Additive, ParticleShader.Alpha })
        {
            for (var level = 0; level <= 255; level++)
            {
                var linear = FrameBufferBlend.Decode(level / 255f);
                var before = FrameBufferBlend.InLinearLight(shader, linear, 1f, 0);
                var now = FrameBufferBlend.ThroughEncodedOutput(shader, linear, 1f, 0);
                if (Math.Abs(before - now) > 1) { Assert(false, $"{shader} level {level}: {now} against {before}"); return; }
            }
        }

        // Two additive glows of a translucent mesh (a muzzle flash over its own second card) add their bytes, as the original's ADD shader does.
        var glow = FrameBufferBlend.Decode(100 / 255f);
        var once = FrameBufferBlend.ThroughEncodedOutput(ParticleShader.Additive, glow, 1f, 0);
        var twice = FrameBufferBlend.ThroughEncodedOutput(ParticleShader.Additive, glow, 1f, once);
        var linearTwice = FrameBufferBlend.InLinearLight(ParticleShader.Additive, glow, 1f, FrameBufferBlend.InLinearLight(ParticleShader.Additive, glow, 1f, 0));
        Assert(once == 100 && twice == 200, $"100 and 100 make {twice}, as the original adds ({once} after the first)");
        Assert(linearTwice == 138, $"in linear light they made {linearTwice}");

        // A scorch decal of half alpha, black, over a ground of 160: the original darkens to 80, linear light left 116.
        var scorch = FrameBufferBlend.InDisplayValues(ParticleShader.Alpha, FrameBufferBlend.VertexChannel(0f, false) / 255f, 0.5f, 160);
        var scorchBefore = FrameBufferBlend.InLinearLight(ParticleShader.Alpha, 0f, 0.5f, 160);
        Assert(scorch == 80 && scorchBefore == 116, $"half a black scorch over 160: {scorch} now, {scorchBefore} in linear light");

        // Water of 40 % alpha over a bright bank of 200, a water colour of 60: the bytes mix as the original's alpha blend.
        var water = FrameBufferBlend.ThroughEncodedOutput(ParticleShader.Alpha, FrameBufferBlend.Decode(60 / 255f), 0.4f, 200);
        Assert(water == 144, $"water over the bank shows {water} (0.4 * 60 + 0.6 * 200)");
    }

    private static void LinearRuns()
    {
        // The table of the client before this change, written out: the ground decals and the alpha-tested sprites keep their look.
        for (var index = 0; index < 256; index++)
        {
            var value = index / 255f;
            var linear = value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
            var before = (byte)(linear * 255f + 0.5f);
            if (FrameBufferBlend.SrgbDecodeTable[index] != before) { Assert(false, $"byte {index}: {FrameBufferBlend.SrgbDecodeTable[index]} against {before}"); return; }
        }

        var color = new Vector3(0.5f, 0.2f, 0.9f);
        var decoded = FrameBufferBlend.ToLinearLight(FrameBufferBlend.PackDisplay(color, 0.6f));
        var table = FrameBufferBlend.SrgbDecodeTable;
        var expected = table[128] | ((uint)table[51] << 8) | ((uint)table[230] << 16) | (153u << 24);
        Assert(decoded == expected, $"decoded colour channels and the alpha untouched: {decoded:X8} against {expected:X8}");
        Assert(Math.Abs(FrameBufferBlend.Encode(FrameBufferBlend.Decode(0.37f)) - 0.37f) < 1e-5f, "encode undoes decode");
    }
}

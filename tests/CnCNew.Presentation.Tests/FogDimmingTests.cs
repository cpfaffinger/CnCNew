using CnCNew.Presentation.Objects;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The owner's playtest of 7 November 2026: a remembered supply depot in the fog was very dark and almost invisible. The original multiplies the picture of an object in the
/// fog by about 127/255 in display values (<c>RTS3DScene::Customized_Render</c>: the fogged light environment, or the shroud pass), as it does the fogged ground. The client handed
/// the factor to the materials as a colour constant, which the renderer converts from sRGB to linear light: 0.25 became 0.05 and the object showed at a quarter of its brightness.
/// </summary>
internal static class FogDimmingTests
{
    public static int Run(string[] args)
    {
        var t = new TestHarness("FogDimming");
        t.Run("an object in the fog is darkened like the fogged ground: about half in display values, not to a quarter", Darkening);
        t.Run("the constant that carries the factor survives the renderer's conversion, and an undimmed one is untouched", Constants);
        return t.Failures;
    }

    /// <summary>What the screen shows of a surface of display brightness <paramref name="display"/> after the renderer multiplied its linear light by <paramref name="linearFactor"/>.</summary>
    private static float Shown(float display, float linearFactor) => FogDimming.LinearToSrgb(FogDimming.SrgbToLinear(display) * linearFactor);

    private static void Darkening()
    {
        var factor = FogDimming.LinearFactor(127, 255);
        Assert(MathF.Abs(factor - 0.248f) < 0.001f && factor == FogDimming.DefaultLinearFactor, $"the fog level of the original, squared as the terrain layer does ({factor})");
        // What the renderer finally multiplies, from the constant the client passes (it converts a constant from sRGB to linear light).
        var constant = FogDimming.ConstantFor(1f, factor);
        var multiplied = FogDimming.SrgbToLinear(constant);
        Assert(MathF.Abs(multiplied - factor) < 0.0005f, $"the renderer multiplies the light by {multiplied}, as asked ({factor})");
        foreach (var display in new[] { 0.9f, 0.72f, 0.5f })
        {
            var ratio = Shown(display, multiplied) / display;
            Assert(ratio is > 0.46f and < 0.60f, $"a surface of {display:F2} shows at {ratio:F2} of its brightness in the fog (the original's 127/255 = 0.50)");
            // The ground around it is multiplied by the same square of the fog texture's level: the object and the ground keep their contrast.
            var ground = Shown(display, (127 / 255f) * (127 / 255f)) / display;
            Assert(MathF.Abs(ratio - ground) < 0.001f, $"object {ratio:F3} and ground {ground:F3} are darkened alike");
        }
        // The defect: the same 0.25 handed over unencoded is converted once more.
        var unencoded = FogDimming.SrgbToLinear(0.25f);
        Assert(MathF.Abs(unencoded - 0.0509f) < 0.001f && Shown(0.72f, unencoded) / 0.72f < 0.25f, $"unencoded the factor would reach the surface as {unencoded} and show at {Shown(0.72f, unencoded) / 0.72f:F2}");
    }

    private static void Constants()
    {
        foreach (var authored in new[] { 0f, 0.31f, 0.5f, 1f, 1.4f })
        {
            Assert(FogDimming.ConstantFor(authored, 1f) == authored, $"no dimming leaves {authored} as it is");
            var wanted = FogDimming.SrgbToLinear(authored) * 0.25f;
            var got = FogDimming.SrgbToLinear(FogDimming.ConstantFor(authored, 0.25f));
            Assert(MathF.Abs(got - wanted) < 0.0005f, $"{authored} dimmed to a quarter of its light: {got} ({wanted})");
        }
        try { _ = FogDimming.LinearFactor(127, 0); Assert(false, "a clear level of zero is refused"); }
        catch (ArgumentOutOfRangeException) { }
    }
}

using System.Numerics;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// How a drawable's flash tint (<c>DrawableInstance.FlashTint</c>, the value of <c>Drawable::getSelectionColor</c>) reaches the materials. The original adds the tint to the
/// ambient and to the diffuse colour of every global light of that one drawable (<c>RTS3DScene::Customized_Render</c> in <c>W3DScene.cpp</c>); the lights here are scene
/// lights shared by all objects, so the tint becomes a factor on the surface colour instead: <c>1 + Gain * tint</c> per channel, never below zero. With an ambient of about
/// 0.4 and a sun of about 0.8 a face that is lit at a medium angle receives about 0.9, and adding the tint to both lights changes that by about 1.6 times the tint, so the
/// gain is 2 (an approximation: the original brightens dark faces relatively more than lit ones). The factor is quantised to 1/32 so the material variants can be cached.
/// </summary>
internal static class FlashTint
{
    public const float Gain = 2f;
    private const int Steps = 32;

    /// <summary>The key of "no tint" (a factor of one on every channel).</summary>
    public const uint None = (Steps << 16) | (Steps << 8) | Steps;

    public static uint Key(Vector3 tint)
        => tint == Vector3.Zero ? None : (Quantise(tint.X) << 16) | (Quantise(tint.Y) << 8) | Quantise(tint.Z);

    /// <summary>The factor on the surface colour for <paramref name="key"/>.</summary>
    public static Vector3 Factor(uint key) => new(((key >> 16) & 0xFF) / (float)Steps, ((key >> 8) & 0xFF) / (float)Steps, (key & 0xFF) / (float)Steps);

    private static uint Quantise(float tint) => (uint)Math.Clamp((int)MathF.Round((1f + Gain * tint) * Steps), 0, 255);
}

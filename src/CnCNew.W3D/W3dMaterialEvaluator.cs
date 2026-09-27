using System.Numerics;

namespace CnCNew.W3D;

public static class W3dMaterialEvaluator
{
    public static Vector4 ApplyPrimaryGradient(
        Vector4 fragment,
        Vector4 gradient,
        W3dPrimaryGradient function)
        => function switch
        {
            W3dPrimaryGradient.Disabled => fragment,
            W3dPrimaryGradient.Modulate => fragment * gradient,
            W3dPrimaryGradient.Add => new Vector4(
                Vector3.Clamp(new Vector3(fragment.X, fragment.Y, fragment.Z) + new Vector3(gradient.X, gradient.Y, gradient.Z), Vector3.Zero, Vector3.One),
                gradient.W),
            W3dPrimaryGradient.Modulate2X => new Vector4(
                Vector3.Clamp(new Vector3(fragment.X * gradient.X, fragment.Y * gradient.Y, fragment.Z * gradient.Z) * 2, Vector3.Zero, Vector3.One),
                fragment.W * gradient.W),
            W3dPrimaryGradient.BumpEnvironmentMap or W3dPrimaryGradient.BumpEnvironmentMapLuminance
                => throw new InvalidDataException($"Gradient '{function}' requires bump/environment inputs."),
            _ => throw new InvalidDataException($"Unknown primary gradient {(byte)function}."),
        };

    public static Vector4 ApplyDetail(
        Vector4 local,
        Vector4 other,
        W3dDetailColorFunction colorFunction,
        W3dDetailAlphaFunction alphaFunction)
    {
        var rgb = colorFunction switch
        {
            W3dDetailColorFunction.Disabled => local,
            W3dDetailColorFunction.Detail => other,
            W3dDetailColorFunction.Scale => local * other,
            W3dDetailColorFunction.InverseScale => Vector4.One - (Vector4.One - local) * (Vector4.One - other),
            W3dDetailColorFunction.Add => local + other,
            W3dDetailColorFunction.Subtract => local - other,
            W3dDetailColorFunction.ReverseSubtract => other - local,
            W3dDetailColorFunction.Blend => local.W * local + (1 - local.W) * other,
            W3dDetailColorFunction.DetailBlend => other.W * local + (1 - other.W) * other,
            W3dDetailColorFunction.AddSigned => local + other - new Vector4(0.5f),
            W3dDetailColorFunction.AddSigned2X => (local + other - new Vector4(0.5f)) * 2,
            W3dDetailColorFunction.Scale2X => local * other * 2,
            W3dDetailColorFunction.ModulateAlphaAddColor => local + local.W * other,
            _ => throw new InvalidDataException($"Unknown detail color function {(byte)colorFunction}."),
        };
        var alpha = alphaFunction switch
        {
            W3dDetailAlphaFunction.Disabled => local.W,
            W3dDetailAlphaFunction.Detail => other.W,
            W3dDetailAlphaFunction.Scale => local.W * other.W,
            W3dDetailAlphaFunction.InverseScale => 1 - (1 - local.W) * (1 - other.W),
            _ => throw new InvalidDataException($"Unknown detail alpha function {(byte)alphaFunction}."),
        };
        return new Vector4(Vector3.Clamp(new Vector3(rgb.X, rgb.Y, rgb.Z), Vector3.Zero, Vector3.One), Math.Clamp(alpha, 0, 1));
    }
}

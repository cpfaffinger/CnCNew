using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.Presentation.Shadows;

/// <summary>How a template casts its shadow (<c>Shadow</c>, <c>ShadowTexture</c>, <c>ShadowSizeX/Y</c>, <c>ShadowOffsetX/Y</c> of <c>ThingTemplate</c>).</summary>
public sealed record ShadowSpec(bool Volume, bool Decal, string? Texture, float SizeX, float SizeY, float OffsetX, float OffsetY)
{
    /// <summary>The name of a decal's texture: <c>W3DProjectedShadowManager::addShadow</c> takes <c>shadow.tga</c> without a name, and the name with <c>.tga</c> otherwise.</summary>
    public string DecalTexture => Texture is { Length: > 1 } name ? name + ".tga" : ShadowDecal.DefaultTexture;

    public static ShadowSpec None { get; } = new(false, false, null, 0f, 0f, 0f, 0f);

    public static ShadowSpec From(ImmutableArray<string> types, string? texture, float sizeX, float sizeY, float offsetX, float offsetY)
    {
        var volume = types.Contains("SHADOW_VOLUME", StringComparer.OrdinalIgnoreCase);
        var decal = types.Contains("SHADOW_DECAL", StringComparer.OrdinalIgnoreCase);
        return volume || decal ? new ShadowSpec(volume, decal, texture, sizeX, sizeY, offsetX, offsetY) : None;
    }
}

/// <summary>
/// A shadow decal under an object (<c>W3DProjectedShadowManager::queueDecal</c>): a rectangle laid on the ground and turned with the object. <see cref="U"/> and <see cref="V"/> are the
/// world directions of the texture's axes (one unit long), <see cref="Center"/> the world point of its middle. The original multiplies it into what is drawn
/// (<c>_PresetMultiplicativeShader</c>).
/// </summary>
public readonly record struct ShadowDecal(string Texture, Vector2 Center, Vector2 U, Vector2 V, float SizeX, float SizeY)
{
    public const string DefaultTexture = "shadow.tga";

    /// <summary>
    /// The decal of an object at <paramref name="position"/> with heading <paramref name="yaw"/>. The texture's u runs along the object's X axis and v along its Y axis: <c>queueDecal</c>
    /// takes the V axis 90 degrees clockwise of U and a negative one over height for it. <c>ShadowOffsetX</c> moves the decal forward, <c>ShadowOffsetY</c> to the object's right
    /// (<c>addShadow</c>: <c>decalOffsetX = -offsetX * oowSizeX</c>, <c>decalOffsetY = -offsetY * oowSizeY</c>, and the height's inverse is negative).
    /// </summary>
    public static ShadowDecal Under(string texture, Vector3 position, float yaw, float sizeX, float sizeY, float offsetX, float offsetY)
    {
        var u = new Vector2(MathF.Cos(yaw), MathF.Sin(yaw));
        var v = new Vector2(-u.Y, u.X);
        return new ShadowDecal(texture, new Vector2(position.X, position.Y) + u * offsetX - v * offsetY, u, v, sizeX, sizeY);
    }

    /// <summary>The decal of a tree (<c>W3DTreeBuffer::drawTrees</c>): <c>shadow.tga</c>, no turn, as wide as the average extent of the model times two.</summary>
    public static ShadowDecal UnderTree(Vector3 position, float size)
        => new(DefaultTexture, new Vector2(position.X, position.Y), Vector2.UnitX, Vector2.UnitY, size, size);

    /// <summary>The world position of a point of the texture, <paramref name="u"/> and <paramref name="v"/> from 0 to 1.</summary>
    public Vector2 At(float u, float v) => Center + U * ((u - 0.5f) * SizeX) + V * ((v - 0.5f) * SizeY);
}

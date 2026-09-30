using CnCNew.Client.Stride.Rendering;
using CnCNew.W3D;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace CnCNew.Client.Stride.Layers.Fx;

/// <summary>The four blend equations of the W3D sprite shader presets.</summary>
internal enum FxBlend
{
    /// <summary>One / One, alpha ignored (the particle system fades additive sprites through their color).</summary>
    Additive,

    /// <summary>SrcAlpha / InvSrcAlpha.</summary>
    Alpha,

    /// <summary>Opaque with an alpha cutoff; writes depth like an opaque mesh.</summary>
    AlphaTest,

    /// <summary>Zero / SrcColor: darkens what is behind.</summary>
    Multiply,
}

/// <summary>A draw state: texture name (null draws the vertex color only) and blend equation.</summary>
internal readonly record struct FxMaterialKey(string? Texture, FxBlend Blend);

/// <summary>
/// Unlit materials for effect quads: output is <c>texture * vertex color</c> (RGBA) as emissive color and alpha, blended with a manual
/// blend state. They are registered with <see cref="RenderServices.DepthWrites"/> so the compositor's pipeline processor switches depth
/// writes off for everything but alpha-tested sprites (depth test stays on, like the W3D sprite shaders).
/// </summary>
internal sealed class FxMaterials(RenderServices services)
{
    private const float AlphaTestReference = 96f / 255f;

    private readonly Dictionary<FxMaterialKey, Material> _materials = [];

    public int Count => _materials.Count;

    public Material Get(FxMaterialKey key)
    {
        if (_materials.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var material = Create(key);
        _materials.Add(key, material);
        return material;
    }

    private Material Create(FxMaterialKey key)
    {
        IComputeColor vertexColor = new ComputeVertexStreamColor { Stream = new ColorVertexStreamDefinition(0) };
        IComputeColor color = vertexColor;
        if (key.Texture is not null)
        {
            var texture = services.Textures.Resolve(new W3dTextureReference(key.Texture));
            color = new ComputeBinaryColor(
                new ComputeTextureColor(texture, TextureCoordinate.Texcoord0, Vector2.One, Vector2.Zero)
                {
                    Filtering = TextureFilter.Linear,
                    AddressModeU = TextureAddressMode.Clamp,
                    AddressModeV = TextureAddressMode.Clamp,
                },
                vertexColor,
                BinaryOperator.Multiply);
        }

        var attributes = new MaterialAttributes
        {
            Emissive = new MaterialEmissiveMapFeature(color) { UseAlpha = true },
            CullMode = CullMode.None,
        };
        if (key.Blend == FxBlend.AlphaTest)
        {
            attributes.Transparency = new MaterialTransparencyCutoffFeature { Alpha = new ComputeFloat(AlphaTestReference) };
        }

        var material = Material.New(services.GraphicsDevice, new MaterialDescriptor { Attributes = attributes });
        var pass = material.Passes[0];
        pass.CullMode = CullMode.None;
        pass.DepthFunction = CompareFunction.LessEqual;
        pass.AlphaToCoverage = false;
        switch (key.Blend)
        {
            case FxBlend.Additive:
                pass.HasTransparency = true;
                pass.BlendState = new BlendStateDescription(Blend.One, Blend.One);
                break;
            case FxBlend.Alpha:
                pass.HasTransparency = true;
                pass.BlendState = new BlendStateDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha);
                break;
            case FxBlend.Multiply:
                pass.HasTransparency = true;
                pass.BlendState = new BlendStateDescription(Blend.Zero, Blend.SourceColor);
                break;
            case FxBlend.AlphaTest:
                pass.HasTransparency = false;
                break;
            default:
                throw new InvalidOperationException($"Unknown effect blend {key.Blend}.");
        }

        services.DepthWrites[pass] = key.Blend == FxBlend.AlphaTest;
        return material;
    }
}

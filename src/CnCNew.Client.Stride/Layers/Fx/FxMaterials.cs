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

    /// <summary>DestColor / One: lights what is behind, the target times one plus the colour, as a dynamic light adds to the light of the terrain.</summary>
    Light,
}

/// <summary>A draw state: texture name (null draws the vertex color only), blend equation, and whether it lies on the ground with the terrain, under the stencil shadows.</summary>
internal readonly record struct FxMaterialKey(string? Texture, FxBlend Blend, bool Ground = false)
{
    /// <summary>
    /// Drawn by <see cref="DisplayValuePass"/>, which blends in display values as the original's frame buffer does (<see cref="CnCNew.Presentation.Fx.FrameBufferBlend"/>): the texels and the vertex
    /// colour reach the blend as they are stored. Every blended run is; an alpha-tested sprite is opaque and drawn with the world, so its colour does not depend on the blend. A ground run is
    /// drawn with the transparent stage, which is in display values only while <see cref="DisplayValueOutput.Available"/>.
    /// </summary>
    public bool DisplayValues => Blend != FxBlend.AlphaTest && (!Ground || DisplayValueOutput.Available);

    /// <summary>
    /// In the stage of the blended effects, drawn after everything else (<c>DoParticles</c> last in <c>RTS3DScene::Flush</c>): every blended sprite, streak, tracer and band. The ground layers
    /// (decals, tread marks, light pulses, shadow decals) stay in the transparent stage, under the stencil shadows and before water and translucent meshes.
    /// </summary>
    public bool InEffectsStage => !Ground && Blend != FxBlend.AlphaTest;
}

/// <summary>
/// Unlit materials for effect quads: output is <c>texture * vertex color</c> (RGBA) as emissive color and alpha, blended with a manual
/// blend state. They are registered with <see cref="RenderServices.DepthWrites"/> so the compositor's pipeline processor switches depth
/// writes off for everything but alpha-tested sprites (depth test stays on, like the W3D sprite shaders). A material of
/// <see cref="FxMaterialKey.DisplayValues"/> reads its texture undecoded.
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
            // The pass that blends in display values reads the texels as they are stored, as the original's sprite shader does; the linear-light passes read them decoded.
            var texture = services.Textures.Resolve(new W3dTextureReference(key.Texture), raw: key.DisplayValues);
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
                pass.BlendState = Shadows.ShadowVolumeRenderer.Multiply(Blend.Zero, Blend.SourceColor);
                break;
            case FxBlend.Light:
                pass.HasTransparency = true;
                pass.BlendState = Shadows.ShadowVolumeRenderer.Multiply(Blend.DestinationColor, Blend.One);
                break;
            case FxBlend.AlphaTest:
                pass.HasTransparency = false;
                break;
            default:
                throw new InvalidOperationException($"Unknown effect blend {key.Blend}.");
        }

        services.DepthWrites[pass] = key.Blend == FxBlend.AlphaTest;
        if (key.Ground) services.TransparentOrder.Layers[pass] = StackedBackToFrontSortMode.GroundDecalLayer;
        return material;
    }
}

using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace CnCNew.Client.Stride.Layers.Terrain;

/// <summary>
/// Material graphs of the terrain and water layers, built from Stride compute-colour nodes (no custom shader assets). Terrain, per pixel:
/// <c>lerp(lerp(atlas(uv0), atlas(uv1), w1), atlas(uv2), w2) * lighting * fog</c>, where <c>w1</c> is the alpha of vertex colour 0 and
/// <c>w2</c> that of vertex colour 1. The result goes into the emissive channel, so the sun light of the host does not touch terrain
/// (the vertex colours already contain the map lighting).
/// </summary>
internal static class TerrainMaterials
{
    private static readonly TextureCoordinate Uv0 = (TextureCoordinate)0;
    private static readonly TextureCoordinate Uv1 = (TextureCoordinate)1;
    private static readonly TextureCoordinate Uv2 = (TextureCoordinate)2;
    private static readonly TextureCoordinate FogUv = (TextureCoordinate)3;

    public static Material CreateTerrain(GraphicsDevice device, Texture atlas, Texture fog, bool linearSpace, Dictionary<MaterialPass, bool> depthWrites)
    {
        IComputeColor Atlas(TextureCoordinate coordinate) => new ComputeTextureColor(atlas, coordinate, Vector2.One, Vector2.Zero)
        {
            // The atlas has a 4-pixel wrap-around border per class block, so clamping never shows a foreign block.
            Filtering = TextureFilter.Anisotropic,
            AddressModeU = TextureAddressMode.Clamp,
            AddressModeV = TextureAddressMode.Clamp,
        };
        var vertexColor0 = VertexColor(0);
        var vertexColor1 = VertexColor(1);
        var mix1 = Lerp(Atlas(Uv0), SubstituteAlpha(Atlas(Uv1), vertexColor0));
        var mix2 = Lerp(mix1, SubstituteAlpha(Atlas(Uv2), vertexColor1));
        var lit = new ComputeBinaryColor(mix2, vertexColor0, BinaryOperator.Multiply);
        var surface = new ComputeBinaryColor(lit, FogFactor(fog, linearSpace), BinaryOperator.Multiply);

        var material = Material.New(device, new MaterialDescriptor
        {
            Attributes =
            {
                Emissive = new MaterialEmissiveMapFeature(surface),
                CullMode = CullMode.Back,
            },
        });
        var pass = material.Passes[0];
        pass.CullMode = CullMode.Back;
        pass.HasTransparency = false;
        depthWrites[pass] = true;
        return material;
    }

    public static Material CreateWater(GraphicsDevice device, Texture water, Texture fog, bool linearSpace, bool additive, Dictionary<MaterialPass, bool> depthWrites)
    {
        IComputeColor color = new ComputeTextureColor(water, Uv0, Vector2.One, Vector2.Zero)
        {
            Filtering = TextureFilter.Anisotropic,
            AddressModeU = TextureAddressMode.Wrap,
            AddressModeV = TextureAddressMode.Wrap,
        };
        var modulated = new ComputeBinaryColor(color, VertexColor(0), BinaryOperator.Multiply);
        var fogged = new ComputeBinaryColor(modulated, FogFactor(fog, linearSpace), BinaryOperator.Multiply);
        // The fog multiplication scales alpha too; keep the modulated alpha so shrouded water stays as opaque as unshrouded water.
        var surface = SubstituteAlpha(fogged, modulated);

        var material = Material.New(device, new MaterialDescriptor
        {
            Attributes =
            {
                Diffuse = new MaterialDiffuseMapFeature(surface),
                Emissive = new MaterialEmissiveMapFeature(surface),
                CullMode = CullMode.None,
            },
        });
        var pass = material.Passes[0];
        pass.CullMode = CullMode.None;
        pass.HasTransparency = true;
        pass.BlendState = additive
            ? new BlendStateDescription(Blend.SourceAlpha, Blend.One)
            : new BlendStateDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha);
        pass.AlphaToCoverage = false;
        depthWrites[pass] = false;
        return material;
    }

    /// <summary>
    /// Fog level (red channel, 0..1) replicated to all channels. The reference multiplies in gamma space; in a linear colour space the
    /// factor is squared so a fogged cell (0.5) ends up at the same display brightness ratio.
    /// </summary>
    private static IComputeColor FogFactor(Texture fog, bool linearSpace)
    {
        var level = new ComputeTextureColor(fog, FogUv, Vector2.One, Vector2.Zero)
        {
            Filtering = TextureFilter.Linear,
            AddressModeU = TextureAddressMode.Clamp,
            AddressModeV = TextureAddressMode.Clamp,
            Swizzle = "rrrr",
        };
        return linearSpace ? new ComputeBinaryColor(level, level, BinaryOperator.Multiply) : level;
    }

    private static ComputeVertexStreamColor VertexColor(int index) => new() { Stream = new ColorVertexStreamDefinition(index) };

    private static IComputeColor SubstituteAlpha(IComputeColor color, IComputeColor alpha)
        => new ComputeBinaryColor(color, alpha, BinaryOperator.SubstituteAlpha);

    private static IComputeColor Lerp(IComputeColor color1, IComputeColor color2) => new ComputeShaderClassColor
    {
        MixinReference = "ComputeColorLerpAlpha",
        CompositionNodes = { ["color1"] = color1, ["color2"] = color2 },
    };
}

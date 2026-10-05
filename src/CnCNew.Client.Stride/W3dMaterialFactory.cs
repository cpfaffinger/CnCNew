using CnCNew.W3D;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace CnCNew.Client.Stride;

internal sealed class W3dMaterialFactory(
    GraphicsDevice graphicsDevice,
    W3dTextureCache textures,
    Dictionary<MaterialPass, bool> depthWrites)
{
    /// <summary>
    /// Brightness factor applied to the surface colour of every material created while it is below one (fogged objects: <c>RTS3DScene</c> draws objects through the shroud
    /// texture, the terrain layer squares the fog level for the linear colour pipeline, so a fog level of one half darkens to a quarter). Set by the caller around <see cref="Create"/>.
    /// </summary>
    public float Dim { get; set; } = 1f;

    /// <summary>A colour factor on the surface of every material created while it is not one (a flashing object, <c>Layers.Objects.FlashTint</c>); multiplies with <see cref="Dim"/>. Set by the caller around <see cref="Create"/>.</summary>
    public System.Numerics.Vector3 Tint { get; set; } = System.Numerics.Vector3.One;

    /// <summary>
    /// Opacity of every material created while it is below one (<c>Drawable::setDrawableOpacity</c>, the building preview of a placement): an opaque pass becomes an alpha blended pass with
    /// this alpha (a cut-out pass keeps its holes, its alpha is scaled), a pass that already blends has its alpha (or, when it adds or modulates, its colour) scaled. Depth writing stays
    /// as the shader has it, so the preview does not show its own far side through its near side. Set by the caller around <see cref="Create"/>.
    /// </summary>
    public float Opacity { get; set; } = 1f;

    public Material Create(W3dMesh mesh, W3dRenderBatch batch)
    {
        if (batch.Passes.Count == 0)
        {
            return Material.New(graphicsDevice, new MaterialDescriptor
            {
                Attributes =
                {
                    Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(new Color4(0.68f, 0.76f, 0.86f, 1))),
                    DiffuseModel = new MaterialDiffuseLambertModelFeature(),
                },
            });
        }

        var material = new Material();
        for (var passIndex = 0; passIndex < batch.Passes.Count; passIndex++)
        {
            var source = batch.Passes[passIndex];
            var compiled = CreateSinglePass(mesh, source, passIndex);
            var pass = compiled.Passes[0];
            compiled.Passes.RemoveAt(0);
            pass.PassIndex = passIndex;
            material.Passes.Add(pass);
        }

        return material;
    }

    private Material CreateSinglePass(W3dMesh mesh, W3dRenderPass source, int passIndex)
    {
        if (source.MaterialPass.TextureStages.Count > 2)
        {
            throw new InvalidDataException(
                $"Mesh '{mesh.Name}' pass {source.MaterialPassIndex} has {source.MaterialPass.TextureStages.Count} stages; W3D supports at most two.");
        }

        var shader = source.Shader;
        var vertexColor = VertexColor(passIndex * 2);
        var hasTexture0 = shader?.TexturingEnabled != false && source.TextureIds.ElementAtOrDefault(0) is uint;
        IComputeColor texture0 = hasTexture0
            ? TextureColor(mesh, source.TextureIds[0]!.Value, passIndex * 2)
            : new ComputeColor(Color4.White);
        var surface = ApplyPrimaryGradient(texture0, vertexColor, shader, hasTexture0);

        if (shader?.SecondaryGradient == W3dSecondaryGradient.Enabled)
        {
            surface = SubstituteAlpha(
                ShaderNode("ComputeColorAdd", surface, VertexColor(passIndex * 2 + 1)),
                surface);
        }

        if (shader?.TexturingEnabled != false && source.TextureIds.ElementAtOrDefault(1) is uint detailId)
        {
            surface = ApplyDetail(
                surface,
                TextureColor(mesh, detailId, passIndex * 2 + 1),
                source.EffectiveDetailColorFunction,
                source.EffectiveDetailAlphaFunction);
        }

        var factor = Tint * Dim;
        if (factor != System.Numerics.Vector3.One)
        {
            surface = SubstituteAlpha(new ComputeBinaryColor(surface, new ComputeColor(new Color4(factor.X, factor.Y, factor.Z, 1f)), BinaryOperator.Multiply), surface);
        }

        var opaque = shader is null || (shader.SourceBlend == W3dSourceBlend.One && shader.DestinationBlend == W3dDestinationBlend.Zero);
        var translucent = Opacity < 1f;
        if (translucent)
        {
            var scale = new ComputeColor(new Color4(Opacity, Opacity, Opacity, Opacity));
            var cutout = shader?.AlphaTestEnabled == true;
            var alphaBlended = shader?.SourceBlend == W3dSourceBlend.SourceAlpha;
            surface = opaque && !cutout ? SubstituteAlpha(surface, scale) // the alpha of an opaque pass is whatever the texture holds; it is the opacity now
                : cutout || alphaBlended ? SubstituteAlpha(surface, new ComputeBinaryColor(surface, scale, BinaryOperator.Multiply)) // alpha only: holes stay holes
                : new ComputeBinaryColor(surface, scale, BinaryOperator.Multiply); // adds or modulates: the colour carries the amount
        }

        var attributes = new MaterialAttributes
        {
            Diffuse = new MaterialDiffuseMapFeature(surface),
            DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            CullMode = mesh.IsTwoSided ? CullMode.None : CullMode.Back,
        };
        if (shader?.AlphaTestEnabled == true)
        {
            attributes.Transparency = new MaterialTransparencyCutoffFeature { Alpha = new ComputeFloat(96f / 255f) };
        }

        var material = Material.New(graphicsDevice, new MaterialDescriptor { Attributes = attributes });
        var pass = material.Passes[0];
        if (shader is null)
        {
            if (translucent) BlendByAlpha(pass);
            return material;
        }

        pass.DepthFunction = MapDepthCompare(shader.DepthCompare);
        pass.CullMode = mesh.IsTwoSided ? CullMode.None : CullMode.Back;
        pass.HasTransparency = !opaque;
        if (!opaque)
        {
            pass.BlendState = new BlendStateDescription(
                MapSourceBlend(shader.SourceBlend),
                MapDestinationBlend(shader.DestinationBlend));
            pass.AlphaToCoverage = false;
        }
        else if (translucent) BlendByAlpha(pass);

        depthWrites[pass] = shader.DepthWriteEnabled;
        return material;
    }

    private static void BlendByAlpha(MaterialPass pass)
    {
        pass.HasTransparency = true;
        pass.BlendState = new BlendStateDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha);
        pass.AlphaToCoverage = false;
    }

    private IComputeColor TextureColor(W3dMesh mesh, uint textureId, int coordinateIndex)
    {
        if (textureId >= mesh.Materials.Textures.Count)
        {
            throw new InvalidDataException($"Mesh '{mesh.Name}' references missing texture table entry {textureId}.");
        }

        return new ComputeTextureColor(
            textures.Resolve(mesh.Materials.Textures[checked((int)textureId)]),
            (TextureCoordinate)coordinateIndex,
            Vector2.One,
            Vector2.Zero)
        {
            Filtering = TextureFilter.Linear,
            AddressModeU = TextureAddressMode.Wrap,
            AddressModeV = TextureAddressMode.Wrap,
        };
    }

    private static ComputeVertexStreamColor VertexColor(int index)
        => new() { Stream = new ColorVertexStreamDefinition(index) };

    private static IComputeColor ApplyPrimaryGradient(
        IComputeColor texture,
        IComputeColor vertexColor,
        W3dShader? shader,
        bool hasTexture)
    {
        if (!hasTexture)
        {
            return shader?.PrimaryGradient == W3dPrimaryGradient.Disabled
                ? new ComputeColor(Color4.White)
                : vertexColor;
        }

        return shader?.PrimaryGradient switch
        {
            W3dPrimaryGradient.Disabled => texture,
            W3dPrimaryGradient.Add => SubstituteAlpha(ShaderNode("ComputeColorAdd", texture, vertexColor), vertexColor),
            W3dPrimaryGradient.Modulate2X => SubstituteAlpha(
                new ComputeBinaryColor(
                    new ComputeBinaryColor(texture, vertexColor, BinaryOperator.Multiply),
                    new ComputeColor(new Color4(2, 2, 2, 1)),
                    BinaryOperator.Multiply),
                new ComputeBinaryColor(texture, vertexColor, BinaryOperator.Multiply)),
            W3dPrimaryGradient.BumpEnvironmentMap or W3dPrimaryGradient.BumpEnvironmentMapLuminance
                => throw new InvalidDataException(
                    $"Primary gradient '{shader.PrimaryGradient}' requires a tangent-space bump path; it is not present in the audited shipped static meshes."),
            _ => new ComputeBinaryColor(texture, vertexColor, BinaryOperator.Multiply),
        };
    }

    private static IComputeColor ApplyDetail(
        IComputeColor current,
        IComputeColor detail,
        W3dDetailColorFunction colorFunction,
        W3dDetailAlphaFunction alphaFunction)
    {
        var color = ApplyDetailColor(current, detail, colorFunction);
        var alpha = ApplyDetailAlpha(current, detail, alphaFunction);
        return SubstituteAlpha(color, alpha);
    }

    private static IComputeColor ApplyDetailColor(
        IComputeColor current,
        IComputeColor detail,
        W3dDetailColorFunction function)
    {
        var one = new ComputeColor(Color4.White);
        return function switch
        {
            W3dDetailColorFunction.Disabled => current,
            W3dDetailColorFunction.Detail => detail,
            W3dDetailColorFunction.Scale => new ComputeBinaryColor(current, detail, BinaryOperator.Multiply),
            W3dDetailColorFunction.InverseScale => new ComputeBinaryColor(
                one,
                new ComputeBinaryColor(
                    new ComputeBinaryColor(one, detail, BinaryOperator.Subtract),
                    new ComputeBinaryColor(one, current, BinaryOperator.Subtract),
                    BinaryOperator.Multiply),
                BinaryOperator.Subtract),
            W3dDetailColorFunction.Add => ShaderNode("ComputeColorAdd", current, detail),
            W3dDetailColorFunction.Subtract => new ComputeBinaryColor(current, detail, BinaryOperator.Subtract),
            W3dDetailColorFunction.ReverseSubtract => new ComputeBinaryColor(detail, current, BinaryOperator.Subtract),
            W3dDetailColorFunction.Blend => ShaderNode("ComputeColorLerpAlpha", detail, current),
            W3dDetailColorFunction.DetailBlend => ShaderNode("ComputeColorLerpAlpha", detail, SubstituteAlpha(current, detail)),
            W3dDetailColorFunction.AddSigned => new ComputeBinaryColor(
                ShaderNode("ComputeColorAdd", current, detail), new ComputeColor(new Color4(0.5f)), BinaryOperator.Subtract),
            W3dDetailColorFunction.AddSigned2X => new ComputeBinaryColor(
                new ComputeBinaryColor(
                    ShaderNode("ComputeColorAdd", current, detail), new ComputeColor(new Color4(0.5f)), BinaryOperator.Subtract),
                new ComputeColor(new Color4(2)), BinaryOperator.Multiply),
            W3dDetailColorFunction.Scale2X => new ComputeBinaryColor(
                new ComputeBinaryColor(current, detail, BinaryOperator.Multiply),
                new ComputeColor(new Color4(2)), BinaryOperator.Multiply),
            W3dDetailColorFunction.ModulateAlphaAddColor => ShaderNode("ComputeColorAdd", current,
                new ComputeBinaryColor(detail, SubstituteAlpha(new ComputeColor(Color4.White), current), BinaryOperator.Multiply)),
            _ => throw new InvalidDataException($"Unknown detail color function {(byte)function}."),
        };
    }

    private static IComputeColor ApplyDetailAlpha(
        IComputeColor current,
        IComputeColor detail,
        W3dDetailAlphaFunction function)
        => function switch
        {
            W3dDetailAlphaFunction.Disabled => current,
            W3dDetailAlphaFunction.Detail => detail,
            W3dDetailAlphaFunction.Scale => new ComputeBinaryColor(current, detail, BinaryOperator.Multiply),
            W3dDetailAlphaFunction.InverseScale => new ComputeBinaryColor(
                new ComputeColor(Color4.White),
                new ComputeBinaryColor(
                    new ComputeBinaryColor(new ComputeColor(Color4.White), detail, BinaryOperator.Subtract),
                    new ComputeBinaryColor(new ComputeColor(Color4.White), current, BinaryOperator.Subtract),
                    BinaryOperator.Multiply),
                BinaryOperator.Subtract),
            _ => throw new InvalidDataException($"Unknown detail alpha function {(byte)function}."),
        };

    private static IComputeColor SubstituteAlpha(IComputeColor color, IComputeColor alpha)
        => new ComputeBinaryColor(color, alpha, BinaryOperator.SubstituteAlpha);

    private static IComputeColor ShaderNode(string mixin, IComputeColor color1, IComputeColor color2)
        => new ComputeShaderClassColor
        {
            MixinReference = mixin,
            CompositionNodes = { ["color1"] = color1, ["color2"] = color2 },
        };

    private static CompareFunction MapDepthCompare(W3dDepthCompare value) => value switch
    {
        W3dDepthCompare.Never => CompareFunction.Never,
        W3dDepthCompare.Less => CompareFunction.Less,
        W3dDepthCompare.Equal => CompareFunction.Equal,
        W3dDepthCompare.LessOrEqual => CompareFunction.LessEqual,
        W3dDepthCompare.Greater => CompareFunction.Greater,
        W3dDepthCompare.NotEqual => CompareFunction.NotEqual,
        W3dDepthCompare.GreaterOrEqual => CompareFunction.GreaterEqual,
        W3dDepthCompare.Always => CompareFunction.Always,
        _ => throw new InvalidDataException($"Unknown depth compare {(byte)value}."),
    };

    private static Blend MapSourceBlend(W3dSourceBlend value) => value switch
    {
        W3dSourceBlend.Zero => Blend.Zero,
        W3dSourceBlend.One => Blend.One,
        W3dSourceBlend.SourceAlpha => Blend.SourceAlpha,
        // The Generals Direct3D runtime intentionally maps this legacy value to DESTCOLOR.
        W3dSourceBlend.OneMinusSourceAlpha => Blend.DestinationColor,
        _ => throw new InvalidDataException($"Unknown source blend {(byte)value}."),
    };

    private static Blend MapDestinationBlend(W3dDestinationBlend value) => value switch
    {
        W3dDestinationBlend.Zero => Blend.Zero,
        W3dDestinationBlend.One => Blend.One,
        W3dDestinationBlend.SourceColor or W3dDestinationBlend.SourceColorPreFog => Blend.SourceColor,
        W3dDestinationBlend.OneMinusSourceColor => Blend.InverseSourceColor,
        W3dDestinationBlend.SourceAlpha => Blend.SourceAlpha,
        W3dDestinationBlend.OneMinusSourceAlpha => Blend.InverseSourceAlpha,
        _ => throw new InvalidDataException($"Unknown destination blend {(byte)value}."),
    };
}

using System.Text;
using Stride.Core.Storage;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Shaders;
using Stride.Shaders.Compiler;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// A texture stage with a W3D environment mapper (<c>mapper.cpp</c>), whose texture coordinates the original's fixed-function pipeline makes per vertex from the camera: the classic mapper takes the
/// camera-space normal (<c>D3DTSS_TCI_CAMERASPACENORMAL</c>) scaled by one half and moved by one half (<c>ClassicEnvironmentMapperClass::Apply</c>), the environment mapper the camera-space
/// reflection of the line from the eye to the vertex (<c>D3DTSS_TCI_CAMERASPACEREFLECTIONVECTOR</c>) scaled by a quarter and moved by one half (<c>EnvironmentMapperClass::Apply</c>); only x and
/// y count. The coordinates therefore follow the camera and the object's own turn, which coordinates baked into the shared vertices of a model cannot: the shader computes them for every pixel
/// from the view-space normal and position. The shaders are classes of this project's own handed over as source (see <see cref="DisplayValueOutput"/> for why they are registered once per process);
/// while they are not known, the stage samples the coordinates the vertices carry, as before.
/// </summary>
internal sealed class EnvironmentMapColor(Texture texture, TextureCoordinate fallbackCoordinates, bool reflection) : ComputeTextureColor(texture, fallbackCoordinates, global::Stride.Core.Mathematics.Vector2.One, global::Stride.Core.Mathematics.Vector2.Zero)
{
    private const string ClassicName = "CnCNewW3dClassicEnvironmentMap";
    private const string ReflectionName = "CnCNewW3dReflectionEnvironmentMap";

    // The generic parameters are those of Stride's ComputeColorTextureScaledOffsetDynamicSampler, so the texture and sampler keys the base class makes fit; the stream, scale and offset are unused.
    private const string Header = "<LinkType TTextureName, Semantic TStream, LinkType TSampler, MemberName TRgba, LinkType TScale, LinkType TOffset> : ComputeColor, DynamicTexture<TTextureName, PerMaterial>, DynamicSampler<TSampler, PerMaterial>, PositionStream4, NormalStream, Transformation";

    private const string ClassicSource = "shader " + ClassicName + Header + """

        {
            override float4 Compute()
            {
                float3 normalVS = normalize(mul(streams.normalWS, (float3x3)View));
                return Texture.Sample(Sampler, normalVS.xy * 0.5f + 0.5f).TRgba;
            }
        };
        """;

    private const string ReflectionSource = "shader " + ReflectionName + Header + """

        {
            override float4 Compute()
            {
                float3 normalVS = normalize(mul(streams.normalWS, (float3x3)View));
                float3 toVertex = normalize(mul(streams.PositionWS, View).xyz);
                float3 reflected = toVertex - 2.0f * dot(normalVS, toVertex) * normalVS;
                return Texture.Sample(Sampler, reflected.xy * 0.25f + 0.5f).TRgba;
            }
        };
        """;

    private static readonly object Gate = new();

    /// <summary>Whether both shaders are known to this process's compiler.</summary>
    public static bool Available { get; private set; }

    /// <summary>What became of the registration, for the report.</summary>
    public static string State { get; private set; } = "not registered";

    public override ShaderSource GenerateShaderSource(ShaderGeneratorContext context, MaterialComputeColorKeys baseKeys)
    {
        var source = base.GenerateShaderSource(context, baseKeys);
        if (!Available || source is not ShaderClassSource { ClassName: "ComputeColorTextureScaledOffsetDynamicSampler" } sampled) return source;
        return reflection
            ? new ShaderClassString(ReflectionName, ReflectionSource, sampled.GenericArguments)
            : new ShaderClassString(ClassicName, ClassicSource, sampled.GenericArguments);
    }

    /// <summary>Makes both classes known to the compiler, as <see cref="DisplayValueOutput.Register"/> does for its class.</summary>
    public static void Register(EffectSystem effects, GraphicsDevice device)
    {
        lock (Gate)
        {
            if (Available) return;
            try
            {
                var mixin = new ShaderMixinSource { Name = "CnCNewEnvironmentMapRegistration" };
                mixin.Mixins.Add(new ShaderClassString(ClassicName, ClassicSource));
                mixin.Mixins.Add(new ShaderClassString(ReflectionName, ReflectionSource));
                mixin.Mixins.Add(new ShaderClassString("CnCNewEnvironmentMapRegistration", "shader CnCNewEnvironmentMapRegistration { registration only, not a shader }"));
                mixin.Macros.Add(new ShaderMacro("CNCNEW_REGISTRATION", Guid.NewGuid().ToString("N")));
                var parameters = new CompilerParameters();
                parameters.EffectParameters.Platform = GraphicsDevice.Platform;
                parameters.EffectParameters.Profile = device.Features.RequestedProfile;
                var compiled = effects.Compiler.Compile(mixin, parameters);
                if (compiled.Bytecode.Task is { } pending) pending.Wait();
                Available = effects.Compiler is EffectCompilerBase compiler
                    && compiler.GetShaderSourceHash(ClassicName) == ObjectId.FromBytes(Encoding.UTF8.GetBytes(ClassicSource))
                    && compiler.GetShaderSourceHash(ReflectionName) == ObjectId.FromBytes(Encoding.UTF8.GetBytes(ReflectionSource));
                State = Available ? "registered" : "the compiler does not know the classes";
            }
            catch (Exception problem) when (problem is not OutOfMemoryException) // Without them the stage samples the baked coordinates, as before; a missing shader must not end the game.
            {
                State = $"failed: {problem.Message.ReplaceLineEndings(" ")}";
            }
        }
    }
}

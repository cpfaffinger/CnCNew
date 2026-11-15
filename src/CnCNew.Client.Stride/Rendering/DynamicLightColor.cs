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
/// The light the pulses (<c>W3DDynamicLight</c>) give an object, as the emissive term of its materials: for each pulse among the object's four lights
/// (<see cref="CnCNew.Presentation.Fx.DynamicLightEnvironment"/>) the Direct3D point light of <c>DX8Wrapper::Set_Light_Environment</c> - its colour as ambient and as diffuse light,
/// attenuated per pixel by <c>1 / (1 + a1 d + a2 d²)</c> up to its range -, plus what the pulses add to the object's ambient at its centre, less a global light a pulse pushed out of
/// the four. It is added to the lit surface, as Stride's own lights are: a direct light gives the surface times its colour times N·L, an ambient light the surface times its colour.
/// The values are per draw (<c>cbuffer PerDraw.CnCNewDynamicLights</c>), written by <see cref="DynamicLightRenderFeature"/>; they are <c>stage</c> members, so their keys keep their plain names
/// wherever the material composes the class.
/// <para/>
/// The shader is a class of this project's own handed over as source, registered once per process as <see cref="DisplayValueOutput"/> is; while it is not known, no material gets it
/// and the pulses light only the ground, as before.
/// </summary>
internal sealed class DynamicLightColor : ComputeNode, IComputeColor
{
    public const string ClassName = "CnCNewDynamicLight";

    /// <summary>The members of the per-draw buffer: the ambient the pulses add (xyz) and the count of point lights (w); the point lights' position and linear factor, their colour and range; the global lights pushed out.</summary>
    public const string AmbientMember = ClassName + ".DynamicLightAmbient";
    public const string PositionsMember = ClassName + ".DynamicLightPositions";
    public const string ColorsMember = ClassName + ".DynamicLightColors";
    public const string DroppedColorsMember = ClassName + ".DynamicLightDroppedColors";
    public const string DroppedDirectionsMember = ClassName + ".DynamicLightDroppedDirections";

    public const int PointLights = CnCNew.Presentation.Fx.DynamicLightEnvironment.MaxLights;
    public const int DroppedLights = 3;

    private const string Source = """
        shader CnCNewDynamicLight : ComputeColor, MaterialPixelStream, NormalStream, PositionStream4
        {
            cbuffer PerDraw.CnCNewDynamicLights
            {
                stage float4 DynamicLightAmbient;
                stage float4 DynamicLightPositions[4];
                stage float4 DynamicLightColors[4];
                stage float4 DynamicLightDroppedColors[3];
                stage float4 DynamicLightDroppedDirections[3];
            };

            override float4 Compute()
            {
                float3 light = DynamicLightAmbient.xyz;
                int count = (int)DynamicLightAmbient.w;
                float3 normal = normalize(streams.normalWS);
                for (int index = 0; index < 4; index++)
                {
                    if (index < count)
                    {
                        float3 toLight = DynamicLightPositions[index].xyz - streams.PositionWS.xyz;
                        float distance = length(toLight);
                        float range = DynamicLightColors[index].w;
                        if (distance < range)
                        {
                            float attenuation = 1.0f / (1.0f + DynamicLightPositions[index].w * distance + 8.0f / (range * range) * distance * distance);
                            float lambert = max(dot(normal, toLight / max(distance, 0.0001f)), 0.0f);
                            light += DynamicLightColors[index].rgb * attenuation * (1.0f + lambert);
                        }
                    }
                }

                for (int dropped = 0; dropped < 3; dropped++)
                {
                    light -= DynamicLightDroppedColors[dropped].rgb * max(dot(normal, DynamicLightDroppedDirections[dropped].xyz), 0.0001f);
                }

                return float4(streams.matDiffuse.rgb * light, 0.0f);
            }
        };
        """;

    private static readonly object Gate = new();

    /// <summary>Whether the shader is known to this process's compiler.</summary>
    public static bool Available { get; private set; }

    /// <summary>What became of the registration, for the report.</summary>
    public static string State { get; private set; } = "not registered";

    public bool HasChanged => false;

    public override ShaderSource GenerateShaderSource(ShaderGeneratorContext context, MaterialComputeColorKeys baseKeys) => new ShaderClassString(ClassName, Source);

    /// <summary>Makes the class known to the compiler, as <see cref="DisplayValueOutput.Register"/> does for its class; <c>CNCNEW_DYNAMIC_LIGHTS=0</c> leaves it out, for a comparison of pictures.</summary>
    public static void Register(EffectSystem effects, GraphicsDevice device)
    {
        lock (Gate)
        {
            if (Available) return;
            if (Environment.GetEnvironmentVariable("CNCNEW_DYNAMIC_LIGHTS") == "0")
            {
                State = "off (CNCNEW_DYNAMIC_LIGHTS=0)";
                return;
            }

            try
            {
                var mixin = new ShaderMixinSource { Name = "CnCNewDynamicLightRegistration" };
                mixin.Mixins.Add(new ShaderClassString(ClassName, Source));
                mixin.Mixins.Add(new ShaderClassString("CnCNewDynamicLightRegistration", "shader CnCNewDynamicLightRegistration { registration only, not a shader }"));
                mixin.Macros.Add(new ShaderMacro("CNCNEW_REGISTRATION", Guid.NewGuid().ToString("N")));
                var parameters = new CompilerParameters();
                parameters.EffectParameters.Platform = GraphicsDevice.Platform;
                parameters.EffectParameters.Profile = device.Features.RequestedProfile;
                var compiled = effects.Compiler.Compile(mixin, parameters);
                if (compiled.Bytecode.Task is { } pending) pending.Wait();
                Available = effects.Compiler is EffectCompilerBase compiler && compiler.GetShaderSourceHash(ClassName) == ObjectId.FromBytes(Encoding.UTF8.GetBytes(Source));
                State = Available ? "registered" : "the compiler does not know the class";
            }
            catch (Exception problem) when (problem is not OutOfMemoryException) // Without it the pulses light the ground only, as before; a missing shader must not end the game.
            {
                State = $"failed: {problem.Message.ReplaceLineEndings(" ")}";
            }
        }
    }
}

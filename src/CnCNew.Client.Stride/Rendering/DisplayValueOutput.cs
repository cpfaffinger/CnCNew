using System.Text;
using Stride.Core.Storage;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Shaders;
using Stride.Shaders.Compiler;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// A material feature that ends the pixel stage by encoding the shaded colour from linear light to display values (the sRGB curve), for a material drawn in the pass that blends in
/// display values (<see cref="DisplayValuePass"/>): the surface keeps the look it had in the linear-light target, and the blend works on the bytes as the original's frame buffer does
/// (<see cref="CnCNew.Presentation.Fx.FrameBufferBlend.ThroughEncodedOutput"/>). It sits in the occlusion slot of <see cref="MaterialAttributes"/>, which no material of this client uses; what it
/// adds runs after the lighting, as Stride's own alpha-blend colour does (<see cref="MaterialGeneratorContext.AddFinalCallback"/>).
/// <para/>
/// The shader is a class of this project's own, handed over as source. Stride checks a compiled effect it finds in its cache against the source of every class it names, by name, and finds a
/// class that has no file only when the compiler has met it as a class of its own: <see cref="Register"/> does that once per process, and every material asks <see cref="Available"/> first.
/// </summary>
internal sealed class DisplayValueOutput : MaterialFeature, IMaterialOcclusionFeature
{
    /// <summary>After every other final step of the pixel stage.</summary>
    private const int Order = MaterialTransparencyBlendFeature.ShadingColorAlphaFinalCallbackOrder + 100;

    private const string ClassName = "CnCNewDisplayValueOutput";

    private const string Source = """
        shader CnCNewDisplayValueOutput : IMaterialSurfacePixel, MaterialPixelShadingStream
        {
            override void Compute()
            {
                float3 light = max(streams.shadingColor, float3(0.0f, 0.0f, 0.0f));
                float3 higher = 1.055f * pow(light, 1.0f / 2.4f) - 0.055f;
                float3 lower = light * 12.92f;
                streams.shadingColor = lerp(higher, lower, step(light, float3(0.0031308f, 0.0031308f, 0.0031308f)));
            }
        };
        """;

    private static readonly object Gate = new();

    /// <summary>Whether the shader is known to this process's compiler; without it the client blends in linear light as before (<see cref="RenderServices.CreateCompositor"/>).</summary>
    public static bool Available { get; private set; }

    /// <summary>What became of the registration, for the report.</summary>
    public static string State { get; private set; } = "not registered";

    /// <summary>
    /// Makes the class known to the compiler: a mixin that names it as a class of its own and fails to compile on purpose (so nothing is stored), with a value that differs in every
    /// process (so the compiler always parses it instead of finding it in its cache, which is the step that registers the class).
    /// </summary>
    public static void Register(EffectSystem effects, GraphicsDevice device)
    {
        lock (Gate)
        {
            if (Available) return;
            if (device.ColorSpace != ColorSpace.Linear)
            {
                State = "not needed: the device blends in display values";
                return;
            }

            try
            {
                var mixin = new ShaderMixinSource { Name = "CnCNewDisplayValueRegistration" };
                mixin.Mixins.Add(new ShaderClassString(ClassName, Source));
                mixin.Mixins.Add(new ShaderClassString("CnCNewDisplayValueRegistration", "shader CnCNewDisplayValueRegistration { registration only, not a shader }"));
                mixin.Macros.Add(new ShaderMacro("CNCNEW_REGISTRATION", Guid.NewGuid().ToString("N")));
                var parameters = new CompilerParameters();
                parameters.EffectParameters.Platform = GraphicsDevice.Platform;
                parameters.EffectParameters.Profile = device.Features.RequestedProfile;
                var compiled = effects.Compiler.Compile(mixin, parameters);
                if (compiled.Bytecode.Task is { } pending) pending.Wait();
                var expected = ObjectId.FromBytes(Encoding.UTF8.GetBytes(Source));
                Available = effects.Compiler is EffectCompilerBase compiler && compiler.GetShaderSourceHash(ClassName) == expected;
                State = Available ? "registered" : "the compiler does not know the class";
            }
            catch (Exception problem) when (problem is not OutOfMemoryException) // Without it the effects blend in linear light, as before; a missing pass must not end the game.
            {
                State = $"failed: {problem.Message.ReplaceLineEndings(" ")}";
            }
        }
    }

    public override void GenerateShader(MaterialGeneratorContext context)
        => context.AddFinalCallback(MaterialShaderStage.Pixel, (stage, generator) => generator.AddShaderSource(MaterialShaderStage.Pixel, new ShaderClassString(ClassName, Source)), Order);

    /// <summary>The feature for a material of the display-value pass, or none while the pass is not in use.</summary>
    public static DisplayValueOutput? ForBlendedPass() => Available ? new DisplayValueOutput() : null;
}

using CnCNew.Game.Shell;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Shaders;
using Stride.Shaders.Compiler;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// The two screen filters a mission script can switch on (<c>docs/SCRIPT-ENGINE.md</c> §7a), drawn over the finished world before the HUD: <c>CAMERA_BW_MODE_*</c> and <c>CAMERA_MOTION_BLUR_JUMP</c>.
/// The original renders the scene into a texture and draws it back through a pixel shader (<c>ScreenBWFilter</c>, <c>ScreenMotionBlurFilter</c> in <c>W3DShaderManager.cpp</c>). This project has no custom
/// shaders of its own on disk, so both are built from what a sprite batch offers: the back buffer is copied once and drawn back several times.
/// <list type="bullet">
/// <item><description>Grey-scale: the one pixel shader of this project, the source below compiled when it is first needed: <c>monochrome.pso</c> with the constants of <c>ScreenBWFilter::set</c>, the original's luminance
/// 0.30 / 0.59 / 0.11 of the picture as it is displayed, blended by the fade <c>t</c>: <c>(1 - t) * C + t * (L, L, L)</c>. Should the compiler fail, the red channel stands in for the grey (<c>SwizzleMode.RRR1</c>).</description></item>
/// <item><description>Motion blur: the copy zoomed into the centre by <c>sqrt(1 - count / 60 * 0.9)</c>, then up to 30 more layers, each zoomed a further 1 percent and blended at alpha 0x15 (or 2 percent and
/// additive alpha 9 + (count - 30) / 5, plus 60 at the peak, for the saturating variant) - the same layer sums the original draws.</description></item>
/// </list>
/// Both are approximations of the original's passes (different filtering, the colour space of the back buffer), not copies; only a look at the picture can say how close they come.
/// </summary>
internal sealed class ScreenFilters : IDisposable
{
    // streams.Color.a carries the fade: a sprite's colour is the one value a batch hands to its shader for free. The back buffer holds linear light, and the original weighs what is displayed.
    private const string GreyShader = """
        shader CnCNewGreyFilter<bool TSRgb> : SpriteBatchShader<TSRgb>
        {
            stage override float4 Shading()
            {
                float4 color = Texture0.Sample(Sampler, streams.TexCoord);
                float3 displayed = pow(max(color.rgb, 0.0f), 1.0f / 2.2f);
                float grey = pow(dot(displayed, float3(0.30f, 0.59f, 0.11f)), 2.2f);
                return float4(lerp(color.rgb, float3(grey, grey, grey), streams.Color.a), 1.0f);
            }
        };
        """;

    private readonly GraphicsDevice _device;
    private readonly EffectSystem _effects;
    private readonly SpriteBatch _batch;
    private Texture? _copy;
    private EffectInstance? _grey;
    private bool _greyTried;

    public ScreenFilters(GraphicsDevice device, EffectSystem effects)
    {
        _device = device;
        _effects = effects;
        _batch = new SpriteBatch(device);
    }

    /// <summary>What became of the grey-scale's shader, for the report; empty until it was needed.</summary>
    public string GreyShaderState { get; private set; } = string.Empty;

    /// <summary>How many frames the filters changed the picture of (for the report).</summary>
    public long FramesFiltered { get; private set; }

    public void Apply(GraphicsContext context, Texture back, MissionPostEffects effects)
    {
        if (!effects.Any) return;
        EnsureCopy(back);
        var commandList = context.CommandList;
        commandList.Copy(back, _copy);
        commandList.SetRenderTargetAndViewport(null, back);
        var screen = new RectangleF(0, 0, back.Width, back.Height);
        var sampler = _device.SamplerStates.LinearClamp;
        FramesFiltered++;

        if (effects.MotionBlurCount > 0) DrawBlur(context, screen, sampler, effects);
        else DrawGrey(context, screen, sampler, effects.BlackWhite);
    }

    /// <summary>Rings per smudge: a sprite batch has no fan of triangles with a shifted middle, so the fall to the edge is a stack of rectangles.</summary>
    private const int SmudgeLayers = 5;

    /// <summary><c>THE_COLOR</c> of <c>W3DSmudge.cpp</c>.</summary>
    private static readonly Color3 SmudgeTint = new(1f, 0xEE / 255f, 0xDD / 255f);

    /// <summary>
    /// <c>W3DSmudgeManager::render</c>: over the finished world and under the HUD a copy of the picture is drawn back in every smudge, with its middle looking a little aside and its edge
    /// fading out, which is what the heat of a microwave looks like. The original draws a fan of four triangles; here nested rectangles, each shifted a little further.
    /// </summary>
    public void DrawSmudges(GraphicsContext context, Texture back, IReadOnlyList<CnCNew.Client.Stride.Layers.Fx.FxSmudge> smudges, Func<System.Numerics.Vector3, (bool Visible, float X, float Y)> toScreen)
    {
        if (smudges.Count == 0) return;
        EnsureCopy(back);
        var commandList = context.CommandList;
        commandList.Copy(back, _copy!);
        commandList.SetRenderTargetAndViewport(null, back);
        _batch.Begin(context, SpriteSortMode.Deferred, BlendStates.NonPremultiplied, _device.SamplerStates.LinearClamp, DepthStencilStates.None);
        foreach (var smudge in smudges)
        {
            var (visible, x, y) = toScreen(smudge.Position);
            if (!visible) continue;
            var color = new Color4(SmudgeTint.R, SmudgeTint.G, SmudgeTint.B, Math.Clamp(smudge.Opacity, 0f, 1f) / SmudgeLayers);
            for (var layer = 0; layer < SmudgeLayers; layer++)
            {
                var share = 1f - layer / (float)SmudgeLayers;
                var width = smudge.Pixels * share;
                var shift = (1f - share) * smudge.Pixels;
                var target = new RectangleF(x - width / 2f, y - width / 2f, width, width);
                _batch.Draw(_copy, target, new RectangleF(target.X + smudge.Offset.X * shift, target.Y + smudge.Offset.Y * shift, width, width), color, 0f, Vector2.Zero);
            }
        }

        _batch.End();
    }

    private EffectInstance? Grey()
    {
        if (_greyTried) return _grey;
        _greyTried = true;
        if (_device.ColorSpace != ColorSpace.Linear)
        {
            GreyShaderState = "red channel: the device is not in linear colour space";
            return null;
        }
        try
        {
            var parameters = new CompilerParameters();
            parameters.EffectParameters.Platform = GraphicsDevice.Platform;
            parameters.EffectParameters.Profile = _device.Features.RequestedProfile;
            var compiled = _effects.Compiler.Compile(new ShaderClassString("CnCNewGreyFilter", GreyShader, "true"), parameters);
            if (compiled.HasErrors) throw new InvalidOperationException(compiled.ToText());
            var result = compiled.Bytecode.WaitForResult();
            if (result.CompilationLog.HasErrors || result.Bytecode is null) throw new InvalidOperationException(result.CompilationLog.ToText());
            _grey = new EffectInstance(new Effect(_device, result.Bytecode) { Name = "CnCNewGreyFilter" });
            GreyShaderState = "compiled";
        }
        catch (Exception problem) when (problem is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            GreyShaderState = $"red channel: {problem.Message.ReplaceLineEndings(" ")}";
        }
        return _grey;
    }

    private void DrawGrey(GraphicsContext context, RectangleF screen, SamplerState sampler, float amount)
    {
        if (Grey() is { } grey)
        {
            _batch.Begin(context, SpriteSortMode.Deferred, BlendStates.Opaque, sampler, DepthStencilStates.None, null, grey);
            _batch.Draw(_copy, screen, null, new Color4(1f, 1f, 1f, amount), 0f, Vector2.Zero);
            _batch.End();
            return;
        }

        // What a sprite batch can do alone: RRR1 keeps the alpha at one (RRRR squares the red through the additive blend and leaves the picture nearly black).
        var keep = 1f - amount;
        _batch.Begin(context, SpriteSortMode.Deferred, BlendStates.Opaque, sampler, DepthStencilStates.None);
        _batch.Draw(_copy, screen, null, new Color4(keep, keep, keep, 1f), 0f, Vector2.Zero);
        _batch.End();

        _batch.Begin(context, SpriteSortMode.Deferred, BlendStates.Additive, sampler, DepthStencilStates.None);
        _batch.Draw(_copy, screen, null, new Color4(amount, amount, amount, 1f), 0f, Vector2.Zero, SpriteEffects.None, ImageOrientation.AsIs, 0f, default, SwizzleMode.RRR1);
        _batch.End();
    }

    private void EnsureCopy(Texture back)
    {
        if (_copy is not null && _copy.Width == back.Width && _copy.Height == back.Height && _copy.Format == back.Format) return;
        _copy?.Dispose();
        _copy = Texture.New2D(_device, back.Width, back.Height, back.Format, TextureFlags.ShaderResource);
    }

    private void DrawBlur(GraphicsContext context, RectangleF screen, SamplerState sampler, MissionPostEffects effects)
    {
        var width = screen.Width;
        var height = screen.Height;
        RectangleF Zoomed(float factor) => new(width * (1f - factor) / 2f, height * (1f - factor) / 2f, width * factor, height * factor);

        var count = effects.MotionBlurCount;
        var zoom = effects.MotionBlurZoomFactor;
        _batch.Begin(context, SpriteSortMode.Deferred, BlendStates.Opaque, sampler, DepthStencilStates.None);
        _batch.Draw(_copy, screen, Zoomed(zoom), Color4.White, 0f, Vector2.Zero);
        _batch.End();

        var limit = Math.Min(count, 30);
        var additive = effects.MotionBlurAdditive;
        var step = additive ? 0.98f : 0.99f;
        var alpha = additive ? 9 + (count > limit ? (count - limit) / 5 : 0) + (count == MissionPostEffects.MotionBlurMaxCount ? 60 : 0) : 0x15;
        var color = new Color4(1f, 1f, 1f, Math.Min(alpha, 255) / 255f);
        _batch.Begin(context, SpriteSortMode.Deferred, additive ? BlendStates.Additive : BlendStates.NonPremultiplied, sampler, DepthStencilStates.None);
        var layerZoom = zoom;
        for (var layer = 0; layer < limit; layer++)
        {
            layerZoom *= step;
            _batch.Draw(_copy, screen, Zoomed(layerZoom), color, 0f, Vector2.Zero);
        }
        _batch.End();
    }

    public void Dispose()
    {
        _copy?.Dispose();
        _grey?.Effect.Dispose();
        _grey?.Dispose();
        _batch.Dispose();
    }
}

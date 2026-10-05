using CnCNew.Game.Shell;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// The two screen filters a mission script can switch on (<c>docs/SCRIPT-ENGINE.md</c> §7a), drawn over the finished world before the HUD: <c>CAMERA_BW_MODE_*</c> and <c>CAMERA_MOTION_BLUR_JUMP</c>.
/// The original renders the scene into a texture and draws it back through a pixel shader (<c>ScreenBWFilter</c>, <c>ScreenMotionBlurFilter</c> in <c>W3DShaderManager.cpp</c>). This project has no custom
/// shaders, so both are built from what a sprite batch offers: the back buffer is copied once and drawn back several times.
/// <list type="bullet">
/// <item><description>Grey-scale: <c>(1 - t) * C</c> drawn opaque, then <c>t * 0.30 * R</c>, <c>t * 0.59 * G</c> and <c>t * 0.11 * B</c> added with the red, green and blue channel swizzled into all three
/// channels - the luminance weights of the original's pixel shader constant, blended by the fade <c>t</c>.</description></item>
/// <item><description>Motion blur: the copy zoomed into the centre by <c>sqrt(1 - count / 60 * 0.9)</c>, then up to 30 more layers, each zoomed a further 1 percent and blended at alpha 0x15 (or 2 percent and
/// additive alpha 9 + (count - 30) / 5, plus 60 at the peak, for the saturating variant) - the same layer sums the original draws.</description></item>
/// </list>
/// Both are approximations of the original's passes (different filtering, the colour space of the back buffer), not copies; only a look at the picture can say how close they come.
/// </summary>
internal sealed class ScreenFilters : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _batch;
    private Texture? _copy;

    public ScreenFilters(GraphicsDevice device)
    {
        _device = device;
        _batch = new SpriteBatch(device);
    }

    /// <summary>How many frames the filters changed the picture of (for the report).</summary>
    public long FramesFiltered { get; private set; }

    public void Apply(GraphicsContext context, Texture back, MissionPostEffects effects)
    {
        if (!effects.Any) return;
        if (_copy is null || _copy.Width != back.Width || _copy.Height != back.Height || _copy.Format != back.Format)
        {
            _copy?.Dispose();
            _copy = Texture.New2D(_device, back.Width, back.Height, back.Format, TextureFlags.ShaderResource);
        }
        var commandList = context.CommandList;
        commandList.Copy(back, _copy);
        commandList.SetRenderTargetAndViewport(null, back);
        var screen = new RectangleF(0, 0, back.Width, back.Height);
        var sampler = _device.SamplerStates.LinearClamp;
        FramesFiltered++;

        if (effects.MotionBlurCount > 0) DrawBlur(context, screen, sampler, effects);
        else DrawGrey(context, screen, sampler, effects.BlackWhite);
    }

    private void DrawGrey(GraphicsContext context, RectangleF screen, SamplerState sampler, float amount)
    {
        // The sprite batch can copy only the red channel into all three (SwizzleMode.RRRR), not mix the three: the grey is the red channel alone, as a photograph through a red filter (vegetation and water
        // come out darker than with the original's 0.30 / 0.59 / 0.11 luminance). The fade blends the picture and its grey: (1 - t) * C + t * (R, R, R).
        var keep = 1f - amount;
        _batch.Begin(context, SpriteSortMode.Deferred, BlendStates.Opaque, sampler, DepthStencilStates.None);
        _batch.Draw(_copy, screen, null, new Color4(keep, keep, keep, 1f), 0f, Vector2.Zero);
        _batch.End();

        _batch.Begin(context, SpriteSortMode.Deferred, BlendStates.Additive, sampler, DepthStencilStates.None);
        _batch.Draw(_copy, screen, null, new Color4(amount, amount, amount, 1f), 0f, Vector2.Zero, SpriteEffects.None, ImageOrientation.AsIs, 0f, default, SwizzleMode.RRRR);
        _batch.End();
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
        _batch.Dispose();
    }
}

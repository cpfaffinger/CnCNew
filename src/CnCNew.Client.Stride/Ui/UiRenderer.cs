using CnCNew.Client.Stride.Rendering;
using CnCNew.Content;
using CnCNew.Ui.Images;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace CnCNew.Client.Stride.Ui;

/// <summary>
/// Draws <see cref="UiDrawList"/>s (and the same primitives directly, for the HUD) with one Stride <see cref="SpriteBatch"/>:
/// fills, borders, mapped images and text all become textured quads in draw order with straight-alpha blending, so later
/// commands lie on top exactly as in the list. Clips are scissor rectangles; a clip change ends the current batch and begins
/// the next, draws that lie wholly inside their clip keep the running batch. Text comes from <see cref="GdiFontProvider"/>
/// glyphs in an atlas (<see cref="UiGlyphCache"/>), drawn 1:1 at integer positions. Everything that cannot be drawn as asked
/// (unknown image, missing texture page, unresolved label, missing glyph) gets a visible placeholder and is counted in
/// <see cref="Stats"/>; nothing is dropped silently. See <c>docs/CLIENT-UI-RENDERING.md</c>.
/// </summary>
/// <remarks>The current render target must be <c>width x height</c> pixels; <see cref="Begin"/> sets the viewport to that size.</remarks>
internal sealed class UiRenderer : IDisposable
{
    private const int AtlasPageSize = 1024;
    private static readonly WndColor PlaceholderFill = new(255, 0, 255, 96);
    private static readonly WndColor PlaceholderLine = new(255, 0, 255, 255);

    private readonly GraphicsDevice _device;
    private readonly IUiFontSource _fonts;
    private readonly AtlasPageTextures _pageTextures;
    private readonly UiGlyphAtlas _atlas;
    private readonly UiGlyphCache _glyphs;
    private readonly UiGlyphCache? _glyphsLightOnDark;
    private readonly UiImageTextures _images;
    private readonly MappedImageTable _imageTable;
    private readonly SpriteBatch _batch;
    private readonly UiClipStack _clips = new(new UiRect(0, 0, 1, 1));
    private readonly UiRenderCounters _frame = new();
    private readonly UiRenderCounters _total = new();

    private GraphicsContext? _context;
    private Viewport _savedViewport;
    private UiRect _bounds;
    private UiRect _scissor;
    private bool _batchOpen;
    private Texture? _lastTexture;
    private bool _inFrame;

    public UiRenderer(RenderServices services)
        : this(services.GraphicsDevice, services.Content, services.Data.Images, "English")
    {
    }

    public UiRenderer(GraphicsDevice device, ContentFileSystem content, MappedImageTable images, string language, UiFontBackend backend = UiFontBackend.Gdi)
    {
        _device = device;
        _imageTable = images;
        _fonts = backend == UiFontBackend.Gdi ? new GdiFontProvider() : new FreeTypeFontProvider();
        TextMeasure = new UiTextMetrics(_fonts);
        _pageTextures = new AtlasPageTextures(device);
        _atlas = new UiGlyphAtlas(AtlasPageSize, _pageTextures);
        _glyphs = new UiGlyphCache(_atlas);
        // The original blends text in gamma space; a linear-light device needs another alpha for light text (UiAlphaCurves).
        _glyphsLightOnDark = device.ColorSpace == ColorSpace.Linear ? new UiGlyphCache(_atlas, UiAlphaCurves.LightOnDark) : null;
        _images = new UiImageTextures(device, content, images, language);
        _batch = new SpriteBatch(device);
        Images = new MappedImageLookup(images);
        Language = language;
    }

    /// <summary>Called for <see cref="UiCustomDraw"/> commands (minimap, radar, portraits) that the host draws itself.</summary>
    public Action<GraphicsContext, UiCustomDraw>? CustomDraw { get; set; }

    /// <summary>Text measurement on the same faces the renderer draws with; hand it to <c>UiTreeServices</c> so layout and drawing agree.</summary>
    public UiTextMetrics TextMeasure { get; }

    /// <summary>Image sizes for <c>UiTreeServices.Images</c>.</summary>
    public IUiImageLookup Images { get; }

    public string Language { get; }

    /// <summary>The fonts that stood in for unavailable requests (requested to used).</summary>
    public IReadOnlyDictionary<string, string> FontSubstitutions => _fonts.Substitutions;

    public IReadOnlyCollection<string> TextureSizeMismatches => _images.SizeMismatches;

    /// <summary>Totals since construction or <see cref="ResetStats"/>.</summary>
    public UiRenderStats Stats
    {
        get
        {
            var snapshot = new UiRenderCounters();
            snapshot.Add(_total);
            snapshot.Add(_frame);
            return snapshot.Snapshot();
        }
    }

    /// <summary>The most recently completed frame (<see cref="Begin"/> to <see cref="End"/>).</summary>
    public UiRenderStats LastFrame { get; private set; } = UiRenderStats.Empty;

    public void ResetStats()
    {
        _total.Reset();
        _frame.Reset();
    }

    /// <summary>Draws a whole list in one frame.</summary>
    public void Draw(GraphicsContext context, UiDrawList list, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(list);
        Begin(context, width, height);
        foreach (var command in list.Commands) Execute(command);
        End();
    }

    /// <summary>Starts a frame on the current render target, which must be <paramref name="width"/> x <paramref name="height"/>.</summary>
    public void Begin(GraphicsContext context, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_inFrame) throw new InvalidOperationException("UiRenderer.Begin called twice without End.");
        _context = context;
        _inFrame = true;
        _frame.Reset();
        _bounds = new UiRect(0, 0, width, height);
        _scissor = _bounds;
        _clips.Reset(_bounds);
        _pageTextures.CommandList = context.CommandList;
        _atlas.White();
        _savedViewport = context.CommandList.Viewport;
        context.CommandList.SetViewport(new Viewport(0, 0, width, height));
    }

    /// <summary>Flushes the batch and finishes the frame; <see cref="LastFrame"/> is set.</summary>
    public void End()
    {
        if (!_inFrame) throw new InvalidOperationException("UiRenderer.End called without Begin.");
        Flush();
        _frame.UnbalancedClipPops += _clips.UnderflowCount + _clips.Depth;
        var commandList = _context!.CommandList;
        commandList.SetViewport(_savedViewport);
        commandList.SetScissorRectangle(new Rectangle(0, 0, (int)_savedViewport.Width, (int)_savedViewport.Height));
        _pageTextures.CommandList = null;
        LastFrame = _frame.Snapshot();
        _total.Add(_frame);
        _frame.Reset();
        _context = null;
        _inFrame = false;
    }

    public void Execute(UiDrawCommand command)
    {
        RequireFrame();
        _frame.Commands++;
        switch (command)
        {
            case UiFillRect fill: DrawRect(fill.Rect, fill.Color); break;
            case UiBorderRect border: DrawBorder(border.Rect, border.Color); break;
            case UiLineDraw line: DrawLine(line); break;
            case UiImageDraw image: DrawImage(image.Image, image.Destination, image.Tint, image.FlipHorizontal, image.FlipVertical); break;
            case UiTextDraw text: DrawText(text); break;
            case UiPushClip push: PushClip(push.Rect); break;
            case UiPopClip: PopClip(); break;
            case UiCustomDraw custom: DrawCustom(custom); break;
            default: throw new NotSupportedException($"Unknown UI draw command {command.GetType().Name}.");
        }
    }

    public void PushClip(UiRect rect)
    {
        RequireFrame();
        _clips.Push(rect);
    }

    public void PopClip()
    {
        RequireFrame();
        _clips.Pop();
    }

    public void DrawRect(UiRect rect, WndColor color)
    {
        RequireFrame();
        if (!color.IsVisible || rect.IsEmpty) return;
        Solid(rect, color);
    }

    /// <summary>One-pixel outline along the inside of the rectangle (<c>winOpenRect</c>).</summary>
    public void DrawBorder(UiRect rect, WndColor color)
    {
        RequireFrame();
        if (!color.IsVisible || rect.IsEmpty) return;
        if (rect.Width <= 2 || rect.Height <= 2)
        {
            Solid(rect, color);
            return;
        }
        Solid(new UiRect(rect.X, rect.Y, rect.Width, 1), color);
        Solid(new UiRect(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        Solid(new UiRect(rect.X, rect.Y + 1, 1, rect.Height - 2), color);
        Solid(new UiRect(rect.Right - 1, rect.Y + 1, 1, rect.Height - 2), color);
    }

    /// <summary>A line as one rotated quad of the white atlas block, pivoting on its left edge centre; clipped by the scissor of the current clip.</summary>
    public void DrawLine(UiLineDraw line)
    {
        RequireFrame();
        if (!line.Color.IsVisible || line.Thickness <= 0f) return;
        var dx = line.X1 - line.X0;
        var dy = line.Y1 - line.Y0;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.01f) return;
        var clip = _clips.Current;
        if (clip.IsEmpty) return;
        var white = _atlas.White();
        SelectScissor(clip);
        var source = new RectangleF(white.X + 1, white.Y + 1, white.Width - 2, white.Height - 2);
        var texture = _pageTextures.Page(white.Page);
        if (!ReferenceEquals(texture, _lastTexture))
        {
            _lastTexture = texture;
            _frame.DrawCalls++;
        }

        _frame.Sprites++;
        _batch.Draw(texture, new RectangleF(line.X0, line.Y0, length, line.Thickness), source, ToColor(line.Color), MathF.Atan2(dy, dx), new Vector2(0f, source.Height / 2f), SpriteEffects.None, ImageOrientation.AsIs, 0f);
    }

    public void DrawImage(string name, UiRect destination, WndColor tint, bool flipHorizontal = false, bool flipVertical = false)
    {
        RequireFrame();
        if (destination.IsEmpty || !tint.IsVisible) return;
        if (!_imageTable.TryGet(name, out var image))
        {
            _frame.MissingImage(name);
            Placeholder(destination);
            return;
        }
        var sprite = _images.Resolve(image, out var failure);
        if (sprite is null)
        {
            _frame.MissingTexture(failure);
            Placeholder(destination);
            return;
        }
        Queue(sprite.Value.Texture, destination, sprite.Value.Source, tint, Effects(flipHorizontal, flipVertical));
    }

    /// <summary>Draws a host-owned texture (minimap, portrait render target) stretched to <paramref name="destination"/>.</summary>
    public void DrawTexture(Texture texture, UiRect destination, UiRect? source, WndColor tint)
    {
        RequireFrame();
        ArgumentNullException.ThrowIfNull(texture);
        if (destination.IsEmpty || !tint.IsVisible) return;
        Queue(texture, destination, source is { } rect ? new RectangleF(rect.X, rect.Y, rect.Width, rect.Height) : null, tint, SpriteEffects.None);
    }

    public void DrawText(UiTextDraw text)
    {
        RequireFrame();
        if (!text.Color.IsVisible || text.Text.Length == 0) return;
        var unresolved = text.LabelKey is not null && text.Text == text.LabelKey;
        if (unresolved) _frame.MissingLabel(text.LabelKey!);
        var spec = UiFontPolicy.Resolve(text.Font);
        var face = TextMeasure.Face(spec);
        var placements = UiTextRuns.Layout(text.Text, face);
        if (placements.Count == 0) return;
        var (originX, baseline) = UiTextRuns.Origin(text, face.Metrics);
        var width = placements[^1].PenX + face.Advance(placements[^1].Codepoint);
        var lineRect = new UiRect(originX, text.Y, width + 1, face.Metrics.LineHeight + 1);
        var clip = _clips.With(text.Clip);
        if (clip.IsEmpty || !clip.Intersects(lineRect)) return;

        var color = unresolved ? PlaceholderLine : text.Color;
        if (text.ShadowColor.IsVisible && !unresolved) DrawGlyphs(spec, face, placements, originX + 1, baseline + 1, text.ShadowColor, clip, lineRect);
        DrawGlyphs(spec, face, placements, originX, baseline, color, clip, lineRect);
        if (unresolved) DrawClipped(new UiRect(originX, baseline + 2, width, 1), PlaceholderLine, clip);
    }

    private void DrawGlyphs(UiFontSpec spec, IUiFontFace face, List<UiGlyphPlacement> placements, int x, int baseline, WndColor color, UiRect clip, UiRect bounds)
    {
        var tint = ToColor(color);
        var cache = _glyphsLightOnDark is not null && UiAlphaCurves.IsLight(color) ? _glyphsLightOnDark : null;
        SelectScissor(bounds.IsInside(clip) ? _bounds : clip);
        foreach (var placement in placements)
        {
            var glyph = (cache ?? _glyphs).Get(spec, face, placement.Codepoint);
            if (glyph.IsMissing) _frame.MissingGlyph(placement.Codepoint);
            if (glyph.IsSubstituted) _frame.SubstitutedGlyphs++;
            if (glyph.Region is not { } region) continue;
            var destination = new RectangleF(x + placement.PenX + glyph.BearingX, baseline - glyph.BearingY, region.Width, region.Height);
            Submit(_pageTextures.Page(region.Page), destination, new RectangleF(region.X, region.Y, region.Width, region.Height), tint, SpriteEffects.None);
        }
    }

    private void DrawCustom(UiCustomDraw custom)
    {
        _frame.CustomDraws++;
        if (CustomDraw is null) return;
        Flush();
        CustomDraw(_context!, custom);
    }

    private void Placeholder(UiRect destination)
    {
        DrawRect(destination, PlaceholderFill);
        DrawBorder(destination, PlaceholderLine);
    }

    private void Solid(UiRect rect, WndColor color) => DrawClipped(rect, color, _clips.Current);

    private void DrawClipped(UiRect rect, WndColor color, UiRect clip)
    {
        if (clip.IsEmpty || !rect.Intersects(clip)) return;
        // Inner texels of the white block: sampling never leaves the block, whatever the quad size.
        var white = _atlas.White();
        SelectScissor(rect.IsInside(clip) ? _bounds : clip);
        Submit(_pageTextures.Page(white.Page), new RectangleF(rect.X, rect.Y, rect.Width, rect.Height),
            new RectangleF(white.X + 1, white.Y + 1, white.Width - 2, white.Height - 2), ToColor(color), SpriteEffects.None);
    }

    private void Queue(Texture texture, UiRect destination, RectangleF? source, WndColor tint, SpriteEffects effects)
    {
        var clip = _clips.Current;
        if (clip.IsEmpty || !destination.Intersects(clip)) return;
        SelectScissor(destination.IsInside(clip) ? _bounds : clip);
        Submit(texture, new RectangleF(destination.X, destination.Y, destination.Width, destination.Height), source, ToColor(tint), effects);
    }

    private void Submit(Texture texture, RectangleF destination, RectangleF? source, Color4 color, SpriteEffects effects)
    {
        if (!ReferenceEquals(texture, _lastTexture))
        {
            _lastTexture = texture;
            _frame.DrawCalls++;
        }
        _frame.Sprites++;
        _batch.Draw(texture, destination, source, color, 0f, Vector2.Zero, effects, ImageOrientation.AsIs, 0f);
    }

    /// <summary>Makes <paramref name="scissor"/> the scissor of the open batch, ending it and beginning a new one when it differs.</summary>
    private void SelectScissor(UiRect scissor)
    {
        if (_batchOpen && scissor == _scissor) return;
        Flush();
        _scissor = scissor;
        var rasterizer = RasterizerStates.CullNone;
        rasterizer.ScissorTestEnable = true;
        _batch.Begin(_context!, SpriteSortMode.Deferred, BlendStates.NonPremultiplied, _device.SamplerStates.LinearClamp, DepthStencilStates.None, rasterizer, null, 0);
        _context!.CommandList.SetScissorRectangle(new Rectangle(scissor.X, scissor.Y, scissor.Width, scissor.Height));
        _batchOpen = true;
        _lastTexture = null;
        _frame.Batches++;
    }

    private void Flush()
    {
        if (!_batchOpen) return;
        _batch.End();
        _batchOpen = false;
        _lastTexture = null;
    }

    /// <summary>
    /// Straight sRGB-byte values, unconverted: Stride's <see cref="SpriteBatch"/> shader already treats a vertex/tint color
    /// as sRGB and decodes it to linear internally whenever the device runs in Linear color space (<see cref="GraphicsDevice.ColorSpace"/>),
    /// and the final render target (an <c>_SRgb</c> view, the normal Stride swap chain format in Linear mode) re-encodes the
    /// blended result on write. Converting here as well would decode twice. <see cref="UiImageTextures"/> keeps the same
    /// texture formats plain (never <c>_SRgb</c>) for the matching reason: the shader's decode applies to the sample too,
    /// so a second, hardware-side sRGB decode on the texture view would double it. Net effect: sRGB in, sRGB out, as the
    /// original's non-color-managed GDI/D3D9 UI compositing also was — only the intermediate blend math runs in linear
    /// light, which can shift soft edges and translucent overlays by a few 8-bit steps from a naive byte blend.
    /// </summary>
    private static Color4 ToColor(WndColor color) => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private static SpriteEffects Effects(bool horizontal, bool vertical)
        => (horizontal ? SpriteEffects.FlipHorizontally : SpriteEffects.None) | (vertical ? SpriteEffects.FlipVertically : SpriteEffects.None);

    private void RequireFrame()
    {
        if (!_inFrame) throw new InvalidOperationException("UiRenderer drawing call outside Begin/End.");
    }

    public void Dispose()
    {
        _batch.Dispose();
        _images.Dispose();
        _pageTextures.Dispose();
        TextMeasure.Dispose();
        _fonts.Dispose();
    }
}

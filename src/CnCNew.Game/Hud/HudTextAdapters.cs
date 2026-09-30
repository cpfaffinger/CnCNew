using CnCNew.Ui.Images;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;

namespace CnCNew.Game.Hud;

/// <summary>
/// A deterministic, renderer-free text measure for headless tests and as a fallback until the Stride client supplies real glyph
/// metrics: every glyph is <c>Font.Size * 0.6</c> pixels wide (ceiling) and the line height is <c>Font.Size * 1.2</c> (ceiling).
/// <see cref="MatchController"/> accepts an external <see cref="IUiTextMeasure"/> so a renderer can replace this with actual font
/// metrics without changing anything else in the HUD.
/// </summary>
public sealed class MonospaceTextMeasure : IUiTextMeasure
{
    public int MeasureWidth(string text, UiFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (int)Math.Ceiling(text.Length * font.Size * 0.6);
    }

    public int LineHeight(UiFont font) => (int)Math.Ceiling(font.Size * 1.2);
}

/// <summary>Mapped image sizes from the compiled <see cref="MappedImageTable"/> (<see cref="CnCNew.Game.Data.GameData.Images"/>).</summary>
public sealed class MappedImageLookup(MappedImageTable images) : IUiImageLookup
{
    private readonly MappedImageTable _images = images ?? throw new ArgumentNullException(nameof(images));

    public bool TryGetSize(string imageName, out int width, out int height)
    {
        if (_images.TryGet(imageName, out var image))
        {
            width = image.Width;
            height = image.Height;
            return true;
        }
        width = height = 0;
        return false;
    }
}

/// <summary>String-table labels (<c>GUI:Options</c>, command button <c>TextLabel</c>s, window <c>TEXT</c>) via <see cref="LocalizedText"/>.</summary>
public sealed class LocalizedTextSource(LocalizedText text) : IUiTextSource
{
    private readonly LocalizedText _text = text ?? throw new ArgumentNullException(nameof(text));

    public bool TryResolve(string label, out string resolved) => _text.TryGet(label, out resolved!);
}

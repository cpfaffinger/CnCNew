using CnCNew.Client.Stride.Rendering;
using CnCNew.Ui.Runtime;
using Stride.Graphics;

namespace CnCNew.Client.Stride.Ui;

/// <summary>Placeholder until the 2D draw-list renderer (images, rectangles, text) is implemented.</summary>
internal sealed class UiRenderer(RenderServices services) : IDisposable
{
    /// <summary>Called for <see cref="UiCustomDraw"/> commands (minimap, radar, portraits) that the host draws itself.</summary>
    public Action<GraphicsContext, UiCustomDraw>? CustomDraw { get; set; }

    public void Draw(GraphicsContext context, UiDrawList list, int width, int height)
    {
        _ = (services, context, list, width, height);
    }

    public void Dispose()
    {
    }
}

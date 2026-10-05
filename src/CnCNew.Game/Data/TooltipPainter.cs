using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;

namespace CnCNew.Game.Data;

/// <summary>
/// Draws the pointer tooltip of a window tree (<see cref="UiWindowTree.Tooltip"/>) the way the original's mouse does: the style from the
/// <c>Mouse</c> block of <c>Mouse.ini</c>, the font <c>TooltipFontName</c> of <c>Language.ini</c> at the resolution-adjusted size
/// (<see cref="UiScaler.ScaleFontSize"/> scales it like the header template fonts). Shared by the shell and the in-game HUD.
/// </summary>
public sealed class TooltipPainter
{
    private readonly MouseTooltipSettings _style;
    private readonly FontDescription _font;

    public TooltipPainter(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _style = MouseTooltipSettings.FromGraph(graph);
        var language = GlobalLanguageSettings.FromGraph(graph);
        // Mouse::mouseNotifyResolutionChange takes the Language.ini slot whenever its name is not empty, and the slot's default name never is
        // (FontDesc::FontDesc): the font of the Mouse block is dead data in the retail game.
        _font = language.Font("TooltipFontName");
    }

    public MouseTooltipSettings Style => _style;

    public ImmutableArray<UiDrawCommand> Build(UiTooltip tooltip, UiViewport viewport, UiScaleOptions options, IUiTextMeasure measure)
    {
        var size = UiScaler.ScaleFontSize(_font.Size, true, viewport, options, UiScaler.DefaultDisplayWidth, UiScaler.DefaultDisplayHeight);
        return UiTooltipLayout.Build(tooltip, _style, new UiFont(_font.Name, size, _font.Bold), viewport, measure);
    }
}

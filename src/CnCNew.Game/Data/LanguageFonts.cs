using CnCNew.Data.Definitions;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;

namespace CnCNew.Game.Data;

/// <summary>
/// The font slots of <c>Language.ini</c> (<c>CreditsTitleFont</c>, <c>TooltipFontName</c>, <c>MessageFont</c> ...) as <see cref="UiFont"/>s
/// for a screen size. The original builds them with <c>GlobalLanguage::adjustFontSize</c>; <see cref="UiScaler.ScaleFontSize"/> does the same
/// for the layout mode in use, exactly as it does for the header template fonts.
/// </summary>
public sealed class LanguageFonts(GlobalLanguageSettings language)
{
    public static LanguageFonts FromGraph(CompiledDefinitionGraph graph) => new(GlobalLanguageSettings.FromGraph(graph));

    public GlobalLanguageSettings Settings => language;

    public UiFont Resolve(string slot, UiViewport viewport, UiScaleOptions options)
    {
        var font = language.Font(slot);
        return new UiFont(font.Name, UiScaler.ScaleFontSize(font.Size, true, viewport, options, UiScaler.DefaultDisplayWidth, UiScaler.DefaultDisplayHeight), font.Bold);
    }
}

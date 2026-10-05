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

    public UiFont Resolve(string slot, UiViewport viewport, UiScaleOptions options) => Resolve(language.Font(slot), viewport, options);

    /// <summary>A font that is not a <c>Language.ini</c> slot (a script's cinematic text font, the fonts of <c>InGameUI.ini</c>) at the adjusted size of <paramref name="viewport"/>.</summary>
    public UiFont Resolve(FontDescription font, UiViewport viewport, UiScaleOptions options)
    {
        ArgumentNullException.ThrowIfNull(font);
        return new UiFont(font.Name, UiScaler.ScaleFontSize(font.Size, true, viewport, options, UiScaler.DefaultDisplayWidth, UiScaler.DefaultDisplayHeight), font.Bold);
    }
}

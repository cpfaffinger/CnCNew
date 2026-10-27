using CnCNew.Data.Definitions;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;

namespace CnCNew.Game.Data;

/// <summary>The fonts <c>InGameUI</c> draws its own text with.</summary>
public enum InGameUiFontRole
{
    Message,
    MilitaryCaptionTitle,
    MilitaryCaption,
    SuperweaponNormal,
    SuperweaponReady,
    NamedTimerNormal,
    NamedTimerReady,
    DrawableCaption,
}

/// <summary>
/// What the in-game text of a mission or match is drawn from: the typed <c>InGameUI.ini</c> values (<see cref="InGameUiSettings"/>: positions, colors, durations) and the fonts
/// <c>InGameUI::init</c> takes from <c>Language.ini</c> (<see cref="LanguageFonts"/>), at the adjusted size for a screen (<c>GlobalLanguage::adjustFontSize</c>). One instance
/// per loaded game; it holds no per-frame state.
/// </summary>
public sealed class InGameUiStyle(InGameUiSettings settings, LanguageFonts language, CnCNew.Ui.Skins.UiSkinStyle? skin = null)
{
    public static InGameUiStyle FromGraph(CompiledDefinitionGraph graph) => new(InGameUiSettings.FromGraph(graph), LanguageFonts.FromGraph(graph));

    public InGameUiSettings Settings => settings;
    public LanguageFonts Language => language;

    /// <summary>
    /// The style in a skin's type (docs/UI-MODERN.md section 4): the modern skin draws the message list, the superweapon clocks and the other in-game texts in its own family,
    /// each role at the step its original size falls in; the colours stay the original's (house colours, the message colours). The original skin returns this style.
    /// </summary>
    public InGameUiStyle WithSkin(CnCNew.Ui.Skins.UiSkin skin) => skin.IsModern ? new InGameUiStyle(settings, language, skin.Style) : this;

    /// <summary>
    /// <c>InGameUI::init</c> replaces the font of every role by the <c>Language.ini</c> slot of the same name, so the font of a role is that slot's name, point size and weight, scaled
    /// for <paramref name="viewport"/> like every other text.
    /// </summary>
    public UiFont Font(InGameUiFontRole role, UiViewport viewport, UiScaleOptions options)
        => skin is null ? language.Resolve(SlotOf(role), viewport, options)
            : skin.Font(CnCNew.Ui.Skins.UiSkinStyle.StepOf(language.Settings.Font(SlotOf(role)).Size), viewport, options.TextScale);

    /// <summary>The unscaled description of a role's font (the <c>Language.ini</c> slot).</summary>
    public FontDescription Description(InGameUiFontRole role) => language.Settings.Font(SlotOf(role));

    private static string SlotOf(InGameUiFontRole role) => role switch
    {
        InGameUiFontRole.Message => "MessageFont",
        InGameUiFontRole.MilitaryCaptionTitle => "MilitaryCaptionTitleFont",
        InGameUiFontRole.MilitaryCaption => "MilitaryCaptionFont",
        InGameUiFontRole.SuperweaponNormal => "SuperweaponCountdownNormalFont",
        InGameUiFontRole.SuperweaponReady => "SuperweaponCountdownReadyFont",
        InGameUiFontRole.NamedTimerNormal => "NamedTimerCountdownNormalFont",
        InGameUiFontRole.NamedTimerReady => "NamedTimerCountdownReadyFont",
        InGameUiFontRole.DrawableCaption => "DrawableCaptionFont",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown InGameUI font role."),
    };

    /// <summary>The <c>InGameUI.ini</c> key of the font whose <c>Language.ini</c> slot <paramref name="role"/> reads (see <see cref="InGameUiSettings.LanguageSlots"/>).</summary>
    public static string SlotName(InGameUiFontRole role) => SlotOf(role);
}

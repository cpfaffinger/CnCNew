using CnCNew.Game.Data;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>CreditsMenu.wnd</c>. <c>WinTextDraw</c> carries the only non-builtin draw callback (<c>W3DCreditsMenuDraw</c>),
/// expanded by <see cref="ShellCustomDraw.ExpandCredits"/> into the lines <see cref="ShellCredits"/> parsed from
/// <c>Data/INI/Credits.ini</c>, scrolling upward at a fixed rate driven by <see cref="Update"/> (deterministic: it is a
/// function of accumulated simulation time, never wall-clock time). The original lets a mouse click skip the credits;
/// <c>WinTextDraw</c> is a plain <c>USER</c> window, and this runtime only routes messages for gadgets and keys (see
/// <c>docs/UI-WND.md</c>), so a plain window click produces no event here. Escape exits the screen instead; a future
/// revision could add a synthetic "any mouse button on a non-gadget window" event to the tree if that turns out to matter.
/// </summary>
public sealed class CreditsScreen : ShellScreen
{
    private const float PixelsPerSecond = 40f;

    private float _scrollPixels;

    public CreditsScreen(GameShell shell) : base(shell, ShellLayoutNames.CreditsMenu)
    {
        Tree.Register("CreditsMenuInput", OnInput);
    }

    public override void Update(double dtSeconds)
    {
        base.Update(dtSeconds);
        _scrollPixels += (float)(dtSeconds * PixelsPerSecond);
    }

    public override UiDrawList BuildDrawList()
        => ShellCustomDraw.ExpandCredits(Tree.BuildDrawList(), new ShellCreditsContext(Shell.Credits, (int)_scrollPixels, Shell.TextSource, Fonts()), Tree.Services.TextMeasure);

    /// <summary>Credits.cpp: CreditsTitleFont for TITLE, CreditsMinorTitleFont for MINORTITLE, CreditsNormalFont for NORMAL and COLUMN, each at the adjusted size.</summary>
    private CreditsFonts Fonts()
    {
        _languageFonts ??= LanguageFonts.FromGraph(Shell.Data.Graph);
        // The modern skin draws the credits in its own family at the sizes the original gives them (docs/UI-MODERN.md section 4).
        UiFont Font(string slot)
        {
            var font = _languageFonts.Resolve(slot, Shell.Viewport, Shell.ScaleOptions);
            return Shell.Skin.IsModern ? font with { Name = Shell.Skin.Style.FontFamily } : font;
        }
        return new CreditsFonts(Font("CreditsTitleFont"), Font("CreditsMinorTitleFont"), Font("CreditsNormalFont"));
    }

    private LanguageFonts? _languageFonts;

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }
}

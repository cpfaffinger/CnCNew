using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.World;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>
/// The general's window (<c>GeneralsExpPoints.wnd</c>, <c>ControlBar::populatePurchaseScience</c>): the rank, the way to the next one, the points to spend and the sciences of the
/// player's side. The window is not modal, and the star of the control bar blinks while points wait (<c>ControlBar::getStarImage</c>).
/// </summary>
public sealed partial class MatchController
{
    private const string GeneralsRank1 = "GeneralsExpPoints.wnd:ButtonRank1Number";
    private const string GeneralsRank3 = "GeneralsExpPoints.wnd:ButtonRank3Number";
    private const string GeneralsRank8 = "GeneralsExpPoints.wnd:ButtonRank8Number";

    private UiWindow _generalsRoot = null!;
    private bool _starFlash = true;
    private int _lastFlashedAtPointValue = -1;
    private int _starRank;
    private int _starPoints;

    /// <summary>The star blinks on the real clock, so a picture pinned at a moment of a match switches it off.</summary>
    public bool StarBlinks { get; set; } = true;

    /// <summary>What the window shows, or null for a player without a general.</summary>
    public GeneralsView? Generals { get; private set; }

    public bool IsGeneralsWindowOpen => !_generalsRoot.IsHidden;

    private void AddGeneralsWindow()
    {
        // The window keeps the original's art in every skin, as the bar of the powers does: its translucent background and the states of its buttons are pictures (the owner's
        // playtest of 3 November 2026 found it drawn flat and opaque, owned and unaffordable sciences alike).
        var roots = _tree.AddLayout(Skin.Derive(_assets.GeneralsExpPoints, _assets.HeaderTemplates, keepArt: true));
        _generalsRoot = roots.Single(window => window.Name == "GeneralsExpPoints.wnd:GenExpParent");
        _generalsRoot.SetHidden(true);
        _tree.Register("GeneralsExpPointsSystem", OnGeneralsSystem);
    }

    /// <summary>The star of the control bar: opens or closes the general's window.</summary>
    public void ToggleGenerals() => ToggleGeneralsWindow();

    private void ToggleGeneralsWindow()
    {
        if (IsGeneralsWindowOpen) HideGeneralsWindow();
        else ShowGeneralsWindow();
    }

    private void ShowGeneralsWindow()
    {
        if (Generals is null) return;
        _starFlash = false;
        _generalsRoot.SetHidden(false);
    }

    private void HideGeneralsWindow() => _generalsRoot.SetHidden(true);

    private void OnGeneralsSystem(UiWindowTree tree, UiEvent uiEvent)
    {
        if (uiEvent.Kind != UiEventKind.ButtonSelected) return;
        ButtonClicked?.Invoke();
        if (uiEvent.Source == "GeneralsExpPoints.wnd:ButtonExit") HideGeneralsWindow();
        else if (GeneralsButton(uiEvent.Source) is { IsEnabled: true } button) Input.Commands.Activate(Match.World, button.Button, []);
    }

    private CommandBarButton? GeneralsButton(string name)
    {
        if (Generals is not { } view) return null;
        return Slot(GeneralsRank1, view.Rank1) ?? Slot(GeneralsRank3, view.Rank3) ?? Slot(GeneralsRank8, view.Rank8);

        CommandBarButton? Slot(string prefix, ImmutableArray<CommandBarButton?> slots)
            => name.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < slots.Length
                ? slots[index] : null;
    }

    private void UpdateGenerals()
    {
        Generals = Input.CommandBar!.Generals(Match.World, Match.Human);
        if (Generals is not { } view) return;
        if (view.RankLevel != _starRank || view.SciencePoints != _starPoints)
        {
            if (_starRank != 0) _starFlash = true;
            _starRank = view.RankLevel;
            _starPoints = view.SciencePoints;
        }
        if (_lastFlashedAtPointValue > view.SciencePoints || view.SciencePoints <= 0) _starFlash = false;
        else _lastFlashedAtPointValue = view.SciencePoints;
    }

    private void RefreshGeneralsWindow()
    {
        if (_scheme is { } scheme && _modernBar is null)
            _tree.FindByName("ControlBar.wnd:ButtonGeneral")?.SetEnabledImage0(StarBlinks && _starFlash && _clockMs % 1000 >= 500 ? scheme.GeneralImages.Hilited : scheme.GeneralImages.Enabled);
        if (!IsGeneralsWindowOpen || Generals is not { } view) return;
        Slots(GeneralsRank1, view.Rank1);
        Slots(GeneralsRank3, view.Rank3);
        Slots(GeneralsRank8, view.Rank8);
        _tree.FindByName("GeneralsExpPoints.wnd:StaticTextRankPointsAvailable")?.SetText(view.SciencePoints.ToString(CultureInfo.InvariantCulture));
        _tree.FindByName("GeneralsExpPoints.wnd:StaticTextTitle")?.SetText(_assets.Text.Get("SCIENCE:Rank" + view.RankLevel.ToString(CultureInfo.InvariantCulture)));
        _tree.FindByName("GeneralsExpPoints.wnd:ProgressBarExperience")?.SetProgress(view.ProgressPercent);

        void Slots(string prefix, ImmutableArray<CommandBarButton?> slots)
        {
            for (var index = 0; index < slots.Length; index++)
            {
                var window = _tree.FindByName(prefix + index.ToString(CultureInfo.InvariantCulture))!;
                window.SetHidden(slots[index] is null);
                window.SetEnabled(slots[index] is { IsEnabled: true });
            }
        }
    }

    /// <summary>The pictures of the window's buttons: an owned science and one that can be bought in full colour (<c>WIN_STATUS_ALWAYS_COLOR</c>), the rest grey.</summary>
    private void AddGeneralsCameos(ImmutableArray<UiDrawCommand>.Builder commands)
    {
        if (!IsGeneralsWindowOpen || IsMenuOpen || Generals is not { } view) return;
        Cameos(GeneralsRank1, view.Rank1);
        Cameos(GeneralsRank3, view.Rank3);
        Cameos(GeneralsRank8, view.Rank8);

        void Cameos(string prefix, ImmutableArray<CommandBarButton?> slots)
        {
            for (var index = 0; index < slots.Length; index++)
            {
                if (slots[index] is not { } button || button.Button.ButtonImage.Length == 0 || !_assets.Images.TryGet(button.Button.ButtonImage, out _)) continue;
                var rect = _tree.FindByName(prefix + index.ToString(CultureInfo.InvariantCulture))!.Rect;
                commands.Add(new UiImageDraw(button.Button.ButtonImage, rect, WndColor.White, false, false) { Grayscale = !button.IsEnabled && button.Reason != ButtonReason.AlreadyOwned });
            }
        }
    }
}

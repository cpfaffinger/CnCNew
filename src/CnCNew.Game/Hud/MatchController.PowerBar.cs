using System.Collections.Immutable;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>
/// The bar of a player's special powers at the side of the screen (<c>ControlBar::initSpecialPowershortcutBar</c>, <c>populateSpecialPowerShortcut</c>): the <c>GenPowersShortcutBar</c> layout its
/// faction names, one button for every power it has, packed from the bottom, the recharge over the cameo. A press is the shortcut's command button; a power is used from the carrier that is readiest.
/// </summary>
public sealed partial class MatchController
{
    private const int MaxPowerButtons = 11;

    private string _powerLayout = string.Empty;
    private string _powerSet = string.Empty;
    private ImmutableArray<CommandBarButton> _powers = [];

    /// <summary>The buttons of the bar as it stands.</summary>
    public ImmutableArray<CommandBarButton> Powers => _powers;

    private void AddPowerBar()
    {
        if (Match.World.FactionOf(Match.Human) is not { PowerShortcutLayout.Length: > 0 } faction) return;
        var path = $"Window/{faction.PowerShortcutLayout}";
        if (!Match.Data.Content.Exists(path)) return;
        WndLayout layout;
        using (var stream = Match.Data.Content.OpenRead(path)) layout = WndParser.Parse(stream, faction.PowerShortcutLayout);
        _tree.AddLayout(Skin.Derive(layout, _assets.HeaderTemplates, keepArt: true));
        (_powerLayout, _powerSet) = (faction.PowerShortcutLayout, faction.PowerShortcutSet);
    }

    private void RefreshPowerBar()
    {
        if (_powerLayout.Length == 0) return;
        _powers = Input.CommandBar!.PowerShortcuts(Match.World, Match.Human, _powerSet);
        for (var index = 0; index < MaxPowerButtons; index++)
        {
            var shown = index < _powers.Length;
            _tree.FindByName($"{_powerLayout}:ButtonParent{index + 1}")?.SetHidden(!shown);
            if (_tree.FindByName($"{_powerLayout}:ButtonCommand{index + 1}") is not { } window) continue;
            window.SetHidden(!shown);
            if (shown) window.SetEnabled(_powers[index].IsEnabled);
        }
    }

    private void AddPowerCameos(ImmutableArray<UiDrawCommand>.Builder commands, WndColor cooldown)
    {
        for (var index = 0; index < _powers.Length && index < MaxPowerButtons; index++)
        {
            if (_tree.FindByName($"{_powerLayout}:ButtonCommand{index + 1}") is not { IsHidden: false } window) continue;
            var power = _powers[index];
            if (power.Button.ButtonImage.Length > 0 && _assets.Images.TryGet(power.Button.ButtonImage, out _))
                commands.Add(new UiImageDraw(power.Button.ButtonImage, window.Rect, WndColor.White, false, false));
            if (power.Availability == ButtonAvailability.NotReady && power.ReadyFraction is { } fraction)
            {
                var wipe = (int)(window.Rect.Height * (1 - fraction));
                if (wipe > 0) commands.Add(new UiFillRect(new CnCNew.Ui.Layout.UiRect(window.Rect.X, window.Rect.Y, window.Rect.Width, wipe), cooldown));
            }
        }
    }

    /// <summary>A press on button <paramref name="number"/> (from 1) of the bar, as a click on it would be.</summary>
    public bool PressPowerButton(int number) => PressPower($"{_powerLayout}:ButtonCommand{number}");

    /// <summary>A press on a button of the bar: true when it was one.</summary>
    private bool PressPower(string name)
    {
        var prefix = $"{_powerLayout}:ButtonCommand";
        if (_powerLayout.Length == 0 || !name.StartsWith(prefix, StringComparison.Ordinal)) return false;
        if (int.TryParse(name[prefix.Length..], out var number) && number >= 1 && number <= _powers.Length && _powers[number - 1].IsEnabled)
            Input.Commands.Activate(Match.World, _powers[number - 1].Button, []);
        return true;
    }
}

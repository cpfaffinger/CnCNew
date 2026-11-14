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

    /// <summary>The tick each power of the bar last became ready (from recharging), for the modern skin's flash; and what each was in the last refresh.</summary>
    private readonly Dictionary<string, long> _powerReadySince = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ButtonAvailability> _powerWas = new(StringComparer.Ordinal);

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
        // The modern skin draws the bar's trays itself (ModernPowerButton): the layout's tray pictures (SATray, SATraySmall, SNTraySmall, SUTraySmall) are not drawn there.
        if (!Skin.IsModern) return;
        foreach (var window in _tree.AllWindows.Where(window => window.Name.StartsWith(_powerLayout + ":", StringComparison.Ordinal)))
            foreach (var kind in new[] { WndDrawDataKind.Enabled, WndDrawDataKind.Disabled, WndDrawDataKind.Hilite })
                window.SetDrawImage(kind, 0, null);
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
        foreach (var power in _powers)
        {
            var name = power.Button.Name;
            if (_powerWas.TryGetValue(name, out var was) && was == ButtonAvailability.NotReady && power.Availability == ButtonAvailability.Available) _powerReadySince[name] = Match.World.Tick;
            _powerWas[name] = power.Availability;
        }
    }

    /// <summary>The power of the bar's button <paramref name="name"/> (<c>...:ButtonCommandN</c>), or null for any other window.</summary>
    private CommandBarButton? PowerShortcut(string name)
    {
        var prefix = $"{_powerLayout}:ButtonCommand";
        return _powerLayout.Length > 0 && name.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(name.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
            && number >= 1 && number <= _powers.Length ? _powers[number - 1] : null;
    }

    /// <summary>
    /// The modern skin's flash of a power that has just become ready: 60 frames of the original, the outline in the accent every other 10 frames (the period of the original's flashing
    /// command buttons, <c>ControlBar::update</c>). The original's bar does not flash; its meaning - "this can be used now" - is what the clock's end says there.
    /// </summary>
    public static bool PowerReadyFlash(long elapsedTicks, long ticksPerOriginalFrame)
    {
        if (ticksPerOriginalFrame <= 0 || elapsedTicks < 0) return false;
        var frames = elapsedTicks / ticksPerOriginalFrame;
        return frames < 60 && frames / 10 % 2 == 0;
    }

    private void AddPowerCameos(ImmutableArray<UiDrawCommand>.Builder commands, WndColor cooldown)
    {
        for (var index = 0; index < _powers.Length && index < MaxPowerButtons; index++)
        {
            if (_tree.FindByName($"{_powerLayout}:ButtonCommand{index + 1}") is not { IsHidden: false } window) continue;
            var power = _powers[index];
            if (Skin.IsModern)
            {
                ModernPowerButton(commands, _tree.FindByName($"{_powerLayout}:ButtonParent{index + 1}")?.Rect ?? window.Rect, window, power, cooldown);
                continue;
            }
            // updateSpecialPowerShortcut: the buttons use the overlay states - a power that is not ready keeps its colours under the clock, one restricted is grey.
            if (power.Button.ButtonImage.Length > 0 && _assets.Images.TryGet(power.Button.ButtonImage, out _))
                commands.Add(new UiImageDraw(power.Button.ButtonImage, window.Rect, power.Availability == ButtonAvailability.CantAfford ? CantAffordTint : WndColor.White, false, false)
                    { Grayscale = power.Availability == ButtonAvailability.Restricted });
            AddCameoState(commands, window, power, cooldown);
        }
    }

    /// <summary>
    /// One power of the bar in the modern skin: a flat tray of the palette with the power's cameo (the only picture), the recharge's clock over it as in the original, and at its
    /// right edge, where the original's tray has its tab, a status strip - filling from the bottom while the power recharges, the skin's good colour when it is ready, the disabled
    /// colour when it cannot be used. The pointer over it outlines the cameo in the accent, a press in the selected colour; a power that has just become ready flashes.
    /// </summary>
    private void ModernPowerButton(ImmutableArray<UiDrawCommand>.Builder commands, CnCNew.Ui.Layout.UiRect tray, UiWindow window, CommandBarButton power, WndColor cooldown)
    {
        var palette = Skin.Style.Palette;
        var cameoRect = window.Rect;
        commands.Add(new UiFillRect(tray, palette.Panel));
        commands.Add(new UiBorderRect(tray, palette.Border));
        if (power.Button.ButtonImage.Length > 0 && _assets.Images.TryGet(power.Button.ButtonImage, out _))
            commands.Add(new UiImageDraw(power.Button.ButtonImage, cameoRect, power.Availability == ButtonAvailability.CantAfford ? CantAffordTint : WndColor.White, false, false)
                { Grayscale = power.Availability == ButtonAvailability.Restricted });
        if (power.Availability == ButtonAvailability.NotReady && power.ReadyFraction is { } fraction) commands.AddRange(UiClock.Remaining(cameoRect, (int)(fraction * 100), cooldown));
        commands.Add(new UiBorderRect(cameoRect, palette.Border));

        var gap = Math.Max(1, (tray.Right - cameoRect.Right) / 4);
        var strip = CnCNew.Ui.Layout.UiRect.FromEdges(cameoRect.Right + gap, cameoRect.Y, Math.Max(cameoRect.Right + gap + 1, tray.Right - gap), cameoRect.Bottom);
        commands.Add(new UiFillRect(strip, palette.Control));
        var (share, color) = power.Availability switch
        {
            ButtonAvailability.Available or ButtonAvailability.Active => (1f, palette.Good),
            ButtonAvailability.NotReady => (Math.Clamp(power.ReadyFraction ?? 0f, 0f, 1f), palette.Accent),
            _ => (1f, palette.Disabled),
        };
        var filled = (int)(strip.Height * share);
        if (filled > 0) commands.Add(new UiFillRect(new CnCNew.Ui.Layout.UiRect(strip.X, strip.Bottom - filled, strip.Width, filled), color));

        var flash = _powerReadySince.TryGetValue(power.Button.Name, out var since) && PowerReadyFlash(Match.World.Tick - since, Match.World.Time.OriginalFrames(1));
        if (window.IsEnabled && window.IsSelected) Outline(cameoRect, palette.Selected);
        else if (window.IsEnabled && window.IsHilited) Outline(cameoRect, palette.Accent);
        if (flash) Outline(tray, palette.Accent);

        void Outline(CnCNew.Ui.Layout.UiRect rect, WndColor outline)
        {
            commands.Add(new UiBorderRect(rect, outline));
            commands.Add(new UiBorderRect(rect.Inset(1), outline));
        }
    }

    /// <summary>Where button <paramref name="number"/> (from 1) of the bar stands on the screen.</summary>
    public CnCNew.Ui.Layout.UiRect PowerButtonRect(int number) => _tree.FindByName($"{_powerLayout}:ButtonCommand{number}")?.Rect ?? default;

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

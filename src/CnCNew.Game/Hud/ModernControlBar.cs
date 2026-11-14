using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Skins;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>What the selection plate of the modern bar shows: one object with its health, or the kinds of several.</summary>
/// <param name="ActiveKind">The index in <paramref name="Kinds"/> of the kind Tab chose in a mixed selection, or -1.</param>
internal sealed record ModernSelection(string Name, int Count, float Health, ImmutableArray<(string Image, int Count, string Template)> Kinds, int ActiveKind = -1);

/// <summary>What the construction overlay of the modern bar shows for a structure under construction (docs/UI-MODERN.md section 19).</summary>
/// <param name="Cameo">The building's cameo (<c>ButtonImage</c>), or null when it has none.</param>
/// <param name="Text">The original's line under the percent (<c>CONTROLBAR:UnderConstructionDesc</c> with the share).</param>
/// <param name="Share">How far the construction is, 0 to 1.</param>
internal sealed record ModernConstruction(string Name, string? Cameo, string Text, float Share);

/// <summary>What the modern bar's own controls show that their windows do not carry (docs/UI-MODERN.md section 20).</summary>
/// <param name="IdleWorkers">How many of the player's workers stand idle: the count on the idle worker button.</param>
/// <param name="UnderAttack">The status light is lit: the original's under-attack glow is in a bright phase.</param>
internal readonly record struct ModernBarState(int IdleWorkers, bool UnderAttack);

/// <summary>
/// The command bar of the modern skin (docs/UI-MODERN.md section 17): a band across the whole width of the window at every aspect ratio. Nothing of it is stretched art. The radar, the
/// cameos and the portrait keep their proportions at the window's uniform scale; the radar with its buttons stands at the left edge, the portrait and the command buttons at the
/// right edge, and what the width adds beyond 4:3 goes to the selection plate between them, which the skin draws. At 4:3 and narrower the two sides meet and the bar has the original's
/// order. The windows of <c>ControlBar.wnd</c> stay the windows the HUD drives: only their places change, so clicks, tests and scripts find them by their names.
/// </summary>
internal sealed class ModernControlBar(UiWindowTree tree, UiWindow root, UiSkin skin)
{
    /// <summary>The band's top line in the layout's 800 x 600.</summary>
    private const int Top = 438;

    private const string Prefix = "ControlBar.wnd:";

    /// <summary>Windows the bar puts somewhere else than the layout: their places in the layout's units. Left: measured from the left edge; everything else from the right edge.</summary>
    private static readonly ImmutableDictionary<string, (int Left, int Top, int Right, int Bottom)> Moved = new Dictionary<string, (int, int, int, int)>
    {
        ["ButtonOptions"] = (180, 446, 216, 479),
        ["ButtonIdleWorker"] = (180, 483, 216, 516),
        ["ButtonPlaceBeacon"] = (180, 520, 216, 553),
        ["PopupCommunicator"] = (180, 557, 216, 590),
        ["WinUAttack"] = (222, 449, 256, 467),
        ["ButtonGeneral"] = (621, 446, 758, 477),
        ["MoneyDisplay"] = (258, 447, 350, 472),
    }.ToImmutableDictionary();

    /// <summary>The children of the root that stand at the left edge, with everything inside them.</summary>
    private static readonly ImmutableHashSet<string> LeftSide =
        [Prefix + "LeftHUD", Prefix + "ButtonOptions", Prefix + "ButtonIdleWorker", Prefix + "ButtonPlaceBeacon", Prefix + "PopupCommunicator", Prefix + "WinUAttack", Prefix + "MoneyDisplay"];

    /// <summary>
    /// How opaque the band and the wells behind the bar's buttons are, in percent (<c>display.barOpacity</c>, 20 to 100): the world shows through the rest. The buttons, cameos, the radar
    /// and the text are never translucent.
    /// </summary>
    public int OpacityPercent { get; set; } = CnCNew.Ui.Settings.DisplaySettings.DefaultBarOpacityPercent;

    private WndColor Faded(WndColor color) => color with { A = (byte)Math.Clamp((color.A * Math.Clamp(OpacityPercent, 0, 100) + 50) / 100, 0, 255) };

    /// <summary>
    /// Set while the selected structure is under construction: the bar then has no button grid (a structure under construction has no other command than the cancel button) and
    /// the construction overlay stands in its place. The HUD sets it before every frame.
    /// </summary>
    public ModernConstruction? Construction { get; set; }

    private UiViewport _placed;
    private UiRect _band, _radarWell, _rightWell, _grid, _plate, _power, _cameo, _cancel, _column;

    /// <summary>The numbers of the layout at the window's uniform scale.</summary>
    private int Scale(int units)
    {
        var (numerator, denominator) = UiScaler.UniformScale(tree.Viewport, UiScaler.DefaultDisplayWidth, UiScaler.DefaultDisplayHeight);
        return (int)(units * numerator / denominator);
    }

    /// <summary>The right side stands this far closer to the edge than in the layout, where the general's experience bar was: it is part of the general's button here.</summary>
    private const int RightShift = 34;

    private int X(int x, bool left) => left ? Scale(x) : tree.Viewport.Width - Scale(800 - RightShift - x);

    private int Y(int y) => tree.Viewport.Height - Scale(600 - y);

    private UiRect Rect(int l, int t, int r, int b, bool left) => UiRect.FromEdges(X(l, left), Y(t), X(r, left), Y(b));

    /// <summary>Puts the bar's windows at their places for the viewport (once per size).</summary>
    public void Place()
    {
        if (_placed == tree.Viewport) return;
        _placed = tree.Viewport;
        _band = new UiRect(0, Y(Top), tree.Viewport.Width, tree.Viewport.Height - Y(Top));
        root.SetScreenRect(_band);
        foreach (var child in root.Children)
        {
            var left = LeftSide.Contains(child.Name);
            foreach (var window in UiWindowTree.Descendants(child))
            {
                var shortName = window.Name.StartsWith(Prefix, StringComparison.Ordinal) ? window.Name[Prefix.Length..] : window.Name;
                var rect = window.Definition.Rect;
                var (l, t, r, b) = Moved.TryGetValue(shortName, out var moved) ? moved : (rect.Left, rect.Top, rect.Right, rect.Bottom);
                window.SetScreenRect(window == child && rect.Width >= 790 ? _band : Rect(l, t, r, b, left));
            }
        }
        foreach (var name in new[] { "ButtonLarge", "GeneralsExp", "ExpBarForeground" }) tree.FindByName(Prefix + name)?.SetHidden(true);
        _radarWell = Rect(4, 442, 177, 598, true);
        _rightWell = Rect(617, 481, 762, 595, false);
        _grid = Rect(219, 490, 608, 593, false);
        _plate = UiRect.FromEdges(X(222, true), Y(481), _grid.X - Scale(4), Y(595));
        _power = UiRect.FromEdges(X(356, true), Y(455), X(608, false), Y(464));
        tree.FindByName(Prefix + "PowerWindow")?.SetScreenRect(_power);
        PlaceConstruction();
    }

    /// <summary>
    /// The places inside the grid's area for a structure under construction: its cameo at the left, its name, bar and percent in a column beside it, the cancel button (the
    /// original's own window, so that clicks, the tooltip and the tests find it by its name) at the right end.
    /// </summary>
    private void PlaceConstruction()
    {
        var pad = Scale(6);
        var inner = _grid.Inset(pad);
        var cellHeight = Math.Min(inner.Height, Scale(48));
        var top = inner.Y + (inner.Height - cellHeight) / 2;
        _cameo = new UiRect(inner.X, top, cellHeight * 4 / 3, cellHeight);
        var cancelWidth = Scale(56);
        _cancel = new UiRect(inner.Right - cancelWidth, top, cancelWidth, cellHeight);
        _column = UiRect.FromEdges(_cameo.Right + pad, inner.Y, _cancel.X - pad, inner.Bottom);
        tree.FindByName(Prefix + "ButtonCancelConstruction")?.SetScreenRect(_cancel);
        tree.FindByName(Prefix + "UnderConstructionDesc")?.SetScreenRect(_column);
    }

    /// <summary>Whether the window leaves room for the selection plate (at 4:3 it does not).</summary>
    internal bool HasPlate => _plate.Width >= Scale(80);

    /// <summary>The places of the queue-load cells drawn by the last <see cref="Selection"/> (empty when none were).</summary>
    internal ImmutableArray<UiRect> QueueCells { get; private set; } = [];

    /// <summary>The places of the kind cells drawn by the last <see cref="Selection"/> with the template each stands for (empty when none were): what a click on the plate acts on.</summary>
    internal ImmutableArray<(UiRect Rect, string Template)> KindCells { get; private set; } = [];

    /// <summary>The top edge of the band: what the tray of the queue loads stands on when the window has no plate.</summary>
    internal int BandTop => _band.Y;

    /// <summary>The band and its wells, under the bar's windows.</summary>
    public ImmutableArray<UiDrawCommand> Chrome()
    {
        var palette = skin.Style.Palette;
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var well = ModernLayout.Well;
        commands.Add(new UiFillRect(_band, Faded(palette.Canvas)));
        commands.Add(new UiFillRect(new UiRect(0, _band.Y, _band.Width, 1), palette.Border));
        commands.Add(new UiFillRect(new UiRect(0, _band.Y + 1, _band.Width, Math.Max(1, Scale(2))), palette.Panel));
        foreach (var rect in new[] { _radarWell, _rightWell }) AddWell(commands, rect, Faded(well), palette.Border);
        commands.Add(new UiFillRect(tree.FindByName(Prefix + "LeftHUD")!.Rect, new WndColor(0, 0, 0, 255)));
        if (Construction is not null)
        {
            // The construction overlay's plate in the grid's area, with a well behind the cameo and the cancel button.
            AddWell(commands, _grid, Faded(well), palette.Border);
            AddWell(commands, _cameo.Inset(-1), Faded(well), palette.Border);
            AddWell(commands, _cancel.Inset(-1), Faded(well), palette.Border);
        }
        else
            for (var slot = 1; slot <= CnCNew.Presentation.Input.CommandBarModel.GridSlots; slot++)
                if (tree.FindByName($"{Prefix}ButtonCommand{slot:00}") is { } button) AddWell(commands, button.Rect.Inset(-1), Faded(well), palette.Border);
        // The money and the status light share one well: what the player has and whether something of it is under attack.
        if (tree.FindByName(Prefix + "MoneyDisplay") is { IsHidden: false } moneyWindow)
        {
            var money = moneyWindow.Rect;
            var light = tree.FindByName(Prefix + "WinUAttack") is { IsHidden: false } lamp ? lamp.Rect : money;
            AddWell(commands, UiRect.FromEdges(Math.Min(light.X, money.X) - Scale(2), Math.Min(light.Y, money.Y), money.Right, Math.Max(light.Bottom, money.Bottom)), Faded(well), palette.Border);
        }
        if (HasPlate) AddWell(commands, _plate, Faded(well), palette.Border);
        return commands.ToImmutable();
    }

    private static void AddWell(ImmutableArray<UiDrawCommand>.Builder commands, UiRect rect, WndColor fill, WndColor border)
    {
        commands.Add(new UiFillRect(rect, fill));
        commands.Add(new UiBorderRect(rect, border));
    }

    /// <summary>
    /// The construction overlay's contents over its plate: the building's cameo, its name and the construction's bar with the original's line of text under it. The cancel button
    /// is a window of the layout and draws itself. Nothing when no structure is under construction.
    /// </summary>
    public ImmutableArray<UiDrawCommand> ConstructionOverlay(IUiTextMeasure measure, decimal textScale)
    {
        if (Construction is not { } site) return [];
        var palette = skin.Style.Palette;
        var heading = skin.Style.Font(UiTypeStep.Heading, tree.Viewport, textScale);
        var body = skin.Style.Font(UiTypeStep.Body, tree.Viewport, textScale);
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        if (site.Cameo is { Length: > 0 } cameo)
        {
            commands.Add(new UiImageDraw(cameo, _cameo, WndColor.White, false, false));
            commands.Add(new UiBorderRect(_cameo, palette.Border));
        }
        var nameHeight = measure.LineHeight(heading);
        var barHeight = Math.Max(4, Scale(10));
        var textHeight = measure.LineHeight(body);
        var gap = Scale(4);
        var block = nameHeight + gap + barHeight + gap + textHeight;
        var y = _column.Y + Math.Max(0, (_column.Height - block) / 2);
        commands.Add(new UiPushClip(_column));
        commands.Add(new UiTextDraw(site.Name, null, heading, palette.Text, WndColor.Undefined, _column.X, y, UiTextAlignment.Left, _column));
        var bar = new UiRect(_column.X, y + nameHeight + gap, _column.Width, barHeight);
        commands.Add(new UiFillRect(bar, palette.Control));
        commands.Add(new UiFillRect(bar with { Width = (int)(bar.Width * Math.Clamp(site.Share, 0f, 1f)) }, palette.Accent));
        commands.Add(new UiBorderRect(bar, palette.Border));
        commands.Add(new UiTextDraw(site.Text, null, body, palette.Text, WndColor.Undefined, _column.X, bar.Bottom + gap, UiTextAlignment.Left, _column));
        commands.Add(new UiPopClip());
        return commands.ToImmutable();
    }

    /// <summary>The power bar on its track, with the two numbers beside it.</summary>
    public ImmutableArray<UiDrawCommand> Power(int production, int consumption, IUiTextMeasure measure, decimal textScale)
    {
        var palette = skin.Style.Palette;
        var font = skin.Style.Font(UiTypeStep.Caption, tree.Viewport, textScale);
        var text = FormattableString.Invariant($"{consumption} / {production}");
        var room = measure.MeasureWidth("0000 / 0000", font) + Scale(8);
        var track = _power with { Width = Math.Max(1, _power.Width - room) };
        var used = production <= 0 ? (consumption > 0 ? 1f : 0f) : Math.Clamp(consumption / (float)production, 0f, 1f);
        var color = consumption > production ? palette.Bad : palette.Good;
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        commands.Add(new UiFillRect(track, palette.Control));
        commands.Add(new UiFillRect(track with { Width = (int)(track.Width * used) }, color));
        commands.Add(new UiBorderRect(track, palette.Border));
        commands.Add(new UiTextDraw(text, null, font, color, WndColor.Undefined, _power.Right - measure.MeasureWidth(text, font), track.Y + (track.Height - measure.LineHeight(font)) / 2, UiTextAlignment.Left, _band));
        return commands.ToImmutable();
    }

    /// <summary>
    /// The general's button in the skin's own cell: the rank in words, under it the way to the next rank (the experience bar of the original's art). <paramref name="attention"/>: points wait to be spent
    /// (the outline and the rank in the accent); <paramref name="blink"/>: the bright half of the blink the original's star image makes then (<c>ControlBar::getStarImage</c>), a wash of the accent.
    /// </summary>
    public ImmutableArray<UiDrawCommand> General(string rank, int progressPercent, bool attention, IUiTextMeasure measure, decimal textScale, bool blink = false)
    {
        if (tree.FindByName(Prefix + "ButtonGeneral") is not { IsHidden: false } button) return [];
        var palette = skin.Style.Palette;
        var font = skin.Style.Font(UiTypeStep.Body, tree.Viewport, textScale);
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var enabled = button.IsEnabled;
        commands.Add(new UiFillRect(button.Rect, !enabled ? palette.Panel : button.IsSelected ? palette.Selected : button.IsHilited ? palette.Hover : palette.Control));
        if (enabled && attention && blink) commands.Add(new UiFillRect(button.Rect, AccentWash));
        commands.Add(new UiBorderRect(button.Rect, enabled && attention ? palette.Accent : palette.Border));
        var bar = UiRect.FromEdges(button.Rect.X + 1, button.Rect.Bottom - Math.Max(2, Scale(4)), button.Rect.Right - 1, button.Rect.Bottom - 1);
        commands.Add(new UiFillRect(bar with { Width = bar.Width * Math.Clamp(progressPercent, 0, 100) / 100 }, enabled ? palette.Accent : palette.Disabled));
        var color = !enabled ? palette.Disabled : attention && !blink ? palette.Accent : palette.Text;
        commands.Add(new UiTextDraw(rank, null, font, color, WndColor.Undefined, button.Rect.X + (button.Rect.Width - measure.MeasureWidth(rank, font)) / 2,
            button.Rect.Y + (bar.Y - button.Rect.Y - measure.LineHeight(font)) / 2, UiTextAlignment.Left, button.Rect));
        return commands.ToImmutable();
    }

    /// <summary>The accent laid thinly over a control: a flashing button's bright half, the halo of the lit status light.</summary>
    private static readonly WndColor AccentWash = new(242, 179, 61, 64);

    /// <summary>The bar's four small buttons beside the radar, each with the icon the skin draws for it.</summary>
    private static readonly ImmutableArray<(string Window, ModernBarIcon Icon)> SmallButtons =
        [("ButtonOptions", ModernBarIcon.Options), ("ButtonIdleWorker", ModernBarIcon.IdleWorker), ("ButtonPlaceBeacon", ModernBarIcon.Beacon), ("PopupCommunicator", ModernBarIcon.Chat)];

    /// <summary>
    /// The bar's own controls, drawn by the skin and not from the scheme's art (docs/UI-MODERN.md section 20): the four small buttons beside the radar with an icon each, the idle
    /// workers' count on their button, and the status light beside the money, which is lit in the phases the original's under-attack glow is (<c>ControlBar::updateRadarAttackGlow</c>).
    /// Every state of a window keeps its meaning: hilited (the pointer is over it), pressed, disabled (the idle worker button with nobody idle, the beacon button at the limit).
    /// </summary>
    public ImmutableArray<UiDrawCommand> Controls(ModernBarState state, IUiTextMeasure measure, decimal textScale)
    {
        var palette = skin.Style.Palette;
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        foreach (var (name, icon) in SmallButtons)
        {
            if (tree.FindByName(Prefix + name) is not { IsHidden: false } button) continue;
            var rect = button.Rect;
            var enabled = button.IsEnabled;
            var pressed = enabled && button.IsSelected;
            var hover = enabled && button.IsHilited;
            commands.Add(new UiFillRect(rect, !enabled ? palette.Panel : pressed ? palette.Selected : hover ? palette.Hover : palette.Control));
            commands.Add(new UiBorderRect(rect, palette.Border));
            // The hilited or pressed button carries the accent as a bar at its left edge, as every flat button of the skin does.
            if (hover || pressed) commands.Add(new UiFillRect(new UiRect(rect.X, rect.Y, Math.Max(2, Scale(2)), rect.Height), palette.Accent));
            var side = Math.Min(rect.Width, rect.Height) * 0.62f;
            var left = rect.X + (rect.Width - side) / 2f;
            var top = rect.Y + (rect.Height - side) / 2f + (pressed ? 1 : 0);
            ModernBarIcons.Draw(commands, icon, left, top, side, enabled ? palette.Text : palette.Disabled);
            if (icon == ModernBarIcon.IdleWorker && enabled && state.IdleWorkers > 0) AddBadge(commands, rect, state.IdleWorkers, measure, textScale);
        }
        if (tree.FindByName(Prefix + "WinUAttack") is { IsHidden: false } lamp) AddStatusLight(commands, lamp.Rect, state.UnderAttack);
        return commands.ToImmutable();
    }

    /// <summary>How many workers stand idle, on a plate of the accent at the bottom right of the button (the original keeps this count and does not show it: <c>InGameUI::showIdleWorkerLayout</c>).</summary>
    private void AddBadge(ImmutableArray<UiDrawCommand>.Builder commands, UiRect button, int count, IUiTextMeasure measure, decimal textScale)
    {
        var palette = skin.Style.Palette;
        var font = skin.Style.Font(UiTypeStep.Caption, tree.Viewport, textScale);
        var label = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var height = Math.Min(measure.LineHeight(font), button.Height);
        var width = Math.Min(button.Width, Math.Max(height, measure.MeasureWidth(label, font) + Scale(4)));
        var plate = new UiRect(button.Right - width, button.Bottom - height, width, height);
        commands.Add(new UiFillRect(plate, palette.Accent));
        commands.Add(new UiTextDraw(label, null, font, palette.Canvas, WndColor.Undefined, plate.X + (plate.Width - measure.MeasureWidth(label, font)) / 2, plate.Y, UiTextAlignment.Left, plate));
    }

    /// <summary>A round lamp in the middle of the light's window: dark with a ring at rest, the skin's warning colour with a halo while lit.</summary>
    private void AddStatusLight(ImmutableArray<UiDrawCommand>.Builder commands, UiRect window, bool lit)
    {
        var palette = skin.Style.Palette;
        var diameter = Math.Max(4, Math.Min(window.Width, window.Height) * 7 / 10);
        var (cx, cy) = (window.X + window.Width / 2, window.Y + window.Height / 2);
        if (lit)
        {
            var halo = Math.Min(Math.Min(window.Width, window.Height), diameter * 3 / 2);
            ModernBarIcons.Disc(commands, cx, cy, halo, palette.Bad with { A = 90 });
            ModernBarIcons.Disc(commands, cx, cy, diameter, palette.Bad);
        }
        else
        {
            ModernBarIcons.Disc(commands, cx, cy, diameter, palette.Border);
            ModernBarIcons.Disc(commands, cx, cy, Math.Max(2, diameter - 2 * Math.Max(1, Scale(1))), palette.Control);
        }
    }

    /// <summary>The selection plate: what the extra width of the window shows. One object: its name and its health; several: their kinds with a count each.</summary>
    public ImmutableArray<UiDrawCommand> Selection(ModernSelection? selection, IUiTextMeasure measure, decimal textScale, ImmutableArray<CnCNew.Presentation.Input.QueueLoad> queues = default)
    {
        QueueCells = [];
        KindCells = [];
        if (!HasPlate || selection is null) return [];
        var palette = skin.Style.Palette;
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var kindCells = ImmutableArray.CreateBuilder<(UiRect, string)>();
        var inner = _plate.Inset(Scale(6));
        var heading = skin.Style.Font(UiTypeStep.Heading, tree.Viewport, textScale);
        var caption = skin.Style.Font(UiTypeStep.Caption, tree.Viewport, textScale);
        commands.Add(new UiPushClip(_plate));
        commands.Add(new UiTextDraw(selection.Name, null, heading, palette.Text, WndColor.Undefined, inner.X, inner.Y, UiTextAlignment.Left, _plate));
        var y = inner.Y + measure.LineHeight(heading) + Scale(4);
        if (selection.Count == 1)
        {
            var bar = new UiRect(inner.X, y, inner.Width, Math.Max(3, Scale(7)));
            commands.Add(new UiFillRect(bar, palette.Control));
            commands.Add(new UiFillRect(bar with { Width = (int)(bar.Width * Math.Clamp(selection.Health, 0f, 1f)) }, selection.Health > 0.5f ? palette.Good : selection.Health > 0.25f ? palette.Accent : palette.Bad));
            commands.Add(new UiBorderRect(bar, palette.Border));
        }
        else
        {
            // The queue loads of a group of buildings take the row under the kinds; the kinds' cells give up height for it when the plate is low.
            var pad = Math.Max(2, Scale(3));
            var pipHeight = Math.Max(3, Scale(5));
            var rowHeight = QueueLoadStrip.RowHeight(measure.LineHeight(caption), pipHeight, pad);
            var withLoads = !queues.IsDefaultOrEmpty && queues.Length > 1;
            var reserved = withLoads ? rowHeight + Scale(4) : 0;
            var height = Math.Max(1, Math.Min(Scale(40), inner.Bottom - y - reserved));
            if (withLoads && height < Scale(16)) height = Math.Max(1, Math.Min(Scale(16), inner.Bottom - y));
            var width = height * 4 / 3;
            var kindsTop = y;
            var x = inner.X;
            var kindIndex = -1;
            foreach (var (image, count, template) in selection.Kinds)
            {
                kindIndex++;
                if (x + width > inner.Right) break;
                var cell = new UiRect(x, y, width, height);
                commands.Add(new UiImageDraw(image, cell, WndColor.White, false, false));
                commands.Add(new UiBorderRect(cell, palette.Border));
                kindCells.Add((cell, template));
                if (kindIndex == selection.ActiveKind)
                {
                    commands.Add(new UiBorderRect(cell.Inset(-1), palette.Accent));
                    commands.Add(new UiBorderRect(cell.Inset(-2), palette.Accent));
                }
                var label = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var labelWidth = measure.MeasureWidth(label, caption);
                commands.Add(new UiFillRect(new UiRect(cell.Right - labelWidth - Scale(4), cell.Bottom - measure.LineHeight(caption), labelWidth + Scale(4), measure.LineHeight(caption)), palette.Canvas));
                commands.Add(new UiTextDraw(label, null, caption, palette.Text, WndColor.Undefined, cell.Right - labelWidth - Scale(2), cell.Bottom - measure.LineHeight(caption), UiTextAlignment.Left, cell));
                x += width + Scale(4);
            }
            if (withLoads)
            {
                var area = UiRect.FromEdges(inner.X, kindsTop + height + Scale(4), inner.Right, inner.Bottom);
                var layout = QueueLoadStrip.Place(queues, area, false, Math.Max(2, Scale(3)), Math.Max(1, Scale(1)), pipHeight, pad, measure.LineHeight(caption), text => measure.MeasureWidth(text, caption));
                QueueCells = [.. layout.Cells.Select(cell => cell.Rect)];
                commands.AddRange(QueueLoadStrip.Draw(layout, new QueueLoadStrip.Colours(palette.Control, palette.Border, palette.Canvas, palette.Good, palette.Bad, palette.Text, WndColor.Undefined), caption, _plate));
            }
        }
        KindCells = kindCells.ToImmutable();
        commands.Add(new UiPopClip());
        return commands.ToImmutable();
    }
}

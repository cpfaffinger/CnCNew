using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Skins;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>What the selection plate of the modern bar shows: one object with its health, or the kinds of several.</summary>
/// <param name="ActiveKind">The index in <paramref name="Kinds"/> of the kind Tab chose in a mixed selection, or -1.</param>
internal sealed record ModernSelection(string Name, int Count, float Health, ImmutableArray<(string Image, int Count)> Kinds, int ActiveKind = -1);

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

    private UiViewport _placed;
    private UiRect _band, _radarWell, _rightWell, _grid, _plate, _power;

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
    }

    /// <summary>Whether the window leaves room for the selection plate (at 4:3 it does not).</summary>
    internal bool HasPlate => _plate.Width >= Scale(80);

    /// <summary>The places of the queue-load cells drawn by the last <see cref="Selection"/> (empty when none were).</summary>
    internal ImmutableArray<UiRect> QueueCells { get; private set; } = [];

    /// <summary>The top edge of the band: what the tray of the queue loads stands on when the window has no plate.</summary>
    internal int BandTop => _band.Y;

    /// <summary>The band and its wells, under the bar's windows.</summary>
    public ImmutableArray<UiDrawCommand> Chrome()
    {
        var palette = skin.Style.Palette;
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var well = ModernLayout.Well;
        commands.Add(new UiFillRect(_band, palette.Canvas));
        commands.Add(new UiFillRect(new UiRect(0, _band.Y, _band.Width, 1), palette.Border));
        commands.Add(new UiFillRect(new UiRect(0, _band.Y + 1, _band.Width, Math.Max(1, Scale(2))), palette.Panel));
        foreach (var rect in new[] { _radarWell, _rightWell }) AddWell(commands, rect, well, palette.Border);
        commands.Add(new UiFillRect(tree.FindByName(Prefix + "LeftHUD")!.Rect, new WndColor(0, 0, 0, 255)));
        for (var slot = 1; slot <= CnCNew.Presentation.Input.CommandBarModel.GridSlots; slot++)
            if (tree.FindByName($"{Prefix}ButtonCommand{slot:00}") is { } button) AddWell(commands, button.Rect.Inset(-1), well, palette.Border);
        foreach (var name in new[] { "ButtonOptions", "ButtonIdleWorker", "ButtonPlaceBeacon", "PopupCommunicator" })
            if (tree.FindByName(Prefix + name) is { IsHidden: false } button) AddWell(commands, button.Rect, palette.Control, palette.Border);
        if (HasPlate) AddWell(commands, _plate, well, palette.Border);
        return commands.ToImmutable();
    }

    private static void AddWell(ImmutableArray<UiDrawCommand>.Builder commands, UiRect rect, WndColor fill, WndColor border)
    {
        commands.Add(new UiFillRect(rect, fill));
        commands.Add(new UiBorderRect(rect, border));
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
    /// The general's button in the skin's own cell: the rank in words, under it the way to the next rank (the experience bar of the original's art). <paramref name="attention"/>: points wait to be spent.
    /// </summary>
    public ImmutableArray<UiDrawCommand> General(string rank, int progressPercent, bool attention, IUiTextMeasure measure, decimal textScale)
    {
        if (tree.FindByName(Prefix + "ButtonGeneral") is not { IsHidden: false } button) return [];
        var palette = skin.Style.Palette;
        var font = skin.Style.Font(UiTypeStep.Body, tree.Viewport, textScale);
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        commands.Add(new UiFillRect(button.Rect, button.IsHilited ? palette.Hover : palette.Control));
        commands.Add(new UiBorderRect(button.Rect, attention ? palette.Accent : palette.Border));
        var bar = UiRect.FromEdges(button.Rect.X + 1, button.Rect.Bottom - Math.Max(2, Scale(4)), button.Rect.Right - 1, button.Rect.Bottom - 1);
        commands.Add(new UiFillRect(bar with { Width = bar.Width * Math.Clamp(progressPercent, 0, 100) / 100 }, palette.Accent));
        commands.Add(new UiTextDraw(rank, null, font, attention ? palette.Accent : palette.Text, WndColor.Undefined, button.Rect.X + (button.Rect.Width - measure.MeasureWidth(rank, font)) / 2,
            button.Rect.Y + (bar.Y - button.Rect.Y - measure.LineHeight(font)) / 2, UiTextAlignment.Left, button.Rect));
        return commands.ToImmutable();
    }

    /// <summary>The selection plate: what the extra width of the window shows. One object: its name and its health; several: their kinds with a count each.</summary>
    public ImmutableArray<UiDrawCommand> Selection(ModernSelection? selection, IUiTextMeasure measure, decimal textScale, ImmutableArray<CnCNew.Presentation.Input.QueueLoad> queues = default)
    {
        QueueCells = [];
        if (!HasPlate || selection is null) return [];
        var palette = skin.Style.Palette;
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
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
            foreach (var (image, count) in selection.Kinds)
            {
                kindIndex++;
                if (x + width > inner.Right) break;
                var cell = new UiRect(x, y, width, height);
                commands.Add(new UiImageDraw(image, cell, WndColor.White, false, false));
                commands.Add(new UiBorderRect(cell, palette.Border));
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
        commands.Add(new UiPopClip());
        return commands.ToImmutable();
    }
}

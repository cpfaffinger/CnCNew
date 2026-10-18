using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Images;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Spectator;

/// <summary>
/// The observer HUD as draw commands (docs/SPECTATOR-MODE.md §3): a top bar with the players side by side, the detail panel of the chosen tab, the radar with the simulated
/// cameras of the computer players as frames in their colours, a button bar where a player has the command bar, markers of the simulated cameras in the world, and the result.
/// <para>
/// Everything except the cameo pictures is a placeholder: flat rectangles and plain text in one font, no art of the original observer control bar. The cameos are the
/// button images of the original's command buttons (the picture a player clicks to build the object); an object without one is shown by its template name.
/// </para>
/// The layout is a function of the viewport height (720 pixels is scale 1); the commands are a pure function of the controller's state.
/// </summary>
public sealed class SpectatorHud
{
    // The colours of the original skin are the ones this HUD always had; the modern skin takes its palette (docs/UI-MODERN.md section 5).
    private readonly WndColor Plate, PlateStrong, Edge, Text, Dim, Accent, Shadow, BarBack, BarFill, ButtonBack, ButtonOn;
    private readonly CnCNew.Ui.Skins.UiSkin _skin;
    private WndColor Warning => _skin.IsModern ? _skin.Style.Palette.Bad : new WndColor(255, 96, 80, 255);

    private const string FontName = "Arial";
    private const int ListRows = 8;

    private static readonly (SpectatorAction Action, string Label)[] ButtonTable =
    [
        (SpectatorAction.TabProduction, "Production"), (SpectatorAction.TabUnits, "Units"), (SpectatorAction.TabLosses, "Losses"), (SpectatorAction.TabEconomy, "Economy"),
        (SpectatorAction.FollowFirst, "Follow AI 1"), (SpectatorAction.FollowSecond, "Follow AI 2"), (SpectatorAction.FreeCamera, "Free cam"), (SpectatorAction.Overview, "Overview"),
        (SpectatorAction.TabResearch, "Research"),
        (SpectatorAction.CycleFog, "Fog"), (SpectatorAction.Pause, "Pause"), (SpectatorAction.Speed1, "1x"), (SpectatorAction.Speed2, "2x"), (SpectatorAction.Speed4, "4x"), (SpectatorAction.Speed8, "8x"),
        (SpectatorAction.ToggleTicker, "Ticker"),
    ];

    /// <summary>The third row of a replay's button bar: seeking to the checkpoints the playback keeps.</summary>
    private static readonly (SpectatorAction Action, string Label)[] ReplayButtonTable =
    [
        (SpectatorAction.SeekStart, "Start"), (SpectatorAction.SeekBack, "<< Back"), (SpectatorAction.SeekForward, "Next >>"),
    ];

    private readonly LiveMatch _match;
    private readonly MappedImageTable _images;
    private readonly IUiTextMeasure _measure;
    private readonly Dictionary<string, string> _cameos = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(UiRect Rect, SpectatorAction Action)> _buttons = [];
    private readonly List<(UiRect Rect, TickerLine Line)> _tickerLines = [];
    private UiRect _minimap;

    public SpectatorHud(LiveMatch match, MappedImageTable images, IUiTextMeasure measure, CnCNew.Ui.Skins.UiSkin? skin = null)
    {
        _skin = skin ?? CnCNew.Ui.Skins.UiSkin.Original;
        if (_skin.IsModern)
        {
            var p = _skin.Style.Palette;
            (Plate, PlateStrong, Edge, Text, Dim, Accent, Shadow) = (p.Panel with { A = 230 }, p.Panel with { A = 245 }, p.Border, p.Text, p.Muted, p.Accent, p.Shadow);
            (BarBack, BarFill, ButtonBack, ButtonOn) = (p.Control, p.Good, p.Control, p.Selected);
        }
        else
        {
            (Plate, PlateStrong, Edge, Text, Dim, Accent, Shadow) = (new(8, 12, 18, 205), new(8, 12, 18, 235), new(96, 108, 124, 255), new(236, 238, 242, 255), new(160, 168, 180, 255),
                new(255, 208, 84, 255), new(0, 0, 0, 255));
            (BarBack, BarFill, ButtonBack, ButtonOn) = (new(40, 46, 56, 255), new(96, 200, 110, 255), new(30, 36, 46, 235), new(70, 92, 130, 245));
        }
        _match = match;
        _images = images;
        _measure = measure;
        foreach (var button in CommandButtonCatalog.Build(match.Data.Graph).Buttons)
            if (button.Object.Length > 0 && button.ButtonImage.Length > 0 && button.Kind is GuiCommandKind.UnitBuild or GuiCommandKind.DozerConstruct
                && images.TryGet(button.ButtonImage, out _))
                _cameos.TryAdd(button.Object, button.ButtonImage);
    }

    /// <summary>Objects that have a cameo picture.</summary>
    public int CameoCount => _cameos.Count;

    /// <summary>The button under a screen position in the last layout.</summary>
    public SpectatorAction? ActionAt(Vector2 screen)
    {
        foreach (var (rect, action) in _buttons)
            if (rect.Contains((int)screen.X, (int)screen.Y)) return action;
        return null;
    }

    /// <summary>The ticker line under a screen position in the last layout.</summary>
    public TickerLine? TickerLineAt(Vector2 screen)
    {
        foreach (var (rect, line) in _tickerLines)
            if (rect.Contains((int)screen.X, (int)screen.Y)) return line;
        return null;
    }

    /// <summary>The rectangle of the <paramref name="index"/>-th line the ticker shows, for a test or a script that clicks it.</summary>
    public UiRect? TickerLineRect(SpectatorController controller, int index, int width, int height)
    {
        Build(controller, width, height);
        return index >= 0 && index < _tickerLines.Count ? _tickerLines[index].Rect : null;
    }

    public UiRect? ButtonRect(SpectatorController controller, SpectatorAction action, int width, int height)
    {
        Build(controller, width, height);
        foreach (var (rect, candidate) in _buttons)
            if (candidate == action) return rect;
        return null;
    }

    /// <summary>Screen position on the radar to world position (the radar picture is fitted into its frame keeping its aspect).</summary>
    public bool TryMinimapWorld(Vector2 screen, out Vector2 world)
    {
        world = default;
        if (_minimap.IsEmpty || !_minimap.Contains((int)screen.X, (int)screen.Y)) return false;
        var (scale, offset) = MinimapFit();
        var pixel = (screen - offset) / scale;
        var image = _match.Minimap;
        if (pixel.X < 0 || pixel.Y < 0 || pixel.X >= image.Width || pixel.Y >= image.Height) return false;
        world = image.PixelToWorld(pixel);
        return true;
    }

    private (float Scale, Vector2 Offset) MinimapFit()
    {
        var image = _match.Minimap;
        var scale = Math.Min(_minimap.Width / (float)image.Width, _minimap.Height / (float)image.Height);
        return (scale, new Vector2(_minimap.X + (_minimap.Width - image.Width * scale) / 2f, _minimap.Y + (_minimap.Height - image.Height * scale) / 2f));
    }

    private Vector2 MinimapScreen(Vector2 world)
    {
        var (scale, offset) = MinimapFit();
        return offset + _match.Minimap.WorldToPixel(world) * scale;
    }

    public UiDrawList Build(SpectatorController controller, int width, int height)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "The viewport needs at least 1x1 pixels.");
        var scale = MathF.Max(0.75f, height / 720f);
        int S(float value) => (int)MathF.Round(value * scale);
        var viewport = new UiViewport(width, height);
        var style = _skin.Style;
        var small = _skin.IsModern ? style.Font(CnCNew.Ui.Skins.UiTypeStep.Caption, viewport) : new UiFont(FontName, Math.Max(8, S(9)), false);
        var normal = _skin.IsModern ? style.Font(CnCNew.Ui.Skins.UiTypeStep.Body, viewport) : new UiFont(FontName, Math.Max(9, S(10)), false);
        var bold = _skin.IsModern ? style.Font(CnCNew.Ui.Skins.UiTypeStep.Control, viewport) with { Bold = true } : new UiFont(FontName, Math.Max(9, S(11)), true);
        var large = _skin.IsModern ? style.Font(CnCNew.Ui.Skins.UiTypeStep.Title, viewport) : new UiFont(FontName, Math.Max(12, S(18)), true);
        var screen = new UiRect(0, 0, width, height);
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var panels = controller.Panels;

        void Label(string text, UiFont font, WndColor color, int x, int y) => commands.Add(new UiTextDraw(text, null, font, color, Shadow, x, y, UiTextAlignment.Left, screen));
        void Centered(string text, UiFont font, WndColor color, int centerX, int y) => Label(text, font, color, centerX - _measure.MeasureWidth(text, font) / 2, y);
        void RightAligned(string text, UiFont font, WndColor color, int right, int y) => Label(text, font, color, right - _measure.MeasureWidth(text, font), y);
        void Box(UiRect rect, WndColor fill)
        {
            commands.Add(new UiFillRect(rect, fill));
            commands.Add(new UiBorderRect(rect, Edge));
        }

        // ---- the simulated cameras in the world: a diamond in the player's colour where each one looks.
        for (var index = 0; index < controller.Cameras.Length; index++)
        {
            var camera = controller.Cameras[index];
            var ground = new Vector3(camera.Position, _match.Heights.HeightAt(camera.Position.X, camera.Position.Y));
            if (!_match.Camera.TryWorldToScreen(ground, out var at) || at.X < 0 || at.Y < 0 || at.X > width || at.Y > height) continue;
            var color = ColorOf(camera.Player);
            float r = S(14);
            var corners = new[] { new Vector2(at.X, at.Y - r), new Vector2(at.X + r, at.Y), new Vector2(at.X, at.Y + r), new Vector2(at.X - r, at.Y) };
            for (var corner = 0; corner < corners.Length; corner++)
            {
                var next = corners[(corner + 1) % corners.Length];
                commands.Add(new UiLineDraw(corners[corner].X, corners[corner].Y, next.X, next.Y, 2f, color));
            }
            // House colours are dark blues and reds: the names are white, the colour is in the mark next to them.
            Label($"AI {index + 1}: {camera.Interest.Kind}", small, Text, (int)at.X + S(18), (int)at.Y - S(6));
        }

        // ---- the radar, bottom left, with the simulated cameras as frames.
        var radarSize = S(190);
        var radarFrame = new UiRect(S(8), height - radarSize - S(8), radarSize, radarSize);
        Box(radarFrame, PlateStrong);
        _minimap = new UiRect(radarFrame.X + 2, radarFrame.Y + 2, radarFrame.Width - 4, radarFrame.Height - 4);
        commands.Add(new UiCustomDraw("W3DLeftHUDDraw", "Spectator:Radar", _minimap));
        commands.Add(new UiPushClip(_minimap));
        for (var index = 0; index < controller.Cameras.Length; index++)
        {
            var color = ColorOf(controller.Cameras[index].Player);
            var footprint = controller.FootprintOf(index);
            for (var corner = 0; corner < footprint.Length; corner++)
            {
                var from = MinimapScreen(footprint[corner]);
                var to = MinimapScreen(footprint[(corner + 1) % footprint.Length]);
                commands.Add(new UiLineDraw(from.X, from.Y, to.X, to.Y, controller.Followed == index ? 3f : 2f, color));
            }
            var center = MinimapScreen(controller.Cameras[index].Position);
            Label((index + 1).ToString(CultureInfo.InvariantCulture), small, color, (int)center.X - 3, (int)center.Y - S(6));
        }
        commands.Add(new UiPopClip());

        // ---- the button bar where a player has the command bar.
        _buttons.Clear();
        var buttonWidth = S(74);
        var buttonHeight = S(34);
        var gap = S(4);
        var perRow = ButtonTable.Length / 2;
        var table = controller.Replay is null ? ButtonTable : [.. ButtonTable, .. ReplayButtonTable];
        var rows = (table.Length + perRow - 1) / perRow;
        var barWidth = perRow * buttonWidth + (perRow - 1) * gap;
        var barLeft = Math.Max(radarFrame.Right + S(12), (width - barWidth) / 2);
        var barTop = height - rows * buttonHeight - (rows - 1) * gap - S(8);
        Box(new UiRect(barLeft - gap, barTop - gap, barWidth + 2 * gap, rows * buttonHeight + (rows + 1) * gap), Plate);
        // The views (Phase 37): everything, or one player's - a row of small buttons above the bar, the player's colour beside its number.
        var viewWidth = S(46);
        var viewHeight = S(20);
        var viewTop = barTop - gap - S(16) - viewHeight - gap;
        var viewCount = 1 + Math.Min(8, controller.Match.Players.Length);
        commands.Add(new UiFillRect(new UiRect(barLeft - gap, viewTop - gap, viewCount * (viewWidth + gap) + gap, viewHeight + 2 * gap), Plate));
        for (var index = 0; index < viewCount; index++)
        {
            var action = index == 0 ? SpectatorAction.ViewAll : SpectatorAction.ViewPlayer1 + (index - 1);
            var rect = new UiRect(barLeft + index * (viewWidth + gap), viewTop, viewWidth, viewHeight);
            _buttons.Add((rect, action));
            Box(rect, IsOn(controller, action) ? ButtonOn : ButtonBack);
            if (index > 0) commands.Add(new UiFillRect(new UiRect(rect.X + 2, rect.Y + 2, S(4), rect.Height - 4), ColorOf(controller.Match.Players[index - 1].Id)));
            Centered(index == 0 ? $"All [{SpectatorController.KeyHint(action)}]" : $"P{index} [{SpectatorController.KeyHint(action)}]", small, Text, rect.CenterX + (index > 0 ? S(2) : 0), rect.Y + S(4));
        }
        for (var index = 0; index < table.Length; index++)
        {
            var (action, label) = table[index];
            var rect = new UiRect(barLeft + index % perRow * (buttonWidth + gap), barTop + index / perRow * (buttonHeight + gap), buttonWidth, buttonHeight);
            _buttons.Add((rect, action));
            Box(rect, IsOn(controller, action) ? ButtonOn : ButtonBack);
            var caption = action == SpectatorAction.CycleFog ? $"Fog: {controller.FogLabel}" : label;
            Centered(caption, small, Text, rect.CenterX, rect.Y + S(4));
            Centered($"[{SpectatorController.KeyHint(action)}]", small, Accent, rect.CenterX, rect.Y + S(18));
        }
        if (controller.Replay is { } replay)
        {
            // The replay's own line beside its seek buttons: where the playback stands in the recording, and how the recording ended.
            var rate = _match.World.Time;
            var at = rate.ToWholeSeconds(_match.Tick);
            var total = rate.ToWholeSeconds(replay.EndTick);
            var checkpoint = (int)(_match.Tick / replay.CheckpointInterval) + 1;
            var checkpoints = (int)((replay.EndTick + replay.CheckpointInterval - 1) / replay.CheckpointInterval);
            var line = string.Create(CultureInfo.InvariantCulture,
                $"REPLAY {at / 60}:{at % 60:00} / {total / 60}:{total % 60:00}   checkpoint {Math.Min(checkpoint, checkpoints)} of {checkpoints}   {CnCNew.Game.Replays.ReplayText.EndingLine(replay.Info)}");
            var row = barTop + (rows - 1) * (buttonHeight + gap);
            var left = barLeft + ReplayButtonTable.Length * (buttonWidth + gap) + S(6);
            Label(line, normal, Accent, left, row + S(4));
            Label(replay.HashMismatches == 0 ? $"{replay.HashesChecked} recorded hashes matched" : $"{replay.HashMismatches} of {replay.HashesChecked} recorded hashes differ (first at tick {replay.FirstMismatchTick})",
                small, replay.HashMismatches == 0 ? Dim : Warning, left, row + S(20));
        }
        var hints = controller.Replay is null
            ? $"seed {controller.Options.Seed}   [J]/[K] jump to AI 1/2   [G] go to the latest event   [Tab] next tab   [H] panels   wheel: zoom   arrows, screen edge, right drag: scroll   [Esc] quit"
            : "[F] fog: all or one player   [G] go to the latest event   [Tab] next tab   [H] panels   wheel: zoom   arrows, screen edge, right drag: scroll   [Esc] back to the replays";
        commands.Add(new UiFillRect(new UiRect(barLeft - gap, barTop - gap - S(16), Math.Min(barWidth + 2 * gap, _measure.MeasureWidth(hints, small) + S(12)), S(16)), Plate));
        Label(hints, small, Dim, barLeft + S(2), barTop - gap - S(14));

        // ---- the top bar: the players side by side, the clock between them.
        var players = panels.Players;
        var topHeight = S(46);
        var clockWidth = S(120);
        var sideWidth = Math.Min(S(470), (width - clockWidth - S(16)) / 2);
        var topLeft = (width - clockWidth) / 2 - sideWidth;
        Box(new UiRect(topLeft, 0, 2 * sideWidth + clockWidth, topHeight), Plate);
        var seconds = panels.GameSeconds;
        Centered(string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}"), bold, Text, width / 2, S(5));
        Centered(controller.SpeedLabel, small, controller.Match.Paused ? Accent : Dim, width / 2, S(20));
        Centered($"fog: {controller.FogLabel}", small, Dim, width / 2, S(31));
        for (var index = 0; index < players.Length && index < 2; index++)
        {
            var player = players[index];
            var left = index == 0 ? topLeft : topLeft + sideWidth + clockWidth;
            var color = ToColor(player.Color);
            commands.Add(new UiFillRect(new UiRect(index == 0 ? left : left + sideWidth - S(6), 0, S(6), topHeight), color));
            var x = left + S(12);
            Label($"{index + 1}  {ShortFaction(player.Faction)}{(player.Defeated ? "  (defeated)" : string.Empty)}", bold, player.Defeated ? Dim : Text, x, S(4));
            var column = (sideWidth - S(24)) / 5;
            TopValue("Money", "$" + Number(player.Money), x);
            TopValue("Income/min", "+" + Number(player.IncomePerMinute), x + column);
            TopValue("Army value", Number(player.ArmyValue), x + 2 * column);
            TopValue("Units", $"{player.Units} ({player.ArmyUnits} army)", x + 3 * column);
            TopValue("Power", $"{player.PowerProduction}/{player.PowerConsumption}", x + 4 * column, player.PowerProduction < player.PowerConsumption ? Warning : Text);
        }
        // More than two players: the others as plain rows under the bar.
        for (var index = 2; index < players.Length; index++)
        {
            var player = players[index];
            var row = new UiRect(topLeft, topHeight + (index - 2) * S(16), 2 * sideWidth + clockWidth, S(16));
            commands.Add(new UiFillRect(row, Plate));
            Label($"{index + 1}  {ShortFaction(player.Faction)}   ${Number(player.Money)}   +{Number(player.IncomePerMinute)}/min   army {Number(player.ArmyValue)}   units {player.Units}   power {player.PowerProduction}/{player.PowerConsumption}",
                small, ToColor(player.Color), row.X + S(12), row.Y + S(2));
        }

        void TopValue(string caption, string value, int x, WndColor? color = null)
        {
            Label(caption, small, Dim, x, S(20));
            Label(value, normal, color ?? Text, x, S(31));
        }

        // ---- the event ticker, top right under the bar (Phase 37): the latest lines up to the frame the match stands at; a click on a line with a place moves the camera there.
        _tickerLines.Clear();
        if (controller.TickerVisible)
        {
            var shown = controller.TickerShown;
            var tickerWidth = Math.Min(S(360), width / 3);
            var tickerLeft = width - tickerWidth - S(8);
            var tickerTop = topHeight + S(8) + Math.Max(0, players.Length - 2) * S(16);
            var lineHeight = S(16);
            var tickerHeight = S(20) + Math.Max(1, shown.Count) * lineHeight + S(4);
            Box(new UiRect(tickerLeft, tickerTop, tickerWidth, tickerHeight), Plate);
            Label($"Events [{SpectatorController.KeyHint(SpectatorAction.ToggleTicker)}]   go to the latest [{SpectatorController.KeyHint(SpectatorAction.GoToEvent)}]", small, Accent, tickerLeft + S(8), tickerTop + S(3));
            var clip = new UiRect(tickerLeft + 2, tickerTop, tickerWidth - 4, tickerHeight);
            commands.Add(new UiPushClip(clip));
            if (shown.Count == 0) Label("nothing yet", small, Dim, tickerLeft + S(8), tickerTop + S(20));
            for (var index = 0; index < shown.Count; index++)
            {
                var item = shown[index];
                var top = tickerTop + S(20) + index * lineHeight;
                var rect = new UiRect(tickerLeft + 2, top, tickerWidth - 4, lineHeight);
                if (item.HasPlace) _tickerLines.Add((rect, item));
                var player = controller.Match.Players.FirstOrDefault(value => value.Id.Value == item.Player);
                commands.Add(new UiFillRect(new UiRect(tickerLeft + S(6), top + S(3), S(4), lineHeight - S(6)), player is null ? Edge : ColorOf(player.Id)));
                var stamp = string.Create(CultureInfo.InvariantCulture, $"{item.Second / 60}:{item.Second % 60:00}");
                Label(stamp, small, Dim, tickerLeft + S(14), top + S(2));
                Label(item.Text, small, controller.WentTo == item ? Accent : Text, tickerLeft + S(50), top + S(2));
            }
            commands.Add(new UiPopClip());
        }

        // ---- the result.
        if (panels.IsOver)
        {
            var result = panels.Winner is { } winner && players.FirstOrDefault(player => player.Player == winner) is { } won
                ? $"WINNER: {ShortFaction(won.Faction)} (player {winner.Value})"
                : "NO WINNER (time limit)";
            var resultWidth = _measure.MeasureWidth(result, large) + S(40);
            Box(new UiRect((width - resultWidth) / 2, topHeight + S(40), resultWidth, S(40)), PlateStrong);
            if (panels.Winner is { } victor) commands.Add(new UiFillRect(new UiRect((width - resultWidth) / 2, topHeight + S(40), S(8), S(40)), ColorOf(victor)));
            Centered(result, large, Accent, width / 2, topHeight + S(48));
        }

        // ---- the detail panel of the chosen tab, top left.
        if (controller.PanelsVisible)
        {
            var line = S(15);
            var icon = S(22);
            var panelWidth = S(300);
            var x = S(8);
            var y = topHeight + S(8) + Math.Max(0, players.Length - 2) * S(16);
            // The plate is as tall as its rows, at most down to the radar; it is put under them once they are laid out.
            var plateAt = commands.Count;
            var available = Math.Max(S(40), radarFrame.Y - y - S(8));
            commands.Add(new UiPushClip(new UiRect(x, y, panelWidth, available)));
            var tabX = x + S(8);
            foreach (var tab in Enum.GetValues<SpectatorTab>())
            {
                var caption = $"[{SpectatorController.KeyHint(SpectatorController.ActionOfTab(tab))}] {tab}";
                Label(caption, small, tab == controller.Tab ? Accent : Dim, tabX, y + S(6));
                tabX += _measure.MeasureWidth(caption, small) + S(10);
            }
            var cursor = y + S(24);
            foreach (var player in players)
            {
                var color = ToColor(player.Color);
                commands.Add(new UiFillRect(new UiRect(x + S(6), cursor + 2, S(4), S(13)), color));
                Label(ShortFaction(player.Faction), bold, Text, x + S(14), cursor);
                cursor += S(20);
                cursor = controller.Tab switch
                {
                    SpectatorTab.Production => ProductionRows(player, cursor),
                    SpectatorTab.Units => CountRows(player.Army, cursor, $"army {player.ArmyUnits}, value {Number(player.ArmyValue)}, workers {player.Workers}"),
                    SpectatorTab.Losses => LossRows(player, cursor),
                    SpectatorTab.Research => ResearchRows(player, cursor),
                    _ => EconomyRows(player, cursor),
                };
                cursor += S(6);
            }
            commands.Add(new UiPopClip());
            var plate = new UiRect(x, y, panelWidth, Math.Min(cursor - y, available));
            commands.Insert(plateAt, new UiBorderRect(plate, Edge));
            commands.Insert(plateAt, new UiFillRect(plate, Plate));

            int Row(string template, string text, int top, float? progress = null)
            {
                var rowHeight = Math.Max(line, icon + 2);
                var textX = x + S(14);
                if (_cameos.TryGetValue(template, out var cameo))
                {
                    commands.Add(new UiImageDraw(cameo, new UiRect(textX, top, icon, icon), WndColor.White, false, false));
                    textX += icon + S(6);
                }
                Label(text, normal, Text, textX, top + (rowHeight - line) / 2);
                if (progress is { } done)
                {
                    var bar = new UiRect(x + panelWidth - S(78), top + rowHeight / 2 - S(3), S(70), S(6));
                    commands.Add(new UiFillRect(bar, BarBack));
                    commands.Add(new UiFillRect(bar with { Width = (int)(bar.Width * Math.Clamp(done, 0f, 1f)) }, BarFill));
                }
                return top + rowHeight;
            }

            int Note(string text, int top)
            {
                Label(text, small, Dim, x + S(14), top);
                return top + line;
            }

            int ProductionRows(SpectatorPlayerPanel player, int top)
            {
                if (player.Production.IsEmpty) return Note("nothing in production", top);
                foreach (var item in player.Production.Take(ListRows))
                    top = Row(item.Template, item.IsConstruction ? $"{Short(item.Template, 20)} (site)" : $"{Short(item.Template, 22)}{(item.Queued > 1 ? $" +{item.Queued - 1}" : string.Empty)}", top, item.Progress);
                return player.Production.Length > ListRows ? Note($"... {player.Production.Length - ListRows} more", top) : top;
            }

            int CountRows(ImmutableArray<SpectatorCount> counts, int top, string summary)
            {
                top = Note(summary, top);
                foreach (var item in counts.Take(ListRows)) top = Row(item.Template, $"{item.Count} x {Short(item.Template)}   ({Number(item.Value)})", top);
                return counts.Length > ListRows ? Note($"... {counts.Length - ListRows} more types", top) : top;
            }

            int LossRows(SpectatorPlayerPanel player, int top)
            {
                top = Note($"kills {player.Kills}, value {Number(player.KillsValue)}", top);
                top = CountRows([..player.UnitsLost.Take(ListRows / 2)], top, $"units lost {player.UnitsLostCount}, value {Number(player.UnitsLostValue)}");
                return CountRows([..player.StructuresLost.Take(ListRows / 2)], top, $"structures lost {player.StructuresLostCount}, value {Number(player.StructuresLostValue)}");
            }

            int ResearchRows(SpectatorPlayerPanel player, int top)
            {
                top = Note($"upgrades {player.Upgrades.Length} (and {player.ObjectUpgrades} of its objects), sciences {player.Sciences.Length}", top);
                foreach (var name in player.Upgrades.Take(ListRows / 2)) top = Note("  " + Short(name, 34), top);
                if (player.Upgrades.Length > ListRows / 2) top = Note($"  ... {player.Upgrades.Length - ListRows / 2} more upgrades", top);
                foreach (var name in player.Sciences.Take(ListRows / 2)) top = Note("  " + Short(name, 34), top);
                return player.Sciences.Length > ListRows / 2 ? Note($"  ... {player.Sciences.Length - ListRows / 2} more sciences", top) : top;
            }

            int EconomyRows(SpectatorPlayerPanel player, int top)
            {
                foreach (var (caption, value) in new[]
                         {
                             ("Money", "$" + Number(player.Money)), ("Income, last minute", "+" + Number(player.IncomePerMinute)), ("Collected in total", Number(player.Collected)),
                             ("Supply carriers", player.Harvesters.ToString(CultureInfo.InvariantCulture)), ("Supply centres", player.SupplyCenters.ToString(CultureInfo.InvariantCulture)),
                             ("Power made / used", $"{player.PowerProduction} / {player.PowerConsumption}"),
                             ("Structures (built)", $"{player.Structures} ({player.StructuresBuilt})"), ("Workers", player.Workers.ToString(CultureInfo.InvariantCulture)),
                         })
                {
                    Label(caption, normal, Dim, x + S(14), top);
                    RightAligned(value, normal, Text, x + panelWidth - S(10), top);
                    top += line;
                }
                return top;
            }
        }

        return new UiDrawList(commands.ToImmutable(), new UiDrawStatistics(0, 0, 0, 0,
            ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal).Add("W3DLeftHUDDraw", 1)));
    }

    private static bool IsOn(SpectatorController controller, SpectatorAction action) => action switch
    {
        SpectatorAction.TabProduction => controller.PanelsVisible && controller.Tab == SpectatorTab.Production,
        SpectatorAction.TabUnits => controller.PanelsVisible && controller.Tab == SpectatorTab.Units,
        SpectatorAction.TabLosses => controller.PanelsVisible && controller.Tab == SpectatorTab.Losses,
        SpectatorAction.TabEconomy => controller.PanelsVisible && controller.Tab == SpectatorTab.Economy,
        SpectatorAction.TabResearch => controller.PanelsVisible && controller.Tab == SpectatorTab.Research,
        SpectatorAction.ViewAll => controller.FogViewer is null,
        >= SpectatorAction.ViewPlayer1 and <= SpectatorAction.ViewPlayer8 => controller.FogViewer is { } viewer && controller.Match.Players.Length > action - SpectatorAction.ViewPlayer1
            && controller.Match.Players[action - SpectatorAction.ViewPlayer1].Id == viewer,
        SpectatorAction.ToggleTicker => controller.TickerVisible,
        SpectatorAction.FollowFirst => controller.Followed == 0,
        SpectatorAction.FollowSecond => controller.Followed == 1,
        SpectatorAction.FreeCamera => controller.Followed is null && !controller.Overview,
        SpectatorAction.Overview => controller.Overview,
        SpectatorAction.CycleFog => controller.FogViewer is not null,
        SpectatorAction.Pause => controller.Match.Paused,
        SpectatorAction.Speed1 => !controller.Match.Paused && controller.Match.Speed == 1,
        SpectatorAction.Speed2 => !controller.Match.Paused && controller.Match.Speed == 2,
        SpectatorAction.Speed4 => !controller.Match.Paused && controller.Match.Speed == 4,
        SpectatorAction.Speed8 => !controller.Match.Paused && controller.Match.Speed == 8,
        _ => false,
    };

    private WndColor ColorOf(CnCNew.Simulation.Ids.PlayerId player) => ToColor(_match.Colors.Of(player));

    private static WndColor ToColor(ColorValue color) => new(color.Red, color.Green, color.Blue, 255);

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary><c>FactionAmericaAirForceGeneral</c> reads as <c>AmericaAirForceGeneral</c>.</summary>
    public static string ShortFaction(string faction) => faction.StartsWith("Faction", StringComparison.Ordinal) && faction.Length > "Faction".Length ? faction["Faction".Length..] : faction;

    /// <summary>Template names are long; the panel cuts them, the full name is in the report of a headless run.</summary>
    private static string Short(string template, int length = 26) => template.Length > length ? template[..(length - 1)] + "." : template;
}

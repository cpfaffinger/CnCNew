using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>
/// The world-space feedback of a running match as draw commands (<see cref="HudOverlay"/> -> <see cref="UiDrawCommand"/>): the drag-select box, a ring and health bar under every selected
/// unit, standing-order paths, rally flags, the construction footprint, and the healing wrench / cross over a healed object. The overlay was computed every frame but nothing drew it
/// (docs/DEMO-FINDINGS.md #8, #14). Pure function of the overlay and a clock in milliseconds, so the same overlay always yields the same commands.
/// <para>
/// Healing icons follow <c>Animation2D.ini</c>: <c>StructureHeal</c>/<c>VehicleHeal</c> are the ten <c>SCPWrench000..009</c> frames played ping-pong at 60 ms per frame, <c>DefaultHeal</c> the
/// sixteen <c>SCPCross000..015</c> frames in a loop at 30 ms; the start frame is randomised in the original (here a fixed phase per entity so a replay of the frame is the same).
/// </para>
/// </summary>
public static class HudOverlayRenderer
{
    private static readonly WndColor Own = new(60, 255, 90, 255);
    private static readonly WndColor Enemy = new(255, 60, 60, 255);
    private static readonly WndColor Neutral = new(255, 230, 80, 255);
    private static readonly WndColor ActiveKindColor = new(255, 255, 255, 255);
    private static readonly WndColor MoveOrder = new(90, 255, 120, 220);
    private static readonly WndColor AttackOrder = new(255, 90, 80, 220);
    /// <summary>The waypoint line colour of <c>W3DWaypointBuffer::setDefaultLineStyle</c> (0.25, 0.5, 1.0).</summary>
    private static readonly WndColor WaypointColor = new(64, 128, 255, 235);
    private static readonly WndColor GhostValid = new(80, 255, 110, 255);
    private static readonly WndColor GhostInvalid = new(255, 70, 60, 255);
    private static readonly WndColor RallyColor = new(255, 235, 90, 255);
    private static readonly WndColor BarOutline = new(0, 0, 0, 255);
    private static readonly WndColor BarBack = new(0, 0, 0, 170);

    private const int RingSegments = 28;
    private const int PlacementHatchLines = 8;
    private const int WrenchFrames = 10;
    private const int CrossFrames = 16;

    public static ImmutableArray<UiDrawCommand> Build(HudOverlay overlay, long clockMilliseconds, HudMarkerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        options ??= HudMarkerOptions.Default;
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();

        if (overlay.SelectionBox.Active)
        {
            var box = Rectangle(overlay.SelectionBox.Start, overlay.SelectionBox.Current);
            commands.Add(new UiFillRect(box, new WndColor(255, 255, 255, 28)));
            commands.Add(new UiBorderRect(box, new WndColor(255, 255, 255, 230)));
        }

        foreach (var order in overlay.OrderLines)
        {
            var color = order.IsAttack ? AttackOrder : MoveOrder;
            for (var index = 1; index < order.Points.Length; index++)
                commands.Add(new UiLineDraw(order.Points[index - 1].X, order.Points[index - 1].Y, order.Points[index].X, order.Points[index].Y, 1.5f, color));
            if (!order.Points.IsDefaultOrEmpty) Ring(commands, order.Points[^1], 5f, 1.5f, color, 12);
        }

        if (!overlay.WaypointPaths.IsDefaultOrEmpty)
            foreach (var path in overlay.WaypointPaths)
            {
                for (var index = 1; index < path.Points.Length; index++)
                    commands.Add(new UiLineDraw(path.Points[index - 1].X, path.Points[index - 1].Y, path.Points[index].X, path.Points[index].Y, 2f, WaypointColor));
                for (var index = 1; index < path.Points.Length; index++) WaypointNode(commands, path.Points[index]);
            }

        // The rally line of a selected producer: the same line and node marker as a waypoint path (W3DWaypointBuffer draws both with one style).
        if (!overlay.RallyLines.IsDefaultOrEmpty)
            foreach (var line in overlay.RallyLines)
            {
                for (var index = 1; index < line.Points.Length; index++)
                    commands.Add(new UiLineDraw(line.Points[index - 1].X, line.Points[index - 1].Y, line.Points[index].X, line.Points[index].Y, 2f, WaypointColor));
                foreach (var node in line.Nodes) WaypointNode(commands, node);
            }

        if (!overlay.OrderHints.IsDefaultOrEmpty)
            foreach (var hint in overlay.OrderHints)
                DrawOrderHint(commands, hint);

        foreach (var marker in overlay.Selected)
        {
            var color = marker.IsOwn ? Own : marker.IsEnemy ? Enemy : Neutral;
            if (!marker.BarOnly) SelectionMark(commands, marker, options.Style, color);
            HealthBar(commands, marker, options);
        }

        if (overlay.Placement.Active && overlay.Placement.FootprintScreen.Length >= 3)
        {
            var color = overlay.Placement.IsValid ? GhostValid : GhostInvalid;
            var corners = overlay.Placement.FootprintScreen;
            for (var index = 0; index < corners.Length; index++)
            {
                var next = corners[(index + 1) % corners.Length];
                commands.Add(new UiLineDraw(corners[index].X, corners[index].Y, next.X, next.Y, 2.5f, color));
            }

            // The diagonals make the footprint readable on any ground.
            commands.Add(new UiLineDraw(corners[0].X, corners[0].Y, corners[2].X, corners[2].Y, 1f, color with { A = 150 }));
            commands.Add(new UiLineDraw(corners[1].X, corners[1].Y, corners[corners.Length - 1].X, corners[corners.Length - 1].Y, 1f, color with { A = 150 }));

            // There is no ghost model yet: a hatching across the footprint stands in for it, so the preview reads as an area (green: legal, red: not) and not as a thin frame.
            if (corners.Length == 4)
            {
                for (var line = 1; line < PlacementHatchLines; line++)
                {
                    var t = line / (float)PlacementHatchLines;
                    var from = Vector2.Lerp(corners[0], corners[3], t);
                    var to = Vector2.Lerp(corners[1], corners[2], t);
                    commands.Add(new UiLineDraw(from.X, from.Y, to.X, to.Y, 2f, color with { A = 110 }));
                }
            }
        }

        if (!overlay.HealIcons.IsDefaultOrEmpty)
            foreach (var icon in overlay.HealIcons)
                HealIcon(commands, icon, clockMilliseconds);

        if (!overlay.Icons.IsDefaultOrEmpty)
            foreach (var icon in overlay.Icons)
                commands.Add(new UiImageDraw(icon.Image, icon.Place, WndColor.White with { A = icon.Alpha }, false, false));

        // The text's line starts at its point and the font is scaled with the screen: the clip holds the whole line (the owner's playtest of 3 November 2026 saw the lower half of a
        // construction's percent cut off by a clip of 18 pixels from 8 above the point).
        if (!overlay.Labels.IsDefaultOrEmpty)
            foreach (var label in overlay.Labels)
                commands.Add(new UiTextDraw(label.Text, null, LabelFont, label.Color ?? WndColor.White, new WndColor(0, 0, 0, label.Color?.A ?? 255), (int)label.At.X, (int)label.At.Y, UiTextAlignment.Center,
                    new UiRect((int)label.At.X - LabelClipHalfWidth, (int)label.At.Y - LabelClipAbove, 2 * LabelClipHalfWidth, LabelClipAbove + LabelClipBelow)));

        if (!overlay.Ranks.IsDefaultOrEmpty)
            foreach (var rank in overlay.Ranks)
                if (rank.Level is >= 1 and <= 3)
                    commands.Add(new UiImageDraw($"SCVeter{rank.Level}", new UiRect((int)MathF.Round(rank.At.X) + 1, (int)MathF.Round(rank.At.Y) + 1, RankSize, RankSize), WndColor.White, false, false));

        return commands.ToImmutable();
    }

    /// <summary>
    /// The station marker of a waypoint path: a diamond with a centre ring (the original places the <c>SCMNode</c> model there, <c>W3DWaypointBuffer</c>; this
    /// client has no 3D marker models yet, so the node is drawn in the overlay in the line's colour).
    /// </summary>
    private static void WaypointNode(ImmutableArray<UiDrawCommand>.Builder commands, Vector2 at)
    {
        const float size = 7f;
        var corners = new[] { new Vector2(at.X, at.Y - size), new Vector2(at.X + size, at.Y), new Vector2(at.X, at.Y + size), new Vector2(at.X - size, at.Y) };
        for (var index = 0; index < corners.Length; index++)
        {
            var next = corners[(index + 1) % corners.Length];
            commands.Add(new UiLineDraw(corners[index].X, corners[index].Y, next.X, next.Y, 2f, WaypointColor));
        }
        Ring(commands, at, 2f, 2f, WaypointColor, 6);
    }

    /// <summary>
    /// The marker of an order just given: a ring that closes in on the spot and fades (the original plays the move hint model's animation once and removes it
    /// after 40 frames, <c>W3DInGameUI::drawMoveHints</c>); an attack is marked by four closing corner ticks instead.
    /// </summary>
    private static void DrawOrderHint(ImmutableArray<UiDrawCommand>.Builder commands, OrderHintMarker hint)
    {
        var age = Math.Clamp(hint.Age, 0f, 1f);
        var alpha = (byte)MathF.Round(255f * (1f - age));
        if (alpha == 0) return;
        var baseColor = hint.Kind switch
        {
            OrderHintKind.Attack => AttackOrder,
            OrderHintKind.Waypoint => WaypointColor,
            OrderHintKind.RallyPoint => RallyColor,
            _ => MoveOrder,
        };
        var color = baseColor with { A = alpha };
        var radius = 20f - 14f * age;
        if (hint.Kind == OrderHintKind.Attack)
        {
            foreach (var (sx, sy) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                var cx = hint.Screen.X + sx * radius;
                var cy = hint.Screen.Y + sy * radius;
                commands.Add(new UiLineDraw(cx, cy, cx - sx * 7f, cy, 2.5f, color));
                commands.Add(new UiLineDraw(cx, cy, cx, cy - sy * 7f, 2.5f, color));
            }
            return;
        }
        Ring(commands, hint.Screen, radius, 2.5f, color, 20);
        commands.Add(new UiLineDraw(hint.Screen.X - 4f, hint.Screen.Y, hint.Screen.X + 4f, hint.Screen.Y, 2f, color));
        commands.Add(new UiLineDraw(hint.Screen.X, hint.Screen.Y - 4f, hint.Screen.X, hint.Screen.Y + 4f, 2f, color));
    }

    /// <summary>
    /// What marks a selected object in the world, by <see cref="SelectionMarkerStyle"/>. <see cref="SelectionMarkerStyle.Extended"/> is what this port drew from the start (a ring under a unit, corner
    /// brackets round a building); <see cref="SelectionMarkerStyle.Original"/> draws nothing - the original marks a selection by its health box alone; <see cref="SelectionMarkerStyle.Modern"/> a thin,
    /// translucent ring and short brackets. The kind Tab chose keeps its second ring in every style (the original has no subgroups). An object only under the pointer is marked by its bar alone.
    /// </summary>
    private static void SelectionMark(ImmutableArray<UiDrawCommand>.Builder commands, UnitMarker marker, SelectionMarkerStyle style, WndColor color)
    {
        var center = marker.IsStructure ? StructureCenter(marker) : marker.Center;
        var half = MathF.Max(marker.IsStructure ? StructureHalf(marker) : marker.Radius, 6f);
        if (!marker.Hovered)
        {
            switch (style)
            {
                case SelectionMarkerStyle.Extended:
                    // A structure is marked by corner brackets round its screen footprint (the selection radius of a building is its bounding circle, far too large for a ring).
                    if (marker.IsStructure) Brackets(commands, center, StructureHalf(marker), color);
                    else Ring(commands, marker.Center, MathF.Max(marker.Radius, 6f), 2f, color, RingSegments);
                    break;
                case SelectionMarkerStyle.Modern:
                    var soft = color with { A = 210 };
                    if (marker.IsStructure) Brackets(commands, center, StructureHalf(marker), soft, thickness: 1.5f, legScale: 0.55f);
                    else
                    {
                        Ring(commands, marker.Center, half, 1.5f, soft, RingSegments);
                        Ring(commands, marker.Center, half + 2.5f, 1f, color with { A = 70 }, RingSegments);
                    }
                    break;
            }
        }
        // The kind Tab chose in a mixed selection: a second ring outside the first, in white.
        if (marker.ActiveKind && !marker.Hovered) Ring(commands, center, half + 4f, style == SelectionMarkerStyle.Modern ? 1.5f : 2f, ActiveKindColor, RingSegments);
    }

    /// <summary>The pixels of a health bar's fill for a style and the viewer's choice: 3, 5 or 8 (docs/GAME-HUD.md "Phase 65"); the modern style's bar is thinner: 3, 4 or 6.</summary>
    public static int BarFillHeight(SelectionMarkerStyle style, HealthBarHeight height) => (style, height) switch
    {
        (SelectionMarkerStyle.Modern, HealthBarHeight.Narrow) => 3,
        (SelectionMarkerStyle.Modern, HealthBarHeight.Normal) => 4,
        (SelectionMarkerStyle.Modern, HealthBarHeight.Wide) => 6,
        (_, HealthBarHeight.Narrow) => 3,
        (_, HealthBarHeight.Wide) => 8,
        _ => 5,
    };

    /// <summary>The height of a bar with its outline: the fill and one pixel above and below in the extended style; the original's box is as high as the choice says (3 pixels is its own); the modern bar has no outline.</summary>
    public static int BarOuterHeight(SelectionMarkerStyle style, HealthBarHeight height) => style switch
    {
        SelectionMarkerStyle.Extended => BarFillHeight(style, height) + 2,
        _ => BarFillHeight(style, height),
    };

    /// <summary>
    /// <c>Drawable::drawHealthBar</c>'s colours: green through yellow to red by the health ratio, blue to cyan for an object under construction or disabled, each averaged with red for a
    /// really damaged body and with green for a pristine one; the outline is the same colour at half. Returns (fill, outline).
    /// </summary>
    public static (WndColor Fill, WndColor Outline) OriginalHealthColors(float health, int damage, bool blue)
    {
        health = Math.Clamp(health, 0f, 1f);
        if (blue) return (new WndColor(0, (byte)(health * 255f), 255, 255), new WndColor(0, (byte)(health * 128f), 128, 255));
        float red, green;
        if (health >= 0.5f) { red = 1f - (health - 0.5f) / 0.5f; green = 1f; }
        else { red = 1f; green = 1f - (0.5f - health) / 0.5f; }
        var (outRed, outGreen) = (red * 0.5f, green * 0.5f);
        if (damage >= 2) { red = (1f + red) * 0.5f; green *= 0.5f; }
        else if (damage < 1) { green = (1f + green) * 0.5f; red *= 0.5f; }
        return (new WndColor((byte)(255f * red), (byte)(255f * green), 0, 255), new WndColor((byte)(255f * outRed), (byte)(255f * outGreen), 0, 255));
    }

    /// <summary>The modern bar's fill: the skin's green above half, amber above a quarter, red below (the same thresholds as the selection plate of the bar).</summary>
    public static WndColor ModernHealthColor(float health)
    {
        var palette = CnCNew.Ui.Skins.UiSkin.Modern.Style.Palette;
        return health > 0.5f ? palette.Good : health > 0.25f ? palette.Accent : palette.Bad;
    }

    /// <summary>The ring is a closed polyline; the bar sits above it: a dark plate, the health in a green-to-red ramp (blue-cyan is for objects under construction in the original; not tracked here).</summary>
    private static void HealthBar(ImmutableArray<UiDrawCommand>.Builder commands, UnitMarker marker, HudMarkerOptions options)
    {
        var width = MathF.Max(26f, marker.Radius * (marker.IsStructure ? 1.1f : 1.7f));
        var left = marker.Center.X - width / 2f;
        var top = (marker.IsStructure ? StructureCenter(marker).Y - StructureHalf(marker) * 0.7f : marker.Center.Y - MathF.Max(marker.Radius, 6f)) - 11f;
        var health = Math.Clamp(marker.HealthFraction, 0f, 1f);
        var fill = BarFillHeight(options.Style, options.Height);
        var outer = BarOuterHeight(options.Style, options.Height);
        switch (options.Style)
        {
            case SelectionMarkerStyle.Original:
            {
                // Drawable::drawHealthBar: an object without health has no box; the box is an outline in the dark colour and the fill inside it, no plate behind the empty part.
                if (health <= 0f) { if (marker.BarOnly) return; break; }
                var (color, outline) = OriginalHealthColors(health, marker.Damage, marker.Blue);
                var box = new UiRect((int)MathF.Floor(left), (int)MathF.Floor(top), (int)MathF.Ceiling(width), outer);
                commands.Add(new UiBorderRect(box, outline));
                var inside = (int)MathF.Round((box.Width - 2) * health);
                if (inside > 0 && outer > 2) commands.Add(new UiFillRect(new UiRect(box.X + 1, box.Y + 1, inside, outer - 2), color));
                break;
            }
            case SelectionMarkerStyle.Modern:
            {
                if (health <= 0f) { if (marker.BarOnly) return; break; }
                var track = new UiRect((int)MathF.Floor(left), (int)MathF.Floor(top), (int)MathF.Ceiling(width), outer);
                var palette = CnCNew.Ui.Skins.UiSkin.Modern.Style.Palette;
                // A flat bar with the corners cut off by a pixel: the track in the skin's canvas, the fill in its health colour.
                Rounded(commands, track, palette.Canvas with { A = 190 });
                var inside = (int)MathF.Round(track.Width * health);
                if (inside > 0) Rounded(commands, new UiRect(track.X, track.Y, inside, track.Height), ModernHealthColor(health));
                break;
            }
            default:
            {
                var plate = new UiRect((int)MathF.Floor(left) - 1, (int)MathF.Floor(top) - 1, (int)MathF.Ceiling(width) + 2, outer);
                commands.Add(new UiFillRect(plate, BarBack));
                commands.Add(new UiBorderRect(plate, BarOutline));
                var filled = (int)MathF.Round(width * health);
                if (filled > 0) commands.Add(new UiFillRect(new UiRect((int)MathF.Floor(left), (int)MathF.Floor(top), filled, fill), HealthColor(health)));
                break;
            }
        }
        if (marker.BarOnly) return;
        // Drawable::drawUIText, with the values of DrawGroupInfo.ini: the number of the control group at the left end of the bar, a fifth of its width to the left of it and ten pixels above it.
        if (marker.GroupNumber >= 0 && marker.GroupText.Length > 0)
        {
            var (x, y) = ((int)MathF.Floor(left + width * GroupOffsetX), (int)MathF.Floor(top) + GroupOffsetY);
            commands.Add(new UiTextDraw(marker.GroupText, null, GroupFont, WndColor.White, BarOutline, x, y, UiTextAlignment.Left, new UiRect(x - 4, y - 4, GroupClipWidth, GroupClipHeight)));
        }
        var row = (int)MathF.Floor(top) + outer;
        if (marker.Ammo is { } ammo)
        {
            for (var pip = 0; pip < ammo.Total; pip++)
                commands.Add(new UiImageDraw(pip < ammo.Full ? "SCPAmmoFull" : "SCPAmmoEmpty", new UiRect((int)MathF.Floor(left) + pip * (AmmoPipWidth + 1), row, AmmoPipWidth, AmmoPipHeight), WndColor.White, false, false));
            row += AmmoPipHeight + 1;
        }
        // "If empty, don't show nothin'."
        if (marker.Seats is { Full: > 0 } seats)
            for (var pip = 0; pip < seats.Total; pip++)
            {
                var place = new UiRect((int)MathF.Floor(left) + pip * (AmmoPipWidth + 1), row, AmmoPipWidth, SeatPipHeight);
                if (pip < seats.Full) commands.Add(new UiImageDraw("SCPPipFull", place, pip < seats.Infantry ? InfantrySeat : VehicleSeat, false, false));
                else commands.Add(new UiImageDraw("SCPPipEmpty", place, WndColor.White, false, false));
            }
    }

    private static readonly UiFont LabelFont = new("Arial", 12, true);

    /// <summary><c>DrawGroupInfo.ini</c>: Arial 10 not bold, white with a black shadow, at -20 % of the bar's width and -10 pixels from the bar's top.</summary>
    private static readonly UiFont GroupFont = new("Arial", 10, false);
    private const float GroupOffsetX = -0.2f;
    private const int GroupOffsetY = -10, GroupClipWidth = 40, GroupClipHeight = 40;

    /// <summary>The clip around a label's point: wide enough for a long line, from a little above the point to below the tallest scaled line.</summary>
    private const int LabelClipHalfWidth = 200, LabelClipAbove = 8, LabelClipBelow = 64;

    private const int AmmoPipWidth = 5, AmmoPipHeight = 10, SeatPipHeight = 12, RankSize = 16;
    private static readonly WndColor InfantrySeat = new(0, 255, 0, 255);
    private static readonly WndColor VehicleSeat = new(0, 0, 255, 255);

    // The projected centre of a building is its bounding sphere's centre, above the footprint on screen; the brackets hug the visible footprint a little below it.
    private static Vector2 StructureCenter(UnitMarker marker) => new(marker.Center.X, marker.Center.Y + marker.Radius * 0.22f);

    private static float StructureHalf(UnitMarker marker) => MathF.Max(marker.Radius * 0.62f, 12f);

    /// <summary>Green at full health, through yellow at half, to red (the original's ramp without its blue: <c>inColor.blue = 0</c>).</summary>
    public static WndColor HealthColor(float health)
    {
        health = Math.Clamp(health, 0f, 1f);
        var red = health >= 0.5f ? (1f - (health - 0.5f) / 0.5f) : 1f;
        var green = health >= 0.5f ? 1f : health / 0.5f;
        return new WndColor((byte)MathF.Round(red * 255f), (byte)MathF.Round(green * 255f), 0, 255);
    }

    /// <summary>
    /// <c>Drawable::drawHealing</c>: the icon is placed over the health bar region at 75 percent of its width, its size a share of the bar width (0.33 for structures, 0.7 otherwise); the
    /// bar of a structure is wide, so the wrench is clamped to a readable size here.
    /// </summary>
    private static void HealIcon(ImmutableArray<UiDrawCommand>.Builder commands, HealIconMarker icon, long clockMilliseconds)
    {
        var name = icon.Kind == HealIconKind.Default
            ? "SCPCross" + Frame(CrossFrames, clockMilliseconds / 30 + icon.Entity.Value % CrossFrames, pingPong: false).ToString("000", System.Globalization.CultureInfo.InvariantCulture)
            : "SCPWrench" + Frame(WrenchFrames, clockMilliseconds / 60 + icon.Entity.Value % WrenchFrames, pingPong: true).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
        var scale = icon.Kind == HealIconKind.Structure ? 0.33f : 0.7f;
        var size = (int)Math.Clamp(icon.BarWidth * scale, 22f, 40f);
        var x = (int)MathF.Round(icon.Anchor.X + icon.BarWidth * 0.25f - size / 2f);
        var y = (int)MathF.Round(icon.Anchor.Y - size - 12f);
        commands.Add(new UiImageDraw(name, new UiRect(x, y, size, size), WndColor.White, false, false));
    }

    /// <summary>Frame index <paramref name="step"/> of an animation of <paramref name="count"/> frames, looping or ping-pong (<c>Anim2D</c> <c>LOOP</c> and <c>PING_PONG</c>).</summary>
    public static int Frame(int count, long step, bool pingPong)
    {
        if (!pingPong) return (int)(step % count);
        var cycle = Math.Max(1, 2 * count - 2);
        var position = (int)(step % cycle);
        return position < count ? position : cycle - position;
    }

    /// <summary>A bar of <paramref name="color"/> whose four corner pixels are left out (a height of three or more): the nearest a flat rectangle gets to a rounded end.</summary>
    private static void Rounded(ImmutableArray<UiDrawCommand>.Builder commands, UiRect rect, WndColor color)
    {
        if (rect.Height < 3 || rect.Width < 3) { commands.Add(new UiFillRect(rect, color)); return; }
        commands.Add(new UiFillRect(new UiRect(rect.X + 1, rect.Y, rect.Width - 2, rect.Height), color));
        commands.Add(new UiFillRect(new UiRect(rect.X, rect.Y + 1, rect.Width, rect.Height - 2), color));
    }

    private static void Brackets(ImmutableArray<UiDrawCommand>.Builder commands, Vector2 center, float half, WndColor color, float thickness = 2.5f, float legScale = 1f)
    {
        half = MathF.Max(half, 12f);
        var leg = MathF.Min(18f, half * 0.6f) * legScale;
        foreach (var (sx, sy) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var cx = center.X + sx * half;
            var cy = center.Y + sy * half * 0.7f;
            commands.Add(new UiLineDraw(cx, cy, cx - sx * leg, cy, thickness, color));
            commands.Add(new UiLineDraw(cx, cy - sy * 1.2f, cx, cy - sy * (leg * 0.7f), thickness, color));
        }
    }

    private static void Ring(ImmutableArray<UiDrawCommand>.Builder commands, Vector2 center, float radius, float thickness, WndColor color, int segments)
    {
        var previous = new Vector2(center.X + radius, center.Y);
        for (var segment = 1; segment <= segments; segment++)
        {
            var angle = MathF.Tau * segment / segments;
            var next = new Vector2(center.X + MathF.Cos(angle) * radius, center.Y + MathF.Sin(angle) * radius);
            commands.Add(new UiLineDraw(previous.X, previous.Y, next.X, next.Y, thickness, color));
            previous = next;
        }
    }

    private static UiRect Rectangle(Vector2 a, Vector2 b)
    {
        var left = (int)MathF.Round(MathF.Min(a.X, b.X));
        var top = (int)MathF.Round(MathF.Min(a.Y, b.Y));
        var right = (int)MathF.Round(MathF.Max(a.X, b.X));
        var bottom = (int)MathF.Round(MathF.Max(a.Y, b.Y));
        return new UiRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }
}

/// <summary>How the world-space markers are drawn (<c>display.markerStyle</c> and <c>display.healthBarHeight</c>); the default is what this port drew before the options existed.</summary>
public sealed record HudMarkerOptions(CnCNew.Ui.Settings.SelectionMarkerStyle Style = CnCNew.Ui.Settings.SelectionMarkerStyle.Extended, CnCNew.Ui.Settings.HealthBarHeight Height = CnCNew.Ui.Settings.HealthBarHeight.Normal)
{
    public static HudMarkerOptions Default { get; } = new();
}

/// <summary>Which objects get a health bar (<c>display.healthBarMode</c>).</summary>
public static class HealthBarRule
{
    /// <summary>
    /// Whether the bar of an object shows: what is selected or under the pointer always does (the original's rule, and the selection's only sign in the original style); every other object only when
    /// the viewer asked for every object (<see cref="CnCNew.Ui.Settings.HealthBarMode.AlwaysAuto"/>) or for the damaged ones (<see cref="CnCNew.Ui.Settings.HealthBarMode.AlwaysBelowFull"/>, health below its maximum).
    /// </summary>
    public static bool Shows(CnCNew.Ui.Settings.HealthBarMode mode, bool selectedOrHovered, bool belowFullHealth) => selectedOrHovered || mode switch
    {
        CnCNew.Ui.Settings.HealthBarMode.AlwaysAuto => true,
        CnCNew.Ui.Settings.HealthBarMode.AlwaysBelowFull => belowFullHealth,
        _ => false,
    };
}

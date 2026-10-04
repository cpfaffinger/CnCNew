using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
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
    private static readonly WndColor MoveOrder = new(90, 255, 120, 220);
    private static readonly WndColor AttackOrder = new(255, 90, 80, 220);
    private static readonly WndColor GhostValid = new(80, 255, 110, 255);
    private static readonly WndColor GhostInvalid = new(255, 70, 60, 255);
    private static readonly WndColor RallyColor = new(255, 235, 90, 255);
    private static readonly WndColor BarOutline = new(0, 0, 0, 255);
    private static readonly WndColor BarBack = new(0, 0, 0, 170);

    private const int RingSegments = 28;
    private const int WrenchFrames = 10;
    private const int CrossFrames = 16;

    public static ImmutableArray<UiDrawCommand> Build(HudOverlay overlay, long clockMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(overlay);
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

        foreach (var rally in overlay.RallyPoints)
        {
            var x = rally.Screen.X;
            var y = rally.Screen.Y;
            commands.Add(new UiLineDraw(x, y, x, y - 22f, 2f, RallyColor));
            commands.Add(new UiLineDraw(x, y - 22f, x + 14f, y - 17f, 2f, RallyColor));
            commands.Add(new UiLineDraw(x + 14f, y - 17f, x, y - 12f, 2f, RallyColor));
            Ring(commands, rally.Screen, 4f, 1.5f, RallyColor, 10);
        }

        foreach (var marker in overlay.Selected)
        {
            var color = marker.IsOwn ? Own : marker.IsEnemy ? Enemy : Neutral;
            // A structure is marked by corner brackets round its screen footprint (the selection radius of a building is its bounding circle, far too large for a ring).
            if (marker.IsStructure) Brackets(commands, StructureCenter(marker), StructureHalf(marker), color);
            else Ring(commands, marker.Center, MathF.Max(marker.Radius, 6f), 2f, color, RingSegments);
            HealthBar(commands, marker);
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
        }

        if (!overlay.HealIcons.IsDefaultOrEmpty)
            foreach (var icon in overlay.HealIcons)
                HealIcon(commands, icon, clockMilliseconds);

        return commands.ToImmutable();
    }

    /// <summary>The ring is a closed polyline; the bar sits above it: a dark plate, the health in a green-to-red ramp (blue-cyan is for objects under construction in the original; not tracked here).</summary>
    private static void HealthBar(ImmutableArray<UiDrawCommand>.Builder commands, UnitMarker marker)
    {
        var width = MathF.Max(26f, marker.Radius * (marker.IsStructure ? 1.1f : 1.7f));
        var left = marker.Center.X - width / 2f;
        var top = (marker.IsStructure ? StructureCenter(marker).Y - StructureHalf(marker) * 0.7f : marker.Center.Y - MathF.Max(marker.Radius, 6f)) - 11f;
        var plate = new UiRect((int)MathF.Floor(left) - 1, (int)MathF.Floor(top) - 1, (int)MathF.Ceiling(width) + 2, 7);
        commands.Add(new UiFillRect(plate, BarBack));
        commands.Add(new UiBorderRect(plate, BarOutline));
        var health = Math.Clamp(marker.HealthFraction, 0f, 1f);
        var filled = (int)MathF.Round(width * health);
        if (filled > 0) commands.Add(new UiFillRect(new UiRect((int)MathF.Floor(left), (int)MathF.Floor(top), filled, 5), HealthColor(health)));
    }

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

    private static void Brackets(ImmutableArray<UiDrawCommand>.Builder commands, Vector2 center, float half, WndColor color)
    {
        half = MathF.Max(half, 12f);
        var leg = MathF.Min(18f, half * 0.6f);
        foreach (var (sx, sy) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var cx = center.X + sx * half;
            var cy = center.Y + sy * half * 0.7f;
            commands.Add(new UiLineDraw(cx, cy, cx - sx * leg, cy, 2.5f, color));
            commands.Add(new UiLineDraw(cx, cy - sy * 1.2f, cx, cy - sy * (leg * 0.7f), 2.5f, color));
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

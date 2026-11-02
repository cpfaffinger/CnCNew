using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// The numbered start positions on a map's preview, the same in the skirmish setup and the room of a server (<c>positionStartSpots</c>, <c>updateMapStartSpots</c> of
/// <c>SkirmishGameOptionsMenu.cpp</c>, the clicks of <c>LanGameOptionsMenu.cpp</c>). The layouts have eight buttons, <c>ButtonMapStartPosition0..7</c>; a map shows as many as it has
/// places, each where its <c>Player_N_Start</c> waypoint lies on the preview, with the number of the seat that took it. A seat without a place is given a random one when the match starts.
/// </summary>
internal static class StartSpots
{
    private const int Count = 8;
    private const string Prefix = "ButtonMapStartPosition";

    /// <summary>The spot a window's name stands for, or -1.</summary>
    public static int SpotOf(string windowName)
        => windowName.StartsWith(Prefix, StringComparison.Ordinal) && int.TryParse(windowName[Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var spot) && spot < Count ? spot : -1;

    public static void Hide(ShellScreen screen)
    {
        for (var spot = 0; spot < Count; spot++) screen.HideWindow($"{Prefix}{spot}");
    }

    /// <summary>Places the spots of <paramref name="map"/> and numbers the taken ones; <paramref name="starts"/> is the place of every seat, -1 for none.</summary>
    public static void Show(ShellScreen screen, ShellMapEntry? map, bool hidden, ImmutableArray<int> starts)
    {
        if (hidden || map is not { IsMultiplayer: true } || map.Starts.IsDefaultOrEmpty)
        {
            Hide(screen);
            return;
        }
        var frame = screen.Window("MapWindow").Definition.Rect;
        var (minX, minY, maxX, maxY) = map.Extent;
        var (width, height) = (maxX - minX, maxY - minY);
        if (width <= 0 || height <= 0)
        {
            Hide(screen);
            return;
        }
        // findDrawPositions: the map keeps its proportions inside the window and stands in its middle.
        var scale = Math.Min(frame.Width / width, frame.Height / height);
        var (drawnWidth, drawnHeight) = (width * scale, height * scale);
        var (left, top) = (frame.Left + (frame.Width - drawnWidth) / 2, frame.Top + (frame.Height - drawnHeight) / 2);
        var placed = new List<(int X, int Y)>();
        for (var spot = 0; spot < Count; spot++)
        {
            var button = screen.Window($"{Prefix}{spot}");
            var exists = spot < map.Starts.Length;
            button.SetHidden(!exists);
            if (!exists) continue;
            var (sizeX, sizeY) = (button.Definition.Rect.Width, button.Definition.Rect.Height);
            var x = (int)((map.Starts[spot].X - minX) / width * drawnWidth + left) - sizeX / 2;
            var y = (int)((1 - (map.Starts[spot].Y - minY) / height) * drawnHeight + top) - sizeY / 2;
            // positionStartSpotControls: "make sure we're not on top of anyone else".
            foreach (var (otherX, otherY) in placed)
            {
                if (x <= otherX || x >= otherX + sizeX || y <= otherY || y >= otherY + sizeY) continue;
                var (closerRight, closerBottom) = (otherX + sizeX - x, otherY + sizeY - y);
                if (closerRight <= closerBottom) x = otherX + sizeX + 1;
                if (closerBottom <= closerRight) y = otherY + sizeY + 1;
            }
            placed.Add((x, y));
            button.SetDesignPosition(button.Definition.Rect with { Left = x, Top = y, Right = x + sizeX, Bottom = y + sizeY });
            var seat = starts.IndexOf(spot);
            button.SetText(seat >= 0 ? (seat + 1).ToString(CultureInfo.InvariantCulture) : string.Empty);
        }
    }

    /// <summary>
    /// <c>GBM_SELECTED</c> and <c>GBM_SELECTED_RIGHT</c> on a spot. A free spot takes the first of the person's seats without a place, or moves its own seat there. A spot held by one of its
    /// seats goes on to its next seat without a place, or is given up; a right click gives it up. Somebody else's spot stays. Returns the new place of every seat.
    /// </summary>
    /// <param name="local">The person's own seat; -1 for somebody without one.</param>
    /// <param name="host">The host decides for the computer armies as well.</param>
    /// <param name="isComputer">Whether a seat is a computer army.</param>
    public static ImmutableArray<int> Click(ImmutableArray<int> starts, int spot, int local, bool host, Func<int, bool> isComputer, bool right)
    {
        var builder = starts.ToBuilder();
        bool Mine(int seat) => seat == local || (host && isComputer(seat));
        int Next(int from)
        {
            if (!host) return local >= 0 && builder[local] < 0 ? local : -1;
            for (var seat = from; seat < builder.Count; seat++)
                if (builder[seat] < 0 && Mine(seat)) return seat;
            return -1;
        }
        var holder = starts.IndexOf(spot);
        if (right)
        {
            if (holder >= 0 && Mine(holder)) builder[holder] = -1;
            return builder.ToImmutable();
        }
        if (holder >= 0)
        {
            if (!Mine(holder)) return builder.ToImmutable();
            var next = Next(holder + 1);
            builder[holder] = -1;
            if (next >= 0) builder[next] = spot;
            return builder.ToImmutable();
        }
        var taker = Next(0);
        if (taker < 0) taker = local;
        if (taker >= 0 && taker < builder.Count) builder[taker] = spot;
        return builder.ToImmutable();
    }
}

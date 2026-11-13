using System.Numerics;
using CnCNew.Data;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Presentation.Minimap;

/// <summary>One edge of a radar event's triangle in pixels of the drawn radar (0,0 its top-left corner), shaded from <see cref="Start"/> to <see cref="End"/>.</summary>
public readonly record struct RadarEventEdge(Vector2 From, Vector2 To, Rgba32 Start, Rgba32 End);

/// <summary>
/// How the original draws a radar event over the radar (<c>W3DRadar::drawSingleGenericEvent</c>, <c>W3DRadar::drawSingleBeaconEvent</c>, rewritten): a triangle round the event's place
/// that is largest when the event begins and shrinks to its smallest size over 1.5 seconds while it turns once round, then keeps turning at that size; every edge is a line from the
/// event's first colour to its second (<c>radarColorLookupTable</c>), and over the last half second of its life (<c>Radar::internalCreateEvent</c>, <c>secondsBeforeDieToFade</c>) both
/// colours fade to nothing. A beacon's pulse is a smaller triangle (a tenth of the radar's width instead of a half) that turns the other way.
/// <para>
/// The sizes are those of the original, which mixes two units: the largest size is half (a tenth) of the drawn radar's width in screen pixels, but it is added to the event's place in
/// radar cells (<c>RADAR_CELL_WIDTH</c> = 128 across the map) and only then scaled to the screen (<c>W3DRadar::radarToPixel</c>), so a radar drawn wider starts its triangles larger
/// than the radar and they come into view as they shrink; the clip to the radar's window cuts them. The smallest size is six radar cells. The place itself is the map position
/// the minimap draws at (<see cref="MinimapImage.WorldToPixel"/>) instead of the original's whole radar cell, and the corners are not rounded to whole cells and pixels.
/// </para>
/// Time is whole frames of the original (1/30 s) since the event was made, as the original's <c>TheGameLogic->getFrame() - createFrame</c>, so the picture is the same at every rate.
/// </summary>
public static class RadarEventMarker
{
    /// <summary><c>RADAR_CELL_WIDTH</c>, <c>RADAR_CELL_HEIGHT</c>.</summary>
    public const int RadarCells = 128;

    /// <summary><c>TIME_FROM_FULL_SIZE_TO_SMALL_SIZE</c>: <c>LOGICFRAMES_PER_SECOND * 1.5</c>.</summary>
    public const int FramesToSmallest = OriginalFrame.PerSecond * 3 / 2;

    /// <summary><c>minEventSize</c>, in radar cells.</summary>
    public const int SmallestSize = 6;

    /// <summary><c>secondsBeforeDieToFade</c> (0.5 s) in frames of the original.</summary>
    public const int FadeFrames = OriginalFrame.PerSecond / 2;

    /// <summary><c>Radar::createEvent</c>'s default life of four seconds, in frames of the original.</summary>
    public const int DefaultLifeFrames = 4 * OriginalFrame.PerSecond;

    /// <summary>
    /// The three edges of an event made <paramref name="ageFrames"/> frames of the original ago that lives <paramref name="lifeFrames"/> frames, or none when it is not (yet or any more)
    /// active (<c>Radar::update</c> ends it in the first frame after its <c>dieFrame</c>). <paramref name="place"/> is its place in pixels of the drawn radar and <paramref name="drawnSize"/>
    /// the drawn radar's width and height in screen pixels.
    /// </summary>
    public static RadarEventEdge[] Edges(Vector2 place, Vector2 drawnSize, long ageFrames, long lifeFrames, bool beaconPulse, Rgba32 color1, Rgba32 color2)
    {
        if (ageFrames < 0 || ageFrames > lifeFrames) return [];
        var maxSize = drawnSize.X / (beaconPulse ? 10f : 2f);
        var progress = ageFrames / (float)FramesToSmallest;
        // REAL_TO_INT truncates; a size below the smallest is the smallest.
        var size = Math.Max(SmallestSize, (int)(maxSize * (1f - progress)));
        // The generic event turns one way, the beacon's pulse the other (drawSingleBeaconEvent negates the added angle).
        var turn = 2f * MathF.PI * progress * (beaconPulse ? -1f : 1f);
        Span<Vector2> corners = stackalloc Vector2[3];
        ReadOnlySpan<float> angles = [0f, 2f * MathF.PI / 3f, -2f * MathF.PI / 3f];
        for (var corner = 0; corner < 3; corner++)
        {
            var angle = angles[corner] - turn;
            // Radar cells to pixels: x to the right, y upwards on the radar and downwards on the screen (radarToPixel's "inverted" y).
            corners[corner] = place + new Vector2(MathF.Cos(angle) * size * drawnSize.X / RadarCells, -MathF.Sin(angle) * size * drawnSize.Y / RadarCells);
        }
        var start = Faded(color1, ageFrames, lifeFrames);
        var end = Faded(color2, ageFrames, lifeFrames);
        return
        [
            new RadarEventEdge(corners[0], corners[1], start, end),
            new RadarEventEdge(corners[1], corners[2], start, end),
            new RadarEventEdge(corners[2], corners[0], start, end),
        ];
    }

    /// <summary>The colour's alpha after <c>fadeFrame</c> (<c>dieFrame - 15</c>): the share of the fade still left, truncated to a byte (<c>REAL_TO_UNSIGNEDBYTE</c>).</summary>
    public static Rgba32 Faded(Rgba32 color, long ageFrames, long lifeFrames)
    {
        var fadeStart = lifeFrames - FadeFrames;
        if (ageFrames <= fadeStart) return color;
        var span = lifeFrames - fadeStart;
        var left = 1f - (ageFrames - fadeStart) / (float)span;
        return color with { A = (byte)Math.Clamp((int)(color.A * left), 0, 255) };
    }
}

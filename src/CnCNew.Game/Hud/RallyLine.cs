using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.Game.Hud;

/// <summary>
/// The line a selected producer of the local player shows to its rally point (<c>W3DWaypointBuffer::drawWaypoints</c>, the branch outside waypoint mode): from the door
/// (<c>getExitPosition</c>) to the natural rally point (<c>getNaturalRallyPoint</c> without its offset), round one or two corners of the building's footprint when the rally point lies
/// behind the door, and on to the rally point. A node marker (the original's <c>SCMNode</c>) stands at every corner it goes round and at the natural rally point. When door and natural
/// rally point are the same place (an airfield's helipad) the line runs straight from there.
/// </summary>
public static class RallyLine
{
    /// <summary>What the original takes for "far enough out" and its rough test of whether the line needs to go round the building at all.</summary>
    private const float WayOutScale = 99999.9f;
    private const float WrapSlack = 100f;

    public readonly record struct Shape(ImmutableArray<Vector3> Points, ImmutableArray<Vector3> Nodes);

    /// <summary>
    /// The points of the line and its nodes in world space. <paramref name="center"/>, <paramref name="angle"/>, <paramref name="majorRadius"/> and <paramref name="minorRadius"/> are the
    /// producer's position, heading and geometry (the corners it may go round).
    /// </summary>
    public static Shape Compute(Vector3 exit, Vector3 natural, Vector3 rally, Vector3 center, float angle, float majorRadius, float minorRadius)
    {
        var points = ImmutableArray.CreateBuilder<Vector3>();
        var nodes = ImmutableArray.CreateBuilder<Vector3>();
        points.Add(exit);
        var boxWrap = natural != exit;
        if (boxWrap) points.Add(natural);

        if (boxWrap)
        {
            var delta = new Vector3(natural.X - exit.X, natural.Y - exit.Y, 0f);
            var wayOut = Normalized(delta) * WayOutScale;
            var wayOutLength = wayOut.Length();
            var wayOutPoint = wayOut + natural;
            // A rally point well out in front of the door needs no wrapping.
            if (WrapSlack + (wayOutPoint - rally).Length() > wayOutLength)
            {
                var outward = Normalized(wayOutPoint);
                var naturalToRally = Normalized(natural - rally);
                if (naturalToRally.X * outward.X + naturalToRally.Y * outward.Y > 0f)
                {
                    var (c, s) = (MathF.Cos(angle), MathF.Sin(angle));
                    var (exc, eyc, exs, eys) = (majorRadius * c, minorRadius * c, majorRadius * s, minorRadius * s);
                    Span<Vector2> corners =
                    [
                        new(center.X - exc - eys, center.Y + eyc - exs),
                        new(center.X + exc - eys, center.Y + eyc + exs),
                        new(center.X + exc + eys, center.Y - eyc + exs),
                        new(center.X - exc + eys, center.Y - eyc - exs),
                    ];
                    int near = -1, far = -1;
                    var (nearDistance, farDistance) = (WayOutScale, WayOutScale);
                    for (var index = 0; index < corners.Length; index++)
                    {
                        var corner = corners[index];
                        var toExit = Normalized(new Vector3(exit.X - corner.X, exit.Y - corner.Y, 0f));
                        var distance = new Vector2(rally.X - corner.X, rally.Y - corner.Y).Length();
                        // A corner on the door's side is a candidate for the first bend, one on the far side for the second.
                        if (toExit.X * outward.X + toExit.Y * outward.Y < 0f)
                        {
                            if (distance < nearDistance) (nearDistance, near) = (distance, index);
                        }
                        else if (distance < farDistance) (farDistance, far) = (distance, index);
                    }
                    if (near >= 0)
                    {
                        var first = new Vector3(corners[near].X, corners[near].Y, center.Z);
                        nodes.Add(first);
                        points.Add(first);
                        if (far >= 0)
                        {
                            var firstLeg = Normalized(new Vector3(natural.X - first.X, natural.Y - first.Y, 0f));
                            var firstToRally = Normalized(new Vector3(first.X - rally.X, first.Y - rally.Y, 0f));
                            // The rally point lies behind the first corner too: the line goes round the second.
                            if (firstToRally.X * firstLeg.X + firstToRally.Y * firstLeg.Y < 0f)
                            {
                                var second = new Vector3(corners[far].X, corners[far].Y, center.Z);
                                nodes.Add(second);
                                points.Add(second);
                            }
                        }
                    }
                }
            }
        }

        points.Add(rally);
        nodes.Add(natural);
        return new Shape(points.ToImmutable(), nodes.ToImmutable());
    }

    /// <summary><c>Coord3D::normalize</c>: a vector of no length stays as it is.</summary>
    private static Vector3 Normalized(Vector3 value)
    {
        var length = value.Length();
        return length != 0f ? value / length : value;
    }
}

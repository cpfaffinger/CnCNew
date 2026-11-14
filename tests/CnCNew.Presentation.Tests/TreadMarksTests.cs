using CnCNew.Presentation.Fx;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The track marks of tank-like vehicles (issue 323; <c>W3DTerrainTracks.cpp</c>): an edge for every 10 units travelled, a transparent start, fading with age, at most MaxEdges
/// edges with the older ones fading out, a cap that ends a stretch, a limit of tracks.
/// </summary>
internal static class TreadMarksTests
{
    public static int Run(string[] args)
    {
        var t = new TestHarness("TreadMarks");
        t.Run("marks are laid by the distance travelled, across the heading, above the ground", Distance);
        t.Run("the first edges are transparent, the rest opaque, and every other edge has the other texture coordinates", Opacity);
        t.Run("marks fade with age and are released when gone", Fading);
        t.Run("the ring holds at most MaxEdges and the oldest of them fade out first", Limit);
        t.Run("a cap ends the stretch with a transparent edge, and the track begins again apart from it", Cap);
        t.Run("a flight restarts the track transparent", Airborne);
        return t.Failures;
    }

    private static readonly TreadSettings Small = new(MaxEdges: 10, OpaqueEdges: 4, FadeSeconds: 10.0);
    private static float Flat(float x, float y) => 5f;

    private static TreadTrack Track(TreadSettings? settings = null) => new(settings ?? new TreadSettings(), "EXTnkTrack.tga", 14f);

    private static void Distance()
    {
        var track = Track();
        track.AddEdge(0f, 0f, Flat, 0);
        Assert(track.ActiveEdges == 0, "the first point is only the anchor");
        for (var step = 1; step <= 5; step++) track.AddEdge(step * 4f, 0f, Flat, 0);
        // 4, 8, 12 (the first edge: 12 >= 10 from the anchor), 16, 20 (8 from it: none), 24 (12: second edge)
        Assert(track.ActiveEdges == 1, $"20 units laid {track.ActiveEdges} edges (one so far: 12; 16 and 20 are within 10 of it... 20 is 8 from 12)");
        track.AddEdge(40f, 0f, Flat, 0);
        track.AddEdge(60f, 0f, Flat, 0);
        var strip = track.Snapshot();
        Assert(strip is { Edges.Length: 3 }, $"three edges ({strip?.Edges.Length})");
        var edge = strip!.Edges[2];
        Assert(MathF.Abs(edge.Left.Y - edge.Right.Y - 14f) < 0.01f || MathF.Abs(MathF.Abs(edge.Left.Y - edge.Right.Y) - 14f) < 0.01f, $"the edge is 14 wide across a heading along x ({edge.Left}, {edge.Right})");
        Assert(MathF.Abs(edge.Left.X - edge.Right.X) < 0.01f, "the edge stands across the heading");
        Assert(MathF.Abs(edge.Left.Z - 7f) < 0.01f && MathF.Abs(edge.Right.Z - 7f) < 0.01f, $"two units above the ground (5): {edge.Left.Z}");
    }

    private static void Opacity()
    {
        var track = Track();
        for (var step = 0; step <= 6; step++) track.AddEdge(step * 12f, 0f, Flat, 0);
        var edges = track.Snapshot()!.Edges;
        Assert(edges.Length == 6, $"six edges ({edges.Length})");
        Assert(edges[0].Alpha == 0f && edges[1].Alpha == 0f, "the track begins transparent (the anchor was a start, and the first edge too)");
        Assert(edges[2].Alpha == 1f && edges[5].Alpha == 1f, "then opaque");
        Assert(edges[0].LeftUv != edges[1].LeftUv && edges[0].LeftUv == edges[2].LeftUv, "every other edge has the other texture coordinates");
    }

    private static void Fading()
    {
        var track = Track(Small);
        for (var step = 0; step <= 8; step++) track.AddEdge(step * 12f, 0f, Flat, step);
        track.Fade(8.0);
        var edges = track.Snapshot()!.Edges;
        var last = edges[^1];
        Assert(last.Alpha == 1f, $"the newest edge, added at 8 s, has not aged at 8 s ({last.Alpha})");
        track.Fade(13.0);
        var aged = track.Snapshot()!.Edges[^1];
        Assert(MathF.Abs(aged.Alpha - 0.5f) < 0.01f, $"five of ten seconds old is half as opaque ({aged.Alpha})");
        track.Fade(100.0);
        Assert(track.ActiveEdges == 0 && track.Snapshot() is null, "everything fades away");
    }

    private static void Limit()
    {
        var track = Track(Small);
        for (var step = 0; step <= 30; step++) track.AddEdge(step * 12f, 0f, Flat, 0);
        var edges = track.Snapshot()!.Edges;
        Assert(edges.Length == 10 && track.ActiveEdges == 10, $"the ring holds ten edges ({edges.Length})");
        Assert(edges[^1].Alpha == 1f && edges[^4].Alpha == 1f, "the newest four stay opaque");
        Assert(edges[^5].Alpha < 1f && edges[0].Alpha < edges[^5].Alpha, $"the older ones fade out towards the oldest ({edges[^5].Alpha}, {edges[0].Alpha})");
        Assert(edges[0].Left.X > 100f, "the oldest were released");
    }

    private static void Cap()
    {
        var track = Track();
        for (var step = 0; step <= 6; step++) track.AddEdge(step * 12f, 0f, Flat, 0);
        var before = track.ActiveEdges;
        track.AddCap(100f, 0f, Flat, 0);
        Assert(track.ActiveEdges == before + 1 && track.Snapshot()!.Edges[^1].Alpha == 0f, "the cap is a transparent edge");
        track.AddCap(110f, 0f, Flat, 0);
        Assert(track.ActiveEdges == before + 1, "a second cap adds nothing");
        // Resuming: the first point after the cap is an anchor again, the next edge starts transparent.
        track.AddEdge(500f, 0f, Flat, 0);
        Assert(track.ActiveEdges == before + 1, "an anchor lays no edge");
        track.AddEdge(520f, 0f, Flat, 0);
        Assert(track.ActiveEdges == before + 2 && track.Snapshot()!.Edges[^1].Alpha == 0f, "the track begins again transparent");
    }

    private static void Airborne()
    {
        var track = Track();
        for (var step = 0; step <= 6; step++) track.AddEdge(step * 12f, 0f, Flat, 0);
        track.SetAirborne();
        track.AddEdge(100f, 0f, Flat, 0);
        Assert(track.Snapshot()!.Edges[^1].Alpha == 0f, "an edge after a flight is transparent");
        track.AddEdge(120f, 0f, Flat, 0);
        Assert(track.Snapshot()!.Edges[^1].Alpha == 1f, "and the next is opaque again");
    }
}

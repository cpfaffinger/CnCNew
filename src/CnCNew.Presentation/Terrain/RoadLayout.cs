using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;

namespace CnCNew.Presentation.Terrain;

/// <summary>What a road segment of the layout draws (<c>TCorner</c> in <c>W3DRoadBuffer.h</c>), in the reference's draw order.</summary>
public enum RoadPiece
{
    Segment,
    Curve,
    Tee,
    FourWay,
    ThreeWayY,
    ThreeWayH,
    ThreeWayHFlip,
    AlphaJoin,
}

/// <summary>One end of a road segment (<c>TRoadPt</c>). Mutable and shared by reference inside the layout, like the reference's pointers into its segment array.</summary>
internal sealed class RoadPt
{
    public Vector2 Loc;
    public Vector2 Top;
    public Vector2 Bottom;
    public int Count;
    public bool Last;
    public bool Multi;
    public bool IsAngled;
    public bool IsJoin;

    public RoadPt Clone() => (RoadPt)MemberwiseClone();
}

/// <summary>One road segment of the layout (<c>RoadSegment</c>) together with the triangles its piece draws.</summary>
internal sealed class RoadSeg
{
    public RoadPt P1 = new();
    public RoadPt P2 = new();
    public float CurveRadius;
    public RoadPiece Type;
    public float Scale = 1f;
    public float WidthInTexture = 1f;
    public int UniqueId;

    public Vector3[] Positions = [];
    public Vector2[] Uvs = [];
    public int[] Indices = [];

    public RoadSeg Clone()
    {
        var copy = (RoadSeg)MemberwiseClone();
        copy.P1 = P1.Clone();
        copy.P2 = P2.Clone();
        return copy;
    }

    public void Flip() => (P1, P2) = (P2, P1);
}

/// <summary>Counters of one <see cref="RoadLayout"/> run; every road the reference would have dropped or could not place is counted here, never ignored.</summary>
internal sealed class RoadLayoutCounters
{
    public int MapSegments;
    public int DuplicateSegments;
    public int Curves;
    public int Mitres;
    public int CurvesAborted;
    public int Tees;
    public int FourWays;
    public int YJunctions;
    public int SlantedTees;
    public int AlphaJoins;
    public int CrossTypeJoins;
    public int DroppedBySegmentBudget;
    public int UnmatchedPoint1;
}

/// <summary>
/// The topology of the roads of one map, ported from <c>W3DRoadBuffer</c> (<c>loadRoads</c>, <c>addMapObjects</c>, <c>addMapObject</c>, <c>updateCountsAndFlags</c>,
/// <c>insertTeeIntersections</c> with <c>insertTee</c>/<c>insertY</c>/<c>insert4Way</c>, <c>insertCurveSegments</c>/<c>insertCurveSegmentAt</c>/<c>miter</c> and
/// <c>insertCrossTypeJoins</c>): map road point pairs become segments, segments that meet end to end are chained, three meeting segments become a tee (or a Y,
/// or an H for a slanted arm), four a crossing, chained segments with a corner get curve pieces or a mitre, and open ends flagged as joins get an alpha join. The
/// geometry of the pieces is <see cref="RoadGeometry"/>. Everything is plain float arithmetic on <see cref="Vector2"/>, deterministic for equal input.
/// </summary>
internal sealed class RoadLayout
{
    internal const float TeeWidthAdjustment = 1.03f;
    internal const float CornerRadius = 1.5f;
    internal const float TightCornerRadius = 0.5f;
    internal const float DefaultRoadScale = 8f;
    internal const float MinRoadSegment = 0.25f;
    private const float HalfPi = MathF.PI / 2f;

    private readonly List<RoadSeg> _roads = [];
    private readonly RoadCatalog _catalog;
    private readonly Dictionary<int, int> _stacking = [];

    public RoadLayout(RoadCatalog catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<RoadSeg> Roads => _roads;

    public RoadLayoutCounters Counters { get; } = new();

    /// <summary>Draw order of a road type: 0 first, higher on top (<c>RoadType::m_stackingOrder</c>).</summary>
    public int StackingOf(int uniqueId) => _stacking.GetValueOrDefault(uniqueId);

    /// <summary>Template names that map road points name but <see cref="RoadCatalog"/> does not define, with the number of segments.</summary>
    public SortedDictionary<string, int> UnknownTemplates { get; } = new(StringComparer.Ordinal);

    /// <summary><c>W3DRoadBuffer::loadRoads</c> up to the segment geometry (that is <see cref="RoadGeometry"/>).</summary>
    public void Build(ImmutableArray<MapObject> objects)
    {
        AddMapObjects(objects);
        UpdateCountsAndFlags();
        InsertTeeIntersections();
        InsertCurveSegments();
        InsertCrossTypeJoins();
    }

    // ------------------------------------------------------------------ math helpers (WWMath semantics)

    private static Vector2 Normalized(Vector2 value)
    {
        var length2 = value.LengthSquared();
        return length2 != 0f ? value * (1f / MathF.Sqrt(length2)) : value;
    }

    /// <summary><c>Vector2::Rotate</c>: counter-clockwise by <paramref name="theta"/> radians.</summary>
    internal static Vector2 Rotate(Vector2 value, float theta)
    {
        var s = MathF.Sin(theta);
        var c = MathF.Cos(theta);
        return new Vector2(value.X * c + value.Y * -s, value.X * s + value.Y * c);
    }

    /// <summary><c>xpSign</c>: -1 when v2 lies to the right of v1, 1 to the left, 0 when parallel.</summary>
    private static int XpSign(Vector2 v1, Vector2 v2)
    {
        var cross = v1.X * v2.Y - v1.Y * v2.X;
        return cross < 0f ? -1 : cross > 0f ? 1 : 0;
    }

    private static float CrossZ(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>A <c>LineSegClass</c> in the z = 0 plane.</summary>
    private readonly struct LineSeg
    {
        public readonly Vector3 P0;
        public readonly Vector3 Dir;

        public LineSeg(Vector2 p0, Vector2 p1)
        {
            P0 = new Vector3(p0, 0f);
            var dp = new Vector3(p1, 0f) - P0;
            var length2 = dp.LengthSquared();
            Dir = length2 != 0f ? dp * (1f / MathF.Sqrt(length2)) : dp;
        }

        public LineSeg(Vector3 p0, Vector3 p1)
            : this(new Vector2(p0.X, p0.Y), new Vector2(p1.X, p1.Y))
        {
        }

        /// <summary><c>LineSegClass::Find_Intersection</c>: the closest point on this infinite line to <paramref name="other"/>'s infinite line; false when parallel.</summary>
        public bool FindIntersection(LineSeg other, out Vector3 point)
        {
            var cross1 = Vector3.Cross(Dir, other.Dir);
            var cross2 = Vector3.Cross(other.P0 - P0, other.Dir);
            var top1 = Vector3.Dot(cross2, cross1);
            var bottom1 = Vector3.Dot(cross1, cross1);
            var cross3 = Vector3.Cross(other.Dir, Dir);
            var cross4 = Vector3.Cross(P0 - other.P0, Dir);
            var bottom2 = Vector3.Dot(cross3, cross3);
            if (bottom1 != 0f && bottom2 != 0f)
            {
                point = P0 + Dir * (top1 / bottom1);
                return true;
            }
            point = default;
            return false;
        }

        public Vector3 FindPointClosestTo(Vector3 position, float length)
        {
            var dot = Vector3.Dot(Dir, position - P0);
            if (dot <= 0f) return P0;
            if (dot >= length) return P0 + Dir * length;
            return P0 + dot * Dir;
        }
    }

    // ------------------------------------------------------------------ map objects to segments

    private void AddMapObjects(ImmutableArray<MapObject> objects)
    {
        for (var index = 0; index < objects.Length; index++)
        {
            if (_roads.Count >= _catalog.MaxSegments)
            {
                // The reference stops here and never mentions the roads it leaves out; they are counted.
                for (var rest = index; rest < objects.Length; rest++)
                    if ((objects[rest].Flags & MapObject.FlagRoadPoint1) != 0) Counters.DroppedBySegmentBudget++;
                break;
            }
            var first = objects[index];
            if ((first.Flags & MapObject.FlagRoadPoint1) == 0) continue;
            if (index + 1 >= objects.Length)
            {
                Counters.UnmatchedPoint1++;
                break;
            }
            var second = objects[index + 1];
            if ((second.Flags & MapObject.FlagRoadPoint2) == 0)
            {
                Counters.UnmatchedPoint1++;
                continue;
            }
            index++; // pMapObj = pMapObj2: both points of the pair are consumed.
            if (!_catalog.TryGet(first.Name, out var template))
            {
                UnknownTemplates[first.Name] = UnknownTemplates.GetValueOrDefault(first.Name) + 1;
                continue;
            }
            var loc1 = new Vector2((float)first.X, (float)first.Y);
            var loc2 = new Vector2((float)second.X, (float)second.Y);
            if (loc1.X == loc2.X && loc1.Y == loc2.Y) loc2.X += 0.25f;
            var road = new RoadSeg
            {
                Scale = template.Width,
                WidthInTexture = template.WidthInTexture,
                UniqueId = template.Id,
                Type = RoadPiece.Segment,
                CurveRadius = (first.Flags & MapObject.FlagRoadCornerTight) != 0 ? TightCornerRadius : CornerRadius,
            };
            road.P1.Loc = loc1;
            road.P1.IsAngled = (first.Flags & MapObject.FlagRoadCornerAngled) != 0;
            road.P1.IsJoin = (first.Flags & MapObject.FlagRoadJoin) != 0;
            road.P2.Loc = loc2;
            road.P2.IsAngled = (second.Flags & MapObject.FlagRoadCornerAngled) != 0;
            road.P2.IsJoin = (second.Flags & MapObject.FlagRoadJoin) != 0;
            Counters.MapSegments++;
            AddMapObject(road, true);
        }
        // Second pass: re-add every segment in order so chained segments end up next to each other with their final counts.
        var copy = _roads.Select(segment => segment.Clone()).ToList();
        _roads.Clear();
        foreach (var segment in copy) AddMapObject(segment, false);
    }

    private void AddMapObject(RoadSeg source, bool updateCounts)
    {
        var cur = source.Clone();
        var loc1 = cur.P1.Loc;
        var loc2 = cur.P2.Loc;
        var roadNormal = Normalized(new Vector2(-(loc2.Y - loc1.Y), loc2.X - loc1.X)) * (cur.Scale * cur.WidthInTexture / 2f);
        cur.P1.Top = loc1 + roadNormal;
        cur.P1.Bottom = loc1 - roadNormal;
        cur.P2.Top = loc2 + roadNormal;
        cur.P2.Bottom = loc2 - roadNormal;
        if (updateCounts) UpdateCounts(cur);
        if (!CheckSegments()) return;

        var flip = false;
        var addBefore = false;
        var addAfter = false;
        var bothMatch = false;
        int i;
        for (i = 0; i < _roads.Count; i++)
        {
            bothMatch = false;
            var other = _roads[i];
            if ((other.P1.Loc == loc1 && other.P2.Loc == loc2) || (other.P1.Loc == loc2 && other.P2.Loc == loc1))
            {
                bothMatch = true; // identical segment, discarded.
                break;
            }
            if (cur.P1.Count == 1)
            {
                if (other.P1.Loc == loc1)
                {
                    flip = true;
                    addAfter = true;
                }
                if (other.P2.Loc == loc1)
                {
                    flip = false;
                    addBefore = true;
                }
            }
            if (cur.P2.Count == 1)
            {
                if (other.P1.Loc == loc2)
                {
                    flip = false;
                    addAfter = true;
                }
                if (other.P2.Loc == loc2)
                {
                    flip = true;
                    addBefore = true;
                }
            }
            if (addBefore || addAfter) break;
        }
        if (bothMatch)
        {
            Counters.DuplicateSegments++;
            return;
        }
        var addIndex = addAfter ? i + 1 : i;
        _roads.Insert(addIndex, cur);
        if (flip) cur.Flip();
        if (addBefore) CheckLinkBefore(addIndex);
        else if (addAfter) CheckLinkAfter(addIndex);
    }

    private bool CheckSegments()
    {
        if (_roads.Count < _catalog.MaxSegments) return true;
        Counters.DroppedBySegmentBudget++;
        return false;
    }

    private void MoveRoadSegTo(int from, int to)
    {
        if (from < 0 || from >= _roads.Count || to < 0 || to >= _roads.Count || from == to) return;
        var segment = _roads[from];
        _roads.RemoveAt(from);
        _roads.Insert(to, segment);
    }

    private void CheckLinkBefore(int ndx)
    {
        if (_roads[ndx].P2.Count != 1) return;
        var loc2 = _roads[ndx].P2.Loc;
        var endOfCurSeg = ndx + 1;
        while (endOfCurSeg < _roads.Count - 1)
        {
            if (_roads[endOfCurSeg].P1.Loc != _roads[endOfCurSeg + 1].P2.Loc) break;
            endOfCurSeg++;
        }
        var checkNdx = endOfCurSeg + 1;
        while (checkNdx < _roads.Count)
        {
            if (_roads[checkNdx].P1.Loc == loc2)
            {
                MoveRoadSegTo(checkNdx, ndx);
                loc2 = _roads[ndx].P2.Loc;
                if (_roads[ndx].P2.Count != 1) return;
                endOfCurSeg++;
            }
            else if (_roads[checkNdx].P2.Loc == loc2)
            {
                _roads[checkNdx].Flip();
                MoveRoadSegTo(checkNdx, ndx);
                loc2 = _roads[ndx].P2.Loc;
                if (_roads[ndx].P2.Count != 1) return;
                endOfCurSeg++;
            }
            else
            {
                checkNdx++;
            }
            if (checkNdx <= endOfCurSeg) checkNdx = endOfCurSeg + 1;
        }
    }

    private void CheckLinkAfter(int ndx)
    {
        if (_roads[ndx].P1.Count != 1) return;
        if (ndx >= _roads.Count - 1) return;
        var loc1 = _roads[ndx].P1.Loc;
        var checkNdx = ndx + 1;
        while (checkNdx < _roads.Count && ndx < _roads.Count - 1)
        {
            if (_roads[checkNdx].P2.Loc == loc1)
            {
                ndx++;
                MoveRoadSegTo(checkNdx, ndx);
                loc1 = _roads[ndx].P1.Loc;
                if (_roads[ndx].P1.Count != 1) return;
            }
            else if (_roads[checkNdx].P1.Loc == loc1)
            {
                _roads[checkNdx].Flip();
                ndx++;
                MoveRoadSegTo(checkNdx, ndx);
                loc1 = _roads[ndx].P1.Loc;
                if (_roads[ndx].P1.Count != 1) return;
            }
            else
            {
                checkNdx++;
            }
        }
    }

    /// <summary><c>updateCounts</c>: counts the same-type segments that already end where the new one does, on both sides.</summary>
    private void UpdateCounts(RoadSeg road)
    {
        road.P1.Last = true;
        road.P2.Last = true;
        road.P1.Multi = false;
        road.P2.Multi = false;
        road.P1.Count = 0;
        road.P2.Count = 0;
        var loc1 = road.P1.Loc;
        var loc2 = road.P2.Loc;
        foreach (var other in _roads)
        {
            if (other.UniqueId != road.UniqueId) continue; // only connect the same type of road.
            if (other.P1.Loc == loc1) { other.P1.Count++; road.P1.Count++; }
            if (other.P1.Loc == loc2) { other.P1.Count++; road.P2.Count++; }
            if (other.P2.Loc == loc1) { other.P2.Count++; road.P1.Count++; }
            if (other.P2.Loc == loc2) { other.P2.Count++; road.P2.Count++; }
            other.P1.Multi = other.P1.Count > 1;
            other.P2.Multi = other.P2.Count > 1;
        }
        road.P1.Multi = road.P1.Count > 1;
        road.P2.Multi = road.P2.Count > 1;
    }

    /// <summary><c>updateCountsAndFlags</c>: the number of other same-type segments that meet each end point (1 = chain, 2 = tee, 3 = crossing).</summary>
    private void UpdateCountsAndFlags()
    {
        foreach (var road in _roads)
        {
            road.P1.Last = true;
            road.P2.Last = true;
            road.P1.Count = 0;
            road.P2.Count = 0;
        }
        for (var j = _roads.Count - 1; j > 0; j--)
        {
            var loc1 = _roads[j].P1.Loc;
            var loc2 = _roads[j].P2.Loc;
            for (var i = 0; i < j; i++)
            {
                if (_roads[i].UniqueId != _roads[j].UniqueId) continue;
                if (_roads[i].P1.Loc == loc1) { _roads[i].P1.Last = false; _roads[i].P1.Count++; _roads[j].P1.Count++; }
                if (_roads[i].P1.Loc == loc2) { _roads[i].P1.Last = false; _roads[i].P1.Count++; _roads[j].P2.Count++; }
                if (_roads[i].P2.Loc == loc1) { _roads[i].P2.Last = false; _roads[i].P2.Count++; _roads[j].P1.Count++; }
                if (_roads[i].P2.Loc == loc2) { _roads[i].P2.Last = false; _roads[i].P2.Count++; _roads[j].P2.Count++; }
            }
        }
    }

    // ------------------------------------------------------------------ tees, Ys, H shapes, crossings

    private void InsertTeeIntersections()
    {
        var numRoadSegments = _roads.Count;
        for (var i = 0; i < numRoadSegments; i++)
        {
            if (_roads[i].Type != RoadPiece.Segment) continue;
            if (_roads[i].P1.Count == 2) InsertTee(_roads[i].P1.Loc, i, _roads[i].Scale);
            if (_roads[i].P2.Count == 2) InsertTee(_roads[i].P2.Loc, i, _roads[i].Scale);
            if (_roads[i].P1.Count == 3) Insert4Way(_roads[i].P1.Loc, i, _roads[i].Scale);
            if (_roads[i].P2.Count == 3) Insert4Way(_roads[i].P2.Loc, i, _roads[i].Scale);
        }
    }

    /// <summary>Gathers the other segment ends at <paramref name="loc"/> after <paramref name="index1"/> (marking them consumed) in the order the reference finds them.</summary>
    private List<(RoadPt Far, RoadPt Center)> OtherEndsAt(Vector2 loc, int index1)
    {
        var ends = new List<(RoadPt, RoadPt)>();
        for (var i = index1 + 1; i < _roads.Count; i++)
        {
            if (_roads[i].P1.Loc == loc)
            {
                _roads[i].P1.Count = -2;
                ends.Add((_roads[i].P2, _roads[i].P1));
            }
            if (_roads[i].P2.Loc == loc)
            {
                _roads[i].P2.Count = -2;
                ends.Add((_roads[i].P1, _roads[i].P2));
            }
        }
        return ends;
    }

    private void InsertTee(Vector2 loc, int index1, float scale)
    {
        if (InsertY(loc, index1, scale)) return;
        RoadPt pr1, pc1;
        if (_roads[index1].P1.Loc == loc) { pr1 = _roads[index1].P2; pc1 = _roads[index1].P1; }
        else { pr1 = _roads[index1].P1; pc1 = _roads[index1].P2; }
        RoadPt? pr2 = null, pr3 = null, pc2 = null, pc3 = null;
        foreach (var (far, center) in OtherEndsAt(loc, index1))
        {
            if (pr2 is null) { pr2 = far; pc2 = center; }
            else { pr3 = far; pc3 = center; }
        }
        if (pr2 is null || pr3 is null) return;

        var v1 = Normalized(pr1.Loc - loc);
        var v2 = Normalized(pr2.Loc - loc);
        var v3 = Normalized(pr3.Loc - loc);
        var dot12 = Vector2.Dot(v1, v2);
        var dot13 = Vector2.Dot(v1, v3);
        var dot32 = Vector2.Dot(v3, v2);
        // The most negative dot product is the pair heading most opposite to each other.
        bool do12 = false, do13 = false, do32 = false;
        if (dot12 < dot13)
        {
            if (dot12 < dot32) do12 = true; else do32 = true;
        }
        else
        {
            if (dot13 < dot32) do13 = true; else do32 = true;
        }

        Vector2 upVector = default, decider = default;
        if (do12) { upVector = v2 - v1; decider = v3; }
        if (do13) { upVector = v3 - v1; decider = v2; }
        if (do32) { upVector = v2 - v3; decider = v1; }
        upVector = Normalized(upVector);

        var index = _roads[index1];
        const float cos60 = 0.5f;
        var dot = MathF.Abs(Vector2.Dot(upVector, decider));
        var angle = HalfPi;
        var xpdct = CrossZ(upVector, decider);
        if (dot > cos60)
        {
            // The arm of the tee is slanted: an H shaped piece.
            var mirror = false;
            if (xpdct < 0f)
            {
                angle = -angle;
                mirror = true;
            }
            upVector = Normalized(upVector) * (0.5f * scale);
            var teeVector = Rotate(upVector, angle);
            bool flip;
            if (do12)
            {
                flip = XpSign(teeVector, v3) == 1;
                OffsetH(pc1, pc2!, pc3!, loc, upVector, teeVector, flip, mirror, index.WidthInTexture);
                Counters.SlantedTees++;
                FinishJunction(pc1, pc2!, pc3!, loc, teeVector, index, flip ? RoadPiece.ThreeWayHFlip : RoadPiece.ThreeWayH, -3);
            }
            else if (do13)
            {
                flip = XpSign(teeVector, v2) == 1;
                OffsetH(pc1, pc3!, pc2!, loc, upVector, teeVector, flip, mirror, index.WidthInTexture);
                Counters.SlantedTees++;
                FinishJunction(pc1, pc2!, pc3!, loc, teeVector, index, flip ? RoadPiece.ThreeWayHFlip : RoadPiece.ThreeWayH, -3);
            }
            else
            {
                flip = XpSign(teeVector, v1) == 1;
                OffsetH(pc3!, pc2!, pc1, loc, upVector, teeVector, flip, mirror, index.WidthInTexture);
                Counters.SlantedTees++;
                FinishJunction(pc1, pc2!, pc3!, loc, teeVector, index, flip ? RoadPiece.ThreeWayHFlip : RoadPiece.ThreeWayH, -3);
            }
        }
        else
        {
            if (xpdct < 0f) angle = -angle;
            upVector = Normalized(upVector) * (0.5f * scale);
            var teeVector = Rotate(upVector, angle);
            if (do12) Offset3Way(pc1, pc2!, pc3!, loc, upVector, teeVector, index.WidthInTexture);
            if (do13) Offset3Way(pc1, pc3!, pc2!, loc, upVector, teeVector, index.WidthInTexture);
            if (do32) Offset3Way(pc3!, pc2!, pc1, loc, upVector, teeVector, index.WidthInTexture);
            Counters.Tees++;
            FinishJunction(pc1, pc2!, pc3!, loc, teeVector, index, RoadPiece.Tee, -3);
        }
    }

    /// <summary>Resets the three consumed ends and appends the junction piece (the code every <c>insertTee</c>/<c>insertY</c> branch ends with).</summary>
    private void FinishJunction(RoadPt pc1, RoadPt pc2, RoadPt pc3, Vector2 loc, Vector2 arm, RoadSeg template, RoadPiece type, int count)
    {
        pc1.Last = true; pc1.Count = 0;
        pc2.Last = true; pc2.Count = 0;
        pc3.Last = true; pc3.Count = 0;
        if (!CheckSegments()) return;
        var piece = new RoadSeg
        {
            Scale = template.Scale,
            WidthInTexture = template.WidthInTexture,
            Type = type,
            UniqueId = template.UniqueId,
        };
        piece.P1.Loc = loc;
        piece.P2.Loc = loc + arm;
        piece.P1.Last = true;
        piece.P2.Last = true;
        piece.P1.Count = count;
        _roads.Add(piece);
    }

    private bool InsertY(Vector2 loc, int index1, float scale)
    {
        RoadPt pr1, pc1;
        if (_roads[index1].P1.Loc == loc) { pr1 = _roads[index1].P2; pc1 = _roads[index1].P1; }
        else { pr1 = _roads[index1].P1; pc1 = _roads[index1].P2; }
        RoadPt? pr2 = null, pr3 = null, pc2 = null, pc3 = null;
        foreach (var (far, center) in OtherEndsAt(loc, index1))
        {
            if (pr2 is null) { pr2 = far; pc2 = center; }
            else { pr3 = far; pc3 = center; }
        }
        if (pr2 is null || pr3 is null) return false;

        var v1 = Normalized(pr1.Loc - loc);
        var v2 = Normalized(pr2.Loc - loc);
        var v3 = Normalized(pr3.Loc - loc);
        bool do12 = false, do13 = false, do32 = false;
        var dot12 = Vector2.Dot(v1, v2);
        var dot13 = Vector2.Dot(v1, v3);
        var dot32 = Vector2.Dot(v3, v2);
        float score12 = 2f, score13 = 2f, score32 = 2f;
        const float cos30 = 0.866f;
        const float cos45 = 0.707f;
        if (dot12 < -cos30) return false; // too close to a straight line: a straight side tee.
        if (dot13 < -cos30) return false;
        if (dot32 < -cos30) return false;

        var s2 = XpSign(v1, v2);
        var s3 = XpSign(v1, v3);
        if (s2 != s3 && s2 + s3 == 0)
        {
            var v190 = new Vector2(-v1.Y, v1.X);
            if (XpSign(v190, v2) == 1 && XpSign(v190, v3) == 1)
            {
                do32 = true;
                score32 = MathF.Abs(dot12 + cos45) + MathF.Abs(dot13 + cos45);
            }
        }
        var s1 = XpSign(v3, v1);
        s2 = XpSign(v3, v2);
        if (s2 != s1 && s2 + s1 == 0)
        {
            var v390 = new Vector2(-v3.Y, v3.X);
            if (XpSign(v390, v2) == 1 && XpSign(v390, v1) == 1)
            {
                do12 = true;
                score12 = MathF.Abs(dot13 + cos45) + MathF.Abs(dot32 + cos45);
            }
        }
        s1 = XpSign(v2, v1);
        s3 = XpSign(v2, v3);
        if (s3 != s1 && s3 + s1 == 0)
        {
            var v290 = new Vector2(-v2.Y, v2.X);
            if (XpSign(v290, v3) == 1 && XpSign(v290, v1) == 1)
            {
                do13 = true;
                score13 = MathF.Abs(dot12 + cos45) + MathF.Abs(dot32 + cos45);
            }
        }
        if (score12 < score13)
        {
            do13 = false;
            if (score12 < score32) do32 = false; else do12 = false;
        }
        else
        {
            do12 = false;
            if (score13 < score32) do32 = false; else do13 = false;
        }

        Vector2 upVector;
        if (do12) upVector = v3;
        else if (do13) upVector = v2;
        else if (do32) upVector = v1;
        else return false;

        upVector = Normalized(upVector) * (0.5f * scale);
        var teeVector = Rotate(upVector, -HalfPi);
        var widthInTexture = _roads[index1].WidthInTexture;
        if (do12)
        {
            if (XpSign(v3, v1) == -1) OffsetY(pc1, pc2!, pc3!, loc, upVector, widthInTexture);
            else OffsetY(pc2!, pc1, pc3!, loc, upVector, widthInTexture);
        }
        if (do13)
        {
            if (XpSign(v2, v1) == -1) OffsetY(pc1, pc3!, pc2!, loc, upVector, widthInTexture);
            else OffsetY(pc3!, pc1, pc2!, loc, upVector, widthInTexture);
        }
        if (do32)
        {
            if (XpSign(v1, v3) == -1) OffsetY(pc3!, pc2!, pc1, loc, upVector, widthInTexture);
            else OffsetY(pc2!, pc3!, pc1, loc, upVector, widthInTexture);
        }
        pc1.Last = true; pc1.Count = 0;
        pc2!.Last = true; pc2.Count = 0;
        pc3!.Last = true; pc3.Count = 0;
        if (_roads.Count >= _catalog.MaxSegments) return false;
        var index = _roads[index1];
        var piece = new RoadSeg { Scale = index.Scale, WidthInTexture = index.WidthInTexture, Type = RoadPiece.ThreeWayY, UniqueId = index.UniqueId };
        piece.P1.Loc = loc;
        piece.P2.Loc = loc + teeVector;
        piece.P1.Last = true;
        piece.P2.Last = true;
        piece.P1.Count = -3;
        _roads.Add(piece);
        Counters.YJunctions++;
        return true;
    }

    /// <summary><c>offset3Way</c>: moves the three segment ends to the join points of the tee piece and re-aims their edges.</summary>
    private static void Offset3Way(RoadPt pc1, RoadPt pc2, RoadPt pc3, Vector2 loc, Vector2 upVector, Vector2 teeVector, float widthInTexture)
    {
        pc1.Loc = loc - upVector;
        pc2.Loc = loc + upVector;
        pc3.Loc = loc + teeVector;

        var xpdct = CrossZ(upVector, teeVector);
        var rightTee = teeVector;
        if (xpdct < 0f) rightTee = -teeVector;
        rightTee *= widthInTexture;

        xpdct = CrossZ(upVector, pc1.Top - pc1.Loc);
        // Like the reference, the old top is measured against the already moved end point.
        if (xpdct > 0f) { pc1.Bottom = pc1.Loc - rightTee; pc1.Top = pc1.Loc + rightTee; }
        else { pc1.Bottom = pc1.Loc + rightTee; pc1.Top = pc1.Loc - rightTee; }
        xpdct = CrossZ(upVector, pc2.Top - pc2.Loc);
        if (xpdct > 0f) { pc2.Bottom = pc2.Loc - rightTee; pc2.Top = pc2.Loc + rightTee; }
        else { pc2.Bottom = pc2.Loc + rightTee; pc2.Top = pc2.Loc - rightTee; }
        upVector *= widthInTexture;
        xpdct = CrossZ(rightTee, pc3.Top - pc3.Loc);
        if (xpdct < 0f) { pc3.Bottom = pc3.Loc - upVector; pc3.Top = pc3.Loc + upVector; }
        else { pc3.Bottom = pc3.Loc + upVector; pc3.Top = pc3.Loc - upVector; }
    }

    private static void OffsetH(RoadPt pc1, RoadPt pc2, RoadPt pc3, Vector2 loc, Vector2 upVector, Vector2 teeVector, bool flip, bool mirror, float widthInTexture)
    {
        if (flip != mirror)
        {
            pc1.Loc = loc - upVector * 2.05f;
            pc2.Loc = loc + upVector * 0.46f;
        }
        else
        {
            pc1.Loc = loc - upVector * 0.46f;
            pc2.Loc = loc + upVector * 2.05f;
        }

        var xpdct = CrossZ(upVector, teeVector);
        var rightTee = teeVector;
        if (xpdct < 0f) rightTee = -teeVector;
        rightTee *= widthInTexture;

        xpdct = CrossZ(upVector, pc1.Top - pc1.Loc);
        if (xpdct > 0f) { pc1.Bottom = pc1.Loc - rightTee; pc1.Top = pc1.Loc + rightTee; }
        else { pc1.Bottom = pc1.Loc + rightTee; pc1.Top = pc1.Loc - rightTee; }
        xpdct = CrossZ(upVector, pc2.Top - pc2.Loc);
        if (xpdct > 0f) { pc2.Bottom = pc2.Loc - rightTee; pc2.Top = pc2.Loc + rightTee; }
        else { pc2.Bottom = pc2.Loc + rightTee; pc2.Top = pc2.Loc - rightTee; }

        var arm = flip ? Rotate(teeVector, MathF.PI / 4f) : Rotate(teeVector, -MathF.PI / 4f);
        var armNormal = new Vector2(-arm.Y, arm.X) * widthInTexture;
        pc3.Loc += arm * 2.10f;
        var sign = XpSign(arm, pc3.Top - loc);
        if (sign == 1) { pc3.Top = pc3.Loc + armNormal; pc3.Bottom = pc3.Loc - armNormal; }
        else { pc3.Top = pc3.Loc - armNormal; pc3.Bottom = pc3.Loc + armNormal; }
    }

    private static void OffsetY(RoadPt pc1, RoadPt pc2, RoadPt pc3, Vector2 loc, Vector2 upVector, float widthInTexture)
    {
        pc3.Loc += upVector * 0.55f;
        pc3.Top += upVector * 0.55f;
        pc3.Bottom += upVector * 0.55f;

        var arm = Rotate(upVector, 3f * MathF.PI / 4f);
        var armNormal = new Vector2(-arm.Y, arm.X) * widthInTexture;
        pc2.Loc += arm * 1.1f;
        var sign = XpSign(arm, pc2.Top - loc);
        if (sign == 1) { pc2.Top = pc2.Loc + armNormal; pc2.Bottom = pc2.Loc - armNormal; }
        else { pc2.Top = pc2.Loc - armNormal; pc2.Bottom = pc2.Loc + armNormal; }

        arm = Rotate(upVector, -3f * MathF.PI / 4f);
        armNormal = new Vector2(-arm.Y, arm.X) * widthInTexture;
        pc1.Loc += arm * 1.1f;
        sign = XpSign(arm, pc1.Top - loc);
        if (sign == 1) { pc1.Top = pc1.Loc + armNormal; pc1.Bottom = pc1.Loc - armNormal; }
        else { pc1.Top = pc1.Loc - armNormal; pc1.Bottom = pc1.Loc + armNormal; }
    }

    private void Insert4Way(Vector2 loc, int index1, float scale)
    {
        RoadPt pr1, pc1;
        if (_roads[index1].P1.Loc == loc) { pr1 = _roads[index1].P2; pc1 = _roads[index1].P1; }
        else { pr1 = _roads[index1].P1; pc1 = _roads[index1].P2; }
        RoadPt? pr2 = null, pr3 = null, pr4 = null, pc2 = null, pc3 = null, pc4 = null;
        foreach (var (far, center) in OtherEndsAt(loc, index1))
        {
            if (pr2 is null) { pr2 = far; pc2 = center; }
            else if (pr3 is null) { pr3 = far; pc3 = center; }
            else { pr4 = far; pc4 = center; }
        }
        if (pr2 is null || pr3 is null || pr4 is null) return;

        var v1 = Normalized(pr1.Loc - loc);
        var v2 = Normalized(pr2.Loc - loc);
        var v3 = Normalized(pr3.Loc - loc);
        var v4 = Normalized(pr4.Loc - loc);
        var dot12 = Vector2.Dot(v1, v2);
        var dot13 = Vector2.Dot(v1, v3);
        var dot14 = Vector2.Dot(v1, v4);
        var dot23 = Vector2.Dot(v2, v3);
        var dot24 = Vector2.Dot(v2, v4);
        var dot34 = Vector2.Dot(v3, v4);
        // The most negative dot product is the pair heading most opposite to each other.
        var curPair = 12;
        var curDot = dot12;
        if (dot13 < curDot) { curPair = 13; curDot = dot13; }
        if (dot14 < curDot) { curPair = 14; curDot = dot14; }
        if (dot23 < curDot) { curPair = 23; curDot = dot23; }
        if (dot24 < curDot) { curPair = 24; curDot = dot24; }
        if (dot34 < curDot) { curPair = 34; }

        var alignVector = curPair switch
        {
            12 => v2 - v1,
            13 => v3 - v1,
            14 => v4 - v1,
            23 => v3 - v2,
            24 => v4 - v2,
            _ => v4 - v3,
        };
        alignVector = Normalized(alignVector) * (0.5f * scale);

        var widthInTexture = _roads[index1].WidthInTexture;
        switch (curPair)
        {
            case 12: Offset4Way(pc1, pc2!, pc3!, pr3, pc4!, loc, alignVector, widthInTexture); break;
            case 13: Offset4Way(pc1, pc3!, pc2!, pr2, pc4!, loc, alignVector, widthInTexture); break;
            case 14: Offset4Way(pc1, pc4!, pc3!, pr3, pc2!, loc, alignVector, widthInTexture); break;
            case 23: Offset4Way(pc2!, pc3!, pc1, pr1, pc4!, loc, alignVector, widthInTexture); break;
            case 24: Offset4Way(pc2!, pc4!, pc1, pr1, pc3!, loc, alignVector, widthInTexture); break;
            default: Offset4Way(pc3!, pc4!, pc1, pr1, pc2!, loc, alignVector, widthInTexture); break;
        }
        if (alignVector.X < 0f) alignVector = -alignVector; // a crossing is symmetric: make it point to +x.
        if (!CheckSegments()) return;
        var index = _roads[index1];
        var piece = new RoadSeg { Scale = index.Scale, WidthInTexture = TeeWidthAdjustment, Type = RoadPiece.FourWay, UniqueId = index.UniqueId };
        piece.P1.Loc = loc;
        piece.P2.Loc = loc + alignVector;
        piece.P1.Last = true;
        piece.P2.Last = true;
        piece.P1.Count = -4;
        _roads.Add(piece);
        Counters.FourWays++;
    }

    private static void Offset4Way(RoadPt pc1, RoadPt pc2, RoadPt pc3, RoadPt pr3, RoadPt pc4, Vector2 loc, Vector2 alignVector, float widthInTexture)
    {
        pc1.Loc = loc - alignVector;
        pc2.Loc = loc + alignVector;

        var v3 = pr3.Loc - loc;
        var angle = HalfPi;
        var xpdct = CrossZ(alignVector, v3);
        if (xpdct < 0f) angle = -angle;
        var teeVector = Rotate(alignVector, angle);
        pc3.Loc = loc + teeVector;
        pc4.Loc = loc - teeVector;

        var realTee = Rotate(alignVector, HalfPi) * widthInTexture;

        xpdct = CrossZ(alignVector, pc1.Top - pc1.Loc);
        if (xpdct > 0f) { pc1.Bottom = pc1.Loc - realTee; pc1.Top = pc1.Loc + realTee; }
        else { pc1.Bottom = pc1.Loc + realTee; pc1.Top = pc1.Loc - realTee; }
        xpdct = CrossZ(alignVector, pc2.Top - pc2.Loc);
        if (xpdct > 0f) { pc2.Bottom = pc2.Loc - realTee; pc2.Top = pc2.Loc + realTee; }
        else { pc2.Bottom = pc2.Loc + realTee; pc2.Top = pc2.Loc - realTee; }
        alignVector *= widthInTexture;

        xpdct = CrossZ(realTee, pc3.Top - pc3.Loc);
        if (xpdct < 0f) { pc3.Bottom = pc3.Loc - alignVector; pc3.Top = pc3.Loc + alignVector; }
        else { pc3.Bottom = pc3.Loc + alignVector; pc3.Top = pc3.Loc - alignVector; }
        xpdct = CrossZ(realTee, pc4.Top - pc4.Loc);
        if (xpdct < 0f) { pc4.Bottom = pc4.Loc - alignVector; pc4.Top = pc4.Loc + alignVector; }
        else { pc4.Bottom = pc4.Loc + alignVector; pc4.Top = pc4.Loc - alignVector; }

        pc1.Last = true; pc1.Count = 0;
        pc2.Last = true; pc2.Count = 0;
        pc3.Last = true; pc3.Count = 0;
        pc4.Last = true; pc4.Count = 0;
    }

    // ------------------------------------------------------------------ corners: curves and mitres

    private void InsertCurveSegments()
    {
        var numRoadSegments = _roads.Count;
        var segmentStartIndex = -1;
        for (var i = 0; i < numRoadSegments; i++)
        {
            if (i < numRoadSegments - 1 && _roads[i].P1.Loc == _roads[i + 1].P2.Loc)
            {
                if (_roads[i + 1].P2.Count == 1 && _roads[i].P1.Count == 1)
                {
                    InsertCurveSegmentAt(i, i + 1);
                    if (segmentStartIndex < 0) segmentStartIndex = i;
                }
            }
            else if (segmentStartIndex >= 0)
            {
                if (_roads[i].P1.Loc == _roads[segmentStartIndex].P2.Loc)
                {
                    if (_roads[segmentStartIndex].P2.Count == 1 && _roads[i].P1.Count == 1) InsertCurveSegmentAt(i, segmentStartIndex);
                }
                segmentStartIndex = -1;
            }
        }
    }

    /// <summary><c>miter</c>: moves the facing edge end points of two chained segments to the crossing of their edge lines.</summary>
    private void Miter(int ndx1, int ndx2)
    {
        var a = _roads[ndx1];
        var b = _roads[ndx2];
        var line1 = new LineSeg(a.P1.Top, a.P2.Top);
        var line2 = new LineSeg(b.P1.Top, b.P2.Top);
        if (line1.FindIntersection(line2, out var point))
        {
            b.P2.Top = new Vector2(point.X, point.Y);
            a.P1.Top = new Vector2(point.X, point.Y);
        }
        line1 = new LineSeg(a.P1.Bottom, a.P2.Bottom);
        line2 = new LineSeg(b.P1.Bottom, b.P2.Bottom);
        if (line1.FindIntersection(line2, out point))
        {
            b.P2.Bottom = new Vector2(point.X, point.Y);
            a.P1.Bottom = new Vector2(point.X, point.Y);
        }
        Counters.Mitres++;
    }

    private void InsertCurveSegmentAt(int ndx1, int ndx2)
    {
        const float dotLimit = 0.5f;
        var seg1 = _roads[ndx1];
        var seg2 = _roads[ndx2];
        var radius = seg1.CurveRadius * seg1.Scale;
        var originalPt = seg1.P1.Loc;
        if (seg1.UniqueId != seg2.UniqueId) return;
        var line1 = new LineSeg(seg1.P1.Loc, seg1.P2.Loc);
        var line2 = new LineSeg(seg2.P1.Loc, seg2.P2.Loc);
        RoadPt pr1, pr2, pr3, pr4;
        var curCos = Vector3.Dot(line1.Dir, line2.Dir);
        var xpdct = Vector3.Cross(line1.Dir, line2.Dir).Z;
        if (xpdct > 0f)
        {
            pr1 = seg1.P1; pr2 = seg1.P2; pr3 = seg2.P1; pr4 = seg2.P2;
        }
        else
        {
            pr4 = seg1.P1; pr3 = seg1.P2; pr2 = seg2.P1; pr1 = seg2.P2;
            line1 = new LineSeg(pr1.Loc, pr2.Loc);
            line2 = new LineSeg(pr3.Loc, pr4.Loc);
        }
        var angle = MathF.Acos(Math.Clamp(curCos, -1f, 1f));
        var count = angle / (MathF.PI / 6f); // number of 30 degree steps
        if (count < 0.9f || seg1.P1.IsAngled)
        {
            Miter(ndx1, ndx2);
            return;
        }

        var offset1 = Rotate(new Vector2(line1.Dir.X, line1.Dir.Y) * radius, -HalfPi);
        var offset2 = Rotate(new Vector2(line2.Dir.X, line2.Dir.Y) * radius, -HalfPi);
        var offsetLine1 = new LineSeg(pr1.Loc + offset1, pr2.Loc + offset1);
        var offsetLine2 = new LineSeg(pr3.Loc + offset2, pr4.Loc + offset2);
        if (!offsetLine1.FindIntersection(offsetLine2, out var pInt1Full)) return;

        seg2.P2.Last = true;
        var pInt1v = new Vector2(pInt1Full.X, pInt1Full.Y);
        var cross1 = new LineSeg(pInt1v, pInt1v - offset2);
        var cross2 = new LineSeg(pInt1v, pInt1v - offset1);
        // A failed intersection (zero direction) leaves the previous point, like the reference's untouched out parameters.
        var pInt1 = pInt1Full;
        if (cross1.FindIntersection(line2, out var foot2)) pInt1 = foot2;
        var pInt3 = pInt1Full;
        if (cross2.FindIntersection(line1, out var foot1)) pInt3 = foot1;
        // Make sure the lines did not clip out of existence.
        var theDot = Vector3.Dot(line2.Dir, pInt1 - new Vector3(pr3.Loc, 0f));
        if (theDot < dotLimit)
        {
            pr1.Loc = originalPt;
            pr4.Loc = originalPt;
            Counters.CurvesAborted++;
            Miter(ndx1, ndx2);
            return;
        }
        theDot = Vector3.Dot(line1.Dir, new Vector3(pr2.Loc, 0f) - pInt3);
        if (theDot < dotLimit)
        {
            pr1.Loc = originalPt;
            pr4.Loc = originalPt;
            Counters.CurvesAborted++;
            Miter(ndx1, ndx2);
            return;
        }

        pr4.Loc = new Vector2(pInt1.X, pInt1.Y);
        var step = -MathF.PI / 6f; // -30 degrees
        var pt2 = pr4.Loc;
        var pt1 = pr3.Loc;
        var direction = new Vector2(pt1.X - pt2.X, pt1.Y - pt2.Y);
        // offset = normal of the vector from pt1 to pt2.
        var centerOfCurve = Normalized(new Vector2(-direction.Y, direction.X)) * (seg1.CurveRadius * seg1.Scale) + pt2;

        pt2 = RotateAbout(pt2, centerOfCurve, step);
        direction = Rotate(direction, step);
        pt1 = pt2 + direction;
        AddCurve(pt2, pt1, seg1);
        if (count > 2f)
        {
            for (var i = 2; i < count; i++)
            {
                direction = Rotate(direction, step);
                pt2 = RotateAbout(pt2, centerOfCurve, step);
                pt1 = pt2 + direction;
                AddCurve(pt2, pt1, seg1);
            }
        }

        pr1.Loc = new Vector2(pInt3.X, pInt3.Y);
        seg1.P1.Last = true;
        if (count > 1f)
        {
            pt2 = pr1.Loc;
            pt1 = pr1.Loc + pr1.Loc - pr2.Loc;
            direction = new Vector2(pt1.X - pt2.X, pt1.Y - pt2.Y);
            pt1 = pt2 + direction;
            AddCurve(pt2, pt1, seg1);
        }
        Counters.Curves++;

        // Recalculate top and bottom of the shortened ends.
        var roadVector = seg1.P2.Loc - seg1.P1.Loc;
        var roadNormal = Normalized(new Vector2(-roadVector.Y, roadVector.X)) * (seg1.Scale * seg1.WidthInTexture / 2f);
        seg1.P1.Top = seg1.P1.Loc + roadNormal;
        seg1.P1.Bottom = seg1.P1.Loc - roadNormal;
        roadVector = seg2.P2.Loc - seg2.P1.Loc;
        roadNormal = Normalized(new Vector2(-roadVector.Y, roadVector.X)) * (seg2.Scale * seg2.WidthInTexture / 2f);
        seg2.P2.Top = seg2.P2.Loc + roadNormal;
        seg2.P2.Bottom = seg2.P2.Loc - roadNormal;
    }

    private void AddCurve(Vector2 loc1, Vector2 loc2, RoadSeg source)
    {
        if (!CheckSegments()) return;
        var piece = new RoadSeg
        {
            Scale = source.Scale,
            WidthInTexture = source.WidthInTexture,
            Type = RoadPiece.Curve,
            CurveRadius = source.CurveRadius,
            UniqueId = source.UniqueId,
        };
        piece.P1.Loc = loc1;
        piece.P2.Loc = loc2;
        piece.P1.Last = true;
        piece.P2.Last = true;
        _roads.Add(piece);
    }

    /// <summary><c>rotateAbout</c>: rotates the point around the centre by the angle.</summary>
    private static Vector2 RotateAbout(Vector2 point, Vector2 center, float angle)
    {
        var offset = point - center;
        var rotated = Rotate(offset, angle);
        return point + (rotated - offset);
    }

    // ------------------------------------------------------------------ open ends that blend into another road type

    private void InsertCrossTypeJoins()
    {
        var numRoadSegments = _roads.Count;
        for (var i = 0; i < numRoadSegments; i++)
        {
            var road = _roads[i];
            Vector2 loc1, loc2;
            var isPt1 = false;
            if (road.P2.Count == 0 && road.P2.IsJoin)
            {
                loc1 = road.P2.Loc;
                loc2 = road.P1.Loc;
            }
            else if (road.P1.Count == 0 && road.P1.IsJoin)
            {
                loc1 = road.P1.Loc;
                loc2 = road.P2.Loc;
                isPt1 = true;
            }
            else
            {
                continue;
            }
            var roadVector = Normalized(loc2 - loc1);
            var joinVector = roadVector;
            var otherId = FindCrossTypeJoinVector(loc1, ref joinVector, road.UniqueId);
            if (otherId == 0) joinVector *= 100f;
            var roadNormal = new Vector2(-roadVector.Y, roadVector.X);
            var joinNormal = new Vector2(-joinVector.Y, joinVector.X);

            var half = road.Scale * road.WidthInTexture / 2f;
            var p1 = loc1 + roadNormal * half;
            var p2 = loc2 + roadNormal * half;
            var roadLine = new LineSeg(p1, p2);
            var joinLine = new LineSeg(loc1, loc1 + joinNormal);
            var top = road.P1.Top;
            if (joinLine.FindIntersection(roadLine, out var pInt))
            {
                var hit = new Vector2(pInt.X, pInt.Y);
                if (isPt1) road.P1.Top = hit; else road.P2.Bottom = hit;
                top = hit;
            }
            p1 = loc1 - roadNormal * half;
            p2 = loc2 - roadNormal * half;
            roadLine = new LineSeg(p1, p2);
            var bottom = road.P1.Bottom;
            if (joinLine.FindIntersection(roadLine, out pInt))
            {
                var hit = new Vector2(pInt.X, pInt.Y);
                if (isPt1) road.P1.Bottom = hit; else road.P2.Top = hit;
                bottom = hit;
            }
            var scaleAdjustment = (bottom - top).Length() / (road.Scale * road.WidthInTexture);
            if (otherId != 0)
            {
                AdjustStacking(road.UniqueId, otherId);
                Counters.CrossTypeJoins++;
            }
            if (!CheckSegments()) return;
            var piece = new RoadSeg
            {
                Scale = road.Scale,
                WidthInTexture = road.Scale * scaleAdjustment,
                Type = RoadPiece.AlphaJoin,
                UniqueId = road.UniqueId,
            };
            piece.P1.Loc = loc1;
            piece.P2.Loc = loc1 + joinVector;
            piece.P1.Last = true;
            piece.P2.Last = true;
            piece.P1.Count = 0;
            _roads.Add(piece);
            Counters.AlphaJoins++;
        }
    }

    private int FindCrossTypeJoinVector(Vector2 loc, ref Vector2 joinVector, int uniqueId)
    {
        var numRoadSegments = _roads.Count;
        for (var i = 0; i < numRoadSegments; i++)
        {
            var other = _roads[i];
            if (other.UniqueId == uniqueId) continue; // only different road types
            if (other.Type != RoadPiece.Segment) continue; // only join to straight segments
            var loc1 = other.P1.Loc;
            var loc2 = other.P2.Loc;
            var lo = Vector2.Min(loc1, loc2) - new Vector2(other.Scale / 2f);
            var hi = Vector2.Max(loc1, loc2) + new Vector2(other.Scale / 2f);
            if (loc.X >= lo.X && loc.Y >= lo.Y && loc.X <= hi.X && loc.Y <= hi.Y)
            {
                var line = new LineSeg(loc1, loc2);
                var onLine = line.FindPointClosestTo(new Vector3(loc, 0f), (loc2 - loc1).Length());
                var distance = Vector3.Distance(onLine, new Vector3(loc, 0f));
                if (distance < other.Scale * 0.55f)
                {
                    var roadVec = loc2 - loc1;
                    roadVec = XpSign(roadVec, joinVector) == 1 ? Rotate(roadVec, HalfPi) : Rotate(roadVec, -HalfPi);
                    joinVector = roadVec;
                    return other.UniqueId;
                }
            }
        }
        return 0;
    }

    /// <summary><c>adjustStacking</c>: makes <paramref name="topId"/> draw after (above) <paramref name="bottomId"/>.</summary>
    private void AdjustStacking(int topId, int bottomId)
    {
        var top = _stacking.GetValueOrDefault(topId);
        var bottom = _stacking.GetValueOrDefault(bottomId);
        if (top > bottom) return; // already on top
        var newStacking = bottom;
        foreach (var id in _stacking.Keys.ToArray())
            if (_stacking[id] > newStacking) _stacking[id]++;
        _stacking[topId] = newStacking + 1;
        _stacking.TryAdd(bottomId, bottom);
    }
}

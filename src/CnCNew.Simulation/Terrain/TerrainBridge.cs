using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Terrain;

/// <summary>A point of a bridge's deck.</summary>
public readonly record struct DeckPoint(Fixed X, Fixed Y, Fixed Z);

/// <summary>A pair of bridge points of a map (<c>FLAG_BRIDGE_POINT1</c> directly followed by <c>FLAG_BRIDGE_POINT2</c>, <c>W3DBridgeBuffer::loadBridges</c>).</summary>
public sealed record MapBridgeSpan(string Template, Fixed FromX, Fixed FromY, Fixed ToX, Fixed ToY, string ScriptName);

/// <summary>
/// One bridge of the terrain (<c>Bridge</c>, <c>BridgeInfo</c> of <c>TerrainLogic.cpp</c>): the corners its model or its object's box gave it, the geometry questions the pathfinder and the
/// height of everything on it are answered from, and what the world keeps about it. The deck is the plane through <see cref="FromLeft"/>, <see cref="FromRight"/> and <see cref="ToLeft"/>
/// (<c>getBridgeHeight</c>). Immutable but for the object ids, the layer and the damage state, which are the world's.
/// </summary>
public sealed class TerrainBridge
{
    /// <summary><c>BRIDGE_MAX_TOWERS</c>.</summary>
    public const int MaxTowers = 4;

    /// <summary><c>LAYER_Z_CLOSE_ENOUGH_F</c>: how close in height an object has to be to interact with a layer.</summary>
    public static readonly Fixed LayerCloseEnough = Fixed.FromInt(10);

    /// <summary><c>BRIDGE_FLOAT_AMT</c>: how far above the ground the end points of a terrain bridge lie.</summary>
    public static readonly Fixed FloatAmount = Fixed.FromFraction(1, 4);

    private static readonly Fixed CellSize = Fixed.FromInt(TerrainMap.CellSize);
    private readonly Fixed _lowX, _lowY, _highX, _highY;
    private readonly Fixed _normalX, _normalY, _normalZ;

    /// <param name="renderIndex"><c>BridgeInfo::bridgeIndex</c>: the place of a terrain bridge in the drawn list; -1 for a landmark bridge, which its own object draws.</param>
    public TerrainBridge(string templateName, string scriptName, int renderIndex, DeckPoint from, DeckPoint to, Fixed width, DeckPoint fromLeft, DeckPoint fromRight, DeckPoint toLeft, DeckPoint toRight)
    {
        TemplateName = templateName;
        ScriptName = scriptName;
        RenderIndex = renderIndex;
        From = from;
        To = to;
        Width = width;
        FromLeft = fromLeft;
        FromRight = fromRight;
        ToLeft = toLeft;
        ToRight = toRight;
        _lowX = Fixed.Min(Fixed.Min(fromLeft.X, fromRight.X), Fixed.Min(toLeft.X, toRight.X));
        _lowY = Fixed.Min(Fixed.Min(fromLeft.Y, fromRight.Y), Fixed.Min(toLeft.Y, toRight.Y));
        _highX = Fixed.Max(Fixed.Max(fromLeft.X, fromRight.X), Fixed.Max(toLeft.X, toRight.X));
        _highY = Fixed.Max(Fixed.Max(fromLeft.Y, fromRight.Y), Fixed.Max(toLeft.Y, toRight.Y));
        var (ax, ay, az) = (fromRight.X - fromLeft.X, fromRight.Y - fromLeft.Y, fromRight.Z - fromLeft.Z);
        var (bx, by, bz) = (toLeft.X - fromLeft.X, toLeft.Y - fromLeft.Y, toLeft.Z - fromLeft.Z);
        _normalX = ay * bz - az * by;
        _normalY = az * bx - ax * bz;
        _normalZ = ax * by - ay * bx;
    }

    public string TemplateName { get; }
    public string ScriptName { get; }
    public int RenderIndex { get; }
    public DeckPoint From { get; }
    public DeckPoint To { get; }
    public Fixed Width { get; }
    public DeckPoint FromLeft { get; }
    public DeckPoint FromRight { get; }
    public DeckPoint ToLeft { get; }
    public DeckPoint ToRight { get; }

    /// <summary><c>Bridge::m_layer</c>, 1 and up; 0 when the pathfinder ran out of layers.</summary>
    public int Layer { get; internal set; }

    /// <summary><c>BridgeInfo::bridgeObjectID</c>.</summary>
    public EntityId ObjectId { get; internal set; }

    /// <summary><c>BridgeInfo::towerObjectID</c>, by <c>BridgeTowerType</c>.</summary>
    public EntityId[] Towers { get; } = [EntityId.Invalid, EntityId.Invalid, EntityId.Invalid, EntityId.Invalid];

    /// <summary><c>BridgeInfo::curDamageState</c>.</summary>
    public int DamageState { get; internal set; }

    /// <summary><c>BridgeInfo::damageStateChanged</c>.</summary>
    public bool DamageStateChanged { get; internal set; }

    /// <summary>The point of the deck's middle, where the bridge's object stands.</summary>
    public DeckPoint Center => new(Half(FromLeft.X + ToRight.X), Half(FromLeft.Y + ToRight.Y), Half(FromLeft.Z + ToRight.Z));

    /// <summary>The angle of the object that stands for the bridge: from the 'from' side to the 'to' side.</summary>
    public Fixed Angle => FixedTrig.Atan2(ToLeft.Y - FromLeft.Y, ToLeft.X - FromLeft.X);

    private static Fixed Half(Fixed value) => value / Fixed.FromInt(2);

    /// <summary>The corners of a terrain bridge between two points of the map: <c>W3DBridge::getBridgeInfo</c>, with the model's lateral extent <paramref name="minY"/> to <paramref name="maxY"/> scaled.</summary>
    public static TerrainBridge FromSpan(string templateName, string scriptName, int renderIndex, DeckPoint from, DeckPoint to, Fixed minY, Fixed maxY, Fixed scale)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var length = Fixed.Sqrt(dx * dx + dy * dy);
        var (normalX, normalY) = length > Fixed.Zero ? (-dy / length, dx / length) : (Fixed.Zero, Fixed.Zero);
        var (left, right) = (maxY * scale, minY * scale);
        return new TerrainBridge(templateName, scriptName, renderIndex, from, to, (maxY - minY) * scale,
            new DeckPoint(from.X + normalX * left, from.Y + normalY * left, from.Z), new DeckPoint(from.X + normalX * right, from.Y + normalY * right, from.Z),
            new DeckPoint(to.X + normalX * left, to.Y + normalY * left, to.Z), new DeckPoint(to.X + normalX * right, to.Y + normalY * right, to.Z));
    }

    /// <summary>The corners of a landmark bridge: <c>Bridge::Bridge(Object*)</c>, the box of its object.</summary>
    public static TerrainBridge FromObject(string templateName, string scriptName, Fixed x, Fixed y, Fixed z, Fixed angle, Fixed halfLength, Fixed halfWidth)
    {
        var (c, s) = (FixedTrig.Cos(angle), FixedTrig.Sin(angle));
        var fromLeft = new DeckPoint(x - halfLength * c - halfWidth * s, y + halfWidth * c - halfLength * s, z);
        var toLeft = new DeckPoint(x + halfLength * c - halfWidth * s, y + halfWidth * c + halfLength * s, z);
        var fromRight = new DeckPoint(x - halfLength * c + halfWidth * s, y - halfWidth * c - halfLength * s, z);
        var toRight = new DeckPoint(x + halfLength * c + halfWidth * s, y - halfWidth * c + halfLength * s, z);
        var from = new DeckPoint(Half(fromLeft.X + fromRight.X), Half(fromLeft.Y + fromRight.Y), z);
        var to = new DeckPoint(Half(toLeft.X + toRight.X), Half(toLeft.Y + toRight.Y), z);
        return new TerrainBridge(templateName, scriptName, -1, from, to, halfWidth * Fixed.FromInt(2), fromLeft, fromRight, toLeft, toRight);
    }

    /// <summary>The range of cells the bridge's bounds cover, padded as <c>PathfindLayer::allocateCells</c> does; the caller clips it to the map.</summary>
    internal (int X0, int Y0, int X1, int Y1) CellBounds()
    {
        var margin = Fixed.FromFraction(TerrainMap.CellSize, 100);
        return (FloorCell(_lowX - margin) - 1, FloorCell(_lowY - margin) - 1, CeilCell(_highX + margin) + 1, CeilCell(_highY + margin) + 1);
    }

    private static int FloorCell(Fixed value) => checked((int)(value / CellSize).Floor());
    private static int CeilCell(Fixed value) => checked((int)(value / CellSize).Ceiling());

    /// <summary><c>Bridge::isPointOnBridge</c>: inside the bounds and in one of the deck's two triangles.</summary>
    public bool IsPointOnBridge(Fixed x, Fixed y)
    {
        if (x < _lowX || x > _highX || y < _lowY || y > _highY) return false;
        return InTriangle(FromLeft, FromRight, ToLeft, x, y) || InTriangle(FromRight, ToLeft, ToRight, x, y);
    }

    /// <summary><c>Point_In_Triangle_2D</c>: on the same side of all three edges, an edge counting as inside.</summary>
    private static bool InTriangle(DeckPoint a, DeckPoint b, DeckPoint c, Fixed x, Fixed y)
    {
        var whole = Perp(b.X - a.X, b.Y - a.Y, c.X - a.X, c.Y - a.Y);
        if (whole == Fixed.Zero) return false;
        var positive = whole > Fixed.Zero;
        bool Outside(Fixed factor) => positive ? factor < Fixed.Zero : factor > Fixed.Zero;
        return !Outside(Perp(b.X - a.X, b.Y - a.Y, x - a.X, y - a.Y)) && !Outside(Perp(c.X - b.X, c.Y - b.Y, x - b.X, y - b.Y)) && !Outside(Perp(a.X - c.X, a.Y - c.Y, x - c.X, y - c.Y));
    }

    private static Fixed Perp(Fixed ax, Fixed ay, Fixed bx, Fixed by) => ax * by - ay * bx;

    /// <summary><c>Bridge::getBridgeHeight</c>: the height of the deck's plane above the point, on the deck or not.</summary>
    public Fixed HeightAt(Fixed x, Fixed y)
        => _normalZ == Fixed.Zero ? FromLeft.Z : FromLeft.Z - (_normalX * (x - FromLeft.X) + _normalY * (y - FromLeft.Y)) / _normalZ;

    /// <summary>The unit vector of the line <c>from - to</c> in three dimensions, as <c>Coord3D::normalize</c> gives it.</summary>
    private static (Fixed X, Fixed Y) Normalized(Fixed x, Fixed y, Fixed z)
    {
        var length = Fixed.Sqrt(x * x + y * y + z * z);
        return length > Fixed.Zero ? (x / length, y / length) : (Fixed.Zero, Fixed.Zero);
    }

    private (Fixed X, Fixed Y) EndVector() => Normalized(FromRight.X - FromLeft.X, FromRight.Y - FromLeft.Y, FromRight.Z - FromLeft.Z);

    /// <summary><c>Bridge::isCellOnEnd</c>: the two end lines, a cell shorter on both sides, cross the region.</summary>
    public bool IsCellOnEnd(Fixed loX, Fixed loY, Fixed hiX, Fixed hiY)
    {
        var (ex, ey) = EndVector();
        var (offX, offY) = (ex * CellSize, ey * CellSize);
        return LineInRegion(FromLeft.X + offX, FromLeft.Y + offY, FromRight.X - offX, FromRight.Y - offY, loX, loY, hiX, hiY)
            || LineInRegion(ToLeft.X + offX, ToLeft.Y + offY, ToRight.X - offX, ToRight.Y - offY, loX, loY, hiX, hiY);
    }

    /// <summary><c>Bridge::isCellOnSide</c>: one of the two side lines, moved out by 0.51 and by 1.02 cells, crosses the region.</summary>
    public bool IsCellOnSide(Fixed loX, Fixed loY, Fixed hiX, Fixed hiY)
    {
        var (ex, ey) = EndVector();
        var share = Fixed.FromFraction(51, 100);
        var (offX, offY) = (ex * CellSize * share, ey * CellSize * share);
        for (var step = 1; step <= 2; step++)
        {
            var (x, y) = (offX * step, offY * step);
            if (LineInRegion(FromLeft.X - x, FromLeft.Y - y, ToLeft.X - x, ToLeft.Y - y, loX, loY, hiX, hiY)) return true;
            if (LineInRegion(FromRight.X + x, FromRight.Y + y, ToRight.X + x, ToRight.Y + y, loX, loY, hiX, hiY)) return true;
        }
        return false;
    }

    /// <summary><c>Bridge::isCellEntryPoint</c>: half a cell beyond the deck's end, a cell shorter on both sides: where the ground joins the deck.</summary>
    public bool IsCellEntryPoint(Fixed loX, Fixed loY, Fixed hiX, Fixed hiY)
    {
        var (ex, ey) = EndVector();
        var (offX, offY) = (ex * CellSize, ey * CellSize);
        var (bx, by) = Normalized(To.X - From.X, To.Y - From.Y, To.Z - From.Z);
        var half = CellSize / Fixed.FromInt(2);
        var (alongX, alongY) = (bx * half, by * half);
        return LineInRegion(FromLeft.X - alongX + offX, FromLeft.Y - alongY + offY, FromRight.X - alongX - offX, FromRight.Y - alongY - offY, loX, loY, hiX, hiY)
            || LineInRegion(ToLeft.X + alongX + offX, ToLeft.Y + alongY + offY, ToRight.X + alongX - offX, ToRight.Y + alongY - offY, loX, loY, hiX, hiY);
    }

    /// <summary><c>getBridgeAttackPoints</c>: the two points of a bridge a weapon aims at, half its width inside its ends.</summary>
    public (DeckPoint First, DeckPoint Second) AttackPoints()
    {
        var (dx, dy, dz) = (To.X - From.X, To.Y - From.Y, To.Z - From.Z);
        var length = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length == Fixed.Zero) return (From, To);
        var (ux, uy, uz) = (dx / length, dy / length, dz / length);
        var (wx, wy, wz) = (FromRight.X - FromLeft.X, FromRight.Y - FromLeft.Y, FromRight.Z - FromLeft.Z);
        var half = Half(Fixed.Sqrt(wx * wx + wy * wy + wz * wz));
        return (new DeckPoint(From.X + ux * half, From.Y + uy * half, From.Z + uz * half), new DeckPoint(To.X - ux * half, To.Y - uy * half, To.Z - uz * half));
    }

    /// <summary>
    /// <c>LineInRegion</c> of <c>TerrainLogic.cpp</c>, Cohen-Sutherland as the original has it: both points are clipped in turn, the second with the slope the first left behind (<c>x2 += (x2 - x1) * ...</c>,
    /// which is what the original writes, and which is why it is not the textbook routine).
    /// </summary>
    internal static bool LineInRegion(Fixed x1, Fixed y1, Fixed x2, Fixed y2, Fixed clipLeft, Fixed clipTop, Fixed clipRight, Fixed clipBottom)
    {
        const int Left = 1, Right = 2, Bottom = 4, Top = 8;
        var code1 = x1 < clipLeft ? Left : x1 > clipRight ? Right : 0;
        if (y1 < clipTop) code1 |= Top;
        else if (y1 > clipBottom) code1 |= Bottom;
        var code2 = x2 < clipLeft ? Left : x2 > clipRight ? Right : 0;
        if (y2 < clipTop) code2 |= Top;
        else if (y2 > clipBottom) code2 |= Bottom;
        if ((code1 | code2) == 0) return true;
        if ((code1 & code2) != 0) return false;
        Fixed diff;
        if (code1 != 0)
        {
            if ((code1 & Top) != 0)
            {
                if ((diff = y2 - y1) == Fixed.Zero) return false;
                x1 += (x2 - x1) * (clipTop - y1) / diff;
                y1 = clipTop;
            }
            else if ((code1 & Bottom) != 0)
            {
                if ((diff = y2 - y1) == Fixed.Zero) return false;
                x1 += (x2 - x1) * (clipBottom - y1) / diff;
                y1 = clipBottom;
            }
            if (x1 > clipRight)
            {
                if ((diff = x2 - x1) == Fixed.Zero) return false;
                y1 += (y2 - y1) * (clipRight - x1) / diff;
                x1 = clipRight;
            }
            else if (x1 < clipLeft)
            {
                if ((diff = x2 - x1) == Fixed.Zero) return false;
                y1 += (y2 - y1) * (clipLeft - x1) / diff;
                x1 = clipLeft;
            }
        }
        if (code2 != 0)
        {
            if ((code2 & Top) != 0)
            {
                if ((diff = y2 - y1) == Fixed.Zero) return false;
                x2 += (x2 - x1) * (clipTop - y2) / diff;
                y2 = clipTop;
            }
            else if ((code2 & Bottom) != 0)
            {
                if ((diff = y2 - y1) == Fixed.Zero) return false;
                x2 += (x2 - x1) * (clipBottom - y2) / diff;
                y2 = clipBottom;
            }
            if (x2 > clipRight)
            {
                if ((diff = x2 - x1) == Fixed.Zero) return false;
                y2 += (y2 - y1) * (clipRight - x2) / diff;
                x2 = clipRight;
            }
            else if (x2 < clipLeft)
            {
                if ((diff = x2 - x1) == Fixed.Zero) return false;
                y2 += (y2 - y1) * (clipLeft - x2) / diff;
                x2 = clipLeft;
            }
        }
        return x1 >= clipLeft && x1 <= clipRight && y1 >= clipTop && y1 <= clipBottom && x2 >= clipLeft && x2 <= clipRight && y2 >= clipTop && y2 <= clipBottom;
    }
}

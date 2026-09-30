using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;

namespace CnCNew.Presentation.Terrain;

/// <summary>
/// Float terrain height and normal queries in world space for the renderer, camera and picking. Same surface as the simulation's
/// <c>TerrainMap.HeightAt</c> (<c>WorldHeightMap::getHeightMapHeight</c>): two triangles per height cell split along the
/// (ix, iy)-(ix+1, iy+1) diagonal, positions outside the height field clamped to its edge. World (0, 0) is the first playable height
/// vertex, i.e. height vertex (<see cref="Border"/>, <see cref="Border"/>).
/// </summary>
public sealed class TerrainHeightSampler
{
    public const float CellSize = 10f;
    public const float HeightScale = 0.625f;

    private readonly ImmutableArray<byte> _heights;

    public TerrainHeightSampler(MapHeightField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Width < 2 || field.Height < 2) throw new InvalidDataException("A map needs at least 2x2 height vertices.");
        if (field.Heights.Length != field.Width * field.Height) throw new InvalidDataException("Height field size does not match its dimensions.");
        if (field.Boundaries.IsDefaultOrEmpty) throw new InvalidDataException("The map has no boundary.");
        _heights = field.Heights;
        VertexWidth = field.Width;
        VertexHeight = field.Height;
        Border = field.Border;
        var cellsX = 0;
        var cellsY = 0;
        foreach (var boundary in field.Boundaries)
        {
            cellsX = Math.Max(cellsX, boundary.X);
            cellsY = Math.Max(cellsY, boundary.Y);
        }
        PlayableCellsX = cellsX;
        PlayableCellsY = cellsY;
        var min = byte.MaxValue;
        var max = byte.MinValue;
        foreach (var height in _heights)
        {
            min = Math.Min(min, height);
            max = Math.Max(max, height);
        }
        MinHeight = min * HeightScale;
        MaxHeight = max * HeightScale;
    }

    public int VertexWidth { get; }
    public int VertexHeight { get; }
    public int Border { get; }

    /// <summary>The logical extent in cells (<c>TerrainLogic::getExtent</c>, the largest map boundary), the same as <c>TerrainMap.CellsX</c>.</summary>
    public int PlayableCellsX { get; }
    public int PlayableCellsY { get; }

    public float MinHeight { get; }
    public float MaxHeight { get; }

    /// <summary>World rectangle of the whole height field, border included.</summary>
    public Vector2 FieldMin => new(-Border * CellSize, -Border * CellSize);
    public Vector2 FieldMax => new((VertexWidth - 1 - Border) * CellSize, (VertexHeight - 1 - Border) * CellSize);

    /// <summary>World rectangle of the playable area: (0, 0) to the logical extent.</summary>
    public Vector2 PlayableMax => new(PlayableCellsX * CellSize, PlayableCellsY * CellSize);

    public byte RawHeight(int vertexX, int vertexY) => _heights[vertexY * VertexWidth + vertexX];

    public bool IsInsideField(float x, float y)
        => x >= FieldMin.X && y >= FieldMin.Y && x <= FieldMax.X && y <= FieldMax.Y;

    public float HeightAt(float x, float y)
    {
        Locate(x, y, out var ix, out var iy, out var fx, out var fy);
        float P(int px, int py) => _heights[py * VertexWidth + px];
        var p0 = P(ix, iy);
        var p1 = P(ix + 1, iy);
        var p2 = P(ix + 1, iy + 1);
        var p3 = P(ix, iy + 1);
        var raw = fy > fx
            ? p3 + (1f - fy) * (p0 - p3) + fx * (p2 - p3)
            : p1 + fy * (p2 - p1) + (1f - fx) * (p0 - p1);
        return raw * HeightScale;
    }

    /// <summary>Unit normal of the triangle under the point (flat per triangle, like the collision surface).</summary>
    public Vector3 NormalAt(float x, float y)
    {
        Locate(x, y, out var ix, out var iy, out var fx, out var fy);
        float P(int px, int py) => _heights[py * VertexWidth + px] * HeightScale;
        var p0 = P(ix, iy);
        var p1 = P(ix + 1, iy);
        var p2 = P(ix + 1, iy + 1);
        var p3 = P(ix, iy + 1);
        // Upper-left triangle (p0, p2, p3) or lower-right triangle (p0, p1, p2); both have the same x/y spans of one cell.
        var slopeX = fy > fx ? p2 - p3 : p1 - p0;
        var slopeY = fy > fx ? p3 - p0 : p2 - p1;
        return Vector3.Normalize(new Vector3(-slopeX * CellSize, -slopeY * CellSize, CellSize * CellSize));
    }

    private void Locate(float x, float y, out int ix, out int iy, out float fx, out float fy)
    {
        var gx = Math.Clamp(x / CellSize + Border, 0f, VertexWidth - 1);
        var gy = Math.Clamp(y / CellSize + Border, 0f, VertexHeight - 1);
        ix = Math.Min((int)MathF.Floor(gx), VertexWidth - 2);
        iy = Math.Min((int)MathF.Floor(gy), VertexHeight - 2);
        fx = gx - ix;
        fy = gy - iy;
    }
}

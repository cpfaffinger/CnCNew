using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Client.Stride.Layers.Terrain;

internal readonly record struct ChunkMergeResult(TerrainVertex[] Vertices, ushort[] Indices, int BlendCells, int ExtraCells, int DiagonalMismatches);

/// <summary>
/// Folds the three passes of a <see cref="TerrainChunk"/> (base, blend, 3-way blend; each with four vertices per cell) into one vertex array:
/// the base pass fixes vertices and indices, the blend and 3-way tiles are attached to their cells as extra UV sets with the vertex alpha as
/// weight. Cells without a blend tile get weight 0. One opaque pass replaces the three draw passes of the reference; it avoids depth-sorting
/// the alpha-blended layers against each other and against water. Stride-free, so the geometry can be checked without a GPU.
/// </summary>
internal static class TerrainMeshMerger
{
    public const float CellSize = 10f;

    public static ChunkMergeResult Merge(TerrainRenderModel model, TerrainChunk chunk, Vector2 fogScale, byte[] lightTable)
    {
        var basePass = chunk.Base;
        var cellCount = chunk.CellsX * chunk.CellsY;
        if (basePass.Positions.Length != cellCount * 4 || basePass.Indices.Length != cellCount * 6)
            throw new InvalidDataException($"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): base pass has {basePass.Positions.Length} vertices / {basePass.Indices.Length} indices for {cellCount} cells.");
        var vertices = new TerrainVertex[basePass.Positions.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var position = basePass.Positions[i];
            var color = basePass.Colors[i];
            var uv = basePass.Uv[i];
            vertices[i] = new TerrainVertex
            {
                Position = position,
                Color0 = PackedColor.Pack(lightTable[color.R], lightTable[color.G], lightTable[color.B], 0),
                Color1 = 0,
                Uv0 = uv,
                Uv1 = uv,
                Uv2 = uv,
                Fog = new Vector2(position.X * fogScale.X, position.Y * fogScale.Y),
            };
        }
        var indices = basePass.Indices.ToArray();

        var mismatches = 0;
        var blendCells = chunk.Blend is { } blend ? Attach(model, chunk, blend, vertices, indices, second: false, ref mismatches) : 0;
        mismatches = 0;
        var extraCells = chunk.ExtraBlend is { } extra ? Attach(model, chunk, extra, vertices, indices, second: true, ref mismatches) : 0;
        return new ChunkMergeResult(vertices, indices, blendCells, extraCells, mismatches);
    }

    private static int Attach(TerrainRenderModel model, TerrainChunk chunk, TerrainMesh pass, TerrainVertex[] vertices, ushort[] baseIndices, bool second,
        ref int diagonalMismatches)
    {
        if (pass.Positions.Length % 4 != 0 || pass.Indices.Length != pass.Positions.Length / 4 * 6)
            throw new InvalidDataException($"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): overlay pass has {pass.Positions.Length} vertices / {pass.Indices.Length} indices.");
        var cells = pass.Positions.Length / 4;
        var seen = new HashSet<int>();
        for (var k = 0; k < cells; k++)
        {
            var first = pass.Positions[k * 4];
            var cellX = (int)MathF.Round(first.X / CellSize) + model.Border - chunk.FirstCellX;
            var cellY = (int)MathF.Round(first.Y / CellSize) + model.Border - chunk.FirstCellY;
            if ((uint)cellX >= (uint)chunk.CellsX || (uint)cellY >= (uint)chunk.CellsY)
                throw new InvalidDataException($"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): overlay cell at {first} lies outside the chunk.");
            var slot = cellY * chunk.CellsX + cellX;
            if (!seen.Add(slot)) throw new InvalidDataException($"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): overlay pass draws cell {slot} twice.");
            var baseVertex = slot * 4;
            for (var corner = 0; corner < 4; corner++)
            {
                if (pass.Positions[k * 4 + corner] != vertices[baseVertex + corner].Position)
                    throw new InvalidDataException($"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): overlay vertex does not coincide with the base vertex of cell {slot}.");
                ref var vertex = ref vertices[baseVertex + corner];
                var uv = pass.Uv[k * 4 + corner];
                var alpha = pass.Colors[k * 4 + corner].A;
                if (second)
                {
                    vertex.Uv2 = uv;
                    vertex.Color1 = PackedColor.WithAlpha(vertex.Color1, alpha);
                }
                else
                {
                    vertex.Uv1 = uv;
                    vertex.Color0 = PackedColor.WithAlpha(vertex.Color0, alpha);
                }
            }
            // The overlay cell may be split along the other diagonal than the base cell; the merged mesh keeps the base split.
            var baseFlipped = baseIndices[slot * 6 + 2] == slot * 4 + 3;
            var overlayFlipped = pass.Indices[k * 6 + 2] == k * 4 + 3;
            if (baseFlipped != overlayFlipped) diagonalMismatches++;
        }
        return cells;
    }

    /// <summary>Checks index ranges and that blend weights exist only where overlay tiles were attached; throws on any inconsistency.</summary>
    public static void Validate(ChunkMergeResult merged, TerrainChunk chunk)
    {
        foreach (var index in merged.Indices)
            if (index >= merged.Vertices.Length) throw new InvalidDataException($"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): index {index} beyond {merged.Vertices.Length} vertices.");
        var weighted = 0;
        for (var cell = 0; cell < merged.Vertices.Length / 4; cell++)
        {
            var any = false;
            for (var corner = 0; corner < 4; corner++) any |= PackedColor.Alpha(merged.Vertices[cell * 4 + corner].Color0) != 0;
            if (any) weighted++;
        }
        if (weighted > merged.BlendCells) throw new InvalidDataException($"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): {weighted} cells carry blend weight but only {merged.BlendCells} have a blend tile.");
    }
}

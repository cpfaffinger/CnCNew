namespace CnCNew.W3D;

public sealed record W3dFile(long Length, IReadOnlyList<W3dChunk> Chunks)
{
    /// <summary>Bytes after the last top-level chunk, fewer than a chunk header: ignored as the original's loader ignores them, and counted here.</summary>
    public int TrailingBytes { get; init; }

    /// <summary>Top-level hierarchies without the container flag: read as the original's loader reads them, which never asks for the flag, and counted here.</summary>
    public int UnmarkedHierarchies { get; init; }

    public IEnumerable<W3dChunk> AllChunks => Chunks.SelectMany(chunk => chunk.DescendantsAndSelf());
}

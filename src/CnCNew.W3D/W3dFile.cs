namespace CnCNew.W3D;

public sealed record W3dFile(long Length, IReadOnlyList<W3dChunk> Chunks)
{
    public IEnumerable<W3dChunk> AllChunks => Chunks.SelectMany(chunk => chunk.DescendantsAndSelf());
}

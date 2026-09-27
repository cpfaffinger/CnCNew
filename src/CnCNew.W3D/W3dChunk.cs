namespace CnCNew.W3D;

public sealed record W3dChunk(
    uint Type,
    long HeaderOffset,
    long DataOffset,
    int Size,
    bool IsContainer,
    IReadOnlyList<W3dChunk> Children)
{
    public long EndOffset => DataOffset + Size;

    public IEnumerable<W3dChunk> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }
}

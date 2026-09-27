namespace CnCNew.Data.Ini;

public sealed record IniDocument(string Source, IReadOnlyList<IniNode> Nodes)
{
    public IEnumerable<IniBlock> Blocks => Nodes.OfType<IniBlock>();

    public IEnumerable<IniBlock> DescendantBlocks()
        => Blocks.SelectMany(DescendantsAndSelf);

    private static IEnumerable<IniBlock> DescendantsAndSelf(IniBlock block)
    {
        yield return block;
        foreach (var child in block.Children.OfType<IniBlock>())
        {
            foreach (var descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }
}


namespace CnCNew.Data.Ini;

public abstract record IniNode(IniSourceLocation Location);

public sealed record IniBlock(
    string Declaration,
    IReadOnlyList<IniNode> Children,
    IniSourceLocation Location) : IniNode(Location)
{
    public string Type => Declaration.Split([' ', '='], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
}

public sealed record IniProperty(
    string Name,
    string Value,
    IniSourceLocation Location) : IniNode(Location);

public sealed record IniStatement(
    string Text,
    IniSourceLocation Location) : IniNode(Location);


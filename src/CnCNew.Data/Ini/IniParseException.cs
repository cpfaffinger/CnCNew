namespace CnCNew.Data.Ini;

public sealed class IniParseException(string message, IniSourceLocation location)
    : FormatException($"{location.Source}({location.Line},{location.Column}): {message}")
{
    public IniSourceLocation Location { get; } = location;
}


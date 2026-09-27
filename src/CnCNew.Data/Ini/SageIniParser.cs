using System.Text;

namespace CnCNew.Data.Ini;

public static class SageIniParser
{
    public const int MaximumLineLength = 1028;
    private static readonly HashSet<string> EqualsBlockOpeners = new(StringComparer.OrdinalIgnoreCase)
    {
        "AnimationState",
        "Behavior",
        "Body",
        "ClientUpdate",
        "ConditionState",
        "Draw",
        "TransitionState",
    };
    private static readonly HashSet<string> StatementBlockOpeners = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddModule",
        "ApplyRandomForce",
        "ArmorSet",
        "Attack",
        "CreateDebris",
        "CreateObject",
        "DefaultConditionState",
        "DeliverPayload",
        "FireWeapon",
        "FXListAtBonePos",
        "ImagePart",
        "InheritableModule",
        "LightPulse",
        "Mission",
        "ParticleSystem",
        "Prerequisites",
        "RayEffect",
        "ReplaceModule",
        "Sound",
        "SideSounds",
        "TerrainScorch",
        "Tracer",
        "Turret",
        "UnitSpecificFX",
        "UnitSpecificSounds",
        "ViewShake",
        "WeaponSet",
        "Window",
    };
    private static readonly HashSet<string> TopLevelEqualsBlockOpeners = new(StringComparer.OrdinalIgnoreCase)
    {
        "DynamicGameLOD",
        "StaticGameLOD",
    };

    public static IniDocument Parse(Stream stream, string source)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        using var reader = new StreamReader(
            stream,
            Encoding.Latin1,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);
        return Parse(reader.ReadToEnd(), source);
    }

    public static IniDocument Parse(string text, string source = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var lines = ReadLines(text, source);
        var nodes = new List<IniNode>();
        var index = 0;
        while (index < lines.Count)
        {
            if (lines[index].Content.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                throw Error("Unexpected End token.", source, lines[index]);
            }

            var line = lines[index];
            if (IsInclude(line.Content) || IsConditionDirective(line.Content))
            {
                nodes.Add(new IniStatement(line.Content, Location(source, line)));
                index++;
                continue;
            }
            var equalsIndex = FindEqualsOutsideQuotes(line.Content);
            var topLevelName = equalsIndex < 0 ? line.Content : line.Content[..equalsIndex].Trim();
            var topLevelBlockWithEquals = equalsIndex >= 0
                && TopLevelEqualsBlockOpeners.Contains(topLevelName)
                && index + 1 < lines.Count
                && lines[index + 1].Indent > line.Indent;
            if (equalsIndex >= 0 && !topLevelBlockWithEquals)
            {
                nodes.Add(ParseLeaf(line, source));
                index++;
            }
            else
            {
                nodes.Add(ParseBlock(lines, ref index, source));
            }
        }

        return new IniDocument(source, nodes);
    }

    private static IniBlock ParseBlock(IReadOnlyList<SourceLine> lines, ref int index, string source)
    {
        var header = lines[index++];
        var children = new List<IniNode>();
        while (index < lines.Count)
        {
            var line = lines[index];
            if (line.Content.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                return new IniBlock(header.Content, children, Location(source, header));
            }

            var equalsIndex = FindEqualsOutsideQuotes(line.Content);
            var fieldName = equalsIndex < 0 ? line.Content : line.Content[..equalsIndex].Trim();
            var indentedStructuredBlock = equalsIndex < 0
                && line.Indent > header.Indent
                && index + 1 < lines.Count
                && lines[index + 1].Indent > line.Indent
                && HasClosingEndAtIndent(lines, index + 1, line.Indent);
            var isNestedBlock = equalsIndex < 0
                ? StatementBlockOpeners.Contains(FirstToken(fieldName)) || indentedStructuredBlock
                : EqualsBlockOpeners.Contains(fieldName);
            if (isNestedBlock)
            {
                children.Add(ParseBlock(lines, ref index, source));
                continue;
            }

            children.Add(ParseLeaf(line, source));
            index++;
        }

        throw new IniParseException(
            $"Block '{header.Content}' is missing its End token at end of file.",
            Location(source, header));
    }

    private static IniNode ParseLeaf(SourceLine line, string source)
    {
        var equalsIndex = FindEqualsOutsideQuotes(line.Content);
        var location = Location(source, line);
        if (equalsIndex < 0)
        {
            return new IniStatement(line.Content, location);
        }

        var name = line.Content[..equalsIndex].Trim();
        if (name.Length == 0)
        {
            throw Error("Property name is empty.", source, line);
        }

        return new IniProperty(name, line.Content[(equalsIndex + 1)..].Trim(), location);
    }

    private static bool IsInclude(string value)
        => value.StartsWith("#include ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("include ", StringComparison.OrdinalIgnoreCase);

    private static bool IsConditionDirective(string value)
        => value.StartsWith("#if ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("#ifdef ", StringComparison.OrdinalIgnoreCase)
            || value.Equals("#else", StringComparison.OrdinalIgnoreCase)
            || value.Equals("#endif", StringComparison.OrdinalIgnoreCase);

    private static List<SourceLine> ReadLines(string text, string source)
    {
        var result = new List<SourceLine>();
        using var reader = new StringReader(text);
        var number = 0;
        while (reader.ReadLine() is { } rawLine)
        {
            number++;
            // SAGE asserts on tabs in debug builds, then normalizes control whitespace to spaces in
            // the actual reader. Two shipped Zero Hour mission INIs rely on that retail behavior.
            var normalizedLine = rawLine.Replace('\t', ' ');
            if (normalizedLine.Length > MaximumLineLength)
            {
                throw new IniParseException(
                    $"Line exceeds the SAGE limit of {MaximumLineLength} characters.",
                    new IniSourceLocation(source, number, MaximumLineLength + 1));
            }

            // The original reader treats the first semicolon as a comment marker, even inside quotes.
            var comment = normalizedLine.IndexOf(';');
            var contentWithIndent = (comment >= 0 ? normalizedLine[..comment] : normalizedLine).TrimEnd();
            if (contentWithIndent.Length == 0)
            {
                continue;
            }

            var indent = contentWithIndent.Length - contentWithIndent.TrimStart(' ').Length;
            var content = contentWithIndent[indent..];
            if (content.Length > 0)
            {
                result.Add(new SourceLine(number, indent, content));
            }
        }

        return result;
    }

    private static int FindEqualsOutsideQuotes(string line)
    {
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"')
            {
                quoted = !quoted;
            }
            else if (line[index] == '=' && !quoted)
            {
                return index;
            }
        }

        return -1;
    }

    private static string FirstToken(string line)
    {
        var separator = line.IndexOf(' ');
        return separator < 0 ? line : line[..separator];
    }

    private static bool HasClosingEndAtIndent(IReadOnlyList<SourceLine> lines, int startIndex, int indent)
    {
        for (var index = startIndex; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!line.Content.Equals("End", StringComparison.OrdinalIgnoreCase) || line.Indent > indent)
            {
                continue;
            }

            return line.Indent == indent;
        }

        return false;
    }

    private static IniParseException Error(string message, string source, SourceLine line)
        => new(message, new IniSourceLocation(source, line.Number, line.Indent + 1));

    private static IniSourceLocation Location(string source, SourceLine line)
        => new(source, line.Number, line.Indent + 1);

    private sealed record SourceLine(int Number, int Indent, string Content);
}


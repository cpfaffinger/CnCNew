using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Game.Shell;

/// <summary>
/// The localized words of the skirmish setup: the army names (<c>PlayerTemplate.DisplayName</c>, label <c>INI:FactionAmerica</c> and the like),
/// the color names (<c>MultiplayerColor.TooltipName</c>, <c>Color:Gold</c>) and the team names (<c>Team:0</c> .. <c>Team:4</c>, <c>GUI:Random</c>,
/// <c>GUI:Open</c>). The setup used to show the internal names (<c>FactionAmerica</c>, <c>Color 1</c>); every label the table lacks falls back to the
/// internal name and is counted in <see cref="ShellTextSource.RawNameHits"/>, never silently.
/// </summary>
public sealed class ShellSkirmishText
{
    private readonly ImmutableArray<GameDefinition> _definitions;
    private readonly ShellTextSource? _text;

    public ShellSkirmishText(CompiledDefinitionGraph graph, ShellTextSource? text)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _definitions = graph.Definitions;
        _text = text;
    }

    public string Random => Resolve("GUI:Random", "Random");
    public string Open => Resolve("GUI:Open", "Open");
    public string Team(int index) => Resolve($"Team:{index}", index == 0 ? "None" : index.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The army name of a player template (<c>FactionAmerica</c> to <c>USA</c>).</summary>
    public string Faction(string templateName)
    {
        var definition = _definitions.FirstOrDefault(value => value.Id.Kind == DefinitionKind.Faction && value.Id.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
        var label = definition?.Fields.FirstOrDefault(field => field.Name.Equals("DisplayName", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim().Trim('"');
        return label is { Length: > 0 } ? Resolve(label, templateName) : Count(templateName, templateName);
    }

    /// <summary>The names of the multiplayer colors in file order (<c>MultiplayerColor</c> blocks of <c>multiplayer.ini</c>).</summary>
    public ImmutableArray<string> ColorNames()
    {
        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var definition in _definitions
                     .Where(value => value.Id.Kind == DefinitionKind.Compatibility && value.Id.Name.StartsWith("multiplayercolor/", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(value => value.Location.Line))
        {
            var label = definition.Fields.FirstOrDefault(field => field.Name.Equals("TooltipName", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim().Trim('"');
            var fallback = definition.Id.Name[(definition.Id.Name.IndexOf('/') + 1)..];
            names.Add(label is { Length: > 0 } ? Resolve(label, fallback) : Count(fallback, fallback));
        }
        return names.ToImmutable();
    }

    private string Resolve(string label, string fallback)
    {
        if (_text is not null && _text.TryResolve(label, out var value)) return value;
        return Count(label, fallback);
    }

    private string Count(string label, string fallback)
    {
        _text?.CountRawName(label);
        return fallback;
    }
}

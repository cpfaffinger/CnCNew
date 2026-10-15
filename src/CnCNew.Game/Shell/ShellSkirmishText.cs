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

    /// <summary><c>GUI:Closed</c>, the second entry of every opponent slot (<c>SkirmishGameOptionsMenuInit</c>).</summary>
    public string Closed => Resolve("GUI:Closed", "Closed");

    /// <summary>
    /// <c>GUI:MediumAI</c> ("Medium Army"): the one computer opponent a slot offers. The original lists <c>GUI:EasyAI</c>, <c>GUI:MediumAI</c> and <c>GUI:HardAI</c>
    /// (<c>SLOT_EASY_AI/SLOT_MED_AI/SLOT_BRUTAL_AI</c>), but what tells them apart in a skirmish is the difficulty flags of the skirmish AI scripts, which this build does not run;
    /// the data-backed difficulty bonuses (<c>Player::friend_applyDifficultyBonusesForObject</c>) apply to campaign games only. The medium entry is kept because the
    /// simulation's one computer player corresponds to <c>DIFFICULTY_NORMAL</c> (<c>docs/SIMULATION-CORE.md</c> §35).
    /// </summary>
    public string ComputerArmy => Resolve("GUI:MediumAI", "Medium Army");

    /// <summary><c>GUI:EasyAI</c> and <c>GUI:HardAI</c>: the two other armies of the original. The original AI plays them by its scripts, the built-in AI by its strength.</summary>
    public string EasyArmy => Resolve("GUI:EasyAI", "Easy Army");
    public string HardArmy => Resolve("GUI:HardAI", "Hard Army");

    /// <summary>The tactics profiles of the built-in AI (Stage 3, S3-M1), at medium strength. The original has no such entries and no text for them; English only.</summary>
    public static string RushArmy => "Rush Army";
    public static string TurtleArmy => "Turtle Army";
    public static string EcoArmy => "Eco Army";

    /// <summary><c>GUI:Player</c>: the name the original puts into the player name field when the preferences hold none.</summary>
    public string Player => Resolve("GUI:Player", "Player");
    public string Team(int index) => Resolve($"Team:{index}", index == 0 ? "None" : index.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The army name of a player template (<c>FactionAmerica</c> to <c>USA</c>).</summary>
    public string Faction(string templateName)
    {
        var definition = _definitions.FirstOrDefault(value => value.Id.Kind == DefinitionKind.Faction && value.Id.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
        var label = definition?.Fields.FirstOrDefault(field => field.Name.Equals("DisplayName", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim().Trim('"');
        return label is { Length: > 0 } ? Resolve(label, templateName) : Count(templateName, templateName);
    }

    /// <summary>
    /// The three medallion images of a player template (<c>MedallionRegular</c>, <c>MedallionHilite</c>, <c>MedallionSelect</c> of <c>PlayerTemplate.ini</c>, the originals' <c>getMedallionNormal</c>,
    /// <c>getMedallionHilite</c>, <c>getMedallionSelected</c>); <see langword="null"/> when the template or one of the three keys is missing (the caller counts and shows nothing).
    /// </summary>
    public (string Regular, string Hilite, string Select)? Medallions(string templateName)
    {
        var definition = _definitions.FirstOrDefault(value => value.Id.Kind == DefinitionKind.Faction && value.Id.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
        string? Field(string name) => definition?.Fields.FirstOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim().Trim('"');
        return Field("MedallionRegular") is { Length: > 0 } regular && Field("MedallionHilite") is { Length: > 0 } hilite && Field("MedallionSelect") is { Length: > 0 } select ? (regular, hilite, select) : null;
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

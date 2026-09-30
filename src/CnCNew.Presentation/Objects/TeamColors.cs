using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// House colors per player. Resolution order: an explicit override, the <c>PreferredColor</c> of the player's <c>PlayerTemplate</c> when the
/// graph has it and no earlier player already uses that color, otherwise the next unused entry of <see cref="Palette"/>. The system player
/// and unknown owners get <see cref="Neutral"/>. Deterministic: players are resolved in their configuration order.
/// </summary>
public sealed class TeamColors
{
    /// <summary>A project-own palette of well separated house colors (not taken from the game data).</summary>
    public static ImmutableArray<ColorValue> Palette { get; } =
    [
        new(40, 90, 230), new(220, 40, 40), new(40, 190, 60), new(240, 200, 30),
        new(240, 130, 20), new(30, 200, 220), new(150, 60, 210), new(240, 110, 190),
    ];

    public static ColorValue Neutral { get; } = new(200, 200, 200);

    private readonly ImmutableDictionary<int, ColorValue> _colors;

    private TeamColors(ImmutableDictionary<int, ColorValue> colors) => _colors = colors;

    public ColorValue Of(PlayerId player) => _colors.TryGetValue(player.Value, out var color) ? color : Neutral;

    public static TeamColors Resolve(IEnumerable<PlayerConfiguration> players, CompiledDefinitionGraph? graph, IReadOnlyDictionary<int, ColorValue>? overrides = null)
    {
        var result = ImmutableDictionary.CreateBuilder<int, ColorValue>();
        var used = new List<ColorValue>();
        foreach (var player in players)
        {
            if (player.Id.IsSystem) continue;
            ColorValue color;
            if (overrides is not null && overrides.TryGetValue(player.Id.Value, out var forced)) color = forced;
            else if (PreferredColor(graph, player.Faction) is { } preferred && !used.Contains(preferred)) color = preferred;
            else color = Palette.FirstOrDefault(entry => !used.Contains(entry), Palette[used.Count % Palette.Length]);
            used.Add(color);
            result[player.Id.Value] = color;
        }
        return new TeamColors(result.ToImmutable());
    }

    private static ColorValue? PreferredColor(CompiledDefinitionGraph? graph, string? faction)
    {
        if (graph is null || string.IsNullOrEmpty(faction)) return null;
        var id = new DefinitionId(DefinitionKind.Faction, faction);
        var definition = graph.Definitions.FirstOrDefault(value => value.Id == id);
        if (definition is null) return null;
        ColorValue? color = null;
        foreach (var field in definition.Fields)
            if (field.Name.Equals("PreferredColor", StringComparison.OrdinalIgnoreCase) && field.Value.Value is ColorValue value) color = value;
        return color;
    }
}

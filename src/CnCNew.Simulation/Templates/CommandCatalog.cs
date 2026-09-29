using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Simulation.Templates;

/// <summary>
/// The build buttons of the <c>CommandSet</c>/<c>CommandButton</c> definitions (<c>ControlBar</c> data): which objects a producer or
/// builder offers. Only <c>UNIT_BUILD</c> and <c>DOZER_CONSTRUCT</c> buttons matter to the simulation; everything else on a set
/// (rally point, sell, upgrades, special powers) is presentation or handled elsewhere.
/// </summary>
public sealed class CommandCatalog
{
    private readonly ImmutableDictionary<string, ImmutableArray<string>> _buildsBySet;

    private CommandCatalog(ImmutableDictionary<string, ImmutableArray<string>> buildsBySet) => _buildsBySet = buildsBySet;

    /// <summary>Object names the named command set can build or construct; empty for an unknown set.</summary>
    public ImmutableArray<string> BuildsOf(string commandSet) => _buildsBySet.TryGetValue(commandSet, out var builds) ? builds : [];

    public static CommandCatalog Build(CompiledDefinitionGraph graph)
    {
        var buttons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var button in graph.Definitions.OfType<CommandButtonDefinition>())
        {
            var command = button.Fields.LastOrDefault(field => field.Name.Equals("Command", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText ?? string.Empty;
            if (!command.Equals("UNIT_BUILD", StringComparison.OrdinalIgnoreCase) && !command.Equals("DOZER_CONSTRUCT", StringComparison.OrdinalIgnoreCase)) continue;
            var target = button.Fields.LastOrDefault(field => field.Name.Equals("Object", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText;
            if (!string.IsNullOrWhiteSpace(target)) buttons[button.OriginalName] = target.Trim();
        }
        var sets = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in graph.Definitions.OfType<CommandSetDefinition>())
        {
            var builds = ImmutableArray.CreateBuilder<string>();
            foreach (var field in set.Fields)
                if (buttons.TryGetValue(field.Value.OriginalText.Trim(), out var target) && !builds.Contains(target, StringComparer.OrdinalIgnoreCase)) builds.Add(target);
            sets[set.OriginalName] = builds.ToImmutable();
        }
        return new CommandCatalog(sets.ToImmutable());
    }
}

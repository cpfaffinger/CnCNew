using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Simulation.Templates;

/// <summary>
/// The buttons of the <c>CommandSet</c>/<c>CommandButton</c> definitions (<c>ControlBar</c> data) the simulation itself needs: which objects a
/// producer or builder offers (<c>UNIT_BUILD</c>/<c>DOZER_CONSTRUCT</c>), and which <c>SpecialPower</c> a named ability button fires
/// (<c>SPECIAL_POWER</c>/<c>SPECIAL_POWER_FROM_SHORTCUT</c>/<c>SPECIAL_POWER_CONSTRUCT(_FROM_SHORTCUT)</c>, for
/// <c>*_USE_COMMANDBUTTON_ABILITY*</c> script actions, <c>ScriptEngine.Actions.cs</c>). Everything else on a button (rally point, sell,
/// upgrades, non-power commands) is presentation or handled elsewhere.
/// </summary>
/// <summary>What a command button does, as far as scripts that name buttons need it: its <c>Command</c> type, the <c>Upgrade</c> it names and the <c>WeaponSlot</c> of a <c>FIRE_WEAPON</c> button (empty when absent).</summary>
public sealed record CommandButtonInfo(string Command, string Upgrade, string WeaponSlot);

public sealed class CommandCatalog
{
    private readonly ImmutableDictionary<string, CommandButtonInfo> _buttonsByName = ImmutableDictionary<string, CommandButtonInfo>.Empty;
    private readonly ImmutableDictionary<string, ImmutableArray<string>> _buildsBySet;
    private readonly ImmutableDictionary<string, string> _specialPowerByButton;

    private CommandCatalog(ImmutableDictionary<string, ImmutableArray<string>> buildsBySet, ImmutableDictionary<string, string> specialPowerByButton,
        ImmutableDictionary<string, CommandButtonInfo> buttonsByName)
    {
        _buildsBySet = buildsBySet;
        _specialPowerByButton = specialPowerByButton;
        _buttonsByName = buttonsByName;
    }

    /// <summary>Any command button by definition name (<c>TheControlBar-&gt;findCommandButton</c>), for scripts that use a button that is not a special power; <see langword="false"/> for an unknown name.</summary>
    public bool TryGetButton(string buttonName, out CommandButtonInfo info) => _buttonsByName.TryGetValue(buttonName, out info!);

    /// <summary>Object names the named command set can build or construct; empty for an unknown set.</summary>
    public ImmutableArray<string> BuildsOf(string commandSet) => _buildsBySet.TryGetValue(commandSet, out var builds) ? builds : [];

    /// <summary>
    /// The <c>SpecialPowerTemplate</c> a command button of this name fires, for the script actions that name a button globally by its definition
    /// name (<c>TheControlBar-&gt;findCommandButton</c>); <see langword="false"/> for an unknown button or one that is not a special-power command.
    /// </summary>
    public bool TryGetSpecialPowerAbility(string buttonName, out string specialPower) => _specialPowerByButton.TryGetValue(buttonName, out specialPower!);

    public static CommandCatalog Build(CompiledDefinitionGraph graph)
    {
        var buttons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var abilities = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        var all = ImmutableDictionary.CreateBuilder<string, CommandButtonInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var button in graph.Definitions.OfType<CommandButtonDefinition>())
        {
            var command = button.Fields.LastOrDefault(field => field.Name.Equals("Command", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText ?? string.Empty;
            all[button.OriginalName] = new CommandButtonInfo(command.Trim(),
                button.Fields.LastOrDefault(field => field.Name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim() ?? string.Empty,
                button.Fields.LastOrDefault(field => field.Name.Equals("WeaponSlot", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim() ?? string.Empty);
            if (command.StartsWith("SPECIAL_POWER", StringComparison.OrdinalIgnoreCase))
            {
                var power = button.Fields.LastOrDefault(field => field.Name.Equals("SpecialPower", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText;
                if (!string.IsNullOrWhiteSpace(power)) abilities[button.OriginalName] = power.Trim();
            }
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
        return new CommandCatalog(sets.ToImmutable(), abilities.ToImmutable(), all.ToImmutable());
    }
}

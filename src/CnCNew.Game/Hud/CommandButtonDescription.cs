using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.World;
using CnCNew.Ui.Text;

namespace CnCNew.Game.Hud;

/// <summary>The three texts of the original's command button popup (<c>ControlBarPopupDescription.wnd</c>: <c>StaticTextName</c>, <c>StaticTextCost</c>, <c>StaticTextDescription</c>).</summary>
public sealed record CommandButtonTexts(string Name, string? Cost, string Description)
{
    /// <summary>The texts as the lines of one pointer tooltip: name, cost when there is one, description when there is one.</summary>
    public string ToTooltip()
        => string.Join("\n", new[] { Name, Cost, Description }.Where(part => part is { Length: > 0 }));
}

/// <summary>
/// What the description popup of a command button says (<c>ControlBar::populateBuildTooltipLayout</c> in ControlBarPopupDescription.cpp), from the same data:
/// <list type="bullet">
/// <item><description>the name (<c>TextLabel</c>; the popup underlines the hot key, here the <c>&amp;</c> is dropped) and the description (<c>DescriptLabel</c>);</description></item>
/// <item><description>for a unit or structure: the cost (<c>TOOLTIP:Cost</c>) when it costs something, behind the description after an empty line why it cannot be bought now
/// (<c>TOOLTIP:TooltipNotEnoughMoneyToBuild</c>, <c>TOOLTIP:TooltipCannotPurchaseBecauseQueueFull</c>; only when the button has a description, as in the original), and on a new line
/// <c>CONTROLBAR:Requirements</c> with what is missing: one entry per <c>Prerequisites</c> line the player does not meet, separated by ", " - the display name of the required
/// object, for a line with alternatives the last two joined by <c>CONTROLBAR:OrRequirement</c> (<c>ProductionPrerequisite::getRequiresList</c> names only those two), for a science
/// line <c>CONTROLBAR:GeneralsPromotion</c>;</description></item>
/// <item><description>for an upgrade: <c>PurchasedLabel</c> (or <c>TOOLTIP:AlreadyUpgradedDefault</c>) instead of the description once the player or the selected object has it;
/// otherwise the cost, the queue/money reason, and "Requires: General's Promotion" when a science of the button is missing;</description></item>
/// <item><description>for the overcharge button the state (<c>TOOLTIP:TooltipNukeReactorOverChargeIsOn</c>/<c>...IsOff</c>) on a new line.</description></item>
/// </list>
/// Not built: the science purchase texts (<c>ScienceStore::getNameAndDescription</c>; this client has no generals' powers panel), the conflicting-upgrade text (the simulation has no
/// <c>affectedByUpgrade</c>), the "maximum number" and "airfield full" reasons (no such state reaches the command bar model).
/// </summary>
public sealed class CommandButtonDescription
{
    private readonly LocalizedText _text;
    private readonly Dictionary<string, string> _objectLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _purchasedLabels = new(StringComparer.OrdinalIgnoreCase);

    public CommandButtonDescription(CompiledDefinitionGraph graph, LocalizedText text)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        foreach (var definition in graph.Definitions)
        {
            var (target, field) = definition.Id.Kind switch
            {
                DefinitionKind.Object => (_objectLabels, "DisplayName"),
                DefinitionKind.CommandButton => (_purchasedLabels, "PurchasedLabel"),
                _ => (null, string.Empty),
            };
            if (target is null) continue;
            var label = definition.Fields.FirstOrDefault(candidate => candidate.Name.Equals(field, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim().Trim('"');
            if (label is { Length: > 0 }) target[definition.OriginalName] = label;
        }
    }

    /// <summary><c>ThingTemplate::getDisplayName</c>: the text of the template's <c>DisplayName</c>, or <c>INI:MissingDisplayName</c> with the template name.</summary>
    public string DisplayName(string template)
        => _objectLabels.TryGetValue(template, out var label) ? _text.Get(label) : _text.Format("INI:MissingDisplayName", template);

    /// <summary>
    /// <c>ProductionPrerequisite::getRequiresList</c> for every line and the join of <c>populateBuildTooltipLayout</c>: the missing requirements of <paramref name="template"/> as one
    /// string, empty when nothing is missing.
    /// </summary>
    public string Requirements(SimulationWorld world, PlayerId player, string template)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.Templates.TryGet(template, out var info)) return string.Empty;
        var entries = new List<string>();
        foreach (var line in SimulationQueries.MissingPrerequisites(world, player, info))
        {
            if (line.IsScience) entries.Add(_text.Get("CONTROLBAR:GeneralsPromotion"));
            else if (line.Alternatives.Length >= 2) entries.Add($"{DisplayName(line.Alternatives[^2])} {_text.Get("CONTROLBAR:OrRequirement")} {DisplayName(line.Alternatives[^1])}");
            else if (line.Alternatives.Length == 1) entries.Add(DisplayName(line.Alternatives[0]));
        }
        return string.Join(", ", entries);
    }

    /// <summary>The popup texts of <paramref name="button"/> for <paramref name="player"/>, with <paramref name="selected"/> as the first selected object (<c>getFirstSelectedDrawable</c>).</summary>
    public CommandButtonTexts Build(CommandBarButton button, SimulationWorld world, PlayerId player, Entity? selected)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(world);
        var info = button.Button;
        var name = info.TextLabel.Length > 0 ? _text.Get(info.TextLabel).Replace("&", string.Empty, StringComparison.Ordinal) : string.Empty;
        var description = info.DescriptLabel.Length > 0 ? _text.Get(info.DescriptLabel) : string.Empty;
        string? cost = null;
        var isUpgradeButton = info.Kind is GuiCommandKind.PlayerUpgrade or GuiCommandKind.ObjectUpgrade;
        var hasTemplate = info.Object.Length > 0 && world.Templates.TryGet(info.Object, out _);
        var hasUpgrade = info.Upgrade.Length > 0 && world.Upgrades.TryGet(info.Upgrade, out _);
        var money = world.MoneyOf(player);

        if (description.Length > 0 && selected is not null)
        {
            if (info.Kind == GuiCommandKind.ToggleOvercharge)
            {
                foreach (var overcharge in selected.Modules.OfType<OverchargeBehavior>())
                    description += "\n" + _text.Get(overcharge.Active ? "TOOLTIP:TooltipNukeReactorOverChargeIsOn" : "TOOLTIP:TooltipNukeReactorOverChargeIsOff");
            }
            else if (hasTemplate)
            {
                // BuildAssistant::canMakeUnit: money is asked before the queue.
                if (button.Cost.Raw > 0 && money.Raw < button.Cost.Raw) description += "\n\n" + _text.Get("TOOLTIP:TooltipNotEnoughMoneyToBuild");
                else if (button.Reason == ButtonReason.QueueFull) description += "\n\n" + _text.Get("TOOLTIP:TooltipCannotPurchaseBecauseQueueFull");
            }
            else if (hasUpgrade && isUpgradeButton)
            {
                if (button.Reason == ButtonReason.QueueFull) description += "\n\n" + _text.Get("TOOLTIP:TooltipCannotPurchaseBecauseQueueFull");
                else if (button.Cost.Raw > 0 && money.Raw < button.Cost.Raw) description += "\n\n" + _text.Get("TOOLTIP:TooltipNotEnoughMoneyToBuild");
            }
        }

        if (hasTemplate && info.Kind != GuiCommandKind.PurchaseScience)
        {
            if (button.Cost.Raw > 0) cost = _text.Format("TOOLTIP:Cost", (int)button.Cost.ToDecimal());
            var requires = Requirements(world, player, info.Object);
            if (requires.Length > 0) description = Append(description, _text.Format("CONTROLBAR:Requirements", requires));
        }
        else if (hasUpgrade && world.Upgrades.TryGet(info.Upgrade, out var upgrade))
        {
            var owned = world.PlayerUpgrades(player).Has(upgrade.Index) || selected is not null && selected.CompletedUpgrades.Has(upgrade.Index);
            if (owned && isUpgradeButton)
                description = _text.Get(_purchasedLabels.TryGetValue(info.Name, out var purchased) ? purchased : "TOOLTIP:AlreadyUpgradedDefault");
            else if (!owned)
            {
                if (upgrade.BuildCost.Raw > 0) cost = _text.Format("TOOLTIP:Cost", (int)upgrade.BuildCost.ToDecimal());
                var sciences = world.PlayerSciences(player);
                if (info.Sciences.Any(science => !world.Sciences.TryGet(science, out var known) || !sciences.Has(known.Index)))
                    description = Append(description, _text.Format("CONTROLBAR:Requirements", _text.Get("CONTROLBAR:GeneralsPromotion")));
            }
        }
        return new CommandButtonTexts(name, cost, description);
    }

    /// <summary>The money window's popup (<c>CONTROLBAR:Money</c>, <c>CONTROLBAR:MoneyDescription</c>).</summary>
    public CommandButtonTexts Money() => new(_text.Get("CONTROLBAR:Money"), null, _text.Get("CONTROLBAR:MoneyDescription"));

    /// <summary>The power window's popup: <c>CONTROLBAR:Power</c> and <c>CONTROLBAR:PowerDescription</c> with the player's production and consumption (<c>Energy::getProduction</c>, <c>getConsumption</c>).</summary>
    public CommandButtonTexts Power(int production, int consumption)
        => new(_text.Get("CONTROLBAR:Power"), null, _text.Format("CONTROLBAR:PowerDescription", production, consumption));

    private static string Append(string description, string line) => description.Length == 0 ? line : description + "\n" + line;
}

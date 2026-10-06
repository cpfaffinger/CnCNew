using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;
using CnCNew.Ui.Text;

namespace CnCNew.Game.Hud;

/// <summary>
/// What the game itself tells the local player in the message list at the top left (<c>InGameUI::message</c>, the same list <c>DISPLAY_TEXT</c> writes to): every line is a label of
/// the string table, chosen where the original chooses it. A feedback or event the original prints no line for is counted in <see cref="WithoutLine"/> and shows nothing; nothing is
/// dropped unseen and no text is invented, with one marked exception (<see cref="ClientNote"/>).
/// <list type="bullet">
/// <item><description>Refused production and construction (<c>ControlBar::processCommandUI</c>, <c>PlaceEventTranslator::translateGameMessage</c>): <c>GUI:NotEnoughMoneyToBuild</c>,
/// <c>GUI:ProductionQueueFull</c>, <c>GUI:UnitMaxedOut</c>; a refused upgrade purchase <c>GUI:NotEnoughMoneyToUpgrade</c> (<c>UpgradeCenter::canAffordUpgrade</c>).
/// <c>GUI:ParkingPlacesFull</c> has no source here: the simulation has no such refusal.</description></item>
/// <item><description>An illegal building spot (<c>InGameUI::displayCantBuildMessage</c>): <c>GUI:CantBuildShroud</c> for unexplored ground, otherwise <c>GUI:CantBuildThere</c> - the
/// simulation reports one "blocked footprint" where the original tells terrain, slope, obstacles and path apart.</description></item>
/// <item><description><c>DOZER:ConstructionComplete</c> with the structure's display name (<c>DozerAIUpdate</c>, the build task's completion), <c>UPGRADE:UpgradeComplete</c> with the
/// upgrade's (<c>ProductionUpdate::update</c>; no line for an upgrade without <c>DisplayName</c>), both for the local player only. The original prints the upgrade line where a
/// production queue finishes an upgrade, not where one is granted (<c>GrantUpgradeCreate</c>, scripts, the radar a command center comes with); the simulation's event does not tell
/// the two apart, so only an upgrade the player ordered is announced (<see cref="ExpectUpgrade"/>), the others are counted.</description></item>
/// <item><description><c>RADAR:UnitUnderAttack</c>, <c>RADAR:HarvesterUnderAttack</c>, <c>RADAR:StructureUnderAttack</c>, <c>RADAR:UnderAttack</c> (<c>Radar::tryUnderAttackEvent</c>): for
/// damage another player deals to a local object, at most one line per 250 world units and 10 seconds (<c>Radar::tryEvent</c>).</description></item>
/// <item><description><c>GUI:PlayerHasBeenDefeated</c> (<c>VictoryConditions::update</c>, skirmish and network games only, see <see cref="AnnounceDefeats"/>).</description></item>
/// </list>
/// </summary>
public sealed class HudMessageFeed
{
    private const float UnderAttackDistance = 250f;
    // Radar::tryEvent: LOGICFRAMES_PER_SECOND * 10, in original frames; compared in ticks of the world.
    private const long UnderAttackFrames = CnCNew.Data.OriginalFrame.PerSecond * 10;

    private readonly LocalizedText _text;
    private readonly Dictionary<string, string> _objectLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _upgradeLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _factionLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(long Tick, Vector2 Position)> _underAttack = [];
    private readonly SortedDictionary<string, int> _withoutLine = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _orderedUpgrades = new(StringComparer.OrdinalIgnoreCase);

    public HudMessageFeed(UiMessageLog log, LocalizedText text, CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(graph);
        Log = log;
        _text = text;
        foreach (var definition in graph.Definitions)
        {
            var target = definition.Id.Kind switch
            {
                DefinitionKind.Object => _objectLabels,
                DefinitionKind.Upgrade => _upgradeLabels,
                DefinitionKind.Faction => _factionLabels,
                _ => null,
            };
            if (target is null) continue;
            var label = definition.Fields.FirstOrDefault(field => field.Name.Equals("DisplayName", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim().Trim('"');
            if (label is { Length: > 0 }) target[definition.OriginalName] = label;
        }
    }

    /// <summary>The list the lines go to; a mission shares it with its scripts' <c>DISPLAY_TEXT</c>.</summary>
    public UiMessageLog Log { get; }

    /// <summary>The frame a new line is stamped with (the clock <see cref="Log"/> is updated on).</summary>
    public Func<long> Clock { get; set; } = () => 0;

    /// <summary><c>VictoryConditions::update</c> runs in skirmish and network games only (<c>RecorderClass::isMultiplayer</c>): a campaign mission announces no defeated player.</summary>
    public bool AnnounceDefeats { get; set; } = true;

    /// <summary>How many lines were added.</summary>
    public int Shown { get; private set; }

    /// <summary>Feedback and refusals the original prints no message line for, by kind, with how often each happened. They show nothing, as in the original.</summary>
    public IReadOnlyDictionary<string, int> WithoutLine => _withoutLine;

    /// <summary><c>InGameUI::message( AsciiString label, ... )</c>: the text of the label, formatted, as a new line. A label the table lacks shows its marker and is counted by the table.</summary>
    public void Message(string label, params object?[] args)
    {
        Log.Add(args.Length == 0 ? _text.Get(label) : _text.Format(label, args), Clock());
        Shown++;
    }

    /// <summary>
    /// A line of this client's own, not of the original: something the original can do and this build cannot (a button whose command the simulation lacks). In parentheses, like the
    /// speech and movie placeholders, so it cannot be taken for a text of the game.
    /// </summary>
    public void ClientNote(string what)
    {
        Log.Add($"({what})", Clock());
        Shown++;
    }

    /// <summary>The local player ordered this upgrade (an accepted <see cref="PurchaseUpgradeCommand"/>): its completion will be announced.</summary>
    public void ExpectUpgrade(string upgrade)
    {
        ArgumentNullException.ThrowIfNull(upgrade);
        _orderedUpgrades[upgrade] = _orderedUpgrades.GetValueOrDefault(upgrade) + 1;
    }

    /// <summary>The feedback of the player's own orders (<see cref="CommandModeController.DrainFeedback"/>).</summary>
    public void Observe(CommandFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        switch (feedback.Kind)
        {
            case FeedbackKind.Rejected:
                switch (feedback.Rejection)
                {
                    case CommandRejection.InsufficientFunds:
                        Message(feedback.Detail == nameof(PurchaseUpgradeCommand) ? "GUI:NotEnoughMoneyToUpgrade" : "GUI:NotEnoughMoneyToBuild");
                        return;
                    case CommandRejection.QueueFull:
                        Message("GUI:ProductionQueueFull");
                        return;
                    case CommandRejection.ProductionLimitReached:
                        Message("GUI:UnitMaxedOut");
                        return;
                    case CommandRejection.InvalidPlacement:
                        Message("GUI:CantBuildThere");
                        return;
                    default:
                        Count($"rejected {feedback.Rejection} ({feedback.Detail})");
                        return;
                }
            case FeedbackKind.InvalidOrder:
                // Only a refused building spot carries a detail (the PlacementIssue); an order the cursor already showed as invalid prints nothing in the original.
                if (feedback.Detail.Length == 0) { Count("invalid order"); return; }
                if (!Enum.TryParse<PlacementIssue>(feedback.Detail, out var issue)) { Count($"invalid order ({feedback.Detail})"); return; }
                Message(issue switch
                {
                    PlacementIssue.Shroud => "GUI:CantBuildShroud",
                    PlacementIssue.InsufficientFunds => "GUI:NotEnoughMoneyToBuild",
                    _ => "GUI:CantBuildThere",
                });
                return;
            case FeedbackKind.Unsupported:
                ClientNote($"not supported in this build: {feedback.Detail}");
                return;
        }
    }

    /// <summary>One simulation event of a step, seen by the local player <paramref name="human"/>.</summary>
    public void Observe(SimEvent simEvent, SimulationWorld world, PlayerId human)
    {
        ArgumentNullException.ThrowIfNull(world);
        switch (simEvent.Kind)
        {
            case SimEventKind.ConstructionCompleted:
                if (world.FindEntity(simEvent.Entity) is not { } site || site.Owner != human) return;
                // DozerAIUpdate: the template's display name, or INI:MissingDisplayName with the template name.
                var siteName = _objectLabels.TryGetValue(site.Template.Name, out var siteLabel) ? _text.Get(siteLabel) : _text.Format("INI:MissingDisplayName", site.Template.Name);
                Message("DOZER:ConstructionComplete", siteName);
                return;
            case SimEventKind.UpgradeCompleted:
                var owner = world.FindEntity(simEvent.Entity)?.Owner ?? simEvent.Player;
                if (owner != human) return;
                if (_orderedUpgrades.GetValueOrDefault(simEvent.Text) is not (> 0 and var ordered)) { Count("upgrade granted, not ordered"); return; }
                _orderedUpgrades[simEvent.Text] = ordered - 1;
                if (!_upgradeLabels.TryGetValue(simEvent.Text, out var upgradeLabel)) { Count("upgrade completed without DisplayName"); return; }
                Message("UPGRADE:UpgradeComplete", _text.Get(upgradeLabel));
                return;
            case SimEventKind.PlayerDefeated:
                if (!AnnounceDefeats || simEvent.Tick <= 1) return;
                var defeated = world.Configuration.Players.FirstOrDefault(player => player.Id == simEvent.Player);
                var faction = defeated?.Faction ?? defeated?.Name ?? string.Empty;
                Message("GUI:PlayerHasBeenDefeated", _factionLabels.TryGetValue(faction, out var factionLabel) ? _text.Get(factionLabel) : defeated?.Name ?? $"Player {simEvent.Player.Value}");
                return;
            case SimEventKind.EntityDamaged:
                UnderAttack(simEvent, world, human);
                return;
        }
    }

    private void UnderAttack(SimEvent simEvent, SimulationWorld world, PlayerId human)
    {
        if (simEvent.Value <= 0 || world.FindEntity(simEvent.Entity) is not { } victim || victim.Owner != human) return;
        if (world.FindEntity(simEvent.Other) is { } attacker && attacker.Owner == human) return;
        var position = SimulationQueries.GroundOf(victim);
        var tick = simEvent.Tick;
        var window = world.Time.OriginalFrames(UnderAttackFrames);
        _underAttack.RemoveAll(entry => tick - entry.Tick >= window);
        if (_underAttack.Any(entry => Vector2.DistanceSquared(entry.Position, position) <= UnderAttackDistance * UnderAttackDistance)) return;
        _underAttack.Add((tick, position));
        var template = victim.Template;
        if (template.IsKindOf("INFANTRY") || template.IsKindOf("VEHICLE")) Message(template.IsKindOf("HARVESTER") ? "RADAR:HarvesterUnderAttack" : "RADAR:UnitUnderAttack");
        else if (template.IsKindOf("STRUCTURE") && template.IsKindOf("MP_COUNT_FOR_VICTORY")) Message("RADAR:StructureUnderAttack");
        else Message("RADAR:UnderAttack");
    }

    private void Count(string what) => _withoutLine[what] = _withoutLine.GetValueOrDefault(what) + 1;
}

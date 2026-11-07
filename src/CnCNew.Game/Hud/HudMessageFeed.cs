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
    // Radar.h RadarEventType; the ordinals are what a script passes.
    private const int EventConstruction = 1, EventUpgrade = 2, EventUnderAttack = 3, EventInformation = 4, EventBeaconPulse = 5, EventInfiltration = 6, EventBattlePlan = 7,
        EventStealthDiscovered = 8, EventStealthNeutralized = 9, EventFake = 10;
    // Radar::MAX_RADAR_EVENTS: the table is a ring, the oldest event drops off.
    private const int MaxRadarEvents = 64;

    /// <summary><c>Radar::m_event</c>: the events made so far, oldest first, by type, tick and place. <c>Radar::tryEvent</c> looks at it for a recent one of its kind.</summary>
    private readonly List<(int Type, long Tick, Vector2 Position, bool FromScript)> _radarEvents = [];

    /// <summary>
    /// <c>Radar::getLastEventLoc</c>: where the last radar event happened (<c>Radar::createEvent</c>, everything but a beacon's pulse): a building finished (<c>RADAR_EVENT_CONSTRUCTION</c>),
    /// an upgrade done (<c>UPGRADE</c>), an attack (<c>UNDER_ATTACK</c>, as rationed as its message), a beacon of an ally (<c>INFORMATION</c>), an overcharge that gave out
    /// (<c>INFORMATION</c>), an infiltration of the local player's object (<c>INFILTRATION</c>: a hijack, a car bomb, a capture, a hack, a sabotage), a strategy center unpacking
    /// a plan, of whoever's (<c>BATTLE_PLAN</c>), a stealth object found or lost (<c>STEALTH_DISCOVERED</c>, <c>STEALTH_NEUTRALIZED</c>), a unit of the local player lost
    /// (<c>FAKE</c>, "so the user can use the spacebar to quickly jump to this"), or an event a script made (<see cref="NoteRadarEvent"/>). The space bar looks at it
    /// (<c>MSG_META_VIEW_LAST_RADAR_EVENT</c>); null before the first.
    /// </summary>
    public Vector2? LastRadarEvent { get; private set; }

    /// <summary>A radar event a script made (<c>RADAR_CREATE_EVENT</c>, <c>OBJECT_CREATE_RADAR_EVENT</c>, <c>TEAM_CREATE_RADAR_EVENT</c>: <c>ScriptActions::doRadarCreateEvent</c>), of the raw <c>RadarEventType</c> the script passed.</summary>
    public void NoteRadarEvent(Vector2 position, int type, long tick) => CreateEvent(type, position, tick, fromScript: true);

    /// <summary><c>Radar::createEvent</c>: the event is made, whoever it is for; it becomes the last one unless it is a beacon's pulse.</summary>
    private void CreateEvent(int type, Vector2 position, long tick, bool fromScript = false)
    {
        _radarEvents.Add((type, tick, position, fromScript));
        if (_radarEvents.Count > MaxRadarEvents) _radarEvents.RemoveAt(0);
        if (type != EventBeaconPulse) LastRadarEvent = position;
    }

    /// <summary>
    /// <c>Radar::tryEvent</c>: creates the event unless one of its type happened within 250 units and ten seconds before; true when it was made. A script's event with a type outside the table is
    /// made as it is (<c>createEvent</c> only warns about a type without colours).
    /// </summary>
    private bool TryEvent(int type, Vector2 position, SimulationWorld world, long tick)
    {
        var window = world.Time.OriginalFrames(UnderAttackFrames);
        _radarEvents.RemoveAll(entry => tick - entry.Tick >= window);
        if (_radarEvents.Any(entry => entry.Type == type && Vector2.DistanceSquared(entry.Position, position) <= UnderAttackDistance * UnderAttackDistance)) return false;
        CreateEvent(type, position, tick);
        return true;
    }

    // Radar.cpp radarColorLookupTable: the two colours an event blinks between on the minimap, by type (a fake event has none).
    private static readonly (byte R, byte G, byte B)[][] EventColours =
    [
        [], [(128, 128, 255), (128, 255, 255)], [(128, 0, 64), (255, 185, 220)], [(255, 0, 0), (255, 128, 128)], [(255, 255, 0), (255, 255, 128)], [(255, 255, 0), (255, 255, 128)],
        [(0, 255, 255), (128, 255, 255)], [(255, 255, 255), (255, 255, 255)], [(0, 255, 0), (0, 128, 0)], [(0, 255, 0), (0, 128, 0)], [],
    ];

    /// <summary>
    /// The radar events the minimap shows at <paramref name="tick"/>: those made here, not by a script (the director shows those), that are not over (<c>Radar::createEvent</c>'s four seconds), with the
    /// colour of the blink's <paramref name="phase"/> (<c>radarColorLookupTable</c>). A fake event is not drawn.
    /// </summary>
    public IEnumerable<(Vector2 Position, byte R, byte G, byte B)> RadarEventsToDraw(long tick, SimulationWorld world, bool phase)
    {
        var lifetime = world.Time.OriginalFrames(4 * CnCNew.Data.OriginalFrame.PerSecond);
        foreach (var (type, created, position, fromScript) in _radarEvents.ToArray())
        {
            if (fromScript || tick - created >= lifetime || type < 0 || type >= EventColours.Length || EventColours[type].Length == 0) continue;
            var (r, g, b) = EventColours[type][phase ? 1 : 0];
            yield return (position, r, g, b);
        }
    }
    /// <summary><c>Radar::tryInfiltrationEvent</c>: only an infiltration against the local player's own object; a radar event and <c>RADAR:Infiltration</c>.</summary>
    private void Infiltration(Entity? victim, PlayerId human, long tick)
    {
        if (victim is null || victim.Owner != human) return;
        CreateEvent(EventInfiltration, SimulationQueries.GroundOf(victim), tick);
        Message("RADAR:Infiltration");
    }
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

    /// <summary>The name of a player of a room, where a server named one; the rest go by the world's name.</summary>
    public Func<PlayerId, string?> PlayerName { get; set; } = _ => null;

    /// <summary>A beacon the local player may know of was placed; true when it is an ally's. The audio layer plays the original's two sounds.</summary>
    public event Action<bool>? BeaconPlaced;

    /// <summary>The display name of a faction (<c>PlayerTemplate::getDisplayName</c>), or its own name without one.</summary>
    public string FactionText(string faction) => _factionLabels.TryGetValue(faction, out var label) ? _text.Get(label) : faction;

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
                    case CommandRejection.TooManyBeacons:
                        Message("GUI:TooManyBeacons");
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
                    PlacementIssue.RestrictedTerrain => "GUI:CantBuildRestrictedTerrain",
                    PlacementIssue.NotFlatEnough => "GUI:CantBuildNotFlatEnough",
                    PlacementIssue.TooCloseToSupplies => "GUI:CantBuildTooCloseToSupplies",
                    PlacementIssue.NoClearPath => "GUI:CantBuildNoClearPath",
                    PlacementIssue.BlockedFootprint => "GUI:CantBuildObjectsInTheWay",
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
                CreateEvent(EventConstruction, SimulationQueries.GroundOf(site), simEvent.Tick);
                return;
            case SimEventKind.UpgradeCompleted:
                var upgraded = world.FindEntity(simEvent.Entity);
                var owner = upgraded?.Owner ?? simEvent.Player;
                if (owner != human) return;
                // ProductionUpdate::update: an upgrade finished at its producer is a radar event there.
                if (upgraded is not null) CreateEvent(EventUpgrade, SimulationQueries.GroundOf(upgraded), simEvent.Tick);
                if (_orderedUpgrades.GetValueOrDefault(simEvent.Text) is not (> 0 and var ordered)) { Count("upgrade granted, not ordered"); return; }
                _orderedUpgrades[simEvent.Text] = ordered - 1;
                if (!_upgradeLabels.TryGetValue(simEvent.Text, out var upgradeLabel)) { Count("upgrade completed without DisplayName"); return; }
                Message("UPGRADE:UpgradeComplete", _text.Get(upgradeLabel));
                return;
            case SimEventKind.BeaconPlaced:
                // GameLogic::onPlaceBeacon: the player and its allies hear of it, in words and a sound; speech only an ally.
                if (simEvent.Player != human && world.RelationshipOf(human, simEvent.Player) != Relationship.Allies) return;
                Message("GUI:BeaconPlaced", PlayerName(simEvent.Player) ?? world.Configuration.Players.FirstOrDefault(player => player.Id == simEvent.Player)?.Name ?? $"Player {simEvent.Player.Value}");
                // "beacons are a rare event; play a nifty radar event thingy"
                if (world.FindEntity(simEvent.Entity) is { } beacon) CreateEvent(EventInformation, SimulationQueries.GroundOf(beacon), simEvent.Tick);
                BeaconPlaced?.Invoke(simEvent.Player != human);
                return;
            case SimEventKind.EntityDied:
                // Object::onDie: a unit of the local player that was not killed by itself "creates a fake radar event so the user can use the spacebar to quickly jump to this".
                if (world.FindEntity(simEvent.Entity) is { } lost && lost.Owner == human && simEvent.Other != simEvent.Entity && (lost.Template.IsKindOf("INFANTRY") || lost.Template.IsKindOf("VEHICLE")))
                    TryEvent(EventFake, SimulationQueries.GroundOf(lost), world, simEvent.Tick);
                return;
            case SimEventKind.ObjectConverted:
                // ConvertToHijackedVehicleCrateCollide::executeCrateBehavior and Object::defect (a car bomb): the vehicle's old owner is told; a defection needs two real players.
                if (new PlayerId((int)simEvent.Value) != human || world.FindEntity(simEvent.Entity) is not { } converted) return;
                if (simEvent.Text == "CARBOMB" && !(world.IsPlayableSide(human) && world.IsPlayableSide(converted.Owner))) return;
                CreateEvent(EventInfiltration, SimulationQueries.GroundOf(converted), simEvent.Tick);
                Message("RADAR:Infiltration");
                return;
            case SimEventKind.StructureCaptured:
                // Object::defect at the end of a capture: the old owner's building, between two real players.
                if (new PlayerId((int)simEvent.Value) != human || !world.IsPlayableSide(human) || !world.IsPlayableSide(simEvent.Player) || world.FindEntity(simEvent.Entity) is not { } captured) return;
                CreateEvent(EventInfiltration, SimulationQueries.GroundOf(captured), simEvent.Tick);
                Message("RADAR:Infiltration");
                return;
            case SimEventKind.Sabotaged:
                // The Sabotage*CrateCollide modules: tryInfiltrationEvent( other ), the sabotaged building.
                Infiltration(world.FindEntity(simEvent.Entity), human, simEvent.Tick);
                return;
            case SimEventKind.RadarNotice:
                Notice(simEvent, world, human);
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
        if (!TryEvent(EventUnderAttack, position, world, simEvent.Tick)) return;
        var template = victim.Template;
        if (template.IsKindOf("INFANTRY") || template.IsKindOf("VEHICLE")) Message(template.IsKindOf("HARVESTER") ? "RADAR:HarvesterUnderAttack" : "RADAR:UnitUnderAttack");
        else if (template.IsKindOf("STRUCTURE") && template.IsKindOf("MP_COUNT_FOR_VICTORY")) Message("RADAR:StructureUnderAttack");
        else Message("RADAR:UnderAttack");
    }

    /// <summary>A <see cref="SimEventKind.RadarNotice"/>: what the simulation tells every client's radar, the local player's client choosing what is its own.</summary>
    private void Notice(SimEvent simEvent, SimulationWorld world, PlayerId human)
    {
        if (world.FindEntity(simEvent.Entity) is not { } subject) return;
        var position = SimulationQueries.GroundOf(subject);
        switch (simEvent.Text)
        {
            case RadarNotices.StealthRevealed:
                if (subject.Owner == human)
                {
                    // StealthDetectorUpdate::update, "for the unit being revealed": mines and traps are rationed, everything else makes its event.
                    var template = subject.Template;
                    var made = true;
                    if (template.IsKindOf("MINE") || template.IsKindOf("BOOBY_TRAP") || template.IsKindOf("DEMOTRAP")) made = TryEvent(EventStealthNeutralized, position, world, simEvent.Tick);
                    else CreateEvent(EventStealthNeutralized, position, simEvent.Tick);
                    if (made) Message("MESSAGE:StealthNeutralized");
                }
                // "for the player revealing the stealth unit": the detector is the local player's, the revealed one not an ally's (the detector never reveals an ally).
                else if (world.FindEntity(simEvent.Other) is { } detector && detector.Owner == human && TryEvent(EventStealthDiscovered, position, world, simEvent.Tick))
                    Message("MESSAGE:StealthDiscovered");
                return;
            case RadarNotices.Infiltration:
                Infiltration(subject, human, simEvent.Tick);
                return;
            case RadarNotices.BattlePlan:
                CreateEvent(EventBattlePlan, position, simEvent.Tick);
                return;
            case RadarNotices.OverchargeExhausted:
                if (subject.Owner != human) return;
                Message("GUI:OverchargeExhausted");
                CreateEvent(EventInformation, position, simEvent.Tick);
                return;
            default:
                Count($"radar notice {simEvent.Text}");
                return;
        }
    }

    private void Count(string what) => _withoutLine[what] = _withoutLine.GetValueOrDefault(what) + 1;
}

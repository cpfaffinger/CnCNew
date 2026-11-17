using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Data.World;
using CnCNew.Presentation;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CommandButtonInfo = CnCNew.Presentation.Input.CommandButtonInfo;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Diagnostics;

public enum ProbeOutcome
{
    Passed,
    Failed,
    /// <summary>The button is not one a player can press on this object as it stands (a script-only button, a rider's place, a tooltip without a command).</summary>
    NotApplicable,
}

/// <summary>One pressed button: what the bar showed, what the press gave and what the world did in the game time after it (<see cref="Seconds"/>).</summary>
public sealed record ProbeResult(string Faction, string Object, string CommandSet, int Slot, string Button, GuiCommandKind Kind, ProbeOutcome Outcome, string Detail, double Seconds)
{
    /// <summary>What a list of known failures names: the faction, the object (or the science or shortcut set) and the button.</summary>
    public string Key => $"{Faction}|{Object}|{Button}";
}

/// <summary>
/// The dynamic half of the faction audit (#411): every button of a <see cref="FactionRoster"/> pressed on its object the way the command bar presses it - <see cref="CommandBarModel.Evaluate"/>
/// for its state, <see cref="CommandModeController.Activate"/> and, for a button that waits for a target, <see cref="CommandModeController.CompleteTarget(SimulationWorld, Entity?, Vector3?)"/>
/// or the placement - in a world of its own on flat ground: the object complete, the player rich and at the top rank, every science of the general's window, every structure the button's
/// object needs and power for it given, the upgrades the button needs, riders in a container whose button lets them out; beside it the enemy's sleeping objects (a vehicle, a soldier, a
/// building, the command center, a cash generator) and a civilian player's car and house. Then the world runs, at most a budget of game time per kind, until what the button is for shows: a
/// unit made, a structure placed, an upgrade completed and its modules switched on, a science owned, the riders out, the weapon of the button's slot fired, and for a power or an ability a
/// difference to a control world built the same way in which the button was not pressed (an object made, given away or disabled, the target harmed, money taken, a hazard cleaned, the map
/// seen) - so what the source would have done anyway proves nothing. A button that waits for an object is tried at each target a click could give it. Failures name what happened.
/// </summary>
public sealed class FactionProbe
{
    private static readonly PlayerId Me = new(1);
    private static readonly PlayerId Enemy = new(2);
    private static readonly PlayerId Civilian = new(3);
    private const int MapCells = 256;
    private const int Centre = 1280;

    private readonly TemplateCatalog _catalog;
    private readonly CommandButtonCatalog _buttons;
    private readonly CompiledDefinitionGraph _graph;
    private readonly string _fingerprint;
    private readonly TerrainMap _terrain;
    private readonly Dictionary<string, FactionRoster> _rosters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The target index at which a button of an object passed (<see cref="ProbeResult.Key"/>): a later press that needs it first gives it the same.</summary>
    private readonly Dictionary<string, int> _winners = new(StringComparer.OrdinalIgnoreCase);

    public FactionProbe(CompiledDefinitionGraph graph, TemplateCatalog catalog, CommandButtonCatalog buttons, string fingerprint)
    {
        _graph = graph;
        _catalog = catalog;
        _buttons = buttons;
        _fingerprint = fingerprint;
        _terrain = FlatTerrain();
    }

    /// <summary>Game time stepped by every world of the probe so far (test and control), in seconds.</summary>
    public double SecondsStepped { get; private set; }

    /// <summary>Worlds built so far.</summary>
    public int Worlds { get; private set; }

    public FactionRoster RosterOf(string faction)
    {
        if (!_rosters.TryGetValue(faction, out var roster)) _rosters[faction] = roster = FactionRoster.Build(_graph, _catalog, _buttons, faction);
        return roster;
    }

    /// <summary>Every button of the roster: the objects' (those <paramref name="objectFilter"/> keeps), the general's window and the power shortcuts.</summary>
    public IEnumerable<ProbeResult> Probe(FactionRoster roster, Func<string, bool>? objectFilter = null)
    {
        foreach (var entry in roster.Objects)
        {
            if (objectFilter is not null && !objectFilter(entry.Name)) continue;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var button in entry.Buttons)
            {
                // The same button in two sets of one object (its own and an upgrade's) is the same press.
                if (!seen.Add(button.Button.Name)) continue;
                yield return ProbeObjectButton(roster, entry, button);
            }
        }
        if (objectFilter is not null && !objectFilter(roster.Faction)) yield break;
        foreach (var button in roster.ScienceButtons) yield return ProbeScience(roster, button);
        foreach (var button in roster.ShortcutButtons) yield return ProbeShortcut(roster, button);
    }

    // ------------------------------------------------------------------------------------------------ the three kinds of press

    private ProbeResult ProbeObjectButton(FactionRoster roster, RosterObject entry, RosterButton button)
    {
        ImmutableArray<string> riders = [..(Template(entry.Name)?.Modules.Select(module => module.Data).OfType<RiderChangeContainData>().SelectMany(data => data.Riders) ?? [])
            .Where(value => value.CommandSet.Equals(button.CommandSet, StringComparison.OrdinalIgnoreCase)).Select(value => value.TemplateName)];
        var setup = new Setup(roster, entry.Name, button.Button) { Riders = riders, CommandSet = button.CommandSet };
        return Run(roster, entry.Name, button, setup);
    }

    private ProbeResult ProbeScience(FactionRoster roster, RosterButton button)
    {
        // The window shows a science of the set; the press buys the first the player does not own. The prerequisites are given, the science itself not.
        var setup = new Setup(roster, null, button.Button) { GrantAllSciences = false };
        foreach (var science in button.Button.Sciences)
            if (_catalog.Sciences.TryGet(science, out var info))
                foreach (var prerequisite in PrerequisitesOf(info)) setup.Sciences.Add(prerequisite);
        return Run(roster, button.CommandSet, button, setup);
    }

    private ProbeResult ProbeShortcut(FactionRoster roster, RosterButton button)
    {
        var power = roster.Powers.FirstOrDefault(value => value.Name.Equals(button.Button.SpecialPower, StringComparison.OrdinalIgnoreCase));
        var carrier = power?.Carriers.FirstOrDefault();
        // ControlBar::populateSpecialPowerShortcut shows only the powers an object of the player carries.
        if (carrier is null)
            return Result(roster, button.CommandSet, button, ProbeOutcome.NotApplicable, $"no object of the faction carries the power '{button.Button.SpecialPower}': the shortcut bar never shows it", 0);
        var setup = new Setup(roster, carrier, button.Button) { SourceIsCarrier = true };
        return Run(roster, button.CommandSet, button, setup);
    }

    // ------------------------------------------------------------------------------------------------ setup

    private sealed class Setup(FactionRoster roster, string? source, CommandButtonInfo button)
    {
        public FactionRoster Roster { get; } = roster;
        public string? Source { get; } = source;
        public CommandButtonInfo Button { get; } = button;
        public bool GrantAllSciences { get; init; } = true;
        public bool SourceIsCarrier { get; init; }

        /// <summary>The riders whose <c>RiderChangeContain</c> command set holds the button (a combat bike's Jarmen Kell): one of them sits in it at the press.</summary>
        public ImmutableArray<string> Riders { get; init; } = [];

        /// <summary>The command set the button was found in.</summary>
        public string CommandSet { get; init; } = string.Empty;
        public SortedSet<string> Sciences { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>One world of a probe: who is where, and the input layer that presses for the player.</summary>
    private sealed class Arena
    {
        public required SimulationWorld World { get; init; }
        public required CommandModeController Controller { get; init; }
        public required CommandBarModel Bar { get; init; }
        public required Sink Sink { get; init; }
        public EntityId Source { get; set; } = EntityId.Invalid;
        public List<EntityId> EnemyObjects { get; } = [];
        public List<EntityId> NeutralObjects { get; } = [];
        public List<EntityId> Allies { get; } = [];
        public Vector3 EmptyGround { get; set; }
        public Vector3 EnemyGround { get; set; }
        public List<SimEvent> Events { get; } = [];
        public long PressTick { get; set; }

        /// <summary>The objects that stood at the press: their ids are the same in the test and the control world.</summary>
        public HashSet<uint> PressIds { get; set; } = [];

        /// <summary>The object the press aimed at, whose health counts as a difference.</summary>
        public EntityId Target { get; set; } = EntityId.Invalid;
    }

    private sealed class Sink(SimulationWorld world) : ICommandSink
    {
        private ulong _sequence;
        public ulong Next() => ++_sequence;
        public CommandReceipt Issue(Func<ulong, long, SimCommand> make)
        {
            var command = make(++_sequence, world.Tick);
            return new CommandReceipt((long)_sequence, world.Submit(command));
        }
    }

    private Arena Build(Setup setup, out string? problem)
    {
        problem = null;
        Worlds++;
        var roster = setup.Roster;
        var enemyFaction = EnemyOf(roster.BaseFaction);
        var players = ImmutableArray.Create(
            new PlayerConfiguration(Me, roster.Faction, roster.Faction),
            new PlayerConfiguration(Enemy, enemyFaction, enemyFaction, 0, true),
            new PlayerConfiguration(Civilian, "FactionCivilian", "FactionCivilian", 0, true));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), _catalog.Rate, 411u, players, "faction-audit", _fingerprint, _terrain.Fingerprint), _catalog, _terrain);
        // Quick build and recharge, and the general's rank at its top (the sciences of each rank that the purchases need, and their points).
        world.ConfigureMatchOptions(buildTimePercent: 1, neutralRespawnSeconds: 0, startingSkillPoints: 1_000_000, rechargePercent: 1);
        world.SetMoney(Me, Fixed.FromInt(1_000_000));
        world.AddSciencePoints(Me, 100);
        world.SetMoney(Enemy, Fixed.FromInt(10_000));
        foreach (var other in new[] { Me, Enemy })
        {
            world.SetRelationship(other, Civilian, Relationship.Neutral);
            world.SetRelationship(Civilian, other, Relationship.Neutral);
        }
        var sink = new Sink(world);
        var selection = new SelectionModel(Me);
        var arena = new Arena { World = world, Sink = sink, Bar = new CommandBarModel(_buttons), Controller = new CommandModeController(Me, sink, selection) };

        // Sciences: every science of the general's window (the powers they unlock), or only what a science button needs; prerequisites first.
        var sciences = new List<ScienceInfo>();
        IEnumerable<string> wanted = setup.GrantAllSciences ? roster.Sciences.Select(value => value.Name).Concat(roster.GrantedSciences).Concat(setup.Sciences) : setup.Sciences;
        foreach (var name in wanted)
            if (_catalog.Sciences.TryGet(name, out var info))
                foreach (var needed in PrerequisitesOf(info).Append(info.Name))
                    if (_catalog.Sciences.TryGet(needed, out var known) && !sciences.Contains(known)) sciences.Add(known);
        foreach (var science in sciences) world.GrantScience(Me, science.Name);

        var spawns = new List<(string Template, PlayerId Owner, decimal X, decimal Y, string Role)>();
        var source = setup.Source is null ? null : Template(setup.Source);
        var radius = source is null ? 20m : Math.Max(20m, (decimal)source.BoundingCircleRadius.ToDecimal());
        if (source is not null) spawns.Add((source.Name, Me, Centre, Centre, "source"));

        // What the button's object needs (its prerequisites), and power for everything.
        var needs = new List<string>();
        var built = setup.Button.Object.Length > 0 ? Template(setup.Button.Object) : null;
        if (built is not null) needs.AddRange(PrerequisiteObjects(built, roster, setup.Sciences));
        var row = 0;
        foreach (var need in needs.Distinct(StringComparer.OrdinalIgnoreCase))
            if (source is null || !need.Equals(source.Name, StringComparison.OrdinalIgnoreCase))
                spawns.Add((need, Me, Centre - 900 + 200 * (row++ % 10), Centre - 900 - 200 * (row / 10), "need"));
        var plant = roster.Objects.Select(value => Template(value.Name)).Where(value => value is { EnergyProduction: > 0 } && value.IsKindOf("STRUCTURE"))
            .OrderByDescending(value => value!.EnergyProduction).ThenBy(value => value!.Name, StringComparer.Ordinal).FirstOrDefault();
        if (plant is not null)
        {
            var demand = spawns.Where(value => value.Owner == Me).Sum(value => Math.Max(0, -(Template(value.Template)?.EnergyProduction ?? 0))) + 20;
            for (var made = 0; made * plant.EnergyProduction < demand; made++)
                spawns.Add((plant.Name, Me, Centre + 900 - 200 * (made % 8), Centre - 900 - 200 * (made / 8), "plant"));
        }

        // Harmless enemies east of the source, and an own unit north-west of it for buttons that take an ally.
        // A weapon aimed by a button at an object stands farther off: the source's own fire must not kill the target before the press (Colonel Burton's gun and his knife).
        var distance = radius + (setup.Button.Kind == GuiCommandKind.FireWeapon && (setup.Button.Options & CommandOptions.NeedObjectTarget) != 0 ? 170m : 70m);
        foreach (var (target, dx, dy) in EnemyTargets(enemyFaction))
            spawns.Add((target, Enemy, Centre + distance + dx, Centre + dy, "enemy"));
        if (source is not null && setup.Button.SpecialPower.Length > 0 && source.Modules.Any(module => module.Data is CleanupAreaPowerData)
            && _catalog.Templates.Where(value => value.IsKindOf("CLEANUP_HAZARD") && value.IsInstantiable).OrderBy(value => value.Name, StringComparer.Ordinal).FirstOrDefault() is { } hazard)
            spawns.Add((hazard.Name, Enemy, Centre + distance, Centre + 120m, "hazard"));
        // Neutral objects (the system player's, as a map's civilians): a car and a building that soldiers can enter, west of the source.
        foreach (var (neutral, dy) in NeutralTargets())
            spawns.Add((neutral.Name, Civilian, Centre - distance - (decimal)neutral.BoundingCircleRadius.ToDecimal(), Centre + dy, "neutral"));
        var ally = roster.Objects.Select(value => Template(value.Name)).OfType<ObjectTemplate>()
            .Where(value => value.IsKindOf("VEHICLE") && value.Locomotor is not null && !value.IsKindOf("AIRCRAFT")).OrderBy(value => Armed(value) ? 1 : 0).FirstOrDefault();
        if (ally is not null) spawns.Add((ally.Name, Me, Centre - distance, Centre + distance, "ally"));

        // A player upgrade is shown working on objects that read it: up to two of the faction's objects with a module it switches on.
        if (setup.Button.Kind == GuiCommandKind.PlayerUpgrade && setup.Button.Upgrade.Length > 0)
        {
            var readers = roster.Objects.Where(value => !value.Name.Equals(source?.Name, StringComparison.OrdinalIgnoreCase)
                                                        && !FactionRoster.TriggeredModules(_graph, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { value.Name }, setup.Button.Upgrade).IsEmpty)
                .Select(value => value.Name).Take(2).ToList();
            for (var index = 0; index < readers.Count; index++) spawns.Add((readers[index], Me, Centre - 300m - 200m * index, Centre - 300m, "reader"));
        }

        var before = world.Entities.Select(entity => entity.Id.Value).ToHashSet();
        ulong system = 0;
        foreach (var spawn in spawns)
        {
            var result = world.Submit(new SpawnCommand(PlayerId.System, ++system, world.Tick, spawn.Template, spawn.Owner, Fixed.FromDecimal(spawn.X), Fixed.FromDecimal(spawn.Y), Fixed.Zero, Fixed.Zero));
            if (!result.Accepted) problem = $"spawning {spawn.Template} ({spawn.Role}) was refused: {result.Reason}";
        }
        Step(arena);
        var created = world.Entities.Where(entity => !before.Contains(entity.Id.Value)).OrderBy(entity => entity.Id.Value).ToList();
        // Spawns that make more objects (a structure's free unit) come later in id order: match the spawns by template in order.
        foreach (var spawn in spawns)
        {
            var match = created.FirstOrDefault(entity => entity.Template.Name.Equals(spawn.Template, StringComparison.OrdinalIgnoreCase) && entity.Owner == spawn.Owner);
            if (match is null) { problem ??= $"{spawn.Template} ({spawn.Role}) did not appear"; continue; }
            created.Remove(match);
            switch (spawn.Role)
            {
                case "source": arena.Source = match.Id; break;
                case "enemy": arena.EnemyObjects.Add(match.Id); break;
                case "neutral": arena.NeutralObjects.Add(match.Id); break;
                case "ally": arena.Allies.Add(match.Id); break;
            }
        }
        // The enemy's and the civilians' objects sleep (computer players' units, Attitude.Sleep): they look for no victim, so the source is not shot at and nothing of theirs
        // fights a course of its own that the press could change.
        foreach (var entity in world.Entities.Where(entity => entity.Owner == Enemy || entity.Owner == Civilian).OrderBy(entity => entity.Id.Value))
            world.SetAttitude(entity, (int)Attitude.Sleep);
        arena.EmptyGround = new Vector3(Centre, (float)(Centre + distance + 60m), 0);
        arena.EnemyGround = new Vector3((float)(Centre + distance), Centre, 0);

        // Upgrades the press needs: the button's own when it needs one, and every upgrade that unpauses a power of the source.
        if (world.FindEntity(arena.Source) is { } placed)
        {
            foreach (var upgrade in UpgradesToGrant(placed.Template, setup.Button, setup.CommandSet))
                if (_catalog.Upgrades.TryGet(upgrade, out var info))
                    world.CompleteUpgrade(Me, info.Scope == UpgradeScope.Object ? placed.Id : null, info.Name);
        }

        // Riders get in before the press of a button that lets them out or that a rider brings (a set of RiderChangeContain): the rider of that set, else the faction's
        // soldiers that the container takes. A container that came with riders (a troop crawler's) keeps them.
        // A container's initial payload (a combat bike's rebel, a troop crawler's squad) arrives in the ticks after it is made.
        if (NeedsRiders(setup.Button) || !setup.Riders.IsEmpty)
            for (var settle = world.Tick + world.Time.TicksPerSecond; world.Tick < settle;) Step(arena);
        if (world.FindEntity(arena.Source) is { Container: { } held } container)
        {
            var riderKinds = !setup.Riders.IsEmpty
                ? (held.ContainCount > 0 && held.Contained.All(id => world.FindEntity(id) is { } rider && setup.Riders.Contains(rider.Template.Name, StringComparer.OrdinalIgnoreCase))
                    ? []
                    : [setup.Riders[0]])
                : NeedsRiders(setup.Button) && held.ContainCount == 0
                    ? roster.Objects.Select(value => Template(value.Name)).OfType<ObjectTemplate>().Where(value => value.IsKindOf("INFANTRY") && value.Locomotor is not null)
                        .Where(value => setup.Button.Kind != GuiCommandKind.CombatDrop || value.IsKindOf("CAN_RAPPEL")).Select(value => value.Name).Take(8).ToList()
                    : new List<string>();
            if (riderKinds.Count > 0)
            {
                var known = world.Entities.Select(entity => entity.Id.Value).ToHashSet();
                for (var index = 0; index < riderKinds.Count; index++)
                    world.Submit(new SpawnCommand(PlayerId.System, ++system, world.Tick, riderKinds[index], Me, Fixed.FromDecimal(Centre - radius - 15m), Fixed.FromDecimal(Centre - 90m + 25m * index), Fixed.Zero, Fixed.Zero));
                // The rider of a rider's set takes the place of whoever sits there by getting in (RiderChangeContain::onContaining removes the rider before; a rider that gets out
                // without one scuttles the combat bike). Staging, not a player's order: the riders are put in directly.
                var sitting = held.Contained.ToList();
                Step(arena);
                var seated = 0;
                var candidates = world.Entities.Where(entity => !known.Contains(entity.Id.Value) && entity.Owner == Me && riderKinds.Contains(entity.Template.Name, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(entity => entity.Id.Value).ToList();
                foreach (var made in candidates)
                    if (world.TryEnterContainer(made, container)) seated++;
                if (seated == 0)
                    problem ??= $"no rider got in (the container takes none of {string.Join(" ", riderKinds)}{(sitting.Count > 0 ? $"; {sitting.Count} were inside, {held.ContainCount} are" : "")}; {candidates.Count} made)";
            }
        }

        // The powers are charged: a unit that has stood around for a while.
        foreach (var entity in world.Entities.Where(entity => entity.Owner == Me).OrderBy(entity => entity.Id.Value))
            foreach (var module in entity.Modules)
                if (module is ISpecialPowerModule power)
                {
                    power.SetReadyFrame(world.Tick);
                    if (_catalog.SpecialPowers[power.PowerIndex].SharedSynced) world.SetSharedReadyFrame(Me, power.PowerIndex, world.Tick);
                }
        Step(arena);
        arena.Events.Clear();
        return arena;
    }

    /// <summary>The science a power button needs that neither the faction's general window nor its own sciences give, or null.</summary>
    private string? Unobtainable(FactionRoster roster, CommandButtonInfo button)
    {
        if (!_catalog.SpecialPowers.TryGet(button.SpecialPower, out var power) || power.RequiredScience < 0) return null;
        var science = _catalog.Sciences.Sciences.FirstOrDefault(value => value.Index == power.RequiredScience);
        if (science is null || roster.Sciences.Any(value => value.Name.Equals(science.Name, StringComparison.OrdinalIgnoreCase))
            || roster.GrantedSciences.Contains(science.Name, StringComparer.OrdinalIgnoreCase)) return null;
        return _catalog.Factions.TryGet(roster.Faction, out var faction) && faction.IntrinsicSciences.Has(science.Index) ? null : science.Name;
    }

    private static bool NeedsRiders(CommandButtonInfo button) => button.Kind is GuiCommandKind.Evacuate or GuiCommandKind.CombatDrop;

    /// <summary>
    /// The upgrades the press needs: the button's own when it needs one (never the one it buys), every upgrade that unpauses a power of the source, and the upgrade that switches the
    /// source to the command set the button is in (an Overlord's bunker set).
    /// </summary>
    private IEnumerable<string> UpgradesToGrant(ObjectTemplate source, CommandButtonInfo button, string commandSet)
    {
        foreach (var module in source.Modules)
            if (module.Data is CommandSetUpgradeData switching && commandSet.Length > 0 && !(source.CommandSets.FirstOrDefault()?.Equals(commandSet, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                if (switching.CommandSet.Equals(commandSet, StringComparison.OrdinalIgnoreCase))
                    foreach (var name in switching.Mux.TriggerNames) yield return name;
                else if (switching.CommandSetAlt.Equals(commandSet, StringComparison.OrdinalIgnoreCase) && switching.TriggerAlt.Length > 0)
                {
                    foreach (var name in switching.Mux.TriggerNames) yield return name;
                    yield return switching.TriggerAlt;
                }
            }
        var own = button.Kind is GuiCommandKind.PlayerUpgrade or GuiCommandKind.ObjectUpgrade ? button.Upgrade : string.Empty;
        if (button.Has(CommandOptions.NeedUpgrade) && button.Upgrade.Length > 0 && own.Length == 0) yield return button.Upgrade;
        if (_graph.Definitions.OfType<ObjectDefinition>().FirstOrDefault(value => value.OriginalName.Equals(source.Name, StringComparison.OrdinalIgnoreCase)) is not { } definition) yield break;
        foreach (var module in definition.Modules.Where(module => module.Name.Equals("UnpauseSpecialPowerUpgrade", StringComparison.OrdinalIgnoreCase)))
        {
            var power = module.Fields.FirstOrDefault(field => field.Name.Equals("SpecialPowerTemplate", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim() ?? string.Empty;
            if (button.SpecialPower.Length > 0 && !power.Equals(button.SpecialPower, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var field in module.Fields.Where(field => field.Name.Equals("TriggeredBy", StringComparison.OrdinalIgnoreCase)))
                foreach (var name in field.Value.OriginalText.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                    if (!name.Equals(own, StringComparison.OrdinalIgnoreCase)) yield return name;
        }
    }

    /// <summary>The structures the template's prerequisites name (the first alternative the faction has, else the first), and the sciences they name into <paramref name="sciences"/>.</summary>
    private IEnumerable<string> PrerequisiteObjects(ObjectTemplate template, FactionRoster roster, SortedSet<string> sciences)
    {
        foreach (var line in template.Prerequisites)
        {
            if (line.IsScience)
            {
                foreach (var name in line.Alternatives.Take(1)) sciences.Add(name);
                continue;
            }
            var chosen = line.Alternatives.FirstOrDefault(name => roster.Find(name) is not null) ?? line.Alternatives.FirstOrDefault();
            if (chosen is not null) yield return chosen;
        }
    }

    private IEnumerable<string> PrerequisitesOf(ScienceInfo science)
    {
        foreach (var name in science.PrerequisiteNames)
            if (_catalog.Sciences.TryGet(name, out var info))
            {
                foreach (var deeper in PrerequisitesOf(info)) yield return deeper;
                yield return info.Name;
            }
    }

    private static string EnemyOf(string baseFaction) => baseFaction.Equals("FactionAmerica", StringComparison.OrdinalIgnoreCase) ? "FactionChina"
        : baseFaction.Equals("FactionChina", StringComparison.OrdinalIgnoreCase) ? "FactionGLA" : "FactionAmerica";

    /// <summary>Unarmed targets of the enemy faction: a vehicle, a soldier and a building (none of them fires back), by name order among the roster's.</summary>
    private IEnumerable<(string Template, decimal Dx, decimal Dy)> EnemyTargets(string faction)
    {
        var roster = RosterOf(faction);
        ObjectTemplate? Pick(Func<ObjectTemplate, bool> wanted) => roster.Objects.Select(value => Template(value.Name)).OfType<ObjectTemplate>()
            .Where(wanted).Where(value => !value.Modules.Any(module => module.Name == "VeterancyCrateCollide"))
            .OrderBy(value => value.Modules.Any(module => module.Data is StealthData { InnateStealth: true }) ? 1 : 0).ThenBy(value => Armed(value) ? 1 : 0)
            .ThenBy(value => value.Name, StringComparer.Ordinal).FirstOrDefault();
        var vehicle = Pick(value => value.IsKindOf("VEHICLE") && !value.IsKindOf("AIRCRAFT") && value.Locomotor is not null && !value.IsKindOf("HUGE_VEHICLE"));
        var soldier = Pick(value => value.IsKindOf("INFANTRY"));
        var building = Pick(value => value.IsKindOf("STRUCTURE") && !value.IsKindOf("COMMANDCENTER") && value.EnergyProduction >= 0 && value.Modules.Any(module => module.Data is ProductionData));
        if (vehicle is not null) yield return (vehicle.Name, 0m, -60m);
        if (soldier is not null) yield return (soldier.Name, 0m, 40m);
        if (building is not null) yield return (building.Name, 40m + (decimal)building.BoundingCircleRadius.ToDecimal(), 0m);
        // The command center: what a saboteur goes for; and what a cash hack steals from.
        var center = _catalog.Factions.TryGet(faction, out var info) ? Template(info.StartingBuilding) : null;
        if (center is not null)
            yield return (center.Name, 40m + (decimal)center.BoundingCircleRadius.ToDecimal(), 120m + (decimal)center.BoundingCircleRadius.ToDecimal());
        if (Pick(value => value.IsKindOf("CASH_GENERATOR") && value.IsKindOf("STRUCTURE") && value.Name != center?.Name) is { } cash)
            yield return (cash.Name, 40m + (decimal)cash.BoundingCircleRadius.ToDecimal(), -120m - (decimal)cash.BoundingCircleRadius.ToDecimal());
    }

    /// <summary>The <c>CARBOMB</c> weapon set condition: a car a terrorist can turn into a bomb has a set for it.</summary>
    private static readonly uint CarBombSet = NameTablesPhase5.WeaponSetCondition.TryIndexOf("CARBOMB", out var index) ? 1u << index : 0u;

    /// <summary>A civilian car and a civilian building with a garrison, the first by name of the catalog's that can be made.</summary>
    private IEnumerable<(ObjectTemplate Template, decimal Dy)> NeutralTargets()
    {
        var civilians = _catalog.Templates.Where(value => value.IsInstantiable && value.Side.Equals("Civilian", StringComparison.OrdinalIgnoreCase)).OrderBy(value => value.Name, StringComparer.Ordinal).ToList();
        if (civilians.Where(value => value.IsKindOf("VEHICLE") && value.Locomotor is not null && !value.IsKindOf("AIRCRAFT") && !value.IsKindOf("BOAT")
                                     && !value.Modules.Any(module => module.Data is RailedTransportAiData))
                .OrderBy(value => value.WeaponSets.Any(set => (set.Conditions & CarBombSet) != 0 && set.WeaponCount > 0) ? 0 : 1)
                .ThenBy(value => value.Name.StartsWith("Car", StringComparison.Ordinal) ? 0 : 1).FirstOrDefault() is { } car)
            yield return (car, -60m);
        if (civilians.FirstOrDefault(value => value.IsKindOf("STRUCTURE") && value.Modules.Any(module => module.Data is GarrisonContainData)) is { } building)
            yield return (building, 60m + (decimal)building.BoundingCircleRadius.ToDecimal());
    }

    private static bool Armed(ObjectTemplate template) => template.WeaponSets.Any(set => set.WeaponCount > 0);

    private ObjectTemplate? Template(string name) => _catalog.TryGet(name, out var template) ? template : null;

    // ------------------------------------------------------------------------------------------------ the press

    private ProbeResult Run(FactionRoster roster, string owner, RosterButton entry, Setup setup)
    {
        var button = entry.Button;
        if (Progress) Console.Error.WriteLine($"probe {roster.Faction}|{owner}|{button.Name}");
        if (button.Kind == GuiCommandKind.None) return Result(roster, owner, entry, ProbeOutcome.NotApplicable, "no command (a tooltip button)", 0);
        if (button.Has(CommandOptions.ScriptOnly)) return Result(roster, owner, entry, ProbeOutcome.NotApplicable, "SCRIPT_ONLY", 0);
        if (button.Kind == GuiCommandKind.ExitContainer) return Result(roster, owner, entry, ProbeOutcome.NotApplicable, "a rider's place, not a button of the set", 0);

        // A button that waits for an object is tried at each target a click could give it, each in worlds of their own, until one shows what it is for.
        ProbeResult? first = null;
        var tried = new List<string>();
        for (var candidate = 0; ; candidate++)
        {
            var result = RunOnce(roster, owner, entry, setup, candidate, out var candidates);
            if (result.Outcome == ProbeOutcome.Passed) _winners[result.Key] = candidate;
            if (result.Outcome != ProbeOutcome.Failed) return result;
            first ??= result;
            if (candidate > 0) tried.Add($"[{result.Detail}]");
            if (candidate + 1 >= candidates) return tried.Count == 0 ? first : first with { Detail = $"{first.Detail}; tried also {string.Join(", ", tried)}" };
        }
    }

    private ProbeResult RunOnce(FactionRoster roster, string owner, RosterButton entry, Setup setup, int candidate, out int candidates)
    {
        candidates = 1;
        var button = entry.Button;
        var test = Build(setup, out var problem);
        if (problem is not null) return Result(roster, owner, entry, ProbeOutcome.Failed, $"setup: {problem}", 0);
        var world = test.World;
        var source = world.FindEntity(test.Source);
        var evidence = EvidenceKindOf(button);
        var control = evidence == Evidence.Difference || evidence == Evidence.WeaponOrDifference ? Build(setup, out _) : null;
        var sources = setup.SourceIsCarrier || source is null ? ImmutableArray<Entity>.Empty : [source];

        // What has to happen before the press: charges placed before they are detonated, a battle plan chosen before its cannon is stopped. In both worlds alike.
        if (source is not null && Prelude(roster, setup, button) is { } prelude)
        {
            foreach (var arena in new[] { test, control }.OfType<Arena>())
            {
                var placed = arena.World.FindEntity(arena.Source);
                var mine = placed is null ? ImmutableArray<Entity>.Empty : [placed];
                arena.Controller.Selection.Replace(mine.Select(value => value.Id));
                int? target = _winners.TryGetValue($"{roster.Faction}|{setup.Source}|{prelude.Button.Name}", out var known) ? known : null;
                var refused = Press(arena, prelude.Button, mine, target, out _);
                if (refused is not null) return Result(roster, owner, entry, ProbeOutcome.Failed, $"the step before ({prelude.Button.Name}): {refused}", 0);
                var until = arena.World.Tick + arena.World.Time.WholeSecondsToTicks(prelude.Seconds);
                while (arena.World.Tick < until)
                {
                    arena.Controller.Update(arena.World);
                    Step(arena);
                }
                arena.Controller.DrainFeedback();
                arena.Events.Clear();
            }
            source = world.FindEntity(test.Source);
            sources = source is null ? ImmutableArray<Entity>.Empty : [source];
        }

        // What the bar shows for the button (the object's own bar, or the shortcut bar's most ready carrier).
        if (source is not null && setup.Source is not null)
        {
            var (availability, reason, _, _, _) = test.Bar.Evaluate(world, Me, source, button);
            if (availability == ButtonAvailability.Hidden && reason is ButtonReason.NotBuildable)
                return Result(roster, owner, entry, ProbeOutcome.NotApplicable, $"bar: hidden ({reason})", 0);
            if (availability == ButtonAvailability.Hidden && reason is ButtonReason.MissingScience && Unobtainable(roster, button) is { } science)
                return Result(roster, owner, entry, ProbeOutcome.NotApplicable, $"bar: hidden, the power needs {science}, which the general's window of the faction does not offer", 0);
            if (availability is not (ButtonAvailability.Available or ButtonAvailability.Active))
            {
                var inside = source.Container is { ContainCount: > 0 } held
                    ? $"; inside: {string.Join(" ", held.Contained.Select(id => world.FindEntity(id)?.Template.Name ?? "?"))}"
                    : source.Container is not null ? "; nobody inside" : string.Empty;
                return Result(roster, owner, entry, ProbeOutcome.Failed, $"bar: {availability} ({reason}){inside}", 0);
            }
        }

        candidates = Math.Max(1, Candidates(test, button).Count());
        test.Controller.Selection.Replace(sources.Select(value => value.Id));
        var press = Press(test, button, sources, candidate, out var pressDetail);
        if (press is not null) return Result(roster, owner, entry, ProbeOutcome.Failed, press, 0);

        var budget = world.Time.WholeSecondsToTicks(BudgetSeconds(button));
        var start = world.Tick;
        test.PressTick = start;
        test.PressIds = [..world.Entities.Select(entity => entity.Id.Value)];
        var baseline = Count(world, button.Object);
        string? found = null;
        while (world.Tick - start < budget)
        {
            test.Controller.Update(world);
            Step(test);
            if (control is not null) Step(control);
            if (!world.Time.IsOriginalFrameBoundary(world.Tick)) continue;
            found = Check(test, control, button, evidence, baseline);
            if (Trace is { } trace && button.Name.Equals(trace, StringComparison.OrdinalIgnoreCase) && world.Tick % world.Time.TicksPerSecond == 0)
                TraceState(test, candidate);
            if (found is not null) break;
        }
        var seconds = (double)(world.Tick - start) / world.Time.TicksPerSecond;
        var rejections = test.Events.Where(item => item.Kind == SimEventKind.CommandRejected && item.Player == Me)
            .Select(item => $"{item.Text}:{(CommandRejection)item.Value}").Distinct(StringComparer.Ordinal).ToList();
        var skipped = test.Events.Where(item => item.Kind == SimEventKind.CreationSkipped && item.Value == CreationSkipReasons.NuggetNotSimulated)
            .Select(item => $"not simulated: {item.Text}").Distinct(StringComparer.Ordinal).ToList();
        var notes = string.Join("; ", new[] { pressDetail }.Concat(rejections.Select(value => "rejected " + value)).Concat(skipped).Where(value => value.Length > 0));
        if (found is not null && skipped.Count == 0)
            return Result(roster, owner, entry, ProbeOutcome.Passed, notes.Length == 0 ? found : $"{found}; {notes}", seconds);
        var happened = test.Events.Where(item => item.Kind is SimEventKind.SpecialPowerUsed or SimEventKind.SpecialPowerCompleted or SimEventKind.EntitySpawned or SimEventKind.EntityDamaged
                or SimEventKind.WeaponFired or SimEventKind.ObjectConverted or SimEventKind.Sabotaged or SimEventKind.StructureCaptured or SimEventKind.UpgradeCompleted or SimEventKind.EntityDied)
            .GroupBy(item => item.Kind).OrderBy(group => group.Key).Select(group => $"{group.Key} {group.Count()}").ToList();
        var what = found is null
            ? $"nothing to show for it in {BudgetSeconds(button)} s ({ExpectationOf(button, evidence)}; events: {(happened.Count == 0 ? "none" : string.Join(", ", happened))})"
            : found;
        return Result(roster, owner, entry, ProbeOutcome.Failed, notes.Length == 0 ? what : $"{what}; {notes}", seconds);
    }

    private sealed record PreludeStep(CommandButtonInfo Button, int Seconds);

    /// <summary>
    /// A press that only means something after another of the same object: a power's button without a target whose sibling with a target uses the same power (the remote charges are
    /// detonated after they are placed), and the Strategy Center's own Stop (<c>OPTION_ONE</c>), which stops the bombardment of its battle plan.
    /// </summary>
    private static PreludeStep? Prelude(FactionRoster roster, Setup setup, CommandButtonInfo button)
    {
        var entry = setup.Source is null ? null : roster.Find(setup.Source);
        if (entry is null || setup.SourceIsCarrier) return null;
        if (button.Kind == GuiCommandKind.SpecialPower && !button.NeedsTarget && button.SpecialPower.Length > 0
            && entry.Buttons.Select(value => value.Button).FirstOrDefault(other => other.Name != button.Name && other.NeedsTarget
                && other.SpecialPower.Equals(button.SpecialPower, StringComparison.OrdinalIgnoreCase)) is { } placing)
            return new PreludeStep(placing, 30);
        if (button.Kind == GuiCommandKind.Stop && button.Has(CommandOptions.OptionOne)
            && entry.Buttons.Select(value => value.Button).FirstOrDefault(other => other.Kind == GuiCommandKind.SpecialPower && other.Has(CommandOptions.OptionOne)) is { } plan)
            return new PreludeStep(plan, 30);
        return null;
    }

    /// <summary>Presses the button as the bar does; null when the order went out, else why not.</summary>
    private string? Press(Arena arena, CommandButtonInfo button, ImmutableArray<Entity> sources, int? only, out string detail)
    {
        detail = string.Empty;
        var world = arena.World;
        var controller = arena.Controller;
        var result = controller.Activate(world, button, sources);
        var feedback = controller.DrainFeedback();
        if (feedback.FirstOrDefault(item => item.Kind is FeedbackKind.Unsupported) is { } unsupported) return $"unsupported: {unsupported.Detail}";
        if (result is ActivationResult.Unsupported) return "unsupported";
        if (result is ActivationResult.NoSource) return "no source for the button";
        if (result is ActivationResult.Rejected)
            return $"rejected: {string.Join(", ", feedback.Where(item => item.Kind == FeedbackKind.Rejected).Select(item => $"{item.Detail}:{item.Rejection}"))}";
        if (result is ActivationResult.Issued) return null;

        // A mode: the target click, the placement.
        switch (controller.Mode)
        {
            case CommandModeKind.Placement:
            {
                var builder = controller.PendingSources.IsEmpty ? null : world.FindEntity(controller.PendingSources[0]);
                var site = FindSite(world, button, builder);
                if (site is null) return "no legal place for the structure near the builder";
                var view = new GroundView();
                var screen = new Vector2(site.Value.X, site.Value.Y);
                controller.PlacementPress(world, view, screen);
                var placed = controller.PlacementRelease(world, view, screen);
                var answer = controller.DrainFeedback();
                if (!placed) return $"placement refused: {string.Join(", ", answer.Select(item => $"{item.Kind}:{item.Rejection}:{item.Detail}"))}";
                detail = $"placed at {site.Value.X - Centre:0},{site.Value.Y - Centre:0}";
                return null;
            }
            case CommandModeKind.AttackMove or CommandModeKind.Guard:
                return controller.CompleteTarget(world, null, arena.EmptyGround) ? null : $"target refused: {Describe(controller.DrainFeedback())}";
            case CommandModeKind.ButtonTarget:
            {
                var tried = new List<string>();
                var all = Candidates(arena, button).ToList();
                foreach (var (target, ground, label) in only is { } index ? all.Skip(index).Take(1) : all)
                {
                    if (controller.Mode != CommandModeKind.ButtonTarget)
                    {
                        controller.Activate(world, button, sources);
                        controller.DrainFeedback();
                    }
                    if (controller.CompleteTarget(world, target, ground))
                    {
                        arena.Target = target?.Id ?? EntityId.Invalid;
                        controller.DrainFeedback();
                        detail = $"at {label}";
                        return null;
                    }
                    tried.Add($"{label}: {Describe(controller.DrainFeedback())}");
                }
                controller.Cancel();
                return $"no target accepted ({string.Join("; ", tried)})";
            }
            default:
                return $"activation gave {result} in mode {controller.Mode}";
        }
    }

    private static string Describe(ImmutableArray<CommandFeedback> feedback)
        => feedback.IsEmpty ? "no answer" : string.Join(", ", feedback.Select(item => item.Kind == FeedbackKind.Rejected ? $"rejected {item.Detail}:{item.Rejection}" : $"{item.Kind}{(item.Detail.Length > 0 ? ":" + item.Detail : "")}"));

    /// <summary>The targets a click could give the button, in the order tried: the enemy's objects, an own unit, the enemy's place, an empty place.</summary>
    private static IEnumerable<(Entity? Target, Vector3? Ground, string Label)> Candidates(Arena arena, CommandButtonInfo button)
    {
        var world = arena.World;
        var entering = button.Kind is GuiCommandKind.HijackVehicle or GuiCommandKind.ConvertToCarbomb or GuiCommandKind.SabotageBuilding;
        if (button.Has(CommandOptions.NeedTargetEnemyObject) || entering)
            foreach (var id in arena.EnemyObjects)
                if (world.FindEntity(id) is { } enemy) yield return (enemy, null, $"enemy {enemy.Template.Name}");
        if (button.Has(CommandOptions.NeedTargetNeutralObject) || entering)
            foreach (var id in arena.NeutralObjects)
                if (world.FindEntity(id) is { } neutral) yield return (neutral, null, $"neutral {neutral.Template.Name}");
        if (button.Has(CommandOptions.NeedTargetAllyObject))
            foreach (var id in arena.Allies)
                if (world.FindEntity(id) is { } ally) yield return (ally, null, $"own {ally.Template.Name}");
        if (button.Has(CommandOptions.NeedTargetPos))
        {
            var offensive = button.Kind is GuiCommandKind.SpecialPower or GuiCommandKind.SpecialPowerFromShortcut or GuiCommandKind.FireWeapon or GuiCommandKind.CombatDrop;
            yield return offensive ? (null, arena.EnemyGround, "the enemy's place") : (null, arena.EmptyGround, "an empty place");
            yield return offensive ? (null, arena.EmptyGround, "an empty place") : (null, arena.EnemyGround, "the enemy's place");
        }
    }

    private static Vector3? FindSite(SimulationWorld world, CommandButtonInfo button, Entity? builder)
    {
        var power = button.Kind is GuiCommandKind.SpecialPowerConstruct or GuiCommandKind.SpecialPowerConstructFromShortcut;
        var origin = power ? new Vector2(Centre + 400, Centre) : builder is null ? new Vector2(Centre, Centre) : SimulationQueries.GroundOf(builder);
        for (var distance = 120; distance <= 400; distance += 20)
            for (var step = 0; step < 16; step++)
            {
                var angle = step * MathF.PI / 8f;
                var point = origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
                var (x, y) = (SimulationQueries.ToFixed(point.X), SimulationQueries.ToFixed(point.Y));
                var issue = power ? SimulationQueries.CheckPowerPlacement(world, builder, button.Object, x, y, Fixed.Zero)
                    : SimulationQueries.CheckPlacement(world, Me, builder, button.Object, x, y, Fixed.Zero);
                if (issue == PlacementIssue.None) return new Vector3(point, 0);
            }
        return null;
    }

    // ------------------------------------------------------------------------------------------------ what shows that it worked

    private enum Evidence { Accepted, Produced, Constructed, Upgraded, Science, Difference, WeaponOrDifference, WeaponSlot, Overcharge, Moved, Emptied, Sold, Beacon }

    private static Evidence EvidenceKindOf(CommandButtonInfo button) => button.Kind switch
    {
        GuiCommandKind.UnitBuild => Evidence.Produced,
        GuiCommandKind.DozerConstruct or GuiCommandKind.SpecialPowerConstruct or GuiCommandKind.SpecialPowerConstructFromShortcut => Evidence.Constructed,
        GuiCommandKind.PlayerUpgrade or GuiCommandKind.ObjectUpgrade => Evidence.Upgraded,
        GuiCommandKind.PurchaseScience => Evidence.Science,
        GuiCommandKind.SpecialPower or GuiCommandKind.SpecialPowerFromShortcut or GuiCommandKind.HijackVehicle or GuiCommandKind.ConvertToCarbomb
            or GuiCommandKind.SabotageBuilding or GuiCommandKind.HackInternet => Evidence.Difference,
        GuiCommandKind.CombatDrop => Evidence.Emptied,
        GuiCommandKind.FireWeapon => Evidence.WeaponOrDifference,
        GuiCommandKind.SwitchWeapon => Evidence.WeaponSlot,
        GuiCommandKind.ToggleOvercharge => Evidence.Overcharge,
        GuiCommandKind.AttackMove or GuiCommandKind.Guard or GuiCommandKind.GuardWithoutPursuit or GuiCommandKind.GuardFlyingUnitsOnly => Evidence.Moved,
        GuiCommandKind.Evacuate => Evidence.Emptied,
        GuiCommandKind.Sell => Evidence.Sold,
        GuiCommandKind.PlaceBeacon => Evidence.Beacon,
        _ => Evidence.Accepted,
    };

    private static string ExpectationOf(CommandButtonInfo button, Evidence evidence) => evidence switch
    {
        Evidence.Produced => $"no new {button.Object}",
        Evidence.Constructed => $"no {button.Object} placed",
        Evidence.Upgraded => $"{button.Upgrade} not completed or no module of it switched on",
        Evidence.Science => "the science is not owned",
        Evidence.Difference => "the world is the same as without the press",
        Evidence.WeaponOrDifference => "the source fired nothing and the world is the same as without the press",
        Evidence.WeaponSlot => $"the current weapon is not {button.WeaponSlot}",
        Evidence.Overcharge => "not overcharged",
        Evidence.Moved => "the unit did not move",
        Evidence.Emptied => "the riders are still inside",
        Evidence.Sold => "the structure is still there",
        Evidence.Beacon => "no beacon",
        _ => "not accepted",
    };

    private static int BudgetSeconds(CommandButtonInfo button) => EvidenceKindOf(button) switch
    {
        Evidence.Produced or Evidence.Constructed => 90,
        Evidence.Upgraded => 60,
        Evidence.Difference => 45,
        Evidence.WeaponOrDifference => 20,
        Evidence.Sold => 20,
        Evidence.Emptied => button.Kind == GuiCommandKind.CombatDrop ? 45 : 20,
        Evidence.Moved => 10,
        _ => 2,
    };

    private static int Count(SimulationWorld world, string template)
        => template.Length == 0 ? 0 : world.Entities.Count(entity => entity.Owner == Me && entity.Template.Name.Equals(template, StringComparison.OrdinalIgnoreCase));

    /// <summary>The evidence of <paramref name="evidence"/>, or null while there is none yet.</summary>
    private string? Check(Arena test, Arena? control, CommandButtonInfo button, Evidence evidence, int baseline)
    {
        var world = test.World;
        var source = world.FindEntity(test.Source);
        switch (evidence)
        {
            case Evidence.Accepted:
                return "accepted";
            case Evidence.Produced:
                return Count(world, button.Object) > baseline ? $"{button.Object} made" : null;
            case Evidence.Constructed:
                return Count(world, button.Object) > baseline ? $"{button.Object} placed" : null;
            case Evidence.Science:
                return button.Sciences.Any(name => _catalog.Sciences.TryGet(name, out var info) && world.PlayerSciences(Me).Has(info.Index)) ? "science owned" : null;
            case Evidence.Upgraded:
            {
                if (!_catalog.Upgrades.TryGet(button.Upgrade, out var upgrade)) return null;
                var done = upgrade.Scope == UpgradeScope.Player ? world.PlayerUpgrades(Me).Has(upgrade.Index) : source?.CompletedUpgrades.Has(upgrade.Index) == true;
                if (!done && source is null && test.Events.Any(item => item.Kind == SimEventKind.UpgradeCompleted && item.Player == Me && item.Text.Equals(upgrade.Name, StringComparison.OrdinalIgnoreCase)))
                    return $"completed, the object replaced by {string.Join(" ", world.Entities.Where(entity => entity.Owner == Me && entity.SpawnTick > test.PressTick).Select(entity => entity.Template.Name).Distinct().Order(StringComparer.Ordinal))}";
                // A module that removes the upgrade that switched it on (the GLA worker's switch back to its real buildings, RemovesUpgrades): the object holds no bit, the module ran.
                var removedBySelf = !done && source is not null && test.Events.Any(item => item.Kind == SimEventKind.UpgradeCompleted && item.Entity == source.Id
                    && item.Text.Equals(upgrade.Name, StringComparison.OrdinalIgnoreCase));
                if (!done && !removedBySelf) return null;
                var switched = SwitchedModules(world, upgrade.Name);
                if (switched.Count > 0)
                {
                    var offers = source is not null && switched.Any(name => name.Contains("/CommandSetUpgrade:", StringComparison.Ordinal)) && test.Bar.ActiveCommandSet(world, source) is { } set ? $", offers {set}" : string.Empty;
                    return $"completed{(removedBySelf ? " and removed by its own module" : string.Empty)}, switched on {string.Join(" ", switched)}{offers}";
                }
                if (removedBySelf) return null;
                var expected = FactionRoster.TriggeredModules(_graph, world.Entities.Where(entity => entity.Owner == Me).Select(entity => entity.Template.Name).ToHashSet(StringComparer.OrdinalIgnoreCase), upgrade.Name);
                return expected.IsEmpty ? "completed (no module of a present object names it)" : null;
            }
            case Evidence.WeaponSlot:
                return source is not null && source.CurrentWeaponSlot == button.WeaponSlotIndex ? $"weapon {button.WeaponSlot}" : null;
            case Evidence.Overcharge:
                return source?.Modules.OfType<OverchargeBehavior>().Any(module => module.Active) == true ? "overcharged" : null;
            case Evidence.Moved:
                return source is not null && (source.HasMoveTarget || source.Speed != Fixed.Zero) ? "moving" : null;
            case Evidence.Emptied:
                return source?.Container is { ContainCount: 0 } ? "riders out" : null;
            case Evidence.Sold:
                return source is null || source.HasStatus(ObjectStatusBits.Sold) || test.Events.Any(item => item.Kind == SimEventKind.StructureSold) ? "sold" : null;
            case Evidence.Beacon:
                return test.Events.Any(item => item.Kind == SimEventKind.BeaconPlaced) ? "beacon placed" : null;
            case Evidence.WeaponOrDifference:
            {
                // The weapon of the button's slot, in any of the source's weapon sets: the source's own fire with another weapon proves nothing.
                var slotWeapons = (source?.Template.WeaponSets ?? []).Where(set => button.WeaponSlotIndex < set.Weapons.Length && set.Weapons[button.WeaponSlotIndex] >= 0)
                    .Select(set => _catalog.Weapons[set.Weapons[button.WeaponSlotIndex]].Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (test.Events.FirstOrDefault(item => item.Kind == SimEventKind.WeaponFired && item.Entity == test.Source && slotWeapons.Contains(item.Text)) is { Text.Length: > 0 } shot)
                    return $"the source fired {shot.Text}";
                return control is null ? null : Difference(test, control, button);
            }
            case Evidence.Difference when button.Kind == GuiCommandKind.SabotageBuilding:
                // The saboteur's way in ends in a sabotage of the building (Sabotage*CrateCollide), whatever else changed on the way.
                return test.Events.FirstOrDefault(item => item.Kind == SimEventKind.Sabotaged && item.Entity == test.Target) is { Text.Length: > 0 } sabotage
                    ? $"sabotaged ({sabotage.Text})" : null;
            case Evidence.Difference:
                return control is null ? null : Difference(test, control, button);
        }
        return null;
    }

    /// <summary>The modules of the player's objects that a completed upgrade switched on (<c>Object/Module:Tag</c>).</summary>
    private List<string> SwitchedModules(SimulationWorld world, string upgrade)
    {
        var result = new List<string>();
        foreach (var entity in world.Entities.Where(entity => entity.Owner == Me).OrderBy(entity => entity.Id.Value))
        {
            var expected = FactionRoster.TriggeredModules(_graph, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entity.Template.Name }, upgrade);
            if (expected.IsEmpty) continue;
            foreach (var module in entity.Modules)
            {
                if (module is not IUpgradeModule) continue;
                var name = $"{entity.Template.Name}/{module.ModuleName}:{module.Tag}";
                if (!expected.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                var executed = module is UpgradeMuxModule mux ? mux.Executed : SimulationQueries.Describe(module).Bool("executed");
                if (executed) result.Add(name);
            }
        }
        return [..result.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// What differs between the pressed world and the control. An object both worlds have alive (by id: the worlds were built alike) that differs in owner, place in a container, status,
    /// disablement, bonus or disguise; the target's health and its life; the player's own objects (made, lost, put somewhere); the enemy's money, and the player's for an internet hack;
    /// what the player sees, when the source stood where it stands in the control. Not: an enemy that one world's fire killed and the other's did not, nor the player's money from bounties
    /// - the source fights a course of its own when it is not pressed.
    /// </summary>
    private static string? Difference(Arena test, Arena control, CommandButtonInfo button)
    {
        var a = Observe(test, test.Target);
        var b = Observe(control, test.Target);
        var differences = new List<string>();
        if (button.Kind == GuiCommandKind.HackInternet && a.MyMoney != b.MyMoney) differences.Add($"money {b.MyMoney}->{a.MyMoney}");
        if (a.EnemyMoney != b.EnemyMoney) differences.Add($"enemy money {b.EnemyMoney}->{a.EnemyMoney}");
        var stood = test.World.FindEntity(test.Source) is not { } mine || control.World.FindEntity(control.Source) is { } theirs && mine.X == theirs.X && mine.Y == theirs.Y;
        if (a.Visible != b.Visible && stood) differences.Add($"seen {b.Visible}->{a.Visible}");
        // Of the objects that stood at the press: the target, the player's own, and whatever changed hands. An enemy's other objects react to the source's own fire, which the press
        // changes.
        var changed = a.Alive.Where(pair => test.PressIds.Contains(pair.Key) && b.Alive.TryGetValue(pair.Key, out var other) && other != pair.Value
                                            && (pair.Key == test.Target.Value || Owner(pair.Value) != Owner(other) || Owner(pair.Value) == Me.Value))
            .Select(pair => $"{b.Alive[pair.Key]} -> {pair.Value}").Take(3).ToList();
        if (changed.Count > 0) differences.Add($"changed {string.Join(" ", changed)}");
        // A hazard cleaned up (the cleanup powers).
        if (a.Hazards < b.Hazards) differences.Add($"hazards {b.Hazards}->{a.Hazards}");
        // The target took harm it did not take in the control (the control's own fire at it is no evidence of the press).
        if (a.TargetHealth is null && b.TargetHealth is not null) differences.Add("the target is gone");
        else if (a.TargetHealth < b.TargetHealth) differences.Add($"the target's health {b.TargetHealth}->{a.TargetHealth}");
        var counts = a.Own.Concat(b.Own).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(kind => (Kind: kind, Delta: a.Own.Count(value => value == kind) - b.Own.Count(value => value == kind))).Where(pair => pair.Delta != 0).ToList();
        var only = counts.Where(pair => pair.Delta > 0).Select(pair => $"{pair.Kind}x{pair.Delta}").Take(3).ToList();
        var gone = counts.Where(pair => pair.Delta < 0).Select(pair => $"{pair.Kind}x{-pair.Delta}").Take(3).ToList();
        if (only.Count > 0 || gone.Count > 0) differences.Add($"own objects +[{string.Join(" ", only)}] -[{string.Join(" ", gone)}]");
        return differences.Count == 0 ? null : string.Join("; ", differences);
    }

    /// <summary>The owner written into an object's description (<c>Template@owner...</c>).</summary>
    private static int Owner(string description)
    {
        var at = description.IndexOf('@', StringComparison.Ordinal);
        var end = at;
        while (end + 1 < description.Length && char.IsAsciiDigit(description[end + 1])) end++;
        return at < 0 || end == at ? -1 : int.Parse(description.AsSpan(at + 1, end - at), CultureInfo.InvariantCulture);
    }

    /// <summary>What the source does rather than is: it uses an ability, attacks, aims, fires, brakes. A press that only sets these has done nothing yet.</summary>
    private static readonly ulong Transient = new[] { "IS_USING_ABILITY", "IS_ATTACKING", "IS_AIMING_WEAPON", "IS_FIRING_WEAPON", "IS_BRAKING" }
        .Aggregate(0UL, (mask, name) => CnCNew.Data.Definitions.NameTables.ObjectStatus.TryIndexOf(name, out var index) ? mask | 1UL << index : mask);

    /// <param name="Alive">Every living object by id, with what it is (<see cref="Observe"/>).</param>
    /// <param name="Own">The player's living objects as kinds (template, place in a container), sorted: the ids of what the press made differ from the control's.</param>
    private sealed record Observation(long MyMoney, long EnemyMoney, int Visible, IReadOnlyDictionary<uint, string> Alive, ImmutableArray<string> Own, long? TargetHealth, int Hazards);

    private static Observation Observe(Arena arena, EntityId target)
    {
        var world = arena.World;
        var alive = new SortedDictionary<uint, string>();
        var own = new List<string>();
        var hazards = 0;
        foreach (var entity in world.Entities.OrderBy(entity => entity.Id.Value))
        {
            if (entity.DestroyRequested || entity.EffectivelyDead) continue;
            var disabled = world.DisabledFlagsOf(entity);
            var disguise = entity.Modules.OfType<StealthUpdate>().FirstOrDefault()?.DisguisedTemplate ?? -1;
            // The source's own status and bonuses follow what it does (walking to a target, a veteran's bonus): of the source only what it becomes counts.
            var state = entity.Id == arena.Source ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $":st{entity.Status & ~Transient:X}:dis{(int)disabled}:wb{entity.WeaponBonusConditions:X}");
            var contained = entity.ContainedBy.IsValid ? "(in)" : string.Empty;
            alive[entity.Id.Value] = string.Create(CultureInfo.InvariantCulture,
                $"{entity.Template.Name}@{entity.Owner.Value}{contained}{state}{(disguise >= 0 ? ":as" + disguise : "")}");
            // A projectile of the player's is the course of a fight, not what the press made.
            if (entity.Owner == Me && (!entity.Template.IsKindOf("PROJECTILE") || entity.Template.IsKindOf("MINE"))) own.Add(entity.Template.Name + contained);
            if (entity.Template.IsKindOf("CLEANUP_HAZARD")) hazards++;
        }
        own.Sort(StringComparer.Ordinal);
        var visible = 0;
        for (var x = 0; x < MapCells * 10; x += 80)
            for (var y = 0; y < MapCells * 10; y += 80)
                if (world.IsPositionVisibleTo(Me, Fixed.FromInt(x), Fixed.FromInt(y))) visible++;
        long? targetHealth = world.FindEntity(target) is { DestroyRequested: false, EffectivelyDead: false } aimed ? aimed.Body?.Health.Floor() ?? 0 : null;
        return new Observation(world.MoneyOf(Me).Floor(), world.MoneyOf(Enemy).Floor(), visible, alive, [..own], targetHealth, hazards);
    }

    // ------------------------------------------------------------------------------------------------ plumbing

    /// <summary>The button whose probe prints its world once a second of game time (<c>CNCNEW_FACTION_TRACE</c>): to find why a press shows nothing.</summary>
    public static string? Trace { get; set; } = Environment.GetEnvironmentVariable("CNCNEW_FACTION_TRACE");

    /// <summary>Every press named on the error stream before it is made (<c>CNCNEW_FACTION_PROGRESS=1</c>): the last line names a press that ended the process.</summary>
    public static bool Progress { get; set; } = Environment.GetEnvironmentVariable("CNCNEW_FACTION_PROGRESS") == "1";

    private static void TraceState(Arena arena, int candidate)
    {
        var world = arena.World;
        var line = new System.Text.StringBuilder(string.Create(CultureInfo.InvariantCulture, $"trace t={world.Tick} candidate={candidate}"));
        foreach (var entity in world.Entities.Where(entity => entity.Id == arena.Source || entity.ContainedBy == arena.Source || arena.EnemyObjects.Contains(entity.Id) || entity.Owner == Me && entity.Template.IsKindOf("INFANTRY")).OrderBy(entity => entity.Id.Value))
            line.Append(string.Create(CultureInfo.InvariantCulture,
                $" | {entity.Template.Name} at {entity.X.Floor()},{entity.Y.Floor()},{entity.Z.Floor()} hp {(entity.Body?.Health.Floor() ?? 0)} st {entity.Status:X} move {entity.HasMoveTarget} slot {entity.CurrentWeaponSlot}"));
        foreach (var item in arena.Events.Where(item => item.Tick > world.Tick - world.Time.TicksPerSecond && item.Kind is not (SimEventKind.FxRequested or SimEventKind.MoveArrived)))
            line.Append(string.Create(CultureInfo.InvariantCulture, $" | {item.Kind} {item.Entity.Value}->{item.Other.Value} {item.Text} {item.Value}"));
        Console.Error.WriteLine(line.ToString());
    }

    private void Step(Arena arena)
    {
        var result = arena.World.Step();
        arena.Events.AddRange(result.Events);
        SecondsStepped += 1.0 / arena.World.Time.TicksPerSecond;
    }

    private static ProbeResult Result(FactionRoster roster, string owner, RosterButton entry, ProbeOutcome outcome, string detail, double seconds)
        => new(roster.Faction, owner, entry.CommandSet, entry.Slot, entry.Button.Name, entry.Button.Kind, outcome, detail, seconds);

    /// <summary>Flat ground of 256 by 256 cells, without water or cliffs.</summary>
    private static TerrainMap FlatTerrain()
    {
        const int border = 1;
        var width = MapCells + 2 * border;
        var stride = (width + 7) / 8;
        var blend = new MapBlendTiles(8, [], [], [], [], [..new byte[stride * width]], stride, 0, 0, [], [], [], []);
        var map = new MapData("faction-audit", new MapHeightField(width, width, border, [new MapBoundary(MapCells, MapCells)], [..new byte[width * width]]), blend,
            MapProperties.Empty, [], [], [], [], [], [], []);
        return TerrainMap.Build(map, "faction-audit");
    }

    /// <summary>A view straight down in which a screen point is the ground point of the same coordinates.</summary>
    private sealed class GroundView : IViewProjection
    {
        public Vector2 ViewportSize => new(MapCells * 10, MapCells * 10);
        public Matrix4x4 ViewProjection => Matrix4x4.Identity;
        public void GetScreenRay(Vector2 screen, out Vector3 origin, out Vector3 direction)
        {
            origin = new Vector3(screen, 1000);
            direction = -Vector3.UnitZ;
        }
        public bool TryScreenToGround(Vector2 screen, out Vector3 world)
        {
            world = new Vector3(screen, 0);
            return true;
        }
        public bool TryWorldToScreen(Vector3 world, out Vector2 screen)
        {
            screen = new Vector2(world.X, world.Y);
            return true;
        }
    }
}

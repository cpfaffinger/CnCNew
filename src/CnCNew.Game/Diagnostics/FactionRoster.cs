using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Templates;
using CommandButtonInfo = CnCNew.Presentation.Input.CommandButtonInfo;

namespace CnCNew.Game.Diagnostics;

/// <summary>One button of a command set the roster reaches: the set, its 1-based slot and the button.</summary>
public sealed record RosterButton(string CommandSet, int Slot, CommandButtonInfo Button);

/// <summary>
/// An object a player of the faction can have: <see cref="Origin"/> says how the roster reached it (the faction's start, a builder's or a producer's button, a special power's or a
/// module's creation list, a rider), <see cref="Buttons"/> are every button of every command set it can show (its own, those a <c>CommandSetUpgrade</c> switches to, a rider's).
/// </summary>
public sealed record RosterObject(string Name, string Origin, bool Structure, ImmutableArray<string> CommandSets, ImmutableArray<RosterButton> Buttons);

/// <summary>A science of the general's promotion window: the rank whose set shows it, its purchase cost and the special powers that require it.</summary>
public sealed record RosterScience(string Name, int Rank, int Cost, ImmutableArray<string> Prerequisites, ImmutableArray<string> Powers);

/// <summary>An upgrade a button of the roster buys, with the modules of roster objects it switches on (<c>TriggeredBy</c>) as <c>Object/Module:Tag</c>.</summary>
public sealed record RosterUpgrade(string Name, UpgradeScope Scope, ImmutableArray<string> Buttons, ImmutableArray<string> Modules);

/// <summary>A special power of the faction: the buttons that use it and the objects that carry a module of it.</summary>
public sealed record RosterPower(string Name, string Type, string RequiredScience, ImmutableArray<string> Buttons, ImmutableArray<string> Carriers);

/// <summary>
/// What a player of one faction can have and do, followed from its <c>PlayerTemplate</c> through the command sets (#411): the start, every structure a builder can place, every unit a
/// structure offers, every object a module or a special power of those creates that has a command set of its own, every upgrade, science and special power and every button.
/// </summary>
public sealed record FactionRoster(
    string Faction,
    string Side,
    string BaseFaction,
    ImmutableArray<RosterObject> Objects,
    ImmutableArray<RosterUpgrade> Upgrades,
    ImmutableArray<RosterScience> Sciences,
    ImmutableArray<RosterPower> Powers,
    ImmutableArray<RosterButton> ScienceButtons,
    ImmutableArray<RosterButton> ShortcutButtons)
{
    /// <summary>The sciences an upgrade of a roster object grants (<c>GrantScienceUpgrade</c>: the MOAB, the Internet Center's second satellite hack), outside the general's window.</summary>
    public ImmutableArray<string> GrantedSciences { get; init; } = [];

    /// <summary>What the roster's objects make that a player does not command (projectiles, slaves, debris): read for the modules an upgrade switches on.</summary>
    public ImmutableArray<string> Made { get; init; } = [];

    public RosterObject? Find(string name) => Objects.FirstOrDefault(value => value.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The twelve factions a human can choose in the original's skirmish setup.</summary>
    public static readonly ImmutableArray<string> PlayableFactions =
    [
        "FactionAmerica", "FactionAmericaAirForceGeneral", "FactionAmericaLaserGeneral", "FactionAmericaSuperWeaponGeneral",
        "FactionChina", "FactionChinaTankGeneral", "FactionChinaInfantryGeneral", "FactionChinaNukeGeneral",
        "FactionGLA", "FactionGLAToxinGeneral", "FactionGLADemolitionGeneral", "FactionGLAStealthGeneral",
    ];

    /// <summary>
    /// Builds the roster of <paramref name="factionName"/>. Objects are reached through build buttons (always included) and through the references of a reached object's definition
    /// and of the creation lists it names (included when the object has a command set: what a player can select and order - a drone, a pilot, the gunship of a power).
    /// </summary>
    public static FactionRoster Build(CompiledDefinitionGraph graph, TemplateCatalog catalog, CommandButtonCatalog buttons, string factionName)
    {
        if (!catalog.Factions.TryGet(factionName, out var faction)) throw new ArgumentException($"Unknown faction '{factionName}'.", nameof(factionName));
        // What an object makes is named by its modules (not by its own fields: a prerequisite names a structure it needs, not one it makes). A power's UpgradeOCL is a list per
        // science: only the lists of sciences the faction can own count (every general's ambush is named by every command center).
        var factionSciences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setName in new[] { faction.ScienceSetRank1, faction.ScienceSetRank3, faction.ScienceSetRank8 })
            if (setName.Length > 0 && buttons.TryGetSet(setName, out var scienceSet))
                foreach (var button in scienceSet.Slots.OfType<CommandButtonInfo>())
                    foreach (var science in button.Sciences) factionSciences.Add(science);
        foreach (var science in catalog.Sciences.Sciences.Where(value => faction.IntrinsicSciences.Has(value.Index))) factionSciences.Add(science.Name);
        var references = graph.References
            .Where(edge => edge.Source.Kind != DefinitionKind.Object || MakesWhatItNames(edge.Field))
            .GroupBy(edge => edge.Source)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Target)
                .Where(target => !target.IsNone && target.Kind is DefinitionKind.Object or DefinitionKind.ObjectCreationList or DefinitionKind.Weapon).ToArray());

        var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var made = new SortedSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        var followed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var madeQueue = new Queue<ObjectTemplate>();
        void Reach(string name, string origin, bool always)
        {
            if (name.Length == 0 || origins.ContainsKey(name) || !catalog.TryGet(name, out var template)) return;
            // A button whose object is not buildable is hidden (ControlBar::getCommandAvailability, BSTATUS_NO): a player never gets the object that way.
            if (always && origin != "start" && template.Buildable.Equals("No", StringComparison.OrdinalIgnoreCase)) return;
            if (!always && SetsOf(template, buttons).Length == 0)
            {
                // Something the faction's objects make that a player does not command (a projectile, a slave of a spawner): it can read an upgrade.
                if (made.Add(template.Name)) madeQueue.Enqueue(template);
                return;
            }
            origins[template.Name] = origin;
            queue.Enqueue(template.Name);
        }

        // Objects a reached object creates (its modules' creation lists, object names and its weapons' projectiles), followed through creation lists and weapons.
        void FollowReferences(ObjectTemplate template)
        {
            if (!followed.Add(template.Name)) return;
            var seen = new HashSet<DefinitionId>();
            var pending = new Stack<(DefinitionId Id, string Via)>();
            pending.Push((template.Id, template.Name));
            foreach (var weapon in template.WeaponSets.SelectMany(set => set.Weapons).Where(index => index >= 0).Distinct().Select(index => catalog.Weapons[index]))
            {
                Reach(weapon.ProjectileObject, $"fired by {template.Name} ({weapon.Name})", always: false);
                foreach (var list in new[] { weapon.FireOcl, weapon.DetonationOcl }.Where(value => value.Length > 0))
                    pending.Push((new DefinitionId(DefinitionKind.ObjectCreationList, list), weapon.Name));
            }
            foreach (var module in template.Modules)
                if (module.Data is OclSpecialPowerData power)
                    foreach (var (science, list) in power.UpgradeOcls)
                        if (factionSciences.Contains(science)) pending.Push((new DefinitionId(DefinitionKind.ObjectCreationList, list), list));
            while (pending.Count > 0)
            {
                var (id, via) = pending.Pop();
                if (!seen.Add(id) || !references.TryGetValue(id, out var targets)) continue;
                foreach (var target in targets)
                {
                    if (target.Kind is DefinitionKind.ObjectCreationList or DefinitionKind.Weapon) pending.Push((new DefinitionId(target.Kind, target.Name), target.Name));
                    else if (!target.Name.Equals(template.Name, StringComparison.OrdinalIgnoreCase)) Reach(target.Name, $"created by {template.Name} ({via})", always: false);
                }
            }
        }

        Reach(faction.StartingBuilding, "start", always: true);
        foreach (var unit in faction.StartingUnits) Reach(unit, "start", always: true);

        var objects = new List<RosterObject>();
        var upgradeButtons = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var powerButtons = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        void NoteButton(RosterButton entry, string owner)
        {
            var button = entry.Button;
            if (button.Upgrade.Length > 0 && button.Kind is GuiCommandKind.PlayerUpgrade or GuiCommandKind.ObjectUpgrade)
                Add(upgradeButtons, button.Upgrade, $"{owner}:{button.Name}");
            if (button.SpecialPower.Length > 0) Add(powerButtons, button.SpecialPower, $"{owner}:{button.Name}");
        }

        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            var template = catalog.TryGet(name, out var found) ? found : throw new InvalidOperationException(name);
            var sets = SetsOf(template, buttons);
            var entries = new List<RosterButton>();
            foreach (var setName in sets)
            {
                if (!buttons.TryGetSet(setName, out var set)) continue;
                for (var index = 0; index < set.Slots.Length; index++)
                {
                    if (set.Slots[index] is not { } button) continue;
                    var entry = new RosterButton(set.Name, index + 1, button);
                    entries.Add(entry);
                    NoteButton(entry, template.Name);
                    switch (button.Kind)
                    {
                        case GuiCommandKind.DozerConstruct: Reach(button.Object, $"built by {template.Name}", always: true); break;
                        case GuiCommandKind.UnitBuild: Reach(button.Object, $"produced by {template.Name}", always: true); break;
                        case GuiCommandKind.SpecialPowerConstruct or GuiCommandKind.SpecialPowerConstructFromShortcut: Reach(button.Object, $"placed by a power of {template.Name}", always: true); break;
                    }
                }
            }
            FollowReferences(template);
            objects.Add(new RosterObject(template.Name, origins[template.Name], template.IsKindOf("STRUCTURE"), sets, [..entries]));
            while (queue.Count == 0 && madeQueue.Count > 0) FollowReferences(madeQueue.Dequeue());
        }

        // The general's promotion window and the power shortcuts.
        var scienceButtons = new List<RosterButton>();
        var sciences = new SortedDictionary<string, (int Rank, ScienceInfo Info)>(StringComparer.Ordinal);
        foreach (var (rank, setName) in new[] { (1, faction.ScienceSetRank1), (3, faction.ScienceSetRank3), (8, faction.ScienceSetRank8) })
        {
            if (setName.Length == 0 || !buttons.TryGetSet(setName, out var set)) continue;
            for (var index = 0; index < set.Slots.Length; index++)
            {
                if (set.Slots[index] is not { } button) continue;
                var entry = new RosterButton(set.Name, index + 1, button);
                scienceButtons.Add(entry);
                NoteButton(entry, set.Name);
                foreach (var science in button.Sciences)
                    if (catalog.Sciences.TryGet(science, out var info) && !sciences.ContainsKey(info.Name))
                        sciences[info.Name] = (rank, info);
            }
        }
        var shortcutButtons = new List<RosterButton>();
        if (faction.PowerShortcutSet.Length > 0 && buttons.TryGetSet(faction.PowerShortcutSet, out var shortcuts))
            for (var index = 0; index < shortcuts.Slots.Length; index++)
                if (shortcuts.Slots[index] is { } button)
                {
                    var entry = new RosterButton(shortcuts.Name, index + 1, button);
                    shortcutButtons.Add(entry);
                    NoteButton(entry, shortcuts.Name);
                }

        // Every power a roster object carries, whether or not a button names it (a power the shortcut bar fires from another object, a power only a science unlocks).
        var carriers = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var rosterNames = objects.Select(value => value.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>())
        {
            if (!rosterNames.Contains(definition.OriginalName)) continue;
            foreach (var module in definition.Modules)
                foreach (var field in module.Fields)
                    if (field.Name.Equals("SpecialPowerTemplate", StringComparison.OrdinalIgnoreCase) && field.Value.OriginalText.Trim() is { Length: > 0 } power
                        && !power.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                        Add(carriers, catalog.SpecialPowers.TryGet(power, out var info) ? info.Name : power, definition.OriginalName);
        }
        var powers = new List<RosterPower>();
        foreach (var name in powerButtons.Keys.Union(carriers.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            var info = catalog.SpecialPowers.TryGet(name, out var known) ? known : null;
            var science = info is { RequiredScience: >= 0 } ? catalog.Sciences.Sciences.FirstOrDefault(value => value.Index == info!.RequiredScience)?.Name ?? string.Empty : string.Empty;
            powers.Add(new RosterPower(info?.Name ?? name, info?.Type ?? "UNKNOWN", science,
                [..powerButtons.GetValueOrDefault(name) ?? []], [..carriers.GetValueOrDefault(name) ?? []]));
        }

        var scienceList = sciences.Values.Select(value => new RosterScience(value.Info.Name, value.Rank, value.Info.PurchasePointCost, value.Info.PrerequisiteNames,
            [..powers.Where(power => power.RequiredScience.Equals(value.Info.Name, StringComparison.OrdinalIgnoreCase)).Select(power => power.Name)])).ToImmutableArray();

        var objectNames = objects.Select(value => value.Name).Concat(made).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var upgrades = new List<RosterUpgrade>();
        foreach (var (name, users) in upgradeButtons)
        {
            var info = catalog.Upgrades.TryGet(name, out var known) ? known : null;
            upgrades.Add(new RosterUpgrade(info?.Name ?? name, info?.Scope ?? UpgradeScope.Player, [..users], UpgradeReaders(graph, objectNames, name)));
        }

        var side = faction.Side;
        // A general's names carry a prefix before its side's (Chem_GLACommandCenter, GLACommandCenter): its base faction starts with the building without it.
        var baseFaction = catalog.Factions.Factions.Where(value => value.PlayableSide && value.StartingBuilding.Length > 0 && value.Name != faction.Name
                                                                   && faction.StartingBuilding.EndsWith("_" + value.StartingBuilding, StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Name).Order(StringComparer.Ordinal).FirstOrDefault() ?? faction.Name;
        return new FactionRoster(faction.Name, side, baseFaction, [..objects.OrderBy(value => value.Name, StringComparer.Ordinal)], [..upgrades], scienceList, [..powers],
            [..scienceButtons], [..shortcutButtons])
        {
            Made = [..made.Where(name => !origins.ContainsKey(name))],
            GrantedSciences = [..graph.Definitions.OfType<ObjectDefinition>().Where(definition => objectNames.Contains(definition.OriginalName))
                .SelectMany(definition => definition.Modules).Where(module => module.Name.Equals("GrantScienceUpgrade", StringComparison.OrdinalIgnoreCase))
                .SelectMany(module => module.Fields).Where(field => field.Name.Equals("GrantScience", StringComparison.OrdinalIgnoreCase))
                .Select(field => field.Value.OriginalText.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)],
        };
    }

    /// <summary>
    /// Whether a field of an object definition names something the object makes: a module's field (not a power's <c>UpgradeOCL</c>, which is a list per science and followed
    /// by the faction's sciences) or a weapon of a weapon set. An object's own fields and its <c>Prerequisites</c> name what it needs, not what it makes.
    /// </summary>
    private static bool MakesWhatItNames(string field)
        => field.StartsWith("module[", StringComparison.Ordinal) && !field.EndsWith("/UpgradeOCL", StringComparison.OrdinalIgnoreCase)
           || field.StartsWith("section[", StringComparison.Ordinal) && field.Contains(":WeaponSet/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every command set the object can show: the template's (its own and the <c>CommandSetUpgrade</c> sets) and a rider's (<c>RiderChangeContain</c>).</summary>
    public static ImmutableArray<string> SetsOf(ObjectTemplate template, CommandButtonCatalog buttons)
    {
        var sets = new List<string>();
        foreach (var set in template.CommandSets)
            if (set.Length > 0 && !set.Equals("NONE", StringComparison.OrdinalIgnoreCase) && buttons.TryGetSet(set, out _)) sets.Add(set);
        foreach (var module in template.Modules)
            if (module.Data is RiderChangeContainData rider)
                foreach (var info in rider.Riders)
                    if (info.CommandSet.Length > 0 && buttons.TryGetSet(info.CommandSet, out _)) sets.Add(info.CommandSet);
        return [..sets.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The upgrades the original's engine reads by name rather than through a module's field, with the module that reads them: the horde bonus (<c>AIUpdate.cpp</c>,
    /// <c>AIUpdateInterface::evaluateMoraleBonus</c>), the Chinook's supply lines (<c>ChinookAIUpdate.cpp</c>), the worker's shoes (<c>WorkerAIUpdate.cpp</c>), the EMP mines
    /// (<c>GenerateMinefieldBehavior.cpp</c>).
    /// </summary>
    public static readonly ImmutableDictionary<string, string> EngineReaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Upgrade_Nationalism"] = "HordeUpdate", ["Upgrade_Fanaticism"] = "HordeUpdate", ["Upgrade_AmericaSupplyLines"] = "ChinookAIUpdate",
        ["Upgrade_GLAWorkerShoes"] = "WorkerAIUpdate", ["Upgrade_ChinaEMPMines"] = "GenerateMinefieldBehavior",
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every module of the named objects that reads <paramref name="upgrade"/>: a field that names it (<c>TriggeredBy</c>, <c>UpgradeRequired</c>, <c>UpgradedBoost</c> ..) or, for an upgrade the
    /// engine reads by name, a module of the type that reads it (<see cref="EngineReaders"/>); as <c>Object/Module:Tag</c>.
    /// </summary>
    public static ImmutableArray<string> UpgradeReaders(CompiledDefinitionGraph graph, IReadOnlySet<string> objects, string upgrade)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var engine = EngineReaders.GetValueOrDefault(upgrade);
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>())
        {
            if (!objects.Contains(definition.OriginalName)) continue;
            foreach (var module in definition.Modules)
                if (module.Name.Equals(engine, StringComparison.OrdinalIgnoreCase)
                    || module.Fields.Any(field => field.Value.OriginalText.Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries).Contains(upgrade, StringComparer.OrdinalIgnoreCase)))
                    result.Add($"{definition.OriginalName}/{module.Name}:{module.Tag}");
        }
        return [..result];
    }

    /// <summary>The modules of the named objects whose <c>TriggeredBy</c> names <paramref name="upgrade"/>, as <c>Object/Module:Tag</c>.</summary>
    public static ImmutableArray<string> TriggeredModules(CompiledDefinitionGraph graph, IReadOnlySet<string> objects, string upgrade)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>())
        {
            if (!objects.Contains(definition.OriginalName)) continue;
            foreach (var module in definition.Modules)
                foreach (var field in module.Fields)
                    if (field.Name.Equals("TriggeredBy", StringComparison.OrdinalIgnoreCase)
                        && field.Value.OriginalText.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Contains(upgrade, StringComparer.OrdinalIgnoreCase))
                        result.Add($"{definition.OriginalName}/{module.Name}:{module.Tag}");
        }
        return [..result];
    }

    private static void Add(SortedDictionary<string, SortedSet<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var set)) map[key] = set = new SortedSet<string>(StringComparer.Ordinal);
        set.Add(value);
    }
}

/// <summary>One thing the data of a roster show: <see cref="Severity"/> <c>finding</c> (the port cannot do what the data ask) or <c>info</c> (the data offer something nobody uses).</summary>
public sealed record RosterFinding(string Faction, string Severity, string Subject, string Finding)
{
    public string Key => $"{Faction}|{Subject}";
}

/// <summary>The static half of the faction audit (#411): what a <see cref="FactionRoster"/> shows without a world.</summary>
public static class FactionRosterFindings
{
    /// <summary>What the data alone show: references that lead nowhere, objects the simulation cannot make, upgrades and sciences nothing answers, powers nobody carries or presses.</summary>
    public static IEnumerable<RosterFinding> Of(FactionRoster roster, TemplateCatalog catalog, CommandButtonCatalog buttons, CompiledDefinitionGraph graph)
    {
        var faction = roster.Faction;
        foreach (var entry in roster.Objects)
        {
            if (catalog.TryGet(entry.Name, out var template) && !template.IsInstantiable)
                yield return new RosterFinding(faction, "finding", $"{entry.Name}", $"not instantiable: missing modules {string.Join(" ", template.MissingModules)}");
            // A command set the object names that the command bar does not know: the object shows no buttons of it.
            if (template is not null)
                foreach (var set in template.CommandSets.Where(value => value.Length > 0 && !value.Equals("NONE", StringComparison.OrdinalIgnoreCase) && !buttons.TryGetSet(value, out _)))
                    yield return new RosterFinding(faction, "finding", $"{entry.Name}", $"command set '{set}' is not a CommandSet: the object has no buttons of it");
            foreach (var button in entry.Buttons.Select(value => value.Button).DistinctBy(value => value.Name))
            {
                if (button.Object.Length > 0 && button.Kind is GuiCommandKind.UnitBuild or GuiCommandKind.DozerConstruct && !catalog.TryGet(button.Object, out _))
                    yield return new RosterFinding(faction, "finding", $"{entry.Name}:{button.Name}", $"unknown object {button.Object}");
                if (Named(button.Upgrade) && !catalog.Upgrades.TryGet(button.Upgrade, out _))
                    yield return new RosterFinding(faction, "finding", $"{entry.Name}:{button.Name}", $"unknown upgrade {button.Upgrade}");
                if (Named(button.SpecialPower) && !catalog.SpecialPowers.TryGet(button.SpecialPower, out _))
                    yield return new RosterFinding(faction, "finding", $"{entry.Name}:{button.Name}", $"unknown special power {button.SpecialPower}");
                if (button.SpecialPower.Length > 0 && button.Kind is GuiCommandKind.SpecialPower or GuiCommandKind.SpecialPowerConstruct
                    && roster.Powers.FirstOrDefault(value => value.Name.Equals(button.SpecialPower, StringComparison.OrdinalIgnoreCase)) is { } power
                    && !power.Carriers.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                    yield return new RosterFinding(faction, "finding", $"{entry.Name}:{button.Name}", $"the button's power {button.SpecialPower} has no module on the object (carried by {string.Join(" ", power.Carriers)})");
            }
        }
        foreach (var upgrade in roster.Upgrades.Where(value => value.Modules.IsEmpty))
            yield return new RosterFinding(faction, "finding", $"(upgrade {upgrade.Name})", $"no module of what the faction has or makes reads it ({string.Join(" ", upgrade.Buttons)})");
        foreach (var power in roster.Powers)
        {
            if (power.Carriers.IsEmpty) yield return new RosterFinding(faction, "info", $"(power {power.Name})", $"no roster object carries it ({string.Join(" ", power.Buttons)})");
            if (power.Buttons.IsEmpty) yield return new RosterFinding(faction, "info", $"(power {power.Name})", $"no button of the faction uses it (carried by {string.Join(" ", power.Carriers)})");
        }
        var rosterNames = roster.Objects.Select(value => value.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var science in roster.Sciences)
        {
            var users = ScienceUsers(science.Name, roster, catalog, graph, rosterNames);
            if (users.Count == 0) yield return new RosterFinding(faction, "finding", $"(science {science.Name})", $"nothing of the faction needs it: no power, prerequisite, button or module names it");
        }
        foreach (var button in roster.ScienceButtons.Select(value => value.Button).Concat(roster.ShortcutButtons.Select(value => value.Button)))
            foreach (var science in button.Sciences)
                if (!catalog.Sciences.TryGet(science, out _)) yield return new RosterFinding(faction, "finding", $"{button.Name}", $"unknown science {science}");
    }

    /// <summary>What of the faction names the science: a power it unlocks, a science after it, an object's prerequisite, a button, a module field of a roster object.</summary>
    public static List<string> ScienceUsers(string science, FactionRoster roster, TemplateCatalog catalog, CompiledDefinitionGraph graph, HashSet<string> objects)
    {
        var users = new List<string>();
        users.AddRange(roster.Sciences.Where(value => value.Name.Equals(science, StringComparison.OrdinalIgnoreCase)).SelectMany(value => value.Powers));
        users.AddRange(roster.Sciences.Where(value => value.Prerequisites.Contains(science, StringComparer.OrdinalIgnoreCase)).Select(value => value.Name));
        foreach (var name in objects)
            if (catalog.TryGet(name, out var template) && template.Prerequisites.Any(line => line.IsScience && line.Alternatives.Contains(science, StringComparer.OrdinalIgnoreCase)))
                users.Add(name);
        foreach (var entry in roster.Objects)
            users.AddRange(entry.Buttons.Where(value => value.Button.Kind != GuiCommandKind.PurchaseScience && value.Button.Sciences.Contains(science, StringComparer.OrdinalIgnoreCase)).Select(value => value.Button.Name));
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>().Where(value => objects.Contains(value.OriginalName)))
            foreach (var module in definition.Modules)
                if (module.Fields.Any(field => field.Value.OriginalText.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Contains(science, StringComparer.OrdinalIgnoreCase)))
                    users.Add($"{definition.OriginalName}/{module.Name}");
        return users;
    }

    private static bool Named(string value) => value.Length > 0 && !value.Equals("None", StringComparison.OrdinalIgnoreCase);
}

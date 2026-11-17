using System.Diagnostics;
using System.Globalization;
using System.Text;
using CnCNew.Data.Definitions;
using CnCNew.Game.Diagnostics;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Templates;

namespace CnCNew.SimTool;

/// <summary>
/// <c>faction-audit</c> (#411): what each of the twelve playable factions can build and do, followed from its <c>PlayerTemplate</c> through the command sets (<see cref="FactionRoster"/>),
/// written per faction with a diff of each general against the base faction of its side, the findings of the data (references that lead nowhere, objects that cannot be made, upgrades no
/// module answers, powers nobody carries), and with <c>--dynamic</c> every button pressed in a world of its own (<see cref="FactionProbe"/>).
/// </summary>
internal static class FactionAudit
{
    public const string Usage = "faction-audit <content-dir> --out <dir> [--faction NAME[,NAME..]] [--object TEXT] [--dynamic]";

    public static int Run(string[] args, Func<string, (CompiledDefinitionGraph Graph, TemplateCatalog Catalog, string Fingerprint)> compile)
    {
        var directory = args[1];
        var output = Option(args, "--out") ?? throw new ArgumentException("--out <dir> is required.");
        var factions = Option(args, "--faction")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [..FactionRoster.PlayableFactions];
        var objectFilter = Option(args, "--object");
        var dynamic = args.Contains("--dynamic");
        Directory.CreateDirectory(output);

        var clock = Stopwatch.StartNew();
        var (graph, catalog, fingerprint) = compile(directory);
        var buttons = CommandButtonCatalog.Build(graph);
        var probe = new FactionProbe(graph, catalog, buttons, fingerprint);
        var rosters = FactionRoster.PlayableFactions.Select(probe.RosterOf).ToDictionary(roster => roster.Faction, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"data loaded and rosters built in {clock.Elapsed.TotalSeconds:F1} s");

        var summary = new StringBuilder();
        summary.Append("# Faction audit\n\n| faction | objects | structures | units | buttons | upgrades | sciences | powers | science buttons | shortcut buttons |\n|---|---|---|---|---|---|---|---|---|---|\n");
        var findings = new List<string>();
        foreach (var name in factions)
        {
            var roster = rosters.TryGetValue(name, out var known) ? known : probe.RosterOf(name);
            Write(Path.Combine(output, $"roster-{roster.Faction}.tsv"), RosterLines(roster));
            summary.Append(Invariant($"| {roster.Faction} | {roster.Objects.Length} | {roster.Objects.Count(value => value.Structure)} | {roster.Objects.Count(value => !value.Structure)} | {roster.Objects.Sum(value => value.Buttons.Length)} | {roster.Upgrades.Length} | {roster.Sciences.Length} | {roster.Powers.Length} | {roster.ScienceButtons.Length} | {roster.ShortcutButtons.Length} |\n"));
            findings.AddRange(FactionRosterFindings.Of(roster, catalog, buttons, graph).Select(item => $"{item.Faction}\t{item.Severity}\t{item.Subject}\t{item.Finding}"));
        }
        summary.Append("\n## Generals against their base faction\n\n");
        foreach (var name in factions)
        {
            var roster = rosters[name];
            if (roster.BaseFaction.Equals(roster.Faction, StringComparison.OrdinalIgnoreCase)) continue;
            summary.Append(Diff(roster, rosters[roster.BaseFaction], catalog));
        }
        Write(Path.Combine(output, "summary.md"), [summary.ToString().TrimEnd('\n')]);
        Write(Path.Combine(output, "static-findings.tsv"), ["faction\tseverity\tsubject\tfinding", ..findings]);
        Console.WriteLine($"static: {findings.Count} findings; {output}");

        if (!dynamic) return 0;
        var lines = new List<string> { "faction\tobject\tset\tslot\tbutton\tkind\toutcome\tseconds\tdetail" };
        var counts = new SortedDictionary<string, (int Passed, int Failed, int NotApplicable)>(StringComparer.Ordinal);
        foreach (var name in factions)
        {
            var started = clock.Elapsed;
            foreach (var result in probe.Probe(rosters[name], objectFilter is null ? null : value => value.Contains(objectFilter, StringComparison.OrdinalIgnoreCase)))
            {
                lines.Add(Invariant($"{result.Faction}\t{result.Object}\t{result.CommandSet}\t{result.Slot}\t{result.Button}\t{result.Kind}\t{result.Outcome}\t{result.Seconds:F1}\t{result.Detail}"));
                var (passed, failed, notApplicable) = counts.GetValueOrDefault(name);
                counts[name] = result.Outcome switch
                {
                    ProbeOutcome.Passed => (passed + 1, failed, notApplicable),
                    ProbeOutcome.Failed => (passed, failed + 1, notApplicable),
                    _ => (passed, failed, notApplicable + 1),
                };
                if (result.Outcome == ProbeOutcome.Failed) Console.WriteLine($"FAIL {result.Key}: {result.Detail}");
            }
            var count = counts.GetValueOrDefault(name);
            Console.WriteLine($"{name}: {count.Passed} passed, {count.Failed} failed, {count.NotApplicable} not applicable ({(clock.Elapsed - started).TotalSeconds:F0} s)");
        }
        Write(Path.Combine(output, "dynamic.tsv"), lines);
        Console.WriteLine($"dynamic: {probe.Worlds} worlds, {probe.SecondsStepped:F0} s of game time stepped, {clock.Elapsed.TotalSeconds:F0} s in all");
        return 0;
    }

    private static IEnumerable<string> RosterLines(FactionRoster roster)
    {
        yield return "object\torigin\tstructure\tset\tslot\tbutton\tkind\toptions\ttarget";
        foreach (var entry in roster.Objects)
        {
            if (entry.Buttons.IsEmpty) yield return $"{entry.Name}\t{entry.Origin}\t{(entry.Structure ? "yes" : "no")}\t\t\t\t\t\t";
            foreach (var button in entry.Buttons) yield return $"{entry.Name}\t{entry.Origin}\t{(entry.Structure ? "yes" : "no")}\t{ButtonColumns(button)}";
        }
        foreach (var button in roster.ScienceButtons) yield return $"(general's window)\t\t\t{ButtonColumns(button)}";
        foreach (var button in roster.ShortcutButtons) yield return $"(power shortcuts)\t\t\t{ButtonColumns(button)}";
        foreach (var science in roster.Sciences)
            yield return Invariant($"(science {science.Name})\trank {science.Rank}\t\t\t\t\t\tcost {science.Cost}; after {string.Join(" ", science.Prerequisites)}\tpowers {string.Join(" ", science.Powers)}");
        foreach (var upgrade in roster.Upgrades)
            yield return $"(upgrade {upgrade.Name})\t{upgrade.Scope}\t\t\t\t{string.Join(" ", upgrade.Buttons)}\t\t\tmodules {string.Join(" ", upgrade.Modules)}";
        foreach (var power in roster.Powers)
            yield return $"(power {power.Name})\t{power.Type}\t\t\t\t{string.Join(" ", power.Buttons)}\t\t{power.RequiredScience}\tcarriers {string.Join(" ", power.Carriers)}";
    }

    private static string ButtonColumns(RosterButton entry)
    {
        var button = entry.Button;
        var target = button.Kind switch
        {
            GuiCommandKind.DozerConstruct or GuiCommandKind.UnitBuild or GuiCommandKind.SpecialPowerConstruct or GuiCommandKind.SpecialPowerConstructFromShortcut => button.Object,
            GuiCommandKind.PlayerUpgrade or GuiCommandKind.ObjectUpgrade => button.Upgrade,
            GuiCommandKind.PurchaseScience => string.Join(" ", button.Sciences),
            GuiCommandKind.FireWeapon or GuiCommandKind.SwitchWeapon => button.WeaponSlot,
            _ => button.SpecialPower,
        };
        return Invariant($"{entry.CommandSet}\t{entry.Slot}\t{button.Name}\t{button.Kind}\t{button.Options}\t{target}");
    }

    /// <summary>A general against the base faction of its side: objects, buttons, upgrades, sciences and powers only one of them has, names compared without the general's prefix.</summary>
    private static string Diff(FactionRoster general, FactionRoster baseFaction, TemplateCatalog catalog)
    {
        var prefix = Prefix(general, baseFaction, catalog);
        string Plain(string name) => prefix.Length > 0 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name[prefix.Length..] : name;
        var text = new StringBuilder();
        text.Append(Invariant($"### {general.Faction} against {baseFaction.Faction} (prefix `{prefix}`)\n\n"));
        void Section(string title, IEnumerable<string> mine, IEnumerable<string> theirs)
        {
            var a = mine.Select(Plain).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var b = theirs.Select(Plain).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = a.Except(b, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
            var removed = b.Except(a, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
            if (added.Count == 0 && removed.Count == 0) return;
            text.Append($"- {title}: only the general {(added.Count == 0 ? "-" : string.Join(", ", added))}; only the base {(removed.Count == 0 ? "-" : string.Join(", ", removed))}\n");
        }
        Section("objects", general.Objects.Select(value => value.Name), baseFaction.Objects.Select(value => value.Name));
        Section("upgrades", general.Upgrades.Select(value => value.Name), baseFaction.Upgrades.Select(value => value.Name));
        Section("sciences", general.Sciences.Select(value => value.Name), baseFaction.Sciences.Select(value => value.Name));
        Section("powers", general.Powers.Select(value => value.Name), baseFaction.Powers.Select(value => value.Name));
        foreach (var entry in general.Objects)
        {
            var other = baseFaction.Find(Plain(entry.Name));
            if (other is null) continue;
            Section($"buttons of {entry.Name}", entry.Buttons.Select(value => value.Button.Name), other.Buttons.Select(value => value.Button.Name));
        }
        text.Append('\n');
        return text.ToString();
    }

    /// <summary>The general's name prefix: what its starting building has before the base faction's (<c>AirF_</c> of <c>AirF_AmericaCommandCenter</c>).</summary>
    private static string Prefix(FactionRoster general, FactionRoster baseFaction, TemplateCatalog catalog)
    {
        var mine = catalog.Factions.TryGet(general.Faction, out var a) ? a.StartingBuilding : string.Empty;
        var theirs = catalog.Factions.TryGet(baseFaction.Faction, out var b) ? b.StartingBuilding : string.Empty;
        return theirs.Length > 0 && mine.EndsWith(theirs, StringComparison.OrdinalIgnoreCase) ? mine[..^theirs.Length] : string.Empty;
    }

    private static void Write(string path, IEnumerable<string> lines) => File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string? Option(string[] args, string name) => Array.IndexOf(args, name) is var index and >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

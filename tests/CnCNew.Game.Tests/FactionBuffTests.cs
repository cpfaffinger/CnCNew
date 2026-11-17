using System.Diagnostics;
using CnCNew.Game.Data;
using CnCNew.Game.Diagnostics;
using CnCNew.Presentation.Input;

namespace CnCNew.Game.Tests;

/// <summary>
/// #419, the owner's question whether every general's buffs and special functions are properly implemented: per playable faction every buff the data name - each module an upgrade of
/// the faction switches on, the horde and its nationalism and fanaticism, a collector's supply boost, the veterancy a science gives at birth, a general's own price and build time -
/// measured where it acts, in a control world without it and a test world with it on real data (<see cref="FactionBuffProbe"/>), against what the data name and the original's code
/// does. A failed measurement is either a named known failure below - a finding, each with what the original does - or a failure of the suite; a known failure that passes fails the
/// suite too. <c>CNCNEW_BUFF_FACTIONS=A,B</c> measures only those factions (and does not judge the list), <c>CNCNEW_BUFF_ALL=1</c> prints every result.
/// </summary>
internal static class FactionBuffTests
{
    /// <summary>The measurements that fail today (<see cref="BuffResult.Key"/>), each with its finding.</summary>
    private static readonly Dictionary<string, string> KnownFailures = new(StringComparer.Ordinal)
    {
        // AirF_AmericaVehicleChinook has two AI modules; the original keeps the last (ChinookAIUpdate) as m_ai, the port's AiOf the first (FactionAuditTests' combat drop): the Chinook
        // never gathers, so its supply lines boost nothing.
        ["FactionAmericaAirForceGeneral|Upgrade_AmericaSupplyLines|supply-boost|AirF_AmericaVehicleChinook|ChinookAIUpdate:ModuleTag_07"] = "the first of two AI modules wins",

        // ThingFactory::newObject makes one of a template's BuildVariations (ThingFactory.cpp): a produced Technical is ChassisOne, Two or Three, each trainable with its own
        // VeterancyGainCreate. The port reads no BuildVariations: it makes the bare, untrainable GLAVehicleTechnical, and Technical Training gives it nothing.
        ["FactionGLA|SCIENCE_TechnicalTraining|veterancy-at-birth|GLAVehicleTechnical|VeterancyGainCreate:ModuleTag_03"] = "BuildVariations are not read",
        ["FactionGLAToxinGeneral|SCIENCE_TechnicalTraining|veterancy-at-birth|Chem_GLAVehicleTechnical|VeterancyGainCreate:ModuleTag_03"] = "BuildVariations are not read",
        ["FactionGLADemolitionGeneral|SCIENCE_TechnicalTraining|veterancy-at-birth|Demo_GLAVehicleTechnical|VeterancyGainCreate:ModuleTag_03"] = "BuildVariations are not read",
        ["FactionGLAStealthGeneral|SCIENCE_TechnicalTraining|veterancy-at-birth|Slth_GLAVehicleTechnical|VeterancyGainCreate:ModuleTag_03"] = "BuildVariations are not read",

        // BattleBusSlowDeathBehavior wakes its empty-hulk check when the bus is made; the original's SlowDeathBehavior constructor sleeps forever and only beginSlowDeath wakes the
        // module (BattleBusSlowDeathBehavior.cpp), so only the hulk of a second life is killed when empty. In the port an empty Battle Bus kills itself about 1.5 s after it appears.
        ["FactionGLA|Upgrade_GLAJunkRepair|auto-heal|GLAVehicleBattleBus|AutoHealBehavior:ModuleTag_06"] = "an empty Battle Bus dies",
        ["FactionGLAToxinGeneral|Upgrade_GLAJunkRepair|auto-heal|Chem_GLAVehicleBattleBus|AutoHealBehavior:ModuleTag_06"] = "an empty Battle Bus dies",
        ["FactionGLADemolitionGeneral|Upgrade_GLAJunkRepair|auto-heal|Demo_GLAVehicleBattleBus|AutoHealBehavior:ModuleTag_06"] = "an empty Battle Bus dies",
        ["FactionGLAStealthGeneral|Upgrade_GLAJunkRepair|auto-heal|Slth_GLAVehicleBattleBus|AutoHealBehavior:ModuleTag_06"] = "an empty Battle Bus dies",

        // RiderChangeContain::onRemoving returns before it clears the rider's status when the bike is dead (RiderChangeContain.cpp), so the bike's death modules still see
        // STATUS_RIDER5 of a terrorist; the port's RiderChangeContain.Remove clears the status first, and a terrorist's bike dies with the death weapons of a riderless one.
        ["FactionGLAToxinGeneral|Chem_Upgrade_GLAAnthraxGamma|death-weapon|Chem_GLAVehicleCombatBike|FireWeaponWhenDeadBehavior:ModuleTag91"] = "a dead bike forgets its rider",
        ["FactionGLADemolitionGeneral|Demo_Upgrade_SuicideBomb|death-weapon|Demo_GLAVehicleCombatBike|FireWeaponWhenDeadBehavior:ModuleTag996"] = "a dead bike forgets its rider",
        ["FactionGLADemolitionGeneral|Demo_Upgrade_SuicideBomb|death-weapon|Demo_GLAVehicleCombatBike|FireWeaponWhenDeadBehavior:ModuleTag997"] = "a dead bike forgets its rider",
        ["FactionGLADemolitionGeneral|Demo_Upgrade_SuicideBomb|death-weapon|Demo_GLAVehicleCombatBike|FireWeaponWhenDeadBehavior:ModuleTag_CrushedDeath_3"] = "a dead bike forgets its rider",
    };

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(FactionBuffTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP FactionBuffTests with real data (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        var buttons = CommandButtonCatalog.Build(data.Graph);
        var rosters = new FactionProbe(data.Graph, data.Templates, buttons, data.Fingerprint);
        var probe = new FactionBuffProbe(data.Graph, data.Templates, data.Fingerprint, rosters.RosterOf);
        var only = Environment.GetEnvironmentVariable("CNCNEW_BUFF_FACTIONS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        harness.Run("Every faction's buffs are derived from its data: upgrades, sciences, the horde, a general's production", () => Cases(rosters, probe));
        harness.Run("Every buff of every playable faction acts as the data name it, but for the named findings", () => Measured(rosters, probe, only));
        return harness.Failures;
    }

    private static void Cases(FactionProbe rosters, FactionBuffProbe probe)
    {
        foreach (var name in CnCNew.Game.Diagnostics.FactionRoster.PlayableFactions)
        {
            var roster = rosters.RosterOf(name);
            var cases = probe.CasesOf(roster);
            var effects = cases.GroupBy(value => value.Effect).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} {group.Count()}");
            Console.WriteLine($"       {name}: {cases.Length} buffs ({string.Join(", ", effects)}); not measured: {string.Join(" ", probe.Unmeasured(roster))}");
            TestHarness.Assert(cases.Length >= 20, $"{name}: {cases.Length} buffs: the derivation lost them");
            TestHarness.Assert(cases.Any(value => value.Effect == "armor" || value.Effect == "max-health"), $"{name}: no armour or health upgrade");
            TestHarness.Assert(cases.Any(value => value.Effect is "damage" or "weapon-set"), $"{name}: no weapon upgrade");
        }
    }

    private static void Measured(FactionProbe rosters, FactionBuffProbe probe, string[]? only)
    {
        var clock = Stopwatch.StartNew();
        var all = Environment.GetEnvironmentVariable("CNCNEW_BUFF_ALL") == "1";
        var results = new List<BuffResult>();
        foreach (var name in CnCNew.Game.Diagnostics.FactionRoster.PlayableFactions)
        {
            if (only is not null && !only.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            var roster = rosters.RosterOf(name);
            foreach (var item in probe.CasesOf(roster))
            {
                var result = probe.Measure(roster, item);
                results.Add(result);
                if (all || result.Outcome != BuffOutcome.Passed)
                    Console.WriteLine($"       {(result.Outcome == BuffOutcome.Failed ? KnownFailures.ContainsKey(result.Key) ? "known" : "NEW" : result.Outcome.ToString())} {result.Key}: "
                                      + $"expected {result.Expected}; measured {result.Measured}; {result.Detail}");
            }
        }
        var failed = results.Where(result => result.Outcome == BuffOutcome.Failed).ToList();
        Console.WriteLine($"       {results.Count} buffs: {results.Count(result => result.Outcome == BuffOutcome.Passed)} passed, {failed.Count} failed, "
                          + $"{results.Count(result => result.Outcome == BuffOutcome.NotMeasured)} not measured; {probe.Worlds} worlds, {probe.SecondsStepped:F0} s of game time, {clock.Elapsed.TotalSeconds:F0} s");
        var unknown = failed.Where(result => !KnownFailures.ContainsKey(result.Key)).Select(result => $"{result.Key}: expected {result.Expected}; measured {result.Measured}").ToList();
        TestHarness.Assert(unknown.Count == 0, $"{unknown.Count} new failures: {string.Join(" | ", unknown)}");
        if (only is not null) return;
        TestHarness.Assert(results.Count >= 600, $"{results.Count} buffs measured: the derivation lost them");
        var passing = KnownFailures.Keys.Where(key => results.Any(result => result.Key == key && result.Outcome != BuffOutcome.Failed)).ToList();
        var missing = KnownFailures.Keys.Where(key => results.All(result => result.Key != key)).ToList();
        TestHarness.Assert(passing.Count == 0, $"known failures that pass now (take them off the list): {string.Join("; ", passing)}");
        TestHarness.Assert(missing.Count == 0, $"known failures no measurement reaches any more: {string.Join("; ", missing)}");
    }
}

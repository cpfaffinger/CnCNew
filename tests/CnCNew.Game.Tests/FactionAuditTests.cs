using System.Diagnostics;
using CnCNew.Game.Data;
using CnCNew.Game.Diagnostics;
using CnCNew.Presentation.Input;

namespace CnCNew.Game.Tests;

/// <summary>
/// #411, the owner's question whether every general is right: what each of the twelve playable factions can build and do, followed from its <c>PlayerTemplate</c> through the command
/// sets (<see cref="FactionRoster"/>), and every button of it pressed the way the command bar presses it, in a world of its own on real data (<see cref="FactionProbe"/>). A press that
/// fails is either a named known failure below - a finding of the audit, each with what the original does - or a failure of the suite; a known failure that passes fails the suite
/// too, so the list follows the fixes. <c>SimTool faction-audit game/content --out DIR --dynamic</c> writes the same rosters and results as files.
/// </summary>
internal static class FactionAuditTests
{
    /// <summary>The presses that fail today (<see cref="ProbeResult.Key"/>), each with its finding.</summary>
    private static readonly Dictionary<string, string> KnownFailures = new(StringComparer.Ordinal)
    {
        // The GLA worker's switch between its real and its fake buildings: CommandSetUpgrade ModuleTag_14/15 remove the other switch's upgrade. UpgradeMux::giveSelfUpgrade removes first and
        // Object::removeUpgrade (Object.cpp) only clears the bit and re-arms the modules it triggered; SimulationWorld.RemoveUpgradeByName runs every upgrade module of the object again
        // (ReevaluateUpgrades) before the module counts as executed, and the module removes again: the stack overflows and the game ends. Not pressed (FactionProbe.PressRecursingUpgrades).
        ["FactionGLA|GLAInfantryWorker|Command_UpgradeGLAWorkerFakeCommandSet"] = "worker command set switch recurses",
        ["FactionGLA|GLAInfantryWorker|Command_UpgradeGLAWorkerRealCommandSet"] = "worker command set switch recurses",
        ["FactionGLAToxinGeneral|Chem_GLAInfantryWorker|Command_UpgradeGLAWorkerFakeCommandSet"] = "worker command set switch recurses",
        ["FactionGLAToxinGeneral|Chem_GLAInfantryWorker|Command_UpgradeGLAWorkerRealCommandSet"] = "worker command set switch recurses",
        ["FactionGLADemolitionGeneral|Demo_GLAInfantryWorker|Command_UpgradeGLAWorkerFakeCommandSet"] = "worker command set switch recurses",
        ["FactionGLADemolitionGeneral|Demo_GLAInfantryWorker|Command_UpgradeGLAWorkerRealCommandSet"] = "worker command set switch recurses",
        ["FactionGLAStealthGeneral|Slth_GLAInfantryWorker|Command_UpgradeGLAWorkerFakeCommandSet"] = "worker command set switch recurses",
        ["FactionGLAStealthGeneral|Slth_GLAInfantryWorker|Command_UpgradeGLAWorkerRealCommandSet"] = "worker command set switch recurses",

        // Colonel Burton's knife (FIRE_WEAPON, SECONDARY ColonelBurtonKnifeWeapon, AttackRange 3): ordered at a soldier that stands still he walks up and circles it at some
        // 20 units without ever stabbing; at a vehicle or a building the order is refused (InvalidWeaponTarget), as the knife's data allow.
        ["FactionAmerica|AmericaInfantryColonelBurton|Command_ColonelBurtonKnifeAttack"] = "Burton circles his knife's target",
        ["FactionAmericaAirForceGeneral|AirF_AmericaInfantryColonelBurton|Command_ColonelBurtonKnifeAttack"] = "Burton circles his knife's target",
        ["FactionAmericaLaserGeneral|Lazr_AmericaInfantryColonelBurton|Command_ColonelBurtonKnifeAttack"] = "Burton circles his knife's target",
        ["FactionAmericaSuperWeaponGeneral|SupW_AmericaInfantryColonelBurton|Command_ColonelBurtonKnifeAttack"] = "Burton circles his knife's target",

        // The unload button of a Chinook in the air: the original's MSG_EVACUATE (AIGroup::groupEvacuate) is AICMD_EVACUATE to the Chinook, and ChinookAIUpdate::aiDoCommand lands
        // first ("gotta land first!"). CommandModeController sends an ExitContainerCommand per rider instead, which nobody acts on while the Chinook hovers: nobody gets out.
        ["FactionAmerica|AmericaVehicleChinook|Command_ChinookUnload"] = "hovering Chinook does not unload",
        ["FactionAmericaAirForceGeneral|AFG_AmericaVehicleChinook|Command_ChinookUnload"] = "hovering Chinook does not unload",
        ["FactionAmericaLaserGeneral|Lazr_AmericaVehicleChinook|Command_ChinookUnload"] = "hovering Chinook does not unload",
        ["FactionAmericaSuperWeaponGeneral|SupW_AmericaVehicleChinook|Command_ChinookUnload"] = "hovering Chinook does not unload",

        // AirF_AmericaVehicleChinook has two AI modules (AIUpdateInterface ModuleTag_04 and ChinookAIUpdate ModuleTag_07). Object::Object keeps the last as m_ai (Object.cpp, the
        // assertion against two is debug-only); the port's AiOf takes the first, so the Chinook's own AI is never asked: no rappellers, the combat drop is greyed.
        ["FactionAmericaAirForceGeneral|AirF_AmericaVehicleChinook|Command_CombatDrop"] = "the first of two AI modules wins",

        // The sneak attack tunnel's rally point button: ControlBar::getCommandAvailability has no producer check for GUI_COMMAND_SET_RALLY_POINT, and doSetRallyPoint sets it on the
        // object's exit interface (TunnelContain is one). The port's bar restricts it without a ProductionUpdate (ButtonReason.NoProducer).
        ["FactionGLA|GLASneakAttackTunnelNetwork|Command_SetRallyPoint"] = "rally point greyed without a production module",
        ["FactionGLAToxinGeneral|GLASneakAttackTunnelNetwork|Command_SetRallyPoint"] = "rally point greyed without a production module",
        ["FactionGLADemolitionGeneral|GLASneakAttackTunnelNetwork|Command_SetRallyPoint"] = "rally point greyed without a production module",
        ["FactionGLAStealthGeneral|GLASneakAttackTunnelNetwork|Command_SetRallyPoint"] = "rally point greyed without a production module",
    };

    /// <summary>
    /// The static findings of severity <c>finding</c> (<see cref="RosterFinding.Key"/>): GLADemoTrap names its set as <c>CommandSet = = GLADemoTrapCommandSet</c>. The original's
    /// <c>INI::parseAsciiString</c> reads the next token with the separators <c>" \n\r\t="</c> (INI.cpp), so the set is GLADemoTrapCommandSet and the trap has its detonate and
    /// mode buttons; the port keeps "= GLADemoTrapCommandSet", which names no set, and the base GLA's demo trap has no buttons.
    /// </summary>
    private static readonly Dictionary<string, string> KnownStaticFindings = new(StringComparer.Ordinal)
    {
        ["FactionGLA|GLADemoTrap"] = "CommandSet = = GLADemoTrapCommandSet read with the '='",
    };

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(FactionAuditTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP FactionAuditTests with real data (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        var buttons = CommandButtonCatalog.Build(data.Graph);
        var probe = new FactionProbe(data.Graph, data.Templates, buttons, data.Fingerprint);
        harness.Run("Each playable faction starts with its command center and a builder, and has a general's window and a power shortcut bar", () => Rosters(data, probe));
        harness.Run("What the data show without a world is right, but for the named findings", () => Static(data, buttons, probe));
        harness.Run("Every button of every playable faction does what it is for, but for the named findings", () => Dynamic(probe));
        return harness.Failures;
    }

    private static void Rosters(GameData data, FactionProbe probe)
    {
        foreach (var name in FactionRoster.PlayableFactions)
        {
            var roster = probe.RosterOf(name);
            TestHarness.Assert(data.Templates.Factions.TryGet(name, out var faction), $"{name} is a faction");
            TestHarness.Assert(roster.Find(faction.StartingBuilding) is { Origin: "start" }, $"{name}: its starting building {faction.StartingBuilding} is in the roster");
            TestHarness.Assert(roster.Objects.Any(entry => entry.Buttons.Any(button => button.Button.Kind == GuiCommandKind.DozerConstruct)), $"{name}: something of it builds");
            TestHarness.Assert(roster.Objects.Count(entry => entry.Structure) >= 10 && roster.Objects.Count(entry => !entry.Structure) >= 15,
                $"{name}: {roster.Objects.Count(entry => entry.Structure)} structures and {roster.Objects.Count(entry => !entry.Structure)} units");
            TestHarness.Assert(roster.ScienceButtons.Length >= 10 && roster.Sciences.Length >= 10, $"{name}: {roster.ScienceButtons.Length} buttons and {roster.Sciences.Length} sciences in the general's window");
            TestHarness.Assert(roster.ShortcutButtons.Length >= 5, $"{name}: {roster.ShortcutButtons.Length} power shortcuts");
            Console.WriteLine($"       {name}: {roster.Objects.Length} objects ({roster.Objects.Count(entry => entry.Structure)} structures), {roster.Objects.Sum(entry => entry.Buttons.Length)} buttons, "
                              + $"{roster.Upgrades.Length} upgrades, {roster.Sciences.Length} sciences, {roster.Powers.Length} powers");
        }
    }

    private static void Static(GameData data, CommandButtonCatalog buttons, FactionProbe probe)
    {
        var found = FactionRoster.PlayableFactions.SelectMany(name => FactionRosterFindings.Of(probe.RosterOf(name), data.Templates, buttons, data.Graph))
            .Where(item => item.Severity == "finding").ToList();
        foreach (var item in found) Console.WriteLine($"       {(KnownStaticFindings.ContainsKey(item.Key) ? "known" : "NEW")} {item.Key}: {item.Finding}");
        var unknown = found.Where(item => !KnownStaticFindings.ContainsKey(item.Key)).Select(item => $"{item.Key}: {item.Finding}").ToList();
        var gone = KnownStaticFindings.Keys.Where(key => found.All(item => item.Key != key)).ToList();
        TestHarness.Assert(unknown.Count == 0, $"new static findings: {string.Join("; ", unknown)}");
        TestHarness.Assert(gone.Count == 0, $"known static findings that no longer show (take them off the list): {string.Join("; ", gone)}");
    }

    private static void Dynamic(FactionProbe probe)
    {
        var clock = Stopwatch.StartNew();
        var results = FactionRoster.PlayableFactions.SelectMany(name => probe.Probe(probe.RosterOf(name))).ToList();
        var failed = results.Where(result => result.Outcome == ProbeOutcome.Failed).ToList();
        foreach (var result in failed)
            Console.WriteLine($"       {(KnownFailures.ContainsKey(result.Key) ? "known" : "NEW")} {result.Key}: {result.Detail}");
        Console.WriteLine($"       {results.Count} presses: {results.Count(result => result.Outcome == ProbeOutcome.Passed)} passed, {failed.Count} failed, "
                          + $"{results.Count(result => result.Outcome == ProbeOutcome.NotApplicable)} not applicable; {probe.Worlds} worlds, {probe.SecondsStepped:F0} s of game time, "
                          + $"{clock.Elapsed.TotalSeconds:F0} s");
        TestHarness.Assert(results.Count >= 2000, $"{results.Count} presses: the rosters lost buttons");
        var unknown = failed.Where(result => !KnownFailures.ContainsKey(result.Key)).Select(result => $"{result.Key}: {result.Detail}").ToList();
        var passing = KnownFailures.Keys.Where(key => results.Any(result => result.Key == key && result.Outcome != ProbeOutcome.Failed)).ToList();
        var missing = KnownFailures.Keys.Where(key => results.All(result => result.Key != key)).ToList();
        TestHarness.Assert(unknown.Count == 0, $"{unknown.Count} new failures: {string.Join(" | ", unknown)}");
        TestHarness.Assert(passing.Count == 0, $"known failures that pass now (take them off the list): {string.Join("; ", passing)}");
        TestHarness.Assert(missing.Count == 0, $"known failures no press reaches any more: {string.Join("; ", missing)}");
    }
}

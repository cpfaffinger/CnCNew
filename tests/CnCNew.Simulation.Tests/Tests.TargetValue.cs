using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// The owner's decision (docs/SIMULATION-CORE.md §87): a unit that chooses its own victim takes the enemy it can hurt most and that threatens it most, not the closest.
// Real data throughout - a stand-in weapon proves nothing about the armour table and the weapons of the shipped units.
static partial class Tests
{
    private sealed class RealTargets
    {
        private readonly CnCNew.Data.Definitions.CompiledDefinitionGraph _graph;
        private readonly string _fingerprint;
        private readonly Dictionary<(SimulationRate, bool), TemplateCatalog> _catalogs = [];

        public RealTargets(string directory)
        {
            var content = ContentFileSystem.Open(directory);
            var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
            _graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
            (_fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(_graph);
        }

        /// <summary>A world on flat ground with two players under the given rules (the catalogs are built once per rate and profile).</summary>
        public SimulationWorld WorldWith(SimulationRate rate, RuleProfile rules)
        {
            if (!_byRules.TryGetValue((rate, rules.Name), out var catalog))
                _byRules[(rate, rules.Name)] = catalog = TemplateCatalog.Build(_graph, ModuleBindingTable.Default, rate, rules: rules);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "rules-world");
            return new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-rules-world", _fingerprint, terrain.Fingerprint), catalog, terrain);
        }

        private readonly Dictionary<(SimulationRate, string), TemplateCatalog> _byRules = [];

        /// <summary>A world on flat ground with two players; <paramref name="withFix"/> false is the product rules without this one fix.</summary>
        public SimulationWorld World(SimulationRate rate, bool withFix, bool computer = false)
        {
            if (!_catalogs.TryGetValue((rate, withFix), out var catalog))
            {
                var rules = withFix ? RuleProfile.Product : RuleProfile.Parse(string.Join('+', RuleProfile.Catalogue.Where(info => info.Fix != GameFix.TargetByDamagePotential).Select(info => info.Name)) + "+movement-budget");
                _catalogs[(rate, withFix)] = catalog = TemplateCatalog.Build(_graph, ModuleBindingTable.Default, rate, rules: rules);
            }

            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East", IsComputer: computer), new PlayerConfiguration(new PlayerId(2), "West"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "target-value");
            return new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-target-value", _fingerprint, terrain.Fingerprint), catalog, terrain);
        }
    }

    private static ulong _sequence;

    /// <summary>Spawns all of them in one tick, so that none acts before the others stand there.</summary>
    private static Entity[] SpawnReal(SimulationWorld world, params (string Template, int Owner, int X, int Y)[] specs)
    {
        var before = world.Entities.Select(value => value.Id).ToHashSet();
        foreach (var spec in specs)
            Check.That(world.Submit(new SpawnCommand(new PlayerId(spec.Owner), ++_sequence, world.Tick, spec.Template, new PlayerId(spec.Owner), Fixed.FromInt(spec.X), Fixed.FromInt(spec.Y), Fixed.Zero, Fixed.Zero)).Accepted, spec.Template + " spawns");
        for (var step = 0; step < 8; step++)
        {
            world.Step();
            var made = world.Entities.Where(value => !before.Contains(value.Id)).OrderBy(value => value.Id.Value).ToArray();
            if (made.Length >= specs.Length) return made;
        }

        throw new InvalidOperationException("the spawned objects did not appear");
    }

    private enum TargetMode { Idle, Guard, Ordered }

    /// <summary>One look of a unit among enemies: the unit (player 1's), its enemies as (template, distance) and how it comes to look.</summary>
    private sealed record TargetScenario(string Looker, (string Template, int Distance)[] Enemies, bool Flying = false, TargetMode Mode = TargetMode.Idle, bool Computer = false, int OrderedIndex = -1);

    /// <summary>The template of the first victim the looker picks within five seconds (null for none) and the original frame it was picked in.</summary>
    private static (string? Victim, long Frame) RunTargetScenario(RealTargets real, SimulationRate rate, bool withFix, TargetScenario scenario)
    {
        var world = real.World(rate, withFix, scenario.Computer);
        var looker = SpawnReal(world, (scenario.Looker, 1, 400, 400))[0];
        if (scenario.Flying)
        {
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++_sequence, world.Tick, looker.Id, Fixed.FromInt(330), Fixed.FromInt(400))).Accepted, "The flyer is ordered up.");
            for (var step = 0; step < world.Frames(30 * 20) && (looker.HasMoveTarget || looker.Speed != Fixed.Zero || looker.Z < Fixed.FromInt(10)); step++) world.Step();
            Check.That(!looker.HasMoveTarget && looker.Z >= Fixed.FromInt(10), $"{rate}: the {scenario.Looker} hovers (height {looker.Z}).");
        }

        var (cx, cy) = ((int)looker.X.Floor(), (int)looker.Y.Floor());
        var spawns = scenario.Enemies.Select((enemy, index) => (enemy.Template, 2, cx + (index % 2 == 0 ? enemy.Distance : 0), cy + (index % 2 == 0 ? 0 : enemy.Distance))).ToArray();
        if (scenario.Mode == TargetMode.Guard)
            Check.That(world.Submit(new GuardCommand(new PlayerId(1), ++_sequence, world.Tick, [looker.Id], looker.X, looker.Y, EntityId.Invalid, GuardMode.Normal)).Accepted, "guard");
        var enemies = SpawnReal(world, spawns);
        if (scenario.Mode == TargetMode.Ordered)
        {
            // The enemies must be seen before an order can name them; the looker may have picked its own victim meanwhile - the order replaces it.
            for (var step = 0; step < world.Frames(30 * 2); step++) world.Step();
            var target = enemies.First(value => value.Template.Name == scenario.Enemies[scenario.OrderedIndex].Template);
            var result = world.Submit(new PursueCommand(new PlayerId(1), ++_sequence, world.Tick, looker.Id, target.Id, target.X, target.Y));
            Check.That(result.Accepted, "pursue: " + result);
            for (var step = 0; step < world.Frames(30 * 1); step++) world.Step();
        }

        for (var step = 0; step < world.Frames(30 * 5); step++)
        {
            if (world.AcquiredVictimOf(looker) is { IsValid: true } victim && world.FindEntity(victim) is { } found) return (found.Template.Name, world.Tick / rate.TicksPerOriginalFrame);
            world.Step();
        }

        return (null, 0);
    }

    private static readonly SimulationRate[] TargetRates = [SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120];

    /// <summary>The control (without the fix: the closest) and the fix (the expected one), at every rate; the victim is the same at every rate.</summary>
    private static void CheckTargetChoice(RealTargets real, string label, TargetScenario scenario, string closest, string expected)
    {
        long frame = -1;
        foreach (var rate in TargetRates)
        {
            var control = RunTargetScenario(real, rate, false, scenario);
            Check.That(control.Victim == closest, $"{label}, {rate}: without the fix the closest enemy is taken ({control.Victim}, expected {closest}).");
            var fix = RunTargetScenario(real, rate, true, scenario);
            Check.That(fix.Victim == expected, $"{label}, {rate}: with the fix {expected} is taken ({fix.Victim}).");
            if (frame < 0) frame = fix.Frame;
            // The spawn and the climb of a flyer start a tick off each other at the rates; the fix must not add a delay of its own, so the frame is that of the control.
            Check.That(fix.Frame == control.Frame && Math.Abs(fix.Frame - frame) <= 1, $"{label}, {rate}: the choice falls in the control's frame {control.Frame}, near {frame} at every rate ({fix.Frame}).");
        }
    }

    public static void RealTargetValueChoosesWhatItHurtsMost(string directory)
    {
        var real = new RealTargets(directory);
        // A Comanche hovering over a tank (near) and a Tank Hunter (far): the rocket trooper is the danger to it, the tank cannot even shoot at it.
        CheckTargetChoice(real, "Comanche, tank near, Tank Hunter far", new("AmericaVehicleComanche", [("ChinaTankBattleMaster", 70), ("ChinaInfantryTankHunter", 95)], Flying: true), "ChinaTankBattleMaster", "ChinaInfantryTankHunter");
        // A Missile Defender between a Red Guard (near) and a Battle Master (far): its rockets do their damage to the tank.
        CheckTargetChoice(real, "Missile Defender, Red Guard near, tank far", new("AmericaInfantryMissileDefender", [("ChinaInfantryRedguard", 70), ("ChinaTankBattleMaster", 95)]), "ChinaInfantryRedguard", "ChinaTankBattleMaster");
        // A Tank Hunter likewise.
        CheckTargetChoice(real, "Tank Hunter, Red Guard near, tank far", new("ChinaInfantryTankHunter", [("ChinaInfantryRedguard", 70), ("AmericaTankCrusader", 95)]), "ChinaInfantryRedguard", "AmericaTankCrusader");
        // A Crusader: the tank gun does a tenth of its damage to a man.
        CheckTargetChoice(real, "Crusader, Red Guard near, tank far", new("AmericaTankCrusader", [("ChinaInfantryRedguard", 70), ("ChinaTankBattleMaster", 95)]), "ChinaInfantryRedguard", "ChinaTankBattleMaster");
        // The same for a Crusader of a computer player (its victims it may walk to), and for a unit on guard.
        CheckTargetChoice(real, "computer Crusader", new("AmericaTankCrusader", [("ChinaInfantryRedguard", 70), ("ChinaTankBattleMaster", 95)], Computer: true), "ChinaInfantryRedguard", "ChinaTankBattleMaster");
        CheckTargetChoice(real, "guarding Crusader", new("AmericaTankCrusader", [("ChinaInfantryRedguard", 70), ("ChinaTankBattleMaster", 95)], Mode: TargetMode.Guard), "ChinaInfantryRedguard", "ChinaTankBattleMaster");
        // An armed man before a worker: a unit does not spend itself on what shoots at nothing.
        CheckTargetChoice(real, "Missile Defender, worker near, armed Humvee far", new("AmericaInfantryMissileDefender", [("AmericaVehicleDozer", 70), ("AmericaVehicleHumvee", 95)]), "AmericaVehicleDozer", "AmericaVehicleHumvee");
    }

    public static void RealTargetValueLeavesOrdersAndLoneVictimsAlone(string directory)
    {
        var real = new RealTargets(directory);
        foreach (var rate in TargetRates)
        {
            // An ordered attack names its victim: the Crusader keeps the Red Guard it was sent at though the tank is worth more.
            var ordered = new TargetScenario("AmericaTankCrusader", [("ChinaInfantryRedguard", 95), ("ChinaTankBattleMaster", 70)], Mode: TargetMode.Ordered, OrderedIndex: 0);
            foreach (var withFix in new[] { false, true })
                Check.That(RunTargetScenario(real, rate, withFix, ordered).Victim == "ChinaInfantryRedguard", $"{rate}: an ordered attack is never changed (fix {withFix}).");
            // One candidate: who is attacked does not depend on the fix.
            var lone = new TargetScenario("AmericaTankCrusader", [("ChinaInfantryRedguard", 80)]);
            foreach (var withFix in new[] { false, true })
                Check.That(RunTargetScenario(real, rate, withFix, lone).Victim == "ChinaInfantryRedguard", $"{rate}: the only enemy is attacked (fix {withFix}).");
        }
    }

    public static void RealTargetValueNumbersFollowTheRule(string directory)
    {
        var real = new RealTargets(directory);
        foreach (var rate in TargetRates)
        {
            var world = real.World(rate, true);
            var made = SpawnReal(world, ("AmericaTankCrusader", 1, 300, 300), ("ChinaInfantryRedguard", 2, 330, 300), ("ChinaTankBattleMaster", 2, 360, 300), ("AmericaVehicleDozer", 2, 390, 300), ("ChinaInfantryTankHunter", 2, 420, 300));
            var (crusader, redGuard, tank, dozer, hunter) = (made[0], made[1], made[2], made[3], made[4]);
            var onMan = world.TargetValueParts(crusader, redGuard);
            var onTank = world.TargetValueParts(crusader, tank);
            var onDozer = world.TargetValueParts(crusader, dozer);
            var onHunter = world.TargetValueParts(crusader, hunter);
            // The armour table: a tank round goes through a tank's armour whole and through a man's at a tenth.
            Check.That(onTank.Efficiency == Fixed.One && onMan.Efficiency == Fixed.FromDecimal(0.1m), $"{rate}: efficiency of the tank gun: tank {onTank.Efficiency}, man {onMan.Efficiency}.");
            Check.That(onTank.KillRate > onMan.KillRate / Fixed.FromInt(2), $"{rate}: the kill rate follows the victim's health ({onTank.KillRate}, {onMan.KillRate}).");
            // What shoots at the Crusader counts: a Tank Hunter is a danger to it, a Dozer is none, a Red Guard hardly.
            Check.That(onHunter.Threat > Fixed.FromDecimal(0.5m) && onDozer.Threat == Fixed.Zero && onMan.Threat < Fixed.FromDecimal(0.1m), $"{rate}: threat to the Crusader: hunter {onHunter.Threat}, dozer {onDozer.Threat}, Red Guard {onMan.Threat}.");
            Check.That(onTank.Value > onMan.Value && onTank.Value > onDozer.Value && onHunter.Value > onDozer.Value, $"{rate}: values: tank {onTank.Value}, Red Guard {onMan.Value}, dozer {onDozer.Value}, hunter {onHunter.Value}.");
        }
    }
}

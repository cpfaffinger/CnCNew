using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// The owner's playtest of 6 November 2026 (the stances of the Strategy Center do not switch), on the shipped objects of all four American Strategy Centers: each of the three
    /// plans unpacks for its 7 seconds, is in force with the bonuses of <c>GameData.ini</c> and the center's data, and a click on another plan (while one is unpacking, active or packing)
    /// packs the first, paralyzes the troops for 5 seconds, and unpacks the second - the same course in seconds at every tick rate.
    /// </summary>
    public static void RealBattlePlansSwitch(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        var bombardBit = BattlePlanBit("BATTLEPLAN_BOMBARDMENT");
        var holdBit = BattlePlanBit("BATTLEPLAN_HOLDTHELINE");
        var searchBit = BattlePlanBit("BATTLEPLAN_SEARCHANDDESTROY");

        foreach (var centerName in new[] { "AmericaStrategyCenter", "AirF_AmericaStrategyCenter", "Lazr_AmericaStrategyCenter", "SupW_AmericaStrategyCenter" })
        {
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "plans");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 7, players, "real-plans", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            Entity Spawn(string template, int owner, int x, int y)
            {
                var known = world.Entities.Select(value => value.Id).ToHashSet();
                Check.That(world.Submit(new SpawnCommand(new PlayerId(owner), ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
                world.Step();
                return world.Entities.First(value => value.Template.Name == template && !known.Contains(value.Id));
            }
            var center = Spawn(centerName, 1, 600, 600);
            var ranger = Spawn("AmericaInfantryRanger", 1, 500, 500);
            var tank = Spawn("AmericaTankCrusader", 1, 520, 500);
            var dozer = Spawn("AmericaVehicleDozer", 1, 540, 500);
            var plan = center.Modules.OfType<BattlePlanUpdate>().Single();
            for (var tick = 0; tick < world.Frames(30); tick++) world.Step();
            var maxHealth = center.Body!.MaxHealth;
            var frame = world.Time.OriginalFrames(1);
            var sevenSeconds = world.Time.OriginalFrames(210);

            void Choose(int option, string what)
                => Check.That(world.Submit(new UseSpecialPowerCommand(new PlayerId(1), ++sequence, world.Tick, center.Id, "SpecialAbilityChangeBattlePlans", SpecialPowerTargetKind.None, EntityId.Invalid, Fixed.Zero, Fixed.Zero, option)).Accepted,
                    $"{centerName}: the click on {what} is accepted.");
            long Until(Func<bool> done, string what, long limit)
            {
                var start = world.Tick;
                while (!done())
                {
                    world.Step();
                    Check.That(world.Tick - start <= limit, $"{centerName}: {what} did not come within {limit} ticks (status {plan.Status}, current {plan.CurrentPlan}, desired {plan.DesiredPlan}).");
                }
                return world.Tick - start;
            }

            Choose(1, "bombardment");
            var unpack = Until(() => plan.ActivePlan == BattlePlanKind.Bombardment, "bombardment", sevenSeconds + 6 * frame);
            Check.That(unpack >= sevenSeconds && unpack <= sevenSeconds + 4 * frame, $"{centerName}: bombardment unpacks in 7 s ({unpack} ticks, {sevenSeconds} expected).");
            Check.That((ranger.WeaponBonusConditions & bombardBit) != 0 && (tank.WeaponBonusConditions & bombardBit) != 0 && (dozer.WeaponBonusConditions & bombardBit) == 0 && (center.WeaponBonusConditions & bombardBit) == 0,
                $"{centerName}: bombardment gives the troops its weapon bonus and the dozer and the center none.");
            Check.That(world.DamageScalarOf(ranger) == Fixed.One, $"{centerName}: bombardment does not touch the armor.");

            // A click while the plan runs: it packs (the troops are paralyzed for 5 s), then the new plan unpacks.
            Choose(2, "hold the line");
            var switchStart = world.Tick;
            Until(() => plan.Status == BattlePlanUpdate.TransitionStatus.Packing, "packing", 6 * frame);
            Check.That((ranger.WeaponBonusConditions & bombardBit) == 0, $"{centerName}: the bombardment bonus is gone while it packs.");
            Check.That(ranger.ParalyzedUntil > world.Tick && tank.ParalyzedUntil > world.Tick && dozer.ParalyzedUntil <= world.Tick, $"{centerName}: the troops are paralyzed by the change, the dozer is not.");
            var paralysis = ranger.ParalyzedUntil - world.Tick;
            Check.That(paralysis >= world.Time.OriginalFrames(150) - 2 * frame && paralysis <= world.Time.OriginalFrames(150) + 3 * frame, $"{centerName}: the paralysis lasts 5 s ({paralysis} ticks).");
            Until(() => plan.ActivePlan == BattlePlanKind.HoldTheLine, "hold the line", 2 * sevenSeconds + 8 * frame);
            var switched = world.Tick - switchStart;
            Check.That(switched >= 2 * sevenSeconds && switched <= 2 * sevenSeconds + 8 * frame, $"{centerName}: the switch takes 14 s ({switched} ticks, {2 * sevenSeconds} expected).");
            Check.That((ranger.WeaponBonusConditions & holdBit) != 0 && (ranger.WeaponBonusConditions & bombardBit) == 0, $"{centerName}: hold the line replaced the bombardment bonus.");
            var scalar = world.DamageScalarOf(ranger);
            Check.That(scalar > Fixed.FromDecimal(0.83m) && scalar < Fixed.FromDecimal(0.84m) && world.DamageScalarOf(dozer) == Fixed.One, $"{centerName}: hold the line takes a sixth off the damage to the troops ({scalar}), not to the dozer.");
            Check.That(center.Body!.MaxHealth == maxHealth * Fixed.FromInt(2), $"{centerName}: hold the line doubles the health of the center ({center.Body.MaxHealth} of {maxHealth}).");

            Choose(3, "search and destroy");
            Until(() => plan.ActivePlan == BattlePlanKind.SearchAndDestroy, "search and destroy", 2 * sevenSeconds + 8 * frame);
            Check.That(center.Body.MaxHealth == maxHealth && world.DamageScalarOf(ranger) == Fixed.One, $"{centerName}: leaving hold the line gives the health and the armor back.");
            Check.That((ranger.WeaponBonusConditions & searchBit) != 0 && (tank.WeaponBonusConditions & searchBit) != 0 && center.ShroudScalar > Fixed.One, $"{centerName}: search and destroy gives the troops the range bonus and the center a wider sight.");

            // A click while the first choice is still unpacking: the unpack runs out, then the plan packs and the plan chosen last unpacks.
            Choose(1, "bombardment again");
            Until(() => plan.Status == BattlePlanUpdate.TransitionStatus.Unpacking && plan.CurrentPlan == BattlePlanKind.Bombardment, "unpacking", 2 * sevenSeconds);
            Choose(2, "hold the line during the unpack");
            Until(() => plan.ActivePlan == BattlePlanKind.Bombardment, "bombardment", 2 * sevenSeconds);
            Until(() => plan.ActivePlan == BattlePlanKind.HoldTheLine, "hold the line after the unpack", 2 * sevenSeconds + 8 * frame);
            Check.That((ranger.WeaponBonusConditions & (bombardBit | searchBit)) == 0 && (ranger.WeaponBonusConditions & holdBit) != 0, $"{centerName}: only hold the line is in force at the end.");
        }
    }
}

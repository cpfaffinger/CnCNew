using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// Real data: every retail object whose data carry a range that cannot be squared any more (docs/FIXED-POINT-Q39-24.md §5) is driven through the
/// comparison that squares it – the objects with a weapon whose <c>AttackRange</c> is 999,999 or 1,000,000 are ordered to fire, the objects with a
/// <c>StartAbilityRange</c> of 1,000,000 are ordered to use that ability on another object. Uses the public surface of the world only (it runs
/// unchanged on the code before the fix, where every one of these orders throws an <see cref="OverflowException"/>).
/// </summary>
static partial class Tests
{
    public static void RealSentinelRanges(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        // Anything above this cannot be squared at the current resolution (741,455 at 24 fraction bits).
        var limit = Fixed.Sqrt(Fixed.MaxValue);
        const string victimTemplate = "ChinaTankBattleMaster";
        Check.That(catalog.TryGet(victimTemplate, out _), victimTemplate + " is a retail template.");

        (SimulationWorld World, Feeder Feeder) World(string template, string label)
        {
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "sentinel");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 7, players, label, fingerprint, terrain.Fingerprint), catalog, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, template, owner: 1, x: 300, y: 600).Accepted, template + " spawns.");
            Check.That(feeder.Spawn(2, victimTemplate, owner: 2, x: 700, y: 600).Accepted, victimTemplate + " spawns.");
            feeder.SetRelationship(1, 2, Relationship.Enemies);
            feeder.SetRelationship(2, 1, Relationship.Enemies);
            world.RevealMapPermanently(new PlayerId(1), true);
            world.Step();
            return (world, feeder);
        }

        // ------------------------------------------------------------------------------------------------ weapons with an "everywhere" range
        var farWeapons = catalog.Weapons.Weapons.Where(weapon => weapon.AttackRange > limit).OrderBy(weapon => weapon.Name, StringComparer.Ordinal).ToArray();
        string[] expectedWeapons =
        [
            "AircraftCarrierOrderLaunch", "CINE_GLAJarmenKellRifle", "CINE_RangerAdvancedCombatRifle", "CINE_USAPathfinderSniperRifle",
            "Chem_ScudStormWeapon", "Demo_ScudStormWeapon", "GC_Chem_ScudStormWeapon", "ScudStormWeapon",
        ];
        Check.That(farWeapons.Select(weapon => weapon.Name).SequenceEqual(expectedWeapons),
            $"The retail weapons with an attack range beyond the square limit are the eight known ones (found: {string.Join(", ", farWeapons.Select(weapon => weapon.Name))}).");

        // Every step that can square the range runs on its own, so that one overflow does not hide the next: the list is empty on the fixed code
        // and names every step on the code before the fix.
        var overflows = new List<string>();
        void Drive(string what, Action step)
        {
            try { step(); }
            catch (OverflowException) { overflows.Add(what); }
        }

        var drivenWeapons = new HashSet<string>(StringComparer.Ordinal);
        var carriers = 0;
        foreach (var template in catalog.Templates.OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            var carried = template.WeaponSets.SelectMany(set => set.Weapons).Where(index => index >= 0).Select(index => catalog.Weapons[index]).Where(weapon => weapon.AttackRange > limit)
                .Select(weapon => weapon.Name).Distinct(StringComparer.Ordinal).ToArray();
            if (carried.Length == 0) continue;
            carriers++;
            var report = new List<string>();

            // 1. An order to fire: SimulationWorld.ChooseWeapon -> WithinWeaponRange for every weapon the command source may choose; then the shots themselves.
            Drive($"{template.Name}: fire order", () =>
            {
                var (world, feeder) = World(template.Name, "real-sentinel-order");
                var shooter = world.FindEntity(new EntityId(1))!;
                Check.That(world.MaxWeaponRange(shooter) > limit, $"{template.Name}: the reach of its weapons ({world.MaxWeaponRange(shooter)}) is beyond the square limit.");
                // A sniper rifle takes no vehicle: the second victim is a soldier.
                Check.That(feeder.Spawn(2, "ChinaInfantryRedguard", owner: 2, x: 700, y: 650).Accepted, "The second victim spawns.");
                world.Step();
                foreach (var victimId in new uint[] { 2, 3 })
                {
                    var victim = world.FindEntity(new EntityId(victimId))!;
                    var order = feeder.FireWeapon(1, 1, victimId);
                    var choice = world.ChooseWeapon(shooter, victim, FireOrigin.Player);
                    Check.That(order.Reason != CommandRejection.OutOfRange && choice.Rejection != CommandRejection.OutOfRange, $"{template.Name}: a target 400 units away is not out of range.");
                    var shots = 0;
                    for (var index = 0; index < world.Frames(60); index++)
                        shots += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == shooter.Id);
                    report.Add($"order on {victim.Template.Name} {(order.Accepted ? "accepted" : order.Reason.ToString())}, {shots} shots");
                }
            });

            // 2. No order at all: target acquisition and the turrets look as far as the weapons reach (SimulationWorld.FindMoodTarget, KeepsAttackIntent).
            Drive($"{template.Name}: standing next to an enemy", () =>
            {
                var (world, _) = World(template.Name, "real-sentinel-idle");
                var shooter = world.FindEntity(new EntityId(1))!;
                var shots = 0;
                for (var index = 0; index < world.Frames(150); index++)
                    shots += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == shooter.Id);
                report.Add($"idle {shots} shots");
            });

            // 3. The superweapon: FireWeaponPower fires the primary weapon at a place (the Scud Storm).
            foreach (var powerName in template.Modules.Select(module => module.Data).OfType<FireWeaponPowerData>().Select(data => data.Base.PowerName).Distinct(StringComparer.Ordinal))
                Drive($"{template.Name}: {powerName}", () =>
                {
                    var (world, feeder) = World(template.Name, "real-sentinel-power");
                    var shooter = world.FindEntity(new EntityId(1))!;
                    foreach (var module in shooter.Modules.OfType<ISpecialPowerModule>()) module.SetReadyFrame(world.Tick);
                    var order = feeder.UseSpecialPower(1, 1, powerName, SpecialPowerTargetKind.Location, x: 700, y: 600);
                    var shots = 0;
                    for (var index = 0; index < world.Frames(400); index++)
                        shots += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == shooter.Id);
                    report.Add($"{powerName} {(order.Accepted ? "accepted" : order.Reason.ToString())}, {shots} shots, victim hurt {Hurt(world.FindEntity(new EntityId(2))!)}");
                });

            Console.WriteLine($"       {template.Name} ({string.Join(", ", carried)}): {string.Join("; ", report)}");
            foreach (var name in carried) drivenWeapons.Add(name);
        }
        Check.That(drivenWeapons.OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(expectedWeapons), $"Every one of the eight weapons was driven by an object that carries it ({drivenWeapons.Count} of 8, {carriers} objects).");

        // ------------------------------------------------------------------------------------- abilities with an "everywhere" start range
        string[] expectedTrucks = ["Chem_GLAVehicleBombTruck", "Demo_GLAVehicleBombTruck", "GC_Chem_GLAVehicleBombTruck", "GC_Slth_GLAVehicleBattleBus", "GLAVehicleBombTruck", "Slth_GLAVehicleBombTruck"];
        var far = new List<(string Template, SpecialAbilityUpdateData Data)>();
        foreach (var template in catalog.Templates.OrderBy(value => value.Name, StringComparer.Ordinal))
            foreach (var data in template.Modules.Select(module => module.Data).OfType<SpecialAbilityUpdateData>())
                if (data.StartAbilityRange > limit && data.StartAbilityRange < SpecialAbilityUpdateData.HugeDistance) far.Add((template.Name, data));
        Check.That(far.Select(value => value.Template).SequenceEqual(expectedTrucks),
            $"The retail objects with a start range beyond the square limit (and below the 'no approach' value of 10,000,000) are the five bomb trucks and GC_Slth_GLAVehicleBattleBus (found: {string.Join(", ", far.Select(value => value.Template))}).");
        var refused = 0;
        foreach (var (template, data) in far)
            Drive($"{template}: {data.Base.PowerName} on an object", () =>
            {
                var (world, feeder) = World(template, "real-sentinel-ability");
                var truck = world.FindEntity(new EntityId(1))!;
                var ability = truck.Modules.OfType<SpecialAbilityUpdate>().Single(module => module.Data == data);
                foreach (var module in truck.Modules.OfType<ISpecialPowerModule>()) module.SetReadyFrame(world.Tick);
                var before = truck.X;
                // The disguise with a vehicle as its target: SpecialAbilityUpdate.IsWithinStartRange -> WithinSpheres squares range + both bounding circles.
                var order = feeder.UseSpecialPower(1, 1, data.Base.PowerName, SpecialPowerTargetKind.Object, target: 2);
                var stages = new List<AbilityStage>();
                for (var index = 0; index < world.Frames(150); index++)
                {
                    world.Step();
                    if (stages.Count == 0 || stages[^1] != ability.Stage) stages.Add(ability.Stage);
                }
                Console.WriteLine($"       {template} ({data.Base.PowerName}, start range {data.StartAbilityRange}): order {(order.Accepted ? "accepted" : order.Reason.ToString())}, stages {string.Join(" > ", stages)}, moved {(truck.X - before).Abs()}");
                // The disguise itself is not simulated (no handler for SPECIAL_DISGUISE_AS_VEHICLE): the order is refused before the ability's range is looked
                // at, so the retail value is not consumed today. Once a handler exists, the order is accepted and the range comparison runs right here.
                if (!order.Accepted)
                {
                    Check.That(order.Reason == CommandRejection.SpecialPowerHandlerMissing, $"{template}: the disguise is refused for the missing handler, nothing else ({order.Reason}).");
                    refused++;
                    return;
                }
                Check.That(stages.Count > 1, $"{template}: the ability went through its stages ({string.Join(" > ", stages)}).");
                Check.That(truck.X == before, $"{template}: with a start range of 1,000,000 the truck does not drive to its target.");
            });

        Console.WriteLine($"       abilities with a start range of 1,000,000: {far.Count} objects, {refused} refused for the missing disguise handler, {far.Count - refused} run");
        foreach (var overflow in overflows) Console.WriteLine($"       OVERFLOW  {overflow}");
        Check.That(overflows.Count == 0, $"No step overflows ({overflows.Count} did: {string.Join(" | ", overflows)}).");
    }
}

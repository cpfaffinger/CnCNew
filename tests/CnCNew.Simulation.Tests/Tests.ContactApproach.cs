using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// #414: a contact weapon's range is measured between the bounding circles (Weapon::isWithinAttackRange, ATTACK_RANGE_CALC_TYPE = FROM_BOUNDINGSPHERE_2D), and at a structure the
// attacker must touch it. Measured between the centres, a range of 3 reached no soldier whose circle kept the attacker's off: Colonel Burton circled his victim and never stabbed.
static partial class Tests
{
    public static void RealContactWeaponsReachTheirVictims(string directory)
    {
        var content = CnCNew.Content.ContentFileSystem.Open(directory);
        var options = CnCNew.Data.Compilation.DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = CnCNew.Data.Compilation.MapOverlayMode.Exclude };
        var graph = CnCNew.Data.Compilation.DefinitionCompiler.Compile(new RealSource(content), CnCNew.Data.Registry.ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = System.Collections.Immutable.ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "Attacker"), new PlayerConfiguration(new PlayerId(2), "Enemy", IsComputer: true));
        // Colonel Burton's knife button (SECONDARY, range 3), a terrorist (range 1) and a bomb truck (range 1) at a tank, and the bomb truck at a barracks it has to touch.
        var cases = new (string Source, string Victim, int Slot)[]
        {
            ("AmericaInfantryColonelBurton", "ChinaInfantryHacker", CnCNew.Simulation.Templates.WeaponSlot.Secondary),
            ("GLAInfantryTerrorist", "ChinaTankBattleMaster", CnCNew.Simulation.Templates.WeaponSlot.Best),
            ("GLAVehicleBombTruck", "ChinaTankBattleMaster", CnCNew.Simulation.Templates.WeaponSlot.Best),
            ("GLAVehicleBombTruck", "ChinaBarracks", CnCNew.Simulation.Templates.WeaponSlot.Best),
        };
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = CnCNew.Simulation.Templates.TemplateCatalog.Build(graph, CnCNew.Simulation.Modules.ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var (sourceName, victimName, slot) in cases)
            {
                var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "contact-weapons");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-contact-weapons", fingerprint, terrain.Fingerprint), catalog, terrain);
                var sequence = 0UL;
                void Spawn(string template, int owner, int x, int y)
                    => Check.That(world.Submit(new CnCNew.Simulation.Commands.SpawnCommand(new PlayerId(owner), ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
                Spawn(sourceName, 1, 400, 400);
                Spawn(victimName, 2, 570, 400);
                // The first look of the attacker's player (above 30 Hz the vision is taken once per logic frame of the original).
                for (var step = 0; step < world.Frames(2); step++) world.Step();
                var source = world.Entities.First(value => value.Template.Name == sourceName);
                var victim = world.Entities.First(value => value.Template.Name == victimName);
                // The victim stands still and fires at nobody.
                world.SetAttitude(victim, (int)Attitude.Sleep);
                // The command button's order (MSG_DO_WEAPON_AT_OBJECT without a shot limit): the attack approaches and fires.
                var order = world.Submit(new CnCNew.Simulation.Commands.FireWeaponCommand(new PlayerId(1), ++sequence, world.Tick, source.Id, victim.Id, slot, CnCNew.Simulation.Templates.FireOrigin.Player,
                    CnCNew.Simulation.Commands.FireWeaponCommand.NoMaxShotsLimit));
                var what = $"{rate}: {sourceName} at {victimName}";
                Check.That(order.Accepted, $"{what}: the order is accepted ({order.Reason}).");
                var firedAt = -1L;
                var distance = Fixed.Zero;
                for (var step = 0; step < world.Frames(600) && firedAt < 0; step++)
                    foreach (var value in world.Step().Events)
                        if (value.Entity == source.Id && value.Kind == SimEventKind.WeaponFired && firedAt < 0)
                        {
                            firedAt = world.Tick;
                            var dx = victim.X - source.X;
                            var dy = victim.Y - source.Y;
                            distance = Fixed.Sqrt(dx * dx + dy * dy);
                        }
                Check.That(firedAt >= 0, $"{what}: the attacker walks up and uses its contact weapon within 20 s.");
                for (var step = 0; step < world.Frames(30) && !(victim.EffectivelyDead || victim.DestroyRequested); step++) world.Step();
                Check.That(victim.EffectivelyDead || victim.DestroyRequested, $"{what}: the victim dies.");
                if (sourceName == "AmericaInfantryColonelBurton")
                    Check.That(distance >= Fixed.FromInt(20) && distance <= Fixed.FromInt(23), $"{what}: he stabs where the two circles of 10 are no more than the knife's 3 apart ({distance}).");
            }
        }
    }
}

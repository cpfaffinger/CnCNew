using System.Collections.Immutable;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The combat arithmetic on the shipped data under the product rules, against the hand calculation from the INI (owner's playtest of 7 November 2026: "units die too fast", "one rocket of
/// a rocket trooper kills a Comanche"). Measured with the whole weapon set of the three armies at 60 Hz and the key duels at 30, 60 and 120 Hz: every hit equals the weapon's damage times the
/// victim's armour coefficient, no weapon fires faster than its data, and a Comanche (220 health, <c>ComancheArmor</c> 100% against <c>INFANTRY_MISSILE</c>) takes six rockets of 40 - the
/// patch's data, no worse than the original's five of 48. The numbers below are written down from <c>Weapon.ini</c>, <c>Armor.ini</c> and <c>AmericaAir.ini</c>, not read from the catalog.
/// </summary>
static partial class Tests
{
    private static (CnCNew.Data.Definitions.CompiledDefinitionGraph Graph, string Fingerprint) CombatMathGraph(string directory)
    {
        var content = CnCNew.Content.ContentFileSystem.Open(directory);
        var options = CnCNew.Data.Compilation.DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = CnCNew.Data.Compilation.MapOverlayMode.Exclude };
        var graph = CnCNew.Data.Compilation.DefinitionCompiler.Compile(new RealSource(content), CnCNew.Data.Registry.ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        return (graph, fingerprint);
    }

    private sealed class CombatMathScene
    {
        public required SimulationWorld World { get; init; }
        public ulong Sequence;

        public void Spawn(string template, int owner, int x, int y)
            => Check.That(World.Submit(new CnCNew.Simulation.Commands.SpawnCommand(new PlayerId(owner), ++Sequence, World.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");

        public static CombatMathScene Build(CnCNew.Data.Definitions.CompiledDefinitionGraph graph, string fingerprint, SimulationRate rate)
        {
            var catalog = CnCNew.Simulation.Templates.TemplateCatalog.Build(graph, CnCNew.Simulation.Modules.ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "A"), new PlayerConfiguration(new PlayerId(2), "B"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "combat-math");
            return new CombatMathScene { World = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "combat-math", fingerprint, terrain.Fingerprint), catalog, terrain) };
        }
    }

    public static void RealCombatMathMatchesTheData(string directory)
    {
        var (graph, fingerprint) = CombatMathGraph(directory);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var where = $"{rate.TicksPerSecond} Hz";
            var perSecond = rate.TicksPerSecond;

            // ------------------------------------------------------------------------------------------------ rocket troopers against a Comanche
            // Hand calculation: MissileDefenderMissileWeapon 40 damage (INFANTRY_MISSILE) every 1000 ms; ComancheArmor INFANTRY_MISSILE 100%; Comanche MaxHealth 220: six rockets (5 x 40 = 200 leaves 20).
            // Three troopers fire in volleys of three: the second volley kills it.
            {
                var scene = CombatMathScene.Build(graph, fingerprint, rate);
                var world = scene.World;
                scene.Spawn("AmericaVehicleComanche", 2, 800, 800);
                world.Step();
                var comanche = world.Entities.First(value => value.Template.Name == "AmericaVehicleComanche");
                Check.That(comanche.Body!.MaxHealth == Fixed.FromInt(220), $"{where}: a Comanche has 220 health ({comanche.Body.MaxHealth}).");
                world.Submit(new CnCNew.Simulation.Commands.SetMoveTargetCommand(new PlayerId(2), ++scene.Sequence, world.Tick, comanche.Id, Fixed.FromInt(860), Fixed.FromInt(800)));
                for (var step = 0; step < 20 * perSecond && (comanche.HasMoveTarget || comanche.Z <= Fixed.Zero); step++) world.Step();
                Check.That(comanche.Z > Fixed.Zero, $"{where}: the Comanche hovers.");
                for (var index = 0; index < 3; index++) scene.Spawn("AmericaInfantryMissileDefender", 1, 770, 794 + index * 6);
                world.Step();
                var troopers = world.Entities.Where(value => value.Template.Name == "AmericaInfantryMissileDefender").Select(value => value.Id).ToHashSet();
                var hits = new List<Fixed>();
                var troopersFired = new List<long>();
                var diedAt = -1L;
                var start = world.Tick;
                for (var step = 0; step < 10 * perSecond && diedAt < 0; step++)
                    foreach (var value in world.Step().Events)
                    {
                        if (value.Kind == SimEventKind.WeaponFired && troopers.Contains(value.Entity)) troopersFired.Add(value.Tick - start);
                        if (value.Kind == SimEventKind.EntityDamaged && value.Entity == comanche.Id && troopers.Contains(value.Other)) hits.Add(Fixed.FromRaw(value.Value));
                        if (value.Kind == SimEventKind.EntityDied && value.Entity == comanche.Id) diedAt = value.Tick - start;
                    }
                Check.That(diedAt >= 0, $"{where}: three Missile Defenders kill a hovering Comanche.");
                Check.That(hits.Count == 6, $"{where}: it takes six rockets, not one: {hits.Count}.");
                Check.That(hits.Take(5).All(value => value == Fixed.FromInt(40)) && hits[5] == Fixed.FromInt(20), $"{where}: five hits of 40, the sixth takes the last 20: {string.Join(",", hits)}.");
                var seconds = diedAt / (double)perSecond;
                Check.That(seconds is > 1.0 and < 2.5, $"{where}: two volleys, a second apart, and the flight of the missiles: {seconds:0.00} s.");
                Check.That(troopersFired.Count is >= 6 and <= 9, $"{where}: the troopers fired two volleys and no more before it was dead ({troopersFired.Count} shots).");
            }

            // ------------------------------------------------------------------------------------------------ every weapon against a Dozer that is healed in every tick
            // (shooter, weapon the shooter fires, damage of one hit, shots a second at most). Dozer armour against the damage types: SMALL_ARMS 25%, ARMOR_PIERCING and INFANTRY_MISSILE 100%,
            // EXPLOSION 100%; the shots a second are 1000 / DelayBetweenShots (frames of 33.3 ms rounded up to an even number, F-140), a clip weapon is clip / (cycle).
            var weapons = new (string Unit, string Weapon, double Hit, double MaxPerSecond)[]
            {
                ("AmericaInfantryMissileDefender", "MissileDefenderMissileWeapon", 40, 1.0),       // 40 x 100%, 1000 ms
                ("ChinaInfantryTankHunter", "ChinaInfantryTankHunterMissileLauncher", 40, 1.0),    // 40 x 100%, 1000 ms
                ("GLAInfantryTunnelDefender", "TunnelDefenderRocketWeapon", 40, 1.0),              // 40 x 100%, 1000 ms
                ("AmericaTankCrusader", "CrusaderTankGun", 60, 0.5),                               // 60 x 100%, 2000 ms
                ("ChinaTankBattleMaster", "BattleMasterTankGun", 60, 0.5),                         // 60 x 100%, 2000 ms
                ("AmericaTankPaladin", "PaladinTankGun", 60, 0.5),                                 // 60 x 100%, 2000 ms
                ("GLATankScorpion", "ScorpionTankGun", 20, 1.0),                                   // 20 x 100%, 1000 ms
                ("AmericaInfantryRanger", "RangerAdvancedCombatRifle", 1.25, 3.34),                // 5 x 25%; clip of 3, 100 ms between shots, 700 ms reload: 3 per 900 ms
                ("GLAInfantryRebel", "GLARebelMachineGun", 1.25, 3.34),                            // 5 x 25%; the same cycle
                ("AmericaVehicleHumvee", "HumveeGun", 2.5, 5.01),                                  // 10 x 25%, 200 ms
                ("GLAVehicleTechnicalChassisOne", "TechnicalMachineGunWeapon", 1.0, 5.01),         // 10 x 10%, 200 ms
                ("ChinaInfantryRedguard", "RedguardMachineGun", 3.75, 1.0),                        // 15 x 25%, 1000 ms
            };
            foreach (var (unit, weaponName, hit, maxPerSecond) in weapons)
            {
                var scene = CombatMathScene.Build(graph, fingerprint, rate);
                var world = scene.World;
                scene.Spawn("AmericaVehicleDozer", 2, 800, 800);
                scene.Spawn(unit, 1, 800 - 70, 800);
                world.Step();
                var dozer = world.Entities.First(value => value.Template.Name == "AmericaVehicleDozer");
                var shooter = world.Entities.First(value => value.Template.Name == unit);
                var shots = new List<long>();
                var damage = new List<Fixed>();
                for (var step = 0; step < 12 * perSecond; step++)
                {
                    foreach (var value in world.Step().Events)
                    {
                        if (value.Kind == SimEventKind.WeaponFired && value.Entity == shooter.Id) { shots.Add(value.Tick); Check.That(value.Text == weaponName, $"{where}: {unit} fires {weaponName}, not {value.Text}."); }
                        if (value.Kind == SimEventKind.EntityDamaged && value.Entity == dozer.Id && value.Other == shooter.Id) damage.Add(Fixed.FromRaw(value.Value));
                    }
                    world.Submit(new CnCNew.Simulation.Commands.DamageCommand(PlayerId.System, ++scene.Sequence, world.Tick, dozer.Id, Fixed.FromInt(100000), DamageTypes.Healing, DeathTypes.Normal));
                }
                Check.That(shots.Count >= 4, $"{where}: {unit} fires at the dozer ({shots.Count} shots).");
                Check.That(damage.Count >= shots.Count - 1 && damage.All(value => (double)value.ToDecimal() > hit - 0.001 && (double)value.ToDecimal() < hit + 0.001),
                    $"{where}: every hit of {unit} is {hit}: {string.Join(",", damage.Take(6))}.");
                var perSecondMeasured = (shots.Count - 1) / ((shots[^1] - shots[0]) / (double)perSecond);
                // a clip weapon's span is measured over whole cycles only when it ends on a clip's last shot - allow for the one reload gap of the span's end
                var tolerance = weaponName is "RangerAdvancedCombatRifle" or "GLARebelMachineGun" ? 1.35 : 1.0;
                Check.That(perSecondMeasured <= maxPerSecond * tolerance, $"{where}: {unit} fires {perSecondMeasured:0.00} a second, the data allow {maxPerSecond:0.00}.");
            }

            // ------------------------------------------------------------------------------------------------ tanks: 400 and 480 health at 60 damage
            // Crusader (480) against a Battlemaster (400): 7 hits of 60 (6 x 60 = 360), one every 2000 ms.
            {
                var scene = CombatMathScene.Build(graph, fingerprint, rate);
                var world = scene.World;
                scene.Spawn("ChinaTankBattleMaster", 2, 800, 800);
                scene.Spawn("AmericaTankCrusader", 1, 700, 800);
                world.Step();
                var victim = world.Entities.First(value => value.Template.Name == "ChinaTankBattleMaster");
                var shooter = world.Entities.First(value => value.Template.Name == "AmericaTankCrusader");
                var hits = new List<Fixed>();
                var shotTicks = new List<long>();
                var diedAt = -1L;
                for (var step = 0; step < 40 * perSecond && diedAt < 0; step++)
                    foreach (var value in world.Step().Events)
                    {
                        if (value.Kind == SimEventKind.WeaponFired && value.Entity == shooter.Id) shotTicks.Add(value.Tick);
                        if (value.Kind == SimEventKind.EntityDamaged && value.Entity == victim.Id && value.Other == shooter.Id) hits.Add(Fixed.FromRaw(value.Value));
                        if (value.Kind == SimEventKind.EntityDied && value.Entity == victim.Id) diedAt = value.Tick;
                    }
                Check.That(!shooter.EffectivelyDead && diedAt >= 0, $"{where}: the Crusader wins its duel (7 shots kill the Battlemaster, 8 the Crusader).");
                Check.That(hits.Count == 7 && hits.Take(6).All(value => value == Fixed.FromInt(60)) && hits[6] == Fixed.FromInt(40), $"{where}: 6 x 60 and the last 40: {string.Join(",", hits)}.");
                Check.That(shotTicks.Zip(shotTicks.Skip(1), (a, b) => b - a).All(gap => gap * 1000.0 / perSecond >= 1990), $"{where}: a tank shell every 2000 ms at the quickest.");
            }
        }
    }
}

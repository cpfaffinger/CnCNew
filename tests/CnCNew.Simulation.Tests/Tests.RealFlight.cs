using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Retail data: the first units whose primary weapon launches a DumbProjectileBehavior shell and a MissileAIUpdate missile fire at a Crusader; both projectiles fly and hit; a Chinook
    /// ferries supplies between a supply center and a warehouse at its flight height; a GLA tunnel network lets a unit out through another tunnel.
    /// The whole scenario runs twice with identical tick hashes.
    /// </summary>
    public static void RealFlightAndTransports(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, SimulationRate.Original);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var name in new[] { "AmericaTankCrusader", "AmericaVehicleChinook", "AmericaSupplyCenter", "GLATunnelNetwork" })
            Check.That(catalog.TryGet(name, out var template) && template.IsInstantiable, $"{name} is instantiable.");
        // The first instantiable unit (ordinal name order) whose primary weapon launches an instantiable object with the given projectile module.
        ObjectTemplate Launcher(string module) => catalog.Templates.Where(value => value.IsInstantiable && value.Locomotor is not null && value.DefaultWeapon >= 0)
            .Where(value => !value.Name.StartsWith("CINE_", StringComparison.Ordinal)).OrderBy(value => value.Name, StringComparer.Ordinal)
            .First(value => catalog.ProjectileTemplates[value.DefaultWeapon] is var projectile && projectile >= 0 && catalog[projectile].IsInstantiable
                && catalog[projectile].Modules.Any(spec => spec.Name == module));
        var shellLauncher = Launcher("DumbProjectileBehavior");
        var missileLauncher = Launcher("MissileAIUpdate");
        var shellName = catalog[catalog.ProjectileTemplates[shellLauncher.DefaultWeapon]].Name;
        var missileName = catalog[catalog.ProjectileTemplates[missileLauncher.DefaultWeapon]].Name;
        var warehouse = catalog.Templates.First(value => value.IsInstantiable && value.Modules.Any(module => module.Name == "SupplyWarehouseDockUpdate"));
        var infantry = catalog.Templates.First(value => value.IsInstantiable && value.Side == "GLA" && !value.Name.StartsWith("CINE_", StringComparison.Ordinal) && value.IsKindOf("INFANTRY") && value.TransportSlotCount > 0 && value.Locomotor is not null);
        var projectileWeapons = catalog.Weapons.Weapons.Where(value => catalog.ProjectileTemplates[value.Index] >= 0).ToArray();
        var flying = projectileWeapons.Count(value => catalog[catalog.ProjectileTemplates[value.Index]].IsInstantiable);
        Console.WriteLine($"       real flight: {projectileWeapons.Length} weapons fire a projectile object, {flying} of them an instantiable one; shell {shellLauncher.Name}/{shellName}, missile {missileLauncher.Name}/{missileName}, warehouse {warehouse.Name}, tunnel rider {infantry.Name}");
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));

        (ulong[] Hashes, Dictionary<string, long> Facts) Run()
        {
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Original, 5, players, "real-flight", fingerprint), catalog);
            var sequence = new Dictionary<int, ulong>();
            SubmitResult Send(int player, Func<ulong, SimCommand> make) => world.Submit(make(sequence[player] = sequence.GetValueOrDefault(player) + 1));
            void Spawn(int player, string template, int x, int y)
                => Check.That(Send(player, next => new SpawnCommand(new PlayerId(player), next, world.Tick, template, PlayerId.System, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted
                    || player == 0 && Send(0, next => new SpawnCommand(PlayerId.System, next, world.Tick, template, PlayerId.System, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted,
                    $"{template} spawns.");
            Entity Find(string template, int x) => world.Entities.First(value => value.Template.Name == template && value.X == Fixed.FromInt(x));
            var shellRange = catalog.Weapons[shellLauncher.DefaultWeapon].AttackRange;
            var missileRange = catalog.Weapons[missileLauncher.DefaultWeapon].AttackRange;
            Spawn(1, shellLauncher.Name, 1000, 1000);
            Spawn(2, "AmericaTankCrusader", 1000 + (int)(shellRange * Fixed.FromDecimal(0.8m)).Floor(), 1000);
            Spawn(1, missileLauncher.Name, 5000, 1000);
            Spawn(2, "AmericaTankCrusader", 5000 + (int)(missileRange * Fixed.FromDecimal(0.8m)).Floor(), 1000);
            Spawn(2, "AmericaSupplyCenter", 2000, 3000);
            Spawn(0, warehouse.Name, 2300, 3000);
            Spawn(2, "AmericaVehicleChinook", 2000, 3000);
            Spawn(1, "GLATunnelNetwork", 3000, 5000);
            Spawn(1, "GLATunnelNetwork", 3400, 5000);
            Spawn(1, infantry.Name, 3000, 5000);
            var facts = new Dictionary<string, long>();
            var hashes = new List<ulong> { world.Step().StateHash };
            var tank = world.Entities.First(value => value.Template.Name == shellLauncher.Name && value.X == Fixed.FromInt(1000));
            var hunter = world.Entities.First(value => value.Template.Name == missileLauncher.Name && value.X == Fixed.FromInt(5000));
            var shellTarget = world.Entities.Where(value => value.Template.Name == "AmericaTankCrusader" && value.Owner == new PlayerId(2)).OrderBy(value => value.X).First();
            var missileTarget = world.Entities.Where(value => value.Template.Name == "AmericaTankCrusader" && value.Owner == new PlayerId(2)).OrderBy(value => value.X).Last();
            var chinook = world.Entities.First(value => value.Template.Name == "AmericaVehicleChinook");
            var rider = world.Entities.First(value => value.Template.Name == infantry.Name);
            var tunnelA = Find("GLATunnelNetwork", 3000);
            var tunnelB = Find("GLATunnelNetwork", 3400);
            var fireTick = world.Tick;
            Check.That(Send(1, next => new FireWeaponCommand(new PlayerId(1), next, world.Tick, tank.Id, shellTarget.Id)).Accepted, "The Battle Master fires.");
            Check.That(Send(1, next => new FireWeaponCommand(new PlayerId(1), next, world.Tick, hunter.Id, missileTarget.Id)).Accepted, "The Tank Hunter fires.");
            Check.That(Send(2, next => new DockCommand(new PlayerId(2), next, world.Tick, chinook.Id, world.Entities.First(value => value.Template.Name == warehouse.Name).Id)).Accepted, "The Chinook is sent to the warehouse.");
            Check.That(Send(1, next => new EnterContainerCommand(new PlayerId(1), next, world.Tick, rider.Id, tunnelA.Id)).Accepted, "The GLA unit enters tunnel A.");
            var shellHealth = shellTarget.Body!.Health;
            var missileHealth = missileTarget.Body!.Health;
            for (var tick = 0; tick < 1800; tick++)
            {
                if (tick == 10) Send(1, next => new EvacuateCommand(new PlayerId(1), next, world.Tick, tunnelB.Id, false, Fixed.Zero, Fixed.Zero));
                var result = world.Step();
                hashes.Add(result.StateHash);
                foreach (var simEvent in result.Events)
                {
                    if (simEvent.Kind == SimEventKind.EntitySpawned && (simEvent.Text == shellName || simEvent.Text == missileName)) facts.TryAdd("spawn:" + simEvent.Text, result.Tick);
                    if (simEvent.Kind == SimEventKind.WeaponImpact) facts.TryAdd("impact:" + simEvent.Text, result.Tick);
                    if (simEvent.Kind == SimEventKind.SupplyDeposited) facts["deposits"] = facts.GetValueOrDefault("deposits") + 1;
                    if (simEvent.Kind == SimEventKind.EntityUncontained && simEvent.Other == tunnelB.Id) facts.TryAdd("tunnelExit", result.Tick);
                }
                if (chinook.Z > Fixed.Zero) facts["chinookAirborne"] = 1;
                if (chinook.Modules.OfType<AIUpdateInterface>().FirstOrDefault() is { IsInsideDock: true })
                {
                    facts["chinookInsideDock"] = facts.GetValueOrDefault("chinookInsideDock") + 1;
                    if (chinook.HasMoveTarget) facts["chinookDockWhileMoving"] = facts.GetValueOrDefault("chinookDockWhileMoving") + 1;
                }
            }
            facts["shellDamage"] = (shellHealth - shellTarget.Body.Health).Raw;
            facts["missileDamage"] = (missileHealth - missileTarget.Body.Health).Raw;
            facts["fire"] = fireTick;
            world.VerifyIncrementalHash();
            return ([..hashes], facts);
        }

        var (first, facts) = Run();
        string Fact(string key) => facts.TryGetValue(key, out var value) ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
        Console.WriteLine($"       real flight: shell impact at +{facts.GetValueOrDefault("impact:" + catalog.Weapons[shellLauncher.DefaultWeapon].Name) - facts["fire"]}, missile impact at +{facts.GetValueOrDefault("impact:" + catalog.Weapons[missileLauncher.DefaultWeapon].Name) - facts["fire"]}, Chinook deposits {Fact("deposits")}, tunnel exit at {Fact("tunnelExit")}");
        Check.That(facts.ContainsKey("spawn:" + shellName) && facts.ContainsKey("spawn:" + missileName), "Both projectile objects are created.");
        Check.That(facts.GetValueOrDefault("impact:" + catalog.Weapons[shellLauncher.DefaultWeapon].Name) > facts["fire"] + 1 && facts["shellDamage"] > 0, "The shell flies for more than a tick and hurts the Crusader.");
        Check.That(facts.GetValueOrDefault("impact:" + catalog.Weapons[missileLauncher.DefaultWeapon].Name) > facts["fire"] + 1 && facts["missileDamage"] > 0, "The missile flies for more than a tick and hurts the Crusader.");
        Check.That(facts.GetValueOrDefault("deposits") >= 1 && facts.ContainsKey("chinookAirborne"), "The Chinook flies and delivers supplies.");
        Check.That(facts.GetValueOrDefault("chinookInsideDock") > 0 && facts.GetValueOrDefault("chinookDockWhileMoving") == 0,
            $"The Chinook shows the dock state ({Fact("chinookInsideDock")} ticks) only while it hovers at the dock, never on the way ({Fact("chinookDockWhileMoving")} moving ticks).");
        Check.That(facts.ContainsKey("tunnelExit"), "The GLA unit leaves through the other tunnel.");
        var (second, _) = Run();
        Check.That(first.SequenceEqual(second), "Two runs are hash identical on every tick.");
    }


}

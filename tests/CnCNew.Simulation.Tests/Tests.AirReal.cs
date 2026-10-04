using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Retail data: every aircraft with a <c>JetAIUpdate</c> is instantiable (the cruise missile of the cinematics too) and declares the locomotor sets it names; a real airfield produces a Raptor and a
    /// Comanche, the Raptor takes off on an order, empties its missiles on Crusaders, returns, lands and reloads on its own, the Comanche rises from the pad; the run is deterministic.
    /// </summary>
    public static void RealAirfieldAndAircraft(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);

        var aircraft = catalog.Templates.Where(value => value.Modules.Any(module => module.Name == "JetAIUpdate")).ToArray();
        var blocked = aircraft.Where(value => !value.IsInstantiable).ToArray();
        Check.That(aircraft.Length >= 34 && blocked.Length == 0, $"Every JetAIUpdate object is instantiable ({aircraft.Length} objects; blocked: {string.Join(",", blocked.Select(value => value.Name + ":" + string.Join("/", value.MissingModules)))}).");
        Check.That(catalog.TryGet("CINE_U06_CruiseMissileFLY", out var cruise) && cruise.IsInstantiable, "The cinematic cruise missile is instantiable.");
        var withoutTaxi = aircraft.Where(value => !value.LocomotorSets.ContainsKey("SET_TAXIING")).Select(value => value.Name).ToArray();
        Console.WriteLine("       real air: JetAIUpdate objects without a taxi locomotor set: " + string.Join(", ", withoutTaxi));
        foreach (var template in aircraft)
        {
            var jet = (JetAIData)template.Modules.First(module => module.Name == "JetAIUpdate").Data;
            Check.That(template.LocomotorSets.ContainsKey(jet.AttackLocomotor) || jet.AttackLocomotor == "SET_NORMAL", $"{template.Name} declares its attack locomotor set {jet.AttackLocomotor}.");
            Check.That(template.LocomotorSets.ContainsKey(jet.ReturnLocomotor) || jet.ReturnLocomotor == "SET_NORMAL", $"{template.Name} declares its return locomotor set {jet.ReturnLocomotor}.");
        }
        var airfields = catalog.Templates.Where(value => value.Modules.Any(module => module.Name == "ParkingPlaceBehavior")).ToArray();
        Check.That(airfields.Length == 9 && airfields.All(value => value.IsInstantiable && value.IsKindOf("FS_AIRFIELD")), $"All nine airfield objects are instantiable and FS_AIRFIELD ({airfields.Length}).");
        Console.WriteLine($"       real air: {aircraft.Length} aircraft objects, {airfields.Length} airfields; {catalog.Templates.Count(value => value.Modules.Any(module => module.Name == "SpectreGunshipUpdate") && value.IsInstantiable)} gunships, {catalog.Templates.Count(value => value.Modules.Any(module => module.Name == "MissileLauncherBuildingUpdate") && value.IsInstantiable)} silos");
        foreach (var name in new[] { "AmericaAirfield", "AmericaJetRaptor", "AmericaVehicleComanche", "AmericaTankCrusader" })
            Check.That(catalog.TryGet(name, out var template) && template.IsInstantiable, $"{name} is instantiable.");
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));

        (ulong[] Hashes, List<string> Log) Run()
        {
            var log = new List<string>();
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 5, players, "real-air", fingerprint), catalog);
            var sequence = new Dictionary<int, ulong>();
            SubmitResult Send(int player, Func<ulong, SimCommand> make) => world.Submit(make(sequence[player] = sequence.GetValueOrDefault(player) + 1));
            void Spawn(int player, string template, int x, int y)
                => Check.That(Send(player, next => new SpawnCommand(new PlayerId(player), next, world.Tick, template, new PlayerId(player), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template} spawns.");
            Spawn(1, "AmericaAirfield", 2000, 2000);
            for (var index = 0; index < 3; index++) Spawn(2, "AmericaTankCrusader", 4600 + index * 30, 2000);
            var hashes = new List<ulong> { world.Step().StateHash };
            Check.That(Send(0, next => new GiveMoneyCommand(PlayerId.System, next, world.Tick, new PlayerId(1), Fixed.FromInt(20000))).Accepted, "Money is granted.");
            hashes.Add(world.Step().StateHash);
            var airfield = world.Entities.First(value => value.Template.Name == "AmericaAirfield");
            // The airfield is complete when it is spawned directly; production needs nothing else for aircraft.
            Check.That(Send(1, next => new QueueProductionCommand(new PlayerId(1), next, world.Tick, airfield.Id, "AmericaJetRaptor")).Accepted, "The Raptor is queued.");
            Check.That(Send(1, next => new QueueProductionCommand(new PlayerId(1), next, world.Tick, airfield.Id, "AmericaVehicleComanche")).Accepted, "The Comanche is queued.");
            Entity? raptor = null;
            Entity? comanche = null;
            for (var tick = 0; tick < 4000 && (raptor is null || comanche is null || raptor.Modules.OfType<JetAIUpdate>().Single().Phase != JetAIUpdate.JetPhase.None); tick++)
            {
                hashes.Add(world.Step().StateHash);
                raptor ??= world.Entities.FirstOrDefault(value => value.Template.Name == "AmericaJetRaptor");
                comanche ??= world.Entities.FirstOrDefault(value => value.Template.Name == "AmericaVehicleComanche");
            }
            Check.That(raptor is not null && comanche is not null, "The airfield produced both aircraft.");
            var jetAi = raptor!.Modules.OfType<JetAIUpdate>().Single();
            var heliAi = comanche!.Modules.OfType<JetAIUpdate>().Single();
            Check.That(jetAi.Phase == JetAIUpdate.JetPhase.None && !jetAi.AllowAirLoco && jetAi.Airfield == airfield.Id, $"The Raptor is parked at the airfield ({jetAi.Phase}).");
            log.Add($"raptor parked at tick {world.Tick} at ({raptor.X}, {raptor.Y}, {raptor.Z})");
            Check.That(jetAi.IsIdle, "A parked Raptor without a stored order is idle (the scripts ask AIUpdateInterface.IsIdle, which the jet overrides).");
            var weapon = catalog.Weapons[raptor.Template.DefaultWeapon];
            Check.That(weapon.ReloadsAtBase && weapon.ClipSize > 0 && raptor.WeaponClip == weapon.ClipSize, $"The Raptor's weapon returns to base to reload (clip {weapon.ClipSize}).");

            Check.That(Send(1, next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, raptor.Id, Fixed.FromInt(4600) - weapon.AttackRange / Fixed.FromInt(2), Fixed.FromInt(2000))).Accepted, "The Raptor is ordered out.");
            hashes.Add(world.Step().StateHash);
            var phases = new List<JetAIUpdate.JetPhase>();
            var idleInAirfieldState = false;
            for (var tick = 0; tick < 3000 && !(jetAi.AllowAirLoco && jetAi.Phase == JetAIUpdate.JetPhase.None && !raptor.HasMoveTarget); tick++)
            {
                hashes.Add(world.Step().StateHash);
                if (phases.Count == 0 || phases[^1] != jetAi.Phase) phases.Add(jetAi.Phase);
                idleInAirfieldState |= jetAi.Phase != JetAIUpdate.JetPhase.None && ((AIUpdateInterface)jetAi).IsIdle;
            }
            Check.That(!idleInAirfieldState, "The jet is never idle between the order and take-off, through any airfield state, seen through the base type the scripts use.");
            Check.That(jetAi.AllowAirLoco && !raptor.HasMoveTarget, "The Raptor took off and reached its destination: " + string.Join(">", phases));
            Check.That(phases.Contains(JetAIUpdate.JetPhase.TaxiToTakeoff) && phases.Contains(JetAIUpdate.JetPhase.TakingOff), "It took off through the runway states: " + string.Join(">", phases));
            log.Add($"raptor at its destination at tick {world.Tick} ({raptor.X}, {raptor.Y}, {raptor.Z})");

            // empty the clip on the Crusaders
            var shots = 0;
            for (var tick = 0; tick < 2000 && raptor.WeaponClip > 0; tick++)
            {
                var target = world.Entities.Where(value => value.Template.Name == "AmericaTankCrusader" && !value.EffectivelyDead).OrderBy(value => value.Id.Value).FirstOrDefault();
                if (target is not null && world.Tick >= raptor.WeaponReadyTick)
                    if (Send(1, next => new FireWeaponCommand(new PlayerId(1), next, world.Tick, raptor.Id, target.Id)).Accepted) shots++;
                hashes.Add(world.Step().StateHash);
            }
            Check.That(raptor.WeaponClip == 0 && shots == weapon.ClipSize, $"The Raptor emptied its clip ({shots} shots).");
            Check.That(jetAi.IsOutOfSpecialReloadAmmo(world), "It knows it is out of ammunition.");
            var back = new List<JetAIUpdate.JetPhase>();
            for (var tick = 0; tick < 6000 && !(back.Count > 2 && jetAi.Phase == JetAIUpdate.JetPhase.None && !jetAi.AllowAirLoco); tick++)
            {
                hashes.Add(world.Step().StateHash);
                if (back.Count == 0 || back[^1] != jetAi.Phase) back.Add(jetAi.Phase);
            }
            Check.That(!jetAi.AllowAirLoco && jetAi.Phase == JetAIUpdate.JetPhase.None && raptor.WeaponClip == weapon.ClipSize, $"The Raptor is parked again with a full clip: {string.Join(">", back)} (clip {raptor.WeaponClip}).");
            log.Add($"raptor reloaded at tick {world.Tick}; phases {string.Join(">", back)}");

            // the helicopter rises from the pad and flies on a move order
            Check.That(Send(1, next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, comanche.Id, Fixed.FromInt(2600), Fixed.FromInt(2000))).Accepted, "The Comanche is ordered out.");
            hashes.Add(world.Step().StateHash);
            for (var tick = 0; tick < 1500 && !(heliAi.AllowAirLoco && !comanche.HasMoveTarget && heliAi.Phase == JetAIUpdate.JetPhase.None); tick++) hashes.Add(world.Step().StateHash);
            Check.That(heliAi.AllowAirLoco && comanche.Z > Fixed.Zero, $"The Comanche flew to its destination ({comanche.X}, {comanche.Y}, {comanche.Z}).");
            log.Add($"comanche flying at tick {world.Tick} at ({comanche.X}, {comanche.Y}, {comanche.Z})");
            // the cinematic cruise missile is a jet without an airfield: it simply flies where it is sent
            Spawn(1, "CINE_U06_CruiseMissileFLY", 300, 300);
            hashes.Add(world.Step().StateHash);
            var cruiseMissile = world.Entities.First(value => value.Template.Name == "CINE_U06_CruiseMissileFLY");
            Check.That(Send(1, next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, cruiseMissile.Id, Fixed.FromInt(1500), Fixed.FromInt(300))).Accepted, "The cruise missile is ordered.");
            hashes.Add(world.Step().StateHash);
            for (var tick = 0; tick < 600 && cruiseMissile.HasMoveTarget; tick++) hashes.Add(world.Step().StateHash);
            Check.That(!cruiseMissile.HasMoveTarget && (cruiseMissile.X - Fixed.FromInt(1500)).Abs() < Fixed.FromInt(30) && cruiseMissile.Z > Fixed.Zero, $"The cruise missile flew to its destination ({cruiseMissile.X}, {cruiseMissile.Y}, {cruiseMissile.Z}).");
            for (var tick = 0; tick < 300; tick++) hashes.Add(world.Step().StateHash);
            return ([..hashes], log);
        }

        // real EMP pulse and real neutron missile (fired by the weapon directly: the creation-list nugget that launches it from the silo is not simulated yet)
        void Pulse()
        {
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 5, players, "real-air-emp", fingerprint), catalog);
            var sequence = new Dictionary<int, ulong>();
            void Spawn(int player, string template, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(new PlayerId(player), sequence[player] = sequence.GetValueOrDefault(player) + 1, world.Tick, template, new PlayerId(player), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template} spawns.");
            Spawn(2, "AmericaTankCrusader", 1010, 1000);
            Spawn(2, "AmericaJetRaptor", 1000, 1020);
            for (var tick = 0; tick < 150; tick++) world.Step();
            var tank = world.Entities.First(value => value.Template.Name == "AmericaTankCrusader");
            var raptor = world.Entities.First(value => value.Template.Name == "AmericaJetRaptor");
            Check.That(raptor.Z > Fixed.FromInt(50), $"The Raptor flies ({raptor.Z}).");
            Spawn(1, "EMPPulseEffectSpheroid", 1000, 1000);
            for (var tick = 0; tick < 30; tick++) world.Step();
            Check.That(tank.EmpDisabledUntil > world.Tick + 800, $"The real EMP pulse disables the tank for 30 seconds ({tank.EmpDisabledUntil} at {world.Tick}).");
            Check.That(raptor.EffectivelyDead || !world.Entities.Contains(raptor), "The airborne Raptor was shot out of the sky by the pulse.");
        }
        Pulse();

        void Nuke()
        {
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 5, players, "real-air-nuke", fingerprint), catalog);
            var sequence = new Dictionary<int, ulong>();
            void Spawn(int player, string template, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(new PlayerId(player), sequence[player] = sequence.GetValueOrDefault(player) + 1, world.Tick, template, new PlayerId(player), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template} spawns.");
            Spawn(1, "ChinaNuclearMissileLauncher", 1000, 3000);
            Spawn(2, "AmericaTankCrusader", 3000, 3000);
            Spawn(2, "AmericaTankCrusader", 3090, 3000);
            Spawn(2, "AmericaTankCrusader", 3400, 3000);
            world.Step();
            var silo = world.Entities.First(value => value.Template.Name == "ChinaNuclearMissileLauncher");
            var tanks = world.Entities.Where(value => value.Template.Name == "AmericaTankCrusader").OrderBy(value => value.X).ToArray();
            Check.That(catalog.Weapons.TryGet("NeutronMissileWeapon", out var weapon) && catalog.TryGet("NeutronMissile", out var missileTemplate) && missileTemplate.IsInstantiable, "The neutron missile weapon and object exist.");
            world.FireWeaponAt(silo, weapon, null, Fixed.FromInt(3000), Fixed.FromInt(3000), Fixed.Zero);
            Entity? missile = null;
            var maxZ = Fixed.Zero;
            for (var tick = 0; tick < 700; tick++)
            {
                world.Step();
                missile ??= world.Entities.FirstOrDefault(value => value.Template.Name == "NeutronMissile");
                if (missile is not null && missile.Z > maxZ) maxZ = missile.Z;
                if (missile is not null && !world.Entities.Contains(missile)) break;
            }
            Check.That(missile is not null && !world.Entities.Contains(missile), "The real neutron missile flew and was removed after its slow death.");
            Check.That(maxZ > Fixed.FromInt(300), $"It rose high above the target ({maxZ}).");
            Check.That(tanks[0].EffectivelyDead || !world.Entities.Contains(tanks[0]), "The tank at ground zero did not survive.");
            Check.That(tanks[2].Body!.Health == tanks[2].Body!.MaxHealth, "The tank 400 units away is untouched.");
            Console.WriteLine($"       real air: neutron missile peaked at z={maxZ}, tanks at 0/90/400: " + string.Join(" / ", tanks.Select(value => world.Entities.Contains(value) ? value.Body!.Health.ToString() : "destroyed")));
        }
        Nuke();

        void NukeFromSilo()
        {
            // Through the real chain: UseSpecialPowerCommand -> OCLSpecialPower -> SUPERWEAPON_NeutronMissile (FireWeapon nugget) -> the silo is the weapon's source.
            var terrain = CnCNew.Simulation.Terrain.TerrainMap.Build(SyntheticMaps.Build(400, 400, 1, (_, _) => 0), "real-air-silo"); // the silo OCL is created at the map edge near the source: it needs a terrain, as every match and mission has one
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 5, players, "real-air-silo", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = new Dictionary<int, ulong>();
            SubmitResult Send(int player, Func<ulong, SimCommand> make) => world.Submit(make(sequence[player] = sequence.GetValueOrDefault(player) + 1));
            Check.That(Send(1, next => new SpawnCommand(new PlayerId(1), next, world.Tick, "ChinaNuclearMissileLauncher", new PlayerId(1), Fixed.FromInt(1000), Fixed.FromInt(3000), Fixed.Zero, Fixed.Zero)).Accepted, "The silo spawns.");
            Check.That(Send(2, next => new SpawnCommand(new PlayerId(2), next, world.Tick, "AmericaTankCrusader", new PlayerId(2), Fixed.FromInt(3000), Fixed.FromInt(3000), Fixed.Zero, Fixed.Zero)).Accepted, "The target spawns.");
            world.Step();
            var silo = world.Entities.First(value => value.Template.Name == "ChinaNuclearMissileLauncher");
            var tank = world.Entities.First(value => value.Template.Name == "AmericaTankCrusader");
            var power = silo.Modules.OfType<SpecialPowerModuleBase>().First();
            for (var tick = 0; tick < 12000 && !power.IsReady(world); tick++) world.Step();
            Check.That(power.IsReady(world), "The silo's superweapon becomes ready by recharging.");
            var launch = Send(1, next => new UseSpecialPowerCommand(new PlayerId(1), next, world.Tick, silo.Id, "SuperweaponNeutronMissile", SpecialPowerTargetKind.Location, EntityId.Invalid, Fixed.FromInt(3000), Fixed.FromInt(3000)));
            Check.That(launch.Accepted, "The launch command is accepted (" + launch + ").");
            Entity? missile = null;
            for (var tick = 0; tick < 40 && missile is null; tick++)
            {
                world.Step();
                missile = world.Entities.FirstOrDefault(value => value.Template.Name == "NeutronMissile");
            }
            Check.That(missile is not null, "The missile exists shortly after the launch.");
            var dx = missile!.X - silo.X;
            var dy = missile.Y - silo.Y;
            Check.That(dx * dx + dy * dy < Fixed.FromInt(120 * 120), $"It starts at the silo ({dx},{dy} away), not at the target.");
            for (var tick = 0; tick < 900 && world.Entities.Contains(missile); tick++) world.Step();
            Check.That(tank.EffectivelyDead || !world.Entities.Contains(tank), "The target at ground zero is destroyed by the silo's missile.");
        }
        NukeFromSilo();

        var first = Run();
        var second = Run();
        Check.That(first.Hashes.SequenceEqual(second.Hashes), "The real air scenario is deterministic over " + first.Hashes.Length + " ticks.");
        foreach (var line in first.Log) Console.WriteLine("       real air: " + line);
    }
}

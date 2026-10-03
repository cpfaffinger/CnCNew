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
            var weapon = catalog.Weapons[raptor.Template.DefaultWeapon];
            Check.That(weapon.ReloadsAtBase && weapon.ClipSize > 0 && raptor.WeaponClip == weapon.ClipSize, $"The Raptor's weapon returns to base to reload (clip {weapon.ClipSize}).");

            Check.That(Send(1, next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, raptor.Id, Fixed.FromInt(4600) - weapon.AttackRange / Fixed.FromInt(2), Fixed.FromInt(2000))).Accepted, "The Raptor is ordered out.");
            hashes.Add(world.Step().StateHash);
            var phases = new List<JetAIUpdate.JetPhase>();
            for (var tick = 0; tick < 3000 && !(jetAi.AllowAirLoco && jetAi.Phase == JetAIUpdate.JetPhase.None && !raptor.HasMoveTarget); tick++)
            {
                hashes.Add(world.Step().StateHash);
                if (phases.Count == 0 || phases[^1] != jetAi.Phase) phases.Add(jetAi.Phase);
            }
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
            for (var tick = 0; tick < 300; tick++) hashes.Add(world.Step().StateHash);
            return ([..hashes], log);
        }

        var first = Run();
        var second = Run();
        Check.That(first.Hashes.SequenceEqual(second.Hashes), "The real air scenario is deterministic over " + first.Hashes.Length + " ticks.");
        foreach (var line in first.Log) Console.WriteLine("       real air: " + line);
    }
}

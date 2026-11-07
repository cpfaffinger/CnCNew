using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// F-158, the owner's playtest of 7 November 2026: when one Patriot battery fires at a victim, a free neighbour joins in (<c>AssistedTargetingUpdate::assistAttack</c>) and the original
    /// makes its two data stream beams (<c>makeFeedbackLaser</c>). On the shipped Patriot battery the simulation raises both, from the assisting battery, the first to its own place (the
    /// original's parent is always the assisting object, so this beam has no length), the second to the place the victim stood, at 30, 60 and 120 Hz.
    /// </summary>
    public static void RealPatriotsMakeTheirFeedbackLasers(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "America"), new PlayerConfiguration(new PlayerId(2), "China"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "patriots");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "patriots", fingerprint, terrain.Fingerprint), catalog, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "AmericaPatriotBattery", owner: 1, x: 600, y: 600).Accepted, "The first battery spawns.");
            // The neighbour stands within the request range (200) of the first battery but out of reach of the victim (225), so it joins only when asked.
            Check.That(feeder.Spawn(1, "AmericaPatriotBattery", owner: 1, x: 600, y: 790).Accepted, "The second battery spawns.");
            Check.That(feeder.Spawn(1, "AmericaPatriotBattery", owner: 1, x: 1400, y: 600).Accepted, "A distant battery spawns.");
            // A powered base defence is disabled without power (F-16): three batteries need more than one plant gives.
            Check.That(feeder.Spawn(1, "AmericaPowerPlant", owner: 1, x: 500, y: 300).Accepted && feeder.Spawn(1, "AmericaPowerPlant", owner: 1, x: 500, y: 200).Accepted, "The power plants spawn.");
            Check.That(feeder.Spawn(2, "ChinaVehicleDozer", owner: 2, x: 420, y: 600).Accepted, "The victim spawns.");
            feeder.SetRelationship(1, 2, Relationship.Enemies);
            feeder.SetRelationship(2, 1, Relationship.Enemies);
            world.RevealMapPermanently(new PlayerId(1), true);
            for (var index = 0; index < world.Frames(60); index++) world.Step();
            var batteries = world.Entities.Where(value => value.Template.Name == "AmericaPatriotBattery").OrderBy(value => value.Id.Value).ToArray();
            var victim = world.Entities.Single(value => value.Template.Name == "ChinaVehicleDozer");
            var (victimX, victimY) = (victim.X, victim.Y);
            var lasers = new List<SimEvent>();
            for (var index = 0; index < world.Frames(120); index++)
            {
                // The attack run of the game layer: the battery is ordered to fire at its victim every frame until it has (an immobile structure aims first).
                if (world.Time.IsOriginalFrameBoundary(world.Tick) && lasers.Count == 0) feeder.FireWeapon(1, batteries[0].Id.Value, victim.Id.Value);
                var events = world.Step().Events;
                lasers.AddRange(events.Where(value => value.Kind == SimEventKind.AssistFeedbackLaser));
            }
            Check.That(lasers.Count > 0, $"{rate}: the request raised the feedback beams.");
            var neighbour = lasers[0].Entity == batteries[0].Id ? batteries[0] : batteries[1];
            var distant = batteries[2];
            Check.That(lasers.Count % 2 == 0 && lasers.All(value => value.Entity == neighbour.Id || value.Entity == batteries[0].Id || value.Entity == batteries[1].Id) && lasers.All(value => value.Entity != distant.Id), $"{rate}: only the assisting battery makes them, two for a request ({lasers.Count}).");
            Check.That(lasers.All(value => value.Text == "PatriotBinaryDataStream" && value.HasPosition), $"{rate}: the template is the module's LaserFromAssisted, with a place.");
            var toItself = lasers[0];
            var toVictim = lasers[1];
            Check.That(toItself.Other == neighbour.Id && toItself.X == neighbour.X && toItself.Y == neighbour.Y, $"{rate}: the beam from the requester has the assisting battery at both ends.");
            Check.That(toVictim.Other == victim.Id, $"{rate}: the second beam is for the victim.");
            Check.That((toVictim.X - victimX).Abs() < Fixed.FromInt(60) && (toVictim.Y - victimY).Abs() < Fixed.FromInt(60), $"{rate}: it ends where the victim stood ({toVictim.X}, {toVictim.Y}).");
            Check.That(lasers.Take(2).All(value => value.Entity == neighbour.Id), $"{rate}: the first request's two beams belong to the assisting battery.");
        }
    }
}

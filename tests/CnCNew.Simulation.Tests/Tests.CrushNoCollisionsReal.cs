using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// #373 on real data: a hijacker takes a Crusader and rides in it hidden (<c>NO_COLLISIONS</c>); an Overlord then drives over the Crusader. The Overlord crushes the Crusader, but the
    /// hijacker inside has no contact with it and is not squished while it rides. At 60 Hz.
    /// </summary>
    public static void RealCrushSparesAHiddenHijacker(string directory)
    {
        var data = OpenRealFireData(directory);
        var rate = SimulationRate.Hz60;
        var catalog = TemplateCatalog.Build(data.Graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East", Team: 0), new PlayerConfiguration(new PlayerId(2), "West", Team: 0));
        var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "hidden-rider");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-hidden-rider", data.Fingerprint, terrain.Fingerprint), catalog, terrain);
        ulong sequence = 0;
        Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, "AmericaTankCrusader", new PlayerId(1), Fixed.FromInt(500), Fixed.FromInt(600), Fixed.Zero, Fixed.Zero)).Accepted, "the Crusader spawns");
        Check.That(world.Submit(new SpawnCommand(new PlayerId(2), ++sequence, world.Tick, "GLAInfantryHijacker", new PlayerId(2), Fixed.FromInt(470), Fixed.FromInt(600), Fixed.Zero, Fixed.Zero)).Accepted, "the hijacker spawns");
        // The two see each other first (an order to enter needs a target its player sees).
        for (var step = 0; step < world.Frames(2 * SimulationRate.OriginalFramesPerSecond); step++) world.Step();
        var crusader = world.Entities.Single(value => value.Template.Name == "AmericaTankCrusader");
        var man = world.Entities.Single(value => value.Template.Name == "GLAInfantryHijacker");
        var rider = man.Modules.OfType<HijackerUpdate>().Single();
        var enter = world.Submit(new MoveAndEnterCommand(new PlayerId(2), ++sequence, world.Tick, man.Id, crusader.Id));
        Check.That(enter.Accepted, $"the hijack is accepted ({enter.Reason})");
        for (var step = 0; step < world.Frames(10 * SimulationRate.OriginalFramesPerSecond) && !rider.InVehicle; step++) world.Step();
        Check.That(rider.InVehicle && crusader.Owner == new PlayerId(2), $"the hijacker rides in the Crusader it took (riding {rider.InVehicle}, owner {crusader.Owner.Value}).");
        var hidden = ObjectStatusBits.Index("NO_COLLISIONS");
        Check.That(man.HasStatus(hidden), "the riding hijacker is NO_COLLISIONS.");

        // Neither shoots at the other while the Overlord drives through: the run is about the contact only.
        world.SetRelationship(new PlayerId(1), new PlayerId(2), Relationship.Neutral);
        world.SetRelationship(new PlayerId(2), new PlayerId(1), Relationship.Neutral);
        Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, "ChinaTankOverlord", new PlayerId(1), Fixed.FromInt(300), Fixed.FromInt(600), Fixed.Zero, Fixed.Zero)).Accepted, "the Overlord spawns");
        world.Step();
        var overlord = world.Entities.Single(value => value.Template.Name == "ChinaTankOverlord");
        Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++sequence, world.Tick, overlord.Id, Fixed.FromInt(800), Fixed.FromInt(600))).Accepted, "the Overlord's move is accepted");
        var (crusaderCrushed, riderKilled) = (false, false);
        for (var step = 0; step < world.Frames(20 * SimulationRate.OriginalFramesPerSecond); step++)
            foreach (var value in world.Step().Events)
            {
                if (value.Kind != SimEventKind.EntityDied) continue;
                if (value.Entity == crusader.Id && value.Other == overlord.Id) crusaderCrushed = true;
                if (value.Entity == man.Id && man.HasStatus(hidden)) riderKilled = true;
            }
        Check.That(crusaderCrushed, "the Overlord crushes the Crusader it drives over.");
        Check.That(!riderKilled, "the hijacker riding hidden in the Crusader is not killed while it rides.");
    }
}

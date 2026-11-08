using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data (the owner's playtest of 7 November 2026, finding 7): soldiers never climb into a container that is not their player's. <c>TransportContain::isValidContainerFor</c>
    /// lets only the transport's own player's units in ("only OUR OWN units can be transported"); an empty enemy Humvee, Troop Crawler, Battle Bus, Chinook or Helix was entered
    /// because the port tested the relationship of the data alone (<c>AllowEnemiesInside</c> defaults to yes). The pre-check refuses the order for a player (<c>CannotEnterContainer</c>),
    /// the arrival path refuses it too, an own transport, an empty neutral building and a mixed selection behave as <c>canEnterObject</c> says, at 30, 60 and 120 Hz.
    /// </summary>
    public static void RealSoldiersNeverEnterAnEnemyTransport(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var transports = new[] { "AmericaVehicleHumvee", "ChinaVehicleTroopCrawler", "GLAVehicleTechnicalChassisOne", "GLAVehicleBattleBus", "AmericaVehicleChinook", "ChinaVehicleHelix" };
            var soldiers = new[] { "AmericaInfantryRanger", "ChinaInfantryRedguard", "GLAInfantryRebel" };
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "enter");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-enter", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            void Spawn(string template, PlayerId owner, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(owner, ++sequence, world.Tick, template, owner, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
            var row = 0;
            foreach (var transport in transports) { Spawn(transport, new PlayerId(2), 600, 200 + 60 * row); Spawn(transport, new PlayerId(1), 800, 200 + 60 * row); row++; }
            foreach (var soldier in soldiers) Spawn(soldier, new PlayerId(1), 300, 200 + 60 * Array.IndexOf(soldiers, soldier));
            Spawn("GermanTownHouse1", PlayerId.System, 700, 700);
            for (var step = 0; step < world.Frames(1); step++) world.Step();

            SimCommand Enter(Entity actor, Entity container) => new MoveAndEnterCommand(actor.Owner, ++sequence, world.Tick, actor.Id, container.Id);
            foreach (var transport in transports)
            {
                var enemy = world.Entities.Single(e => e.Template.Name == transport && e.Owner.Value == 2);
                var own = world.Entities.Single(e => e.Template.Name == transport && e.Owner.Value == 1);
                foreach (var soldierName in soldiers)
                {
                    var soldier = world.Entities.Single(e => e.Template.Name == soldierName);
                    var refused = world.Check(Enter(soldier, enemy));
                    Check.That(!refused.Accepted && refused.Reason == CommandRejection.CannotEnterContainer, $"{rate}: {soldierName} may not be ordered into an enemy {transport} ({refused.Reason}).");
                    Check.That(!world.CanEnter(soldier, enemy), $"{rate}: the enter test says no for an enemy {transport}.");
                    Check.That(!world.TryEnterContainer(soldier, enemy) && !soldier.ContainedBy.IsValid, $"{rate}: {soldierName} does not get into an enemy {transport} on arrival either.");
                    var counted = world.RejectionCounts[(int)CommandRejection.CannotEnterContainer];
                    Check.That(!world.Submit(Enter(soldier, enemy)).Accepted && world.RejectionCounts[(int)CommandRejection.CannotEnterContainer] > counted, $"{rate}: the submitted order is rejected and counted.");
                }
                // The own one is entered by whoever it takes (the Helix and the Chinook take infantry, the cargo checks aside).
                var ranger = world.Entities.Single(e => e.Template.Name == "AmericaInfantryRanger");
                Check.That(world.CanEnter(ranger, own) == world.Check(Enter(ranger, own)).Accepted, $"{rate}: the pre-check of an own {transport} agrees with the enter test.");
            }

            var ownHumvee = world.Entities.Single(e => e.Template.Name == "AmericaVehicleHumvee" && e.Owner.Value == 1);
            var ownRanger = world.Entities.Single(e => e.Template.Name == "AmericaInfantryRanger");
            Check.That(world.Check(Enter(ownRanger, ownHumvee)).Accepted && world.CanEnter(ownRanger, ownHumvee), $"{rate}: a Ranger may be ordered into an own Humvee.");

            // A neutral building nobody holds is garrisoned as before; the same soldiers, the same order.
            var house = world.Entities.Single(e => e.Template.Name == "GermanTownHouse1");
            Check.That(world.Check(Enter(ownRanger, house)).Accepted && world.CanEnter(ownRanger, house), $"{rate}: an empty neutral building may be entered.");

            // The enemy's Battle Bus takes nobody: not the infantry, not a vehicle (the Game tests pick the mixed selection with the pointer).
            var enemyBus = world.Entities.Single(e => e.Template.Name == "GLAVehicleBattleBus" && e.Owner.Value == 2);
            Check.That(soldiers.All(name => !world.CanEnter(world.Entities.Single(e => e.Template.Name == name), enemyBus)) && !world.CanEnter(ownHumvee, enemyBus), $"{rate}: nobody can enter the enemy's Battle Bus.");
            // TransportContain takes only its own player's units: an ally's Humvee is no shelter either (canEnterObject ends in TransportContain::isValidContainerFor).
            foreach (var (from, to) in new[] { (1, 2), (2, 1) })
                Check.That(world.Submit(new SetRelationshipCommand(PlayerId.System, ++sequence, world.Tick, new PlayerId(from), new PlayerId(to), Relationship.Allies)).Accepted, "the two players ally.");
            var alliedHumvee = world.Entities.Single(e => e.Template.Name == "AmericaVehicleHumvee" && e.Owner.Value == 2);
            Check.That(!world.CanEnter(ownRanger, alliedHumvee) && world.Check(Enter(ownRanger, alliedHumvee)).Reason == CommandRejection.CannotEnterContainer, $"{rate}: an ally's transport is not entered either.");
        }
    }
}

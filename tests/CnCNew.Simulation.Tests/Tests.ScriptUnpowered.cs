using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// The script statuses "disabled" and "unpowered" (<c>Object::setScriptStatus</c>, <c>BuildAssistant::canMakeUnit</c>). The port kept the unpowered status and read it nowhere.
/// </summary>
static partial class Tests
{
    /// <summary>
    /// A map's <c>objectPowered</c> no: the object is disabled as <c>DISABLED_SCRIPT_UNDERPOWERED</c> (a power plant takes its production out by its disabled edge), a factory refuses to
    /// queue (<c>CANMAKE_FACTORY_IS_DISABLED</c>, as for a script disable), a human player's dozer refuses to build while a computer player's builds ("the ai will validate, or cheat").
    /// </summary>
    public static void ScriptUnpoweredMakesNothing()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: ConstructionFiles());
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta", IsComputer: true)];
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.GiveMoney(1, 1000);
        feeder.GiveMoney(2, 1000);
        ulong sequence = 100;
        PlaceMapObjectCommand Place(string template, int owner, int x, int y, MapObjectFlags flags)
            => new(PlayerId.System, sequence++, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero, 100, flags, 0);
        Check.That(world.Submit(Place("Barracks", 1, 100, 100, MapObjectFlags.Unpowered)).Accepted && world.Submit(Place("Plant", 1, 200, 100, MapObjectFlags.Unpowered)).Accepted
                   && world.Submit(Place("Dozer", 1, 300, 100, MapObjectFlags.Unpowered)).Accepted && world.Submit(Place("Dozer", 2, 300, 300, MapObjectFlags.Unpowered)).Accepted,
            "Setup: the system places the unpowered objects.");
        world.Step();
        world.Step();
        Entity Of(string template, int owner) => world.Entities.Single(entity => entity.Template.Name == template && entity.Owner.Value == owner);
        var barracks = Of("Barracks", 1);
        var plant = Of("Plant", 1);
        Check.That(world.DisabledFlagsOf(barracks) == DisabledMask.ScriptUnderpowered && world.DisabledFlagsOf(plant) == DisabledMask.ScriptUnderpowered,
            $"An object a map leaves unpowered is disabled as DISABLED_SCRIPT_UNDERPOWERED ({world.DisabledFlagsOf(barracks)}).");
        Check.That(world.ProducedEnergyOf(new PlayerId(1)) == 0, $"A script-unpowered plant produces nothing: its disabled edge took it out ({world.ProducedEnergyOf(new PlayerId(1))}).");

        var queue = new QueueProductionCommand(new PlayerId(1), 1, world.Tick, barracks.Id, "Grunt");
        Check.That(world.Check(queue).Reason == CommandRejection.FactoryIsDisabled && feeder.QueueProduction(1, barracks.Id.Value, "Grunt").Reason == CommandRejection.FactoryIsDisabled,
            "An unpowered factory refuses to queue: CANMAKE_FACTORY_IS_DISABLED.");
        Check.That(feeder.Construct(1, Of("Dozer", 1).Id.Value, "Plant", 100, 300).Reason == CommandRejection.FactoryIsDisabled,
            "A human player's unpowered dozer builds nothing (DozerAIUpdate::construct asks canMakeUnit).");
        Check.That(feeder.Construct(2, Of("Dozer", 2).Id.Value, "Plant", 400, 300).Accepted, "A computer player's unpowered dozer is not asked.");

        world.SetScriptDisable(barracks, Entity.ScriptUnpowered, false);
        world.SetScriptDisable(plant, Entity.ScriptUnpowered, false);
        world.Step();
        Check.That(world.DisabledFlagsOf(barracks) == DisabledMask.None && world.ProducedEnergyOf(new PlayerId(1)) == 5, "Powered again, the plant produces.");
        Check.That(feeder.QueueProduction(1, barracks.Id.Value, "Grunt").Accepted, "Powered again, the factory queues.");
        world.SetScriptDisable(barracks, Entity.ScriptDisabled, true);
        Check.That(feeder.QueueProduction(1, barracks.Id.Value, "Grunt").Reason == CommandRejection.FactoryIsDisabled, "A script-disabled factory refuses for the same reason.");
        Check.That(world.RejectionCounts[(int)CommandRejection.FactoryIsDisabled] == 3, $"And every refusal is counted ({world.RejectionCounts[(int)CommandRejection.FactoryIsDisabled]}).");

        // ProductionUpdate processes only DISABLED_HELD: a queue that stands when the power is taken goes on when it comes back.
        var retail = new SyntheticFixture(TestRate.Default, files: RetailModuleFiles());
        var factoryWorld = NewWorld(retail);
        var factoryFeeder = new Feeder(factoryWorld);
        factoryFeeder.Spawn(1, "DefaultExitFactory", x: 300, y: 300);
        factoryWorld.Step();
        factoryFeeder.GiveMoney(1, 1000);
        factoryWorld.Step();
        var factory = factoryWorld.Entities.Single(entity => entity.Template.Name == "DefaultExitFactory");
        Check.That(factoryFeeder.QueueProduction(1, factory.Id.Value, "QuickUnit").Accepted, "Setup: the factory queues its unit.");
        factoryWorld.Step();
        factoryWorld.SetScriptDisable(factory, Entity.ScriptUnpowered, true);
        for (var tick = 0; tick < factoryWorld.Frames(90); tick++) factoryWorld.Step();
        Check.That(CountOf(factoryWorld, "QuickUnit") == 0, "An unpowered factory's queue stands: ProductionUpdate does not process DISABLED_SCRIPT_UNDERPOWERED.");
        factoryWorld.SetScriptDisable(factory, Entity.ScriptUnpowered, false);
        for (var tick = 0; tick < factoryWorld.Frames(90); tick++) factoryWorld.Step();
        Check.That(CountOf(factoryWorld, "QuickUnit") == 1, "Powered again, it makes its unit.");
    }
}

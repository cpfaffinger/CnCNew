using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// <c>RebuildHoleExposeDie::onDie</c> with <c>TransferAttackers</c> (<c>AIUpdateInterface::transferAttack</c>): a unit that attacks a structure which dies into a rebuild hole attacks the hole,
    /// at 30, 60 and 120 Hz; without the transfer it would stand, as an idle unit does not choose a building.
    /// </summary>
    public static void AttackersGoOverToTheHole()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var files = new Dictionary<string, string>(RetailModuleFiles());
            foreach (var (key, value) in AcquisitionFiles()) files.TryAdd(key, value);
            var fixture = new SyntheticFixture(rate, files: files);
            ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "AcqSlowTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "Shack", owner: 2, x: 100, y: 0);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var tank = world.Entities.Single(value => value.Template.Name == "AcqSlowTank");
            var shack = world.Entities.Single(value => value.Template.Name == "Shack");
            Check.That(feeder.Pursue(1, tank.Id.Value, shack.Id.Value, 100, 0).Accepted, $"{rate}: the attack on the shack is accepted.");
            for (var frame = 0; frame < 10; frame++)
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            Check.That(world.AcquiredVictimOf(tank) == shack.Id, $"{rate}: the tank attacks the shack.");
            feeder.Kill(shack.Id.Value);
            for (var frame = 0; frame < 3; frame++)
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var hole = world.Entities.Single(value => value.Template.Name == "ShackHole");
            Check.That(world.AcquiredVictimOf(tank) == hole.Id, $"{rate}: the tank attacks the hole the shack left ({world.AcquiredVictimOf(tank).Value}, the hole {hole.Id.Value}).");
        }
    }
}

using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static Dictionary<string, string> StepAsideFiles() => new(GroupMoveFiles()) { ["Data/INI/Object/Reverse.ini"] = ReverseIni };

    /// <summary>
    /// <c>AIUpdateInterface::processCollision</c>: a vehicle on its way tells idle infantry that stands in it to move out of its way (<c>aiMoveAwayFromUnit</c>), its own player's or an ally's (an enemy is no way a path goes through); infantry at work,
    /// and infantry in front of infantry, stay. The place is the same at the end of every frame at 30, 60 and 120 Hz, and a save inside the frame of the request keeps the request.
    /// </summary>
    public static void InfantryStepsAsideForAVehicle()
    {
        (Fixed X, Fixed Y)[]? thirty = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder, _) = RatedWorld(rate, StepAsideFiles(), NavTerrain());
            feeder.Spawn(1, "ForwardCar", x: 20, y: 100);
            feeder.Spawn(1, "Marcher", x: 70, y: 102);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var car = world.Entities.Single(value => value.Template.Name == "ForwardCar");
            var man = world.Entities.Single(value => value.Template.Name == "Marcher");
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), 1000, world.Tick, car.Id, Fixed.FromInt(170), Fixed.FromInt(100))).Accepted, "The move is accepted.");
            var places = new List<(Fixed X, Fixed Y)>();
            for (var frame = 0; frame < 600; frame++)
            {
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
                places.Add((man.X, man.Y));
            }
            var moved = places.FindIndex(place => place != places[0]) >= 0 || places[0] != (Fixed.FromInt(70), Fixed.FromInt(102));
            Check.That(moved && (man.Y - Fixed.FromInt(100)).Abs() > Fixed.FromInt(10), $"{rate}: the infantry has stepped out of the car's way (it stands at {man.X}, {man.Y}).");
            Check.That((car.X - Fixed.FromInt(170)).Abs() < Fixed.FromInt(20) && (car.Y - Fixed.FromInt(100)).Abs() < Fixed.FromInt(20), $"{rate}: the car has gone on ({car.X}, {car.Y}; the infantry at {man.X}, {man.Y}; moving {man.HasMoveTarget}; car moving {car.HasMoveTarget}).");
            if (thirty is null) thirty = [.. places];
            else Check.That(places.SequenceEqual(thirty), $"{rate}: the infantry stands where the 30 Hz one stands at the end of every frame.");
        }

        // Infantry does not tell infantry to make way.
        var (walkers, walkerFeeder, _) = RatedWorld(SimulationRate.Hz30, StepAsideFiles(), NavTerrain());
        walkerFeeder.Spawn(1, "Marcher", x: 20, y: 100);
        walkerFeeder.Spawn(1, "Marcher", x: 70, y: 102);
        walkers.Step();
        var walker = walkers.Entities.First(value => value.X == Fixed.FromInt(20));
        var stander = walkers.Entities.First(value => value.X == Fixed.FromInt(70));
        Check.That(walkers.Submit(new SetMoveTargetCommand(new PlayerId(1), 1000, walkers.Tick, walker.Id, Fixed.FromInt(170), Fixed.FromInt(100))).Accepted, "The walk is accepted.");
        for (var step = 0; step < 200; step++) walkers.Step();
        Check.That(stander.X == Fixed.FromInt(70) && stander.Y == Fixed.FromInt(102), $"Infantry passes infantry; nobody steps aside ({stander.X}, {stander.Y}).");
    }

    /// <summary>A request made in the first tick of a frame above 30 Hz is answered at the frame's end; a save between the two keeps it, and the restored world goes on as the one that was saved.</summary>
    public static void AStepAsideRequestSurvivesASave()
    {
        var rate = SimulationRate.Hz60;
        var (world, feeder, fixture) = RatedWorld(rate, StepAsideFiles(), NavTerrain());
        feeder.Spawn(1, "ForwardCar", x: 20, y: 100);
        feeder.Spawn(1, "Marcher", x: 70, y: 102);
        for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
        var car = world.Entities.Single(value => value.Template.Name == "ForwardCar");
        Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), 1000, world.Tick, car.Id, Fixed.FromInt(170), Fixed.FromInt(100))).Accepted, "The move is accepted.");
        var checkedTicks = 0;
        for (var step = 0; step < world.Frames(120); step++)
        {
            world.Step();
            if (world.Tick % rate.TicksPerOriginalFrame == 0) continue;
            var restored = CnCNew.Simulation.Snapshots.WorldSnapshot.Restore(CnCNew.Simulation.Snapshots.WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, NavTerrain());
            for (var more = 0; more < rate.TicksPerOriginalFrame * 3; more++)
            {
                var a = world.Step().StateHash;
                var b = restored.Step().StateHash;
                Check.That(a == b, $"Saved at tick {world.Tick - more - 1}: the restored world goes on alike ({a:X16} / {b:X16}).");
            }
            checkedTicks++;
            if (checkedTicks > 80) break;
        }
    }
}

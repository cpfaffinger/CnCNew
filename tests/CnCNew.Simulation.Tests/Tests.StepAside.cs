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

    /// <summary>
    /// F-167: a step-aside request of a vehicle is decided at the end of the vehicle's frame, after the spawn phase of an earlier tick of that frame has created an infantry unit above 30 Hz; at
    /// 30 Hz the spawn phase decides claims first and creates objects after them, so the new infantry is not asked in that frame. A pilot that came out of a jet in the frame was asked at 60
    /// Hz and not at 30 (Armored Fury America against China, frame 18,391). Infantry created in the first tick of every frame in which the car's request may fall ends at the same place at every rate.
    /// </summary>
    public static void InfantryCreatedInTheFrameOfARequestIsNotAskedToStepAside()
    {
        (Fixed X, Fixed Y)? Run(SimulationRate rate, int frame, int tickInFrame)
        {
            var (world, feeder, _) = RatedWorld(rate, StepAsideFiles(), NavTerrain());
            feeder.Spawn(1, "ForwardCar", x: 20, y: 100);
            feeder.Spawn(1, "Marcher", x: 70, y: 102);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var car = world.Entities.Single(value => value.Template.Name == "ForwardCar");
            Check.That(feeder.SetMoveTarget(1, car.Id.Value, 170, 100).Accepted, "The move is accepted.");
            for (var tick = 0; tick < frame * rate.TicksPerOriginalFrame + tickInFrame; tick++) world.Step();
            var spawned = feeder.Spawn(1, "Marcher", x: 60, y: 98);
            Check.That(spawned.Accepted, "The infantry is created: " + spawned.Reason + ".");
            for (var tick = 0; tick < 150 * rate.TicksPerOriginalFrame; tick++) world.Step();
            var man = world.Entities.OrderBy(value => value.Id.Value).Last(value => value.Template.Name == "Marcher");
            return (man.X, man.Y);
        }
        var asked = 0;
        for (var frame = 0; frame < 40; frame++)
        {
            var reference = Run(SimulationRate.Hz30, frame, 0);
            if (reference is { } place && place != (Fixed.FromInt(60), Fixed.FromInt(98))) asked++;
            foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
            {
                // Created in the first tick of the frame, as the pilot was: the car's frame ends in a later tick, where the request is decided.
                var other = Run(rate, frame, 0);
                Check.That(other == reference, $"{rate}: infantry created in the first tick of frame {frame} after the move order stands at {other} instead of {reference}.");
            }
        }
        Check.That(asked > 0, "Setup: the infantry in the car's way is asked to step aside at 30 Hz when it is created in some of the frames.");
    }

    /// <summary>
    /// <c>INFANTRY_MOVES_THROUGH_INFANTRY</c>: "Infantry can run through infantry". Infantry whose way crosses a row of standing infantry plans no way round it and goes straight on, the same at the end of
    /// every frame at 30, 60 and 120 Hz; a vehicle goes round the same row.
    /// </summary>
    public static void InfantryPlansNoWayRoundInfantry()
    {
        (Fixed Walked, (Fixed X, Fixed Y)[] Places) Walk(SimulationRate rate, string walker)
        {
            var (world, feeder, _) = RatedWorld(rate, StepAsideFiles(), NavTerrain());
            feeder.Spawn(1, walker, x: 20, y: 100);
            for (var y = 70; y <= 130; y += 15) feeder.Spawn(1, "Marcher", x: 80, y: y);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var mover = world.Entities.Single(value => value.Template.Name == walker && value.X == Fixed.FromInt(20));
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), 1000, world.Tick, mover.Id, Fixed.FromInt(170), Fixed.FromInt(100))).Accepted, "The walk is accepted.");
            var places = new List<(Fixed X, Fixed Y)>();
            var walked = Fixed.Zero;
            var (lastX, lastY) = (mover.X, mover.Y);
            for (var frame = 0; frame < 600; frame++)
            {
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
                places.Add((mover.X, mover.Y));
                walked += Fixed.Sqrt((mover.X - lastX) * (mover.X - lastX) + (mover.Y - lastY) * (mover.Y - lastY));
                (lastX, lastY) = (mover.X, mover.Y);
            }
            Check.That((mover.X - Fixed.FromInt(170)).Abs() < Fixed.FromInt(15) && (mover.Y - Fixed.FromInt(100)).Abs() < Fixed.FromInt(15), $"{rate}: the {walker} arrived ({mover.X}, {mover.Y}).");
            return (walked, [.. places]);
        }

        (Fixed X, Fixed Y)[]? thirty = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (walked, places) = Walk(rate, "Marcher");
            Check.That(walked < Fixed.FromInt(165), $"{rate}: the infantry walked {walked} for 150 straight, through the row.");
            if (thirty is null) thirty = places;
            else Check.That(places.SequenceEqual(thirty), $"{rate}: the infantry stands where the 30 Hz one stands at the end of every frame.");
        }
        var (driven, _) = Walk(SimulationRate.Hz30, "ForwardCar");
        Check.That(driven > Fixed.FromInt(165), $"A car goes round the row ({driven} for 150 straight).");
    }

    /// <summary>
    /// <c>Pathfinder::moveAllies</c> weighed against the way round (F-125): a car whose only way leads through a gap in a wall where an idle allied car stands has that car move off its way and
    /// drives through; on open ground the same car goes round the one that stands, which stays where it is. The places at the end of every frame are the same at 30, 60 and 120 Hz.
    /// </summary>
    public static void AlliesMakeWayWhenTheWayRoundCostsMore()
    {
        var wall = Enumerable.Range(0, NavCells).Where(y => y is < 9 or > 11).Select(y => (10, y)).ToArray();
        (bool Arrived, Fixed StanderMoved, (Fixed X, Fixed Y)[] Places) Drive(SimulationRate rate, bool walled)
        {
            var (world, feeder, _) = RatedWorld(rate, StepAsideFiles(), walled ? NavTerrain(wall) : NavTerrain());
            feeder.Spawn(1, "ForwardCar", x: 40, y: 105);
            feeder.Spawn(1, "ForwardCar", x: 105, y: 105);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var car = world.Entities.Single(value => value.Template.Name == "ForwardCar" && value.X == Fixed.FromInt(40));
            var stander = world.Entities.Single(value => value.Template.Name == "ForwardCar" && value.Id != car.Id);
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), 1000, world.Tick, car.Id, Fixed.FromInt(170), Fixed.FromInt(105))).Accepted, "The move is accepted.");
            var places = new List<(Fixed X, Fixed Y)>();
            var arrived = false;
            for (var frame = 0; frame < 900; frame++)
            {
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
                places.Add((car.X, car.Y));
                places.Add((stander.X, stander.Y));
                if (!car.HasMoveTarget && (car.X - Fixed.FromInt(170)).Abs() < Fixed.FromInt(20) && (car.Y - Fixed.FromInt(105)).Abs() < Fixed.FromInt(20)) arrived = true;
            }
            var (dx, dy) = (stander.X - Fixed.FromInt(105), stander.Y - Fixed.FromInt(105));
            return (arrived, Fixed.Sqrt(dx * dx + dy * dy), [.. places]);
        }

        foreach (var walled in new[] { true, false })
        {
            (Fixed X, Fixed Y)[]? thirty = null;
            foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
            {
                var (arrived, moved, places) = Drive(rate, walled);
                Check.That(arrived, $"{rate}, {(walled ? "a gap" : "open ground")}: the car arrived.");
                if (walled) Check.That(moved > Fixed.FromInt(10), $"{rate}: the car in the gap made way (it moved {moved}).");
                else Check.That(moved == Fixed.Zero, $"{rate}: on open ground the car that stands stays (it moved {moved}).");
                if (thirty is null) thirty = places;
                else Check.That(places.SequenceEqual(thirty), $"{rate}, {(walled ? "a gap" : "open ground")}: both cars stand where the 30 Hz ones stand at the end of every frame.");
            }
        }
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

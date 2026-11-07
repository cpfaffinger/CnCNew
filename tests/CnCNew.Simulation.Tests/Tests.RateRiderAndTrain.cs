using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// F-144: a rider that turns inside its container - the pilot under its parachute walks toward a vehicle while it comes down - covers the turn of a frame in the frame's shares, as
    /// it does in the single 30 Hz step. Before, above 30 Hz the collision test that is made at the frame's first tick took the rider's being contained for a container it had just
    /// entered, dropped the rest of the frame and decided a whole new turn in the next tick: the rider turned once per tick instead of once per frame.
    /// </summary>
    public static void AContainedRiderTurnsOncePerFrame()
    {
        List<long>? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder) = FlightRateWorld(rate, ScriptFiles());
            feeder.Spawn(1, "AlphaChute", owner: 1, x: 500, y: 500, z: 300);
            feeder.Spawn(1, "AlphaTrooper", owner: 1, x: 500, y: 500);
            world.Step();
            var chute = Only(world, "AlphaChute");
            var trooper = Only(world, "AlphaTrooper");
            chute.Modules.OfType<ParachuteContain>().Single().Add(world, trooper);
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            trooper.Modules.OfType<AIUpdateInterface>().Single().OrderMove(world, Fixed.FromInt(400), Fixed.FromInt(500), false);
            var track = new List<long>();
            for (var frame = 0; frame < 12; frame++)
            {
                for (var tick = 0; tick < world.Frames(1); tick++) world.Step();
                Check.That(trooper.ContainedBy.IsValid, $"{rate}: the rider is still under the parachute in frame {frame}.");
                track.Add(trooper.Angle.Raw);
            }
            Check.That(track.Distinct().Count() > 3, $"{rate}: the rider turns step by step ({string.Join(", ", track)}).");
            if (reference is null) reference = track;
            else Check.That(track.SequenceEqual(reference), $"{rate}: the rider has turned as at 30 Hz at the end of each frame ({string.Join(", ", track)} against {string.Join(", ", reference)}).");
        }
    }
}

static partial class Tests
{
    /// <summary>
    /// F-145: what a locomotive kills, crushes or shoves is hit in the first tick of its logic frame, where its whole 30 Hz step is taken, so that an object that decides after the
    /// train in that tick finds it dead, as at 30 Hz. Before, the train covered its step tick by tick and answered in the frame's last tick: a victim that updates after the train
    /// reacted to its death a frame later than at 30 Hz.
    /// </summary>
    public static void ATrainHitsInTheFirstTickOfItsFrame()
    {
        List<(long Frame, SimEventKind Kind, uint Entity)>? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, fixture, _) = RatedStructuresWorld(rate);
            RequestSpawn(world, fixture, "SwHeavy", 2, 95, 104);
            RequestSpawn(world, fixture, "SwTrooper", 2, 300, 100);
            RequestSpawn(world, fixture, "SwLoco", 0, 50, 100);
            var hits = new List<(long Frame, SimEventKind Kind, uint Entity)>();
            var perFrame = world.Time.TicksPerOriginalFrame;
            for (var step = 0; step < world.Frames(400); step++)
            {
                var tick = world.Tick;
                foreach (var item in world.Step().Events)
                {
                    if (item.Kind is not (SimEventKind.TrainWhistle or SimEventKind.EntityDamaged or SimEventKind.EntityDied)) continue;
                    Check.That(tick % perFrame == 0, $"{rate}: the train hits in the first tick of its frame ({item.Kind} in tick {tick}).");
                    hits.Add((tick / perFrame, item.Kind, item.Entity.Value));
                }
            }
            Check.That(hits.Count > 3, $"{rate}: the train hits something ({hits.Count}).");
            if (reference is null) reference = hits;
            else Check.That(hits.SequenceEqual(reference), $"{rate}: the train hits in the frames of 30 Hz.");
        }
    }
}

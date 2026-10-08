using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, third round (<c>docs/RATE-INVARIANCE-ROUND3-FLIGHT.md</c> §2): flight and special movers publish the rest of the frame they are flying (<c>Entity.AheadX/Y/Z</c>), so
/// an observer reads them where their frame ends (<c>Entity.FrameEnd</c>) – the position the 30 Hz step has given them when a 30 Hz observer looks. The tests name their rates.
/// </summary>
static partial class Tests
{
    private readonly record struct ObserverSample(bool There, Fixed ReadX, Fixed ReadY, Fixed ReadZ, Fixed X, Fixed Y, Fixed Z, bool Inside);

    /// <summary>
    /// Steps <paramref name="frames"/> logic frames of the original and takes, after every tick, what an observer of the next tick reads of the object (<c>FrameEnd</c>) and where
    /// the object is. <paramref name="script"/> runs where each frame of the track begins.
    /// </summary>
    private static List<ObserverSample> ObserverTrack(SimulationWorld world, Func<Entity?> who, int frames, Action<int>? script)
    {
        var samples = new List<ObserverSample>();
        for (var frame = 0; frame < frames; frame++)
        {
            script?.Invoke(frame);
            for (var tick = 0; tick < world.Time.TicksPerOriginalFrame; tick++)
            {
                world.Step();
                var entity = who();
                if (entity is null || world.FindEntity(entity.Id) != entity) { samples.Add(default); continue; }
                var (x, y, z) = entity.FrameEnd(world.Tick);
                samples.Add(new ObserverSample(true, x, y, z, entity.X, entity.Y, entity.Z, entity.IsInsideFrameStep));
            }
        }
        return samples;
    }

    private static Fixed Apart(Fixed ax, Fixed ay, Fixed az, Fixed bx, Fixed by, Fixed bz)
    {
        var (dx, dy, dz) = (bx - ax, by - ay, bz - az);
        return Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// One mover at the three rates. (1) At 30 Hz nothing is published. (2) Above, in every tick the reading is the position the object has when the frame it is flying is flown
    /// (bit for bit), and the object does publish. (3) The reading after the first tick of frame k is the 30 Hz position after tick k, within <paramref name="units"/>.
    /// </summary>
    private static void ObserverReadsTheFrameEnd(string what, Func<SimulationRate, (SimulationWorld World, Func<Entity?> Who, Action<int>? Script)> stage, int frames, decimal units)
    {
        var tracks = new List<ObserverSample>[FlightRates.Length];
        var published = new int[FlightRates.Length];
        var worstInside = new Fixed[FlightRates.Length];
        var worstAcross = new Fixed[FlightRates.Length];
        var momentary = new Fixed[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, who, script) = stage(FlightRates[index]);
            var stride = world.Time.TicksPerOriginalFrame;
            var track = tracks[index] = ObserverTrack(world, who, frames, script);
            published[index] = track.Count(sample => sample.Inside);
            for (var at = 0; at < track.Count; at++)
            {
                if (!track[at].There || !track[at].Inside) continue;
                var end = at;
                while (end < track.Count && track[end].There && track[end].Inside) end++;
                if (end == track.Count || !track[end].There) continue; // the track or the object ended inside the frame
                if (end - at >= stride) FlightFailures.Add($"{what}: at {FlightRates[index]} a published rest is not flown within a frame (tick {at} of the track)");
                var off = Apart(track[at].ReadX, track[at].ReadY, track[at].ReadZ, track[end].X, track[end].Y, track[end].Z);
                worstInside[index] = Fixed.Max(worstInside[index], off);
                momentary[index] = Fixed.Max(momentary[index], Apart(track[at].X, track[at].Y, track[at].Z, track[end].X, track[end].Y, track[end].Z));
            }
            for (var frame = 0; frame < frames && index > 0; frame++)
            {
                var (mine, reference) = (track[frame * stride], tracks[0][frame]);
                if (mine.There != reference.There) { FlightFailures.Add($"{what}: at {FlightRates[index]} the object {(mine.There ? "exists" : "is gone")} after the first tick of frame {frame + 1}, at 30 Hz it {(reference.There ? "exists" : "is gone")}"); break; }
                if (mine.There) worstAcross[index] = Fixed.Max(worstAcross[index], Apart(mine.ReadX, mine.ReadY, mine.ReadZ, reference.X, reference.Y, reference.Z));
            }
        }
        Console.WriteLine($"       {what}: ticks with a published rest {published[0]} / {published[1]} / {published[2]}; the reading is off the end of the frame by at most {Units(worstInside[1])} / {Units(worstInside[2])} units " +
                          $"(the momentary position by {Units(momentary[1])} / {Units(momentary[2])}); off the 30 Hz position by at most {Units(worstAcross[1])} / {Units(worstAcross[2])} (60 / 120 Hz)");
        if (published[0] != 0) FlightFailures.Add($"{what}: a rest is published at 30 Hz ({published[0]} ticks)");
        for (var index = 1; index < FlightRates.Length; index++)
        {
            if (published[index] == 0) FlightFailures.Add($"{what}: nothing is published at {FlightRates[index]}");
            if (worstInside[index] != Fixed.Zero) FlightFailures.Add($"{what}: at {FlightRates[index]} an observer reads the object up to {Units(worstInside[index])} units from where its frame ends");
            if (worstAcross[index].ToDecimal() > units) FlightFailures.Add($"{what}: at {FlightRates[index]} an observer reads the object up to {Units(worstAcross[index])} units from the 30 Hz position (allowed {Units(Fixed.FromDecimal(units))})");
        }
    }

    /// <summary>
    /// What an observer reads of a flying object at 30, 60 and 120 Hz: a jet (taxi, take-off, winged flight, circling, landing), a helicopter (climb and flight), a Chinook
    /// (climb while it flies), a parachute with its rider, a shot-down jet and a shot-down helicopter, the Spectre gunship and a countermeasure flare.
    /// </summary>
    public static void RateFlightObserversReadTheFrameEnd()
    {
        ObserverReadsTheFrameEnd("jet at its airfield and in winged flight", rate =>
        {
            var (world, feeder) = FlightRateWorld(rate, WingAirfieldFiles, terrain: false);
            feeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
            StepFrames(world, 1);
            feeder.GiveMoney(1, 1000);
            StepFrames(world, 1);
            var airfield = Only(world, "AirTestAirfield");
            Entity? jet = null;
            Entity? Who() => jet ??= world.Entities.SingleOrDefault(entity => entity.Template.Name == "AirTestJet");
            return (world, Who, frame =>
            {
                if (frame == 0) feeder.QueueProduction(1, airfield.Id.Value, "AirTestJet");
                if (frame == 100) feeder.SetMoveTarget(1, Who()!.Id.Value, 1200, 300);
                if (frame == 320) feeder.SetMoveTarget(1, Who()!.Id.Value, 200, 900);
            });
        }, 1100, 0m);

        ObserverReadsTheFrameEnd("helicopter of a helipad", rate =>
        {
            var (world, feeder) = FlightRateWorld(rate, AirFixtureFiles, terrain: false);
            feeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
            StepFrames(world, 1);
            feeder.GiveMoney(1, 1000);
            StepFrames(world, 1);
            var airfield = Only(world, "AirTestAirfield");
            Entity? heli = null;
            Entity? Who() => heli ??= world.Entities.SingleOrDefault(entity => entity.Template.Name == "AirTestHeli");
            return (world, Who, frame =>
            {
                if (frame == 0) feeder.QueueProduction(1, airfield.Id.Value, "AirTestHeli");
                if (frame == 150) feeder.SetMoveTarget(1, Who()!.Id.Value, 903, 541);
                if (frame == 230) feeder.SetMoveTarget(1, Who()!.Id.Value, 377, 811);
            });
        }, 600, 0m);

        ObserverReadsTheFrameEnd("Chinook, climbing while it flies", rate =>
        {
            var (world, feeder) = FlightRateWorld(rate, SpiralFiles(), terrain: false);
            feeder.Spawn(1, "Lifter", x: 0, y: 0);
            StepFrames(world, 1);
            var chinook = Only(world, "Lifter");
            return (world, () => chinook, frame =>
            {
                if (frame == 5) feeder.SetMoveTarget(1, chinook.Id.Value, 300, 170);
            });
        }, 120, 0.001m);

        foreach (var rider in new[] { false, true })
            ObserverReadsTheFrameEnd(rider ? "rider of a steered parachute" : "steered parachute", rate =>
            {
                var (world, feeder) = FlightRateWorld(rate, ScriptFiles());
                feeder.Spawn(1, "AlphaChute", owner: 1, x: 500, y: 500, z: 100);
                feeder.Spawn(1, "AlphaTrooper", owner: 1, x: 500, y: 500);
                world.Step();
                var chute = Only(world, "AlphaChute");
                var trooper = Only(world, "AlphaTrooper");
                var module = chute.Modules.OfType<ParachuteContain>().Single();
                module.Add(world, trooper);
                module.SetOverrideDestination(Fixed.FromInt(560), Fixed.FromInt(580));
                return (world, () => rider ? trooper : chute, null);
            }, rider ? 100 : 130, 0.001m);

        ObserverReadsTheFrameEnd("shot-down jet", rate =>
        {
            var (world, feeder) = FlightRateWorld(rate, ScriptFiles());
            feeder.Spawn(1, "AlphaPlane", owner: 1, x: 500, y: 500, z: 45);
            world.Step();
            StepFrames(world, 4);
            var plane = Only(world, "AlphaPlane");
            feeder.SetMoveTarget(1, plane.Id.Value, 2500, 500);
            StepFrames(world, 60);
            feeder.Kill(plane.Id.Value);
            world.Step();
            return (world, () => plane, null);
        }, 60, 0.001m);

        ObserverReadsTheFrameEnd("shot-down helicopter", rate =>
        {
            var (world, feeder) = FlightRateWorld(rate, SpiralFiles(), terrain: false);
            feeder.Spawn(1, "Spinner", x: 500, y: 500, z: 100);
            world.Step();
            StepFrames(world, 30);
            var spinner = Only(world, "Spinner");
            feeder.Kill(spinner.Id.Value);
            StepFrames(world, 1);
            return (world, () => spinner, null);
        }, 120, 0.001m);

        ObserverReadsTheFrameEnd("Spectre gunship", rate =>
        {
            var (world, feeder) = FlightRateWorld(rate, AirWeaponFixtureFiles, terrain: false);
            feeder.Spawn(1, "AirGunship", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "AirDummy", owner: 2, x: 405, y: 5);
            StepFrames(world, 125);
            var ship = Only(world, "AirGunship");
            while (!world.Time.IsOriginalFrameBoundary(world.Tick + 1)) world.Step();
            feeder.UseSpecialPower(1, ship.Id.Value, "AirGunshipPower", CnCNew.Simulation.Commands.SpecialPowerTargetKind.Location, x: 400, y: 0);
            world.Step();
            return (world, () => ship, null);
        }, 180, 0.001m);

        ObserverReadsTheFrameEnd("countermeasure flare", rate =>
        {
            var (world, feeder) = FlightRateWorld(rate, ScriptFiles());
            feeder.Spawn(1, "AlphaPlane", owner: 1, x: 300, y: 500, z: 60);
            feeder.Spawn(2, "BetaLauncher", owner: 2, x: 1300, y: 540);
            world.Step();
            var plane = Only(world, "AlphaPlane");
            world.CompleteUpgrade(plane.Owner, null, "Upgrade_AlphaFlares");
            feeder.SetMoveTarget(1, plane.Id.Value, 2500, 500);
            Entity? flare = null;
            for (var tick = 0; tick < world.Frames(600) && flare is null; tick++)
            {
                world.Step();
                flare = world.Entities.Where(entity => entity.Template.Name == "AlphaFlare").OrderBy(entity => entity.Id.Value).FirstOrDefault();
            }
            Check.That(flare is not null, $"{rate}: a volley of flares left the plane.");
            var origin = (flare!.X, flare.Y, flare.Z);
            _ = origin;
            return (world, () => flare, null);
        }, 20, 1000m); // when the flare appears depends on the missile it answers: only the reading inside its own frames is compared
        FlightVerdict();
    }
}

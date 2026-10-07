using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, package flight (<c>docs/RATE-INVARIANCE-FLIGHT.md</c>): the same little scene at 30, 60 and 120 Hz on the synthetic fixture. The 30 Hz run is the definition; at the higher
/// rates an object must stand, at the end of every logic frame of the original, where the 30 Hz step puts it, and events must fall on the same frame. The tests name their rates and ignore
/// <c>--tick-rate</c>. Every test prints its measurement (30 / 60 / 120 Hz).
/// <para>
/// How the scenes are lined up: an object decides the step of a frame in the first tick of that frame and has flown it at the end of the last one. Its first update comes one tick after
/// it appeared or was killed, so a scene takes that one step and then whole frames; an order is given where a frame begins.
/// </para>
/// </summary>
static partial class Tests
{
    private static readonly SimulationRate[] FlightRates = [SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120];

    private readonly record struct FlightPose(Fixed X, Fixed Y, Fixed Z, Fixed Angle);

    private static (SimulationWorld World, Feeder Feeder) FlightRateWorld(SimulationRate rate, IReadOnlyDictionary<string, string> files, bool terrain = true, int cells = 300)
    {
        var fixture = new SyntheticFixture(rate, files: files);
        var map = terrain ? TerrainMap.Build(SyntheticMaps.Build(cells, cells, 1, (_, _) => 0), "flight-rate") : null;
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 9, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, map?.Fingerprint ?? string.Empty), fixture.Templates, map);
        return (world, new Feeder(world));
    }

    /// <summary>Steps whole logic frames of the original.</summary>
    private static void StepFrames(SimulationWorld world, int frames)
    {
        for (var tick = 0; tick < world.Frames(frames); tick++) world.Step();
    }

    private static Entity Only(SimulationWorld world, string template) => world.Entities.Single(entity => entity.Template.Name == template);

    /// <summary>The pose of <paramref name="entity"/> at the end of each of the next <paramref name="frames"/> logic frames of the original (the track stops when the object is gone).</summary>
    private static List<FlightPose> FrameTrack(SimulationWorld world, Entity entity, int frames, System.Action<TickResult>? each = null)
    {
        var track = new List<FlightPose>();
        for (var frame = 0; frame < frames; frame++)
        {
            for (var tick = 0; tick < world.Time.TicksPerOriginalFrame; tick++)
            {
                var result = world.Step();
                each?.Invoke(result);
            }
            if (world.FindEntity(entity.Id) != entity) break;
            track.Add(new FlightPose(entity.X, entity.Y, entity.Z, entity.Angle));
        }
        return track;
    }

    private static string Units(Fixed value) => decimal.Round(value.ToDecimal(), 4).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The largest distance and the largest difference of heading between two tracks, frame by frame.</summary>
    private static (Fixed Distance, Fixed Angle) TrackDeviation(List<FlightPose> reference, List<FlightPose> other)
    {
        var distance = Fixed.Zero;
        var angle = Fixed.Zero;
        for (var index = 0; index < Math.Min(reference.Count, other.Count); index++)
        {
            var (a, b) = (reference[index], other[index]);
            var (dx, dy, dz) = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
            distance = Fixed.Max(distance, Fixed.Sqrt(dx * dx + dy * dy + dz * dz));
            angle = Fixed.Max(angle, FixedTrig.AngleDifference(a.Angle, b.Angle).Abs());
        }
        return (distance, angle);
    }

    /// <summary>What a test measured outside its tolerance; every measurement is printed before the first of them fails the test (<see cref="FlightVerdict"/>).</summary>
    private static readonly List<string> FlightFailures = [];

    private static void FlightVerdict()
    {
        var failures = FlightFailures.ToArray();
        FlightFailures.Clear();
        Check.That(failures.Length == 0, string.Join(" | ", failures));
    }

    /// <summary>The tracks of the three rates are the same track: equally long and, frame by frame, within <paramref name="units"/> world units and <paramref name="radians"/>.</summary>
    private static void SameTracks(string what, List<FlightPose>[] tracks, decimal units, decimal radians)
    {
        var deviations = tracks.Select(track => TrackDeviation(tracks[0], track)).ToArray();
        Console.WriteLine($"       {what}: frames {tracks[0].Count} / {tracks[1].Count} / {tracks[2].Count}; largest distance from the 30 Hz track {Units(deviations[1].Distance)} / {Units(deviations[2].Distance)} units, " +
                          $"heading {Units(deviations[1].Angle)} / {Units(deviations[2].Angle)} rad (60 / 120 Hz)");
        for (var index = 1; index < tracks.Length; index++)
        {
            if (tracks[index].Count != tracks[0].Count) FlightFailures.Add($"{what}: {FlightRates[index]} is tracked for {tracks[index].Count} frames, 30 Hz for {tracks[0].Count}");
            if (deviations[index].Distance.ToDecimal() > units) FlightFailures.Add($"{what}: {FlightRates[index]} is up to {Units(deviations[index].Distance)} units away from the 30 Hz track at the end of a frame (allowed {Units(Fixed.FromDecimal(units))})");
            if (deviations[index].Angle.ToDecimal() > radians) FlightFailures.Add($"{what}: {FlightRates[index]} heads up to {Units(deviations[index].Angle)} rad off the 30 Hz track at the end of a frame (allowed {Units(Fixed.FromDecimal(radians))})");
        }
    }

    /// <summary>Ticks as quarters of a logic frame of the original (1/120 s): the one unit in which a time is a whole number at 30, 60 and 120 Hz.</summary>
    private static long FrameQuarters(SimulationWorld world, long ticks) => ticks * (SimulationRate.Hz120.TicksPerSecond / world.Time.TicksPerSecond);

    private static string InFrames(long quarters) => quarters < 0 ? "never" : (quarters / 4m).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Something happens at the same game time at the three rates, to the tick (<paramref name="quarters"/> from <see cref="FrameQuarters"/>).</summary>
    private static void SameFrames(string what, long[] quarters)
    {
        Console.WriteLine($"       {what}: after {InFrames(quarters[0])} / {InFrames(quarters[1])} / {InFrames(quarters[2])} frames");
        if (quarters[0] < 0) FlightFailures.Add($"{what}: it never happened at 30 Hz");
        else if (quarters[1] != quarters[0] || quarters[2] != quarters[0]) FlightFailures.Add($"{what}: after {InFrames(quarters[0])} frames at 30 Hz, {InFrames(quarters[1])} at 60 Hz, {InFrames(quarters[2])} at 120 Hz");
    }

    /// <summary>A measured value is the same at the three rates within <paramref name="tolerance"/>.</summary>
    private static void SameValues(string what, Fixed[] values, decimal tolerance)
    {
        Console.WriteLine($"       {what}: {Units(values[0])} / {Units(values[1])} / {Units(values[2])}");
        for (var index = 1; index < values.Length; index++)
            if ((values[index] - values[0]).Abs().ToDecimal() > tolerance) FlightFailures.Add($"{what}: {Units(values[0])} at 30 Hz, {Units(values[index])} at {FlightRates[index]} (allowed {Units(Fixed.FromDecimal(tolerance))})");
    }

    /// <summary>
    /// Winged flight (<c>AIUpdateInterface.MoveWinged</c>/<c>SteerWinged</c>, <c>JetAIUpdate.CircleStep</c>): a jet without an airfield circles, is sent 1,000 units away, loops back and circles
    /// its destination. Heading and speed change once per logic frame of the original; in between the jet moves every tick by the same share.
    /// </summary>
    public static void RateFlightWingedTrackIsTheSame()
    {
        var tracks = new List<FlightPose>[FlightRates.Length];
        var arrivals = new long[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder) = FlightRateWorld(FlightRates[index], WingFixtureFiles, terrain: false);
            feeder.Spawn(1, "WingJet", owner: 1, x: 500, y: 500);
            world.Step();
            StepFrames(world, 20);
            var jet = Only(world, "WingJet");
            Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 1500, 600).Accepted, "The move order is accepted.");
            long arrived = -1;
            var frame = 0L;
            var start = world.Tick;
            var steady = true;
            var last = (jet.X, jet.Y);
            tracks[index] = FrameTrack(world, jet, 900, result =>
            {
                frame = FrameQuarters(world, result.Tick - start);
                if (arrived < 0 && result.Events.Any(value => value.Kind == SimEventKind.MoveArrived && value.Entity == jet.Id)) arrived = frame;
                // Every tick the jet covers the same share of the frame's travel: its speed per tick.
                var (dx, dy) = (jet.X - last.X, jet.Y - last.Y);
                last = (jet.X, jet.Y);
                if ((Fixed.Sqrt(dx * dx + dy * dy) - jet.Speed).Abs() > Fixed.FromDecimal(0.001m)) steady = false;
            });
            arrivals[index] = arrived;
            Check.That(steady, $"{FlightRates[index]}: the jet moves by its speed per tick in every tick.");
        }
        SameFrames("winged flight, the order ends at its destination", arrivals);
        SameTracks("winged flight, 900 frames of flight and circling", tracks, 0.05m, 0.001m);
        FlightVerdict();
    }

    /// <summary>
    /// <c>JetSlowDeathBehavior</c>: a plane shot down at height 45 while flying 4 units per frame falls with half the gravity, hits the ground and blows up. The speed of the fall grows once
    /// per logic frame of the original.
    /// </summary>
    public static void RateFlightJetSlowDeathFallIsTheSame()
    {
        var tracks = new List<FlightPose>[FlightRates.Length];
        var ground = new long[FlightRates.Length];
        var gone = new long[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder) = FlightRateWorld(FlightRates[index], ScriptFiles());
            feeder.Spawn(1, "AlphaPlane", owner: 1, x: 500, y: 500, z: 45);
            world.Step();
            StepFrames(world, 4);
            var plane = Only(world, "AlphaPlane");
            // It is shot down at full speed (4 units per frame); the flight up to there is the ordinary air movement, so the fall is compared from where it began.
            Check.That(feeder.SetMoveTarget(1, plane.Id.Value, 2500, 500).Accepted, "The move order is accepted.");
            StepFrames(world, 60);
            Check.That(plane.Speed == world.Time.PerSecondToPerTick(120m), $"{FlightRates[index]}: the plane flies at its speed.");
            feeder.Kill(plane.Id.Value);
            world.Step();
            var origin = (plane.X, plane.Y);
            var start = world.Tick;
            (ground[index], gone[index]) = (-1, -1);
            var at = index;
            tracks[index] = FrameTrack(world, plane, 400, result =>
            {
                if (ground[at] < 0 && plane.Z == Fixed.Zero) ground[at] = FrameQuarters(world, result.Tick - start);
                if (gone[at] < 0 && world.FindEntity(plane.Id) != plane) gone[at] = FrameQuarters(world, result.Tick - start);
            }).Select(pose => pose with { X = pose.X - origin.X, Y = pose.Y - origin.Y }).ToList();
        }
        SameFrames("jet slow death, the wreck hits the ground", ground);
        SameFrames("jet slow death, the wreck is gone", gone);
        SameTracks("jet slow death, the fall", tracks, 0.01m, 0.0001m);
        FlightVerdict();
    }

    /// <summary>
    /// <c>ParachuteContain</c>: a parachute released at height 100 falls freely until it has dropped its open distance (10), then sinks at its locomotor's <c>SpeedLimitZ</c> (one unit per
    /// frame) and, with a landing spot (<c>ParachuteDirectly</c>), steers there at the locomotor's speed (two units per frame); on the ground the rider gets off. Once straight down, once steered.
    /// </summary>
    public static void RateFlightParachuteIsTheSame()
    {
        foreach (var steered in new[] { false, true })
        {
            var name = steered ? "parachute, steered to a spot" : "parachute, straight down";
            var tracks = new List<FlightPose>[FlightRates.Length];
            var opened = new long[FlightRates.Length];
            var landed = new long[FlightRates.Length];
            var miss = new Fixed[FlightRates.Length];
            for (var index = 0; index < FlightRates.Length; index++)
            {
                var (world, feeder) = FlightRateWorld(FlightRates[index], ScriptFiles());
                feeder.Spawn(1, "AlphaChute", owner: 1, x: 500, y: 500, z: 100);
                feeder.Spawn(1, "AlphaTrooper", owner: 1, x: 500, y: 500);
                world.Step();
                var chute = Only(world, "AlphaChute");
                var trooper = Only(world, "AlphaTrooper");
                var module = chute.Modules.OfType<ParachuteContain>().Single();
                module.Add(world, trooper);
                if (steered) module.SetOverrideDestination(Fixed.FromInt(560), Fixed.FromInt(580));
                var start = world.Tick;
                (opened[index], landed[index]) = (-1, -1);
                var at = index;
                tracks[index] = FrameTrack(world, chute, 300, result =>
                {
                    var frame = FrameQuarters(world, result.Tick - start);
                    if (opened[at] < 0 && module.Opened) opened[at] = frame;
                    if (landed[at] < 0 && result.Events.Any(value => value.Kind == SimEventKind.EntityUncontained && value.Entity == trooper.Id)) landed[at] = frame;
                });
                var (dx, dy) = (trooper.X - Fixed.FromInt(steered ? 560 : 500), trooper.Y - Fixed.FromInt(steered ? 580 : 500));
                miss[index] = Fixed.Sqrt(dx * dx + dy * dy);
                Check.That(!trooper.EffectivelyDead, $"{FlightRates[index]}: the rider survives the landing.");
            }
            SameFrames($"{name}: it opens", opened);
            SameFrames($"{name}: the rider is on the ground", landed);
            SameValues($"{name}: distance of the rider from the spot", miss, 0.01m);
            SameTracks($"{name}: the descent", tracks, 0.01m, 0.0001m);
        }
        FlightVerdict();
    }

    /// <summary>
    /// <c>CountermeasuresBehavior.MoveFlares</c>: a flare leaves the plane at twice the plane's speed, loses speed at its locomotor's braking and sinks at its <c>SpeedLimitZ</c>. The braking
    /// acts once per logic frame of the original; the flare moves every tick. Compared from where the flare appeared (when it appears depends on the missile it answers).
    /// </summary>
    public static void RateFlightFlaresAreTheSame()
    {
        var tracks = new List<FlightPose>[FlightRates.Length];
        var launched = new long[FlightRates.Length];
        var travel = new Fixed[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder) = FlightRateWorld(FlightRates[index], ScriptFiles());
            feeder.Spawn(1, "AlphaPlane", owner: 1, x: 300, y: 500, z: 60);
            // Out of the launcher's range at first: the plane is at full speed when the missile comes.
            feeder.Spawn(2, "BetaLauncher", owner: 2, x: 1300, y: 540);
            world.Step();
            var plane = Only(world, "AlphaPlane");
            world.CompleteUpgrade(plane.Owner, null, "Upgrade_AlphaFlares");
            Check.That(feeder.SetMoveTarget(1, plane.Id.Value, 2500, 500).Accepted, "The move order is accepted.");
            var start = world.Tick;
            Entity? flare = null;
            for (var tick = 0; tick < world.Frames(600) && flare is null; tick++)
            {
                world.Step();
                flare = world.Entities.Where(entity => entity.Template.Name == "AlphaFlare").OrderBy(entity => entity.Id.Value).FirstOrDefault();
            }
            Check.That(flare is not null, $"{FlightRates[index]}: a missile was fired at the plane and a volley of flares left it.");
            launched[index] = FrameQuarters(world, world.Tick - start);
            var origin = (flare!.X, flare.Y);
            tracks[index] = FrameTrack(world, flare, 25).Select(pose => pose with { X = pose.X - origin.X, Y = pose.Y - origin.Y, Angle = Fixed.Zero }).ToList();
            var last = tracks[index][^1];
            travel[index] = Fixed.Sqrt(last.X * last.X + last.Y * last.Y);
        }
        Console.WriteLine($"       flares: the first volley leaves after {InFrames(launched[0])} / {InFrames(launched[1])} / {InFrames(launched[2])} frames (not compared: the missile decides)");
        SameValues("flares: travel of the first flare in 25 frames", travel, 0.01m);
        SameTracks("flares: the first flare, 25 frames", tracks, 0.01m, 0.0001m);
        FlightVerdict();
    }

    /// <summary>
    /// <c>ParticleUplinkCannonUpdate.MoveBeam</c>, driven along waypoints (a scripted use): the beam drives 20 units per frame from waypoint to waypoint; the last step to a waypoint is
    /// as short as the rest of the way and the next step heads for the next waypoint. That is the 30 Hz step; a beam that switches when the rest is one tick of travel loses less at every corner.
    /// </summary>
    public static void RateFlightUplinkBeamPathIsTheSame()
    {
        var tracks = new List<FlightPose>[FlightRates.Length];
        var corner = new long[FlightRates.Length];
        var end = new long[FlightRates.Length];
        Point3[] path = [new(Fixed.FromInt(300), Fixed.FromInt(200), Fixed.Zero), new(Fixed.FromInt(367), Fixed.FromInt(200), Fixed.Zero), new(Fixed.FromInt(367), Fixed.FromInt(293), Fixed.Zero), new(Fixed.FromInt(300), Fixed.FromInt(293), Fixed.Zero)];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder) = FlightRateWorld(FlightRates[index], ScriptFiles());
            feeder.Spawn(2, "BetaCannon", owner: 2, x: 100, y: 200);
            world.Step();
            var module = Only(world, "BetaCannon").Modules.OfType<ParticleUplinkCannonUpdate>().Single();
            Check.That(world.SpecialPowers.TryGet("UplinkPower", out var power), "The power exists.");
            module.PrepareScriptedPath(world, path);
            module.Initiate(world, power!.Index, new CnCNew.Simulation.Commands.SpecialPowerTarget(CnCNew.Simulation.Commands.SpecialPowerTargetKind.Location, EntityId.Invalid, path[0].X, path[0].Y));
            var start = world.Tick;
            (corner[index], end[index]) = (-1, -1);
            tracks[index] = [];
            for (var frame = 0; frame < 60 && module.InUse; frame++)
            {
                for (var tick = 0; tick < world.Time.TicksPerOriginalFrame && module.InUse; tick++)
                {
                    world.Step();
                    var now = FrameQuarters(world, world.Tick - start);
                    if (corner[index] < 0 && module.BeamPosition.X == path[1].X && module.BeamPosition.Y == path[1].Y) corner[index] = now;
                    if (end[index] < 0 && module.BeamPosition.X == path[3].X && module.BeamPosition.Y == path[3].Y) end[index] = now;
                }
                if (module.InUse) tracks[index].Add(new FlightPose(module.BeamPosition.X, module.BeamPosition.Y, Fixed.Zero, Fixed.Zero));
            }
        }
        SameFrames("uplink beam on a path: stands on the second waypoint", corner);
        SameFrames("uplink beam on a path: stands on the last waypoint", end);
        SameTracks("uplink beam on a path: the drive", tracks, 0.01m, 0.0001m);
        FlightVerdict();
    }

    /// <summary>The transport fixture plus a helicopter whose death spirals: orbit 150 units per second damped by 0.98 per frame, self spin between 360 and 720 degrees per second
    /// (changed by 40: no multiple of it is the range, so that the turn-around does not hang on the last bit of a sum), a blade that flies off.</summary>
    private static IReadOnlyDictionary<string, string> SpiralFiles() => new Dictionary<string, string>(TransportFiles())
    {
        ["Data/INI/Object/RateFlightSpiral.ini"] = """
        Object Spinner
          KindOf = VEHICLE AIRCRAFT
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = HelicopterSlowDeathBehavior Death01
            DestructionDelay = 99999999
            SpiralOrbitTurnRate = 140.0
            SpiralOrbitForwardSpeed = 150.0
            SpiralOrbitForwardSpeedDamping = 0.98
            MinSelfSpin = 360.0
            MaxSelfSpin = 720.0
            SelfSpinUpdateDelay = 150
            SelfSpinUpdateAmount = 40.0
            FallHowFast = 30%
            MinBladeFlyOffDelay = 610
            MaxBladeFlyOffDelay = 1490
            FXBlade = FX_Other
            FXHitGround = FX_Test
            DelayFromGroundToFinalDeath = 500
            FinalRubbleObject = RotorHulk
          End
        End
        """,
    };

    /// <summary>
    /// <c>HelicopterSlowDeathBehavior</c>: the wreck spins, spirals on a damped orbit, loses its blade and falls. The damping is a factor per logic frame of the original and the orbit, the
    /// spin and the fall are the 30 Hz step; the blade's countdown and the wait on the ground count frames.
    /// </summary>
    public static void RateFlightHelicopterSlowDeathIsTheSame()
    {
        var tracks = new List<FlightPose>[FlightRates.Length];
        var blade = new long[FlightRates.Length];
        var ground = new long[FlightRates.Length];
        var gone = new long[FlightRates.Length];
        var drift = new Fixed[FlightRates.Length];
        var spin = new Fixed[FlightRates.Length];
        var lowest = new Fixed[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder) = FlightRateWorld(FlightRates[index], SpiralFiles(), terrain: false);
            feeder.Spawn(1, "Spinner", x: 500, y: 500, z: 100);
            world.Step();
            // Longer than SelfSpinUpdateDelay: the first change of the self spin is due "since tick 0" at every rate.
            StepFrames(world, 30);
            var wreck = Only(world, "Spinner");
            feeder.Kill(wreck.Id.Value);
            world.Step();
            var start = world.Tick;
            (blade[index], ground[index], gone[index]) = (-1, -1, -1);
            var at = index;
            var heading = wreck.Angle;
            var turned = Fixed.Zero;
            var low = wreck.Z;
            var last = (wreck.X, wreck.Y);
            tracks[index] = FrameTrack(world, wreck, 400, result =>
            {
                var now = FrameQuarters(world, result.Tick - start);
                if (blade[at] < 0 && result.Events.Any(value => value.Kind == SimEventKind.FxRequested && value.Text == "FX_Other")) blade[at] = now;
                if (ground[at] < 0 && result.Events.Any(value => value.Kind == SimEventKind.FxRequested && value.Text == "FX_Test")) ground[at] = now;
                if (world.FindEntity(wreck.Id) != wreck)
                {
                    if (gone[at] < 0) gone[at] = now;
                    return;
                }
                turned += FixedTrig.AngleDifference(heading, wreck.Angle).Abs();
                heading = wreck.Angle;
                low = Fixed.Min(low, wreck.Z);
                last = (wreck.X, wreck.Y);
            });
            var (dx, dy) = (last.X - Fixed.FromInt(500), last.Y - Fixed.FromInt(500));
            drift[index] = Fixed.Sqrt(dx * dx + dy * dy);
            spin[index] = turned;
            lowest[index] = low;
        }
        Console.WriteLine($"       helicopter slow death: the blade flies off: after {InFrames(blade[0])} / {InFrames(blade[1])} / {InFrames(blade[2])} frames (not compared yet: its countdown is the next cause)");
        SameFrames("helicopter slow death: the wreck hits the ground", ground);
        SameFrames("helicopter slow death: the wreck is gone", gone);
        SameValues("helicopter slow death: distance of the wreck from where it was shot", drift, 0.01m);
        SameValues("helicopter slow death: lowest height", lowest, 0.01m);
        SameValues("helicopter slow death: sum of its turns (radians)", spin, 0.01m);
        SameTracks("helicopter slow death: the spiral", tracks, 0.01m, 0.001m);
        FlightVerdict();
    }
}

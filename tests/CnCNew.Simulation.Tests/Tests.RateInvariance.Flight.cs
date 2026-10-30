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
/// Phase 23, package flight (<c>docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md</c>): the same little scene at 30, 60 and 120 Hz on the synthetic fixture. The 30 Hz run is the definition; at the higher
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

    private static (SimulationWorld World, Feeder Feeder) FlightRateWorld(SimulationRate rate, IReadOnlyDictionary<string, string> files, bool terrain = true, int cells = 300, bool computer = false)
    {
        var fixture = new SyntheticFixture(rate, files: files);
        var map = terrain ? TerrainMap.Build(SyntheticMaps.Build(cells, cells, 1, (_, _) => 0), "flight-rate") : null;
        System.Collections.Immutable.ImmutableArray<PlayerConfiguration> players = computer ? [new(new PlayerId(1), "Alpha", IsComputer: true), new(new PlayerId(2), "Beta")] : CombatPlayers;
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 9, players, SyntheticFixture.FixtureId, fixture.Fingerprint, map?.Fingerprint ?? string.Empty), fixture.Templates, map);
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

    /// <summary>The whole logic frames of the original that have passed in <paramref name="ticks"/>, in the unit of <see cref="FrameQuarters"/>: for a position, which is reached with the last tick of a frame.</summary>
    private static long InWholeFrames(SimulationWorld world, long ticks) => world.Time.ToOriginalFrames(ticks) * 4;

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
            // A new aircraft begins its first frame one frame after it appeared (the step that creates it and twenty frames; the same 21 ticks at 30 Hz).
            StepFrames(world, 21);
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

    private static IReadOnlyDictionary<string, string> FallingPlaneFiles() => new Dictionary<string, string>(ScriptFiles())
    {
        ["Data/INI/Object/FallingPlaneFixture.ini"] = """
        Object InheritPlane
          KindOf = SELECTABLE VEHICLE AIRCRAFT
          Locomotor = SET_NORMAL PlaneLoco
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 1.0
          End
          Behavior = JetSlowDeathBehavior JetDeath01
            DestructionDelay = 5000
            FallHowFast = 50%
          End
        End
        Object InheritCrate
          KindOf = SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 1.0
          End
        End
        ObjectCreationList OCL_InheritCrate
          CreateObject
            ObjectNames = InheritCrate
            Disposition = LIKE_EXISTING INHERIT_VELOCITY
          End
        End
        """,
    };

    /// <summary>
    /// F-66's rest: <c>INHERIT_VELOCITY</c> gives what a falling plane makes the way it falls - a crate made ten frames into the fall flies on along the plane's heading and comes down away
    /// from where it was made, at the same place at 30, 60 and 120 Hz. Before, only a fall or flight of the physics module gave a velocity, and the crate dropped where it was made.
    /// </summary>
    public static void CreationListTakesAFallingPlanesVelocity()
    {
        var places = new (long X, long Y)[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder) = FlightRateWorld(FlightRates[index], FallingPlaneFiles());
            feeder.Spawn(1, "InheritPlane", owner: 1, x: 500, y: 500, z: 90);
            StepFrames(world, 5);
            var plane = Only(world, "InheritPlane");
            Check.That(feeder.SetMoveTarget(1, plane.Id.Value, 2500, 500).Accepted, "The move order is accepted.");
            StepFrames(world, 60);
            feeder.Kill(plane.Id.Value);
            StepFrames(world, 10);
            var (madeX, madeY, _) = plane.FrameEnd(world.Tick);
            world.CreateFromList(plane, "OCL_InheritCrate", EntityId.Invalid, false);
            world.Step();
            var crate = Only(world, "InheritCrate");
            StepFrames(world, 300);
            Check.That(!crate.InPhysicsFlight, $"{FlightRates[index]}: the crate rests.");
            places[index] = (crate.X.Raw - madeX.Raw, crate.Y.Raw - madeY.Raw);
            Check.That(places[index].X > Fixed.FromInt(20).Raw, $"{FlightRates[index]}: the crate flies on along the plane's way ({places[index].X}).");
            Check.That(places[index] == places[0], $"{FlightRates[index]}: the crate rests where it rests at 30 Hz.");
        }
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
        Locomotor OddRotorLoco
          Surfaces = AIR
          Speed = 140
          Acceleration = 900
          Braking = 900
          TurnRate = 3600
          PreferredHeight = 100
        End
        Object Lifter
          KindOf = VEHICLE AIRCRAFT TRANSPORT HARVESTER
          Locomotor = SET_NORMAL OddRotorLoco
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = ChinookAIUpdate AI01
            MaxBoxes = 2
            SupplyCenterActionDelay = 0
            SupplyWarehouseActionDelay = 0
            SupplyWarehouseScanDistance = 700
          End
          Behavior = TransportContain Transport01
            Slots = 4
          End
          Behavior = DestroyDie Destroy01
          End
        End
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
            // The spiral's first step is one frame after the death (one tick at 30 Hz): the track starts there.
            StepFrames(world, 1);
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
        SameFrames("helicopter slow death: the blade flies off", blade);
        SameFrames("helicopter slow death: the wreck hits the ground", ground);
        SameFrames("helicopter slow death: the wreck is gone", gone);
        SameValues("helicopter slow death: distance of the wreck from where it was shot", drift, 0.01m);
        SameValues("helicopter slow death: lowest height", lowest, 0.01m);
        SameValues("helicopter slow death: sum of its turns (radians)", spin, 0.01m);
        SameTracks("helicopter slow death: the spiral", tracks, 0.01m, 0.001m);
        FlightVerdict();
    }

    /// <summary>
    /// <c>TunnelContain.Update</c>: a rider is healed by <c>MaxHealth / TimeForFullHeal</c> once per logic frame of the original, counted from the tick it entered. A grunt hurt by 50 of 100
    /// enters a tunnel that heals fully in 30 frames; once on a tick where a frame begins, once one tick later (which is a whole frame at 30 Hz and a part of one above).
    /// </summary>
    public static void RateFlightTunnelHealingIsTheSame()
    {
        foreach (var late in new[] { false, true })
        {
            var name = late ? "tunnel healing, entered one tick after a frame began" : "tunnel healing, entered where a frame begins";
            var first = new long[FlightRates.Length];
            var full = new long[FlightRates.Length];
            var heals = new Fixed[FlightRates.Length];
            var frames = new Fixed[FlightRates.Length];
            var afterFive = new Fixed[FlightRates.Length];
            for (var index = 0; index < FlightRates.Length; index++)
            {
                var (world, feeder) = FlightRateWorld(FlightRates[index], TransportFiles(), terrain: false);
                feeder.Spawn(1, "Tunnel", x: 0, y: 0);
                feeder.Spawn(1, "Grunt", x: 30, y: 0);
                world.Step();
                var grunt = Only(world, "Grunt");
                var tunnel = Only(world, "Tunnel");
                feeder.Damage(grunt.Id.Value, 50m);
                StepFrames(world, 3);
                // The world stands where a frame of the original begins (tick 1 + 3 frames is one for the grid of this scene only by the count of frames; what matters is the same game time).
                if (late) world.Step();
                Check.That(feeder.EnterContainer(1, grunt.Id.Value, tunnel.Id.Value).Accepted, "The grunt enters the tunnel.");
                var start = world.Tick;
                var body = grunt.Body!;
                var health = body.Health;
                var atFrame = body.Health;
                (first[index], full[index]) = (-1, -1);
                var (count, changed) = (0, 0);
                for (var frame = 0; frame < 60; frame++)
                {
                    for (var tick = 0; tick < world.Time.TicksPerOriginalFrame; tick++)
                    {
                        var result = world.Step();
                        var now = FrameQuarters(world, result.Tick - start);
                        if (body.Health > health)
                        {
                            count++;
                            if (first[index] < 0) first[index] = now;
                            if (full[index] < 0 && body.Health == body.MaxHealth) full[index] = now;
                        }
                        health = body.Health;
                    }
                    if (body.Health > atFrame) changed++;
                    atFrame = body.Health;
                    if (frame == 4) afterFive[index] = body.Health;
                }
                heals[index] = Fixed.FromInt(count);
                frames[index] = Fixed.FromInt(changed);
            }
            SameFrames($"{name}: first healing", first);
            SameFrames($"{name}: full health", full);
            SameValues($"{name}: number of healings", heals, 0m);
            SameValues($"{name}: frames with healing", frames, 0m);
            SameValues($"{name}: health after five frames", afterFive, 0.0001m);
        }
        FlightVerdict();
    }

    /// <summary>
    /// <c>ChinookAIUpdate.Update</c>: a Chinook changes its height by one locomotor speed per frame (140 units per second here, 4.667 per frame, so that 100 units are no whole number of
    /// steps): the climb after it appears, the landing on an evacuation order with a passenger, the passenger's exit and the climb back.
    /// </summary>
    public static void RateFlightChinookClimbIsTheSame()
    {
        var tracks = new List<FlightPose>[FlightRates.Length];
        var up = new long[FlightRates.Length];
        var down = new long[FlightRates.Length];
        var exit = new long[FlightRates.Length];
        var again = new long[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder) = FlightRateWorld(FlightRates[index], SpiralFiles(), terrain: false);
            feeder.Spawn(1, "Lifter", x: 0, y: 0);
            feeder.Spawn(1, "Grunt", x: 0, y: 0);
            // A new Chinook begins its first frame one frame after it appeared (one tick at 30 Hz): the track starts there.
            StepFrames(world, 1);
            var chinook = Only(world, "Lifter");
            var grunt = Only(world, "Grunt");
            var start = world.Tick;
            (up[index], down[index], exit[index], again[index]) = (-1, -1, -1, -1);
            var at = index;
            var top = Fixed.FromInt(100);
            tracks[index] = FrameTrack(world, chinook, 40, result =>
            {
                if (up[at] < 0 && chinook.Z == top) up[at] = InWholeFrames(world, result.Tick - start);
            });
            Check.That(feeder.EnterContainer(1, grunt.Id.Value, chinook.Id.Value).Accepted, "A grunt boards the Chinook.");
            StepFrames(world, 1);
            Check.That(feeder.Evacuate(1, chinook.Id.Value).Accepted, "Evacuation is ordered in the air.");
            start = world.Tick;
            tracks[index].AddRange(FrameTrack(world, chinook, 80, result =>
            {
                // A height is reached when the frame's step has been flown (its last tick); what the Chinook does about it happens in the first tick of a frame.
                if (down[at] < 0 && chinook.Z == Fixed.Zero) down[at] = InWholeFrames(world, result.Tick - start);
                if (exit[at] < 0 && !grunt.ContainedBy.IsValid) exit[at] = FrameQuarters(world, result.Tick - start);
                if (exit[at] >= 0 && again[at] < 0 && chinook.Z == top) again[at] = InWholeFrames(world, result.Tick - start);
            }));
        }
        SameFrames("Chinook: at its height after it appeared", up);
        SameFrames("Chinook: on the ground after the evacuation order", down);
        SameFrames("Chinook: the passenger is out", exit);
        SameFrames("Chinook: back at its height", again);
        SameTracks("Chinook: climb, landing and climb", tracks, 0.01m, 0.0001m);
        FlightVerdict();
    }

    /// <summary>
    /// <c>SpectreGunshipUpdate</c>: the gunship inserts toward a circle around its target, orbits it for <c>OrbitTime</c>, strafes its gattling's aim point toward the enemy in the reticle and
    /// fires its howitzer once the aim point has stayed on it longer than <c>HowitzerFollowLag</c>, then departs. Flight and strafing are the 30 Hz step on the frames of the original,
    /// on which the howitzer's rhythm lies. The power is used one tick before a frame begins, so that the gunship's first update is a frame's first tick at every rate.
    /// </summary>
    public static void RateFlightSpectreGunshipIsTheSame()
    {
        // A human's gunship aims where it was sent; a computer player's finds its victim in the whole attack area and winds the gattling's aim point there (56.3 units, 2.8 increments).
        foreach (var computer in new[] { false, true })
        {
        var name = computer ? "Spectre gunship of a computer player" : "Spectre gunship";
        var tracks = new List<FlightPose>[FlightRates.Length];
        var orbit = new long[FlightRates.Length];
        var depart = new long[FlightRates.Length];
        var firstHowitzer = new long[FlightRates.Length];
        var howitzers = new Fixed[FlightRates.Length];
        var gattlings = new Fixed[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder) = FlightRateWorld(FlightRates[index], AirWeaponFixtureFiles, terrain: false, computer: computer);
            feeder.Spawn(1, "AirGunship", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "AirDummy", owner: 2, x: computer ? 455 : 405, y: computer ? 12 : 5);
            // Whole frames from tick 0 and then up to the last tick of a frame: the power is used at the same game time at every rate (the howitzer fires on ticks that are a
            // multiple of its rate, counted from tick 0).
            StepFrames(world, 125); // the power recharges for 4 seconds after the gunship appeared
            var ship = Only(world, "AirGunship");
            var spectre = ship.Modules.OfType<SpectreGunshipUpdate>().Single();
            while (!world.Time.IsOriginalFrameBoundary(world.Tick + 1)) world.Step();
            Check.That(feeder.UseSpecialPower(1, ship.Id.Value, "AirGunshipPower", CnCNew.Simulation.Commands.SpecialPowerTargetKind.Location, x: 400, y: 0).Accepted, "The power is used on a location.");
            world.Step();
            var start = world.Tick;
            (orbit[index], depart[index], firstHowitzer[index]) = (-1, -1, -1);
            var (fired, shells) = (0, 0);
            var at = index;
            tracks[index] = FrameTrack(world, ship, 260, result =>
            {
                var now = FrameQuarters(world, result.Tick - start);
                if (orbit[at] < 0 && spectre.Status == SpectreGunshipUpdate.GunshipStatus.Orbiting) orbit[at] = now;
                if (depart[at] < 0 && spectre.Status == SpectreGunshipUpdate.GunshipStatus.Departing) depart[at] = now;
                foreach (var value in result.Events)
                {
                    if (value.Kind != SimEventKind.WeaponFired) continue;
                    if (value.Entity == ship.Id)
                    {
                        shells++;
                        if (firstHowitzer[at] < 0) firstHowitzer[at] = now;
                    }
                    else if (value.Entity == spectre.Gattling) fired++;
                }
            });
            howitzers[index] = Fixed.FromInt(shells);
            gattlings[index] = Fixed.FromInt(fired);
        }
        SameFrames($"{name}: it starts to orbit", orbit);
        SameFrames($"{name}: first howitzer shell", firstHowitzer);
        SameFrames($"{name}: it departs", depart);
        SameValues($"{name}: howitzer shells", howitzers, 0m);
        SameValues($"{name}: gattling shots", gattlings, 0m);
        SameTracks($"{name}: insertion, orbit and departure", tracks, 0.05m, 0.001m);
        }
        FlightVerdict();
    }

    /// <summary>A parked jet of the airfield fixture, at the same game time at every rate and where a frame begins (the production ends on a tick of its own at every rate).</summary>
    private static (SimulationWorld World, Feeder Feeder, Entity Jet, Entity Airfield) ParkedJet(SimulationRate rate)
    {
        var (world, feeder) = FlightRateWorld(rate, WingAirfieldFiles, terrain: false);
        feeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
        world.Step();
        feeder.GiveMoney(1, 1000);
        world.Step();
        var airfield = Only(world, "AirTestAirfield");
        Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestJet").Accepted, "The jet is queued.");
        while (world.Tick < world.Frames(900)) world.Step();
        var jet = Only(world, "AirTestJet");
        var ai = JetOf(jet);
        Check.That(ai.Phase == JetAIUpdate.JetPhase.None && !ai.AllowAirLoco, $"{rate}: the jet is parked 30 s after it was ordered.");
        return (world, feeder, jet, airfield);
    }

    /// <summary>
    /// <c>JetAIUpdate</c> at its airfield: a parked jet is sent away, taxis to the runway, pauses, takes off, climbs, flies, circles; after its idle time it goes home, approaches, lands, taxis to
    /// its space, turns and reloads. Every change of state falls on the same game time and the jet is in the same place at the end of every frame.
    /// </summary>
    public static void RateFlightJetAirfieldLifecycleIsTheSame()
    {
        var tracks = new List<FlightPose>[FlightRates.Length];
        var states = new string[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder, jet, airfield) = ParkedJet(FlightRates[index]);
            var ai = JetOf(jet);
            Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 1200, 500).Accepted, "The move order is accepted.");
            var start = world.Tick;
            var phase = ai.Phase;
            var airborne = ai.AllowAirLoco;
            var log = new List<string>();
            void Watch(TickResult result)
            {
                if (ai.Phase == phase && ai.AllowAirLoco == airborne) return;
                (phase, airborne) = (ai.Phase, ai.AllowAirLoco);
                log.Add($"{phase}{(airborne ? "+" : "-")}@{InFrames(FrameQuarters(world, result.Tick - start))}");
            }
            // The jet of the fixture goes home by itself after ReturnToBaseIdleTime: the whole round trip follows from the one order.
            tracks[index] = FrameTrack(world, jet, 700, Watch);
            Check.That(log.Any(entry => entry.StartsWith("Landing", StringComparison.Ordinal)) && !ai.AllowAirLoco && ai.Phase == JetAIUpdate.JetPhase.None, $"{FlightRates[index]}: the jet flew, landed and is parked again ({string.Join(" ", log)}).");
            _ = airfield;
            states[index] = string.Join(" ", log);
        }
        Console.WriteLine($"       jet at its airfield, states (state, + airborne, @ frames after the first order): {states[0]}");
        for (var index = 1; index < FlightRates.Length; index++)
            if (states[index] != states[0]) FlightFailures.Add($"jet at its airfield: the states of {FlightRates[index]} are {states[index]}, at 30 Hz {states[0]}");
        SameTracks("jet at its airfield: take-off, flight, landing, taxi", tracks, 0.1m, 0.002m);
        FlightVerdict();
    }

    /// <summary>
    /// The step of a frame that is being flown is state: a snapshot taken in the middle of a frame carries it (60 and 120 Hz) and the restored world continues hash-identically; at
    /// 30 Hz a frame is a tick and no snapshot ever carries one. One scene per module that flies a decided frame in shares.
    /// </summary>
    public static void RateFlightFrameStepsSurviveSnapshots()
    {
        var scenes = new (string Key, Func<SimulationRate, SimulationWorld> Stage)[]
        {
            ("wingStep", rate =>
            {
                var (world, feeder) = FlightRateWorld(rate, WingFixtureFiles, terrain: false);
                feeder.Spawn(1, "WingJet", owner: 1, x: 500, y: 500);
                StepFrames(world, 21);
                feeder.SetMoveTarget(1, Only(world, "WingJet").Id.Value, 1500, 600);
                StepFrames(world, 10);
                return world;
            }),
            ("frameStep", rate =>
            {
                var (world, feeder, jet, _) = ParkedJet(rate);
                feeder.SetMoveTarget(1, jet.Id.Value, 1200, 500);
                StepFrames(world, 40);
                return world;
            }),
            ("spiralStep", rate =>
            {
                var (world, feeder) = FlightRateWorld(rate, SpiralFiles(), terrain: false);
                feeder.Spawn(1, "Spinner", x: 500, y: 500, z: 100);
                world.Step();
                StepFrames(world, 30);
                feeder.Kill(Only(world, "Spinner").Id.Value);
                StepFrames(world, 11);
                return world;
            }),
            ("fallStep", rate =>
            {
                var (world, feeder) = FlightRateWorld(rate, ScriptFiles());
                feeder.Spawn(1, "AlphaPlane", owner: 1, x: 500, y: 500, z: 45);
                world.Step();
                feeder.SetMoveTarget(1, Only(world, "AlphaPlane").Id.Value, 2500, 500);
                StepFrames(world, 60);
                feeder.Kill(Only(world, "AlphaPlane").Id.Value);
                world.Step();
                StepFrames(world, 10);
                return world;
            }),
            ("descentStep", rate =>
            {
                var (world, feeder) = FlightRateWorld(rate, ScriptFiles());
                feeder.Spawn(1, "AlphaChute", owner: 1, x: 500, y: 500, z: 100);
                feeder.Spawn(1, "AlphaTrooper", owner: 1, x: 500, y: 500);
                world.Step();
                var module = Only(world, "AlphaChute").Modules.OfType<ParachuteContain>().Single();
                module.Add(world, Only(world, "AlphaTrooper"));
                module.SetOverrideDestination(Fixed.FromInt(560), Fixed.FromInt(580));
                StepFrames(world, 30);
                return world;
            }),
            ("driveLeft", rate =>
            {
                var (world, feeder) = FlightRateWorld(rate, ScriptFiles());
                feeder.Spawn(2, "BetaCannon", owner: 2, x: 100, y: 200);
                world.Step();
                var module = Only(world, "BetaCannon").Modules.OfType<ParticleUplinkCannonUpdate>().Single();
                world.SpecialPowers.TryGet("UplinkPower", out var power);
                module.PrepareScriptedPath(world, [new(Fixed.FromInt(300), Fixed.FromInt(200), Fixed.Zero), new(Fixed.FromInt(367), Fixed.FromInt(200), Fixed.Zero), new(Fixed.FromInt(367), Fixed.FromInt(293), Fixed.Zero)]);
                module.Initiate(world, power!.Index, new CnCNew.Simulation.Commands.SpecialPowerTarget(CnCNew.Simulation.Commands.SpecialPowerTargetKind.Location, EntityId.Invalid, Fixed.FromInt(300), Fixed.FromInt(200)));
                StepFrames(world, 9);
                return world;
            }),
            ("flightStep", rate =>
            {
                var (world, feeder) = FlightRateWorld(rate, AirWeaponFixtureFiles, terrain: false, computer: true);
                feeder.Spawn(1, "AirGunship", owner: 1, x: 0, y: 0);
                feeder.Spawn(2, "AirDummy", owner: 2, x: 455, y: 12);
                StepFrames(world, 125);
                while (!world.Time.IsOriginalFrameBoundary(world.Tick + 1)) world.Step();
                feeder.UseSpecialPower(1, Only(world, "AirGunship").Id.Value, "AirGunshipPower", CnCNew.Simulation.Commands.SpecialPowerTargetKind.Location, x: 400, y: 0);
                world.Step();
                StepFrames(world, 43);
                return world;
            }),
            ("climbStep", rate =>
            {
                var (world, feeder) = FlightRateWorld(rate, SpiralFiles(), terrain: false);
                feeder.Spawn(1, "Lifter", x: 0, y: 0);
                // The Chinook's frames begin whole frames after the tick it appeared in.
                StepFrames(world, 6);
                return world;
            }),
        };
        foreach (var (key, stage) in scenes)
            foreach (var rate in FlightRates)
            {
                var world = stage(rate);
                // The scene ends where a frame begins for its object: one more tick decides the frame and flies its first share.
                world.Step();
                var bytes = CnCNew.Simulation.Snapshots.WorldSnapshot.Write(world);
                var carries = System.Text.Encoding.UTF8.GetString(bytes).Contains($"\"{key}\"", StringComparison.Ordinal);
                Check.That(carries == rate.TicksPerOriginalFrame > 1, $"{key} at {rate}: the snapshot in the middle of a frame {(carries ? "carries" : "does not carry")} the step.");
                var restored = CnCNew.Simulation.Snapshots.WorldSnapshot.Restore(bytes, world.Templates, world.Configuration.DefinitionFingerprint, world.Terrain);
                Check.That(restored.StateHash == world.StateHash, $"{key} at {rate}: the restored world has the hash of the original.");
                for (var tick = 0; tick < world.Frames(12); tick++)
                    Check.That(world.Step().StateHash == restored.Step().StateHash, $"{key} at {rate}: the restored world continues hash-identically (tick {tick} after the snapshot).");
            }
    }
}

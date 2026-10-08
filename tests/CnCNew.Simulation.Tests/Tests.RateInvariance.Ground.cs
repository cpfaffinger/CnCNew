using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, ground movement (<c>docs/RATE-INVARIANCE-GROUND.md</c>): above 30 Hz <c>AIUpdateInterface</c> decides once per logic frame of the original and covers that frame's step
/// in one share per tick. These tests hold the consequence on the synthetic fixture: after every logic frame a lone object is, to the bit, where the 30 Hz step puts it, at 60 and
/// at 120 Hz; between two frames it is on the line between them; what ends a frame (arrival, crush, dock step) happens with the frame's last tick.
/// </summary>
static partial class Tests
{
    // Odd numbers on purpose: none of the per-tick constants of the 60 and 120 Hz catalogs is an exact part of the 30 Hz one.
    private static IReadOnlyDictionary<string, string> GroundRateFiles() => new Dictionary<string, string>(CollisionFiles())
    {
        ["Data/INI/Object/GroundRateFixture.ini"] = """
        Locomotor RateGroundLoco
          Surfaces = GROUND
          TurnRate = 113.0
          Speed = 47.0
          Acceleration = 31.0
          Braking = 53.0
          MinSpeed = 0.0
          SpeedDamaged = 29.0
          TurnRateDamaged = 61.0
          AccelerationDamaged = 17.0
        End
        Object RateWalker
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          Locomotor = SET_NORMAL RateGroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object RateCrusher
          KindOf = SELECTABLE VEHICLE
          CrusherLevel = 2
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Locomotor = SET_NORMAL RateGroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    private sealed record GroundTrace(List<string> Frames, List<long> ArrivalSteps, List<string> MidFrame);

    /// <summary>
    /// Spawns one object, lets a logic frame pass and drives it along <paramref name="legs"/>, each ordered before the step after the previous arrival. Records position, heading
    /// and order state after every logic frame, the step of every <c>MoveArrived</c>, and whether every tick inside a frame lies on the line between the two frame ends.
    /// </summary>
    private static GroundTrace GroundDrive(SimulationRate rate, bool terrain, string template, (int X, int Y)[] legs, int damagePercent = 0, (string Template, int X, int Y)[]? standing = null)
    {
        var map = terrain ? NavTerrain() : null;
        var (world, feeder, _) = RatedWorld(rate, GroundRateFiles(), map);
        var stride = rate.TicksPerOriginalFrame;
        feeder.Spawn(1, template, x: 30, y: 30);
        foreach (var other in standing ?? []) feeder.Spawn(1, other.Template, x: other.X, y: other.Y);
        for (var tick = 0; tick < stride; tick++) world.Step();
        var unit = world.FindEntity(new EntityId(IdOf(world, template)))!;
        if (damagePercent > 0)
        {
            feeder.Damage(unit.Id.Value, damagePercent);
            for (var tick = 0; tick < stride; tick++) world.Step();
        }
        var trace = new GroundTrace([], [], []);
        long step = 0;
        foreach (var (x, y) in legs)
        {
            Check.That(feeder.SetMoveTarget(1, unit.Id.Value, x, y).Accepted, $"{rate}: the move order is accepted.");
            var arrived = false;
            for (var frame = 0; frame < 600 && !arrived; frame++)
            {
                var beforeX = unit.X;
                var beforeY = unit.Y;
                var inside = new List<(Fixed X, Fixed Y)>();
                for (var tick = 0; tick < stride; tick++)
                {
                    var result = world.Step();
                    step++;
                    inside.Add((unit.X, unit.Y));
                    if (!result.Events.Any(value => value.Kind == SimEventKind.MoveArrived && value.Entity == unit.Id)) continue;
                    arrived = true;
                    trace.ArrivalSteps.Add(step);
                    Check.That(tick == 0, $"{rate}: the move ends with the first tick of its logic frame, the tick of the 30 Hz step (tick {tick} of {stride}).");
                }
                for (var tick = 0; tick < stride - 1; tick++)
                {
                    // Share i of n of the frame's step, cut off toward zero: within one step of the resolution of the exact point on the line.
                    var expectedX = beforeX.Raw + (unit.X.Raw - beforeX.Raw) * (tick + 1) / stride;
                    var expectedY = beforeY.Raw + (unit.Y.Raw - beforeY.Raw) * (tick + 1) / stride;
                    if (Math.Abs(inside[tick].X.Raw - expectedX) > 1 || Math.Abs(inside[tick].Y.Raw - expectedY) > 1)
                        trace.MidFrame.Add($"frame {trace.Frames.Count + 1} tick {tick}");
                }
                trace.Frames.Add($"{unit.X.Raw},{unit.Y.Raw},{unit.Z.Raw},{unit.Angle.Raw},{(unit.HasMoveTarget ? 1 : 0)}");
            }
            Check.That(arrived, $"{rate}: the object arrives at ({x}, {y}).");
            Check.That(unit.X == Fixed.FromInt(x) && unit.Y == Fixed.FromInt(y) && unit.Speed == Fixed.Zero && !unit.HasMoveTarget, $"{rate}: it stands on its target.");
        }
        return trace;
    }

    private static void GroundCompare(string what, bool terrain, string template, (int X, int Y)[] legs, int damagePercent = 0, (string Template, int X, int Y)[]? standing = null)
    {
        var reference = GroundDrive(SimulationRate.Hz30, terrain, template, legs, damagePercent, standing);
        Check.That(reference.Frames.Count > 60, $"{what}: the 30 Hz course takes more than 60 logic frames ({reference.Frames.Count}).");
        foreach (var (rate, factor) in WorldRates)
        {
            if (factor == 1) continue;
            var trace = GroundDrive(rate, terrain, template, legs, damagePercent, standing);
            Check.That(trace.Frames.Count == reference.Frames.Count, $"{what}, {rate}: the course takes {reference.Frames.Count} logic frames (measured {trace.Frames.Count}).");
            var first = Enumerable.Range(0, Math.Min(trace.Frames.Count, reference.Frames.Count)).FirstOrDefault(index => trace.Frames[index] != reference.Frames[index], -1);
            Check.That(first < 0, $"{what}, {rate}: position, height, heading and order state after every logic frame are those of 30 Hz (first difference after frame {first + 1}).");
            // Step s of 30 Hz is the logic frame s - 1; the move ends in the first tick of that frame.
            Check.That(trace.ArrivalSteps.SequenceEqual(reference.ArrivalSteps.Select(value => (value - 1) * factor + 1)),
                $"{what}, {rate}: every arrival falls into the same logic frame ({string.Join(",", trace.ArrivalSteps)} against {string.Join(",", reference.ArrivalSteps)} at 30 Hz).");
            Check.That(trace.MidFrame.Count == 0, $"{what}, {rate}: between two frames the object is on the line between them ({string.Join("; ", trace.MidFrame.Take(3))}).");
        }
    }

    /// <summary>Acceleration, braking, turning, arrival radius and the arrival itself, with a terrain (path following) and without (the straight mode), undamaged and damaged.</summary>
    public static void GroundRateInvarianceCourse()
    {
        var course = new[] { (150, 30), (150, 140), (40, 170), (33, 31) };
        GroundCompare("path following", terrain: true, "RateWalker", course);
        GroundCompare("straight mode", terrain: false, "RateWalker", course);
        GroundCompare("damaged locomotor", terrain: true, "RateWalker", course, damagePercent: 90);
        // A short hop inside the braking distance and a target within the arrival radius of one logic frame's travel.
        GroundCompare("short hops", terrain: true, "RateWalker", [(36, 30), (37, 30), (37, 31), (120, 31), (120, 32)]);
    }

    /// <summary>
    /// Standing units on the way: the look-ahead (25 logic frames of travel), the replan around them (at most once per second) and the local avoidance of a blocked step are
    /// decided once per logic frame with that frame's step, so the detour is the 30 Hz detour.
    /// </summary>
    public static void GroundRateInvarianceStandingUnits()
    {
        var onTheWay = new[] { ("Blocker", 92, 31), ("Blocker", 150, 96), ("Blocker", 60, 150) };
        var reference = GroundDrive(SimulationRate.Hz30, terrain: true, "RateWalker", [(150, 30)]);
        var detour = GroundDrive(SimulationRate.Hz30, terrain: true, "RateWalker", [(150, 30)], standing: onTheWay);
        Check.That(!detour.Frames.SequenceEqual(reference.Frames), "The standing unit changes the way of the 30 Hz run (the test would prove nothing otherwise).");
        GroundCompare("around standing units", terrain: true, "RateWalker", [(150, 30), (150, 140), (40, 170), (33, 31)], standing: onTheWay);
    }

    /// <summary>A snapshot taken between two decisions carries the rest of the frame's step: the restored world runs like the live one.</summary>
    public static void GroundRateInvarianceSnapshotInsideAFrame()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            if (factor == 1) continue;
            foreach (var terrain in new[] { true, false })
            {
                var map = terrain ? NavTerrain() : null;
                var (world, feeder, fixture) = RatedWorld(rate, GroundRateFiles(), map);
                feeder.Spawn(1, "RateWalker", x: 30, y: 30);
                for (var tick = 0; tick < factor; tick++) world.Step();
                var id = IdOf(world, "RateWalker");
                feeder.SetMoveTarget(1, id, 150, 120);
                // Stop one tick into a logic frame, while the object accelerates and turns.
                for (var tick = 0; tick < 9 * factor + 1; tick++) world.Step();
                var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, map);
                Check.That(restored.StateHash == world.StateHash, $"{rate}: the restored world has the hash of the live one inside a frame.");
                Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(WorldSnapshot.Write(world)), $"{rate}: its snapshot is the same bytes.");
                for (var tick = 0; tick < 400 * factor; tick++)
                {
                    world.Step();
                    restored.Step();
                    Check.That(restored.StateHash == world.StateHash, $"{rate} (terrain {terrain}): live and restored world agree {tick + 1} ticks after a snapshot inside a frame.");
                }
                Check.That(!world.FindEntity(new EntityId(id))!.HasMoveTarget, $"{rate}: the object arrived within the run.");
            }
        }
    }

    /// <summary>
    /// An order given inside a frame: the object finishes the step it is on (position and heading stay continuous) and decides on the new target with its next frame; an order
    /// given inside the last frame of a move cancels that arrival. At 30 Hz there is no "inside".
    /// </summary>
    public static void GroundRateInvarianceOrderInsideAFrame()
    {
        var drift = Fixed.FromFraction(1, 1000);
        foreach (var (rate, factor) in WorldRates)
        {
            if (factor == 1) continue;
            (SimulationWorld World, Feeder Feeder, Entity Unit) Start()
            {
                var (world, feeder, _) = RatedWorld(rate, GroundRateFiles(), NavTerrain());
                feeder.Spawn(1, "RateWalker", x: 30, y: 30);
                for (var tick = 0; tick < factor; tick++) world.Step();
                var unit = world.FindEntity(new EntityId(IdOf(world, "RateWalker")))!;
                feeder.SetMoveTarget(1, unit.Id.Value, 150, 30);
                return (world, feeder, unit);
            }

            // (a) A new order one tick into a frame of the cruise.
            {
                var (world, feeder, unit) = Start();
                for (var tick = 0; tick < 40 * factor + 1; tick++) world.Step();
                var x = unit.X;
                var y = unit.Y;
                feeder.SetMoveTarget(1, unit.Id.Value, 100, 160);
                var arrivals = 0;
                for (var tick = 0; tick < factor - 1; tick++)
                {
                    arrivals += world.Step().Events.Count(value => value.Kind == SimEventKind.MoveArrived);
                    Check.That(unit.X > x && (unit.Y - y).Abs() < drift, $"{rate}: the object covers the rest of the frame it was in when the order came (tick {tick + 1}).");
                    x = unit.X;
                }
                for (var tick = 0; tick < 3 * factor; tick++) arrivals += world.Step().Events.Count(value => value.Kind == SimEventKind.MoveArrived);
                Check.That(unit.Angle > drift, $"{rate}: with its next frame it turns toward the new target.");
                for (var tick = 0; tick < 2_000 * factor && unit.HasMoveTarget; tick++) arrivals += world.Step().Events.Count(value => value.Kind == SimEventKind.MoveArrived);
                // The move ends in the first tick of its last frame; the object covers the rest of that frame's step in the ticks after it.
                for (var tick = 1; tick < factor; tick++) arrivals += world.Step().Events.Count(value => value.Kind == SimEventKind.MoveArrived);
                Check.That(arrivals == 1 && unit.X == Fixed.FromInt(100) && unit.Y == Fixed.FromInt(160), $"{rate}: one arrival, at the new target ({arrivals}).");
            }

            // (b) A new order inside the last frame of the move: the tick of the arrival is known from an undisturbed twin.
            long arrivalTick;
            {
                var (world, _, unit) = Start();
                world.Step();
                while (unit.HasMoveTarget) world.Step();
                arrivalTick = world.Tick - 1;
            }
            {
                // The move ends in the first tick of its last frame, as the 30 Hz step ends it; an order in the tick after it finds an object that has arrived and is still
                // covering that frame's step: it covers it to the end and sets off to the new target with its next frame.
                var (world, feeder, unit) = Start();
                while (world.Tick < arrivalTick) world.Step();
                Check.That(unit.HasMoveTarget && unit.X < Fixed.FromInt(150), $"{rate}: one tick before the arrival the object is still on its way.");
                var arrivals = world.Step().Events.Count(value => value.Kind == SimEventKind.MoveArrived);
                Check.That(arrivals == 1 && !unit.HasMoveTarget && unit.X < Fixed.FromInt(150), $"{rate}: the move ends in the first tick of its last frame, a share of the step before the target.");
                feeder.SetMoveTarget(1, unit.Id.Value, 100, 160);
                for (var tick = 1; tick < factor; tick++) arrivals += world.Step().Events.Count(value => value.Kind == SimEventKind.MoveArrived);
                Check.That(unit.HasMoveTarget && unit.X == Fixed.FromInt(150) && (unit.Y - Fixed.FromInt(30)).Abs() < drift, $"{rate}: the object still covers that frame's step to the old target.");
                for (var tick = 0; tick < 2_000 * factor && unit.HasMoveTarget; tick++) arrivals += world.Step().Events.Count(value => value.Kind == SimEventKind.MoveArrived);
                for (var tick = 1; tick < factor; tick++) arrivals += world.Step().Events.Count(value => value.Kind == SimEventKind.MoveArrived);
                Check.That(arrivals == 2 && unit.X == Fixed.FromInt(100) && unit.Y == Fixed.FromInt(160), $"{rate}: a second arrival, at the new target ({arrivals}).");
            }
        }
    }

    /// <summary>
    /// Footprint collisions are sampled once per logic frame, at the end of the frame's step and with its whole displacement: a crusher kills the same soldier in the same logic
    /// frame at every rate, and a tree it runs into is pushed in the same frame.
    /// </summary>
    public static void GroundRateInvarianceCollisionsPerFrame()
    {
        long crushFrame30 = -1, toppleFrame30 = -1;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, GroundRateFiles(), NavTerrain());
            feeder.Spawn(1, "RateCrusher", x: 30, y: 30);
            feeder.Spawn(2, "Infantry", x: 70, y: 30);
            feeder.Spawn(0, "Tree", x: 120, y: 30);
            for (var tick = 0; tick < factor; tick++) world.Step();
            var crusher = IdOf(world, "RateCrusher");
            var soldier = new EntityId(IdOf(world, "Infantry"));
            var tree = world.FindEntity(new EntityId(IdOf(world, "Tree")))!;
            var topple = tree.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.ToppleUpdate>().Single();
            feeder.SetMoveTarget(1, crusher, 170, 30);
            long crushStep = -1, toppleStep = -1;
            for (var step = 1; step <= 400 * factor && (crushStep < 0 || toppleStep < 0); step++)
            {
                var result = world.Step();
                if (crushStep < 0 && result.Events.Any(value => value.Kind == SimEventKind.EntityDied && value.Entity == soldier)) crushStep = step;
                if (toppleStep < 0 && !topple.IsAbleToBeToppled) toppleStep = step;
            }
            Check.That(crushStep > 0 && toppleStep > 0, $"{rate}: the soldier is crushed and the tree pushed (steps {crushStep}, {toppleStep}).");
            Check.That(crushStep % factor == 0 && toppleStep % factor == 0, $"{rate}: both happen with the last tick of a logic frame (steps {crushStep}, {toppleStep}).");
            if (factor == 1)
            {
                crushFrame30 = crushStep;
                toppleFrame30 = toppleStep;
                continue;
            }
            Check.That(crushStep == crushFrame30 * factor, $"{rate}: the soldier dies in logic frame {crushFrame30} (measured step {crushStep}).");
            Check.That(toppleStep == toppleFrame30 * factor, $"{rate}: the tree is pushed in logic frame {toppleFrame30} (measured step {toppleStep}).");
        }
    }

    /// <summary>
    /// The dock machine and the supply machine take one step per logic frame: a hauler's deposits fall on the same logic frames at every rate, and its position after every
    /// frame is that of 30 Hz.
    /// </summary>
    public static void GroundRateInvarianceSupplyTrips()
    {
        List<long>? deposits30 = null;
        List<string>? frames30 = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, SupplyFiles());
            feeder.Spawn(1, "Depot", x: -100);
            feeder.Spawn(1, "Warehouse", x: 100);
            feeder.Spawn(1, "Hauler", x: 0);
            for (var tick = 0; tick < factor; tick++) world.Step();
            var hauler = world.FindEntity(new EntityId(IdOf(world, "Hauler")))!;
            Check.That(world.Submit(new DockCommand(new PlayerId(1), 900, world.Tick, hauler.Id, new EntityId(IdOf(world, "Warehouse")))).Accepted, $"{rate}: the dock order is accepted.");
            var start = world.Tick;
            var deposits = new List<long>();
            var frames = new List<string>();
            for (var frame = 0; frame < 3_000; frame++)
            {
                for (var tick = 0; tick < factor; tick++)
                    foreach (var item in world.Step().Events)
                        if (item.Kind == SimEventKind.SupplyDeposited) deposits.Add(item.Tick - start);
                frames.Add($"{hauler.X.Raw},{hauler.Y.Raw},{(hauler.HasMoveTarget ? 1 : 0)},{world.MoneyOf(new PlayerId(1)).Raw}");
            }
            Check.That(deposits.Count == 3, $"{rate}: three deposits empty the warehouse (measured {deposits.Count}).");
            if (factor == 1)
            {
                deposits30 = deposits;
                frames30 = frames;
                continue;
            }
            Check.That(deposits.SequenceEqual(deposits30!.Select(value => value * factor)), $"{rate}: deposits at ticks {string.Join(",", deposits)}, at 30 Hz {string.Join(",", deposits30!)}.");
            var first = Enumerable.Range(0, frames.Count).FirstOrDefault(index => frames[index] != frames30![index], -1);
            Check.That(first < 0, $"{rate}: the hauler and the ledger after every logic frame are those of 30 Hz (first difference after frame {first + 1}).");
        }
    }
}

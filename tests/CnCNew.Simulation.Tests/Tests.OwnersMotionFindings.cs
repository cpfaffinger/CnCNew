using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The owner's acceptance of 1 November 2026, motion and placement (docs/SIMULATION-CORE.md §75): each a test that fails on the code before its fix, at 30, 60 and 120 Hz.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> MotionFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/MotionFixture.ini"] = """
        Locomotor MotionTread
          Surfaces = GROUND
          TurnRate = 180.0
          Speed = 30.0
          Acceleration = 300.0
          Braking = 300.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Locomotor MotionFoot
          Surfaces = GROUND
          TurnRate = 720.0
          Speed = 18.0
          Acceleration = 300.0
          Braking = 300.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Object MotionTank
          KindOf = SELECTABLE VEHICLE
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          BuildCost = 10
          BuildTime = 1
          Locomotor = SET_NORMAL MotionTread
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object MotionFactory
          KindOf = STRUCTURE IMMOBILE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:-70.0 Y:0.0 Z:0.0
            NaturalRallyPoint = X:-90.0 Y:0.0 Z:0.0
          End
        End
        Object MotionMan
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL MotionFoot
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>The widest change of a walker's direction between two logic frames of the original, in radians, and its largest step aside.</summary>
    private static (double Turn, double Aside) WatchLine(SimulationWorld world, Entity unit, int factor, long frames, int lineY)
    {
        double turn = 0, aside = 0;
        var last = (X: unit.X, Y: unit.Y);
        double? heading = null;
        for (var frame = 0; frame < frames; frame++)
        {
            for (var tick = 0; tick < factor; tick++) world.Step();
            var (dx, dy) = ((unit.X - last.X).Raw / (double)Fixed.ScaleRaw, (unit.Y - last.Y).Raw / (double)Fixed.ScaleRaw);
            last = (unit.X, unit.Y);
            aside = Math.Max(aside, Math.Abs((unit.Y - Fixed.FromInt(lineY)).Raw / (double)Fixed.ScaleRaw));
            if (Math.Abs(dx) + Math.Abs(dy) < 1e-6) { heading = null; continue; }
            var now = Math.Atan2(dy, dx);
            if (heading is { } before) turn = Math.Max(turn, Math.Abs(Math.IEEERemainder(now - before, 2 * Math.PI)));
            heading = now;
        }
        return (turn, aside);
    }

    /// <summary>
    /// UNIT-10: <c>DefaultProductionExitUpdate::exitObjectViaDoor</c> "make sure the point is on the terrain": a factory on a step of the ground makes its unit on the ground of the place
    /// it appears at, not at the factory's own height. On a ramp in the model the unit stays on the terrain too: the logic has no other height.
    /// </summary>
    public static void AUnitAppearsOnTheGroundOfItsPlace()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, (x, _) => x >= 26 ? 40 : 0), "step");
            var (world, feeder, _) = RatedWorld(rate, MotionFiles(), terrain);
            feeder.GiveMoney(1, 100);
            feeder.Spawn(1, "MotionFactory", owner: 1, x: 300, y: 300);
            for (var tick = 0; tick < factor; tick++) world.Step();
            var factory = world.Entities.Single(value => value.Template.Name == "MotionFactory");
            Check.That(factory.Z > world.GroundHeightAt(Fixed.FromInt(230), Fixed.FromInt(300)) + Fixed.FromInt(10), $"{rate}: the factory stands above the place of its exit.");
            Check.That(feeder.QueueProduction(1, factory.Id.Value, "MotionTank").Accepted, $"{rate}: the tank is queued.");
            Entity? tank = null;
            for (var tick = 0; tick < world.Frames(120) && tank is null; tick++)
            {
                world.Step();
                tank = world.Entities.SingleOrDefault(value => value.Template.Name == "MotionTank");
            }
            Check.That(tank is not null, $"{rate}: the tank appears.");
            Check.That(tank!.Z == world.GroundHeightAt(tank.X, tank.Y), $"{rate}: on the ground ({(tank.Z.Raw - world.GroundHeightAt(tank.X, tank.Y).Raw) / (double)Fixed.ScaleRaw:F2} units above it).");
        }
    }

    /// <summary>
    /// UNIT-02: a tank behind infantry that walks the same way stuttered: the bounded avoidance shoved its step aside by 45 degrees, the path pulled it back, frame after frame.
    /// <c>AIUpdateInterface::processCollision</c> holds it behind the soldier at his speed instead (<c>calculateMaxBlockedSpeed</c>, <c>m_bumpSpeedLimit</c>).
    /// </summary>
    public static void TankBehindInfantryFollowsWithoutAStutter()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, MotionFiles(), TerrainMap.Build(SyntheticMaps.Build(60, 60, 1), "motion"));
            feeder.Spawn(1, "MotionTank", owner: 1, x: 40, y: 300);
            feeder.Spawn(1, "MotionMan", owner: 1, x: 72, y: 300);
            for (var tick = 0; tick < factor; tick++) world.Step();
            var tank = world.Entities.Single(value => value.Template.Name == "MotionTank");
            var man = world.Entities.Single(value => value.Template.Name == "MotionMan");
            Check.That(feeder.SetMoveTarget(1, tank.Id.Value, 500, 300).Accepted && feeder.SetMoveTarget(1, man.Id.Value, 500, 300).Accepted, $"{rate}: both are ordered along the line.");
            var (turn, aside) = WatchLine(world, tank, factor, 600, 300);
            Check.That(aside < 0.01, $"{rate}: the tank stays on its line ({aside:F3} units aside).");
            Check.That(turn < 0.01, $"{rate}: the tank does not change direction ({turn:F3} rad between two frames).");
            Check.That(man.X - tank.X < Fixed.FromInt(25), $"{rate}: the tank has caught up with the soldier ({(man.X - tank.X).Raw / Fixed.ScaleRaw}).");
            Check.That(man.X > tank.X, $"{rate}: and stays behind him, on his line.");
            var perFrame = tank.Speed.Raw * factor / (double)Fixed.ScaleRaw;
            Check.That(perFrame <= 0.75, $"{rate}: it drives at his 0.6 units per frame, not its own 1.0 ({perFrame:F3}).");
        }
    }

    /// <summary><c>Lift</c> is no key of the locomotor schema and arrives as text; it was read as 0 for every locomotor, and only <c>LiftDamaged</c> took effect.</summary>
    public static void ALocomotorReadsItsLift()
    {
        var (world, _) = FlightRateWorld(SimulationRate.Hz30, WingAirfieldFiles, terrain: false);
        Check.That(world.Templates.TryGet("AirTestJet", out var jet) && jet.Locomotor is { } locomotor && locomotor.Lift == Fixed.FromDecimal(120m / 900m), "The jet's Lift of 120 per second² is its lift per frame².");
    }

    /// <summary>
    /// UNIT-19: the height of a landing was a straight line from the cruising height to the end of the runway: the sink began at once and ended at full speed on the ground. The original holds
    /// the height with the locomotor's lift (<c>Locomotor::calcLiftToUseAtPt</c>): the sink builds up, is braked in time, and the wheels touch softly. The aircraft does not go back for an
    /// approach point it has passed and does not stand still in the air for the state change.
    /// </summary>
    public static void AJetFlaresItsLanding()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder) = FlightRateWorld(rate, WingAirfieldFiles, terrain: false);
            feeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
            StepFrames(world, 1);
            feeder.GiveMoney(1, 1000);
            StepFrames(world, 1);
            var airfield = Only(world, "AirTestAirfield");
            var track = new List<(Fixed X, Fixed Y, Fixed Z)>();
            BitTrack(world, "AirTestJet", 1500, (frame, jet) =>
            {
                if (frame == 0) Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestJet").Accepted, $"{rate}: the jet is queued.");
                if (jet is null) return;
                if (frame == 100) Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 1200, 300).Accepted, $"{rate}: the parked jet is sent away.");
                if (JetOf(jet).Phase == JetAIUpdate.JetPhase.Landing) track.Add((jet.X, jet.Y, jet.Z));
            });
            Check.That(track.Count > 40, $"{rate}: the jet lands ({track.Count} frames).");
            double D(Fixed value) => value.Raw / (double)Fixed.ScaleRaw;
            var ground = D(track[^1].Z);
            double hardest = 0, touchdown = 0, standing = 0;
            for (var index = 2; index < track.Count; index++)
            {
                var (sink, before) = (D(track[index].Z) - D(track[index - 1].Z), D(track[index - 1].Z) - D(track[index - 2].Z));
                hardest = Math.Max(hardest, Math.Abs(sink - before));
                if (D(track[index].Z) == ground && D(track[index - 1].Z) > ground) touchdown = -sink;
                if (D(track[index].Z) > ground && track[index].X == track[index - 1].X && track[index].Y == track[index - 1].Y) standing++;
            }
            Check.That(hardest < 0.5, $"{rate}: the sink changes softly ({hardest:F3} units per frame between two frames).");
            Check.That(touchdown < 0.6, $"{rate}: the wheels touch softly ({touchdown:F3} units per frame).");
            Check.That(standing == 0, $"{rate}: the jet does not stand in the air ({standing} frames).");
        }
    }

    /// <summary><c>blockedBy</c>: a unit beside the mover, off 45 degrees and going its own way, holds nothing up: the two overlap for a moment, as in the original.</summary>
    public static void AUnitBesideAMoverIsNoObstacle()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, MotionFiles(), TerrainMap.Build(SyntheticMaps.Build(60, 60, 1), "motion"));
            feeder.Spawn(1, "MotionTank", owner: 1, x: 40, y: 300);
            feeder.Spawn(1, "MotionMan", owner: 1, x: 72, y: 318);
            for (var tick = 0; tick < factor; tick++) world.Step();
            var tank = world.Entities.Single(value => value.Template.Name == "MotionTank");
            var man = world.Entities.Single(value => value.Template.Name == "MotionMan");
            Check.That(feeder.SetMoveTarget(1, tank.Id.Value, 500, 300).Accepted && feeder.SetMoveTarget(1, man.Id.Value, 500, 318).Accepted, $"{rate}: both are ordered along their lines.");
            var (turn, aside) = WatchLine(world, tank, factor, 300, 300);
            Check.That(aside < 0.01 && turn < 0.01, $"{rate}: the tank passes the soldier beside it ({aside:F3} units aside, {turn:F3} rad).");
            Check.That(tank.X > man.X, $"{rate}: and overtakes him.");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Fixed-wing flight (<c>docs/SIM-JET-FLIGHT.md</c>): an aircraft whose locomotor has <c>Appearance = WINGS</c> never flies slower than its <c>MinSpeed</c>, turns at no more than its <c>TurnRate</c>,
/// and circles at <c>CirclingRadius</c> (positive clockwise, negative counter-clockwise, 0 the smallest circle) instead of stopping where its move order ends; helicopters are unchanged.
/// </summary>
static partial class Tests
{
    private static readonly IReadOnlyDictionary<string, string> WingFixtureFiles = new Dictionary<string, string>(AirFixtureFiles)
    {
        ["Data/INI/Object/WingFixture.ini"] = """
        Locomotor WingLoco
          Surfaces = AIR
          Speed = 150.0
          Acceleration = 120.0
          Braking = 60.0
          MinSpeed = 60.0
          MinTurnSpeed = 150.0
          TurnRate = 120.0
          Lift = 120.0
          PreferredHeight = 60.0
          CloseEnoughDist = 4.0
          CirclingRadius = 100.0
          Appearance = WINGS
        End
        Locomotor WingLocoCcw
          Surfaces = AIR
          Speed = 150.0
          Acceleration = 120.0
          Braking = 60.0
          MinSpeed = 60.0
          TurnRate = 120.0
          Lift = 120.0
          PreferredHeight = 60.0
          CloseEnoughDist = 4.0
          CirclingRadius = -100.0
          Appearance = WINGS
        End
        Locomotor WingLocoSmallest
          Surfaces = AIR
          Speed = 150.0
          Acceleration = 120.0
          Braking = 60.0
          MinSpeed = 60.0
          TurnRate = 120.0
          Lift = 120.0
          PreferredHeight = 60.0
          CloseEnoughDist = 4.0
          CirclingRadius = 0.0
          Appearance = WINGS
        End
        Locomotor WingLocoWeakBrake
          Surfaces = AIR
          Speed = 150.0
          Acceleration = 120.0
          Braking = 10.0
          MinSpeed = 60.0
          TurnRate = 120.0
          Lift = 120.0
          PreferredHeight = 60.0
          CloseEnoughDist = 4.0
          CirclingRadius = 100.0
          Appearance = WINGS
        End

        Object WingJet
          Side = Alpha
          KindOf = SELECTABLE AIRCRAFT CAN_ATTACK VEHICLE
          Locomotor = SET_NORMAL WingLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = JetAIUpdate Jet01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object WingJetCcw
          Side = Alpha
          KindOf = SELECTABLE AIRCRAFT CAN_ATTACK VEHICLE
          Locomotor = SET_NORMAL WingLocoCcw
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = JetAIUpdate Jet01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object WingJetSmallest
          Side = Alpha
          KindOf = SELECTABLE AIRCRAFT CAN_ATTACK VEHICLE
          Locomotor = SET_NORMAL WingLocoSmallest
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = JetAIUpdate Jet01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object WingJetWeakBrake
          Side = Alpha
          KindOf = SELECTABLE AIRCRAFT CAN_ATTACK VEHICLE
          Locomotor = SET_NORMAL WingLocoWeakBrake
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = JetAIUpdate Jet01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static readonly Fixed WingMinSpeed = SimulationTiming.PerSecondToPerTick(60m);
    private static readonly Fixed WingTurnPerTick = SimulationTiming.DegreesPerSecondToRadiansPerTick(120m);

    private static SimulationWorld WingWorld(out SyntheticFixture fixture, out Feeder feeder, params (string Template, int X, int Y)[] jets)
    {
        var world = AirWorld(out fixture, out feeder, files: WingFixtureFiles);
        foreach (var (template, x, y) in jets) Check.That(feeder.Spawn(1, template, owner: 1, x: x, y: y).Accepted, $"{template} spawns.");
        world.Step();
        return world;
    }

    private static Fixed Distance2D(Fixed ax, Fixed ay, Fixed bx, Fixed by) => Fixed.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));

    private static (Fixed X, Fixed Y, Fixed Angle, Fixed Speed) Pose(Entity entity) => (entity.X, entity.Y, entity.Angle, entity.Speed);

    private static decimal DistanceTo(Entity entity, int x, int y)
    {
        var dx = (double)(entity.X - Fixed.FromInt(x)).ToDecimal();
        var dy = (double)(entity.Y - Fixed.FromInt(y)).ToDecimal();
        return (decimal)Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// A winged jet without an airfield flies from the start. It never falls below its minimum speed, not at the end of a move order (it does not stop), not while it turns, and its position changes
    /// on every tick; helicopters (no <c>WINGS</c>) stop at the end of their order as before.
    /// </summary>
    public static void WingMinimumSpeedAndNoStopping()
    {
        var world = WingWorld(out _, out var feeder, ("WingJet", 500, 500), ("AirTestHeli", 900, 100));
        var jet = Get(world, "WingJet");
        var heli = Get(world, "AirTestHeli");
        Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 1500, 500).Accepted, "The move order is accepted.");
        Check.That(feeder.SetMoveTarget(1, heli.Id.Value, 1300, 100).Accepted, "The helicopter is ordered, too.");
        var arrived = -1;
        var slowest = Fixed.MaxValue;
        var shortestStep = Fixed.MaxValue;
        for (var tick = 0; tick < 900; tick++)
        {
            var before = Pose(jet);
            world.Step();
            if (arrived < 0 && !jet.HasMoveTarget) arrived = tick;
            slowest = Fixed.Min(slowest, jet.Speed);
            var step = Distance2D(before.X, before.Y, jet.X, jet.Y);
            shortestStep = Fixed.Min(shortestStep, step);
        }
        Check.That(arrived > 0 && arrived < 400, $"The jet reached the end of its order (tick {arrived}).");
        Check.That(slowest >= WingMinSpeed, $"The jet never fell below its minimum speed ({slowest} vs {WingMinSpeed}).");
        Check.That(shortestStep >= WingMinSpeed - Fixed.FromRaw(64), $"It moved by at least the minimum speed on every tick, also after its order ended ({shortestStep}).");
        Check.That(jet.Z == Fixed.FromInt(60), $"It flies at the preferred height ({jet.Z}).");

        // a helicopter has no minimum speed: it stops where its order ends
        Check.That(!heli.HasMoveTarget && heli.Speed == Fixed.Zero, $"The helicopter stands still at the end of its order (speed {heli.Speed}).");
        var parked = (heli.X, heli.Y);
        for (var i = 0; i < 30; i++) world.Step();
        Check.That((heli.X, heli.Y) == parked, "It stays where it stopped.");
    }

    /// <summary>
    /// Turning is limited by the turn rate (120 degrees/s, 4 degrees per tick) whatever the speed; a destination behind the jet is reached by a loop whose heading changes at most that much per tick, and
    /// whose position never jumps.
    /// </summary>
    public static void WingTurnRateIsLimited()
    {
        var world = WingWorld(out _, out var feeder, ("WingJet", 1000, 1000));
        var jet = Get(world, "WingJet");
        Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 1500, 1000).Accepted, "The jet is sent east.");
        for (var i = 0; i < 40; i++) world.Step();
        Check.That(FixedTrig.AngleDifference(jet.Angle, Fixed.Zero).Abs() < Fixed.FromDecimal(0.01m), "It flies east.");
        // now a destination straight behind it
        Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 600, 1000).Accepted, "The destination behind the jet is accepted.");
        var maxTurn = Fixed.Zero;
        var maxStep = Fixed.Zero;
        var turned = 0;
        var farthest = Fixed.Zero;
        for (var tick = 0; tick < 500; tick++)
        {
            var before = Pose(jet);
            world.Step();
            var turn = FixedTrig.AngleDifference(before.Angle, jet.Angle).Abs();
            maxTurn = Fixed.Max(maxTurn, turn);
            if (turn > Fixed.Zero) turned++;
            maxStep = Fixed.Max(maxStep, Distance2D(before.X, before.Y, jet.X, jet.Y));
            farthest = Fixed.Max(farthest, jet.X);
        }
        Check.That(maxTurn <= WingTurnPerTick + Fixed.FromRaw(2), $"No tick turns the jet by more than the turn rate ({maxTurn} vs {WingTurnPerTick}).");
        Check.That(turned >= 40, $"A U-turn takes many ticks ({turned}).");
        Check.That(maxStep <= SimulationTiming.PerSecondToPerTick(150m) + Fixed.FromRaw(64), $"It never moves faster than its speed ({maxStep}).");
        Check.That(farthest > Fixed.FromInt(1000 + 40), $"The turn is a loop, not a pivot: the jet flew on to x = {farthest}.");
    }

    /// <summary>
    /// A jet that reached its destination circles it: clockwise for a positive <c>CirclingRadius</c>, counter-clockwise for a negative one, and the smallest circle the minimum speed and the turn rate allow
    /// for 0. The circle is around the destination at about the radius, the speed settles at the minimum speed, the track is continuous.
    /// </summary>
    public static void WingCirclesItsDestination()
    {
        var world = WingWorld(out _, out var feeder, ("WingJet", 500, 1000), ("WingJetCcw", 500, 1500), ("WingJetSmallest", 500, 2000), ("WingJetWeakBrake", 500, 2500));
        var jets = new[] { Get(world, "WingJet"), Get(world, "WingJetCcw"), Get(world, "WingJetSmallest"), Get(world, "WingJetWeakBrake") };
        var centers = new[] { (1200, 1000), (1200, 1500), (1200, 2000), (1200, 2500) };
        for (var index = 0; index < jets.Length; index++) Check.That(feeder.SetMoveTarget(1, jets[index].Id.Value, centers[index].Item1, centers[index].Item2).Accepted, "Move orders are accepted.");
        for (var tick = 0; tick < 900; tick++) world.Step();
        var minTurnRadius = (decimal)(WingMinSpeed / WingTurnPerTick).ToDecimal();
        var measured = new List<string>();
        for (var index = 0; index < jets.Length; index++)
        {
            var jet = jets[index];
            Check.That(!jet.HasMoveTarget, $"{jet.Template.Name} reached the end of its order.");
            var (cx, cy) = centers[index];
            decimal minRadius = decimal.MaxValue, maxRadius = 0, orbit = 0;
            var slowest = Fixed.MaxValue;
            for (var tick = 0; tick < 600; tick++)
            {
                var before = Pose(jet);
                world.Step();
                var radius = DistanceTo(jet, cx, cy);
                minRadius = Math.Min(minRadius, radius);
                maxRadius = Math.Max(maxRadius, radius);
                slowest = Fixed.Min(slowest, jet.Speed);
                // z component of (position - center) x (velocity): negative is clockwise (x east, y north)
                var rx = (double)(before.X - Fixed.FromInt(cx)).ToDecimal();
                var ry = (double)(before.Y - Fixed.FromInt(cy)).ToDecimal();
                var vx = (double)(jet.X - before.X).ToDecimal();
                var vy = (double)(jet.Y - before.Y).ToDecimal();
                orbit += (decimal)(rx * vy - ry * vx);
            }
            measured.Add($"{jet.Template.Name}: radius {minRadius:F1}..{maxRadius:F1}, orbit {(orbit < 0 ? "clockwise" : "counter-clockwise")}, slowest {slowest}");
            Check.That(slowest >= WingMinSpeed, $"{jet.Template.Name} circles at no less than the minimum speed ({slowest}).");
            switch (index)
            {
                case 0 or 3:
                    Check.That(orbit < 0, $"A positive CirclingRadius circles clockwise ({measured[^1]}).");
                    Check.That(minRadius > 70m && maxRadius < 110m, $"It circles at about the radius 100 ({measured[^1]}).");
                    break;
                case 1:
                    Check.That(orbit > 0, $"A negative CirclingRadius circles counter-clockwise ({measured[^1]}).");
                    Check.That(minRadius > 70m && maxRadius < 110m, $"It circles at about the radius 100 ({measured[^1]}).");
                    break;
                default:
                    Check.That(orbit < 0, $"CirclingRadius 0 circles ({measured[^1]}).");
                    Check.That(maxRadius < 2m * minTurnRadius && maxRadius > minTurnRadius / 2m, $"CirclingRadius 0 flies the smallest circle the minimum speed and the turn rate allow (about {minTurnRadius:F1}; {measured[^1]}).");
                    break;
            }
        }
        foreach (var line in measured) Console.WriteLine("       wing circle: " + line);
    }

    /// <summary>The circle (its center and the speed) survives a snapshot: the restored world follows the original through the circling.</summary>
    public static void WingCircleSurvivesSnapshots()
    {
        var world = WingWorld(out var fixture, out var feeder, ("WingJet", 500, 1000));
        var jet = Get(world, "WingJet");
        Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 1000, 1000).Accepted, "The order is accepted.");
        for (var tick = 0; tick < 300; tick++) world.Step();
        Check.That(!jet.HasMoveTarget, "The jet circles.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
        Check.That(restored.StateHash == world.StateHash, "The snapshot of a circling jet restores to the same hash.");
        for (var follow = 0; follow < 200; follow++)
            Check.That(restored.Step().StateHash == world.Step().StateHash, $"The restored world follows the circling jet (step {follow}).");
        // a new order leaves the circle
        Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 2000, 1000).Accepted, "A new order is accepted.");
        world.Step();
        for (var tick = 0; tick < 500 && jet.HasMoveTarget; tick++) world.Step();
        Check.That(!jet.HasMoveTarget && DistanceTo(jet, 2000, 1000) < 130m, $"The jet left the first circle for the new destination ({jet.X}, {jet.Y}).");
    }

    /// <summary>The full airfield lifecycle with a winged locomotor: take-off, flight, empty clip, return, landing, reload; the jet is never below its minimum speed in the air.</summary>
    public static void WingJetFlightLifecycle()
    {
        var world = AirWorld(out var fixture, out var feeder, files: WingAirfieldFiles);
        feeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
        feeder.Spawn(2, "AirDummy", owner: 2, x: 1500, y: 500);
        world.Step();
        feeder.GiveMoney(1, 1000);
        world.Step();
        var airfield = Get(world, "AirTestAirfield");
        Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestJet").Accepted, "The jet is queued.");
        StepUntil(world, () => world.Entities.Any(value => value.Template.Name == "AirTestJet"), 200, "The airfield produced the jet.");
        var jet = Get(world, "AirTestJet");
        var ai = JetOf(jet);
        Check.That(jet.Template.Locomotor is { IsWings: true }, "The test jet flies with a WINGS locomotor.");
        StepUntil(world, () => ai.Phase == JetAIUpdate.JetPhase.None, 600, "The jet parked.");
        Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 1200, 500).Accepted, "The move order is accepted.");
        StepUntil(world, () => ai.AllowAirLoco && ai.Phase == JetAIUpdate.JetPhase.None && !jet.HasMoveTarget, 1500, "The jet took off and reached its destination.");
        var slowest = Fixed.MaxValue;
        var dummy = Get(world, "AirDummy");
        var shots = 0;
        var phases = new List<JetAIUpdate.JetPhase>();
        for (var tick = 0; tick < 400; tick++)
        {
            if (jet.WeaponClip > 0 && world.Tick >= jet.WeaponReadyTick && feeder.FireWeapon(1, jet.Id.Value, dummy.Id.Value).Accepted) shots++;
            world.Step();
            if (phases.Count == 0 || phases[^1] != ai.Phase) phases.Add(ai.Phase);
            if (jet.Z > Fixed.FromInt(25) && ai.AllowAirLoco) slowest = Fixed.Min(slowest, jet.Speed);
        }
        Check.That(shots == 2 && jet.WeaponClip == 0, $"The jet fired its clip ({shots} shots).");
        Check.That(slowest >= WingMinSpeed, $"In the air the jet never fell below the minimum speed ({slowest}).");
        for (var tick = 0; tick < 4000 && !(phases.Contains(JetAIUpdate.JetPhase.Landing) && ai.Phase == JetAIUpdate.JetPhase.None && !ai.AllowAirLoco); tick++)
        {
            world.Step();
            if (phases.Count == 0 || phases[^1] != ai.Phase) phases.Add(ai.Phase);
            if (jet.Z > Fixed.FromInt(25) && ai.AllowAirLoco && ai.Phase != JetAIUpdate.JetPhase.Landing) slowest = Fixed.Min(slowest, jet.Speed);
        }
        Check.That(!ai.AllowAirLoco && ai.Phase == JetAIUpdate.JetPhase.None && jet.WeaponClip == 2, $"The jet is parked again with a full clip: {string.Join(">", phases)} (clip {jet.WeaponClip}).");
        Check.That(phases.Contains(JetAIUpdate.JetPhase.ReturningForLanding) && phases.Contains(JetAIUpdate.JetPhase.Landing) && phases.Contains(JetAIUpdate.JetPhase.ReloadAmmo), "It returned, landed and reloaded: " + string.Join(">", phases));
        Check.That(slowest >= WingMinSpeed, $"On the way home it never fell below the minimum speed either ({slowest}).");
    }

    /// <summary>The airfield fixture with the jet's locomotor turned into a winged one (turn rate 120 degrees/s, circling radius 100).</summary>
    private static readonly IReadOnlyDictionary<string, string> WingAirfieldFiles = new Dictionary<string, string>(AirFixtureFiles)
    {
        ["Data/INI/Object/AirFixture.ini"] = AirFixtureFiles["Data/INI/Object/AirFixture.ini"].Replace(
            "Locomotor AirJetLoco\n  Surfaces = AIR\n  Speed = 150.0\n  Acceleration = 450.0\n  Braking = 60.0\n  MinSpeed = 60.0\n  TurnRate = 360.0",
            "Locomotor AirJetLoco\n  Appearance = WINGS\n  CirclingRadius = 100.0\n  Surfaces = AIR\n  Speed = 150.0\n  Acceleration = 450.0\n  Braking = 60.0\n  MinSpeed = 60.0\n  TurnRate = 120.0"),
    };
}

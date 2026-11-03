using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// A destination of its own for every unit (fidelity finding F-04, <c>docs/SIMULATION-CORE.md</c> §44.2): <c>AIGroup::groupMoveToPosition</c> with
/// <c>computeIndividualDestination</c> for a group order (<see cref="GroupMoveCommand"/>), <c>Pathfinder::adjustDestination</c> for every move at the order.
/// </summary>
internal static partial class Tests
{
    private static IReadOnlyDictionary<string, string> GroupMoveFiles() => new Dictionary<string, string>(TransportFiles())
    {
        ["Data/INI/Object/GroupMoveFixture.ini"] = """
        Object Marcher
          KindOf = INFANTRY SELECTABLE
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL WalkLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, CnCNew.Simulation.Testing.SyntheticFixture Fixture, Entity[] Units) GroupMoveWorld(SimulationRate rate, params (int X, int Y)[] places)
    {
        var (world, feeder, fixture) = RatedWorld(rate, GroupMoveFiles());
        foreach (var (x, y) in places) feeder.Spawn(1, "Marcher", x: x, y: y);
        for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
        return (world, feeder, fixture, world.Entities.Where(value => value.Template.Name == "Marcher").OrderBy(value => value.Id.Value).ToArray());
    }

    private static SubmitResult GroupMove(SimulationWorld world, IEnumerable<Entity> units, int x, int y, bool attack = false, ulong sequence = 1000)
        => world.Submit(new GroupMoveCommand(new PlayerId(1), sequence, world.Tick, [..units.Select(unit => unit.Id)], Fixed.FromInt(x), Fixed.FromInt(y), attack));

    /// <summary>The members of a group keep their offsets from the member nearest to the goal, shortened to six bounding radii, and nobody shares a place.</summary>
    public static void GroupMoveGivesEveryUnitItsOwnDestination()
    {
        // Three in a row 12 apart (bounding radius 5), one far behind (300 away: more than six bounding radii).
        var (world, _, _, units) = GroupMoveWorld(TestRate.Default, (100, 100), (100, 112), (100, 124), (100, 400));
        var radius = units[0].Template.BoundingCircleRadius;
        Check.That(GroupMove(world, units, 500, 100).Accepted, "The group order is accepted.");
        world.Step();
        // Nearest to (500, 100) is the unit at (100, 100): it is the centre and goes to the goal itself.
        Check.That(units[0].MoveTargetX == Fixed.FromInt(500) && units[0].MoveTargetY == Fixed.FromInt(100), $"The nearest member goes to the goal ({units[0].MoveTargetX}, {units[0].MoveTargetY}).");
        Check.That(units[1].MoveTargetX == Fixed.FromInt(500) && units[1].MoveTargetY == Fixed.FromInt(112) && units[2].MoveTargetX == Fixed.FromInt(500) && units[2].MoveTargetY == Fixed.FromInt(124), $"The others keep their offsets from it ({units[1].MoveTargetX}, {units[1].MoveTargetY}; {units[2].MoveTargetX}, {units[2].MoveTargetY}).");
        var far = Fixed.Sqrt((units[3].MoveTargetX - Fixed.FromInt(500)) * (units[3].MoveTargetX - Fixed.FromInt(500)) + (units[3].MoveTargetY - Fixed.FromInt(100)) * (units[3].MoveTargetY - Fixed.FromInt(100)));
        // Shortened it would stand on (500, 130), inside the member on (500, 124): it takes the nearest free cell beside that point.
        Check.That(far <= radius * Fixed.FromInt(6) + Fixed.FromInt(20) && far >= radius * Fixed.FromInt(2), $"An offset longer than six bounding radii is shortened to that, then moved to a free place ({far} for a radius of {radius}).");
        for (var tick = 0; tick < world.Frames(900) && units.Any(unit => unit.HasMoveTarget); tick++) world.Step();
        Check.That(units.All(unit => !unit.HasMoveTarget), "Everybody arrives.");
        for (var first = 0; first < units.Length; first++)
            for (var second = first + 1; second < units.Length; second++)
                Check.That(Apart(units[first], units[second]), $"Units {first} and {second} do not stand inside each other.");
    }

    private static readonly string ReverseIni = """
        Locomotor ReverseLoco
          Surfaces = GROUND
          Speed = 30
          Acceleration = 900
          Braking = 900
          TurnRate = 90
          Appearance = FOUR_WHEELS
          CanMoveBackwards = Yes
        End
        Locomotor PivotLoco
          Surfaces = GROUND
          Speed = 30
          Acceleration = 900
          Braking = 900
          TurnRate = 90
          Appearance = FOUR_WHEELS
          CanMoveBackwards = Yes
          TurnPivotOffset = -0.5
        End
        Locomotor ForwardLoco
          Surfaces = GROUND
          Speed = 30
          Acceleration = 900
          Braking = 900
          TurnRate = 90
          Appearance = FOUR_WHEELS
        End
        Object ReverseCar
          KindOf = VEHICLE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL ReverseLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object PivotCar
          KindOf = VEHICLE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL PivotLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object ForwardCar
          KindOf = VEHICLE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL ForwardLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    /// <summary><c>Locomotor::moveTowardsPositionWheels</c>: a car with <c>CanMoveBackwards</c> reverses to a goal close behind it; one without turns round, and so does the first for a far goal.</summary>
    public static void ACarReversesToAGoalCloseBehindIt()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var files = new Dictionary<string, string>(GroupMoveFiles()) { ["Data/INI/Object/Reverse.ini"] = ReverseIni };
            Fixed Turned(string template, int back)
            {
                var (world, feeder, _) = RatedWorld(rate, files, NavTerrain());
                feeder.Spawn(1, template, x: 170, y: 100);
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
                var car = world.Entities.Single(value => value.Template.Name == template);
                var start = car.Angle;
                Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), 1000, world.Tick, car.Id, Fixed.FromInt(170 - back), Fixed.FromInt(100))).Accepted, $"{rate}: the move is accepted.");
                for (var step = 0; step < world.Frames(600) && (step < 2 || car.HasMoveTarget); step++) world.Step();
                Check.That(!car.HasMoveTarget && (car.X - Fixed.FromInt(170 - back)).Abs() < Fixed.FromInt(3), $"{rate}: {template} arrives ({car.X}).");
                return FixedTrig.AngleDifference(start, car.Angle).Abs();
            }
            Check.That(Turned("ReverseCar", 40) < Fixed.FromDecimal(0.1m), $"{rate}: it backs up without turning.");
            // The pivot lies behind the car: the goal passes it before the car arrives, and only the mirrored goal stays behind the tail.
            Check.That(Turned("PivotCar", 40) < Fixed.FromDecimal(0.1m), $"{rate}: a car with a pivot behind it backs up without turning.");
            Check.That(Turned("ForwardCar", 40) > Fixed.FromInt(2), $"{rate}: a car that cannot turns round.");
            Check.That(Turned("ReverseCar", 150) > Fixed.FromInt(2), $"{rate}: over a longer way it turns as well.");
        }
    }

    /// <summary><c>groupTightenToPosition</c>: a click into the group's own area gathers it; a click outside keeps the offsets, and so does a script's order.</summary>
    public static void AClickIntoTheGroupGathersIt()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, _, _, units) = GroupMoveWorld(rate, (100, 100), (160, 100), (100, 160), (160, 160));
            Check.That(world.Submit(new GroupMoveCommand(new PlayerId(1), 1000, world.Tick, [..units.Select(unit => unit.Id)], Fixed.FromInt(130), Fixed.FromInt(130), false, FromPlayer: true)).Accepted, $"{rate}: the click is accepted.");
            world.Step();
            foreach (var unit in units)
            {
                var (dx, dy) = (unit.MoveTargetX - Fixed.FromInt(130), unit.MoveTargetY - Fixed.FromInt(130));
                Check.That(dx.Abs() <= Fixed.FromInt(25) && dy.Abs() <= Fixed.FromInt(25), $"{rate}: every member goes for the click ({unit.MoveTargetX}, {unit.MoveTargetY}).");
            }
            Check.That(units.Select(unit => (unit.MoveTargetX, unit.MoveTargetY)).Distinct().Count() == 4, $"{rate}: each to a place of its own.");

            (world, _, _, units) = GroupMoveWorld(rate, (100, 100), (160, 100), (100, 160), (160, 160));
            Check.That(GroupMove(world, units, 130, 130).Accepted, $"{rate}: a script's order is accepted.");
            world.Step();
            Check.That(units.Any(unit => (unit.MoveTargetX - Fixed.FromInt(130)).Abs() >= Fixed.FromInt(30) || (unit.MoveTargetY - Fixed.FromInt(130)).Abs() >= Fixed.FromInt(30)), $"{rate}: it keeps the offsets.");

            // The factor of the fixture is 1 (the key is not there): the whole box gathers. A click outside it does not.
            (world, _, _, units) = GroupMoveWorld(rate, (100, 100), (160, 100), (100, 160), (160, 160));
            Check.That(world.Submit(new GroupMoveCommand(new PlayerId(1), 1000, world.Tick, [..units.Select(unit => unit.Id)], Fixed.FromInt(400), Fixed.FromInt(400), false, FromPlayer: true)).Accepted, $"{rate}: a click far away is accepted.");
            world.Step();
            Check.That(units.Any(unit => (unit.MoveTargetX - Fixed.FromInt(400)).Abs() >= Fixed.FromInt(30) || (unit.MoveTargetY - Fixed.FromInt(400)).Abs() >= Fixed.FromInt(30)), $"{rate}: outside the box the offsets stay.");
        }
    }

    /// <summary>Offsets that would put two members on one place (after shortening) are adjusted: the second takes the nearest free cell; an occupied goal moves a single mover too.</summary>
    public static void GroupMoveAdjustsToFreePlaces()
    {
        // Two members far behind in nearly the same direction: both offsets are shortened onto nearly the same point.
        var (world, feeder, _, units) = GroupMoveWorld(TestRate.Default, (100, 100), (100, 500), (104, 520), (700, 100));
        var bystander = units[3];
        Check.That(GroupMove(world, units.Take(3), 400, 100).Accepted, "The group order is accepted.");
        world.Step();
        Check.That(units[1].MoveTargetX != units[2].MoveTargetX || units[1].MoveTargetY != units[2].MoveTargetY, "Two members whose shortened offsets coincide get different places.");
        var dx = units[1].MoveTargetX - units[2].MoveTargetX;
        var dy = units[1].MoveTargetY - units[2].MoveTargetY;
        var touching = units[1].Template.BoundingCircleRadius * Fixed.FromInt(2);
        Check.That(dx * dx + dy * dy > touching * touching, "The two places do not overlap.");

        // A single unit ordered onto a standing ally takes the nearest free place; ordered onto its own position it stays.
        Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), 1500, world.Tick, units[0].Id, Fixed.FromInt(700), Fixed.FromInt(100))).Accepted, "A single move onto the bystander is accepted.");
        world.Step();
        Check.That(units[0].HasMoveTarget && (units[0].MoveTargetX != bystander.X || units[0].MoveTargetY != bystander.Y), $"Its destination is moved off the standing ally ({units[0].MoveTargetX}, {units[0].MoveTargetY}).");
        var ox = units[0].MoveTargetX - bystander.X;
        var oy = units[0].MoveTargetY - bystander.Y;
        Check.That(ox * ox + oy * oy > touching * touching && ox.Abs() <= Fixed.FromInt(40) && oy.Abs() <= Fixed.FromInt(40), "It is the nearest free cell: beside the ally, not far away.");
        var (bx, by) = (bystander.X, bystander.Y);
        Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), 2000, world.Tick, bystander.Id, bx, by)).Accepted, "A move onto the unit's own position is accepted.");
        world.Step();
        world.Step();
        Check.That(bystander.X == bx && bystander.Y == by && !bystander.HasMoveTarget, "A unit ordered onto the point it stands on stays there (the stop order).");
    }

    /// <summary>The command is validated as a whole, takes part in snapshot and replay, and an attack group marches as an attack-move.</summary>
    public static void GroupMoveCommandIsValidatedSavedAndReplayed()
    {
        var (world, feeder, fixture, units) = GroupMoveWorld(TestRate.Default, (100, 100), (100, 130));
        feeder.Spawn(2, "Marcher", x: 900, y: 900);
        world.Step();
        var foreign = world.Entities.Single(value => value.Owner == new PlayerId(2));
        Check.That(world.Submit(new GroupMoveCommand(new PlayerId(1), 900, world.Tick, [], Fixed.Zero, Fixed.Zero, false)).Reason == CommandRejection.UnknownEntity, "An empty group is refused.");
        Check.That(GroupMove(world, units.Append(foreign), 300, 300, sequence: 901).Reason == CommandRejection.NotPermitted, "A group with somebody else's unit is refused.");
        Check.That(world.Submit(new GroupMoveCommand(new PlayerId(1), 902, world.Tick, [new EntityId(9999)], Fixed.Zero, Fixed.Zero, false)).Reason == CommandRejection.UnknownEntity, "A group with an unknown unit is refused.");

        // Pending in a snapshot: the restored world executes it like the live one.
        Check.That(world.Submit(new GroupMoveCommand(new PlayerId(1), 903, world.Tick + 3, [..units.Select(unit => unit.Id)], Fixed.FromInt(400), Fixed.FromInt(100), true)).Accepted, "An attack group order for a later tick is accepted.");
        var bytes = WorldSnapshot.Write(world);
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, null);
        Check.That(restored.StateHash == world.StateHash && WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), "A snapshot with the pending group order restores to the same world.");
        for (var tick = 0; tick < 8; tick++)
        {
            world.Step();
            restored.Step();
            world.VerifyIncrementalHash();
            Check.That(restored.StateHash == world.StateHash, $"Live and restored world agree {tick + 1} ticks later.");
        }
        Check.That(units.All(unit => unit.HasMoveTarget && world.IsAttackMoving(unit)), "The attack group marches as an attack-move.");

        // The replay encoding round-trips.
        var command = new GroupMoveCommand(new PlayerId(1), 7, 12, [new EntityId(3), new EntityId(5)], Fixed.FromInt(10), Fixed.FromInt(-20), true);
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, TestRate.Default.TicksPerSecond, new string('A', 64), "fixture", 1, new WorldId(1), TwoPlayers, 20, 5);
        var read = ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(header, [command], [])));
        Check.That(read.Commands.Length == 1 && read.Commands[0] is GroupMoveCommand back && back.Equals(command) && CommandTypes.IsKnown(CommandTypes.GroupMove), "The group move round-trips through the replay format.");
    }

    /// <summary>The same group order at 30, 60 and 120 Hz: the same destinations and the same places at the end of every logic frame.</summary>
    public static void GroupMoveIsTheSameAtEveryRate()
    {
        static List<string> Run(SimulationRate rate)
        {
            var (world, _, _, units) = GroupMoveWorld(rate, (100, 100), (100, 130), (100, 160), (70, 115), (70, 145), (100, 400));
            Check.That(GroupMove(world, units, 400, 130).Accepted, $"{rate}: the group order is accepted.");
            var trace = new List<string>();
            for (var frame = 0; frame < 420; frame++)
            {
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++)
                {
                    world.Step();
                    world.VerifyIncrementalHash();
                }
                trace.Add(string.Join(";", units.Select(unit => $"{unit.X.Raw},{unit.Y.Raw},{(unit.HasMoveTarget ? 1 : 0)},{unit.MoveTargetX.Raw},{unit.MoveTargetY.Raw}")));
            }
            Check.That(units.All(unit => !unit.HasMoveTarget), $"{rate}: everybody has arrived.");
            return trace;
        }

        var reference = Run(SimulationRate.Hz30);
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var run = Run(rate);
            var first = Enumerable.Range(0, reference.Count).FirstOrDefault(frame => run[frame] != reference[frame], -1);
            Check.That(first < 0, $"{rate}: the units are where 30 Hz has them at the end of every logic frame (first difference in frame {first + 1}: {(first < 0 ? "" : run[first] + " against " + reference[first])}).");
        }
    }
}

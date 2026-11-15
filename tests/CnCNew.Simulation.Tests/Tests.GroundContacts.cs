using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private const string GroundContactIni = """
        Locomotor ContactLoco
          Surfaces = GROUND
          TurnRate = 99999.0
          Speed = 60.0
          Acceleration = 30000.0
          Braking = 30000.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Object ContactRunner
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 3.0
          GeometryMinorRadius = 3.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL ContactLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object ContactTank
          KindOf = SELECTABLE VEHICLE
          CrusherLevel = 2
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 6.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL ContactLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object ContactSoldier
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          GeometryHeight = 10.0
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SquishCollide Squish01
          End
        End
        Object LongCrate
          KindOf = CRATE
          Geometry = BOX
          GeometryMajorRadius = 40.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 4.0
          Behavior = MoneyCrateCollide Money01
            MoneyProvided = 1000
          End
        End
        Object LongMinefield
          Geometry = BOX
          GeometryMajorRadius = 40.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 2.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = MinefieldBehavior Mine01
            NumVirtualMines = 1
          End
        End
        """;

    /// <summary>
    /// #363, <c>PartitionContactList::processContactList</c>: a ground contact - a crate, a minefield, a crusher and its victim - comes only to a pair whose shapes collide
    /// (<c>PartitionData::collidesWith</c>), and <c>SquishCollide</c> tests the crusher's own geometry against the victim's with its radii set to 1 (<c>geomCollidesWithGeom</c>).
    /// A unit that drives along the long side of a crate or a minefield 80 units long and 8 wide, its centre 20 units off the middle, is inside the bounding circles (radius 40.2
    /// and 3) and outside the box: it collects nothing and sets nothing off, while one 5 units off does. A tank 30 units long and 12 wide passes a soldier 10 units off its line,
    /// inside both bounding circles and outside its box: the soldier lives; one 4 units off is crushed. At 30, 60 and 120 Hz.
    /// </summary>
    public static void GroundContactsMeetTheShapes()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            Check.That(!DrivesPast(rate, "ContactRunner", "LongCrate", 0, 20), $"{rate}: a runner along the long side of a crate, inside its circle and outside its box, does not collect it.");
            Check.That(DrivesPast(rate, "ContactRunner", "LongCrate", 0, 5), $"{rate}: a runner that touches the crate's box collects it.");
            Check.That(!DrivesPast(rate, "ContactRunner", "LongMinefield", 2, 20), $"{rate}: a runner along the long side of a minefield, inside its circle and outside its box, sets nothing off.");
            Check.That(DrivesPast(rate, "ContactRunner", "LongMinefield", 2, 5), $"{rate}: a runner that touches the minefield's box sets it off.");
            Check.That(!DrivesPast(rate, "ContactTank", "ContactSoldier", 2, 10), $"{rate}: a soldier beside a tank's long side, inside its circle and outside its box, is not crushed.");
            Check.That(DrivesPast(rate, "ContactTank", "ContactSoldier", 2, 4), $"{rate}: a soldier the tank's box meets is crushed.");
        }
    }

    /// <summary>Drives a <paramref name="mover"/> of player 1 along y = 100 + <paramref name="offset"/> past a <paramref name="standing"/> object at (100, 100); whether the contact happened.</summary>
    private static bool DrivesPast(SimulationRate rate, string mover, string standing, int owner, int offset)
    {
        var files = new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/GroundContacts.ini"] = GroundContactIni };
        var (world, feeder, _) = RatedWorld(rate, files);
        feeder.Spawn(1, mover, owner: 1, x: 20, y: 100 + offset);
        feeder.Spawn(owner, standing, owner: owner, x: 100, y: 100);
        world.Step();
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        var unit = world.Entities.Single(value => value.Template.Name == mover);
        var other = world.Entities.Single(value => value.Template.Name == standing);
        Check.That(feeder.SetMoveTarget(1, unit.Id.Value, 180, 100 + offset).Accepted, $"{rate}: the move is accepted.");
        for (var step = 0; step < world.Frames(150); step++) world.Step();
        Check.That(world.FindEntity(unit.Id) is { } after && after.X > Fixed.FromInt(170), $"{rate}: the {mover} drove past.");
        // A crate pays, a minefield of one mine is destroyed, a soldier dies.
        return world.MoneyOf(new CnCNew.Simulation.Ids.PlayerId(1)) > Fixed.Zero || world.FindEntity(other.Id) is not { } left || (left.Template.IsKindOf("INFANTRY") && left.EffectivelyDead);
    }
}

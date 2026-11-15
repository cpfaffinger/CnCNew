using CnCNew.Simulation.Time;

/// <summary>
/// #373, <c>PartitionContactList::processContactList</c>: "if( obj->getStatusBits().test( OBJECT_STATUS_NO_COLLISIONS ) || other->getStatusBits().test( OBJECT_STATUS_NO_COLLISIONS ) ) continue;" -
/// a pair of which either object does not collide has no contact, so a crusher does not squish or crush what is <c>NO_COLLISIONS</c> (a hijacker riding hidden in its vehicle) and a crusher that
/// is <c>NO_COLLISIONS</c> runs over nobody.
/// </summary>
static partial class Tests
{
    private const string CrushNoCollisionsIni = """
        Upgrade Upgrade_TestNoCollisions
          Type = OBJECT
          BuildCost = 10
          BuildTime = 0.1
        End
        Object SquishSoldierHidden
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
          Behavior = StatusBitsUpgrade Hidden01
            TriggeredBy = Upgrade_TestNoCollisions
            StatusToSet = NO_COLLISIONS
          End
        End
        Object CrushSoldierHidden
          KindOf = SELECTABLE INFANTRY
          CrushableLevel = 0
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
          Behavior = PhysicsBehavior Physics01
            Mass = 5.0
          End
          Behavior = StatusBitsUpgrade Hidden01
            TriggeredBy = Upgrade_TestNoCollisions
            StatusToSet = NO_COLLISIONS
          End
        End
        Object SquishTankHidden
          KindOf = SELECTABLE VEHICLE
          CrusherLevel = 2
          VisionRange = 200.0
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 6.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL SquishTankLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 50.0
          End
          Behavior = StatusBitsUpgrade Hidden01
            TriggeredBy = Upgrade_TestNoCollisions
            StatusToSet = NO_COLLISIONS
          End
        End
        """;

    /// <summary>
    /// #373: a soldier that can be squished and one that can be crushed are run over; each is killed while it collides and lives when it is <c>NO_COLLISIONS</c>, and a crusher that is
    /// <c>NO_COLLISIONS</c> kills neither. At 30, 60 and 120 Hz.
    /// </summary>
    public static void CrushSparesWhatDoesNotCollide()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
            foreach (var victim in new[] { "SquishSoldierHidden", "CrushSoldierHidden" })
            {
                Check.That(RunOver(rate, "SquishTank", victim, "none", extraIni: CrushNoCollisionsIni).Crushed, $"{rate}: the tank kills the {victim} it runs over.");
                var hidden = RunOver(rate, "SquishTank", victim, "none", extraIni: CrushNoCollisionsIni,
                    setup: (feeder, _, man) => feeder.GrantUpgrade(2, "Upgrade_TestNoCollisions", man.Id.Value));
                Check.That(!hidden.Crushed, $"{rate}: a {victim} that is NO_COLLISIONS is not killed by the tank that runs over it.");
                var ghost = RunOver(rate, "SquishTankHidden", victim, "none", extraIni: CrushNoCollisionsIni,
                    setup: (feeder, tank, _) => feeder.GrantUpgrade(1, "Upgrade_TestNoCollisions", tank.Id.Value));
                Check.That(!ghost.Crushed, $"{rate}: a tank that is NO_COLLISIONS does not kill the {victim} it runs over.");
            }
    }
}

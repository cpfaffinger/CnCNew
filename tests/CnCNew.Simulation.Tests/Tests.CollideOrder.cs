using CnCNew.Simulation.Time;

/// <summary>
/// #373, <c>PartitionContactList::processContactList</c> and <c>Object::onCollide</c>: a contact calls the mover's collide modules, then - unless one of the two is destroyed - the other's,
/// each object's in the order of its module list, and an object's loop ends when it stops colliding. A squish is the victim's <c>SquishCollide</c> and a crush the crusher's
/// <c>PhysicsBehavior</c>, so a victim's collide module listed before its <c>SquishCollide</c> acts on the crusher first.
/// </summary>
static partial class Tests
{
    private const string CollideOrderIni = """
        Object HijackThenSquish
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
          Behavior = ConvertToHijackedVehicleCrateCollide Hijack01
            RequiredKindOf = VEHICLE
          End
          Behavior = SquishCollide Squish01
          End
        End
        Object SquishThenHijack
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
          Behavior = ConvertToHijackedVehicleCrateCollide Hijack01
            RequiredKindOf = VEHICLE
          End
        End
        """;

    /// <summary>
    /// #373: a tank runs over a soldier without an AI that takes any vehicle it touches and can be squished. Listed before its <c>SquishCollide</c>, the take acts first: the tank is taken and the
    /// soldier, gone into it, is not squished. Listed after, the squish acts first and the dead soldier takes nothing. At 30, 60 and 120 Hz.
    /// </summary>
    public static void CollideModulesActInModuleOrder()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var hijackFirst = RunOver(rate, "SquishTank", "HijackThenSquish", "none", extraIni: CollideOrderIni);
            Check.That(hijackFirst.TankTaken && !hijackFirst.Crushed, $"{rate}: a take listed before the squish takes the tank and the taker is not squished (taken {hijackFirst.TankTaken}, crushed {hijackFirst.Crushed}).");
            var squishFirst = RunOver(rate, "SquishTank", "SquishThenHijack", "none", extraIni: CollideOrderIni);
            Check.That(squishFirst.Crushed && !squishFirst.TankTaken, $"{rate}: a squish listed before the take squishes the taker, which takes nothing (taken {squishFirst.TankTaken}, crushed {squishFirst.Crushed}).");
        }
    }
}

using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The dock bones of a dock's model (#354, <c>DockUpdate::loadDockPositions</c>): the approach places are its <c>DockWaitingNN</c> bones (<c>computeApproachPosition</c>), and the docker drives
/// to <c>DockStart</c>, <c>DockAction</c> and <c>DockEnd</c> (<c>AIDockMoveToEntryState</c>, <c>AIDockMoveToDockState</c>, <c>AIDockMoveToExitState</c>). A model without them keeps the boneless
/// branches.
/// </summary>
static partial class Tests
{
    private const string DockBonesIni = """
        Object BoneBay
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = RepairDockUpdate Dock01
            NumberApproachPositions = 3
            TimeForFullHeal = 1000
          End
        End
        Object IgnoringBay
          KindOf = STRUCTURE IGNORE_DOCKING_BONES
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = RepairDockUpdate Dock01
            NumberApproachPositions = 3
            TimeForFullHeal = 1000
          End
        End
        Object DockTank
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 40.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    private static BonePose Bone(int x, int y) => new(Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero);

    /// <summary>The bones of both bays: two approach places for three slots, and a way in, a place of action and a way out.</summary>
    private static ModelGeometry BayBones(BonePose? start = null, BonePose? action = null, BonePose? end = null)
    {
        var bones = new DockBones(start, action, end, [Bone(80, 0), Bone(80, 40)]);
        return new ModelGeometry([], [], [], docks: [new KeyValuePair<string, DockBones>("BoneBay", bones), new KeyValuePair<string, DockBones>("IgnoringBay", bones)]);
    }

    private static (SimulationWorld World, Feeder Feeder) DockBonesWorld(SimulationRate rate, ModelGeometry geometry)
    {
        var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(Phase5Files()) { ["Data/INI/Object/DockBones.ini"] = DockBonesIni });
        var templates = TemplateCatalog.Build(fixture.Graph, fixture.Bindings, rate, geometry);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 11, CombatPlayers, SyntheticFixture.FixtureId, geometry.Combine(fixture.Fingerprint), string.Empty), templates, null);
        return (world, new Feeder(world));
    }

    private static bool Near(Fixed value, Fixed expected) => (value - expected).Abs() <= Fixed.FromDecimal(0.01m);

    /// <summary>
    /// <c>DockUpdate::computeApproachPosition</c> with bones: the docker that takes slot <c>n</c> heads for the bone <c>DockWaiting(n+1)</c> of the dock, turned with the dock (no push towards the
    /// docker); a slot beyond the model's bones takes the zero vector, the dock's centre. A dock that is <c>KINDOF_IGNORE_DOCKING_BONES</c> keeps the boneless push towards the docker.
    /// </summary>
    public static void DockApproachTakesTheModelsWaitingBones()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder) = DockBonesWorld(rate, BayBones());
            // The bays face north: a bone at (80, 40) of the model lies at (-40, 80) from the bay's centre.
            feeder.Spawn(1, "BoneBay", owner: 1, x: 400, y: 400, angle: FixedTrig.HalfPi);
            feeder.Spawn(1, "IgnoringBay", owner: 1, x: 800, y: 400, angle: FixedTrig.HalfPi);
            for (var tank = 0; tank < 4; tank++) feeder.Spawn(1, "DockTank", owner: 1, x: 380 + 20 * tank, y: 250);
            feeder.Spawn(1, "DockTank", owner: 1, x: 800, y: 250);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var bay = world.Entities.Single(value => value.Template.Name == "BoneBay");
            var ignoring = world.Entities.Single(value => value.Template.Name == "IgnoringBay");
            var tanks = world.Entities.Where(value => value.Template.Name == "DockTank").OrderBy(value => value.Id.Value).ToArray();
            for (var tank = 0; tank < 4; tank++) Check.That(feeder.Dock(1, tanks[tank].Id.Value, bay.Id.Value).Accepted, $"{rate}: tank {tank} is ordered to the bay.");
            Check.That(feeder.Dock(1, tanks[4].Id.Value, ignoring.Id.Value).Accepted, $"{rate}: the fifth tank is ordered to the bay that ignores its bones.");
            world.Step();
            var expected = new (int X, int Y)[] { (400, 480), (360, 480), (400, 400) };
            for (var tank = 0; tank < 3; tank++)
                Check.That(tanks[tank].HasMoveTarget && Near(tanks[tank].MoveTargetX, Fixed.FromInt(expected[tank].X)) && Near(tanks[tank].MoveTargetY, Fixed.FromInt(expected[tank].Y)),
                    $"{rate}: tank {tank} takes slot {tank} and heads for ({expected[tank].X}, {expected[tank].Y}), not ({tanks[tank].MoveTargetX}, {tanks[tank].MoveTargetY}).");
            Check.That(!tanks[3].HasMoveTarget, $"{rate}: the fourth tank finds the three slots taken.");
            // The boneless push: the bay's centre moved half its major radius (10) towards the tank 150 units south.
            Check.That(tanks[4].HasMoveTarget && Near(tanks[4].MoveTargetX, Fixed.FromInt(800)) && Near(tanks[4].MoveTargetY, Fixed.FromInt(390)),
                $"{rate}: a dock that ignores its bones keeps the boneless approach ({tanks[4].MoveTargetX}, {tanks[4].MoveTargetY}).");
        }
    }

    /// <summary>
    /// <c>AIDockProcessDockState</c>: the dock's first action is the state's first update, in the frame after the docker reached the dock. Above 30 Hz it fell in the next tick, inside that
    /// frame, and a repair dock - which heals once per frame counted from its first action - healed and let its docker go half a frame early at 60 Hz, three quarters at 120 Hz.
    /// </summary>
    public static void RepairDockActsInTheFrameAfterTheDockerArrived()
    {
        var frames = new List<(long First, long Full, long Done)>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder) = DockBonesWorld(rate, BayBones());
            feeder.Spawn(1, "IgnoringBay", owner: 1, x: 400, y: 400);
            feeder.Spawn(1, "DockTank", owner: 1, x: 500, y: 250);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var bay = world.Entities.Single(value => value.Template.Name == "IgnoringBay");
            var tank = world.Entities.Single(value => value.Template.Name == "DockTank");
            var ai = tank.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.AIUpdateInterface>().Single();
            Check.That(feeder.Dock(1, tank.Id.Value, bay.Id.Value).Accepted, $"{rate}: the tank is ordered to the bay.");
            var (first, full, done) = (-1L, -1L, -1L);
            for (var step = 0; step < world.Frames(30 * 60) && done < 0; step++)
            {
                world.Step();
                var frame = (world.Tick - 1) / rate.TicksPerOriginalFrame;
                if (first < 0 && tank.Body!.Health > Fixed.FromInt(40)) first = frame;
                if (full < 0 && tank.Body!.Health == tank.Body.MaxHealth) full = frame;
                if (done < 0 && first >= 0 && !ai.IsDocking) done = frame;
            }
            Check.That(first >= 0 && full >= 0 && done >= 0, $"{rate}: the tank is repaired and leaves ({first}, {full}, {done}).");
            frames.Add((first, full, done));
        }
        Check.That(frames.Distinct().Count() == 1, $"the first repair, the full health and the end of the dock lie in the same frames of the original at 30, 60 and 120 Hz: {string.Join(", ", frames)}.");
    }
}

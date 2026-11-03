using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private const string GuardHijackerIni = """
        Object GuardHijacker
          KindOf = SELECTABLE INFANTRY
          EnterGuard = Yes
          HijackGuard = Yes
          VisionRange = 150.0
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = HijackerUpdate Hijack01
            ParachuteName = FixtureChute
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = ConvertToHijackedVehicleCrateCollide Convert01
            RequiredKindOf = VEHICLE
          End
        End
        Object PlainGuardHijacker
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = ConvertToHijackedVehicleCrateCollide Convert01
            RequiredKindOf = VEHICLE
          End
        End
        """;

    /// <summary>
    /// <c>AIGuardMachine::lookForInnerTarget</c> with <c>isEnterGuard</c> and <c>isHijackGuard</c>: a hijacker on guard takes the closest enemy vehicle within its guard range of the post - in the same
    /// frame at 30, 60 and 120 Hz; one without the two keys stands, having no weapon to guard with.
    /// </summary>
    public static void AHijackerOnGuardTakesVehicles()
    {
        (long Frame, bool Taken) Guard(SimulationRate rate, string hijackerName)
        {
            var files = new Dictionary<string, string>(CratesAndSpecialsFiles()) { ["Data/INI/Object/GuardHijacker.ini"] = GuardHijackerIni };
            var fixture = new SyntheticFixture(rate, files: files);
            ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, hijackerName, owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "EjectingTruck", owner: 2, x: 70, y: 20);
            feeder.Spawn(2, "EjectingTruck", owner: 2, x: 400, y: 0);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var hijacker = world.Entities.Single(value => value.Template.Name == hijackerName);
            var truck = world.Entities.Single(value => value.Template.Name == "EjectingTruck" && value.X == Fixed.FromInt(70));
            var far = world.Entities.Single(value => value.Template.Name == "EjectingTruck" && value.X == Fixed.FromInt(400));
            Check.That(feeder.Guard(1, [hijacker.Id.Value], 0, 0).Accepted, $"{rate}: the guard order is taken.");
            for (var frame = 1; frame <= 300; frame++)
            {
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
                if (truck.Owner == new PlayerId(1))
                {
                    Check.That(far.Owner == new PlayerId(2), $"{rate}: the vehicle beyond the guard range stays its owner's.");
                    return (frame, true);
                }
            }
            return (-1, false);
        }

        var frames = new List<long>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (frame, taken) = Guard(rate, "GuardHijacker");
            Check.That(taken, $"{rate}: the hijacker on guard takes the enemy vehicle near its post.");
            frames.Add(frame);
            Check.That(!Guard(rate, "PlainGuardHijacker").Taken, $"{rate}: without EnterGuard and HijackGuard it does not.");
        }
        Check.That(frames.Distinct().Count() == 1, $"The vehicle is taken in the same frame at every rate ({string.Join(", ", frames)}).");
    }
}

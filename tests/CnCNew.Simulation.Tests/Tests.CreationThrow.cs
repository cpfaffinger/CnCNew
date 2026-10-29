using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-66, the throw of a created object (<c>ObjectCreationList.cpp</c>, <c>doStuffToObj</c>): an object with a physics module that a creation list makes with <c>SEND_IT_FLYING</c> gets
/// the drawn spin rates and an upward force and flies; with <c>SEND_IT_OUT</c> it is pushed along the ground.
/// </summary>
static partial class Tests
{
    private const string CreationThrowIni = """
        Object CtCrate
          KindOf = SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 1.0
          End
        End
        Object CtSource
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        ObjectCreationList OCL_CtFlying
          CreateObject
            ObjectNames = CtCrate
            Disposition = SEND_IT_FLYING
            DispositionIntensity = 2.0
          End
        End
        ObjectCreationList OCL_CtOut
          CreateObject
            ObjectNames = CtCrate
            Disposition = SEND_IT_OUT
            DispositionIntensity = 2.0
          End
        End
        """;

    /// <summary>
    /// <c>SEND_IT_FLYING</c>: the crate rises (the force is upward by a third of up to three times the intensity), falls back, lands and comes to rest away from where it was made;
    /// <c>SEND_IT_OUT</c>: it slides along the ground and stops. The same resting place at 30, 60 and 120 Hz, and a snapshot in the flight continues identically. Before, both stood
    /// where they were made.
    /// </summary>
    public static void CreationListThrowsWhatItCreates()
    {
        (long X, long Y, Fixed Highest) Run(SimulationRate rate, string list, bool save)
        {
            var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(CreationThrowIni));
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            var source = GdSpawn(world, feeder, "CtSource", 1, 100, 100);
            world.CreateFromList(source, list, EntityId.Invalid, false);
            world.Step();
            var crate = world.Entities.Single(value => value.Template.Name == "CtCrate");
            var highest = crate.Z;
            for (var step = 0; step < world.Frames(300); step++)
            {
                if (save && step == world.Frames(5))
                {
                    var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                    for (var follow = 0; follow < restored.Frames(30); follow++)
                        Check.That(restored.Step().StateHash == world.Step().StateHash, $"{rate}: the restored world follows the flight (step {follow}).");
                }
                world.Step();
                highest = Fixed.Max(highest, crate.Z);
            }
            var physics = crate.Modules.OfType<PhysicsBehavior>().Single();
            Check.That(!physics.Flying && crate.Z == Fixed.Zero, $"{rate} {list}: the crate rests on the ground ({crate.Z}).");
            Check.That(crate.X != Fixed.FromInt(100) || crate.Y != Fixed.FromInt(100), $"{rate} {list}: it rests away from where it was made ({crate.X}, {crate.Y}).");
            return (crate.X.Raw, crate.Y.Raw, highest);
        }

        var flying = Run(SimulationRate.Hz30, "OCL_CtFlying", false);
        Check.That(flying.Highest > Fixed.FromInt(1), $"SEND_IT_FLYING: the crate flew ({flying.Highest}).");
        var outward = Run(SimulationRate.Hz30, "OCL_CtOut", false);
        Check.That(outward.Highest == Fixed.Zero, $"SEND_IT_OUT: the crate stays on the ground ({outward.Highest}).");
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = Run(rate, "OCL_CtFlying", false);
            Check.That((other.X, other.Y) == (flying.X, flying.Y), $"{rate}: the crate rests where it rests at 30 Hz.");
            var outOther = Run(rate, "OCL_CtOut", false);
            Check.That((outOther.X, outOther.Y) == (outward.X, outward.Y), $"{rate}: the pushed crate rests where it rests at 30 Hz.");
        }
        Run(TestRate.Default, "OCL_CtFlying", true);
    }
}

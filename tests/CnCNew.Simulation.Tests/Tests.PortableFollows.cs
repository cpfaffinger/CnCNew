using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private const string PortableFollowsIni = """
        Locomotor FcSlowTurn
          Surfaces = GROUND
          Speed = 30
          Acceleration = 900
          Braking = 900
          TurnRate = 90
        End
        Object FcArmedOverlord
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 15.0
          GeometryHeight = 10.0
          TransportSlotCount = 3
          VisionRange = 300.0
          Locomotor = SET_NORMAL FcSlowTurn
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY FcRifle
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
          End
          Behavior = OverlordContain Overlord01
            Slots = 1
            AllowInsideKindOf = PORTABLE_STRUCTURE
          End
        End
        """;

    /// <summary>
    /// F-150: the structure on an Overlord's or Avenger's back stands and faces as the 30 Hz step has it at every frame end. The follow runs in the host's update, before the turn the
    /// target acquisition gives the host later in the tick, so at 30 Hz the structure takes that turn a frame late; above 30 Hz it took the shares of the same turn in the host's own
    /// update of the next tick and was a frame ahead at the frame end (Death Valley and Armored Fury, America against China: the Avenger's laser turret).
    /// </summary>
    public static void APortableStructureFollowsItsHostAsAt30Hz()
    {
        (List<long> Angles, List<long> Positions)? reference = null;
        foreach (var (rate, _) in WorldRates)
        {
            var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(PortableFollowsIni));
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            // Everything is put into the world at a frame boundary, so that the frames of the objects lie where they lie at every rate.
            feeder.Spawn(1, "FcArmedOverlord", owner: 1, x: 500, y: 500);
            feeder.Spawn(1, "FcGattling", owner: 1, x: 500, y: 500);
            do world.Step(); while (!world.Time.IsOriginalFrameBoundary(world.Tick));
            var host = world.Entities.Single(value => value.Template.Name == "FcArmedOverlord");
            var cannon = world.Entities.Single(value => value.Template.Name == "FcGattling");
            Check.That(world.TryEnterContainer(cannon, host), "Setup: the structure boards the host.");
            // The target stands at right angles to the host's heading, within its range: the host turns toward it in the acquisition phase and then fires.
            feeder.Spawn(2, "FcTarget", owner: 2, x: 500, y: 560);
            var angles = new List<long>();
            var positions = new List<long>();
            var turned = false;
            for (var frame = 0; frame < 40; frame++)
            {
                for (var tick = 0; tick < world.Time.TicksPerOriginalFrame; tick++) world.Step();
                angles.Add(cannon.Angle.Raw);
                positions.Add(cannon.X.Raw * 31 + cannon.Y.Raw);
                turned |= host.Angle != Fixed.Zero;
            }
            Check.That(turned && angles.Distinct().Count() > 3, $"{rate}: the host turns toward its target and the structure follows ({string.Join(", ", angles.Take(12))}).");
            reference ??= (angles, positions);
            if (reference.Value.Angles == angles) continue;
            SameSeries(reference.Value.Angles, angles, $"The heading of the structure on the host's back at the frame ends at {rate}");
            SameSeries(reference.Value.Positions, positions, $"The place of the structure on the host's back at the frame ends at {rate}");
        }
    }
}

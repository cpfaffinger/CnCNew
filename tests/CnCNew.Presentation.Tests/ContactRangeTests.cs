using System.Collections.Immutable;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// #414: the client's "in range" (<c>SimulationQueries.InRange</c>, asked by the standing orders before they stop the chase and give the fire order) for a contact weapon is the
/// world's own answer - between the bounding circles (<c>ATTACK_RANGE_CALC_TYPE = FROM_BOUNDINGSPHERE_2D</c>), at a structure only when touched. Between the centres a knife of 3 never
/// reached a soldier of radius 10, and the order chased on without firing.
/// </summary>
internal static class ContactRangeTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/ContactRangeFixture.ini"] = """
        Weapon ContactKnife
          PrimaryDamage = 10
          AttackRange = 3.0
          DamageType = MELEE
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Object ContactCommando
          KindOf = SELECTABLE INFANTRY CAN_ATTACK
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 12.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ContactKnife
          End
        End
        Object ContactVictim
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 12.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Object ContactHut
          KindOf = SELECTABLE STRUCTURE
          Geometry = BOX
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 20.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ContactRangeTests));
        harness.Run(nameof(ContactWeaponIsInRangeBetweenTheCircles), ContactWeaponIsInRangeBetweenTheCircles);
        return harness.Failures;
    }

    private static void ContactWeaponIsInRangeBetweenTheCircles()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: Files());
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, Players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            ulong sequence = 0;
            void Spawn(string template, int owner, int x, int y)
                => Assert(world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns");
            Spawn("ContactCommando", 1, 100, 100);  // id 1
            Spawn("ContactVictim", 2, 120, 100);    // id 2: 20 apart, the circles touch
            Spawn("ContactVictim", 2, 100, 122);    // id 3: 22 apart, the circles 2 apart
            Spawn("ContactHut", 2, 400, 400);       // id 4: a box of 40 by 40
            Spawn("ContactCommando", 1, 400, 365);  // id 5: its circle within 3 of the hut's, its cylinder 5 short of the box
            Spawn("ContactCommando", 1, 400, 372);  // id 6: touching the box
            world.Step();
            Entity Of(int id) => world.FindEntity(new EntityId((uint)id))!;
            // The knife's range of 3 is 0.5 a quarter cell short (RATIONALIZE_ATTACK_RANGE, #422).
            Assert(SimulationQueries.InRange(world, Of(1), Of(2)), $"{rate}: a knife of 3 reaches a soldier whose circle touches the commando's (the centres 20 apart)");
            Assert(!SimulationQueries.InRange(world, Of(1), Of(3)), $"{rate}: and not one 2 from it - its range is 0.5 between the circles");
            Assert(!SimulationQueries.InRange(world, Of(5), Of(4)), $"{rate}: at a structure the circles are not enough: the commando must touch it");
            Assert(SimulationQueries.InRange(world, Of(6), Of(4)), $"{rate}: a commando that touches the structure reaches it");
        }
    }
}

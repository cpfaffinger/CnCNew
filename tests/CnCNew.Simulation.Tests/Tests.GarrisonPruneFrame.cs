using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// #305: <c>OpenContain::update</c> takes the dead riders out of a garrison once per logic frame of the original. A rider that dies after the building's update is gone in the
/// building's next frame, and the building goes back to its owner there. Above 30 Hz the building's update in the frame's second tick took it out a frame early (Alpine Assault
/// China against GLA: a GLA terrorist killed in a pub at frame 15,358, the pub neutral again in that frame at 60 Hz and in the next at 30 Hz).
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> GarrisonPruneFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/GarrisonPruneFixture.ini"] = """
        Weapon GpClearGun
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = KILL_GARRISONED
          DelayBetweenShots = 100
        End
        Object GpBuilding
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 30.0
          GeometryMinorRadius = 30.0
          GeometryHeight = 20.0
          Body = StructureBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 3
          End
        End
        Object GpSoldier
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 5000
          End
        End
        Object GpClearer
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY GpClearGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>
    /// A soldier garrisons a civilian building and is killed inside by a building clearer whose update comes after the building's; the building lets it go and is civilian again in
    /// the frame after its death at 30, 60 and 120 Hz.
    /// </summary>
    public static void AGarrisonLetsItsDeadRiderGoInTheNextFrame()
    {
        var frames = new List<long>();
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, GarrisonPruneFiles());
            feeder.Spawn(0, "GpBuilding", owner: 0, x: 100, y: 0);
            feeder.Spawn(0, "GpSoldier", owner: 2, x: 100, y: 0);
            feeder.Spawn(0, "GpClearer", owner: 1, x: 0, y: 0);
            feeder.SetRelationship(1, 2, Relationship.Enemies);
            world.Step();
            var building = world.Entities.Single(value => value.Template.Name == "GpBuilding");
            var soldier = world.Entities.Single(value => value.Template.Name == "GpSoldier");
            var clearer = world.Entities.Single(value => value.Template.Name == "GpClearer");
            Check.That(clearer.Id.Value > building.Id.Value, $"{rate}: the clearer updates after the building.");
            Check.That(world.TryEnterContainer(soldier, building), $"{rate}: the soldier garrisons the building.");
            while (world.Tick < world.Time.OriginalFrames(10)) world.Step();
            Check.That(building.Owner == new PlayerId(2), $"{rate}: the garrisoned building is the soldier's player's.");
            Check.That(feeder.FireWeapon(1, clearer.Id.Value, building.Id.Value).Accepted, $"{rate}: the clearer fires at the building.");
            long died = -1, back = -1;
            while (world.Tick < world.Time.OriginalFrames(120) && back < 0)
            {
                var tick = world.Tick;
                world.Step();
                if (died < 0 && soldier.EffectivelyDead) died = world.Time.ToOriginalFrames(tick);
                if (back < 0 && building.Owner.IsSystem) back = world.Time.ToOriginalFrames(tick);
            }
            System.Console.WriteLine($"       {rate}: the soldier dies in frame {died}, the building is civilian again in frame {back}");
            Check.That(died > 0 && back == died + 1, $"{rate}: the building lets its dead rider go in the frame after its death (died {died}, civilian in {back}).");
            frames.Add(back);
        }
        Check.That(frames.Distinct().Count() == 1, $"The building is civilian again in the same frame at every rate ({string.Join(", ", frames)}).");
    }
}

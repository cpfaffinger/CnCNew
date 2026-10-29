using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-64, an attack on a place (docs/ATTACK-POSITION.md, <c>AIUpdateInterface::privateAttackPosition</c>, <c>AIAttackState</c> on a position): a cannon (range 150, a blast of 30) is
/// told to attack a place 300 away, where one of its own trucks stands.
/// </summary>
static partial class Tests
{
    private const string AttackPlaceIni = """
        Locomotor ApTreads
          Surfaces = GROUND
          Speed = 30
          Acceleration = 300
          Braking = 300
          TurnRate = 3600
        End
        Weapon ApShell
          PrimaryDamage = 10
          PrimaryDamageRadius = 30.0
          RadiusDamageAffects = ALLIES ENEMIES NEUTRALS
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 1000
        End
        Weapon ApMortar
          PrimaryDamage = 25
          PrimaryDamageRadius = 30.0
          RadiusDamageAffects = ALLIES ENEMIES NEUTRALS
          AttackRange = 400.0
          MinimumAttackRange = 100.0
          DamageType = EXPLOSION
          ClipSize = 1
          ClipReloadTime = 1500
        End
        Object ApCannon
          KindOf = SELECTABLE VEHICLE CAN_ATTACK
          VisionRange = 150.0
          Locomotor = SET_NORMAL ApTreads
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ApShell
            Weapon = SECONDARY ApMortar
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object ApTruck
          KindOf = SELECTABLE VEHICLE
          VisionRange = 100.0
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, Entity Cannon, Entity Truck) AttackPlaceWorld(SimulationRate rate)
    {
        var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(AttackPlaceIni));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        var cannon = GdSpawn(world, feeder, "ApCannon", 1, 0, 0);
        var truck = GdSpawn(world, feeder, "ApTruck", 1, 300, 0);
        return (world, feeder, fixture, cannon, truck);
    }

    /// <summary>The frames of the original (counted to their end) in which the truck lost health, over <paramref name="frames"/> frames.</summary>
    private static List<long> AttackPlaceHits(SimulationWorld world, Entity truck, int frames, Action<int>? atStep = null)
    {
        var hits = new List<long>();
        var start = world.Tick;
        var health = truck.Body!.Health;
        for (var step = 0; step < world.Frames(frames); step++)
        {
            atStep?.Invoke(step);
            world.Step();
            if (truck.Body.Health >= health) continue;
            hits.Add((world.Tick - start + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame);
            health = truck.Body.Health;
        }
        return hits;
    }

    /// <summary>
    /// Step 1, the state: the cannon walks toward the place until it is within its range of 150 and stops there, fires its two shots at the place - the truck there takes the blasts,
    /// though it is the cannon's own and nothing aims at it - and is idle again. The same frames at 30, 60 and 120 Hz, and a snapshot in the walk continues identically.
    /// </summary>
    public static void AttackPlaceWalksFiresAndEnds()
    {
        long[] Run(SimulationRate rate, bool save)
        {
            var (world, feeder, fixture, cannon, truck) = AttackPlaceWorld(rate);
            Check.That(feeder.AttackPosition(1, [cannon.Id.Value], 300, 0, shots: 2).Accepted, $"{rate}: the attack on the place is accepted.");
            world.Step();
            Check.That(world.IsAttackingPlace(cannon), $"{rate}: the cannon attacks the place.");
            var hits = AttackPlaceHits(world, truck, 600, step =>
            {
                if (!save || step != world.Frames(30)) return;
                var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                Check.That(restored.StateHash == world.StateHash, $"{rate}: the snapshot in the walk restores to the same hash.");
                for (var follow = 0; follow < restored.Frames(20); follow++)
                    Check.That(restored.Step().StateHash == world.Step().StateHash, $"{rate}: the restored world follows the walk (step {follow}).");
            });
            Check.That(hits.Count == 2, $"{rate}: two shots hit the place ({hits.Count}).");
            Check.That(!world.IsAttackingPlace(cannon), $"{rate}: with its shots fired the cannon is done.");
            var distance = GdDistance(cannon, 300, 0);
            Check.That(distance <= Fixed.FromInt(150) && distance > Fixed.FromInt(120), $"{rate}: it stopped within its range of the place ({distance}).");
            return [.. hits];
        }

        var reference = Run(SimulationRate.Hz30, save: false);
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = Run(rate, save: false);
            Check.That(other.SequenceEqual(reference), $"{rate}: the hits fall on the same frames as at 30 Hz ({string.Join(",", other)} against {string.Join(",", reference)}).");
        }
        Run(TestRate.Default, save: true);
    }

    /// <summary>
    /// Step 1: without a limit the cannon fires until another order (a move ends the attack); a button's slot is locked for the attack - the mortar (range 400, never nearer than 100)
    /// fires from where the cannon stands, the lock ends with its clip and the attack with it; an order for a slot no member has does nothing; the order is refused for another
    /// player's unit and for a place beyond the map's reach.
    /// </summary>
    public static void AttackPlaceLimitsAndSlots()
    {
        var (world, feeder, _, cannon, truck) = AttackPlaceWorld(TestRate.Default);
        Check.That(feeder.AttackPosition(1, [cannon.Id.Value], 300, 0).Accepted, "The attack without a limit is accepted.");
        var hits = AttackPlaceHits(world, truck, 450);
        Check.That(hits.Count >= 4 && world.IsAttackingPlace(cannon), $"Without a limit it goes on firing ({hits.Count} hits).");
        Check.That(feeder.SetMoveTarget(1, cannon.Id.Value, 0, 0).Accepted, "The move order is accepted.");
        world.Step();
        Check.That(!world.IsAttackingPlace(cannon), "A move order ends the attack.");
        Check.That(AttackPlaceHits(world, truck, 120).Count == 0, "No shot after the move order.");

        (world, feeder, _, cannon, truck) = AttackPlaceWorld(TestRate.Default);
        var start = (cannon.X, cannon.Y);
        Check.That(feeder.AttackPosition(1, [cannon.Id.Value], 300, 0, slot: WeaponSlot.Secondary, shots: 3).Accepted, "The mortar's button is accepted.");
        hits = AttackPlaceHits(world, truck, 300);
        Check.That(hits.Count == 1, $"The mortar fires its one round, and the attack ends with the lock ({hits.Count} hits).");
        Check.That(!world.IsAttackingPlace(cannon) && cannon.X == start.X && cannon.Y == start.Y, "The mortar reaches the place from where the cannon stands.");

        (world, feeder, _, cannon, truck) = AttackPlaceWorld(TestRate.Default);
        var noWeapon = world.RejectionCounts[(int)CommandRejection.NoWeapon];
        Check.That(feeder.AttackPosition(1, [cannon.Id.Value], 300, 0, slot: WeaponSlot.Tertiary).Accepted, "An order for a slot is accepted by the pre-check.");
        world.Step();
        Check.That(!world.IsAttackingPlace(cannon) && world.RejectionCounts[(int)CommandRejection.NoWeapon] == noWeapon + 1, "No member has the slot: nothing happens.");
        Check.That(!feeder.AttackPosition(2, [cannon.Id.Value], 300, 0).Accepted, "Another player's unit is refused.");
        Check.That(!feeder.AttackPosition(1, [cannon.Id.Value], 200_000, 0).Accepted, "A place beyond the map's reach is refused.");
    }

    /// <summary>
    /// Step 3, through the container (<c>AIGroup::groupAttackPosition</c>: "garrisoned buildings force attack a ground position"): the force-fire of a garrisoned building at a place
    /// within its rider's reach makes the rider fire its three shots at the place from inside; the rider stays inside. At 30, 60 and 120 Hz the shots fall in the same frames.
    /// </summary>
    public static void AttackPlaceThroughTheContainer()
    {
        long[] Run(SimulationRate rate)
        {
            var scene = FcScene(rate, "FcBunker", "FcRifleman", 2000);
            Check.That(scene.Feeder.AttackPosition(1, [scene.Container.Id.Value], 0, -80, shots: 3).Accepted, $"{rate}: the garrisoned building takes the force-fire.");
            var start = scene.World.Tick;
            var frames = new List<long>();
            for (var step = 0; step < scene.World.Frames(300); step++)
            {
                var result = scene.World.Step();
                for (var shot = 0; shot < ShotsOf(result, scene.Rider); shot++)
                    frames.Add((scene.World.Tick - start + scene.World.Time.TicksPerOriginalFrame - 1) / scene.World.Time.TicksPerOriginalFrame);
            }
            Check.That(frames.Count == 3 && scene.Rider.ContainedBy == scene.Container.Id && !scene.World.IsAttackingPlace(scene.Rider),
                $"{rate}: the rider fired its three shots from inside and is done ({frames.Count}).");
            return [.. frames];
        }

        var reference = Run(SimulationRate.Hz30);
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = Run(rate);
            Check.That(other.SequenceEqual(reference), $"{rate}: the shots fall on the same frames as at 30 Hz ({string.Join(",", other)} against {string.Join(",", reference)}).");
        }
    }
}

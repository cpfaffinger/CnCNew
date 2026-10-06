using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// The paths that square a range of the data (docs/FIXED-POINT-Q39-24.md §5), entered with the "everywhere" values the retail data use: an
/// <c>AttackRange</c> of 999,999 and 1,000,000, a <c>StartAbilityRange</c> of 1,000,000. With 24 fraction bits the square of such a value does not
/// fit a fixed-point number; before the range comparisons went through <c>SquaredReach</c>, every test here threw an <see cref="OverflowException"/>.
/// The tests use the public surface of the world only, so that they run unchanged on the code before the fix.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> SentinelRangeFiles() => new Dictionary<string, string>(AcquisitionFiles())
    {
        ["Data/INI/Object/SentinelRangeFixture.ini"] = """
        Weapon SentinelGun
          PrimaryDamage = 10
          AttackRange = 999999.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Weapon EverywhereGun
          PrimaryDamage = 10
          AttackRange = 1000000.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Weapon SentinelBlast
          PrimaryDamage = 10
          PrimaryDamageRadius = 1000000.0
          AttackRange = 100.0
          DamageType = EXPLOSION
          RadiusDamageAffects = ENEMIES
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Weapon SentinelMinimum
          PrimaryDamage = 10
          AttackRange = 999999.0
          MinimumAttackRange = 800000.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End

        Object SentinelSoldier
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SentinelGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
          End
        End
        Object EverywhereSoldier
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY EverywhereGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = No
          End
        End
        Object BlastSoldier
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SentinelBlast
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = No
          End
        End
        Object MinimumSoldier
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SentinelMinimum
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = No
          End
        End
        Object SentinelGiant
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 99999999.0
            InitialHealth = 99999999.0
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder) SentinelWorld(params (string Template, int Owner, int X, int Y)[] objects)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: SentinelRangeFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        foreach (var (template, owner, x, y) in objects) feeder.Spawn(owner, template, owner: owner, x: x, y: y);
        feeder.SetRelationship(1, 2, Relationship.Enemies);
        feeder.SetRelationship(2, 1, Relationship.Enemies);
        world.Step();
        return (world, feeder);
    }

    private static int ShotsWithin(SimulationWorld world, uint source, int frames)
    {
        var shots = 0;
        for (var index = 0; index < world.Frames(frames); index++)
            shots += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == new EntityId(source));
        return shots;
    }

    public static void WeaponsWithAnEverywhereRangeFire()
    {
        // 1. An order to fire with an AttackRange of 999,999 (the Scud Storm's and the cinematic rifles') and of 1,000,000 (the carrier's): the range check of
        //    the command, of the shot and of the turret's attack intent all compare a squared distance with the square of that range.
        foreach (var (template, range) in new[] { ("SentinelSoldier", 999999), ("EverywhereSoldier", 1000000) })
        {
            var (world, feeder) = SentinelWorld((template, 1, 100, 100), ("AcqDummy", 2, 700, 100));
            var shooter = world.FindEntity(new EntityId(1))!;
            var victim = world.FindEntity(new EntityId(2))!;
            Check.That(world.MaxWeaponRange(shooter) == Fixed.FromInt(range), $"{template} reaches {range} units.");
            var order = feeder.FireWeapon(1, 1, 2);
            Check.That(order.Accepted, $"{template}: firing at a target 600 units away is accepted ({order.Reason}).");
            var shots = ShotsWithin(world, 1, 60);
            Check.That(shots > 0 && Hurt(victim) > Fixed.Zero, $"{template} fires and hits ({shots} shots, {Hurt(victim)} damage).");
            Check.That(!shooter.HasMoveTarget && shooter.X == Fixed.FromInt(100), "It fires from where it stands: everything is in range.");
        }

        // 2. Without an order: an idle unit of a human player looks for victims as far as its weapons reach (the acquisition radius is squared too).
        {
            var (world, _) = SentinelWorld(("SentinelSoldier", 1, 100, 100), ("AcqDummy", 2, 220, 100));
            var shots = ShotsWithin(world, 1, 90);
            Check.That(shots > 0 && Hurt(world.FindEntity(new EntityId(2))!) > Fixed.Zero, $"An idle unit with a range of 999,999 acquires and shoots an enemy by itself ({shots} shots).");
        }

        // 3. A minimum attack range beyond the limit: everything is "too close", and saying so needs the square of the minimum.
        {
            var (world, feeder) = SentinelWorld(("MinimumSoldier", 1, 100, 100), ("AcqDummy", 2, 700, 100));
            var order = feeder.FireWeapon(1, 1, 2);
            var shots = ShotsWithin(world, 1, 60);
            Check.That(shots == 0 && Hurt(world.FindEntity(new EntityId(2))!) == Fixed.Zero, $"A target inside a minimum range of 800,000 is not shot ({order.Reason}, {shots} shots).");
        }

        // 4. A damage radius of 1,000,000: the blast compares squared distances with the square of the radius plus the victim's bounding sphere.
        {
            var (world, feeder) = SentinelWorld(("BlastSoldier", 1, 100, 100), ("AcqDummy", 2, 150, 100), ("AcqDummy", 2, 900, 800));
            Check.That(feeder.FireWeapon(1, 1, 2).Accepted, "The blast weapon fires at the near target.");
            ShotsWithin(world, 1, 30);
            Check.That(Hurt(world.FindEntity(new EntityId(2))!) > Fixed.Zero && Hurt(world.FindEntity(new EntityId(3))!) > Fixed.Zero, "A blast radius of 1,000,000 damages the near and the far enemy.");
        }
    }

    private static IReadOnlyDictionary<string, string> SentinelAbilityFiles() => new Dictionary<string, string>(StagedCaptureFiles())
    {
        ["Data/INI/Object/SentinelAbilityFixture.ini"] = """
        Object EverywhereCapturer
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL CaptureLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SpecialAbility Cap01
            SpecialPowerTemplate = SPECIAL_StagedLotus
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate CapUpdate01
            SpecialPowerTemplate = SPECIAL_StagedLotus
            StartAbilityRange = 1000000.0
            AbilityAbortRange = 2000000.0
            UnpackTime = 1000
            PreparationTime = 2000
            PackTime = 500
            DoCaptureFX = Yes
          End
        End
        """,
    };

    public static void AbilityWithAnEverywhereStartRangeBeginsWhereTheUnitStands()
    {
        // The bomb truck's disguise: StartAbilityRange = 1000000 with an object as the target. "Within the start range" and "outside the abort range"
        // compare the squared distance of the two objects with the square of range plus bounding circles.
        var fixture = new SyntheticFixture(TestRate.Default, files: SentinelAbilityFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "EverywhereCapturer", owner: 1, x: 100, y: 50);
        feeder.Spawn(2, "PlainStation", owner: 2, x: 700, y: 50);
        feeder.SetRelationship(1, 2, Relationship.Enemies);
        world.Step();
        for (var index = 0; index < world.Frames(5); index++) world.Step();
        var actor = world.FindEntity(new EntityId(1))!;
        var ability = actor.Modules.OfType<SpecialAbilityUpdate>().Single();
        var order = feeder.Capture(1, 1, 2);
        Check.That(order.Accepted, $"The ability on a target 600 units away is accepted ({order.Reason}).");

        var samples = ObserveCapture(world, 1, 2, 200);
        // "Approaching" also is the stage in which the unit turns to face its target; with a start range of 1,000,000 it is nothing but that.
        Check.That(StageRun(samples, AbilityStage.Approaching).Length < world.Frames(30), $"With a start range of 1,000,000 the approach is only the turn toward the target ({StageRun(samples, AbilityStage.Approaching).Length} ticks).");
        Check.That(StageRun(samples, AbilityStage.Unpacking).Length == world.Frames(StagedUnpack) && StageRun(samples, AbilityStage.Preparing).Length == world.Frames(StagedPrepare),
            "It unpacks and prepares for the times of its data: the abort range of 2,000,000 never ends the preparation.");
        Check.That(samples.Select(value => value.X).Distinct().Count() == 1 && actor.X == Fixed.FromInt(100), "The unit never moves.");
        Check.That(samples.Count(value => value.CapturedThisTick) == 1 && world.FindEntity(new EntityId(2))!.Owner == new PlayerId(1), "The ability takes effect: the building changes hands once.");
        Check.That(!ability.IsActive, "The ability is over afterwards.");
    }

    public static void HealthRatioOfTheLargestBodiesSurvivesANewMaximum()
    {
        // BodyData.MaxHealth is 99,999,999 on 10 retail objects and above 741,455 on 62: "PRESERVE_RATIO" forms health * newMaximum / oldMaximum.
        var (world, _) = SentinelWorld(("SentinelGiant", 1, 100, 100));
        var body = world.FindEntity(new EntityId(1))!.Body!;
        Check.That(body.MaxHealth == Fixed.FromInt(99999999) && body.Health == body.MaxHealth, "The giant starts with 99,999,999 of 99,999,999.");
        ((ActiveBody)body).AdjustMaxHealth(world, Fixed.FromInt(10000000), "PRESERVE_RATIO");
        Check.That(body.MaxHealth == Fixed.FromInt(109999999) && body.Health == body.MaxHealth, $"Ten million more maximum at full health is full health again ({body.Health} of {body.MaxHealth}).");
        ((ActiveBody)body).AdjustMaxHealth(world, Fixed.FromInt(-109999999 + 50000000), "PRESERVE_RATIO");
        Check.That(body.MaxHealth == Fixed.FromInt(50000000) && body.Health == body.MaxHealth, $"And less maximum too ({body.Health} of {body.MaxHealth}).");
    }

    public static void VisionCircleWithAHugeRadiusCoversTheMap()
    {
        // A reveal radius above 46,340 units squared no longer fits the machine word the circle test uses (a script may name any radius).
        foreach (var radius in new[] { 46000, 46341, 99999, 1000000 })
        {
            var map = new VisionMap(40, 30, 1);
            map.Reveal(0, Fixed.FromInt(205), Fixed.FromInt(95), Fixed.FromInt(radius));
            var reference = new VisionMap(40, 30, 1) { ReferenceMode = true };
            reference.Reveal(0, Fixed.FromInt(205), Fixed.FromInt(95), Fixed.FromInt(radius));
            var explored = new VisionMap(40, 30, 1);
            explored.ExploreCircle(0, Fixed.FromInt(205), Fixed.FromInt(95), Fixed.FromInt(radius));
            for (var cy = 0; cy < 30; cy++)
                for (var cx = 0; cx < 40; cx++)
                    Check.That(map.IsVisible(0, cx, cy) && reference.IsVisible(0, cx, cy) && explored.IsExplored(0, cx, cy), $"Radius {radius}: cell ({cx}, {cy}) is inside the circle.");
        }
        var small = new VisionMap(40, 30, 1);
        small.Reveal(0, Fixed.FromInt(205), Fixed.FromInt(95), Fixed.FromInt(30));
        Check.That(small.IsVisible(0, 20, 9) && !small.IsVisible(0, 30, 9), "An ordinary radius still is a circle.");
    }
}

using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static readonly string RiderIni = """
        Locomotor RiderFastLoco
          Surfaces = GROUND
          Speed = 90
          Acceleration = 900
          Braking = 900
          TurnRate = 90
        End
        Locomotor RiderSlowLoco
          Surfaces = GROUND
          Speed = 30
          Acceleration = 900
          Braking = 900
          TurnRate = 90
        End
        Weapon RiderGun
          PrimaryDamage = 1.0
          AttackRange = 100.0
          DamageType = SMALL_ARMS
          DeathType = NORMAL
          DelayBetweenShots = 100
        End
        Weapon KellGun
          PrimaryDamage = 1.0
          AttackRange = 200.0
          DamageType = KILL_PILOT
          DeathType = NORMAL
          DelayBetweenShots = 100
        End
        Object RiderGrunt
          KindOf = INFANTRY SELECTABLE
          TransportSlotCount = 1
          Locomotor = SET_NORMAL RiderSlowLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object RiderKell
          KindOf = INFANTRY SELECTABLE
          TransportSlotCount = 1
          Locomotor = SET_NORMAL RiderSlowLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object RiderBike
          KindOf = VEHICLE SELECTABLE
          Locomotor = SET_NORMAL RiderFastLoco
          Locomotor = SET_SLUGGISH RiderSlowLoco
          WeaponSet
            Conditions = None
            Weapon = PRIMARY None
          End
          WeaponSet
            Conditions = WEAPON_RIDER1
            Weapon = PRIMARY RiderGun
          End
          WeaponSet
            Conditions = WEAPON_RIDER2
            Weapon = PRIMARY KellGun
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = RiderChangeContain Rider01
            Slots = 1
            AllowInsideKindOf = INFANTRY
            InitialPayload = RiderGrunt 1
            Rider1 = RiderGrunt RIDER1 WEAPON_RIDER1 STATUS_RIDER1 RiderSetOne SET_NORMAL
            Rider2 = RiderKell RIDER2 WEAPON_RIDER2 STATUS_RIDER2 RiderSetTwo SET_SLUGGISH
            ScuttleDelay = 600
            ScuttleStatus = TOPPLED
          End
        End
        Upgrade Upgrade_RiderNuclear
          Type = PLAYER
        End
        Object RiderTank
          KindOf = VEHICLE SELECTABLE
          Locomotor = SET_NORMAL RiderSlowLoco
          Locomotor = SET_NORMAL_UPGRADED RiderFastLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = LocomotorSetUpgrade Nuclear01
            TriggeredBy = Upgrade_RiderNuclear
          End
        End
        """;

    private static readonly string ClimberIni = """
        Locomotor ClimberGround
          Surfaces = GROUND
          Speed = 60
          Acceleration = 900
          Braking = 900
          TurnRate = 360
        End
        Locomotor ClimberCliff
          Surfaces = CLIFF
          Speed = 15
          Acceleration = 900
          Braking = 900
          TurnRate = 360
        End
        Object Climber
          KindOf = INFANTRY SELECTABLE
          Locomotor = SET_NORMAL ClimberGround ClimberCliff
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Walker
          KindOf = INFANTRY SELECTABLE
          Locomotor = SET_NORMAL ClimberGround
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    /// <summary><c>LocomotorSet::findLocomotor</c>, <c>chooseGoodLocomotorFromCurrentSet</c>: a set with a cliff locomotor crosses a cliff, at that locomotor's speed; a set without one does not.</summary>
    public static void AClimberCrossesACliff()
    {
        var frames = new List<long>();
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var files = new Dictionary<string, string>(GroupMoveFiles()) { ["Data/INI/Object/Climbers.ini"] = ClimberIni };
            var wall = Enumerable.Range(0, NavCells).Select(y => (15, y));
            var (world, feeder, _) = RatedWorld(rate, files, NavTerrain(wall));
            feeder.Spawn(1, "Climber", x: 100, y: 100);
            feeder.Spawn(1, "Walker", x: 100, y: 160);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var climber = world.Entities.Single(value => value.Template.Name == "Climber");
            var walker = world.Entities.Single(value => value.Template.Name == "Walker");
            var (goalX, goalY) = (Fixed.FromInt(185), Fixed.FromInt(100));
            Check.That(world.Submit(new CnCNew.Simulation.Commands.SetMoveTargetCommand(new PlayerId(1), 1000, world.Tick, climber.Id, goalX, goalY)).Accepted, $"{rate}: the climb is ordered.");
            Check.That(world.Submit(new CnCNew.Simulation.Commands.SetMoveTargetCommand(new PlayerId(1), 1001, world.Tick, walker.Id, goalX, Fixed.FromInt(160))).Accepted, $"{rate}: the walk is ordered.");
            var slowest = Fixed.FromInt(1000);
            var ai = climber.Modules.OfType<AIUpdateInterface>().Single();
            long steps = 0;
            for (; steps < world.Frames(1500) && (steps < 2 || climber.HasMoveTarget); steps++)
            {
                world.Step();
                if (ai.CurrentLocomotor!.Surfaces == CnCNew.Simulation.Templates.LocomotorSurfaces.Cliff) slowest = Fixed.Min(slowest, ai.CurrentLocomotor.Speed);
            }
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            Check.That(climber.X == goalX, $"{rate}: the climber crosses ({climber.X}, {climber.Y}, steps {steps}, target {climber.HasMoveTarget}, cliff speed {slowest}, loco {ai.CurrentLocomotor!.Surfaces}).");
            Check.That(slowest < Fixed.FromInt(1), $"{rate}: on the cliff it climbs with the cliff's locomotor ({slowest}).");
            Check.That(walker.X < Fixed.FromInt(160), $"{rate}: the walker does not ({walker.X}).");
            frames.Add(steps / rate.TicksPerOriginalFrame);
        }
        Check.That(frames.Max() - frames.Min() <= 1, $"The climb takes the same frames at every rate ({string.Join(", ", frames)}).");
    }

    private static IReadOnlyDictionary<string, string> RiderFiles() => new Dictionary<string, string>(GroupMoveFiles()) { ["Data/INI/Object/Riders.ini"] = RiderIni };

    /// <summary><c>RiderChangeContain::onContaining</c>: who rides decides the weapon, the command set and the locomotor of the bike; the set of the rider is gone with him.</summary>
    public static void ARiderArmsItsBike()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, feeder, _) = RatedWorld(rate, RiderFiles(), NavTerrain());
            feeder.Spawn(1, "RiderBike", x: 100, y: 100);
            feeder.Spawn(1, "RiderKell", x: 130, y: 100);
            for (var step = 0; step < world.Frames(3); step++) world.Step();
            var bike = world.Entities.Single(value => value.Template.Name == "RiderBike");
            var contain = bike.Modules.OfType<RiderChangeContain>().Single();
            var ai = bike.Modules.OfType<AIUpdateInterface>().Single();
            string? Weapon() => SimulationWorld.WeaponIndexIn(bike, WeaponSlot.Primary) is >= 0 and var index ? world.Weapons[index].Name : null;
            Check.That(Weapon() == "RiderGun", $"{rate}: the first rider arms the bike ({Weapon()}, rider {contain.RiderIndex}, riders {contain.ContainCount}, bit {bike.RiderWeaponBit}, sets {string.Join(";", bike.Template.WeaponSets.Select(set => set.Conditions))}).");
            Check.That(contain.RiderCommandSet == "RiderSetOne", $"{rate}: his command set.");
            var fast = ai.CurrentLocomotor!.Speed;
            var kell = world.Entities.Single(value => value.Template.Name == "RiderKell");
            contain.Add(world, kell);
            Check.That(Weapon() == "KellGun", $"{rate}: the next rider brings his gun ({Weapon()}).");
            Check.That(contain.RiderCommandSet == "RiderSetTwo", $"{rate}: and his command set.");
            Check.That(ai.CurrentLocomotor!.Speed < fast, $"{rate}: and his locomotor.");
        }
    }

    /// <summary><c>LocomotorSetUpgrade::upgradeImplementation</c>: the upgrade makes the normal set the upgraded one, at once.</summary>
    public static void AnUpgradeChangesTheLocomotor()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, feeder, _) = RatedWorld(rate, RiderFiles(), NavTerrain());
            feeder.Spawn(1, "RiderTank", x: 100, y: 100);
            for (var step = 0; step < world.Frames(3); step++) world.Step();
            var tank = world.Entities.Single(value => value.Template.Name == "RiderTank");
            var ai = tank.Modules.OfType<AIUpdateInterface>().Single();
            var slow = ai.CurrentLocomotor!.Speed;
            feeder.GrantUpgrade(1, "Upgrade_RiderNuclear");
            for (var step = 0; step < world.Frames(3); step++) world.Step();
            Check.That(ai.CurrentLocomotor!.Speed > slow, $"{rate}: the upgrade changes it.");
            Check.That(ai.ChooseLocomotorSet(world, "SET_NORMAL") && ai.CurrentLocomotor!.Speed > slow, $"{rate}: the normal set stays the upgraded one.");
        }
    }
}

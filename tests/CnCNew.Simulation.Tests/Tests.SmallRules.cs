using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static readonly string SmallRulesIni = """
        Object RefundDepot
          KindOf = STRUCTURE SELECTABLE
          BuildCost = 1000
          RefundValue = 650
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object PlainDepot
          KindOf = STRUCTURE SELECTABLE
          BuildCost = 1000
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object OilDerrick
          KindOf = STRUCTURE SELECTABLE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AutoDepositUpdate Deposit01
            DepositTiming = 1000
            DepositAmount = 0
            InitialCaptureBonus = 1000
          End
        End
        Object Superweapon
          KindOf = STRUCTURE SELECTABLE
          ShroudRevealToAllRange = 60.0
          VisionRange = 10.0
          ShroudClearingRange = 10.0
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Object NoSuperweapon
          KindOf = STRUCTURE SELECTABLE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        """;

    private static readonly string StealthRulesIni = """
        Weapon HiddenGun
          PrimaryDamage = 1.0
          AttackRange = 100.0
          DamageType = SMALL_ARMS
          DeathType = NORMAL
          DelayBetweenShots = 100
        End
        Locomotor HiddenLoco
          Surfaces = GROUND
          Speed = 30
          Acceleration = 900
          Braking = 900
          TurnRate = 90
        End
        Object HiddenShooter
          KindOf = INFANTRY SELECTABLE CAN_ATTACK
          VisionRange = 200.0
          ShroudClearingRange = 200.0
          Locomotor = SET_NORMAL HiddenLoco
          WeaponSet
            Conditions = None
            Weapon = PRIMARY HiddenGun
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = StealthUpdate Stealth01
            StealthDelay = 100
            InnateStealth = Yes
            StealthForbiddenConditions = ATTACKING
          End
        End
        Object HiddenVictim
          KindOf = STRUCTURE SELECTABLE
          Body = StructureBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
        End
        """;

    /// <summary><c>StealthUpdate::allowedToStealth</c>: <c>STEALTH_NOT_WHILE_ATTACKING</c> - nothing ever set the status it reads, so that nothing that attacks was ever seen.</summary>
    public static void AnAttackerShowsItself()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var files = new Dictionary<string, string>(SmallRuleFiles()) { ["Data/INI/Object/StealthRules.ini"] = StealthRulesIni };
            var (world, feeder, _) = RatedWorld(rate, files, NavTerrain());
            feeder.Spawn(1, "HiddenShooter", x: 100, y: 100);
            feeder.Spawn(2, "HiddenVictim", x: 150, y: 100);
            for (var step = 0; step < world.Frames(20); step++) world.Step();
            var shooter = world.Entities.Single(value => value.Template.Name == "HiddenShooter");
            var victim = world.Entities.Single(value => value.Template.Name == "HiddenVictim");
            var stealthed = CnCNew.Simulation.Modules.ObjectStatusBits.Index("STEALTHED");
            Check.That(shooter.HasStatus(stealthed), $"{rate}: it hides.");
            Check.That(world.Submit(new CnCNew.Simulation.Commands.PursueCommand(new PlayerId(1), 1000, world.Tick, shooter.Id, victim.Id, victim.X, victim.Y)).Accepted, $"{rate}: the attack is ordered.");
            for (var step = 0; step < world.Frames(3); step++) world.Step();
            Check.That(!shooter.HasStatus(stealthed), $"{rate}: while it attacks it is seen.");
        }
    }

    /// <summary><c>SupplyCenterDockUpdate::action</c>: a hidden building hides what comes to it for a while, and an open one does not; a grant of another kind takes precedence.</summary>
    public static void AHiddenBuildingHidesItsTruck()
    {
        var stealthed = CnCNew.Simulation.Modules.ObjectStatusBits.Index("STEALTHED");
        var ini = StealthRulesIni.Replace("Object HiddenVictim", """
            Object Truck
              KindOf = VEHICLE SELECTABLE
              Body = ActiveBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = StealthUpdate Stealth01
              End
            End
            Object HiddenStore
              KindOf = STRUCTURE SELECTABLE
              Body = StructureBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = StealthUpdate Stealth01
                StealthDelay = 100
                InnateStealth = Yes
              End
            End
            Object HiddenVictim
            """);
        var files = new Dictionary<string, string>(SmallRuleFiles()) { ["Data/INI/Object/StealthRules.ini"] = ini };
        var (world, feeder, _) = RatedWorld(SimulationRate.FromTicksPerSecond(60), files, NavTerrain());
        feeder.Spawn(1, "HiddenVictim", x: 100, y: 100);
        feeder.Spawn(1, "HiddenStore", x: 100, y: 140);
        feeder.Spawn(1, "Truck", x: 140, y: 100);
        for (var step = 0; step < world.Frames(20); step++) world.Step();
        var building = world.Entities.Single(value => value.Template.Name == "HiddenVictim");
        var truck = world.Entities.Single(value => value.Template.Name == "Truck");
        StealthUpdate.GrantFrom(world, building, truck, 600);
        for (var step = 0; step < world.Frames(3); step++) world.Step();
        Check.That(!truck.HasStatus(stealthed), "An open building hides nothing.");
        var store = world.Entities.Single(value => value.Template.Name == "HiddenStore");
        Check.That(store.HasStatus(stealthed), "The store hides.");
        StealthUpdate.GrantFrom(world, store, truck, 600);
        for (var step = 0; step < world.Frames(3); step++) world.Step();
        Check.That(truck.HasStatus(stealthed), "A hidden one hides its truck.");
        for (var step = 0; step < 700; step++) world.Step();
        Check.That(!truck.HasStatus(stealthed), "For a while.");
    }

    private static IReadOnlyDictionary<string, string> SmallRuleFiles() => new Dictionary<string, string>(GroupMoveFiles()) { ["Data/INI/Object/SmallRules.ini"] = SmallRulesIni };

    /// <summary><c>BuildAssistant::update</c>: "0 is the init, and means you have no override".</summary>
    public static void ASaleKeepsItsRefund()
    {
        var (world, _, _) = RatedWorld(SimulationRate.FromTicksPerSecond(60), SmallRuleFiles(), NavTerrain());
        Check.That(world.Templates.TryGet("RefundDepot", out var refund) && refund.RefundValue == Fixed.FromInt(650), "The value is read.");
        Check.That(world.Templates.TryGet("PlainDepot", out var plain) && plain.RefundValue == Fixed.Zero, "Without one there is no override.");
    }

    /// <summary><c>awardInitialCaptureBonus</c>: the first capture after its first deposit date pays once; the object's first owner has nothing.</summary>
    public static void ACapturedDerrickPaysOnce()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, feeder, _) = RatedWorld(rate, SmallRuleFiles(), NavTerrain());
            feeder.Spawn(0, "OilDerrick", x: 100, y: 100);
            for (var step = 0; step < world.Frames(3); step++) world.Step();
            var derrick = world.Entities.Single(value => value.Template.Name == "OilDerrick");
            var start = world.MoneyOf(new PlayerId(1));
            world.TransferOwnership(derrick.Id, new PlayerId(1));
            Check.That(world.MoneyOf(new PlayerId(1)) == start, $"{rate}: too early to pay ({world.MoneyOf(new PlayerId(1))}).");
            world.TransferOwnership(derrick.Id, PlayerId.System);
            for (var step = 0; step < world.Frames(60); step++) world.Step();
            world.TransferOwnership(derrick.Id, new PlayerId(1));
            Check.That(world.MoneyOf(new PlayerId(1)) == start + Fixed.FromInt(1000), $"{rate}: the capture pays ({world.MoneyOf(new PlayerId(1))}).");
            world.TransferOwnership(derrick.Id, new PlayerId(2));
            world.TransferOwnership(derrick.Id, new PlayerId(1));
            Check.That(world.MoneyOf(new PlayerId(1)) == start + Fixed.FromInt(1000), $"{rate}: once.");
        }
    }

    /// <summary><c>Object::look</c>: enemies see around a superweapon, at its own range, whatever they have near it.</summary>
    public static void EnemiesSeeASuperweapon()
    {
        var (world, feeder, _) = RatedWorld(SimulationRate.FromTicksPerSecond(60), SmallRuleFiles(), NavTerrain());
        feeder.Spawn(1, "Superweapon", x: 50, y: 50);
        feeder.Spawn(1, "NoSuperweapon", x: 150, y: 150);
        for (var step = 0; step < world.Frames(40); step++) world.Step();
        var (seen, hidden) = (world.Entities.Single(value => value.Template.Name == "Superweapon"), world.Entities.Single(value => value.Template.Name == "NoSuperweapon"));
        Check.That(world.CanPlayerSee(new PlayerId(2), seen), "The enemy sees the superweapon.");
        Check.That(!world.CanPlayerSee(new PlayerId(2), hidden), "And nothing else of that player.");
    }
}

using CnCNew.Simulation.Ids;
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

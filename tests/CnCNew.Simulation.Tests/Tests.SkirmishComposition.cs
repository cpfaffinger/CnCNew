using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Time;

static partial class Tests
{
    /// <summary>
    /// Two soldiers of the same price from one camp — a rifle that hurts soft armour and hardly scratches hard armour, a bazooka the other way round — and two kinds of
    /// enemy object that only stand there: a hard one and a soft one, with so much health that nothing changes while the test counts.
    /// </summary>
    private static IReadOnlyDictionary<string, string> CompositionFiles() => new Dictionary<string, string>(SkirmishFiles())
    {
        ["Data/INI/Object/CompositionFixture.ini"] = """
        Armor SoftArmor
          Armor = Default 100%
          Armor = ARMOR_PIERCING 10%
        End
        Armor HardArmor
          Armor = Default 100%
          Armor = SMALL_ARMS 10%
        End
        Weapon CompRifle
          PrimaryDamage = 20
          AttackRange = 80.0
          DamageType = SMALL_ARMS
          ClipSize = 0
          DelayBetweenShots = 400
        End
        Weapon CompBazooka
          PrimaryDamage = 20
          AttackRange = 80.0
          DamageType = ARMOR_PIERCING
          ClipSize = 0
          DelayBetweenShots = 400
        End
        Object AlphaRifle
          Side = Alpha
          KindOf = SELECTABLE INFANTRY
          VisionRange = 120.0
          ShroudClearingRange = 120.0
          BuildCost = 25
          BuildTime = 1
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 80.0
            InitialHealth = 80.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CompRifle
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object AlphaBazooka
          Side = Alpha
          KindOf = SELECTABLE INFANTRY
          VisionRange = 120.0
          ShroudClearingRange = 120.0
          BuildCost = 25
          BuildTime = 1
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 80.0
            InitialHealth = 80.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CompBazooka
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object AlphaCamp
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE FS_BARRACKS
          VisionRange = 200.0
          ShroudClearingRange = 200.0
          BuildCost = 80
          BuildTime = 1
          CommandSet = AlphaCampSet
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
        End
        CommandButton Cmd_AlphaRifle
          Command = UNIT_BUILD
          Object = AlphaRifle
        End
        CommandButton Cmd_AlphaBazooka
          Command = UNIT_BUILD
          Object = AlphaBazooka
        End
        CommandSet AlphaCampSet
          1 = Cmd_AlphaRifle
          2 = Cmd_AlphaBazooka
        End
        Object BetaHardTarget
          Side = Beta
          KindOf = SELECTABLE VEHICLE
          BuildCost = 500
          Body = ActiveBody Body01
            MaxHealth = 900000.0
            InitialHealth = 900000.0
          End
          ArmorSet
            Conditions = None
            Armor = HardArmor
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object BetaSoftTarget
          Side = Beta
          KindOf = SELECTABLE INFANTRY
          BuildCost = 500
          Body = ActiveBody Body01
            MaxHealth = 900000.0
            InitialHealth = 900000.0
          End
          ArmorSet
            Conditions = None
            Armor = SoftArmor
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    /// <summary>
    /// The composition rule "exchange value" (observation D of docs/AI-OBSERVED-2026-10-08.md): the AI sees what the enemy fields and builds what hurts it. Against hard
    /// targets it builds mostly bazookas, against soft ones mostly rifles, and still some of the other (the floor of variety); without the rule, or with nothing seen, both
    /// with the same chance. Two runs agree draw for draw.
    /// </summary>
    public static void SkirmishAiComposesItsArmyAgainstWhatItSees()
    {
        (int Rifles, int Bazookas, int Informed, int Uninformed) Play(ArmyComposition? composition, string? target, uint seed = 11)
        {
            var (world, feeder, fixture) = RatedWorld(TestRate.Default, CompositionFiles());
            feeder.Spawn(1, "AlphaCamp", x: 100, y: 100);
            if (target is not null)
                for (var index = 0; index < 4; index++) feeder.Spawn(2, target, x: 160 + 12 * index, y: 100);
            feeder.GiveMoney(1, 100_000);
            var options = new SkirmishAiOptions(new PlayerId(1), seed, Fixed.FromInt(100), Fixed.FromInt(100), "Alpha", string.Empty, ImmutableArray<(Fixed X, Fixed Y)>.Empty, Composition: composition);
            var ai = new SkirmishAi(world, fixture.Templates, options, world.Submit);
            var end = world.Time.OriginalFrames(3_000);
            while (world.Tick < end)
            {
                ai.Update();
                world.Step();
            }
            int Count(string unit) => ai.Stats.ArmyProduction.Where(pair => pair.Key.EndsWith("queued:" + unit, StringComparison.Ordinal)).Sum(pair => pair.Value);
            return (Count("AlphaRifle"), Count("AlphaBazooka"), ai.Stats.InformedDraws, ai.Stats.UninformedDraws);
        }

        var againstHard = Play(ArmyComposition.ExchangeValue, "BetaHardTarget");
        var againstSoft = Play(ArmyComposition.ExchangeValue, "BetaSoftTarget");
        var blind = Play(ArmyComposition.ExchangeValue, null);
        var uniform = Play(ArmyComposition.Uniform, "BetaHardTarget");
        Console.WriteLine($"       composition (rifles/bazookas): against hard targets {againstHard.Rifles}/{againstHard.Bazookas}, against soft {againstSoft.Rifles}/{againstSoft.Bazookas}, nothing seen {blind.Rifles}/{blind.Bazookas}, rule off {uniform.Rifles}/{uniform.Bazookas}");

        Check.That(againstHard.Rifles + againstHard.Bazookas >= 100 && againstSoft.Rifles + againstSoft.Bazookas >= 100, "The camp produced at least a hundred soldiers in each run.");
        Check.That(againstHard.Bazookas >= 3 * againstHard.Rifles, $"Against hard armour the AI builds mostly bazookas ({againstHard.Bazookas} to {againstHard.Rifles} rifles).");
        Check.That(againstSoft.Rifles >= 3 * againstSoft.Bazookas, $"Against soft armour the AI builds mostly rifles ({againstSoft.Rifles} to {againstSoft.Bazookas} bazookas).");
        Check.That(againstHard.Rifles > 0 && againstSoft.Bazookas > 0, "The floor of variety: the weaker choice is still built now and then.");
        Check.That(againstHard.Informed > 0 && againstHard.Uninformed <= 2, $"With the enemy in sight the draws are informed ({againstHard.Informed} informed, {againstHard.Uninformed} before the first look).");
        Check.That(blind.Informed == 0 && blind.Uninformed > 0, "With nothing seen every draw falls back to the uniform one.");
        Check.That(uniform.Informed == 0 && uniform.Uninformed == 0, "The uniform composition never asks the rule.");
        foreach (var (name, run) in new[] { ("nothing seen", blind), ("rule off", uniform) })
            Check.That(run.Rifles * 2 > run.Bazookas && run.Bazookas * 2 > run.Rifles, $"{name}: both soldiers about equally often ({run.Rifles} rifles, {run.Bazookas} bazookas).");
        Check.That(Play(ArmyComposition.ExchangeValue, "BetaHardTarget") == againstHard, "The same seed gives the same army.");
        Check.That(Play(ArmyComposition.ExchangeValue, "BetaHardTarget", seed: 12) != againstHard, "The seed of the AI's stream is an input of the draw.");
        Check.That(Play(null, "BetaHardTarget") == (SkirmishAi.DefaultComposition == ArmyComposition.ExchangeValue ? againstHard : uniform), "Options that name no composition use the default.");
    }
}

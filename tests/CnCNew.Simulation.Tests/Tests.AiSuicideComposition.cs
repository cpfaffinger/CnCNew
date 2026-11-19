using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Time;

static partial class Tests
{
    /// <summary>
    /// The fixture of <see cref="SkirmishAiComposesItsArmyAgainstWhatItSees"/> and a second camp that offers the rifle and a bomber of the same price, whose weapon is the bomb
    /// truck's: 999,999 damage to itself only (<c>RadiusDamageAffects = SELF SUICIDE</c>, the real damage is its death weapon). The enemy is a soft target of 200 health that
    /// nothing may attack (<c>UNATTACKABLE</c>), so that it stays in sight unchanged.
    /// </summary>
    private static IReadOnlyDictionary<string, string> SuicideCompositionFiles() => new Dictionary<string, string>(CompositionFiles())
    {
        ["Data/INI/Object/SuicideCompositionFixture.ini"] = """
        Weapon CompSuicide
          AttackRange = 1.0
          PrimaryDamage = 999999.0
          PrimaryDamageRadius = 1.0
          DamageDealtAtSelfPosition = Yes
          RadiusDamageAffects = SELF SUICIDE
          DamageType = EXPLOSION
          DelayBetweenShots = 0
          ClipSize = 1
          ClipReloadTime = 0
          AutoReloadsClip = No
        End
        Object AlphaBomber
          Side = Alpha
          KindOf = SELECTABLE VEHICLE
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
            Weapon = PRIMARY CompSuicide
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object AlphaBomberCamp
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE FS_BARRACKS
          VisionRange = 200.0
          ShroudClearingRange = 200.0
          BuildCost = 80
          BuildTime = 1
          CommandSet = AlphaBomberCampSet
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
        CommandButton Cmd_AlphaBomber
          Command = UNIT_BUILD
          Object = AlphaBomber
        End
        CommandSet AlphaBomberCampSet
          1 = Cmd_AlphaRifle
          2 = Cmd_AlphaBomber
        End
        Object BetaFragileTarget
          Side = Beta
          KindOf = SELECTABLE INFANTRY UNATTACKABLE
          BuildCost = 500
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
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
    /// #464: a weapon that kills its owner counts as one shot in a lifetime, worth at most one kill. The composition spread its damage over the lifetime before capping it at the
    /// victim's health, so the bomb truck's 999,999 (capped at 100,000: 55 per frame) counted as a kill of every enemy in every frame, and once a GLA player's Palace stood, bomb
    /// trucks took a fifth of its vehicles. Against soft targets of 200 health the rifle (20 damage every 400 ms: a kill in 10 s) is worth some fifteen times the bomber of
    /// the same price (one kill in its lifetime of at most a minute): the AI builds mostly rifles, and bombers only by the floor of variety (before: mostly bombers).
    /// </summary>
    public static void SkirmishAiCountsASuicideWeaponAsOneShot()
    {
        var (world, feeder, fixture) = RatedWorld(TestRate.Default, SuicideCompositionFiles());
        feeder.Spawn(1, "AlphaBomberCamp", x: 100, y: 100);
        for (var index = 0; index < 4; index++) feeder.Spawn(2, "BetaFragileTarget", x: 160 + 12 * index, y: 100);
        feeder.GiveMoney(1, 100_000);
        var options = new SkirmishAiOptions(new PlayerId(1), 11, Fixed.FromInt(100), Fixed.FromInt(100), "Alpha", string.Empty, ImmutableArray<(Fixed X, Fixed Y)>.Empty, Composition: ArmyComposition.ExchangeValue);
        var ai = new SkirmishAi(world, fixture.Templates, options, world.Submit);
        var end = world.Time.OriginalFrames(3_000);
        while (world.Tick < end)
        {
            ai.Update();
            world.Step();
        }
        int Count(string unit) => ai.Stats.ArmyProduction.Where(pair => pair.Key.EndsWith("queued:" + unit, StringComparison.Ordinal)).Sum(pair => pair.Value);
        var (rifles, bombers) = (Count("AlphaRifle"), Count("AlphaBomber"));
        Console.WriteLine($"       composition against soft targets of 200 health: {rifles} rifles, {bombers} bombers ({ai.Stats.InformedDraws} informed draws)");
        Check.That(rifles + bombers >= 100 && ai.Stats.InformedDraws > 0, $"The camp produced at least a hundred units by informed draws ({rifles + bombers}, {ai.Stats.InformedDraws} informed).");
        Check.That(rifles >= 3 * bombers, $"A weapon that kills its owner is worth one kill in a lifetime: mostly rifles ({rifles} to {bombers} bombers).");
        Check.That(bombers > 0, "The floor of variety still builds a bomber now and then.");
    }
}

using CnCNew.Simulation.Testing;

namespace CnCNew.Tests;

/// <summary>
/// The synthetic two-faction skirmish fixture (sides Alpha and Beta: command centre, dozer, soldier, plant, barracks, shared special powers) as definition files. Shared as
/// source by the test programs that play a skirmish on it (the simulation tests, the network tests).
/// </summary>
internal static class SkirmishFixtureFiles
{
    public static string SkirmishSide(string side) => $$"""
        Object {{side}}Center
          Side = {{side}}
          KindOf = SELECTABLE STRUCTURE COMMANDCENTER
          VisionRange = 150.0
          ShroudClearingRange = 150.0
          CommandSet = {{side}}CenterSet
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 15.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 600.0
            InitialHealth = 600.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = {{side}}Shared
          End
        End
        Object {{side}}Dozer
          Side = {{side}}
          KindOf = SELECTABLE VEHICLE DOZER
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          BuildCost = 20
          BuildTime = 1
          CommandSet = {{side}}DozerSet
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DozerAIUpdate AI01
            RepairHealthPercentPerSecond = 10%
          End
        End
        Object {{side}}Soldier
          Side = {{side}}
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
            Weapon = PRIMARY SkirmishGun
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
          End
        End
        Object {{side}}Plant
          Side = {{side}}
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 60
          BuildTime = 1
          EnergyProduction = 5
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object {{side}}Barracks
          Side = {{side}}
          KindOf = SELECTABLE STRUCTURE FS_BARRACKS
          BuildCost = 80
          BuildTime = 1
          CommandSet = {{side}}BarracksSet
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
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
        CommandButton Cmd_{{side}}Dozer
          Command = UNIT_BUILD
          Object = {{side}}Dozer
        End
        CommandButton Cmd_{{side}}Soldier
          Command = UNIT_BUILD
          Object = {{side}}Soldier
        End
        CommandButton Cmd_{{side}}Plant
          Command = DOZER_CONSTRUCT
          Object = {{side}}Plant
        End
        CommandButton Cmd_{{side}}Barracks
          Command = DOZER_CONSTRUCT
          Object = {{side}}Barracks
        End
        CommandButton Cmd_{{side}}Sell
          Command = SELL
        End
        CommandSet {{side}}CenterSet
          1 = Cmd_{{side}}Dozer
          2 = Cmd_{{side}}Soldier
          3 = Cmd_{{side}}Sell
          4 = Cmd_{{side}}Soldier
        End
        CommandSet {{side}}DozerSet
          1 = Cmd_{{side}}Plant
          2 = Cmd_{{side}}Barracks
        End
        CommandSet {{side}}BarracksSet
          1 = Cmd_{{side}}Soldier
        End
        PlayerTemplate Faction{{side}}
          Side = {{side}}
          PlayableSide = Yes
          StartMoney = 500
          StartingBuilding = {{side}}Center
          StartingUnit0 = {{side}}Dozer
          StartingUnit1 = {{side}}Soldier
        End
        """;

    public static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/SkirmishFixture.ini"] = $$"""
        Weapon SkirmishGun
          PrimaryDamage = 20
          AttackRange = 80.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 400
        End
        Locomotor SkirmishLoco
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 60.0
          Acceleration = 60.0
          Braking = 60.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        SpecialPower AlphaShared
          ReloadTime = 10000
          SharedSyncedTimer = Yes
        End
        SpecialPower BetaShared
          ReloadTime = 10000
          SharedSyncedTimer = Yes
        End
        {{SkirmishSide("Alpha")}}
        {{SkirmishSide("Beta")}}
        """,
        ["Data/INI/GameData.ini"] = "GameData\n  SellPercentage = 50%\nEnd\n",
    };
}

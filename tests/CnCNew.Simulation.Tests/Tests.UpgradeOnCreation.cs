using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> UpgradeOnCreationFiles()
    {
        var files = new Dictionary<string, string>(ConstructionFiles());
        files["Data/INI/Object/UpgradeOnCreationFixture.ini"] = """
        Upgrade Upgrade_Plated
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End
        SpecialPower SPECIAL_Seize
          Enum = SPECIAL_CASH_HACK
          ReloadTime = 1000
        End

        Object PlatedGrunt
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = ArmorUpgrade Armor01
            TriggeredBy = Upgrade_Plated
          End
          Behavior = CashHackSpecialPower Seize01
            SpecialPowerTemplate = SPECIAL_Seize
            MoneyAmount = 50
            StartsPaused = Yes
          End
          Behavior = UnpauseSpecialPowerUpgrade Unpause01
            SpecialPowerTemplate = SPECIAL_Seize
            TriggeredBy = Upgrade_Plated
          End
        End
        Object PlatedTower
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 50
          BuildTime = 2
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = ArmorUpgrade Armor01
            TriggeredBy = Upgrade_Plated
          End
        End
        """;
        return files;
    }

    /// <summary>
    /// #314: <c>Object::initObject</c> runs <c>updateUpgradeModules</c>, so an object made after its player's upgrade has it - a ranger trained after the capture research
    /// is unpaused like the rangers that stood when it completed. <c>updateUpgradeModules</c> does nothing while the object is under construction, and the dozer runs it
    /// again when construction completes.
    /// </summary>
    public static void AnObjectMadeAfterItsPlayersUpgradeHasIt()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: UpgradeOnCreationFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.GiveMoney(1, 1000);
        feeder.Spawn(1, "Dozer", x: 0, y: 0);
        world.Step();
        var dozer = ConstructionFind(world, "Dozer");
        Check.That(feeder.Construct(1, dozer.Id.Value, "PlatedTower", 60, 0).Accepted, "The dozer is told to build the tower.");
        world.Step();
        var site = ConstructionFind(world, "PlatedTower");
        Check.That(IsSite(site), "The tower is a site.");

        world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_Plated");
        Check.That(!site.HasPlayerUpgradeArmor, "No upgrade runs on a structure under construction.");

        feeder.Spawn(1, "PlatedGrunt", x: -60, y: 0);
        world.Step();
        var grunt = ConstructionFind(world, "PlatedGrunt");
        var made = grunt.SpawnTick;
        Check.That(grunt.HasPlayerUpgradeArmor, "A unit made after its player's armour upgrade wears the armour.");
        var power = grunt.Modules.OfType<SpecialPowerModuleBase>().Single();
        Check.That(!power.IsPaused, "Its paused ability is unpaused by the upgrade the player already has.");
        while (world.Tick <= made + world.Frames(30) - 1)
        {
            Check.That(!power.IsReady(world), $"The ability recharges for its reload from the unit's creation (tick {world.Tick}).");
            world.Step();
        }
        Check.That(power.IsReady(world), "After its reload the ability is ready.");

        var completed = StepUntil(world, 300, () => !IsSite(site));
        Check.That(completed < 300 && site.HasPlayerUpgradeArmor, $"When construction completes the tower takes the player's upgrade (after {completed} frames).");
    }
}

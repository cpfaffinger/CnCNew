using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// F-54, the stealth garrison (docs/STEALTH-GARRISON.md): a building held only by riders of kind <c>STEALTH_GARRISON</c> whose first rider is not detected shows the players its owner
/// does not count as allies the owner it had before (<c>GarrisonContain::getApparentControllingPlayer</c>); target acquisition and capture read that apparent owner.
/// </summary>
static partial class Tests
{
    private const string StealthGarrisonIni = """
        Object FcSneak
          KindOf = SELECTABLE INFANTRY STEALTH_GARRISON
          TransportSlotCount = 1
          VisionRange = 150.0
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY FcRifle
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
          End
          Behavior = StealthUpdate Stealth01
            StealthDelay = 0
            InnateStealth = Yes
          End
        End
        Object FcHunter
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY FcRifle
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes ATTACK_BUILDINGS
          End
        End
        Object FcCloakedBunker
          KindOf = STRUCTURE CAPTURABLE
          Geometry = CYLINDER
          GeometryMajorRadius = 30.0
          GeometryMinorRadius = 30.0
          GeometryHeight = 20.0
          Body = StructureBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
          Behavior = StealthUpdate Stealth01
            StealthDelay = 0
            InnateStealth = Yes
          End
        End
        """;

    private static (SimulationWorld World, Feeder Feeder) StealthGarrisonWorld()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: FireFromContainerFiles(StealthGarrisonIni));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        return (world, new Feeder(world));
    }

    private static Entity SgSpawn(SimulationWorld world, Feeder feeder, string template, int owner, int x, int y)
    {
        feeder.Spawn(0, template, owner: owner, x: x, y: y);
        world.Step();
        return world.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).Last();
    }

    private static Entity SgInside(SimulationWorld world, Feeder feeder, string template, int owner, Entity container)
    {
        var rider = SgSpawn(world, feeder, template, owner, (int)container.X.Floor(), (int)container.Y.Floor());
        Check.That(world.TryEnterContainer(rider, container), $"Setup: {template} of player {owner} enters {container.Template.Name}.");
        return rider;
    }

    /// <summary>
    /// Step 2: <c>OpenContain::m_stealthUnitsContained</c> counts the riders of kind <c>STEALTH_GARRISON</c>, whatever their stealth state; a rider that leaves or dies inside no longer counts.
    /// </summary>
    public static void StealthGarrisonCount()
    {
        var (world, feeder) = StealthGarrisonWorld();
        var bunker = SgSpawn(world, feeder, "FcBunker", 1, 0, 0);
        var first = SgInside(world, feeder, "FcSneak", 1, bunker);
        var second = SgInside(world, feeder, "FcSneak", 1, bunker);
        SgInside(world, feeder, "FcRifleman", 1, bunker);
        var garrison = bunker.Container!;
        Check.That(garrison.StealthUnitsContained(world) == 2 && garrison.ContainCount == 3, $"Two of three riders are of kind STEALTH_GARRISON ({garrison.StealthUnitsContained(world)} of {garrison.ContainCount}).");
        feeder.ExitContainer(1, first.Id.Value);
        world.Step();
        Check.That(garrison.StealthUnitsContained(world) == 1, "One left: one counts.");
        feeder.Kill(second.Id.Value);
        for (var step = 0; step < world.Frames(2); step++) world.Step();
        Check.That(garrison.StealthUnitsContained(world) == 0 && garrison.ContainCount == 1, "One died inside: none counts.");
    }


}

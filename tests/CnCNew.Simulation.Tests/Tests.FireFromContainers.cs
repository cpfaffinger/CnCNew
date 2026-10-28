using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-60, riders fire from inside a container (docs/FIRE-FROM-CONTAINERS.md): the typed keys, <c>isPassengerAllowedToFire</c> per container, the garrisoned weapon bonus and the
/// acquisition of a contained rider.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> FireFromContainerFiles(string extra = "") => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/GameData.ini"] = """
        GameData
          MaxTunnelCapacity = 4
          WeaponBonus = GARRISONED RANGE 133%
          WeaponBonus = GARRISONED DAMAGE 125%
        End
        """,
        ["Data/INI/Object/FireFromContainerFixture.ini"] = """
        Locomotor FcFeet
          Surfaces = GROUND
          Speed = 30
          Acceleration = 900
          Braking = 900
          TurnRate = 3600
        End
        Weapon FcRifle
          PrimaryDamage = 10
          AttackRange = 100.0
          DamageType = SMALL_ARMS
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Weapon FcGattlingGun
          PrimaryDamage = 5
          AttackRange = 150.0
          DamageType = GATTLING
          ClipSize = 0
          DelayBetweenShots = 100
        End

        Object FcRifleman
          KindOf = SELECTABLE INFANTRY
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
            MoodAttackCheckRate = 250
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object FcJeep
          KindOf = SELECTABLE VEHICLE
          TransportSlotCount = 3
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
        End
        Object FcTarget
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
        End
        Object FcBunker
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 30.0
          GeometryMinorRadius = 30.0
          GeometryHeight = 20.0
          VisionRange = 300.0
          Body = StructureBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 5
          End
        End
        Object FcFireBase
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 30.0
          GeometryMinorRadius = 30.0
          GeometryHeight = 20.0
          VisionRange = 300.0
          Body = StructureBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 2
            IsEnclosingContainer = No
          End
        End
        Object FcHumvee
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          TransportSlotCount = 3
          VisionRange = 300.0
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = TransportAIUpdate AI01
          End
          Behavior = TransportContain Transport01
            Slots = 2
            PassengersAllowedToFire = Yes
          End
        End
        Object FcTruck
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          VisionRange = 300.0
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = TransportAIUpdate AI01
          End
          Behavior = TransportContain Transport01
            Slots = 2
          End
        End
        Object FcCarrier
          KindOf = SELECTABLE VEHICLE TRANSPORT
          VisionRange = 300.0
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = TransportContain Transport01
            Slots = 9
            AllowInsideKindOf = VEHICLE
          End
        End
        Object FcTunnel
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          VisionRange = 300.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = TunnelContain Tunnel01
            TimeForFullHeal = 1000
          End
        End
        Object FcTunnelOpen
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = TunnelContain Tunnel01
            PassengersAllowedToFire = Yes
          End
        End
        Object FcOverlord
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 15.0
          GeometryHeight = 10.0
          TransportSlotCount = 3
          VisionRange = 300.0
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = OverlordContain Overlord01
            Slots = 1
            AllowInsideKindOf = PORTABLE_STRUCTURE
            PassengersAllowedToFire = Yes
          End
        End
        Object FcGattling
          KindOf = PORTABLE_STRUCTURE
          TransportSlotCount = 1
          VisionRange = 200.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY FcGattlingGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
          End
        End
        Object FcBattleBunker
          KindOf = PORTABLE_STRUCTURE
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = TransportContain Transport01
            Slots = 2
          End
        End
        Object FcHelix
          KindOf = SELECTABLE VEHICLE TRANSPORT
          TransportSlotCount = 3
          VisionRange = 300.0
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = HelixContain Helix01
            Slots = 2
            PassengersAllowedToFire = No
          End
        End
        Object FcSpectre
          KindOf = SELECTABLE VEHICLE TRANSPORT
          VisionRange = 300.0
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = HelixContain Helix01
            Slots = 2
            PassengersAllowedToFire = Yes
          End
        End
        """ + "\n" + extra,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) FireFromContainerWorld(SimulationRate rate)
    {
        var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        return (world, new Feeder(world), fixture);
    }

    private static Entity FcSpawn(SimulationWorld world, Feeder feeder, string template, int owner, int x, int y)
    {
        feeder.Spawn(owner, template, owner: owner, x: x, y: y);
        world.Step();
        return world.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).Last();
    }

    /// <summary>
    /// Step 0: <c>PassengersAllowedToFire</c> of a <c>TransportContain</c>, a <c>HelixContain</c> and a <c>TunnelContain</c> and <c>IsEnclosingContainer</c> of a <c>GarrisonContain</c>
    /// are typed: the data's value reaches the module, and a value that is no Yes or No is refused instead of being kept as a compatibility value.
    /// </summary>
    public static void FireFromContainersKeysAreTyped()
    {
        var (world, feeder, _) = FireFromContainerWorld(TestRate.Default);
        var humvee = FcSpawn(world, feeder, "FcHumvee", 1, 0, 0);
        var truck = FcSpawn(world, feeder, "FcTruck", 1, 100, 0);
        var helix = FcSpawn(world, feeder, "FcHelix", 1, 200, 0);
        var spectre = FcSpawn(world, feeder, "FcSpectre", 1, 300, 0);
        var bunker = FcSpawn(world, feeder, "FcBunker", 1, 400, 0);
        var fireBase = FcSpawn(world, feeder, "FcFireBase", 1, 500, 0);
        Check.That(humvee.Modules.OfType<TransportContain>().Single().PassengersMayFire, "PassengersAllowedToFire = Yes reaches a plain TransportContain.");
        Check.That(!truck.Modules.OfType<TransportContain>().Single().PassengersMayFire, "Without the key a transport's riders may not fire.");
        Check.That(!helix.Modules.OfType<HelixContain>().Single().PassengersMayFire && spectre.Modules.OfType<HelixContain>().Single().PassengersMayFire,
            "A HelixContain reads the key: No on the Helix, Yes on the Spectre.");
        Check.That(bunker.Modules.OfType<GarrisonContain>().Single().IsEnclosing && !fireBase.Modules.OfType<GarrisonContain>().Single().IsEnclosing,
            "IsEnclosingContainer is yes by default and No on a fire base.");

        string Bad(string module, string key) => $"Object FcBad\n  KindOf = STRUCTURE\n  Body = StructureBody Body01\n    MaxHealth = 10.0\n    InitialHealth = 10.0\n  End\n  Behavior = {module} Module01\n    {key} = Maybe\n  End\nEnd\n";
        foreach (var (module, key) in new[] { ("TransportContain", "PassengersAllowedToFire"), ("HelixContain", "PassengersAllowedToFire"), ("TunnelContain", "PassengersAllowedToFire"), ("GarrisonContain", "IsEnclosingContainer") })
            Check.Throws<Exception>(() => _ = new SyntheticFixture(TestRate.Default, files: FireFromContainerFiles(Bad(module, key))).Templates, "Maybe");
    }
}

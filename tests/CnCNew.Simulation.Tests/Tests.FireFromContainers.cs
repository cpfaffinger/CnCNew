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

    private static Entity FcInside(SimulationWorld world, Feeder feeder, string template, int owner, Entity container)
    {
        var rider = FcSpawn(world, feeder, template, owner, (int)container.X.Floor(), (int)container.Y.Floor());
        Check.That(world.TryEnterContainer(rider, container), $"Setup: {template} enters {container.Template.Name}.");
        return rider;
    }

    /// <summary>
    /// Step 2: <c>isPassengerAllowedToFire</c> per container (docs/FIRE-FROM-CONTAINERS.md §1.1) - a garrison always; a transport for infantry and only with the flag, which the
    /// upgrade switches on; a tunnel by its flag; an Overlord for infantry and its portable structure, never while it is itself contained; the battle bunker's riders by the Overlord;
    /// a Helix for its portable structure always and for infantry by the flag, never while contained; a transport inside another container by that container.
    /// </summary>
    public static void FireFromContainersPassengerRules()
    {
        var (world, feeder, _) = FireFromContainerWorld(TestRate.Default);
        FcRules Of(Entity container) => new FcRules(world, container);

        var bunker = FcSpawn(world, feeder, "FcBunker", 1, 0, 0);
        var garrisoned = FcInside(world, feeder, "FcRifleman", 1, bunker);
        Check.That(world.ContainerLetsFire(garrisoned) && Of(bunker).Allows(null), "A garrison lets its riders fire, whatever asks.");

        var humvee = FcSpawn(world, feeder, "FcHumvee", 1, 100, 0);
        var jeep = FcSpawn(world, feeder, "FcJeep", 1, 100, 50);
        var humveeRider = FcInside(world, feeder, "FcRifleman", 1, humvee);
        Check.That(world.ContainerLetsFire(humveeRider), "A transport with PassengersAllowedToFire lets its infantry fire.");
        Check.That(!Of(humvee).Allows(jeep), "No vehicle fires from a transport.");
        var truck = FcSpawn(world, feeder, "FcTruck", 1, 200, 0);
        var truckRider = FcInside(world, feeder, "FcRifleman", 1, truck);
        Check.That(!world.ContainerLetsFire(truckRider), "A transport without the flag does not.");
        truck.Modules.OfType<TransportContain>().Single().PassengersAllowedToFire = true;
        Check.That(world.ContainerLetsFire(truckRider), "PassengersFireUpgrade's flag lets them.");
        Check.That(!world.ContainerLetsFire(jeep), "An object in no container is not let fire by one.");

        var tunnel = FcSpawn(world, feeder, "FcTunnel", 1, 300, 0);
        var openTunnel = FcSpawn(world, feeder, "FcTunnelOpen", 1, 400, 0);
        for (var step = 0; step < world.Frames(2); step++) world.Step();
        var tunnelRider = FcInside(world, feeder, "FcRifleman", 1, tunnel);
        Check.That(!world.ContainerLetsFire(tunnelRider), "A tunnel's riders do not fire.");
        Check.That(Of(openTunnel).Allows(tunnelRider), "A tunnel with the flag would let them (no shipped tunnel has it).");

        var overlord = FcSpawn(world, feeder, "FcOverlord", 1, 500, 0);
        var gattling = FcInside(world, feeder, "FcGattling", 1, overlord);
        Check.That(world.ContainerLetsFire(gattling), "The Overlord's portable structure fires.");
        Check.That(Of(overlord).Allows(humveeRider) && !Of(overlord).Allows(jeep), "An Overlord lets infantry fire and no vehicle.");
        var carrier = FcSpawn(world, feeder, "FcCarrier", 1, 600, 0);
        Check.That(world.TryEnterContainer(overlord, carrier), "Setup: the Overlord rides in a carrier.");
        Check.That(!world.ContainerLetsFire(gattling), "Nested containment voids an Overlord's firing.");

        var bunkerOverlord = FcSpawn(world, feeder, "FcOverlord", 1, 700, 0);
        var battleBunker = FcInside(world, feeder, "FcBattleBunker", 1, bunkerOverlord);
        var bunkerRider = FcInside(world, feeder, "FcRifleman", 1, bunkerOverlord);
        Check.That(battleBunker.Container!.Contained.Contains(bunkerRider.Id), "Setup: the rider sits in the battle bunker on the Overlord's back.");
        Check.That(world.ContainerLetsFire(bunkerRider) && Of(battleBunker).Allows(bunkerRider), "The battle bunker's rider fires: the bunker (without the flag) asks the Overlord.");

        var helix = FcSpawn(world, feeder, "FcHelix", 1, 800, 0);
        var helixGun = FcInside(world, feeder, "FcGattling", 1, helix);
        var helixRider = FcInside(world, feeder, "FcRifleman", 1, helix);
        Check.That(world.ContainerLetsFire(helixGun), "A Helix's portable structure always fires.");
        Check.That(!world.ContainerLetsFire(helixRider) && !Of(helix).Allows(jeep) && !Of(helix).Allows(null), "Its infantry needs the flag; a vehicle and a question without a rider are refused.");
        helix.Modules.OfType<HelixContain>().Single().PassengersAllowedToFire = true;
        Check.That(world.ContainerLetsFire(helixRider), "After the upgrade the Helix's infantry fires.");
        var spectre = FcSpawn(world, feeder, "FcSpectre", 1, 900, 0);
        Check.That(Of(spectre).Allows(helixRider), "A Helix with PassengersAllowedToFire = Yes lets infantry fire from the start.");
        var carrier2 = FcSpawn(world, feeder, "FcCarrier", 1, 1000, 0);
        Check.That(world.TryEnterContainer(helix, carrier2), "Setup: the Helix rides in a carrier.");
        Check.That(!world.ContainerLetsFire(helixGun) && !world.ContainerLetsFire(helixRider), "Nested containment voids a Helix's firing, also for its portable structure.");

        var carrier3 = FcSpawn(world, feeder, "FcCarrier", 1, 1100, 0);
        Check.That(world.TryEnterContainer(humvee, carrier3), "Setup: the Humvee rides in a carrier.");
        Check.That(!world.ContainerLetsFire(humveeRider), "A transport inside a container without the flag passes the no on.");
        carrier3.Modules.OfType<TransportContain>().Single().PassengersAllowedToFire = true;
        Check.That(world.ContainerLetsFire(humveeRider), "Inside one with the flag its infantry fires.");
    }

    private readonly record struct FcRules(SimulationWorld World, Entity Container)
    {
        public bool Allows(Entity? rider) => Container.Container!.IsPassengerAllowedToFire(World, rider);
    }
}

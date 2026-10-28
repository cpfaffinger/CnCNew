using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
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
          WeaponBonus = VETERAN RANGE 120%
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
        Object FcBus
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
            WeaponBonusPassedToPassengers = Yes
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

    /// <summary>
    /// Step 3: <c>WEAPONBONUSCONDITION_GARRISONED</c> is set on a rider of a <c>GarrisonContain</c> or a <c>HelixContain</c> (its infantry and its portable structure) while it is inside and
    /// cleared when it leaves, so GameData's <c>GARRISONED RANGE 133%</c> and <c>DAMAGE 125%</c> apply; a <c>TransportContain</c> gives none.
    /// </summary>
    public static void FireFromContainersGarrisonedBonus()
    {
        var (world, feeder, fixture) = FireFromContainerWorld(TestRate.Default);
        var garrisoned = CnCNew.Data.Definitions.NameTablesPhase5.WeaponBonusCondition.TryIndexOf("GARRISONED", out var bit) ? 1u << bit : 0u;
        Check.That(fixture.Templates.Weapons.TryGet("FcRifle", out var rifle), "Setup: the rifle.");
        static bool Near(Fixed value, decimal expected) => value - Fixed.FromDecimal(expected) < Fixed.FromDecimal(0.01m) && Fixed.FromDecimal(expected) - value < Fixed.FromDecimal(0.01m);
        Fixed Damage(Entity rider) => rifle!.DamageWith(fixture.Templates.GlobalWeaponBonus, rider.WeaponBonusConditions, rifle.PrimaryDamage);

        var bunker = FcSpawn(world, feeder, "FcBunker", 1, 0, 0);
        var rifleman = FcInside(world, feeder, "FcRifleman", 1, bunker);
        Check.That((rifleman.WeaponBonusConditions & garrisoned) != 0, "A garrisoned rider has the GARRISONED condition.");
        Check.That(Near(world.MaxWeaponRange(rifleman), 133m) && Near(Damage(rifleman), 12.5m),
            $"Its rifle reaches 133 % and hurts 125 % ({world.MaxWeaponRange(rifleman)}, {Damage(rifleman)}).");
        world.Step();
        feeder.ExitContainer(1, rifleman.Id.Value);
        for (var step = 0; step < world.Frames(2); step++) world.Step();
        Check.That(!rifleman.ContainedBy.IsValid && (rifleman.WeaponBonusConditions & garrisoned) == 0 && world.MaxWeaponRange(rifleman) == Fixed.FromInt(100),
            "Out of the building the condition is gone and the rifle reaches 100 again.");

        var helix = FcSpawn(world, feeder, "FcHelix", 1, 300, 0);
        var gattling = FcInside(world, feeder, "FcGattling", 1, helix);
        var helixRider = FcInside(world, feeder, "FcRifleman", 1, helix);
        Check.That((gattling.WeaponBonusConditions & garrisoned) != 0 && (helixRider.WeaponBonusConditions & garrisoned) != 0 && Near(world.MaxWeaponRange(gattling), 199.5m),
            $"A Helix's portable structure and its infantry are garrisoned too (the gattling reaches {world.MaxWeaponRange(gattling)}).");
        helix.Container!.Remove(world, helixRider, true);
        helix.Modules.OfType<HelixContain>().Single().Remove(world, gattling, true);
        Check.That((gattling.WeaponBonusConditions & garrisoned) == 0 && (helixRider.WeaponBonusConditions & garrisoned) == 0, "Both lose it when they are taken out.");

        var humvee = FcSpawn(world, feeder, "FcHumvee", 1, 600, 0);
        var humveeRider = FcInside(world, feeder, "FcRifleman", 1, humvee);
        Check.That((humveeRider.WeaponBonusConditions & garrisoned) == 0 && world.MaxWeaponRange(humveeRider) == Fixed.FromInt(100), "A transport's rider gets no garrisoned bonus.");
    }

    private const string FcRunnerIni = """
        Object FcRunner
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    /// <summary>
    /// One container scene: <paramref name="container"/> of player 1 at the origin, <paramref name="rider"/> of player 1 put inside it, and an enemy <paramref name="victim"/> of player 2
    /// at (<paramref name="distance"/>, 0) - all made in one tick, the rider inside before the next one (an enemy beyond 100 cannot be fired at from outside in between).
    /// </summary>
    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, Entity Container, Entity Rider, Entity Victim, TemplateCatalog Templates, string Fingerprint) FcScene(
        SimulationRate rate, string container, string rider, int distance, string victim = "FcTarget", bool computer = false, ModelGeometry? geometry = null, Fixed containerAngle = default)
    {
        var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(FcRunnerIni));
        var templates = geometry is null ? fixture.Templates : TemplateCatalog.Build(fixture.Graph, fixture.Bindings, rate, geometry);
        var fingerprint = (geometry ?? ModelGeometry.None).Combine(fixture.Fingerprint);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, computer ? ComputerAgainstHuman : CombatPlayers, SyntheticFixture.FixtureId, fingerprint), templates);
        var feeder = new Feeder(world);
        feeder.Spawn(1, container, owner: 1, x: 0, y: 0, angle: containerAngle);
        feeder.Spawn(1, rider, owner: 1, x: 0, y: 0);
        feeder.Spawn(2, victim, owner: 2, x: distance, y: 0);
        world.Step();
        var box = world.Entities.Single(value => value.Template.Name == container);
        var inside = world.Entities.Single(value => value.Template.Name == rider);
        Check.That(world.TryEnterContainer(inside, box), $"Setup: {rider} enters {container}.");
        return (world, feeder, fixture, box, inside, world.Entities.Single(value => value.Template.Name == victim), templates, fingerprint);
    }

    private static BonePose FcPose(int x, int y, int z) => new(Fixed.FromInt(x), Fixed.FromInt(y), Fixed.FromInt(z), Fixed.Zero);

    /// <summary>
    /// The bones a model would give (step 1): the bunker has two fire points, east and west of its centre on its upper floor, in each of its three sets; the fire base one station
    /// to the east, raised.
    /// </summary>
    private static ModelGeometry FcFirePoints() => new([], [],
    [
        new KeyValuePair<string, ContainerFirePoints>("FcBunker", new ContainerFirePoints([.. Enumerable.Repeat(ImmutableArray.Create(FcPose(30, 0, 10), FcPose(-30, 0, 10)), 3)], [], [])),
        new KeyValuePair<string, ContainerFirePoints>("FcFireBase", new ContainerFirePoints([], [FcPose(20, 0, 15)], [])),
    ]);

    /// <summary>Steps <paramref name="frames"/> original frames; the shots of <paramref name="source"/> and the tick of the first one (-1 for none).</summary>
    private static (int Shots, long First) FcRun(SimulationWorld world, Entity source, int frames)
    {
        var shots = 0;
        var first = -1L;
        for (var step = 0; step < world.Frames(frames); step++)
        {
            var result = world.Step();
            var now = ShotsOf(result, source);
            if (now > 0 && first < 0) first = result.Tick;
            shots += now;
        }
        return (shots, first);
    }

    /// <summary>
    /// Step 5 (the behaviour change): a rifleman garrisoned in a building picks an enemy in range and fires (no shot before F-60), while it stays inside and held; it reaches an enemy
    /// beyond its plain range but within its garrisoned range (133 %) from the building's bounding circle (the stand-in for the fire points), and nothing beyond that; a fire base
    /// (not enclosing) measures from its centre; a computer player's rider never walks after a victim that leaves and drops it; a rider that leaves ends its attack.
    /// </summary>
    public static void FireFromContainersGarrisonFires()
    {
        var near = FcScene(TestRate.Default, "FcBunker", "FcRifleman", 80);
        var (shots, first) = FcRun(near.World, near.Rider, 60);
        Check.That(shots > 0, "A garrisoned rifleman fires at an enemy in range.");
        var firstFrame = near.World.InFrames(first - near.Rider.SpawnTick);
        Check.That(firstFrame is >= 1 and <= 4, $"It looks two frames after it became idle and fires at once (first shot {firstFrame} frames after its creation).");
        Check.That(near.Rider.ContainedBy == near.Container.Id && (near.World.DisabledFlagsOf(near.Rider) & DisabledMask.Held) != 0, "It fires from inside, held by the building.");
        Check.That(Hurt(near.Victim) > Fixed.FromInt(12 * shots) && Hurt(near.Victim) < Fixed.FromInt(13 * shots),
            $"Its shots carry the garrisoned damage, 125 % of 10 ({Hurt(near.Victim)} from {shots} shots).");

        var beyond = FcScene(TestRate.Default, "FcBunker", "FcRifleman", 150);
        Check.That(FcRun(beyond.World, beyond.Rider, 60).Shots == 0 && !beyond.World.AcquiredVictimOf(beyond.Rider).IsValid,
            "A building whose model has no fire points has its 40 points at its centre: an enemy 150 away is beyond 133.");
        var station = FcScene(TestRate.Default, "FcFireBase", "FcRifleman", 120);
        Check.That(FcRun(station.World, station.Rider, 60).Shots > 0, "Within 133 of the centre of a fire base without stations its rider fires.");

        var computer = FcScene(TestRate.Default, "FcBunker", "FcRifleman", 80, "FcRunner", computer: true);
        Check.That(FcRun(computer.World, computer.Rider, 30).Shots > 0, "Setup: the computer player's rider fires at the runner.");
        computer.Feeder.SetMoveTarget(2, computer.Victim.Id.Value, 600, 0);
        FcRun(computer.World, computer.Rider, 150);
        Check.That(computer.Rider.ContainedBy == computer.Container.Id && !computer.Rider.HasMoveTarget && !computer.World.AcquiredVictimOf(computer.Rider).IsValid,
            "When its victim runs out of range a computer player's rider stays inside and drops it.");

        Check.That(near.World.AcquiredVictimOf(near.Rider) == near.Victim.Id, "Setup: the rifleman still attacks.");
        near.Feeder.ExitContainer(1, near.Rider.Id.Value);
        near.World.Step();
        Check.That(!near.Rider.ContainedBy.IsValid && !near.World.AcquiredVictimOf(near.Rider).IsValid, "Leaving the building ends its attack.");
    }

    /// <summary>
    /// Step 6, orders through the container (<c>AIGroup::groupAttackObjectPrivate</c>): an attack order on a garrisoned building is passed to its rider, which leaves the nearer enemy
    /// it picked itself for the ordered one and fires at it; an order on an enemy out of every rider's reach is passed on and fails, the rider cannot approach; an empty building cannot take the order. At 30, 60 and
    /// 120 Hz.
    /// </summary>
    public static void FireFromContainersOrdersThroughTheContainer()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var scene = FcScene(rate, "FcBunker", "FcRifleman", 60);
            scene.Feeder.Spawn(2, "FcTarget", owner: 2, x: -100, y: 0);
            scene.Feeder.Spawn(2, "FcTarget", owner: 2, x: 0, y: 300);
            scene.World.Step();
            var far = scene.World.Entities.Single(value => value.Template.Name == "FcTarget" && value.X == Fixed.FromInt(-100));
            var outOfReach = scene.World.Entities.Single(value => value.Template.Name == "FcTarget" && value.Y == Fixed.FromInt(300));
            FcRun(scene.World, scene.Rider, 30);
            Check.That(scene.World.AcquiredVictimOf(scene.Rider) == scene.Victim.Id, $"{rate}: setup - the rider attacks the nearer enemy it picked itself.");

            Check.That(scene.Feeder.Pursue(1, scene.Container.Id.Value, outOfReach.Id.Value, 0, 0).Accepted, $"{rate}: a garrisoned building takes an attack order.");
            // "Possible after moving" passes too; a rider cannot move, so the attack ends and the rider looks again.
            FcRun(scene.World, scene.Rider, 30);
            Check.That(Hurt(outOfReach) == Fixed.Zero && scene.World.AcquiredVictimOf(scene.Rider) != outOfReach.Id, $"{rate}: an enemy out of the rider's reach is not fired at.");

            var before = Hurt(far);
            Check.That(scene.Feeder.Pursue(1, scene.Container.Id.Value, far.Id.Value, 0, 0).Accepted, $"{rate}: the order on the farther enemy is taken.");
            scene.World.Step();
            Check.That(scene.World.AcquiredVictimOf(scene.Rider) == far.Id, $"{rate}: the rider attacks the ordered enemy.");
            Check.That(FcRun(scene.World, scene.Rider, 60).Shots > 0 && Hurt(far) > before && scene.Rider.ContainedBy == scene.Container.Id,
                $"{rate}: it fires at it from inside.");

            var empty = FcScene(rate, "FcBunker", "FcRifleman", 60);
            empty.Feeder.ExitContainer(1, empty.Rider.Id.Value);
            for (var step = 0; step < empty.World.Frames(30) && empty.Rider.ContainedBy.IsValid; step++) empty.World.Step();
            Check.That(empty.Feeder.Pursue(1, empty.Container.Id.Value, empty.Victim.Id.Value, 0, 0).Reason == CommandRejection.CannotMove, $"{rate}: an empty building cannot take an attack order.");
        }
    }

    /// <summary>
    /// Step 5: a Humvee-like transport with <c>PassengersAllowedToFire</c> lets its rifleman fire; a transport without the flag and a tunnel do not; an Overlord's portable structure
    /// (a gattling cannon) fires from its back.
    /// </summary>
    public static void FireFromContainersTransportsFire()
    {
        var humvee = FcScene(TestRate.Default, "FcHumvee", "FcRifleman", 80);
        Check.That(FcRun(humvee.World, humvee.Rider, 60).Shots > 0, "A rifleman in a Humvee fires.");
        var truck = FcScene(TestRate.Default, "FcTruck", "FcRifleman", 80);
        Check.That(FcRun(truck.World, truck.Rider, 60).Shots == 0, "A rifleman in a transport without the flag does not.");
        var overlord = FcScene(TestRate.Default, "FcOverlord", "FcGattling", 120);
        Check.That(FcRun(overlord.World, overlord.Rider, 60).Shots > 0, "The Overlord's gattling cannon fires.");

        var fixture = new SyntheticFixture(TestRate.Default, files: FireFromContainerFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        var tunnel = FcSpawn(world, feeder, "FcTunnel", 1, 0, 0);
        for (var step = 0; step < world.Frames(2); step++) world.Step();
        var rider = FcInside(world, feeder, "FcRifleman", 1, tunnel);
        FcSpawn(world, feeder, "FcTarget", 2, 60, 0);
        Check.That(FcRun(world, rider, 60).Shots == 0, "A rifleman in a tunnel does not fire.");
    }

    /// <summary>
    /// Step 3, the passed bonus (<c>Weapon::computeBonus</c>, <c>WeaponBonusPassedToPassengers</c>): a veteran bus passes its <c>VETERAN</c> condition to its rider - range 120 instead of 100 -
    /// and the rider fires at an enemy 115 away; a veteran Humvee passes nothing. The rider's own conditions stay its own.
    /// </summary>
    public static void FireFromContainersPassedBonus()
    {
        var bus = FcScene(TestRate.Default, "FcBus", "FcRifleman", 115);
        Check.That(bus.World.MaxWeaponRange(bus.Rider) == Fixed.FromInt(100), $"Setup: a rider of a bus without a bonus has range 100 ({bus.World.MaxWeaponRange(bus.Rider)}).");
        bus.World.SetVeterancy(bus.Container.Id, 1);
        Check.That((bus.World.MaxWeaponRange(bus.Rider) - Fixed.FromInt(120)).Abs() < Fixed.FromDecimal(0.001m) && bus.World.WeaponBonusOf(bus.Rider) != bus.Rider.WeaponBonusConditions,
            $"A veteran bus passes VETERAN to its rider: range 120 ({bus.World.MaxWeaponRange(bus.Rider)}), its own conditions unchanged.");
        Check.That(FcRun(bus.World, bus.Rider, 60).Shots > 0, "The rider fires at an enemy 115 away.");
        var humvee = FcScene(TestRate.Default, "FcHumvee", "FcRifleman", 115);
        humvee.World.SetVeterancy(humvee.Container.Id, 1);
        Check.That(humvee.World.MaxWeaponRange(humvee.Rider) == Fixed.FromInt(100) && FcRun(humvee.World, humvee.Rider, 60).Shots == 0, "A veteran Humvee passes nothing: 115 is out of reach.");
    }

    /// <summary>
    /// Step 4, the match on entry (<c>OpenContain::addToContain</c> -> <c>GarrisonContain::redeployOccupants</c> -> <c>matchObjectsToGarrisonPoints</c>): at 60 Hz a rider at a point
    /// whose victim died in the middle of the building's frame goes back to the centre at once when another rider comes in, not at the building's next frame.
    /// </summary>
    public static void FireFromContainersMatchOnEntry()
    {
        var scene = FcScene(SimulationRate.Hz60, "FcBunker", "FcRifleman", 150, geometry: FcFirePoints());
        FcRun(scene.World, scene.Rider, 60);
        Check.That(scene.Rider.X == Fixed.FromInt(30), $"Setup: the rider fires from the east point ({scene.Rider.X}).");
        scene.Feeder.Spawn(1, "FcRifleman", owner: 1, x: 0, y: 0);
        scene.World.Step();
        var second = scene.World.Entities.Where(value => value.Template.Name == "FcRifleman").OrderBy(value => value.Id.Value).Last();
        var ticks = scene.World.Time.TicksPerOriginalFrame;
        while ((scene.World.Tick - scene.Container.SpawnTick) % ticks != 1) scene.World.Step();
        scene.Feeder.Kill(scene.Victim.Id.Value);
        scene.World.Step();
        Check.That((scene.World.Tick - 1 - scene.Container.SpawnTick) % ticks != 0 && scene.Victim.EffectivelyDead && scene.Rider.X == Fixed.FromInt(30),
            $"Setup: the victim died in the middle of the building's frame, the rider is still at its point ({scene.Rider.X}; tick {scene.World.Tick}, spawn {scene.Container.SpawnTick}, dead {scene.Victim.EffectivelyDead}).");
        Check.That(scene.World.TryEnterContainer(second, scene.Container), "Setup: a second rider comes in.");
        Check.That((scene.Rider.X, scene.Rider.Y) == (scene.Container.X, scene.Container.Y), $"The first rider is back at the centre at once ({scene.Rider.X}, {scene.Rider.Y}).");
    }

    /// <summary>
    /// Step 4, a transport's fire points (<c>OpenContain::redeployOccupants</c>, <c>putObjAtNextFirePoint</c>): the Humvee's model has one point 40 to its east and one 40 to its west; its
    /// first rider stands at the first. It fires at an enemy 130 to the east (90 from its point, within 100) and not at one 130 to the west (170 from its point); without points it is
    /// measured from the Humvee and fires at neither. Turned round, the first point is in the west.
    /// </summary>
    public static void FireFromContainersTransportFirePoints()
    {
        static ModelGeometry Points() => new([], [],
        [
            new KeyValuePair<string, ContainerFirePoints>("FcHumvee", new ContainerFirePoints([], [], [FcPose(40, 0, 5), FcPose(-40, 0, 5)])),
        ]);

        var east = FcScene(TestRate.Default, "FcHumvee", "FcRifleman", 130, geometry: Points());
        Check.That(FcRun(east.World, east.Rider, 60).Shots > 0, "The Humvee's first rider fires from the east point at an enemy 130 to the east.");
        var plain = FcScene(TestRate.Default, "FcHumvee", "FcRifleman", 130);
        Check.That(FcRun(plain.World, plain.Rider, 60).Shots == 0, "Without fire points it is measured from the Humvee: 130 is beyond 100.");
        var west = FcScene(TestRate.Default, "FcHumvee", "FcRifleman", -130, geometry: Points());
        Check.That(FcRun(west.World, west.Rider, 60).Shots == 0, "An enemy 130 to the west is 170 from the east point.");

        var turned = FcScene(TestRate.Default, "FcHumvee", "FcRifleman", -130, geometry: Points(), containerAngle: FixedTrig.Pi);
        Check.That(FcRun(turned.World, turned.Rider, 60).Shots > 0, "Turned round, the first point lies in the west and the enemy there is fired at.");
    }

    /// <summary>
    /// Step 4, the fire points (<c>GarrisonContain</c>, docs/FIRE-FROM-CONTAINERS.md): a rider without a victim stands at the building's centre; it acquires an enemy within range of the
    /// free point closest to it (<c>calcBestGarrisonPosition</c>) and fires from that point; when its victim is gone it goes back to the centre and takes the point on the side of its
    /// next one; a fire base's rider stands at its station, raised, and is put back on the ground when it leaves; a snapshot keeps the points.
    /// </summary>
    public static void FireFromContainersFirePoints()
    {
        var scene = FcScene(TestRate.Default, "FcBunker", "FcRifleman", 150, geometry: FcFirePoints());
        var centre = (scene.Container.X, scene.Container.Y);
        Check.That((scene.Rider.X, scene.Rider.Y) == centre, "A rider that has nobody to fire at stands at the building's centre.");
        var (shots, _) = FcRun(scene.World, scene.Rider, 60);
        Check.That(shots > 0, "An enemy 150 away - beyond 133 from the centre, within 133 of the east fire point 30 out - is fired at.");
        Check.That(scene.Rider.X == Fixed.FromInt(30) && scene.Rider.Y == Fixed.Zero && scene.Rider.Z == scene.Container.Z + Fixed.FromInt(10),
            $"The rider fires from the east fire point, on the upper floor ({scene.Rider.X}, {scene.Rider.Y}, {scene.Rider.Z}).");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(scene.World), scene.Templates, scene.Fingerprint);
        Check.That(restored.StateHash == scene.World.StateHash, "A world with a rider at a fire point restores to the same hash.");
        for (var step = 0; step < scene.World.Frames(10); step++) Check.That(scene.World.Step().StateHash == restored.Step().StateHash, $"Step {step} after the restore has the same hash.");

        scene.Feeder.Kill(scene.Victim.Id.Value);
        scene.Feeder.Spawn(2, "FcTarget", owner: 2, x: -160, y: 0);
        var (westShots, _) = FcRun(scene.World, scene.Rider, 90);
        Check.That(westShots > 0 && scene.Rider.X == Fixed.FromInt(-30), $"Its next enemy, 160 to the west, it fires at from the west point ({westShots} shots, at {scene.Rider.X}).");
        var west = scene.World.Entities.Single(value => value.Template.Name == "FcTarget" && !value.EffectivelyDead && !value.DestroyRequested);
        scene.Feeder.Kill(west.Id.Value);
        FcRun(scene.World, scene.Rider, 10);
        Check.That((scene.Rider.X, scene.Rider.Y) == centre && !scene.World.AcquiredVictimOf(scene.Rider).IsValid, "With nobody left to fire at it goes back to the centre.");

        var far = FcScene(TestRate.Default, "FcBunker", "FcRifleman", 170, geometry: FcFirePoints());
        Check.That(FcRun(far.World, far.Rider, 60).Shots == 0, "An enemy 170 away is beyond 133 from every fire point.");

        var station = FcScene(TestRate.Default, "FcFireBase", "FcRifleman", 150, geometry: FcFirePoints());
        Check.That(station.Rider.X == Fixed.FromInt(20) && station.Rider.Z == station.Container.Z + Fixed.FromInt(15), $"The fire base's rider stands at its station ({station.Rider.X}, {station.Rider.Z}).");
        Check.That(FcRun(station.World, station.Rider, 60).Shots > 0, "From its station 20 out an enemy 150 away is within 133.");
        station.Feeder.ExitContainer(1, station.Rider.Id.Value);
        station.World.Step();
        Check.That(!station.Rider.ContainedBy.IsValid && station.Rider.Z == Fixed.Zero, $"Leaving, it is put on the ground again ({station.Rider.Z}).");
    }

    /// <summary>
    /// Step 5 across rates and a save: the same garrison fight (the enemy at the building's edge range) has its first shot in the same original frame, the same number of shots and the
    /// same damage at 30, 60 and 120 Hz; a snapshot in the middle of the fight continues to the same hashes and shots.
    /// </summary>
    public static void FireFromContainersRatesAndSnapshot()
    {
        var results = new List<(long Frame, int Shots, Fixed Hurt)>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var scene = FcScene(rate, "FcBunker", "FcRifleman", 150, geometry: FcFirePoints());
            var (shots, first) = FcRun(scene.World, scene.Rider, 90);
            Check.That(first >= 0, $"At {rate.TicksPerSecond} Hz the rider fires.");
            // Counted from the rider's creation: the scene is made in the first tick of every world, which lies at another game time at each rate.
            results.Add((scene.World.InFrames(first - scene.Rider.SpawnTick), shots, Hurt(scene.Victim)));
        }
        Check.That(results.All(value => value == results[0]),
            $"The first shot frame, the shots and the damage are the same at 30, 60 and 120 Hz ({string.Join("; ", results.Select(value => $"frame {value.Frame}, {value.Shots} shots, {value.Hurt}"))}).");

        var live = FcScene(TestRate.Default, "FcBunker", "FcRifleman", 80);
        Check.That(FcRun(live.World, live.Rider, 20).Shots > 0, "Setup: the fight is on.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(live.World), live.Fixture.Templates, live.Fixture.Fingerprint);
        Check.That(restored.StateHash == live.World.StateHash, "The garrison fight restores to the same hash.");
        var restoredRider = restored.FindEntity(live.Rider.Id)!;
        int liveShots = 0, restoredShots = 0;
        for (var step = 0; step < live.World.Frames(60); step++)
        {
            var a = live.World.Step();
            var b = restored.Step();
            Check.That(a.StateHash == b.StateHash, $"Step {step} after the restore has the same hash.");
            liveShots += ShotsOf(a, live.Rider);
            restoredShots += ShotsOf(b, restoredRider);
        }
        Check.That(liveShots > 0 && liveShots == restoredShots, $"Both go on firing alike ({liveShots} and {restoredShots} shots).");
    }
}

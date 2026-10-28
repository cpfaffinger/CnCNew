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
        Object FcEye
          KindOf = SELECTABLE VEHICLE
          VisionRange = 150.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = StealthDetectorUpdate Detect01
            DetectionRate = 100
            DetectionRange = 120.0
          End
        End
        Object FcInfantryEye
          KindOf = SELECTABLE VEHICLE
          VisionRange = 150.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = StealthDetectorUpdate Detect01
            DetectionRate = 100
            DetectionRange = 120.0
            ExtraRequiredKindOf = INFANTRY
          End
        End
        Object FcBoarder
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          TransportSlotCount = 1
          VisionRange = 150.0
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object FcSmallBunker
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 30.0
          GeometryMinorRadius = 30.0
          GeometryHeight = 20.0
          Body = StructureBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 1
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

    /// <summary>
    /// Step 3, the apparent owner and the acquisition (<c>PartitionFilterRejectBuildings</c>, <c>PartitionFilterStealthedAndUndetected</c>): player 1's stealth rider holds a neutral
    /// building, which is player 1's now. An idle rifleman of player 2 next to it does not take it for an enemy - its apparent owner is the neutral one; once the rider is
    /// detected it does. A building held by a plain rifleman it takes at once. A unit with <c>ATTACK_BUILDINGS</c> has no building filter and takes the hidden neutral building - as the
    /// original does -, but passes by a hidden building whose owner from before is its enemy.
    /// </summary>
    public static void StealthGarrisonHiddenFromAcquisition()
    {
        var (world, feeder) = StealthGarrisonWorld();
        var bunker = SgSpawn(world, feeder, "FcBunker", 0, 0, 0);
        var sneak = SgInside(world, feeder, "FcSneak", 1, bunker);
        var watcher = SgSpawn(world, feeder, "FcRifleman", 2, 80, 0);
        var garrison = (GarrisonContain)bunker.Container!;
        Check.That(bunker.Owner == new PlayerId(1) && garrison.HidesGarrison(world) && garrison.ApparentOwnerFor(world, new PlayerId(2)) == PlayerId.System
                   && garrison.ApparentOwnerFor(world, new PlayerId(1)) == new PlayerId(1), "The building is player 1's, hidden: player 2 sees the neutral owner, player 1 itself.");
        for (var step = 0; step < world.Frames(30); step++) world.Step();
        Check.That(!world.AcquiredVictimOf(watcher).IsValid, "Player 2's idle rifleman passes the hidden garrison by.");
        sneak.Modules.OfType<StealthUpdate>().Single().MarkDetected(world, world.Frames(300));
        Check.That(!garrison.HidesGarrison(world) && garrison.ApparentOwnerFor(world, new PlayerId(2)) == new PlayerId(1), "Detected, the building shows its owner.");
        for (var step = 0; step < world.Frames(30); step++) world.Step();
        Check.That(world.AcquiredVictimOf(watcher) == bunker.Id, "Then the rifleman takes it.");

        var (plainWorld, plainFeeder) = StealthGarrisonWorld();
        var plain = SgSpawn(plainWorld, plainFeeder, "FcBunker", 0, 0, 0);
        SgInside(plainWorld, plainFeeder, "FcRifleman", 1, plain);
        var plainWatcher = SgSpawn(plainWorld, plainFeeder, "FcRifleman", 2, 80, 0);
        for (var step = 0; step < plainWorld.Frames(30); step++) plainWorld.Step();
        Check.That(plainWorld.AcquiredVictimOf(plainWatcher) == plain.Id, "A building held by a plain rifleman is taken.");

        var (huntWorld, huntFeeder) = StealthGarrisonWorld();
        var neutral = SgSpawn(huntWorld, huntFeeder, "FcBunker", 0, 0, 0);
        SgInside(huntWorld, huntFeeder, "FcSneak", 1, neutral);
        var hunter = SgSpawn(huntWorld, huntFeeder, "FcHunter", 2, 80, 0);
        for (var step = 0; step < huntWorld.Frames(30); step++) huntWorld.Step();
        Check.That(huntWorld.AcquiredVictimOf(hunter) == neutral.Id, "A unit with ATTACK_BUILDINGS takes the hidden neutral building (its apparent owner is no enemy).");

        var (ownWorld, ownFeeder) = StealthGarrisonWorld();
        var own = SgSpawn(ownWorld, ownFeeder, "FcBunker", 1, 0, 0);
        SgInside(ownWorld, ownFeeder, "FcSneak", 1, own);
        var ownHunter = SgSpawn(ownWorld, ownFeeder, "FcHunter", 2, 80, 0);
        for (var step = 0; step < ownWorld.Frames(30); step++) ownWorld.Step();
        Check.That(!ownWorld.AcquiredVictimOf(ownHunter).IsValid, "It passes by player 1's own building held by its stealth rider: the apparent owner is its enemy and the riders are hidden.");
    }

    /// <summary>
    /// <c>ActionManager::canCaptureBuilding</c>'s stealth and garrison rules: a building garrisoned by an enemy's plain rider cannot be captured, a stealthed and undetected structure
    /// neither.
    /// </summary>
    public static void StealthGarrisonCapture()
    {
        var (world, feeder) = StealthGarrisonWorld();
        var actor = SgSpawn(world, feeder, "FcRifleman", 2, 200, 0);
        var held = SgSpawn(world, feeder, "FcBunker", 0, 0, 0);
        SgInside(world, feeder, "FcRifleman", 1, held);
        Check.That(!StagedCaptureRules.CanCapture(world, actor, held), "A building garrisoned by player 1's rifleman cannot be captured.");
        var cloaked = SgSpawn(world, feeder, "FcCloakedBunker", 1, 0, 300);
        for (var step = 0; step < world.Frames(2); step++) world.Step();
        Check.That(cloaked.HasStatus(ObjectStatusBits.Index("STEALTHED")) && !StagedCaptureRules.CanCapture(world, actor, cloaked), "A stealthed, undetected structure cannot be captured.");
    }

    /// <summary>Step 3, capture (<c>appearsToContainFriendlies</c>): a hidden stealth garrison whose apparent owner is neutral cannot be captured; detected, it can.</summary>
    public static void StealthGarrisonHiddenCapture()
    {
        var (world, feeder) = StealthGarrisonWorld();
        var actor = SgSpawn(world, feeder, "FcRifleman", 2, 200, 0);
        var hidden = SgSpawn(world, feeder, "FcBunker", 0, 300, 0);
        var sneak = SgInside(world, feeder, "FcSneak", 1, hidden);
        Check.That(!StagedCaptureRules.CanCapture(world, actor, hidden), "A hidden stealth garrison appears to contain friendlies: no capture.");
        sneak.Modules.OfType<StealthUpdate>().Single().MarkDetected(world, world.Frames(300));
        Check.That(StagedCaptureRules.CanCapture(world, actor, hidden), "Detected, held by stealth riders only, it can be captured.");
    }

    /// <summary>
    /// Step 4, the detector's building branch (<c>StealthDetectorUpdate::update</c>, <c>PartitionFilterStealthedOrStealthGarrisoned</c>): a detector of player 2 next to a hidden
    /// garrison marks its stealth rider and the building shows its owner; one out of range leaves it hidden. A rider inside an enclosing building is out of the partition: a detector
    /// that looks only for infantry (<c>ExtraRequiredKindOf</c>, on the building) does not find it - before, it found the rider itself.
    /// </summary>
    public static void StealthGarrisonDetector()
    {
        foreach (var (eye, distance, detected) in new[] { ("FcEye", 100, true), ("FcEye", 200, false), ("FcInfantryEye", 100, false) })
        {
            var (world, feeder) = StealthGarrisonWorld();
            var bunker = SgSpawn(world, feeder, "FcBunker", 0, 0, 0);
            var sneak = SgInside(world, feeder, "FcSneak", 1, bunker);
            for (var step = 0; step < world.Frames(2); step++) world.Step();
            var garrison = (GarrisonContain)bunker.Container!;
            Check.That(garrison.HidesGarrison(world), $"Setup ({eye} at {distance}): hidden.");
            SgSpawn(world, feeder, eye, 2, distance, 0);
            for (var step = 0; step < world.Frames(10); step++) world.Step();
            Check.That(sneak.HasStatus(ObjectStatusBits.Index("DETECTED")) == detected && garrison.HidesGarrison(world) == !detected,
                $"{eye} at {distance}: the rider {(detected ? "is" : "is not")} detected and the garrison {(detected ? "shows" : "hides")} its owner.");
        }
    }

    /// <summary>
    /// Step 3 d, entering (<c>ActionManager::canEnterObject</c>, <c>OpenContain::onCollide</c>): player 1's stealth rider holds a neutral building with room for one. Player 2's infantry is
    /// ordered in - the building looks empty, so the order is accepted although it is full -, and on arrival the stealth rider is detected and sent out and the rifleman takes the building.
    /// A building held by a plain rifleman of player 1 refuses the order.
    /// </summary>
    public static void StealthGarrisonEnter()
    {
        var (world, feeder) = StealthGarrisonWorld();
        var bunker = SgSpawn(world, feeder, "FcSmallBunker", 0, 0, 0);
        var sneak = SgInside(world, feeder, "FcSneak", 1, bunker);
        var rifleman = SgSpawn(world, feeder, "FcBoarder", 2, 120, 0);
        Check.That(bunker.Owner == new PlayerId(1) && bunker.Container!.ContainCount == 1, "Setup: the full building is player 1's.");
        Check.That(feeder.MoveAndEnter(2, rifleman.Id.Value, bunker.Id.Value).Accepted, "Player 2 may order its rifleman into the building held only by a stealth rider.");
        for (var step = 0; step < world.Frames(150) && !rifleman.ContainedBy.IsValid; step++) world.Step();
        Check.That(rifleman.ContainedBy == bunker.Id && !sneak.ContainedBy.IsValid && sneak.HasStatus(ObjectStatusBits.Index("DETECTED")) && bunker.Owner == new PlayerId(2),
            $"On arrival the stealth rider is detected and out, the rifleman in, the building player 2's (rifleman in {rifleman.ContainedBy}, owner {bunker.Owner}).");

        var (plainWorld, plainFeeder) = StealthGarrisonWorld();
        var plain = SgSpawn(plainWorld, plainFeeder, "FcBunker", 0, 0, 0);
        SgInside(plainWorld, plainFeeder, "FcRifleman", 1, plain);
        var other = SgSpawn(plainWorld, plainFeeder, "FcRifleman", 2, 120, 0);
        Check.That(!plainFeeder.MoveAndEnter(2, other.Id.Value, plain.Id.Value).Accepted, "A building held by a plain rifleman of player 1 refuses player 2's order.");
    }

    /// <summary>Step 5, <c>StealthUpdate::allowedToStealth</c>: a stealth rider keeps <c>STEALTHED</c> inside a garrison and loses it inside a transport.</summary>
    public static void StealthGarrisonNoStealthInTransport()
    {
        var stealthed = ObjectStatusBits.Index("STEALTHED");
        var (world, feeder) = StealthGarrisonWorld();
        var bunker = SgSpawn(world, feeder, "FcBunker", 1, 0, 0);
        var humvee = SgSpawn(world, feeder, "FcHumvee", 1, 300, 0);
        var inBunker = SgInside(world, feeder, "FcSneak", 1, bunker);
        var inHumvee = SgInside(world, feeder, "FcSneak", 1, humvee);
        for (var step = 0; step < world.Frames(5); step++) world.Step();
        Check.That(inBunker.HasStatus(stealthed), "Inside a garrison the stealth rider stays stealthed.");
        Check.That(!inHumvee.HasStatus(stealthed), "Inside a transport it is not.");
        feeder.ExitContainer(1, inHumvee.Id.Value);
        for (var step = 0; step < world.Frames(5); step++) world.Step();
        Check.That(!inHumvee.ContainedBy.IsValid && inHumvee.HasStatus(stealthed), "Out of the transport it stealths again.");
    }
}

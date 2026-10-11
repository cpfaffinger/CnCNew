using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>Tunnels (network capacity 2), infantry, an aircraft, a Humvee-like transport, a supply Chinook with a helicopter death, a Helix with a portable structure.</summary>
    private static IReadOnlyDictionary<string, string> TransportFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/GameData.ini"] = "GameData\n  MaxTunnelCapacity = 2\n  ValuePerSupplyBox = 100\n  Gravity = -64.0\nEnd\n",
        ["Data/INI/Upgrade.ini"] = """
        Upgrade Upgrade_AmericaSupplyLines
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End
        """,
        ["Data/INI/Object/TransportFixture.ini"] = """
        Locomotor WalkLoco
          Surfaces = GROUND
          Speed = 30
          Acceleration = 900
          Braking = 900
          TurnRate = 3600
        End
        Locomotor RotorLoco
          Surfaces = AIR
          Speed = 150
          Acceleration = 900
          Braking = 900
          TurnRate = 3600
          PreferredHeight = 100
        End

        Object Tunnel
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          BuildCost = 100
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = TunnelContain Tunnel01
            TimeForFullHeal = 1000
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Grunt
          KindOf = INFANTRY SELECTABLE
          TransportSlotCount = 1
          Locomotor = SET_NORMAL WalkLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Flyer
          KindOf = AIRCRAFT SELECTABLE
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Hauler4x4
          KindOf = VEHICLE SELECTABLE TRANSPORT
          Locomotor = SET_NORMAL WalkLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = TransportAIUpdate AI01
          End
          Behavior = TransportContain Transport01
            Slots = 2
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Rotorcraft
          KindOf = VEHICLE AIRCRAFT TRANSPORT HARVESTER
          Locomotor = SET_NORMAL RotorLoco
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = ChinookAIUpdate AI01
            MaxBoxes = 2
            SupplyCenterActionDelay = 0
            SupplyWarehouseActionDelay = 0
            SupplyWarehouseScanDistance = 700
            UpgradedSupplyBoost = 60
          End
          Behavior = TransportContain Transport01
            Slots = 4
          End
          Behavior = HelicopterSlowDeathBehavior Death01
            DestructionDelay = 99999999
            SpiralOrbitTurnRate = 140.0
            SpiralOrbitForwardSpeed = 0.0
            FallHowFast = 50%
            FXHitGround = FX_Test
            DelayFromGroundToFinalDeath = 300
            FinalRubbleObject = RotorHulk
          End
        End
        Object RotorHulk
          KindOf = STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        Object Depot
          KindOf = STRUCTURE COMMANDCENTER
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = SupplyCenterDockUpdate Dock01
            NumberApproachPositions = -1
          End
          Behavior = SupplyCenterCreate Create01
          End
        End
        Object Stockpile
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = SupplyWarehouseDockUpdate Dock01
            NumberApproachPositions = -1
            StartingBoxes = 20
          End
          Behavior = SupplyWarehouseCreate Create01
          End
        End
        Object Gunship
          KindOf = VEHICLE SELECTABLE
          Locomotor = SET_NORMAL WalkLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = HelixContain Helix01
            Slots = 2
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Turret
          KindOf = PORTABLE_STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static SimulationWorld TransportWorld(out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(TestRate.Default, files: TransportFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 11, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    /// <summary>
    /// A tunnel network is one shared contain list per player: a grunt that entered tunnel A leaves through tunnel B, scattered to a point 20..30 away
    /// (bounding circle 20); capacity is network-wide (MaxTunnelCapacity 2); aircraft and other players' objects are refused.
    /// </summary>
    public static void TunnelSharedNetwork()
    {
        var world = TransportWorld(out _, out var feeder);
        feeder.Spawn(1, "Tunnel", x: 0, y: 0);
        feeder.Spawn(1, "Tunnel", x: 500, y: 0);
        feeder.Spawn(1, "Grunt", x: 0, y: 0);
        feeder.Spawn(1, "Grunt", x: 0, y: 0);
        feeder.Spawn(1, "Grunt", x: 500, y: 0);
        feeder.Spawn(2, "Grunt", x: 0, y: 0);
        feeder.Spawn(1, "Flyer", x: 0, y: 0);
        world.Step();
        var a = At(world, "Tunnel", 0, 0);
        var b = At(world, "Tunnel", 500, 0);
        var grunts = world.Entities.Where(value => value.Template.Name == "Grunt" && value.Owner == new PlayerId(1)).OrderBy(value => value.Id.Value).ToArray();
        var enemy = world.Entities.Single(value => value.Template.Name == "Grunt" && value.Owner == new PlayerId(2));
        var network = world.TunnelSystemOf(new PlayerId(1));
        Check.That(network.TunnelCount == 2 && network.Tunnels.SequenceEqual([a.Id, b.Id]), "Both tunnels register in creation order.");
        Check.That(feeder.EnterContainer(1, grunts[0].Id.Value, a.Id.Value).Accepted && feeder.EnterContainer(1, grunts[1].Id.Value, a.Id.Value).Accepted, "Two grunts enter tunnel A.");
        Check.That(feeder.EnterContainer(2, enemy.Id.Value, a.Id.Value).Reason == CommandRejection.CannotEnterContainer, "Another player's unit cannot use the network.");
        Check.That(feeder.EnterContainer(1, Get(world, "Flyer").Id.Value, a.Id.Value).Reason == CommandRejection.CannotEnterContainer, "Aircraft cannot use tunnels.");
        world.Step();
        Check.That(network.ContainCount == 2 && grunts[0].ContainedBy == a.Id && b.Container!.ContainCount == 2, "Both tunnels see the shared contain list.");
        Check.That(feeder.EnterContainer(1, grunts[2].Id.Value, b.Id.Value).Reason == CommandRejection.CannotEnterContainer, "The network is full at MaxTunnelCapacity, whichever tunnel.");
        Check.That(feeder.Evacuate(1, b.Id.Value).Accepted, "The owner evacuates tunnel B.");
        var result = world.Step();
        Check.That(result.Events.Count(value => value.Kind == SimEventKind.EntityUncontained && value.Other == b.Id) == 2, "Both leave through tunnel B.");
        foreach (var grunt in grunts.Take(2))
        {
            Check.That(!grunt.ContainedBy.IsValid && network.ContainCount == 0, "The network is empty.");
            var dx = grunt.MoveTargetX - b.X;
            var dy = grunt.MoveTargetY - b.Y;
            var distance = Fixed.Sqrt(dx * dx + dy * dy);
            Check.That(grunt.HasMoveTarget && distance >= Fixed.FromInt(20) - Fixed.FromDecimal(0.01m) && distance <= Fixed.FromInt(30) + Fixed.FromDecimal(0.01m),
                $"The grunt walks out to a point 20..30 from tunnel B (got {distance}).");
        }
    }

    /// <summary>
    /// Healing: TimeForFullHeal 1000 ms = 30 ticks, a sliver of 100/30 per tunnel per tick (two tunnels heal twice, as in the reference), full health once
    /// inside for 30 ticks. A dying tunnel hands its riders to the remaining one; the last tunnel caves in and destroys everybody inside.
    /// </summary>
    public static void TunnelHealingAndCaveIn()
    {
        var world = TransportWorld(out _, out var feeder);
        feeder.Spawn(1, "Tunnel", x: 0, y: 0);
        feeder.Spawn(1, "Tunnel", x: 500, y: 0);
        feeder.Spawn(1, "Grunt", x: 0, y: 0);
        world.Step();
        var a = At(world, "Tunnel", 0, 0);
        var b = At(world, "Tunnel", 500, 0);
        var grunt = Get(world, "Grunt");
        feeder.Damage(grunt.Id.Value, 60);
        world.Step();
        feeder.EnterContainer(1, grunt.Id.Value, a.Id.Value);
        world.Step();
        // A sliver is MaxHealth / TimeForFullHeal per original frame (30 frames here), at every tick rate, and a rider gets its first in the tick it enters.
        var sliver = Fixed.FromInt(100) / Fixed.FromInt(30);
        Check.That(grunt.Body!.Health == Fixed.FromInt(40) + sliver + sliver, $"Two tunnels heal two slivers in the entry tick (got {grunt.Body.Health}).");
        for (var tick = 0; tick < world.Frames(5); tick++) world.Step();
        Check.That(grunt.Body.Health < Fixed.FromInt(100), "Not yet whole after six frames.");
        for (var tick = 0; tick < world.Frames(25); tick++) world.Step();
        Check.That(grunt.Body.Health == Fixed.FromInt(100), "Whole after TimeForFullHeal.");

        feeder.Kill(a.Id.Value);
        world.Step();
        world.Step();
        var network = world.TunnelSystemOf(new PlayerId(1));
        Check.That(network.TunnelCount == 1 && grunt.ContainedBy == b.Id && network.Contains(grunt.Id), "The rider now belongs to the remaining tunnel.");
        feeder.Kill(b.Id.Value);
        world.Step();
        world.Step();
        Check.That(world.FindEntity(grunt.Id) is null && network.ContainCount == 0 && network.TunnelCount == 0, "The last tunnel caves in on its rider.");
    }

    /// <summary>Selling the last tunnel lets the riders out first (<c>onSelling</c>), instead of the cave-in.</summary>
    public static void TunnelSaleEvacuates()
    {
        var world = TransportWorld(out _, out var feeder);
        feeder.Spawn(1, "Tunnel", x: 0, y: 0);
        feeder.Spawn(1, "Grunt", x: 0, y: 0);
        world.Step();
        var tunnel = Get(world, "Tunnel");
        var grunt = Get(world, "Grunt");
        feeder.EnterContainer(1, grunt.Id.Value, tunnel.Id.Value);
        world.Step();
        Check.That(world.Submit(new SellCommand(new PlayerId(1), 100, world.Tick, tunnel.Id)).Accepted, "The tunnel can be sold.");
        for (var tick = 0; tick < world.Frames(200) && world.FindEntity(tunnel.Id) is not null; tick++) world.Step();
        Check.That(world.FindEntity(tunnel.Id) is null, "The sale completes.");
        Check.That(world.FindEntity(grunt.Id) is { } survivor && !survivor.ContainedBy.IsValid, "The rider walked out of the sold tunnel.");
    }

    /// <summary>MoveAndEnter walks a grunt into a transport; an evacuation order with a position drives there first and unloads on arrival.</summary>
    public static void TransportLoadAndUnloadAtPosition()
    {
        var world = TransportWorld(out _, out var feeder);
        feeder.Spawn(1, "Hauler4x4", x: 0, y: 0);
        feeder.Spawn(1, "Grunt", x: 100, y: 0);
        world.Step();
        var truck = Get(world, "Hauler4x4");
        var grunt = Get(world, "Grunt");
        Check.That(feeder.MoveAndEnter(1, grunt.Id.Value, truck.Id.Value).Accepted, "The grunt is ordered in.");
        Check.That(feeder.MoveAndEnter(1, truck.Id.Value, grunt.Id.Value).Reason == CommandRejection.UnknownEntity, "A grunt is no container.");
        for (var tick = 0; tick < world.Frames(200) && !grunt.ContainedBy.IsValid; tick++) world.Step();
        Check.That(grunt.ContainedBy == truck.Id && world.Tick > world.Frames(90), $"The grunt walked about 100 units and got in (tick {world.Tick}).");
        Check.That(feeder.Evacuate(1, truck.Id.Value, x: 200, y: 0).Accepted, "The owner orders an evacuation at (200, 0).");
        world.Step();
        Check.That(grunt.ContainedBy == truck.Id, "Nobody leaves while the truck drives.");
        for (var tick = 0; tick < world.Frames(300) && grunt.ContainedBy.IsValid; tick++) world.Step();
        // OpenContain::exitObjectViaDoor: the rider is put out and walks to the nearest place it can stand on - beside the truck, not inside it.
        Check.That(!grunt.ContainedBy.IsValid && truck.X == Fixed.FromInt(200), "The grunt gets out at the destination.");
        for (var tick = 0; tick < world.Frames(90) && grunt.HasMoveTarget; tick++) world.Step();
        var apartX = grunt.X - truck.X;
        var apartY = grunt.Y - truck.Y;
        var touching = truck.Template.BoundingCircleRadius + grunt.Template.BoundingCircleRadius;
        Check.That(!grunt.HasMoveTarget && apartX * apartX + apartY * apartY > touching * touching, $"The grunt stands beside the truck, not inside it ({grunt.X}, {grunt.Y}).");
        Check.That(feeder.Evacuate(2, truck.Id.Value).Reason == CommandRejection.NotPermitted, "Only the owner may evacuate.");
    }

    /// <summary>
    /// The Chinook climbs to PreferredHeight 100 at one locomotor speed (5) per tick, ferries supplies at that height (two boxes of 100 per trip, 60 more
    /// with Upgrade_AmericaSupplyLines) and lands before its passengers leave.
    /// </summary>
    public static void ChinookSupplyFlightAndLanding()
    {
        var world = TransportWorld(out _, out var feeder);
        feeder.Spawn(1, "Depot", x: 0, y: 0);
        feeder.Spawn(0, "Stockpile", owner: 0, x: 300, y: 0);
        feeder.Spawn(1, "Rotorcraft", x: 0, y: 0);
        // The Chinook's first logic frame begins one frame after it appeared (one tick at 30 Hz).
        for (var tick = 0; tick < world.Frames(1); tick++) world.Step();
        var chinook = Get(world, "Rotorcraft");
        for (var tick = 0; tick < world.Frames(19); tick++) world.Step();
        Check.That(chinook.Z == Fixed.FromInt(95), $"Climbing at 5 per tick (got {chinook.Z}).");
        for (var wait = 0; wait < world.Frames(1); wait++) world.Step();
        Check.That(chinook.Z == Fixed.FromInt(100), "The Chinook reaches its preferred height after 20 ticks.");

        feeder.Dock(1, chinook.Id.Value, Get(world, "Stockpile").Id.Value);
        var deposits = new List<long>();
        var bought = false;
        var ai = chinook.Modules.OfType<AIUpdateInterface>().First();
        var insideTicks = 0;
        for (var tick = 0; tick < world.Frames(2000) && deposits.Count < 3; tick++)
        {
            foreach (var simEvent in world.Step().Events.Where(value => value.Kind == SimEventKind.SupplyDeposited)) deposits.Add(simEvent.Value);
            if (deposits.Count == 1 && !bought)
            {
                feeder.GiveMoney(1, 100);
                world.Step();
                bought = feeder.PurchaseUpgrade(1, "Upgrade_AmericaSupplyLines").Accepted;
            }
            Check.That(chinook.Z == Fixed.FromInt(100), "The Chinook supplies in flight.");
            // The net animation (DOCKING) belongs to the time at the dock, not to the flight there (demo finding: unloading was shown while the Chinook was still on its way).
            Check.That(!(ai.IsInsideDock && chinook.HasMoveTarget), "The Chinook does not show the dock state while it is still flying to the dock.");
            if (ai.IsInsideDock) insideTicks++;
        }
        Check.That(insideTicks > 0, $"The Chinook is inside the dock while it unloads ({insideTicks} ticks).");
        Check.That(deposits.Count == 3 && deposits[0] == 200 && deposits[2] == 260, $"Two boxes pay 200, with Supply Lines 260 (got {string.Join(", ", deposits)}).");

        var grunt = new PlayerId(1);
        // The orders below follow the third deposit by whole logic frames (one step each at 30 Hz), so that they meet the supply machine in the same state at every rate.
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        feeder.Spawn(1, "Grunt", x: 0, y: 0);
        for (var tick = 0; tick < world.Frames(1); tick++) world.Step();
        var passenger = world.Entities.Single(value => value.Template.Name == "Grunt");
        Check.That(feeder.EnterContainer(1, passenger.Id.Value, chinook.Id.Value).Accepted, "A grunt boards the Chinook.");
        // Boarding is the arrival of a move: it takes the logic frame of the order (one tick at 30 Hz).
        for (var tick = 0; tick < world.Frames(1); tick++) world.Step();
        Check.That(passenger.ContainedBy == chinook.Id, "The grunt is aboard one logic frame after the order.");
        Check.That(feeder.Evacuate(1, chinook.Id.Value).Accepted, "Evacuation is ordered in the air.");
        long landedAt = -1;
        for (var tick = 0; tick < world.Frames(200) && passenger.ContainedBy.IsValid; tick++)
        {
            world.Step();
            if (landedAt < 0 && chinook.Z == world.GroundHeightAt(chinook.X, chinook.Y)) landedAt = world.Tick;
            if (passenger.ContainedBy.IsValid) Check.That(chinook.Z > Fixed.Zero || landedAt >= 0, "Nobody leaves before the Chinook is down.");
        }
        Check.That(!passenger.ContainedBy.IsValid && landedAt >= 0, "The grunt leaves the landed Chinook.");
        _ = grunt;
        for (var tick = 0; tick < world.Frames(25); tick++) world.Step();
        Check.That(chinook.Z == Fixed.FromInt(100), "Unloaded, the Chinook climbs back to its height.");
    }

    /// <summary>
    /// HelicopterSlowDeathBehavior, hand-computed: gravity -64/s² is -4660 raw per tick², half of it (FallHowFast 50 %) -2330 raw; after n updates the fall
    /// is 2330 · n(n+1)/2 raw, which first reaches the 99 units down to one unit above the ground at n = 75. DelayFromGroundToFinalDeath 300 ms is
    /// 9 frames; the tenth frame after the impact blows it up, leaves the RotorHulk and removes the Chinook. The fall is stepped once per original frame at
    /// every tick rate (the first step one frame after the death), so both expectations are written in frames.
    /// </summary>
    public static void HelicopterSlowDeathFall()
    {
        var world = TransportWorld(out _, out var feeder);
        feeder.Spawn(1, "Rotorcraft", x: 0, y: 0);
        // The Chinook's first logic frame begins one frame after it appeared (one tick at 30 Hz).
        for (var tick = 0; tick < world.Frames(1); tick++) world.Step();
        var chinook = Get(world, "Rotorcraft");
        for (var tick = 0; tick < world.Frames(20); tick++) world.Step();
        Check.That(chinook.Z == Fixed.FromInt(100), "Setup: the Chinook flies at 100.");
        var death = world.Tick;
        feeder.Kill(chinook.Id.Value);
        world.Step();
        Check.That(chinook.EffectivelyDead && world.FindEntity(chinook.Id) is not null, "The Chinook is dead but falls instead of vanishing.");
        long hit = -1;
        for (var tick = 0; tick < world.Frames(200) && hit < 0; tick++)
        {
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.FxRequested && value.Text == "FX_Test" && value.HasPosition)) hit = result.Tick;
        }
        Check.That(hit == death + world.Frames(75), $"It hits the ground 75 updates after the death (got {hit - death}).");
        long gone = -1;
        for (var tick = 0; tick < world.Frames(30) && gone < 0; tick++)
        {
            world.Step();
            if (world.FindEntity(chinook.Id) is null) gone = world.Tick - 1;
        }
        Check.That(gone == hit + world.Frames(10), $"The final blow-up is the tenth frame after the impact (got {world.InFrames(gone - hit)} frames, {gone - hit} ticks).");
        Check.That(world.Entities.Any(value => value.Template.Name == "RotorHulk"), "The rubble hull remains.");
    }

    /// <summary>HelixContain: the portable structure rides for free (no slot), follows the Helix and dies with it.</summary>
    public static void HelixPortableStructure()
    {
        var world = TransportWorld(out _, out var feeder);
        feeder.Spawn(1, "Gunship", x: 0, y: 0);
        feeder.Spawn(1, "Turret", x: 0, y: 0);
        feeder.Spawn(1, "Grunt", x: 0, y: 0);
        world.Step();
        var helix = Get(world, "Gunship");
        var turret = Get(world, "Turret");
        Check.That(feeder.EnterContainer(1, turret.Id.Value, helix.Id.Value).Accepted, "The portable structure mounts.");
        world.Step();
        var contain = helix.Modules.OfType<HelixContain>().Single();
        Check.That(contain.PortableStructure == turret.Id && contain.ContainCount == 0 && turret.ContainedBy == helix.Id, "It takes no slot.");
        Check.That(feeder.EnterContainer(1, Get(world, "Grunt").Id.Value, helix.Id.Value).Accepted, "Passengers still fit.");
        feeder.SetMoveTarget(1, helix.Id.Value, 60, 0);
        for (var tick = 0; tick < world.Frames(100); tick++) world.Step();
        Check.That(helix.X == Fixed.FromInt(60) && turret.X == helix.X && turret.Y == helix.Y, "The portable structure follows the Helix.");
        feeder.Kill(helix.Id.Value);
        world.Step();
        Check.That(turret.EffectivelyDead, "It dies with the Helix.");
    }

    /// <summary>Tunnels, transports and a flying Chinook survive a snapshot (v7); the new commands round-trip through replay v7, v6 is refused.</summary>
    public static void TransportSnapshotAndReplay()
    {
        List<ulong> Run(int snapshotAt)
        {
            var world = TransportWorld(out var fixture, out var feeder);
            feeder.Spawn(1, "Tunnel", x: 0, y: 0);
            feeder.Spawn(1, "Tunnel", x: 400, y: 0);
            feeder.Spawn(1, "Grunt", x: 50, y: 0);
            feeder.Spawn(1, "Grunt", x: 60, y: 0);
            feeder.Spawn(1, "Hauler4x4", x: 0, y: 100);
            feeder.Spawn(1, "Rotorcraft", x: 0, y: 200);
            world.Step();
            var grunts = world.Entities.Where(value => value.Template.Name == "Grunt").OrderBy(value => value.Id.Value).ToArray();
            feeder.MoveAndEnter(1, grunts[0].Id.Value, At(world, "Tunnel", 0, 0).Id.Value);
            feeder.MoveAndEnter(1, grunts[1].Id.Value, Get(world, "Hauler4x4").Id.Value);
            var hashes = new List<ulong>();
            for (var tick = 0; tick < world.Frames(240); tick++)
            {
                if (tick == world.Frames(120))
                {
                    feeder.Evacuate(1, At(world, "Tunnel", 400, 0).Id.Value);
                    feeder.Evacuate(1, Get(world, "Hauler4x4").Id.Value, x: 100, y: 100);
                }
                if (tick == snapshotAt)
                {
                    world = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                    feeder = feeder.ForkTo(world);
                }
                hashes.Add(world.Step().StateHash);
            }
            return hashes;
        }
        var first = Run(-1);
        Check.That(first.SequenceEqual(Run(-1)), "Two fresh runs are identical.");
        Check.That(first.SequenceEqual(Run(TestRate.Frames(100))), "A snapshot with riders inside continues identically.");
        Check.That(first.SequenceEqual(Run(TestRate.Frames(130))), "A snapshot during the evacuation continues identically.");

        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, 30, new string('A', 64), "fixture", 1, new WorldId(1), CombatPlayers, 10, 5);
        ImmutableArray<SimCommand> commands =
        [
            new MoveAndEnterCommand(new PlayerId(1), 1, 3, new EntityId(4), new EntityId(5)),
            new EvacuateCommand(new PlayerId(1), 2, 4, new EntityId(5), true, Fixed.FromInt(12), Fixed.FromDecimal(-3.5m)),
            new EvacuateCommand(new PlayerId(1), 3, 4, new EntityId(6), false, Fixed.Zero, Fixed.Zero),
        ];
        var bytes = ReplayFormat.Write(new ReplayFile(header, commands, []));
        var read = ReplayFormat.Read(bytes);
        Check.That(read.Commands.SequenceEqual(commands) && ReplayFormat.CurrentVersion == 14 && CommandTypes.Version == 16, "MoveAndEnter and Evacuate round-trip in replay v13 (command table 16).");
        var old = (byte[])bytes.Clone();
        BitConverter.GetBytes(6u).CopyTo(old, 8);
        Check.Throws<ReplayFormatException>(() => ReplayFormat.Read(old), "Unsupported replay format version 6");
        Check.That(WorldSnapshot.Version == 10, "Snapshots are version 10.");
    }
}
